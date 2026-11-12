# ADR-0045: case 名写在 `==` 哪一边都报错、enum 能比大小不能做算术、enum 里能写 `static let`

- 状态：已接受
- 日期：2026-11-12

## 背景

第三十八轮讨论。M2 实现不带数据的 enum 时（progress/M2.md 2026-11-12）碰到三处 ADR 没写清的地方。实现时先按最保守的方式处理，做完再和用户讨论：

1. ADR-0040 决策 2 说判断是不是某个 case 一律写 `is`，又说“只挡右边直接写 case 名这一种形式”。`SlimeState.Idle == state` 问的是同一件事，原文没有管它。
2. enum 能不能比大小、做加减，ADR 没写。C# 都能：`level >= LogLevel.Warning`（EnginePlayground 的 `LogLevel` 就是按严重程度排的）、`e + 1`、`e - E.A`（得到 `int`）。ADR-0024 只规定了位运算：Nyxel 的 enum 只有 flags enum 能用，“因为结果不是任何一个 case”。实现时先都报 NYX2021。
3. NYX2056（ADR-0040 决策 3）建议把别名写成 `static let Old: E = E.New`，可 ADR-0029 决策 4 规定 enum 里不能写字段（NYX1206），照着诊断写反而报错；ADR-0040 也没说别名写在哪。

三项都按推荐定了。

## 决策

1. **case 名写在 `==` / `!=` 的左边同样是编译错误**（NYX2055），诊断给出 `x is 类型.Case`。修改 ADR-0040 决策 2 的“只挡右边”。
   ```nyxel
   if state == SlimeState.Idle { }    // 错误：写 state is SlimeState.Idle
   if SlimeState.Idle == state { }    // 错误：同上
   if state == self.previous { }      // 两个值：照旧 ==
   ```
   - 规则仍是一句话：`==` 的一边直接写 case 名，就是在判断 case，写 `is`。
   - 别名（决策 3）也算 case 名。
2. **同一个 enum 的两个值能比大小（`<` `>` `<=` `>=`），不能做算术。**
   ```nyxel
   if level >= LogLevel.Warning { ... }            // 按值比较，同 C#
   let next = (level as int + 1) as LogLevel       // 要算就先 as int，再转回来
   let n = level + 1                               // 错误（NYX2087）：结果可能不是任何一个 case
   ```
   - 比较只回答“谁在前”，不产生新的 enum 值；算术会产生，理由同 ADR-0024 对位运算的限制。
   - 按底层的值比，同 C#：没写值的 enum 就是声明的顺序。
   - Nyxel 和 .NET 的 enum 都一样，flags enum 也能比。`E?` 照 ADR-0041 不能比，只有 `==` / `!=` 提升。
   - .NET 的枚举同样不能做算术（C# 能），要算也先 `as int`。位运算对 .NET 的枚举放开，是因为引擎常忘了标 `[Flags]`（ADR-0024）；算术没有这样的理由。
3. **enum 里能写 `static let`**（修改 ADR-0029 决策 4 的“不能写字段”）。
   ```nyxel
   enum Tile {
       case Floor = 1
       case Door = 2

       public static let Gate: Tile = Tile.Door    // 别名：Door 的另一个名字
       public static let Default: Tile = Tile.Floor
   }

   if tile is Tile.Gate { }                         // 别名同 case 一样写在 is 后面
   ```
   - `static let` 不是 enum 值的一部分，不违背“enum 的值就是它的 case”。实例字段、`static var`、自动属性仍然不能写（NYX1206）：enum 里只放不会变的东西。
   - 写在全部 case 后面，同别的成员（ADR-0029）。
   - **别名**：enum 里类型是这个 enum、值是常量的 `static let`，就是它所指的 case 的另一个名字。
     - 能写在 `is` 后面（以后 `match` 的 `case` 也能）；写在 `==` 的一边同样报 NYX2055。
     - 它不是新的 case：不受“两个 case 不能同一个值”（NYX2056）的限制，`match` 列全 case 时不用列它；和它所指的 case 在 `match` 里都列出来时报重复，同 .NET 枚举的别名（ADR-0040 决策 3）。
     - 值不是常量的 `static let`（`static let Lucky: Tile = Tile.Pick()`）不是别名，只是一个 enum 类型的值，写在 `is` 后面报 NYX2082。
   - C# 那边：放在 `名字Extensions` 类里，值是常量时是 `const`（ADR-0038）；再加一个同名的 extension 静态属性，`using` 了这个命名空间的 C# 代码能写 `Tile.Gate`，要常量的地方（`switch` 的标签）写 `TileExtensions.Gate`。

## 备选方案

- 第 1 项：**只挡右边**（ADR-0040 原来的写法）：左边的写法问的是同一件事，规则反而要多一句“只看右边”。
- 第 2 项：
  - **比较和算术都不能**（实现时的临时做法）：`level >= LogLevel.Warning` 要写成 `level as int >= LogLevel.Warning as int`，而有序的 enum（日志级别、画质档位、难度）很常见。
  - **都同 C#**：`e + 1` 可能得到不是任何 case 的值，同 ADR-0024 不让普通 enum 做位运算的理由。
  - **.NET 的枚举照 C# 能做算术**（同位运算的处理）：位运算放开有具体的理由（引擎的 flags 枚举常不标 `[Flags]`），算术没有，少一个例外。
- 第 3 项：
  - **仍然不能写，别名写到别的类型里，改掉诊断的建议**：和 enum 有关的别名、默认值离 enum 很远，读者要另外去找。
  - **能写，但别名不能写在 `is` 后面**：别名就只能拿来 `==`，而用 `==` 判断 case 正是决策 1 禁止的，别名等于没法用。
  - **`static var` 也放开**：enum 里会出现会变的全局状态，和“enum 的值不会变”（ADR-0024）放在一起容易误读，也没有碰到需要它的例子。

## 后果

- 改了已合进 main 的 ADR（原文都已改成最新，见各自的修订记录）：ADR-0040 决策 2（两边都挡）和决策 3（别名写在 enum 里）；ADR-0029 决策 4（能写 `static let`）。ADR-0024 决策 1 的别名写法加上了出处。
- language-reference：“enum 的成员与接口”“enum 的值与 flags”“运算符”“类型检查与收窄”四节。
- 实现（M2，随本 ADR）：
  - Parser：NYX1206 不再报 enum 里的 `static let`。
  - Binder：NYX2055 查两边、别名算 case 名；`is` 后面能写别名；同一个 enum 的 `<` `>` `<=` `>=`；enum 的算术报 NYX2087，给出 `as int` 的写法。
  - 降级：enum 里的 `static let` 放进 `名字Extensions`，加同名的 extension 静态属性；Nyxel 代码读它时写 `名字Extensions.名字`。
- 新错误码 NYX2087（design/diagnostics.md）。
