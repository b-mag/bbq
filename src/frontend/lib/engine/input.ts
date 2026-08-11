/**
 * =============================================================================
 * input.ts — Keyboard and Mouse Input Handler
 * =============================================================================
 *
 * WHY A DEDICATED INPUT CLASS:
 * The game needs to track which keys are CURRENTLY HELD (not just key events).
 * For movement, we need to know "is W held RIGHT NOW" every 50ms (at tick rate).
 * The InputHandler continuously tracks held keys in a Set and provides a snapshot
 * via getState() that the input loop samples at 20Hz.
 *
 * HYBRID FIRE CONTROLS:
 * Primary fire is triggered by EITHER:
 *   - Left mouse click on the game canvas (scoped to canvas element only)
 *   - Spacebar press (works regardless of mouse position)
 * This gives players two options: mouse-aimed click-to-shoot (modern feel) or
 * spacebar (retro arcade feel). Both use the current aimAngle from mouse position.
 *
 * WHY CANVAS-SCOPED MOUSE CLICKS:
 * Mouse click listeners are attached to the canvas element specifically (not window).
 * This prevents clicking on HUD buttons, chat UI, or other React DOM elements from
 * accidentally firing the weapon. Mouse MOVE events stay on window so aim tracking
 * works even when the cursor is briefly outside the canvas bounds.
 *
 * AIM ANGLE:
 * The aim direction is calculated from the mouse cursor position relative to the
 * player's position on screen. Updated every mouse move event and read at tick rate.
 * The angle is sent to the server so projectiles fire in the correct direction.
 *
 * INPUT FILTERING:
 * When the user opens the chat selector (Enter key), the InputHandler is disabled
 * via the `enabled` setter. This clears all held keys and mouse state immediately.
 * =============================================================================
 */

export interface InputState {
  /** Horizontal movement: -1 (left) to 1 (right). */
  moveX: number;
  /** Vertical movement: -1 (up) to 1 (down). */
  moveY: number;
  /** True if primary fire is active (mouse click on canvas OR spacebar held). */
  primaryFire: boolean;
  /** True if secondary ability key (E) is held. */
  secondaryAbility: boolean;
  /** True if interact key (F) is held. */
  interact: boolean;
  /** True if med kit use key (H) is pressed. */
  useMedKit: boolean;
  /** Aim direction in radians, calculated from mouse position relative to player. */
  aimAngle: number;
}

/**
 * Captures keyboard and mouse input for the game.
 * Keyboard events are global (window), mouse click events are scoped to the canvas.
 */
export class InputHandler {
  private keys: Set<string> = new Set();
  private mouseX: number = 0;
  private mouseY: number = 0;
  private mouseDown: boolean = false;
  private _aimAngle: number = 0;
  private _enabled: boolean = true;
  /** Reference to the canvas element for scoped mouse click events. */
  private _canvas: HTMLElement | null = null;

  // Callbacks
  private onKeyDown?: (key: string) => void;

  constructor() {
    this.handleKeyDown = this.handleKeyDown.bind(this);
    this.handleKeyUp = this.handleKeyUp.bind(this);
    this.handleMouseMove = this.handleMouseMove.bind(this);
    this.handleCanvasMouseDown = this.handleCanvasMouseDown.bind(this);
    this.handleMouseUp = this.handleMouseUp.bind(this);
    this.handleBlur = this.handleBlur.bind(this);
  }

  /**
   * Set the canvas element for scoped mouse click detection.
   * Left-click fires ONLY when the click target is this element.
   * Must be called after the canvas mounts (useEffect in GameCanvas).
   */
  setCanvas(canvas: HTMLElement | null): void {
    // Remove listeners from old canvas
    if (this._canvas) {
      this._canvas.removeEventListener('mousedown', this.handleCanvasMouseDown);
    }
    this._canvas = canvas;
    // Add listeners to new canvas
    if (this._canvas) {
      this._canvas.addEventListener('mousedown', this.handleCanvasMouseDown);
    }
  }

  /**
   * Attach global event listeners (keyboard + mouse move + mouse up).
   * Mouse DOWN is attached to canvas via setCanvas() instead of window.
   */
  attach(): void {
    window.addEventListener('keydown', this.handleKeyDown);
    window.addEventListener('keyup', this.handleKeyUp);
    window.addEventListener('mousemove', this.handleMouseMove);
    // Mouse UP on window so we catch it even if cursor leaves canvas while holding
    window.addEventListener('mouseup', this.handleMouseUp);
    window.addEventListener('blur', this.handleBlur);
  }

  /**
   * Remove all event listeners.
   */
  detach(): void {
    window.removeEventListener('keydown', this.handleKeyDown);
    window.removeEventListener('keyup', this.handleKeyUp);
    window.removeEventListener('mousemove', this.handleMouseMove);
    window.removeEventListener('mouseup', this.handleMouseUp);
    window.removeEventListener('blur', this.handleBlur);
    // Clean up canvas listeners
    if (this._canvas) {
      this._canvas.removeEventListener('mousedown', this.handleCanvasMouseDown);
    }
  }

  /**
   * Enable/disable input processing.
   * When disabled (e.g., chat open), all held keys and mouse state are cleared.
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
   * Called at 20Hz by the input loop in useGameInput.
   */
  getState(): InputState {
    let moveX = 0;
    let moveY = 0;

    if (this.keys.has('w') || this.keys.has('arrowup')) moveY -= 1;
    if (this.keys.has('s') || this.keys.has('arrowdown')) moveY += 1;
    if (this.keys.has('a') || this.keys.has('arrowleft')) moveX -= 1;
    if (this.keys.has('d') || this.keys.has('arrowright')) moveX += 1;

    // Normalize diagonal movement so it isn't 41% faster
    if (moveX !== 0 && moveY !== 0) {
      const magnitude = Math.sqrt(moveX * moveX + moveY * moveY);
      moveX /= magnitude;
      moveY /= magnitude;
    }

    return {
      moveX,
      moveY,
      // HYBRID FIRE: left-click on canvas OR spacebar triggers primary fire
      primaryFire: this.mouseDown || this.keys.has(' '),
      secondaryAbility: this.keys.has('e'),
      interact: this.keys.has('f'),
      useMedKit: this.keys.has('h'),
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
   * Called by the render loop so aim direction is always current.
   */
  updateAimAngle(playerScreenX: number, playerScreenY: number): void {
    const dx = this.mouseX - playerScreenX;
    const dy = this.mouseY - playerScreenY;
    this._aimAngle = Math.atan2(dy, dx);
  }

  // --- Event handlers ---

  private handleKeyDown(e: KeyboardEvent): void {
    if (!this._enabled) return;

    // Don't capture if user is typing in an input element
    if (e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement) {
      return;
    }

    const key = e.key.toLowerCase();
    this.keys.add(key);

    // Prevent default for game keys (prevent page scrolling, etc.)
    if (['w', 'a', 's', 'd', ' ', 'e', 'f', 'h', 'arrowup', 'arrowdown', 'arrowleft', 'arrowright'].includes(key)) {
      e.preventDefault();
    }

    this.onKeyDown?.(key);
  }

  private handleKeyUp(e: KeyboardEvent): void {
    const key = e.key.toLowerCase();
    this.keys.delete(key);
  }

  private handleMouseMove(e: MouseEvent): void {
    // Track mouse position globally (for aim angle calculation)
    this.mouseX = e.clientX;
    this.mouseY = e.clientY;
  }

  /**
   * Mouse down handler scoped to the canvas element only.
   * This ensures clicking HUD buttons/chat doesn't trigger fire.
   */
  private handleCanvasMouseDown(e: MouseEvent): void {
    if (!this._enabled) return;
    if (e.button === 0) { // Left click only
      this.mouseDown = true;
      // Prevent canvas click from bubbling to other handlers
      e.preventDefault();
    }
  }

  /**
   * Mouse up on window — catches release even if cursor left canvas while holding.
   */
  private handleMouseUp(e: MouseEvent): void {
    if (e.button === 0) {
      this.mouseDown = false;
    }
  }

  private handleBlur(): void {
    // Clear all input state when window loses focus
    this.keys.clear();
    this.mouseDown = false;
  }
}
