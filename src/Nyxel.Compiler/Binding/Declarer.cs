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
            var scope = ImportScope.Create(context, tree, ns);
            scopes.Add(tree, scope);
            context.Scopes[tree] = scope;
        }
        foreach (var type in _types)
        {
            DeclareBaseType(type);
        }
        foreach (var type in _types)
        {
            DeclareMembers(type, scopes[type.SyntaxTree]);
        }
        foreach (var type in _types.Where(t => t.TypeKind == TypeKind.Struct))
        {
            CheckStructLayout(type);
        }
        // The values of the cases can name constants of any type, so they come once every member is declared.
        foreach (var type in _types.Where(t => t.TypeKind == TypeKind.Enum))
        {
            CheckCaseValues(type);
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
            switch (member)
            {
                case TypeDeclarationSyntax { Kind: SyntaxKind.ClassDeclaration or SyntaxKind.StructDeclaration } declaration:
                    if (declaration.TypeParameterList is { } typeParameters)
                    {
                        context.Report(DiagnosticDescriptors.NotSupportedYet, tree, typeParameters, "A generic type");
                    }
                    DeclareType(tree, ns, declaration, declaration.Identifier);
                    break;
                case EnumDeclarationSyntax { IsStruct: false, IsClass: false } enumeration when enumeration.Cases.All(c => c.ParameterList is null):
                    DeclareType(tree, ns, enumeration, enumeration.Identifier);
                    break;
                case EnumDeclarationSyntax enumeration:
                    // 'struct enum' and 'class enum' come next. An enum with data that says neither was reported by
                    // the parser (NYX1235), and so was a 'class enum' without data (NYX1236).
                    var hasData = enumeration.Cases.Any(c => c.ParameterList is not null);
                    if (enumeration.IsStruct || (enumeration.IsClass && hasData))
                    {
                        context.Report(DiagnosticDescriptors.NotSupportedYet, tree, enumeration.KindKeyword!, enumeration.IsStruct ? "A 'struct enum'" : "A 'class enum'");
                    }
                    break;
                default:
                    context.Report(DiagnosticDescriptors.NotSupportedYet, tree, DeclarationKeyword(member), Describe(member));
                    break;
            }
        }
    }

    private void DeclareType(SyntaxTree tree, NamespaceSymbol ns, MemberDeclarationSyntax declaration, SyntaxToken identifier)
    {
        if (identifier.IsMissing)
        {
            return;
        }
        if (ns.GetTypes(identifier.Text).Any(t => t is SourceNamedTypeSymbol))
        {
            context.Report(DiagnosticDescriptors.DuplicateTypeName, tree, identifier, identifier.Text, ns.IsGlobal ? "(global)" : ns.FullName);
            return;
        }
        var type = new SourceNamedTypeSymbol(declaration, tree, ns);
        ns.AddType(type);
        _types.Add(type);
    }

    private void DeclareBaseType(SourceNamedTypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Enum)
        {
            // An enum is a System.Enum (HasFlag, ToString); its underlying type follows the colon, int if none
            // (ADR-0024). The parser checked that only one integer type can be there; an interface it reported.
            type.DeclaredBaseType = context.GetSpecialType(SpecialType.System_Enum);
            var underlying = type.BaseList is { Types: { Count: 1 } types } && types[0] is PredefinedTypeSyntax predefined
                ? context.GetPredefinedType(predefined.Keyword.Kind)
                : null;
            type.DeclaredEnumUnderlyingType = underlying is not null && Conversions.IsWholeNumber(underlying.SpecialType) ? underlying : context.Int;
            return;
        }
        if (type.BaseList is { } baseList)
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
        var isEnum = type.TypeKind == TypeKind.Enum;
        if (type.Syntax is EnumDeclarationSyntax enumeration)
        {
            // Without written values the cases count from 0 in order, missing names included, as in C#.
            for (var ordinal = 0; ordinal < enumeration.Cases.Count; ordinal++)
            {
                var @case = enumeration.Cases[ordinal];
                if (!@case.Identifier.IsMissing && CheckName(@case.Identifier))
                {
                    type.AddMember(new SourceEnumCaseSymbol(type, @case, ordinal, context.Constants));
                }
            }
        }
        foreach (var member in type.MemberSyntaxes)
        {
            if (type.TypeKind == TypeKind.Struct)
            {
                CheckNotProtected(tree, member.Modifiers);
            }
            switch (member)
            {
                case PropertyDeclarationSyntax property when !property.Identifier.IsMissing:
                    if (isEnum && property.AccessorList.Accessors.FirstOrDefault(a => a.Kind == SyntaxKind.SetAccessorDeclaration) is { } setter)
                    {
                        context.Report(DiagnosticDescriptors.NotSupportedYet, tree, setter.Keyword, "A 'set' in an enum");
                        break;
                    }
                    if (CheckName(property.Identifier))
                    {
                        type.AddMember(DeclareProperty(type, property, scope));
                    }
                    break;
                case FieldDeclarationSyntax field when isEnum && !(field.Keyword.Kind == SyntaxKind.LetKeyword && field.Modifiers.Any(m => m.Kind == SyntaxKind.StaticKeyword)):
                case InitDeclarationSyntax when isEnum:
                    // An enum has no storage besides its cases, only static let (ADR-0045); the parser reported these (NYX1206).
                    break;
                case FieldDeclarationSyntax field when !field.Identifier.IsMissing:
                    if (CheckName(field.Identifier))
                    {
                        var fieldType = field.TypeAnnotation is { } annotation ? scope.BindType(annotation.Type) : ErrorTypeSymbol.Instance;
                        type.AddMember(new SourceFieldSymbol(type, field, fieldType, context.Constants));
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
                case FieldDeclarationSyntax or FunctionDeclarationSyntax or PropertyDeclarationSyntax:
                    break;
                default:
                    context.Report(DiagnosticDescriptors.NotSupportedYet, tree, DeclarationKeyword(member), Describe(member));
                    break;
            }
        }

        if (!hasInit && !isEnum)
        {
            // A class without an init gets a public one without parameters, if every field has a value (ADR-0013).
            type.AddMember(new SourceMethodSymbol(type, null, MethodSymbol.ConstructorName, MethodKind.Constructor, isStatic: false)
            {
                DeclaredReturnType = context.Void,
            });
        }
        // A field, or an auto property (ADR-0044), without an initial value is set by every init.
        foreach (var field in type.GetMembers().OfType<SourceFieldSymbol>())
        {
            if (field.Syntax.Initializer is null && (!hasInit || field.IsStatic))
            {
                context.Report(DiagnosticDescriptors.FieldNeedsInitialValue, tree, field.Syntax.Identifier, field.Name);
            }
        }
        foreach (var property in type.GetMembers().OfType<SourcePropertySymbol>().Where(p => p.IsAuto))
        {
            if (property.Syntax.Initializer is null && (!hasInit || property.IsStatic))
            {
                context.Report(DiagnosticDescriptors.FieldNeedsInitialValue, tree, property.Syntax.Identifier, property.Name);
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
        // An enum's 'var self' was reported by the parser (NYX1208).
        if (self?.VarKeyword is { } varKeyword && type.TypeKind == TypeKind.Class)
        {
            context.Report(DiagnosticDescriptors.VarSelfInClass, tree, varKeyword);
        }
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

    /// <summary>
    /// A property and its accessors (ADR-0011, ADR-0044): all with bodies or none, an initial value only without
    /// bodies, a <c>get</c> always, <c>set(value)</c> named when it has a body, and visibility on accessors as in C#.
    /// </summary>
    private SourcePropertySymbol DeclareProperty(SourceNamedTypeSymbol type, PropertyDeclarationSyntax syntax, ImportScope scope)
    {
        var tree = type.SyntaxTree;
        var propertyType = scope.BindType(syntax.TypeAnnotation.Type);
        var property = new SourcePropertySymbol(type, syntax, propertyType);
        foreach (var modifier in syntax.Modifiers.Where(m => m.Kind is SyntaxKind.VirtualKeyword or SyntaxKind.OverrideKeyword
            or SyntaxKind.AbstractKeyword or SyntaxKind.SealedKeyword))
        {
            context.Report(DiagnosticDescriptors.NotSupportedYet, tree, modifier, $"'{modifier.Text}'");
        }

        var accessors = syntax.AccessorList.Accessors;
        var withBodies = accessors.Count(a => a.Body is not null || a.ExpressionBody is not null);
        if (withBodies > 0 && withBodies < accessors.Count)
        {
            context.Report(DiagnosticDescriptors.AccessorBodiesMixed, tree, syntax.Identifier, property.Name);
        }
        if (withBodies > 0 && syntax.Initializer is { } initializer)
        {
            context.Report(DiagnosticDescriptors.ComputedPropertyInitializer, tree, initializer, property.Name);
        }

        foreach (var accessor in accessors)
        {
            var isGet = accessor.Kind == SyntaxKind.GetAccessorDeclaration;
            if ((isGet ? property.Getter : property.Setter) is not null)
            {
                context.Report(DiagnosticDescriptors.DuplicateAccessor, tree, accessor.Keyword, property.Name, accessor.Keyword.Text);
                continue;
            }
            var method = new SourceMethodSymbol(type, accessor, property.Name, isGet ? MethodKind.PropertyGet : MethodKind.PropertySet, property.IsStatic)
            {
                Property = property,
                DeclaredReturnType = isGet ? propertyType : context.Void,
            };
            if (!isGet)
            {
                var hasBody = accessor.Body is not null || accessor.ExpressionBody is not null;
                if (hasBody && accessor.Parameter is null)
                {
                    context.Report(DiagnosticDescriptors.SetterNeedsParameter, tree, accessor.Keyword);
                }
                else if (!hasBody && accessor.Parameter is { } parameter)
                {
                    context.Report(DiagnosticDescriptors.AutoSetterParameter, tree, parameter, parameter.Identifier.Text);
                }
                else if (accessor.Parameter is { Identifier.IsMissing: false } value)
                {
                    method.DeclaredParameters = [new SourceParameterSymbol(method, value, value.Identifier, propertyType, 0)];
                }
            }
            if (isGet)
            {
                property.Getter = method;
            }
            else
            {
                property.Setter = method;
            }
        }
        if (property.Getter is null)
        {
            context.Report(DiagnosticDescriptors.PropertyNeedsGetter, tree, syntax.Identifier, property.Name, SymbolDisplay.FormatType(propertyType));
        }
        CheckAccessorVisibility(type, property);
        return property;
    }

    /// <summary>
    /// <c>private set</c> (ADR-0011, rules as in C#): only <c>private</c> or <c>protected</c>, on one of the two accessors
    /// of a property that has both, and narrower than the property itself.
    /// </summary>
    private void CheckAccessorVisibility(SourceNamedTypeSymbol type, SourcePropertySymbol property)
    {
        var tree = type.SyntaxTree;
        var narrowed = false;
        foreach (var accessor in property.Syntax.AccessorList.Accessors)
        {
            if (type.TypeKind == TypeKind.Struct)
            {
                CheckNotProtected(tree, accessor.Modifiers);
            }
            foreach (var modifier in accessor.Modifiers)
            {
                if (type.TypeKind == TypeKind.Struct && modifier.Kind == SyntaxKind.ProtectedKeyword)
                {
                    continue;
                }
                var reason = modifier.Kind is not (SyntaxKind.PrivateKeyword or SyntaxKind.ProtectedKeyword)
                    ? "an accessor can only narrow the property's visibility, with 'private' or 'protected'"
                    : narrowed ? "only one of 'get' and 'set' can have a visibility of its own"
                    : property.Getter is null || property.Setter is null ? "only a property with both 'get' and 'set' can narrow one of them"
                    : SourceMethodSymbol.AccessorAccessibility(accessor.Modifiers) >= property.Accessibility
                        ? $"the property itself is {(property.Accessibility == Accessibility.Private ? "private" : "protected")}, so this narrows nothing"
                    : null;
                if (reason is not null)
                {
                    context.Report(DiagnosticDescriptors.AccessorVisibility, tree, modifier, modifier.Text, accessor.Keyword.Text, reason);
                }
                narrowed |= modifier.Kind is SyntaxKind.PrivateKeyword or SyntaxKind.ProtectedKeyword;
            }
        }
    }

    /// <summary>A struct can't be inherited, so nothing in it is <c>protected</c>.</summary>
    private void CheckNotProtected(SyntaxTree tree, SyntaxList<SyntaxToken> modifiers)
    {
        if (modifiers.FirstOrDefault(m => m.Kind == SyntaxKind.ProtectedKeyword) is { } protectedKeyword)
        {
            context.Report(DiagnosticDescriptors.ProtectedInStruct, tree, protectedKeyword);
        }
    }

    /// <summary>
    /// A struct holds its fields in place, so it can't contain itself, directly or through other structs (C#'s
    /// CS0523). <c>S?</c> of a struct contains an <c>S</c> too.
    /// </summary>
    private void CheckStructLayout(SourceNamedTypeSymbol type)
    {
        foreach (var (name, location, memberType) in StoredMembers(type))
        {
            if (Contains(memberType, type, []))
            {
                context.Report(DiagnosticDescriptors.StructContainsItself, type.SyntaxTree, location, type.Name, name);
            }
        }

        static bool Contains(TypeSymbol memberType, SourceNamedTypeSymbol target, HashSet<SourceNamedTypeSymbol> visited)
        {
            var stored = memberType is NullableTypeSymbol { UnderlyingType.IsValueType: true } nullable ? nullable.UnderlyingType : memberType;
            if (stored is not SourceNamedTypeSymbol { TypeKind: TypeKind.Struct } structType)
            {
                return false;
            }
            return ReferenceEquals(structType, target)
                || (visited.Add(structType) && StoredMembers(structType).Any(m => Contains(m.Type, target, visited)));
        }

        static IEnumerable<(string Name, SyntaxNode Location, TypeSymbol Type)> StoredMembers(SourceNamedTypeSymbol type) =>
            type.GetMembers().Select(m => m switch
            {
                SourceFieldSymbol { IsStatic: false } field => (field.Name, (SyntaxNode)field.Syntax.Identifier, field.Type),
                SourcePropertySymbol { IsStatic: false, IsAuto: true } property => (property.Name, property.Syntax.Identifier, property.Type),
                _ => default,
            }).Where(m => m.Name is not null);
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
        ParameterSyntax? firstOptional = null;
        foreach (var parameter in parameters.OfType<ParameterSyntax>())
        {
            if (parameter.RefKindKeyword is { } refKind)
            {
                context.Report(DiagnosticDescriptors.NotSupportedYet, tree, refKind, $"'{refKind.Text}' parameters");
            }
            // The values themselves are bound with the bodies; parameters with them go last, as in C# (CS1737).
            if (parameter.Default is not null)
            {
                firstOptional ??= parameter;
            }
            else if (firstOptional is not null && !parameter.Identifier.IsMissing)
            {
                context.Report(DiagnosticDescriptors.OptionalParameterBeforeRequired, tree, parameter.Identifier, parameter.Identifier.Text, firstOptional.Identifier.Text);
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

    /// <summary>
    /// Two cases of an enum can't have the same value (ADR-0040): a case is one of several choices. Working the values
    /// out also reports a value that isn't a constant or doesn't fit.
    /// </summary>
    private void CheckCaseValues(SourceNamedTypeSymbol type)
    {
        var seen = new Dictionary<object, SourceEnumCaseSymbol>();
        foreach (var @case in type.Cases)
        {
            if (@case.ConstantValue is not { } value)
            {
                continue;
            }
            if (seen.TryGetValue(value, out var first))
            {
                context.Report(DiagnosticDescriptors.DuplicateCaseValue, type.SyntaxTree, @case.Syntax.Identifier, @case.Name, first.Name, value, type.Name);
            }
            else
            {
                seen.Add(value, @case);
            }
        }
    }

    /// <summary>The <c>Main</c> without <c>self</c> to start the program from (ADR-0036).</summary>
    private SourceMethodSymbol? FindEntryPoint()
    {
        var valid = new List<SourceMethodSymbol>();
        // An enum's functions become C# extension members, which can't be an entry point.
        foreach (var main in _types.Where(t => t.TypeKind != TypeKind.Enum).SelectMany(t => t.GetMembers("Main")).OfType<SourceMethodSymbol>().Where(m => m.IsStatic))
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
