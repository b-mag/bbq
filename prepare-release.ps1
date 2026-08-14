#!/usr/bin/env pwsh
<#
.SYNOPSIS
Package the Carcosa.Server native EXE with configuration for remote testing.

.DESCRIPTION
Copies the release build to a clean distribution folder with configuration
template and instructions.

.PARAMETER TrackerUrl
The tracker/matchmaking service URL (default: http://70.127.46.77:5100)

.PARAMETER OutputPath
Where to place the distribution package (default: ./carcosa-release)

.EXAMPLE
.\prepare-release.ps1 -TrackerUrl "http://70.127.46.77:5100" -OutputPath "D:\carcosa-release"
#>

param(
    [string]$TrackerUrl = "http://70.127.46.77:5100",
    [string]$OutputPath = ".\carcosa-release"
)

$backendDir = "$PSScriptRoot\src\backend\publish-release"
$releaseExe = "$backendDir\Carcosa.Server.exe"

if (-not (Test-Path $releaseExe)) {
    Write-Error "Release EXE not found at $releaseExe. Run: dotnet publish -c Release -r win-x64"
    exit 1
}

# Create output directory
if (Test-Path $OutputPath) {
    Remove-Item -Path $OutputPath -Recurse -Force
}
New-Item -Path $OutputPath -ItemType Directory -Force | Out-Null

# Copy EXE and runtime
Write-Host "Copying EXE and runtime files..." -ForegroundColor Green
Copy-Item -Path "$backendDir\*" -Destination $OutputPath -Recurse -Force

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

$configPath = "$OutputPath\appsettings.json"
$config | ConvertTo-Json | Set-Content $configPath

Write-Host "Created config at $configPath" -ForegroundColor Green
Write-Host "  Tracker: $TrackerUrl" -ForegroundColor Cyan

# Create README
$readme = @"
# Carcosa - Peer Game Client

## Quick Start

1. Double-click `Carcosa.Server.exe` to launch
2. Your browser will open automatically
3. If using a tracker, the game will auto-discover other players
4. Share your peer address for manual connections (glyph codes)

## Configuration

Edit `appsettings.json` to customize:
- **Port** - Default: 5000
- **Matchmaking.Url** - Tracker service address
- **ServerName** - Your display name
- **MaxPlayers** - Player limit per shard

## Troubleshooting

### "Port already in use"
Change the port in `appsettings.json`:
\`\`\`json
"Port": 5001
\`\`\`

### "Cannot connect to tracker"
1. Verify tracker is running at the configured URL
2. Check your network connectivity
3. Try offline mode by setting "Enabled": false

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

Set-Content -Path "$OutputPath\README.txt" -Value $readme

Write-Host "Created README at $OutputPath\README.txt" -ForegroundColor Green

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

$offlineConfigPath = "$OutputPath\appsettings.offline.json"
$offlineConfig | ConvertTo-Json | Set-Content $offlineConfigPath

Write-Host "Created offline config at $offlineConfigPath" -ForegroundColor Green
Write-Host ""
Write-Host "Package ready at: $OutputPath" -ForegroundColor Yellow
Write-Host "To use offline mode: copy appsettings.offline.json to appsettings.json" -ForegroundColor Cyan
