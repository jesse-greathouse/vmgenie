Import-Module "$PSScriptRoot\vmgenie-import.psm1"
Import-Module "$PSScriptRoot\vmgenie-prompt.psm1"
Import-Module "$PSScriptRoot\vmgenie-client.psm1"
Import-Module "$PSScriptRoot\vmgenie-config.psm1"
Import-YamlDotNet

function Export-Gmi {
    <#
.SYNOPSIS
    Export a Genie Machine Image (GMI) as a .zip archive via the backend service.
.DESCRIPTION
    Prompts the user to select an unprovisioned base VM (GMI), then calls the service to export it as a GMI artifact.
#>
    [CmdletBinding()]
    param ()

    $cfg = Get-Configuration
    if (-not $cfg.ContainsKey('GMI_DIR') -or [string]::IsNullOrWhiteSpace($cfg['GMI_DIR'])) {
        throw "[❌] GMI_DIR not set in configuration."
    }
    $gmiDir = $cfg['GMI_DIR']

    # Prompt the user for the base GMI (exclude provisioned VMs)
    $gmiObj = Invoke-VmPrompt -Provisioned 'exclude'
    if (-not $gmiObj) {
        throw "[❌] No GMI selected for export."
    }
    $gmiGuid = $gmiObj.Id
    $gmiName = $gmiObj.Name

    $manifestFile = Join-Path -Path $gmiDir -ChildPath ("$($gmiName -replace ' ', '-')" + ".yml")

    if (-not (Test-Path $manifestFile)) {
        Write-Host "[ℹ️ ] No manifest found for this GMI. Beginning guided setup..." -ForegroundColor Yellow
        New-GmiManifest -GmiName $gmiName -ManifestPath $manifestFile
    }

    Write-Host "V Selected GMI: $gmiName [ID: $gmiGuid]" -ForegroundColor Cyan

    # Call the 'gmi' command with action 'export'
    $script:ExportError = $null
    $script:ExportResult = $null

    $parameters = @{
        action = 'export'
        id     = $gmiGuid
    }

    Write-Host "[⚙ ] Exporting VM $gmiName ..." -ForegroundColor Yellow

    Send-Event -Command 'gmi' -Parameters $parameters -Handler {
        param ($Response)
        if ($Response.status -ne 'ok') {
            $script:ExportError = $Response.data
        }
        else {
            $script:ExportResult = $Response.data.archive
        }
        Complete-Request -Id $Response.id
    } -TimeoutSeconds 300 | Out-Null

    if ($script:ExportError) {
        throw "[❌] Export failed: $script:ExportError"
    }

    if ($null -eq $script:ExportResult -or -not $script:ExportResult.path) {
        throw "[❌] No archive returned from GMI export operation."
    }

    Write-Host "[✅] GMI '$gmiName' exported successfully to archive:" -ForegroundColor Green
    Write-Host "     $($script:ExportResult.path)" -ForegroundColor Green

    return $script:ExportResult
}

function Import-Gmi {
    <#
.SYNOPSIS
    Import a Genie Machine Image (GMI) from a .zip archive via the backend service.
.DESCRIPTION
    Lets the user select a GMI archive from GMI_DIR or specify one via parameter, and imports it via the service.
.PARAMETER Archive
    The path to the GMI .zip archive. If omitted, prompts to select from available archives.
#>
    [CmdletBinding()]
    param (
        [string] $Archive
    )

    # Prompt if not provided
    if (-not $Archive) {
        try {
            $Archive = Invoke-GmiArchivePrompt
        }
        catch {
            throw "[❌] No GMI archive selected: $_"
        }
    }

    if (-not (Test-Path $Archive)) {
        throw "[❌] Archive file not found: $Archive"
    }

    $script:ImportResult = $null
    $script:ImportError = $null

    $parameters = @{
        action  = 'import'
        archive = $Archive
    }

    Write-Host "[⚙ ] Importing GMI archive $Archive ..." -ForegroundColor Yellow

    Send-Event -Command 'gmi' -Parameters $parameters -Handler {
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

    if (-not $script:ImportResult) {
        throw "[❌] No result returned from GMI import operation."
    }

    Write-Host "[✅] GMI imported successfully: $($script:ImportResult.gmiName)" -ForegroundColor Green

    return $script:ImportResult
}

function New-GmiManifest {
    <#
    .SYNOPSIS
        Interactive guided creation of a GMI manifest YAML file.
    .PARAMETER GmiName
        The display name of the GMI/VM, e.g. "GMI Ubuntu 24.04"
    .PARAMETER ManifestPath
        The full path where the YAML should be saved.
    #>
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string] $GmiName,
        [Parameter(Mandatory = $true)]
        [string] $ManifestPath
    )

    # Interactive prompts
    $os = Invoke-OperatingSystemPrompt
    $version = Invoke-OsVersionPrompt -OperatingSystem $os
    $maintainer = Invoke-MaintainerPrompt
    $maintainerEmail = Invoke-MaintainerEmailPrompt
    $sourceUrl = Invoke-SourceUrlPrompt
    $description = Invoke-DescriptionPrompt

    $nowUtc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

    $gmiMetadata = [ordered]@{
        gmi_version       = "1.0"
        os                = $os
        version           = $version
        created           = $nowUtc
        updated           = $nowUtc
        vm_name           = $GmiName
        hyperv_generation = 2
        maintainer        = $maintainer
        maintainer_email  = $maintainerEmail
        source_url        = $sourceUrl
        description       = $description
        checksum_sha256   = ""
    }

    # Ensure directory exists
    $manifestDir = Split-Path -Path $ManifestPath -Parent
    if (-not (Test-Path $manifestDir)) {
        New-Item -Path $manifestDir -ItemType Directory | Out-Null
    }

    # Serialize to YAML
    $builder = New-Object YamlDotNet.Serialization.SerializerBuilder
    $serializer = $builder.Build()
    $writer = New-Object System.IO.StringWriter
    $serializer.Serialize($writer, $gmiMetadata)
    $writer.Flush()
    $yaml = $writer.ToString()

    Set-Content -Path $ManifestPath -Value $yaml -Encoding UTF8

    Write-Host "[📝] GMI manifest created at: $ManifestPath" -ForegroundColor Green

    return $ManifestPath
}

Export-ModuleMember -Function `
    Export-Gmi, `
    Import-Gmi, `
    New-GmiManifest
