# 诊断

依据：ADR-0003（诊断说清在哪、为什么、怎么改；工具输出机器可读）、ADR-0019（错误码格式）。错误码在 `src/Nyxel.Compiler/Diagnostics/DiagnosticDescriptors.cs` 里定义，本文列出全部错误码；新增错误码时两边一起改（DiagnosticCatalogTests 会检查每个错误码都写进了本文）。

## 格式

```
Slime.nyxel(12,21): error NYX1103: '+' can't end a line; move it to the start of the next line.
```

- MSBuild 的标准格式 `路径(行,列): error 错误码: 消息`，编辑器和 CI 的问题匹配器能直接识别。
- 行和列从 1 开始，列按 UTF-16 码元数（和 `#line`、LSP 一致，见 debug-mapping.md）。一个 Tab 算一列。
- 位置是问题开始的地方。“缺了 X”的错误：缺的东西所在行还有别的记号时指向那个记号（“Expected ')', found 'x'”），否则指向上一个记号的末尾，也就是缺东西的那一行行尾。
- 消息用英文。一句话说清问题；能给出正确写法的直接写出来。

## 错误码

`NYX` 加四位数字，一经发布不再改变含义，删掉的也不重用（ADR-0019）。

| 范围 | 阶段 |
|---|---|
| `NYX1xxx` | 词法和语法 |
| `NYX2xxx` | 语义分析 |

- 段内按添加顺序编号：新错误码取这一段当前最大的号加一。号里不带类别，类别看下面分开的表（词法、语法、其他语言的写法）。
- 早期的号按类别分过段（1001 起词法、1101 起语法、1150 起其他语言的写法），留下的空号不再补。

### 词法

| 错误码 | 什么时候 | 例子 |
|---|---|---|
| `NYX1001` | 不认识的字符 | `a # b` |
| `NYX1002` | 字符串、字符字面量、单行原始字符串到行尾还没闭合 | `"abc`、`'a` |
| `NYX1003` | 不合法的转义序列 | `"a\qb"` |
| `NYX1004` | 数字带类型后缀（ADR-0007） | `2.5f`、`10L` |
| `NYX1005` | 数字写法不对 | `1_`、`0x` |
| `NYX1006` | 整数超出 `ulong` | `18446744073709551616` |
| `NYX1007` | 块注释（ADR-0007 没有块注释） | `/* ... */` |
| `NYX1008` | 逐字字符串（ADR-0020：改用原始字符串） | `@"C:\x"` 写成 `"""C:\x"""` |
| `NYX1009` | 字符字面量不是正好一个字符（ADR-0020） | `''`、`'ab'` |
| `NYX1010` | 小数点开头的数字 | `.5` 写成 `0.5` |
| `NYX1011` | 插值字符串的 `{` 没有闭合 | `$"a {b` |
| `NYX1012` | 插值字符串里单独的 `}` | `$"a } b"` 写成 `}}` |
| `NYX1013` | 写了 C# 的 `..`（ADR-0010 只有 `..<` 和 `...`）。由 Parser 报，切片到末尾的 `a..` 另见 `NYX1170` | `0..5`、`items[..3]` |
| `NYX1014` | 多行原始字符串到文件末尾还没闭合 | |
| `NYX1015` | 原始字符串里连续的引号不少于开头的引号 | `"""a""""` |
| `NYX1016` | 多行原始字符串的某一行不以结尾 `"""` 前的空白开头 | |
| `NYX1017` | 多行原始字符串没有内容行 | `"""` 换行 `"""`；空字符串写 `""` |
| `NYX1018` | 插值原始字符串的文字里有它容不下的大括号 | `$"""{{x}}"""`；文字里要 `{` 就用 `$$"""` |
| `NYX1019` | 插值原始字符串的插值没在同一行用足大括号闭合 | `$$"""{{x}"""` |
| `NYX1020` | 普通字符串前写了多个 `$` | `$$"x"` |

### 语法

| 错误码 | 什么时候 | 例子 |
|---|---|---|
| `NYX1101` | 缺了某个记号、表达式、类型或声明 | `F(a, b` 换行后没有 `)` |
| `NYX1102` | 不该出现的记号 | lambda 参数里的 `self`、前面没有 `if` 的 `else` |
| `NYX1103` | 运算符（含 `.`、`?.`）写在行尾，括号里也一样（ADR-0019） | `a +` 换行 `b` |
| `NYX1104` | `{` 不在前面内容的同一行（ADR-0013） | `if a` 换行 `{` |
| `NYX1105` | `else` / `catch` / `finally` 不在 `}` 的同一行 | `}` 换行 `else {` |
| `NYX1106` | 一行写了两条语句、两个成员、两个分支或两个访问器 | `let a = 1 let b = 2` |
| `NYX1107` | 一行以不能续行的记号开头（ADR-0013 的续行规则） | `let a` 换行 `= 1` |
| `NYX1108` | 修饰符顺序不对，消息给出正确顺序（ADR-0019） | `static public` |
| `NYX1109` | 同一个修饰符写了两次 | `public public` |
| `NYX1110` | `namespace` 不在文件开头（ADR-0012） | `import` 之后的 `namespace` |
| `NYX1111` | 一个文件有两个 `namespace` | |
| `NYX1112` | `import` 写在声明之后 | |
| `NYX1113` | 文件顶层写了类型以外的成员 | 顶层的 `func` |
| `NYX1114` | 类型里声明类型（ADR-0020） | `class` 里的 `enum` |
| `NYX1115` | 字段没写类型（ADR-0006） | `var hp = 100` 写成 `var hp: int = 100` |
| `NYX1116` | 函数、事件、enum case 的参数没写类型 | `func F(a)` |
| `NYX1117` | `self` 不是第一个参数 | `func F(a: int, self)` |
| `NYX1118` | 参数写了 `var` / `let`（ADR-0014，只有 `var self`） | `func F(var a: int)` |
| `NYX1119` | `self` 写了类型 | `func F(self: C)` |
| `NYX1120` | `try` 后面没有 `catch` 也没有 `finally`（ADR-0014） | |
| `NYX1121` | 区间连写（ADR-0019） | `0..<1..<2` |
| `NYX1122` | 在要值的地方赋值（ADR-0011） | `if a = b`、`a = b = c` |
| `NYX1123` | `->` 右边直接写语句，没有花括号（ADR-0009） | `case 1 -> return` |
| `NYX1124` | 元组类型的元素没有名字（ADR-0021），每个元组报一次 | `(int, int)` 写成 `(Min: int, Max: int)` |
| `NYX1125` | 元组、解构只有一个元素 | `(A: int)`、`let (a) = x` |
| `NYX1126` | 解构里写了类型或嵌套的括号（ADR-0021 只接受名字） | `let (a: int, b) = x`、`let ((a, b), c) = x` |
| `NYX1127` | 区间写在 `for`、`case`、切片以外的地方（ADR-0022：区间不是值） | `let r = 0..<10`、`F(1...3)` |
| `NYX1128` | `for` 里的区间后面接了 `Reversed()` / `StepBy(n)` 以外的成员 | `(0..<n).Reverse()` |
| `NYX1129` | 切片以外的区间省略了一边，或切片两边都省略 | `for i in 2...`、`case ..<10`、`items[...]` |
| `NYX1175` | enum 的底层类型不是一个整数类型（ADR-0024） | `enum E : float`、`enum E : byte, int` |
| `NYX1176` | 带数据的 enum 写了底层类型、case 的值或 `flags` | `enum E : int { case A(X: int) }` |
| `NYX1177` | 有的 case 写了值、有的没写，每个没写的报一次 | `case A = 1` 之后的 `case B` |
| `NYX1178` | flags enum 的 case 没写值，每个报一次 | `flags enum E { case A }` |
| `NYX1179` | `struct enum` 没有带数据的 case | `struct enum E { case A }` 写成 `enum E` |

### 其他语言的写法

这些写法在 C# 等语言里合法，Agent 和 C# 程序员最容易写出来。每条都给出 Nyxel 的写法，Parser 按正确写法继续往下读，不连带报别的错。

| 错误码 | 写法 | 应该写成 |
|---|---|---|
| `NYX1150` | `&&`、`\|\|`、`!x` | `and`、`or`、`not x`（ADR-0010） |
| `NYX1151` | `i++`、`--i` | `i += 1`、`i -= 1`（ADR-0010） |
| `NYX1152` | `=>` | `match` 分支用 `->`，lambda 写 `func(x) = 表达式`（ADR-0009、0012） |
| `NYX1153` | 行尾的 `;` | 删掉（ADR-0005） |
| `NYX1154` | `(int)x` | `x as int`（ADR-0014） |
| `NYX1155` | `F(count: 3)`、`(Min: 1, Max: 2)` | `F(count = 3)`、`(Min = 1, Max = 2)`（ADR-0011、0021） |
| `NYX1156` | `x is Enemy e` | `x is Enemy`，`x` 自己被收窄（ADR-0010） |
| `NYX1157` | `a ? b : c` | `if a { b } else { c }`（ADR-0007） |
| `NYX1158` | `x!` | `x ?? throw new 异常("原因")`（ADR-0009） |
| `NYX1159` | `using var r = ...` | `using r = ...`（ADR-0015） |
| `NYX1160` | `using (...) { }` | `using r = ...`，在所在块结束时释放（ADR-0015） |
| `NYX1161` | 文件开头的 `using System` | `import System`（ADR-0012） |
| `NYX1162` | `for (var i = 0; ...; ...)`、`for (x in xs)` | `for i in 0..<n`、`for x in xs`（ADR-0010） |
| `NYX1163` | `case 1, 2` | `case 1 or 2`（ADR-0010） |
| `NYX1164` | `int[]`、`int[,]` | `array<int>`、`array2d<int>`（ADR-0020、0022） |
| `NYX1165` | `new Enemy[10]`、`new Tile[w, h]` | `new array<Enemy>(10)`、`new array2d<Tile>(w, h)`（ADR-0020、0022） |
| `NYX1166` | 语句位置单独的 `{ ... }` | 拆成函数（ADR-0020） |
| `NYX1167` | `new T { ... }`、`new int[] { 1, 2 }` 这类初始化器 | 集合字面量 `[1, 2]`，或创建后再给字段赋值（ADR-0022） |
| `NYX1168` | 元组类型 `(int Min, int Max)` | `(Min: int, Max: int)`（ADR-0021） |
| `NYX1169` | 循环标签 `outer: for`、`break outer`、`continue outer` | 拆成函数，用 `return` 跳出（ADR-0021） |
| `NYX1170` | 切片到末尾 `name[1..]`（也报 `name[1..<]`） | `name[1...]`（ADR-0022） |
| `NYX1171` | 从末尾数 `items[^1]` | `items[items.Count - 1]`，数组和字符串用 `Length`（ADR-0022） |
| `NYX1172` | 集合字面量里的展开 `[..a, ..b]`、`[...a]`，每个字面量报一次 | 先创建，再 `AddRange`（ADR-0022） |
| `NYX1173` | 字典字面量 `["a": 1]`、`["a" = 1]`，每个字面量报一次 | 先创建，再 `d[key] = value`（ADR-0022） |
| `NYX1174` | 超过三维的数组 `int[,,,]`、`new int[1, 2, 3, 4]` | 最多 `array3d<T>`（ADR-0022） |
| `NYX1180` | `x is Damage.Burn(var a, var s)` | `x is Damage.Burn` 只判断；取数据写 `match`（ADR-0024） |
| `NYX1181` | enum 上面的 `[Flags]` | `flags enum`（ADR-0024） |

## 不在语法阶段报的

语法分析只管“写法对不对”。下面这些要知道上下文或类型，留给语义分析（M2）：修饰符能不能用在某种声明上（`async` 只能用在函数上、struct 成员不能 `protected`）、没有 `self` 的函数写了 `static`、访问器有的带函数体有的不带、接口成员带了函数体、表达式能不能当语句、`match` 是否穷尽、集合字面量有没有期望的类型、数组元素类型全零是否合法（ADR-0023）、enum case 的值是不是常量、位运算是不是用在 flags enum 上、`is` 后面是类型还是 case、取 case 数据时名字的个数（ADR-0024）等。区间能写在哪里（`NYX1127`–`NYX1129`）虽然要看所在的位置，但只看语法树，在整棵树建好以后由 Parser 检查。
