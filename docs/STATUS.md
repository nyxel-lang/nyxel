# 当前状态

> 每次收工**覆盖**本文件，保持一屏以内。历史在 [progress/](progress/)，计划在 [roadmap.md](roadmap.md)。
> 最后更新：2026-10-31

## 在哪

- 里程碑：**M1 语言基础设计 + 语法分析**。语法分析已完成，剩下高亮的分词快照测试。“待定”的逐轮讨论排好的九轮都讨论完了，剩下的是零散的细节（见“悬而未决”）。日志在 [progress/M1.md](progress/M1.md)。开发在 `dev` 分支上做，每轮提交一次 wip；用户说"整理合并"时再整理进 main 并推到 GitHub（AGENTS.md "提交"）。
- GitHub：https://github.com/nyxel-lang/nyxel，2026-10-05 起公开，只推 `main`。名字占位全部完成（brand.md）。ADR-0030 到 0035 已在 main 上。
- 语言设计：ADR-0004 到 0027、0029 到 0035，汇总在 design/language-reference.md。ADR-0028 是文档流程：ADR 保持最新，被推翻的决定改写原文，文末记修订（规则在 AGENTS.md）。
- 最近两轮（2026-10-31，已合进 main）：
  - ADR-0034（第 8 轮，协程与资源）：`await all { case ... }` / `await any { case ... }`，句柄 `Coroutine`，`await using`、`await for`，事件处理函数的异常记日志（修订 ADR-0016、0017）。
  - ADR-0035（第 9 轮，约定）：文档注释写 `- Parameter 名字:`、`- Returns:`、`- Throws 类型:`，链接写 `` [`类型.成员`] ``；缩略词同 .NET（`UI`、`HP`、`Xml`、`Id`）；插值的 `{ }` 在开始的那一行闭合，跨行报 NYX1234。例子里的 `Hp` 都改成了 `HP`。
- 错误码：`NYX1xxx` / `NYX2xxx` 段内按添加顺序编号，下一个是 NYX1235（design/diagnostics.md）。编译器还没有警告：命名检查和文档注释的检查是最早的一批（M2）。
- 语法分析：Lexer、Parser、无损语法树、带 `NYX` 错误码的诊断（design/syntax-tree.md、design/diagnostics.md）。`nyxel parse [--tree] <文件>` 检查语法、打印语法树。samples/ 有 16 个样例。
- 最近一次全绿：2026-10-31，`tools\build.cmd --test`，8 + 770 个测试；`dotnet format --verify-no-changes` 通过。

## 下一步

1. 高亮的分词快照测试：把 build/grammar-check 的 vscode-textmate 脚本搬进 tools/vscode-nyxel（devDependencies + `npm test`），对 samples 生成作用域快照，CI 加 setup-node；加一个有原始字符串和字符字面量的用例文件（可以从 build/grammar-check/literals.nyxel 开始）。
2. M2 开头：先写语义分析的设计文档（Binder：名字解析、类型检查，经 Roslyn 符号模型读 .NET 类型），再动手。前几轮留给 M2 的检查和翻译记在各 ADR 的“后果”里：0021（元组）、0022（区间、集合字面量）、0023（数组元素、`== null`）、0024（enum 的值、flags、`struct enum` 布局）、0025（`eval` 的流分析和翻译）、0026（访问器带体）、0027（`init super`、`with`、`required`）、0029（索引器的访问器、enum 冒号后面是接口、事件和委托一致、enum 成员的翻译、C# 那边怎么取 case）、0030（运算符的参数类型和重载决议、生成的相等和比较成员、C# 的隐式转换）、0031（`T?` 的约束、没约束的 T 用成员前检查、元素是 T 的数组）、0032（存储还是副本、没标 `readonly` 的方法、`var self` 扩展的翻译）、0033（同名类型的查找、`under` 前面是命名空间、别名重名）、0034（`await` 系列只在 `async` 函数里、每一项能等待、当值用的类型、`IAsyncDisposable`）、0035（命名检查的缩略词、文档注释的参数名和链接）。

## 悬而未决

- 之前几轮新增的待定：
  - `match` 里的元组模式、嵌套解构、解构时写类型、重写 .NET 里元组元素没有名字的成员（M3）。
  - 区间当值、展开、字典字面量（等 C#）、带元素创建多维数组。
  - 不要值的 `eval` 当语句（ADR-0025 先不放开）；`for _ in 0..<n`、lambda 参数写 `_`（ADR-0026 先不放开）。
  - `with` 用在已有的值上；Nyxel 自己声明只能在创建时设置的成员（ADR-0027）。
  - extension 里的索引器（等 C# 支持）；带数据 enum 的 `ToString`（ADR-0029）。
  - enum 和接口里的运算符（泛型数学）；自定义复合赋值 `operator +=`；struct 自定义相等（ADR-0030）。
  - 声明协变 / 逆变（ADR-0031，以后同 C# 的 `out T` / `in T`）；ref 局部变量（ADR-0032）。
  - 等一个事件触发（GDScript 的 `await signal`，ADR-0034）。M3 / M4 要定的协程细节记在 ADR-0034 后果：开始时就完成的项、同时完成时取哪个、停止时等 .NET 的 `MoveNextAsync`、`Wait.RealSeconds`。
- 特性（attribute）的写法：roadmap 记在 M3。
- roadmap “公开仓库前”剩下的几项（商标政策、命名 RFC、README 快速开始、Discord 等）没挡公开，用户定什么时候做。要用户本人做的还有正式商标检索（brand.md）。
- `nyxelc` 是独立可执行文件还是 `nyxel build` 的别名（ADR-0001 后果）。
