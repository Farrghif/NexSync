package com.nexsync.storagenode.data.repositories

import com.nexsync.storagenode.data.db.daos.StorageFileDao
import com.nexsync.storagenode.data.db.entities.StorageFileEntity
import com.nexsync.storagenode.domain.entities.StorageFile
import com.nexsync.storagenode.domain.repositories.StorageFileRepository
import java.util.UUID
import javax.inject.Inject

class StorageFileRepositoryImpl @Inject constructor(
    private val dao: StorageFileDao
) : StorageFileRepository {

    override suspend fun saveFile(file: StorageFile) {
        val entity = file.toEntity()
        dao.insert(entity)
    }

    override suspend fun saveFiles(files: List<StorageFile>) {
        val entities = files.map { it.toEntity() }
        dao.insertMany(entities)
    }

    override suspend fun getFile(id: String): StorageFile? {
        return dao.getById(id)?.toDomain()
    }

    override suspend fun getFileByUri(uri: String): StorageFile? {
        return dao.getByUri(uri)?.toDomain()
    }

    override suspend fun getRootFiles(directoryId: String): List<StorageFile> {
        return dao.getRootFiles(directoryId).map { it.toDomain() }
    }

    override suspend fun getChildren(directoryId: String, parentUri: String): List<StorageFile> {
        return dao.getChildrenByParent(directoryId, parentUri).map { it.toDomain() }
    }

    override suspend fun getTempFiles(directoryId: String): List<StorageFile> {
        return dao.getTempFiles(directoryId).map { it.toDomain() }
    }

    override suspend fun deleteFile(id: String) {
        dao.delete(id)
    }

    override suspend fun deleteOldTempFiles(directoryId: String, olderThan: Long) {
        dao.deleteOldTempFiles(directoryId, olderThan)
    }

    private fun StorageFile.toEntity(): StorageFileEntity {
        return StorageFileEntity(
            id = this.id.toString(),
            directoryId = "", // Will be set by caller
            uri = this.uri,
            name = this.name,
            parentUri = this.parentUri,
            isDirectory = this.isDirectory,
            sizeBytes = this.sizeBytes,
            sha256Hash = this.sha256Hash,
            lastModifiedAt = this.lastModifiedAt,
            createdAt = this.createdAt,
            isTemp = this.isTemp
        )
    }

    private fun StorageFileEntity.toDomain(): StorageFile {
        return StorageFile(
            id = UUID.fromString(this.id),
            uri = this.uri,
            name = this.name,
            parentUri = this.parentUri,
            isDirectory = this.isDirectory,
            sizeBytes = this.sizeBytes,
            sha256Hash = this.sha256Hash,
            lastModifiedAt = this.lastModifiedAt,
            createdAt = this.createdAt,
            isTemp = this.isTemp
        )
    }
}
