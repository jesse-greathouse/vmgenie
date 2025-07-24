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
    Write-Host "  shutdown       Gracefully shut down a VM."
    Write-Host "  delete         Delete a VM and all its data."
    Write-Host "  backup         Backup a VM to a zip archive."
    Write-Host "  restore        Restore a VM from a backup archive."
    Write-Host "  copy           Make a copy of a VM (creates a new instance)."
    Write-Host "  import         Import a VM from an archive. (advanced)"
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
    Write-Host "  genie provision <name> -Os <os> -Version <version> [-VmSwitch <switch>]"
    Write-Host ""
    Write-Host "Description:"
    Write-Host "  Creates a new VM instance with the specified name, operating system, and version."
    Write-Host ""
    Write-Host "Options:"
    Write-Host "  -Os             Operating system (e.g. Ubuntu, Windows) (required)"
    Write-Host "  -Version        OS version (required)"
    Write-Host "  -VmSwitch       (Optional) Name of the virtual switch to connect"
    Write-Host ""
    Write-Host "Examples:"
    Write-Host "  genie provision mylab -Os Ubuntu -Version 24.04"
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

# ...repeat for stop, pause, resume, shutdown, etc...

# ...define more as needed...

Export-ModuleMember -Function `
    Show-GenieHelp, `
    Show-GenieHelpProvision, `
    Show-GenieHelpStart, `
    Show-GenieHelpStop, `
    Show-GenieHelpPause, `
    Show-GenieHelpResume, `
    Show-GenieHelpShutdown, `
    Show-GenieHelpStateCheck, `
    Show-GenieHelpNetAddress, `
    Show-GenieHelpDelete, `
    Show-GenieHelpExport, `
    Show-GenieHelpImport, `
    Show-GenieHelpSwapIso
