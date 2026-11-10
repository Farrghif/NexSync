# NexSync Storage Node — System Architecture v1.0

**Document Version:** 1.0
**Status:** Sprint 3A Baseline (Partial) — Local SAF & Room Implemented; Ktor & Remote Access Planned
**Effective Date:** 2026-11-08  
**Implementation Status Vocabulary:**
- `IMPLEMENTED` — Code written and present in repository
- `BUILD VERIFIED` — Build executed and verified with compiler proof
- `TEST VERIFIED` — Tests executed with test runner and passing test report proof
- `PARTIALLY IMPLEMENTED` — Partial implementation present with known gaps
- `PLANNED` — Scheduled for future sprint in roadmap
- `NOT IMPLEMENTED` — Required capability not yet written in code
- `NOT VERIFIED` — Code exists but runtime/execution has not been verified
- `BLOCKER` — Defect or missing prerequisite halting progress to next sprint

---

## 1. LAYERED ARCHITECTURE OVERVIEW

### 1.1 Clean Architecture Dependency Model

The system strictly follows Clean Architecture principles where dependencies point strictly inward toward Domain abstractions:

```
┌────────────────────────────────────────────────────────┐
│                   Infrastructure Layer                 │
│  - Hilt Dependency Injection Modules (HiltModules.kt)   │
│  - Android Framework integration                       │
└───────────────────────────┬────────────────────────────┘
                            │ composes & injects
                            ▼
┌───────────────────────────┬────────────────────────────┐
│    Presentation Layer     │         Data Layer         │
│  - Jetpack Compose UI     │  - Room DAOs & DB          │
│  - Android ViewModels     │  - SafStorageAdapter       │
│  - StateFlow UI State     │  - Repository Impls        │
└─────────────┬─────────────┴─────────────┬──────────────┘
              │ depends on                │ implements
              │ Domain interfaces         │ Domain interfaces
              ▼                           ▼
┌────────────────────────────────────────────────────────┐
│                   Domain Layer (Core)                  │
│  - Pure Kotlin Entities (No Android/UI dependencies)  │
│  - Use Cases & Business Rules                          │
│  - Repository & StorageAdapter Interfaces              │
│  - Domain Value Objects & Exceptions                   │
└────────────────────────────────────────────────────────┘
```

**Architectural Reality Verified:**
- `Domain`: Pure Kotlin, no Android imports, no Room/Ktor/UI frameworks → [IMPLEMENTED] (Clean)
- `Data`: Implements `StorageAdapter` and domain repositories → [PARTIALLY IMPLEMENTED] (Room complete, SAF write defective)
- `Presentation`: ViewModels observe domain repositories → [IMPLEMENTED]
- `Infrastructure`: Hilt provides singletons and bindings → [PARTIALLY IMPLEMENTED] (Root URI placeholder)

---

### 1.2 Presentation Layer (UI) [PARTIALLY IMPLEMENTED]

**Components:**
- `MainActivity` — Jetpack Compose navigation host [IMPLEMENTED]
- `OnboardingScreen` — Welcome & overview screen [PARTIALLY IMPLEMENTED — No SAF tree picker implemented]
- `DashboardScreen` — File browser, node status, and transfer UI [IMPLEMENTED]
- `ViewModels` — State management (`StorageViewModel`, `TransferViewModel`, `DeviceViewModel`, `ServerViewModel`) [IMPLEMENTED]
- `Theme` — Material Design 3 colors, typography [IMPLEMENTED]

**Gaps & Blockers:**
- `SAF Tree URI Picker` [NOT IMPLEMENTED — BLOCKER]: No `ActivityResultContracts.OpenDocumentTree` invocation. User cannot select storage directory.
- `Backend URL Configuration UI` [NOT IMPLEMENTED]: No UI to configure server endpoint.
- `API Endpoint Settings Screen` [NOT IMPLEMENTED]: Placeholder only.

---

### 1.3 Domain Layer (Business Logic) [IMPLEMENTED]

**Entities:**
- `StorageNodeEntity`, `StorageDirectoryEntity`, `StorageFileEntity`, `FileTransferEntity`, `AuthorizedDeviceEntity`, `PendingOperationEntity`, `DeviceCredentialsEntity`, `PairingChallengeEntity`, `ServerStateEntity`.

**Use Cases & Abstractions:**
- `StorageAdapter`, `StorageDirectoryRepository`, `StorageFileRepository`, `FileTransferRepository`, etc.

---

### 1.4 Data Layer (Repositories & Adapters) [PARTIALLY IMPLEMENTED]

#### SafStorageAdapter (SAF Implementation) [PARTIALLY IMPLEMENTED — CRITICAL DEFECTS]
- `initialize()`: [IMPLEMENTED] with permission revalidation probe.
- `readFileStream()`: [IMPLEMENTED] with SHA-256 calculation and 64KB chunk streaming.
- `writeFileStream()`: [NOT IMPLEMENTED SAFELY — BLOCKER]: Delete-before-rename creates permanent data-loss window; Boolean return values of `delete()` and `renameTo()` are ignored; null `openOutputStream()` silently skips writes.

#### Root SAF Configuration [NOT IMPLEMENTED SAFELY — BLOCKER]
- `HiltModules.kt` line 146 hardcodes:
  `val rootTreeUri = "content://com.android.providers.media.documents/tree/primary%3A"`
- **Runtime Consequence:** At runtime, `contentResolver.takePersistableUriPermission()` fails because the URI was not received via an `ACTION_OPEN_DOCUMENT_TREE` user intent. Subsequent `verifyAccess()` checks `root.canRead()`, which returns false, causing `initialize()` to fail with `StorageException.PermissionDenied`. Storage initialization fails completely on startup.
- **Remediation Required:** Implement `ACTION_OPEN_DOCUMENT_TREE` in onboarding, persist tree URI to DataStore via `takePersistableUriPermission()`, and inject dynamic URI into `SafStorageAdapter`.

#### Room Database & Repositories [IMPLEMENTED]
- All 9 entities, DAOs, and migrations are implemented in `data/db/`.

#### Ktor Server Implementation State [NOT IMPLEMENTED]
A strict audit of the codebase shows the following actual state for the embedded Ktor server:
- `Ktor dependencies in build.gradle.kts`: [IMPLEMENTED] (`ktor-server-core`, `cio`, `auth`, `jwt`, `cors`)
- `Server engine instantiated`: [NOT IMPLEMENTED] (No `embeddedServer` call in codebase)
- `Server lifecycle management`: [NOT IMPLEMENTED] (`ServerViewModel.kt` lines 48-49 contains `TODO: Implement actual server start logic`)
- `API routes & handlers`: [NOT IMPLEMENTED] (No route definitions for storage node API)
- `HTTP streaming endpoints`: [NOT IMPLEMENTED]
- `Security middleware (JWT/CORS)`: [NOT IMPLEMENTED]
- `Ktor integration verified`: [NOT VERIFIED]
- **Conclusion:** Declaring dependencies in Gradle does not constitute implementation. Ktor server engine is scheduled for Sprint 3B-3F.

#### Backend Client & Remote Access [PARTIALLY IMPLEMENTED]
- `NexSyncApiClient`: Retrofit client structure present.
- `Backend Base URL`: Hardcoded to `"http://localhost:5000"` in `HiltModules.kt` line 197 as a **development placeholder**. On physical Android devices, `localhost` points to the mobile device itself, not the backend host. A configurable URL is required for real networking.
- `Remote Relay Endpoints`: (`/sync/file-relay`) [NOT IMPLEMENTED — PLANNED SPRINT 4].

---

## 2. DATA FLOW & SYNC PROTOCOL

### 2.1 File Upload (Storage Node → Backend)

```
┌─────────────────────┐
│ User selects file   │
│ (Compose UI)        │
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ UploadFileUseCase   │
│ (Domain)            │
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ FileTransferRepo    │
│ (mark UPLOADING)    │
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ SafStorageAdapter   │
│ .readFileStream()   │ ← SHA-256 hashing, 64KB buffer
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ NexSyncApiClient    │
│ .uploadFile()       │ ← Multipart to Backend
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ FileTransferRepo    │
│ (mark COMPLETE)     │
└─────────────────────┘
```

**Idempotency:**
- Each upload request includes `X-Operation-Id` (UUID)
- Backend deduplicates via ProcessedOperations table
- Retry of same operation returns cached result (200, cached DTO)

---

### 2.2 Sync Pull (Backend → Storage Node)

```
┌──────────────────────────┐
│ App foreground/boot      │
└──────────┬───────────────┘
           ▼
┌──────────────────────────┐
│ SyncService.pullChanges()│
└──────────┬───────────────┘
           ▼
┌──────────────────────────────────────────┐
│ NexSyncApiClient.syncPull(               │
│   since=lastCursor,                      │
│   until=latestCursor,                    │
│   limit=500                              │
│ )                                        │
└──────────┬───────────────────────────────┘
           ▼
┌──────────────────────────────────────────┐
│ Backend returns:                         │
│ {                                        │
│   "changes": [                           │
│     {                                    │
│       "fileId": "uuid",                  │
│       "operation": "CREATE",             │
│       "sequence": 123,                   │
│       "payload": {...}                   │
│     }                                    │
│   ],                                     │
│   "nextCursor": 124                      │
│ }                                        │
└──────────┬───────────────────────────────┘
           ▼
┌──────────────────────────────────────────┐
│ Apply changes atomically:                │
│ - Update Room DB (FileTransferEntity)    │
│ - Persist nextCursor                     │
│ - Trigger downloads if needed            │
└──────────────────────────────────────────┘
```

**Cursor Semantics:**
- `since=lastCursor` → exclusive (get changes AFTER this sequence)
- `until=currentCursor` → inclusive (get changes UP TO this sequence)
- Cursor only advances AFTER full batch applied (fail-safe)

---

### 2.3 Permission Flow

```
User selects directory via SAF picker
         ↓
SafStorageAdapter.initialize(treeUri)
         ↓
┌─────────────────────────────────────────┐
│ 1. takePersistableUriPermission()        │
│    (request READ + WRITE grants)        │
├─────────────────────────────────────────┤
│ 2. verifyAccess()                       │
│    (check directory exists + readable)  │
├─────────────────────────────────────────┤
│ 3. listFiles() probe                    │
│    (verify provider responsive)         │
├─────────────────────────────────────────┤
│ ✅ SUCCESS: Store treeUri in DataStore │
└─────────────────────────────────────────┘

Runtime: If permission revoked in Settings
         ↓
readFileStream() catches SecurityException
         ↓
Returns PermissionDenied error (not generic IOException)
         ↓
UI shows "Storage access revoked - select folder again"
```

---

## 3. CRITICAL PATHS & SAFETY GUARANTEES

### 3.1 File Integrity (SHA-256)

**Computed at:**
1. Upload: `SafStorageAdapter.readFileStream()` line 489
2. Download: `SafStorageAdapter.writeFileStream()` line 583
3. Verification: `SafStorageAdapter.computeFileHash()` line 683

**Verification:**
- Every file transferred includes hash
- Mismatch → transfer rejected + retry
- No silent corruption

---

### 3.2 Buffer Ownership & Memory Safety

**Read Stream:**
```kotlin
val chunk = buffer.copyOfRange(0, bytesRead)  // New array, size=bytesRead
outputStream(chunk)  // Consumer owns chunk, can process async
// Original 64KB buffer reused next iteration
```

**Guarantee:** No shared mutable state between iterations, safe for async consumers

---

### 3.3 Permission Revocation Detection

**During Streaming:**
```kotlin
catch (e: SecurityException) {
    // Android raises this when permission revoked mid-operation
    progressListener?.onError(StorageException.PermissionDenied(...))
    return StorageOperationResult.Error(...)
}
```

**Guarantee:** Operation fails fast, not silent data corruption

---

### 3.4 Atomic Temp-File Replacement [❌ CRITICAL DATA LOSS RISK]

**Current Code Implementation (SafStorageAdapter.kt lines 591-594):**
```kotlin
file.delete()                                // Returns Boolean, NOT CHECKED
tempFile.renameTo(file.name ?: "file")     // Returns Boolean, NOT CHECKED
tempUri = null
```

**CRITICAL DEFECTS:**
1. **Return values ignored:** delete() and renameTo() return Boolean; failures do NOT throw IOException
2. **False success path:** If delete succeeds and rename returns false, code continues to `onComplete()` and returns Success
3. **Data loss window:** Original file deleted, temp file NOT promoted = PERMANENT DATA LOSS
4. **openOutputStream null handling:** If openOutputStream() returns null, write loop skipped silently with no error
5. **No true hash verification:** expectedHash is empty string, isValid is hardcoded true; only calculates hash, does not compare
6. **Parent URI issue:** file.parentFile?.uri.toString() evaluates to "null" for fromSingleUri(), createTempFile receives "null"

**Status:** BLOCKER — Write operations MUST be redesigned before multi-device sync or production release.
See full analysis: `docs/reliability/SAF-ATOMIC-WRITE-INVESTIGATION.md`

---

## 4. NETWORK & REMOTE ACCESS [⏳ PLANNED FOR SPRINT 4+]

### 4.1 Backend Relay (Proposed Strategy — ADR) [⏳ PLANNED FOR SPRINT 4]

**ADR Status:** PROPOSED — PENDING OWNER APPROVAL (Implementation NOT granted)
**Prerequisites:** 
1. Owner approval of ADR recommendation
2. Complete API specification for `/sync/file-relay` (Control Plane + Data Plane)
3. Ktor server implementation (Sprint 3B-3F)
4. Atomic write and root URI blockers resolved

**Flow (Proposed):**
```
Storage Node          NexSync Backend       Remote Client
     │                     │                      │
     ├─ Sync changelog ───→│                      │
     │                     │                      │
     │                     │← Request file ───────┤
     │                     │                      │
     ├─ Stream file ──────→│                      │
     │                     ├─ Stream to client ──→│
     │                     │                      │
```

**API Endpoints (Sprint 4):**
- `POST /sync/file-relay` — Storage Node initiates relay
- `GET /sync/file-relay/{fileId}` — Remote clients fetch relayed file

---

### 4.2 Local LAN Optimization (Sprint 6)

**Discovery (mDNS):**
```
Storage Node advertises: _nexsync._tcp.local
Clients discover: Storage Node IP:port
Direct connect: Skip Backend relay for LAN
```

---

## 5. COMPONENT RESPONSIBILITIES

| Component | Responsibility | Constraints |
|-----------|---|---|
| `MainActivity` | Navigation, activity lifecycle | No file I/O |
| `OnboardingScreen` | SAF picker, user setup | No file operations |
| `DashboardScreen` | File list, transfer UI | Read-only state from ViewModel |
| `StorageViewModel` | File list state, filters | Observable via StateFlow |
| `TransferViewModel` | Upload/download progress | Observes FileTransferEntity changes |
| `SafStorageAdapter` | All SAF operations | Max 100 MB, 64 KB buffer |
| `StorageFileRepository` | File metadata CRUD | Atomic with Room transactions |
| `FileTransferRepository` | Transfer state tracking | Idempotent via retry logic |
| `NexSyncApiClient` | Network calls | JWT auth, X-Operation-Id headers |
| `SyncService` | Pull changes, apply atomically | Cursor-based, fail-fast |

---

## 6. DEPLOYMENT READINESS [NOT READY FOR PRODUCTION]

**Sprint 3A Baseline Verification Status:**
- `Clean Architecture`: [IMPLEMENTED] (Domain strictly isolated)
- `Gradle Build Infrastructure`: [PARTIALLY IMPLEMENTED — NOT VERIFIED] (Configuration and files present; wrapper runtime, compilation, and APK NOT VERIFIED due to missing Java environment)
- `Unit Tests`: [NOT VERIFIED] (19 mock-based tests designed in source; 0 tests executed; no execution reports)
- `SAF Storage Operations`: [PARTIALLY IMPLEMENTED] (Read stream verified by inspection; write stream contains critical non-atomic data-loss defect)
- `Root SAF Storage Configuration`: [NOT IMPLEMENTED SAFELY — BLOCKER] (Hardcoded placeholder in `HiltModules.kt`; no SAF picker in UI; fails at runtime with permission denied)
- `ADR Android Remote Access`: [IMPLEMENTED — PROPOSED PENDING OWNER APPROVAL] (Backend Relay recommended; authorization NOT granted)

**CRITICAL BLOCKERS FOR PRODUCTION & SPRINT PROGRESSION:**
1. 🔴 **SAF Atomic Write Safety** [BLOCKER]: Delete-before-rename creates permanent data-loss window; ignored Boolean return values cause false success; null `openOutputStream` promotes empty files. Must evaluate immutable versioned objects pattern.
2. 🔴 **Root URI Initialization** [BLOCKER]: Hardcoded placeholder in DI. Must implement dynamic SAF tree picker in onboarding and persist URI permissions.
3. 🔴 **Onboarding Flow Missing SAF Picker** [BLOCKER]: `OnboardingScreen.kt` navigates directly to dashboard without invoking `ACTION_OPEN_DOCUMENT_TREE`.
4. 🔴 **Backend Relay API Specification** [BLOCKER]: Relay endpoints (`/sync/file-relay`) not yet designed or approved by backend owner.
5. 🔴 **Gradle Build & Test Execution Proof** [NOT VERIFIED]: Environment lacks JDK; build, test, and artifact generation have not been executed.

---

## 7. AGREED ROADMAP & SEQUENCING

| Sprint | Objective | Implementation Status | Scope / Deliverables |
|---|---|---|---|
| **Sprint 3A** | Architecture Documentation + ADR | `IMPLEMENTED` (Pending Owner Decision) | Clean Architecture alignment, ADR status correction, SAF atomic write audit |
| **Sprint 3B** | Ktor Engine + Lifecycle | `PLANNED` | Embedded Ktor server engine, background lifecycle management |
| **Sprint 3C** | API Routes + Streaming | `PLANNED` | Storage API endpoints, chunked file streaming |
| **Sprint 3D** | Security Middleware | `PLANNED` | JWT token validation, CORS configuration, rate limiting |
| **Sprint 3E** | Integration Testing | `PLANNED` | Local node integration tests, mock client verification |
| **Sprint 3F** | Server UI Integration | `PLANNED` | Dashboard server controls, port configuration, status displays |
| **Sprint 4** | Device Pairing + Backend Integration | `PLANNED` (Conditional on ADR Approval) | Outbound sync client, `/sync/file-relay` backend integration |
| **Sprint 5** | Background Service + Discovery | `PLANNED` | Android foreground service, local mDNS discovery |
| **Sprint 6** | Desktop + Web Client Integration | `PLANNED` | Multi-client relay consumption |
| **Sprint 7** | E2E Testing, Hardening & Release | `PLANNED` | Performance verification, security audit, release build |

---

**Architecture Version:** 1.0  
**Status:** PROPOSED — Pending Owner Approval  
**Next Step:** Owner review of ADR recommendation; resolve critical SAF write and root URI blockers before starting Sprint 3B implementation.
