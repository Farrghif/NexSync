package com.nexsync.storagenode.presentation.screens

import android.content.Intent
import android.net.Uri
import android.text.format.DateUtils
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.navigation.NavController
import com.nexsync.storagenode.domain.entities.AuthorizedDevice
import com.nexsync.storagenode.domain.entities.FileTransfer
import com.nexsync.storagenode.domain.entities.ServerState
import com.nexsync.storagenode.domain.entities.StorageDirectory
import com.nexsync.storagenode.domain.entities.TransferDirection
import com.nexsync.storagenode.domain.entities.TransferStatus
import com.nexsync.storagenode.domain.storage.StorageInfo
import com.nexsync.storagenode.presentation.viewmodels.DeviceViewModel
import com.nexsync.storagenode.presentation.viewmodels.ServerViewModel
import com.nexsync.storagenode.presentation.viewmodels.StorageViewModel
import com.nexsync.storagenode.presentation.viewmodels.TransferViewModel
import java.util.UUID

private val StatusGreen = Color(0xFF16A34A)
private val StatusGray = Color(0xFF8A8F9C)

fun formatBytes(bytes: Long): String {
    if (bytes < 1024) return "$bytes B"
    val units = arrayOf("KB", "MB", "GB", "TB")
    var value = bytes.toDouble() / 1024
    var index = 0
    while (value >= 1024 && index < units.lastIndex) {
        value /= 1024
        index++
    }
    return String.format(java.util.Locale.ROOT, "%.1f %s", value, units[index])
}

private fun pathFileName(path: String): String =
    path.substringAfterLast('/').ifBlank { path.ifBlank { "file" } }

private fun relativeTime(ms: Long): String =
    DateUtils.getRelativeTimeSpanString(ms, System.currentTimeMillis(), DateUtils.MINUTE_IN_MILLIS).toString()

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DashboardScreen(navController: NavController) {
    val serverViewModel: ServerViewModel = hiltViewModel()
    val storageViewModel: StorageViewModel = hiltViewModel()
    val deviceViewModel: DeviceViewModel = hiltViewModel()
    val transferViewModel: TransferViewModel = hiltViewModel()

    val serverState by serverViewModel.serverState.collectAsState()
    val serverLoading by serverViewModel.isLoading.collectAsState()
    val serverError by serverViewModel.errorMessage.collectAsState()
    val storageInfo by storageViewModel.storageInfo.collectAsState()
    val primaryDirectory by storageViewModel.selectedDirectory.collectAsState()
    val storageError by storageViewModel.errorMessage.collectAsState()
    val devices by deviceViewModel.authorizedDevices.collectAsState()
    val deviceError by deviceViewModel.errorMessage.collectAsState()
    val transfers by transferViewModel.recentTransfers.collectAsState()
    val transferError by transferViewModel.errorMessage.collectAsState()

    val context = LocalContext.current
    val directoryPicker = rememberLauncherForActivityResult(
        ActivityResultContracts.OpenDocumentTree()
    ) { uri: Uri? ->
        if (uri != null) {
            context.contentResolver.takePersistableUriPermission(
                uri,
                Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION
            )
            storageViewModel.selectStorageDirectory(
                StorageDirectory(
                    id = UUID.randomUUID(),
                    uri = uri.toString(),
                    displayName = folderName(uri),
                    path = null,
                    totalBytes = 0,
                    freeBytes = 0
                )
            )
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = {
                    Text(
                        text = "Storage Node",
                        fontWeight = FontWeight.SemiBold
                    )
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = MaterialTheme.colorScheme.background,
                    titleContentColor = MaterialTheme.colorScheme.onBackground
                )
            )
        }
    ) { paddingValues ->
        LazyColumn(
            modifier = Modifier
                .fillMaxSize()
                .padding(paddingValues),
            contentPadding = PaddingValues(start = 16.dp, end = 16.dp, bottom = 24.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            item {
                ServerSection(
                    state = serverState,
                    loading = serverLoading,
                    error = serverError,
                    onStart = { serverViewModel.startServer() },
                    onStop = { serverViewModel.stopServer() },
                    onDismissError = { serverViewModel.clearError() }
                )
            }
            item {
                StorageSection(
                    directory = primaryDirectory,
                    info = storageInfo,
                    error = storageError,
                    onPickDirectory = { directoryPicker.launch(null) },
                    onDismissError = { storageViewModel.clearError() }
                )
            }
            item {
                DevicesSection(
                    devices = devices,
                    error = deviceError,
                    onDismissError = { deviceViewModel.clearError() }
                )
            }
            item {
                TransfersSection(
                    transfers = transfers,
                    error = transferError,
                    onDismissError = { transferViewModel.clearError() }
                )
            }
        }
    }
}

private fun folderName(uri: Uri): String =
    uri.lastPathSegment
        ?.substringAfterLast('/')
        ?.substringAfterLast(':')
        ?.takeIf { it.isNotBlank() }
        ?: "Storage folder"

// Shared card shell: icon + title + optional trailing content, then section body.
@Composable
private fun SectionCard(
    title: String,
    icon: ImageVector,
    trailing: @Composable (() -> Unit)? = null,
    content: @Composable ColumnScope.() -> Unit
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(16.dp),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface)
    ) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                Box(
                    modifier = Modifier
                        .size(34.dp)
                        .background(
                            MaterialTheme.colorScheme.primaryContainer,
                            RoundedCornerShape(10.dp)
                        ),
                    contentAlignment = Alignment.Center
                ) {
                    Icon(
                        imageVector = icon,
                        contentDescription = null,
                        tint = MaterialTheme.colorScheme.onPrimaryContainer,
                        modifier = Modifier.size(18.dp)
                    )
                }
                Text(
                    text = title,
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.SemiBold,
                    color = MaterialTheme.colorScheme.onSurface
                )
                Spacer(modifier = Modifier.weight(1f))
                trailing?.invoke()
            }
            content()
        }
    }
}

@Composable
private fun StatusPill(text: String, color: Color) {
    Row(
        modifier = Modifier
            .background(color.copy(alpha = 0.12f), RoundedCornerShape(50))
            .padding(horizontal = 10.dp, vertical = 4.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(6.dp)
    ) {
        Box(
            modifier = Modifier
                .size(7.dp)
                .background(color, CircleShape)
        )
        Text(
            text = text,
            fontSize = 11.sp,
            fontWeight = FontWeight.SemiBold,
            color = color
        )
    }
}

@Composable
private fun CardError(message: String, onDismiss: () -> Unit) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .background(
                MaterialTheme.colorScheme.error.copy(alpha = 0.1f),
                RoundedCornerShape(10.dp)
            )
            .padding(horizontal = 10.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        Icon(
            imageVector = Icons.Filled.Error,
            contentDescription = null,
            tint = MaterialTheme.colorScheme.error,
            modifier = Modifier.size(16.dp)
        )
        Text(
            text = message,
            style = MaterialTheme.typography.labelMedium,
            color = MaterialTheme.colorScheme.error,
            modifier = Modifier.weight(1f)
        )
        Icon(
            imageVector = Icons.Filled.Close,
            contentDescription = "Dismiss error",
            tint = MaterialTheme.colorScheme.error,
            modifier = Modifier
                .size(16.dp)
                .clickable(onClick = onDismiss)
        )
    }
}

@Composable
private fun EmptyState(icon: ImageVector, title: String, subtitle: String) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(vertical = 8.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(6.dp)
    ) {
        Icon(
            imageVector = icon,
            contentDescription = null,
            tint = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.size(30.dp)
        )
        Text(
            text = title,
            style = MaterialTheme.typography.titleSmall,
            fontWeight = FontWeight.SemiBold,
            color = MaterialTheme.colorScheme.onSurface
        )
        Text(
            text = subtitle,
            style = MaterialTheme.typography.labelMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            textAlign = androidx.compose.ui.text.style.TextAlign.Center
        )
    }
}

@Composable
private fun ServerSection(
    state: ServerState,
    loading: Boolean,
    error: String?,
    onStart: () -> Unit,
    onStop: () -> Unit,
    onDismissError: () -> Unit
) {
    SectionCard(
        title = "Server",
        icon = Icons.Filled.Dns,
        trailing = {
            if (state.isRunning) {
                StatusPill("Running", StatusGreen)
            } else {
                StatusPill("Stopped", StatusGray)
            }
        }
    ) {
        if (state.isRunning) {
            Text(
                text = "${state.mDnsName ?: state.localAddress ?: "0.0.0.0"}:${state.listeningPort}",
                style = MaterialTheme.typography.bodyMedium,
                fontFamily = FontFamily.Monospace,
                color = MaterialTheme.colorScheme.onSurface
            )
            Text(
                text = "Paired devices can reach this phone on your local network.",
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        } else {
            Text(
                text = "The server is offline. Start it to let paired devices connect.",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }

        if (state.isRunning && state.errorMessage != null) {
            Text(
                text = "Warning: ${state.errorMessage}",
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.error
            )
        }

        val buttonContainerColor: Color
        val buttonContentColor: Color
        if (state.isRunning) {
            buttonContainerColor = MaterialTheme.colorScheme.surfaceVariant
            buttonContentColor = MaterialTheme.colorScheme.onSurfaceVariant
        } else {
            buttonContainerColor = MaterialTheme.colorScheme.primary
            buttonContentColor = MaterialTheme.colorScheme.onPrimary
        }
        Button(
            onClick = { if (state.isRunning) onStop() else onStart() },
            enabled = !loading,
            modifier = Modifier
                .fillMaxWidth()
                .height(46.dp),
            shape = RoundedCornerShape(12.dp),
            colors = ButtonDefaults.buttonColors(
                containerColor = buttonContainerColor,
                contentColor = buttonContentColor
            )
        ) {
            if (loading) {
                CircularProgressIndicator(
                    modifier = Modifier.size(18.dp),
                    color = buttonContentColor,
                    strokeWidth = 2.dp
                )
            } else {
                Icon(
                    imageVector = if (state.isRunning) Icons.Filled.Stop else Icons.Filled.PlayArrow,
                    contentDescription = null,
                    modifier = Modifier.size(18.dp)
                )
                Spacer(modifier = Modifier.size(8.dp))
                Text(
                    text = if (state.isRunning) "Stop Server" else "Start Server",
                    style = MaterialTheme.typography.titleSmall,
                    fontWeight = FontWeight.SemiBold
                )
            }
        }

        if (error != null) {
            CardError(message = error, onDismiss = onDismissError)
        }
    }
}

@Composable
private fun StorageSection(
    directory: StorageDirectory?,
    info: StorageInfo?,
    error: String?,
    onPickDirectory: () -> Unit,
    onDismissError: () -> Unit
) {
    SectionCard(title = "Storage", icon = Icons.Filled.Folder) {
        if (directory == null) {
            Text(
                text = "No folder selected yet",
                style = MaterialTheme.typography.titleSmall,
                fontWeight = FontWeight.SemiBold,
                color = MaterialTheme.colorScheme.onSurface
            )
            Text(
                text = "Pick a folder on this phone to use as your private storage.",
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            Button(
                onClick = onPickDirectory,
                modifier = Modifier
                    .fillMaxWidth()
                    .height(44.dp),
                shape = RoundedCornerShape(12.dp)
            ) {
                Icon(
                    imageVector = Icons.Filled.FolderOpen,
                    contentDescription = null,
                    modifier = Modifier.size(18.dp)
                )
                Spacer(modifier = Modifier.size(8.dp))
                Text(
                    text = "Select Storage Folder",
                    style = MaterialTheme.typography.titleSmall,
                    fontWeight = FontWeight.SemiBold
                )
            }
        } else {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = directory.displayName,
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = FontWeight.SemiBold,
                        color = MaterialTheme.colorScheme.onSurface,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis
                    )
                    if (!directory.isAccessible) {
                        Text(
                            text = "Folder is no longer accessible",
                            style = MaterialTheme.typography.labelMedium,
                            color = MaterialTheme.colorScheme.error
                        )
                    }
                }
                TextButton(onClick = onPickDirectory) {
                    Text(text = "Change", fontWeight = FontWeight.SemiBold)
                }
            }

            if (info != null && info.totalBytes > 0) {
                LinearProgressIndicator(
                    progress = { (info.usagePercentage / 100).toFloat().coerceIn(0f, 1f) },
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(8.dp)
                )
                Row(modifier = Modifier.fillMaxWidth()) {
                    Text(
                        text = "${formatBytes(info.usedBytes)} used",
                        style = MaterialTheme.typography.labelMedium,
                        fontWeight = FontWeight.SemiBold,
                        color = MaterialTheme.colorScheme.onSurface,
                        modifier = Modifier.weight(1f)
                    )
                    Text(
                        text = "${formatBytes(info.freeBytes)} free",
                        style = MaterialTheme.typography.labelMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
            } else {
                Text(
                    text = "Storage usage unavailable.",
                    style = MaterialTheme.typography.labelMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
        }

        if (error != null) {
            CardError(message = error, onDismiss = onDismissError)
        }
    }
}

@Composable
private fun DevicesSection(
    devices: List<AuthorizedDevice>,
    error: String?,
    onDismissError: () -> Unit
) {
    val onlineCount = devices.count { it.isOnline }
    SectionCard(
        title = "Devices",
        icon = Icons.Filled.Devices,
        trailing = {
            if (devices.isNotEmpty()) {
                if (onlineCount > 0) {
                    StatusPill("$onlineCount online", StatusGreen)
                } else {
                    StatusPill("${devices.size} paired", StatusGray)
                }
            }
        }
    ) {
        if (devices.isEmpty()) {
            EmptyState(
                icon = Icons.Filled.Devices,
                title = "No devices paired yet",
                subtitle = "Pair a device to start syncing files with this phone."
            )
        } else {
            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                devices.forEach { device ->
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(10.dp)
                    ) {
                        Box(
                            modifier = Modifier
                                .size(9.dp)
                                .background(
                                    if (device.isOnline) StatusGreen else StatusGray,
                                    CircleShape
                                )
                        )
                        Column(modifier = Modifier.weight(1f)) {
                            Text(
                                text = device.deviceName,
                                style = MaterialTheme.typography.bodyMedium,
                                fontWeight = FontWeight.SemiBold,
                                color = MaterialTheme.colorScheme.onSurface,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis
                            )
                            Text(
                                text = "${device.platform} · Last seen ${relativeTime(device.lastSeenAt)}",
                                style = MaterialTheme.typography.labelMedium,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis
                            )
                        }
                        Text(
                            text = if (device.isOnline) "Online" else "Offline",
                            style = MaterialTheme.typography.labelSmall,
                            fontWeight = FontWeight.SemiBold,
                            color = if (device.isOnline) StatusGreen
                            else MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                }
            }
        }

        if (error != null) {
            CardError(message = error, onDismiss = onDismissError)
        }
    }
}

private fun transferStatusLabel(status: TransferStatus): String = when (status) {
    TransferStatus.PENDING -> "Queued"
    TransferStatus.IN_PROGRESS -> "Syncing"
    TransferStatus.COMPLETED -> "Done"
    TransferStatus.FAILED -> "Failed"
    TransferStatus.CANCELLED -> "Cancelled"
}

@Composable
private fun transferStatusColor(status: TransferStatus): Color = when (status) {
    TransferStatus.COMPLETED -> StatusGreen
    TransferStatus.FAILED -> MaterialTheme.colorScheme.error
    TransferStatus.IN_PROGRESS -> MaterialTheme.colorScheme.primary
    TransferStatus.PENDING, TransferStatus.CANCELLED -> StatusGray
}

@Composable
private fun TransfersSection(
    transfers: List<FileTransfer>,
    error: String?,
    onDismissError: () -> Unit
) {
    SectionCard(title = "Transfer Activity", icon = Icons.Filled.ImportExport) {
        if (transfers.isEmpty()) {
            EmptyState(
                icon = Icons.Filled.SwapVert,
                title = "No transfers yet",
                subtitle = "File transfers from paired devices will show up here."
            )
        } else {
            Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                transfers.take(5).forEach { transfer ->
                    val isUpload = transfer.direction == TransferDirection.UPLOAD
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(10.dp)
                    ) {
                        Box(
                            modifier = Modifier
                                .size(34.dp)
                                .background(
                                    if (isUpload) MaterialTheme.colorScheme.primaryContainer
                                    else MaterialTheme.colorScheme.tertiaryContainer,
                                    CircleShape
                                ),
                            contentAlignment = Alignment.Center
                        ) {
                            Icon(
                                imageVector = if (isUpload) Icons.Filled.ArrowUpward
                                else Icons.Filled.ArrowDownward,
                                contentDescription = if (isUpload) "Upload" else "Download",
                                tint = if (isUpload) MaterialTheme.colorScheme.onPrimaryContainer
                                else MaterialTheme.colorScheme.onTertiaryContainer,
                                modifier = Modifier.size(17.dp)
                            )
                        }
                        Column(modifier = Modifier.weight(1f)) {
                            Text(
                                text = pathFileName(
                                    transfer.destinationPath.ifBlank { transfer.sourcePath }
                                ),
                                style = MaterialTheme.typography.bodyMedium,
                                fontWeight = FontWeight.SemiBold,
                                color = MaterialTheme.colorScheme.onSurface,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis
                            )
                            val subtitle = when (transfer.status) {
                                TransferStatus.IN_PROGRESS ->
                                    "${formatBytes(transfer.transferredBytes)} of ${formatBytes(transfer.totalBytes)}"
                                TransferStatus.COMPLETED ->
                                    if (transfer.checksumVerified) "${formatBytes(transfer.totalBytes)} · verified"
                                    else formatBytes(transfer.totalBytes)
                                TransferStatus.FAILED ->
                                    transfer.errorMessage ?: "Transfer failed"
                                else -> "${formatBytes(transfer.totalBytes)}"
                            }
                            Text(
                                text = subtitle,
                                style = MaterialTheme.typography.labelMedium,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis
                            )
                        }
                        StatusPill(
                            text = transferStatusLabel(transfer.status),
                            color = transferStatusColor(transfer.status)
                        )
                    }
                    if (transfer.status == TransferStatus.IN_PROGRESS && transfer.totalBytes > 0) {
                        LinearProgressIndicator(
                            progress = {
                                (transfer.transferredBytes.toFloat() / transfer.totalBytes.toFloat())
                                    .coerceIn(0f, 1f)
                            },
                            modifier = Modifier
                                .fillMaxWidth()
                                .height(6.dp)
                        )
                    }
                }
            }
        }

        if (error != null) {
            CardError(message = error, onDismiss = onDismissError)
        }
    }
}
