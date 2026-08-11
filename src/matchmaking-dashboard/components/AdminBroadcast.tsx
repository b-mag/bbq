/**
 * =============================================================================
 * AdminBroadcast.tsx — Admin Message Composer for the Dashboard
 * =============================================================================
 *
 * Allows the admin to type a message that is broadcast to all connected peers.
 * Messages appear as a prominent overlay in the center of every player's screen.
 * Use cases: server maintenance warnings, world events, announcements.
 * =============================================================================
 */
'use client';

import { useState } from 'react';

const API_BASE = typeof window !== 'undefined'
  ? `${window.location.protocol}//${window.location.host}`
  : 'http://localhost:5100';

export default function AdminBroadcast() {
  const [message, setMessage] = useState('');
  const [priority, setPriority] = useState<'info' | 'warning' | 'critical'>('info');
  const [duration, setDuration] = useState(15);
  const [status, setStatus] = useState<'idle' | 'sending' | 'sent' | 'error'>('idle');
  const [lastSent, setLastSent] = useState('');

  const handleSend = async () => {
    if (!message.trim()) return;
    setStatus('sending');

    try {
      const response = await fetch(`${API_BASE}/api/admin/broadcast`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ message: message.trim(), priority, durationSeconds: duration }),
      });

      if (response.ok) {
        setLastSent(message.trim());
        setMessage('');
        setStatus('sent');
        setTimeout(() => setStatus('idle'), 3000);
      } else {
        setStatus('error');
        setTimeout(() => setStatus('idle'), 3000);
      }
    } catch {
      setStatus('error');
      setTimeout(() => setStatus('idle'), 3000);
    }
  };

  return (
    <div style={{
      background: 'var(--bg-card)', borderRadius: 8, padding: '1.25rem',
      border: '1px solid var(--border)',
    }}>
      <h3 style={{ fontSize: '0.95rem', color: 'var(--text-primary)', marginBottom: '0.75rem' }}>
        Admin Broadcast
      </h3>
      <p style={{ fontSize: '0.7rem', color: 'var(--text-secondary)', marginBottom: '1rem' }}>
        Send a message to all connected players. Displayed as a prominent overlay.
      </p>

      {/* Message input */}
      <textarea
        value={message}
        onChange={(e) => setMessage(e.target.value)}
        placeholder="Type your message here... (e.g., 'Server restart in 10 minutes')"
        maxLength={300}
        style={{
          width: '100%', minHeight: 80, padding: '0.6rem', fontSize: '0.85rem',
          background: 'var(--bg-primary)', border: '1px solid var(--border)',
          borderRadius: 6, color: 'var(--text-primary)', resize: 'vertical',
          fontFamily: 'inherit',
        }}
      />

      {/* Options row */}
      <div style={{ display: 'flex', gap: '1rem', marginTop: '0.75rem', alignItems: 'center' }}>
        {/* Priority selector */}
        <div style={{ display: 'flex', gap: '0.4rem' }}>
          {(['info', 'warning', 'critical'] as const).map(p => (
            <button key={p} onClick={() => setPriority(p)} style={{
              padding: '4px 10px', fontSize: '0.7rem', borderRadius: 4, cursor: 'pointer',
              background: priority === p ? (p === 'critical' ? 'rgba(231,76,60,0.2)' : p === 'warning' ? 'rgba(241,196,15,0.2)' : 'rgba(52,152,219,0.2)') : 'transparent',
              border: `1px solid ${priority === p ? (p === 'critical' ? 'var(--danger)' : p === 'warning' ? '#f1c40f' : 'var(--accent)') : 'var(--border)'}`,
              color: priority === p ? (p === 'critical' ? 'var(--danger)' : p === 'warning' ? '#f1c40f' : 'var(--accent)') : 'var(--text-secondary)',
            }}>
              {p}
            </button>
          ))}
        </div>

        {/* Duration */}
        <div style={{ display: 'flex', alignItems: 'center', gap: '0.3rem' }}>
          <span style={{ fontSize: '0.7rem', color: 'var(--text-secondary)' }}>Duration:</span>
          <input
            type="number"
            value={duration}
            onChange={(e) => setDuration(parseInt(e.target.value) || 15)}
            min={5} max={300}
            style={{
              width: 50, padding: '3px 6px', fontSize: '0.75rem',
              background: 'var(--bg-primary)', border: '1px solid var(--border)',
              borderRadius: 4, color: 'var(--text-primary)',
            }}
          />
          <span style={{ fontSize: '0.7rem', color: 'var(--text-secondary)' }}>sec</span>
        </div>

        {/* Send button */}
        <button
          onClick={handleSend}
          disabled={!message.trim() || status === 'sending'}
          style={{
            marginLeft: 'auto', padding: '6px 16px', fontSize: '0.8rem',
            borderRadius: 6, cursor: message.trim() ? 'pointer' : 'not-allowed',
            background: status === 'sent' ? 'rgba(46,204,113,0.2)' : status === 'error' ? 'rgba(231,76,60,0.2)' : 'var(--accent)',
            border: 'none', color: '#fff', fontWeight: 500,
            opacity: message.trim() ? 1 : 0.5,
          }}
        >
          {status === 'sending' ? 'Sending...' : status === 'sent' ? 'Sent!' : status === 'error' ? 'Failed' : 'Broadcast'}
        </button>
      </div>

      {/* Last sent preview */}
      {lastSent && (
        <div style={{
          marginTop: '0.75rem', padding: '0.5rem', borderRadius: 4,
          background: 'rgba(52,152,219,0.1)', border: '1px solid rgba(52,152,219,0.2)',
          fontSize: '0.7rem', color: 'var(--text-secondary)',
        }}>
          Last sent: &quot;{lastSent}&quot;
        </div>
      )}
    </div>
  );
}
