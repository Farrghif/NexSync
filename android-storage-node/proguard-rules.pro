-keep class com.nexsync.storagenode.** { *; }
-keep class com.nexsync.storagenode.domain.** { *; }
-keep class com.nexsync.storagenode.data.** { *; }

# Hilt DI
-keep class dagger.hilt.** { *; }
-keep class **_HiltModules { *; }
-keep class **_Hilt_* { *; }

# Room Database
-keep class androidx.room.** { *; }
-keep @androidx.room.Entity class * { *; }
-keepclassmembers class * {
    @androidx.room.PrimaryKey <fields>;
    @androidx.room.ColumnInfo <fields>;
    @androidx.room.Embedded <fields>;
    @androidx.room.Relation <fields>;
}

# Jetpack Compose
-keep class androidx.compose.** { *; }

# Retrofit
-keep class retrofit2.** { *; }
-keep interface retrofit2.** { *; }
-keepattributes Signature
-keepattributes *Annotation*

# OkHttp
-keep class okhttp3.** { *; }
-keep interface okhttp3.** { *; }

# Kotlinx Serialization
-keep class kotlinx.serialization.** { *; }

# Ktor
-keep class io.ktor.** { *; }

# Crypto/Security
-keep class org.bouncycastle.** { *; }
-keep class org.conscrypt.** { *; }
-keep class com.google.crypto.tink.** { *; }

# Timber
-dontwarn timber.log.Timber$Tree
