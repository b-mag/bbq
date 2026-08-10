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
export function useGameInput({ send, map, active }: UseGameInputOptions): UseGameInputReturn {
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
            aimAngle: inputState.aimAngle,
            timestamp: predictedInput.timestamp,
          },
        } as GameMessage);
      } else if (inputState.primaryFire || inputState.secondaryAbility || inputState.interact) {
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
            aimAngle: inputState.aimAngle,
            timestamp: Date.now(),
          },
        } as GameMessage);
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
