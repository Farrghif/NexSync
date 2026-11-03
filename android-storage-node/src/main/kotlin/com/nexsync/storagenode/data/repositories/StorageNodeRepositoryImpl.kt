package com.nexsync.storagenode.data.repositories

import com.nexsync.storagenode.data.db.daos.StorageNodeDao
import com.nexsync.storagenode.data.db.entities.StorageNodeEntity
import com.nexsync.storagenode.domain.entities.StorageNode
import com.nexsync.storagenode.domain.repositories.StorageNodeRepository
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map
import java.util.UUID
import javax.inject.Inject

class StorageNodeRepositoryImpl @Inject constructor(
    private val dao: StorageNodeDao
) : StorageNodeRepository {

    override suspend fun saveNode(node: StorageNode) {
        val entity = node.toEntity()
        dao.insert(entity)
    }

    override suspend fun getNode(nodeId: String): StorageNode? {
        return dao.getById(nodeId)?.toDomain()
    }

    override suspend fun getNodeByUserId(userId: String): StorageNode? {
        return dao.getByUserId(userId)?.toDomain()
    }

    override fun observeNodeByUserId(userId: String): Flow<StorageNode?> {
        return dao.observeByUserId(userId).map { it?.toDomain() }
    }

    override suspend fun updateOnlineStatus(nodeId: String, isOnline: Boolean) {
        dao.updateOnlineStatus(nodeId, isOnline, System.currentTimeMillis())
    }

    override suspend fun updateStorageInfo(nodeId: String, usedBytes: Long, capacityBytes: Long) {
        dao.updateStorageInfo(nodeId, usedBytes, capacityBytes)
    }

    override suspend fun deleteNode(nodeId: String) {
        dao.getById(nodeId)?.let { dao.delete(it) }
    }

    private fun StorageNode.toEntity(): StorageNodeEntity {
        return StorageNodeEntity(
            id = this.id.toString(),
            userId = this.userId.toString(),
            deviceName = this.deviceName,
            platform = this.platform,
            protocolVersion = this.protocolVersion,
            isOnline = this.isOnline,
            lastHeartbeatAt = this.lastHeartbeatAt,
            storageCapacityBytes = this.storageCapacityBytes,
            storageUsedBytes = this.storageUsedBytes,
            supportedOperations = this.supportedOperations.joinToString(","),
            appVersion = this.appVersion,
            createdAt = this.createdAt,
            revokedAt = this.revokedAt
        )
    }

    private fun StorageNodeEntity.toDomain(): StorageNode {
        return StorageNode(
            id = UUID.fromString(this.id),
            userId = UUID.fromString(this.userId),
            deviceName = this.deviceName,
            platform = this.platform,
            protocolVersion = this.protocolVersion,
            isOnline = this.isOnline,
            lastHeartbeatAt = this.lastHeartbeatAt,
            storageCapacityBytes = this.storageCapacityBytes,
            storageUsedBytes = this.storageUsedBytes,
            supportedOperations = this.supportedOperations.split(",").toSet(),
            appVersion = this.appVersion,
            createdAt = this.createdAt,
            revokedAt = this.revokedAt
        )
    }
}
