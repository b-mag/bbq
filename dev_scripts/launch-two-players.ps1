# =============================================================================
# Launch Two Local Peers for Glyph / Manual Discovery Testing
#
# This script intentionally disables the tracker path by pointing both peers at a
# dead matchmaking URL. That keeps them from auto-discovering each other through
# the tracker so we can validate the manual glyph fallback flow.
#
# Starts:
#   - Player 1: port 5000, glyph address 127.0.0.1:5000
#   - Player 2: port 5001, glyph address 127.0.0.1:5001
#
# Glyphs are forced to loopback. Same-machine joins cannot use the STUN WAN IP
# (NAT hairpin returns 404 / wrong host). For an internet test, run one exe
# without --public-address so the glyph encodes the real public IP:port.
#
# Use this for testing:
#   - glyph generation
#   - direct manual connect
#   - mesh bootstrap without tracker assistance
# =============================================================================

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$backendExe = Join-Path $scriptDir "..\src\backend\bin\Release\net10.0\win-x64\publish\Carcosa.exe"
$deadTrackerUrl = "http://127.0.0.1:1"

if (-not (Test-Path $backendExe)) {
    Write-Host "ERROR: Game server exe not found. Run build_all_release.bat or publish the backend first." -ForegroundColor Red
    Write-Host "  Expected: $backendExe" -ForegroundColor Gray
    pause
    exit 1
}

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  Launching Two Local Peer Test" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Tracker override: $deadTrackerUrl" -ForegroundColor Yellow
Write-Host "  Player 1:        http://localhost:5000  (glyph = 127.0.0.1:5000)" -ForegroundColor Green
Write-Host "  Player 2:        http://localhost:5001  (glyph = 127.0.0.1:5001)" -ForegroundColor Green
Write-Host ""
Write-Host "  Purpose:" -ForegroundColor Gray
Write-Host "    - manual glyph connection test" -ForegroundColor Gray
Write-Host "    - peer mesh bootstrap without tracker" -ForegroundColor Gray
Write-Host "    - local SHARD / PEX validation" -ForegroundColor Gray
Write-Host ""

# Start Player 1
$p1 = Start-Process -FilePath $backendExe `
    -ArgumentList "--port=5000", "--name=Franz", "--matchmaking-url=$deadTrackerUrl", "--public-address=127.0.0.1:5000" `
    -WorkingDirectory (Split-Path $backendExe) `
    -PassThru `
    -WindowStyle Normal

Start-Sleep -Seconds 3

# Start Player 2
$p2 = Start-Process -FilePath $backendExe `
    -ArgumentList "--port=5001", "--name=Marina", "--matchmaking-url=$deadTrackerUrl", "--public-address=127.0.0.1:5001" `
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
