# ADR-0040: `init` 默认公开、判断 enum case 一律 `is`、case 的值不能重复

- 状态：已接受
- 日期：2026-11-04

## 背景

2026-11-04 对 language-reference 的整体 review（第一批见 ADR-0038，第二批见 ADR-0039）剩下六项加一条文档去重。这一轮把它们讨论完：两项改规则，四项记下为什么不改。

- **`init` 的可见性有陷阱**。成员默认私有（ADR-0006），`init` 也一样；可是没写 `init` 的类自动得到的无参 `init` 是公开的（ADR-0013 的例外）。于是写了 `init` 反而把类封死：samples/Common/Collections.nyxel 的 `RecentList` 是 `public class`，`init(capacity: int)` 没写 `public`，又没有工厂函数，整个仓库没有一处能创建它。编译器不报错，引擎通过反射创建脚本时才发现没有公开的构造函数。
- **判断不带数据的 case 有两种写法**。`x is Damage.Burn` 判断是不是某个 case（ADR-0024 决策 5），不带数据的 case 又能写 `state == SlimeState.Idle`。一个问题两种写法，而且 `==` 这条只对不带数据的 case 成立（`hit == Damage.Burn` 是错的，`Burn` 要 `new`），读者要先看 enum 的声明才知道哪种能用。
- **两个 case 同一个值**。ADR-0024 没有禁止 `case Old = 1` 加 `case New = 1`（C# 允许，叫别名）。不带数据的 enum 在 .NET 里就是一个整数，`is` 和 `==` 都比整数，两个别名 case 互相 `is` 为 true，没有语义差别；但 `match` 里两个标签是同一个值，C# 报重复标签（CS0152），第二个分支永远走不到，穷尽检查也说不清“列全了”是几个。
- 另外四项：`case` 一个词三种用处（enum 成员、`match` 分支、`await all / any` 的项）；`->` 形式的 `if` 最后一个分支一直延伸到表达式结束；没有可空标注的程序集一律当 `T?`（ADR-0009）；不准同名（ADR-0036）加上只收窄局部变量会不会逼人改名。

## 决策

1. **`init` 默认公开；要藏起来写 `private init`。**
   ```nyxel
   public class RecentList<T: class> {
       init(capacity: int) { ... }             // 公开，和 public init 一样
   }

   public struct Fixed {
       private init FromRaw(raw: int) { ... }  // 只给本类型的运算符用
       public operator +(a: Fixed, b: Fixed) -> Fixed = new Fixed.FromRaw(a.raw + b.raw)
   }
   ```
   - 构造函数十个里九个是公开的，自动生成的那个本来就公开：现在“`init` 是公开的”是一条统一规则，不再是 ADR-0013 的例外。
   - `private` 写在它比默认窄、有信息的地方，和 ADR-0026 保留 `private set` 是同一个理由。`protected init` 照旧（抽象基类）。
   - 这是 ADR-0006“成员默认私有”和 ADR-0026“成员上不写 `private`”各开的一个口子：只有 `init`。别的成员上写 `private` 仍然是 NYX1196。
   - 命名 `init` 同样默认公开。
2. **判断是不是某个 enum case 一律写 `is`；`x == 类型.Case`、`x != 类型.Case` 是编译错误**，诊断给出 `is` / `is not`。
   ```nyxel
   if self.state is SlimeState.Idle { }    // 判断 case
   if key is not Key.Escape { }            // .NET 的枚举一样
   if state == self.previous { }           // 两个值比较：照旧 ==
   match state { case Idle -> ... }        // 不变
   ```
   - 只挡“右边直接写 case 名”这一种形式：两个 enum **值**之间的 `==` 不受影响。规则一句话说完，不用看声明。
   - .NET 的枚举同样适用：`key is Key.Escape` 在 C# 里本来就合法（常量模式）、意思相同，不是“似是而非”。
   - flags enum：整体相等同样写 `is`；包含判断照 ADR-0024 用 `HasFlag`。
   - 取代 ADR-0024 决策 5 的“简单 enum 写 `==` 也可以”和决策 7 的“`state == SlimeState.Idle` 就是判断是不是 Idle”。
3. **Nyxel 声明的 enum 里两个 case 不能是同一个值**（含 flags enum），是编译错误，诊断建议把后一个写成 `static let Old: E = E.New`。
   - case 的含义是“几选一”，两个 case 同一个值就不是两个选项。
   - .NET 的枚举有别名照收：`match` 里两个别名 case 都列出来时报“和 `case Enter` 是同一个值”；穷尽检查按不同的值算。
4. **四项不改**，理由记在这里：
   - `case` 三种用处的共同点是“几选一，一项一行”，Swift 前两处也用 `case`；块头（`enum`、`match`、`await any`）已经说明块里是什么，`case` 不会在读者不知情时改变含义。给 `await` 的项另起关键字反而要多记一个词。
   - `->` 形式的 `if` 最后一个分支延伸到表达式结束，同 C# `?:`、Kotlin。当运算数时已经强制加括号（ADR-0025），只剩读者看到 `else -> b + 1` 的一瞬间。让分支在第一个运算符处截断不行（`-> amount * 2` 太常见），警告又没有可靠的判据。
   - 没有可空标注的程序集一律当 `T?`（ADR-0009）本身不改。老的 NuGet 包或没标注的引擎绑定每个返回值都要 `?? throw` 时，缓解办法是项目文件里按程序集开一个“当作非空”的开关，这是编译器选项，不是语法；`.nyxelproj` 的格式还没定（ADR-0001），记进 roadmap 的 M4，定项目文件时一起定。引擎桩程序集我们自己写、自己标注。
   - 字段要先存进局部变量才能收窄，同一个块里第二次收窄同一个字段不能再 `let` 同名。出路已经有：`var target = self.target`，重新赋值后再检查，收窄跟着流程走，同 C#。真正烦不烦，写 samples 时观察，不预先定规则。

## 备选方案

- `init` 的可见性：
  - **不改规则，只改诊断**（NYX2049 建议 `public init`）：`RecentList` 这种陷阱还是没人管，除非加“公开类型没有任何公开 `init` 也没有返回自己的静态成员”这种靠猜的警告，和“不靠看不见的规则”冲突。
  - **`init` 必须写明 `public` 或 `private`**，不写是错误（同 ADR-0039 两种之间不设默认的思路）：陷阱同样消失，但每个 `init` 多一个词，而构造函数的默认值本来就和读者的预期一致。
  - **保持默认私有**（原来的决定，ADR-0006 的“成员默认私有”不开口子）：就是上面的陷阱。
- 判断 case：
  - **两种都留**（原来的决定）：`== Tile.Wall` 读起来自然，但同一个问题两种写法，其中一种还只对不带数据的 case 成立。
  - **不带数据的只准 `==`，带数据的只准 `is`**：规则跟着声明走，读者负担最重。
- 别名 case：
  - **允许**（原来没有规则，同 C#）：`match` 列不全，第二个别名分支走不到。需要别名时 `static let` 一样能用，读者还能看出它不是一个选项。

## 后果

- 改了已合进 main 的 ADR：
  - ADR-0006 决策 4、ADR-0026 决策 4、ADR-0019 决策 2：`init` 除外。
  - ADR-0013 决策 1：自动生成的 `init` 公开不再是例外。
  - ADR-0024 决策 1（值不能重复）、决策 5 和 7（`==` 判断 case 的写法挪进备选方案）。
  - ADR-0029 例子 `IsHot` 改成 `self is Element.Fire`。
- language-reference：构造、可见性、enum、类型检查与收窄各节改写；`_`、`??` 右边、`->` 分支里能写什么原来在 `if`、`match`、空值、`await` 四节各写一遍，抽成一节“分支与离开语句”。
- 分工：
  - Parser：`init` 上的 `private` 不再报 NYX1196（别的成员照报）。
  - 语义分析（M2）：`init` 的可见性默认公开（已随本 ADR 实现）；`x == 类型.Case` 报 NYX2055、两个 case 同一个值报 NYX2056（enum 还没进 Binder，随“属性、struct、enum”一步实现，semantic-analysis.md“实现状态”）。
- samples：`TileMap.IsWall`、`BossIntro` 的 `key == Key.Escape` 改成 `is`；`Fixed.FromRaw` 写成 `private init`，`RecentList`、`TileMap` 的 `init` 不写修饰符就是公开。
- roadmap M4 加一条：项目文件里按程序集的“当作非空”开关。
- 待定：`public init` 要不要像成员上的 `private` 一样禁掉（现在允许；和默认一样、没有信息）。
- 错误码见 design/diagnostics.md。
