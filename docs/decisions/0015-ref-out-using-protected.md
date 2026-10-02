# ADR-0015: out / ref 参数、using、protected

- 状态：已接受
- 日期：2026-10-02

## 背景

第十一轮讨论，都是调用 .NET / 引擎 API 时绕不开的事：

- .NET 和引擎里到处是 `TryGetValue(key, out value)`、`int.TryParse(s, out n)`、`Physics.Raycast(ray, out hit)` 这类 `out` 参数，以及 `SmoothDamp(..., ref velocity, ...)` 这类 `ref` 参数。
- 文件、网络连接、原生资源要在用完时立刻释放（`IDisposable`），不能等 GC。
- 重写 C# 基类的 `protected virtual` 方法时，C# 要求可见性一致，Nyxel 只有 `public` / 私有两级。

和用户逐项对比代码后，四项都按推荐定了。资源释放最初定的是 `using let x = ...`；之后用户指出 `using` 只剩这一种写法，`let` 是多余的，改成了 `using x = ...`。

## 决策

1. **调用带 `out` / `ref` 参数的方法，调用处必须写标记**：
   ```nyxel
   if not self.cache.TryGetValue(id, out let enemy) {
       return
   }
   enemy.Alert()                     // 按 .NET 的可空标注，这里 enemy 非空

   self.Position = Smooth.Damp(self.Position, target, ref self.velocity, 0.3)
   ```
   - `out let x` / `out var x` 就地声明新变量，类型取参数的类型（重载有歧义时可以写 `out let n: int`）；`out 目标` 写进已有的 `var` 局部变量或 `var` 字段；`out _` 丢弃。
   - `ref 目标`：目标必须是 `var` 局部变量、`var` 字段或数组元素。不能是属性、`let`、临时值（C# 同样不允许属性）。
   - `if` 条件里用 `out let` 声明的变量，在 `if` 之后仍然可用（同 C#），这样才能写 `if not ... { return }` 再往下用。其他位置声明的，作用域到所在语句结束。
   - 读 .NET 的 `NotNullWhen` / `MaybeNullWhen` 等标注做空值收窄（ADR-0009 的“照单全收”）。
   - 漏写标记是编译错误，诊断给出写法。C# 的 `in` 参数照常传值，不写标记。命名实参写 `velocity = ref self.velocity`。
   - 翻译：C# 的 `out var x` / `out x` / `out _` / `ref x`。
2. **自己的函数只在 `override` 或实现接口时才能声明 `out` / `ref` / `in` 参数**，也就是 .NET 那边的签名要求这样时：
   ```nyxel
   public func TryLoad(self, key: string, data: out SaveData) -> bool { ... }
   ```
   - 写法是参数类型前加修饰：`data: out SaveData`、`velocity: ref Vector3`、`value: in Matrix4x4`。
   - `out` 参数在每条返回路径上都必须赋过值；`ref` / `out` 参数可以赋值，是 ADR-0014“参数不能重新赋值”的例外。
   - 自己的 API 表达“可能没有”用返回 `T?`（ADR-0009），不用 `bool` + `out`。
3. **资源释放写 `using 名字 = 值`**：
   ```nyxel
   func ReadSave(self, path: string) -> string {
       using stream = File.OpenRead(path)
       using reader = new StreamReader(stream)
       return reader.ReadToEnd()
   }
   ```
   - 只能用于实现了 `IDisposable` 的类型。值在所在块结束时释放，多个按声明的相反顺序；`return`、`break`、异常离开时也一样。值为 `null` 时跳过。
   - `using` 自己引入名字，不写 `let`，和 `for x in`、`catch e: T`、`case Burn(amount)` 一样。名字不能重新赋值（资源不应该中途换成别的）。需要时可以写类型：`using stream: FileStream = ...`。`out let x` 保留 `let`，是因为 `out` 后面也可以写已有的变量，要区分；`using` 没有“用已有变量”的写法。
   - `using` 在 Nyxel 里只有这一种用法：导入是 `import`，没有块形式的 `using (...) { }`，也没有 `defer`。要更早释放就拆成函数；和 `IDisposable` 无关的清理用 `try` / `finally`（ADR-0014）。
   - 翻译：C# 的 `using var x = ...;`。
4. **可见性加 `protected`**（只有子类能访问），含义同 C#。
   - 重写时可见性必须和基类一致（C# 的规则）：基类是 `protected virtual` 就写 `protected override`。写错时诊断给出应写的修饰。
   - 不加 `internal`：类型默认只在本程序集可见（ADR-0008），它的 `public` 成员实际上也出不了程序集。不加 C# 的 `protected internal`、`private protected`。
   - struct 的成员不能是 `protected`（同 C#，struct 不能被继承）。

## 备选方案

- 调用 `out`：**`out` 参数变成多个返回值**（F#：`let (found, enemy) = dict.TryGetValue(id)`）：调用处不用写 `out`，但需要元组；编译器不知道 `found` 和 `enemy` 的关系，`if found` 之后 `enemy` 仍是可空的；`ref` 还要另外的写法。**不标记，编译器看签名**：最省事，但调用处看不出哪个变量会被改写，违反 ADR-0003“边界处显式”。
- 声明 `out` / `ref`：**随时可以声明**（同 C#）：最灵活，但“可能没有”就有 `T?` 和 `bool` + `out` 两种写法。**完全不支持声明**：没法实现带 `out` 的 C# 接口、重写带 `ref` 的引擎方法。现在的规则和 ADR-0011“不重载，重写 .NET 已有的重载除外”是同一思路。
- 资源释放：**`defer`**（Swift、Go）：任何清理都能写，但要自己记得调用 `Dispose`，而且和 `finally` 是同一件事的两种写法。**`using` 和 `defer` 都要**：清理代码能紧挨着开始的地方，但 `defer` 和 `finally` 功能重叠。**只有块形式**（Python 的 `with`、Kotlin 的 `use`）：作用范围一目了然，但每个资源多一层缩进。**`using let x = ...`**（最初的方案，照搬 C# 的 `using var`）：所有局部变量声明都能搜到 `let`，但 `let` 在这里不区分任何东西。**`use x = ...`**（F#）：更短，但 Rust、PHP 的 `use` 是导入，C# 读者和 Agent 不熟。
- 可见性：**再加 `internal`**：写给别人用的库时有用，游戏脚本很少需要，以后可以再加。**和 C# 完全一样的六种**：互操作最完整，但组合级别很少有人记得清含义。

## 后果

- `TryXxx` 加 `if not ... { return }` 的写法和 `?? return` 一样，都能让后面的代码拿到非空的值。编译器要实现 .NET 可空标注里和 `out` 有关的那几种。
- C# 习惯的 `using var x = ...`、`using (...) { }`、文件开头的 `using System` 都会得到编译错误和对应的写法（去掉 `var`、`using x = ...`、`import System`）。
- 待定：异步资源，写法会是 `await using conn = ...`。
