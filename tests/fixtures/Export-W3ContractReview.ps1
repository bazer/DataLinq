[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Manifest,
    [Parameter(Mandatory)][string]$Output
)

# A readable projection of the captured compiled contract. Keep the original JSON
# and ApiCompat metadata snapshots as the authoritative detailed inputs.
$ErrorActionPreference = 'Stop'
$contract = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
$lines = [Collections.Generic.List[string]]::new()
function Json($value) { ConvertTo-Json -InputObject $value -Depth 25 -Compress }
function NullableShape($value) {
    $text = $value.Read
    if ($value.Write -ne $value.Read) { $text += "/$($value.Write)" }
    if ($value.GenericArguments.Count) { $text += '<' + (($value.GenericArguments | ForEach-Object { NullableShape $_ }) -join ',') + '>' }
    if ($null -ne $value.Element) { $text += '[' + (NullableShape $value.Element) + ']' }
    return $text
}
function Parameter($value) {
    $text = "$($value.Type) $($value.Name)"
    if ($value.IsOptional) { $text += ' optional' }
    if ($value.HasDefaultValue) { $text += ' default=' + (Json $value.DefaultValue) }
    $text += ' nullable=' + (NullableShape $value.Nullability)
    if ($value.MetadataAttributes.Count) { $text += ' metadata=' + (Json $value.MetadataAttributes) }
    return $text
}
$lines.Add('# Compiled W3 contract review projection')
$lines.Add('# Includes selected public/protected async families, their type/constructor/property contracts, diagnostics and generated examples.')
$lines.Add('# Assembly identities and exact byte hashes are in the adjacent evidence record; this projection omits build-specific identities.')
$lines.Add('# Nullable states describe the captured runtime reflection result; raw nullable attributes remain in JSON/ApiCompat snapshots.')
foreach ($type in $contract.Types) {
    $lines.Add('')
    $lines.Add("type $($type.Name) [$($type.Attributes)] base=$($type.BaseType)")
    $lines.Add('  interfaces=' + (Json $type.Interfaces))
    $lines.Add('  generic=' + (Json $type.GenericParameters))
    $lines.Add('  metadata=' + (Json $type.MetadataAttributes))
    foreach ($constructor in $type.Constructors) {
        $lines.Add("  constructor [$($constructor.Attributes)]")
        foreach ($parameter in $constructor.Parameters) { $lines.Add('    ' + (Parameter $parameter)) }
    }
    foreach ($method in $type.Methods) {
        $lines.Add("  method $($method.Name) [$($method.Attributes)] body=$($method.HasBody)")
        $lines.Add('    returns ' + (Parameter $method.Return))
        if ($method.GenericParameters.Count) { $lines.Add('    generic=' + (Json $method.GenericParameters)) }
        foreach ($parameter in $method.Parameters) { $lines.Add('    parameter ' + (Parameter $parameter)) }
        if ($method.MetadataAttributes.Count) { $lines.Add('    metadata=' + (Json $method.MetadataAttributes)) }
    }
    foreach ($property in $type.Properties) {
        $lines.Add("  property $($property.Type) $($property.Name) nullable=$(NullableShape $property.Nullability) get=[$($property.Getter)] set=[$($property.Setter)]")
        if ($property.SetterReturnModifiers.Count) { $lines.Add('    setter-return-modifiers=' + (Json $property.SetterReturnModifiers)) }
        if ($property.MetadataAttributes.Count) { $lines.Add('    metadata=' + (Json $property.MetadataAttributes)) }
    }
    foreach ($value in $type.EnumValues) { $lines.Add("  enum $($value.Name)=$($value.Value)") }
}
[IO.File]::WriteAllLines([IO.Path]::GetFullPath($Output), $lines, [Text.UTF8Encoding]::new($false))
Write-Output "Exported $($contract.Types.Count) compiled types to $Output"
