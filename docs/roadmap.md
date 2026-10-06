# 路线图

完成一项就勾选；里程碑内顺序可调，里程碑之间尽量不跳。当前状态看 [STATUS.md](STATUS.md)，历史日志在 [progress/](progress/)。

M3 以后是草案，随 M1 的语法讨论细化。

## M0 仓库骨架（2026-10-02 完成）

- [x] 目录结构、Nyxel.slnx、Directory.Build.props / Directory.Packages.props、global.json（.NET 10）
- [x] 五个产品项目（Compiler / Runtime / Host / Sdk / Cli）可编译，`nyxel --version`
- [x] tools/build.cmd 一键构建 + 测试 + 打包；GitHub Actions CI（ubuntu + windows，含 format 检查）
- [x] LICENSE / NOTICE（Apache-2.0）、README（含发音）、CONTRIBUTING、CHANGELOG、Issue / PR 模板
- [x] 文档体系：CLAUDE.md、STATUS / roadmap / progress / decisions / design；ADR-0001 到 ADR-0003
- [x] tools/vscode-nyxel：.nyxel 文件关联（声明式扩展，语法高亮等语法定了再填）
- [x] git：master + dev 分支（2026-10-05 master 改名 main，推到 GitHub nyxel-lang/nyxel）

## M1 语言基础设计 + 语法分析

每一项设计决定都和用户讨论，定了写 ADR。

- [x] 编译模型：自研前端 + 翻译成 C# 交给 Roslyn（ADR-0004）
- [x] 语法总体风格：现代花括号系（ADR-0005）
- [x] 技术验证（ADR-0004）：手写带 `#line` 的 C# → 进程内 Roslyn → dll + pdb，序列点、异常堆栈、诊断都指向 .nyxel（RoslynLineMappingTests，规则见 design/debug-mapping.md）；真实调试器停断点并入 M2 验收
- [x] 核心语法逐项定稿：声明与类型标注、可变性、函数、控制流与表达式、空值、类型定义（class / struct / 枚举 / 联合）、模式匹配、泛型、命名空间与导入、命名约定、注释与文档注释（ADR-0006 到 0017，十三轮讨论；剩下的细节记在 language-reference 各节的“待定”里）
- [x] docs/design/language-reference.md 初稿 + samples/ 下的示例脚本（八个文件）
- [x] 词法分析（Lexer）、语法分析（Parser）、语法树、诊断（带错误码和位置）：design/syntax-tree.md、design/diagnostics.md；实现时碰到的四项细节定在 ADR-0019
- [x] VSCode 语法高亮（TextMate 语法，覆盖已定的全部关键字；`tools/vscode-dev.cmd` 试用）。关键字表和 Lexer 的一致性有测试
- [ ] 高亮的分词快照测试（vscode-textmate 跑 samples，比对快照；要在 CI 里装 node）
- [x] 验收：samples/ 全部能解析，语法树能打印出来（`nyxel parse --tree`，快照测试）；故意写错的样例给出准确位置和可读的错误（ParserErrorTests）

## M2 语义分析与代码生成

架构见 [design/architecture-overview.md](design/architecture-overview.md)。

- [ ] Binder：名字解析、类型检查；.NET 类型信息经 Roslyn 符号模型读取（先写设计文档）
- [ ] 降级：绑定树 → C# 语法树，`#line` 跨度映射
- [ ] 后端：进程内 Roslyn 编译出 dll + pdb；Roslyn 诊断映射回 .nyxel 并按内部错误报出
- [ ] CLI：`nyxel build` / `nyxel run` / `--emit-cs`
- [ ] 快照测试：.nyxel → 生成的 C# 逐字比较
- [ ] 验收：hello world 和几个算法小程序能跑，断点能停在 .nyxel 源码行上

## M3 类型系统与 .NET 互操作（草案）

- [ ] class / struct / 接口 / 泛型 / 空值安全 / 模式匹配完整
- [ ] 调用任意 C# 库；C# 调用 Nyxel 编出的程序集
- [ ] Nyxel.Sdk：`.nyxelproj` 用 `dotnet build` 构建
- 待定（启动时列出的问题）：特性（attribute）的写法和使用范围；源生成器能否作用于 Nyxel 代码；反射、动态调用；是否允许 `unsafe` / P/Invoke

## M4 游戏脚本特性与宿主（草案）

- [ ] 游戏控制层的语言特性（协程 / 等待、状态机、事件等，具体哪些在 M1 讨论）。已定：协程（ADR-0016）、事件（ADR-0017），状态机不加语法。要实现 Nyxel.Runtime 的调度器、`Job`、`Wait`、生命周期接口、事件订阅的自动移除和热重载清理
- [ ] Nyxel.Host：可卸载 AssemblyLoadContext 加载、热重载、进程内编译
- [ ] 验收：在一个 native 引擎里跑 Nyxel 脚本（候选：EnginePlayground）
- [ ] 用户文档 host-integration.md：引擎怎么嵌入 Nyxel（加载、调度器、生命周期接口、热重载）
- 待定（启动时列出的问题）：支持哪些运行时（CoreCLR 之外的 Mono、NativeAOT，脚本能否 AOT 编译）；热重载的状态迁移、和引擎主循环的同步；和组件系统 / ECS 怎么配合；序列化；确定性；沙箱与安全；性能预算

## M5 工具链（草案）

- [ ] nyxelfmt 格式化器
- [ ] nyxel-lsp：诊断、补全、跳转、悬停
- [ ] 调试：VSCode 里断点、单步、看变量；热重载时的调试体验
- [ ] VSCode 扩展：TypeScript 客户端接 nyxel-lsp；常见脚本模式的代码片段（snippets）

## M6 发布（草案）

- [ ] NuGet 包、dotnet tool、VSCode Marketplace / Open VSX
- [ ] 发布流水线：打 tag 时发布 NuGet 包的 release.yml，CLI 作为 dotnet tool 发布。包只从 CI 打（`ContinuousIntegrationBuild` 去掉本机路径），版本号要高于 0.0.1（已被占位包用掉）
- [ ] 文档站（`nyxel-lang.dev`）：getting-started、语言参考对外版、host-integration

## 公开仓库前

名字、域名、包名的占位和品牌设想见 [brand.md](brand.md)。

- [x] CODE_OF_CONDUCT.md（Contributor Covenant 2.1 原文）、SECURITY.md，联系邮箱 nyxel@nyxel-lang.dev（Cloudflare Email Routing 转发到项目的 hotmail 邮箱）
- [ ] 商标使用政策；CLA 可选
- [ ] Nyxel 命名与发音 RFC
- [x] README 徽章：CI（GitHub Actions 自带）、License、.NET（shields.io 静态徽章）
- [x] 仓库描述和 Topics（GitHub 仓库主页 About 栏，用户自己填的）
- [x] 删掉 GitHub 仓库重建再推 main（2026-10-05）：旧仓库的 Activity 记着 force push，旧提交按哈希还能打开。重建后 Activity 只有一条 branch_creation，旧提交查不到；描述和 Topics 重新填了
- [x] 2026-10-05 转 Public；Settings → Advanced Security 打开了 Private vulnerability reporting（只有公开仓库才显示这一项）；main 加了 ruleset，禁止 force push 和删除
- [ ] README 快速开始、最小示例、官方发音音频
- [x] GitHub Discussions、Reported content（允许举报不当内容）已打开（2026-10-05）
- [ ] 其他社区渠道（Discord 等）
