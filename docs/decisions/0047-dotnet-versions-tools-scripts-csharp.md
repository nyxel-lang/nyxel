# ADR-0047: .NET 版本分两处定：工具链和脚本的最低要求都是 .NET 10，生成的 C# 用 C# 15

- 状态：已接受
- 日期：2026-11-12

## 背景

第三十九轮讨论里用户问：要把项目从 .NET 10 升到 .NET 11（2026-11-10 发布），需要改什么。Nyxel 是独立的项目，不一定集成进 EnginePlayground，也可能是别的引擎框架，环境要求可以自己定；EnginePlayground 以后也可能升级 .NET。

ADR-0002 只写了“只面向 .NET 10”，实际上是四件事写在了一起：

| | 现在在哪里定 | 影响谁 |
|---|---|---|
| 构建 Nyxel 本身用的 SDK | `global.json`：10.0.100 | 开发 Nyxel 的人、CI |
| Nyxel 的工具跑在哪个 .NET 上（编译器、CLI、测试） | `Directory.Build.props`：`net10.0` | 装 Nyxel 的人 |
| 编出的脚本、Nyxel.Runtime、Nyxel.Host 面向哪个 .NET | 同一行；CLI 编译时拿自己运行的 .NET 的程序集当引用 | 引擎（宿主） |
| 生成的 C# 用哪个语言版本 | Roslyn 包的版本 | 只影响编译器能生成什么 |

查到的事实：

- 新的运行时能加载面向旧版本编的程序集，反过来不行：.NET 10 的宿主加载面向 .NET 11 的程序集会失败（找不到 `System.Runtime` 11.0.0.0）。EnginePlayground 的 runtimeconfig 是 `net10.0` 加 `rollForward: LatestMinor`，只在 10.x 里挑，不会换到 11。
- 支持周期（微软的发布数据）：.NET 10 是 LTS，.NET 11 是 STS，**都支持到 2028-11-14**。.NET 12 是下一个 LTS，按惯例 2027-11 发布。
- C# 的语言版本由 Roslyn 决定，和运行时无关。C# 15 的 union 和 closed 类只需要三个特性类型，在生成的程序集里自己声明就能在 .NET 10 上用（ADR-0046 的探针）。
- 2026-11-12 nuget.org 上的 Roslyn 最新是 5.9.0，只把 C# 15 的功能当预览版；正式支持 C# 15 的包还没发布（微软的每日构建源上有 5.10 到 5.14 的预览构建）。Nyxel.Compiler 要发 NuGet 包，正式版的包依赖预览版的包会报 NU5104（警告即错误）。

按推荐定了，用户没有异议。

## 决策

1. **工具链留在 .NET 10**：`global.json`、`Directory.Build.props` 不动。下一次正式评估放在 .NET 12（LTS，2027-11）；工具链升级只影响开发和使用 Nyxel 的人，可以在决策 2 落实以后单独做。
2. **脚本的最低要求是 .NET 10**：编出的脚本程序集、Nyxel.Runtime、Nyxel.Host 都面向 .NET 10，宿主是 .NET 10 或更新的都能加载。
   - 以后由项目自己选面向的版本（`.nyxelproj` 的 `TargetFramework`，M4）：编译时引用那个版本的引用程序集，不再拿编译器自己运行的 .NET（现在的 `BuildCommands.References`）。这样工具链和脚本的版本互不牵连。
   - Nyxel.Runtime / Nyxel.Host 真有新版本才有的 API 值得用时，同时编 `net10.0` 和新版本两份。
   - 引擎升级 .NET（比如 EnginePlayground 升到 11）不需要 Nyxel 配合。
3. **生成的 C# 用 C# 15**，和面向哪个 .NET 无关：
   - 面向的 .NET 里没有的特性类型（`System.Runtime.CompilerServices.UnionAttribute`、`IUnion`、`IsClosedTypeAttribute`），编译器在生成的程序集里声明成 `internal`；已经有的不声明。
   - Roslyn 正式支持 C# 15 的包上了 nuget.org 就升级，改用 `LanguageVersion.CSharp15`。在那之前用 Roslyn 5.9 的 `LanguageVersion.Preview`（Roslyn 包本身还是正式版，不影响发包）。

## 备选方案

- **全部升到 .NET 11**：要装 .NET 11 SDK、改 `global.json` 和 `Directory.Build.props`、修新 SDK 带来的分析规则警告，编出的脚本也随之要求 .NET 11，EnginePlayground 这样的 .NET 10 宿主就加载不了。换来的只有 C# 15 写编译器本身、运行时的性能改进；支持期限和 .NET 10 是同一天，不多。
- **只升 SDK（`global.json` 换成 11），项目仍面向 `net10.0`**：编译器自己的代码能用 C# 15，但开发 Nyxel 的人都要装 .NET 11 SDK，收益小。
- **生成的 C# 等 Roslyn 正式支持 C# 15 再用新功能**：带数据的 enum（ADR-0046）要等不确定的时间。Preview 模式在 Roslyn 5.9 里已经能编出能运行的程序集。

## 后果

- 修改了 ADR-0002 的“只面向 .NET 10”：分成工具链和脚本两处，原文已改成最新，见它的修订记录。
- 现在 CLI 编译时拿自己运行的 .NET 当引用（`BuildCommands.References`），工具链和脚本的版本还绑在一起；决策 2 的“由项目选”随 `.nyxelproj` 做（roadmap M4）。在那之前工具链不升级。
- Roslyn 5.9 的 Preview 和 C# 15 正式版若有出入（特性的名字、union 的规则），升级 Roslyn 时一起改。STATUS 的“下一步”记着升级 Roslyn。
- 引擎那边用 C# 14 的项目也能用 Nyxel 编出的程序集，只是没有 union 的写法和 closed 的穷尽检查（ADR-0046）。
