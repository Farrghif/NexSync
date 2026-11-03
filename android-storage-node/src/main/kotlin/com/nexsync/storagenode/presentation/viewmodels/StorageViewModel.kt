package com.nexsync.storagenode.presentation.viewmodels

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nexsync.storagenode.domain.entities.StorageDirectory
import com.nexsync.storagenode.domain.storage.StorageFileInfo
import com.nexsync.storagenode.domain.usecases.CleanupTempFilesUseCase
import com.nexsync.storagenode.domain.usecases.ComputeFileHashUseCase
import com.nexsync.storagenode.domain.usecases.CreateDirectoryUseCase
import com.nexsync.storagenode.domain.usecases.CreateFileUseCase
import com.nexsync.storagenode.domain.usecases.DeleteFileUseCase
import com.nexsync.storagenode.domain.usecases.GetStorageInfoUseCase
import com.nexsync.storagenode.domain.usecases.ListFilesUseCase
import com.nexsync.storagenode.domain.usecases.MoveFileUseCase
import com.nexsync.storagenode.domain.usecases.RenameFileUseCase
import com.nexsync.storagenode.domain.repositories.StorageDirectoryRepository
import com.nexsync.storagenode.domain.storage.StorageInfo
import com.nexsync.storagenode.domain.storage.StorageOperationResult
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import javax.inject.Inject

@HiltViewModel
class StorageViewModel @Inject constructor(
    private val storageDirectoryRepository: StorageDirectoryRepository,
    private val listFilesUseCase: ListFilesUseCase,
    private val createFileUseCase: CreateFileUseCase,
    private val createDirectoryUseCase: CreateDirectoryUseCase,
    private val deleteFileUseCase: DeleteFileUseCase,
    private val renameFileUseCase: RenameFileUseCase,
    private val moveFileUseCase: MoveFileUseCase,
    private val computeFileHashUseCase: ComputeFileHashUseCase,
    private val getStorageInfoUseCase: GetStorageInfoUseCase,
    private val cleanupTempFilesUseCase: CleanupTempFilesUseCase
) : ViewModel() {

    private val _selectedDirectory: MutableStateFlow<StorageDirectory?> = MutableStateFlow(null)
    val selectedDirectory: StateFlow<StorageDirectory?> = _selectedDirectory.asStateFlow()

    private val _files: MutableStateFlow<List<StorageFileInfo>> = MutableStateFlow(emptyList())
    val files: StateFlow<List<StorageFileInfo>> = _files.asStateFlow()

    private val _storageInfo: MutableStateFlow<StorageInfo?> = MutableStateFlow(null)
    val storageInfo: StateFlow<StorageInfo?> = _storageInfo.asStateFlow()

    private val _currentPath: MutableStateFlow<String> = MutableStateFlow("")
    val currentPath: StateFlow<String> = _currentPath.asStateFlow()

    private val _isLoading: MutableStateFlow<Boolean> = MutableStateFlow(false)
    val isLoading: StateFlow<Boolean> = _isLoading.asStateFlow()

    private val _errorMessage: MutableStateFlow<String?> = MutableStateFlow(null)
    val errorMessage: StateFlow<String?> = _errorMessage.asStateFlow()

    init {
        loadPrimaryDirectory()
        refreshStorageInfo()
    }

    private fun loadPrimaryDirectory() {
        viewModelScope.launch {
            try {
                val dir = storageDirectoryRepository.getPrimaryDirectory()
                _selectedDirectory.value = dir
                if (dir != null) {
                    loadFilesFromDirectory(dir.uri)
                }
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
                _currentPath.value = ""
                loadFilesFromDirectory(directory.uri)
                refreshStorageInfo()
                _errorMessage.value = null
                _isLoading.value = false
            } catch (e: Exception) {
                _errorMessage.value = "Failed to select directory: ${e.message}"
                _isLoading.value = false
            }
        }
    }

    fun loadFilesFromDirectory(directoryUri: String = "") {
        viewModelScope.launch {
            try {
                _isLoading.value = true
                val result = listFilesUseCase(directoryUri)
                when (result) {
                    is StorageOperationResult.Success -> {
                        _files.value = result.data
                        _errorMessage.value = null
                    }
                    is StorageOperationResult.Error -> {
                        _errorMessage.value = result.exception.message
                    }
                }
                _isLoading.value = false
            } catch (e: Exception) {
                _errorMessage.value = "Failed to load files: ${e.message}"
                _isLoading.value = false
            }
        }
    }

    fun createFile(fileName: String, parentUri: String = "") {
        viewModelScope.launch {
            try {
                _isLoading.value = true
                val result = createFileUseCase(fileName, parentUri)
                when (result) {
                    is StorageOperationResult.Success -> {
                        _errorMessage.value = null
                        loadFilesFromDirectory(parentUri)
                    }
                    is StorageOperationResult.Error -> {
                        _errorMessage.value = result.exception.message
                    }
                }
                _isLoading.value = false
            } catch (e: Exception) {
                _errorMessage.value = "Failed to create file: ${e.message}"
                _isLoading.value = false
            }
        }
    }

    fun createDirectory(dirName: String, parentUri: String = "") {
        viewModelScope.launch {
            try {
                _isLoading.value = true
                val result = createDirectoryUseCase(dirName, parentUri)
                when (result) {
                    is StorageOperationResult.Success -> {
                        _errorMessage.value = null
                        loadFilesFromDirectory(parentUri)
                    }
                    is StorageOperationResult.Error -> {
                        _errorMessage.value = result.exception.message
                    }
                }
                _isLoading.value = false
            } catch (e: Exception) {
                _errorMessage.value = "Failed to create directory: ${e.message}"
                _isLoading.value = false
            }
        }
    }

    fun deleteFile(fileUri: String, parentUri: String = "") {
        viewModelScope.launch {
            try {
                _isLoading.value = true
                val result = deleteFileUseCase(fileUri)
                when (result) {
                    is StorageOperationResult.Success -> {
                        _errorMessage.value = null
                        loadFilesFromDirectory(parentUri)
                    }
                    is StorageOperationResult.Error -> {
                        _errorMessage.value = result.exception.message
                    }
                }
                _isLoading.value = false
            } catch (e: Exception) {
                _errorMessage.value = "Failed to delete file: ${e.message}"
                _isLoading.value = false
            }
        }
    }

    fun renameFile(fileUri: String, newName: String, parentUri: String = "") {
        viewModelScope.launch {
            try {
                _isLoading.value = true
                val result = renameFileUseCase(fileUri, newName)
                when (result) {
                    is StorageOperationResult.Success -> {
                        _errorMessage.value = null
                        loadFilesFromDirectory(parentUri)
                    }
                    is StorageOperationResult.Error -> {
                        _errorMessage.value = result.exception.message
                    }
                }
                _isLoading.value = false
            } catch (e: Exception) {
                _errorMessage.value = "Failed to rename file: ${e.message}"
                _isLoading.value = false
            }
        }
    }

    fun moveFile(fileUri: String, newParentUri: String, oldParentUri: String = "") {
        viewModelScope.launch {
            try {
                _isLoading.value = true
                val result = moveFileUseCase(fileUri, newParentUri)
                when (result) {
                    is StorageOperationResult.Success -> {
                        _errorMessage.value = null
                        loadFilesFromDirectory(oldParentUri)
                    }
                    is StorageOperationResult.Error -> {
                        _errorMessage.value = result.exception.message
                    }
                }
                _isLoading.value = false
            } catch (e: Exception) {
                _errorMessage.value = "Failed to move file: ${e.message}"
                _isLoading.value = false
            }
        }
    }

    fun computeFileHash(fileUri: String) {
        viewModelScope.launch {
            try {
                _isLoading.value = true
                val result = computeFileHashUseCase(fileUri)
                when (result) {
                    is StorageOperationResult.Success -> {
                        _errorMessage.value = "Hash: ${result.data.take(16)}..."
                    }
                    is StorageOperationResult.Error -> {
                        _errorMessage.value = result.exception.message
                    }
                }
                _isLoading.value = false
            } catch (e: Exception) {
                _errorMessage.value = "Failed to compute hash: ${e.message}"
                _isLoading.value = false
            }
        }
    }

    fun refreshStorageInfo() {
        viewModelScope.launch {
            try {
                val result = getStorageInfoUseCase()
                when (result) {
                    is StorageOperationResult.Success -> {
                        _storageInfo.value = result.data
                    }
                    is StorageOperationResult.Error -> {
                        _storageInfo.value = null
                    }
                }
            } catch (e: Exception) {
                _storageInfo.value = null
            }
        }
    }

    fun cleanupTempFiles() {
        viewModelScope.launch {
            try {
                _isLoading.value = true
                val result = cleanupTempFilesUseCase(olderThanHours = 24)
                when (result) {
                    is StorageOperationResult.Success -> {
                        _errorMessage.value = "Cleaned up ${result.data} temporary files"
                    }
                    is StorageOperationResult.Error -> {
                        _errorMessage.value = result.exception.message
                    }
                }
                _isLoading.value = false
            } catch (e: Exception) {
                _errorMessage.value = "Failed to cleanup: ${e.message}"
                _isLoading.value = false
            }
        }
    }

    fun clearError() {
        _errorMessage.value = null
    }
}
