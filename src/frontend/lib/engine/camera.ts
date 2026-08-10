/**
 * Camera system for the game viewport.
 * Follows the local player and handles world-to-screen coordinate transforms.
 */

export interface Camera {
  // World position of camera center (in tiles)
  x: number;
  y: number;
  // Viewport size in pixels
  viewportWidth: number;
  viewportHeight: number;
  // Pixels per tile
  tileSize: number;
}

/**
 * Create a new camera centered at a world position.
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
  };
}

/**
 * Smoothly follow a target position (lerp).
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
 * Convert world coordinates (tile position) to screen coordinates (pixels).
 */
export function worldToScreen(camera: Camera, worldX: number, worldY: number): { x: number; y: number } {
  const screenX = (worldX - camera.x) * camera.tileSize + camera.viewportWidth / 2;
  const screenY = (worldY - camera.y) * camera.tileSize + camera.viewportHeight / 2;
  return { x: screenX, y: screenY };
}

/**
 * Convert screen coordinates (pixels) to world coordinates (tile position).
 */
export function screenToWorld(camera: Camera, screenX: number, screenY: number): { x: number; y: number } {
  const worldX = (screenX - camera.viewportWidth / 2) / camera.tileSize + camera.x;
  const worldY = (screenY - camera.viewportHeight / 2) / camera.tileSize + camera.y;
  return { x: worldX, y: worldY };
}

/**
 * Get the visible tile bounds for culling.
 */
export function getVisibleBounds(camera: Camera): {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
} {
  const halfWidth = camera.viewportWidth / 2 / camera.tileSize;
  const halfHeight = camera.viewportHeight / 2 / camera.tileSize;

  return {
    minX: Math.floor(camera.x - halfWidth) - 1,
    minY: Math.floor(camera.y - halfHeight) - 1,
    maxX: Math.ceil(camera.x + halfWidth) + 1,
    maxY: Math.ceil(camera.y + halfHeight) + 1,
  };
}
