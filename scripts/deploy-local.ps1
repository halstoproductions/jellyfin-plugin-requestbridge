<#
.SYNOPSIS
    Builds RequestBridge and installs it into a local Jellyfin server.

.DESCRIPTION
    Copies only the plugin's own assemblies plus meta.json. Jellyfin assemblies
    are deliberately not copied: the server provides them, and shipping a second
    copy invites assembly identity conflicts at load time.

    The server must be restarted afterwards. Jellyfin discovers and constructs
    plugins during startup, so a running server will not notice a new folder.

    Restart through the tray application, not jellyfin.exe. Stop BOTH processes
    first: the tray refuses to start a second instance of itself, so launching it
    while it is already running is a no-op and leaves the server down.

        Get-Process -Name 'Jellyfin.Windows.Tray' -ErrorAction SilentlyContinue | Stop-Process -Force
        Get-Process -Name 'jellyfin' -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Seconds 4
        Start-Process 'E:\Programs\Jellyfin\Server\jellyfin-windows-tray\Jellyfin.Windows.Tray.exe'

    The tray application launches the server with the arguments the installer
    configured, so the correct data directory is used without having to know
    what it is.

    Do not start jellyfin.exe directly. With no arguments it does NOT reuse the
    existing data directory: it creates a fresh one under %LOCALAPPDATA%\jellyfin
    and comes up as an unconfigured server. Setting the working directory does
    not help. The symptom is /System/Info/Public reporting a different server Id
    and StartupWizardCompleted false. The original data is untouched, so the fix
    is to stop the process and start it through the tray application.

.PARAMETER PluginsPath
    The server's plugins directory.

.PARAMETER Configuration
    Build configuration. Release by default.

.EXAMPLE
    .\scripts\deploy-local.ps1
#>
[CmdletBinding()]
param(
    [string]$PluginsPath = 'E:\Programs\Jellyfin\Server\plugins',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\Jellyfin.Plugin.RequestBridge'
$meta = Join-Path $project 'meta.json'

if (-not (Test-Path $PluginsPath)) {
    throw "Plugins directory not found: $PluginsPath"
}

$version = (Get-Content $meta -Raw | ConvertFrom-Json).version
$target = Join-Path $PluginsPath "RequestBridge_$version"

Write-Host "Building $Configuration..."
dotnet build (Join-Path $repoRoot 'RequestBridge.sln') -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$out = Join-Path $project "bin\$Configuration\net9.0"

# Only our own assemblies. Everything else comes from the server.
$assemblies = @(
    'Jellyfin.Plugin.RequestBridge.dll',
    'RequestBridge.Abstractions.dll'
)

# Remove older versions of this plugin only, so it is unambiguous which build is
# loaded. Best effort: a running server holds its plugin assemblies open, so the
# currently loaded version cannot be deleted until the server stops. That is not
# a failure worth aborting a deploy over, because Jellyfin supersedes older
# versions and loads the newest anyway.
Get-ChildItem $PluginsPath -Directory -Filter 'RequestBridge_*' |
    Where-Object { $_.FullName -ne $target } |
    ForEach-Object {
        try {
            Remove-Item $_.FullName -Recurse -Force -ErrorAction Stop
            Write-Host "Removed previous version $($_.Name)"
        } catch {
            Write-Warning "Could not remove $($_.Name): it is loaded by the running server. It will be superseded, and can be removed after the next restart."
        }
    }

if (-not (Test-Path $target)) {
    New-Item -ItemType Directory -Path $target | Out-Null
}

foreach ($name in $assemblies) {
    $source = Join-Path $out $name
    if (-not (Test-Path $source)) { throw "Missing build output: $source" }
    Copy-Item $source -Destination $target -Force
}

Copy-Item $meta -Destination $target -Force

Write-Host "Installed to $target"
Get-ChildItem $target | Select-Object Name, Length | Format-Table -AutoSize
Write-Host "Restart Jellyfin for the plugin to be discovered."
