using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Nyxel.Compiler.Binding;
using Nyxel.Compiler.Symbols;
using Diagnostic = Nyxel.Compiler.Diagnostics.Diagnostic;
using NyxelSyntaxTree = Nyxel.Compiler.Syntax.SyntaxTree;

namespace Nyxel.Compiler.Tests.Binding;

internal static class BindingTestHelpers
{
    /// <summary>The running .NET's own assemblies, as the CLI uses them in M2.</summary>
    public static readonly ImmutableArray<MetadataReference> References =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)),
    ];

    public static NyxelCompilation Compile(string source, OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary) =>
        NyxelCompilation.Create("Test", [NyxelSyntaxTree.Parse(source, "test.nyxel")], References, outputKind);

    /// <summary>Statements in <c>func F(self)</c> of a class, with <c>System</c> and its collections imported.</summary>
    public static string InFunction(string statements) =>
        "import System\nimport System.Collections.Generic\n\nclass C {\n    func F(self) {\n" + statements + "\n    }\n}\n";

    /// <summary>The bound body of <c>F</c>, after checking that the code has no diagnostics.</summary>
    public static BoundBlock BindClean(string statements)
    {
        var compilation = Compile(InFunction(statements));
        Assert.True(compilation.GetDiagnostics().IsEmpty, "Unexpected diagnostics:\n" + string.Join("\n", compilation.GetDiagnostics()));
        return Body(compilation, "F");
    }

    public static BoundBlock Body(NyxelCompilation compilation, string function) =>
        compilation.Bind().Bodies.Single(b => b.Key.Name == function).Value;

    /// <summary>The value of the first <c>let</c> / <c>var</c> named <paramref name="name"/>.</summary>
    public static BoundExpression ValueOf(BoundBlock body, string name) =>
        body.DescendantsAndSelf().OfType<BoundLocalDeclaration>().First(d => d.Local.Name == name).Initializer;

    public static string TypeOf(BoundBlock body, string name) =>
        SymbolDisplay.FormatType(body.DescendantsAndSelf().OfType<BoundLocalDeclaration>().First(d => d.Local.Name == name).Local.Type);

    /// <summary>Diagnostics as <c>NYX2002 6:9</c>, one-based, as in ParserErrorTests.</summary>
    public static string[] DiagnosticsOf(string source, OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary) =>
        [.. Compile(source, outputKind).GetDiagnostics().Select(Format)];

    public static string Format(Diagnostic diagnostic)
    {
        if (diagnostic.Location.IsNone)
        {
            return diagnostic.Id;
        }
        var start = diagnostic.Location.LineSpan.Start;
        return $"{diagnostic.Id} {start.Line + 1}:{start.Character + 1}";
    }
}
