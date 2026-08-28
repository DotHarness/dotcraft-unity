[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OperationId,

    [string]$ProjectPath = ".",

    [ValidateRange(1, 86400)]
    [int]$TimeoutSeconds = 600,

    [ValidateRange(100, 10000)]
    [int]$PollMilliseconds = 500,

    [switch]$RequireGateway
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path -LiteralPath $ProjectPath).Path
$operationPath = Join-Path $projectRoot "UserSettings\DotCraft\operations\$OperationId.json"
$discoveryPath = Join-Path $projectRoot "UserSettings\DotCraft\dotcraft-unity.json"
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
$lastState = $null

function Read-JsonFile([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    try {
        return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    }
    catch {
        return $null
    }
}

function Test-TcpEndpoint([string]$Endpoint) {
    try {
        $uri = [Uri]$Endpoint
        $client = [System.Net.Sockets.TcpClient]::new()
        try {
            $pending = $client.BeginConnect($uri.Host, $uri.Port, $null, $null)
            if (-not $pending.AsyncWaitHandle.WaitOne(1000)) {
                return $false
            }
            $client.EndConnect($pending)
            return $true
        }
        finally {
            $client.Dispose()
        }
    }
    catch {
        return $false
    }
}

function Write-Result([string]$Outcome, [bool]$GatewayReady, [string]$Reason) {
    [ordered]@{
        outcome = $Outcome
        operationId = $OperationId
        statePath = $operationPath
        status = $lastState.status
        phase = $lastState.phase
        revision = $lastState.revision
        reloadCount = $lastState.reloadCount
        editorProcessId = $lastState.editorProcessId
        gatewayReady = $GatewayReady
        reason = $Reason
    } | ConvertTo-Json -Compress
}

while ([DateTime]::UtcNow -lt $deadline) {
    $state = Read-JsonFile $operationPath
    if ($null -ne $state) {
        $lastState = $state

        if ($state.status -eq "failed") {
            Write-Result "failed" $false $state.message
            exit 2
        }

        if ($state.status -eq "succeeded") {
            if (-not $RequireGateway) {
                Write-Result "succeeded" $false "Operation completed."
                exit 0
            }

            $discovery = Read-JsonFile $discoveryPath
            if ($null -ne $discovery -and $discovery.processId -eq $state.editorProcessId) {
                $process = Get-Process -Id $discovery.processId -ErrorAction SilentlyContinue
                if ($null -ne $process -and (Test-TcpEndpoint $discovery.endpoint)) {
                    Write-Result "succeeded" $true "Operation completed and the Unity Tool Gateway is reachable."
                    exit 0
                }
            }
        }

        $editorProcess = Get-Process -Id $state.editorProcessId -ErrorAction SilentlyContinue
        if ($null -eq $editorProcess -and $state.status -eq "running") {
            Write-Result "editor-exited" $false "The Unity Editor process exited before the operation completed."
            exit 3
        }
    }

    Start-Sleep -Milliseconds $PollMilliseconds
}

if ($null -eq $lastState) {
    $lastState = [pscustomobject]@{
        status = "missing"
        phase = "missing"
        revision = 0
        reloadCount = 0
        editorProcessId = 0
    }
    Write-Result "timeout" $false "The operation state file was not created before the timeout."
}
elseif ($lastState.status -eq "succeeded" -and $RequireGateway) {
    Write-Result "gateway-timeout" $false "The Unity operation completed, but the Tool Gateway did not become reachable before the timeout."
}
else {
    Write-Result "timeout" $false "The Unity operation did not reach a terminal state before the timeout."
}
exit 4
