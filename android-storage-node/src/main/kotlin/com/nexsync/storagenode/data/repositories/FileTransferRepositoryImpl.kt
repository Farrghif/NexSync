package com.nexsync.storagenode.data.repositories

import com.nexsync.storagenode.data.db.daos.FileTransferDao
import com.nexsync.storagenode.data.db.entities.FileTransferEntity
import com.nexsync.storagenode.domain.entities.FileTransfer
import com.nexsync.storagenode.domain.entities.TransferDirection
import com.nexsync.storagenode.domain.entities.TransferStatus
import com.nexsync.storagenode.domain.repositories.FileTransferRepository
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map
import java.util.UUID
import javax.inject.Inject

class FileTransferRepositoryImpl @Inject constructor(
    private val dao: FileTransferDao
) : FileTransferRepository {

    override suspend fun saveTransfer(transfer: FileTransfer) {
        val entity = transfer.toEntity()
        dao.insert(entity)
    }

    override suspend fun getTransfer(id: String): FileTransfer? {
        return dao.getById(id)?.toDomain()
    }

    override suspend fun getActiveTransfers(): List<FileTransfer> {
        return dao.getActiveTransfers().map { it.toDomain() }
    }

    override suspend fun getTransfersByStatus(status: String, limit: Int): List<FileTransfer> {
        return dao.getByStatus(status, limit).map { it.toDomain() }
    }

    override fun observeRecentTransfers(limit: Int): Flow<List<FileTransfer>> {
        return dao.observeRecent(limit).map { transfers -> transfers.map { it.toDomain() } }
    }

    override suspend fun updateTransferProgress(id: String, status: String, transferredBytes: Long) {
        dao.updateProgress(id, status, transferredBytes)
    }

    override suspend fun deleteOldTransfers(olderThan: Long) {
        dao.deleteOldTransfers(olderThan)
    }

    private fun FileTransfer.toEntity(): FileTransferEntity {
        return FileTransferEntity(
            id = this.id.toString(),
            fileId = this.fileId.toString(),
            direction = this.direction.name,
            sourcePath = this.sourcePath,
            destinationPath = this.destinationPath,
            totalBytes = this.totalBytes,
            transferredBytes = this.transferredBytes,
            status = this.status.name,
            errorMessage = this.errorMessage,
            startedAt = this.startedAt,
            completedAt = this.completedAt,
            checksumVerified = this.checksumVerified,
            expectedSha256 = this.expectedSha256,
            createdAt = System.currentTimeMillis()
        )
    }

    private fun FileTransferEntity.toDomain(): FileTransfer {
        return FileTransfer(
            id = UUID.fromString(this.id),
            fileId = UUID.fromString(this.fileId),
            direction = TransferDirection.valueOf(this.direction),
            sourcePath = this.sourcePath,
            destinationPath = this.destinationPath,
            totalBytes = this.totalBytes,
            transferredBytes = this.transferredBytes,
            status = TransferStatus.valueOf(this.status),
            errorMessage = this.errorMessage,
            startedAt = this.startedAt,
            completedAt = this.completedAt,
            checksumVerified = this.checksumVerified,
            expectedSha256 = this.expectedSha256
        )
    }
}
