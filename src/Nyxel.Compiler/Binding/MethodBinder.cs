using System.Collections.Immutable;
using Nyxel.Compiler.Diagnostics;
using Nyxel.Compiler.Symbols;
using Nyxel.Compiler.Syntax;

namespace Nyxel.Compiler.Binding;

/// <summary>
/// Pass 5 of docs/design/semantic-analysis.md: binds one function body (or one field's initial value) into a bound
/// tree. Bodies are independent of each other, because every member's type is written in its signature (ADR-0006).
/// </summary>
internal sealed partial class MethodBinder
{
    private readonly BinderContext _context;
    private readonly ImportScope _scope;
    private readonly SourceNamedTypeSymbol _containingType;
    private readonly SourceMethodSymbol? _method;

    // Scopes from the function's (its parameters) to the innermost block. A local is visible from its declaration to
    // the end of its block, and no local or parameter may take a name that is visible at that point (ADR-0036).
    private readonly List<Dictionary<string, Symbol>> _scopes = [];
    private int _loopDepth;

    public MethodBinder(BinderContext context, ImportScope scope, SourceNamedTypeSymbol containingType, SourceMethodSymbol? method)
    {
        _context = context;
        _scope = scope;
        _containingType = containingType;
        _method = method;
        _scopes.Add([]);
        foreach (var parameter in method?.Parameters ?? [])
        {
            _scopes[0].TryAdd(parameter.Name, parameter);
        }
    }

    private SyntaxTree Tree => _scope.Tree;

    /// <summary>Functions with <c>self</c> and <c>init</c>; not functions without <c>self</c> or field initial values.</summary>
    private bool HasSelf => _method is { IsStatic: false };

    public BoundBlock BindBody()
    {
        var method = _method ?? throw new InvalidOperationException("Not a function.");
        var (body, expressionBody) = method.Syntax switch
        {
            FunctionDeclarationSyntax function => (function.Body, function.ExpressionBody),
            AccessorDeclarationSyntax accessor => (accessor.Body, accessor.ExpressionBody),
            InitDeclarationSyntax init => (init.Body, null),
            _ => (null, null),
        };
        if (body is not null)
        {
            return BindBlock(body);
        }
        if (expressionBody is not null)
        {
            // func F() -> int = value (and get = value) returns the value; without a return type the expression is a statement.
            BoundStatement statement = method.ReturnsVoid
                ? BindExpressionStatement(expressionBody, expressionBody.Expression)
                : new BoundReturn(expressionBody, Convert(BindValue(expressionBody.Expression, method.ReturnType), method.ReturnType));
            return new BoundBlock(expressionBody, [statement]);
        }
        return new BoundBlock(method.Syntax ?? _containingType.Syntax, []);
    }

    /// <summary>The initial value of a field or an auto property, converted to its type.</summary>
    public BoundExpression BindInitializer(EqualsValueClauseSyntax initializer, TypeSymbol type) =>
        Convert(BindValue(initializer.Value, type), type);

    // Scopes ---------------------------------------------------------------------------------------------------

    private void PushScope() => _scopes.Add([]);

    private void PopScope() => _scopes.RemoveAt(_scopes.Count - 1);

    private Symbol? LookupLocal(string name)
    {
        for (var i = _scopes.Count - 1; i >= 0; i--)
        {
            if (_scopes[i].TryGetValue(name, out var symbol))
            {
                return symbol;
            }
        }
        return null;
    }

    private LocalSymbol DeclareLocal(SyntaxToken identifier, TypeSymbol type, bool isReadOnly)
    {
        var local = new LocalSymbol(identifier.Text, type, isReadOnly, identifier);
        if (identifier.IsMissing)
        {
            return local;
        }
        if (LookupLocal(identifier.Text) is { } existing)
        {
            var position = existing switch
            {
                LocalSymbol other => other.Identifier.Position,
                SourceParameterSymbol parameter => parameter.Identifier.Position,
                _ => identifier.Position,
            };
            Report(DiagnosticDescriptors.NameAlreadyDeclared, identifier, identifier.Text, Tree.Text.GetLinePosition(position).Line + 1);
        }
        _scopes[^1][identifier.Text] = local;
        return local;
    }

    // Values and conversions ------------------------------------------------------------------------------------

    /// <summary>
    /// An expression that must be a value: not a type, namespace or function name. A call of a function that returns
    /// nothing is a value only as a statement of its own.
    /// </summary>
    private BoundExpression BindValue(ExpressionSyntax syntax, TypeSymbol? expected = null, bool allowVoid = false)
    {
        var bound = BindExpression(syntax, expected);
        switch (bound)
        {
            case BoundTypeExpression type:
                if (!type.Type.IsError)
                {
                    Report(DiagnosticDescriptors.TypeIsNotValue, syntax, SymbolDisplay.FormatType(type.Type));
                }
                return Bad(syntax);
            case BoundNamespaceExpression ns:
                Report(DiagnosticDescriptors.NamespaceIsNotValue, syntax, ns.Namespace.FullName);
                return Bad(syntax);
            case BoundMethodGroup group:
                Report(DiagnosticDescriptors.FunctionIsNotValue, syntax, group.Name);
                return Bad(syntax);
            case BoundPropertyAccess property when !property.Property.HasGetter
                || !IsAccessibleHere(property.Property.GetterAccessibility, property.Property.ContainingType):
                Report(DiagnosticDescriptors.PropertyHasNoGetter, syntax, SymbolDisplay.Format(property.Property));
                return Bad(syntax);
            case { Type.IsVoid: true } when !allowVoid:
                ReportNoValue(bound);
                return Bad(syntax);
            default:
                return bound;
        }
    }

    private void ReportNoValue(BoundExpression expression)
    {
        if (expression is BoundCall or BoundConditionalAccess { Access: BoundCall })
        {
            var call = expression as BoundCall ?? (BoundCall)((BoundConditionalAccess)expression).Access;
            Report(DiagnosticDescriptors.NoValue, expression.Syntax, SymbolDisplay.Format(call.Method));
        }
    }

    /// <summary>The expression as a value of <paramref name="target"/>, through an implicit conversion if needed.</summary>
    private BoundExpression Convert(BoundExpression expression, TypeSymbol target, SyntaxNode? syntax = null)
    {
        if (expression.HasErrors || target.IsError)
        {
            return expression;
        }
        var kind = Conversions.ClassifyImplicit(expression.Type, target);
        if (kind == ConversionKind.Identity)
        {
            return expression;
        }
        if (kind is { } conversion)
        {
            if (ReportIntegerDivisionAsDecimal(expression, target))
            {
                return Bad(syntax ?? expression.Syntax, expression);
            }
            return MakeConversion(syntax ?? expression.Syntax, expression, conversion, target, isExplicit: false);
        }
        var location = syntax ?? expression.Syntax;
        if (expression.Type is NullTypeSymbol)
        {
            Report(DiagnosticDescriptors.NotNullable, location, SymbolDisplay.FormatType(target));
        }
        else if (expression.Type.IsNullable && Conversions.ClassifyImplicit(expression.Type.NonNullable, target) is not null)
        {
            ReportMaybeNullConversion(expression, target, location);
        }
        else
        {
            Report(DiagnosticDescriptors.CannotConvert, location, SymbolDisplay.FormatType(expression.Type), SymbolDisplay.FormatType(target));
        }
        return Bad(location, expression);
    }

    // Diagnostics ----------------------------------------------------------------------------------------------

    private void Report(DiagnosticDescriptor descriptor, SyntaxNode node, params object[] args) => _context.Report(descriptor, Tree, node, args);

    private static BoundBadExpression Bad(SyntaxNode syntax, params BoundNode[] children) => new(syntax, [.. children]);

    private BoundBadExpression NotSupported(SyntaxNode syntax, string what)
    {
        Report(DiagnosticDescriptors.NotSupportedYet, syntax, what);
        return Bad(syntax);
    }

    private static string Describe(SyntaxNode syntax) => syntax.Kind switch
    {
        SyntaxKind.NullLiteralExpression => "'null'",
        SyntaxKind.LambdaExpression => "A lambda",
        SyntaxKind.MatchExpression => "'match'",
        SyntaxKind.IfExpression => "An 'if' that gives a value",
        SyntaxKind.TryExpression or SyntaxKind.TryStatement => "'try'",
        SyntaxKind.EvalExpression => "'eval'",
        SyntaxKind.TupleExpression => "A tuple",
        SyntaxKind.CollectionExpression => "A collection literal",
        SyntaxKind.IsExpression => "'is'",
        SyntaxKind.CoalesceExpression => "'??'",
        SyntaxKind.ConditionalMemberAccessExpression => "'?.'",
        SyntaxKind.SuperExpression => "'super'",
        SyntaxKind.AwaitExpression or SyntaxKind.AwaitAllExpression or SyntaxKind.AwaitAnyExpression => "'await'",
        SyntaxKind.LaunchExpression => "'launch'",
        SyntaxKind.RangeExpression => "A range outside 'for'",
        SyntaxKind.ReturnExpression or SyntaxKind.ThrowExpression or SyntaxKind.BreakExpression or SyntaxKind.ContinueExpression
            or SyntaxKind.GiveExpression => "A jump inside an expression",
        SyntaxKind.ThrowStatement => "'throw'",
        SyntaxKind.UsingDeclarationStatement => "'using'",
        SyntaxKind.DeconstructionDeclarationStatement => "Deconstruction",
        SyntaxKind.GiveStatement => "'give'",
        SyntaxKind.EmitStatement => "'emit'",
        SyntaxKind.InitStatement => "'init self(...)' and 'init super(...)'",
        _ => $"'{syntax}'",
    };

}
