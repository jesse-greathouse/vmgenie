# bin/uninstall-bootstrap.ps1
chcp 65001 > $null
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Write-Host "[INFO] Running elevated uninstallation for vmgenie..." -ForegroundColor Cyan

# Remove service via module
try {
    $serviceModule = Join-Path $PSScriptRoot "modules/vmgenie-service.psm1"
    Import-Module $serviceModule -Force
    Remove-VmGenieService
}
catch {
    Write-Warning "[WARN] Failed to remove VmGenie Windows Service: $_"
}

# Remove registry key (entire VmGenie service branch)
try {
    $serviceBasePath = 'HKLM:\SYSTEM\CurrentControlSet\Services\VmGenie'
    if (Test-Path $serviceBasePath) {
        Remove-Item -Path $serviceBasePath -Recurse -Force
        Write-Host "[OK] Registry key $serviceBasePath removed." -ForegroundColor Green
    }
    else {
        Write-Host "[INFO] Registry key $serviceBasePath is removed." -ForegroundColor Gray
    }
}
catch {
    Write-Warning "[WARN] Failed to remove registry key: $_"
}

Write-Host "[DONE] Elevated uninstallation complete." -ForegroundColor Cyan
exit 0
