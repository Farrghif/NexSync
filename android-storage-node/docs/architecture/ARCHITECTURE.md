# NexSync Storage Node — System Architecture v1.0

**Document Version:** 1.0  
**Status:** Sprint 3A Baseline  
**Effective Date:** 2026-11-08  

---

## 1. LAYERED ARCHITECTURE OVERVIEW

```
┌────────────────────────────────────────────────────────────┐
│                    PRESENTATION LAYER                      │
│  (Jetpack Compose, Navigation, ViewModels)                 │
├────────────────────────────────────────────────────────────┤
│                    DOMAIN LAYER                            │
│  (UseCases, Entities, Repository Interfaces)               │
├────────────────────────────────────────────────────────────┤
│                    DATA LAYER                              │
│  (Repositories, Room DB, SAF Adapter, Network Client)      │
├────────────────────────────────────────────────────────────┤
│                 INFRASTRUCTURE LAYER                       │
│  (Hilt DI, Database schema, API clients, Local storage)    │
└────────────────────────────────────────────────────────────┘
```

### 1.1 Presentation Layer (UI)

**Components:**
- `MainActivity` — Jetpack Compose navigation host
- `OnboardingScreen` — SAF directory picker, setup flow
- `DashboardScreen` — File browser, transfer UI
- `ViewModels` — State management (Storage, Transfer, Device, Server)
- `Theme` — Material Design 3 colors, typography

**Responsibilities:**
- ✅ User interaction handling
- ✅ UI state via ViewModel
- ✅ Navigation routing

**No Business Logic:**
- ❌ File operations (deferred to domain/data)
- ❌ Network calls (deferred to data layer)
- ❌ Permission management (deferred to SAF adapter)

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

### 1.3 Data Layer (Repositories & Adapters)

**SafStorageAdapter (SAF Implementation):**
- **Responsibility:** Android Storage Access Framework integration
- **Scope:** File I/O, permissions, SAF URI handling
- **Critical Paths:**
  - `initialize()` → permission revalidation + provider check
  - `readFileStream()` → SHA-256 hashing + buffer optimization
  - `writeFileStream()` → atomic temp-file replacement
- **Constraints:**
  - Max file size: 100 MB (configurable)
  - Buffer size: 64 KB (configurable, 4-2048 KB range)
  - Temp file suffix: `.nexsync-temp`
  - Temp cleanup: 24-hour max age

**Room Repositories (9 entities):**
- StorageNodeRepository
- DeviceCredentialsRepository
- PairingChallengeRepository
- StorageDirectoryRepository
- StorageFileRepository
- FileTransferRepository
- AuthorizedDeviceRepository
- PendingOperationRepository
- ServerStateRepository

**Network Client (Retrofit + OkHttp):**
- `NexSyncApiClient` → Backend API calls
- Interceptors: JWT auth, logging, error handling
- Scope: User registration, device pairing, file sync

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
        .baseUrl("http://localhost:5000")
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

### 3.4 Atomic Temp-File Replacement

```kotlin
val tempFile = createTempFile(...)  // Write here first
// ... write complete, hash verified ...
file.delete()  // Remove original
tempFile.renameTo(file.name)  // Atomic swap
```

**Guarantee:** If process crashes, temp file remains (cleanup job picks it up)

---

## 4. NETWORK & REMOTE ACCESS

### 4.1 Backend Relay (Selected Strategy — ADR)

**Why:** Fastest MVP, reuses Backend infrastructure, simplest clients

**Flow:**
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

## 6. DEPLOYMENT READINESS

**v1.0 Baseline:** Sprint 3A complete
- ✅ Clean Architecture enforced
- ✅ Build infrastructure functional
- ✅ 19 unit tests (mock-based)
- ✅ SAF operations verified
- ✅ Permission flow tested
- ✅ ADR documented

**Not Ready for v1.0:**
- ⏳ Remote access (Sprint 4+)
- ⏳ LAN optimization (Sprint 6+)
- ⏳ Integration tests on emulator (Sprint 7+)

---

**Architecture Version:** 1.0  
**Status:** APPROVED for Sprint 4 Implementation  
**Next:** Backend Relay API specification
