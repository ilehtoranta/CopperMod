using CopperMod.Amiga;

namespace CopperScreen;

// Host-level calls only. No per-instruction/device dispatch crosses this boundary.
internal interface ICopperScreenSession : IDisposable
{
    int Width { get; }
    int Height { get; }
    int[] Framebuffer { get; }
    int AudioSampleRate => 44_100;
    double VideoVBlankHz { get; }
    CopperScreenPresentationGeometry PresentationGeometry { get; }
    CopperScreenEmulatorFrameTiming LastFrameTiming { get; }
    bool IsInterlaced { get; }
    int CompletedInterlaceField { get; }
    string ProfileName { get; }
    string DiskName { get; }
    string? DiskPath { get; }
    string BaseDirectory { get; }
    FloppyDriveAudioOptions FloppyDriveAudioOptions { get; }
    CopperScreenCpuState CpuState { get; }
    CopperScreenDebugSnapshot? DebugSnapshot { get; }
    string StatusText { get; }
    string? FaultMessage => null;
    bool IsPaused { get; }
    bool IsWorkbenchHandoffPending { get; }
    bool IsDiskSwapPending { get; }
    bool IsPrimaryFirePressed { get; }
    bool AudioFilterEnabled { get; }
    int AudioFramesPerAppFrame(int sampleRate);
    void RenderNextFrame(int[] destination);
    int RenderAudio(Span<float> destination, int sampleRate, int channels);
    void CaptureDriveStates(Span<CopperScreenDriveState> destination);
    void SetStatusText(string message);
    void CaptureFatalException(Exception exception);
    void Reset();
    bool TogglePaused();
    void PulsePrimaryFire(int frames);
    void KeyDown(AmigaRawKey key);
    void KeyUp(AmigaRawKey key);
    void MoveMousePort(int x, int y);
    void SetMousePortPosition(int x, int y);
    void SetMousePresentationPosition(int x, int y);
    void SetMouseButtons(bool primary, bool secondary);
    void SetJoystickPort(bool up, bool down, bool left, bool right, bool primary, bool secondary);
    void SetJoystickPort(int port, bool up, bool down, bool left, bool right, bool primary, bool secondary);
    void SetInputOptions(CopperScreenInputOptions options);
    void SetPresentationOptions(CopperScreenPresentationOptions options);
    bool InsertLoadedDisk(string path, CopperScreenAdfImage disk, bool markChanged);
    bool InsertLoadedDisk(int drive, string path, CopperScreenAdfImage disk, bool markChanged);
    bool SetDriveWriteProtected(int drive, bool writeProtected);
    bool EjectDisk(int drive) => throw new NotSupportedException("Disk eject is not exposed by this session.");
    bool ConsumeCopperBenchRequest();
    bool LaunchCopperBenchPath(string path, out string message);
    void QueueHostClipboardText(string text);
    void QueueHostClipboardImage(ClipboardImage image);
    bool TryTakeHostClipboardText(out string text);
    bool TryTakeHostClipboardImage(out ClipboardImage? image);
}

internal enum CopperScreenEngine { Legacy, Lightweight }

internal static class CopperScreenSession
{
    public static ICopperScreenSession Create(CopperScreenStartupOptions options)
        => options.Engine switch
        {
            CopperScreenEngine.Legacy => throw new NotSupportedException("Legacy/CopperStart support is not included in this build. Use Lightweight with native Kickstart 1.3; Legacy support will be restored separately."),
            CopperScreenEngine.Lightweight => CreateLightweight(options),
            _ => throw new ArgumentException("--engine must be Legacy or Lightweight.")
        };

    private static ICopperScreenSession CreateLightweight(CopperScreenStartupOptions options)
    {
        try { return new CopperScreenLightweightSession(options); }
        catch (NotSupportedException ex)
        {
            throw new NotSupportedException(ex.Message +
                " No automatic Legacy fallback. Legacy/CopperStart support is not included in this build.", ex);
        }
    }
}
