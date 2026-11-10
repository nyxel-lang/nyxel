# ADR-0036: 程序入口是 `static func Main`，局部变量不和外层同名

- 状态：已接受（2026-11-04 修订）
- 日期：2026-10-31

## 背景

第二十九轮讨论。写语义分析的设计文档（design/semantic-analysis.md）时，发现 M2 的第一条线要用到两条还没定的规则：

- M2 的验收要求 hello world 和几个算法小程序能用 `nyxel run` 跑起来，但还没规定程序从哪个函数开始执行。游戏脚本由引擎调用，不需要入口；控制台程序、小工具、测试用的小程序需要。
- 函数体绑定时要建作用域，要知道内层能不能声明和外层同名的局部变量（遮蔽，shadowing）。

用 Roslyn 5.9 确认了 C# 的规则：内层块里声明和外层局部变量、参数同名的变量是错误（CS0136），并列的块可以重用名字；C# 8 起 lambda 的参数和 lambda 里的局部变量可以和外层同名，嵌套的 lambda 也可以用同一个参数名。

两项都按推荐定。

## 决策

1. **程序入口是任意一个类型里的 `static func Main`**，同 C#：

   ```nyxel
   namespace Hello

   import System

   class Program {
       static func Main() {
           Console.WriteLine("Hello, Nyxel")
       }
   }
   ```

   - 静态函数写 `static func`（ADR-0039；定这条时是“没有 `self` 就是静态”），不加新语法。
   - 写法有四种：`static func Main()`、`static func Main(args: array<string>)`，各自可以加 `-> int` 返回退出码。别的参数或返回类型是编译错误。
   - 可见性随意（默认私有也行），同 C#。不能写在泛型类型里。
   - 只在输出可执行程序时找入口（`nyxel run`、输出类型是可执行文件的项目）。程序里没有 `Main`、或者有两个以上，是编译错误，诊断列出找到的每一个。编译成给引擎用的库时不找入口，叫 `Main` 的函数就是普通的函数。
   - `async func Main` 暂时是编译错误：协程要靠宿主每帧驱动（ADR-0016、M4）。
   - 翻译：C# 的 `static void Main()` / `static int Main(string[] args)` 等。

2. **一个函数里，一个名字只指一个变量**：声明局部变量或参数时，不能和这一点看得见的局部变量、参数同名。

   ```nyxel
   func Spawn(self, count: int) {
       let speed = 2.0
       for i in 0..<count {
           let speed = speed * i                   // 错误：外层已经有 speed
       }
       let e = self.enemies.First()
       self.enemies.Where(func(e) = e.IsDead)      // 错误：外层已经有 e
   }

   func Rename(self, name: string) {
       let trimmed = name.Trim()                   // 参数不能重新赋值（ADR-0014），也不能再声明一个 name
   }
   ```

   - 适用于所有引入名字的地方：`let` / `var`、`for` 的循环变量、解构的名字、`match` 和 `await all` / `await any` 的 `case let`、`catch` 的名字、`out let`、`using`、lambda 的参数和 lambda 里的局部变量。lambda 也不例外，这一点比 C# 严。
   - 并列的块、并列的 `match` 分支可以重用同一个名字，比如两个先后的 `for i in ...`。
   - 局部变量从声明的地方开始可见，到所在的块结束。所以先写完一个块 `{ let a = 1 }`、再在外面声明 `let a = 2` 也可以：两个 `a` 的范围不重叠（C# 不允许，它的局部变量在整个块里都算数）。
   - 字段不受影响：字段总是写成 `self.hp`（ADR-0008），`let hp = self.hp` 没有歧义。`_` 是丢弃（ADR-0026），不算名字，可以写多个。
   - 诊断说明外层的同名变量在哪里，并提示换一个名字。

## 备选方案

- 程序入口：
  - **顶层语句**（C# 9、Python）：hello world 最短。但要打破“文件顶层只能声明类型和扩展”（NYX1113），这个文件和其他文件长得不一样，还要规定只能有一个这样的文件；为很少用到的情况多出一种文件。
  - **加标记**（Swift 的 `@main`）或新关键字 `main func`：特性（attribute）的写法到 M3 才讨论；为很少用的东西加关键字不值得。
  - **由工具指定入口**（`nyxel run --entry Program.Run`、写在项目文件里）：只看代码看不出程序从哪里开始。
- 同名：
  - **完全同 C#**，内层块不行，lambda 可以：lambda 里的 `e` 和外面的 `e` 不是同一个东西，正是容易看错的地方。
  - **都允许**（Swift、Kotlin、Rust 的遮蔽）：`let name = name.Trim()` 这种写法顺手，但读到一个名字要回头确认它指的是哪一个；和 ADR-0014 “参数不能重新赋值”的用意（读者不用确认参数中途有没有被改过）相反。
  - 比 C# 严的地方，Agent 按 C# 的习惯写出来时诊断会说怎么改；现在不允许、以后放开不会让已有代码失效，反过来不行。

## 后果

- design/semantic-analysis.md 的“待讨论”两条定了。
- 语义检查（NYX2xxx，随第一条线实现）：没有 `Main`、两个以上 `Main`、`Main` 的签名不对、`async func Main`；局部变量、参数和外层同名，同一个作用域里重名。
- `nyxel run` 编译成可执行程序后执行 `Main`。

## 修订记录

- 2026-11-04：措辞由“没有 `self` 的 `Main`”改为 `static func Main`，规则不变，见 ADR-0039。
