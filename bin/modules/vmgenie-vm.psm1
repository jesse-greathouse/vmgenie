Import-Module "$PSScriptRoot\vmgenie-prompt.psm1"
Import-Module "$PSScriptRoot\vmgenie-key.psm1"
Import-Module "$PSScriptRoot\vmgenie-template.psm1"
Import-Module "$PSScriptRoot\vmgenie-config.psm1"

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

    $hostname = Invoke-HostnamePrompt -value $instance
    $username = Invoke-UsernamePrompt  -value $cfg.USERNAME
    $timezone = Invoke-TimezonePrompt  -value $cfg.TIMEZONE
    $layout = Invoke-LayoutPrompt      -value $cfg.LAYOUT
    $locale = Invoke-LocalePrompt      -value $cfg.LOCALE

    # Paths
    $artifactDir = Join-Path -Path "var/cloud" -ChildPath $instance
    $seedDataDir = Join-Path -Path $artifactDir -ChildPath "seed-data"
    $seedIsoPath = Join-Path -Path $artifactDir -ChildPath "seed.iso"
    $metadataPath = Join-Path -Path $artifactDir -ChildPath "metadata.yml"
    $privKeyPath = Join-Path -Path $artifactDir -ChildPath ("$instance.pem")
    $pubKeyPath = Join-Path -Path $artifactDir -ChildPath ("$instance.pem.pub")

    # Create directories
    if (-not (Test-Path $seedDataDir)) {
        New-Item -ItemType Directory -Path $seedDataDir -Force | Out-Null
    }
    Write-Host "[OK] Created artifact directory: $artifactDir" -ForegroundColor Green

    # Generate SSH keys directly into artifact dir
    Add-Key -Name $instance -OutputDirectory $artifactDir
    Write-Host "[OK] SSH keys written: $privKeyPath / $pubKeyPath" -ForegroundColor Green

    # Prepare template variables
    $variables = @{
        'INSTANCE'         = $instance
        'OPERATING_SYSTEM' = $os
        'OS_VERSION'       = $osVersion
        'BASE_VM'          = $baseVmObj.Id   # <- Use the CIM ID
        'HOSTNAME'         = $hostname
        'USERNAME'         = $username
        'TIMEZONE'         = $timezone
        'LAYOUT'           = $layout
        'LOCALE'           = $locale
        'PRIVKEY'          = (Get-Content -Raw -Path $pubKeyPath)
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

    # Create dummy seed.iso (placeholder — implement ISO creation later)
    Set-Content -Path $seedIsoPath -Value "[placeholder for seed.iso]" -Encoding utf8
    Write-Host "[INFO] Created dummy seed.iso (implement ISO creation later)" -ForegroundColor Yellow

    Write-Host "[✅] VM artifact created successfully in: $artifactDir" -ForegroundColor Green
    return $artifactDir
}

Export-ModuleMember -Function Publish-VmArtifact
