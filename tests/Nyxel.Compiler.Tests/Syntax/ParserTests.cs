using Nyxel.Compiler.Syntax;
using static Nyxel.Compiler.Tests.Syntax.SyntaxTestHelpers;

namespace Nyxel.Compiler.Tests.Syntax;

public class ParserTests
{
    private static string Expr(string expression) => Compact(ParseExpression(expression));

    private static string Stmt(string statement) => Compact(ParseStatement(statement));

    // Precedence (ADR-0010, ADR-0014, ADR-0019) ------------------------------------------------------------------

    [Theory]
    [InlineData("a + b * c", "(AddExpression a + (MultiplyExpression b * c))")]
    [InlineData("a - b - c", "(SubtractExpression (SubtractExpression a - b) - c)")]
    [InlineData("a << 1 + b", "(LeftShiftExpression a << (AddExpression 1 + b))")]
    [InlineData("a < b == c > d", "(EqualsExpression (LessThanExpression a < b) == (GreaterThanExpression c > d))")]
    [InlineData("a & b ^ c | d", "(BitwiseOrExpression (ExclusiveOrExpression (BitwiseAndExpression a & b) ^ c) | d)")]
    [InlineData("not a and b or c", "(LogicalOrExpression (LogicalAndExpression (LogicalNotExpression not a) and b) or c)")]
    [InlineData("a or b ?? c", "(CoalesceExpression (LogicalOrExpression a or b) ?? c)")]
    [InlineData("a ?? b ?? c", "(CoalesceExpression a ?? (CoalesceExpression b ?? c))")]
    [InlineData("-x * y", "(MultiplyExpression (UnaryMinusExpression - x) * y)")]
    [InlineData("~x", "(BitwiseNotExpression ~ x)")]
    public void BinaryPrecedenceFollowsCSharp(string expression, string expected) => Assert.Equal(expected, Expr(expression));

    [Theory]
    // 'as' is above * / and below unary operators and member access (ADR-0014).
    [InlineData("self.Killed as float / self.Total", "(DivideExpression (AsExpression (SimpleMemberAccessExpression self . Killed) as float) / (SimpleMemberAccessExpression self . Total))")]
    [InlineData("-x as int", "(AsExpression (UnaryMinusExpression - x) as int)")]
    [InlineData("a * b as float", "(MultiplyExpression a * (AsExpression b as float))")]
    [InlineData("x as int?", "(AsExpression x as (NullableType int ?))")]
    public void AsBindsTighterThanMultiplication(string expression, string expected) => Assert.Equal(expected, Expr(expression));

    [Theory]
    // Ranges are below arithmetic (ADR-0019); shown in a 'for', one of the places a range may stand (ADR-0022).
    [InlineData("0..<self.count - 1", "(RangeExpression 0 ..< (SubtractExpression (SimpleMemberAccessExpression self . count) - 1))")]
    [InlineData("a + 1...b * 2", "(RangeExpression (AddExpression a + 1) ... (MultiplyExpression b * 2))")]
    [InlineData("a << 1..<b", "(RangeExpression (LeftShiftExpression a << 1) ..< b)")]
    public void RangesAreBelowArithmetic(string range, string expected) =>
        Assert.Equal(expected, Compact(((ForStatementSyntax)ParseStatement($"for i in {range} {{\n}}")).Collection));

    [Fact]
    public void RangesAreAboveComparisons()
    {
        // Not a place for a range (ADR-0022), but the tree still shows how it binds.
        var source = InMethod("let x = 0..<n == r");
        Assert.Equal(["NYX1127 3:9"], DiagnosticsOf(source));
        var statement = (LocalDeclarationStatementSyntax)Body(SyntaxTree.Parse(source)).Statements.Single();
        Assert.Equal("(EqualsExpression (RangeExpression 0 ..< n) == r)", Compact(statement.Initializer!.Value));
    }

    [Theory]
    [InlineData("x is Enemy", "(IsExpression x is Enemy)")]
    [InlineData("x is not Enemy", "(IsExpression x is not Enemy)")]
    [InlineData("x is Enemy and y", "(LogicalAndExpression (IsExpression x is Enemy) and y)")]
    [InlineData("x is List<int>", "(IsExpression x is (GenericName List (TypeArgumentList < int >)))")]
    public void IsTakesAType(string expression, string expected) => Assert.Equal(expected, Expr(expression));

    [Theory]
    [InlineData("a >> 2", "(RightShiftExpression a >> 2)")]
    [InlineData("a > > 2", null)]
    public void RightShiftIsComposedFromAdjacentGreaterThans(string expression, string? expected)
    {
        if (expected == null)
        {
            Assert.NotEmpty(DiagnosticsOf(InMethod("let x = " + expression)));
            return;
        }
        Assert.Equal(expected, Expr(expression));
    }

    [Fact]
    public void ShiftAssignmentIsComposed() =>
        Assert.Equal("(AssignmentStatement a >>= 1)", Stmt("a >>= 1"));

    // Generics (ADR-0012: '<' is resolved by C#'s rule) -----------------------------------------------------------

    [Theory]
    [InlineData("World.Spawn<Slime>()", "(InvocationExpression (SimpleMemberAccessExpression World . (GenericName Spawn (TypeArgumentList < Slime >))) (ArgumentList ( )))")]
    [InlineData("Max<int>(a = 3, b = 7)", "(InvocationExpression (GenericName Max (TypeArgumentList < int >)) (ArgumentList ( (Argument (NameEquals a =) 3) , (Argument (NameEquals b =) 7) )))")]
    [InlineData("new Dictionary<string, List<int>>()", "(ObjectCreationExpression new (GenericName Dictionary (TypeArgumentList < string , (GenericName List (TypeArgumentList < int >)) >)) (ArgumentList ( )))")]
    [InlineData("a < b and c > d", "(LogicalAndExpression (LessThanExpression a < b) and (GreaterThanExpression c > d))")]
    [InlineData("F(a < b, c > d)", "(InvocationExpression F (ArgumentList ( (Argument (LessThanExpression a < b)) , (Argument (GreaterThanExpression c > d)) )))")]
    // C#'s known ambiguity, resolved the same way: '(' may follow a type argument list, so this is a generic call.
    [InlineData("F(a < b, c > (d))", "(InvocationExpression F (ArgumentList ( (Argument (InvocationExpression (GenericName a (TypeArgumentList < b , c >)) (ArgumentList ( (Argument d) )))) )))")]
    public void GenericArgumentsVersusLessThan(string expression, string expected) => Assert.Equal(expected, Expr(expression));

    // Postfix, primary --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("self.target?.Name ?? \"nobody\"", "(CoalesceExpression (ConditionalMemberAccessExpression (SimpleMemberAccessExpression self . target) ?. Name) ?? \"nobody\")")]
    [InlineData("self.healths[0]", "(ElementAccessExpression (SimpleMemberAccessExpression self . healths) (BracketedArgumentList [ (Argument 0) ]))")]
    [InlineData("int.TryParse(text, out let n)", "(InvocationExpression (SimpleMemberAccessExpression int . TryParse) (ArgumentList ( (Argument text) , (Argument out (DeclarationExpression let n)) )))")]
    [InlineData("F(out let n: int, out _, ref self.v)", "(InvocationExpression F (ArgumentList ( (Argument out (DeclarationExpression let n (TypeAnnotation : int))) , (Argument out (DiscardExpression _)) , (Argument ref (SimpleMemberAccessExpression self . v)) )))")]
    [InlineData("Damp(velocity = ref self.v)", "(InvocationExpression Damp (ArgumentList ( (Argument (NameEquals velocity =) ref (SimpleMemberAccessExpression self . v)) )))")]
    [InlineData("new Damage.Burn(Amount = 3)", "(ObjectCreationExpression new (QualifiedName Damage . Burn) (ArgumentList ( (Argument (NameEquals Amount =) 3) )))")]
    [InlineData("(a + b).Length()", "(InvocationExpression (SimpleMemberAccessExpression (ParenthesizedExpression ( (AddExpression a + b) )) . Length) (ArgumentList ( )))")]
    [InlineData("await launch self.Load()", "(AwaitExpression await (LaunchExpression launch (InvocationExpression (SimpleMemberAccessExpression self . Load) (ArgumentList ( )))))")]
    public void PostfixAndPrimary(string expression, string expected) => Assert.Equal(expected, Expr(expression));

    [Theory]
    [InlineData("init super(power = power)", "(InitStatement init super (ArgumentList ( (Argument (NameEquals power =) power) )))")]
    [InlineData("init super.Homing(power = 10, target = t)", "(InitStatement init super . Homing (ArgumentList ( (Argument (NameEquals power =) 10) , (Argument (NameEquals target =) t) )))")]
    [InlineData("init self()", "(InitStatement init self (ArgumentList ( )))")]
    [InlineData("init self\n    .Small()", "(InitStatement init self . Small (ArgumentList ( )))")]
    public void InitStatements(string statement, string expected) => Assert.Equal(expected, Stmt(statement));

    [Theory]
    [InlineData("new Spin() with (Speed = 2)", "(ObjectCreationExpression new Spin (ArgumentList ( )) (WithClause with ( (Argument (NameEquals Speed =) 2) )))")]
    [InlineData("new P(fileName = f) with (A = 1, B = c.D)", "(ObjectCreationExpression new P (ArgumentList ( (Argument (NameEquals fileName =) f) )) (WithClause with ( (Argument (NameEquals A =) 1) , (Argument (NameEquals B =) (SimpleMemberAccessExpression c . D)) )))")]
    [InlineData("new Spin() with (\n    Axis = a,\n    Speed = 2)", "(ObjectCreationExpression new Spin (ArgumentList ( )) (WithClause with ( (Argument (NameEquals Axis =) a) , (Argument (NameEquals Speed =) 2) )))")]
    [InlineData("new P()\n    with (A = 1)", "(ObjectCreationExpression new P (ArgumentList ( )) (WithClause with ( (Argument (NameEquals A =) 1) )))")]
    public void WithClauses(string expression, string expected) => Assert.Equal(expected, Expr(expression));

    [Theory]
    [InlineData("if pos == new Vector3(0, 0, 0) {\n}")]
    [InlineData("while self.next != new Node() {\n}")]
    [InlineData("for p in new Path() {\n}")]
    [InlineData("match new Point(1, 2) {\n    case _ -> {}\n}")]
    [InlineData("try {\n} catch e: E when e.Data == new Info() {\n}")]
    [InlineData("if xs.Any(func(x) {\n    return x == new P()\n}) {\n}")]
    public void BraceAfterNewInTheHeadOfABlockIsTheBlock(string statement) => ParseClean(InMethod(statement));

    [Theory]
    [InlineData("$\"HP: {self.hp}\"", "(InterpolatedStringExpression $\" (InterpolatedStringText HP: ) (Interpolation { (SimpleMemberAccessExpression self . hp) }) \")")]
    [InlineData("$\"{a,8:F2}\"", "(InterpolatedStringExpression $\" (Interpolation { a (InterpolationAlignmentClause , 8) (InterpolationFormatClause : F2) }) \")")]
    public void InterpolatedStrings(string expression, string expected) => Assert.Equal(expected, Expr(expression));

    [Theory]
    [InlineData("line.Split(',')", "(InvocationExpression (SimpleMemberAccessExpression line . Split) (ArgumentList ( (Argument ',') )))")]
    [InlineData("text[0] == '#'", "(EqualsExpression (ElementAccessExpression text (BracketedArgumentList [ (Argument 0) ])) == '#')")]
    [InlineData("\"\"\"\\d+\"\"\"", "\"\"\"\\d+\"\"\"")]
    [InlineData("$$\"\"\"{ {{a}} }\"\"\"", "(InterpolatedStringExpression $$\"\"\" (InterpolatedStringText { ) (Interpolation {{ a }}) (InterpolatedStringText  }) \"\"\")")]
    public void CharactersAndRawStrings(string expression, string expected) => Assert.Equal(expected, Expr(expression));

    [Fact]
    public void MultiLineRawStringIsOneExpression() =>
        Assert.Equal(
            "(LocalDeclarationStatement let help (EqualsValueClause = \"\"\"\n    Usage:\n    \"\"\"))",
            Stmt("let help = \"\"\"\n    Usage:\n    \"\"\""));

    [Theory]
    [InlineData("var hits: array<RaycastHit>", "(LocalDeclarationStatement var hits (TypeAnnotation : (ArrayType array < RaycastHit >)))")]
    [InlineData("let rows: array<array<int>?>?", "(LocalDeclarationStatement let rows (TypeAnnotation : (NullableType (ArrayType array < (NullableType (ArrayType array < int >) ?) >) ?)))")]
    [InlineData("let a = new array<int>(16)", "(LocalDeclarationStatement let a (EqualsValueClause = (ObjectCreationExpression new (ArrayType array < int >) (ArgumentList ( (Argument 16) )))))")]
    [InlineData("let a = new array<int>(length = 3)", "(LocalDeclarationStatement let a (EqualsValueClause = (ObjectCreationExpression new (ArrayType array < int >) (ArgumentList ( (Argument (NameEquals length =) 3) )))))")]
    [InlineData("let ok = x is array<int>", "(LocalDeclarationStatement let ok (EqualsValueClause = (IsExpression x is (ArrayType array < int >))))")]
    [InlineData("let m = new Dictionary<string, array<int>>()", "(LocalDeclarationStatement let m (EqualsValueClause = (ObjectCreationExpression new (GenericName Dictionary (TypeArgumentList < string , (ArrayType array < int >) >)) (ArgumentList ( )))))")]
    public void ArrayTypes(string statement, string expected) => Assert.Equal(expected, Stmt(statement));

    // Tuples (ADR-0021) -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("let r: (Min: int, Max: int)? = x", "(LocalDeclarationStatement let r (TypeAnnotation : (NullableType (TupleType ( (TupleElement Min : int) , (TupleElement Max : int) )) ?)) (EqualsValueClause = x))")]
    [InlineData("let all = new List<(Name: string, Score: int)>()", "(LocalDeclarationStatement let all (EqualsValueClause = (ObjectCreationExpression new (GenericName List (TypeArgumentList < (TupleType ( (TupleElement Name : string) , (TupleElement Score : int) )) >)) (ArgumentList ( )))))")]
    [InlineData("let f: func((A: int, B: int)) -> (C: int, D: int) = g", "(LocalDeclarationStatement let f (TypeAnnotation : (FunctionType func ( (TupleType ( (TupleElement A : int) , (TupleElement B : int) )) ) (ReturnTypeClause -> (TupleType ( (TupleElement C : int) , (TupleElement D : int) ))))) (EqualsValueClause = g))")]
    [InlineData("let p = World.Get<(A: int, B: int)>()", "(LocalDeclarationStatement let p (EqualsValueClause = (InvocationExpression (SimpleMemberAccessExpression World . (GenericName Get (TypeArgumentList < (TupleType ( (TupleElement A : int) , (TupleElement B : int) )) >))) (ArgumentList ( )))))")]
    [InlineData("let r = (lo, hi)", "(LocalDeclarationStatement let r (EqualsValueClause = (TupleExpression ( (Argument lo) , (Argument hi) ))))")]
    [InlineData("let r = (Min = lo, Max = hi)", "(LocalDeclarationStatement let r (EqualsValueClause = (TupleExpression ( (Argument (NameEquals Min =) lo) , (Argument (NameEquals Max =) hi) ))))")]
    [InlineData("let s = (a + b) * 2", "(LocalDeclarationStatement let s (EqualsValueClause = (MultiplyExpression (ParenthesizedExpression ( (AddExpression a + b) )) * 2)))")]
    [InlineData("let q = ($\"{a,5}\", F(b, c))", "(LocalDeclarationStatement let q (EqualsValueClause = (TupleExpression ( (Argument (InterpolatedStringExpression $\" (Interpolation { a (InterpolationAlignmentClause , 5) }) \")) , (Argument (InvocationExpression F (ArgumentList ( (Argument b) , (Argument c) )))) ))))")]
    [InlineData("self.history.Add((name, 1))", "(ExpressionStatement (InvocationExpression (SimpleMemberAccessExpression (SimpleMemberAccessExpression self . history) . Add) (ArgumentList ( (Argument (TupleExpression ( (Argument name) , (Argument 1) ))) ))))")]
    [InlineData("let (min, max) = self.Range()", "(DeconstructionDeclarationStatement let (Deconstruction ( min , max )) (EqualsValueClause = (InvocationExpression (SimpleMemberAccessExpression self . Range) (ArgumentList ( )))))")]
    [InlineData("var (_, count) = stats", "(DeconstructionDeclarationStatement var (Deconstruction ( _ , count )) (EqualsValueClause = stats))")]
    [InlineData("for (i, item) in items.Index() { }", "(ForStatement for (Deconstruction ( i , item )) in (InvocationExpression (SimpleMemberAccessExpression items . Index) (ArgumentList ( ))) (Block { }))")]
    [InlineData("for (key, value) in self.prices { }", "(ForStatement for (Deconstruction ( key , value )) in (SimpleMemberAccessExpression self . prices) (Block { }))")]
    [InlineData("(a, b) = (b, a)", "(AssignmentStatement (TupleExpression ( (Argument a) , (Argument b) )) = (TupleExpression ( (Argument b) , (Argument a) )))")]
    public void Tuples(string statement, string expected) => Assert.Equal(expected, Stmt(statement));

    [Fact]
    public void TupleElementsMayGoOnSeparateLines() =>
        Assert.Equal(
            "(LocalDeclarationStatement let t (EqualsValueClause = (TupleExpression ( (Argument (NameEquals Name =) \"a\") , (Argument (NameEquals Score =) 3) ))))",
            Stmt("let t = (\n    Name = \"a\",\n    Score = 3)"));

    // Ranges, slices, collection literals, multidimensional arrays (ADR-0022) ------------------------------------

    [Theory]
    [InlineData("for i in (0..<self.enemies.Count).Reversed() { }", "(ForStatement for i in (InvocationExpression (SimpleMemberAccessExpression (ParenthesizedExpression ( (RangeExpression 0 ..< (SimpleMemberAccessExpression (SimpleMemberAccessExpression self . enemies) . Count)) )) . Reversed) (ArgumentList ( ))) (Block { }))")]
    [InlineData("for x in (0..<width).StepBy(2).Reversed() { }", "(ForStatement for x in (InvocationExpression (SimpleMemberAccessExpression (InvocationExpression (SimpleMemberAccessExpression (ParenthesizedExpression ( (RangeExpression 0 ..< width) )) . StepBy) (ArgumentList ( (Argument 2) ))) . Reversed) (ArgumentList ( ))) (Block { }))")]
    [InlineData("let prefix = name[0..<3]", "(LocalDeclarationStatement let prefix (EqualsValueClause = (ElementAccessExpression name (BracketedArgumentList [ (Argument (RangeExpression 0 ..< 3)) ]))))")]
    [InlineData("let rest = name[1...]", "(LocalDeclarationStatement let rest (EqualsValueClause = (ElementAccessExpression name (BracketedArgumentList [ (Argument (RangeExpression 1 ...)) ]))))")]
    [InlineData("let head = items[..<3]", "(LocalDeclarationStatement let head (EqualsValueClause = (ElementAccessExpression items (BracketedArgumentList [ (Argument (RangeExpression ..< 3)) ]))))")]
    [InlineData("let upTo = items[...2]", "(LocalDeclarationStatement let upTo (EqualsValueClause = (ElementAccessExpression items (BracketedArgumentList [ (Argument (RangeExpression ... 2)) ]))))")]
    [InlineData("let stem = path[..<path.Length - 4]", "(LocalDeclarationStatement let stem (EqualsValueClause = (ElementAccessExpression path (BracketedArgumentList [ (Argument (RangeExpression ..< (SubtractExpression (SimpleMemberAccessExpression path . Length) - 4))) ]))))")]
    public void RangesInForAndSlices(string statement, string expected) => Assert.Equal(expected, Stmt(statement));

    [Theory]
    [InlineData("let xs: List<int> = [1, 2, 3]", "(LocalDeclarationStatement let xs (TypeAnnotation : (GenericName List (TypeArgumentList < int >))) (EqualsValueClause = (CollectionExpression [ 1 , 2 , 3 ])))")]
    [InlineData("var enemies: List<Enemy> = []", "(LocalDeclarationStatement var enemies (TypeAnnotation : (GenericName List (TypeArgumentList < Enemy >))) (EqualsValueClause = (CollectionExpression [ ])))")]
    [InlineData("self.Patrol(points = [self.a, self.b])", "(ExpressionStatement (InvocationExpression (SimpleMemberAccessExpression self . Patrol) (ArgumentList ( (Argument (NameEquals points =) (CollectionExpression [ (SimpleMemberAccessExpression self . a) , (SimpleMemberAccessExpression self . b) ])) ))))")]
    [InlineData("for dir in [Dir.Up, Dir.Down] { }", "(ForStatement for dir in (CollectionExpression [ (SimpleMemberAccessExpression Dir . Up) , (SimpleMemberAccessExpression Dir . Down) ]) (Block { }))")]
    [InlineData("for (dx, dy) in [(0, 1), (1, 0)] { }", "(ForStatement for (Deconstruction ( dx , dy )) in (CollectionExpression [ (TupleExpression ( (Argument 0) , (Argument 1) )) , (TupleExpression ( (Argument 1) , (Argument 0) )) ]) (Block { }))")]
    [InlineData("let nested: List<List<int>> = [[1, 2], [3]]", "(LocalDeclarationStatement let nested (TypeAnnotation : (GenericName List (TypeArgumentList < (GenericName List (TypeArgumentList < int >)) >))) (EqualsValueClause = (CollectionExpression [ (CollectionExpression [ 1 , 2 ]) , (CollectionExpression [ 3 ]) ])))")]
    public void CollectionLiterals(string statement, string expected) => Assert.Equal(expected, Stmt(statement));

    [Fact]
    public void CollectionLiteralMayGoOnSeparateLinesWithATrailingComma() =>
        Assert.Equal(
            "(LocalDeclarationStatement let waves (TypeAnnotation : (GenericName List (TypeArgumentList < int >))) (EqualsValueClause = (CollectionExpression [ 3 , 5 , ])))",
            Stmt("let waves: List<int> = [\n    3,\n    5,\n]"));

    [Fact]
    public void BracketAtTheStartOfALineStartsAStatement()
    {
        // Not an element access of the line before (ADR-0013): a '[' on a new line begins a collection literal.
        var tree = ParseClean(InMethod("let a = b\n[1, 2].Count"));
        Assert.Equal(2, Body(tree).Statements.Count);
    }

    [Theory]
    [InlineData("let grid = new array2d<Tile>(width, height)", "(LocalDeclarationStatement let grid (EqualsValueClause = (ObjectCreationExpression new (ArrayType array2d < Tile >) (ArgumentList ( (Argument width) , (Argument height) )))))")]
    [InlineData("grid[x, y] = Tile.Wall", "(AssignmentStatement (ElementAccessExpression grid (BracketedArgumentList [ (Argument x) , (Argument y) ])) = (SimpleMemberAccessExpression Tile . Wall))")]
    [InlineData("let cube: array3d<float>? = null", "(LocalDeclarationStatement let cube (TypeAnnotation : (NullableType (ArrayType array3d < float >) ?)) (EqualsValueClause = null))")]
    public void MultidimensionalArrays(string statement, string expected) => Assert.Equal(expected, Stmt(statement));

    // Lambdas (ADR-0012, ADR-0016) --------------------------------------------------------------------------------

    [Theory]
    [InlineData("func(e) = e.Hp > 0", "(LambdaExpression func (ParameterList ( (Parameter e) )) (ExpressionBody = (GreaterThanExpression (SimpleMemberAccessExpression e . Hp) > 0)))")]
    [InlineData("func() = done", "(LambdaExpression func (ParameterList ( )) (ExpressionBody = done))")]
    [InlineData("func(e: Enemy) -> float { return 1 }", "(LambdaExpression func (ParameterList ( (Parameter e : Enemy) )) (ReturnTypeClause -> float) (Block { (ReturnStatement return 1) }))")]
    [InlineData("async func(e) { }", "(LambdaExpression async func (ParameterList ( (Parameter e) )) (Block { }))")]
    public void Lambdas(string expression, string expected) => Assert.Equal(expected, Expr(expression));

    [Fact]
    public void LambdaBlockInsideArgumentsHasStatementLines()
    {
        var statement = ParseStatement("self.button.OnClick(func(evt) {\n    self.clicks += 1\n    self.Log()\n})");
        var lambda = statement.DescendantNodesAndSelf().OfType<LambdaExpressionSyntax>().Single();
        Assert.Equal(2, lambda.Body!.Statements.Count);
    }

    // if / match / try / eval (ADR-0025) ---------------------------------------------------------------------------

    [Fact]
    public void IfExpressionWithElseIf() =>
        Assert.Equal(
            "(IfExpression if (SimpleMemberAccessExpression self . a) -> 0 else (IfExpression if b -> 1 else -> 2))",
            Expr("if self.a -> 0 else if b -> 1 else -> 2"));

    [Fact]
    public void IfStatementWithElseIf() =>
        Assert.Equal(
            "(IfStatement if a (Block { (ExpressionStatement (InvocationExpression F (ArgumentList ( )))) }) (ElseClause else (IfStatement if b (Block { }) (ElseClause else (Block { })))))",
            Stmt("if a {\n    F()\n} else if b {\n} else {\n}"));

    [Theory]
    // The last branch runs to the end of the expression, as with C#'s '?:'.
    [InlineData("if c -> a else -> b + 1", "(IfExpression if c -> a else -> (AddExpression b + 1))")]
    [InlineData("if c -> a else -> b ?? d", "(IfExpression if c -> a else -> (CoalesceExpression b ?? d))")]
    [InlineData("(if c -> 2 else -> 1) * n", "(MultiplyExpression (ParenthesizedExpression ( (IfExpression if c -> 2 else -> 1) )) * n)")]
    [InlineData("if a -> (if b -> 1 else -> 2) else -> 3", "(IfExpression if a -> (ParenthesizedExpression ( (IfExpression if b -> 1 else -> 2) )) else -> 3)")]
    // A branch can be a leave instead of a value.
    [InlineData("if c -> a else -> return", "(IfExpression if c -> a else -> (ReturnExpression return))")]
    [InlineData("if c -> return 0 else -> a", "(IfExpression if c -> (ReturnExpression return 0) else -> a)")]
    [InlineData("if c -> a else -> throw new E()", "(IfExpression if c -> a else -> (ThrowExpression throw (ObjectCreationExpression new E (ArgumentList ( )))))")]
    public void IfExpressions(string expression, string expected) => Assert.Equal(expected, Expr(expression));

    [Fact]
    public void ElseCatchAndFinallyAtTheStartOfALineContinueTheValueForm()
    {
        Assert.Equal(
            "(IfExpression if (SimpleMemberAccessExpression self . IsFrozen) -> 0 else (IfExpression if h -> (MultiplyExpression s * 2) else -> s))",
            Expr("if self.IsFrozen -> 0\n        else if h -> s * 2\n        else -> s"));
        Assert.Equal(
            "(TryExpression try -> (InvocationExpression Load (ArgumentList ( ))) (CatchArrowClause catch e (TypeAnnotation : IOException) -> Default) (FinallyClause finally (Block { (AssignmentStatement loading = false) })))",
            Expr("try -> Load()\n        catch e: IOException -> Default\n        finally { loading = false }"));
        Assert.Equal("(LocalDeclarationStatement let x (EqualsValueClause = (IfExpression if c -> a else -> b)))", Stmt("let x = if c ->\n    a\n    else ->\n    b"));
    }

    [Fact]
    public void ValueFormsStartALineInsideArguments() =>
        Assert.Equal(
            "(InvocationExpression Spawn (ArgumentList ( (Argument (IfExpression if boss -> big else -> small)) , (Argument p) )))",
            Expr("Spawn(\n    if boss -> big else -> small,\n    p)"));

    [Fact]
    public void MatchArmsWithPatterns()
    {
        // A statement, so that arms may be blocks (ADR-0025).
        var match = (MatchExpressionSyntax)((ExpressionStatementSyntax)ParseStatement("""
            match hit {
                case Physical(amount) -> self.Hit(amount)
                case Burn(amount, seconds) when seconds > 3 -> {
                    self.Burn()
                }
                case Idle -> 0
                case 0 or 1 -> 1
                case > 0 and < 10 -> 2
                case 10..<50 -> 3
                case not null -> 4
                case is Enemy -> 5
                case "boss" -> 6
                case Element.Fire -> 7
                case _ -> {}
            }
            """)).Expression;
        Assert.Equal(
            [
                "(CasePattern Physical (CaseFieldList ( amount )))",
                "(CasePattern Burn (CaseFieldList ( amount , seconds )))",
                "(CasePattern Idle)",
                "(OrPattern (ConstantPattern 0) or (ConstantPattern 1))",
                "(AndPattern (RelationalPattern > 0) and (RelationalPattern < 10))",
                "(RangePattern 10 ..< 50)",
                "(NotPattern not (ConstantPattern null))",
                "(TypePattern is Enemy)",
                "(ConstantPattern \"boss\")",
                "(ConstantPattern (SimpleMemberAccessExpression Element . Fire))",
                "(DiscardPattern _)",
            ],
            match.Arms.Select(a => Compact(a.Pattern)));
        Assert.NotNull(match.Arms[1].WhenClause);
        Assert.IsType<BlockSyntax>(match.Arms[1].Body);
    }

    [Fact]
    public void TryExpressionWithCatchFilterAndFinally() =>
        Assert.Equal(
            "(TryExpression try -> (InvocationExpression Load (ArgumentList ( ))) (CatchArrowClause catch e (TypeAnnotation : IOException) (WhenClause when (EqualsExpression (SimpleMemberAccessExpression e . HResult) == 32)) -> Default) (CatchArrowClause catch e (TypeAnnotation : Exception) -> (ReturnExpression return 0)) (FinallyClause finally (Block { })))",
            Expr("try -> Load() catch e: IOException when e.HResult == 32 -> Default catch e: Exception -> return 0 finally { }"));

    [Fact]
    public void TryStatement() =>
        Assert.Equal(
            "(TryStatement try (Block { (UsingDeclarationStatement using w (EqualsValueClause = (InvocationExpression Open (ArgumentList ( ))))) }) (CatchClause catch e (TypeAnnotation : IOException) (Block { })))",
            Stmt("try {\n    using w = Open()\n} catch e: IOException {\n}"));

    [Fact]
    public void EvalBlockGivesItsValue()
    {
        Assert.Equal(
            "(EvalExpression eval (Block { (IfStatement if a (Block { (GiveStatement give 0) })) (LocalDeclarationStatement let r (EqualsValueClause = (SubtractExpression h - b))) (GiveStatement give (MultiplyExpression r * 2)) }))",
            Expr("eval {\n    if a {\n        give 0\n    }\n    let r = h - b\n    give r * 2\n}"));
        // 'give' on the right of '??', and the block on the left of '?? return'.
        Assert.Equal(
            "(CoalesceExpression (EvalExpression eval (Block { (LocalDeclarationStatement let t (EqualsValueClause = (CoalesceExpression (InvocationExpression Find (ArgumentList ( ))) ?? (GiveExpression give null)))) (GiveStatement give t) })) ?? (ReturnExpression return))",
            Expr("eval {\n    let t = Find() ?? give null\n    give t\n} ?? return"));
    }

    [Fact]
    public void EvalBlockMayUseLoopsLambdasAndAMatchThatGivesInEveryArm() =>
        ParseClean(InMethod("""
            let found = eval {
                for e in enemies {
                    if e.IsDead {
                        continue
                    }
                    give e
                }
                let f = func(x: int) -> int {
                    return x
                }
                match mode {
                    case Easy -> give null
                    case _ -> throw new E()
                }
            }
            """));

    [Theory]
    // In a match used as a statement an arm may be a leave, a block, an assignment or 'emit' (ADR-0025, ADR-0026); in
    // one used as a value, a leave or an eval.
    [InlineData("match s {\n    case Dead -> return\n    case Idle -> {\n        self.Wander()\n    }\n}", "(ExpressionStatement (MatchExpression match s { (MatchArm case (CasePattern Dead) -> (ReturnExpression return)) (MatchArm case (CasePattern Idle) -> (Block { (ExpressionStatement (InvocationExpression (SimpleMemberAccessExpression self . Wander) (ArgumentList ( )))) })) }))")]
    [InlineData("match s {\n    case Hurt -> self.speed = 0\n    case Dead -> self.hp +=\n        1\n    case _ -> emit self.Changed(s = s)\n}", "(ExpressionStatement (MatchExpression match s { (MatchArm case (CasePattern Hurt) -> (AssignmentStatement (SimpleMemberAccessExpression self . speed) = 0)) (MatchArm case (CasePattern Dead) -> (AssignmentStatement (SimpleMemberAccessExpression self . hp) += 1)) (MatchArm case (DiscardPattern _) -> (EmitStatement emit (InvocationExpression (SimpleMemberAccessExpression self . Changed) (ArgumentList ( (Argument (NameEquals s =) s) ))))) }))")]
    [InlineData("let c = match h {\n    case Burn(a) -> eval {\n        self.Burn()\n        give -a\n    }\n    case _ -> continue\n}", "(LocalDeclarationStatement let c (EqualsValueClause = (MatchExpression match h { (MatchArm case (CasePattern Burn (CaseFieldList ( a ))) -> (EvalExpression eval (Block { (ExpressionStatement (InvocationExpression (SimpleMemberAccessExpression self . Burn) (ArgumentList ( )))) (GiveStatement give (UnaryMinusExpression - a)) }))) (MatchArm case (DiscardPattern _) -> (ContinueExpression continue)) })))")]
    [InlineData("let c = match h {\n    case Heal(a) -> if a > 100 -> 100 else -> a\n}", "(LocalDeclarationStatement let c (EqualsValueClause = (MatchExpression match h { (MatchArm case (CasePattern Heal (CaseFieldList ( a ))) -> (IfExpression if (GreaterThanExpression a > 100) -> 100 else -> a)) })))")]
    public void MatchArms(string statement, string expected) => Assert.Equal(expected, Stmt(statement));

    [Fact]
    public void ElseAtTheStartOfALineInAMatchArmContinuesTheIf()
    {
        // The default arm is 'case _' (ADR-0026), so 'else' can only belong to the 'if' in the arm before.
        var match = (MatchExpressionSyntax)ParseExpression("match x {\n    case 1 -> if b -> 1\n    else -> 2\n    case _ -> 3\n}");
        Assert.Equal(2, match.Arms.Count);
        Assert.Equal("(IfExpression if b -> 1 else -> 2)", Compact((SyntaxNode)match.Arms[0].Body));
    }

    [Fact]
    public void ElseArmIsReportedAndReadAsTheDefaultArm()
    {
        var source = InMethod("let a = match x {\n    case 1 -> 0\n    else -> 1\n}");
        Assert.Equal(["NYX1193 5:5"], DiagnosticsOf(source));
        var match = (MatchExpressionSyntax)((LocalDeclarationStatementSyntax)Body(SyntaxTree.Parse(source)).Statements.Single()).Initializer!.Value;
        var arm = match.Arms[^1];
        Assert.Equal(SyntaxKind.ElseKeyword, arm.Keyword.Kind);
        Assert.True(((DiscardPatternSyntax)arm.Pattern).UnderscoreToken.IsMissing);
    }

    [Theory]
    [InlineData("let (_, max) = F()", "(DeconstructionDeclarationStatement let (Deconstruction ( _ , max )) (EqualsValueClause = (InvocationExpression F (ArgumentList ( )))))")]
    [InlineData("for (_, v) in d {\n}", "(ForStatement for (Deconstruction ( _ , v )) in d (Block { }))")]
    [InlineData("try {\n} catch _: E {\n}", "(TryStatement try (Block { }) (CatchClause catch _ (TypeAnnotation : E) (Block { })))")]
    [InlineData("let a = try -> F() catch _: E -> 0", "(LocalDeclarationStatement let a (EqualsValueClause = (TryExpression try -> (InvocationExpression F (ArgumentList ( ))) (CatchArrowClause catch _ (TypeAnnotation : E) -> 0))))")]
    public void DiscardWhereAValueIsNotNeeded(string statement, string expected) => Assert.Equal(expected, Stmt(statement));

    [Theory]
    [InlineData("self.FindTarget() ?? return", "(CoalesceExpression (InvocationExpression (SimpleMemberAccessExpression self . FindTarget) (ArgumentList ( ))) ?? (ReturnExpression return))")]
    [InlineData("a ?? return 0", "(CoalesceExpression a ?? (ReturnExpression return 0))")]
    [InlineData("a ?? throw new E(\"why\")", "(CoalesceExpression a ?? (ThrowExpression throw (ObjectCreationExpression new E (ArgumentList ( (Argument \"why\") )))))")]
    [InlineData("a ?? continue", "(CoalesceExpression a ?? (ContinueExpression continue))")]
    public void CoalesceWithJumps(string expression, string expected) => Assert.Equal(expected, Expr(expression));

    // Line rules (ADR-0013) ---------------------------------------------------------------------------------------

    [Fact]
    public void LeadingDotContinuesTheLine() =>
        Assert.Equal(
            "(InvocationExpression (SimpleMemberAccessExpression (InvocationExpression (SimpleMemberAccessExpression list . Where) (ArgumentList ( (Argument f) ))) . First) (ArgumentList ( )))",
            Expr("list\n        .Where(f)\n        .First()"));

    [Fact]
    public void LeadingOperatorContinuesTheLine() =>
        Assert.Equal(
            "(LogicalAndExpression (LogicalAndExpression (LessThanOrEqualExpression a <= 0) and (NotEqualsExpression t != null)) and (LogicalNotExpression not d))",
            Expr("a <= 0\n        and t != null\n        and not d"));

    [Fact]
    public void TrailingEqualsContinuesTheLine() =>
        Assert.Equal("(LocalDeclarationStatement let x (EqualsValueClause = (AddExpression a + b)))", Stmt("let x =\n    a + b"));

    [Theory]
    [InlineData("self.total +=\n    bonus * 2", "(AssignmentStatement (SimpleMemberAccessExpression self . total) += (MultiplyExpression bonus * 2))")]
    [InlineData("self.Position =\n    target", "(AssignmentStatement (SimpleMemberAccessExpression self . Position) = target)")]
    [InlineData("slime.Died -=\n    self.OnDied", "(AssignmentStatement (SimpleMemberAccessExpression slime . Died) -= (SimpleMemberAccessExpression self . OnDied))")]
    [InlineData("self.cache ??=\n    self.Build()", "(AssignmentStatement (SimpleMemberAccessExpression self . cache) ??= (InvocationExpression (SimpleMemberAccessExpression self . Build) (ArgumentList ( ))))")]
    public void TrailingAssignmentOperatorContinuesTheLine(string statement, string expected) => Assert.Equal(expected, Stmt(statement));

    [Fact]
    public void LineBreaksInsideBracketsAreIgnored() =>
        Assert.Equal(
            "(InvocationExpression F (ArgumentList ( (Argument (NameEquals a =) 1) , (Argument (NameEquals b =) (AddExpression c + d)) )))",
            Expr("F(\n    a = 1,\n    b = c\n        + d)"));

    [Fact]
    public void NewLineEndsAStatement()
    {
        // '(' starts a new statement; '-' at the start of a line continues the previous one.
        var body = Body(ParseClean(InMethod("let a = b\n(c).Run()\n    - x.Foo()")));
        Assert.Equal(2, body.Statements.Count);
        Assert.Equal(SyntaxKind.SubtractExpression, ((ExpressionStatementSyntax)body.Statements[1]).Expression.Kind);
    }

    [Fact]
    public void ReturnWithoutValueEndsAtTheLineBreak()
    {
        var body = Body(ParseClean(InMethod("if a {\n    return\n}\nself.Next()")));
        var ifStatement = (IfStatementSyntax)body.Statements[0];
        Assert.Null(((ReturnStatementSyntax)ifStatement.Block.Statements[0]).Expression);
        Assert.Equal(2, body.Statements.Count);
    }

    [Fact]
    public void SingleLineBlocksAndAccessors() =>
        ParseClean("class C {\n    property P: int { get }\n    property Q: int {\n        get { return 1 }\n    }\n}\n");

    // Statements --------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("var hp: int = 100", "(LocalDeclarationStatement var hp (TypeAnnotation : int) (EqualsValueClause = 100))")]
    [InlineData("using reader = new R(s)", "(UsingDeclarationStatement using reader (EqualsValueClause = (ObjectCreationExpression new R (ArgumentList ( (Argument s) )))))")]
    [InlineData("using s: FileStream = Open()", "(UsingDeclarationStatement using s (TypeAnnotation : FileStream) (EqualsValueClause = (InvocationExpression Open (ArgumentList ( )))))")]
    [InlineData("self.combo += 1", "(AssignmentStatement (SimpleMemberAccessExpression self . combo) += 1)")]
    [InlineData("slime.Died -= self.OnDied", "(AssignmentStatement (SimpleMemberAccessExpression slime . Died) -= (SimpleMemberAccessExpression self . OnDied))")]
    [InlineData("self.target ??= self.FindTarget()", "(AssignmentStatement (SimpleMemberAccessExpression self . target) ??= (InvocationExpression (SimpleMemberAccessExpression self . FindTarget) (ArgumentList ( ))))")]
    [InlineData("name ??= a ?? b", "(AssignmentStatement name ??= (CoalesceExpression a ?? b))")]
    [InlineData("for i in 0..<n { }", "(ForStatement for i in (RangeExpression 0 ..< n) (Block { }))")]
    [InlineData("while self.hp > 0 { }", "(WhileStatement while (GreaterThanExpression (SimpleMemberAccessExpression self . hp) > 0) (Block { }))")]
    [InlineData("throw e", "(ThrowStatement throw e)")]
    [InlineData("emit self.Died(slime = self)", "(EmitStatement emit (InvocationExpression (SimpleMemberAccessExpression self . Died) (ArgumentList ( (Argument (NameEquals slime =) self) ))))")]
    [InlineData("launch self.Flash()", "(ExpressionStatement (LaunchExpression launch (InvocationExpression (SimpleMemberAccessExpression self . Flash) (ArgumentList ( )))))")]
    [InlineData("break", "(BreakStatement break)")]
    [InlineData("continue", "(ContinueStatement continue)")]
    public void Statements(string statement, string expected) => Assert.Equal(expected, Stmt(statement));

    // Declarations ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("public static var Killed: int = 0", "(FieldDeclaration public static var Killed (TypeAnnotation : int) (EqualsValueClause = 0))")]
    [InlineData("var target: Enemy? = null", "(FieldDeclaration var target (TypeAnnotation : (NullableType Enemy ?)) (EqualsValueClause = null))")]
    [InlineData("var onDeath: func(Enemy) -> bool", "(FieldDeclaration var onDeath (TypeAnnotation : (FunctionType func ( Enemy ) (ReturnTypeClause -> bool))))")]
    [InlineData("var cb: func(int)?", "(FieldDeclaration var cb (TypeAnnotation : (NullableType (FunctionType func ( int )) ?)))")]
    [InlineData("var load: async func(string) -> Level", "(FieldDeclaration var load (TypeAnnotation : (FunctionType async func ( string ) (ReturnTypeClause -> Level))))")]
    [InlineData("public func Apply(var self, damage: Damage) -> int = 0", "(FunctionDeclaration public func Apply (ParameterList ( (SelfParameter var self) , (Parameter damage : Damage) )) (ReturnTypeClause -> int) (ExpressionBody = 0))")]
    [InlineData("func TryLoad(self, key: string, data: out SaveData) -> bool", "(FunctionDeclaration func TryLoad (ParameterList ( (SelfParameter self) , (Parameter key : string) , (Parameter data : out SaveData) )) (ReturnTypeClause -> bool))")]
    [InlineData("func Spawn(self, count: int = 1) { }", "(FunctionDeclaration func Spawn (ParameterList ( (SelfParameter self) , (Parameter count : int (EqualsValueClause = 1)) )) (Block { }))")]
    [InlineData("func Max<T: IComparable<T>>(a: T, b: T) -> T = a", "(FunctionDeclaration func Max (TypeParameterList < (TypeParameter T (TypeParameterConstraintClause : (TypeConstraint (GenericName IComparable (TypeArgumentList < T >))))) >) (ParameterList ( (Parameter a : T) , (Parameter b : T) )) (ReturnTypeClause -> T) (ExpressionBody = a))")]
    [InlineData("public init FromPolar(radius: float) { }", "(InitDeclaration public init FromPolar (ParameterList ( (Parameter radius : float) )) (Block { }))")]
    [InlineData("public event Died(slime: Slime)", "(EventDeclaration public event Died (ParameterList ( (Parameter slime : Slime) )))")]
    [InlineData("func MinMax(values: List<int>) -> (Min: int, Max: int) = (0, 0)", "(FunctionDeclaration func MinMax (ParameterList ( (Parameter values : (GenericName List (TypeArgumentList < int >))) )) (ReturnTypeClause -> (TupleType ( (TupleElement Min : int) , (TupleElement Max : int) ))) (ExpressionBody = (TupleExpression ( (Argument 0) , (Argument 0) ))))")]
    [InlineData("protected override async func OnEnable(self) { }", "(FunctionDeclaration protected override async func OnEnable (ParameterList ( (SelfParameter self) )) (Block { }))")]
    public void Members(string member, string expected) => Assert.Equal(expected, Compact(ParseMember(member)));

    [Fact]
    public void AutoAndComputedProperties()
    {
        Assert.Equal(
            "(PropertyDeclaration public property Score (TypeAnnotation : int) (EqualsValueClause = 0) (AccessorList { (GetAccessorDeclaration get) (SetAccessorDeclaration private set) }))",
            Compact(ParseMember("public property Score: int = 0 {\n    get\n    private set\n}")));
        Assert.Equal(
            "(PropertyDeclaration property Hp (TypeAnnotation : int) (AccessorList { (GetAccessorDeclaration get (Block { (ReturnStatement return x) })) (SetAccessorDeclaration set (SetterParameter ( value )) (Block { (AssignmentStatement x = value) })) }))",
            Compact(ParseMember("property Hp: int {\n    get { return x }\n    set(value) { x = value }\n}")));
        // A getter may have an expression body like a function (ADR-0026).
        Assert.Equal(
            "(PropertyDeclaration public property IsDead (TypeAnnotation : bool) (AccessorList { (GetAccessorDeclaration get (ExpressionBody = (LessThanOrEqualExpression (SimpleMemberAccessExpression self . hp) <= 0))) }))",
            Compact(ParseMember("public property IsDead: bool { get = self.hp <= 0 }")));
        Assert.Equal(
            "(PropertyDeclaration property Ratio (TypeAnnotation : float) (AccessorList { (GetAccessorDeclaration get (ExpressionBody = (DivideExpression x / y))) (SetAccessorDeclaration private set (SetterParameter ( value )) (Block { (AssignmentStatement x = value) })) }))",
            Compact(ParseMember("property Ratio: float {\n    get = x / y\n    private set(value) { x = value }\n}")));
    }

    [Fact]
    public void TypeDeclarations()
    {
        var root = ParseClean("""
            namespace Game.Enemies

            import System
            import System.Collections.Generic

            public class Pool<T: class and IPoolable and new()> : Base, IDisposable {
            }

            public enum Damage {
                case Physical(Amount: int)
                case Burn(Amount: int, Seconds: float)
                case Heal
            }

            extension Vector3 {
                public func Flat(self) -> Vector3 = self
            }

            interface IDamageable {
                func TakeDamage(self, amount: int)
                property Health: int { get }
            }
            """).Root;
        Assert.Equal("(NamespaceDeclaration namespace (QualifiedName Game . Enemies))", Compact(root.Namespace!));
        Assert.Equal(2, root.Imports.Count);
        Assert.Equal(
            [SyntaxKind.ClassDeclaration, SyntaxKind.EnumDeclaration, SyntaxKind.ExtensionDeclaration, SyntaxKind.InterfaceDeclaration],
            root.Members.Select(m => m.Kind));
        var pool = (TypeDeclarationSyntax)root.Members[0];
        Assert.Equal(
            "(TypeParameterList < (TypeParameter T (TypeParameterConstraintClause : (ClassConstraint class) and (TypeConstraint IPoolable) and (ConstructorConstraint new ( )))) >)",
            Compact(pool.TypeParameterList!));
        Assert.Equal("(BaseList : Base , IDisposable)", Compact(pool.BaseList!));
        var damage = (EnumDeclarationSyntax)root.Members[1];
        Assert.Equal(3, damage.Cases.Count);
        Assert.Null(damage.Cases[2].ParameterList);
    }

    // Enums (ADR-0024) ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("enum CookedType : uint {\n    case Mesh = 1\n    case Texture = 2\n}", "(EnumDeclaration enum CookedType (BaseList : uint) { (EnumCaseDeclaration case Mesh (EqualsValueClause = 1)) (EnumCaseDeclaration case Texture (EqualsValueClause = 2)) })")]
    [InlineData("public flags enum Layer {\n    case None = 0\n    case Solid = Layer.Ground | 1 << 2\n}", "(EnumDeclaration public flags enum Layer { (EnumCaseDeclaration case None (EqualsValueClause = 0)) (EnumCaseDeclaration case Solid (EqualsValueClause = (BitwiseOrExpression (SimpleMemberAccessExpression Layer . Ground) | (LeftShiftExpression 1 << 2)))) })")]
    [InlineData("struct enum AiState {\n    case Idle\n    case Chasing(Target: Entity)\n}", "(EnumDeclaration struct enum AiState { (EnumCaseDeclaration case Idle) (EnumCaseDeclaration case Chasing (ParameterList ( (Parameter Target : Entity) ))) })")]
    public void EnumDeclarations(string source, string expected) => Assert.Equal(expected, Compact(ParseClean(source).Root.Members[0]));

    // Members of enums, interfaces of enums, indexers (ADR-0029) ----------------------------------------------------

    [Theory]
    [InlineData("enum Element {\n    case Fire\n\n    public func Weakness(self) -> Element = Element.Fire\n    public property IsHot: bool { get = true }\n    func Parse(text: string) -> Element? = null\n}", "(EnumDeclaration enum Element { (EnumCaseDeclaration case Fire) (FunctionDeclaration public func Weakness (ParameterList ( (SelfParameter self) )) (ReturnTypeClause -> Element) (ExpressionBody = (SimpleMemberAccessExpression Element . Fire))) (PropertyDeclaration public property IsHot (TypeAnnotation : bool) (AccessorList { (GetAccessorDeclaration get (ExpressionBody = true)) })) (FunctionDeclaration func Parse (ParameterList ( (Parameter text : string) )) (ReturnTypeClause -> (NullableType Element ?)) (ExpressionBody = null)) })")]
    [InlineData("enum Damage : IDescribable {\n    case Heal(Amount: int)\n}", "(EnumDeclaration enum Damage (BaseList : IDescribable) { (EnumCaseDeclaration case Heal (ParameterList ( (Parameter Amount : int) ))) })")]
    [InlineData("struct enum Element : IDescribable, IComparable<Element> {\n    case Fire\n}", "(EnumDeclaration struct enum Element (BaseList : IDescribable , (GenericName IComparable (TypeArgumentList < Element >))) { (EnumCaseDeclaration case Fire) })")]
    public void EnumMembersAndInterfaces(string source, string expected) => Assert.Equal(expected, Compact(ParseClean(source).Root.Members[0]));

    [Theory]
    [InlineData("public property self[x: int, y: int]: T {\n    get = x\n    private set(value) { }\n}", "(IndexerDeclaration public property self (BracketedParameterList [ (Parameter x : int) , (Parameter y : int) ]) (TypeAnnotation : T) (AccessorList { (GetAccessorDeclaration get (ExpressionBody = x)) (SetAccessorDeclaration private set (SetterParameter ( value )) (Block { })) }))")]
    [InlineData("property self[index: int]: T { get }", "(IndexerDeclaration property self (BracketedParameterList [ (Parameter index : int) ]) (TypeAnnotation : T) (AccessorList { (GetAccessorDeclaration get) }))")]
    public void Indexers(string member, string expected) => Assert.Equal(expected, Compact(ParseMember(member)));

    [Fact]
    public void IndexersAreUsedWithSelfAndBrackets()
    {
        Assert.Equal("(AssignmentStatement (ElementAccessExpression self (BracketedArgumentList [ (Argument x) , (Argument 0) ])) = (SimpleMemberAccessExpression Tile . Wall))", Stmt("self[x, 0] = Tile.Wall"));
    }

    [Fact]
    public void FlagsIsAnIdentifierExceptBeforeEnum()
    {
        Assert.Equal("(LocalDeclarationStatement let flags (EqualsValueClause = (BitwiseOrExpression a | b)))", Stmt("let flags = a | b"));
        Assert.Equal("(FieldDeclaration var flags (TypeAnnotation : int) (EqualsValueClause = 0))", Compact(ParseMember("var flags: int = 0")));
    }

    [Theory]
    [InlineData("if state is AiState.Idle {\n}", "(IfStatement if (IsExpression state is (QualifiedName AiState . Idle)) (Block { }))")]
    [InlineData("match hit {\n    case Burn(_, seconds) -> seconds\n}", "(ExpressionStatement (MatchExpression match hit { (MatchArm case (CasePattern Burn (CaseFieldList ( _ , seconds ))) -> seconds) }))")]
    public void EnumCasesInConditionsAndPatterns(string statement, string expected) => Assert.Equal(expected, Stmt(statement));

    [Fact]
    public void DocCommentsStayInTheLeadingTriviaOfTheDeclaration()
    {
        var member = ParseMember("    /// Heals.\n    ///\n    /// More.\n    public func Heal(self) { }");
        Assert.Equal(3, member.GetFirstToken().LeadingTrivia.Count(t => t.Kind == SyntaxKind.DocCommentTrivia));
    }

    // Tree invariants ---------------------------------------------------------------------------------------------

    [Fact]
    public void ParentsAndSpansAreConsistent() =>
        AssertParentsAndSpansConsistent(ParseClean(File.ReadAllText(Path.Combine(RepoRoot, "samples", "Enemies", "Spawner.nyxel"))).Root);
}
