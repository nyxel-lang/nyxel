namespace Nyxel.Compiler.Syntax;

/// <summary>A pattern after <c>case</c> (ADR-0009, ADR-0010).</summary>
public abstract class PatternSyntax : SyntaxNode
{
    private protected PatternSyntax(SyntaxKind kind)
        : base(kind)
    {
    }
}

/// <summary>A constant: <c>0</c>, <c>"boss"</c>, <c>null</c>, <c>Element.Fire</c>.</summary>
public sealed class ConstantPatternSyntax : PatternSyntax
{
    internal ConstantPatternSyntax(ExpressionSyntax expression)
        : base(SyntaxKind.ConstantPattern)
    {
        Expression = expression;
        AdoptChildren();
    }

    public ExpressionSyntax Expression { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Expression);
}

/// <summary><c>&lt; 10</c>, <c>&gt;= 0</c>.</summary>
public sealed class RelationalPatternSyntax : PatternSyntax
{
    internal RelationalPatternSyntax(SyntaxToken operatorToken, ExpressionSyntax expression)
        : base(SyntaxKind.RelationalPattern)
    {
        OperatorToken = operatorToken;
        Expression = expression;
        AdoptChildren();
    }

    public SyntaxToken OperatorToken { get; }

    public ExpressionSyntax Expression { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(OperatorToken, Expression);
}

/// <summary><c>10..&lt;50</c>, <c>1...3</c>.</summary>
public sealed class RangePatternSyntax : PatternSyntax
{
    internal RangePatternSyntax(ExpressionSyntax lower, SyntaxToken operatorToken, ExpressionSyntax upper)
        : base(SyntaxKind.RangePattern)
    {
        Lower = lower;
        OperatorToken = operatorToken;
        Upper = upper;
        AdoptChildren();
    }

    public ExpressionSyntax Lower { get; }

    public SyntaxToken OperatorToken { get; }

    public ExpressionSyntax Upper { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Lower, OperatorToken, Upper);
}

/// <summary><c>is Enemy</c>.</summary>
public sealed class TypePatternSyntax : PatternSyntax
{
    internal TypePatternSyntax(SyntaxToken isKeyword, TypeSyntax type)
        : base(SyntaxKind.TypePattern)
    {
        IsKeyword = isKeyword;
        Type = type;
        AdoptChildren();
    }

    public SyntaxToken IsKeyword { get; }

    public TypeSyntax Type { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(IsKeyword, Type);
}

/// <summary>
/// An enum case by its bare name, optionally taking out its data by position like a deconstruction (ADR-0024):
/// <c>Idle</c>, <c>Burn(amount, seconds)</c>, <c>Burn(_, seconds)</c>. A bare name that turns out not to be a case
/// of the matched enum is reported during binding.
/// </summary>
public sealed class CasePatternSyntax : PatternSyntax
{
    internal CasePatternSyntax(SyntaxToken identifier, CaseFieldListSyntax? fields)
        : base(SyntaxKind.CasePattern)
    {
        Identifier = identifier;
        Fields = fields;
        AdoptChildren();
    }

    public SyntaxToken Identifier { get; }

    public CaseFieldListSyntax? Fields { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Identifier, Fields);
}

/// <summary>
/// The <c>(amount, seconds)</c> of a case pattern: one name per field in order, each becoming a local variable;
/// <c>_</c> discards a field.
/// </summary>
public sealed class CaseFieldListSyntax : SyntaxNode
{
    internal CaseFieldListSyntax(SyntaxToken openParenToken, SeparatedSyntaxList<IdentifierNameSyntax> fields, SyntaxToken closeParenToken)
        : base(SyntaxKind.CaseFieldList)
    {
        OpenParenToken = openParenToken;
        Fields = fields;
        CloseParenToken = closeParenToken;
        AdoptChildren();
    }

    public SyntaxToken OpenParenToken { get; }

    public SeparatedSyntaxList<IdentifierNameSyntax> Fields { get; }

    public SyntaxToken CloseParenToken { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(OpenParenToken, Fields, CloseParenToken);
}

public sealed class NotPatternSyntax : PatternSyntax
{
    internal NotPatternSyntax(SyntaxToken notKeyword, PatternSyntax pattern)
        : base(SyntaxKind.NotPattern)
    {
        NotKeyword = notKeyword;
        Pattern = pattern;
        AdoptChildren();
    }

    public SyntaxToken NotKeyword { get; }

    public PatternSyntax Pattern { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(NotKeyword, Pattern);
}

/// <summary><c>a and b</c> or <c>a or b</c> between patterns.</summary>
public sealed class BinaryPatternSyntax : PatternSyntax
{
    internal BinaryPatternSyntax(SyntaxKind kind, PatternSyntax left, SyntaxToken operatorToken, PatternSyntax right)
        : base(kind)
    {
        Left = left;
        OperatorToken = operatorToken;
        Right = right;
        AdoptChildren();
    }

    public PatternSyntax Left { get; }

    public SyntaxToken OperatorToken { get; }

    public PatternSyntax Right { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Left, OperatorToken, Right);
}
