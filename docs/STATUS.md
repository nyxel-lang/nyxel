# 当前状态

> 每次收工**覆盖**本文件，保持一屏以内。历史在 [progress/](progress/)，计划在 [roadmap.md](roadmap.md)。
> 最后更新：2026-11-03

## 在哪

- 里程碑：**M2 语义分析与代码生成**，日志在 [progress/M2.md](progress/M2.md)。M1 只剩“待定的逐轮讨论”没勾，和 M2 交错进行。开发在 `dev` 分支上做，每轮提交一次 wip；用户说"整理合并"时再整理进 main 并推到 GitHub（AGENTS.md "提交"）。
- GitHub：https://github.com/nyxel-lang/nyxel，只推 `main`。2026-11-03 整理合并：高亮快照测试、语义分析设计、ADR-0036、M2 第一条线、ADR-0037 都已在 main 上。
- 语言设计：ADR-0004 到 0027、0029 到 0037，汇总在 design/language-reference.md。最近两轮：ADR-0036 程序入口是没有 `self` 的 `Main`，一个函数里一个名字只指一个变量；ADR-0037 调用里的字面量跟着有类型的实参（`Math.Max(speed, 0.5)` 选 `float` 的那个），已实现。
- **M2 验收通过**：
  - 第一条线打通（2026-10-31）：Binder（design/semantic-analysis.md）、带 `#line` 的降级和 Roslyn 后端（design/lowering.md）、`nyxel build [--exe] [--emit-cs]` / `nyxel run`。tests/Nyxel.Compiler.Tests/Programs/ 的五个程序输出正确，C# 有快照，异常堆栈指向 .nyxel 的行。
  - 断点（2026-11-02，用户在 VSCode 的 C# 调试器里验证）：停在 .nyxel 的行上、单步、变量面板都正常（design/debug-mapping.md“在调试器里”）。缺口：`__nyxel_` 临时变量也显示在变量面板里。
  - M2 还没勾的：Binder 和降级逐步加宽、警告（命名、文档注释）。
- 高亮：tools/vscode-nyxel 的 `npm test`（分词快照），`--test` 和 CI 都跑（CI 的 job 这次合并后第一次跑）。
- 错误码：下一个是 NYX1235 / NYX2054（design/diagnostics.md）。编译器还没有警告。
- 最近一次全绿：2026-11-03，`tools\build.cmd --test`，11 + 921 个测试，高亮快照 19 项；`dotnet format --verify-no-changes` 通过。

## 下一步

1. 逐步加宽 Binder（semantic-analysis.md“实现状态”列了缺口），每一步补上对应 ADR 留下的检查：空值和收窄（`T?`、元数据的可空标注、`is`、`?.`、`??`）→ 属性、struct、enum → `match` → lambda 和泛型推断（LINQ）→ 继承和接口、扩展 → `try`、`using`、`eval`、`with` → 运算符、索引器、事件 → 协程。
2. 让 samples/ 能完整地绑定：给假想的 `Engine` API 写一个只有签名的 C# 桩程序集。
3. 警告：命名检查（ADR-0006、0035）、文档注释的检查（ADR-0035）。

## 悬而未决

- 之前几轮新增的待定：
  - `match` 里的元组模式、嵌套解构、解构时写类型、重写 .NET 里元组元素没有名字的成员（M3）。
  - 区间当值、展开、字典字面量（等 C#）、带元素创建多维数组。
  - 不要值的 `eval` 当语句（ADR-0025 先不放开）；`for _ in 0..<n`、lambda 参数写 `_`（ADR-0026 先不放开）。
  - `with` 用在已有的值上；Nyxel 自己声明只能在创建时设置的成员（ADR-0027）。
  - extension 里的索引器（等 C# 支持）；带数据 enum 的 `ToString`（ADR-0029）。
  - enum 和接口里的运算符（泛型数学）；自定义复合赋值 `operator +=`；struct 自定义相等（ADR-0030）。
  - 声明协变 / 逆变（ADR-0031）；ref 局部变量（ADR-0032）。
  - 等一个事件触发（`await signal`，ADR-0034）；ADR-0034 后果里 M3 / M4 要定的协程细节。
- 特性（attribute）的写法：roadmap 记在 M3。
- roadmap “公开仓库前”剩下的几项（商标政策、命名 RFC、README 快速开始、Discord 等）和正式商标检索（brand.md），用户定什么时候做。
- `nyxelc` 是独立可执行文件还是 `nyxel build` 的别名（ADR-0001 后果）。
