/**
 * =============================================================================
 * useGameInput.ts — Input Capture, Prediction, and Server Sync Hook
 * =============================================================================
 *
 * WHY THIS HOOK EXISTS:
 * This hook bridges the gap between browser input events and the game server:
 *   - Captures keyboard/mouse state via InputHandler
 *   - Applies client-side prediction for instant movement feedback
 *   - Sends inputs to the server at 20Hz (matching server tick rate)
 *   - Provides reconciliation interface for server state corrections
 *
 * WHY 20Hz INPUT LOOP (not per-keypress):
 * Sending every keydown/keyup would flood the server with irregular messages.
 * Instead, we sample the input state at the server's tick rate (20Hz) and send
 * one consolidated message per tick: "here's what I'm doing right now."
 * This matches the server's processing cadence perfectly.
 *
 * LIFECYCLE:
 *   - When 'active' becomes true: attach keyboard/mouse listeners, start 20Hz loop
 *   - Each tick: read input state → predict locally → send to server
 *   - When server confirms: reconcile predicted position with authoritative state
 *   - When 'active' becomes false: detach listeners, stop loop (e.g., chat focused)
 * =============================================================================
 */
'use client';

import { useEffect, useRef, useCallback } from 'react';
import { InputHandler } from '@/lib/engine/input';
import { ClientPrediction, PredictedInput } from '@/lib/engine/prediction';
import { GameMap } from '@/lib/map';
import { GameMessage, MessageTypes, PlayerInputPayload } from '@/lib/messages';

export interface UseGameInputOptions {
  /** Send function from WebSocket */
  send: (msg: GameMessage) => void;
  /** Current game map for collision prediction */
  map: GameMap | null;
  /** Whether input should be active */
  active: boolean;
  /** Optional callback fired when the player fires (for visual effects). */
  onFire?: (x: number, y: number, angle: number, classType: string) => void;
}

export interface UseGameInputReturn {
  /** The input handler instance (for enabling/disabling) */
  inputHandler: InputHandler;
  /** The prediction engine instance */
  prediction: ClientPrediction;
  /** Get predicted position */
  getPredictedPosition: () => { x: number; y: number };
  /** Handle server reconciliation */
  reconcile: (serverX: number, serverY: number, lastProcessedInput: number) => void;
  /** Set initial position */
  setPosition: (x: number, y: number) => void;
}

/**
 * Hook that manages keyboard input, client prediction, and sending inputs to server.
 * Runs an input sampling loop at the server tick rate (20Hz) to send inputs.
 */
export function useGameInput({ send, map, active, onFire }: UseGameInputOptions): UseGameInputReturn {
  const inputHandlerRef = useRef<InputHandler>(new InputHandler());
  const predictionRef = useRef<ClientPrediction>(new ClientPrediction());
  const inputLoopRef = useRef<NodeJS.Timeout | null>(null);
  const mapRef = useRef<GameMap | null>(map);

  // Keep map ref current
  useEffect(() => {
    mapRef.current = map;
  }, [map]);

  // Attach/detach input handler
  useEffect(() => {
    if (active) {
      inputHandlerRef.current.attach();
    }
    return () => {
      inputHandlerRef.current.detach();
    };
  }, [active]);

  // Input sampling loop — runs at 20Hz (matching server tick rate)
  useEffect(() => {
    if (!active) {
      if (inputLoopRef.current) {
        clearInterval(inputLoopRef.current);
        inputLoopRef.current = null;
      }
      return;
    }

    inputLoopRef.current = setInterval(() => {
      const inputState = inputHandlerRef.current.getState();
      const prediction = predictionRef.current;

      // Apply prediction and get the input to send
      const predictedInput = prediction.applyInput(inputState, mapRef.current);

      if (predictedInput) {
        // Send input to server
        send({
          type: MessageTypes.PlayerInput,
          playerInput: {
            sequenceNumber: predictedInput.sequenceNumber,
            moveX: predictedInput.moveX,
            moveY: predictedInput.moveY,
            primaryFire: inputState.primaryFire,
            secondaryAbility: inputState.secondaryAbility,
            interact: inputState.interact,
            useMedKit: inputState.useMedKit,
            aimAngle: inputState.aimAngle,
            timestamp: predictedInput.timestamp,
          },
        } as GameMessage);
      } else if (inputState.primaryFire || inputState.secondaryAbility || inputState.interact || inputState.useMedKit) {
        // Send action-only input (no movement but has an action)
        send({
          type: MessageTypes.PlayerInput,
          playerInput: {
            sequenceNumber: prediction.getSequenceNumber(),
            moveX: 0,
            moveY: 0,
            primaryFire: inputState.primaryFire,
            secondaryAbility: inputState.secondaryAbility,
            interact: inputState.interact,
            useMedKit: inputState.useMedKit,
            aimAngle: inputState.aimAngle,
            timestamp: Date.now(),
          },
        } as GameMessage);
      }

      // Trigger visual effect callback when firing (for muzzle flash / slash arc)
      if (inputState.primaryFire && onFire) {
        const pos = prediction;
        onFire(pos.x, pos.y, inputState.aimAngle, '');
      }
    }, 50); // 20Hz

    return () => {
      if (inputLoopRef.current) {
        clearInterval(inputLoopRef.current);
        inputLoopRef.current = null;
      }
    };
  }, [active, send]);

  const getPredictedPosition = useCallback(() => {
    return {
      x: predictionRef.current.x,
      y: predictionRef.current.y,
    };
  }, []);

  const reconcile = useCallback((serverX: number, serverY: number, lastProcessedInput: number) => {
    predictionRef.current.reconcile(serverX, serverY, lastProcessedInput, mapRef.current);
  }, []);

  const setPosition = useCallback((x: number, y: number) => {
    predictionRef.current.setPosition(x, y);
  }, []);

  return {
    inputHandler: inputHandlerRef.current,
    prediction: predictionRef.current,
    getPredictedPosition,
    reconcile,
    setPosition,
  };
}
