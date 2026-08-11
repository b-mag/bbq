# =============================================================================
# Build All Projects (Debug)
# Builds the game server, matchmaking service, and bot client in Debug mode.
# =============================================================================

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Ensure dotnet is in PATH (standard install location)
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $env:PATH = "C:\Program Files\dotnet;$env:PATH"
}

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  Building ALL Projects (Debug)" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

$failed = @()

# --- Game Server ---
Write-Host "[1/3] Game Server..." -ForegroundColor White
Set-Location (Join-Path $scriptDir "..\src\backend")
dotnet build -p:SkipFrontendBuild=true
if ($LASTEXITCODE -eq 0) {
    Write-Host "      PASSED" -ForegroundColor Green
} else {
    Write-Host "      FAILED" -ForegroundColor Red
    $failed += "Game Server"
}

# --- Matchmaking Service ---
Write-Host "[2/3] Matchmaking Service..." -ForegroundColor White
Set-Location (Join-Path $scriptDir "..\src\matchmaking")
dotnet build -p:SkipDashboardBuild=true
if ($LASTEXITCODE -eq 0) {
    Write-Host "      PASSED" -ForegroundColor Green
} else {
    Write-Host "      FAILED" -ForegroundColor Red
    $failed += "Matchmaking Service"
}

# --- Bot Client ---
Write-Host "[3/3] Bot Client..." -ForegroundColor White
Set-Location (Join-Path $scriptDir "..\src\botclient")
dotnet build
if ($LASTEXITCODE -eq 0) {
    Write-Host "      PASSED" -ForegroundColor Green
} else {
    Write-Host "      FAILED" -ForegroundColor Red
    $failed += "Bot Client"
}

# --- Summary ---
Write-Host ""
Write-Host "=========================================" -ForegroundColor Cyan
if ($failed.Count -eq 0) {
    Write-Host "  ALL BUILDS SUCCEEDED (Debug)" -ForegroundColor Green
} else {
    Write-Host "  SOME BUILDS FAILED:" -ForegroundColor Red
    foreach ($f in $failed) { Write-Host "    - $f" -ForegroundColor Red }
}
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Press any key to close..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
