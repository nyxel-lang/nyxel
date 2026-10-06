using Nyxel.Compiler.Text;

namespace Nyxel.Compiler.Syntax;

/// <summary>
/// Writes a tree as indented text, one node or token per line: nodes with their one-based line:column range,
/// tokens with their text. Skipped tokens are shown where they sit, so error recovery is visible. Used by
/// <c>nyxel parse --tree</c> and the snapshot tests.
/// </summary>
public static class SyntaxTreePrinter
{
    public static string Print(SyntaxNode node, SourceText text)
    {
        var writer = new StringWriter { NewLine = "\n" };
        Print(node, text, writer);
        return writer.ToString();
    }

    public static void Print(SyntaxNode node, SourceText text, TextWriter writer) => Print(node, text, writer, indent: 0);

    private static void Print(SyntaxNode node, SourceText text, TextWriter writer, int indent)
    {
        var padding = new string(' ', indent * 2);
        if (node is SyntaxToken token)
        {
            foreach (var trivia in token.LeadingTrivia.Where(t => t.Kind == SyntaxKind.SkippedTokensTrivia))
            {
                writer.WriteLine($"{padding}SkippedTokens {Quote(trivia.Text.Trim())}");
            }
            var value = token.IsMissing ? "<missing>" : Quote(token.Text);
            writer.WriteLine($"{padding}{token.Kind} {value}");
            return;
        }

        var span = text.GetLinePositionSpan(node.Span);
        writer.WriteLine($"{padding}{node.Kind} {span.Start}-{span.End}");
        foreach (var child in node.GetChildren())
        {
            Print(child, text, writer, indent + 1);
        }
    }

    private static string Quote(string text) =>
        "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal) + "\"";
}
