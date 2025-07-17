Import-Module "$PSScriptRoot\vmgenie-prompt.psm1"
Import-Module "$PSScriptRoot\vmgenie-import.psm1"
Import-Module "$PSScriptRoot\vmgenie-key.psm1"
Import-Module "$PSScriptRoot\vmgenie-template.psm1"
Import-Module "$PSScriptRoot\vmgenie-config.psm1"
Import-Module "$PSScriptRoot\vmgenie-client.psm1"
Import-YamlDotNet

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

        Write-Host "[INFO] MERGE_AVHDX decision: $mergeAvhdx" -ForegroundColor Cyan
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

    Write-Host "[INFO] Loading metadata from: $metadataFile" -ForegroundColor Cyan

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

    Write-Host "[INFO] Base VM: $baseVmGuid" -ForegroundColor Cyan
    Write-Host "[INFO] VM Switch: $vmSwitchGuid" -ForegroundColor Cyan
    Write-Host "[INFO] Merge Differencing Disk: $mergeDifferencingDisk" -ForegroundColor Cyan

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
    }

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
    }

    if ($null -eq $response) {
        throw "[ERROR] No response received from service."
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

    Write-Host "[INFO] Requesting VHDX clone for VM '$VmGuid' as '$Name' from service..."

    $parameters = @{
        action        = 'clone'
        guid          = $VmGuid
        instance_name = $Name
    }

    if ($MergeAvhdx) {
        $parameters['merge_avhdx'] = $true
        Write-Host "[INFO] merge_avhdx: true (will merge differencing disk if applicable)"
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
    }

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
    Invoke-ProvisionVm, `
    Publish-VmArtifact, `
    Publish-SeedIso, `
    Copy-Vhdx, `
    Get-IsDifferencingDisk, `
    ConvertTo-Boolean
