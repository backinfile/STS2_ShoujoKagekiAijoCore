param([Parameter(Mandatory)][string]$CorePackage)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($CorePackage).TrimEnd([IO.Path]::DirectorySeparatorChar)
$bundle = Get-Content -LiteralPath (Join-Path $root 'core.bundle') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($bundle.format -ne 1 -or $bundle.bootstrapProtocol -ne 1 -or $bundle.apiMajor -ne 1) { throw 'Unsupported Core package protocol.' }
if ($bundle.resourceVersion -ne $bundle.releaseVersion) { throw 'Core code/resource release mismatch.' }
$required = @($bundle.apiAssembly, $bundle.resourcePack, 'ShoujoKagekiAijoCore.dll', 'ShoujoKagekiAijoCore.json')
$required += @($bundle.targets.PSObject.Properties | ForEach-Object { $_.Value.assembly })
foreach ($relative in $required) {
    if (-not $bundle.files.PSObject.Properties[$relative]) { throw "Core package has no digest for $relative" }
}
foreach ($property in $bundle.files.PSObject.Properties) {
    if ([IO.Path]::IsPathRooted($property.Name)) { throw 'Core file paths must be relative.' }
    $path = [IO.Path]::GetFullPath((Join-Path $root $property.Name))
    if (-not $path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Core file path escapes the package.' }
    $current = $path
    while ($current -ne $root) {
        $item = Get-Item -LiteralPath $current
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Core package contains a link.' }
        $current = Split-Path -Parent $current
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $property.Value) { throw "Core file checksum mismatch for $($property.Name)" }
}
$manifest = Get-Content -LiteralPath (Join-Path $root 'ShoujoKagekiAijoCore.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.id -ne 'ShoujoKagekiAijoCore' -or $manifest.version -ne $bundle.releaseVersion) { throw 'Core manifest and bundle release identity differ.' }
$api = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $root $bundle.apiAssembly))
if ($api.Name -ne 'ShoujoKagekiAijoCore.Api.V1' -or $api.Version -ne [Version]'1.0.0.0') { throw 'Unexpected Core API identity.' }
[pscustomobject]@{ Root = $root; Bundle = $bundle }
