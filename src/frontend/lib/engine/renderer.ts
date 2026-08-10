/**
 * Canvas rendering engine for the top-down RPG.
 * Renders the tile map, entities, and visual effects.
 */

import { Camera, worldToScreen, getVisibleBounds } from './camera';
import { GameMap, TileType, TILE_COLORS, getTile } from '../map';
import { EntityState } from '../messages';

// Entity rendering colors by class/type
const PLAYER_COLORS: Record<string, string> = {
  gangster: '#8b4513',    // Saddle brown
  detective: '#2f4f4f',   // Dark slate gray
  surgeon: '#f5f5dc',     // Beige/white
  default: '#c9a84c',     // Gold
};

const ENEMY_COLORS: Record<string, string> = {
  cultist_acolyte: '#4a1a2e',  // Dark crimson
  cultist_chanter: '#2e1a4a',  // Dark purple
  cult_leader: '#5c0a0a',      // Deep red
  default: '#6b1a1a',          // Dark red
};

/**
 * Render the complete game frame.
 */
export function renderFrame(
  ctx: CanvasRenderingContext2D,
  camera: Camera,
  map: GameMap | null,
  entities: EntityState[],
  localPlayerId: string | null,
  screenShake: { x: number; y: number }
): void {
  const { viewportWidth, viewportHeight } = camera;

  // Clear canvas
  ctx.fillStyle = '#0d0a07';
  ctx.fillRect(0, 0, viewportWidth, viewportHeight);

  // Apply screen shake
  ctx.save();
  ctx.translate(screenShake.x, screenShake.y);

  // Render map tiles
  if (map) {
    renderMap(ctx, camera, map);
  }

  // Render entities (sorted by Y for pseudo-depth)
  const sortedEntities = [...entities].sort((a, b) => a.y - b.y);

  for (const entity of sortedEntities) {
    if (!entity.isAlive) continue;
    renderEntity(ctx, camera, entity, entity.id === `player_${localPlayerId}`);
  }

  ctx.restore();

  // Render fog/vignette overlay
  renderVignette(ctx, viewportWidth, viewportHeight);
}

/**
 * Render the visible portion of the tile map.
 */
function renderMap(ctx: CanvasRenderingContext2D, camera: Camera, map: GameMap): void {
  const bounds = getVisibleBounds(camera);
  const { tileSize } = camera;

  // Clamp bounds to map dimensions
  const minX = Math.max(0, bounds.minX);
  const minY = Math.max(0, bounds.minY);
  const maxX = Math.min(map.width - 1, bounds.maxX);
  const maxY = Math.min(map.height - 1, bounds.maxY);

  for (let y = minY; y <= maxY; y++) {
    for (let x = minX; x <= maxX; x++) {
      const tile = getTile(map, x, y);
      const screen = worldToScreen(camera, x, y);

      // Fill tile
      ctx.fillStyle = TILE_COLORS[tile] || TILE_COLORS[TileType.Wall];
      ctx.fillRect(
        Math.floor(screen.x),
        Math.floor(screen.y),
        tileSize + 1,  // +1 to prevent gaps between tiles
        tileSize + 1
      );

      // Add subtle grid lines for floor/cobblestone
      if (tile === TileType.Floor || tile === TileType.Cobblestone) {
        ctx.strokeStyle = 'rgba(0, 0, 0, 0.15)';
        ctx.lineWidth = 0.5;
        ctx.strokeRect(
          Math.floor(screen.x),
          Math.floor(screen.y),
          tileSize,
          tileSize
        );
      }

      // Door highlight
      if (tile === TileType.Door) {
        ctx.strokeStyle = '#8a6d2f';
        ctx.lineWidth = 1;
        ctx.strokeRect(
          Math.floor(screen.x) + 2,
          Math.floor(screen.y) + 2,
          tileSize - 4,
          tileSize - 4
        );
      }

      // Water shimmer effect
      if (tile === TileType.Water) {
        const shimmer = Math.sin(Date.now() * 0.001 + x * 0.5 + y * 0.3) * 0.1;
        ctx.fillStyle = `rgba(100, 150, 200, ${0.05 + shimmer})`;
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
  isLocalPlayer: boolean
): void {
  const screen = worldToScreen(camera, entity.x, entity.y);
  const { tileSize } = camera;
  const radius = tileSize * 0.35;

  switch (entity.entityType) {
    case 'player':
      renderPlayer(ctx, screen.x, screen.y, radius, entity, isLocalPlayer);
      break;
    case 'enemy':
      renderEnemy(ctx, screen.x, screen.y, radius, entity);
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
  isLocal: boolean
): void {
  const color = PLAYER_COLORS[entity.subType || 'default'] || PLAYER_COLORS.default;

  // Shadow
  ctx.fillStyle = 'rgba(0, 0, 0, 0.4)';
  ctx.beginPath();
  ctx.ellipse(x + 2, y + radius * 0.6, radius * 0.7, radius * 0.3, 0, 0, Math.PI * 2);
  ctx.fill();

  // Body circle
  ctx.fillStyle = color;
  ctx.beginPath();
  ctx.arc(x, y, radius, 0, Math.PI * 2);
  ctx.fill();

  // Border
  ctx.strokeStyle = isLocal ? '#c9a84c' : '#666';
  ctx.lineWidth = isLocal ? 2 : 1;
  ctx.stroke();

  // Local player indicator (golden ring)
  if (isLocal) {
    ctx.strokeStyle = 'rgba(201, 168, 76, 0.5)';
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.arc(x, y, radius + 4, 0, Math.PI * 2);
    ctx.stroke();
  }

  // Health bar (only show if damaged)
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
  entity: EntityState
): void {
  const color = ENEMY_COLORS[entity.subType || 'default'] || ENEMY_COLORS.default;

  // Shadow
  ctx.fillStyle = 'rgba(0, 0, 0, 0.4)';
  ctx.beginPath();
  ctx.ellipse(x + 2, y + radius * 0.6, radius * 0.7, radius * 0.3, 0, 0, Math.PI * 2);
  ctx.fill();

  // Triangle body
  ctx.fillStyle = color;
  ctx.beginPath();
  ctx.moveTo(x, y - radius);
  ctx.lineTo(x - radius * 0.87, y + radius * 0.5);
  ctx.lineTo(x + radius * 0.87, y + radius * 0.5);
  ctx.closePath();
  ctx.fill();

  // Glowing eye effect
  ctx.fillStyle = '#ff3333';
  ctx.beginPath();
  ctx.arc(x, y - radius * 0.1, radius * 0.15, 0, Math.PI * 2);
  ctx.fill();

  // Border
  ctx.strokeStyle = '#8b0000';
  ctx.lineWidth = 1;
  ctx.beginPath();
  ctx.moveTo(x, y - radius);
  ctx.lineTo(x - radius * 0.87, y + radius * 0.5);
  ctx.lineTo(x + radius * 0.87, y + radius * 0.5);
  ctx.closePath();
  ctx.stroke();

  // Health bar
  if (entity.health < entity.maxHealth) {
    renderHealthBar(ctx, x, y - radius - 8, radius * 2, 3, entity.health, entity.maxHealth);
  }
}

/**
 * Render a projectile as a small glowing circle.
 */
function renderProjectile(
  ctx: CanvasRenderingContext2D,
  x: number,
  y: number,
  radius: number,
  entity: EntityState
): void {
  // Glow effect
  ctx.fillStyle = 'rgba(255, 200, 50, 0.3)';
  ctx.beginPath();
  ctx.arc(x, y, radius * 2.5, 0, Math.PI * 2);
  ctx.fill();

  // Core
  ctx.fillStyle = '#ffc832';
  ctx.beginPath();
  ctx.arc(x, y, radius, 0, Math.PI * 2);
  ctx.fill();
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
