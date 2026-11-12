using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Nyxel.Compiler.Binding;
using Nyxel.Compiler.Diagnostics;
using Nyxel.Compiler.Lowering;
using Nyxel.Compiler.Symbols;
using Nyxel.Compiler.Syntax;
using Nyxel.Compiler.Text;
using CodeAnalysis = Microsoft.CodeAnalysis;

namespace Nyxel.Compiler;

/// <summary>
/// Nyxel files and the assemblies they reference, compiled together (docs/design/semantic-analysis.md, "用法").
/// Binding runs once, the first time diagnostics or bound bodies are asked for.
/// </summary>
public sealed class NyxelCompilation
{
    private BoundProgram? _program;

    private NyxelCompilation(string assemblyName, ImmutableArray<SyntaxTree> syntaxTrees, CSharpCompilation references, CodeAnalysis.OutputKind outputKind)
    {
        AssemblyName = assemblyName;
        SyntaxTrees = syntaxTrees;
        OutputKind = outputKind;
        Symbols = new SymbolTable(references);
    }

    public string AssemblyName { get; }

    public ImmutableArray<SyntaxTree> SyntaxTrees { get; }

    /// <summary>A library for an engine or other assemblies, or an executable that starts at <c>Main</c> (ADR-0036).</summary>
    public CodeAnalysis.OutputKind OutputKind { get; }

    internal SymbolTable Symbols { get; }

    public NamespaceSymbol GlobalNamespace => Symbols.GlobalNamespace;

    public static NyxelCompilation Create(
        string assemblyName,
        IEnumerable<SyntaxTree> syntaxTrees,
        IEnumerable<CodeAnalysis.MetadataReference> references,
        CodeAnalysis.OutputKind outputKind = CodeAnalysis.OutputKind.DynamicallyLinkedLibrary)
    {
        var options = new CSharpCompilationOptions(outputKind, nullableContextOptions: CodeAnalysis.NullableContextOptions.Enable);
        var roslyn = CSharpCompilation.Create(assemblyName, syntaxTrees: null, references, options);
        return new NyxelCompilation(assemblyName, [.. syntaxTrees], roslyn, outputKind);
    }

    /// <summary>Syntax and semantic diagnostics of all files, by file and position.</summary>
    public ImmutableArray<Diagnostic> GetDiagnostics() =>
        [.. SyntaxTrees.SelectMany(t => t.Diagnostics).Concat(Bind().Diagnostics)
            .OrderBy(d => d.Location.IsNone ? 0 : 1)
            .ThenBy(d => d.Location.Text.Path, StringComparer.Ordinal)
            .ThenBy(d => d.Location.Span.Start)];

    /// <summary>The C# the program lowers to (<c>--emit-cs</c>); null when the program has errors.</summary>
    public string? EmitCSharp() =>
        GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error) ? null : Lowerer.Lower(Bind());

    /// <summary>
    /// Compiles to an assembly and its portable PDB, with the .nyxel files embedded so that a debugger shows them
    /// (docs/design/debug-mapping.md). Errors of the generated C# are the fallback of ADR-0004: mapped back to
    /// .nyxel and reported as internal errors.
    /// </summary>
    public NyxelEmitResult Emit(Stream peStream, Stream? pdbStream = null)
    {
        var diagnostics = GetDiagnostics();
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return new NyxelEmitResult(false, diagnostics);
        }
        var csharp = Lowerer.Lower(Bind());
        var tree = CSharpSyntaxTree.ParseText(csharp, new CSharpParseOptions(LanguageVersion.Latest), path: $"{AssemblyName}.g.cs", encoding: Encoding.UTF8);
        var embedded = SyntaxTrees
            .Where(t => t.Text.Path.Length > 0)
            .Select(t => CodeAnalysis.EmbeddedText.FromBytes(t.Text.Path, Encoding.UTF8.GetBytes(t.Text.ToString()), CodeAnalysis.Text.SourceHashAlgorithm.Sha256));
        var result = Symbols.References.AddSyntaxTrees(tree).Emit(
            peStream,
            pdbStream,
            // The sources go into the PDB, so there is nowhere to put them without one.
            embeddedTexts: pdbStream is null ? null : embedded,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
        if (result.Success)
        {
            return new NyxelEmitResult(true, diagnostics);
        }
        var internalErrors = result.Diagnostics
            .Where(d => d.Severity == CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => new Diagnostic(DiagnosticDescriptors.InternalError, MapBack(d.Location), d.Id, d.GetMessage(CultureInfo.InvariantCulture)));
        return new NyxelEmitResult(false, [.. diagnostics, .. internalErrors]);
    }

    private Location MapBack(CodeAnalysis.Location location)
    {
        var mapped = location.GetMappedLineSpan();
        if (!mapped.HasMappedPath || SyntaxTrees.FirstOrDefault(t => t.Text.Path == mapped.Path) is not { } tree)
        {
            return Location.None;
        }
        var line = Math.Min(mapped.StartLinePosition.Line, tree.Text.LineCount - 1);
        var start = Math.Min(tree.Text.GetLineStart(line) + mapped.StartLinePosition.Character, tree.Text.Length);
        return new Location(tree.Text, new TextSpan(start, 0));
    }

    internal BoundProgram Bind()
    {
        if (_program is null)
        {
            var diagnostics = new DiagnosticBag();
            var context = new BinderContext(Symbols, diagnostics);
            var declared = new Declarer(context).Declare(SyntaxTrees, OutputKind != CodeAnalysis.OutputKind.DynamicallyLinkedLibrary);
            var bodies = new Dictionary<SourceMethodSymbol, BoundBlock>(ReferenceEqualityComparer.Instance);
            var initializers = new Dictionary<Symbol, BoundExpression>(ReferenceEqualityComparer.Instance);
            var defaultValues = new Dictionary<ParameterSymbol, BoundExpression>(ReferenceEqualityComparer.Instance);
            foreach (var type in declared.Types)
            {
                var scope = declared.Scopes[type.SyntaxTree];
                // Initial values that constants needed are bound already (ADR-0038); the rest are bound now.
                foreach (var member in type.GetMembers())
                {
                    if (member is SourceFieldSymbol or SourcePropertySymbol { IsAuto: true } && context.Constants.Initializer(member) is { } initializer)
                    {
                        initializers.Add(member, initializer);
                    }
                }
                foreach (var method in MethodsWithBodies(type))
                {
                    foreach (var parameter in method.Parameters.OfType<SourceParameterSymbol>())
                    {
                        if (parameter.Syntax is ParameterSyntax { Default: { } defaultValue })
                        {
                            defaultValues.Add(parameter, new MethodBinder(context, scope, type, null).BindDefaultValue(defaultValue, parameter.Type));
                        }
                    }
                    var body = new MethodBinder(context, scope, type, method).BindBody();
                    bodies.Add(method, body);
                    CheckFlow(context, type, method, body);
                }
            }
            _program = new BoundProgram(declared, bodies, initializers, defaultValues, diagnostics.ToImmutable());
        }
        return _program;
    }

    /// <summary>The functions, <c>init</c>s and accessors of a type; the accessors of an auto property have no body.</summary>
    internal static IEnumerable<SourceMethodSymbol> MethodsWithBodies(SourceNamedTypeSymbol type) =>
        type.GetMembers().SelectMany(m => m switch
        {
            SourceMethodSymbol method => [method],
            SourcePropertySymbol { IsAuto: false } property => new[] { property.Getter, property.Setter }.OfType<SourceMethodSymbol>(),
            _ => [],
        });

    private static void CheckFlow(BinderContext context, SourceNamedTypeSymbol type, SourceMethodSymbol method, BoundBlock body)
    {
        if (method.Syntax is null)
        {
            return;
        }
        if (method.MethodKind is MethodKind.Ordinary or MethodKind.PropertyGet && !method.ReturnsVoid && !method.ReturnType.IsError && FlowAnalysis.CanComplete(body))
        {
            context.Report(DiagnosticDescriptors.NotAllPathsReturn, type.SyntaxTree, method.NameLocation, method.Name);
        }
        if (method.MethodKind == MethodKind.Constructor)
        {
            // Fields and auto properties (ADR-0044) without an initial value.
            var unset = type.GetMembers().Where(m => m switch
            {
                SourceFieldSymbol field => !field.IsStatic && field.Syntax.Initializer is null,
                SourcePropertySymbol property => property is { IsAuto: true, IsStatic: false, Syntax.Initializer: null },
                _ => false,
            }).ToList();
            foreach (var member in FlowAnalysis.MembersNotSetOnEveryPath(body, unset))
            {
                context.Report(DiagnosticDescriptors.FieldNotSetInInit, type.SyntaxTree, method.NameLocation, member.Name);
            }
        }
    }
}

/// <summary>Whether <see cref="NyxelCompilation.Emit"/> wrote an assembly, and the diagnostics.</summary>
public sealed record NyxelEmitResult(bool Success, ImmutableArray<Diagnostic> Diagnostics);

/// <summary>The result of binding: the declared program, the bound bodies and initial values, and the diagnostics.</summary>
internal sealed class BoundProgram(
    DeclaredProgram declared,
    IReadOnlyDictionary<SourceMethodSymbol, BoundBlock> bodies,
    IReadOnlyDictionary<Symbol, BoundExpression> initializers,
    IReadOnlyDictionary<ParameterSymbol, BoundExpression> defaultValues,
    ImmutableArray<Diagnostic> diagnostics)
{
    public ImmutableArray<SourceNamedTypeSymbol> Types => declared.Types;

    public SourceMethodSymbol? EntryPoint => declared.EntryPoint;

    public IReadOnlyDictionary<SourceMethodSymbol, BoundBlock> Bodies => bodies;

    /// <summary>The initial values of fields and auto properties.</summary>
    public IReadOnlyDictionary<Symbol, BoundExpression> Initializers => initializers;

    /// <summary>The default values of parameters (<c>count: int = 1</c>), converted to their types.</summary>
    public IReadOnlyDictionary<ParameterSymbol, BoundExpression> DefaultValues => defaultValues;

    public ImmutableArray<Diagnostic> Diagnostics => diagnostics;
}
