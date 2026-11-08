param(
    [ValidatePattern('^[a-z0-9-]+$')][string]$Instance = 'preload-baseline',
    [ValidatePattern('^[a-z0-9-]+$')][string]$Name = 'startup',
    [int]$ExpectedAssets = 0,
    [switch]$Offline
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$base = Join-Path $root ('out/tmp/core-observations/' + $Instance)
$text = Get-Content -LiteralPath (Join-Path $base ($Name + '.log')) -Raw -Encoding UTF8
if ($text -match 'Invalid Task ID|NullReferenceException|(?m)^ERROR:|(?m)^\[ERROR\]') { throw 'Targeted preload regression failed; inspect the original log.' }
$complete = [regex]::Match($text, "Preloading 'Common' Complete: assets=(\d+)")
if (!$complete.Success) { throw 'Common preload has not completed.' }
$assets = [int]$complete.Groups[1].Value
if ($ExpectedAssets -gt 0 -and $assets -ne $ExpectedAssets) { throw 'Preload resource count changed.' }
if (!$Offline) {
    $info = Get-Content -LiteralPath (Join-Path $base 'instance.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $state = Invoke-RestMethod -Uri ("http://127.0.0.1:" + $info.port + '/api/v1/singleplayer?format=json') -TimeoutSec 10
    if ($state.state_type -ne 'menu') { throw 'Expected an operable startup menu.' }
}
[pscustomobject]@{Result='PASS';Assets=$assets;Instance=$Instance;Sample=$Name} | ConvertTo-Json -Compress
