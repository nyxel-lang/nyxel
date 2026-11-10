using System.Collections.Immutable;
using Nyxel.Compiler.Diagnostics;
using Nyxel.Compiler.Symbols;
using Nyxel.Compiler.Syntax;

namespace Nyxel.Compiler.Binding;

/// <summary>
/// The names one file sees (ADR-0012, ADR-0033): the types of its own namespace and the namespaces around it, then
/// those of its imports, and the namespace aliases written with <c>under</c>. Resolves the types written in the file.
/// </summary>
internal sealed class ImportScope
{
    private readonly BinderContext _context;
    private readonly ImmutableArray<NamespaceSymbol> _imports;
    private readonly Dictionary<string, NamespaceSymbol> _aliases;

    private ImportScope(BinderContext context, SyntaxTree tree, NamespaceSymbol ns, ImmutableArray<NamespaceSymbol> imports, Dictionary<string, NamespaceSymbol> aliases)
    {
        _context = context;
        Tree = tree;
        Namespace = ns;
        _imports = imports;
        _aliases = aliases;
    }

    public SyntaxTree Tree { get; }

    /// <summary>The namespace the file declares its types in; the global namespace without a <c>namespace</c> line.</summary>
    public NamespaceSymbol Namespace { get; }

    public static ImportScope Create(BinderContext context, SyntaxTree tree, NamespaceSymbol ns)
    {
        var imports = ImmutableArray.CreateBuilder<NamespaceSymbol>();
        var aliases = new Dictionary<string, NamespaceSymbol>();
        foreach (var import in tree.Root.Imports)
        {
            if (import.Name.GetFirstToken().IsMissing || import.Name.GetLastToken().IsMissing)
            {
                continue;
            }
            var imported = FindNamespace(context.Table.GlobalNamespace, import.Name);
            if (imported is null)
            {
                context.Report(DiagnosticDescriptors.NamespaceNotFound, tree, import.Name, import.Name.ToString());
                continue;
            }
            if (import.Alias is { IsMissing: false } alias)
            {
                aliases.TryAdd(alias.Text, imported);
            }
            else if (import.Alias is null)
            {
                imports.Add(imported);
            }
        }
        return new ImportScope(context, tree, ns, imports.ToImmutable(), aliases);
    }

    /// <summary><c>System.Collections.Generic</c> from the global namespace, or null.</summary>
    public static NamespaceSymbol? FindNamespace(NamespaceSymbol global, NameSyntax name) => name switch
    {
        IdentifierNameSyntax identifier => global.GetNamespace(identifier.Identifier.Text),
        QualifiedNameSyntax qualified when qualified.Right is IdentifierNameSyntax right =>
            FindNamespace(global, qualified.Left)?.GetNamespace(right.Identifier.Text),
        _ => null,
    };

    /// <summary>
    /// What a name without a qualifier means as a type or namespace: a type of the file's namespace or one around it,
    /// then a type of an import, then an alias or a namespace. Null when it is none of them; two imports with the
    /// type are reported as ambiguous.
    /// </summary>
    public Symbol? LookupTypeOrNamespace(string name, int arity, SyntaxNode location)
    {
        for (var ns = Namespace; ns != null; ns = ns.ContainingNamespace)
        {
            var own = ns.GetTypes(name).Where(t => t.Arity == arity).ToList();
            if (own.Count > 0)
            {
                return own[0];
            }
        }

        var imported = _imports.SelectMany(ns => ns.GetTypes(name)).Where(t => t.Arity == arity).Distinct().ToList();
        if (imported.Count > 1)
        {
            _context.Report(DiagnosticDescriptors.AmbiguousType, Tree, location, name, QualifiedName(imported[0]), QualifiedName(imported[1]));
            return ErrorTypeSymbol.Instance;
        }
        if (imported.Count == 1)
        {
            return imported[0];
        }

        if (arity == 0)
        {
            if (_aliases.TryGetValue(name, out var aliased))
            {
                return aliased;
            }
            for (var ns = Namespace; ns != null; ns = ns.ContainingNamespace)
            {
                if (ns.GetNamespace(name) is { } child)
                {
                    return child;
                }
            }
        }
        return null;
    }

    /// <summary>The type a type annotation names. Problems are reported and give the error type.</summary>
    public TypeSymbol BindType(TypeSyntax syntax)
    {
        if (syntax.GetFirstToken().IsMissing && syntax.GetLastToken().IsMissing)
        {
            return ErrorTypeSymbol.Instance;
        }
        switch (syntax)
        {
            case PredefinedTypeSyntax predefined:
                return (TypeSymbol?)_context.GetPredefinedType(predefined.Keyword.Kind) ?? ErrorTypeSymbol.Instance;
            case ArrayTypeSyntax array:
                var element = BindType(array.ElementType);
                return element.IsError ? element : _context.Table.GetArrayType(element, array.Rank);
            case NameSyntax name:
                return BindNamespaceOrType(name) switch
                {
                    TypeSymbol type => type,
                    NamespaceSymbol ns => Error(DiagnosticDescriptors.TypeNotFound, name, ns.FullName),
                    _ => ErrorTypeSymbol.Instance,
                };
            case NullableTypeSyntax nullable:
                return _context.Table.GetNullableType(BindType(nullable.ElementType));
            case FunctionTypeSyntax:
                return Error(DiagnosticDescriptors.NotSupportedYet, syntax, "A function type");
            case TupleTypeSyntax:
                return Error(DiagnosticDescriptors.NotSupportedYet, syntax, "A tuple type");
            default:
                return ErrorTypeSymbol.Instance;
        }
    }

    /// <summary>A name in a type position: a type, a namespace (the left of a qualified name), or null after an error.</summary>
    public Symbol? BindNamespaceOrType(NameSyntax syntax)
    {
        switch (syntax)
        {
            case SimpleNameSyntax simple when simple.Identifier.IsMissing:
                return ErrorTypeSymbol.Instance;
            case SimpleNameSyntax simple:
                var typeArguments = TypeArguments(simple);
                var found = LookupTypeOrNamespace(simple.Identifier.Text, typeArguments.Length, simple);
                if (found is null)
                {
                    return Error(DiagnosticDescriptors.TypeNotFound, simple, simple.ToString());
                }
                return found is NamedTypeSymbol named ? Construct(named, typeArguments) : found;
            case QualifiedNameSyntax qualified:
                var left = BindNamespaceOrType(qualified.Left);
                if (left is null or ErrorTypeSymbol || qualified.Right.Identifier.IsMissing)
                {
                    return ErrorTypeSymbol.Instance;
                }
                var rightArguments = TypeArguments(qualified.Right);
                var name = qualified.Right.Identifier.Text;
                Symbol? member = left switch
                {
                    NamespaceSymbol ns => (Symbol?)ns.GetTypes(name).FirstOrDefault(t => t.Arity == rightArguments.Length)
                        ?? (rightArguments.Length == 0 ? ns.GetNamespace(name) : null),
                    TypeSymbol type => type.GetMembers(name).OfType<NamedTypeSymbol>().FirstOrDefault(t => t.Arity == rightArguments.Length),
                    _ => null,
                };
                if (member is null)
                {
                    return Error(DiagnosticDescriptors.MemberNotFound, qualified.Right, name, SymbolDisplay.Format(left));
                }
                return member is NamedTypeSymbol nested ? Construct(nested, rightArguments) : member;
            default:
                return ErrorTypeSymbol.Instance;
        }
    }

    private ImmutableArray<TypeSymbol> TypeArguments(SimpleNameSyntax name) =>
        name is GenericNameSyntax generic ? [.. generic.TypeArgumentList.Arguments.Select(BindType)] : [];

    private TypeSymbol Construct(NamedTypeSymbol type, ImmutableArray<TypeSymbol> arguments) =>
        arguments.Length == 0 ? type
        : arguments.Any(a => a.IsError) ? ErrorTypeSymbol.Instance
        : type.SpecialType == Microsoft.CodeAnalysis.SpecialType.System_Nullable_T ? _context.Table.GetNullableType(arguments[0])
        : type.Construct(arguments);

    private static string QualifiedName(NamedTypeSymbol type) =>
        type.ContainingNamespace is { IsGlobal: false } ns ? $"{ns.FullName}.{SymbolDisplay.FormatType(type)}" : SymbolDisplay.FormatType(type);

    private ErrorTypeSymbol Error(DiagnosticDescriptor descriptor, SyntaxNode node, params object[] args)
    {
        _context.Report(descriptor, Tree, node, args);
        return ErrorTypeSymbol.Instance;
    }
}
