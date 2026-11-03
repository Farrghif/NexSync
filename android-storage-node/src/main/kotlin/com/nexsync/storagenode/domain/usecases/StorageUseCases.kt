package com.nexsync.storagenode.domain.usecases

import com.nexsync.storagenode.domain.storage.StorageAdapter
import com.nexsync.storagenode.domain.storage.StorageException
import com.nexsync.storagenode.domain.storage.StorageFileInfo
import com.nexsync.storagenode.domain.storage.StorageOperationResult
import timber.log.Timber
import javax.inject.Inject

/**
 * Use case: List files in storage directory
 */
class ListFilesUseCase @Inject constructor(
    private val storageAdapter: StorageAdapter
) {
    suspend operator fun invoke(directoryUri: String = ""): StorageOperationResult<List<StorageFileInfo>> {
        return try {
            if (directoryUri.isEmpty()) {
                storageAdapter.listRootFiles()
            } else {
                storageAdapter.listDirectory(directoryUri)
            }
        } catch (e: Exception) {
            Timber.e(e, "Error listing files in directory: $directoryUri")
            StorageOperationResult.Error(
                StorageException.OperationFailed("Failed to list files: ${e.message}", e)
            )
        }
    }
}

/**
 * Use case: Create new file
 */
class CreateFileUseCase @Inject constructor(
    private val storageAdapter: StorageAdapter
) {
    suspend operator fun invoke(
        fileName: String,
        parentUri: String = ""
    ): StorageOperationResult<StorageFileInfo> {
        return try {
            if (!storageAdapter.isValidPath(fileName)) {
                return StorageOperationResult.Error(
                    StorageException.InvalidPath("Invalid filename: $fileName")
                )
            }
            storageAdapter.createFile(parentUri, fileName)
        } catch (e: Exception) {
            Timber.e(e, "Error creating file: $fileName")
            StorageOperationResult.Error(
                StorageException.OperationFailed("Failed to create file: ${e.message}", e)
            )
        }
    }
}

/**
 * Use case: Create new directory
 */
class CreateDirectoryUseCase @Inject constructor(
    private val storageAdapter: StorageAdapter
) {
    suspend operator fun invoke(
        dirName: String,
        parentUri: String = ""
    ): StorageOperationResult<StorageFileInfo> {
        return try {
            if (!storageAdapter.isValidPath(dirName)) {
                return StorageOperationResult.Error(
                    StorageException.InvalidPath("Invalid directory name: $dirName")
                )
            }
            storageAdapter.createDirectory(parentUri, dirName)
        } catch (e: Exception) {
            Timber.e(e, "Error creating directory: $dirName")
            StorageOperationResult.Error(
                StorageException.OperationFailed("Failed to create directory: ${e.message}", e)
            )
        }
    }
}

/**
 * Use case: Delete file or directory
 */
class DeleteFileUseCase @Inject constructor(
    private val storageAdapter: StorageAdapter
) {
    suspend operator fun invoke(fileUri: String): StorageOperationResult<Unit> {
        return try {
            storageAdapter.delete(fileUri)
        } catch (e: Exception) {
            Timber.e(e, "Error deleting file: $fileUri")
            StorageOperationResult.Error(
                StorageException.OperationFailed("Failed to delete file: ${e.message}", e)
            )
        }
    }
}

/**
 * Use case: Rename file or directory
 */
class RenameFileUseCase @Inject constructor(
    private val storageAdapter: StorageAdapter
) {
    suspend operator fun invoke(
        fileUri: String,
        newName: String
    ): StorageOperationResult<StorageFileInfo> {
        return try {
            if (!storageAdapter.isValidPath(newName)) {
                return StorageOperationResult.Error(
                    StorageException.InvalidPath("Invalid filename: $newName")
                )
            }
            storageAdapter.rename(fileUri, newName)
        } catch (e: Exception) {
            Timber.e(e, "Error renaming file: $fileUri")
            StorageOperationResult.Error(
                StorageException.OperationFailed("Failed to rename file: ${e.message}", e)
            )
        }
    }
}

/**
 * Use case: Move file to new directory
 */
class MoveFileUseCase @Inject constructor(
    private val storageAdapter: StorageAdapter
) {
    suspend operator fun invoke(
        fileUri: String,
        newParentUri: String
    ): StorageOperationResult<StorageFileInfo> {
        return try {
            storageAdapter.move(fileUri, newParentUri)
        } catch (e: Exception) {
            Timber.e(e, "Error moving file: $fileUri")
            StorageOperationResult.Error(
                StorageException.OperationFailed("Failed to move file: ${e.message}", e)
            )
        }
    }
}

/**
 * Use case: Compute file hash for integrity verification
 */
class ComputeFileHashUseCase @Inject constructor(
    private val storageAdapter: StorageAdapter
) {
    suspend operator fun invoke(fileUri: String): StorageOperationResult<String> {
        return try {
            storageAdapter.computeFileHash(fileUri)
        } catch (e: Exception) {
            Timber.e(e, "Error computing file hash: $fileUri")
            StorageOperationResult.Error(
                StorageException.OperationFailed("Failed to compute hash: ${e.message}", e)
            )
        }
    }
}

/**
 * Use case: Get storage information (capacity, usage)
 */
class GetStorageInfoUseCase @Inject constructor(
    private val storageAdapter: StorageAdapter
) {
    suspend operator fun invoke() = storageAdapter.getStorageInfo()
}

/**
 * Use case: Cleanup old temporary files
 */
class CleanupTempFilesUseCase @Inject constructor(
    private val storageAdapter: StorageAdapter
) {
    suspend operator fun invoke(olderThanHours: Int = 24): StorageOperationResult<Int> {
        val olderThanMs = olderThanHours * 60 * 60 * 1000L
        return try {
            storageAdapter.cleanupTempFiles(olderThanMs)
        } catch (e: Exception) {
            Timber.e(e, "Error cleaning up temp files")
            StorageOperationResult.Error(
                StorageException.OperationFailed("Failed to cleanup temp files: ${e.message}", e)
            )
        }
    }
}
