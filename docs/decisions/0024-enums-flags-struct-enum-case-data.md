# ADR-0024: 枚举的值和 flags、`x is` 某个 case、`struct enum`、case 的数据同元组

- 状态：已接受
- 日期：2026-10-06

## 背景

第十九轮讨论，“待定”的第四轮：枚举。之前留下的几项：

- ADR-0008：简单枚举的底层值和 flags；带数据枚举的值类型版本。
- ADR-0010：`x is 某个 enum case`。
- ADR-0009：带数据的 case 在 C# 那边的属性名大小写。
- ADR-0023：能自己写值以后，没有值为 0 的 enum 的数组能不能直接创建。

参考仓库 EnginePlayground 的 C# 代码：

- 和 native 对应的枚举（`LogLevel`、`Phase`）每个值都写明。
- `CookedType : uint` 从 1 开始，没有 0；测试里写着 “enum values are the wire format”。
- 不少枚举是 `: byte`。
- ECS 组件要求 `T : unmanaged`（`World.Get<T>`、`Query<T1>`），组件里不能有引用。

八项都和用户对比了代码。前七项按推荐定。第八项，用户选了 C# 那边用 PascalCase，并提出两点：定义处也像元组一样用 PascalCase；取数据也按元组解构的规则来。这比推荐的做法（定义处 camelCase、按名字取）更一致，采纳了，取代 ADR-0009 的决策 6。

## 决策

1. **简单枚举可以写值；要写就每个 case 都写**。
   ```nyxel
   enum CookedType : uint {
       case Mesh = 1
       case Texture = 2
       case Material = 3
   }
   ```
   - 都不写时按声明顺序 0、1、2，同 C#。
   - 值是整数常量，可以用运算组合：`1 << 3`、`Layer.Ground | Layer.Water`。引用别的 case 也写类型名（ADR-0008 的静态成员规则）。
   - 带数据的 enum 不能写值。
2. **底层类型写在冒号后面，同 C#**：`enum Tile : byte`。
   - 只能是 `byte`、`sbyte`、`short`、`ushort`、`int`、`uint`、`long`、`ulong` 之一，不写是 `int`。
   - 带数据的 enum 不能写。
3. **flags 枚举写 `flags enum`，每个 case 都写值**。
   ```nyxel
   flags enum Layer {
       case None = 0
       case Ground = 1
       case Water = 2
       case Air = 4
       case Solid = Layer.Ground | Layer.Water
   }

   let mask = Layer.Ground | Layer.Air
   if mask.HasFlag(Layer.Air) { ... }
   ```
   - `flags` 只在紧挨着 `enum` 时是关键字，别处照样能当名字（`let flags = ...`）。
   - Nyxel 的 enum 只有 flags enum 能用 `|`、`&`、`^`、`~`。对普通 enum 用是编译错误，因为结果不是任何一个 case。
   - C# 定义的枚举照 C#，都能位运算。有的引擎忘了标 `[Flags]`。
   - `match` 一个 flags enum（包括 C# 带 `[Flags]` 的）必须写 `else`：组合出来的值列不全。
   - C# 那边是 `[Flags] enum`。检查包含用 .NET 的 `HasFlag`。
   - 带数据的 enum 不能是 flags。C# 的 `[Flags]` 写法是编译错误，诊断给出 `flags enum`。
4. **没有值为 0 的 case 时，数组不能直接创建**。这是 ADR-0023 的规则用在自定义值上：
   ```nyxel
   let a = new array<CookedType>(8)                             // 错误：0 不是任何 case
   let b = new array<CookedType>(8, func(i) = CookedType.Mesh)
   ```
   - flags enum 的 0 表示“一个都没有”，总能直接创建。
   - C# 定义的 enum 照旧能直接创建，同 C# 定义的 struct。
5. **`x is 类型.Case` 判断是不是某个 case，得到 bool**。
   ```nyxel
   if self.state is SlimeState.Idle { ... }
   if hit is not Damage.Heal { ... }
   ```
   - 所有 enum 都能用：简单的、带数据的、`struct enum`、C# 的。
   - 要写类型名。match 以外的地方本来都写类型名（`self.state = SlimeState.Idle`），C# 里同样的写法也合法。
   - 只判断，不取数据，也不收窄；要数据写 `match`。C# 的 `x is Damage.Burn(var a, var s)` 是编译错误，诊断给出 match 的写法。
   - 简单 enum 写 `element == Element.Fire` 也可以，意思一样。
6. **带数据的 enum 有值类型版本 `struct enum`**。
   ```nyxel
   struct enum AiState {
       case Idle
       case Chasing(Target: Entity)
       case Fleeing(From: Entity, Until: float)
   }
   ```
   - `enum` 的值在堆上（ADR-0008：抽象基类加每个 case 一个子类），`struct enum` 的值是值类型，同 class 和 struct 的关系。写法和用法（创建、`match`、`is`、`==`）都一样，只差在怎么存放。
   - 一个值里留着所有 case 的数据的位置，同类型的位置可以共用。大小约等于各 case 加起来，赋值时整个复制。
   - case 不能直接包含同一个 struct enum，否则大小是无穷的。struct 也是这样。
   - 数据全是 unmanaged 类型时，整个 struct enum 也是 unmanaged，能放进要求 `T : unmanaged` 的 ECS 组件。
   - 全零的值：
     - 第一个 case 不带数据时，全零就是这个 case（上例是 `Idle`）。可以直接 `new array<AiState>(n)`，引擎不给值创建组件时也是它。
     - 第一个 case 带数据时，同 Nyxel 的 struct，要用 `element`。
   - 不带数据的 enum 本来就是值类型，写 `struct enum` 是编译错误。
7. **带数据的 enum，`==` 比内容**：同一个 case、数据逐个相等，就相等。
   - `GetHashCode` 也按内容算，能当字典的键。
   - `enum` 和 `struct enum` 一样。数据创建以后不能改，所以两者只差在性能上，意思相同。
   - 不带数据的 case 只有一个值，`self.state == SlimeState.Idle` 就是判断是不是 Idle。
   - 和 `null` 比较仍然只看引用（ADR-0023）。
8. **case 的数据按元组的规则**，取代 ADR-0009 决策 6 的“按字段名取出”。
   ```nyxel
   enum Damage {
       case Physical(Amount: int)
       case Burn(Amount: int, Seconds: float)
   }

   let hit = new Damage.Burn(Amount = 12, Seconds = 3)

   match hit {
       case Burn(amount, seconds) -> ...
       case Physical(amount) -> ...
   }
   match hit {
       case Burn(_, seconds) when seconds > 3 -> ...
       else -> {}
   }
   ```
   - 声明同元组类型：每一项都有名字，用 PascalCase。C# 那边是同名的属性（`hit.Amount`），构造函数的参数是 camelCase。
   - 创建同元组的值：按位置写 `new Damage.Burn(12, 3)`，或带名字写 `new Damage.Burn(Amount = 12, Seconds = 3)`。
   - `match` 里按位置取出，同解构（ADR-0021）：
     - 名字的个数要和数据项一样，`_` 丢弃不要的。
     - 名字自己起，重名时换个名字就行，ADR-0009 “不能改名”的限制没有了。
     - 名字和数据项名是同一组（不分大小写）、顺序却不同时，给警告。
   - 不需要数据时照旧不写括号：`case Physical`。

## 备选方案

- **值：没写值的 case 接着上一个加一（C#）**。坏处：读的人要自己数；在中间插入 case，后面的值全变，和不写值是同一个问题。
- **值：不能写**。坏处：和引擎、存档格式对齐只能靠顺序。
- **底层类型：一律 `int`**。坏处：对不上 native 那边 1 字节的枚举，大数组多占内存。
- **flags：没写值的 case 自动取 1、2、4、8**。
  - 好处：不会犯 C# 里忘写值、结果是 0、1、2、3 的错。
  - 坏处：和“要写就都写”不一致；“一个都没有”要另起名字；读的人要知道这条规则才看得出值。
- **flags：不区分，所有 enum 都能位运算（C#）**。坏处：`match` 没法保证列全，普通 enum 的值也可能是组合出来的。
- **flags：C# 的 `[Flags]` 特性**。坏处：特性还没设计（M3）；而且它在这里会改变语言规则（能不能位运算、match 要不要 `else`），不只是给工具看的标注。
- **没有 0 的数组：写了值就必须有一个 `= 0` 的 case**（C# 代码分析规则 CA1008 的建议）。坏处：常常要多编一个用不上的 `None`。
- **没有 0 的数组：允许，同 C#**。坏处：数组里的元素不是任何 case，match 到它时在运行时报错。
- **`is` 顺便取数据：`if x is Damage.Burn(seconds)`**（Rust 的 `if let`、Swift 的 `if case`）。
  - 好处：比 match 少两行。
  - 坏处：取出的名字在 `or` 连起来时、`is not` 之后、`while` 里能不能用，要另定一套规则，C# 这部分就很绕。以后在现在的基础上加不影响已有代码。
- **`is`：不加**。坏处：问“是不是某个 case”要写四行 match。
- **值类型：暂不支持**。坏处：带数据的 enum 放不进 ECS 组件，ADR-0017 的状态机写法在 ECS 里用不了。
- **值类型：全都是值类型（Swift、Rust）**。坏处有三个：
  - 大的 case 每次赋值都要整个复制。
  - 不能递归，比如行为树的 `case Not(Inner: Condition)`。
  - C# 那边不能按子类 `switch`。

  F# 和本 ADR 一样：联合类型默认是引用类型，标 `[<Struct>]` 变成值类型。
- **值类型：编译器自动选**。坏处：读的人看不出赋值时是复制还是共用。
- **`==` 比引用**。坏处：两个内容一样的值不相等；`struct enum` 只能比内容，于是加不加 `struct` 会改变 `==` 的结果。
- **case 数据：定义处 camelCase，match 按名字取**（ADR-0009 原来的规则，C# 那边转成 PascalCase）。
  - 好处：只取需要的项，顺序无关；给 case 加数据项不影响已有的 match；名字一定和数据项对应。
  - 坏处：和元组是两套规则；不能改名；C# 那边的名字和 Nyxel 不一样。
  - 用户选了按元组的规则。
- **case 数据：定义处 PascalCase，按名字取，取出的局部变量首字母小写**。坏处：多一条隐藏的对应规则。

## 后果

- 取代 ADR-0009 的决策 6（按字段名取出 case 的数据、不支持改名）。ADR-0009 已经合进 main，不改原文。
- 按位置取的代价：
  - 只要后面的数据项时，前面要写 `_`。
  - 给 case 加数据项后，取它数据的 match 都要补一个位置。编译器会逐个报出来，不会悄悄出错。
  - 改了名再写反，查不出来。
- 分工：
  - Parser：
    - `flags enum`、`struct enum`、底层类型、case 的值。
    - 只看声明就能查的规则：只有不带数据的 enum 能有底层类型、值和 `flags`；值要么都写，要么都不写；flags enum 每个 case 都写；`struct enum` 要有带数据的 case。
    - C# 写法 `[Flags]`、`x is Damage.Burn(a, s)` 的诊断。
  - 语义分析（M2）：
    - case 的值是整数常量，在底层类型的范围内。
    - 位运算只用于 flags enum；match 一个 flags enum 要有 `else`。
    - `is` 后面的名字是类型还是 case。
    - 取 case 数据时名字的个数，顺序相反的警告。
    - 数组元素全零是否合法，加上本 ADR 的两条：没有值为 0 的 case、`struct enum` 的第一个 case。
  - 降级（M2 / M3）：
    - `struct enum` 的布局：表示哪个 case 的标记，加上各 case 的数据，同类型的位置共用。
    - 带数据 enum 的 `Equals`、`GetHashCode`、`==`。
    - 列全了 case 的 match 遇到不是任何 case 的值（`5 as Element`）时抛异常，同 C# 的 switch 表达式。
    - C# 那边怎么取 `struct enum` 的数据，到时再定。
- 待定：带数据的 enum 实现接口（`enum Damage : IDescribable`）；在 enum 里写方法（现在用扩展）。
- 错误码见 design/diagnostics.md。
