using Nyxel.Compiler.Diagnostics;

namespace Nyxel.Compiler.Syntax;

/// <summary>
/// Waiting for several things at once (ADR-0034): <c>await all { case ... }</c> and <c>await any { case ... }</c>.
/// Each case is <c>case [let name =] thing to wait for [-&gt; branch]</c>, one per line; the branches follow the
/// rules of match arms, and the block is a statement or a value the way a <c>match</c> is.
/// </summary>
internal sealed partial class Parser
{
    /// <summary>
    /// At <c>await</c>: <c>all</c> or <c>any</c> on the same line, then a '{'. Only there are they keywords, so
    /// <c>await all</c> on a coroutine named <c>all</c> still reads as an await.
    /// </summary>
    private bool IsAwaitGroupAhead() =>
        Peek(1) is { Kind: SyntaxKind.IdentifierToken, HasLeadingLineBreak: false } word
        && word.Text is SyntaxFacts.AllContextualKeyword or SyntaxFacts.AnyContextualKeyword
        && Peek(2).Kind == SyntaxKind.OpenBraceToken;

    private AwaitGroupExpressionSyntax ParseAwaitGroup()
    {
        var asStatement = _position == _statementStart;
        var errorsAtStart = ErrorCount;
        var awaitKeyword = NextToken();
        var groupKeyword = NextToken();
        var group = groupKeyword.Text;
        var open = ExpectOpenBrace();
        var saved = _bracketDepth;
        var savedHead = _headBracketDepth;
        _bracketDepth = 0;
        _headBracketDepth = -1;
        var cases = new List<AwaitCaseSyntax>();
        if (!open.IsMissing)
        {
            while (Current.Kind is not (SyntaxKind.CloseBraceToken or SyntaxKind.EndOfFileToken))
            {
                var start = _position;
                var errors = ErrorCount;
                AllowLineBreak();
                if (ParseAwaitCase(group, asStatement) is { } @case)
                {
                    cases.Add(@case);
                }
                ExpectEndOfStatement(errors, "case");
                if (_position == start)
                {
                    SkipCurrentToken();
                }
            }
        }
        var close = ExpectCloseBrace(open);
        _bracketDepth = saved;
        _headBracketDepth = savedHead;
        if (cases.Count < 2 && ErrorCount == errorsAtStart)
        {
            Report(DiagnosticDescriptors.AwaitGroupCaseCount, groupKeyword.Span, group);
        }
        var kind = group == SyntaxFacts.AllContextualKeyword ? SyntaxKind.AwaitAllExpression : SyntaxKind.AwaitAnyExpression;
        return new AwaitGroupExpressionSyntax(kind, awaitKeyword, groupKeyword, open, MakeList(cases), close);
    }

    /// <summary>
    /// <c>case let name = call -&gt; branch</c>. A line without <c>case</c> that still reads as one (a call, a
    /// <c>let</c>) is reported and read as a case; any other line is reported and skipped: null then.
    /// </summary>
    private AwaitCaseSyntax? ParseAwaitCase(string group, bool asStatement)
    {
        var errors = ErrorCount;
        SyntaxToken caseKeyword;
        if (Current.Kind == SyntaxKind.CaseKeyword)
        {
            caseKeyword = NextToken();
        }
        else
        {
            Report(DiagnosticDescriptors.AwaitCaseKeyword, Current.Span, group);
            if (Current.Kind is not (SyntaxKind.LetKeyword or SyntaxKind.VarKeyword)
                && (!CanStartExpression(Current.Kind) || IsBranchingStatementAhead()))
            {
                SkipToEndOfLine();
                return null;
            }
            caseKeyword = SyntaxToken.Missing(SyntaxKind.CaseKeyword, Current.FullSpan.Start);
        }

        SyntaxToken? keyword = null;
        SyntaxToken? identifier = null;
        DeconstructionSyntax? deconstruction = null;
        TypeAnnotationSyntax? typeAnnotation = null;
        SyntaxToken? equals = null;
        if (Current.Kind is SyntaxKind.LetKeyword or SyntaxKind.VarKeyword && !AtLineBreak)
        {
            // The same as a local declaration: a name with an optional type, or names to take a tuple apart.
            keyword = NextToken();
            if (At(SyntaxKind.OpenParenToken))
            {
                deconstruction = ParseDeconstruction();
            }
            else
            {
                identifier = ExpectIdentifier();
                typeAnnotation = TryParseTypeAnnotation();
            }
            equals = Expect(SyntaxKind.EqualsToken);
            if (!equals.IsMissing)
            {
                AllowLineBreak();
            }
        }

        var expression = ParseExpressionCore();
        if (TryEatAssignmentOperator() is { } assignment)
        {
            // case self.texture = Load(): which case finishes first isn't known. Keep the value as what the case
            // waits for; the target and the operator go into trivia.
            Report(DiagnosticDescriptors.AwaitCaseAssignment, assignment.Span);
            SkipConsumed([.. expression.DescendantTokens(), assignment]);
            AllowLineBreak();
            expression = ParseExpressionCore();
        }
        var awaitInCase = expression switch
        {
            UnaryExpressionSyntax { Kind: SyntaxKind.AwaitExpression } awaited => awaited.OperatorToken,
            AwaitGroupExpressionSyntax nested => nested.AwaitKeyword,
            _ => null,
        };
        if (awaitInCase != null)
        {
            Report(DiagnosticDescriptors.AwaitInCase, awaitInCase.Span, group);
        }

        SyntaxToken? arrow = null;
        SyntaxNode? body = null;
        if (At(SyntaxKind.MinusGreaterThanToken) || At(SyntaxKind.EqualsGreaterThanToken))
        {
            if (Current.Kind == SyntaxKind.EqualsGreaterThanToken)
            {
                Report(DiagnosticDescriptors.FatArrow, Current.Span);
            }
            arrow = NextToken();
            AllowLineBreak();
            body = ParseArmBody(asStatement, $"await {group}", "case");
        }
        else if (ErrorCount == errors && (identifier is { IsMissing: false } || deconstruction != null))
        {
            var span = identifier?.Span ?? deconstruction!.Span;
            Report(DiagnosticDescriptors.AwaitCaseNameWithoutBranch, span, identifier?.Text ?? deconstruction!.ToString(), group);
        }
        return new AwaitCaseSyntax(caseKeyword, keyword, identifier, deconstruction, typeAnnotation, equals, expression, arrow, body);
    }

    /// <summary>
    /// At <c>await</c> at the start of a statement: <c>await using</c> or <c>await for</c> follows on the same line
    /// (ADR-0034). Any other <c>await</c> starts an expression.
    /// </summary>
    private bool IsAwaitStatementAhead() =>
        Peek(1) is { Kind: SyntaxKind.UsingKeyword or SyntaxKind.ForKeyword, HasLeadingLineBreak: false };

    private StatementSyntax ParseAwaitStatement()
    {
        var awaitKeyword = NextToken();
        return Current.Kind == SyntaxKind.UsingKeyword ? ParseUsingDeclaration(awaitKeyword) : ParseForStatement(awaitKeyword);
    }

    /// <summary>
    /// <c>for await x in xs</c> (JavaScript, Swift): reported, and read as <c>await for</c> with a missing
    /// <c>await</c> in front of <c>for</c>; the written one goes into trivia.
    /// </summary>
    private SyntaxToken? ReadForAwait(SyntaxToken forKeyword, SyntaxToken? awaitKeyword)
    {
        if (!At(SyntaxKind.AwaitKeyword))
        {
            return awaitKeyword;
        }
        var name = Peek(1).Kind == SyntaxKind.IdentifierToken && !Peek(1).HasLeadingLineBreak ? Peek(1).Text : "x";
        Report(DiagnosticDescriptors.ForAwait, Current.Span, name);
        SkipCurrentToken();
        return awaitKeyword ?? SyntaxToken.Missing(SyntaxKind.AwaitKeyword, forKeyword.FullSpan.Start);
    }
}
