# Carcosa - Peer Game Client

## Quick Start

1. Double-click Carcosa.Server.exe to launch
2. Your browser will open automatically
3. If using a tracker, the game will auto-discover other players
4. Share your peer address for manual connections (glyph codes)

## Configuration

Edit ppsettings.json to customize:
- **Port** - Default: 5000
- **Matchmaking.Url** - Tracker service address
- **ServerName** - Your display name
- **MaxPlayers** - Player limit per shard

## Troubleshooting

### "Port already in use"
Change the port in ppsettings.json:
\\\json
"Port": 5001
\\\

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
