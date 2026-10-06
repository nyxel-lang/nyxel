namespace Nyxel.Compiler.Syntax;

/// <summary>
/// A type. Types derive from <see cref="ExpressionSyntax"/> because names are both: <c>Slime</c> in
/// <c>Slime.Killed</c> and in <c>hp: Slime</c> is the same <see cref="IdentifierNameSyntax"/>; binding decides.
/// </summary>
public abstract class TypeSyntax : ExpressionSyntax
{
    private protected TypeSyntax(SyntaxKind kind)
        : base(kind)
    {
    }
}

public abstract class NameSyntax : TypeSyntax
{
    private protected NameSyntax(SyntaxKind kind)
        : base(kind)
    {
    }
}

public abstract class SimpleNameSyntax : NameSyntax
{
    private protected SimpleNameSyntax(SyntaxKind kind, SyntaxToken identifier)
        : base(kind) => Identifier = identifier;

    /// <summary>
    /// Usually an identifier token. After <c>self.</c> and <c>super.</c> it can be the <c>init</c> keyword
    /// (ADR-0013).
    /// </summary>
    public SyntaxToken Identifier { get; }
}

public sealed class IdentifierNameSyntax : SimpleNameSyntax
{
    internal IdentifierNameSyntax(SyntaxToken identifier)
        : base(SyntaxKind.IdentifierName, identifier) => AdoptChildren();

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Identifier);
}

/// <summary><c>List&lt;Enemy&gt;</c>, also in expressions: <c>World.Spawn&lt;Slime&gt;()</c>.</summary>
public sealed class GenericNameSyntax : SimpleNameSyntax
{
    internal GenericNameSyntax(SyntaxToken identifier, TypeArgumentListSyntax typeArgumentList)
        : base(SyntaxKind.GenericName, identifier)
    {
        TypeArgumentList = typeArgumentList;
        AdoptChildren();
    }

    public TypeArgumentListSyntax TypeArgumentList { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Identifier, TypeArgumentList);
}

/// <summary><c>System.Collections.Generic</c> in a namespace, import or type position.</summary>
public sealed class QualifiedNameSyntax : NameSyntax
{
    internal QualifiedNameSyntax(NameSyntax left, SyntaxToken dotToken, SimpleNameSyntax right)
        : base(SyntaxKind.QualifiedName)
    {
        Left = left;
        DotToken = dotToken;
        Right = right;
        AdoptChildren();
    }

    public NameSyntax Left { get; }

    public SyntaxToken DotToken { get; }

    public SimpleNameSyntax Right { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Left, DotToken, Right);
}

public sealed class TypeArgumentListSyntax : SyntaxNode
{
    internal TypeArgumentListSyntax(SyntaxToken lessThanToken, SeparatedSyntaxList<TypeSyntax> arguments, SyntaxToken greaterThanToken)
        : base(SyntaxKind.TypeArgumentList)
    {
        LessThanToken = lessThanToken;
        Arguments = arguments;
        GreaterThanToken = greaterThanToken;
        AdoptChildren();
    }

    public SyntaxToken LessThanToken { get; }

    public SeparatedSyntaxList<TypeSyntax> Arguments { get; }

    public SyntaxToken GreaterThanToken { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(LessThanToken, Arguments, GreaterThanToken);
}

/// <summary>A built-in type keyword such as <c>int</c>; also the receiver in <c>int.TryParse(...)</c>.</summary>
public sealed class PredefinedTypeSyntax : TypeSyntax
{
    internal PredefinedTypeSyntax(SyntaxToken keyword)
        : base(SyntaxKind.PredefinedType)
    {
        Keyword = keyword;
        AdoptChildren();
    }

    public SyntaxToken Keyword { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Keyword);
}

/// <summary><c>T?</c> (ADR-0009).</summary>
public sealed class NullableTypeSyntax : TypeSyntax
{
    internal NullableTypeSyntax(TypeSyntax elementType, SyntaxToken questionToken)
        : base(SyntaxKind.NullableType)
    {
        ElementType = elementType;
        QuestionToken = questionToken;
        AdoptChildren();
    }

    public TypeSyntax ElementType { get; }

    public SyntaxToken QuestionToken { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(ElementType, QuestionToken);
}

/// <summary><c>func(A, B) -&gt; R</c>, <c>func(A)</c>, <c>async func(A) -&gt; R</c> (ADR-0012, ADR-0016).</summary>
public sealed class FunctionTypeSyntax : TypeSyntax
{
    internal FunctionTypeSyntax(
        SyntaxToken? asyncKeyword,
        SyntaxToken funcKeyword,
        SyntaxToken openParenToken,
        SeparatedSyntaxList<TypeSyntax> parameterTypes,
        SyntaxToken closeParenToken,
        ReturnTypeClauseSyntax? returnType)
        : base(SyntaxKind.FunctionType)
    {
        AsyncKeyword = asyncKeyword;
        FuncKeyword = funcKeyword;
        OpenParenToken = openParenToken;
        ParameterTypes = parameterTypes;
        CloseParenToken = closeParenToken;
        ReturnType = returnType;
        AdoptChildren();
    }

    public SyntaxToken? AsyncKeyword { get; }

    public SyntaxToken FuncKeyword { get; }

    public SyntaxToken OpenParenToken { get; }

    public SeparatedSyntaxList<TypeSyntax> ParameterTypes { get; }

    public SyntaxToken CloseParenToken { get; }

    public ReturnTypeClauseSyntax? ReturnType { get; }

    public override IEnumerable<SyntaxNode> GetChildren() =>
        Children(AsyncKeyword, FuncKeyword, OpenParenToken, ParameterTypes, CloseParenToken, ReturnType);
}

/// <summary>
/// <c>array&lt;T&gt;</c>, the .NET array <c>T[]</c> (ADR-0020); <c>array2d&lt;T&gt;</c> and <c>array3d&lt;T&gt;</c>, the
/// multidimensional <c>T[,]</c> and <c>T[,,]</c> (ADR-0022). C#'s <c>T[]</c> and <c>T[,]</c> are reported and read into
/// this node with a missing keyword, <c>&lt;</c> and <c>&gt;</c>.
/// </summary>
public sealed class ArrayTypeSyntax : TypeSyntax
{
    internal ArrayTypeSyntax(SyntaxToken arrayKeyword, SyntaxToken lessThanToken, TypeSyntax elementType, SyntaxToken greaterThanToken)
        : base(SyntaxKind.ArrayType)
    {
        ArrayKeyword = arrayKeyword;
        LessThanToken = lessThanToken;
        ElementType = elementType;
        GreaterThanToken = greaterThanToken;
        AdoptChildren();
    }

    /// <summary><c>array</c>, <c>array2d</c> or <c>array3d</c>.</summary>
    public SyntaxToken ArrayKeyword { get; }

    public SyntaxToken LessThanToken { get; }

    public TypeSyntax ElementType { get; }

    /// <summary>The number of dimensions: 1, 2 or 3.</summary>
    public int Rank => ArrayKeyword.Kind switch
    {
        SyntaxKind.Array2DKeyword => 2,
        SyntaxKind.Array3DKeyword => 3,
        _ => 1,
    };

    public SyntaxToken GreaterThanToken { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(ArrayKeyword, LessThanToken, ElementType, GreaterThanToken);
}

/// <summary>
/// <c>(Min: int, Max: int)</c>, the .NET <c>ValueTuple</c> (ADR-0021). Every element is named; C#'s <c>(int Min, ...)</c>
/// and the unnamed <c>(int, int)</c> are reported and read into elements with a missing name and colon.
/// </summary>
public sealed class TupleTypeSyntax : TypeSyntax
{
    internal TupleTypeSyntax(SyntaxToken openParenToken, SeparatedSyntaxList<TupleElementSyntax> elements, SyntaxToken closeParenToken)
        : base(SyntaxKind.TupleType)
    {
        OpenParenToken = openParenToken;
        Elements = elements;
        CloseParenToken = closeParenToken;
        AdoptChildren();
    }

    public SyntaxToken OpenParenToken { get; }

    public SeparatedSyntaxList<TupleElementSyntax> Elements { get; }

    public SyntaxToken CloseParenToken { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(OpenParenToken, Elements, CloseParenToken);
}

/// <summary><c>Min: int</c> in a tuple type.</summary>
public sealed class TupleElementSyntax : SyntaxNode
{
    internal TupleElementSyntax(SyntaxToken identifier, SyntaxToken colonToken, TypeSyntax type)
        : base(SyntaxKind.TupleElement)
    {
        Identifier = identifier;
        ColonToken = colonToken;
        Type = type;
        AdoptChildren();
    }

    public SyntaxToken Identifier { get; }

    public SyntaxToken ColonToken { get; }

    public TypeSyntax Type { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Identifier, ColonToken, Type);
}
