/**
 * Chunked overworld tile cache. Zooming out used to draw tens of thousands of
 * individual tiles per frame, which stalled the JS thread and made movement
 * appear frozen. Chunks are rebuilt only when zoom, lake drain, or the anim
 * clock changes.
 *
 * Edge mixing uses precomputed noise masks (see edgeMix.ts) so biome borders
 * blend without per-tile clip paths. ¾-view south faces are stamped onto
 * cliffs/walls/forests in the same raster pass.
 */

import { Camera, getEffectiveTileSize, getVisibleBounds, worldToScreen } from './camera';
import { OverworldGameMap, OwTileType, OW_TILE_COLORS, getOwTile } from '../overworld-map';
import { TileAtlas } from './tilesets';
import { EdgeMixer, EDGE_DIRS, shouldMixEdges } from './edgeMix';
import { TILE_PALETTE_WASH, hexToRgb } from '../palettes';

const CHUNK = 12;
const ANIMATED = new Set<number>([
  OwTileType.DeepWater, OwTileType.ShallowWater, OwTileType.Mist, OwTileType.DungeonEntrance,
]);

const TALL = new Set<number>([
  OwTileType.Wall, OwTileType.Mountain, OwTileType.Ruins, OwTileType.Forest,
]);

export class OverworldTileCache {
  private chunks = new Map<string, HTMLCanvasElement>();
  private zoomKey = -1;
  private animKey = -1;
  private drainKey = false;
  private mixer = new EdgeMixer();

  invalidate(): void {
    this.chunks.clear();
  }

  draw(
    ctx: CanvasRenderingContext2D,
    camera: Camera,
    map: OverworldGameMap,
    atlas: TileAtlas | null,
    nowMs: number
  ): void {
    const tileSize = Math.max(1, Math.round(getEffectiveTileSize(camera)));
    const anim = Math.floor(nowMs / 280);
    const drained = !!map.lakeDrained;
    if (tileSize !== this.zoomKey) {
      this.chunks.clear();
      this.zoomKey = tileSize;
    }
    if (drained !== this.drainKey) {
      this.chunks.clear();
      this.drainKey = drained;
    }
    if (anim !== this.animKey) {
      for (const key of [...this.chunks.keys()]) {
        if (key.startsWith('a:')) this.chunks.delete(key);
      }
      this.animKey = anim;
    }

    const bounds = getVisibleBounds(camera);
    const minCX = Math.floor(Math.max(0, bounds.minX) / CHUNK);
    const minCY = Math.floor(Math.max(0, bounds.minY) / CHUNK);
    const maxCX = Math.floor(Math.min(map.width - 1, bounds.maxX) / CHUNK);
    const maxCY = Math.floor(Math.min(map.height - 1, bounds.maxY) / CHUNK);

    for (let cy = minCY; cy <= maxCY; cy++) {
      for (let cx = minCX; cx <= maxCX; cx++) {
        const originX = cx * CHUNK;
        const originY = cy * CHUNK;
        const hasAnim = chunkHasAnimated(map, originX, originY);
        const key = `${hasAnim ? 'a' : 's'}:${cx}:${cy}:${tileSize}:${hasAnim ? anim : 0}:${drained ? 1 : 0}`;
        let chunk = this.chunks.get(key);
        if (!chunk) {
          chunk = this.rasterize(map, atlas, originX, originY, tileSize, nowMs);
          this.chunks.set(key, chunk);
          if (this.chunks.size > 220) {
            const first = this.chunks.keys().next().value;
            if (first) this.chunks.delete(first);
          }
        }
        const screen = worldToScreen(camera, originX, originY);
        ctx.drawImage(chunk, Math.floor(screen.x), Math.floor(screen.y));
      }
    }
  }

  private rasterize(
    map: OverworldGameMap,
    atlas: TileAtlas | null,
    originX: number,
    originY: number,
    tileSize: number,
    nowMs: number
  ): HTMLCanvasElement {
    const canvas = document.createElement('canvas');
    canvas.width = CHUNK * tileSize;
    canvas.height = CHUNK * tileSize + Math.ceil(tileSize * 0.2);
    const c = canvas.getContext('2d');
    if (!c) return canvas;
    c.imageSmoothingEnabled = false;

    const maxX = Math.min(map.width, originX + CHUNK);
    const maxY = Math.min(map.height, originY + CHUNK);

    const drawCell = (
      ctx: CanvasRenderingContext2D,
      tile: number,
      wx: number,
      wy: number,
      dx: number,
      dy: number
    ) => {
      const drawn = atlas?.drawOverworldTile(ctx, tile, wx, wy, dx, dy, tileSize, nowMs) ?? false;
      if (!drawn) {
        ctx.fillStyle = OW_TILE_COLORS[tile] || OW_TILE_COLORS[OwTileType.Grass];
        ctx.fillRect(dx, dy, tileSize, tileSize);
      }
      const wash = TILE_PALETTE_WASH[tile];
      if (wash) {
        const [r, g, b] = hexToRgb(wash);
        ctx.fillStyle = `rgba(${r},${g},${b},0.18)`;
        ctx.fillRect(dx, dy, tileSize, tileSize);
      }
    };

    for (let y = originY; y < maxY; y++) {
      for (let x = originX; x < maxX; x++) {
        const tile = getOwTile(map, x, y);
        const dx = (x - originX) * tileSize;
        const dy = (y - originY) * tileSize;
        drawCell(c, tile, x, y, dx, dy);
      }
    }

    for (let y = originY; y < maxY; y++) {
      for (let x = originX; x < maxX; x++) {
        const tile = getOwTile(map, x, y);
        const dx = (x - originX) * tileSize;
        const dy = (y - originY) * tileSize;
        for (let d = 0; d < EDGE_DIRS.length; d++) {
          const [ox, oy] = EDGE_DIRS[d];
          const n = getOwTile(map, x + ox, y + oy);
          if (!shouldMixEdges(tile, n)) continue;
          this.mixer.stamp(
            c,
            ctx => drawCell(ctx, n, x + ox, y + oy, 0, 0),
            dx, dy, tileSize, d
          );
        }
      }
    }

    for (let y = originY; y < maxY; y++) {
      for (let x = originX; x < maxX; x++) {
        const tile = getOwTile(map, x, y);
        if (!TALL.has(tile)) continue;
        const south = getOwTile(map, x, y + 1);
        if (TALL.has(south) || south === OwTileType.Wall) continue;
        const dx = (x - originX) * tileSize;
        const dy = (y - originY) * tileSize;
        const faceH = Math.max(4, Math.floor(tileSize * 0.34));
        c.fillStyle = 'rgba(8, 4, 2, 0.42)';
        c.fillRect(dx, dy + tileSize - faceH, tileSize, faceH);
        c.fillStyle = 'rgba(232, 212, 139, 0.08)';
        c.fillRect(dx, dy + tileSize - faceH, tileSize, 2);
        const lip = Math.max(3, Math.floor(tileSize * 0.16));
        c.fillStyle = 'rgba(12, 8, 4, 0.55)';
        c.fillRect(dx + 1, dy + tileSize, tileSize - 2, lip);
      }
    }

    for (let y = originY; y < maxY; y++) {
      for (let x = originX; x < maxX; x++) {
        if (getOwTile(map, x, y) !== OwTileType.Ladder) continue;
        const dx = (x - originX) * tileSize;
        const dy = (y - originY) * tileSize;
        drawLadderRungs(c, dx, dy, tileSize);
      }
    }

    return canvas;
  }
}

function drawLadderRungs(ctx: CanvasRenderingContext2D, dx: number, dy: number, size: number): void {
  const rail = Math.max(2, Math.floor(size * 0.12));
  ctx.fillStyle = '#4A3A22';
  ctx.fillRect(dx + size * 0.22, dy, rail, size);
  ctx.fillRect(dx + size * 0.72, dy, rail, size);
  ctx.fillStyle = '#8B6B2E';
  const step = Math.max(4, Math.floor(size / 4));
  for (let i = 1; i < 4; i++) {
    ctx.fillRect(dx + size * 0.2, dy + i * step, size * 0.6, Math.max(2, Math.floor(size * 0.08)));
  }
}

function chunkHasAnimated(map: OverworldGameMap, originX: number, originY: number): boolean {
  const maxX = Math.min(map.width, originX + CHUNK);
  const maxY = Math.min(map.height, originY + CHUNK);
  for (let y = originY; y < maxY; y++) {
    for (let x = originX; x < maxX; x++) {
      if (ANIMATED.has(getOwTile(map, x, y))) return true;
    }
  }
  return false;
}
