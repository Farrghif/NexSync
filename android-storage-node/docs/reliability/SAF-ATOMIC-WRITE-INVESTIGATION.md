# SAF Atomic Write Investigation & Reliability Assessment

**Date:** 2026-11-08  
**Status:** CRITICAL DATA LOSS RISK IDENTIFIED  
**Scope:** `SafStorageAdapter.writeFileStream()` implementation analysis  
**Severity:** 🔴 BLOCKER — Do not deploy until resolved  

---

## 1. EXECUTIVE SUMMARY

The current `writeFileStream()` implementation in `SafStorageAdapter.kt` claims to perform atomic file replacement using temporary files. Detailed source-level analysis of the committed code reveals two distinct categories of defects:

1. **Current Reachable Failure (Pre-Write Blocker):** Under the current code, `writeFileStream()` fails before writing any data. `DocumentFile.fromSingleUri()` produces a `SingleDocumentFile` whose parent is always `null`. Passing `file.parentFile?.uri.toString()` generates the literal string `"null"`, which causes `DocumentFile.fromTreeUri()` to throw `IllegalArgumentException`. The helper catches this and returns `null`, causing `writeFileStream()` to immediately abort with `OperationFailed("Failed to create temporary file")`.
2. **Latent Dangerous Finalization Defects (Post-Fix Data Loss Hazard):** If temporary file creation is made to succeed, the finalization sequence deletes the original file before promoting the temporary file via `renameTo()`. Because the Boolean return values of `delete()` and `renameTo()` are ignored, a failed rename permanently destroys the original file while falsely reporting `Success`. Furthermore, `openOutputStream()` returning `null` silently skips writing and promotes an empty 0-byte file, while hash checking is purely calculative with no comparison against an expected value.

This document classifies these failures, analyzes the limitations of naive rename patterns, and defines a robust candidate recovery architecture based on immutable versioned objects and a recoverable commit protocol.

---

## 2. CODE AUDIT FINDINGS: REACHABLE VS. LATENT DEFECTS

### 2.1 Current Reachable Failure: `createTempFile()` Failure via Null Parent URI

**Committed Source Code (`SafStorageAdapter.kt` lines 555, 571-574, 782-797):**
```kotlin
// Line 555:
val uri = Uri.parse(fileUri)
val file = DocumentFile.fromSingleUri(context, uri) ?: return@withContext ...

// Line 571:
val tempFile = createTempFile(file.parentFile?.uri.toString(), file.name ?: "temp")
if (tempFile == null) {
    return@withContext StorageOperationResult.Error(
        StorageException.OperationFailed("Failed to create temporary file")
    )
}
```

```kotlin
// Lines 782-797:
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

#### Exact Execution Trace:
1. `file` is instantiated via `DocumentFile.fromSingleUri(context, uri)`.
2. In the AndroidX DocumentFile library, `fromSingleUri()` constructs a `SingleDocumentFile` with no parent reference. Its implementation of `getParentFile()` unconditionally returns `null`.
3. In Kotlin, `file.parentFile?.uri.toString()` safely navigates: because `file.parentFile` is `null`, `(null)?.uri` is `null`, and string conversion produces `"null"`.
4. `createTempFile()` receives `parentUri = "null"`.
5. Since `"null".isEmpty()` is `false`, it branches to `DocumentFile.fromTreeUri(context, Uri.parse("null"))`.
6. `Uri.parse("null")` has no tree document ID. In AndroidX, `DocumentsContract.getTreeDocumentId()` detects this and throws `IllegalArgumentException`.
7. `createTempFile()` catches the exception in its `catch (e: Exception)` block, logs an error, and returns `null`.
8. Line 572 checks `if (tempFile == null)` and immediately returns `StorageOperationResult.Error(StorageException.OperationFailed("Failed to create temporary file"))`.
9. **Impact:** Currently reachable on every call to `writeFileStream()` with a `SingleDocumentFile` URI. No data is written, no streams are opened, and the original file is not touched.

---

### 2.2 Latent Dangerous Finalization Defects (Reachable Once Temp Creation Succeeds)

If the temporary file creation defect is resolved (e.g. by using `rootDocumentFile` or a valid tree URI), execution reaches the write and finalization blocks where severe latent defects exist:

#### Defect A: Ignored Boolean Returns from `delete()` and `renameTo()`
**Committed Source Code (`SafStorageAdapter.kt` lines 591-597):**
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
- AndroidX `DocumentFile.delete()` and `DocumentFile.renameTo()` return `Boolean`. Failures do **NOT** throw `IOException`.
- **False Success Scenario:**
  1. `file.delete()` succeeds (`true`), destroying the original file.
  2. `tempFile.renameTo()` fails (`false`) due to provider permission changes, name collision, or provider refusal.
  3. No exception is thrown.
  4. `tempUri = null` executes, dropping the reference.
  5. `progressListener?.onComplete()` is called.
  6. The method returns `StorageOperationResult.Success`.
  7. **Result: PERMANENT DATA LOSS WITH FALSE SUCCESS.** Original file is deleted, temp file remains orphaned as `.nexsync-temp`, caller is falsely notified that the write succeeded.

#### Defect B: Provider Rename Changes Document URI and MIME Type
- The Android SAF `DocumentProvider` contract allows providers to return a completely new `Uri` or create a new document representation upon `renameTo()`.
- `tempFile.renameTo(...)` does not capture the returned URI. Any caller holding the previous URI now holds a stale reference. Furthermore, providers may mutate MIME types based on filename extensions.

#### Defect C: Unchecked Exceptions After Delete Cause Total Destruction
- If an unchecked exception (such as `SecurityException` or provider `RuntimeException`) is thrown during `tempFile.renameTo()`, execution jumps to the catch block:
  ```kotlin
  tempUri?.let { uri ->
      try {
          DocumentFile.fromSingleUri(context, uri)?.delete()
      } catch (ex: Exception) { ... }
  }
  ```
- The catch block deletes the temporary file, but the original file was **already deleted** at line 592.
- **Result:** Both the original file and temporary file are destroyed.

#### Defect D: Process Crash Between Delete and Rename
- If the Android OS terminates the process (OOM killer, power cut, user force stop) between `file.delete()` and `tempFile.renameTo()`, the original file is lost, the temporary file is unpromoted, and catch-block cleanup is bypassed.

#### Defect E: Silent Skip on Null `openOutputStream()`
**Committed Source Code (`SafStorageAdapter.kt` line 578):**
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
- Per Android documentation, `ContentResolver.openOutputStream(Uri)` can return `null` (e.g. provider crash or unmount). It can also throw `FileNotFoundException`.
- Because safe-call `?.use` is used, a `null` return silently skips the entire loop. Zero bytes are written, `transferredBytes` remains `0`, and execution proceeds directly to line 592, deleting the original file and promoting an empty 0-byte file.

#### Defect F: Calculative Hash Without Comparison
**Committed Source Code (`SafStorageAdapter.kt` lines 598-603):**
```kotlin
val computedHash = hashDigest.digest().joinToString("") { "%02x".format(it) }
StorageOperationResult.Success(HashVerificationResult(
    computedHash = computedHash,
    expectedHash = "",
    isValid = true
))
```
- `expectedHash` is hardcoded to `""` and `isValid` is hardcoded to `true`.
- No comparison against a caller-provided or manifest hash is performed.
- `transferredBytes` is never verified against `totalSizeBytes`.
- Hash is computed from in-flight memory buffers, never verified against the persisted file bytes on disk.

---

## 3. WHY "VERSIONED RENAME" IS NOT UNIVERSALLY SAFE

Early proposals suggested swapping the order: rename new file to target, then delete old file (`versionedFile.renameTo(originalFile.name)` then `originalFile.delete()`).

This pattern is **NOT universally safe** under Android SAF for fundamental platform reasons:
1. **No In-Place Rename Guarantee:** SAF `DocumentProvider` does not guarantee in-place rename. Providers may create a new document with a different URI.
2. **Name Collision Behavior:** If the original file still exists, calling `renameTo()` on a file to give it the original's name produces undefined behavior across OEM providers:
   - Some providers return `false`.
   - Some providers overwrite.
   - Some providers create duplicate indexed files (`document (1).pdf`).
3. **No SAF Distributed Transactions:** The SAF DocumentProvider API operates on isolated, independent operations. No multi-operation transaction or rollback exists. Calling any DocumentFile rename sequence "atomic" is factually unsupported by Android specifications.

---

## 4. CANDIDATE RECOVERY ARCHITECTURE: IMMUTABLE VERSIONED OBJECTS & RECOVERABLE COMMIT PROTOCOL

*(Design evaluation only — strictly no code implementation in this phase)*

To achieve robust reliability without relying on non-existent filesystem atomicity, the architecture must separate **storage object writes** from **metadata pointer updates**.

### 4.1 Architectural Separation: Room Metadata vs. SAF Storage

```
┌────────────────────────────────────────────────────────────────────────┐
│                        Application Layer (Room DB)                     │
│  Active File Record: "report.pdf" ──► Pointer: content://.../v2_uuid   │
│  (Database Transaction: ACID within Room SQLite ONLY)                  │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ points to
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                   SAF DocumentProvider Layer (Storage)                 │
│  [Version 1 (Previous)]: content://.../report_v1_{uuid} (Intact)        │
│  [Version 2 (Active)]  : content://.../report_v2_{uuid} (Verified)      │
│  [In-Flight / Uncommitted]: content://.../report_v3_{uuid} (Isolated)   │
└────────────────────────────────────────────────────────────────────────┘
```

**CRITICAL ARCHITECTURAL DISTINCTION:**
A Room database transaction provides ACID guarantees for metadata stored within SQLite. **It does NOT create a distributed transaction with the Android SAF ContentProvider.** SAF and Room are independent subsystems. Application-level atomicity cannot rely on global filesystem transactions; it must be implemented via a **Recoverable Commit Protocol**.

---

### 4.2 Recoverable Commit Protocol Lifecycle

```
[PREPARING] ──► [WRITTEN] ──► [VERIFIED] ──► [ACTIVE] ──► [GC_PENDING]
     │              │              │
     ▼              ▼              ▼
(Crash/Fail)   (Crash/Fail)   (Crash/Fail)
Old version remains ACTIVE; unpromoted object purged during recovery reconciliation
```

1. **`PREPARING`:**
   - Record in Room a pending operation with unique object identity (e.g. `{fileId}_v{timestamp}_{uuid}`).
   - Existing active version reference remains unchanged in Room.
2. **`WRITTEN`:**
   - Create new immutable document via SAF.
   - Stream content. Check `openOutputStream()` explicitly for null and catch `FileNotFoundException`.
   - Ensure old version is never overwritten or touched during writing.
3. **`VERIFIED`:**
   - Check `transferredBytes == totalSizeBytes`.
   - Compare computed SHA-256 against caller `expectedHash`. If no expected hash is provided, mark status `HASH_CALCULATED`, not `HASH_VERIFIED`.
   - Verify persisted output by reading headers or bytes where feasible.
4. **`ACTIVE`:**
   - Inside a single Room DB transaction, update the active version pointer to point to the new SAF URI.
   - At this precise point, the new version becomes active for the application.
5. **`GC_PENDING`:**
   - Mark previous object version as obsolete.
   - Previous object is retained until defined retention/recovery rules allow garbage collection.

### 4.3 Startup Crash Recovery
- If the application crashes before `ACTIVE`, the database still points to the old, uncorrupted version.
- On startup, a reconciliation worker scans Room for incomplete `PREPARING` or `WRITTEN` entries and cleans up uncommitted SAF documents.

### 4.4 Unresolved External File Browsing Trade-Off
- **Trade-off:** If third-party file managers or users browse the SAF directory directly, they will see versioned filenames (`report_v2_...`) instead of conventional names (`report.pdf`).
- Providing conventional filenames requires provider-level file naming, which re-introduces the naming collision and non-atomic rename risks.
- This requires an explicit product/owner decision before any implementation begins.

---

## 5. FAILURE MODE CLASSIFICATION MATRIX

| Failure Scenario | Classification | Trigger / Condition | Current Outcome | Required Safe Behavior |
|---|---|---|---|---|
| Null Parent URI | **Current Reachable Failure** | `SingleDocumentFile.getParentFile()` returns null | Write fails at line 572 before opening streams | Use valid tree URI or default root |
| Delete succeeds, Rename fails | **Latent Finalization Defect** | Provider rejects rename after original deleted | **Permanent Data Loss**; false Success returned | Immutable versioning; old version preserved |
| Delete fails, Rename fails | **Latent Finalization Defect** | Provider denies delete; rename collides | False Success returned; temp orphaned | Check return values; fail fast without data loss |
| Output stream returns null | **Latent Finalization Defect** | Provider crash / unmounted storage | Safe-call skips write; empty 0-byte file promoted | Explicit null check; abort immediately |
| Crash between Delete & Rename | **Latent Finalization Defect** | Process killed between line 592 & 593 | **Permanent Data Loss**; original gone | Recoverable commit protocol |
| Unchecked exception on Rename | **Latent Finalization Defect** | RuntimeException thrown during rename | Original deleted; temp deleted by catch | Preserve original before new is active |
| Hash mismatch / incomplete stream | **Latent Finalization Defect** | Stream severed or corrupted in transit | False `isValid = true` returned | Compare `computedHash == expectedHash` |

---

## 6. VERDICT & RECOMMENDATIONS

- **Technical Status:** 🔴 **CRITICAL BLOCKER**
- **Sprint 2.1 Acceptance:** Cannot be accepted with non-atomic write logic and unverified failure handling.
- **Sprint 3B Progression:** Blocked. Storage reliability must be resolved before embedded server exposure.
- **Production Readiness:** **NOT READY**.
- **Next Step:** Maintain write path disabled for remote sync until the Immutable Versioned Object protocol is approved, implemented, and verified via instrumented device tests.
