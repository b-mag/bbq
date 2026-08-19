# =============================================================================
# Launch Two Local Peers - Glyph Test Without Cached Peer IPs
#
# Same as launch-two-players.ps1, plus:
#   --no-cache-connect   do not dial known-peers.json on startup
#   --clear-peer-cache   delete known-peers.json so stale WAN IPs cannot sneak in
#
# Tracker is pointed at a dead URL. Glyphs are forced to loopback so a
# same-machine join does not hairpin through the WAN IP.
#
# Starts:
#   - Player 1: port 5000, glyph address 127.0.0.1:5000
#   - Player 2: port 5001, glyph address 127.0.0.1:5001
#
# For an internet test, run one exe without --public-address.
# =============================================================================

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$backendExe = Join-Path $scriptDir "..\..\src\backend\bin\Debug\net10.0\Carcosa.exe"
$deadTrackerUrl = "http://127.0.0.1:1"

if (-not (Test-Path $backendExe)) {
    Write-Host "ERROR: Game server exe not found. Run build_all_debug.bat or publish the backend first." -ForegroundColor Red
    Write-Host "  Expected: $backendExe" -ForegroundColor Gray
    pause
    exit 1
}

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  Local Glyph Test (no peer cache)" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Tracker override: $deadTrackerUrl" -ForegroundColor Yellow
Write-Host "  Peer cache:       cleared, bootstrap disabled" -ForegroundColor Yellow
Write-Host "  Player 1:        http://localhost:5000  (glyph = 127.0.0.1:5000)" -ForegroundColor Green
Write-Host "  Player 2:        http://localhost:5001  (glyph = 127.0.0.1:5001)" -ForegroundColor Green
Write-Host ""
Write-Host "  Purpose:" -ForegroundColor Gray
Write-Host "    - glyph join only (no tracker, no cached IPs)" -ForegroundColor Gray
Write-Host "    - loopback glyphs so NAT hairpin cannot interfere" -ForegroundColor Gray
Write-Host ""

# Start Player 1
$p1 = Start-Process -FilePath $backendExe `
    -ArgumentList "--port=5000", "--name=Franz", "--matchmaking-url=$deadTrackerUrl", "--public-address=127.0.0.1:5000", "--no-cache-connect", "--clear-peer-cache" `
    -WorkingDirectory (Split-Path $backendExe) `
    -PassThru `
    -WindowStyle Normal

Start-Sleep -Seconds 3

# Start Player 2
$p2 = Start-Process -FilePath $backendExe `
    -ArgumentList "--port=5001", "--name=Marina", "--matchmaking-url=$deadTrackerUrl", "--public-address=127.0.0.1:5001", "--no-cache-connect", "--clear-peer-cache" `
    -WorkingDirectory (Split-Path $backendExe) `
    -PassThru `
    -WindowStyle Normal

Write-Host ""
Write-Host "Both peers launched in tracker-disabled mode." -ForegroundColor Green
Write-Host "  Player 1 PID: $($p1.Id)" -ForegroundColor Gray
Write-Host "  Player 2 PID: $($p2.Id)" -ForegroundColor Gray
Write-Host ""
Write-Host "Open both UIs, copy Player 1's glyph, join from Player 2 (or the reverse)." -ForegroundColor White
Write-Host "  Confirm Public Address logs show 127.0.0.1, not your WAN IP." -ForegroundColor Gray
Write-Host "  http://localhost:5000/api/p2p/glyph" -ForegroundColor Gray
Write-Host "  http://localhost:5001/api/p2p/glyph" -ForegroundColor Gray
Write-Host ""
Write-Host "Press any key to STOP both peers..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

Write-Host "Stopping..." -ForegroundColor Yellow
if (!$p1.HasExited) { Stop-Process -Id $p1.Id -Force -ErrorAction SilentlyContinue }
if (!$p2.HasExited) { Stop-Process -Id $p2.Id -Force -ErrorAction SilentlyContinue }
Write-Host "Done." -ForegroundColor Green
