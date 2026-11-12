using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static Nyxel.Compiler.Tests.Binding.BindingTestHelpers;

namespace Nyxel.Compiler.Tests.Binding;

/// <summary>
/// <c>struct enum</c> and <c>class enum</c> (ADR-0024, ADR-0039): creating their values, <c>is</c>, <c>==</c>, and
/// the C# they become (ADR-0046), checked from C# code the way an engine would use it.
/// </summary>
public class DataEnumTests
{
    // The statement goes on line 27, after eight spaces.
    private const string Header = """
        import System

        struct enum AiState {
            case Idle
            case Chasing(Target: string)
            case Fleeing(From: string, Until: float)

            public static let Start: AiState = AiState.Idle
        }

        class enum Condition {
            case Always
            case Not(Inner: Condition)
        }

        struct enum Damage {
            case Physical(Amount: int)
            case Burn(Amount: int, Seconds: float)
        }

        enum Element {
            case Fire
        }

        class C {
            func F(self, state: AiState, c: Condition, hit: Damage) {

        """;

    private static string InF(string statement) => Header + "        " + statement + "\n    }\n}\n";

    [Theory]
    [InlineData("let a = new AiState.Chasing(\"x\")", null)]
    [InlineData("let a = new AiState.Chasing(Target = \"x\")", null)]
    [InlineData("let a = new AiState.Fleeing(Until = 1.5, From = \"x\")", null)]
    [InlineData("let a = AiState.Idle", null)]
    [InlineData("let a = Condition.Always", null)]
    [InlineData("let a = new Condition.Not(Condition.Always)", null)]
    [InlineData("let a = state is AiState.Chasing and c is not Condition.Always", null)]
    [InlineData("let a = state == new AiState.Chasing(\"x\") and c != new Condition.Not(c)", null)]
    [InlineData("let a = new array<AiState>(2)", null)]
    // A case without data is a value; one with data is created with new (ADR-0024).
    [InlineData("let a = new AiState.Idle()", "NYX2088 27:21")]
    [InlineData("let a = new Element.Fire()", "NYX2088 27:21")]
    [InlineData("let a = AiState.Chasing", "NYX2089 27:17")]
    // The data is taken out with match.
    [InlineData("let a = hit.Amount", "NYX2090 27:21")]
    [InlineData("let a = state.Target", "NYX2090 27:23")]
    // Whether a value is a case is asked with is (ADR-0040).
    [InlineData("let a = state == AiState.Idle", "NYX2055 27:23")]
    [InlineData("let a = AiState.Idle != state", "NYX2055 27:30")]
    [InlineData("let a = c == Condition.Always", "NYX2055 27:19")]
    // The data goes in like a call's arguments.
    [InlineData("let a = new AiState.Chasing(1)", "NYX2008 27:37")]
    [InlineData("let a = new AiState.Chasing(Name = \"x\")", "NYX2015 27:37")]
    [InlineData("let a = new AiState.Chasing()", "NYX2013 27:17")]
    [InlineData("let a = new AiState()", "NYX2041 27:21")]
    [InlineData("let a = new Condition()", "NYX2041 27:21")]
    [InlineData("let a = 5 is AiState.Idle", "NYX2061 27:17")]
    // A static let of a struct enum isn't a constant, so it isn't an alias of a case.
    [InlineData("let a = state is AiState.Start", "NYX2082 27:34")]
    [InlineData("let a = state < state", "NYX2021 27:23")]
    // All zeros is the first case only when it has no data (ADR-0024); a class enum's elements would be null.
    [InlineData("let a = new array<Damage>(2)", "NYX2064 27:21")]
    [InlineData("let a = new array<Condition>(2)", "NYX2064 27:21")]
    public void Values(string statement, string? expected) =>
        Assert.Equal(expected is null ? [] : [expected], DiagnosticsOf(InF(statement)));

    [Theory]
    // A struct enum holds its data in place, so it can't contain itself (ADR-0024).
    [InlineData("struct enum S {\n    case A(Inner: S)\n    case B\n}", "NYX2079 2:12")]
    [InlineData("class enum S {\n    case A(Inner: S)\n}", null)]
    [InlineData("struct enum S {\n    case A(X: int = 1)\n}", "NYX2001 2:19")]
    [InlineData("class enum S {\n    case A(X: int, X: int)\n}", "NYX2031 2:20")]
    [InlineData("struct enum S : IDisposable {\n    case A(X: int)\n}", "NYX2001 1:15")]
    public void CaseDeclarations(string source, string? expected) =>
        Assert.Equal(expected is null ? [] : [expected], DiagnosticsOf("import System\n\n" + source).Select(d => Shift(d, -2)));

    [Fact]
    public void MessagesSayWhatToWrite()
    {
        Assert.Equal("'AiState.Idle' has no data, so it is a value already: write 'AiState.Idle' without 'new'.", Message("let a = new AiState.Idle()"));
        Assert.Equal("'AiState.Chasing' has data, so its values are created with 'new': 'new AiState.Chasing(Target = ...)'.", Message("let a = AiState.Chasing"));
        Assert.Equal("'Seconds' is data of the case 'Burn' of 'Damage', not a member: take it out with 'match', as in 'case Burn(_, seconds) -> ...'.", Message("let a = hit.Seconds"));
        Assert.Equal("'AiState.Idle' is a case of 'AiState'; whether a value is a case is checked with 'is': write 'state is AiState.Idle'.", Message("let a = state == AiState.Idle"));

        static string Message(string statement) => Compile(InF(statement)).GetDiagnostics().Single().Message;
    }

    private const string Game = """
        namespace Game

        public struct enum AiState {
            case Idle
            case Chasing(Target: string)
        }

        public class enum Condition {
            case Always
            case Not(Inner: Condition)
        }
        """;

    /// <summary>
    /// What ADR-0046 promises C# code: C# 15 switches over the cases without <c>_</c>, makes a value from a case's
    /// struct, and C# 14 still has <c>TryGetValue</c>; no other assembly derives from a class enum.
    /// </summary>
    [Fact]
    public void CSharpSwitchesOverTheCases()
    {
        var image = Emit(Compile(Game));
        Assert.Empty(CSharpDiagnostics(image, LanguageVersion.Preview, """
            using Game;
            public static class Use
            {
                public static string A(AiState s) => s switch { AiState.Idle => "idle", AiState.Chasing(var target) => target };
                public static string B(Condition c) => c switch { Condition.Always => "always", Condition.Not(var inner) => "not " + B(inner) };
                public static AiState C() => new AiState.Chasing("x");
            }
            """));
        Assert.Empty(CSharpDiagnostics(image, LanguageVersion.CSharp14, """
            using Game;
            public static class Use
            {
                public static bool A(AiState s) => s.TryGetValue(out AiState.Chasing chasing) && chasing.Target == "x";
                public static AiState B() => new AiState(new AiState.Chasing("x"));
                public static string C(Condition c) => c switch { Condition.Not(var inner) => "not", _ => "other" };
            }
            """));
        Assert.Equal(["CS9382"], CSharpDiagnostics(image, LanguageVersion.CSharp14, "public sealed record class Mine : Game.Condition;"));
    }

    /// <summary>The attributes of unions and closed classes are declared only when the references lack them (ADR-0047).</summary>
    [Fact]
    public void AttributesComeFromTheReferencesWhenTheyHaveThem()
    {
        Assert.Contains("internal sealed class UnionAttribute", Compile(Game).EmitCSharp());

        var attributes = CSharpCompilation.Create("Attributes",
            [CSharpSyntaxTree.ParseText("""
                namespace System.Runtime.CompilerServices
                {
                    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)] public sealed class UnionAttribute : Attribute { }
                    public interface IUnion { object Value { get; } }
                    [AttributeUsage(AttributeTargets.Class)] public sealed class IsClosedTypeAttribute : Attribute { }
                }
                """)], References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        Assert.True(attributes.Emit(stream).Success);
        var compilation = NyxelCompilation.Create("Test", [Compiler.Syntax.SyntaxTree.Parse(Game, "test.nyxel")], [.. References, MetadataReference.CreateFromImage(stream.ToArray())]);
        Assert.DoesNotContain("namespace System.Runtime.CompilerServices", compilation.EmitCSharp());
        Emit(compilation);
    }

    private static ImmutableArray<byte> Emit(NyxelCompilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return [.. stream.ToArray()];
    }

    private static string[] CSharpDiagnostics(ImmutableArray<byte> image, LanguageVersion version, string source) =>
        [.. CSharpCompilation.Create("Use",
                [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(version))],
                [.. References, MetadataReference.CreateFromImage(image)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .Select(d => d.Id)];

    /// <summary>A diagnostic as <c>NYX2002 6:9</c> moved up by the lines of the import in front of the source.</summary>
    private static string Shift(string diagnostic, int lines)
    {
        var parts = diagnostic.Split(' ', ':');
        return parts.Length < 3 ? diagnostic : $"{parts[0]} {int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) + lines}:{parts[2]}";
    }
}
