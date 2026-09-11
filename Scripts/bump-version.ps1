[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw 'Version is required. Example: 0.4.2'
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Invalid version '$Version'. Expected format: X.Y.Z"
}

$root = [IO.Path]::GetFullPath("$PSScriptRoot/..")
$targets = @(
    'Packages/com.dotcraft.unity/package.json'
    'Plugins/unity/.craft-plugin/plugin.json'
    'Plugins/dotcraft-unity/.craft-plugin/plugin.json'
    'Plugins/dotcraft-unity/.codex-plugin/plugin.json'
)
$versionPattern = [regex]::new('(?m)^(\s*"version"\s*:\s*")[^"]+(".*)$')
$updates = foreach ($relativePath in $targets) {
    $path = Join-Path $root $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Version file not found: $relativePath"
    }

    $content = [IO.File]::ReadAllText($path)
    $matches = $versionPattern.Matches($content)
    if ($matches.Count -ne 1) {
        throw "Expected exactly one version property in $relativePath"
    }

    $updated = $versionPattern.Replace($content, {
        param($match)
        $match.Groups[1].Value + $Version + $match.Groups[2].Value
    }, 1)

    [pscustomobject]@{
        RelativePath = $relativePath
        Path = $path
        Content = $updated
    }
}

$utf8NoBom = [Text.UTF8Encoding]::new($false)
foreach ($update in $updates) {
    [IO.File]::WriteAllText($update.Path, $update.Content, $utf8NoBom)
    Write-Output "Updated $($update.RelativePath) -> $Version"
}

foreach ($update in $updates) {
    $manifest = Get-Content -LiteralPath $update.Path -Raw | ConvertFrom-Json
    if ($manifest.version -cne $Version) {
        throw "Version verification failed: $($update.RelativePath)"
    }
}
