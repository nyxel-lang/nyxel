# 文档索引

| 位置 | 内容 | 什么时候看 |
|---|---|---|
| [STATUS.md](STATUS.md) | 当前状态：在哪、下一步、悬而未决，一屏以内，每次覆盖 | 每次开工第一件事 |
| [roadmap.md](roadmap.md) | 里程碑，每项可勾选 | 决定下一步做什么 |
| [progress/](progress/) | 每个里程碑一个文件的开发日志，按日期追加 | 查历史、查某个坑的细节 |
| [decisions/](decisions/) | ADR，一个决策一个文件；合进 main 后只追加不修改 | 想改设计之前先看为什么当初这么定 |
| [design/](design/) | 设计文档，描述当前状态 | 动某个部分之前 |
| [brand.md](brand.md) | 名字的发音细节、联想与风险，商标 / 域名 / 包名检索记录，要占位的名字，品牌与社区设想 | 注册、占位、对外发布、写宣传材料时 |

## 设计文档

- [language-reference.md](design/language-reference.md) 语言参考（草稿）：已定的语法和语义，随讨论补全
- [architecture-overview.md](design/architecture-overview.md) 编译流水线、各组件职责与依赖方向
- [syntax-tree.md](design/syntax-tree.md) Lexer、Parser 和语法树：无损的树、trivia 归属、行规则怎么实现、优先级和泛型歧义、错误恢复、测试与快照
- [diagnostics.md](design/diagnostics.md) 诊断的格式和全部错误码（`NYX` 加四位数字）
- [debug-mapping.md](design/debug-mapping.md) 生成的 C# 怎么用 `#line` 把断点、堆栈、诊断映射回 .nyxel：指令形式、生成规则、实测行为
- [coding-conventions.md](design/coding-conventions.md) 编码规范、文档与提交流程

## 决策记录

| ADR | 决策 |
|---|---|
| [0001](decisions/0001-project-identity.md) | 名称 Nyxel、发音 /ˈnɪksəl/、`.nyxel` / `.nyxelproj`、Apache-2.0 |
| [0002](decisions/0002-build-and-repo-layout.md) | 只面向 .NET 10；src / tests / samples / tools / docs 并列；slnx、产物进 build/、NuGet 版本集中管理 |
| [0003](decisions/0003-design-goals.md) | 设计优先级：读者能正确理解 > Agent 能一次写对 > .NET 互操作 > 性能 > 手写省键 |
| [0004](decisions/0004-compile-to-csharp-via-roslyn.md) | 编译模型：自研前端，降级成 C# 交给进程内 Roslyn 出 dll + pdb，`#line` 映射回 .nyxel |
| [0005](decisions/0005-syntax-family.md) | 语法风格：现代花括号系（关键字开头的声明、类型后置、无分号、条件无括号但必须有花括号） |
| [0006](decisions/0006-declarations.md) | 声明：`let` / `var`、字段和签名必须写类型、`func ... -> R`、默认私有 + `public`、.NET 命名约定由编译器检查、关键字用完整单词或通用缩写 |
| [0007](decisions/0007-comments-literals-expressions.md) | 只有 `//` 和 `///`（Markdown）；C# 内置类型名；数值字面量随上下文定型、无后缀；`if` / `match` 是表达式、无 `?:`、函数必须 `return`；`$"..."` 插值同 C# |
| [0008](decisions/0008-type-definitions.md) | 类型：class / struct / interface 同 C#；`enum` + `case`（可带数据）；`init` 最多一个不带名字、其余命名；创建一律 `new`；成员一律 `self.` / `类型名.`；参数里有 `self` 是实例方法，struct 改自己写 `var self`，`let` 的 struct 整个不可变 |
| [0009](decisions/0009-null-safety-and-match.md) | 空值：`T` / `T?`，违反是编译错误，无标注 .NET API 当 `T?`；局部变量流分析收窄；`??` 可接 `return` / `throw` / `continue` / `break`；没有强制解包。match：`case 模式 -> 值`、`else`、按字段名取数据、不支持改名、必须穷尽 |
| [0010](decisions/0010-loops-operators-patterns.md) | `for x in`、`while`，无 C 风格 for；区间 `..<` / `...`；`and` / `or` / `not`；没有 `++` / `--`；模式：常量、比较、区间、`or`、`is` 类型（局部变量自动收窄）、`when` |
| [0011](decisions/0011-properties-assignment-arguments.md) | 冒号后是类型、等号后是值；`property` 声明属性（计算 / 自动，`set(value)`、`private set`）；赋值是语句；命名实参 `name = value`（可选）；不能重载，用默认参数 |
| [0012](decisions/0012-namespaces-lambdas-generics.md) | 文件结构 `namespace` → `import` → 声明；每个文件显式 `import`，无全局 / 隐式导入；函数类型 `func(A) -> R` / `func(A)`；lambda `func(e) = 表达式` / `func(e) { }`；函数可用 `= 表达式` 体；泛型 `<T: 约束 and 约束>` |
| [0013](decisions/0013-default-init-literals-line-continuation.md) | 没写 `init` 自动有公开无参 `init`；`super.init(...)` 是第一句，`self.init(...)` 委托，`super.方法`；混合运算期望类型往里传但不改变整数运算；续行：括号里、下一行以 `.` / 二元运算符开头、行尾 `=` / `->` |
| [0014](decisions/0014-conversions-parameters-extensions-exceptions.md) | 显式转换 `x as T`（只做不会因类型不对而失败的转换，向下转换用 `is`）；参数不能重新赋值；`extension 类型 { }` 块（对应 C# 14）；.NET 异常：`try` / `catch e: 类型` / `when` / `finally`，`try` 可当表达式 |
| [0015](decisions/0015-ref-out-using-protected.md) | 调用处写 `out let x` / `out 目标` / `out _` / `ref 目标`；自己的函数只在 override / 实现接口时声明 `out` / `ref`；`using x = ...` 释放资源（无块形式、无 `defer`）；加 `protected`，不加 `internal` |
| [0016](decisions/0016-async-await-launch.md) | 协程：`async func` + `await`（能等任何 .NET 可等待对象，恢复在主线程）；调用必须写 `await` 或 `launch`；`launch obj.M()` 绑定 `obj`，销毁后自动停止（`finally` 照常执行，`catch` 接不住）；热重载停止所有协程 |
| [0017](decisions/0017-events-and-state-machines.md) | 事件 `event Died(slime: Slime)` + `emit`；`+=` / `-=` 订阅，绑定“执行谁的代码”，销毁或热重载时自动移除；方法可当函数值；状态机不加语法（enum + match + 协程） |
| [0018](decisions/0018-npm-scope.md) | npm scope 用 `@nyxel-lang`（`@nyxel` 被别人注册了），取代 ADR-0001 的这一项 |
| [0019](decisions/0019-syntax-details-for-parser.md) | 运算符一律放行首（括号里也一样）；修饰符顺序固定；区间优先级比算术低、比比较高；错误码 `NYX` 加四位数字 |
| [0020](decisions/0020-literals-arrays-blocks.md) | 字符字面量 `'a'`；`0.5` 不省 0；原始字符串 `"""`（没有 `@"..."`）；数组类型 `array<T>`；不能嵌套类型；没有单独的块；赋值运算符可以写在行尾 |
| [0021](decisions/0021-tuples-deconstruction-loops.md) | 元组 `(Min: int, Max: int)`（元素必须有名字）；按位置解构 `let (a, b) = ...`；`for (key, value) in dict`、`for (i, item) in list.Index()`；没有循环标签 |
| [0022](decisions/0022-ranges-slices-collection-literals.md) | `(0..<n).Reversed()`、`.StepBy(2)`；区间不是值；切片 `name[0..<3]`、`name[1...]`（没有 `^1`）；集合字面量 `[1, 2, 3]`（没有展开和字典字面量）；多维数组 `array2d<T>` |
| [0024](decisions/0024-enums-flags-struct-enum-case-data.md) | 简单枚举写值（要写就都写）、`enum Tile : byte`、`flags enum`；`x is 类型.Case`；带数据枚举的值类型版本 `struct enum`、`==` 比内容；case 的数据同元组（PascalCase、按位置取出，取代 ADR-0009 决策 6） |
| [0023](decisions/0023-array-elements-null-checks-coalesce-assignment.md) | 只有全零合法的元素类型能直接 `new array<T>(n)`，其余用 `element` 给出每个元素；和 `null` 比较只看引用（不调用重载的 `==`）；`??=` |

新建决策：`./tools/new-adr.ps1 <slug>`。
