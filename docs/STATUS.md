# 当前状态

> 每次收工**覆盖**本文件，保持一屏以内。历史在 [progress/](progress/)，计划在 [roadmap.md](roadmap.md)。
> 最后更新：2026-10-16

## 在哪

- 里程碑：**M1 语言基础设计 + 语法分析**。语法分析已完成，剩下高亮的分词快照测试，以及各节“待定”的逐轮讨论。日志在 [progress/M1.md](progress/M1.md)。开发在 `dev` 分支上做，每轮提交一次 wip；用户说"整理合并"时再整理进 main 并推到 GitHub（AGENTS.md "提交"）。
- GitHub：https://github.com/nyxel-lang/nyxel，2026-10-05 起公开，只推 `main`。名字占位全部完成（brand.md）。
- 语言设计：ADR-0004 到 0027，汇总在 design/language-reference.md。最近一批（2026-10-15 合进 main）：
  - ADR-0025（用户提议）：`eval { ... give 值 }`；`if` / `try` 当值用时分支写 `->`，当语句用时写 `{ }`。
  - ADR-0026（整体 review）：`case _` 默认分支；`_` 是保留记号；成员上不写 `private`；`get = 表达式`。
  - ADR-0027（第 5 轮第一批“创建对象”）：创建时设置成员 `new T(...) with (X = 1)`（也用来设 C# 的 `init` 访问器和 `required`）；Nyxel 暂不声明只能在创建时设置的成员；调用其他 init 写 `init super(...)` / `init super.名字(...)` / `init self(...)` / `init self.名字(...)`（用户提议，取代 ADR-0013 的 `super.init(...)`）。
- 文档流程（2026-10-16 合进 main）：ADR-0028，ADR 保持最新。被推翻的决定直接改写原文（所有写过它的 ADR，例子也改），原来的做法挪进备选方案，文末“修订记录”记日期和讨论在哪；历史看 git（规则在 AGENTS.md）。已有的都改成了最新（12 个改写了决定，另外几个只改引用和说法）。项目说明 2026-10-15 改名 AGENTS.md，CLAUDE.md 只剩一行导入。
- 错误码：`NYX1xxx` / `NYX2xxx` 段内按添加顺序编号，下一个是 NYX1201（design/diagnostics.md）。
- 语法分析：Lexer、Parser、无损语法树、带 `NYX` 错误码的诊断（design/syntax-tree.md、design/diagnostics.md）。`nyxel parse [--tree] <文件>` 检查语法、打印语法树。samples/ 有 11 个样例。
- 最近一次全绿：2026-10-16，`tools\build.cmd --test`，8 + 582 个测试；`dotnet format --verify-no-changes` 通过。

## 下一步

1. 继续讨论“待定”，每轮几项相关的；定了写 ADR、实现、提交：
   - 第 5 轮第二批“成员声明”：索引器、`field`、事件访问器（`add` / `remove`）、enum 实现接口和在 enum 里写方法（ADR-0024 的待定）。
   - 第 6 轮 泛型与扩展：泛型的可空性（包括 `new array<T>(n)`）、协变 / 逆变、泛型扩展、`var self` 扩展、没标 `readonly` 的 struct 方法。
   - 第 7 轮 导入：别名、静态导入。
   - 第 8 轮 协程与资源：同时等多个协程、句柄和 `Wait` 的命名、`await using`、事件处理函数抛异常。
   - 第 9 轮 约定：文档注释的写法、缩略词大小写、插值 `{ }` 里换行。
   - 第 6、8 轮有一部分是纯语义，可以留到 M2 / M3 实现时再定。
2. 高亮的分词快照测试：
   - 把 build/grammar-check 的 vscode-textmate 脚本搬进 tools/vscode-nyxel（devDependencies + `npm test`），对 samples 生成作用域快照；CI 加 setup-node。
   - samples 里没有原始字符串和字符字面量，要加一个专门的用例文件，可以从 build/grammar-check/literals.nyxel 开始。
3. M2 开头：先写语义分析的设计文档（Binder：名字解析、类型检查，经 Roslyn 符号模型读 .NET 类型），再动手。前几轮留给 M2 的：
   - ADR-0021：没有期望的类型时元组值要带名字、解构顺序相反的警告、`Index()` 翻译成计数器。
   - ADR-0022：集合字面量的期望类型、`for` 里字面量的元素类型、步长是正数、`Reversed()` / `StepBy()` 翻译成 `for`。
   - ADR-0023：数组元素类型全零是否合法、`element` 填充翻译成循环、`== null` 翻译成 `is null`、`??=` 之后的收窄。
   - ADR-0024：case 的值是常量、位运算只用于 flags enum、flags enum 的 match 要 `case _`、`is` 后面是类型还是 case、取 case 数据的个数和顺序警告；`struct enum` 的布局、带数据枚举的 `Equals` / `==`。
   - ADR-0025：`eval` 每条路径都以 `give` / `throw` 结束（流分析）、块和 `->` 分支的类型；翻译：`eval` 是变量 + 块 + `goto`，`->` 形式的 `if` / `try` 在表达式中间时要把前面的操作数先存进临时变量，保持求值顺序。
   - ADR-0026：访问器“有的带体有的不带”的检查要把 `get = 表达式` 算作带体。
   - ADR-0027：`init super` / `init self` 只能是 `init` 的第一句；`with` 里的成员能不能设、`required` 是否齐全；命名 init 不继承；命名 init 背后的构造函数对子类可见（protected）。

## 悬而未决

- 之前几轮新增的待定：
  - `match` 里的元组模式、嵌套解构、解构时写类型、重写 .NET 里元组元素没有名字的成员（M3）。
  - 区间当值、展开、字典字面量（等 C#）、带元素创建多维数组。
  - 带数据的 enum 实现接口；在 enum 里写方法（第 5 轮第二批）。
  - 不要值的 `eval` 当语句（ADR-0025 先不放开）。
  - `for _ in 0..<n`、lambda 参数写 `_`（ADR-0026 先不放开）。
  - `with` 用在已有的值上（复制一份再改几个成员）；Nyxel 自己声明只能在创建时设置的成员（ADR-0027）。
- 特性（attribute）的写法：roadmap 记在 M3。
- roadmap “公开仓库前”剩下的几项（商标政策、命名 RFC、README 快速开始、Discord 等）没挡公开，用户定什么时候做。要用户本人做的还有正式商标检索（brand.md）。
- `nyxelc` 是独立可执行文件还是 `nyxel build` 的别名（ADR-0001 后果）。
