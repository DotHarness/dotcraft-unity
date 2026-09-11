param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath("$PSScriptRoot/../..")
$output = [IO.Path]::GetFullPath($OutputDirectory)
$bundle = Join-Path $output 'Unity'
if (!(Test-Path $output -PathType Container)) { throw 'Plugin output parent does not exist.' }
if (Test-Path $bundle) { throw 'Plugin output already exists.' }

& "$root/Tools/DotCraft.Unity.Attach/build-native.ps1"
$version = (Get-Content "$root/Packages/com.dotcraft.unity/package.json" -Raw | ConvertFrom-Json).version
$pluginProject = "$root/Tools/DotCraft.Unity.Plugin/DotCraft.Unity.Plugin.csproj"
& dotnet restore $pluginProject --force-evaluate --no-http-cache
if ($LASTEXITCODE) { throw 'DotCraft adapter restore failed.' }
& dotnet build $pluginProject -c Release --no-restore
if ($LASTEXITCODE) { throw 'DotCraft adapter build failed.' }

New-Item -ItemType Directory -Path "$bundle/lib" -Force | Out-Null
foreach ($name in @('.craft-plugin','assets','skills')) {
    Copy-Item -LiteralPath "$root/Plugins/unity/$name" -Destination $bundle -Recurse
}
New-Item -ItemType Directory -Path "$bundle/skills/unity/scripts" -Force | Out-Null
Copy-Item -LiteralPath "$root/Plugins/dotcraft-unity/skills/dotcraft-unity/scripts/console-read.cs" -Destination "$bundle/skills/unity/scripts/console-read.cs"
$manifestPath = "$bundle/.craft-plugin/plugin.json"
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$manifest.version = $version
$manifest | ConvertTo-Json -Depth 30 | Set-Content $manifestPath -Encoding utf8
$managed = "$root/Tools/DotCraft.Unity.Plugin/bin/Release/net10.0"
foreach ($name in @('DotCraft.Unity.dll','DotCraft.Unity.deps.json','DotCraft.Unity.Attach.dll','Microsoft.CodeAnalysis.dll','Microsoft.CodeAnalysis.CSharp.dll')) {
    Copy-Item -LiteralPath "$managed/$name" -Destination "$bundle/lib"
}
