# =============================================================================
# Launch Game Server + Matchmaking Service
# Starts both services for local development/testing.
# Game Server: http://localhost:5000 (opens in native window)
# Matchmaking:  http://localhost:5100 (opens in native window)
#
# Press Ctrl+C in either window to stop that service.
# =============================================================================

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$backendDir = Join-Path $scriptDir "..\src\backend"
$matchmakingDir = Join-Path $scriptDir "..\src\matchmaking"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $env:PATH = "C:\Program Files\dotnet;$env:PATH"
}

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  Launching CARCOSA (Game + Matchmaking)" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Starting Matchmaking Service (port 5100)..." -ForegroundColor Gray
Write-Host "Starting Game Server (port 5000)..." -ForegroundColor Gray
Write-Host ""
Write-Host "Both will open in their own native windows." -ForegroundColor Yellow
Write-Host "Close this script window to stop both services." -ForegroundColor Yellow
Write-Host ""

# Start matchmaking in a new process
$matchmakingProc = Start-Process -FilePath "dotnet" `
    -ArgumentList "run", "--project", $matchmakingDir, "--", "--port=5100" `
    -PassThru -WindowStyle Normal

# Brief delay so matchmaking is up before game server tries to connect
Start-Sleep -Seconds 2

# Start game server in a new process with 2 bots for easy testing
$gameProc = Start-Process -FilePath "dotnet" `
    -ArgumentList "run", "--project", $backendDir, "-p:SkipFrontendBuild=true", "--", "--port=5000", "--spawn-bots=2" `
    -PassThru -WindowStyle Normal

Write-Host "Both services launched." -ForegroundColor Green
Write-Host ""
Write-Host "  Game Server PID:    $($gameProc.Id)" -ForegroundColor Gray
Write-Host "  Matchmaking PID:    $($matchmakingProc.Id)" -ForegroundColor Gray
Write-Host ""
Write-Host "Press any key to STOP both services..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

# Kill both processes on exit
Write-Host ""
Write-Host "Stopping services..." -ForegroundColor Yellow

if (!$gameProc.HasExited) { Stop-Process -Id $gameProc.Id -Force -ErrorAction SilentlyContinue }
if (!$matchmakingProc.HasExited) { Stop-Process -Id $matchmakingProc.Id -Force -ErrorAction SilentlyContinue }

Write-Host "Done." -ForegroundColor Green
