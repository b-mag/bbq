/**
 * =============================================================================
 * renderer.ts — Canvas 2D Rendering Engine
 * =============================================================================
 *
 * WHY CANVAS 2D (not WebGL):
 * For a tile-based top-down game with simple shapes, Canvas 2D is sufficient and
 * much simpler to work with. WebGL would add shader complexity without meaningful
 * performance benefit at our entity counts (<100 entities, ~2000 visible tiles).
 * Canvas 2D can easily handle 60fps for this workload on modern hardware.
 *
 * RENDER PIPELINE (per frame):
 *   1. Clear canvas
 *   2. Apply screen shake offset
 *   3. Render visible map tiles (culled to camera bounds)
 *   4. Render entities sorted by Y position (pseudo-depth ordering)
 *   5. Render atmospheric vignette overlay
 *
 * WHY Y-SORT ENTITIES:
 * Sorting entities by Y position before drawing creates a simple depth illusion:
 * entities lower on screen (higher Y) are drawn on top of entities higher on screen.
 * This makes it look like "lower" entities are in front — a common 2D game technique.
 *
 * VISUAL STYLE:
 * Dark, muted colors with a 1920s aesthetic. Players are circles (distinct per class),
 * enemies are triangles (menacing), projectiles are small glowing dots. Health bars
 * appear above damaged entities. A vignette darkens the screen edges for atmosphere.
 * =============================================================================
 */

import { Camera, worldToScreen, getVisibleBounds, getEffectiveTileSize } from './camera';
import { GameMap, TileType, TILE_COLORS, getTile } from '../map';
import { EntityState } from '../messages';
import { VisualEffect } from './effects';
import { SpriteCache, facingFromMotion, playerSpriteName } from './sprites';
import { drawAvatar } from './avatar';
import { TileAtlas } from './tilesets';
import { walkDistance, isAttacking, attackElapsedMs, syncAttackFromCooldown } from './spriteAnim';

// Entity rendering colors by class/type
const PLAYER_COLORS: Record<string, string> = {
  gangster: '#8b4513',    // Saddle brown
  detective: '#2f4f4f',   // Dark slate gray
  surgeon: '#f5f5dc',     // Beige/white
  invader: '#8b0000',     // Dark red — hostile invader
  default: '#c9a84c',     // Gold
};

const ENEMY_COLORS: Record<string, string> = {
  cultist_acolyte: '#4a1a2e',    // Dark crimson — basic melee
  cultist_torch: '#8b4000',      // Burnt orange — fire/torch bearer
  cultist_dagger: '#3a3a5c',     // Dark steel blue — dagger thrower
  cultist_shotgun: '#5c3a1a',    // Dark brown — shotgunner
  cultist_lightning: '#1a3a5c',  // Deep electric blue — lightning caster
  cultist_chanter: '#2e1a4a',    // Dark purple — eldritch chanter
  cult_leader: '#5c0a0a',        // Deep red — cult leader / mini-boss
  boss_warehouse: '#3d0000',     // Very deep red — final boss
  default: '#6b1a1a',            // Dark red fallback
};

/**
 * Render the complete game frame including map, entities, effects, and UI overlays.
 */
export function renderFrame(
  ctx: CanvasRenderingContext2D,
  camera: Camera,
  map: GameMap | null,
  entities: EntityState[],
  localPlayerId: string | null,
  screenShake: { x: number; y: number },
  effects: VisualEffect[] = [],
  spriteCache: SpriteCache | null = null,
  tileAtlas: TileAtlas | null = null,
  localFigure?: string
): void {
  const { viewportWidth, viewportHeight } = camera;
  const nowMs = Date.now();

  ctx.imageSmoothingEnabled = false;

  // Clear canvas
  ctx.fillStyle = '#0d0a07';
  ctx.fillRect(0, 0, viewportWidth, viewportHeight);

  // Apply screen shake
  ctx.save();
  ctx.translate(screenShake.x, screenShake.y);

  // Render map tiles
  if (map) {
    renderMap(ctx, camera, map, tileAtlas, nowMs);
  }

  // Render entities (sorted by Y for pseudo-depth)
  const sortedEntities = [...entities].sort((a, b) => a.y - b.y);

  for (const entity of sortedEntities) {
    if (!entity.isAlive) {
      if (entity.entityType === 'player') {
        renderDeathMarker(ctx, camera, entity);
      }
      continue;
    }
    renderEntity(
      ctx, camera, entity, entity.id === `player_${localPlayerId}`,
      spriteCache, nowMs, localFigure
    );
  }

  // Render visual effects (muzzle flashes, slash arcs, impact sparks)
  renderEffects(ctx, camera, effects);

  ctx.restore();

  // Render fog/vignette overlay
  renderVignette(ctx, viewportWidth, viewportHeight);
}

/**
 * Render the visible portion of the tile map.
 */
function renderMap(
  ctx: CanvasRenderingContext2D,
  camera: Camera,
  map: GameMap,
  atlas: TileAtlas | null,
  nowMs: number
): void {
  const bounds = getVisibleBounds(camera);
  const tileSize = getEffectiveTileSize(camera);

  const minX = Math.max(0, bounds.minX);
  const minY = Math.max(0, bounds.minY);
  const maxX = Math.min(map.width - 1, bounds.maxX);
  const maxY = Math.min(map.height - 1, bounds.maxY);

  for (let y = minY; y <= maxY; y++) {
    for (let x = minX; x <= maxX; x++) {
      const tile = getTile(map, x, y);
      const screen = worldToScreen(camera, x, y);

      const drawn = atlas?.drawDungeonTile(
        ctx, tile, x, y, screen.x, screen.y, tileSize, nowMs
      ) ?? false;

      if (!drawn) {
        ctx.fillStyle = TILE_COLORS[tile] || TILE_COLORS[TileType.Wall];
        ctx.fillRect(
          Math.floor(screen.x),
          Math.floor(screen.y),
          tileSize + 1,
          tileSize + 1
        );
      }
    }
  }
}

/**
 * Render a single entity as a basic shape.
 */
function renderEntity(
  ctx: CanvasRenderingContext2D,
  camera: Camera,
  entity: EntityState,
  isLocalPlayer: boolean,
  spriteCache: SpriteCache | null,
  nowMs: number,
  localFigure?: string
): void {
  const screen = worldToScreen(camera, entity.x, entity.y);
  const tileSize = getEffectiveTileSize(camera);
  const radius = tileSize * 0.35;

  switch (entity.entityType) {
    case 'player':
      renderPlayer(ctx, screen.x, screen.y, radius, entity, isLocalPlayer, spriteCache, nowMs, localFigure);
      break;
    case 'enemy':
      renderEnemy(ctx, screen.x, screen.y, radius, entity, spriteCache, nowMs, tileSize);
      break;
    case 'projectile':
      renderProjectile(ctx, screen.x, screen.y, tileSize * 0.15, entity);
      break;
  }
}

/**
 * Render a player entity as a circle with a class-colored fill.
 */
function renderPlayer(
  ctx: CanvasRenderingContext2D,
  x: number,
  y: number,
  radius: number,
  entity: EntityState,
  isLocal: boolean,
  spriteCache: SpriteCache | null,
  nowMs: number,
  localFigure?: string
): void {
  const color = PLAYER_COLORS[entity.subType || 'default'] || PLAYER_COLORS.default;
  const moving = entity.velocityX !== 0 || entity.velocityY !== 0;
  const facing = facingFromMotion(entity.id, entity.velocityX, entity.velocityY);
  const classSprite = entity.subType ? `player_${entity.subType}` : null;
  const spriteName = isLocal
    ? playerSpriteName(localFigure)
    : (classSprite && spriteCache?.getEntry(classSprite) ? classSprite : playerSpriteName());
  const dist = walkDistance(entity.id, entity.x, entity.y);
  const attacking = isAttacking(entity.id);
  const attackMs = attackElapsedMs(entity.id);

  ctx.fillStyle = 'rgba(0, 0, 0, 0.4)';
  ctx.beginPath();
  ctx.ellipse(x + 2, y + radius * 0.6, radius * 0.7, radius * 0.3, 0, 0, Math.PI * 2);
  ctx.fill();

  const tileSize = radius / 0.35;
  const figure = isLocal ? (localFigure || 'b') : (entity.subType && entity.subType.length <= 2 ? entity.subType : 'b');
  const drawn = drawAvatar(ctx, isLocal ? (localFigure || 'b') : figure, x, y, tileSize, {
    action: attacking ? 'attack' : (moving ? 'walk' : 'idle'),
    facing,
    distance: dist,
    attackElapsedMs: attacking ? attackMs : undefined,
    heightScale: 1.55,
  }) || (spriteCache?.drawSprite(ctx, spriteName, x, y, tileSize, {
    action: attacking ? 'attack' : (moving ? 'walk' : 'idle'),
    facing,
    distance: dist,
    attackElapsedMs: attacking ? attackMs : undefined,
    anchor: 'feet',
  }) ?? false);

  if (!drawn) {
    ctx.fillStyle = color;
    ctx.beginPath();
    ctx.arc(x, y, radius, 0, Math.PI * 2);
    ctx.fill();
    ctx.strokeStyle = isLocal ? '#c9a84c' : '#666';
    ctx.lineWidth = isLocal ? 2 : 1;
    ctx.stroke();
  }

  if (isLocal) {
    ctx.strokeStyle = 'rgba(201, 168, 76, 0.5)';
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.arc(x, y, radius + 4, 0, Math.PI * 2);
    ctx.stroke();
  }

  if (entity.health < entity.maxHealth) {
    renderHealthBar(ctx, x, y - radius - 6, radius * 2, 3, entity.health, entity.maxHealth);
  }
}

/**
 * Render an enemy entity as a triangle (pointing down = menacing).
 */
function renderEnemy(
  ctx: CanvasRenderingContext2D,
  x: number,
  y: number,
  radius: number,
  entity: EntityState,
  spriteCache: SpriteCache | null,
  nowMs: number,
  tileSize: number
): void {
  const visualType = (entity.subType || '').replace(/^elite_/i, '');
  const color = ENEMY_COLORS[visualType] || ENEMY_COLORS.default;
  const spriteName = spriteCache?.getEntry(visualType) ? visualType : 'gronk';
  const moving = entity.velocityX !== 0 || entity.velocityY !== 0;
  const dist = walkDistance(entity.id, entity.x, entity.y);
  const facing = facingFromMotion(entity.id, entity.velocityX, entity.velocityY);
  syncAttackFromCooldown(entity.id, entity.attackCooldown);
  const attacking = isAttacking(entity.id);
  const attackMs = attackElapsedMs(entity.id);

  ctx.fillStyle = 'rgba(0, 0, 0, 0.4)';
  ctx.beginPath();
  ctx.ellipse(x + 2, y + radius * 0.6, radius * 0.7, radius * 0.3, 0, 0, Math.PI * 2);
  ctx.fill();

  const drawn = spriteCache?.drawSprite(ctx, spriteName, x, y, tileSize, {
    action: attacking ? 'attack' : (moving ? 'walk' : 'idle'),
    facing,
    distance: dist,
    attackElapsedMs: attacking ? attackMs : undefined,
  }) ?? false;

  if (!drawn) {
    ctx.fillStyle = color;
    ctx.beginPath();
    ctx.moveTo(x, y - radius);
    ctx.lineTo(x - radius * 0.87, y + radius * 0.5);
    ctx.lineTo(x + radius * 0.87, y + radius * 0.5);
    ctx.closePath();
    ctx.fill();

    ctx.fillStyle = '#ff3333';
    ctx.beginPath();
    ctx.arc(x, y - radius * 0.1, radius * 0.15, 0, Math.PI * 2);
    ctx.fill();

    ctx.strokeStyle = '#8b0000';
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(x, y - radius);
    ctx.lineTo(x - radius * 0.87, y + radius * 0.5);
    ctx.lineTo(x + radius * 0.87, y + radius * 0.5);
    ctx.closePath();
    ctx.stroke();
  }

  if (entity.health < entity.maxHealth) {
    renderHealthBar(ctx, x, y - radius - 8, radius * 2, 3, entity.health, entity.maxHealth);
  }
}

/**
 * Render a projectile with visuals that vary by weapon/enemy type.
 * Player projectiles are warm-colored; enemy projectiles use distinct colors per type.
 */
function renderProjectile(
  ctx: CanvasRenderingContext2D,
  x: number,
  y: number,
  radius: number,
  entity: EntityState
): void {
  const subType = entity.subType || 'default';

  switch (subType) {
    case 'gangster':
      // Tommy gun bullets: small yellow dots with a motion trail
      ctx.fillStyle = 'rgba(255, 200, 50, 0.2)';
      ctx.beginPath();
      ctx.arc(x, y, radius * 2, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillStyle = '#ffd700';
      ctx.beginPath();
      ctx.arc(x, y, radius * 0.8, 0, Math.PI * 2);
      ctx.fill();
      // Trail line in velocity direction
      if (entity.velocityX !== 0 || entity.velocityY !== 0) {
        const trailLen = radius * 3;
        const angle = Math.atan2(-entity.velocityY, -entity.velocityX);
        ctx.strokeStyle = 'rgba(255, 200, 50, 0.4)';
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.moveTo(x, y);
        ctx.lineTo(x + Math.cos(angle) * trailLen, y + Math.sin(angle) * trailLen);
        ctx.stroke();
      }
      break;

    case 'detective':
      // Magnum bullet: larger, brighter, with a prominent trail
      ctx.fillStyle = 'rgba(255, 255, 200, 0.3)';
      ctx.beginPath();
      ctx.arc(x, y, radius * 3, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillStyle = '#ffffff';
      ctx.beginPath();
      ctx.arc(x, y, radius * 1.2, 0, Math.PI * 2);
      ctx.fill();
      // Bright trail
      if (entity.velocityX !== 0 || entity.velocityY !== 0) {
        const trailLen = radius * 5;
        const angle = Math.atan2(-entity.velocityY, -entity.velocityX);
        ctx.strokeStyle = 'rgba(255, 240, 200, 0.6)';
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.moveTo(x, y);
        ctx.lineTo(x + Math.cos(angle) * trailLen, y + Math.sin(angle) * trailLen);
        ctx.stroke();
      }
      break;

    case 'eldritch_bolt':
      // Purple glowing bolt from chanter enemies
      ctx.fillStyle = 'rgba(128, 0, 255, 0.3)';
      ctx.beginPath();
      ctx.arc(x, y, radius * 2.5, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillStyle = '#9933ff';
      ctx.beginPath();
      ctx.arc(x, y, radius, 0, Math.PI * 2);
      ctx.fill();
      break;

    case 'dagger':
      // Gray steel streak — fast-moving thrown knife
      ctx.fillStyle = '#b0b0b0';
      ctx.beginPath();
      ctx.arc(x, y, radius * 0.7, 0, Math.PI * 2);
      ctx.fill();
      // Elongated trail to suggest a blade shape
      if (entity.velocityX !== 0 || entity.velocityY !== 0) {
        const angle = Math.atan2(entity.velocityY, entity.velocityX);
        ctx.strokeStyle = '#cccccc';
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.moveTo(x - Math.cos(angle) * radius * 2, y - Math.sin(angle) * radius * 2);
        ctx.lineTo(x + Math.cos(angle) * radius * 2, y + Math.sin(angle) * radius * 2);
        ctx.stroke();
      }
      break;

    case 'shotgun_pellet':
      // Small orange dots — buckshot pellets
      ctx.fillStyle = 'rgba(255, 140, 0, 0.4)';
      ctx.beginPath();
      ctx.arc(x, y, radius * 1.5, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillStyle = '#ff8c00';
      ctx.beginPath();
      ctx.arc(x, y, radius * 0.6, 0, Math.PI * 2);
      ctx.fill();
      break;

    case 'lightning_bolt':
      // Bright blue jagged bolt — electric energy
      ctx.fillStyle = 'rgba(50, 150, 255, 0.4)';
      ctx.beginPath();
      ctx.arc(x, y, radius * 3, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillStyle = '#66ccff';
      ctx.beginPath();
      ctx.arc(x, y, radius * 1.2, 0, Math.PI * 2);
      ctx.fill();
      // Electric glow shimmer
      ctx.strokeStyle = 'rgba(100, 200, 255, 0.6)';
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.arc(x, y, radius * 2 + Math.sin(Date.now() * 0.02) * radius, 0, Math.PI * 2);
      ctx.stroke();
      break;

    default:
      // Default golden projectile (fallback)
      ctx.fillStyle = 'rgba(255, 200, 50, 0.3)';
      ctx.beginPath();
      ctx.arc(x, y, radius * 2.5, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillStyle = '#ffc832';
      ctx.beginPath();
      ctx.arc(x, y, radius, 0, Math.PI * 2);
      ctx.fill();
      break;
  }
}

/**
 * Render a health bar above an entity.
 */
function renderHealthBar(
  ctx: CanvasRenderingContext2D,
  x: number,
  y: number,
  width: number,
  height: number,
  current: number,
  max: number
): void {
  const barX = x - width / 2;
  const ratio = Math.max(0, current / max);

  // Background
  ctx.fillStyle = 'rgba(0, 0, 0, 0.6)';
  ctx.fillRect(barX, y, width, height);

  // Health fill (green → yellow → red)
  const color = ratio > 0.6 ? '#4a8c3f' : ratio > 0.3 ? '#c9a84c' : '#a83232';
  ctx.fillStyle = color;
  ctx.fillRect(barX, y, width * ratio, height);

  // Border
  ctx.strokeStyle = '#333';
  ctx.lineWidth = 0.5;
  ctx.strokeRect(barX, y, width, height);
}

/**
 * Render a death X marker at a dead player's position.
 * Shows a red X with a fading pulse effect to indicate where a teammate fell.
 */
function renderDeathMarker(
  ctx: CanvasRenderingContext2D,
  camera: Camera,
  entity: EntityState
): void {
  const screen = worldToScreen(camera, entity.x, entity.y);
  const tileSize = getEffectiveTileSize(camera);
  const size = tileSize * 0.4;

  // Pulsing opacity for visibility
  const pulse = 0.6 + Math.sin(Date.now() * 0.003) * 0.2;

  // Red X mark
  ctx.strokeStyle = `rgba(168, 50, 50, ${pulse})`;
  ctx.lineWidth = 3;
  ctx.lineCap = 'round';
  ctx.beginPath();
  ctx.moveTo(screen.x - size, screen.y - size);
  ctx.lineTo(screen.x + size, screen.y + size);
  ctx.moveTo(screen.x + size, screen.y - size);
  ctx.lineTo(screen.x - size, screen.y + size);
  ctx.stroke();

  // Dim glow around the X
  ctx.strokeStyle = `rgba(168, 50, 50, ${pulse * 0.3})`;
  ctx.lineWidth = 6;
  ctx.beginPath();
  ctx.moveTo(screen.x - size, screen.y - size);
  ctx.lineTo(screen.x + size, screen.y + size);
  ctx.moveTo(screen.x + size, screen.y - size);
  ctx.lineTo(screen.x - size, screen.y + size);
  ctx.stroke();
}

/**
 * Render a dark vignette around the edges for atmosphere.
 */
function renderVignette(
  ctx: CanvasRenderingContext2D,
  width: number,
  height: number
): void {
  const gradient = ctx.createRadialGradient(
    width / 2, height / 2, Math.min(width, height) * 0.3,
    width / 2, height / 2, Math.max(width, height) * 0.7
  );
  gradient.addColorStop(0, 'rgba(0, 0, 0, 0)');
  gradient.addColorStop(1, 'rgba(0, 0, 0, 0.4)');
  ctx.fillStyle = gradient;
  ctx.fillRect(0, 0, width, height);
}

/**
 * Render all active visual effects (muzzle flashes, slash arcs, impact sparks).
 * Effects are positioned in world coordinates and fade out over their duration.
 */
function renderEffects(
  ctx: CanvasRenderingContext2D,
  camera: Camera,
  effects: VisualEffect[]
): void {
  const now = performance.now();
  const tileSize = getEffectiveTileSize(camera);

  for (const effect of effects) {
    const elapsed = now - effect.startTime;
    const progress = elapsed / effect.duration; // 0 to 1
    const alpha = 1 - progress; // Fade out over duration

    if (alpha <= 0) continue;

    const screen = worldToScreen(camera, effect.x, effect.y);

    switch (effect.type) {
      case 'muzzle_flash':
        renderMuzzleFlash(ctx, screen.x, screen.y, tileSize, effect, alpha);
        break;
      case 'slash_arc':
        renderSlashArc(ctx, screen.x, screen.y, tileSize, effect, alpha);
        break;
      case 'impact_spark':
        renderImpactSpark(ctx, screen.x, screen.y, tileSize, effect, alpha);
        break;
    }
  }
}

/**
 * Render a muzzle flash — a brief bright burst at the firing position.
 */
function renderMuzzleFlash(
  ctx: CanvasRenderingContext2D,
  x: number,
  y: number,
  tileSize: number,
  effect: VisualEffect,
  alpha: number
): void {
  const radius = tileSize * 0.3 * effect.size;

  // Outer glow
  ctx.fillStyle = `rgba(255, 200, 50, ${alpha * 0.4})`;
  ctx.beginPath();
  ctx.arc(x, y, radius * 2, 0, Math.PI * 2);
  ctx.fill();

  // Inner bright core
  ctx.fillStyle = `rgba(255, 255, 200, ${alpha * 0.8})`;
  ctx.beginPath();
  ctx.arc(x, y, radius, 0, Math.PI * 2);
  ctx.fill();
}

/**
 * Render a slash arc — a curved sweep showing the surgeon's dagger attack.
 */
function renderSlashArc(
  ctx: CanvasRenderingContext2D,
  x: number,
  y: number,
  tileSize: number,
  effect: VisualEffect,
  alpha: number
): void {
  const radius = tileSize * 0.8;
  const sweepAngle = Math.PI * 0.8; // ~144 degree arc

  ctx.save();
  ctx.translate(x, y);

  // Draw an arc sweep in the aim direction
  ctx.strokeStyle = `rgba(224, 224, 224, ${alpha * 0.9})`;
  ctx.lineWidth = 3;
  ctx.lineCap = 'round';
  ctx.beginPath();
  ctx.arc(0, 0, radius, effect.angle - sweepAngle / 2, effect.angle + sweepAngle / 2);
  ctx.stroke();

  // Inner arc for depth
  ctx.strokeStyle = `rgba(200, 200, 220, ${alpha * 0.5})`;
  ctx.lineWidth = 1.5;
  ctx.beginPath();
  ctx.arc(0, 0, radius * 0.7, effect.angle - sweepAngle / 2, effect.angle + sweepAngle / 2);
  ctx.stroke();

  ctx.restore();
}

/**
 * Render an impact spark — a small burst where a projectile hits.
 */
function renderImpactSpark(
  ctx: CanvasRenderingContext2D,
  x: number,
  y: number,
  tileSize: number,
  effect: VisualEffect,
  alpha: number
): void {
  const radius = tileSize * 0.2 * effect.size;
  const sparkCount = 4;

  // Central flash
  ctx.fillStyle = `rgba(255, 140, 0, ${alpha * 0.7})`;
  ctx.beginPath();
  ctx.arc(x, y, radius, 0, Math.PI * 2);
  ctx.fill();

  // Small spark lines radiating outward
  ctx.strokeStyle = `rgba(255, 200, 50, ${alpha * 0.6})`;
  ctx.lineWidth = 1;
  for (let i = 0; i < sparkCount; i++) {
    const angle = (i / sparkCount) * Math.PI * 2 + effect.startTime * 0.01;
    const len = radius * 2 * (1 - alpha * 0.5); // Sparks expand as they fade
    ctx.beginPath();
    ctx.moveTo(x, y);
    ctx.lineTo(x + Math.cos(angle) * len, y + Math.sin(angle) * len);
    ctx.stroke();
  }
}
