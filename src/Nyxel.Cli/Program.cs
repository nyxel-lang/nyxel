using System.Reflection;
using Nyxel.Compiler;
using Nyxel.Compiler.Diagnostics;
using Nyxel.Compiler.Syntax;
using Nyxel.Compiler.Text;

namespace Nyxel.Cli;

public static class Program
{
    public static int Main(string[] args) => Run(args, Console.Out, Console.Error, File.ReadAllText);

    /// <summary>Entry point with explicit writers and file access so tests can run the CLI in-process.</summary>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, Func<string, string> readFile)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp(stdout);
            return 0;
        }

        if (args[0] is "--version" or "version")
        {
            stdout.WriteLine($"{LanguageInfo.Name} {Version}");
            return 0;
        }

        if (args[0] == "parse")
        {
            return Parse(args[1..], stdout, stderr, readFile);
        }

        stderr.WriteLine($"nyxel: unknown command '{args[0]}'. Run 'nyxel --help' for usage.");
        return 1;
    }

    /// <summary>The informational version without the "+commit" suffix the SDK appends.</summary>
    public static string Version
    {
        get
        {
            var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            var plus = version.IndexOf('+');
            return plus < 0 ? version : version[..plus];
        }
    }

    /// <summary>
    /// <c>nyxel parse [--tree] file...</c>: parses each file and prints its diagnostics, plus the syntax tree with
    /// --tree. Exit code 1 when any file has errors.
    /// </summary>
    private static int Parse(string[] args, TextWriter stdout, TextWriter stderr, Func<string, string> readFile)
    {
        var printTree = args.Contains("--tree");
        var paths = args.Where(a => a != "--tree").ToList();
        if (paths.Count == 0 || paths.Any(p => p.StartsWith('-')))
        {
            stderr.WriteLine("Usage: nyxel parse [--tree] <file>...");
            return 1;
        }

        var hasErrors = false;
        foreach (var path in paths)
        {
            string content;
            try
            {
                content = readFile(path);
            }
            catch (IOException e)
            {
                stderr.WriteLine($"nyxel: cannot read '{path}': {e.Message}");
                hasErrors = true;
                continue;
            }

            var tree = SyntaxTree.Parse(SourceText.From(content, path));
            if (printTree)
            {
                SyntaxTreePrinter.Print(tree.Root, tree.Text, stdout);
            }
            foreach (var diagnostic in tree.Diagnostics)
            {
                stdout.WriteLine(diagnostic);
                hasErrors |= diagnostic.Severity == DiagnosticSeverity.Error;
            }
        }
        return hasErrors ? 1 : 0;
    }

    private static void PrintHelp(TextWriter stdout)
    {
        stdout.WriteLine($"{LanguageInfo.Name} {Version}");
        stdout.WriteLine();
        stdout.WriteLine("Usage: nyxel <command>");
        stdout.WriteLine();
        stdout.WriteLine("Commands:");
        stdout.WriteLine("  parse [--tree] <file>...   Check the syntax of .nyxel files; --tree prints the syntax tree");
        stdout.WriteLine("  --version                  Print the version");
        stdout.WriteLine("  --help                     Print this help");
    }
}
