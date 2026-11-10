# ADR-0041: 空值的五处细节：赋值后收窄、`int?` 只有 `==` 提升、没有成员、多余的检查是警告、没标注的程序集只管最外层

- 状态：已接受
- 日期：2026-11-09

## 背景

2026-11-06 实现 Binder 的空值和收窄（semantic-analysis.md“实现状态 · 空值和收窄”）时，碰到五处 ADR-0009、ADR-0023 没有说清的地方。实现先按最严的报错（以后放开不会让写好的代码编不过），列给用户，这一轮（第三十四轮）逐项定下。

- **赋值会不会让变量收窄**。ADR-0009 只说“给 `var` 局部变量重新赋值后，收窄失效”，没说赋一个不可能是 null 的值之后算不算检查过。
- **`int?` 的运算符**。ADR-0009 写着“值类型的 `int?` 与 C# 相同”，又说“使用 `T?` 的成员是编译错误”。C# 会把运算符“提升”到 `int?` 上：`n + 1` 在 `n` 是 null 时得 null，`n < 3` 得 false。这和“`T?` 用之前要检查”相冲突。
- **`int?` 自己的成员**。`int?` 在 .NET 里是结构体 `Nullable<int>`，有 `.HasValue`、`.Value`（null 时抛异常）、`.GetValueOrDefault()`。
- **对不会是 null 的值做 null 检查**（`target == null`，`target: Enemy`）。按类型永远不成立；可是 Nyxel 的 `public` 函数可能被 C# 调用，C# 传 null 只给警告，运行时真可能是 null。
- **没标注的程序集**。ADR-0009 定了返回值和字段当 `T?`，没说 `List<Enemy>` 里的 `Enemy`、数组的元素。

## 决策

1. **给局部变量赋一个不可能是 null 的值之后，它当作非空；赋了可能是 null 的值，之前的检查作废。** 声明时的初始值也算。
   ```nyxel
   var target: Enemy? = self.target
   if target == null {
       target = self.FindNearest()     // FindNearest 返回 Enemy
   }
   target.Attack()                     // 两条路径上都不是 null

   var node: Node? = self.first        // first 是 Node：node 当作非空
   node = node.next                    // next 是 Node?：之后又要检查
   ```
   - 只管是不是 null，不按值的具体类型收窄：`var weapon: Weapon? = new Sword()` 之后 `weapon` 是 `Weapon`，不是 `Sword`，同 C#。类型的收窄只来自 `is`（ADR-0010）。
   - 循环里的规则不变：循环体里赋过值的变量，在循环开头不算收窄（semantic-analysis.md“流分析”）。
   - 被 lambda 捕获的 `var` 局部变量不收窄，赋了非空的值也一样（ADR-0042）。
2. **`T?` 的运算符：只有 `==` / `!=` 像 C# 那样接受 `int?`，算术、大小比较、位运算都不接受。**
   - 两边去掉 `?` 以后能用 `==` 比的（数字、`bool`、`char`），带 `?` 也能比，规则同 C#：

     | 左边 | 右边 | `a == b` | `a != b` |
     |---|---|---|---|
     | null | null | true | false |
     | null | 有值 | false | true |
     | 有值 | null | false | true |
     | 有值 | 有值 | 两个值相等 | 两个值不等 |

     `a != b` 永远等于 `not (a == b)`。
   - 读的时候注意 null 落在哪一边：`self.target?.IsDead != true` 在没有目标时也是 true，意思是“不能确定已经死了”；要“有目标而且活着”写 `self.target?.IsDead == false`。
   - `hp + 1`、`hp < 10`（`hp: int?`）是编译错误（NYX2057），先检查或者给默认值：`(hp ?? 0) + 1`、`if hp != null and hp < 10`。
   - 引用类型的 `a == b`（`Enemy?` 和 `Enemy`）本来就是比引用，不变；和字面量 `null` 比较仍是 ADR-0023 的“只看引用”。
3. **`T?` 没有成员，`int?` 也一样**：`.Value`、`.HasValue`、`.GetValueOrDefault()` 都是 NYX2057。分别写成检查后直接用、`!= null`、`?? 默认值`；要断言有值写 `?? throw`（ADR-0009 决策 4）。
4. **对不会是 null 的引用类型写 `== null`、`!= null`、`??`、`?.`、`??=` 是警告**，含义照 C#（`== null` 仍是只看引用）。
   - 警告说明“类型说它不会是 null”，读者能看出这个检查是多余的；在和 C# 交界的地方（C# 调用 Nyxel 的 `public` 函数）照样可以写防御性的检查。
   - 不可空的值类型（`count: int`）照旧是错误（NYX2063）：`int` 根本装不下 null，C# 对 `??`、`?.` 也报错。
   - 编译器还没有警告（第一批是命名检查，ADR-0006、0035），在那之前不报。
5. **没有标注的程序集只管最外层**：返回值、字段、属性、参数当 `T?`；类型实参和数组元素照程序集写的。`List<Enemy> GetEnemies()` 是 `List<Enemy>?`，检查一次以后元素就是 `Enemy`。

## 备选方案

- 赋值后收窄：
  - **不收窄**（实现时的暂定）：只有 `!= null`、`is`、`??`、`??=` 能收窄。上面的例子要改写成 `let t = self.target ?? self.FindNearest()`；按 C# 习惯写的 Agent 会被拒。ADR-0040 决策 4 也已经写过“重新赋值后再检查，收窄跟着流程走，同 C#”。
  - **按值的具体类型收窄**（TypeScript、Kotlin）：`var w: Weapon? = new Sword()` 之后能调用 `Sword` 的方法。声明了 `Weapon?` 却当 `Sword` 用，读者要往回找最后一次赋值；C# 也不这样做。
- `int?` 的运算符：
  - **全部提升**（同 C#，原来“值类型的 `int?` 与 C# 相同”的意思）：null 悄悄地沿着算术传下去，和“`T?` 用之前要检查”矛盾；大小比较在 null 时 `hp < 10` 和 `hp >= 10` 都是 false，`not (hp < 10)` 却是 true，容易写出 bug。
  - **都不提升**（实现时的暂定）：`x?.IsDead == true` 这种 C# 程序员和 Agent 的高频写法也要改写，而 `==` 没有上面的陷阱。
- `int?` 的成员：
  - **都能用**（同 C#）：`.Value` 就是 ADR-0009 不要的强制取值，失败时没有原因。
  - **只禁 `.Value`**：`.HasValue`、`.GetValueOrDefault()` 和 `!= null`、`??` 是同一件事的第二种写法；`int?` 和 `Enemy?` 的规则也就不一样了。
- 多余的 null 检查：
  - **错误**（实现时的暂定）：和 C# 交界的地方没法写防御性的检查。
  - **照 C# 允许，不提示**：Agent 习惯性地到处加检查，读者看不出哪些是必要的。Kotlin、Swift 也是给警告。
- 没标注的程序集：
  - **每一层都当 `T?`**：最保险，但 `Dictionary<string, List<Enemy>>` 成了 `Dictionary<string?, List<Enemy?>?>?`，键本来就不能是 null，几乎没法用。老库里“返回 null”常见，“集合里放 null”少见；M4 还有按程序集“当作非空”的开关（ADR-0040 决策 4）。

## 后果

- 改了已合进 main 的 ADR-0009：决策 1（`int?` 的运算符和成员、没标注的程序集只管最外层）、决策 2（赋值后收窄），原来的“值类型的 `int?` 与 C# 相同”挪进它的备选方案。
- language-reference“空值”一节改写。
- 语义分析（M2）：决策 1、2、3、5 随本 ADR 实现；决策 4 现在不报，“不可空的值类型”那半照旧报 NYX2063，警告等编译器有了警告再加（semantic-analysis.md“实现状态”）。
- 写 samples 时观察：`var current = node`（`node` 已经收窄）推断出的类型是 `Node`，之后 `current = current.next` 编不过，要写 `var current: Node? = node`。这是“局部变量的类型就是初始值的类型”的直接结果；C# 的 `var` 在这里推断成可空的。后来定了（ADR-0042 决策 6）：规则不变，赋 null 时的诊断（NYX2069）直接给出 `var current: Node? = node`。
