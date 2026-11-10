using Microsoft.CodeAnalysis;
using static Nyxel.Compiler.Tests.Binding.BindingTestHelpers;

namespace Nyxel.Compiler.Tests.Binding;

/// <summary>Each case is statements in <c>func F(self)</c> (they start on line 6) and the one diagnostic expected.</summary>
public class SemanticErrorTests
{
    [Theory]
    [InlineData("let a = cuont", "NYX2002 6:9")]
    [InlineData("Lsit<int>()", "NYX2004 6:1")]
    [InlineData("let a = Console.WriteLin(\"x\")", "NYX2007 6:17")]
    [InlineData("let n: int = \"3\"", "NYX2008 6:14")]
    [InlineData("let x = Console.WriteLine(\"a\")", "NYX2009 6:9")]
    [InlineData("let t = Console", "NYX2010 6:9")]
    [InlineData("let s = System", "NYX2011 6:9")]
    [InlineData("let f = Console.WriteLine", "NYX2012 6:9")]
    [InlineData("let m = Math.Max(1, 2, 3)", "NYX2013 6:9")]
    [InlineData("let l = \"abc\".Concat(\"a\", \"b\")", "NYX2016 6:15")]
    [InlineData("let l = string.Length", "NYX2017 6:16")]
    [InlineData("let l = List<int>()", "NYX2018 6:9")]
    [InlineData("let a = 1\na()", "NYX2019 7:1")]
    [InlineData("let a = true + 1", "NYX2021 6:14")]
    [InlineData("let a = -\"x\"", "NYX2022 6:9")]
    [InlineData("let a: int = 3\nlet b: int = a * 1.5", "NYX2023 7:18")]
    [InlineData("let b: byte = 300", "NYX2024 6:15")]
    [InlineData("let a = 1 / 0", "NYX2025 6:9")]
    [InlineData("let a = 1\na = 2", "NYX2026 7:1")]
    [InlineData("1 = 2", "NYX2028 6:1")]
    [InlineData("let s = \"abc\"\ns.Length = 2", "NYX2029 7:1")]
    [InlineData("let a = 1\nif true {\n    let a = 2\n}", "NYX2031 8:9")]
    [InlineData("break", "NYX2037 6:1")]
    [InlineData("let a = 1\na + 1", "NYX2038 7:1")]
    [InlineData("for c in 3 {\n}", "NYX2039 6:10")]
    [InlineData("for x in 0.5..<2 {\n}", "NYX2040 6:10")]
    [InlineData("let c = new Console()", "NYX2041 6:13")]
    [InlineData("let a = \"3\" as int", "NYX2042 6:9")]
    [InlineData("let o: object = \"a\"\nlet s = o as string", "NYX2043 7:9")]
    [InlineData("let a = new array<int>(3)\nlet b = a[0, 1]", "NYX2051 7:10")]
    [InlineData("let a = 3\nlet b = a[0]", "NYX2052 7:9")]
    [InlineData("let a: float = 1 / 2", "NYX2054 6:16")]
    [InlineData("let a: float = 10 % 3", "NYX2054 6:16")]
    [InlineData("let a = 1 / 2 * 4.0", "NYX2054 6:9")]
    [InlineData("let f: float = 1\nlet a = f * (1 / 2)", "NYX2054 7:14")]
    [InlineData("let a = null", "NYX2001 6:9")]
    [InlineData("let a = Math.Max(1, 2.5) as cuont", "NYX2004 6:29")]
    public void ErrorsInAFunction(string statements, string expected) => Assert.Equal([expected], DiagnosticsOf(InFunction(statements)));

    [Theory]
    // A member of the type needs 'self.' or the type name (ADR-0008).
    [InlineData("class C {\n    var hp: int = 1\n    func F(self) {\n        hp = 2\n    }\n}", "NYX2003 4:9")]
    [InlineData("class C {\n    static func Helper() {\n    }\n    func F(self) {\n        Helper()\n    }\n}", "NYX2003 5:9")]
    [InlineData("class C {\n    static func F() {\n        let a = self\n    }\n}", "NYX2020 3:17")]
    [InlineData("class C {\n    static func F(count: int) {\n        count = 2\n    }\n}", "NYX2027 3:9")]
    [InlineData("class C {\n    static func F(a: int, a: int) {\n    }\n}", "NYX2031 2:27")]
    [InlineData("class C {\n    static func F(count: int) {\n        let count = 1\n    }\n}", "NYX2031 3:13")]
    [InlineData("class C {\n}\nclass C {\n}", "NYX2032 3:7")]
    [InlineData("class C {\n    static func F() {\n    }\n    static func F() {\n    }\n}", "NYX2033 4:17")]
    [InlineData("class C {\n    static func F(a: int) -> int {\n        if a > 0 {\n            return 1\n        }\n    }\n}", "NYX2034 2:17")]
    [InlineData("class C {\n    static func F() {\n        return 1\n    }\n}", "NYX2035 3:16")]
    [InlineData("class C {\n    static func F() -> int {\n        return\n    }\n}", "NYX2036 3:9")]
    [InlineData("class C {\n    let a: int = 1\n    func F(self) {\n        self.a = 2\n    }\n}", "NYX2026 4:9")]
    [InlineData("class C {\n    let a: int\n}", "NYX2049 2:9")]
    [InlineData("class C {\n    let a: int\n    init(x: int) {\n        if x > 0 {\n            self.a = x\n        }\n    }\n}", "NYX2050 3:5")]
    [InlineData("class C {\n    func Secret(self) {\n    }\n}\nclass D {\n    static func F() {\n        new C().Secret()\n    }\n}", "NYX2044 7:17")]
    [InlineData("import System.Colections\n", "NYX2005 1:8")]
    [InlineData("import System.Threading\nimport System.Timers\n\nclass C {\n    static func F(t: Timer) {\n    }\n}", "NYX2006 5:22")]
    [InlineData("struct S {\n}", "NYX2001 1:1")]
    [InlineData("class C {\n    static func F() -> int {\n        while true {\n        }\n    }\n}", null)]
    public void ErrorsInDeclarations(string source, string? expected) =>
        Assert.Equal(expected is null ? [] : [expected], DiagnosticsOf(source));

    [Theory]
    [InlineData("class C {\n}", "NYX2045")]
    [InlineData("class A {\n    static func Main() {\n    }\n}\nclass B {\n    static func Main() {\n    }\n}", "NYX2046 6:17")]
    [InlineData("class A {\n    static func Main(count: int) {\n    }\n}", "NYX2047 2:17")]
    [InlineData("class A {\n    static func Main() -> string {\n        return \"\"\n    }\n}", "NYX2047 2:17")]
    [InlineData("class A {\n    static func Main(args: array<string>) -> int {\n        return 0\n    }\n}", null)]
    public void EntryPoint(string source, string? expected) =>
        Assert.Equal(expected is null ? [] : [expected], DiagnosticsOf(source, OutputKind.ConsoleApplication));

    [Fact]
    public void AnErrorIsReportedOnceNotByEveryUse()
    {
        // 'cuont' is unknown: nothing that uses it reports again.
        Assert.Equal(["NYX2002 6:9"], DiagnosticsOf(InFunction("let a = cuont + 1\nlet b = a.Length\nConsole.WriteLine(a)")));
    }

    [Fact]
    public void SyntaxErrorsDoNotCascade()
    {
        // The missing name after 'let' and the missing value are syntax errors only.
        var diagnostics = DiagnosticsOf(InFunction("let = 1\nlet b ="));
        Assert.All(diagnostics, d => Assert.StartsWith("NYX1", d));
    }
}
