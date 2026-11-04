package com.nexsync.storagenode.domain.storage

/**
 * Represents the result of a storage operation
 */
sealed class StorageOperationResult<T> {
    data class Success<T>(val data: T) : StorageOperationResult<T>()
    data class Error<T>(val exception: StorageException) : StorageOperationResult<T>()
}

/**
 * Custom exception for storage operations
 */
sealed class StorageException(message: String, cause: Throwable? = null) : Exception(message, cause) {
    class DirectoryNotFound(message: String = "Directory not found") : StorageException(message)
    class FileNotFound(message: String = "File not found") : StorageException(message)
    class PermissionDenied(message: String = "Permission denied") : StorageException(message)
    class InsufficientSpace(message: String = "Insufficient storage space") : StorageException(message)
    class InvalidPath(message: String = "Invalid path") : StorageException(message)
    class OperationFailed(message: String = "Operation failed", cause: Throwable? = null) : StorageException(message, cause)
    class AccessUriExpired(message: String = "Storage access URI has expired") : StorageException(message)
    class NameConflict(message: String = "File or directory already exists with that name") : StorageException(message)
}

/**
 * Represents a file in storage with metadata
 */
data class StorageFileInfo(
    val uri: String,
    val name: String,
    val isDirectory: Boolean,
    val sizeBytes: Long,
    val lastModifiedMs: Long,
    val mimeType: String? = null,
    val isReadable: Boolean = true,
    val isWritable: Boolean = true
)

/**
 * Represents a storage directory with capacity info
 */
data class StorageInfo(
    val totalBytes: Long,
    val freeBytes: Long,
    val usedBytes: Long
) {
    val usagePercentage: Double
        get() = if (totalBytes > 0) (usedBytes.toDouble() / totalBytes.toDouble()) * 100 else 0.0
}

/**
 * Stream configuration for file transfers
 */
data class StreamConfig(
    val bufferSizeBytes: Int = 65536, // 64 KB default
    val maxConcurrentReads: Int = 3,
    val readTimeoutMs: Long = 30000L,
    val writeTimeoutMs: Long = 30000L
) {
    init {
        require(bufferSizeBytes in 4096..2097152) { "Buffer size must be 4KB-2MB" }
        require(maxConcurrentReads in 1..10) { "Max concurrent reads must be 1-10" }
        require(readTimeoutMs in 1000..300000) { "Read timeout must be 1-300 seconds" }
        require(writeTimeoutMs in 1000..300000) { "Write timeout must be 1-300 seconds" }
    }
}

/**
 * Transfer progress callback
 */
interface TransferProgressListener {
    suspend fun onProgress(transferredBytes: Long, totalBytes: Long)
    suspend fun onComplete()
    suspend fun onError(exception: StorageException)
}

/**
 * Hash verification result
 */
data class HashVerificationResult(
    val computedHash: String,
    val expectedHash: String,
    val isValid: Boolean = computedHash.equals(expectedHash, ignoreCase = true)
)
