/**
 * Client-side prediction and server reconciliation.
 *
 * How it works:
 * 1. Client immediately applies movement locally (prediction)
 * 2. Client sends input to server with a sequence number
 * 3. Client stores each predicted input in a buffer
 * 4. When server confirms (sends back lastProcessedInput), client:
 *    - Removes all inputs up to that sequence from the buffer
 *    - Snaps to server position
 *    - Re-applies any unconfirmed inputs still in the buffer
 *
 * This gives instant response while maintaining server authority.
 */

import { GameMap, isWalkableF } from '../map';
import { InputState } from './input';

const PLAYER_SPEED = 5; // tiles per second (must match server)
const TICK_DURATION = 1 / 20; // 50ms per tick

export interface PredictedInput {
  sequenceNumber: number;
  moveX: number;
  moveY: number;
  timestamp: number;
}

export class ClientPrediction {
  private pendingInputs: PredictedInput[] = [];
  private sequenceNumber: number = 0;
  private predictedX: number = 0;
  private predictedY: number = 0;

  /**
   * Get the current predicted position.
   */
  get x(): number { return this.predictedX; }
  get y(): number { return this.predictedY; }

  /**
   * Set position (called when server sends initial state or hard reset).
   */
  setPosition(x: number, y: number): void {
    this.predictedX = x;
    this.predictedY = y;
  }

  /**
   * Apply an input locally (prediction) and return the input to send to server.
   * Returns null if there's no movement to send.
   */
  applyInput(inputState: InputState, map: GameMap | null): PredictedInput | null {
    if (inputState.moveX === 0 && inputState.moveY === 0) {
      return null;
    }

    this.sequenceNumber++;

    const input: PredictedInput = {
      sequenceNumber: this.sequenceNumber,
      moveX: inputState.moveX,
      moveY: inputState.moveY,
      timestamp: Date.now(),
    };

    // Apply movement prediction locally
    this.applyMovement(input, map);

    // Store in pending buffer for reconciliation
    this.pendingInputs.push(input);

    // Limit buffer size (shouldn't grow much if server is responsive)
    if (this.pendingInputs.length > 60) {
      this.pendingInputs.shift();
    }

    return input;
  }

  /**
   * Reconcile with server state.
   * Called when we receive a GameState message with lastProcessedInput.
   */
  reconcile(
    serverX: number,
    serverY: number,
    lastProcessedInput: number,
    map: GameMap | null
  ): void {
    // Remove all inputs that have been processed by the server
    this.pendingInputs = this.pendingInputs.filter(
      input => input.sequenceNumber > lastProcessedInput
    );

    // Start from server's authoritative position
    this.predictedX = serverX;
    this.predictedY = serverY;

    // Re-apply unconfirmed inputs
    for (const input of this.pendingInputs) {
      this.applyMovement(input, map);
    }
  }

  /**
   * Get the next sequence number (for the input message).
   */
  getSequenceNumber(): number {
    return this.sequenceNumber;
  }

  private applyMovement(input: PredictedInput, map: GameMap | null): void {
    const dx = input.moveX * PLAYER_SPEED * TICK_DURATION;
    const dy = input.moveY * PLAYER_SPEED * TICK_DURATION;

    let newX = this.predictedX + dx;
    let newY = this.predictedY + dy;

    // Collision detection against map
    if (map) {
      // Try full movement
      if (!isWalkableF(map, newX, newY)) {
        // Try X only
        if (isWalkableF(map, newX, this.predictedY)) {
          newY = this.predictedY;
        }
        // Try Y only
        else if (isWalkableF(map, this.predictedX, newY)) {
          newX = this.predictedX;
        }
        // Neither works — don't move
        else {
          newX = this.predictedX;
          newY = this.predictedY;
        }
      }
    }

    this.predictedX = newX;
    this.predictedY = newY;
  }
}
