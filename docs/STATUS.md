# 当前状态

> 每次收工**覆盖**本文件，保持一屏以内。历史在 [progress/](progress/)，计划在 [roadmap.md](roadmap.md)。
> 最后更新：2026-10-02

## 在哪

- 里程碑：**M1 语言基础设计 + 语法分析进行中**，日志在 [progress/M1.md](progress/M1.md)。开发在 `dev` 分支上做，每轮提交一次 wip，用户说"整理合并"时再整理进 master（CLAUDE.md "提交"）。
- M0 骨架完成：五个产品项目可编译，`nyxel --version`，CI、开源文件、文档体系、VSCode 文件关联。
- **语言设计的大主题讨论完了**：ADR-0004 到 0017，十三轮，汇总在 design/language-reference.md，索引见 docs/README.md。最后几轮：默认 init / super / 续行（0013）、`as` / 参数不可变 / `extension` / 异常（0014）、`out` / `ref` / `using` / `protected`（0015）、协程 `async` / `await` / `launch`（0016）、事件 `event` / `emit` + 自动绑定的订阅、状态机不加语法（0017）。剩下的细节在各节末尾的“待定”里。
- samples/ 有八个示例文件，用的全是已定的语法。VSCode 扩展覆盖全部关键字，`tools\vscode-dev.cmd` 一键试用。
- `#line` 映射验证通过（design/debug-mapping.md）。
- 最近一次全绿：2026-10-02，`tools\build.cmd --test`，5 + 15 个测试；`dotnet format --verify-no-changes` 通过。

## 下一步

1. 语言设计已整理合并进 master（2026-10-02）。之后改已合并的 ADR 要新建 ADR。用户读的过程中觉得不顺的地方随时提。
2. M1 剩下的实现：Lexer、Parser、语法树、诊断（错误码 + 位置）。先写 design/syntax-tree.md（语法树的形状、诊断格式、续行规则怎么实现），这是工程决定，自己定、回复里说明。验收是 samples/ 全部能解析、语法树能打印，故意写错的样例给出准确的错误。
3. 同时把 build/grammar-check 的分词检查改成正式的高亮快照测试。

## 悬而未决

- 要用户本人做的：创建 GitHub 组织 nyxel-lang、注册域名（优先 nyxel.dev）、占位 NuGet / npm 包名、正式商标检索（中国第 9 / 41 / 42 类，USPTO / EUIPO / WIPO）。清单和检索记录在 [brand.md](brand.md)。
- CODE_OF_CONDUCT.md、SECURITY.md 需要一个联系渠道（邮箱或 GitHub 私密漏洞报告），公开仓库前补。
- `nyxelc` 是独立可执行文件还是 `nyxel build` 的别名（ADR-0001 后果）。
- VSCode 扩展的 publisher id 暂填 `nyxel-lang`，要在 Marketplace 注册后才能发布。
