# 当前状态

> 每次收工**覆盖**本文件，保持一屏以内。历史在 [progress/](progress/)，计划在 [roadmap.md](roadmap.md)。
> 最后更新：2026-10-31

## 在哪

- 里程碑：**M1 语言基础设计 + 语法分析**。语法分析已完成，剩下高亮的分词快照测试，以及各节“待定”的逐轮讨论。日志在 [progress/M1.md](progress/M1.md)。开发在 `dev` 分支上做，每轮提交一次 wip；用户说"整理合并"时再整理进 main 并推到 GitHub（AGENTS.md "提交"）。
- GitHub：https://github.com/nyxel-lang/nyxel，2026-10-05 起公开，只推 `main`。名字占位全部完成（brand.md）。
- 语言设计：ADR-0004 到 0027、0029 到 0033，汇总在 design/language-reference.md。ADR-0028 是文档流程：ADR 保持最新，被推翻的决定改写原文，文末记修订（规则在 AGENTS.md）。
- 最近四轮（2026-10-30 到 31，在 dev 上，还没合进 main）：
  - ADR-0030（第 5 轮第三批）：`public operator +` 定义运算符；比较只写 `<`；struct 和 enum 的 `==` 比内容；只能定义显式转换 `operator as`。修订了 ADR-0011、0014。
  - ADR-0031（第 6 轮第一批，泛型，四项都按推荐）：写 `T?` 时约束要看得出 T 是 class 还是 struct，加 `notnull`；元素是 T 的数组写 `element`，`array<T?>` 可以直接创建；暂不声明协变 / 逆变；泛型扩展 `extension<T> List<T>`。新样例 samples/Common/Collections.nyxel。
  - ADR-0032（第 6 轮第二批，struct 的修改，三项都按推荐）：哪些写法是副本同 C#（数组元素、`Span` 元素、`ref` 返回值能直接改；属性、`List` 的 `[]`、函数返回值是副本）；扩展里能写 `var self`；C# 没标 `readonly` 的方法在只读的值或副本上调用、又不用返回值时报错。修订了 ADR-0008 决策 6。全是语义规则，Parser 没改。新样例 samples/Ecs/Spin.nyxel（照 EnginePlayground 的旋转系统）。
  - ADR-0033（第 7 轮，导入）：同名类型同 C#（自己命名空间里的优先）；只能给命名空间起别名，写 `import ep.protocol under Pb`（写法是用户在 `=`、`as`、`alias` 之外提出的）；没有静态导入。新错误码 NYX1225–NYX1227，新样例 samples/Common/TeamMapping.nyxel。
- 错误码：`NYX1xxx` / `NYX2xxx` 段内按添加顺序编号，下一个是 NYX1228（design/diagnostics.md）。
- 语法分析：Lexer、Parser、无损语法树、带 `NYX` 错误码的诊断（design/syntax-tree.md、design/diagnostics.md）。`nyxel parse [--tree] <文件>` 检查语法、打印语法树。samples/ 有 15 个样例。
- 最近一次全绿：2026-10-31，`tools\build.cmd --test`，8 + 715 个测试；`dotnet format --verify-no-changes` 通过。

## 下一步

1. 继续讨论“待定”，每轮几项相关的；定了写 ADR、实现、提交：
   - 第 8 轮 协程与资源：同时等多个协程、句柄和 `Wait` 的命名、`await using`、事件处理函数抛异常。
   - 第 9 轮 约定：文档注释的写法、缩略词大小写、插值 `{ }` 里换行。
   - 第 8 轮有一部分是纯语义，可以留到 M2 / M3 实现时再定。
2. 高亮的分词快照测试：把 build/grammar-check 的 vscode-textmate 脚本搬进 tools/vscode-nyxel（devDependencies + `npm test`），对 samples 生成作用域快照，CI 加 setup-node；加一个有原始字符串和字符字面量的用例文件（可以从 build/grammar-check/literals.nyxel 开始）。
3. M2 开头：先写语义分析的设计文档（Binder：名字解析、类型检查，经 Roslyn 符号模型读 .NET 类型），再动手。前几轮留给 M2 的检查和翻译记在各 ADR 的“后果”里：0021（元组）、0022（区间、集合字面量）、0023（数组元素、`== null`）、0024（enum 的值、flags、`struct enum` 布局）、0025（`eval` 的流分析和翻译）、0026（访问器带体）、0027（`init super`、`with`、`required`）、0029（索引器的访问器、enum 冒号后面是接口、事件和委托一致、enum 成员的翻译、C# 那边怎么取 case）、0030（运算符的参数类型和重载决议、生成的相等和比较成员、C# 的隐式转换）、0031（`T?` 的约束、没约束的 T 用成员前检查、元素是 T 的数组）、0032（存储还是副本、没标 `readonly` 的方法、`var self` 扩展的翻译）、0033（同名类型的查找、`under` 前面是命名空间、别名重名）。

## 悬而未决

- 之前几轮新增的待定：
  - `match` 里的元组模式、嵌套解构、解构时写类型、重写 .NET 里元组元素没有名字的成员（M3）。
  - 区间当值、展开、字典字面量（等 C#）、带元素创建多维数组。
  - 不要值的 `eval` 当语句（ADR-0025 先不放开）；`for _ in 0..<n`、lambda 参数写 `_`（ADR-0026 先不放开）。
  - `with` 用在已有的值上；Nyxel 自己声明只能在创建时设置的成员（ADR-0027）。
  - extension 里的索引器（等 C# 支持）；带数据 enum 的 `ToString`（ADR-0029）。
  - enum 和接口里的运算符（泛型数学）；自定义复合赋值 `operator +=`；struct 自定义相等（ADR-0030）。
  - 声明协变 / 逆变（ADR-0031，以后同 C# 的 `out T` / `in T`）；ref 局部变量（ADR-0032）。
- 特性（attribute）的写法：roadmap 记在 M3。
- roadmap “公开仓库前”剩下的几项（商标政策、命名 RFC、README 快速开始、Discord 等）没挡公开，用户定什么时候做。要用户本人做的还有正式商标检索（brand.md）。
- `nyxelc` 是独立可执行文件还是 `nyxel build` 的别名（ADR-0001 后果）。
