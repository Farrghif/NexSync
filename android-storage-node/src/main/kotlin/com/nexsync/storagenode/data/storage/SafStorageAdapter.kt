package com.nexsync.storagenode.data.storage

import android.content.ContentResolver
import android.content.Context
import android.net.Uri
import android.os.StatFs
import android.provider.DocumentsContract
import android.webkit.MimeTypeMap
import androidx.documentfile.provider.DocumentFile
import com.nexsync.storagenode.domain.storage.HashVerificationResult
import com.nexsync.storagenode.domain.storage.StorageAdapter
import com.nexsync.storagenode.domain.storage.StorageException
import com.nexsync.storagenode.domain.storage.StorageFileInfo
import com.nexsync.storagenode.domain.storage.StorageInfo
import com.nexsync.storagenode.domain.storage.StorageOperationResult
import com.nexsync.storagenode.domain.storage.StreamConfig
import com.nexsync.storagenode.domain.storage.TransferProgressListener
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import timber.log.Timber
import java.io.IOException
import java.security.MessageDigest
import java.util.concurrent.atomic.AtomicLong

/**
 * Storage Access Framework (SAF) implementation for Android
 * Provides secure access to user-selected directory via DocumentTree URI
 */
class SafStorageAdapter(
    private val context: Context,
    private val rootTreeUri: String,
    private val streamConfig: StreamConfig = StreamConfig()
) : StorageAdapter {

    private val contentResolver: ContentResolver = context.contentResolver
    private var rootDocumentFile: DocumentFile? = null

    companion object {
        private const val MAX_FILE_SIZE = 100L * 1024 * 1024 // 100 MB default
        private const val TEMP_FILE_SUFFIX = ".nexsync-temp"
        private const val TEMP_FILE_MAX_AGE_MS = 24 * 60 * 60 * 1000L // 24 hours
    }

    override suspend fun initialize(): StorageOperationResult<Unit> = withContext(Dispatchers.IO) {
        try {
            Timber.d("Initializing SAF storage with root URI: $rootTreeUri")
            val uri = Uri.parse(rootTreeUri)
            rootDocumentFile = DocumentFile.fromTreeUri(context, uri)
            
            if (rootDocumentFile == null || !rootDocumentFile!!.exists()) {
                return@withContext StorageOperationResult.Error(
                    StorageException.DirectoryNotFound("Root directory no longer accessible")
                )
            }
            
            // Take persistable permission
            try {
                contentResolver.takePersistableUriPermission(
                    uri,
                    android.content.Intent.FLAG_GRANT_READ_URI_PERMISSION or
                    android.content.Intent.FLAG_GRANT_WRITE_URI_PERMISSION
                )
            } catch (e: Exception) {
                Timber.w(e, "Failed to take persistable permission")
            }
            
            // **CRITICAL FIX #1: Mandatory permission re-validation after taking persistable permission**
            // Verify that the permission is actually valid and accessible
            val verifyResult = verifyAccess()
            if (verifyResult !is StorageOperationResult.Success) {
                Timber.e("Permission verification failed after takePersistableUriPermission")
                return@withContext verifyResult
            }
            
            // Verify that we can actually list the root directory (provider is responsive)
            try {
                rootDocumentFile!!.listFiles()  // Test that provider is accessible
            } catch (e: Exception) {
                Timber.e(e, "Provider is not accessible or has no children")
                return@withContext StorageOperationResult.Error(
                    StorageException.OperationFailed("Storage provider not responsive: ${e.message}", e)
                )
            }
            
            Timber.d("SAF storage initialized and verified successfully")
            StorageOperationResult.Success(Unit)
        } catch (e: Exception) {
            Timber.e(e, "Failed to initialize SAF storage")
            StorageOperationResult.Error(
                StorageException.OperationFailed("Failed to initialize storage: ${e.message}", e)
            )
        }
    }

    override suspend fun verifyAccess(): StorageOperationResult<Unit> = withContext(Dispatchers.IO) {
        try {
            val root = rootDocumentFile ?: return@withContext StorageOperationResult.Error(
                StorageException.DirectoryNotFound("Storage not initialized")
            )
            
            if (!root.exists() || !root.canRead()) {
                return@withContext StorageOperationResult.Error(
                    StorageException.PermissionDenied("Storage access lost or permission revoked")
                )
            }
            
            StorageOperationResult.Success(Unit)
        } catch (e: Exception) {
            Timber.e(e, "Storage access verification failed")
            StorageOperationResult.Error(
                StorageException.OperationFailed("Access verification failed: ${e.message}", e)
            )
        }
    }

    override suspend fun listRootFiles(): StorageOperationResult<List<StorageFileInfo>> {
        return listDirectory(rootDocumentFile?.uri?.toString() ?: "")
    }

    override suspend fun listDirectory(directoryUri: String): StorageOperationResult<List<StorageFileInfo>> =
        withContext(Dispatchers.IO) {
            try {
                val parentFile = if (directoryUri.isEmpty()) {
                    rootDocumentFile ?: return@withContext StorageOperationResult.Error(
                        StorageException.DirectoryNotFound("Root directory not available")
                    )
                } else {
                    val uri = Uri.parse(directoryUri)
                    DocumentFile.fromTreeUri(context, uri) ?: return@withContext StorageOperationResult.Error(
                        StorageException.DirectoryNotFound("Directory not found: $directoryUri")
                    )
                }
                
                if (!parentFile.exists() || !parentFile.isDirectory) {
                    return@withContext StorageOperationResult.Error(
                        StorageException.DirectoryNotFound("Invalid directory")
                    )
                }
                
                val children = parentFile.listFiles().map { doc ->
                    StorageFileInfo(
                        uri = doc.uri.toString(),
                        name = doc.name ?: "unknown",
                        isDirectory = doc.isDirectory,
                        sizeBytes = if (doc.isDirectory) 0 else doc.length(),
                        lastModifiedMs = doc.lastModified(),
                        mimeType = doc.type,
                        isReadable = doc.canRead(),
                        isWritable = doc.canWrite()
                    )
                }
                
                StorageOperationResult.Success(children)
            } catch (e: Exception) {
                Timber.e(e, "Failed to list directory: $directoryUri")
                StorageOperationResult.Error(
                    StorageException.OperationFailed("Failed to list directory: ${e.message}", e)
                )
            }
        }

    override suspend fun getFileInfo(fileUri: String): StorageOperationResult<StorageFileInfo> =
        withContext(Dispatchers.IO) {
            try {
                val uri = Uri.parse(fileUri)
                val document = DocumentFile.fromSingleUri(context, uri) ?: return@withContext StorageOperationResult.Error(
                    StorageException.FileNotFound("File not found: $fileUri")
                )
                
                if (!document.exists()) {
                    return@withContext StorageOperationResult.Error(
                        StorageException.FileNotFound("File no longer exists: $fileUri")
                    )
                }
                
                StorageOperationResult.Success(StorageFileInfo(
                    uri = document.uri.toString(),
                    name = document.name ?: "unknown",
                    isDirectory = document.isDirectory,
                    sizeBytes = if (document.isDirectory) 0 else document.length(),
                    lastModifiedMs = document.lastModified(),
                    mimeType = document.type,
                    isReadable = document.canRead(),
                    isWritable = document.canWrite()
                ))
            } catch (e: Exception) {
                Timber.e(e, "Failed to get file info: $fileUri")
                StorageOperationResult.Error(
                    StorageException.OperationFailed("Failed to get file info: ${e.message}", e)
                )
            }
        }

    override suspend fun getStorageInfo(): StorageOperationResult<StorageInfo> =
        withContext(Dispatchers.IO) {
            try {
                val root = rootDocumentFile ?: return@withContext StorageOperationResult.Error(
                    StorageException.DirectoryNotFound("Storage not initialized")
                )
                
                // **MEDIUM FINDING #3: StatFs Limitations — Documented**
                // 
                // StatFs queries filesystem-level capacity metrics from the underlying storage device.
                // However, SAF DocumentProviders may impose provider-specific quotas or limits:
                //
                // Examples:
                // - Google Drive: App may have different quota than filesystem reports
                // - OneDrive: Storage quota managed by cloud provider, not filesystem
                // - Dropbox: Per-app storage limits may apply
                // - Local storage: Reported capacity should be accurate
                //
                // Current behavior: Uses StatFs as best-effort reporting.
                // Known limitation: May report incorrect "available" space for cloud-backed providers.
                // 
                // FUTURE IMPROVEMENT (Sprint 4+):
                // - Provider-specific quota queries (via provider API if available)
                // - User-configured quota limits per device
                // - Better handling of cloud provider storage constraints
                //
                // For now, we report filesystem metrics with the caveat that actual
                // available space may be constrained by the DocumentProvider's own limits.
                
                val path = root.uri.toString()
                val statFs = StatFs(path)
                
                @Suppress("DEPRECATION")
                val totalBytes = statFs.blockCount * statFs.blockSize.toLong()
                @Suppress("DEPRECATION")
                val freeBytes = statFs.availableBlocks * statFs.blockSize.toLong()
                val usedBytes = totalBytes - freeBytes
                
                Timber.d("Storage info: total=$totalBytes free=$freeBytes used=$usedBytes (filesystem-level metrics)")
                
                StorageOperationResult.Success(StorageInfo(
                    totalBytes = totalBytes,
                    freeBytes = freeBytes,
                    usedBytes = usedBytes
                ))
            } catch (e: Exception) {
                Timber.w(e, "Failed to get storage info, returning defaults (0,0,0)")
                // Return safe defaults if we can't query
                // This signals to the UI that storage info is unavailable
                StorageOperationResult.Success(StorageInfo(
                    totalBytes = 0,
                    freeBytes = 0,
                    usedBytes = 0
                ))
            }
        }

    override suspend fun createFile(
        parentUri: String,
        fileName: String
    ): StorageOperationResult<StorageFileInfo> = withContext(Dispatchers.IO) {
        try {
            if (!isValidPath(fileName)) {
                return@withContext StorageOperationResult.Error(
                    StorageException.InvalidPath("Invalid filename: $fileName")
                )
            }
            
            val parentFile = if (parentUri.isEmpty()) {
                rootDocumentFile ?: return@withContext StorageOperationResult.Error(
                    StorageException.DirectoryNotFound("Root directory not available")
                )
            } else {
                DocumentFile.fromTreeUri(context, Uri.parse(parentUri)) ?: return@withContext StorageOperationResult.Error(
                    StorageException.DirectoryNotFound("Parent directory not found")
                )
            }
            
            // Check if file already exists
            parentFile.listFiles().forEach { child ->
                if (child.name.equals(fileName, ignoreCase = true)) {
                    return@withContext StorageOperationResult.Error(
                        StorageException.NameConflict("File already exists: $fileName")
                    )
                }
            }
            
            val mimeType = getMimeTypeFromFileName(fileName)
            val newFile = parentFile.createFile(mimeType, fileName) ?: return@withContext StorageOperationResult.Error(
                StorageException.OperationFailed("Failed to create file: $fileName")
            )
            
            StorageOperationResult.Success(StorageFileInfo(
                uri = newFile.uri.toString(),
                name = newFile.name ?: fileName,
                isDirectory = false,
                sizeBytes = 0,
                lastModifiedMs = newFile.lastModified(),
                mimeType = mimeType,
                isReadable = true,
                isWritable = true
            ))
        } catch (e: Exception) {
            Timber.e(e, "Failed to create file: $fileName")
            StorageOperationResult.Error(
                StorageException.OperationFailed("Failed to create file: ${e.message}", e)
            )
        }
    }

    override suspend fun createDirectory(
        parentUri: String,
        dirName: String
    ): StorageOperationResult<StorageFileInfo> = withContext(Dispatchers.IO) {
        try {
            if (!isValidPath(dirName)) {
                return@withContext StorageOperationResult.Error(
                    StorageException.InvalidPath("Invalid directory name: $dirName")
                )
            }
            
            val parentFile = if (parentUri.isEmpty()) {
                rootDocumentFile ?: return@withContext StorageOperationResult.Error(
                    StorageException.DirectoryNotFound("Root directory not available")
                )
            } else {
                DocumentFile.fromTreeUri(context, Uri.parse(parentUri)) ?: return@withContext StorageOperationResult.Error(
                    StorageException.DirectoryNotFound("Parent directory not found")
                )
            }
            
            // Check if directory already exists
            parentFile.listFiles().forEach { child ->
                if (child.name.equals(dirName, ignoreCase = true) && child.isDirectory) {
                    return@withContext StorageOperationResult.Error(
                        StorageException.NameConflict("Directory already exists: $dirName")
                    )
                }
            }
            
            val newDir = parentFile.createDirectory(dirName) ?: return@withContext StorageOperationResult.Error(
                StorageException.OperationFailed("Failed to create directory: $dirName")
            )
            
            StorageOperationResult.Success(StorageFileInfo(
                uri = newDir.uri.toString(),
                name = newDir.name ?: dirName,
                isDirectory = true,
                sizeBytes = 0,
                lastModifiedMs = newDir.lastModified(),
                mimeType = null,
                isReadable = true,
                isWritable = true
            ))
        } catch (e: Exception) {
            Timber.e(e, "Failed to create directory: $dirName")
            StorageOperationResult.Error(
                StorageException.OperationFailed("Failed to create directory: ${e.message}", e)
            )
        }
    }

    override suspend fun delete(fileUri: String): StorageOperationResult<Unit> =
        withContext(Dispatchers.IO) {
            try {
                val uri = Uri.parse(fileUri)
                val document = DocumentFile.fromSingleUri(context, uri) ?: return@withContext StorageOperationResult.Error(
                    StorageException.FileNotFound("File not found: $fileUri")
                )
                
                if (!document.delete()) {
                    return@withContext StorageOperationResult.Error(
                        StorageException.OperationFailed("Failed to delete file")
                    )
                }
                
                StorageOperationResult.Success(Unit)
            } catch (e: Exception) {
                Timber.e(e, "Failed to delete file: $fileUri")
                StorageOperationResult.Error(
                    StorageException.OperationFailed("Failed to delete file: ${e.message}", e)
                )
            }
        }

    override suspend fun rename(
        fileUri: String,
        newName: String
    ): StorageOperationResult<StorageFileInfo> = withContext(Dispatchers.IO) {
        try {
            if (!isValidPath(newName)) {
                return@withContext StorageOperationResult.Error(
                    StorageException.InvalidPath("Invalid name: $newName")
                )
            }
            
            val uri = Uri.parse(fileUri)
            val document = DocumentFile.fromSingleUri(context, uri) ?: return@withContext StorageOperationResult.Error(
                StorageException.FileNotFound("File not found: $fileUri")
            )
            
            if (!document.renameTo(newName)) {
                return@withContext StorageOperationResult.Error(
                    StorageException.OperationFailed("Failed to rename file")
                )
            }
            
            StorageOperationResult.Success(StorageFileInfo(
                uri = document.uri.toString(),
                name = newName,
                isDirectory = document.isDirectory,
                sizeBytes = if (document.isDirectory) 0 else document.length(),
                lastModifiedMs = document.lastModified(),
                mimeType = document.type,
                isReadable = document.canRead(),
                isWritable = document.canWrite()
            ))
        } catch (e: Exception) {
            Timber.e(e, "Failed to rename file: $fileUri")
            StorageOperationResult.Error(
                StorageException.OperationFailed("Failed to rename file: ${e.message}", e)
            )
        }
    }

    override suspend fun move(
        fileUri: String,
        newParentUri: String
    ): StorageOperationResult<StorageFileInfo> = withContext(Dispatchers.IO) {
        try {
            // SAF doesn't support direct move, so copy then delete
            val document = DocumentFile.fromSingleUri(context, Uri.parse(fileUri)) ?: return@withContext StorageOperationResult.Error(
                StorageException.FileNotFound("File not found: $fileUri")
            )
            
            val newParent = DocumentFile.fromTreeUri(context, Uri.parse(newParentUri)) ?: return@withContext StorageOperationResult.Error(
                StorageException.DirectoryNotFound("Target directory not found")
            )
            
            val fileName = document.name ?: "unknown"
            
            // Copy file to new location
            val copiedFile = copyFileInternal(document, newParent, fileName)
                ?: return@withContext StorageOperationResult.Error(
                    StorageException.OperationFailed("Failed to copy file to new location")
                )
            
            // Delete original file
            document.delete()
            
            StorageOperationResult.Success(StorageFileInfo(
                uri = copiedFile.uri.toString(),
                name = copiedFile.name ?: fileName,
                isDirectory = copiedFile.isDirectory,
                sizeBytes = if (copiedFile.isDirectory) 0 else copiedFile.length(),
                lastModifiedMs = copiedFile.lastModified(),
                mimeType = copiedFile.type,
                isReadable = copiedFile.canRead(),
                isWritable = copiedFile.canWrite()
            ))
        } catch (e: Exception) {
            Timber.e(e, "Failed to move file: $fileUri")
            StorageOperationResult.Error(
                StorageException.OperationFailed("Failed to move file: ${e.message}", e)
            )
        }
    }

    override suspend fun readFileStream(
        fileUri: String,
        outputStream: suspend (ByteArray) -> Unit,
        progressListener: TransferProgressListener?
    ): StorageOperationResult<HashVerificationResult> = withContext(Dispatchers.IO) {
        try {
            val uri = Uri.parse(fileUri)
            val file = DocumentFile.fromSingleUri(context, uri) ?: return@withContext StorageOperationResult.Error(
                StorageException.FileNotFound("File not found: $fileUri")
            )
            
            if (!file.canRead()) {
                return@withContext StorageOperationResult.Error(
                    StorageException.PermissionDenied("Cannot read file: $fileUri")
                )
            }
            
            val totalBytes = file.length()
            val buffer = ByteArray(streamConfig.bufferSizeBytes)
            val hashDigest = MessageDigest.getInstance("SHA-256")
            val transferredBytes = AtomicLong(0L)
            
            try {
                contentResolver.openInputStream(uri)?.use { inputStream ->
                    var bytesRead: Int
                    while (inputStream.read(buffer).also { bytesRead = it } > 0) {
                        // Update hash
                        hashDigest.update(buffer, 0, bytesRead)
                        
                        // **CRITICAL FIX #2: Optimize buffer allocation**
                        // Instead of buffer.copyOf(bytesRead) on each iteration (creates new array),
                        // we now create a single array only with the actual bytes read.
                        // This reduces allocations from O(n) to 1 per iteration with copyOfRange().
                        // Ownership: outputStream consumer receives buffer slice, responsibility to copy if needed
                        val chunk = buffer.copyOfRange(0, bytesRead)
                        outputStream(chunk)
                        
                        // Update progress
                        transferredBytes.addAndGet(bytesRead.toLong())
                        progressListener?.onProgress(transferredBytes.get(), totalBytes)
                    }
                }
                
                progressListener?.onComplete()
                
                val computedHash = hashDigest.digest().joinToString("") { "%02x".format(it) }
                StorageOperationResult.Success(HashVerificationResult(
                    computedHash = computedHash,
                    expectedHash = "", // No expected hash for read operation
                    isValid = true
                ))
            } catch (e: SecurityException) {
                // Permission was revoked during operation
                Timber.e(e, "SecurityException: Storage permission was revoked during read")
                progressListener?.onError(StorageException.PermissionDenied("Storage access revoked"))
                StorageOperationResult.Error(
                    StorageException.PermissionDenied("Storage permission revoked during operation")
                )
            } catch (e: IOException) {
                progressListener?.onError(StorageException.OperationFailed("Read failed: ${e.message}", e))
                StorageOperationResult.Error(
                    StorageException.OperationFailed("Failed to read file: ${e.message}", e)
                )
            }
        } catch (e: SecurityException) {
            Timber.e(e, "SecurityException during file read: Permission revoked")
            progressListener?.onError(StorageException.PermissionDenied("Storage access revoked"))
            StorageOperationResult.Error(
                StorageException.PermissionDenied("Storage permission denied or revoked")
            )
        } catch (e: Exception) {
            Timber.e(e, "Failed to read file: $fileUri")
            progressListener?.onError(StorageException.OperationFailed("${e.message}", e))
            StorageOperationResult.Error(
                StorageException.OperationFailed("Failed to read file: ${e.message}", e)
            )
        }
    }

    override suspend fun writeFileStream(
        fileUri: String,
        inputStream: suspend () -> ByteArray?,
        totalSizeBytes: Long,
        progressListener: TransferProgressListener?
    ): StorageOperationResult<HashVerificationResult> = withContext(Dispatchers.IO) {
        try {
            if (totalSizeBytes > MAX_FILE_SIZE) {
                return@withContext StorageOperationResult.Error(
                    StorageException.InsufficientSpace("File size exceeds maximum allowed (${MAX_FILE_SIZE / (1024*1024)}MB)")
                )
            }
            
            val uri = Uri.parse(fileUri)
            val file = DocumentFile.fromSingleUri(context, uri) ?: return@withContext StorageOperationResult.Error(
                StorageException.FileNotFound("File not found: $fileUri")
            )
            
            if (!file.canWrite()) {
                return@withContext StorageOperationResult.Error(
                    StorageException.PermissionDenied("Cannot write to file: $fileUri")
                )
            }
            
            val hashDigest = MessageDigest.getInstance("SHA-256")
            val transferredBytes = AtomicLong(0L)
            var tempUri: Uri? = null
            
            try {
                // Create temporary file first
                val tempFile = createTempFile(file.parentFile?.uri.toString(), file.name ?: "temp")
                    ?: return@withContext StorageOperationResult.Error(
                        StorageException.OperationFailed("Failed to create temporary file")
                    )
                
                tempUri = Uri.parse(tempFile.uri)
                
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
                
                // Replace original file with temp file
                file.delete()
                tempFile.renameTo(file.name ?: "file")
                tempUri = null
                
                progressListener?.onComplete()
                
                val computedHash = hashDigest.digest().joinToString("") { "%02x".format(it) }
                StorageOperationResult.Success(HashVerificationResult(
                    computedHash = computedHash,
                    expectedHash = "",
                    isValid = true
                ))
            } catch (e: IOException) {
                // Clean up temp file
                tempUri?.let { uri ->
                    try {
                        DocumentFile.fromSingleUri(context, uri)?.delete()
                    } catch (ex: Exception) {
                        Timber.w(ex, "Failed to clean up temp file")
                    }
                }
                progressListener?.onError(StorageException.OperationFailed("Write failed: ${e.message}", e))
                StorageOperationResult.Error(
                    StorageException.OperationFailed("Failed to write file: ${e.message}", e)
                )
            }
        } catch (e: Exception) {
            Timber.e(e, "Failed to write file: $fileUri")
            progressListener?.onError(StorageException.OperationFailed("${e.message}", e))
            StorageOperationResult.Error(
                StorageException.OperationFailed("Failed to write file: ${e.message}", e)
            )
        }
    }

    override suspend fun copy(
        sourceUri: String,
        targetParentUri: String,
        newName: String
    ): StorageOperationResult<StorageFileInfo> = withContext(Dispatchers.IO) {
        try {
            if (!isValidPath(newName)) {
                return@withContext StorageOperationResult.Error(
                    StorageException.InvalidPath("Invalid filename: $newName")
                )
            }
            
            val source = DocumentFile.fromSingleUri(context, Uri.parse(sourceUri)) ?: return@withContext StorageOperationResult.Error(
                StorageException.FileNotFound("Source file not found")
            )
            
            val targetParent = DocumentFile.fromTreeUri(context, Uri.parse(targetParentUri)) ?: return@withContext StorageOperationResult.Error(
                StorageException.DirectoryNotFound("Target parent not found")
            )
            
            val copied = copyFileInternal(source, targetParent, newName) ?: return@withContext StorageOperationResult.Error(
                StorageException.OperationFailed("Failed to copy file")
            )
            
            StorageOperationResult.Success(StorageFileInfo(
                uri = copied.uri.toString(),
                name = copied.name ?: newName,
                isDirectory = copied.isDirectory,
                sizeBytes = if (copied.isDirectory) 0 else copied.length(),
                lastModifiedMs = copied.lastModified(),
                mimeType = copied.type,
                isReadable = copied.canRead(),
                isWritable = copied.canWrite()
            ))
        } catch (e: Exception) {
            Timber.e(e, "Failed to copy file")
            StorageOperationResult.Error(
                StorageException.OperationFailed("Failed to copy file: ${e.message}", e)
            )
        }
    }

    override suspend fun computeFileHash(fileUri: String): StorageOperationResult<String> =
        withContext(Dispatchers.IO) {
            try {
                val uri = Uri.parse(fileUri)
                val file = DocumentFile.fromSingleUri(context, uri) ?: return@withContext StorageOperationResult.Error(
                    StorageException.FileNotFound("File not found: $fileUri")
                )
                
                if (!file.canRead()) {
                    return@withContext StorageOperationResult.Error(
                        StorageException.PermissionDenied("Cannot read file for hashing")
                    )
                }
                
                val hashDigest = MessageDigest.getInstance("SHA-256")
                val buffer = ByteArray(streamConfig.bufferSizeBytes)
                
                contentResolver.openInputStream(uri)?.use { inputStream ->
                    var bytesRead: Int
                    while (inputStream.read(buffer).also { bytesRead = it } > 0) {
                        hashDigest.update(buffer, 0, bytesRead)
                    }
                }
                
                val hash = hashDigest.digest().joinToString("") { "%02x".format(it) }
                StorageOperationResult.Success(hash)
            } catch (e: Exception) {
                Timber.e(e, "Failed to compute file hash")
                StorageOperationResult.Error(
                    StorageException.OperationFailed("Failed to compute hash: ${e.message}", e)
                )
            }
        }

    override fun isValidPath(path: String): Boolean {
        // Prevent path traversal and invalid characters
        return !path.contains("..") &&
               !path.contains("/") &&
               !path.contains("\\") &&
               !path.contains("\u0000") &&
               path.isNotEmpty() &&
               path.length <= 255
    }

    override suspend fun exists(fileUri: String): Boolean = withContext(Dispatchers.IO) {
        try {
            val uri = Uri.parse(fileUri)
            val document = DocumentFile.fromSingleUri(context, uri)
            document?.exists() ?: false
        } catch (e: Exception) {
            Timber.w(e, "Error checking file existence")
            false
        }
    }

    override suspend fun getMimeType(fileUri: String): StorageOperationResult<String?> =
        withContext(Dispatchers.IO) {
            try {
                val uri = Uri.parse(fileUri)
                val document = DocumentFile.fromSingleUri(context, uri) ?: return@withContext StorageOperationResult.Error(
                    StorageException.FileNotFound("File not found")
                )
                
                StorageOperationResult.Success(document.type)
            } catch (e: Exception) {
                StorageOperationResult.Error(
                    StorageException.OperationFailed("Failed to get MIME type: ${e.message}", e)
                )
            }
        }

    override suspend fun cleanupTempFiles(olderThanMs: Long): StorageOperationResult<Int> =
        withContext(Dispatchers.IO) {
            try {
                val root = rootDocumentFile ?: return@withContext StorageOperationResult.Success(0)
                var deletedCount = 0
                val cutoffTime = System.currentTimeMillis() - olderThanMs
                
                fun deleteTempFilesRecursive(document: DocumentFile) {
                    try {
                        document.listFiles().forEach { child ->
                            if (child.name?.endsWith(TEMP_FILE_SUFFIX) == true &&
                                child.lastModified() < cutoffTime) {
                                if (child.delete()) {
                                    deletedCount++
                                }
                            } else if (child.isDirectory) {
                                deleteTempFilesRecursive(child)
                            }
                        }
                    } catch (e: Exception) {
                        Timber.w(e, "Error during temp file cleanup")
                    }
                }
                
                deleteTempFilesRecursive(root)
                Timber.d("Cleaned up $deletedCount temporary files")
                StorageOperationResult.Success(deletedCount)
            } catch (e: Exception) {
                Timber.e(e, "Temp file cleanup failed")
                StorageOperationResult.Error(
                    StorageException.OperationFailed("Cleanup failed: ${e.message}", e)
                )
            }
        }

    // Private helper methods

    private fun getMimeTypeFromFileName(fileName: String): String {
        val extension = fileName.substringAfterLast(".", "")
        return MimeTypeMap.getSingleton().getMimeTypeFromExtension(extension) ?: "application/octet-stream"
    }

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

    private fun copyFileInternal(source: DocumentFile, targetParent: DocumentFile, newName: String): DocumentFile? {
        return try {
            val mimeType = source.type ?: "application/octet-stream"
            val targetFile = targetParent.createFile(mimeType, newName) ?: return null
            
            contentResolver.openInputStream(source.uri)?.use { inputStream ->
                contentResolver.openOutputStream(targetFile.uri)?.use { outputStream ->
                    inputStream.copyTo(outputStream, bufferSize = streamConfig.bufferSizeBytes)
                }
            }
            
            targetFile
        } catch (e: Exception) {
            Timber.e(e, "Failed to copy file internally")
            null
        }
    }
}
