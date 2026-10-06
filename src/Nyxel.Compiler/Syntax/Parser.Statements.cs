using Nyxel.Compiler.Diagnostics;
using Nyxel.Compiler.Text;

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
            SyntaxKind.WhileKeyword => ParseWhileStatement(),
            SyntaxKind.ForKeyword => ParseForStatement(),
            SyntaxKind.ReturnKeyword => ParseReturnStatement(),
            SyntaxKind.ThrowKeyword => new ThrowStatementSyntax(NextToken(), ParseExpression()),
            SyntaxKind.BreakKeyword => ParseJumpStatement(SyntaxKind.BreakStatement),
            SyntaxKind.ContinueKeyword => ParseJumpStatement(SyntaxKind.ContinueStatement),
            SyntaxKind.EmitKeyword => new EmitStatementSyntax(NextToken(), ParseExpression()),
            SyntaxKind.OpenBraceToken => ParseStrayBlock(),
            SyntaxKind.IdentifierToken when Peek(1).Kind == SyntaxKind.ColonToken
                && Peek(2).Kind is SyntaxKind.ForKeyword or SyntaxKind.WhileKeyword => ParseLabeledLoop(),
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
        Report(DiagnosticDescriptors.StandaloneBlock, Current.Span);
        return ParseBlock();
    }

    private StatementSyntax ParseLocalDeclaration()
    {
        var keyword = NextToken();
        if (At(SyntaxKind.OpenParenToken))
        {
            var deconstruction = ParseDeconstruction();
            var value = TryParseInitializer()
                ?? new EqualsValueClauseSyntax(Expect(SyntaxKind.EqualsToken), MissingExpression());
            return new DeconstructionDeclarationStatementSyntax(keyword, deconstruction, value);
        }
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

    private WhileStatementSyntax ParseWhileStatement() => new(NextToken(), ParseExpression(), ParseBlock());

    private ForStatementSyntax ParseForStatement()
    {
        var forKeyword = NextToken();
        SyntaxToken? identifier = null;
        DeconstructionSyntax? deconstruction = null;
        if (At(SyntaxKind.OpenParenToken))
        {
            if (!IsDeconstructionBeforeIn())
            {
                Report(DiagnosticDescriptors.CStyleFor, Current.Span);
                SkipBalanced();
                return new ForStatementSyntax(
                    forKeyword, Missing(SyntaxKind.IdentifierToken), null, Missing(SyntaxKind.InKeyword), MissingExpression(), ParseBlock());
            }
            deconstruction = ParseDeconstruction();
        }
        else
        {
            identifier = ExpectIdentifier();
        }
        var inKeyword = Expect(SyntaxKind.InKeyword);
        var collection = ParseExpression();
        return new ForStatementSyntax(forKeyword, identifier, deconstruction, inKeyword, collection, ParseBlock());
    }

    /// <summary>
    /// At '(' after <c>for</c>: <c>(i, item) in</c> takes each element apart (ADR-0021); anything else, such as
    /// <c>(var i = 0; ...)</c> or <c>(item in items)</c>, is C#'s or another language's loop header.
    /// </summary>
    private bool IsDeconstructionBeforeIn()
    {
        var depth = 0;
        for (var offset = 0; ; offset++)
        {
            switch (Peek(offset).Kind)
            {
                case SyntaxKind.OpenParenToken:
                    depth++;
                    break;
                case SyntaxKind.CloseParenToken:
                    depth--;
                    if (depth == 0)
                    {
                        return Peek(offset + 1).Kind == SyntaxKind.InKeyword;
                    }
                    break;
                case SyntaxKind.InKeyword or SyntaxKind.SemicolonToken or SyntaxKind.OpenBraceToken or SyntaxKind.EndOfFileToken:
                    return false;
            }
        }
    }

    /// <summary><c>(min, max)</c> after <c>let</c>, <c>var</c> or <c>for</c>: plain names or <c>_</c> (ADR-0021).</summary>
    private DeconstructionSyntax ParseDeconstruction()
    {
        var open = NextToken();
        _bracketDepth++;
        var names = ParseSeparatedList(SyntaxKind.CloseParenToken, _ => ParseDeconstructionName());
        var close = Expect(SyntaxKind.CloseParenToken);
        _bracketDepth--;
        var deconstruction = new DeconstructionSyntax(open, names, close);
        if (names.Count < 2)
        {
            Report(DiagnosticDescriptors.TupleElementCount, deconstruction.Span);
        }
        return deconstruction;
    }

    /// <summary>A name in a deconstruction. Types and nested tuples are reported and skipped.</summary>
    private IdentifierNameSyntax ParseDeconstructionName()
    {
        if (Current.Kind == SyntaxKind.OpenParenToken)
        {
            Report(DiagnosticDescriptors.DeconstructionElement, Current.Span, "take the inner tuple apart with a second 'let'");
            SkipBalanced();
            return new IdentifierNameSyntax(Missing(SyntaxKind.IdentifierToken));
        }
        var name = new IdentifierNameSyntax(ExpectIdentifier());
        if (Current.Kind == SyntaxKind.ColonToken)
        {
            Report(DiagnosticDescriptors.DeconstructionElement, Current.Span, "the types come from the value");
            SkipCurrentToken();
            SkipNode(ParseType());
        }
        return name;
    }

    /// <summary><c>break</c> or <c>continue</c>; a label after it is reported (ADR-0021).</summary>
    private JumpStatementSyntax ParseJumpStatement(SyntaxKind kind)
    {
        var keyword = NextToken();
        if (Current.Kind == SyntaxKind.IdentifierToken && !AtStatementEnd)
        {
            Report(DiagnosticDescriptors.LoopLabel, Current.Span);
            SkipCurrentToken();
        }
        return new JumpStatementSyntax(kind, keyword);
    }

    /// <summary><c>outer: for ...</c>: Nyxel has no loop labels (ADR-0021). Reports the label and reads the loop.</summary>
    private StatementSyntax ParseLabeledLoop()
    {
        Report(DiagnosticDescriptors.LoopLabel, TextSpan.FromBounds(Current.Span.Start, Peek(1).Span.End));
        SkipCurrentToken();
        SkipCurrentToken();
        return Current.Kind == SyntaxKind.ForKeyword ? ParseForStatement() : ParseWhileStatement();
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
            // A line may end after any assignment operator (ADR-0013, ADR-0020).
            AllowLineBreak();
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
