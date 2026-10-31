# ADR-0034: 同时等多个 `await all` / `await any`、句柄 `Coroutine`、`await using` 和 `await for`、事件处理函数的异常记日志

- 状态：已接受
- 日期：2026-10-31

## 背景

第二十七轮讨论，“待定”的第八轮：协程与资源。ADR-0016 定了 `async` / `await` / `launch`，留下几项：同时等多个协程、句柄类型和 `Wait` 系列的名字、`await using`（ADR-0015 也留了）。ADR-0017 留了一项：`emit` 时一个处理函数抛异常，要不要继续通知其他处理函数。四项放在一轮里讨论。

EnginePlayground 的游戏脚本这一侧没有用到协程。编辑器（Avalonia）这一侧有：`await foreach (var e in session.Events.ReadAllAsync())` 逐个读 Channel；`RuntimeSession` 实现 `IAsyncDisposable`，关闭窗口时手动 `await session.DisposeAsync()`；关闭窗口的处理函数是 `async void OnClosing(...)`。`await using` 的同类 `await foreach` 也一起定了。

第二到第四项按推荐定。第一项先推荐了 `await all { }` / `await any { }` 块，每行一个调用。之后用户又提了几个问题，规则跟着改了几次：

- 用户问调用有返回值时怎么处理，能不能像 `match` 一样每一项接一个处理分支。于是加了 `let 名字 = 调用 -> 分支`；`any` 也能像 `match` 一样当值用。
- 用户问能不能写 `var`。原来只许 `let`，没有站得住的理由，改成普通的 `let` / `var` 声明。
- 用户提出两点：块要和普通的块区分开；结果要显式地带出 `all`，默认不出块。于是每一项前面写 `case`，名字只在自己的分支里可见，`all` 也能当值用，按位置解构。
- 用户问分支里的值既要处理、又要带出时怎么写，会不会有复制的性能问题。答案是写 `-> eval { ...; give 值 }`，规则同 `match` 当值用。复制可以忽略，见“后果”。

## 决策

1. **同时等多个协程写 `await all { }` / `await any { }`**，每一项以 `case` 开头：

   ```nyxel
   await all {                                  // 都完成才继续
       case self.Camera.PanTo(target)
       case self.Dialog.FadeIn()
   }

   await any {                                  // 先完成的一项胜出，其余的停止
       case let key = Input.WaitForAnyKey() -> self.Answer(key)
       case Wait.Seconds(10) -> self.Answer(Key.None)
   }

   await any {                                  // 一边做一边抖屏，蓄力结束就停
       case self.Boss.Charge()
       case self.Camera.ShakeForever()
   }

   let dodged = await any {                     // 当值用，规则同 match
       case self.WaitForKey(Key.E) -> true
       case Wait.Seconds(2) -> false
   }

   let (tex, sfx) = await all {                 // 当值用：每一项的值按顺序组成元组
       case Assets.LoadTexture("slime")
       case Assets.LoadSound("slime")
   }
   ```

   - 写法：
     - 每一项写成 `case [let | var 名字 =] 能等待的表达式 [-> 分支]`，至少两项，一项一行。
     - `case` 和 enum 的成员、`match` 的分支一样，表示“花括号里列出的一项”。Go 的 `select` 同时等多个通道，每一项也写 `case`。
     - `all`、`any` 只在 `await` 后面紧跟 `{` 时是关键字，别处照样能当名字。
   - 每一项：
     - 可以是调用（Nyxel 的 `async` 函数、返回 `Task` 的 .NET 方法、`Wait.Seconds(2)` 这类等待函数），也可以是已有的句柄。
     - 不写 `await`：块自己会等每一项。
     - 不能给已有的变量或字段赋值（`case self.texture = Load()`）：各项谁先完成不确定。要存进字段，就在分支里或块后面赋值。
   - 名字：
     - `case let` / `case var` 的写法同普通的 `let` / `var` 声明，可以写类型，也可以解构（`case let (min, max) = self.ScanRange()`）。
     - 名字只在这一项的 `->` 分支里可见，块后面用不到（`all` 和 `any` 都一样）。所以起了名字就要有分支。
   - 分支：
     - 规则同 `match` 的分支（ADR-0025）。当语句用时是一条简单语句或 `{ }` 块，里面可以 `await`，写 `return` 时离开整个函数。
     - 当值用时是一个表达式或一条离开语句。要先执行几条语句，写 `-> eval { ...; give 值 }`：
       ```nyxel
       let (tex, sfx) = await all {
           case let t = Assets.LoadTexture("slime") -> eval {
               self.loading.Step(name = t.Name)
               give t
           }
           case Assets.LoadSound("slime")
       }
       ```
   - 运行：
     - 各项从上到下依次开始，然后同时进行。
     - 块启动的调用属于当前协程，同 `await 调用`（ADR-0016），绑定的也是当前协程的对象。它们没有自己的句柄，不会被单独停掉。
     - `all`：每一项完成后执行它的分支，这时其他项可能还在跑。所有项连同分支都结束后才继续。有一项抛异常时，其余的停止，异常从 `await all` 抛出。
     - `any`：有一项先完成或抛异常时，其余的停止（它们的 `finally`、`using` 照常执行），然后执行胜出那一项的分支，或者抛出它的异常。
     - 当前协程被停止时，块里的项全部停止。
     - 块只停自己启动的。已有的句柄输了不会被停，块只是不再等它（“加载超过 5 秒就显示提示，加载照常继续”）。.NET 的 `Task` 停不了，输了也只是不再等它。
   - 当值用：
     - 每一项的值：写了 `->` 是分支的值，没写就是这一项本身的结果。
     - `any` 的值是胜出那一项的值，各项的值类型要一致，同 `match` 各分支。
     - `all` 的值是所有项的值按顺序组成的元组，规则同按位置写的元组值（ADR-0021）：直接解构，或者有期望的类型。单写 `let r = await all { ... }` 是编译错误。每一项都要有值：本身没有结果的项（比如镜头移动）要写 `-> 值`，或者不放在当值用的块里。
   - 要拿结果、又要各自独立运行（各自绑定自己的对象、能单独停止）时，照旧用 `launch` 加 `await` 句柄：
     ```nyxel
     let tex = launch Assets.LoadTexture("slime")
     let sfx = launch Assets.LoadSound("slime")
     self.Setup(texture = await tex, sound = await sfx)
     ```

2. **句柄类型叫 `Coroutine` / `Coroutine<T>`，等待函数是 `Wait.Seconds`、`Wait.NextFrame`、`Wait.Frames`、`Wait.Until`。**

   ```nyxel
   var flash: Coroutine? = null
   ...
   self.flash?.Stop()
   self.flash = launch self.Flash()

   await Wait.Seconds(2)                            // 游戏时间
   await Wait.NextFrame()
   await Wait.Frames(3)
   await Wait.Until(func() = self.AliveCount == 0)
   ```

   - `launch` 的结果和 C# 那边看到的 `async` 函数返回类型都是 `Coroutine`（没有结果的）或 `Coroutine<T>`（有结果的），在 Nyxel.Runtime 的 `Nyxel` 命名空间里。停止仍然是 `Stop()`。
   - `Wait.Seconds` 按游戏时间计，也就是宿主每帧传进来的时间，游戏暂停时不走，同 Unity 的 `WaitForSeconds`。按真实时间计的版本（暂停菜单的动画用）暂定 `Wait.RealSeconds`，和 M4 的宿主接口一起定。
   - 没有 `Wait.While`：`Wait.Until(func() = not ...)` 就是，一件事一种写法。
   - 等待函数本身是能等待的调用：单写 `Wait.Seconds(2)` 是编译错误（ADR-0016 决策 2），也能写成 `await any` 的一项。

3. **异步释放写 `await using`，异步遍历写 `await for`。**

   ```nyxel
   async func Upload(self, path: string) {
       await using conn = await Server.ConnectAsync(self.address)
       await conn.SendAsync(File.ReadAllBytes(path))
   }   // 离开时等 conn.DisposeAsync() 完成

   async func Pump(self, session: RuntimeSession) {
       await for e in session.Events.ReadAllAsync() {
           self.Apply(e)
       }
   }
   ```

   - 两者都只能写在 `async` 函数里。会暂停的语句一律以 `await` 开头，扫一眼行首就能找到函数在哪里可能暂停。
   - `await using`：
     - 值要实现 `IAsyncDisposable`，或者有 C# 认可的 `DisposeAsync` 方法。其余规则同 `using`（ADR-0015）：名字不能重新赋值，可以写类型，和普通的 `using` 一起按声明的相反顺序在块结束时释放，值为 `null` 时跳过。
     - 只实现了 `IAsyncDisposable` 的类型写成 `using` 是编译错误，诊断提示 `await using`。两个接口都实现了的类型（比如 `FileStream`），两种写法都可以，各调各的。
   - `await for`：
     - 遍历 `IAsyncEnumerable<T>`，或者有 C# 认可的 `GetAsyncEnumerator`。写法同 `for`：可以解构（`await for (key, value) in ...`），恢复后在主线程执行。
     - JS / TS 和 Swift 的 `for await x in xs` 是编译错误（NYX1233），诊断给出 `await for`。
   - 停止时：
     - 协程被停止后执行清理，`await using` 的 `DisposeAsync` 和 `finally` 里的 `await` 照常等到完成，这期间不再因为绑定对象已经销毁而再停一次，否则清理做不完。
     - 停止时正在 `await for` 里的，照样释放枚举器。
   - 翻译：C# 的 `await using var x = ...;` 和 `await foreach (var x in xs)`。

4. **事件处理函数抛的异常交给宿主记日志，接着通知下一个，`emit` 的一方不受影响。**

   ```nyxel
   func Die(self) {
       emit self.Died(slime = self)   // HUD 的处理函数抛了异常：记日志，计分、刷怪器照样收到
       self.Destroy()                 // 照常执行
   }
   ```

   - 宣布方不认识订阅方（ADR-0017），也处理不了订阅方的错误。把异常传给它，只会打断它自己的逻辑：`emit` 后面的 `self.Destroy()` 不执行，排在后面的订阅方也收不到事件。
   - 这和 `launch` 是同一个原则：`launch` 出去的协程里没接住的异常交给宿主记日志（ADR-0016）。两者都是“发出去就不管”。引擎的惯例也是这样：一个脚本的 `Update` 抛异常只记日志，其他脚本照常运行。
   - 两个方向都适用：
     - Nyxel 的处理函数订阅 C# 或引擎的事件（`button.Clicked += self.OnClick`），异常在编译器生成的包装函数里捕获（ADR-0017 决策 2 本来就要生成这个函数）。
     - Nyxel `emit`、C# 写的处理函数抛了异常，同样记日志后继续。
   - 测试时宿主可以把这类日志当作失败。调试器照样会在异常抛出的地方停下。
   - 处理函数要跨帧时，在里面 `launch`：`slime.Died += func(s) { launch self.RespawnLater(s) }`。事件的处理函数不是 `async`，所以 `+= async func(s) { ... }` 是类型不匹配（现有规则的推论）。

## 备选方案

- 同时等多个：
  - **库函数 `Coroutine.All(...)` / `Coroutine.Any(...)`**（同 C# 的 `Task.WhenAll` / `WhenAny`）：不加语法，Agent 熟悉。坏处如下：
    - 每个调用前都要写 `launch`，因为 ADR-0016 不许能等待的调用直接当实参。
    - `launch` 出去的协程是独立的，各自绑定自己的对象，库函数拿到时没法再收归当前协程。于是要另外规定：等待方被停止时它们停不停；一项被自己的对象停掉时，`All` 算它完成，还是让等待方也停（`await` 一个被停止的协程时等待方也停）。
    - `Any` 停掉输家，名字像 `WhenAny`，行为却不同（`WhenAny` 不取消输家），和 C# 似是而非。
  - **不加，只用 `launch` 加 `Stop()`**：能写“一边做 X 一边做 Y”，写不了“按键或超时”这种事先不知道谁先完成的情况。
  - **每一项不写 `case`**（最初的方案）：每一项和普通的顺序调用长得一模一样，只能靠开头的 `await all` 来区分。
  - **圆括号加逗号 `await all(a(), b())`**：看起来像在调用一个叫 `all` 的函数，分支和 `let` 放进去很别扭。
  - 名字和结果：
    - **`all` 里起的名字在块后面可用**（讨论中的一版，类似 Swift 的 `async let`）：写起来最顺，但花括号里声明的名字出到了外面。用户倾向默认不出块，要显式地带出来。
    - **带名字的元组 `case Texture = ...`，之后写 `loaded.Texture`**：不会写反，但一项里会出现 `case let x =`（分支里用的名字）和 `case X =`（元组的元素名）两种很像的写法。
    - **给某一项加标记让名字出块**（比如 `case out let`）：新概念，`out` 已经表示 out 参数。
    - 选按位置的元组。JS 的 `Promise.all`、Python 的 `asyncio.gather`、Rust 的 `join!` 都按位置给出结果，名字写在外面的 `let` 上一眼可见。代价是类型相同的两项写反时发现不了，和 ADR-0021 的解构是同样的取舍。
  - 分支：
    - **分支只是回调，值永远是这一项本身的结果**：既要处理又要带出时不用 `eval`。但在要值的地方，`match` 的 `->` 后面是值，这里的却不是，长得一样的代码意思不同；`any` 当值用时也写不了 `-> true`。
    - **分支写 lambda `-> func(key) { ... }`**（Kotlin 的 `onAwait { }`）：看起来像新建了一个函数，`return` 只离开这个 lambda，和 `match` 分支里的 `return` 意思不同。
    - **起名不写 `let`：`key = 调用 -> ...`**（Rust 的 `tokio::select!`）：看起来像给已有的变量赋值。
  - **只许 `let`，不许 `var`**：能写 `let` 的地方都能写 `var`（`out let` / `out var`），只许 `let` 就多一条例外；struct 的结果要改字段时还得另存一份。
  - **关键字用 `all` / `first` 或 `race`**：`any` 和 `all` 成对，对应 `WhenAll` / `WhenAny`。
- 句柄的名字：
  - **`Job` / `Job<T>`**（Kotlin 的 `launch` 返回 `Job`，ADR-0016 原来的暂定名）：引擎圈里 job 指多线程任务（Unity 的 Job System、各家引擎的 JobSystem），而协程在主线程上跑。
  - **`Task`**：和 `System.Threading.Tasks.Task` 撞名。
  - **`Routine` / `Fiber`**：少见；`Fiber` 会让人以为每个协程有独立的栈。
  - `Coroutine` 和 `UnityEngine.Coroutine` 同名，Nyxel 不在 Unity 里跑，不影响。
- 等待函数：
  - **加 `Wait.While`**（Unity、UniTask 都有）：和 `Wait.Until(func() = not ...)` 是同一件事的两种写法。
  - **`Delay.Seconds(2)`、`Time.Wait(2)`**：避免 `await Wait` 里“等”出现两次，但 `Wait.Seconds` 一读就懂，Unity 用户熟悉 `WaitForSeconds`。
- 异步释放：
  - **只写 `using`，类型只实现了 `IAsyncDisposable` 时自动等**：少写一个词，但函数结尾会暂停这件事从代码上看不出来，违反 ADR-0016“会暂停的地方都写 `await`”。
- 异步遍历：
  - **`for await x in xs`**（JS / TS、Swift）：Agent 见得多，但 `await` 不在行首，和 `await using` 不一致。选 `await for`，对应 C# 的 `await foreach` 和 Dart 的 `await for`。
- 事件处理函数的异常：
  - **同 C#，异常直接传给 `emit` 的一方**（ADR-0017 原来的规则）：排在后面的订阅方收不到，`emit` 后面的代码不执行；一个外围系统（UI）的 bug 会打断核心逻辑（销毁、计分）。
  - **都通知完，再把异常抛给 `emit` 的一方**（多个时包成 `AggregateException`）：其他订阅方能收到，但 `emit` 后面的代码照样不执行，宣布方还是要处理它不认识的订阅方的错误。

## 后果

- 修改的旧决定：
  - ADR-0016：句柄定名 `Coroutine`，原来暂称 `Job`；等待函数加了 `Wait.Frames`，`Wait.Seconds` 按游戏时间计。
  - ADR-0017 决策 1：处理函数的异常不再传给 `emit` 的一方，改成记日志后继续通知。
  - ADR-0015、0016、0017 的待定改成“后来定了”。
- 语法树：
  - `await all { }` / `await any { }` 是表达式 `AwaitGroupExpressionSyntax`（`AwaitAllExpression` / `AwaitAnyExpression`），每一项是 `AwaitCaseSyntax`。当不当值用，同 `match` 看它是不是写在语句开头。
  - `all` / `any` 是标识符记号（上下文关键字，同 `under`）。
  - `await using` 和 `await for` 分别是 `UsingDeclarationStatementSyntax` 和 `ForStatementSyntax` 上的 `AwaitKeyword`。
- 新错误码：
  - NYX1228：一项不以 `case` 开头。
  - NYX1229：一项里写了 `await`。
  - NYX1230：一项给已有的目标赋值。
  - NYX1231：不到两项。
  - NYX1232：起了名字但没有分支。
  - NYX1233：`for await`。
  - NYX1123、NYX1190 的消息扩大到 `await all` / `await any`。
- 留给语义分析（M2）：
  - `await all` / `await any`、`await using`、`await for` 只能写在 `async` 函数里。
  - 每一项是能等待的表达式；当值用时每一项都有值、`any` 的各项类型一致、`all` 的元组有期望的类型或被解构。
  - `await using` 的类型实现了 `IAsyncDisposable`，只实现了 `IAsyncDisposable` 的写成 `using` 时提示 `await using`；`await for` 的集合能异步遍历。
  - `+= async func` 的类型不匹配。
- 留给 M3（翻译和 Nyxel.Runtime）：
  - 当前协程和子协程的关系：`all` 的每一项连同分支翻译成一个子协程；`any` 在其余的停止之后，按胜出的是哪一项执行分支。
  - 开始时就已经完成的项（`Wait.Until` 的条件本来就成立）：倾向于 `any` 就此决定，后面的项不再开始，实现时定。
  - 同一次调度里有几项同时完成或抛异常时取哪一个。
  - 停止时正在等 .NET 的 `MoveNextAsync` 怎么办：枚举器在等待中不能释放，可能要用 `WithCancellation` 传一个停止时取消的 token。
  - `emit` 逐个调用处理函数，每个包在 `try` / `catch` 里，捕获的异常交给 Nyxel.Runtime 报给宿主（带上事件和处理函数的名字）。不能每次 `emit` 都分配，比如在订阅变化时缓存处理函数数组。
- 留给 M4（宿主接口）：
  - `Wait.Seconds` 用的游戏时间，以及 `Wait.RealSeconds`。
  - 热重载时清理代码里的等待要等多久。
  - 处理函数异常的日志接口。
- 性能：
  - `await all` / `await any` 的每一项都是一个子协程，启动时要分配状态机和句柄，同 `launch`；Nyxel.Runtime 可以池化。这两个块是给流程（过场、加载、QTE）用的，不放在每帧遍历实体的循环里。
  - `case let t = ...` 加 `give t` 只是值的复制：class 是一个引用，struct 是它的字节，JIT 常把中间变量合并掉。子协程的结果本来就要存进句柄再取出来，C# 的 `Task<T>` 也是这样，多一个名字最多多一次复制。`eval` 不是 lambda，不分配（ADR-0025）。
- 待定：等一个事件触发（GDScript 的 `await signal`，比如“等 Boss 的 `Died`”），现在要写成 `Wait.Until` 加标志，或者订阅后在处理函数里做事。写了真实脚本后再看要不要语法。
