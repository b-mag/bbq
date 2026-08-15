/**
 * World-space sprite animation clocks.
 * Walk cycles advance by tiles traveled (zoom-independent).
 * Attack windows are wall-clock so a strike always plays the same length.
 */

const attackUntil = new Map<string, number>();
const walked = new Map<string, number>();
const lastPos = new Map<string, { x: number; y: number }>();

export const DEFAULT_ATTACK_MS = 280;

export function noteAttack(id: string, durationMs: number = DEFAULT_ATTACK_MS): void {
  attackUntil.set(id, performance.now() + durationMs);
}

export function attackElapsedMs(id: string, durationMs: number = DEFAULT_ATTACK_MS): number {
  const until = attackUntil.get(id);
  if (until == null) return -1;
  const remaining = until - performance.now();
  if (remaining <= 0) {
    attackUntil.delete(id);
    return -1;
  }
  return durationMs - remaining;
}

export function isAttacking(id: string): boolean {
  return attackElapsedMs(id) >= 0;
}

/** Accumulate world-tile distance. Teleports (>4 tiles) are ignored. */
export function walkDistance(id: string, x: number, y: number): number {
  const prev = lastPos.get(id);
  lastPos.set(id, { x, y });
  const current = walked.get(id) ?? 0;
  if (!prev) return current;
  const step = Math.hypot(x - prev.x, y - prev.y);
  if (step > 4) return current;
  const next = current + step;
  walked.set(id, next);
  return next;
}

const lastCooldown = new Map<string, number>();

/** Play attack frames when remaining cooldown ticks jump (any max length). */
export function syncAttackFromCooldown(id: string, cooldown: number | undefined): void {
  if (cooldown == null) return;
  const prev = lastCooldown.get(id) ?? 0;
  lastCooldown.set(id, cooldown);
  if (cooldown > prev && cooldown >= 8) noteAttack(id);
}
