using System.Collections.Immutable;
using Nyxel.Compiler.Diagnostics;
using Nyxel.Compiler.Symbols;
using Nyxel.Compiler.Syntax;
using FlowState = System.Collections.Immutable.ImmutableDictionary<Nyxel.Compiler.Symbols.Symbol, Nyxel.Compiler.Symbols.TypeSymbol>;

namespace Nyxel.Compiler.Binding;

// Null safety and narrowing (ADR-0009, ADR-0010, ADR-0023; docs/design/semantic-analysis.md, "流分析"). The binder
// carries a flow state through the function: the locals and parameters that a check has narrowed at this point, and
// to what. A read of such a variable has the narrowed type; lowering converts it where C# needs that.

internal sealed partial class MethodBinder
{
    private static readonly FlowState NoNarrowing = FlowState.Empty.WithComparers(ReferenceEqualityComparer.Instance);

    private FlowState _flow = NoNarrowing;

    // The receivers of the '?.' being bound, by the '?.' they belong to (see BindConditionalAccess).
    private readonly Dictionary<MemberAccessExpressionSyntax, BoundConditionalReceiver> _conditionalReceivers = [];

    // The value of the statement being bound (a 'let' initial value, the right of '='): where '?? return',
    // '?? break' and '?? continue' can be lowered (see IsStatementValue).
    private ExpressionSyntax? _statementValue;

    // Flow state -------------------------------------------------------------------------------------------------

    private TypeSymbol TypeAtThisPoint(Symbol variable, TypeSymbol declared) =>
        _flow.TryGetValue(variable, out var narrowed) ? narrowed : declared;

    /// <summary>The variable a local or parameter read refers to, which is what can be narrowed; null otherwise.</summary>
    private static (Symbol Variable, TypeSymbol Declared)? Narrowable(BoundExpression expression) => expression switch
    {
        BoundLocalAccess local => (local.Local, local.Local.Type),
        BoundParameterAccess parameter => (parameter.Parameter, parameter.Parameter.Type),
        _ => null,
    };

    /// <summary>The state with <paramref name="operand"/> narrowed to <paramref name="type"/>, if it is a local or parameter.</summary>
    private static FlowState Narrow(FlowState state, BoundExpression operand, TypeSymbol type)
    {
        if (Narrowable(operand) is not var (variable, declared) || type.IsError)
        {
            return state;
        }
        return ReferenceEquals(type, declared) ? state.Remove(variable) : state.SetItem(variable, type);
    }

    /// <summary>
    /// Where two paths meet: only what holds on both. Null stands for a path that never gets here (it returned or
    /// jumped), which adds nothing.
    /// </summary>
    private static FlowState? Join(FlowState? a, FlowState? b)
    {
        if (a is null || b is null)
        {
            return a ?? b;
        }
        var joined = NoNarrowing;
        foreach (var (variable, type) in a)
        {
            if (b.TryGetValue(variable, out var other))
            {
                // Slime on one path and Enemy on the other is Enemy on both.
                var common = ReferenceEquals(type, other) ? type
                    : Conversions.ClassifyImplicit(type, other) is ConversionKind.ImplicitReference or ConversionKind.ImplicitNullable ? other
                    : Conversions.ClassifyImplicit(other, type) is ConversionKind.ImplicitReference or ConversionKind.ImplicitNullable ? type
                    : null;
                if (common is not null)
                {
                    joined = joined.SetItem(variable, common);
                }
            }
        }
        return joined;
    }

    /// <summary>
    /// The state at the start of a loop body: a variable assigned anywhere in the loop may have been assigned by an
    /// earlier pass, so its narrowing doesn't hold there (semantic-analysis.md, "流分析").
    /// </summary>
    private FlowState ForgetAssignedIn(FlowState state, SyntaxNode loop)
    {
        foreach (var assignment in loop.DescendantNodesAndSelf().OfType<AssignmentStatementSyntax>())
        {
            if (assignment.Target is IdentifierNameSyntax { Identifier.IsMissing: false } name && LookupLocal(name.Identifier.Text) is { } variable)
            {
                state = state.Remove(variable);
            }
        }
        return state;
    }

    // Conditions ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// A condition, with the flow state after it is true and after it is false: <c>x != null</c> narrows <c>x</c> when
    /// true, <c>x == null</c> when false, <c>x is Enemy</c> when true. <c>and</c>, <c>or</c> and <c>not</c> combine
    /// them, and the right of <c>and</c> is bound where the left is true (<c>x != null and x.IsAlive</c>).
    /// </summary>
    private (BoundExpression Condition, FlowState WhenTrue, FlowState WhenFalse) BindConditionFlow(ExpressionSyntax syntax)
    {
        switch (syntax)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return BindConditionFlow(parenthesized.Expression);
            case UnaryExpressionSyntax { Kind: SyntaxKind.LogicalNotExpression } not:
                {
                    var (operand, whenTrue, whenFalse) = BindConditionFlow(not.Operand);
                    return (ResolveUnary(not, UnaryOperatorKind.LogicalNot, operand), whenFalse, whenTrue);
                }
            case BinaryExpressionSyntax { Kind: SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression } binary:
                {
                    var isAnd = binary.Kind == SyntaxKind.LogicalAndExpression;
                    var (left, leftTrue, leftFalse) = BindConditionFlow(binary.Left);
                    _flow = isAnd ? leftTrue : leftFalse;
                    var (right, rightTrue, rightFalse) = BindConditionFlow(binary.Right);
                    var operatorKind = isAnd ? BinaryOperatorKind.LogicalAnd : BinaryOperatorKind.LogicalOr;
                    var bound = left.HasErrors || right.HasErrors
                        ? Bad(binary, left, right)
                        : ResolveBinary(binary, binary.OperatorToken, operatorKind, left, right);
                    return isAnd
                        ? (bound, rightTrue, Join(leftFalse, rightFalse)!)
                        : (bound, Join(leftTrue, rightTrue)!, rightFalse);
                }
            default:
                {
                    var bound = BindValue(syntax, _context.Bool);
                    var (whenTrue, whenFalse) = (_flow, _flow);
                    switch (bound)
                    {
                        case BoundNullCheck check:
                            var nonNull = Narrow(_flow, check.Operand, check.Operand.Type.NonNullable);
                            (whenTrue, whenFalse) = check.IsNull ? (_flow, nonNull) : (nonNull, _flow);
                            break;
                        case BoundIsType isType:
                            var narrowed = Narrow(_flow, isType.Operand, NarrowedByIs(isType.Operand.Type, isType.TestedType));
                            (whenTrue, whenFalse) = isType.IsNegated ? (_flow, narrowed) : (narrowed, _flow);
                            break;
                    }
                    return (bound, whenTrue, whenFalse);
                }
        }
    }

    /// <summary>A condition of <c>if</c> or <c>while</c>: a <c>bool</c>, with the states after it as in <see cref="BindConditionFlow"/>.</summary>
    private (BoundExpression Condition, FlowState WhenTrue, FlowState WhenFalse) BindCondition(ExpressionSyntax syntax)
    {
        var (condition, whenTrue, whenFalse) = BindConditionFlow(syntax);
        return (Convert(condition, _context.Bool, syntax), whenTrue, whenFalse);
    }

    /// <summary><c>and</c>, <c>or</c> and <c>not</c> used as a value: the narrowing inside applies, then both outcomes meet.</summary>
    private BoundExpression BindLogical(ExpressionSyntax syntax)
    {
        var (bound, whenTrue, whenFalse) = BindConditionFlow(syntax);
        _flow = Join(whenTrue, whenFalse)!;
        return bound;
    }

    /// <summary>What <c>x is T</c> narrows <c>x</c> to: <c>T</c>, or the type <c>x</c> has without null when that is already a <c>T</c>.</summary>
    private static TypeSymbol NarrowedByIs(TypeSymbol operandType, TypeSymbol tested)
    {
        var nonNull = operandType.NonNullable;
        return Conversions.ClassifyImplicit(nonNull, tested) is ConversionKind.Identity or ConversionKind.ImplicitReference ? nonNull : tested;
    }

    // == null, is ------------------------------------------------------------------------------------------------

    private static bool IsNullLiteral(ExpressionSyntax syntax) => syntax switch
    {
        LiteralExpressionSyntax { Kind: SyntaxKind.NullLiteralExpression } => true,
        ParenthesizedExpressionSyntax parenthesized => IsNullLiteral(parenthesized.Expression),
        _ => false,
    };

    /// <summary><c>x == null</c>, <c>x != null</c> (either side): whether the reference is null (ADR-0023).</summary>
    private BoundExpression BindNullCheck(BinaryExpressionSyntax syntax, ExpressionSyntax operandSyntax, bool isNull)
    {
        var operand = AsMaybeNull(BindValue(operandSyntax));
        if (operand.HasErrors)
        {
            return Bad(syntax, operand);
        }
        if (operand.Type is NullTypeSymbol)
        {
            Report(DiagnosticDescriptors.OperatorNotDefined, syntax.OperatorToken, syntax.OperatorToken.Text, "null", "null");
            return Bad(syntax, operand);
        }
        if (!CheckCanBeNull(operand, operandSyntax, $"{syntax.OperatorToken.Text} null"))
        {
            return Bad(syntax, operand);
        }
        return new BoundNullCheck(syntax, operand, isNull, _context.Bool);
    }

    private BoundExpression BindIs(IsExpressionSyntax syntax)
    {
        var operand = BindValue(syntax.Expression);
        if (syntax.Type is NullableTypeSyntax nullable)
        {
            Report(DiagnosticDescriptors.IsNullableType, syntax.Type, nullable.ElementType.ToString());
            return Bad(syntax, operand);
        }
        var isNegated = syntax.NotKeyword is { IsMissing: false };
        if (CaseNamedBy(syntax.Type, out var hadErrors) is { } enumCase)
        {
            if (operand.HasErrors)
            {
                return Bad(syntax, operand);
            }
            if (operand.Type is NullTypeSymbol || !Conversions.CanBe(operand.Type, enumCase.ContainingType))
            {
                Report(DiagnosticDescriptors.IsNeverTrue, syntax, SymbolDisplay.FormatType(operand.Type), SymbolDisplay.Format(enumCase));
                return Bad(syntax, operand);
            }
            return new BoundIsCase(syntax, operand, enumCase, isNegated, _context.Bool);
        }
        var type = hadErrors ? ErrorTypeSymbol.Instance : _scope.BindType(syntax.Type);
        if (operand.HasErrors || type.IsError)
        {
            return Bad(syntax, operand);
        }
        if (operand.Type is NullTypeSymbol || !Conversions.CanBe(operand.Type, type))
        {
            Report(DiagnosticDescriptors.IsNeverTrue, syntax, SymbolDisplay.FormatType(operand.Type), SymbolDisplay.FormatType(type));
            return Bad(syntax, operand);
        }
        return new BoundIsType(syntax, operand, type, isNegated, _context.Bool);
    }

    // ?. ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The deepest <c>?.</c> on the chain of member accesses, calls and indexing that ends at <paramref name="syntax"/>,
    /// leaving out the ones being bound already. Parentheses end a chain, as in C#.
    /// </summary>
    private MemberAccessExpressionSyntax? DeepestConditional(ExpressionSyntax syntax)
    {
        MemberAccessExpressionSyntax? found = null;
        for (var node = syntax; ;)
        {
            switch (node)
            {
                case MemberAccessExpressionSyntax access:
                    if (access.Kind == SyntaxKind.ConditionalMemberAccessExpression && !_conditionalReceivers.ContainsKey(access))
                    {
                        found = access;
                    }
                    node = access.Expression;
                    break;
                case InvocationExpressionSyntax invocation:
                    node = invocation.Expression;
                    break;
                case ElementAccessExpressionSyntax element:
                    node = element.Expression;
                    break;
                default:
                    return found;
            }
        }
    }

    /// <summary>
    /// A chain with a <c>?.</c> in it, as in C#: <c>a?.B.C()</c> calls <c>C</c> on <c>a.B</c> when <c>a</c> isn't null,
    /// and is null otherwise. The receiver left of <c>?.</c> is bound first; the whole chain is then bound again with
    /// a placeholder of the receiver's type without null in its place, and its type becomes <c>T?</c>.
    /// </summary>
    private BoundExpression BindConditionalAccess(ExpressionSyntax syntax, MemberAccessExpressionSyntax conditional)
    {
        var receiver = AsMaybeNull(BindValue(conditional.Expression));
        if (receiver.HasErrors)
        {
            return Bad(syntax, receiver);
        }
        if (!CheckCanBeNull(receiver, conditional.Expression, "?."))
        {
            return Bad(syntax, receiver);
        }
        var before = _flow;
        _conditionalReceivers.Add(conditional, new BoundConditionalReceiver(conditional.Expression, receiver.Type.NonNullable));
        var access = BindExpression(syntax, null);
        _conditionalReceivers.Remove(conditional);
        // The rest of the chain may not run, so what it narrowed doesn't hold afterwards.
        _flow = before;
        if (access.HasErrors || access is BoundMethodGroup or BoundTypeExpression or BoundNamespaceExpression)
        {
            return access.HasErrors ? Bad(syntax, receiver, access) : access;
        }
        var type = access.Type.IsVoid ? access.Type : _context.Table.GetNullableType(access.Type);
        return new BoundConditionalAccess(syntax, receiver, access, type);
    }

    // ?? ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>left ?? right</c> (ADR-0009): the left must be a <c>T?</c>. The right is a value, with C#'s rules for the
    /// type of the result, or a statement that leaves (<c>return</c>, <c>throw</c>, <c>break</c>, <c>continue</c>),
    /// after which the result, and the left when it is a local or parameter, is a <c>T</c>.
    /// </summary>
    private BoundExpression BindCoalesce(BinaryExpressionSyntax syntax)
    {
        var left = AsMaybeNull(BindValue(syntax.Left));
        if (left.HasErrors)
        {
            return Bad(syntax, left);
        }
        if (left.Type is NullTypeSymbol)
        {
            Report(DiagnosticDescriptors.OperatorNotDefined, syntax.OperatorToken, "??", "null", "null");
            return Bad(syntax, left);
        }
        if (!CheckCanBeNull(left, syntax.Left, "??"))
        {
            return Bad(syntax, left);
        }
        var underlying = left.Type.NonNullable;
        var afterLeft = _flow;

        if (syntax.Right is JumpExpressionSyntax jumpSyntax)
        {
            var jump = BindJumpExpression(syntax, jumpSyntax);
            _flow = Narrow(afterLeft, left, underlying);
            return jump is null ? Bad(syntax, left) : new BoundCoalesce(syntax, left, null, jump, underlying);
        }

        // A number follows the left as next to an operator (ADR-0013): 'f ?? 0' is a float 0, 'n ?? 1.5' a double.
        var right = IsLiteralOnly(syntax.Right)
            ? BindLiteralOperand(syntax.Right, underlying, null, arithmetic: false)
            : BindValue(syntax.Right, underlying);
        // The right runs only when the left is null.
        _flow = Join(afterLeft, _flow)!;
        if (right.HasErrors)
        {
            return Bad(syntax, left, right);
        }
        // C#'s type of 'a ?? b': A0 (A without null) when b converts to it, else A, else B when A0 converts to B.
        if (Conversions.ClassifyImplicit(right.Type, underlying) is not null)
        {
            return new BoundCoalesce(syntax, left, Convert(right, underlying), null, underlying);
        }
        if (Conversions.ClassifyImplicit(right.Type, left.Type) is not null)
        {
            return new BoundCoalesce(syntax, left, Convert(right, left.Type), null, left.Type);
        }
        if (Conversions.ClassifyImplicit(underlying, right.Type) is not null)
        {
            return new BoundCoalesce(syntax, left, right, null, right.Type);
        }
        Report(DiagnosticDescriptors.CannotConvert, syntax.Right, SymbolDisplay.FormatType(right.Type), SymbolDisplay.FormatType(underlying));
        return Bad(syntax, left, right);
    }

    /// <summary>
    /// The statement on the right of <c>??</c>. <c>throw</c> goes anywhere (C# has it as an expression too); the
    /// others only where the value is a whole statement's, which lowering turns into a check before the statement.
    /// </summary>
    private BoundStatement? BindJumpExpression(BinaryExpressionSyntax coalesce, JumpExpressionSyntax syntax)
    {
        if (syntax.Kind == SyntaxKind.ThrowExpression)
        {
            return BindThrow(syntax, syntax.Expression);
        }
        if (syntax.Kind == SyntaxKind.GiveExpression)
        {
            NotSupported(syntax.Keyword, "'give'");
            return null;
        }
        if (!IsStatementValue(coalesce))
        {
            NotSupported(syntax.Keyword, $"'?? {syntax.Keyword.Text}' inside a larger expression");
            return null;
        }
        switch (syntax.Kind)
        {
            case SyntaxKind.ReturnExpression:
                return BindReturn(syntax, syntax.Keyword, syntax.Expression);
            default:
                if (_loopDepth == 0)
                {
                    Report(DiagnosticDescriptors.JumpOutsideLoop, syntax, syntax.Keyword.Text);
                }
                return syntax.Kind == SyntaxKind.BreakExpression ? new BoundBreak(syntax) : new BoundContinue(syntax);
        }
    }

    /// <summary>
    /// Whether a <c>??</c> is the value of the statement being bound, or the right of one that is
    /// (<c>let t = a ?? b ?? return</c>), with parentheses around it or not.
    /// </summary>
    private bool IsStatementValue(ExpressionSyntax syntax)
    {
        while (true)
        {
            if (ReferenceEquals(syntax, _statementValue))
            {
                return true;
            }
            switch (syntax.Parent)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    syntax = parenthesized;
                    break;
                case BinaryExpressionSyntax { Kind: SyntaxKind.CoalesceExpression } outer when ReferenceEquals(outer.Right, syntax):
                    syntax = outer;
                    break;
                default:
                    return false;
            }
        }
    }

    // throw ------------------------------------------------------------------------------------------------------

    private BoundThrow BindThrow(SyntaxNode syntax, ExpressionSyntax? expressionSyntax)
    {
        if (expressionSyntax is null)
        {
            return new BoundThrow(syntax, Bad(syntax));
        }
        var exception = BindValue(expressionSyntax);
        var exceptionType = _context.Table.GetWellKnownType("System.Exception");
        if (!exception.HasErrors && exceptionType is not null && Conversions.ClassifyImplicit(exception.Type, exceptionType) is not (ConversionKind.Identity or ConversionKind.ImplicitReference))
        {
            if (exception.Type.IsNullable && Conversions.ClassifyImplicit(exception.Type.NonNullable, exceptionType) is not null)
            {
                ReportMaybeNullConversion(exception, exceptionType, expressionSyntax);
            }
            else
            {
                Report(DiagnosticDescriptors.NotException, expressionSyntax, SymbolDisplay.FormatType(exception.Type));
            }
            return new BoundThrow(syntax, Bad(expressionSyntax, exception));
        }
        return new BoundThrow(syntax, exception);
    }

    /// <summary>
    /// Whether <c>== null</c>, <c>??</c>, <c>?.</c> or <c>??=</c> can be used on the operand: a <c>T?</c>, or a reference
    /// type, where the check is only redundant (ADR-0041: a warning once the compiler has warnings, since C# code may
    /// still pass null). A value type that can't be null is an error (NYX2063).
    /// </summary>
    private bool CheckCanBeNull(BoundExpression operand, SyntaxNode location, string operatorText)
    {
        if (operand.Type.IsNullable || operand.Type.IsError || !operand.Type.IsValueType)
        {
            return true;
        }
        Report(DiagnosticDescriptors.NeverNull, location, SymbolDisplay.FormatType(operand.Type), operatorText);
        return false;
    }

    /// <summary>
    /// A variable declared <c>T?</c> as the null operators see it: still a <c>T?</c> after a check narrowed it, so that
    /// checking it again is only redundant (ADR-0041), and C# gets <c>x is null</c> on its declared type, not on
    /// <c>(int)x</c>. A type narrowing stays: an <c>Enemy?</c> checked to be a <c>Slime</c> is a <c>Slime?</c> here.
    /// </summary>
    private BoundExpression AsMaybeNull(BoundExpression operand)
    {
        if (Narrowable(operand) is not var (_, declared) || !declared.IsNullable || operand.Type.IsNullable)
        {
            return operand;
        }
        var type = _context.Table.GetNullableType(operand.Type);
        return operand switch
        {
            BoundLocalAccess local => new BoundLocalAccess(local.Syntax, local.Local, type),
            BoundParameterAccess parameter => new BoundParameterAccess(parameter.Syntax, parameter.Parameter, type),
            _ => operand,
        };
    }

    // Diagnostics --------------------------------------------------------------------------------------------------

    /// <summary>NYX2057 for using a <c>T?</c> as if it had a value: its members, indexing it, walking it, an operator.</summary>
    private void ReportMaybeNull(BoundExpression operand, SyntaxNode location, bool suggestConditional)
    {
        var text = operand.Syntax.ToString();
        var check = Narrowable(operand) is not null
            ? $"check it first ('if {text} != null {{ ... }}' or 'if {text} == null {{ return }}')"
            : $"store it in a local and check it ('let value = {text} ?? return')";
        Report(DiagnosticDescriptors.MaybeNull, location, text, suggestConditional ? $"{check}, or use '?.'" : $"{check}, or give a default with '??'");
    }

    /// <summary>NYX2058 for a <c>T?</c> where a <c>T</c> is expected.</summary>
    private void ReportMaybeNullConversion(BoundExpression value, TypeSymbol target, SyntaxNode location)
    {
        var text = value.Syntax.ToString();
        var check = Narrowable(value) is not null
            ? $"check '{text}' first ('if {text} == null {{ return }}'), or give a default with '??'"
            : $"check it with '?? return' or '?? throw', or give a default with '??'";
        Report(DiagnosticDescriptors.MaybeNullConversion, location, SymbolDisplay.FormatType(value.Type), SymbolDisplay.FormatType(target), check);
    }
}
