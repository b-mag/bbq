/**
 * =============================================================================
 * OverworldCanvas.tsx — Canvas Renderer for the Persistent Overworld
 * =============================================================================
 *
 * Renders the overworld map and all connected players. Reuses the existing
 * camera system for zoom/pan. Players are shown as colored circles.
 * World objects, landmarks, and dungeon entrances are rendered as distinct visuals.
 * =============================================================================
 */
'use client';

import { useEffect, useRef, useCallback } from 'react';
import { Camera, createCamera, cameraFollow, worldToScreen, screenToWorld, getVisibleBounds, getEffectiveTileSize } from '@/lib/engine/camera';
import { OverworldGameMap, OwTileType, OW_TILE_COLORS, getOwTile } from '@/lib/overworld-map';
import { OwPlayerState, OwDungeonEntranceData, OwWorldObjectData, OwLandmarkData } from '@/lib/overworld-messages';
import { SpriteCache, initSprites } from '@/lib/engine/sprites';

interface OverworldCanvasProps {
  map: OverworldGameMap | null;
  players: OwPlayerState[];
  localPlayerId: string | null;
  dungeonEntrances: OwDungeonEntranceData[];
  worldObjects: OwWorldObjectData[];
  landmarks: OwLandmarkData[];
  width: number;
  height: number;
  onPlayerClick?: (playerId: string) => void;
}

export default function OverworldCanvas({
  map, players, localPlayerId, dungeonEntrances, worldObjects, landmarks, width, height, onPlayerClick
}: OverworldCanvasProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const cameraRef = useRef<Camera>(createCamera(width, height, 16));
  const animFrameRef = useRef<number>(0);
  const spriteCacheRef = useRef<SpriteCache | null>(null);
  const cameraInitializedRef = useRef(false);

  // Load sprites on mount
  useEffect(() => {
    initSprites().then(cache => {
      spriteCacheRef.current = cache;
    });
  }, []);

  // Render loop
  const render = useCallback(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const camera = cameraRef.current;

    // Follow local player
    const localPlayer = players.find(p => p.id === localPlayerId);
    if (localPlayer) {
      if (!cameraInitializedRef.current && (localPlayer.x !== 0 || localPlayer.y !== 0)) {
        // SNAP camera to player on first valid frame (no lerp)
        camera.x = localPlayer.x;
        camera.y = localPlayer.y;
        cameraInitializedRef.current = true;
      } else {
        // Smooth follow after initial snap
        cameraFollow(camera, localPlayer.x, localPlayer.y, 0.12);
      }
    }

    // Clear
    ctx.fillStyle = '#0d0f07';
    ctx.fillRect(0, 0, width, height);

    // Render map tiles
    if (map) {
      renderMapTiles(ctx, camera, map);
    }

    // Render dungeon entrances
    renderDungeonEntrances(ctx, camera, dungeonEntrances);

    // Render world objects
    renderWorldObjects(ctx, camera, worldObjects, spriteCacheRef.current);

    // Render players (Y-sorted for depth)
    const sortedPlayers = [...players].sort((a, b) => a.y - b.y);
    for (const player of sortedPlayers) {
      if (player.status === 'in_dungeon') continue;
      renderPlayer(ctx, camera, player, player.id === localPlayerId);
    }

    // Render landmark labels (only nearby ones)
    if (localPlayer) {
      renderLandmarkLabels(ctx, camera, landmarks, localPlayer.x, localPlayer.y);
    }

    // Vignette
    renderVignette(ctx, width, height);

    animFrameRef.current = requestAnimationFrame(render);
  }, [map, players, localPlayerId, dungeonEntrances, worldObjects, landmarks, width, height]);

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
      camera.zoom = Math.max(0.5, Math.min(3.0, camera.zoom + delta));
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
    };
    canvas.addEventListener('click', handleClick);
    return () => canvas.removeEventListener('click', handleClick);
  }, [players, localPlayerId, onPlayerClick]);

  return (
    <canvas
      ref={canvasRef}
      width={width}
      height={height}
      style={{ display: 'block', background: '#0d0f07' }}
    />
  );
}

// =============================================================================
// Rendering functions
// =============================================================================

function renderMapTiles(ctx: CanvasRenderingContext2D, camera: Camera, map: OverworldGameMap) {
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

      ctx.fillStyle = OW_TILE_COLORS[tile] || OW_TILE_COLORS[OwTileType.Grass];
      ctx.fillRect(Math.floor(screen.x), Math.floor(screen.y), tileSize + 1, tileSize + 1);

      // Subtle grid for paths/cobblestone
      if (tile === OwTileType.Path || tile === OwTileType.Cobblestone) {
        ctx.strokeStyle = 'rgba(0, 0, 0, 0.12)';
        ctx.lineWidth = 0.5;
        ctx.strokeRect(Math.floor(screen.x), Math.floor(screen.y), tileSize, tileSize);
      }

      // Water shimmer
      if (tile === OwTileType.DeepWater || tile === OwTileType.ShallowWater) {
        const shimmer = Math.sin(Date.now() * 0.0008 + x * 0.4 + y * 0.3) * 0.08;
        ctx.fillStyle = `rgba(100, 160, 200, ${0.03 + shimmer})`;
        ctx.fillRect(Math.floor(screen.x), Math.floor(screen.y), tileSize + 1, tileSize + 1);
      }

      // Mist animation
      if (tile === OwTileType.Mist) {
        const mistAlpha = 0.1 + Math.sin(Date.now() * 0.001 + x * 0.2 + y * 0.3) * 0.05;
        ctx.fillStyle = `rgba(180, 200, 210, ${mistAlpha})`;
        ctx.fillRect(Math.floor(screen.x), Math.floor(screen.y), tileSize + 1, tileSize + 1);
      }

      // Mountain depth shading
      if (tile === OwTileType.Mountain) {
        const shade = Math.sin(x * 0.3 + y * 0.5) * 0.05;
        ctx.fillStyle = `rgba(0, 0, 0, ${0.1 + shade})`;
        ctx.fillRect(Math.floor(screen.x), Math.floor(screen.y), tileSize + 1, tileSize + 1);
      }
    }
  }
}

function renderDungeonEntrances(ctx: CanvasRenderingContext2D, camera: Camera, entrances: OwDungeonEntranceData[]) {
  const tileSize = getEffectiveTileSize(camera);

  for (const entrance of entrances) {
    const screen = worldToScreen(camera, entrance.x, entrance.y);
    const size = tileSize * 1.2;

    // Pulsing glow
    const pulse = 0.5 + Math.sin(Date.now() * 0.003) * 0.3;

    // Dark entrance portal
    ctx.fillStyle = `rgba(60, 20, 20, ${0.8 + pulse * 0.2})`;
    ctx.beginPath();
    ctx.arc(screen.x + tileSize / 2, screen.y + tileSize / 2, size / 2, 0, Math.PI * 2);
    ctx.fill();

    // Glow ring
    ctx.strokeStyle = `rgba(200, 100, 50, ${pulse * 0.6})`;
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.arc(screen.x + tileSize / 2, screen.y + tileSize / 2, size / 2 + 2, 0, Math.PI * 2);
    ctx.stroke();

    // Label
    ctx.fillStyle = `rgba(200, 168, 76, ${0.7 + pulse * 0.3})`;
    ctx.font = `${Math.max(8, tileSize * 0.4)}px Georgia, serif`;
    ctx.textAlign = 'center';
    ctx.fillText(entrance.name, screen.x + tileSize / 2, screen.y - 4);
  }
}

function renderWorldObjects(ctx: CanvasRenderingContext2D, camera: Camera, objects: OwWorldObjectData[], spriteCache: SpriteCache | null) {
  const tileSize = getEffectiveTileSize(camera);
  const tick = Date.now(); // Use wall clock for animation timing

  for (const obj of objects) {
    const screen = worldToScreen(camera, obj.x, obj.y);

    // Try sprite rendering first
    if (spriteCache && spriteCache.hasSprite(obj.type)) {
      spriteCache.drawSprite(ctx, obj.type, screen.x + tileSize / 2, screen.y + tileSize / 2, tileSize, tick);
      continue;
    }

    // Try placeholder from manifest (correct size even without PNG)
    if (spriteCache && spriteCache.getEntry(obj.type)) {
      spriteCache.drawSprite(ctx, obj.type, screen.x + tileSize / 2, screen.y + tileSize / 2, tileSize, tick);
      continue;
    }

    // Fallback: hardcoded shape rendering (legacy)
    switch (obj.type) {
      case 'tree':
        ctx.fillStyle = '#2a5a1a';
        ctx.beginPath();
        ctx.arc(screen.x + tileSize / 2, screen.y + tileSize * 0.3, tileSize * 0.4, 0, Math.PI * 2);
        ctx.fill();
        ctx.fillStyle = '#4a3a1a';
        ctx.fillRect(screen.x + tileSize * 0.4, screen.y + tileSize * 0.5, tileSize * 0.2, tileSize * 0.4);
        break;

      case 'ruined_pillar':
        ctx.fillStyle = '#6a6a60';
        ctx.fillRect(screen.x + tileSize * 0.3, screen.y + tileSize * 0.1, tileSize * 0.4, tileSize * 0.8);
        ctx.strokeStyle = '#4a4a40';
        ctx.lineWidth = 1;
        ctx.strokeRect(screen.x + tileSize * 0.3, screen.y + tileSize * 0.1, tileSize * 0.4, tileSize * 0.8);
        break;

      case 'fishing_boat':
        ctx.fillStyle = '#6a5030';
        ctx.beginPath();
        ctx.ellipse(screen.x + tileSize / 2, screen.y + tileSize / 2, tileSize * 0.4, tileSize * 0.2, 0, 0, Math.PI * 2);
        ctx.fill();
        break;

      case 'signpost':
        ctx.fillStyle = '#5a4020';
        ctx.fillRect(screen.x + tileSize * 0.45, screen.y + tileSize * 0.3, tileSize * 0.1, tileSize * 0.5);
        ctx.fillStyle = '#7a6040';
        ctx.fillRect(screen.x + tileSize * 0.2, screen.y + tileSize * 0.2, tileSize * 0.6, tileSize * 0.25);
        break;

      default:
        ctx.fillStyle = '#5a5a5a';
        ctx.fillRect(screen.x + tileSize * 0.2, screen.y + tileSize * 0.2, tileSize * 0.6, tileSize * 0.6);
        break;
    }
  }
}

function renderPlayer(ctx: CanvasRenderingContext2D, camera: Camera, player: OwPlayerState, isLocal: boolean) {
  const screen = worldToScreen(camera, player.x, player.y);
  const tileSize = getEffectiveTileSize(camera);
  const radius = tileSize * 0.35;

  // Shadow
  ctx.fillStyle = 'rgba(0, 0, 0, 0.3)';
  ctx.beginPath();
  ctx.ellipse(screen.x + 2, screen.y + radius * 0.5, radius * 0.6, radius * 0.25, 0, 0, Math.PI * 2);
  ctx.fill();

  // Body
  ctx.fillStyle = isLocal ? '#c9a84c' : '#8a7a5a';
  ctx.beginPath();
  ctx.arc(screen.x, screen.y, radius, 0, Math.PI * 2);
  ctx.fill();

  // Border
  ctx.strokeStyle = isLocal ? '#e8d080' : '#6a6050';
  ctx.lineWidth = isLocal ? 2 : 1;
  ctx.stroke();

  // Party leader crown indicator
  if (player.isPartyLeader) {
    ctx.fillStyle = '#ffd700';
    ctx.font = `${Math.max(8, tileSize * 0.35)}px sans-serif`;
    ctx.textAlign = 'center';
    ctx.fillText('★', screen.x, screen.y - radius - 2);
  }

  // Party ring
  if (player.partyId) {
    ctx.strokeStyle = 'rgba(100, 200, 100, 0.5)';
    ctx.lineWidth = 1.5;
    ctx.beginPath();
    ctx.arc(screen.x, screen.y, radius + 3, 0, Math.PI * 2);
    ctx.stroke();
  }

  // Name label
  ctx.fillStyle = isLocal ? '#e8dcc8' : '#9a9080';
  ctx.font = `${Math.max(8, tileSize * 0.35)}px sans-serif`;
  ctx.textAlign = 'center';
  ctx.fillText(player.name, screen.x, screen.y + radius + tileSize * 0.4);
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
