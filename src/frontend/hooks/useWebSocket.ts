'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { GameMessage, MessageTypes } from '@/lib/messages';

export type ConnectionStatus = 'disconnected' | 'connecting' | 'connected' | 'error';

export interface UseWebSocketOptions {
  /** Player name to send on connection */
  playerName: string;
  /** Server URL (defaults to current host) */
  serverUrl?: string;
  /** Auto-reconnect on disconnect */
  autoReconnect?: boolean;
  /** Reconnect delay in ms */
  reconnectDelay?: number;
  /** Max reconnect attempts */
  maxReconnectAttempts?: number;
}

export interface UseWebSocketReturn {
  /** Current connection status */
  status: ConnectionStatus;
  /** Assigned player ID from server */
  playerId: string | null;
  /** Send a message to the server */
  send: (message: GameMessage) => void;
  /** Connect to the server */
  connect: () => void;
  /** Disconnect from the server */
  disconnect: () => void;
  /** Last measured latency in ms */
  latency: number;
  /** Register a message handler */
  onMessage: (handler: (message: GameMessage) => void) => () => void;
}

export function useWebSocket(options: UseWebSocketOptions): UseWebSocketReturn {
  const {
    playerName,
    serverUrl,
    autoReconnect = true,
    reconnectDelay = 2000,
    maxReconnectAttempts = 5,
  } = options;

  const [status, setStatus] = useState<ConnectionStatus>('disconnected');
  const [playerId, setPlayerId] = useState<string | null>(null);
  const [latency, setLatency] = useState(0);

  const wsRef = useRef<WebSocket | null>(null);
  const handlersRef = useRef<Set<(msg: GameMessage) => void>>(new Set());
  const reconnectAttemptsRef = useRef(0);
  const reconnectTimerRef = useRef<NodeJS.Timeout | null>(null);
  const pingIntervalRef = useRef<NodeJS.Timeout | null>(null);

  const getWsUrl = useCallback(() => {
    if (serverUrl) {
      // If a full URL is provided, use it
      const url = new URL(serverUrl);
      const protocol = url.protocol === 'https:' ? 'wss:' : 'ws:';
      return `${protocol}//${url.host}/ws?name=${encodeURIComponent(playerName)}`;
    }
    // Default: connect to same host
    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    return `${protocol}//${window.location.host}/ws?name=${encodeURIComponent(playerName)}`;
  }, [serverUrl, playerName]);

  const handleMessage = useCallback((event: MessageEvent) => {
    try {
      const message: GameMessage = JSON.parse(event.data);

      // Handle internal messages
      if (message.type === MessageTypes.PlayerJoined && message.playerJoined) {
        // If this is our own join confirmation (first message we receive)
        if (!playerId) {
          setPlayerId(message.playerJoined.playerId);
        }
      }

      if (message.type === MessageTypes.Pong && message.pong) {
        const now = Date.now();
        setLatency(now - message.pong.clientTimestamp);
      }

      // Forward to all registered handlers
      handlersRef.current.forEach(handler => handler(message));
    } catch (e) {
      console.error('[WS] Failed to parse message:', e);
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
    // Clean up any existing connection
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
        console.log('[WS] Connected to server');

        // Start ping interval for latency measurement
        pingIntervalRef.current = setInterval(() => {
          if (ws.readyState === WebSocket.OPEN) {
            const msg: GameMessage = {
              type: MessageTypes.Ping,
              ping: { clientTimestamp: Date.now() },
            };
            ws.send(JSON.stringify(msg));
          }
        }, 5000);
      };

      ws.onmessage = handleMessage;

      ws.onclose = (event) => {
        cleanup();
        setStatus('disconnected');
        console.log(`[WS] Disconnected (code: ${event.code})`);

        // Auto-reconnect logic
        if (autoReconnect && reconnectAttemptsRef.current < maxReconnectAttempts) {
          reconnectAttemptsRef.current++;
          console.log(`[WS] Reconnecting (attempt ${reconnectAttemptsRef.current}/${maxReconnectAttempts})...`);
          reconnectTimerRef.current = setTimeout(connect, reconnectDelay);
        }
      };

      ws.onerror = () => {
        setStatus('error');
        console.error('[WS] Connection error');
      };
    } catch (e) {
      setStatus('error');
      console.error('[WS] Failed to connect:', e);
    }
  }, [getWsUrl, handleMessage, autoReconnect, reconnectDelay, maxReconnectAttempts, cleanup]);

  const disconnect = useCallback(() => {
    reconnectAttemptsRef.current = maxReconnectAttempts; // Prevent auto-reconnect
    cleanup();
    if (wsRef.current) {
      wsRef.current.close(1000, 'Client disconnect');
      wsRef.current = null;
    }
    setStatus('disconnected');
    setPlayerId(null);
  }, [cleanup, maxReconnectAttempts]);

  const send = useCallback((message: GameMessage) => {
    if (wsRef.current?.readyState === WebSocket.OPEN) {
      wsRef.current.send(JSON.stringify(message));
    }
  }, []);

  const onMessage = useCallback((handler: (msg: GameMessage) => void) => {
    handlersRef.current.add(handler);
    return () => {
      handlersRef.current.delete(handler);
    };
  }, []);

  // Cleanup on unmount
  useEffect(() => {
    return () => {
      cleanup();
      if (wsRef.current) {
        wsRef.current.close(1000, 'Component unmount');
        wsRef.current = null;
      }
    };
  }, [cleanup]);

  return {
    status,
    playerId,
    send,
    connect,
    disconnect,
    latency,
    onMessage,
  };
}
