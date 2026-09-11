param([Parameter(Mandatory)][string]$ProjectDirectory)
$ErrorActionPreference = 'Stop'
$assets = Get-Content "$ProjectDirectory/obj/project.assets.json" -Raw | ConvertFrom-Json
$packages = @($assets.libraries.PSObject.Properties | Where-Object Name -Like 'DotCraft.Harness/*')
if ($packages.Count -ne 1) { throw 'Expected exactly one restored DotCraft.Harness package.' }
$version = $packages[0].Name.Split('/')[1]
$hash = $packages[0].Value.sha512
if (!$hash -or [Convert]::FromBase64String($hash).Length -ne 64) { throw 'Missing NuGet package content hash.' }
[pscustomobject]@{ id = 'DotCraft.Harness'; version = $version; contentHash = $hash }
