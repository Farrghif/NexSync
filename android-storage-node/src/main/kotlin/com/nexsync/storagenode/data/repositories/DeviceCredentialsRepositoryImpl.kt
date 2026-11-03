package com.nexsync.storagenode.data.repositories

import com.nexsync.storagenode.data.db.daos.DeviceCredentialsDao
import com.nexsync.storagenode.data.db.entities.DeviceCredentialsEntity
import com.nexsync.storagenode.domain.entities.DeviceCredentials
import com.nexsync.storagenode.domain.repositories.DeviceCredentialsRepository
import java.util.UUID
import javax.inject.Inject

class DeviceCredentialsRepositoryImpl @Inject constructor(
    private val dao: DeviceCredentialsDao
) : DeviceCredentialsRepository {

    override suspend fun saveCredentials(
        deviceId: String,
        accessToken: String,
        accessTokenExpiresAt: Long,
        refreshToken: String,
        refreshTokenExpiresAt: Long,
        tokenFamily: String?
    ) {
        val entity = DeviceCredentialsEntity(
            deviceId = deviceId,
            accessToken = accessToken,
            accessTokenExpiresAt = accessTokenExpiresAt,
            refreshToken = refreshToken,
            refreshTokenExpiresAt = refreshTokenExpiresAt,
            tokenFamily = tokenFamily,
            createdAt = System.currentTimeMillis(),
            updatedAt = System.currentTimeMillis()
        )
        dao.insert(entity)
    }

    override suspend fun getCredentials(deviceId: String): DeviceCredentials? {
        return dao.getByDeviceId(deviceId)?.toDomain()
    }

    override suspend fun getCurrentCredentials(): DeviceCredentials? {
        return dao.getCurrent()?.toDomain()
    }

    override suspend fun updateAccessToken(deviceId: String, token: String, expiresAt: Long) {
        dao.updateAccessToken(deviceId, token, expiresAt, System.currentTimeMillis())
    }

    override suspend fun getExpiredCredentials(): List<DeviceCredentials> {
        return dao.getExpiredCredentials(System.currentTimeMillis()).map { it.toDomain() }
    }

    override suspend fun deleteCredentials(deviceId: String) {
        dao.deleteByDeviceId(deviceId)
    }

    override suspend fun isCredentialsValid(deviceId: String): Boolean {
        val creds = dao.getByDeviceId(deviceId) ?: return false
        return creds.accessTokenExpiresAt > System.currentTimeMillis()
    }

    private fun DeviceCredentialsEntity.toDomain(): DeviceCredentials {
        return DeviceCredentials(
            deviceId = UUID.fromString(this.deviceId),
            accessToken = this.accessToken,
            accessTokenExpiresAt = this.accessTokenExpiresAt,
            refreshToken = this.refreshToken,
            refreshTokenExpiresAt = this.refreshTokenExpiresAt,
            tokenFamily = this.tokenFamily,
            createdAt = this.createdAt
        )
    }
}
