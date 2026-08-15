/**
 * Per-player interior instances. Link-to-the-Past single dungeon room:
 * 16×11 tiles, door on the south wall, exit by walking onto it.
 */

import { OverworldGameMap, OwTileType } from './overworld-map';
import { OwWorldObjectData } from './overworld-messages';
import { buildingKind } from './npc-dialogue';

export const INTERIOR_W = 16;
export const INTERIOR_H = 11;

export interface InteriorInstance {
  map: OverworldGameMap;
  objects: OwWorldObjectData[];
  spawnX: number;
  spawnY: number;
  kind: ReturnType<typeof buildingKind>;
  returnX: number;
  returnY: number;
  title: string;
}

function hashSeed(x: number, y: number, type: string): number {
  let h = 2166136261;
  const s = `${Math.floor(x)}:${Math.floor(y)}:${type}`;
  for (let i = 0; i < s.length; i++) {
    h ^= s.charCodeAt(i);
    h = Math.imul(h, 16777619);
  }
  return h >>> 0;
}

function rng(seed: number): () => number {
  let s = seed || 1;
  return () => {
    s = (Math.imul(s, 1664525) + 1013904223) >>> 0;
    return s / 4294967296;
  };
}

export function generateInterior(
  type: string,
  worldX: number,
  worldY: number,
  returnX: number,
  returnY: number
): InteriorInstance {
  const kind = buildingKind(type);
  const tiles = new Uint8Array(INTERIOR_W * INTERIOR_H);
  const rand = rng(hashSeed(worldX, worldY, type));

  const wall = OwTileType.Wall;
  const floor = kind === 'hut' || kind === 'cave'
    ? OwTileType.Flesh
    : kind === 'shop'
      ? OwTileType.Palace
      : kind === 'tower'
        ? OwTileType.Cobblestone
        : OwTileType.Floor;

  for (let y = 0; y < INTERIOR_H; y++) {
    for (let x = 0; x < INTERIOR_W; x++) {
      const edge = y === 0 || y === INTERIOR_H - 1 || x === 0 || x === INTERIOR_W - 1;
      tiles[y * INTERIOR_W + x] = edge ? wall : floor;
    }
  }

  const doorX = 8;
  const doorY = INTERIOR_H - 1;
  tiles[doorY * INTERIOR_W + doorX] = OwTileType.Door;
  tiles[doorY * INTERIOR_W + doorX - 1] = OwTileType.Door;
  tiles[doorY * INTERIOR_W + doorX + 1] = OwTileType.Door;

  if (kind === 'shop' || kind === 'tower') {
    for (let x = 2; x < INTERIOR_W - 2; x++) {
      if (x === doorX) continue;
      tiles[2 * INTERIOR_W + x] = OwTileType.Ruins;
    }
  }

  if (kind === 'hut') {
    tiles[4 * INTERIOR_W + 4] = OwTileType.Flesh;
    tiles[5 * INTERIOR_W + 11] = OwTileType.Swamp;
  }

  const objects: OwWorldObjectData[] = [];
  if (kind === 'shop') {
    objects.push({
      type: 'npc_shopkeep',
      x: 8.5,
      y: 3.5,
      collision: false,
      collisionRadius: 0,
    });
  } else if (rand() > 0.45) {
    objects.push({
      type: rand() > 0.5 ? 'signpost' : 'ruined_pillar',
      x: 4.5 + rand() * 6,
      y: 3.5 + rand() * 2,
      collision: true,
      collisionRadius: 0.3,
    });
  }

  const titles: Record<typeof kind, string> = {
    house: 'A Dwelling',
    hut: 'A Low Hut',
    tower: 'The Dark Tower',
    shop: 'The Intact House',
    cave: 'A Hollow',
  };

  return {
    map: {
      width: INTERIOR_W,
      height: INTERIOR_H,
      seed: hashSeed(worldX, worldY, type),
      tiles,
    },
    objects,
    spawnX: doorX + 0.5,
    spawnY: INTERIOR_H - 2.4,
    kind,
    returnX,
    returnY,
    title: titles[kind],
  };
}

export function isInteriorExit(map: OverworldGameMap, x: number, y: number): boolean {
  if (y >= map.height - 1.05) return true;
  const tx = Math.floor(x);
  const ty = Math.floor(y);
  if (ty < 0 || ty >= map.height || tx < 0 || tx >= map.width) return true;
  return map.tiles[ty * map.width + tx] === OwTileType.Door && y > map.height - 1.55;
}
