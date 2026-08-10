/**
 * Keyboard input handler for WASD movement and abilities.
 * Tracks which keys are currently held and converts to movement vectors.
 */

export interface InputState {
  // Movement
  moveX: number; // -1 (left) to 1 (right)
  moveY: number; // -1 (up) to 1 (down)
  // Actions
  primaryFire: boolean;
  secondaryAbility: boolean;
  interact: boolean;
  // Aim direction (angle in radians, from mouse position)
  aimAngle: number;
}

export class InputHandler {
  private keys: Set<string> = new Set();
  private mouseX: number = 0;
  private mouseY: number = 0;
  private mouseDown: boolean = false;
  private _aimAngle: number = 0;
  private _enabled: boolean = true;

  // Callbacks
  private onKeyDown?: (key: string) => void;

  constructor(canvas?: HTMLElement) {
    this.handleKeyDown = this.handleKeyDown.bind(this);
    this.handleKeyUp = this.handleKeyUp.bind(this);
    this.handleMouseMove = this.handleMouseMove.bind(this);
    this.handleMouseDown = this.handleMouseDown.bind(this);
    this.handleMouseUp = this.handleMouseUp.bind(this);
    this.handleBlur = this.handleBlur.bind(this);
  }

  /**
   * Attach event listeners to the window.
   */
  attach(): void {
    window.addEventListener('keydown', this.handleKeyDown);
    window.addEventListener('keyup', this.handleKeyUp);
    window.addEventListener('mousemove', this.handleMouseMove);
    window.addEventListener('mousedown', this.handleMouseDown);
    window.addEventListener('mouseup', this.handleMouseUp);
    window.addEventListener('blur', this.handleBlur);
  }

  /**
   * Remove event listeners.
   */
  detach(): void {
    window.removeEventListener('keydown', this.handleKeyDown);
    window.removeEventListener('keyup', this.handleKeyUp);
    window.removeEventListener('mousemove', this.handleMouseMove);
    window.removeEventListener('mousedown', this.handleMouseDown);
    window.removeEventListener('mouseup', this.handleMouseUp);
    window.removeEventListener('blur', this.handleBlur);
  }

  /**
   * Enable/disable input processing (e.g., disable when typing in chat).
   */
  set enabled(value: boolean) {
    this._enabled = value;
    if (!value) {
      this.keys.clear();
      this.mouseDown = false;
    }
  }

  get enabled(): boolean {
    return this._enabled;
  }

  /**
   * Set a callback for key down events (for non-movement keys like abilities).
   */
  setKeyDownCallback(cb: (key: string) => void): void {
    this.onKeyDown = cb;
  }

  /**
   * Get the current input state as a snapshot.
   */
  getState(): InputState {
    let moveX = 0;
    let moveY = 0;

    if (this.keys.has('w') || this.keys.has('arrowup')) moveY -= 1;
    if (this.keys.has('s') || this.keys.has('arrowdown')) moveY += 1;
    if (this.keys.has('a') || this.keys.has('arrowleft')) moveX -= 1;
    if (this.keys.has('d') || this.keys.has('arrowright')) moveX += 1;

    // Normalize diagonal movement
    if (moveX !== 0 && moveY !== 0) {
      const magnitude = Math.sqrt(moveX * moveX + moveY * moveY);
      moveX /= magnitude;
      moveY /= magnitude;
    }

    return {
      moveX,
      moveY,
      primaryFire: this.mouseDown || this.keys.has(' '),
      secondaryAbility: this.keys.has('e'),
      interact: this.keys.has('f'),
      aimAngle: this._aimAngle,
    };
  }

  /**
   * Check if the player is providing any movement input.
   */
  isMoving(): boolean {
    return this.keys.has('w') || this.keys.has('a') || this.keys.has('s') || this.keys.has('d')
        || this.keys.has('arrowup') || this.keys.has('arrowleft')
        || this.keys.has('arrowdown') || this.keys.has('arrowright');
  }

  /**
   * Update aim angle based on canvas-relative mouse position and player screen position.
   */
  updateAimAngle(playerScreenX: number, playerScreenY: number): void {
    const dx = this.mouseX - playerScreenX;
    const dy = this.mouseY - playerScreenY;
    this._aimAngle = Math.atan2(dy, dx);
  }

  private handleKeyDown(e: KeyboardEvent): void {
    if (!this._enabled) return;

    // Don't capture if user is typing in an input
    if (e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement) {
      return;
    }

    const key = e.key.toLowerCase();
    this.keys.add(key);

    // Prevent default for game keys (prevent scrolling, etc.)
    if (['w', 'a', 's', 'd', ' ', 'e', 'f', 'arrowup', 'arrowdown', 'arrowleft', 'arrowright'].includes(key)) {
      e.preventDefault();
    }

    this.onKeyDown?.(key);
  }

  private handleKeyUp(e: KeyboardEvent): void {
    const key = e.key.toLowerCase();
    this.keys.delete(key);
  }

  private handleMouseMove(e: MouseEvent): void {
    this.mouseX = e.clientX;
    this.mouseY = e.clientY;
  }

  private handleMouseDown(e: MouseEvent): void {
    if (!this._enabled) return;
    if (e.button === 0) { // Left click
      this.mouseDown = true;
    }
  }

  private handleMouseUp(e: MouseEvent): void {
    if (e.button === 0) {
      this.mouseDown = false;
    }
  }

  private handleBlur(): void {
    // Clear all keys when window loses focus
    this.keys.clear();
    this.mouseDown = false;
  }
}
