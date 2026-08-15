/**
 * =============================================================================
 * tilesets.ts — Terrain atlas loader with color fallback
 * =============================================================================
 *
 * Loads /assets/tilesets/manifest.json plus one PNG per sheet.
 * Overworld and dungeon tile bytes map to atlas cells. Missing art falls back
 * to the existing solid-color palettes.
 * =============================================================================
 */

export interface TileSheetDef {
  file: string;
  columns: number;
}

export interface TileMapping {
  sheet: string;
  index: number;
  variants?: number;
  frames?: number;
  animSpeed?: number;
}

export interface TilesetManifest {
  tileSize: number;
  sheets: Record<string, TileSheetDef>;
  overworld: Record<string, TileMapping>;
  dungeon: Record<string, TileMapping>;
}

interface LoadedSheet {
  image: HTMLImageElement;
  columns: number;
  loaded: boolean;
}

/**
 * Caches tileset pages and draws individual terrain cells.
 */
export class TileAtlas {
  private manifest: TilesetManifest | null = null;
  private sheets: Map<string, LoadedSheet> = new Map();
  private manifestLoaded = false;

  async loadAll(): Promise<void> {
    try {
      const response = await fetch('/assets/tilesets/manifest.json');
      if (!response.ok) {
        console.warn('[Tilesets] Failed to load manifest, using color tiles');
        return;
      }
      const manifest = await response.json() as TilesetManifest;
      this.manifest = manifest;
      this.manifestLoaded = true;

      const loadPromises = Object.entries(manifest.sheets).map(([name, def]) =>
        this.loadSheet(name, def)
      );
      await Promise.allSettled(loadPromises);

      const loaded = Array.from(this.sheets.values()).filter(s => s.loaded).length;
      console.log(`[Tilesets] Loaded ${loaded}/${Object.keys(manifest.sheets).length} sheets`);
    } catch (e) {
      console.warn('[Tilesets] Error loading tilesets:', e);
    }
  }

  private loadSheet(name: string, def: TileSheetDef): Promise<void> {
    return new Promise(resolve => {
      const img = new Image();
      img.onload = () => {
        this.sheets.set(name, { image: img, columns: def.columns, loaded: true });
        resolve();
      };
      img.onerror = () => {
        this.sheets.set(name, { image: img, columns: def.columns, loaded: false });
        resolve();
      };
      img.src = `/assets/tilesets/${def.file}`;
    });
  }

  get isLoaded(): boolean {
    return this.manifestLoaded;
  }

  get tileSize(): number {
    return this.manifest?.tileSize ?? 32;
  }

  drawOverworldTile(
    ctx: CanvasRenderingContext2D,
    tileType: number,
    worldX: number,
    worldY: number,
    destX: number,
    destY: number,
    destSize: number,
    nowMs: number,
    _neighbor?: (dx: number, dy: number) => number
  ): boolean {
    return this.drawMapped(ctx, this.manifest?.overworld, tileType, worldX, worldY, destX, destY, destSize, nowMs);
  }

  drawDungeonTile(
    ctx: CanvasRenderingContext2D,
    tileType: number,
    worldX: number,
    worldY: number,
    destX: number,
    destY: number,
    destSize: number,
    nowMs: number
  ): boolean {
    return this.drawMapped(ctx, this.manifest?.dungeon, tileType, worldX, worldY, destX, destY, destSize, nowMs);
  }

  private drawMapped(
    ctx: CanvasRenderingContext2D,
    table: Record<string, TileMapping> | undefined,
    tileType: number,
    worldX: number,
    worldY: number,
    destX: number,
    destY: number,
    destSize: number,
    nowMs: number
  ): boolean {
    if (!table) return false;
    const mapping = table[String(tileType)];
    if (!mapping) return false;
    const sheet = this.sheets.get(mapping.sheet);
    if (!sheet?.loaded) return false;

    const srcSize = this.tileSize;
    let index = mapping.index;
    if (mapping.frames && mapping.frames > 1) {
      const speed = mapping.animSpeed || 280;
      index += Math.floor(nowMs / speed) % mapping.frames;
    } else if (mapping.variants && mapping.variants > 1) {
      index += Math.abs(worldX * 13 + worldY * 7) % mapping.variants;
    }

    const sx = (index % sheet.columns) * srcSize;
    const sy = Math.floor(index / sheet.columns) * srcSize;

    ctx.drawImage(
      sheet.image,
      sx, sy, srcSize, srcSize,
      Math.floor(destX), Math.floor(destY), Math.ceil(destSize), Math.ceil(destSize)
    );
    return true;
  }

}

let globalAtlas: TileAtlas | null = null;

export function getTileAtlas(): TileAtlas {
  if (!globalAtlas) {
    globalAtlas = new TileAtlas();
  }
  return globalAtlas;
}

export async function initTilesets(): Promise<TileAtlas> {
  const atlas = getTileAtlas();
  if (!atlas.isLoaded) {
    await atlas.loadAll();
  }
  return atlas;
}
