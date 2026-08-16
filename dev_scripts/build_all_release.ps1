# =============================================================================
# Build All Projects (Release - Native AOT)
# Publishes the game server, matchmaking service, and bot client as native AOT
# executables. Includes React frontends for game and dashboard.
#
# Output:
#   bbq/src/backend/bin/Release/net10.0/win-x64/publish/Carcosa.exe
#   bbq/src/matchmaking/bin/Release/net10.0/win-x64/publish/Carcosa.Matchmaking.exe
#   bbq/src/botclient/bin/Release/net10.0/win-x64/publish/Carcosa.BotClient.exe
# =============================================================================

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Ensure dotnet is in PATH (standard install location)
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $env:PATH = "C:\Program Files\dotnet;$env:PATH"
}

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  Building ALL Projects (Release AOT)" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "This will compile Native AOT binaries with embedded React frontends." -ForegroundColor Gray
Write-Host "This may take several minutes..." -ForegroundColor Gray
Write-Host ""

# Kill any running instances that could lock the output files
Get-Process -Name "Carcosa", "Carcosa.Server", "Carcosa.Matchmaking", "Carcosa.BotClient" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
# Kill the dotnet build server which can hold file locks between builds
& dotnet build-server shutdown 2>$null | Out-Null
Start-Sleep -Seconds 1

$failed = @()

# --- Game Server ---
Write-Host "[1/3] Game Server (with React frontend)..." -ForegroundColor White
Set-Location (Join-Path $scriptDir "..\src\backend")
# Clean stale static web asset cache (Next.js produces new hashes each build)
if (Test-Path "obj") { Remove-Item -Recurse -Force "obj" -ErrorAction SilentlyContinue }
if (Test-Path "wwwroot") { Remove-Item -Recurse -Force "wwwroot" -ErrorAction SilentlyContinue }
if (Test-Path "bin\Release") { Remove-Item -Recurse -Force "bin\Release" -ErrorAction SilentlyContinue }
dotnet publish -c Release -r win-x64
if ($LASTEXITCODE -eq 0) {
    $exe = Join-Path (Get-Location) "bin\Release\net10.0\win-x64\publish\Carcosa.exe"
    if (Test-Path $exe) {
        $size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
        Write-Host "      PASSED ($size MB)" -ForegroundColor Green
    } else {
        Write-Host "      PASSED" -ForegroundColor Green
    }
} else {
    Write-Host "      FAILED" -ForegroundColor Red
    $failed += "Game Server"
}

# --- Matchmaking Service ---
Write-Host "[2/3] Matchmaking Service (with React dashboard)..." -ForegroundColor White
Set-Location (Join-Path $scriptDir "..\src\matchmaking")
# Clean stale static web asset cache
if (Test-Path "obj") { Remove-Item -Recurse -Force "obj" -ErrorAction SilentlyContinue }
if (Test-Path "wwwroot") { Remove-Item -Recurse -Force "wwwroot" -ErrorAction SilentlyContinue }
if (Test-Path "bin\Release") { Remove-Item -Recurse -Force "bin\Release" -ErrorAction SilentlyContinue }
dotnet publish -c Release -r win-x64
if ($LASTEXITCODE -eq 0) {
    $exe = Join-Path (Get-Location) "bin\Release\net10.0\win-x64\publish\Carcosa.Matchmaking.exe"
    if (Test-Path $exe) {
        $size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
        Write-Host "      PASSED ($size MB)" -ForegroundColor Green
    } else {
        Write-Host "      PASSED" -ForegroundColor Green
    }
} else {
    Write-Host "      FAILED" -ForegroundColor Red
    $failed += "Matchmaking Service"
}

# --- Bot Client ---
Write-Host "[3/3] Bot Client..." -ForegroundColor White
Set-Location (Join-Path $scriptDir "..\src\botclient")
dotnet publish -c Release -r win-x64
if ($LASTEXITCODE -eq 0) {
    $exe = Join-Path (Get-Location) "bin\Release\net10.0\win-x64\publish\Carcosa.BotClient.exe"
    if (Test-Path $exe) {
        $size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
        Write-Host "      PASSED ($size MB)" -ForegroundColor Green
    } else {
        Write-Host "      PASSED" -ForegroundColor Green
    }
} else {
    Write-Host "      FAILED" -ForegroundColor Red
    $failed += "Bot Client"
}

# --- Summary ---
Write-Host ""
Write-Host "=========================================" -ForegroundColor Cyan
if ($failed.Count -eq 0) {
    Write-Host "  ALL BUILDS SUCCEEDED (Release AOT)" -ForegroundColor Green
    Write-Host ""
    Write-Host "  Outputs:" -ForegroundColor Gray
    Write-Host "    src/backend/bin/Release/net10.0/win-x64/publish/Carcosa.exe" -ForegroundColor Gray
    Write-Host "    src/matchmaking/bin/Release/net10.0/win-x64/publish/Carcosa.Matchmaking.exe" -ForegroundColor Gray
    Write-Host "    src/botclient/bin/Release/net10.0/win-x64/publish/Carcosa.BotClient.exe" -ForegroundColor Gray
} else {
    Write-Host "  SOME BUILDS FAILED:" -ForegroundColor Red
    foreach ($f in $failed) { Write-Host "    - $f" -ForegroundColor Red }
}
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Press any key to close..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
