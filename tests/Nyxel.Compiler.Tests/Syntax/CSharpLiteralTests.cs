using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nyxel.Compiler.Text;
using NyxelSyntaxKind = Nyxel.Compiler.Syntax.SyntaxKind;
using NyxelSyntaxTree = Nyxel.Compiler.Syntax.SyntaxTree;

namespace Nyxel.Compiler.Tests.Syntax;

/// <summary>
/// Character literals and raw strings are the same as in C# (ADR-0020). Each literal is lexed by Nyxel and parsed
/// by Roslyn; the values and whether there is an error must agree.
/// </summary>
public class CSharpLiteralTests
{
    private const string Q = "\"\"\"";

    public static TheoryData<string> CharacterLiterals =>
    [
        "'a'", "'\\n'", "'\\''", "'\"'", "'\\\\'", "'\\0'", "'\\x41'", "'\\x041'", "'\\u0041'", "'\\U00000041'",
        "'é'", "'\\U0001F600'", "'\U0001F600'", "''", "'ab'", "'\\q'", "'\\'", "'a",
    ];

    [Theory]
    [MemberData(nameof(CharacterLiterals))]
    public void CharacterLiteralsMatchCSharp(string literal) => _ = AssertSameAsCSharp(literal);

    public static TheoryData<string> RawStrings =>
    [
        Q + "a\"b" + Q,
        Q + " " + Q,
        Q + "C:\\Games\\save.json" + Q,
        "\"\"\"\" a \"\"\" b \"\"\"\"",
        Q + "a\"\"\"\"",
        "\"\"\"\"\"\"",
        Q + "\n    a\n\n  \n      \n    b\n    " + Q,
        Q + "\n    a\n\t\n    " + Q,
        Q + "\n    a\n      \t\n    " + Q,
        Q + "\n\ta\n \n\t" + Q,
        Q + "\n  \ta\n  \n  \t" + Q,
        Q + "\r\n    a\r\n    b\r\n    " + Q,
        Q + "   \n  x\n  " + Q,
        Q + "\n    a\n  b\n    " + Q,
        Q + "\n\ta\n    " + Q,
        Q + "\n    " + Q,
        Q + "\n  a " + Q + " b\n  " + Q,
        Q + "\n  a\n  \"\"\"\"",
        Q + "\n  a\n",
        Q + "a\nb" + Q,
        "$" + Q + "a {1} b" + Q,
        "$" + Q + "a {{1}} b" + Q,
        "$" + Q + "a } b" + Q,
        "$$" + Q + "a {1} {{2}} b" + Q,
        "$$" + Q + "a {{{2}}} b" + Q,
        "$$" + Q + "a {{{{2}}}} b" + Q,
        "$$" + Q + "a }} b" + Q,
        "$$" + Q + "a } b" + Q,
        "$$" + Q + "a {{2}}} b" + Q,
        "$$" + Q + "a {{2} b" + Q,
        "$" + Q + "\n    x {1}\n      y\n    " + Q,
        "$" + Q + "\n    {1}\n  \n    " + Q,
        "$" + Q + "\n    a {1:F2}\n    " + Q,
        "$$" + Q + "\n    a {{1:F2}}\n    " + Q,
        "$$" + Q + "\n    a {{1,5:F2}}}\n    " + Q,
        "$$" + Q + "\n    { \"hp\": {{1}} }\n    " + Q,
        "$" + Q + "\n    a {1} " + Q + "\n    " + Q,
    ];

    [Theory]
    [MemberData(nameof(RawStrings))]
    public void RawStringsMatchCSharp(string literal) => _ = AssertSameAsCSharp(literal);

    [Fact]
    public void GeneratedRawStringsMatchCSharp()
    {
        var random = new Random(20261006);
        var valid = 0;
        for (var i = 0; i < 3000; i++)
        {
            if (AssertSameAsCSharp(GenerateRawString(random)) != "error")
            {
                valid++;
            }
        }
        // Both sides agreeing that everything is an error would prove little.
        Assert.InRange(valid, 600, 2400);
    }

    private static string AssertSameAsCSharp(string literal)
    {
        var expected = LexWithRoslyn(literal);
        var actual = LexWithNyxel(literal);
        Assert.True(expected == actual, $"Literal: {Show(literal)}\nC#:    {expected}\nNyxel: {actual}");
        return expected;
    }

    /// <summary>"error", or the value: text parts joined with '|', a hole as '{}', empty text left out.</summary>
    private static string LexWithRoslyn(string literal)
    {
        var expression = SyntaxFactory.ParseExpression(literal);
        if (expression.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error) || expression.FullSpan.Length != literal.Length)
        {
            return "error";
        }
        return expression switch
        {
            LiteralExpressionSyntax l => Show(l.Token.ValueText),
            InterpolatedStringExpressionSyntax s => string.Join("|", s.Contents
                .Select(c => c is InterpolatedStringTextSyntax t ? Show(t.TextToken.ValueText) : "{}")
                .Where(part => part.Length > 0)),
            _ => "unexpected " + expression.Kind(),
        };
    }

    private static string LexWithNyxel(string literal)
    {
        var tokens = NyxelSyntaxTree.ParseTokens(SourceText.From(literal), out var diagnostics);
        Assert.Equal(literal, string.Concat(tokens.Select(t => t.ToFullString())));
        if (!diagnostics.IsEmpty)
        {
            return "error";
        }

        var parts = new List<string>();
        var holeDepth = 0;
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            var token = tokens[i];
            switch (token.Kind)
            {
                case NyxelSyntaxKind.CharacterLiteralToken when i == 0:
                    parts.Add(Show(((char)token.Value!).ToString()));
                    break;
                case NyxelSyntaxKind.StringLiteralToken when i == 0:
                    parts.Add(Show((string)token.Value!));
                    break;
                case NyxelSyntaxKind.InterpolatedStringStartToken when i == 0:
                    break;
                case NyxelSyntaxKind.InterpolatedStringTextToken when holeDepth == 0:
                    parts.Add(Show((string)token.Value!));
                    break;
                case NyxelSyntaxKind.OpenBraceToken:
                    if (holeDepth++ == 0)
                    {
                        parts.Add("{}");
                    }
                    break;
                case NyxelSyntaxKind.CloseBraceToken:
                    holeDepth--;
                    break;
                case NyxelSyntaxKind.InterpolatedStringEndToken when i == tokens.Length - 2:
                    break;
                default:
                    if (holeDepth == 0)
                    {
                        // More than one literal: C# reports the rest as unexpected.
                        return "error";
                    }
                    break;
            }
        }
        return string.Join("|", parts.Where(part => part.Length > 0));
    }

    /// <summary>
    /// A raw string with random quotes, '$', indentation, blank lines and line breaks. Runs of quotes and braces are
    /// separated by letters so that they don't merge, and holes stay on one line (Nyxel doesn't allow line breaks
    /// in holes yet, C# does).
    /// </summary>
    private static string GenerateRawString(Random random)
    {
        var dollars = random.Next(4);
        var quotes = random.Next(3, 5);
        var text = new StringBuilder().Append('$', dollars).Append('"', quotes);
        if (random.Next(4) == 0)
        {
            text.Append(Content(random, dollars, quotes));
        }
        else
        {
            text.Append(Pick(random, "", " ", "  ")).Append(NewLine(random));
            var indentation = Pick(random, "", "  ", "    ", "\t");
            var lines = random.Next(0, 5);
            for (var i = 0; i < lines; i++)
            {
                text.Append(random.Next(5) == 0
                    ? Pick(random, "", " ", "  ", "      ", "\t", "  \t")
                    : indentation + Pick(random, "", "", " ", "  "));
                if (random.Next(5) != 0)
                {
                    text.Append(Content(random, dollars, quotes));
                }
                text.Append(NewLine(random));
            }
            text.Append(indentation);
        }
        return text.Append('"', quotes + (random.Next(10) == 0 ? 1 : 0)).ToString();
    }

    private static string Content(Random random, int dollars, int quotes)
    {
        var text = new StringBuilder();
        var pieces = random.Next(1, 5);
        for (var i = 0; i < pieces; i++)
        {
            text.Append(Pick(random, "a", "b c", "x\\n"));
            switch (random.Next(5))
            {
                case 0:
                    text.Append('"', random.Next(1, quotes + 1));
                    break;
                case 1 when dollars > 0:
                    // A well-formed hole, maybe with a format.
                    text.Append('{', dollars).Append('x').Append(random.Next(3) == 0 ? ":F2" : "").Append('}', dollars);
                    break;
                case 2 when dollars > 0:
                    // Brace runs around the boundaries: text, hole, too many.
                    text.Append('{', random.Next(1, (2 * dollars) + 1)).Append('x').Append('}', random.Next(dollars, (2 * dollars) + 1));
                    break;
                case 3:
                    text.Append('}', random.Next(1, Math.Max(dollars, 1) + 1));
                    break;
                case 1 or 2:
                    text.Append("{x}");
                    break;
                default:
                    text.Append("  ");
                    break;
            }
        }
        return text.ToString();
    }

    private static string NewLine(Random random) => random.Next(4) == 0 ? "\r\n" : "\n";

    private static string Pick(Random random, params string[] choices) => choices[random.Next(choices.Length)];

    private static string Show(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal);
}
