/**
 * =============================================================================
 * OverworldCanvas.tsx — Canvas Renderer for the Persistent Overworld
 * =============================================================================
 *
 * Renders the overworld map and all connected players. Reuses the existing
 * camera system for zoom/pan. Players and enemies use manifest sprites; tiles
 * are chunk-cached so zooming out does not stall movement.
 * =============================================================================
 */
'use client';

import { useEffect, useRef, useCallback } from 'react';
import { Camera, createCamera, cameraFollow, worldToScreen, screenToWorld, getVisibleBounds, getEffectiveTileSize, MIN_ZOOM, MAX_ZOOM } from '@/lib/engine/camera';
import { OverworldGameMap, OwTileType, OW_TILE_COLORS, getOwTile } from '@/lib/overworld-map';
import { OwPlayerState, OwDungeonEntranceData, OwWorldObjectData, OwLandmarkData } from '@/lib/overworld-messages';
import { SpriteCache, initSprites, facingFromMotion, facingFromVelocity, playerSpriteName } from '@/lib/engine/sprites';
import { walkDistance, isAttacking, attackElapsedMs, noteAttack, syncAttackFromCooldown } from '@/lib/engine/spriteAnim';
import { TileAtlas, initTilesets } from '@/lib/engine/tilesets';
import { OverworldTileCache } from '@/lib/engine/tileLayer';
import { EnemyState, ProjectileState, LootDropState } from '@/hooks/useOverworldEnemies';

interface OverworldCanvasProps {
  map: OverworldGameMap | null;
  players: OwPlayerState[];
  localPlayerId: string | null;
  dungeonEntrances: OwDungeonEntranceData[];
  worldObjects: OwWorldObjectData[];
  landmarks: OwLandmarkData[];
  enemies: EnemyState[];
  projectiles: ProjectileState[];
  lootDrops: LootDropState[];
  width: number;
  height: number;
  onPlayerClick?: (playerId: string) => void;
  lakeDrained?: boolean;
  combatEnabled?: boolean;
  interiorMode?: boolean;
}

export default function OverworldCanvas({
  map, players, localPlayerId, dungeonEntrances, worldObjects, landmarks, enemies, projectiles, lootDrops, width, height, onPlayerClick,
  lakeDrained = false, combatEnabled = true, interiorMode = false,
}: OverworldCanvasProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const cameraRef = useRef<Camera>(createCamera(width, height, 32));
  const animFrameRef = useRef<number>(0);
  const spriteCacheRef = useRef<SpriteCache | null>(null);
  const tileAtlasRef = useRef<TileAtlas | null>(null);
  const tileCacheRef = useRef(new OverworldTileCache());
  const cameraInitializedRef = useRef(false);
  const slashFxRef = useRef<Array<{ x: number; y: number; angle: number; start: number }>>([]);

  const mapRef = useRef(map);
  const playersRef = useRef(players);
  const localIdRef = useRef(localPlayerId);
  const entrancesRef = useRef(dungeonEntrances);
  const objectsRef = useRef(worldObjects);
  const landmarksRef = useRef(landmarks);
  const enemiesRef = useRef(enemies);
  const projectilesRef = useRef(projectiles);
  const lootRef = useRef(lootDrops);
  const lakeDrainedRef = useRef(lakeDrained);
  const interiorRef = useRef(interiorMode);
  mapRef.current = map;
  playersRef.current = players;
  localIdRef.current = localPlayerId;
  entrancesRef.current = dungeonEntrances;
  objectsRef.current = worldObjects;
  landmarksRef.current = landmarks;
  enemiesRef.current = enemies;
  projectilesRef.current = projectiles;
  lootRef.current = lootDrops;
  lakeDrainedRef.current = lakeDrained;
  interiorRef.current = interiorMode;

  useEffect(() => {
    initSprites().then(cache => {
      spriteCacheRef.current = cache;
    });
    initTilesets().then(atlas => {
      tileAtlasRef.current = atlas;
      tileCacheRef.current.invalidate();
    });
    cameraRef.current.zoom = 1.2;
  }, []);

  useEffect(() => {
    cameraRef.current.zoom = interiorMode ? 1.7 : 1.2;
    cameraInitializedRef.current = false;
    tileCacheRef.current.invalidate();
  }, [interiorMode]);

  useEffect(() => {
    cameraRef.current.viewportWidth = width;
    cameraRef.current.viewportHeight = height;
    cameraRef.current.tileSize = 32;
  }, [width, height]);

  // Render loop
  const render = useCallback(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const camera = cameraRef.current;
    const mapNow = mapRef.current;
    const playersNow = playersRef.current;
    const localId = localIdRef.current;
    const dungeonEntrancesNow = entrancesRef.current;
    const worldObjectsNow = objectsRef.current;
    const landmarksNow = landmarksRef.current;
    const enemiesNow = enemiesRef.current;
    const projectilesNow = projectilesRef.current;
    const lootNow = lootRef.current;
    const lakeDrainedNow = lakeDrainedRef.current;
    const interiorNow = interiorRef.current;

    const localPlayer = playersNow.find(p => p.id === localId);
    if (localPlayer) {
      if (!cameraInitializedRef.current && (localPlayer.x !== 0 || localPlayer.y !== 0)) {
        camera.x = localPlayer.x;
        camera.y = localPlayer.y;
        cameraInitializedRef.current = true;
      } else {
        cameraFollow(camera, localPlayer.x, localPlayer.y, 0.12);
      }
    }

    ctx.imageSmoothingEnabled = false;
    drawRegionBackdrop(ctx, camera, localPlayer, mapNow, width, height);

    const nowMs = Date.now();

    if (mapNow) {
      tileCacheRef.current.draw(ctx, camera, mapNow, tileAtlasRef.current, nowMs);
    }

    for (const drop of lootNow) {
      renderLootDrop(ctx, camera, drop);
    }

    const sprites = spriteCacheRef.current;
    const drawables: Array<{ y: number; draw: () => void }> = [];

    if (!interiorNow) {
      for (const entrance of dungeonEntrancesNow) {
        drawables.push({
          y: entrance.y,
          draw: () => renderDungeonEntrances(ctx, camera, [entrance], sprites, nowMs),
        });
      }
    }

    const visibleObjects = worldObjectsNow.filter(o => lakeDrainedNow || o.type !== 'lake_shop');
    for (const obj of visibleObjects) {
      const wander = obj.type.startsWith('npc_') ? npcWander(obj, nowMs) : { x: obj.x, y: obj.y };
      drawables.push({
        y: wander.y,
        draw: () => renderWorldObject(ctx, camera, { ...obj, x: wander.x, y: wander.y }, sprites),
      });
    }

    for (const player of playersNow) {
      if (player.status === 'in_dungeon') continue;
      const facing = facingFromMotion(player.id, player.velocityX, player.velocityY);
      drawables.push({
        y: player.y,
        draw: () => renderPlayer(ctx, camera, player, player.id === localId, sprites, nowMs, facing),
      });
    }

    if (!interiorNow) {
      for (const enemy of enemiesNow) {
        drawables.push({
          y: enemy.y,
          draw: () => renderEnemy(ctx, camera, enemy, sprites, nowMs),
        });
      }
    }

    drawables.sort((a, b) => a.y - b.y);
    for (const d of drawables) d.draw();

    if (!interiorNow) {
      for (const proj of projectilesNow) {
        renderProjectile(ctx, camera, proj);
      }
      renderSlashFx(ctx, camera, slashFxRef.current, nowMs);
    }

    if (localPlayer) {
      renderLandmarkLabels(ctx, camera, landmarksNow, localPlayer.x, localPlayer.y);
    }

    renderVignette(ctx, width, height);

    animFrameRef.current = requestAnimationFrame(render);
  }, [width, height]);

  useEffect(() => {
    animFrameRef.current = requestAnimationFrame(render);
    return () => cancelAnimationFrame(animFrameRef.current);
  }, [render]);

  // Handle scroll zoom
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const handleWheel = (e: WheelEvent) => {
      e.preventDefault();
      const camera = cameraRef.current;
      const delta = e.deltaY > 0 ? -0.1 : 0.1;
      camera.zoom = Math.max(MIN_ZOOM, Math.min(MAX_ZOOM, camera.zoom + delta));
      tileCacheRef.current.invalidate();
    };
    canvas.addEventListener('wheel', handleWheel, { passive: false });
    return () => canvas.removeEventListener('wheel', handleWheel);
  }, []);

  // Handle click on players for party invite
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas || !onPlayerClick) return;
    const handleClick = (e: MouseEvent) => {
      const rect = canvas.getBoundingClientRect();
      const screenX = e.clientX - rect.left;
      const screenY = e.clientY - rect.top;
      const camera = cameraRef.current;
      const world = screenToWorld(camera, screenX, screenY);

      // Check if click is near any other player (within 1 tile)
      for (const player of players) {
        if (player.id === localPlayerId) continue;
        if (player.status === 'in_dungeon') continue;
        const dist = Math.sqrt((world.x - player.x) ** 2 + (world.y - player.y) ** 2);
        if (dist < 1.0) {
          onPlayerClick(player.id);
          return;
        }
      }

      // Not a player click — fire primary ability toward cursor
      if (e.button === 0 && combatEnabled) {
        const localPlayer = players.find(p => p.id === localPlayerId);
        if (localPlayer) {
          const aimAngle = Math.atan2(world.y - localPlayer.y, world.x - localPlayer.x);
          fetch('/api/gameplay/combat-action', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ abilitySlot: 'primary', aimAngle }),
          }).catch(() => {});
          if (localPlayerId) {
            noteAttack(localPlayerId);
            slashFxRef.current.push({ x: localPlayer.x, y: localPlayer.y, angle: aimAngle, start: performance.now() });
          }
        }
      }
    };

    // Right-click = secondary ability
    const handleContextMenu = (e: MouseEvent) => {
      e.preventDefault();
      const rect = canvas.getBoundingClientRect();
      const screenX = e.clientX - rect.left;
      const screenY = e.clientY - rect.top;
      const camera = cameraRef.current;
      const world = screenToWorld(camera, screenX, screenY);

      const localPlayer = players.find(p => p.id === localPlayerId);
      if (localPlayer && combatEnabled) {
        const aimAngle = Math.atan2(world.y - localPlayer.y, world.x - localPlayer.x);
        fetch('/api/gameplay/combat-action', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ abilitySlot: 'secondary', aimAngle }),
        }).catch(() => {});
        if (localPlayerId) {
          noteAttack(localPlayerId);
          slashFxRef.current.push({ x: localPlayer.x, y: localPlayer.y, angle: aimAngle, start: performance.now() });
        }
      }
    };

    canvas.addEventListener('click', handleClick);
    canvas.addEventListener('contextmenu', handleContextMenu);
    return () => {
      canvas.removeEventListener('click', handleClick);
      canvas.removeEventListener('contextmenu', handleContextMenu);
    };
  }, [players, localPlayerId, onPlayerClick, combatEnabled]);

  return (
    <canvas
      ref={canvasRef}
      width={width}
      height={height}
      style={{ display: 'block', background: '#0d0f07', imageRendering: 'pixelated' }}
    />
  );
}

// =============================================================================
// Rendering functions
// =============================================================================

function drawRegionBackdrop(
  ctx: CanvasRenderingContext2D,
  camera: Camera,
  localPlayer: OwPlayerState | undefined,
  map: OverworldGameMap | null,
  width: number,
  height: number
) {
  const yNorm = map && localPlayer ? localPlayer.y / map.height : 0.5;
  const xNorm = map && localPlayer ? localPlayer.x / map.width : 0.5;
  let top = '#0d0f07';
  let bot = '#0a0c06';
  if (yNorm < 0.16) {
    top = '#05040a';
    bot = '#120c18';
  } else if (yNorm > 0.88) {
    top = '#0a1018';
    bot = '#061018';
  } else if (xNorm < 0.32 && yNorm > 0.28 && yNorm < 0.55) {
    top = '#1a1408';
    bot = '#2a1c0c';
  } else if (xNorm < 0.42 && yNorm > 0.48 && yNorm < 0.68) {
    top = '#0a120c';
    bot = '#08140e';
  }

  const g = ctx.createLinearGradient(0, 0, 0, height);
  g.addColorStop(0, top);
  g.addColorStop(1, bot);
  ctx.fillStyle = g;
  ctx.fillRect(0, 0, width, height);

    if (yNorm < 0.16) {
      const t = Date.now() * 0.00015;
      const parX = camera.x * 8;
      ctx.fillStyle = '#e8e0d0';
      for (let layer = 0; layer < 3; layer++) {
        const drift = parX * (0.12 + layer * 0.08) + t * (20 + layer * 14);
        const count = 28 + layer * 16;
        ctx.globalAlpha = 0.22 + layer * 0.12;
        for (let i = 0; i < count; i++) {
          const n = Math.sin((i * 19.17 + layer * 8) * 12.9898) * 43758.5453;
          const m = Math.sin((i * 37.91 + layer * 3) * 78.233) * 23421.123;
          const sx = ((n - Math.floor(n)) * width * 1.4 + drift) % width;
          const sy = (m - Math.floor(m)) * height * (0.42 + layer * 0.08);
          const r = layer === 0 && i % 9 === 0 ? 1.8 : 0.55 + layer * 0.25;
          ctx.beginPath();
          ctx.arc(sx < 0 ? sx + width : sx, sy, r, 0, Math.PI * 2);
          ctx.fill();
        }
      }
      ctx.globalAlpha = 0.18;
      ctx.fillStyle = '#9B6BC9';
      ctx.beginPath();
      ctx.arc(width * 0.72 - (camera.x * 0.4) % 40, height * 0.18, 18, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillStyle = '#C45A30';
      ctx.beginPath();
      ctx.arc(width * 0.78 - (camera.x * 0.35) % 40, height * 0.22, 11, 0, Math.PI * 2);
      ctx.fill();
      ctx.globalAlpha = 1;
      ctx.fillStyle = 'rgba(20, 0, 8, 0.28)';
      ctx.fillRect(0, 0, width, height * 0.22);
    }
  }

function renderSlashFx(
  ctx: CanvasRenderingContext2D,
  camera: Camera,
  fx: Array<{ x: number; y: number; angle: number; start: number }>,
  _nowMs: number
) {
  const now = performance.now();
  const tileSize = getEffectiveTileSize(camera);
  for (let i = fx.length - 1; i >= 0; i--) {
    const e = fx[i];
    const t = (now - e.start) / 220;
    if (t >= 1) {
      fx.splice(i, 1);
      continue;
    }
    const screen = worldToScreen(camera, e.x, e.y);
    const alpha = 1 - t;
    ctx.save();
    ctx.translate(screen.x, screen.y);
    ctx.strokeStyle = `rgba(230, 220, 200, ${alpha * 0.95})`;
    ctx.lineWidth = Math.max(2, tileSize * 0.08);
    ctx.lineCap = 'round';
    ctx.beginPath();
    ctx.arc(0, 0, tileSize * 0.7, e.angle - 0.7, e.angle + 0.7);
    ctx.stroke();
    ctx.strokeStyle = `rgba(255, 240, 180, ${alpha * 0.5})`;
    ctx.lineWidth = 1.5;
    ctx.beginPath();
    ctx.arc(0, 0, tileSize * 0.55, e.angle - 0.5, e.angle + 0.5);
    ctx.stroke();
    ctx.restore();
  }
}

function renderMapTiles(
  ctx: CanvasRenderingContext2D,
  camera: Camera,
  map: OverworldGameMap,
  atlas: TileAtlas | null,
  nowMs: number
) {
  const bounds = getVisibleBounds(camera);
  const tileSize = getEffectiveTileSize(camera);

  const minX = Math.max(0, bounds.minX);
  const minY = Math.max(0, bounds.minY);
  const maxX = Math.min(map.width - 1, bounds.maxX);
  const maxY = Math.min(map.height - 1, bounds.maxY);

  for (let y = minY; y <= maxY; y++) {
    for (let x = minX; x <= maxX; x++) {
      const tile = getOwTile(map, x, y);
      const screen = worldToScreen(camera, x, y);

      const drawn = atlas?.drawOverworldTile(
        ctx, tile, x, y, screen.x, screen.y, tileSize, nowMs,
        (dx, dy) => getOwTile(map, x + dx, y + dy)
      ) ?? false;

      if (!drawn) {
        ctx.fillStyle = OW_TILE_COLORS[tile] || OW_TILE_COLORS[OwTileType.Grass];
        ctx.fillRect(Math.floor(screen.x), Math.floor(screen.y), tileSize + 1, tileSize + 1);
      }
    }
  }
}

function renderDungeonEntrances(
  ctx: CanvasRenderingContext2D,
  camera: Camera,
  entrances: OwDungeonEntranceData[],
  spriteCache: SpriteCache | null,
  nowMs: number
) {
  const tileSize = getEffectiveTileSize(camera);

  for (const entrance of entrances) {
    const screen = worldToScreen(camera, entrance.x, entrance.y);
    const size = tileSize * 1.2;
    const pulse = 0.5 + Math.sin(nowMs * 0.003) * 0.3;

    const drawn = spriteCache?.drawSprite(
      ctx, 'dungeon_entrance',
      screen.x, screen.y,
      tileSize,
      { anchor: 'feet' }
    ) ?? false;

    if (!drawn) {
      ctx.fillStyle = `rgba(60, 20, 20, ${0.8 + pulse * 0.2})`;
      ctx.beginPath();
      ctx.arc(screen.x + tileSize / 2, screen.y + tileSize / 2, size / 2, 0, Math.PI * 2);
      ctx.fill();
    }

    ctx.strokeStyle = `rgba(200, 100, 50, ${pulse * 0.6})`;
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.arc(screen.x + tileSize / 2, screen.y + tileSize / 2, size / 2 + 2, 0, Math.PI * 2);
    ctx.stroke();

    ctx.fillStyle = `rgba(200, 168, 76, ${0.7 + pulse * 0.3})`;
    ctx.font = `${Math.max(8, tileSize * 0.4)}px Georgia, serif`;
    ctx.textAlign = 'center';
    ctx.fillText(entrance.name, screen.x + tileSize / 2, screen.y - 4);
  }
}

function npcWander(obj: OwWorldObjectData, nowMs: number): { x: number; y: number } {
  const phase = nowMs * 0.00035 + obj.x * 1.7 + obj.y * 0.9;
  return {
    x: obj.x + Math.sin(phase) * 0.35,
    y: obj.y + Math.cos(phase * 0.8) * 0.22,
  };
}

function renderWorldObject(ctx: CanvasRenderingContext2D, camera: Camera, obj: OwWorldObjectData, spriteCache: SpriteCache | null) {
  const tileSize = getEffectiveTileSize(camera);
  const screen = worldToScreen(camera, obj.x, obj.y);
  const facing = obj.type.startsWith('npc_')
    ? facingFromVelocity(Math.cos(obj.x + Date.now() * 0.0003), Math.sin(obj.y + Date.now() * 0.00025))
    : 0;

  if (spriteCache && (spriteCache.hasSprite(obj.type) || spriteCache.getEntry(obj.type))) {
    spriteCache.drawSprite(ctx, obj.type, screen.x, screen.y, tileSize, {
      action: obj.type.startsWith('npc_') ? 'walk' : 'idle',
      facing,
      distance: obj.type.startsWith('npc_') ? (Date.now() * 0.002 + obj.x) : undefined,
      anchor: 'feet',
    });
    return;
  }

  switch (obj.type) {
    case 'tree':
      ctx.fillStyle = '#2a5a1a';
      ctx.beginPath();
      ctx.arc(screen.x, screen.y - tileSize * 0.45, tileSize * 0.4, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillStyle = '#4a3a1a';
      ctx.fillRect(screen.x - tileSize * 0.08, screen.y - tileSize * 0.25, tileSize * 0.16, tileSize * 0.25);
      break;
    default:
      ctx.fillStyle = '#5a5a5a';
      ctx.fillRect(screen.x - tileSize * 0.3, screen.y - tileSize * 0.6, tileSize * 0.6, tileSize * 0.6);
      break;
  }
}

function renderPlayer(
  ctx: CanvasRenderingContext2D,
  camera: Camera,
  player: OwPlayerState,
  isLocal: boolean,
  spriteCache: SpriteCache | null,
  nowMs: number,
  facing: number
) {
  const screen = worldToScreen(camera, player.x, player.y);
  const tileSize = getEffectiveTileSize(camera);
  const radius = tileSize * 0.35;
  const moving = player.velocityX !== 0 || player.velocityY !== 0;
  const spriteName = playerSpriteName(player.figure);
  const dist = walkDistance(player.id, player.x, player.y);
  const attacking = isAttacking(player.id);
  const attackMs = attackElapsedMs(player.id);

  ctx.fillStyle = 'rgba(0, 0, 0, 0.3)';
  ctx.beginPath();
  ctx.ellipse(screen.x + 2, screen.y, radius * 0.55, radius * 0.22, 0, 0, Math.PI * 2);
  ctx.fill();

  const drawn = spriteCache?.drawSprite(
    ctx, spriteName, screen.x, screen.y, tileSize, {
      action: attacking ? 'attack' : (moving ? 'walk' : 'idle'),
      facing,
      distance: dist,
      attackElapsedMs: attacking ? attackMs : undefined,
      anchor: 'feet',
    }
  ) ?? false;

  if (!drawn) {
    ctx.fillStyle = isLocal ? '#c9a84c' : '#8b5fbf';
    ctx.beginPath();
    ctx.arc(screen.x, screen.y, radius, 0, Math.PI * 2);
    ctx.fill();
    ctx.strokeStyle = isLocal ? '#e8d080' : '#a87dd4';
    ctx.lineWidth = isLocal ? 2 : 1;
    ctx.stroke();
  }

  if (player.isPartyLeader) {
    ctx.fillStyle = '#ffd700';
    ctx.font = `${Math.max(8, tileSize * 0.35)}px sans-serif`;
    ctx.textAlign = 'center';
    ctx.fillText('★', screen.x, screen.y - radius - 2);
  }

  if (player.partyId) {
    ctx.strokeStyle = 'rgba(100, 200, 100, 0.5)';
    ctx.lineWidth = 1.5;
    ctx.beginPath();
    ctx.arc(screen.x, screen.y, radius + 3, 0, Math.PI * 2);
    ctx.stroke();
  }

  ctx.fillStyle = isLocal ? '#e8dcc8' : '#b8a0d4';
  ctx.font = `${Math.max(8, tileSize * 0.35)}px sans-serif`;
  ctx.textAlign = 'center';
  ctx.fillText(player.name, screen.x, screen.y + tileSize * 0.28);
}

function renderLootDrop(ctx: CanvasRenderingContext2D, camera: Camera, drop: LootDropState) {
  const screen = worldToScreen(camera, drop.x, drop.y);
  const tileSize = getEffectiveTileSize(camera);
  const size = tileSize * 0.3;

  // Rarity glow colors
  const RARITY_GLOW: Record<string, string> = {
    Common: '#9a9a9a',
    Uncommon: '#4a8c3f',
    Rare: '#3f6fcc',
    Epic: '#8b5fbf',
  };

  const glowColor = RARITY_GLOW[drop.rarity] || '#9a9a9a';

  // Pulsing bob animation
  const bob = Math.sin(Date.now() * 0.004 + drop.x * 3) * 1.5;

  // Glow
  ctx.shadowColor = glowColor;
  ctx.shadowBlur = 6;

  // Draw as a small square (item placeholder)
  ctx.fillStyle = glowColor;
  ctx.fillRect(
    screen.x - size / 2,
    screen.y - size / 2 + bob,
    size, size
  );

  // Inner highlight
  ctx.fillStyle = 'rgba(255, 255, 255, 0.3)';
  ctx.fillRect(
    screen.x - size / 4,
    screen.y - size / 4 + bob,
    size / 2, size / 2
  );

  ctx.shadowBlur = 0;
}

function renderEnemy(
  ctx: CanvasRenderingContext2D,
  camera: Camera,
  enemy: EnemyState,
  spriteCache: SpriteCache | null,
  nowMs: number
) {
  const screen = worldToScreen(camera, enemy.x, enemy.y);
  const tileSize = getEffectiveTileSize(camera);
  const radius = tileSize * 0.5;
  const spriteName = spriteCache?.getEntry(enemy.subType) ? enemy.subType : 'gronk';
  const moving = enemy.velocityX !== 0 || enemy.velocityY !== 0;
  const dist = walkDistance(enemy.id, enemy.x, enemy.y);
  const facing = facingFromMotion(enemy.id, enemy.velocityX, enemy.velocityY);
  syncAttackFromCooldown(enemy.id, enemy.attackCooldown);
  const attacking = isAttacking(enemy.id);
  const attackMs = attackElapsedMs(enemy.id);

  if (!enemy.isAlive) {
    ctx.globalAlpha = 0.4;
    ctx.fillStyle = '#2a1a1a';
    ctx.beginPath();
    ctx.ellipse(screen.x, screen.y + radius * 0.2, radius * 0.7, radius * 0.3, 0, 0, Math.PI * 2);
    ctx.fill();
    ctx.globalAlpha = 1;
    return;
  }

  ctx.fillStyle = 'rgba(0, 0, 0, 0.4)';
  ctx.beginPath();
  ctx.ellipse(screen.x + 1, screen.y, radius * 0.55, radius * 0.18, 0, 0, Math.PI * 2);
  ctx.fill();

  const drawn = spriteCache?.drawSprite(
    ctx, spriteName, screen.x, screen.y, tileSize, {
      action: attacking ? 'attack' : (moving ? 'walk' : 'idle'),
      facing,
      distance: dist,
      attackElapsedMs: attacking ? attackMs : undefined,
      anchor: 'feet',
    }
  ) ?? false;

  if (!drawn) {
    ctx.fillStyle = '#3a2a1a';
    ctx.beginPath();
    ctx.ellipse(screen.x, screen.y, radius * 0.7, radius, 0, 0, Math.PI * 2);
    ctx.fill();
    ctx.strokeStyle = '#2a1a0a';
    ctx.lineWidth = 1.5;
    ctx.stroke();
    const bobY = Math.sin(nowMs * 0.003 + enemy.x * 2) * 1.5;
    ctx.fillStyle = '#c08030';
    ctx.beginPath();
    ctx.arc(screen.x + radius * 0.15, screen.y - radius * 0.3 + bobY, tileSize * 0.06, 0, Math.PI * 2);
    ctx.fill();
  }

  if (enemy.taggedBy) {
    ctx.strokeStyle = `rgba(200, 50, 50, ${0.4 + Math.sin(nowMs * 0.005) * 0.2})`;
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.ellipse(screen.x, screen.y, radius * 0.8, radius * 1.1, 0, 0, Math.PI * 2);
    ctx.stroke();
  }

  if (enemy.health < enemy.maxHealth) {
    const barWidth = tileSize * 0.8;
    const barHeight = 3;
    const barX = screen.x - barWidth / 2;
    const barY = screen.y - radius - 6;
    const hpPct = enemy.health / enemy.maxHealth;

    ctx.fillStyle = 'rgba(0, 0, 0, 0.7)';
    ctx.fillRect(barX - 1, barY - 1, barWidth + 2, barHeight + 2);
    ctx.fillStyle = hpPct > 0.5 ? '#6a3030' : '#a02020';
    ctx.fillRect(barX, barY, barWidth * hpPct, barHeight);
  }
}

function renderProjectile(ctx: CanvasRenderingContext2D, camera: Camera, proj: ProjectileState) {
  const screen = worldToScreen(camera, proj.x, proj.y);
  const tileSize = getEffectiveTileSize(camera);

  // Different visual per ability type
  switch (proj.subType) {
    case 'ember_spray': {
      // Small orange-red circle with glow
      const radius = tileSize * 0.12;
      ctx.shadowColor = '#ff6020';
      ctx.shadowBlur = 4;
      ctx.fillStyle = '#ff8040';
      ctx.beginPath();
      ctx.arc(screen.x, screen.y, radius, 0, Math.PI * 2);
      ctx.fill();
      ctx.shadowBlur = 0;
      break;
    }
    case 'void_bolt': {
      // Larger purple circle with strong glow
      const radius = tileSize * 0.18;
      ctx.shadowColor = '#8040ff';
      ctx.shadowBlur = 8;
      ctx.fillStyle = '#a060ff';
      ctx.beginPath();
      ctx.arc(screen.x, screen.y, radius, 0, Math.PI * 2);
      ctx.fill();
      ctx.shadowBlur = 0;
      break;
    }
    default: {
      // Generic white dot
      const radius = tileSize * 0.1;
      ctx.fillStyle = '#e0e0e0';
      ctx.beginPath();
      ctx.arc(screen.x, screen.y, radius, 0, Math.PI * 2);
      ctx.fill();
      break;
    }
  }
}

function renderLandmarkLabels(
  ctx: CanvasRenderingContext2D, camera: Camera,
  landmarks: OwLandmarkData[], playerX: number, playerY: number
) {
  const tileSize = getEffectiveTileSize(camera);

  for (const landmark of landmarks) {
    // Only show labels within 30 tiles
    const dist = Math.sqrt((landmark.x - playerX) ** 2 + (landmark.y - playerY) ** 2);
    if (dist > 30) continue;

    const screen = worldToScreen(camera, landmark.x, landmark.y);
    const alpha = Math.max(0, 1 - dist / 30) * 0.7;

    ctx.fillStyle = `rgba(201, 168, 76, ${alpha})`;
    ctx.font = `italic ${Math.max(9, tileSize * 0.5)}px Georgia, serif`;
    ctx.textAlign = 'center';
    ctx.fillText(landmark.name, screen.x, screen.y - tileSize * 0.5);
  }
}

function renderVignette(ctx: CanvasRenderingContext2D, width: number, height: number) {
  const gradient = ctx.createRadialGradient(
    width / 2, height / 2, Math.min(width, height) * 0.35,
    width / 2, height / 2, Math.max(width, height) * 0.7
  );
  gradient.addColorStop(0, 'rgba(0, 0, 0, 0)');
  gradient.addColorStop(1, 'rgba(0, 0, 0, 0.3)');
  ctx.fillStyle = gradient;
  ctx.fillRect(0, 0, width, height);
}
