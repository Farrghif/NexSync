package com.nexsync.storagenode.presentation.viewmodels

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nexsync.storagenode.domain.entities.FileTransfer
import com.nexsync.storagenode.domain.repositories.FileTransferRepository
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import javax.inject.Inject

@HiltViewModel
class TransferViewModel @Inject constructor(
    private val fileTransferRepository: FileTransferRepository
) : ViewModel() {

    private val _recentTransfers: MutableStateFlow<List<FileTransfer>> = MutableStateFlow(emptyList())
    val recentTransfers: StateFlow<List<FileTransfer>> = _recentTransfers.asStateFlow()

    private val _activeTransfers: MutableStateFlow<List<FileTransfer>> = MutableStateFlow(emptyList())
    val activeTransfers: StateFlow<List<FileTransfer>> = _activeTransfers.asStateFlow()

    private val _isLoading: MutableStateFlow<Boolean> = MutableStateFlow(false)
    val isLoading: StateFlow<Boolean> = _isLoading.asStateFlow()

    private val _errorMessage: MutableStateFlow<String?> = MutableStateFlow(null)
    val errorMessage: StateFlow<String?> = _errorMessage.asStateFlow()

    init {
        observeTransfers()
    }

    private fun observeTransfers() {
        viewModelScope.launch {
            try {
                fileTransferRepository.observeRecentTransfers(50).collect { transfers ->
                    _recentTransfers.value = transfers
                }
            } catch (e: Exception) {
                _errorMessage.value = "Failed to observe transfers: ${e.message}"
            }
        }

        viewModelScope.launch {
            try {
                fileTransferRepository.getActiveTransfers()
                // Observe active transfers periodically or via other mechanism
            } catch (e: Exception) {
                _errorMessage.value = "Failed to load active transfers: ${e.message}"
            }
        }
    }

    fun getTransferProgress(transferId: String): Double {
        val transfer = _recentTransfers.value.find { it.id.toString() == transferId }
        return if (transfer != null && transfer.totalBytes > 0) {
            (transfer.transferredBytes.toDouble() / transfer.totalBytes.toDouble()) * 100
        } else {
            0.0
        }
    }

    fun clearError() {
        _errorMessage.value = null
    }
}
