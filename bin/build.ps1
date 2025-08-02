[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Write-Host "📦 Restoring NuGet packages..." -ForegroundColor Cyan

$repoRoot = Resolve-Path "$PSScriptRoot\.."
$slnPath = Join-Path $repoRoot "vmgenie.sln"

if (-not (Test-Path $slnPath)) {
    Write-Warning "🚫 Could not find solution file: $slnPath"
    Write-Host "✅ Run 'dotnet new sln -n vmgenie' and add your project to it before building."
    exit 1
}

Push-Location $repoRoot

Write-Host "🔗 Restoring solution: $slnPath"
dotnet restore $slnPath
if ($LASTEXITCODE -ne 0) {
    Write-Warning "🚫 Failed to restore NuGet packages."
    Pop-Location
    exit 1
}

Pop-Location
Write-Host "✅ NuGet packages restored successfully." -ForegroundColor Green
Write-Host ""
Write-Host "🎉 Build complete." -ForegroundColor Cyan
exit 0
