'use client';

import { TabId } from '@/app/page';

interface SidebarProps {
  activeTab: TabId;
  onTabChange: (tab: TabId) => void;
}

const TABS: { id: TabId; label: string; icon: string }[] = [
  { id: 'overview', label: 'Overview', icon: '📊' },
  { id: 'sessions', label: 'Sessions', icon: '🎮' },
  { id: 'players', label: 'Players', icon: '👥' },
  { id: 'analytics', label: 'Analytics', icon: '📈' },
];

export default function Sidebar({ activeTab, onTabChange }: SidebarProps) {
  return (
    <aside style={{
      width: '220px',
      background: 'var(--bg-secondary)',
      borderRight: '1px solid var(--border)',
      display: 'flex',
      flexDirection: 'column',
      padding: '1rem 0',
    }}>
      {/* Logo */}
      <div style={{
        padding: '0 1.25rem',
        marginBottom: '2rem',
      }}>
        <h2 style={{
          fontSize: '1.1rem',
          fontWeight: 700,
          color: 'var(--accent)',
          letterSpacing: '0.1em',
        }}>
          CARCOSA
        </h2>
        <p style={{ fontSize: '0.65rem', color: 'var(--text-dim)', marginTop: '2px' }}>
          Matchmaking Admin
        </p>
      </div>

      {/* Navigation */}
      <nav style={{ display: 'flex', flexDirection: 'column', gap: '2px', padding: '0 0.5rem' }}>
        {TABS.map(tab => (
          <button
            key={tab.id}
            onClick={() => onTabChange(tab.id)}
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: '0.75rem',
              padding: '0.6rem 0.75rem',
              borderRadius: '6px',
              border: 'none',
              background: activeTab === tab.id ? 'var(--accent)' : 'transparent',
              color: activeTab === tab.id ? '#fff' : 'var(--text-secondary)',
              cursor: 'pointer',
              fontSize: '0.85rem',
              fontWeight: activeTab === tab.id ? 500 : 400,
              textAlign: 'left',
              transition: 'background 0.15s, color 0.15s',
            }}
            onMouseEnter={(e) => {
              if (activeTab !== tab.id) e.currentTarget.style.background = 'var(--bg-hover)';
            }}
            onMouseLeave={(e) => {
              if (activeTab !== tab.id) e.currentTarget.style.background = 'transparent';
            }}
          >
            <span style={{ fontSize: '1rem' }}>{tab.icon}</span>
            {tab.label}
          </button>
        ))}
      </nav>

      {/* Footer */}
      <div style={{
        marginTop: 'auto',
        padding: '1rem 1.25rem',
        fontSize: '0.65rem',
        color: 'var(--text-dim)',
        borderTop: '1px solid var(--border)',
      }}>
        <div>Matchmaking v1.0.0</div>
        <div style={{ marginTop: '2px' }}>Kafka + REST API</div>
      </div>
    </aside>
  );
}
