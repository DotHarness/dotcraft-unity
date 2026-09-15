param([string]$OutputDirectory = "$PSScriptRoot/../../.artifacts/release")
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath("$PSScriptRoot/../..")
$commit = (& git -C $root rev-parse HEAD).Trim()
$sourceDirty = [bool](& git -C $root status --porcelain --untracked-files=normal)
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path $output) { throw 'Use a new output directory to prevent stale release files.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$version = (Get-Content "$root/Packages/com.dotcraft.unity/package.json" -Raw | ConvertFrom-Json).version
& "$PSScriptRoot/build-unity-plugin.ps1" -OutputDirectory $output
$harnessPackage = & "$PSScriptRoot/get-harness-package.ps1" -ProjectDirectory "$root/Tools/src/DotCraft.Unity.Plugin"
& dotnet publish "$root/Tools/src/DotCraft.Unity.Cli/DotCraft.Unity.Cli.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o "$output/cli"
if ($LASTEXITCODE) { throw 'Standalone publish failed.' }
$bundle = "$output/Unity"
Copy-Item "$output/cli/dotcraft-unity.exe" "$output/dotcraft-unity.exe"
Copy-Item "$root/Tools/scripts/install.ps1" "$output/install.ps1"
@{version=$version;rid='win-x64';fileName='dotcraft-unity.exe';sha256=(Get-FileHash "$output/dotcraft-unity.exe" -Algorithm SHA256).Hash.ToLowerInvariant()} | ConvertTo-Json | Set-Content "$output/artifact.json" -Encoding utf8
Compress-Archive -Path "$bundle/*","$bundle/.craft-plugin" -DestinationPath "$output/Unity-$version.zip"
& "$PSScriptRoot/package-upm.ps1" -OutputDirectory $output -Version $version
$pluginFiles = @(Get-ChildItem $bundle -Recurse -File -Force | ForEach-Object { @{path=[IO.Path]::GetRelativePath($bundle,$_.FullName).Replace('\','/');sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()} })
@{schemaVersion=1;version=$version;commit=$commit;sourceDirty=$sourceDirty;platform='win-x64';harnessPackage=$harnessPackage;pluginFiles=$pluginFiles;files=@(Get-ChildItem $output -File | ForEach-Object { @{path=$_.Name;sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()} })} | ConvertTo-Json -Depth 30 | Set-Content "$output/release-manifest.json" -Encoding utf8
& "$PSScriptRoot/verify-package.ps1" -ArtifactDirectory $output
