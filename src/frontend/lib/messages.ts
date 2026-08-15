/**
 * =============================================================================
 * messages.ts — Game Network Protocol Types (Client Mirror of Server Messages.cs)
 * =============================================================================
 *
 * WHY MIRROR THE SERVER:
 * This file defines the exact same message types as the server's Messages.cs.
 * Both sides must agree on the JSON structure for WebSocket communication.
 * Any change to message shapes must be made in BOTH files simultaneously.
 *
 * WHY NOT CODE GENERATION:
 * With only ~15 message types, manual mirroring is simpler than setting up a
 * code generator (protobuf, typespec, etc.). The AOT requirement on the server
 * makes most code-gen tools incompatible anyway.
 *
 * NAMING CONVENTION:
 * Server uses PascalCase (C# convention), but serializes to camelCase via
 * JsonSerializerOptions. TypeScript natively uses camelCase, so the types here
 * match the wire format directly.
 * =============================================================================
 */

// --- Message type constants ---
// Must match MessageTypes in the server's Messages.cs exactly.
export const MessageTypes = {
  PlayerJoined: 'player_joined',
  PlayerLeft: 'player_left',
  PlayerInput: 'player_input',
  GameState: 'game_state',
  MapData: 'map_data',
  Chat: 'chat',
  SessionInfo: 'session_info',
  SessionAction: 'session_action',
  GameEvent: 'game_event',
  Ping: 'ping',
  Pong: 'pong',
  Error: 'error',
} as const;

export type MessageType = typeof MessageTypes[keyof typeof MessageTypes];

// --- Payload interfaces ---

export interface PlayerJoinedPayload {
  playerId: string;
  playerName: string;
  selectedClass?: string;
}

export interface PlayerLeftPayload {
  playerId: string;
  reason?: string;
}

export interface PlayerInputPayload {
  sequenceNumber: number;
  moveX: number;
  moveY: number;
  primaryFire: boolean;
  secondaryAbility: boolean;
  interact: boolean;
  useMedKit: boolean;
  aimAngle: number;
  timestamp: number;
}

export interface GameStatePayload {
  tick: number;
  entities: EntityState[];
  lastProcessedInput?: number;
}

export interface EntityState {
  id: string;
  entityType: 'player' | 'enemy' | 'projectile';
  x: number;
  y: number;
  velocityX: number;
  velocityY: number;
  health: number;
  maxHealth: number;
  subType?: string;
  isAlive: boolean;
  medKits: number;
  attackCooldown?: number;
}

export interface ChatMessagePayload {
  senderId: string;
  senderName: string;
  message: string;
  timestamp: number;
}

export interface SessionInfoPayload {
  sessionId: string;
  hostId: string;
  state: 'lobby' | 'playing' | 'game_over';
  players: PlayerInfo[];
  maxPlayers: number;
  currentWave: number;
  scenario: 'warehouse' | 'temple';
}

export interface PlayerInfo {
  id: string;
  name: string;
  selectedClass?: string;
  isReady: boolean;
  isHost: boolean;
}

export interface PingPayload {
  clientTimestamp: number;
}

export interface PongPayload {
  clientTimestamp: number;
  serverTimestamp: number;
}

export interface ErrorPayload {
  code: string;
  message: string;
}

export interface MapDataPayload {
  width: number;
  height: number;
  seed: number;
  tilesBase64: string;
}

export interface SessionActionPayload {
  action: 'select_class' | 'set_ready' | 'start_game' | 'return_to_lobby' | 'select_scenario';
  value?: string;
}

export interface GameEventPayload {
  event: 'damage' | 'heal' | 'death' | 'revive' | 'wave_start' | 'game_over' | 'victory';
  targetId?: string;
  sourceId?: string;
  amount?: number;
  x?: number;
  y?: number;
  wave?: number;
  message?: string;
}

// --- Main message envelope ---

export interface GameMessage {
  type: MessageType;
  playerJoined?: PlayerJoinedPayload;
  playerLeft?: PlayerLeftPayload;
  playerInput?: PlayerInputPayload;
  gameState?: GameStatePayload;
  mapData?: MapDataPayload;
  gameEvent?: GameEventPayload;
  chat?: ChatMessagePayload;
  sessionInfo?: SessionInfoPayload;
  sessionAction?: SessionActionPayload;
  ping?: PingPayload;
  pong?: PongPayload;
  error?: ErrorPayload;
}

// --- Helper functions ---

export function createMessage(type: typeof MessageTypes.PlayerInput, payload: PlayerInputPayload): GameMessage;
export function createMessage(type: typeof MessageTypes.Chat, payload: ChatMessagePayload): GameMessage;
export function createMessage(type: typeof MessageTypes.Ping, payload: PingPayload): GameMessage;
export function createMessage(type: MessageType, payload?: unknown): GameMessage;
export function createMessage(type: MessageType, payload?: unknown): GameMessage {
  const msg: GameMessage = { type };

  switch (type) {
    case MessageTypes.PlayerInput:
      msg.playerInput = payload as PlayerInputPayload;
      break;
    case MessageTypes.Chat:
      msg.chat = payload as ChatMessagePayload;
      break;
    case MessageTypes.Ping:
      msg.ping = payload as PingPayload;
      break;
  }

  return msg;
}
