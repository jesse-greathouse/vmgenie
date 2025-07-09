using namespace System.IO
using namespace System.IO.Pipes
using namespace System.Text
using namespace System.Text.Json

# Global state
$script:WaitingRequests   = @{}   # [id] -> Request object
$script:CompletedRequests = @{}   # [id] -> Request object
$script:ErroredRequests   = @{}   # [id] -> Request object
$script:TimedOutRequests  = @{}   # [id] -> Request object

function New-UniqueId {
    return [Guid]::NewGuid().ToString()
}

function Send-Event {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Command,

        [Parameter(Mandatory)]
        [hashtable] $Parameters,

        [Parameter(Mandatory)]
        [scriptblock] $Handler,

        [int] $TimeoutSeconds = 30
    )

    $id = New-UniqueId
    $requestEvent = @{
        id         = $id
        command    = $Command
        parameters = $Parameters
        timestamp  = (Get-Date).ToUniversalTime().ToString("o")
    }

    $json = ($requestEvent | ConvertTo-Json -Compress) + "`n"

    # Register Request object
    $script:WaitingRequests[$id] = @{
        handler   = $Handler
        timeout   = $TimeoutSeconds
        progress  = 0.0
        response  = '{}'
    }

    $pipe = $null
    try {
        $pipe = [NamedPipeClientStream]::new('.', 'vmgenie', [PipeDirection]::InOut)
        $pipe.Connect(5000)

        $writer = [StreamWriter]::new($pipe, [UTF8Encoding]::new($false))
        $reader = [StreamReader]::new($pipe, [UTF8Encoding]::new($false))

        # Send the request
        $writer.Write($json)
        $writer.Flush()

        $task = $reader.ReadLineAsync()
        $completed = $task.Wait([TimeSpan]::FromSeconds($TimeoutSeconds))

        if (-not $completed) {
            Write-Warning "Timeout waiting for response to id=$id"
            $script:WaitingRequests[$id].response = @{
                id        = $id
                command   = $Command
                status    = 'timeout'
                data      = @{ details = "Request timed out after $TimeoutSeconds seconds." }
                timestamp = (Get-Date).ToUniversalTime().ToString("o")
            }
            $script:TimedOutRequests[$id] = $script:WaitingRequests[$id]
            $null = $script:WaitingRequests.Remove($id)
        }
        else {
            $line = $task.Result
            if ($line) {
                Invoke-HandleResponse $line
            }
        }

    } finally {
        if ($pipe) { $pipe.Dispose() }
    }

    return $script:CompletedRequests[$id]
}

function Invoke-HandleResponse {
    param(
        [Parameter(Mandatory)]
        [string] $Json
    )

    try {
        $response = $Json | ConvertFrom-Json
        $id = $response.id

        if ($null -eq $id -or -not $script:WaitingRequests.ContainsKey($id)) {
            Write-Verbose "Ignoring unsolicited response: $Json"
            return
        }

        $script:WaitingRequests[$id].response = $response

        & $script:WaitingRequests[$id].handler -Response $response

    } catch {
        Write-Warning "Failed to handle response: $_"
    }
}

function Complete-Request {
    param(
        [Parameter(Mandatory)]
        [string] $Id
    )

    if ($script:WaitingRequests.ContainsKey($Id)) {
        $script:WaitingRequests[$Id].progress = 1.0
        $script:CompletedRequests[$Id] = $script:WaitingRequests[$Id]
        $null = $script:WaitingRequests.Remove($Id)
    }
}

function Disable-Request {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Id
    )

    if ($script:WaitingRequests.ContainsKey($Id)) {
        $script:ErroredRequests[$Id] = $script:WaitingRequests[$Id]
        $null = $script:WaitingRequests.Remove($Id)
    }
}

function Get-ErroredRequests {
    return $script:ErroredRequests
}

function Get-TimedOutRequests {
    return $script:TimedOutRequests
}

function Set-RequestProgress {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Id,

        [Parameter(Mandatory)]
        [ValidateRange(0,1)]
        [double] $Progress
    )

    if ($script:WaitingRequests.ContainsKey($Id)) {
        $script:WaitingRequests[$Id].progress = $Progress
    }
}

function Test-AllCompleted {
    return ($script:WaitingRequests.Count -eq 0)
}

Export-ModuleMember -Function `
    New-UniqueId, `
    Send-Event, `
    Invoke-HandleResponse, `
    Complete-Request, `
    Disable-Request, `
    Set-RequestProgress, `
    Test-AllCompleted, `
    Get-ErroredRequests, `
    Get-TimedOutRequests
