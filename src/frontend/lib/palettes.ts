/**
 * Carcosa region palettes. Suggestions, not hard locks — multiply-tinted
 * onto terrain in the tile cache so biome edges pick up the right mood.
 *
 * Edit hexes here (or add a new palette) and the overworld picks them up
 * on the next chunk rebuild / page reload.
 *
 * Full art reference (named + sampled from every PNG):
 *   src/frontend/public/assets/palettes.json
 *   src/frontend/public/assets/catalog.json → palettes
 */

export interface CarcosaPalette {
  id: string;
  name: string;
  colors: string[];
}

export const PALETTES: Record<string, CarcosaPalette> = {
  gold: {
    id: 'gold',
    name: 'Carcosa Gold & Black Ichor',
    colors: ['#1A1208', '#2E2214', '#4A3A22', '#8B6B2E', '#C9A84C', '#E8D48B', '#3D1F0F', '#6B3A28', '#A67C52'],
  },
  flesh: {
    id: 'flesh',
    name: 'Giger Biomechanical Flesh',
    colors: ['#0D0505', '#1F0A0A', '#4A1515', '#8B2A2A', '#C45C4A', '#E8C8A0', '#A89070', '#5C3A2A', '#3A2820'],
  },
  purple: {
    id: 'purple',
    name: 'Purple Dream / Non-Euclidean Chamber',
    colors: ['#0F0518', '#1E0F2E', '#3A1F5C', '#6B3A9E', '#9B6BC9', '#C9A8E0', '#2A4A3A', '#5C7A4A', '#8BA87A'],
  },
  teal: {
    id: 'teal',
    name: 'Abyssal Teal & Black Water',
    colors: ['#020810', '#0A1A22', '#1A3A4A', '#2A6A7A', '#4A9AAB', '#8AC8D8', '#1A2A28', '#3A4A42', '#6A8A7A'],
  },
  crimson: {
    id: 'crimson',
    name: 'Scorched Crimson Wastes',
    colors: ['#0A0404', '#1F0A0A', '#4A1510', '#8B2A18', '#C45A30', '#E8A060', '#3A2210', '#6B4A28', '#A87A50'],
  },
  chartreuse: {
    id: 'chartreuse',
    name: 'Sickly Chartreuse & Void',
    colors: ['#0A0A05', '#1A1A0A', '#3A3A18', '#6B6B28', '#A8A84A', '#D8D88A', '#2A2210', '#5C4A28', '#8B7A50'],
  },
  drowned_dock: {
    id: 'drowned_dock',
    name: 'Drowned Dock / Labyrinth of Dagon',
    colors: ['#0A0D0B', '#1A1A1A', '#2E332A', '#3D3A30', '#8B4513', '#A0522D', '#B8860B', '#D2B48C', '#E8C8A0'],
  },
  agwan: {
    id: 'agwan',
    name: 'Agwan Flesh & Ichor',
    colors: ['#0D0505', '#1A1208', '#3A2820', '#8B2A2A', '#A0522D', '#C45C4A', '#E8C8A0', '#E8D4B8', '#1A1A1A'],
  },
};

/** Mid-tone used as a cheap multiply wash on a tile type. */
export const TILE_PALETTE_WASH: Record<number, string> = {
  0: PALETTES.chartreuse.colors[2],  // Grass
  1: PALETTES.teal.colors[2],        // DeepWater
  2: PALETTES.teal.colors[3],        // ShallowWater
  3: PALETTES.chartreuse.colors[3],  // Forest
  4: PALETTES.gold.colors[2],        // Mountain
  5: PALETTES.gold.colors[3],        // Ruins
  6: PALETTES.gold.colors[2],        // Path
  7: PALETTES.teal.colors[6],        // Sand
  8: PALETTES.gold.colors[2],        // Bridge
  9: PALETTES.flesh.colors[2],       // DungeonEntrance
  10: PALETTES.gold.colors[2],       // Cobblestone
  11: PALETTES.gold.colors[1],       // Wall
  12: PALETTES.gold.colors[2],       // Floor
  13: PALETTES.gold.colors[3],       // Door
  14: PALETTES.chartreuse.colors[2], // DarkGrass
  15: PALETTES.purple.colors[2],     // Mist
  16: PALETTES.crimson.colors[3],    // Desert
  17: PALETTES.chartreuse.colors[2], // Swamp
  18: PALETTES.gold.colors[2],       // MountainPath
  19: PALETTES.purple.colors[0],     // Snow
  20: PALETTES.crimson.colors[2],    // Ash
  21: PALETTES.gold.colors[4],       // Palace
  22: PALETTES.flesh.colors[3],      // Flesh
  23: PALETTES.gold.colors[3],       // Ladder
};

export function hexToRgb(hex: string): [number, number, number] {
  const h = hex.replace('#', '');
  return [
    parseInt(h.slice(0, 2), 16),
    parseInt(h.slice(2, 4), 16),
    parseInt(h.slice(4, 6), 16),
  ];
}
