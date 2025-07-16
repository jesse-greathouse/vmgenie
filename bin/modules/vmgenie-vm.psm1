Import-Module "$PSScriptRoot\vmgenie-prompt.psm1"
Import-Module "$PSScriptRoot\vmgenie-key.psm1"
Import-Module "$PSScriptRoot\vmgenie-template.psm1"
Import-Module "$PSScriptRoot\vmgenie-config.psm1"
Import-Module "$PSScriptRoot\vmgenie-client.psm1"

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
    $cfg = Get-Configuration
    if ($null -eq $cfg) { throw "[FATAL] Get-Configuration returned null." }

    Write-Host "=== VmGenie Artifact Wizard ===" -ForegroundColor Cyan

    # Prompt for all values
    $instance = Invoke-InstancePrompt
    $os = Invoke-OperatingSystemPrompt
    $osVersion = Invoke-OsVersionPrompt -OperatingSystem $os

    # capture full VM object
    $baseVmObj = Invoke-VmPrompt -Os $os -Version $osVersion
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
        'PRIVKEY'          = (Get-Content -Raw -Path $pubKeyPath)
        'MERGE_AVHDX'      = $mergeAvhdx
    }

    # Render metadata.yml
    Convert-Template `
        -TemplatePath "etc/metadata.yml" `
        -OutputPath $metadataPath `
        -Variables $variables

    # Render seed-data/meta-data
    $metaDataTemplate = Join-Path -Path "etc/cloud/$os/$osVersion/seed-data" -ChildPath "meta-data"
    $metaDataOutput = Join-Path -Path $seedDataDir -ChildPath "meta-data"
    Convert-Template `
        -TemplatePath $metaDataTemplate `
        -OutputPath $metaDataOutput `
        -Variables $variables

    # Render seed-data/user-data
    $userDataTemplate = Join-Path -Path "etc/cloud/$os/$osVersion/seed-data" -ChildPath "user-data"
    $userDataOutput = Join-Path -Path $seedDataDir -ChildPath "user-data"
    Convert-Template `
        -TemplatePath $userDataTemplate `
        -OutputPath $userDataOutput `
        -Variables $variables

    # Call the ISO creation
    Publish-SeedIso -InstanceName $instance

    Write-Host "[✅] VM artifact created successfully in: $artifactDir" -ForegroundColor Green
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
    Publish-VmArtifact, `
    Publish-SeedIso, `
    Copy-Vhdx, `
    Get-IsDifferencingDisk, `
    ConvertTo-Boolean
