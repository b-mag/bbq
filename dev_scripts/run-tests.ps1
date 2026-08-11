# =============================================================================
# Run Unit Tests
# =============================================================================

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$testsDir = Join-Path $scriptDir "..\src\tests"
$backendDir = Join-Path $scriptDir "..\src\backend"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $env:PATH = "C:\Program Files\dotnet;$env:PATH"
}

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  Running Unit Tests" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

# Clean stale web assets to prevent build failures
Set-Location $backendDir
if (Test-Path "obj") { Remove-Item -Recurse -Force "obj" -ErrorAction SilentlyContinue }
if (Test-Path "wwwroot") { Remove-Item -Recurse -Force "wwwroot" -ErrorAction SilentlyContinue }

Set-Location $testsDir
dotnet test -p:SkipFrontendBuild=true

Write-Host ""
Write-Host "Press any key to close..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
