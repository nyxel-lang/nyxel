# 语法树与语法分析

编译流水线的第一段（architecture-overview.md）：`SourceText` → Lexer → Parser → `SyntaxTree`（含语法诊断）。代码在 `src/Nyxel.Compiler/Syntax/`，诊断的格式和错误码见 diagnostics.md。本文是工程设计，不涉及语言规则；语言规则以 language-reference 和 ADR 为准。

## 用法

```csharp
var tree = SyntaxTree.Parse(SourceText.From(text, "Slime.nyxel"));
tree.Root          // CompilationUnitSyntax
tree.Diagnostics   // 词法和语法诊断，按位置排序
SyntaxTreePrinter.Print(tree.Root, tree.Text)   // 缩进文本
```

命令行：`nyxel parse [--tree] <文件>...` 打印诊断（`--tree` 再打印语法树），有错误时退出码为 1。

## 无损

树覆盖源文件的每一个字符：空白、换行、注释、写错被跳过的记号都挂在某个记号上，`tree.Root.ToFullString()` 逐字还原源文件。以后的格式化器（nyxelfmt）和语言服务器要靠这一点：它们要在保留注释的前提下改写代码，并把位置准确映射回源文件。

- **记号**（`SyntaxToken`）：种类、文本、位置（不含 trivia 的起点）、值（数字是 `ulong` / `double`，字符串是解码后的 `string`，字符字面量是 `char`）、前导和尾随 trivia。记号也是树的叶子节点，所以遍历子节点只有一种写法。
- **trivia**（`SyntaxTrivia`）：空白、换行、`//` 注释、`///` 文档注释、跳过的记号、（报错的）块注释。
- 归属规则：一个记号的**尾随** trivia 到行尾为止，不含换行；换行和下一行开头的空白、注释都是**下一个**记号的前导 trivia。所以行尾注释属于这一行，声明前的 `///` 文档注释在这个声明第一个记号（通常是修饰符或关键字）的前导 trivia 里，语义分析从那里取。
- 节点的 `Span` 不含首尾 trivia，`FullSpan` 含。行列（`LinePosition`）从 0 开始存，显示时加 1；列是 UTF-16 码元，和 `#line`、LSP 一致。

## 节点

- 树是不可变的；每个节点知道自己的 `Parent`；`GetChildren()` 按源码顺序给出子节点和记号。节点类是手写的（`DeclarationSyntax.cs`、`StatementSyntax.cs`、`ExpressionSyntax.cs`、`TypeSyntax.cs`、`PatternSyntax.cs`），种类在 `SyntaxKind`。
- 名字既是表达式也是类型：`TypeSyntax` 继承 `ExpressionSyntax`（同 Roslyn）。`Slime.Killed` 里的 `Slime` 和 `hp: Slime` 里的 `Slime` 是同一种节点，是类型还是值由语义分析决定。
- `if`、`match`、`try` 是表达式（ADR-0007、0014），当语句用时包在 `ExpressionStatementSyntax` 里。`return` / `throw` / `break` / `continue` 是语句，只在 `??` 右边是表达式（`JumpExpressionSyntax`）。
- 赋值（含 `+=` 等）是语句 `AssignmentStatementSyntax`，不是表达式（ADR-0011）。
- 模式：`case Burn(_, seconds)` 这种不带点的名字解析成 `CasePatternSyntax`（enum case 和按位置取出数据的名字，ADR-0024），带点的名字和字面量是 `ConstantPatternSyntax`，`10..<50` 是 `RangePatternSyntax`。名字到底是不是被匹配的 enum 的 case 由语义分析判断。
- `x is SlimeState.Idle` 和 `x is Enemy` 是同一种 `IsExpressionSyntax`，`is` 后面是类型还是 enum case 由语义分析判断（ADR-0024）。
- enum（ADR-0024）：`EnumDeclarationSyntax.KindKeyword` 是 `enum` 前面的 `flags` 或 `struct`；底层类型复用 `BaseListSyntax`（同 Roslyn）；case 的值是 `EqualsValueClauseSyntax`。只看声明就能查的规则（底层类型、值、flags 只用于不带数据的 enum，值要么都写要么都不写等）在声明读完后检查。
- `get` / `set` 只在属性的访问器列表里是关键字，`flags` 只在同一行紧跟 `enum` 时是关键字，别处都是普通名字；`self.init(...)` / `super.init(...)` 的 `init` 作为成员名。
- 写错时缺的记号是 `IsMissing` 的零宽记号，放在上一个记号的末尾。

## 行规则的实现

ADR-0013 的续行规则和 ADR-0019 的“运算符一律放行首”由 Parser 实现，Lexer 不产生换行记号：换行是 trivia，每个记号只记一个 `HasLeadingLineBreak`（前面有没有换行）。ADR-0013“后果”里设想的是 Lexer 产生换行记号、Parser 按规则跳过，效果相同。改成标记是因为 Parser 知道自己在不在括号里、在哪种结构里：括号没闭合时，Parser 能在下一行的语句关键字处停下来只报一个错，而在 Lexer 里数括号会把后面整个文件都当成括号里。

- **括号里**（圆括号、方括号，Parser 里的 `_bracketDepth > 0`）换行不起作用。括号里的块（lambda 体）重新有行规则：进入块时深度清零，出来时恢复。
- **别处**，在新一行上的记号会结束正在解析的结构，除非这里语法允许换行：语句或成员的开头、块的 `}`、赋值运算符（`=`、`+=` 等）和 `->` 之后（ADR-0020：行尾的赋值运算符和 `->` 接着下一行）。
- **行首的二元运算符**（包括 `and`、`or`、`??`、`is`、`as`、区间）**和 `.`、`?.`** 接着上一行：表达式解析读运算符时不看换行。语句不会以二元运算符开头（ADR-0013），所以不会误判。`(` 和 `[` 在行首时不是调用和下标，而是新语句的开头。
- **运算符在行尾**：读完运算符后，如果下一个记号在新的一行，报 NYX1103（括号里也报），然后照样从下一行读操作数，因为几乎一定是这个意思。
- **`{` 在下一行**、**`else` / `catch` / `finally` 在下一行**：报 NYX1104 / NYX1105，但仍然归属到前面的结构：没有别的结构能以它们开头，这样恢复不会产生连锁错误。
- **语句结束**：一条语句（成员、`case`、访问器同样）之后必须是换行、`}` 或文件末尾。同一行还有东西时报 NYX1106 并跳到行尾；这条语句已经报过错时只跳不报。

## 表达式

优先级爬升（precedence climbing）：读一个一元表达式，然后循环读二元运算符，运算符优先级高于当前层才往右结合。优先级表在 `SyntaxFacts.GetBinaryPrecedence`，从低到高：`??`（右结合）< `or` < `and` < `|` < `^` < `&` < `==` `!=` < `<` `>` `<=` `>=` `is` < 区间（不结合）< `<<` `>>` < `+` `-` < `*` `/` `%` < `as` < 一元 < 后缀（ADR-0019）。

- **泛型的 `<`**（ADR-0012：按 C# 的规则）：表达式里 `名字<` 后面先试着扫描一个类型实参列表（不建节点），扫描成功、并且 `>` 后面是 `( ) ] } : , . ?. == != | ^ & [ and or ??`、换行或文件末尾之一时，才当作泛型。所以 `World.Spawn<Slime>()` 是泛型调用，`a < b and c > d` 是两个比较。C# 的已知歧义 `F(a < b, c > (d))` 和 C# 一样读成泛型调用。
- **`>>`**：Lexer 只产生单个 `>`，这样 `List<List<int>>` 能关闭两层；两个 `>` 紧挨着（中间没有任何字符）时 Parser 把它们拼成 `>>` / `>>=`。
- **插值字符串**：Lexer 用一个模式栈把 `$"HP: {self.hp,8:F2}"` 切成开头、文本、`{`、表达式的记号、`,`、`:`、格式文本、`}`、结尾；嵌套的插值字符串各自压栈。洞里的 `:` 只在不在括号里时才开始格式（`{list.Max(func(e: Enemy) = e.Hp)}` 里的 `:` 不会误判；Nyxel 也没有 `?:`）。洞没闭合就到了行尾时，Lexer 报错并补上零宽的 `}` 和结尾，Parser 不再重复报。
- **原始字符串**（ADR-0020，同 C# 11）：
  - 不插值的是一个 `StringLiteralToken`，文本是整个字面量（含引号和缩进）。
  - `$"""` / `$$"""` 走同一个模式栈。模式记下 `$` 的个数，开、关一个洞都要这么多个大括号，所以开头和结尾记号的文本是 `{{` / `}}`。
  - 多行字符串的缩进要读到结尾的 `"""` 才知道。所以文本记号先按原文记下，字符串关闭时再统一算出每段的值，并检查每一行的缩进。
  - 多行插值字符串里，洞没闭合就到了行尾时只补 `}`，字符串本身继续读下一行。
  - 规则和 Roslyn 逐条对照过，`CSharpLiteralTests` 把同一个字面量交给两边比较。
- **数组类型**：`array<T>`、`array2d<T>`、`array3d<T>` 都是 `ArrayTypeSyntax`，`Rank` 从关键字得出。C# 的 `T[]`、`T[,]`、`new T[n]`、`new T[w, h]` 报错后读成同一种节点，关键字、`<`、`>` 是零宽的缺失记号（关键字按逗号数取 `array` / `array2d` / `array3d`）；`new T[n]` 的长度留在方括号的实参列表里。这样语义分析看到的仍是数组类型，不会连带报类型错误。
- **集合字面量**（ADR-0022）：操作数开头的 `[` 是 `CollectionExpressionSyntax`，元素是表达式。C# 的展开 `..xs` 和字典项的 `: 值` / `= 值` 报错后进 trivia，元素照常留下。`[` 在行首时开始新语句，不是上一行的下标。
- **区间和切片**（ADR-0022）：
  - `RangeExpressionSyntax` 的两边都可以为 null：操作数开头的 `..<` / `...` 是省略开头的区间，运算符后面没有能开始表达式的记号（`]`、`,`、`{` 等）时省略结尾。
  - C# 的 `..` 是单独的记号 `DotDotToken`（Lexer 不报错），在区间、`case` 区间的位置照常读。
  - 区间能写在哪里要看它的父节点，读区间时还不知道，所以在整棵树建好以后检查（`Parser.Ranges.cs`）：去掉外层括号后，是下标 `x[...]` 的实参，或者（经过 `.Reversed()` / `.StepBy(n)` 调用）是 `for` 的集合；其余位置报“区间不是值”。省略哪一边、`..` 该怎么改也在这里报，因为消息取决于位置：`name[1..]` 提示 `name[1...]`，`0..10` 提示 `..<` / `...`。
  - C# 的 `^1`：报错后跳过 `^`，读后面的操作数。
- **元组**（ADR-0021）：
  - 类型位置的 `(` 一定是元组类型 `TupleTypeSyntax`，Nyxel 没有加括号的类型。C# 的 `(int Min, ...)` 报错后跳过名字；没写名字的元素和它一样，名字和冒号是零宽的缺失记号。
  - 表达式里的 `(` 后面在同一层括号里有逗号时是元组值 `TupleExpressionSyntax`（先往后扫描到配对的 `)`，不建节点），否则是加括号的表达式。元素复用 `ArgumentSyntax`（没有 `out` / `ref`）。`(a, b) = (b, a)` 是目标为元组值的普通赋值语句。
  - `let (a, b) = ...` 是单独的 `DeconstructionDeclarationStatementSyntax`；`for (i, item) in` 的 `ForStatementSyntax` 用 `Deconstruction` 代替 `Identifier`。两处共用 `DeconstructionSyntax`（括号里的名字）。
  - `for (` 后面先扫描：配对的 `)` 后面紧跟 `in` 才是解构，其他（`for (var i = 0; ...)`、`for (x in xs)`）报 C 风格 `for`。

## 错误恢复

Parser 从不因为输入而抛异常，树总是覆盖整个文件：

- 缺了必需的记号：报错，插入零宽的缺失记号，继续。
- 多出来的记号：跳过（变成下一个记号前面的 `SkippedTokensTrivia`），跳过括号时连同配对的另一半一起跳。
- 同一位置只报一个错；一条语句已经有错时，行尾剩下的东西只跳不报。
- 其他语言的写法（diagnostics.md 里单独一张表）报错后按 Nyxel 的写法继续：`&&` 当 `and`，`(int)x` 跳过转换部分读 `x`，`case 1, 2` 当 `case 1 or 2`，`int[]` 当 `array<int>`，`@"..."` 照 C# 的规则读完（不再为其中的 `\` 报错），`outer: for` 跳过标签读循环，`items[^1]` 跳过 `^`，`[Flags]` 跳过后读下面的 enum，`x is Damage.Burn(a, s)` 跳过括号。
- 写在错误位置的 `namespace` / `import` 照常解析、报错，然后整个挪进 trivia，树的结构保持“namespace → import → 声明”的固定顺序。
- `SampleTests.MangledSamplesNeverThrowAndStayLossless` 对每个样例做 200 轮随机的删字、复制、交换字符，检查不抛异常、仍然无损。

语法分析只管写法；要知道上下文的规则（修饰符能不能用在这里、访问器的形式是否一致、表达式能不能当语句等）留给语义分析，清单见 diagnostics.md 末尾。

## 测试

tests/Nyxel.Compiler.Tests/Syntax/：

- `LexerTests`：记号、数字和字符串的值、插值字符串的切分、trivia 归属。
- `CSharpLiteralTests`：字符字面量和原始字符串交给 Nyxel 和 Roslyn 各解析一遍，值和“是否报错”必须一致。用例包括手写的边界情况，以及 3000 个随机生成的原始字符串（引号、`$`、缩进、空白行、换行符随机组合；固定种子）。
- `ParserTests`：用紧凑的 S 表达式（`(AddExpression a + (MultiplyExpression b * c))`）核对树的形状，覆盖优先级、续行、各种声明和语句；以及父子关系和跨度的一致性。
- `ParserErrorTests`：故意写错的代码和它得到的错误码、行列；每个错误码至少一个例子。
- `SampleTests`：samples/ 全部零诊断、无损，语法树和 `Syntax/Snapshots/*.tree` 逐字相同。树的形状有意改变后，设环境变量 `NYXEL_UPDATE_SNAPSHOTS=1` 跑一次测试重新生成快照，再看 diff。

## 以后

- 语言服务器需要时再考虑增量解析和 red/green 树（Roslyn 的做法：不可变的“绿”节点共享，按需包一层带父指针和绝对位置的“红”节点）。现在的树简单、一次建好，够 M1 / M2 用。
- 节点类现在是手写的；节点多到维护困难时，改成从一个定义文件生成。
