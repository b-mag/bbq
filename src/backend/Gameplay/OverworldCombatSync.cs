// =============================================================================
// OverworldCombatSync.cs — Overworld Combat Orchestration & P2P Sync
// =============================================================================
//
// OVERVIEW:
// This is the central orchestrator for all overworld combat. It ties together:
//   - ShardHostManager (who processes combat)
//   - EnemySpawner (enemy population)
//   - EnemyAI (enemy behavior per tick)
//   - CombatSystem (ability execution)
//   - StaminaSystem (stamina management)
//   - P2P message handling (combat actions, enemy sync, damage events)
//
// ARCHITECTURE:
//   SHARD HOST runs the combat tick loop:
//     1. Process enemy AI (movement, aggro, attacks)
//     2. Process stamina regen for local player
//     3. Tick projectile lifetimes
//     4. Broadcast enemy state to all peers at 10Hz
//     5. Handle incoming combat actions from remote peers
//     6. Broadcast damage events to all peers on hit
//
//   NON-HOST peers:
//     1. Send combat actions to mesh (host processes them)
//     2. Receive and store enemy state from host (for rendering)
//     3. Receive damage events for visual feedback
//     4. Manage their own stamina/cooldowns locally (for responsive UI)
//
// LOCAL PLAYER STATE:
// Every peer tracks their own player entity locally for stamina, HP, abilities.
// The host also tracks a "mirror" entity for each remote player (just position)
// to resolve combat targeting (e.g., "is this player in range of heal?").
//
// THREAD SAFETY:
// The combat tick loop runs on a background task. State accessed by REST endpoints
// uses ConcurrentDictionary or lock-protected reads.
// =============================================================================

using System.Collections.Concurrent;
using Carcosa.Server.Game;
using Carcosa.Server.P2P;

namespace Carcosa.Server.Gameplay;

/// <summary>
/// Orchestrates overworld combat: enemy AI, combat resolution, P2P sync.
/// Created once at server startup and runs for the lifetime of the application.
/// </summary>
public sealed class OverworldCombatSync
{
    // =========================================================================
    // FIELDS
    // =========================================================================

    private readonly PeerMesh _mesh;
    private readonly PeerIdentity _localIdentity;
    private readonly ShardHostManager _hostManager;
    private readonly EnemySpawner _spawner;
    private readonly OverworldSync _overworldSync;
    private readonly LootDropManager _lootDropManager;
    private readonly PlayerInventory _inventory;
    private readonly CancellationTokenSource _cts = new();
    private Task? _tickLoop;

    // Local player entity (this peer's player — always tracked locally)
    private readonly Entity _localPlayer;

    // Enemy state mirror (non-host: received from host. Host: from spawner)
    private readonly ConcurrentDictionary<string, Entity> _enemyMirror = new();

    // Projectile state (host: authoritative. Non-host: mirror from host broadcast)
    private readonly ConcurrentDictionary<string, Entity> _projectiles = new();
    private readonly ConcurrentDictionary<string, Entity> _projectileMirror = new();

    // GameState adapter for CombatSystem (wraps enemies + projectiles + local player)
    private readonly GameState _combatState;

    // Tick counter for sync frequency control
    private int _tickCount;

    // =========================================================================
    // CONSTRUCTOR
    // =========================================================================

    public OverworldCombatSync(
        PeerMesh mesh,
        PeerIdentity localIdentity,
        ShardHostManager hostManager,
        EnemySpawner spawner,
        OverworldSync overworldSync,
        LootDropManager lootDropManager,
        PlayerInventory inventory)
    {
        _mesh = mesh;
        _localIdentity = localIdentity;
        _hostManager = hostManager;
        _spawner = spawner;
        _overworldSync = overworldSync;
        _lootDropManager = lootDropManager;
        _inventory = inventory;

        // Initialize local player entity with defaults
        _localPlayer = new Entity
        {
            Id = $"player_{localIdentity.PeerId}",
            Type = EntityType.Player,
            SubType = "player",
            OwnerId = localIdentity.PeerId,
            Health = 100,
            MaxHealth = 100,
            Stamina = 100f,
            MaxStamina = 100f,
            StaminaRegenRate = 40f,
            Level = 1,
            PrimaryAbility = "ember_spray",    // Default loadout
            SecondaryAbility = "iron_veil",
            IsAlive = true,
        };

        // Create a GameState for combat resolution (host uses this)
        _combatState = new GameState();
        _combatState.AddEntity(_localPlayer);

        // Subscribe to host status changes
        _hostManager.OnHostStatusChanged += OnHostStatusChanged;

        // Subscribe to P2P combat messages
        _mesh.OnPeerMessage += HandlePeerMessage;
    }

    // =========================================================================
    // PROPERTIES (for REST API access)
    // =========================================================================

    /// <summary>The local player entity (for stats endpoint).</summary>
    public Entity LocalPlayer => _localPlayer;

    /// <summary>Whether this peer is the shard host.</summary>
    public bool IsHost => _hostManager.IsLocalHost;

    // =========================================================================
    // LIFECYCLE
    // =========================================================================

    /// <summary>
    /// Start the combat tick loop. Called once at server startup.
    /// </summary>
    public void Start()
    {
        _tickLoop = Task.Run(() => CombatTickLoop(_cts.Token));
        Console.WriteLine("[CombatSync] Overworld combat sync started (20Hz tick loop).");

        // If we're already host (solo player), activate spawner immediately
        if (_hostManager.IsLocalHost)
        {
            OnHostStatusChanged(true);
        }
    }

    /// <summary>
    /// Stop the combat tick loop.
    /// </summary>
    public void Stop()
    {
        _cts.Cancel();
        _tickLoop?.Wait(TimeSpan.FromSeconds(2));
        _spawner.Deactivate();
        Console.WriteLine("[CombatSync] Overworld combat sync stopped.");
    }

    // =========================================================================
    // HOST STATUS CHANGE
    // =========================================================================

    private void OnHostStatusChanged(bool isHost)
    {
        if (isHost)
        {
            _spawner.Activate();
            // Add all spawned enemies to our combat state
            foreach (var enemy in _spawner.GetAllEnemies())
            {
                _combatState.AddEntity(enemy);
                _enemyMirror[enemy.Id] = enemy;
            }
            Console.WriteLine("[CombatSync] Now shard host — enemies spawned and combat processing active.");
        }
        else
        {
            _spawner.Deactivate();
            // Clear local combat state of enemies (will be populated by sync messages)
            var enemyIds = _combatState.Entities
                .Where(kv => kv.Value.Type == EntityType.Enemy)
                .Select(kv => kv.Key).ToList();
            foreach (var id in enemyIds)
            {
                _combatState.RemoveEntity(id);
            }
            _enemyMirror.Clear();
            Console.WriteLine("[CombatSync] Lost host status — cleared enemy state, awaiting sync from new host.");
        }
    }

    // =========================================================================
    // COMBAT TICK LOOP (20Hz)
    // =========================================================================

    private async Task CombatTickLoop(CancellationToken ct)
    {
        const int tickIntervalMs = 50; // 20Hz

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(tickIntervalMs, ct);
                _tickCount++;

                // Always process local player stamina regen and i-frames
                StaminaSystem.ProcessStaminaTick(_localPlayer);
                StaminaSystem.ProcessIFrameTick(_localPlayer);

                // Decrement cooldowns
                if (_localPlayer.PrimaryFireCooldown > 0) _localPlayer.PrimaryFireCooldown--;
                if (_localPlayer.SecondaryAbilityCooldown > 0) _localPlayer.SecondaryAbilityCooldown--;

                // Auto-pickup loot when walking over it (within 1.2 tiles)
                CheckAutoPickupLoot();

                // Process loot despawn timers
                _lootDropManager.ProcessTick();

                // Host-only: process enemy AI, projectiles, and broadcast state
                if (_hostManager.IsLocalHost)
                {
                    ProcessHostTick();

                    // Broadcast enemy sync at 10Hz (every 2nd tick)
                    if (_tickCount % 2 == 0)
                    {
                        await BroadcastEnemySyncAsync();
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Console.WriteLine($"[CombatSync] Tick error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Host-only tick processing: enemy AI, projectile movement, spawner maintenance.
    /// </summary>
    private void ProcessHostTick()
    {
        // Process enemy AI
        foreach (var enemy in _spawner.GetAliveEnemies())
        {
            EnemyAI.ProcessTick(enemy, FindEntityById);
        }

        // Process spawner (respawn timers, corpse cleanup)
        _spawner.ProcessTick();

        // Process projectile lifetime and collision
        ProcessProjectiles();
    }

    /// <summary>
    /// Move projectiles, check collisions with enemies, and remove expired ones.
    /// </summary>
    private void ProcessProjectiles()
    {
        var toRemove = new List<string>();

        foreach (var (id, proj) in _projectiles)
        {
            if (!proj.IsAlive) { toRemove.Add(id); continue; }

            // Increment lifetime
            proj.LifetimeTicks++;
            if (proj.LifetimeTicks >= proj.MaxLifetimeTicks)
            {
                proj.IsAlive = false;
                toRemove.Add(id);
                continue;
            }

            // Move projectile
            proj.X += proj.VelocityX;
            proj.Y += proj.VelocityY;
            proj.DistanceTraveled += MathF.Sqrt(proj.VelocityX * proj.VelocityX + proj.VelocityY * proj.VelocityY);

            // Range check
            if (proj.DistanceTraveled >= proj.Range)
            {
                proj.IsAlive = false;
                toRemove.Add(id);
                continue;
            }

            // Collision with enemies
            foreach (var enemy in _spawner.GetAliveEnemies())
            {
                float dx = enemy.X - proj.X;
                float dy = enemy.Y - proj.Y;
                float distSq = dx * dx + dy * dy;

                if (distSq < 0.8f * 0.8f) // Hit radius ~0.8 tiles
                {
                    // Apply damage
                    bool killed = enemy.TakeDamage(proj.Damage);

                    // Set tag (RuneScape-style: first hit tags the enemy)
                    if (enemy.TaggedBy == null && proj.SourceEntityId != null)
                    {
                        // Extract peer ID from source entity ID (format: "player_{peerId}")
                        var peerId = proj.SourceEntityId.StartsWith("player_")
                            ? proj.SourceEntityId["player_".Length..]
                            : proj.SourceEntityId;
                        enemy.TaggedBy = peerId;
                    }

                    // Notify enemy AI of attack
                    if (proj.SourceEntityId != null)
                    {
                        EnemyAI.NotifyAttacked(enemy, proj.SourceEntityId);
                    }

                    // Broadcast damage event
                    _ = BroadcastDamageEventAsync(
                        proj.SourceEntityId ?? _localIdentity.PeerId,
                        enemy.Id, proj.Damage, enemy.Health, killed, enemy.X, enemy.Y);

                    // Notify spawner of death
                    if (killed)
                    {
                        _spawner.NotifyEnemyDeath(enemy.Id);
                        GenerateLootForKill(enemy, proj.SourceEntityId);
                    }

                    // Remove projectile on hit
                    proj.IsAlive = false;
                    toRemove.Add(id);
                    break;
                }
            }
        }

        foreach (var id in toRemove)
        {
            _projectiles.TryRemove(id, out _);
            _combatState.RemoveEntity(id);
        }
    }

    // =========================================================================
    // COMBAT ACTION PROCESSING
    // =========================================================================

    /// <summary>
    /// Process a combat action from the local player (called by REST endpoint).
    /// If host: process immediately. If not host: broadcast to mesh for host to process.
    /// </summary>
    public async Task<bool> ProcessLocalCombatActionAsync(string abilitySlot, float aimAngle)
    {
        // Determine which ability to use based on slot
        string abilityId = abilitySlot == "primary"
            ? _localPlayer.PrimaryAbility
            : _localPlayer.SecondaryAbility;

        if (string.IsNullOrEmpty(abilityId)) return false;

        if (_hostManager.IsLocalHost)
        {
            // Host: process locally
            return ProcessCombatActionOnHost(_localIdentity.PeerId, abilityId, aimAngle,
                _localPlayer.X, _localPlayer.Y);
        }
        else
        {
            // Non-host: broadcast to mesh (host will process)
            var msg = new PeerMessage
            {
                Type = PeerMessageTypes.CombatAction,
                CombatAction = new PeerCombatActionPayload
                {
                    PeerId = _localIdentity.PeerId,
                    AbilityId = abilityId,
                    AimAngle = aimAngle,
                    SourceX = _localPlayer.X,
                    SourceY = _localPlayer.Y,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                }
            };
            await _mesh.BroadcastAsync(msg);

            // Also drain stamina locally for responsive UI
            var ability = AbilityRegistry.GetAbility(abilityId);
            if (ability != null && StaminaSystem.CanUseAbility(_localPlayer, ability.StaminaCost))
            {
                StaminaSystem.ProcessStaminaDrain(_localPlayer, ability.StaminaCost);
                if (ability.Slot == AbilitySlot.Primary)
                    _localPlayer.PrimaryFireCooldown = ability.CooldownTicks;
                else
                    _localPlayer.SecondaryAbilityCooldown = ability.CooldownTicks;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Process a combat action on the host. Creates projectiles, applies melee hits,
    /// broadcasts damage events.
    /// </summary>
    private bool ProcessCombatActionOnHost(string peerId, string abilityId, float aimAngle, float sourceX, float sourceY)
    {
        // Find or create a player entity for this peer
        var playerEntity = GetOrCreatePlayerEntity(peerId, sourceX, sourceY);

        var ability = AbilityRegistry.GetAbility(abilityId);
        if (ability == null) return false;

        // For local player, use full stamina check. For remote players, trust them
        // (they check locally before sending — host doesn't double-check stamina for remotes)
        if (peerId == _localIdentity.PeerId)
        {
            if (!StaminaSystem.CanUseAbility(playerEntity, ability.StaminaCost)) return false;
            StaminaSystem.ProcessStaminaDrain(playerEntity, ability.StaminaCost);
        }

        // Set cooldown
        if (ability.Slot == AbilitySlot.Primary)
            playerEntity.PrimaryFireCooldown = ability.CooldownTicks;
        else
            playerEntity.SecondaryAbilityCooldown = ability.CooldownTicks;

        // Execute ability
        switch (ability.Type)
        {
            case AbilityType.RangedAoE:
            case AbilityType.RangedSingle:
                // Create projectiles in our local projectile tracker
                CreateHostProjectiles(playerEntity, ability, aimAngle);
                break;

            case AbilityType.Melee:
                // Instant melee — check enemies in range
                ProcessMeleeOnHost(playerEntity, ability, aimAngle, peerId);
                break;

            case AbilityType.HealAoE:
                // Heal self (and party members once party sync is deeper)
                playerEntity.Heal(ability.HealAmount);
                break;

            case AbilityType.Shield:
                playerEntity.ShieldHP = ability.ShieldAmount;
                break;

            case AbilityType.Mobility:
                playerEntity.HasIFrames = true;
                playerEntity.IFrameTicks = ability.DurationTicks;
                // Position update happens on the client (peer-authoritative position)
                break;
        }

        return true;
    }

    /// <summary>
    /// Create projectile entities for ranged abilities (host only).
    /// </summary>
    private void CreateHostProjectiles(Entity source, AbilityDefinition ability, float aimAngle)
    {
        var rng = Random.Shared;
        int count = ability.ProjectileCount > 0 ? ability.ProjectileCount : 1;

        for (int i = 0; i < count; i++)
        {
            float angle = aimAngle;
            if (count > 1 && ability.SpreadAngle > 0)
            {
                float baseOffset = ((float)i / (count - 1) - 0.5f) * ability.SpreadAngle;
                float jitter = (rng.NextSingle() - 0.5f) * ability.SpreadAngle * 0.15f;
                angle += baseOffset + jitter;
            }

            var id = $"proj_{Interlocked.Increment(ref _projectileCounter)}";
            int lifetimeTicks = ability.ProjectileSpeed > 0
                ? (int)(ability.Range / ability.ProjectileSpeed) + 10
                : 60;

            var proj = new Entity
            {
                Id = id,
                Type = EntityType.Projectile,
                SubType = ability.Id,
                X = source.X + MathF.Cos(angle) * 0.5f,
                Y = source.Y + MathF.Sin(angle) * 0.5f,
                VelocityX = MathF.Cos(angle) * ability.ProjectileSpeed,
                VelocityY = MathF.Sin(angle) * ability.ProjectileSpeed,
                Damage = ability.Damage,
                Range = ability.Range,
                SourceEntityId = source.Id,
                MaxLifetimeTicks = lifetimeTicks,
                LifetimeTicks = 0,
                IsAlive = true,
                Health = 1,
                MaxHealth = 1,
            };

            _projectiles[id] = proj;
            _combatState.AddEntity(proj);
        }
    }

    private static int _projectileCounter;

    /// <summary>
    /// Process melee hit on host — check enemies in range in aim direction.
    /// </summary>
    private void ProcessMeleeOnHost(Entity source, AbilityDefinition ability, float aimAngle, string peerId)
    {
        float meleeX = source.X + MathF.Cos(aimAngle) * ability.Range * 0.5f;
        float meleeY = source.Y + MathF.Sin(aimAngle) * ability.Range * 0.5f;

        foreach (var enemy in _spawner.GetAliveEnemies())
        {
            float dx = enemy.X - meleeX;
            float dy = enemy.Y - meleeY;
            float distSq = dx * dx + dy * dy;

            if (distSq <= ability.Range * ability.Range)
            {
                bool killed = enemy.TakeDamage(ability.Damage);

                // Tag enemy
                if (enemy.TaggedBy == null)
                {
                    enemy.TaggedBy = peerId;
                }

                // Aggro
                EnemyAI.NotifyAttacked(enemy, source.Id);

                // Broadcast damage
                _ = BroadcastDamageEventAsync(peerId, enemy.Id, ability.Damage, enemy.Health, killed, enemy.X, enemy.Y);

                if (killed)
                {
                    _spawner.NotifyEnemyDeath(enemy.Id);
                    GenerateLootForKill(enemy, source.Id);
                }

                break; // Single target melee
            }
        }
    }

    // =========================================================================
    // P2P MESSAGE HANDLING
    // =========================================================================

    private void HandlePeerMessage(PeerConnection connection, PeerMessage message)
    {
        switch (message.Type)
        {
            case PeerMessageTypes.CombatAction when message.CombatAction != null:
                HandleRemoteCombatAction(message.CombatAction);
                break;

            case PeerMessageTypes.EnemySync when message.EnemySync != null:
                HandleEnemySync(message.EnemySync);
                break;

            case PeerMessageTypes.DamageEvent when message.DamageEvent != null:
                HandleDamageEvent(message.DamageEvent);
                break;
        }
    }

    /// <summary>
    /// Host receives a combat action from a remote peer → process it.
    /// </summary>
    private void HandleRemoteCombatAction(PeerCombatActionPayload action)
    {
        if (!_hostManager.IsLocalHost) return; // Only host processes combat

        ProcessCombatActionOnHost(action.PeerId, action.AbilityId, action.AimAngle,
            action.SourceX, action.SourceY);
    }

    /// <summary>
    /// Non-host receives enemy state from host → update local mirror for rendering.
    /// Also updates the projectile mirror for non-host rendering of ability effects.
    /// </summary>
    private void HandleEnemySync(PeerEnemySyncPayload sync)
    {
        if (_hostManager.IsLocalHost) return; // Host doesn't need its own sync

        _enemyMirror.Clear();
        foreach (var entry in sync.Enemies)
        {
            var entity = new Entity
            {
                Id = entry.Id,
                Type = EntityType.Enemy,
                SubType = entry.SubType,
                X = entry.X,
                Y = entry.Y,
                VelocityX = entry.VelocityX,
                VelocityY = entry.VelocityY,
                Health = entry.Health,
                MaxHealth = entry.MaxHealth,
                IsAlive = entry.IsAlive,
                TaggedBy = entry.TaggedBy,
            };
            _enemyMirror[entry.Id] = entity;
        }

        // Update projectile mirror from host broadcast
        _projectileMirror.Clear();
        if (sync.Projectiles != null)
        {
            foreach (var proj in sync.Projectiles)
            {
                var entity = new Entity
                {
                    Id = proj.Id,
                    Type = EntityType.Projectile,
                    SubType = proj.SubType,
                    X = proj.X,
                    Y = proj.Y,
                    VelocityX = proj.VelocityX,
                    VelocityY = proj.VelocityY,
                    IsAlive = true,
                };
                _projectileMirror[proj.Id] = entity;
            }
        }
    }

    /// <summary>
    /// Receive a damage event from host — update mirror state for visual feedback.
    /// </summary>
    private void HandleDamageEvent(PeerDamageEventPayload dmgEvent)
    {
        // Update enemy mirror health if applicable
        if (_enemyMirror.TryGetValue(dmgEvent.TargetEntityId, out var enemy))
        {
            enemy.Health = dmgEvent.NewHealth;
            if (dmgEvent.IsKill)
            {
                enemy.IsAlive = false;
            }
        }

        // If damage was to local player (enemy peck attack)
        if (dmgEvent.TargetEntityId == _localPlayer.Id)
        {
            _localPlayer.Health = dmgEvent.NewHealth;
            if (dmgEvent.IsKill)
            {
                _localPlayer.IsAlive = false;
            }
        }
    }

    // =========================================================================
    // P2P BROADCASTING
    // =========================================================================

    /// <summary>
    /// Host broadcasts enemy state to all peers at 10Hz.
    /// Also includes active projectiles so non-host peers can render them.
    /// </summary>
    private async Task BroadcastEnemySyncAsync()
    {
        var enemies = _spawner.GetAllEnemies();

        var entries = enemies.Select(e => new PeerEnemySyncEntry
        {
            Id = e.Id,
            SubType = e.SubType,
            X = e.X,
            Y = e.Y,
            VelocityX = e.VelocityX,
            VelocityY = e.VelocityY,
            Health = e.Health,
            MaxHealth = e.MaxHealth,
            IsAlive = e.IsAlive,
            TaggedBy = e.TaggedBy,
        }).ToArray();

        // Include active projectiles so non-host peers can render ability effects
        var projEntries = _projectiles.Values
            .Where(p => p.IsAlive)
            .Select(p => new PeerProjectileSyncEntry
            {
                Id = p.Id,
                SubType = p.SubType,
                X = p.X,
                Y = p.Y,
                VelocityX = p.VelocityX,
                VelocityY = p.VelocityY,
            }).ToArray();

        var msg = new PeerMessage
        {
            Type = PeerMessageTypes.EnemySync,
            EnemySync = new PeerEnemySyncPayload
            {
                Enemies = entries,
                Projectiles = projEntries.Length > 0 ? projEntries : null,
            }
        };

        await _mesh.BroadcastAsync(msg);
    }

    /// <summary>
    /// Host broadcasts a damage event to all peers.
    /// </summary>
    private async Task BroadcastDamageEventAsync(
        string sourcePeerId, string targetId, int damage, int newHealth, bool isKill, float x, float y)
    {
        var msg = new PeerMessage
        {
            Type = PeerMessageTypes.DamageEvent,
            DamageEvent = new PeerDamageEventPayload
            {
                SourcePeerId = sourcePeerId,
                TargetEntityId = targetId,
                Damage = damage,
                NewHealth = newHealth,
                IsKill = isKill,
                X = x,
                Y = y,
            }
        };

        await _mesh.BroadcastAsync(msg);
    }

    // =========================================================================
    // QUERY METHODS (for REST API endpoints)
    // =========================================================================

    /// <summary>
    /// Get all enemies for the frontend to render.
    /// Host returns spawner enemies; non-host returns mirror from sync.
    /// </summary>
    public List<Entity> GetEnemiesForRendering()
    {
        if (_hostManager.IsLocalHost)
        {
            return _spawner.GetAllEnemies();
        }
        return _enemyMirror.Values.ToList();
    }

    /// <summary>
    /// Get active projectiles for rendering.
    /// Host returns authoritative projectiles; non-host returns mirror from sync.
    /// </summary>
    public List<Entity> GetProjectilesForRendering()
    {
        if (_hostManager.IsLocalHost)
        {
            return _projectiles.Values.Where(p => p.IsAlive).ToList();
        }
        return _projectileMirror.Values.ToList();
    }

    /// <summary>
    /// Update local player position (called by the existing position update flow).
    /// Keeps combat sync aware of where the local player is for targeting.
    /// </summary>
    public void UpdateLocalPlayerPosition(float x, float y)
    {
        _localPlayer.X = x;
        _localPlayer.Y = y;
    }

    /// <summary>
    /// Set the local player's ability loadout.
    /// </summary>
    public void SetAbilities(string primary, string secondary)
    {
        if (AbilityRegistry.GetAbility(primary) is { Slot: AbilitySlot.Primary })
            _localPlayer.PrimaryAbility = primary;
        if (AbilityRegistry.GetAbility(secondary) is { Slot: AbilitySlot.Secondary })
            _localPlayer.SecondaryAbility = secondary;
    }

    // =========================================================================
    // LOOT GENERATION ON KILL
    // =========================================================================

    /// <summary>
    /// Generate loot drops when an enemy is killed. Uses LootSystem to roll drops
    /// and adds them to LootDropManager for ground display and auto-pickup.
    /// </summary>
    private void GenerateLootForKill(Entity enemy, string? killerEntityId)
    {
        // Determine eligibility (who can loot) based on the enemy's tag
        var eligible = LootSystem.DetermineEligibility(enemy.TaggedBy, null);

        // Generate drops from loot table
        var drops = LootSystem.GenerateDrops(enemy.SubType, enemy.X, enemy.Y, eligible);

        // Add to ground loot manager
        foreach (var drop in drops)
        {
            _lootDropManager.AddDrop(drop);
            Console.WriteLine($"[Loot] Dropped: {drop.ItemId} x{drop.Quantity} at ({drop.X:F1}, {drop.Y:F1}) for {(eligible.Count > 0 ? string.Join(",", eligible) : "anyone")}");
        }
    }

    // =========================================================================
    // AUTO-PICKUP LOOT (walk-over collection)
    // =========================================================================

    /// <summary>
    /// Check if the local player is standing near any loot drops and auto-collect them.
    /// Pickup radius: 1.2 tiles (slightly larger than player circle for generous feel).
    /// Called every tick but only processes drops once per 5 ticks (4Hz) to reduce spam.
    /// </summary>
    private void CheckAutoPickupLoot()
    {
        // Only check every 5 ticks (4Hz) to avoid excessive iteration
        if (_tickCount % 5 != 0) return;

        const float pickupRadius = 1.2f;
        const float pickupRadiusSq = pickupRadius * pickupRadius;

        var nearbyDrops = _lootDropManager.GetDropsForPeer(_localIdentity.PeerId);
        foreach (var drop in nearbyDrops)
        {
            float dx = drop.X - _localPlayer.X;
            float dy = drop.Y - _localPlayer.Y;
            float distSq = dx * dx + dy * dy;

            if (distSq <= pickupRadiusSq)
            {
                // Try to pick up — adds to inventory if space available
                var picked = _lootDropManager.TryPickUp(drop.DropId, _localIdentity.PeerId);
                if (picked != null)
                {
                    _inventory.AddItem(picked.ItemId, picked.Quantity);
                    Console.WriteLine($"[Loot] Auto-picked up: {picked.ItemId} x{picked.Quantity}");
                }
            }
        }
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    /// <summary>
    /// Find entity by ID across all tracked entities (local player, enemies, projectiles).
    /// Used by EnemyAI to find aggro targets.
    /// </summary>
    private Entity? FindEntityById(string id)
    {
        if (_localPlayer.Id == id) return _localPlayer;
        if (_spawner.GetEnemy(id) is { } enemy) return enemy;
        if (_projectiles.TryGetValue(id, out var proj)) return proj;
        return null;
    }

    /// <summary>
    /// Get or create a player entity for a remote peer (used for combat targeting on host).
    /// Position is updated from combat action payload.
    /// </summary>
    private Entity GetOrCreatePlayerEntity(string peerId, float x, float y)
    {
        if (peerId == _localIdentity.PeerId) return _localPlayer;

        var entityId = $"player_{peerId}";
        if (_combatState.Entities.TryGetValue(entityId, out var existing))
        {
            existing.X = x;
            existing.Y = y;
            return existing;
        }

        var entity = new Entity
        {
            Id = entityId,
            Type = EntityType.Player,
            SubType = "player",
            OwnerId = peerId,
            X = x,
            Y = y,
            Health = 100,
            MaxHealth = 100,
            IsAlive = true,
        };
        _combatState.AddEntity(entity);
        return entity;
    }
}
