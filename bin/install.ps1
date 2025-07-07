# bin/install.ps1
# Orchestrates bootstrap (elevated), build (normal), and configure (normal) steps

Write-Host "🧰 Starting vmgenie installation..." -ForegroundColor Cyan

function Start-BootstrapElevated {
    Write-Host "🔍 Checking if current session is elevated..."
    $isAdmin = ([Security.Principal.WindowsPrincipal] `
               [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(`
               [Security.Principal.WindowsBuiltinRole]::Administrator)

    $bootstrapScript = Join-Path $PSScriptRoot "bootstrap.ps1"

    if ($isAdmin) {
        Write-Host "✅ Already running elevated. Executing bootstrap directly..." -ForegroundColor Green
        & $bootstrapScript
        return $LASTEXITCODE
    }
    else {
        Write-Host "⚠️ Not running elevated. Launching bootstrap in a new elevated session..." -ForegroundColor Yellow

        # Compose a properly-escaped command block
        $cmd = @"
& {
    & '$bootstrapScript'
    Write-Host ''
    Read-Host '🚧 Bootstrap complete. Press Enter to close this window.'
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

# Step 1: Run bootstrap elevated
$bootstrapExitCode = Start-BootstrapElevated

if ($bootstrapExitCode -ne 0) {
    Write-Warning "🚫 Bootstrap failed with exit code $bootstrapExitCode. Installation aborted."
    exit 1
}

Write-Host "✅ Bootstrap succeeded. Continuing with build..." -ForegroundColor Green

# Step 2: Run build (normal privileges)
$buildScript = Join-Path $PSScriptRoot "build.ps1"
& $buildScript

if ($LASTEXITCODE -ne 0) {
    Write-Warning "🚫 Build failed with exit code $LASTEXITCODE. Installation aborted."
    exit 1
}

Write-Host "✅ Build succeeded. Continuing with configuration..." -ForegroundColor Green

# Step 3: Run configure (normal privileges)
$configureScript = Join-Path $PSScriptRoot "configure.ps1"
& $configureScript

if ($LASTEXITCODE -ne 0) {
    Write-Warning "🚫 Configuration failed with exit code $LASTEXITCODE. Installation aborted."
    exit 1
}

Write-Host "🎉 Installation and configuration complete! Ready to run." -ForegroundColor Cyan
exit 0
