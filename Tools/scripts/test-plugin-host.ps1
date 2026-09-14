param(
    [Parameter(Mandatory)][string]$PluginRoot,
    [string]$ResultsDirectory = "$PSScriptRoot/../../.artifacts/host-acceptance",
    [string]$LogFileName = 'marketplace.trx'
)
$ErrorActionPreference = 'Stop'
$projectDirectory = [IO.Path]::GetFullPath("$PSScriptRoot/../tests/DotCraft.Unity.Plugin.Tests")
$project = "$projectDirectory/DotCraft.Unity.Plugin.Tests.csproj"
& dotnet restore $project
if ($LASTEXITCODE) { throw 'Host package restore failed.' }
$previousPlugin = $env:DOTCRAFT_PREBUILT_PLUGIN
$previousReadTool = $env:DOTCRAFT_PREBUILT_READ_TOOL
try {
    $env:DOTCRAFT_PREBUILT_PLUGIN = (Resolve-Path -LiteralPath $PluginRoot).Path
    $env:DOTCRAFT_PREBUILT_READ_TOOL = 'unity.list'
    & dotnet test $project -c Release --no-restore --logger "trx;LogFileName=$LogFileName" --results-directory $ResultsDirectory
    if ($LASTEXITCODE) { throw 'Real host plugin acceptance failed.' }
    $trx = [xml](Get-Content (Join-Path $ResultsDirectory $LogFileName) -Raw)
    $counters = $trx.TestRun.ResultSummary.Counters
    if ([int]$counters.total -lt 3 -or [int]$counters.passed -ne [int]$counters.total) { throw 'All host acceptance scenarios must execute and pass.' }
} finally {
    $env:DOTCRAFT_PREBUILT_PLUGIN = $previousPlugin
    $env:DOTCRAFT_PREBUILT_READ_TOOL = $previousReadTool
}
