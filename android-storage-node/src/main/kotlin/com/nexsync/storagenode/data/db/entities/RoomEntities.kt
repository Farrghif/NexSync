package com.nexsync.storagenode.data.db.entities

import androidx.room.Entity
import androidx.room.ForeignKey
import androidx.room.Index
import androidx.room.PrimaryKey
import java.util.UUID

@Entity(
    tableName = "storage_nodes",
    indices = [
        Index(value = ["userId"], unique = false),
        Index(value = ["createdAt"], unique = false)
    ]
)
data class StorageNodeEntity(
    @PrimaryKey val id: String, // UUID as string
    val userId: String,
    val deviceName: String,
    val platform: String = "Android",
    val protocolVersion: String = "2.0",
    val isOnline: Boolean = false,
    val lastHeartbeatAt: Long,
    val storageCapacityBytes: Long? = null,
    val storageUsedBytes: Long? = null,
    val supportedOperations: String, // Serialized JSON array
    val appVersion: String = "1.0.0",
    val createdAt: Long,
    val revokedAt: Long? = null
)

@Entity(
    tableName = "device_credentials",
    indices = [
        Index(value = ["deviceId"], unique = true),
        Index(value = ["accessTokenExpiresAt"], unique = false)
    ]
)
data class DeviceCredentialsEntity(
    @PrimaryKey val deviceId: String,
    val accessToken: String,
    val accessTokenExpiresAt: Long,
    val refreshToken: String,
    val refreshTokenExpiresAt: Long,
    val tokenFamily: String? = null,
    val createdAt: Long,
    val updatedAt: Long
)

@Entity(
    tableName = "pairing_challenges",
    indices = [
        Index(value = ["challengeId"], unique = true),
        Index(value = ["userId"], unique = false),
        Index(value = ["expiresAt"], unique = false)
    ]
)
data class PairingChallengeEntity(
    @PrimaryKey val challengeId: String,
    val userId: String,
    val code: String,
    val expiresAt: Long,
    val isUsed: Boolean = false,
    val createdAt: Long,
    val usedAt: Long? = null
)

@Entity(
    tableName = "storage_directories",
    indices = [
        Index(value = ["uri"], unique = true),
        Index(value = ["selectedAt"], unique = false)
    ]
)
data class StorageDirectoryEntity(
    @PrimaryKey val id: String,
    val uri: String, // DocumentTree URI
    val displayName: String,
    val path: String? = null,
    val totalBytes: Long,
    val freeBytes: Long,
    val isAccessible: Boolean = true,
    val selectedAt: Long,
    val lastVerifiedAt: Long
)

@Entity(
    tableName = "storage_files",
    foreignKeys = [
        ForeignKey(
            entity = StorageDirectoryEntity::class,
            parentColumns = ["id"],
            childColumns = ["directoryId"],
            onDelete = ForeignKey.CASCADE
        )
    ],
    indices = [
        Index(value = ["directoryId"], unique = false),
        Index(value = ["uri"], unique = true),
        Index(value = ["name"], unique = false),
        Index(value = ["lastModifiedAt"], unique = false),
        Index(value = ["isTemp"], unique = false)
    ]
)
data class StorageFileEntity(
    @PrimaryKey val id: String,
    val directoryId: String,
    val uri: String,
    val name: String,
    val parentUri: String? = null,
    val isDirectory: Boolean = false,
    val sizeBytes: Long = 0,
    val sha256Hash: String? = null,
    val lastModifiedAt: Long,
    val createdAt: Long,
    val isTemp: Boolean = false
)

@Entity(
    tableName = "file_transfers",
    indices = [
        Index(value = ["fileId"], unique = false),
        Index(value = ["direction"], unique = false),
        Index(value = ["status"], unique = false),
        Index(value = ["startedAt"], unique = false)
    ]
)
data class FileTransferEntity(
    @PrimaryKey val id: String,
    val fileId: String,
    val direction: String, // UPLOAD or DOWNLOAD
    val sourcePath: String,
    val destinationPath: String,
    val totalBytes: Long,
    val transferredBytes: Long = 0,
    val status: String = "PENDING",
    val errorMessage: String? = null,
    val startedAt: Long? = null,
    val completedAt: Long? = null,
    val checksumVerified: Boolean = false,
    val expectedSha256: String? = null,
    val createdAt: Long
)

@Entity(
    tableName = "authorized_devices",
    indices = [
        Index(value = ["deviceId"], unique = true),
        Index(value = ["platform"], unique = false),
        Index(value = ["isOnline"], unique = false),
        Index(value = ["revokedAt"], unique = false)
    ]
)
data class AuthorizedDeviceEntity(
    @PrimaryKey val id: String,
    val deviceId: String,
    val deviceName: String,
    val platform: String,
    val isOnline: Boolean = false,
    val lastSeenAt: Long,
    val permissions: String, // Serialized JSON array
    val revokedAt: Long? = null,
    val pairedAt: Long
)

@Entity(
    tableName = "pending_operations",
    indices = [
        Index(value = ["status"], unique = false),
        Index(value = ["createdAt"], unique = false),
        Index(value = ["retryCount"], unique = false)
    ]
)
data class PendingOperationEntity(
    @PrimaryKey val id: String,
    val operationType: String,
    val targetPath: String,
    val payloadJson: String? = null,
    val status: String = "PENDING",
    val retryCount: Int = 0,
    val lastErrorMessage: String? = null,
    val createdAt: Long,
    val executedAt: Long? = null
)

@Entity(tableName = "server_state")
data class ServerStateEntity(
    @PrimaryKey val id: Int = 1, // Singleton
    val isRunning: Boolean = false,
    val listeningPort: Int = 8443,
    val localAddress: String? = null,
    val mDnsName: String? = null,
    val isBackendConnected: Boolean = false,
    val lastHealthCheckAt: Long? = null,
    val errorMessage: String? = null,
    val updatedAt: Long = System.currentTimeMillis()
)
