param(
    [string]$StableGameDir = $env:SHOUJO_STABLE_GAME,
    [string]$BetaGameDir = $env:SHOUJO_BETA_GAME,
    [string]$Godot = $env:GODOT_BIN,
    [string]$TelemetryEndpointPath = $env:SHOUJO_TELEMETRY_ENDPOINT_PATH,
    [string]$OutputDirectory = '',
    [string]$Version = '1.9.3'
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not $TelemetryEndpointPath) {
    $localEndpoint = Join-Path $root 'artifacts/private/CoreTelemetryEndpoint.json'
    if (Test-Path -LiteralPath $localEndpoint -PathType Leaf) { $TelemetryEndpointPath = $localEndpoint }
}
if ($TelemetryEndpointPath -and -not (Test-Path -LiteralPath $TelemetryEndpointPath -PathType Leaf)) { throw 'TelemetryEndpointPath must identify an existing endpoint configuration.' }
if (-not $StableGameDir -or -not $BetaGameDir -or -not $Godot) { throw 'Provide StableGameDir, BetaGameDir and Godot, or their documented environment variables.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'dist/ShoujoKagekiAijoCore' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$staging = Join-Path $root ('artifacts/package-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $staging | Out-Null
New-Item -ItemType File -Force -Path (Join-Path $root 'artifacts/.gdignore') | Out-Null
function Build-Core([string]$project, [string]$game, [string]$api) {
    $arguments = @('build', (Join-Path $root $project), '-c', 'ExportRelease', '--nologo', '-v:q', "-p:GameDir=$game", "-p:Sts2Api=$api", "-p:CoreVersion=$Version", "-p:GodotProjectDir=$root/")
    if ($TelemetryEndpointPath) { $arguments += "-p:TelemetryEndpointPath=$TelemetryEndpointPath" }
    & dotnet @arguments
    if ($LASTEXITCODE) { throw "Core $api build failed." }
}
$lib = Join-Path $staging "lib/$Version"
foreach ($api in @('stable','beta')) {
    $game = if ($api -eq 'stable') { $StableGameDir } else { $BetaGameDir }
    Build-Core 'src/Runtime/ShoujoKagekiAijoCore.Runtime.csproj' $game $api
    $name = if ($api -eq 'stable') { 'Stable' } else { 'Beta' }
    New-Item -ItemType Directory -Force -Path (Join-Path $lib $api) | Out-Null
    Copy-Item -LiteralPath (Join-Path $root ".godot/mono/temp/bin/ExportRelease/ShoujoKagekiAijoCore.Runtime.$name.dll") -Destination (Join-Path $lib $api)
    if ($api -eq 'stable') {
        New-Item -ItemType Directory -Force -Path (Join-Path $lib 'api') | Out-Null
        Copy-Item -LiteralPath (Join-Path $root 'src/Api.V1/bin/ExportRelease/net9.0/ShoujoKagekiAijoCore.Api.V1.dll') -Destination (Join-Path $lib 'api')
    }
}
Build-Core 'src/Bootstrap/ShoujoKagekiAijoCore.Bootstrap.csproj' $StableGameDir 'stable'
Copy-Item -LiteralPath (Join-Path $root 'src/Bootstrap/bin/ExportRelease/net9.0/ShoujoKagekiAijoCore.dll') -Destination $staging
$manifest = Get-Content -LiteralPath (Join-Path $root 'ShoujoKagekiAijoCore.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$manifest.version = $Version
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $staging 'ShoujoKagekiAijoCore.json') -Encoding UTF8
New-Item -ItemType Directory -Force -Path (Join-Path $staging "resources/$Version"), (Join-Path $staging 'sdk') | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'sdk/pack-character.ps1'), (Join-Path $root 'sdk/validate-package.ps1'), (Join-Path $root 'sdk/CharacterLoader.cs') -Destination (Join-Path $staging 'sdk')
Copy-Item -LiteralPath (Join-Path $root 'src/Bootstrap/Bundle.cs') -Destination (Join-Path $staging 'sdk/BundleValidation.cs')
$import = Start-Process -FilePath $Godot -ArgumentList '--headless','--editor','--path',('"'+$root+'"'),'--import','--quit' -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput (Join-Path $root 'artifacts/import.log') -RedirectStandardError (Join-Path $root 'artifacts/import-errors.log')
if ($import.ExitCode) { throw 'Godot resource import failed. See artifacts/import-errors.log.' }
$pack = Join-Path $staging "resources/$Version/Core.Resources.pck"
& $Godot --headless --path $root --script tools/pack.gd -- $pack
if ($LASTEXITCODE -or -not (Test-Path -LiteralPath $pack)) { throw 'Core resource packing failed.' }
$files = [ordered]@{}
Get-ChildItem -LiteralPath $staging -Recurse -File | Sort-Object FullName | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($staging, $_.FullName).Replace('\','/')
    $files[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}
$bundle = [ordered]@{
    format=1; bootstrapProtocol=1; releaseVersion=$Version; apiMajor=1; apiVersion='1.9.0'; apiAssembly="lib/$Version/api/ShoujoKagekiAijoCore.Api.V1.dll"
    baseLibMinimumVersion='3.4.8'; resourceVersion=$Version; resourcePack="resources/$Version/Core.Resources.pck"; saveProtocol=2; networkProtocol=2
    features=@('player-state-events@1','card-clone-events@1','model-method-scopes@1','shine-reward-pools@1','shine-rewards@1','card-staging@1','card-payloads@1','player-choices@1','card-picking@1','card-upgrades@1','music-overrides@1','shine@1','diagnostics@1','music@1','configuration@1','card-play-routing@1','card-effects@1','card-execution@1','card-options@1','effect-registration@1','movement-pipeline@1','model-execution@1','card-copies@1','card-snapshots@1','card-selection@1','presentation@1','temporary-music@1') + @(if (Test-Path (Join-Path $root 'src/Modules/Telemetry')) { 'telemetry@1' }) + @(if (Test-Path (Join-Path $root 'src/Modules/Exports')) { 'card-export@1' })
    targets=[ordered]@{
        stable=[ordered]@{ minimumGameVersion='0.107.1'; maximumGameVersion='0.107.1'; assembly="lib/$Version/stable/ShoujoKagekiAijoCore.Runtime.Stable.dll" }
        beta=[ordered]@{ minimumGameVersion='0.113.0'; maximumGameVersion='0.113.0'; assembly="lib/$Version/beta/ShoujoKagekiAijoCore.Runtime.Beta.dll" }
    }
    files=$files
}
$bundle | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $staging 'core.bundle') -Encoding UTF8
$null = & (Join-Path $root 'sdk/validate-package.ps1') -CorePackage $staging
if (Test-Path -LiteralPath $output) { throw 'Output directory already exists. Select a fresh OutputDirectory.' }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $output) | Out-Null
Move-Item -LiteralPath $staging -Destination $output
Write-Host "Core package $output"
