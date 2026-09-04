[CmdletBinding()]
param(
    [string]$Version = 'latest',
    [string]$InstallDir = (Join-Path ([Environment]::GetFolderPath('UserProfile')) '.craft\bin')
)

$ErrorActionPreference = 'Stop'

function Get-DcuUserPath { [Environment]::GetEnvironmentVariable('Path', 'User') }
function Set-DcuUserPath([string]$Value) { [Environment]::SetEnvironmentVariable('Path', $Value, 'User') }

function Add-DcuUserPath([string]$Directory) {
    $directoryPath = [IO.Path]::GetFullPath($Directory).TrimEnd('\', '/')
    $userPath = Get-DcuUserPath
    $parts = @($userPath -split ';' | Where-Object { $_ })
    $contains = @($parts | Where-Object { $_.TrimEnd('\', '/') -ieq $directoryPath }).Count -gt 0
    if (-not $contains) {
        Set-DcuUserPath (($parts + $directoryPath) -join ';')
    }
    $processParts = @($env:Path -split ';' | Where-Object { $_ })
    if (@($processParts | Where-Object { $_.TrimEnd('\', '/') -ieq $directoryPath }).Count -eq 0) {
        $env:Path = ($processParts + $directoryPath) -join ';'
    }
}

function Get-DcuExecutableVersion([string]$Path) {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $Path
    $start.Arguments = 'version --json'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(10000)) {
            $process.Kill()
            throw 'Executable version check timed out.'
        }
        if ($process.ExitCode -ne 0) { throw 'Executable version check failed.' }
        return $stdout.GetAwaiter().GetResult() | ConvertFrom-Json
    }
    finally { $process.Dispose() }
}

function Install-DotCraftUnity([string]$RequestedVersion, [string]$Destination) {
    $arch = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or $arch -ne 'X64') {
        throw 'dotcraft-unity currently supports Windows x64 only.'
    }
    $headers = @{ 'User-Agent' = 'dotcraft-unity-install' }
    $repoUrl = 'https://github.com/DotHarness/dotcraft-unity'
    $tag = $RequestedVersion
    if ($tag -eq 'latest') {
        $release = Invoke-RestMethod -Uri 'https://api.github.com/repos/DotHarness/dotcraft-unity/releases/latest' -Headers $headers
        $tag = $release.tag_name
    }
    if ($tag -notmatch '^v?\d+\.\d+\.\d+$') { throw 'Invalid release version. Expected latest or vX.Y.Z.' }
    $versionNumber = $tag -replace '^v', ''
    $baseUrl = "$repoUrl/releases/download/v$versionNumber"
    $artifact = Invoke-RestMethod -Uri "$baseUrl/gateway-artifact.json" -Headers $headers
    if ($artifact.version -cne $versionNumber -or $artifact.rid -cne 'win-x64' -or
        $artifact.fileName -cne 'dotcraft-unity.exe' -or $artifact.sha256 -notmatch '^[a-fA-F0-9]{64}$') {
        throw 'Release manifest does not match the requested dotcraft-unity Windows x64 release.'
    }

    $destinationPath = [IO.Path]::GetFullPath($Destination)
    [IO.Directory]::CreateDirectory($destinationPath) | Out-Null
    $stagePath = Join-Path $destinationPath ('.dotcraft-unity-install-' + [Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($stagePath) | Out-Null
    try {
        $stagedExe = Join-Path $stagePath 'dotcraft-unity.exe'
        Write-Host "Downloading dotcraft-unity $versionNumber (win-x64)"
        Invoke-WebRequest -Uri "$baseUrl/dotcraft-unity.exe" -OutFile $stagedExe -Headers $headers
        if ((Get-FileHash -LiteralPath $stagedExe -Algorithm SHA256).Hash -ine $artifact.sha256) {
            throw 'Downloaded executable failed SHA-256 validation. Existing installation was preserved.'
        }
        $metadata = Get-DcuExecutableVersion $stagedExe
        if ($metadata.version -cne $versionNumber -or $metadata.rid -cne 'win-x64' -or $metadata.mcpSdkVersion -cne '2.2.0') {
            throw 'Executable version metadata does not match the release. Existing installation was preserved.'
        }
        $stagedNotices = Join-Path $stagePath 'dotcraft-unity.NOTICES.txt'
        Invoke-WebRequest -Uri "$baseUrl/THIRD-PARTY-NOTICES.txt" -OutFile $stagedNotices -Headers $headers
        foreach ($file in @('dotcraft-unity.NOTICES.txt', 'dotcraft-unity.exe')) {
            $source = Join-Path $stagePath $file
            $target = Join-Path $destinationPath $file
            if ([IO.File]::Exists($target)) { [IO.File]::Replace($source, $target, [NullString]::Value) }
            else { [IO.File]::Move($source, $target) }
        }
        Add-DcuUserPath $destinationPath
        Write-Host "Installed dotcraft-unity $versionNumber to $destinationPath"
        Write-Host 'User PATH is configured. Open a new terminal if another running app cannot find the command.'
        Write-Host 'Enable Unity Tool Gateway in the Unity project. MCP configuration is optional for CLI use.'
    }
    finally {
        $resolvedStage = [IO.Path]::GetFullPath($stagePath)
        if ($resolvedStage.StartsWith($destinationPath.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
            [IO.Path]::GetFileName($resolvedStage).StartsWith('.dotcraft-unity-install-')) {
            Remove-Item -LiteralPath $resolvedStage -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    Install-DotCraftUnity -RequestedVersion $Version -Destination $InstallDir
}
