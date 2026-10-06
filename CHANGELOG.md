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
- Character literals, raw string literals (`"""`, `$"""`, `$$"""`, as in C# 11) and the array type `array<T>` (ADR-0020). A line may now end with any assignment operator, not only `=`.
- Tuples with named elements (`(Min: int, Max: int)`), deconstruction by position (`let (min, max) = ...`, `(a, b) = (b, a)`) and in loops (`for (key, value) in dict`, `for (i, item) in list.Index()`) (ADR-0021).
- Reverse and step through a range in `for` with `(0..<n).Reversed()` and `.StepBy(2)`; slices such as `name[0..<3]` and `name[1...]`; collection literals `[1, 2, 3]` typed by where they are used, as in C# 12; multidimensional arrays `array2d<T>` and `array3d<T>` (ADR-0022). A range is not a value: it stands only in `for`, `case` and slices.
- `x ??= value` as a statement (ADR-0023). Designed for the semantic checks to come: comparisons with `null` look only at the reference, never at an overloaded `==`, and `new array<T>(n)` is only for element types whose all-zero value is valid; others give each element with `new array<T>(n, func(i) = ...)`.
- Enums (ADR-0024): case values (all or none) and an underlying type, `enum Tile : byte`; `flags enum`, the only Nyxel enums that take `|` and `&`; `struct enum`, a value-type enum whose cases carry data. A case's data is named like tuple elements, `case Burn(Amount: int, Seconds: float)`, and `match` takes it out by position, `case Burn(_, seconds)`. Errors for C#'s `[Flags]` and `x is Damage.Burn(var a, var s)`; `x is Damage.Burn` itself already parses, and semantic analysis will check it.
