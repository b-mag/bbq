/**
 * Per-player fog of war. Knowledge is local — never sent over the P2P mesh.
 * Packed as 4×4 tile chunks so a 640×640 world is 3200 bytes.
 */
const CHUNK = 4;
const REVEAL_RADIUS = 12;

export class FogOfWar {
  readonly worldW: number;
  readonly worldH: number;
  readonly chunksW: number;
  readonly chunksH: number;
  private bits: Uint8Array;
  dirty = false;

  constructor(worldW: number, worldH: number, packed?: Uint8Array | null) {
    this.worldW = worldW;
    this.worldH = worldH;
    this.chunksW = Math.ceil(worldW / CHUNK);
    this.chunksH = Math.ceil(worldH / CHUNK);
    const n = Math.ceil((this.chunksW * this.chunksH) / 8);
    this.bits = packed && packed.length === n ? packed.slice() : new Uint8Array(n);
  }

  static fromBase64(worldW: number, worldH: number, b64?: string | null): FogOfWar {
    if (!b64) return new FogOfWar(worldW, worldH);
    try {
      const raw = atob(b64);
      const bytes = new Uint8Array(raw.length);
      for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
      return new FogOfWar(worldW, worldH, bytes);
    } catch {
      return new FogOfWar(worldW, worldH);
    }
  }

  clone(): FogOfWar {
    return new FogOfWar(this.worldW, this.worldH, this.bits);
  }

  toBase64(): string {
    let s = '';
    for (let i = 0; i < this.bits.length; i++) s += String.fromCharCode(this.bits[i]);
    return btoa(s);
  }

  revealAround(tx: number, ty: number, radius = REVEAL_RADIUS): void {
    const minX = Math.max(0, Math.floor(tx - radius));
    const maxX = Math.min(this.worldW - 1, Math.floor(tx + radius));
    const minY = Math.max(0, Math.floor(ty - radius));
    const maxY = Math.min(this.worldH - 1, Math.floor(ty + radius));
    const r2 = radius * radius;
    for (let y = minY; y <= maxY; y += CHUNK) {
      for (let x = minX; x <= maxX; x += CHUNK) {
        const dx = x + CHUNK * 0.5 - tx;
        const dy = y + CHUNK * 0.5 - ty;
        if (dx * dx + dy * dy > r2 + CHUNK * CHUNK) continue;
        this.markChunk(Math.floor(x / CHUNK), Math.floor(y / CHUNK));
      }
    }
    // Always reveal the standing chunk
    this.markChunk(Math.floor(tx / CHUNK), Math.floor(ty / CHUNK));
  }

  isRevealed(tx: number, ty: number): boolean {
    return this.isChunkRevealed(Math.floor(tx / CHUNK), Math.floor(ty / CHUNK));
  }

  isChunkRevealed(cx: number, cy: number): boolean {
    if (cx < 0 || cy < 0 || cx >= this.chunksW || cy >= this.chunksH) return false;
    const i = cy * this.chunksW + cx;
    return (this.bits[i >> 3] & (1 << (i & 7))) !== 0;
  }

  private markChunk(cx: number, cy: number): void {
    if (cx < 0 || cy < 0 || cx >= this.chunksW || cy >= this.chunksH) return;
    const i = cy * this.chunksW + cx;
    const bit = 1 << (i & 7);
    const prev = this.bits[i >> 3];
    const next = prev | bit;
    if (next !== prev) {
      this.bits[i >> 3] = next;
      this.dirty = true;
    }
  }
}
