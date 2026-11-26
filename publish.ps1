<#
.SYNOPSIS
    Builds an Ochre release and packs it with Velopack into releases\.

.DESCRIPTION
    Publishes the host (self-contained, single file) and the console CLI (NativeAOT, as ochre.com) into publish\,
    packs them with the vpk pinned in .config\dotnet-tools.json, and writes SHA256SUMS.txt beside the result.
    release.yml runs this same script for a tagged release (ADR 0014).

    releases\ ends up with OchreApp-win-Setup.exe (the installer), OchreApp-win-Portable.zip, and the update feed
    (the .nupkg, RELEASES and releases.win.json) that an installed Ochre reads. Older versions' packages left in
    releases\ let vpk add delta packages.

    KEEP THIS FILE PURE ASCII: Windows PowerShell 5.1 reads a file without a byte-order mark as the system code page.

.PARAMETER Version
    The release version (SemVer, e.g. 0.2.0). Defaults to <Version> in src\Ochre.Host\Ochre.Host.csproj.

.PARAMETER Install
    Run the new installer afterwards. It installs per user, puts Ochre in the Start menu and starts it.

.EXAMPLE
    .\publish.ps1
    .\publish.ps1 -Version 0.2.0 -Install
#>
#Requires -Version 5.1
param(
    [string] $Version,
    [switch] $Install
)

$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot

# The installed app's id. Not "Ochre": Velopack installs to %LocalAppData%\<id> and removes that folder on uninstall,
# and %LocalAppData%\Ochre holds Ochre's own data (logs, window layout, terminal scrollback).
$PackId = 'OchreApp'

function Invoke-Native {
    param([string] $What, [scriptblock] $Command)
    Write-Host "== $What" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$What failed (exit code $LASTEXITCODE)." }
}

if (-not $Version) {
    $Version = ([xml](Get-Content -LiteralPath 'src\Ochre.Host\Ochre.Host.csproj' -Raw)).Project.PropertyGroup |
        ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
}
$Version = "$Version".TrimStart('v')
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw "'$Version' isn't a release version; pass -Version 1.2.3 (SemVer)."
}
Write-Host "Building Ochre $Version" -ForegroundColor Cyan

# A clean publish folder, so nothing from an earlier build is packed by mistake.
if (Test-Path -LiteralPath 'publish') { Remove-Item -LiteralPath 'publish' -Recurse -Force }

Invoke-Native 'Bridge types are current' { dotnet run --project tools\Ochre.BridgeGen -- --check }
Invoke-Native 'UI packages' { pnpm --dir ui install --frozen-lockfile }
Invoke-Native 'UI' { pnpm --dir ui build }

# No EnableCompressionInSingleFile: a compressed bundle inflates every assembly into private memory for the tray's
# whole life, and the Velopack package is a zip anyway (Perch's measurement). Native libraries such as
# WebView2Loader.dll stay beside the exe, and wwwroot is copied as content.
Invoke-Native 'Host' {
    dotnet publish src\Ochre.Host\Ochre.Host.csproj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:DebugType=embedded -p:PublishReferencesDocumentationFiles=false -p:Version=$Version -o publish
}

# NativeAOT gives `ochre` a fast start in every terminal and agent call, but links with the Visual Studio C++ tools.
# Without them, fall back to a self-contained single file so a local build still packs; CI has them.
Write-Host '== CLI (NativeAOT)' -ForegroundColor Cyan
dotnet publish src\Ochre.Cli\Ochre.Cli.csproj -c Release -r win-x64 -p:DebugType=embedded -p:Version=$Version -o publish
if ($LASTEXITCODE -ne 0) {
    Write-Warning 'NativeAOT publish failed (no C++ build tools?); publishing the CLI as a self-contained single file instead.'
    Invoke-Native 'CLI (single file)' {
        dotnet publish src\Ochre.Cli\Ochre.Cli.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=false `
            -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=embedded -p:Version=$Version -o publish
    }
}

foreach ($required in 'ochre.exe', 'ochre.com', 'wwwroot\index.html') {
    if (-not (Test-Path -LiteralPath (Join-Path 'publish' $required))) { throw "publish\$required is missing." }
}

Invoke-Native 'Restore vpk' { dotnet tool restore }
Invoke-Native 'Pack' {
    dotnet vpk pack --packId $PackId --packTitle 'Ochre' --packVersion $Version --packDir publish `
        --mainExe ochre.exe --icon 'src\Ochre.Platform.Windows\Assets\ochre.ico' --shortcuts StartMenuRoot --outputDir releases
}

# sha256sum's own format (lower-case hex, two spaces, LF), so `sha256sum -c` checks it as it is. Only this version's
# packages are listed: releases\ keeps older ones for deltas, while every other file is rewritten by each pack.
Write-Host '== Checksums' -ForegroundColor Cyan
$dir = (Resolve-Path -LiteralPath 'releases').Path
$lines = Get-ChildItem -LiteralPath $dir -File |
    Where-Object { $_.Name -ne 'SHA256SUMS.txt' -and ($_.Extension -ne '.nupkg' -or $_.Name.StartsWith("$PackId-$Version-", [StringComparison]::OrdinalIgnoreCase)) } |
    Sort-Object Name |
    ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
[IO.File]::WriteAllText((Join-Path $dir 'SHA256SUMS.txt'), (($lines -join "`n") + "`n"))
$lines | ForEach-Object { Write-Host "  $_" }

$setup = Join-Path $dir "$PackId-win-Setup.exe"
Write-Host ''
Write-Host "Ochre $Version is in releases\." -ForegroundColor Green
if ($Install) {
    Write-Host "Running $setup"
    Start-Process -FilePath $setup
}
else {
    Write-Host "  Install it:  $setup"
    Write-Host '  Or try it as an update: set OCHRE_UPDATE_FEED to the releases folder for an installed Ochre, then Check for updates in Settings.'
}
