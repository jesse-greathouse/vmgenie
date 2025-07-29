Import-Module "$PSScriptRoot/vmgenie-vm.psm1"
Import-Module "$PSScriptRoot/vmgenie-gmi.psm1"
Import-Module "$PSScriptRoot/vmgenie-help.psm1"

function Invoke-GenieStart {
    param(
        [string]$InstanceName,
        [hashtable]$Options
    )
    if ($Options.ContainsKey('Help')) {
        Show-GenieHelpStart
        exit 0
    }
    if ($InstanceName) {
        Start-VMInstance -InstanceName $InstanceName
    }
    else {
        Start-VMInstance
    }
}

function Invoke-GenieStop {
    param(
        [string]$InstanceName,
        [hashtable]$Options
    )
    if ($Options.ContainsKey('Help')) {
        Show-GenieHelpStop
        exit 0
    }
    if ($Options.ContainsKey('Force')) {
        if ($InstanceName) {
            Stop-VMInstance -InstanceName $InstanceName
        }
        else {
            Stop-VMInstance
        }
    }
    else {
        if ($InstanceName) {
            Stop-VMInstanceGracefully -InstanceName $InstanceName
        }
        else {
            Stop-VMInstanceGracefully
        }
    }
}

function Invoke-GeniePause {
    param(
        [string]$InstanceName,
        [hashtable]$Options
    )
    if ($Options.ContainsKey('Help')) {
        Show-GenieHelpPause
        exit 0
    }
    if ($InstanceName) {
        Suspend-VMInstance -InstanceName $InstanceName
    }
    else {
        Suspend-VMInstance
    }
}

function Invoke-GenieResume {
    param(
        [string]$InstanceName,
        [hashtable]$Options
    )
    if ($Options.ContainsKey('Help')) {
        Show-GenieHelpResume
        exit 0
    }
    if ($InstanceName) {
        Resume-VMInstance -InstanceName $InstanceName
    }
    else {
        Resume-VMInstance
    }
}

function Invoke-GeniePs {
    param(
        [string]$InstanceName,
        [hashtable]$Options
    )
    # `ps` does not use instance name or options, just list VMs
    if ($Options.ContainsKey('Help')) {
        Show-GenieHelpPs
        exit 0
    }
    Get-VMInstanceStatus
}

function Invoke-GenieConnect {
    param(
        [string]$InstanceName,
        [hashtable]$Options
    )
    if ($Options.ContainsKey('Help')) {
        Show-GenieHelpConnect
        exit 0
    }
    if ($InstanceName) {
        Connect-VMInstance -InstanceName $InstanceName
    }
    else {
        Connect-VMInstance
    }
}

function Invoke-GenieProvision {
    param(
        [string]$InstanceName,
        [hashtable]$Options
    )
    if ($Options.ContainsKey('Help')) {
        Show-GenieHelpProvision
        exit 0
    }
    Invoke-ProvisionVm -InstanceName $InstanceName
}

function Invoke-GenieDelete {
    param(
        [string]$InstanceName,
        [hashtable]$Options
    )
    if ($Options.ContainsKey('Help')) {
        Show-GenieHelpDelete
        exit 0
    }
    Remove-VMInstance -InstanceName $InstanceName
}

function Invoke-GenieSwapIso {
    param(
        [string]$InstanceName,
        [hashtable]$Options
    )
    if ($Options.ContainsKey('Help')) {
        Show-GenieHelpSwapIso
        exit 0
    }
    $isoPath = $null
    if ($Options.ContainsKey('IsoPath')) {
        $isoPath = $Options['IsoPath']
    }
    Update-VMInstanceIso -InstanceName $InstanceName -IsoPath $isoPath
}

function Invoke-GenieBackup {
    param(
        [string]$InstanceName,
        [hashtable]$Options
    )
    if ($Options.ContainsKey('Help')) {
        Show-GenieHelpBackup
        exit 0
    }
    Export-VMInstance -InstanceName $InstanceName
}

function Invoke-GenieRestore {
    param(
        [string]$InstanceName,
        [hashtable]$Options
    )
    if ($Options.ContainsKey('Help')) {
        Show-GenieHelpRestore
        exit 0
    }
    Import-VMInstance -InstanceName $InstanceName -Mode 'restore'
}

function Invoke-GenieCopy {
    param(
        [string]$InstanceName,
        [hashtable]$Options
    )

    if ($Options.ContainsKey('Help')) {
        Show-GenieHelpCopy
        exit 0
    }

    $NewInstanceName = $null
    if ($Options.ContainsKey('NewInstanceName')) {
        $NewInstanceName = $Options['NewInstanceName']
    }

    # -Mode 'copy' is required; Import-VMInstance will prompt for new name if not provided.
    Import-VMInstance -InstanceName $InstanceName -Mode 'copy' -NewInstanceName $NewInstanceName
}

function Invoke-GenieGmi {
    param(
        [string]$SubAction,
        [string]$Archive,
        [hashtable]$Options
    )

    switch ($SubAction.ToLower()) {
        'export' {
            if ($Options.ContainsKey('Help')) {
                Show-GenieHelpGmiExport
                exit 0
            }
            Export-Gmi
            break
        }
        'import' {
            if ($Options.ContainsKey('Help')) {
                Show-GenieHelpGmiImport
                exit 0
            }
            Import-Gmi -Archive $Archive
            break
        }
        'help' {
            Show-GenieHelpGmi
            exit 0
        }
        default {
            Show-GenieHelpGmi
            exit 1
        }
    }
}

Export-ModuleMember -Function `
    Invoke-GenieStart, `
    Invoke-GenieStop, `
    Invoke-GeniePause, `
    Invoke-GenieResume, `
    Invoke-GeniePs, `
    Invoke-GenieConnect, `
    Invoke-GenieProvision, `
    Invoke-GenieDelete, `
    Invoke-GenieSwapIso, `
    Invoke-GenieBackup, `
    Invoke-GenieRestore, `
    Invoke-GenieCopy, `
    Invoke-GenieGmi
