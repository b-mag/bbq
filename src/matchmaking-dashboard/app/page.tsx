'use client';

import { useState, useEffect, useCallback } from 'react';
import Sidebar from '@/components/Sidebar';
import StatCards from '@/components/StatCards';
import SessionsTable from '@/components/SessionsTable';
import SessionModal from '@/components/SessionModal';
import AnalyticsPanel from '@/components/AnalyticsPanel';
import PlayersPanel from '@/components/PlayersPanel';
import LiveFeed from '@/components/LiveFeed';
import { DashboardData, SessionInfo, fetchDashboardData } from '@/lib/api';

export type TabId = 'overview' | 'sessions' | 'players' | 'analytics';

export default function Dashboard() {
  const [activeTab, setActiveTab] = useState<TabId>('overview');
  const [data, setData] = useState<DashboardData | null>(null);
  const [selectedSession, setSelectedSession] = useState<SessionInfo | null>(null);
  const [liveEvents, setLiveEvents] = useState<string[]>([]);

  // Fetch dashboard data on interval (every 3 seconds for operational monitoring)
  useEffect(() => {
    const load = async () => {
      const result = await fetchDashboardData();
      if (result) setData(result);
    };
    load();
    const interval = setInterval(load, 3000);
    return () => clearInterval(interval);
  }, []);

  const addLiveEvent = useCallback((event: string) => {
    setLiveEvents(prev => [...prev.slice(-50), `[${new Date().toLocaleTimeString()}] ${event}`]);
  }, []);

  // Simulate live events from session data changes
  useEffect(() => {
    if (data?.sessions) {
      const playing = data.sessions.filter(s => s.state === 'playing').length;
      if (playing > 0) {
        addLiveEvent(`${playing} active session(s) in progress`);
      }
    }
  }, [data?.sessions?.length]);

  return (
    <div style={{ display: 'flex', height: '100vh', overflow: 'hidden' }}>
      {/* Sidebar Navigation */}
      <Sidebar activeTab={activeTab} onTabChange={setActiveTab} />

      {/* Main Content */}
      <main style={{
        flex: 1,
        overflow: 'auto',
        padding: '1.5rem',
        display: 'flex',
        flexDirection: 'column',
        gap: '1.5rem',
      }}>
        {/* Header */}
        <header style={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
        }}>
          <div>
            <h1 style={{ fontSize: '1.5rem', fontWeight: 600, color: 'var(--text-primary)' }}>
              {activeTab === 'overview' && 'Dashboard Overview'}
              {activeTab === 'sessions' && 'Active Sessions'}
              {activeTab === 'players' && 'Player Registry'}
              {activeTab === 'analytics' && 'Analytics & Insights'}
            </h1>
            <p style={{ fontSize: '0.8rem', color: 'var(--text-secondary)', marginTop: '0.25rem' }}>
              Carcosa Matchmaking Service Monitor
            </p>
          </div>
          <div style={{
            display: 'flex',
            alignItems: 'center',
            gap: '0.5rem',
            padding: '0.4rem 0.8rem',
            background: 'var(--bg-card)',
            borderRadius: '6px',
            border: '1px solid var(--border)',
          }}>
            <div style={{
              width: '8px',
              height: '8px',
              borderRadius: '50%',
              background: data ? 'var(--success)' : 'var(--danger)',
              animation: data ? 'none' : 'pulse 1.5s infinite',
            }} />
            <span style={{ fontSize: '0.75rem', color: 'var(--text-secondary)' }}>
              {data ? 'Connected' : 'Connecting...'}
            </span>
          </div>
        </header>

        {/* Tab Content */}
        {activeTab === 'overview' && (
          <>
            <StatCards data={data} />
            <div style={{ display: 'grid', gridTemplateColumns: '2fr 1fr', gap: '1.5rem' }}>
              <SessionsTable
                sessions={data?.sessions || []}
                onSelect={setSelectedSession}
              />
              <LiveFeed events={liveEvents} />
            </div>
          </>
        )}

        {activeTab === 'sessions' && (
          <SessionsTable
            sessions={data?.sessions || []}
            onSelect={setSelectedSession}
            fullWidth
          />
        )}

        {activeTab === 'players' && (
          <PlayersPanel players={data?.players || []} />
        )}

        {activeTab === 'analytics' && (
          <AnalyticsPanel analytics={data?.analytics ?? null} />
        )}
      </main>

      {/* Session Detail Modal */}
      {selectedSession && (
        <SessionModal
          session={selectedSession}
          onClose={() => setSelectedSession(null)}
        />
      )}
    </div>
  );
}
