# =============================================================================
# Build Game Server (Release - Native AOT)
# Publishes the game server as a single native AOT executable with the React
# frontend embedded in wwwroot. No .NET runtime needed on the target machine.
# Output: bbq/src/backend/bin/Release/net10.0/win-x64/publish/Carcosa.Server.exe
# =============================================================================

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = Join-Path $scriptDir "..\src\backend"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $env:PATH = "C:\Program Files\dotnet;$env:PATH"
}

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  Building CARCOSA Game Server (Release AOT)" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "This will:" -ForegroundColor Gray
Write-Host "  1. Build the React frontend (npm ci + npm run build)" -ForegroundColor Gray
Write-Host "  2. Compile the .NET server as Native AOT" -ForegroundColor Gray
Write-Host "  3. Produce a single distributable exe" -ForegroundColor Gray
Write-Host ""

Set-Location $projectDir
# Clean stale static web asset cache (Next.js produces new hashes each build)
if (Test-Path "obj") { Remove-Item -Recurse -Force "obj" -ErrorAction SilentlyContinue }
if (Test-Path "wwwroot") { Remove-Item -Recurse -Force "wwwroot" -ErrorAction SilentlyContinue }
dotnet publish -c Release -r win-x64

if ($LASTEXITCODE -eq 0) {
    $publishDir = Join-Path $projectDir "bin\Release\net10.0\win-x64\publish"
    Write-Host ""
    Write-Host "BUILD SUCCEEDED" -ForegroundColor Green
    Write-Host "Output: $publishDir\Carcosa.Server.exe" -ForegroundColor Gray
    
    if (Test-Path "$publishDir\Carcosa.Server.exe") {
        $size = (Get-Item "$publishDir\Carcosa.Server.exe").Length / 1MB
        Write-Host "Size: $([math]::Round($size, 1)) MB" -ForegroundColor Gray
    }
} else {
    Write-Host ""
    Write-Host "BUILD FAILED" -ForegroundColor Red
}

Write-Host ""
Write-Host "Press any key to close..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
