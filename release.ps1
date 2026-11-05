[CmdletBinding()]
param(
    [string]$GameDir = "$env:LOCALAPPDATA\OrbusRebornModdingInstances\My Orbus Renewed 2"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\OrbusAutoMount\OrbusAutoMount.csproj'
$plugin = Join-Path $root 'src\OrbusAutoMount\Plugin.cs'

function Check-Exit([string]$label) {
    if ($LASTEXITCODE -ne 0) { throw "$label failed (exit $LASTEXITCODE)." }
}

Push-Location $root
try {
    Write-Host '====================================='
    Write-Host '      OrbusAutoMount Release Tool'
    Write-Host '====================================='
    Write-Host ''

    if (-not (Test-Path -LiteralPath (Join-Path $root '.git'))) {
        throw "Clone the GitHub repository first: https://github.com/Hor1zonVR/OrbusAutoMount"
    }
    if (-not (Test-Path -LiteralPath $GameDir -PathType Container)) {
        throw "Orbus reference instance not found: $GameDir"
    }
    if ((& git status --porcelain)) {
        throw 'Uncommitted local changes. Commit or stash them before running release.ps1.'
    }
    & git pull --ff-only origin main
    Check-Exit 'Updating repository source'

    $source = Get-Content -LiteralPath $plugin -Raw
    $match = [regex]::Match($source,
        '\[BepInPlugin\(\s*"[^"]+"\s*,\s*"[^"]+"\s*,\s*"(?<version>\d+\.\d+\.\d+)"')
    if (-not $match.Success) {
        throw 'Could not read BepInEx plugin version from Plugin.cs.'
    }
    $version = $match.Groups['version'].Value
    $tag = "v$version"

    & gh auth status
    Check-Exit 'Checking GitHub login'

    & gh release view $tag --repo 'Hor1zonVR/OrbusAutoMount' *> $null
    if ($LASTEXITCODE -eq 0) {
        throw "Release $tag already exists. Increment Plugin.cs version before another release."
    }

    Write-Host "Building OrbusAutoMount $tag using reference instance: $GameDir"
    & dotnet build $project -c Release "-p:GameDir=$GameDir" --nologo
    Check-Exit 'Building AutoMount DLL'

    $dll = Join-Path $root 'src\OrbusAutoMount\bin\Release\net6.0\OrbusAutoMount.dll'
    if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) {
        throw "Build produced no DLL: $dll"
    }

    Write-Host 'Publishing compiler-built EXPERIMENTAL client mod...'
    $notes = 'Initial experimental IL2CPP client mod. Configurable enable switch (off by default) and summon delay, auto-discovered by RebornMenu. Polls local monster combat state, detects real combat to out-of-combat transitions, waits and invokes the native PlayerCharacter.SummonMount / ceh.lew action once. No forced mount state or manual networking edits. Native summon zone restrictions and combat-field mappings still require in-game verification. Not yet in RebornManager catalogue.'
    & gh release create $tag $dll --repo 'Hor1zonVR/OrbusAutoMount' --target main --title "OrbusAutoMount $tag (experimental)" --notes $notes
    Check-Exit 'Publishing GitHub release'

    Write-Host ''
    Write-Host "SUCCESS: https://github.com/Hor1zonVR/OrbusAutoMount/releases/tag/$tag"
    Write-Host 'Test locally before adding the mod to RM catalogue.'
}
finally {
    Pop-Location
}
