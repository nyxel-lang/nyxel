# Nyxel Language for VSCode

Language support for [Nyxel](https://github.com/nyxel-lang/nyxel) (`.nyxel` files).

Current state: file association, `//` comment toggling, breakpoints allowed in `.nyxel` files, and highlighting for the parts of the syntax decided so far (comments, strings and `$"..."` interpolation, numbers, declaration keywords and modifiers, `new`, `self`, control keywords, built-in type names). The grammar grows with the language reference. The language server (nyxel-lsp) and debugging come later and will add a TypeScript client under `src/`.

## Trying it locally

The extension is declarative (no code to build). From the repository root, run:

```
tools\vscode-dev.cmd
```

This opens a new VSCode window (Extension Development Host) with the extension loaded from this folder and `samples/` open. Pass files or folders to open something else. After editing the grammar, run **Developer: Reload Window** in that window to pick up the change. **Developer: Inspect Editor Tokens and Scopes** shows the scope assigned to the token under the cursor.

To keep the extension active in every window instead, link the folder into the VSCode extensions directory and restart VSCode:

```powershell
New-Item -ItemType Junction -Path "$env:USERPROFILE\.vscode\extensions\nyxel-lang.nyxel-lang-0.0.1" -Target "$PWD\tools\vscode-nyxel"
```
