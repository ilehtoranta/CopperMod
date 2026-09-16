
using CopperMod.Amiga.Lightweight;

namespace CopperScreen;

// One owner, one machine. Host commands run on CopperScreenRuntime's thread.
internal sealed class CopperScreenLightweightSession : ICopperScreenSession
{
    private readonly LightweightA500Machine _machine;
    private CopperScreenInputOptions _inputOptions;
    private byte _mouseButtons, _joystick0, _joystick1;
    private int _fireFrames;
    private bool _haveFrame, _audioAvailable, _faulted;
    private byte[]? _pendingAdf;
    private int _insertDelay;

    public CopperScreenLightweightSession(CopperScreenStartupOptions options)
    {
        Validate(options);
        // Media decoding is mount-time only; none of the old execution machinery is created.
        var rom = CopperScreenKickstartRomArchive.ReadRomImage(options.KickstartRomPath!,
            options.Profile.KickstartSource, options.Profile.KickstartVersion);
        if (rom.Length != 262144 || rom[12] != 0 || rom[13] != 34)
            throw new NotSupportedException("Lightweight requires a native 256 KiB Kickstart 1.3 (v34) ROM.");
        var diskPath = options.DriveDiskPaths[0];
        var adf = diskPath is null ? null : GetAdf(CopperScreenDiskImageArchive.LoadDiskImage(diskPath));
        BaseDirectory = options.BaseDirectory;
        ProfileName = options.Profile.DisplayName + " [Lightweight]";
        FloppyDriveAudioOptions = options.FloppyDriveAudio;
        _inputOptions = options.Input;
        _machine = new(new LightweightA500Configuration { FramebufferWidth = 908 });
        try
        {
            _machine.LoadKickstart(rom);
            if (adf != null) _machine.MountAdf(adf);
            DiskPath = diskPath;
            ResetHostState();
        }
        catch { _machine.Dispose(); throw; }
    }

    internal static void Validate(CopperScreenStartupOptions options)
    {
        if (options.Error != null) throw new ArgumentException(options.Error);
        var p = options.Profile;
        if (p.Chipset != AmigaChipset.OcsPal || p.ChipRamSize != 512 * 1024 ||
            p.ExpansionRamSize != 512 * 1024 || p.ExpansionRamBase != 0xC00000 ||
            p.RealFastRamSize != 0 || p.RtgVramSize != 0 || p.RtcEnabled ||
            p.FloppyDriveCount != 1 || options.HardDrives.Count != 0 ||
            (options.CpuBackendOverride ?? p.CpuBackend) != M68kBackendKind.AccurateM68000 ||
            p.KickstartSource is not (CopperScreenKickstartSource.Kickstart13Rom or CopperScreenKickstartSource.KickstartRom) ||
            p.KickstartVersion != KickstartVersion.Kickstart13)
            throw new NotSupportedException("Lightweight supports PAL OCS / 68000 / 512 KiB Chip + 512 KiB slow / native Kickstart 1.3 / one read-only DF0 only; no RTC, RTG or hard drives.");
        if (string.IsNullOrWhiteSpace(options.KickstartRomPath))
            throw new NotSupportedException("Lightweight needs your native Kickstart 1.3 ROM. Set ROM path in Settings > Machine or supply --kickstart <path>.");
        if (options.DriveWriteProtected.Any(value => value == false) ||
            options.DriveDiskPaths.Skip(1).Any(path => path != null))
            throw new NotSupportedException("Lightweight supports read-only DF0 only.");
        if (options.AgnusBusArbitration != AgnusBusArbitrationMode.Legacy ||
            options.CopperQuiescentFastPath || options.CopperQuiescentFastPathVerify || options.CopperQuiescentDiagnostics ||
            options.DeferredCpuBusBatchConfigured || options.DeferredCpuChipWriteJournalConfigured ||
            options.DeferredCpuChipReadSegmentsConfigured || options.DeferredCpuChipInstructionFetchBatchConfigured ||
            options.DeferredCpuChipInstructionFetchShadowConfigured || options.DeferredCpuCustomPointerWritesConfigured ||
            options.DeferredCpuCustomCompositionWritesConfigured || options.CpuWaitSlotReference || !options.HardwareSpecialization)
            throw new NotSupportedException("Legacy execution overrides do not apply to Lightweight.");
        ValidateInput(options.Input);
    }

    private static void ValidateInput(CopperScreenInputOptions input)
    {
        if (input.IsMousePort(1))
            throw new NotSupportedException("Lightweight currently supports a mouse on port 1 only.");
    }

    private static byte[] GetAdf(CopperScreenAdfImage disk)
    {
        if (!Path.GetExtension(disk.Name).Equals(".adf", StringComparison.OrdinalIgnoreCase) || disk.Data.Length != 901120)
            throw new NotSupportedException("Lightweight accepts standard 880 KiB ADF media only (including ADF entries in ZIP files).");
        return disk.Data;
    }

    public int Width => 908;
    public int Height => 626;
    public int[] Framebuffer { get; } = new int[908 * 626];
    public int AudioSampleRate => 48_000;
    public double VideoVBlankHz => 7_093_790.0 / (454 * 313);
    internal static CopperScreenPresentationGeometry NativePresentationGeometry =>
        new(356, 285, 320, 256, 2, 2, true, false)
        {
            // The engine stores the complete beam: visible samples start at
            // lowres x=98, line=26. Standard PAL DIW is ($81,$2C), 320x256.
            FullViewport = new(196, 52, 712, 570),
            StandardViewport = new(258, 88, 640, 512)
        };
    public CopperScreenPresentationGeometry PresentationGeometry => NativePresentationGeometry;
    public CopperScreenEmulatorFrameTiming LastFrameTiming => default; // No invented CPU/device timing split.
    public bool IsInterlaced { get; private set; }
    public int CompletedInterlaceField { get; private set; }
    public string ProfileName { get; }
    public string DiskName => CopperScreenDiskImageArchive.GetDisplayName(DiskPath);
    public string? DiskPath { get; private set; }
    public string BaseDirectory { get; }
    public FloppyDriveAudioOptions FloppyDriveAudioOptions { get; }
    public CopperScreenCpuState CpuState => new(_machine.Cpu.ProgramCounter,
        _machine.Cpu.LastInstructionProgramCounter, _machine.Cpu.StatusRegister);
    public CopperScreenDebugSnapshot? DebugSnapshot => null;
    public string StatusText { get; private set; } = "Lightweight experimental A500";
    public string? FaultMessage => _faulted ? StatusText : null;
    public bool IsPaused { get; private set; }
    public bool IsWorkbenchHandoffPending => false;
    public bool IsDiskSwapPending => _pendingAdf != null;
    public bool IsPrimaryFirePressed => (_mouseButtons & 1) != 0 || ((_joystick0 | _joystick1) & 16) != 0 || _fireFrames > 0;
    public bool AudioFilterEnabled => _machine.AudioFilterControlEnabled;
    internal LightweightA500Machine Machine => _machine;

    public int AudioFramesPerAppFrame(int sampleRate)
        => sampleRate == AudioSampleRate ? 962 : throw new NotSupportedException("Lightweight delivers 48 kHz stereo without host resampling.");

    public void RenderNextFrame(int[] destination)
    {
        if (destination.Length != Framebuffer.Length) throw new ArgumentException("Wrong framebuffer size.");
        if (IsPaused) { Framebuffer.CopyTo(destination, 0); return; }
        if (_pendingAdf != null && _insertDelay-- <= 0)
        {
            _machine.MountAdf(_pendingAdf);
            _pendingAdf = null;
            StatusText = "Lightweight: inserted " + DiskName;
        }
        var field = _machine.IsLongField ? 0 : 1;
        _machine.ExecuteFrame();
        if (_machine.UnsupportedActiveFeature is { } feature)
        {
            CaptureFatalException(new NotSupportedException("Lightweight unsupported: " + feature));
            throw new NotSupportedException(StatusText);
        }
        // Preserve all native pixels. Vertical duplication/weaving is host presentation only.
        var interlaced = _machine.InterlaceEnabled;
        var pixels = _machine.Framebuffer.Span;
        for (var y = 0; y < 313; y++)
        {
            var row = pixels.Slice(y * Width, Width);
            row.CopyTo(Framebuffer.AsSpan((y * 2 + (interlaced ? field : 0)) * Width, Width));
            if (!interlaced || !_haveFrame || !IsInterlaced)
                row.CopyTo(Framebuffer.AsSpan((y * 2 + (interlaced ? 1 - field : 1)) * Width, Width));
        }
        IsInterlaced = interlaced;
        CompletedInterlaceField = field;
        _haveFrame = _audioAvailable = true;
        if (!ReferenceEquals(destination, Framebuffer)) Framebuffer.CopyTo(destination, 0);
        if (_fireFrames > 0 && --_fireFrames == 0) ApplyInput();
    }

    public int RenderAudio(Span<float> destination, int sampleRate, int channels)
    {
        if (sampleRate != AudioSampleRate || channels != 2) throw new NotSupportedException("48 kHz stereo required.");
        if (IsPaused || !_audioAvailable) { destination.Clear(); return 0; }
        var pcm = _machine.AudioSamples.Span;
        if (destination.Length < pcm.Length) throw new ArgumentException("Audio buffer too small.");
        for (var i = 0; i < pcm.Length; i++) destination[i] = pcm[i] * (1.0f / 32768);
        _audioAvailable = false;
        return pcm.Length / 2;
    }

    public void CaptureDriveStates(Span<CopperScreenDriveState> destination)
    {
        var drive = _machine.DriveState;
        for (var i = 0; i < destination.Length; i++)
            destination[i] = i == 0
                ? new(i, true, _machine.IsAdfMounted, DiskName, DiskPath, drive.Cylinder, drive.Head,
                    drive.MotorOn, drive.Selected, true, drive.ActiveDma)
                : new(i, false, false, "Not connected", null, 0, 0, false, false, true, false);
    }

    public bool InsertLoadedDisk(string path, CopperScreenAdfImage disk, bool markChanged)
        => InsertLoadedDisk(0, path, disk, markChanged);

    public bool InsertLoadedDisk(int drive, string path, CopperScreenAdfImage disk, bool markChanged)
    {
        if (drive != 0) return Reject("Only DF0 is supported.");
        var adf = GetAdf(disk); // Validate before changing the existing medium.
        if (markChanged)
        {
            _machine.EjectAdf();
            _pendingAdf = adf;
            _insertDelay = 25;
        }
        else { _machine.MountAdf(adf); _pendingAdf = null; }
        DiskPath = path;
        StatusText = "Lightweight: " + (markChanged ? "swapping " : "inserted ") + DiskName;
        return true;
    }

    public bool EjectDisk(int drive)
    {
        if (drive != 0) return Reject("Only DF0 is supported.");
        _pendingAdf = null;
        _machine.EjectAdf();
        DiskPath = null;
        StatusText = "Lightweight: DF0 ejected";
        return true;
    }
    public bool SetDriveWriteProtected(int drive, bool writeProtected)
        => drive == 0 && writeProtected ? true : Reject("Lightweight supports read-only DF0 only.");
    private bool Reject(string message) { SetStatusText(message); return false; }
    public void SetStatusText(string message) { if (!_faulted) StatusText = message; }
    public void CaptureFatalException(Exception exception)
    { _faulted = IsPaused = true; _audioAvailable = false; StatusText = exception.Message; }
    public bool TogglePaused()
    {
        if (_faulted) return true;
        IsPaused = !IsPaused;
        _audioAvailable = false;
        return IsPaused;
    }
    public void Reset()
    {
        if (_pendingAdf != null) _machine.MountAdf(_pendingAdf);
        _machine.Reset();
        ResetHostState();
    }
    private void ResetHostState()
    {
        _pendingAdf = null;
        _mouseButtons = _joystick0 = _joystick1 = 0;
        _fireFrames = 0;
        _haveFrame = _audioAvailable = _faulted = IsPaused = IsInterlaced = false;
        CompletedInterlaceField = 0;
        Array.Fill(Framebuffer, unchecked((int)0xFF000000));
        ApplyInput();
        StatusText = "Lightweight experimental A500 — native Kickstart 1.3, read-only ADF";
    }
    private void ApplyInput(short dx = 0, short dy = 0)
        => _machine.SubmitInput(new((byte)(_joystick0 | (_inputOptions.IsMousePort(0) ? 0 : 128)),
            _joystick1, (byte)(_mouseButtons | (_fireFrames > 0 ? 1 : 0)), dx, dy, 0));
    public void MoveMousePort(int x, int y)
    {
        if (!_inputOptions.IsMousePort(0)) return;
        // Relative counters wrap at eight bits; reducing large host deltas preserves that result.
        ApplyInput(unchecked((short)x), unchecked((short)y));
    }
    // CopperStart-only absolute cursor hints. MainWindow separately submits relative
    // hardware deltas; applying both would double movement on a native-ROM machine.
    public void SetMousePortPosition(int x, int y) { }
    public void SetMousePresentationPosition(int x, int y) { }
    public void SetMouseButtons(bool primary, bool secondary)
    { _mouseButtons = (byte)((primary ? 1 : 0) | (secondary ? 2 : 0)); ApplyInput(); }
    public void PulsePrimaryFire(int frames) { _fireFrames = Math.Max(0, frames); ApplyInput(); }
    public void SetJoystickPort(bool up, bool down, bool left, bool right, bool primary, bool secondary)
        => SetJoystickPort(_inputOptions.JoystickPortIndex, up, down, left, right, primary, secondary);
    public void SetJoystickPort(int port, bool up, bool down, bool left, bool right, bool primary, bool secondary)
    {
        if ((uint)port > 1) throw new ArgumentOutOfRangeException(nameof(port));
        var value = (byte)((up ? 1 : 0) | (down ? 2 : 0) | (left ? 4 : 0) | (right ? 8 : 0) | (primary ? 16 : 0) | (secondary ? 32 : 0));
        if (port == 0) _joystick0 = value; else _joystick1 = value;
        ApplyInput();
    }
    public void SetInputOptions(CopperScreenInputOptions options)
    { ValidateInput(options); _inputOptions = options; _joystick0 = _joystick1 = _mouseButtons = 0; ApplyInput(); }
    public void SetPresentationOptions(CopperScreenPresentationOptions options) { /* Host presenter owns these options. */ }
    public void KeyDown(AmigaRawKey key) => _machine.SubmitKey((byte)key);
    public void KeyUp(AmigaRawKey key) => _machine.SubmitKey((byte)((byte)key | 128));
    public bool ConsumeCopperBenchRequest() => false;
    public bool LaunchCopperBenchPath(string path, out string message)
    { message = "CopperBench/CopperStart services are not available in Lightweight."; return Reject(message); }
    public void QueueHostClipboardText(string text) => SetStatusText("Host clipboard integration is not available in Lightweight.");
    public void QueueHostClipboardImage(ClipboardImage image) => SetStatusText("Host clipboard integration is not available in Lightweight.");
    public bool TryTakeHostClipboardText(out string text) { text = string.Empty; return false; }
    public bool TryTakeHostClipboardImage(out ClipboardImage? image) { image = null; return false; }
    public void Dispose() => _machine.Dispose();
}
