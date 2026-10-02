# ADR-0014: 类型转换 as、参数不可变、extension 块、异常

- 状态：已接受
- 日期：2026-10-02

## 背景

第十轮讨论：稍长一点的脚本都会碰到的四件事。`float` 转 `int` 要显式写（ADR-0010 只定了隐式转换同 C#）；参数能不能重新赋值；自己怎么给引擎或 .NET 的类型加方法（ADR-0008 后果里留了一条线索）；.NET API 抛出的异常怎么处理。和用户逐项对比代码后，四项都按推荐定了。

## 决策

1. **显式转换写 `x as T`**：
   - `as` 是转换，不是类型检查：不会因为“类型不对”而失败。允许的有：数字之间、枚举和整数之间、子类转父类或接口、C# 类型自己定义的显式转换运算符。
   - 可能失败的向下转换（`Collider` 转 `Projectile`）写 `as` 是编译错误，诊断提示改用 `is` 收窄（ADR-0010）。C# 的 `obj as Enemy`（失败得 null）因此不会在 Nyxel 里悄悄换了含义。
   - 语义同 C# 的强制转换：`float` 转 `int` 向零截断；不检查溢出；常量超出范围是编译错误。
   - 优先级比 `*` `/` 高，比一元运算符和成员访问低：`self.Killed as float / self.Total` 是先转换再除，`-x as int` 是 `(-x) as int`。
   - 写 C# 的 `(int)x` 是编译错误，诊断给出 `x as int`。
   - 翻译：C# 的 `(T)(x)`。
2. **参数不能重新赋值**，和 `let` 一样（`ref` / `out` 参数除外，见 ADR-0015）；需要改就复制到局部变量。lambda 的参数同样。没有 `var` 参数：`var self` 的含义是修改调用方的 struct，`var amount` 却只能改副本，两个 `var` 含义不同。
3. **扩展用 `extension` 块**：
   ```nyxel
   extension Vector3 {
       public func Flat(self) -> Vector3 = new Vector3(self.X, 0, self.Z)
       public func FromAngle(radians: float) -> Vector3 { ... }   // 没有 self：Vector3.FromAngle(...)
   }
   ```
   - 写在文件顶层，块里的写法和类里一样：有 `self` 是实例方法，没有是静态函数，也可以写属性（`static property` 是静态扩展属性）。不能有字段和 `init`。
   - 可见性同类成员：默认私有（只在这个块里可见），`public` 才能在块外用。使用方 `import` 扩展所在的命名空间后可见（同 C#）。类型本身的成员优先于扩展。
   - 扩展只能访问类型的公开成员。
   - 翻译：C# 14 的 `extension` 块，放在名为 `<类型名>Extensions` 的 `static partial class` 里。C# 那边看到的就是普通的扩展方法和扩展属性。
4. **错误处理用 .NET 的异常**：
   ```nyxel
   try {
       let text = File.ReadAllText(path)
   } catch e: FileNotFoundException {
       Log.Warn($"没有存档：{path}")
   } catch e: IOException when e.HResult == 32 {
       Log.Warn("文件被占用")
   } finally {
       self.loading = false
   }
   ```
   - `catch 名字: 类型`，名字必须写；捕获所有异常写 `catch e: Exception`。`when` 过滤同 C#（和 `match` 的 `when` 同一个词）。
   - 被前面的 `catch` 完全覆盖、永远到不了的 `catch` 是编译错误（同 C#）。
   - `try` 后面至少有一个 `catch` 或 `finally`。`} catch`、`} finally` 跟在 `}` 后面同一行（ADR-0013）。
   - `try` 可以当表达式用，规则同 `if` / `match`（ADR-0007）：`let config = try { Config.Load(path) } catch e: IOException { Config.Default }`。每个分支的最后一个表达式是值，或者以 `return` / `throw` 离开；`finally` 不产生值。
   - `throw 异常对象` 是语句（`??` 右边也能用，ADR-0009）。在 `catch e` 里写 `throw e` 重新抛出同一个异常时，翻译成 C# 的 `throw;`，保留原来的调用栈（C# 的 `throw e;` 会丢掉，是常见的坑）。
   - `finally` 里不能 `return`（同 C#）。

## 备选方案

- 转换：
  - **`float(x)`**（Swift、Python）：像调用转换函数，但对不是关键字的类型（`Half(x)`、`Element(3)`）和 ADR-0008 的“创建一律写 `new`、`名字(...)` 一定是函数调用”冲突。
  - **`x.ToInt()` 方法**（Kotlin）：读起来清楚，但每种目标类型都要加一个方法，泛型代码里写不出来。
  - **同 C# 的 `(int)x`**：Agent 最熟，但放进方法链要多套括号（`((float)self.Wave).ToString()`），`(a) - b` 是转换还是减法要靠猜。
- 参数：**同 C#，参数可以重新赋值**：少一个变量名，但长函数里读者要确认参数中途有没有被改过。**写 `var` 的参数可以改**：和 `var self` 含义不同；Swift 3 因为这种混淆删掉了 `var` 参数。
- 扩展：**类型外面写 `func IsBoss(self: Enemy)`**：要先允许类型外面的函数，和 ADR-0008 的“不带接收者的名字只能是局部变量或参数”冲突；每个函数重复 `self: Enemy`，也写不了扩展属性。**暂不支持定义**：语言最小，但给引擎类型加便捷方法是游戏脚本的常见需求，LINQ 风格的 API 也要靠它。
- 错误处理：**Rust 式 `Result<T, E>` 返回值**：错误必须处理，但 .NET API 全都抛异常，要再包一层，等于两套错误机制并存。**Swift 式 `throws` / `try` 标记**：调用处看得出哪里会出错，但 .NET 元数据里没有“会抛什么”，调用 .NET API 时没法检查。

## 后果

- C# 习惯的 `(int)x`、`obj as Enemy`、`throw e` 都不会悄悄换含义：前两个是编译错误并给出写法，`throw e` 按读者以为的含义（保留调用栈）翻译。
- 扩展依赖 C# 14 的 `extension` 块（.NET 10 SDK 自带）。
- 后来定了：`ref` / `out` 参数、资源释放（ADR-0015）。
- 待定：泛型扩展（`extension<T> List<T>`）；修改 struct 自己的扩展（`var self`，对应 C# 的 `extension(ref Vector3 v)`）；文档注释里写会抛哪些异常的约定。
