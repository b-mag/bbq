# Remote Peer Testing Guide

**Superseded for new clones:** use [Developer_Testing_Guide.docx](Developer_Testing_Guide.docx) for build/launch scripts. This file is kept as historical notes for internet STUN / firewall testing. Hardcoded IPs below were from a specific tester machine and are not the default for this repo.

## Prerequisites

- Windows 10 or later (x64)
- Internet connection with forwarded ports (or UPnP-enabled router)
- 16 MB free disk space for the EXE

---

## Configuration

### 1. **Default Setup (Auto-Discovery)**
When you launch the EXE, it will:
1. Discover your public IP via STUN (Google STUN servers)
2. Connect to the matchmaking tracker at: `http://70.127.46.77:5100`
3. Register and discover other peers
4. Fall back to localhost if behind double NAT

**Just run the EXE:**
```
Carcosa.Server.exe
```

The game will open in your browser at `http://localhost:5000`

---

### 2. **Custom Matchmaking Service**
If you want to point to a different tracker/matchmaking service:

**Edit `appsettings.json`:**
```json
{
  "Carcosa": {
    "Matchmaking": {
      "Url": "http://your-server-ip:5100",
      "Enabled": true
    }
  }
}
```

Then place this file next to the EXE and launch:
```
Carcosa.Server.exe
```

**Or use command line:**
```
Carcosa.Server.exe --matchmaking-url=http://your-tracker:5100
```

---

### 3. **Offline Mode (P2P Only)**
To test without a matchmaking service, use glyph codes for manual peer discovery:

**Edit `appsettings.json`:**
```json
{
  "Carcosa": {
    "Matchmaking": {
      "Enabled": false
    }
  }
}
```

Or use the flag:
```
Carcosa.Server.exe --matchmaking-url=""
```

Then share glyph codes manually with other players.

---

## Network Configuration

### Port Forwarding
- **Default Port:** `5000` (HTTP)
- **P2P Port:** `5001` (WebSocket)
- **Public IP:** `70.127.46.77`

**If behind a router:**
1. Enable UPnP on your router (automatic port mapping)
   - OR manually forward TCP 5000-5001 to your machine

2. Test connectivity:
   ```
   http://70.127.46.77:5000
   ```
   Should load the game UI

### Firewall
Allow Carcosa.Server.exe through your Windows Firewall:
- Inbound: TCP ports 5000, 5001
- Outbound: All (for STUN and tracker communication)

---

## Testing Scenarios

### Scenario 1: Same Machine, Two Instances
```
# Terminal 1
Carcosa.Server.exe --port=5000 --name="Player1"

# Terminal 2
Carcosa.Server.exe --port=5001 --name="Player2"
```

Both will connect via localhost and see each other immediately.

### Scenario 2: Two Players on Different Networks
1. **Your machine (host):** Run the EXE normally
   - Matchmaking enabled
   - Public IP advertised: `70.127.46.77`

2. **Remote tester:** Run the EXE on their machine
   - Configure to point to your tracker
   - Should auto-discover and connect

### Scenario 3: Glyph-Based Manual Connection
1. Player 1 launches and gets a glyph code
2. Player 1 shares the code with Player 2 (Discord, etc.)
3. Player 2 enters the code in the game
4. Connection established peer-to-peer

---

## STUN/NAT Traversal

The peer automatically discovers its public address using STUN:
- Falls back through multiple STUN servers
- Detects type of NAT (Full Cone, Port Restricted, etc.)
- Logs results on startup:
  ```
  [P2P:NAT] Public address: 70.127.46.77:5000
  [P2P:Stun] Discovery succeeded
  ```

### If STUN Fails
- You're likely behind a restrictive firewall
- TURN relay fallback will be used (in-progress feature)
- Manual port forwarding may be required

---

## Troubleshooting

### "Connection refused" from remote peers
- Check if port 5000 is reachable: `http://70.127.46.77:5000`
- Verify router port forwarding
- Check Windows Firewall exceptions
- Disable any VPN (changes public IP)

### "Cannot connect to matchmaking tracker"
- Ensure tracker is running on your machine
- Verify correct IP/port in config
- Check network connectivity

### "Peers not showing on screen"
- Confirm both peers are in same shard
- Check if peers are in same world version
- Verify they completed the initial handshake (3-way sync)
- Try moving to trigger state broadcast

### Latency/Lag
- STUN discovery adds ~1-2 seconds startup time
- P2P broadcasting at 20Hz (50ms ticks)
- Expected RTT latency: varies by ISP/routing
- Test with `ping 70.127.46.77`

---

## Command-Line Reference

```
Carcosa.Server.exe [OPTIONS]

Options:
  --port=<port>                    HTTP listening port (default: 5000)
  --name=<name>                    Display name (default: "Carcosa Server")
  --headless                       Don't open browser
  --spawn-bots=<count>             Auto-spawn bots for testing
  --matchmaking-url=<url>          Tracker service URL
  --no-cache-connect               Don't auto-reconnect to cached peers
  --clear-peer-cache               Delete known peers list
  --public-address=<ip:port>       Manual public address override
```

---

## Expected Behavior

### On Startup
1. Discover local network interface
2. Query STUN servers for public IP (~1-2s)
3. Set peer identity and listen port
4. Load static world map (bundled)
5. Open browser to UI
6. Start accepting inbound connections
7. If tracker enabled: register and discover peers

### On Peer Connection
1. Handshake: exchange identity, version, world info
2. **Immediate sync:** Each peer sends initial position
3. Validate shard assignment
4. Add to visible players
5. Begin periodic state broadcasts (20Hz)

### Expected Log Output
```
[P2P:Mesh] Listening on ws://127.0.0.1:5001
[P2P:NAT] Public address: 70.127.46.77:5000
[P2P:Tracker] Registering with tracker...
[P2P:Tracker] Discovered 2 peers in shard
[P2P:Sync] Peer joined: <peer-id>
[P2P:Sync] Broadcast started at 20Hz
```

---

## Report Results

When testing, please report:
- ✅ Connection established (yes/no)
- ✅ Peers visible on screen (yes/no)
- ✅ Movement synchronized (yes/no)
- ✅ Chat works (yes/no)
- ⚠️ Latency (ping time)
- ⚠️ Any errors in console
- ⚠️ Network type (ISP/carrier, NAT type if known)

---

## Next: TURN Relay Fallback

If direct P2P fails (UDP blocked, double NAT), a TURN relay server will be added for guaranteed fallback connectivity. This is currently in-progress.
