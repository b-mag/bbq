# =============================================================================
# Build All Projects (Debug / develop)
# Builds the game server, matchmaking service, and bot client in Debug mode
# WITH frontends so launch scripts can open the UI.
#
# Faster than Native AOT. Output:
#   src/backend/bin/Debug/net10.0/Carcosa.exe
#   src/matchmaking/bin/Debug/net10.0/Carcosa.Matchmaking.exe
#   src/botclient/bin/Debug/net10.0/Carcosa.BotClient.exe
# =============================================================================

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $env:PATH = "C:\Program Files\dotnet;$env:PATH"
}

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  Building ALL Projects (Debug)" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Debug builds include the React frontends but skip Native AOT." -ForegroundColor Gray
Write-Host "This is faster than build_all_release.bat." -ForegroundColor Gray
Write-Host ""

Get-Process -Name "Carcosa", "Carcosa.Server", "Carcosa.Matchmaking", "Carcosa.BotClient" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

$failed = @()

Write-Host "[1/3] Game Server (with React frontend)..." -ForegroundColor White
Set-Location (Join-Path $scriptDir "..\..\src\backend")
dotnet build -c Debug
if ($LASTEXITCODE -eq 0) {
    $exe = Join-Path (Get-Location) "bin\Debug\net10.0\Carcosa.exe"
    if (Test-Path $exe) {
        Write-Host "      PASSED" -ForegroundColor Green
    } else {
        Write-Host "      PASSED (exe path may differ)" -ForegroundColor Green
    }
} else {
    Write-Host "      FAILED" -ForegroundColor Red
    $failed += "Game Server"
}

Write-Host "[2/3] Matchmaking Service (with dashboard)..." -ForegroundColor White
Set-Location (Join-Path $scriptDir "..\..\src\matchmaking")
dotnet build -c Debug
if ($LASTEXITCODE -eq 0) {
    Write-Host "      PASSED" -ForegroundColor Green
} else {
    Write-Host "      FAILED" -ForegroundColor Red
    $failed += "Matchmaking Service"
}

Write-Host "[3/3] Bot Client..." -ForegroundColor White
Set-Location (Join-Path $scriptDir "..\..\src\botclient")
dotnet build -c Debug
if ($LASTEXITCODE -eq 0) {
    Write-Host "      PASSED" -ForegroundColor Green
} else {
    Write-Host "      FAILED" -ForegroundColor Red
    $failed += "Bot Client"
}

Write-Host ""
Write-Host "=========================================" -ForegroundColor Cyan
if ($failed.Count -eq 0) {
    Write-Host "  ALL BUILDS SUCCEEDED (Debug)" -ForegroundColor Green
    Write-Host ""
    Write-Host "  Outputs:" -ForegroundColor Gray
    Write-Host "    src/backend/bin/Debug/net10.0/Carcosa.exe" -ForegroundColor Gray
    Write-Host "    src/matchmaking/bin/Debug/net10.0/Carcosa.Matchmaking.exe" -ForegroundColor Gray
    Write-Host "    src/botclient/bin/Debug/net10.0/Carcosa.BotClient.exe" -ForegroundColor Gray
} else {
    Write-Host "  SOME BUILDS FAILED:" -ForegroundColor Red
    foreach ($f in $failed) { Write-Host "    - $f" -ForegroundColor Red }
}
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Press any key to close..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
