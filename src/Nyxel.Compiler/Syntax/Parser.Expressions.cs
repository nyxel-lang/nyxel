using Nyxel.Compiler.Diagnostics;
using Nyxel.Compiler.Text;

namespace Nyxel.Compiler.Syntax;

internal sealed partial class Parser
{
    /// <summary>An expression where a value is needed; <c>a = b</c> here is reported (ADR-0011).</summary>
    private ExpressionSyntax ParseExpression()
    {
        var expression = ParseExpressionCore();
        if (At(SyntaxKind.EqualsToken))
        {
            Report(DiagnosticDescriptors.AssignmentAsValue, Current.Span);
            var equals = NextToken();
            expression = new BinaryExpressionSyntax(SyntaxKind.EqualsExpression, expression, equals, ParseExpressionCore());
        }
        return expression;
    }

    private ExpressionSyntax ParseExpressionCore() => ParseBinaryExpression(0);

    private ExpressionSyntax MissingExpression() => new IdentifierNameSyntax(Missing(SyntaxKind.IdentifierToken));

    private static bool CanStartExpression(SyntaxKind kind) => kind is SyntaxKind.IdentifierToken
        or SyntaxKind.NumericLiteralToken or SyntaxKind.StringLiteralToken or SyntaxKind.InterpolatedStringStartToken
        or SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword or SyntaxKind.NullKeyword or SyntaxKind.SelfKeyword
        or SyntaxKind.SuperKeyword or SyntaxKind.OpenParenToken or SyntaxKind.NewKeyword or SyntaxKind.FuncKeyword
        or SyntaxKind.AsyncKeyword or SyntaxKind.IfKeyword or SyntaxKind.MatchKeyword or SyntaxKind.TryKeyword
        or SyntaxKind.MinusToken or SyntaxKind.PlusToken or SyntaxKind.TildeToken or SyntaxKind.NotKeyword
        or SyntaxKind.ExclamationToken or SyntaxKind.AwaitKeyword or SyntaxKind.LaunchKeyword
        or SyntaxKind.PlusPlusToken or SyntaxKind.MinusMinusToken
        || SyntaxFacts.IsPredefinedType(kind);

    // Operators ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Precedence climbing over the table in <see cref="SyntaxFacts.GetBinaryPrecedence"/>. An operator at the
    /// start of a line continues the expression (ADR-0013), so operators are taken without a line check; the
    /// operand after an operator must be on the same line (ADR-0019).
    /// </summary>
    private ExpressionSyntax ParseBinaryExpression(int parentPrecedence)
    {
        var left = ParseUnaryExpression();
        while (true)
        {
            var composedShift = IsAdjacent(SyntaxKind.GreaterThanToken, SyntaxKind.GreaterThanToken);
            var kind = composedShift ? SyntaxKind.GreaterThanGreaterThanToken : Current.Kind;
            var precedence = SyntaxFacts.GetBinaryPrecedence(kind);
            if (precedence == 0 || precedence <= parentPrecedence
                || IsAdjacent(SyntaxKind.GreaterThanToken, SyntaxKind.GreaterThanEqualsToken))
            {
                return left;
            }

            var operatorToken = composedShift ? EatComposed(SyntaxKind.GreaterThanGreaterThanToken) : NextToken();
            CheckOperatorAtLineEnd(operatorToken);
            switch (kind)
            {
                case SyntaxKind.AsKeyword:
                    left = new AsExpressionSyntax(left, operatorToken, ParseType());
                    continue;
                case SyntaxKind.IsKeyword:
                    {
                        var notKeyword = TryEat(SyntaxKind.NotKeyword);
                        var type = ParseType();
                        if (At(SyntaxKind.IdentifierToken))
                        {
                            Report(DiagnosticDescriptors.IsWithName, Current.Span, Current.Text);
                            SkipCurrentToken();
                        }
                        left = new IsExpressionSyntax(left, operatorToken, notKeyword, type);
                        continue;
                    }
                case SyntaxKind.DotDotLessThanToken or SyntaxKind.DotDotDotToken:
                    if (left is RangeExpressionSyntax)
                    {
                        Report(DiagnosticDescriptors.ChainedRange, operatorToken.Span);
                    }
                    left = new RangeExpressionSyntax(left, operatorToken, ParseBinaryExpression(SyntaxFacts.RangePrecedence));
                    continue;
                case SyntaxKind.AmpersandAmpersandToken:
                    Report(DiagnosticDescriptors.SymbolicLogicalOperator, operatorToken.Span, "and", "&&");
                    break;
                case SyntaxKind.BarBarToken:
                    Report(DiagnosticDescriptors.SymbolicLogicalOperator, operatorToken.Span, "or", "||");
                    break;
            }

            var right = kind == SyntaxKind.QuestionQuestionToken
                ? ParseCoalesceRight(precedence)
                : ParseBinaryExpression(precedence);
            left = new BinaryExpressionSyntax(SyntaxFacts.GetBinaryExpressionKind(kind), left, operatorToken, right);
        }
    }

    /// <summary>The right of '??': a value (right-associative), or return / throw / break / continue (ADR-0009).</summary>
    private ExpressionSyntax ParseCoalesceRight(int precedence)
    {
        var kind = Current.Kind switch
        {
            SyntaxKind.ReturnKeyword => SyntaxKind.ReturnExpression,
            SyntaxKind.ThrowKeyword => SyntaxKind.ThrowExpression,
            SyntaxKind.BreakKeyword => SyntaxKind.BreakExpression,
            SyntaxKind.ContinueKeyword => SyntaxKind.ContinueExpression,
            _ => SyntaxKind.None,
        };
        if (kind == SyntaxKind.None || AtLineBreak)
        {
            return ParseBinaryExpression(precedence - 1);
        }
        var keyword = NextToken();
        ExpressionSyntax? value = null;
        if (kind == SyntaxKind.ThrowExpression)
        {
            value = ParseExpressionCore();
        }
        else if (kind == SyntaxKind.ReturnExpression && !AtStatementEnd && CanStartExpression(Current.Kind))
        {
            value = ParseExpressionCore();
        }
        return new JumpExpressionSyntax(kind, keyword, value);
    }

    private ExpressionSyntax ParseUnaryExpression()
    {
        var kind = Current.Kind switch
        {
            SyntaxKind.MinusToken => SyntaxKind.UnaryMinusExpression,
            SyntaxKind.PlusToken => SyntaxKind.UnaryPlusExpression,
            SyntaxKind.TildeToken => SyntaxKind.BitwiseNotExpression,
            SyntaxKind.NotKeyword or SyntaxKind.ExclamationToken => SyntaxKind.LogicalNotExpression,
            SyntaxKind.AwaitKeyword => SyntaxKind.AwaitExpression,
            SyntaxKind.LaunchKeyword => SyntaxKind.LaunchExpression,
            _ => SyntaxKind.None,
        };
        if (kind != SyntaxKind.None && !AtLineBreak)
        {
            var operatorToken = NextToken();
            if (operatorToken.Kind == SyntaxKind.ExclamationToken)
            {
                Report(DiagnosticDescriptors.SymbolicLogicalOperator, operatorToken.Span, "not", "!");
            }
            CheckOperatorAtLineEnd(operatorToken);
            return new UnaryExpressionSyntax(kind, operatorToken, ParseUnaryExpression());
        }
        if (Current.Kind is SyntaxKind.PlusPlusToken or SyntaxKind.MinusMinusToken && !AtLineBreak)
        {
            ReportIncrement();
            return ParseUnaryExpression();
        }
        return ParsePostfixExpression(ParsePrimaryExpression());
    }

    private void ReportIncrement()
    {
        var replacement = Current.Kind == SyntaxKind.PlusPlusToken ? "+= 1" : "-= 1";
        Report(DiagnosticDescriptors.IncrementOperator, Current.Span, Current.Text, replacement);
        SkipCurrentToken();
    }

    private ExpressionSyntax ParsePostfixExpression(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (Current.Kind)
            {
                // '.' and '?.' at the start of a line continue the expression (ADR-0013).
                case SyntaxKind.DotToken or SyntaxKind.QuestionDotToken:
                    {
                        var operatorToken = NextToken();
                        CheckOperatorAtLineEnd(operatorToken);
                        var kind = operatorToken.Kind == SyntaxKind.DotToken
                            ? SyntaxKind.SimpleMemberAccessExpression
                            : SyntaxKind.ConditionalMemberAccessExpression;
                        expression = new MemberAccessExpressionSyntax(kind, expression, operatorToken, ParseMemberName());
                        break;
                    }
                case SyntaxKind.OpenParenToken when !AtLineBreak:
                    expression = new InvocationExpressionSyntax(expression, ParseArgumentList(SyntaxKind.ArgumentList));
                    break;
                case SyntaxKind.OpenBracketToken when !AtLineBreak:
                    expression = new ElementAccessExpressionSyntax(expression, ParseArgumentList(SyntaxKind.BracketedArgumentList));
                    break;
                case SyntaxKind.ExclamationToken when !AtLineBreak:
                    Report(DiagnosticDescriptors.ForceUnwrap, Current.Span);
                    SkipCurrentToken();
                    break;
                case SyntaxKind.PlusPlusToken or SyntaxKind.MinusMinusToken when !AtLineBreak:
                    ReportIncrement();
                    break;
                case SyntaxKind.QuestionToken when !AtLineBreak:
                    // a ? b : c -- report once and skip the branches.
                    Report(DiagnosticDescriptors.Ternary, Current.Span);
                    SkipCurrentToken();
                    SkipNode(ParseExpressionCore());
                    if (At(SyntaxKind.ColonToken))
                    {
                        SkipCurrentToken();
                        SkipNode(ParseExpressionCore());
                    }
                    return expression;
                default:
                    return expression;
            }
        }
    }

    /// <summary>The name after '.': an identifier (maybe generic), or <c>init</c> in <c>super.init</c> / <c>self.init</c>.</summary>
    private SimpleNameSyntax ParseMemberName()
    {
        if (Current.Kind == SyntaxKind.IdentifierToken && !AtLineBreak)
        {
            return ParseSimpleNameInExpression();
        }
        if (Current.Kind == SyntaxKind.InitKeyword && !AtLineBreak)
        {
            return new IdentifierNameSyntax(NextToken());
        }
        ReportExpected("a member name");
        return new IdentifierNameSyntax(Missing(SyntaxKind.IdentifierToken));
    }

    // Primary expressions --------------------------------------------------------------------------------------

    private ExpressionSyntax ParsePrimaryExpression()
    {
        if (AtLineBreak)
        {
            ReportExpected("an expression");
            return MissingExpression();
        }
        switch (Current.Kind)
        {
            case SyntaxKind.NumericLiteralToken:
                return new LiteralExpressionSyntax(SyntaxKind.NumericLiteralExpression, NextToken());
            case SyntaxKind.StringLiteralToken:
                return new LiteralExpressionSyntax(SyntaxKind.StringLiteralExpression, NextToken());
            case SyntaxKind.TrueKeyword:
                return new LiteralExpressionSyntax(SyntaxKind.TrueLiteralExpression, NextToken());
            case SyntaxKind.FalseKeyword:
                return new LiteralExpressionSyntax(SyntaxKind.FalseLiteralExpression, NextToken());
            case SyntaxKind.NullKeyword:
                return new LiteralExpressionSyntax(SyntaxKind.NullLiteralExpression, NextToken());
            case SyntaxKind.InterpolatedStringStartToken:
                return ParseInterpolatedString();
            case SyntaxKind.SelfKeyword:
                return new InstanceExpressionSyntax(SyntaxKind.SelfExpression, NextToken());
            case SyntaxKind.SuperKeyword:
                return new InstanceExpressionSyntax(SyntaxKind.SuperExpression, NextToken());
            case SyntaxKind.IdentifierToken:
                return ParseSimpleNameInExpression();
            case SyntaxKind.OpenParenToken:
                return ParseParenthesizedExpression();
            case SyntaxKind.NewKeyword:
                return ParseObjectCreation();
            case SyntaxKind.FuncKeyword:
            case SyntaxKind.AsyncKeyword when Peek(1).Kind == SyntaxKind.FuncKeyword:
                return ParseLambda();
            case SyntaxKind.IfKeyword:
                return ParseIfExpression();
            case SyntaxKind.MatchKeyword:
                return ParseMatchExpression();
            case SyntaxKind.TryKeyword:
                return ParseTryExpression();
            case var kind when SyntaxFacts.IsPredefinedType(kind):
                return new PredefinedTypeSyntax(NextToken());
            default:
                ReportExpected("an expression");
                return MissingExpression();
        }
    }

    /// <summary>A name in an expression; <c>Name&lt;</c> is a generic name only when C#'s disambiguation rule says so (ADR-0012).</summary>
    private SimpleNameSyntax ParseSimpleNameInExpression()
    {
        var identifier = NextToken();
        if (Current.Kind == SyntaxKind.LessThanToken && !Current.HasLeadingLineBreak && IsGenericArgumentListAhead())
        {
            return new GenericNameSyntax(identifier, ParseTypeArgumentList());
        }
        return new IdentifierNameSyntax(identifier);
    }

    /// <summary>
    /// C#'s rule (spec §6.2.5): <c>&lt;...&gt;</c> is a type argument list if it scans as one and the token after
    /// '&gt;' is one that cannot start an operand: <c>( ) ] } : , . ?. == != | ^ &amp; [ and or ??</c>, a line
    /// break or the end of the file. So <c>Spawn&lt;Slime&gt;()</c> is generic and <c>a &lt; b &gt; c</c> is not.
    /// </summary>
    private bool IsGenericArgumentListAhead()
    {
        var offset = 0;
        if (!ScanTypeArgumentList(ref offset))
        {
            return false;
        }
        var next = Peek(offset);
        return next.HasLeadingLineBreak || next.Kind is SyntaxKind.OpenParenToken or SyntaxKind.CloseParenToken
            or SyntaxKind.CloseBracketToken or SyntaxKind.CloseBraceToken or SyntaxKind.ColonToken
            or SyntaxKind.CommaToken or SyntaxKind.DotToken or SyntaxKind.QuestionDotToken
            or SyntaxKind.EqualsEqualsToken or SyntaxKind.ExclamationEqualsToken or SyntaxKind.BarToken
            or SyntaxKind.CaretToken or SyntaxKind.AmpersandToken or SyntaxKind.OpenBracketToken
            or SyntaxKind.AndKeyword or SyntaxKind.OrKeyword or SyntaxKind.QuestionQuestionToken
            or SyntaxKind.EndOfFileToken;
    }

    private ExpressionSyntax ParseParenthesizedExpression()
    {
        if (LooksLikeCast(out var castLength))
        {
            // (int)x: report with the Nyxel spelling, drop the cast into trivia and parse the operand.
            var start = Current.Span.Start;
            var typeText = string.Concat(Enumerable.Range(1, castLength - 2).Select(i => Peek(i).Text));
            Report(DiagnosticDescriptors.CastSyntax, TextSpan.FromBounds(start, Peek(castLength - 1).Span.End), typeText);
            for (var i = 0; i < castLength; i++)
            {
                SkipCurrentToken();
            }
            return ParseUnaryExpression();
        }

        var open = NextToken();
        _bracketDepth++;
        var expression = ParseExpression();
        var close = Expect(SyntaxKind.CloseParenToken);
        _bracketDepth--;
        return new ParenthesizedExpressionSyntax(open, expression, close);
    }

    /// <summary><c>( type )</c> followed on the same line by the start of an operand: a C# cast.</summary>
    private bool LooksLikeCast(out int length)
    {
        length = 0;
        var offset = 1;
        var isPredefined = SyntaxFacts.IsPredefinedType(Peek(1).Kind);
        if (!ScanType(ref offset) || Peek(offset).Kind != SyntaxKind.CloseParenToken)
        {
            return false;
        }
        var next = Peek(offset + 1);
        if (next.HasLeadingLineBreak)
        {
            return false;
        }
        var startsOperand = next.Kind is SyntaxKind.IdentifierToken or SyntaxKind.NumericLiteralToken
            or SyntaxKind.StringLiteralToken or SyntaxKind.InterpolatedStringStartToken or SyntaxKind.SelfKeyword
            or SyntaxKind.SuperKeyword or SyntaxKind.NewKeyword or SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword
            or SyntaxKind.NullKeyword
            || SyntaxFacts.IsPredefinedType(next.Kind)
            || (isPredefined && next.Kind == SyntaxKind.OpenParenToken);
        if (!startsOperand)
        {
            return false;
        }
        length = offset + 1;
        return true;
    }

    private ObjectCreationExpressionSyntax ParseObjectCreation()
    {
        var newKeyword = NextToken();
        var type = ParseType();
        ArgumentListSyntax arguments;
        if (At(SyntaxKind.OpenParenToken))
        {
            arguments = ParseArgumentList(SyntaxKind.ArgumentList);
        }
        else
        {
            ReportExpected("'('");
            arguments = new ArgumentListSyntax(
                SyntaxKind.ArgumentList, Missing(SyntaxKind.OpenParenToken), SeparatedSyntaxList<ArgumentSyntax>.Empty, Missing(SyntaxKind.CloseParenToken));
        }
        return new ObjectCreationExpressionSyntax(newKeyword, type, arguments);
    }

    /// <summary>
    /// <c>(args)</c> or <c>[args]</c>. Line breaks inside mean nothing (ADR-0013), but bodies of lambdas inside
    /// get the line rules back (see <see cref="ParseBlock"/>).
    /// </summary>
    private ArgumentListSyntax ParseArgumentList(SyntaxKind kind)
    {
        var closeKind = kind == SyntaxKind.ArgumentList ? SyntaxKind.CloseParenToken : SyntaxKind.CloseBracketToken;
        var open = NextToken();
        _bracketDepth++;
        var arguments = ParseSeparatedList(closeKind, _ => ParseArgument());
        var close = Expect(closeKind);
        _bracketDepth--;
        return new ArgumentListSyntax(kind, open, arguments, close);
    }

    /// <summary><c>value</c>, <c>name = value</c>, <c>out let x</c>, <c>ref self.velocity</c>.</summary>
    private ArgumentSyntax ParseArgument()
    {
        NameEqualsSyntax? nameEquals = null;
        if (Current.Kind == SyntaxKind.IdentifierToken && Peek(1).Kind is SyntaxKind.EqualsToken or SyntaxKind.ColonToken)
        {
            if (Peek(1).Kind == SyntaxKind.ColonToken)
            {
                Report(DiagnosticDescriptors.NamedArgumentColon, Peek(1).Span, Current.Text);
            }
            var name = new IdentifierNameSyntax(NextToken());
            nameEquals = new NameEqualsSyntax(name, NextToken());
        }

        var refKind = Current.Kind is SyntaxKind.OutKeyword or SyntaxKind.RefKeyword ? NextToken() : null;
        ExpressionSyntax expression;
        if (refKind?.Kind == SyntaxKind.OutKeyword && Current.Kind is SyntaxKind.LetKeyword or SyntaxKind.VarKeyword)
        {
            var keyword = NextToken();
            var identifier = ExpectIdentifier();
            expression = new DeclarationExpressionSyntax(keyword, identifier, TryParseTypeAnnotation());
        }
        else
        {
            expression = ParseExpression();
        }
        return new ArgumentSyntax(nameEquals, refKind, expression);
    }

    private InterpolatedStringExpressionSyntax ParseInterpolatedString()
    {
        var start = NextToken();
        _bracketDepth++;
        var contents = new List<InterpolatedStringContentSyntax>();
        while (true)
        {
            if (Current.Kind == SyntaxKind.InterpolatedStringTextToken)
            {
                contents.Add(new InterpolatedStringTextSyntax(NextToken()));
            }
            else if (Current.Kind == SyntaxKind.OpenBraceToken)
            {
                contents.Add(ParseInterpolation());
            }
            else
            {
                break;
            }
        }
        var end = Expect(SyntaxKind.InterpolatedStringEndToken);
        _bracketDepth--;
        return new InterpolatedStringExpressionSyntax(start, MakeList(contents), end);
    }

    private InterpolationSyntax ParseInterpolation()
    {
        var open = NextToken();
        var expression = ParseExpression();
        InterpolationAlignmentClauseSyntax? alignment = null;
        if (At(SyntaxKind.CommaToken))
        {
            var comma = NextToken();
            alignment = new InterpolationAlignmentClauseSyntax(comma, ParseExpression());
        }
        InterpolationFormatClauseSyntax? format = null;
        if (At(SyntaxKind.ColonToken))
        {
            var colon = NextToken();
            var formatToken = Current.Kind == SyntaxKind.InterpolationFormatToken
                ? NextToken()
                : SyntaxToken.Missing(SyntaxKind.InterpolationFormatToken, colon.Span.End);
            format = new InterpolationFormatClauseSyntax(colon, formatToken);
        }
        if (Current.Kind is not (SyntaxKind.CloseBraceToken or SyntaxKind.InterpolatedStringEndToken or SyntaxKind.EndOfFileToken))
        {
            ReportExpected("'}'");
            while (Current.Kind is not (SyntaxKind.CloseBraceToken or SyntaxKind.InterpolatedStringEndToken
                or SyntaxKind.InterpolatedStringTextToken or SyntaxKind.EndOfFileToken))
            {
                SkipBalanced();
            }
        }
        var close = Expect(SyntaxKind.CloseBraceToken);
        return new InterpolationSyntax(open, expression, alignment, format, close);
    }

    private LambdaExpressionSyntax ParseLambda()
    {
        var asyncKeyword = Current.Kind == SyntaxKind.AsyncKeyword ? NextToken() : null;
        var funcKeyword = NextToken();
        var parameters = ParseParameterList(ParameterContext.Lambda);
        var returnType = TryParseReturnType();
        var (body, expressionBody) = ParseFunctionBody(required: true);
        return new LambdaExpressionSyntax(asyncKeyword, funcKeyword, parameters, returnType, body, expressionBody);
    }

    // if / match / try -----------------------------------------------------------------------------------------

    private IfExpressionSyntax ParseIfExpression()
    {
        var ifKeyword = NextToken();
        var condition = ParseExpression();
        var block = ParseBlock();
        ElseClauseSyntax? elseClause = null;
        if (Current.Kind == SyntaxKind.ElseKeyword)
        {
            // Nothing can start a line with 'else', so one on the next line still belongs here; it is reported.
            ReportIfOnNextLine();
            var elseKeyword = NextToken();
            SyntaxNode body = Current.Kind == SyntaxKind.IfKeyword && !Current.HasLeadingLineBreak
                ? ParseIfExpression()
                : ParseBlock();
            elseClause = new ElseClauseSyntax(elseKeyword, body);
        }
        return new IfExpressionSyntax(ifKeyword, condition, block, elseClause);
    }

    /// <summary><c>else</c>, <c>catch</c> and <c>finally</c> go on the line of the '}' before them (ADR-0013).</summary>
    private void ReportIfOnNextLine()
    {
        if (Current.HasLeadingLineBreak)
        {
            Report(DiagnosticDescriptors.KeywordOnNextLine, Current.Span, Current.Text);
        }
    }

    private MatchExpressionSyntax ParseMatchExpression()
    {
        var matchKeyword = NextToken();
        var expression = ParseExpression();
        var open = ExpectOpenBrace();
        var saved = _bracketDepth;
        _bracketDepth = 0;
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
                    arms.Add(ParseMatchArm());
                }
                else
                {
                    ReportExpected("'case' or 'else'", atLineStart: true);
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
        return new MatchExpressionSyntax(matchKeyword, expression, open, MakeList(arms), close);
    }

    private MatchArmSyntax ParseMatchArm()
    {
        var keyword = NextToken();
        PatternSyntax? pattern = null;
        if (keyword.Kind == SyntaxKind.CaseKeyword)
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
        return new MatchArmSyntax(keyword, pattern, whenClause, arrow, ParseMatchArmBody());
    }

    private WhenClauseSyntax ParseWhenClause()
    {
        var whenKeyword = NextToken();
        return new WhenClauseSyntax(whenKeyword, ParseExpression());
    }

    /// <summary>
    /// An expression or a block (ADR-0009). A statement written directly after '-&gt;' is reported and read into a
    /// block with missing braces, so the rest of the match still parses.
    /// </summary>
    private SyntaxNode ParseMatchArmBody()
    {
        if (Current.Kind == SyntaxKind.OpenBraceToken)
        {
            return ParseBlock();
        }
        if (!AtLineBreak && Current.Kind is SyntaxKind.ReturnKeyword or SyntaxKind.ThrowKeyword or SyntaxKind.BreakKeyword
            or SyntaxKind.ContinueKeyword or SyntaxKind.LetKeyword or SyntaxKind.VarKeyword or SyntaxKind.EmitKeyword
            or SyntaxKind.UsingKeyword or SyntaxKind.WhileKeyword or SyntaxKind.ForKeyword)
        {
            Report(DiagnosticDescriptors.MatchArmStatement, Current.Span, $"'{Current.Text}' statement");
            var open = Missing(SyntaxKind.OpenBraceToken);
            var statement = ParseStatement();
            List<StatementSyntax> statements = statement == null ? [] : [statement];
            return new BlockSyntax(open, MakeList(statements), Missing(SyntaxKind.CloseBraceToken));
        }
        var expression = ParseExpressionCore();
        if (TryEatAssignmentOperator() is { } operatorToken)
        {
            Report(DiagnosticDescriptors.MatchArmStatement, operatorToken.Span, "assignment");
            AllowLineBreak();
            var assignment = new AssignmentStatementSyntax(expression, operatorToken, ParseExpressionCore());
            return new BlockSyntax(
                SyntaxToken.Missing(SyntaxKind.OpenBraceToken, expression.Span.Start), MakeList<StatementSyntax>([assignment]), Missing(SyntaxKind.CloseBraceToken));
        }
        return expression;
    }

    private TryExpressionSyntax ParseTryExpression()
    {
        var tryKeyword = NextToken();
        var block = ParseBlock();
        var catches = new List<CatchClauseSyntax>();
        while (Current.Kind == SyntaxKind.CatchKeyword)
        {
            ReportIfOnNextLine();
            var catchKeyword = NextToken();
            var identifier = ExpectIdentifier();
            var typeAnnotation = TryParseTypeAnnotation()
                ?? new TypeAnnotationSyntax(Expect(SyntaxKind.ColonToken), new IdentifierNameSyntax(Missing(SyntaxKind.IdentifierToken)));
            var filter = At(SyntaxKind.WhenKeyword) ? ParseWhenClause() : null;
            catches.Add(new CatchClauseSyntax(catchKeyword, identifier, typeAnnotation, filter, ParseBlock()));
        }
        FinallyClauseSyntax? @finally = null;
        if (Current.Kind == SyntaxKind.FinallyKeyword)
        {
            ReportIfOnNextLine();
            var finallyKeyword = NextToken();
            @finally = new FinallyClauseSyntax(finallyKeyword, ParseBlock());
        }
        if (catches.Count == 0 && @finally == null)
        {
            Report(DiagnosticDescriptors.TryWithoutHandler, tryKeyword.Span);
        }
        return new TryExpressionSyntax(tryKeyword, block, MakeList(catches), @finally);
    }

    // Patterns (ADR-0009, ADR-0010): or < and < not < primary, the same order as the logical operators. -------------

    private PatternSyntax ParsePattern() => ParseOrPattern();

    private PatternSyntax ParseOrPattern()
    {
        var left = ParseAndPattern();
        while (Current.Kind is SyntaxKind.OrKeyword or SyntaxKind.BarBarToken)
        {
            var operatorToken = NextToken();
            if (operatorToken.Kind == SyntaxKind.BarBarToken)
            {
                Report(DiagnosticDescriptors.SymbolicLogicalOperator, operatorToken.Span, "or", "||");
            }
            CheckOperatorAtLineEnd(operatorToken);
            left = new BinaryPatternSyntax(SyntaxKind.OrPattern, left, operatorToken, ParseAndPattern());
        }
        return left;
    }

    private PatternSyntax ParseAndPattern()
    {
        var left = ParseNotPattern();
        while (Current.Kind is SyntaxKind.AndKeyword or SyntaxKind.AmpersandAmpersandToken)
        {
            var operatorToken = NextToken();
            if (operatorToken.Kind == SyntaxKind.AmpersandAmpersandToken)
            {
                Report(DiagnosticDescriptors.SymbolicLogicalOperator, operatorToken.Span, "and", "&&");
            }
            CheckOperatorAtLineEnd(operatorToken);
            left = new BinaryPatternSyntax(SyntaxKind.AndPattern, left, operatorToken, ParseNotPattern());
        }
        return left;
    }

    private PatternSyntax ParseNotPattern()
    {
        if (At(SyntaxKind.NotKeyword))
        {
            var notKeyword = NextToken();
            CheckOperatorAtLineEnd(notKeyword);
            return new NotPatternSyntax(notKeyword, ParseNotPattern());
        }
        return ParsePrimaryPattern();
    }

    /// <summary>
    /// <c>is T</c>; <c>&lt; 10</c>; a bare name, maybe with fields (an enum case); otherwise a constant or a range
    /// whose bounds are expressions above the range operators in precedence.
    /// </summary>
    private PatternSyntax ParsePrimaryPattern()
    {
        if (At(SyntaxKind.IsKeyword))
        {
            var isKeyword = NextToken();
            return new TypePatternSyntax(isKeyword, ParseType());
        }
        if (Current.Kind is SyntaxKind.LessThanToken or SyntaxKind.LessThanEqualsToken or SyntaxKind.GreaterThanToken
            or SyntaxKind.GreaterThanEqualsToken && !AtLineBreak)
        {
            var operatorToken = NextToken();
            return new RelationalPatternSyntax(operatorToken, ParseBinaryExpression(SyntaxFacts.RangePrecedence));
        }
        if (Current.Kind == SyntaxKind.IdentifierToken && !AtLineBreak
            && Peek(1).Kind is not (SyntaxKind.DotToken or SyntaxKind.QuestionDotToken or SyntaxKind.DotDotLessThanToken or SyntaxKind.DotDotDotToken))
        {
            var identifier = NextToken();
            CaseFieldListSyntax? fields = null;
            if (At(SyntaxKind.OpenParenToken))
            {
                var open = NextToken();
                _bracketDepth++;
                var names = ParseSeparatedList(SyntaxKind.CloseParenToken, _ => new IdentifierNameSyntax(ExpectIdentifier()));
                var close = Expect(SyntaxKind.CloseParenToken);
                _bracketDepth--;
                fields = new CaseFieldListSyntax(open, names, close);
            }
            return new CasePatternSyntax(identifier, fields);
        }
        var expression = ParseBinaryExpression(SyntaxFacts.RangePrecedence);
        if (Current.Kind is SyntaxKind.DotDotLessThanToken or SyntaxKind.DotDotDotToken)
        {
            var operatorToken = NextToken();
            CheckOperatorAtLineEnd(operatorToken);
            return new RangePatternSyntax(expression, operatorToken, ParseBinaryExpression(SyntaxFacts.RangePrecedence));
        }
        return new ConstantPatternSyntax(expression);
    }

    // Types ----------------------------------------------------------------------------------------------------

    private TypeSyntax ParseType()
    {
        TypeSyntax type;
        if (AtLineBreak)
        {
            ReportExpected("a type");
            return new IdentifierNameSyntax(Missing(SyntaxKind.IdentifierToken));
        }
        if (SyntaxFacts.IsPredefinedType(Current.Kind))
        {
            type = new PredefinedTypeSyntax(NextToken());
        }
        else if (Current.Kind == SyntaxKind.IdentifierToken)
        {
            type = ParseNamedType();
        }
        else if (Current.Kind == SyntaxKind.FuncKeyword || (Current.Kind == SyntaxKind.AsyncKeyword && Peek(1).Kind == SyntaxKind.FuncKeyword))
        {
            type = ParseFunctionType();
        }
        else
        {
            ReportExpected("a type");
            return new IdentifierNameSyntax(Missing(SyntaxKind.IdentifierToken));
        }

        while (At(SyntaxKind.QuestionToken))
        {
            type = new NullableTypeSyntax(type, NextToken());
        }
        if (At(SyntaxKind.OpenBracketToken) && Peek(1).Kind == SyntaxKind.CloseBracketToken)
        {
            Report(DiagnosticDescriptors.ArrayType, TextSpan.FromBounds(Current.Span.Start, Peek(1).Span.End));
            SkipCurrentToken();
            SkipCurrentToken();
        }
        return type;
    }

    private NameSyntax ParseNamedType()
    {
        NameSyntax name = ParseSimpleNameInType();
        while (At(SyntaxKind.DotToken) && Peek(1).Kind == SyntaxKind.IdentifierToken)
        {
            var dot = NextToken();
            name = new QualifiedNameSyntax(name, dot, ParseSimpleNameInType());
        }
        return name;
    }

    private SimpleNameSyntax ParseSimpleNameInType()
    {
        var identifier = NextToken();
        var offset = 0;
        if (At(SyntaxKind.LessThanToken) && ScanTypeArgumentList(ref offset))
        {
            return new GenericNameSyntax(identifier, ParseTypeArgumentList());
        }
        return new IdentifierNameSyntax(identifier);
    }

    private TypeArgumentListSyntax ParseTypeArgumentList()
    {
        var lessThan = NextToken();
        var arguments = new List<SyntaxNode> { ParseType() };
        while (At(SyntaxKind.CommaToken))
        {
            arguments.Add(NextToken());
            arguments.Add(ParseType());
        }
        var greaterThan = Expect(SyntaxKind.GreaterThanToken);
        return new TypeArgumentListSyntax(lessThan, Separated<TypeSyntax>(arguments), greaterThan);
    }

    private FunctionTypeSyntax ParseFunctionType()
    {
        var asyncKeyword = Current.Kind == SyntaxKind.AsyncKeyword ? NextToken() : null;
        var funcKeyword = NextToken();
        var open = Expect(SyntaxKind.OpenParenToken);
        _bracketDepth++;
        var parameterTypes = open.IsMissing
            ? SeparatedSyntaxList<TypeSyntax>.Empty
            : ParseSeparatedList(SyntaxKind.CloseParenToken, _ => ParseType());
        var close = Expect(SyntaxKind.CloseParenToken);
        _bracketDepth--;
        return new FunctionTypeSyntax(asyncKeyword, funcKeyword, open, parameterTypes, close, TryParseReturnType());
    }

    // Scanning ahead without building nodes, for the generic-name and cast decisions. -----------------------------

    private bool ScanTypeArgumentList(ref int offset)
    {
        if (Peek(offset).Kind != SyntaxKind.LessThanToken)
        {
            return false;
        }
        offset++;
        while (true)
        {
            if (!ScanType(ref offset))
            {
                return false;
            }
            if (Peek(offset).Kind == SyntaxKind.CommaToken)
            {
                offset++;
                continue;
            }
            if (Peek(offset).Kind == SyntaxKind.GreaterThanToken)
            {
                offset++;
                return true;
            }
            return false;
        }
    }

    private bool ScanType(ref int offset)
    {
        var kind = Peek(offset).Kind;
        if (SyntaxFacts.IsPredefinedType(kind))
        {
            offset++;
        }
        else if (kind == SyntaxKind.IdentifierToken)
        {
            offset++;
            if (Peek(offset).Kind == SyntaxKind.LessThanToken && !ScanTypeArgumentList(ref offset))
            {
                return false;
            }
            while (Peek(offset).Kind == SyntaxKind.DotToken && Peek(offset + 1).Kind == SyntaxKind.IdentifierToken)
            {
                offset += 2;
                if (Peek(offset).Kind == SyntaxKind.LessThanToken && !ScanTypeArgumentList(ref offset))
                {
                    return false;
                }
            }
        }
        else if (kind == SyntaxKind.FuncKeyword || (kind == SyntaxKind.AsyncKeyword && Peek(offset + 1).Kind == SyntaxKind.FuncKeyword))
        {
            offset += kind == SyntaxKind.AsyncKeyword ? 2 : 1;
            if (Peek(offset).Kind != SyntaxKind.OpenParenToken)
            {
                return false;
            }
            offset++;
            while (Peek(offset).Kind != SyntaxKind.CloseParenToken)
            {
                if (!ScanType(ref offset))
                {
                    return false;
                }
                if (Peek(offset).Kind == SyntaxKind.CommaToken)
                {
                    offset++;
                }
            }
            offset++;
            if (Peek(offset).Kind == SyntaxKind.MinusGreaterThanToken)
            {
                offset++;
                if (!ScanType(ref offset))
                {
                    return false;
                }
            }
        }
        else
        {
            return false;
        }
        while (Peek(offset).Kind == SyntaxKind.QuestionToken)
        {
            offset++;
        }
        return true;
    }

    // Composed '>>' and '>>=' ----------------------------------------------------------------------------------

    private bool IsAdjacent(SyntaxKind first, SyntaxKind second)
    {
        var next = Peek(1);
        return Current.Kind == first && next.Kind == second
            && Current.TrailingTrivia.IsEmpty && next.LeadingTrivia.IsEmpty && next.Position == Current.Span.End;
    }

    private SyntaxToken EatComposed(SyntaxKind kind)
    {
        var first = NextToken();
        var second = NextToken();
        return new SyntaxToken(kind, first.Position, first.Text + second.Text, null, first.LeadingTrivia, second.TrailingTrivia, first.HasLeadingLineBreak);
    }
}
