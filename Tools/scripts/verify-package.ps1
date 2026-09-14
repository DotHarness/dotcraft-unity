param([Parameter(Mandatory)][string]$ArtifactDirectory)
$ErrorActionPreference = 'Stop'
$manifest = Get-Content "$ArtifactDirectory/release-manifest.json" -Raw | ConvertFrom-Json
if ($manifest.harnessPackage.id -cne 'DotCraft.Harness' -or $manifest.harnessPackage.version -notmatch '^\d+\.\d+\.\d+$' -or
    !$manifest.harnessPackage.contentHash -or [Convert]::FromBase64String($manifest.harnessPackage.contentHash).Length -ne 64) { throw 'Invalid Harness package identity.' }
foreach ($entry in $manifest.files) {
    if ($entry.path -match '[/\\]' -or (Get-FileHash "$ArtifactDirectory/$($entry.path)" -Algorithm SHA256).Hash -ine $entry.sha256) { throw 'Release artifact hash mismatch.' }
}
$metadata = & "$ArtifactDirectory/dotcraft-unity.exe" version --json | ConvertFrom-Json
if ($LASTEXITCODE -or $metadata.version -cne $manifest.version -or $metadata.rid -cne 'win-x64' -or $metadata.protocolVersion -ne 1) { throw 'Executable metadata mismatch.' }
$plugin = "$ArtifactDirectory/Unity"
foreach ($entry in $manifest.pluginFiles) {
    $root = [IO.Path]::GetFullPath($plugin)
    $path = [IO.Path]::GetFullPath((Join-Path $root $entry.path))
    if (!$path.StartsWith($root + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or (Get-FileHash $path -Algorithm SHA256).Hash -ine $entry.sha256) { throw 'Plugin content hash mismatch.' }
}
foreach ($name in @('DotCraft.Unity.dll','DotCraft.Unity.Attach.dll','Microsoft.CodeAnalysis.dll','Microsoft.CodeAnalysis.CSharp.dll')) {
    if (!(Test-Path "$plugin/lib/$name")) { throw "Missing private dependency: $name" }
    if (@($manifest.pluginFiles | Where-Object path -CEQ "lib/$name").Count -ne 1) { throw "Private dependency must have exactly one recorded hash: $name" }
}
foreach ($file in Get-ChildItem $plugin -File -Recurse -Force) {
    $relative = [IO.Path]::GetRelativePath([IO.Path]::GetFullPath($plugin),$file.FullName).Replace('\','/')
    if ($relative -cnotin $manifest.pluginFiles.path) { throw "Unrecorded plugin content: $relative" }
}
foreach ($name in @('DotCraft.Harness','DotCraft.Runtime','DotCraft.Core','DotCraft.Agents','DotCraft.Agents.OpenAI','DotCraft.Agents.Anthropic','DotCraft.Generators')) {
    if (Get-ChildItem $plugin -File -Recurse -Filter "$name.dll") { throw "Host assembly was bundled: $name" }
}
Write-Output 'Package integrity verified.'
