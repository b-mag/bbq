/**
 * =============================================================================
 * map.ts — Client-Side Tile Map Utilities
 * =============================================================================
 *
 * WHY CLIENT-SIDE MAP:
 * The server sends the entire tile map once at game start (base64 encoded).
 * The client decodes it into a Uint8Array and uses it for:
 *   - Rendering tiles (each byte maps to a color via TILE_COLORS)
 *   - Client-side prediction collision checks (movement prediction needs to
 *     know where walls are without asking the server)
 *
 * WHY DUPLICATE COLLISION LOGIC:
 * The isWalkableF() function here mirrors TileMap.IsWalkableF() on the server.
 * Both use the same 4-corner bounding box check with the same 0.3 tile radius.
 * This is critical for prediction accuracy — if the client and server disagree
 * on what's walkable, the player will experience rubber-banding.
 *
 * TILE COLORS:
 * The color palette is intentionally dark and muted (1920s noir aesthetic).
 * Walls are nearly black, floors are dark wood, streets are gray-brown.
 * Water gets a subtle animated shimmer effect in the renderer.
 * =============================================================================
 */

import { MapDataPayload } from './messages';

/**
 * Tile types matching the server-side TileType enum.
 */
export enum TileType {
  Floor = 0,
  Wall = 1,
  Door = 2,
  Water = 3,
  Cobblestone = 4,
  Sand = 5,
}

/**
 * Color palette for each tile type (1920s muted aesthetic).
 */
export const TILE_COLORS: Record<TileType, string> = {
  [TileType.Floor]: '#3d3225',     // Dark wood floor
  [TileType.Wall]: '#1a1510',      // Very dark wall
  [TileType.Door]: '#5c4a2e',      // Lighter wood for doors
  [TileType.Water]: '#1a3040',     // Dark ocean blue
  [TileType.Cobblestone]: '#4a4438', // Gray-brown cobblestone
  [TileType.Sand]: '#5c5040',      // Sandy beige
};

/**
 * Decoded client-side tile map.
 */
export interface GameMap {
  width: number;
  height: number;
  seed: number;
  tiles: Uint8Array;
}

/**
 * Decode map data received from the server (base64 encoded tiles).
 */
export function decodeMap(data: MapDataPayload): GameMap {
  // Decode base64 to Uint8Array
  const binaryString = atob(data.tilesBase64);
  const tiles = new Uint8Array(binaryString.length);
  for (let i = 0; i < binaryString.length; i++) {
    tiles[i] = binaryString.charCodeAt(i);
  }

  return {
    width: data.width,
    height: data.height,
    seed: data.seed,
    tiles,
  };
}

/**
 * Get the tile type at a given position.
 */
export function getTile(map: GameMap, x: number, y: number): TileType {
  if (x < 0 || x >= map.width || y < 0 || y >= map.height) {
    return TileType.Wall;
  }
  return map.tiles[y * map.width + x] as TileType;
}

/**
 * Check if a tile position is walkable.
 */
export function isWalkable(map: GameMap, x: number, y: number): boolean {
  const tile = getTile(map, x, y);
  return tile !== TileType.Wall && tile !== TileType.Water;
}

/**
 * Check if a floating-point position is walkable (with entity radius).
 */
export function isWalkableF(map: GameMap, x: number, y: number, radius: number = 0.3): boolean {
  return (
    isWalkable(map, Math.floor(x - radius), Math.floor(y - radius)) &&
    isWalkable(map, Math.floor(x + radius), Math.floor(y - radius)) &&
    isWalkable(map, Math.floor(x - radius), Math.floor(y + radius)) &&
    isWalkable(map, Math.floor(x + radius), Math.floor(y + radius))
  );
}
