[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$ArtifactDirectory)

$ErrorActionPreference = 'Stop'
$artifactRoot = (Resolve-Path -LiteralPath $ArtifactDirectory).Path
$installer = Join-Path $PSScriptRoot '../../install.ps1'
. $installer
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('dotcraft-unity-installer-test-' + [Guid]::NewGuid().ToString('N'))
$testInstall = Join-Path $tempRoot 'bin with spaces'
$originalProcessPath = $env:Path
$existingTools = Join-Path $tempRoot 'existing-tools'
$script:mockUserPath = $existingTools
$script:pathWrites = 0
$script:failure = ''
$exeFixture = Join-Path $artifactRoot 'dotcraft-unity.exe'
$script:releaseVersion = (Get-DcuExecutableVersion $exeFixture).version
$script:manifest = [pscustomobject]@{
    version = $script:releaseVersion
    rid = 'win-x64'
    fileName = 'dotcraft-unity.exe'
    sha256 = (Get-FileHash -LiteralPath $exeFixture -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "Assertion failed: $Message" }
}
function Get-DcuUserPath { $script:mockUserPath }
function Set-DcuUserPath([string]$Value) { $script:mockUserPath = $Value; $script:pathWrites++ }
function Invoke-RestMethod($Uri, $Headers) {
    if ($script:failure -eq 'bootstrap') { throw 'BootstrapReachedDownload' }
    if ($Uri.EndsWith('/latest')) { return [pscustomobject]@{ tag_name = "v$script:releaseVersion" } }
    Assert-True ($Uri.EndsWith('/gateway-artifact.json')) 'Unexpected manifest request'
    if ($script:failure -eq 'wrongVersion') {
        return [pscustomobject]@{ version = '0.0.0'; rid = 'win-x64'; fileName = 'dotcraft-unity.exe'; sha256 = $script:manifest.sha256 }
    }
    return $script:manifest
}
function Invoke-WebRequest($Uri, $OutFile, $Headers) {
    if ($script:failure -eq 'failDownload') { throw 'Simulated download failure.' }
    if ($Uri.EndsWith('/dotcraft-unity.exe')) {
        Copy-Item -LiteralPath $exeFixture -Destination $OutFile
        if ($script:failure -eq 'corruptDownload') { [IO.File]::AppendAllText($OutFile, 'corruption') }
    }
    elseif ($Uri.EndsWith('/THIRD-PARTY-NOTICES.txt')) {
        [IO.File]::WriteAllText($OutFile, 'fixture notices')
    }
    else { throw "Unexpected download: $Uri" }
}

try {
    [IO.Directory]::CreateDirectory($testInstall) | Out-Null
    $unrelated = Join-Path $testInstall 'dotcraft.exe'
    [IO.File]::WriteAllText($unrelated, 'another tool')
    Install-DotCraftUnity -RequestedVersion latest -Destination $testInstall
    $installedExe = Join-Path $testInstall 'dotcraft-unity.exe'
    Assert-True ((Get-FileHash -LiteralPath $installedExe).Hash -ieq $script:manifest.sha256) 'Installed bytes'
    Assert-True ((Get-DcuExecutableVersion $installedExe).version -eq $script:releaseVersion) 'Installed executable runs'
    Assert-True ($script:pathWrites -eq 1) 'First install adds user PATH'
    Assert-True (($env:Path -split ';') -contains $testInstall) 'Current process PATH updated'
    Assert-True ([IO.File]::ReadAllText($unrelated) -eq 'another tool') 'Other CLI remains untouched'

    Install-DotCraftUnity -RequestedVersion $script:releaseVersion -Destination $testInstall
    Assert-True ($script:pathWrites -eq 1) 'Repeated installation does not duplicate PATH'
    $script:mockUserPath = $existingTools + ';' + $testInstall.ToUpperInvariant() + '\'
    $env:Path = $originalProcessPath
    Add-DcuUserPath $testInstall
    Assert-True ($script:pathWrites -eq 1) 'PATH comparison ignores casing and trailing slash'
    Assert-True (($env:Path -split ';') -contains $testInstall) 'Refresh process PATH even if user PATH already contains directory'

    foreach ($scenario in @('wrongVersion', 'corruptDownload', 'failDownload')) {
        $script:failure = $scenario
        $failed = $false
        try { Install-DotCraftUnity -RequestedVersion latest -Destination $testInstall }
        catch { $failed = $true }
        $script:failure = ''
        Assert-True $failed "$scenario is rejected"
        Assert-True ((Get-FileHash -LiteralPath $installedExe).Hash -ieq $script:manifest.sha256) "$scenario preserves old executable"
        Assert-True ($script:pathWrites -eq 1) "$scenario does not change user PATH"
    }
    $script:manifest.version = '999.0.0'
    $failed = $false
    try { Install-DotCraftUnity -RequestedVersion '999.0.0' -Destination $testInstall }
    catch { $failed = $_.Exception.Message -like '*Executable version metadata*' }
    Assert-True $failed 'Hash-valid executable with mismatching version metadata is rejected'
    Assert-True ((Get-FileHash -LiteralPath $installedExe).Hash -ieq $script:manifest.sha256) 'Metadata mismatch preserves old executable'
    $script:manifest.version = $script:releaseVersion
    Assert-True (@(Get-ChildItem -LiteralPath $testInstall -Force -Directory).Count -eq 0) 'Temporary download directories cleaned'

    $script:failure = 'bootstrap'
    $bootstrapReached = $false
    try { Get-Content -LiteralPath $installer -Raw | Invoke-Expression }
    catch { $bootstrapReached = $_.Exception.Message -eq 'BootstrapReachedDownload' }
    Assert-True $bootstrapReached 'Piped installer invokes its entry point'
    Write-Host 'Installer checks passed: latest/version install, repeat install, isolated PATH, corruption, download failure, version metadata, pipe-to-iex entry.'
}
finally {
    $env:Path = $originalProcessPath
    $resolvedTemp = [IO.Path]::GetFullPath($tempRoot)
    $tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if ($resolvedTemp.StartsWith($tempParent, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolvedTemp).StartsWith('dotcraft-unity-installer-test-')) {
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
