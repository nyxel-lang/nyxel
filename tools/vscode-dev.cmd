@echo off
rem Opens a new VSCode window with the in-repo Nyxel extension loaded (Extension Development Host).
rem Usage: tools\vscode-dev.cmd [files or folders...] [other code options...]
rem   With no arguments it opens samples/.
rem   The extension is read straight from tools/vscode-nyxel; nothing is installed.
rem   After editing the grammar, run "Developer: Reload Window" (Ctrl+R) in that window to pick it up.
setlocal
for %%i in ("%~dp0..") do set "ROOT=%%~fi"
set "CODE_CMD="
for /f "delims=" %%p in ('where code.cmd 2^>nul') do if not defined CODE_CMD set "CODE_CMD=%%p"
if not defined CODE_CMD if exist "%LOCALAPPDATA%\Programs\Microsoft VS Code\bin\code.cmd" set "CODE_CMD=%LOCALAPPDATA%\Programs\Microsoft VS Code\bin\code.cmd"
if not defined CODE_CMD if exist "%ProgramFiles%\Microsoft VS Code\bin\code.cmd" set "CODE_CMD=%ProgramFiles%\Microsoft VS Code\bin\code.cmd"
if not defined CODE_CMD (
    echo [vscode-dev] VSCode command line 'code' not found. Install VSCode or add its bin folder to PATH. 1>&2
    exit /b 1
)
if "%~1"=="" (
    call "%CODE_CMD%" --extensionDevelopmentPath="%ROOT%\tools\vscode-nyxel" "%ROOT%\samples"
) else (
    call "%CODE_CMD%" --extensionDevelopmentPath="%ROOT%\tools\vscode-nyxel" %*
)
