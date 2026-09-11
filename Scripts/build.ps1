$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath("$PSScriptRoot/..")
$output = [IO.Path]::GetFullPath((Join-Path $root 'Build'))
if (Test-Path $output) { Remove-Item -LiteralPath $output -Recurse -Force }
New-Item -ItemType Directory -Path $output | Out-Null
& "$root/Tools/Scripts/build-unity-plugin.ps1" -OutputDirectory $output
Write-Output "$output/Unity"
