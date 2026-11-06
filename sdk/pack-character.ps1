param(
    [Parameter(Mandatory)][string]$CorePackage,
    [Parameter(Mandatory)][string]$CharacterDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$ModId,
    [string[]]$RequiredFeatures = @('shine@1'),
    [string[]]$OptionalFeatures = @('diagnostics@1', 'music@1'),
    [string]$CharacterFamily = ''
)
$ErrorActionPreference = 'Stop'
$checked = & (Join-Path $PSScriptRoot 'validate-package.ps1') -CorePackage $CorePackage
$bundle = $checked.Bundle
foreach ($feature in $RequiredFeatures) { if ($bundle.features -notcontains $feature) { throw "Core lacks required feature $feature" } }
$source = [IO.Path]::GetFullPath($CharacterDirectory)
$upload = [IO.Path]::GetFullPath($OutputDirectory)
if ($ModId -ne [IO.Path]::GetFileName($ModId) -or $ModId -eq 'ShoujoKagekiAijoCore') { throw 'Invalid character mod ID.' }
$manifest = Get-Content -LiteralPath (Join-Path $source ($ModId + '.json')) -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.id -ne $ModId) { throw 'Character manifest ID mismatch.' }
$coreDependency = @($manifest.dependencies | Where-Object { $_.id -eq 'ShoujoKagekiAijoCore' })
if ($coreDependency.Count -ne 1 -or -not $coreDependency[0].min_version) { throw 'Character manifest must declare the Core minimum version.' }
if ([Version]($bundle.releaseVersion.Split('-')[0]) -lt [Version]($coreDependency[0].min_version.Split('-')[0])) { throw 'Core package is older than the character dependency.' }
if (@($manifest.dependencies | Where-Object { $_.id -eq 'BaseLib' }).Count -ne 1) { throw 'Character manifest must declare BaseLib.' }
New-Item -ItemType Directory -Force -Path $upload | Out-Null
$character = Join-Path $upload $ModId
if (-not $source.Equals([IO.Path]::GetFullPath($character), [StringComparison]::OrdinalIgnoreCase)) {
    if (Test-Path -LiteralPath $character) { throw 'Character output already exists. Use its directory for in-place metadata generation.' }
    Copy-Item -LiteralPath $source -Destination $character -Recurse
}
$destination = Join-Path $upload 'ShoujoKagekiAijoCore'
if (-not $checked.Root.Equals([IO.Path]::GetFullPath($destination), [StringComparison]::OrdinalIgnoreCase)) {
    if (Test-Path -LiteralPath $destination) {
        $installed = & (Join-Path $PSScriptRoot 'validate-package.ps1') -CorePackage $destination
        if ((Get-FileHash -LiteralPath (Join-Path $installed.Root 'core.bundle')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $checked.Root 'core.bundle')).Hash) {
            throw 'Output contains a different Core package. Choose a fresh upload directory.'
        }
    } else { Copy-Item -LiteralPath $checked.Root -Destination $destination -Recurse }
}
$requirements = [ordered]@{ format=1; modId=$ModId; apiMajor=1; minimumApiVersion='1.0.0'; requiredFeatures=@($RequiredFeatures); optionalFeatures=@($OptionalFeatures) }
if (-not $CharacterFamily -and $ModId.StartsWith('ShoujoKageki', [StringComparison]::Ordinal)) { $CharacterFamily = 'ShoujoKageki' }
if ($CharacterFamily) { $requirements['characterFamily'] = $CharacterFamily }
$requirements | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $character 'core.requirements') -Encoding UTF8
$lockFiles = [ordered]@{}
Get-ChildItem -LiteralPath $destination -Recurse -File | Sort-Object FullName | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($destination, $_.FullName).Replace('\','/')
    $lockFiles[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}
[ordered]@{ format=1; releaseVersion=$bundle.releaseVersion; files=$lockFiles } | ConvertTo-Json -Depth 10 |
    Set-Content -LiteralPath (Join-Path $character 'core.package.lock') -Encoding UTF8
Write-Host "Packaged $ModId with Core $($bundle.releaseVersion)"
