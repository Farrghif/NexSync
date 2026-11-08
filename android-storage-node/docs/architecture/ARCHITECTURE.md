# NexSync Storage Node — System Architecture v1.0

**Document Version:** 1.0  
**Status:** Sprint 3A Baseline (Partial) — Local SAF + Room Complete; Remote Access & Ktor Planned  
**Effective Date:** 2026-11-08  
**Implementation Status Vocabulary:** [✅ IMPLEMENTED] [⏳ PLANNED] [❌ NOT IMPLEMENTED] [⚠️ PARTIAL]

---

## 1. LAYERED ARCHITECTURE OVERVIEW

**Clean Architecture Dependency Model:**

Domain layer (innermost) — contains business logic, NO dependencies on Android/Room/Ktor/Retrofit/UI
   ↑ (implementations depend inward)
Data layer — implements Domain interfaces, repositories
   ↑
Presentation layer — depends on Domain abstractions via DI
   ↑
Infrastructure — Hilt modules provide concrete implementations

**Code reality verified:**
- ✅ Domain: No Android imports, no frameworks → CLEAN
- ✅ Data: Implements StorageAdapter, StorageRepository interfaces → CLEAN
- ✅ Presentation: ViewModels depend on repositories → CLEAN
- ✅ Infrastructure: HiltModules instantiate implementations → CORRECT

### 1.1 Presentation Layer (UI) [✅ IMPLEMENTED — Sprint 3A]

**Components:**
- `MainActivity` — Jetpack Compose navigation host [✅]
- `OnboardingScreen` — SAF directory picker, setup flow [⚠️ PARTIAL — no SAF picker implemented]
- `DashboardScreen` — File browser, transfer UI [✅]
- `ViewModels` — State management (Storage, Transfer, Device, Server) [✅]
- `Theme` — Material Design 3 colors, typography [✅]

**Responsibilities:**
- ✅ User interaction handling
- ✅ UI state via ViewModel
- ✅ Navigation routing

**Not Implemented:**
- ❌ SAF tree URI picker (user cannot select storage folder)
- ❌ Backend URL configuration UI
- ❌ API endpoint settings screen

---

### 1.2 Domain Layer (Business Logic)

**Entities:**
```kotlin
data class StorageNodeEntity(
    val id: UUID,
    val nodeId: String,
    val userId: UUID,
    val createdAt: LocalDateTime
)

data class StorageDirectoryEntity(
    val id: UUID,
    val nodeId: UUID,
    val uri: String,      // SAF tree URI
    val lastSyncAt: LocalDateTime?
)

data class StorageFileEntity(
    val id: UUID,
    val parentUri: String,
    val name: String,
    val hash: String,     // SHA-256
    val size: Long,
    val isTemp: Boolean,
    val lastModifiedAt: LocalDateTime
)

data class FileTransferEntity(
    val id: UUID,
    val fileId: UUID,
    val status: TransferStatus, // PENDING, UPLOADING, COMPLETE, FAILED
    val direction: TransferDirection, // UPLOAD, DOWNLOAD
    val progressBytes: Long,
    val totalBytes: Long,
    val retryCount: Int,
    val errorMessage: String?
)
```

**UseCases (Interfaces):**
```kotlin
interface SelectStorageDirectoryUseCase
interface ListStorageFilesUseCase
interface ReadFileStreamUseCase
interface UploadFileUseCase
interface ComputeFileHashUseCase
interface SyncChangesUseCase
```

**Storage Adapter Interface:**
```kotlin
interface StorageAdapter {
    suspend fun initialize(): StorageOperationResult<Unit>
    suspend fun listRootFiles(): StorageOperationResult<List<StorageFileInfo>>
    suspend fun readFileStream(
        fileUri: String,
        outputStream: suspend (ByteArray) -> Unit,
        progressListener: TransferProgressListener?
    ): StorageOperationResult<HashVerificationResult>
    suspend fun writeFileStream(...)
    suspend fun computeFileHash(fileUri: String): StorageOperationResult<String>
    // ... 16 more operations
}
```

**Exception Hierarchy:**
```
StorageException
├── PermissionDenied
├── FileNotFound
├── DirectoryNotFound
├── InvalidPath
├── NameConflict
├── InsufficientSpace
└── OperationFailed
```

---

### 1.3 Data Layer (Repositories & Adapters) [⚠️ PARTIALLY IMPLEMENTED]

**SafStorageAdapter (SAF Implementation) [⚠️ PARTIAL]:**
- **Status:** IMPLEMENTED with critical gaps
- **Responsibility:** Android Storage Access Framework integration
- **Scope:** File I/O, permissions, SAF URI handling
- **Critical Paths:**
  - `initialize()` → permission revalidation + provider check [✅ IMPLEMENTED]
  - `readFileStream()` → SHA-256 hashing + buffer optimization [✅ IMPLEMENTED]
  - `writeFileStream()` → atomic temp-file replacement [❌ NOT ATOMIC — see BLOCKERS]
- **Constraints:**
  - Max file size: 100 MB (configurable)
  - Buffer size: 64 KB (configurable, 4-2048 KB range)
  - Temp file suffix: `.nexsync-temp`
  - Temp cleanup: 24-hour max age

**CRITICAL ISSUES:**
- ⚠️ Root SAF URI: Hardcoded placeholder, not user-selected [LINE: HiltModules.kt:146]
- 🔴 Atomic write: Non-atomic delete-then-rename, Boolean returns not checked [LINE: SafStorageAdapter.kt:592-593]
- ⚠️ Output stream: null return silently skips write [LINE: SafStorageAdapter.kt:578]
- ⚠️ Hash verification: expectedHash empty, isValid always true [LINE: SafStorageAdapter.kt:601-602]

**Room Repositories (9 entities) [✅ IMPLEMENTED]:**
- All 9 repositories complete and tested

**Network Client (Retrofit + OkHttp) [⚠️ PARTIAL]:**
- NexSyncApiClient infrastructure complete [✅]
- File sync endpoints (basic CRUD) [✅]
- **Remote relay endpoints:** [❌ NOT IMPLEMENTED]
  - `/sync/file-relay` — not designed
  - `/sync/metadata-sync` — not designed

---

### 1.4 Infrastructure Layer (DI & Configuration)

**Hilt Modules:**
```kotlin
@Module
@InstallIn(SingletonComponent::class)
object StorageModule {
    @Provides
    @Singleton
    fun provideSafStorageAdapter(
        context: Context,
        preferences: DataStore<Preferences>
    ): StorageAdapter = SafStorageAdapter(context, ...)
}

@Module
@InstallIn(SingletonComponent::class)
object DatabaseModule {
    @Provides
    @Singleton
    fun provideDatabase(context: Context): AppDatabase = 
        Room.databaseBuilder(context, AppDatabase::class.java, "nexsync.db")
            .addMigrations(*AppDatabase.migrations)
            .build()
}

@Module
@InstallIn(SingletonComponent::class)
object NetworkModule {
    @Provides
    @Singleton
    fun provideNexSyncApi(
        okHttpClient: OkHttpClient
    ): NexSyncApiClient = Retrofit.Builder()
        .baseUrl("http://localhost:5000") // ⚠️ DEVELOPMENT PLACEHOLDER ONLY — physical Android needs configurable URL
        .client(okHttpClient)
        .addConverterFactory(KotlinxSerializationConverterFactory())
        .build()
        .create(NexSyncApiClient::class.java)
}
```

**Configuration (DataStore):**
```
nexsync.preferences
├── storage_root_uri: String (SAF tree URI)
├── device_id: UUID
├── device_name: String
├── sync_enabled: Boolean
├── buffer_size_bytes: Int (default 65536)
└── api_base_url: String
```

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

## 6. DEPLOYMENT READINESS [🔴 NOT READY FOR PRODUCTION]

**v1.0 Baseline (Sprint 3A status):**
- ✅ Clean Architecture enforced (Domain independent)
- ✅ Gradle build infrastructure files present (runtime NOT verified)
- ⏳ 19 unit tests designed (NOT executed — Java unavailable in environment)
- ⚠️ SAF operations: read-path functional, write-path has critical data-loss defect
- ⚠️ Root URI: hardcoded placeholder, no onboarding picker
- ✅ ADR documented (status: PROPOSED — PENDING OWNER APPROVAL)

**BLOCKERS FOR PRODUCTION:**
1. 🔴 SAF atomic write safety (delete-then-rename data loss window)
2. 🔴 Root URI initialization (placeholder instead of user selection)
3. 🔴 Onboarding flow (no SAF picker implementation)
4. 🔴 Backend Relay API specification (not yet designed)
5. 🔴 Gradle build/test execution proof (environment lacks Java)

**NOT YET IMPLEMENTED:**
- ⏳ Ktor Server engine and API routes (Sprint 3B-3F)
- ⏳ Device pairing (Sprint 4)
- ⏳ Remote access via Backend Relay (Sprint 4)
- ⏳ LAN optimization via mDNS (Sprint 6)
- ⏳ Integration tests on emulator/device (Sprint 3E/7)

---

**Architecture Version:** 1.0  
**Status:** PROPOSED — Pending Owner Approval  
**Next:** Sprint 3B (Ktor Engine) AFTER critical blockers resolved
