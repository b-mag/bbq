/**
 * =============================================================================
 * usePlayerStats.ts — Player Stats Polling Hook (10Hz)
 * =============================================================================
 *
 * Polls GET /api/gameplay/player-stats at 10Hz (100ms) for responsive combat UI.
 * Returns HP, stamina, abilities, cooldowns, and shard host status.
 *
 * WHY 10Hz: Combat requires responsive UI feedback. 100ms latency between
 * ability use and stamina bar update feels instantaneous to the player.
 * Lower frequencies (e.g., 2Hz like mesh status) would feel laggy during combat.
 *
 * WHY POLLING (not WebSocket): The game server architecture uses REST for all
 * frontend-to-backend communication. WebSocket is reserved for the P2P mesh.
 * Polling at 10Hz is acceptable for a local-only connection (no network latency).
 * =============================================================================
 */
'use client';

import { useState, useEffect, useRef } from 'react';

export interface PlayerStats {
  hp: number;
  maxHp: number;
  stamina: number;
  maxStamina: number;
  isStaminaDepleted: boolean;
  level: number;
  xp: number;
  xpForNextLevel: number;
  loadoutLocked: boolean;
  lastSavedAt: string;
  primaryAbility: string;
  secondaryAbility: string;
  primaryCooldown: number;
  secondaryCooldown: number;
  shieldHp: number;
  isShardHost: boolean;
}

const DEFAULT_STATS: PlayerStats = {
  hp: 100,
  maxHp: 100,
  stamina: 100,
  maxStamina: 100,
  isStaminaDepleted: false,
  level: 1,
  xp: 0,
  xpForNextLevel: 200,
  loadoutLocked: false,
  lastSavedAt: '',
  primaryAbility: 'ember_spray',
  secondaryAbility: 'iron_veil',
  primaryCooldown: 0,
  secondaryCooldown: 0,
  shieldHp: 0,
  isShardHost: false,
};

/**
 * Hook that polls player stats from the local game server at 10Hz.
 * Provides all data needed by the HUD (HP bar, stamina bar, ability bar).
 */
export function usePlayerStats(): PlayerStats {
  const [stats, setStats] = useState<PlayerStats>(DEFAULT_STATS);
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

  useEffect(() => {
    const fetchStats = async () => {
      try {
        const res = await fetch('/api/gameplay/player-stats');
        if (res.ok) {
          const data = await res.json();
          setStats({
            ...DEFAULT_STATS,
            ...data,
            xpForNextLevel: data.xpForNextLevel ?? DEFAULT_STATS.xpForNextLevel,
            loadoutLocked: data.loadoutLocked ?? false,
            lastSavedAt: data.lastSavedAt ?? '',
          });
        }
      } catch {
        // Silently fail — server might not be ready yet
      }
    };

    // Initial fetch
    fetchStats();

    // Poll at 10Hz (100ms)
    intervalRef.current = setInterval(fetchStats, 100);

    return () => {
      if (intervalRef.current) clearInterval(intervalRef.current);
    };
  }, []);

  return stats;
}
