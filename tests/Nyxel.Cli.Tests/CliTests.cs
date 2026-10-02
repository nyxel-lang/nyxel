namespace Nyxel.Cli.Tests;

public class CliTests
{
    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = Program.Run(args, stdout, stderr);
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

    [Fact]
    public void UnknownCommandFailsOnStderr()
    {
        var (code, output, error) = Run("frobnicate");

        Assert.Equal(1, code);
        Assert.Empty(output);
        Assert.Contains("unknown command 'frobnicate'", error);
    }
}
