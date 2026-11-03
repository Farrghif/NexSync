package com.nexsync.storagenode.data.repositories

import com.nexsync.storagenode.data.db.daos.ServerStateDao
import com.nexsync.storagenode.data.db.entities.ServerStateEntity
import com.nexsync.storagenode.domain.entities.ServerState
import com.nexsync.storagenode.domain.repositories.ServerStateRepository
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map
import javax.inject.Inject

class ServerStateRepositoryImpl @Inject constructor(
    private val dao: ServerStateDao
) : ServerStateRepository {

    override suspend fun getServerState(): ServerState? {
        return dao.getState()?.toDomain()
    }

    override fun observeServerState(): Flow<ServerState?> {
        return dao.observeState().map { it?.toDomain() }
    }

    override suspend fun updateRunningState(isRunning: Boolean) {
        dao.updateRunningState(isRunning, System.currentTimeMillis())
    }

    override suspend fun setServerInfo(port: Int, address: String?, mDnsName: String?) {
        val currentState = dao.getState()
        val newState = if (currentState != null) {
            currentState.copy(
                listeningPort = port,
                localAddress = address,
                mDnsName = mDnsName,
                updatedAt = System.currentTimeMillis()
            )
        } else {
            ServerStateEntity(
                id = 1,
                isRunning = false,
                listeningPort = port,
                localAddress = address,
                mDnsName = mDnsName,
                isBackendConnected = false,
                lastHealthCheckAt = null,
                errorMessage = null,
                updatedAt = System.currentTimeMillis()
            )
        }
        dao.insert(newState)
    }

    override suspend fun setBackendConnected(isConnected: Boolean) {
        val currentState = dao.getState()
        val newState = if (currentState != null) {
            currentState.copy(
                isBackendConnected = isConnected,
                updatedAt = System.currentTimeMillis()
            )
        } else {
            ServerStateEntity(
                id = 1,
                isRunning = false,
                listeningPort = 8443,
                localAddress = null,
                mDnsName = null,
                isBackendConnected = isConnected,
                lastHealthCheckAt = System.currentTimeMillis(),
                errorMessage = null,
                updatedAt = System.currentTimeMillis()
            )
        }
        dao.insert(newState)
    }

    override suspend fun setServerError(errorMessage: String?) {
        val currentState = dao.getState()
        val newState = if (currentState != null) {
            currentState.copy(
                errorMessage = errorMessage,
                updatedAt = System.currentTimeMillis()
            )
        } else {
            ServerStateEntity(
                id = 1,
                isRunning = false,
                listeningPort = 8443,
                localAddress = null,
                mDnsName = null,
                isBackendConnected = false,
                lastHealthCheckAt = null,
                errorMessage = errorMessage,
                updatedAt = System.currentTimeMillis()
            )
        }
        dao.insert(newState)
    }

    private fun ServerStateEntity.toDomain(): ServerState {
        return ServerState(
            isRunning = this.isRunning,
            listeningPort = this.listeningPort,
            localAddress = this.localAddress,
            mDnsName = this.mDnsName,
            isBackendConnected = this.isBackendConnected,
            lastHealthCheckAt = this.lastHealthCheckAt,
            errorMessage = this.errorMessage
        )
    }
}
