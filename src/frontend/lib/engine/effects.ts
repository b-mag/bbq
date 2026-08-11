/**
 * =============================================================================
 * effects.ts — Client-Side Visual Effects System
 * =============================================================================
 *
 * WHY CLIENT-SIDE EFFECTS:
 * Visual effects like muzzle flashes, slash arcs, and impact sparks are purely
 * cosmetic — they don't affect game state. Sending them from the server would
 * waste bandwidth. Instead, the client generates them locally when it detects
 * relevant actions (firing, melee attacks, damage events).
 *
 * LIFECYCLE:
 * Each effect has a duration (in milliseconds). The effects list is iterated
 * each render frame, drawing active effects and removing expired ones.
 * Effects are positioned in world coordinates and transformed to screen coords
 * using the camera system (so they stay in place as the camera moves).
 *
 * EFFECT TYPES:
 *   - MuzzleFlash: brief bright circle at player position when firing
 *   - SlashArc: arc-shaped sweep for surgeon dagger melee
 *   - BulletTrail: fading line behind fast-moving projectiles
 *   - ImpactSpark: small burst at point of projectile collision
 * =============================================================================
 */

/** Types of visual effects the system can render. */
export type EffectType = 'muzzle_flash' | 'slash_arc' | 'impact_spark';

/** A single active visual effect instance. */
export interface VisualEffect {
  type: EffectType;
  /** World X position (tile coords). */
  x: number;
  /** World Y position (tile coords). */
  y: number;
  /** Direction/angle in radians (for directional effects like slash). */
  angle: number;
  /** Time this effect was created (performance.now()). */
  startTime: number;
  /** How long the effect lasts in milliseconds. */
  duration: number;
  /** Color for the effect. */
  color: string;
  /** Size multiplier (varies by weapon type). */
  size: number;
}

/**
 * Manages a pool of short-lived visual effects rendered on the game canvas.
 * Effects are purely cosmetic and don't interact with game state.
 */
export class VisualEffectsSystem {
  private effects: VisualEffect[] = [];

  /**
   * Add a muzzle flash effect at a position.
   * Shows a brief bright circle when a ranged weapon fires.
   */
  addMuzzleFlash(x: number, y: number, angle: number, color: string = '#ffc832', size: number = 1.0): void {
    this.effects.push({
      type: 'muzzle_flash',
      x,
      y,
      angle,
      startTime: performance.now(),
      duration: 80, // Very brief flash (80ms)
      color,
      size,
    });
  }

  /**
   * Add a melee slash arc effect.
   * Shows a curved sweep in the aim direction for the surgeon's dagger.
   */
  addSlashArc(x: number, y: number, angle: number): void {
    this.effects.push({
      type: 'slash_arc',
      x,
      y,
      angle,
      startTime: performance.now(),
      duration: 200, // Visible slash sweep (200ms)
      color: '#e0e0e0', // Light steel color for blade
      size: 1.0,
    });
  }

  /**
   * Add an impact spark effect where a projectile hits.
   */
  addImpactSpark(x: number, y: number): void {
    this.effects.push({
      type: 'impact_spark',
      x,
      y,
      angle: 0,
      startTime: performance.now(),
      duration: 150,
      color: '#ff8800',
      size: 0.8,
    });
  }

  /**
   * Get all currently active effects (removes expired ones).
   * Called each render frame.
   */
  getActiveEffects(): VisualEffect[] {
    const now = performance.now();
    // Remove expired effects
    this.effects = this.effects.filter(e => now - e.startTime < e.duration);
    return this.effects;
  }

  /**
   * Clear all effects (e.g., on game reset).
   */
  clear(): void {
    this.effects = [];
  }
}
