'use client';

import { DashboardData } from '@/lib/api';

interface StatCardsProps {
  data: DashboardData | null;
}

interface StatCardInfo {
  label: string;
  value: string | number;
  subtitle?: string;
  color: string;
  trend?: 'up' | 'down' | 'neutral';
}

export default function StatCards({ data }: StatCardsProps) {
  const activeSessions = data?.sessions?.filter(s => s.state === 'playing').length ?? 0;
  const totalPlayers = data?.players?.length ?? 0;
  const playersInGame = data?.sessions?.reduce((sum, s) => sum + s.playerCount, 0) ?? 0;
  const cryptolAwarded = data?.analytics?.totalCryptolAwarded ?? 0;

  const cards: StatCardInfo[] = [
    {
      label: 'Active Sessions',
      value: activeSessions,
      subtitle: `${data?.sessions?.length ?? 0} total (incl. lobby)`,
      color: 'var(--accent)',
    },
    {
      label: 'Players Online',
      value: playersInGame,
      subtitle: `${totalPlayers} registered total`,
      color: 'var(--success)',
    },
    {
      label: 'Cryptol Awarded',
      value: cryptolAwarded.toLocaleString(),
      subtitle: 'All time',
      color: 'var(--warning)',
    },
    {
      label: 'Win Rate',
      value: `${((data?.analytics?.winRate ?? 0) * 100).toFixed(0)}%`,
      subtitle: `${data?.analytics?.matchesLast24h ?? 0} matches (24h)`,
      color: 'var(--info)',
    },
  ];

  return (
    <div style={{
      display: 'grid',
      gridTemplateColumns: 'repeat(4, 1fr)',
      gap: '1rem',
    }}>
      {cards.map((card, i) => (
        <div key={i} style={{
          background: 'var(--bg-card)',
          border: '1px solid var(--border)',
          borderRadius: '8px',
          padding: '1.25rem',
          display: 'flex',
          flexDirection: 'column',
          gap: '0.5rem',
        }}>
          <span style={{ fontSize: '0.75rem', color: 'var(--text-secondary)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
            {card.label}
          </span>
          <span style={{ fontSize: '1.75rem', fontWeight: 700, color: card.color }}>
            {card.value}
          </span>
          {card.subtitle && (
            <span style={{ fontSize: '0.7rem', color: 'var(--text-dim)' }}>
              {card.subtitle}
            </span>
          )}
        </div>
      ))}
    </div>
  );
}
