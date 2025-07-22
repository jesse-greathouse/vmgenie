Import-Module "$PSScriptRoot\vmgenie-prompt.psm1"
Import-Module "$PSScriptRoot\vmgenie-import.psm1"
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
    $guid = $selection.Guid
    $InstanceName = $selection.DisplayName

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
    <#
    .SYNOPSIS
    Guided wizard to produce a VM artifact product directory under var/cloud/<INSTANCE>.
    .DESCRIPTION
    Prompts the user for all necessary values, generates an SSH keypair, renders templates,
    and writes files into var/cloud/<INSTANCE> following the standard structure.
    .PARAMETER InstanceName
    Optional instance name to use. If not provided, the user will be prompted.
    .OUTPUTS
    The path to the created artifact directory.
    #>
    [CmdletBinding()]
    param (
        [string] $InstanceName
    )

    # Bring in global application configuration to use for some default values
    $repoRoot = Resolve-Path "$PSScriptRoot\..\.."
    $cfg = Get-Configuration
    if ($null -eq $cfg) { throw "[FATAL] Get-Configuration returned null." }

    Write-Host "=== VmGenie Artifact Wizard ===" -ForegroundColor Cyan

    # Instance name: prompt only if not provided
    if ($InstanceName) {
        $instance = $InstanceName
        Write-Host "✔ Instance Name: $instance"
    }
    else {
        $instance = Invoke-InstancePrompt
    }

    # Check for existing artifact
    if (Test-IsPublished -InstanceName $instance) {
        throw "[FATAL] Instance '$instance' is already published (artifact exists in var/cloud/$instance)."
    }

    $os = Invoke-OperatingSystemPrompt
    $osVersion = Invoke-OsVersionPrompt -OperatingSystem $os

    # capture full VM object
    $baseVmObj = Invoke-VmPrompt -Os $os -Version $osVersion -Provisioned 'exclude'
    Write-Host "V Selected Base VM: $($baseVmObj.Name) [ID: $($baseVmObj.Id)]" -ForegroundColor Cyan

    # determine if the VM is a differencing disk
    $mergeAvhdx = $false

    if (Get-IsDifferencingDisk -Guid $baseVmObj.Id) {
        $warningMessage = "[WARNING] The virtual hard drive of '$($baseVmObj.Name)' is a differencing disk (.avhdx). " +
        "Merging it into its parent will REMOVE all snapshots/checkpoints and is destructive."
        Write-Host $warningMessage -ForegroundColor Yellow

        $mergeAvhdx = Invoke-MergeAvhdxPrompt

        Write-Verbose "[INFO] MERGE_AVHDX decision: $mergeAvhdx"
    }

    # capture VM switch
    $vmSwitchObj = Invoke-VmSwitchPrompt -value $cfg.VM_SWITCH
    Write-Host "V Selected VM Switch: $($vmSwitchObj.Name) [ID: $($vmSwitchObj.Id)]" -ForegroundColor Cyan

    $hostname = Invoke-HostnamePrompt -value $instance
    $username = Invoke-UsernamePrompt  -value $cfg.USERNAME
    $timezone = Invoke-TimezonePrompt  -value $cfg.TIMEZONE
    $layout = Invoke-LayoutPrompt      -value $cfg.LAYOUT
    $locale = Invoke-LocalePrompt      -value $cfg.LOCALE

    # Paths
    $artifactDir = Join-Path -Path "var/cloud" -ChildPath $instance
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
        'BASE_VM'          = $baseVmObj.Id
        'VM_SWITCH'        = $vmSwitchObj.Id
        'HOSTNAME'         = $hostname
        'USERNAME'         = $username
        'TIMEZONE'         = $timezone
        'LAYOUT'           = $layout
        'LOCALE'           = $locale
        'PRIVKEY'          = ((Get-Content -Raw -Path $pubKeyPath).Trim())
        'MERGE_AVHDX'      = $mergeAvhdx
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

        Write-Host "[OK] Seed ISO created → $($Response.data.isoPath)" -ForegroundColor Green
        $script:isoPath = $Response.data.isoPath

        Complete-Request -Id $Response.id
    } | Out-Null

    if ($null -eq $script:isoPath) {
        throw "[ERROR] ISO path was not set by handler."
    }

    return $isoPath
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
    Remove-VMInstance, `
    Export-VMInstance, `
    Connect-VMInstance, `
    Invoke-ProvisionVm, `
    Publish-VmArtifact, `
    Publish-SeedIso, `
    Copy-Vhdx, `
    Get-IsDifferencingDisk, `
    ConvertTo-Boolean, `
    Wait-VMInstanceState, `
    Get-VMNetAddress
