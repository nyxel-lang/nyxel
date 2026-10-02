# CLAUDE.md

Nyxel：.NET 10 上的游戏脚本语言，编译成普通 .NET 程序集，由 native 引擎宿主的 CLR 加载。设计优先级见 ADR-0003：读者能正确理解 > Agent 能一次写对 > .NET 互操作 > 性能 > 手写省键。

## 和用户协作

- 用户对编程语言开发了解不多，并希望在过程中学到。涉及的决策和技术要详细说明：先讲清概念（不假设读过编译原理），再比较方案（附代码对比），给出推荐和理由。
- **语法和语义的每一项决定都要和用户讨论**，不要自己定了再告诉用户。一次讨论几项相关的，用代码样例对比。定了写 ADR（和用户讨论定的必写），写进 language-reference。
- 不涉及语言设计的工程细节（项目结构、测试、内部实现）照常自行决定，回复里说明。

## 每次开工的起点

1. 读 docs/STATUS.md（一屏以内）：现在在哪、下一步、悬而未决。
2. 对照 docs/roadmap.md 当前里程碑未勾选项，确认要做的事。
3. 动某个部分前读对应的 docs/design/*.md；改语言设计前读相关 ADR。
4. 只有需要查历史时才打开 docs/progress/M<n>.md，且只读当前里程碑那份。

收工前：

- 覆盖 docs/STATUS.md（不是追加），更新"下一步"。
- 在 docs/progress/M<n>.md 末尾追加一条日期日志：做了什么、坑。新里程碑开始时新建文件。
- 仍然有效的坑或约定提升到本文件或设计文档，日志里的内容不指望再被读。
- 勾选 docs/roadmap.md，跑 `tools\build.cmd --test` 确认全绿。

## 必读

- docs/README.md 文档索引
- docs/design/coding-conventions.md 编码规范、文档语言、提交信息
- docs/decisions/0003-design-goals.md 设计优先级和准则

## 硬性约定

- 改决定：新建 ADR（`pwsh tools/new-adr.ps1 <slug>`），不改已合进 master 的 ADR。dev 上还没合并的 ADR 不算定案，推翻时直接改原文件。新决定满足任一条也写 ADR，和实现同一轮提交：约束多个组件；认真比较过备选（尤其是和用户讨论定的）；推翻代价高。设计文档写"是什么"，ADR 写"为什么、比较过什么"，进度日志不承载理由。
- 警告即错误；`dotnet format Nyxel.slnx --verify-no-changes` 要过（CI 会查）。
- 构建产物统一在 build/（`UseArtifactsOutput`），不要在源码树留 bin/obj。
- Nyxel.Runtime 零第三方依赖、保持 AOT 兼容。
- NuGet 版本只写在 Directory.Packages.props。

## 构建与验证

- 一键：`cmd //c 'tools\build.cmd --test'`（Git Bash 里这样调）。`--release` 走 Release，`--pack` 输出 NuGet 包到 build/packages。
- dotnet 一律用 `"/c/Program Files/dotnet/dotnet.exe"` 全路径：PATH 上的 dotnet 可能是没有 SDK 的纯运行时。
- VSCode 扩展试用：`cmd //c 'tools\vscode-dev.cmd'`，开一个加载了 tools/vscode-nyxel 的新窗口并打开 samples/（会在用户屏幕上弹窗）。
- CLI 冒烟：`"/c/Program Files/dotnet/dotnet.exe" build/dotnet/bin/Nyxel.Cli/debug/nyxel.dll --version`。
- 提交（参照 EnginePlayground）：开发都在 `dev` 分支上。每轮改动结束就在 dev 上提交一次（`git add -A` + commit，标题 `wip: <这轮做了什么>`，不要求能编过），方便用户逐次看 diff；提交前看一眼 `git status`。master 只在用户说"整理合并"时动：
  1. 先按"收工前"清单更新文档、`tools\build.cmd --test` 全绿。
  2. master 若有新提交，dev 先 `git rebase master`。
  3. 在 dev 上 `git reset --soft master`，按仓库格式重新提交（`<area>: <summary>` + 正文 + 测试结果 + Co-Authored-By 行；消息用 Write 工具写到 build/COMMIT_MSG.txt 再 `git commit -F`，别走 Bash heredoc）。一次迭代含几个独立主题时改用 `git reset master`，按文件分组 add、分几条提交。
  4. `git switch master && git merge --ff-only dev && git switch dev`，master 保持线性。
  5. 回复里给出整理前 dev 的旧 tip hash（reflog 也能找回）。

  不 push（还没有远端），不用 `rebase -i`（工具不支持交互）。master 的根提交是一个空提交，方便第一次 `reset --soft master`。

## 工具坑

- Bash heredoc 里的反斜杠会被折叠（连 `<<'EOF'` 也一样），含 `\` 的内容用 Write / Edit 工具写。python 补丁里的 `tools\\vscode` 会变成 `\v`（垂直制表符），断言照样通过、悄悄写坏文件；文档里的路径能写正斜杠就写正斜杠。python 补丁用 `read_bytes` / `write_bytes`，`write_text` 在 Windows 上会把 LF 写成 CRLF。
- .cmd 必须是 CRLF（.gitattributes 已固定），Write 工具写出来是 LF：写完用 `sed -i 's/$/\r/' <file>` 转换，`file <file>` 确认。
- Bash 工具每次调用后工作目录会被重置回仓库根，`cd` 不跨调用保留。
- 本机 dotnet 和 Roslyn 的输出是中文（跟随 UI 语言）：`dotnet test` 的结果行是“已通过! - 失败: 0，通过: N”，grep 用“通过 / 失败”。代码里包装 Roslyn 诊断消息用 `GetMessage(CultureInfo.InvariantCulture)`。
- 生成 C# 时的 `#line` 规则（结束列包含、列偏移、绝对路径、嵌入源码用原始字节）见 docs/design/debug-mapping.md，不照做列号会错。
- 文本里出现“反斜杠 + u + 四位十六进制”这种 Unicode 转义时，生成内容时就会被解码成字符本身。文档里描述这类转义用文字，写完回读确认。

## 环境

Windows 10，Git Bash。.NET SDK 10.0.400（另有 8 / 9），node 24，pwsh 7。参考仓库 EnginePlayground（C++ 引擎 + C# 脚本经 hostfxr 嵌入，M4 宿主集成的候选验收目标）。
