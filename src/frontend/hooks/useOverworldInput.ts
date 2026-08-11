/**
 * =============================================================================
 * useOverworldInput.ts — Overworld Movement Input (No Combat)
 * =============================================================================
 *
 * Simplified input system for the overworld. Only handles movement (WASD/arrows)
 * and interact (E key). No combat, no abilities, no aim angle.
 * Sends input to the overworld server at 20Hz (matching server tick rate).
 * Includes client-side prediction for smooth movement.
 * =============================================================================
 */
'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { OverworldMessage, OwMessageTypes, OwPlayerInputPayload, OwWorldObjectData } from '@/lib/overworld-messages';
import { OverworldGameMap, isOwWalkableF } from '@/lib/overworld-map';

const TICK_RATE = 20;
const TICK_INTERVAL = 1000 / TICK_RATE;
const PLAYER_SPEED = 4.5; // tiles/second
const MOVE_PER_TICK = PLAYER_SPEED / TICK_RATE;
const PLAYER_RADIUS = 0.3;

interface UseOverworldInputOptions {
  send: (msg: OverworldMessage) => void;
  map: OverworldGameMap | null;
  active: boolean;
  worldObjects?: OwWorldObjectData[];
}

export function useOverworldInput(options: UseOverworldInputOptions) {
  const { send, map, active, worldObjects } = options;
  const [position, setPosition] = useState({ x: 0, y: 0 });
  const posRef = useRef({ x: 0, y: 0 });
  const keysRef = useRef<Set<string>>(new Set());
  const sequenceRef = useRef(0);
  const intervalRef = useRef<NodeJS.Timeout | null>(null);
  const interactRef = useRef(false);

  // Track pending inputs for reconciliation
  const pendingInputsRef = useRef<Array<{ seq: number; dx: number; dy: number }>>([]);

  const setInitialPosition = useCallback((x: number, y: number) => {
    posRef.current = { x, y };
    setPosition({ x, y });
  }, []);

  // Reconcile with server state
  const reconcile = useCallback((serverX: number, serverY: number, lastProcessedInput: number) => {
    // Remove confirmed inputs
    pendingInputsRef.current = pendingInputsRef.current.filter(i => i.seq > lastProcessedInput);

    // Start from server position and replay unconfirmed inputs
    let x = serverX;
    let y = serverY;
    for (const input of pendingInputsRef.current) {
      if (map && canMoveTo(map, worldObjects, x + input.dx, y)) {
        x += input.dx;
      }
      if (map && canMoveTo(map, worldObjects, x, y + input.dy)) {
        y += input.dy;
      }
    }
    posRef.current = { x, y };
    setPosition({ x, y });
  }, [map, worldObjects]);

  // Key handlers
  useEffect(() => {
    if (!active) return;

    const handleKeyDown = (e: KeyboardEvent) => {
      keysRef.current.add(e.key.toLowerCase());
      if (e.key.toLowerCase() === 'e') {
        interactRef.current = true;
      }
    };
    const handleKeyUp = (e: KeyboardEvent) => {
      keysRef.current.delete(e.key.toLowerCase());
    };

    window.addEventListener('keydown', handleKeyDown);
    window.addEventListener('keyup', handleKeyUp);
    return () => {
      window.removeEventListener('keydown', handleKeyDown);
      window.removeEventListener('keyup', handleKeyUp);
    };
  }, [active]);

  // 20Hz input loop
  useEffect(() => {
    if (!active) {
      if (intervalRef.current) {
        clearInterval(intervalRef.current);
        intervalRef.current = null;
      }
      return;
    }

    intervalRef.current = setInterval(() => {
      const keys = keysRef.current;
      let moveX = 0;
      let moveY = 0;

      if (keys.has('w') || keys.has('arrowup')) moveY = -1;
      if (keys.has('s') || keys.has('arrowdown')) moveY = 1;
      if (keys.has('a') || keys.has('arrowleft')) moveX = -1;
      if (keys.has('d') || keys.has('arrowright')) moveX = 1;

      // Normalize diagonal
      if (moveX !== 0 && moveY !== 0) {
        const len = Math.sqrt(moveX * moveX + moveY * moveY);
        moveX /= len;
        moveY /= len;
      }

      const seq = ++sequenceRef.current;

      // Client-side prediction
      const dx = moveX * MOVE_PER_TICK;
      const dy = moveY * MOVE_PER_TICK;

      if (dx !== 0 || dy !== 0) {
        let newX = posRef.current.x;
        let newY = posRef.current.y;

        if (map && canMoveTo(map, worldObjects, newX + dx, newY)) {
          newX += dx;
        }
        if (map && canMoveTo(map, worldObjects, newX, newY + dy)) {
          newY += dy;
        }

        posRef.current = { x: newX, y: newY };
        setPosition({ x: newX, y: newY });
        pendingInputsRef.current.push({ seq, dx, dy });
      }

      // Only send input to server when there's movement or interaction
      if (moveX !== 0 || moveY !== 0 || interactRef.current) {
        const input: OwPlayerInputPayload = {
          sequenceNumber: seq,
          moveX,
          moveY,
          interact: interactRef.current,
          timestamp: Date.now(),
        };
        interactRef.current = false;

        send({
          type: OwMessageTypes.PlayerInput,
          playerInput: input,
        });
      } else {
        interactRef.current = false;
      }
    }, TICK_INTERVAL);

    return () => {
      if (intervalRef.current) {
        clearInterval(intervalRef.current);
        intervalRef.current = null;
      }
    };
  }, [active, map, send]);

  return {
    position,
    setInitialPosition,
    reconcile,
  };
}

/**
 * Combined walkability check: tile-based + world object collision.
 */
function canMoveTo(
  map: OverworldGameMap,
  worldObjects: OwWorldObjectData[] | undefined,
  x: number,
  y: number
): boolean {
  // Check tile collision
  if (!isOwWalkableF(map, x, y, PLAYER_RADIUS)) return false;

  // Check world object collision
  if (worldObjects) {
    for (const obj of worldObjects) {
      if (!obj.collision) continue;
      const dx = x - obj.x;
      const dy = y - obj.y;
      const combinedRadius = PLAYER_RADIUS + obj.collisionRadius;
      if (dx * dx + dy * dy < combinedRadius * combinedRadius) {
        return false;
      }
    }
  }

  return true;
}
