# ADR-0043: 当值用的 `if` 写 `if c then a else b`，当值用的 `try` 去掉第一个箭头，语句形式不写 `then`

- 状态：已接受
- 日期：2026-11-10

## 背景

第三十六轮讨论，由用户提出。ADR-0025 把当值用的 `if` / `try` 定成分支写 `->`：

```nyxel
let label = if self.hp > 0 -> "alive" else -> "dead"
case Heal(amount) -> if amount > 100 -> 100 else -> amount
let config = try -> Config.Load(path) catch e: IOException -> Config.Default
```

用户觉得这个形状不好看，问了两件事：`match` 能不能完全顶替当值用的 `if`，这样就不要它了；或者它有没有更好的写法。

不好看的地方：

- `else ->`、`try ->` 的箭头左边没有东西。`case 模式 -> 值` 的箭头左边是一种情况，这两处只是一个关键字。
- 嵌在 `match` 分支里时一行三个箭头。
- 这个写法只有 Nyxel 有，Agent 要靠文档才写得对。ADR-0025 选它时的理由是不加关键字、和 `match` 的分支统一。

`match` 能不能顶替：

- `match` 要有一个被匹配的值。两选一能硬写成 `match 条件 { case true -> a  case _ -> b }`，一行变四行，也不能直接当实参。
- 几个不相干的条件依次判断（`else if` 连写）现在的 `match` 写不出来，要新加一种不带值的 `match`（Kotlin 的 `when { }`）。
- samples 里当值用的 `if` 两处，都是一行的两选一，一处直接当实参（`line.Append(if self.IsWall(x, y) -> '#' else -> '.')`）。同一天定的“局部变量必须有初始值”（ADR-0042 决策 3）靠的也是有一个紧凑的“按条件选一个值”。

所以当值用的 `if` 留着，换写法。对比了三种写法后用户选了 `then`，`try` 跟着去掉第一个箭头，不带值的 `match` 先不加。

定了以后用户接着问：语句形式要不要也写 `then`（`if c then { ... }`），让两种形式形状统一。比较后定为不加（决策 7）。

## 决策

1. **当值用的 `if` 写 `if 条件 then 值 else 值`。**
   ```nyxel
   let label = if self.hp > 0 then "alive" else "dead"

   let speed = if self.IsFrozen then 0
       else if self.IsHasted then self.BaseSpeed * 2
       else self.BaseSpeed

   line.Append(if self.IsWall(x, y) then '#' else '.')
   case Heal(amount) -> if amount > 100 then 100 else amount
   ```
   - `then` 是保留字，不能再当名字。
   - 其余规则同 ADR-0025，没有变：必须有 `else`；每个分支是一个表达式或一条离开语句，要先执行语句写 `eval`；最后一个分支延伸到表达式结束，所以当运算数、或者直接写在另一个 `if` / `try` 的分支里时要加括号（`else if` 连写不算）。
   - 语句形式照旧是 `if 条件 { ... } else { ... }`。两种写反、同一个 `if` 里混用都是编译错误：看到 `if ... then` 就知道它在产生值，看到 `if ... {` 就知道是在执行语句。
2. **当值用的 `try` 写 `try 值 catch e: T -> 值`**：`try` 后面直接跟值，不写箭头；`catch` 的箭头留着；`finally` 照旧写 `finally { 语句 }`。
   ```nyxel
   let config = try Config.Load(path) catch e: IOException -> Config.Default

   let level = try Level.Load(name)
       catch e: FileNotFoundException -> return
       finally { self.loading = false }
   ```
   - `try {` 是语句，`try` 后面跟别的就是值形式。值要和 `try` 写在同一行。
3. **`->` 在分支里只跟在一种情况后面**：`case 模式 ->`（`match`、`await all` / `await any`）和 `catch e: T ->`，读作“这种情况得到”。它另外的用处不变：函数的返回类型和函数类型 `func(A) -> R`。
4. **换行**：`then`、`else` 像运算符一样放行首（ADR-0019）。
   ```nyxel
   let message = if self.hp > 0
       then $"还剩 {self.hp} 点生命"
       else "已经倒下"
   ```
   - 当值用的 `if` / `try`，下一行以 `then`、`else`、`catch`、`finally` 开头时接着上一行。
   - `then`、`else` 写在行尾是编译错误（NYX1103，诊断提示挪到下一行开头）。`catch e: T ->` 的行尾照旧可以换行（ADR-0013 的“行尾是 `->`”）。
5. **写成原来的箭头是编译错误**（NYX1241）：`if c -> a` 的诊断说把 `->` 换成 `then`，`else ->`、`try ->` 的诊断说去掉 `->`。C# 的 `a ? b : c`（NYX1157）建议的写法跟着改成 `if a then b else c`。
6. **不加不带值的 `match`。** 几个条件依次判断用 `else if` 连写。以后要加不影响已有代码。
7. **语句形式的 `if` 不写 `then`**，照旧是 `if 条件 { ... }`。`if c then { ... }` 是编译错误（NYX1242），诊断说去掉 `then`。
   ```nyxel
   if self.hp <= 0 {                // 语句：{ 开始一段语句
       self.Die()
   }
   let label = if self.hp > 0 then "alive" else "dead"   // 值：then 开始一个值
   ```
   - 两种形式是同一个骨架：`if 条件` 后面，`{` 开始一段语句，`then` 开始一个值。

## 备选方案

- **维持 `if c -> a else -> b`**（ADR-0025 原来的决定）：不加关键字，值分支和 `match` 的分支用同一个记号；但 `else ->` 的箭头左边没有东西，嵌在 `match` 分支里一行三个箭头，别的语言没有这个写法。
- **只去掉 `else` 后面的箭头**（`if c -> a else b`）：改动最小，不加关键字；但两个分支一个带箭头一个不带，`match` 分支里仍是两个箭头。
- **去掉当值用的 `if`，只留 `match`**（加上不带值的 `match`）：少一种结构，但最常见的两选一从一行变四行，不能再当实参；写的人会退回“先给个值，再用 `if` 语句改”，和 ADR-0042 的“局部变量在声明处给值”相反。
- **Python 的 `a if c else b`**：值在条件前面，读到中间才知道它是有条件的。**`c ? a : b`**、**大括号分支 `if c { a } else { b }`**：ADR-0007、ADR-0025 否掉的，理由没变。
- **`try` 保留第一个箭头**（`try -> a catch e: T -> b`）：`if` 改了以后它是唯一一处“关键字后面直接跟箭头”，和 `else ->` 是同一个问题。
- **`then`、`else` 也可以写在行尾**：同一个式子两种断行，和运算符只放行首（ADR-0019）不一致。
- **语句形式也写 `then`**（`if c then { ... } else { ... }`，用户提出）：两种形式都是 `if 条件 then 分支 else 分支`，只差分支是块还是值，学了值形式的人顺手就会这样写。不选的理由：
  - 语句形式里 `{` 已经标出条件到哪结束，`then {` 把同一件事说两遍；值形式需要 `then`，是因为条件和值之间没有别的东西隔开。
  - 加在最常写的语句上：samples 里语句 `if` 38 处，当值用的 2 处。
  - `while c {`、`for x in xs {`、`match x {` 的 `{` 前面都没有词，只给 `if` 加会让它和别的块语句不一样。
  - 带花括号的语言都写 `if c {`，有 `then` 的语言要么没有花括号（Lua、Pascal），要么 `if` 只当表达式（F#、Haskell）；`then {` 没有先例，Agent 的条件反射是 `if c {`。
  - 现在的差别本身有信息：看到 `then` 就知道这个 `if` 在产生值。
  - 顺手写出 `if c then {` 的情况由 NYX1242 接住。
- **不带值的 `match` 现在就加**：`case when 条件 -> 值` 这类写法要另定，而 `else if` 连写已经能表达；samples 里还没有三路以上的例子。

## 后果

- 好处：`if c then a else b` 是 F#、Scala 3、Haskell、OCaml、Ada 的写法，读起来是一句话，Agent 更容易一次写对；箭头只剩“某种情况得到某个结果”一个意思。
- 代价：多一个保留字 `then`；当值用的 `if` 和 `match` 的分支记号不再相同（`then` / `else` 对 `->`），同 F#、Scala 3。
- 改了已合进 main 的 ADR（原文都已改成最新，见各自的修订记录）：
  - ADR-0025 决策 3（当值用的 `if` / `try` 的写法和换行）、决策 1 和例子，原来的箭头写法挪进它的备选方案。
  - ADR-0007 决策 4、ADR-0014 决策 4 的例子和说法；ADR-0026 决策 1 的例子。
- dev 上还没合并的直接改：ADR-0040、ADR-0042 里的例子和说法。
- language-reference：“总体风格”“换行与续行”“分支与离开语句”“if 与 eval”“错误处理”各节，和其余各节例子里的写法。
- 实现：
  - 词法：`then` 是关键字；高亮语法（tools/vscode-nyxel）加上它。
  - Parser：`ParseIf` 看到 `then` 走值形式，`ParseTry` 看到 `try` 后面同一行跟着值走值形式；`then`、`else` 在行尾报 NYX1103；原来的箭头报 NYX1241 后跳过，照新写法往下读；语句位置的 `if c then {` 报 NYX1242，跳过 `then` 照语句读。NYX1187、1189、1192、1157 的消息改成新写法。
  - 语法树：`IfExpressionSyntax` 的 `ArrowToken` 换成 `ThenKeyword`，去掉 `ElseArrowToken`；`TryExpressionSyntax` 去掉 `ArrowToken`。
  - Binder 还没实现当值用的 `if` / `try`，不受影响。
  - samples 三处、语法树快照和高亮快照各两个文件跟着改。
- 错误码见 design/diagnostics.md。
