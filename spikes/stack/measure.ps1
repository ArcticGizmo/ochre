# Samples memory for a spike shell and every process it spawned (WebView2 browser, renderers, GPU, conhost, shells).
# Usage: .\measure.ps1 -ProcessName ochre-spike-webview2 [-Samples 5] [-IntervalSeconds 2]
param(
    [Parameter(Mandatory = $true)][string]$ProcessName,
    [int]$Samples = 5,
    [int]$IntervalSeconds = 2
)

$ErrorActionPreference = 'Stop'

function Get-Tree([int]$RootId, $All) {
    $result = @($All | Where-Object { $_.ProcessId -eq $RootId })
    $queue = [System.Collections.Generic.Queue[int]]::new()
    $queue.Enqueue($RootId)
    while ($queue.Count -gt 0) {
        $parent = $queue.Dequeue()
        foreach ($child in ($All | Where-Object { $_.ParentProcessId -eq $parent })) {
            $result += $child
            $queue.Enqueue([int]$child.ProcessId)
        }
    }
    return $result
}

$root = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $root) { Write-Error "No process named $ProcessName is running." }

for ($i = 1; $i -le $Samples; $i++) {
    $all = Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId, Name, WorkingSetSize, PrivatePageCount
    $tree = Get-Tree -RootId $root.Id -All $all
    $ws = ($tree | Measure-Object -Property WorkingSetSize -Sum).Sum / 1MB
    $priv = ($tree | Measure-Object -Property PrivatePageCount -Sum).Sum / 1MB
    $hostWs = ($tree | Where-Object { $_.ProcessId -eq $root.Id }).WorkingSetSize / 1MB
    # The spike target is host + WebView only; the terminal's shell and conhost belong to the user's work.
    $app = $tree | Where-Object { $_.Name -notin @('pwsh.exe', 'powershell.exe', 'conhost.exe', 'cmd.exe') }
    $appWs = ($app | Measure-Object -Property WorkingSetSize -Sum).Sum / 1MB
    $appPriv = ($app | Measure-Object -Property PrivatePageCount -Sum).Sum / 1MB
    $names = ($tree | Group-Object Name | ForEach-Object { "$($_.Name)x$($_.Count)" }) -join ', '
    "{0:HH:mm:ss}  total WS {1,7:N1} MB  private {2,7:N1} MB  host+WebView WS {3,7:N1} MB  private {4,7:N1} MB  host WS {5,6:N1} MB  [{6}]" -f (Get-Date), $ws, $priv, $appWs, $appPriv, $hostWs, $names
    if ($i -lt $Samples) { Start-Sleep -Seconds $IntervalSeconds }
}
