# =============================================================================
# Build Game Server (Debug)
# Builds the game server in Debug configuration with the React frontend included.
# Output: bbq/src/backend/bin/Debug/net10.0/
# =============================================================================

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = Join-Path $scriptDir "..\src\backend"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $env:PATH = "C:\Program Files\dotnet;$env:PATH"
}

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  Building CARCOSA Game Server (Debug)" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

Set-Location $projectDir
dotnet build

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "BUILD SUCCEEDED" -ForegroundColor Green
    Write-Host "Output: $projectDir\bin\Debug\net10.0\" -ForegroundColor Gray
} else {
    Write-Host ""
    Write-Host "BUILD FAILED" -ForegroundColor Red
}

Write-Host ""
Write-Host "Press any key to close..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
