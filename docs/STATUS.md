# 当前状态

> 每次收工**覆盖**本文件，保持一屏以内。历史在 [progress/](progress/)，计划在 [roadmap.md](roadmap.md)。
> 最后更新：2026-11-04

## 在哪

- 里程碑：**M2 语义分析与代码生成**，日志在 [progress/M2.md](progress/M2.md)。M1 只剩“待定的逐轮讨论”没勾，和 M2 交错进行。开发在 `dev` 分支上做，每轮提交一次 wip；用户说"整理合并"时再整理进 main 并推到 GitHub（AGENTS.md "提交"）。
- GitHub：https://github.com/nyxel-lang/nyxel，只推 `main`。2026-11-03 整理合并到 1f6a2be，CI 通过。之后 dev 上新增：ADR-0038、ADR-0039（还没进 main）。
- 语言设计：ADR-0004 到 0027、0029 到 0039，汇总在 design/language-reference.md。2026-11-04 对 language-reference 做了整体 review，列出十项候选，已讨论四项：
  - ADR-0038：整数字面量相除永远是整数除法、放在浮点位置报 NYX2054（已实现）；初始值是常量表达式的 `static let` 自动成为 C# `const`（还没实现，semantic-analysis.md“实现状态”）。
  - ADR-0039：没有 `self` 的函数必须写 `static func`（NYX1237 / 1238）；带数据的 enum 必须写明 `struct enum` 或 `class enum`（NYX1235 / 1236）。Parser、样例、快照都已更新。
- **M2 验收通过**：
  - 第一条线打通（2026-10-31）：Binder（design/semantic-analysis.md）、带 `#line` 的降级和 Roslyn 后端（design/lowering.md）、`nyxel build [--exe] [--emit-cs]` / `nyxel run`。tests/Nyxel.Compiler.Tests/Programs/ 的五个程序输出正确，C# 有快照，异常堆栈指向 .nyxel 的行。
  - 断点（2026-11-02，用户在 VSCode 的 C# 调试器里验证）：停在 .nyxel 的行上、单步、变量面板都正常（design/debug-mapping.md“在调试器里”）。缺口：`__nyxel_` 临时变量也显示在变量面板里。
  - M2 还没勾的：Binder 和降级逐步加宽、警告（命名、文档注释）。
- 高亮：tools/vscode-nyxel 的 `npm test`（分词快照），`--test` 和 CI 的 highlighting job 都跑。
- 错误码：下一个是 NYX1239 / NYX2055（design/diagnostics.md）。编译器还没有警告。
- 最近一次全绿：2026-11-04，`tools\build.cmd --test`，11 + 933 个测试，高亮快照 19 项；`dotnet format --verify-no-changes` 通过。

## 下一步

1. 继续讨论 review 剩下的候选（“悬而未决”第一条），用户挑；或者转回 Binder 加宽。
2. 逐步加宽 Binder（semantic-analysis.md“实现状态”列了缺口），每一步补上对应 ADR 留下的检查：空值和收窄（`T?`、元数据的可空标注、`is`、`?.`、`??`）→ 属性、struct、enum（含 `class enum`）、`static let` 常量（ADR-0038）→ `match` → lambda 和泛型推断（LINQ）→ 继承和接口、扩展 → `try`、`using`、`eval`、`with` → 运算符、索引器、事件 → 协程。
3. 让 samples/ 能完整地绑定：给假想的 `Engine` API 写一个只有签名的 C# 桩程序集。
4. 警告：命名检查（ADR-0006、0035）、文档注释的检查（ADR-0035）。

## 悬而未决

- 2026-11-04 整体 review 剩下的候选（讨论记录在对话里，没写进文档；下面是摘要）：
  - `init` 不写 `public` 是私有的，但自动生成的无参 `init` 是公开的；至少诊断要直接给出 `public init`。
  - 判断不带数据的 case 有 `==` 和 `is` 两种写法；候选：Nyxel 的 enum 只用 `is`。
  - `case` 一个词三种含义（enum 成员、match 分支、`await all / any` 的项）；`->` 形式的 `if` 最后一个分支吃掉后面的运算符（同 C# `?:`）；没有标注可空的程序集一律当 `T?`，建议项目文件里加按程序集的开关；ADR-0036 加上只收窄局部变量会不会造成命名 churn，写 samples 时观察。
  - 文档：reference 里 `_`、`??` 右边、`->` 分支的规则在多节重复，建议抽成一节。
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
