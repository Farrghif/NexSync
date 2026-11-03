package com.nexsync.storagenode.data.db

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase
import androidx.room.migration.Migration
import androidx.sqlite.db.SupportSQLiteDatabase
import com.nexsync.storagenode.data.db.daos.AuthorizedDeviceDao
import com.nexsync.storagenode.data.db.daos.DeviceCredentialsDao
import com.nexsync.storagenode.data.db.daos.FileTransferDao
import com.nexsync.storagenode.data.db.daos.PairingChallengeDao
import com.nexsync.storagenode.data.db.daos.PendingOperationDao
import com.nexsync.storagenode.data.db.daos.ServerStateDao
import com.nexsync.storagenode.data.db.daos.StorageDirectoryDao
import com.nexsync.storagenode.data.db.daos.StorageFileDao
import com.nexsync.storagenode.data.db.daos.StorageNodeDao
import com.nexsync.storagenode.data.db.entities.AuthorizedDeviceEntity
import com.nexsync.storagenode.data.db.entities.DeviceCredentialsEntity
import com.nexsync.storagenode.data.db.entities.FileTransferEntity
import com.nexsync.storagenode.data.db.entities.PairingChallengeEntity
import com.nexsync.storagenode.data.db.entities.PendingOperationEntity
import com.nexsync.storagenode.data.db.entities.ServerStateEntity
import com.nexsync.storagenode.data.db.entities.StorageDirectoryEntity
import com.nexsync.storagenode.data.db.entities.StorageFileEntity
import com.nexsync.storagenode.data.db.entities.StorageNodeEntity

@Database(
    entities = [
        StorageNodeEntity::class,
        DeviceCredentialsEntity::class,
        PairingChallengeEntity::class,
        StorageDirectoryEntity::class,
        StorageFileEntity::class,
        FileTransferEntity::class,
        AuthorizedDeviceEntity::class,
        PendingOperationEntity::class,
        ServerStateEntity::class
    ],
    version = 1,
    exportSchema = true
)
abstract class AppDatabase : RoomDatabase() {
    abstract fun storageNodeDao(): StorageNodeDao
    abstract fun deviceCredentialsDao(): DeviceCredentialsDao
    abstract fun pairingChallengeDao(): PairingChallengeDao
    abstract fun storageDirectoryDao(): StorageDirectoryDao
    abstract fun storageFileDao(): StorageFileDao
    abstract fun fileTransferDao(): FileTransferDao
    abstract fun authorizedDeviceDao(): AuthorizedDeviceDao
    abstract fun pendingOperationDao(): PendingOperationDao
    abstract fun serverStateDao(): ServerStateDao

    companion object {
        private const val DATABASE_NAME = "nexsync_storagenode.db"

        @Volatile
        private var instance: AppDatabase? = null

        fun getInstance(context: Context): AppDatabase {
            return instance ?: synchronized(this) {
                instance ?: Room.databaseBuilder(
                    context.applicationContext,
                    AppDatabase::class.java,
                    DATABASE_NAME
                )
                    .addMigrations(MIGRATION_1_2) // Add future migrations here
                    .fallbackToDestructiveMigration() // Only for development
                    .build()
                    .also { instance = it }
            }
        }

        /**
         * Future migrations should be added here
         * Example:
         * private val MIGRATION_1_2 = object : Migration(1, 2) {
         *     override fun migrate(db: SupportSQLiteDatabase) {
         *         db.execSQL("ALTER TABLE storage_nodes ADD COLUMN new_column TEXT")
         *     }
         * }
         */
        private val MIGRATION_1_2 = object : Migration(1, 2) {
            override fun migrate(db: SupportSQLiteDatabase) {
                // Placeholder for future migrations
            }
        }
    }
}
