package com.nexsync.storagenode.presentation.viewmodels

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nexsync.storagenode.domain.entities.ServerState
import com.nexsync.storagenode.domain.repositories.ServerStateRepository
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import javax.inject.Inject

@HiltViewModel
class ServerViewModel @Inject constructor(
    private val serverStateRepository: ServerStateRepository
) : ViewModel() {

    private val _serverState: MutableStateFlow<ServerState> = MutableStateFlow(
        ServerState()
    )
    val serverState: StateFlow<ServerState> = _serverState.asStateFlow()

    private val _isLoading: MutableStateFlow<Boolean> = MutableStateFlow(false)
    val isLoading: StateFlow<Boolean> = _isLoading.asStateFlow()

    private val _errorMessage: MutableStateFlow<String?> = MutableStateFlow(null)
    val errorMessage: StateFlow<String?> = _errorMessage.asStateFlow()

    init {
        observeServerState()
    }

    private fun observeServerState() {
        viewModelScope.launch {
            serverStateRepository.observeServerState().collect { state ->
                _serverState.value = state ?: ServerState()
            }
        }
    }

    fun startServer(port: Int = 8443) {
        viewModelScope.launch {
            try {
                _isLoading.value = true
                _errorMessage.value = null
                
                // TODO: Implement actual server start logic
                // This will be implemented in Sprint 3 with Ktor server
                
                serverStateRepository.updateRunningState(true)
                serverStateRepository.setServerInfo(port, "0.0.0.0", "nexsync-node.local")
                
                _isLoading.value = false
            } catch (e: Exception) {
                _errorMessage.value = e.message ?: "Failed to start server"
                _isLoading.value = false
            }
        }
    }

    fun stopServer() {
        viewModelScope.launch {
            try {
                _isLoading.value = true
                _errorMessage.value = null
                
                // TODO: Implement actual server stop logic
                
                serverStateRepository.updateRunningState(false)
                serverStateRepository.setServerError(null)
                
                _isLoading.value = false
            } catch (e: Exception) {
                _errorMessage.value = e.message ?: "Failed to stop server"
                _isLoading.value = false
            }
        }
    }

    fun clearError() {
        _errorMessage.value = null
    }
}
