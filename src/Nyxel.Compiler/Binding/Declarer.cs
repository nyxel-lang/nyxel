using System.Collections.Immutable;
using Nyxel.Compiler.Diagnostics;
using Nyxel.Compiler.Symbols;
using Nyxel.Compiler.Syntax;
using SpecialType = Microsoft.CodeAnalysis.SpecialType;

namespace Nyxel.Compiler.Binding;

/// <summary>The program after the declaration passes: its types with their members, and the scope of each file.</summary>
internal sealed class DeclaredProgram(ImmutableArray<SourceNamedTypeSymbol> types, ImmutableDictionary<SyntaxTree, ImportScope> scopes, SourceMethodSymbol? entryPoint)
{
    public ImmutableArray<SourceNamedTypeSymbol> Types => types;

    public ImmutableDictionary<SyntaxTree, ImportScope> Scopes => scopes;

    public SourceMethodSymbol? EntryPoint => entryPoint;
}

/// <summary>
/// Passes 1 to 4 of docs/design/semantic-analysis.md: types, imports, base types, member signatures. Function bodies
/// are bound afterwards, each on its own.
/// </summary>
internal sealed class Declarer(BinderContext context)
{
    private readonly List<SourceNamedTypeSymbol> _types = [];

    public DeclaredProgram Declare(ImmutableArray<SyntaxTree> trees, bool needsEntryPoint)
    {
        var scopes = ImmutableDictionary.CreateBuilder<SyntaxTree, ImportScope>(ReferenceEqualityComparer.Instance);
        foreach (var tree in trees)
        {
            var ns = DeclareNamespace(tree);
            DeclareTypes(tree, ns);
            scopes.Add(tree, ImportScope.Create(context, tree, ns));
        }
        foreach (var type in _types)
        {
            DeclareBaseType(type);
        }
        foreach (var type in _types)
        {
            DeclareMembers(type, scopes[type.SyntaxTree]);
        }
        var entryPoint = needsEntryPoint ? FindEntryPoint() : null;
        return new DeclaredProgram([.. _types], scopes.ToImmutable(), entryPoint);
    }

    private NamespaceSymbol DeclareNamespace(SyntaxTree tree)
    {
        var ns = context.Table.GlobalNamespace;
        if (tree.Root.Namespace is { } declaration)
        {
            foreach (var part in NameParts(declaration.Name))
            {
                ns = ns.GetOrAddNamespace(part);
            }
        }
        return ns;

        static IEnumerable<string> NameParts(NameSyntax name) => name switch
        {
            QualifiedNameSyntax qualified => NameParts(qualified.Left).Append(qualified.Right.Identifier.Text),
            SimpleNameSyntax simple when !simple.Identifier.IsMissing => [simple.Identifier.Text],
            _ => [],
        };
    }

    private void DeclareTypes(SyntaxTree tree, NamespaceSymbol ns)
    {
        foreach (var member in tree.Root.Members)
        {
            if (member is TypeDeclarationSyntax { Kind: SyntaxKind.ClassDeclaration } declaration)
            {
                if (declaration.Identifier.IsMissing)
                {
                    continue;
                }
                if (ns.GetTypes(declaration.Identifier.Text).Any(t => t is SourceNamedTypeSymbol))
                {
                    context.Report(DiagnosticDescriptors.DuplicateTypeName, tree, declaration.Identifier, declaration.Identifier.Text, ns.IsGlobal ? "(global)" : ns.FullName);
                    continue;
                }
                if (declaration.TypeParameterList is { } typeParameters)
                {
                    context.Report(DiagnosticDescriptors.NotSupportedYet, tree, typeParameters, "A generic type");
                }
                var type = new SourceNamedTypeSymbol(declaration, tree, ns);
                ns.AddType(type);
                _types.Add(type);
            }
            else
            {
                context.Report(DiagnosticDescriptors.NotSupportedYet, tree, DeclarationKeyword(member), Describe(member));
            }
        }
    }

    private void DeclareBaseType(SourceNamedTypeSymbol type)
    {
        if (type.Syntax.BaseList is { } baseList)
        {
            context.Report(DiagnosticDescriptors.NotSupportedYet, type.SyntaxTree, baseList, "Inheritance and interfaces");
        }
        type.DeclaredBaseType = context.GetSpecialType(SpecialType.System_Object);
    }

    private void DeclareMembers(SourceNamedTypeSymbol type, ImportScope scope)
    {
        var tree = type.SyntaxTree;
        var names = new HashSet<string>();
        var hasInit = false;
        foreach (var member in type.Syntax.Members)
        {
            switch (member)
            {
                case FieldDeclarationSyntax field when !field.Identifier.IsMissing:
                    if (CheckName(field.Identifier))
                    {
                        var fieldType = field.TypeAnnotation is { } annotation ? scope.BindType(annotation.Type) : ErrorTypeSymbol.Instance;
                        type.AddMember(new SourceFieldSymbol(type, field, fieldType));
                    }
                    break;
                case FunctionDeclarationSyntax function when !function.Identifier.IsMissing:
                    if (CheckName(function.Identifier))
                    {
                        type.AddMember(DeclareFunction(type, function, scope));
                    }
                    break;
                case InitDeclarationSyntax init:
                    if (init.Identifier is { } initName)
                    {
                        context.Report(DiagnosticDescriptors.NotSupportedYet, tree, initName, "A named 'init'");
                        break;
                    }
                    if (hasInit)
                    {
                        context.Report(DiagnosticDescriptors.DuplicateMemberName, tree, init.InitKeyword, "init", type.Name);
                        break;
                    }
                    hasInit = true;
                    type.AddMember(DeclareInit(type, init, scope));
                    break;
                case FieldDeclarationSyntax or FunctionDeclarationSyntax:
                    break;
                default:
                    context.Report(DiagnosticDescriptors.NotSupportedYet, tree, DeclarationKeyword(member), Describe(member));
                    break;
            }
        }

        if (!hasInit)
        {
            // A class without an init gets a public one without parameters, if every field has a value (ADR-0013).
            type.AddMember(new SourceMethodSymbol(type, null, MethodSymbol.ConstructorName, MethodKind.Constructor, isStatic: false)
            {
                DeclaredReturnType = context.Void,
            });
        }
        foreach (var field in type.GetMembers().OfType<SourceFieldSymbol>())
        {
            if (field.Syntax.Initializer is null && (!hasInit || field.IsStatic))
            {
                context.Report(DiagnosticDescriptors.FieldNeedsInitialValue, tree, field.Syntax.Identifier, field.Name);
            }
        }

        bool CheckName(SyntaxToken identifier)
        {
            if (names.Add(identifier.Text))
            {
                return true;
            }
            context.Report(DiagnosticDescriptors.DuplicateMemberName, tree, identifier, identifier.Text, type.Name);
            return false;
        }
    }

    private SourceMethodSymbol DeclareFunction(SourceNamedTypeSymbol type, FunctionDeclarationSyntax syntax, ImportScope scope)
    {
        var tree = type.SyntaxTree;
        var parameters = syntax.ParameterList.Parameters;
        var self = parameters.Count > 0 ? parameters[0] as SelfParameterSyntax : null;
        var method = new SourceMethodSymbol(type, syntax, syntax.Identifier.Text, MethodKind.Ordinary, isStatic: self is null)
        {
            HasVarSelf = self?.VarKeyword is not null,
        };
        if (syntax.TypeParameterList is { } typeParameters)
        {
            context.Report(DiagnosticDescriptors.NotSupportedYet, tree, typeParameters, "A generic function");
        }
        foreach (var modifier in syntax.Modifiers.Where(m => m.Kind is SyntaxKind.AsyncKeyword or SyntaxKind.VirtualKeyword
            or SyntaxKind.OverrideKeyword or SyntaxKind.AbstractKeyword or SyntaxKind.SealedKeyword))
        {
            context.Report(DiagnosticDescriptors.NotSupportedYet, tree, modifier, $"'{modifier.Text}'");
        }
        if (syntax.Body is null && syntax.ExpressionBody is null)
        {
            context.Report(DiagnosticDescriptors.NotSupportedYet, tree, syntax.Identifier, "A function without a body");
        }
        method.DeclaredParameters = DeclareParameters(method, parameters.Skip(self is null ? 0 : 1), scope);
        method.DeclaredReturnType = syntax.ReturnType is { } returnType ? scope.BindType(returnType.Type) : context.Void;
        return method;
    }

    private SourceMethodSymbol DeclareInit(SourceNamedTypeSymbol type, InitDeclarationSyntax syntax, ImportScope scope)
    {
        var method = new SourceMethodSymbol(type, syntax, MethodSymbol.ConstructorName, MethodKind.Constructor, isStatic: false);
        method.DeclaredParameters = DeclareParameters(method, syntax.ParameterList.Parameters, scope);
        method.DeclaredReturnType = context.Void;
        return method;
    }

    private ImmutableArray<ParameterSymbol> DeclareParameters(SourceMethodSymbol method, IEnumerable<BaseParameterSyntax> parameters, ImportScope scope)
    {
        var tree = method.ContainingType.SyntaxTree;
        var result = ImmutableArray.CreateBuilder<ParameterSymbol>();
        var names = new HashSet<string>();
        foreach (var parameter in parameters.OfType<ParameterSyntax>())
        {
            if (parameter.RefKindKeyword is { } refKind)
            {
                context.Report(DiagnosticDescriptors.NotSupportedYet, tree, refKind, $"'{refKind.Text}' parameters");
            }
            if (parameter.Default is { } defaultValue)
            {
                context.Report(DiagnosticDescriptors.NotSupportedYet, tree, defaultValue, "A default parameter value");
            }
            if (!parameter.Identifier.IsMissing && !names.Add(parameter.Identifier.Text))
            {
                var first = parameters.OfType<ParameterSyntax>().First(p => p.Identifier.Text == parameter.Identifier.Text);
                context.Report(DiagnosticDescriptors.NameAlreadyDeclared, tree, parameter.Identifier, parameter.Identifier.Text,
                    tree.Text.GetLinePosition(first.Identifier.Position).Line + 1);
            }
            var type = parameter.Type is { } typeSyntax ? scope.BindType(typeSyntax) : ErrorTypeSymbol.Instance;
            result.Add(new SourceParameterSymbol(method, parameter, type, result.Count));
        }
        return result.ToImmutable();
    }

    /// <summary>The <c>Main</c> without <c>self</c> to start the program from (ADR-0036).</summary>
    private SourceMethodSymbol? FindEntryPoint()
    {
        var valid = new List<SourceMethodSymbol>();
        foreach (var main in _types.SelectMany(t => t.GetMembers("Main")).OfType<SourceMethodSymbol>().Where(m => m.IsStatic))
        {
            var tree = main.ContainingType.SyntaxTree;
            if (main.IsAsync)
            {
                context.Report(DiagnosticDescriptors.AsyncEntryPoint, tree, main.NameLocation);
                continue;
            }
            var parameters = main.Parameters;
            var parametersOk = parameters.Length == 0
                || parameters is [{ Type: ArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_String } }];
            var returnOk = main.ReturnsVoid || main.ReturnType.SpecialType == SpecialType.System_Int32 || main.ReturnType.IsError;
            if (!parametersOk || !returnOk)
            {
                context.Report(DiagnosticDescriptors.BadEntryPointSignature, tree, main.NameLocation);
                continue;
            }
            valid.Add(main);
        }
        if (valid.Count == 0)
        {
            if (!_types.SelectMany(t => t.GetMembers("Main")).Any())
            {
                context.Diagnostics.Report(DiagnosticDescriptors.NoEntryPoint, Location.None);
            }
            return null;
        }
        if (valid.Count > 1)
        {
            context.Report(DiagnosticDescriptors.MultipleEntryPoints, valid[1].ContainingType.SyntaxTree, valid[1].NameLocation,
                $"{valid[0].ContainingType.Name}.Main", $"{valid[1].ContainingType.Name}.Main");
        }
        return valid[0];
    }

    private static SyntaxNode DeclarationKeyword(MemberDeclarationSyntax member) => member switch
    {
        TypeDeclarationSyntax type => type.Keyword,
        EnumDeclarationSyntax enumeration => enumeration.EnumKeyword,
        ExtensionDeclarationSyntax extension => extension.ExtensionKeyword,
        PropertyDeclarationSyntax or IndexerDeclarationSyntax or EventDeclarationSyntax or OperatorDeclarationSyntax =>
            member.GetChildren().OfType<SyntaxToken>().First(t => t.Kind is SyntaxKind.PropertyKeyword or SyntaxKind.EventKeyword or SyntaxKind.OperatorKeyword),
        _ => member,
    };

    private static string Describe(MemberDeclarationSyntax member) => member switch
    {
        TypeDeclarationSyntax { Kind: SyntaxKind.StructDeclaration } => "A struct",
        TypeDeclarationSyntax { Kind: SyntaxKind.InterfaceDeclaration } => "An interface",
        TypeDeclarationSyntax => "A nested type",
        EnumDeclarationSyntax => "An enum",
        ExtensionDeclarationSyntax => "An extension",
        PropertyDeclarationSyntax => "A property",
        IndexerDeclarationSyntax => "An indexer",
        EventDeclarationSyntax => "An event",
        OperatorDeclarationSyntax => "An operator",
        _ => "This declaration",
    };
}
