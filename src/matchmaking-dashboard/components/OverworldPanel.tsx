/**
 * OverworldPanel.tsx — Dashboard panel showing overworld metrics.
 * Displays connected players, parties, and dungeon instance counts.
 */
'use client';

import { useState, useEffect } from 'react';
import { fetchOverworldData, OverworldPlayerInfo, OverworldPartyInfo, OverworldStats } from '@/lib/api';

export default function OverworldPanel() {
  const [players, setPlayers] = useState<OverworldPlayerInfo[]>([]);
  const [parties, setParties] = useState<OverworldPartyInfo[]>([]);
  const [stats, setStats] = useState<OverworldStats | null>(null);

  useEffect(() => {
    const load = async () => {
      const data = await fetchOverworldData();
      if (data) {
        setPlayers(data.players);
        setParties(data.parties);
        setStats(data.stats);
      }
    };
    load();
    const interval = setInterval(load, 3000);
    return () => clearInterval(interval);
  }, []);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
      {/* Stats cards */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(5, 1fr)', gap: '1rem' }}>
        <StatCard label="Total Connected" value={stats?.totalConnected ?? 0} color="var(--accent)" />
        <StatCard label="In Overworld" value={stats?.inOverworld ?? 0} color="var(--success)" />
        <StatCard label="In Dungeons" value={stats?.inDungeon ?? 0} color="var(--danger)" />
        <StatCard label="In Parties" value={stats?.inParties ?? 0} color="#9b59b6" />
        <StatCard label="Active Parties" value={stats?.totalParties ?? 0} color="#3498db" />
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: '2fr 1fr', gap: '1.5rem' }}>
        {/* Players list */}
        <div style={{
          background: 'var(--bg-card)', borderRadius: 8,
          border: '1px solid var(--border)', overflow: 'hidden',
        }}>
          <div style={{ padding: '0.75rem 1rem', borderBottom: '1px solid var(--border)' }}>
            <h3 style={{ fontSize: '0.9rem', color: 'var(--text-primary)' }}>
              Overworld Players ({players.length})
            </h3>
          </div>
          <div style={{ maxHeight: 300, overflow: 'auto' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.75rem' }}>
              <thead>
                <tr style={{ borderBottom: '1px solid var(--border)' }}>
                  <th style={thStyle}>Name</th>
                  <th style={thStyle}>Position</th>
                  <th style={thStyle}>Status</th>
                  <th style={thStyle}>Party</th>
                </tr>
              </thead>
              <tbody>
                {players.map(p => (
                  <tr key={p.id} style={{ borderBottom: '1px solid var(--border)' }}>
                    <td style={tdStyle}>{p.name}</td>
                    <td style={tdStyle}>({Math.round(p.x)}, {Math.round(p.y)})</td>
                    <td style={tdStyle}>
                      <span style={{
                        padding: '2px 6px', borderRadius: 8, fontSize: '0.65rem',
                        background: p.status === 'exploring' ? 'rgba(46,204,113,0.15)' : 'rgba(155,89,182,0.15)',
                        color: p.status === 'exploring' ? 'var(--success)' : '#9b59b6',
                      }}>
                        {p.status}
                      </span>
                    </td>
                    <td style={tdStyle}>{p.partyId ? p.partyId.slice(0, 6) : '—'}</td>
                  </tr>
                ))}
                {players.length === 0 && (
                  <tr><td colSpan={4} style={{ ...tdStyle, textAlign: 'center', color: 'var(--text-secondary)' }}>
                    No players online
                  </td></tr>
                )}
              </tbody>
            </table>
          </div>
        </div>

        {/* Parties list */}
        <div style={{
          background: 'var(--bg-card)', borderRadius: 8,
          border: '1px solid var(--border)', overflow: 'hidden',
        }}>
          <div style={{ padding: '0.75rem 1rem', borderBottom: '1px solid var(--border)' }}>
            <h3 style={{ fontSize: '0.9rem', color: 'var(--text-primary)' }}>
              Active Parties ({parties.length})
            </h3>
          </div>
          <div style={{ maxHeight: 300, overflow: 'auto', padding: '0.5rem' }}>
            {parties.map(party => (
              <div key={party.id} style={{
                padding: '0.5rem', marginBottom: '0.5rem',
                background: 'rgba(255,255,255,0.03)', borderRadius: 6,
                border: '1px solid var(--border)',
              }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                  <span style={{ color: 'var(--text-primary)', fontSize: '0.8rem', fontWeight: 500 }}>
                    ★ {party.leaderName}
                  </span>
                  <span style={{
                    padding: '2px 6px', borderRadius: 8, fontSize: '0.6rem',
                    background: party.status === 'in_dungeon' ? 'rgba(231,76,60,0.15)' : 'rgba(46,204,113,0.15)',
                    color: party.status === 'in_dungeon' ? 'var(--danger)' : 'var(--success)',
                  }}>
                    {party.status}
                  </span>
                </div>
                <div style={{ color: 'var(--text-secondary)', fontSize: '0.7rem', marginTop: 4 }}>
                  {party.members.map(m => m.name).join(', ')} ({party.memberCount})
                </div>
              </div>
            ))}
            {parties.length === 0 && (
              <div style={{ color: 'var(--text-secondary)', fontSize: '0.75rem', textAlign: 'center', padding: '1rem' }}>
                No active parties
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

function StatCard({ label, value, color }: { label: string; value: number; color: string }) {
  return (
    <div style={{
      background: 'var(--bg-card)', borderRadius: 8, padding: '1rem',
      border: '1px solid var(--border)', textAlign: 'center',
    }}>
      <div style={{ fontSize: '1.8rem', fontWeight: 700, color }}>{value}</div>
      <div style={{ fontSize: '0.7rem', color: 'var(--text-secondary)', marginTop: 4 }}>{label}</div>
    </div>
  );
}

const thStyle: React.CSSProperties = {
  padding: '0.5rem 0.75rem', textAlign: 'left', color: 'var(--text-secondary)',
  fontWeight: 500, fontSize: '0.7rem',
};

const tdStyle: React.CSSProperties = {
  padding: '0.4rem 0.75rem', color: 'var(--text-primary)',
};
