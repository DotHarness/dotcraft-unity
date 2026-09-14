param(
    [Parameter(Mandatory)][int]$EditorPid,
    [Parameter(Mandatory)][string]$ProjectPath,
    [Parameter(Mandatory)][string]$EvidencePath,
    [switch]$AllowProjectMutation,
    [string]$CandidateLibraryDirectory
)
$ErrorActionPreference = 'Stop'
if (!$AllowProjectMutation) { throw 'This acceptance test creates an Editor script, deliberately compiles an error, reloads scripts, and cleans up. Use an isolated project and explicitly pass -AllowProjectMutation.' }
$runArguments = @('run', '--project', (Join-Path $PSScriptRoot '../tests/DotCraft.Unity.Attach.Acceptance/Acceptance.csproj'))
if ($CandidateLibraryDirectory) {
    $candidate = [IO.Path]::GetFullPath($CandidateLibraryDirectory)
    if (!(Test-Path -LiteralPath (Join-Path $candidate 'DotCraft.Unity.Attach.dll'))) { throw 'Candidate Attach library was not found.' }
    $runArguments += "-p:CandidateLibraryDirectory=$candidate"
}
$runArguments += @('--', $EditorPid, ([IO.Path]::GetFullPath($ProjectPath)), ([IO.Path]::GetFullPath($EvidencePath)))
dotnet @runArguments
if ($LASTEXITCODE -ne 0) { throw 'Live Editor Attach acceptance failed; inspect its evidence JSON and Editor log.' }
$result = Get-Content -LiteralPath $EvidencePath -Raw | ConvertFrom-Json
if ($result.passed -ne $true -or $result.cleanupError) { throw 'Live Editor acceptance did not complete with clean teardown.' }
if ($CandidateLibraryDirectory -and $result.attachAssemblySha256 -ine (Get-FileHash (Join-Path $candidate 'DotCraft.Unity.Attach.dll') -Algorithm SHA256).Hash) { throw 'Live Editor acceptance loaded a different Attach library.' }
