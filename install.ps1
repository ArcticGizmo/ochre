<#
.SYNOPSIS
    Installs Ochre on Windows.

.DESCRIPTION
    Downloads the Velopack installer (OchreApp-win-Setup.exe) from a GitHub release, checks it against the
    SHA256SUMS.txt published with it, and runs it. From then on Ochre updates itself: Settings, Check for updates.

        irm https://raw.githubusercontent.com/ArcticGizmo/ochre/main/install.ps1 | iex

    A download through a browser is marked as from the web and stops at SmartScreen; this route isn't.

    Adapted from Perch's install.ps1 (perch 9275e0b). KEEP THIS FILE PURE ASCII: Windows PowerShell 5.1 reads a file
    without a byte-order mark as the system code page, where a UTF-8 dash can become a curly quote that ends a string.

.PARAMETER Version
    A specific version (e.g. 0.2.0) instead of the latest release. Also read from $env:OCHRE_VERSION, which is how to
    pin one through the piped one-liner.

.PARAMETER Repo
    owner/name of the GitHub repository. Defaults to ArcticGizmo/ochre; also read from $env:OCHRE_REPO.
#>
#Requires -Version 5.1
param(
    [string] $Version = $env:OCHRE_VERSION,
    [string] $Repo    = $(if ($env:OCHRE_REPO) { $env:OCHRE_REPO } else { 'ArcticGizmo/ochre' })
)

# Failures throw rather than exit: run through iex, `exit` would close the user's own shell.
function Install-Ochre {
    [CmdletBinding()]
    param([string] $Version, [string] $Repo)

    $ErrorActionPreference = 'Stop'
    $SetupAsset = 'OchreApp-win-Setup.exe'
    $SumsAsset  = 'SHA256SUMS.txt'

    if ([System.Environment]::OSVersion.Platform -ne 'Win32NT') { throw 'Ochre installs on Windows only for now.' }
    if (-not [System.Environment]::Is64BitOperatingSystem) { throw 'Ochre is 64-bit only.' }
    if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { Write-Warning 'ARM64 Windows: Ochre is x64, so it runs under emulation.' }
    # Older Windows PowerShell hosts can still default to TLS 1.0, which api.github.com refuses.
    try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 } catch { }

    $release = Get-OchreRelease -Repo $Repo -Version $Version
    $tag = $release.tag_name
    Write-Host "Installing Ochre $tag" -ForegroundColor Cyan
    $setupUrl = Get-AssetUrl -Release $release -Name $SetupAsset
    $sumsUrl  = Get-AssetUrl -Release $release -Name $SumsAsset

    $work = Join-Path ([System.IO.Path]::GetTempPath()) ('ochre-install-' + [guid]::NewGuid().ToString('N').Substring(0, 12))
    New-Item -ItemType Directory -Path $work | Out-Null
    $keepWork = $false
    try {
        # Release assets are served as application/octet-stream, so Windows PowerShell 5.1 hands back bytes, not text.
        $raw  = (Invoke-WebRequest -Uri $sumsUrl -UseBasicParsing).Content
        $sums = if ($raw -is [byte[]]) { [System.Text.Encoding]::UTF8.GetString($raw) } else { [string]$raw }
        $want = Get-ExpectedHash -Sums $sums -Name $SetupAsset -Tag $tag

        $setup = Join-Path $work $SetupAsset
        Save-File -Uri $setupUrl -OutFile $setup -Label $SetupAsset

        $got = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash
        if ($got -ne $want) {
            Remove-Item -LiteralPath $setup -Force -ErrorAction SilentlyContinue
            throw "Checksum mismatch for $SetupAsset, so it was deleted and not run.`n  expected  $want`n  actual    $got"
        }
        Write-Host "  SHA-256 verified  $($want.ToLowerInvariant())" -ForegroundColor DarkGray

        # The installer needs no admin rights: it installs to %LocalAppData%\OchreApp, adds the Start menu entry and
        # the uninstaller, then starts Ochre. Wait on its own process only: Start-Process -Wait would also wait for
        # Ochre, which keeps running.
        Write-Host 'Running the installer...'
        $psi = [System.Diagnostics.ProcessStartInfo]::new($setup)
        $psi.UseShellExecute = $true
        $proc = [System.Diagnostics.Process]::Start($psi)
        if (-not $proc) { throw "Could not start $SetupAsset." }
        if (-not $proc.WaitForExit(10 * 60 * 1000)) {
            $keepWork = $true
            Write-Warning "The installer is still running after 10 minutes; leaving it to finish. Delete $work afterwards."
            return
        }
        if ($proc.ExitCode -ne 0) { throw "The installer exited with code $($proc.ExitCode)." }
    }
    finally {
        if (-not $keepWork) { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
    }

    Write-Host ''
    Write-Host "Ochre $tag is installed and starting." -ForegroundColor Green
    Write-Host '  `ochre` works in new terminals. Updates: Settings, Check for updates.' -ForegroundColor DarkGray
}

function Get-OchreRelease {
    param([string] $Repo, [string] $Version)
    $headers = @{ 'Accept' = 'application/vnd.github+json'; 'User-Agent' = 'ochre-install.ps1' }
    # Only useful behind a shared address that has used up the anonymous rate limit; never sent with downloads.
    if ($env:GITHUB_TOKEN) { $headers['Authorization'] = "Bearer $env:GITHUB_TOKEN" }
    if ([string]::IsNullOrWhiteSpace($Version)) {
        $uri = "https://api.github.com/repos/$Repo/releases/latest"; $what = 'the latest release'
    }
    else {
        $tag = if ($Version.StartsWith('v')) { $Version } else { "v$Version" }
        $uri = "https://api.github.com/repos/$Repo/releases/tags/$tag"; $what = "release $tag"
    }
    try { return Invoke-RestMethod -Uri $uri -Headers $headers -UseBasicParsing }
    catch { throw "Could not look up $what of $Repo. $($_.Exception.Message)" }
}

function Get-AssetUrl {
    param($Release, [string] $Name)
    $asset = $Release.assets | Where-Object { $_.name -eq $Name } | Select-Object -First 1
    if (-not $asset) { throw "Release $($Release.tag_name) has no $Name, so it can't be checked or installed. See $($Release.html_url)." }
    return $asset.browser_download_url
}

# A manifest that lists other files but not this one is a build error; treating it as nothing to check would defeat
# the check, so it throws.
function Get-ExpectedHash {
    param([string] $Sums, [string] $Name, [string] $Tag)
    foreach ($line in $Sums -split "`r?`n") {
        if ($line -match '^\s*([0-9a-fA-F]{64})\s+\*?(.+?)\s*$' -and $Matches[2] -eq $Name) { return $Matches[1].ToUpperInvariant() }
    }
    throw "SHA256SUMS.txt for $Tag has no entry for $Name, so the download can't be checked."
}

# Streams to disk with progress. Invoke-WebRequest -OutFile buffers the whole response in Windows PowerShell 5.1 and
# its progress bar slows a large download to a crawl. No Authorization header: asset URLs redirect to pre-signed URLs
# that refuse one.
function Save-File {
    param([string] $Uri, [string] $OutFile, [string] $Label)
    $req = [System.Net.WebRequest]::CreateHttp($Uri)
    $req.UserAgent = 'ochre-install.ps1'
    $req.Timeout = 60000
    $req.ReadWriteTimeout = 60000
    $resp = $req.GetResponse()
    try {
        $total = $resp.ContentLength
        $shown = -1
        $in = $resp.GetResponseStream()
        $out = [System.IO.File]::Create($OutFile)
        try {
            $buffer = [byte[]]::new(131072)
            $read = 0
            while (($n = $in.Read($buffer, 0, $buffer.Length)) -gt 0) {
                $out.Write($buffer, 0, $n)
                $read += $n
                if ($total -gt 0) {
                    $pct = [int](100 * $read / $total)
                    if ($pct -ne $shown) {
                        $shown = $pct
                        Write-Progress -Activity "Downloading $Label" -Status ('{0:N1} of {1:N1} MB' -f ($read / 1MB), ($total / 1MB)) -PercentComplete $pct
                    }
                }
            }
        }
        finally {
            $out.Dispose()
            $in.Dispose()
            Write-Progress -Activity "Downloading $Label" -Completed
        }
        if ($total -gt 0 -and (Get-Item -LiteralPath $OutFile).Length -ne $total) { throw "$Label downloaded incompletely." }
    }
    finally { $resp.Dispose() }
    Write-Host ('  downloaded {0} ({1:N1} MB)' -f $Label, ((Get-Item -LiteralPath $OutFile).Length / 1MB))
}

Install-Ochre -Version $Version -Repo $Repo
