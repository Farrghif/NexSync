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

### Scenario 1: Rename Fails After Delete (MOST LIKELY)

**Execution Sequence:**

```
1. tempFile written successfully
2. Hash verified
3. file.delete() executes
   → SUCCESS: Original file deleted from filesystem
4. tempFile.renameTo() called
   → FAILS: Returns false or throws exception
     - Reason could be:
       * Permission denied (permissions changed between operations)
       * Provider error (provider becomes unavailable)
       * Filesystem full (unexpected)
       * Race condition (another process renamed file)
       * SAF provider bug or unexpected behavior
5. Exception caught in line 604 (IOException)
6. Catch block tries to delete tempUri
   → Deletes temp file (now useless)
7. Returns ERROR to caller
8. RESULT: Original file DELETED, temp file also DELETED
   → DATA LOSS ❌
```

**Who is affected?** ANY Android version (21+), ANY SAF provider (Google Drive, OneDrive, local).

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

## COMPARISON: SAFE PATTERNS FROM OTHER PLATFORMS

### Android Content Provider Pattern (Recommended)

**Replace without delete:**
```kotlin
// Instead of: delete then rename
// Use: copy then verify then delete

// Step 1: Write to temp file
val tempUri = /* ... */

// Step 2: Verify temp file completely
val tempHash = computeHash(tempUri)
if (tempHash != expected) {
    tempUri.delete()  // Clean up bad temp
    return ERROR
}

// Step 3: Copy temp to final location (atomic at provider level)
val finalUri = parentDirectory.createFile(mimeType, fileName)
tempUri.copyTo(finalUri)

// Step 4: Only after successful copy, delete original + temp
originalFile.delete()  // Can fail, original preserved
tempUri.delete()  // Clean up temp
```

**Advantage:** If copy fails, original file untouched. If delete fails, file still exists.

---

### Version-and-Promote Pattern (Best for SAF)

**Keep old version until new is verified:**
```kotlin
// Current file: document.pdf
// New version: document.pdf.new-v1234

// Step 1: Write to versioned file
val newVersionUri = createFile("document.pdf.new-v1234")
writeData(newVersionUri)

// Step 2: Verify new version
val newHash = computeHash(newVersionUri)
if (newHash != expected) {
    newVersionUri.delete()
    return ERROR
}

// Step 3: Rename new version to current (single operation)
newVersionUri.renameTo("document.pdf")
// ← If this fails, old version still at "document.pdf", new at "document.pdf.new-v1234"

// Step 4: Delete old version only after successful rename
originalFile.delete()  // Original is gone, but new version exists
```

**Advantage:** At worst, both old and new version exist. Never zero versions.

---

## PROPOSED SOLUTIONS

### Solution A: Versioned Files (RECOMMENDED FOR SAF)

**Design:** Keep old version until new version proven and promoted.

**Implementation:**
```kotlin
suspend fun writeFileStreamSafe(
    fileUri: String,
    inputStream: suspend () -> ByteArray?,
    totalSizeBytes: Long,
    expectedHash: String?,
    progressListener: TransferProgressListener?
): StorageOperationResult<HashVerificationResult> {
    val originalFile = DocumentFile.fromSingleUri(context, Uri.parse(fileUri))
    val versionedName = "${originalFile.name}.new-${System.currentTimeMillis()}"
    
    // Step 1: Write to versioned temp file
    val versionedFile = createTempFile(parentUri, versionedName) ?: return ERROR
    val computedHash = writeToFile(versionedFile, inputStream, progressListener)
    
    // Step 2: Verify hash
    if (expectedHash != null && computedHash != expectedHash) {
        versionedFile.delete()  // Clean up bad version
        return ERROR
    }
    
    // Step 3: Attempt rename (single atomic operation at SAF level)
    val renameSuccess = versionedFile.renameTo(originalFile.name)
    if (!renameSuccess) {
        versionedFile.delete()  // Clean up renamed version
        return ERROR  // Original file untouched
    }
    
    // Step 4: Clean up old version (optional, deferred if fails)
    try {
        originalFile.delete()  // Try to remove old, but don't fail operation
    } catch (e: Exception) {
        Timber.w("Could not delete old version, will clean up later")
    }
    
    return SUCCESS
}
```

**Advantages:**
- ✅ Original never deleted until rename succeeds
- ✅ Always at least one version available
- ✅ Can clean up old version asynchronously
- ✅ Data loss nearly impossible

**Disadvantages:**
- ⏳ Storage overhead (two versions temporarily)
- ⏳ Cleanup needed for orphaned old versions

---

### Solution B: Atomic SAF Operations (Provider-Specific)

**Design:** Use provider-specific atomic operations if available.

**Implementation:**
```kotlin
// For providers supporting atomic replace (Google Drive, some cloud providers):
if (provider.supportsAtomicReplace()) {
    provider.atomicReplace(fileUri, inputStream)
} else {
    // Fall back to versioned approach
    writeFileStreamSafe(fileUri, inputStream, ...)
}
```

**Advantage:** ✅ True atomicity when available

**Disadvantage:** ❌ Provider-specific, may not work for all SAF providers

---

### Solution C: Backup-Rollback Pattern

**Design:** Keep backup of original, roll back if rename fails.

**Implementation:**
```kotlin
// Step 1: Backup original
val backupUri = createBackup(originalFile)

// Step 2: Try to replace
try {
    originalFile.delete()
    tempFile.renameTo(originalFile.name)
    backupUri.delete()  // Success, clean up backup
} catch (e: Exception) {
    // Rollback: restore from backup
    backupUri.renameTo(originalFile.name)
    return ERROR
}
```

**Advantage:** ✅ Can rollback if rename fails

**Disadvantage:** ❌ Requires backup space, adds complexity, still not guaranteed atomic

---

## RECOMMENDATION: PATH FORWARD

### Immediate (Before Sprint 3B)

**Option:** Disable write operations or restrict to read-only until atomic write is safe.

**Rationale:** Current implementation is a data-loss hazard. Better to have incomplete feature than data corruption.

---

### Short Term (Sprint 3B-3C)

**Implement:** Solution A (Versioned Files Pattern)

**Rationale:**
- ✅ Works with any SAF provider
- ✅ Maximizes data safety
- ✅ No complex provider-specific logic
- ✅ Reasonable overhead for reliability

**Required Changes:**
1. Change temp file naming from `.nexsync-temp` to `.new-{timestamp}`
2. Update writeFileStream() to check rename return value
3. Add cleanup task for orphaned old versions
4. Document versioning strategy for users

**Testing:** Must test failure scenarios:
- [ ] Rename fails after delete
- [ ] Permission revoked mid-operation
- [ ] Process crashes between steps
- [ ] Large files (>50MB)
- [ ] Various SAF providers (local, Google Drive, OneDrive)

---

### Long Term (Sprint 5+)

**Research:** Provider-specific atomic operations
- Google Drive API: `files().update()` with `setNewRevision()`
- OneDrive API: `PUT /drive/items/{id}/content` is atomic
- Local SAF: Use `DocumentFile.createFile()` + `renameTo()` is closest to atomic

---

## CURRENT RISK ASSESSMENT

| Risk | Probability | Impact | Mitigation |
|------|---|---|---|
| Rename fails after delete | **HIGH** (common SAF failure mode) | **CRITICAL** (data loss) | ❌ None |
| Process crash mid-operation | **MEDIUM** (depends on device) | **CRITICAL** (data loss) | ⏳ Partial (cleanup doesn't restore) |
| Permission revoked | **MEDIUM** (user changes settings) | **MEDIUM** (operation fails, data preserved) | ✅ Acceptable |
| Temp file accumulation | **HIGH** (crash between steps) | **LOW** (storage only) | ⏳ Manual cleanup needed |

**Overall Risk Level:** 🔴 **CRITICAL** — Do not deploy for multi-device sync

---

## CONCLUSION

The current `writeFileStream()` implementation is **NOT atomic** despite claims in documentation.

**Critical Gap:** Delete-then-rename pattern with no return value checks creates a data-loss window.

**Required Fix:** Implement versioned file pattern (Solution A) before any write operations are exposed to remote sync or multi-device scenarios.

**Blocker Status:** ✅ This must be resolved before Sprint 4 (Backend Relay) or any production deployment.

---

**Investigation Complete:** 2026-11-08  
**Recommendation:** STOP production readiness assessment until atomic write is redesigned and tested.
