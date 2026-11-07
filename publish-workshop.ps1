#requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$Workspace,
    [Parameter(Mandatory)][string]$Uploader,
    [switch]$CreateNew
)
$ErrorActionPreference = 'Stop'
$checked = & (Join-Path $PSScriptRoot 'tools/release/validate-workspace.ps1') -Workspace $Workspace
if ($CreateNew -and $checked.WorkshopId) { throw 'Workspace already has a Workshop ID. Omit CreateNew to update that item.' }
if (-not $CreateNew -and -not $checked.WorkshopId) { throw 'Set mod_id.txt to the existing Core Workshop ID, or use CreateNew for the first release.' }
$uploaderPath = (Get-Item -LiteralPath $Uploader).FullName
Push-Location (Split-Path -Parent $uploaderPath)
try {
    & $uploaderPath upload -w $checked.Workspace
    if ($LASTEXITCODE -ne 0) { throw "ModUploader failed with exit code $LASTEXITCODE. See its mod-uploader.log." }
} finally { Pop-Location }
$published = & (Join-Path $PSScriptRoot 'tools/release/validate-workspace.ps1') -Workspace $checked.Workspace
if (-not $published.WorkshopId) { throw 'ModUploader did not persist the Workshop item ID in mod_id.txt.' }
Write-Host "Published Core $($published.Package.Bundle.releaseVersion): https://steamcommunity.com/sharedfiles/filedetails/?id=$($published.WorkshopId)"
