#!/usr/bin/env pwsh
<#
.SYNOPSIS
Package the Carcosa.Server native EXE with configuration for remote testing.

.DESCRIPTION
Copies the Release AOT publish output to a clean distribution folder with
configuration template and instructions.

.PARAMETER TrackerUrl
The tracker/matchmaking service URL (default: http://70.127.46.77:5100)

.PARAMETER OutputPath
Where to place the distribution package (default: <repo>/carcosa-release)

.EXAMPLE
.\prepare-release.ps1 -TrackerUrl "http://70.127.46.77:5100" -OutputPath "D:\carcosa-release"
#>

param(
    [string]$TrackerUrl = "http://70.127.46.77:5100",
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..")

# Same folder build_all_release.ps1 produces
$backendDir = Join-Path $repoRoot "src\backend\bin\Release\net10.0\win-x64\publish"
$legacyDir = Join-Path $repoRoot "src\backend\publish-release"
$releaseExe = Join-Path $backendDir "Carcosa.Server.exe"

if (-not (Test-Path $releaseExe)) {
    # Fallback for older publish-release copy workflows
    $legacyExe = Join-Path $legacyDir "Carcosa.Server.exe"
    if (Test-Path $legacyExe) {
        $backendDir = $legacyDir
        $releaseExe = $legacyExe
    }
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "carcosa-release"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath = Join-Path $repoRoot $OutputPath
}

if (-not (Test-Path $releaseExe)) {
    Write-Host ""
    Write-Host "Release EXE not found." -ForegroundColor Red
    Write-Host "  Looked at: $backendDir" -ForegroundColor Gray
    Write-Host "  Looked at: $legacyDir" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Run build_all_release.bat first, then re-run this script." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Press any key to close..."
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit 1
}

# Create output directory
if (Test-Path $OutputPath) {
    Remove-Item -Path $OutputPath -Recurse -Force
}
New-Item -Path $OutputPath -ItemType Directory -Force | Out-Null

# Copy EXE and runtime
Write-Host "Copying EXE and runtime files..." -ForegroundColor Green
Write-Host "  From: $backendDir" -ForegroundColor Gray
Write-Host "  To:   $OutputPath" -ForegroundColor Gray
Copy-Item -Path (Join-Path $backendDir "*") -Destination $OutputPath -Recurse -Force

# Create appsettings.json with tracker URL
$config = @{
    Logging = @{
        LogLevel = @{
            Default = "Information"
            "Microsoft.AspNetCore" = "Warning"
        }
    }
    AllowedHosts = "*"
    Carcosa = @{
        Port = 5000
        ServerName = "Carcosa Server"
        MaxPlayers = 8
        SpawnBots = 0
        Headless = $false
        Matchmaking = @{
            Url = $TrackerUrl
            Enabled = $true
            HeartbeatIntervalSeconds = 10
        }
    }
}

$configPath = Join-Path $OutputPath "appsettings.json"
$config | ConvertTo-Json | Set-Content $configPath

Write-Host "Created config at $configPath" -ForegroundColor Green
Write-Host "  Tracker: $TrackerUrl" -ForegroundColor Cyan

# Create README
$readme = @"
# Carcosa - Peer Game Client

## Quick Start

1. Double-click `Carcosa.Server.exe` to launch
2. The game window opens automatically (Edge app mode)
3. Works fully offline / solo — the overworld map is compiled into the EXE
4. If a tracker is configured and reachable, peers auto-discover
5. Share glyph codes for manual connections when the tracker is down

## Standalone / Offline

Matchmaking is optional. With no tracker and no peers you still get the full
overworld, dungeons, inventory, and local saves (`player-save.dat` next to the EXE).
Toggle Offline Mode in Settings, or set Matchmaking.Enabled to false.

## Configuration

Edit `appsettings.json` to customize:
- **Port** - Default: 5000
- **Matchmaking.Url** - Tracker service address (optional)
- **ServerName** - Your display name
- **MaxPlayers** - Player limit per shard

## Troubleshooting

### "Port already in use"
Change the port in `appsettings.json`:
\`\`\`json
"Port": 5001
\`\`\`

### "Cannot connect to tracker"
Expected when offline — the game continues in standalone mode.
1. Verify tracker is running at the configured URL
2. Check your network connectivity
3. Set Matchmaking.Enabled to false, or copy appsettings.offline.json over appsettings.json

### "Port forwarding issues"
The game uses:
- TCP 5000 (HTTP/UI)
- TCP 5001 (P2P WebSocket)

Ensure these ports are forwarded or UPnP is enabled on your router.

## Manual Peer Connection

Without a tracker, connect via glyph codes:
1. Launch the game
2. Click "Enter Glyph Code"
3. Exchange codes with other players
4. Enter their glyph to connect

## Support

For issues, check the console output and verify network configuration.
Network type (ISP, NAT, firewall) affects peer discovery.
"@

Set-Content -Path (Join-Path $OutputPath "README.txt") -Value $readme

Write-Host "Created README at $(Join-Path $OutputPath 'README.txt')" -ForegroundColor Green

# Create optional offline config
$offlineConfig = @{
    Logging = @{
        LogLevel = @{
            Default = "Information"
            "Microsoft.AspNetCore" = "Warning"
        }
    }
    AllowedHosts = "*"
    Carcosa = @{
        Port = 5000
        ServerName = "Carcosa Server (Offline)"
        MaxPlayers = 8
        SpawnBots = 0
        Headless = $false
        Matchmaking = @{
            Url = ""
            Enabled = $false
            HeartbeatIntervalSeconds = 10
        }
    }
}

$offlineConfigPath = Join-Path $OutputPath "appsettings.offline.json"
$offlineConfig | ConvertTo-Json | Set-Content $offlineConfigPath

Write-Host "Created offline config at $offlineConfigPath" -ForegroundColor Green
Write-Host ""
Write-Host "Package ready at: $OutputPath" -ForegroundColor Yellow
Write-Host "To use offline mode: copy appsettings.offline.json to appsettings.json" -ForegroundColor Cyan
Write-Host ""
Write-Host "Press any key to close..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
