package com.nexsync.storagenode.di

import android.content.Context
import android.content.SharedPreferences
import androidx.preference.PreferenceManager
import com.nexsync.storagenode.data.db.AppDatabase
import com.nexsync.storagenode.data.repositories.AuthorizedDeviceRepositoryImpl
import com.nexsync.storagenode.data.repositories.DeviceCredentialsRepositoryImpl
import com.nexsync.storagenode.data.repositories.FileTransferRepositoryImpl
import com.nexsync.storagenode.data.repositories.PairingChallengeRepositoryImpl
import com.nexsync.storagenode.data.repositories.PendingOperationRepositoryImpl
import com.nexsync.storagenode.data.repositories.ServerStateRepositoryImpl
import com.nexsync.storagenode.data.repositories.StorageDirectoryRepositoryImpl
import com.nexsync.storagenode.data.repositories.StorageFileRepositoryImpl
import com.nexsync.storagenode.data.repositories.StorageNodeRepositoryImpl
import com.nexsync.storagenode.data.storage.SafStorageAdapter
import com.nexsync.storagenode.domain.repositories.AuthorizedDeviceRepository
import com.nexsync.storagenode.domain.repositories.DeviceCredentialsRepository
import com.nexsync.storagenode.domain.repositories.FileTransferRepository
import com.nexsync.storagenode.domain.repositories.PairingChallengeRepository
import com.nexsync.storagenode.domain.repositories.PendingOperationRepository
import com.nexsync.storagenode.domain.repositories.ServerStateRepository
import com.nexsync.storagenode.domain.repositories.StorageDirectoryRepository
import com.nexsync.storagenode.domain.repositories.StorageFileRepository
import com.nexsync.storagenode.domain.repositories.StorageNodeRepository
import com.nexsync.storagenode.domain.storage.StorageAdapter
import com.nexsync.storagenode.domain.storage.StreamConfig
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import javax.inject.Singleton

@Module
@InstallIn(SingletonComponent::class)
object DatabaseModule {

    
    @Provides
    @Singleton
    fun provideAppDatabase(
        @ApplicationContext context: Context
    ): AppDatabase {
        return AppDatabase.getInstance(context)
    }
}

@Module
@InstallIn(SingletonComponent::class)
object RepositoryModule {

    @Provides
    @Singleton
    fun provideStorageNodeRepository(
        db: AppDatabase
    ): StorageNodeRepository {
        return StorageNodeRepositoryImpl(db.storageNodeDao())
    }

    @Provides
    @Singleton
    fun provideDeviceCredentialsRepository(
        db: AppDatabase
    ): DeviceCredentialsRepository {
        return DeviceCredentialsRepositoryImpl(db.deviceCredentialsDao())
    }

    @Provides
    @Singleton
    fun providePairingChallengeRepository(
        db: AppDatabase
    ): PairingChallengeRepository {
        return PairingChallengeRepositoryImpl(db.pairingChallengeDao())
    }

    @Provides
    @Singleton
    fun provideStorageDirectoryRepository(
        db: AppDatabase
    ): StorageDirectoryRepository {
        return StorageDirectoryRepositoryImpl(db.storageDirectoryDao())
    }

    @Provides
    @Singleton
    fun provideStorageFileRepository(
        db: AppDatabase
    ): StorageFileRepository {
        return StorageFileRepositoryImpl(db.storageFileDao())
    }

    @Provides
    @Singleton
    fun provideFileTransferRepository(
        db: AppDatabase
    ): FileTransferRepository {
        return FileTransferRepositoryImpl(db.fileTransferDao())
    }

    @Provides
    @Singleton
    fun provideAuthorizedDeviceRepository(
        db: AppDatabase
    ): AuthorizedDeviceRepository {
        return AuthorizedDeviceRepositoryImpl(db.authorizedDeviceDao())
    }

    @Provides
    @Singleton
    fun providePendingOperationRepository(
        db: AppDatabase
    ): PendingOperationRepository {
        return PendingOperationRepositoryImpl(db.pendingOperationDao())
    }

    @Provides
    @Singleton
    fun provideServerStateRepository(
        db: AppDatabase
    ): ServerStateRepository {
        return ServerStateRepositoryImpl(db.serverStateDao())
    }
}

@Module
@InstallIn(SingletonComponent::class)
object StorageModule {

    private const val PREF_ROOT_TREE_URI = "root_tree_uri"
    private const val DEFAULT_ROOT_TREE_URI = "content://com.android.providers.media.documents/tree/primary%3A"

    @Provides
    @Singleton
    fun provideStreamConfig(): StreamConfig {
        return StreamConfig(
            bufferSizeBytes = 65536, // 64 KB
            maxConcurrentReads = 3,
            readTimeoutMs = 30000L,
            writeTimeoutMs = 30000L
        )
    }

    @Provides
    @Singleton
    fun provideSharedPreferences(@ApplicationContext context: Context): SharedPreferences {
        return PreferenceManager.getDefaultSharedPreferences(context)
    }

    @Provides
    @Singleton
    fun provideRootTreeUri(prefs: SharedPreferences): String {
        return prefs.getString(PREF_ROOT_TREE_URI, DEFAULT_ROOT_TREE_URI) ?: DEFAULT_ROOT_TREE_URI
    }

    @Provides
    @Singleton
    fun provideStorageAdapter(
        @ApplicationContext context: Context,
        rootTreeUri: String,
        streamConfig: StreamConfig
    ): StorageAdapter {
        // TODO: In a future sprint, persist rootTreeUri via pairing UI / preferences
        // For now, using provided value (default or previously persisted)
        return SafStorageAdapter(
            context = context,
            rootTreeUri = rootTreeUri,
            streamConfig = streamConfig
        )
    }
}