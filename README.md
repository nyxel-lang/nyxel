# Nyxel

[![CI](https://github.com/nyxel-lang/nyxel/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/nyxel-lang/nyxel/actions/workflows/ci.yml)
[![License: Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)

> Nyxel is pronounced /ˈnɪksəl/ — **NIX-əl**, rhymes with “pixel”.

Nyxel is a game scripting language for .NET. It compiles to ordinary .NET assemblies that a native game engine loads into the CLR it hosts, and it talks to C# code directly. It is meant for the control layer of a game: abilities, AI, state machines, level logic, events, with hot reload.

Nyxel is designed first for **code that AI agents write and humans read**. Where a feature would make code faster to type but harder to read or easier to get subtly wrong, Nyxel picks the readable form.

**Status: pre-alpha.** The first draft of the language design is done: see the [language reference](docs/design/language-reference.md) and the [samples](samples/). The compiler is not written yet, so nothing here is usable yet.

- Name: always the full word *Nyxel* (not Nyx, NXL or Nixel)
- Source files: `.nyxel` · project files: `.nyxelproj` · MIME type: `text/x-nyxel`
- License: [Apache-2.0](LICENSE)

## Building

Requires the .NET 10 SDK.

```powershell
tools\build.cmd --test        # Windows: build + test
```

```sh
dotnet build Nyxel.slnx && dotnet test Nyxel.slnx
```

Build outputs go to `build/`.

## Repository layout

```
src/        Nyxel.Compiler, Nyxel.Runtime, Nyxel.Host, Nyxel.Sdk, Nyxel.Cli
tests/      test projects
samples/    example scripts in the designed syntax (not compilable yet)
tools/      build scripts and the VSCode extension (tools/vscode-nyxel)
docs/       decisions (ADRs), design docs, roadmap, status
```

Design decisions and their reasons are in [docs/decisions/](docs/decisions/); where the project is now is in [docs/STATUS.md](docs/STATUS.md).

## 中文简介

Nyxel，读作“尼克塞尔”，重音在前。

Nyxel 是一门 .NET 上的游戏脚本语言，编译成普通 .NET 程序集，由 native 游戏引擎在它宿主的 CLR 里加载，可直接与 C# 互操作，用于技能、AI、状态机、关卡控制、事件系统等控制层逻辑，支持热重载。设计上优先保证 **AI Agent 写得对、人读得懂**。

项目处于早期：语言设计的初稿已完成（见[语言参考](docs/design/language-reference.md)和 [samples/](samples/)），编译器还没开始写。设计决策见 [docs/decisions/](docs/decisions/)，当前进度见 [docs/STATUS.md](docs/STATUS.md)。

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Everyone taking part follows the [Code of Conduct](CODE_OF_CONDUCT.md); security issues are reported privately as described in [SECURITY.md](SECURITY.md).
