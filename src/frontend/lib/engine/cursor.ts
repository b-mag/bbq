/**
 * Canvas cursor styles shared by overworld and dungeon.
 * Persisted as: off | crosshair | sword | hand
 */

export type CursorStyle = 'off' | 'crosshair' | 'sword' | 'hand';

const SWORD_CURSOR =
  'url("data:image/svg+xml;utf8,' +
  encodeURIComponent(
    '<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24">' +
    '<path fill="none" stroke="%23e8dcc8" stroke-width="1.6" stroke-linecap="round" ' +
    'd="M5 19 L15 9 M14 8 L16 10 M13 11 L11 13 M7 17 L9 15"/>' +
    '<path fill="%23c9a84c" d="M14.2 7.2 l2.6 2.6 -1.1 1.1 -2.6 -2.6 z"/>' +
    '</svg>'
  ) +
  '") 4 4, crosshair';

export function normalizeCursor(value: unknown): CursorStyle {
  return value === 'off' || value === 'crosshair' || value === 'sword' || value === 'hand'
    ? value
    : 'crosshair';
}

export function canvasCursorCss(style: CursorStyle | string | undefined): string {
  switch (style) {
    case 'off':
      return 'none';
    case 'sword':
      return SWORD_CURSOR;
    case 'hand':
      return 'pointer';
    default:
      return 'crosshair';
  }
}
