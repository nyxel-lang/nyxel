using System.Collections.Immutable;
using CodeAnalysis = Microsoft.CodeAnalysis;

namespace Nyxel.Compiler.Symbols;

/// <summary>
/// A namespace as the program sees it: the Nyxel types declared in it together with the referenced assemblies'
/// public types of the same namespace.
/// </summary>
public sealed class NamespaceSymbol : Symbol
{
    private readonly SymbolTable _table;
    private readonly CodeAnalysis.INamespaceSymbol? _metadata;
    private readonly Dictionary<string, NamespaceSymbol?> _namespaces = [];
    private readonly Dictionary<string, ImmutableArray<NamedTypeSymbol>> _metadataTypes = [];
    private readonly List<NamedTypeSymbol> _sourceTypes = [];

    internal NamespaceSymbol(SymbolTable table, NamespaceSymbol? containingNamespace, string name, CodeAnalysis.INamespaceSymbol? metadata)
    {
        _table = table;
        ContainingNamespace = containingNamespace;
        Name = name;
        _metadata = metadata;
    }

    public override SymbolKind Kind => SymbolKind.Namespace;

    public override string Name { get; }

    public NamespaceSymbol? ContainingNamespace { get; }

    public override Symbol? ContainingSymbol => ContainingNamespace;

    public bool IsGlobal => ContainingNamespace is null;

    /// <summary><c>System.Collections.Generic</c>; empty for the global namespace.</summary>
    public string FullName => ContainingNamespace is { IsGlobal: false } outer ? $"{outer.FullName}.{Name}" : Name;

    /// <summary>A namespace directly inside this one, declared in Nyxel or in a referenced assembly.</summary>
    public NamespaceSymbol? GetNamespace(string name)
    {
        if (!_namespaces.TryGetValue(name, out var child))
        {
            var metadata = _metadata?.GetNamespaceMembers().FirstOrDefault(n => n.Name == name);
            child = metadata is null ? null : new NamespaceSymbol(_table, this, name, metadata);
            _namespaces.Add(name, child);
        }
        return child;
    }

    /// <summary>The types named <paramref name="name"/>, of any arity: the Nyxel ones first.</summary>
    public ImmutableArray<NamedTypeSymbol> GetTypes(string name)
    {
        if (!_metadataTypes.TryGetValue(name, out var metadataTypes))
        {
            metadataTypes = _metadata is null
                ? []
                : [.. _metadata.GetTypeMembers(name)
                    .Where(t => t.DeclaredAccessibility == CodeAnalysis.Accessibility.Public)
                    .Select(t => _table.GetType(t))
                    .OfType<NamedTypeSymbol>()];
            _metadataTypes.Add(name, metadataTypes);
        }
        return [.. _sourceTypes.Where(t => t.Name == name), .. metadataTypes];
    }

    internal NamespaceSymbol GetOrAddNamespace(string name)
    {
        if (GetNamespace(name) is { } existing)
        {
            return existing;
        }
        var created = new NamespaceSymbol(_table, this, name, metadata: null);
        _namespaces[name] = created;
        return created;
    }

    internal void AddType(NamedTypeSymbol type) => _sourceTypes.Add(type);
}
