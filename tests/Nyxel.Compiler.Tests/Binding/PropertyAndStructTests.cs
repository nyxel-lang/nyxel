using static Nyxel.Compiler.Tests.Binding.BindingTestHelpers;

namespace Nyxel.Compiler.Tests.Binding;

/// <summary>Properties (ADR-0011, ADR-0044) and structs: what may change a struct value (ADR-0008, ADR-0032).</summary>
public class PropertyAndStructTests
{
    [Theory]
    // The accessors of a property all have bodies or none.
    [InlineData("class C {\n    var x: int = 0\n    property P: int {\n        get = self.x\n        set\n    }\n}", "NYX2070 3:14")]
    // Only an auto property stores an initial value.
    [InlineData("class C {\n    property P: int = 1 { get = 2 }\n}", "NYX2072 2:21")]
    // A property must be readable (ADR-0044).
    [InlineData("class C {\n    property P: int = 0 { set }\n}", "NYX2071 2:14")]
    [InlineData("class C {\n    var x: int = 0\n    property P: int {\n        set(v) { self.x = v }\n    }\n}", "NYX2071 3:14")]
    // set(value) names the new value when the setter has a body, and only then.
    [InlineData("class C {\n    property P: int {\n        get = 0\n        set { }\n    }\n}", "NYX2073 4:9")]
    [InlineData("class C {\n    property P: int = 0 {\n        get\n        set(v)\n    }\n}", "NYX2074 4:12")]
    [InlineData("class C {\n    property P: int = 0 {\n        get\n        get\n    }\n}", "NYX2075 4:9")]
    // A visibility on an accessor, as in C#.
    [InlineData("class C {\n    property P: int = 0 {\n        get\n        private set\n    }\n}", "NYX2076 4:9")]
    [InlineData("class C {\n    public property P: int = 0 {\n        private get\n        private set\n    }\n}", "NYX2076 4:9")]
    [InlineData("class C {\n    public property P: int = 0 {\n        private get\n    }\n}", "NYX2076 3:9")]
    [InlineData("class C {\n    public property P: int = 0 {\n        get\n        static set\n    }\n}", "NYX2076 4:9")]
    [InlineData("class C {\n    public property P: int = 0 {\n        get\n        protected set\n    }\n}", null)]
    // An auto property's value works like a field's (ADR-0044).
    [InlineData("class C {\n    public property P: int { get }\n}", "NYX2049 2:21")]
    [InlineData("class C {\n    public property P: int { get }\n    init() {\n    }\n}", "NYX2050 3:5")]
    [InlineData("class C {\n    public property P: int { get }\n    init(p: int) {\n        self.P = p\n    }\n}", null)]
    [InlineData("class C {\n    public property P: int = 0 { get }\n    func F(self) {\n        self.P = 1\n    }\n}", "NYX2029 4:9")]
    [InlineData("class C {\n    public property P: int = 0 {\n        get\n        private set\n    }\n}\nclass D {\n    static func F() {\n        new C().P = 1\n    }\n}", "NYX2029 9:9")]
    [InlineData("class C {\n    public property P: int = 0 {\n        private get\n        set\n    }\n}\nclass D {\n    static func F() -> int {\n        return new C().P\n    }\n}", "NYX2030 9:16")]
    [InlineData("class C {\n    property P: int {\n        get {\n        }\n    }\n}", "NYX2034 3:9")]
    [InlineData("class C {\n    public static property Count: int = 0 {\n        get\n        private set\n    }\n    static func F() {\n        C.Count += 1\n    }\n}", null)]
    // A struct can't be inherited, can't contain itself; 'var self' is for structs only (ADR-0008).
    [InlineData("struct S {\n    protected var x: int = 0\n}", "NYX2077 2:5")]
    [InlineData("struct S {\n    public property P: int = 0 {\n        get\n        protected set\n    }\n}", "NYX2077 4:9")]
    [InlineData("class C {\n    func F(var self) {\n    }\n}", "NYX2078 2:12")]
    [InlineData("struct S {\n    var next: S? = null\n}", "NYX2079 2:9")]
    [InlineData("struct S {\n    var x: int\n}", "NYX2049 2:9")]
    // A struct that declares an init is made through it: new S() would skip it (ADR-0013).
    [InlineData("struct S {\n    var x: int\n    init(x: int) {\n        self.x = x\n    }\n}\nclass C {\n    static func F() {\n        let s = new S()\n    }\n}", "NYX2013 9:17")]
    [InlineData("struct S {\n    var x: int = 0\n}\nclass C {\n    static func F() {\n        let s = new S()\n    }\n}", null)]
    public void Declarations(string source, string? expected) =>
        Assert.Equal(expected is null ? [] : [expected], DiagnosticsOf(source));

    [Fact]
    public void StructsThatContainEachOtherAreBothReported() =>
        Assert.Equal(["NYX2079 2:9", "NYX2079 5:9"],
            DiagnosticsOf("struct A {\n    var b: B = new B()\n}\nstruct B {\n    var a: A = new A()\n}"));

    private const string StructSource = """
        import System
        import System.Collections.Generic
        import System.Numerics

        struct S {
            public var x: int = 0
            public property X: int {
                get = self.x
                set(v) { self.x = v }
            }
            public func Bump(var self) {
                self.x += 1
            }
            public func Read(self) -> int = self.x
        }

        class C {
            let fixed: S = new S()
            var free: S = new S()
            let list: List<S> = new List<S>()
            public property Free: S {
                get = self.free
                set(v) { self.free = v }
            }

            func F(self, p: S) {

        """;

    /// <summary>Statements in <c>func F(self, p: S)</c> of a class, starting on line 27, with the struct <c>S</c> declared.</summary>
    private static string[] InStructFunction(string statements) =>
        DiagnosticsOf(StructSource + statements + "\n    }\n}\n");

    [Theory]
    // Storage that can change (ADR-0032 decision 1).
    [InlineData("var s = new S()\ns.x = 1\ns.X = 2\ns.Bump()\ns.x += 1")]
    [InlineData("self.free.x = 1\nself.free.X = 2\nself.free.Bump()")]
    [InlineData("let arr = new array<Vector3>(2)\narr[0].X = 1")]
    // Reading never changes: self functions and getters work on any value.
    [InlineData("let s = new S()\nlet a = s.Read() + s.X + self.list[0].Read() + p.X + self.Free.X")]
    // A C# struct method that isn't readonly, with its value used (ADR-0032 decision 3).
    [InlineData("let e = self.list.GetEnumerator()\nlet more = e.MoveNext()")]
    [InlineData("var e = self.list.GetEnumerator()\ne.MoveNext()")]
    public void ChangesWhereTheStructCanChange(string statements) =>
        Assert.Empty(InStructFunction(statements));

    [Theory]
    // Read-only storage: let, parameters, let fields, 'for' variables.
    [InlineData("let s = new S()\ns.x = 1", "NYX2081 28:1")]
    [InlineData("let s = new S()\ns.X = 1", "NYX2081 28:1")]
    [InlineData("let s = new S()\ns.Bump()", "NYX2081 28:1")]
    [InlineData("let s = new S()\ns.x += 1", "NYX2081 28:1")]
    [InlineData("p.x = 1", "NYX2081 27:1")]
    [InlineData("self.fixed.x = 1", "NYX2081 27:1")]
    [InlineData("self.fixed.Bump()", "NYX2081 27:1")]
    [InlineData("for s in self.list {\n    s.Bump()\n}", "NYX2081 28:5")]
    // Copies: properties, ordinary indexers.
    [InlineData("self.list[0].x = 1", "NYX2080 27:1")]
    [InlineData("self.list[0].X = 1", "NYX2080 27:1")]
    [InlineData("self.list[0].Bump()", "NYX2080 27:1")]
    [InlineData("self.Free.x = 1", "NYX2080 27:1")]
    // A checked S? is read as the S inside the Nullable<S>, a copy.
    [InlineData("var n: S? = new S()\nif n != null {\n    n.x = 1\n}", "NYX2080 29:5")]
    [InlineData("var n: S? = new S()\nif n != null {\n    n.Bump()\n}", "NYX2080 29:5")]
    // A C# struct method that isn't readonly, for no value (ADR-0032 decision 3).
    [InlineData("let e = self.list.GetEnumerator()\ne.MoveNext()", "NYX2081 28:1")]
    public void ChangesWhereTheStructCannotChange(string statements, string expected) =>
        Assert.Equal([expected], InStructFunction(statements));

    [Theory]
    // In a struct, self can change in var self functions, setters and init; not in self functions and getters.
    [InlineData("    func F(self) {\n        self.x = 1\n    }", "NYX2081 4:9")]
    [InlineData("    func F(self) {\n        self.Bump()\n    }", "NYX2081 4:9")]
    [InlineData("    func F(self) {\n        self.X = 1\n    }", "NYX2081 4:9")]
    [InlineData("    property Y: int {\n        get {\n            self.x = 1\n            return 0\n        }\n    }", "NYX2081 5:13")]
    [InlineData("    func F(var self) {\n        self.x = 1\n        self.Bump()\n        self.X = 2\n    }", null)]
    [InlineData("    property Y: int {\n        get = self.x\n        set(v) {\n            self.x = v\n            self.Bump()\n        }\n    }", null)]
    [InlineData("    init(x: int) {\n        self.x = x\n        self.Bump()\n    }", null)]
    public void SelfOfAStruct(string member, string? expected) =>
        Assert.Equal(expected is null ? [] : [expected], DiagnosticsOf(
            "struct S {\n    var x: int = 0\n" + member + "\n    property X: int {\n        get = self.x\n        set(v) { self.x = v }\n    }\n    func Bump(var self) {\n        self.x += 1\n    }\n}"));

    [Fact]
    public void MessagesSayWhatChangesAndWhy()
    {
        Assert.Equal(
            "Calling 'Bump', a 'var self' function, changes 's', which is declared with 'let'; declare it with 'var'.",
            Message(StructSource + "let s = new S()\ns.Bump()\n    }\n}\n"));
        Assert.Equal(
            "Setting 'x' changes 'self.list[0]', which is a copy of the struct, so the change would be lost. Store it in a 'var', change that, and assign it back.",
            Message(StructSource + "self.list[0].x = 1\n    }\n}\n"));
        Assert.Equal(
            "Setting 'x' changes 'self', which is 'self' in a function without 'var self'; write 'var self' to let the function change the value.",
            Message("struct S {\n    var x: int = 0\n    func F(self) {\n        self.x = 1\n    }\n}"));
        Assert.Equal(
            "'P' has no 'get': a property must be readable. Something that can only be set is a function, such as 'func SetP(self, value: int)'.",
            Message("class C {\n    property P: int = 0 { set }\n}"));
        Assert.Equal(
            "'private' can't be written on this 'set': the property itself is private, so this narrows nothing.",
            Message("class C {\n    property P: int = 0 {\n        get\n        private set\n    }\n}"));

        static string Message(string source) => Compile(source).GetDiagnostics().Single().Message;
    }
}
