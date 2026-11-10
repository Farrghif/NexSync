# SAF Atomic Write Investigation & Reliability Assessment

**Date:** 2026-11-08  
**Status:** CRITICAL DATA LOSS RISK IDENTIFIED  
**Scope:** `SafStorageAdapter.writeFileStream()` implementation analysis  
**Severity:** 🔴 BLOCKER — Do not deploy until resolved  

---

## EXECUTIVE SUMMARY

The current `writeFileStream()` implementation claims to perform atomic file replacement using temporary files. However, detailed analysis reveals a **non-atomic deletion/rename sequence that creates a data-loss window**.

**Critical Issue:** If the rename operation fails after the original file is deleted, the original file is permanently lost and replaced with nothing.

**Risk Assessment:** Under typical Android/SAF provider conditions, this scenario is not just possible — it's likely under certain failure modes (permission changes, provider errors, process termination).

---

## ARCHITECTURE DOCUMENTATION CLAIMS

**From `ARCHITECTURE.md` Section 3.4 (Line 383-392):**

```markdown
### 3.4 Atomic Temp-File Replacement

val tempFile = createTempFile(...)  // Write here first
// ... write complete, hash verified ...
file.delete()  // Remove original
tempFile.renameTo(file.name)  // Atomic swap

Guarantee: If process crashes, temp file remains (cleanup job picks it up)
```

**Stated Guarantee:** "Atomic swap" — implies all-or-nothing behavior.

---

## ACTUAL SOURCE CODE ANALYSIS

**Location:** `SafStorageAdapter.kt` lines 541-625

### Current Implementation

```kotlin
override suspend fun writeFileStream(
    fileUri: String,
    inputStream: suspend () -> ByteArray?,
    totalSizeBytes: Long,
    progressListener: TransferProgressListener?
): StorageOperationResult<HashVerificationResult> = withContext(Dispatchers.IO) {
    try {
        // Validation checks (lines 548-563)
        if (totalSizeBytes > MAX_FILE_SIZE) { /* ... */ }
        val uri = Uri.parse(fileUri)
        val file = DocumentFile.fromSingleUri(context, uri) ?: /* error */
        if (!file.canWrite()) { /* error */ }
        
        val hashDigest = MessageDigest.getInstance("SHA-256")
        val transferredBytes = AtomicLong(0L)
        var tempUri: Uri? = null
        
        try {
            // PHASE 1: Create temporary file
            val tempFile = createTempFile(file.parentFile?.uri.toString(), file.name ?: "temp")
                ?: return@withContext StorageOperationResult.Error(/* ... */)
            
            tempUri = Uri.parse(tempFile.uri)
            
            // PHASE 2: Write data to temporary file
            contentResolver.openOutputStream(tempUri)?.use { outputStream ->
                while (true) {
                    val chunk = inputStream()
                    if (chunk == null || chunk.isEmpty()) break
                    
                    hashDigest.update(chunk)
                    outputStream.write(chunk)
                    
                    transferredBytes.addAndGet(chunk.size.toLong())
                    progressListener?.onProgress(transferredBytes.get(), totalSizeBytes)
                }
            }
            
            // PHASE 3: Replace original with temp (NON-ATOMIC)
            file.delete()                                    // Line 592: DELETE original
            tempFile.renameTo(file.name ?: "file")         // Line 593: RENAME temp
            tempUri = null
            
            progressListener?.onComplete()
            
            val computedHash = hashDigest.digest().joinToString("") { "%02x".format(it) }
            StorageOperationResult.Success(HashVerificationResult(
                computedHash = computedHash,
                expectedHash = "",
                isValid = true
            ))
        } catch (e: IOException) {
            // PHASE 4: Error handling
            tempUri?.let { uri ->
                try {
                    DocumentFile.fromSingleUri(context, uri)?.delete()
                } catch (ex: Exception) {
                    Timber.w(ex, "Failed to clean up temp file")
                }
            }
            progressListener?.onError(StorageException.OperationFailed("Write failed: ${e.message}", e))
            StorageOperationResult.Error(/* ... */)
        }
    } catch (e: Exception) { /* ... */ }
}
```

---

## CRITICAL FAILURE SCENARIO ANALYSIS

### 1. Return-Value Handling Defect & Silent False Success

AndroidX `DocumentFile.delete()` and `DocumentFile.renameTo()` return `Boolean`. Ordinary failures do **NOT** throw `IOException` or any exception at all.

**Actual Code (`SafStorageAdapter.kt` lines 591-597):**
```kotlin
file.delete()                                // Returns Boolean, result ignored
tempFile.renameTo(file.name ?: "file")     // Returns Boolean, result ignored
tempUri = null

progressListener?.onComplete()

val computedHash = hashDigest.digest().joinToString("") { "%02x".format(it) }
StorageOperationResult.Success(HashVerificationResult(
    computedHash = computedHash,
    expectedHash = "",
    isValid = true
))
```

#### Detailed Scenario Breakdown:

- **Scenario 1A: Delete returns `false` (Delete fails)**
  - Cause: Permission revoked, file locked, or provider restriction.
  - Execution: `file.delete()` returns `false` silently. No exception is thrown.
  - Next step: `tempFile.renameTo(...)` attempts to rename the temporary file to the original file's name. Because the original file still exists, most providers will reject the rename (returning `false`) or create a collision document (e.g., `filename (1)`).
  - Outcome: `tempUri` is set to `null`, `progressListener?.onComplete()` is called, and `StorageOperationResult.Success` is returned.
  - Result: False success reported. The original file was not updated, the temp file was not promoted, and reference to `tempUri` is lost for cleanup.

- **Scenario 1B: Delete succeeds (`true`), Rename returns `false` (Rename fails)**
  - Cause: Provider rejects rename, name collision, or provider-specific naming constraints.
  - Execution: Original file is deleted permanently. `tempFile.renameTo(...)` returns `false` without throwing an exception.
  - Next step: `tempUri = null` runs, clearing the reference. `progressListener?.onComplete()` is invoked.
  - Outcome: Function returns `StorageOperationResult.Success`.
  - Result: **PERMANENT DATA LOSS WITH FALSE SUCCESS**. The original file is destroyed, the temporary file is left orphaned with `.nexsync-temp` suffix, and the caller is falsely informed that write succeeded.

- **Scenario 1C: Rename succeeds but changes the Document URI / MIME Type**
  - Android SAF DocumentProvider contract explicitly permits providers to return a new `Uri` or create a new document representation upon `renameTo()`.
  - In `SafStorageAdapter.kt`, `tempFile.renameTo(...)` is called, but the resulting URI is never captured or returned. Any caller or database entity holding the original URI now holds a dangling/invalid URI. Furthermore, on certain providers, MIME types are altered based on filename extensions.

- **Scenario 1D: Provider throws an Unchecked Exception after `delete()`**
  - If the provider throws a `RuntimeException` or `SecurityException` during `tempFile.renameTo()`:
  - Execution jumps to catch block. In the catch block:
    ```kotlin
    tempUri?.let { uri ->
        DocumentFile.fromSingleUri(context, uri)?.delete()
    }
    ```
  - The temp file is deleted during cleanup, but the original file was ALREADY deleted at line 592!
  - Result: **PERMANENT DATA LOSS**. Both the original and temporary files are now gone.

- **Scenario 1E: Output Stream returns `null`**
  - Cause: Provider crashed, storage unmounted, or URI invalid.
  - Execution: `contentResolver.openOutputStream(tempUri)?.use { ... }` skips the entire write block silently via safe-call `?.`.
  - Next step: Zero bytes written (`transferredBytes` = 0). `file.delete()` deletes the original file. `tempFile.renameTo(...)` promotes an EMPTY 0-byte temporary file.
  - Result: **DATA LOSS**. Original file replaced with an empty 0-byte file without any exception thrown.

- **Scenario 1F: Process Crashes Between `delete()` and `renameTo()`**
  - Execution: `file.delete()` succeeds. Before `renameTo()` executes, the Android OS kills the process (OOM killer, low memory, device power-off, user force-stop).
  - Restart: The original file is gone. The temp file remains orphaned on disk (`.nexsync-temp`). App metadata has no record of the crash.
  - Result: **UNRECOVERABLE DATA LOSS**. Catch block cleanup is bypassed because the process terminated abruptly.

- **Scenario 1G: SAF Permission Revoked During Finalization**
  - If permissions are revoked in Android Settings between `delete()` and `renameTo()`, `renameTo()` fails (returns `false` or throws `SecurityException`).
  - Result: Original file deleted, new file cannot be promoted. Data lost.

---

### 2. Parent URI and DocumentFile Type Defect

**Actual Code (`SafStorageAdapter.kt` lines 555 & 571):**
```kotlin
val uri = Uri.parse(fileUri)
val file = DocumentFile.fromSingleUri(context, uri) ?: return@withContext StorageOperationResult.Error(...)
...
val tempFile = createTempFile(file.parentFile?.uri.toString(), file.name ?: "temp")
```

**Tracing `createTempFile()`:**
```kotlin
private fun createTempFile(parentUri: String, originalName: String): DocumentFile? {
    return try {
        val parent = if (parentUri.isEmpty()) {
            rootDocumentFile
        } else {
            DocumentFile.fromTreeUri(context, Uri.parse(parentUri))
        }
        val tempFileName = "${originalName}${TEMP_FILE_SUFFIX}"
        val mimeType = getMimeTypeFromFileName(originalName)
        parent?.createFile(mimeType, tempFileName)
    } catch (e: Exception) {
        Timber.e(e, "Failed to create temp file")
        null
    }
}
```

**Technical Finding:**
1. `file` is instantiated using `DocumentFile.fromSingleUri(context, uri)`.
2. Under AndroidX DocumentFile implementation, `SingleDocumentFile` has no concept of a parent tree and its `getParentFile()` method **always returns `null`**.
3. In Kotlin, `file.parentFile?.uri.toString()` evaluates with safe navigation: `file.parentFile` is `null`, so `file.parentFile?.uri` is `null`, and `(null).toString()` or safe call produces the literal string `"null"`.
4. In `createTempFile()`, `parentUri` receives the string `"null"`.
5. Since `"null".isEmpty()` is `false`, it executes `DocumentFile.fromTreeUri(context, Uri.parse("null"))`.
6. `Uri.parse("null")` is not a valid hierarchical tree URI. `DocumentFile.fromTreeUri()` returns `null` or a dysfunctional wrapper.
7. Consequently, `parent?.createFile(...)` fails and returns `null`.
8. `createTempFile()` returns `null`, causing `writeFileStream()` to immediately exit at line 572 with:
   `StorageOperationResult.Error(StorageException.OperationFailed("Failed to create temporary file"))`.
9. **Conclusion:** Because `SingleDocumentFile` has no parent, `writeFileStream()` fails to create temporary files unless the target file was created from a tree-based document (`TreeDocumentFile`) or `parentUri` explicitly defaults to `rootDocumentFile`.

---

### 3. Hash Integrity & Verification Reality

**Actual Code (`SafStorageAdapter.kt` lines 598-603):**
```kotlin
val computedHash = hashDigest.digest().joinToString("") { "%02x".format(it) }
StorageOperationResult.Success(HashVerificationResult(
    computedHash = computedHash,
    expectedHash = "",
    isValid = true
))
```

**Technical Distinctions:**
- **Hash Calculation (Implemented):** The code computes SHA-256 over incoming stream chunks via `hashDigest.update(chunk)`.
- **Expected-Hash Comparison (NOT IMPLEMENTED):** `expectedHash` is hardcoded to `""` (empty string) and `isValid` is hardcoded to `true`. No comparison against an expected hash provided by the caller or server is performed.
- **Transferred Byte Count Verification (NOT IMPLEMENTED):** `transferredBytes` is accumulated during chunk streaming, but it is never checked against `totalSizeBytes`. If the stream terminates prematurely, the code still proceeds to finalize.
- **Persisted-Content Verification (NOT IMPLEMENTED):** The hash is computed from stream data in flight, not from the persisted file on disk. If the output stream buffered or dropped data during flush/close, the actual file on disk may be corrupt or truncated despite returning `isValid = true`.

---

### 4. Output Stream Null Return Analysis

**Actual Code (`SafStorageAdapter.kt` line 578):**
```kotlin
contentResolver.openOutputStream(tempUri)?.use { outputStream ->
    while (true) {
        val chunk = inputStream()
        if (chunk == null || chunk.isEmpty()) break
        hashDigest.update(chunk)
        outputStream.write(chunk)
        transferredBytes.addAndGet(chunk.size.toLong())
        progressListener?.onProgress(transferredBytes.get(), totalSizeBytes)
    }
}
```

Per Android Developers documentation for `ContentResolver.openOutputStream(Uri)`:
- The method can return `null` if the target content provider has crashed, unmounted, or cannot open the resource.
- Because `?.use` is used, a `null` return value causes the entire lambda block to be skipped without throwing any exception.
- When skipped, `transferredBytes` remains `0`, `hashDigest` remains unpopulated, and execution falls through to `file.delete()`, deleting the original file and promoting a 0-byte empty file.
- **Requirement:** A `null` return from `openOutputStream()` must be treated as an immediate write failure (`StorageException.OperationFailed`), aborting the operation before touching any existing file.

---

### Scenario 2: Process Crashes Between Delete and Rename

**Execution Sequence:**

```
1. tempFile written successfully
2. file.delete() executes
   → SUCCESS: Original file deleted
3. App crashes (memory error, OOM killer, user force-stop)
   → Before renameTo() executes
4. On app restart:
   → Original file is gone (deleted)
   → Temp file still exists (not cleaned up)
   → Database thinks file doesn't exist
5. User loses original file
   → DATA LOSS ❌
```

**Note:** Current code only cleans up tempUri in catch block. Crash between delete + rename bypasses catch block.

---

### Scenario 3: Provider Revokes Access During Finalization

**Execution Sequence:**

```
1. tempFile written successfully
2. Hash verified
3. SAF provider permission revoked (user changes settings)
4. file.delete() throws SecurityException
   → Caught as IOException
5. Catch block deletes tempUri
6. RESULT: Temp file deleted, but original still exists
   → ACCEPTABLE (no data loss, but operation fails)
```

**Better outcome than Scenario 1, but still failure.**

---

### Scenario 4: Temp File and Original Have Same Name (Edge Case)

**Execution Sequence:**

```
1. tempFile created with suffix ".nexsync-temp"
   → Name: "document.pdf.nexsync-temp"
2. Data written, hash verified
3. file.delete() removes "document.pdf"
4. tempFile.renameTo("document.pdf") called
   → Should succeed (different names)
5. BUT if renameTo() implementation is naive:
   → Might interpret names as same in case-insensitive filesystem
   → Operation might fail unexpectedly
```

**Risk:** Low, but non-zero on certain filesystems.

---

## COMPARISON: SAF DOCUMENT API GUARANTEES

### What DocumentFile Methods Actually Guarantee

**`DocumentFile.delete()`:**
- Returns `Boolean` (true = success, false = failure)
- No atomicity guarantee
- Can fail if permission revoked
- Deletes immediately (not deferred)

**`DocumentFile.renameTo()`:**
- Returns `Boolean` (true = success, false = failure)
- No atomicity guarantee
- Can fail if permission changed
- Not a true rename (might be copy + delete internally)

**Critical:** Neither method provides atomic "delete-then-rename" semantics.

---

### What SAF Does NOT Guarantee

```
❌ Atomic multi-step file operations
❌ Rollback capabilities
❌ Transaction semantics
❌ Consistency after partial failures
❌ Protection against permission revocation mid-operation
```

**Fact:** SAF operations are individual, not transactional.

---

## FAILURE MODE MATRIX

| Scenario | Delete Result | Rename Result | Original File | Temp File | Data Loss? |
|----------|---|---|---|---|---|
| Both succeed | ✅ | ✅ | ✅ Replaced | ✅ Removed | ❌ NO |
| Delete succeeds, Rename fails | ✅ | ❌ | ❌ **DELETED** | ❌ **DELETED** | ✅ **YES** |
| Delete fails, Rename skipped | ❌ | - | ✅ Original | ✅ Remains | ❌ NO |
| Process crash between | ✅ | ⚠️ | ❌ **DELETED** | ✅ Remains | ✅ **YES** |
| Permission revoked | ❌ | ❌ | ✅ Original | ✅ Remains | ❌ NO |

**Only 2 out of 5 scenarios = safe. 3 scenarios = DATA LOSS.**

---

## ROOT CAUSE ANALYSIS

**Why is this dangerous?**

1. **No Transaction Support in SAF:** SAF DocumentFile API provides no multi-step transaction support. Each operation is independent.

2. **Delete is Irreversible:** Once deleted, file cannot be recovered (unless provider has trash/recycle bin, which is provider-specific).

3. **Rename is Not Guaranteed:** Rename can fail for many reasons unrelated to the temp file (permissions, provider state, race conditions).

4. **Catch Block Doesn't Help:** Catch block only fires on EXCEPTIONS, not on false return values from delete/rename.

5. **Code Assumes Success:** Lines 592-593 assume both operations will succeed without checking return values:
   ```kotlin
   file.delete()  // No check if this succeeded
   tempFile.renameTo(file.name)  // No check if this succeeds
   ```

---

## CURRENT MITIGATIONS (INSUFFICIENT)

### Mitigation #1: Temp File Cleanup

**Code (lines 605-611):**
```kotlin
tempUri?.let { uri ->
    try {
        DocumentFile.fromSingleUri(context, uri)?.delete()
    } catch (ex: Exception) {
        Timber.w(ex, "Failed to clean up temp file")
    }
}
```

**Effectiveness:** ⏳ PARTIAL
- Cleans up temp file if exception occurs
- **Does NOT restore original file** (already deleted)
- **Doesn't catch false returns** (delete/rename return false without exception)

---

### Mitigation #2: Initial Validation

**Code (lines 548-563):**
```kotlin
if (totalSizeBytes > MAX_FILE_SIZE) { /* ... */ }
val uri = Uri.parse(fileUri)
val file = DocumentFile.fromSingleUri(context, uri) ?: return@withContext StorageOperationResult.Error(/* ... */)
if (!file.canWrite()) { /* error */ }
```

**Effectiveness:** ✅ GOOD
- Prevents operation if file not writable
- But doesn't prevent mid-operation permission changes

---

### Mitigation #3: Hash Verification

**Code (lines 583, 598):**
```kotlin
hashDigest.update(chunk)  // During write
val computedHash = hashDigest.digest()  // After write
```

**Effectiveness:** ⏳ PARTIAL
- Verifies written data is correct
- **Does not verify file was successfully renamed** (hash computed from temp file, not final location)

---

## CANDIDATE ARCHITECTURE EVALUATION (NO CODE IMPLEMENTATION IN THIS PHASE)

### Why "Versioned Files + renameTo()" is NOT a Universal or Guaranteed Solution

The previously proposed pattern (`versionedFile.renameTo(originalFile.name)` followed by `originalFile.delete()`) has critical limitations under the Android Storage Access Framework:

1. **`renameTo()` Does Not Guarantee In-Place Rename:**
   - Under the `DocumentProvider` contract, a provider may create a **completely new document with a different URI** to represent the renamed entity.
   - Any external reference, URI permission grant, or database record pointing to the old URI becomes dangling or invalid.
2. **MIME Type and Extension Mutations:**
   - Some providers infer and mutate document MIME types dynamically when extensions change, altering how other apps handle the file.
3. **Collision Semantics on Existing Names Vary by Provider:**
   - If `originalFile` still exists, some providers return `false`, some silently overwrite, and others create a duplicate document with an indexed suffix (e.g., `document (1).pdf`).
4. **No Atomicity Guarantee from SAF:**
   - Android SAF explicitly does not guarantee that `renameTo()` is an atomic operation. On network, cloud, or virtual document providers, rename is implemented as a multi-step copy-and-delete.

Calling any DocumentFile rename "atomic" is factually incorrect and unsupported by the platform contract.

---

### Safer Candidate Architecture: Immutable Versioned Objects with Metadata Pointer

A much safer architectural direction to evaluate for future implementation:

```
┌────────────────────────────────────────────────────────────────────────┐
│                        Application Layer (Room DB)                     │
│  File Record: "report.pdf" ──► Active Object URI: content://.../v2_uuid│
│  (Database Pointer Update: ACID TRANSACTION = TRUE ATOMICITY)         │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ points to
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                        SAF Storage Layer (Disk)                        │
│  [Version 1 (Retained)]: content://.../report_v1_{uuid} (Read-only)     │
│  [Version 2 (Active)]  : content://.../report_v2_{uuid} (Verified)      │
│  [Abandoned / In-flight]: content://.../report_v3_{uuid} (Uncommitted)  │
└────────────────────────────────────────────────────────────────────────┘
```

#### Principles of the Immutable Versioned Object Model:

1. **Write New Version with Unique Identity:**
   - Never write in-place or overwrite existing objects.
   - Allocate a unique immutable document name (e.g., `{fileId}_v{timestamp}_{uuid}`).
2. **Verify Before Promotion:**
   - Verify non-null output stream.
   - Verify stream transferred byte count equals `totalSizeBytes`.
   - Verify computed SHA-256 matches `expectedHash` from caller/server.
   - Verify persisted output can be opened and probed where feasible.
3. **Transactional Pointer Promotion:**
   - Update the active version reference inside a Room database transaction.
   - **Key Architectural Distinction:** The database pointer update in SQLite/Room is **STRICTLY ATOMIC** (ACID). SAF file operations are **NOT ATOMIC**. By relying on DB transactions for promotion rather than filesystem renames, application-level atomicity is achieved.
4. **Retention and Failure Recovery:**
   - Retain the previous version intact until the new version is confirmed healthy and any remote sync synchronization is acknowledged.
   - If the write crashes or fails at any point, the database still points to the old, uncorrupted version.
5. **Garbage Collection Policy:**
   - Abandoned or uncommitted versions are purged only after a defined retention window (e.g., 24 hours), preventing orphaned file accumulation without risking active transfers.

#### Unresolved External File Browsing Trade-Off:
- If users or third-party file manager applications browse the SAF directory tree directly, they will see versioned filenames (e.g., `report_v2_...`) rather than plain conventional filenames (`report.pdf`).
- Providing conventional filenames requires provider-level file naming, which re-introduces the naming collision and non-atomic rename risks.
- This trade-off requires explicit product and architecture owner decision before implementation.

---

## FAILURE MODE MATRIX

| Scenario | Delete Result | Rename Result | Original File | Temp / New File | Data Loss? | Root Cause |
|---|---|---|---|---|---|---|
| Normal Success | ✅ True | ✅ True | Replaced | Promoted | ❌ NO | Happy path |
| Delete succeeds, Rename fails | ✅ True | ❌ False | ❌ **DELETED** | Orphaned | ✅ **YES** | Ignored Boolean return value |
| Delete fails, Rename fails | ❌ False | ❌ False | ✅ Untouched | Orphaned | ❌ NO | Ignored return, false success reported |
| Output stream returns null | N/A | N/A | ❌ **DELETED** | Empty 0-byte file promoted | ✅ **YES** | Safe-call `?.` skips write block |
| Crash between Delete & Rename | ✅ True | Crashed | ❌ **DELETED** | Orphaned | ✅ **YES** | Non-atomic sequence interrupted |
| Unchecked exception after Delete | ✅ True | Exception | ❌ **DELETED** | Deleted by catch | ✅ **YES** | Catch block only cleans temp file |
| Permission revoked mid-operation | ❌ SecurityException | Skipped | ✅ Untouched | Orphaned/Failed | ❌ NO | Safe failure |

---

## RECOMMENDATION: PATH FORWARD

### 1. Immediate (Current State — Sprint 3A)
- **Status:** Identified as CRITICAL BLOCKER.
- **Rule:** Do NOT expose `writeFileStream()` to multi-device sync, remote relay, or production workloads.
- **Documentation:** Document the defect, the failure modes, and the candidate architecture accurately.
- **Execution Rule:** NO source-code modifications in this documentation phase.

### 2. Implementation Planning (Sprint 3B / Architecture Remediation)
- Redesign the write path based on immutable versioned objects with transactional database pointers.
- Address the external file browsing trade-off with the project owner.
- Implement explicit validation:
  - Check non-null `openOutputStream()`
  - Verify `transferredBytes == totalSizeBytes`
  - Perform real expected-hash comparison (`computedHash == expectedHash`)
  - Capture and handle Boolean return values of all SAF operations
- Verify with comprehensive failure-injection tests across physical devices and emulators.

---

## CONCLUSION

The current `writeFileStream()` implementation in `SafStorageAdapter.kt` is **NOT atomic** and exhibits multiple critical failure modes that result in silent data loss or false success reporting.

**Blocker Status:** 🔴 **CRITICAL BLOCKER** — Must be resolved and verified with real test execution before Android Storage Node write operations are enabled for multi-device sync or production release.

---

**Investigation Complete:** 2026-11-08  
**Recommendation:** STOP production readiness assessment until atomic write is redesigned and tested.
