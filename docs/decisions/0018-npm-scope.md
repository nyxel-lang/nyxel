# ADR-0018: npm scope 用 `@nyxel-lang`

- 状态：已接受
- 日期：2026-10-05

## 背景

ADR-0001 定的 npm scope 是 `@nyxel`。npm 的 scope 就是用户名或组织名，谁注册了这个名字，`@名字/...` 下的包就只有谁能发。2026-10-05 查 registry：`nyxel` 和 `nyxel-dev` 已被别人注册，都还没有公开包。npm 的名称争议政策禁止只为将来使用而注册用户名、组织名或发布包，所以不能见到近似的名字就先注册下来占着。

## 决策

npm scope 改用 `@nyxel-lang`，用户已在 2026-10-05 注册了 npm 用户 `nyxel-lang`。本 ADR 取代 ADR-0001 里“npm scope `@nyxel`”这一项，ADR-0001 的其他内容不变。

## 备选方案

- 按争议政策向 npm 申请转让 `@nyxel`：对方账号没有包，申请有理由，但结果不确定、要等，而且现在还没有要发的 npm 包。以后真想要 `@nyxel` 还可以再申请，不影响现在的选择。
- `@nyxellang`：没人注册，但和 GitHub 组织 `nyxel-lang`、域名 `nyxel-lang.dev` 拼法不同，多一种写法要记。
- 不用 scope，直接用 `nyxel`、`nyxel-grammar` 这样的包名：每个名字都要单独去抢，也看不出这些包出自同一个发布者。

## 后果

- GitHub 组织、NuGet 账号、npm、域名统一是 `nyxel-lang`，VSCode publisher 暂填的也是它。
- 包名会比 `@nyxel/...` 长，比如 `@nyxel-lang/grammar`。
- 现在是用户账号；以后要多人管理时，npm 支持把用户账号转成组织。
- 不发空的占位包，等有真东西再发（见 [brand.md](../brand.md)）。
