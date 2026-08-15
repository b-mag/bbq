/**
 * =============================================================================
 * OverworldView.tsx — Main Overworld UI Component
 * =============================================================================
 *
 * The primary view when a player is connected to the overworld.
 * Party, dungeon enter, and altar use REST (mesh RPG) rather than /ws/overworld.
 * =============================================================================
 */
'use client';

import { useEffect, useState, useCallback, useRef } from 'react';
import { useOverworldInput } from '@/hooks/useOverworldInput';
import { OwDungeonEntranceData, OwWorldObjectData, OwLandmarkData } from '@/lib/overworld-messages';
import { OverworldGameMap, decodeOverworldMap, buildLakeOverlay, nearestOwWalkable } from '@/lib/overworld-map';
import { FogOfWar } from '@/lib/engine/fogOfWar';
import OverworldCanvas from './OverworldCanvas';
import OverworldMapPanel from './OverworldMapPanel';
import OverworldChat from './OverworldChat';
import P2POverlay from './P2POverlay';
import HealthBar from './HealthBar';
import StaminaBar from './StaminaBar';
import XpBar from './XpBar';
import AbilityBar from './AbilityBar';
import PauseMenu from './PauseMenu';
import SettingsPanel, { GameSettings } from './SettingsPanel';
import InventoryPanel from './InventoryPanel';
import AbilitySelectPanel from './AbilitySelectPanel';
import FlameOfferingPanel from './FlameOfferingPanel';
import DialoguePanel from './DialoguePanel';
import CryptolShopPanel from './CryptolShopPanel';
import SaveIndicator from './SaveIndicator';
import { useP2POverworld } from '@/hooks/useP2POverworld';
import { usePlayerStats } from '@/hooks/usePlayerStats';
import { useOverworldEnemies } from '@/hooks/useOverworldEnemies';
import { pushPanel, removePanel } from '@/lib/ui-stack';
import { dialogueFor, ENTERABLE_BUILDINGS, NpcDialogue } from '@/lib/npc-dialogue';
import { generateInterior, InteriorInstance, isInteriorExit } from '@/lib/interior';

interface OverworldViewProps {
  playerName: string;
  playerFigure?: string;
  onDisconnect: () => void;
  onEnterDungeon?: (data: { hostAddress: string; seed: number; scenario: string }) => void;
}

interface PartySnapshot {
  partyId: string | null;
  leaderPeerId: string | null;
  memberPeerIds: string[];
  pendingInvitePeerIds: string[];
}

function isAltarObject(obj: OwWorldObjectData): boolean {
  const t = (obj.type || '').toLowerCase();
  return t.includes('altar') || t.includes('flame') || t === 'meditation_altar';
}

function isAltarLandmark(lm: OwLandmarkData): boolean {
  const t = (lm.type || '').toLowerCase();
  const n = (lm.name || '').toLowerCase();
  return t.includes('altar') || n.includes('altar') || n.includes('meditation');
}

export default function OverworldView({ playerName, playerFigure, onDisconnect, onEnterDungeon }: OverworldViewProps) {
  const [map, setMap] = useState<OverworldGameMap | null>(null);
  const [dungeonEntrances, setDungeonEntrances] = useState<OwDungeonEntranceData[]>([]);
  const [worldObjects, setWorldObjects] = useState<OwWorldObjectData[]>([]);
  const [landmarks, setLandmarks] = useState<OwLandmarkData[]>([]);
  const [party, setParty] = useState<PartySnapshot | null>(null);
  const [nearbyEntrance, setNearbyEntrance] = useState<OwDungeonEntranceData | null>(null);
  const [nearbyAltar, setNearbyAltar] = useState(false);
  const [pendingInvite, setPendingInvite] = useState<{ fromPeerId: string; inviterName: string } | null>(null);
  const [chatFocused, setChatFocused] = useState(false);
  const [clientSettings, setClientSettings] = useState({ showGlyphOverlay: true, showFps: false, devMode: false });
  const [showMap, setShowMap] = useState(false);
  const [fog, setFog] = useState<FogOfWar | null>(null);
  const fogRef = useRef<FogOfWar | null>(null);
  const resumeRef = useRef<{ x: number; y: number } | null>(null);

  const [showPauseMenu, setShowPauseMenu] = useState(false);
  const [showSettings, setShowSettings] = useState(false);
  const [showInventory, setShowInventory] = useState(false);
  const [showAbilitySelect, setShowAbilitySelect] = useState(false);
  const [showFlameOffering, setShowFlameOffering] = useState(false);
  const [showShop, setShowShop] = useState(false);
  const [lakeDrained, setLakeDrained] = useState(false);
  const [interior, setInterior] = useState<InteriorInstance | null>(null);
  const [dialogue, setDialogue] = useState<{ key: string; data: NpcDialogue; page: number } | null>(null);
  const [nearbyBuilding, setNearbyBuilding] = useState<OwWorldObjectData | null>(null);
  const [nearbyNpc, setNearbyNpc] = useState<OwWorldObjectData | null>(null);

  const p2p = useP2POverworld();
  const stats = usePlayerStats();
  const { enemies, projectiles, lootDrops } = useOverworldEnemies();

  const enteringDungeonRef = useRef(false);
  const lastDungeonInstanceRef = useRef<string | null>(
    typeof sessionStorage !== 'undefined' ? sessionStorage.getItem('carcosa.lastDungeonInstance') : null
  );
  const playersRef = useRef(p2p.players);
  playersRef.current = p2p.players;
  const declinedInvitesRef = useRef<Set<string>>(new Set());
  const anyPanelOpen = showPauseMenu || showSettings || showInventory || showAbilitySelect || showFlameOffering || showShop || showMap || !!dialogue;

  const activeMap = interior?.map ?? map;
  const activeObjects = interior?.objects ?? worldObjects;

  const input = useOverworldInput({
    send: () => {},
    map: activeMap,
    active: activeMap !== null && !chatFocused && !anyPanelOpen,
    worldObjects: activeObjects,
  });

  const localId = p2p.status?.peerId ?? null;

  const enterInterior = useCallback((obj: OwWorldObjectData) => {
    if (obj.type === 'lake_shop' && !lakeDrained) return;
    const inst = generateInterior(obj.type, obj.x, obj.y, input.position.x, input.position.y);
    setInterior(inst);
    input.setInitialPosition(inst.spawnX, inst.spawnY);
    if (inst.kind === 'shop') {
      setShowShop(true);
      pushPanel('cryptol-shop');
    }
  }, [lakeDrained, input]);

  const exitInterior = useCallback(() => {
    if (!interior) return;
    const retX = interior.returnX;
    const retY = interior.returnY;
    setInterior(null);
    setShowShop(false);
    removePanel('cryptol-shop');
    input.setInitialPosition(retX, retY + 1.2);
  }, [interior, input]);

  const openDialogue = useCallback((obj: OwWorldObjectData) => {
    const data = dialogueFor(obj.type);
    if (!data) return;
    setDialogue({ key: obj.type, data, page: 0 });
    pushPanel('dialogue');
  }, []);

  const openFlame = useCallback(() => {
    if (showFlameOffering) return;
    setShowFlameOffering(true);
    pushPanel('flame-offering');
  }, [showFlameOffering]);

  const transitionToDungeon = useCallback((seed: number, scenario: string, instanceId?: string | null) => {
    if (!onEnterDungeon) return;
    const id = instanceId || `${seed}:${scenario}`;
    lastDungeonInstanceRef.current = id;
    try { sessionStorage.setItem('carcosa.lastDungeonInstance', id); } catch { /* ignore */ }
    onEnterDungeon({
      hostAddress: window.location.origin,
      seed,
      scenario: scenario || 'mountain_cave',
    });
  }, [onEnterDungeon]);

  const enterDungeon = useCallback(async (entrance?: OwDungeonEntranceData | null) => {
    if (!onEnterDungeon || enteringDungeonRef.current) return;
    enteringDungeonRef.current = true;
    try {
      const res = await fetch('/api/gameplay/dungeon/enter', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          scenario: entrance?.scenario || 'mountain_cave',
          entranceX: entrance?.x ?? input.position.x,
          entranceY: entrance?.y ?? input.position.y,
        }),
      });
      if (!res.ok) {
        enteringDungeonRef.current = false;
        return;
      }
      const data = await res.json();
      const instance = data.instance;
      if (data.started || instance?.active) {
        transitionToDungeon(
          instance?.seed ?? 0,
          instance?.scenario || 'mountain_cave',
          instance?.instanceId
        );
      } else {
        enteringDungeonRef.current = false;
      }
    } catch {
      enteringDungeonRef.current = false;
    }
  }, [onEnterDungeon, input.position.x, input.position.y, transitionToDungeon]);

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (chatFocused) return;
      if (e.repeat) return;

      if (e.key === 'Escape') {
        e.preventDefault();
        if (dialogue) { setDialogue(null); removePanel('dialogue'); }
        else if (showShop) { setShowShop(false); removePanel('cryptol-shop'); }
        else if (showMap) { setShowMap(false); removePanel('overworld-map'); }
        else if (showSettings) { setShowSettings(false); removePanel('settings'); }
        else if (showFlameOffering) { setShowFlameOffering(false); removePanel('flame-offering'); }
        else if (showInventory) { setShowInventory(false); removePanel('inventory'); }
        else if (showAbilitySelect) { setShowAbilitySelect(false); removePanel('ability-select'); }
        else if (interior) { exitInterior(); }
        else if (showPauseMenu) { setShowPauseMenu(false); removePanel('pause-menu'); }
        else { setShowPauseMenu(true); pushPanel('pause-menu'); }
        return;
      }

      if (e.key === 'm' || e.key === 'M') {
        if (showMap) {
          setShowMap(false);
          removePanel('overworld-map');
        } else if (!dialogue && !showShop && !showSettings && !showPauseMenu) {
          setShowMap(true);
          pushPanel('overworld-map');
        }
        return;
      }

      if (dialogue) {
        if (e.key === 'e' || e.key === 'E' || e.key === ' ') {
          if (dialogue.page >= dialogue.data.lines.length - 1) {
            setDialogue(null);
            removePanel('dialogue');
          } else {
            setDialogue({ ...dialogue, page: dialogue.page + 1 });
          }
        }
        return;
      }

      if (anyPanelOpen) return;

      if (e.key === 'i' || e.key === 'I') {
        setShowInventory(true);
        pushPanel('inventory');
      }

      if (e.key === 'f' || e.key === 'F') {
        openFlame();
      }

      if (e.key === 'e' || e.key === 'E') {
        if (interior) {
          if (interior.kind === 'shop') {
            setShowShop(true);
            pushPanel('cryptol-shop');
          } else {
            exitInterior();
          }
        } else if (nearbyNpc) {
          openDialogue(nearbyNpc);
        } else if (nearbyBuilding) {
          enterInterior(nearbyBuilding);
        } else if (nearbyEntrance) {
          enterDungeon(nearbyEntrance);
        } else if (nearbyAltar) {
          openFlame();
        }
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [
    chatFocused, anyPanelOpen, showSettings, showFlameOffering, showInventory,
    showAbilitySelect, showPauseMenu, showShop, showMap, nearbyEntrance, nearbyAltar,
    nearbyBuilding, nearbyNpc, interior, dialogue, enterDungeon, openFlame,
    enterInterior, exitInterior, openDialogue,
  ]);

  // Load overworld map from local server REST API
  useEffect(() => {
    if (map) return;

    fetch('/api/p2p/name', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name: playerName, figure: playerFigure || undefined }),
    }).catch(() => {});

    const loadMap = async () => {
      try {
        const res = await fetch('/api/p2p/map');
        if (!res.ok) {
          console.warn(`[Overworld] /api/p2p/map returned ${res.status}, will retry...`);
          setTimeout(loadMap, 2000);
          return;
        }
        const data = await res.json();
        if (data && data.tilesBase64) {
          const decoded = decodeOverworldMap(data);
          decoded.lakeIsland = (data.lakeIsland || []).map((p: { x?: number; X?: number; y?: number; Y?: number }) => ({
            x: p.x ?? p.X ?? 0,
            y: p.y ?? p.Y ?? 0,
          }));
          decoded.drainCauseway = (data.drainCauseway || []).map((p: { x?: number; X?: number; y?: number; Y?: number }) => ({
            x: p.x ?? p.X ?? 0,
            y: p.y ?? p.Y ?? 0,
          }));
          decoded.lakeOverlay = buildLakeOverlay(decoded);
          decoded.lakeDrained = false;
          setMap(decoded);
          setDungeonEntrances(data.dungeonEntrances || []);
          setWorldObjects(data.worldObjects || []);
          setLandmarks(data.landmarks || []);
          const spawnX = data.spawnPoint?.x != null ? data.spawnPoint.x + 0.5 : 320.5;
          const spawnY = data.spawnPoint?.y != null ? data.spawnPoint.y + 0.5 : 544.5;
          let startX = spawnX;
          let startY = spawnY;
          try {
            const bootRes = await fetch('/api/gameplay/bootstrap');
            if (bootRes.ok) {
              const boot = await bootRes.json();
              if (typeof boot.lastX === 'number' && typeof boot.lastY === 'number'
                  && Number.isFinite(boot.lastX) && Number.isFinite(boot.lastY)
                  && boot.lastX >= 1 && boot.lastY >= 1
                  && boot.lastX < decoded.width - 1 && boot.lastY < decoded.height - 1) {
                startX = boot.lastX;
                startY = boot.lastY;
              }
              if (boot.devMode) {
                setClientSettings(s => ({ ...s, devMode: true }));
              }
              const nextFog = FogOfWar.fromBase64(decoded.width, decoded.height, boot.exploredFogBase64);
              nextFog.revealAround(startX, startY);
              fogRef.current = nextFog;
              setFog(nextFog);
            }
          } catch { /* spawn fallback */ }
          if (!fogRef.current) {
            const nextFog = new FogOfWar(decoded.width, decoded.height);
            nextFog.revealAround(startX, startY);
            fogRef.current = nextFog;
            setFog(nextFog);
          }
          resumeRef.current = { x: startX, y: startY };
          input.setInitialPosition(startX, startY);
          p2p.updatePosition(startX, startY, 0, 0, true);
          console.log(`[Overworld] Map loaded via REST: ${data.width}x${data.height}, resume at (${startX}, ${startY})`);
        } else {
          console.warn('[Overworld] /api/p2p/map missing tilesBase64, will retry...');
          setTimeout(loadMap, 2000);
        }
      } catch (e) {
        console.warn('[Overworld] Failed to load map from /api/p2p/map, will retry...', e);
        setTimeout(loadMap, 2000);
      }
    };
    loadMap();
  }, [map]);

  useEffect(() => {
    fetch('/api/gameplay/settings')
      .then(r => r.ok ? r.json() : null)
      .then(d => {
        if (!d) return;
        setClientSettings({
          showGlyphOverlay: d.showGlyphOverlay !== false,
          showFps: !!d.showFps,
          devMode: !!d.devMode,
        });
      })
      .catch(() => {});
  }, []);

  // Nearby dungeon entrance / altar / building / NPC
  useEffect(() => {
    const pos = input.position;
    if (interior) {
      setNearbyEntrance(null);
      setNearbyBuilding(null);
      setNearbyNpc(interior.objects.find(o => o.type.startsWith('npc_')) || null);
      setNearbyAltar(false);
      return;
    }

    const nearby = dungeonEntrances.find(e => {
      const dist = Math.sqrt((e.x - pos.x) ** 2 + (e.y - pos.y) ** 2);
      return dist < 2.5;
    });
    setNearbyEntrance(nearby || null);

    const altarObj = worldObjects.some(o => {
      if (!isAltarObject(o)) return false;
      return Math.sqrt((o.x - pos.x) ** 2 + (o.y - pos.y) ** 2) < 2.5;
    });
    const altarLm = landmarks.some(lm => {
      if (!isAltarLandmark(lm)) return false;
      return Math.sqrt((lm.x - pos.x) ** 2 + (lm.y - pos.y) ** 2) < 2.5;
    });
    setNearbyAltar(altarObj || altarLm);

    const building = worldObjects.find(o => {
      if (!ENTERABLE_BUILDINGS.has(o.type)) return false;
      if (o.type === 'lake_shop' && !lakeDrained) return false;
      const dx = pos.x - o.x;
      const dy = pos.y - o.y;
      const reach = (o.collisionRadius || 1.4) + 1.4;
      return Math.abs(dx) < reach && dy > -0.2 && dy < reach;
    });
    setNearbyBuilding(building || null);

    const npc = [...worldObjects, ...enemies.map(e => ({
      type: e.subType,
      x: e.x,
      y: e.y,
      collision: false,
      collisionRadius: 0,
    }))].find(o => {
      if (!o.type.startsWith('npc_')) return false;
      return Math.sqrt((o.x - pos.x) ** 2 + (o.y - pos.y) ** 2) < 1.8;
    });
    setNearbyNpc(npc || null);
  }, [input.position, dungeonEntrances, worldObjects, landmarks, interior, lakeDrained, enemies]);

  useEffect(() => {
    if (!interior) return;
    const { x, y } = input.position;
    if (x < 0.4 || x >= interior.map.width - 0.4 || y < 0.4) return;
    if (isInteriorExit(interior.map, x, y)) {
      exitInterior();
    }
  }, [interior, input.position, exitInterior]);

  useEffect(() => {
    const poll = async () => {
      try {
        const res = await fetch('/api/gameplay/world-atmosphere');
        if (!res.ok) return;
        const data = await res.json();
        const drained = !!data.lakeDrained;
        setLakeDrained(drained);
        setMap(m => m ? { ...m, lakeDrained: drained } : m);
      } catch { /* ignore */ }
    };
    poll();
    const id = setInterval(poll, 4000);
    return () => clearInterval(id);
  }, []);

  useEffect(() => {
    const pos = input.position;
    if (interior) return;
    if (pos.x !== 0 || pos.y !== 0) {
      p2p.updatePosition(pos.x, pos.y, input.velocityX, input.velocityY);
    }
    const f = fogRef.current;
    if (f && (pos.x !== 0 || pos.y !== 0)) {
      f.revealAround(pos.x, pos.y);
    }
  }, [input.position, p2p, interior]);

  useEffect(() => {
    if (input.position.x === 0 && input.position.y === 0 && p2p.status) {
      const localP2P = p2p.players.find(p => p.id === p2p.status?.peerId);
      if (!localP2P || (localP2P.x === 0 && localP2P.y === 0)) return;
      if (map && (localP2P.x < 1 || localP2P.y < 1 || localP2P.x >= map.width - 1 || localP2P.y >= map.height - 1)) {
        return;
      }
      input.setInitialPosition(localP2P.x, localP2P.y);
    }
  }, [p2p.players, p2p.status, input, map]);

  // Party REST poll
  useEffect(() => {
    const pollParty = async () => {
      try {
        const res = await fetch('/api/p2p/party');
        if (!res.ok) return;
        const data: PartySnapshot = await res.json();
        if (data.partyId && data.memberPeerIds?.length) {
          setParty(data);
        } else {
          setParty(null);
        }
        if (localId && data.pendingInvitePeerIds?.includes(localId)) {
          const fromId = data.leaderPeerId || localId;
          if (!declinedInvitesRef.current.has(fromId)) {
            const inviter = playersRef.current.find(p => p.id === fromId);
            setPendingInvite({
              fromPeerId: fromId,
              inviterName: inviter?.name || 'A traveler',
            });
          }
        } else {
          setPendingInvite(null);
        }
      } catch { /* ignore */ }
    };
    pollParty();
    const interval = setInterval(pollParty, 1500);
    return () => clearInterval(interval);
  }, [localId]);

  // Dungeon instance poll — party members get pulled in when a dungeon is active
  useEffect(() => {
    if (!onEnterDungeon) return;
    const pollDungeon = async () => {
      if (enteringDungeonRef.current) return;
      try {
        const res = await fetch('/api/gameplay/dungeon');
        if (!res.ok) return;
        const data = await res.json();
        if (data.active) {
          const id = data.instanceId || `${data.seed}:${data.scenario}`;
          if (id === lastDungeonInstanceRef.current) return;
          enteringDungeonRef.current = true;
          transitionToDungeon(data.seed ?? 0, data.scenario || 'mountain_cave', data.instanceId);
        }
      } catch { /* ignore */ }
    };
    const interval = setInterval(pollDungeon, 1500);
    return () => clearInterval(interval);
  }, [onEnterDungeon, transitionToDungeon]);

  const resolvedLocalId = localId ?? '__local__';
  const displayPlayers = (() => {
    const mapped = p2p.players.map(p => {
      if (localId && p.id === localId) {
        return {
          ...p,
          x: input.position.x,
          y: input.position.y,
          velocityX: input.velocityX,
          velocityY: input.velocityY,
          figure: playerFigure || p.figure,
        };
      }
      return p;
    });
    if (!mapped.some(p => p.id === resolvedLocalId)) {
      mapped.push({
        id: resolvedLocalId,
        name: playerName,
        x: input.position.x,
        y: input.position.y,
        velocityX: input.velocityX,
        velocityY: input.velocityY,
        figure: playerFigure,
        status: 'exploring',
        isPartyLeader: false,
      });
    }
    return mapped;
  })();

  const handleInvitePlayer = useCallback(async (targetId: string) => {
    if (!targetId || targetId === localId) return;
    try {
      await fetch('/api/p2p/party/invite', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ targetPeerId: targetId }),
      });
    } catch { /* ignore */ }
  }, [localId]);

  const handleAcceptInvite = useCallback(async () => {
    if (!pendingInvite) return;
    try {
      await fetch('/api/p2p/party/accept', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ fromPeerId: pendingInvite.fromPeerId }),
      });
    } catch { /* ignore */ }
    setPendingInvite(null);
  }, [pendingInvite]);

  const handleDeclineInvite = useCallback(() => {
    if (pendingInvite) declinedInvitesRef.current.add(pendingInvite.fromPeerId);
    setPendingInvite(null);
  }, [pendingInvite]);

  const handleSettingsSaved = useCallback((s: GameSettings) => {
    setClientSettings({ showGlyphOverlay: s.showGlyphOverlay, showFps: s.showFps, devMode: s.devMode });
  }, []);

  const persistFog = useCallback(() => {
    const f = fogRef.current;
    if (!f || !f.dirty) return;
    f.dirty = false;
    fetch('/api/gameplay/explored-fog', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ exploredFogBase64: f.toBase64() }),
    }).catch(() => {});
  }, []);

  useEffect(() => {
    const id = setInterval(() => persistFog(), 8000);
    return () => {
      persistFog();
      clearInterval(id);
    };
  }, [persistFog]);

  const handleMobilityDash = useCallback((x: number, y: number) => {
    input.setMovementLocked(false);
    input.setInitialPosition(x, y);
    p2p.updatePosition(x, y, 0, 0, true);
  }, [input, p2p]);

  const handleDevTeleport = useCallback((x: number, y: number) => {
    if (interior) return;
    if (!map) return;
    const dest = nearestOwWalkable(map, x, y);
    input.setInitialPosition(dest.x, dest.y);
    p2p.updatePosition(dest.x, dest.y, 0, 0, true);
    const f = fogRef.current;
    if (f) {
      f.revealAround(dest.x, dest.y);
      const next = f.clone();
      fogRef.current = next;
      setFog(next);
    }
  }, [interior, map, input, p2p]);

  if (!map) {
    return (
      <div style={{
        display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
        height: '100vh', background: '#0d0f07', color: '#c9a84c', fontFamily: 'Georgia, serif',
      }}>
        <h2 style={{ marginBottom: '1rem' }}>Entering the Overworld...</h2>
        <p style={{ color: '#6a5d4a', fontSize: '0.9rem' }}>
          Loading world data...
        </p>
      </div>
    );
  }

  const partyMembers = (party?.memberPeerIds || []).map(id => {
    const p = p2p.players.find(pl => pl.id === id);
    return {
      id,
      name: p?.name || (id === localId ? playerName : id.slice(0, 8)),
      isLeader: id === party?.leaderPeerId,
    };
  });

  return (
    <div style={{ position: 'relative', width: '100vw', height: '100vh', overflow: 'hidden', background: '#0d0f07' }}>
      <OverworldCanvas
        map={activeMap}
        players={interior ? displayPlayers.filter(p => p.id === resolvedLocalId) : displayPlayers}
        localPlayerId={resolvedLocalId}
        dungeonEntrances={interior ? [] : dungeonEntrances}
        worldObjects={activeObjects}
        landmarks={interior ? [] : landmarks}
        enemies={interior ? [] : enemies}
        projectiles={interior ? [] : projectiles}
        lootDrops={interior ? [] : lootDrops}
        width={typeof window !== 'undefined' ? window.innerWidth : 1280}
        height={typeof window !== 'undefined' ? window.innerHeight : 720}
        onPlayerClick={handleInvitePlayer}
        lakeDrained={lakeDrained}
        combatEnabled={!interior}
        interiorMode={!!interior}
        localFigure={playerFigure}
        secondaryAbility={stats.secondaryAbility}
        onMobilityDash={handleMobilityDash}
        onSpiritBegin={() => input.setMovementLocked(true)}
      />

      <HealthBar hp={stats.hp} maxHp={stats.maxHp} level={stats.level} />

      <StaminaBar
        stamina={stats.stamina}
        maxStamina={stats.maxStamina}
        isDepleted={stats.isStaminaDepleted}
        shieldHp={stats.shieldHp}
      />

      <XpBar xp={stats.xp} xpForNextLevel={stats.xpForNextLevel} />

      <AbilityBar
        primaryAbility={stats.primaryAbility}
        secondaryAbility={stats.secondaryAbility}
        primaryCooldown={stats.primaryCooldown}
        secondaryCooldown={stats.secondaryCooldown}
        stamina={stats.stamina}
        isDepleted={stats.isStaminaDepleted}
      />

      <SaveIndicator />

      {clientSettings.showFps && <FpsMeter />}

      {party && partyMembers.length > 0 && (
        <div style={{
          position: 'absolute', top: 12, right: 12, padding: '8px 12px',
          background: 'rgba(13, 15, 7, 0.85)', border: '1px solid #2a4a2a',
          borderRadius: 4, color: '#e8dcc8', fontSize: '0.75rem', minWidth: 140,
          zIndex: 20,
        }}>
          <div style={{ color: '#4a8c3f', fontWeight: 'bold', marginBottom: 4 }}>Party</div>
          {partyMembers.map(m => (
            <div key={m.id} style={{ display: 'flex', alignItems: 'center', gap: 4, marginBottom: 2 }}>
              {m.isLeader && <span style={{ color: '#ffd700' }}>★</span>}
              <span style={{ color: m.id === localId ? '#c9a84c' : '#9a9080' }}>{m.name}</span>
            </div>
          ))}
        </div>
      )}

      {nearbyEntrance && !interior && (
        <div style={{
          position: 'absolute', bottom: 80, left: '50%', transform: 'translateX(-50%)',
          padding: '10px 20px', background: 'rgba(60, 20, 20, 0.9)',
          border: '1px solid #c9a84c', borderRadius: 6, color: '#e8dcc8',
          textAlign: 'center', fontSize: '0.85rem',
        }}>
          <div style={{ color: '#c9a84c', fontWeight: 'bold' }}>{nearbyEntrance.name}</div>
          <div style={{ color: '#9a8b74', marginTop: 4 }}>Press <strong>E</strong> to enter</div>
        </div>
      )}

      {!nearbyEntrance && !interior && nearbyBuilding && (
        <div style={{
          position: 'absolute', bottom: 80, left: '50%', transform: 'translateX(-50%)',
          padding: '10px 20px', background: 'rgba(26, 18, 8, 0.92)',
          border: '1px solid #C9A84C', borderRadius: 6, color: '#e8dcc8',
          textAlign: 'center', fontSize: '0.85rem',
        }}>
          <div style={{ color: '#C9A84C', fontWeight: 'bold' }}>
            {nearbyBuilding.type === 'lake_shop' ? 'The Intact House' : 'A dwelling'}
          </div>
          <div style={{ color: '#9a8b74', marginTop: 4 }}>Press <strong>E</strong> to enter</div>
        </div>
      )}

      {!nearbyEntrance && !nearbyBuilding && !interior && nearbyNpc && (
        <div style={{
          position: 'absolute', bottom: 80, left: '50%', transform: 'translateX(-50%)',
          padding: '10px 20px', background: 'rgba(15, 5, 24, 0.92)',
          border: '1px solid #6B3A9E', borderRadius: 6, color: '#e8dcc8',
          textAlign: 'center', fontSize: '0.85rem',
        }}>
          <div style={{ color: '#C9A8E0', fontWeight: 'bold' }}>{dialogueFor(nearbyNpc.type)?.name || 'Someone'}</div>
          <div style={{ color: '#9a8b74', marginTop: 4 }}>Press <strong>E</strong> to speak</div>
        </div>
      )}

      {interior && !dialogue && !showShop && (
        <div style={{
          position: 'absolute', bottom: 80, left: '50%', transform: 'translateX(-50%)',
          padding: '8px 16px', background: 'rgba(13, 10, 6, 0.85)',
          border: '1px solid #4A3A22', borderRadius: 6, color: '#9a8b74',
          textAlign: 'center', fontSize: '0.8rem',
        }}>
          {interior.title} — walk south through the door to leave
        </div>
      )}

      {!nearbyEntrance && !nearbyBuilding && !nearbyNpc && nearbyAltar && !interior && (
        <div style={{
          position: 'absolute', bottom: 80, left: '50%', transform: 'translateX(-50%)',
          padding: '10px 20px', background: 'rgba(40, 20, 10, 0.9)',
          border: '1px solid #c08050', borderRadius: 6, color: '#e8dcc8',
          textAlign: 'center', fontSize: '0.85rem',
        }}>
          <div style={{ color: '#c08050', fontWeight: 'bold' }}>Meditation Altar</div>
          <div style={{ color: '#9a8b74', marginTop: 4 }}>Press <strong>E</strong> to offer to the Flame</div>
        </div>
      )}

      {pendingInvite && (
        <div style={{
          position: 'absolute', top: '30%', left: '50%', transform: 'translate(-50%, -50%)',
          padding: '16px 24px', background: 'rgba(13, 15, 7, 0.95)',
          border: '1px solid #4a8c3f', borderRadius: 8, color: '#e8dcc8',
          textAlign: 'center',
        }}>
          <div style={{ marginBottom: 8 }}>
            <strong style={{ color: '#c9a84c' }}>{pendingInvite.inviterName}</strong> invites you to a party
          </div>
          <div style={{ display: 'flex', gap: 8, justifyContent: 'center' }}>
            <button onClick={handleAcceptInvite} style={{
              padding: '6px 16px', background: '#2a4a2a', border: '1px solid #4a8c3f',
              borderRadius: 4, color: '#4a8c3f', cursor: 'pointer',
            }}>Accept</button>
            <button onClick={handleDeclineInvite} style={{
              padding: '6px 16px', background: '#4a2a2a', border: '1px solid #8c3f3f',
              borderRadius: 4, color: '#8c3f3f', cursor: 'pointer',
            }}>Decline</button>
          </div>
        </div>
      )}

      <OverworldChat
        onFocusChange={(focused) => setChatFocused(focused)}
      />

      <div style={{
        position: 'absolute', bottom: 12, left: 12, padding: '6px 10px',
        background: 'rgba(13, 15, 7, 0.7)', borderRadius: 4,
        color: '#6a5d4a', fontSize: '0.65rem',
      }}>
        WASD: Move | LMB: Attack | RMB: Secondary | E: Interact | F: Flame | I: Inventory | M: Map | ESC: Pause | Enter: Chat
      </div>

      <P2POverlay
        status={p2p.status}
        shard={p2p.shard}
        glyph={p2p.glyph}
        onGlyphConnect={p2p.connectViaGlyph}
        showGlyphOverlay={clientSettings.showGlyphOverlay}
      />

      {showPauseMenu && (
        <PauseMenu
          onResume={() => { setShowPauseMenu(false); removePanel('pause-menu'); }}
          onSettings={() => { setShowSettings(true); pushPanel('settings'); }}
          onQuit={onDisconnect}
        />
      )}

      {showMap && map && fog && (
        <OverworldMapPanel
          map={map}
          fog={fog}
          devMode={clientSettings.devMode}
          localX={input.position.x}
          localY={input.position.y}
          localId={resolvedLocalId}
          players={displayPlayers}
          landmarks={landmarks}
          onClose={() => { setShowMap(false); removePanel('overworld-map'); persistFog(); }}
          onDevTeleport={clientSettings.devMode ? handleDevTeleport : undefined}
        />
      )}

      {showSettings && (
        <SettingsPanel
          onClose={() => { setShowSettings(false); removePanel('settings'); }}
          onSaved={handleSettingsSaved}
        />
      )}

      {showInventory && (
        <InventoryPanel
          onClose={() => { setShowInventory(false); removePanel('inventory'); }}
          loadoutLocked={stats.loadoutLocked}
          primaryAbility={stats.primaryAbility}
          secondaryAbility={stats.secondaryAbility}
        />
      )}

      {showAbilitySelect && (
        <AbilitySelectPanel
          currentPrimary={stats.primaryAbility}
          currentSecondary={stats.secondaryAbility}
          onConfirm={async (primary, secondary) => {
            await fetch('/api/gameplay/swap-abilities', {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ primary, secondary }),
            });
            setShowAbilitySelect(false);
            removePanel('ability-select');
          }}
          onClose={() => { setShowAbilitySelect(false); removePanel('ability-select'); }}
        />
      )}

      {dialogue && (
        <DialoguePanel
          dialogue={dialogue.data}
          page={dialogue.page}
          onAdvance={() => setDialogue(d => d ? { ...d, page: d.page + 1 } : d)}
          onClose={() => { setDialogue(null); removePanel('dialogue'); }}
        />
      )}

      {showShop && (
        <CryptolShopPanel
          onClose={() => { setShowShop(false); removePanel('cryptol-shop'); }}
        />
      )}

      {showFlameOffering && (
        <FlameOfferingPanel
          onClose={() => { setShowFlameOffering(false); removePanel('flame-offering'); }}
        />
      )}
    </div>
  );
}

function FpsMeter() {
  const [fps, setFps] = useState(0);

  useEffect(() => {
    let frames = 0;
    let last = performance.now();
    let raf = 0;
    const loop = (now: number) => {
      frames++;
      if (now - last >= 1000) {
        setFps(frames);
        frames = 0;
        last = now;
      }
      raf = requestAnimationFrame(loop);
    };
    raf = requestAnimationFrame(loop);
    return () => cancelAnimationFrame(raf);
  }, []);

  return (
    <div style={{
      position: 'absolute', top: 12, left: 210,
      padding: '2px 6px',
      background: 'rgba(13, 15, 7, 0.7)',
      borderRadius: 3,
      color: '#6a5d4a', fontSize: '0.65rem', fontFamily: 'monospace',
    }}>
      {fps} FPS
    </div>
  );
}
