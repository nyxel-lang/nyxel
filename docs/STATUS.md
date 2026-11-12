# 当前状态

> 每次收工**覆盖**本文件，保持一屏以内。历史在 [progress/](progress/)，计划在 [roadmap.md](roadmap.md)。
> 最后更新：2026-11-11

## 在哪

- 里程碑：**M2 语义分析与代码生成**，日志在 [progress/M2.md](progress/M2.md)。M1 只剩“待定的逐轮讨论”没勾，和 M2 交错进行。开发在 `dev` 分支上做，每轮提交一次 wip；用户说"整理合并"时再整理进 main 并推到 GitHub（AGENTS.md "提交"）。
- GitHub：https://github.com/nyxel-lang/nyxel，只推 `main`。上次整理合并 2026-11-10（f76e631 到 9a49b48），之后的 wip 都还在 dev 上。
- 语言设计：ADR-0004 到 0027、0029 到 0044，汇总在 design/language-reference.md。
- **M2 验收通过**（第一条线 2026-10-31，断点 2026-11-02）。M2 还没勾的：Binder 和降级逐步加宽、警告（命名、文档注释、多余的 null 检查）。
- **属性和 struct 进了 Binder**（2026-11-11，ADR-0044）：自动属性和计算属性（`private set`、只读的自动属性在 `init` 里赋值、`static property`）、struct（`self` / `var self` 函数降级成 C# 的 `readonly` / 普通方法）、ADR-0032 的“存储、只读、副本”检查（NYX2080、2081）。实现时和用户定了三条（ADR-0044）：自动属性的值同字段、属性必须有 `get`、struct 上给属性赋值算“改”。新程序 Programs/Structs。
- 错误码：下一个是 NYX1243 / NYX2082（design/diagnostics.md；2055、2056 已分配给 ADR-0040，还没实现）。编译器还没有警告。
- 最近一次全绿：2026-11-11，`tools\build.cmd --test`，11 + 1153 个测试，高亮快照 19 项；`dotnet format --verify-no-changes` 通过。

## 下一步

1. 接着加宽 Binder（semantic-analysis.md“实现状态”）：enum（含 `class enum` / `struct enum`、NYX2055 / 2056）和 `static let` 常量（ADR-0038，enum case 的值要用到常量求值）→ `match`（含 `case null`、`case is T` 的收窄）→ lambda 和泛型推断（LINQ；带上 ADR-0042 的“被捕获的 `var` 不收窄”）→ 继承和接口、扩展 → `try`、`using`、`eval`、`with` → 运算符（含 struct 的 `==`，ADR-0030）、索引器、事件 → 协程。
2. 让 samples/ 能完整地绑定：给假想的 `Engine` API 写一个只有签名的 C# 桩程序集。
3. 警告：命名检查（ADR-0006、0035）、文档注释的检查（ADR-0035）、对不会是 null 的引用类型做 null 检查（ADR-0041）。

## 悬而未决

- ADR-0036 的只收窄局部变量会不会逼人改名：写 samples 时观察（ADR-0040 决策 4）。
- 之前几轮新增的待定：
  - `match` 里的元组模式、嵌套解构、解构时写类型、重写 .NET 里元组元素没有名字的成员（M3）。
  - 区间当值、展开、字典字面量（等 C#）、带元素创建多维数组。
  - 不要值的 `eval` 当语句（ADR-0025 先不放开）；`for _ in 0..<n`、lambda 参数写 `_`（ADR-0026 先不放开）。
  - `with` 用在已有的值上；Nyxel 自己声明只能在创建时设置的成员（ADR-0027）。
  - extension 里的索引器（等 C# 支持）；带数据 enum 的 `ToString`（ADR-0029）。
  - enum 和接口里的运算符（泛型数学）；自定义复合赋值 `operator +=`；struct 自定义相等（ADR-0030）。
  - 声明协变 / 逆变（ADR-0031）；ref 局部变量（ADR-0032）。
  - 等一个事件触发（`await signal`，ADR-0034）；ADR-0034 后果里 M3 / M4 要定的协程细节。
  - `var self` 函数里整个换掉 `self`（`self = 新值`，C# 的 `this = ...`）：现在按最严报 NYX2028，没讨论过。
  - C# 里只有 `set` 的接口属性在 Nyxel 里没法实现（ADR-0044 决策 2）：M3 实现接口时真碰到再看。
- 特性（attribute）的写法、`typeof` / `nameof`（ADR-0042 决策 7）：roadmap 记在 M3。项目文件 `.nyxelproj` 的格式和按程序集的“当作非空”开关：roadmap 记在 M4。
- roadmap “公开仓库前”剩下的几项（商标政策、命名 RFC、README 快速开始、Discord 等）和正式商标检索（brand.md），用户定什么时候做。
- `nyxelc` 是独立可执行文件还是 `nyxel build` 的别名（ADR-0001 后果）。
