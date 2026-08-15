/**
 * =============================================================================
 * interpolation.ts — Entity Position Interpolation Between Server Ticks
 * =============================================================================
 *
 * WHY INTERPOLATION:
 * The server sends state at 20Hz (50ms between updates), but we render at 60fps
 * (16.6ms between frames). Without interpolation, entities would "teleport" to new
 * positions every 3 frames and sit still in between — creating choppy movement.
 *
 * HOW IT WORKS:
 * When a new server state arrives, we store the entity's current position as "prev"
 * and the new position as "target". Each render frame, we linearly interpolate (lerp)
 * between prev and target based on elapsed time since the update. The result is
 * smooth continuous motion that spans the entire 50ms between server ticks.
 *
 * WHY NOT EXTRAPOLATION:
 * Extrapolation (predicting beyond the last known state using velocity) can cause
 * entities to overshoot when they stop or turn. Interpolation is always behind by
 * one tick (~50ms) but is visually smooth and never wrong. At 50ms latency, the
 * delay is imperceptible.
 *
 * LOCAL PLAYER EXCEPTION:
 * The local player doesn't use interpolation — they use client-side prediction
 * (from prediction.ts) which provides instant response. Only OTHER players and
 * enemies use interpolation.
 * =============================================================================
 */

import { EntityState } from '../messages';

export interface InterpolatedEntity extends EntityState {
  // Previous state for interpolation
  prevX: number;
  prevY: number;
  // Target state (latest from server)
  targetX: number;
  targetY: number;
  // Interpolation progress (0-1)
  interpProgress: number;
  // Timestamp of last state update
  lastUpdateTime: number;
}

/**
 * Store for managing interpolated entity states.
 */
export class EntityInterpolator {
  private entities: Map<string, InterpolatedEntity> = new Map();
  private readonly interpDuration: number; // ms between server ticks

  constructor(serverTickRate: number = 20) {
    this.interpDuration = 1000 / serverTickRate; // 50ms at 20Hz
  }

  /**
   * Update entities with new server state.
   * Shifts current position to prev, sets new target.
   */
  updateFromServer(serverEntities: EntityState[]): void {
    const now = performance.now();
    const activeIds = new Set<string>();

    for (const serverEntity of serverEntities) {
      activeIds.add(serverEntity.id);
      const existing = this.entities.get(serverEntity.id);

      if (existing) {
        // Shift current interpolated position to prev
        existing.prevX = existing.x;
        existing.prevY = existing.y;
        // Set new target
        existing.targetX = serverEntity.x;
        existing.targetY = serverEntity.y;
        // Reset interpolation
        existing.interpProgress = 0;
        existing.lastUpdateTime = now;
        // Update other fields
        existing.health = serverEntity.health;
        existing.maxHealth = serverEntity.maxHealth;
        existing.velocityX = serverEntity.velocityX;
        existing.velocityY = serverEntity.velocityY;
        existing.isAlive = serverEntity.isAlive;
        existing.subType = serverEntity.subType;
        existing.entityType = serverEntity.entityType;
        existing.attackCooldown = serverEntity.attackCooldown;
      } else {
        // New entity — no interpolation needed, snap to position
        this.entities.set(serverEntity.id, {
          ...serverEntity,
          prevX: serverEntity.x,
          prevY: serverEntity.y,
          targetX: serverEntity.x,
          targetY: serverEntity.y,
          interpProgress: 1,
          lastUpdateTime: now,
        });
      }
    }

    // Remove entities that are no longer in server state
    // (Only remove if they've been gone for a while to handle delta updates)
    // Note: with delta updates, we only get dirty entities, so don't remove on every update
  }

  /**
   * Mark entities as removed (from PlayerLeft or death events).
   */
  removeEntity(id: string): void {
    this.entities.delete(id);
  }

  /**
   * Advance interpolation for all entities based on elapsed time.
   * Call this each render frame.
   */
  interpolate(): EntityState[] {
    const now = performance.now();
    const result: EntityState[] = [];

    for (const [, entity] of this.entities) {
      // Calculate interpolation progress
      const elapsed = now - entity.lastUpdateTime;
      entity.interpProgress = Math.min(1, elapsed / this.interpDuration);

      // Lerp position
      const t = entity.interpProgress;
      entity.x = entity.prevX + (entity.targetX - entity.prevX) * t;
      entity.y = entity.prevY + (entity.targetY - entity.prevY) * t;

      result.push(entity);
    }

    return result;
  }

  /**
   * Get the current interpolated position of a specific entity.
   */
  getEntity(id: string): InterpolatedEntity | undefined {
    return this.entities.get(id);
  }

  /**
   * Override the local player's position (for client-side prediction).
   * The local player doesn't interpolate — they use predicted position.
   */
  setLocalPlayerPosition(id: string, x: number, y: number): void {
    const entity = this.entities.get(id);
    if (entity) {
      entity.x = x;
      entity.y = y;
      entity.prevX = x;
      entity.prevY = y;
      entity.targetX = x;
      entity.targetY = y;
    }
  }

  /**
   * Clear all entities.
   */
  clear(): void {
    this.entities.clear();
  }
}
