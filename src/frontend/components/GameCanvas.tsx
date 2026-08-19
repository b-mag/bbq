/**
 * =============================================================================
 * GameCanvas.tsx — Main Game Rendering Surface
 * =============================================================================
 *
 * WHY CANVAS (not DOM/SVG):
 * With 2000+ tiles and ~50 entities updating at 60fps, DOM manipulation would
 * thrash the browser's layout engine. Canvas 2D provides direct pixel control
 * with a single composited layer — perfect for game rendering.
 *
 * WHY requestAnimationFrame (not setInterval):
 * rAF syncs to the display's refresh rate (typically 60Hz), prevents rendering
 * when the tab is hidden (saves battery), and provides smooth frame timing.
 * The render loop runs independently of the server tick rate (20Hz) — we use
 * entity interpolation to smoothly animate between server updates.
 *
 * ARCHITECTURE:
 * This component owns the render loop but doesn't own the game state.
 * Entities and map data flow in as props from the parent (page.tsx).
 * The EntityInterpolator smooths entity positions between server updates.
 * The Camera follows the local player with lerp smoothing.
 * =============================================================================
 */
'use client';

import { useEffect, useRef, useCallback } from 'react';
import { Camera, createCamera, cameraFollow, cameraZoom, ZOOM_STEP, worldToScreen } from '@/lib/engine/camera';
import { renderFrame } from '@/lib/engine/renderer';
import { EntityInterpolator } from '@/lib/engine/interpolation';
import { VisualEffectsSystem } from '@/lib/engine/effects';
import { GameMap } from '@/lib/map';
import { EntityState } from '@/lib/messages';
import { SpriteCache, initSprites } from '@/lib/engine/sprites';
import { TileAtlas, initTilesets } from '@/lib/engine/tilesets';

export interface GameCanvasProps {
  /** The decoded game map to render */
  map: GameMap | null;
  /** Current entities from server (updated via WebSocket) */
  entities: EntityState[];
  /** The local player's entity ID (for camera follow) */
  localPlayerId: string | null;
  /**
   * If set, camera follows this entity instead of the local player (spectate mode).
   * When null, camera follows the local player normally.
   */
  spectateTargetId?: string | null;
  /** Canvas width in pixels */
  width?: number;
  /** Canvas height in pixels */
  height?: number;
  /** Tile size in pixels */
  tileSize?: number;
  /** Screen shake amount (from damage, etc.) */
  screenShake?: { x: number; y: number };
  /** Callback fired when canvas element mounts (for input handler binding). */
  onCanvasReady?: (canvas: HTMLCanvasElement | null) => void;
  /** Callback fired with the effects system instance for external effect triggers. */
  onEffectsReady?: (effects: VisualEffectsSystem) => void;
  /** Input handler reference — used to update aim angle based on player screen position. */
  inputHandler?: { updateAimAngle: (playerScreenX: number, playerScreenY: number) => void } | null;
  /** Cosmetic body used for the local player sprite. */
  localFigure?: string;
  /** Canvas cursor CSS (from settings). */
  cursor?: string;
}

/**
 * The main game canvas component.
 * Renders the tile map, entities with interpolation, and visual effects.
 * Uses requestAnimationFrame for 60fps rendering independent of server tick rate.
 */
export default function GameCanvas({
  map,
  entities,
  localPlayerId,
  width = 800,
  height = 600,
  tileSize = 24,
  screenShake = { x: 0, y: 0 },
  onCanvasReady,
  onEffectsReady,
  spectateTargetId,
  inputHandler,
  localFigure,
  cursor = 'crosshair',
}: GameCanvasProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const cameraRef = useRef<Camera>(createCamera(width, height, tileSize));
  const interpolatorRef = useRef<EntityInterpolator>(new EntityInterpolator(20));
  const effectsRef = useRef<VisualEffectsSystem>(new VisualEffectsSystem());
  const spriteCacheRef = useRef<SpriteCache | null>(null);
  const tileAtlasRef = useRef<TileAtlas | null>(null);
  const animFrameRef = useRef<number>(0);
  const entitiesRef = useRef<EntityState[]>(entities);

  useEffect(() => {
    initSprites().then(cache => { spriteCacheRef.current = cache; });
    initTilesets().then(atlas => { tileAtlasRef.current = atlas; });
  }, []);

  // Update entities ref when new data arrives from server
  useEffect(() => {
    entitiesRef.current = entities;
    interpolatorRef.current.updateFromServer(entities);
  }, [entities]);

  // Notify parent when canvas element is available (for input handler binding)
  useEffect(() => {
    if (onCanvasReady) {
      onCanvasReady(canvasRef.current);
    }
    return () => {
      if (onCanvasReady) {
        onCanvasReady(null);
      }
    };
  }, [onCanvasReady]);

  // Expose the visual effects system to parent for triggering effects from game events
  useEffect(() => {
    if (onEffectsReady) {
      onEffectsReady(effectsRef.current);
    }
  }, [onEffectsReady]);

  // Handle mouse scroll wheel for zoom control
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const handleWheel = (e: WheelEvent) => {
      // Prevent page scrolling when cursor is over the canvas
      e.preventDefault();
      // deltaY < 0 = scroll up = zoom in (positive delta)
      // deltaY > 0 = scroll down = zoom out (negative delta)
      const zoomDelta = e.deltaY < 0 ? ZOOM_STEP : -ZOOM_STEP;
      cameraZoom(cameraRef.current, zoomDelta);
    };

    // Use passive: false so we can call preventDefault() to stop page scroll
    canvas.addEventListener('wheel', handleWheel, { passive: false });
    const blockMenu = (e: Event) => e.preventDefault();
    canvas.addEventListener('contextmenu', blockMenu);
    return () => {
      canvas.removeEventListener('wheel', handleWheel);
      canvas.removeEventListener('contextmenu', blockMenu);
    };
  }, []);

  // Update camera dimensions if viewport changes
  useEffect(() => {
    cameraRef.current.viewportWidth = width;
    cameraRef.current.viewportHeight = height;
    cameraRef.current.tileSize = tileSize;
  }, [width, height, tileSize]);

  // Main render loop
  const render = useCallback(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const camera = cameraRef.current;
    const interpolator = interpolatorRef.current;

    // Get interpolated entity positions
    const interpolatedEntities = interpolator.interpolate();

    // Follow the local player (or spectate target if spectating)
    const followId = spectateTargetId || (localPlayerId ? `player_${localPlayerId}` : null);
    if (followId) {
      const followEntity = interpolator.getEntity(followId);
      if (followEntity) {
        cameraFollow(camera, followEntity.x, followEntity.y, 0.12);
      }
    }

    // Update aim angle: tell the input handler where the player is on screen
    // so it can calculate the angle from mouse cursor to player position.
    if (inputHandler && localPlayerId) {
      const localEntity = interpolator.getEntity(`player_${localPlayerId}`);
      if (localEntity) {
        const playerScreen = worldToScreen(camera, localEntity.x, localEntity.y);
        inputHandler.updateAimAngle(playerScreen.x, playerScreen.y);
      }
    }

    // Render the frame (including active visual effects)
    const activeEffects = effectsRef.current.getActiveEffects();
    renderFrame(
      ctx, camera, map, interpolatedEntities, localPlayerId, screenShake, activeEffects,
      spriteCacheRef.current, tileAtlasRef.current, localFigure
    );

    // Schedule next frame
    animFrameRef.current = requestAnimationFrame(render);
  }, [map, localPlayerId, screenShake, spectateTargetId, inputHandler, localFigure]);

  // Start/stop render loop
  useEffect(() => {
    animFrameRef.current = requestAnimationFrame(render);
    return () => {
      if (animFrameRef.current) {
        cancelAnimationFrame(animFrameRef.current);
      }
    };
  }, [render]);

  return (
    <canvas
      ref={canvasRef}
      width={width}
      height={height}
      style={{
        display: 'block',
        background: '#0d0a07',
        imageRendering: 'pixelated',
        cursor,
      }}
    />
  );
}
