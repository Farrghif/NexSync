package com.nexsync.storagenode.presentation.viewmodels

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nexsync.storagenode.domain.entities.AuthorizedDevice
import com.nexsync.storagenode.domain.entities.StorageNode
import com.nexsync.storagenode.domain.repositories.AuthorizedDeviceRepository
import com.nexsync.storagenode.domain.repositories.StorageNodeRepository
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import javax.inject.Inject

@HiltViewModel
class DeviceViewModel @Inject constructor(
    private val storageNodeRepository: StorageNodeRepository,
    private val authorizedDeviceRepository: AuthorizedDeviceRepository
) : ViewModel() {

    private val _storageNode: MutableStateFlow<StorageNode?> = MutableStateFlow(null)
    val storageNode: StateFlow<StorageNode?> = _storageNode.asStateFlow()

    private val _authorizedDevices: MutableStateFlow<List<AuthorizedDevice>> = MutableStateFlow(emptyList())
    val authorizedDevices: StateFlow<List<AuthorizedDevice>> = _authorizedDevices.asStateFlow()

    private val _isLoading: MutableStateFlow<Boolean> = MutableStateFlow(false)
    val isLoading: StateFlow<Boolean> = _isLoading.asStateFlow()

    private val _errorMessage: MutableStateFlow<String?> = MutableStateFlow(null)
    val errorMessage: StateFlow<String?> = _errorMessage.asStateFlow()

    init {
        observeDevices()
    }

    private fun observeDevices() {
        viewModelScope.launch {
            try {
                // Observe storage node
                storageNodeRepository.observeNodeByUserId("").collect { node ->
                    _storageNode.value = node
                }
            } catch (e: Exception) {
                _errorMessage.value = "Failed to observe storage node: ${e.message}"
            }
        }

        viewModelScope.launch {
            try {
                // Observe authorized devices
                authorizedDeviceRepository.observeActiveDevices().collect { devices ->
                    _authorizedDevices.value = devices
                }
            } catch (e: Exception) {
                _errorMessage.value = "Failed to observe authorized devices: ${e.message}"
            }
        }
    }

    fun revokeDevice(deviceId: String) {
        viewModelScope.launch {
            try {
                _isLoading.value = true
                val device = authorizedDeviceRepository.getDeviceByDeviceId(deviceId)
                if (device != null) {
                    authorizedDeviceRepository.revokeDevice(device.id.toString())
                    _errorMessage.value = null
                }
                _isLoading.value = false
            } catch (e: Exception) {
                _errorMessage.value = "Failed to revoke device: ${e.message}"
                _isLoading.value = false
            }
        }
    }

    fun clearError() {
        _errorMessage.value = null
    }
}
