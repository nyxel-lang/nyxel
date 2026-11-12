using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nyxel.Compiler.Symbols;
using static Nyxel.Compiler.Tests.Binding.BindingTestHelpers;
using NyxelSeverity = Nyxel.Compiler.Diagnostics.DiagnosticSeverity;
using SymbolDisplay = Nyxel.Compiler.Symbols.SymbolDisplay;

namespace Nyxel.Compiler.Tests.Binding;

/// <summary>Enums without data (ADR-0024, ADR-0029, ADR-0040) and constants (ADR-0038).</summary>
public class EnumAndConstantTests
{
    /// <summary>
    /// Operations on constants fold at compile time with C#'s rules (ADR-0038). Each expression is written in Nyxel and
    /// in C#; the value must be the one Roslyn folds to, and an expression must be an error exactly when it is in C#.
    /// </summary>
    [Theory]
    [InlineData("1 << 33", "1 << 33")]
    [InlineData("1 << 31", "1 << 31")]
    [InlineData("-8 >> 1", "-8 >> 1")]
    [InlineData("uint.MaxValue >> 31", "uint.MaxValue >> 31")]
    [InlineData("~5", "~5")]
    [InlineData("~uint.MinValue", "~uint.MinValue")]
    [InlineData("int.MaxValue + 1", "int.MaxValue + 1")]
    [InlineData("int.MinValue / -1", "int.MinValue / -1")]
    [InlineData("int.MinValue % -1", "int.MinValue % -1")]
    [InlineData("-int.MinValue", "-int.MinValue")]
    [InlineData("uint.MaxValue + 1", "uint.MaxValue + 1")]
    [InlineData("long.MaxValue * 2", "long.MaxValue * 2")]
    [InlineData("byte.MaxValue + 1", "byte.MaxValue + 1")]
    [InlineData("int.MaxValue / 0", "int.MaxValue / 0")]
    [InlineData("int.MaxValue as long + 1", "(long)int.MaxValue + 1")]
    [InlineData("300 as byte", "(byte)300")]
    [InlineData("2.7 as int", "(int)2.7")]
    [InlineData("-2.7 as int", "(int)-2.7")]
    [InlineData("1e20 as int", "(int)1e20")]
    [InlineData("double.NaN as int", "(int)double.NaN")]
    [InlineData("char.MaxValue as int", "(int)char.MaxValue")]
    [InlineData("'a' + 1", "'a' + 1")]
    [InlineData("float.MaxValue * 2", "float.MaxValue * 2")]
    [InlineData("float.Epsilon / 2", "float.Epsilon / 2")]
    [InlineData("double.NaN < 1.0", "double.NaN < 1.0")]
    [InlineData("double.NaN != double.NaN", "double.NaN != double.NaN")]
    [InlineData("decimal.MaxValue + 1", "decimal.MaxValue + 1")]
    [InlineData("decimal.One / 0", "decimal.One / 0")]
    [InlineData("\"a\" + \"b\"", "\"a\" + \"b\"")]
    [InlineData("\"a\" + 1", "\"a\" + 1")]
    [InlineData("\"a\" == \"a\"", "\"a\" == \"a\"")]
    [InlineData("1 < 2 and 3 > 2", "1 < 2 && 3 > 2")]
    [InlineData("not (1 < 2)", "!(1 < 2)")]
    [InlineData("ConsoleColor.Red | ConsoleColor.Blue", "ConsoleColor.Red | ConsoleColor.Blue")]
    [InlineData("ConsoleColor.Red as int + 1", "(int)ConsoleColor.Red + 1")]
    [InlineData("5 as ConsoleColor", "(ConsoleColor)5")]
    [InlineData("~ConsoleColor.Red", "~ConsoleColor.Red")]
    [InlineData("ConsoleColor.Red as int == 12", "(int)ConsoleColor.Red == 12")]
    [InlineData("ConsoleColor.Red < ConsoleColor.Blue", "ConsoleColor.Red < ConsoleColor.Blue")]
    public void ConstantsFoldAsInCSharp(string nyxel, string csharp)
    {
        var (expected, csharpHasError) = RoslynConstant(csharp);
        var compilation = Compile(InFunction("let x = " + nyxel));
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == NyxelSeverity.Error).ToList();
        Assert.True(csharpHasError == errors.Count > 0, $"C# error: {csharpHasError}; Nyxel: {string.Join("; ", errors)}");
        if (!csharpHasError)
        {
            Assert.Equal(expected, ValueOf(Body(compilation, "F"), "x").ConstantValue);
        }
    }

    private static (object? Value, bool HasError) RoslynConstant(string expression)
    {
        var tree = CSharpSyntaxTree.ParseText($"using System; class C {{ void M() {{ var x = {expression}; }} }}");
        var compilation = CSharpCompilation.Create("Constants", [tree], References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var hasError = compilation.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error);
        var initializer = tree.GetRoot().DescendantNodes().OfType<VariableDeclaratorSyntax>().Single().Initializer!.Value;
        var value = compilation.GetSemanticModel(tree).GetConstantValue(initializer);
        return (value.HasValue ? value.Value : null, hasError);
    }

    [Fact]
    public void StaticLetWithAConstantValueIsAConstant()
    {
        var compilation = Compile("""
            import System
            import System.Numerics

            class C {
                static let Count: int = 3
                static let Twice: int = C.Count * 2
                static let Name: string = "a" + "b"
                static let Start: ConsoleColor = ConsoleColor.Red
                static let Ratio: float = C.Count
                static let Origin: Vector3 = Vector3.Zero
                static let Seed: int = Environment.ProcessorCount
                static let Maybe: int? = 3
                static var Mutable: int = 1
                let instance: int = 1
            }
            """);
        Assert.Empty(compilation.GetDiagnostics());
        var fields = compilation.Bind().Types.Single().GetMembers().OfType<FieldSymbol>().ToDictionary(f => f.Name);
        Assert.Equal(6, fields["Twice"].ConstantValue);
        Assert.Equal("ab", fields["Name"].ConstantValue);
        Assert.Equal(12, fields["Start"].ConstantValue);
        Assert.Equal(3f, fields["Ratio"].ConstantValue);
        Assert.All(new[] { "Count", "Twice", "Name", "Start", "Ratio" }, name => Assert.True(fields[name].IsConst, name));
        // A type C# can't make const, a value worked out when the program starts, or something that can change.
        Assert.All(new[] { "Origin", "Seed", "Maybe", "Mutable", "instance" }, name => Assert.False(fields[name].IsConst, name));
    }

    [Fact]
    public void CasesCountFromZeroOrTakeTheirValues()
    {
        var compilation = Compile("""
            enum Element {
                case Fire
                case Ice
                case Lightning
            }

            enum Tile : byte {
                case Floor = 1
                case Wall = 1 << 3
            }

            flags enum Layer {
                case None = 0
                case Ground = 1
                case Water = 2
                case Solid = Layer.Ground | Layer.Water
            }
            """);
        Assert.Empty(compilation.GetDiagnostics());
        var types = compilation.Bind().Types.ToDictionary(t => t.Name);
        Assert.Equal([0, 1, 2], types["Element"].Cases.Select(c => c.ConstantValue));
        Assert.Equal([(byte)1, (byte)8], types["Tile"].Cases.Select(c => c.ConstantValue));
        Assert.Equal("byte", SymbolDisplay.FormatType(types["Tile"].EnumUnderlyingType!));
        Assert.Equal(3, types["Layer"].Cases.Last().ConstantValue);
    }

    [Fact]
    public void ConstantsNameEachOtherInAnyOrder()
    {
        var compilation = Compile("""
            enum E {
                case A = C.Value
                case B = D.Value + 1
            }

            class C {
                public static let Value: int = D.Value * 2
            }

            class D {
                public static let Value: int = 5
            }
            """);
        Assert.Empty(compilation.GetDiagnostics());
        Assert.Equal([10, 6], compilation.Bind().Types.Single(t => t.Name == "E").Cases.Select(c => c.ConstantValue));
    }

    [Fact]
    public void EnumFunctionsSeeSelfAsTheEnum()
    {
        var compilation = Compile("""
            enum Element {
                case Fire
                case Ice

                public func Next(self) -> Element = if self is Element.Fire then Element.Ice else Element.Fire
                public property IsHot: bool { get = self is Element.Fire }
                public static func First() -> Element = Element.Fire
            }

            class C {
                static func F() -> bool = Element.First().Next().IsHot
            }
            """);
        // The value 'if' isn't bound yet; everything else is.
        Assert.Equal(["NYX2001 5:41"], compilation.GetDiagnostics().Select(Format));
    }

    private const string ElementSource = "import System\n\nenum Element {\n    case Fire\n    case Ice\n}\n\nclass C {\n    func F(self, e: Element) {\n";

    /// <summary>Statements in a function with a parameter <c>e: Element</c>, starting on line 10.</summary>
    private static string[] WithElement(string statements) => DiagnosticsOf(ElementSource + statements + "\n    }\n}\n");

    [Theory]
    // Two values compare with == (ADR-0024); a case is checked with 'is', on either side (ADR-0040, ADR-0045).
    [InlineData("let a = e == e", null)]
    [InlineData("let a = e == Element.Fire", "NYX2055 10:11")]
    [InlineData("let a = e != Element.Ice", "NYX2055 10:11")]
    [InlineData("let a = Element.Fire == e", "NYX2055 10:22")]
    [InlineData("let a = Element.Fire != e", "NYX2055 10:22")]
    [InlineData("let k = ConsoleKey.A\nlet a = k == ConsoleKey.Escape", "NYX2055 11:11")]
    [InlineData("let a = e is Element.Fire or e is not Element.Ice", null)]
    [InlineData("let a = \"x\" is Element.Fire", "NYX2061 10:9")]
    [InlineData("let a = e is Element.Water", "NYX2007 10:22")]
    // 'is' only answers; it doesn't narrow an Element? (ADR-0024).
    [InlineData("let n: Element? = null\nif n is Element.Fire {\n    let k: Element = n\n}", "NYX2058 12:22")]
    // Only a flags enum combines its values (ADR-0024); C#'s enums all can.
    [InlineData("let a = Element.Fire | Element.Ice", "NYX2084 10:22")]
    [InlineData("let a = ~Element.Ice", "NYX2084 10:9")]
    [InlineData("var a = e\na &= Element.Ice", "NYX2084 11:3")]
    [InlineData("let a = ConsoleModifiers.Alt | ConsoleModifiers.Shift", null)]
    // Values compare in order; arithmetic goes through 'as int', for .NET's enums too (ADR-0045).
    [InlineData("let a = e < Element.Ice and Element.Ice >= e", null)]
    [InlineData("let a = ConsoleColor.Red > ConsoleColor.Blue", null)]
    [InlineData("let n: Element? = null\nlet a = n < Element.Ice", "NYX2057 11:9")]
    [InlineData("let a = e + 1", "NYX2087 10:11")]
    [InlineData("let a = Element.Ice - e", "NYX2087 10:21")]
    [InlineData("let a = e * 2", "NYX2087 10:11")]
    [InlineData("var a = e\na += 1", "NYX2087 11:3")]
    [InlineData("let a = ConsoleColor.Red + 1", "NYX2087 10:26")]
    [InlineData("let a = (e as int + 1) as Element", null)]
    [InlineData("let a = \"e: \" + e", null)]
    // Between an enum and a whole number with 'as' (ADR-0014), not a float or another enum.
    [InlineData("let a = e as int + 1\nlet b = 5 as Element\nlet c = e as long", null)]
    [InlineData("let a = e as float", "NYX2042 10:9")]
    [InlineData("let a = ConsoleColor.Red as ConsoleKey", "NYX2042 10:9")]
    // Zero is the first case.
    [InlineData("let a = new array<Element>(2)", null)]
    // Constants out of range and division by a constant zero, as in C#.
    [InlineData("let a = int.MaxValue + 1", "NYX2024 10:9")]
    [InlineData("let a = e as int / 0", "NYX2025 10:9")]
    [InlineData("let z = 0\nlet a = 5 / z", null)]
    public void EnumValues(string statements, string? expected) =>
        Assert.Equal(expected is null ? [] : [expected], WithElement(statements));

    [Theory]
    // Two cases are two choices (ADR-0040).
    [InlineData("enum E {\n    case A = 1\n    case B = 2\n    case C = 1\n}", "NYX2056 4:10")]
    [InlineData("flags enum F {\n    case A = 1\n    case B = 1\n}", "NYX2056 3:10")]
    // A value is a whole-number constant that fits the underlying type (ADR-0024).
    [InlineData("enum E : byte {\n    case A = 256\n}", "NYX2024 2:14")]
    [InlineData("enum E : uint {\n    case A = -1\n}", "NYX2024 2:14")]
    [InlineData("enum E {\n    case A = \"x\"\n}", "NYX2008 2:14")]
    [InlineData("enum E {\n    case A = 1.5\n}", "NYX2023 2:14")]
    [InlineData("import System\n\nenum E {\n    case A = Environment.ProcessorCount\n}", "NYX2082 4:14")]
    [InlineData("class C {\n    public static let Seed: int = C.Next()\n    static func Next() -> int = 1\n}\nenum E {\n    case A = C.Seed\n}", "NYX2082 6:14")]
    [InlineData("enum E {\n    case A = E.B as int\n    case B = E.A as int\n}", "NYX2083 2:10")]
    [InlineData("enum E {\n    case A = E.A as int\n}", "NYX2083 2:10")]
    [InlineData("enum E {\n    case A\n    public func A(self) { }\n}", "NYX2033 3:17")]
    // A static let in an enum (ADR-0045): an alias isn't a case, so it may share a value; it is checked with 'is'.
    [InlineData("enum Tile {\n    case A = 1\n\n    static let B: Tile = Tile.A\n    static let C: Tile\n}", "NYX2049 5:16")]
    [InlineData("enum Tile {\n    case Floor = 1\n    case Door = 2\n\n    public static let Gate: Tile = Tile.Door\n}\nclass C {\n    static func F(t: Tile) -> bool = t is Tile.Gate or t is not Tile.Floor\n}", null)]
    [InlineData("enum Tile {\n    case Floor = 1\n    case Door = 2\n\n    public static let Gate: Tile = Tile.Door\n}\nclass C {\n    static func F(t: Tile) -> bool = t == Tile.Gate\n}", "NYX2055 8:40")]
    [InlineData("enum Tile {\n    case Floor = 1\n\n    static let Gate: Tile = Tile.Floor\n}\nclass C {\n    static func F(t: Tile) -> bool = t is Tile.Gate\n}", "NYX2044 7:48")]
    [InlineData("import System\n\nenum Tile {\n    case Floor = 1\n\n    public static let Lucky: Tile = Environment.ProcessorCount as Tile\n}\nclass C {\n    static func F(t: Tile) -> bool = t is Tile.Lucky\n}", "NYX2082 9:48")]
    // Without a 0 case an array can't start as zeros; a flags enum's 0 is 'none', and C#'s enums can (ADR-0024).
    [InlineData("enum E {\n    case A = 1\n}\nclass C {\n    static func F() {\n        let a = new array<E>(2)\n    }\n}", "NYX2085 6:21")]
    [InlineData("flags enum F {\n    case A = 1\n}\nclass C {\n    static func F() {\n        let a = new array<F>(2)\n    }\n}", null)]
    [InlineData("import System\n\nclass C {\n    static func F() {\n        let a = new array<ConsoleKey>(2)\n    }\n}", null)]
    // Not yet: enums with data, and a 'set' in an enum.
    [InlineData("struct enum S {\n    case A(X: int)\n}", "NYX2001 1:1")]
    [InlineData("enum E {\n    case A\n\n    static property P: int {\n        get = 1\n        set(v) { }\n    }\n}", "NYX2001 6:9")]
    public void EnumDeclarations(string source, string? expected) =>
        Assert.Equal(expected is null ? [] : [expected], DiagnosticsOf(source));

    [Theory]
    // A default value is a constant, null for a T?, or new S() of a C# struct (ADR-0038, as in C#).
    [InlineData("static func F(a: int = 1, b: int) { }", "NYX2086 4:31")]
    [InlineData("static func F(a: int = Environment.ProcessorCount) { }", "NYX2082 4:28")]
    [InlineData("static func F(a: int = C.Seed) { }", "NYX2082 4:28")]
    [InlineData("static func F(a: string = null) { }", "NYX2059 4:31")]
    [InlineData("static func F(a: int = C.Count, b: string? = null, c: int? = 3, d: ConsoleColor = ConsoleColor.Red, e: DateTime = new DateTime()) { }", null)]
    public void DefaultValues(string function, string? expected) =>
        Assert.Equal(expected is null ? [] : [expected], DiagnosticsOf(
            "import System\n\nclass C {\n    " + function + "\n    static let Count: int = 2\n    static let Seed: int = Environment.ProcessorCount\n}\n"));

    [Fact]
    public void DefaultValuesAndConstantsCompileInCSharp()
    {
        var compilation = Compile("""
            import System

            public enum Element {
                case Fire
                case Ice

                public func Name(self, prefix: string = "") -> string = prefix + self.ToString()
                public func IsHidden(self) -> bool = self is Element.Hidden

                static let Hidden: Element = Element.Ice
                public static let Lucky: Element = (Environment.ProcessorCount % 2) as Element
            }

            public class C {
                static let Count: int = 2
                static let Start: Element = Element.Ice
                public static func F(a: int = C.Count, b: string? = null, c: int? = 3, d: Element = C.Start, e: DateTime = new DateTime(), f: float = C.Count) -> string =
                    $"{a}{b}{c}{d.Name()}{e.Year}{f}"
                static func G() -> string = C.F() + C.F(b = "x") + Element.Fire.Name(prefix = "!") + Element.Lucky.Name()
            }
            """);
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
    }

    [Fact]
    public void MessagesSayWhatToWrite()
    {
        Assert.Equal(
            "'Element.Fire' is a case of 'Element'; whether a value is a case is checked with 'is': write 'e is Element.Fire'.",
            Message(ElementSource + "let a = e == Element.Fire\n    }\n}\n"));
        Assert.Equal(
            "'Door' has the same value as 'Floor' (1), but the cases of an enum are different choices. If 'Door' is another name for 'Floor', declare it as 'static let Door: Tile = Tile.Floor'.",
            Message("enum Tile {\n    case Floor = 1\n    case Door = 1\n}"));
        Assert.Equal(
            "A default parameter value has to be known when compiling: 'C.Seed' is a 'static let' whose initial value isn't a constant expression, so it is worked out when the program starts.",
            Message("import System\n\nclass C {\n    static let Seed: int = Environment.ProcessorCount\n    static func F(a: int = C.Seed) { }\n}"));
        Assert.Equal(
            "'|' on values of 'Element' gives a value that isn't one of its cases, as 'Element' isn't a flags enum. If its values are meant to combine, declare it as 'flags enum Element'.",
            Message(ElementSource + "let a = e | e\n    }\n}\n"));
        Assert.Equal(
            "'Element.Ice' is a case of 'Element'; whether a value is a case is checked with 'is': write 'e is not Element.Ice'.",
            Message(ElementSource + "let a = Element.Ice != e\n    }\n}\n"));
        Assert.Equal(
            "'+' can't calculate with values of 'Element': the result might not be one of its cases. To calculate with the number behind a value, convert it first: 'e as int'.",
            Message(ElementSource + "let a = e + 1\n    }\n}\n"));

        static string Message(string source) => Compile(source).GetDiagnostics().Single().Message;
    }
}
