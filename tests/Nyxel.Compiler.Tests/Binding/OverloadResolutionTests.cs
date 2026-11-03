using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nyxel.Compiler.Binding;
using Nyxel.Compiler.Symbols;
using static Nyxel.Compiler.Tests.Binding.BindingTestHelpers;
using SymbolDisplay = Nyxel.Compiler.Symbols.SymbolDisplay;

namespace Nyxel.Compiler.Tests.Binding;

/// <summary>
/// The binder implements C#'s overload resolution itself (docs/design/semantic-analysis.md). Each case writes the same
/// call in Nyxel and in C#, and the overload the binder picks must be the one Roslyn picks.
/// </summary>
public class OverloadResolutionTests
{
    [Theory]
    [InlineData("Console.WriteLine(\"a\")", "System.Console.WriteLine(\"a\");")]
    [InlineData("Console.WriteLine(1)", "System.Console.WriteLine(1);")]
    [InlineData("Console.WriteLine(1.5)", "System.Console.WriteLine(1.5);")]
    [InlineData("Console.WriteLine(3000000000)", "System.Console.WriteLine(3000000000);")]
    [InlineData("Console.WriteLine('c')", "System.Console.WriteLine('c');")]
    [InlineData("Console.WriteLine(true)", "System.Console.WriteLine(true);")]
    [InlineData("Console.WriteLine()", "System.Console.WriteLine();")]
    [InlineData("Console.WriteLine(new List<int>())", "System.Console.WriteLine(new System.Collections.Generic.List<int>());")]
    [InlineData("Console.WriteLine($\"{1}\")", "System.Console.WriteLine($\"{1}\");")]
    [InlineData("let x: long = 1\nConsole.WriteLine(x)", "long x = 1; System.Console.WriteLine(x);")]
    [InlineData("let x: float = 1\nConsole.WriteLine(x)", "float x = 1; System.Console.WriteLine(x);")]
    [InlineData("let x: byte = 1\nConsole.WriteLine(x)", "byte x = 1; System.Console.WriteLine(x);")]
    [InlineData("let x: short = 1\nConsole.WriteLine(x)", "short x = 1; System.Console.WriteLine(x);")]
    [InlineData("let a = Math.Max(1, 2)", "var a = System.Math.Max(1, 2);")]
    [InlineData("let a = Math.Max(1, 2.5)", "var a = System.Math.Max(1, 2.5);")]
    [InlineData("let x: long = 1\nlet a = Math.Max(x, 2)", "long x = 1; var a = System.Math.Max(x, 2);")]
    [InlineData("let x: uint = 1\nlet a = Math.Max(x, 2)", "uint x = 1; var a = System.Math.Max(x, 2);")]
    [InlineData("let x: float = 1\nlet a = Math.Max(x, 2)", "float x = 1; var a = System.Math.Max(x, 2);")]
    [InlineData("let a = Math.Abs(-3)", "var a = System.Math.Abs(-3);")]
    [InlineData("let a = Math.Round(2.5)", "var a = System.Math.Round(2.5);")]
    [InlineData("let a = Math.Round(2.5, 1)", "var a = System.Math.Round(2.5, 1);")]
    [InlineData("let a = string.Concat(\"a\", \"b\")", "var a = string.Concat(\"a\", \"b\");")]
    [InlineData("let list = new List<int>()\nlist.Add(1)", "var list = new System.Collections.Generic.List<int>(); list.Add(1);")]
    [InlineData("let s = \"abc\".Substring(1)", "var s = \"abc\".Substring(1);")]
    [InlineData("let s = \"abc\".IndexOf('b')", "var s = \"abc\".IndexOf('b');")]
    [InlineData("let s = \"abc\".IndexOf(\"b\")", "var s = \"abc\".IndexOf(\"b\");")]
    [InlineData("let s = \"a,b\".Split(',')", "var s = \"a,b\".Split(',');")]
    [InlineData("let s = 12.ToString()", "var s = 12.ToString();")]
    [InlineData("let s = 1.5.ToString(\"F2\")", "var s = 1.5.ToString(\"F2\");")]
    [InlineData("let n = int.Parse(\"3\")", "var n = int.Parse(\"3\");")]
    public void ChoosesTheSameOverloadAsCSharp(string nyxel, string csharp)
    {
        var call = BindClean(nyxel).DescendantsAndSelf().OfType<BoundCall>().Last();
        var nyxelChoice = ((MetadataMethodSymbol)call.Method.OriginalDefinition).RoslynSymbol.GetDocumentationCommentId();
        Assert.Equal(RoslynChoice(csharp), nyxelChoice);
    }

    [Fact]
    public void ADecimalLiteralBecomesAFloatOnlyWhenNothingElseApplies()
    {
        // Single(float) is the only overload: 1.5 is a float there (ADR-0013); C# would need 1.5f.
        var call = BindClean("let a = MathF.Sqrt(2.5)").DescendantsAndSelf().OfType<BoundCall>().Single();
        Assert.Equal("float", SymbolDisplay.FormatType(call.Arguments[0].Value.Type));
    }

    private static string? RoslynChoice(string statements)
    {
        var tree = CSharpSyntaxTree.ParseText("class C { void F() { " + statements + " } }");
        var compilation = CSharpCompilation.Create("Probe", [tree], References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Last();
        var method = (IMethodSymbol?)model.GetSymbolInfo(invocation).Symbol;
        Assert.NotNull(method);
        return method.OriginalDefinition.GetDocumentationCommentId();
    }
}
