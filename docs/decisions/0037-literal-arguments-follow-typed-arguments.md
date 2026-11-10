# ADR-0037: 调用里的字面量跟着有类型的实参

- 状态：已接受（2026-11-04 修订）
- 日期：2026-11-02

## 背景

第三十轮讨论。实现 M2 第一条线的重载决议时发现的问题。

ADR-0007 规定数值字面量的类型由上下文决定，参数类型是一种上下文（`new Vector3(0, 1.5, 0)` 里的小数是 `float`）。但参数类型要等选出重载才知道，选哪个重载又要看实参的类型。只有一个重载能接受时没有问题；几个都能接受时，第一条线先用了一个过渡做法：字面量当成 C# 的类型（`0.5` 是 `double`），完全按 C# 选；一个都不适用时，小数字面量才能变成 `float` / `decimal`。

用 Roslyn 5.9 确认了这个做法（也就是 C#）的结果，`speed` 是 `float`，`weights` 是 `array<float>`：

```nyxel
self.speed = Math.Max(self.speed, 0.5)      // Max(double, double)，结果是 double，赋给 float 是编译错误
self.t = Math.Clamp(self.t, 0.0, 1.0)       // Clamp(double, double, double)，同上
let at = Array.IndexOf(self.weights, 0.5)   // IndexOf(Array, object)：0.5 装箱成 double，和 float 元素永远不相等，总是 -1
let same = speed.Equals(0.5)                // Equals(object)：永远是 false
let order = speed.CompareTo(0.5)            // CompareTo(object)：运行时抛 ArgumentException
```

C# 程序员写 `0.5f` 避开这些；Nyxel 没有后缀（ADR-0007），只能写 `0.5 as float`。这也和运算符不一致：ADR-0013 里 `self.speed * 0.5` 的 `0.5` 跟着 `speed` 是 `float`，换成 `Math.Max(self.speed, 0.5)` 却是 `double`。

按推荐定（B）。

## 决策

**选重载时，数值字面量跟着有类型的实参**（补充 ADR-0007 决策 3、ADR-0013 决策 3）。“字面量”指只由数值字面量组成的表达式，包括 `-0.5`、`1.0 / 2`（ADR-0013；整数字面量相除跟着 `float` 实参是编译错误，ADR-0038）；“有类型的实参”是其余的实参：变量、字段、属性、调用的结果等。

1. **先找有类型的实参原样传进去、字面量也放得下的重载**：
   - 原样传进去：参数的类型和实参的类型完全相同，不经过任何转换。
   - 放得下：整数字面量放得进任何装得下它的数值类型，小数字面量放得进 `float`、`double`、`decimal`（值超出范围的不算）；也可以按自己的默认类型装箱成 `object` 或接口。
   - 有的重载能让字面量都变成数值类型时，把字面量装箱的那些不考虑。
   - 剩下的里面只有一个最合适的，就选它，字面量取它的参数类型。比较时字面量变成哪种数值类型都一样好；两个重载只差在可选参数上时，同 C#，不用默认值的那个更好。
2. **找不到，或者不止一个**：字面量用默认类型（整数是 `int`，放不下时依次 `uint`、`long`、`ulong`；小数是 `double`），完全按 C# 的规则选。
3. **还是一个都不适用**：小数字面量也可以变成 `float` / `decimal`，再按 C# 的规则选（`MathF.Max(count, 0.5)`，`count` 是 `int`）。

```nyxel
self.speed = Math.Max(self.speed, 0.5)      // Max(float, float)
self.t = Math.Clamp(self.t, 0.0, 1.0)       // Clamp(float, float, float)
let at = Array.IndexOf(self.weights, 0.5)   // IndexOf<float>(float[], float)
let same = speed.Equals(0.5)                // Equals(float)
let order = speed.CompareTo(0.5)            // CompareTo(float)
let r = MathF.Sqrt(2.5)                     // 只有 Sqrt(float)
let b = Math.Max(level, 1)                  // level 是 byte：Max(byte, byte)，C# 报二义性

// 和 C# 一样：
let a = Math.Max(self.wave, 0.5)            // wave 是 int：没有重载能原样收下 int 又放得下 0.5 → 第 2 步，double
let m = Math.Max(1, 2.5)                    // 只有字面量，float / double / decimal 都行 → 第 2 步，double
Console.WriteLine(0.5)                      // 同上，WriteLine(double)
let c = Math.Max(speed, 1)                  // Max(float, float)，C# 也是
```

- **泛型推断也一样**：类型实参先由有类型的实参推出来，字面量跟着它（`IndexOf<T>` 的 `T` 由 `weights` 推出是 `float`）；只有字面量时用默认类型（`Enumerable.Repeat(0.5, 3)` 是 `double` 的序列）。泛型推断随 M2 加宽 Binder 时实现。
- **调用不看外面期望的类型**，同 C#：`self.speed = Math.Max(self.wave, 0.5)` 的结果是 `double`，赋给 `float` 要写 `as float`。运算符看（ADR-0013 决策 3.2），调用不看。
- 第 2、3 步就是原来的过渡做法：B 只改变第 1 步能选出一个重载的调用。没有字面量的调用和 C# 选的完全一样。
- 翻译：字面量按选中的参数类型写后缀（`0.5f`、`(byte)1`，lowering.md），生成的 C# 选中的一定是同一个重载。

## 备选方案

- **照 C#**（原来的过渡做法）：C# 能编过的调用选的和 C# 完全一样，不用另学规则。但背景里的五行都有问题，后三行是不报错、悄悄出错的坑；Agent 照 C# 写了 `0.5f`，按诊断删掉后缀（ADR-0007），选中的就悄悄换成了另一个重载。
- **期望的类型也参与**（Swift 式）：`let m: float = Math.Max(1, 2.5)` 选 `float` 版本，`self.speed = Math.Max(self.wave, 0.5)` 也是。但同一个调用放的位置不同选的重载就不同，ADR-0013 没让期望的类型传给所有字面量也是这个原因；C# 从不按返回类型选重载，读者和 Agent 都想不到。
- **有分歧就报错**：B 和 C# 选的不一样时报错，要求写 `0.5 as float` 或 `self.speed as double`。没有新的选择规则，但最常见的 `Math.Max(self.speed, 0.5)` 每次都报错，修法人人一样，是样板代码。
- **第 1 步按 C# 的“更好的转换”比较有类型的实参**，不要求原样传进去：能覆盖 `Enemy` 传给 `Actor` 参数这种情况，但结果难以预料。比如 `int` 实参和小数字面量：只有 `float` / `double` 两个重载时，C# 认为 `int` 转 `float` 更好，选 `float`；再多一个 `decimal` 重载，`float` 和 `decimal` 分不出好坏，退回第 2 步变成 `double`。“原样传进去”这条好说清楚，需要转换的实参退回按 C# 选。

## 后果

- 和 C# 选得不一样的只有一类：C# 里要给字面量写后缀才能选中的重载。也有一些 C# 报二义性的调用能编过了（上面的 `Math.Max(level, 1)`）。
- 重载决议的对照测试（OverloadResolutionTests）分两组：没有分歧的调用和原样的 C# 比；字面量跟着实参的调用，和补上后缀的 C# 比。
- design/semantic-analysis.md“调用 .NET 的成员”和 language-reference“数值字面量”写进这条规则；STATUS 的这一项悬而未决的问题定了。

## 修订记录

- 2026-11-04：“字面量”的例子由 `1 / 2` 改为 `1.0 / 2`，整数字面量相除放在浮点位置是编译错误，见 ADR-0038。
