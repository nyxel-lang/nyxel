# ADR-0001: 项目身份：名称、发音、文件后缀、许可证

- 状态：已接受
- 日期：2026-10-02

## 背景

启动前需要定下名字和各处的命名，后面的包名、命名空间、文件关联、编辑器扩展都依赖它。发音细节、联想与风险、商标 / 域名 / 包名的检索记录在 [brand.md](../brand.md)。

## 决策

- 语言名 **Nyxel**，中文名尼克塞尔。发音 /ˈnɪksəl/（NIX-əl，与 pixel 押韵），重音在首音节。
- 始终写全名。不用 Nyx、NXL、Nixel 作简称，正式品牌里也不用。
- 源文件 `.nyxel`，项目文件 `.nyxelproj`，MIME `text/x-nyxel`。
- 工具与包名：CLI `nyxel`，编译器 `nyxelc`，语言服务器 `nyxel-lsp`，格式化器 `nyxelfmt`；NuGet `Nyxel.Compiler` / `Nyxel.Runtime` / `Nyxel.Host` / `Nyxel.Sdk` / `Nyxel.Cli`；命名空间 `Nyxel.*`；VSCode 语言 id `nyxel`，TextMate scope `source.nyxel`；npm scope `@nyxel`；GitHub 组织优先 `nyxel-lang`。
- 开源，许可证 Apache-2.0，根目录放 LICENSE 和 NOTICE。
- 代码里用到这些名字时从 `Nyxel.Compiler.LanguageInfo` 取，不重复写字面量。

## 备选方案

- 后缀 `.nxl`：Hancom Nexcel、NextLabs SkyDRM、Sycon.net 许可证文件都在用，文件关联会冲突。
- 许可证 MIT：更短，但没有明确的专利授权条款。游戏引擎、图形、物理、网络同步是专利多发领域，Apache-2.0 的专利授权和终止条款对贡献者和使用者都更稳妥。

## 后果

- 正式商标检索（中国第 9 / 41 / 42 类，USPTO / EUIPO / WIPO）、域名注册、NuGet / npm / GitHub 占位要用户本人去做，记在 STATUS.md 的"悬而未决"里。
- `nyxelc` 是独立可执行文件还是 `nyxel build` 的别名，等有编译器时再定。
