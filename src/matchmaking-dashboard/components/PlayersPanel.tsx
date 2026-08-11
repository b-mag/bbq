'use client';

import { useState } from 'react';
import { PlayerInfo } from '@/lib/api';

interface PlayersPanelProps {
  players: PlayerInfo[];
}

/**
 * Player registry panel with sortable table, search, and balance display.
 * Demonstrates: data table, search filter, sorting, number formatting.
 */
export default function PlayersPanel({ players }: PlayersPanelProps) {
  const [search, setSearch] = useState('');
  const [sortBy, setSortBy] = useState<'balance' | 'registeredAt'>('balance');
  const [sortAsc, setSortAsc] = useState(false);

  const filtered = players.filter(p => p.id.includes(search));
  const sorted = [...filtered].sort((a, b) => {
    if (sortBy === 'balance') return sortAsc ? a.balance - b.balance : b.balance - a.balance;
    return sortAsc
      ? a.registeredAt.localeCompare(b.registeredAt)
      : b.registeredAt.localeCompare(a.registeredAt);
  });

  return (
    <div style={{
      background: 'var(--bg-card)',
      border: '1px solid var(--border)',
      borderRadius: '8px',
      overflow: 'hidden',
      display: 'flex',
      flexDirection: 'column',
      maxHeight: 'calc(100vh - 200px)',
    }}>
      {/* Header with search and stats */}
      <div style={{
        padding: '1rem',
        borderBottom: '1px solid var(--border)',
        display: 'flex',
        justifyContent: 'space-between',
        alignItems: 'center',
      }}>
        <div>
          <h3 style={{ fontSize: '0.9rem', fontWeight: 600 }}>Registered Players</h3>
          <span style={{ fontSize: '0.7rem', color: 'var(--text-dim)' }}>
            {players.length} total
          </span>
        </div>
        <input
          type="text"
          placeholder="Search by ID..."
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          style={{
            background: 'var(--bg-primary)',
            border: '1px solid var(--border)',
            borderRadius: '4px',
            padding: '0.3rem 0.6rem',
            color: 'var(--text-primary)',
            fontSize: '0.75rem',
            outline: 'none',
            width: '200px',
          }}
        />
      </div>

      {/* Table */}
      <div style={{ overflow: 'auto', flex: 1 }}>
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.8rem' }}>
          <thead>
            <tr style={{ background: 'var(--bg-secondary)' }}>
              <th style={{ padding: '0.6rem 1rem', textAlign: 'left', color: 'var(--text-secondary)', fontWeight: 500, borderBottom: '1px solid var(--border)' }}>
                Player ID
              </th>
              <th
                onClick={() => { if (sortBy === 'balance') setSortAsc(!sortAsc); else { setSortBy('balance'); setSortAsc(false); } }}
                style={{ padding: '0.6rem 1rem', textAlign: 'right', color: 'var(--text-secondary)', fontWeight: 500, cursor: 'pointer', borderBottom: '1px solid var(--border)' }}
              >
                Cryptol Balance {sortBy === 'balance' && (sortAsc ? '↑' : '↓')}
              </th>
              <th
                onClick={() => { if (sortBy === 'registeredAt') setSortAsc(!sortAsc); else { setSortBy('registeredAt'); setSortAsc(false); } }}
                style={{ padding: '0.6rem 1rem', textAlign: 'right', color: 'var(--text-secondary)', fontWeight: 500, cursor: 'pointer', borderBottom: '1px solid var(--border)' }}
              >
                Registered {sortBy === 'registeredAt' && (sortAsc ? '↑' : '↓')}
              </th>
            </tr>
          </thead>
          <tbody>
            {sorted.length === 0 ? (
              <tr>
                <td colSpan={3} style={{ padding: '2rem', textAlign: 'center', color: 'var(--text-dim)' }}>
                  {search ? 'No matching players' : 'No players registered'}
                </td>
              </tr>
            ) : (
              sorted.map(player => (
                <tr key={player.id} style={{ borderBottom: '1px solid var(--border)' }}>
                  <td style={{ padding: '0.6rem 1rem', fontFamily: 'monospace', fontSize: '0.75rem' }}>
                    {player.id}
                  </td>
                  <td style={{ padding: '0.6rem 1rem', textAlign: 'right', color: 'var(--warning)', fontWeight: 500 }}>
                    {player.balance.toLocaleString()}
                  </td>
                  <td style={{ padding: '0.6rem 1rem', textAlign: 'right', color: 'var(--text-dim)', fontSize: '0.72rem' }}>
                    {new Date(player.registeredAt).toLocaleDateString()}
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
