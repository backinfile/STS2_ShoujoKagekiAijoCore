param([Parameter(Mandatory)][string]$CorePackage, [switch]$Strict)
$ErrorActionPreference = 'Stop'
# The packaged SDK carries the exact source compiled into the bootstrap.
$source = Join-Path $PSScriptRoot 'BundleValidation.cs'
if (-not (Test-Path -LiteralPath $source)) { $source = Join-Path $PSScriptRoot '../src/Bootstrap/Bundle.cs' }
if (-not ('ShoujoKageki.Core.Bootstrap.BundleSelection' -as [type])) {
    Add-Type -Path $source -CompilerOptions '/langversion:preview'
}
$root = [IO.Path]::GetFullPath($CorePackage)
$candidate = if ($Strict) {
    [ShoujoKageki.Core.Bootstrap.BundleSelection]::ValidateStrict($root, 'SDK')
} else {
    [ShoujoKageki.Core.Bootstrap.BundleSelection]::Validate($root, 'SDK')
}
# Preserve the SDK's existing PowerShell object contract for packaging callers.
$json = [System.Text.Json.JsonSerializer]::Serialize($candidate.Bundle, $candidate.Bundle.GetType(), [ShoujoKageki.Core.Bootstrap.BundleSelection]::JsonOptions)
[pscustomobject]@{ Root = $candidate.Root; Bundle = ($json | ConvertFrom-Json) }
