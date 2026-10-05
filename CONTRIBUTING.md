# Contributing to Nyxel

Nyxel is at an early design stage. The most useful contributions right now are discussions about the language design, in issues.

Everyone taking part follows the [Code of Conduct](CODE_OF_CONDUCT.md). Do not report security vulnerabilities in public issues; see [SECURITY.md](SECURITY.md).

## Ground rules

- **Readability over brevity.** Nyxel optimizes for code that agents write and humans read ([ADR-0003](docs/decisions/0003-design-goals.md)). Language proposals should show before / after code and explain why a reader would understand the new form.
- **Decisions are recorded.** Anything that constrains several components, was chosen between real alternatives, or would be expensive to undo gets an ADR in [docs/decisions/](docs/decisions/) (`tools/new-adr.ps1 <slug>`). Merged ADRs are not edited; a new ADR supersedes them.
- **Design docs describe the current state.** [docs/design/](docs/design/) is updated together with the code.

## Building and testing

Requires the .NET 10 SDK.

```powershell
tools\build.cmd --test
dotnet format Nyxel.slnx --verify-no-changes
```

Warnings are errors. Build outputs go to `build/`; nothing should appear under `src/` or `tests/`.

## Commits

- Message: `<area>: <summary>` in the imperative, e.g. `compiler: parse match expressions`. The body says what changed and why, and how it was tested.
- One topic per commit.

## License

By contributing you agree that your contributions are licensed under the [Apache License 2.0](LICENSE).
