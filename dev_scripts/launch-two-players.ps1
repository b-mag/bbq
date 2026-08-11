# =============================================================================
# Launch Two Game Instances (for testing multiplayer locally)
# 
# Starts:
#   - Player 1: port 5000 (default)
#   - Player 2: port 5001
#   - Matchmaking: port 5100 (optional, start separately or via docker-compose)
#
# Each instance opens in its own Edge window. You can test:
#   - Both players joining the same lobby (Player 2 connects to Player 1's game)
#   - Session discovery via matchmaking
#   - Invader mode
# =============================================================================

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$backendDir = Join-Path $scriptDir "..\src\backend"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $env:PATH = "C:\Program Files\dotnet;$env:PATH"
}

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  Launching Two Game Instances" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Player 1: http://localhost:5000" -ForegroundColor Green
Write-Host "  Player 2: http://localhost:5001" -ForegroundColor Green
Write-Host ""
Write-Host "  To test multiplayer:" -ForegroundColor Gray
Write-Host "    1. Player 1 creates a lobby (host)" -ForegroundColor Gray
Write-Host "    2. Player 2 uses 'Join a Game' or connects" -ForegroundColor Gray
Write-Host "       to http://localhost:5000 directly" -ForegroundColor Gray
Write-Host ""

# Start Player 1 (port 5000, with 1 bot for company)
$p1 = Start-Process -FilePath "dotnet" `
    -ArgumentList "run", "--project", $backendDir, "-p:SkipFrontendBuild=true", "--", "--port=5000", "--spawn-bots=1" `
    -PassThru -WindowStyle Normal

Start-Sleep -Seconds 3

# Start Player 2 (port 5001, no bots)
$p2 = Start-Process -FilePath "dotnet" `
    -ArgumentList "run", "--project", $backendDir, "-p:SkipFrontendBuild=true", "--", "--port=5001" `
    -PassThru -WindowStyle Normal

Write-Host ""
Write-Host "Both instances launched." -ForegroundColor Green
Write-Host "  Player 1 PID: $($p1.Id)" -ForegroundColor Gray
Write-Host "  Player 2 PID: $($p2.Id)" -ForegroundColor Gray
Write-Host ""
Write-Host "Press any key to STOP both instances..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

Write-Host "Stopping..." -ForegroundColor Yellow
if (!$p1.HasExited) { Stop-Process -Id $p1.Id -Force -ErrorAction SilentlyContinue }
if (!$p2.HasExited) { Stop-Process -Id $p2.Id -Force -ErrorAction SilentlyContinue }
Write-Host "Done." -ForegroundColor Green
