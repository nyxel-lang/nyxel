using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Nyxel.Compiler;
using Nyxel.Compiler.Diagnostics;
using Nyxel.Compiler.Text;
using Diagnostic = Nyxel.Compiler.Diagnostics.Diagnostic;
using DiagnosticSeverity = Nyxel.Compiler.Diagnostics.DiagnosticSeverity;
using NyxelSyntaxTree = Nyxel.Compiler.Syntax.SyntaxTree;

namespace Nyxel.Cli;

/// <summary><c>nyxel build</c> and <c>nyxel run</c>: compile .nyxel files with the binder, lowering and Roslyn (M2).</summary>
internal static class BuildCommands
{
    /// <summary>
    /// <c>nyxel build [--exe] [--emit-cs] [-o file.dll] file...</c>: writes the assembly and its PDB (and, with
    /// --exe, the runtimeconfig.json that <c>dotnet file.dll</c> needs); --emit-cs prints the lowered C# instead.
    /// </summary>
    public static int Build(string[] args, TextWriter stdout, TextWriter stderr, Func<string, string> readFile)
    {
        var exe = args.Contains("--exe");
        var emitCSharp = args.Contains("--emit-cs");
        var outputIndex = Array.IndexOf(args, "-o");
        var output = outputIndex >= 0 && outputIndex + 1 < args.Length ? args[outputIndex + 1] : null;
        var paths = args.Where((a, i) => !a.StartsWith('-') && (outputIndex < 0 || i != outputIndex + 1)).ToList();
        if (paths.Count == 0 || (outputIndex >= 0 && output is null))
        {
            stderr.WriteLine("Usage: nyxel build [--exe] [--emit-cs] [-o <file.dll>] <file>...");
            return 1;
        }

        var compilation = Compile(paths, exe ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary, stderr, readFile);
        if (compilation is null)
        {
            return 1;
        }
        if (emitCSharp)
        {
            var csharp = compilation.EmitCSharp();
            if (csharp is null)
            {
                return PrintDiagnostics(compilation.GetDiagnostics(), stdout);
            }
            stdout.Write(csharp);
            return 0;
        }

        output ??= Path.ChangeExtension(paths[0], ".dll");
        using var pe = new MemoryStream();
        using var pdb = new MemoryStream();
        var result = compilation.Emit(pe, pdb);
        if (PrintDiagnostics(result.Diagnostics, stdout) != 0 || !result.Success)
        {
            return 1;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            File.WriteAllBytes(output, pe.ToArray());
            File.WriteAllBytes(Path.ChangeExtension(output, ".pdb"), pdb.ToArray());
            if (exe)
            {
                File.WriteAllText(Path.ChangeExtension(output, ".runtimeconfig.json"), RuntimeConfig());
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"nyxel: cannot write '{output}': {e.Message}");
            return 1;
        }
        stdout.WriteLine($"nyxel: wrote {output}");
        return 0;
    }

    /// <summary>
    /// <c>nyxel run file... [-- args]</c>: compiles to an executable in memory and runs its <c>Main</c> (ADR-0036)
    /// in this process. The exit code is Main's, or 1 for errors and for an exception Main doesn't catch.
    /// </summary>
    public static int RunProgram(string[] args, TextWriter stdout, TextWriter stderr, Func<string, string> readFile)
    {
        var separator = Array.IndexOf(args, "--");
        var paths = (separator < 0 ? args : args[..separator]).ToList();
        var programArgs = separator < 0 ? [] : args[(separator + 1)..];
        if (paths.Count == 0 || paths.Any(p => p.StartsWith('-')))
        {
            stderr.WriteLine("Usage: nyxel run <file>... [-- <args>]");
            return 1;
        }

        var compilation = Compile(paths, OutputKind.ConsoleApplication, stderr, readFile);
        if (compilation is null)
        {
            return 1;
        }
        using var pe = new MemoryStream();
        using var pdb = new MemoryStream();
        var result = compilation.Emit(pe, pdb);
        if (PrintDiagnostics(result.Diagnostics, stdout) != 0 || !result.Success)
        {
            return 1;
        }
        pe.Position = 0;
        pdb.Position = 0;

        var context = new AssemblyLoadContext(compilation.AssemblyName, isCollectible: true);
        var originalOut = Console.Out;
        try
        {
            var entryPoint = context.LoadFromStream(pe, pdb).EntryPoint!;
            Console.SetOut(stdout);
            var returned = entryPoint.Invoke(null, entryPoint.GetParameters().Length == 0 ? [] : [programArgs]);
            return returned is int code ? code : 0;
        }
        catch (TargetInvocationException e) when (e.InnerException is { } inner)
        {
            stdout.Flush();
            stderr.WriteLine($"Unhandled exception. {inner}");
            return 1;
        }
        finally
        {
            Console.SetOut(originalOut);
            context.Unload();
        }
    }

    private static NyxelCompilation? Compile(List<string> paths, OutputKind outputKind, TextWriter stderr, Func<string, string> readFile)
    {
        var trees = new List<NyxelSyntaxTree>();
        foreach (var path in paths)
        {
            try
            {
                // Absolute paths go into #line and the PDB (debug-mapping.md, rule 6).
                trees.Add(NyxelSyntaxTree.Parse(SourceText.From(readFile(path), Path.GetFullPath(path))));
            }
            catch (IOException e)
            {
                stderr.WriteLine($"nyxel: cannot read '{path}': {e.Message}");
                return null;
            }
        }
        return NyxelCompilation.Create(Path.GetFileNameWithoutExtension(paths[0]), trees, References(), outputKind);
    }

    /// <summary>The assemblies of the .NET this tool runs on (the shared framework); a project's own come in M3.</summary>
    private static IEnumerable<MetadataReference> References()
    {
        var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
        return ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => path.StartsWith(runtimeDirectory, StringComparison.OrdinalIgnoreCase))
            .Select(path => MetadataReference.CreateFromFile(path));
    }

    private static int PrintDiagnostics(IEnumerable<Diagnostic> diagnostics, TextWriter stdout)
    {
        var hasErrors = false;
        foreach (var diagnostic in diagnostics)
        {
            stdout.WriteLine(diagnostic);
            hasErrors |= diagnostic.Severity == DiagnosticSeverity.Error;
        }
        return hasErrors ? 1 : 0;
    }

    private static string RuntimeConfig() => $$"""
        {
          "runtimeOptions": {
            "tfm": "net{{Environment.Version.Major}}.{{Environment.Version.Minor}}",
            "framework": {
              "name": "Microsoft.NETCore.App",
              "version": "{{Environment.Version.Major}}.{{Environment.Version.Minor}}.0"
            }
          }
        }

        """;
}
