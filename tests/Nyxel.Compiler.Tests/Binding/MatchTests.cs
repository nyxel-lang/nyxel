using static Nyxel.Compiler.Tests.Binding.BindingTestHelpers;

namespace Nyxel.Compiler.Tests.Binding;

/// <summary>
/// <c>match</c> (ADR-0009, ADR-0010, ADR-0024, ADR-0040): its patterns, an arm never reached, every value covered,
/// the type of a match that gives a value, and the C# it becomes compiling.
/// </summary>
public class MatchTests
{
    // The statement starts on line 29, each of its lines after eight spaces.
    private const string Header = """
        import System
        import System.Net

        enum Element {
            case Fire
            case Ice
            case Lightning

            public static let Spark: Element = Element.Lightning
        }

        flags enum Layer {
            case None = 0
            case Ground = 1
        }

        struct enum Damage {
            case Physical(Amount: int)
            case Burn(Amount: int, Seconds: float)
        }

        class Spawner {
            public static let Seed: int = Environment.TickCount
            public static let Max: int = 10
        }

        class C {
            func F(self, e: Element, maybe: Element?, layer: Layer, hit: Damage, n: int, s: string?, b: bool, o: object, status: HttpStatusCode) {

        """;

    private static string InF(string statement) =>
        Header + string.Join("\n", statement.Split('\n').Select(line => "        " + line)) + "\n    }\n}\n";

    [Theory]
    [InlineData("match e {\ncase Fire -> {}\ncase Ice -> {}\ncase Lightning -> {}\n}", null)]
    // An alias is its case (ADR-0045).
    [InlineData("match e {\ncase Fire -> {}\ncase Ice -> {}\ncase Spark -> {}\n}", null)]
    [InlineData("match e {\ncase Fire -> {}\n}", "NYX2091 29:9")]
    [InlineData("match e {\ncase Fire -> {}\ncase Fire -> {}\ncase _ -> {}\n}", "NYX2092 31:14")]
    [InlineData("match e {\ncase Lightning -> {}\ncase Spark -> {}\ncase _ -> {}\n}", "NYX2092 31:14")]
    [InlineData("match n {\ncase _ -> {}\ncase 0 -> {}\n}", "NYX2092 31:14")]
    [InlineData("match e {\ncase Fire or Fire -> {}\ncase _ -> {}\n}", "NYX2092 30:22")]
    // .NET's enums may have aliases: HttpStatusCode.Found is Redirect (ADR-0040).
    [InlineData("match status {\ncase Found or Redirect -> {}\ncase _ -> {}\n}", "NYX2092 30:23")]
    [InlineData("let a = match b {\ncase true -> 1\ncase false -> 0\n}", null)]
    [InlineData("let a = match b {\ncase true -> 1\ncase false -> 0\ncase _ -> 2\n}", "NYX2092 32:14")]
    [InlineData("let a = match n {\ncase 0 -> 1\n}", "NYX2091 29:17")]
    // A flags enum's values combine (ADR-0024).
    [InlineData("match layer {\ncase None -> {}\ncase Ground -> {}\n}", "NYX2091 29:9")]
    [InlineData("match maybe {\ncase Fire -> {}\ncase Ice -> {}\ncase Lightning -> {}\n}", "NYX2091 29:9")]
    [InlineData("match maybe {\ncase Fire -> {}\ncase Ice -> {}\ncase Lightning -> {}\ncase null -> {}\n}", null)]
    [InlineData("let a = match hit {\ncase Physical(x) -> x\ncase Burn(x, _) -> x\n}", null)]
    [InlineData("let a = match hit {\ncase Physical(x) -> x\ncase Burn(x, _) -> x\ncase _ -> 0\n}", "NYX2092 32:14")]
    [InlineData("let a = match hit {\ncase Physical(x) -> x\n}", "NYX2091 29:17")]
    [InlineData("let a = match hit {\ncase Burn(x, secs) when secs > 2 -> x\ncase Burn(x, _) -> x\ncase Physical -> 0\n}", null)]
    [InlineData("match e {\ncase Fier -> {}\ncase _ -> {}\n}", "NYX2093 30:14")]
    [InlineData("match n {\ncase Foo -> {}\ncase _ -> {}\n}", "NYX2093 30:14")]
    [InlineData("let a = match hit {\ncase Burn(x) -> x\ncase _ -> 0\n}", "NYX2094 30:18")]
    [InlineData("match e {\ncase Fire(x) -> {}\ncase _ -> {}\n}", "NYX2095 30:14")]
    [InlineData("match e {\ncase Element.Fire -> {}\ncase _ -> {}\n}", "NYX2096 30:14")]
    [InlineData("let a = match n {\ncase 0 -> 1\ncase _ -> \"x\"\n}", "NYX2097 29:17")]
    [InlineData("match n {\ncase null -> {}\ncase _ -> {}\n}", "NYX2063 30:14")]
    [InlineData("match n {\ncase is string -> {}\ncase _ -> {}\n}", "NYX2061 30:14")]
    // A case label is a constant (ADR-0038).
    [InlineData("match n {\ncase Spawner.Seed -> {}\ncase _ -> {}\n}", "NYX2082 30:14")]
    [InlineData("match n {\ncase Spawner.Max -> {}\ncase 1...3 -> {}\ncase > 100 and < 200 -> {}\ncase not 50 -> {}\ncase _ -> {}\n}", null)]
    [InlineData("match s {\ncase < \"a\" -> {}\ncase _ -> {}\n}", "NYX2021 30:14")]
    [InlineData("match e {\ncase < Element.Ice -> {}\ncase _ -> {}\n}", "NYX2001 30:14")]
    [InlineData("match n {\ncase 5..<5 -> {}\ncase _ -> {}\n}", "NYX2092 30:14")]
    [InlineData("match o {\ncase is object -> {}\ncase is string -> {}\n}", "NYX2092 31:14")]
    [InlineData("match o {\ncase is Exception -> {}\ncase is ArgumentException -> {}\ncase _ -> {}\n}", "NYX2092 31:14")]
    // 'case is T' and 'case not null' narrow (ADR-0010).
    [InlineData("let a = match o {\ncase is string -> o.Length\ncase _ -> 0\n}", null)]
    [InlineData("let a = match s {\ncase not null -> s.Length\ncase null -> 0\n}", null)]
    [InlineData("let a = match s {\ncase _ -> s.Length\n}", "NYX2057 30:19")]
    // After an arm that matched null, the value isn't null (ADR-0048), as after 'if s == null { return }'.
    [InlineData("let a = match s {\ncase null -> 0\ncase _ -> s.Length\n}", null)]
    [InlineData("let a = match maybe {\ncase null or Fire -> 0\ncase _ -> maybe as int\n}", null)]
    [InlineData("let a = match s {\ncase null when b -> 0\ncase _ -> s.Length\n}", "NYX2057 31:19")]
    [InlineData("match s {\ncase null -> return\ncase _ -> {}\n}\nlet a = s.Length", null)]
    [InlineData("match s {\ncase null -> {}\ncase _ -> {}\n}\nlet a = s.Length", "NYX2057 33:17")]
    // A case's data is a let (ADR-0048).
    [InlineData("match hit {\ncase Burn(x, _) -> x = 1\ncase _ -> {}\n}", "NYX2098 30:28")]
    [InlineData("match hit {\ncase Burn(x, _) -> x += 1\ncase _ -> {}\n}", "NYX2098 30:28")]
    // Leaving from an arm, as from the right of '??' (ADR-0025).
    [InlineData("let a = match n {\ncase 0 -> 1\ncase _ -> return\n}", null)]
    [InlineData("let a = 1 + match n {\ncase 0 -> 1\ncase _ -> return\n}", "NYX2001 31:19")]
    [InlineData("let a = match n {\ncase 0 -> 1\ncase _ -> throw new Exception()\n}", null)]
    [InlineData("match n {\ncase 0 -> break\ncase _ -> {}\n}", "NYX2037 30:19")]
    public void Arms(string statement, string? expected) =>
        Assert.Equal(expected is null ? [] : [expected], DiagnosticsOf(InF(statement)));

    [Fact]
    public void MessagesSayWhatToWrite()
    {
        Assert.Equal("'match' doesn't cover every value of 'Element': add 'case Ice' and 'case Lightning', or 'case _' for the rest.",
            Message("match e {\ncase Fire -> {}\n}"));
        Assert.Equal("'match' doesn't cover every value of 'Element?': add 'case null', or 'case _' for the rest.",
            Message("match maybe {\ncase Fire -> {}\ncase Ice -> {}\ncase Lightning -> {}\n}"));
        Assert.Equal("'match' doesn't cover every value of 'Layer': add 'case _' (a flags enum's values combine, so they can't all be listed).",
            Message("match layer {\ncase None -> {}\ncase Ground -> {}\n}"));
        Assert.Equal("'match' doesn't cover every value of 'int': add 'case _' for the values not listed.",
            Message("match n {\ncase 0 -> {}\n}"));
        Assert.Equal("This 'case' is never reached: 'Spark' is the same value as 'Lightning' on line 30.",
            Message("match e {\ncase Lightning -> {}\ncase Spark -> {}\ncase _ -> {}\n}"));
        Assert.Equal("This 'case' is never reached: 'case _' on line 30 already matches every value.",
            Message("match n {\ncase _ -> {}\ncase 0 -> {}\n}"));
        Assert.Equal("'Fier' is not a case of 'Element': its cases are Fire, Ice, Lightning.",
            Message("match e {\ncase Fier -> {}\ncase _ -> {}\n}"));
        Assert.Equal("'Burn' has 2 data items (Amount, Seconds): write one name or '_' for each, in order, as in 'case Burn(amount, seconds)'.",
            Message("let a = match hit {\ncase Burn(x) -> x\ncase _ -> 0\n}"));
        Assert.Equal("A case of the enum matched is written by its name alone: 'case Fire'.",
            Message("match e {\ncase Element.Fire -> {}\ncase _ -> {}\n}"));
        Assert.Equal("'x' is data taken out of a case, which can't be assigned; copy it into a 'var' to change it.",
            Message("match hit {\ncase Burn(x, _) -> x = 1\ncase _ -> {}\n}"));

        static string Message(string statement) => Compile(InF(statement)).GetDiagnostics().Single().Message;
    }

    [Theory]
    [InlineData("let a = match n {\ncase 0 -> 1\ncase _ -> 2.5\n}", "double")]
    [InlineData("let a: float = match n {\ncase 0 -> 1\ncase _ -> 2.5\n}", "float")]
    [InlineData("let a = match n {\ncase 0 -> \"zero\"\ncase _ -> null\n}", "string?")]
    [InlineData("let a = match hit {\ncase Burn(_, seconds) -> seconds\ncase _ -> 0\n}", "float")]
    [InlineData("let a = match n {\ncase 0 -> o\ncase _ -> \"x\"\n}", "object")]
    public void TheTypeOfAMatchIsItsArms(string statement, string type)
    {
        var compilation = Compile(InF(statement));
        Assert.Empty(compilation.GetDiagnostics());
        Assert.Equal(type, TypeOf(Body(compilation, "F"), "a"));
    }

    /// <summary>
    /// What the binder lets through compiles as C#: C# reports an arm it finds covered already as an error, so the
    /// binder must have found it first; C#'s scopes don't allow a name a block inside already used.
    /// </summary>
    [Theory]
    [InlineData("match e {\ncase Fire or Ice -> {}\ncase Lightning -> {}\n}")]
    [InlineData("let a = match b {\ncase true -> 1\ncase false -> 0\n}")]
    [InlineData("let a = match hit {\ncase Physical(x) -> x\ncase Burn(x, secs) when secs > 1 -> x\ncase Burn(x, _) -> -x\n}")]
    [InlineData("let a = match hit {\ncase not Physical -> 1\ncase _ -> 0\n}")]
    [InlineData("match hit {\ncase Physical -> {}\ncase Burn(_, _) -> {}\n}")]
    [InlineData("let a = match maybe {\ncase null -> 0\ncase not null -> 1\n}")]
    [InlineData("let a = match n {\ncase 0 -> 1\ncase < 0 -> 2\ncase 1...3 -> 3\ncase > 10 and < 20 -> 4\ncase not 50 -> 5\ncase _ -> 6\n}")]
    [InlineData("let a = match o {\ncase is string -> o.Length\ncase is int -> 1\ncase _ -> 0\n}")]
    [InlineData("let a = match o {\ncase is object -> 1\n}")]
    [InlineData("let a = match s {\ncase \"a\" or \"b\" -> 1\ncase null -> 2\ncase _ -> 3\n}")]
    [InlineData("for i in 0..<3 {\nmatch i {\ncase 1 -> break\ncase 2 -> continue\ncase _ -> {}\n}\n}")]
    [InlineData("for i in 0..<3 {\nlet x = match i {\ncase 1 -> break\ncase _ -> i\n}\n}")]
    [InlineData("match hit {\ncase Burn(x, _) -> {}\ncase _ -> {}\n}\nlet x = 1")]
    [InlineData("if b {\nlet x = 1\n}\nlet x = 2")]
    [InlineData("let x = match hit {\ncase Burn(x, _) -> x\ncase _ -> 0\n}")]
    [InlineData("let a = match maybe {\ncase null -> 0\ncase _ -> maybe as int\n}")]
    [InlineData("match s {\ncase null -> return\ncase _ -> {}\n}\nlet a = s.Length")]
    public void CompilesToCSharp(string statement)
    {
        var compilation = Compile(InF(statement));
        Assert.Empty(compilation.GetDiagnostics());
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
    }
}
