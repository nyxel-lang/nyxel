namespace Nyxel.Compiler.Syntax;

/// <summary>
/// A statement. <c>if</c>, <c>match</c> and <c>try</c> are expressions (ADR-0007, ADR-0014); used as statements
/// they are wrapped in an <see cref="ExpressionStatementSyntax"/>.
/// </summary>
public abstract class StatementSyntax : SyntaxNode
{
    private protected StatementSyntax(SyntaxKind kind)
        : base(kind)
    {
    }
}

public sealed class BlockSyntax : StatementSyntax
{
    internal BlockSyntax(SyntaxToken openBraceToken, SyntaxList<StatementSyntax> statements, SyntaxToken closeBraceToken)
        : base(SyntaxKind.Block)
    {
        OpenBraceToken = openBraceToken;
        Statements = statements;
        CloseBraceToken = closeBraceToken;
        AdoptChildren();
    }

    public SyntaxToken OpenBraceToken { get; }

    public SyntaxList<StatementSyntax> Statements { get; }

    public SyntaxToken CloseBraceToken { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(OpenBraceToken, Statements, CloseBraceToken);
}

/// <summary><c>let name: Type = value</c> or <c>var ...</c> inside a function.</summary>
public sealed class LocalDeclarationStatementSyntax : StatementSyntax
{
    internal LocalDeclarationStatementSyntax(
        SyntaxToken keyword,
        SyntaxToken identifier,
        TypeAnnotationSyntax? typeAnnotation,
        EqualsValueClauseSyntax? initializer)
        : base(SyntaxKind.LocalDeclarationStatement)
    {
        Keyword = keyword;
        Identifier = identifier;
        TypeAnnotation = typeAnnotation;
        Initializer = initializer;
        AdoptChildren();
    }

    /// <summary><c>let</c> or <c>var</c>.</summary>
    public SyntaxToken Keyword { get; }

    public SyntaxToken Identifier { get; }

    public TypeAnnotationSyntax? TypeAnnotation { get; }

    public EqualsValueClauseSyntax? Initializer { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Keyword, Identifier, TypeAnnotation, Initializer);
}

/// <summary><c>using name: Type = value</c> (ADR-0015).</summary>
public sealed class UsingDeclarationStatementSyntax : StatementSyntax
{
    internal UsingDeclarationStatementSyntax(
        SyntaxToken usingKeyword,
        SyntaxToken identifier,
        TypeAnnotationSyntax? typeAnnotation,
        EqualsValueClauseSyntax initializer)
        : base(SyntaxKind.UsingDeclarationStatement)
    {
        UsingKeyword = usingKeyword;
        Identifier = identifier;
        TypeAnnotation = typeAnnotation;
        Initializer = initializer;
        AdoptChildren();
    }

    public SyntaxToken UsingKeyword { get; }

    public SyntaxToken Identifier { get; }

    public TypeAnnotationSyntax? TypeAnnotation { get; }

    public EqualsValueClauseSyntax Initializer { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(UsingKeyword, Identifier, TypeAnnotation, Initializer);
}

/// <summary>An expression used as a statement. Which expressions are allowed here is checked during binding.</summary>
public sealed class ExpressionStatementSyntax : StatementSyntax
{
    internal ExpressionStatementSyntax(ExpressionSyntax expression)
        : base(SyntaxKind.ExpressionStatement)
    {
        Expression = expression;
        AdoptChildren();
    }

    public ExpressionSyntax Expression { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Expression);
}

/// <summary><c>target = value</c>, <c>target += value</c> and the other compound forms (ADR-0011: a statement, not an expression).</summary>
public sealed class AssignmentStatementSyntax : StatementSyntax
{
    internal AssignmentStatementSyntax(ExpressionSyntax target, SyntaxToken operatorToken, ExpressionSyntax value)
        : base(SyntaxKind.AssignmentStatement)
    {
        Target = target;
        OperatorToken = operatorToken;
        Value = value;
        AdoptChildren();
    }

    public ExpressionSyntax Target { get; }

    public SyntaxToken OperatorToken { get; }

    public ExpressionSyntax Value { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Target, OperatorToken, Value);
}

public sealed class WhileStatementSyntax : StatementSyntax
{
    internal WhileStatementSyntax(SyntaxToken whileKeyword, ExpressionSyntax condition, BlockSyntax body)
        : base(SyntaxKind.WhileStatement)
    {
        WhileKeyword = whileKeyword;
        Condition = condition;
        Body = body;
        AdoptChildren();
    }

    public SyntaxToken WhileKeyword { get; }

    public ExpressionSyntax Condition { get; }

    public BlockSyntax Body { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(WhileKeyword, Condition, Body);
}

/// <summary><c>for name in collection { }</c> (ADR-0010).</summary>
public sealed class ForStatementSyntax : StatementSyntax
{
    internal ForStatementSyntax(
        SyntaxToken forKeyword,
        SyntaxToken identifier,
        SyntaxToken inKeyword,
        ExpressionSyntax collection,
        BlockSyntax body)
        : base(SyntaxKind.ForStatement)
    {
        ForKeyword = forKeyword;
        Identifier = identifier;
        InKeyword = inKeyword;
        Collection = collection;
        Body = body;
        AdoptChildren();
    }

    public SyntaxToken ForKeyword { get; }

    public SyntaxToken Identifier { get; }

    public SyntaxToken InKeyword { get; }

    public ExpressionSyntax Collection { get; }

    public BlockSyntax Body { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(ForKeyword, Identifier, InKeyword, Collection, Body);
}

public sealed class ReturnStatementSyntax : StatementSyntax
{
    internal ReturnStatementSyntax(SyntaxToken returnKeyword, ExpressionSyntax? expression)
        : base(SyntaxKind.ReturnStatement)
    {
        ReturnKeyword = returnKeyword;
        Expression = expression;
        AdoptChildren();
    }

    public SyntaxToken ReturnKeyword { get; }

    public ExpressionSyntax? Expression { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(ReturnKeyword, Expression);
}

public sealed class ThrowStatementSyntax : StatementSyntax
{
    internal ThrowStatementSyntax(SyntaxToken throwKeyword, ExpressionSyntax expression)
        : base(SyntaxKind.ThrowStatement)
    {
        ThrowKeyword = throwKeyword;
        Expression = expression;
        AdoptChildren();
    }

    public SyntaxToken ThrowKeyword { get; }

    public ExpressionSyntax Expression { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(ThrowKeyword, Expression);
}

/// <summary><c>break</c> or <c>continue</c>; <see cref="SyntaxNode.Kind"/> tells which.</summary>
public sealed class JumpStatementSyntax : StatementSyntax
{
    internal JumpStatementSyntax(SyntaxKind kind, SyntaxToken keyword)
        : base(kind)
    {
        Keyword = keyword;
        AdoptChildren();
    }

    public SyntaxToken Keyword { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Keyword);
}

/// <summary><c>emit self.Died(slime = self)</c> (ADR-0017). The expression is checked to be an event call during binding.</summary>
public sealed class EmitStatementSyntax : StatementSyntax
{
    internal EmitStatementSyntax(SyntaxToken emitKeyword, ExpressionSyntax expression)
        : base(SyntaxKind.EmitStatement)
    {
        EmitKeyword = emitKeyword;
        Expression = expression;
        AdoptChildren();
    }

    public SyntaxToken EmitKeyword { get; }

    public ExpressionSyntax Expression { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(EmitKeyword, Expression);
}
