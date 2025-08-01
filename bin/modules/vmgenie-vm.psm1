Import-Module "$PSScriptRoot\vmgenie-prompt.psm1"
Import-Module "$PSScriptRoot\vmgenie-import.psm1"
Import-Module "$PSScriptRoot\vmgenie-gmi.psm1"
Import-Module "$PSScriptRoot\vmgenie-key.psm1"
Import-Module "$PSScriptRoot\vmgenie-template.psm1"
Import-Module "$PSScriptRoot\vmgenie-config.psm1"
Import-Module "$PSScriptRoot\vmgenie-client.psm1"
Import-YamlDotNet

# Define script-scoped VM state constants
$script:VmState_Running = 2
$script:VmState_Off = 3
$script:VmState_Paused = 6
$script:VmState_Suspended = 7
$script:VmState_ShuttingDown = 4
$script:VmState_Starting = 10
$script:VmState_Snapshotting = 11
$script:VmState_Saving = 32773
$script:VmState_Stopping = 32774
$script:VmState_NetworkReady = 9999

$script:VmStateMap = @{
    2     = 'Running'
    3     = 'Off'
    6     = 'Paused'
    7     = 'Suspended'
    4     = 'ShuttingDown'
    10    = 'Starting'
    11    = 'Snapshotting'
    32773 = 'Saving'
    32774 = 'Stopping'
    9999  = 'NetworkReady'
}

function Get-StateName {
    param (
        [int] $State
    )
    return $script:VmStateMap[$State] ?? "Unknown($State)"
}

function Get-HyperVErrorMessage {
    param(
        [int]$Code
    )

    $errorMap = @{
        0     = 'Completed successfully'
        4096  = 'Job started'
        32768 = 'Failed'
        32769 = 'Access denied'
        32770 = 'Not supported'
        32771 = 'Status unknown'
        32772 = 'Timeout'
        32773 = 'Invalid parameter'
        32774 = 'System is in use'
        32775 = 'Invalid state'
        32776 = 'Incorrect data type'
        32777 = 'System not available'
        32778 = 'Out of memory'
    }

    if ($errorMap.ContainsKey($Code)) {
        return $errorMap[$Code]
    }
    else {
        return "Unknown Hyper-V error code: $Code"
    }
}

function Start-VMInstance {
    [CmdletBinding()]
    param (
        [string] $InstanceName
    )

    $selection = Resolve-VMInstanceSelectionOrNew -InstanceName $InstanceName
    $guid = $selection.Guid
    $DisplayName = $selection.DisplayName

    Send-VmLifecycleEvent -Action 'start' -InstanceId $guid -DisplayName $DisplayName
    Wait-VMInstanceState -InstanceName $guid -DesiredState $script:VmState_Running -DisplayName $DisplayName
}

function Stop-VMInstance {
    [CmdletBinding()]
    param (
        [string] $InstanceName
    )

    $resolved = Resolve-VMInstanceSelection -InstanceName $InstanceName
    $guid = $resolved.Guid
    $DisplayName = $resolved.DisplayName

    Send-VmLifecycleEvent -Action 'stop' -InstanceId $guid -DisplayName $DisplayName
    Wait-VMInstanceState -InstanceName $guid -DesiredState $script:VmState_Off -DisplayName $DisplayName
}

function Suspend-VMInstance {
    [CmdletBinding()]
    param (
        [string] $InstanceName
    )

    $resolved = Resolve-VMInstanceSelection -InstanceName $InstanceName
    $guid = $resolved.Guid
    $DisplayName = $resolved.DisplayName

    Send-VmLifecycleEvent -Action 'pause' -InstanceId $guid -DisplayName $DisplayName
    Wait-VMInstanceState -InstanceName $guid -DesiredState $script:VmState_Paused -DisplayName $DisplayName
}

function Resume-VMInstance {
    [CmdletBinding()]
    param (
        [string] $InstanceName
    )

    $resolved = Resolve-VMInstanceSelection -InstanceName $InstanceName
    $guid = $resolved.Guid
    $DisplayName = $resolved.DisplayName

    Send-VmLifecycleEvent -Action 'resume' -InstanceId $guid -DisplayName $DisplayName
    Wait-VMInstanceState -InstanceName $guid -DesiredState $script:VmState_Running -DisplayName $DisplayName
}

function Stop-VMInstanceGracefully {
    [CmdletBinding()]
    param (
        [string] $InstanceName
    )

    $resolved = Resolve-VMInstanceSelection -InstanceName $InstanceName
    $guid = $resolved.Guid
    $DisplayName = $resolved.DisplayName

    Send-VmLifecycleEvent -Action 'shutdown' -InstanceId $guid -DisplayName $DisplayName
    Wait-VMInstanceState -InstanceName $guid -DesiredState $script:VmState_Off -DisplayName $DisplayName
}

function Get-VMInstanceStatus {
    <#
    .SYNOPSIS
        Lists all VM instances with state and primary IP address.
    #>
    [CmdletBinding()]
    param()

    $script:VmResult = $null
    $script:VmError = $null

    # 1. List all provisioned VMs, with net addresses included
    $parameters = @{
        action            = 'list'
        provisioned       = 'only'
        includeNetAddress = $true
    }

    Send-Event -Command 'vm' -Parameters $parameters -Handler {
        param ($Response)
        if ($Response.status -ne 'ok') {
            $script:VmError = $Response.data
            Complete-Request -Id $Response.id
            return
        }
        $script:VmResult = $Response.data.vms
        Complete-Request -Id $Response.id
    } | Out-Null

    if ($script:VmError) {
        throw "[❌] Service error during list: $script:VmError"
    }
    if (-not $script:VmResult -or $script:VmResult.Count -eq 0) {
        Write-Host "No VMs found."
        return
    }

    $rows = @()

    foreach ($vm in $script:VmResult) {
        $state = $vm.State
        $ip = ""
        if ($vm.NetAddresses -and $vm.NetAddresses.IPv4 -and $vm.NetAddresses.IPv4.Count -gt 0) {
            $ip = $vm.NetAddresses.IPv4[0]
        }
        else {
            $ip = "(unavailable)"
        }
        $rows += [PSCustomObject]@{
            Name  = $vm.Name
            State = $state
            IP    = $ip
            Guid  = $vm.Id
        }
    }

    # Column widths
    $colName = 15
    $colState = 10
    $colIP = 20
    $colGuid = 36

    # Build header and underline
    $headerFmt = "{0,-$colName} {1,-$colState} {2,-$colIP} {3,-$colGuid}"
    $underline = ("-" * $colName) + " " + ("-" * $colState) + " " + ("-" * $colIP) + " " + ("-" * $colGuid)

    # Extra space above table
    Write-Host ''

    # Print header in green
    Write-Host ($headerFmt -f 'Name', 'State', 'IP', 'Guid') -ForegroundColor Green
    Write-Host $underline -ForegroundColor Green

    # Print data rows
    foreach ($row in $rows) {
        Write-Host ($headerFmt -f $row.Name, $row.State, $row.IP, $row.Guid)
    }

    # Extra space below table
    Write-Host ''
}

function Remove-VMInstance {
    <#
    .SYNOPSIS
    Deletes a VM and all associated artifacts via the CoordinatorService.
    .PARAMETER InstanceName
    Optional instance name or GUID. If omitted, presents a selection prompt (no "New" option).
    #>
    [CmdletBinding()]
    param(
        [string] $InstanceName
    )

    # If InstanceName not provided, prompt for selection (without -New option)
    if (-not $InstanceName) {
        $selection = Invoke-VmPrompt -Provisioned 'only'
        if (-not $selection) {
            throw "[❌] No VM selected for deletion."
        }
        $guid = $selection.Id
        $DisplayName = $selection.Name
    }
    else {
        $resolved = Resolve-VMInstanceSelection -InstanceName $InstanceName
        $guid = $resolved.Guid
        $DisplayName = $resolved.DisplayName
    }

    # Confirm before deleting
    $confirm = Read-Host "Are you sure you want to delete VM '$DisplayName' [$guid]? This operation is destructive. Type 'yes' to confirm"
    if ($confirm -ne 'yes') {
        Write-Host "[❌] Deletion cancelled by user." -ForegroundColor Red
        return
    }

    Write-Host "[⚙ ] Working on Deleting $DisplayName …" -ForegroundColor Yellow

    $script:DeleteError = $null

    $parameters = @{
        action = 'delete'
        id     = $guid
    }

    Send-Event -Command 'vm' -Parameters $parameters -Handler {
        param ($Response)
        if ($Response.status -ne 'ok') {
            $script:DeleteError = $Response.data
        }
        Complete-Request -Id $Response.id
    } | Out-Null

    if ($script:DeleteError) {
        throw "[❌] Service error during deletion: $script:DeleteError"
    }

    Write-Host "[✅] VM '$DisplayName' [$guid] deleted successfully." -ForegroundColor Green
}

function Import-VMInstance {
    <#
    .SYNOPSIS
        Import a VM instance from an archive, supporting both copy and restore.
    .PARAMETER InstanceName
        (Optional) Name of the VM instance whose archives to consider for import.
        If omitted, prompts for selection.
    .PARAMETER Mode
        'copy' for new instance, 'restore' for backup restore (default: 'restore').
    .PARAMETER NewInstanceName
        Required if Mode is 'copy'. Name for the new VM instance.
    .EXAMPLE
        Import-VMInstance -InstanceName 'test1' -Mode copy -NewInstanceName 'myclone'
    #>
    [CmdletBinding()]
    param (
        [string] $InstanceName,
        [ValidateSet('restore', 'copy')]
        [string] $Mode = 'restore',
        [string] $NewInstanceName
    )

    # INSTANCE SELECTION
    $resolved = Resolve-VMInstanceSelection -InstanceName $InstanceName
    $DisplayName = $resolved.DisplayName

    # ARCHIVE SELECTION — Only show archives for this instance
    $archive = Invoke-ExportArchivePrompt -InstanceName $DisplayName
    if (-not $archive) {
        throw "[❌] No exported archives found for instance '$DisplayName'."
    }
    $ArchiveName = $archive.archiveName
    $ArchivePath = $archive.archiveUri      # CORRECT PROPERTY!
    $OriginalInstanceName = $archive.instanceName

    # CHOOSE MODE
    if (-not $PSBoundParameters.ContainsKey('Mode')) {
        $Mode = Invoke-ImportModePrompt  # Defaults to 'restore'
    }

    # 4. GET NEW INSTANCE NAME (if copy)
    if ($Mode -eq 'copy' -and -not $NewInstanceName) {
        $NewInstanceName = Invoke-InstancePrompt -Label 'Enter New Instance Name'
        if (-not $NewInstanceName) {
            throw "[❌] Copy mode requires a new instance name."
        }
    }

    $wasShutDown = $false

    if ($Mode -eq 'restore') {
        # --- Check if VM is running (use state-check logic as in Export-VMInstance) ---
        $isProvisioned = Test-IsProvisioned -InstanceName $OriginalInstanceName
        if ($isProvisioned) {
            $guid = Resolve-VMInstanceId -InstanceName $OriginalInstanceName

            $script:StateResult = $null
            $script:StateError = $null

            $parameters = @{
                action = 'state-check'
                id     = $guid
                state  = $script:VmState_Running
            }
            Send-Event -Command 'vm' -Parameters $parameters -Handler {
                param ($Response)
                if ($Response.status -ne 'ok') {
                    $script:StateError = $Response.data
                }
                else {
                    $script:StateResult = $Response.data
                }
                Complete-Request -Id $Response.id
            } | Out-Null

            if ($script:StateError) {
                throw "[❌] Service error during state-check: $script:StateError"
            }

            $isRunning = $script:StateResult.matches
            $currentState = $script:StateResult.currentState
            $currentStateName = Get-StateName -State $currentState

            if ($isRunning) {
                $doShutdown = [Sharprompt.Prompt]::Confirm(
                    "Instance '$OriginalInstanceName' is currently *running* (state: $currentStateName). Shut down before restore?"
                )
                if (-not $doShutdown) {
                    throw "[❌] Cannot restore over a running VM. Import cancelled."
                }
                Stop-VMInstanceGracefully -InstanceName $OriginalInstanceName
                $wasShutDown = $true
            }
        }
    }

    # PERFORM IMPORT VIA SERVICE
    $parameters = @{
        action  = 'import'
        archive = $ArchivePath    # **this must be a non-null string**
        mode    = $Mode
    }
    if ($Mode -eq 'copy') {
        $parameters['newInstanceName'] = $NewInstanceName  # Service expects newInstanceName
    }

    $script:ImportError = $null
    $script:ImportResult = $null

    Write-Host "[⚙ ] Importing archive $ArchiveName …" -ForegroundColor Yellow

    Send-Event -Command 'vm' -Parameters $parameters -Handler {
        param ($Response)
        if ($Response.status -ne 'ok') {
            $script:ImportError = $Response.data
        }
        else {
            $script:ImportResult = $Response.data
        }
        Complete-Request -Id $Response.id
    } -TimeoutSeconds 300 | Out-Null

    if ($script:ImportError) {
        throw "[❌] Import failed: $script:ImportError"
    }

    Write-Host "[✅] Import completed: $ArchiveName ($Mode mode)" -ForegroundColor Green

    # Offer to start/connect imported VM if relevant
    $importedName = if ($Mode -eq 'copy') { $NewInstanceName } else { $OriginalInstanceName }
    if ($wasShutDown -or $Mode -eq 'copy') {
        $connect = [Sharprompt.Prompt]::Confirm(
            "Import complete. Would you like to start and connect to '$importedName' now?"
        )
        if ($connect) {
            Start-VMInstance -InstanceName $importedName
            Connect-VMInstance -InstanceName $importedName
        }
    }
}

function Export-VMInstance {
    <#
    .SYNOPSIS
    Export a VM and all artifacts as a .zip archive.
    .DESCRIPTION
    Prompts for the VM if not specified. If the VM is running, allows user to pause, export live (crash-consistent), or cancel.
    .PARAMETER InstanceName
    (Optional) Name or GUID of the VM instance to export.
    #>
    [CmdletBinding()]
    param (
        [string] $InstanceName
    )

    # 1. Resolve instance to GUID and display name.
    $resolved = Resolve-VMInstanceSelection -InstanceName $InstanceName
    $guid = $resolved.Guid
    $DisplayName = $resolved.DisplayName

    Write-Verbose "[INFO] Preparing to export VM: $DisplayName [$guid]"

    # 2. Check current VM state.
    $script:StateResult = $null
    $script:StateError = $null

    $parameters = @{
        action = 'state-check'
        id     = $guid
        state  = $script:VmState_Running
    }

    Send-Event -Command 'vm' -Parameters $parameters -Handler {
        param ($Response)
        if ($Response.status -ne 'ok') {
            $script:StateError = $Response.data
        }
        else {
            $script:StateResult = $Response.data
        }
        Complete-Request -Id $Response.id
    } | Out-Null

    if ($script:StateError) {
        throw "[❌] Service error during state-check: $script:StateError"
    }

    $isRunning = $script:StateResult.matches
    $currentState = $script:StateResult.currentState
    $currentStateName = Get-StateName -State $currentState

    # 3. If running, confirm user intent.
    if ($isRunning) {
        $decision = Invoke-ExportVmWhileRunningPrompt -InstanceName $DisplayName -VmState $currentStateName

        switch ($decision) {
            "cancel" {
                Write-Host "[❌] Export cancelled by user." -ForegroundColor Red
                return
            }
            "pause" {
                Write-Host "[⏸️] Pausing VM $DisplayName before export..." -ForegroundColor Yellow
                Suspend-VMInstance -InstanceName $guid
                # Wait for Paused state
                Wait-VMInstanceState -InstanceName $guid -DesiredState $script:VmState_Paused -DisplayName $DisplayName
                Write-Host "[✅] VM $DisplayName paused. Proceeding with export." -ForegroundColor Green
            }
            "live" {
                Write-Host "[⚡] Proceeding to export VM $DisplayName while RUNNING (crash-consistent)." -ForegroundColor Yellow
            }
            default {
                Write-Host "[❌] Unrecognized response from export prompt, cancelling." -ForegroundColor Red
                return
            }
        }
    }
    else {
        Write-Host "[ℹ️] VM $DisplayName is in state: $currentStateName ($currentState). Proceeding with export." -ForegroundColor Cyan
    }

    # 4. Send export event to the service.
    $script:ExportError = $null
    $script:ExportResult = $null

    $exportParams = @{
        action = 'export'
        id     = $guid
    }

    Write-Host "[⚙ ] Exporting $DisplayName..." -ForegroundColor Yellow

    Send-Event -Command 'vm' -Parameters $exportParams -Handler {
        param ($Response)
        if ($Response.status -ne 'ok') {
            $script:ExportError = $Response.data
        }
        else {
            $script:ExportResult = $Response.data.archive
        }
        Complete-Request -Id $Response.id
    } -TimeoutSeconds 300 | Out-Null

    # 5. If we paused the VM earlier, resume it now.
    if ($isRunning -and $decision -eq "pause") {
        Write-Host "[▶️] Resuming VM $DisplayName after export..." -ForegroundColor Yellow
        Resume-VMInstance -InstanceName $guid
        Wait-VMInstanceState -InstanceName $guid -DesiredState $script:VmState_Running -DisplayName $DisplayName
        Write-Host "[✅] VM $DisplayName resumed." -ForegroundColor Green
    }

    if ($script:ExportError) {
        throw "[❌] Export failed: $script:ExportError"
    }

    if ($null -eq $script:ExportResult -or -not $script:ExportResult.path) {
        throw "[❌] No archive returned from export operation."
    }

    Write-Host "[✅] VM '$DisplayName' exported successfully to archive:" -ForegroundColor Green
    Write-Host "     $($script:ExportResult.path)" -ForegroundColor Green

    # Optionally return the archive info as output object
    return $script:ExportResult
}

function Send-VmLifecycleEvent {
    param (
        [Parameter(Mandatory)] [string] $Action,
        [Parameter(Mandatory)] [string] $InstanceId,
        [string] $DisplayName = 'VM'
    )

    Write-Verbose "[INFO] Sending $Action for $DisplayName id: $InstanceId"

    $parameters = @{
        action = $Action
        id     = $InstanceId
    }

    $script:LifecycleError = $null

    Send-Event -Command 'vm' -Parameters $parameters -Handler {
        param ($Response)

        if ($Response.status -ne 'ok') {
            $script:LifecycleError = $Response.data
        }
        else {
            Write-Host "[⚙ ] Working on $DisplayName $Action …" -ForegroundColor Yellow
        }

        Complete-Request -Id $Response.id
    } | Out-Null

    if ($script:LifecycleError) {
        # Try to parse an error code and make it friendly
        if ($script:LifecycleError -match 'Error code: (\d+)') {
            $code = [int]$matches[1]
            $reason = Get-HyperVErrorMessage -Code $code
            throw "[❌] Service Error: $reason ($code)"
        }
        else {
            throw "[❌] Service Error: $script:LifecycleError"
        }
    }
}

function Wait-VMInstanceState {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)]
        [string] $InstanceName,

        [Parameter(Mandatory)]
        [int] $DesiredState,

        [string] $DisplayName = 'VM',

        [int] $TimeoutSeconds = 120,

        [int] $PollIntervalSeconds = 3
    )

    # resolve to GUID if needed
    if ($InstanceName -notmatch '^[0-9a-fA-F-]{36}$') {
        Write-Verbose "Resolving instance name '$InstanceName' to GUID…"
        $InstanceName = Resolve-VMInstanceId -InstanceName $InstanceName
    }

    $startTime = Get-Date

    Write-Verbose "[INFO] Waiting for $DisplayName '$InstanceName' to reach state $DesiredState…"

    do {
        $script:StateResult = $null
        $script:StateError = $null

        $parameters = @{
            action = 'state-check'
            id     = $InstanceName
            state  = $DesiredState
        }

        Send-Event -Command 'vm' -Parameters $parameters -Handler {
            param ($Response)

            if ($Response.status -ne 'ok') {
                $script:StateError = $Response.data
            }
            else {
                $script:StateResult = $Response.data
            }

            Complete-Request -Id $Response.id
        } | Out-Null

        if ($script:StateError) {
            throw "[❌] Service error during state-check: $script:StateError"
        }

        if ($script:StateResult -and $script:StateResult.matches) {
            $stateName = Get-StateName -State $DesiredState
            Write-Host "[✅] $DisplayName reached desired state: $stateName ($DesiredState)" -ForegroundColor Green
            return
        }

        $currentStateName = Get-StateName -State $script:StateResult.currentState
        Write-Host "[⏳] $DisplayName is in state $currentStateName ($($script:StateResult.currentState)), waiting…" -ForegroundColor Yellow

        Start-Sleep -Seconds $PollIntervalSeconds

    } while ((Get-Date) -lt $startTime.AddSeconds($TimeoutSeconds))

    throw "[⏰] Timeout: $DisplayName did not reach desired state $DesiredState within $TimeoutSeconds seconds."
}

function Get-VMNetAddress {
    [CmdletBinding()]
    param (
        [string] $InstanceName
    )

    # Resolve VM GUID + Display Name
    if (-not $InstanceName) {
        $vm = Invoke-VmPrompt -Provisioned only
        $guid = $vm.Id
        $DisplayName = $vm.Name
    }
    elseif ($InstanceName -notmatch '^[0-9a-fA-F-]{36}$') {
        Write-Verbose "Resolving instance name '$InstanceName' to GUID…"
        $guid = Resolve-VMInstanceId -InstanceName $InstanceName
        $DisplayName = $InstanceName
    }
    else {
        $guid = $InstanceName
        $DisplayName = $InstanceName
    }

    Write-Verbose "[INFO] Fetching network addresses for VM '$DisplayName' [$guid]…"

    $script:NetAddressResult = $null
    $script:NetAddressError = $null

    $parameters = @{
        action = 'net-address'
        id     = $guid
    }

    Write-Host "[⚙ ] Retrieving net address for VM: $DisplayName" -ForegroundColor Yellow

    Send-Event -Command 'vm' -Parameters $parameters -Handler {
        param ($Response)

        if ($Response.status -ne 'ok') {
            $script:NetAddressError = $Response.data
        }
        else {
            $script:NetAddressResult = $Response.data.addresses
        }

        Complete-Request -Id $Response.id
    } | Out-Null

    if ($script:NetAddressError) {
        throw "[❌] Service error during net-address query: $script:NetAddressError"
    }

    if ($null -eq $script:NetAddressResult) {
        throw "[ERROR] No response or addresses were returned."
    }

    return $script:NetAddressResult
}

function Connect-VMInstance {
    <#
    .SYNOPSIS
    Starts a VM if needed, waits for network readiness, then connects via SSH.
    #>
    [CmdletBinding()]
    param(
        [string]$InstanceName
    )

    # Use the new helper: may prompt/create as needed, always returns a valid VM
    $selection = Resolve-VMInstanceSelectionOrNew -InstanceName $InstanceName
    if (-not $selection.DisplayName) {
        throw "[❌] Internal error: selection.DisplayName is null"
    }
    $InstanceName = $selection.DisplayName
    $guid = $selection.Guid

    Write-Verbose "[INFO] Selected instance name: $InstanceName"

    $repoRoot = Resolve-Path "$PSScriptRoot\..\.."
    $artifactDir = Join-Path -Path $repoRoot -ChildPath "var/cloud/$InstanceName"
    $metadataFile = Join-Path -Path $artifactDir -ChildPath "metadata.yml" 

    if (-not (Test-Path $metadataFile)) {
        throw "[❌] metadata.yml not found at: $metadataFile"
    }

    $yaml = Get-Content -Raw -Path $metadataFile
    $deserializer = [YamlDotNet.Serialization.DeserializerBuilder]::new().Build()
    $reader = New-Object System.IO.StringReader($yaml)
    $metadata = $deserializer.Deserialize(
        $reader,
        [System.Collections.Generic.Dictionary[string, object]]
    )

    if (-not $metadata) { throw "[❌] Failed to deserialize metadata.yml." }

    $username = $metadata['username']
    if (-not $username) { throw "[❌] No 'username' in metadata.yml." }

    $pemPath = Join-Path -Path $artifactDir -ChildPath "$InstanceName.pem"
    if (-not (Test-Path $pemPath)) {
        throw "[❌] Private key not found: $pemPath"
    }

    # Step 1 — check VM state
    Write-Verbose "[INFO] Checking VM state…"
    $script:StateResult = $null
    $script:StateError = $null

    $parameters = @{
        action = 'state-check'
        id     = $guid
        state  = $script:VmState_Running
    }

    Send-Event -Command 'vm' -Parameters $parameters -Handler {
        param ($Response)
        if ($Response.status -ne 'ok') {
            $script:StateError = $Response.data
        }
        else {
            $script:StateResult = $Response.data
        }
        Complete-Request -Id $Response.id
    } | Out-Null

    if ($script:StateError) {
        throw "[❌] Service error during state-check: $script:StateError"
    }

    $currentState = $script:StateResult.currentState

    if ($null -eq $currentState) {
        throw "[❌] Could not determine current VM state for '$InstanceName'."
    }

    $desiredRunningState = $script:VmState_Running
    $currentStateName = $script:VmStateMap[[int]$currentState] ?? "Unknown"

    if ($currentState -ne $desiredRunningState) {
        Write-Host "[🟢] $InstanceName is not running (state: $currentStateName). Starting it…" -ForegroundColor Yellow
        Start-VMInstance -InstanceName $InstanceName
    }
    else {
        Write-Host "[✅] $InstanceName is already running (state: $currentStateName)." -ForegroundColor Green
    }

    # Step 2 — wait for network readiness
    $networkReadyState = $script:VmState_NetworkReady
    Write-Host "[🌐] Waiting for $InstanceName network readiness…" -ForegroundColor Yellow
    Wait-VMInstanceState -InstanceName $InstanceName -DesiredState $networkReadyState -DisplayName $InstanceName

    # Step 3 — get IP and SSH
    $addresses = Get-VMNetAddress -InstanceName $InstanceName
    $ipv4 = $addresses.IPv4[0]

    if (-not $ipv4) {
        throw "[❌] No IPv4 address found for instance '$InstanceName'."
    }

    Write-Host "[➡ ] Connecting to $InstanceName at $ipv4 as $username …" -ForegroundColor Cyan

    & ssh -i $pemPath -o IdentitiesOnly=yes "$username@$ipv4"
}

function Publish-VmArtifact {
    [CmdletBinding()]
    param (
        [string] $InstanceName
    )

    # Load configuration
    $repoRoot = Resolve-Path "$PSScriptRoot\..\.."
    $cfg = Get-Configuration
    if ($null -eq $cfg) { throw "[FATAL] Get-Configuration returned null." }

    Write-Host "=== VmGenie Artifact Wizard ===" -ForegroundColor Cyan

    # Instance name
    if ($InstanceName) {
        $instance = $InstanceName.Trim()
        Write-Host "✔ Instance Name: $instance"
    }
    else {
        $instance = Invoke-InstancePrompt
        $instance = $instance.Trim()
    }

    if (Test-IsPublished -InstanceName $instance) {
        throw "[FATAL] Instance '$instance' is already published (artifact exists in var/cloud/$instance)."
    }

    $os = Invoke-OperatingSystemPrompt
    $osVersion = Invoke-OsVersionPrompt -OperatingSystem $os

    # Select GMI (with "New" option!)
    $gmiObj = Invoke-VmPrompt -Os $os -Version $osVersion -Provisioned 'exclude' -label 'Select GMI' -New -Gmi

    # If "New GMI", go through online GMI package flow using Install-GmiPackage
    if ($gmiObj -eq '__NEW__') {
        # This will prompt, download, import, and return the GMI object
        Install-GmiPackage -Os $os -Version $osVersion | Out-Null

        # After import, locate the new GMI VM object by Name
        # Typically, $installedGmi is what you want (if Install-GmiPackage returns Get-Gmi -Name $pkg.Name)
        # But, if your workflow expects a prompt here, you can re-invoke selection:
        $gmiObj = Invoke-VmPrompt -Os $os -Version $osVersion -Provisioned 'exclude' -label 'Select GMI'
    }

    Write-Host "V Selected GMI: $($gmiObj.Name) [ID: $($gmiObj.Id)]" -ForegroundColor Cyan

    # Determine if the VM is a differencing disk
    $mergeAvhdx = $false
    if (Get-IsDifferencingDisk -Guid $gmiObj.Id) {
        $warningMessage = "[WARNING] The virtual hard drive of '$($gmiObj.Name)' is a differencing disk (.avhdx). " +
        "Merging it into its parent will REMOVE all snapshots/checkpoints and is destructive."
        Write-Host $warningMessage -ForegroundColor Yellow
        $mergeAvhdx = Invoke-MergeAvhdxPrompt
        Write-Verbose "[INFO] MERGE_AVHDX decision: $mergeAvhdx"
    }

    # Prompt for CPU and Memory allocations (after GMI selection, before VM switch)
    $cpuCount = Invoke-CpuCountPrompt -value $cfg.CPU_COUNT
    $memoryMb = Invoke-MemoryMbPrompt -value $cfg.MEMORY_MB

    # Capture VM switch
    $vmSwitchObj = Invoke-VmSwitchPrompt -default $cfg.VM_SWITCH
    Write-Host "V Selected VM Switch: $($vmSwitchObj.Name) [ID: $($vmSwitchObj.Id)]" -ForegroundColor Cyan

    $hostname = Invoke-HostnamePrompt  -value $instance
    $username = Invoke-UsernamePrompt  -value $cfg.USERNAME
    $timezone = Invoke-TimezonePrompt  -value $cfg.TIMEZONE
    $layout = Invoke-LayoutPrompt    -value $cfg.LAYOUT
    $locale = Invoke-LocalePrompt    -value $cfg.LOCALE

    # Paths
    $artifactDir = Join-Path -Path $repoRoot -ChildPath "var/cloud/$instance"
    $seedDataDir = Join-Path -Path $artifactDir -ChildPath "seed-data"
    $metadataPath = Join-Path -Path $artifactDir -ChildPath "metadata.yml"
    $pubKeyPath = Join-Path -Path $artifactDir -ChildPath ("$instance.pem.pub")

    # Create directories
    if (-not (Test-Path $seedDataDir)) {
        New-Item -ItemType Directory -Path $seedDataDir -Force | Out-Null
    }

    # Generate SSH keys directly into artifact dir
    Add-Key -Name $instance -OutputDirectory $artifactDir

    # Prepare template variables
    $variables = @{
        'INSTANCE'         = $instance
        'OPERATING_SYSTEM' = $os
        'OS_VERSION'       = $osVersion
        'BASE_VM'          = $gmiObj.Id
        'VM_SWITCH'        = $vmSwitchObj.Id
        'HOSTNAME'         = $hostname
        'USERNAME'         = $username
        'TIMEZONE'         = $timezone
        'LAYOUT'           = $layout
        'LOCALE'           = $locale
        'PRIVKEY'          = ((Get-Content -Raw -Path $pubKeyPath).Trim())
        'MERGE_AVHDX'      = $mergeAvhdx
        'CPU_COUNT'        = $cpuCount
        'MEMORY_MB'        = $memoryMb
    }

    # Render metadata.yml
    Convert-Template `
        -TemplatePath (Join-Path $repoRoot "etc/metadata.yml") `
        -OutputPath $metadataPath `
        -Variables $variables

    # Render seed-data/meta-data
    $metaDataTemplate = Join-Path -Path $repoRoot -ChildPath "etc/cloud/$os/$osVersion/seed-data/meta-data"
    $metaDataOutput = Join-Path -Path $seedDataDir -ChildPath "meta-data"
    Convert-Template `
        -TemplatePath $metaDataTemplate `
        -OutputPath $metaDataOutput `
        -Variables $variables

    # Render seed-data/user-data
    $userDataTemplate = Join-Path -Path $repoRoot -ChildPath "etc/cloud/$os/$osVersion/seed-data/user-data"
    $userDataOutput = Join-Path -Path $seedDataDir -ChildPath "user-data"
    Convert-Template `
        -TemplatePath $userDataTemplate `
        -OutputPath $userDataOutput `
        -Variables $variables

    # Render seed-data/network-config
    $networkConfigTemplate = Join-Path -Path $repoRoot -ChildPath "etc/cloud/$os/$osVersion/seed-data/network-config"
    $networkConfigOutput = Join-Path -Path $seedDataDir -ChildPath "network-config"
    Convert-Template `
        -TemplatePath $networkConfigTemplate `
        -OutputPath $networkConfigOutput `
        -Variables $variables

    # Call the ISO creation
    Publish-SeedIso -InstanceName $instance

    Write-Host "[✅] VM artifact created successfully in: $artifactDir" -ForegroundColor Green
}

## TODO: Rotate Pivate keys
## Rotating keys is something that can't currently be supported.
## cloud-init only updates ~/.ssh/authorized_keys on the first boot of an instance.
## Changing the keys after the first boot will result in the user being locked out of the instance.
## I'd like to find a way to programmatically rotate the keys in the instance, but for now its a non starter.
## Keeping this code here in case I figure out how to do it later, but for now its not supported.
function Update-VmInstancePrivKey {
    <#
    .SYNOPSIS
    Rotates the SSH keypair for a VM Instance, updates metadata, and recreates seed ISO.
    .DESCRIPTION
    Generates a new SSH private/public key pair for an existing VM Instance artifact.
    Updates the public key in metadata.yml and recreates the seed ISO.
    Also ensures the new ISO is attached to the VM (if provisioned).
    Does NOT overwrite other configuration or artifact data.
    #>
    [CmdletBinding()]
    param (
        [string]$InstanceName
    )

    # Resolve instance selection
    $resolved = Resolve-VMInstanceSelection -InstanceName $InstanceName
    if (-not $resolved) { throw "[FATAL] Could not resolve VM instance selection." }
    $InstanceName = $resolved.DisplayName

    # Locate artifact directory
    $repoRoot = Resolve-Path "$PSScriptRoot\..\.."
    $artifactDir = Join-Path -Path $repoRoot -ChildPath "var/cloud/$InstanceName"

    # Sanity check artifact exists
    if (-not (Test-Path $artifactDir)) {
        throw "[FATAL] Artifact directory does not exist: $artifactDir"
    }

    # Remove old keys and seed ISO (if present)
    $keyFiles = @(
        Join-Path $artifactDir "$InstanceName.pem"
        Join-Path $artifactDir "$InstanceName.pem.pub"
        Join-Path $artifactDir "seed.iso"
    )
    foreach ($file in $keyFiles) {
        if (Test-Path $file) {
            Remove-Item -Path $file -Recurse -Force
        }
    }

    # Generate new SSH keypair
    Add-Key -Name $InstanceName -OutputDirectory $artifactDir

    # Read public key from .pem.pub file
    $pubKeyPath = Join-Path $artifactDir "$InstanceName.pem.pub"
    $pubKey = (Get-Content -Raw -Path $pubKeyPath).Trim()

    # Read username from metadata.yml
    $metadataPath = Join-Path $artifactDir "metadata.yml"
    $metadataYaml = Get-Content -Raw -Path $metadataPath
    $metadataBuilder = New-Object YamlDotNet.Serialization.DeserializerBuilder
    $metadataDeserializer = $metadataBuilder.Build()
    $metadataReader = New-Object System.IO.StringReader($metadataYaml)
    $metadata = $metadataDeserializer.Deserialize($metadataReader, [System.Collections.Generic.Dictionary[string, object]])

    if ($null -eq $metadata) { throw "[FATAL] Failed to parse metadata.yml" }
    if (-not $metadata.ContainsKey('username')) {
        throw "[FATAL] No username field in metadata.yml"
    }
    $username = $metadata['username']

    # Update public key in user-data YAML for that user
    $userDataPath = Join-Path $artifactDir "seed-data/user-data"
    if (-not (Test-Path $userDataPath)) {
        throw "[FATAL] user-data not found: $userDataPath"
    }
    $userDataYaml = Get-Content -Raw -Path $userDataPath

    $userDataBuilder = New-Object YamlDotNet.Serialization.DeserializerBuilder
    $userDataDeserializer = $userDataBuilder.Build()
    $userDataReader = New-Object System.IO.StringReader($userDataYaml)
    $userData = $userDataDeserializer.Deserialize($userDataReader)

    # Find user by name
    $targetUser = $null
    if ($userData.ContainsKey('users')) {
        foreach ($user in $userData['users']) {
            if ($user['name'] -eq $username) {
                $targetUser = $user
                break
            }
        }
    }

    if ($null -eq $targetUser) {
        throw "[FATAL] Could not find user '$username' in user-data YAML."
    }

    if ($targetUser.PSObject.Properties.Match('ssh-authorized-keys')) {
        # If it's null or not an array, replace with an array containing the key
        if ($null -eq $targetUser['ssh-authorized-keys'] -or
            -not ($targetUser['ssh-authorized-keys'] -is [System.Collections.IList])) {
            $targetUser['ssh-authorized-keys'] = @($pubKey)
        }
        else {
            $targetUser['ssh-authorized-keys'][0] = $pubKey
        }
    }

    # Re-insert the updated user into the users array
    for ($i = 0; $i -lt $userData['users'].Count; $i++) {
        if ($userData['users'][$i]['name'] -eq $username) {
            $userData['users'][$i] = $targetUser
        }
    }

    # Serialize back to YAML
    $userDataSerializerBuilder = New-Object YamlDotNet.Serialization.SerializerBuilder
    $userDataSerializer = $userDataSerializerBuilder.Build()
    $userDataWriter = New-Object System.IO.StringWriter
    $userDataSerializer.Serialize($userDataWriter, $userData)
    $userDataWriter.Flush()
    $newUserDataYaml = $userDataWriter.ToString()
    Set-Content -Path $userDataPath -Value $newUserDataYaml -Encoding UTF8

    # Re-create seed ISO
    Publish-SeedIso -InstanceName $InstanceName

    # Swap ISO in provisioned VM (if provisioned)
    $isProvisioned = Test-IsProvisioned -InstanceName $InstanceName
    if ($isProvisioned) {
        try {
            Update-VMInstanceIso -InstanceName $InstanceName
            Write-Host "[✅] New seed ISO attached to running VM: $InstanceName" -ForegroundColor Green
        }
        catch {
            Write-Host "[⚠️] Warning: Failed to attach new ISO to VM. You may need to reattach manually. Error: $_" -ForegroundColor Yellow
        }
    }

    Write-Host "[✅] SSH keypair rotated and seed ISO updated for instance: $InstanceName" -ForegroundColor Green
    Write-Host "    - New keys: $InstanceName.pem, $InstanceName.pem.pub"
    Write-Host "    - metadata.yml updated with new public key"
    Write-Host "    - seed-iso regenerated"
}

function Invoke-ProvisionVm {
    <#
    .SYNOPSIS
    Provisions a VM based on the artifacts and metadata in var/cloud/<InstanceName>.

    .DESCRIPTION
    Loads the metadata.yml for the given InstanceName, reads required parameters
    (base_vm, vm_switch, merge_avhdx) and sends a `vm` command with `action=provision`
    to the VmGenie service. Returns the provisioned VM DTO.

    .PARAMETER InstanceName
    Optional instance name to use. If not provided, the user will be prompted.

    .EXAMPLE
    Invoke-ProvisionVm -InstanceName my-instance
    #>
    [CmdletBinding()]
    param (
        [string] $InstanceName
    )

    if (-not $InstanceName) {
        $InstanceName = Invoke-InstancePrompt
    }
    $InstanceName = $InstanceName.Trim()

    if (Test-IsProvisioned -InstanceName $InstanceName) {
        throw "[FATAL] Instance '$InstanceName' is already provisioned."
    }

    if (-not (Test-IsPublished -InstanceName $InstanceName)) {
        Publish-VmArtifact -InstanceName $InstanceName
    }

    # Locate metadata.yml
    $repoRoot = Resolve-Path "$PSScriptRoot\..\.."
    $artifactDir = Join-Path -Path $repoRoot -ChildPath "var/cloud/$InstanceName"
    $metadataFile = Join-Path -Path $artifactDir -ChildPath "metadata.yml"

    if (-not (Test-Path $metadataFile)) {
        throw "[❌] metadata.yml not found at: $metadataFile"
    }

    Write-Verbose "[INFO] Loading metadata from: $metadataFile"

    # Read and parse YAML
    $yaml = Get-Content -Raw -Path $metadataFile
    $deserializer = [YamlDotNet.Serialization.DeserializerBuilder]::new().Build()
    $reader = New-Object System.IO.StringReader($yaml)

    $metadata = $deserializer.Deserialize(
        $reader,
        [System.Collections.Generic.Dictionary[string, object]]
    )

    if (-not $metadata) {
        throw "[❌] Failed to deserialize metadata.yml."
    }

    # Extract required metadata
    $baseVmGuid = $metadata['base_vm']
    $vmSwitchGuid = $metadata['vm_switch']
    $cpuCount = $metadata.ContainsKey('cpu_count') ? $metadata['cpu_count'] : '1'
    $memoryMb = $metadata.ContainsKey('memory_mb') ? $metadata['memory_mb'] : '512'
    $mergeDifferencingDisk = $false

    if ($metadata.ContainsKey('merge_avhdx')) {
        $mergeDifferencingDisk = ConvertTo-Boolean $metadata['merge_avhdx']
    }

    if (-not $baseVmGuid -or -not $vmSwitchGuid) {
        throw "[❌] Metadata is missing required keys: base_vm and/or vm_switch."
    }

    Write-Verbose "[INFO] Base VM: $baseVmGuid"
    Write-Verbose "[INFO] VM Switch: $vmSwitchGuid"
    Write-Verbose "[INFO] Merge Differencing Disk: $mergeDifferencingDisk"

    # Build parameters for service
    $parameters = @{
        action                = 'provision'
        baseVmGuid            = $baseVmGuid
        instanceName          = $InstanceName
        vmSwitchGuid          = $vmSwitchGuid
        mergeDifferencingDisk = $mergeDifferencingDisk
        cpuCount              = $cpuCount
        memoryMb              = $memoryMb
    }

    $script:ProvisionVmError = $null
    $script:ProvisionedVm = $null

    Write-Host "[⚙ ] Working on Provisioning $InstanceName …" -ForegroundColor Yellow

    Send-Event -Command 'vm' -Parameters $parameters -Handler {
        param ($Response)

        if ($Response.status -ne 'ok') {
            $script:ProvisionVmError = $Response.data
        }
        else {
            $script:ProvisionedVm = $Response.data.vm
            Write-Host "[✅] VM provisioned successfully!" -ForegroundColor Green
        }

        Complete-Request -Id $Response.id
    } | Out-Null

    if ($null -ne $script:ProvisionVmError) {
        throw "[❌] Service Error: $script:ProvisionVmError"
    }

    if ($null -eq $script:ProvisionedVm) {
        throw "[ERROR] No response received or VM DTO was null."
    }

    return $script:ProvisionedVm
}

function Publish-SeedIso {
    <#
.SYNOPSIS
Request the VmGenie service to generate a cloud-init seed.iso for a given instance.

.DESCRIPTION
Sends an `artifact` command to the VmGenie service via named pipe with
`action=create` and the `instanceName`. The service performs all path validation
and ISO creation internally.

.PARAMETER InstanceName
The name of the instance (e.g., "my-instance"). Must correspond to a directory
under CLOUD_DIR on the server side.

.EXAMPLE
Publish-SeedIso -InstanceName test5
#>
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)]
        [string] $InstanceName
    )

    $parameters = @{
        action       = 'create'
        instanceName = $InstanceName
    }

    $isoPath = $null

    $response = Send-Event -Command 'artifact' -Parameters $parameters -Handler {
        param($Response)

        if ($Response.status -ne 'ok') {
            throw "[❌] Failed to create ISO: $($Response.data.details)"
        }

        Write-Verbose "[OK] Seed ISO created → $($Response.data.isoPath)"
        $script:isoPath = $Response.data.isoPath

        Complete-Request -Id $Response.id
    } | Out-Null

    if ($null -eq $script:isoPath) {
        throw "[ERROR] ISO path was not set by handler."
    }

    return $isoPath
}

function Update-VMInstanceIso {
    <#
    .SYNOPSIS
    Swaps the attached ISO on a VM instance with a new ISO.
    .DESCRIPTION
    Sends an event to the `vm` command with `action = swap-iso`, instructing the VmGenie backend to detach any existing ISO and attach a new one.
    .PARAMETER InstanceName
    The name or GUID of the VM instance.
    .PARAMETER IsoPath
    The full path to the new ISO file to attach.
    .EXAMPLE
    Swap-VMInstanceIso -InstanceName test1 -IsoPath C:\Users\jessegreathouse\vmgenie\var\cloud\test1\seed.iso
    #>
    [CmdletBinding()]
    param(
        [string] $InstanceName,
        [string] $IsoPath
    )

    # Prompt for instance if not given
    $resolved = Resolve-VMInstanceSelection -InstanceName $InstanceName
    $guid = $resolved.Guid
    $DisplayName = $resolved.DisplayName

    # Default ISO path if not given: use cloud/<InstanceName>/seed.iso
    if (-not $IsoPath) {
        $repoRoot = Resolve-Path "$PSScriptRoot\..\.."
        $defaultIso = Join-Path $repoRoot "var/cloud/$DisplayName/seed.iso"
        if (Test-Path $defaultIso) {
            $IsoPath = $defaultIso
        }
        else {
            $IsoPath = Read-Host "Enter path to ISO to attach"
        }
    }

    if (-not (Test-Path $IsoPath)) {
        throw "[❌] ISO file not found: $IsoPath"
    }

    Write-Host "[⚙ ] Swapping ISO for $DisplayName..." -ForegroundColor Yellow

    $script:SwapIsoError = $null
    $script:SwapIsoResult = $null

    $parameters = @{
        action  = 'swap-iso'
        id      = $guid
        isoPath = $IsoPath
    }

    Send-Event -Command 'vm' -Parameters $parameters -Handler {
        param ($Response)
        if ($Response.status -ne 'ok') {
            $script:SwapIsoError = $Response.data
        }
        else {
            $script:SwapIsoResult = $Response.data
        }
        Complete-Request -Id $Response.id
    } | Out-Null

    if ($script:SwapIsoError) {
        throw "[❌] Swap-ISO failed: $script:SwapIsoError"
    }

    Write-Host "[✅] ISO swapped for $DisplayName - $IsoPath" -ForegroundColor Green
    return $script:SwapIsoResult
}

function Copy-Vhdx {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)]
        [string] $VmGuid,

        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter()]
        [bool] $MergeAvhdx = $false
    )

    $script:CopyVhdxResult = $null
    $script:CopyVhdxError = $null

    Write-Verbose "[INFO] Requesting VHDX clone for VM '$VmGuid' as '$Name' from service..."

    $parameters = @{
        action        = 'clone'
        guid          = $VmGuid
        instance_name = $Name
    }

    if ($MergeAvhdx) {
        $parameters['merge_avhdx'] = $true
        Write-Verbose "[INFO] merge_avhdx: true (will merge differencing disk if applicable)"
    }

    Send-Event -Command 'vhdx' -Parameters $parameters -Handler {
        param ($Response)

        if ($Response.status -ne 'ok') {
            $script:CopyVhdxError = $Response.data
        }
        else {
            $script:CopyVhdxResult = $Response.data.path
        }

        Complete-Request -Id $Response.id
    } | Out-Null

    if ($null -ne $script:CopyVhdxError) {
        throw "[❌] Service Error: $script:CopyVhdxError"
    }

    Write-Host "[✅] VHDX cloned: $script:CopyVhdxResult" -ForegroundColor Green
    return $script:CopyVhdxResult
}

function Get-IsDifferencingDisk {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)]
        [string] $Guid
    )

    $script:IsDiffDiskError = $null
    $script:IsDiffDiskResult = $false

    $parameters = @{
        action = 'is-differencing-disk'
        guid   = $Guid
    }

    Send-Event -Command 'vhdx' -Parameters $parameters -Handler {
        param ($Response)

        if ($Response.status -ne 'ok') {
            $script:IsDiffDiskError = $Response.data
        }
        else {
            $value = $Response.data.isDifferencing
            $script:IsDiffDiskResult = ConvertTo-Boolean $value
        }

        Complete-Request -Id $Response.id
    } | Out-Null

    if ($null -ne $script:IsDiffDiskError) {
        throw "[❌] Service Error: $script:IsDiffDiskError"
    }

    return $script:IsDiffDiskResult
}

function Resolve-VMInstanceSelection {
    param(
        [string] $InstanceName,
        [string] $Provisioned = 'only'
    )

    if (-not $InstanceName) {
        $vm = Invoke-VmPrompt -Provisioned $Provisioned
        return @{ Guid = $vm.Id; DisplayName = $vm.Name }
    }
    elseif ($InstanceName -notmatch '^[0-9a-fA-F-]{36}$') {
        Write-Verbose "Resolving instance name '$InstanceName' to GUID…"
        $guid = Resolve-VMInstanceId -InstanceName $InstanceName
        return @{ Guid = $guid; DisplayName = $InstanceName }
    }
    else {
        return @{ Guid = $InstanceName; DisplayName = $InstanceName }
    }
}

function Resolve-VMInstanceSelectionOrNew { 
    param(
        [string] $InstanceName,
        [string] $Provisioned = 'only'
    )

    # Case 1: No InstanceName provided, interactive prompt (with "New" option)
    if (-not $InstanceName) {
        $vm = Invoke-VmPrompt -Provisioned $Provisioned -New
        if ($vm -eq '__NEW__') {
            # User chose to create a new VM
            $InstanceName = Invoke-InstancePrompt  # prompt for name of new VM
            $newVm = Invoke-ProvisionVm -InstanceName $InstanceName
            return @{ Guid = $newVm.Id; DisplayName = $newVm.Name }
        }
        else {
            # Existing VM selected
            return @{ Guid = $vm.Id; DisplayName = $vm.Name }
        }
    }

    # Case 2: InstanceName supplied, check if it exists (by name, not GUID)
    elseif ($InstanceName -notmatch '^[0-9a-fA-F-]{36}$') {
        $exists = $false
        try {
            $guid = Resolve-VMInstanceId -InstanceName $InstanceName
            $exists = $true
        }
        catch {
            $exists = $false
        }

        if ($exists) {
            # VM exists, normal path
            return @{ Guid = $guid; DisplayName = $InstanceName }
        }
        else {
            # VM doesn't exist, prompt to create
            if (Invoke-CreateVmConfirmPrompt -InstanceName $InstanceName) {
                $newVm = Invoke-ProvisionVm -InstanceName $InstanceName
                return @{ Guid = $newVm.Id; DisplayName = $newVm.Name }
            }
            else {
                throw "[FATAL] Instance '$InstanceName' does not exist."
            }
        }
    }

    # Case 3: InstanceName is a GUID (already provisioned)
    else {
        # Optionally, you might want to resolve DisplayName from GUID here for full symmetry
        return @{ Guid = $InstanceName; DisplayName = $InstanceName }
    }
}

function Resolve-VMInstanceId {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)]
        [string]$InstanceName
    )

    $script:VmResult = $null
    $script:VmError = $null

    $parameters = @{
        action      = 'list'
        provisioned = 'only'
    }

    Send-Event -Command 'vm' -Parameters $parameters -Handler {
        param ($Response)

        if ($Response.status -ne 'ok') {
            $script:VmError = $Response.data
            Complete-Request -Id $Response.id
            return
        }

        $script:VmResult = $Response.data.vms
        Complete-Request -Id $Response.id
    } | Out-Null

    if ($script:VmError) {
        throw "Service error: $script:VmError"
    }

    if (-not $script:VmResult -or $script:VmResult.Count -eq 0) {
        throw "No VMs returned from service."
    }

    $vm = $script:VmResult | Where-Object { $_.Name -eq $InstanceName }

    if (-not $vm) {
        throw "Instance name '$InstanceName' could not be resolved to a GUID."
    }

    return $vm.Id
}

function Test-IsPublished {
    param(
        [Parameter(Mandatory)]
        [string] $InstanceName
    )

    $repoRoot = Resolve-Path "$PSScriptRoot\..\.."
    $artifactDir = Join-Path -Path $repoRoot -ChildPath "var/cloud/$InstanceName"

    return (Test-Path $artifactDir)
}

function Test-IsProvisioned {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)]
        [string] $InstanceName
    )

    $script:VmResult = $null
    $script:VmError = $null

    $parameters = @{
        action      = 'list'
        provisioned = 'only'
    }

    Send-Event -Command 'vm' -Parameters $parameters -Handler {
        param ($Response)
        if ($Response.status -ne 'ok') {
            $script:VmError = $Response.data
            Complete-Request -Id $Response.id
            return
        }
        $script:VmResult = $Response.data.vms
        Complete-Request -Id $Response.id
    } | Out-Null

    if ($script:VmError) {
        throw "Service error: $script:VmError"
    }

    if (-not $script:VmResult -or $script:VmResult.Count -eq 0) {
        return $false
    }

    foreach ($vm in $script:VmResult) {
        if ($vm.Name -eq $InstanceName) {
            return $true
        }
    }

    return $false
}

function ConvertTo-Boolean {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowNull()]
        $InputObject
    )

    # null or empty → false
    if ($null -eq $InputObject) {
        return $false
    }

    # unwrap if it's an enumerable but not a string
    if ($InputObject -is [System.Collections.IEnumerable] -and -not ($InputObject -is [string])) {
        $InputObject = @($InputObject) | Where-Object { $_ -ne $null } | Select-Object -First 1
    }

    # if already bool → done
    if ($InputObject -is [bool]) {
        return ($InputObject -eq $true)
    }

    # normalize to string if possible
    if ($null -ne $InputObject) {
        $str = ($InputObject.ToString() ?? "").Trim().ToLowerInvariant()
    }
    else {
        $str = ""
    }

    switch ($str) {
        'true' { return $true }
        '1' { return $true }
        'false' { return $false }
        '0' { return $false }
        default { return $false }
    }
}

Export-ModuleMember -Function `
    Start-VMInstance, `
    Stop-VMInstance, `
    Suspend-VMInstance, `
    Resume-VMInstance, `
    Stop-VMInstanceGracefully, `
    Get-VMInstanceStatus, `
    Remove-VMInstance, `
    Import-VMInstance, `
    Export-VMInstance, `
    Connect-VMInstance, `
    Invoke-ProvisionVm, `
    Publish-VmArtifact, `
    Publish-SeedIso, `
    Update-VMInstanceIso, `
    Copy-Vhdx, `
    Get-IsDifferencingDisk, `
    ConvertTo-Boolean, `
    Wait-VMInstanceState, `
    Get-VMNetAddress
