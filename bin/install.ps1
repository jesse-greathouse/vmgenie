[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
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

# Step 1: Copy dist GMI repository file if user copy does not exist
$distRepoFile = Join-Path $PSScriptRoot "..\gmi-repository.dist.yml"
$userRepoFile = Join-Path $PSScriptRoot "..\gmi-repository.yml"

if (-Not (Test-Path $userRepoFile)) {
    try {
        Copy-Item $distRepoFile $userRepoFile -Force
        Write-Host "✅ Copied default gmi-repository.dist.yml to gmi-repository.yml" -ForegroundColor Green
    }
    catch {
        Write-Warning "⚠️ Failed to copy gmi-repository.dist.yml to gmi-repository.yml: $_"
        exit 1
    }
}
else {
    Write-Host "ℹ️ gmi-repository.yml already exists; skipping copy." -ForegroundColor Yellow
}

# Step 2: Install 'genie.ps1' script to per-user WindowsApps for CLI access (non-elevated)
$windowsAppsDir = Join-Path $env:USERPROFILE "AppData\Local\Microsoft\WindowsApps"
$targetScript = Join-Path $windowsAppsDir "genie.ps1"
$sourceScript = Join-Path $PSScriptRoot "genie.ps1"

Write-Host "🗃️ Installing genie.ps1 to: $targetScript" -ForegroundColor Cyan

if (Test-Path $targetScript) {
    try {
        Remove-Item $targetScript -Force
        Write-Host "🧹 Removed previous genie.ps1" -ForegroundColor Gray
    }
    catch {
        Write-Warning "⚠️ Failed to remove existing genie.ps1 at $targetScript : $_"
        exit 1
    }
}

try {
    Copy-Item $sourceScript $targetScript -Force
    Write-Host "✅ genie.ps1 installed to WindowsApps" -ForegroundColor Green
}
catch {
    Write-Warning "🚫 Failed to copy genie.ps1 to WindowsApps: $_"
    exit 1
}

# Step 2.5: Restore NuGet packages before configuration
$buildScript = Join-Path $PSScriptRoot "build.ps1"
Write-Host "📦 Ensuring all NuGet packages are restored..." -ForegroundColor Cyan

& "$buildScript"
if ($LASTEXITCODE -ne 0) {
    Write-Warning "🚫 Failed to restore NuGet packages. Installation aborted."
    exit 1
}

# Step 3: Run configure (normal privileges) - runs interactive for everything but VM_SWITCH
$configureScript = Join-Path $PSScriptRoot "configure.ps1"
Write-Host "⚙️ Running initial configuration (all keys except VM_SWITCH)..." -ForegroundColor Cyan

& "$configureScript"
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0) {
    Write-Warning "🚫 Configuration failed with exit code $exitCode. Installation aborted."
    exit 1
}

# Step 4: Run bootstrap (elevated, required for SCM install/registry/etc)
Write-Host "🚀 Installing and starting VmGenie service..." -ForegroundColor Cyan

$bootstrapExitCode = Start-BootstrapElevated

if ($bootstrapExitCode -ne 0) {
    Write-Warning "🚫 Bootstrap failed with exit code $bootstrapExitCode. Installation aborted."
    exit 1
}

Write-Host "✅ Bootstrap/service install succeeded." -ForegroundColor Green

# Step 5: Now that the service is up, configure VM_SWITCH key only
Write-Host "⚙️ Running post-install configuration for VM_SWITCH..." -ForegroundColor Cyan

& "$configureScript" -Key VM_SWITCH
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0) {
    Write-Warning "🚫 VM_SWITCH configuration failed with exit code $exitCode. Installation aborted."
    exit 1
}

Write-Host "🎉 Installation and configuration complete! Ready to run." -ForegroundColor Cyan
exit 0
