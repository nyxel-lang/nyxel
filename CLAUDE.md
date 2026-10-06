# CLAUDE.md

Nyxel：.NET 10 上的游戏脚本语言，编译成普通 .NET 程序集，由 native 引擎宿主的 CLR 加载。设计优先级见 ADR-0003：读者能正确理解 > Agent 能一次写对 > .NET 互操作 > 性能 > 手写省键。

## 协作

- 语法和语义的决定不由 Agent 自行拍板：先和人讨论，一次讨论几项相关的，用代码样例对比方案；定了写 ADR，写进 language-reference。
- 不涉及语言设计的工程细节（项目结构、测试、内部实现）照常自行决定，回复里说明。
- 维护者本人的协作偏好写在 CLAUDE.local.md（不进仓库，没有就跳过）。

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

- 改决定：新建 ADR（`pwsh tools/new-adr.ps1 <slug>`），不改已合进 main 的 ADR。dev 上还没合并的 ADR 不算定案，推翻时直接改原文件。新决定满足任一条也写 ADR，和实现同一轮提交：约束多个组件；认真比较过备选（尤其是和用户讨论定的）；推翻代价高。设计文档写"是什么"，ADR 写"为什么、比较过什么"，进度日志不承载理由。
- 警告即错误；`dotnet format Nyxel.slnx --verify-no-changes` 要过（CI 会查）。
- 构建产物统一在 build/（`UseArtifactsOutput`），不要在源码树留 bin/obj。
- Nyxel.Runtime 零第三方依赖、保持 AOT 兼容。
- NuGet 版本只写在 Directory.Packages.props。

## 构建与验证

- 一键：`cmd //c 'tools\build.cmd --test'`（Git Bash 里这样调）。`--release` 走 Release，`--pack` 输出 NuGet 包到 build/packages。
- dotnet 一律用 `"/c/Program Files/dotnet/dotnet.exe"` 全路径：PATH 上的 dotnet 可能是没有 SDK 的纯运行时。
- VSCode 扩展试用：`cmd //c 'tools\vscode-dev.cmd'`，开一个加载了 tools/vscode-nyxel 的新窗口并打开 samples/（会在用户屏幕上弹窗）。
- CLI 冒烟：`"/c/Program Files/dotnet/dotnet.exe" build/dotnet/bin/Nyxel.Cli/debug/nyxel.dll --version`；检查语法：同一个 dll 加 `parse [--tree] samples/*/*.nyxel`。
- 语法树快照（tests/Nyxel.Compiler.Tests/Syntax/Snapshots）：树的形状有意改变后，`NYXEL_UPDATE_SNAPSHOTS=1` 跑一次测试重新生成，看 diff 再提交。
- 提交（参照 EnginePlayground）：开发都在 `dev` 分支上。每轮改动结束就在 dev 上提交一次（`git add -A` + commit，标题 `wip: <这轮做了什么>`，不要求能编过），方便用户逐次看 diff；提交前看一眼 `git status`。main 只在用户说"整理合并"时动：
  1. 先按"收工前"清单更新文档、`tools\build.cmd --test` 全绿。
  2. `git fetch origin`；origin/main 有本地没有的提交（比如在网页上改过）就先 `git switch main && git merge --ff-only origin/main`。main 若有新提交，dev 先 `git rebase main`。
  3. 给 dev 的旧 tip 打本地标签留住 wip 提交：`git tag -a wip/<日期> -m "<压进了 main 的哪几条>"`，同一天第二次起加 `-2`、`-3`。
  4. 在 dev 上 `git reset --soft main`，按仓库格式重新提交（`<area>: <summary>` + 正文 + 测试结果 + Co-Authored-By 行；消息用 Write 工具写到 build/COMMIT_MSG.txt 再 `git commit -F`，别走 Bash heredoc）。一次迭代含几个独立主题时改用 `git reset main`，按文件分组 add、分几条提交；几个主题改的是同一批文件时，可以按某个 wip 提交的状态切开：`git commit-tree <wip>^{tree} -p main -F 消息` 逐条接上，最后 `git reset --hard` 到最后一条（树和 dev 原来的 tip 相同）。切开的每个状态先在 `git worktree` 里跑一遍 `tools\build.cmd --test`。
  5. `git switch main && git merge --ff-only dev && git switch dev`，main 保持线性。
  6. `git push origin main`，推完 CI 会在 GitHub Actions 上跑。
  7. 回复里给出整理前 dev 的旧 tip hash 和 wip 标签名。

  远端：`origin` = https://github.com/nyxel-lang/nyxel（公开仓库），只推 `main`，dev 和 `wip/*` 标签都不推（`git push origin main` 默认不带标签，别加 `--tags`）。已推送的历史不重写、不 force push（main 上有 ruleset 禁止 force push 和删除）。不用 `rebase -i`（工具不支持交互）。main 的根提交是一个空提交，方便第一次 `reset --soft main`。

## 工具坑

- Bash heredoc 里的反斜杠会被折叠（连 `<<'EOF'` 也一样），含 `\` 的内容用 Write / Edit 工具写；heredoc 里有不成对的单引号（`C#'s`）时整条命令解析失败。python 补丁脚本用 Write 写成文件再运行。python 补丁里的 `tools\\vscode` 会变成 `\v`（垂直制表符），断言照样通过、悄悄写坏文件；文档里的路径能写正斜杠就写正斜杠。python 补丁用 `read_bytes` / `write_bytes`，`write_text` 在 Windows 上会把 LF 写成 CRLF。
- .cmd 必须是 CRLF（.gitattributes 已固定），Write 工具写出来是 LF：写完用 `sed -i 's/$/\r/' <file>` 转换，`file <file>` 确认。
- Bash 工具里的 `cd` 会保留到后面的调用（工作目录跟着变）：要在子目录里跑命令就用 `(cd dir && ...)` 子 shell，或者用绝对路径。
- 临时探针用 .NET 10 的单文件程序（`dotnet run probe.cs`，`#:package` / `#:project` 引用依赖，放 build/tmp）。同一目录放两个 .cs 时可能跑成另一个文件，每个探针放单独目录。
- 和 C# 相同的语法（字面量、转义等）不凭记忆实现：用 Roslyn（测试项目可以直接引用）做对照测试，见 CSharpLiteralTests。
- 本机 dotnet 和 Roslyn 的输出是中文（跟随 UI 语言）：`dotnet test` 的结果行是“已通过! - 失败: 0，通过: N”，grep 用“通过 / 失败”。代码里包装 Roslyn 诊断消息用 `GetMessage(CultureInfo.InvariantCulture)`。
- 生成 C# 时的 `#line` 规则（结束列包含、列偏移、绝对路径、嵌入源码用原始字节）见 docs/design/debug-mapping.md，不照做列号会错。
- 文本里出现“反斜杠 + u + 四位十六进制”这种 Unicode 转义时，生成内容时就会被解码成字符本身。文档里描述这类转义用文字，写完回读确认。C# 代码里要这类字符时写成 `(char)0xFEFF`，不写字符字面量的转义（Write 工具写 Lexer 时真的把 BOM 写进了源码）。

## 环境

本机环境（系统、SDK 版本、参考仓库的位置）写在 CLAUDE.local.md，不进仓库；公开文件里不写本机绝对路径和个人信息。参考仓库 EnginePlayground：C++ 引擎 + C# 脚本经 hostfxr 嵌入，M4 宿主集成的候选验收目标。
