package com.nexsync.storagenode.presentation.screens

import org.junit.Assert.assertEquals
import org.junit.Test

class FormatBytesTest {

    @Test
    fun `formats small values in bytes`() {
        assertEquals("0 B", formatBytes(0))
        assertEquals("1023 B", formatBytes(1023))
    }

    @Test
    fun `formats kilobytes`() {
        assertEquals("1.0 KB", formatBytes(1024))
        assertEquals("1.5 KB", formatBytes(1536))
    }

    @Test
    fun `formats megabytes and gigabytes`() {
        assertEquals("5.0 MB", formatBytes(5L * 1024 * 1024))
        assertEquals("2.0 GB", formatBytes(2L * 1024 * 1024 * 1024))
    }

    @Test
    fun `does not divide past terabytes`() {
        assertEquals("2.0 TB", formatBytes(2L * 1024 * 1024 * 1024 * 1024))
    }
}
