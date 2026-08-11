'use client';

import { useState } from 'react';
import { SessionInfo } from '@/lib/api';

interface SessionsTableProps {
  sessions: SessionInfo[];
  onSelect: (session: SessionInfo) => void;
  fullWidth?: boolean;
}

type SortKey = 'sessionId' | 'playerCount' | 'state' | 'scenario' | 'currentWave';

export default function SessionsTable({ sessions, onSelect, fullWidth }: SessionsTableProps) {
  const [sortKey, setSortKey] = useState<SortKey>('playerCount');
  const [sortAsc, setSortAsc] = useState(false);
  const [filter, setFilter] = useState('');

  const filtered = sessions.filter(s =>
    s.sessionId.includes(filter) || s.scenario.includes(filter) || s.state.includes(filter)
  );

  const sorted = [...filtered].sort((a, b) => {
    const aVal = a[sortKey];
    const bVal = b[sortKey];
    if (typeof aVal === 'number' && typeof bVal === 'number') {
      return sortAsc ? aVal - bVal : bVal - aVal;
    }
    return sortAsc
      ? String(aVal).localeCompare(String(bVal))
      : String(bVal).localeCompare(String(aVal));
  });

  const handleSort = (key: SortKey) => {
    if (sortKey === key) setSortAsc(!sortAsc);
    else { setSortKey(key); setSortAsc(false); }
  };

  const getStateBadge = (state: string) => {
    const colors: Record<string, string> = {
      lobby: 'var(--warning)',
      playing: 'var(--success)',
      game_over: 'var(--danger)',
    };
    return (
      <span style={{
        padding: '2px 8px',
        borderRadius: '10px',
        fontSize: '0.7rem',
        fontWeight: 500,
        background: `${colors[state] || 'var(--text-dim)'}20`,
        color: colors[state] || 'var(--text-dim)',
        border: `1px solid ${colors[state] || 'var(--text-dim)'}40`,
      }}>
        {state}
      </span>
    );
  };

  return (
    <div style={{
      background: 'var(--bg-card)',
      border: '1px solid var(--border)',
      borderRadius: '8px',
      overflow: 'hidden',
      display: 'flex',
      flexDirection: 'column',
      maxHeight: fullWidth ? 'calc(100vh - 200px)' : '400px',
    }}>
      {/* Header */}
      <div style={{
        padding: '1rem',
        borderBottom: '1px solid var(--border)',
        display: 'flex',
        justifyContent: 'space-between',
        alignItems: 'center',
      }}>
        <h3 style={{ fontSize: '0.9rem', fontWeight: 600 }}>Game Sessions</h3>
        <input
          type="text"
          placeholder="Filter sessions..."
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          style={{
            background: 'var(--bg-primary)',
            border: '1px solid var(--border)',
            borderRadius: '4px',
            padding: '0.3rem 0.6rem',
            color: 'var(--text-primary)',
            fontSize: '0.75rem',
            outline: 'none',
            width: '180px',
          }}
        />
      </div>

      {/* Table */}
      <div style={{ overflow: 'auto', flex: 1 }}>
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.8rem' }}>
          <thead>
            <tr style={{ background: 'var(--bg-secondary)' }}>
              {([
                ['sessionId', 'Session ID'],
                ['scenario', 'Scenario'],
                ['state', 'State'],
                ['playerCount', 'Players'],
                ['currentWave', 'Wave'],
              ] as [SortKey, string][]).map(([key, label]) => (
                <th
                  key={key}
                  onClick={() => handleSort(key)}
                  style={{
                    padding: '0.6rem 1rem',
                    textAlign: 'left',
                    color: 'var(--text-secondary)',
                    fontWeight: 500,
                    cursor: 'pointer',
                    userSelect: 'none',
                    borderBottom: '1px solid var(--border)',
                  }}
                >
                  {label} {sortKey === key && (sortAsc ? '↑' : '↓')}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {sorted.length === 0 ? (
              <tr>
                <td colSpan={5} style={{ padding: '2rem', textAlign: 'center', color: 'var(--text-dim)' }}>
                  No active sessions
                </td>
              </tr>
            ) : (
              sorted.map(session => (
                <tr
                  key={session.sessionId}
                  onClick={() => onSelect(session)}
                  style={{ cursor: 'pointer', borderBottom: '1px solid var(--border)' }}
                  onMouseEnter={(e) => e.currentTarget.style.background = 'var(--bg-hover)'}
                  onMouseLeave={(e) => e.currentTarget.style.background = 'transparent'}
                >
                  <td style={{ padding: '0.6rem 1rem', fontFamily: 'monospace', fontSize: '0.75rem' }}>
                    {session.sessionId}
                  </td>
                  <td style={{ padding: '0.6rem 1rem', textTransform: 'capitalize' }}>
                    {session.scenario}
                  </td>
                  <td style={{ padding: '0.6rem 1rem' }}>
                    {getStateBadge(session.state)}
                  </td>
                  <td style={{ padding: '0.6rem 1rem' }}>
                    <span style={{ color: 'var(--text-primary)' }}>{session.playerCount}</span>
                    <span style={{ color: 'var(--text-dim)' }}>/{session.maxPlayers}</span>
                  </td>
                  <td style={{ padding: '0.6rem 1rem' }}>
                    {session.currentWave > 0 ? session.currentWave : '—'}
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
