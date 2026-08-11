/**
 * =============================================================================
 * camera.ts — Game Viewport Camera System with Zoom
 * =============================================================================
 *
 * WHY A CAMERA ABSTRACTION:
 * The game world is much larger than the viewport (80x60 tiles vs ~33x25 visible).
 * The camera defines which portion of the world is visible, follows the local player
 * smoothly, and converts between world coordinates (tiles) and screen coordinates (pixels).
 *
 * COORDINATE SYSTEMS:
 *   - World coords: float tile positions (e.g., player at 34.5, 22.7)
 *   - Screen coords: integer pixel positions on the canvas (e.g., 400, 300)
 *   - The camera position is the world coordinate at the CENTER of the viewport
 *
 * ZOOM:
 * The zoom level multiplies the effective tile size (pixels per tile).
 *   - zoom=1.0: default view (24px per tile)
 *   - zoom=2.0: zoomed in (48px per tile, fewer tiles visible, more detail)
 *   - zoom=0.5: zoomed out (12px per tile, more tiles visible, less detail)
 * Zoom is clamped between 0.5 and 2.5. Controlled by mouse scroll wheel.
 * All coordinate transforms use `tileSize * zoom` as the effective pixel scale.
 *
 * WHY SMOOTH FOLLOW (lerp):
 * Snapping the camera instantly to the player feels jarring. Lerping with a
 * smoothing factor of 0.12 creates a fluid "elastic" follow effect.
 *
 * CULLING:
 * getVisibleBounds() returns the tile range that's on-screen (accounting for zoom).
 * The renderer only draws tiles within these bounds.
 * =============================================================================
 */

/** Minimum zoom level (zoomed out — more world visible). */
export const MIN_ZOOM = 0.5;
/** Maximum zoom level (zoomed in — less world visible, more detail). */
export const MAX_ZOOM = 2.5;
/** How much zoom changes per scroll wheel tick. */
export const ZOOM_STEP = 0.1;

export interface Camera {
  /** World position of camera center (in tiles). */
  x: number;
  /** World position of camera center Y (in tiles). */
  y: number;
  /** Viewport width in pixels (matches canvas width). */
  viewportWidth: number;
  /** Viewport height in pixels (matches canvas height). */
  viewportHeight: number;
  /** Base pixels per tile (before zoom is applied). */
  tileSize: number;
  /**
   * Zoom multiplier. Effective pixels per tile = tileSize * zoom.
   * Default 1.0. Range: [MIN_ZOOM, MAX_ZOOM].
   * Scroll wheel up = zoom in (increase). Scroll down = zoom out (decrease).
   */
  zoom: number;
}

/**
 * Create a new camera centered at origin (0,0) with default zoom.
 */
export function createCamera(
  viewportWidth: number,
  viewportHeight: number,
  tileSize: number = 24
): Camera {
  return {
    x: 0,
    y: 0,
    viewportWidth,
    viewportHeight,
    tileSize,
    zoom: 1.0,
  };
}

/**
 * Get the effective tile size in pixels (base tileSize multiplied by zoom).
 * Used by all rendering and coordinate transform functions.
 */
export function getEffectiveTileSize(camera: Camera): number {
  return camera.tileSize * camera.zoom;
}

/**
 * Smoothly follow a target position using linear interpolation (lerp).
 *
 * @param smoothing - How quickly camera catches up (0=frozen, 1=instant snap).
 *                    0.12 gives a nice elastic feel at 60fps.
 */
export function cameraFollow(
  camera: Camera,
  targetX: number,
  targetY: number,
  smoothing: number = 0.1
): void {
  camera.x += (targetX - camera.x) * smoothing;
  camera.y += (targetY - camera.y) * smoothing;
}

/**
 * Adjust zoom level by a delta amount. Clamps to [MIN_ZOOM, MAX_ZOOM].
 *
 * @param delta - Positive zooms in, negative zooms out.
 *               Typically ±ZOOM_STEP per scroll wheel tick.
 */
export function cameraZoom(camera: Camera, delta: number): void {
  camera.zoom = Math.max(MIN_ZOOM, Math.min(MAX_ZOOM, camera.zoom + delta));
}

/**
 * Convert world coordinates (tile position) to screen coordinates (pixels).
 * Uses effective tile size (tileSize * zoom) for the conversion.
 *
 * Math: screenPos = (worldPos - cameraPos) * effectiveTileSize + viewportCenter
 */
export function worldToScreen(camera: Camera, worldX: number, worldY: number): { x: number; y: number } {
  const effectiveSize = camera.tileSize * camera.zoom;
  const screenX = (worldX - camera.x) * effectiveSize + camera.viewportWidth / 2;
  const screenY = (worldY - camera.y) * effectiveSize + camera.viewportHeight / 2;
  return { x: screenX, y: screenY };
}

/**
 * Convert screen coordinates (pixels) to world coordinates (tile position).
 * Used for mouse aim calculations (where is the cursor in world space?).
 *
 * Inverse of worldToScreen.
 */
export function screenToWorld(camera: Camera, screenX: number, screenY: number): { x: number; y: number } {
  const effectiveSize = camera.tileSize * camera.zoom;
  const worldX = (screenX - camera.viewportWidth / 2) / effectiveSize + camera.x;
  const worldY = (screenY - camera.viewportHeight / 2) / effectiveSize + camera.y;
  return { x: worldX, y: worldY };
}

/**
 * Get the visible tile bounds for render culling.
 * Accounts for zoom — zoomed in means fewer tiles visible, zoomed out means more.
 * The +1/-1 padding ensures tiles at the edge aren't popped in/out visibly.
 */
export function getVisibleBounds(camera: Camera): {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
} {
  const effectiveSize = camera.tileSize * camera.zoom;
  const halfWidth = camera.viewportWidth / 2 / effectiveSize;
  const halfHeight = camera.viewportHeight / 2 / effectiveSize;

  return {
    minX: Math.floor(camera.x - halfWidth) - 1,
    minY: Math.floor(camera.y - halfHeight) - 1,
    maxX: Math.ceil(camera.x + halfWidth) + 1,
    maxY: Math.ceil(camera.y + halfHeight) + 1,
  };
}
