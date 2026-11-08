# POST-SPRINT 3A ARCHITECTURE REVIEW & CORRECTION
**NexSync Storage Node — Final Architecture Documentation Alignment**

**Date:** 2026-11-08  
**Status:** Final review with corrective documentation  
**Baseline:** Commit `3a5c6e9`  
**Directive:** Correct ADR status, align roadmap, identify blockers  

---

## PHASE C: ADR CORRECTION

### Finding: ADR Status Contradiction

**Location:** `android-storage-node/docs/architecture/ADR-ANDROID-REMOTE-ACCESS.md`

**The Contradiction:**
```
Line 4:    Status: PENDING DECISION
Line 261:  Selected Approach: Backend Relay (Approach A)
Line 293:  ADR Status: APPROVED (Backend Relay)
```

**Root Cause:** 
- Recommendation section (lines 261-268) shows decision was made
- Final status label (line 293) claims APPROVED
- Front-matter (line 4) was not updated

**Correction Required:**

Change line 4 from:
```
**Status:** PENDING DECISION
```

To:
```
**Status:** PROPOSED — PENDING OWNER APPROVAL

**Recommendation:** Backend Relay (Approach A) — Fastest to MVP
**Implementation Status:** NOT GRANTED — Authorization pending
**Decision Owner:** Project Owner
**Implementation Timeline:** Sprint 4-5 (if approved)
```

Also remove the unsupported claim at line 293. Replace:
```
**ADR Status:** APPROVED (Backend Relay)
```

With:
```
**ADR Status:** PROPOSED — Owner decision pending

**Next Steps:**
1. Project owner reviews recommendation
2. Owner approves or selects alternative
3. If approved: Backend team produces `/sync/file-relay` API spec
4. If approved: Android team begins Sprint 4 implementation
```

---

### Backend Relay Feasibility Assessment

**Current Claim (Line 243-246):**
> Phase 1 (Sprint 4-5): Backend Relay (Primary)
> - Rationale: Fastest to MVP, reuses existing Backend, simplest client logic
> - Path: Extend NexSync.API with `/sync/file-relay` endpoint

**Feasibility Analysis:**

#### Control Plane (Storage Node ↔ Backend)

**Required (NOT YET DESIGNED):**
- [ ] Storage Node authentication to Backend (service account / device credential / OAuth)
- [ ] Heartbeat mechanism (keep connection alive through NAT)
- [ ] Job dispatch protocol (Backend tells Storage Node "client X wants file Y")
- [ ] Connection lifecycle (reconnect logic, exponential backoff)
- [ ] Device revocation (invalidate Storage Node if compromised)

**Proposed Design:**
```
1. Storage Node authentication:
   - Option A: Service account with long-lived token
   - Option B: Device ID + shared secret (from pairing)
   - Option C: mTLS certificate (if self-hosted)
   → DECISION NEEDED: Which mechanism?

2. Outbound connection from Storage Node:
   - WebSocket? (persistent, bidirectional)
   - Long-poll? (simpler, less resource-intensive)
   - gRPC? (typed, efficient)
   → DECISION NEEDED: Which protocol?

3. Backend needs to track:
   - Which Storage Nodes are online
   - Which user owns each Storage Node
   - Heartbeat timestamps
   - Last successful sync
```

**Risk:** If not designed correctly, Storage Node connection state becomes unreliable and sync fails.

---

#### Data Plane (File Transfer)

**Required (NOT YET DESIGNED):**
- [ ] `/sync/file-relay` endpoint specification
- [ ] Streaming vs. buffering strategy (can't buffer 100MB files)
- [ ] Quota management (per Storage Node, per user)
- [ ] Temporary data expiration (how long to keep relayed files in Backend cache)
- [ ] File integrity verification (checksum validation)
- [ ] Error handling (Storage Node offline, file moved, permission revoked)

**Proposed Flow:**

```
Scenario: Desktop client requests file from Storage Node

1. Desktop → Backend: "Give me file {id} from Storage Node {nodeId}"
2. Backend looks up Storage Node connection state
   - If offline: Return 503 "Storage Node unavailable"
   - If online: Proceed
3. Backend → Storage Node (over outbound connection): "Stream file {id} to relay"
4. Storage Node reads file from SAF in chunks
5. Storage Node → Backend: Chunks (streamed)
6. Backend → Desktop: Chunks (streamed)
7. All three parties verify SHA-256 independently
```

**Questions:**
- Does Backend buffer entire file or stream direct?
  - Streaming: Lower memory, but complex coordination
  - Buffering: Simpler, but 100MB × N users = massive cache
- If Storage Node goes offline mid-transfer, what happens?
- How does Desktop verify file integrity during relay?
- What if Desktop's connection drops? Can it resume from Backend cache?

**Risk:** If not designed correctly, large files fail to transfer or cause Backend memory exhaustion.

---

### Assessment: Is Backend Relay Viable for Sprint 4?

**Based on Design Readiness:**

| Component | Status | Ready for Sprint 4? |
|-----------|--------|---|
| Control Plane design | ⏳ PENDING | ❌ NO — Decision needed on auth, protocol |
| Data Plane design | ⏳ PENDING | ❌ NO — Spec needed for streaming |
| Storage Node outbound client | ✅ READY | ✅ YES — Retrofit already added |
| Backend API skeleton | ✅ READY | ✅ YES — Can extend existing API |
| Desktop/Web client changes | ⏳ PENDING | ❌ NO — Depends on Backend spec |

**Verdict:** Sprint 4 implementation possible IF design decisions are made **before** Sprint 4 starts.

**Critical Path Item:** Backend Relay API Specification (must be written by Backend team BEFORE Sprint 4 kickoff)

---

## PHASE D: ARCHITECTURE DOCUMENTATION CORRECTION

### Finding #1: Ktor Implementation Status Mismatch

**Current Claims:**

| Source | Claim | Line |
|--------|-------|------|
| ADR | "✅ Embedded Ktor server (port-based communication)" | 15 |
| ARCHITECTURE.md | "Ktor Server | 2.3.9 | Latest" | 136 |
| SPRINT_2_1_FINAL_EVIDENCE.md | "✅ No Ktor implementation (Sprint 3 blocker)" | 286 |

**Reality:**
- Dependencies added: ✅ YES (`build.gradle.kts` lines 100-104)
- Server instantiation: ❌ NO (ServerViewModel.kt line 48: "TODO: Implement")
- Route definitions: ❌ NO (no Ktor route code found)
- Application module: ❌ NO (no Ktor Application class)

**Correction Needed:**

1. **ADR (Line 15):** Change from:
```
- ✅ Embedded Ktor server (port-based communication)
```
To:
```
- ⏳ PLANNED: Embedded Ktor server (Sprint 3B-3F implementation)
```

2. **ARCHITECTURE.md (Section 4.1):** Add status marker:
```markdown
### 4.1 Backend Relay (Selected Strategy — ADR) [⏳ PLANNED FOR SPRINT 4]

**Current Status:** Architecture decided, implementation not yet started
**Ktor Server Dependency:** Added (build.gradle.kts), server NOT instantiated
**Implementation Timeline:** 
- Sprint 3B: Ktor engine + lifecycle
- Sprint 3C: API routes + streaming
- Sprint 3D: Security middleware
- Sprint 4+: Integration with Backend relay
```

3. **Documentation:** Add clear section marking implementation status:
```markdown
## IMPLEMENTATION STATUS BY COMPONENT

| Component | Status | Notes |
|-----------|--------|-------|
| SAF Storage Adapter | ✅ IMPLEMENTED (Sprint 2) | 21 operations, 19 unit tests |
| Room Database | ✅ IMPLEMENTED (Sprint 2) | 9 entities, schema complete |
| Jetpack Compose UI | ✅ IMPLEMENTED (Sprint 3A) | Screens, ViewModels |
| Ktor Server Engine | ⏳ PLANNED (Sprint 3B) | Dependencies added, not started |
| Storage Node → Backend Client | ⏳ PLANNED (Sprint 4) | Retrofit added, not implemented |
| Backend Relay Integration | ⏳ PLANNED (Sprint 4) | API spec needed first |
| Device Pairing | ❌ NOT IMPLEMENTED | Sprint 4 |
| Local LAN Discovery (mDNS) | ❌ NOT IMPLEMENTED | Sprint 6+ |
| Direct P2P | ❌ NOT IMPLEMENTED | Sprint 7+ (optional) |
```

---

### Finding #2: Layered Architecture Dependency Direction

**Current Claim (ARCHITECTURE.md lines 10-25):**

Shows layers as simple top-down dependency chain:
```
Presentation Layer
        ↓
    Domain Layer
        ↓
    Data Layer
        ↓
Infrastructure Layer
```

**Issue:** This is misleading. Clean Architecture actually has:
- Presentation → Domain (abstraction)
- Data implements Domain (repository interfaces)
- Infrastructure provides concrete implementations
- **Domain has NO dependencies on other layers**

**Correction:**

Replace with:
```markdown
## 1. CLEAN ARCHITECTURE DEPENDENCY MODEL

Domain layer is the **innermost** — contains business logic and abstractions:
- ✅ Domain depends on: Nothing (no Android, no DB, no network)
- ❌ Domain cannot depend on: Android framework, Room, Ktor, Retrofit

Data layer implements Domain repository interfaces:
- ✅ Data depends on: Domain (implements interfaces)
- ❌ Data cannot depend on: Presentation

Presentation layer depends on Domain and Data:
- ✅ Presentation depends on: Domain (interfaces), Data (via DI)
- ❌ Presentation cannot depend on: Direct database/network calls

Infrastructure layer provides concrete implementations:
- ✅ Infrastructure depends on: All layers (implements abstractions)
- Hilt DI module coordinates dependencies

**Dependency Graph (arrows show "depends on"):**
```
Presentation
     ↓
   Data  ← Repository implementations
     ↓
  Domain ← Interfaces, entities, business logic (no external deps)
     ↓
Infrastructure ← Concrete Hilt modules, DI setup
```

**Verification (Source Code Check):**
- ✅ Domain layer: No Android imports, no Room, no Ktor → CLEAN
- ✅ Data layer: Implements StorageAdapter, StorageRepository → CLEAN
- ✅ Presentation layer: ViewModels depend on repositories → CLEAN
- ⚠️ HiltModules: Instantiates concrete implementations → CORRECT

---

### Finding #3: Root Tree URI Hardcoding

**Current Documentation (ARCHITECTURE.md lines 203-211):**

```markdown
**Configuration (DataStore):**
nexsync.preferences
├── storage_root_uri: String (SAF tree URI)
├── device_id: UUID
├── device_name: String
├── sync_enabled: Boolean
├── buffer_size_bytes: Int (default 65536)
└── api_base_url: String
```

**Reality (HiltModules.kt lines 144-146):**
```kotlin
// TODO: In Sprint 4, get rootTreeUri from preferences/pairing
val rootTreeUri = "content://com.android.providers.media.documents/tree/primary%3A"
```

**Issue:** Code uses hardcoded placeholder, not preferences.

**Correction:**

Add section to ARCHITECTURE.md:
```markdown
### CRITICAL: Root URI Initialization [❌ NOT YET IMPLEMENTED]

**Current State:**
- SafStorageAdapter initialized with placeholder URI
- Real SAF URI (from user folder selection) NOT persisted
- No SAF picker implementation found

**This is a BLOCKER for any multi-device sync.**

**Until Fixed:**
- All file operations go to placeholder folder (likely wrong/non-existent)
- User cannot select actual storage location
- Files appear backed up but are in wrong place

**Required Before Sprint 4:**
1. Implement SAF tree picker in OnboardingScreen
2. Persist selected URI to DataStore
3. Read from DataStore in HiltModules (replace placeholder)
4. Validate URI on app startup
5. Handle permission revocation gracefully

**Roadmap Impact:** Blocks Sprint 3B+ implementation until resolved
```

---

### Finding #4: Backend URL Configuration

**Current Code (HiltModules.kt line 195):**
```kotlin
.baseUrl("http://localhost:5000")
```

**Issue:** Hardcoded localhost. Won't work for:
- Physical Android device
- Production deployment
- Any scenario where Backend ≠ local device

**Documentation:** Add note to ARCHITECTURE.md:
```markdown
### Backend URL Configuration [⚠️ DEVELOPMENT ONLY]

**Current:** `http://localhost:5000` (hardcoded)

**This is development-only and must be changed:**

**Until Fixed:**
- Storage Node can only connect to Backend on same device (localhost)
- Physical Android device cannot find Backend on desktop
- Production deployment impossible

**Required Before Sprint 4:**
1. Read api_base_url from DataStore preferences
2. Add BuildConfig variants for dev/prod:
   - dev: http://localhost:5000
   - prod: https://api.nexsync.io (or custom domain)
3. Add settings UI to configure API endpoint
4. Do NOT use runBlocking in Hilt providers

**Roadmap Impact:** Must be fixed before any multi-device testing
```

---

### Finding #5: Roadmap Alignment

**Current ADR (Lines 243-289):**
```
| Sprint | Task | Ownership |
|--------|------|-----------|
| Sprint 4 | Backend `/sync/file-relay` API | Backend team |
| Sprint 4 | Storage Node outbound sync client | Android team |
```

**Correction:** Align with approved roadmap:

```
| Sprint | Task | Status | Ownership |
|--------|------|--------|-----------|
| 3A | Architecture documentation + ADR | ✅ COMPLETE | Architecture |
| 3B | Ktor engine + lifecycle | ⏳ NOT STARTED | Android |
| 3C | API routes + streaming | ⏳ NOT STARTED | Android |
| 3D | Security middleware | ⏳ NOT STARTED | Android |
| 3E | Integration tests | ⏳ NOT STARTED | Android |
| 3F | Server UI integration | ⏳ NOT STARTED | Android |
| 4 | Device pairing + Backend integration | ⏳ NOT STARTED | Backend + Android |
| 5 | Background service + discovery | ⏳ NOT STARTED | Android |
| 6 | Desktop + Web integration | ⏳ NOT STARTED | Desktop + Web |
| 7 | E2E testing + release | ⏳ NOT STARTED | All teams |
```

**Key Points:**
- ✅ Ktor is Sprint 3B-3F, NOT Sprint 4
- ✅ Backend relay spec needed BEFORE Sprint 4
- ✅ Root URI initialization BLOCKER for all sprints
- ✅ Atomic write safety BLOCKER for production

---

## PHASE E-G: CORRECTIVE DOCUMENTATION COMPLETE

### Documents Created

1. ✅ **SPRINT_2_1_FINAL_EVIDENCE_RECONCILIATION.md**
   - Verifies Sprint 2.1 claims against actual source
   - Identifies root URI placeholder as BLOCKER
   - Identifies atomic write as BLOCKER
   - Identifies onboarding flow as MISSING

2. ✅ **SAF-ATOMIC-WRITE-INVESTIGATION.md**
   - Deep analysis of delete-then-rename non-atomicity
   - Failure scenarios with data loss
   - Proposed solutions (versioned files pattern)
   - Recommendation: Solution A before any production deployment

3. ✅ **POST_SPRINT_3A_ARCHITECTURE_REVIEW.md** (this document)
   - ADR correction (PENDING OWNER APPROVAL)
   - Architecture doc corrections
   - Roadmap alignment
   - Blocker identification

---

## THREE SEPARATE VERDICTS

### VERDICT 1: Sprint 2.1 Technical Readiness

| Component | Status | Evidence |
|-----------|--------|----------|
| Build Infrastructure | ✅ READY | Gradle, wrapper verified |
| SAF Adapter (21 ops) | ✅ DESIGNED | Source complete, tests designed |
| Room Database (9 entities) | ✅ READY | Schema, DAOs, migrations complete |
| Jetpack Compose UI | ✅ READY | Screens and ViewModels functional |
| Unit Tests (19 mock-based) | ⏳ DESIGNED (not executed) | Java environment missing |
| **Root URI Initialization** | ❌ BROKEN | Uses placeholder, not user-selected |
| **Atomic Write Safety** | ❌ UNSAFE | Delete-then-rename creates data-loss window |
| **Onboarding Flow** | ❌ MISSING | No SAF picker, no URI persistence |

**VERDICT:** ⚠️ **PARTIAL PASS** — Design complete, runtime initialization broken

**Recommendation:** Fix root URI + atomic write before deploying to multiple devices

---

### VERDICT 2: Sprint 3A Documentation Readiness

| Aspect | Status |
|--------|--------|
| ADR status clarity | ❌ NEEDS CORRECTION (PENDING vs APPROVED) |
| Ktor status accuracy | ❌ NEEDS CORRECTION (claimed implemented, actually TODO) |
| Root URI documentation | ❌ NEEDS CORRECTION (claims preferences, actually hardcoded) |
| Backend URL documentation | ❌ NEEDS CORRECTION (not flagged as dev-only) |
| Implementation status markers | ❌ NEEDS CORRECTION (no [✅/⏳/❌] demarcation) |
| Roadmap alignment | ⚠️ PARTIAL (Sprint 4 assumed, should be 3B-3F) |
| Atomic write strategy | ❌ MISSING (needs SAF-ATOMIC-WRITE-INVESTIGATION.md) |
| Backend relay feasibility | ⏳ NEEDS DESIGN (Control/Data planes not specified) |

**VERDICT:** ❌ **NOT READY** — Multiple corrections required before approval

**Recommendation:** Update ARCHITECTURE.md + ADR with corrections listed in PHASE D

---

### VERDICT 3: NexSync Storage Node Production Readiness

| Criterion | Status | Blocker? |
|-----------|--------|----------|
| Local file access (SAF) | ✅ DESIGNED | No |
| File integrity (SHA-256) | ✅ DESIGNED | No |
| Local database persistence | ✅ IMPLEMENTED | No |
| **Root URI initialization** | ❌ BROKEN | ✅ **YES** |
| **Atomic write safety** | ❌ UNSAFE | ✅ **YES** |
| **Onboarding UX** | ❌ MISSING | ✅ **YES** |
| Multi-device sync | ❌ NOT STARTED | ✅ YES (after blockers fixed) |
| Remote file relay | ❌ NOT DESIGNED | ✅ YES (needs API spec) |
| Backend integration | ❌ NOT STARTED | ✅ YES (after Ktor) |

**VERDICT:** 🔴 **NOT READY FOR PRODUCTION**

**Critical Blockers:**
1. Root URI initialization (uses placeholder folder)
2. Atomic write safety (data loss risk)
3. Onboarding flow (user can't select folder)
4. Backend relay API specification (not designed)

**Recommendation:** 
- **Do NOT proceed to Sprint 4 until blockers #1-2 are fixed**
- Reorder work: Fix critical issues before new features
- Re-evaluate production readiness after corrections

---

## CORRECTIVE ACTIONS REQUIRED

### Immediate (This Sprint)

- [ ] Update ADR line 4: Change from "PENDING DECISION" to "PROPOSED — PENDING OWNER APPROVAL"
- [ ] Update ADR line 293: Add clarification that owner approval not yet granted
- [ ] Update ARCHITECTURE.md: Mark Ktor as PLANNED (Sprint 3B), not IMPLEMENTED
- [ ] Update ARCHITECTURE.md: Mark root URI as BROKEN (uses placeholder)
- [ ] Add [BLOCKER] flags to critical issues in documentation

### Before Sprint 3B Starts

- [ ] Implement SAF folder picker in OnboardingScreen
- [ ] Implement DataStore persistence for selected URI
- [ ] Redesign atomic write to use versioned files pattern
- [ ] Add Root URI validation on app startup
- [ ] Execute unit tests with proper Java/Gradle environment

### Before Sprint 4 Starts

- [ ] Backend team produces `/sync/file-relay` API specification
- [ ] Specification covers Control Plane (auth, heartbeat, job dispatch)
- [ ] Specification covers Data Plane (streaming, quota, error handling)
- [ ] Android team reviews spec, identifies unknowns
- [ ] Roadmap reviewed and approved

---

## CONCLUSION

**Sprint 3A Deliverables:**
- ✅ Architecture documentation framework
- ✅ ADR decision recorded (with caveats)
- ❌ Documentation accuracy issues identified
- ❌ Critical blockers discovered

**Critical Gap:** Runtime initialization (root URI) and data safety (atomic writes) are broken before any multi-device sync is attempted.

**Next Steps:**
1. ✅ Create corrective documentation (DONE)
2. ⏳ Owner reviews findings and approves ADR (PENDING)
3. ⏳ Fix critical blockers before Sprint 3B
4. ⏳ Execute tests with proper environment

**No Sprint 3B Implementation** until:
- [ ] Root URI initialization fixed and tested
- [ ] Atomic write redesigned and tested
- [ ] Onboarding flow complete
- [ ] Architecture doc corrections applied

---

**Report Generated:** 2026-11-08T13:00 UTC  
**Baseline:** Commit `3a5c6e9`  
**Status:** DOCUMENTATION REVIEW COMPLETE  
**Recommendation:** STOP implementation, fix blockers, re-evaluate before Sprint 3B
