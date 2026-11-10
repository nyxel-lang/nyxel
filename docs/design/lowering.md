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
- **成员**：`let` 字段是 `readonly`；`static func` 是 `static`；`init` 是构造函数；自动生成的无参 `init` 不写，C# 的默认构造函数就是它。可见性照写：私有是 `private`，类型默认是 `internal`；`init` 不写修饰符是 `public`（ADR-0040）。
- **调用**：实参按源码顺序写，源码里带名字的照样带名字（`point: x`），省略的可选参数交给 C# 填。C# 也按源码顺序求值，和 Nyxel 一致。
- **转换**：数值转换（包括隐式的，比如 `int` 到 `float`）一律写成强制转换 `(float)x`，这样 C# 选的运算和重载一定是 Binder 选的那个；引用转换、装箱不写。`as` 是 `(T)x`。
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
