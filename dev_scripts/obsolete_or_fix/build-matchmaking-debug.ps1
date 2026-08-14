# =============================================================================
# Build Matchmaking Service (Debug)
# Builds the matchmaking service in Debug configuration.
# Skips the React dashboard build for faster iteration.
# Output: bbq/src/matchmaking/bin/Debug/net10.0/
# =============================================================================

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = Join-Path $scriptDir "..\src\matchmaking"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $env:PATH = "C:\Program Files\dotnet;$env:PATH"
}

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  Building Matchmaking Service (Debug)" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

Set-Location $projectDir
dotnet build -p:SkipDashboardBuild=true

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "BUILD SUCCEEDED" -ForegroundColor Green
    Write-Host "Output: $projectDir\bin\Debug\net10.0\" -ForegroundColor Gray
    Write-Host "Note: Dashboard frontend was skipped. Run build-matchmaking-release.ps1 for full build." -ForegroundColor Yellow
} else {
    Write-Host ""
    Write-Host "BUILD FAILED" -ForegroundColor Red
}

Write-Host ""
Write-Host "Press any key to close..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
