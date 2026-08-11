/**
 * =============================================================================
 * useOverworldSocket.ts — WebSocket Hook for the Overworld Server
 * =============================================================================
 *
 * Connects to the matchmaking/overworld server at /ws/overworld.
 * Similar to useWebSocket but uses the overworld message protocol.
 * =============================================================================
 */
'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { OverworldMessage, OwMessageTypes } from '@/lib/overworld-messages';

export type ConnectionStatus = 'disconnected' | 'connecting' | 'connected' | 'error';

export interface UseOverworldSocketOptions {
  playerName: string;
  /** Overworld server URL (defaults to port 5100 on same host) */
  serverUrl?: string;
  autoReconnect?: boolean;
}

export interface UseOverworldSocketReturn {
  status: ConnectionStatus;
  playerId: string | null;
  send: (message: OverworldMessage) => void;
  connect: () => void;
  disconnect: () => void;
  latency: number;
  onMessage: (handler: (message: OverworldMessage) => void) => () => void;
}

export function useOverworldSocket(options: UseOverworldSocketOptions): UseOverworldSocketReturn {
  const { playerName, serverUrl, autoReconnect = true } = options;

  const [status, setStatus] = useState<ConnectionStatus>('disconnected');
  const [playerId, setPlayerId] = useState<string | null>(null);
  const [latency, setLatency] = useState(0);

  const wsRef = useRef<WebSocket | null>(null);
  const handlersRef = useRef<Set<(msg: OverworldMessage) => void>>(new Set());
  const reconnectAttemptsRef = useRef(0);
  const reconnectTimerRef = useRef<NodeJS.Timeout | null>(null);
  const pingIntervalRef = useRef<NodeJS.Timeout | null>(null);

  const getWsUrl = useCallback(() => {
    if (serverUrl) {
      const url = new URL(serverUrl);
      const protocol = url.protocol === 'https:' ? 'wss:' : 'ws:';
      return `${protocol}//${url.host}/ws/overworld?name=${encodeURIComponent(playerName)}`;
    }
    // Default: connect to the SAME origin (local Carcosa.Server handles everything via P2P mesh)
    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    return `${protocol}//${window.location.host}/ws/overworld?name=${encodeURIComponent(playerName)}`;
  }, [serverUrl, playerName]);

  const handleMessage = useCallback((event: MessageEvent) => {
    try {
      const message: OverworldMessage = JSON.parse(event.data);

      // Extract player ID from first player_joined message
      if (message.type === OwMessageTypes.PlayerJoined && message.playerJoined && !playerId) {
        setPlayerId(message.playerJoined.playerId);
      }

      // Handle pong for latency measurement
      if (message.type === OwMessageTypes.Pong && message.pong) {
        setLatency(Date.now() - message.pong.clientTimestamp);
      }

      // Forward to all handlers
      handlersRef.current.forEach(handler => handler(message));
    } catch (e) {
      console.error('[OW-WS] Failed to parse message:', e);
    }
  }, [playerId]);

  const cleanup = useCallback(() => {
    if (pingIntervalRef.current) {
      clearInterval(pingIntervalRef.current);
      pingIntervalRef.current = null;
    }
    if (reconnectTimerRef.current) {
      clearTimeout(reconnectTimerRef.current);
      reconnectTimerRef.current = null;
    }
  }, []);

  const connect = useCallback(() => {
    if (wsRef.current) {
      wsRef.current.close();
      wsRef.current = null;
    }
    cleanup();
    setStatus('connecting');

    const url = getWsUrl();
    try {
      const ws = new WebSocket(url);
      wsRef.current = ws;

      ws.onopen = () => {
        setStatus('connected');
        reconnectAttemptsRef.current = 0;
        console.log('[OW-WS] Connected to overworld server');

        // Ping every 5s for latency
        pingIntervalRef.current = setInterval(() => {
          if (ws.readyState === WebSocket.OPEN) {
            const msg: OverworldMessage = {
              type: OwMessageTypes.Ping,
              ping: { clientTimestamp: Date.now() },
            };
            ws.send(JSON.stringify(msg));
          }
        }, 5000);
      };

      ws.onmessage = handleMessage;

      ws.onclose = () => {
        cleanup();
        setStatus('disconnected');
        if (autoReconnect && reconnectAttemptsRef.current < 5) {
          reconnectAttemptsRef.current++;
          reconnectTimerRef.current = setTimeout(connect, 2000);
        }
      };

      ws.onerror = () => {
        setStatus('error');
      };
    } catch {
      setStatus('error');
    }
  }, [getWsUrl, handleMessage, autoReconnect, cleanup]);

  const disconnect = useCallback(() => {
    reconnectAttemptsRef.current = 5;
    cleanup();
    if (wsRef.current) {
      wsRef.current.close(1000, 'Client disconnect');
      wsRef.current = null;
    }
    setStatus('disconnected');
    setPlayerId(null);
  }, [cleanup]);

  const send = useCallback((message: OverworldMessage) => {
    if (wsRef.current?.readyState === WebSocket.OPEN) {
      wsRef.current.send(JSON.stringify(message));
    }
  }, []);

  const onMessage = useCallback((handler: (msg: OverworldMessage) => void) => {
    handlersRef.current.add(handler);
    return () => { handlersRef.current.delete(handler); };
  }, []);

  useEffect(() => {
    return () => {
      cleanup();
      if (wsRef.current) {
        wsRef.current.close(1000, 'Unmount');
        wsRef.current = null;
      }
    };
  }, [cleanup]);

  return { status, playerId, send, connect, disconnect, latency, onMessage };
}
