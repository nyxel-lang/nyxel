@echo off
rem One-shot build of the whole solution.
rem Usage: tools\build.cmd [Debug|Release] [--release] [--test] [--pack]
rem   --test  also run every test project.
rem   --pack  also produce NuGet packages into build/packages.
setlocal enabledelayedexpansion
for %%i in ("%~dp0..") do set "ROOT=%%~fi"
rem The dotnet on PATH may be a runtime-only install without the SDK; use the SDK one explicitly.
set "DOTNET_EXE=C:\Program Files\dotnet\dotnet.exe"
if not exist "%DOTNET_EXE%" set "DOTNET_EXE=dotnet"
set CONFIG=Debug
set TEST=0
set PACK=0

:args
if "%~1"=="" goto run
if /i "%~1"=="Debug" set CONFIG=Debug
if /i "%~1"=="Release" set CONFIG=Release
if /i "%~1"=="--release" set CONFIG=Release
if /i "%~1"=="--test" set TEST=1
if /i "%~1"=="--pack" set PACK=1
shift
goto args

:run
cd /d "%ROOT%"
"%DOTNET_EXE%" build Nyxel.slnx -c %CONFIG% -v q -nologo || exit /b 1
if "%TEST%"=="1" (
    "%DOTNET_EXE%" test Nyxel.slnx -c %CONFIG% -v q -nologo --no-build || exit /b 1
    call :grammar || exit /b 1
)
if "%PACK%"=="1" (
    "%DOTNET_EXE%" pack Nyxel.slnx -c %CONFIG% -v q -nologo --no-build -o build/packages || exit /b 1
)
echo [build] done
exit /b 0

:grammar
rem Highlighting snapshots (tools/vscode-nyxel/test). They need node; without npm they are skipped with a note.
where npm >nul 2>nul || (
    echo [build] npm not found: skipping the highlighting snapshot test
    exit /b 0
)
pushd "%ROOT%\tools\vscode-nyxel"
if not exist node_modules (
    call npm ci --no-audit --no-fund || (popd & exit /b 1)
)
call npm test --silent || (popd & exit /b 1)
popd
exit /b 0
