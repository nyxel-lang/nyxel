using System.Collections.Immutable;
using Nyxel.Compiler.Diagnostics;
using Nyxel.Compiler.Text;

namespace Nyxel.Compiler.Syntax;

/// <summary>
/// Recursive-descent parser: one method per construct, binary operators by precedence climbing. Never throws on bad
/// input: it reports a diagnostic, inserts a missing token or skips tokens into trivia, and goes on, so the tree
/// always covers the whole file. Design notes, including how the line rules work, are in
/// docs/design/syntax-tree.md.
/// </summary>
internal sealed partial class Parser
{
    private readonly DiagnosticBag _diagnostics;
    private readonly ImmutableArray<SyntaxToken> _tokens;
    private readonly List<SyntaxTrivia> _skipped = [];
    private int _position;
    private int _previousTokenEnd;
    private int _lastErrorPosition = -1;
    private int _badTokensSkipped;

    // Line rules (ADR-0013, ADR-0019). Inside ( ) and [ ] line breaks mean nothing; elsewhere a token on a new line
    // ends the construct being parsed, except where the grammar allows a line break before it: the start of a
    // statement or member, after '=' or '->', and operators at the start of a line, which continue it.
    private int _bracketDepth;
    private bool _lineBreakAllowed = true;

    private Parser(ImmutableArray<SyntaxToken> tokens, DiagnosticBag diagnostics)
    {
        _tokens = tokens;
        _diagnostics = diagnostics;
        SkipBadTokens();
    }

    public static CompilationUnitSyntax Parse(SourceText text, DiagnosticBag diagnostics)
    {
        var tokens = Lexer.Lex(text, diagnostics);
        var parser = new Parser(tokens, diagnostics);
        return parser.ParseCompilationUnit();
    }

    // Token stream ---------------------------------------------------------------------------------------------

    private SyntaxToken Current => _tokens[_position];

    private SyntaxToken Peek(int offset) => _tokens[Math.Min(_position + offset, _tokens.Length - 1)];

    private SyntaxToken NextToken()
    {
        var token = Current;
        if (token.Kind != SyntaxKind.EndOfFileToken)
        {
            _position++;
        }
        _lineBreakAllowed = false;
        _previousTokenEnd = token.Span.End;
        if (_skipped.Count > 0)
        {
            token = token.WithLeadingTrivia([.. _skipped, .. token.LeadingTrivia]);
            _skipped.Clear();
        }
        SkipBadTokens();
        return token;
    }

    /// <summary>
    /// Bad characters were reported by the lexer; ';' is reported here. Both are moved into trivia so the grammar
    /// never sees them.
    /// </summary>
    private void SkipBadTokens()
    {
        while (Current.Kind is SyntaxKind.BadToken or SyntaxKind.SemicolonToken)
        {
            if (Current.Kind == SyntaxKind.SemicolonToken)
            {
                Report(DiagnosticDescriptors.Semicolon, Current.Span);
            }
            else
            {
                _badTokensSkipped++;
            }
            SkipCurrentToken();
        }
    }

    /// <summary>
    /// Errors so far, counting bad tokens the lexer reported and the parser skipped. A statement that already has
    /// an error does not get a second "expected the end of the line" for the leftovers.
    /// </summary>
    private int ErrorCount => _diagnostics.Count + _badTokensSkipped;

    private void SkipCurrentToken()
    {
        var token = Current;
        if (token.Kind == SyntaxKind.EndOfFileToken)
        {
            return;
        }
        _skipped.Add(new SyntaxTrivia(SyntaxKind.SkippedTokensTrivia, token.FullSpan.Start, token.ToFullString()));
        _position++;
    }

    /// <summary>
    /// Moves already consumed tokens (a parsed node, or modifiers without a declaration) into skipped trivia. Any
    /// tokens skipped since then come later in the source, so these go in front of them.
    /// </summary>
    private void SkipNode(SyntaxNode node) => SkipConsumed(node.DescendantTokens());

    private void SkipConsumed(IEnumerable<SyntaxToken> tokens)
    {
        var trivia = tokens
            .Where(t => !t.IsMissing || t.LeadingTrivia.Length > 0)
            .Select(t => new SyntaxTrivia(SyntaxKind.SkippedTokensTrivia, t.FullSpan.Start, t.ToFullString()));
        _skipped.InsertRange(0, trivia);
    }

    // Line rules -----------------------------------------------------------------------------------------------

    /// <summary>True when the current token is on a new line where a line break ends the construct.</summary>
    private bool AtLineBreak => _bracketDepth == 0 && !_lineBreakAllowed && Current.HasLeadingLineBreak;

    /// <summary>The end of a statement or member: a new line, a '}' or the end of the file.</summary>
    private bool AtStatementEnd =>
        Current.Kind is SyntaxKind.CloseBraceToken or SyntaxKind.EndOfFileToken
        || (_bracketDepth == 0 && Current.HasLeadingLineBreak);

    /// <summary>Lets the current token start a new line: after '=' and '->', and at the start of a statement.</summary>
    private void AllowLineBreak() => _lineBreakAllowed = true;

    private bool At(SyntaxKind kind) => Current.Kind == kind && !AtLineBreak;

    private SyntaxToken? TryEat(SyntaxKind kind) => At(kind) ? NextToken() : null;

    private SyntaxToken Expect(SyntaxKind kind)
    {
        if (At(kind))
        {
            return NextToken();
        }
        ReportExpected(SyntaxFacts.GetDisplayText(kind));
        return Missing(kind);
    }

    private SyntaxToken ExpectIdentifier() => Expect(SyntaxKind.IdentifierToken);

    private SyntaxToken Missing(SyntaxKind kind)
    {
        _lineBreakAllowed = false;
        return SyntaxToken.Missing(kind, _previousTokenEnd);
    }

    /// <summary>
    /// After an operator: the operand must be on the same line, inside brackets too (ADR-0019). Reports and then
    /// reads the operand from the next line anyway when it can start an expression, which is almost always meant.
    /// </summary>
    private void CheckOperatorAtLineEnd(SyntaxToken operatorToken)
    {
        if (Current.HasLeadingLineBreak && Current.Kind != SyntaxKind.EndOfFileToken && !operatorToken.IsMissing)
        {
            Report(DiagnosticDescriptors.OperatorAtEndOfLine, operatorToken.Span, operatorToken.Text);
            if (CanStartExpression(Current.Kind))
            {
                AllowLineBreak();
            }
        }
    }

    /// <summary>
    /// Requires the end of the line after a statement or member. If more follows on the same line, reports it
    /// (unless this statement already has an error) and skips to the end of the line.
    /// </summary>
    private void ExpectEndOfStatement(int errorsAtStart, string what)
    {
        if (AtStatementEnd)
        {
            return;
        }
        if (ErrorCount == errorsAtStart)
        {
            Report(DiagnosticDescriptors.ExpectedLineBreak, Current.Span, what);
        }
        SkipToEndOfLine();
    }

    /// <summary>Skips tokens up to the end of the line, skipping bracketed groups whole (even across lines).</summary>
    private void SkipToEndOfLine()
    {
        var first = true;
        while (Current.Kind != SyntaxKind.EndOfFileToken && (first || !AtStatementEnd))
        {
            first = false;
            SkipBalanced();
        }
    }

    private void SkipBalanced()
    {
        var depth = 0;
        do
        {
            switch (Current.Kind)
            {
                case SyntaxKind.OpenBraceToken or SyntaxKind.OpenParenToken or SyntaxKind.OpenBracketToken:
                    depth++;
                    break;
                case SyntaxKind.CloseBraceToken or SyntaxKind.CloseParenToken or SyntaxKind.CloseBracketToken:
                    depth--;
                    break;
                case SyntaxKind.EndOfFileToken:
                    return;
            }
            SkipCurrentToken();
        }
        while (depth > 0);
    }

    // Diagnostics ----------------------------------------------------------------------------------------------

    /// <summary>Reports unless something was already reported at the same position (avoids cascades).</summary>
    private void Report(DiagnosticDescriptor descriptor, TextSpan span, params object[] args)
    {
        if (span.Start == _lastErrorPosition)
        {
            return;
        }
        _lastErrorPosition = span.Start;
        _diagnostics.Report(descriptor, span, args);
    }

    /// <summary>
    /// "Expected X": at the current token when it is on the same line, otherwise at the end of the previous token
    /// (the end of the line where X is missing). Where a line should start with X (a declaration, a case), at the
    /// current token.
    /// </summary>
    private void ReportExpected(string what, bool atLineStart = false)
    {
        if (Current.Kind != SyntaxKind.EndOfFileToken && (atLineStart || !Current.HasLeadingLineBreak))
        {
            Report(DiagnosticDescriptors.Expected, Current.Span, $"{what}, found {Describe(Current)}");
        }
        else
        {
            Report(DiagnosticDescriptors.Expected, new TextSpan(_previousTokenEnd, 0), what);
        }
    }

    private static string Describe(SyntaxToken token) => token.Kind switch
    {
        SyntaxKind.IdentifierToken => $"'{token.Text}'",
        SyntaxKind.NumericLiteralToken or SyntaxKind.StringLiteralToken => $"{token.Text}",
        SyntaxKind.EndOfFileToken => "the end of the file",
        _ => SyntaxFacts.GetDisplayText(token.Kind),
    };

    // Lists ----------------------------------------------------------------------------------------------------

    private static SyntaxList<T> MakeList<T>(List<T> items)
        where T : SyntaxNode => new([.. items]);

    private static SeparatedSyntaxList<T> Separated<T>(List<SyntaxNode> nodesAndSeparators)
        where T : SyntaxNode => new([.. nodesAndSeparators]);

    /// <summary>
    /// A comma-separated list inside brackets up to <paramref name="closeKind"/>. Stops early, leaving the close
    /// token missing, at a token that cannot continue the list and starts a new line with a statement keyword.
    /// </summary>
    private SeparatedSyntaxList<T> ParseSeparatedList<T>(SyntaxKind closeKind, Func<int, T> parseElement)
        where T : SyntaxNode
    {
        var items = new List<SyntaxNode>();
        while (Current.Kind != closeKind && Current.Kind != SyntaxKind.EndOfFileToken && !LooksLikeLineAfterMissingClose())
        {
            var start = _position;
            items.Add(parseElement(items.Count / 2));
            if (Current.Kind == SyntaxKind.CommaToken)
            {
                items.Add(NextToken());
                continue;
            }
            if (Current.Kind == closeKind || LooksLikeLineAfterMissingClose())
            {
                break;
            }
            ReportExpected($"',' or {SyntaxFacts.GetDisplayText(closeKind)}");
            if (_position == start || !CanStartExpression(Current.Kind))
            {
                break;
            }
        }
        return Separated<T>(items);
    }

    /// <summary>
    /// A '}' or a statement keyword at the start of a line inside brackets: most likely a ')' or ']' was forgotten.
    /// Ending the list there keeps the error to one line.
    /// </summary>
    private bool LooksLikeLineAfterMissingClose() =>
        Current.Kind == SyntaxKind.CloseBraceToken
        || (Current.HasLeadingLineBreak && IsStatementKeyword(Current.Kind));

    private static bool IsStatementKeyword(SyntaxKind kind) => kind is SyntaxKind.LetKeyword or SyntaxKind.VarKeyword
        or SyntaxKind.IfKeyword or SyntaxKind.WhileKeyword or SyntaxKind.ForKeyword or SyntaxKind.ReturnKeyword
        or SyntaxKind.ThrowKeyword or SyntaxKind.BreakKeyword or SyntaxKind.ContinueKeyword or SyntaxKind.EmitKeyword
        or SyntaxKind.UsingKeyword or SyntaxKind.FuncKeyword or SyntaxKind.PropertyKeyword or SyntaxKind.InitKeyword
        or SyntaxKind.EventKeyword or SyntaxKind.ClassKeyword or SyntaxKind.StructKeyword or SyntaxKind.EnumKeyword
        or SyntaxKind.InterfaceKeyword or SyntaxKind.ExtensionKeyword or SyntaxKind.PublicKeyword
        or SyntaxKind.ProtectedKeyword or SyntaxKind.PrivateKeyword or SyntaxKind.StaticKeyword
        or SyntaxKind.OverrideKeyword or SyntaxKind.CaseKeyword;
}
