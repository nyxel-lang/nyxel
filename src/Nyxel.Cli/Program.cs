using System.Reflection;
using Nyxel.Compiler;

namespace Nyxel.Cli;

public static class Program
{
    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    /// <summary>Entry point with explicit writers so tests can run the CLI in-process.</summary>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
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

    private static void PrintHelp(TextWriter stdout)
    {
        stdout.WriteLine($"{LanguageInfo.Name} {Version}");
        stdout.WriteLine();
        stdout.WriteLine("Usage: nyxel <command>");
        stdout.WriteLine();
        stdout.WriteLine("Commands:");
        stdout.WriteLine("  --version   Print the version");
        stdout.WriteLine("  --help      Print this help");
    }
}
