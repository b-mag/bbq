# =============================================================================
# Build Bot Client (Release - Native AOT)
# Publishes the bot client as a single native AOT executable.
# Output: bbq/src/botclient/bin/Release/net10.0/win-x64/publish/Carcosa.BotClient.exe
# =============================================================================

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = Join-Path $scriptDir "..\src\botclient"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $env:PATH = "C:\Program Files\dotnet;$env:PATH"
}

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  Building Bot Client (Release AOT)" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

Set-Location $projectDir
dotnet publish -c Release -r win-x64

if ($LASTEXITCODE -eq 0) {
    $publishDir = Join-Path $projectDir "bin\Release\net10.0\win-x64\publish"
    Write-Host ""
    Write-Host "BUILD SUCCEEDED" -ForegroundColor Green
    Write-Host "Output: $publishDir\Carcosa.BotClient.exe" -ForegroundColor Gray
    
    if (Test-Path "$publishDir\Carcosa.BotClient.exe") {
        $size = (Get-Item "$publishDir\Carcosa.BotClient.exe").Length / 1MB
        Write-Host "Size: $([math]::Round($size, 1)) MB" -ForegroundColor Gray
    }
} else {
    Write-Host ""
    Write-Host "BUILD FAILED" -ForegroundColor Red
}

Write-Host ""
Write-Host "Press any key to close..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
