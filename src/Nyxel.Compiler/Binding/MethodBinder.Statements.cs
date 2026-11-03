using System.Collections.Immutable;
using Nyxel.Compiler.Diagnostics;
using Nyxel.Compiler.Symbols;
using Nyxel.Compiler.Syntax;
using SpecialType = Microsoft.CodeAnalysis.SpecialType;

namespace Nyxel.Compiler.Binding;

internal sealed partial class MethodBinder
{
    private BoundBlock BindBlock(BlockSyntax syntax)
    {
        PushScope();
        var statements = syntax.Statements.Select(BindStatement).ToImmutableArray();
        PopScope();
        return new BoundBlock(syntax, statements);
    }

    private BoundStatement BindStatement(StatementSyntax syntax) => syntax switch
    {
        BlockSyntax block => BindBlock(block),
        LocalDeclarationStatementSyntax declaration => BindLocalDeclaration(declaration),
        ExpressionStatementSyntax statement => BindExpressionStatement(statement, statement.Expression),
        AssignmentStatementSyntax assignment => BindAssignment(assignment),
        IfStatementSyntax ifStatement => BindIf(ifStatement),
        WhileStatementSyntax whileStatement => BindWhile(whileStatement),
        ForStatementSyntax forStatement => BindFor(forStatement),
        ReturnStatementSyntax returnStatement => BindReturn(returnStatement),
        JumpStatementSyntax jump => BindJump(jump),
        _ => new BoundExpressionStatement(syntax, NotSupported(FirstToken(syntax), Describe(syntax))),
    };

    private static SyntaxNode FirstToken(SyntaxNode syntax) => syntax.GetFirstToken();

    private BoundStatement BindLocalDeclaration(LocalDeclarationStatementSyntax syntax)
    {
        var isReadOnly = syntax.Keyword.Kind == SyntaxKind.LetKeyword;
        var declaredType = syntax.TypeAnnotation is { } annotation ? _scope.BindType(annotation.Type) : null;
        if (syntax.Initializer is not { } initializer)
        {
            var missing = NotSupported(syntax.Identifier, "A local variable without an initial value");
            DeclareLocal(syntax.Identifier, declaredType ?? ErrorTypeSymbol.Instance, isReadOnly);
            return new BoundExpressionStatement(syntax, missing);
        }

        var value = BindValue(initializer.Value, declaredType);
        if (declaredType is not null)
        {
            value = Convert(value, declaredType, initializer.Value);
        }
        var local = DeclareLocal(syntax.Identifier, declaredType ?? value.Type, isReadOnly);
        return new BoundLocalDeclaration(syntax, local, value);
    }

    private BoundStatement BindExpressionStatement(SyntaxNode syntax, ExpressionSyntax expression)
    {
        var bound = BindValue(expression, allowVoid: true);
        if (bound is not (BoundCall or BoundObjectCreation) && !bound.HasErrors)
        {
            Report(DiagnosticDescriptors.ExpressionNotStatement, expression);
        }
        return new BoundExpressionStatement(syntax, bound);
    }

    private BoundStatement BindAssignment(AssignmentStatementSyntax syntax)
    {
        var target = BindExpression(syntax.Target, null);
        var assignable = CheckAssignable(target, syntax.Target);
        if (syntax.OperatorToken.Kind == SyntaxKind.EqualsToken)
        {
            var value = BindValue(syntax.Value, target.HasErrors ? null : target.Type);
            return new BoundAssignment(syntax, target, assignable ? Convert(value, target.Type, syntax.Value) : value);
        }

        if (CompoundOperator(syntax.OperatorToken.Kind) is not { } operatorKind)
        {
            return new BoundExpressionStatement(syntax, NotSupported(syntax.OperatorToken, $"'{syntax.OperatorToken.Text}'"));
        }
        if (target is BoundPropertyAccess { Property.HasGetter: false } or BoundIndexerAccess { Indexer.HasGetter: false })
        {
            Report(DiagnosticDescriptors.PropertyHasNoGetter, syntax.Target, SymbolDisplay.Format(target is BoundPropertyAccess p ? p.Property : ((BoundIndexerAccess)target).Indexer));
        }
        var right = IsLiteralOnly(syntax.Value)
            ? BindLiteralOperand(syntax.Value, target.Type, target.Type, arithmetic: true)
            : BindValue(syntax.Value, target.HasErrors ? null : target.Type);
        if (!assignable || target.HasErrors || right.HasErrors)
        {
            return new BoundCompoundAssignment(syntax, target, operatorKind, right);
        }
        // As in C#: x op= y is x = (T)(x op y), allowed when the result converts to T implicitly, or explicitly
        // between numeric types when y itself converts to T (byte b; b += 1).
        var operation = ResolveBinary(syntax, syntax.OperatorToken, operatorKind, target, right);
        if (operation is BoundBinary binary)
        {
            var implicitResult = Conversions.ClassifyImplicit(binary.Type, target.Type) is not null;
            var explicitResult = Conversions.ClassifyExplicit(binary.Type, target.Type) is ConversionKind.ExplicitNumeric
                && (Conversions.ClassifyImplicit(right.Type, target.Type) is not null || operatorKind is BinaryOperatorKind.LeftShift or BinaryOperatorKind.RightShift);
            if (!implicitResult && !explicitResult)
            {
                Report(DiagnosticDescriptors.CannotConvert, syntax.Value, SymbolDisplay.FormatType(binary.Type), SymbolDisplay.FormatType(target.Type));
            }
            return new BoundCompoundAssignment(syntax, target, operatorKind, binary.Right);
        }
        return new BoundCompoundAssignment(syntax, target, operatorKind, right);
    }

    private static BinaryOperatorKind? CompoundOperator(SyntaxKind token) => token switch
    {
        SyntaxKind.PlusEqualsToken => BinaryOperatorKind.Add,
        SyntaxKind.MinusEqualsToken => BinaryOperatorKind.Subtract,
        SyntaxKind.AsteriskEqualsToken => BinaryOperatorKind.Multiply,
        SyntaxKind.SlashEqualsToken => BinaryOperatorKind.Divide,
        SyntaxKind.PercentEqualsToken => BinaryOperatorKind.Modulo,
        SyntaxKind.AmpersandEqualsToken => BinaryOperatorKind.BitwiseAnd,
        SyntaxKind.BarEqualsToken => BinaryOperatorKind.BitwiseOr,
        SyntaxKind.CaretEqualsToken => BinaryOperatorKind.ExclusiveOr,
        SyntaxKind.LessThanLessThanEqualsToken => BinaryOperatorKind.LeftShift,
        SyntaxKind.GreaterThanGreaterThanEqualsToken => BinaryOperatorKind.RightShift,
        _ => null,
    };

    /// <summary>Whether a value can be assigned to the target; reports why not.</summary>
    private bool CheckAssignable(BoundExpression target, ExpressionSyntax syntax)
    {
        switch (target)
        {
            case { HasErrors: true } when target is not (BoundNamespaceExpression or BoundMethodGroup or BoundTypeExpression):
                return false;
            case BoundLocalAccess { Local.IsReadOnly: true } local:
                Report(DiagnosticDescriptors.AssignToLet, syntax, local.Local.Name);
                return false;
            case BoundLocalAccess or BoundArrayAccess:
                return true;
            case BoundParameterAccess parameter:
                Report(DiagnosticDescriptors.AssignToParameter, syntax, parameter.Parameter.Name);
                return false;
            case BoundFieldAccess field:
                // A let field can be set where it is declared and in an init of its type (ADR-0006).
                var inInit = _method is { MethodKind: MethodKind.Constructor } && ReferenceEquals(field.Field.ContainingType, _containingType)
                    && field.Receiver is BoundSelf;
                if (field.Field.IsReadOnly && !(inInit && field.Field is SourceFieldSymbol))
                {
                    Report(DiagnosticDescriptors.AssignToLet, syntax, field.Field.Name);
                    return false;
                }
                return true;
            case BoundPropertyAccess { Property.HasSetter: false } property:
                Report(DiagnosticDescriptors.PropertyHasNoSetter, syntax, SymbolDisplay.Format(property.Property));
                return false;
            case BoundIndexerAccess { Indexer.HasSetter: false } indexer:
                Report(DiagnosticDescriptors.PropertyHasNoSetter, syntax, SymbolDisplay.Format(indexer.Indexer));
                return false;
            case BoundPropertyAccess or BoundIndexerAccess:
                return true;
            default:
                Report(DiagnosticDescriptors.NotAssignable, syntax);
                return false;
        }
    }

    private BoundStatement BindIf(IfStatementSyntax syntax)
    {
        var condition = BindCondition(syntax.Condition);
        var then = BindBlock(syntax.Block);
        var @else = syntax.ElseClause?.Body is { } elseBody ? BindStatement(elseBody) : null;
        return new BoundIf(syntax, condition, then, @else);
    }

    private BoundExpression BindCondition(ExpressionSyntax syntax) => Convert(BindValue(syntax, _context.Bool), _context.Bool, syntax);

    private BoundStatement BindWhile(WhileStatementSyntax syntax)
    {
        var condition = BindCondition(syntax.Condition);
        _loopDepth++;
        var body = BindBlock(syntax.Body);
        _loopDepth--;
        return new BoundWhile(syntax, condition, body);
    }

    private BoundStatement BindFor(ForStatementSyntax syntax)
    {
        if (syntax.AwaitKeyword is { IsMissing: false } awaitKeyword)
        {
            return new BoundExpressionStatement(syntax, NotSupported(awaitKeyword, "'await for'"));
        }
        if (syntax.Deconstruction is { } deconstruction)
        {
            return new BoundExpressionStatement(syntax, NotSupported(deconstruction, "Deconstruction in 'for'"));
        }

        PushScope();
        BoundStatement result;
        if (syntax.Collection is RangeExpressionSyntax { Left: { } left, Right: { } right } range)
        {
            var (from, to) = BindRangeEnds(left, right);
            var type = from.HasErrors || to.HasErrors ? ErrorTypeSymbol.Instance : from.Type;
            var variable = DeclareLocal(syntax.Identifier!, type, isReadOnly: true);
            var body = BindLoopBody(syntax.Body);
            result = new BoundForRange(syntax, variable, from, to, range.OperatorToken.Kind == SyntaxKind.DotDotDotToken, body);
        }
        else
        {
            var collection = BindValue(syntax.Collection);
            var elementType = collection.HasErrors ? ErrorTypeSymbol.Instance : ElementType(collection.Type);
            if (elementType is null)
            {
                Report(DiagnosticDescriptors.NotIterable, syntax.Collection, SymbolDisplay.FormatType(collection.Type));
                elementType = ErrorTypeSymbol.Instance;
            }
            var variable = DeclareLocal(syntax.Identifier!, elementType, isReadOnly: true);
            var body = BindLoopBody(syntax.Body);
            result = new BoundForEach(syntax, variable, collection, body);
        }
        PopScope();
        return result;
    }

    private BoundBlock BindLoopBody(BlockSyntax body)
    {
        _loopDepth++;
        var bound = BindBlock(body);
        _loopDepth--;
        return bound;
    }

    /// <summary>The ends of <c>a..&lt;b</c> in <c>for</c>: whole numbers of one type; a literal end takes the other end's type.</summary>
    private (BoundExpression From, BoundExpression To) BindRangeEnds(ExpressionSyntax left, ExpressionSyntax right)
    {
        BoundExpression from, to;
        if (IsLiteralOnly(left) && !IsLiteralOnly(right))
        {
            to = BindValue(right);
            from = BindLiteralOperand(left, to.Type, null, arithmetic: false);
        }
        else
        {
            from = BindValue(left);
            to = IsLiteralOnly(right) ? BindLiteralOperand(right, from.Type, null, arithmetic: false) : BindValue(right);
        }
        if (from.HasErrors || to.HasErrors)
        {
            return (from, to);
        }
        var promoted = NumericPromotion(from.Type.SpecialType, to.Type.SpecialType);
        if (promoted is not { } type || !Conversions.IsIntegral(type))
        {
            var wrong = Conversions.IsIntegral(from.Type.SpecialType) ? to : from;
            Report(DiagnosticDescriptors.RangeNotIntegral, wrong.Syntax, SymbolDisplay.FormatType(wrong.Type));
            return (Bad(left), Bad(right));
        }
        var rangeType = _context.GetSpecialType(type);
        return (Convert(from, rangeType), Convert(to, rangeType));
    }

    /// <summary>What <c>for x in</c> gives for each element: of an array, a string, or an <c>IEnumerable&lt;T&gt;</c>.</summary>
    private TypeSymbol? ElementType(TypeSymbol collection)
    {
        if (collection is ArrayTypeSymbol array)
        {
            return array.ElementType;
        }
        if (collection.SpecialType == SpecialType.System_String)
        {
            return _context.GetSpecialType(SpecialType.System_Char);
        }
        var enumerable = _context.Table.GetWellKnownType("System.Collections.Generic.IEnumerable`1");
        var candidates = new[] { collection }.Concat(collection.AllInterfaces())
            .OfType<NamedTypeSymbol>()
            .Where(t => enumerable is not null && ReferenceEquals(t.OriginalDefinition, enumerable))
            .ToList();
        return candidates.Count == 1 ? candidates[0].TypeArguments[0] : null;
    }

    private BoundStatement BindReturn(ReturnStatementSyntax syntax)
    {
        if (_method is null)
        {
            return new BoundReturn(syntax, null);
        }
        var name = _method.MethodKind == MethodKind.Constructor ? "init" : _method.Name;
        if (syntax.Expression is not { } expression)
        {
            if (!_method.ReturnsVoid && !_method.ReturnType.IsError)
            {
                Report(DiagnosticDescriptors.ReturnWithoutValue, syntax.ReturnKeyword, name, SymbolDisplay.FormatType(_method.ReturnType));
            }
            return new BoundReturn(syntax, null);
        }
        if (_method.ReturnsVoid)
        {
            Report(DiagnosticDescriptors.ReturnValueFromVoid, expression, name);
            return new BoundReturn(syntax, BindValue(expression));
        }
        return new BoundReturn(syntax, Convert(BindValue(expression, _method.ReturnType), _method.ReturnType, expression));
    }

    private BoundStatement BindJump(JumpStatementSyntax syntax)
    {
        if (_loopDepth == 0)
        {
            Report(DiagnosticDescriptors.JumpOutsideLoop, syntax, syntax.Keyword.Text);
        }
        return syntax.Kind == SyntaxKind.BreakStatement ? new BoundBreak(syntax) : new BoundContinue(syntax);
    }
}
