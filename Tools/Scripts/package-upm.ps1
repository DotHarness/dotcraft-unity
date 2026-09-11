param([Parameter(Mandatory)][string]$OutputDirectory,[Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath("$PSScriptRoot/../..")
$packageRoot = Join-Path $root 'Packages/com.dotcraft.unity'
$staging = Join-Path $OutputDirectory 'upm'
New-Item -ItemType Directory -Path $staging | Out-Null
foreach ($name in @('package.json','package.json.meta','Editor','Editor.meta','Tests','Tests.meta','LICENSE','LICENSE.meta')) {
    Copy-Item -LiteralPath "$packageRoot/$name" -Destination $staging -Recurse
}
if (Get-ChildItem -LiteralPath $staging -Recurse -File | Where-Object { $_.Name -like 'Microsoft.CodeAnalysis*' -or $_.FullName -match '[\\/]Roslyn[\\/]' }) {
    throw 'The public UPM package must not contain Roslyn compiler assemblies.'
}
Compress-Archive -Path "$staging/*" -DestinationPath "$OutputDirectory/com.dotcraft.unity-$Version.zip"
