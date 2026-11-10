using System.Collections.Immutable;
using Nyxel.Compiler.Syntax;

namespace Nyxel.Compiler.Symbols;

/// <summary>
/// A type declared in Nyxel. The declaration pass creates it; the later passes fill in its base type and members
/// (docs/design/semantic-analysis.md, "分几遍"), after which it doesn't change.
/// </summary>
internal sealed class SourceNamedTypeSymbol(TypeDeclarationSyntax syntax, SyntaxTree syntaxTree, NamespaceSymbol containingNamespace) : NamedTypeSymbol
{
    private readonly List<Symbol> _members = [];

    public TypeDeclarationSyntax Syntax => syntax;

    public SyntaxTree SyntaxTree => syntaxTree;

    public override string Name => syntax.Identifier.Text;

    public override TypeKind TypeKind => syntax.Kind switch
    {
        SyntaxKind.StructDeclaration => TypeKind.Struct,
        SyntaxKind.InterfaceDeclaration => TypeKind.Interface,
        _ => TypeKind.Class,
    };

    /// <summary>Types are visible to the assembly unless they are <c>public</c> (ADR-0008).</summary>
    public override Accessibility Accessibility =>
        syntax.Modifiers.Any(m => m.Kind == SyntaxKind.PublicKeyword) ? Accessibility.Public : Accessibility.Internal;

    public override NamespaceSymbol ContainingNamespace => containingNamespace;

    public override ImmutableArray<TypeParameterSymbol> TypeParameters => [];

    public override NamedTypeSymbol? BaseType => DeclaredBaseType;

    internal NamedTypeSymbol? DeclaredBaseType { get; set; }

    public override ImmutableArray<Symbol> GetMembers() => [.. _members];

    internal void AddMember(Symbol member) => _members.Add(member);
}

/// <summary>A <c>func</c> or an <c>init</c>, or the <c>init</c> a class without one gets (ADR-0013).</summary>
internal sealed class SourceMethodSymbol : MethodSymbol
{
    public SourceMethodSymbol(SourceNamedTypeSymbol containingType, MemberDeclarationSyntax? syntax, string name, MethodKind methodKind, bool isStatic)
    {
        ContainingType = containingType;
        Syntax = syntax;
        Name = name;
        MethodKind = methodKind;
        IsStatic = isStatic;
    }

    /// <summary>The <see cref="FunctionDeclarationSyntax"/> or <see cref="InitDeclarationSyntax"/>; null for the implicit init.</summary>
    public MemberDeclarationSyntax? Syntax { get; }

    public override SourceNamedTypeSymbol ContainingType { get; }

    public override string Name { get; }

    public override MethodKind MethodKind { get; }

    public override bool IsStatic { get; }

    /// <summary>An <c>init</c> is public unless marked <c>private</c> or <c>protected</c> (ADR-0040); other members are private by default.</summary>
    public override Accessibility Accessibility => Syntax is null ? Accessibility.Public
        : MethodKind == MethodKind.Constructor ? InitAccessibility(Syntax.Modifiers)
        : MemberAccessibility(Syntax.Modifiers);

    public override bool IsAsync => Syntax?.Modifiers.Any(m => m.Kind == SyntaxKind.AsyncKeyword) == true;

    /// <summary>A <c>var self</c> method of a struct, which changes the value it is called on (ADR-0008).</summary>
    public bool HasVarSelf { get; init; }

    public override TypeSymbol ReturnType => DeclaredReturnType;

    internal TypeSymbol DeclaredReturnType { get; set; } = ErrorTypeSymbol.Instance;

    public override ImmutableArray<ParameterSymbol> Parameters => DeclaredParameters;

    internal ImmutableArray<ParameterSymbol> DeclaredParameters { get; set; } = [];

    /// <summary>Where diagnostics about the whole function point: its name, or <c>init</c>.</summary>
    public SyntaxNode NameLocation => Syntax switch
    {
        FunctionDeclarationSyntax function => function.Identifier,
        InitDeclarationSyntax init => (SyntaxNode?)init.Identifier ?? init.InitKeyword,
        _ => ContainingType.Syntax.Identifier,
    };

    internal static Accessibility InitAccessibility(SyntaxList<SyntaxToken> modifiers) =>
        modifiers.Any(m => m.Kind == SyntaxKind.PrivateKeyword) ? Accessibility.Private
        : modifiers.Any(m => m.Kind == SyntaxKind.ProtectedKeyword) ? Accessibility.Protected
        : Accessibility.Public;

    internal static Accessibility MemberAccessibility(SyntaxList<SyntaxToken> modifiers) =>
        modifiers.Any(m => m.Kind == SyntaxKind.PublicKeyword) ? Accessibility.Public
        : modifiers.Any(m => m.Kind == SyntaxKind.ProtectedKeyword) ? Accessibility.Protected
        : Accessibility.Private;
}

internal sealed class SourceParameterSymbol(Symbol containingSymbol, ParameterSyntax syntax, TypeSymbol type, int ordinal) : ParameterSymbol
{
    public ParameterSyntax Syntax => syntax;

    public override string Name => syntax.Identifier.Text;

    public override Symbol ContainingSymbol => containingSymbol;

    public override TypeSymbol Type => type;

    public override int Ordinal => ordinal;
}

/// <summary>A <c>let</c> or <c>var</c> field.</summary>
internal sealed class SourceFieldSymbol(SourceNamedTypeSymbol containingType, FieldDeclarationSyntax syntax, TypeSymbol type) : FieldSymbol
{
    public FieldDeclarationSyntax Syntax => syntax;

    public override string Name => syntax.Identifier.Text;

    public override NamedTypeSymbol ContainingType => containingType;

    public override Accessibility Accessibility => SourceMethodSymbol.MemberAccessibility(syntax.Modifiers);

    public override TypeSymbol Type => type;

    public override bool IsStatic => syntax.Modifiers.Any(m => m.Kind == SyntaxKind.StaticKeyword);

    public override bool IsReadOnly => syntax.Keyword.Kind == SyntaxKind.LetKeyword;
}
