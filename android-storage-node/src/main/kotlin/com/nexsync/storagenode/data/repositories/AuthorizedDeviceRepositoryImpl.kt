package com.nexsync.storagenode.data.repositories

import com.nexsync.storagenode.data.db.daos.AuthorizedDeviceDao
import com.nexsync.storagenode.data.db.entities.AuthorizedDeviceEntity
import com.nexsync.storagenode.domain.entities.AuthorizedDevice
import com.nexsync.storagenode.domain.repositories.AuthorizedDeviceRepository
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map
import java.util.UUID
import javax.inject.Inject

class AuthorizedDeviceRepositoryImpl @Inject constructor(
    private val dao: AuthorizedDeviceDao
) : AuthorizedDeviceRepository {

    override suspend fun saveDevice(device: AuthorizedDevice) {
        val entity = AuthorizedDeviceEntity(
            id = device.id.toString(),
            deviceId = device.deviceId.toString(),
            deviceName = device.deviceName,
            platform = device.platform,
            isOnline = device.isOnline,
            lastSeenAt = device.lastSeenAt,
            permissions = device.permissions.joinToString(","),
            revokedAt = device.revokedAt,
            pairedAt = device.pairedAt
        )
        dao.insert(entity)
    }

    override suspend fun getDevice(id: String): AuthorizedDevice? {
        return dao.getById(id)?.toDomain()
    }

    override suspend fun getDeviceByDeviceId(deviceId: String): AuthorizedDevice? {
        return dao.getByDeviceId(deviceId)?.toDomain()
    }

    override suspend fun getActiveDevices(): List<AuthorizedDevice> {
        return dao.getActiveDevices().map { it.toDomain() }
    }

    override fun observeActiveDevices(): Flow<List<AuthorizedDevice>> {
        return dao.observeActiveDevices().map { devices -> devices.map { it.toDomain() } }
    }

    override suspend fun updateOnlineStatus(id: String, isOnline: Boolean) {
        dao.updateOnlineStatus(id, isOnline, System.currentTimeMillis())
    }

    override suspend fun revokeDevice(id: String) {
        dao.revoke(id, System.currentTimeMillis())
    }

    override suspend fun deleteDevice(id: String) {
        dao.delete(id)
    }

    private fun AuthorizedDeviceEntity.toDomain(): AuthorizedDevice {
        return AuthorizedDevice(
            id = UUID.fromString(this.id),
            deviceId = UUID.fromString(this.deviceId),
            deviceName = this.deviceName,
            platform = this.platform,
            isOnline = this.isOnline,
            lastSeenAt = this.lastSeenAt,
            permissions = this.permissions.split(",").toSet(),
            revokedAt = this.revokedAt,
            pairedAt = this.pairedAt
        )
    }
}
