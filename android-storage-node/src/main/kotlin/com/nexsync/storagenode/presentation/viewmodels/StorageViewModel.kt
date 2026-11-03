package com.nexsync.storagenode.presentation.viewmodels

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nexsync.storagenode.domain.entities.StorageDirectory
import com.nexsync.storagenode.domain.repositories.StorageDirectoryRepository
import com.nexsync.storagenode.domain.repositories.StorageFileRepository
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import javax.inject.Inject

@HiltViewModel
class StorageViewModel @Inject constructor(
    private val storageDirectoryRepository: StorageDirectoryRepository,
    private val storageFileRepository: StorageFileRepository
) : ViewModel() {

    private val _selectedDirectory: MutableStateFlow<StorageDirectory?> = MutableStateFlow(null)
    val selectedDirectory: StateFlow<StorageDirectory?> = _selectedDirectory.asStateFlow()

    private val _isLoading: MutableStateFlow<Boolean> = MutableStateFlow(false)
    val isLoading: StateFlow<Boolean> = _isLoading.asStateFlow()

    private val _errorMessage: MutableStateFlow<String?> = MutableStateFlow(null)
    val errorMessage: StateFlow<String?> = _errorMessage.asStateFlow()

    init {
        loadPrimaryDirectory()
    }

    private fun loadPrimaryDirectory() {
        viewModelScope.launch {
            try {
                val dir = storageDirectoryRepository.getPrimaryDirectory()
                _selectedDirectory.value = dir
            } catch (e: Exception) {
                _errorMessage.value = "Failed to load storage directory: ${e.message}"
            }
        }
    }

    fun selectStorageDirectory(directory: StorageDirectory) {
        viewModelScope.launch {
            try {
                _isLoading.value = true
                storageDirectoryRepository.saveDirectory(directory)
                _selectedDirectory.value = directory
                _errorMessage.value = null
                _isLoading.value = false
            } catch (e: Exception) {
                _errorMessage.value = "Failed to select directory: ${e.message}"
                _isLoading.value = false
            }
        }
    }

    fun verifyDirectoryAccessibility(directory: StorageDirectory) {
        viewModelScope.launch {
            try {
                // TODO: Implement actual SAF verification
                storageDirectoryRepository.updateAccessibility(directory.id.toString(), true)
            } catch (e: Exception) {
                _errorMessage.value = "Directory access verification failed: ${e.message}"
                storageDirectoryRepository.updateAccessibility(directory.id.toString(), false)
            }
        }
    }

    fun clearError() {
        _errorMessage.value = null
    }
}
