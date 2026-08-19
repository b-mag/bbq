# =============================================================================
# Launch Two Local Peers Against the Optional Tracker
#
# Production Carcosa.exe is unchanged: without --public-address it still uses
# STUN / public IPs for long-distance Glyph sharing. This script is the local
# development path. Same-machine NAT hairpin cannot reach your WAN IP, so both
# peers pin loopback and register that with the tracker.
#
# Starts:
#   - Tracker / matchmaking: port 5100
#   - Player 1: port 5000, glyph/tracker address 127.0.0.1:5000
#   - Player 2: port 5001, glyph/tracker address 127.0.0.1:5001
#
# Use this for testing:
#   - automatic peer discovery via the local tracker
#   - mesh bootstrap without pasting Glyphs
#   - local SHARD / PEX with tracker online
#
# For internet Glyph tests, run Carcosa.exe with no --public-address.
# For Glyph-only local tests (tracker off), use launch-two-players.ps1.
# To skip known-peers.json, use launch-two-players-local-tracker-no-cache.ps1.
# =============================================================================

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$backendExe = Join-Path $scriptDir "..\..\src\backend\bin\Release\net10.0\win-x64\publish\Carcosa.exe"
$matchmakingExe = Join-Path $scriptDir "..\..\src\matchmaking\bin\Release\net10.0\win-x64\publish\Carcosa.Matchmaking.exe"
$trackerUrl = "http://127.0.0.1:5100"

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
Write-Host "  Local Tracker Two-Peer Test" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Tracker:  $trackerUrl" -ForegroundColor Yellow
Write-Host "  Player 1: http://localhost:5000  (pinned 127.0.0.1:5000)" -ForegroundColor Green
Write-Host "  Player 2: http://localhost:5001  (pinned 127.0.0.1:5001)" -ForegroundColor Green
Write-Host ""
Write-Host "  Production Glyph / STUN path is not used." -ForegroundColor Gray
Write-Host "  Peers auto-discover each other through the local tracker." -ForegroundColor Gray
Write-Host ""

Write-Host "[1/3] Launching Matchmaking (with dashboard)..." -ForegroundColor White
$matchmaking = Start-Process -FilePath $matchmakingExe -ArgumentList "--port=5100" -WorkingDirectory (Split-Path $matchmakingExe) -PassThru
Start-Sleep -Seconds 2

Write-Host "[2/3] Launching Player 1 (Franz on port 5000)..." -ForegroundColor White
$player1 = Start-Process -FilePath $backendExe -ArgumentList "--port=5000", "--name=Franz", "--matchmaking-url=$trackerUrl", "--public-address=127.0.0.1:5000" -WorkingDirectory (Split-Path $backendExe) -PassThru
Start-Sleep -Seconds 2

Write-Host "[3/3] Launching Player 2 (Marina on port 5001)..." -ForegroundColor White
$player2 = Start-Process -FilePath $backendExe -ArgumentList "--port=5001", "--name=Marina", "--matchmaking-url=$trackerUrl", "--public-address=127.0.0.1:5001" -WorkingDirectory (Split-Path $backendExe) -PassThru

Write-Host ""
Write-Host "  All launched!" -ForegroundColor Green
Write-Host "  Player 1 PID: $($player1.Id)" -ForegroundColor Gray
Write-Host "  Player 2 PID: $($player2.Id)" -ForegroundColor Gray
Write-Host "  Tracker PID:  $($matchmaking.Id)" -ForegroundColor Gray
Write-Host ""
Write-Host "  Open both UIs. They should find each other via tracker within a few seconds." -ForegroundColor White
Write-Host "  Confirm logs show pinned 127.0.0.1, not your WAN IP." -ForegroundColor Gray
Write-Host ""
Write-Host "  Press any key to STOP all..." -ForegroundColor Red
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

if (!$player2.HasExited) { Stop-Process -Id $player2.Id -Force -ErrorAction SilentlyContinue }
if (!$player1.HasExited) { Stop-Process -Id $player1.Id -Force -ErrorAction SilentlyContinue }
if (!$matchmaking.HasExited) { Stop-Process -Id $matchmaking.Id -Force -ErrorAction SilentlyContinue }
Write-Host "Stopped." -ForegroundColor Green
