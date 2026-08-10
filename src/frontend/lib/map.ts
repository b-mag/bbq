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
