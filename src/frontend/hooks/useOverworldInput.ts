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
import { OverworldGameMap, OwTileType, isOwWalkableF, getOwTile } from '@/lib/overworld-map';

const SEND_HZ = 20;
const SEND_INTERVAL_MS = 1000 / SEND_HZ;
const PLAYER_SPEED = 4.5; // tiles/second
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
  const velRef = useRef({ x: 0, y: 0 });
  const keysRef = useRef<Set<string>>(new Set());
  const sequenceRef = useRef(0);
  const rafRef = useRef<number>(0);
  const lastFrameRef = useRef(0);
  const sendAccRef = useRef(0);
  const interactRef = useRef(false);
  const lockedRef = useRef(false);
  const mapRef = useRef(map);
  const objectsRef = useRef(worldObjects);
  const sendRef = useRef(send);
  mapRef.current = map;
  objectsRef.current = worldObjects;
  sendRef.current = send;

  // Track pending inputs for reconciliation
  const pendingInputsRef = useRef<Array<{ seq: number; dx: number; dy: number }>>([]);

  const setInitialPosition = useCallback((x: number, y: number) => {
    posRef.current = { x, y };
    setPosition({ x, y });
    pendingInputsRef.current = [];
  }, []);

  const setMovementLocked = useCallback((locked: boolean) => {
    lockedRef.current = locked;
    if (locked) velRef.current = { x: 0, y: 0 };
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

  // Display movement on the paint loop; mesh still gets 20Hz samples.
  useEffect(() => {
    if (!active) {
      if (rafRef.current) cancelAnimationFrame(rafRef.current);
      rafRef.current = 0;
      lastFrameRef.current = 0;
      return;
    }

    const frame = (now: number) => {
      const prev = lastFrameRef.current || now;
      lastFrameRef.current = now;
      const dt = Math.min(0.05, (now - prev) / 1000);

      if (lockedRef.current) {
        if (velRef.current.x !== 0 || velRef.current.y !== 0) {
          velRef.current = { x: 0, y: 0 };
          setPosition({ x: posRef.current.x, y: posRef.current.y });
        }
        rafRef.current = requestAnimationFrame(frame);
        return;
      }

      const keys = keysRef.current;
      let moveX = 0;
      let moveY = 0;
      if (keys.has('w') || keys.has('arrowup')) moveY = -1;
      if (keys.has('s') || keys.has('arrowdown')) moveY = 1;
      if (keys.has('a') || keys.has('arrowleft')) moveX = -1;
      if (keys.has('d') || keys.has('arrowright')) moveX = 1;

      const liveMap = mapRef.current;
      if (liveMap && getOwTile(liveMap, Math.floor(posRef.current.x), Math.floor(posRef.current.y)) === OwTileType.Ladder) {
        moveX = 0;
      }

      if (moveX !== 0 && moveY !== 0) {
        const len = Math.sqrt(moveX * moveX + moveY * moveY);
        moveX /= len;
        moveY /= len;
      }

      const dx = moveX * PLAYER_SPEED * dt;
      const dy = moveY * PLAYER_SPEED * dt;
      const wasMoving = velRef.current.x !== 0 || velRef.current.y !== 0;
      velRef.current = { x: moveX * PLAYER_SPEED, y: moveY * PLAYER_SPEED };

      if (dx !== 0 || dy !== 0) {
        let newX = posRef.current.x;
        let newY = posRef.current.y;
        if (!liveMap || canMoveTo(liveMap, objectsRef.current, newX + dx, newY)) newX += dx;
        if (!liveMap || canMoveTo(liveMap, objectsRef.current, newX, newY + dy)) newY += dy;
        posRef.current = { x: newX, y: newY };
        setPosition({ x: newX, y: newY });
        pendingInputsRef.current.push({ seq: sequenceRef.current + 1, dx, dy });
        if (pendingInputsRef.current.length > 48) pendingInputsRef.current.shift();
      } else if (wasMoving) {
        setPosition({ x: posRef.current.x, y: posRef.current.y });
      }

      sendAccRef.current += dt * 1000;
      if (sendAccRef.current >= SEND_INTERVAL_MS) {
        sendAccRef.current = 0;
        if (moveX !== 0 || moveY !== 0 || interactRef.current) {
          const seq = ++sequenceRef.current;
          const input: OwPlayerInputPayload = {
            sequenceNumber: seq,
            moveX,
            moveY,
            interact: interactRef.current,
            timestamp: Date.now(),
          };
          interactRef.current = false;
          sendRef.current({
            type: OwMessageTypes.PlayerInput,
            playerInput: input,
          });
        } else {
          interactRef.current = false;
        }
      }

      rafRef.current = requestAnimationFrame(frame);
    };

    rafRef.current = requestAnimationFrame(frame);
    return () => {
      if (rafRef.current) cancelAnimationFrame(rafRef.current);
      rafRef.current = 0;
      lastFrameRef.current = 0;
    };
  }, [active]);

  return {
    position,
    velocityX: velRef.current.x,
    velocityY: velRef.current.y,
    setInitialPosition,
    setMovementLocked,
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
