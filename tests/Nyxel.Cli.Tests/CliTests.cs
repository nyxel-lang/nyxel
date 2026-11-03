namespace Nyxel.Cli.Tests;

public class CliTests
{
    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = Program.Run(args, stdout, stderr, File.ReadAllText);
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void VersionPrintsNameAndVersionWithoutCommitSuffix()
    {
        var (code, output, error) = Run("--version");

        Assert.Equal(0, code);
        Assert.Equal($"Nyxel {Program.Version}", output.TrimEnd());
        Assert.DoesNotContain('+', output);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData]
    [InlineData("--help")]
    [InlineData("-h")]
    public void HelpListsUsage(params string[] args)
    {
        var (code, output, _) = Run(args);

        Assert.Equal(0, code);
        Assert.Contains("Usage: nyxel <command>", output);
    }

    private static (int Code, string Out, string Err) RunWithFiles(Dictionary<string, string> files, params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = Program.Run(args, stdout, stderr, path => files.TryGetValue(path, out var text) ? text : throw new FileNotFoundException(path));
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void ParseReportsDiagnosticsInMSBuildFormat()
    {
        var files = new Dictionary<string, string> { ["a.nyxel"] = "class C {\n    var hp = 1\n}\n" };
        var (code, output, error) = RunWithFiles(files, "parse", "a.nyxel");

        Assert.Equal(1, code);
        Assert.Equal("a.nyxel(2,9): error NYX1115: Field 'hp' must declare its type, for example 'hp: int'.", output.TrimEnd());
        Assert.Empty(error);
    }

    [Fact]
    public void ParsePrintsTheTreeAndSucceedsOnCleanFiles()
    {
        var files = new Dictionary<string, string> { ["ok.nyxel"] = "class C {\n}\n" };
        var (code, output, _) = RunWithFiles(files, "parse", "--tree", "ok.nyxel");

        Assert.Equal(0, code);
        Assert.StartsWith("CompilationUnit 1:1-3:1", output, StringComparison.Ordinal);
        Assert.Contains("ClassKeyword \"class\"", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseFailsOnMissingFileAndWithoutArguments()
    {
        var (code, _, error) = RunWithFiles(new Dictionary<string, string>(), "parse", "missing.nyxel");
        Assert.Equal(1, code);
        Assert.Contains("cannot read 'missing.nyxel'", error, StringComparison.Ordinal);

        (code, _, error) = RunWithFiles(new Dictionary<string, string>(), "parse");
        Assert.Equal(1, code);
        Assert.Contains("Usage: nyxel parse", error, StringComparison.Ordinal);
    }

    private const string Hello = """
        import System

        class Hello {
            func Main(args: array<string>) -> int {
                Console.WriteLine($"Hello, {args.Length} args")
                return 3
            }
        }

        """;

    [Fact]
    public void RunCompilesAndRunsMainWithItsArguments()
    {
        var files = new Dictionary<string, string> { ["hello.nyxel"] = Hello };
        var (code, output, error) = RunWithFiles(files, "run", "hello.nyxel", "--", "a", "b");

        Assert.Equal(3, code);
        Assert.Equal("Hello, 2 args", output.TrimEnd());
        Assert.Empty(error);
    }

    [Fact]
    public void BuildReportsSemanticErrors()
    {
        var files = new Dictionary<string, string>
        {
            ["bad.nyxel"] = """
                class C {
                    func F() {
                        let a = cuont
                    }
                }

                """,
        };
        var (code, output, _) = RunWithFiles(files, "build", "bad.nyxel");

        Assert.Equal(1, code);
        Assert.EndsWith("bad.nyxel(3,17): error NYX2002: There is no local variable or parameter named 'cuont'.", output.TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildEmitsCSharpOrWritesTheAssembly()
    {
        var files = new Dictionary<string, string> { ["hello.nyxel"] = Hello };
        var (code, output, _) = RunWithFiles(files, "build", "--exe", "--emit-cs", "hello.nyxel");
        Assert.Equal(0, code);
        Assert.Contains("global::System.Console.WriteLine($\"Hello, {(args.Length)} args\");", output, StringComparison.Ordinal);

        var directory = Directory.CreateTempSubdirectory("nyxel-cli-");
        try
        {
            // The output directory doesn't exist yet: build creates it.
            var outputDirectory = Path.Combine(directory.FullName, "out");
            var dll = Path.Combine(outputDirectory, "hello.dll");
            (code, output, _) = RunWithFiles(files, "build", "--exe", "-o", dll, "hello.nyxel");
            Assert.Equal(0, code);
            Assert.True(File.Exists(dll), output);
            Assert.True(File.Exists(Path.Combine(outputDirectory, "hello.pdb")));
            Assert.True(File.Exists(Path.Combine(outputDirectory, "hello.runtimeconfig.json")));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void UnknownCommandFailsOnStderr()
    {
        var (code, output, error) = Run("frobnicate");

        Assert.Equal(1, code);
        Assert.Empty(output);
        Assert.Contains("unknown command 'frobnicate'", error);
    }
}
