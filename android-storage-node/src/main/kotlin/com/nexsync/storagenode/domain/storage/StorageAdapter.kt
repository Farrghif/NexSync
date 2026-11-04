package com.nexsync.storagenode.domain.storage

/**
 * Abstract storage adapter interface
 * Allows multiple storage backends (SAF, LocalDisk, Cloud, etc)
 */
interface StorageAdapter {
    
    /**
     * Initialize storage access
     */
    suspend fun initialize(): StorageOperationResult<Unit>
    
    /**
     * Verify directory access is still valid
     */
    suspend fun verifyAccess(): StorageOperationResult<Unit>
    
    /**
     * List files in root directory
     */
    suspend fun listRootFiles(): StorageOperationResult<List<StorageFileInfo>>
    
    /**
     * List files in a specific directory
     */
    suspend fun listDirectory(directoryUri: String): StorageOperationResult<List<StorageFileInfo>>
    
    /**
     * Get file metadata
     */
    suspend fun getFileInfo(fileUri: String): StorageOperationResult<StorageFileInfo>
    
    /**
     * Get storage capacity information
     */
    suspend fun getStorageInfo(): StorageOperationResult<StorageInfo>
    
    /**
     * Create a new file
     */
    suspend fun createFile(parentUri: String, fileName: String): StorageOperationResult<StorageFileInfo>
    
    /**
     * Create a new directory
     */
    suspend fun createDirectory(parentUri: String, dirName: String): StorageOperationResult<StorageFileInfo>
    
    /**
     * Delete file or directory
     */
    suspend fun delete(fileUri: String): StorageOperationResult<Unit>
    
    /**
     * Rename file or directory
     */
    suspend fun rename(fileUri: String, newName: String): StorageOperationResult<StorageFileInfo>
    
    /**
     * Move file to new parent directory
     */
    suspend fun move(fileUri: String, newParentUri: String): StorageOperationResult<StorageFileInfo>
    
    /**
     * Read file with streaming and progress callback
     */
    suspend fun readFileStream(
        fileUri: String,
        outputStream: suspend (ByteArray) -> Unit,
        progressListener: TransferProgressListener? = null
    ): StorageOperationResult<HashVerificationResult>
    
    /**
     * Write file with streaming and progress callback
     */
    suspend fun writeFileStream(
        fileUri: String,
        inputStream: suspend () -> ByteArray?,
        totalSizeBytes: Long,
        progressListener: TransferProgressListener? = null
    ): StorageOperationResult<HashVerificationResult>
    
    /**
     * Copy file (for offline operations)
     */
    suspend fun copy(sourceUri: String, targetParentUri: String, newName: String): StorageOperationResult<StorageFileInfo>
    
    /**
     * Get SHA-256 hash of file
     */
    suspend fun computeFileHash(fileUri: String): StorageOperationResult<String>
    
    /**
     * Check if path is valid (no traversal)
     */
    fun isValidPath(path: String): Boolean
    
    /**
     * Check if file exists
     */
    suspend fun exists(fileUri: String): Boolean
    
    /**
     * Get MIME type for file
     */
    suspend fun getMimeType(fileUri: String): StorageOperationResult<String?>
    
    /**
     * Cleanup temporary files older than specified time
     */
    suspend fun cleanupTempFiles(olderThanMs: Long): StorageOperationResult<Int>
}
