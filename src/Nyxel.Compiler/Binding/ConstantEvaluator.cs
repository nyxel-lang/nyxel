using Nyxel.Compiler.Diagnostics;
using Nyxel.Compiler.Symbols;
using Nyxel.Compiler.Syntax;

namespace Nyxel.Compiler.Binding;

/// <summary>
/// The values of constants, worked out the first time they are asked for (ADR-0038): a <c>static let</c> is a
/// constant when its initial value folds to one, and an enum case's value is the one written or its position. Both
/// take binding an expression that may name other constants, in any order, so the initial values bound here are kept
/// and the body pass uses them: each is bound, and reports its problems, once.
/// </summary>
internal sealed class ConstantEvaluator(BinderContext context) : IConstantValues
{
    private readonly Dictionary<Symbol, BoundExpression> _initializers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SourceEnumCaseSymbol, object?> _caseValues = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Symbol> _inProgress = new(ReferenceEqualityComparer.Instance);

    // Cases without a value: their problem is reported, so the cases that name them don't report again.
    private readonly HashSet<SourceEnumCaseSymbol> _failed = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// The initial value of a field or auto property, converted to its type; null when it has none, or when it is
    /// being bound already and names itself through constants.
    /// </summary>
    public BoundExpression? Initializer(Symbol member)
    {
        if (_initializers.TryGetValue(member, out var bound))
        {
            return bound;
        }
        var (type, initializer, memberType) = member switch
        {
            SourceFieldSymbol field => ((SourceNamedTypeSymbol)field.ContainingType, field.Syntax.Initializer, field.Type),
            SourcePropertySymbol property => ((SourceNamedTypeSymbol)property.ContainingType, property.Syntax.Initializer, property.Type),
            _ => default,
        };
        // A static let whose value comes back to itself through other constants isn't one: C#'s static readonly
        // fields that read each other run in order, and so do Nyxel's.
        if (initializer is null || !_inProgress.Add(member))
        {
            return null;
        }
        try
        {
            bound = Binder(type).BindInitializer(initializer, memberType);
        }
        finally
        {
            _inProgress.Remove(member);
        }
        _initializers[member] = bound;
        return bound;
    }

    public object? ValueOf(SourceFieldSymbol field) => Initializer(field)?.ConstantValue;

    public object? ValueOf(SourceEnumCaseSymbol enumCase)
    {
        if (_caseValues.TryGetValue(enumCase, out var value))
        {
            return value;
        }
        if (!_inProgress.Add(enumCase))
        {
            // A case's value must be a constant, so one that comes back to itself has none.
            if (_failed.Add(enumCase))
            {
                Report(enumCase.ContainingType, DiagnosticDescriptors.ConstantDependsOnItself, enumCase.Syntax.Identifier, $"{enumCase.ContainingType.Name}.{enumCase.Name}");
            }
            return null;
        }
        try
        {
            value = CaseValue(enumCase);
        }
        finally
        {
            _inProgress.Remove(enumCase);
        }
        if (value is null)
        {
            _failed.Add(enumCase);
        }
        _caseValues[enumCase] = value;
        return value;
    }

    /// <summary>
    /// Why an expression that must be a constant isn't one, for NYX2082: it names a <c>static let</c> that is worked
    /// out when the program starts, or it is some other expression.
    /// </summary>
    public static string NotConstantReason(BoundExpression expression)
    {
        var field = expression.DescendantsAndSelf().OfType<BoundFieldAccess>()
            .FirstOrDefault(f => f.Field is SourceFieldSymbol { IsStatic: true, IsReadOnly: true, IsConst: false });
        return field is not null
            ? $"'{SymbolDisplay.Format(field.Field)}' is a 'static let' whose initial value isn't a constant expression, so it is worked out when the program starts"
            : "write a literal, an enum case, a 'static let' with a constant value, or operations on these";
    }

    /// <summary>
    /// The value written for a case, or its position among the cases (from 0, as in C#). The value is a whole-number
    /// constant, or a constant of the enum itself (<c>Layer.Ground | Layer.Water</c>), that fits the underlying type
    /// (ADR-0024): as for C#'s enums, <c>case A = 1 &lt;&lt; 3</c> fits a <c>byte</c> enum although <c>1 &lt;&lt; 3</c> is an <c>int</c>.
    /// </summary>
    private object? CaseValue(SourceEnumCaseSymbol enumCase)
    {
        var type = enumCase.ContainingType;
        var underlying = type.EnumUnderlyingType ?? context.Int;
        if (enumCase.Syntax.EqualsValue is not { } equalsValue)
        {
            if (!ConstantFolder.Fits(enumCase.Ordinal, underlying.SpecialType))
            {
                Report(type, DiagnosticDescriptors.ConstantOutOfRange, enumCase.Syntax.Identifier, enumCase.Ordinal, SymbolDisplay.FormatType(underlying));
                return null;
            }
            return ConstantFolder.ToIntegral(enumCase.Ordinal, underlying.SpecialType);
        }
        var bound = Binder(type).BindConstantExpression(equalsValue.Value, underlying);
        if (bound.HasErrors)
        {
            return null;
        }
        if (!ReferenceEquals(bound.Type, type) && !Conversions.IsWholeNumber(bound.Type.SpecialType))
        {
            Report(type, DiagnosticDescriptors.CannotConvert, equalsValue.Value, SymbolDisplay.FormatType(bound.Type), SymbolDisplay.FormatType(underlying));
            return null;
        }
        if (ConstantFolder.AsInteger(bound.ConstantValue) is not { } integer)
        {
            if (!NamesFailedCase(bound))
            {
                Report(type, DiagnosticDescriptors.NotConstant, equalsValue.Value, "The value of a case", NotConstantReason(bound));
            }
            return null;
        }
        if (!ConstantFolder.Fits(integer, underlying.SpecialType))
        {
            Report(type, DiagnosticDescriptors.ConstantOutOfRange, equalsValue.Value, integer, SymbolDisplay.FormatType(underlying));
            return null;
        }
        return ConstantFolder.ToIntegral(integer, underlying.SpecialType);
    }

    private bool NamesFailedCase(BoundExpression expression) =>
        expression.DescendantsAndSelf().OfType<BoundFieldAccess>().Any(f => f.Field is SourceEnumCaseSymbol c && _failed.Contains(c));

    private MethodBinder Binder(SourceNamedTypeSymbol type) => new(context, context.Scopes[type.SyntaxTree], type, null);

    private void Report(SourceNamedTypeSymbol type, DiagnosticDescriptor descriptor, SyntaxNode node, params object[] args) =>
        context.Report(descriptor, type.SyntaxTree, node, args);
}
