# P2P Loot Distribution & Distributed Task Architecture - Implementation Plan

**Status**: Planning Phase  
**Start Date**: 2026-08-14  
**Target Completion**: Phase 1 by 2026-08-16  
**Scope**: Foundation for distributed task scheduling with autonomous loot pickup

---

## 📋 Overview

This plan implements a **capability-aware, distributed task architecture** that enables:
1. ✅ **Loot Drop Synchronization** - All peers see drops they can pick up
2. ✅ **Autonomous Pickup** - Non-host peers don't need host permission to pick up
3. ✅ **Deterministic Elite Drops** - Anti-cheat via cryptographic seeding
4. ✅ **Time-Based Eligibility** - Owned (0-60s) → Fair Game (60-120s) → Despawn
5. ✅ **Metrics Foundation** - CPU, bandwidth, latency collection for future load balancing
6. ✅ **Task Assignment Ready** - Architecture extensible for future task distribution

---

## 🏗️ Architecture Decisions (FINAL)

### **Loot Visibility Rule**
```
Visibility = CanPickup
IF (drop.IsCollected OR drop.IsExpired)
  HIDE
ELSE IF (drop.EligiblePeerIds.Empty OR drop.EligiblePeerIds.Contains(peerId))
  SHOW
ELSE
  HIDE
```

### **Drop Types & Eligibility**

| Scenario | Owner | Initial Eligible | 60s Rule | 120s |
|----------|-------|------------------|----------|------|
| Solo Kill | Killer | {Killer} | Convert to {} (fair) | Despawn |
| Party Kill (Rotation) | Killer | {Party} | No change | Despawn |
| Party Kill (Any-One) | Killer | {Party} | No change | Despawn |
| Elite Kill | Each Attacker | {Attacker} | Personal roll, no change | Despawn |

### **Elite Drop Generation**
- **Method**: Deterministic seed-based (Option C)
- **Seed Formula**: `SHA256(elite_id + ":" + peer_id + ":" + server_tick + ":" + world_id)`
- **Benefit**: Fully distributed, cryptographically verifiable, anti-cheat built-in
- **Each peer**: Rolls independently, always gets same result from seed

### **Pickup Authority**
- **Model**: Autonomous (non-host doesn't ask host permission)
- **Protocol**: Peer picks up locally → broadcasts removal event → all peers remove
- **Reliability**: Eventual consistency (timestamp prevents replay attacks)

---

## 📂 Phase 1: Implementation (NOW)

### **Phase 1 Objectives**
- [ ] Fix loot visibility bug (non-host peers receive drops)
- [ ] Implement time-based eligibility (owned → fair game → despawn)
- [ ] Enable autonomous pickup (non-host can pick up without host approval)
- [ ] Add metrics collection foundation (CPU, bandwidth, latency)
- [ ] Design (but not implement) task assignment system

---

## 📁 New Files to Create

### **1. `src/backend/P2P/PeerMetrics.cs`**
**Purpose**: Define peer capability metrics  
**Contains**:
- `PeerMetrics` class (CPU, bandwidth, latency, uptime)
- `PeerFitnessScore` struct (composite scoring, ready for Phase 2)
- `MetricsCalculator` static methods (scoring algorithms)

**Key Classes**:
```csharp
public sealed class PeerMetrics
{
    public required string PeerId { get; init; }
    public long LatencyMs { get; set; }
    public float PacketLossRate { get; set; }
    public int CpuUsagePercent { get; set; }
    public int AvailableCpuPercent { get; set; }
    public long AvailableMemoryMb { get; set; }
    public float UploadBandwidthMbps { get; set; }
    public float DownloadBandwidthMbps { get; set; }
    public float CurrentUploadUtilization { get; set; }
    public float CurrentDownloadUtilization { get; set; }
    public TimeSpan Uptime { get; set; }
    public int DisconnectCount { get; set; }
    public DateTime LastUpdated { get; set; }
    public DateTime ConnectedSince { get; set; }
}

public readonly struct PeerFitnessScore
{
    public long LatencyMs { get; init; }
    public float PacketLoss { get; init; }
    public int CpuUsage { get; init; }
    public float BandwidthUtilization { get; init; }
    public TimeSpan Uptime { get; init; }
    
    // Phase 2: Implement scoring algorithm
    public float CalculateScore() => throw new NotImplementedException("Phase 2");
}
```

---

### **2. `src/backend/Gameplay/LootSystemEnhancements.cs`**
**Purpose**: Extend loot generation with deterministic seeding  
**Contains**:
- `LootDropMode` enum (Solo, PartyRotation, PartyAnyOne, ElitePersonal)
- `DeterministicLootGenerator` class
- Seed calculation helpers
- Drop verification methods (for anti-cheat)

**Key Classes**:
```csharp
public enum LootDropMode
{
    Solo,              // Single killer owns
    PartyRotation,     // Party members take turns
    PartyAnyOne,       // Party members race
    ElitePersonal,     // Each attacker gets own roll
}

public static class DeterministicLootGenerator
{
    // Generate loot with deterministic seed
    public static GroundLootDrop GenerateDropWithSeed(
        string enemySubType,
        float x, float y,
        string seed,
        HashSet<string> eligiblePeerIds
    );
    
    // Compute seed from elite death event
    public static string ComputeEliteLootSeed(
        string eliteId,
        string peerId,
        long serverTick,
        string worldId
    );
    
    // Verify drop matches expected seed (anti-cheat)
    public static bool VerifyDropFromSeed(
        GroundLootDrop drop,
        string expectedSeed
    );
}
```

---

### **3. `src/backend/Gameplay/LootDropVisibility.cs`**
**Purpose**: Centralize pickup eligibility logic  
**Contains**:
- `CanPickup()` method (single source of truth)
- Eligibility expansion logic (solo → fair game at 60s)
- Drop expiration logic
- Visibility filtering for frontend

**Key Methods**:
```csharp
public static class LootDropVisibility
{
    // Master method: can this peer pick up this drop?
    public static bool CanPickup(
        GroundLootDrop drop,
        string peerId,
        int currentServerTick
    );
    
    // Check if drop should be shown to this peer
    public static bool IsVisibleTo(
        GroundLootDrop drop,
        string peerId,
        int currentServerTick
    ) => CanPickup(drop, peerId, currentServerTick);
    
    // Expand eligibility when solo drop becomes fair game (60s)
    public static void ExpandToFairGame(GroundLootDrop drop, int currentServerTick);
    
    // Check expiration
    public static bool IsExpired(GroundLootDrop drop, int currentServerTick);
}
```

---

### **4. `src/backend/P2P/TaskAssignmentManager.cs`** (Design Phase)
**Purpose**: Foundation for distributed task scheduling  
**Status**: Design only, minimal implementation  
**Contains**:
- `TaskAssignment` record (task ID, type, assigned peer)
- `TaskTypes` static class (extensible enum)
- Simple assignment logic (deterministic, lowest peer ID wins)
- Prepared for Phase 2 fitness-weighted assignment

**Note**: Will be integrated in Phase 2, but structure defined now.

---

## 📝 Modified Files

### **1. `src/backend/P2P/PeerMessagePayloads.cs`**

**Add these new payload types**:

```csharp
// NEW: Elite defeated event (host broadcasts)
public sealed class PeerEliteDefeatedPayload
{
    public required string EliteId { get; init; }
    public required string EliteSubType { get; init; }
    public float X { get; set; }
    public float Y { get; set; }
    public long ServerTickWhenDefeated { get; init; }
    public string[] AttackerPeerIds { get; init; } = Array.Empty<string>();
}

// NEW: Loot drop sync (host broadcasts active drops)
public sealed class PeerLootDropSyncPayload
{
    public required PeerLootDropEntry[] Drops { get; init; }
}

public sealed class PeerLootDropEntry
{
    public required string DropId { get; init; }
    public required string ItemId { get; init; }
    public int Quantity { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public string[] EligiblePeerIds { get; init; } = Array.Empty<string>();
    public bool IsCollected { get; set; }
    public long CreatedAtServerTick { get; init; }
}

// NEW: Loot picked up event (any peer broadcasts)
public sealed class PeerLootPickupPayload
{
    public required string DropId { get; init; }
    public required string PickedUpByPeerId { get; init; }
    public long ServerTick { get; init; }
}

// NEW: Loot became fair game (peer broadcasts)
public sealed class PeerLootFairGamePayload
{
    public required string DropId { get; init; }
    public long ServerTick { get; init; }
}

// NEW: Metrics update (all peers broadcast)
public sealed class PeerMetricsUpdatePayload
{
    public required string PeerId { get; init; }
    public int CurrentCpuUsagePercent { get; set; }
    public float CurrentUploadUtilization { get; set; }
    public float CurrentDownloadUtilization { get; set; }
    public long Timestamp { get; set; }
}
```

---

### **2. `src/backend/P2P/PeerMessages.cs`**

**Add to PeerMessage class**:
```csharp
public PeerEliteDefeatedPayload? EliteDefeated { get; set; }
public PeerLootDropSyncPayload? LootDropSync { get; set; }
public PeerLootPickupPayload? LootPickup { get; set; }
public PeerLootFairGamePayload? LootFairGame { get; set; }
public PeerMetricsUpdatePayload? MetricsUpdate { get; set; }
```

**Add to PeerMessageTypes class**:
```csharp
public const string EliteDefeated = "elite_defeated";
public const string LootDropSync = "loot_drop_sync";
public const string LootPickup = "loot_pickup";
public const string LootFairGame = "loot_fair_game";
public const string MetricsUpdate = "metrics_update";
```

---

### **3. `src/backend/P2P/PeerJsonContext.cs`**

**Add serialization for new types**:
```csharp
[JsonSerializable(typeof(PeerEliteDefeatedPayload))]
[JsonSerializable(typeof(PeerLootDropSyncPayload))]
[JsonSerializable(typeof(PeerLootDropEntry))]
[JsonSerializable(typeof(PeerLootDropEntry[]))]
[JsonSerializable(typeof(PeerLootPickupPayload))]
[JsonSerializable(typeof(PeerLootFairGamePayload))]
[JsonSerializable(typeof(PeerMetricsUpdatePayload))]
```

---

### **4. `src/backend/P2P/PeerHandshakePayload.cs` (Extend)**

**Add capabilities advertising to handshake**:
```csharp
public sealed class PeerHandshakePayload
{
    // ... existing fields ...
    
    // NEW: Advertise capabilities at connect time
    public int AvailableCpuPercent { get; set; }      // e.g., 50
    public long AvailableMemoryMb { get; set; }       // e.g., 2048
    public float UploadBandwidthMbps { get; set; }    // e.g., 50
    public float DownloadBandwidthMbps { get; set; }  // e.g., 100
}
```

---

### **5. `src/backend/Gameplay/LootSystem.cs` (Extend)**

**Extend existing LootSystem class**:
```csharp
public static class LootSystem
{
    // NEW: Generate drop with deterministic seed
    public static GroundLootDrop GenerateDropWithSeed(
        string enemySubType,
        float x, float y,
        string seed,
        HashSet<string> eligiblePeerIds
    );
    
    // Existing: GenerateDrops() - keep for backward compatibility
}
```

---

### **6. `src/backend/Gameplay/GroundLootDrop.cs` (Extend)**

**Add new fields to GroundLootDrop**:
```csharp
public sealed class GroundLootDrop
{
    // ... existing fields ...
    
    // NEW: Phase tracking for eligibility expansion
    public long CreatedAtServerTick { get; init; }
    public int DespawnAfterTicks { get; set; } = 2400; // 120s at 20Hz
    
    // NEW: Pickup state
    public bool IsCollected { get; set; }
    public string? CollectedByPeerId { get; set; }
    public long CollectedAtTick { get; set; }
    
    // NEW: Computed properties
    public bool IsExpired(int currentTick) 
        => !IsCollected && (currentTick - CreatedAtServerTick) > DespawnAfterTicks;
    
    public bool CanPickup(string peerId, int currentTick)
        => !IsCollected && !IsExpired(currentTick) 
           && (EligiblePeerIds.Count == 0 || EligiblePeerIds.Contains(peerId));
}
```

---

### **7. `src/backend/Gameplay/LootDropManager.cs` (Refactor)**

**Update existing class to use new visibility logic**:
```csharp
public sealed class LootDropManager
{
    // ... existing fields ...
    
    // MODIFIED: Use new visibility rules
    public List<GroundLootDrop> GetDropsForPeer(string peerId, int currentServerTick)
    {
        return _drops.Values
            .Where(d => LootDropVisibility.IsVisibleTo(d, peerId, currentServerTick))
            .ToList();
    }
    
    // MODIFIED: Better naming
    public GroundLootDrop? TryPickUp(string dropId, string peerId, int currentServerTick)
    {
        if (!_drops.TryGetValue(dropId, out var drop)) return null;
        if (!LootDropVisibility.CanPickup(drop, peerId, currentServerTick)) return null;
        
        drop.IsCollected = true;
        drop.CollectedByPeerId = peerId;
        drop.CollectedAtTick = currentServerTick;
        
        return drop;
    }
    
    // MODIFIED: Tick processing now includes eligibility expansion
    public void ProcessTick(int currentServerTick)
    {
        var toRemove = new List<string>();
        
        foreach (var (id, drop) in _drops)
        {
            // Expand solo drops to fair game at 60s
            LootDropVisibility.ExpandToFairGame(drop, currentServerTick);
            
            // Remove expired drops
            if (LootDropVisibility.IsExpired(drop, currentServerTick))
            {
                toRemove.Add(id);
            }
        }
        
        foreach (var id in toRemove)
        {
            _drops.TryRemove(id, out _);
        }
    }
}
```

---

### **8. `src/backend/Gameplay/OverworldCombatSync.cs` (Major Refactor)**

**Key changes**:

1. **Replace ShardHostManager with TaskAssignmentManager** (Phase 1: still uses host)
2. **Add loot drop broadcasting**:
   - Method: `BroadcastLootDropsAsync()` - send active drops to all peers
   - Trigger: On drop creation and despawn only (not every tick)
   
3. **Add elite defeat event**:
   - Method: `BroadcastEliteDefeatedAsync()` - when elite dies
   - Includes: elite_id, attacker list, server_tick (for deterministic seeding)
   
4. **Add message handlers**:
   - `HandleLootDropSync()` - receive drops from host
   - `HandleLootPickup()` - receive pickup events, remove locally
   - `HandleEliteDefeated()` - generate own drop from deterministic seed
   - `HandleLootFairGame()` - expand eligibility
   
5. **Modify pickup logic**:
   - When non-host picks up: broadcast removal event (not ask host)
   - All peers remove autonomously from own manager
   
6. **Add metrics reporting**:
   - Track CPU usage in tick loop
   - Track bandwidth in `PeerConnection`
   - Broadcast metrics every 10s

---

### **9. `src/backend/P2P/PeerConnection.cs` (Extend)**

**Add bandwidth tracking**:
```csharp
public sealed class PeerConnection
{
    // ... existing fields ...
    
    // NEW: Bandwidth metrics
    private long _bytesReceived;
    private long _bytesSent;
    private float _uploadBandwidthMbps;
    private float _downloadBandwidthMbps;
    private float _currentUploadUtilization;
    private float _currentDownloadUtilization;
    
    // Track in Send/Receive methods
    public async Task<bool> SendAsync(PeerMessage message)
    {
        // ... existing code ...
        _bytesSent += serialized.Length;
    }
    
    public PeerMessage? ReceiveMessage(ReadOnlySpan<byte> buffer)
    {
        // ... existing code ...
        _bytesReceived += buffer.Length;
    }
}
```

---

### **10. `src/backend/Program.cs` (Register Services)**

**Add DI registrations**:
```csharp
// Metrics collection
builder.Services.AddSingleton<PeerMetrics>();
builder.Services.AddSingleton<MetricsCollector>();

// Loot system enhancements
builder.Services.AddSingleton<LootSystemEnhancements>();
builder.Services.AddSingleton<LootDropVisibility>();

// Future task assignment (Phase 2)
builder.Services.AddSingleton<TaskAssignmentManager>();
```

---

## 🔄 Integration Points

### **1. OverworldCombatSync Constructor**
- Remove dependency on `ShardHostManager`
- Add dependency on `TaskAssignmentManager`
- Subscribe to new message types (elite, loot, metrics)

### **2. Tick Loop Changes**
- Add `ProcessLootEligibilityExpansion()` call
- Add `ReportMetricsIfDue()` call
- Keep existing combat processing

### **3. Kill Event Handler**
- When enemy dies:
  - If elite: broadcast elite-defeated event
  - If normal: generate loot, broadcast drop-sync
  - All: add to local manager

### **4. Pickup Flow**
- Frontend calls: `POST /api/gameplay/pickup-loot`
- Backend:
  1. Check if can pick up (visibility + eligibility)
  2. Mark as collected
  3. Add to inventory
  4. **Broadcast removal event** (not ask host)
  5. Return success

### **5. Non-Host Peer Reception**
- On `LootDropSync`: Add drops to local manager
- On `LootPickup`: Remove from manager
- On `EliteDefeated`: Generate own drops (deterministic seed)
- On `MetricsUpdate`: Store peer's metrics

---

## 📊 Testing Strategy

### **Unit Tests** (Phase 1)
- `LootDropVisibilityTests.cs`:
  - CanPickup() logic for all scenarios
  - Time-based expansion (60s transition)
  - Expiration logic
  
- `DeterministicLootGeneratorTests.cs`:
  - Seed calculation determinism
  - Drop generation with seed
  - Elite drop verification

- `PeerMetricsTests.cs`:
  - Metrics collection
  - Fitness score calculation (mock for Phase 2)

### **Integration Tests** (Phase 1)
- `TwoPlayerLootSyncTests.cs`:
  - Player A attacks enemy
  - Verify Player B doesn't see drop (0-60s)
  - Verify Player B sees drop (60-120s)
  - Player B picks up, verify removal on both peers
  
- `EliteLootTests.cs`:
  - Multiple players attack elite
  - Each peer independently rolls same drop (deterministic seed)
  - Each player picks up only their roll

### **Manual Testing Checklist** (Phase 1)
- [ ] Two players: Solo kill → owned only → fair game → despawn
- [ ] Two players: Party kill (rotation) → both see → one picks up
- [ ] Two players: Elite kill → each sees different drop → each picks up independently
- [ ] Three players: Mix of scenarios
- [ ] Non-host picks up while host is unaware → removal broadcasts correctly

---

## 📈 Phase 2 Preview (Not Implementation)

**Won't implement yet, but design for**:

- [ ] Quality-weighted host election (uses `PeerFitnessScore`)
- [ ] Host rotation based on CPU/bandwidth metrics
- [ ] Automated host migration on metric degradation
- [ ] Dashboard integration (matchmaking service)
- [ ] Visualization of peer fitness scores

---

## 📈 Phase 3 Preview (Future Scale)

**For 10+ player shards**:

- [ ] Distribute elite drop generation (any attacker can be drop owner)
- [ ] Zone-based task assignment (player A owns enemies in zone A)
- [ ] Dynamic task reassignment on peer degradation
- [ ] Load-aware pickup event handling

---

## 📈 Phase 4 Preview (Distant Future)

**Full distributed authority** (20+ player shards):

- [ ] Anti-cheat audit layer (seed-based drop verification)
- [ ] Peer-based reputation system
- [ ] Cross-shard coordination
- [ ] Byzantine fault tolerance

---

## 🎯 Success Criteria (Phase 1)

✅ **Loot Visibility Fixed**
- Non-host peers receive all drops they're eligible for
- Hidden drops not shown on frontend

✅ **Time-Based Eligibility Works**
- 0-60s: Owned only
- 60-120s: Fair game
- 120s+: Despawned

✅ **Autonomous Pickup Works**
- Non-host picks up without host approval
- Removal broadcasts correctly
- No race conditions (timestamps prevent replay)

✅ **Metrics Collection Foundation**
- CPU usage tracked
- Bandwidth tracked
- Latency tracked (already done via keepalive)
- All broadcast every 10s

✅ **Architecture Ready for Distribution**
- TaskAssignmentManager in place (unused, but ready)
- Deterministic seeding for elite drops
- No host dependencies in loot pickup flow

✅ **No Breaking Changes**
- Existing solo gameplay still works
- Party system ready (placeholder)
- Backward compatible with current client

---

## 🔗 Dependencies

### **Internal**
- `PeerMesh` (existing) - for broadcasting
- `PeerIdentity` (existing) - for local peer info
- `ShardHostManager` (existing, will replace with `TaskAssignmentManager`)
- `LootSystem` (existing, extend only)
- `LootDropManager` (existing, refactor to use new logic)

### **External**
- System.Security.Cryptography (for SHA256)
- System.Diagnostics (for CPU/memory monitoring)

### **No New External Dependencies** ✅

---

## 📅 Estimated Timeline

| Phase | Task | Effort | Duration |
|-------|------|--------|----------|
| 1.1 | Create new files (PeerMetrics, LootSystemEnhancements) | 4h | 2h |
| 1.2 | Extend PeerMessage types | 2h | 1h |
| 1.3 | Refactor LootDropManager + LootDropVisibility | 3h | 2h |
| 1.4 | Update OverworldCombatSync (broadcast + handlers) | 6h | 3h |
| 1.5 | Add metrics collection | 3h | 2h |
| 1.6 | Testing + debugging | 4h | 2-3h |
| **Total** | | 22h | **12-13h** |

---

## 📝 Notes

- **No frontend changes needed** in Phase 1 (visibility is backend filter)
- **No database changes** (all in-memory for overworld)
- **Backward compatible** (existing code paths work unchanged)
- **Ready for distribution** (architecture doesn't depend on host decisions)

---

## ✅ Sign-Off

This plan is ready for implementation. Each step is self-contained and can be built incrementally with testing after each phase.

**Next Step**: Begin Phase 1.1 (new files) when ready.
