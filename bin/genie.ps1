# Genie CLI for VmGenie
# Run this from any directory, always finds the installed module location

# Read APPLICATION_DIR from the registry
$regPath = 'HKLM:\SYSTEM\CurrentControlSet\Services\VmGenie\Parameters'
try {
    $repoRoot = (Get-ItemProperty -Path $regPath -Name 'APPLICATION_DIR').APPLICATION_DIR
    if (-not $repoRoot) {
        throw "APPLICATION_DIR not found in registry"
    }
}
catch {
    Write-Host "[FATAL] Could not find APPLICATION_DIR registry key. Did you run bin/bootstrap.ps1?" -ForegroundColor Red
    exit 1
}

# Import modules using the registry-resolved path
Import-Module -Force (Join-Path $repoRoot 'bin/modules/vmgenie-help.psm1')
Import-Module -Force (Join-Path $repoRoot 'bin/modules/vmgenie-cli-handlers.psm1')

$parsed = Get-GenieArgs $args
$script:action = $parsed.Action
$script:instanceName = $parsed.InstanceName
$script:options = $parsed.Options

$actionHandlers = @{
    start      = 'Invoke-GenieStart'
    stop       = 'Invoke-GenieStop'
    pause      = 'Invoke-GeniePause'
    resume     = 'Invoke-GenieResume'
    ps         = 'Invoke-GeniePs'
    connect    = 'Invoke-GenieConnect'
    provision  = 'Invoke-GenieProvision'
    delete     = 'Invoke-GenieDelete'
    'swap-iso' = 'Invoke-GenieSwapIso'
    backup     = 'Invoke-GenieBackup'
    restore    = 'Invoke-GenieRestore'
    copy       = 'Invoke-GenieCopy'
}

if (-not $script:action -or $script:action -eq 'help') {
    Show-GenieHelp
    exit 0
}

if ($actionHandlers.ContainsKey($script:action)) {
    $handlerFn = $actionHandlers[$script:action]
    & $handlerFn -InstanceName $script:instanceName -Options $script:options
    exit 0
}
else {
    Write-Host "Unknown or unimplemented action: $($script:action)" -ForegroundColor Red
    Show-GenieHelp
    exit 1
}
