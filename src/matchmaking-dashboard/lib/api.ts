/**
 * API client for the matchmaking service.
 * Fetches session, player, and analytics data for the dashboard.
 */

const API_BASE = typeof window !== 'undefined'
  ? `${window.location.protocol}//${window.location.host}`
  : 'http://localhost:5100';

export interface SessionInfo {
  sessionId: string;
  hostAddress: string;
  playerCount: number;
  maxPlayers: number;
  state: string;
  scenario: string;
  currentWave: number;
  timestamp: number;
}

export interface PlayerInfo {
  id: string;
  balance: number;
  registeredAt: string;
}

export interface AnalyticsData {
  totalPlayers: number;
  totalMatches: number;
  classDistribution: { gangster: number; detective: number; surgeon: number };
  scenarioDistribution: { warehouse: number; temple: number };
  averageWaveReached: number;
  totalCryptolAwarded: number;
  winRate: number;
  invaderJoinRate: number;
  peakPlayersToday: number;
  matchesLast24h: number;
}

export interface DashboardData {
  sessions: SessionInfo[];
  players: PlayerInfo[];
  analytics: AnalyticsData | null;
}

/**
 * Fetch all dashboard data from the matchmaking service API.
 */
export async function fetchDashboardData(): Promise<DashboardData | null> {
  try {
    const [sessionsRes, playersRes, analyticsRes] = await Promise.allSettled([
      fetch(`${API_BASE}/api/sessions`),
      fetch(`${API_BASE}/api/players`),
      fetch(`${API_BASE}/api/analytics`),
    ]);

    const sessions: SessionInfo[] = sessionsRes.status === 'fulfilled' && sessionsRes.value.ok
      ? await sessionsRes.value.json()
      : [];

    const players: PlayerInfo[] = playersRes.status === 'fulfilled' && playersRes.value.ok
      ? await playersRes.value.json()
      : [];

    const analytics: AnalyticsData | null = analyticsRes.status === 'fulfilled' && analyticsRes.value.ok
      ? await analyticsRes.value.json()
      : null;

    return { sessions, players, analytics };
  } catch {
    return null;
  }
}

// --- Overworld API Types ---

export interface OverworldPlayerInfo {
  id: string;
  name: string;
  x: number;
  y: number;
  status: string;
  partyId?: string;
}

export interface OverworldPartyInfo {
  id: string;
  leaderId: string;
  leaderName: string;
  memberCount: number;
  members: { id: string; name: string }[];
  status: string;
}

export interface OverworldStats {
  totalConnected: number;
  inOverworld: number;
  inDungeon: number;
  inParties: number;
  totalParties: number;
}

/**
 * Fetch overworld-specific data for the dashboard.
 */
export async function fetchOverworldData(): Promise<{
  players: OverworldPlayerInfo[];
  parties: OverworldPartyInfo[];
  stats: OverworldStats | null;
} | null> {
  try {
    const [playersRes, partiesRes, statsRes] = await Promise.allSettled([
      fetch(`${API_BASE}/api/overworld/players`),
      fetch(`${API_BASE}/api/overworld/parties`),
      fetch(`${API_BASE}/api/overworld/stats`),
    ]);

    const players: OverworldPlayerInfo[] = playersRes.status === 'fulfilled' && playersRes.value.ok
      ? await playersRes.value.json()
      : [];

    const parties: OverworldPartyInfo[] = partiesRes.status === 'fulfilled' && partiesRes.value.ok
      ? await partiesRes.value.json()
      : [];

    const stats: OverworldStats | null = statsRes.status === 'fulfilled' && statsRes.value.ok
      ? await statsRes.value.json()
      : null;

    return { players, parties, stats };
  } catch {
    return null;
  }
}
