using System.Collections.Immutable;
using Nyxel.Compiler.Syntax;
using SpecialType = Microsoft.CodeAnalysis.SpecialType;

namespace Nyxel.Compiler.Symbols;

/// <summary>
/// A type declared in Nyxel. The declaration pass creates it; the later passes fill in its base type and members
/// (docs/design/semantic-analysis.md, "分几遍"), after which it doesn't change.
/// </summary>
internal sealed class SourceNamedTypeSymbol : NamedTypeSymbol
{
    private readonly List<Symbol> _members = [];
    private readonly NamespaceSymbol _containingNamespace;

    public SourceNamedTypeSymbol(MemberDeclarationSyntax syntax, SyntaxTree syntaxTree, NamespaceSymbol containingNamespace)
    {
        if (syntax is not (TypeDeclarationSyntax or EnumDeclarationSyntax))
        {
            throw new ArgumentException("A type is declared by a type or enum declaration.", nameof(syntax));
        }
        Syntax = syntax;
        SyntaxTree = syntaxTree;
        _containingNamespace = containingNamespace;
    }

    /// <summary>The <see cref="TypeDeclarationSyntax"/> or <see cref="EnumDeclarationSyntax"/>.</summary>
    public MemberDeclarationSyntax Syntax { get; }

    public SyntaxTree SyntaxTree { get; }

    public SyntaxToken Identifier => Syntax is EnumDeclarationSyntax enumeration ? enumeration.Identifier : ((TypeDeclarationSyntax)Syntax).Identifier;

    /// <summary>The declared members; for an enum, those after its cases.</summary>
    public SyntaxList<MemberDeclarationSyntax> MemberSyntaxes =>
        Syntax is EnumDeclarationSyntax enumeration ? enumeration.Members : ((TypeDeclarationSyntax)Syntax).Members;

    public BaseListSyntax? BaseList => Syntax is EnumDeclarationSyntax enumeration ? enumeration.BaseList : ((TypeDeclarationSyntax)Syntax).BaseList;

    public override string Name => Identifier.Text;

    /// <summary>
    /// An enum without data is a C# enum; a <c>struct enum</c> is a struct and a <c>class enum</c> a class, as they are
    /// in C# (ADR-0046), with <see cref="IsDataEnum"/> set.
    /// </summary>
    public override TypeKind TypeKind => Syntax switch
    {
        EnumDeclarationSyntax { IsStruct: true } => TypeKind.Struct,
        EnumDeclarationSyntax { IsClass: true } => TypeKind.Class,
        EnumDeclarationSyntax => TypeKind.Enum,
        { Kind: SyntaxKind.StructDeclaration } => TypeKind.Struct,
        { Kind: SyntaxKind.InterfaceDeclaration } => TypeKind.Interface,
        _ => TypeKind.Class,
    };

    /// <summary>Declared with <c>enum</c>, of any of the four kinds (plain, flags, <c>struct enum</c>, <c>class enum</c>).</summary>
    public bool IsDeclaredEnum => Syntax is EnumDeclarationSyntax;

    /// <summary>A <c>struct enum</c> or <c>class enum</c> (ADR-0024, ADR-0039): its cases can carry data.</summary>
    public bool IsDataEnum => Syntax is EnumDeclarationSyntax { IsStruct: true } or EnumDeclarationSyntax { IsClass: true };

    /// <summary>A <c>flags enum</c> (ADR-0024): its values combine with <c>|</c>, <c>&amp;</c>, <c>^</c> and <c>~</c>.</summary>
    public bool IsFlags => Syntax is EnumDeclarationSyntax { IsFlags: true };

    /// <summary>The cases of an enum, in declaration order.</summary>
    public IEnumerable<SourceEnumCaseSymbol> Cases => _members.OfType<SourceEnumCaseSymbol>();

    public override NamedTypeSymbol? EnumUnderlyingType => DeclaredEnumUnderlyingType;

    internal NamedTypeSymbol? DeclaredEnumUnderlyingType { get; set; }

    /// <summary>Types are visible to the assembly unless they are <c>public</c> (ADR-0008).</summary>
    public override Accessibility Accessibility =>
        Syntax.Modifiers.Any(m => m.Kind == SyntaxKind.PublicKeyword) ? Accessibility.Public : Accessibility.Internal;

    public override NamespaceSymbol ContainingNamespace => _containingNamespace;

    public override ImmutableArray<TypeParameterSymbol> TypeParameters => [];

    public override NamedTypeSymbol? BaseType => DeclaredBaseType;

    internal NamedTypeSymbol? DeclaredBaseType { get; set; }

    public override ImmutableArray<Symbol> GetMembers() => [.. _members];

    internal void AddMember(Symbol member) => _members.Add(member);
}

/// <summary>
/// A <c>func</c>, an <c>init</c>, the <c>get</c> or <c>set</c> of a property, or the <c>init</c> a class without one
/// gets (ADR-0013).
/// </summary>
internal sealed class SourceMethodSymbol : MethodSymbol
{
    public SourceMethodSymbol(SourceNamedTypeSymbol containingType, SyntaxNode? syntax, string name, MethodKind methodKind, bool isStatic)
    {
        ContainingType = containingType;
        Syntax = syntax;
        Name = name;
        MethodKind = methodKind;
        IsStatic = isStatic;
    }

    /// <summary>
    /// The <see cref="FunctionDeclarationSyntax"/>, <see cref="InitDeclarationSyntax"/> or <see cref="AccessorDeclarationSyntax"/>;
    /// null for the implicit init.
    /// </summary>
    public SyntaxNode? Syntax { get; }

    public override SourceNamedTypeSymbol ContainingType { get; }

    public override string Name { get; }

    public override MethodKind MethodKind { get; }

    public override bool IsStatic { get; }

    /// <summary>For a <c>get</c> or <c>set</c>, its property.</summary>
    public SourcePropertySymbol? Property { get; init; }

    /// <summary>
    /// An <c>init</c> is public unless marked <c>private</c> or <c>protected</c> (ADR-0040); other members are private
    /// by default. An accessor has its property's visibility unless it narrows it (<c>private set</c>).
    /// </summary>
    public override Accessibility Accessibility => Syntax switch
    {
        null => Accessibility.Public,
        InitDeclarationSyntax init => InitAccessibility(init.Modifiers),
        MemberDeclarationSyntax member => MemberAccessibility(member.Modifiers),
        AccessorDeclarationSyntax accessor when Property is { } property =>
            AccessorAccessibility(accessor.Modifiers) ?? property.Accessibility,
        _ => Accessibility.Private,
    };

    public override bool IsAsync => Syntax is FunctionDeclarationSyntax function && function.Modifiers.Any(m => m.Kind == SyntaxKind.AsyncKeyword);

    /// <summary>A <c>var self</c> method of a struct, which changes the value it is called on (ADR-0008).</summary>
    public bool HasVarSelf { get; init; }

    /// <summary>
    /// A method of a struct that can't change <c>self</c>: a <c>self</c> method and a <c>get</c> (ADR-0008, ADR-0044).
    /// A <c>set</c> and a <c>var self</c> method can, and so can an <c>init</c>, which builds the value.
    /// </summary>
    public override bool IsReadOnly => ContainingType.TypeKind == TypeKind.Struct && !IsStatic
        && MethodKind is MethodKind.Ordinary or MethodKind.PropertyGet && !HasVarSelf;

    public override TypeSymbol ReturnType => DeclaredReturnType;

    internal TypeSymbol DeclaredReturnType { get; set; } = ErrorTypeSymbol.Instance;

    public override ImmutableArray<ParameterSymbol> Parameters => DeclaredParameters;

    internal ImmutableArray<ParameterSymbol> DeclaredParameters { get; set; } = [];

    /// <summary>Where diagnostics about the whole function point: its name, <c>init</c>, <c>get</c> or <c>set</c>.</summary>
    public SyntaxNode NameLocation => Syntax switch
    {
        FunctionDeclarationSyntax function => function.Identifier,
        InitDeclarationSyntax init => (SyntaxNode?)init.Identifier ?? init.InitKeyword,
        AccessorDeclarationSyntax accessor => accessor.Keyword,
        _ => ContainingType.Identifier,
    };

    internal static Accessibility InitAccessibility(SyntaxList<SyntaxToken> modifiers) =>
        modifiers.Any(m => m.Kind == SyntaxKind.PrivateKeyword) ? Accessibility.Private
        : modifiers.Any(m => m.Kind == SyntaxKind.ProtectedKeyword) ? Accessibility.Protected
        : Accessibility.Public;

    internal static Accessibility MemberAccessibility(SyntaxList<SyntaxToken> modifiers) =>
        modifiers.Any(m => m.Kind == SyntaxKind.PublicKeyword) ? Accessibility.Public
        : modifiers.Any(m => m.Kind == SyntaxKind.ProtectedKeyword) ? Accessibility.Protected
        : Accessibility.Private;

    /// <summary>The visibility written on an accessor (<c>private set</c>), or null when it has none.</summary>
    internal static Accessibility? AccessorAccessibility(SyntaxList<SyntaxToken> modifiers) =>
        modifiers.Any(m => m.Kind == SyntaxKind.PrivateKeyword) ? Accessibility.Private
        : modifiers.Any(m => m.Kind == SyntaxKind.ProtectedKeyword) ? Accessibility.Protected
        : modifiers.Any(m => m.Kind == SyntaxKind.PublicKeyword) ? Accessibility.Public
        : null;
}

/// <summary>A parameter of a function or <c>init</c>, or the new value of a <c>set(value)</c>.</summary>
internal sealed class SourceParameterSymbol(Symbol containingSymbol, SyntaxNode syntax, SyntaxToken identifier, TypeSymbol type, int ordinal) : ParameterSymbol
{
    public SourceParameterSymbol(Symbol containingSymbol, ParameterSyntax syntax, TypeSymbol type, int ordinal)
        : this(containingSymbol, syntax, syntax.Identifier, type, ordinal)
    {
    }

    /// <summary>The <see cref="ParameterSyntax"/>, or the <see cref="SetterParameterSyntax"/> of a <c>set</c>.</summary>
    public SyntaxNode Syntax => syntax;

    public SyntaxToken Identifier => identifier;

    public override string Name => identifier.Text;

    public override Symbol ContainingSymbol => containingSymbol;

    public override TypeSymbol Type => type;

    public override int Ordinal => ordinal;

    /// <summary><c>count: int = 1</c>: the value is bound with the function bodies (it must be a constant, as in C#).</summary>
    public override bool HasDefaultValue => syntax is ParameterSyntax { Default: not null };
}

/// <summary>
/// A <c>property</c> (ADR-0011): computed when its accessors have bodies, an auto property when they don't. An auto
/// property's value works like a field's (ADR-0044): with only <c>get</c> it is set like a <c>let</c> field.
/// </summary>
internal sealed class SourcePropertySymbol(SourceNamedTypeSymbol containingType, PropertyDeclarationSyntax syntax, TypeSymbol type) : PropertySymbol
{
    public PropertyDeclarationSyntax Syntax => syntax;

    public override string Name => syntax.Identifier.Text;

    public override NamedTypeSymbol ContainingType => containingType;

    public override Accessibility Accessibility => SourceMethodSymbol.MemberAccessibility(syntax.Modifiers);

    public override TypeSymbol Type => type;

    public override bool IsStatic => syntax.Modifiers.Any(m => m.Kind == SyntaxKind.StaticKeyword);

    public SourceMethodSymbol? Getter { get; internal set; }

    public SourceMethodSymbol? Setter { get; internal set; }

    public override bool HasGetter => Getter is not null;

    public override bool HasSetter => Setter is not null;

    public override Accessibility GetterAccessibility => Getter?.Accessibility ?? Accessibility;

    public override Accessibility SetterAccessibility => Setter?.Accessibility ?? Accessibility;

    /// <summary>Accessors without bodies: the compiler stores the value.</summary>
    public bool IsAuto => syntax.AccessorList.Accessors.All(a => a.Body is null && a.ExpressionBody is null);

    /// <summary>An auto property with only <c>get</c>: set where it is declared and in an <c>init</c>, as a <c>let</c> field (ADR-0044).</summary>
    public bool IsReadOnly => IsAuto && Setter is null;
}

/// <summary>
/// A <c>let</c> or <c>var</c> field. A <c>static let</c> whose initial value is a constant expression, of a type C#
/// can make a <c>const</c>, is a compile-time constant (ADR-0038); finding that out binds the initial value.
/// </summary>
internal sealed class SourceFieldSymbol(SourceNamedTypeSymbol containingType, FieldDeclarationSyntax syntax, TypeSymbol type, IConstantValues constants) : FieldSymbol
{
    public FieldDeclarationSyntax Syntax => syntax;

    public override string Name => syntax.Identifier.Text;

    public override NamedTypeSymbol ContainingType => containingType;

    public override Accessibility Accessibility => SourceMethodSymbol.MemberAccessibility(syntax.Modifiers);

    public override TypeSymbol Type => type;

    public override bool IsStatic => syntax.Modifiers.Any(m => m.Kind == SyntaxKind.StaticKeyword);

    public override bool IsReadOnly => syntax.Keyword.Kind == SyntaxKind.LetKeyword;

    /// <summary>A <c>static let</c> that could be a constant, whatever its initial value: it has one, of a type C# can make a <c>const</c>.</summary>
    public bool CouldBeConst => IsStatic && IsReadOnly && syntax.Initializer is not null && CanBeConst(type);

    public override bool IsConst => CouldBeConst && constants.ValueOf(this) is not null;

    public override object? ConstantValue => CouldBeConst ? constants.ValueOf(this) : null;

    /// <summary>The types of C#'s <c>const</c>: numbers, <c>bool</c>, <c>char</c>, <c>string</c> and enums (ADR-0038).</summary>
    public static bool CanBeConst(TypeSymbol type) => type.TypeKind == TypeKind.Enum || type.SpecialType is
        SpecialType.System_Boolean or SpecialType.System_Char
        or SpecialType.System_String or SpecialType.System_Decimal
        or SpecialType.System_SByte or SpecialType.System_Byte
        or SpecialType.System_Int16 or SpecialType.System_UInt16
        or SpecialType.System_Int32 or SpecialType.System_UInt32
        or SpecialType.System_Int64 or SpecialType.System_UInt64
        or SpecialType.System_Single or SpecialType.System_Double;
}

/// <summary>
/// A case of an enum. In an enum without data it is a constant of the enum, as the members of a C# enum are
/// (ADR-0024): its value is the one written, or its position among the cases when none is written; working it out
/// binds the written value. In a <c>struct enum</c> or <c>class enum</c> it isn't a constant: a case without data is a
/// value (<c>AiState.Idle</c>), one with data is created with <c>new</c> and its <see cref="Items"/>.
/// </summary>
internal sealed class SourceEnumCaseSymbol(SourceNamedTypeSymbol containingType, EnumCaseDeclarationSyntax syntax, int ordinal, IConstantValues constants) : FieldSymbol
{
    public EnumCaseDeclarationSyntax Syntax => syntax;

    /// <summary>The case's position among the cases, from 0: its value when the enum writes none, its tag in a <c>struct enum</c>.</summary>
    public int Ordinal => ordinal;

    /// <summary><c>case Burn(Amount: int, Seconds: float)</c>: created with <c>new</c>, its data given like a tuple's (ADR-0024).</summary>
    public bool HasData => syntax.ParameterList is not null;

    /// <summary>The data of the case, named as in a tuple type: <c>Amount</c>, <c>Seconds</c>.</summary>
    public ImmutableArray<ParameterSymbol> Items => DeclaredItems;

    internal ImmutableArray<ParameterSymbol> DeclaredItems { get; set; } = [];

    public override string Name => syntax.Identifier.Text;

    public override SourceNamedTypeSymbol ContainingType => containingType;

    public override Accessibility Accessibility => Accessibility.Public;

    public override TypeSymbol Type => containingType;

    public override bool IsStatic => true;

    public override bool IsReadOnly => true;

    public override bool IsConst => containingType.TypeKind == TypeKind.Enum;

    /// <summary>The value as the enum's underlying type (<c>byte</c> for <c>enum Tile : byte</c>); null after an error and for a data enum.</summary>
    public override object? ConstantValue => IsConst ? constants.ValueOf(this) : null;
}

/// <summary>
/// Works out the values of constants when they are first asked for (ADR-0038): knowing them takes binding the
/// expressions that give them, which can name other constants.
/// </summary>
internal interface IConstantValues
{
    /// <summary>The value of a <c>static let</c>'s initial value when it is a constant expression; null otherwise.</summary>
    object? ValueOf(SourceFieldSymbol field);

    object? ValueOf(SourceEnumCaseSymbol enumCase);
}
