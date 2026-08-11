# =============================================================================
# Full End-to-End Test: Matchmaking + 2 Game Instances
#
# Launches the published exes directly (no compilation).
# Run build_all_release.bat first to ensure exes are built.
#
# Starts:
#   - Matchmaking: port 5100 (headless, console only)
#   - Player 1:    port 5000 (with 1 bot)
#   - Player 2:    port 5001
# =============================================================================

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$backendExe = Join-Path $scriptDir "..\src\backend\bin\Release\net10.0\win-x64\publish\Carcosa.Server.exe"
$matchmakingExe = Join-Path $scriptDir "..\src\matchmaking\bin\Release\net10.0\win-x64\publish\Carcosa.Matchmaking.exe"

# Check exes exist
if (-not (Test-Path $backendExe)) {
    Write-Host "ERROR: Game server exe not found. Run build_all_release.bat first." -ForegroundColor Red
    Write-Host "  Expected: $backendExe" -ForegroundColor Gray
    pause
    exit 1
}
if (-not (Test-Path $matchmakingExe)) {
    Write-Host "ERROR: Matchmaking exe not found. Run build_all_release.bat first." -ForegroundColor Red
    Write-Host "  Expected: $matchmakingExe" -ForegroundColor Gray
    pause
    exit 1
}

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  CARCOSA Full End-to-End Test" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Matchmaking:  http://localhost:5100" -ForegroundColor Yellow
Write-Host "  Player 1:     http://localhost:5000" -ForegroundColor Green
Write-Host "  Player 2:     http://localhost:5001" -ForegroundColor Green
Write-Host ""

# Start Matchmaking (with dashboard window)
Write-Host "[1/3] Launching Matchmaking (with dashboard)..." -ForegroundColor White
$matchmaking = Start-Process -FilePath $matchmakingExe -ArgumentList "--port=5100" -WorkingDirectory (Split-Path $matchmakingExe) -PassThru
Start-Sleep -Seconds 2

# Start Player 1
Write-Host "[2/3] Launching Player 1 (port 5000, 1 bot)..." -ForegroundColor White
$player1 = Start-Process -FilePath $backendExe -ArgumentList "--port=5000", "--spawn-bots=1" -WorkingDirectory (Split-Path $backendExe) -PassThru
Start-Sleep -Seconds 2

# Start Player 2
Write-Host "[3/3] Launching Player 2 (port 5001)..." -ForegroundColor White
$player2 = Start-Process -FilePath $backendExe -ArgumentList "--port=5001" -WorkingDirectory (Split-Path $backendExe) -PassThru

Write-Host ""
Write-Host "  All launched!" -ForegroundColor Green
Write-Host ""
Write-Host "  Press any key to STOP all..." -ForegroundColor Red
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

# Cleanup
if (!$player2.HasExited) { Stop-Process -Id $player2.Id -Force -ErrorAction SilentlyContinue }
if (!$player1.HasExited) { Stop-Process -Id $player1.Id -Force -ErrorAction SilentlyContinue }
if (!$matchmaking.HasExited) { Stop-Process -Id $matchmaking.Id -Force -ErrorAction SilentlyContinue }
Write-Host "Stopped." -ForegroundColor Green
