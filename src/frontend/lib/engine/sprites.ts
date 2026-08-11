/**
 * =============================================================================
 * sprites.ts — Sprite Asset Pipeline with Placeholder Fallback
 * =============================================================================
 *
 * Loads a manifest.json that defines all entity sprites (dimensions, frames, speed).
 * Attempts to load PNG spritesheets by convention (assets/sprites/<name>.png).
 * If a PNG doesn't exist, renders a colored rectangle placeholder at the correct size.
 *
 * SPRITESHEET FORMAT:
 * Horizontal strip: each frame is side-by-side left to right.
 * E.g., a 32x32 sprite with 4 frames = a 128x32 PNG.
 * Frame 0 is leftmost, frame N-1 is rightmost.
 *
 * USAGE:
 *   await spriteCache.loadAll();
 *   spriteCache.drawSprite(ctx, 'tree', screenX, screenY, tileSize, tick);
 *   // If tree.png exists → draws the sprite frame
 *   // If tree.png missing → draws a green rectangle placeholder
 * =============================================================================
 */

export interface SpriteManifestEntry {
  width: number;
  height: number;
  frames: number;
  animSpeed?: number;  // ms per frame (default 150)
  color?: string;      // placeholder color
  collision?: boolean;
  collisionRadius?: number;
}

export type SpriteManifest = Record<string, SpriteManifestEntry>;

interface LoadedSprite {
  image: HTMLImageElement;
  loaded: boolean;
}

/**
 * Manages sprite loading, caching, and rendering with placeholder fallback.
 */
export class SpriteCache {
  private manifest: SpriteManifest = {};
  private sprites: Map<string, LoadedSprite> = new Map();
  private manifestLoaded = false;

  /**
   * Load the manifest and attempt to load all sprite PNGs.
   */
  async loadAll(): Promise<void> {
    try {
      const response = await fetch('/assets/sprites/manifest.json');
      if (!response.ok) {
        console.warn('[Sprites] Failed to load manifest, using placeholders only');
        return;
      }
      this.manifest = await response.json();
      this.manifestLoaded = true;
      console.log(`[Sprites] Manifest loaded: ${Object.keys(this.manifest).length} entries`);

      // Attempt to load each sprite PNG
      const loadPromises = Object.keys(this.manifest).map(name => this.loadSprite(name));
      await Promise.allSettled(loadPromises);

      const loaded = Array.from(this.sprites.values()).filter(s => s.loaded).length;
      console.log(`[Sprites] Loaded ${loaded}/${Object.keys(this.manifest).length} sprite images`);
    } catch (e) {
      console.warn('[Sprites] Error loading sprites:', e);
    }
  }

  /**
   * Attempt to load a single sprite PNG.
   */
  private loadSprite(name: string): Promise<void> {
    return new Promise((resolve) => {
      const img = new Image();
      img.onload = () => {
        this.sprites.set(name, { image: img, loaded: true });
        resolve();
      };
      img.onerror = () => {
        // PNG not found — will use placeholder
        this.sprites.set(name, { image: img, loaded: false });
        resolve();
      };
      img.src = `/assets/sprites/${name}.png`;
    });
  }

  /**
   * Get manifest entry for a sprite name.
   */
  getEntry(name: string): SpriteManifestEntry | undefined {
    return this.manifest[name];
  }

  /**
   * Check if a sprite image is available (loaded successfully).
   */
  hasSprite(name: string): boolean {
    const sprite = this.sprites.get(name);
    return sprite?.loaded ?? false;
  }

  /**
   * Draw a sprite (or placeholder) at the given screen position.
   * 
   * @param ctx - Canvas rendering context
   * @param name - Sprite name (must match manifest key)
   * @param x - Screen X position (center of sprite)
   * @param y - Screen Y position (center of sprite)
   * @param scale - Scale factor (tileSize to render at)
   * @param tick - Current animation tick (for frame selection)
   * @param moving - Whether entity is moving (some sprites only animate when moving)
   */
  drawSprite(
    ctx: CanvasRenderingContext2D,
    name: string,
    x: number,
    y: number,
    scale: number,
    tick?: number,
    moving?: boolean
  ): boolean {
    const entry = this.manifest[name];
    if (!entry) return false; // Unknown sprite

    const sprite = this.sprites.get(name);
    const renderWidth = (entry.width / 32) * scale;  // Normalize to tile size
    const renderHeight = (entry.height / 32) * scale;

    if (sprite?.loaded) {
      // Draw the sprite from the spritesheet
      const frameIndex = this.getFrameIndex(entry, tick, moving);
      const srcX = frameIndex * entry.width;
      const srcY = 0;

      ctx.drawImage(
        sprite.image,
        srcX, srcY, entry.width, entry.height,  // Source rect
        x - renderWidth / 2, y - renderHeight / 2, renderWidth, renderHeight  // Dest rect
      );
      return true;
    } else {
      // Draw placeholder rectangle
      this.drawPlaceholder(ctx, name, entry, x, y, renderWidth, renderHeight);
      return false;
    }
  }

  /**
   * Calculate the current animation frame index.
   */
  private getFrameIndex(entry: SpriteManifestEntry, tick?: number, moving?: boolean): number {
    if (entry.frames <= 1) return 0;
    if (!tick && tick !== 0) return 0;

    // Only animate if moving (for entities), always animate for objects
    if (moving === false) return 0;

    const speed = entry.animSpeed || 150;
    const totalDuration = speed * entry.frames;
    const elapsed = tick % totalDuration;
    return Math.floor(elapsed / speed);
  }

  /**
   * Draw a colored rectangle placeholder with the entity type name.
   */
  private drawPlaceholder(
    ctx: CanvasRenderingContext2D,
    name: string,
    entry: SpriteManifestEntry,
    x: number,
    y: number,
    width: number,
    height: number
  ): void {
    const color = entry.color || '#5a5a5a';
    const drawX = x - width / 2;
    const drawY = y - height / 2;

    // Fill
    ctx.fillStyle = color;
    ctx.fillRect(drawX, drawY, width, height);

    // Border
    ctx.strokeStyle = 'rgba(255, 255, 255, 0.3)';
    ctx.lineWidth = 1;
    ctx.strokeRect(drawX, drawY, width, height);

    // Label (only if large enough to read)
    if (width > 20 && height > 12) {
      ctx.fillStyle = 'rgba(255, 255, 255, 0.6)';
      ctx.font = `${Math.max(7, Math.min(10, width * 0.2))}px monospace`;
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      // Truncate long names
      const label = name.length > 8 ? name.slice(0, 7) + '…' : name;
      ctx.fillText(label, x, y);
    }
  }

  /**
   * Check if the manifest has been loaded.
   */
  get isLoaded(): boolean {
    return this.manifestLoaded;
  }
}

// Global singleton instance
let globalCache: SpriteCache | null = null;

/**
 * Get or create the global sprite cache singleton.
 */
export function getSpriteCache(): SpriteCache {
  if (!globalCache) {
    globalCache = new SpriteCache();
  }
  return globalCache;
}

/**
 * Initialize the sprite cache (call once on app start).
 */
export async function initSprites(): Promise<SpriteCache> {
  const cache = getSpriteCache();
  if (!cache.isLoaded) {
    await cache.loadAll();
  }
  return cache;
}
