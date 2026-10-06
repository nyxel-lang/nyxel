using Nyxel.Compiler.Diagnostics;

namespace Nyxel.Compiler.Syntax;

/// <summary>
/// A range is not a value (ADR-0022). It may stand in three places, which the parser can't see while it reads the
/// range, so they are checked on the finished tree, where each range can look at its parents:
/// <list type="bullet">
/// <item>a slice <c>items[a..&lt;b]</c>, the only place that may leave out an end: <c>items[..&lt;3]</c>, <c>name[1...]</c>;</item>
/// <item>the collection of a <c>for</c>, maybe followed by <c>.Reversed()</c> and <c>.StepBy(n)</c>;</item>
/// <item>a <c>case</c>, which has its own node (<see cref="RangePatternSyntax"/>) and is checked while parsing.</item>
/// </list>
/// C#'s <c>..</c> is reported here too, because the right message depends on the place: <c>name[1..]</c> is
/// <c>name[1...]</c>.
/// </summary>
internal sealed partial class Parser
{
    private void CheckRanges(SyntaxNode root)
    {
        foreach (var range in root.DescendantNodesAndSelf().OfType<RangeExpressionSyntax>())
        {
            CheckRange(range);
        }
    }

    private void CheckRange(RangeExpressionSyntax range)
    {
        var operatorToken = range.OperatorToken;
        var node = OutsideParentheses(range);

        if (IsSliceArgument(node))
        {
            if (range.Right == null && range.Left != null && operatorToken.Kind != SyntaxKind.DotDotDotToken)
            {
                // name[1..] and name[1..<]: to the end is 'a...'.
                Report(DiagnosticDescriptors.SliceToEnd, operatorToken.Span, range.Left.ToString());
            }
            else if (operatorToken.Kind == SyntaxKind.DotDotToken)
            {
                Report(DiagnosticDescriptors.RangeDotDot, operatorToken.Span);
            }
            else if (range.Left == null && range.Right == null)
            {
                Report(DiagnosticDescriptors.RangeEnd, operatorToken.Span);
            }
            return;
        }

        if (operatorToken.Kind == SyntaxKind.DotDotToken)
        {
            Report(DiagnosticDescriptors.RangeDotDot, operatorToken.Span);
        }
        if (node.Parent is RangeExpressionSyntax)
        {
            return; // a..<b..<c, reported while parsing
        }

        // (0..<n).Reversed().StepBy(2) in a 'for'.
        while (node.Parent is MemberAccessExpressionSyntax access && access.Expression == node)
        {
            var name = access.Name.Identifier.Text;
            if (name is not ("Reversed" or "StepBy"))
            {
                Report(DiagnosticDescriptors.RangeMethod, access.Name.Span, name);
                return;
            }
            if (access.Kind != SyntaxKind.SimpleMemberAccessExpression
                || access.Parent is not InvocationExpressionSyntax invocation || invocation.Expression != access)
            {
                break;
            }
            node = OutsideParentheses(invocation);
        }

        if (node.Parent is ForStatementSyntax loop && loop.Collection == node)
        {
            if (range.Left == null || range.Right == null)
            {
                Report(DiagnosticDescriptors.RangeEnd, operatorToken.Span);
            }
            return;
        }
        if (node.Parent is ConstantPatternSyntax && (range.Left == null || range.Right == null))
        {
            // case ..<10: a case range has both ends; 'case < 10' says the rest.
            Report(DiagnosticDescriptors.RangeEnd, operatorToken.Span);
            return;
        }
        Report(DiagnosticDescriptors.RangeValue, range.Span);
    }

    private static SyntaxNode OutsideParentheses(SyntaxNode node)
    {
        while (node.Parent is ParenthesizedExpressionSyntax parenthesized)
        {
            node = parenthesized;
        }
        return node;
    }

    /// <summary>A plain argument of <c>x[...]</c>: not named, not <c>ref</c>, not the size in C#'s <c>new T[n]</c>.</summary>
    private static bool IsSliceArgument(SyntaxNode node) =>
        node.Parent is ArgumentSyntax { NameEquals: null, RefKindKeyword: null } argument
        && argument.Parent is ArgumentListSyntax { Kind: SyntaxKind.BracketedArgumentList } list
        && list.Parent is ElementAccessExpressionSyntax;
}
