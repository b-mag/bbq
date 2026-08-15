/**
 * =============================================================================
 * overworld-map.ts — Overworld Tile Map Types & Utilities
 * =============================================================================
 *
 * Defines tile types and colors for the persistent overworld map.
 * Mirrors the server's OverworldTileType enum.
 * Colors use a fantasy/Carcosa palette — more varied than the dungeon's
 * dark 1920s aesthetic, with greens, blues, and earth tones for outdoor biomes.
 * =============================================================================
 */

import { OwMapDataPayload } from './overworld-messages';

/**
 * Overworld tile types matching the server's OverworldTileType enum.
 */
export enum OwTileType {
  Grass = 0,
  DeepWater = 1,
  ShallowWater = 2,
  Forest = 3,
  Mountain = 4,
  Ruins = 5,
  Path = 6,
  Sand = 7,
  Bridge = 8,
  DungeonEntrance = 9,
  Cobblestone = 10,
  Wall = 11,
  Floor = 12,
  Door = 13,
  DarkGrass = 14,
  Mist = 15,
  Desert = 16,
  Swamp = 17,
  MountainPath = 18,
  Snow = 19,
  Ash = 20,
  Palace = 21,
  Flesh = 22,
  Ladder = 23,
}

/**
 * Color palette for overworld tiles — Carcosa-themed fantasy palette.
 * Muted, slightly alien feel (twin suns, black stars aesthetic).
 */
export const OW_TILE_COLORS: Record<number, string> = {
  [OwTileType.Grass]: '#3a5a2a',         // Dark forest green
  [OwTileType.DeepWater]: '#1a2a4a',     // Deep lake blue (Lake Hali)
  [OwTileType.ShallowWater]: '#2a4a5a',  // Shallow murky water
  [OwTileType.Forest]: '#1a3a1a',        // Very dark green (dense canopy)
  [OwTileType.Mountain]: '#4a4a50',      // Gray stone
  [OwTileType.Ruins]: '#5a5040',         // Weathered stone/sandstone
  [OwTileType.Path]: '#6a5a3a',          // Dirt path brown
  [OwTileType.Sand]: '#7a6a4a',          // Sandy coast
  [OwTileType.Bridge]: '#5a4a2a',        // Dark wood bridge
  [OwTileType.DungeonEntrance]: '#3a2020', // Dark entrance (ominous red-black)
  [OwTileType.Cobblestone]: '#5a5a52',   // Village cobblestone
  [OwTileType.Wall]: '#3a3530',          // Building walls
  [OwTileType.Floor]: '#4a4035',         // Interior floor
  [OwTileType.Door]: '#6a5030',          // Door wood
  [OwTileType.DarkGrass]: '#2a4a22',
  [OwTileType.Mist]: '#4a5a5a',
  [OwTileType.Desert]: '#8a7048',
  [OwTileType.Swamp]: '#1e3a22',
  [OwTileType.MountainPath]: '#6a6058',
  [OwTileType.Snow]: '#c8d0d8',
  [OwTileType.Ash]: '#4a4038',
  [OwTileType.Palace]: '#c9a84c',
  [OwTileType.Flesh]: '#5a2030',
  [OwTileType.Ladder]: '#6a5030',
};

/**
 * Decoded overworld map for client-side rendering and collision.
 */
export interface OverworldPoint {
  x: number;
  y: number;
}

export interface OverworldGameMap {
  width: number;
  height: number;
  seed: number;
  tiles: Uint8Array;
  /** Sand bar + island hidden under Hali until matchmaking is online. */
  lakeDrained?: boolean;
  lakeIsland?: OverworldPoint[];
  drainCauseway?: OverworldPoint[];
  lakeOverlay?: Set<string>;
}

function pointKey(x: number, y: number): string {
  return `${x},${y}`;
}

export function buildLakeOverlay(map: OverworldGameMap): Set<string> {
  const set = new Set<string>();
  for (const p of map.lakeIsland || []) set.add(pointKey(p.x, p.y));
  for (const p of map.drainCauseway || []) set.add(pointKey(p.x, p.y));
  return set;
}

/**
 * Decode overworld map data from the server.
 */
export function decodeOverworldMap(data: OwMapDataPayload): OverworldGameMap {
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
 * Get tile type at a coordinate.
 */
export function getOwTile(map: OverworldGameMap, x: number, y: number): OwTileType {
  if (x < 0 || x >= map.width || y < 0 || y >= map.height) {
    return OwTileType.DeepWater;
  }
  const overlay = map.lakeOverlay;
  if (!map.lakeDrained && overlay?.has(pointKey(x, y))) {
    return OwTileType.DeepWater;
  }
  return map.tiles[y * map.width + x] as OwTileType;
}

/**
 * Check if a tile is walkable.
 */
export function isOwWalkable(map: OverworldGameMap, x: number, y: number): boolean {
  const tile = getOwTile(map, x, y);
  switch (tile) {
    case OwTileType.Grass:
    case OwTileType.ShallowWater:
    case OwTileType.Path:
    case OwTileType.Sand:
    case OwTileType.Bridge:
    case OwTileType.DungeonEntrance:
    case OwTileType.Cobblestone:
    case OwTileType.Floor:
    case OwTileType.Door:
    case OwTileType.DarkGrass:
    case OwTileType.Mist:
    case OwTileType.Desert:
    case OwTileType.Swamp:
    case OwTileType.MountainPath:
    case OwTileType.Snow:
    case OwTileType.Ash:
    case OwTileType.Palace:
    case OwTileType.Flesh:
    case OwTileType.Ladder:
      return true;
    default:
      return false;
  }
}

/**
 * Check walkability with entity radius (4-corner bounding box).
 */
export function isOwWalkableF(map: OverworldGameMap, x: number, y: number, radius: number = 0.3): boolean {
  return (
    isOwWalkable(map, Math.floor(x - radius), Math.floor(y - radius)) &&
    isOwWalkable(map, Math.floor(x + radius), Math.floor(y - radius)) &&
    isOwWalkable(map, Math.floor(x - radius), Math.floor(y + radius)) &&
    isOwWalkable(map, Math.floor(x + radius), Math.floor(y + radius))
  );
}
