# ADR-0042: 整数相除不隐式变浮点、被 lambda 捕获的 `var` 不收窄、局部变量必须有初始值、`public init` 是错误

- 状态：已接受
- 日期：2026-11-10

## 背景

第三十五轮讨论。2026-11-10 对 language-reference 又做了一轮整体 review（前三批见 ADR-0038、0039、0040），这次带着 Binder 已经能跑的部分去试。大的决定没有要推翻的，找出五处，加上 STATUS 里悬着的两小项，一起定了：

- **整数相除的结果悄悄变成浮点**。ADR-0038 堵住了字面量（`let x: float = 1 / 2` 报错），变量相除没管。下面三行都能编译，实际运行分别得到 0、1、0.5，都不是写的人想要的：
  ```nyxel
  static func Ratio(kills: int, total: int) -> float {
      return kills / total              // 3 / 4 得 0
  }
  let speed: float = wave / 2           // wave 是 3：得 1，不是 1.5
  let scaled = wave / 2 * 0.5           // 得 0.5，不是 0.75
  ```
  这是 ADR-0038 说的最坏的那种失败：能编译，意思不对。Swift、Kotlin、Rust 没有 `int` 到 `float` 的隐式转换，所以没有这个问题；Nyxel 照 C# 有（ADR-0010）。
- **被 lambda 捕获的 `var` 还在收窄**。收窄的前提是“检查和使用之间变量不会被别处改”，字段因此不收窄（ADR-0009）。被 lambda 捕获的 `var` 局部变量有同样的问题，规则没提：
  ```nyxel
  var target: Enemy? = self.FindTarget()
  slime.Died += func(s) { target = null }     // 以后某个时刻执行
  if target == null { return }
  self.Shout()                                 // 里面触发了 Died
  target.Attack()                              // 编译器认为非空，运行时崩
  ```
  `is` 的收窄也一样，降级时按收窄后的类型插入的转换会抛 `InvalidCastException`。
- **局部变量能不能不写初始值**。ADR-0006 只说“有初始值时可以省略类型”，没说 `let label: string` 之后再赋值行不行；编译器报“还不支持”（NYX2001）。
- **两处优先级，报错不指路**。`if not hp > 0` 被读成 `(not hp) > 0`，报“`not` 不能用于 `int`”；`if mask & 4 != 0` 被读成 `mask & (4 != 0)`，报“`&` 不能用于 `int` 和 `bool`”。Python、GDScript 里前一句是 `not (hp > 0)`，Agent 会照着写。
- **`typeof`、`nameof` 没出现过**。docs 里搜不到这两个词；引擎和反射的 API 常要类型对象，`INotifyPropertyChanged` 这类接口要成员的名字。
- STATUS 里悬着的：ADR-0040 留下的“`public init` 要不要禁掉”；ADR-0041 留下的“`var current = node` 推断成 `Node`，之后赋 `Node?` 编不过”。

每一项都对比了方案，用户全部按推荐定。

## 决策

1. **整数相除的结果不能隐式转换成 `float`、`double`、`decimal`，是编译错误**（NYX2066）。
   ```nyxel
   let ratio: float = kills / total                // 错误
   let exact: float = kills as float / total       // 要小数：先把一边转成浮点
   let cells: float = (kills / total) as float     // 确实要整除：对结果写 as
   let half = wave / 2                             // int，没有转换：照旧
   ```
   - 准确的范围：两边都是整数的 `/`，连同它外面再做的整数加、减、乘、取余、取负和括号，这样一个整数算式的值要隐式变成浮点时报错。`kills / total * 100` 赋给 `float` 和 `kills / total` 是同一个错。
   - 隐式转换出现的地方都算：赋值和声明的类型、实参、返回值、和浮点值做运算或比较（`wave / 2 * 0.5`、`wave / 2 > 0.5`）、复合赋值的右边（`speed += wave / 2`）。
   - 相除的结果先经过别的东西就不算，那已经表明要的是整数：传给要整数的参数（`Math.Abs(a / b)` 的结果再赋给 `float`）、`as`、移位的操作数。单独的 `%` 不算，余数是精确的。
   - 选重载照旧按 C# 的规则，选完以后要做的那次转换报错，不因为这条规则改选别的重载。
   - 含义没有变，只是拒绝：能编过的写法和 C# 结果相同（ADR-0003 的“不似是而非”）。只有字面量的式子照旧是 ADR-0038 的 NYX2054。
   - 诊断给出两种写法：`'kills / total' divides whole numbers ... write 'kills as float / total' ... or '(kills / total) as float'`；一边是字面量时建议写成小数（`wave / 2.0`）。
2. **被 lambda 捕获的 `var` 局部变量不收窄。** 函数里只要有一个 lambda 读或写了它，它在整个函数里（lambda 里面也算）一直是声明的类型：`!= null`、`is`、`??=`、赋非空值（ADR-0041 决策 1）都不让它收窄。
   ```nyxel
   var target: Enemy? = self.FindTarget()
   slime.Died += func(s) { target = null }
   if target == null { return }
   target.Attack()                              // 错误：target 仍是 Enemy?

   let current = target ?? return               // 存进 let 再用
   current.Attack()
   ```
   - `let` 局部变量和参数不受影响：它们本来就不能重新赋值，被捕获了也照常收窄。
   - 和“字段不收窄”是同一个理由，出路也一样：先存进 `let`。诊断在通常的“先检查”之外说明它被 lambda 捕获了。
3. **局部变量必须在声明的地方给初始值**（NYX1239）；类型照旧可以省略。
   ```nyxel
   let label: string                                    // 错误
   let phase = if wave > 2 -> "late" else -> "early"    // 分几种情况算一个值：if / match / try 的 -> 形式，或 eval
   var best: Enemy? = null                              // 一开始没有：写成可空的
   ```
   - 字段不变：没有初始值的字段由 `init` 赋值（ADR-0008）。`for`、`catch`、`using`、`out let`、`case let` 引入的名字本来就带着值。
   - 编译器因此不需要局部变量的“确定赋值”分析（证明每条路径上用之前都赋过值），读者在声明处就能看到值从哪来。
4. **`not` 和位运算的优先级不动（同 C#，ADR-0010、ADR-0019），加两条专门的诊断。**
   - NYX2067：`not` 后面跟着比较或 `is`、而它管到的那个值不是 `bool` 时，给出两种写法：`not hp > 0` 写成 `hp <= 0` 或 `not (hp > 0)`；`not x is T` 写成 `x is not T`。
   - NYX2068：`&`、`^`、`|` 的一边是没加括号的比较、另一边不是 `bool` 时，说明它被读成了什么、括号加在哪：`mask & 4 != 0` 写成 `(mask & 4) != 0`。
5. **`init` 上写 `public` 是编译错误**（NYX1240）：`init` 默认公开（ADR-0040），`public init` 和不写一样，是第二种写法；理由同成员上的 `private`（ADR-0026）。`init` 上能写的可见性只有 `private`、`protected`。
6. **`var` 推断出的类型不变，赋 null 时的诊断指到声明**（NYX2069）。`var current = node`（`node` 已经收窄成 `Node`）的类型是 `Node`；之后 `current = current.next`（`Node?`）或 `current = null` 报错，消息给出 `var current: Node? = node`。ADR-0041 后果里的观察点就此定了。
7. **`typeof`、`nameof` 记入待定**，和特性（attribute）一起在 M3 讨论：特性的参数最常用到类型对象。倾向直接用 C# 的写法，含义也相同。

## 备选方案

- 整数相除变浮点：
  - **不管**（同 C#，原来的状态）：背景里的三行继续悄悄算错。
  - **只给警告**：照样能跑出错的结果；这一条没有“有时是故意的、不值得拦”的情况，故意的写法 `(a / b) as float` 只多几个字。
  - **只管直接的那一层**（`let x: float = a / b` 报错，`a / b * 100` 不报）：规则字面上最短，但 `kills / total * 100` 是同一个 bug，而且是算百分比时最常见的写法。clang-tidy 的 `bugprone-integer-division` 检查也是沿着整数运算往外找。
  - **让 `/` 在浮点位置按浮点除**（ADR-0013 否掉的“期望类型传给所有字面量”的同类）：同一个式子放的位置不同结果不同，和 C# 同写法不同含义。
- 被捕获的 `var`：
  - **不管**（同 C#）：C# 的可空检查在这里也有洞，但它只是警告；Nyxel 的空值安全全靠收窄，洞就是运行时崩溃。
  - **只有被 lambda 修改过的才不收窄**（Kotlin 的规则）：能多收窄一些只读捕获的情况，但规则要分“在 lambda 里改”“捕获之后在外面改”几种，读者要找遍函数里的 lambda 和赋值才知道一个变量此刻收不收窄。
  - **只从捕获的那一行往后不收窄**：捕获之前确实安全，但同一个变量在函数前半截能收窄、后半截不能，把 lambda 往上挪一行就让别处编不过。
- 局部变量的初始值：
  - **可以先声明后赋值，编译器证明每条路径都赋过值**（C#、Swift、Kotlin）：`let label: string` 加各分支赋值能写，但 ADR-0025 已经给了“分几种情况算出一个值”的表达式写法，这就是第二种；编译器还要多一套分析。先不放开，以后放开不会让已有代码失效。
- 优先级：
  - **`not` 降到比较下面**（Python、GDScript）：`not hp > 0` 一次写对，但 `not x is T` 和 `x is not T`、`not a == b` 和 `a != b` 都成了合法的两种写法；`a == not b` 反而要加括号。现在的优先级顺带逼出了唯一写法。
  - **位运算升到比较上面**（Swift、Rust、Go）：`mask & 4 != 0` 一次写对，但 `bool` 上的 `a & b == c` 和 C# 含义不同。
  - **位运算和比较混写一律要括号**（当成语法规则）：和只加诊断的效果几乎一样（混写本来就过不了类型检查），多一条规则。
- `public init`：**照旧允许**（ADR-0040 留下的状态）：samples 里已经全去掉了，留着只会让两种写法并存。
- `var current = node`：
  - **初始值是收窄过的变量时，按它声明的类型推断**（C# 的 `var` 对引用类型总是推断成可空的）：`current = current.next` 能直接写，但“局部变量的类型就是初始值的类型”多一条例外，读者看到 `var name = "x"` 不知道它以后能不能是 null。

## 后果

- 改了已合进 main 的 ADR（原文都已改成最新，见各自的修订记录）：
  - ADR-0006 决策 2：局部变量必须有初始值。
  - ADR-0008 决策 3、ADR-0019 决策 2：`init` 上不写 `public`。ADR-0013、ADR-0027、ADR-0029 例子里的 `public init` 去掉 `public`。
  - ADR-0009 决策 2、ADR-0010 决策 7：被 lambda 捕获的 `var` 不收窄。
  - ADR-0010 决策 5：整数相除的结果不隐式变浮点。ADR-0013 决策 3 规则 1 补一句指到这里。
- dev 上还没合并的直接改：ADR-0040 决策 1 和待定（`public init`）、ADR-0041 决策 1 和后果（被捕获的 `var`、`var current = node`）、ADR-0038 后果（变量相除）。
- language-reference：“运算里的字面量”“运算符”“类型转换”（整数相除）、“变量与字段”（初始值）、“空值”“类型检查与收窄”（被捕获的 `var`、NYX2069）、“构造”“可见性”（`public init`）、文末“待定”（`typeof`、`nameof`）。顺带改了 review 查出的四处文档不一致：`case Burn(seconds) when ...` 少取一项、“基类与 super”例子里的 `public init`、“`private set` 是 `private` 唯一能出现的位置”、循环变量每轮是新的变量（原来只写在 lowering.md）。
- 实现：
  - Parser：NYX1239（局部变量没有初始值）、NYX1240（`public init`）。
  - Binder：NYX2066（`Convert` 里查整数算式）、NYX2067、NYX2068、NYX2069；没有初始值的局部变量不再报 NYX2001。
  - 决策 2 等 Binder 支持 lambda 时一起实现（semantic-analysis.md“流分析”“实现状态”）：先扫一遍函数体，找出被 lambda 用到的 `var`。
- 代价：
  - 决策 1：确实要整除再当浮点用的地方多写一个 `as`。
  - 决策 2：被捕获的 `var` 每次用之前要先存进 `let`，即使那个 lambda 只读不写。
  - 决策 3：按 C# 习惯写的“先声明、各分支赋值”会被拒，诊断指向 `if ... ->`、`match`、`eval`。
- 错误码见 design/diagnostics.md。
