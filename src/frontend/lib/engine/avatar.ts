/**
 * Avatar drawing — base figure plus future cosmetic layers.
 *
 * Layers (back to front):
 *   base  → chosen body (player_a / custom id)
 *   lower → shoes / legs
 *   core  → chest / core armor
 *   head  → headgear
 *
 * Each layer is a character sheet with the same cell layout as the base.
 * Missing layer files are skipped. Shops can start dropping PNGs later
 * without changing the draw path.
 *
 * Figure ids are lowercase [a-z0-9_]. Peers send their id; we load
 * `/assets/sprites/<sheet>.png` on demand so a joining player appears
 * as themselves instead of a gold circle.
 */

export interface AvatarLoadout {
  figure: string;
  lower?: string | null;
  core?: string | null;
  head?: string | null;
}

export interface DrawAvatarOpts {
  action?: 'idle' | 'walk' | 'attack';
  facing?: number;
  distance?: number;
  attackElapsedMs?: number;
  heightScale?: number;
}

const COLS = 4;
const WALK_ROWS = 4;

const images = new Map<string, HTMLImageElement>();
const failed = new Set<string>();

export function normalizeFigureId(figure?: string | null): string {
  if (!figure) return 'b';
  const t = figure.trim().toLowerCase();
  return /^[a-z0-9_]{1,32}$/.test(t) ? t : 'b';
}

export function figureSheetName(figure?: string | null): string {
  const id = normalizeFigureId(figure);
  if (id.startsWith('player_') || id.startsWith('villager_') || id.startsWith('satyr_')) return id;
  return `player_${id}`;
}

function sheetUrl(sheet: string): string {
  return `/assets/sprites/${sheet}.png`;
}

function ensureImage(sheet: string): HTMLImageElement | null {
  if (failed.has(sheet)) return null;
  let img = images.get(sheet);
  if (!img) {
    img = new Image();
    img.decoding = 'async';
    img.onload = () => { /* decoded */ };
    img.onerror = () => failed.add(sheet);
    img.src = sheetUrl(sheet);
    images.set(sheet, img);
  }
  if (img.complete && img.naturalWidth > 0) return img;
  return null;
}

/** Start loading a figure (and optional gear) as soon as we know the id. */
export function prefetchAvatar(loadout: AvatarLoadout | string): void {
  const spec = typeof loadout === 'string' ? { figure: loadout } : loadout;
  ensureImage(figureSheetName(spec.figure));
  if (spec.lower) ensureImage(spec.lower);
  if (spec.core) ensureImage(spec.core);
  if (spec.head) ensureImage(spec.head);
}

function cellSize(img: HTMLImageElement): { w: number; h: number; rows: number } {
  const w = Math.max(1, Math.round(img.naturalWidth / COLS));
  const rows = img.naturalHeight >= w * 8 ? 8 : Math.max(WALK_ROWS, Math.round(img.naturalHeight / w));
  const h = Math.max(1, Math.round(img.naturalHeight / rows));
  return { w, h, rows };
}

function frameIndex(opts: DrawAvatarOpts, attackRows: boolean): { col: number; row: number } {
  const facing = Math.max(0, Math.min(3, opts.facing ?? 0));
  const attacking = opts.action === 'attack' && attackRows;
  if (attacking) {
    const elapsed = Math.max(0, opts.attackElapsedMs ?? 0);
    const col = Math.min(1, Math.floor((elapsed / 280) * 2));
    return { col, row: WALK_ROWS + facing };
  }
  if (opts.action === 'walk') {
    const dist = opts.distance ?? 0;
    const col = Math.floor((dist / 0.9) * COLS) % COLS;
    return { col, row: facing };
  }
  return { col: 0, row: facing };
}

function blitSheet(
  ctx: CanvasRenderingContext2D,
  img: HTMLImageElement,
  x: number,
  y: number,
  tileSize: number,
  opts: DrawAvatarOpts
): boolean {
  const { w, h, rows } = cellSize(img);
  const attackRows = rows >= 8;
  const { col, row } = frameIndex(opts, attackRows);
  const heightScale = opts.heightScale ?? 1.45;
  const dw = (w / 32) * tileSize;
  const dh = (h / 32) * tileSize * heightScale;
  const dx = Math.round(x - dw / 2);
  const dy = Math.round(y - dh + tileSize * 0.12);
  ctx.imageSmoothingEnabled = false;
  ctx.drawImage(img, col * w, row * h, w, h, dx, dy, Math.round(dw), Math.round(dh));
  return true;
}

/**
 * Draw a player or NPC avatar. Returns true if the base sheet was blitted.
 * Gear layers are optional and never block the base.
 */
export function drawAvatar(
  ctx: CanvasRenderingContext2D,
  loadout: AvatarLoadout | string,
  x: number,
  y: number,
  tileSize: number,
  opts: DrawAvatarOpts = {}
): boolean {
  const spec = typeof loadout === 'string' ? { figure: loadout } : loadout;
  prefetchAvatar(spec);
  const base = ensureImage(figureSheetName(spec.figure));
  if (!base) return false;
  blitSheet(ctx, base, x, y, tileSize, opts);
  for (const layer of [spec.lower, spec.core, spec.head]) {
    if (!layer) continue;
    const img = ensureImage(layer);
    if (img) blitSheet(ctx, img, x, y, tileSize, opts);
  }
  return true;
}
