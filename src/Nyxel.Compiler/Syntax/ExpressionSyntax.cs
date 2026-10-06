namespace Nyxel.Compiler.Syntax;

public abstract class ExpressionSyntax : SyntaxNode
{
    private protected ExpressionSyntax(SyntaxKind kind)
        : base(kind)
    {
    }
}

/// <summary>A number, string, <c>true</c>, <c>false</c> or <c>null</c>.</summary>
public sealed class LiteralExpressionSyntax : ExpressionSyntax
{
    internal LiteralExpressionSyntax(SyntaxKind kind, SyntaxToken token)
        : base(kind)
    {
        Token = token;
        AdoptChildren();
    }

    public SyntaxToken Token { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Token);
}

/// <summary><c>$"HP: {self.hp}"</c>; the contents alternate text and interpolations (ADR-0007).</summary>
public sealed class InterpolatedStringExpressionSyntax : ExpressionSyntax
{
    internal InterpolatedStringExpressionSyntax(
        SyntaxToken stringStartToken,
        SyntaxList<InterpolatedStringContentSyntax> contents,
        SyntaxToken stringEndToken)
        : base(SyntaxKind.InterpolatedStringExpression)
    {
        StringStartToken = stringStartToken;
        Contents = contents;
        StringEndToken = stringEndToken;
        AdoptChildren();
    }

    public SyntaxToken StringStartToken { get; }

    public SyntaxList<InterpolatedStringContentSyntax> Contents { get; }

    public SyntaxToken StringEndToken { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(StringStartToken, Contents, StringEndToken);
}

public abstract class InterpolatedStringContentSyntax : SyntaxNode
{
    private protected InterpolatedStringContentSyntax(SyntaxKind kind)
        : base(kind)
    {
    }
}

/// <summary>Literal text between interpolations; the token's value has <c>{{</c> / <c>}}</c> and escapes decoded.</summary>
public sealed class InterpolatedStringTextSyntax : InterpolatedStringContentSyntax
{
    internal InterpolatedStringTextSyntax(SyntaxToken textToken)
        : base(SyntaxKind.InterpolatedStringText)
    {
        TextToken = textToken;
        AdoptChildren();
    }

    public SyntaxToken TextToken { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(TextToken);
}

/// <summary><c>{expression,alignment:format}</c>.</summary>
public sealed class InterpolationSyntax : InterpolatedStringContentSyntax
{
    internal InterpolationSyntax(
        SyntaxToken openBraceToken,
        ExpressionSyntax expression,
        InterpolationAlignmentClauseSyntax? alignmentClause,
        InterpolationFormatClauseSyntax? formatClause,
        SyntaxToken closeBraceToken)
        : base(SyntaxKind.Interpolation)
    {
        OpenBraceToken = openBraceToken;
        Expression = expression;
        AlignmentClause = alignmentClause;
        FormatClause = formatClause;
        CloseBraceToken = closeBraceToken;
        AdoptChildren();
    }

    public SyntaxToken OpenBraceToken { get; }

    public ExpressionSyntax Expression { get; }

    public InterpolationAlignmentClauseSyntax? AlignmentClause { get; }

    public InterpolationFormatClauseSyntax? FormatClause { get; }

    public SyntaxToken CloseBraceToken { get; }

    public override IEnumerable<SyntaxNode> GetChildren() =>
        Children(OpenBraceToken, Expression, AlignmentClause, FormatClause, CloseBraceToken);
}

public sealed class InterpolationAlignmentClauseSyntax : SyntaxNode
{
    internal InterpolationAlignmentClauseSyntax(SyntaxToken commaToken, ExpressionSyntax value)
        : base(SyntaxKind.InterpolationAlignmentClause)
    {
        CommaToken = commaToken;
        Value = value;
        AdoptChildren();
    }

    public SyntaxToken CommaToken { get; }

    public ExpressionSyntax Value { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(CommaToken, Value);
}

public sealed class InterpolationFormatClauseSyntax : SyntaxNode
{
    internal InterpolationFormatClauseSyntax(SyntaxToken colonToken, SyntaxToken formatStringToken)
        : base(SyntaxKind.InterpolationFormatClause)
    {
        ColonToken = colonToken;
        FormatStringToken = formatStringToken;
        AdoptChildren();
    }

    public SyntaxToken ColonToken { get; }

    public SyntaxToken FormatStringToken { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(ColonToken, FormatStringToken);
}

/// <summary><c>self</c> or <c>super</c>; <see cref="SyntaxNode.Kind"/> tells which.</summary>
public sealed class InstanceExpressionSyntax : ExpressionSyntax
{
    internal InstanceExpressionSyntax(SyntaxKind kind, SyntaxToken keyword)
        : base(kind)
    {
        Keyword = keyword;
        AdoptChildren();
    }

    public SyntaxToken Keyword { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Keyword);
}

public sealed class ParenthesizedExpressionSyntax : ExpressionSyntax
{
    internal ParenthesizedExpressionSyntax(SyntaxToken openParenToken, ExpressionSyntax expression, SyntaxToken closeParenToken)
        : base(SyntaxKind.ParenthesizedExpression)
    {
        OpenParenToken = openParenToken;
        Expression = expression;
        CloseParenToken = closeParenToken;
        AdoptChildren();
    }

    public SyntaxToken OpenParenToken { get; }

    public ExpressionSyntax Expression { get; }

    public SyntaxToken CloseParenToken { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(OpenParenToken, Expression, CloseParenToken);
}

/// <summary><c>a.b</c> or <c>a?.b</c>.</summary>
public sealed class MemberAccessExpressionSyntax : ExpressionSyntax
{
    internal MemberAccessExpressionSyntax(SyntaxKind kind, ExpressionSyntax expression, SyntaxToken operatorToken, SimpleNameSyntax name)
        : base(kind)
    {
        Expression = expression;
        OperatorToken = operatorToken;
        Name = name;
        AdoptChildren();
    }

    public ExpressionSyntax Expression { get; }

    public SyntaxToken OperatorToken { get; }

    public SimpleNameSyntax Name { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Expression, OperatorToken, Name);
}

public sealed class InvocationExpressionSyntax : ExpressionSyntax
{
    internal InvocationExpressionSyntax(ExpressionSyntax expression, ArgumentListSyntax argumentList)
        : base(SyntaxKind.InvocationExpression)
    {
        Expression = expression;
        ArgumentList = argumentList;
        AdoptChildren();
    }

    public ExpressionSyntax Expression { get; }

    public ArgumentListSyntax ArgumentList { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Expression, ArgumentList);
}

/// <summary><c>a[i]</c>.</summary>
public sealed class ElementAccessExpressionSyntax : ExpressionSyntax
{
    internal ElementAccessExpressionSyntax(ExpressionSyntax expression, ArgumentListSyntax argumentList)
        : base(SyntaxKind.ElementAccessExpression)
    {
        Expression = expression;
        ArgumentList = argumentList;
        AdoptChildren();
    }

    public ExpressionSyntax Expression { get; }

    /// <summary>A <see cref="SyntaxKind.BracketedArgumentList"/>.</summary>
    public ArgumentListSyntax ArgumentList { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Expression, ArgumentList);
}

/// <summary><c>(a, b)</c> of a call or <c>[i]</c> of an element access (kind <see cref="SyntaxKind.BracketedArgumentList"/>).</summary>
public sealed class ArgumentListSyntax : SyntaxNode
{
    internal ArgumentListSyntax(SyntaxKind kind, SyntaxToken openToken, SeparatedSyntaxList<ArgumentSyntax> arguments, SyntaxToken closeToken)
        : base(kind)
    {
        OpenToken = openToken;
        Arguments = arguments;
        CloseToken = closeToken;
        AdoptChildren();
    }

    public SyntaxToken OpenToken { get; }

    public SeparatedSyntaxList<ArgumentSyntax> Arguments { get; }

    public SyntaxToken CloseToken { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(OpenToken, Arguments, CloseToken);
}

/// <summary><c>value</c>, <c>name = value</c>, <c>out let x</c>, <c>ref self.velocity</c> (ADR-0011, ADR-0015).</summary>
public sealed class ArgumentSyntax : SyntaxNode
{
    internal ArgumentSyntax(NameEqualsSyntax? nameEquals, SyntaxToken? refKindKeyword, ExpressionSyntax expression)
        : base(SyntaxKind.Argument)
    {
        NameEquals = nameEquals;
        RefKindKeyword = refKindKeyword;
        Expression = expression;
        AdoptChildren();
    }

    public NameEqualsSyntax? NameEquals { get; }

    /// <summary><c>out</c> or <c>ref</c>.</summary>
    public SyntaxToken? RefKindKeyword { get; }

    public ExpressionSyntax Expression { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(NameEquals, RefKindKeyword, Expression);
}

/// <summary>The <c>name =</c> of a named argument.</summary>
public sealed class NameEqualsSyntax : SyntaxNode
{
    internal NameEqualsSyntax(IdentifierNameSyntax name, SyntaxToken equalsToken)
        : base(SyntaxKind.NameEquals)
    {
        Name = name;
        EqualsToken = equalsToken;
        AdoptChildren();
    }

    public IdentifierNameSyntax Name { get; }

    /// <summary>The <c>=</c>; a ':' token when the parser recovered from the C# form <c>name: value</c>.</summary>
    public SyntaxToken EqualsToken { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Name, EqualsToken);
}

/// <summary>The <c>let x</c> / <c>var x: int</c> of <c>out let x</c>.</summary>
public sealed class DeclarationExpressionSyntax : ExpressionSyntax
{
    internal DeclarationExpressionSyntax(SyntaxToken keyword, SyntaxToken identifier, TypeAnnotationSyntax? typeAnnotation)
        : base(SyntaxKind.DeclarationExpression)
    {
        Keyword = keyword;
        Identifier = identifier;
        TypeAnnotation = typeAnnotation;
        AdoptChildren();
    }

    public SyntaxToken Keyword { get; }

    public SyntaxToken Identifier { get; }

    public TypeAnnotationSyntax? TypeAnnotation { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Keyword, Identifier, TypeAnnotation);
}

/// <summary><c>new Type(args)</c>; also named inits and data cases: <c>new Damage.Burn(amount = 3)</c> (ADR-0008).</summary>
public sealed class ObjectCreationExpressionSyntax : ExpressionSyntax
{
    internal ObjectCreationExpressionSyntax(SyntaxToken newKeyword, TypeSyntax type, ArgumentListSyntax argumentList)
        : base(SyntaxKind.ObjectCreationExpression)
    {
        NewKeyword = newKeyword;
        Type = type;
        ArgumentList = argumentList;
        AdoptChildren();
    }

    public SyntaxToken NewKeyword { get; }

    public TypeSyntax Type { get; }

    public ArgumentListSyntax ArgumentList { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(NewKeyword, Type, ArgumentList);
}

/// <summary><c>func(e) = e.Hp &gt; 0</c>, <c>func(e: Enemy) -&gt; float { ... }</c>, <c>async func() { }</c> (ADR-0012).</summary>
public sealed class LambdaExpressionSyntax : ExpressionSyntax
{
    internal LambdaExpressionSyntax(
        SyntaxToken? asyncKeyword,
        SyntaxToken funcKeyword,
        ParameterListSyntax parameterList,
        ReturnTypeClauseSyntax? returnType,
        BlockSyntax? body,
        ExpressionBodySyntax? expressionBody)
        : base(SyntaxKind.LambdaExpression)
    {
        AsyncKeyword = asyncKeyword;
        FuncKeyword = funcKeyword;
        ParameterList = parameterList;
        ReturnType = returnType;
        Body = body;
        ExpressionBody = expressionBody;
        AdoptChildren();
    }

    public SyntaxToken? AsyncKeyword { get; }

    public SyntaxToken FuncKeyword { get; }

    public ParameterListSyntax ParameterList { get; }

    public ReturnTypeClauseSyntax? ReturnType { get; }

    public BlockSyntax? Body { get; }

    public ExpressionBodySyntax? ExpressionBody { get; }

    public override IEnumerable<SyntaxNode> GetChildren() =>
        Children(AsyncKeyword, FuncKeyword, ParameterList, ReturnType, Body, ExpressionBody);
}

/// <summary><c>if condition { } else { }</c>, a statement or an expression (ADR-0007).</summary>
public sealed class IfExpressionSyntax : ExpressionSyntax
{
    internal IfExpressionSyntax(SyntaxToken ifKeyword, ExpressionSyntax condition, BlockSyntax block, ElseClauseSyntax? elseClause)
        : base(SyntaxKind.IfExpression)
    {
        IfKeyword = ifKeyword;
        Condition = condition;
        Block = block;
        ElseClause = elseClause;
        AdoptChildren();
    }

    public SyntaxToken IfKeyword { get; }

    public ExpressionSyntax Condition { get; }

    public BlockSyntax Block { get; }

    public ElseClauseSyntax? ElseClause { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(IfKeyword, Condition, Block, ElseClause);
}

public sealed class ElseClauseSyntax : SyntaxNode
{
    internal ElseClauseSyntax(SyntaxToken elseKeyword, SyntaxNode body)
        : base(SyntaxKind.ElseClause)
    {
        ElseKeyword = elseKeyword;
        Body = body;
        AdoptChildren();
    }

    public SyntaxToken ElseKeyword { get; }

    /// <summary>A <see cref="BlockSyntax"/>, or an <see cref="IfExpressionSyntax"/> for <c>else if</c>.</summary>
    public SyntaxNode Body { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(ElseKeyword, Body);
}

/// <summary><c>match value { case ... -&gt; ... }</c> (ADR-0009).</summary>
public sealed class MatchExpressionSyntax : ExpressionSyntax
{
    internal MatchExpressionSyntax(
        SyntaxToken matchKeyword,
        ExpressionSyntax expression,
        SyntaxToken openBraceToken,
        SyntaxList<MatchArmSyntax> arms,
        SyntaxToken closeBraceToken)
        : base(SyntaxKind.MatchExpression)
    {
        MatchKeyword = matchKeyword;
        Expression = expression;
        OpenBraceToken = openBraceToken;
        Arms = arms;
        CloseBraceToken = closeBraceToken;
        AdoptChildren();
    }

    public SyntaxToken MatchKeyword { get; }

    public ExpressionSyntax Expression { get; }

    public SyntaxToken OpenBraceToken { get; }

    public SyntaxList<MatchArmSyntax> Arms { get; }

    public SyntaxToken CloseBraceToken { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(MatchKeyword, Expression, OpenBraceToken, Arms, CloseBraceToken);
}

/// <summary><c>case pattern when condition -&gt; body</c> or <c>else -&gt; body</c>.</summary>
public sealed class MatchArmSyntax : SyntaxNode
{
    internal MatchArmSyntax(SyntaxToken keyword, PatternSyntax? pattern, WhenClauseSyntax? whenClause, SyntaxToken arrowToken, SyntaxNode body)
        : base(SyntaxKind.MatchArm)
    {
        Keyword = keyword;
        Pattern = pattern;
        WhenClause = whenClause;
        ArrowToken = arrowToken;
        Body = body;
        AdoptChildren();
    }

    /// <summary><c>case</c>, or <c>else</c> for the default arm (which has no pattern).</summary>
    public SyntaxToken Keyword { get; }

    public PatternSyntax? Pattern { get; }

    public WhenClauseSyntax? WhenClause { get; }

    public SyntaxToken ArrowToken { get; }

    /// <summary>An <see cref="ExpressionSyntax"/> or a <see cref="BlockSyntax"/>.</summary>
    public SyntaxNode Body { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Keyword, Pattern, WhenClause, ArrowToken, Body);
}

/// <summary><c>when condition</c> of a match arm or a catch clause.</summary>
public sealed class WhenClauseSyntax : SyntaxNode
{
    internal WhenClauseSyntax(SyntaxToken whenKeyword, ExpressionSyntax condition)
        : base(SyntaxKind.WhenClause)
    {
        WhenKeyword = whenKeyword;
        Condition = condition;
        AdoptChildren();
    }

    public SyntaxToken WhenKeyword { get; }

    public ExpressionSyntax Condition { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(WhenKeyword, Condition);
}

/// <summary><c>try { } catch e: T { } finally { }</c>, a statement or an expression (ADR-0014).</summary>
public sealed class TryExpressionSyntax : ExpressionSyntax
{
    internal TryExpressionSyntax(SyntaxToken tryKeyword, BlockSyntax block, SyntaxList<CatchClauseSyntax> catches, FinallyClauseSyntax? @finally)
        : base(SyntaxKind.TryExpression)
    {
        TryKeyword = tryKeyword;
        Block = block;
        Catches = catches;
        Finally = @finally;
        AdoptChildren();
    }

    public SyntaxToken TryKeyword { get; }

    public BlockSyntax Block { get; }

    public SyntaxList<CatchClauseSyntax> Catches { get; }

    public FinallyClauseSyntax? Finally { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(TryKeyword, Block, Catches, Finally);
}

/// <summary><c>catch e: IOException when e.HResult == 32 { }</c>.</summary>
public sealed class CatchClauseSyntax : SyntaxNode
{
    internal CatchClauseSyntax(
        SyntaxToken catchKeyword,
        SyntaxToken identifier,
        TypeAnnotationSyntax typeAnnotation,
        WhenClauseSyntax? filter,
        BlockSyntax block)
        : base(SyntaxKind.CatchClause)
    {
        CatchKeyword = catchKeyword;
        Identifier = identifier;
        TypeAnnotation = typeAnnotation;
        Filter = filter;
        Block = block;
        AdoptChildren();
    }

    public SyntaxToken CatchKeyword { get; }

    public SyntaxToken Identifier { get; }

    public TypeAnnotationSyntax TypeAnnotation { get; }

    public WhenClauseSyntax? Filter { get; }

    public BlockSyntax Block { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(CatchKeyword, Identifier, TypeAnnotation, Filter, Block);
}

public sealed class FinallyClauseSyntax : SyntaxNode
{
    internal FinallyClauseSyntax(SyntaxToken finallyKeyword, BlockSyntax block)
        : base(SyntaxKind.FinallyClause)
    {
        FinallyKeyword = finallyKeyword;
        Block = block;
        AdoptChildren();
    }

    public SyntaxToken FinallyKeyword { get; }

    public BlockSyntax Block { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(FinallyKeyword, Block);
}

/// <summary>
/// <c>return</c> / <c>throw</c> / <c>break</c> / <c>continue</c> on the right of <c>??</c> (ADR-0009); the only
/// place they are expressions.
/// </summary>
public sealed class JumpExpressionSyntax : ExpressionSyntax
{
    internal JumpExpressionSyntax(SyntaxKind kind, SyntaxToken keyword, ExpressionSyntax? expression)
        : base(kind)
    {
        Keyword = keyword;
        Expression = expression;
        AdoptChildren();
    }

    public SyntaxToken Keyword { get; }

    public ExpressionSyntax? Expression { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Keyword, Expression);
}

/// <summary><c>-x</c>, <c>+x</c>, <c>~x</c>, <c>not x</c>, <c>await x</c>, <c>launch x</c>.</summary>
public sealed class UnaryExpressionSyntax : ExpressionSyntax
{
    internal UnaryExpressionSyntax(SyntaxKind kind, SyntaxToken operatorToken, ExpressionSyntax operand)
        : base(kind)
    {
        OperatorToken = operatorToken;
        Operand = operand;
        AdoptChildren();
    }

    public SyntaxToken OperatorToken { get; }

    public ExpressionSyntax Operand { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(OperatorToken, Operand);
}

public sealed class BinaryExpressionSyntax : ExpressionSyntax
{
    internal BinaryExpressionSyntax(SyntaxKind kind, ExpressionSyntax left, SyntaxToken operatorToken, ExpressionSyntax right)
        : base(kind)
    {
        Left = left;
        OperatorToken = operatorToken;
        Right = right;
        AdoptChildren();
    }

    public ExpressionSyntax Left { get; }

    public SyntaxToken OperatorToken { get; }

    public ExpressionSyntax Right { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Left, OperatorToken, Right);
}

/// <summary><c>x as T</c> (ADR-0014).</summary>
public sealed class AsExpressionSyntax : ExpressionSyntax
{
    internal AsExpressionSyntax(ExpressionSyntax expression, SyntaxToken asKeyword, TypeSyntax type)
        : base(SyntaxKind.AsExpression)
    {
        Expression = expression;
        AsKeyword = asKeyword;
        Type = type;
        AdoptChildren();
    }

    public ExpressionSyntax Expression { get; }

    public SyntaxToken AsKeyword { get; }

    public TypeSyntax Type { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Expression, AsKeyword, Type);
}

/// <summary><c>x is T</c> or <c>x is not T</c> (ADR-0010).</summary>
public sealed class IsExpressionSyntax : ExpressionSyntax
{
    internal IsExpressionSyntax(ExpressionSyntax expression, SyntaxToken isKeyword, SyntaxToken? notKeyword, TypeSyntax type)
        : base(SyntaxKind.IsExpression)
    {
        Expression = expression;
        IsKeyword = isKeyword;
        NotKeyword = notKeyword;
        Type = type;
        AdoptChildren();
    }

    public ExpressionSyntax Expression { get; }

    public SyntaxToken IsKeyword { get; }

    public SyntaxToken? NotKeyword { get; }

    public TypeSyntax Type { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Expression, IsKeyword, NotKeyword, Type);
}

/// <summary><c>a..&lt;b</c> or <c>a...b</c> (ADR-0010, ADR-0019).</summary>
public sealed class RangeExpressionSyntax : ExpressionSyntax
{
    internal RangeExpressionSyntax(ExpressionSyntax left, SyntaxToken operatorToken, ExpressionSyntax right)
        : base(SyntaxKind.RangeExpression)
    {
        Left = left;
        OperatorToken = operatorToken;
        Right = right;
        AdoptChildren();
    }

    public ExpressionSyntax Left { get; }

    public SyntaxToken OperatorToken { get; }

    public ExpressionSyntax Right { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Left, OperatorToken, Right);
}
