param(
    [Parameter(Mandatory)][string]$GameDir,
    [Parameter(Mandatory)][string]$CorePackage,
    [Parameter(Mandatory)][string]$McpDirectory,
    [string[]]$AdditionalMods = @(),
    [ValidatePattern('^[a-z0-9-]+$')][string]$Instance = 'preload-baseline'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$base = Join-Path $root ('out/tmp/core-observations/' + $Instance)
$game = Join-Path $base 'game'
$user = Join-Path $base 'user'
if (Test-Path -LiteralPath (Join-Path $base 'instance.json')) { throw 'Use a fresh instance directory or the recorded restart script.' }
$null = & (Join-Path $root 'sdk/validate-package.ps1') -CorePackage $CorePackage -Strict
New-Item -ItemType Directory -Force -Path $game,(Join-Path $game 'mods'),(Join-Path $user 'AppData/Roaming/SlayTheSpire2/default/1'),(Join-Path $user 'AppData/Roaming/SlayTheSpire2/mod_configs') | Out-Null
foreach ($file in Get-ChildItem -LiteralPath $GameDir -File) {
    if ($file.Name -ne 'override.cfg') { New-Item -ItemType HardLink -Path (Join-Path $game $file.Name) -Target $file.FullName | Out-Null }
}
New-Item -ItemType Junction -Path (Join-Path $game 'data_sts2_windows_x86_64') -Target (Join-Path $GameDir 'data_sts2_windows_x86_64') | Out-Null
Copy-Item -LiteralPath (Join-Path $GameDir 'mods/BaseLib') -Destination (Join-Path $game 'mods') -Recurse
Copy-Item -LiteralPath $CorePackage -Destination (Join-Path $game 'mods/ShoujoKagekiAijoCore') -Recurse
foreach ($mod in $AdditionalMods) { Copy-Item -LiteralPath $mod -Destination (Join-Path $game 'mods') -Recurse }
Copy-Item -LiteralPath (Join-Path $McpDirectory 'KarenSTS2MCP.dll'),(Join-Path $McpDirectory 'KarenSTS2MCP.json') -Destination (Join-Path $game 'mods')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CoreObservationProbe.json'),(Join-Path $PSScriptRoot '.godot/mono/temp/bin/Debug/CoreObservationProbe.dll') -Destination (Join-Path $game 'mods')
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop()
@{port=$port;fullscreen=$false;muted=$false;silent_test=$true;skip_tutorial=$true;skip_first_prompt=$true;window_width=1280;window_height=720} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $game 'mods/KarenSTS2MCP.conf') -Encoding UTF8
@{schema_version=8;mod_settings=@{mods_enabled=$true;mod_list=@()};fullscreen=$false;seen_ea_disclaimer=$true;skip_intro_logo=$true;language='zhs';limit_fps_in_background=$false;volume_master=0.5;window_size=@{X=1280;Y=720}} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $user 'AppData/Roaming/SlayTheSpire2/default/1/settings.save') -Encoding UTF8
@{EnableDataUpload='False';EnableErrorUpload='True';TelemetryBaseUrl='http://127.0.0.1:1/core-observations'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $user 'AppData/Roaming/SlayTheSpire2/mod_configs/ShoujoKagekiAijoCore.cfg') -Encoding UTF8
"[display]`nwindow/size/mode=1`nwindow/size/no_focus=true`nwindow/size/viewport_width=1280`nwindow/size/viewport_height=720`n" | Set-Content -LiteralPath (Join-Path $game 'override.cfg') -Encoding UTF8
@{pid=0;port=$port;exe=(Join-Path $game 'SlayTheSpire2.exe')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $base 'instance.json') -Encoding UTF8
& (Join-Path $PSScriptRoot 'restart-preload.ps1') -Instance $Instance -Name startup -Draw
