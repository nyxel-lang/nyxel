namespace Nyxel.Compiler.Syntax;

/// <summary>A whole file: optional namespace, imports, then type declarations (ADR-0012).</summary>
public sealed class CompilationUnitSyntax : SyntaxNode
{
    internal CompilationUnitSyntax(
        NamespaceDeclarationSyntax? @namespace,
        SyntaxList<ImportDirectiveSyntax> imports,
        SyntaxList<MemberDeclarationSyntax> members,
        SyntaxToken endOfFileToken)
        : base(SyntaxKind.CompilationUnit)
    {
        Namespace = @namespace;
        Imports = imports;
        Members = members;
        EndOfFileToken = endOfFileToken;
        AdoptChildren();
    }

    public NamespaceDeclarationSyntax? Namespace { get; }

    public SyntaxList<ImportDirectiveSyntax> Imports { get; }

    public SyntaxList<MemberDeclarationSyntax> Members { get; }

    /// <summary>Carries the trivia at the end of the file, such as a trailing comment.</summary>
    public SyntaxToken EndOfFileToken { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Namespace, Imports, Members, EndOfFileToken);
}

public sealed class NamespaceDeclarationSyntax : SyntaxNode
{
    internal NamespaceDeclarationSyntax(SyntaxToken namespaceKeyword, NameSyntax name)
        : base(SyntaxKind.NamespaceDeclaration)
    {
        NamespaceKeyword = namespaceKeyword;
        Name = name;
        AdoptChildren();
    }

    public SyntaxToken NamespaceKeyword { get; }

    public NameSyntax Name { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(NamespaceKeyword, Name);
}

public sealed class ImportDirectiveSyntax : SyntaxNode
{
    internal ImportDirectiveSyntax(SyntaxToken importKeyword, NameSyntax name)
        : base(SyntaxKind.ImportDirective)
    {
        ImportKeyword = importKeyword;
        Name = name;
        AdoptChildren();
    }

    public SyntaxToken ImportKeyword { get; }

    public NameSyntax Name { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(ImportKeyword, Name);
}

/// <summary>A declaration that can carry modifiers: a type, an extension or a member of a type.</summary>
public abstract class MemberDeclarationSyntax : SyntaxNode
{
    private protected MemberDeclarationSyntax(SyntaxKind kind, SyntaxList<SyntaxToken> modifiers)
        : base(kind) => Modifiers = modifiers;

    public SyntaxList<SyntaxToken> Modifiers { get; }
}

/// <summary><c>class</c>, <c>struct</c> or <c>interface</c>; <see cref="SyntaxNode.Kind"/> tells which.</summary>
public sealed class TypeDeclarationSyntax : MemberDeclarationSyntax
{
    internal TypeDeclarationSyntax(
        SyntaxKind kind,
        SyntaxList<SyntaxToken> modifiers,
        SyntaxToken keyword,
        SyntaxToken identifier,
        TypeParameterListSyntax? typeParameterList,
        BaseListSyntax? baseList,
        SyntaxToken openBraceToken,
        SyntaxList<MemberDeclarationSyntax> members,
        SyntaxToken closeBraceToken)
        : base(kind, modifiers)
    {
        Keyword = keyword;
        Identifier = identifier;
        TypeParameterList = typeParameterList;
        BaseList = baseList;
        OpenBraceToken = openBraceToken;
        Members = members;
        CloseBraceToken = closeBraceToken;
        AdoptChildren();
    }

    public SyntaxToken Keyword { get; }

    public SyntaxToken Identifier { get; }

    public TypeParameterListSyntax? TypeParameterList { get; }

    public BaseListSyntax? BaseList { get; }

    public SyntaxToken OpenBraceToken { get; }

    public SyntaxList<MemberDeclarationSyntax> Members { get; }

    public SyntaxToken CloseBraceToken { get; }

    public override IEnumerable<SyntaxNode> GetChildren() =>
        Children(Modifiers, Keyword, Identifier, TypeParameterList, BaseList, OpenBraceToken, Members, CloseBraceToken);
}

/// <summary>
/// <c>enum Tile : byte { ... }</c>, <c>flags enum Layer { ... }</c> or <c>struct enum AiState { ... }</c> (ADR-0008,
/// ADR-0024). The base list holds the underlying type.
/// </summary>
public sealed class EnumDeclarationSyntax : MemberDeclarationSyntax
{
    internal EnumDeclarationSyntax(
        SyntaxList<SyntaxToken> modifiers,
        SyntaxToken? kindKeyword,
        SyntaxToken enumKeyword,
        SyntaxToken identifier,
        BaseListSyntax? baseList,
        SyntaxToken openBraceToken,
        SyntaxList<EnumCaseDeclarationSyntax> cases,
        SyntaxToken closeBraceToken)
        : base(SyntaxKind.EnumDeclaration, modifiers)
    {
        KindKeyword = kindKeyword;
        EnumKeyword = enumKeyword;
        Identifier = identifier;
        BaseList = baseList;
        OpenBraceToken = openBraceToken;
        Cases = cases;
        CloseBraceToken = closeBraceToken;
        AdoptChildren();
    }

    /// <summary>The <c>flags</c> (a contextual keyword) or <c>struct</c> before <c>enum</c>, if any.</summary>
    public SyntaxToken? KindKeyword { get; }

    public bool IsFlags => KindKeyword?.Kind == SyntaxKind.IdentifierToken;

    public bool IsStruct => KindKeyword?.Kind == SyntaxKind.StructKeyword;

    public SyntaxToken EnumKeyword { get; }

    public SyntaxToken Identifier { get; }

    public BaseListSyntax? BaseList { get; }

    public SyntaxToken OpenBraceToken { get; }

    public SyntaxList<EnumCaseDeclarationSyntax> Cases { get; }

    public SyntaxToken CloseBraceToken { get; }

    public override IEnumerable<SyntaxNode> GetChildren() =>
        Children(Modifiers, KindKeyword, EnumKeyword, Identifier, BaseList, OpenBraceToken, Cases, CloseBraceToken);
}

/// <summary>
/// <c>case Burn(Amount: int, Seconds: float)</c> or <c>case Mesh = 1</c>. The parameter list holds the case's data,
/// named like tuple elements (ADR-0024); the value is for enums without data.
/// </summary>
public sealed class EnumCaseDeclarationSyntax : SyntaxNode
{
    internal EnumCaseDeclarationSyntax(
        SyntaxToken caseKeyword, SyntaxToken identifier, ParameterListSyntax? parameterList, EqualsValueClauseSyntax? equalsValue)
        : base(SyntaxKind.EnumCaseDeclaration)
    {
        CaseKeyword = caseKeyword;
        Identifier = identifier;
        ParameterList = parameterList;
        EqualsValue = equalsValue;
        AdoptChildren();
    }

    public SyntaxToken CaseKeyword { get; }

    public SyntaxToken Identifier { get; }

    public ParameterListSyntax? ParameterList { get; }

    public EqualsValueClauseSyntax? EqualsValue { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(CaseKeyword, Identifier, ParameterList, EqualsValue);
}

/// <summary><c>extension Vector3 { ... }</c> (ADR-0014).</summary>
public sealed class ExtensionDeclarationSyntax : MemberDeclarationSyntax
{
    internal ExtensionDeclarationSyntax(
        SyntaxList<SyntaxToken> modifiers,
        SyntaxToken extensionKeyword,
        TypeSyntax extendedType,
        SyntaxToken openBraceToken,
        SyntaxList<MemberDeclarationSyntax> members,
        SyntaxToken closeBraceToken)
        : base(SyntaxKind.ExtensionDeclaration, modifiers)
    {
        ExtensionKeyword = extensionKeyword;
        ExtendedType = extendedType;
        OpenBraceToken = openBraceToken;
        Members = members;
        CloseBraceToken = closeBraceToken;
        AdoptChildren();
    }

    public SyntaxToken ExtensionKeyword { get; }

    public TypeSyntax ExtendedType { get; }

    public SyntaxToken OpenBraceToken { get; }

    public SyntaxList<MemberDeclarationSyntax> Members { get; }

    public SyntaxToken CloseBraceToken { get; }

    public override IEnumerable<SyntaxNode> GetChildren() =>
        Children(Modifiers, ExtensionKeyword, ExtendedType, OpenBraceToken, Members, CloseBraceToken);
}

/// <summary>A <c>let</c> or <c>var</c> field. The type annotation is required; the parser reports it when absent.</summary>
public sealed class FieldDeclarationSyntax : MemberDeclarationSyntax
{
    internal FieldDeclarationSyntax(
        SyntaxList<SyntaxToken> modifiers,
        SyntaxToken keyword,
        SyntaxToken identifier,
        TypeAnnotationSyntax? typeAnnotation,
        EqualsValueClauseSyntax? initializer)
        : base(SyntaxKind.FieldDeclaration, modifiers)
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

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Modifiers, Keyword, Identifier, TypeAnnotation, Initializer);
}

/// <summary>
/// <c>func Name&lt;T&gt;(params) -&gt; R</c> followed by a block, <c>= expression</c>, or nothing (interface and
/// abstract members). Whether a body may be absent is checked during binding.
/// </summary>
public sealed class FunctionDeclarationSyntax : MemberDeclarationSyntax
{
    internal FunctionDeclarationSyntax(
        SyntaxList<SyntaxToken> modifiers,
        SyntaxToken funcKeyword,
        SyntaxToken identifier,
        TypeParameterListSyntax? typeParameterList,
        ParameterListSyntax parameterList,
        ReturnTypeClauseSyntax? returnType,
        BlockSyntax? body,
        ExpressionBodySyntax? expressionBody)
        : base(SyntaxKind.FunctionDeclaration, modifiers)
    {
        FuncKeyword = funcKeyword;
        Identifier = identifier;
        TypeParameterList = typeParameterList;
        ParameterList = parameterList;
        ReturnType = returnType;
        Body = body;
        ExpressionBody = expressionBody;
        AdoptChildren();
    }

    public SyntaxToken FuncKeyword { get; }

    public SyntaxToken Identifier { get; }

    public TypeParameterListSyntax? TypeParameterList { get; }

    public ParameterListSyntax ParameterList { get; }

    public ReturnTypeClauseSyntax? ReturnType { get; }

    public BlockSyntax? Body { get; }

    public ExpressionBodySyntax? ExpressionBody { get; }

    public override IEnumerable<SyntaxNode> GetChildren() =>
        Children(Modifiers, FuncKeyword, Identifier, TypeParameterList, ParameterList, ReturnType, Body, ExpressionBody);
}

/// <summary><c>init(params) { }</c> or the named form <c>init FromPolar(params) { }</c> (ADR-0008).</summary>
public sealed class InitDeclarationSyntax : MemberDeclarationSyntax
{
    internal InitDeclarationSyntax(
        SyntaxList<SyntaxToken> modifiers,
        SyntaxToken initKeyword,
        SyntaxToken? identifier,
        ParameterListSyntax parameterList,
        BlockSyntax body)
        : base(SyntaxKind.InitDeclaration, modifiers)
    {
        InitKeyword = initKeyword;
        Identifier = identifier;
        ParameterList = parameterList;
        Body = body;
        AdoptChildren();
    }

    public SyntaxToken InitKeyword { get; }

    /// <summary>The name of a named init; null for the unnamed one.</summary>
    public SyntaxToken? Identifier { get; }

    public ParameterListSyntax ParameterList { get; }

    public BlockSyntax Body { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Modifiers, InitKeyword, Identifier, ParameterList, Body);
}

/// <summary><c>property Name: Type [= value] { accessors }</c> (ADR-0011).</summary>
public sealed class PropertyDeclarationSyntax : MemberDeclarationSyntax
{
    internal PropertyDeclarationSyntax(
        SyntaxList<SyntaxToken> modifiers,
        SyntaxToken propertyKeyword,
        SyntaxToken identifier,
        TypeAnnotationSyntax typeAnnotation,
        EqualsValueClauseSyntax? initializer,
        AccessorListSyntax accessorList)
        : base(SyntaxKind.PropertyDeclaration, modifiers)
    {
        PropertyKeyword = propertyKeyword;
        Identifier = identifier;
        TypeAnnotation = typeAnnotation;
        Initializer = initializer;
        AccessorList = accessorList;
        AdoptChildren();
    }

    public SyntaxToken PropertyKeyword { get; }

    public SyntaxToken Identifier { get; }

    public TypeAnnotationSyntax TypeAnnotation { get; }

    public EqualsValueClauseSyntax? Initializer { get; }

    public AccessorListSyntax AccessorList { get; }

    public override IEnumerable<SyntaxNode> GetChildren() =>
        Children(Modifiers, PropertyKeyword, Identifier, TypeAnnotation, Initializer, AccessorList);
}

public sealed class AccessorListSyntax : SyntaxNode
{
    internal AccessorListSyntax(SyntaxToken openBraceToken, SyntaxList<AccessorDeclarationSyntax> accessors, SyntaxToken closeBraceToken)
        : base(SyntaxKind.AccessorList)
    {
        OpenBraceToken = openBraceToken;
        Accessors = accessors;
        CloseBraceToken = closeBraceToken;
        AdoptChildren();
    }

    public SyntaxToken OpenBraceToken { get; }

    public SyntaxList<AccessorDeclarationSyntax> Accessors { get; }

    public SyntaxToken CloseBraceToken { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(OpenBraceToken, Accessors, CloseBraceToken);
}

/// <summary>
/// <c>get</c> or <c>set(value)</c>, with an optional visibility and an optional body. <c>get</c> and <c>set</c>
/// are identifier tokens: they are keywords only here.
/// </summary>
public sealed class AccessorDeclarationSyntax : SyntaxNode
{
    internal AccessorDeclarationSyntax(
        SyntaxKind kind,
        SyntaxList<SyntaxToken> modifiers,
        SyntaxToken keyword,
        SetterParameterSyntax? parameter,
        BlockSyntax? body)
        : base(kind)
    {
        Modifiers = modifiers;
        Keyword = keyword;
        Parameter = parameter;
        Body = body;
        AdoptChildren();
    }

    public SyntaxList<SyntaxToken> Modifiers { get; }

    public SyntaxToken Keyword { get; }

    public SetterParameterSyntax? Parameter { get; }

    public BlockSyntax? Body { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Modifiers, Keyword, Parameter, Body);
}

/// <summary>The <c>(value)</c> of <c>set(value)</c>.</summary>
public sealed class SetterParameterSyntax : SyntaxNode
{
    internal SetterParameterSyntax(SyntaxToken openParenToken, SyntaxToken identifier, SyntaxToken closeParenToken)
        : base(SyntaxKind.SetterParameter)
    {
        OpenParenToken = openParenToken;
        Identifier = identifier;
        CloseParenToken = closeParenToken;
        AdoptChildren();
    }

    public SyntaxToken OpenParenToken { get; }

    public SyntaxToken Identifier { get; }

    public SyntaxToken CloseParenToken { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(OpenParenToken, Identifier, CloseParenToken);
}

/// <summary><c>event Died(slime: Slime)</c> (ADR-0017).</summary>
public sealed class EventDeclarationSyntax : MemberDeclarationSyntax
{
    internal EventDeclarationSyntax(
        SyntaxList<SyntaxToken> modifiers,
        SyntaxToken eventKeyword,
        SyntaxToken identifier,
        ParameterListSyntax parameterList)
        : base(SyntaxKind.EventDeclaration, modifiers)
    {
        EventKeyword = eventKeyword;
        Identifier = identifier;
        ParameterList = parameterList;
        AdoptChildren();
    }

    public SyntaxToken EventKeyword { get; }

    public SyntaxToken Identifier { get; }

    public ParameterListSyntax ParameterList { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Modifiers, EventKeyword, Identifier, ParameterList);
}

/// <summary><c>: Behaviour, IDamageable</c>.</summary>
public sealed class BaseListSyntax : SyntaxNode
{
    internal BaseListSyntax(SyntaxToken colonToken, SeparatedSyntaxList<TypeSyntax> types)
        : base(SyntaxKind.BaseList)
    {
        ColonToken = colonToken;
        Types = types;
        AdoptChildren();
    }

    public SyntaxToken ColonToken { get; }

    public SeparatedSyntaxList<TypeSyntax> Types { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(ColonToken, Types);
}

public sealed class TypeParameterListSyntax : SyntaxNode
{
    internal TypeParameterListSyntax(SyntaxToken lessThanToken, SeparatedSyntaxList<TypeParameterSyntax> parameters, SyntaxToken greaterThanToken)
        : base(SyntaxKind.TypeParameterList)
    {
        LessThanToken = lessThanToken;
        Parameters = parameters;
        GreaterThanToken = greaterThanToken;
        AdoptChildren();
    }

    public SyntaxToken LessThanToken { get; }

    public SeparatedSyntaxList<TypeParameterSyntax> Parameters { get; }

    public SyntaxToken GreaterThanToken { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(LessThanToken, Parameters, GreaterThanToken);
}

/// <summary><c>T</c> or <c>T: class and IPoolable</c> (ADR-0012).</summary>
public sealed class TypeParameterSyntax : SyntaxNode
{
    internal TypeParameterSyntax(SyntaxToken identifier, TypeParameterConstraintClauseSyntax? constraintClause)
        : base(SyntaxKind.TypeParameter)
    {
        Identifier = identifier;
        ConstraintClause = constraintClause;
        AdoptChildren();
    }

    public SyntaxToken Identifier { get; }

    public TypeParameterConstraintClauseSyntax? ConstraintClause { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Identifier, ConstraintClause);
}

/// <summary>The <c>: A and B</c> after a type parameter; the separators are <c>and</c> tokens.</summary>
public sealed class TypeParameterConstraintClauseSyntax : SyntaxNode
{
    internal TypeParameterConstraintClauseSyntax(SyntaxToken colonToken, SeparatedSyntaxList<TypeParameterConstraintSyntax> constraints)
        : base(SyntaxKind.TypeParameterConstraintClause)
    {
        ColonToken = colonToken;
        Constraints = constraints;
        AdoptChildren();
    }

    public SyntaxToken ColonToken { get; }

    public SeparatedSyntaxList<TypeParameterConstraintSyntax> Constraints { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(ColonToken, Constraints);
}

public abstract class TypeParameterConstraintSyntax : SyntaxNode
{
    private protected TypeParameterConstraintSyntax(SyntaxKind kind)
        : base(kind)
    {
    }
}

/// <summary>A type as a constraint. <c>unmanaged</c> is parsed as a type name and recognized during binding.</summary>
public sealed class TypeConstraintSyntax : TypeParameterConstraintSyntax
{
    internal TypeConstraintSyntax(TypeSyntax type)
        : base(SyntaxKind.TypeConstraint)
    {
        Type = type;
        AdoptChildren();
    }

    public TypeSyntax Type { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Type);
}

/// <summary>The <c>class</c> or <c>struct</c> constraint.</summary>
public sealed class ClassOrStructConstraintSyntax : TypeParameterConstraintSyntax
{
    internal ClassOrStructConstraintSyntax(SyntaxKind kind, SyntaxToken keyword)
        : base(kind)
    {
        Keyword = keyword;
        AdoptChildren();
    }

    public SyntaxToken Keyword { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Keyword);
}

/// <summary>The <c>new()</c> constraint.</summary>
public sealed class ConstructorConstraintSyntax : TypeParameterConstraintSyntax
{
    internal ConstructorConstraintSyntax(SyntaxToken newKeyword, SyntaxToken openParenToken, SyntaxToken closeParenToken)
        : base(SyntaxKind.ConstructorConstraint)
    {
        NewKeyword = newKeyword;
        OpenParenToken = openParenToken;
        CloseParenToken = closeParenToken;
        AdoptChildren();
    }

    public SyntaxToken NewKeyword { get; }

    public SyntaxToken OpenParenToken { get; }

    public SyntaxToken CloseParenToken { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(NewKeyword, OpenParenToken, CloseParenToken);
}

public sealed class ParameterListSyntax : SyntaxNode
{
    internal ParameterListSyntax(SyntaxToken openParenToken, SeparatedSyntaxList<BaseParameterSyntax> parameters, SyntaxToken closeParenToken)
        : base(SyntaxKind.ParameterList)
    {
        OpenParenToken = openParenToken;
        Parameters = parameters;
        CloseParenToken = closeParenToken;
        AdoptChildren();
    }

    public SyntaxToken OpenParenToken { get; }

    public SeparatedSyntaxList<BaseParameterSyntax> Parameters { get; }

    public SyntaxToken CloseParenToken { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(OpenParenToken, Parameters, CloseParenToken);
}

public abstract class BaseParameterSyntax : SyntaxNode
{
    private protected BaseParameterSyntax(SyntaxKind kind)
        : base(kind)
    {
    }
}

/// <summary>
/// <c>name: Type = default</c>; <c>name: out Type</c> for the ref kinds of ADR-0015. The type is optional only for
/// lambda parameters; the parser reports it missing elsewhere.
/// </summary>
public sealed class ParameterSyntax : BaseParameterSyntax
{
    internal ParameterSyntax(
        SyntaxToken identifier,
        SyntaxToken? colonToken,
        SyntaxToken? refKindKeyword,
        TypeSyntax? type,
        EqualsValueClauseSyntax? @default)
        : base(SyntaxKind.Parameter)
    {
        Identifier = identifier;
        ColonToken = colonToken;
        RefKindKeyword = refKindKeyword;
        Type = type;
        Default = @default;
        AdoptChildren();
    }

    public SyntaxToken Identifier { get; }

    public SyntaxToken? ColonToken { get; }

    /// <summary><c>out</c>, <c>ref</c> or <c>in</c> before the type.</summary>
    public SyntaxToken? RefKindKeyword { get; }

    public TypeSyntax? Type { get; }

    public EqualsValueClauseSyntax? Default { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(Identifier, ColonToken, RefKindKeyword, Type, Default);
}

/// <summary><c>self</c> or <c>var self</c> as the first parameter: an instance method (ADR-0008).</summary>
public sealed class SelfParameterSyntax : BaseParameterSyntax
{
    internal SelfParameterSyntax(SyntaxToken? varKeyword, SyntaxToken selfKeyword)
        : base(SyntaxKind.SelfParameter)
    {
        VarKeyword = varKeyword;
        SelfKeyword = selfKeyword;
        AdoptChildren();
    }

    public SyntaxToken? VarKeyword { get; }

    public SyntaxToken SelfKeyword { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(VarKeyword, SelfKeyword);
}

/// <summary><c>: Type</c> after a name.</summary>
public sealed class TypeAnnotationSyntax : SyntaxNode
{
    internal TypeAnnotationSyntax(SyntaxToken colonToken, TypeSyntax type)
        : base(SyntaxKind.TypeAnnotation)
    {
        ColonToken = colonToken;
        Type = type;
        AdoptChildren();
    }

    public SyntaxToken ColonToken { get; }

    public TypeSyntax Type { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(ColonToken, Type);
}

/// <summary><c>= value</c> of a declaration or a default parameter value.</summary>
public sealed class EqualsValueClauseSyntax : SyntaxNode
{
    internal EqualsValueClauseSyntax(SyntaxToken equalsToken, ExpressionSyntax value)
        : base(SyntaxKind.EqualsValueClause)
    {
        EqualsToken = equalsToken;
        Value = value;
        AdoptChildren();
    }

    public SyntaxToken EqualsToken { get; }

    public ExpressionSyntax Value { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(EqualsToken, Value);
}

/// <summary><c>-&gt; Type</c> of a function, lambda or function type.</summary>
public sealed class ReturnTypeClauseSyntax : SyntaxNode
{
    internal ReturnTypeClauseSyntax(SyntaxToken arrowToken, TypeSyntax type)
        : base(SyntaxKind.ReturnTypeClause)
    {
        ArrowToken = arrowToken;
        Type = type;
        AdoptChildren();
    }

    public SyntaxToken ArrowToken { get; }

    public TypeSyntax Type { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(ArrowToken, Type);
}

/// <summary><c>= expression</c> as the body of a function or lambda (ADR-0012).</summary>
public sealed class ExpressionBodySyntax : SyntaxNode
{
    internal ExpressionBodySyntax(SyntaxToken equalsToken, ExpressionSyntax expression)
        : base(SyntaxKind.ExpressionBody)
    {
        EqualsToken = equalsToken;
        Expression = expression;
        AdoptChildren();
    }

    public SyntaxToken EqualsToken { get; }

    public ExpressionSyntax Expression { get; }

    public override IEnumerable<SyntaxNode> GetChildren() => Children(EqualsToken, Expression);
}
