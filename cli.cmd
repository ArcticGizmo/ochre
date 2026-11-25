@echo off
rem Build this checkout's CLI and run it, passing every argument through:
rem   cli status
rem   cli open
rem   cli dev eval "document.title"
rem It talks to the dev (Debug) Ochre host for this user. `cli open` starts that host from this checkout's build
rem if it isn't running; run.cmd builds it.
setlocal
set "CLI_ROOT=%~dp0"
rem The build log is only shown when the build fails, so a working build doesn't bury Ochre's own output.
set "CLI_LOG=%TEMP%\ochre-cli-build.log"
dotnet build "%CLI_ROOT%src\Ochre.Cli\Ochre.Cli.csproj" -nologo -v q > "%CLI_LOG%" 2>&1
if errorlevel 1 goto failed
set "OCHRE_HOST_EXE=%CLI_ROOT%src\Ochre.Host\bin\Debug\net10.0-windows\ochre.exe"
"%CLI_ROOT%src\Ochre.Cli\bin\Debug\net10.0\ochre-cli.exe" %*
endlocal & exit /b %ERRORLEVEL%

:failed
type "%CLI_LOG%" 1>&2
echo cli: build failed, not running ochre-cli 1>&2
endlocal & exit /b 1
