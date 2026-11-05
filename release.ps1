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

    # A first release is NOT an error. gh release view exits nonzero
    # and prints "release not found", which PowerShell can promote into
    # a terminating NativeCommandError under ErrorActionPreference=Stop.
    # Release listing succeeds and returns [] for a repository with no
    # published releases, so we can check the tag without a false failure.
    $releaseJson = & gh release list --repo 'Hor1zonVR/OrbusAutoMount' --json tagName --limit 100
    Check-Exit 'Checking existing GitHub releases'
    # ConvertFrom-Json returns zero pipeline objects for [], but wrapping
    # the pipeline in @() can still produce a null entry in Windows
    # PowerShell. Never access .tagName on null under StrictMode.
    $existingReleases = ConvertFrom-Json -InputObject ($releaseJson -join "`n")
    foreach ($release in $existingReleases) {
        if ($null -ne $release -and
            $release.PSObject.Properties.Name -contains 'tagName' -and
            $release.tagName -eq $tag) {
            throw "Release $tag already exists. Increment Plugin.cs version before another release."
        }
    }

    Write-Host "Building OrbusAutoMount $tag using reference instance: $GameDir"
    & dotnet build $project -c Release "-p:GameDir=$GameDir" --nologo
    Check-Exit 'Building AutoMount DLL'

    $dll = Join-Path $root 'src\OrbusAutoMount\bin\Release\net6.0\OrbusAutoMount.dll'
    if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) {
        throw "Build produced no DLL: $dll"
    }

    Write-Host 'Publishing compiler-built EXPERIMENTAL client mod...'
    $notes = 'v0.1.2 fixes confirmed bug from gameplay logs: AutoMount correctly observed combat START/END and scheduled a summon, but then incorrectly skipped every attempt because the legacy PlayerAvatar.mountSlot check appeared empty—even during sessions when the user mounted normally. Removed that false eligibility gate. After combat ends, AutoMount now attempts the normal PlayerCharacter.SummonMount / ceh.lew method exactly once after the configured delay, as long as the local player/avatar are active, combat has ended and the player is not already mounted or dead. Existing bounded /State diagnostics remain. This is still experimental: native summon restrictions and success require live gameplay verification. No forced mounted-state writes or network packet changes.'
    & gh release create $tag $dll --repo 'Hor1zonVR/OrbusAutoMount' --target main --title "OrbusAutoMount $tag (experimental)" --notes $notes
    Check-Exit 'Publishing GitHub release'

    Write-Host ''
    Write-Host "SUCCESS: https://github.com/Hor1zonVR/OrbusAutoMount/releases/tag/$tag"
    Write-Host 'Test locally before adding the mod to RM catalogue.'
}
finally {
    Pop-Location
}
