# bin/uninstall.ps1
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Write-Host "🧹 Starting vmgenie uninstallation..." -ForegroundColor Cyan

function Start-UninstallBootstrapElevated {
    Write-Host "🔍 Checking if current session is elevated..."
    $isAdmin = ([Security.Principal.WindowsPrincipal] `
            [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(`
            [Security.Principal.WindowsBuiltinRole]::Administrator)

    $bootstrapScript = Join-Path $PSScriptRoot "uninstall-bootstrap.ps1"

    if ($isAdmin) {
        Write-Host "✅ Already running elevated. Executing uninstall bootstrap directly..." -ForegroundColor Green
        & $bootstrapScript
        return $LASTEXITCODE
    }
    else {
        Write-Host "⚠️ Not running elevated. Launching uninstall bootstrap in a new elevated session..." -ForegroundColor Yellow

        # Compose a properly-escaped command block
        $cmd = @"
& {
    & '$bootstrapScript'
    Write-Host ''
    Read-Host '🚧 Uninstall complete. Press Enter to close this window.'
    exit `$LASTEXITCODE
}
"@.Trim()

        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = "powershell.exe"
        $psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -Command `"${cmd}`""
        $psi.Verb = "RunAs"
        $psi.UseShellExecute = $true

        $process = [System.Diagnostics.Process]::Start($psi)
        $process.WaitForExit()
        return $process.ExitCode
    }
}

# Run uninstall-bootstrap (elevated, required for SCM and registry cleanup)
$bootstrapExitCode = Start-UninstallBootstrapElevated

if ($bootstrapExitCode -ne 0) {
    Write-Warning "🚫 Uninstall bootstrap failed with exit code $bootstrapExitCode. Aborted."
    exit 1
}

Write-Host "✅ Elevated uninstall steps complete. Continuing with user cleanup..." -ForegroundColor Green

# Remove genie.ps1 from WindowsApps (non-elevated)
$windowsAppsDir = Join-Path $env:USERPROFILE "AppData\Local\Microsoft\WindowsApps"
$targetScript = Join-Path $windowsAppsDir "genie.ps1"

Write-Host "🧹 Removing genie.ps1 from: $targetScript" -ForegroundColor Cyan

if (Test-Path $targetScript) {
    try {
        Remove-Item $targetScript -Force
        Write-Host "✅ genie.ps1 removed from WindowsApps" -ForegroundColor Green
    }
    catch {
        Write-Warning "⚠️ Failed to remove genie.ps1 at $targetScript : $_"
    }
}
else {
    Write-Host "ℹ️ genie.ps1 not found in WindowsApps; nothing to remove." -ForegroundColor Gray
}

Write-Host "🎉 Uninstallation complete!" -ForegroundColor Cyan
exit 0
