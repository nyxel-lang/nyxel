# ADR-0046: 带数据的 enum 在 C# 那边：`class enum` 是 closed 的 record 层次，`struct enum` 是 union，`ToString` 同创建时的写法

- 状态：已接受
- 日期：2026-11-12

## 背景

第三十九轮讨论。M2 实现完不带数据的 enum（ADR-0045），下一步是 `struct enum` / `class enum`。它们在 Nyxel 里的写法和用法早就定了（ADR-0024、ADR-0039），C# 那边（引用 Nyxel 编出的程序集、手写的 C# 代码，ADR-0029 背景里的定义）看到的是什么却一直留着：

- ADR-0024 后果：“C# 那边怎么取 `struct enum` 的数据，到时再定。”
- ADR-0029 后果：C# 那边怎么判断是哪个 case、怎么取数据。候选方向（用户提议）：内嵌一个 C# enum 列出所有 case，再加一个属性返回当前是哪个 case。
- ADR-0029 待定：带数据 enum 的 `ToString`。

讨论前两天（2026-11-10），C# 15 随 .NET 11 发布，正好加了两样东西：

- **closed 的类层次**：基类加 `closed` 修饰符（隐含 `abstract`），子类只能在同一个程序集里声明，`switch` 列全了直接子类就不用写 `_`。
- **union 类型**：“几个类型里的一个”，`switch` 同样能检查列全。可以自己写：struct 上标 `[Union]`、实现 `IUnion`（一个 `object? Value` 属性），每个 case 类型一个单参数的公开构造函数；再提供 `HasValue` 和 `TryGetValue(out T)`，`switch` 就不装箱。
- 封闭的 enum 没有进 C# 15。

探针（Roslyn 5.9 的 Preview 模式，见“后果”）验证了：在生成的程序集里自己声明 `UnionAttribute`、`IUnion`、`IsClosedTypeAttribute` 后，下面两种形状在 .NET 10 上能编过、能运行；另一个程序集里用 C# 15 写的代码 `switch` 能查出漏掉的 case；别的程序集（C# 14 也一样）不能继承 closed 的类。

三项都对比了代码，按推荐定。讨论中用户问了 C# 的 record 是什么（见决策 1 的说明），以及升到 .NET 11 要改什么（见 ADR-0047）。实现时又碰到两处 Nyxel 这边的细节，先按最严做，用户随后确认（决策 4）。

## 决策

1. **`class enum` 是 closed 的 record 层次**：抽象基类（ADR-0008）加上 `closed`，基类和每个 case 的子类都是 record class，case 的子类嵌套在基类里、`sealed`。

   ```nyxel
   class enum Condition {
       case Leaf(Check: func() -> bool)
       case Not(Inner: Condition)
       case Always
   }
   ```

   C# 那边：

   ```csharp
   public closed record class Condition
   {
       public sealed record class Leaf : Condition
       {
           public Leaf(Func<bool> check) { Check = check; }      // 构造函数的参数 camelCase（ADR-0024）
           public Func<bool> Check { get; }
           public void Deconstruct(out Func<bool> check) { ... }
           public override string ToString() => ...;              // 决策 3
       }
       public sealed record class Not : Condition { ... }
       public sealed record class Always : Condition { ... }
       // enum 里写的成员在基类上（ADR-0029）
   }

   // 手写的 C#：
   string Describe(Condition c) => c switch
   {
       Condition.Leaf => "leaf",
       Condition.Not(var inner) => "not " + Describe(inner),
       Condition.Always => "always",
   };   // 列全了，不用写 _；以后加了 case，这里会出警告
   ```

   - record 是 C# 的一种声明方式：编译器按全部字段生成 `Equals`、`GetHashCode`、`==`、`!=`、`ToString` 和给 `with` 用的复制。运行时没有“record”这种东西，编出来就是普通的类，F# 和别的 .NET 语言照常用。ADR-0024 决策 7 要的“`==` 比内容、能当字典的键”由它生成。
   - 不用 record 的一行写法（`record Leaf(Func<bool> Check)`）：那样构造函数的参数也是 PascalCase，和 ADR-0024 决策 8 不符。属性、构造函数、`Deconstruct` 照写出来。
   - 不带数据的 case（`Condition.Always`）在 Nyxel 里是值，不写 `new`：每处用到的都是同一个对象，不会每次分配。C# 那边写 `new Condition.Always()`，和它相等。
   - 别的程序集不能继承 `Condition`（CS9382）。C# 14 及更早的代码照样能用，只是 `switch` 要写 `_`。

2. **`struct enum` 是自己写的 union struct**：每个 case 一个嵌套的 `readonly record struct`，C# 判断和取数据的写法和 `class enum` 一样。

   ```nyxel
   struct enum AiState {
       case Idle
       case Chasing(Target: Entity)
       case Fleeing(From: Entity, Until: float)
   }
   ```

   C# 那边：

   ```csharp
   [Union]
   public readonly record struct AiState : IUnion
   {
       public readonly record struct Idle { ... }
       public readonly record struct Chasing { public Chasing(Entity target) { ... } public Entity Target { get; } ... }
       public readonly record struct Fleeing { ... }

       public AiState(Idle value) { ... }        // 每个 case 一个构造函数
       public AiState(Chasing value) { ... }
       public AiState(Fleeing value) { ... }
       public object? Value { get; }             // IUnion 要的，取出来会装箱
       public bool HasValue => true;
       public bool TryGetValue(out Chasing value) { ... }   // 不装箱，switch 用它
       ...
       // 存放：一个表示哪个 case 的标记，加上各 case 数据的位置（同类型的共用，ADR-0024），都是私有字段
   }

   // 手写的 C#（C# 15）：
   AiState s = new AiState.Chasing(player);
   var text = s switch
   {
       AiState.Idle => "idle",
       AiState.Chasing(var target) => $"chasing {target}",
       AiState.Fleeing f => $"fleeing until {f.Until}",
   };   // 列全了，也不分配
   ```

   - 数据只有先匹配到那个 case 才拿得到，不会读错 case。
   - `case Damage.Burn b` 这种写法对两种 enum 都成立：Nyxel 里两种 enum 用法一样、只差存放，C# 那边也是。
   - 不带数据的 case 在 C# 那边写 `new AiState.Idle()`（`AiState.Idle` 这个名字是嵌套类型）。全零的值仍是第一个 case（ADR-0024），`default(AiState)` 就是 `Idle`。
   - case 的数据是 case 类型上的属性：`Damage.Burn` 的 `Amount`，不是 `Damage` 本身的。修改 ADR-0024 决策 8 的“C# 那边是同名的属性（`hit.Amount`）”。
   - `==`、`GetHashCode` 由 record struct 按全部字段生成：标记加数据的位置。没用到的位置始终是零，所以结果就是“同一个 case、数据逐个相等”。
   - 只实现接口、不带数据的 `struct enum`（ADR-0029 决策 5）也是这个形状，C# 15 能 `switch` 它的 case。
   - C# 14 及更早的代码把它当普通 struct：用构造函数创建（`new AiState(new AiState.Chasing(p))`），用 `TryGetValue` 取数据。新的 Roslyn 在 C# 14 模式下看到 union 的写法（`AiState.Idle =>` 这种模式、从 case 类型隐式转换）会报“预览功能”。

3. **`ToString` 同 Nyxel 创建时的写法**：不带数据的 case 是名字，带数据的是 case 名加括号里的 `名字 = 值`。

   ```nyxel
   Log.Info($"{AiState.Idle}")                                  // Idle
   Log.Info($"{new Damage.Burn(Amount = 12, Seconds = 3)}")     // Burn(Amount = 12, Seconds = 3)
   Log.Info($"{new Condition.Not(Condition.Always)}")           // Not(Inner = Always)
   ```

   - 和不带数据的 enum 打印 `Fire` 一致，又和 `new Damage.Burn(Amount = 12, Seconds = 3)` 对得上。
   - 值按字符串插值的规则转成文字，数据是 enum 的照样递归打印。
   - 两种 enum 一样。C# 那边的 case 类型单独打印也是这个格式。

4. **不带数据的 case 不写 `new`，case 的数据项不能写默认值**：

   ```nyxel
   let a = AiState.Idle                     // 不带数据的 case 本身就是值
   let b = new AiState.Idle()               // 错误（NYX2088）：诊断给出 AiState.Idle

   struct enum Damage {
       case Burn(Amount: int, Seconds: float = 3)   // 错误（NYX2001）：同元组类型的元素，不能有默认值
   }
   ```

   - 同一个值只留一种写法。四种 enum 和 .NET 的枚举都一样（`new Element.Fire()` 也是错误）。C# 那边照旧 `new AiState.Idle()`（决策 2）。
   - 数据项不能写默认值，同元组类型的元素（ADR-0024 决策 8 “按元组的规则”）。常用的组合写成 enum 里的 `static func` 或 `static let`。

## 备选方案

- `class enum`：
  - **普通的抽象类**（ADR-0008 原来的说法，没写用不用 record、加不加 `closed`）：相等、哈希、`ToString` 要编译器自己生成；C# 那边 `switch` 不知道列全了，别的程序集还能继承它，Nyxel 的 `match` 列全了也可能碰到不是任何 case 的对象。
  - **只加 `closed`，不用 record**：穷尽检查有了，相等和哈希仍要自己生成。record 生成的正好是 ADR-0024 决策 7 要的。
- `struct enum`：
  - **内嵌一个 `Kind` 枚举加属性**（ADR-0029 后果里用户提议的方向，F# 的 struct union 编出来也是这样）：
    ```csharp
    switch (s.Kind)
    {
        case AiState.Kinds.Chasing: Use(s.Target); break;   // 不是 Chasing 时读 s.Target 会抛异常
        // 漏写了 Fleeing，编译器不知道：C# 15 没有封闭的 enum
    }
    ```
    好处：能 `Enum.GetValues`、能转成整数，序列化器和编辑器按 enum 处理。坏处：C# 查不出漏掉的 case；读错 case 的数据到运行时才发现；几个 case 有同名的数据项（`Damage` 的三个 `Amount`）时还要另定规则。提议时 C# 还没有能检查列全的写法，union 补上了这一点。
  - **union 再加 `Kind`**：两样都有，生成的东西更多。真需要按 enum 序列化时再加，不影响已有的写法。
  - **C# 15 的 `union` 关键字**（`union AiState(Idle, Chasing, Fleeing)`）：编译器生成的 union 把值存成 `object`，每个带数据的 case 都要装箱分配，违背 `struct enum` 不分配的承诺（ADR-0039）。
- `ToString`：
  - **C# record 的格式**：`Idle { }`、`Burn { Amount = 12, Seconds = 3 }`。和 Nyxel 的写法对不上，不带数据的 case 多一对空括号。
  - **只有 case 名**：`Burn`。日志里看不到数据，调试时最常要看的正是数据。
- 不带数据的 case **也能写 `new`**（同 C# 那边）：两种写法是同一个值，读的人要知道它们一样；`class enum` 里 `new` 看起来像每次分配，其实共用一个对象。
- case 的数据项**能写默认值**（同函数参数，C# record 的构造函数也能有）：创建时可以少写几项，但“同元组”的规则要开一个例外。真有需要时再放开，不影响已有代码。

## 后果

- 修改了 ADR-0024 决策 8（C# 那边的属性在 case 类型上，不在 enum 本身上）和 ADR-0029 的后果（`Kind` 的候选方向、`ToString` 待定），原文都已改成最新，见各自的修订记录。
- 依赖 C# 15 的两个功能，环境上的安排见 ADR-0047：生成的程序集自己声明三个特性类型（面向的 .NET 里已经有时不声明）；Roslyn 正式支持 C# 15 的包发布以前，生成的 C# 用 Roslyn 5.9 的 Preview 模式编译。Preview 和 C# 15 正式版若有出入，以正式版为准再改。
- 相等（ADR-0024 决策 7）的细节由 record 定下：数据逐个用 .NET 的 `Equals` 比（`EqualityComparer<T>.Default`，同 C# 的 record 和元组的 `Equals`），这是“能当字典的键”的前提。所以 `float` 的 NaN 等于 NaN，引用类型的数据项用它自己的 `Equals`（`string` 比内容，`List<T>` 比是不是同一个列表）。
- 引擎那边的 C# 项目是 C# 14 时，能用但没有穷尽检查和 union 的写法，见决策 1、2。
- 实现：
  - Binder：`struct enum` / `class enum` 的 case 和数据、创建（按位置或带名字，同元组）、不带数据的 case 当值、`is`、`==`、成员里的 `self`。`match` 取数据随 `match` 一起做。
  - 降级：决策 1、2 的形状；不带数据的 `class enum` case 共用的对象放在一个 `internal` 的静态字段里；`struct enum` 的标记一般是 `byte`。
