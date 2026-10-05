# 当前状态

> 每次收工**覆盖**本文件，保持一屏以内。历史在 [progress/](progress/)，计划在 [roadmap.md](roadmap.md)。
> 最后更新：2026-10-05

## 在哪

- 里程碑：**M1 语言基础设计 + 语法分析进行中**，日志在 [progress/M1.md](progress/M1.md)。开发在 `dev` 分支上做，每轮提交一次 wip，用户说"整理合并"时再整理进 main 并推到 GitHub（CLAUDE.md "提交"）。
- GitHub：https://github.com/nyxel-lang/nyxel，现在是 Private，只推 `main`。2026-10-05 重写过一次历史：去掉旧提交里的本机路径，提交邮箱换成 GitHub noreply 地址；本地 master 同时改名 main。本机专属说明和维护者的协作偏好在 CLAUDE.local.md（不进仓库）。
- M0 骨架完成：五个产品项目可编译，`nyxel --version`，CI、开源文件、文档体系、VSCode 文件关联。
- **语言设计的大主题讨论完了**：ADR-0004 到 0017，十三轮，汇总在 design/language-reference.md，索引见 docs/README.md。剩下的细节在各节末尾的“待定”里。
- samples/ 有八个示例文件，用的全是已定的语法。VSCode 扩展覆盖全部关键字，`tools\vscode-dev.cmd` 一键试用。
- `#line` 映射验证通过（design/debug-mapping.md）。
- 最近一次全绿：2026-10-05，`tools\build.cmd --test`，5 + 15 个测试；`dotnet format --verify-no-changes` 通过。GitHub Actions 首跑（main 1ee71bc）ubuntu + windows 全部通过。

## 下一步

1. M1 剩下的实现：Lexer、Parser、语法树、诊断（错误码 + 位置）。先写 design/syntax-tree.md（语法树的形状、诊断格式、续行规则怎么实现），这是工程决定，自己定、回复里说明。验收是 samples/ 全部能解析、语法树能打印，故意写错的样例给出准确的错误。
2. 同时把 build/grammar-check 的分词检查改成正式的高亮快照测试。

## 悬而未决

- 公开仓库：GitHub 的 Activity 记录着那次 force push，旧提交 4995cb6 按哈希还能打开（个人邮箱、本机路径），建议用户删仓库重建后再公开（重建后重填描述和 Topics）。公开后打开 Private vulnerability reporting。roadmap “公开仓库前”其余几项（商标政策、命名 RFC、快速开始、社区渠道）不挡公开，用户定。
- 要用户本人做的：注册域名（优先 nyxel.dev）、占位 NuGet / npm 包名、正式商标检索（中国第 9 / 41 / 42 类，USPTO / EUIPO / WIPO）。清单和检索记录在 [brand.md](brand.md)。
- `nyxelc` 是独立可执行文件还是 `nyxel build` 的别名（ADR-0001 后果）。
- VSCode 扩展的 publisher id 暂填 `nyxel-lang`，要在 Marketplace 注册后才能发布。
