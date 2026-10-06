# ADR-0021: 元组、解构、遍历时拿下标、没有循环标签

- 状态：已接受
- 日期：2026-10-06

## 背景

第十六轮讨论，开始逐个讨论 language-reference 各节的“待定”，这一轮是循环一节的“遍历时同时拿下标”和“带标签的 `break`”。

C# 的下标遍历和字典遍历都靠元组解构：`foreach (var (i, x) in list.Index())`、`foreach (var (key, value) in dict)`。元组本身一直没讨论过，调用 .NET API 也绕不开它：`Math.DivRem` 返回 `(Quotient, Remainder)`，`Enumerable.Index()`、`Zip()` 返回元组。所以先定元组，再定遍历。五项都和用户对比了代码，按推荐定。

## 决策

1. **元组就是 .NET 的 `ValueTuple`**。
   - 值类型，不分配，每帧用也没问题。
   - 自己的函数可以返回元组，参数、字段、泛型实参也可以是元组类型。
   - 指导（不强制）：公开 API、要长期存放的、元素超过三个的，仍然定义 struct。
2. **写法按“冒号后面是类型，等号后面是值”（ADR-0011）**，和参数、实参是同一套。
   - 类型写 `(Min: int, Max: int)`。
     - 每个元素都要有名字，用 PascalCase：元素相当于公开字段，.NET 自己的 API 也这样（`Quotient`、`Index`）。
     - `(int, int)` 和 C# 的 `(int Min, int Max)` 是编译错误。
     - 至少两个元素。
   - 值写 `(lo, hi)`（按位置）或 `(Min = lo, Max = hi)`（带名字）。C# 的 `(Min: lo, Max: hi)` 是编译错误。
   - 按位置写的值，名字来自期望的类型：返回类型、参数类型、声明的类型。
     - 没有期望的类型时要带名字：`let r = (lo, hi)` 是编译错误，写 `let r = (Min = lo, Max = hi)`。
     - 直接被解构的值不需要名字：`(a, b) = (b, a)`。
   - 元素用名字访问：`r.Min`。`Item1`、`Item2` 只用于 C# 那边没起名字的元组。
   - `==`、`!=` 同 C#，逐个比较元素。
   - 元素名写进程序集的元数据，C# 调用方看到同样的名字。
   ```nyxel
   func MinMax(values: List<int>) -> (Min: int, Max: int) {
       ...
       return (lo, hi)
   }
   ```
3. **解构按位置，同 C#**：第几个变量拿第几个元素。
   - `let (min, max) = 值`、`var (a, b) = 值`；`_` 丢弃不要的元素。
   - 给已有的变量赋值：`(a, b) = (b, a)`。
   - 能解构的是元组和有 `Deconstruct` 方法的类型（如 `KeyValuePair`），同 C#。
   - 变量名和元素名是同一组名字（不分大小写）、顺序却不同时，给警告。这是写反时最典型的样子：
     ```nyxel
     // Stats() -> (Total: int, Count: int)
     let (count, total) = self.Stats()    // 警告：count 拿到的是 Total
     ```
   - 解构里只写名字，见“后果”里的待定项。
4. **`for` 里可以解构**：`for (key, value) in dict`。
   - 同时拿下标用 .NET 9 的 `Enumerable.Index()`，和 C# 13 的写法相同：
     ```nyxel
     for (i, item) in self.inventory.Index() {
         self.slots[i].Show(item)
     }
     ```
   - 编译器把 `for (...) in x.Index()` 翻译成计数器加普通遍历，不分配。结果和 `Index()` 一样，遍历途中修改 List 照样报错。
   - 只要下标、或者要边遍历边改元素时，仍然写 `for i in 0..<list.Count`。
5. **没有循环标签**：`outer: for`、`break outer`、`continue outer` 都是编译错误。
   - 要从里层循环直接跳出外层，就把循环拆成函数，用 `return` 跳出。
   - 和 ADR-0020 对单独的块的处理是同一个思路。诊断给出这个改法。

## 备选方案

- **元组：只能用 C# 给的**（能解构、能取元素，自己的函数不能声明元组类型）。坏处：每个返回两个值的私有辅助函数都得配一个 struct，正是 ADR-0003 要避免的胶水代码。
- **元组：没有**。坏处：C# 返回的元组只能 `.Item1`、`.Item2`，下标遍历和字典遍历都没有好写法。
- **元素名可写可不写（C#）**。坏处：`-> (int, int)` 看签名不知道哪个是哪个。enum case 的数据也要求每一项有名字（ADR-0008），这里保持一致。
- **解构按名字，顺序无关**（同 `match` 取 case 数据，ADR-0009）。
  - 好处：不会写反。
  - 坏处：C# 给的名字只能照抄，`for (Index, Item) in list.Index()`、`for (key, value) in dict` 里的名字都由对方定；没名字的元组没法解构。
  - 按位置解构加上“顺序相反就警告”，能抓住最典型的写反。
- **不解构，只用 `r.Min` 取**。坏处：下标遍历和字典遍历仍然没有好写法。
- **下标：`for i, item in list`（Go）**。
  - 好处：短，不需要元组。
  - 坏处：`for key, value in dict` 读起来像“键和值”，实际是“下标和键值对”。键是 `int` 的字典还能编译通过，只是意思错了。
- **下标：不加**，只用 `for i in 0..<list.Count`。坏处：没有下标的集合（LINQ 的结果）只能自己维护计数器，`continue` 时容易漏加。
- **循环标签：`outer: for` + `break outer`（Java、Swift、Go）**。坏处：Nyxel 里“名字 + 冒号”后面是类型，`outer: for` 像在声明一个变量。
- **循环标签：`outer@ for` + `break@outer`（Kotlin）**。坏处：多一套符号，用处又少。C# 本身也没有带标签的 `break`（只有 `goto`），C# 程序员习惯拆函数或用标志变量。

## 后果

- 下面几项还没定，记在 language-reference 的“待定”里：
  - `match` 里的元组模式（C# 的 `case (0, 0)`）。
  - 嵌套解构 `let ((a, b), c) = ...`、解构时写类型 `let (a: int, b) = ...`：目前是编译错误。
  - 重写或实现 .NET 里元组元素没有名字的成员（C# 不允许重写时改元素名）：M3 做互操作时定。
- 分工：
  - Parser：元组类型、元组值、解构、各种错误写法。
  - 语义分析（M2）：没有期望的类型时要带名字、顺序相反的警告、`Item1` 只用于无名元组。
  - 降级（M2）：`Index()` 翻译成计数器。
- 错误码见 design/diagnostics.md。
