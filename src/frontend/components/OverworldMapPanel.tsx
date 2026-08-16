/**
 * Overworld map overlay (M). Fog of war hides unvisited chunks unless Dev mode.
 * Dev: click a tile to fast-travel. ESC / M closes.
 */
'use client';

import { useEffect, useRef, useCallback } from 'react';
import { OverworldGameMap, OW_TILE_COLORS, OwTileType, getOwTile } from '@/lib/overworld-map';
import { FogOfWar } from '@/lib/engine/fogOfWar';
import { OwPlayerState, OwLandmarkData } from '@/lib/overworld-messages';

interface OverworldMapPanelProps {
  map: OverworldGameMap;
  fog: FogOfWar;
  devMode: boolean;
  localX: number;
  localY: number;
  localId: string | null;
  players: OwPlayerState[];
  landmarks: OwLandmarkData[];
  seeBeyond?: { x: number; y: number; label: string; active: boolean } | null;
  onClose: () => void;
  onDevTeleport?: (x: number, y: number) => void;
}

export default function OverworldMapPanel({
  map, fog, devMode, localX, localY, localId, players, landmarks, seeBeyond, onClose, onDevTeleport,
}: OverworldMapPanelProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const layoutRef = useRef({ scale: 1, ox: 0, oy: 0, dw: 0, dh: 0 });

  const paint = useCallback(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const cssW = canvas.clientWidth;
    const cssH = canvas.clientHeight;
    const dpr = Math.min(2, window.devicePixelRatio || 1);
    canvas.width = Math.max(1, Math.floor(cssW * dpr));
    canvas.height = Math.max(1, Math.floor(cssH * dpr));
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.imageSmoothingEnabled = false;

    ctx.fillStyle = '#070605';
    ctx.fillRect(0, 0, cssW, cssH);

    const pad = 8;
    const scale = Math.min((cssW - pad * 2) / map.width, (cssH - pad * 2) / map.height);
    const dw = map.width * scale;
    const dh = map.height * scale;
    const ox = (cssW - dw) / 2;
    const oy = (cssH - dh) / 2;
    layoutRef.current = { scale, ox, oy, dw, dh };

    ctx.fillStyle = '#0a0806';
    ctx.fillRect(ox, oy, dw, dh);

    const step = scale < 0.6 ? 2 : 1;
    for (let y = 0; y < map.height; y += step) {
      for (let x = 0; x < map.width; x += step) {
        if (!devMode && !fog.isRevealed(x, y)) continue;
        const tile = getOwTile(map, x, y);
        ctx.fillStyle = OW_TILE_COLORS[tile] || OW_TILE_COLORS[OwTileType.Grass];
        ctx.fillRect(ox + x * scale, oy + y * scale, scale * step + 0.5, scale * step + 0.5);
      }
    }

    if (!devMode) {
      ctx.fillStyle = 'rgba(4, 3, 2, 0.92)';
      const cw = 4;
      const chunksW = Math.ceil(map.width / cw);
      const chunksH = Math.ceil(map.height / cw);
      for (let cy = 0; cy < chunksH; cy++) {
        for (let cx = 0; cx < chunksW; cx++) {
          if (fog.isChunkRevealed(cx, cy)) continue;
          ctx.fillRect(ox + cx * cw * scale, oy + cy * cw * scale, cw * scale + 0.5, cw * scale + 0.5);
        }
      }
    }

    for (const lm of landmarks) {
      if (!devMode && !fog.isRevealed(lm.x, lm.y)) continue;
      ctx.fillStyle = 'rgba(201, 168, 76, 0.85)';
      ctx.beginPath();
      ctx.arc(ox + lm.x * scale, oy + lm.y * scale, Math.max(2, scale * 1.6), 0, Math.PI * 2);
      ctx.fill();
    }

    for (const p of players) {
      if (p.id === localId) continue;
      if (p.status === 'in_dungeon') continue;
      if (!devMode && !fog.isRevealed(p.x, p.y)) continue;
      ctx.fillStyle = '#8b5fbf';
      ctx.beginPath();
      ctx.arc(ox + p.x * scale, oy + p.y * scale, Math.max(2.5, scale * 2), 0, Math.PI * 2);
      ctx.fill();
    }

    if (seeBeyond) {
      const mx = ox + seeBeyond.x * scale;
      const my = oy + seeBeyond.y * scale;
      const pulse = seeBeyond.active ? 0.55 + 0.45 * Math.sin(performance.now() / 280) : 0.45;
      ctx.strokeStyle = `rgba(201, 168, 76, ${pulse})`;
      ctx.fillStyle = `rgba(232, 208, 128, ${seeBeyond.active ? pulse : 0.55})`;
      ctx.lineWidth = 2;
      ctx.beginPath();
      ctx.arc(mx, my, Math.max(5, scale * 3.4), 0, Math.PI * 2);
      ctx.fill();
      ctx.stroke();
      if (seeBeyond.active) {
        ctx.strokeStyle = `rgba(201, 168, 76, ${0.35 * pulse})`;
        ctx.beginPath();
        ctx.arc(mx, my, Math.max(9, scale * 6.5), 0, Math.PI * 2);
        ctx.stroke();
      }
    }

    ctx.fillStyle = '#e8d080';
    ctx.strokeStyle = '#1a1410';
    ctx.lineWidth = 1.5;
    ctx.beginPath();
    ctx.arc(ox + localX * scale, oy + localY * scale, Math.max(3, scale * 2.4), 0, Math.PI * 2);
    ctx.fill();
    ctx.stroke();
  }, [map, fog, devMode, localX, localY, localId, players, landmarks, seeBeyond]);

  useEffect(() => {
    paint();
    if (!seeBeyond?.active) return;
    let raf = 0;
    const loop = () => {
      paint();
      raf = requestAnimationFrame(loop);
    };
    raf = requestAnimationFrame(loop);
    return () => cancelAnimationFrame(raf);
  }, [paint, seeBeyond?.active]);

  const handleClick = (e: React.MouseEvent<HTMLCanvasElement>) => {
    if (!devMode || !onDevTeleport) return;
    const canvas = canvasRef.current;
    if (!canvas) return;
    const rect = canvas.getBoundingClientRect();
    const mx = e.clientX - rect.left;
    const my = e.clientY - rect.top;
    const { scale, ox, oy, dw, dh } = layoutRef.current;
    if (mx < ox || my < oy || mx > ox + dw || my > oy + dh) return;
    onDevTeleport((mx - ox) / scale, (my - oy) / scale);
  };

  return (
    <div
      style={{
        position: 'absolute', inset: 0,
        display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
        background: 'rgba(0, 0, 0, 0.78)',
        zIndex: 1050,
      }}
      onClick={onClose}
    >
      <div
        style={{
          width: 'min(92vw, 820px)',
          height: 'min(86vh, 820px)',
          display: 'flex',
          flexDirection: 'column',
          background: 'rgba(13, 15, 7, 0.97)',
          border: '1px solid #4a3520',
          borderRadius: 8,
          padding: 14,
        }}
        onClick={e => e.stopPropagation()}
      >
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', marginBottom: 8 }}>
          <h3 style={{
            color: '#c9a84c', fontFamily: 'Georgia, serif', margin: 0,
            fontSize: '1.05rem', letterSpacing: '0.08em',
          }}>
            The Map of Carcosa
          </h3>
          <span style={{ color: '#6a5d4a', fontSize: '0.7rem' }}>
          {devMode ? 'Dev: click to travel  ·  ' : ''}
            {seeBeyond ? `See Beyond: ${seeBeyond.label}${seeBeyond.active ? ' (pulsing)' : ''}  ·  ` : ''}
            M / ESC to close
          </span>
        </div>
        <canvas
          ref={canvasRef}
          onClick={handleClick}
          style={{
            flex: 1,
            width: '100%',
            minHeight: 0,
            cursor: devMode ? 'crosshair' : 'default',
            borderRadius: 4,
            border: '1px solid #2a2218',
          }}
        />
      </div>
    </div>
  );
}
