'use client';

import { useEffect, useRef, useCallback } from 'react';
import { Camera, createCamera, cameraFollow } from '@/lib/engine/camera';
import { renderFrame } from '@/lib/engine/renderer';
import { EntityInterpolator } from '@/lib/engine/interpolation';
import { GameMap } from '@/lib/map';
import { EntityState } from '@/lib/messages';

export interface GameCanvasProps {
  /** The decoded game map to render */
  map: GameMap | null;
  /** Current entities from server (updated via WebSocket) */
  entities: EntityState[];
  /** The local player's entity ID (for camera follow) */
  localPlayerId: string | null;
  /** Canvas width in pixels */
  width?: number;
  /** Canvas height in pixels */
  height?: number;
  /** Tile size in pixels */
  tileSize?: number;
  /** Screen shake amount (from damage, etc.) */
  screenShake?: { x: number; y: number };
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
}: GameCanvasProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const cameraRef = useRef<Camera>(createCamera(width, height, tileSize));
  const interpolatorRef = useRef<EntityInterpolator>(new EntityInterpolator(20));
  const animFrameRef = useRef<number>(0);
  const entitiesRef = useRef<EntityState[]>(entities);

  // Update entities ref when new data arrives from server
  useEffect(() => {
    entitiesRef.current = entities;
    interpolatorRef.current.updateFromServer(entities);
  }, [entities]);

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

    // Follow the local player
    if (localPlayerId) {
      const localEntity = interpolator.getEntity(`player_${localPlayerId}`);
      if (localEntity) {
        cameraFollow(camera, localEntity.x, localEntity.y, 0.12);
      }
    }

    // Render the frame
    renderFrame(ctx, camera, map, interpolatedEntities, localPlayerId, screenShake);

    // Schedule next frame
    animFrameRef.current = requestAnimationFrame(render);
  }, [map, localPlayerId, screenShake]);

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
      }}
    />
  );
}
