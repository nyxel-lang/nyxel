namespace Nyxel.Compiler.Binding;

internal static class BoundNodeExtensions
{
    /// <summary>The bound nodes directly under this one, in source order; null where an optional part is absent.</summary>
    public static IEnumerable<BoundNode?> GetChildren(this BoundNode node) => node switch
    {
        BoundBlock block => block.Statements,
        BoundLocalDeclaration declaration => Of(declaration.Initializer),
        BoundExpressionStatement statement => Of(statement.Expression),
        BoundAssignment assignment => Of(assignment.Target, assignment.Value),
        BoundCompoundAssignment compound => Of(compound.Target, compound.Value),
        BoundIf ifStatement => Of(ifStatement.Condition, ifStatement.Then, ifStatement.Else),
        BoundWhile loop => Of(loop.Condition, loop.Body),
        BoundForRange range => Of(range.From, range.To, range.Body),
        BoundForEach each => Of(each.Collection, each.Body),
        BoundReturn returnStatement => Of(returnStatement.Value),
        BoundThrow throwStatement => Of(throwStatement.Exception),
        BoundCoalesceAssignment coalesce => Of(coalesce.Target, coalesce.Value),
        BoundFieldAccess field => Of(field.Receiver),
        BoundPropertyAccess property => Of(property.Receiver),
        BoundIndexerAccess indexer => Of(indexer.Receiver).Concat(indexer.Arguments.Select(a => a.Value)),
        BoundArrayAccess array => Of(array.Array).Concat(array.Indices),
        BoundCall call => Of(call.Receiver).Concat(call.Arguments.Select(a => a.Value)),
        BoundObjectCreation creation => creation.Arguments.Select(a => a.Value),
        BoundArrayCreation array => array.Lengths,
        BoundUnary unary => Of(unary.Operand),
        BoundBinary binary => Of(binary.Left, binary.Right),
        BoundConversion conversion => Of(conversion.Operand),
        BoundInterpolatedString interpolated => interpolated.Parts.Select(p => p.Value),
        BoundNullCheck check => Of(check.Operand),
        BoundIsType isType => Of(isType.Operand),
        BoundIsCase isCase => Of(isCase.Operand),
        BoundConditionalAccess conditional => Of(conditional.Receiver, conditional.Access),
        BoundCoalesce coalesce => Of(coalesce.Left, coalesce.Right, coalesce.Jump),
        BoundBadExpression bad => bad.Children,
        _ => [],
    };

    private static IEnumerable<BoundNode?> Of(params BoundNode?[] nodes) => nodes;

    public static IEnumerable<BoundNode> DescendantsAndSelf(this BoundNode node)
    {
        yield return node;
        foreach (var child in node.GetChildren().OfType<BoundNode>())
        {
            foreach (var descendant in child.DescendantsAndSelf())
            {
                yield return descendant;
            }
        }
    }
}
