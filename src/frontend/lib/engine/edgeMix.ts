/**
 * Performant biome-edge mixing.
 *
 * The old clip-path fringe ran 4 save/clip/restore cycles per tile, every
 * time a chunk was rasterized. This stamps a neighbor through a precomputed
 * noisy alpha mask instead:
 *   1. Masks are built once at 32px (N/E/S/W).
 *   2. Scaled to the current tile size when zoom changes.
 *   3. Applied with destination-in on a reused scratch canvas.
 *
 * Mixing only happens inside OverworldTileCache.rasterize (cached chunks),
 * never per animation frame.
 */

const MASK_SRC = 32;
const DIRS: Array<[number, number]> = [[0, -1], [1, 0], [0, 1], [-1, 0]];

function hashNoise(x: number, y: number, dir: number): number {
  const n = Math.sin((x * 12.9898 + y * 78.233 + dir * 37.719) * 0.017) * 43758.5453;
  return n - Math.floor(n);
}

function buildMask(dir: number): HTMLCanvasElement {
  const canvas = document.createElement('canvas');
  canvas.width = MASK_SRC;
  canvas.height = MASK_SRC;
  const ctx = canvas.getContext('2d');
  if (!ctx) return canvas;
  const img = ctx.createImageData(MASK_SRC, MASK_SRC);
  const depth = MASK_SRC * 0.4;
  for (let y = 0; y < MASK_SRC; y++) {
    for (let x = 0; x < MASK_SRC; x++) {
      let dist = 0;
      if (dir === 0) dist = y;
      else if (dir === 1) dist = MASK_SRC - 1 - x;
      else if (dir === 2) dist = MASK_SRC - 1 - y;
      else dist = x;
      const jagged = depth * (0.42 + hashNoise(x, y, dir) * 0.72);
      const a = dist < jagged ? 1 - dist / jagged : 0;
      const i = (y * MASK_SRC + x) * 4;
      img.data[i] = 255;
      img.data[i + 1] = 255;
      img.data[i + 2] = 255;
      img.data[i + 3] = Math.floor(Math.max(0, Math.min(1, a * a)) * 255);
    }
  }
  ctx.putImageData(img, 0, 0);
  return canvas;
}

export class EdgeMixer {
  private sourceMasks: HTMLCanvasElement[] | null = null;
  private scaledMasks: HTMLCanvasElement[] | null = null;
  private scaledSize = -1;
  private scratch: HTMLCanvasElement | null = null;
  private scratchSize = -1;

  private ensureSource(): HTMLCanvasElement[] {
    if (!this.sourceMasks) {
      this.sourceMasks = [0, 1, 2, 3].map(buildMask);
    }
    return this.sourceMasks;
  }

  private ensureScaled(size: number): HTMLCanvasElement[] {
    if (this.scaledMasks && this.scaledSize === size) return this.scaledMasks;
    const src = this.ensureSource();
    this.scaledMasks = src.map(mask => {
      const c = document.createElement('canvas');
      c.width = size;
      c.height = size;
      const ctx = c.getContext('2d');
      if (ctx) {
        ctx.imageSmoothingEnabled = false;
        ctx.drawImage(mask, 0, 0, size, size);
      }
      return c;
    });
    this.scaledSize = size;
    return this.scaledMasks;
  }

  private ensureScratch(size: number): CanvasRenderingContext2D | null {
    if (!this.scratch || this.scratchSize !== size) {
      this.scratch = document.createElement('canvas');
      this.scratch.width = size;
      this.scratch.height = size;
      this.scratchSize = size;
    }
    return this.scratch.getContext('2d');
  }

  stamp(
    dest: CanvasRenderingContext2D,
    drawNeighbor: (ctx: CanvasRenderingContext2D) => void,
    destX: number,
    destY: number,
    destSize: number,
    dir: number
  ): void {
    const masks = this.ensureScaled(destSize);
    const scratch = this.ensureScratch(destSize);
    if (!scratch || !this.scratch) return;
    scratch.clearRect(0, 0, destSize, destSize);
    scratch.globalCompositeOperation = 'source-over';
    drawNeighbor(scratch);
    scratch.globalCompositeOperation = 'destination-in';
    scratch.drawImage(masks[dir], 0, 0);
    scratch.globalCompositeOperation = 'source-over';
    dest.drawImage(this.scratch, destX, destY);
  }
}

export { DIRS as EDGE_DIRS };

/** Architecture tiles keep hard edges; biomes mix. */
export function shouldMixEdges(a: number, b: number): boolean {
  if (a === b) return false;
  const hard = a === 11 || a === 13 || b === 11 || b === 13;
  return !hard;
}
