/**
 * =============================================================================
 * SettingsPanel.tsx — Player Settings (from Pause Menu)
 * =============================================================================
 *
 * Loads GET /api/gameplay/settings and saves POST /api/gameplay/settings.
 * Name, offline mode, master volume stub, glyph overlay, FPS overlay.
 * =============================================================================
 */
'use client';

import { useEffect, useState, useCallback, type CSSProperties } from 'react';

import { normalizeCursor, type CursorStyle } from '@/lib/engine/cursor';

export interface GameSettings {
  displayName: string;
  offlineMode: boolean;
  masterVolume: number;
  showGlyphOverlay: boolean;
  showFps: boolean;
  devMode: boolean;
  showHudOverworld: boolean;
  showHudDungeon: boolean;
  cursorOverworld: CursorStyle;
  cursorDungeon: CursorStyle;
}

interface SettingsPanelProps {
  onClose: () => void;
  onSaved?: (settings: GameSettings) => void;
}

const labelStyle: CSSProperties = {
  color: '#9a8b74', fontSize: '0.7rem', display: 'block', marginBottom: 4,
};

const inputStyle: CSSProperties = {
  width: '100%',
  background: '#1a1410',
  border: '1px solid #4a3d2e',
  borderRadius: 4,
  padding: '0.45rem 0.6rem',
  color: '#e8dcc8',
  fontSize: '0.85rem',
  outline: 'none',
  fontFamily: 'Georgia, serif',
};

const rowStyle: CSSProperties = {
  display: 'flex', alignItems: 'center', justifyContent: 'space-between',
  gap: 12, marginBottom: 12,
};

export default function SettingsPanel({ onClose, onSaved }: SettingsPanelProps) {
  const [settings, setSettings] = useState<GameSettings>({
    displayName: '',
    offlineMode: false,
    masterVolume: 1,
    showGlyphOverlay: true,
    showFps: false,
    devMode: false,
    showHudOverworld: false,
    showHudDungeon: false,
    cursorOverworld: 'crosshair',
    cursorDungeon: 'crosshair',
  });
  const [status, setStatus] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    const load = async () => {
      try {
        const res = await fetch('/api/gameplay/settings');
        if (!res.ok) return;
        const data = await res.json();
        setSettings({
          displayName: data.displayName ?? '',
          offlineMode: !!data.offlineMode,
          masterVolume: typeof data.masterVolume === 'number' ? data.masterVolume : 1,
          showGlyphOverlay: data.showGlyphOverlay !== false,
          showFps: !!data.showFps,
          devMode: !!data.devMode,
          showHudOverworld: !!data.showHudOverworld,
          showHudDungeon: !!data.showHudDungeon,
          cursorOverworld: normalizeCursor(data.cursorOverworld),
          cursorDungeon: normalizeCursor(data.cursorDungeon),
        });
      } catch { /* ignore */ }
    };
    load();
  }, []);

  const handleSave = useCallback(async () => {
    setSaving(true);
    try {
      const res = await fetch('/api/gameplay/settings', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          displayName: settings.displayName.trim() || null,
          offlineMode: settings.offlineMode,
          masterVolume: settings.masterVolume,
          showGlyphOverlay: settings.showGlyphOverlay,
          showFps: settings.showFps,
          devMode: settings.devMode,
          showHudOverworld: settings.showHudOverworld,
          showHudDungeon: settings.showHudDungeon,
          cursorOverworld: settings.cursorOverworld,
          cursorDungeon: settings.cursorDungeon,
        }),
      });
      if (res.ok) {
        const saved = await res.json();
        const next: GameSettings = {
          displayName: saved.displayName ?? settings.displayName,
          offlineMode: saved.offlineMode ?? settings.offlineMode,
          masterVolume: saved.masterVolume ?? settings.masterVolume,
          showGlyphOverlay: saved.showGlyphOverlay !== false,
          showFps: !!saved.showFps,
          devMode: !!saved.devMode,
          showHudOverworld: !!saved.showHudOverworld,
          showHudDungeon: !!saved.showHudDungeon,
          cursorOverworld: normalizeCursor(saved.cursorOverworld ?? settings.cursorOverworld),
          cursorDungeon: normalizeCursor(saved.cursorDungeon ?? settings.cursorDungeon),
        };
        setSettings(next);
        onSaved?.(next);
        setStatus('Saved.');
      } else {
        setStatus('Could not save.');
      }
    } catch {
      setStatus('Could not save.');
    } finally {
      setSaving(false);
      setTimeout(() => setStatus(null), 2000);
    }
  }, [settings, onSaved]);

  return (
    <div style={{
      position: 'absolute', inset: 0,
      display: 'flex', alignItems: 'center', justifyContent: 'center',
      background: 'rgba(0, 0, 0, 0.65)',
      zIndex: 1100,
    }} onClick={onClose}>
      <div style={{
        padding: '24px 28px',
        background: 'rgba(13, 15, 7, 0.97)',
        border: '1px solid #4a3520',
        borderRadius: 8,
        minWidth: 340,
        maxWidth: 440,
        maxHeight: '90vh',
        overflowY: 'auto',
      }} onClick={e => e.stopPropagation()}>
        <div style={{
          display: 'flex', justifyContent: 'space-between', alignItems: 'center',
          marginBottom: 16,
        }}>
          <h3 style={{
            color: '#c9a84c', fontFamily: 'Georgia, serif', margin: 0,
            fontSize: '1.05rem', letterSpacing: '0.08em',
          }}>
            ⚙  Settings
          </h3>
          <button onClick={onClose} style={{
            background: 'none', border: 'none', color: '#6a5d4a', cursor: 'pointer', fontSize: '1.2rem',
          }}>✕</button>
        </div>

        <div style={{ marginBottom: 14 }}>
          <label style={labelStyle}>Display name</label>
          <input
            type="text"
            value={settings.displayName}
            onChange={e => setSettings(s => ({ ...s, displayName: e.target.value }))}
            style={inputStyle}
          />
        </div>

        <label style={{ ...rowStyle, cursor: 'pointer' }}>
          <span style={{ color: '#e8dcc8', fontSize: '0.8rem' }}>Offline mode</span>
          <input
            type="checkbox"
            checked={settings.offlineMode}
            onChange={e => setSettings(s => ({ ...s, offlineMode: e.target.checked }))}
          />
        </label>

        <div style={{ marginBottom: 14 }}>
          <label style={labelStyle}>
            Master volume (stub) — {Math.round(settings.masterVolume * 100)}%
          </label>
          <input
            type="range"
            min={0}
            max={1}
            step={0.05}
            value={settings.masterVolume}
            onChange={e => setSettings(s => ({ ...s, masterVolume: Number(e.target.value) }))}
            style={{ width: '100%', accentColor: '#c9a84c' }}
          />
        </div>

        <label style={{ ...rowStyle, cursor: 'pointer' }}>
          <span style={{ color: '#e8dcc8', fontSize: '0.8rem' }}>Show glyph overlay</span>
          <input
            type="checkbox"
            checked={settings.showGlyphOverlay}
            onChange={e => setSettings(s => ({ ...s, showGlyphOverlay: e.target.checked }))}
          />
        </label>

        <label style={{ ...rowStyle, cursor: 'pointer' }}>
          <span style={{ color: '#e8dcc8', fontSize: '0.8rem' }}>Show FPS</span>
          <input
            type="checkbox"
            checked={settings.showFps}
            onChange={e => setSettings(s => ({ ...s, showFps: e.target.checked }))}
          />
        </label>

        <div style={{ marginBottom: 14 }}>
          <label style={labelStyle}>Cursor in overworld</label>
          <select
            value={settings.cursorOverworld}
            onChange={e => setSettings(s => ({ ...s, cursorOverworld: normalizeCursor(e.target.value) }))}
            style={inputStyle}
          >
            <option value="off">Off</option>
            <option value="crosshair">Crosshair</option>
            <option value="sword">Sword</option>
            <option value="hand">Hand</option>
          </select>
        </div>

        <div style={{ marginBottom: 14 }}>
          <label style={labelStyle}>Cursor in dungeon</label>
          <select
            value={settings.cursorDungeon}
            onChange={e => setSettings(s => ({ ...s, cursorDungeon: normalizeCursor(e.target.value) }))}
            style={inputStyle}
          >
            <option value="off">Off</option>
            <option value="crosshair">Crosshair</option>
            <option value="sword">Sword</option>
            <option value="hand">Hand</option>
          </select>
        </div>

        <div style={{
          margin: '16px 0 12px', paddingTop: 12, borderTop: '1px solid #4a3d2e',
          color: '#c9a84c', fontSize: '0.7rem', letterSpacing: '0.14em', textTransform: 'uppercase',
        }}>
          DEV
        </div>

        <label style={{ ...rowStyle, cursor: 'pointer' }}>
          <span style={{ color: '#e8dcc8', fontSize: '0.8rem' }}>
            Dev
            <span style={{ display: 'block', color: '#6a5d4a', fontSize: '0.65rem', marginTop: 2 }}>
              Reveal the map and click-to-travel. Local only.
            </span>
          </span>
          <input
            type="checkbox"
            checked={settings.devMode}
            onChange={e => setSettings(s => ({ ...s, devMode: e.target.checked }))}
          />
        </label>

        <label style={{ ...rowStyle, cursor: 'pointer' }}>
          <span style={{ color: '#e8dcc8', fontSize: '0.8rem' }}>
            HUD in overworld
            <span style={{ display: 'block', color: '#6a5d4a', fontSize: '0.65rem', marginTop: 2 }}>
              HP, stamina, XP, and ability bar. Default off.
            </span>
          </span>
          <input
            type="checkbox"
            checked={settings.showHudOverworld}
            onChange={e => setSettings(s => ({ ...s, showHudOverworld: e.target.checked }))}
          />
        </label>

        <label style={{ ...rowStyle, cursor: 'pointer' }}>
          <span style={{ color: '#e8dcc8', fontSize: '0.8rem' }}>
            HUD in dungeon
            <span style={{ display: 'block', color: '#6a5d4a', fontSize: '0.65rem', marginTop: 2 }}>
              Surrounding dungeon chrome. Default off.
            </span>
          </span>
          <input
            type="checkbox"
            checked={settings.showHudDungeon}
            onChange={e => setSettings(s => ({ ...s, showHudDungeon: e.target.checked }))}
          />
        </label>

        {status && (
          <div style={{
            color: status === 'Saved.' ? '#4a8c3f' : '#c05050',
            fontSize: '0.7rem', textAlign: 'center', marginBottom: 8,
          }}>
            {status}
          </div>
        )}

        <div style={{ display: 'flex', gap: 10, justifyContent: 'center', marginTop: 8 }}>
          <button onClick={handleSave} disabled={saving} style={{
            padding: '8px 20px', background: '#2a3a2a', border: '1px solid #4a8c3f',
            borderRadius: 4, color: '#4a8c3f', cursor: 'pointer', fontSize: '0.85rem',
            fontFamily: 'Georgia, serif',
          }}>
            {saving ? 'Saving…' : 'Save'}
          </button>
          <button onClick={onClose} style={{
            padding: '8px 20px', background: '#3a3520', border: '1px solid #6a5d4a',
            borderRadius: 4, color: '#6a5d4a', cursor: 'pointer', fontSize: '0.85rem',
            fontFamily: 'Georgia, serif',
          }}>
            Back
          </button>
        </div>
      </div>
    </div>
  );
}
