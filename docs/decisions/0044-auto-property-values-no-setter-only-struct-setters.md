# ADR-0044: 自动属性的值同字段，属性必须有 `get`，struct 上给属性赋值算“改”

- 状态：已接受
- 日期：2026-11-11

## 背景

第三十七轮讨论。M2 加宽 Binder、实现属性和 struct 时，碰到三处 ADR 没写到的规则（按 semantic-analysis.md“写设计时定下的语言规则”，先停下来讨论）：

- ADR-0011 定了自动属性的初始值“写在类型后面”，没说没写初始值行不行，也没说只有 `get` 的自动属性能不能在 `init` 里给值。它常用来实现接口要求的只读属性（`property Health: int { get }`），值往往来自 `init` 的参数。
- ADR-0011 没说只有 `set`、没有 `get` 的属性行不行。C# 允许计算属性只写 `set`（自动属性只写 `set` 是 CS8051）；.NET 的设计准则说不要提供只写属性；Swift、Kotlin 的属性都必须能读。
- ADR-0032 决策 1 说 `let` 的 struct 和副本不能“改”，“改”指给字段赋值、调用 `var self` 方法，没提属性的 setter。setter 是一个会改自己的函数，C# 也这么算：给 `foreach` 变量的属性赋值是 CS1654，给属性取出来的 struct 的属性赋值是 CS1612。

三项都按推荐定。

## 决策

1. **自动属性的值同字段。** 只有 `get` 的自动属性同 `let` 字段：在声明处或本类型的 `init` 里（经 `self.`）赋值，之后只读；带 `set` 的同 `var` 字段。初始值可以不写，规则也同字段：类型没有 `init` 时必须写（NYX2049），有 `init` 时每个 `init` 结束前都要赋过值（NYX2050）。

   ```nyxel
   class Slime : IDamageable {
       public property Health: int { get }            // 同 let 字段
       public property Score: int = 0 {               // 同 var 字段，外面只读
           get
           private set
       }

       init(hp: int) {
           self.Health = hp                           // 能：init 里给值
       }

       func Hit(self) {
           self.Health -= 1                           // 错误：Health 只有 get
           self.Score += 1                            // 能：类型里面能用 private set
       }
   }
   ```

   - C# 那边同 `public int Health { get; }`：只读的自动属性能在构造函数里赋值。

2. **属性必须有 `get`。** 只写 `set` 是编译错误，计算属性、自动属性、索引器、接口里的属性都一样；诊断建议写成函数。

   ```nyxel
   public property Volume: float {
       set(v) { Audio.SetVolume(v) }              // 错误：属性要有 get
   }

   public func SetVolume(self, v: float) {        // 改写成函数
       Audio.SetVolume(v)
   }
   ```

   - 属性在读的人眼里是一个值，能写却读不出来的值让人困惑；“只能写”的操作用函数表达更清楚。
   - C# 里只有 `set` 的接口属性在 Nyxel 里没法实现，等 M3 实现接口时真碰到再看。

3. **struct 上给属性赋值算“改”，同 C#。** getter 同 `self` 方法（不能改自己），setter 同 `var self` 方法（会改自己）：

   ```nyxel
   struct Pos {
       var x: float = 0
       public property X: float {
           get = self.x
           set(v) { self.x = v }
       }
   }

   let p = new Pos()
   p.X = 3                     // 错误：p 是 let
   self.list[0].X = 3          // 错误：list[0] 是副本，改动会丢
   var q = self.list[0]        // 先存进 var，改完写回
   q.X = 3
   self.list[0] = q
   ```

   - ADR-0032 决策 1 的表照旧，“改”指给它的字段或属性赋值、调用它的 `var self` 方法。
   - struct 的 `self` 方法里不能给 `self` 的属性赋值，`var self` 方法和 `init` 里可以。
   - 自动属性的 setter 也算：它改的是编译器生成的那个字段。
   - C# 那边：Nyxel struct 的 getter 是 `readonly` 访问器，同 `self` 方法翻译成 `readonly` 方法（ADR-0008 决策 6）。

## 备选方案

- 自动属性：
  - **必须写初始值，只读的在 `init` 里也不能改**：规则更少，但实现接口的只读属性没法按 `init` 的参数给值，要写成一个 `let` 字段加一个计算属性，两行表达一件事；和 `let` 字段的规则也不一致。
- 只有 `set` 的属性：
  - **同 C#**（计算属性可以只有 `set`，自动属性不行）：少一条规则，能实现 C# 里只有 `set` 的接口属性；但只写属性在 .NET 设计准则里就是不推荐的写法，读的人看到 `self.Volume = 0.5` 会以为能读回来。
- struct 上的属性赋值：
  - **不算“改”，只按 ADR-0032 字面的范围查字段赋值和 `var self` 方法**：`let p` 上的 `p.X = 3` 会悄悄改掉 `let` 的值；`list[0].X = 3` 编译通过但改的是副本，改动丢了，就是 Unity 的 `transform.position.x = 1` 那个坑，ADR-0032 本来要堵住的。

## 后果

- 改了已合进 main 的 ADR（原文都已改成最新，见各自的修订记录）：
  - ADR-0011 决策 2：补上自动属性的值同字段、属性必须有 `get`。
  - ADR-0032 决策 1 和 ADR-0008 决策 6：“改”包括给属性赋值。
- language-reference：“属性”“函数与方法”两节。
- 实现（M2）：
  - 第 4 遍声明属性：访问器有的带体有的不带、计算属性写了初始值、没有 `get`、`get` / `set` 重复、`set` 的参数（计算属性要写 `set(value)`，自动属性不写）、访问器的可见性（同 C#：只能有一个访问器写，属性要有两个访问器，写的要比属性本身更严）。
  - 第 5 遍：访问器的函数体（getter 里 `self` 只读，setter 里能改），自动属性的初始值；只读的自动属性按 `let` 字段查赋值，`init` 的确定赋值把没有初始值的自动属性算进去；外面用不了的 `private set` 报 NYX2029。
  - struct：表达式是存储、只读的存储还是副本（ADR-0032 决策 1 的表），在后两种上给字段或属性赋值、调用 `var self` 方法报错；C# 里没标 `readonly` 的方法按 ADR-0032 决策 3 查。
  - 降级：属性是 C# 属性（自动属性 `{ get; private set; } = 初始值`），setter 的参数不叫 `value` 时在开头复制一份；struct 的 `self` 方法和 getter 加 `readonly`。
- 错误码见 design/diagnostics.md。
