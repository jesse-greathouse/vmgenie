# bin/status.ps1
# Checks VmGenie Service status and prints simple result

$ErrorActionPreference = 'Stop'
chcp 65001 > $null
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repoRoot = Resolve-Path "$PSScriptRoot\.."
$modulePath = Join-Path $repoRoot 'bin\modules\vmgenie-service.psm1'

if (-not (Test-Path $modulePath)) {
    Write-Error "Could not find vmgenie-service.psm1 at: $modulePath"
    exit 1
}

Import-Module $modulePath -Force

if (Test-VmGenieStatus) {
    Write-Host "✅ Service is up" -ForegroundColor Green
    exit 0
} else {
    Write-Host "❌ Service is down" -ForegroundColor Red
    exit 1
}
