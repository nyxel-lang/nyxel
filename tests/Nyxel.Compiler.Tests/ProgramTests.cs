using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Nyxel.Compiler.Tests.Binding;
using Nyxel.Compiler.Tests.Syntax;
using NyxelSyntaxTree = Nyxel.Compiler.Syntax.SyntaxTree;

namespace Nyxel.Compiler.Tests;

[CollectionDefinition(nameof(ConsoleCollection), DisableParallelization = true)]
public class ConsoleCollection;

/// <summary>
/// The M2 acceptance programs (docs/design/semantic-analysis.md, "第一条线"): each file in Programs/ is compiled to
/// an executable, run in this process, and must print what the .out file next to it says.
/// </summary>
[Collection(nameof(ConsoleCollection))]
public class ProgramTests
{
    private static readonly string ProgramsDirectory = Path.Combine(SyntaxTestHelpers.RepoRoot, "tests", "Nyxel.Compiler.Tests", "Programs");

    public static TheoryData<string> Programs()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(ProgramsDirectory, "*" + LanguageInfo.SourceFileExtension).Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetFileNameWithoutExtension(file));
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Programs))]
    public void PrintsTheExpectedOutput(string program)
    {
        var path = Path.Combine(ProgramsDirectory, program + LanguageInfo.SourceFileExtension);
        var output = Run(File.ReadAllText(path).ReplaceLineEndings("\n"), path);
        Assert.Equal(File.ReadAllText(Path.ChangeExtension(path, ".out")).ReplaceLineEndings("\n"), output);
    }

    /// <summary>
    /// The C# each program lowers to, word for word, with the machine's path in the #line directives replaced. After
    /// an intended change to lowering, NYXEL_UPDATE_SNAPSHOTS=1 regenerates the .g.cs files.
    /// </summary>
    [Theory]
    [MemberData(nameof(Programs))]
    public void LowersToTheSnapshotCSharp(string program)
    {
        var path = Path.Combine(ProgramsDirectory, program + LanguageInfo.SourceFileExtension);
        var compilation = NyxelCompilation.Create(program, [NyxelSyntaxTree.Parse(File.ReadAllText(path).ReplaceLineEndings("\n"), path)],
            BindingTestHelpers.References, OutputKind.ConsoleApplication);
        var csharp = compilation.EmitCSharp()!.Replace($"\"{path}\"", $"\"{program}{LanguageInfo.SourceFileExtension}\"", StringComparison.Ordinal);
        var snapshot = Path.ChangeExtension(path, ".g.cs");
        if (Environment.GetEnvironmentVariable("NYXEL_UPDATE_SNAPSHOTS") == "1")
        {
            File.WriteAllText(snapshot, csharp);
        }
        Assert.Equal(File.ReadAllText(snapshot).ReplaceLineEndings("\n"), csharp);
    }

    [Fact]
    public void AnExceptionPointsAtTheNyxelLine()
    {
        var path = Path.Combine(ProgramsDirectory, "Crash.nyxel");
        var source = """
            class Crash {
                func Main() {
                    let zero = 0
                    let x = 10 / zero
                }
            }
            """;
        var exception = Assert.Throws<DivideByZeroException>(() => Run(source, path));
        Assert.Contains($"{path}:line 4", exception.StackTrace);
    }

    [Fact]
    public void TheGeneratedCSharpMapsEachStatementToItsLine()
    {
        var path = Path.Combine(ProgramsDirectory, "Hello.nyxel");
        var compilation = NyxelCompilation.Create("Hello", [NyxelSyntaxTree.Parse(File.ReadAllText(path).ReplaceLineEndings("\n"), path)],
            BindingTestHelpers.References, OutputKind.ConsoleApplication);
        var csharp = compilation.EmitCSharp()!;
        Assert.Contains($"#line (7, 9) - (7, 41) 13 \"{path}\"\n            global::System.Console.WriteLine(\"Hello, Nyxel\");", csharp);
    }

    /// <summary>Compiles the program, runs its Main in a collectible load context and returns what it printed.</summary>
    private static string Run(string source, string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var compilation = NyxelCompilation.Create(name, [NyxelSyntaxTree.Parse(source, path)], BindingTestHelpers.References, OutputKind.ConsoleApplication);
        using var pe = new MemoryStream();
        using var pdb = new MemoryStream();
        var result = compilation.Emit(pe, pdb);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics) + "\n" + compilation.EmitCSharp());
        pe.Position = 0;
        pdb.Position = 0;

        var context = new AssemblyLoadContext(name, isCollectible: true);
        var output = new StringWriter();
        var original = Console.Out;
        var culture = CultureInfo.CurrentCulture;
        try
        {
            var entryPoint = context.LoadFromStream(pe, pdb).EntryPoint!;
            Console.SetOut(output);
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            entryPoint.Invoke(null, entryPoint.GetParameters().Length == 0 ? [] : [Array.Empty<string>()]);
        }
        catch (TargetInvocationException e) when (e.InnerException is { } inner)
        {
            ExceptionDispatchInfo.Capture(inner).Throw();
            throw;
        }
        finally
        {
            Console.SetOut(original);
            CultureInfo.CurrentCulture = culture;
            context.Unload();
        }
        return output.ToString().ReplaceLineEndings("\n");
    }
}
