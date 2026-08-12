/**
 * =============================================================================
 * useOverworldCombat.ts — Combat Input Hook (Aim + Click to Attack)
 * =============================================================================
 *
 * Handles combat input for the overworld:
 *   - Tracks mouse position continuously for aim direction
 *   - Left-click (on empty space) = fire primary ability toward cursor
 *   - Right-click = fire secondary ability toward cursor
 *   - Disables browser context menu on the game canvas
 *   - Throttles actions to 100ms minimum between sends
 *
 * WHY SEPARATE HOOK: Combat input is complex enough to warrant its own hook.
 * It needs canvas reference for coordinate conversion, throttling logic,
 * and integration with the camera system for world-space aim calculation.
 * =============================================================================
 */
'use client';

import { useRef, useCallback, useEffect } from 'react';

interface UseCombatOptions {
  /** Canvas element ref for coordinate conversion */
  canvasRef: React.RefObject<HTMLCanvasElement | null>;
  /** Local player position in world coordinates */
  playerX: number;
  playerY: number;
  /** Camera state for screen-to-world conversion */
  cameraX: number;
  cameraY: number;
  cameraZoom: number;
  baseTileSize: number;
  /** Whether combat input is active (disabled during chat, menus, etc.) */
  active: boolean;
  /** Callback when a player is clicked (for inspect, not attack) */
  onPlayerClick?: (playerId: string) => void;
  /** All players for click-detection (to distinguish attack vs inspect) */
  players: { id: string; x: number; y: number }[];
  /** Local player ID (to exclude from click detection) */
  localPlayerId: string | null;
}

/**
 * Hook that manages combat input: aim tracking, click-to-attack, and right-click secondary.
 * Returns mouse world position for aim indicator rendering.
 */
export function useOverworldCombat({
  canvasRef, playerX, playerY,
  cameraX, cameraY, cameraZoom, baseTileSize,
  active, onPlayerClick, players, localPlayerId,
}: UseCombatOptions) {
  const lastActionTime = useRef(0);
  const mouseWorldRef = useRef({ x: 0, y: 0 });

  // Convert screen coordinates to world coordinates
  const screenToWorld = useCallback((screenX: number, screenY: number) => {
    const canvas = canvasRef.current;
    if (!canvas) return { x: 0, y: 0 };

    const tileSize = baseTileSize * cameraZoom;
    const centerX = canvas.width / 2;
    const centerY = canvas.height / 2;

    const worldX = cameraX + (screenX - centerX) / tileSize;
    const worldY = cameraY + (screenY - centerY) / tileSize;
    return { x: worldX, y: worldY };
  }, [canvasRef, cameraX, cameraY, cameraZoom, baseTileSize]);

  // Calculate aim angle from player to mouse world position
  const getAimAngle = useCallback(() => {
    const dx = mouseWorldRef.current.x - playerX;
    const dy = mouseWorldRef.current.y - playerY;
    return Math.atan2(dy, dx);
  }, [playerX, playerY]);

  // Send combat action to server
  const sendCombatAction = useCallback(async (abilitySlot: 'primary' | 'secondary') => {
    const now = Date.now();
    if (now - lastActionTime.current < 100) return; // Throttle to 100ms
    lastActionTime.current = now;

    const aimAngle = getAimAngle();

    try {
      await fetch('/api/gameplay/combat-action', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ abilitySlot, aimAngle }),
      });
    } catch {
      // Silently fail
    }
  }, [getAimAngle]);

  // Check if click is near another player (for inspect, not attack)
  const isPlayerClick = useCallback((worldX: number, worldY: number): string | null => {
    for (const player of players) {
      if (player.id === localPlayerId) continue;
      const dist = Math.sqrt((worldX - player.x) ** 2 + (worldY - player.y) ** 2);
      if (dist < 1.0) return player.id;
    }
    return null;
  }, [players, localPlayerId]);

  // Set up event listeners on canvas
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    // Track mouse position for aim
    const handleMouseMove = (e: MouseEvent) => {
      const rect = canvas.getBoundingClientRect();
      const screenX = e.clientX - rect.left;
      const screenY = e.clientY - rect.top;
      mouseWorldRef.current = screenToWorld(screenX, screenY);
    };

    // Disable context menu (right-click = secondary ability)
    const handleContextMenu = (e: Event) => {
      e.preventDefault();
    };

    // Left click = primary attack (unless clicking on a player)
    const handleMouseDown = (e: MouseEvent) => {
      if (!active) return;

      const rect = canvas.getBoundingClientRect();
      const screenX = e.clientX - rect.left;
      const screenY = e.clientY - rect.top;
      const world = screenToWorld(screenX, screenY);

      if (e.button === 0) {
        // Left click — check if it's a player click first
        const clickedPlayerId = isPlayerClick(world.x, world.y);
        if (clickedPlayerId && onPlayerClick) {
          onPlayerClick(clickedPlayerId);
          return;
        }
        // Not a player — fire primary ability
        sendCombatAction('primary');
      } else if (e.button === 2) {
        // Right click — fire secondary ability
        sendCombatAction('secondary');
      }
    };

    canvas.addEventListener('mousemove', handleMouseMove);
    canvas.addEventListener('contextmenu', handleContextMenu);
    canvas.addEventListener('mousedown', handleMouseDown);

    return () => {
      canvas.removeEventListener('mousemove', handleMouseMove);
      canvas.removeEventListener('contextmenu', handleContextMenu);
      canvas.removeEventListener('mousedown', handleMouseDown);
    };
  }, [canvasRef, active, screenToWorld, sendCombatAction, isPlayerClick, onPlayerClick]);

  return {
    mouseWorld: mouseWorldRef.current,
    aimAngle: getAimAngle(),
  };
}
