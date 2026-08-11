/**
 * =============================================================================
 * useP2POverworld.ts — Hook for P2P Overworld State (via Local Game Server)
 * =============================================================================
 *
 * In the P2P architecture, the frontend connects to its LOCAL Carcosa.Server
 * (same process, same port) which handles all mesh networking. The frontend
 * polls REST endpoints to get the merged world state:
 *
 *   /api/p2p/players → All visible players (local + remote peers)
 *   /api/p2p/status  → Mesh status (peer count, identity, latency)
 *   /api/p2p/shard   → Current world shard info
 *   /api/p2p/glyph   → Our Glyph code for sharing
 *
 * WHY POLLING (not WebSocket to local server):
 * The P2P state updates come at 20Hz from remote peers. The local server
 * aggregates them and exposes a snapshot via REST. Polling at 60ms (16Hz)
 * gives smooth rendering without the complexity of another WebSocket layer.
 * The existing /ws endpoint is still used for dungeon gameplay (combat).
 *
 * The frontend also sends the local player's position to the server via
 * POST /api/p2p/position so the server can broadcast it to peers.
 * =============================================================================
 */
'use client';

import { useState, useEffect, useCallback, useRef } from 'react';
import { OwPlayerState } from '@/lib/overworld-messages';

export interface P2PStatus {
  peerId: string;
  displayName: string;
  worldId: string;
  peerCount: number;
  gameVersion: string;
  protocolVersion: number;
  connectedPeers: { id: string; name: string; latency: number }[];
}

export interface ShardInfo {
  shardId: string;
  shardIndex: number;
  playerCount: number;
  maxPlayers: number;
  isAtCapacity: boolean;
}

export interface UseP2POverworldReturn {
  /** All visible players (local + remote) */
  players: OwPlayerState[];
  /** Mesh connection status */
  status: P2PStatus | null;
  /** Current world shard info */
  shard: ShardInfo | null;
  /** Our Glyph code for sharing */
  glyph: string;
  /** Whether data is loading */
  loading: boolean;
  /** Connect to a peer via Glyph code */
  connectViaGlyph: (code: string) => Promise<boolean>;
  /** Switch to a different world shard */
  switchShard: (shardId: string) => Promise<boolean>;
  /** Update our local player position (sent to server for mesh broadcast) */
  updatePosition: (x: number, y: number, vx: number, vy: number) => void;
}

/**
 * Hook that polls the local Carcosa.Server P2P endpoints for overworld state.
 */
export function useP2POverworld(): UseP2POverworldReturn {
  const [players, setPlayers] = useState<OwPlayerState[]>([]);
  const [status, setStatus] = useState<P2PStatus | null>(null);
  const [shard, setShard] = useState<ShardInfo | null>(null);
  const [glyph, setGlyph] = useState('');
  const [loading, setLoading] = useState(true);
  const pollIntervalRef = useRef<NodeJS.Timeout | null>(null);

  // Poll player state at ~16Hz (60ms) for smooth rendering
  useEffect(() => {
    const pollPlayers = async () => {
      try {
        const res = await fetch('/api/p2p/players');
        if (res.ok) {
          const data = await res.json();
          // Map the API response to OwPlayerState format
          const mapped: OwPlayerState[] = data.map((p: any) => ({
            id: p.id,
            name: p.name,
            x: p.x,
            y: p.y,
            velocityX: p.velocityX,
            velocityY: p.velocityY,
            status: p.status || 'exploring',
            partyId: p.partyId,
            isPartyLeader: p.isPartyLeader || false,
          }));
          setPlayers(mapped);
          setLoading(false);
        }
      } catch { /* Silently retry on next poll */ }
    };

    pollPlayers(); // Initial load
    pollIntervalRef.current = setInterval(pollPlayers, 60); // 16Hz

    return () => {
      if (pollIntervalRef.current) clearInterval(pollIntervalRef.current);
    };
  }, []);

  // Poll status and shard info at 2Hz (slower — doesn't change often)
  useEffect(() => {
    const pollMeta = async () => {
      try {
        const [statusRes, shardRes, glyphRes] = await Promise.allSettled([
          fetch('/api/p2p/status'),
          fetch('/api/p2p/shard'),
          fetch('/api/p2p/glyph'),
        ]);

        if (statusRes.status === 'fulfilled' && statusRes.value.ok) {
          setStatus(await statusRes.value.json());
        }
        if (shardRes.status === 'fulfilled' && shardRes.value.ok) {
          setShard(await shardRes.value.json());
        }
        if (glyphRes.status === 'fulfilled' && glyphRes.value.ok) {
          const data = await glyphRes.value.json();
          setGlyph(data.glyph || '');
        }
      } catch { /* Best effort */ }
    };

    pollMeta();
    const interval = setInterval(pollMeta, 500); // 2Hz
    return () => clearInterval(interval);
  }, []);

  // Connect to a peer via Glyph code
  const connectViaGlyph = useCallback(async (code: string): Promise<boolean> => {
    try {
      const res = await fetch('/api/p2p/glyph/connect', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ glyph: code }),
      });
      return res.ok;
    } catch {
      return false;
    }
  }, []);

  // Switch world shard
  const switchShard = useCallback(async (shardId: string): Promise<boolean> => {
    try {
      const res = await fetch('/api/p2p/shard/switch', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ shardId }),
      });
      return res.ok;
    } catch {
      return false;
    }
  }, []);

  // Send local player position to server for mesh broadcast
  const updatePosition = useCallback((x: number, y: number, vx: number, vy: number) => {
    // POST to local server — it broadcasts to all mesh peers
    fetch('/api/p2p/position', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ x, y, velocityX: vx, velocityY: vy }),
    }).catch(() => { /* Best effort */ });
  }, []);

  return {
    players,
    status,
    shard,
    glyph,
    loading,
    connectViaGlyph,
    switchShard,
    updatePosition,
  };
}
