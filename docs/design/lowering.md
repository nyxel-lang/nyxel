# 降级与后端

编译流水线的第三、四段（architecture-overview.md）：绑定树 → C# 源码 → Roslyn → dll + portable PDB。代码在 `src/Nyxel.Compiler/Lowering/`（`Lowerer`、`CSharpWriter`）和 `NyxelCompilation.Emit`。本文是工程设计，随实现更新；`#line` 的规则以 debug-mapping.md 为准。

## 用法

```csharp
compilation.EmitCSharp()            // 降级出的 C#；有错误时是 null
compilation.Emit(peStream, pdbStream)   // NyxelEmitResult：Success、Diagnostics
```

命令行：

- `nyxel build [--exe] [--emit-cs] [-o file.dll] <file>...`：写 dll 和 pdb，`--exe` 时再写 `runtimeconfig.json`（`dotnet file.dll` 能直接跑）；`--emit-cs` 只打印 C#。
- `nyxel run <file>... [-- <args>]`：在内存里编译成可执行程序，加载到可卸载的 `AssemblyLoadContext`，调用 `Main`（ADR-0036）。退出码是 `Main` 的返回值；有编译错误或者 `Main` 没接住的异常时是 1。
- 源文件的路径转成绝对路径再交给编译器：它会写进 `#line`、PDB 和异常堆栈（debug-mapping.md 规则 6）。
- M2 的 CLI 引用运行中的 .NET 的共享框架（`TRUSTED_PLATFORM_ASSEMBLIES` 里运行时目录下的程序集）；项目自己的引用在 M3 由 Nyxel.Sdk 传进来。

## 生成 C# 文本，再交给 Roslyn

ADR-0004 留下的选择：用列跟踪的文本写出器生成 C#，再 `CSharpSyntaxTree.ParseText`，不用 SyntaxFactory 构造语法树。原因是 `#line` 的列偏移要知道每条语句生成在第几列（debug-mapping.md 规则 1、4），文本写出器天然知道。

- `CSharpWriter` 一行一行地写，记着缩进。
  - `WriteMapped(源码, 跨度, 行)`：先写 `#line (行, 列) - (行, 列) 列偏移 "路径"`，列偏移是这一行第一个字符的列，再写这一行。
  - `WriteHidden(行)`：没有 Nyxel 对应的脚手架代码（命名空间、类型、方法的声明、块的大括号），前面是 `#line hidden`，连续的隐藏行只写一次。
  - 跨度指令只管到下一条指令为止，所以每一行要么前面有指令，要么在 `#line hidden` 下面。
- 文件开头是 `#nullable disable` 和 `#pragma warning disable`：生成代码的警告对用户没有意义；可空标注随“空值”那一步再开。

## 翻译规则

- **一条 Nyxel 语句一行 C#**，映射到这条语句的跨度。`if` / `while` / `for` 映射的是开头到条件（集合）结束的那一段，块的大括号是隐藏的。
- **函数的 `{` 和 `}` 也映射**（规则 5），断点能停在函数入口和出口；没有大括号的表达式体函数和自动生成的 `init` 没有映射。
- **名字**：
  - 类型一律写全名，`global::System.Collections.Generic.List<int>`，内置类型写 C# 关键字。这样 C# 的名字查找不用做任何决定，Nyxel 的查找规则（ADR-0033）和 C# 的差别不会影响结果。
  - 静态成员写在类型全名后面，实例成员写在接收者后面，`self` 是 `this`。
  - Nyxel 的名字是 C# 关键字时（`base`、`params`）加 `@`。
  - 局部变量照原名写，只有 C# 的作用域规则不允许时改名（`PlanLocalNames`）：C# 里一个块和它里面的块不能有同名的局部变量，先后都不行（CS0136）；Nyxel 只看声明处看得见的名字（ADR-0036），`if c { let a = 1 }` 后面可以再 `let a = 2`，`case Burn(amount, _)` 后面可以再 `let amount`。后声明的那个改成 `a_2` 这类函数里没有的名字（也避开参数）；并列的块之间照旧同名。
- **成员**：`let` 字段是 `readonly`；`static func` 是 `static`；`init` 是构造函数；class 自动生成的无参 `init` 不写，C# 的默认构造函数就是它。可见性照写：私有是 `private`，类型默认是 `internal`；`init` 不写修饰符是 `public`（ADR-0040）。
- **struct**（ADR-0008）：C# 的 `struct`。`self` 函数是 `readonly` 方法，`var self` 函数是普通方法。自动生成的无参 `init` 写成隐藏的 `public S() { }`：C# 只在写出来的构造函数里执行 struct 的字段初始值，有初始值时也必须写一个（CS8983）。
- **属性**（ADR-0011、ADR-0044）：
  - 自动属性一行，映射到声明的开头到初始值（或类型）结束：`public int Score { get; private set; } = 0;`。只有 `get` 的是 `{ get; }`，`init` 里的 `this.Health = hp` 由 C# 写进背后的字段。
  - 计算属性的访问器写成块，`get = 表达式` 同表达式体函数（`return` 一行，大括号不映射）；块的大括号和语句照函数映射。
  - 访问器上的可见性只在比属性窄时写（`private set`）。
  - struct 的 `get` 是 `readonly`：只有 `get` 时 C# 要求写在属性上（`public readonly bool IsDead`，CS8664），有 `set` 时写在 `get` 上（`readonly get`）。
  - `set(v)` 的新值在 C# 里叫 `value`：名字不是 `value` 时，setter 开头加一行隐藏的 `int v = value;`。这个 setter 里叫 `value` 的局部变量和 C# 的撞名（CS0136），同样改名（`value_2`）。
- **不带数据的 enum**（ADR-0024、ADR-0029）：C# 的 `enum`，flags enum 加 `[global::System.Flags]`，底层类型不是 `int` 时写出来。每个 case 一行、映射到 case 的声明，值总是写成算好的数（`Solid = 3,`），C# 不用再按自己的规则算一遍。
  - enum 里的函数和属性放进同一命名空间、同样可见性的 `static class 名字Extensions`：实例成员在 `extension(global::Game.Element self)` 块里，静态成员在 `extension(global::Game.Element)` 块里。C# 代码照样写 `element.Weakness()`、`Element.Parse("ice")`。
  - 这些成员里的 `self` 写成接收者 `self`（Nyxel 的名字不会是 `self`，撞不了名）。
  - Nyxel 代码调用它们一律经过这个类，写成 C# 为 extension 成员生成的静态方法：`global::Game.ElementExtensions.Weakness(e)`、`ElementExtensions.get_IsHot(e)`、`ElementExtensions.Parse("ice")`。因为生成的 C# 不写 `using`，extension 不在作用域里；而且 C# 查 `Element.Parse` 时先找到 `System.Enum.Parse`，轮不到扩展。
  - enum 里的 `static let`（ADR-0045）是这个类的字段，值是常量时是 `const`；非私有的再在静态的 extension 块里加一个同名属性（`public static Tile Gate => global::Game.TileExtensions.Gate;`），C# 代码 `using` 了命名空间后写 `Tile.Gate`，`switch` 标签这种要常量的地方写 `TileExtensions.Gate`。Nyxel 代码读它写 `global::Game.TileExtensions.Gate`。
  - 带数据的 enum（ADR-0046，`LowerDataEnum`）：每个 case 是嵌套的 record 类型，数据是只读属性，构造函数的参数是 camelCase（`Naming.CamelCase`，带名字的实参也按它写），另有 `Deconstruct` 和 `ToString`（`Burn(Amount = {...}, Seconds = {...})`，值用 C# 的插值转成文字）。enum 自己的成员写在外层类型上。case 的声明行映射到嵌套类型那一行。
    - `class enum` 是 `closed record class`，case 是 `sealed record class`。不带数据的 case 共用一个对象，放在外层的 `internal static readonly` 字段 `__nyxel_名字` 里，Nyxel 的 `Condition.Always` 读它。`x is Condition.Not` 是 C# 的类型模式。
    - `struct enum` 是 `[Union] readonly record struct`，实现 `IUnion`。case 是 `readonly record struct`。标记是 `internal readonly byte __nyxel_Tag`（case 的序号，所以全零是第一个 case），数据放在 `private readonly` 的 `__nyxel_0`、`__nyxel_1`…里：按 case 的顺序分配，同一个 C# 类型的字段在不同 case 之间共用。每个 case 一个构造函数、一个 `TryGetValue`，加上 `Value`、`HasValue`、`ToString`。相等由 record struct 按全部字段生成，没用到的字段始终是零。
    - Nyxel 创建 `struct enum` 的值写成 `new global::Game.AiState(new global::Game.AiState.Chasing(target))`。
    - `x is AiState.Chasing`：C# 15 把 union 上的模式都当成“是哪个 case 类型”（`x is AiState { ... }` 是 CS8121），所以值的类型就是这个 enum 时写成 `x.__nyxel_Tag == 1`；`AiState?` 和 `object` 调生成的 `internal static bool __nyxel_Is(AiState? value, int tag)`，`object` 先 `as AiState?`。
    - 生成的程序集用到的 `UnionAttribute`、`IUnion`、`IsClosedTypeAttribute`，引用里没有时在 `namespace System.Runtime.CompilerServices` 里声明成 `internal`（`DeclareMissingAttributes`，ADR-0047）。Roslyn 用 `LanguageVersion.Preview` 编译生成的 C#（Roslyn 5.9 把这两个功能当预览版）。
  - `x is Element.Fire` 是 C# 的常量模式 `x is global::Game.Element.Fire`，对 `Element?` 和 `object` 的值也是同样的意思；别名写成 `x is global::Game.TileExtensions.Gate`。
- **常量**（ADR-0038）：值是常量的 `static let` 写成 `const`（不写 `static`、`readonly`），初始值照原来的表达式写，C# 折叠出的值和 Binder 的相同。
- **默认参数值**：照表达式写在参数后面（`int count = global::Game.Spawner.DefaultCount`）。`null` 写成 `null`，放进 `int?` 的常量写成常量本身（`int? n = 3`）：C# 在这里只认常量、`null` 和 `new S()`，`(int?)3` 不算常量。
- **调用**：实参按源码顺序写，源码里带名字的照样带名字（`point: x`），省略的可选参数交给 C# 填。C# 也按源码顺序求值，和 Nyxel 一致。
- **转换**：数值转换（包括隐式的，比如 `int` 到 `float`）一律写成强制转换 `(float)x`，这样 C# 选的运算和重载一定是 Binder 选的那个；引用转换、装箱不写。`as` 是 `(T)x`，enum 和整数之间也是（`(int)e`、`(global::Game.Element)2`）。
- **字面量**：按绑定后的类型写后缀：`1u`、`1L`、`0.5f`（`R` 格式，往返精确）、`0.5d`、`0.5m`，`byte` 等写成 `(byte)200`；负数加括号。字面量算式在绑定时已经折叠成一个值，`let x: float = 1.5 * 2` 生成 `3f`。
- **运算符**按 C# 的优先级只在需要时加括号；`- -x` 写成 `-(-x)`，不会变成 `--x`。`and` / `or` 是 `&&` / `||`。
- **插值字符串**是 C# 的 `$"..."`；每个洞的表达式外面加括号，避免 `global::` 里的 `:` 被当成格式的开头。
- **`for i in a..<b`**：
  ```csharp
  for (int __nyxel_i1 = a, __nyxel_end2 = b;
  __nyxel_i1 < __nyxel_end2;
  __nyxel_i1++)
  {
      int i = __nyxel_i1;
      { 循环体 }
  }
  ```
  - 两端只求一次值，同 Nyxel。
  - 循环变量每次迭代是一个新的变量，以后有了 lambda，捕获它的闭包看到的是这一次的值。
  - 头部三段各占一行、各带指令（规则 2），每段都是自己的序列点。
  - 临时变量以 `__nyxel_` 开头。
- **`for x in 集合`** 是 C# 的 `foreach`，头部同样分两行。
- **`T?`**：值类型的是 `int?`（`Nullable<int>`）；引用类型的就是 `T` 本身，生成的 C# 关着可空标注（`#nullable disable`）。值转成 `int?` 和 `null` 转成 `T?` 都写成强制转换 `(int?)x`、`(string)null`，同数值转换：C# 选重载时 `null` 没有类型。
- **收窄后的读取**：Binder 给读到的局部变量、参数记上收窄后的类型，和声明的类型不同时写成强制转换：`((int)value) + 1`、`((global::Game.Pickup)other).Collect()`。引用类型从 `T?` 收窄成 `T` 是同一个 C# 类型，不写。赋值的左边用声明的类型，不转换。
- **和 null 有关的写法**：`x == null` / `x != null` 是 `x is null` / `x is not null`（不调用重载的 `==`，ADR-0023）；`x is T` 照写；`??`、`??=`、`a ?? throw e` 照写。
- **`?.`**：Binder 把 `a?.B.C()` 绑成“接收者 `a`”加“在一个占位的接收者上做 `.B.C()`”，降级时占位的接收者写成空，正好拼成 C# 的 `a?.B.C()`，短路的范围和 C# 一样。
- **`?? return` / `?? break` / `?? continue`**：C# 不能在表达式中间离开，所以先检查、再取值，两行都映射到这条语句：
  ```csharp
  if (!(this.FindTarget() is {} __nyxel_value1)) return;
  global::Game.Enemy target = __nyxel_value1;
  ```
  Binder 只允许它们出现在整条语句的值上（`let` 的初始值、`=` 的右边，外面最多套着隐式转换；`a ?? b ?? return` 也行），其余位置报 NYX2001。`?? throw` 在哪都直接写成 C# 的 `?? throw`。
- **`match`**（ADR-0009、ADR-0010、ADR-0024，`Lowerer.Match.cs`）：
  - 当语句用的是 C# 的 `switch` 语句：头部映射到 `match 值`，每个分支一个 section，`case` 标签映射到 `case 模式 when 条件`，分支能走到结尾时加隐藏的 `break;`。最后没有 `case _` 时加 `default: throw new global::System.Runtime.CompilerServices.SwitchExpressionException(值);`：列全了 case 的 match 碰到不是任何 case 的值（`5 as Element`）时抛出，同 C# 的 switch 表达式（ADR-0024）；值是局部变量、参数、`self` 时带上它，别的不再求一次值。没有条件的 `case _` 写成 `default:`。
  - 当值用的是 C# 的 switch 表达式，写在一行里（`x switch { A => 1, _ => 2 }`）。没有分支匹配时 C# 自己抛 `SwitchExpressionException`。分支的值是引用转换或装箱时写成强制转换：C# 按各分支定 switch 表达式的类型，不写的话由派生类的分支决定。`throw` 分支照写。
  - 分支里有 `return` / `break` / `continue` 的 `let t = match ...`（Binder 只允许整条语句的值，同 `?? return`）：先声明一个隐藏的临时变量，再写成 switch 语句，每个分支给它赋值或者离开，这条语句再用这个临时变量。
  - Nyxel 的 `break` 出现在 switch 语句里时，C# 的 `break` 只会离开 switch，所以写成 `goto __nyxel_break1;`，标签放在循环后面（只在用到时写出来）；`?? break` 同样。`continue` 照写。
  - 模式：`case _` 是 `_`，`null`、`not null` 照写；常量照写，数字不带强制转换（`5` 对 `byte` 也行，C# 自己转换）；不带数据的 enum 的 case 是常量模式，别名是 `TileExtensions.Gate`；`struct enum` / `class enum` 的 case 是 case 类型的模式，取数据走 `Deconstruct`：`global::Game.Damage.Burn(var amount, _)`（ADR-0046，`struct enum` 是 C# 15 的 union 模式）；`case is T` 是类型模式，值本来就是 `T` 时写成 `not null`（值类型写成 `_`），因为 union 上的类型模式测的是 case 的类型；`or` 照写。
  - C# 会把被前面的分支覆盖了的分支报成错误（CS8510、CS8120），Binder 先报（NYX2092），只是比较、区间、`and`、别的 `not` 的覆盖关系 Binder 不算。所以这几种不写成 C# 的模式，而是 `var __nyxel_match1 when __nyxel_match1 is >= 3 and <= 9`：C# 不检查 `when` 里的东西。区间 `a..<b` 写成 `>= a and < b`。

## 后端

- 降级出的 C# 加进 `SymbolTable.References` 那个只有引用的 `CSharpCompilation`（语义分析读元数据用的同一个对象），输出类型是库或者可执行程序。
- PDB 是 portable 格式，所有 .nyxel 文件按 UTF-8 嵌入，同时写 SHA-256 校验和，调试器不用到磁盘上找源码。
  - 嵌入的是编码后的文本。如果原文件带 BOM，校验和会和磁盘上的文件不一致，等 CLI 改成读原始字节时再修。
- **Roslyn 的诊断是兜底**（ADR-0004）：生成的 C# 编译不过时，每个错误用 `GetMappedLineSpan()` 映射回 .nyxel，按 NYX2053（编译器内部错误）报出，消息里带上 Roslyn 的错误码和消息（`InvariantCulture`）。出现一次就说明 Binder 漏了一项检查。

## 测试

- tests/Nyxel.Compiler.Tests/Programs/：每个 `.nyxel` 程序配一个期望输出 `.out` 和降级出的 C# 快照 `.g.cs`。
  - `ProgramTests` 编译、在进程里运行（重定向 `Console`，用不变的区域设置），比较输出。
  - 快照逐字比较；`#line` 里本机的绝对路径换成文件名。降级有意改变后，用 `NYXEL_UPDATE_SNAPSHOTS=1` 重新生成，看 diff 再提交。
  - 这个目录不参与测试项目的编译（csproj 里 `Compile Remove`）。
- 异常堆栈指向 .nyxel 的行：`AnExceptionPointsAtTheNyxelLine`。
- 命令行的 `build` / `run`：tests/Nyxel.Cli.Tests。

## 已知的缺口

- `for i in a...b` 在 `b` 是类型的最大值时会无限循环（C# 的 `<=` 加一溢出），以后改成先判断再加一。
- `__nyxel_` 临时变量会出现在调试器的变量面板里（debug-mapping.md“在调试器里”）。
- 当值用的 `match` 写成一行 C# 的 switch 表达式，断点只能停在整条语句上，不能停在某个分支上。
