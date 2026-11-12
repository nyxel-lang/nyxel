using Nyxel.Compiler.Symbols;

namespace Nyxel.Compiler.Binding;

/// <summary>
/// The checks that follow the paths through a bound body (docs/design/semantic-analysis.md, "流分析"): whether the
/// end of a body can be reached, and which fields every path of an <c>init</c> sets.
/// </summary>
internal static class FlowAnalysis
{
    /// <summary>Whether running the statement can finish normally, rather than always leaving by a jump.</summary>
    public static bool CanComplete(BoundStatement statement) => statement switch
    {
        BoundBlock block => block.Statements.All(CanComplete),
        BoundReturn or BoundBreak or BoundContinue or BoundThrow => false,
        BoundIf ifStatement => ifStatement.Else is null || CanComplete(ifStatement.Then) || CanComplete(ifStatement.Else),
        BoundWhile loop => loop.Condition.ConstantValue is not true || ContainsBreak(loop.Body),
        _ => true,
    };

    /// <summary>Whether a <c>break</c> leaves the loop this is the body of (not one of a loop inside it).</summary>
    public static bool ContainsBreak(BoundStatement statement) => statement switch
    {
        BoundBreak => true,
        BoundBlock block => block.Statements.Any(ContainsBreak),
        BoundIf ifStatement => ContainsBreak(ifStatement.Then) || (ifStatement.Else is { } @else && ContainsBreak(@else)),
        BoundWhile or BoundForRange or BoundForEach => false,
        _ => CoalesceJumps(statement).Any(j => j is BoundBreak),
    };

    /// <summary>The statements on the right of <c>??</c> in a statement's expressions: <c>let t = x ?? return</c>.</summary>
    public static IEnumerable<BoundStatement> CoalesceJumps(BoundStatement statement) => statement switch
    {
        BoundBlock or BoundIf or BoundWhile or BoundForRange or BoundForEach => [],
        _ => statement.GetChildren().OfType<BoundExpression>()
            .SelectMany(e => e.DescendantsAndSelf())
            .OfType<BoundCoalesce>()
            .Select(c => c.Jump)
            .OfType<BoundStatement>(),
    };

    /// <summary>
    /// The fields and auto properties of <paramref name="members"/> that some path through the init body leaves
    /// unset, at a <c>return</c> or at its end. A member counts as set by <c>self.member = value</c>.
    /// </summary>
    public static IEnumerable<Symbol> MembersNotSetOnEveryPath(BoundBlock body, IReadOnlyCollection<Symbol> members)
    {
        var exits = new List<HashSet<Symbol>>();
        var atEnd = Assign(body, [], exits);
        if (atEnd is not null)
        {
            exits.Add(atEnd);
        }
        return members.Where(m => exits.Any(set => !set.Contains(m)));
    }

    // The members set when the statement finishes; null when it never finishes normally. Returns record their set.
    private static HashSet<Symbol>? Assign(BoundStatement statement, HashSet<Symbol> before, List<HashSet<Symbol>> exits)
    {
        // let t = x ?? return: a way out before the statement finishes.
        if (CoalesceJumps(statement).Any(j => j is BoundReturn))
        {
            exits.Add(before);
        }
        switch (statement)
        {
            case BoundBlock block:
                HashSet<Symbol>? current = before;
                foreach (var inner in block.Statements)
                {
                    if (current is null)
                    {
                        break;
                    }
                    current = Assign(inner, current, exits);
                }
                return current;
            case BoundAssignment { Target: BoundFieldAccess { Receiver: BoundSelf, Field: var field } }:
                return [.. before, field];
            case BoundAssignment { Target: BoundPropertyAccess { Receiver: BoundSelf, Property: var property } }:
                return [.. before, property];
            case BoundIf ifStatement:
                var then = Assign(ifStatement.Then, before, exits);
                var @else = ifStatement.Else is { } elseStatement ? Assign(elseStatement, before, exits) : before;
                return then is null ? @else : @else is null ? then : [.. then.Intersect(@else)];
            case BoundWhile loop:
                Assign(loop.Body, before, exits);
                return loop.Condition.ConstantValue is true && !ContainsBreak(loop.Body) ? null : before;
            case BoundForRange forRange:
                Assign(forRange.Body, before, exits);
                return before;
            case BoundForEach forEach:
                Assign(forEach.Body, before, exits);
                return before;
            case BoundReturn:
                exits.Add(before);
                return null;
            case BoundBreak or BoundContinue or BoundThrow:
                return null;
            default:
                return before;
        }
    }
}
