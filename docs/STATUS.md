# 当前状态

> 每次收工**覆盖**本文件，保持一屏以内。历史在 [progress/](progress/)，计划在 [roadmap.md](roadmap.md)。
> 最后更新：2026-10-06

## 在哪

- 里程碑：**M1 语言基础设计 + 语法分析，只剩高亮的分词快照测试**，日志在 [progress/M1.md](progress/M1.md)。开发在 `dev` 分支上做，每轮提交一次 wip，用户说"整理合并"时再整理进 main 并推到 GitHub（CLAUDE.md "提交"）。
- GitHub：https://github.com/nyxel-lang/nyxel，2026-10-05 起公开，只推 `main`。名字占位全部完成（brand.md）。
- 语言设计：ADR-0004 到 0019，汇总在 design/language-reference.md。
- **语法分析完成**：Lexer、Parser、无损语法树、带 `NYX` 错误码的诊断（design/syntax-tree.md、design/diagnostics.md）。`nyxel parse [--tree] <文件>` 检查语法、打印语法树。8 个样例零诊断、树有快照；每个错误码都有故意写错的测试；C# 习惯写法（`&&`、`(int)x`、`i++`、`;` 等）各给一条带 Nyxel 写法的错误。
- 最近一次全绿：2026-10-06，`tools\build.cmd --test`，8 + 256 个测试；`dotnet format --verify-no-changes` 通过。

## 下一步

1. 高亮的分词快照测试：把 build/grammar-check 的 vscode-textmate 脚本搬进 tools/vscode-nyxel（devDependencies + `npm test`），对 samples 生成作用域快照；CI 加 setup-node。完成后 M1 收尾。
2. M2 开头：先写语义分析的设计文档（Binder：名字解析、类型检查，经 Roslyn 符号模型读 .NET 类型），再动手。

## 悬而未决

- **实现语法分析时碰到、还没讨论的写法**（language-reference 末尾“待定”）：`'a'` 字符字面量、`.5`、数组类型 `T[]`、嵌套类型、单独的块语句、`+=` 写在行尾。目前都报错，下次一起讨论。
- roadmap “公开仓库前”剩下的几项（商标政策、命名 RFC、README 快速开始、Discord 等）没挡公开，用户定什么时候做。要用户本人做的还有正式商标检索（brand.md）。
- `nyxelc` 是独立可执行文件还是 `nyxel build` 的别名（ADR-0001 后果）。
