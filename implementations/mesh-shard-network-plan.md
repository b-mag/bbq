# CARCOSA Mesh Shard Network Plan

## Goal

Build a self-supporting peer-to-peer game network where players join a mesh shard, share peer connection lists with each other, and automatically move to the next shard when the current one reaches capacity.

Target design:
- Max players per shard: 100
- Default behavior: fill the current shard first
- Overflow behavior: move to shard N+1 and continue filling there
- Core gameplay remains peer-to-peer without a central game server
- Tracker is bootstrap-only, not gameplay orchestration

---

## Current State Assessment

The codebase already has the right structural direction:
- `TrackerClient` is acting as a lightweight discovery/registration layer
- `PeerIdentity` tracks the public address and local identity
- `PeerMesh` is the connection manager for peer links

What it is missing is the actual NAT traversal and traffic model needed for public internet play:
- no real STUN server/client flow
- no TURN fallback relay
- no shard capacity enforcement
- no true distributed peer gossip / membership propagation beyond initial tracker-based discovery

This means the project is conceptually close to the desired architecture, but not yet fully implemented for a production-style internet mesh.

---

## Architectural Direction

### 1. Bootstrap layer

A small bootstrap tracker service remains useful for:
- initial peer registration
- shard membership lookup
- seed peer discovery
- NAT/public-address discovery support
- shard load balancing metadata

This tracker should not host gameplay state. It does not replace peer-to-peer gameplay. It only helps players find each other and start the mesh.

### 2. Mesh membership layer

Each peer maintains a local membership table containing:
- peer ID
- public address
- listen port
- shard ID
- connection state
- last heartbeat
- whether direct or relay connection is in use

Peers periodically exchange peer lists with neighbors using a bounded gossip model. This allows the mesh to stay self-supporting even if the bootstrap tracker is unavailable.

### 3. Shard model

Each shard is a peer overlay with a hard size limit of 100 players.

Rules:
- each peer belongs to exactly one shard at a time
- if shard count is below 100, players join that shard
- if shard count reaches capacity, the system selects the next shard
- the next chosen shard is the lowest index with available room

Example:
- shard 0: 100/100
- shard 1: 42/100
- new player joins shard 1
- if shard 1 reaches 100, new player goes to shard 2

This creates natural expansion time without a central authority.

---

## NAT Traversal Design

### STUN

STUN will be used to discover the public-facing IP:port that a client appears to have from outside the local NAT.

This informs the peer identity and allows peers to attempt direct connections.

Implementation pattern:
- client sends a binding request to a public STUN server
- server responds with mapped public address
- peer stores this in `PublicAddress`
- peer advertises that address to others

### TURN

TURN is required as a fallback relay when direct P2P fails due to restrictive NATs, symmetric NATs, or firewall behavior.

Use this model:
- direct peer connection attempted first
- if direct fails, relay through TURN
- if TURN is unavailable, the peer remains isolated or falls back to tracker-assisted discovery only

This is the actual production pattern for real-world public networking.

---

## Self-Supporting Mesh Requirements

### Peer list sharing

Each peer should send a compact list of known peers, such as:
- peer ID
- address
- shard ID
- state
- last seen timestamp

These lists should be:
- rate-limited
- deduplicated
- bounded in size
- exchanged only with nearby peers

### Membership propagation

The mesh should be able to spread knowledge of new peers without requiring every peer to contact the tracker.

Recommended behavior:
- new peers announce themselves to connected neighbors
- neighbors pass along the data to a small subset of nearby peers
- stale peers are removed after heartbeat timeout

This allows the network to remain operational when the tracker is temporarily unavailable.

### Connection health checks

Every active peer should be periodically checked:
- send heartbeat
- measure responsive connections
- drop stale nodes
- reconnect to known peers if needed

This keeps the mesh stable as players leave or NAT changes occur.

---

## Shard Capacity Model

### Data structure

Each shard should maintain:
- `ShardId`
- `PeerIds`
- `PlayerCount`
- `MaxPlayers = 100`
- `Status = Active | Full`
- `LastUpdated`

### Join decision

When a peer joins:
1. determine current shard preference
2. check candidate shard occupancy
3. if candidate shard has room, assign there
4. if full, move to the next shard with capacity
5. register with the shard's peer list

### Overflow migration

Overflow behavior should be explicit:
- peers are not forced to move instantly during a full state transition
- new peers are routed to the next shard
- old shard can remain active until its members leave

This avoids churn while still filling the next shard naturally.

---

## Proposed Components

### 1. `ShardCoordinator`
Responsible for:
- shard selection
- capacity tracking
- overflow logic
- shard membership updates

### 2. `PeerMembershipManager`
Responsible for:
- local peer table
- gossip list exchange
- duplicate filtering
- health checks
- stale member cleanup

### 3. `NatTraversalService`
Responsible for:
- STUN binding discovery
- TURN fallback selection
- address validation
- direct-vs-relay decision

### 4. `TrackerBootstrapService`
Responsible for:
- registration and discovery
- seed peer list retrieval
- shard load identification
- optional public metadata publishing

### 5. `PeerMesh`
Responsible for:
- maintaining live WebSocket connections
- handshake flow
- state sync between connected peers
- connection retries

---

## Recommended Operational Flow

### Startup
1. peer starts and creates identity
2. `NatTraversalService` queries STUN
3. peer publishes public address to tracker if available
4. tracker returns peers in the same shard or nearby shard candidates
5. peer connects to seed peers

### During play
1. peer sends heartbeat to neighbors
2. peer exchanges membership lists with active peers
3. peer removes stale peers
4. peer attempts to fill missing connections in the same shard
5. if the shard is near or at capacity, peer selects the next shard

### Overflow
1. shard size reaches 100
2. new join attempts are redirected to next shard
3. shard leader or coordinator marks current shard as full
4. mesh continues operating without central interruption

---

## Reliability and Failure Handling

### Tracker outage
- peer mesh should stay alive using known peers and gossip
- tracker is not mandatory for direct gameplay

### NAT failure
- direct peer connect attempts are attempted first
- TURN fallback is attempted if direct connection is blocked
- if both fail, the peer stays in a wait/retry state

### Stale peers
- peer without heartbeat for a timeout is removed
- reconnect attempts are retried against known candidate peers

### Shard imbalance
- if one shard is overloaded, coordination can shift new peers to the next shard
- shard load metadata should be propagated periodically

---

## Implementation Milestones

### Phase 1: Discovery + NAT
- add real STUN discovery flow
- store public address in peer identity
- preserve tracker reflection as fallback
- add basic TURN config scaffolding

### Phase 2: Shard selection
- add `ShardCoordinator`
- enforce max size 100
- redirect overflow to next shard
- add shard metadata to tracker registration payload

### Phase 3: Peer gossip
- add local peer list exchange
- dedupe and bound peer list size
- maintain active connection neighborhood
- implement stale-peer cleanup

### Phase 4: Relay fallback
- add TURN client support
- treat relay as fallback only
- keep direct connection as preferred route

### Phase 5: Hardening
- test NAT edge cases
- verify shard fill/overflow behavior under simulated peer joins
- add logs and metrics for shard occupancy and connection quality

---

## Recommended Final Architecture

Use a hybrid model:
- Tracker = bootstrap and seed discovery
- STUN = public address discovery
- TURN = fallback relay
- P2P mesh = actual gameplay network
- Shards = 100-player overlays with automatic spillover
- Peer gossip = self-supporting membership propagation

This creates a reasonable self-supporting mesh architecture without forcing a central gameplay server, while still being practical for real internet conditions.

---

## Summary

The project is already close to the right architectural shape, but it needs three missing layers to become a real public mesh game network:
1. true STUN discovery
2. TURN fallback relay
3. shard-aware peer orchestration with overflow routing

Once those are in place, the network can self-support across multiple shards with a cap of 100 players each, while preserving a decentralized gameplay model.
