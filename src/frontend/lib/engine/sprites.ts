/**
 * =============================================================================
 * sprites.ts — Sprite loader (manifest-driven, easy to swap art)
 * =============================================================================
 *
 * Drop a PNG in /assets/sprites/ and add (or edit) an entry in manifest.json.
 * Keys are entity ids (player_a, gronk, cultist_torch, ...). `file` overrides
 * the default `<id>.png` so several ids can share one sheet (elite_gronk).
 *
 * SHEET LAYOUT (one PNG per sprite, or shared via `file`):
 *   Cell size = width x height.
 *   Columns   = frames (walk cycle length).
 *   Rows 0 .. directions-1              = walk (and idle = frame 0)
 *   Rows directions .. 2*directions-1   = attack (optional; attackFrames >= 1)
 *
 * Walk frames advance from WORLD distance traveled, not screen pixels or zoom.
 * Attack frames play from elapsed ms after noteAttack() / attackCooldown.
 * =============================================================================
 */

export interface SpriteManifestEntry {
  width: number;
  height: number;
  frames: number;
  directions?: number;
  animSpeed?: number;
  /** World tiles per full walk cycle. Independent of camera zoom. */
  strideTiles?: number;
  attackFrames?: number;
  attackDurationMs?: number;
  color?: string;
  collision?: boolean;
  collisionRadius?: number;
  file?: string;
}

export interface DrawSpriteOpts {
  action?: 'idle' | 'walk' | 'attack';
  facing?: number;
  /** Accumulated world-tile distance for walk cycling. */
  distance?: number;
  attackElapsedMs?: number;
  /** ¾-view default: sprite sits on (x,y) at its feet. */
  anchor?: 'center' | 'feet';
  /** Extra height for characters so they read as standing in front of facades. */
  heightScale?: number;
}

export const FIGURE_IDS = ['a', 'b', 'c'] as const;
export type FigureId = (typeof FIGURE_IDS)[number];

export function normalizeFigure(figure?: string | null): FigureId {
  return figure === 'a' || figure === 'b' || figure === 'c' ? figure : 'b';
}

export function playerSpriteName(figure?: string | null): string {
  return `player_${normalizeFigure(figure)}`;
}

/** Down=0, Left=1, Right=2, Up=3 */
export function facingFromVelocity(vx: number, vy: number, fallback = 0): number {
  if (vx === 0 && vy === 0) return fallback;
  if (Math.abs(vy) >= Math.abs(vx)) return vy >= 0 ? 0 : 3;
  return vx >= 0 ? 2 : 1;
}

const lastFacing = new Map<string, number>();

/** Remember last non-zero facing so idle/attack keep the walk direction. */
export function facingFromMotion(id: string, vx: number, vy: number): number {
  const next = facingFromVelocity(vx, vy, lastFacing.get(id) ?? 0);
  lastFacing.set(id, next);
  return next;
}

export type SpriteManifest = Record<string, SpriteManifestEntry>;

interface LoadedSprite {
  image: HTMLImageElement;
  loaded: boolean;
}

export class SpriteCache {
  private manifest: SpriteManifest = {};
  private sprites: Map<string, LoadedSprite> = new Map();
  private byFile: Map<string, LoadedSprite> = new Map();
  private manifestLoaded = false;

  async loadAll(): Promise<void> {
    try {
      const response = await fetch('/assets/sprites/manifest.json');
      if (!response.ok) {
        console.warn('[Sprites] Failed to load manifest, using built-in keys');
        this.manifest = builtinManifest();
        this.manifestLoaded = true;
      } else {
        this.manifest = await response.json();
        this.manifestLoaded = true;
      }

      const uniqueFiles = new Map<string, string[]>();
      for (const name of Object.keys(this.manifest)) {
        const file = this.fileFor(name);
        const list = uniqueFiles.get(file) ?? [];
        list.push(name);
        uniqueFiles.set(file, list);
      }

      await Promise.allSettled(
        [...uniqueFiles.entries()].map(([file, names]) => this.loadFile(file, names))
      );

      const loaded = Array.from(this.sprites.values()).filter(s => s.loaded).length;
      console.log(`[Sprites] Loaded ${loaded}/${Object.keys(this.manifest).length} entries (${uniqueFiles.size} files)`);
    } catch (e) {
      console.warn('[Sprites] Error loading sprites:', e);
    }
  }

  private fileFor(name: string): string {
    return this.manifest[name]?.file || `${name}.png`;
  }

  private loadFile(file: string, names: string[]): Promise<void> {
    const existing = this.byFile.get(file);
    if (existing) {
      for (const name of names) this.sprites.set(name, existing);
      return Promise.resolve();
    }

    return new Promise(resolve => {
      const img = new Image();
      const slot: LoadedSprite = { image: img, loaded: false };
      this.byFile.set(file, slot);
      for (const name of names) this.sprites.set(name, slot);
      img.onload = () => {
        slot.loaded = true;
        resolve();
      };
      img.onerror = () => resolve();
      img.src = `/assets/sprites/${file}`;
    });
  }

  getEntry(name: string): SpriteManifestEntry | undefined {
    return this.manifest[name];
  }

  hasSprite(name: string): boolean {
    return this.sprites.get(name)?.loaded ?? false;
  }

  drawSprite(
    ctx: CanvasRenderingContext2D,
    name: string,
    x: number,
    y: number,
    scale: number,
    opts: DrawSpriteOpts = {}
  ): boolean {
    const entry = this.manifest[name];
    if (!entry) return false;

    const sprite = this.sprites.get(name);
    const heightScale = opts.heightScale ?? (entry.directions && entry.directions > 1 ? 1.28 : 1);
    const renderWidth = (entry.width / 32) * scale;
    const renderHeight = (entry.height / 32) * scale * heightScale;
    const anchor = opts.anchor ?? 'feet';
    const dx = Math.round(x - renderWidth / 2);
    const dy = anchor === 'feet'
      ? Math.round(y - renderHeight + scale * 0.12)
      : Math.round(y - renderHeight / 2);

    if (dx + renderWidth < 0 || dy + renderHeight < 0 ||
        dx > ctx.canvas.width || dy > ctx.canvas.height) {
      return true;
    }

    if (sprite?.loaded) {
      const dirs = entry.directions && entry.directions > 1 ? entry.directions : 1;
      const facing = Math.max(0, Math.min(dirs - 1, opts.facing ?? 0));
      const attacking = opts.action === 'attack' && (entry.attackFrames ?? 0) > 0;
      const walkFrames = Math.max(1, entry.frames);
      let frame = 0;
      let row = facing;

      if (attacking) {
        const af = entry.attackFrames || 1;
        const dur = entry.attackDurationMs || 280;
        const elapsed = Math.max(0, opts.attackElapsedMs ?? 0);
        frame = Math.min(af - 1, Math.floor((elapsed / dur) * af));
        row = dirs + facing;
      } else if (opts.action === 'walk') {
        const stride = entry.strideTiles && entry.strideTiles > 0 ? entry.strideTiles : 0.9;
        const dist = opts.distance ?? 0;
        frame = Math.floor((dist / stride) * walkFrames) % walkFrames;
      } else if (walkFrames > 1 && entry.animSpeed) {
        frame = Math.floor(performance.now() / entry.animSpeed) % walkFrames;
      }

      ctx.drawImage(
        sprite.image,
        frame * entry.width, row * entry.height, entry.width, entry.height,
        dx, dy, Math.round(renderWidth), Math.round(renderHeight)
      );
      return true;
    }

    this.drawPlaceholder(ctx, name, entry, dx, dy, renderWidth, renderHeight);
    return false;
  }

  private drawPlaceholder(
    ctx: CanvasRenderingContext2D,
    name: string,
    entry: SpriteManifestEntry,
    drawX: number,
    drawY: number,
    width: number,
    height: number
  ): void {
    const color = entry.color || '#5a5a5a';
    const x = drawX + width / 2;
    const y = drawY + height / 2;
    ctx.fillStyle = color;
    ctx.fillRect(drawX, drawY, width, height);
    ctx.strokeStyle = 'rgba(255, 255, 255, 0.3)';
    ctx.lineWidth = 1;
    ctx.strokeRect(drawX, drawY, width, height);
    if (width > 20 && height > 12) {
      ctx.fillStyle = 'rgba(255, 255, 255, 0.6)';
      ctx.font = `${Math.max(7, Math.min(10, width * 0.2))}px monospace`;
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      const label = name.length > 8 ? name.slice(0, 7) + '…' : name;
      ctx.fillText(label, x, y);
    }
  }

  get isLoaded(): boolean {
    return this.manifestLoaded;
  }
}

let globalCache: SpriteCache | null = null;

function builtinManifest(): SpriteManifest {
  const player = {
    width: 32, height: 32, frames: 4, directions: 4,
    strideTiles: 0.9, attackFrames: 2, attackDurationMs: 280, color: '#c9a84c',
  };
  return {
    player_a: { ...player },
    player_b: { ...player, color: '#9a8b74' },
    player_c: { ...player, color: '#8b5f3a' },
    gronk: { width: 48, height: 48, frames: 4, strideTiles: 0.8, attackFrames: 2, attackDurationMs: 280, color: '#3a2a1a' },
  };
}

export function getSpriteCache(): SpriteCache {
  if (!globalCache) globalCache = new SpriteCache();
  return globalCache;
}

export async function initSprites(): Promise<SpriteCache> {
  const cache = getSpriteCache();
  if (!cache.isLoaded) await cache.loadAll();
  return cache;
}
