# ADR-0038: 整数字面量相除永远是整数除法、`static let` 自动成为编译期常量

- 状态：已接受
- 日期：2026-11-04

## 背景

第三十一轮讨论。对 language-reference 做整体 review 时找出的两处，都属于“能编译但意思不对”或“被 Roslyn 的内部错误拦住”这两类最坏的失败方式。

1. ADR-0013 决策 3 的规则 3“两边都是字面量时看期望的类型”让 `let x: float = 1 / 2` 得 0.5，而同一段文字在 C# 里是整数除法得 0。这是字面量规则里唯一和 C# 分道扬镳的一处（ADR-0013 的后果里记过），违反 ADR-0003 的“和 C# 不似是而非：写法相同的代码含义也相同”。读者要先想“这个 `1 / 2` 放在什么位置”才知道结果；从 C# 搬过来的常量算式会悄悄变值。
2. Nyxel 没有 `const`，类型级的常量只能写 `static let`，降级成 C# 的 `static readonly`。C# 要求默认参数值、`case` 标签、枚举的值是编译期常量，所以下面的写法 Nyxel 自己不报错，到 Roslyn 才编不过（NYX2053 内部错误）：

   ```nyxel
   class Spawner {
       static let DefaultCount: int = 3
       static let BossName: string = "Dragon"

       func Spawn(self, count: int = Spawner.DefaultCount) { }   // 默认值不是常量
       func Describe(self, name: string) -> string = match name {
           case Spawner.BossName -> "boss"                       // case 标签不是常量
           case _ -> "mob"
       }
   }
   ```

   C# 的两种“不会变的值”：`const` 在编译期定值、写进使用它的每一处，只能是数字、`bool`、`char`、`string`、枚举；`static readonly` 在程序启动时执行初始化代码、存在静态字段里，什么类型都行，但只能当普通的值读。

两项都按推荐定了（第 1 项 C1，第 2 项 A）。

## 决策

1. **整数字面量之间的 `/` 和 `%` 永远是整数运算；这样的运算出现在按浮点（或 `decimal`）算的字面量表达式里是编译错误**（修改 ADR-0013 决策 3 的规则 3）。诊断给出写法：一边写成小数，`1.0 / 2`。
   - 规则 3 其余不变：两边都是字面量的 `+`、`-`、`*` 按期望的类型算，`let x: float = 1.5 * 2` 是 `float` 的 3.0。
   - “按浮点算的字面量表达式”指：期望的类型是 `float`、`double`、`decimal`，或者没有期望的类型但表达式里有小数字面量（默认 `double`）。调用里跟着有类型的实参定型的字面量（ADR-0037）同样：`Math.Max(self.speed, 1 / 2)` 是错误。
   - 除得尽也报错（`let x: float = 10 / 2`）：规则一句话说完，不让“除得尽就行”成为另一条要记的规则。
   - 期望整数类型、或者没有期望的类型且全是整数字面量时照旧：`let n: int = 7 / 2` 是 3，`let n = 1 / 2` 是 0，同 C#。
   ```nyxel
   let a: float = 1 / 2        // 错误：写 1.0 / 2
   let b: float = 1.0 / 2      // 0.5
   let c: float = 10 / 2       // 错误：写 10.0 / 2 或 5
   let d: int = 7 / 2          // 3
   let e = 1 / 2               // 0（int）
   let f = 1 / 2 * 4.0         // 错误：有小数字面量，整个式子按 double 算
   ```
2. **`static let` 的初始值是常量表达式、类型是 C# 能 `const` 的类型时，编译器直接生成 C# 的 `const`**；其余的 `static let` 照旧生成 `static readonly`。语法不加任何东西，读者只看到“这个值不变”。
   - 常量表达式：数值、`bool`、`char`、`string` 字面量，不带数据的 enum case，别的编译期常量（Nyxel 的和 C# 的 `const`），以及它们的算术、位运算、比较、逻辑运算、字符串拼接，同 C#。
   - 能 `const` 的类型：数字、`bool`、`char`、`string`、enum。`static let Origin: Vector3 = ...`、数组、`List` 都是 `static readonly`。
   - 默认参数值、`case` 标签、枚举 case 的值（ADR-0024）都能引用它。引用了一个初始值不是常量表达式的 `static let` 时（`static let Seed: int = Environment.TickCount`），Nyxel 自己报错：这里要编译期常量，`Seed` 的初始值不是常量表达式。不能让 Roslyn 以内部错误的形式报出来。
   - `static let` 的其他用法不变：只读、不能 `ref`、通过类型名访问。实例的 `let` 字段不受影响。
   ```nyxel
   static let DefaultCount: int = 3                        // → const
   static let Twice: int = Spawner.DefaultCount * 2        // → const：引用的也是常量
   static let BossName: string = "Dragon"                  // → const
   static let Neighbors: array<(X: int, Y: int)> = [...]   // → static readonly：数组不能 const
   static let Origin: Vector3 = Vector3.Zero               // → static readonly
   ```

## 备选方案

- 第 1 项：
  - **维持现状**：方便，但同一段文字和 C# 含义不同；从 C# 搬来的 `1 / 2 * width`（作者本来就靠整数除法）会悄悄变值。
  - **整数字面量相除永远是整数除法，不报错**（完全同 C#）：规则最少，但 `let a: float = 1 / 2` 悄悄得 0，写的人多半想要 0.5。
  - **除不尽才报错**（`10 / 2` 放行）：更宽松，但多一条“除得尽就行”的规则，而且 `10 / 4` 改成 `10 / 5` 时错误消失，读者摸不着规律。
- 第 2 项：
  - **加 `const` 关键字**（`const DefaultCount: int = 3`）：读者一眼知道是编译期常量；但写的人要在 `static let` 和 `const` 之间选，还要学“什么类型能 const”这条 C# 的边界（`const Up: Vector3` 不合法）。同一件事两种写法。Swift 只有 `static let`，编译器自己决定怎么优化；ADR-0006 说拿不准参照 Swift。
  - **不支持**：默认值和 `case` 只能写字面量。魔法数字到处抄，正是常量要解决的问题。

## 后果

- 第 1 项让字面量规则和 C# 不同的只剩一类：C# 会报错的混合运算（`2 + self.Wave * 0.5` 赋给 `float`，ADR-0013）。所有能编过的纯字面量运算结果都和 C# 相同。
- 新错误码 NYX2054（diagnostics.md）。Binder 的 `BindLiteralOnly` 在按浮点折叠前查有没有整数字面量相除，BinderTests 的 `let x: float = 1 / 2` 改成期待错误。
- 第 2 项的代价是 C# `const` 的老问题：别的程序集引用它时值被复制过去，只重编译定义方不会更新使用方。游戏脚本整体一起编译，碰不到；以后要是出了 Nyxel 写的库再议。
- 第 2 项要等 Binder 支持静态字段的常量求值和默认参数值、`case` 标签、枚举值再实现：需要一个常量表达式求值器（ADR-0007 的“超出范围是编译错误”和枚举的值本来就要它），`SourceFieldSymbol` 上加 `ConstantValue`，降级时写成 `const`。记在 semantic-analysis.md“实现状态”。
- 改了 ADR-0013 决策 3 的规则 3 和后果；ADR-0037 里“字面量表达式包括 `1 / 2`”的例子改成 `-0.5`、`1.0 / 2`。language-reference 的“数值字面量”一节合并 ADR-0013 的四条和 ADR-0037 的三步，先讲“字面量跟着旁边有类型的东西”这个总原则；“变量与字段”一节写明 `static let` 什么时候是编译期常量。
