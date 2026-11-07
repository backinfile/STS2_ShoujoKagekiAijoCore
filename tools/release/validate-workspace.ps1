#requires -Version 7.0
param([Parameter(Mandatory)][string]$Workspace)
$ErrorActionPreference = 'Stop'
$workspacePath = [IO.Path]::GetFullPath($Workspace)
$content = Join-Path $workspacePath 'content'
$entries = @(Get-ChildItem -LiteralPath $content -Force)
if ($entries.Count -ne 1 -or -not $entries[0].PSIsContainer -or $entries[0].Name -ne 'ShoujoKagekiAijoCore' -or
    ($entries[0].Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw 'Core Workshop content must contain exactly one ShoujoKagekiAijoCore mod directory.'
}
$package = & (Join-Path $PSScriptRoot '../../sdk/validate-package.ps1') -CorePackage $entries[0].FullName -Strict
$preview = Get-Item -LiteralPath (Join-Path $workspacePath 'image.png')
if ($preview.Length -ge 1MB -or $preview.PSIsContainer) { throw 'Workshop preview must be a PNG smaller than 1 MB.' }
$signature = [IO.File]::ReadAllBytes($preview.FullName)
if ($signature.Length -lt 8 -or [Convert]::ToHexString($signature[0..7]) -ne '89504E470D0A1A0A') { throw 'Workshop preview must be a PNG.' }
$config = Get-Content -LiteralPath (Join-Path $workspacePath 'workshop.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $config.title -or -not $config.description -or $config.visibility -notin @('public', 'private', 'unlisted', 'friends_only')) {
    throw 'Workshop config must include title, description and a valid visibility.'
}
if ($config.dependencies -notcontains 3737335127) { throw 'Core Workshop config must declare the BaseLib required item.' }
$itemIdPath = Join-Path $workspacePath 'mod_id.txt'
$itemId = ''
if (Test-Path -LiteralPath $itemIdPath) {
    $itemId = [string](Get-Content -LiteralPath $itemIdPath -Raw -Encoding UTF8)
    $itemId = $itemId.Trim()
    if ($itemId -notmatch '^[1-9][0-9]*$' -or [ulong]::Parse($itemId) -eq 0) { throw 'Invalid mod_id.txt Workshop item ID.' }
}
[pscustomobject]@{ Workspace = $workspacePath; Package = $package; WorkshopId = $itemId }
