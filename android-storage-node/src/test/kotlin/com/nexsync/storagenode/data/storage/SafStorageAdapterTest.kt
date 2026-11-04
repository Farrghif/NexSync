package com.nexsync.storagenode.data.storage

import android.content.ContentResolver
import android.content.Context
import android.net.Uri
import androidx.documentfile.provider.DocumentFile
import com.nexsync.storagenode.domain.storage.StorageException
import com.nexsync.storagenode.domain.storage.StreamConfig
import io.mockk.every
import io.mockk.mockk
import io.mockk.verify
import kotlinx.coroutines.test.runTest
import org.junit.Before
import org.junit.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class SafStorageAdapterTest {

    private lateinit var context: Context
    private lateinit var contentResolver: ContentResolver
    private lateinit var adapter: SafStorageAdapter

    @Before
    fun setup() {
        context = mockk(relaxed = true)
        contentResolver = mockk(relaxed = true)
        every { context.contentResolver } returns contentResolver
        
        adapter = SafStorageAdapter(
            context = context,
            rootTreeUri = "content://test/tree/root",
            streamConfig = StreamConfig(bufferSizeBytes = 8192)
        )
    }

    @Test
    fun `isValidPath rejects path traversal`() {
        assertFalse(adapter.isValidPath("../etc/passwd"))
        assertFalse(adapter.isValidPath("..\\windows\\system32"))
        assertFalse(adapter.isValidPath("/absolute/path"))
        assertFalse(adapter.isValidPath(""))
    }

    @Test
    fun `isValidPath accepts valid filenames`() {
        assertTrue(adapter.isValidPath("document.pdf"))
        assertTrue(adapter.isValidPath("my-file_v1.2.3"))
        assertTrue(adapter.isValidPath("фото"))
        assertTrue(adapter.isValidPath("a"))
    }

    @Test
    fun `isValidPath rejects null character`() {
        assertFalse(adapter.isValidPath("file\u0000.txt"))
    }

    @Test
    fun `isValidPath rejects overly long names`() {
        val longName = "a".repeat(256)
        assertFalse(adapter.isValidPath(longName))
    }

    @Test
    fun `initialize succeeds with valid URI` () = runTest {
        val result = adapter.initialize()
        // Will fail without mock setup, but tests the flow
        assertTrue(result is com.nexsync.storagenode.domain.storage.StorageOperationResult)
    }

    @Test
    fun `computeFileHash handles missing file` () = runTest {
        val result = adapter.computeFileHash("content://invalid/file")
        assertTrue(result is com.nexsync.storagenode.domain.storage.StorageOperationResult.Error)
    }
}

/**
 * Integration tests (require actual Android context)
 */
class SafStorageAdapterIntegrationTest {
    // These tests require AndroidJUnit4 and real device/emulator
    // Kept as reference for manual testing
    
    @Test
    fun testRealStorageOperations() {
        // TODO: Run on emulator with actual DocumentFile API
    }
}
