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

    // Enums (ADR-0024) ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("enum E : float {\n    case A\n}\n", "NYX1175 1:10")]
    [InlineData("enum E : byte, int {\n    case A\n}\n", "NYX1175 1:16")]
    [InlineData("enum E : IFoo {\n    case A\n}\n", "NYX1175 1:10")]
    [InlineData("enum E : int {\n    case A(X: int)\n}\n", "NYX1176 1:10")]
    [InlineData("enum E {\n    case A(X: int) = 1\n    case B = 2\n}\n", "NYX1176 2:20", "NYX1176 3:12")]
    [InlineData("flags enum E {\n    case A(X: int)\n}\n", "NYX1176 1:1")]
    [InlineData("enum E {\n    case A = 1\n    case B\n    case C\n}\n", "NYX1177 3:10", "NYX1177 4:10")]
    [InlineData("flags enum E {\n    case A\n    case B = 2\n}\n", "NYX1178 2:10")]
    [InlineData("struct enum E {\n    case A\n}\n", "NYX1179 1:1")]
    [InlineData("[Flags]\nenum E {\n    case A = 1\n}\n", "NYX1181 1:1")]
    [InlineData("public flags\nenum E {\n    case A = 1\n}\n", "NYX1101 1:8")]
    public void EnumErrors(string source, params string[] expected) => AssertFileErrors(source, expected);

    [Fact]
    public void EnumCaseValueMessageNamesTheCase()
    {
        var diagnostic = SyntaxTree.Parse("enum E {\n    case A = 1\n    case B\n}\n").Diagnostics.Single();
        Assert.Equal("Give 'B' a value too; either every case of an enum has a value or none does.", diagnostic.Message);
    }

    // Statements and expressions ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("try {\n}", "NYX1120 3:1")]
    [InlineData("for i in 0..<1..<2 {\n}", "NYX1121 3:15")]
    [InlineData("if a = b {\n}", "NYX1122 3:6")]
    [InlineData("a = b = c", "NYX1122 3:7")]
    [InlineData("match x {\n    case 1 -> return\n}", "NYX1123 4:15")]
    [InlineData("match x {\n    case 1 -> self.a = 2\n}", "NYX1123 4:22")]
    [InlineData("let a =\nlet b = 1", "NYX1101 3:8")]
    [InlineData("F(a, b\nlet c = 1", "NYX1101 3:7")]
    [InlineData("let a = 1 # 2", "NYX1001 3:11")]
    [InlineData("let s = \"abc\nlet t = 1", "NYX1002 3:9")]
    public void StatementErrors(string statements, string expected) => AssertStatementErrors(statements, expected);

    // Tuples (ADR-0021) -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("let a: (int, int) = x", "NYX1124 3:9")]
    [InlineData("let a: List<(int, string)> = x", "NYX1124 3:14")]
    [InlineData("let a: (A: int) = x", "NYX1125 3:8")]
    [InlineData("let (a) = x", "NYX1125 3:5")]
    [InlineData("for (item) in items {\n}", "NYX1125 3:5")]
    [InlineData("let ((a, b), c) = x", "NYX1126 3:6")]
    [InlineData("let (a: int, b) = x", "NYX1126 3:7")]
    [InlineData("let x = (a = b)", "NYX1122 3:12")]
    [InlineData("for (x in xs) {\n}", "NYX1162 3:5")]
    public void TupleErrors(string statements, string expected) => AssertStatementErrors(statements, expected);

    // Ranges and collection literals (ADR-0022) ---------------------------------------------------------------------

    [Theory]
    [InlineData("let r = 0..<10", "NYX1127 3:9")]
    [InlineData("self.Use(1...3)", "NYX1127 3:10")]
    [InlineData("let r = (0..<n).Reversed()", "NYX1127 3:10")]
    [InlineData("let a: List<int> = [..<3]", "NYX1127 3:21")]
    [InlineData("for i in (0..<n).Reverse() {\n}", "NYX1128 3:18")]
    [InlineData("for i in 2... {\n}", "NYX1129 3:11")]
    [InlineData("match x {\n    case ..<10 -> 1\n    else -> 2\n}", "NYX1129 4:10")]
    [InlineData("let a = items[...]", "NYX1129 3:15")]
    [InlineData("let a = items[1..<]", "NYX1170 3:16")]
    [InlineData("for i in a..<b..<c {\n}", "NYX1121 3:15")]
    public void RangeErrors(string statements, string expected) => AssertStatementErrors(statements, expected);

    [Fact]
    public void CollectionLiteralKeepsItsElementsAfterAnError()
    {
        // The spread and the dictionary values go into trivia, reported once per literal.
        var tree = SyntaxTree.Parse(InMethod("let a: List<int> = [..b, ..c]\nlet d: Dictionary<string, int> = [\"x\": 1, \"y\": 2]"));
        Assert.Equal(["NYX1172 3:21", "NYX1173 4:38"], tree.Diagnostics.Select(Format));
        var literals = Body(tree).Statements.Cast<LocalDeclarationStatementSyntax>().Select(s => (CollectionExpressionSyntax)s.Initializer!.Value);
        Assert.Equal(["b, c", "\"x\", \"y\""], literals.Select(l => string.Join(", ", l.Elements.Select(e => e.ToString()))));
        AssertSeparatedListsAlternate(tree.Root);
    }

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
    [InlineData("let a: int[] = x", "NYX1164 3:11")]
    [InlineData("let a: Enemy?[]? = x", "NYX1164 3:14")]
    [InlineData("let a = new Enemy[10]", "NYX1165 3:9")]
    [InlineData("{\n    let a = 1\n}", "NYX1166 3:1")]
    [InlineData("let a = new Enemy() { Hp = 3 }", "NYX1167 3:21")]
    [InlineData("let a = new List<int> { 1, 2 }", "NYX1167 3:23")]
    [InlineData("let a = new int[] { 1, 2 }", "NYX1164 3:16", "NYX1167 3:19")]
    [InlineData("let a: (int Min, int Max) = x", "NYX1168 3:9", "NYX1168 3:18")]
    [InlineData("let r = (Min: 1, Max: 2)", "NYX1155 3:13", "NYX1155 3:21")]
    [InlineData("outer: for row in rows {\n}", "NYX1169 3:1")]
    [InlineData("for row in rows {\n    break outer\n}", "NYX1169 4:11")]
    [InlineData("while a {\n    continue outer\n}", "NYX1169 4:14")]
    [InlineData("for i in 0..10 {\n}", "NYX1013 3:11")]
    [InlineData("match x {\n    case 1..5 -> 0\n    else -> 1\n}", "NYX1013 4:11")]
    [InlineData("let a = items[..3]", "NYX1013 3:15")]
    [InlineData("let a = items[1..]", "NYX1170 3:16")]
    [InlineData("let a = items[^1]", "NYX1171 3:15")]
    [InlineData("let a = path[..<^4]", "NYX1171 3:17")]
    [InlineData("let a: List<int> = [...b]", "NYX1172 3:21")]
    [InlineData("let d: Dictionary<string, int> = [\"a\" = 1]", "NYX1173 3:39")]
    [InlineData("let a: int[,] = x", "NYX1164 3:11")]
    [InlineData("let a = new Tile[w, h]", "NYX1165 3:9")]
    [InlineData("let a: int[,,,] = x", "NYX1174 3:11")]
    [InlineData("let a = new int[1, 2, 3, 4]", "NYX1174 3:9")]
    [InlineData("if hit is Damage.Burn(var a, var s) {\n}", "NYX1180 3:22")]
    public void CSharpHabits(string statements, params string[] expected) => AssertStatementErrors(statements, expected);

    [Theory]
    [InlineData("let a = (int)x", "Nyxel has no '(int)x' cast; write 'x as int'.")]
    [InlineData("if a && b {\n}", "Use 'and' instead of '&&'.")]
    [InlineData("i++", "Nyxel has no '++'; write '+= 1'.")]
    [InlineData("using var r = Open()", "Write 'using r = ...' without 'var'.")]
    [InlineData("F(count: 3)", "Named arguments and tuple elements use '=': write 'count = ...'.")]
    [InlineData("let a: (Min: int, int Max) = x", "Write 'Max: int' instead of 'int Max'.")]
    [InlineData("let a: (int, int) = x", "Give each tuple element a name, as in '(Min: int, Max: int)'.")]
    [InlineData("for row in rows {\n    break outer\n}", "Nyxel has no loop labels; move the loops into a function of their own and leave them with 'return'.")]
    [InlineData("let a: Enemy?[] = x", "Write 'array<Enemy?>' instead of 'Enemy?[]'.")]
    [InlineData("let a = new RaycastHit[self.count * 2]", "Write 'new array<RaycastHit>(self.count * 2)' instead of 'new RaycastHit[self.count * 2]'.")]
    [InlineData("let a: int[,] = x", "Write 'array2d<int>' instead of 'int[,]'.")]
    [InlineData("let a = new Tile[w, h]", "Write 'new array2d<Tile>(w, h)' instead of 'new Tile[w, h]'.")]
    [InlineData("let a = items[1..]", "Write '1...' to slice to the end.")]
    [InlineData("let a = items[^1]", "Nyxel doesn't count from the end with '^'; write 'items[items.Count - 1]' ('Length' for arrays and strings).")]
    [InlineData("for i in (0..<n).Reverse() {\n}", "'Reverse' can't be used on a range; in 'for', a range can be followed by 'Reversed()' and 'StepBy(n)'.")]
    [InlineData("let r = 0..<10", "A range is not a value; write it in 'for', in 'case', or in a slice such as 'items[1..<3]'.")]
    [InlineData("if hit is Damage.Burn(var a, var s) {\n}", "'is' only checks which case a value is; take out the data with 'match', as in 'case Burn(amount, seconds) -> ...'.")]
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
    public void MissingCommaKeepsTheListAlternating()
    {
        var tree = SyntaxTree.Parse(InMethod("F(a b, c)"));
        Assert.Single(tree.Diagnostics);
        var call = (InvocationExpressionSyntax)((ExpressionStatementSyntax)Body(tree).Statements.Single()).Expression;
        Assert.Equal(["a", "b", "c"], call.ArgumentList.Arguments.Select(a => a.ToString()));
        AssertSeparatedListsAlternate(tree.Root);
    }

    [Fact]
    public void CSharpArrayTypesAreReadAsArrayTypes()
    {
        var tree = SyntaxTree.Parse(InMethod("let a: int[] = new Enemy[10]"));
        var statement = (LocalDeclarationStatementSyntax)Body(tree).Statements.Single();
        Assert.Equal("(ArrayType <missing> <missing> int <missing>)", Compact(statement.TypeAnnotation!.Type));
        var creation = (ObjectCreationExpressionSyntax)statement.Initializer!.Value;
        Assert.Equal(SyntaxKind.ArrayType, creation.Type.Kind);
        Assert.Equal(SyntaxKind.BracketedArgumentList, creation.ArgumentList.Kind);
        Assert.Equal(InMethod("let a: int[] = new Enemy[10]"), tree.Root.ToFullString());
    }

    [Fact]
    public void CSharpMultidimensionalArraysAreReadAsArray2D()
    {
        var source = InMethod("let a: int[,] = new Tile[w, h]");
        var tree = SyntaxTree.Parse(source);
        var statement = (LocalDeclarationStatementSyntax)Body(tree).Statements.Single();
        Assert.Equal(2, ((ArrayTypeSyntax)statement.TypeAnnotation!.Type).Rank);
        Assert.Equal(2, ((ArrayTypeSyntax)((ObjectCreationExpressionSyntax)statement.Initializer!.Value).Type).Rank);
        Assert.Equal(source, tree.Root.ToFullString());
    }

    [Fact]
    public void LabeledLoopIsStillReadAsALoop()
    {
        var tree = SyntaxTree.Parse(InMethod("outer: for row in rows {\n    for cell in row {\n        break outer\n    }\n}"));
        Assert.Equal(["NYX1169 3:1", "NYX1169 5:15"], tree.Diagnostics.Select(Format));
        var loop = Assert.IsType<ForStatementSyntax>(Body(tree).Statements.Single());
        Assert.Equal("row", loop.Identifier!.Text);
    }

    [Fact]
    public void CSharpTupleElementsAreReadWithoutNames()
    {
        var tree = SyntaxTree.Parse(InMethod("let a: (int Min, int Max) = x"));
        var statement = (LocalDeclarationStatementSyntax)Body(tree).Statements.Single();
        Assert.Equal("(TupleType ( (TupleElement <missing> <missing> int) , (TupleElement <missing> <missing> int) ))", Compact(statement.TypeAnnotation!.Type));
        Assert.Equal(InMethod("let a: (int Min, int Max) = x"), tree.Root.ToFullString());
    }

    [Fact]
    public void UnclosedBracesAtTheEndOfTheFile()
    {
        var diagnostics = DiagnosticsOf("class C {\n    func F(self) {\n        let a = 1\n");
        Assert.Equal(["NYX1101 3:18"], diagnostics);
    }
}
