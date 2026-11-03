# 当前状态

> 每次收工**覆盖**本文件，保持一屏以内。历史在 [progress/](progress/)，计划在 [roadmap.md](roadmap.md)。
> 最后更新：2026-10-31

## 在哪

- 里程碑：**M2 语义分析与代码生成**刚开始，日志在 [progress/M2.md](progress/M2.md)。M1 只剩“待定的逐轮讨论”没勾：排好的九轮都讨论完了，剩下零散的细节（见“悬而未决”），和 M2 交错进行（日志 [progress/M1.md](progress/M1.md)）。开发在 `dev` 分支上做，每轮提交一次 wip；用户说"整理合并"时再整理进 main 并推到 GitHub（AGENTS.md "提交"）。
- GitHub：https://github.com/nyxel-lang/nyxel，2026-10-05 起公开，只推 `main`。名字占位全部完成（brand.md）。ADR-0030 到 0035 已在 main 上（c1f91bf、af5226c 等）。
- 语言设计：ADR-0004 到 0027、0029 到 0035，汇总在 design/language-reference.md。ADR-0028 是文档流程：ADR 保持最新，被推翻的决定改写原文，文末记修订（规则在 AGENTS.md）。
- 语义分析：design/semantic-analysis.md 写好了（实现前的设计）。要点：符号一套接口、两种来源（Nyxel 源码、包装 Roslyn 读出的元数据）；分五遍（声明、导入、类型头、成员签名、函数体）；`BindExpression` 带期望的类型；重载决议和类型推断自己实现 C# 的规则，和 Roslyn 做对照测试；先打通“第一条线”（类、函数、局部变量、基本类型、控制流、调用 .NET、插值、数组）到 `nyxel run`，再逐步加宽。
- 语法分析：Lexer、Parser、无损语法树、带 `NYX` 错误码的诊断（design/syntax-tree.md、design/diagnostics.md）。`nyxel parse [--tree] <文件>` 检查语法、打印语法树。samples/ 有 16 个样例。
- 高亮：tools/vscode-nyxel 的 `npm test` 用 VSCode 的 TextMate 引擎给 samples 和 test/cases 分词，和快照比对；`tools\build.cmd --test` 和 CI 的 highlighting job 都跑（在 dev 上，CI 的 job 下次合并后第一次跑）。
- 错误码：`NYX1xxx` / `NYX2xxx` 段内按添加顺序编号，下一个是 NYX1235，语义诊断从 NYX2001 开始（design/diagnostics.md）。编译器还没有警告：命名检查和文档注释的检查是最早的一批。
- 最近一次全绿：2026-10-31，`tools\build.cmd --test`，8 + 770 个测试，高亮快照 19 项；`dotnet format --verify-no-changes` 通过。

## 下一步

1. 和用户讨论 semantic-analysis.md“待讨论”的两条，定了写 ADR：程序从哪里开始执行（`nyxel run`、可执行文件的入口）；局部变量和外层同名。
2. 实现第一条线，按顺序：
   - 符号和前四遍（`Symbols/`、`Binding/`），`NyxelCompilation`，跨文件的 `DiagnosticBag`；
   - 函数体的绑定和 `BoundTreePrinter`、第一步的重载决议和对照测试；
   - 降级（先写 design/lowering.md：列跟踪的文本写出器、`#line`）、后端、`nyxel build` / `run` / `--emit-cs`；
   - tests/…/Programs/ 的验收程序，VSCode 里手动验证断点。
3. 之后逐步加宽，补上各 ADR“后果”里留给 M2 的检查（清单按遍分在 semantic-analysis.md；逐条的出处：0021、0022、0023、0024、0025、0026、0027、0029、0030、0031、0032、0033、0034、0035）。

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
