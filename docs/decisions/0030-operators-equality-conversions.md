# ADR-0030: 运算符 `operator +`、struct 的 `==` 比内容、转换 `operator as`

- 状态：已接受
- 日期：2026-10-30

## 背景

第二十三轮讨论，“待定”的第五轮第三批。ADR-0029 那一轮发现两个从没讨论过的空缺：

- 运算符重载：自己的类型定义 `a + b`。
- 自定义转换：C# 的 `implicit` / `explicit operator`。

相关的一件也放了进来：自己的 struct 和 class 怎么用 `==`。带数据的 enum 和 `struct enum` 已经按内容比（ADR-0024），普通 struct 还没定。

查了 EnginePlayground：170 个 .cs 文件里，自己定义的运算符和转换一个都没有；System.Numerics 的向量、矩阵运算符用得很多（`axis - n * Vector3.Dot(n, axis)`）。这些是 .NET 定义的，Nyxel 照 C# 用（ADR-0010）。这一批定的是 Nyxel 自己的类型能不能也这样。

用 Roslyn 5.9（C# 14）验证过：

- 运算符必须 `public static`（CS0558）。
- `==` 和 `!=`、`<` 和 `>`、`<=` 和 `>=` 必须成对定义（CS0216）。
- struct 没定义 `==` 时，`a == b` 编译不过（CS0019）。
- C# 14 的 extension 块里能写运算符，不能写转换（CS9282）。C# 14 还能自定义复合赋值 `operator +=`。
- 不能定义转成基类的转换（CS0553）。
- `record struct` 的 `==` 用字段的 `Equals` 比：两个 NaN 算相等。

四项都按推荐定。

## 决策

1. **自己的类型用 `operator` 定义运算符。**

   ```nyxel
   public struct Cell {
       public let X: int
       public let Y: int
       ...
       public operator +(a: Cell, b: Cell) -> Cell = new Cell(x = a.X + b.X, y = a.Y + b.Y)
       public operator *(a: Cell, k: int) -> Cell = new Cell(x = a.X * k, y = a.Y * k)
       public operator -(a: Cell) -> Cell = new Cell(x = -a.X, y = -a.Y)     // 一个参数：取负
   }

   let next = cell + dir * 2
   ```

   - 没有 `self`，按“没有 `self` 就是静态的”（ADR-0008）是静态的，同 C#：两个操作数都是参数，左边不是这个类型的 `2 * dir` 也能定义。写 `self`（NYX1216）、`static`（NYX1211）是编译错误。
   - 必须写 `public`（NYX1210）：C# 要求运算符公开，而 Nyxel 的成员不写 `public` 就是私有的，不写也公开的话读起来像私有。别的修饰符都不能写（NYX1211）。
   - 参数至少一个是这个类型（扩展里是被扩展的类型），同 C#。
   - 能定义的：`+ - * / %`、`& | ^ << >>`，一元的 `-` `+` `~`；比较只有 `<`（决策 2）。参数个数：`~` 一个，`+` `-` 一个或两个，其余两个（NYX1215）。
   - 不能定义的：`==` `!=`（决策 3，NYX1212）；`>` `<=` `>=`（决策 2，NYX1213）；`and` `or` `not`（短路求值，写成函数就没了）、`??`、`is`、区间、复合赋值、`++` 等（NYX1214）。
   - 同一个运算符可以按操作数类型写几个（`Cell * int` 和 `int * Cell`）。这是“自己声明的函数不能重载”（ADR-0011 决策 5）的例外：函数可以换个名字区分，运算符没有名字可换。
   - `a += b` 用 `+`，不单独定义。C# 14 的自定义 `operator +=` 不提供声明；C# 类型定义了的，翻译成 C# 后照样生效。
   - 能写在 class、struct 和 extension 块里（C# 14 支持扩展运算符，给引擎里缺运算符的类型补上）；enum 和 interface 里暂时不能写（NYX1218）。
   - 参数不能有默认值，不能是 `out` / `ref`（NYX1203），可以是 `in`（`Matrix4x4` 这类大 struct）。返回类型（NYX1217）和体（`= 表达式` 或块）都要写。
   - 翻译：C# 的 `public static Cell operator +(Cell a, Cell b)`。

2. **比较只写 `<`，`>`、`<=`、`>=` 由它生成。**

   ```nyxel
   public operator <(a: Fixed, b: Fixed) -> bool = a.raw < b.raw
   // a > b 是 b < a；a <= b 是 not (b < a)；a >= b 是 not (a < b)
   ```

   - `<` 返回 bool。写 `>`、`<=`、`>=` 是编译错误（NYX1213）。
   - 同时生成 `IComparable<T>`，`list.Sort()`、`Max()` 直接能用。声明了 `<` 的类型再写 `CompareTo` 是编译错误（NYX1220）。扩展里的 `<` 只生成三个运算符：扩展加不了接口。
   - 只适合任意两个值都能比出大小的类型。包着 float 的类型遇到 NaN 时，生成的 `<=` 和 float 本身的结果不同。
   - C# 那边四个运算符都有。

3. **struct 和 enum 的 `==` 比内容，class 比引用；`==` 和 `!=` 不能定义。**

   ```nyxel
   public struct Cell {
       public let X: int
       public let Y: int
       ...
   }

   if cell == goal { ... }                  // X、Y 都相等就相等
   let walls = new HashSet<Cell>()          // 能当字典的键，不装箱
   ```

   - 一句话：class 比是不是同一个对象，struct、enum、元组比内容。读到 `a == b`，只要知道 `a` 是 class 还是 struct，不用去翻类型里有没有定义 `==`。
   - struct 的字段逐个用 .NET 的 `Equals` 比（`EqualityComparer<T>.Default`），同 C# 的 `record struct`：class 类型的字段比引用（两个内容一样的 `List` 不相等），float 字段两个 NaN 算相等。带数据的 enum 同 ADR-0024 决策 7。
   - 编译器给 struct 生成 `IEquatable<T>`、`Equals`、`GetHashCode`、`==`、`!=`。字典和 `HashSet` 走 `IEquatable<T>`，不装箱；C# 那边也能用 `==`。
   - struct 和 enum 不能自己写 `Equals`、`GetHashCode`（NYX1219）。要别的比较方式（比如比名字时忽略大小写）就写普通方法 `a.SameName(b)`，以后真有需要再放开。
   - class 不能定义 `==`，一律比引用。可以照 C# 重写 `Equals` / `GetHashCode`（影响字典和 `Contains`），`==` 不变；C# 的 class 也大多如此，只有 `string` 等少数类型重载了 `==`。
   - 和 C# 不冲突：没定义 `==` 的 struct 在 C# 里写 `a == b` 本来就编译不过。

4. **转换：C# 定义的隐式转换照用；Nyxel 只能定义显式转换 `operator as`。**

   ```nyxel
   public struct Fixed {
       let raw: int
       ...
       public operator as(value: int) -> Fixed = new Fixed.FromRaw(value << 16)
       public operator as(value: Fixed) -> float = value.raw as float / 65536
   }

   let speed = 3 as Fixed
   let shown = speed as float
   ```

   - C# 类型定义的隐式转换（`string` 到 `ReadOnlySpan<char>` 等）同 C#，在需要时自动发生；否则调 .NET 的新 API 要到处写 `as`。保证它不丢信息、不失败是库作者的事，这是 .NET 的约定。
   - 声明写 `operator as(值: 源类型) -> 目标类型`，和使用处的 `值 as 目标类型` 一一对应，用 `as` 调用（ADR-0014 决策 1）。C# 那边是 `explicit operator`，C# 代码写 `(float)speed`。
   - 没有隐式转换：ADR-0003 的准则“少用隐式转换”。
   - 约定：`as` 用于同一个量换一种表示（数字之间）；创建新东西照样 `new`。
   - 源和目标有一个是这个类型；不能转成自己的基类或接口（C# 也不允许，本来就能直接当基类用）；可以按源类型写几个（决策 1）；必须 `public`；extension 里不能写（C# 14 不支持，NYX1218）。
   - C# 的写法 `public static explicit operator float(Fixed f)`、`implicit operator` 是编译错误（NYX1222），诊断按原来的参数写出 `operator as`；C# 的运算符写法同样（NYX1221）。

## 备选方案

- 运算符：
  - **不能定义，只用方法**（Java、Go）：`cell.Add(dir.Times(2))` 难读；C# 用这个类型时也没有 `+`；实现 .NET 的泛型数学接口（`IAdditionOperators` 等）要运算符。
  - **`func` 后面直接写符号**（Swift 的 `static func +`）：`func` 一般表示“按名字调用的函数”。
  - **有名字的函数对应符号**（Kotlin 的 `operator fun plus`）：要记一张对照表（`Plus` 是 `+`、`Rem` 是 `%`），读的人也要去查。
  - 选 `operator`：声明以关键字开头，搜 `operator` 能找全；和 C# 的 `operator +` 对应，Agent 熟。
- 比较：
  - **同 C#，四个都写**（成对）：Agent 熟，但四个可能互相矛盾（`<=` 误写成 `<`），编译器查不出来。
  - **实现 `IComparable<T>` 就生成运算符**（Kotlin 的 `compareTo`）：多一条“实现了接口就多出运算符”的隐藏规则；.NET 的 `string` 实现了 `IComparable<string>` 却没有 `<`，两边不一致。
- `==`：
  - **同 C#，struct 要用 `==` 就自己定义，再重写 `Equals`、`GetHashCode`**：每个要比较的 struct 写五个成员；漏了 `IEquatable<T>`，当字典的键就又慢又容易冲突（默认的 `Equals` 一般靠反射、要装箱），是游戏性能的常见坑。
  - **写了 `: IEquatable<Cell>` 才生成**（Swift 写 `: Equatable`）：多一条规则，没写时 `==` 编译不过，Agent 第一次多半会漏。
- 转换：
  - **不能定义，用 init 和方法**（`new Fixed.FromInt(3)`、`speed.ToFloat()`）：定义了 `+` 和 `<` 的像数字的类型，转换却和内置数字写法不同；C# 那边也不能 `(float)speed`。
  - **显式、隐式都能定义**（C#）：`let speed: Fixed = 3` 看不出调用了转换。
  - **C# 的隐式转换也要写 `as`**：调参数是 `ReadOnlySpan<char>` 等的 .NET API 要到处写 `as`，违背 ADR-0003 的第 3 条。

## 后果

- 修改了 ADR-0011 决策 5（运算符和转换是“不能重载”的例外）、ADR-0014 决策 1（`as` 也用 Nyxel 声明的转换）。原文已改成最新，见各自的修订记录。
- `operator` 是新关键字。
- 语法树：`OperatorDeclarationSyntax`，`Kind` 是 `OperatorDeclaration` 或 `ConversionOperatorDeclaration`（`as`）。不能定义的运算符（`==`）报错后照样留在树里；没写运算符时是缺失的记号。
- Parser 查只看声明就知道的：修饰符、能不能定义、参数个数、`self`、返回类型、体、写在哪里；struct 和 enum 的 `Equals`、`GetHashCode`；和 `<` 一起写的 `CompareTo`。C# 的运算符和转换写法报一次，整个声明跳过。新错误码 NYX1210–NYX1222，NYX1203 扩大到运算符的参数。
- 留给语义分析（M2）：参数至少一个是这个类型；转换的源和目标有一个是这个类型、不是基类或接口；`<` 返回 bool；同一个运算符的操作数类型重复；运算符的重载决议；C# 的隐式转换。
- 留给降级（M2 / M3）：struct 的 `IEquatable<T>`、`Equals`、`GetHashCode`、`==`、`!=`；由 `<` 生成的三个运算符和 `IComparable<T>`（扩展里只有运算符）。
- 样例 samples/Common/Fixed.nyxel：定点数和帧同步的移动。
- 待定：enum 和 interface 里的运算符（C# 11 的静态抽象成员，泛型数学）；自定义复合赋值 `operator +=`；struct 自定义相等。
