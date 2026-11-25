# Times window open for a spike shell: first open, then close and reopen, in both close modes.
# Set OCHRE_SPIKE_DESKTOP to a virtual desktop GUID first to keep the windows off the current desktop.
# Usage: .\open-timing.ps1 -Shell webview2|avalonia
param(
    [Parameter(Mandatory = $true)][ValidateSet('webview2', 'avalonia')][string]$Shell
)

$ErrorActionPreference = 'Stop'
$folder = if ($Shell -eq 'webview2') { 'Shell.WebView2' } else { 'Shell.Avalonia' }
$name = "ochre-spike-$Shell"
$exe = Join-Path $PSScriptRoot "$folder\bin\Release\net10.0-windows\$name.exe"
$log = Join-Path $env:LOCALAPPDATA 'Ochre-spike\timing.log'
Remove-Item Env:OCHRE_SPIKE_AUTOBENCH -ErrorAction SilentlyContinue

function Invoke-Shell([string[]]$Arguments) {
    if ($Arguments) { Start-Process -FilePath $exe -ArgumentList $Arguments } else { Start-Process -FilePath $exe }
}

foreach ($mode in @('close', 'hide')) {
    Stop-Process -Name $name -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    if ($mode -eq 'hide') { $env:OCHRE_SPIKE_HIDE_ON_CLOSE = '1' } else { Remove-Item Env:OCHRE_SPIKE_HIDE_ON_CLOSE -ErrorAction SilentlyContinue }
    $before = (Get-Content $log).Count

    Invoke-Shell
    Start-Sleep -Seconds 3
    foreach ($round in 1..3) {
        Invoke-Shell
        Start-Sleep -Seconds 8
        Invoke-Shell @('--close')
        Start-Sleep -Seconds 3
    }

    $root = Get-Process -Name $name | Select-Object -First 1
    $children = Get-CimInstance Win32_Process | Where-Object { $_.ParentProcessId -eq $root.Id }
    "[$Shell close-mode=$mode] processes after last close: host + $($children.Count) children"
    Get-Content $log | Select-Object -Skip $before | Where-Object { $_ -match "\[$Shell\] (window-open|cold-start|bench page)" } | ForEach-Object { "[$Shell close-mode=$mode] $_" }
}

Stop-Process -Name $name -ErrorAction SilentlyContinue
Remove-Item Env:OCHRE_SPIKE_HIDE_ON_CLOSE -ErrorAction SilentlyContinue
