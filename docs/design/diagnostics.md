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
| `NYX1103` | 运算符（含 `.`、`?.`、`with`）写在行尾，括号里也一样（ADR-0019、ADR-0027） | `a +` 换行 `b` |
| `NYX1104` | `{` 不在前面内容的同一行（ADR-0013） | `if a` 换行 `{` |
| `NYX1105` | `{ }` 形式的 `else` / `catch` / `finally` 不在 `}` 的同一行（`->` 形式可以写在下一行开头，ADR-0025） | `}` 换行 `else {` |
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
| `NYX1123` | `match` 分支 `->` 右边直接写 `let`、`using`、循环、`if` / `try` 语句（ADR-0025：当语句用时简单语句和块可以直接写），或当值用时写了 `emit` 等语句；消息按用法说明 | `case 1 -> let a = 2` |
| `NYX1124` | 元组类型的元素没有名字（ADR-0021），每个元组报一次 | `(int, int)` 写成 `(Min: int, Max: int)` |
| `NYX1125` | 元组、解构只有一个元素 | `(A: int)`、`let (a) = x` |
| `NYX1126` | 解构里写了类型或嵌套的括号（ADR-0021 只接受名字） | `let (a: int, b) = x`、`let ((a, b), c) = x` |
| `NYX1127` | 区间写在 `for`、`case`、切片以外的地方（ADR-0022：区间不是值） | `let r = 0..<10`、`F(1...3)` |
| `NYX1128` | `for` 里的区间后面接了 `Reversed()` / `StepBy(n)` 以外的成员 | `(0..<n).Reverse()` |
| `NYX1129` | 切片以外的区间省略了一边，或切片两边都省略 | `for i in 2...`、`case ..<10`、`items[...]` |
| `NYX1175` | enum 的底层类型不是一个整数类型（ADR-0024） | `enum E : float`、`enum E : byte, int` |
| `NYX1176` | 带数据的 enum 或 `struct enum` 写了底层类型、case 的值或 `flags`（ADR-0029：它们冒号后面是接口） | `enum E : int { case A(X: int) }`、`struct enum E : IFoo { case A = 1 }` |
| `NYX1177` | 有的 case 写了值、有的没写，每个没写的报一次 | `case A = 1` 之后的 `case B` |
| `NYX1178` | flags enum 的 case 没写值，每个报一次 | `flags enum E { case A }` |
| `NYX1179` | `struct enum` 既没有带数据的 case，也没写接口（ADR-0029） | `struct enum E { case A }` 写成 `enum E` |
| `NYX1182` | `eval` 块单独当语句（ADR-0025） | `eval { ... }` 写成 `let x = eval { ... }` |
| `NYX1183` | `eval` 外面的 `give`，包括 `eval` 里的 lambda 里的 | 顶层的 `give 1`、`b ?? give 0` |
| `NYX1184` | `eval` 块里的 `return`（lambda 里的不算） | 让块给出 `null`，在块后面写 `?? return` |
| `NYX1185` | `eval` 块里的 `break` / `continue` 要离开块（块里自己的循环不算） | `for` 里的 `eval { if a { break } ... }` |
| `NYX1186` | `eval` 块是空的，或者最后一句是没有 `give` 的表达式（其他走到末尾的路径在语义分析查） | `eval { ...; r * 2 }` 写成 `give r * 2` |
| `NYX1188` | `->` 形式的 `if` / `try` 单独当语句 | `if a -> F() else -> G()` 写成 `if a { F() } else { G() }` |
| `NYX1189` | 同一个 `if` / `try` 的分支两种形式混用 | `if a { } else -> 1`、`if b -> 1 else { 2 }` |
| `NYX1190` | 当值用的 `match` 有 `{ }` 块分支 | `case 1 -> { ... }` 写成 `case 1 -> eval { ... give 值 }` |
| `NYX1191` | `->` 形式的 `if` / `try` 当运算数，或直接写在另一个 `if` / `try` 的分支里（`else if` 不算） | `1 + if b -> 1 else -> 2`、`if a -> if b -> 1 else -> 2 else -> 3`，加括号 |
| `NYX1192` | `->` 形式的 `if` 没有 `else` | `if b -> 1` |
| `NYX1194` | `_` 用作名字（ADR-0026：它是保留记号） | `let _ = 1`、`for _ in xs` |
| `NYX1195` | `_` 用作值 | `let a = _`、`F(_)`；只有 `out _` 可以 |
| `NYX1196` | 成员上写了 `private`（ADR-0026：默认就是私有，只有访问器写 `private set`） | `private var x: int = 0` |
| `NYX1197` | setter 写了表达式体（ADR-0026：只有 getter 能写 `get = 表达式`） | `set(value) = x` |
| `NYX1198` | `with (...)` 不在 `new 类型(...)` 后面（ADR-0027：复制已有的值再改几个成员还是待定） | `self.pos with (Y = 0)` |
| `NYX1199` | `with (...)` 里的一项没写成员名 | `new Spin() with (2)` 写成 `with (Speed = 2)` |
| `NYX1201` | 索引器没有参数（ADR-0029） | `property self[]: int` |
| `NYX1202` | 索引器写了初始值、`static`，或写在扩展里（C# 14 的 extension 块不支持） | `property self[i: int]: int = 0 { get }`、`static property self[...]` |
| `NYX1203` | 事件或索引器的参数写了默认值，索引器的参数是 `out` / `ref` | `event Died(slime: Slime = null)`、`property self[i: out int]` |
| `NYX1205` | enum 的 case 写在成员后面（ADR-0029：先写全部 case） | `func F(self) { }` 之后的 `case A` |
| `NYX1206` | enum 里写了字段、`init`、事件、索引器或自动属性 | case 后面的 `var x: int = 0` |
| `NYX1207` | enum 的成员写了 `protected`、`virtual`、`abstract`、`sealed`、`override` | `public override func ToString(self)` |
| `NYX1208` | enum 的函数写了 `var self` | `func F(var self)` 写成 `func F(self)` |
| `NYX1209` | 不带数据的 `enum` 写了接口（C# 的 enum 不能实现接口） | `enum E : IFoo { case A }` 写成 `struct enum E : IFoo` |

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
| `NYX1157` | `a ? b : c` | `if a -> b else -> c`（ADR-0007、0025） |
| `NYX1158` | `x!` | `x ?? throw new 异常("原因")`（ADR-0009） |
| `NYX1159` | `using var r = ...` | `using r = ...`（ADR-0015） |
| `NYX1160` | `using (...) { }` | `using r = ...`，在所在块结束时释放（ADR-0015） |
| `NYX1161` | 文件开头的 `using System` | `import System`（ADR-0012） |
| `NYX1162` | `for (var i = 0; ...; ...)`、`for (x in xs)` | `for i in 0..<n`、`for x in xs`（ADR-0010） |
| `NYX1163` | `case 1, 2` | `case 1 or 2`（ADR-0010） |
| `NYX1164` | `int[]`、`int[,]` | `array<int>`、`array2d<int>`（ADR-0020、0022） |
| `NYX1165` | `new Enemy[10]`、`new Tile[w, h]` | `new array<Enemy>(10)`、`new array2d<Tile>(w, h)`（ADR-0020、0022） |
| `NYX1166` | 语句位置单独的 `{ ... }` | 算一个值用 `let x = eval { ... give 值 }`（ADR-0025），否则拆成函数（ADR-0020） |
| `NYX1167` | `new T { X = 1 }`、`new List<int> { 1, 2 }`、`new int[] { 1, 2 }` 这类初始化器 | 设成员写 `new T() with (X = 1)`（ADR-0027），带元素创建用集合字面量 `[1, 2]`（ADR-0022）；消息按花括号里的内容给出其中一种 |
| `NYX1168` | 元组类型 `(int Min, int Max)` | `(Min: int, Max: int)`（ADR-0021） |
| `NYX1169` | 循环标签 `outer: for`、`break outer`、`continue outer` | 拆成函数，用 `return` 跳出（ADR-0021） |
| `NYX1170` | 切片到末尾 `name[1..]`（也报 `name[1..<]`） | `name[1...]`（ADR-0022） |
| `NYX1171` | 从末尾数 `items[^1]` | `items[items.Count - 1]`，数组和字符串用 `Length`（ADR-0022） |
| `NYX1172` | 集合字面量里的展开 `[..a, ..b]`、`[...a]`，每个字面量报一次 | 先创建，再 `AddRange`（ADR-0022） |
| `NYX1173` | 字典字面量 `["a": 1]`、`["a" = 1]`，每个字面量报一次 | 先创建，再 `d[key] = value`（ADR-0022） |
| `NYX1174` | 超过三维的数组 `int[,,,]`、`new int[1, 2, 3, 4]` | 最多 `array3d<T>`（ADR-0022） |
| `NYX1180` | `x is Damage.Burn(var a, var s)` | `x is Damage.Burn` 只判断；取数据写 `match`（ADR-0024） |
| `NYX1181` | enum 上面的 `[Flags]` | `flags enum`（ADR-0024） |
| `NYX1187` | 当值用的 `if` / `try` 写 `{ }` 分支（Rust、Kotlin），或 `->` 后面写 `{ }` | `if c -> a else -> b`、`try -> a catch e: E -> b`；要先执行语句用 `eval`（ADR-0025） |
| `NYX1193` | `match` 的默认分支写 `else`（Kotlin） | `case _ -> ...`（ADR-0026） |
| `NYX1200` | `super.init(...)`、`self.init(...)`（Swift，也是 ADR-0013 的旧写法），包括 `super.init.Name(...)` | `init super(...)`、`init self(...)`、`init super.Name(...)`（ADR-0027） |
| `NYX1204` | 事件的访问器 `event Clicked() { add(h) { ... } remove(h) { ... } }`，整个 `{ }` 跳过 | 订阅要转发的事件，在处理函数里 `emit` 自己的事件（ADR-0029） |

## 不在语法阶段报的

语法分析只管“写法对不对”。下面这些要知道上下文或类型，留给语义分析（M2）：修饰符能不能用在某种声明上（`async` 只能用在函数上、struct 成员不能 `protected`）、没有 `self` 的函数写了 `static`、访问器有的带函数体有的不带、接口成员带了函数体、表达式能不能当语句、`match` 是否穷尽、集合字面量有没有期望的类型、数组元素类型全零是否合法（ADR-0023）、enum case 的值是不是常量、位运算是不是用在 flags enum 上、`is` 后面是类型还是 case、取 case 数据时名字的个数（ADR-0024）、`eval` 块的每条路径是否都以 `give` / `throw` 结束（ADR-0025）、`init super` / `init self` 是不是 `init` 的第一句、`with` 里的成员能不能设、`required` 成员是否都设了、命名 init 不继承（ADR-0027）、索引器的访问器带不带体、一个类型最多一个索引器、带数据的 enum 和 `struct enum` 冒号后面是不是接口、实现接口的事件参数和委托是否一致（ADR-0029）等。区间能写在哪里（`NYX1127`–`NYX1129`）、`give` / `return` / `break` / `continue` 和 `eval` 块的关系（`NYX1183`–`NYX1186`）虽然要看所在的位置，但只看语法树，在整棵树建好以后由 Parser 检查。
