# 编码规范

## C#（编译器、运行时、工具）

- .NET 10，Nullable 启用，file-scoped namespace，`var` 允许，大括号不省略。警告即错误。
- 格式以 `dotnet format` 为准，CI 会检查。
- Nyxel.Runtime 被加载进游戏进程：零第三方依赖，保持 `IsAotCompatible`。
- 可测的逻辑不直接碰 `Console` / 文件系统 / 环境变量，由调用方传入（参考 `Nyxel.Cli.Program.Run` 的写法）。
- 一个产品项目对应一个测试项目：`src/Nyxel.X` ↔ `tests/Nyxel.X.Tests`。
- 语言相关的名字（后缀、语言 id、MIME）从 `Nyxel.Compiler.LanguageInfo` 取。

## 语言

- docs/ 下的设计文档、ADR、进度日志用中文。
- 代码、代码注释、提交信息用英文。
- 面向外部贡献者的文件（README、CONTRIBUTING、Issue / PR 模板、CHANGELOG）用英文，README 附中文简介。

## 文档

- 改决定：新建 ADR（tools/new-adr.ps1），旧 ADR 标"被取代"，不改历史。"旧"指已经合进 master 的；dev 上还没合并的 ADR 不算定案，推翻时直接改原文件。
- 新决定满足任一条就写 ADR，和实现同一轮提交：约束多个组件；认真比较过备选（尤其是和用户讨论定的）；推翻代价高。语法和语义的每一项决定都满足第二条。
- 设计文档写"是什么"，随实现更新；ADR 写"为什么、比较过什么"；进度日志只记做了什么和坑，不承载理由。
- 每次收工覆盖 docs/STATUS.md（在哪、下一步），在 docs/progress/M<n>.md 追加日期日志。仍然有效的坑提升到 CLAUDE.md 或设计文档。

## 提交信息

`<area>: <summary>`，祈使语气，如 `compiler: parse match expressions`。正文写改了什么、为什么、怎么验证的，末尾 Co-Authored-By 行。area 用项目或目录名的简写：`compiler`、`runtime`、`host`、`sdk`、`cli`、`vscode`、`docs`、`build`。
