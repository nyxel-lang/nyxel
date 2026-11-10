using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Nyxel.Compiler.Binding;
using Nyxel.Compiler.Symbols;
using static Nyxel.Compiler.Tests.Binding.BindingTestHelpers;
using NyxelSyntaxTree = Nyxel.Compiler.Syntax.SyntaxTree;
using SymbolDisplay = Nyxel.Compiler.Symbols.SymbolDisplay;

namespace Nyxel.Compiler.Tests.Binding;

/// <summary>
/// <c>T?</c>, the checks that narrow it, and the operators for null (ADR-0009, ADR-0010, ADR-0023). Statements are in
/// <c>func F(self)</c> and start on line 6, as in <see cref="SemanticErrorTests"/>.
/// </summary>
public class NullSafetyTests
{
    [Theory]
    [InlineData("let x: string? = null", "string?")]
    [InlineData("let x: int? = 3", "int?")]
    [InlineData("let x: Nullable<int> = 3", "int?")]
    [InlineData("let x = Console.ReadLine()", "string?")]
    [InlineData("let x = \"a\".ToUpper()", "string")]
    [InlineData("let x = Console.ReadLine() ?? \"\"", "string")]
    [InlineData("let x = Console.ReadLine()?.Length", "int?")]
    [InlineData("let x = Console.ReadLine()?.Trim()", "string?")]
    [InlineData("let x = Console.ReadLine()?.Trim().Length", "int?")]
    [InlineData("let s: string? = null\nlet x = s ?? null", "string?")]
    [InlineData("let n: int? = null\nlet x = n ?? 1.5", "double")]
    [InlineData("let f: float? = null\nlet x = f ?? 0", "float")]
    [InlineData("let x = new List<string?>()", "List<string?>")]
    [InlineData("let x = new array<int?>(2)", "array<int?>")]
    [InlineData("let x = Console.ReadLine() ?? return", "string")]
    public void Types(string statements, string type) => Assert.Equal(type, TypeOf(BindClean(statements), "x"));

    [Fact]
    public void ALiteralAfterCoalesceTakesTheLeftType()
    {
        var value = Assert.IsType<BoundCoalesce>(ValueOf(BindClean("let f: float? = null\nlet x = f ?? 0"), "x"));
        Assert.Equal(0f, value.Right!.ConstantValue);
    }

    [Fact]
    public void NullGoesIntoTheOverloadThatTakesNull()
    {
        var call = BindClean("let s: string? = null\nConsole.WriteLine(s)").DescendantsAndSelf().OfType<BoundCall>().Single();
        Assert.Equal("Console.WriteLine(value: string?)", SymbolDisplay.Format(call.Method));
    }

    // Checks narrow a local or parameter: afterwards it is used as a T.
    [Theory]
    [InlineData("let s = Console.ReadLine()\nif s != null {\n    Console.WriteLine(s.Length)\n}")]
    [InlineData("let s = Console.ReadLine()\nif null != s {\n    Console.WriteLine(s.Length)\n}")]
    [InlineData("let s = Console.ReadLine()\nif s == null {\n    return\n}\nConsole.WriteLine(s.Length)")]
    [InlineData("let s = Console.ReadLine()\nif s != null and s.Length > 0 {\n}")]
    [InlineData("let s = Console.ReadLine()\nif s == null or s.Length == 0 {\n    return\n}\nlet n = s.Length")]
    [InlineData("let s = Console.ReadLine()\nif not (s == null) {\n    let n = s.Length\n}")]
    [InlineData("let s = Console.ReadLine()\nlet ok = s != null and s.Length > 2")]
    [InlineData("let s = Console.ReadLine()\nif s != null {\n} else {\n    return\n}\nlet n = s.Length")]
    [InlineData("let a = Console.ReadLine()\nlet b = Console.ReadLine()\nif a == null or b == null {\n    return\n}\nlet n = a.Length + b.Length")]
    [InlineData("let s = Console.ReadLine()\nif s == null {\n    throw new InvalidOperationException(\"none\")\n}\nlet n = s.Length")]
    [InlineData("let s = Console.ReadLine()\nlet t = s ?? return\nlet n = s.Length + t.Length")]
    [InlineData("let s = Console.ReadLine() ?? throw new InvalidOperationException(\"none\")\nlet n = s.Length")]
    [InlineData("let s = Console.ReadLine() ?? Console.ReadLine() ?? return\nlet n = s.Length")]
    [InlineData("var s = Console.ReadLine()\ns ??= \"x\"\nlet n = s.Length")]
    [InlineData("var s = Console.ReadLine()\nwhile s != null {\n    Console.WriteLine(s.Length)\n    s = Console.ReadLine()\n}")]
    [InlineData("for i in 0..<3 {\n    let s = Console.ReadLine() ?? continue\n    Console.WriteLine(s.Length)\n}")]
    [InlineData("while true {\n    let s = Console.ReadLine() ?? break\n    Console.WriteLine(s.Length)\n}")]
    [InlineData("let n = Console.ReadLine()?.Length\nif n != null {\n    let m = n + 1\n}")]
    [InlineData("var best: int? = null\nfor x in 0..<3 {\n    if best == null or x > best {\n        best = x\n    }\n}")]
    [InlineData("let o: object = \"a\"\nif o is string {\n    let n = o.Length\n}")]
    [InlineData("let o: object = \"a\"\nif o is not string {\n    return\n}\nlet n = o.Length")]
    [InlineData("let s = Console.ReadLine()\nif s is string {\n    let n = s.Length\n}")]
    [InlineData("Console.ReadLine()?.Trim()")]
    [InlineData("let s: string? = null\nlet t = $\"{s} {s?.Length}\"")]
    [InlineData("let s: string? = null\nlet t = \"name: \" + s")]
    public void NarrowingAccepts(string statements) => BindClean(statements);

    // ADR-0041 decision 1: a local given a value that can't be null is not null afterwards; only null is tracked.
    [Theory]
    [InlineData("var s: string? = null\ns = \"a\"\nlet n = s.Length")]
    [InlineData("var s: string? = \"a\"\nlet n = s.Length")]
    [InlineData("var s = Console.ReadLine()\nif s == null {\n    s = \"none\"\n}\nlet n = s.Length")]
    [InlineData("var n: int? = null\nn = 3\nn += 1\nlet m = n + 1")]
    public void AssigningAValueThatIsNotNullNarrows(string statements) => BindClean(statements);

    // Checking a checked T? again is redundant, not wrong (a warning later); C# gets the declared type.
    [Theory]
    [InlineData("let n: int? = 5\nif n != null {\n}")]
    [InlineData("let n: int? = 5\nlet m = n ?? 0")]
    [InlineData("let n: int? = 5\nlet s = n?.ToString()")]
    [InlineData("let s = Console.ReadLine() ?? return\nlet t = s ?? \"x\"")]
    public void CheckingACheckedValueAgainIsAllowed(string statements) => BindClean(statements);

    // ADR-0041 decision 2: == and != take a T? of a value type as in C#; nothing else does.
    [Theory]
    [InlineData("let n: int? = null\nlet b = n == 3")]
    [InlineData("let n: int? = null\nlet b = n != 3")]
    [InlineData("let a = Console.ReadLine()?.Length\nlet b: long? = null\nlet c = a == b")]
    [InlineData("let s = Console.ReadLine()\nlet b = s?.Length == 0")]
    [InlineData("let s = Console.ReadLine()\nif s?.StartsWith(\"a\") == true {\n}")]
    [InlineData("let s = Console.ReadLine()\nif s?.StartsWith(\"a\") != false {\n}")]
    public void EqualityTakesNullableValues(string statements) => BindClean(statements);

    [Fact]
    public void ALiteralComparedWithAFloatQuestionIsAFloat()
    {
        var comparison = Assert.IsType<BoundBinary>(ValueOf(BindClean("var f: float? = null\nlet x = f == 0.5"), "x"));
        Assert.Equal("float?", SymbolDisplay.FormatType(comparison.Left.Type));
        var literal = Assert.IsType<BoundConversion>(comparison.Right).Operand;
        Assert.Equal(0.5f, literal.ConstantValue);
    }

    // ADR-0041 decision 4: on a reference type that can't be null these are redundant, not wrong (a warning later).
    [Theory]
    [InlineData("let s = \"a\"\nlet t = s ?? \"b\"")]
    [InlineData("let s = \"a\"\nlet n = s?.Length")]
    [InlineData("var s = \"a\"\ns ??= \"b\"")]
    [InlineData("let s = \"a\"\nif s == null {\n    return\n}")]
    public void ChecksOnAReferenceThatCannotBeNullAreAllowed(string statements) => BindClean(statements);

    [Fact]
    public void ANarrowedReadHasTheNarrowedType()
    {
        var body = BindClean("let s = Console.ReadLine()\nif s != null {\n    let t = s\n}");
        Assert.Equal("string?", TypeOf(body, "s"));
        Assert.Equal("string", TypeOf(body, "t"));
    }

    [Theory]
    [InlineData("let s = Console.ReadLine()\nlet n = s.Length", "NYX2057 7:9")]
    [InlineData("let s = Console.ReadLine()\nConsole.WriteLine(s.Length)", "NYX2057 7:19")]
    [InlineData("let n = Console.ReadLine()?.Length\nlet m = n + 1", "NYX2057 7:9")]
    [InlineData("let s = Console.ReadLine()\nfor c in s {\n}", "NYX2057 7:10")]
    [InlineData("let s = Console.ReadLine()\nlet c = s[0]", "NYX2057 7:9")]
    [InlineData("var s = Console.ReadLine()\nif s != null {\n    s = Console.ReadLine()\n    let n = s.Length\n}", "NYX2057 9:13")]
    [InlineData("var s = Console.ReadLine()\nwhile s != null {\n    s = Console.ReadLine()\n}\nlet n = s.Length", "NYX2057 10:9")]
    [InlineData("let s = Console.ReadLine()\nif s != null or true {\n    let n = s.Length\n}", "NYX2057 8:13")]
    [InlineData("let s = Console.ReadLine()\nlet ok = s != null\nlet n = s.Length", "NYX2057 8:9")]
    [InlineData("let n = Console.ReadLine()?.Length\nlet b = n < 3", "NYX2057 7:9")]
    [InlineData("let n = Console.ReadLine()?.Length\nlet v = n.Value", "NYX2057 7:9")]
    [InlineData("let n = Console.ReadLine()?.Length\nlet v = n.HasValue", "NYX2057 7:9")]
    [InlineData("let n = Console.ReadLine()?.Length\nlet v = n.GetValueOrDefault()", "NYX2057 7:9")]
    [InlineData("var s: string? = \"a\"\ns = Console.ReadLine()\nlet n = s.Length", "NYX2057 8:9")]
    [InlineData("var o: object? = null\no = \"a\"\nlet n = o.Length", "NYX2007 8:11")]
    [InlineData("let n = Console.ReadLine()?.Length\nlet b = n == \"a\"", "NYX2021 7:11")]
    [InlineData("let s = Console.ReadLine()\nlet t: string = s", "NYX2058 7:17")]
    [InlineData("let t: string = null", "NYX2059 6:17")]
    [InlineData("let n: int = null", "NYX2059 6:14")]
    [InlineData("let a = null", "NYX2060 6:9")]
    [InlineData("let s = \"a\"\nlet b = s is int", "NYX2061 7:9")]
    [InlineData("let o: object = 1\nlet b = o is string?", "NYX2062 7:14")]
    [InlineData("let n = 3\nlet m = n ?? 0", "NYX2063 7:9")]
    [InlineData("let n = 3\nlet s = n?.ToString()", "NYX2063 7:9")]
    [InlineData("var n = 3\nn ??= 4", "NYX2063 7:1")]
    [InlineData("let n = 3\nlet b = n == null", "NYX2063 7:9")]
    [InlineData("let x = new array<string>(2)", "NYX2064 6:13")]
    [InlineData("throw \"oops\"", "NYX2065 6:7")]
    [InlineData("let s = Console.ReadLine()\nlet n = 1 + (s ?? return).Length", "NYX2001 7:19")]
    [InlineData("let s = Console.ReadLine() ?? break", "NYX2037 6:31")]
    public void Errors(string statements, string expected) => Assert.Equal([expected], DiagnosticsOf(InFunction(statements)));

    [Fact]
    public void TheMessageSaysHowToCheck()
    {
        var local = Compile(InFunction("let s = Console.ReadLine()\nlet n = s.Length")).GetDiagnostics().Single().Message;
        Assert.Contains("'if s != null { ... }'", local, StringComparison.Ordinal);
        Assert.Contains("'?.'", local, StringComparison.Ordinal);
        var call = Compile(InFunction("let n = Console.ReadLine().Length")).GetDiagnostics().Single().Message;
        Assert.Contains("'let value = Console.ReadLine() ?? return'", call, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnannotatedAssemblyGivesNullableResultsAndFields()
    {
        // ADR-0009: without nullable annotations, return values, fields and parameters are T?; only the outer type.
        var legacy = CSharpLibrary("Legacy", """
            public class Legacy
            {
                public static string Field = "f";
                public static string Name() => "x";
                public static System.Collections.Generic.List<string> Names() => null;
                public static int Count() => 1;
                public static void Take(string s) { }
            }
            """, NullableContextOptions.Disable);
        var modern = CSharpLibrary("Modern", """
            public class Modern
            {
                public static string Name() => "x";
                public static string? Maybe() => null;
                public static System.Collections.Generic.List<string?> Names() => new();
            }
            """, NullableContextOptions.Enable);
        var compilation = NyxelCompilation.Create("Test", [NyxelSyntaxTree.Parse(InFunction("""
            let a = Legacy.Name()
            let b = Legacy.Field
            let c = Legacy.Names()
            let d = Legacy.Count()
            Legacy.Take(null)
            let e = Modern.Name()
            let f = Modern.Maybe()
            let g = Modern.Names()
            """), "test.nyxel")], [.. References, legacy, modern]);
        Assert.Empty(compilation.GetDiagnostics());
        var body = Body(compilation, "F");
        string[] types = [.. "abcdefg".Select(name => TypeOf(body, name.ToString()))];
        Assert.Equal(["string?", "string?", "List<string>?", "int", "string", "string?", "List<string?>"], types);
    }

    private static MetadataReference CSharpLibrary(string name, string source, NullableContextOptions nullable)
    {
        var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source)], References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: nullable));
        using var image = new MemoryStream();
        var result = compilation.Emit(image);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return MetadataReference.CreateFromImage(image.ToArray());
    }
}
