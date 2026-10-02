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
)
if "%PACK%"=="1" (
    "%DOTNET_EXE%" pack Nyxel.slnx -c %CONFIG% -v q -nologo --no-build -o build/packages || exit /b 1
)
echo [build] done
