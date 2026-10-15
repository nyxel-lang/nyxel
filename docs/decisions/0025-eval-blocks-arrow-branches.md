# ADR-0025: `eval` 块和 `give`；`if` / `try` 当值用时分支写 `->`

- 状态：已接受
- 日期：2026-10-08

## 背景

第二十轮讨论，由用户提出。ADR-0015 否掉了块形式的 `using (...) { }`，ADR-0020 否掉了语句位置单独的 `{ }`，理由是用处小、每个资源多一层缩进，要早点结束作用域就拆成函数。用户提议换个角度：一个关键字后面接块，块还在原来函数的作用域里，只是控制流不同；块里有类似 `return` 的语句给出一个值。这样只为算出某个值而用到的临时变量都关在块里，函数顶层只剩“算出 X”这样的步骤，结构更清楚。

这是其他语言里的块表达式：Rust 的 `{ ...; v }` 加带标签的 `break 'a v`，Zig 的 `blk: { break :blk v; }`，Kotlin 的 `run { }` 加 `return@run v`，Java switch 表达式分支块里的 `yield v`。C# 没有，只能拆函数或立即调用一个 lambda（会分配委托）。

讨论分三步：

1. 块本身：块里的 `return`、值怎么给、能不能当语句、关键字。块里的 `return` 按推荐定；值怎么给，用户提出既然加了关键字就统一都写 `give`；语句形式按推荐先不放开；关键字用户选了表示“求值”的 `eval`。
2. 用户提出 `if` / `match` 当值用时也统一用 `eval`。照字面每个分支都写 `eval { give x }` 太重，改成“分支只能是一个表达式，要写语句就用 `eval`”，用户同意。
3. 这样一来 `if c { "alive" } else { "dead" }` 里的大括号不再是块，只是括住一个值，用户觉得突兀，提出像 `match` 一样用 `->`。比较了 `->`、`then`、保持大括号，用户选了 `->`。

最后用户确认了两处细节：当语句用的 `match` 分支也能直接写离开语句；箭头形式的 `try` 的 `finally` 也能写在下一行开头。

## 决策

1. **`eval { ... }` 是一个表达式，块里用 `give 值` 给出它的值**。
   ```nyxel
   let damage = eval {
       if self.isInvincible {
           give 0
       }
       let reduced = hit.Amount - self.armor
       if reduced <= 0 {
           give 0
       }
       let crit = if hit.IsCritical -> 2 else -> 1
       give reduced * crit
   }
   // reduced、crit 到这里已经不可见；damage 是 let

   let text = eval {
       using stream = File.OpenRead(path)
       using reader = new StreamReader(stream)
       give reader.ReadToEnd()
   }   // 文件在这里就关了
   ```
   - 块里声明的名字（包括 `using`）只在块里可见；`using` 的资源在块结束时释放。
   - 每条路径都以 `give` 或 `throw` 结束，走到块末尾还没给值是编译错误。最后一句是没有 `give` 的表达式时，语法分析就报错；其余情况由语义分析检查（同 C# 的“并非所有代码路径都返回值”）。
   - `give` 交给离它最近的 `eval`，可以穿过中间的 `if`、`match`、`try`、循环，但不能穿过 lambda。`eval` 外面写 `give` 是编译错误。
   - 块里不能写 `return`；`break` / `continue` 只能用于块里面的循环，不能离开块。函数的出口因此都在顶层看得见。要在块里决定“结束函数”，就让块给出 `null`，在外面写 `?? return`：
     ```nyxel
     let target = eval {
         let candidates = self.Scan()
         if candidates.Count == 0 {
             give null
         }
         give candidates.MinBy(func(e) = e.Hp)
     } ?? return
     ```
     块里定义的 lambda 有自己的 `return`，不受这条限制。`throw`、`await` 照常可用。
   - `??` 的右边也能写 `give`：`let t = self.Find() ?? give null`（ADR-0009 决策 3 的扩展）。
   - `eval` 只能写在要值的地方（`let x = eval { }`、参数、`??` 的左边等），单独当一条语句是编译错误。
   - 块的类型是所有 `give` 的值的共同类型，同 `if` 表达式的各分支；有期望的类型（`let x: T = eval { }`）时按期望的类型。
   - `eval`、`give` 是保留字。
   - 翻译：C# 里先声明一个变量，块翻译成一个 C# 块，每个 `give` 是赋值加 `goto` 到块后面的标签。`goto` 离开 C# 的块时，块里的 `using var` 照样释放。不是 lambda，不分配。
2. **`{ }` 块本身永远没有值**。用语句算出一个值只有 `eval` 加 `give` 这一种写法。`if`、`match`、`try` 当值用时，每个分支只能是一个表达式或一条离开语句：
   - 离开语句就是 `??` 右边能写的那几种：`return`、`throw`、`break`、`continue`，`eval` 里再加 `give`。
   - 分支要先执行语句时，这个表达式就是一个 `eval` 块；`if` 的分支要写语句时，通常把整个 `if` 改写成 `eval`，在里面用普通的 `if` 语句。
3. **`if` / `try` 当值用时分支写 `->`，当语句用时照旧写 `{ }`**。
   ```nyxel
   let label = if self.hp > 0 -> "alive" else -> "dead"

   let speed = if self.IsFrozen -> 0
       else if self.IsHasted -> self.BaseSpeed * 2
       else -> self.BaseSpeed

   let config = try -> Config.Load(path)
       catch e: IOException -> Config.Default

   let text = try -> File.ReadAllText(self.path) catch e: FileNotFoundException -> return 0
   ```
   - 看到 `if ... ->` 就知道这个 `if` 在产生值，看到 `if ... {` 就知道是在执行语句。写反了是编译错误：`{ }` 形式写在要值的地方，或者 `->` 形式单独当语句。同一个 `if` 的分支不能两种混用。
   - `->` 形式必须有 `else`。`try` 至少有一个 `catch` 或 `finally`；`finally` 不产生值，照旧写 `finally { 语句 }`。
   - 换行：下一行以 `else`、`catch` 或 `finally` 开头时接着上一行（只对 `->` 形式；`{ }` 形式照旧要跟在 `}` 后面同一行）。行尾是 `->` 时接着下一行（ADR-0013）。
   - 实现时发现：`match` 的默认分支也叫 `else`，`case 1 -> if b -> 1` 下一行的 `else -> 2` 会被分支里的 `if` 吞掉。先定为“直接在 `match` 分支里时行首的 `else` 归 `match`”，review 时觉得这是看不见的规则，改成默认分支写 `case _`，`else` 只属于 `if`（ADR-0026）。
   - 范围：最后一个分支一直延伸到表达式结束，同 C# 的 `?:`：`if c -> a else -> b + 1` 的 else 分支是 `b + 1`。所以 `->` 形式的 `if` / `try` 拿来当运算数，或者直接写在另一个 `if` / `try` 的分支里时，必须加括号；`else if` 连写不算。这挡住了“悬挂 else”：
     ```nyxel
     let total = (if hasBonus -> 2 else -> 1) * base        // 正确
     let x = if a -> (if b -> 1 else -> 2) else -> 3         // 正确
     let x = if a -> if b -> 1 else -> 2 else -> 3           // 错误：分不清 else 属于哪个 if
     ```
     `match` 分支、函数参数、`=` 后面都是完整的表达式位置，不用加括号：`case Heal(amount) -> if amount > 100 -> 100 else -> amount`。
4. **`match` 分支 `->` 的右边按用法定：当值用时是一个表达式或一条离开语句；当语句用时是一条简单语句或一个 `{ }` 块**。
   ```nyxel
   let change = match hit {
       case Physical(amount) -> -amount
       case Burn(amount, seconds) -> eval {
           self.StartBurning(seconds)
           give -amount
       }
       case Heal(amount) -> amount
   }

   match self.state {
       case Dead -> return                 // 离开语句
       case Idle -> self.Wander()          // 调用
       case Hurt -> self.speed = 0         // 赋值
       case Hunting -> {
           self.Chase()
       }
   }
   ```
   - 当值用的 `match` 分支写 `{ }` 块是编译错误，诊断提示写成 `-> eval { ... give 值 }`。
   - 简单语句指一行写完、不带块的语句：表达式语句（调用、`await`、`launch`）、赋值（含 `+=`、`??=`）、`emit`、离开语句。`let` / `using`（名字出了分支就用不上）和带块的 `if` / `for` / `while` / `match` / `try` 直接写在 `->` 后面是编译错误，要放进块里。
   - 一开始定的是“一个表达式或离开语句，其余放进块”，于是调用能直接写、赋值要包块，读者要记“表达式可以、语句不行”这条细线，而赋值是状态机里最常见的分支。review 时（和 ADR-0026 同一轮）改成按用法说。

## 备选方案

- **块的关键字**：
  - `do`（Swift 的作用域块，Swift 社区也讨论过让它当表达式）：短，但 C# 读者第一反应是 `do { } while`，ADR-0020 否掉 `do { }` 就是这个理由。
  - `run`（Kotlin）：语义完全一样，但 C# 的 `Task.Run` 是“丢到别的线程去跑”，游戏脚本里容易读成启动一段协程。
  - `block`：体素类游戏里是常见的变量名。
  - `compute`：没有别的含义，但长一些，游戏开发里容易想到 compute shader。
  - 选了 `eval`：就是“求值”这个词。JavaScript、Python 等语言的 `eval("...")` 在运行时执行一段代码字符串，读者第一眼可能误会；但 `eval` 后面是大括号和语句，照那些语言的习惯写 `eval("...")` 会直接编译报错，不会写出能编译却意思不对的代码。
- **给值的关键字**：
  - `yield`（Java switch 表达式）：Unity 用户看到 `yield` 先想到 `yield return null` 协程，以后加迭代器也没词可用了。
  - `then`（Swift 社区的提案）：`if c { then 0 }` 容易读成这个 `if` 的值。
  - `break 值`（Rust、Zig）：块里有循环时，`break` 和 `break 值` 指向不同的东西，只差有没有值；Rust 靠标签区分，ADR-0021 没有标签。
- **块里的 `return`**：
  - 从函数返回（Kotlin 的 `run`）：最贴近“还在原来的函数里”，但想给块值时误写 `return 0`，函数返回类型恰好相同时就悄悄跳过了后面的代码。Kotlin 的 `forEach { return }` 是出名的坑。
  - 从块返回（JavaScript 立即调用函数的习惯）：不用新关键字，但想结束函数的人写 `return` 只跳出了块，读者在长函数里看到 `return` 也要先往上找自己在不在块里。
  - 禁止：两个方向的误写都在编译期抓住，函数的出口都在顶层。
- **块的值**：最后一个表达式是值，`give` 只用于提前给值。和当时 `if` / `match` 的规则一致，但同一个块有两种给值的写法；用户提出统一写 `give`，长块里给值的地方都有标记，能搜索。
- **不要值的 `eval` 当语句**：和 `if` 既能当语句又能当表达式一致，写文件这类没有结果的 `using` 也能早点释放。但没有值的块就是 ADR-0020 否掉的单独的块，那时的理由（用处小）仍然成立；读完就释放的常见情况有值的形式已经覆盖。先不放开，以后放开不会让已有代码失效。
- **不加关键字，`{ ... }` 直接当值（Rust）**：ADR-0020 否掉了单独的块；有关键字时开头一眼可见，也能搜索。
- **`if` / `match` 的分支也写 `give`**：`if c { give "alive" } else { give "dead" }`，短分支只是多了字；`give` 还会有两个去向，`eval` 里嵌着当值用的 `if` 时要判断它给的是哪一个。
- **`if` / `match` 的每个分支都写 `eval`**：`if c { eval { give "alive" } } else { ... }` 太重；现有的 `if` / `match` 表达式几乎都是一个值的分支。
- **单值分支保持大括号** `if c { "alive" } else { "dead" }`：不用改，但大括号有时是块、有时只是括住一个值。
- **`then`**：`if c then a else b`（Scala 3、F#、OCaml、Haskell），读起来最像英语，嵌套时不堆箭头。但要多一个关键字，`try` 也用不上 `then`，分支就有了两种标记。选 `->` 是因为 Nyxel 已经规定“分支用 `->`”（`match` 的每个分支），Kotlin 不带参数的 `when { c -> a; else -> b }` 也是这个样子；代价是嵌在 `match` 分支里时一行箭头较多。
- **当语句用的 `match` 分支不能直接写离开语句**（照旧报 `NYX1123`）：同一个 `match` 会因为当值用还是当语句用，允许的写法不同。
- **当语句用的 `match` 分支一律写 `{ }`、不写 `->`**（`case Idle { self.Wander() }`）：`->` 就能在全语言里只表示“产生值”，和 `if` 一致；但每个分支都带花括号，同一个 enum 当值和当语句 `match` 时形状不同，一行的 `{ return }` 是噪音。

## 后果

- 改了以下已合进 main 的内容（原文都已改成最新，见各自的修订记录）：
  - ADR-0007 决策 4 的“分支块里最后一个表达式就是这个分支的值”。“当表达式用时必须有 `else`”“没有 `?:`”“函数体必须用 `return`”不变。
  - ADR-0009 决策 5 的“分支的右边是一个表达式或一个块；`match` 当表达式用时，块的值是它最后一个表达式”。
  - ADR-0014 决策 4 里 `try` 表达式的写法和“每个分支的最后一个表达式是值”。
  - ADR-0015 决策 3 和 ADR-0020 决策 6 里“要更早释放就拆成函数”：现在也可以用 `eval`。单独的 `{ }` 仍然不允许。
- `match` 分支直接写 `return` 等离开语句不再报 `NYX1123`；`NYX1157`（`?:`）的建议改成 `if c -> a else -> b`。新的错误码见 design/diagnostics.md。
- 语法分析能查的在语法分析查：`eval` 当语句、`eval` 外的 `give`、`eval` 里的 `return` 和离开块的 `break` / `continue`、`eval` 最后一句没有 `give`、两种 `if` / `try` 写反或混用、当值用的 `match` 写块分支、`->` 形式要加括号的地方。留给语义分析（M2）：每条路径都以 `give` / `throw` 结束；块的类型；翻译成 C#。
- 语法树：`{ }` 形式的 `if` / `try` 是语句 `IfStatementSyntax` / `TryStatementSyntax`；`->` 形式是表达式 `IfExpressionSyntax` / `TryExpressionSyntax`；新增 `EvalExpressionSyntax`、`GiveStatementSyntax`，`??` 右边和分支里的 `give` 是 `GiveExpression`。
