Import-Module "$PSScriptRoot\vmgenie-prompt.psm1"
Import-Module "$PSScriptRoot\vmgenie-import.psm1"
Import-Module "$PSScriptRoot\vmgenie-key.psm1"
Import-Module "$PSScriptRoot\vmgenie-template.psm1"
Import-Module "$PSScriptRoot\vmgenie-config.psm1"
Import-Module "$PSScriptRoot\vmgenie-client.psm1"
Import-YamlDotNet

# Define script-scoped VM state constants
$script:VmState_Running       = 2
$script:VmState_Off           = 3
$script:VmState_Paused        = 6
$script:VmState_Suspended     = 7
$script:VmState_ShuttingDown  = 4
$script:VmState_Starting      = 10
$script:VmState_Snapshotting  = 11
$script:VmState_Saving        = 32773
$script:VmState_Stopping      = 32774
$script:VmState_NetworkReady  = 9999

$script:VmStateMap = @{
    2      = 'Running'
    3      = 'Off'
    6      = 'Paused'
    7      = 'Suspended'
    4      = 'ShuttingDown'
    10     = 'Starting'
    11     = 'Snapshotting'
    32773  = 'Saving'
    32774  = 'Stopping'
    9999   = 'NetworkReady'
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
        0       = 'Completed successfully'
        4096    = 'Job started'
        32768   = 'Failed'
        32769   = 'Access denied'
        32770   = 'Not supported'
        32771   = 'Status unknown'
        32772   = 'Timeout'
        32773   = 'Invalid parameter'
        32774   = 'System is in use'
        32775   = 'Invalid state'
        32776   = 'Incorrect data type'
        32777   = 'System not available'
        32778   = 'Out of memory'
    }

    if ($errorMap.ContainsKey($Code)) {
        return $errorMap[$Code]
    } else {
        return "Unknown Hyper-V error code: $Code"
    }
}

function Start-VMInstance {
    [CmdletBinding()]
    param (
        [string] $InstanceName
    )

    if (-not $InstanceName) {
        $vm = Invoke-VmPrompt -Provisioned only
        $guid = $vm.Id
        $DisplayName = $vm.Name
    }
    elseif ($InstanceName -notmatch '^[0-9a-fA-F-]{36}$') {
        Write-Verbose "Resolving instance name '$InstanceName' to GUID…"
        $guid = Resolve-VMInstanceId -InstanceName $InstanceName
        $DisplayName = $InstanceName
    } else {
        $guid = $InstanceName
        $DisplayName = $InstanceName
    }

    Send-VmLifecycleEvent -Action 'start' -InstanceId $guid -DisplayName $DisplayName
    Wait-VMInstanceState -InstanceName $guid -DesiredState $script:VmState_Running -DisplayName $DisplayName
}

function Stop-VMInstance {
    [CmdletBinding()]
    param (
        [string] $InstanceName
    )

    if (-not $InstanceName) {
        $vm = Invoke-VmPrompt -Provisioned only
        $guid = $vm.Id
        $DisplayName = $vm.Name
    }
    elseif ($InstanceName -notmatch '^[0-9a-fA-F-]{36}$') {
        Write-Verbose "Resolving instance name '$InstanceName' to GUID…"
        $guid = Resolve-VMInstanceId -InstanceName $InstanceName
        $DisplayName = $InstanceName
    } else {
        $guid = $InstanceName
        $DisplayName = $InstanceName
    }

    Send-VmLifecycleEvent -Action 'stop' -InstanceId $guid -DisplayName $DisplayName
    Wait-VMInstanceState -InstanceName $guid -DesiredState $script:VmState_Off -DisplayName $DisplayName
}

function Suspend-VMInstance {
    [CmdletBinding()]
    param (
        [string] $InstanceName
    )

    if (-not $InstanceName) {
        $vm = Invoke-VmPrompt -Provisioned only
        $guid = $vm.Id
        $DisplayName = $vm.Name
    }
    elseif ($InstanceName -notmatch '^[0-9a-fA-F-]{36}$') {
        Write-Verbose "Resolving instance name '$InstanceName' to GUID…"
        $guid = Resolve-VMInstanceId -InstanceName $InstanceName
        $DisplayName = $InstanceName
    } else {
        $guid = $InstanceName
        $DisplayName = $InstanceName
    }

    Send-VmLifecycleEvent -Action 'pause' -InstanceId $guid -DisplayName $DisplayName
    Wait-VMInstanceState -InstanceName $guid -DesiredState $script:VmState_Paused -DisplayName $DisplayName
}

function Resume-VMInstance {
    [CmdletBinding()]
    param (
        [string] $InstanceName
    )

    if (-not $InstanceName) {
        $vm = Invoke-VmPrompt -Provisioned only
        $guid = $vm.Id
        $DisplayName = $vm.Name
    }
    elseif ($InstanceName -notmatch '^[0-9a-fA-F-]{36}$') {
        Write-Verbose "Resolving instance name '$InstanceName' to GUID…"
        $guid = Resolve-VMInstanceId -InstanceName $InstanceName
        $DisplayName = $InstanceName
    } else {
        $guid = $InstanceName
        $DisplayName = $InstanceName
    }

    Send-VmLifecycleEvent -Action 'resume' -InstanceId $guid -DisplayName $DisplayName
    Wait-VMInstanceState -InstanceName $guid -DesiredState $script:VmState_Running -DisplayName $DisplayName
}

function Stop-VMInstanceGracefully {
    [CmdletBinding()]
    param (
        [string] $InstanceName
    )

    if (-not $InstanceName) {
        $vm = Invoke-VmPrompt -Provisioned only
        $guid = $vm.Id
        $DisplayName = $vm.Name
    }
    elseif ($InstanceName -notmatch '^[0-9a-fA-F-]{36}$') {
        Write-Verbose "Resolving instance name '$InstanceName' to GUID…"
        $guid = Resolve-VMInstanceId -InstanceName $InstanceName
        $DisplayName = $InstanceName
    } else {
        $guid = $InstanceName
        $DisplayName = $InstanceName
    }

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
        } else {
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
        $script:StateError  = $null

        $parameters = @{
            action = 'state-check'
            id     = $InstanceName
            state  = $DesiredState
        }

        Send-Event -Command 'vm' -Parameters $parameters -Handler {
            param ($Response)

            if ($Response.status -ne 'ok') {
                $script:StateError = $Response.data
            } else {
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

function Publish-VmArtifact {
    <#
.SYNOPSIS
Guided wizard to produce a VM artifact product directory under var/cloud/<INSTANCE>.

.DESCRIPTION
Prompts the user for all necessary values, generates an SSH keypair, renders templates,
and writes files into var/cloud/<INSTANCE> following the standard structure.

.OUTPUTS
The path to the created artifact directory.
#>
    [CmdletBinding()]
    param ()

    # Bring in global application configuration to use for some default values
    $repoRoot = Resolve-Path "$PSScriptRoot\..\.."
    $cfg = Get-Configuration
    if ($null -eq $cfg) { throw "[FATAL] Get-Configuration returned null." }

    Write-Host "=== VmGenie Artifact Wizard ===" -ForegroundColor Cyan

    # Prompt for all values
    $instance = Invoke-InstancePrompt
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
The name of the instance (artifact directory) to provision.

.EXAMPLE
Invoke-ProvisionVm -InstanceName my-instance
#>
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)]
        [string] $InstanceName
    )

    $artifactDir = Join-Path -Path "var/cloud" -ChildPath $InstanceName
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

function Resolve-VMInstanceId {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)]
        [string]$InstanceName
    )

    $script:VmResult = $null
    $script:VmError  = $null

    $parameters = @{
        action       = 'list'
        provisioned  = 'only'
    }

    $result = Send-Event -Command 'vm' -Parameters $parameters -Handler {
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
    Invoke-ProvisionVm, `
    Publish-VmArtifact, `
    Publish-SeedIso, `
    Copy-Vhdx, `
    Get-IsDifferencingDisk, `
    ConvertTo-Boolean, `
    Wait-VMInstanceState
