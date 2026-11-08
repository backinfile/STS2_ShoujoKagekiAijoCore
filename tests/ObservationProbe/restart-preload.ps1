param([ValidatePattern('^[a-z0-9-]+$')][string]$Name = 'sample', [switch]$Draw, [ValidatePattern('^[a-z0-9-]+$')][string]$Instance = 'preload-baseline')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$base = Join-Path $root ('out/tmp/core-observations/' + $Instance)
$instancePath = Join-Path $base 'instance.json'
$instanceInfo = Get-Content -LiteralPath $instancePath -Raw -Encoding UTF8 | ConvertFrom-Json
$process = if ($instanceInfo.pid -gt 0) { Get-Process -Id $instanceInfo.pid -ErrorAction SilentlyContinue } else { $null }
if ($process) {
    if ([IO.Path]::GetFullPath($process.Path) -ne [IO.Path]::GetFullPath($instanceInfo.exe)) { throw 'Different process path' }
    Stop-Process -Id $process.Id
    if (!$process.WaitForExit(10000)) { throw 'Test process did not exit' }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '.godot/mono/temp/bin/Debug/CoreObservationProbe.dll') -Destination (Join-Path (Split-Path -Parent $instanceInfo.exe) 'mods') -Force
$previousApp = $env:APPDATA; $previousLocal = $env:LOCALAPPDATA; $previousDraw = $env:CORE_OBSERVATION_DRAW
try {
    $env:APPDATA = Join-Path $base 'user/AppData/Roaming'
    $env:LOCALAPPDATA = Join-Path $base 'user/AppData/Local'
    $env:CORE_OBSERVATION_DRAW = if ($Draw) { '1' } else { '0' }
    $process = Start-Process -FilePath $instanceInfo.exe -WorkingDirectory (Split-Path -Parent $instanceInfo.exe) -WindowStyle Hidden -PassThru -ArgumentList '--force-steam=off','--rendering-method','gl_compatibility','--log-file',('"' + (Join-Path $base ($Name + '.log')) + '"')
} finally {
    $env:APPDATA = $previousApp; $env:LOCALAPPDATA = $previousLocal; $env:CORE_OBSERVATION_DRAW = $previousDraw
}
$instanceInfo.pid = $process.Id
$instanceInfo | ConvertTo-Json | Set-Content -LiteralPath $instancePath -Encoding UTF8
[pscustomobject]@{Pid=$process.Id;Port=$instanceInfo.port;Sample=$Name;Draw=[bool]$Draw} | ConvertTo-Json -Compress
