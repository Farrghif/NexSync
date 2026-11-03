package com.nexsync.storagenode.data.repositories

import com.nexsync.storagenode.data.db.daos.StorageDirectoryDao
import com.nexsync.storagenode.data.db.entities.StorageDirectoryEntity
import com.nexsync.storagenode.domain.entities.StorageDirectory
import com.nexsync.storagenode.domain.repositories.StorageDirectoryRepository
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map
import java.util.UUID
import javax.inject.Inject

class StorageDirectoryRepositoryImpl @Inject constructor(
    private val dao: StorageDirectoryDao
) : StorageDirectoryRepository {

    override suspend fun saveDirectory(directory: StorageDirectory) {
        val entity = StorageDirectoryEntity(
            id = directory.id.toString(),
            uri = directory.uri,
            displayName = directory.displayName,
            path = directory.path,
            totalBytes = directory.totalBytes,
            freeBytes = directory.freeBytes,
            isAccessible = directory.isAccessible,
            selectedAt = directory.selectedAt,
            lastVerifiedAt = directory.lastVerifiedAt
        )
        dao.insert(entity)
    }

    override suspend fun getDirectory(id: String): StorageDirectory? {
        return dao.getById(id)?.toDomain()
    }

    override suspend fun getDirectoryByUri(uri: String): StorageDirectory? {
        return dao.getByUri(uri)?.toDomain()
    }

    override suspend fun getPrimaryDirectory(): StorageDirectory? {
        return dao.getPrimary()?.toDomain()
    }

    override fun observeAllDirectories(): Flow<List<StorageDirectory>> {
        return dao.observeAll().map { entities -> entities.map { it.toDomain() } }
    }

    override suspend fun updateAccessibility(id: String, isAccessible: Boolean) {
        dao.updateAccessibility(id, isAccessible, System.currentTimeMillis())
    }

    override suspend fun deleteDirectory(id: String) {
        dao.delete(id)
    }

    private fun StorageDirectoryEntity.toDomain(): StorageDirectory {
        return StorageDirectory(
            id = UUID.fromString(this.id),
            uri = this.uri,
            displayName = this.displayName,
            path = this.path,
            totalBytes = this.totalBytes,
            freeBytes = this.freeBytes,
            isAccessible = this.isAccessible,
            selectedAt = this.selectedAt,
            lastVerifiedAt = this.lastVerifiedAt
        )
    }
}
