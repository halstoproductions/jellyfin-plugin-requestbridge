<#
.SYNOPSIS
    Builds RequestBridge and installs it into a local Jellyfin server.

.DESCRIPTION
    Copies only the plugin's own assemblies plus meta.json. Jellyfin assemblies
    are deliberately not copied: the server provides them, and shipping a second
    copy invites assembly identity conflicts at load time.

    The server must be restarted afterwards. Jellyfin discovers and constructs
    plugins during startup, so a running server will not notice a new folder.

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
