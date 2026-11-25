@echo off
rem Build this checkout and start the dev (Debug) Ochre host with its window open.
rem   run            quit a running dev host, rebuild everything, start
rem   run -NoBuild   start the last build
rem The dev host keeps its tasks in %APPDATA%\Ochre-dev and never touches a normal install.
rem No ( ) blocks: a ")" in an expanded path such as "Program Files (x86)" would end one.
setlocal
set "RUN_ROOT=%~dp0"
set "RUN_LOG=%TEMP%\ochre-run-build.log"
set "RUN_CLI=%RUN_ROOT%src\Ochre.Cli\bin\Debug\net10.0\ochre-cli.exe"
set "RUN_HOST=%RUN_ROOT%src\Ochre.Host\bin\Debug\net10.0-windows\ochre.exe"
if /i "%~1"=="-NoBuild" goto start
if not "%~1"=="" goto usage

echo Building the CLI ...
dotnet build "%RUN_ROOT%src\Ochre.Cli" -nologo -v q > "%RUN_LOG%" 2>&1 || goto failed

rem A running host locks its own exe, so the build below would fail to copy it.
"%RUN_CLI%" quit > nul 2>&1

echo Generating bridge types ...
dotnet run --project "%RUN_ROOT%tools\Ochre.BridgeGen" > "%RUN_LOG%" 2>&1 || goto failed

if exist "%RUN_ROOT%ui\node_modules\" goto ui_build
echo Installing UI packages ...
call pnpm --dir "%RUN_ROOT%ui" install --frozen-lockfile > "%RUN_LOG%" 2>&1 || goto failed

:ui_build
echo Building the UI ...
call pnpm --dir "%RUN_ROOT%ui" build > "%RUN_LOG%" 2>&1 || goto failed

echo Building the host ...
dotnet build "%RUN_ROOT%src\Ochre.Host" -nologo -v q > "%RUN_LOG%" 2>&1 || goto failed

:start
if not exist "%RUN_HOST%" goto missing
start "" "%RUN_HOST%"
echo Started. Use cli.cmd to talk to it, e.g. "cli status".
endlocal & exit /b 0

:usage
echo Usage: run.cmd [-NoBuild]
endlocal & exit /b 2

:missing
echo No build at %RUN_HOST%; run without -NoBuild.
endlocal & exit /b 1

:failed
type "%RUN_LOG%"
echo run: build failed
endlocal & exit /b 1
