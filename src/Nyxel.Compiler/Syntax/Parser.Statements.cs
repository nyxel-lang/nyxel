using Nyxel.Compiler.Diagnostics;

namespace Nyxel.Compiler.Syntax;

internal sealed partial class Parser
{
    /// <summary>
    /// <c>{ statements }</c>. Inside a block line breaks matter again, even when the block itself is inside
    /// brackets (a lambda body in an argument list).
    /// </summary>
    private BlockSyntax ParseBlock()
    {
        var open = ExpectOpenBrace();
        if (open.IsMissing)
        {
            return new BlockSyntax(open, SyntaxList<StatementSyntax>.Empty, Missing(SyntaxKind.CloseBraceToken));
        }
        var saved = _bracketDepth;
        _bracketDepth = 0;
        var statements = new List<StatementSyntax>();
        while (Current.Kind is not (SyntaxKind.CloseBraceToken or SyntaxKind.EndOfFileToken))
        {
            var start = _position;
            if (ParseStatement() is { } statement)
            {
                statements.Add(statement);
            }
            if (_position == start)
            {
                SkipCurrentToken();
            }
        }
        var close = ExpectCloseBrace(open);
        _bracketDepth = saved;
        return new BlockSyntax(open, MakeList(statements), close);
    }

    private StatementSyntax? ParseStatement()
    {
        var errors = ErrorCount;
        AllowLineBreak();
        StatementSyntax? statement = Current.Kind switch
        {
            SyntaxKind.LetKeyword or SyntaxKind.VarKeyword => ParseLocalDeclaration(),
            SyntaxKind.UsingKeyword => ParseUsingDeclaration(),
            SyntaxKind.WhileKeyword => new WhileStatementSyntax(NextToken(), ParseExpression(), ParseBlock()),
            SyntaxKind.ForKeyword => ParseForStatement(),
            SyntaxKind.ReturnKeyword => ParseReturnStatement(),
            SyntaxKind.ThrowKeyword => new ThrowStatementSyntax(NextToken(), ParseExpression()),
            SyntaxKind.BreakKeyword => new JumpStatementSyntax(SyntaxKind.BreakStatement, NextToken()),
            SyntaxKind.ContinueKeyword => new JumpStatementSyntax(SyntaxKind.ContinueStatement, NextToken()),
            SyntaxKind.EmitKeyword => new EmitStatementSyntax(NextToken(), ParseExpression()),
            SyntaxKind.OpenBraceToken => ParseStrayBlock(),
            _ when CanStartExpression(Current.Kind) => ParseExpressionOrAssignmentStatement(),
            _ => ReportInvalidStatementStart(),
        };
        ExpectEndOfStatement(errors, "statement");
        return statement;
    }

    private StatementSyntax? ReportInvalidStatementStart()
    {
        var token = Current;
        if (token.HasLeadingLineBreak && token.Kind is not (SyntaxKind.ElseKeyword or SyntaxKind.CatchKeyword or SyntaxKind.FinallyKeyword))
        {
            Report(DiagnosticDescriptors.InvalidLineStart, token.Span, Describe(token));
        }
        else
        {
            Report(DiagnosticDescriptors.UnexpectedToken, token.Span, Describe(token));
        }
        SkipToEndOfLine();
        return null;
    }

    /// <summary>A '{' where a statement should start. Bare blocks are not part of the language; read it to recover.</summary>
    private BlockSyntax ParseStrayBlock()
    {
        Report(DiagnosticDescriptors.UnexpectedToken, Current.Span, "'{'");
        return ParseBlock();
    }

    private LocalDeclarationStatementSyntax ParseLocalDeclaration()
    {
        var keyword = NextToken();
        var identifier = ExpectIdentifier();
        var typeAnnotation = TryParseTypeAnnotation();
        var initializer = TryParseInitializer();
        return new LocalDeclarationStatementSyntax(keyword, identifier, typeAnnotation, initializer);
    }

    /// <summary><c>using name = value</c> (ADR-0015), with targeted errors for the C# forms.</summary>
    private UsingDeclarationStatementSyntax ParseUsingDeclaration()
    {
        var usingKeyword = NextToken();
        if (Current.Kind is SyntaxKind.VarKeyword or SyntaxKind.LetKeyword && Peek(1).Kind == SyntaxKind.IdentifierToken)
        {
            Report(DiagnosticDescriptors.UsingVar, Current.Span, Peek(1).Text, Current.Text);
            SkipCurrentToken();
        }
        else if (At(SyntaxKind.OpenParenToken))
        {
            Report(DiagnosticDescriptors.UsingBlock, Current.Span);
            SkipToEndOfLine();
            return new UsingDeclarationStatementSyntax(
                usingKeyword,
                Missing(SyntaxKind.IdentifierToken),
                null,
                new EqualsValueClauseSyntax(Missing(SyntaxKind.EqualsToken), MissingExpression()));
        }
        var identifier = ExpectIdentifier();
        var typeAnnotation = TryParseTypeAnnotation();
        var initializer = TryParseInitializer()
            ?? new EqualsValueClauseSyntax(Expect(SyntaxKind.EqualsToken), MissingExpression());
        return new UsingDeclarationStatementSyntax(usingKeyword, identifier, typeAnnotation, initializer);
    }

    private ForStatementSyntax ParseForStatement()
    {
        var forKeyword = NextToken();
        if (At(SyntaxKind.OpenParenToken))
        {
            Report(DiagnosticDescriptors.CStyleFor, Current.Span);
            SkipBalanced();
            return new ForStatementSyntax(
                forKeyword, Missing(SyntaxKind.IdentifierToken), Missing(SyntaxKind.InKeyword), MissingExpression(), ParseBlock());
        }
        var identifier = ExpectIdentifier();
        var inKeyword = Expect(SyntaxKind.InKeyword);
        var collection = ParseExpression();
        return new ForStatementSyntax(forKeyword, identifier, inKeyword, collection, ParseBlock());
    }

    private ReturnStatementSyntax ParseReturnStatement()
    {
        var returnKeyword = NextToken();
        var expression = AtStatementEnd ? null : ParseExpression();
        return new ReturnStatementSyntax(returnKeyword, expression);
    }

    /// <summary>An expression statement, or an assignment when an assignment operator follows (ADR-0011).</summary>
    private StatementSyntax ParseExpressionOrAssignmentStatement()
    {
        var expression = ParseExpressionCore();
        if (TryEatAssignmentOperator() is { } operatorToken)
        {
            // A line may end after '=' (ADR-0013). Whether '+=' and the like may end a line is not decided; until
            // then they follow the literal rule and may not.
            if (operatorToken.Kind == SyntaxKind.EqualsToken)
            {
                AllowLineBreak();
            }
            var value = ParseExpressionCore();
            if (SyntaxFacts.IsAssignmentOperator(Current.Kind) && !AtLineBreak)
            {
                Report(DiagnosticDescriptors.AssignmentAsValue, Current.Span);
            }
            return new AssignmentStatementSyntax(expression, operatorToken, value);
        }
        return new ExpressionStatementSyntax(expression);
    }

    private SyntaxToken? TryEatAssignmentOperator()
    {
        if (AtLineBreak)
        {
            return null;
        }
        if (SyntaxFacts.IsAssignmentOperator(Current.Kind))
        {
            return NextToken();
        }
        if (IsAdjacent(SyntaxKind.GreaterThanToken, SyntaxKind.GreaterThanEqualsToken))
        {
            return EatComposed(SyntaxKind.GreaterThanGreaterThanEqualsToken);
        }
        return null;
    }
}
