chcp 65001 > $null
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$ServiceName = "VmGenie"
$DisplayName = "VmGenie Service"
$PublishConfig = "Release"
$Runtime = "win-x64"
$ProjectPath = Resolve-Path "$PSScriptRoot\..\..\src\vmgenie.csproj"
$ServiceTargetDir = Join-Path ([Environment]::GetFolderPath("ProgramFiles")) "VmGenie\Service"

function Publish-VmGenieService {
    Write-Host "[INFO] Building (publishing) VmGenie Service..." -ForegroundColor Cyan
    Push-Location (Split-Path $ProjectPath)

    # Find csproj directory (should be ...\src)
    $projectDir = Split-Path $ProjectPath

    # Compose expected publish output
    $publishOutputPath = Join-Path $projectDir 'bin\Release\net8.0-windows\win-x64\publish'

    $null = dotnet publish $ProjectPath `
        -c $PublishConfig `
        -r $Runtime `
        --self-contained `
        -p:PublishSingleFile=false

    if ($LASTEXITCODE -ne 0) {
        Pop-Location
        throw "[FAIL] dotnet publish failed."
    }

    if (-not (Test-Path $publishOutputPath)) {
        Pop-Location
        throw "[FAIL] Publish output directory '$publishOutputPath' does not exist."
    }

    Pop-Location
    Write-Host "[OK] VmGenie Service published to: $publishOutputPath" -ForegroundColor Green
    return $publishOutputPath
}

function Install-VmGenieService {
    <#
    .SYNOPSIS
    Installs the VmGenie service with Windows Service Control Manager.
    #>
    Write-Host "[INFO] Installing VmGenie Service..." -ForegroundColor Cyan

    $PublishOutput = Publish-VmGenieService

    # Stop service if already installed
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        Write-Host "[INFO] Service already exists. Stopping & removing existing service..."
        Stop-VmGenieService -ErrorAction SilentlyContinue
        sc.exe delete $ServiceName | Out-Null
        Start-Sleep -Seconds 2
    }

    # Deploy binaries
    if (-not (Test-Path $ServiceTargetDir)) {
        New-Item -ItemType Directory -Path $ServiceTargetDir -Force | Out-Null
    }
    Copy-Item -Path "$PublishOutput\*" -Destination $ServiceTargetDir -Recurse -Force

    # Install service (elevated required)
    $exePath = Join-Path $ServiceTargetDir "vmgenie.exe"  # name after .csproj output

    Write-Host "[INFO] Registering Windows Service: $ServiceName -- $exePath"
    sc.exe create $ServiceName binPath= "`"$exePath`"" DisplayName= "`"$DisplayName`"" start= auto | Out-Null

    # --- Set the service description ---
    $ServiceDescription = "Automated, reproducible Hyper V VM provisioning made easy. (https://github.com/jesse-greathouse/vmgenie)"
    sc.exe description $ServiceName "$ServiceDescription"

    Start-VmGenieService
    Write-Host "[INFO] VmGenie Service installed & started!" -ForegroundColor Green
}

function Start-VmGenieService {
    <#
    .SYNOPSIS
    Starts the VmGenie service. Fails with explicit error if unsuccessful.
    #>
    Write-Host "[INFO] Starting VmGenie Service..." -ForegroundColor Cyan
    try {
        Start-Service -Name $ServiceName -ErrorAction Stop
        Write-Host "[OK] VmGenie Service started." -ForegroundColor Green
    }
    catch {
        Write-Host "[FAIL] Could not start VmGenie Service: $($_.Exception.Message)" -ForegroundColor Red
        throw
    }
}

function Stop-VmGenieService {
    <#
    .SYNOPSIS
    Stops the VmGenie service. Fails with explicit error if unsuccessful.
    #>
    Write-Host "[INFO] Stopping VmGenie Service..." -ForegroundColor Cyan
    try {
        Stop-Service -Name $ServiceName -Force -ErrorAction Stop
        Write-Host "[OK] VmGenie Service stopped." -ForegroundColor Green
    }
    catch {
        Write-Host "[WARN] Could not stop VmGenie Service (may not be running): $($_.Exception.Message)" -ForegroundColor Yellow
        # Don't throw, since it's not always fatal
    }
}

function Restart-VmGenieService {
    <#
    .SYNOPSIS
    Restarts the VmGenie service. Fails with explicit error if unsuccessful.
    #>
    Write-Host "[INFO] Restarting VmGenie Service..." -ForegroundColor Cyan
    try {
        Stop-VmGenieService
        Start-VmGenieService
        Write-Host "[OK] VmGenie Service restarted." -ForegroundColor Green
    }
    catch {
        Write-Host "[FAIL] Could not restart VmGenie Service: $($_.Exception.Message)" -ForegroundColor Red
        throw
    }
}

function Test-VmGenieStatus {
    <#
    .SYNOPSIS
    Queries the VmGenie Service via the "status" command and returns $true if it responds OK.
    #>
    $repoRoot = Resolve-Path "$PSScriptRoot\..\.."
    $clientModulePath = Join-Path $repoRoot 'bin\modules\vmgenie-client.psm1'

    if (-not (Test-Path $clientModulePath)) {
        throw "Could not find vmgenie-client.psm1 at: $clientModulePath"
    }

    Import-Module $clientModulePath -Force

    $responseStatus = $false
    $responseStatusRef = [ref]$responseStatus

    try {
        $null = Send-Event -Command "status" -Parameters @{} -Handler {
            param($Response)

            if ($Response.status -eq 'ok') {
                $responseStatusRef.Value = $true
            }

            Complete-Request -Id $Response.id
        } -TimeoutSeconds 3 | Out-Null
    }
    catch {
        # swallow connection errors silently — service is likely down
        $responseStatusRef.Value = $false
    }

    return $responseStatusRef.Value
}

function Remove-VmGenieService {
    <#
    .SYNOPSIS
        Stops, unregisters, and deletes the VmGenie Windows Service and its program files directory.
    .DESCRIPTION
        - Stops the VmGenie service (if running)
        - Deletes the service from Windows Service Control Manager (SCM)
        - Removes the published application directory under Program Files
    #>
    Write-Host "[INFO] Removing VmGenie Windows Service..." -ForegroundColor Cyan

    # Stop the service (no error if already stopped)
    try {
        Stop-VmGenieService -ErrorAction SilentlyContinue
    }
    catch {
        Write-Host "[WARN] Failed to stop service (may not be running): $_" -ForegroundColor Yellow
    }

    # Delete the service from the SCM
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($service) {
        Write-Host "[INFO] Deleting service from SCM: $ServiceName"
        sc.exe delete $ServiceName | Out-Null
        Start-Sleep -Seconds 2
    }
    else {
        Write-Host "[INFO] Service $ServiceName does not exist in SCM (already removed)." -ForegroundColor Yellow
    }

    # Remove the published application directory
    $publishedRoot = Join-Path ([Environment]::GetFolderPath("ProgramFiles")) "VmGenie"
    if (Test-Path $publishedRoot) {
        try {
            Remove-Item -Path $publishedRoot -Recurse -Force
            Write-Host "[OK] Removed program directory: $publishedRoot" -ForegroundColor Green
        }
        catch {
            Write-Warning "[FAIL] Failed to remove program directory: $publishedRoot -- $_"
        }
    }
    else {
        Write-Host "[INFO] No program directory found at $publishedRoot (already removed)." -ForegroundColor Yellow
    }

    Write-Host "[DONE] VmGenie Service removal complete." -ForegroundColor Green
}


Export-ModuleMember -Function `
    Publish-VmGenieService, `
    Install-VmGenieService, `
    Start-VmGenieService, `
    Stop-VmGenieService, `
    Restart-VmGenieService, `
    Test-VmGenieStatus, `
    Remove-VmGenieService
