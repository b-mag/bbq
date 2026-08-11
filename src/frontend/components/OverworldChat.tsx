/**
 * =============================================================================
 * OverworldChat.tsx — Free-Text Chat with Global/Nearby/Party Channels
 * =============================================================================
 *
 * Chat UI overlay for the overworld. Features:
 *   - Free text input (Enter to focus, Enter to send)
 *   - Channel selector (/g, /n, /p prefixes or clickable tabs)
 *   - Color-coded messages by channel
 *   - Scrollable message history (last 50)
 * =============================================================================
 */
'use client';

import { useState, useRef, useEffect, useCallback } from 'react';
import { OverworldMessage, OwMessageTypes, OwChatMessagePayload } from '@/lib/overworld-messages';

interface ChatMessage {
  channel: 'global' | 'nearby' | 'party';
  senderId: string;
  senderName: string;
  text: string;
  timestamp: number;
}

interface OverworldChatProps {
  send: (msg: OverworldMessage) => void;
  playerId: string | null;
  playerName: string;
  onMessage: (handler: (msg: OverworldMessage) => void) => () => void;
}

const CHANNEL_COLORS = {
  global: '#e8dcc8',   // White/cream
  nearby: '#d4c45a',   // Yellow
  party: '#5ac45a',    // Green
};

const CHANNEL_LABELS = {
  global: '[G]',
  nearby: '[N]',
  party: '[P]',
};

export default function OverworldChat({ send, playerId, playerName, onMessage }: OverworldChatProps) {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [inputText, setInputText] = useState('');
  const [activeChannel, setActiveChannel] = useState<'global' | 'nearby' | 'party'>('global');
  const [isFocused, setIsFocused] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  const scrollRef = useRef<HTMLDivElement>(null);

  // Listen for incoming chat messages
  useEffect(() => {
    const unsub = onMessage((msg: OverworldMessage) => {
      if (msg.type === OwMessageTypes.ChatMessage && msg.chatMessage) {
        const cm = msg.chatMessage;
        setMessages(prev => [...prev.slice(-49), {
          channel: cm.channel as 'global' | 'nearby' | 'party',
          senderId: cm.senderId,
          senderName: cm.senderName,
          text: cm.text,
          timestamp: cm.timestamp,
        }]);
      }
    });
    return unsub;
  }, [onMessage]);

  // Auto-scroll to bottom on new messages
  useEffect(() => {
    if (scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [messages.length]);

  // Enter key to toggle focus
  useEffect(() => {
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === 'Enter' && !isFocused) {
        e.preventDefault();
        inputRef.current?.focus();
        setIsFocused(true);
      }
    };
    window.addEventListener('keydown', handleKey);
    return () => window.removeEventListener('keydown', handleKey);
  }, [isFocused]);

  const handleSend = useCallback(() => {
    if (!inputText.trim() || !playerId) return;

    let text = inputText.trim();
    let channel = activeChannel;

    // Check for prefix commands
    if (text.startsWith('/g ')) { channel = 'global'; text = text.slice(3); }
    else if (text.startsWith('/n ')) { channel = 'nearby'; text = text.slice(3); }
    else if (text.startsWith('/p ')) { channel = 'party'; text = text.slice(3); }

    if (!text) return;

    send({
      type: OwMessageTypes.ChatMessage,
      chatMessage: {
        channel,
        senderId: playerId,
        senderName: playerName,
        text,
        timestamp: Date.now(),
      },
    });

    setInputText('');
  }, [inputText, activeChannel, playerId, playerName, send]);

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter') {
      e.preventDefault();
      if (inputText.trim()) {
        handleSend();
      } else {
        inputRef.current?.blur();
        setIsFocused(false);
      }
    }
    if (e.key === 'Escape') {
      inputRef.current?.blur();
      setIsFocused(false);
    }
    // Tab cycles channels
    if (e.key === 'Tab') {
      e.preventDefault();
      const channels: Array<'global' | 'nearby' | 'party'> = ['global', 'nearby', 'party'];
      const idx = channels.indexOf(activeChannel);
      setActiveChannel(channels[(idx + 1) % channels.length]);
    }
  };

  return (
    <div style={{
      position: 'absolute', bottom: 40, left: 12, width: 340,
      background: isFocused ? 'rgba(13, 15, 7, 0.92)' : 'rgba(13, 15, 7, 0.6)',
      border: '1px solid #3a3520', borderRadius: 6,
      transition: 'background 0.2s',
      pointerEvents: isFocused ? 'auto' : 'none',
    }}>
      {/* Message log */}
      <div ref={scrollRef} style={{
        maxHeight: isFocused ? 180 : 100, overflow: 'hidden auto',
        padding: '6px 8px', fontSize: '0.72rem', lineHeight: 1.4,
      }}>
        {messages.slice(-20).map((msg, i) => (
          <div key={i} style={{ marginBottom: 2, opacity: isFocused ? 1 : 0.7 }}>
            <span style={{ color: CHANNEL_COLORS[msg.channel], fontWeight: 'bold', fontSize: '0.65rem' }}>
              {CHANNEL_LABELS[msg.channel]}
            </span>{' '}
            <span style={{ color: msg.senderId === playerId ? '#c9a84c' : '#9a9080' }}>
              {msg.senderName}:
            </span>{' '}
            <span style={{ color: CHANNEL_COLORS[msg.channel] }}>{msg.text}</span>
          </div>
        ))}
        {messages.length === 0 && (
          <div style={{ color: '#4a4530', fontStyle: 'italic' }}>
            Press Enter to chat. /g /n /p for channel.
          </div>
        )}
      </div>

      {/* Input area */}
      <div style={{
        display: 'flex', alignItems: 'center', gap: 4,
        padding: '4px 6px', borderTop: '1px solid #2a2518',
        pointerEvents: 'auto',
      }}>
        {/* Channel tabs */}
        {(['global', 'nearby', 'party'] as const).map(ch => (
          <button key={ch} onClick={() => setActiveChannel(ch)} style={{
            padding: '2px 6px', fontSize: '0.6rem', borderRadius: 3,
            background: activeChannel === ch ? 'rgba(100,100,50,0.3)' : 'transparent',
            border: `1px solid ${activeChannel === ch ? CHANNEL_COLORS[ch] : '#3a3520'}`,
            color: CHANNEL_COLORS[ch], cursor: 'pointer',
          }}>
            {ch[0].toUpperCase()}
          </button>
        ))}
        <input
          ref={inputRef}
          type="text"
          value={inputText}
          onChange={(e) => setInputText(e.target.value)}
          onKeyDown={handleKeyDown}
          onFocus={() => setIsFocused(true)}
          onBlur={() => setIsFocused(false)}
          placeholder={`${activeChannel} chat...`}
          maxLength={200}
          style={{
            flex: 1, background: '#1a1510', border: '1px solid #3a3520', borderRadius: 3,
            padding: '3px 6px', color: '#e8dcc8', fontSize: '0.72rem', outline: 'none',
          }}
        />
      </div>
    </div>
  );
}
