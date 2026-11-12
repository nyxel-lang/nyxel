# 语义分析（Binder）

编译流水线的第二段（architecture-overview.md）：语法树加引用的程序集 → Binder → 符号和绑定树（含语义诊断）→ 降级（lowering.md）。本文是工程设计，实现时按实际情况更新；语言规则以 language-reference 和 ADR 为准。代码将放在 `src/Nyxel.Compiler/Symbols/` 和 `src/Nyxel.Compiler/Binding/`。

语法分析只知道代码的形状：`self.NearestTo(point = other.Position)` 是在 `self` 上调用一个叫 `NearestTo` 的成员，带一个叫 `point` 的实参。语义分析回答它是什么意思、对不对：`NearestTo` 是哪个方法，有没有叫 `point` 的参数，`other.Position` 是什么类型、能不能传过去，返回值是什么类型。为什么不把这些交给 Roslyn，见 ADR-0004：有些 Nyxel 的规则 C# 没有（`let` 不能重新赋值，翻译成 C# 后 Roslyn 不会报），有些写法翻译之前就要知道类型（`let x: float = 1.5 * 2` 两边都是 `float`，要写成 `1.5f * 2f`），报错也要用 Nyxel 的术语、指向 .nyxel。

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
├─ FieldSymbol、PropertySymbol、EventSymbol   不带数据的 enum case 是 enum 的常量字段，同 C#
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

- 查名字时从里往外：块（局部变量）→ 函数（参数）→ 文件（导入、别名）→ 命名空间 → 全局。
  - 函数里的作用域是 `MethodBinder` 的一个作用域栈，每个块压一层；同名检查（ADR-0036）就是声明时把整个栈查一遍。
  - 文件那一层是 `ImportScope`：所在命名空间和外层的类型，然后是导入的命名空间、`under` 别名，再是命名空间名；两个导入都有同一个类型时报 NYX2006。
  - 类型参数等泛型做到时再加进来。
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
- 字面量实参跟着有类型的实参（ADR-0037），`Resolve` 分三步，每一步是一组“适用”的规则（`ArgumentRule`）加同一套“更好”的比较：
  1. `LiteralsFollow`：有类型的实参只认原样传进去，字面量放得进任何装得下它的数值类型（或者按默认类型装箱）。比较时字面量变成哪种数值类型都一样好、数值类型比装箱好，其余同 C#。选出唯一一个就用它（`Math.Max(speed, 0.5)` 是 `float` 的那个）。
  2. `CSharp`：字面量按默认类型，完全是 C# 的规则（整数字面量能转成放得下它的整数类型，小数字面量是 `double`）。`Math.Max(1, 2.5)` 是 `double` 的那个，和 C# 一样。
  3. `DecimalLiterals`：第 2 步一个都不适用时，小数字面量也能转成 `float` / `decimal`（`MathF.Max(count, 0.5)`）。
  - 选定以后，字面量按参数的类型重新绑定。
  - 第 1 步选出的、和 C# 不同的调用，对照测试和补上后缀的 C# 比（`0.5f`、`(byte)1`）。
  - 二义性的诊断列出两个谁也不比谁好的重载（同 Roslyn），不是前两个适用的。
- 还没实现的情况报“这种写法还不支持”（NYX2001，消息写明是哪种写法），不猜。随着实现补全，它出现的地方逐个消失。
- 选错了的兜底：降级出的 C# 交给 Roslyn 时，Roslyn 会按自己的规则再选一遍。两边不一致导致的错误按内部错误报出（ADR-0004），说明对照测试漏了一种情况，要补上。

为什么不直接问 Roslyn，见“备选方案”。

## 流分析

有些规则要沿着执行路径推导。

- **收窄，在绑定时做**：`if other is not Projectile { return }` 之后 `other` 的类型是 `Projectile`，后面绑定 `other.Power` 时马上就要用到。
  - Binder 绑定语句时带着一个“流状态”：哪些局部变量和参数在这一点被收窄成了什么类型。
  - 只有局部变量和参数会收窄（language-reference“空值”“类型检查”两节）。`let` 和参数不能重新赋值，收窄后一直有效；`var` 局部变量被赋值后取消收窄。
  - 被 lambda 捕获的 `var` 局部变量不收窄（ADR-0042）：lambda 可能在检查和使用之间把它改掉。绑定函数体之前先在语法树上扫一遍，记下被 lambda 读或写的 `var`（按名字找就行，一个函数里一个名字只指一个变量，ADR-0036），收窄和赋值后收窄都跳过它们；报 NYX2057 / NYX2058 时说明它被 lambda 捕获了，建议先存进 `let`。
  - 收窄来自 `is`、`!= null`、`?? return` / `?? throw`、提前 `return` 的 `if`。分支汇合时只保留两边都成立的收窄；循环体里赋过值的变量，在循环开头就不算收窄。
- **在绑定树上另走一遍**：
  - 确定赋值：`let` 字段在每个 `init` 里都赋过值（ADR-0006、ADR-0027）。局部变量不需要：它们必须在声明处给初始值（ADR-0042，Parser 报 NYX1239）。
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
  - 转换（`BoundConversion`），包括字面量按期望类型定下来的类型（`1.5 * 2` 赋给 `float` 时两边都是 `float` 字面量）；
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
  - `class`（不继承）、`static func` 和带 `self` 的函数、`let` / `var` 字段和初始值、不带名字的 `init`；
  - 局部变量 `let` / `var` 和类型推断；
  - `int`、`long`、`float`、`double`、`bool`、`char`、`string` 的字面量，按期望的类型定型；
  - 算术、比较、`and` / `or` / `not`、赋值和复合赋值；
  - `if` / `else`、`while`、`for i in 0..<n`、遍历数组和 `List<T>`、`break`、`continue`、`return`；
  - 调用自己的函数，调用 .NET 的静态方法、实例方法、构造函数（`Console.WriteLine`、`Math.Max`、`new List<int>()`、`list.Add(x)`），用上面第一步的重载决议；
  - 插值字符串；`array<T>` 的创建和下标。
- **之后逐步加**：属性、struct、enum（带数据的、flags）、`match`、空值和收窄、lambda 和泛型推断、扩展、继承和接口、`try` / `catch`、`using`、`eval`、`with`、运算符、索引器、事件、协程。每加一块，补上对应 ADR 留下的检查。
- **验收的程序**：hello world、FizzBuzz、递归的斐波那契、用 `array<bool>` 的素数筛、对 `List<int>` 排序，放在 tests/…/Programs/。断点在 VSCode 里手动验证（debug-mapping.md“还没验证”）。

## 实现状态

第一条线已经打通（2026-10-31）：tests/…/Programs/ 的五个程序编译、运行、输出正确，异常堆栈指向 .nyxel 的行。代码：

- `Symbols/`：`Symbol` 和各种类型、成员；`SymbolTable` 包装 Roslyn 读出的元数据，管数组类型；源码符号在 `SourceSymbols.cs`；`SymbolDisplay` 用 Nyxel 的写法显示名字。
- `Binding/`：`Declarer`（第 1 到 4 遍）、`ImportScope`、`MethodBinder`（第 5 遍，按语句、表达式、调用分文件）、`Conversions`、`FlowAnalysis`、绑定树的节点和打印。
- `NyxelCompilation`：`Create`、`GetDiagnostics`、`EmitCSharp`、`Emit`。
- 语义诊断 NYX2001 到 NYX2090（diagnostics.md“语义”）。
- 测试：`BinderTests`、`SemanticErrorTests`、`NullSafetyTests`、`PropertyAndStructTests`、`EnumAndConstantTests`、`DataEnumTests`、`OverloadResolutionTests`（30 个调用和 Roslyn 对照）、`ProgramTests`。

### 空值和收窄（2026-11-06）

- **`T?`** 是 `NullableTypeSymbol`，每个底层类型一个（`SymbolTable.GetNullableType`）；`null` 字面量的类型是 `NullTypeSymbol`，只能转成 `T?`。`T?` 没有成员、基类、接口：用之前必须检查。
- **读元数据**：`SymbolTable.GetType` 按 C# 的可空标注转换（`string?`、`Nullable<int>` 都成 `T?`）；成员的类型走 `GetMemberType`，没有标注的程序集（Roslyn 的 `NullableAnnotation.None`）的返回值、字段、属性、参数是 `T?`，只管最外层，类型实参和数组元素照程序集写的。.NET 里没约束的 `T?` 代入值类型时是 `T` 本身（`TypeMap.Substitute`，ADR-0031）。重载决议里 `string` 和 `string?` 算同一个类型，同 C#。
- **流状态**：`MethodBinder` 带着一张“局部变量 / 参数 → 此刻收窄成的类型”的不可变表（`_flow`）。读变量时取表里的类型，`BoundLocalAccess` / `BoundParameterAccess` 的 `Type` 就是它。
  - 条件绑定成三样东西：表达式、条件成立时的状态、不成立时的状态（`BindConditionFlow`）。`x != null`、`x is T` 在成立时收窄，`== null`、`is not` 在不成立时；`not` 交换两边；`and` 的右边在左边成立的状态里绑定，`or` 的右边在左边不成立的状态里。当值用的 `and` / `or` / `not` 绑完以后两边汇合。
  - `if`：两个分支分别从成立 / 不成立的状态开始，之后取能走到结尾的分支的交集（`FlowAnalysis.CanComplete` 判断），所以 `if x == null { return }` 之后 `x` 是非空的。
  - 循环：循环体里赋过值的变量（按名字在语法树上找）在进入循环前就取消收窄；`while` 正常结束时条件不成立，有 `break` 时只保留进入循环时的状态。
  - 给局部变量赋值（包括声明时的初始值）先取消它的收窄，值不可能是 null 时再收窄成声明的类型去掉 `?`（`NarrowAfterAssignment`，ADR-0041）；`??=` 同样（ADR-0023）；`let t = x ?? return` 之后 `x` 也收窄。
  - `== null`、`??`、`?.` 读一个声明成 `T?`、此刻已经收窄的变量时，按 `T?` 看它（`AsMaybeNull`）：再检查一次只是多余，降级出的 C# 也是在声明的类型上检查（`n is null`，不是 `((int)n) is null`）。
- **`?.`**：`a?.B.C()` 先绑定 `a`，再把整条链（成员访问、调用、下标，括号截断）在“`a` 换成一个占位的非空接收者”的情况下重新绑定，结果类型是 `T?`（`BindConditionalAccess`）。链后面的收窄不带出来（链可能没执行）。
- **`??`**：右边的数字跟着左边的类型（ADR-0013）；结果类型按 C# 的三条规则。
- **`int?` 的 `==` / `!=`**（ADR-0041）：两边去掉 `?` 以后按 C# 的规则定出比较的类型（数字的提升、`bool`），两边都转成它的 `T?` 再比，降级成 C# 的提升运算符。别的运算符碰到 `T?` 报 NYX2057。和 `int?` 比较的数字字面量取它去掉 `?` 的类型（`f == 0.5` 比 `float`）。
- **多余的检查**（ADR-0041）：`CheckCanBeNull` 只对不可空的值类型报 NYX2063；不可空的引用类型照 C# 处理，等编译器有了警告在这里加。右边是 `return` / `break` / `continue` 时只能出现在整条语句的值上（`IsStatementValue`，降级见 lowering.md），`throw` 哪都行。
- **新的写法**：`throw` 语句（类型要是 `Exception`）、`is` / `is not`（两个类型没关系时报 NYX2061）、`new array<T>(n)` 的元素类型检查（ADR-0023，NYX2064）。
- 实现时先按最严的方式处理、2026-11-09 和用户定下的五处细节见 ADR-0041：赋值后收窄、`int?` 只有 `==` / `!=` 提升、`T?` 没有成员、多余的检查是警告、没标注的程序集只管最外层。字符串拼接 `"a" + s` 照 C# 可以（它是 `string + object`）。
- `x == 3` 成立时 `x` 不收窄（Kotlin 会），收窄只来自 ADR 列的写法。

### 整数相除、优先级的诊断、推断出的 `var`（2026-11-10，ADR-0042）

- **整数相除的结果不隐式变浮点**（NYX2066）：查在 `MethodBinder.Convert` 里，所有隐式转换都从这里过。目标是 `float` / `double` / `decimal`（或它们的 `T?`）、值是整数时，`IntegerDivisionIn` 沿着绑定树往里找：整数的 `+ - * %`、取负、整数之间的隐式转换都穿过去，碰到两边是整数的 `/` 就报；调用、`as`、移位、别的节点都停。括号在绑定树里没有节点，自然穿过。重载照旧先选（OverloadResolution 不知道这条规则），选完做转换时才报。只有字面量的式子绑定时就折叠成一个 `BoundLiteral`，走不到这里，由 NYX2054 管（ADR-0038）。
- **`not` 后面的比较**（NYX2067）：`ResolveUnary` 发现 `not` 的操作数不是 `bool` 时，看语法树上的父节点：`not` 是比较、`==` / `!=` 或 `is` 的左边，就换成这条诊断，给出反过来的比较（`hp <= 0`、`x is not T`）和加括号的写法。操作数是 `bool` 时（`not a == b`）照常通过。
- **位运算和比较混写**（NYX2068）：`ResolveBinary` 里 `& ^ |` 的两边既不都是 `bool` 也不都是整数时，看语法上哪一边是没加括号的比较（加了括号的是 `ParenthesizedExpressionSyntax`），另一边不是 `bool` 就报，消息里拼出它被读成的样子和该加的括号。
- **推断出类型的 `var` 赋了可能是 null 的值**（NYX2069）：`BindAssignment` 在做转换之前查：目标是没写类型的 `var` 局部变量（标识符的父节点是 `TypeAnnotation` 为空的声明）、类型不可空，值是 `null` 或去掉 `?` 以后能转成它的 `T?`。别的情况照旧是 NYX2058 / NYX2059。
- **局部变量没有初始值**：Parser 报 NYX1239；Binder 不再报，把它当 `var` 声明出来（类型取写的类型，没写是错误类型），后面给它赋值的语句不连带报错。

### 属性和 struct（2026-11-11，ADR-0044）

- **属性**：`SourcePropertySymbol` 带着两个访问器，访问器是 `MethodKind` 为 `PropertyGet` / `PropertySet` 的 `SourceMethodSymbol`（语法是 `AccessorDeclarationSyntax`，名字取属性名，消息里好认）；`set(v)` 的新值是它唯一的参数，同名检查和别的参数一样。
  - 第 4 遍（`Declarer.DeclareProperty`）查 NYX2070 到 2076：访问器有的带体有的不带、计算属性写了初始值、没有 `get`、重复的 `get` / `set`、`set` 的参数、访问器上的可见性（同 C#）。
  - 自动属性的值同字段：没有初始值时按字段的规则报 NYX2049 / 2050（`FlowAnalysis.MembersNotSetOnEveryPath` 把 `self.P = ...` 也算进去）；只有 `get` 的在本类型的 `init` 里经 `self.` 能赋值，别处报 NYX2029。
  - 访问器上窄一些的可见性：`PropertySymbol.GetterAccessibility` / `SetterAccessibility`，读的时候和赋值的时候分别查（`IsAccessibleHere`），用不了时报 NYX2030 / 2029。元数据的属性照旧只看 public / protected 的访问器。
  - 计算属性的访问器在第 5 遍和函数一样绑定（`NyxelCompilation.MethodsWithBodies`），getter 要每条路径都 `return`；自动属性的初始值和字段的一起绑定（`BoundProgram.Initializers` 按符号存）。
- **struct**：第 1 遍和 class 一起声明。第 4 遍查 `protected`（NYX2077）、class 的 `var self`（NYX2078）、struct 直接或经别的 struct 包含自己（NYX2079，`S?` 也算）。`new S()` 只对 C# 的 struct 是全零；Nyxel 的 struct 声明了带参数的 `init` 就要经过它，没有不带参数的重载时报 NYX2013。
- **能不能改**（ADR-0032 决策 1 的表，`MethodBinder.Structs.cs`）：`WhyCantChange` 把一个 struct 的值分成存储、只读的存储、副本，并找出决定它的那一段（`let` 局部变量、参数、`for` 的变量、`self`、`let` 字段，或者给出副本的属性、索引器、调用）。
  - 给字段或属性赋值（含复合赋值、`??=`）、调用 Nyxel 的 `var self` 函数时，接收者是 struct 就查它；只读的报 NYX2081，副本报 NYX2080，消息说明是哪一段、为什么、怎么改。class 的值不查：改的是对象。
  - 字段访问沿着接收者往外找：class 的字段是存储，struct 的字段跟着所在的 struct。数组元素是存储；返回 `ref` 的索引器和函数（`Span<T>`）是存储，`ref readonly` 是只读（读元数据的 `ReturnRefKind`）。
  - struct 的 `self` 函数和 `get` 里的 `self` 是只读的，`var self` 函数、`set`、`init` 里能改（`SourceMethodSymbol.IsReadOnly`，降级成 C# 的 `readonly` 也用它）。
  - 检查过的 `S?` 局部变量读出来是 `Nullable<S>` 里那个值的副本，改它报 NYX2080（原来给它的字段赋值报“还不支持”）。
  - C# 里没标 `readonly` 的 struct 方法（`MethodSymbol.IsReadOnly` 读 Roslyn 的同名属性）只在单独成一条语句、值没用上时查（`BindExpressionStatement` 里的 `CheckUnusedCall`，ADR-0032 决策 3）。

### enum 和常量（2026-11-12，ADR-0024、ADR-0029、ADR-0038、ADR-0040、ADR-0045）

- **不带数据的 enum**：第 1 遍和 class 一起声明（`SourceNamedTypeSymbol` 的语法也可以是 `EnumDeclarationSyntax`），`TypeKind.Enum`，基类是 `System.Enum`（`HasFlag`、`ToString` 由此而来）；第 3 遍定底层类型，冒号后面的整数类型，没写是 `int`。`struct enum` / `class enum` 见下一节。
  - case 是 enum 类型的常量字段（`SourceEnumCaseSymbol`），和 Roslyn 读出的 .NET 枚举成员一个样子：`Element.Fire`、`ConsoleColor.Red` 都绑定成 `BoundFieldAccess`，`ConstantValue` 是底层类型的值。
  - case 的值第一次被问到时才算（见下面的 `ConstantEvaluator`）：写了值的，按底层类型作期望的类型绑定那个表达式，结果是整数常量或者本 enum 的常量（`Layer.Ground | Layer.Water`），值放得进底层类型就行。这同 C#：`byte` 的 enum 里 `case A = 1 << 3` 可以，虽然 `1 << 3` 是 `int`。没写值的按位置从 0 数。第 4 遍之后（所有成员的签名都有了）`Declarer.CheckCaseValues` 把每个 case 的值算一遍，同时查 NYX2056。
  - 成员：函数、静态函数、计算属性同 class，`self` 的类型是 enum 本身。带 `set` 的属性还报 NYX2001（C# 那边是 extension 属性，给它赋值要另外降级）。enum 里的 `Main` 不算程序入口：extension 成员当不了入口。
  - `static let`（ADR-0045）同 class 里的，别的字段 Parser 已经报了（NYX1206）。类型是这个 enum、值是常量的是**别名**（`IsCaseName`）：和 case 一样能写在 `is` 后面、写在 `==` 一边报 NYX2055；它不是 case（`Cases` 里没有它），不参与 NYX2056。值不是常量的写在 `is` 后面报 NYX2082。
- **运算**（`MethodBinder.Constants.cs` 的 `ResolveEnumBinary`）：同一个 enum 的两个值 `==` / `!=`，`E?` 同 `int?` 提升（ADR-0041）；`<` `>` `<=` `>=` 按底层的值比，`E?` 不能比（ADR-0045）；`&`、`|`、`^`、`~` 只给 flags enum 和 .NET 的枚举，Nyxel 的普通 enum 报 NYX2084。enum 和整数或 enum 之间的算术、移位报 NYX2087（`ReportEnumArithmetic`，.NET 的枚举也算），消息给出 `as int` 的写法。`"e: " + e` 是字符串拼接，在 enum 的分支之前处理。
- **`x is 类型.Case`**：`BindIs` 先看 `is` 后面的限定名，左边是 enum、右边是它的常量时（`CaseNamedBy`），绑定成 `BoundIsCase`，降级成 C# 的常量模式。它不收窄（ADR-0024）。`==` / `!=` 的一边在语法上是成员访问、绑定出来是 case 或别名时报 NYX2055（`ReportCaseComparedWithEquals`，ADR-0045 起两边都查）。
- **`as`**：enum 和整数类型（不含 `char`）之间是 `ExplicitEnum` 转换，两边都是 `T?` 时也行（ADR-0014）。
- **数组**：Nyxel 的非 flags enum 没有值为 0 的 case 时，`new array<E>(n)` 报 NYX2085；有 case 的值本身出了错时不报，免得连带。
- **常量折叠**（ADR-0038）：`BoundExpression.ConstantValue` 同 Roslyn，字面量、常量字段、enum case，以及它们的一元、二元运算和数值 / enum 转换都有值。`MakeUnary`、`MakeBinary`、`MakeConversion` 建节点时调 `ConstantFolder`：
  - 规则同 C# 的常量表达式，默认 checked：整数溢出（含 `int.MinValue / -1`、`-int.MinValue`）、`decimal` 溢出、显式转换放不下（`300 as byte`、`1e20 as int`、NaN）报 NYX2024；整数和 `decimal` 除以常量零报 NYX2025，左边不是常量也报（C# 的 CS0020）。移位的位数按宽度取低 5 / 6 位，左移溢出不报；浮点按 IEEE 算，得到无穷不报。
  - 只有 `string + string` 和字符串的 `==` / `!=` 是常量，`"a" + 1` 不是；`T?`、装箱、引用转换不是常量。都同 C#。
  - `EnumAndConstantTests.ConstantsFoldAsInCSharp` 把同一个表达式写成 Nyxel 和 C#，和 Roslyn 的 `GetConstantValue`、有没有错误逐个对照。
  - 降级仍然写原来的表达式，C# 自己再折叠一遍，结果相同。只有 enum case 的值写成算好的数。
- **`static let` 常量**：`SourceFieldSymbol.IsConst` 是静态、`let`、有初始值、类型能 `const`（数字、`bool`、`char`、`string`、`decimal`、enum），而且初始值折叠出了值。`ConstantEvaluator`（`BinderContext.Constants`）第一次被问到时绑定这个初始值并留下，第 5 遍直接用留下的结果，诊断只报一次。
  - 被问到的时机：读这个字段的 `BoundFieldAccess.ConstantValue`，也就是别的常量运算、case 的值、默认参数值用到它的时候。符号经 `IConstantValues` 接口问，`Symbols/` 不依赖 `Binding/`。
  - 初始值经过别的常量又用到自己时，再次进入的那一次得到“不是常量”，整个环于是都是 `static readonly`，按 C# 的规则依次初始化，不报错（C# 的 `static readonly` 同样允许）。只有 enum case 成环报 NYX2083：case 的值必须是常量。
- **默认参数值**：第 4 遍只查顺序（NYX2086），值在第 5 遍和函数体一起绑定（`BindDefaultValue`，存进 `BoundProgram.DefaultValues`）。同 C#，可以是常量、`T?` 的 `null`、放进 `T?` 的常量、C# struct 的全零 `new S()`；最后一种要选中的构造函数是 Roslyn 给元数据里的 struct 补的那个（`IsImplicitlyDeclared`），struct 自己声明的无参构造函数会执行代码，当不了默认值。其余报 NYX2082，引用了不是常量的 `static let` 时说出是哪一个。

### 带数据的 enum（2026-11-12，ADR-0024、ADR-0039、ADR-0046）

- **类型**：`struct enum` 的 `TypeKind` 是 `Struct`，`class enum` 的是 `Class`，同它们在 C# 里的样子，值类型、引用类型的规则（赋值复制、`T?`、null 检查）因此照常适用；`SourceNamedTypeSymbol.IsDataEnum` 标出它们，`IsDeclaredEnum` 标出四种 enum。基类同 Nyxel 的 struct 和 class。冒号后面的接口还报 NYX2001。
- **case**：同不带数据的 enum，是 `SourceEnumCaseSymbol`，类型是 enum 本身；不是常量（`IsConst` 只对 `TypeKind.Enum`）。带数据的 case 的数据项（`Items`）是参数符号，第 4 遍声明，规则同元组类型的元素：写默认值、`ref` 报 NYX2001，重名报 NYX2031。`struct enum` 的数据项放在值里，`CheckStructLayout` 一并查“包含自己”（NYX2079）。
- **值**：
  - `AiState.Idle`：成员访问找到不带数据的 case，绑定成 `BoundEnumCase`（没有实参）；带数据的报 NYX2089，消息列出数据项。
  - `new Damage.Burn(12, 3)`：`BindObjectCreation` 先看限定名（`BindCreated`），左边是 `struct enum` / `class enum`、右边是带数据的 case 时，把数据项当参数走一遍重载决议（只有一个候选，`ParametersOf` 认 case），按位置或带名字，规则同调用的实参，绑定成 `BoundEnumCase`。`new 某个 enum.不带数据的 case()` 报 NYX2088，普通 enum 和 .NET 的枚举也一样。`new AiState()` 报 NYX2041。
  - `hit.Amount`：找不到成员时，名字是某个 case 的数据项就报 NYX2090，给出 `match` 的写法（数据项名转成 camelCase 当局部变量名，`Naming.CamelCase`）。
- **`is`**：`CaseNamedBy` 认 `struct enum` / `class enum` 的 case，绑定成 `BoundIsCase`，同普通 enum。`static let` 的值不是常量，写在 `is` 后面照旧报 NYX2082。
- **`==`**：`class enum` 是引用类型，走引用类型的 `==`（降级出的 record 比内容）；`struct enum` 由 `ResolveStructEnumEquality` 处理，`E?` 同 `int?` 提升。一边是不带数据的 case 名时报 NYX2055（`IsEnumCase` 认 `BoundEnumCase`）。`<` 这类运算符报 NYX2021。
- **数组**：`struct enum` 的第一个 case 不带数据时全零就是它，`new array<E>(n)` 可以；否则、以及 `class enum`，报 NYX2064（`ZeroIsValid`）。
- **成员**：同不带数据的 enum（ADR-0029）。`self` 的类型是 enum 本身；`struct enum` 的函数同 struct 的 `self` 函数，不能改值。enum 里的 `Main` 不算程序入口，四种 enum 都一样。
- 还没做：`match` 取数据（随 `match` 一步做）；接口；`struct enum` 当参数的默认值（ADR-0029 留下的）；别的程序集里 Nyxel 编出的带数据 enum（读元数据时按 C# 的 union / closed 类认出来，M3）。

和设计不同、或者还没做到的：

- 被 lambda 捕获的 `var` 不收窄（ADR-0042 决策 2）还没实现：Binder 还不支持 lambda，随“lambda 和泛型推断”一步做，做法见“流分析”。
- 元数据的类型只看 public 的；嵌套类型、泛型方法、扩展方法、事件、`params` 的展开形式还不支持。
- C# 的 `[NotNullWhen]` 这类特性不读：`if string.IsNullOrEmpty(s) { return }` 之后 `s` 仍是 `string?`。收窄只来自 ADR 列的几种写法。
- `match` 的 `case null`、`case is T` 收窄随 `match` 一步做；`match` 里两个 .NET 枚举的别名 case 都列出来时报重复（ADR-0040），`case` 标签引用常量（ADR-0038），也随 `match` 做。
- 带数据的 enum 里，case 名和它自己的数据项同名（`case Score(Score: int)`）、和 union 的成员同名（`Value`、`HasValue`、`TryGetValue`），或者 enum 自己的成员叫这些名字时，生成的 C# 编不过（C# 的成员不能和所在类型同名），按 NYX2053 内部错误出来。要么改名规则，要么补一条 Nyxel 自己的检查，碰到再定。
- 公开的成员用了 `internal` 的类型（`public func F(e: Element)`，`Element` 没写 `public`）还没有检查，到 Roslyn 报 CS0051，按 NYX2053 内部错误出来。
- struct 的 `==` 还不能用（NYX2021）：ADR-0030 的“struct 比内容”要编译器生成 `Equals` / `GetHashCode` / `==`，随运算符一步做。`self = 新值`（在 `var self` 函数里整个换掉）照旧报 NYX2028。

## 写设计时定下的语言规则

第一条线会碰到、前几轮还没定的两条，和用户讨论后定在 ADR-0036：

- **程序入口**：输出可执行程序时，从任意一个类型里的 `static func Main` 开始执行，同 C#（定这条时写成“没有 `self` 的 `Main`”，ADR-0039 后要写 `static`）。在第 4 遍找：没有、两个以上、签名不对都报错。
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
