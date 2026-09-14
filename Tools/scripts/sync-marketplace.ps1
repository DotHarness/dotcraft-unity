param([Parameter(Mandatory)][string]$ArtifactDirectory, [Parameter(Mandatory)][string]$MarketplaceRoot)
$ErrorActionPreference = 'Stop'
& "$PSScriptRoot/verify-package.ps1" -ArtifactDirectory $ArtifactDirectory
$root = [IO.Path]::GetFullPath($MarketplaceRoot)
$target = [IO.Path]::GetFullPath((Join-Path $root 'plugins/DotCraft.Unity'))
if (!$target.StartsWith($root + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid marketplace target.' }
if (Test-Path $target) { Remove-Item -LiteralPath $target -Recurse -Force }
Copy-Item -LiteralPath "$ArtifactDirectory/Unity" -Destination $target -Recurse
Copy-Item -LiteralPath "$ArtifactDirectory/release-manifest.json" -Destination "$target/release-provenance.json"
$path = "$root/.craft/plugins/marketplace.json"
$index = Get-Content $path -Raw | ConvertFrom-Json
$entry = [pscustomobject]@{name='DotCraft.Unity';source=@{source='local';path='./plugins/DotCraft.Unity'};policy=@{installation='AVAILABLE';authentication='ON_INSTALL'};category='Engineering'}
$index.plugins = @($index.plugins | Where-Object name -cne 'DotCraft.Unity') + $entry
$index | ConvertTo-Json -Depth 30 | Set-Content $path -Encoding utf8NoBOM
& node "$root/scripts/validate-registry.mjs"
if ($LASTEXITCODE) { throw 'Marketplace registry validation failed.' }
