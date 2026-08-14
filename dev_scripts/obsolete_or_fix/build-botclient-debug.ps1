# =============================================================================
# Build Bot Client (Debug)
# Builds the headless bot client in Debug configuration.
# Output: bbq/src/botclient/bin/Debug/net10.0/
# =============================================================================

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = Join-Path $scriptDir "..\src\botclient"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $env:PATH = "C:\Program Files\dotnet;$env:PATH"
}

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  Building Bot Client (Debug)" -ForegroundColor Cyan
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
