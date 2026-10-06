using Nyxel.Compiler.Syntax;
using Nyxel.Compiler.Text;
using static Nyxel.Compiler.Tests.Syntax.SyntaxTestHelpers;

namespace Nyxel.Compiler.Tests.Syntax;

public class LexerTests
{
    private static List<SyntaxToken> Lex(string text, bool expectClean = true)
    {
        var tokens = SyntaxTree.ParseTokens(SourceText.From(text), out var diagnostics);
        if (expectClean)
        {
            Assert.True(diagnostics.IsEmpty, string.Join("\n", diagnostics));
        }
        Assert.Equal(SyntaxKind.EndOfFileToken, tokens[^1].Kind);
        Assert.Equal(text, string.Concat(tokens.Select(t => string.Concat(t.LeadingTrivia.Select(x => x.Text)) + t.Text + string.Concat(t.TrailingTrivia.Select(x => x.Text)))));
        return [.. tokens.SkipLast(1)];
    }

    private static SyntaxKind[] Kinds(string text) => [.. Lex(text).Select(t => t.Kind)];

    private static string[] LexDiagnostics(string text)
    {
        SyntaxTree.ParseTokens(SourceText.From(text), out var diagnostics);
        return [.. diagnostics.Select(Format)];
    }

    [Fact]
    public void EveryKeywordLexesToItsKind()
    {
        foreach (var keyword in SyntaxFacts.GetKeywordTexts())
        {
            var token = Assert.Single(Lex(keyword));
            Assert.Equal(SyntaxFacts.GetKeywordKind(keyword), token.Kind);
            Assert.Equal(keyword, SyntaxFacts.GetText(token.Kind));
        }
    }

    [Theory]
    [InlineData("get")]
    [InlineData("set")]
    [InlineData("from")]
    [InlineData("value")]
    [InlineData("_")]
    [InlineData("élan")]
    [InlineData("名字")]
    public void ContextualWordsAndUnicodeAreIdentifiers(string text) =>
        Assert.Equal(SyntaxKind.IdentifierToken, Assert.Single(Lex(text)).Kind);

    [Fact]
    public void OperatorsAreLexedLongestFirst()
    {
        Assert.Equal(
            [SyntaxKind.DotDotLessThanToken, SyntaxKind.DotDotDotToken, SyntaxKind.QuestionDotToken, SyntaxKind.QuestionQuestionToken,
             SyntaxKind.MinusGreaterThanToken, SyntaxKind.LessThanLessThanEqualsToken, SyntaxKind.LessThanEqualsToken,
             SyntaxKind.GreaterThanEqualsToken, SyntaxKind.ExclamationEqualsToken, SyntaxKind.EqualsEqualsToken,
             SyntaxKind.PlusEqualsToken, SyntaxKind.QuestionQuestionEqualsToken, SyntaxKind.QuestionToken],
            Kinds("..< ... ?. ?? -> <<= <= >= != == += ??= ?"));
    }

    [Fact]
    public void GreaterThanIsNeverCombinedSoGenericsCanClose()
    {
        // List<List<int>>: the parser composes '>>' for shifts when the two are adjacent.
        Assert.Equal(
            [SyntaxKind.IdentifierToken, SyntaxKind.LessThanToken, SyntaxKind.IdentifierToken, SyntaxKind.LessThanToken,
             SyntaxKind.IntKeyword, SyntaxKind.GreaterThanToken, SyntaxKind.GreaterThanToken],
            Kinds("List<List<int>>"));
    }

    [Fact]
    public void CSharpOperatorsAreKeptForTheParserToReport() =>
        Assert.Equal(
            [SyntaxKind.AmpersandAmpersandToken, SyntaxKind.BarBarToken, SyntaxKind.ExclamationToken, SyntaxKind.PlusPlusToken,
             SyntaxKind.MinusMinusToken, SyntaxKind.EqualsGreaterThanToken, SyntaxKind.SemicolonToken],
            Kinds("&& || ! ++ -- => ;"));

    [Theory]
    [InlineData("0", 0UL)]
    [InlineData("42", 42UL)]
    [InlineData("1_000_000", 1_000_000UL)]
    [InlineData("0xFF", 255UL)]
    [InlineData("0x_FF", 255UL)]
    [InlineData("0b1010", 10UL)]
    [InlineData("18446744073709551615", ulong.MaxValue)]
    public void IntegerLiterals(string text, ulong value)
    {
        var token = Assert.Single(Lex(text));
        Assert.Equal(SyntaxKind.NumericLiteralToken, token.Kind);
        Assert.Equal(value, token.Value);
    }

    [Theory]
    [InlineData("2.5", 2.5)]
    [InlineData("1e-3", 0.001)]
    [InlineData("1E3", 1000.0)]
    [InlineData("6.02e+23", 6.02e23)]
    [InlineData("1_000.5", 1000.5)]
    public void RealLiterals(string text, double value) => Assert.Equal(value, Assert.Single(Lex(text)).Value);

    [Fact]
    public void RangeAfterIntegerIsNotAFraction() =>
        Assert.Equal([SyntaxKind.NumericLiteralToken, SyntaxKind.DotDotDotToken, SyntaxKind.NumericLiteralToken], Kinds("1...3"));

    [Fact]
    public void MemberAccessOnIntegerIsNotAFraction() =>
        Assert.Equal([SyntaxKind.NumericLiteralToken, SyntaxKind.DotToken, SyntaxKind.IdentifierToken], Kinds("1.ToString"));

    [Theory]
    [InlineData("2.5f", "NYX1004 1:4")]
    [InlineData("10L", "NYX1004 1:3")]
    [InlineData("1_", "NYX1005 1:1")]
    [InlineData("0x", "NYX1005 1:1")]
    [InlineData("18446744073709551616", "NYX1006 1:1")]
    [InlineData(".5", "NYX1010 1:1")]
    public void BadNumbers(string text, string expected) => Assert.Equal([expected], LexDiagnostics(text));

    [Fact]
    public void CSharpRangeIsATokenOfItsOwn()
    {
        // The parser reports it, with the spelling that fits the place: '0..<5', or 'name[1...]' (ADR-0022).
        Assert.Equal([SyntaxKind.NumericLiteralToken, SyntaxKind.DotDotToken, SyntaxKind.NumericLiteralToken], Kinds("0..5"));
        Assert.Empty(LexDiagnostics("0..5"));
    }

    [Fact]
    public void StringEscapesAreDecoded()
    {
        var text = "\"a\\tb\\n\\\"q\\\" \\\\ \\u0041\\x42\\U0001F600\"";
        Assert.Equal("a\tb\n\"q\" \\ AB\U0001F600", Assert.Single(Lex(text)).Value);
    }

    [Theory]
    [InlineData("\"abc", "NYX1002 1:1")]
    [InlineData("\"a\\qb\"", "NYX1003 1:3")]
    [InlineData("\"\\u12\"", "NYX1003 1:2")]
    [InlineData("@\"x\"", "NYX1008 1:1")]
    [InlineData("$@\"x\"", "NYX1008 1:2")]
    [InlineData("@\"C:\\x \"\"q\"\"\"", "NYX1008 1:1")]
    [InlineData("$@\"C:\\x {a}\"", "NYX1008 1:2")]
    [InlineData("$\"\"\"{{x}}\"\"\"", "NYX1018 1:5")]
    [InlineData("''", "NYX1009 1:1")]
    [InlineData("'ab'", "NYX1009 1:1")]
    [InlineData("'a", "NYX1002 1:1")]
    [InlineData("\"\"\"a\nx", "NYX1002 1:1")]
    [InlineData("\"\"\"\n  a\n", "NYX1014 1:1")]
    [InlineData("\"\"\"a\"\"\"\"", "NYX1015 1:5")]
    [InlineData("\"\"\"\n    a\n  b\n    \"\"\"", "NYX1016 3:1")]
    [InlineData("\"\"\"\n    \"\"\"", "NYX1017 2:5")]
    [InlineData("$\"\"\"a } b\"\"\"", "NYX1018 1:7")]
    [InlineData("$$\"\"\"a {{x} b\"\"\"", "NYX1019 1:11")]
    [InlineData("$$\"\"\"\n    a {{x\n    \"\"\"", "NYX1019 2:7")]
    [InlineData("$$\"x\"", "NYX1020 1:1")]
    [InlineData("a # b", "NYX1001 1:3")]
    [InlineData("/* no */ x", "NYX1007 1:1")]
    public void BadStringsAndCharacters(string text, string expected) => Assert.Equal([expected], LexDiagnostics(text));

    [Theory]
    [InlineData("'a'", 'a')]
    [InlineData("'\\n'", '\n')]
    [InlineData("'\\''", '\'')]
    [InlineData("'\"'", '"')]
    [InlineData("'\\x41'", 'A')]
    [InlineData("'é'", 'é')]
    public void CharacterLiterals(string text, char value)
    {
        var token = Assert.Single(Lex(text));
        Assert.Equal(SyntaxKind.CharacterLiteralToken, token.Kind);
        Assert.Equal(value, token.Value);
    }

    [Fact]
    public void SingleLineRawStringHasNoEscapes()
    {
        var token = Assert.Single(Lex("\"\"\"C:\\Games\\save.json \"quoted\" ok\"\"\""));
        Assert.Equal(SyntaxKind.StringLiteralToken, token.Kind);
        Assert.Equal("C:\\Games\\save.json \"quoted\" ok", token.Value);
    }

    [Fact]
    public void MultiLineRawStringLeavesOutTheClosingIndentation()
    {
        var token = Assert.Single(Lex("\"\"\"\n    Usage:\n      spawn <kind>\n\n    \"\"\""));
        Assert.Equal("Usage:\n  spawn <kind>\n", token.Value);
    }

    [Fact]
    public void InterpolatedRawStringTakesAsManyBracesAsDollars()
    {
        var tokens = Lex("$$\"\"\"\n    { \"hp\": {{self.hp}} }\n    \"\"\"");
        Assert.Equal(
            [SyntaxKind.InterpolatedStringStartToken, SyntaxKind.InterpolatedStringTextToken, SyntaxKind.OpenBraceToken,
             SyntaxKind.SelfKeyword, SyntaxKind.DotToken, SyntaxKind.IdentifierToken, SyntaxKind.CloseBraceToken,
             SyntaxKind.InterpolatedStringTextToken, SyntaxKind.InterpolatedStringEndToken],
            tokens.Select(t => t.Kind));
        Assert.Equal("$$\"\"\"", tokens[0].Text);
        Assert.Equal("{ \"hp\": ", tokens[1].Value);
        Assert.Equal("{{", tokens[2].Text);
        Assert.Equal("}}", tokens[6].Text);
        Assert.Equal(" }", tokens[7].Value);
    }

    [Fact]
    public void BrokenHoleInAMultiLineRawStringKeepsTheStringOpen()
    {
        // The hole is closed with a missing '}', and the lines after it are still text.
        var tree = SyntaxTree.Parse(InMethod("let s = $\"\"\"\n    a {x\n    b\n    \"\"\"\nlet t = 1"));
        Assert.Equal(["NYX1019 4:7"], tree.Diagnostics.Select(Format));
        Assert.Equal(2, Body(tree).Statements.Count);
    }

    [Fact]
    public void InterpolatedStringIsSplitIntoTextAndHoles()
    {
        var tokens = Lex("$\"HP: {self.hp}/{max,5:F2} {{ok}}\"");
        Assert.Equal(
            [SyntaxKind.InterpolatedStringStartToken, SyntaxKind.InterpolatedStringTextToken,
             SyntaxKind.OpenBraceToken, SyntaxKind.SelfKeyword, SyntaxKind.DotToken, SyntaxKind.IdentifierToken, SyntaxKind.CloseBraceToken,
             SyntaxKind.InterpolatedStringTextToken,
             SyntaxKind.OpenBraceToken, SyntaxKind.IdentifierToken, SyntaxKind.CommaToken, SyntaxKind.NumericLiteralToken,
             SyntaxKind.ColonToken, SyntaxKind.InterpolationFormatToken, SyntaxKind.CloseBraceToken,
             SyntaxKind.InterpolatedStringTextToken, SyntaxKind.InterpolatedStringEndToken],
            tokens.Select(t => t.Kind));
        Assert.Equal("HP: ", tokens[1].Value);
        Assert.Equal("F2", tokens[13].Text);
        Assert.Equal(" {ok}", tokens[15].Value);
    }

    [Fact]
    public void TextAfterAHoleKeepsItsLeadingSpace()
    {
        var tokens = Lex("$\"{a} b\"");
        Assert.Equal(" b", tokens[4].Value);
        Assert.Empty(tokens[3].TrailingTrivia);
    }

    [Fact]
    public void FormatMayContainSemicolonsAndSigns()
    {
        var tokens = Lex("$\"{change:+0;-0} -> {x}\"");
        Assert.Equal("+0;-0", tokens.Single(t => t.Kind == SyntaxKind.InterpolationFormatToken).Text);
    }

    [Fact]
    public void NestedInterpolatedStrings()
    {
        var tokens = Lex("$\"a {F($\"b {c}\")} d\"");
        Assert.Equal(2, tokens.Count(t => t.Kind == SyntaxKind.InterpolatedStringStartToken));
        Assert.Equal(2, tokens.Count(t => t.Kind == SyntaxKind.InterpolatedStringEndToken));
        Assert.Equal(" d", tokens[^2].Value);
    }

    [Theory]
    [InlineData("$\"a {b\nx", "NYX1011 1:5")]
    [InlineData("$\"a\nx", "NYX1002 1:1")]
    [InlineData("$\"a } b\"", "NYX1012 1:5")]
    public void BrokenInterpolatedStrings(string text, string expected) => Assert.Equal([expected], LexDiagnostics(text));

    [Fact]
    public void LineBreaksAreLeadingTriviaAndFlagTheNextToken()
    {
        var tokens = Lex("a // c\n\n  b c");
        Assert.True(tokens[0].HasLeadingLineBreak, "the first token starts a line");
        Assert.Equal([SyntaxKind.WhitespaceTrivia, SyntaxKind.LineCommentTrivia], tokens[0].TrailingTrivia.Select(t => t.Kind));
        Assert.True(tokens[1].HasLeadingLineBreak);
        Assert.Equal(
            [SyntaxKind.EndOfLineTrivia, SyntaxKind.EndOfLineTrivia, SyntaxKind.WhitespaceTrivia],
            tokens[1].LeadingTrivia.Select(t => t.Kind));
        Assert.False(tokens[2].HasLeadingLineBreak);
    }

    [Theory]
    [InlineData("/// doc\nx", SyntaxKind.DocCommentTrivia)]
    [InlineData("//// not doc\nx", SyntaxKind.LineCommentTrivia)]
    [InlineData("// plain\nx", SyntaxKind.LineCommentTrivia)]
    public void DocCommentsAreExactlyThreeSlashes(string text, SyntaxKind kind) =>
        Assert.Equal(kind, Lex(text)[0].LeadingTrivia[0].Kind);

    [Fact]
    public void CrLfIsOneLineBreak()
    {
        var tokens = Lex("a\r\nb");
        Assert.Equal("\r\n", tokens[1].LeadingTrivia.Single().Text);
        Assert.Equal(new LinePosition(1, 0), SourceText.From("a\r\nb").GetLinePosition(tokens[1].Position));
    }
}
