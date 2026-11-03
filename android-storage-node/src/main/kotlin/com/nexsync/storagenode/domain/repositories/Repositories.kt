package com.nexsync.storagenode.domain.repositories

import com.nexsync.storagenode.domain.entities.StorageNode
import kotlinx.coroutines.flow.Flow

interface StorageNodeRepository {
    suspend fun saveNode(node: StorageNode)
    suspend fun getNode(nodeId: String): StorageNode?
    suspend fun getNodeByUserId(userId: String): StorageNode?
    fun observeNodeByUserId(userId: String): Flow<StorageNode?>
    suspend fun updateOnlineStatus(nodeId: String, isOnline: Boolean)
    suspend fun updateStorageInfo(nodeId: String, usedBytes: Long, capacityBytes: Long)
    suspend fun deleteNode(nodeId: String)
}

interface DeviceCredentialsRepository {
    suspend fun saveCredentials(deviceId: String, accessToken: String, accessTokenExpiresAt: Long, refreshToken: String, refreshTokenExpiresAt: Long, tokenFamily: String? = null)
    suspend fun getCredentials(deviceId: String): com.nexsync.storagenode.domain.entities.DeviceCredentials?
    suspend fun getCurrentCredentials(): com.nexsync.storagenode.domain.entities.DeviceCredentials?
    suspend fun updateAccessToken(deviceId: String, token: String, expiresAt: Long)
    suspend fun getExpiredCredentials(): List<com.nexsync.storagenode.domain.entities.DeviceCredentials>
    suspend fun deleteCredentials(deviceId: String)
    suspend fun isCredentialsValid(deviceId: String): Boolean
}

interface PairingChallengeRepository {
    suspend fun savePairingChallenge(challenge: com.nexsync.storagenode.domain.entities.PairingChallenge)
    suspend fun getPairingChallenge(challengeId: String): com.nexsync.storagenode.domain.entities.PairingChallenge?
    suspend fun getUnusedChallenge(code: String): com.nexsync.storagenode.domain.entities.PairingChallenge?
    suspend fun markChallengeAsUsed(challengeId: String)
    suspend fun deleteExpiredChallenges()
}

interface StorageDirectoryRepository {
    suspend fun saveDirectory(directory: com.nexsync.storagenode.domain.entities.StorageDirectory)
    suspend fun getDirectory(id: String): com.nexsync.storagenode.domain.entities.StorageDirectory?
    suspend fun getDirectoryByUri(uri: String): com.nexsync.storagenode.domain.entities.StorageDirectory?
    suspend fun getPrimaryDirectory(): com.nexsync.storagenode.domain.entities.StorageDirectory?
    fun observeAllDirectories(): Flow<List<com.nexsync.storagenode.domain.entities.StorageDirectory>>
    suspend fun updateAccessibility(id: String, isAccessible: Boolean)
    suspend fun deleteDirectory(id: String)
}

interface StorageFileRepository {
    suspend fun saveFile(file: com.nexsync.storagenode.domain.entities.StorageFile)
    suspend fun saveFiles(files: List<com.nexsync.storagenode.domain.entities.StorageFile>)
    suspend fun getFile(id: String): com.nexsync.storagenode.domain.entities.StorageFile?
    suspend fun getFileByUri(uri: String): com.nexsync.storagenode.domain.entities.StorageFile?
    suspend fun getRootFiles(directoryId: String): List<com.nexsync.storagenode.domain.entities.StorageFile>
    suspend fun getChildren(directoryId: String, parentUri: String): List<com.nexsync.storagenode.domain.entities.StorageFile>
    suspend fun getTempFiles(directoryId: String): List<com.nexsync.storagenode.domain.entities.StorageFile>
    suspend fun deleteFile(id: String)
    suspend fun deleteOldTempFiles(directoryId: String, olderThan: Long)
}

interface FileTransferRepository {
    suspend fun saveTransfer(transfer: com.nexsync.storagenode.domain.entities.FileTransfer)
    suspend fun getTransfer(id: String): com.nexsync.storagenode.domain.entities.FileTransfer?
    suspend fun getActiveTransfers(): List<com.nexsync.storagenode.domain.entities.FileTransfer>
    suspend fun getTransfersByStatus(status: String, limit: Int = 50): List<com.nexsync.storagenode.domain.entities.FileTransfer>
    fun observeRecentTransfers(limit: Int = 10): Flow<List<com.nexsync.storagenode.domain.entities.FileTransfer>>
    suspend fun updateTransferProgress(id: String, status: String, transferredBytes: Long)
    suspend fun deleteOldTransfers(olderThan: Long)
}

interface AuthorizedDeviceRepository {
    suspend fun saveDevice(device: com.nexsync.storagenode.domain.entities.AuthorizedDevice)
    suspend fun getDevice(id: String): com.nexsync.storagenode.domain.entities.AuthorizedDevice?
    suspend fun getDeviceByDeviceId(deviceId: String): com.nexsync.storagenode.domain.entities.AuthorizedDevice?
    suspend fun getActiveDevices(): List<com.nexsync.storagenode.domain.entities.AuthorizedDevice>
    fun observeActiveDevices(): Flow<List<com.nexsync.storagenode.domain.entities.AuthorizedDevice>>
    suspend fun updateOnlineStatus(id: String, isOnline: Boolean)
    suspend fun revokeDevice(id: String)
    suspend fun deleteDevice(id: String)
}

interface PendingOperationRepository {
    suspend fun savePendingOperation(operation: com.nexsync.storagenode.domain.entities.PendingOperation)
    suspend fun getOperation(id: String): com.nexsync.storagenode.domain.entities.PendingOperation?
    suspend fun getPendingOperations(): List<com.nexsync.storagenode.domain.entities.PendingOperation>
    fun observePendingOperations(): Flow<List<com.nexsync.storagenode.domain.entities.PendingOperation>>
    suspend fun updateOperationStatus(id: String, status: String, errorMessage: String? = null)
    suspend fun deleteOldCompletedOperations(olderThan: Long)
}

interface ServerStateRepository {
    suspend fun getServerState(): com.nexsync.storagenode.domain.entities.ServerState?
    fun observeServerState(): Flow<com.nexsync.storagenode.domain.entities.ServerState?>
    suspend fun updateRunningState(isRunning: Boolean)
    suspend fun setServerInfo(port: Int, address: String?, mDnsName: String?)
    suspend fun setBackendConnected(isConnected: Boolean)
    suspend fun setServerError(errorMessage: String?)
}
