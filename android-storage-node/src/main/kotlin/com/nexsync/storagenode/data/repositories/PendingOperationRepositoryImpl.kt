package com.nexsync.storagenode.data.repositories

import com.nexsync.storagenode.data.db.daos.PendingOperationDao
import com.nexsync.storagenode.data.db.entities.PendingOperationEntity
import com.nexsync.storagenode.domain.entities.OperationStatus
import com.nexsync.storagenode.domain.entities.PendingOperation
import com.nexsync.storagenode.domain.repositories.PendingOperationRepository
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map
import java.util.UUID
import javax.inject.Inject

class PendingOperationRepositoryImpl @Inject constructor(
    private val dao: PendingOperationDao
) : PendingOperationRepository {

    override suspend fun savePendingOperation(operation: PendingOperation) {
        val entity = PendingOperationEntity(
            id = operation.id.toString(),
            operationType = operation.operationType,
            targetPath = operation.targetPath,
            payloadJson = operation.payloadJson,
            status = operation.status.name,
            retryCount = operation.retryCount,
            lastErrorMessage = operation.lastErrorMessage,
            createdAt = operation.createdAt,
            executedAt = operation.executedAt
        )
        dao.insert(entity)
    }

    override suspend fun getOperation(id: String): PendingOperation? {
        return dao.getById(id)?.toDomain()
    }

    override suspend fun getPendingOperations(): List<PendingOperation> {
        return dao.getPendingOperations().map { it.toDomain() }
    }

    override fun observePendingOperations(): Flow<List<PendingOperation>> {
        return dao.observePendingOperations().map { operations -> operations.map { it.toDomain() } }
    }

    override suspend fun updateOperationStatus(id: String, status: String, errorMessage: String?) {
        dao.updateStatus(id, status, errorMessage)
    }

    override suspend fun deleteOldCompletedOperations(olderThan: Long) {
        dao.deleteOldCompleted(olderThan)
    }

    private fun PendingOperationEntity.toDomain(): PendingOperation {
        return PendingOperation(
            id = UUID.fromString(this.id),
            operationType = this.operationType,
            targetPath = this.targetPath,
            payloadJson = this.payloadJson,
            status = OperationStatus.valueOf(this.status),
            retryCount = this.retryCount,
            lastErrorMessage = this.lastErrorMessage,
            createdAt = this.createdAt,
            executedAt = this.executedAt
        )
    }
}
