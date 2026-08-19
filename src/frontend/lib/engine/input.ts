/**
 * =============================================================================
 * input.ts — Keyboard and Mouse Input Handler
 * =============================================================================
 *
 * Movement keys are held-state. Combat is click-to-cast like the overworld:
 * left mouse = primary ability, right mouse = secondary. One fire per click
 * (not held-repeat at 20Hz). Space and E are not combat leftovers.
 * =============================================================================
 */

export interface InputState {
  /** Horizontal movement: -1 (left) to 1 (right). */
  moveX: number;
  /** Vertical movement: -1 (up) to 1 (down). */
  moveY: number;
  /** True for one sample after a left-click (primary ability). */
  primaryFire: boolean;
  /** True for one sample after a right-click (secondary ability). */
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
  private pendingPrimary = false;
  private pendingSecondary = false;
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
    this.handleCanvasContextMenu = this.handleCanvasContextMenu.bind(this);
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
      this._canvas.removeEventListener('contextmenu', this.handleCanvasContextMenu);
    }
    this._canvas = canvas;
    if (this._canvas) {
      this._canvas.addEventListener('mousedown', this.handleCanvasMouseDown);
      this._canvas.addEventListener('contextmenu', this.handleCanvasContextMenu);
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
      this._canvas.removeEventListener('contextmenu', this.handleCanvasContextMenu);
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
      this.pendingPrimary = false;
      this.pendingSecondary = false;
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

    const primaryFire = this.pendingPrimary;
    const secondaryAbility = this.pendingSecondary;
    this.pendingPrimary = false;
    this.pendingSecondary = false;

    return {
      moveX,
      moveY,
      primaryFire,
      secondaryAbility,
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
    if (['w', 'a', 's', 'd', 'f', 'h', 'arrowup', 'arrowdown', 'arrowleft', 'arrowright'].includes(key)) {
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
    if (e.button === 0) {
      this.pendingPrimary = true;
      e.preventDefault();
    } else if (e.button === 2) {
      this.pendingSecondary = true;
      e.preventDefault();
    }
  }

  private handleCanvasContextMenu(e: Event): void {
    e.preventDefault();
  }

  /**
   * Mouse up on window — catches release even if cursor left canvas while holding.
   */
  private handleMouseUp(_e: MouseEvent): void {
    // Click-to-cast: fire is latched on mousedown, consumed by getState().
  }

  private handleBlur(): void {
    this.keys.clear();
    this.pendingPrimary = false;
    this.pendingSecondary = false;
  }
}
