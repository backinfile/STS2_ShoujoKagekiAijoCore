#requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$CorePackage,
    [Parameter(Mandatory)][string]$PreviewImage,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$WorkshopConfig = (Join-Path $PSScriptRoot 'workshop.json'),
    [ValidatePattern('^[1-9][0-9]*$')][string]$WorkshopId,
    [string]$ChangeNote = ''
)
$ErrorActionPreference = 'Stop'
$checked = & (Join-Path $PSScriptRoot 'sdk/validate-package.ps1') -CorePackage $CorePackage -Strict
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Release output already exists. Choose a fresh OutputDirectory.' }
if ($output.StartsWith($checked.Root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Release output must be outside the source Core package.'
}
$preview = Get-Item -LiteralPath $PreviewImage
if ($preview.Length -ge 1MB -or $preview.PSIsContainer) { throw 'Workshop preview must be a PNG smaller than 1 MB.' }
$signature = [IO.File]::ReadAllBytes($preview.FullName)
if ($signature.Length -lt 8 -or [Convert]::ToHexString($signature[0..7]) -ne '89504E470D0A1A0A') {
    throw 'Workshop preview must be a PNG.'
}
$config = Get-Content -LiteralPath $WorkshopConfig -Raw -Encoding UTF8 | ConvertFrom-Json
if ($config.dependencies -notcontains 3737335127) { throw 'Core Workshop config must declare the BaseLib required item.' }
if ($ChangeNote) { $config | Add-Member -NotePropertyName changeNote -NotePropertyValue $ChangeNote -Force }
if ($WorkshopId) { $null = [ulong]::Parse($WorkshopId) }
$workspace = Join-Path $output 'workshop'
$content = Join-Path $workspace 'content'
New-Item -ItemType Directory -Path $content -Force | Out-Null
Copy-Item -LiteralPath $checked.Root -Destination (Join-Path $content 'ShoujoKagekiAijoCore') -Recurse
Copy-Item -LiteralPath $preview.FullName -Destination (Join-Path $workspace 'image.png')
$config | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $workspace 'workshop.json') -Encoding UTF8
if ($WorkshopId) { Set-Content -LiteralPath (Join-Path $workspace 'mod_id.txt') -Value $WorkshopId -Encoding UTF8 }
$prepared = & (Join-Path $PSScriptRoot 'tools/release/validate-workspace.ps1') -Workspace $workspace
$archiveName = "ShoujoKagekiAijoCore-$($checked.Bundle.releaseVersion).zip"
$archive = Join-Path $output $archiveName
Compress-Archive -LiteralPath $prepared.Package.Root -DestinationPath $archive -CompressionLevel Optimal
$bundleHash = (Get-FileHash -LiteralPath (Join-Path $prepared.Package.Root 'core.bundle') -Algorithm SHA256).Hash.ToLowerInvariant()
$archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
@("$archiveHash  $archiveName", "$bundleHash  workshop/content/ShoujoKagekiAijoCore/core.bundle") |
    Set-Content -LiteralPath (Join-Path $output 'checksums.sha256') -Encoding UTF8
[ordered]@{
    releaseVersion = $checked.Bundle.releaseVersion
    apiVersion = $checked.Bundle.apiVersion
    targets = $checked.Bundle.targets
    bundleSha256 = $bundleHash
    archive = $archiveName
    archiveSha256 = $archiveHash
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'release.json') -Encoding UTF8
Write-Host "Prepared Core $($checked.Bundle.releaseVersion): $output"
