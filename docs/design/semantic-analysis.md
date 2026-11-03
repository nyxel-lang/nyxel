# 语义分析（Binder）

编译流水线的第二段（architecture-overview.md）：语法树加引用的程序集 → Binder → 符号和绑定树（含语义诊断）→ 降级。本文是工程设计，写在实现之前，实现时按实际情况更新；语言规则以 language-reference 和 ADR 为准。代码将放在 `src/Nyxel.Compiler/Symbols/` 和 `src/Nyxel.Compiler/Binding/`。

语法分析只知道代码的形状：`self.NearestTo(point = other.Position)` 是在 `self` 上调用一个叫 `NearestTo` 的成员，带一个叫 `point` 的实参。语义分析回答它是什么意思、对不对：`NearestTo` 是哪个方法，有没有叫 `point` 的参数，`other.Position` 是什么类型、能不能传过去，返回值是什么类型。为什么不把这些交给 Roslyn，见 ADR-0004：有些 Nyxel 的规则 C# 没有（`let` 不能重新赋值，翻译成 C# 后 Roslyn 不会报），有些写法翻译之前就要知道类型（`let x: float = 1 / 2` 是 0.5，要写成 `1f / 2f`），报错也要用 Nyxel 的术语、指向 .nyxel。

## 用法

```csharp
var compilation = NyxelCompilation.Create(
    assemblyName: "Game.Scripts",
    syntaxTrees: [SyntaxTree.Parse(text, "Slime.nyxel"), ...],
    references: [MetadataReference.CreateFromFile(".../System.Runtime.dll"), ...]);

compilation.GetDiagnostics()     // 语法和语义诊断，按文件、位置排序
compilation.GetBoundBody(method) // 函数体的绑定树，给降级和测试用
```

- 引用用 Roslyn 的 `MetadataReference`：程序集的元数据本来就经 Roslyn 读（见“符号”）。
- 叫 `NyxelCompilation`，不叫 `Compilation`：同一个文件里常要同时用到 Roslyn 的 `Microsoft.CodeAnalysis.Compilation`。
- M2 的 CLI 编译时引用运行中的 .NET 自己的程序集（`TRUSTED_PLATFORM_ASSEMBLIES`，同 RoslynLineMappingTests）；`.nyxelproj` 的引用由 MSBuild 解析后传进来（M3 的 Nyxel.Sdk）。
- 以后的语言服务器（M5）要的“这个节点指向哪个符号、是什么类型”，由 `GetSemanticModel(tree)` 提供，接口到 M5 再定。

## 符号

符号（symbol）是一个有名字的东西的完整描述：`Spawner.NearestTo` 是实例方法，参数 `point: Vector3`，返回 `Slime?`，public，声明在 Spawner.nyxel 第 64 行。名字解析就是把代码里的每个名字对应到一个符号。

```
Symbol（Name、Kind、ContainingSymbol、Accessibility、Locations）
├─ NamespaceSymbol
├─ TypeSymbol
│   ├─ NamedTypeSymbol      class、struct、interface、enum、委托；List<int> 这样的构造类型也是它
│   ├─ ArrayTypeSymbol      array<T>、array2d<T>、array3d<T>
│   ├─ NullableTypeSymbol   T?
│   ├─ TypeParameterSymbol
│   ├─ TupleTypeSymbol、FunctionTypeSymbol（func(A) -> R）
│   └─ ErrorTypeSymbol      出过错的地方，见“诊断”
├─ MethodSymbol             func、init、运算符、访问器、lambda
├─ FieldSymbol、PropertySymbol、EventSymbol、EnumCaseSymbol
└─ ParameterSymbol、LocalSymbol
```

- **一套接口，两种来源**：
  - 源码符号：从 Nyxel 的声明建出来（`SourceNamedTypeSymbol` 等）。
  - 元数据符号：包装 Roslyn 从引用的程序集读出的符号（`MetadataNamedTypeSymbol` 包一个 `INamedTypeSymbol`）。成员第一次被问到时才建，每个 Roslyn 符号只包一次。
  - Binder 不关心符号从哪来：`class Slime : Behaviour` 的基类是元数据符号，在 Slime 上查成员时一样沿着继承关系往上找。
- 读元数据用一个只有引用、没有源码的 `CSharpCompilation`（`GlobalNamespace`、`GetTypeByMetadataName`）。后端编译降级出来的 C# 时往同一个对象里加语法树，元数据不用读两遍。
- 内置类型名就是对应的元数据符号：`int` 和 `System.Int32` 是同一个符号。
- 同一个类型只有一个符号对象，比较类型就是比较引用。构造类型（`List<int>`）按类型实参缓存。
- `T?`：值类型的 `T?` 就是 `System.Nullable<T>`；引用类型的 `T?` 是加了“可能是 null”标记的同一个类型。两种都表示成 `NullableTypeSymbol`，Binder 按一种规则处理（ADR-0009）。读元数据时，带可空标注的引用类型、`Nullable<T>` 都转成它；没有标注的程序集，返回值和字段当作 `T?`（ADR-0009）。
- Nyxel 有、C# 没有的概念直接放在源码符号上：字段是 `let` 还是 `var`，函数有没有 `self`、是不是 `var self`，带数据的 enum 的 case，命名 `init`。翻译成什么样的 C# 是降级的事。

## 分几遍

```
1. 声明     所有文件：命名空间和类型的符号（名字、种类、类型参数、修饰符）
2. 导入     每个文件：import、under 别名、同名类型
3. 类型头   基类、接口、类型参数的约束、enum 的底层类型
4. 成员签名 字段、属性、函数、init、事件、enum case 的类型；重写、实现接口、命名检查
5. 函数体   每个函数体、字段初始值、访问器：绑定树，流分析
```

- 第 1 遍先扫完所有文件，后面才能引用别的文件、同一个文件后面才声明的类型。
- 第 3 遍查继承的环（`class A : B`、`class B : A`）。
- 第 4 遍能一次做完，是因为成员的类型都写在签名上：字段、参数、返回类型必须写（ADR-0006），没有哪个成员的类型要从函数体推出来。所以第 5 遍的函数体互相独立，按什么顺序绑定都行，以后也可以并行。
- 每一遍只依赖前面几遍的结果。语言服务器（M5）要按需绑定（只算光标所在的函数），到时把第 3、4 遍改成第一次问到时才算、带环检测；符号建好后不再改，为这一步留了余地。

## 名字查找

规则以 language-reference 为准（ADR-0008、ADR-0012、ADR-0033），这里是实现方式。

- 作用域是一串链起来的 `Binder`，查名字时从里往外：块（局部变量）→ 函数（参数、类型参数）→ 类型（类型参数、嵌套类型）→ 文件（导入、别名）→ 命名空间 → 全局。
- 不带接收者的名字只找局部变量和参数（ADR-0008），成员一律经 `self.` 或类型名。所以类型的 Binder 只提供类型参数和嵌套类型，不提供字段和方法。
- `x.Name` 看 `x` 是什么：命名空间里找类型和子命名空间；类型上找静态成员、enum case、嵌套类型；值上找实例成员，找不到再找导入的扩展。
- 同一个名字既可能是类型也可能是值时（`Slime.Killed` 里的 `Slime`），先按值找，找不到再按类型找，同 C#。

## 类型和转换

- **期望的类型往里传**：`BindExpression(语法, 期望的类型)`。这一点贯穿整个 Binder，很多写法的含义要靠它：
  - 数字字面量的类型（ADR-0007、ADR-0013）；
  - lambda 的参数类型（`func(e) = e.HP > 0` 的 `e`）；
  - 集合字面量 `[]`、`null`、按位置写的元组值（ADR-0021）；
  - 当值用的 `if`、`match`、`await any`（各分支的类型）。
- 期望的类型来自赋值目标、声明的类型、参数类型、返回类型、比较运算的另一边，经过算术运算符往里传（ADR-0013）。
- **隐式转换**同 C#：数值的隐式转换（`int` 到 `float`，ADR-0010）、子类到父类和接口、装箱、`null` 到可空类型、C# 类型定义的隐式转换（ADR-0030）。Nyxel 自己不能定义隐式转换。每次转换在绑定树里都是显式的 `BoundConversion` 节点，降级看得到。
- **`as` 转换**按 ADR-0014、ADR-0030 的规则，可能失败的向下转换报错。

## 调用 .NET 的成员：重载决议和类型推断

最难的一块。Nyxel 自己声明的函数不能重载（ADR-0011），要选重载的只有：.NET 的方法、构造函数、索引器，运算符（Nyxel 和 .NET 的），转换。

**做法：Binder 自己实现 C# 的这部分规则，并和 Roslyn 做对照测试**，同字面量的 CSharpLiteralTests：

- 规则按 C# 语言规范的“成员查找”“重载决议”“类型推断”几节实现，不凭记忆。
- 对照测试：同一个调用写成 C# 和 Nyxel，C# 那边用 Roslyn 的 `SemanticModel` 看它选中了哪个方法，Nyxel 那边看 Binder 选的，比较两边方法的 `GetDocumentationCommentId()` 和推出的类型实参。
- 分步做：
  - 第一步（M2 的第一条线）：按名字和实参个数找候选，命名实参、可选参数、隐式转换（上面那些），按“更好的转换”选出一个。没有泛型推断。
  - 之后：泛型推断（包括 lambda：先从别的实参推出 `TSource`，再用它绑定 lambda 体、推返回类型）、扩展方法（经典的 `this` 静态方法和 C# 14 的 extension 块）、`params`、C# 定义的隐式转换。LINQ 要到这一步才能用。
- 还没实现的情况报“这种写法还不支持”（一个通用的错误码，消息写明是哪种写法），不猜。随着实现补全，它出现的地方逐个消失。
- 选错了的兜底：降级出的 C# 交给 Roslyn 时，Roslyn 会按自己的规则再选一遍。两边不一致导致的错误按内部错误报出（ADR-0004），说明对照测试漏了一种情况，要补上。

为什么不直接问 Roslyn，见“备选方案”。

## 流分析

有些规则要沿着执行路径推导。

- **收窄，在绑定时做**：`if other is not Projectile { return }` 之后 `other` 的类型是 `Projectile`，后面绑定 `other.Power` 时马上就要用到。
  - Binder 绑定语句时带着一个“流状态”：哪些局部变量和参数在这一点被收窄成了什么类型。
  - 只有局部变量和参数会收窄（language-reference“空值”“类型检查”两节）。`let` 和参数不能重新赋值，收窄后一直有效；`var` 局部变量被赋值后取消收窄。
  - 收窄来自 `is`、`!= null`、`?? return` / `?? throw`、提前 `return` 的 `if`。分支汇合时只保留两边都成立的收窄；循环体里赋过值的变量，在循环开头就不算收窄。
- **在绑定树上另走一遍**：
  - 确定赋值：`let` 字段在每个 `init` 里都赋过值（ADR-0006、ADR-0027）。
  - 可达性：有返回值的函数每条路径都 `return`（ADR-0007），`eval` 块每条路径都 `give` 或 `throw`（ADR-0025）。

## 前几轮留下的检查放在哪一遍

各 ADR 的“后果”里留给 M2 的检查，按要用到的信息分到各遍。完整的清单见 STATUS 的“下一步”和各 ADR。

| 哪一遍 | 例子 |
|---|---|
| 2 导入 | 同名类型、`under` 前面是命名空间、别名重名（ADR-0033） |
| 3 类型头 | 带数据的 enum 和 `struct enum` 冒号后面只能是接口（ADR-0029），约束够不够写 `T?`（ADR-0031） |
| 4 成员签名 | 访问器带不带体（ADR-0026、ADR-0029），运算符的参数类型（ADR-0030），实现接口的事件和委托一致（ADR-0029），修饰符能不能用在这种声明上，命名和缩略词（ADR-0006、ADR-0035），文档注释的参数名和链接（ADR-0035） |
| 5 函数体 | 类型检查，`match` 是否穷尽，在副本上调用 `var self` 方法（ADR-0032），`with` 和 `required`（ADR-0027），`await` 只在 `async` 函数里（ADR-0016、ADR-0034），区间和集合字面量的期望类型（ADR-0022） |
| 5 之后的流分析 | `let` 字段的确定赋值，`eval` 的 `give`（ADR-0025） |

## 绑定树

绑定树（bound tree）和语法树的形状差不多，但每个节点都知道自己的符号和类型，隐含的东西都写明了。降级只看绑定树，不再回头查名字。

```
语法树：  调用( 成员访问(self, "NearestTo"), 实参[ point = 成员访问(other, "Position") ] )
绑定树：  调用 Spawner.NearestTo(point: Vector3) -> Slime?
            接收者: self（Spawner）
            实参:   属性 Collider.Position（Vector3）
```

- 节点不可变，每个节点记着对应的语法节点（报错、`#line` 映射用）；表达式节点都有 `Type`。
- 显式写出的东西：
  - 转换（`BoundConversion`），包括字面量按期望类型定下来的类型（`1 / 2` 赋给 `float` 时两边都是 `float` 字面量）；
  - 收窄（读 `other` 时节点上带着收窄后的类型，降级据此插入转换）；
  - 选中的重载和推出的类型实参。
- 绑定树不挂在语法树上：语法树是不可变的，语言服务器在编辑之间会复用它。
- 测试和调试用 `BoundTreePrinter` 打印成缩进文本，同 `SyntaxTreePrinter`。

## 诊断

- 语义诊断用 `NYX2xxx`，从 NYX2001 开始，规则同 diagnostics.md：一句话说清问题，能给出正确写法的直接写出来，位置指向问题开始的地方。
- 一处错只报一次：
  - 出过错的表达式的类型是 `ErrorTypeSymbol`，和任何类型都兼容，用到它的地方不再报错。
  - 名字找不到时报一次，这个名字后面的成员访问、调用都不再报。
- 消息里的类型用 Nyxel 的写法：`int` 不写 `Int32`，`array<int>`、`T?`、`func(int) -> bool`。由一个专门的格式化器统一生成。
- 第一批警告（命名检查、文档注释）在这里出现，`Diagnostic` 已经有 `Warning` 级别。
- 诊断会跨文件，`DiagnosticBag` 改成按 `Location` 收集；最后按文件路径、位置排序。

## 测试

- **BinderTests**：小段代码绑定后，检查表达式的类型、名字指向的符号，或者对比 `BoundTreePrinter` 的输出。
- **SemanticErrorTests**：同 ParserErrorTests，一段代码对应期望的错误码和位置（`"NYX2003 4:9"`），消息另有测试。
- **对照测试**（OverloadResolutionTests）：见“调用 .NET 的成员”。
- **端到端**（有了降级和后端之后）：tests/Nyxel.Compiler.Tests/Programs/ 里每个 `.nyxel` 程序配一个期望输出，编译、在进程内运行、比较输出；降级出的 C# 做快照（`--emit-cs`）。
- **样例**：samples/ 用的 `Engine` 是假想的引擎 API。要让样例完整地绑定，给它写一个只有签名的 C# 桩程序集，放在测试里引用。等第一条线做完、开始逐步加宽时再写。

## 第一条线（M2）

roadmap 上 M2 的验收是 hello world 和几个算法小程序能跑、断点能停在 .nyxel 上。所以先做最窄的一条线，从 Binder 到降级、后端、`nyxel run`，全部打通，再逐步加宽。

- **包括**：
  - `namespace`、`import`；
  - `class`（不继承）、没有 `self` 的函数和带 `self` 的函数、`let` / `var` 字段和初始值、不带名字的 `init`；
  - 局部变量 `let` / `var` 和类型推断；
  - `int`、`long`、`float`、`double`、`bool`、`char`、`string` 的字面量，按期望的类型定型；
  - 算术、比较、`and` / `or` / `not`、赋值和复合赋值；
  - `if` / `else`、`while`、`for i in 0..<n`、遍历数组和 `List<T>`、`break`、`continue`、`return`；
  - 调用自己的函数，调用 .NET 的静态方法、实例方法、构造函数（`Console.WriteLine`、`Math.Max`、`new List<int>()`、`list.Add(x)`），用上面第一步的重载决议；
  - 插值字符串；`array<T>` 的创建和下标。
- **之后逐步加**：属性、struct、enum（带数据的、flags）、`match`、空值和收窄、lambda 和泛型推断、扩展、继承和接口、`try` / `catch`、`using`、`eval`、`with`、运算符、索引器、事件、协程。每加一块，补上对应 ADR 留下的检查。
- **验收的程序**：hello world、FizzBuzz、递归的斐波那契、用 `array<bool>` 的素数筛、对 `List<int>` 排序，放在 tests/…/Programs/。断点在 VSCode 里手动验证（debug-mapping.md“还没验证”）。

## 写设计时定下的语言规则

第一条线会碰到、前几轮还没定的两条，和用户讨论后定在 ADR-0036：

- **程序入口**：输出可执行程序时，从任意一个类型里没有 `self` 的 `Main` 开始执行，同 C#。在第 4 遍找：没有、两个以上、签名不对都报错。
- **同名**：一个函数里一个名字只指一个变量，lambda 也不例外。块的 Binder 声明名字时，沿着作用域链查到函数为止（包括外层函数，lambda 嵌在里面时），看得见的局部变量和参数里有同名的就报错。局部变量从声明处开始可见。

以后实现时再碰到 ADR 没覆盖的语义问题，同样先停下来讨论。

## 备选方案

- **不做类型检查，翻译成 C# 后交给 Roslyn 查**：ADR-0004 已经不选。C# 没有的规则查不出来，字面量按期望的类型定型这种写法翻译之前就要知道类型，报错是 C# 的术语。
- **调用 .NET 的成员时，把问题翻译成一小段 C# 去问 Roslyn**（Roslyn 的试探性绑定 `GetSpeculativeSymbolInfo`）：省掉自己实现重载决议和类型推断。不选的理由：
  - Roslyn 要在一段 C# 代码的上下文里回答，这段代码里要能看到 Nyxel 声明的类型，等于先给所有 Nyxel 声明生成 C# 的签名。
  - lambda 的推断和函数体的绑定交织在一起：要推 `Where(func(s) = ...)` 的类型，Roslyn 得绑定 lambda 的体，可体是 Nyxel 代码。
  - Nyxel 的字面量定型、收窄和 C# 不同，传给 Roslyn 的问题要先换成 C# 的说法，答案再换回来；失败时的报错是 C# 的消息，要翻译。
  - Roslyn 仍然用作对照测试的标准答案。
- **先给 Nyxel 的声明生成 C# 的签名，所有符号都用 Roslyn 的**：一套符号，重载决议和类型推断都能交给 Roslyn。不选的理由：Nyxel 有、C# 没有的概念（带数据的 enum、`let` 字段、`var self`、命名 `init`）在 Roslyn 的符号上看不出来，要另配一张对照表；生成签名本身就要先解析类型名，而解析规则（ADR-0033）和 C# 不同；每次编辑都要重新生成。
- **一开始就按需绑定**（Roslyn 的做法，适合语言服务器）：分遍更容易理解和测试，第一条线也用不到；符号建好后不可变，以后改成按需算不用推翻（见“分几遍”）。
- **把类型和符号直接记在语法树的节点上**，不另建绑定树：少一套类，但语法树要变成可变的，语言服务器没法在编辑之间复用；隐含的转换、收窄也没有地方写明。
