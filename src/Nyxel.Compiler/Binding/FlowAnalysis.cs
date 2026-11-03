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
        BoundReturn or BoundBreak or BoundContinue => false,
        BoundIf ifStatement => ifStatement.Else is null || CanComplete(ifStatement.Then) || CanComplete(ifStatement.Else),
        BoundWhile loop => loop.Condition.ConstantValue is not true || ContainsBreak(loop.Body),
        _ => true,
    };

    private static bool ContainsBreak(BoundStatement statement) => statement switch
    {
        BoundBreak => true,
        BoundBlock block => block.Statements.Any(ContainsBreak),
        BoundIf ifStatement => ContainsBreak(ifStatement.Then) || (ifStatement.Else is { } @else && ContainsBreak(@else)),
        _ => false,
    };

    /// <summary>
    /// The fields of <paramref name="fields"/> that some path through the init body leaves unset, at a
    /// <c>return</c> or at its end. A field counts as set by <c>self.field = value</c>.
    /// </summary>
    public static IEnumerable<FieldSymbol> FieldsNotSetOnEveryPath(BoundBlock body, IReadOnlyCollection<FieldSymbol> fields)
    {
        var exits = new List<HashSet<FieldSymbol>>();
        var atEnd = Assign(body, [], exits);
        if (atEnd is not null)
        {
            exits.Add(atEnd);
        }
        return fields.Where(f => exits.Any(set => !set.Contains(f)));
    }

    // The fields set when the statement finishes; null when it never finishes normally. Returns record their set.
    private static HashSet<FieldSymbol>? Assign(BoundStatement statement, HashSet<FieldSymbol> before, List<HashSet<FieldSymbol>> exits)
    {
        switch (statement)
        {
            case BoundBlock block:
                HashSet<FieldSymbol>? current = before;
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
            case BoundBreak or BoundContinue:
                return null;
            default:
                return before;
        }
    }
}
