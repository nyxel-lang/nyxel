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
    // Ranges are below arithmetic and above comparisons (ADR-0019).
    [InlineData("0..<self.count - 1", "(RangeExpression 0 ..< (SubtractExpression (SimpleMemberAccessExpression self . count) - 1))")]
    [InlineData("a + 1...b * 2", "(RangeExpression (AddExpression a + 1) ... (MultiplyExpression b * 2))")]
    [InlineData("a << 1..<b", "(RangeExpression (LeftShiftExpression a << 1) ..< b)")]
    [InlineData("0..<n == r", "(EqualsExpression (RangeExpression 0 ..< n) == r)")]
    public void RangesAreBelowArithmetic(string expression, string expected) => Assert.Equal(expected, Expr(expression));

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
    [InlineData("F(out let n: int, out _, ref self.v)", "(InvocationExpression F (ArgumentList ( (Argument out (DeclarationExpression let n (TypeAnnotation : int))) , (Argument out _) , (Argument ref (SimpleMemberAccessExpression self . v)) )))")]
    [InlineData("Damp(velocity = ref self.v)", "(InvocationExpression Damp (ArgumentList ( (Argument (NameEquals velocity =) ref (SimpleMemberAccessExpression self . v)) )))")]
    [InlineData("new Damage.Burn(amount = 3)", "(ObjectCreationExpression new (QualifiedName Damage . Burn) (ArgumentList ( (Argument (NameEquals amount =) 3) )))")]
    [InlineData("(a + b).Length()", "(InvocationExpression (SimpleMemberAccessExpression (ParenthesizedExpression ( (AddExpression a + b) )) . Length) (ArgumentList ( )))")]
    [InlineData("await launch self.Load()", "(AwaitExpression await (LaunchExpression launch (InvocationExpression (SimpleMemberAccessExpression self . Load) (ArgumentList ( )))))")]
    public void PostfixAndPrimary(string expression, string expected) => Assert.Equal(expected, Expr(expression));

    [Fact]
    public void SuperInitAndSelfInitUseTheInitKeywordAsAMemberName()
    {
        Assert.Equal(
            "(ExpressionStatement (InvocationExpression (SimpleMemberAccessExpression super . init) (ArgumentList ( (Argument (NameEquals power =) power) ))))",
            Stmt("super.init(power = power)"));
        Assert.Equal(
            "(ExpressionStatement (InvocationExpression (SimpleMemberAccessExpression self . init) (ArgumentList ( ))))",
            Stmt("self.init()"));
    }

    [Theory]
    [InlineData("$\"HP: {self.hp}\"", "(InterpolatedStringExpression $\" (InterpolatedStringText HP: ) (Interpolation { (SimpleMemberAccessExpression self . hp) }) \")")]
    [InlineData("$\"{a,8:F2}\"", "(InterpolatedStringExpression $\" (Interpolation { a (InterpolationAlignmentClause , 8) (InterpolationFormatClause : F2) }) \")")]
    public void InterpolatedStrings(string expression, string expected) => Assert.Equal(expected, Expr(expression));

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

    // if / match / try as expressions -----------------------------------------------------------------------------

    [Fact]
    public void IfExpressionWithElseIf() =>
        Assert.Equal(
            "(IfExpression if (SimpleMemberAccessExpression self . a) (Block { (ExpressionStatement 0) }) (ElseClause else (IfExpression if b (Block { (ExpressionStatement 1) }) (ElseClause else (Block { (ExpressionStatement 2) })))))",
            Expr("if self.a { 0 } else if b { 1 } else { 2 }"));

    [Fact]
    public void MatchArmsWithPatterns()
    {
        var match = (MatchExpressionSyntax)ParseExpression("""
            match hit {
                    case Physical(amount) -> -amount
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
                    else -> {}
                }
            """);
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
            ],
            match.Arms.Where(a => a.Pattern != null).Select(a => Compact(a.Pattern!)));
        Assert.NotNull(match.Arms[1].WhenClause);
        Assert.IsType<BlockSyntax>(match.Arms[1].Body);
        Assert.Equal(SyntaxKind.ElseKeyword, match.Arms[^1].Keyword.Kind);
    }

    [Fact]
    public void TryExpressionWithCatchFilterAndFinally() =>
        Assert.Equal(
            "(TryExpression try (Block { (ExpressionStatement (InvocationExpression Load (ArgumentList ( )))) }) (CatchClause catch e (TypeAnnotation : IOException) (WhenClause when (EqualsExpression (SimpleMemberAccessExpression e . HResult) == 32)) (Block { (ExpressionStatement Default) })) (FinallyClause finally (Block { })))",
            Expr("try { Load() } catch e: IOException when e.HResult == 32 { Default } finally { }"));

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
        var ifStatement = (IfExpressionSyntax)((ExpressionStatementSyntax)body.Statements[0]).Expression;
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
                case Physical(amount: int)
                case Burn(amount: int, seconds: float)
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

    [Fact]
    public void DocCommentsStayInTheLeadingTriviaOfTheDeclaration()
    {
        var member = ParseMember("    /// Heals.\n    ///\n    /// More.\n    public func Heal(self) { }");
        Assert.Equal(3, member.GetFirstToken().LeadingTrivia.Count(t => t.Kind == SyntaxKind.DocCommentTrivia));
    }

    // Tree invariants ---------------------------------------------------------------------------------------------

    [Fact]
    public void ParentsAndSpansAreConsistent()
    {
        var tree = ParseClean(File.ReadAllText(Path.Combine(RepoRoot, "samples", "Enemies", "Spawner.nyxel")));
        foreach (var node in tree.Root.DescendantNodesAndSelf().Where(n => n is not SyntaxToken))
        {
            var previousEnd = node.FullSpan.Start;
            foreach (var child in node.GetChildren())
            {
                Assert.Same(node, child.Parent);
                Assert.Equal(previousEnd, child.FullSpan.Start);
                previousEnd = child.FullSpan.End;
            }
            Assert.Equal(node.FullSpan.End, previousEnd);
        }
    }
}
