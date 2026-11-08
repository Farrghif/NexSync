# Architecture Decision Record: Android Remote Access Strategy

**Date:** 2026-11-08  
**Status:** PENDING DECISION  
**Scope:** NexSync Storage Node v1.0 Remote Access Architecture  
**Affected Component:** Backend relay, device communication, network traversal  

---

## DECISION CONTEXT

NexSync Storage Node currently supports:
- ✅ Local SAF file access (same device)
- ✅ Local HTTP API (loopback)
- ✅ Embedded Ktor server (port-based communication)

**Missing:** Cross-network communication between Storage Node and other NexSync clients (Desktop, Web, Mobile MAUI).

**Problem Statement:** How should Storage Node expose files to remote NexSync clients without direct WAN access?

---

## EVALUATION CRITERIA

| Criterion | Weight | Rationale |
|-----------|--------|-----------|
| Security | 10/10 | User file exposure on untrusted networks |
| Reliability | 9/10 | Unpredictable home networks, firewall behavior |
| Throughput | 7/10 | Large file transfers (100MB+), real-time sync |
| Cost | 8/10 | Infrastructure, bandwidth, developer overhead |
| Complexity | 8/10 | Implementation time, maintenance burden |
| User Experience | 8/10 | Setup friction, configuration, troubleshooting |

---

## CANDIDATE APPROACHES

### APPROACH A: Backend Relay (NexSync API Proxy)

**Architecture:**
```
┌─────────────────────┐
│  Storage Node       │
│  (Android, local)   │
└──────────┬──────────┘
           │
           ▼ (outbound only)
┌─────────────────────────────────────┐
│  NexSync Backend API                │
│  (PostgreSQL, ASP.NET Core)         │
│  - File relay (multipart upload)    │
│  - Metadata sync (changelog)        │
│  - Quota management                 │
└──────────┬────────────────────────────┘
           │
    ┌──────┴──────────┬──────────┐
    ▼                 ▼          ▼
Desktop (WPF)    Web (React)   Mobile MAUI
```

**How It Works:**
1. Storage Node connects outbound to Backend API (reverse tunnel not needed)
2. File metadata pushed to Backend (ChangeLog, device registry)
3. Remote clients request files from Backend (Backend proxies to Storage Node)
4. Backend buffers or streams large transfers
5. Bandwidth metered at Backend (can apply quotas)

**Security:**
- ✅ No inbound firewall holes required
- ✅ TLS on all connections (Backend ↔ Storage Node, Clients ↔ Backend)
- ✅ JWT auth per device identity
- ✅ Storage Node never exposes raw SAF URIs to untrusted clients
- ✅ Backend validates file access per user ownership
- ⚠️ Backend becomes single point of trust (must be secure)

**Reliability:**
- ✅ NAT/firewall transparent (outbound always works)
- ✅ Works on cellular, tethering, airport WiFi
- ✅ Client disconnects don't cascade
- ⚠️ Backend downtime = total sync loss
- ⚠️ Network split between Storage Node and Backend → data unavailable

**Throughput:**
- ✅ No bandwidth throttling (unless Backend rate-limits)
- ✅ Backend can use efficient binary protocols
- ⚠️ File transfers serialized through Backend (not P2P)
- ⚠️ Double bandwidth on Backend (incoming from Storage Node, outgoing to clients)

**Cost:**
- 💰 Backend infrastructure must scale with file transfers
- 💰 Bandwidth amplification (file transferred twice through Backend)
- 💰 Storage for temporary buffers
- ✅ No additional client-side complexity
- ✅ No P2P discovery/NAT traversal complexity

**Complexity:**
- ✅ Least complex: reuse existing Backend API
- ✅ Storage Node: simple outbound HTTP/WebSocket
- ✅ Clients: familiar REST/sync patterns
- ✅ No new protocol needed

**Dev Effort:** ~2-3 weeks (Backend middleware + Storage Node outbound integration)

---

### APPROACH B: VPN (Site-to-Site)

**Architecture:**
```
┌────────────────────────────────────────┐
│  Virtual Private Network (WireGuard,   │
│  OpenVPN, Tailscale)                   │
└────────────────────────────────────────┘
   │                        │
   ▼                        ▼
Storage Node        NexSync Clients
(Android)           (Desktop, Web, Mobile)
   │                        │
   └────────────────────────┘
   Private subnet (e.g., 10.0.0.0/8)
   Direct access to Storage Node API
```

**How It Works:**
1. User installs VPN client on all devices (Storage Node, Desktop, Web browser, Mobile MAUI)
2. All devices join shared virtual network
3. Storage Node listens on private IP (e.g., `10.0.0.2:8080`)
4. Clients access Storage Node directly over private network
5. Files transferred peer-to-peer (no relay)

**Security:**
- ✅ Peer-to-peer encrypted (VPN layer)
- ✅ Private network isolated from WAN
- ✅ Can use certificate-based auth (WireGuard public keys)
- ⚠️ VPN credential management (who controls VPN server?)
- ⚠️ User must manage VPN for each trusted device

**Reliability:**
- ✅ Direct communication (no single point of failure)
- ✅ Storage Node and clients can operate independently
- ⚠️ VPN server downtime → cannot add new devices
- ⚠️ User responsible for VPN uptime
- ⚠️ Mobile (Android) VPN requirement: heavy battery drain

**Throughput:**
- ✅ Direct P2P (no Backend relay)
- ✅ Full bandwidth between Storage Node and client
- ✅ No double-amplification on backend
- ⚠️ VPN overhead (encryption/decryption on limited Android CPU)

**Cost:**
- 💰 VPN infrastructure (self-hosted or paid SaaS like Tailscale)
- 💰 Developer overhead (VPN configuration, troubleshooting)
- ✅ No Backend bandwidth amplification

**Complexity:**
- ❌ High user friction: setup VPN on each device
- ❌ High dev complexity: VPN integration, credential rotation
- ❌ Troubleshooting difficult (network layer issues)
- ⚠️ Android MAUI: VPN permission model complicated
- ⚠️ Web browser: browser-based VPN client (limited options)

**Dev Effort:** ~4-6 weeks (VPN setup, client integration, Android permissions)

---

### APPROACH C: Direct Peer-to-Peer (NAT Traversal)

**Architecture:**
```
┌─────────────────┐
│ STUN/TURN       │ (NAT discovery, relay if needed)
│ Server          │
└────────┬────────┘
         │
    ┌────┴───────────┬─────────────┐
    ▼                ▼             ▼
Storage Node     Desktop        Web (WebRTC)
(mDNS/UPnP)      (direct if LAN)
    │                │             │
    └────────────────┴─────────────┘
    Direct P2P connections
    (or TURN relay if NAT blocks)
```

**How It Works:**
1. Storage Node publishes availability via mDNS (local network discovery)
2. Storage Node registers with STUN/TURN server (WAN-side discovery)
3. Clients query STUN for Storage Node address
4. Direct connection attempted (UDP hole punching or TCP traversal)
5. Falls back to TURN relay if direct fails

**Security:**
- ✅ No central Backend relay (less trusted server)
- ✅ End-to-end encrypted (TLS/DTLS)
- ⚠️ Complex NAT traversal logic (attack surface)
- ⚠️ STUN/TURN server must be trusted (sees connection metadata)

**Reliability:**
- ✅ No single point of failure
- ✅ LAN clients connect directly (fast, resilient)
- ⚠️ WAN clients depend on STUN/TURN (if direct fails)
- ⚠️ NAT rules unpredictable (some networks block UDP, etc.)
- ⚠️ Symmetric NAT blocks hole punching (falls back to TURN)

**Throughput:**
- ✅ Direct P2P (full bandwidth if connection succeeds)
- ⚠️ TURN relay degrades throughput (like Backend Relay)
- ⚠️ Unreliable throughput (depends on NAT behavior)

**Cost:**
- 💰 STUN/TURN infrastructure (or use free services with limitations)
- 💰 High development cost (NAT traversal, fallback logic)
- ✅ Reduced Backend bandwidth (if direct P2P works)

**Complexity:**
- ❌ Very high complexity: NAT traversal algorithms, fallback logic
- ❌ High dev effort: STUN/TURN client integration
- ❌ Testing difficult (simulate various NAT scenarios)
- ❌ Troubleshooting nightmare (is it NAT? Firewall? DNS?)

**Dev Effort:** ~6-8 weeks (STUN/TURN integration, NAT traversal, fallback states)

---

## COMPARISON MATRIX

| Criterion | Backend Relay | VPN | Direct P2P |
|-----------|---|---|---|
| **Security** | 9/10 | 9/10 | 8/10 |
| **Reliability** | 7/10 | 7/10 | 6/10 |
| **Throughput** | 7/10 | 8/10 | 9/10 |
| **Cost** | 6/10 | 7/10 | 5/10 |
| **Complexity** | 9/10 | 6/10 | 3/10 |
| **UX** | 9/10 | 5/10 | 7/10 |
| **Dev Effort (weeks)** | 2-3 | 4-6 | 6-8 |
| **Time to First MVP** | ✅ Fastest | ⏳ Medium | ❌ Slowest |

---

## RECOMMENDATION

**Phase 1 (Sprint 4-5): Backend Relay (Primary)**
- Rationale: Fastest to MVP, reuses existing Backend, simplest client logic
- Risk: Backend infrastructure cost (mitigated by caching, CDN, smart buffering)
- Path: Extend NexSync.API with `/sync/file-relay` endpoint

**Phase 2 (Sprint 6+): Direct LAN Optimization**
- Rationale: mDNS local discovery + direct connect when same LAN
- Benefit: Fast local transfers without Backend relay
- Fallback: Still uses Backend for WAN clients

**Phase 3 (Sprint 7+): Optional VPN/P2P**
- Rationale: Advanced users who want full control or self-hosting
- Not required for v1.0

---

## DECISION

**Selected Approach:** Backend Relay (Approach A)

**Rationale:**
1. ✅ MVP fastest (v1.0 target)
2. ✅ Reuses existing Backend infrastructure
3. ✅ Simplest client integration
4. ✅ Most reliable (no NAT surprises)
5. ✅ Scalable (backend can cache, deduplicate)

**Constraints:**
- Backend must handle file relay efficiently (streaming, not buffering entire file)
- Storage Node must authenticate to Backend (OAuth or service account)
- Clients must trust Backend as file provider

**Future Optimization:**
- Sprint 6+: Add LAN discovery (mDNS) for local-only transfers
- Sprint 7+: Add optional Direct P2P for advanced users

---

## IMPLEMENTATION ROADMAP

| Sprint | Task | Ownership |
|--------|------|-----------|
| Sprint 4 | Backend `/sync/file-relay` API | Backend team |
| Sprint 4 | Storage Node outbound sync client | Android team |
| Sprint 5 | Desktop/Web relay client integration | Desktop/Web team |
| Sprint 6 | Local LAN optimization (mDNS) | Android team |
| Sprint 7+ | Optional VPN/P2P (backlog) | TBD |

---

**ADR Status:** APPROVED (Backend Relay)  
**Next Action:** Detailed API spec for `/sync/file-relay` endpoint
