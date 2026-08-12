/**
 * =============================================================================
 * useOverworldEnemies.ts — Enemy State Polling Hook (10Hz)
 * =============================================================================
 *
 * Polls GET /api/gameplay/enemies at 10Hz for rendering overworld enemies.
 * Returns array of enemy state objects with position, health, and tag info.
 *
 * Also polls GET /api/gameplay/projectiles for active projectile rendering.
 * =============================================================================
 */
'use client';

import { useState, useEffect, useRef } from 'react';

export interface EnemyState {
  id: string;
  subType: string;
  x: number;
  y: number;
  velocityX: number;
  velocityY: number;
  health: number;
  maxHealth: number;
  isAlive: boolean;
  taggedBy: string | null;
}

export interface ProjectileState {
  id: string;
  subType: string;
  x: number;
  y: number;
  velocityX: number;
  velocityY: number;
}

export interface LootDropState {
  dropId: string;
  itemId: string;
  itemName: string;
  rarity: string;
  quantity: number;
  x: number;
  y: number;
}

/**
 * Hook that polls enemy and projectile state from the local game server at 10Hz.
 * Provides data needed for rendering enemies and projectiles on the overworld canvas.
 */
export function useOverworldEnemies() {
  const [enemies, setEnemies] = useState<EnemyState[]>([]);
  const [projectiles, setProjectiles] = useState<ProjectileState[]>([]);
  const [lootDrops, setLootDrops] = useState<LootDropState[]>([]);
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

  useEffect(() => {
    const fetchData = async () => {
      try {
        const [enemyRes, projRes, lootRes] = await Promise.all([
          fetch('/api/gameplay/enemies'),
          fetch('/api/gameplay/projectiles'),
          fetch('/api/gameplay/loot-drops'),
        ]);

        if (enemyRes.ok) {
          const data = await enemyRes.json();
          setEnemies(data.enemies || []);
        }

        if (projRes.ok) {
          const data = await projRes.json();
          setProjectiles(data.projectiles || []);
        }

        if (lootRes.ok) {
          const data = await lootRes.json();
          setLootDrops(data.drops || []);
        }
      } catch {
        // Silently fail — server might not be ready
      }
    };

    fetchData();
    intervalRef.current = setInterval(fetchData, 100);

    return () => {
      if (intervalRef.current) clearInterval(intervalRef.current);
    };
  }, []);

  return { enemies, projectiles, lootDrops };
}
