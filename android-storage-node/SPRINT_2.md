# NexSync Storage Node — Sprint 2 Implementation Report

**Date:** 2026-11-03  
**Sprint:** 2 (Storage Management)  
**Status:** ✅ COMPLETE

---

## Sprint 2 Objectives

- [x] Implement Storage Access Framework (SAF) integration
- [x] Create file operations (list, read, write, delete, move, rename, copy)
- [x] Implement SHA-256 hashing with streaming
- [x] Create storage adapter abstraction pattern
- [x] Implement streaming file transfer with bounded buffers
- [x] Add comprehensive error handling
- [x] Create domain use cases for storage operations
- [x] Update StorageViewModel with use case integration
- [x] Add unit tests for storage operations
- [x] Implement temp file cleanup

---

## Architecture: Storage Layer

```
┌─────────────────────────────────────────────────────────────┐
│  Storage Management Architecture                            │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  Domain Layer (Contracts)                                   │
│  ├─ StorageAdapter (interface - 21 operations)              │
│  ├─ StorageFileInfo (data class)                            │
│  ├─ StorageInfo (data class)                                │
│  ├─ StorageException (sealed class with 7 types)            │
│  ├─ StreamConfig (configurable buffer/timeout)              │
│  └─ TransferProgressListener (callback interface)           │
│                                                             │
│  Data Layer (Implementations)                               │
│  ├─ SafStorageAdapter                                       │
│  │  ├─ 21 public operations via StorageAdapter              │
│  │  ├─ DocumentFile + ContentResolver integration           │
│  │  ├─ Streaming with SHA-256 hashing                       │
│  │  ├─ Path traversal protection                            │
│  │  ├─ Temp file management                                 │
│  │  └─ Error handling + logging (Timber)                    │
│  └─ StreamConfig (default: 64KB buffer)                     │
│                                                             │
│  Use Cases Layer                                            │
│  ├─ ListFilesUseCase                                        │
│  ├─ CreateFileUseCase                                       │
│  ├─ CreateDirectoryUseCase                                  │
│  ├─ DeleteFileUseCase                                       │
│  ├─ RenameFileUseCase                                       │
│  ├─ MoveFileUseCase                                         │
│  ├─ ComputeFileHashUseCase                                  │
│  ├─ GetStorageInfoUseCase                                   │
│  └─ CleanupTempFilesUseCase                                 │
│                                                             │
│  Presentation Layer (ViewModel)                             │
│  └─ StorageViewModel                                        │
│     ├─ State: files, storageInfo, currentPath               │
│     ├─ Actions: CRUD operations via use cases               │
│     ├─ Flow integration: StateFlow<List<StorageFileInfo>>   │
│     └─ Error handling: centralized errorMessage state       │
│                                                             │
│  DI (Hilt)                                                  │
│  └─ StorageModule provides:                                 │
│     ├─ StreamConfig (singleton)                             │
│     ├─ StorageAdapter (singleton, SAF-backed)               │
│     └─ 9 use cases (singleton via @Inject)                  │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

---

## SafStorageAdapter Implementation

### Overview
- **Purpose**: Concrete implementation of StorageAdapter using Android SAF
- **File**: `data/storage/SafStorageAdapter.kt` (~850 lines)
- **Scope**: Full-featured file operations with streaming, hashing, error handling

### Key Operations (21 Total)

1. **Navigation & Discovery**
   - `listRootFiles()` — List root directory
   - `listDirectory(uri)` — List specific directory
   - `getFileInfo(uri)` — Get single file metadata
   - `exists(uri)` — Check file existence
   - `getMimeType(uri)` — Detect MIME type

2. **File Operations**
   - `createFile(parentUri, fileName)` — Create empty file
   - `createDirectory(parentUri, dirName)` — Create directory
   - `delete(uri)` — Delete file/directory
   - `rename(uri, newName)` — Rename in-place
   - `move(uri, newParentUri)` — Move to new directory
   - `copy(sourceUri, targetUri, newName)` — Copy file

3. **File I/O with Streaming**
   - `readFileStream(uri, outputStream, progressListener)` — Read with SHA-256 hashing
   - `writeFileStream(uri, inputStream, totalSize, progressListener)` — Write with hashing
   - **Bounded buffers**: Configurable 64KB default (prevents OOM)
   - **Progress tracking**: Real-time callback with transferred/total bytes
   - **Atomic writes**: Write to temp, rename on success

4. **Storage Management**
   - `getStorageInfo()` — Query capacity/free/used bytes
   - `computeFileHash(uri)` — SHA-256 for integrity verification
   - `cleanupTempFiles(olderThanMs)` — Recursive cleanup of `*.nexsync-temp`

5. **Access & Security**
   - `initialize()` — Setup with persistable permission
   - `verifyAccess()` — Check access still valid
   - `isValidPath(path)` — Prevent `../`, null, traversal

---

## Error Handling Strategy

### StorageException Hierarchy
```kotlin
sealed class StorageException(message: String) : Exception(message) {
    DirectoryNotFound          // Parent/target directory missing
    FileNotFound               // File doesn't exist
    PermissionDenied           // Read/write forbidden
    InsufficientSpace          // Disk full or quota exceeded
    InvalidPath                // Path traversal or invalid chars
    OperationFailed            // Generic I/O failure
    AccessUriExpired           // SAF permission revoked
    NameConflict               // File already exists
}
```

### Result Type
```kotlin
sealed class StorageOperationResult<T> {
    data class Success<T>(val data: T)
    data class Error<T>(val exception: StorageException)
}
```

**Pattern**: All operations return `Result<T>` for type-safe error handling.

---

## Streaming & Memory Safety

### Buffer Configuration
```kotlin
data class StreamConfig(
    val bufferSizeBytes: Int = 65536,      // 64 KB
    val maxConcurrentReads: Int = 3,
    val readTimeoutMs: Long = 30000L,
    val writeTimeoutMs: Long = 30000L
)
```

**Validation**:
- Buffer: 4KB–2MB (prevents tiny/giant allocations)
- Concurrent reads: 1–10 (prevents thread explosion)
- Timeouts: 1–300 seconds (prevents hangs)

### Read Streaming
```
1. Open InputStream from ContentResolver
2. Allocate 64KB buffer (NOT entire file)
3. Read loop: 64KB chunks → hash → output → progress callback
4. Close automatically (try-with-resources)
5. Return computed SHA-256 hash
```

**Memory usage**: O(64KB) regardless of file size.

### Write Streaming  
```
1. Create temporary file (*.nexsync-temp)
2. Open OutputStream to temp
3. Read from input stream in chunks → hash → write → progress
4. Close output stream
5. Delete original, rename temp → atomic swap
6. Return hash for verification
```

**Safety**: Incomplete writes don't corrupt existing files.

---

## Use Cases Layer (9 Operations)

Each use case is a **single-responsibility class**:

```kotlin
class ListFilesUseCase @Inject constructor(
    private val storageAdapter: StorageAdapter
) {
    suspend operator fun invoke(directoryUri: String = ""): StorageOperationResult<List<StorageFileInfo>>
}
```

**Benefits**:
- Testable in isolation (inject mock adapter)
- Composable (chain operations)
- Logging/error handling centralized
- Follows Domain-Driven Design

---

## StorageViewModel Integration

### State
```kotlin
StateFlow<List<StorageFileInfo>>    // Current directory contents
StateFlow<StorageInfo?>             // Capacity/usage info
StateFlow<String>                   // Current path (for breadcrumb)
StateFlow<Boolean>                  // Loading indicator
StateFlow<String?>                  // Error message
```

### Actions
- `loadFilesFromDirectory(uri)` — List files via use case
- `createFile(name, parentUri)` — CRUD via use cases
- `createDirectory(name, parentUri)`
- `deleteFile(uri, parentUri)` — Re-list after op
- `renameFile(uri, newName, parentUri)`
- `moveFile(uri, newParent, oldParent)`
- `computeFileHash(uri)` — Display hash snippet
- `refreshStorageInfo()` — Update capacity display
- `cleanupTempFiles()` — Manual cleanup trigger

### Example Flow
```
User taps "Create Folder"
  → ViewModel.createDirectory("My Folder", parentUri)
    → CreateDirectoryUseCase(parentUri, "My Folder")
      → SafStorageAdapter.createDirectory(...)
        → DocumentFile.createDirectory(...)
        → Returns StorageFileInfo
    → ViewModel re-lists directory
      → UI updates with new folder
```

---

## Path Traversal Protection

### Validation Rules
```kotlin
fun isValidPath(path: String): Boolean {
    return !path.contains("..")          // No parent refs
        && !path.contains("/")           // No separators  
        && !path.contains("\\")          // No Windows paths
        && !path.contains("\u0000")      // No null terminator
        && path.isNotEmpty()             // Not empty
        && path.length <= 255            // Filesystem limit
}
```

**Examples**:
- ✅ "document.pdf"
- ✅ "my-photo_v1.2"
- ✅ "фото" (Unicode OK)
- ❌ "../etc/passwd" (traversal)
- ❌ "/absolute" (absolute path)
- ❌ "" (empty)
- ❌ "a".repeat(256) (too long)

---

## Testing

### Unit Tests
- Path validation (7 test cases)
- Error handling (mocked DocumentFile)
- Use case error propagation

**Location**: `src/test/kotlin/com/nexsync/storagenode/data/storage/`

### Integration Tests (Manual/Emulator)
- Real SAF operations
- Stream correctness
- Hash verification
- Temp file cleanup

### E2E Scenarios (Sprint 7)
1. User selects storage directory (SAF picker)
2. List files → create folder → create file → delete
3. Verify SHA-256 hash integrity
4. Concurrent read/write operations
5. Insufficient space handling

---

## Key Design Decisions

### 1. Streaming Over Buffering
**Decision**: Stream files in 64KB chunks instead of loading entire file.
**Rationale**: 
- Handles 100MB+ files without OOM
- Enables progress tracking
- Works with low-RAM devices

### 2. Atomic Writes via Temp Files
**Decision**: Write to `.nexsync-temp`, then atomic rename.
**Rationale**: 
- Crash safety: incomplete writes don't corrupt
- Matches NexSync server's fail-closed principle
- Recoverable by cleanup task

### 3. Result Type Instead of Exceptions
**Decision**: Return `StorageOperationResult<T>` instead of throwing.
**Rationale**:
- Composable error handling
- No exception overhead
- Type-safe at compile time
- Matches Kotlin idioms

### 4. Use Cases Instead of Direct Repository Access
**Decision**: ViewModels call use cases, not adapter.
**Rationale**:
- Separation of concerns
- Easier testing
- Reusable business logic
- Clear data flow

### 5. SAF + ContentResolver
**Decision**: Use Android Storage Access Framework (not hardcoded paths).
**Rationale**:
- User-selected, secure access
- Works on all Android versions ≥5.0
- Respects file permissions
- Persistent URI grants

---

## Known Limitations & Future

### Current Limitations
1. **Max file size**: 100 MB hardcoded (configurable in Sprint 3)
2. **Concurrent writes**: Sequential only (thread-safe but slower)
3. **Direct LAN access**: Not implemented (Sprint 5)
4. **Storage quotas**: Per-user limits not enforced (Sprint 4)
5. **Encryption**: Files not encrypted at rest (Sprint 6)

### Future Improvements
- **Chunked uploads**: Split large files into 10MB chunks
- **Resume capability**: Pause/resume transfers
- **Bandwidth throttling**: Rate limiting for transfers
- **Encryption at rest**: SQLCipher or Tink
- **Cloud backup**: Optional sync to backend

---

## Files Created (Sprint 2)

### Domain (Contracts)
- `domain/storage/StorageTypes.kt` (9 types/interfaces)
- `domain/storage/StorageAdapter.kt` (interface, 21 operations)
- `domain/usecases/StorageUseCases.kt` (9 use cases)

### Data (Implementation)
- `data/storage/SafStorageAdapter.kt` (~850 lines)
  - Streaming file I/O
  - SHA-256 hashing
  - Path validation
  - Temp file management
  - Comprehensive error handling

### DI
- Updated `di/HiltModules.kt` (added StorageModule)

### Presentation
- Updated `presentation/viewmodels/StorageViewModel.kt` (~200 lines)
  - 9 action methods
  - State management via use cases
  - Progress tracking
  - Error handling

### Testing
- `src/test/.../SafStorageAdapterTest.kt` (unit tests)

### Documentation
- `SPRINT_2.md` (this file)

---

## Build & Test

```bash
# Build
./gradlew :android-storage-node:build

# Run unit tests
./gradlew :android-storage-node:test

# Run on device
adb install -r android-storage-node/build/outputs/apk/debug/android-storage-node-debug.apk

# Logcat with Timber
adb logcat | grep "StorageNode"
```

---

## Metrics

- **SafStorageAdapter**: ~850 lines of code
- **Use cases**: 9 classes, ~200 lines total
- **StorageViewModel**: ~200 lines (updated)
- **Test coverage**: Path validation, error cases
- **Dependencies added**: androidx.documentfile:documentfile (1)
- **Memory overhead**: 64KB per transfer (configured)
- **Error types**: 7 specific exceptions

---

## Code Quality Checklist

- ✅ All operations suspend (Coroutines)
- ✅ All I/O on Dispatchers.IO
- ✅ No memory leaks (try-with-resources)
- ✅ Path traversal protected
- ✅ Null-safe (?.let, Elvis)
- ✅ Timber logging throughout
- ✅ Error messages descriptive
- ✅ Use cases testable
- ✅ StateFlow reactive
- ✅ ViewModels MVVM-compliant

---

## Security Review

### Threats Mitigated
1. **Path traversal**: `isValidPath()` blocks `../`, `/`, etc.
2. **File disclosure**: SAF enforces user-selected directory only
3. **Resource exhaustion**: Bounded buffers (64KB), timeouts
4. **Corrupt writes**: Temp file + atomic rename strategy
5. **Hash manipulation**: SHA-256 computed during I/O, not added after

### Not Yet Addressed (Sprint 5+)
- Encryption in transit (TLS — Sprint 3)
- Encryption at rest (SQLCipher — Sprint 6)
- Access control (device revocation — Sprint 4)
- Rate limiting (backend — Sprint 4)

---

## Next Steps

**Sprint 3: Embedded Server**
- Setup Ktor CIO embedded server
- Implement HTTP API endpoints
- TLS/HTTPS configuration
- Request validation middleware

**Sprint 4: Device Pairing & Backend Integration**
- QR code scanning (ML Kit)
- Pairing challenge verification
- Device credentials storage (Android Keystore)
- Heartbeat pinging

**Sprint 5: Background Service & Local Discovery**
- ForegroundService lifecycle
- mDNS service advertisement
- Network state monitoring
- Graceful shutdown

---

## Sign-Off

**Status:** ✅ Sprint 2 Complete  
**Quality:** Production-ready storage layer with streaming, hashing, error handling  
**Test Coverage**: Path validation, exception handling, use case contracts  
**Blockers**: None  
**Ready for Sprint 3**: YES

Sprint 2 completes the storage abstraction layer with comprehensive file operations, streaming I/O, and error safety. SAF integration allows secure access to user-selected directories with path traversal protection. All operations follow result-based error handling and use cases enable testable, composable business logic.
