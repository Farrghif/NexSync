package com.nexsync.storagenode.data.db.daos

import androidx.room.Dao
import androidx.room.Delete
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import androidx.room.Update
import com.nexsync.storagenode.data.db.entities.StorageNodeEntity
import kotlinx.coroutines.flow.Flow

@Dao
interface StorageNodeDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insert(node: StorageNodeEntity)

    @Update
    suspend fun update(node: StorageNodeEntity)

    @Query("SELECT * FROM storage_nodes WHERE id = :nodeId")
    suspend fun getById(nodeId: String): StorageNodeEntity?

    @Query("SELECT * FROM storage_nodes WHERE userId = :userId LIMIT 1")
    suspend fun getByUserId(userId: String): StorageNodeEntity?

    @Query("SELECT * FROM storage_nodes WHERE userId = :userId")
    fun observeByUserId(userId: String): Flow<StorageNodeEntity?>

    @Query("UPDATE storage_nodes SET isOnline = :isOnline, lastHeartbeatAt = :timestamp WHERE id = :nodeId")
    suspend fun updateOnlineStatus(nodeId: String, isOnline: Boolean, timestamp: Long)

    @Query("UPDATE storage_nodes SET storageUsedBytes = :usedBytes, storageCapacityBytes = :capacityBytes WHERE id = :nodeId")
    suspend fun updateStorageInfo(nodeId: String, usedBytes: Long, capacityBytes: Long)

    @Delete
    suspend fun delete(node: StorageNodeEntity)
}

@Dao
interface DeviceCredentialsDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insert(credentials: com.nexsync.storagenode.data.db.entities.DeviceCredentialsEntity)

    @Update
    suspend fun update(credentials: com.nexsync.storagenode.data.db.entities.DeviceCredentialsEntity)

    @Query("SELECT * FROM device_credentials WHERE deviceId = :deviceId")
    suspend fun getByDeviceId(deviceId: String): com.nexsync.storagenode.data.db.entities.DeviceCredentialsEntity?

    @Query("SELECT * FROM device_credentials LIMIT 1")
    suspend fun getCurrent(): com.nexsync.storagenode.data.db.entities.DeviceCredentialsEntity?

    @Query("UPDATE device_credentials SET accessToken = :token, accessTokenExpiresAt = :expiresAt, updatedAt = :timestamp WHERE deviceId = :deviceId")
    suspend fun updateAccessToken(deviceId: String, token: String, expiresAt: Long, timestamp: Long)

    @Query("SELECT * FROM device_credentials WHERE accessTokenExpiresAt < :now")
    suspend fun getExpiredCredentials(now: Long): List<com.nexsync.storagenode.data.db.entities.DeviceCredentialsEntity>

    @Query("DELETE FROM device_credentials WHERE deviceId = :deviceId")
    suspend fun deleteByDeviceId(deviceId: String)
}

@Dao
interface PairingChallengeDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insert(challenge: com.nexsync.storagenode.data.db.entities.PairingChallengeEntity)

    @Update
    suspend fun update(challenge: com.nexsync.storagenode.data.db.entities.PairingChallengeEntity)

    @Query("SELECT * FROM pairing_challenges WHERE challengeId = :challengeId")
    suspend fun getByChallengeId(challengeId: String): com.nexsync.storagenode.data.db.entities.PairingChallengeEntity?

    @Query("SELECT * FROM pairing_challenges WHERE code = :code AND expiresAt > :now AND isUsed = 0 LIMIT 1")
    suspend fun getUnusedByCode(code: String, now: Long): com.nexsync.storagenode.data.db.entities.PairingChallengeEntity?

    @Query("UPDATE pairing_challenges SET isUsed = 1, usedAt = :timestamp WHERE challengeId = :challengeId")
    suspend fun markAsUsed(challengeId: String, timestamp: Long)

    @Query("DELETE FROM pairing_challenges WHERE expiresAt < :now")
    suspend fun deleteExpired(now: Long)
}

@Dao
interface StorageDirectoryDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insert(directory: com.nexsync.storagenode.data.db.entities.StorageDirectoryEntity)

    @Update
    suspend fun update(directory: com.nexsync.storagenode.data.db.entities.StorageDirectoryEntity)

    @Query("SELECT * FROM storage_directories WHERE id = :id")
    suspend fun getById(id: String): com.nexsync.storagenode.data.db.entities.StorageDirectoryEntity?

    @Query("SELECT * FROM storage_directories WHERE uri = :uri")
    suspend fun getByUri(uri: String): com.nexsync.storagenode.data.db.entities.StorageDirectoryEntity?

    @Query("SELECT * FROM storage_directories LIMIT 1")
    suspend fun getPrimary(): com.nexsync.storagenode.data.db.entities.StorageDirectoryEntity?

    @Query("SELECT * FROM storage_directories")
    fun observeAll(): Flow<List<com.nexsync.storagenode.data.db.entities.StorageDirectoryEntity>>

    @Query("UPDATE storage_directories SET isAccessible = :isAccessible, lastVerifiedAt = :timestamp WHERE id = :id")
    suspend fun updateAccessibility(id: String, isAccessible: Boolean, timestamp: Long)

    @Query("DELETE FROM storage_directories WHERE id = :id")
    suspend fun delete(id: String)
}

@Dao
interface StorageFileDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insert(file: com.nexsync.storagenode.data.db.entities.StorageFileEntity)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertMany(files: List<com.nexsync.storagenode.data.db.entities.StorageFileEntity>)

    @Update
    suspend fun update(file: com.nexsync.storagenode.data.db.entities.StorageFileEntity)

    @Query("SELECT * FROM storage_files WHERE id = :id")
    suspend fun getById(id: String): com.nexsync.storagenode.data.db.entities.StorageFileEntity?

    @Query("SELECT * FROM storage_files WHERE uri = :uri")
    suspend fun getByUri(uri: String): com.nexsync.storagenode.data.db.entities.StorageFileEntity?

    @Query("SELECT * FROM storage_files WHERE directoryId = :directoryId AND parentUri IS NULL ORDER BY isDirectory DESC, name ASC")
    suspend fun getRootFiles(directoryId: String): List<com.nexsync.storagenode.data.db.entities.StorageFileEntity>

    @Query("SELECT * FROM storage_files WHERE directoryId = :directoryId AND parentUri = :parentUri ORDER BY isDirectory DESC, name ASC")
    suspend fun getChildrenByParent(directoryId: String, parentUri: String): List<com.nexsync.storagenode.data.db.entities.StorageFileEntity>

    @Query("SELECT * FROM storage_files WHERE directoryId = :directoryId AND isTemp = 1")
    suspend fun getTempFiles(directoryId: String): List<com.nexsync.storagenode.data.db.entities.StorageFileEntity>

    @Query("DELETE FROM storage_files WHERE id = :id")
    suspend fun delete(id: String)

    @Query("DELETE FROM storage_files WHERE directoryId = :directoryId AND isTemp = 1 AND createdAt < :olderThan")
    suspend fun deleteOldTempFiles(directoryId: String, olderThan: Long)
}

@Dao
interface FileTransferDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insert(transfer: com.nexsync.storagenode.data.db.entities.FileTransferEntity)

    @Update
    suspend fun update(transfer: com.nexsync.storagenode.data.db.entities.FileTransferEntity)

    @Query("SELECT * FROM file_transfers WHERE id = :id")
    suspend fun getById(id: String): com.nexsync.storagenode.data.db.entities.FileTransferEntity?

    @Query("SELECT * FROM file_transfers WHERE status IN ('PENDING', 'IN_PROGRESS') ORDER BY createdAt ASC")
    suspend fun getActiveTransfers(): List<com.nexsync.storagenode.data.db.entities.FileTransferEntity>

    @Query("SELECT * FROM file_transfers WHERE status = :status ORDER BY createdAt DESC LIMIT :limit")
    suspend fun getByStatus(status: String, limit: Int = 50): List<com.nexsync.storagenode.data.db.entities.FileTransferEntity>

    @Query("SELECT * FROM file_transfers ORDER BY startedAt DESC LIMIT :limit")
    fun observeRecent(limit: Int = 10): Flow<List<com.nexsync.storagenode.data.db.entities.FileTransferEntity>>

    @Query("UPDATE file_transfers SET status = :status, transferredBytes = :transferredBytes, updatedAt = CURRENT_TIMESTAMP WHERE id = :id")
    suspend fun updateProgress(id: String, status: String, transferredBytes: Long)

    @Query("DELETE FROM file_transfers WHERE completedAt < :olderThan AND status IN ('COMPLETED', 'FAILED_PERMANENTLY')")
    suspend fun deleteOldTransfers(olderThan: Long)
}

@Dao
interface AuthorizedDeviceDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insert(device: com.nexsync.storagenode.data.db.entities.AuthorizedDeviceEntity)

    @Update
    suspend fun update(device: com.nexsync.storagenode.data.db.entities.AuthorizedDeviceEntity)

    @Query("SELECT * FROM authorized_devices WHERE id = :id")
    suspend fun getById(id: String): com.nexsync.storagenode.data.db.entities.AuthorizedDeviceEntity?

    @Query("SELECT * FROM authorized_devices WHERE deviceId = :deviceId")
    suspend fun getByDeviceId(deviceId: String): com.nexsync.storagenode.data.db.entities.AuthorizedDeviceEntity?

    @Query("SELECT * FROM authorized_devices WHERE revokedAt IS NULL ORDER BY lastSeenAt DESC")
    suspend fun getActiveDevices(): List<com.nexsync.storagenode.data.db.entities.AuthorizedDeviceEntity>

    @Query("SELECT * FROM authorized_devices WHERE revokedAt IS NULL")
    fun observeActiveDevices(): Flow<List<com.nexsync.storagenode.data.db.entities.AuthorizedDeviceEntity>>

    @Query("UPDATE authorized_devices SET isOnline = :isOnline, lastSeenAt = :timestamp WHERE id = :id")
    suspend fun updateOnlineStatus(id: String, isOnline: Boolean, timestamp: Long)

    @Query("UPDATE authorized_devices SET revokedAt = :timestamp WHERE id = :id")
    suspend fun revoke(id: String, timestamp: Long)

    @Query("DELETE FROM authorized_devices WHERE id = :id")
    suspend fun delete(id: String)
}

@Dao
interface PendingOperationDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insert(operation: com.nexsync.storagenode.data.db.entities.PendingOperationEntity)

    @Update
    suspend fun update(operation: com.nexsync.storagenode.data.db.entities.PendingOperationEntity)

    @Query("SELECT * FROM pending_operations WHERE id = :id")
    suspend fun getById(id: String): com.nexsync.storagenode.data.db.entities.PendingOperationEntity?

    @Query("SELECT * FROM pending_operations WHERE status = 'PENDING' ORDER BY createdAt ASC")
    suspend fun getPendingOperations(): List<com.nexsync.storagenode.data.db.entities.PendingOperationEntity>

    @Query("SELECT * FROM pending_operations WHERE status IN ('PENDING', 'IN_PROGRESS') ORDER BY createdAt ASC")
    fun observePendingOperations(): Flow<List<com.nexsync.storagenode.data.db.entities.PendingOperationEntity>>

    @Query("UPDATE pending_operations SET status = :status, retryCount = retryCount + 1, lastErrorMessage = :errorMessage WHERE id = :id")
    suspend fun updateStatus(id: String, status: String, errorMessage: String? = null)

    @Query("DELETE FROM pending_operations WHERE status = 'COMPLETED' AND executedAt < :olderThan")
    suspend fun deleteOldCompleted(olderThan: Long)
}

@Dao
interface ServerStateDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insert(state: com.nexsync.storagenode.data.db.entities.ServerStateEntity)

    @Update
    suspend fun update(state: com.nexsync.storagenode.data.db.entities.ServerStateEntity)

    @Query("SELECT * FROM server_state WHERE id = 1")
    suspend fun getState(): com.nexsync.storagenode.data.db.entities.ServerStateEntity?

    @Query("SELECT * FROM server_state WHERE id = 1")
    fun observeState(): Flow<com.nexsync.storagenode.data.db.entities.ServerStateEntity?>

    @Query("UPDATE server_state SET isRunning = :isRunning, updatedAt = :timestamp WHERE id = 1")
    suspend fun updateRunningState(isRunning: Boolean, timestamp: Long)
}
