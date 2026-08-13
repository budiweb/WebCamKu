package id.webcamku.app

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Test

class CameraViewModelTest {
    @Test
    fun `camera starts on back lens`() {
        val state = CameraViewModel().state.value

        assertEquals(CameraFacing.Back, state.facing)
        assertFalse(state.isReady)
    }

    @Test
    fun `switch camera alternates between front and back`() {
        val viewModel = CameraViewModel()

        viewModel.switchCamera()
        assertEquals(CameraFacing.Front, viewModel.state.value.facing)

        viewModel.onCameraReady()
        viewModel.switchCamera()
        assertEquals(CameraFacing.Back, viewModel.state.value.facing)
        assertFalse(viewModel.state.value.isReady)
    }

    @Test
    fun `camera callbacks expose useful status`() {
        val viewModel = CameraViewModel()

        viewModel.onCameraReady()
        assertEquals("Preview active", viewModel.state.value.status)

        viewModel.onCameraError("Selected camera is not available")
        assertEquals("Selected camera is not available", viewModel.state.value.status)
        assertFalse(viewModel.state.value.isReady)
    }
}
