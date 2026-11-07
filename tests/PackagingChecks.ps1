#requires -Version 7.0
param([Parameter(Mandatory)][string]$CorePackage)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$scratch = Join-Path $root ('out/tmp/packaging-checks-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$script:checks = 0
function Check([bool]$condition, [string]$name) {
    if (-not $condition) { throw "Check failed: $name" }
    $script:checks++
}
function Reject([scriptblock]$action, [string]$message) {
    try { & $action } catch {
        if ($_.Exception.Message -notlike "*$message*") { throw }
        $script:checks++
        return
    }
    throw "Expected rejection: $message"
}
$fixture = Join-Path $scratch 'Core'
Copy-Item -LiteralPath $CorePackage -Destination $fixture -Recurse
Copy-Item -LiteralPath (Join-Path $root 'sdk/pack-character.ps1'), (Join-Path $root 'sdk/validate-package.ps1') -Destination (Join-Path $fixture 'sdk') -Force
$bundle = Get-Content -LiteralPath (Join-Path $fixture 'core.bundle') -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($relative in @('sdk/pack-character.ps1', 'sdk/validate-package.ps1')) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $fixture $relative) -Algorithm SHA256).Hash.ToLowerInvariant()
    $bundle.files | Add-Member -NotePropertyName $relative -NotePropertyValue $hash -Force
}
$bundle | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $fixture 'core.bundle') -Encoding UTF8
$null = & (Join-Path $root 'sdk/validate-package.ps1') -CorePackage $fixture -Strict
$pack = Join-Path $fixture 'sdk/pack-character.ps1'
foreach ($modId in @('ShoujoKagekiAijoKaren', 'ShoujoKagekiJunna', 'ExampleCharacterMod')) {
    $source = Join-Path $scratch "sources/$modId"
    New-Item -ItemType Directory -Path $source -Force | Out-Null
    [ordered]@{ id=$modId; dependencies=@(@{id='BaseLib'; min_version='3.4.7'}, @{id='ShoujoKagekiAijoCore'; min_version='1.0.0'}) } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $source "$modId.json") -Encoding UTF8
    $upload = Join-Path $scratch "uploads/$modId"
    & $pack -CorePackage $fixture -CharacterDirectory $source -OutputDirectory $upload -ModId $modId
    $role = Join-Path $upload $modId
    $embedded = Join-Path $upload 'ShoujoKagekiAijoCore'
    Check ((Test-Path -LiteralPath $embedded) -eq ($modId -eq 'ShoujoKagekiAijoKaren')) "$modId distribution"
    $requirements = Get-Content -LiteralPath (Join-Path $role 'core.requirements') -Raw -Encoding UTF8 | ConvertFrom-Json
    Check ($requirements.modId -eq $modId -and $requirements.requiredFeatures -contains 'shine@1') "$modId requirements"
    $lock = Get-Content -LiteralPath (Join-Path $role 'core.package.lock') -Raw -Encoding UTF8 | ConvertFrom-Json
    Check ($lock.releaseVersion -eq $bundle.releaseVersion -and $lock.files.'core.bundle' -eq (Get-FileHash (Join-Path $fixture 'core.bundle')).Hash.ToLowerInvariant()) "$modId SDK lock"
    & $pack -CorePackage $fixture -CharacterDirectory $role -OutputDirectory $upload -ModId $modId
    if ($modId -eq 'ShoujoKagekiAijoKaren') {
        Check ((Get-FileHash (Join-Path $embedded 'core.bundle')).Hash -eq (Get-FileHash (Join-Path $fixture 'core.bundle')).Hash) 'Karen embeds identical official Core'
    } else {
        New-Item -ItemType Directory -Path $embedded | Out-Null
        Reject { & $pack -CorePackage $fixture -CharacterDirectory $role -OutputDirectory $upload -ModId $modId } 'Only Karen embeds Core'
    }
}
$preview = Join-Path $scratch 'preview.png'
[IO.File]::WriteAllBytes($preview, [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aN9sAAAAASUVORK5CYII='))
$release = Join-Path $scratch 'release'
$prepare = Join-Path $root 'prepare-release.ps1'
& $prepare -CorePackage $fixture -PreviewImage $preview -OutputDirectory $release -ChangeNote '定向验证'
$workspace = Join-Path $release 'workshop'
$validate = Join-Path $root 'tools/release/validate-workspace.ps1'
$prepared = & $validate -Workspace $workspace
Check (-not $prepared.WorkshopId) 'first-release workspace has no item ID'
Check ((Get-ChildItem (Join-Path $workspace 'content')).Count -eq 1) 'standalone content contains only Core'
$config = Get-Content (Join-Path $workspace 'workshop.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Check ($config.changeNote -eq '定向验证' -and $config.dependencies -contains 3737335127) 'release notes and BaseLib required item'
$summary = Get-Content (Join-Path $release 'release.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Check ($summary.archiveSha256 -eq (Get-FileHash (Join-Path $release $summary.archive)).Hash.ToLowerInvariant()) 'archive checksum'
$archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $release $summary.archive))
try {
    Check (@($archive.Entries | Where-Object { -not $_.FullName.StartsWith('ShoujoKagekiAijoCore/') }).Count -eq 0) 'ZIP installation directory'
    foreach ($entry in $archive.Entries | Where-Object Length -gt 0) {
        $relative = $entry.FullName.Substring('ShoujoKagekiAijoCore/'.Length)
        $stream = $entry.Open()
        try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
        Check ($hash -eq (Get-FileHash (Join-Path $fixture $relative)).Hash) "ZIP file integrity: $relative"
    }
} finally { $archive.Dispose() }
Reject { & $prepare -CorePackage $fixture -PreviewImage $preview -OutputDirectory $release } 'already exists'
Reject { & $prepare -CorePackage $fixture -PreviewImage $preview -OutputDirectory (Join-Path $fixture 'nested') } 'outside the source'
$updater = Join-Path $scratch 'update'
& $prepare -CorePackage $fixture -PreviewImage $preview -OutputDirectory $updater -WorkshopId '1234567890'
Check ((& $validate -Workspace (Join-Path $updater 'workshop')).WorkshopId -eq '1234567890') 'update workspace preserves target item ID'
$mock = Join-Path $scratch 'MockUploader.ps1'
@'
param([string]$Verb, [string]$w)
if ($Verb -ne 'upload' -or -not [IO.Path]::IsPathFullyQualified($w)) { throw 'Incorrect uploader invocation.' }
Add-Content -LiteralPath (Join-Path $PSScriptRoot 'uploader-calls.txt') -Value $Verb
$id = Join-Path $w 'mod_id.txt'
if (-not (Test-Path -LiteralPath $id)) { Set-Content -LiteralPath $id -Value '1234567890' }
exit 0
'@ | Set-Content -LiteralPath $mock -Encoding UTF8
$publish = Join-Path $root 'publish-workshop.ps1'
Reject { & $publish -Workspace $workspace -Uploader $mock } 'existing Core Workshop ID'
& $publish -Workspace $workspace -Uploader $mock -CreateNew
Check ((& $validate -Workspace $workspace).WorkshopId -eq '1234567890') 'first upload persists item ID'
Reject { & $publish -Workspace $workspace -Uploader $mock -CreateNew } 'already has a Workshop ID'
& $publish -Workspace $workspace -Uploader $mock
Check (@(Get-Content (Join-Path $scratch 'uploader-calls.txt')).Count -eq 2) 'updates reuse the uploader workspace'
$extra = Join-Path $prepared.Package.Root 'unexpected.log'
Set-Content -LiteralPath $extra -Value 'must not upload'
Reject { & $publish -Workspace $workspace -Uploader $mock } 'unlisted file'
Check (@(Get-Content (Join-Path $scratch 'uploader-calls.txt')).Count -eq 2) 'invalid content rejected before uploader'
Remove-Item -LiteralPath $extra
'exit 7' | Set-Content -LiteralPath $mock -Encoding UTF8
Reject { & $publish -Workspace $workspace -Uploader $mock } 'exit code 7'
Check ((& $validate -Workspace $workspace).WorkshopId -eq '1234567890') 'failed update preserves item ID'
Set-Content -LiteralPath (Join-Path $workspace 'mod_id.txt') -Value ''
Reject { & $validate -Workspace $workspace } 'Invalid mod_id.txt'
Write-Host "Packaging checks passed: $script:checks. Evidence: $scratch"
