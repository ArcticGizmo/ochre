@echo off
rem Regenerates every icon asset from ochre.svg (see tools\Ochre.IconGen\Program.cs for the list).
rem Run after editing ochre.svg, then commit what it writes. Pass --preview captures\<name>.png for a zoomed sheet
rem of the small frames.
dotnet run --project "%~dp0Ochre.IconGen" -c Release -- %*
