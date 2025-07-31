function Get-GenieArgs {
    param([string[]]$Arguments)

    $action = 'help'
    $instanceName = $null
    $options = @{}

    $argList = @($Arguments)

    # Parse action (first positional, not starting with '-')
    if ($argList.Count -ge 1 -and $argList[0] -notmatch '^-') {
        $action = $argList[0]
        if ($argList.Count -gt 1) {
            $argList = $argList[1..($argList.Count - 1)]
        }
        else {
            $argList = @()
        }
    }

    # Parse instanceName (second positional, not starting with '-')
    if ($argList.Count -ge 1 -and $argList[0] -notmatch '^-') {
        $instanceName = $argList[0]
        if ($argList.Count -gt 1) {
            $argList = $argList[1..($argList.Count - 1)]
        }
        else {
            $argList = @()
        }
    }

    # Enhanced option parsing: support flags (no value after option)
    $i = 0
    while ($i -lt $argList.Count) {
        $key = $argList[$i]
        if ($key -notmatch '^-') {
            Write-Host "Error: Option '$key' must start with '-'." -ForegroundColor Red
            Write-Host "For usage, try: genie $action -Help" -ForegroundColor Yellow
            exit 1
        }
        $optName = $key.TrimStart('-')

        # Look ahead for value
        if (($i + 1) -lt $argList.Count -and $argList[$i + 1] -notmatch '^-') {
            $val = $argList[$i + 1]
            $options[$optName] = $val
            $i += 2
        }
        else {
            # No value: treat as flag
            $options[$optName] = $null
            $i += 1
        }
    }

    [PSCustomObject]@{
        Action       = $action
        InstanceName = $instanceName
        Options      = $options
    }
}

function Show-GenieHelp {
    <#
    .SYNOPSIS
        Lists available VmGenie CLI actions and usage.
    #>
    Write-Host ""
    Write-Host "VmGenie Command-Line Tool" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Usage:" -ForegroundColor Yellow
    Write-Host "  genie <action> [InstanceName] [options...]" -ForegroundColor White
    Write-Host ""
    Write-Host "  <action>        The command to execute (see list below)"
    Write-Host "  [InstanceName]  (Optional) The name of the VM instance. If omitted, you will be prompted to select one interactively."
    Write-Host ""
    Write-Host "Available Actions:" -ForegroundColor Yellow
    Write-Host "  ps             List all VM instances and their current status."
    Write-Host "  connect        Connect to a VM instance via SSH."
    Write-Host "  provision      Create and initialize a new VM instance."
    Write-Host "  start          Start an existing VM instance."
    Write-Host "  stop           Stop a running VM instance."
    Write-Host "  pause          Pause a running VM."
    Write-Host "  resume         Resume a paused VM."
    Write-Host "  delete         Delete a VM and all its data."
    Write-Host "  backup         Backup a VM to a zip archive."
    Write-Host "  restore        Restore a VM from a backup archive."
    Write-Host "  copy           Make a copy of a VM (creates a new instance)."
    Write-Host "  gmi            Manage GMI images."
    Write-Host "  swap-iso       Attach a new ISO to a VM."
    Write-Host ""
    Write-Host "For help on a specific action: genie <action> help"
    Write-Host ""
    Write-Host "Examples:" -ForegroundColor Yellow
    Write-Host "  genie ps"
    Write-Host "  genie backup mylab"
    Write-Host "  genie restore -Archive var\\export\\mylab.zip"
    Write-Host "  genie copy mylab -NewInstanceName mylab-copy"
    Write-Host "  genie provision mylab -Os Ubuntu -Version 24.04"
    Write-Host ""
    Write-Host "Note:" -ForegroundColor Yellow
    Write-Host "  If [InstanceName] is omitted, you will be prompted to select one interactively."
    Write-Host ""
}

function Show-GenieHelpProvision {
    <#
    .SYNOPSIS
        Help for the 'provision' action.
    #>
    Write-Host ""
    Write-Host "provision: Create and initialize a new VM instance." -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Usage:"
    Write-Host "  genie provision <name>"
    Write-Host ""
    Write-Host "Description:"
    Write-Host "  Provisions a VM instance using the artifact for <name> in var/cloud/<name>."
    Write-Host "  If the artifact does not exist, you will be guided through artifact creation."
    Write-Host ""
    Write-Host "Example:"
    Write-Host "  genie provision mylab"
    Write-Host ""
}

function Show-GenieHelpStart {
    <#
    .SYNOPSIS
        Help for the 'start' action.
    #>
    Write-Host ""
    Write-Host "start: Start an existing VM instance." -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Usage:"
    Write-Host "  genie start <name>"
    Write-Host ""
    Write-Host "Starts the VM specified by instance name."
    Write-Host ""
    Write-Host "Example:"
    Write-Host "  genie start mylab"
    Write-Host ""
}

function Show-GenieHelpStop {
    Write-Host ""
    Write-Host "stop: Gracefully shuts down a VM, or force stops with -Force." -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Usage:"
    Write-Host "  genie stop <name> [-Force]"
    Write-Host ""
    Write-Host "Options:"
    Write-Host "  -Force   Force stop the VM (equivalent to pulling the plug)."
    Write-Host ""
    Write-Host "Example:"
    Write-Host "  genie stop mylab"
    Write-Host "  genie stop mylab -Force"
    Write-Host ""
}

function Show-GenieHelpPause {
    Write-Host ""
    Write-Host "pause: Pause (suspend) a running VM." -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Usage:"
    Write-Host "  genie pause <name>"
    Write-Host ""
    Write-Host "Example:"
    Write-Host "  genie pause mylab"
    Write-Host ""
}

function Show-GenieHelpResume {
    Write-Host ""
    Write-Host "resume: Resume a paused VM." -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Usage:"
    Write-Host "  genie resume <name>"
    Write-Host ""
    Write-Host "Example:"
    Write-Host "  genie resume mylab"
    Write-Host ""
}

function Show-GenieHelpPs {
    Write-Host ""
    Write-Host "ps: List all VM instances and their current status." -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Usage:"
    Write-Host "  genie ps"
    Write-Host ""
    Write-Host "Example:"
    Write-Host "  genie ps"
    Write-Host ""
}

function Show-GenieHelpConnect {
    Write-Host ""
    Write-Host "connect: Connect to a VM via SSH (starts it if needed)." -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Usage:"
    Write-Host "  genie connect <name>"
    Write-Host ""
    Write-Host "Example:"
    Write-Host "  genie connect mylab"
    Write-Host ""
}

function Show-GenieHelpDelete {
    Write-Host ""
    Write-Host "delete: Delete a VM and all its data." -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Usage:"
    Write-Host "  genie delete <name>"
    Write-Host ""
    Write-Host "Example:"
    Write-Host "  genie delete mylab"
    Write-Host ""
}
function Show-GenieHelpSwapIso {
    Write-Host ""
    Write-Host "swap-iso: Attach or swap the ISO for a VM." -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Usage:"
    Write-Host "  genie swap-iso <name> [-IsoPath <path>]"
    Write-Host ""
    Write-Host "Example:"
    Write-Host "  genie swap-iso mylab -IsoPath C:\my\seed.iso"
    Write-Host ""
}

function Show-GenieHelpBackup {
    Write-Host ""
    Write-Host "backup: Export a VM instance to a zip archive." -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Usage:"
    Write-Host "  genie backup <name>"
    Write-Host ""
    Write-Host "Description:"
    Write-Host "  Creates a backup archive (zip) of the specified VM instance and all artifacts."
    Write-Host ""
    Write-Host "Example:"
    Write-Host "  genie backup mylab"
    Write-Host ""
}

function Show-GenieHelpRestore {
    Write-Host ""
    Write-Host "restore: Restore a VM from a backup archive." -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Usage:"
    Write-Host "  genie restore <name>"
    Write-Host ""
    Write-Host "Description:"
    Write-Host "  Restores the specified VM instance from a backup archive. The VM must be stopped before restore."
    Write-Host ""
    Write-Host "Example:"
    Write-Host "  genie restore mylab"
    Write-Host ""
}

function Show-GenieHelpCopy {
    Write-Host ""
    Write-Host "copy: Clone a VM from an existing backup archive as a new instance." -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Usage:"
    Write-Host "  genie copy <source-instance> [-NewInstanceName <new-instance>]"
    Write-Host ""
    Write-Host "Description:"
    Write-Host "  Copies the specified VM from a backup archive and provisions it as a new, separate instance."
    Write-Host "  If -NewInstanceName is not provided, you will be prompted to enter one interactively."
    Write-Host ""
    Write-Host "Options:"
    Write-Host "  -NewInstanceName   (Optional) Name for the new, cloned VM instance."
    Write-Host ""
    Write-Host "Example:"
    Write-Host "  genie copy test1 -NewInstanceName test1-copy"
    Write-Host "  genie copy test1         # prompts for new instance name"
    Write-Host ""
}

function Show-GenieHelpGmi {
    Write-Host ""
    Write-Host "gmi: Manage Genie Machine Images (GMI) for portable base VM images." -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Usage:" -ForegroundColor Yellow
    Write-Host "  genie gmi <subaction> [options]" -ForegroundColor White
    Write-Host ""
    Write-Host "Subactions:" -ForegroundColor Yellow
    Write-Host "  export               Export a Genie Machine Image (GMI) as a .zip archive."
    Write-Host "  import [-Archive]    Import a Genie Machine Image (GMI) from a .zip archive."
    Write-Host "  fetch [-Os] [-Version]   Download and import a GMI image from the online repository."
    Write-Host "  help                 Show this help message."
    Write-Host ""
    Write-Host "Examples:" -ForegroundColor Yellow
    Write-Host "  genie gmi export"
    Write-Host "  genie gmi import"
    Write-Host "  genie gmi import -Archive C:\\Users\\you\\vmgenie\\var\\gmi\\GMI-Ubuntu-24.04.zip"
    Write-Host "  genie gmi fetch"
    Write-Host "  genie gmi fetch -Os Ubuntu -Version 25.04"
    Write-Host ""
}

function Show-GenieHelpGmiExport {
    Write-Host ""
    Write-Host "gmi export: Export a Genie Machine Image as a .zip archive." -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Usage:"
    Write-Host "  genie gmi export"
    Write-Host ""
    Write-Host "Description:"
    Write-Host "  Prompts you to select an unprovisioned base VM (GMI), and exports it as a GMI artifact."
    Write-Host ""
}

function Show-GenieHelpGmiImport {
    Write-Host ""
    Write-Host "gmi import: Import a Genie Machine Image from a .zip archive." -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Usage:"
    Write-Host "  genie gmi import [-Archive <path>]"
    Write-Host ""
    Write-Host "Description:"
    Write-Host "  Lets you select a GMI archive to import, or provide the path to a .zip GMI archive."
    Write-Host ""
}

function Show-GenieHelpGmiFetch {
    Write-Host ""
    Write-Host "gmi fetch: Download and install a GMI package from the online repository." -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Usage:"
    Write-Host "  genie gmi fetch [-Os <name>] [-Version <version>]"
    Write-Host ""
    Write-Host "Description:"
    Write-Host "  Prompts you to select a GMI package from the repository, downloads it, and imports it for use."
    Write-Host "  Use -Os to filter by operating system, and -Version to filter by version (must be used with -Os)."
    Write-Host ""
    Write-Host "Examples:"
    Write-Host "  genie gmi fetch"
    Write-Host "  genie gmi fetch -Os Fedora"
    Write-Host "  genie gmi fetch -Os Ubuntu -Version 25.04"
    Write-Host ""
}

Export-ModuleMember -Function `
    Get-GenieArgs, `
    Show-GenieHelp, `
    Show-GenieHelpProvision, `
    Show-GenieHelpStart, `
    Show-GenieHelpStop, `
    Show-GenieHelpPause, `
    Show-GenieHelpResume, `
    Show-GenieHelpPs, `
    Show-GenieHelpConnect, `
    Show-GenieHelpDelete, `
    Show-GenieHelpBackup, `
    Show-GenieHelpRestore, `
    Show-GenieHelpCopy, `
    Show-GenieHelpSwapIso, `
    Show-GenieHelpGmi, `
    Show-GenieHelpGmiExport, `
    Show-GenieHelpGmiImport, `
    Show-GenieHelpGmiFetch
