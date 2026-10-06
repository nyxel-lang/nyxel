using Nyxel.Compiler.Syntax;
using static Nyxel.Compiler.Tests.Syntax.SyntaxTestHelpers;

namespace Nyxel.Compiler.Tests.Syntax;

/// <summary>
/// Deliberately broken code and the exact diagnostics it gets: id and one-based line:column. Statements are placed
/// by <see cref="SyntaxTestHelpers.InMethod"/>, so their first line is line 3.
/// </summary>
public class ParserErrorTests
{
    private static void AssertStatementErrors(string statements, params string[] expected) =>
        Assert.Equal(expected, DiagnosticsOf(InMethod(statements)));

    private static void AssertFileErrors(string source, params string[] expected) =>
        Assert.Equal(expected, DiagnosticsOf(source));

    // Line rules (ADR-0013, ADR-0019) -----------------------------------------------------------------------------

    [Theory]
    [InlineData("let x = a +\n    b", "NYX1103 3:11")]
    [InlineData("let ok = a and\n    b", "NYX1103 3:12")]
    [InlineData("F(a +\n    b)", "NYX1103 3:5")]
    [InlineData("self.\n    Foo()", "NYX1103 3:5")]
    [InlineData("let t = a ??\n    b", "NYX1103 3:11")]
    // Only '=' and '->' may end a line; compound assignments are not decided and follow the literal rule.
    [InlineData("self.total +=\n    bonus", "NYX1101 3:14")]
    public void OperatorAtTheEndOfALine(string statements, string expected) => AssertStatementErrors(statements, expected);

    [Fact]
    public void OperatorAtTheEndOfALineSaysWhereToMoveIt()
    {
        var diagnostic = SyntaxTree.Parse(InMethod("let x = a +\n    b")).Diagnostics.Single();
        Assert.Equal("'+' can't end a line; move it to the start of the next line.", diagnostic.Message);
    }

    [Fact]
    public void OpenBraceOnTheNextLine() => AssertStatementErrors("if a\n{\n}", "NYX1104 4:1");

    [Fact]
    public void OpenBraceOfAFunctionOnTheNextLine() =>
        AssertFileErrors("class C {\n    func F(self)\n    {\n    }\n}\n", "NYX1104 3:5");

    [Theory]
    [InlineData("if a {\n}\nelse {\n}", "NYX1105 5:1")]
    [InlineData("try {\n}\ncatch e: E {\n}", "NYX1105 5:1")]
    [InlineData("try {\n}\nfinally {\n}", "NYX1105 5:1")]
    public void KeywordOnTheLineAfterTheBrace(string statements, string expected) => AssertStatementErrors(statements, expected);

    [Fact]
    public void TwoStatementsOnOneLine() => AssertStatementErrors("let a = 1 let b = 2", "NYX1106 3:11");

    [Fact]
    public void TwoCasesOnOneLine() => AssertStatementErrors("match x {\n    case 1 -> 0 case 2 -> 1\n}", "NYX1106 4:17");

    [Fact]
    public void LineThatCannotContinue() => AssertStatementErrors("let a = 1\n= 2", "NYX1107 4:1");

    [Fact]
    public void ReturnValueOnTheNextLineIsAnotherStatement()
    {
        // 'return' ends at the line break; the value below is a separate (useless) expression statement.
        var body = Body(SyntaxTree.Parse(InMethod("return\n    5")));
        Assert.Equal(2, body.Statements.Count);
    }

    // Declarations ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("static public var x: int = 0", "NYX1108 2:8")]
    [InlineData("override public func F(self) { }", "NYX1108 2:10")]
    [InlineData("public public var x: int = 0", "NYX1109 2:8")]
    [InlineData("var hp = 100", "NYX1115 2:5")]
    [InlineData("func F(a) { }", "NYX1116 2:8")]
    [InlineData("func F(a: int, self) { }", "NYX1117 2:16")]
    [InlineData("func F(var a: int) { }", "NYX1118 2:8")]
    [InlineData("func F(self: C) { }", "NYX1119 2:12")]
    [InlineData("class D { }", "NYX1114 2:1")]
    [InlineData("let x = 1", "NYX1115 2:5")]
    public void MemberErrors(string member, string expected) => AssertFileErrors("class C {\n" + member + "\n}\n", expected);

    [Fact]
    public void ModifierOrderMessageListsAllModifiers()
    {
        var diagnostic = SyntaxTree.Parse("class C {\noverride async public func F(self) { }\n}\n").Diagnostics.Single();
        Assert.Equal("Write the modifiers in the order 'public override async'.", diagnostic.Message);
    }

    [Theory]
    [InlineData("import System\nnamespace A\n", "NYX1110 2:1")]
    [InlineData("namespace A\nnamespace B\n", "NYX1111 2:1")]
    [InlineData("class C {\n}\nimport System\n", "NYX1112 3:1")]
    [InlineData("func F() {\n}\n", "NYX1113 1:1")]
    [InlineData("using System\n", "NYX1161 1:1")]
    [InlineData("enum E {\n    Fire\n}\n", "NYX1101 2:5")]
    public void FileStructureErrors(string source, string expected) => AssertFileErrors(source, expected);

    [Fact]
    public void FileStructureErrorsKeepTheFileLossless()
    {
        var tree = SyntaxTree.Parse("class C {\n}\nimport System\nnamespace A\n");
        Assert.Equal(2, tree.Diagnostics.Length);
        Assert.Empty(tree.Root.Imports);
        Assert.Null(tree.Root.Namespace);
    }

    // Statements and expressions ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("try {\n}", "NYX1120 3:1")]
    [InlineData("for i in 0..<1..<2 {\n}", "NYX1121 3:15")]
    [InlineData("if a = b {\n}", "NYX1122 3:6")]
    [InlineData("a = b = c", "NYX1122 3:7")]
    [InlineData("match x {\n    case 1 -> return\n}", "NYX1123 4:15")]
    [InlineData("match x {\n    case 1 -> self.a = 2\n}", "NYX1123 4:22")]
    [InlineData("let a: int[] = x", "NYX1124 3:11")]
    [InlineData("let a =\nlet b = 1", "NYX1101 3:8")]
    [InlineData("F(a, b\nlet c = 1", "NYX1101 3:7")]
    [InlineData("let a = 1 # 2", "NYX1001 3:11")]
    [InlineData("let s = \"abc\nlet t = 1", "NYX1002 3:9")]
    public void StatementErrors(string statements, string expected) => AssertStatementErrors(statements, expected);

    // Habits from C# and other languages (ADR-0003: each gets the Nyxel spelling) -------------------------------------

    [Theory]
    [InlineData("if a && b {\n}", "NYX1150 3:6")]
    [InlineData("if a || b {\n}", "NYX1150 3:6")]
    [InlineData("if !a {\n}", "NYX1150 3:4")]
    [InlineData("i++", "NYX1151 3:2")]
    [InlineData("--i", "NYX1151 3:1")]
    [InlineData("match x {\n    case 1 => 2\n}", "NYX1152 4:12")]
    [InlineData("let a = 1;", "NYX1153 3:10")]
    [InlineData("let a = (int)x", "NYX1154 3:9")]
    [InlineData("let e = (Enemy)other", "NYX1154 3:9")]
    [InlineData("F(count: 3)", "NYX1155 3:8")]
    [InlineData("if x is Enemy e {\n}", "NYX1156 3:15")]
    [InlineData("let a = b ? c : d", "NYX1157 3:11")]
    [InlineData("let a = b!", "NYX1158 3:10")]
    [InlineData("using var r = Open()", "NYX1159 3:7")]
    [InlineData("using (var r = Open()) {\n    r.Read()\n}", "NYX1160 3:7")]
    [InlineData("for (var i = 0; i < n; i += 1) {\n}", "NYX1162 3:5")]
    [InlineData("match x {\n    case 1, 2 -> 0\n}", "NYX1163 4:11")]
    public void CSharpHabits(string statements, string expected) => AssertStatementErrors(statements, expected);

    [Theory]
    [InlineData("let a = (int)x", "Nyxel has no '(int)x' cast; write 'x as int'.")]
    [InlineData("if a && b {\n}", "Use 'and' instead of '&&'.")]
    [InlineData("i++", "Nyxel has no '++'; write '+= 1'.")]
    [InlineData("using var r = Open()", "Write 'using r = ...' without 'var'.")]
    [InlineData("F(count: 3)", "Named arguments use '=': write 'count = ...'.")]
    public void CSharpHabitMessagesShowTheNyxelSpelling(string statements, string message) =>
        Assert.Equal(message, SyntaxTree.Parse(InMethod(statements)).Diagnostics.Single().Message);

    [Fact]
    public void RecoveryKeepsParsingAfterAnError()
    {
        // One error per broken line; the good statements around them still parse.
        var source = InMethod("let a = (int)x\nlet b = 2\ni++\nself.Run()");
        var tree = SyntaxTree.Parse(source);
        Assert.Equal(["NYX1154 3:9", "NYX1151 5:2"], tree.Diagnostics.Select(Format));
        Assert.Equal(4, Body(tree).Statements.Count);
        Assert.Equal(source, tree.Root.ToFullString());
    }

    [Fact]
    public void UnclosedBracesAtTheEndOfTheFile()
    {
        var diagnostics = DiagnosticsOf("class C {\n    func F(self) {\n        let a = 1\n");
        Assert.Equal(["NYX1101 3:18"], diagnostics);
    }
}
