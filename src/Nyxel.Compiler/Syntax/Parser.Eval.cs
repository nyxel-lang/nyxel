using Nyxel.Compiler.Diagnostics;

namespace Nyxel.Compiler.Syntax;

/// <summary>
/// What may stand in an <c>eval</c> block, and where <c>give</c> may stand (ADR-0025), depends on the blocks, loops
/// and lambdas around it, so it is checked on the finished tree, where each node can look at its parents:
/// <list type="bullet">
/// <item><c>give</c> belongs to the nearest <c>eval</c> and can't leave a lambda;</item>
/// <item>an <c>eval</c> block can't <c>return</c>, and <c>break</c> / <c>continue</c> in it only serve its own loops;</item>
/// <item>a block whose last statement is an expression falls off its end. Whether every other path ends in
/// <c>give</c> or <c>throw</c> is up to binding.</item>
/// </list>
/// </summary>
internal sealed partial class Parser
{
    private void CheckEvalBlocks(SyntaxNode root)
    {
        foreach (var node in root.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case EvalExpressionSyntax eval:
                    CheckEvalEnd(eval);
                    break;
                case GiveStatementSyntax give when EnclosingEval(give) == null:
                    Report(DiagnosticDescriptors.GiveOutsideEval, give.GiveKeyword.Span);
                    break;
                case JumpExpressionSyntax { Kind: SyntaxKind.GiveExpression } give when EnclosingEval(give) == null:
                    Report(DiagnosticDescriptors.GiveOutsideEval, give.Keyword.Span);
                    break;
                case ReturnStatementSyntax @return when EnclosingEval(@return) != null:
                    Report(DiagnosticDescriptors.ReturnInEval, @return.ReturnKeyword.Span);
                    break;
                case JumpExpressionSyntax { Kind: SyntaxKind.ReturnExpression } @return when EnclosingEval(@return) != null:
                    Report(DiagnosticDescriptors.ReturnInEval, @return.Keyword.Span);
                    break;
                case JumpStatementSyntax jump when LeavesEval(jump):
                    Report(DiagnosticDescriptors.JumpOutOfEval, jump.Keyword.Span, jump.Keyword.Text);
                    break;
                case JumpExpressionSyntax { Kind: SyntaxKind.BreakExpression or SyntaxKind.ContinueExpression } jump when LeavesEval(jump):
                    Report(DiagnosticDescriptors.JumpOutOfEval, jump.Keyword.Span, jump.Keyword.Text);
                    break;
            }
        }
    }

    /// <summary>
    /// An empty block, or one whose last statement is an expression (Rust's and Kotlin's way to give the value),
    /// runs off its end. A <c>match</c> statement there can still give in every arm.
    /// </summary>
    private void CheckEvalEnd(EvalExpressionSyntax eval)
    {
        var block = eval.Block;
        if (block.OpenBraceToken.IsMissing)
        {
            return;
        }
        if (block.Statements.Count == 0)
        {
            Report(DiagnosticDescriptors.EvalWithoutGive, eval.EvalKeyword.Span, "value");
        }
        else if (block.Statements[^1] is ExpressionStatementSyntax { Expression: not MatchExpressionSyntax } last)
        {
            Report(DiagnosticDescriptors.EvalWithoutGive, last.Span, last.Expression.ToString());
        }
    }

    /// <summary>The <c>eval</c> a <c>give</c> or <c>return</c> is in, or null when a lambda or nothing comes first.</summary>
    private static EvalExpressionSyntax? EnclosingEval(SyntaxNode node)
    {
        for (var parent = node.Parent; parent != null; parent = parent.Parent)
        {
            switch (parent)
            {
                case EvalExpressionSyntax eval:
                    return eval;
                case LambdaExpressionSyntax:
                    return null;
            }
        }
        return null;
    }

    /// <summary>A <c>break</c> or <c>continue</c> that has no loop between it and the nearest <c>eval</c>.</summary>
    private static bool LeavesEval(SyntaxNode node)
    {
        for (SyntaxNode? child = node, parent = node.Parent; parent != null; child = parent, parent = parent.Parent)
        {
            switch (parent)
            {
                case WhileStatementSyntax loop when loop.Body == child:
                    return false;
                case ForStatementSyntax loop when loop.Body == child:
                    return false;
                case EvalExpressionSyntax:
                    return true;
                case LambdaExpressionSyntax:
                    return false;
            }
        }
        return false;
    }
}
