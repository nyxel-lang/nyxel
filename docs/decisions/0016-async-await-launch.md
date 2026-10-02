# ADR-0016: 协程：async / await / launch

- 状态：已接受
- 日期：2026-10-02

## 背景

第十二轮讨论，游戏专用特性的第一项。游戏逻辑经常是“做 A，等 2 秒，做 B，等玩家按键，做 C”。引擎每帧调用一次脚本，没有协程时只能自己维护“第几步 + 计时器”。

约束：

- Nyxel 不绑定某个引擎。M4 验收目标 EnginePlayground 是 ECS（组件是 struct、系统是静态函数），没有 Unity 式的 `Behaviour` 对象，协程要能用在关卡流程、过场这类不属于某个对象的地方。
- 要能等 .NET 的异步操作（文件、网络）和引擎自己的等待对象。
- Unity 用 `async` 的最大痛点：对象销毁后协程还在它身上继续跑；要避免这件事只能到处传 `CancellationToken`。
- 热重载要卸载旧程序集，正在运行的协程会引用旧代码。

和用户逐项对比代码后，三项都按推荐定了。

## 决策

1. **`async func` + `await`**：
   ```nyxel
   async func Intro(self) {
       self.Camera.Focus(self)
       await Wait.Seconds(2)
       self.Roar()
   }

   async func Load(self, name: string) -> Level { ... }
   let level = await self.Load(name = "Forest")
   ```
   - `async` 写在 `func` 前面，签名上看得出会跨帧。lambda 写 `async func(e) { ... }`，函数类型写 `async func(A) -> R`。接口成员可以是 `async`；重写时 `async` 必须和基类一致。
   - `await` 只能写在 `async` 函数里。能 `await` 的：Nyxel 的协程、.NET 的 `Task` / `ValueTask`，以及任何符合 C# 等待模式（`GetAwaiter`）的对象，包括引擎提供的等待对象。
   - 恢复后的代码一律在主线程、调度器的下一次 tick 里执行，可以直接访问引擎对象。调度器由引擎每帧驱动（Nyxel.Runtime 提供，宿主接口 M4 定）。
   - 等待函数由 Nyxel.Runtime 提供，在 `Nyxel` 命名空间：`Wait.Seconds(s)`、`Wait.NextFrame()`、`Wait.Until(func() = 条件)`（名字实现时可调整）。
   - `async` 函数不能有 `out` / `ref` 参数（同 C#）。
   - 翻译：C# 的 `async` 方法，返回 Nyxel.Runtime 的任务类型（暂称 `Job` / `Job<T>`，C# 里也能 `await`）。
2. **调用 `async` 函数必须写 `await` 或 `launch`**：
   ```nyxel
   await self.FadeIn()            // 等它完成
   launch self.FlashRed()         // 启动它，自己接着往下走
   let job = launch self.Load(name = "Forest")
   ...
   let level = await job          // 之后再等结果
   self.FadeIn()                  // 错误：要 await 还是 launch？
   ```
   - 结果可以等待的调用（Nyxel 的 `async` 函数，以及返回 `Task` 等的 .NET 方法）只能直接写在 `await` 或 `launch` 后面。不写、存进变量、当实参传，都是编译错误，诊断问要哪一种。忘写 `await` 的 bug 因此在编译时就会暴露。
   - `launch 调用` 是表达式，得到协程的句柄（`Job`），可以丢弃，也可以存起来：`job.Stop()` 停止，`await job` 等它完成并拿到结果。非 `async` 函数里（比如 `Update`）也能 `launch`。
   - 关键字用 `launch`（Kotlin 的词）。没用 `start`：它常被用作变量名，也是 .NET API 的参数名（`Enumerable.Range(start, count)`）。
3. **`launch` 出去的协程绑定在被调用的对象上，对象销毁后自动停止**：
   - `launch enemy.Burn()` 绑定 `enemy`。引擎的对象基类实现 Nyxel.Runtime 的一个接口（暂称 `ILifetime`，提供“是否还活着”）。每次 `await` 之后恢复前检查，绑定对象已经销毁就停止。
   - 静态函数、struct、没实现这个接口的对象启动的协程，没有绑定对象，一直运行到结束、`Stop()` 或热重载。
   - 被 `await` 的 `async` 函数属于当前协程，按当前协程的绑定对象判断。
   - 停止时，`finally` 和 `using` 的释放照常执行，但 `catch` 接不住这次停止（编译器翻译 `catch` 时排除停止信号）。`await` 一个被停止的协程，等待方也停止。
   - `launch` 出去的协程里没有被接住的异常，交给宿主记日志；`await` 这个协程的一方会收到这个异常。
   - 热重载前，所有正在运行的协程都会停止。暂停到一半的函数没法迁移到改过的代码里。

## 备选方案

- 机制：**Unity 传统的 `yield` 协程**：`yield` 出去的对象由引擎解释，不能直接等 .NET 的异步操作，也没法返回值；Unity 从 2023 版起也加了 `async` / `await`（`Awaitable`）。**不写 `async`，函数里有 `await` 就自动算异步**：少写一个词，但签名看不出会跨帧；给一个函数加一句 `await`，所有调用方都跟着变。
- 启动：**同 C#，直接调用就是启动、不等**：写得最少，但忘写 `await` 时两段逻辑同时跑，C# 只给警告。**用库函数启动**（`Job.Start(owner = self, job = self.FlashRed())`）：不加关键字，但绑定的对象要自己传，写法更长。
- 生命周期：**手动 `CancellationToken`**（C#）：和 .NET 一致、控制最精细，但每个协程都要传 token，每次等待都要带上，漏一处就是 bug。**不自动停，自己检查**（Unity 现在的 `async`）：语言最简单，漏写一次就在已销毁的对象上报错。

## 后果

- 引擎集成时要提供两样东西：每帧驱动 Nyxel 的调度器；对象基类实现 `ILifetime`。都放在 M4 的宿主接口里设计。
- 停止的语义依赖编译器翻译 `catch`：Nyxel 的 `catch` 接不住停止信号，但 Nyxel 调用的 C# 代码如果捕获所有异常，仍可能吞掉它。
- 绑定只看 `launch` 时的那个对象。`await boss.PlayIntro()` 期间 `boss` 被销毁，如果当前协程绑定的不是 `boss`，`PlayIntro` 会继续执行。以后需要时再考虑更细的规则。
- 待定：`await using`（`IAsyncDisposable`）；同时等多个协程的写法（`Job.WhenAll` 之类，可能只是库函数）；`launch` 的句柄类型和 `Wait` 系列的最终命名（实现时定）。
