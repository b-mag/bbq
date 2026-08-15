/**
 * =============================================================================
 * ui-stack.ts — Layered UI Panel Management (ESC Key System)
 * =============================================================================
 *
 * Manages a stack of open UI panels. ESC key always closes the topmost panel.
 * When no panels are open, ESC shows the pause menu.
 *
 * PANEL PRIORITY (topmost closes first):
 *   pause-menu > settings > inventory > ability-select > flame-offering > admin-message > chat
 *
 * WHY A STACK:
 * Multiple panels can be open simultaneously (e.g., chat + admin message).
 * ESC should always close the most recently opened one — LIFO behavior.
 * A stack naturally provides this without complex priority logic.
 *
 * USAGE:
 *   - Components call pushPanel() when they open
 *   - Components call popPanel() when they close (or ESC triggers it)
 *   - OverworldView listens for ESC and calls handleEscape()
 *   - handleEscape() returns which panel was closed (or 'pause-menu' if opened)
 * =============================================================================
 */

export type PanelId = 'chat' | 'admin-message' | 'inventory' | 'ability-select' | 'pause-menu' | 'settings' | 'flame-offering' | 'inspect' | 'cryptol-shop' | 'dialogue' | 'overworld-map';

/** Simple panel stack — last in, first out. */
let panelStack: PanelId[] = [];

/** Subscribers notified when stack changes. */
let listeners: (() => void)[] = [];

/** Push a panel onto the stack (opens it). No-op if already on stack. */
export function pushPanel(id: PanelId): void {
  if (!panelStack.includes(id)) {
    panelStack.push(id);
    notifyListeners();
  }
}

/** Pop the top panel from the stack (closes it). Returns the closed panel ID. */
export function popPanel(): PanelId | null {
  const closed = panelStack.pop() ?? null;
  if (closed) notifyListeners();
  return closed;
}

/** Remove a specific panel from anywhere in the stack (for explicit close). */
export function removePanel(id: PanelId): void {
  const index = panelStack.indexOf(id);
  if (index >= 0) {
    panelStack.splice(index, 1);
    notifyListeners();
  }
}

/** Get the topmost panel (what ESC would close). */
export function topPanel(): PanelId | null {
  return panelStack.length > 0 ? panelStack[panelStack.length - 1] : null;
}

/** Check if a specific panel is currently open. */
export function isOpen(id: PanelId): boolean {
  return panelStack.includes(id);
}

/** Get the full stack (for debugging). */
export function getStack(): readonly PanelId[] {
  return panelStack;
}

/** Clear all panels (reset). */
export function clearStack(): void {
  panelStack = [];
  notifyListeners();
}

/**
 * Handle ESC key press. Returns the action taken.
 * - If panels are open: closes the topmost panel, returns its ID
 * - If no panels open: opens pause-menu, returns 'pause-menu'
 */
export function handleEscape(): PanelId {
  if (panelStack.length > 0) {
    return popPanel()!;
  } else {
    pushPanel('pause-menu');
    return 'pause-menu';
  }
}

/** Subscribe to stack changes. Returns unsubscribe function. */
export function subscribe(listener: () => void): () => void {
  listeners.push(listener);
  return () => {
    listeners = listeners.filter(l => l !== listener);
  };
}

function notifyListeners(): void {
  for (const l of listeners) l();
}
