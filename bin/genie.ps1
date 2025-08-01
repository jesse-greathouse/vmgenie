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
    gmi        = 'Invoke-GenieGmi'
}

if (-not $script:action -or $script:action -eq 'help') {
    Show-GenieHelp
    exit 0
}

if ($actionHandlers.ContainsKey($script:action)) {
    $handlerFn = $actionHandlers[$script:action]

    if ($script:action -eq 'gmi') {
        # Defensive: build remainingArgs as an array no matter what
        $remainingArgs = @()
        if ($args.Count -gt 1) {
            $remainingArgs = @($args[1..($args.Count - 1)])
        }

        # Subaction is first non-flag positional, or "help" if none
        $subAction = if ($remainingArgs.Count -ge 1 -and $remainingArgs[0] -notmatch '^-') { $remainingArgs[0] } else { "help" }

        # Now remove the subAction from the args **only if it was present**
        if ($subAction -ne "help" -and $remainingArgs.Count -ge 2) {
            # This ensures [1..N] only when N >= 1
            $remainingArgs = @($remainingArgs[1..($remainingArgs.Count - 1)])
        }
        else {
            $remainingArgs = @()
        }

        $archive = $null
        $options = @{}

        for ($i = 0; $i -lt $remainingArgs.Count; $i++) {
            $argStr = [string]$remainingArgs[$i]
            if ($argStr -eq '-Archive' -and ($i + 1) -lt $remainingArgs.Count) {
                $archive = $remainingArgs[$i + 1]
                $i++
            }
            elseif ($argStr -like '-*') {
                $optName = $argStr.TrimStart('-')
                if (($i + 1) -lt $remainingArgs.Count -and $remainingArgs[$i + 1] -notlike '-*') {
                    $options[$optName] = $remainingArgs[$i + 1]
                    $i++
                }
                else {
                    $options[$optName] = $true
                }
            }
        }

        & $handlerFn -SubAction $subAction -Archive $archive -Options $options
        exit 0
    }
    else {
        & $handlerFn -InstanceName $script:instanceName -Options $script:options
        exit 0
    }
}
else {
    Write-Host ("[❌] Unknown command: '{0}'" -f $script:action) -ForegroundColor Red
    Write-Host "For usage, try: genie help" -ForegroundColor Yellow
    Show-GenieHelp
    exit 1
}
