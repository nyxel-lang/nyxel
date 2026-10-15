# ADR-0027: 创建时设置成员 `with (...)`、调用其他 init 写 `init super(...)`

- 状态：已接受
- 日期：2026-10-15

## 背景

第二十一轮讨论，“待定”的第五轮：类型成员。七项分两批，这是第一批“创建对象”的三项：

- **创建时设置成员**（C# 的对象初始化器 `new Spin { Speed = 2 }`）。ADR-0020、ADR-0022 把它留作待定，暂时是编译错误，要“创建后再给字段赋值”。查 EnginePlayground 时发现三个问题：
  - 真实脚本就这么写：给实体加组件 `world.Commands.Add(e, new Spin { Axis = ..., Speed = ... })`。ECS 组件是字段带默认值的 struct，创建时改几个，这是最常见的形状；分开写要多一个 `var` 局部变量和几行赋值。
  - C# 的 `init` 访问器（`{ get; init; }`）和 `required` 成员只能在对象初始化器里赋值。EnginePlayground 的编辑器里有 30 多处 `init;`，宿主里有 `required string Name`。没有这个语法，Nyxel 创建不了这类对象，是 .NET 互操作的缺口。
  - 分开写时变量只能是 `var`；`let` 的 struct 不能改字段（ADR-0008）。
- **只能在创建时设置的成员**：C# 的 `init` 访问器和 `required`，要不要在 Nyxel 里声明。`init` 已经是构造函数的关键字，访问器不能再用这个词。
- **调用基类的命名 init**（ADR-0013 留下的待定）：基类只有命名 init 时，子类根本没法构造基类部分；同一类里委托给命名 init 的写法也没定。

第 1、2 项按推荐定。第 3 项原推荐 `super.init.Homing(...)`，用户提出 `init super.Homing(...)`；比较后改为用户的写法，并且不带名字的也一起改。

## 决策

1. **创建时设置成员写 `new 类型(实参) with (成员 = 值, ...)`。**

   ```nyxel
   world.Commands.Add(entities[i], new Spin() with (
       Axis = Vector3.Normalize(axis),
       Speed = 0.3 + rng.NextSingle() * 1.7))

   let psi = new ProcessStartInfo(fileName = "git")
       with (Arguments = "status", RedirectStandardOutput = true)
   ```

   - `()` 里是 init 的实参，`with ()` 里是构造完以后要设的成员。先执行 init，再按书写顺序赋值，同 C# 的对象初始化器。
   - 能设的：外面能访问的 `var` 字段、有 `set` 的属性、C# 的 `init` 访问器。`let` 字段和只读属性不能设。
   - C# 类型的 `required` 成员必须写在 `with` 里，漏了是编译错误，诊断列出缺哪些（C# 标了 `[SetsRequiredMembers]` 的构造函数除外）。
   - 每一项都写 `名字 = 值`，同带名字的元组值；C# 的 `名字: 值` 报错（NYX1155）。
   - `with` 是关键字。现在只能跟在 `new 类型(...)` 后面（`()` 不能省）；写在别的值后面是编译错误（NYX1198）。
   - 下一行以 `with` 开头时接着上一行，同 `.`。
   - 没有 C# 的嵌套初始化器（`new A { B = { X = 1 } }`）和集合初始化器（用集合字面量，ADR-0022）。C# 的 `new T { X = 1 }`、`new List<int> { 1 }` 仍是 NYX1167，诊断按内容分别给出 `with` 和集合字面量的写法。
   - 翻译：C# 的对象初始化器 `new Spin() { Axis = ..., Speed = ... }`。

2. **Nyxel 暂不提供自己声明“只能在创建时设置”的成员**（C# 的 `init` 访问器、`required`）。
   - C# 定义的这类成员照常使用：`init` 访问器只能在 `with` 里设，`required` 必须设。
   - Nyxel 里“创建后不能改”用 `let` 字段加 `init` 参数；“必须给值”由确定初始化检查保证（ADR-0008）。缺的只是“不能改，但用 `with` 的写法创建”这个组合，主要用处是给 C# 用户写库。以后需要时再找访问器的关键字。

3. **调用其他 init 写 `init` 开头的语句**，取代 ADR-0013 决策 2 的 `super.init(...)` / `self.init(...)`：

   ```nyxel
   public class Missile : Projectile {
       public init(target: Entity) {
           init super.Homing(power = 10, target = target)  // 基类的命名 init
           ...
       }

       public init Dud() {
           init self(target = Entity.None)                 // 本类不带名字的 init
       }
   }
   ```

   | | 写法 |
   |---|---|
   | 基类，不带名字 | `init super(实参)` |
   | 基类，命名 | `init super.名字(实参)` |
   | 本类委托，不带名字 | `init self(实参)` |
   | 本类委托，命名 | `init self.名字(实参)` |

   - 规则同 ADR-0013：只能是 `init` 的第一句；参数里不能用 `self`；没写时自动调用基类的无参 `init`；委托了的 `init` 不再调用基类的 `init`。
   - 和创建对象对称：`new Projectile.Homing(...)` 创建一个完整的对象，`init super.Homing(...)` 构造自己的基类部分。
   - `super(...)`、`self(...)` 只出现在 `init` 后面，别处的 `super`、`self` 后面一定是 `.`。
   - 写成 `super.init(...)` / `self.init(...)`（Swift，也是 ADR-0013 的旧写法）是编译错误（NYX1200），诊断给出新写法。
   - 命名 init 不继承，同不带名字的 init：子类没声明 `Homing` 时 `new Missile.Homing(...)` 是编译错误。C# 那边命名 init 是静态方法，C# 会让 `Missile.Homing` 找到基类的那个、返回一个 `Projectile`，所以要由 Nyxel 拦住。
   - 翻译：`init super(...)` 是 `: base(...)`，`init self(...)` 是 `: this(...)`。命名 init 背后的隐藏构造函数（ADR-0008）对子类可见（protected），`init super.Homing(...)` 翻译成 `: base(...)` 到它。

## 备选方案

- 创建时设置成员：
  - **照搬 C# 的花括号 `new Spin() { Axis = a, Speed = s }`**：C# 程序员和 Agent 一次写对。但 Nyxel 的花括号里一直是一行一条语句（ADR-0005、0025），这里变成逗号分隔的值；`Axis = ...` 看着像给不带接收者的名字赋值，而 Nyxel 规定那只能是局部变量；`if pos == new Vector3() { X = 1 } {` 两个 `{` 分不清，要像 Rust 那样再加一条“条件里要加括号”的规则。
  - **合进 init 的命名实参 `new Spin(Axis = a, Speed = s)`**：最短，带数据的 case（`new Damage.Burn(Amount = 12)`）和 C# 的特性（`[Obsolete("x", DiagnosticId = "Y")]`）也这么写。但同一对括号里哪些是 init 的参数、哪些是构造完才赋的值，只能靠大小写分辨，两者的时机还不同（init 里的检查看不到后设的值）；写错名字时编译器也说不清你想传参数还是设成员。违反 ADR-0003 的第一条。
  - 选 `with (...)`：两步在写法上分开；圆括号加 `名字 = 值` 已经用于命名实参和带名字的元组，花括号继续只装语句；`with` 借自 C# 的 `with` 表达式（复制一份再改几个成员），含义一致，以后放开到任何值上（复制 struct）不用新语法。代价是多一个关键字，写惯 C# 的 Agent 第一次会写成花括号。
- 只能在创建时设置的成员：**现在就加，访问器另起一个词**：没有真实需求撑着，词也不好找；还没定的写法是编译错误，以后加不会让已有代码失效。
- 调用基类的命名 init：
  - **`super.init.Homing(...)`**（原推荐）：不带名字的 `super.init(...)` 不用改；`.Homing` 和 `new Projectile.Homing(...)` 的后缀同形。但要读到中间的 `.init` 才知道这一行在构造。
  - **`super.Homing(...)`**（Dart）：最短，但 `super.名字(...)` 在 Nyxel 里是调用基类的方法，同一个写法在 init 第一句和别处含义不同。
  - **`super.init Homing(...)`**：和声明 `init Homing(...)` 一样，但表达式里两个名字用空格隔开，别处没有这种形状。
  - **只有命名的写成 `init super.Homing(...)`，不带名字的保留 `super.init(...)`**：同一件事两种形状。
  - 选 `init` 开头（用户提议）：行首的关键字说明这一行在做什么，同 `emit self.Died(...)`、`launch`、`give`、`return`；`super.Homing` 本来像方法调用的问题由前面的 `init` 解决；和 `new 类型.名字(...)` 对称。代价是改 ADR-0013 的写法（已合进 main），习惯 Swift 的人和 Agent 第一反应是 `super.init(...)`，会撞一次错误。

## 后果

- 取代 ADR-0013 决策 2 的 `super.init(...)` / `self.init(...)` 写法（规则不变）。ADR-0013 已合进 main，不改原文。ADR-0022 留下的“对象初始化器”待定由决策 1 解决。
- 语法树：`ObjectCreationExpressionSyntax.WithClause`（`WithClauseSyntax`，元素复用 `ArgumentSyntax`）；`InitStatementSyntax`（`init`、`self` / `super`、可选的 `.名字`、实参列表）。`init` 不再能当成员名。
- 新错误码 NYX1198（`with` 不在 `new` 后面）、NYX1199（`with` 里的一项没写名字）、NYX1200（`super.init(...)` / `self.init(...)`）；NYX1167 的消息按内容给出 `with` 或集合字面量。
- 留给语义分析（M2）：`init super` / `init self` 只能是 `init` 的第一句、`with` 里的成员能不能设、`required` 是否齐全、命名 init 不继承。
- 待定：`with` 用在已有的值上（复制一份再改几个成员，C# 的 `with` 表达式）；Nyxel 自己声明只能在创建时设置的成员。
