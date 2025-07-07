# bin/modules/vmgenie-service.psm1

# Requires PowerShell 5.1+ or Core
# .NET 8.0 is expected and validated by the bootstrap script already.

# Constants
$ServiceName      = "VmGenie"
$DisplayName      = "VmGenie Service"
$PublishConfig    = "Release"
$Runtime          = "win-x64"
$ProjectPath      = Resolve-Path "$PSScriptRoot\..\..\src\vmgenie.csproj"
$PublishOutput    = Resolve-Path "$PSScriptRoot\..\..\bin\Release\net8.0\win-x64\publish"
$ServiceTargetDir = "C:\Program Files\VmGenie\Service"  # Customize as desired

function Build-VmGenieService {
<#
.SYNOPSIS
Publishes the VmGenie service to the publish directory.
#>
    Write-Host "🔨 Building (publishing) VmGenie Service..." -ForegroundColor Cyan
    Push-Location (Split-Path $ProjectPath)

    dotnet publish $ProjectPath `
        -c $PublishConfig `
        -r $Runtime `
        --self-contained `
        -p:PublishSingleFile=false

    if ($LASTEXITCODE -ne 0) {
        throw "❌ dotnet publish failed."
    }

    Pop-Location
    Write-Host "✅ VmGenie Service published to: $PublishOutput" -ForegroundColor Green
}

function Install-VmGenieService {
<#
.SYNOPSIS
Installs the VmGenie service with Windows Service Control Manager.
#>
    Write-Host "🧰 Installing VmGenie Service..." -ForegroundColor Cyan

    Build-VmGenieService

    # Stop service if already installed
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        Write-Host "ℹ️ Service already exists. Stopping & removing existing service..."
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

    Write-Host "📦 Registering Windows Service: $ServiceName → $exePath"
    sc.exe create $ServiceName binPath= "`"$exePath`"" DisplayName= "`"$DisplayName`"" start= auto | Out-Null

    Start-VmGenieService
    Write-Host "🎉 VmGenie Service installed & started!" -ForegroundColor Green
}

function Start-VmGenieService {
<#
.SYNOPSIS
Starts the VmGenie service.
#>
    Write-Host "🚀 Starting VmGenie Service..." -ForegroundColor Cyan
    Start-Service -Name $ServiceName
    Write-Host "✅ VmGenie Service started." -ForegroundColor Green
}

function Stop-VmGenieService {
<#
.SYNOPSIS
Stops the VmGenie service.
#>
    Write-Host "🛑 Stopping VmGenie Service..." -ForegroundColor Cyan
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    Write-Host "✅ VmGenie Service stopped." -ForegroundColor Green
}

function Restart-VmGenieService {
<#
.SYNOPSIS
Restarts the VmGenie service.
#>
    Stop-VmGenieService
    Start-VmGenieService
}

Export-ModuleMember -Function `
    Build-VmGenieService, `
    Install-VmGenieService, `
    Start-VmGenieService, `
    Stop-VmGenieService, `
    Restart-VmGenieService
