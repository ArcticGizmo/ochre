# Runs the spike's throughput commands inside a Windows Terminal window and records how long each takes
# to push its output through Windows Terminal's ConPTY and renderer. Compare with the bench panel's numbers.
# Usage: .\wt-baseline.ps1   (results appended to %LOCALAPPDATA%\Ochre-spike\wt-baseline.log)
param(
    [string]$ListPath = "$env:USERPROFILE\.nuget\packages",
    [string]$BigFile = "$env:LOCALAPPDATA\Ochre-spike\big-50mb.txt"
)

$ErrorActionPreference = 'Stop'
$log = Join-Path $env:LOCALAPPDATA 'Ochre-spike\wt-baseline.log'
$inner = Join-Path $env:LOCALAPPDATA 'Ochre-spike\wt-baseline-inner.ps1'

if (-not (Test-Path $BigFile)) { Write-Error "Big file missing: run the bench panel's 'Cat 50 MB file' once first." }

$script = @"
`$list = Measure-Command { Get-ChildItem -Recurse '$ListPath' | Out-Host }
`$cat = Measure-Command { cmd /c type '$BigFile' | Out-Host }
"{0:O} windows-terminal dir listing {1:N0} ms, cat 50 MB {2:N0} ms" -f (Get-Date), `$list.TotalMilliseconds, `$cat.TotalMilliseconds | Add-Content '$log'
exit
"@
Set-Content -Path $inner -Value $script -Encoding ascii

$pwsh = (Get-Command pwsh -ErrorAction SilentlyContinue).Source
if (-not $pwsh) { $pwsh = 'powershell.exe' }
Start-Process wt.exe -ArgumentList @('new-tab', $pwsh, '-NoLogo', '-NoProfile', '-File', $inner)
"Started; result will be appended to $log"
