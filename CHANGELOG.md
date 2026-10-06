# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Repository skeleton: solution, projects, build script, CI.
- `nyxel --version` and `nyxel --help`.
- First draft of the language design: decision records ADR-0005 to ADR-0017, the language reference and sample scripts.
- VSCode extension: `.nyxel` file association and syntax highlighting for the designed syntax.
- Lexer, parser and a lossless syntax tree for the whole designed syntax, with diagnostics that carry stable `NYX` error codes and suggest the Nyxel spelling for common C# habits (ADR-0019).
- `nyxel parse [--tree] <file>...` checks the syntax of `.nyxel` files and can print the syntax tree.
