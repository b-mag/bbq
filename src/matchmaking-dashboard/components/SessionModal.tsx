'use client';

import { SessionInfo } from '@/lib/api';

interface SessionModalProps {
  session: SessionInfo;
  onClose: () => void;
}

export default function SessionModal({ session, onClose }: SessionModalProps) {
  const timeSinceHeartbeat = Math.round((Date.now() - session.timestamp) / 1000);

  return (
    <div
      style={{
        position: 'fixed',
        inset: 0,
        background: 'rgba(0,0,0,0.6)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        zIndex: 1000,
      }}
      onClick={onClose}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        style={{
          background: 'var(--bg-card)',
          border: '1px solid var(--border)',
          borderRadius: '12px',
          padding: '1.5rem',
          width: '500px',
          maxHeight: '80vh',
          overflow: 'auto',
        }}
      >
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1.25rem' }}>
          <h2 style={{ fontSize: '1.1rem', fontWeight: 600 }}>Session Details</h2>
          <button
            onClick={onClose}
            style={{
              background: 'var(--bg-hover)',
              border: '1px solid var(--border)',
              borderRadius: '4px',
              padding: '0.25rem 0.5rem',
              color: 'var(--text-secondary)',
              cursor: 'pointer',
              fontSize: '0.8rem',
            }}
          >
            Close
          </button>
        </div>

        {/* Session Info Grid */}
        <div style={{
          display: 'grid',
          gridTemplateColumns: '1fr 1fr',
          gap: '1rem',
          marginBottom: '1.25rem',
        }}>
          <InfoField label="Session ID" value={session.sessionId} mono />
          <InfoField label="Host Address" value={session.hostAddress} mono />
          <InfoField label="Scenario" value={session.scenario} />
          <InfoField label="State" value={session.state} badge />
          <InfoField label="Players" value={`${session.playerCount} / ${session.maxPlayers}`} />
          <InfoField label="Current Wave" value={session.currentWave > 0 ? `Wave ${session.currentWave}` : 'Lobby'} />
          <InfoField label="Last Heartbeat" value={`${timeSinceHeartbeat}s ago`} />
        </div>

        {/* Player Progress Bar */}
        <div style={{ marginBottom: '1rem' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '4px' }}>
            <span style={{ fontSize: '0.75rem', color: 'var(--text-secondary)' }}>Player Capacity</span>
            <span style={{ fontSize: '0.75rem', color: 'var(--text-dim)' }}>
              {Math.round((session.playerCount / session.maxPlayers) * 100)}%
            </span>
          </div>
          <div style={{
            height: '6px',
            background: 'var(--bg-primary)',
            borderRadius: '3px',
            overflow: 'hidden',
          }}>
            <div style={{
              height: '100%',
              width: `${(session.playerCount / session.maxPlayers) * 100}%`,
              background: session.playerCount >= session.maxPlayers ? 'var(--danger)' : 'var(--accent)',
              borderRadius: '3px',
              transition: 'width 0.3s',
            }} />
          </div>
        </div>

        {/* Wave Progress (for Warehouse) */}
        {session.scenario === 'warehouse' && session.currentWave > 0 && (
          <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '4px' }}>
              <span style={{ fontSize: '0.75rem', color: 'var(--text-secondary)' }}>Wave Progress</span>
              <span style={{ fontSize: '0.75rem', color: 'var(--text-dim)' }}>
                {session.currentWave}/5
              </span>
            </div>
            <div style={{
              height: '6px',
              background: 'var(--bg-primary)',
              borderRadius: '3px',
              overflow: 'hidden',
            }}>
              <div style={{
                height: '100%',
                width: `${(session.currentWave / 5) * 100}%`,
                background: 'var(--success)',
                borderRadius: '3px',
              }} />
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

function InfoField({ label, value, mono, badge }: {
  label: string;
  value: string | number;
  mono?: boolean;
  badge?: boolean;
}) {
  const colors: Record<string, string> = {
    lobby: 'var(--warning)',
    playing: 'var(--success)',
    game_over: 'var(--danger)',
  };

  return (
    <div>
      <div style={{ fontSize: '0.7rem', color: 'var(--text-dim)', marginBottom: '2px' }}>{label}</div>
      {badge ? (
        <span style={{
          padding: '2px 8px',
          borderRadius: '10px',
          fontSize: '0.75rem',
          background: `${colors[String(value)] || 'var(--text-dim)'}20`,
          color: colors[String(value)] || 'var(--text-secondary)',
          border: `1px solid ${colors[String(value)] || 'var(--text-dim)'}40`,
        }}>
          {value}
        </span>
      ) : (
        <div style={{
          fontSize: '0.85rem',
          color: 'var(--text-primary)',
          fontFamily: mono ? 'monospace' : 'inherit',
          textTransform: !mono ? 'capitalize' : 'none',
        }}>
          {value}
        </div>
      )}
    </div>
  );
}
