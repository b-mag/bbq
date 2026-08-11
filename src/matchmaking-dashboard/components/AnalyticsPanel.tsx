'use client';

import { AnalyticsData } from '@/lib/api';

interface AnalyticsPanelProps {
  analytics: AnalyticsData | null;
}

/**
 * Analytics panel with charts and insights.
 * Demonstrates: bar chart (CSS), donut chart (SVG), stat cards, progress bars.
 * No chart library needed — pure CSS/SVG for learning purposes.
 */
export default function AnalyticsPanel({ analytics }: AnalyticsPanelProps) {
  if (!analytics) {
    return (
      <div style={{ color: 'var(--text-dim)', textAlign: 'center', padding: '3rem' }}>
        Loading analytics data...
      </div>
    );
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
      {/* Row 1: Class Popularity + Scenario Distribution */}
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '1.5rem' }}>
        <ClassPopularityChart data={analytics.classDistribution} />
        <ScenarioDonutChart data={analytics.scenarioDistribution} />
      </div>

      {/* Row 2: Key Metrics */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '1rem' }}>
        <MetricCard label="Avg Wave Reached" value={analytics.averageWaveReached.toFixed(1)} icon="🌊" />
        <MetricCard label="Invader Join Rate" value={`${(analytics.invaderJoinRate * 100).toFixed(0)}%`} icon="👹" />
        <MetricCard label="Peak Players Today" value={String(analytics.peakPlayersToday)} icon="📈" />
      </div>

      {/* Row 3: Economy Overview */}
      <EconomyOverview analytics={analytics} />
    </div>
  );
}

/**
 * Bar chart showing class popularity (gangster/detective/surgeon).
 * Pure CSS bars — no chart library needed.
 */
function ClassPopularityChart({ data }: { data: { gangster: number; detective: number; surgeon: number } }) {
  const total = data.gangster + data.detective + data.surgeon || 1;
  const bars = [
    { label: 'Gangster', value: data.gangster, pct: (data.gangster / total) * 100, color: '#8b4513' },
    { label: 'Detective', value: data.detective, pct: (data.detective / total) * 100, color: '#2f4f4f' },
    { label: 'Surgeon', value: data.surgeon, pct: (data.surgeon / total) * 100, color: '#9ca3af' },
  ];

  return (
    <div style={{
      background: 'var(--bg-card)',
      border: '1px solid var(--border)',
      borderRadius: '8px',
      padding: '1.25rem',
    }}>
      <h3 style={{ fontSize: '0.9rem', fontWeight: 600, marginBottom: '1rem' }}>Class Popularity</h3>
      <div style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
        {bars.map(bar => (
          <div key={bar.label}>
            <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '4px' }}>
              <span style={{ fontSize: '0.75rem', color: 'var(--text-secondary)' }}>{bar.label}</span>
              <span style={{ fontSize: '0.75rem', color: 'var(--text-dim)' }}>
                {bar.value} picks ({bar.pct.toFixed(0)}%)
              </span>
            </div>
            <div style={{
              height: '20px',
              background: 'var(--bg-primary)',
              borderRadius: '4px',
              overflow: 'hidden',
            }}>
              <div style={{
                height: '100%',
                width: `${bar.pct}%`,
                background: bar.color,
                borderRadius: '4px',
                transition: 'width 0.5s ease',
                minWidth: bar.pct > 0 ? '8px' : '0',
              }} />
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

/**
 * Donut chart showing scenario split (Warehouse vs Temple).
 * Pure SVG — demonstrates how to draw charts without a library.
 */
function ScenarioDonutChart({ data }: { data: { warehouse: number; temple: number } }) {
  const total = data.warehouse + data.temple || 1;
  const warehousePct = data.warehouse / total;
  const templePct = data.temple / total;

  // SVG donut using stroke-dasharray
  const radius = 60;
  const circumference = 2 * Math.PI * radius;
  const warehouseArc = circumference * warehousePct;
  const templeArc = circumference * templePct;

  return (
    <div style={{
      background: 'var(--bg-card)',
      border: '1px solid var(--border)',
      borderRadius: '8px',
      padding: '1.25rem',
      display: 'flex',
      flexDirection: 'column',
      alignItems: 'center',
    }}>
      <h3 style={{ fontSize: '0.9rem', fontWeight: 600, marginBottom: '1rem', alignSelf: 'flex-start' }}>
        Scenario Distribution
      </h3>
      <svg width="160" height="160" viewBox="0 0 160 160">
        {/* Background ring */}
        <circle cx="80" cy="80" r={radius} fill="none" stroke="var(--bg-primary)" strokeWidth="20" />
        {/* Warehouse segment */}
        <circle
          cx="80" cy="80" r={radius}
          fill="none" stroke="var(--info)" strokeWidth="20"
          strokeDasharray={`${warehouseArc} ${circumference}`}
          strokeDashoffset="0"
          transform="rotate(-90 80 80)"
          style={{ transition: 'stroke-dasharray 0.5s' }}
        />
        {/* Temple segment */}
        <circle
          cx="80" cy="80" r={radius}
          fill="none" stroke="var(--warning)" strokeWidth="20"
          strokeDasharray={`${templeArc} ${circumference}`}
          strokeDashoffset={`${-warehouseArc}`}
          transform="rotate(-90 80 80)"
          style={{ transition: 'stroke-dasharray 0.5s' }}
        />
        {/* Center text */}
        <text x="80" y="76" textAnchor="middle" fill="var(--text-primary)" fontSize="18" fontWeight="700">
          {total}
        </text>
        <text x="80" y="94" textAnchor="middle" fill="var(--text-dim)" fontSize="10">
          matches
        </text>
      </svg>
      {/* Legend */}
      <div style={{ display: 'flex', gap: '1.5rem', marginTop: '0.75rem' }}>
        <LegendItem color="var(--info)" label="Warehouse" value={`${(warehousePct * 100).toFixed(0)}%`} />
        <LegendItem color="var(--warning)" label="Temple" value={`${(templePct * 100).toFixed(0)}%`} />
      </div>
    </div>
  );
}

function LegendItem({ color, label, value }: { color: string; label: string; value: string }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: '0.4rem' }}>
      <div style={{ width: '10px', height: '10px', borderRadius: '2px', background: color }} />
      <span style={{ fontSize: '0.72rem', color: 'var(--text-secondary)' }}>{label}</span>
      <span style={{ fontSize: '0.72rem', color: 'var(--text-dim)' }}>{value}</span>
    </div>
  );
}

function MetricCard({ label, value, icon }: { label: string; value: string; icon: string }) {
  return (
    <div style={{
      background: 'var(--bg-card)',
      border: '1px solid var(--border)',
      borderRadius: '8px',
      padding: '1rem',
      display: 'flex',
      alignItems: 'center',
      gap: '0.75rem',
    }}>
      <span style={{ fontSize: '1.5rem' }}>{icon}</span>
      <div>
        <div style={{ fontSize: '1.25rem', fontWeight: 700, color: 'var(--text-primary)' }}>{value}</div>
        <div style={{ fontSize: '0.7rem', color: 'var(--text-dim)' }}>{label}</div>
      </div>
    </div>
  );
}

function EconomyOverview({ analytics }: { analytics: AnalyticsData }) {
  return (
    <div style={{
      background: 'var(--bg-card)',
      border: '1px solid var(--border)',
      borderRadius: '8px',
      padding: '1.25rem',
    }}>
      <h3 style={{ fontSize: '0.9rem', fontWeight: 600, marginBottom: '1rem' }}>Cryptol Economy</h3>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '1.5rem' }}>
        <div>
          <div style={{ fontSize: '0.7rem', color: 'var(--text-dim)', marginBottom: '4px' }}>Total Awarded</div>
          <div style={{ fontSize: '1.4rem', fontWeight: 700, color: 'var(--warning)' }}>
            {analytics.totalCryptolAwarded.toLocaleString()}
          </div>
        </div>
        <div>
          <div style={{ fontSize: '0.7rem', color: 'var(--text-dim)', marginBottom: '4px' }}>Total Players</div>
          <div style={{ fontSize: '1.4rem', fontWeight: 700, color: 'var(--accent)' }}>
            {analytics.totalPlayers.toLocaleString()}
          </div>
        </div>
        <div>
          <div style={{ fontSize: '0.7rem', color: 'var(--text-dim)', marginBottom: '4px' }}>Avg Balance</div>
          <div style={{ fontSize: '1.4rem', fontWeight: 700, color: 'var(--success)' }}>
            {analytics.totalPlayers > 0
              ? Math.round(analytics.totalCryptolAwarded / analytics.totalPlayers).toLocaleString()
              : '0'}
          </div>
        </div>
      </div>
    </div>
  );
}
