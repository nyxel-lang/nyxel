using System.Collections.Immutable;
using System.Diagnostics;
using SpecialType = Microsoft.CodeAnalysis.SpecialType;

namespace Nyxel.Compiler.Symbols;

public enum TypeKind
{
    Class,
    Struct,
    Interface,
    Enum,
    Delegate,
    Array,
    TypeParameter,

    /// <summary><c>T?</c>: <see cref="NullableTypeSymbol"/>.</summary>
    Nullable,

    /// <summary>The type of the literal <c>null</c> before it converts to a <c>T?</c>.</summary>
    Null,
    Error,
}

/// <summary>
/// A type. Each type has exactly one symbol in a compilation, so types are compared by reference: <c>int</c> and
/// <c>System.Int32</c> are the same object, and <c>List&lt;int&gt;</c> is constructed once.
/// </summary>
public abstract class TypeSymbol : Symbol
{
    public abstract TypeKind TypeKind { get; }

    /// <summary>Which built-in type this is, if any: <c>int</c>, <c>string</c>, <c>object</c>, <c>void</c>...</summary>
    public virtual SpecialType SpecialType => SpecialType.None;

    public virtual NamedTypeSymbol? BaseType => null;

    public virtual ImmutableArray<NamedTypeSymbol> Interfaces => [];

    /// <summary>A struct or enum, or a <c>T?</c> of one (the .NET <c>Nullable&lt;T&gt;</c>).</summary>
    public bool IsValueType => TypeKind is TypeKind.Struct or TypeKind.Enum || this is NullableTypeSymbol { UnderlyingType.IsValueType: true };

    /// <summary>A class, interface, function type or array, or a <c>T?</c> of one.</summary>
    public bool IsReferenceType => TypeKind is TypeKind.Class or TypeKind.Interface or TypeKind.Delegate or TypeKind.Array
        || this is NullableTypeSymbol { UnderlyingType.IsReferenceType: true };

    /// <summary><c>T?</c>: a value of this type may be null (ADR-0009).</summary>
    public bool IsNullable => TypeKind == TypeKind.Nullable;

    /// <summary><c>T</c> for <c>T?</c>, the type itself otherwise.</summary>
    public TypeSymbol NonNullable => this is NullableTypeSymbol nullable ? nullable.UnderlyingType : this;

    public bool IsError => TypeKind == TypeKind.Error;

    public bool IsVoid => SpecialType == SpecialType.System_Void;

    /// <summary>The members declared on this type itself, not on its base types.</summary>
    public virtual ImmutableArray<Symbol> GetMembers() => [];

    public virtual ImmutableArray<Symbol> GetMembers(string name) => [.. GetMembers().Where(m => m.Name == name)];

    /// <summary>Every interface the type implements: its own, its base types' and the ones those inherit.</summary>
    public IEnumerable<NamedTypeSymbol> AllInterfaces()
    {
        var seen = new HashSet<NamedTypeSymbol>(ReferenceEqualityComparer.Instance);
        var pending = new Queue<NamedTypeSymbol>();
        for (TypeSymbol? type = this; type != null; type = type.BaseType)
        {
            foreach (var direct in type.Interfaces)
            {
                pending.Enqueue(direct);
            }
        }
        while (pending.TryDequeue(out var next))
        {
            if (!seen.Add(next))
            {
                continue;
            }
            yield return next;
            foreach (var inherited in next.Interfaces)
            {
                pending.Enqueue(inherited);
            }
        }
    }
}

/// <summary>A class, struct, interface, enum or delegate, possibly constructed: <c>List&lt;Slime&gt;</c>.</summary>
public abstract class NamedTypeSymbol : TypeSymbol
{
    private Dictionary<ImmutableArray<TypeSymbol>, ConstructedNamedTypeSymbol>? _constructed;

    public override SymbolKind Kind => SymbolKind.NamedType;

    public virtual NamespaceSymbol? ContainingNamespace => null;

    public virtual NamedTypeSymbol? ContainingType => null;

    public override Symbol? ContainingSymbol => (Symbol?)ContainingType ?? ContainingNamespace;

    /// <summary>The type's own type parameters, not those of the types it is nested in.</summary>
    public abstract ImmutableArray<TypeParameterSymbol> TypeParameters { get; }

    public virtual ImmutableArray<TypeSymbol> TypeArguments => [.. TypeParameters];

    public int Arity => TypeParameters.Length;

    public virtual NamedTypeSymbol OriginalDefinition => this;

    public bool IsDefinition => ReferenceEquals(OriginalDefinition, this);

    /// <summary>A static class such as <c>Console</c> or <c>Math</c>: only static members, no instances.</summary>
    public virtual bool IsStatic => false;

    public virtual bool IsAbstract => false;

    public virtual bool IsSealed => false;

    /// <summary>For an enum, the integer type its values are stored as: <c>int</c> unless declared (ADR-0024).</summary>
    public virtual NamedTypeSymbol? EnumUnderlyingType => null;

    /// <summary>The type parameters of the enclosing types, outermost first, then the type's own.</summary>
    internal ImmutableArray<TypeParameterSymbol> AllTypeParameters =>
        ContainingType is { } outer ? outer.OriginalDefinition.AllTypeParameters.AddRange(TypeParameters) : TypeParameters;

    /// <summary>The arguments for <see cref="AllTypeParameters"/>; a definition's are its own parameters.</summary>
    internal virtual ImmutableArray<TypeSymbol> AllTypeArguments => [.. AllTypeParameters];

    /// <summary>This generic type with its own type arguments, keeping those of the types it is nested in.</summary>
    public NamedTypeSymbol Construct(ImmutableArray<TypeSymbol> typeArguments) =>
        OriginalDefinition.ConstructAll((ContainingType?.AllTypeArguments ?? []).AddRange(typeArguments));

    /// <summary>
    /// The definition with arguments for all of <see cref="AllTypeParameters"/>. Constructed types are cached, so
    /// <c>List&lt;int&gt;</c> is the same object however it was reached; the parameters themselves give the definition.
    /// </summary>
    internal NamedTypeSymbol ConstructAll(ImmutableArray<TypeSymbol> allArguments)
    {
        Debug.Assert(IsDefinition, "construct from the definition");
        var parameters = AllTypeParameters;
        if (parameters.Length != allArguments.Length)
        {
            throw new ArgumentException($"{Name} takes {parameters.Length} type arguments, not {allArguments.Length}.", nameof(allArguments));
        }
        if (parameters.Length == 0 || parameters.Zip(allArguments).All(pair => ReferenceEquals(pair.First, pair.Second)))
        {
            return this;
        }
        _constructed ??= new Dictionary<ImmutableArray<TypeSymbol>, ConstructedNamedTypeSymbol>(TypeListComparer.Instance);
        if (!_constructed.TryGetValue(allArguments, out var constructed))
        {
            constructed = new ConstructedNamedTypeSymbol(this, allArguments);
            _constructed.Add(allArguments, constructed);
        }
        return constructed;
    }
}

/// <summary>A generic type with its type arguments filled in. Its members are the definition's, substituted.</summary>
internal sealed class ConstructedNamedTypeSymbol : NamedTypeSymbol
{
    private readonly NamedTypeSymbol _definition;
    private readonly ImmutableArray<TypeSymbol> _allArguments;
    private readonly Dictionary<string, ImmutableArray<Symbol>> _membersByName = [];
    private ImmutableArray<Symbol> _members;

    public ConstructedNamedTypeSymbol(NamedTypeSymbol definition, ImmutableArray<TypeSymbol> allArguments)
    {
        _definition = definition;
        _allArguments = allArguments;
        TypeMap = new TypeMap(definition.AllTypeParameters, allArguments);
    }

    public TypeMap TypeMap { get; }

    public override string Name => _definition.Name;

    public override TypeKind TypeKind => _definition.TypeKind;

    public override Accessibility Accessibility => _definition.Accessibility;

    public override NamespaceSymbol? ContainingNamespace => _definition.ContainingNamespace;

    public override NamedTypeSymbol? ContainingType => _definition.ContainingType is { } outer
        ? outer.OriginalDefinition.ConstructAll(_allArguments[..outer.OriginalDefinition.AllTypeParameters.Length])
        : null;

    public override ImmutableArray<TypeParameterSymbol> TypeParameters => _definition.TypeParameters;

    public override ImmutableArray<TypeSymbol> TypeArguments => _allArguments[(_allArguments.Length - Arity)..];

    internal override ImmutableArray<TypeSymbol> AllTypeArguments => _allArguments;

    public override NamedTypeSymbol OriginalDefinition => _definition;

    public override bool IsStatic => _definition.IsStatic;

    public override bool IsAbstract => _definition.IsAbstract;

    public override bool IsSealed => _definition.IsSealed;

    public override NamedTypeSymbol? EnumUnderlyingType => _definition.EnumUnderlyingType;

    public override NamedTypeSymbol? BaseType => _definition.BaseType is { } baseType ? (NamedTypeSymbol)TypeMap.Substitute(baseType) : null;

    public override ImmutableArray<NamedTypeSymbol> Interfaces => [.. _definition.Interfaces.Select(i => (NamedTypeSymbol)TypeMap.Substitute(i))];

    public override ImmutableArray<Symbol> GetMembers()
    {
        if (_members.IsDefault)
        {
            _members = [.. _definition.GetMembers().Select(m => GetMembers(m.Name)).SelectMany(m => m).Distinct()];
        }
        return _members;
    }

    public override ImmutableArray<Symbol> GetMembers(string name)
    {
        if (!_membersByName.TryGetValue(name, out var members))
        {
            members = [.. _definition.GetMembers(name).Select(Substitute)];
            _membersByName.Add(name, members);
        }
        return members;
    }

    private Symbol Substitute(Symbol member) => member switch
    {
        MethodSymbol method => new SubstitutedMethodSymbol(method, this, TypeMap),
        FieldSymbol field => new SubstitutedFieldSymbol(field, this, TypeMap),
        PropertySymbol property => new SubstitutedPropertySymbol(property, this, TypeMap),
        _ => member,
    };
}

/// <summary><c>array&lt;T&gt;</c>, <c>array2d&lt;T&gt;</c>, <c>array3d&lt;T&gt;</c>: the .NET <c>T[]</c>, <c>T[,]</c>, <c>T[,,]</c>.</summary>
public sealed class ArrayTypeSymbol : TypeSymbol
{
    private ImmutableArray<NamedTypeSymbol> _interfaces;

    internal ArrayTypeSymbol(SymbolTable table, TypeSymbol elementType, int rank)
    {
        Table = table;
        ElementType = elementType;
        Rank = rank;
    }

    public TypeSymbol ElementType { get; }

    public int Rank { get; }

    internal SymbolTable Table { get; }

    public override SymbolKind Kind => SymbolKind.ArrayType;

    public override TypeKind TypeKind => TypeKind.Array;

    public override string Name => Rank switch
    {
        1 => "array",
        2 => "array2d",
        _ => "array3d",
    };

    public override NamedTypeSymbol BaseType => Table.GetSpecialType(SpecialType.System_Array);

    /// <summary>A one-dimensional array is an <c>IList&lt;T&gt;</c> and <c>IReadOnlyList&lt;T&gt;</c>, as in .NET.</summary>
    public override ImmutableArray<NamedTypeSymbol> Interfaces
    {
        get
        {
            if (_interfaces.IsDefault)
            {
                _interfaces = Rank != 1
                    ? []
                    : [.. new[] { "System.Collections.Generic.IList`1", "System.Collections.Generic.IReadOnlyList`1" }
                        .Select(Table.GetWellKnownType)
                        .OfType<NamedTypeSymbol>()
                        .Select(list => list.Construct([ElementType]))];
            }
            return _interfaces;
        }
    }

    // Members such as Length come from the base type, System.Array, like any other inherited member.
}

public abstract class TypeParameterSymbol : TypeSymbol
{
    public override SymbolKind Kind => SymbolKind.TypeParameter;

    public override TypeKind TypeKind => TypeKind.TypeParameter;

    public abstract int Ordinal { get; }

    /// <summary>A <c>struct</c> (or <c>unmanaged</c>) constraint: <c>T?</c> is then <c>Nullable&lt;T&gt;</c> (ADR-0031).</summary>
    public virtual bool HasValueTypeConstraint => false;
}

/// <summary>
/// <c>T?</c>, a value of <c>T</c> or null (ADR-0009). For a value type it is the .NET <c>Nullable&lt;T&gt;</c>; for a
/// reference type it is the same .NET type marked "may be null". Both follow the same rules: no members until the
/// value is checked, and only <c>T?</c> takes <c>null</c>. One symbol per underlying type, made by the symbol table.
/// </summary>
public sealed class NullableTypeSymbol : TypeSymbol
{
    internal NullableTypeSymbol(SymbolTable table, TypeSymbol underlyingType)
    {
        Table = table;
        UnderlyingType = underlyingType;
    }

    public TypeSymbol UnderlyingType { get; }

    internal SymbolTable Table { get; }

    public override SymbolKind Kind => SymbolKind.NullableType;

    public override TypeKind TypeKind => TypeKind.Nullable;

    public override string Name => UnderlyingType.Name;

    // No members, base type or interfaces: the value has to be checked before anything is used (ADR-0009).
}

/// <summary>The type of <c>null</c> itself. It converts to every <c>T?</c> and nothing else.</summary>
public sealed class NullTypeSymbol : TypeSymbol
{
    public static NullTypeSymbol Instance { get; } = new();

    private NullTypeSymbol()
    {
    }

    public override SymbolKind Kind => SymbolKind.NullType;

    public override TypeKind TypeKind => TypeKind.Null;

    public override string Name => "null";
}

/// <summary>
/// The type of an expression that already had an error. It converts to and from everything, so one mistake is
/// reported once and not again by every expression that uses it.
/// </summary>
public sealed class ErrorTypeSymbol : TypeSymbol
{
    public static ErrorTypeSymbol Instance { get; } = new();

    private ErrorTypeSymbol()
    {
    }

    public override SymbolKind Kind => SymbolKind.ErrorType;

    public override TypeKind TypeKind => TypeKind.Error;

    public override string Name => "?";
}

/// <summary>Replaces type parameters by type arguments, through arrays and constructed types.</summary>
internal sealed class TypeMap(ImmutableArray<TypeParameterSymbol> parameters, ImmutableArray<TypeSymbol> arguments)
{
    public TypeSymbol Substitute(TypeSymbol type)
    {
        switch (type)
        {
            case TypeParameterSymbol parameter:
                var index = parameters.IndexOf(parameter, ReferenceEqualityComparer.Instance);
                return index < 0 ? parameter : arguments[index];
            case ArrayTypeSymbol array:
                var element = Substitute(array.ElementType);
                return ReferenceEquals(element, array.ElementType) ? array : array.Table.GetArrayType(element, array.Rank);
            case NullableTypeSymbol nullable:
                var underlying = Substitute(nullable.UnderlyingType);
                if (ReferenceEquals(underlying, nullable.UnderlyingType))
                {
                    return nullable;
                }
                // An unconstrained T? from .NET is T itself when T is a struct, as in C#: FirstOrDefault() on a
                // List<int> gives an int (ADR-0031).
                return nullable.UnderlyingType is TypeParameterSymbol { HasValueTypeConstraint: false } && underlying.IsValueType
                    ? underlying
                    : nullable.Table.GetNullableType(underlying);
            case NamedTypeSymbol named when named.AllTypeArguments.Length > 0:
                var substituted = named.AllTypeArguments.Select(Substitute).ToImmutableArray();
                return Enumerable.SequenceEqual(substituted, named.AllTypeArguments, ReferenceEqualityComparer.Instance)
                    ? named
                    : named.OriginalDefinition.ConstructAll(substituted);
            default:
                return type;
        }
    }
}

internal sealed class TypeListComparer : IEqualityComparer<ImmutableArray<TypeSymbol>>
{
    public static TypeListComparer Instance { get; } = new();

    public bool Equals(ImmutableArray<TypeSymbol> x, ImmutableArray<TypeSymbol> y) => Enumerable.SequenceEqual(x, y, ReferenceEqualityComparer.Instance);

    public int GetHashCode(ImmutableArray<TypeSymbol> types)
    {
        var hash = new HashCode();
        foreach (var type in types)
        {
            hash.Add(ReferenceEqualityComparer.Instance.GetHashCode(type));
        }
        return hash.ToHashCode();
    }
}
