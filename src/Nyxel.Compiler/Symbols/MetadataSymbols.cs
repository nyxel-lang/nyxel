using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp;
using CodeAnalysis = Microsoft.CodeAnalysis;
using SpecialType = Microsoft.CodeAnalysis.SpecialType;

namespace Nyxel.Compiler.Symbols;

/// <summary>
/// The symbols of one compilation that don't come from Nyxel source: the referenced assemblies, read through a
/// Roslyn compilation that has the references and no source (docs/design/semantic-analysis.md), plus the array types.
/// Every Roslyn symbol is wrapped once; generic types are wrapped as definitions and constructed here.
/// </summary>
internal sealed class SymbolTable
{
    private readonly Dictionary<CodeAnalysis.ISymbol, Symbol?> _metadata = new(CodeAnalysis.SymbolEqualityComparer.Default);
    private readonly Dictionary<(TypeSymbol Element, int Rank), ArrayTypeSymbol> _arrays = [];
    private readonly Dictionary<CodeAnalysis.INamespaceSymbol, NamespaceSymbol> _namespaces = new(CodeAnalysis.SymbolEqualityComparer.Default);

    public SymbolTable(CSharpCompilation references)
    {
        References = references;
        GlobalNamespace = new NamespaceSymbol(this, null, "", references.GlobalNamespace);
    }

    /// <summary>The Roslyn compilation with only the references; the back end adds the lowered C# to it.</summary>
    public CSharpCompilation References { get; }

    public NamespaceSymbol GlobalNamespace { get; }

    public NamedTypeSymbol GetSpecialType(SpecialType type) => (NamedTypeSymbol)GetType(References.GetSpecialType(type));

    /// <summary>A type by its metadata name, such as <c>System.Collections.Generic.List`1</c>.</summary>
    public NamedTypeSymbol? GetWellKnownType(string metadataName) =>
        References.GetTypeByMetadataName(metadataName) is { } type ? GetType(type) as NamedTypeSymbol : null;

    public ArrayTypeSymbol GetArrayType(TypeSymbol elementType, int rank)
    {
        if (!_arrays.TryGetValue((elementType, rank), out var array))
        {
            array = new ArrayTypeSymbol(this, elementType, rank);
            _arrays.Add((elementType, rank), array);
        }
        return array;
    }

    /// <summary>
    /// The Nyxel symbol for a Roslyn type. Types Nyxel can't use (pointers, function pointers, <c>dynamic</c>) and
    /// types missing from the references are the error type; members that use them are left out.
    /// </summary>
    public TypeSymbol GetType(CodeAnalysis.ITypeSymbol type)
    {
        switch (type)
        {
            case CodeAnalysis.IArrayTypeSymbol array:
                var element = GetType(array.ElementType);
                return element.IsError ? element : GetArrayType(element, array.Rank);
            case CodeAnalysis.ITypeParameterSymbol parameter:
                return (TypeSymbol)Wrap(parameter, () => new MetadataTypeParameterSymbol(parameter))!;
            case CodeAnalysis.INamedTypeSymbol named when named.TypeKind != CodeAnalysis.TypeKind.Error:
                var definition = named.OriginalDefinition;
                var wrapped = (NamedTypeSymbol)Wrap(definition, () => new MetadataNamedTypeSymbol(this, definition))!;
                if (CodeAnalysis.SymbolEqualityComparer.Default.Equals(named, definition))
                {
                    return wrapped;
                }
                var arguments = AllTypeArguments(named).Select(GetType).ToImmutableArray();
                return arguments.Any(a => a.IsError) ? ErrorTypeSymbol.Instance : wrapped.ConstructAll(arguments);
            default:
                return ErrorTypeSymbol.Instance;
        }

        static IEnumerable<CodeAnalysis.ITypeSymbol> AllTypeArguments(CodeAnalysis.INamedTypeSymbol type) =>
            (type.ContainingType is { } outer ? AllTypeArguments(outer) : []).Concat(type.TypeArguments);
    }

    public NamespaceSymbol GetNamespace(CodeAnalysis.INamespaceSymbol ns)
    {
        if (ns.IsGlobalNamespace)
        {
            return GlobalNamespace;
        }
        if (!_namespaces.TryGetValue(ns, out var result))
        {
            result = GetNamespace(ns.ContainingNamespace).GetNamespace(ns.Name)!;
            _namespaces.Add(ns, result);
        }
        return result;
    }

    /// <summary>A member Nyxel code can use, or null: accessors, events and members with unusable types are left out.</summary>
    public Symbol? GetMember(CodeAnalysis.ISymbol member)
    {
        if (member.DeclaredAccessibility is not (CodeAnalysis.Accessibility.Public or CodeAnalysis.Accessibility.Protected or CodeAnalysis.Accessibility.ProtectedOrInternal)
            || member.IsImplicitlyDeclared && member is not CodeAnalysis.IMethodSymbol { MethodKind: CodeAnalysis.MethodKind.Constructor })
        {
            return null;
        }
        return member switch
        {
            CodeAnalysis.IMethodSymbol { MethodKind: CodeAnalysis.MethodKind.Ordinary or CodeAnalysis.MethodKind.Constructor or CodeAnalysis.MethodKind.UserDefinedOperator or CodeAnalysis.MethodKind.Conversion } method =>
                Wrap(method, () => Usable(new MetadataMethodSymbol(this, method))),
            CodeAnalysis.IFieldSymbol field => Wrap(field, () => Usable(new MetadataFieldSymbol(this, field))),
            CodeAnalysis.IPropertySymbol property => Wrap(property, () => Usable(new MetadataPropertySymbol(this, property))),
            CodeAnalysis.INamedTypeSymbol type => GetType(type) as NamedTypeSymbol,
            _ => null,
        };
    }

    private Symbol? Wrap(CodeAnalysis.ISymbol symbol, Func<Symbol?> create)
    {
        if (!_metadata.TryGetValue(symbol, out var wrapped))
        {
            wrapped = create();
            _metadata.Add(symbol, wrapped);
        }
        return wrapped;
    }

    private static Symbol? Usable(Symbol member)
    {
        var types = member switch
        {
            MethodSymbol method => method.Parameters.Select(p => p.Type).Append(method.ReturnType),
            FieldSymbol field => [field.Type],
            PropertySymbol property => property.Parameters.Select(p => p.Type).Append(property.Type),
            _ => [],
        };
        return types.Any(t => t.IsError) ? null : member;
    }

    internal static Accessibility ToAccessibility(CodeAnalysis.Accessibility accessibility) => accessibility switch
    {
        CodeAnalysis.Accessibility.Public => Accessibility.Public,
        CodeAnalysis.Accessibility.Protected or CodeAnalysis.Accessibility.ProtectedOrInternal => Accessibility.Protected,
        CodeAnalysis.Accessibility.Internal or CodeAnalysis.Accessibility.ProtectedAndInternal => Accessibility.Internal,
        _ => Accessibility.Private,
    };
}

internal sealed class MetadataNamedTypeSymbol(SymbolTable table, CodeAnalysis.INamedTypeSymbol type) : NamedTypeSymbol
{
    private readonly Dictionary<string, ImmutableArray<Symbol>> _membersByName = [];
    private ImmutableArray<Symbol> _members;
    private ImmutableArray<TypeParameterSymbol> _typeParameters;

    public CodeAnalysis.INamedTypeSymbol RoslynSymbol => type;

    public override string Name => type.Name;

    public override TypeKind TypeKind => type.TypeKind switch
    {
        CodeAnalysis.TypeKind.Struct => TypeKind.Struct,
        CodeAnalysis.TypeKind.Interface => TypeKind.Interface,
        CodeAnalysis.TypeKind.Enum => TypeKind.Enum,
        CodeAnalysis.TypeKind.Delegate => TypeKind.Delegate,
        _ => TypeKind.Class,
    };

    public override SpecialType SpecialType => type.SpecialType;

    public override Accessibility Accessibility => SymbolTable.ToAccessibility(type.DeclaredAccessibility);

    public override NamespaceSymbol? ContainingNamespace => type.ContainingNamespace is { } ns ? table.GetNamespace(ns) : null;

    public override NamedTypeSymbol? ContainingType => type.ContainingType is { } outer ? table.GetType(outer) as NamedTypeSymbol : null;

    public override ImmutableArray<TypeParameterSymbol> TypeParameters
    {
        get
        {
            if (_typeParameters.IsDefault)
            {
                _typeParameters = [.. type.TypeParameters.Select(p => (TypeParameterSymbol)table.GetType(p))];
            }
            return _typeParameters;
        }
    }

    public override bool IsStatic => type.IsStatic;

    public override bool IsAbstract => type.IsAbstract;

    public override bool IsSealed => type.IsSealed;

    public override NamedTypeSymbol? BaseType => type.BaseType is { } baseType ? table.GetType(baseType) as NamedTypeSymbol : null;

    public override ImmutableArray<NamedTypeSymbol> Interfaces => [.. type.Interfaces.Select(table.GetType).OfType<NamedTypeSymbol>()];

    public override ImmutableArray<Symbol> GetMembers()
    {
        if (_members.IsDefault)
        {
            _members = [.. type.GetMembers().Select(table.GetMember).OfType<Symbol>()];
        }
        return _members;
    }

    public override ImmutableArray<Symbol> GetMembers(string name)
    {
        if (!_membersByName.TryGetValue(name, out var members))
        {
            members = [.. type.GetMembers(name).Select(table.GetMember).OfType<Symbol>()];
            _membersByName.Add(name, members);
        }
        return members;
    }
}

internal sealed class MetadataTypeParameterSymbol(CodeAnalysis.ITypeParameterSymbol parameter) : TypeParameterSymbol
{
    public override string Name => parameter.Name;

    public override int Ordinal => parameter.Ordinal;
}

internal sealed class MetadataMethodSymbol(SymbolTable table, CodeAnalysis.IMethodSymbol method) : MethodSymbol
{
    private ImmutableArray<ParameterSymbol> _parameters;

    public CodeAnalysis.IMethodSymbol RoslynSymbol => method;

    public override string Name => method.Name;

    public override MethodKind MethodKind => method.MethodKind switch
    {
        CodeAnalysis.MethodKind.Constructor => MethodKind.Constructor,
        CodeAnalysis.MethodKind.UserDefinedOperator => MethodKind.Operator,
        CodeAnalysis.MethodKind.Conversion => MethodKind.Conversion,
        _ => MethodKind.Ordinary,
    };

    public override Accessibility Accessibility => SymbolTable.ToAccessibility(method.DeclaredAccessibility);

    public override NamedTypeSymbol ContainingType => (NamedTypeSymbol)table.GetType(method.ContainingType);

    public override bool IsStatic => method.IsStatic;

    public override TypeSymbol ReturnType => table.GetType(method.ReturnType);

    public override ImmutableArray<ParameterSymbol> Parameters
    {
        get
        {
            if (_parameters.IsDefault)
            {
                _parameters = [.. method.Parameters.Select(p => new MetadataParameterSymbol(table, p, this))];
            }
            return _parameters;
        }
    }

    public override ImmutableArray<TypeParameterSymbol> TypeParameters => [.. method.TypeParameters.Select(p => (TypeParameterSymbol)table.GetType(p))];

    public override bool IsAbstract => method.IsAbstract;

    public override bool IsVirtual => method.IsVirtual;

    public override bool IsOverride => method.IsOverride;

    public override bool IsAsync => method.IsAsync;

    public override MethodSymbol? OverriddenMethod =>
        method.OverriddenMethod is { } overridden ? table.GetMember(overridden.OriginalDefinition) as MethodSymbol : null;
}

internal sealed class MetadataParameterSymbol(SymbolTable table, CodeAnalysis.IParameterSymbol parameter, Symbol containingSymbol) : ParameterSymbol
{
    public override string Name => parameter.Name;

    public override Symbol ContainingSymbol => containingSymbol;

    public override TypeSymbol Type => table.GetType(parameter.Type);

    public override int Ordinal => parameter.Ordinal;

    public override RefKind RefKind => parameter.RefKind switch
    {
        CodeAnalysis.RefKind.Ref => RefKind.Ref,
        CodeAnalysis.RefKind.Out => RefKind.Out,
        CodeAnalysis.RefKind.In or CodeAnalysis.RefKind.RefReadOnlyParameter => RefKind.In,
        _ => RefKind.None,
    };

    public override bool IsParams => parameter.IsParams;

    public override bool HasDefaultValue => parameter.HasExplicitDefaultValue;

    public override object? DefaultValue => parameter.HasExplicitDefaultValue ? parameter.ExplicitDefaultValue : null;
}

internal sealed class MetadataFieldSymbol(SymbolTable table, CodeAnalysis.IFieldSymbol source) : FieldSymbol
{
    public override string Name => source.Name;

    public override Accessibility Accessibility => SymbolTable.ToAccessibility(source.DeclaredAccessibility);

    public override NamedTypeSymbol ContainingType => (NamedTypeSymbol)table.GetType(source.ContainingType);

    public override TypeSymbol Type => table.GetType(source.Type);

    public override bool IsStatic => source.IsStatic;

    public override bool IsReadOnly => source.IsReadOnly || source.IsConst;

    public override bool IsConst => source.IsConst;

    public override object? ConstantValue => source.HasConstantValue ? source.ConstantValue : null;
}

internal sealed class MetadataPropertySymbol(SymbolTable table, CodeAnalysis.IPropertySymbol property) : PropertySymbol
{
    private ImmutableArray<ParameterSymbol> _parameters;

    public override string Name => property.Name;

    public override Accessibility Accessibility => SymbolTable.ToAccessibility(property.DeclaredAccessibility);

    public override NamedTypeSymbol ContainingType => (NamedTypeSymbol)table.GetType(property.ContainingType);

    public override TypeSymbol Type => table.GetType(property.Type);

    public override bool IsStatic => property.IsStatic;

    public override ImmutableArray<ParameterSymbol> Parameters
    {
        get
        {
            if (_parameters.IsDefault)
            {
                _parameters = [.. property.Parameters.Select(p => new MetadataParameterSymbol(table, p, this))];
            }
            return _parameters;
        }
    }

    public override bool HasGetter => property.GetMethod is { } getter && IsAccessible(getter);

    /// <summary>An <c>init</c> setter only works in <c>with</c> (ADR-0027), so it doesn't count here.</summary>
    public override bool HasSetter => property.SetMethod is { IsInitOnly: false } setter && IsAccessible(setter);

    private static bool IsAccessible(CodeAnalysis.IMethodSymbol accessor) =>
        accessor.DeclaredAccessibility is CodeAnalysis.Accessibility.Public or CodeAnalysis.Accessibility.Protected or CodeAnalysis.Accessibility.ProtectedOrInternal;
}
