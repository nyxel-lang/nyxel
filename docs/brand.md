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

**域名**：`nyxell.com`（双 L）被 Nyxell App 使用，和 Nyxel 不是同一个词。要查询可用性：`nyxel.dev`（优先）、`nyxel.io`、`nyxel.com`、`nyxel.net`、`nyxel.org`、`nyxel.cn`、`nyxel.run`；备选 `nyxel-lang.dev`、`nyxel.engineer`。2026-10-05：`nyxel.dev` 已被别人注册，用户在 Cloudflare 注册了 `nyxel-lang.dev`。

**NuGet**：没有名为 Nyxel 的包。有一个 Nixcel（Excel 导入导出），无关。

**GitHub**：没有 Nyxel 相关的组织或仓库被占用。

**npm**：`nyxel`、`@nyxel` 都没有被占用。2026-10-05 复查：包名 `nyxel` 仍没人发布；但 registry 的 `/-/org/<名字>/package` 对 `nyxel`、`nyxel-lang`、`nyxel-dev` 返回空列表（200），对不存在的名字返回 “Scope not found”（404），说明这三个名字都已注册（还没有公开包）。上次只查了包，没查 scope。其中 `nyxel-lang` 是用户当天注册的，`nyxel` 和 `nyxel-dev` 是别人的。

**VSCode Marketplace / Open VSX**：没有名为 Nyxel Language 或类似名字的扩展。

## 要占位的名字

都要用户本人去做，进度记在 [STATUS.md](STATUS.md) 的“悬而未决”。

- GitHub 组织 `nyxel-lang`：已创建（2026-10-05），仓库 `nyxel-lang/nyxel` 已公开。现在是单仓库；启动时设想过拆成 `nyxel-compiler`、`nyxel-runtime`、`nyxel-vscode`，暂时不拆。
- NuGet：`Nyxel`，以及 `Nyxel.Compiler`、`Nyxel.Runtime`、`Nyxel.Host`、`Nyxel.Sdk`、`Nyxel.Cli`。2026-10-05 用户在 nuget.org 账号 `nyxel-lang` 下传了后五个的 0.0.1（本地 Release 构建的 M0 骨架）并设为 unlisted：ID 从上传起就归这个账号，unlist 只是不出现在搜索里，指定版本仍可下载；nuget.org 不能删包、版本号不能重用，所以 0.0.1 已经用掉。这几个包的 DLL / PDB 里带着本机构建路径（没有用户名），删不掉，不再处理；之后的包只从 CI 打。`Nyxel` 本身没有项目，另打了一个不含代码的 0.0.1 占位包（只有元数据和 README），用户同日上传并 unlist；六个 ID 都已在 nuget.org 的公开接口上查到 0.0.1。`Nyxel`将来是 dotnet tool 还是元包到时候再定。可选：发邮件给 account@nuget.org 申请 ID 前缀保留 `Nyxel.*`，审核通过后别人不能再传这个前缀的包，包旁边显示认证标记。
- npm scope `@nyxel-lang`：给 JavaScript / TypeScript 工具链、语法高亮、CLI 包装用。scope 就是 npm 的用户名或组织名。原计划的 `@nyxel` 已被别人注册，2026-10-05 用户注册了 npm 用户 `nyxel-lang`，scope 改用 `@nyxel-lang`，和 GitHub 组织、NuGet 账号、域名一致（[ADR-0018](decisions/0018-npm-scope.md)）。以后要多人管理时，npm 支持把用户账号转成组织。npm 的名称争议政策明确禁止“只为将来使用而注册用户名、组织名或发布包”，违反的可能被改名或删除，所以不发空的占位包，等有真东西（比如给其他编辑器用的语法包）再发。
- VSCode Marketplace 的 publisher `nyxel-lang`：2026-10-05 用户已注册，扩展名 Nyxel Language。
- Open VSX 的 namespace `nyxel-lang`（和 Marketplace 的 publisher 同名，扩展 package.json 的 `publisher` 就是它）：2026-10-06 用户已建好（API 显示 `verified: false`，还没认领 owner）。占 namespace 不用传扩展。步骤是注册 Eclipse 账号（填 GitHub 用户名）、用 GitHub 登录 open-vsx.org 并在 Profile 里关联 Eclipse 账号、签 Publisher Agreement、生成 access token，再运行 `npx ovsx create-namespace nyxel-lang -p <token>`。建了之后只有 namespace 成员能往里发（2020 年起 namespace 不再公开）。创建者只是 contributor，扩展会显示“未验证”；要成为 owner（扩展带验证标记、能管理成员）得到 github.com/EclipseFdn/open-vsx.org 公开提 issue 认领，可以等第一次发布时再做。
- 域名：`nyxel-lang.dev` 已注册（2026-10-05，Cloudflare），和 GitHub 组织名一致，已在 GitHub 组织上验证；`nyxel.dev` 已被别人注册。联系邮箱 `nyxel@nyxel-lang.dev` 用 Cloudflare Email Routing 转发到项目的 hotmail 邮箱，只收不发（回信从 hotmail 发）。文档站做出来前，域名先 302 跳转到 GitHub 组织主页（Cloudflare Redirect Rule + 代理的占位记录 A `192.0.2.1` / AAAA `100::`），仓库 About 的 Website 填的是它。

## 品牌与社区设想

- Logo 元素：夜、像素、符文、螺旋、核心。吉祥物候选：夜鸦、黑猫、暗影精灵。
- 文档站放在 `nyxel-lang.dev`。
- 社区渠道：Discord、GitHub Discussions、Reddit r/nyxel、X、Bilibili。
- 公开前发布一篇“Nyxel 命名与发音 RFC”，录一段官方发音音频放进 README。
- 需要商标使用政策；CLA 可选。
