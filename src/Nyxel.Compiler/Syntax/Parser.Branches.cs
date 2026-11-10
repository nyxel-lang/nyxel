using Nyxel.Compiler.Diagnostics;

namespace Nyxel.Compiler.Syntax;

/// <summary>
/// <c>if</c>, <c>match</c>, <c>try</c> and <c>eval</c> (ADR-0025, ADR-0026, ADR-0043). A <c>{ }</c> block never has a
/// value: <c>if</c> and <c>try</c> with blocks are statements, and give a value in the forms
/// <c>if c then a else b</c> and <c>try a catch e: T -&gt; b</c> instead. Each branch of those is one expression or a
/// leave (<c>return</c>, <c>throw</c>, <c>break</c>, <c>continue</c>, <c>give</c>); statements that compute a value
/// go in <c>eval { ... give value }</c>. A <c>match</c> used as a statement may also have a simple statement or a
/// block after <c>-&gt;</c>.
/// </summary>
internal sealed partial class Parser
{
    // The value form of an 'if' or 'try' only stands at the start of a whole expression, and not directly as a
    // branch of another one: its last branch runs to the end of the expression, so elsewhere it is not clear where it
    // ends (ADR-0025). ParseExpressionCore and ParseBranchValue record where those start.
    private int _expressionStart = -1;
    private int _branchValueStart = -1;

    // Where the statement being parsed starts: a 'match' there is a statement, anywhere else a value.
    private int _statementStart = -1;

    // The second half of NYX1123 when the match or 'await' block is a statement.
    private const string ArmStatementInBraces = "put it in braces: '-> { ... }'";

    /// <summary>How an <c>if</c> or a <c>try</c> that gives a value is written, for messages.</summary>
    private static string ValueFormExample(string keyword) =>
        keyword == "if" ? "if condition then a else b" : "try a catch e: Exception -> b";

    /// <summary><c>if</c> or <c>try</c> at the start of a statement: the form with blocks.</summary>
    private StatementSyntax ParseBranchingStatement()
    {
        var keyword = Current;
        var node = keyword.Kind == SyntaxKind.IfKeyword ? ParseIf(asValue: false) : ParseTry(asValue: false);
        if (node is StatementSyntax statement)
        {
            return statement;
        }
        var example = keyword.Kind == SyntaxKind.IfKeyword ? "if condition { ... }" : "try { ... } catch e: Exception { ... }";
        Report(DiagnosticDescriptors.ArrowBranchesAsStatement, keyword.Span, keyword.Text, example);
        return new ExpressionStatementSyntax((ExpressionSyntax)node);
    }

    /// <summary>
    /// <c>if</c> or <c>try</c> where a value is needed: the form without blocks. The form with blocks is reported
    /// and skipped.
    /// </summary>
    private ExpressionSyntax ParseBranchingValue()
    {
        var keyword = Current;
        var needsParentheses = _position != _expressionStart || _position == _branchValueStart;
        var node = keyword.Kind == SyntaxKind.IfKeyword ? ParseIf(asValue: true) : ParseTry(asValue: true);
        if (node is ExpressionSyntax expression)
        {
            if (needsParentheses)
            {
                Report(DiagnosticDescriptors.ArrowBranchesNeedParentheses, keyword.Span, keyword.Text);
            }
            return expression;
        }
        Report(DiagnosticDescriptors.BlockBranchesAsValue, keyword.Span, keyword.Text, ValueFormExample(keyword.Text));
        return SkipToMissingExpression(node);
    }

    /// <summary>Moves a parsed node into skipped trivia and returns a missing expression in its place.</summary>
    private ExpressionSyntax SkipToMissingExpression(SyntaxNode node)
    {
        SkipNode(node);
        return new IdentifierNameSyntax(SyntaxToken.Missing(SyntaxKind.IdentifierToken, node.FullSpan.Start));
    }

    // if ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>if</c> in either form, decided by what follows the condition: <c>{</c> for the statement, <c>then</c> for
    /// the value (ADR-0043; a <c>then</c> may start the next line, and the <c>-&gt;</c> written here before is
    /// reported). When neither follows, the form the place needs. Branches written the other way are reported.
    /// </summary>
    private SyntaxNode ParseIf(bool asValue)
    {
        var errors = ErrorCount;
        var ifKeyword = NextToken();
        var condition = ParseHeadExpression();
        if (!asValue && IsThenBeforeBlock(0))
        {
            // 'if c then { ... }': the statement, written with the 'then' of the value form. The '{' already ends the
            // condition, so the statement has no 'then' (ADR-0043); it is reported and skipped.
            Report(DiagnosticDescriptors.ThenInIfStatement, Current.Span);
            SkipCurrentToken();
            return ParseIfStatementRest(ifKeyword, condition);
        }
        return Current.Kind == SyntaxKind.ThenKeyword || At(SyntaxKind.MinusGreaterThanToken) || (asValue && Current.Kind != SyntaxKind.OpenBraceToken)
            ? ParseIfExpressionRest(ifKeyword, condition, errors)
            : ParseIfStatementRest(ifKeyword, condition);
    }

    private IfStatementSyntax ParseIfStatementRest(SyntaxToken ifKeyword, ExpressionSyntax condition)
    {
        var block = ParseBlock();
        ElseClauseSyntax? elseClause = null;
        if (Current.Kind == SyntaxKind.ElseKeyword)
        {
            // Nothing can start a line with 'else', so one on the next line still belongs here; it is reported.
            ReportIfOnNextLine();
            var elseKeyword = NextToken();
            StatementSyntax body;
            if (Current.Kind == SyntaxKind.IfKeyword && !Current.HasLeadingLineBreak)
            {
                var next = ParseIf(asValue: false);
                body = next as StatementSyntax ?? MixedValueInStatement((IfExpressionSyntax)next);
            }
            else if (Current.Kind != SyntaxKind.OpenBraceToken && !AtLineBreak && CanStartBranchValue())
            {
                body = ParseValueBranchInStatement("if");
            }
            else
            {
                body = ParseBlock();
            }
            elseClause = new ElseClauseSyntax(elseKeyword, body);
        }
        return new IfStatementSyntax(ifKeyword, condition, block, elseClause);
    }

    /// <summary><c>else if c then ...</c> after branches in braces: reported, and kept in a block with missing braces.</summary>
    private BlockSyntax MixedValueInStatement(IfExpressionSyntax next)
    {
        var at = next.ThenKeyword.IsMissing ? next.Value.Span : next.ThenKeyword.Span;
        Report(DiagnosticDescriptors.MixedBranches, at, "if", ValueFormExample("if"));
        return InMissingBraces(new ExpressionStatementSyntax(next));
    }

    /// <summary>
    /// A value where a branch in braces should be (<c>} else b</c>, <c>} catch e: E -&gt; b</c>): reported, and read
    /// into a block with missing braces, an arrow before it skipped.
    /// </summary>
    private BlockSyntax ParseValueBranchInStatement(string keyword)
    {
        Report(DiagnosticDescriptors.MixedBranches, Current.Span, keyword, ValueFormExample(keyword));
        if (At(SyntaxKind.MinusGreaterThanToken))
        {
            SkipCurrentToken();
            AllowLineBreak();
        }
        return InMissingBraces(new ExpressionStatementSyntax(ParseBranchValue(keyword)));
    }

    /// <summary>On the line: something a branch value can start with, counting the '-&gt;' written before one until ADR-0043.</summary>
    private bool CanStartBranchValue() => Current.Kind is SyntaxKind.MinusGreaterThanToken or SyntaxKind.ReturnKeyword
        or SyntaxKind.ThrowKeyword or SyntaxKind.BreakKeyword or SyntaxKind.ContinueKeyword or SyntaxKind.GiveKeyword
        || CanStartExpression(Current.Kind);

    private static BlockSyntax InMissingBraces(StatementSyntax statement) => new(
        SyntaxToken.Missing(SyntaxKind.OpenBraceToken, statement.FullSpan.Start),
        MakeList<StatementSyntax>([statement]),
        SyntaxToken.Missing(SyntaxKind.CloseBraceToken, statement.FullSpan.End));

    /// <summary>The rest of an <c>if</c> that gives a value. A missing <c>else</c> is only reported when the rest had no error.</summary>
    private IfExpressionSyntax ParseIfExpressionRest(SyntaxToken ifKeyword, ExpressionSyntax condition, int errorsAtStart)
    {
        var thenKeyword = ExpectThen();
        var value = ParseBranchValue("if");
        SyntaxToken elseKeyword;
        ExpressionSyntax elseValue;
        // 'else' continues the 'if' on the same line or at the start of the next (ADR-0025). Nothing else starts
        // with 'else': the default arm of a match is 'case _' (ADR-0026).
        if (Current.Kind == SyntaxKind.ElseKeyword)
        {
            elseKeyword = NextToken();
            if (Current.Kind == SyntaxKind.IfKeyword && !Current.HasLeadingLineBreak)
            {
                var next = ParseIf(asValue: true);
                if (next is IfStatementSyntax statement)
                {
                    Report(DiagnosticDescriptors.MixedBranches, statement.Block.OpenBraceToken.Span, "if");
                    elseValue = SkipToMissingExpression(statement);
                }
                else
                {
                    elseValue = (ExpressionSyntax)next;
                }
            }
            else
            {
                elseValue = ParseElseValue(elseKeyword);
            }
        }
        else
        {
            if (ErrorCount == errorsAtStart)
            {
                Report(DiagnosticDescriptors.IfValueWithoutElse, ifKeyword.Span);
            }
            elseKeyword = Missing(SyntaxKind.ElseKeyword);
            elseValue = MissingExpression();
        }
        return new IfExpressionSyntax(ifKeyword, condition, thenKeyword, value, elseKeyword, elseValue);
    }

    /// <summary>
    /// The <c>then</c> of an <c>if</c> that gives a value (ADR-0043). Like an operator it may start a line but not end
    /// one (ADR-0019). A <c>-&gt;</c> in its place, the form until ADR-0043, is reported and skipped.
    /// </summary>
    private SyntaxToken ExpectThen()
    {
        if (Current.Kind == SyntaxKind.ThenKeyword)
        {
            var thenKeyword = NextToken();
            CheckOperatorAtLineEnd(thenKeyword);
            return thenKeyword;
        }
        if (TrySkipValueArrow("if", "Write 'then' instead of this '->'"))
        {
            return Missing(SyntaxKind.ThenKeyword);
        }
        return Expect(SyntaxKind.ThenKeyword);
    }

    /// <summary>The value after the <c>else</c> of an <c>if</c> that gives a value. A block in its place is reported and skipped.</summary>
    private ExpressionSyntax ParseElseValue(SyntaxToken elseKeyword)
    {
        if (Current.Kind == SyntaxKind.OpenBraceToken && !AtLineBreak)
        {
            Report(DiagnosticDescriptors.MixedBranches, Current.Span, "if", ValueFormExample("if"));
            return SkipToMissingExpression(ParseBlock());
        }
        if (!TrySkipValueArrow("if"))
        {
            CheckOperatorAtLineEnd(elseKeyword);
        }
        return ParseBranchValue("if");
    }

    /// <summary>
    /// A <c>-&gt;</c> where <c>if c -&gt; a else -&gt; b</c> and <c>try -&gt; a</c> had one until ADR-0043: each is
    /// reported with what to do about it and skipped; the value may follow on the next line as it could then.
    /// </summary>
    private bool TrySkipValueArrow(string keyword, string fix = "Remove this '->'")
    {
        if (!At(SyntaxKind.MinusGreaterThanToken))
        {
            return false;
        }
        Report(DiagnosticDescriptors.ArrowInValueBranch, Current.Span, fix, keyword == "if" ? "an 'if'" : "a 'try'", ValueFormExample(keyword));
        SkipCurrentToken();
        AllowLineBreak();
        return true;
    }

    /// <summary>
    /// <c>-&gt; value</c> after the <c>catch</c> of a <c>try</c> that gives a value. A block in its place is reported
    /// and skipped.
    /// </summary>
    private (SyntaxToken Arrow, ExpressionSyntax Value) ParseArrowBranch(string keyword)
    {
        if (Current.Kind == SyntaxKind.OpenBraceToken && !AtLineBreak)
        {
            Report(DiagnosticDescriptors.MixedBranches, Current.Span, keyword, ValueFormExample(keyword));
            var block = ParseBlock();
            return (SyntaxToken.Missing(SyntaxKind.MinusGreaterThanToken, block.FullSpan.Start), SkipToMissingExpression(block));
        }
        return (ExpectArrow(), ParseBranchValue(keyword));
    }

    /// <summary>A '-&gt;'; the line may end after it (ADR-0013).</summary>
    private SyntaxToken ExpectArrow()
    {
        var arrow = Expect(SyntaxKind.MinusGreaterThanToken);
        if (!arrow.IsMissing)
        {
            AllowLineBreak();
        }
        return arrow;
    }

    /// <summary>
    /// A branch of an <c>if</c> or <c>try</c> that gives a value: a leave, or an expression. A block written there
    /// (<c>then { ... }</c>) is reported and skipped: statements that compute a value go in <c>eval</c>.
    /// </summary>
    private ExpressionSyntax ParseBranchValue(string keyword)
    {
        if (TryParseLeave() is { } leave)
        {
            return leave;
        }
        if (Current.Kind == SyntaxKind.OpenBraceToken && !AtLineBreak)
        {
            Report(DiagnosticDescriptors.BlockBranchesAsValue, Current.Span, keyword, ValueFormExample(keyword));
            return SkipToMissingExpression(ParseBlock());
        }
        _branchValueStart = _position;
        return ParseExpression();
    }

    /// <summary>
    /// <c>return</c>, <c>throw</c>, <c>break</c>, <c>continue</c> or <c>give</c> where a value would be: on the right
    /// of <c>??</c> (ADR-0009) and as a branch (ADR-0025). Null when none of them is here.
    /// </summary>
    private JumpExpressionSyntax? TryParseLeave()
    {
        var kind = Current.Kind switch
        {
            SyntaxKind.ReturnKeyword => SyntaxKind.ReturnExpression,
            SyntaxKind.ThrowKeyword => SyntaxKind.ThrowExpression,
            SyntaxKind.BreakKeyword => SyntaxKind.BreakExpression,
            SyntaxKind.ContinueKeyword => SyntaxKind.ContinueExpression,
            SyntaxKind.GiveKeyword => SyntaxKind.GiveExpression,
            _ => SyntaxKind.None,
        };
        if (kind == SyntaxKind.None || AtLineBreak)
        {
            return null;
        }
        var keyword = NextToken();
        ExpressionSyntax? value = null;
        if (kind is SyntaxKind.ThrowExpression or SyntaxKind.GiveExpression)
        {
            value = ParseExpressionCore();
        }
        else if (kind == SyntaxKind.ReturnExpression && !AtStatementEnd && CanStartExpression(Current.Kind))
        {
            value = ParseExpressionCore();
        }
        return new JumpExpressionSyntax(kind, keyword, value);
    }

    /// <summary><c>else</c>, <c>catch</c> and <c>finally</c> go on the line of the '}' before them (ADR-0013).</summary>
    private void ReportIfOnNextLine()
    {
        if (Current.HasLeadingLineBreak)
        {
            Report(DiagnosticDescriptors.KeywordOnNextLine, Current.Span, Current.Text);
        }
    }

    /// <summary>A <c>then</c> with a <c>{</c> right after it on the same line: the <c>then</c> of a statement, which has none.</summary>
    private bool IsThenBeforeBlock(int offset) =>
        Peek(offset).Kind == SyntaxKind.ThenKeyword && Peek(offset + 1) is { Kind: SyntaxKind.OpenBraceToken, HasLeadingLineBreak: false };

    /// <summary>
    /// At <c>if</c>: whether a <c>then</c> (or the <c>-&gt;</c> written in its place until ADR-0043) comes before a
    /// <c>{</c> outside brackets, making it the <c>if</c> that gives a value. <c>then {</c> is the statement with a
    /// <c>then</c> too many. Scans ahead without building nodes.
    /// </summary>
    private bool IsValueIfAhead()
    {
        var depth = 0;
        for (var offset = 1; ; offset++)
        {
            switch (Peek(offset).Kind)
            {
                case SyntaxKind.OpenParenToken or SyntaxKind.OpenBracketToken:
                    depth++;
                    break;
                case SyntaxKind.CloseParenToken or SyntaxKind.CloseBracketToken:
                    if (--depth < 0)
                    {
                        return false;
                    }
                    break;
                case SyntaxKind.ThenKeyword when depth == 0:
                    return !IsThenBeforeBlock(offset);
                case SyntaxKind.MinusGreaterThanToken when depth == 0:
                    return true;
                case SyntaxKind.OpenBraceToken or SyntaxKind.CloseBraceToken when depth == 0:
                    return false;
                case SyntaxKind.EndOfFileToken:
                    return false;
            }
        }
    }

    /// <summary>An <c>if</c> or <c>try</c> statement (with blocks) starting here.</summary>
    private bool IsBranchingStatementAhead() =>
        (Current.Kind == SyntaxKind.IfKeyword && !IsValueIfAhead())
        || (Current.Kind == SyntaxKind.TryKeyword && Peek(1).Kind == SyntaxKind.OpenBraceToken);

    // match ------------------------------------------------------------------------------------------------------

    private MatchExpressionSyntax ParseMatchExpression()
    {
        var asStatement = _position == _statementStart;
        var matchKeyword = NextToken();
        var expression = ParseHeadExpression();
        var open = ExpectOpenBrace();
        var saved = _bracketDepth;
        var savedHead = _headBracketDepth;
        _bracketDepth = 0;
        _headBracketDepth = -1;
        var arms = new List<MatchArmSyntax>();
        if (!open.IsMissing)
        {
            while (Current.Kind is not (SyntaxKind.CloseBraceToken or SyntaxKind.EndOfFileToken))
            {
                var start = _position;
                var errors = ErrorCount;
                AllowLineBreak();
                if (Current.Kind is SyntaxKind.CaseKeyword or SyntaxKind.ElseKeyword)
                {
                    arms.Add(ParseMatchArm(asStatement));
                }
                else
                {
                    ReportExpected("'case'", atLineStart: true);
                    SkipToEndOfLine();
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
        return new MatchExpressionSyntax(matchKeyword, expression, open, MakeList(arms), close);
    }

    /// <summary><c>case pattern -&gt; body</c>; the default arm is <c>case _</c>. An <c>else</c> arm is reported and read as one (ADR-0026).</summary>
    private MatchArmSyntax ParseMatchArm(bool asStatement)
    {
        var keyword = NextToken();
        PatternSyntax pattern;
        if (keyword.Kind == SyntaxKind.ElseKeyword)
        {
            Report(DiagnosticDescriptors.MatchElse, keyword.Span);
            pattern = new DiscardPatternSyntax(Missing(SyntaxKind.UnderscoreToken));
        }
        else
        {
            pattern = ParsePattern();
            while (At(SyntaxKind.CommaToken))
            {
                // case 1, 2 -- C#'s list; read it as 'or'.
                Report(DiagnosticDescriptors.PatternComma, Current.Span);
                SkipCurrentToken();
                pattern = new BinaryPatternSyntax(SyntaxKind.OrPattern, pattern, Missing(SyntaxKind.OrKeyword), ParsePattern());
            }
        }
        var whenClause = At(SyntaxKind.WhenKeyword) ? ParseWhenClause() : null;
        SyntaxToken arrow;
        if (At(SyntaxKind.EqualsGreaterThanToken))
        {
            Report(DiagnosticDescriptors.FatArrow, Current.Span);
            arrow = NextToken();
        }
        else
        {
            arrow = Expect(SyntaxKind.MinusGreaterThanToken);
        }
        AllowLineBreak();
        return new MatchArmSyntax(keyword, pattern, whenClause, arrow, ParseArmBody(asStatement, "match", "arm"));
    }

    private WhenClauseSyntax ParseWhenClause()
    {
        var whenKeyword = NextToken();
        return new WhenClauseSyntax(whenKeyword, ParseHeadExpression());
    }

    /// <summary>
    /// The branch after the '-&gt;' of a match arm or of a case of <c>await all</c> / <c>await any</c>
    /// (<paramref name="construct"/>, whose branches are called <paramref name="item"/>s in messages). An expression or a
    /// leave; when the construct is a statement, also a block or a simple statement: an assignment or <c>emit</c>
    /// (ADR-0025, ADR-0026, ADR-0034). Declarations and statements with blocks of their own written directly after
    /// '-&gt;' are reported and read into a block with missing braces, so the rest of the construct still parses.
    /// </summary>
    private SyntaxNode ParseArmBody(bool asStatement, string construct, string item)
    {
        var how = asStatement
            ? ArmStatementInBraces
            : $"when '{construct}' gives a value, each {item} is one expression, or 'eval {{ ... give value }}'";
        if (Current.Kind == SyntaxKind.OpenBraceToken)
        {
            var block = ParseBlock();
            if (!asStatement)
            {
                Report(DiagnosticDescriptors.MatchBlockArmAsValue, block.OpenBraceToken.Span, construct, item);
            }
            return block;
        }
        if (TryParseLeave() is { } leave)
        {
            return leave;
        }
        if (asStatement && At(SyntaxKind.EmitKeyword))
        {
            return new EmitStatementSyntax(NextToken(), ParseExpression());
        }
        var awaitStatement = Current.Kind == SyntaxKind.AwaitKeyword && IsAwaitStatementAhead();
        if (!AtLineBreak && (Current.Kind is SyntaxKind.LetKeyword or SyntaxKind.VarKeyword or SyntaxKind.EmitKeyword
            or SyntaxKind.UsingKeyword or SyntaxKind.WhileKeyword or SyntaxKind.ForKeyword
            || awaitStatement || (asStatement && IsBranchingStatementAhead())))
        {
            var what = awaitStatement ? $"'await {Peek(1).Text}' statement" : $"'{Current.Text}' statement";
            Report(DiagnosticDescriptors.MatchArmStatement, Current.Span, what, how);
            var open = Missing(SyntaxKind.OpenBraceToken);
            var statement = ParseStatement();
            List<StatementSyntax> statements = statement == null ? [] : [statement];
            return new BlockSyntax(open, MakeList(statements), Missing(SyntaxKind.CloseBraceToken));
        }
        var expression = ParseExpressionCore();
        if (TryEatAssignmentOperator() is { } operatorToken)
        {
            // A line may end after any assignment operator (ADR-0013, ADR-0020).
            AllowLineBreak();
            var assignment = new AssignmentStatementSyntax(expression, operatorToken, ParseExpressionCore());
            if (asStatement)
            {
                return assignment;
            }
            Report(DiagnosticDescriptors.AssignmentAsValue, operatorToken.Span);
            return new BlockSyntax(
                SyntaxToken.Missing(SyntaxKind.OpenBraceToken, expression.Span.Start), MakeList<StatementSyntax>([assignment]), Missing(SyntaxKind.CloseBraceToken));
        }
        return expression;
    }

    // try --------------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>try</c> in either form, decided by what follows it: <c>{</c> for the statement, a value on the same line
    /// for the <c>try</c> that gives one (ADR-0043; the <c>-&gt;</c> written before the value until then is reported).
    /// </summary>
    private SyntaxNode ParseTry(bool asValue)
    {
        var errors = ErrorCount;
        var tryKeyword = NextToken();
        var valueFollows = Current.Kind != SyntaxKind.OpenBraceToken && !AtLineBreak && CanStartBranchValue();
        return valueFollows || (asValue && Current.Kind != SyntaxKind.OpenBraceToken)
            ? ParseTryExpressionRest(tryKeyword, errors)
            : ParseTryStatementRest(tryKeyword);
    }

    private TryStatementSyntax ParseTryStatementRest(SyntaxToken tryKeyword)
    {
        var block = ParseBlock();
        var catches = new List<CatchClauseSyntax>();
        while (Current.Kind == SyntaxKind.CatchKeyword)
        {
            ReportIfOnNextLine();
            var (catchKeyword, identifier, typeAnnotation, filter) = ParseCatchHeader();
            var body = At(SyntaxKind.MinusGreaterThanToken) ? ParseValueBranchInStatement("try") : ParseBlock();
            catches.Add(new CatchClauseSyntax(catchKeyword, identifier, typeAnnotation, filter, body));
        }
        FinallyClauseSyntax? @finally = null;
        if (Current.Kind == SyntaxKind.FinallyKeyword)
        {
            ReportIfOnNextLine();
            @finally = ParseFinallyClause();
        }
        if (catches.Count == 0 && @finally == null)
        {
            Report(DiagnosticDescriptors.TryWithoutHandler, tryKeyword.Span);
        }
        return new TryStatementSyntax(tryKeyword, block, MakeList(catches), @finally);
    }

    /// <summary>The rest of a <c>try</c> that gives a value. A missing <c>catch</c> is only reported when the rest had no error.</summary>
    private TryExpressionSyntax ParseTryExpressionRest(SyntaxToken tryKeyword, int errorsAtStart)
    {
        TrySkipValueArrow("try");
        var value = ParseBranchValue("try");
        var catches = new List<CatchArrowClauseSyntax>();
        // 'catch' and 'finally' continue the 'try' on the same line or at the start of the next (ADR-0025).
        while (Current.Kind == SyntaxKind.CatchKeyword)
        {
            var (catchKeyword, identifier, typeAnnotation, filter) = ParseCatchHeader();
            var (catchArrow, catchValue) = ParseArrowBranch("try");
            catches.Add(new CatchArrowClauseSyntax(catchKeyword, identifier, typeAnnotation, filter, catchArrow, catchValue));
        }
        var @finally = Current.Kind == SyntaxKind.FinallyKeyword ? ParseFinallyClause() : null;
        if (catches.Count == 0 && @finally == null && ErrorCount == errorsAtStart)
        {
            Report(DiagnosticDescriptors.TryWithoutHandler, tryKeyword.Span);
        }
        return new TryExpressionSyntax(tryKeyword, value, MakeList(catches), @finally);
    }

    /// <summary>
    /// <c>catch e: IOException when e.HResult == 32</c>; the name and the type are required (ADR-0014), the name is
    /// <c>_</c> when the exception is not needed (ADR-0026).
    /// </summary>
    private (SyntaxToken CatchKeyword, SyntaxToken Identifier, TypeAnnotationSyntax TypeAnnotation, WhenClauseSyntax? Filter) ParseCatchHeader()
    {
        var catchKeyword = NextToken();
        var identifier = ExpectIdentifierOrDiscard();
        var typeAnnotation = TryParseTypeAnnotation()
            ?? new TypeAnnotationSyntax(Expect(SyntaxKind.ColonToken), new IdentifierNameSyntax(Missing(SyntaxKind.IdentifierToken)));
        var filter = At(SyntaxKind.WhenKeyword) ? ParseWhenClause() : null;
        return (catchKeyword, identifier, typeAnnotation, filter);
    }

    private FinallyClauseSyntax ParseFinallyClause()
    {
        var finallyKeyword = NextToken();
        return new FinallyClauseSyntax(finallyKeyword, ParseBlock());
    }
}
