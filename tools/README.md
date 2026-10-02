# tools/

开发脚本，从仓库根目录运行。

| 脚本 | 用途 |
|---|---|
| `build.cmd` | 一键构建 Nyxel.slnx：`[Debug\|Release] [--release] [--test] [--pack]`。`--test` 跑全部测试，`--pack` 把 NuGet 包输出到 build/packages。用 `C:\Program Files\dotnet\dotnet.exe` 全路径（PATH 上可能是没有 SDK 的纯运行时）。Git Bash 里调用：`cmd //c 'tools\build.cmd --test'` |
| `vscode-dev.cmd` | 打开一个加载了仓库内 VSCode 扩展的新窗口（Extension Development Host），默认打开 samples/，也可以传文件或文件夹。扩展直接从 tools/vscode-nyxel 读取，不安装。改了语法文件后，在那个窗口里运行 "Developer: Reload Window"（Ctrl+R）生效。双击即可；Git Bash 里：`cmd //c 'tools\vscode-dev.cmd'` |
| `new-adr.ps1` | 从 docs/decisions/template.md 创建下一个编号的 ADR：`pwsh tools/new-adr.ps1 <slug>` |
| `vscode-nyxel/` | VSCode 扩展，见其中的 README |
