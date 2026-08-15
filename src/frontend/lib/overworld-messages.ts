/**
 * =============================================================================
 * overworld-messages.ts — Overworld WebSocket Protocol (Client Mirror)
 * =============================================================================
 *
 * Mirrors the server's OverworldMessages.cs. Used for communication with the
 * matchmaking/overworld server (not the dungeon game server).
 * =============================================================================
 */

export const OwMessageTypes = {
  PlayerJoined: 'player_joined',
  PlayerLeft: 'player_left',
  PlayerInput: 'player_input',
  WorldState: 'world_state',
  MapData: 'map_data',
  ChatMessage: 'chat_message',
  PartyInvite: 'party_invite',
  PartyResponse: 'party_response',
  PartyUpdate: 'party_update',
  DungeonPrepare: 'dungeon_prepare',
  DungeonConnect: 'dungeon_connect',
  DungeonComplete: 'dungeon_complete',
  Ping: 'ping',
  Pong: 'pong',
  Error: 'error',
} as const;

export type OwMessageType = typeof OwMessageTypes[keyof typeof OwMessageTypes];

// --- Payload interfaces ---

export interface OwPlayerJoinedPayload {
  playerId: string;
  playerName: string;
  x: number;
  y: number;
}

export interface OwPlayerLeftPayload {
  playerId: string;
  reason?: string;
}

export interface OwPlayerInputPayload {
  sequenceNumber: number;
  moveX: number;
  moveY: number;
  interact: boolean;
  timestamp: number;
}

export interface OwWorldStatePayload {
  tick: number;
  players: OwPlayerState[];
  lastProcessedInput?: number;
}

export interface OwPlayerState {
  id: string;
  name: string;
  x: number;
  y: number;
  velocityX: number;
  velocityY: number;
  status: 'exploring' | 'in_party' | 'in_dungeon';
  partyId?: string;
  isPartyLeader: boolean;
  figure?: string;
}

export interface OwMapDataPayload {
  width: number;
  height: number;
  seed: number;
  tilesBase64: string;
  landmarks: OwLandmarkData[];
  dungeonEntrances: OwDungeonEntranceData[];
  worldObjects: OwWorldObjectData[];
  spawnX: number;
  spawnY: number;
}

export interface OwLandmarkData {
  name: string;
  x: number;
  y: number;
  type: string;
}

export interface OwDungeonEntranceData {
  name: string;
  x: number;
  y: number;
  scenario: string;
}

export interface OwWorldObjectData {
  type: string;
  x: number;
  y: number;
  collision: boolean;
  collisionRadius: number;
}

export interface OwChatMessagePayload {
  channel: 'global' | 'nearby' | 'party';
  senderId: string;
  senderName: string;
  text: string;
  timestamp: number;
}

export interface OwPartyInvitePayload {
  partyId: string;
  inviterId: string;
  inviterName: string;
}

export interface OwPartyResponsePayload {
  partyId: string;
  accepted: boolean;
}

export interface OwPartyUpdatePayload {
  partyId: string;
  leaderId: string;
  members: OwPartyMember[];
  event?: string;
}

export interface OwPartyMember {
  id: string;
  name: string;
  isLeader: boolean;
}

export interface OwDungeonPreparePayload {
  seed: number;
  scenario: string;
  dungeonWidth: number;
  dungeonHeight: number;
  partyMemberIds: string[];
}

export interface OwDungeonConnectPayload {
  hostAddress: string;
  seed: number;
  scenario: string;
}

export interface OwDungeonCompletePayload {
  victory: boolean;
  wavesCompleted: number;
}

export interface OwPingPayload {
  clientTimestamp: number;
}

export interface OwPongPayload {
  clientTimestamp: number;
  serverTimestamp: number;
}

export interface OwErrorPayload {
  code: string;
  message: string;
}

// --- Main message envelope ---

export interface OverworldMessage {
  type: OwMessageType;
  playerJoined?: OwPlayerJoinedPayload;
  playerLeft?: OwPlayerLeftPayload;
  playerInput?: OwPlayerInputPayload;
  worldState?: OwWorldStatePayload;
  mapData?: OwMapDataPayload;
  chatMessage?: OwChatMessagePayload;
  partyInvite?: OwPartyInvitePayload;
  partyResponse?: OwPartyResponsePayload;
  partyUpdate?: OwPartyUpdatePayload;
  dungeonPrepare?: OwDungeonPreparePayload;
  dungeonConnect?: OwDungeonConnectPayload;
  dungeonComplete?: OwDungeonCompletePayload;
  ping?: OwPingPayload;
  pong?: OwPongPayload;
  error?: OwErrorPayload;
}
