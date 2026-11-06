param(
    [Parameter(Mandatory)][string]$Assembly,
    [Parameter(Mandatory)][string]$DnlibPath,
    [Parameter(Mandatory)][string]$Output
)
$ErrorActionPreference = 'Stop'
Add-Type -LiteralPath $DnlibPath
$module = [dnlib.DotNet.ModuleDefMD]::Load((Resolve-Path -LiteralPath $Assembly).Path)
try {
    $types = @($module.GetTypes())
    $cardTypes = @{}
    $rarities = @{}
    foreach ($type in $types) {
        if ($type.FullName -in @('MegaCrit.Sts2.Core.Entities.Cards.CardType','MegaCrit.Sts2.Core.Entities.Cards.CardRarity')) {
            $values = if ($type.Name.String -eq 'CardType') { $cardTypes } else { $rarities }
            foreach ($field in $type.Fields) { if ($field.HasConstant) { $values[[int]$field.Constant.Value] = $field.Name.String } }
        }
    }
    $owners = @{}
    foreach ($type in $types) {
        if ($type.Namespace.String -ne 'MegaCrit.Sts2.Core.Models.CardPools') { continue }
        foreach ($method in $type.Methods) {
            if ($method.Name.String -ne 'GenerateAllCards' -or -not $method.HasBody) { continue }
            foreach ($instruction in $method.Body.Instructions) {
                if ($instruction.Operand -and $instruction.Operand.ToString() -match '::Card<([^>]+)>') {
                    $name = $Matches[1].Split('.')[-1]
                    if (-not $owners.ContainsKey($name)) { $owners[$name] = @() }
                    $owners[$name] += $type.Name.String.Replace('CardPool','')
                }
            }
        }
    }
    $cards = @{}
    foreach ($type in $types) {
        if ($type.Namespace.String -ne 'MegaCrit.Sts2.Core.Models.Cards' -or $type.IsAbstract) { continue }
        $name = $type.Name.String
        $constants = @()
        $found = $false
        foreach ($method in $type.Methods) {
            if ($method.Name.String -ne '.ctor' -or -not $method.HasBody) { continue }
            $values = @()
            foreach ($instruction in $method.Body.Instructions) {
                if ($instruction.IsLdcI4()) { $values += $instruction.GetLdcI4Value() }
                if ($instruction.Operand -and $instruction.Operand.ToString() -like '*Models.CardModel::.ctor*') {
                    $constants = $values
                    $found = $values.Length -ge 3
                    break
                }
            }
            if ($found) { break }
        }
        if (-not $found) { throw "Cannot derive native card constructor: $name" }
        $id = [regex]::Replace($name,'(?<!^)(?=[A-Z][a-z])|(?<=[a-z0-9])(?=[A-Z])','_').ToUpperInvariant()
        $cards[$id] = @{ name = $name; cost = $constants[0]; type = $cardTypes[[int]$constants[1]]; rarity = $rarities[[int]$constants[2]]; pools = @($owners[$name] | Select-Object -Unique) }
    }
    $parent = Split-Path -Parent ([System.IO.Path]::GetFullPath($Output))
    [System.IO.Directory]::CreateDirectory($parent) | Out-Null
    $json = ConvertTo-Json -InputObject $cards -Depth 6
    [System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($Output),$json,[System.Text.UTF8Encoding]::new($false))
    Write-Output ("Exported {0} native card definitions." -f $cards.Count)
}
finally { $module.Dispose() }
