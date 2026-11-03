package com.nexsync.storagenode.domain.entities

import java.util.UUID

/**
 * Represents a NexSync Storage Node (this Android device)
 * Synced with backend Device entity
 */
data class StorageNode(
    val id: UUID,
    val userId: UUID,
    val deviceName: String,
    val platform: String = "Android",
    val protocolVersion: String = "2.0",
    val isOnline: Boolean = false,
    val lastHeartbeatAt: Long = System.currentTimeMillis(),
    val storageCapacityBytes: Long? = null,
    val storageUsedBytes: Long? = null,
    val supportedOperations: Set<String> = setOf(
        "LIST", "READ", "WRITE", "CREATE", "DELETE", "MOVE", "RENAME"
    ),
    val appVersion: String = "1.0.0",
    val createdAt: Long = System.currentTimeMillis(),
    val revokedAt: Long? = null
)

/**
 * Device-specific authentication credentials
 * Stored encrypted in Android Keystore
 */
data class DeviceCredentials(
    val deviceId: UUID,
    val accessToken: String,
    val accessTokenExpiresAt: Long,
    val refreshToken: String,
    val refreshTokenExpiresAt: Long,
    val tokenFamily: String? = null, // For rotation tracking
    val createdAt: Long = System.currentTimeMillis()
)

/**
 * Pairing challenge for secure device registration
 */
data class PairingChallenge(
    val challengeId: UUID,
    val userId: UUID,
    val code: String, // QR code content
    val expiresAt: Long,
    val isUsed: Boolean = false,
    val createdAt: Long = System.currentTimeMillis()
)

/**
 * Storage directory metadata
 */
data class StorageDirectory(
    val id: UUID,
    val uri: String, // Android DocumentTree URI
    val displayName: String,
    val path: String?, // Nullable, SAF may not provide it
    val totalBytes: Long,
    val freeBytes: Long,
    val isAccessible: Boolean = true,
    val selectedAt: Long = System.currentTimeMillis(),
    val lastVerifiedAt: Long = System.currentTimeMillis()
)

/**
 * Represents a file or directory in storage
 */
data class StorageFile(
    val id: UUID,
    val uri: String, // DocumentFile URI
    val name: String,
    val parentUri: String? = null,
    val isDirectory: Boolean = false,
    val sizeBytes: Long = 0,
    val sha256Hash: String? = null,
    val lastModifiedAt: Long = System.currentTimeMillis(),
    val createdAt: Long = System.currentTimeMillis(),
    val isTemp: Boolean = false
)

/**
 * File transfer operation (upload/download)
 */
data class FileTransfer(
    val id: UUID,
    val fileId: UUID,
    val direction: TransferDirection, // UPLOAD or DOWNLOAD
    val sourcePath: String,
    val destinationPath: String,
    val totalBytes: Long,
    val transferredBytes: Long = 0,
    val status: TransferStatus = TransferStatus.PENDING,
    val errorMessage: String? = null,
    val startedAt: Long? = null,
    val completedAt: Long? = null,
    val checksumVerified: Boolean = false,
    val expectedSha256: String? = null
)

enum class TransferDirection {
    UPLOAD, DOWNLOAD
}

enum class TransferStatus {
    PENDING, IN_PROGRESS, COMPLETED, FAILED, CANCELLED
}

/**
 * Represents a paired authorized device
 */
data class AuthorizedDevice(
    val id: UUID,
    val deviceId: UUID,
    val deviceName: String,
    val platform: String,
    val isOnline: Boolean = false,
    val lastSeenAt: Long = System.currentTimeMillis(),
    val permissions: Set<String> = setOf("READ", "WRITE"),
    val revokedAt: Long? = null,
    val pairedAt: Long = System.currentTimeMillis()
)

/**
 * Operation queue item for offline-first sync
 */
data class PendingOperation(
    val id: UUID,
    val operationType: String, // CREATE, UPDATE, DELETE, MOVE
    val targetPath: String,
    val payloadJson: String? = null,
    val status: OperationStatus = OperationStatus.PENDING,
    val retryCount: Int = 0,
    val lastErrorMessage: String? = null,
    val createdAt: Long = System.currentTimeMillis(),
    val executedAt: Long? = null
)

enum class OperationStatus {
    PENDING, IN_PROGRESS, COMPLETED, FAILED_PERMANENTLY
}

/**
 * Server state snapshot
 */
data class ServerState(
    val isRunning: Boolean = false,
    val listeningPort: Int = 8443,
    val localAddress: String? = null,
    val mDnsName: String? = null,
    val isBackendConnected: Boolean = false,
    val lastHealthCheckAt: Long? = null,
    val errorMessage: String? = null
)
