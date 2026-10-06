using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Nyxel.Compiler.Diagnostics;
using Nyxel.Compiler.Text;

namespace Nyxel.Compiler.Syntax;

/// <summary>
/// Turns source text into tokens with leading and trailing trivia. Line breaks are trivia; a token only records
/// whether a line break precedes it (<see cref="SyntaxToken.HasLeadingLineBreak"/>) and the parser applies the line
/// rules. Trailing trivia stops before the line break, so the break always leads the next token.
/// </summary>
internal sealed class Lexer
{
    private readonly SourceText _text;
    private readonly DiagnosticBag _diagnostics;
    private readonly ImmutableArray<SyntaxToken>.Builder _tokens = ImmutableArray.CreateBuilder<SyntaxToken>();

    // Interpolated strings nest: $"a {f($"b {c}")} d". Each open string pushes a Text mode, each '{' a Hole.
    private readonly Stack<Mode> _modes = new();
    private int _position;

    private Lexer(SourceText text, DiagnosticBag diagnostics)
    {
        _text = text;
        _diagnostics = diagnostics;
    }

    public static ImmutableArray<SyntaxToken> Lex(SourceText text, DiagnosticBag diagnostics)
    {
        var lexer = new Lexer(text, diagnostics);
        lexer.Run();
        return lexer._tokens.ToImmutable();
    }

    private enum ModeKind
    {
        Text,
        Hole,
        Format,
    }

    private sealed class Mode(ModeKind kind, int start)
    {
        public ModeKind Kind { get; set; } = kind;

        /// <summary>Where the string or the hole began, for "not closed" diagnostics.</summary>
        public int Start { get; } = start;

        /// <summary>Open ( [ { inside a hole; ':' and '}' only end the hole at depth 0.</summary>
        public int Depth { get; set; }

        /// <summary>Braces that open and close a hole: the number of '$' before a raw string, otherwise 1.</summary>
        public int Braces { get; init; } = 1;

        /// <summary>The raw string this text or hole belongs to; null in $"...".</summary>
        public RawString? Raw { get; init; }

        /// <summary>A reported $@"...": read without escapes so that its '\' cause no more errors.</summary>
        public bool IsVerbatim { get; init; }
    }

    /// <summary>A raw string being read (ADR-0020): the quotes that delimit it and whether it spans lines.</summary>
    private sealed class RawString(int quotes, bool isMultiLine, int contentStart)
    {
        public int Quotes { get; } = quotes;

        public bool IsMultiLine { get; } = isMultiLine;

        /// <summary>Just after the opening quotes.</summary>
        public int ContentStart { get; } = contentStart;

        /// <summary>Indices in <see cref="_tokens"/> of the text tokens, whose values are set when the string closes.</summary>
        public List<int> TextTokens { get; } = [];

        /// <summary>Braces the string can't contain are reported once: $"""{{x}}""" is one mistake, not two.</summary>
        public bool ReportedBraces { get; set; }
    }

    private enum RawStop
    {
        Close,
        Hole,
        End,
    }

    private const string QuoteName = "quote (\")";

    private char Current => Peek(0);

    private char Peek(int offset)
    {
        var index = _position + offset;
        return index < _text.Length ? _text[index] : '\0';
    }

    private bool AtEnd => _position >= _text.Length;

    private void Run()
    {
        while (true)
        {
            if (_modes.TryPeek(out var mode) && mode.Kind != ModeKind.Hole)
            {
                if (mode.Kind == ModeKind.Text && mode.Raw != null)
                {
                    LexRawInterpolatedText(mode, mode.Raw);
                }
                else if (mode.Kind == ModeKind.Text)
                {
                    LexInterpolatedText(mode);
                }
                else
                {
                    LexInterpolationFormat();
                }
                continue;
            }

            var sawLineBreak = _tokens.Count == 0;
            var leading = LexTrivia(leading: mode == null, ref sawLineBreak);
            if (mode != null && (AtEnd || Current is '\r' or '\n'))
            {
                AbandonInterpolatedString();
                continue;
            }

            var start = _position;
            var (kind, value) = LexToken();
            var text = _text.ToString(TextSpan.FromBounds(start, _position));

            // A token that opened a string ('$"'), closed a hole ('}') or started a format (':') is followed by
            // string content, not trivia: in $"{a} b" the space belongs to the text.
            var noLineBreak = false;
            var trailing = _modes.TryPeek(out var after) && after.Kind != ModeKind.Hole
                ? []
                : LexTrivia(leading: false, ref noLineBreak);
            _tokens.Add(new SyntaxToken(kind, start, text, value, leading, trailing, sawLineBreak));
            if (kind == SyntaxKind.EndOfFileToken)
            {
                break;
            }
        }
    }

    // Trivia ---------------------------------------------------------------------------------------------------

    private ImmutableArray<SyntaxTrivia> LexTrivia(bool leading, ref bool sawLineBreak)
    {
        var trivia = ImmutableArray.CreateBuilder<SyntaxTrivia>();
        while (!AtEnd)
        {
            var start = _position;
            var c = Current;
            SyntaxKind kind;
            if (c is '\r' or '\n')
            {
                if (!leading)
                {
                    break;
                }
                _position += c == '\r' && Peek(1) == '\n' ? 2 : 1;
                kind = SyntaxKind.EndOfLineTrivia;
                sawLineBreak = true;
            }
            else if (IsWhitespace(c))
            {
                while (!AtEnd && IsWhitespace(Current))
                {
                    _position++;
                }
                kind = SyntaxKind.WhitespaceTrivia;
            }
            else if (c == '/' && Peek(1) == '/')
            {
                // Exactly three slashes is a doc comment (ADR-0007); four or more is an ordinary comment, as in C#.
                kind = Peek(2) == '/' && Peek(3) != '/' ? SyntaxKind.DocCommentTrivia : SyntaxKind.LineCommentTrivia;
                while (!AtEnd && Current is not ('\r' or '\n'))
                {
                    _position++;
                }
            }
            else if (c == '/' && Peek(1) == '*')
            {
                _position += 2;
                while (!AtEnd && !(Current == '*' && Peek(1) == '/'))
                {
                    _position++;
                }
                _position = Math.Min(_position + 2, _text.Length);
                kind = SyntaxKind.BlockCommentTrivia;
                _diagnostics.Report(DiagnosticDescriptors.BlockComment, new TextSpan(start, 2));
                if (leading && _text.ToString(TextSpan.FromBounds(start, _position)).AsSpan().ContainsAny('\r', '\n'))
                {
                    sawLineBreak = true;
                }
            }
            else
            {
                break;
            }
            trivia.Add(new SyntaxTrivia(kind, start, _text.ToString(TextSpan.FromBounds(start, _position))));
        }
        return trivia.ToImmutable();
    }

    private static bool IsWhitespace(char c) =>
        c is ' ' or '\t' or '\v' or '\f' or (char)0xFEFF || (c > 127 && char.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator);

    // Tokens ---------------------------------------------------------------------------------------------------

    private (SyntaxKind Kind, object? Value) LexToken()
    {
        if (AtEnd)
        {
            return (SyntaxKind.EndOfFileToken, null);
        }

        var c = Current;
        if (char.IsAsciiDigit(c))
        {
            return LexNumber();
        }
        if (IsIdentifierStart(c))
        {
            return LexIdentifierOrKeyword();
        }

        var start = _position;
        _position++;
        switch (c)
        {
            case '{':
                EnterBracket();
                return (SyntaxKind.OpenBraceToken, null);
            case '}':
                if (_modes.TryPeek(out var hole) && hole.Kind == ModeKind.Hole && hole.Depth == 0)
                {
                    CloseHole(hole, start);
                }
                else
                {
                    LeaveBracket();
                }
                return (SyntaxKind.CloseBraceToken, null);
            case '(':
                EnterBracket();
                return (SyntaxKind.OpenParenToken, null);
            case ')':
                LeaveBracket();
                return (SyntaxKind.CloseParenToken, null);
            case '[':
                EnterBracket();
                return (SyntaxKind.OpenBracketToken, null);
            case ']':
                LeaveBracket();
                return (SyntaxKind.CloseBracketToken, null);
            case ',':
                return (SyntaxKind.CommaToken, null);
            case ':':
                if (_modes.TryPeek(out var mode) && mode.Kind == ModeKind.Hole && mode.Depth == 0)
                {
                    mode.Kind = ModeKind.Format;
                }
                return (SyntaxKind.ColonToken, null);
            case ';':
                return (SyntaxKind.SemicolonToken, null);
            case '~':
                return (SyntaxKind.TildeToken, null);
            case '.':
                if (Current == '.' && Peek(1) == '<')
                {
                    _position += 2;
                    return (SyntaxKind.DotDotLessThanToken, null);
                }
                if (Current == '.' && Peek(1) == '.')
                {
                    _position += 2;
                    return (SyntaxKind.DotDotDotToken, null);
                }
                if (Current == '.')
                {
                    // C#'s '..': the parser reports it with what to write in its place (ADR-0022).
                    _position++;
                    return (SyntaxKind.DotDotToken, null);
                }
                if (char.IsAsciiDigit(Current))
                {
                    _position = start;
                    return LexNumber();
                }
                return (SyntaxKind.DotToken, null);
            case '?':
                if (Current == '.' && !char.IsAsciiDigit(Peek(1)))
                {
                    _position++;
                    return (SyntaxKind.QuestionDotToken, null);
                }
                if (Current == '?' && Peek(1) == '=')
                {
                    _position += 2;
                    return (SyntaxKind.QuestionQuestionEqualsToken, null);
                }
                return Choose('?', SyntaxKind.QuestionQuestionToken, SyntaxKind.QuestionToken);
            case '-':
                if (Current == '>')
                {
                    _position++;
                    return (SyntaxKind.MinusGreaterThanToken, null);
                }
                if (Current == '-')
                {
                    _position++;
                    return (SyntaxKind.MinusMinusToken, null);
                }
                return Choose('=', SyntaxKind.MinusEqualsToken, SyntaxKind.MinusToken);
            case '+':
                if (Current == '+')
                {
                    _position++;
                    return (SyntaxKind.PlusPlusToken, null);
                }
                return Choose('=', SyntaxKind.PlusEqualsToken, SyntaxKind.PlusToken);
            case '=':
                if (Current == '>')
                {
                    _position++;
                    return (SyntaxKind.EqualsGreaterThanToken, null);
                }
                return Choose('=', SyntaxKind.EqualsEqualsToken, SyntaxKind.EqualsToken);
            case '!':
                return Choose('=', SyntaxKind.ExclamationEqualsToken, SyntaxKind.ExclamationToken);
            case '<':
                if (Current == '<')
                {
                    _position++;
                    return Choose('=', SyntaxKind.LessThanLessThanEqualsToken, SyntaxKind.LessThanLessThanToken);
                }
                return Choose('=', SyntaxKind.LessThanEqualsToken, SyntaxKind.LessThanToken);
            case '>':
                // '>>' is composed by the parser so that List<List<int>> closes two type argument lists.
                return Choose('=', SyntaxKind.GreaterThanEqualsToken, SyntaxKind.GreaterThanToken);
            case '*':
                return Choose('=', SyntaxKind.AsteriskEqualsToken, SyntaxKind.AsteriskToken);
            case '/':
                return Choose('=', SyntaxKind.SlashEqualsToken, SyntaxKind.SlashToken);
            case '%':
                return Choose('=', SyntaxKind.PercentEqualsToken, SyntaxKind.PercentToken);
            case '^':
                return Choose('=', SyntaxKind.CaretEqualsToken, SyntaxKind.CaretToken);
            case '&':
                if (Current == '&')
                {
                    _position++;
                    return (SyntaxKind.AmpersandAmpersandToken, null);
                }
                return Choose('=', SyntaxKind.AmpersandEqualsToken, SyntaxKind.AmpersandToken);
            case '|':
                if (Current == '|')
                {
                    _position++;
                    return (SyntaxKind.BarBarToken, null);
                }
                return Choose('=', SyntaxKind.BarEqualsToken, SyntaxKind.BarToken);
            case '"':
                _position = start;
                return LexString();
            case '$' when Current is '"' or '$' || (Current == '@' && Peek(1) == '"'):
                return LexInterpolatedStringStart(start);
            case '@' when Current == '"' || (Current == '$' && Peek(1) == '"'):
                _diagnostics.Report(DiagnosticDescriptors.VerbatimString, new TextSpan(start, 1));
                if (Current == '$')
                {
                    _position += 2;
                    _modes.Push(new Mode(ModeKind.Text, start) { IsVerbatim = true });
                    return (SyntaxKind.InterpolatedStringStartToken, null);
                }
                return LexVerbatimString(start);
            case '\'':
                return LexCharacterLiteral(start);
            default:
                return LexBadCharacters(start);
        }
    }

    private (SyntaxKind, object?) Choose(char next, SyntaxKind ifNext, SyntaxKind otherwise)
    {
        if (Current == next)
        {
            _position++;
            return (ifNext, null);
        }
        return (otherwise, null);
    }

    private void EnterBracket()
    {
        if (_modes.TryPeek(out var mode) && mode.Kind == ModeKind.Hole)
        {
            mode.Depth++;
        }
    }

    private void LeaveBracket()
    {
        if (_modes.TryPeek(out var mode) && mode.Kind == ModeKind.Hole && mode.Depth > 0)
        {
            mode.Depth--;
        }
    }

    /// <summary>
    /// The first '}' closing a hole has been read. After <c>$$"""</c> a hole closes with '}}', as many braces as
    /// there are '$' (ADR-0020); too few are reported and the hole is closed anyway.
    /// </summary>
    private void CloseHole(Mode hole, int braceStart)
    {
        _modes.Pop();
        for (var i = 1; i < hole.Braces; i++)
        {
            if (Current != '}')
            {
                _diagnostics.Report(DiagnosticDescriptors.RawStringHoleClose, TextSpan.FromBounds(braceStart, _position), new string('}', hole.Braces));
                return;
            }
            _position++;
        }
    }

    private (SyntaxKind, object?) LexBadCharacters(int start)
    {
        while (!AtEnd && !IsIdentifierStart(Current) && !char.IsAsciiDigit(Current) && !IsWhitespace(Current)
            && Current is not ('\r' or '\n') && !IsTokenStart(Current))
        {
            _position++;
        }
        var text = _text.ToString(TextSpan.FromBounds(start, _position));
        _diagnostics.Report(DiagnosticDescriptors.UnexpectedCharacter, new TextSpan(start, text.Length), text);
        return (SyntaxKind.BadToken, null);
    }

    private static bool IsTokenStart(char c) => "{}()[],.:;~?-+=!<>*/%^&|\"$@'".Contains(c);

    private static bool IsIdentifierStart(char c) =>
        c == '_' || char.IsAsciiLetter(c) || (c > 127 && char.IsLetter(c))
        || (c > 127 && char.GetUnicodeCategory(c) == UnicodeCategory.LetterNumber);

    private static bool IsIdentifierPart(char c)
    {
        if (c == '_' || char.IsAsciiLetterOrDigit(c))
        {
            return true;
        }
        if (c <= 127)
        {
            return false;
        }
        return char.GetUnicodeCategory(c) is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
            or UnicodeCategory.LetterNumber or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.ConnectorPunctuation or UnicodeCategory.Format;
    }

    private (SyntaxKind, object?) LexIdentifierOrKeyword()
    {
        var start = _position;
        while (!AtEnd && IsIdentifierPart(Current))
        {
            _position++;
        }
        var text = _text.ToString(TextSpan.FromBounds(start, _position));
        return (SyntaxFacts.GetKeywordKind(text), null);
    }

    // Numbers --------------------------------------------------------------------------------------------------

    private (SyntaxKind, object?) LexNumber()
    {
        var start = _position;
        var isReal = false;
        var valid = true;
        var radix = 10;

        if (Current == '.')
        {
            // '.5': read it as 0.5 so parsing can go on, and say how to write it.
            _position++;
            ScanDigits(char.IsAsciiDigit, ref valid);
            ScanExponent(ref valid);
            var text = _text.ToString(TextSpan.FromBounds(start, _position));
            _diagnostics.Report(DiagnosticDescriptors.LeadingDotNumber, TextSpan.FromBounds(start, _position), text);
            return (SyntaxKind.NumericLiteralToken, ParseReal("0" + text));
        }

        if (Current == '0' && Peek(1) is 'x' or 'X')
        {
            _position += 2;
            radix = 16;
            if (ScanDigits(char.IsAsciiHexDigit, ref valid) == 0)
            {
                valid = false;
            }
        }
        else if (Current == '0' && Peek(1) is 'b' or 'B')
        {
            _position += 2;
            radix = 2;
            if (ScanDigits(c => c is '0' or '1', ref valid) == 0)
            {
                valid = false;
            }
        }
        else
        {
            ScanDigits(char.IsAsciiDigit, ref valid);
            if (Current == '.' && char.IsAsciiDigit(Peek(1)))
            {
                isReal = true;
                _position++;
                ScanDigits(char.IsAsciiDigit, ref valid);
            }
            isReal |= ScanExponent(ref valid);
        }

        var numberEnd = _position;
        var numberText = _text.ToString(TextSpan.FromBounds(start, numberEnd));

        // A suffix such as 2.5f or 10L: part of the token, reported, ignored for the value.
        if (IsIdentifierPart(Current))
        {
            while (!AtEnd && IsIdentifierPart(Current))
            {
                _position++;
            }
            var suffix = _text.ToString(TextSpan.FromBounds(numberEnd, _position));
            _diagnostics.Report(DiagnosticDescriptors.NumericSuffix, TextSpan.FromBounds(numberEnd, _position), suffix);
        }

        if (!valid)
        {
            _diagnostics.Report(DiagnosticDescriptors.InvalidNumber, TextSpan.FromBounds(start, numberEnd), numberText);
            return (SyntaxKind.NumericLiteralToken, isReal ? 0.0 : 0UL);
        }
        if (isReal)
        {
            return (SyntaxKind.NumericLiteralToken, ParseReal(numberText));
        }

        var digits = numberText.Replace("_", "", StringComparison.Ordinal);
        if (radix != 10)
        {
            digits = digits[2..];
        }
        if (!TryParseInteger(digits, radix, out var value))
        {
            _diagnostics.Report(DiagnosticDescriptors.IntegerTooLarge, TextSpan.FromBounds(start, numberEnd), numberText);
        }
        return (SyntaxKind.NumericLiteralToken, value);
    }

    /// <summary>Reads digits and '_' separators. A separator may not end the digits (C#: 1_000 yes, 1_ no).</summary>
    private int ScanDigits(Func<char, bool> isDigit, ref bool valid)
    {
        var count = 0;
        var lastWasSeparator = false;
        while (!AtEnd && (isDigit(Current) || Current == '_'))
        {
            lastWasSeparator = Current == '_';
            if (!lastWasSeparator)
            {
                count++;
            }
            _position++;
        }
        if (lastWasSeparator)
        {
            valid = false;
        }
        return count;
    }

    private bool ScanExponent(ref bool valid)
    {
        if (Current is not ('e' or 'E'))
        {
            return false;
        }
        var signLength = Peek(1) is '+' or '-' ? 1 : 0;
        if (!char.IsAsciiDigit(Peek(1 + signLength)))
        {
            return false;
        }
        _position += 1 + signLength;
        ScanDigits(char.IsAsciiDigit, ref valid);
        return true;
    }

    private static double ParseReal(string text) =>
        double.Parse(text.Replace("_", "", StringComparison.Ordinal), NumberStyles.Float, CultureInfo.InvariantCulture);

    private static bool TryParseInteger(string digits, int radix, out ulong value)
    {
        value = 0;
        foreach (var c in digits)
        {
            var digit = (ulong)(char.IsAsciiDigit(c) ? c - '0' : char.ToLowerInvariant(c) - 'a' + 10);
            if (value > (ulong.MaxValue - digit) / (ulong)radix)
            {
                value = 0;
                return false;
            }
            value = (value * (ulong)radix) + digit;
        }
        return true;
    }

    // Strings --------------------------------------------------------------------------------------------------

    private (SyntaxKind, object?) LexString()
    {
        var start = _position;
        if (Peek(1) == '"' && Peek(2) == '"')
        {
            return LexRawString(start);
        }
        _position++;
        var value = new StringBuilder();
        while (true)
        {
            if (AtEnd || Current is '\r' or '\n')
            {
                _diagnostics.Report(DiagnosticDescriptors.UnterminatedString, TextSpan.FromBounds(start, _position), QuoteName);
                break;
            }
            if (Current == '"')
            {
                _position++;
                break;
            }
            if (Current == '\\')
            {
                LexEscapeSequence(value);
                continue;
            }
            value.Append(Current);
            _position++;
        }
        return (SyntaxKind.StringLiteralToken, value.ToString());
    }

    /// <summary>'a', '\n': one UTF-16 character, with the escapes of strings (ADR-0020).</summary>
    private (SyntaxKind, object?) LexCharacterLiteral(int start)
    {
        var errors = _diagnostics.Count;
        var value = new StringBuilder();
        while (!AtEnd && Current is not ('\'' or '\r' or '\n'))
        {
            if (Current == '\\')
            {
                LexEscapeSequence(value);
                continue;
            }
            value.Append(Current);
            _position++;
        }
        if (Current == '\'')
        {
            _position++;
            if (value.Length != 1 && _diagnostics.Count == errors)
            {
                _diagnostics.Report(DiagnosticDescriptors.CharacterLiteralLength, TextSpan.FromBounds(start, _position));
            }
        }
        else
        {
            _diagnostics.Report(DiagnosticDescriptors.UnterminatedString, TextSpan.FromBounds(start, _position), "quote (')");
        }
        return (SyntaxKind.CharacterLiteralToken, value.Length > 0 ? value[0] : '\0');
    }

    /// <summary>
    /// <c>$"</c>, or <c>$"""</c> / <c>$$"""</c> opening an interpolated raw string whose holes take as many braces as
    /// there are '$' (ADR-0020). The text that follows is read in a <see cref="ModeKind.Text"/> mode.
    /// </summary>
    private (SyntaxKind, object?) LexInterpolatedStringStart(int start)
    {
        var dollars = 1;
        while (Current == '$')
        {
            dollars++;
            _position++;
        }
        var isVerbatim = Current == '@';
        if (isVerbatim)
        {
            _diagnostics.Report(DiagnosticDescriptors.VerbatimString, new TextSpan(_position, 1));
            _position++;
        }
        if (Current != '"')
        {
            _position = start + 1;
            return LexBadCharacters(start);
        }

        var quotes = CountRun('"');
        if (quotes >= 3)
        {
            _position += quotes;
            var raw = new RawString(quotes, RestOfLineIsWhitespace(), _position);
            _modes.Push(new Mode(ModeKind.Text, start) { Braces = dollars, Raw = raw });
            return (SyntaxKind.InterpolatedStringStartToken, null);
        }
        if (dollars > 1)
        {
            _diagnostics.Report(DiagnosticDescriptors.DollarsWithoutRawString, new TextSpan(start, dollars));
        }
        _position++;
        _modes.Push(new Mode(ModeKind.Text, start) { IsVerbatim = isVerbatim });
        return (SyntaxKind.InterpolatedStringStartToken, null);
    }

    /// <summary>
    /// A reported @"...", read as C# reads it (no escapes, "" for a quote, line breaks allowed) so that its '\'
    /// cause no more errors.
    /// </summary>
    private (SyntaxKind, object?) LexVerbatimString(int start)
    {
        _position++;
        var value = new StringBuilder();
        while (true)
        {
            if (AtEnd)
            {
                _diagnostics.Report(DiagnosticDescriptors.UnterminatedString, TextSpan.FromBounds(start, _position), QuoteName);
                break;
            }
            if (Current == '"')
            {
                _position++;
                if (Current != '"')
                {
                    break;
                }
            }
            value.Append(Current);
            _position++;
        }
        return (SyntaxKind.StringLiteralToken, value.ToString());
    }

    // Raw strings (ADR-0020): the same as C# 11. ------------------------------------------------------------------

    /// <summary>
    /// <c>"""text"""</c> on one line, or <c>"""</c>, a line break, lines of text and the closing quotes on a line of
    /// their own. More quotes delimit text that contains quotes. A multi-line string's value leaves out the
    /// whitespace before its closing quotes from every line.
    /// </summary>
    private (SyntaxKind, object?) LexRawString(int start)
    {
        var quotes = CountRun('"');
        _position += quotes;
        var raw = new RawString(quotes, RestOfLineIsWhitespace(), _position);
        var stop = ScanRawContent(raw, braces: 0, out var closeLength);
        var contentEnd = _position;
        if (stop != RawStop.Close)
        {
            ReportUnterminatedRawString(raw, start);
            return (SyntaxKind.StringLiteralToken, _text.ToString(TextSpan.FromBounds(raw.ContentStart, contentEnd)));
        }

        _position += closeLength;
        ReportExtraClosingQuotes(raw, contentEnd, closeLength);
        var value = raw.IsMultiLine
            ? MultiLineRawValues(raw, contentEnd, [TextSpan.FromBounds(raw.ContentStart, contentEnd)])[0]
            : _text.ToString(TextSpan.FromBounds(raw.ContentStart, contentEnd));
        return (SyntaxKind.StringLiteralToken, value);
    }

    /// <summary>Text of an interpolated raw string, then the '{' of a hole or the closing quotes.</summary>
    private void LexRawInterpolatedText(Mode mode, RawString raw)
    {
        var start = _position;
        var stop = ScanRawContent(raw, mode.Braces, out var closeLength);
        if (_position > start)
        {
            // The value of a multi-line string's text is set when the string closes and its indentation is known.
            raw.TextTokens.Add(_tokens.Count);
            AddToken(SyntaxKind.InterpolatedStringTextToken, start, _text.ToString(TextSpan.FromBounds(start, _position)));
        }

        var noLineBreak = false;
        switch (stop)
        {
            case RawStop.Hole:
                {
                    var braceStart = _position;
                    _position += mode.Braces;
                    var text = _text.ToString(TextSpan.FromBounds(braceStart, _position));
                    var trailing = LexTrivia(leading: false, ref noLineBreak);
                    _tokens.Add(new SyntaxToken(SyntaxKind.OpenBraceToken, braceStart, text, null, [], trailing, false));
                    _modes.Push(new Mode(ModeKind.Hole, braceStart) { Braces = mode.Braces, Raw = raw });
                    return;
                }
            case RawStop.Close:
                {
                    _modes.Pop();
                    var quoteStart = _position;
                    _position += closeLength;
                    ReportExtraClosingQuotes(raw, quoteStart, closeLength);
                    var text = _text.ToString(TextSpan.FromBounds(quoteStart, _position));
                    var trailing = LexTrivia(leading: false, ref noLineBreak);
                    _tokens.Add(new SyntaxToken(SyntaxKind.InterpolatedStringEndToken, quoteStart, text, null, [], trailing, false));
                    if (raw.IsMultiLine)
                    {
                        SetMultiLineTextValues(raw, quoteStart);
                    }
                    return;
                }
            default:
                _modes.Pop();
                ReportUnterminatedRawString(raw, mode.Start);
                _tokens.Add(SyntaxToken.Missing(SyntaxKind.InterpolatedStringEndToken, _position));
                return;
        }
    }

    /// <summary>
    /// Reads raw string text up to the closing quotes, the braces that open a hole (when <paramref name="braces"/> is
    /// positive: an interpolated string with that many '$') or where the string can't go on. Runs of quotes or
    /// braces the string can't contain are reported and read as text, as C# does.
    /// </summary>
    private RawStop ScanRawContent(RawString raw, int braces, out int closeLength)
    {
        closeLength = 0;
        while (true)
        {
            if (AtEnd || (!raw.IsMultiLine && Current is '\r' or '\n'))
            {
                return RawStop.End;
            }
            var c = Current;
            if (c == '"')
            {
                var run = CountRun('"');
                if (run >= raw.Quotes)
                {
                    if (!raw.IsMultiLine || IsFirstOnLine(_position))
                    {
                        closeLength = run;
                        return RawStop.Close;
                    }
                    // C# ends the string here; reading on keeps the lines after it from turning into code.
                    _diagnostics.Report(DiagnosticDescriptors.RawStringQuotes, new TextSpan(_position, run), raw.Quotes, run);
                }
                _position += run;
            }
            else if (braces > 0 && c == '{')
            {
                var run = CountRun('{');
                if (run >= braces)
                {
                    // The braces before the last 'braces' ones are text: in $$"""{{{x}}}""" the value is "{" + x + "}".
                    if (run >= 2 * braces)
                    {
                        ReportRawStringBraces(raw, run - braces, '{');
                    }
                    _position += run - braces;
                    return RawStop.Hole;
                }
                _position += run;
            }
            else if (braces > 0 && c == '}')
            {
                var run = CountRun('}');
                if (run >= braces)
                {
                    ReportRawStringBraces(raw, run, '}');
                }
                _position += run;
            }
            else
            {
                _position++;
            }
        }
    }

    private void ReportRawStringBraces(RawString raw, int count, char brace)
    {
        if (!raw.ReportedBraces)
        {
            raw.ReportedBraces = true;
            _diagnostics.Report(DiagnosticDescriptors.RawStringBraces, new TextSpan(_position, count), new string(brace, count), new string('$', count + 1));
        }
    }

    private void ReportExtraClosingQuotes(RawString raw, int quoteStart, int closeLength)
    {
        if (closeLength > raw.Quotes)
        {
            _diagnostics.Report(DiagnosticDescriptors.RawStringQuotes, new TextSpan(quoteStart, closeLength), raw.Quotes, closeLength);
        }
    }

    private void ReportUnterminatedRawString(RawString raw, int start)
    {
        var quotes = new string('"', raw.Quotes);
        if (raw.IsMultiLine)
        {
            _diagnostics.Report(DiagnosticDescriptors.UnterminatedRawString, TextSpan.FromBounds(start, raw.ContentStart), quotes);
        }
        else
        {
            _diagnostics.Report(DiagnosticDescriptors.UnterminatedString, TextSpan.FromBounds(start, _position), $"quotes ({quotes})");
        }
    }

    private void SetMultiLineTextValues(RawString raw, int closeStart)
    {
        var segments = raw.TextTokens.Select(i => _tokens[i].Span).ToList();
        var values = MultiLineRawValues(raw, closeStart, segments);
        for (var i = 0; i < values.Length; i++)
        {
            var token = _tokens[raw.TextTokens[i]];
            _tokens[raw.TextTokens[i]] = new SyntaxToken(
                token.Kind, token.Position, token.Text, values[i], token.LeadingTrivia, token.TrailingTrivia, token.HasLeadingLineBreak);
        }
    }

    /// <summary>
    /// The values of the text segments of a closed multi-line raw string. Left out: the rest of the opening line,
    /// the line break before the closing line, and from every line the whitespace before the closing quotes. A
    /// line that doesn't start with that whitespace is reported, unless it is a blank line shorter than it (then
    /// it becomes empty).
    /// </summary>
    private string[] MultiLineRawValues(RawString raw, int closeStart, List<TextSpan> segments)
    {
        // Line breaks of the string's own text; holes are on one line and their nested strings are not looked at.
        var breaks = new List<TextSpan>();
        foreach (var segment in segments)
        {
            for (var i = segment.Start; i < segment.End; i++)
            {
                if (_text[i] is '\r' or '\n')
                {
                    var length = _text[i] == '\r' && i + 1 < segment.End && _text[i + 1] == '\n' ? 2 : 1;
                    breaks.Add(new TextSpan(i, length));
                    i += length - 1;
                }
            }
        }

        // The closing quotes are first on their line, so there is at least the opening line's break.
        var skipped = new List<TextSpan> { TextSpan.FromBounds(raw.ContentStart, breaks[0].End) };
        if (breaks.Count == 1)
        {
            _diagnostics.Report(DiagnosticDescriptors.EmptyRawString, new TextSpan(closeStart, raw.Quotes));
            skipped.Add(TextSpan.FromBounds(breaks[0].End, closeStart));
        }
        else
        {
            var indentation = _text.ToString(TextSpan.FromBounds(breaks[^1].End, closeStart));
            for (var i = 0; i < breaks.Count - 1; i++)
            {
                SkipIndentation(TextSpan.FromBounds(breaks[i].End, breaks[i + 1].Start), indentation, skipped);
            }
            skipped.Add(TextSpan.FromBounds(breaks[^1].Start, closeStart));
        }

        var values = new string[segments.Count];
        var next = 0;
        for (var s = 0; s < segments.Count; s++)
        {
            var value = new StringBuilder();
            for (var i = segments[s].Start; i < segments[s].End; i++)
            {
                while (next < skipped.Count && skipped[next].End <= i)
                {
                    next++;
                }
                if (next == skipped.Count || i < skipped[next].Start)
                {
                    value.Append(_text[i]);
                }
            }
            values[s] = value.ToString();
        }
        return values;
    }

    private void SkipIndentation(TextSpan line, string indentation, List<TextSpan> skipped)
    {
        var text = _text.ToString(line);
        if (text.StartsWith(indentation, StringComparison.Ordinal))
        {
            skipped.Add(new TextSpan(line.Start, indentation.Length));
        }
        else if (text.All(IsWhitespace) && indentation.StartsWith(text, StringComparison.Ordinal))
        {
            skipped.Add(line);
        }
        else
        {
            var whitespace = text.TakeWhile(IsWhitespace).Count();
            _diagnostics.Report(DiagnosticDescriptors.RawStringIndentation, new TextSpan(line.Start, Math.Max(whitespace, 1)));
        }
    }

    private int CountRun(char c)
    {
        var count = 0;
        while (Peek(count) == c)
        {
            count++;
        }
        return count;
    }

    /// <summary>Only whitespace up to the end of the line: the opening quotes start a multi-line raw string.</summary>
    private bool RestOfLineIsWhitespace()
    {
        var i = _position;
        while (i < _text.Length && IsWhitespace(_text[i]))
        {
            i++;
        }
        return i == _text.Length || _text[i] is '\r' or '\n';
    }

    private bool IsFirstOnLine(int position)
    {
        var i = position - 1;
        while (i >= 0 && IsWhitespace(_text[i]))
        {
            i--;
        }
        return i < 0 || _text[i] is '\r' or '\n';
    }

    /// <summary>The escape sequences of C# (ADR-0007). Appends the decoded character; invalid ones are reported.</summary>
    private void LexEscapeSequence(StringBuilder value)
    {
        var start = _position;
        _position++;
        var c = Current;
        if (AtEnd || c is '\r' or '\n')
        {
            _diagnostics.Report(DiagnosticDescriptors.InvalidEscapeSequence, new TextSpan(start, 1), "\\");
            return;
        }
        _position++;
        switch (c)
        {
            case '\'': value.Append('\''); return;
            case '"': value.Append('"'); return;
            case '\\': value.Append('\\'); return;
            case '0': value.Append('\0'); return;
            case 'a': value.Append('\a'); return;
            case 'b': value.Append('\b'); return;
            case 'e': value.Append('\u001b'); return;
            case 'f': value.Append('\f'); return;
            case 'n': value.Append('\n'); return;
            case 'r': value.Append('\r'); return;
            case 't': value.Append('\t'); return;
            case 'v': value.Append('\v'); return;
            case 'u':
                AppendHexEscape(value, start, minDigits: 4, maxDigits: 4);
                return;
            case 'U':
                AppendHexEscape(value, start, minDigits: 8, maxDigits: 8);
                return;
            case 'x':
                AppendHexEscape(value, start, minDigits: 1, maxDigits: 4);
                return;
            default:
                _diagnostics.Report(DiagnosticDescriptors.InvalidEscapeSequence, TextSpan.FromBounds(start, _position), _text.ToString(TextSpan.FromBounds(start, _position)));
                return;
        }
    }

    private void AppendHexEscape(StringBuilder value, int start, int minDigits, int maxDigits)
    {
        var digitsStart = _position;
        while (_position - digitsStart < maxDigits && char.IsAsciiHexDigit(Current))
        {
            _position++;
        }
        var digits = _text.ToString(TextSpan.FromBounds(digitsStart, _position));
        if (digits.Length < minDigits || !uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code) || code > 0x10FFFF)
        {
            _diagnostics.Report(DiagnosticDescriptors.InvalidEscapeSequence, TextSpan.FromBounds(start, _position), _text.ToString(TextSpan.FromBounds(start, _position)));
            return;
        }
        value.Append(char.ConvertFromUtf32((int)code));
    }

    // Interpolated strings -------------------------------------------------------------------------------------

    /// <summary>Text between '$"' / '}' and the next '{' / '"'. Emits text, then '{' (entering a hole) or the end.</summary>
    private void LexInterpolatedText(Mode mode)
    {
        var start = _position;
        var value = new StringBuilder();
        while (!AtEnd && Current is not ('\r' or '\n'))
        {
            if (Current == '"' && mode.IsVerbatim && Peek(1) == '"')
            {
                value.Append('"');
                _position += 2;
            }
            else if (Current == '"')
            {
                break;
            }
            else if (Current == '{' && Peek(1) == '{')
            {
                value.Append('{');
                _position += 2;
            }
            else if (Current == '{')
            {
                break;
            }
            else if (Current == '}')
            {
                if (Peek(1) == '}')
                {
                    _position++;
                }
                else
                {
                    _diagnostics.Report(DiagnosticDescriptors.UnescapedCloseBrace, new TextSpan(_position, 1));
                }
                value.Append('}');
                _position++;
            }
            else if (Current == '\\' && !mode.IsVerbatim)
            {
                LexEscapeSequence(value);
            }
            else
            {
                value.Append(Current);
                _position++;
            }
        }
        if (_position > start)
        {
            AddToken(SyntaxKind.InterpolatedStringTextToken, start, value.ToString());
        }

        if (Current == '{')
        {
            var braceStart = _position;
            _position++;
            var noLineBreak = false;
            var trailing = LexTrivia(leading: false, ref noLineBreak);
            _tokens.Add(new SyntaxToken(SyntaxKind.OpenBraceToken, braceStart, "{", null, [], trailing, false));
            _modes.Push(new Mode(ModeKind.Hole, braceStart));
        }
        else if (Current == '"')
        {
            _modes.Pop();
            var quoteStart = _position;
            _position++;
            var noLineBreak = false;
            var trailing = LexTrivia(leading: false, ref noLineBreak);
            _tokens.Add(new SyntaxToken(SyntaxKind.InterpolatedStringEndToken, quoteStart, "\"", null, [], trailing, false));
        }
        else
        {
            _diagnostics.Report(DiagnosticDescriptors.UnterminatedString, TextSpan.FromBounds(mode.Start, _position), QuoteName);
            _modes.Pop();
            _tokens.Add(SyntaxToken.Missing(SyntaxKind.InterpolatedStringEndToken, _position));
        }
    }

    /// <summary>The format after ':' in a hole, up to '}'.</summary>
    private void LexInterpolationFormat()
    {
        var hole = _modes.Peek();
        var start = _position;
        while (!AtEnd && Current is not ('}' or '\r' or '\n') && !(Current == '"' && hole.Raw == null))
        {
            _position++;
        }
        if (_position > start)
        {
            AddToken(SyntaxKind.InterpolationFormatToken, start, null);
        }
        if (Current == '}')
        {
            var braceStart = _position;
            _position++;
            CloseHole(hole, braceStart);
            _tokens.Add(new SyntaxToken(SyntaxKind.CloseBraceToken, braceStart, _text.ToString(TextSpan.FromBounds(braceStart, _position)), null, [], [], false));
        }
        else if (Current == '"')
        {
            // $"{x:F2" -- close the hole and let the text mode end the string at the quote.
            _modes.Pop();
            _diagnostics.Report(DiagnosticDescriptors.UnclosedInterpolation, new TextSpan(hole.Start, 1));
            _tokens.Add(SyntaxToken.Missing(SyntaxKind.CloseBraceToken, _position));
        }
        else
        {
            AbandonInterpolatedString();
        }
    }

    /// <summary>
    /// A hole or format ran into the end of the line: report the open '{', close the hole and the string with
    /// missing tokens so the parser does not report them again, and go back to normal lexing. A multi-line raw
    /// string stays open and goes on with the next line.
    /// </summary>
    private void AbandonInterpolatedString()
    {
        var hole = _modes.Pop();
        if (hole.Raw != null)
        {
            _diagnostics.Report(DiagnosticDescriptors.RawStringHoleClose, new TextSpan(hole.Start, hole.Braces), new string('}', hole.Braces));
        }
        else
        {
            _diagnostics.Report(DiagnosticDescriptors.UnclosedInterpolation, new TextSpan(hole.Start, 1));
        }
        _tokens.Add(SyntaxToken.Missing(SyntaxKind.CloseBraceToken, _position));
        if (hole.Raw is { IsMultiLine: true })
        {
            return;
        }
        if (_modes.TryPop(out _))
        {
            _tokens.Add(SyntaxToken.Missing(SyntaxKind.InterpolatedStringEndToken, _position));
        }
    }

    private void AddToken(SyntaxKind kind, int start, object? value) =>
        _tokens.Add(new SyntaxToken(kind, start, _text.ToString(TextSpan.FromBounds(start, _position)), value, [], [], false));
}
