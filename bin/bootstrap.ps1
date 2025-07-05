# bin/bootstrap.ps1
# Validates host system for vmgenie. Returns 0 on success, 1 on failure.

chcp 65001 > $null
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Write-Host '[INFO] Running vmgenie host validation...' -ForegroundColor Cyan

function Fail($msg) {
    Write-Host "[FAIL] $msg" -ForegroundColor Red
    exit 1
}

# region --- Check if running as Administrator ---
if (-not ([Security.Principal.WindowsPrincipal] `
          [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(`
          [Security.Principal.WindowsBuiltinRole]::Administrator)) {
    Fail 'This script must be run from an elevated (Administrator) PowerShell session.'
} else {
    Write-Host '[OK] Running as Administrator' -ForegroundColor Green
}

# region --- Check Windows edition ---
$winEdition = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').EditionID
if ($winEdition -notin @('Professional', 'Enterprise', 'Education')) {
    Fail "Windows Edition '$winEdition' does NOT support Hyper-V."
} else {
    Write-Host "[OK] Windows Edition: $winEdition" -ForegroundColor Green
}

# region --- Check Windows build number ---
$winBuild = [int](Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').CurrentBuild
if ($winBuild -lt 19041) {
    Fail "Windows build $winBuild is too old. Requires at least 19041 (Windows 10 2004)."
} else {
    Write-Host "[OK] Windows build: $winBuild" -ForegroundColor Green
}

# region --- Check Hyper-V feature ---
$hvFeature = Get-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V-All
if ($hvFeature.State -ne 'Enabled') {
    Fail 'Hyper-V feature is not enabled. Enable it and reboot before continuing.'
} else {
    Write-Host '[OK] Hyper-V is enabled' -ForegroundColor Green
}

# region --- Check .NET SDK ---
try {
    $dotnetVersion = & dotnet --version
    if ($LASTEXITCODE -ne 0) { throw }
    Write-Host "[OK] .NET SDK: $dotnetVersion" -ForegroundColor Green
} catch {
    Fail 'The .NET SDK is not installed or not in PATH. Install it from https://dotnet.microsoft.com/download'
}

Write-Host ''
Write-Host '[DONE] Host validation complete. Environment is ready!' -ForegroundColor Green
exit 0
