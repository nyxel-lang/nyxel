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
        ReturnStatementSyntax returnStatement => BindReturn(returnStatement, returnStatement.ReturnKeyword, returnStatement.Expression),
        JumpStatementSyntax jump => BindJump(jump),
        ThrowStatementSyntax throwStatement => BindThrow(throwStatement, throwStatement.Expression),
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

        var value = BindStatementValue(initializer.Value, declaredType);
        var valueType = value.Type;
        if (declaredType is not null)
        {
            value = Convert(value, declaredType, initializer.Value);
        }
        else if (value.Type is NullTypeSymbol)
        {
            Report(DiagnosticDescriptors.NullNeedsType, initializer.Value, syntax.Identifier.Text);
            value = Bad(initializer.Value, value);
        }
        var local = DeclareLocal(syntax.Identifier, declaredType ?? value.Type, isReadOnly);
        if (!value.HasErrors)
        {
            NarrowAfterAssignment(local, local.Type, valueType);
        }
        return new BoundLocalDeclaration(syntax, local, value);
    }

    /// <summary>The value of a whole statement, where the right of '??' may also be return, break or continue.</summary>
    private BoundExpression BindStatementValue(ExpressionSyntax syntax, TypeSymbol? expected)
    {
        var outer = _statementValue;
        _statementValue = syntax;
        var value = BindValue(syntax, expected);
        _statementValue = outer;
        return value;
    }

    private BoundStatement BindExpressionStatement(SyntaxNode syntax, ExpressionSyntax expression)
    {
        var bound = BindValue(expression, allowVoid: true);
        if (bound is not (BoundCall or BoundObjectCreation or BoundConditionalAccess { Access: BoundCall }) && !bound.HasErrors)
        {
            Report(DiagnosticDescriptors.ExpressionNotStatement, expression);
        }
        return new BoundExpressionStatement(syntax, bound);
    }

    private BoundStatement BindAssignment(AssignmentStatementSyntax syntax)
    {
        // A narrowed local is read with its narrowed type; it is assigned with its declared one.
        var read = BindExpression(syntax.Target, null);
        var target = read switch
        {
            BoundLocalAccess local => new BoundLocalAccess(local.Syntax, local.Local),
            _ => read,
        };
        var assignable = CheckAssignable(target, syntax.Target);
        if (syntax.OperatorToken.Kind == SyntaxKind.EqualsToken)
        {
            var value = BindStatementValue(syntax.Value, target.HasErrors ? null : target.Type);
            // A new value: what was known about the old one is gone, and the new one may be known not null (ADR-0041).
            if (Narrowable(target) is var (variable, declared))
            {
                NarrowAfterAssignment(variable, declared, value.HasErrors ? ErrorTypeSymbol.Instance : value.Type);
            }
            return new BoundAssignment(syntax, target, assignable ? Convert(value, target.Type, syntax.Value) : value);
        }
        if (syntax.OperatorToken.Kind == SyntaxKind.QuestionQuestionEqualsToken)
        {
            return BindCoalesceAssignment(syntax, target, assignable);
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
            ? BindLiteralOperand(syntax.Value, read.Type, read.Type, arithmetic: true)
            : BindValue(syntax.Value, read.HasErrors ? null : read.Type);
        if (!assignable || target.HasErrors || right.HasErrors)
        {
            return new BoundCompoundAssignment(syntax, target, operatorKind, right);
        }
        // As in C#: x op= y is x = (T)(x op y), allowed when the result converts to T implicitly, or explicitly
        // between numeric types when y itself converts to T (byte b; b += 1).
        var operation = ResolveBinary(syntax, syntax.OperatorToken, operatorKind, read, right);
        if (Narrowable(target) is var (assigned, assignedType))
        {
            NarrowAfterAssignment(assigned, assignedType, operation.Type);
        }
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

    /// <summary>
    /// <c>target ??= value</c> (ADR-0023): the target must be a <c>T?</c>; the value goes in as a <c>T</c> when it is
    /// one, and then a local target is no longer null.
    /// </summary>
    private BoundStatement BindCoalesceAssignment(AssignmentStatementSyntax syntax, BoundExpression target, bool assignable)
    {
        if (!target.HasErrors && !CheckCanBeNull(target, syntax.Target, "??="))
        {
            assignable = false;
        }
        var underlying = target.HasErrors ? null : target.Type.NonNullable;
        var value = IsLiteralOnly(syntax.Value) && underlying is not null
            ? BindLiteralOperand(syntax.Value, underlying, null, arithmetic: false)
            : BindValue(syntax.Value, underlying);
        if (!assignable || target.HasErrors || value.HasErrors)
        {
            return new BoundCoalesceAssignment(syntax, target, value);
        }
        if (Conversions.ClassifyImplicit(value.Type, underlying!) is not null)
        {
            _flow = Narrow(_flow, target, underlying!);
            return new BoundCoalesceAssignment(syntax, target, Convert(value, underlying!));
        }
        return new BoundCoalesceAssignment(syntax, target, Convert(value, target.Type, syntax.Value));
    }

    /// <summary>
    /// After a local gets a value (ADR-0041): what was known about the old value is gone, and a <c>T?</c> local is a
    /// <c>T</c> when the value can't be null. Only null is tracked: a <c>Weapon?</c> given a <c>Sword</c> is a <c>Weapon</c>.
    /// </summary>
    private void NarrowAfterAssignment(Symbol variable, TypeSymbol declared, TypeSymbol valueType)
    {
        _flow = _flow.Remove(variable);
        if (declared.IsNullable && !valueType.IsNullable && !valueType.IsError && valueType is not NullTypeSymbol)
        {
            _flow = _flow.SetItem(variable, declared.NonNullable);
        }
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
            case BoundFieldAccess { Receiver: var receiver } when receiver is not null && Narrowable(receiver) is var (_, declared) && declared.IsNullable && declared.IsValueType:
                // C# can't set a field of the value inside a Nullable<T> in place.
                NotSupported(syntax, "Setting a field of a checked 'T?' struct");
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

    /// <summary>
    /// <c>if</c>: each branch starts with what the condition says when it is true or false, and after the <c>if</c>
    /// holds what holds at the end of every branch that gets there (<c>if x == null { return }</c> leaves <c>x</c>
    /// checked).
    /// </summary>
    private BoundStatement BindIf(IfStatementSyntax syntax)
    {
        var (condition, whenTrue, whenFalse) = BindCondition(syntax.Condition);
        _flow = whenTrue;
        var then = BindBlock(syntax.Block);
        var afterThen = FlowAnalysis.CanComplete(then) ? _flow : null;
        _flow = whenFalse;
        var @else = syntax.ElseClause?.Body is { } elseBody ? BindStatement(elseBody) : null;
        var afterElse = @else is null || FlowAnalysis.CanComplete(@else) ? _flow : null;
        _flow = Join(afterThen, afterElse) ?? whenFalse;
        return new BoundIf(syntax, condition, then, @else);
    }

    private BoundStatement BindWhile(WhileStatementSyntax syntax)
    {
        _flow = ForgetAssignedIn(_flow, syntax);
        var start = _flow;
        var (condition, whenTrue, whenFalse) = BindCondition(syntax.Condition);
        _flow = whenTrue;
        _loopDepth++;
        var body = BindBlock(syntax.Body);
        _loopDepth--;
        // Leaving by the condition, it is false; leaving by 'break', only what held at the start is sure.
        _flow = FlowAnalysis.ContainsBreak(body) ? start : whenFalse;
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
        _flow = ForgetAssignedIn(_flow, syntax);
        var start = _flow;
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
            if (collection.Type.IsNullable)
            {
                ReportMaybeNull(collection, syntax.Collection, suggestConditional: false);
                elementType = ErrorTypeSymbol.Instance;
            }
            else if (elementType is null)
            {
                Report(DiagnosticDescriptors.NotIterable, syntax.Collection, SymbolDisplay.FormatType(collection.Type));
                elementType = ErrorTypeSymbol.Instance;
            }
            var variable = DeclareLocal(syntax.Identifier!, elementType, isReadOnly: true);
            var body = BindLoopBody(syntax.Body);
            result = new BoundForEach(syntax, variable, collection, body);
        }
        PopScope();
        _flow = start;
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

    /// <summary><c>return</c>, as a statement or on the right of <c>??</c>.</summary>
    private BoundReturn BindReturn(SyntaxNode syntax, SyntaxToken keyword, ExpressionSyntax? value)
    {
        if (_method is null)
        {
            return new BoundReturn(syntax, null);
        }
        var name = _method.MethodKind == MethodKind.Constructor ? "init" : _method.Name;
        if (value is not { } expression)
        {
            if (!_method.ReturnsVoid && !_method.ReturnType.IsError)
            {
                Report(DiagnosticDescriptors.ReturnWithoutValue, keyword, name, SymbolDisplay.FormatType(_method.ReturnType));
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
