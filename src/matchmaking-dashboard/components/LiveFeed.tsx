'use client';

interface LiveFeedProps {
  events: string[];
}

/**
 * Live event feed showing real-time activity (toast-style notifications log).
 * Demonstrates: scrollable log list, auto-scroll, timestamp formatting.
 */
export default function LiveFeed({ events }: LiveFeedProps) {
  return (
    <div style={{
      background: 'var(--bg-card)',
      border: '1px solid var(--border)',
      borderRadius: '8px',
      display: 'flex',
      flexDirection: 'column',
      maxHeight: '400px',
      overflow: 'hidden',
    }}>
      <div style={{
        padding: '1rem',
        borderBottom: '1px solid var(--border)',
        display: 'flex',
        alignItems: 'center',
        gap: '0.5rem',
      }}>
        <div style={{
          width: '6px',
          height: '6px',
          borderRadius: '50%',
          background: 'var(--success)',
          animation: 'pulse 2s infinite',
        }} />
        <h3 style={{ fontSize: '0.9rem', fontWeight: 600 }}>Live Activity</h3>
      </div>
      <div style={{
        flex: 1,
        overflow: 'auto',
        padding: '0.75rem 1rem',
        display: 'flex',
        flexDirection: 'column-reverse',
      }}>
        {events.length === 0 ? (
          <div style={{ color: 'var(--text-dim)', fontSize: '0.75rem', textAlign: 'center', padding: '2rem 0' }}>
            Waiting for events...
          </div>
        ) : (
          events.slice().reverse().map((event, i) => (
            <div key={i} style={{
              fontSize: '0.72rem',
              color: 'var(--text-secondary)',
              padding: '0.25rem 0',
              borderBottom: '1px solid var(--bg-primary)',
              fontFamily: 'monospace',
            }}>
              {event}
            </div>
          ))
        )}
      </div>
    </div>
  );
}
