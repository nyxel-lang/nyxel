# ADR-0002: .NET 10、仓库布局与构建方式

- 状态：已接受
- 日期：2026-10-02

## 背景

启动文档草案里 CI 用的是 .NET 9，目录约定前后不一致（文字说测试、示例放在 src/ 下，目录树里却在根目录）。开发流程参考 EnginePlayground。

## 决策

- **只面向 .NET 10**（`net10.0`），`global.json` 固定 10.0.100 + `latestFeature` 前滚。
- **顶层目录**：`src/`（产品代码）、`tests/`、`samples/`、`tools/`、`docs/` 并列放在根目录；根目录只放配置和面向用户的文档。
- **解决方案**用 `Nyxel.slnx`（XML 格式），不用 `.sln`。
- **公共 MSBuild 设置**在 `Directory.Build.props`：Nullable、警告即错误、构建时检查代码风格、确定性构建；`UseArtifactsOutput` 把所有产物放到 `build/dotnet/`，源码树里不出现 bin/obj。
- **NuGet 版本集中管理**：版本号只写在 `Directory.Packages.props`，csproj 里只写包名。
- **测试**用 xUnit 2.9.3（和 EnginePlayground 同版本，本机已缓存）。
- **一键构建**：`tools\build.cmd [--release] [--test] [--pack]`。CI（GitHub Actions）在 ubuntu 和 windows 上跑 restore → format 检查 → build → test → pack。
- **.nyxelproj** 走 MSBuild 项目 SDK：`<Project Sdk="Nyxel.Sdk">`，Nyxel.Sdk 包只含 `Sdk/Sdk.props` 和 `Sdk/Sdk.targets`，在 .NET SDK 之上加 Nyxel 编译目标（和 F# 的 .fsproj 同一思路）。
- **开发流程**与 EnginePlayground 一致：dev 分支每轮一个 wip 提交，用户说"整理合并"时整理进 master（CLAUDE.md "提交"）。

## 备选方案

- .NET 9：短期支持版本（STS），支持到 2026-11-10，一个多月后就到期。.NET 10 是长期支持版本（LTS，2025-11 发布，支持到 2028-11）。
- 同时编译 netstandard2.1 以兼容 Unity（Mono / IL2CPP）：Unity 的 C# 版本和运行时能力落后，会把运行时库限制在老 API 上。项目定位是由 native 引擎宿主的 CoreCLR，不做。
- `.sln`：格式里全是 GUID 和配置矩阵，人和 Agent 都难读难改。`.slnx` 从 .NET SDK 9.0.200、VS 17.13、Rider 2024.3 起支持，.NET 10 的 `dotnet new sln` 默认就是它。
- 自定义 `BaseOutputPath`（EnginePlayground 的做法）：要分别设 bin 和 obj，`UseArtifactsOutput` 是 .NET 8 起官方给的一站式写法。

## 后果

- 使用者必须装 .NET 10 SDK / 运行时。
- 没有用 .slnx 的老 IDE 打不开解决方案，可以直接打开单个 csproj。
- CI 有格式检查，提交前要跑 `dotnet format`。
