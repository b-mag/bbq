# =============================================================================
# Launch Two Local Peers Without Tracker (No Peer Cache)
#
# Same as launch-two-players-local-tracker-no-cache.ps1, but does not start
# matchmaking. Tracker URL is pointed at a dead address so peers cannot
# auto-discover. Use Glyphs to join.
#
#   --no-cache-connect   do not dial known-peers.json on startup
#   --clear-peer-cache   delete known-peers.json so stale WAN IPs cannot sneak in
#
# Starts:
#   - Player 1: port 5000, glyph address 127.0.0.1:5000
#   - Player 2: port 5001, glyph address 127.0.0.1:5001
#
# For internet Glyph tests, run Carcosa.exe with no --public-address.
# For tracker auto-discovery with no cache, use launch-two-players-local-tracker-no-cache.ps1.
# =============================================================================

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$backendExe = Join-Path $scriptDir "..\..\src\backend\bin\Debug\net10.0\Carcosa.exe"
$deadTrackerUrl = "http://127.0.0.1:1"

if (-not (Test-Path $backendExe)) {
    Write-Host "ERROR: Game server exe not found. Run build_all_debug.bat first." -ForegroundColor Red
    Write-Host "  Expected: $backendExe" -ForegroundColor Gray
    pause
    exit 1
}

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  Local Two-Peer Test (no tracker, no cache)" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Tracker:    $deadTrackerUrl (disabled)" -ForegroundColor Yellow
Write-Host "  Peer cache: cleared, bootstrap disabled" -ForegroundColor Yellow
Write-Host "  Player 1:   http://localhost:5000  (pinned 127.0.0.1:5000)" -ForegroundColor Green
Write-Host "  Player 2:   http://localhost:5001  (pinned 127.0.0.1:5001)" -ForegroundColor Green
Write-Host ""
Write-Host "  Production Glyph / STUN path is not used." -ForegroundColor Gray
Write-Host "  Copy a Glyph from one UI and join from the other." -ForegroundColor Gray
Write-Host ""

Write-Host "[1/2] Launching Player 1 (Franz on port 5000)..." -ForegroundColor White
$player1 = Start-Process -FilePath $backendExe -ArgumentList "--port=5000", "--name=Franz", "--matchmaking-url=$deadTrackerUrl", "--public-address=127.0.0.1:5000", "--no-cache-connect", "--clear-peer-cache" -WorkingDirectory (Split-Path $backendExe) -PassThru
Start-Sleep -Seconds 2

Write-Host "[2/2] Launching Player 2 (Marina on port 5001)..." -ForegroundColor White
$player2 = Start-Process -FilePath $backendExe -ArgumentList "--port=5001", "--name=Marina", "--matchmaking-url=$deadTrackerUrl", "--public-address=127.0.0.1:5001", "--no-cache-connect", "--clear-peer-cache" -WorkingDirectory (Split-Path $backendExe) -PassThru

Write-Host ""
Write-Host "  Both peers launched!" -ForegroundColor Green
Write-Host "  Player 1 PID: $($player1.Id)" -ForegroundColor Gray
Write-Host "  Player 2 PID: $($player2.Id)" -ForegroundColor Gray
Write-Host ""
Write-Host "  Open both UIs, copy Player 1 Glyph, join from Player 2 (or the reverse)." -ForegroundColor White
Write-Host "  Confirm logs show pinned 127.0.0.1, not your WAN IP." -ForegroundColor Gray
Write-Host "  http://localhost:5000/api/p2p/glyph" -ForegroundColor Gray
Write-Host "  http://localhost:5001/api/p2p/glyph" -ForegroundColor Gray
Write-Host ""
Write-Host "  Press any key to STOP both peers..." -ForegroundColor Red
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

if (!$player2.HasExited) { Stop-Process -Id $player2.Id -Force -ErrorAction SilentlyContinue }
if (!$player1.HasExited) { Stop-Process -Id $player1.Id -Force -ErrorAction SilentlyContinue }
Write-Host "Stopped." -ForegroundColor Green
