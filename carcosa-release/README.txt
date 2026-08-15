# Carcosa - Peer Game Client

## Quick Start

1. Double-click Carcosa.Server.exe to launch
2. The game window opens automatically (Edge app mode)
3. Works fully offline / solo — the overworld map is compiled into the EXE
4. If a tracker is configured and reachable, peers auto-discover
5. Share glyph codes for manual connections when the tracker is down

## Standalone / Offline

Matchmaking is optional. With no tracker and no peers you still get the full
overworld, dungeons, inventory, and local saves (player-save.dat next to the EXE).
Toggle Offline Mode in Settings, or set Matchmaking.Enabled to false.

## Configuration

Edit ppsettings.json to customize:
- **Port** - Default: 5000
- **Matchmaking.Url** - Tracker service address (optional)
- **ServerName** - Your display name
- **MaxPlayers** - Player limit per shard

## Troubleshooting

### "Port already in use"
Change the port in ppsettings.json:
\\\json
"Port": 5001
\\\

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
