using Microsoft.CodeAnalysis;
using Nyxel.Compiler.Binding;
using Nyxel.Compiler.Symbols;
using static Nyxel.Compiler.Tests.Binding.BindingTestHelpers;

namespace Nyxel.Compiler.Tests.Binding;

public class BinderTests
{
    [Fact]
    public void HelloWorldHasAnEntryPoint()
    {
        var compilation = Compile("""
            namespace Hello

            import System

            class Program {
                func Main() {
                    Console.WriteLine("Hello, Nyxel")
                }
            }
            """, OutputKind.ConsoleApplication);
        Assert.Empty(compilation.GetDiagnostics());
        Assert.Equal("Main", compilation.Bind().EntryPoint?.Name);
        var call = Body(compilation, "Main").DescendantsAndSelf().OfType<BoundCall>().Single();
        Assert.Equal("Console.WriteLine(value: string)", SymbolDisplay.Format(call.Method));
    }

    // ADR-0007, ADR-0013: a number's type comes from where it is used.
    [Theory]
    [InlineData("let x = 10", "int")]
    [InlineData("let x = 2.5", "double")]
    [InlineData("let x = 3000000000", "uint")]
    [InlineData("let x = 5000000000", "long")]
    [InlineData("let x: float = 3", "float")]
    [InlineData("let x: float = 2.5", "float")]
    [InlineData("let x: byte = 200", "byte")]
    [InlineData("let x = -5", "int")]
    [InlineData("let x = \"a\" + 1", "string")]
    [InlineData("let x = 'c'", "char")]
    [InlineData("let x = 1 < 2", "bool")]
    [InlineData("let x = new List<int>()", "List<int>")]
    [InlineData("let x = new array<string>(3)", "array<string>")]
    [InlineData("let x = new array2d<int>(2, 3)", "array2d<int>")]
    [InlineData("let x = Math.Max(1, 2)", "int")]
    [InlineData("let x = Math.Sqrt(2)", "double")]
    [InlineData("let x = \"abc\".Length", "int")]
    [InlineData("let x = int.MaxValue", "int")]
    [InlineData("let x = 7 as float", "float")]
    [InlineData("let x = $\"a {1} b\"", "string")]
    public void LocalTypes(string statement, string type) => Assert.Equal(type, TypeOf(BindClean(statement), "x"));

    [Theory]
    [InlineData("let x: float = 1 / 2", 0.5f)]
    [InlineData("let x: double = 1 / 2", 0.5)]
    [InlineData("let x = 1 / 2", 0)]
    [InlineData("let x = 7 % 3", 1)]
    [InlineData("let x = -7 / 2", -3)]
    [InlineData("let x: long = 2147483647 + 1", 2147483648L)]
    public void LiteralArithmeticIsFoldedInTheExpectedType(string statement, object value) =>
        Assert.Equal(value, ValueOf(BindClean(statement), "x").ConstantValue);

    [Fact]
    public void IntegerDivisionStaysIntegerNextToAVariable()
    {
        // Wave is an int: 'wave / 2' divides integers, then converts to float (ADR-0013 rule 1).
        var body = BindClean("let wave = 3\nlet speed: float = wave / 2");
        var conversion = Assert.IsType<BoundConversion>(ValueOf(body, "speed"));
        Assert.Equal(ConversionKind.ImplicitNumeric, conversion.ConversionKind);
        var division = Assert.IsType<BoundBinary>(conversion.Operand);
        Assert.Equal("int", SymbolDisplay.FormatType(division.Type));
    }

    [Theory]
    [InlineData("let wave = 3\nlet x = wave * 0.5", "double")]
    [InlineData("let wave = 3\nlet x: float = wave * 0.5", "float")]
    [InlineData("let f: float = 1\nlet x = f * 2", "float")]
    [InlineData("let wave = 3\nlet x: float = 2 + wave * 0.5", "float")]
    public void ADecimalNextToAWholeNumberMakesItFloating(string statements, string type)
    {
        var value = ValueOf(BindClean(statements), "x");
        var operation = value is BoundConversion conversion ? conversion.Operand : value;
        Assert.Equal(type, SymbolDisplay.FormatType(operation.Type));
    }

    [Fact]
    public void FunctionsAndFieldsOfTheType()
    {
        var compilation = Compile("""
            class Counter {
                var count: int = 0
                let step: int

                public init(step: int) {
                    self.step = step
                }

                public func Add(self) -> int {
                    self.count += self.step
                    return self.count
                }

                public func Twice(value: int) -> int = value * 2
            }

            class User {
                func Run() -> int {
                    let counter = new Counter(step = 2)
                    counter.Add()
                    return Counter.Twice(counter.Add())
                }
            }
            """);
        Assert.Empty(compilation.GetDiagnostics());
        var run = Body(compilation, "Run");
        var calls = run.DescendantsAndSelf().OfType<BoundCall>().Select(c => SymbolDisplay.Format(c.Method)).ToList();
        Assert.Equal(["Counter.Add() -> int", "Counter.Twice(value: int) -> int", "Counter.Add() -> int"], calls);
        var creation = run.DescendantsAndSelf().OfType<BoundObjectCreation>().Single();
        Assert.Equal("step", creation.Arguments.Single().Name);
    }

    [Fact]
    public void LoopsOverRangesArraysListsAndStrings()
    {
        var body = BindClean("""
            var total = 0
            for i in 0..<10 {
                total += i
            }
            for i in 1...3 {
                total -= i
            }
            let numbers = new array<int>(3)
            for n in numbers {
                total += n
            }
            let names = new List<string>()
            for name in names {
                total += name.Length
            }
            for c in "abc" {
                total += c
            }
            """);
        var variables = body.DescendantsAndSelf().Select(n => n switch
        {
            BoundForRange range => $"{range.Variable.Name}: {SymbolDisplay.FormatType(range.Variable.Type)}",
            BoundForEach each => $"{each.Variable.Name}: {SymbolDisplay.FormatType(each.Variable.Type)}",
            _ => null,
        }).OfType<string>();
        Assert.Equal(["i: int", "i: int", "n: int", "name: string", "c: char"], variables);
    }

    [Fact]
    public void ListMembersAreSubstituted()
    {
        var body = BindClean("let list = new List<int>()\nlist.Add(1)\nlet first = list[0]\nlet count = list.Count");
        Assert.Equal("List<int>.Add(item: int)", SymbolDisplay.Format(body.DescendantsAndSelf().OfType<BoundCall>().Single().Method));
        Assert.Equal("int", TypeOf(body, "first"));
        Assert.Equal("int", TypeOf(body, "count"));
    }

    [Fact]
    public void SiblingBlocksReuseANameAndALaterDeclarationMayToo()
    {
        // ADR-0036: a local is visible from its declaration to the end of its block.
        BindClean("""
            for i in 0..<2 {
                let a = i
            }
            for i in 0..<2 {
                let a = i
            }
            if true {
                let b = 1
            }
            let b = 2
            """);
    }
}
