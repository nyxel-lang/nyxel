# ADR-0026: `case _` 默认分支、`_` 保留记号、`catch _`、成员不写 `private`、getter 的表达式体

- 状态：已接受
- 日期：2026-10-09

## 背景

对 language-reference 做了一轮整体 review，目标是在 ADR-0003 的优先级下让语法更简单、更少歧义。找出的问题分两类：同一件事有两种写法（成员上的 `private` 和默认私有；`else` 既是 `if` 的分支又是 `match` 的默认分支），以及规则按语法类别而不是按用法切分、读者要记细线（`match` 当语句用时调用能直接写、赋值要包块）。顺带两处省行数的写法：getter 的表达式体、`catch` 用不到的名字。

`match` 的 `else` 还带来一条“看不见的规则”：`->` 形式的 `if` 允许 `else` 写在下一行开头（ADR-0025），而 `match` 分支里行首的 `else` 又得归 `match`，ADR-0025 为此加了一条例外（“直接在 `match` 分支里时，行首的 `else` 是 `match` 的默认分支”）。读者得知道 `else` 在不同位置归属不同，编译器能查出写错，但这正是 ADR-0003 要避免的。

本轮一共讨论了六项，第 1 项（`match` 当语句用时分支可以是简单语句）改在 ADR-0025 决策 4 里（它还在 dev 上，没合进 main）；其余五项在这里。

## 决策

1. **`match` 的默认分支写 `case _`，`else` 只属于 `if`。**

   ```nyxel
   let scale = match self.size {
       case Small -> 0.5
       case Huge -> if self.rumble -> 2.5
           else -> 2.0                   // 一定是 if 的 else
       case _ -> 1.0                     // 默认分支
   }

   match hit {
       case Burn(_, seconds) -> self.StartBurning(seconds)
       case _ -> {}                      // 其余不处理
   }
   ```

   - `_` 单独当模式匹配其余任何值。ADR-0009 决策 5、7 里的 `else` 全部改成 `case _`，“其余不处理”写 `case _ -> {}`，flags enum 的 `match` 必须有 `case _`（ADR-0024）。
   - `match` 里写 `else` 是编译错误（NYX1193），诊断给出 `case _`。
   - ADR-0025 里“直接在 `match` 分支里时行首的 `else` 归 `match`”的例外删掉：行首的 `else` 总是接着前面 `->` 形式的 `if`。嵌在另一个 `if` / `try` 分支里要加括号的规则（悬挂 else）不变。

2. **`_` 是保留记号，不是名字。** 它只表示“这个位置的值不要”，出现在五个位置：解构 `let (_, max) = ...`、`out _`、取 case 数据 `case Burn(_, seconds)`、默认分支 `case _`、`catch _: T`。
   - 不能声明叫 `_` 的变量、参数、成员（NYX1194），不能当表达式用（NYX1195）；C# 允许 `var _ = x` 之后再读 `_`，Nyxel 不允许。`_x`、`x_` 仍是普通名字。
   - 高亮按关键字上色，弥补它字形矮、紧跟 `,` `:` `)` 时不醒目的问题。
   - `for _ in 0..<n`（只要循环次数）暂不放开，列为待定。

3. **`catch _: 类型`**：用不到异常对象时名字写 `_`，不引入名字，`when` 过滤里引用不到它。`catch 名字: 类型` 的形状不变（ADR-0014）。

4. **成员上不写 `private`。** 成员默认私有（ADR-0006），写 `private` 是编译错误（NYX1196），诊断说明默认就是私有。`private` 只写在访问器上（`private set`，ADR-0011），那里它比属性本身窄，是有信息的。修饰符顺序（ADR-0019）里的“可见性”在成员上只有 `public` / `protected`。

5. **getter 可以写表达式体 `get = 表达式`**，同函数的 `= 表达式`（ADR-0012）。setter 的体是语句（几乎都是赋值），只能写块，`set(value) = ...` 是编译错误（NYX1197）。

   ```nyxel
   public property IsDead: bool { get = self.health.IsDead }

   public property Ratio: float {
       get = self.current as float / self.Max
       private set(value) { self.current = (value * self.Max) as int }
   }
   ```

## 备选方案

- **默认分支**：
  - 保留 `else`，`->` 形式的 `if` 嵌在 `match` 分支里时必须加括号（和嵌在另一个 `if` 分支里同一条规则）：是打补丁，`match` 分支本来是完整的表达式位置，加括号别扭。
  - 保持现状（例外规则）：读者要知道 `else` 在不同位置归属不同。
  - 选 `case _`：`else` 一词一义；`_` 的含义和解构里一致；C# 的 switch 表达式默认分支也写 `_ =>`，含义相同。代价是改 ADR-0009（新 ADR）、所有 `else -> {}` 改写，以及习惯 Kotlin `when` 的读者少了一个熟悉的词。
- **`_` 换成一个词**（用户担心 `_` 字形矮、紧跟符号时不易分辨）：
  - `any`：模式和默认分支里读得通（`case any`、`case Burn(any, seconds)`），解构里像类型（`let (any, max)`）。
  - `skip` / `ignore`：解构和 `out` 里读得通，默认分支 `case skip -> 1.0` 读成“跳过”，`catch skip: IOException` 读成“跳过异常”。
  - 丢弃用 `_`、默认分支换词（`case other`）：同一个概念两种写法，`case Burn(_, seconds)` 和 `case other` 挨着时读者要问为什么不一样。
  - 选保留 `_`：“不要这个”是 C#、Rust、Swift、Kotlin、Python 的共同约定，读者零成本；Agent 的反射动作就是写 `_`，换词每次都会撞；C# 的 `_` 就是丢弃，是“写法相同含义相同”。字形问题靠高亮和格式化补。变成保留记号后它永远只有一个意思，读者不用想它是不是某个变量。
- **成员上的 `private`**：保持可写（和 C# 一样，强调用）：同一件事两种写法，而且 ADR-0006 选默认私有的理由就是不写也清楚。彻底去掉 `private`：访问器那里没有合适的替代词，`private set` 恰好和 C# 相同。
- **getter 表达式体**：
  - 保持只有块体：三行写一个一行的表达式，samples 里 10 个 getter 全是这种。
  - 属性直接 `= 表达式`（`property IsDead: bool = self.hp <= 0`）：自动属性的初始值已经写在这个位置（`property Score: int = 0 { get }`），“初始值存一次”和“每次读都算”长得几乎一样，正是 ADR-0003 要避免的。
  - 选 `get = 表达式`：和函数是同一条规则，不是新形式。
- **`catch` 用不到的名字**：保持必须写，靠 M2 的未使用变量警告：读者仍要扫一遍确认 `e` 没用到。省略名字 `catch IOException {`：和 `catch e: T` 形状不同，“名字: 类型”是全语言统一的声明形状。

## 后果

- 改了以下已合进 main 的内容（原文都已改成最新，见各自的修订记录）：ADR-0009 决策 5、7 和 ADR-0024 决策 3、8 的 `else` 默认分支；ADR-0014 决策 4 的“`catch` 名字必须写”（名字仍必须写，但可以是 `_`）；ADR-0019 决策 2 修饰符顺序里成员上的 `private`；ADR-0011 决策 2 访问器只有块体。ADR-0025 决策 3 的 `match` 例外和决策 4 的分支内容直接改原文。
- 语法树：`_` 是 `UnderscoreToken`；`case _` 是 `DiscardPatternSyntax`；`out _` 是 `DiscardExpressionSyntax`；解构、case 数据、`catch` 的名字位置上 `_` 就放在名字记号的位置；`AccessorDeclarationSyntax` 增加 `ExpressionBody`。
- 新错误码 NYX1193–1197；NYX1123 的消息改成按用法说明。见 design/diagnostics.md。
- 待定：`for _ in 区间`；`_` 当 lambda 参数（`func(_) = 0`）。
