# 名字、品牌与检索记录

名字、后缀、许可证的决定见 [ADR-0001](decisions/0001-project-identity.md)，本文记录决定背后的发音细节、联想与风险、检索结果，以及对外发布时的品牌设想。内容来自项目启动说明（2026-10-02）。

## 发音

- 国际音标 /ˈnɪksəl/，英文近似 NIX-əl，重音在首音节；中文音译“尼克塞尔”。
- 记忆方法：与 pixel 押韵，把 p 换成 n。
- 官方说明（README 里用的就是这两句）：
  - Nyxel is pronounced /ˈnɪksəl/ — NIX-əl, rhymes with “pixel”.
  - Nyxel：读作“尼克塞尔”，重音在前。
- 不采用的读法：/ˈnaɪksəl/（NYX-əl）；/ˈnɪkəl/（像 nickel）；nik-SELL（重音后置）。

## 联想与风险

正面联想：

- Nyx：希腊神话的夜之女神，黑夜、神秘、潜行、控制。
- Pixel：像素，游戏与图形的技术感；Texel / Voxel（纹素、体素）同样指向引擎和图形管线。
- Nexus：连接、核心、枢纽。
- 夜、暗影、符文：适合技能、AI、状态机、控制脚本这类定位。

风险联想：

- nix：英语俚语里是“无、取消、拒绝、禁止”。
- nickel：镍、硬币，发音接近。
- Nyx Professional Makeup（化妆品牌）、Dota 2 的 Nyx Assassin（游戏圈会直接想到）。
- Nixel：Lego Mixels 里的反派角色。
- NXL：National Xball League 的缩写。
- Nyxell（双 L）：西班牙的 Nyxell App / Nyxellapp S.L.。

策略：始终写全名 Nyxel，不简称 Nyx，正式品牌里不用 NXL，文档里写明发音和拼写。

## 检索记录（2026-10-02）

> 检索时的记录，不构成法律意见。正式使用前要做正式商标检索。

**商标**：中国商标网有豪威科技（OMNIVISION）申请的“夜鹰 NYXEL”，第 9 类（数据处理设备、摄像机、集成电路、半导体等），2018 年 9 月收到驳回通知，驳回复审后未获注册，状态为商标无效。要做的：中国商标网检索第 9 / 41 / 42 类，USPTO、EUIPO、WIPO 检索，关注软件、游戏、编程语言相关类目；预算允许时委托商标代理出正式检索报告。

**域名**：`nyxell.com`（双 L）被 Nyxell App 使用，和 Nyxel 不是同一个词。要查询可用性：`nyxel.dev`（优先）、`nyxel.io`、`nyxel.com`、`nyxel.net`、`nyxel.org`、`nyxel.cn`、`nyxel.run`；备选 `nyxel-lang.dev`、`nyxel.engineer`。

**NuGet**：没有名为 Nyxel 的包。有一个 Nixcel（Excel 导入导出），无关。

**GitHub**：没有 Nyxel 相关的组织或仓库被占用。

**npm**：`nyxel`、`@nyxel` 都没有被占用。

**VSCode Marketplace / Open VSX**：没有名为 Nyxel Language 或类似名字的扩展。

## 要占位的名字

都要用户本人去做，进度记在 [STATUS.md](STATUS.md) 的“悬而未决”。

- GitHub 组织 `nyxel-lang`（备选 `nyxeldev`）。现在是单仓库 `nyxel`；启动时设想过拆成 `nyxel-compiler`、`nyxel-runtime`、`nyxel-vscode`，暂时不拆。
- NuGet：`Nyxel`，以及 `Nyxel.Compiler`、`Nyxel.Runtime`、`Nyxel.Host`、`Nyxel.Sdk`、`Nyxel.Cli`。
- npm scope `@nyxel`：给 JavaScript / TypeScript 工具链、语法高亮、CLI 包装用。
- VSCode Marketplace 的 publisher（现在暂填 `nyxel-lang`），扩展名 Nyxel Language；同时在 Open VSX 注册。
- 域名，优先 `nyxel.dev`。

## 品牌与社区设想

- Logo 元素：夜、像素、符文、螺旋、核心。吉祥物候选：夜鸦、黑猫、暗影精灵。
- 文档站放在 `nyxel.dev`。
- 社区渠道：Discord、GitHub Discussions、Reddit r/nyxel、X、Bilibili。
- 公开前发布一篇“Nyxel 命名与发音 RFC”，录一段官方发音音频放进 README。
- 需要商标使用政策；CLA 可选。
