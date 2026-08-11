/**
 * =============================================================================
 * prediction.ts — Client-Side Prediction and Server Reconciliation
 * =============================================================================
 *
 * WHY CLIENT-SIDE PREDICTION:
 * Without prediction, player movement would have a round-trip delay (50-200ms)
 * before appearing on screen. This feels extremely sluggish. Prediction applies
 * movement LOCALLY and immediately, making the game feel responsive regardless
 * of network latency.
 *
 * HOW RECONCILIATION WORKS:
 *   1. Client sends input to server with a sequence number
 *   2. Client immediately applies that movement locally (prediction)
 *   3. Client stores the input in a pending buffer
 *   4. Server processes the input and sends back state + lastProcessedInput
 *   5. Client receives server confirmation:
 *      a. Removes all inputs with sequence <= lastProcessedInput from buffer
 *      b. Snaps position to server's authoritative position
 *      c. Re-applies all remaining unconfirmed inputs from the buffer
 *   6. If server and client agree, re-applying gives the same position = no jitter
 *   7. If they disagree (e.g., server blocked movement), client snaps to correct pos
 *
 * WHY RE-APPLY UNCONFIRMED INPUTS:
 * Between sending an input and receiving confirmation, the client may have sent
 * 2-4 more inputs (at 20Hz with 100ms RTT). After snapping to server position,
 * those unconfirmed inputs need to be replayed so the client doesn't "jump back"
 * to the confirmed position and then "jump forward" again.
 *
 * SPEED CONSTANTS:
 * PLAYER_SPEED and TICK_DURATION must match the server exactly (5 tiles/sec, 50ms/tick).
 * Any mismatch causes prediction drift that reconciliation must constantly correct,
 * resulting in visible jitter.
 * =============================================================================
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
