using Copper68k;

namespace CopperMod.Amiga.Lightweight;

/// <summary>
/// Independent A500 PAL OCS execution boundary.
/// </summary>
/// <remarks>
/// This engine owns memory, the canonical clock and reusable output buffers.
/// Device phases are explicit extension points; unsupported hardware is
/// reported instead of being delegated to the legacy scheduler.
/// </remarks>
public sealed class LightweightA500Machine : IM68kBus, IDisposable
{
    private const int BatchProbeScalarInstructionBudget = 4;
    private const byte CiaAPortAResetLatch = 0xFC;
    private const byte CiaAPortAResetDataDirection = 0x03;
    public const uint CustomBase = 0x00DFF000;
    public const uint CiaABase = 0x00BFE000;
    public const uint CiaBBase = 0x00BFD000;
    public const uint RomBase = 0x00F80000;

    private readonly LightweightA500Configuration _configuration;
    private readonly byte[] _chipRam;
    private readonly byte[] _slowRam;
    private readonly byte[] _rom = new byte[512 * 1024];
    private readonly LightweightClock _clock = new();
    private readonly LightweightBusArbiter _busArbiter = new();
    private readonly LightweightRegisters _registers = new();
    private readonly LightweightCopper _copper = new();
    private readonly LightweightBitplanes _bitplanes = new();
    private readonly LightweightBlitter _blitter = new();
    private readonly LightweightSpriteDma _spriteDma = new();
    private readonly LightweightSprites _sprites = new();
    private readonly LightweightPaulaAudio _paula = new();
    private readonly LightweightCia _ciaA = new();
    private readonly LightweightCia _ciaB = new();
    private readonly LightweightFloppyDrive _floppy = new();
    private LightweightDiskSerial _diskSerial = new();
    private LightweightDiskDma _diskDma = new();
    private LightweightKeyboard _keyboard;
    private LightweightControllers _controllers;
    private readonly LightweightVideo _video;
    private readonly M68kCoreFactory _cpuFactory = M68kCoreFactory.Default;
    private readonly IM68kCore _cpu;
    private readonly IM68kBatchCore _batchCpu;
    private readonly LightweightCpuBoundary _cpuBoundary;
    private readonly bool _enableConservativeCpuLoopBatch;
    private LightweightInputState _input;
    private int _romImageOffset;
    private int _romImageLength;
    private bool _romLoaded;
    private bool _overlayEnabled;
    private long _completedFrames;
    private long _nextDeviceCycle = long.MaxValue;
    private long _nextVerticalBlankLatchCycle = long.MaxValue;
    private long _nextCiaInterruptCycle = long.MaxValue;
    private long _nextInterruptPinEvaluationCycle = long.MaxValue;
    private readonly long[] _interruptRequestVisibilityCycles = new long[14];
    private long _interruptEnableVisibilityCycle = long.MaxValue;
    private ushort _cpuVisibleIntena;
    private ushort _cpuVisibleIntreq;
    private int _interruptPinLevel;
    private long _interruptPinChangeCycle = long.MinValue;
    private string? _unsupportedActiveFeature;
    private bool _disposed;

    public LightweightA500Machine(LightweightA500Configuration? configuration = null)
        : this(configuration, enableConservativeCpuLoopBatch: true)
    {
    }

    internal LightweightA500Machine(
        LightweightA500Configuration? configuration,
        bool enableConservativeCpuLoopBatch)
    {
        _configuration = configuration ?? new LightweightA500Configuration();
        if (_configuration.ChipRamBytes != 512 * 1024 || _configuration.SlowRamBytes != 512 * 1024)
            throw new ArgumentException("The lightweight A500 profile supports exactly 512 KiB Chip RAM and 512 KiB slow RAM.", nameof(configuration));
        if (_configuration.AudioSampleRate != 48_000 || _configuration.AudioChannels != 2)
            throw new ArgumentException("The lightweight A500 output supports 48 kHz stereo PCM.", nameof(configuration));

        _chipRam = new byte[_configuration.ChipRamBytes];
        _slowRam = new byte[_configuration.SlowRamBytes];
        _video = new LightweightVideo(_configuration);
        _cpu = _cpuFactory.Create(M68kCpuModel.M68000, this);
        _batchCpu = (IM68kBatchCore)_cpu;
        _cpuBoundary = new LightweightCpuBoundary(this);
        _enableConservativeCpuLoopBatch = enableConservativeCpuLoopBatch;
        InstallResetLoop();
        Reset();
    }

    public M68kCpuState Cpu => _cpu.State;
    public long Cycle => _clock.Cycle;
    public long CompletedFrames => _completedFrames;
    public int BeamLine => _clock.Line;
    public int BeamColorClock => _clock.ColorClock;
    public ReadOnlyMemory<int> Framebuffer => _video.Framebuffer;
    public int FramebufferWidth => _configuration.FramebufferWidth;
    public int FramebufferHeight => _configuration.FramebufferHeight;
    /// <summary>Completed field PCM, interleaved left/right; valid until the next ExecuteFrame.
    /// The sample count follows the PAL clock and can vary between fields.</summary>
    public ReadOnlyMemory<short> AudioSamples => _paula.Audio;
    internal bool AudioProducesPcm => true;
    public ReadOnlyMemory<byte> ChipRam => _chipRam;
    public LightweightInputState Input => _input;
    public bool IsAdfMounted => _floppy.Mounted;
    /// <summary>Read-only host metadata; sample on the hardware owner thread.</summary>
    public LightweightDriveState DriveState => new(_floppy.Cylinder, _floppy.Head,
        _floppy.MotorOn, _floppy.Selected, _diskDma.Active);
    public bool InterlaceEnabled => (_video.EffectiveBplcon0 & 4) != 0;
    public bool AudioFilterControlEnabled =>
        ((_ciaA.ReadPortLatch(0) | ~_ciaA.ReadDataDirection(0)) & 2) == 0;
    internal long DiskSerialNextCycle => _diskSerial.NextCycle;
    internal int DiskBitPosition => _diskSerial.BitPosition;
    internal ushort DiskInputShift => _diskSerial.Shift;
    internal bool DiskDmaActive => _diskDma.Active;
    internal int DiskDmaRemaining => _diskDma.Remaining;
    internal long DiskDmaNextCycle => _diskDma.NextCycle;
    public bool RomOverlayEnabled => _overlayEnabled;
    public string? UnsupportedActiveFeature =>
        _unsupportedActiveFeature ?? _video.UnsupportedActiveFeature;
    internal int LinesThisField => _clock.LinesThisField;
    public bool IsLongField => _clock.IsLongField;
    internal long NextFrameCycle => _clock.NextFrameCycle;
    internal long NextDeviceCycle => _nextDeviceCycle;
    internal ushort Dmacon => _registers.Dmacon;
    internal ushort Adkcon => _registers.Adkcon;
    internal ushort Intena => _registers.Intena;
    internal ushort Intreq => _registers.Intreq;
    internal ushort DdfStop => _registers.DdfStopValue;
    internal int InterruptPinLevel => _interruptPinLevel;
    internal long InterruptPinChangeCycle => _interruptPinChangeCycle;
    internal byte CiaAInterruptMask => _ciaA.InterruptMask;
    internal byte CiaAPendingInterrupts => _ciaA.PendingInterrupts;
    internal byte CiaBInterruptMask => _ciaB.InterruptMask;
    internal byte CiaBPendingInterrupts => _ciaB.PendingInterrupts;
    internal uint CiaATod => _ciaA.TodCounter;
    internal uint CiaBTod => _ciaB.TodCounter;
    internal long KeyboardNextCycle => _keyboard.NextCycle;
    internal bool KeyboardWaitingForHandshake => _keyboard.WaitingForHandshake;
    internal long NextCiaInterruptCycle => _nextCiaInterruptCycle;
    internal long CopperNextCycle => _copper.NextCycle;
    internal long CopperPendingOutputCycle => _copper.PendingOutputCycle;
    internal long CopperLastOutputCycle => _copper.LastOutputCycle;
    internal long CopperLastAcceptedInputCycle => _copper.LastAcceptedInputCycle;
    internal long CopperLastInstructionFirstInputCycle => _copper.LastInstructionFirstInputCycle;
    internal long CopperLastMoveCycle => _copper.LastMoveCycle;
    internal uint CopperProgramCounter => _copper.ProgramCounter;
    internal bool CopperWaiting => _copper.IsWaiting;
    internal long BitplaneNextCycle => _bitplanes.NextCycle;
    internal long BitplanePendingOutputCycle => _bitplanes.PendingOutputCycle;
    internal long BitplaneLastInputCycle => _bitplanes.LastInputCycle;
    internal long BitplaneLastOutputCycle => _bitplanes.LastOutputCycle;
    internal int BitplaneLastPlane => _bitplanes.LastPlane;
    internal uint BitplaneLastAddress => _bitplanes.LastAddress;
    internal bool BitplaneRunActive => _bitplanes.RunActive;
    internal long VideoNextCycle => _video.NextCycle;
    internal bool VideoActive => _video.Active;
    internal bool BlitterBusy => _blitter.Active;
    internal bool BlitterStatusBusy => _blitter.Busy;
    internal bool BlitterZero => _blitter.Zero;
    internal bool BlitterActive => _blitter.Active;
    internal bool BlitterSupportsDescendingFill => true;
    internal bool BlitterSupportsLineMode => true;
    internal bool BlitterEffectiveNasty => _blitter.EffectiveNasty;
    internal long BlitterNextCycle => _blitter.NextCycle;
    internal long BlitterPendingOutputCycle => _blitter.PendingOutputCycle;
    internal long BlitterLastAcceptedInputCycle => _blitter.LastAcceptedInputCycle;
    internal long BlitterLastOutputCycle => _blitter.LastBusOutputCycle;
    internal long BlitterLastCompletionCycle => _blitter.LastCompletionCycle;
    internal long BlitterLastTerminationCycle => _blitter.LastTerminationCycle;
    internal long SpriteNextCycle => _sprites.NextCycle;
    internal long SpriteLastRegisterInputCycle => _sprites.LastRegisterInputCycle;
    internal long SpriteDmaNextCycle => _spriteDma.NextCycle;
    internal long SpriteDmaPendingOutputCycle => _spriteDma.PendingOutputCycle;
    internal uint SpriteDmaPendingAddress => _spriteDma.PendingAddress;
    internal long SpriteDmaLastInputCycle => _spriteDma.LastInputCycle;
    internal long SpriteDmaLastOutputCycle => _spriteDma.LastOutputCycle;
    internal int SpriteDmaLastChannel => _spriteDma.LastChannel;
    internal int SpriteDmaLastWord => _spriteDma.LastWord;
    internal uint SpriteDmaLastAddress => _spriteDma.LastAddress;
    internal long PaulaNextCycle => _paula.NextCycle;

    public static IReadOnlyList<string> UnsupportedFeatures { get; } = new[]
    {
        "ECS/AGA chipset profiles",
        "IPF and non-ADF disk formats",
        "save states",
        "dual-playfield output, HAM outside five/six-plane lores, hires BPU above four and disk write DMA",
        "hires output with a 454-pixel framebuffer (select 908 for native OCS output)",
        "keyboard power-up/resynchronization/reset-chord MCU sequences and general CIA serial output/CNT timer modes",
        "analog paddles, controller adapters and physical mouse quadrature phase",
        "physical TOD pulse/debounce phases and comparator-write glitches (bounded raster-TOD model only)",
        "nonstandard disk speed, analog disk recovery, slow/GCR/MSBSYNC disk input",
        "writable ADF media and additional floppy drives",
        "undocumented 227-CCK wrap-dummy bus data"
    };

    public void Reset()
    {
        ThrowIfDisposed();
        _clock.Reset();
        _registers.Reset();
        _copper.Reset();
        _bitplanes.Reset();
        _blitter.Reset();
        _spriteDma.Reset(_clock.FrameStartCycle);
        _sprites.Reset();
        _paula.Reset();
        _ciaA.Reset(CiaAPortAResetLatch, CiaAPortAResetDataDirection);
        _ciaB.Reset();
        _keyboard.Reset();
        _controllers = default;
        _input = default;
        _floppy.ResetControl();
        _diskSerial.Reset();
        _diskDma.Reset();
        _video.Reset();
        _overlayEnabled = _romLoaded;
        _completedFrames = 0;
        _nextDeviceCycle = long.MaxValue;
        _nextVerticalBlankLatchCycle = long.MaxValue;
        _nextCiaInterruptCycle = long.MaxValue;
        _nextInterruptPinEvaluationCycle = long.MaxValue;
        Array.Fill(_interruptRequestVisibilityCycles, long.MaxValue);
        _interruptEnableVisibilityCycle = long.MaxValue;
        _cpuVisibleIntena = 0;
        _cpuVisibleIntreq = 0;
        _interruptPinLevel = 0;
        _interruptPinChangeCycle = long.MinValue;
        _unsupportedActiveFeature = null;
        var stackPointer = ReadLongRaw(0);
        var programCounter = ReadLongRaw(4);
        if (stackPointer == 0 || stackPointer >= _chipRam.Length)
            stackPointer = (uint)(_chipRam.Length - 4);
        var programCounterInChipRam = programCounter < _chipRam.Length;
        var programCounterInRom = _romLoaded && programCounter >= RomBase && programCounter < RomBase + (uint)_rom.Length;
        if (programCounter == 0 || (!programCounterInChipRam && !programCounterInRom))
            programCounter = 0x1000;
        _cpu.Reset(programCounter, stackPointer);
        _input = default;
    }

    public void ExecuteFrame()
    {
        ThrowIfDisposed();
        var targetCompletedFrames = _completedFrames + 1;
        while (_completedFrames < targetCompletedFrames)
        {
            var frameBoundary = _clock.NextFrameCycle;
            if (_cpu.State.Halted)
            {
                AdvanceHardwareTo(frameBoundary);
                _cpu.State.Cycles = Math.Max(_cpu.State.Cycles, frameBoundary);
                continue;
            }

            if (_cpu.State.Stopped)
            {
                // The A500 chipset modeled here can assert at most IPL6. A
                // STOPped 68000 masking level 6 or 7 therefore cannot wake
                // before the caller's frame boundary. Devices still execute
                // every physical phase inside AdvanceHardwareTo; this only
                // removes one host ExecuteFrame iteration per active CCK.
                var interruptMask = (_cpu.State.StatusRegister >> 8) & 0x07;
                var wakeCycle = interruptMask >= 6
                    ? frameBoundary
                    : ClampCpuBatchTarget(frameBoundary);
                AdvanceHardwareTo(wakeCycle);
                _cpu.State.Cycles = Math.Max(_cpu.State.Cycles, wakeCycle);
                _ = DispatchPendingCpuInterrupt();
                continue;
            }

            if (DispatchPendingCpuInterrupt())
            {
                continue;
            }

            if (!_enableConservativeCpuLoopBatch ||
                !IsConservativeCpuLoopCandidate())
            {
                ExecuteScalarFallbackQuantum(frameBoundary);
                continue;
            }

            _cpuBoundary.BeginBatchProbe(BatchProbeScalarInstructionBudget);
            var executed = _batchCpu.ExecuteInstructions(
                int.MaxValue,
                frameBoundary,
                _cpuBoundary);
            if (executed == 0 && !_cpu.State.Stopped && !_cpu.State.Halted)
            {
                ExecuteScalarFallbackQuantum(frameBoundary);
            }
        }
    }

    private bool IsConservativeCpuLoopCandidate()
    {
        const ushort nop = 0x4E71;
        const ushort branchBackOneWord = 0x60FC;
        var programCounter = _cpu.State.ProgramCounter & 0x00FF_FFFFu;
        if (!TryPeekInstructionWord(programCounter, out var opcode))
        {
            return false;
        }

        if (opcode == nop)
        {
            return TryPeekInstructionWord(
                unchecked(programCounter + 2),
                out var branch) &&
                branch == branchBackOneWord;
        }

        return opcode == branchBackOneWord &&
            programCounter >= 2 &&
            TryPeekInstructionWord(programCounter - 2, out var previous) &&
            previous == nop;
    }

    private bool TryPeekInstructionWord(uint address, out ushort value)
    {
        address &= 0x00FF_FFFFu;
        var readable =
            _overlayEnabled && address + 1 < 0x400 ||
            address + 1 < _chipRam.Length ||
            address >= 0x00C00000 &&
                address + 1 < 0x00C00000 + _slowRam.Length ||
            address >= RomBase && address + 1 < RomBase + _rom.Length;
        if (!readable)
        {
            value = 0;
            return false;
        }

        value = ReadWordRaw(address);
        return true;
    }

    private void ExecuteScalarFallbackQuantum(long targetCycle)
    {
        while (_cpu.State.Cycles < targetCycle)
        {
            if (DispatchPendingCpuInterrupt())
            {
                continue;
            }

            if (_cpu.State.Halted || _cpu.State.Stopped)
            {
                return;
            }

            var previousCycle = _cpu.State.Cycles;
            _cpu.ExecuteInstruction();
            if (_cpu.State.Cycles <= previousCycle)
            {
                throw new InvalidOperationException(
                    "The 68000 core did not advance the lightweight clock.");
            }

            AdvanceHardwareTo(_cpu.State.Cycles);
        }
    }

    public void SubmitInput(LightweightInputState input)
    {
        ThrowIfDisposed();
        if (input.KeyCode != 0 && (input.KeyCode & 0xFF00) != 0x0100)
            throw new ArgumentOutOfRangeException(nameof(input), "KeyCode must be zero (no key) or 0x100 | raw key byte.");
        if ((input.MouseButtons & ~7) != 0 || (input.JoystickPort0 & 0x40) != 0 ||
            (input.JoystickPort1 & 0xC0) != 0)
            throw new ArgumentOutOfRangeException(nameof(input), "Unsupported controller flags.");
        if (input.KeyCode != 0) SubmitKey((byte)input.KeyCode);
        _input = input;
        _controllers.Submit(input);
    }

    /// <summary>Queue a raw Amiga key transition (bit 7 means release).
    /// Call on the hardware owner thread at the desired machine-cycle boundary.
    /// Host key mapping/repeat/caps-lock translation is outside the engine.</summary>
    public void SubmitKey(byte rawKey)
    {
        ThrowIfDisposed();
        if (!_keyboard.Enqueue(rawKey, _clock.Cycle))
            throw new InvalidOperationException("Keyboard type-ahead buffer is full (10 queued keycodes).");
        RefreshCiaInterruptCycle();
    }

    public void LoadKickstart(ReadOnlySpan<byte> image, uint baseAddress = RomBase)
    {
        ThrowIfDisposed();
        if (image.Length is not (0x40000 or 0x80000))
            throw new ArgumentException("The A500 engine accepts 256 KiB or 512 KiB Kickstart images.", nameof(image));

        // A 256 KiB Kickstart occupies the upper half of the A500's 512 KiB
        // ROM window ($FC0000-$FFFFFF); 512 KiB images occupy the full window.
        var mappedBase = image.Length == 0x40000 && baseAddress == RomBase ? RomBase + 0x40000u : baseAddress;
        if (mappedBase < RomBase || mappedBase >= RomBase + (uint)_rom.Length || image.Length > _rom.Length - (mappedBase - RomBase))
            throw new ArgumentOutOfRangeException(nameof(baseAddress));
        _rom.AsSpan().Clear();
        _romImageOffset = (int)(mappedBase - RomBase);
        _romImageLength = image.Length;
        image.CopyTo(_rom.AsSpan(_romImageOffset, image.Length));
        _romLoaded = true;
        Reset();
    }

    public void MountAdf(ReadOnlySpan<byte> image)
    {
        ThrowIfDisposed();
        _floppy.Mount(image);
        _diskSerial.OnDriveChanged(_floppy, Cycle, mediaChanged: true);
        RefreshNextDeviceCycle();
    }

    public void EjectAdf()
    {
        ThrowIfDisposed();
        _floppy.Eject();
        _diskSerial.OnDriveChanged(_floppy, Cycle, mediaChanged: true);
        RefreshNextDeviceCycle();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cpu.Dispose();
    }

    public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
    {
        _ = accessKind;
        GrantCpuWord(address, cycle, out var grantedCycle, out var completedCycle, byteAccess: true);
        AdvanceHardwareTo(grantedCycle);
        var value = ReadByteAt(address, grantedCycle);
        AdvanceHardwareTo(completedCycle);
        cycle = completedCycle;
        return value;
    }

    public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
    {
        _ = accessKind;
        GrantCpuWord(address, cycle, out var grantedCycle, out var completedCycle);
        AdvanceHardwareTo(grantedCycle);
        var value = ReadWordAt(address, grantedCycle);
        AdvanceHardwareTo(completedCycle);
        cycle = completedCycle;
        return value;
    }

    public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind)
    {
        _ = accessKind;
        GrantCpuWord(address, cycle, out var firstWordCycle, out var firstCompletedCycle);
        AdvanceHardwareTo(firstWordCycle);
        var high = ReadWordAt(address, firstWordCycle);
        AdvanceHardwareTo(firstCompletedCycle);
        var secondRequestCycle = UsesAgnusBus(address)
            ? firstWordCycle + (2 * LightweightBusArbiter.SlotCycles)
            : firstCompletedCycle;
        GrantCpuWord(address + 2, secondRequestCycle, out var secondWordCycle, out var completedCycle);
        AdvanceHardwareTo(secondWordCycle);
        var low = ReadWordAt(address + 2, secondWordCycle);
        AdvanceHardwareTo(completedCycle);
        cycle = completedCycle;
        return ((uint)high << 16) | low;
    }

    public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
    {
        _ = accessKind;
        GrantCpuWord(address, cycle, out var grantedCycle, out var completedCycle, byteAccess: true);
        AdvanceHardwareTo(grantedCycle);
        WriteByteRaw(address, value, grantedCycle);
        AdvanceHardwareTo(completedCycle);
        cycle = completedCycle;
    }

    public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
    {
        _ = accessKind;
        GrantCpuWord(address, cycle, out var grantedCycle, out var completedCycle);
        AdvanceHardwareTo(grantedCycle);
        WriteWordRaw(address, value, grantedCycle);
        AdvanceHardwareTo(completedCycle);
        cycle = completedCycle;
    }

    public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
    {
        _ = accessKind;
        GrantCpuWord(address, cycle, out var firstWordCycle, out var firstCompletedCycle);
        AdvanceHardwareTo(firstWordCycle);
        WriteWordRaw(address, (ushort)(value >> 16), firstWordCycle);
        AdvanceHardwareTo(firstCompletedCycle);
        var secondRequestCycle = UsesAgnusBus(address)
            ? firstWordCycle + (2 * LightweightBusArbiter.SlotCycles)
            : firstCompletedCycle;
        GrantCpuWord(address + 2, secondRequestCycle, out var secondWordCycle, out var completedCycle);
        AdvanceHardwareTo(secondWordCycle);
        WriteWordRaw(address + 2, (ushort)value, secondWordCycle);
        AdvanceHardwareTo(completedCycle);
        cycle = completedCycle;
    }

    public void ResetExternalDevices(long cycle)
    {
        AdvanceHardwareTo(cycle);
        _registers.Reset();
        _copper.Reset();
        _bitplanes.Reset();
        _blitter.Reset();
        _spriteDma.Reset(_clock.FrameStartCycle);
        _sprites.Reset();
        _paula.ResetHardware(_clock.Cycle);
        _ciaA.Reset(CiaAPortAResetLatch, CiaAPortAResetDataDirection);
        _ciaB.Reset();
        _floppy.ResetControl();
        _diskSerial.Reset();
        _diskDma.Reset();
        _video.Reset();
        _overlayEnabled = _romLoaded;
        _nextVerticalBlankLatchCycle = long.MaxValue;
        _nextDeviceCycle = long.MaxValue;
        _nextCiaInterruptCycle = long.MaxValue;
        _nextInterruptPinEvaluationCycle = long.MaxValue;
        Array.Fill(_interruptRequestVisibilityCycles, long.MaxValue);
        _interruptEnableVisibilityCycle = long.MaxValue;
        _cpuVisibleIntena = 0;
        _cpuVisibleIntreq = 0;
        _interruptPinLevel = 0;
        _interruptPinChangeCycle = long.MinValue;
        _unsupportedActiveFeature = null;
        // RESET resets the CIA, not the independently powered keyboard.
        _keyboard.HostDataChanged(_ciaA.SerialSpHigh, cycle);
        RefreshCiaInterruptCycle();
    }

    private void GrantCpuWord(
        uint address,
        long requestedCycle,
        out long grantedCycle,
        out long completedCycle,
        bool byteAccess = false)
    {
        var start = Math.Max(requestedCycle, _clock.Cycle);
        var usesCia = byteAccess
            ? TryDecodeCiaRegister(address, out _, out _)
            : IsCiaWordAccess(address);
        if (usesCia)
        {
            var ciaCycle = Math.Max(0, start + 1);
            var remainder = ciaCycle % LightweightCia.CpuCyclesPerTick;
            grantedCycle = remainder == 0
                ? ciaCycle
                : ciaCycle + LightweightCia.CpuCyclesPerTick - remainder;
            completedCycle = grantedCycle;
            return;
        }

        if (UsesAgnusBus(address))
        {
            grantedCycle = LightweightBusArbiter.AlignToSlot(start);
            _blitter.BeginCpuRequest(grantedCycle);
            try
            {
                grantedCycle = _clock.AdvanceToCpuGrant(grantedCycle, this);
            }
            finally
            {
                _blitter.EndCpuRequest();
            }
            completedCycle = grantedCycle + LightweightBusArbiter.SlotCycles;
            return;
        }

        grantedCycle = start;
        completedCycle = start;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal void UpdateCpuRequestCandidate(long cycle)
        => _blitter.UpdateCpuRequestCandidate(cycle);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal bool IsCpuOutputOwned(long cycle)
        => IsHigherPriorityOutputOwned(cycle) || _blitter.OwnsOutputSlot(cycle);

    private bool UsesAgnusBus(uint address)
    {
        address &= 0x00FF_FFFFu;
        if (_overlayEnabled && address < 0x400)
            return false;
        return address < _chipRam.Length ||
            address >= 0x00C00000 && address < 0x00C00000 + _slowRam.Length ||
            address >= CustomBase && address < CustomBase + 0x200;
    }

    private static bool IsCiaWordAccess(uint address)
        => TryDecodeCiaRegister(address, out _, out _) ||
            TryDecodeCiaRegister(address + 1, out _, out _);

    internal void AdvanceHardwareToCpuCycle()
        => AdvanceHardwareTo(_cpu.State.Cycles);

    internal void AdvanceHardwareTo(long cycle)
    {
        if (cycle > _clock.Cycle)
            _clock.AdvanceTo(cycle, this);
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal bool CanCopperOwnOutputSlot(long outputCycle)
    {
        System.Diagnostics.Debug.Assert(
            outputCycle == _clock.Cycle + LightweightClock.CpuCyclesPerColorClock);
        return !IsFollowingCckMandatoryRefresh() &&
            _diskDma.PendingOutputCycle != outputCycle &&
            !_paula.OwnsOutputSlot(outputCycle) &&
            !_bitplanes.OwnsOutputSlot(outputCycle) &&
            !_spriteDma.OwnsOutputSlot(outputCycle);
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal bool CanBitplaneOwnOutputSlot(long outputCycle)
    {
        System.Diagnostics.Debug.Assert(
            outputCycle == _clock.Cycle + LightweightClock.CpuCyclesPerColorClock);
        return !IsFollowingCckMandatoryRefresh() &&
            _diskDma.PendingOutputCycle != outputCycle && !_paula.OwnsOutputSlot(outputCycle);
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal bool CanSpriteOwnOutputSlot(long outputCycle)
    {
        System.Diagnostics.Debug.Assert(
            outputCycle == _clock.Cycle + LightweightClock.CpuCyclesPerColorClock);
        return !IsFollowingCckMandatoryRefresh() &&
            _diskDma.PendingOutputCycle != outputCycle &&
            !_paula.OwnsOutputSlot(outputCycle) &&
            !_bitplanes.OwnsOutputSlot(outputCycle) &&
            !_copper.OwnsOutputSlot(outputCycle) &&
            !_blitter.OwnsOutputSlot(outputCycle);
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal bool CanBlitterAdvanceControl(long inputCycle)
    {
        System.Diagnostics.Debug.Assert(inputCycle == _clock.Cycle);
        var outputCycle = inputCycle + LightweightClock.CpuCyclesPerColorClock;
        return !IsFollowingCckMandatoryRefresh() &&
            _diskDma.PendingOutputCycle != outputCycle &&
            !_paula.OwnsOutputSlot(outputCycle) &&
            !_bitplanes.OwnsOutputSlot(outputCycle) &&
            !_spriteDma.OwnsOutputSlot(outputCycle) &&
            !_copper.OwnsOutputSlot(outputCycle);
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal bool IsHigherPriorityOutputOwned(long outputCycle)
    {
        System.Diagnostics.Debug.Assert(outputCycle == _clock.Cycle);
        return IsCurrentCckMandatoryRefresh() ||
            _diskDma.LastOutputCycle == outputCycle ||
            _paula.OwnsOutputSlot(outputCycle) ||
            _bitplanes.OwnsOutputSlot(outputCycle) ||
            _spriteDma.OwnsOutputSlot(outputCycle) ||
            _copper.OwnsOutputSlot(outputCycle);
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private bool IsCurrentCckMandatoryRefresh()
    {
        var horizontal = _clock.ColorClock;
        return horizontal <= LightweightBusArbiter.LastRefreshHorizontal &&
            ((horizontal - LightweightBusArbiter.FirstRefreshHorizontal) & 1) == 0;
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private bool IsFollowingCckMandatoryRefresh()
    {
        var horizontal = _clock.ColorClock + 1;
        if (horizontal ==
            LightweightClock.CpuCyclesPerLine /
                LightweightClock.CpuCyclesPerColorClock)
        {
            horizontal = 0;
        }

        return horizontal <= LightweightBusArbiter.LastRefreshHorizontal &&
            ((horizontal - LightweightBusArbiter.FirstRefreshHorizontal) & 1) == 0;
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal ushort ReadChipWordDma(uint address)
    {
        address &= (uint)(_chipRam.Length - 1);
        address &= 0x00FF_FFFEu;
        return (ushort)((_chipRam[address] << 8) | _chipRam[address + 1]);
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal void WriteChipWordDma(uint address, ushort value)
    {
        address &= (uint)(_chipRam.Length - 1);
        address &= 0x00FF_FFFEu;
        _chipRam[address] = (byte)(value >> 8);
        _chipRam[address + 1] = (byte)value;
    }

    internal uint GetCopperListPointer(bool secondList)
        => _registers.GetCopperListPointer(secondList);

    internal uint GetBitplanePointer(int plane)
        => _registers.GetBitplanePointer(plane);

    internal uint GetSpritePointer(int sprite)
        => _registers.GetSpritePointer(sprite);

    internal short GetBitplaneModulo(int plane)
        => unchecked((short)_registers.Read(
            (plane & 1) == 0 ? LightweightRegisters.Bpl1mod : LightweightRegisters.Bpl2mod));

    internal void SetBitplanePointerFromDma(int plane, uint pointer)
        => _registers.SetBitplanePointerFromDma(plane, pointer);

    internal void SetSpritePointerFromDma(int sprite, uint pointer)
        => _registers.SetSpritePointerFromDma(sprite, pointer);

    internal void SetSpriteRegisterFromDma(
        int sprite,
        int registerOffset,
        ushort value)
        => _registers.SetSpriteRegisterFromDma(
            sprite,
            registerOffset,
            value);

    internal uint GetLiveSpritePointer(int sprite)
        => _spriteDma.GetPointer(sprite);

    internal ushort GetSpriteDmaPos(int sprite)
        => _spriteDma.GetPos(sprite);

    internal ushort GetSpriteDmaCtl(int sprite)
        => _spriteDma.GetCtl(sprite);

    internal ushort GetSpriteDmaDataA(int sprite)
        => _spriteDma.GetDataA(sprite);

    internal ushort GetSpriteDmaDataB(int sprite)
        => _spriteDma.GetDataB(sprite);

    internal bool IsSpriteDmaActive(int sprite)
        => _spriteDma.IsActive(sprite);

    internal bool IsSpriteDmaExhausted(int sprite)
        => _spriteDma.IsExhausted(sprite);

    internal ushort BitplaneEffectiveBplcon0 => _bitplanes.EffectiveBplcon0;

    internal void OnBitplaneDataOutput(int plane, ushort value, long cycle)
        => _video.AcceptBitplaneWord(plane, value, cycle);

    internal void OnSpriteDmaControlOutput(
        int sprite,
        ushort pos,
        ushort ctl,
        long cycle)
        => _sprites.OnDmaControl(sprite, pos, ctl, cycle);

    internal void OnSpriteDmaDataAOutput(
        int sprite,
        ushort value,
        long cycle)
        => _sprites.OnDmaDataA(sprite, value, cycle);

    internal void OnSpriteDmaDataBOutput(
        int sprite,
        ushort value,
        long cycle)
        => _sprites.OnDmaDataB(sprite, value, cycle);

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal int ComposeSpriteColorIndex(
        int line,
        int x,
        int playfieldColorIndex,
        int playfieldPlacement)
        => _sprites.ComposeColorIndex(
            line,
            x,
            playfieldColorIndex,
            playfieldPlacement);

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal void ComposeSpriteColorIndexes(
        int line,
        int x,
        int firstPlayfieldColorIndex,
        int secondPlayfieldColorIndex,
        int playfieldPlacement,
        out int first,
        out int second)
        => _sprites.ComposeColorIndexes(
            line,
            x,
            firstPlayfieldColorIndex,
            secondPlayfieldColorIndex,
            playfieldPlacement,
            out first,
            out second);

    internal void ComposeHiresSpriteColorIndexes(int line, int x, int firstPlayfield,
        int secondPlayfield, int placement, out int first, out int second)
        => _sprites.ComposeHiresColorIndexes(line, x, firstPlayfield, secondPlayfield,
            placement, out first, out second);

    internal bool IsManualSpriteArmed(int sprite)
        => _sprites.IsManualArmed(sprite);

    internal uint GetLiveBitplanePointer(int plane)
        => _bitplanes.GetPointer(plane);

    internal ushort GetBitplaneDataLatch(int plane)
        => _bitplanes.GetDataLatch(plane);

    internal ushort GetCustomRegister(ushort offset)
    {
        offset &= 0x01FE;
        return offset == LightweightRegisters.Dmaconr
            ? (ushort)(_registers.Dmacon | _blitter.StatusBits)
            : _registers.Read(offset);
    }

    internal LightweightPaulaChannelSnapshot GetPaulaChannelSnapshot(int channel)
        => _paula.GetChannelSnapshot(channel);

    internal uint GetBlitterPointer(ushort highOffset)
        => _registers.GetBlitterPointer(highOffset);

    internal void SetBlitterPointerFromDma(ushort highOffset, uint pointer)
        => _registers.SetBlitterPointerFromDma(highOffset, pointer);

    internal void SetBlitterDataFromDma(ushort offset, ushort value)
        => _registers.SetBlitterDataFromDma(offset, value);

    internal void ReportUnsupportedFeature(string feature)
        => _unsupportedActiveFeature ??= feature;

    internal uint GetDiskPointer() => _registers.GetDiskPointer();

    internal void SetDiskPointerFromDma(uint pointer)
        => _registers.SetDiskPointerFromDma(pointer);

    internal void SetDiskDataFromDma(ushort data)
        => _registers.SetDiskDataFromDma(data);

    internal void SetDiskLengthFromDma(int remaining)
        => _registers.SetDiskLengthFromDma(remaining);

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal void ReceiveDiskBit(ushort shift, bool equal, long cycle)
        => _diskDma.ReceiveBit(shift, equal, cycle, this);

    internal void WriteCustomRegisterFromCopper(
        ushort offset,
        ushort value,
        long cycle)
        => WriteCustomRegister(offset, value, cycle);

    internal long ClampCpuBatchTarget(long targetCycle)
    {
        var boundary = Math.Min(targetCycle, _clock.NextFrameCycle);
        boundary = Math.Min(boundary, _nextDeviceCycle);
        return Math.Max(_cpu.State.Cycles, boundary);
    }

    internal void TickDevices(long cycle)
    {
        System.Diagnostics.Debug.Assert(cycle >= _nextDeviceCycle);

        if (cycle >= _nextCiaInterruptCycle)
        {
            AdvanceCiasTo(cycle);
        }

        if (cycle == _video.NextCycle)
        {
            _video.Step(cycle, this);
        }

        if (cycle == _diskSerial.NextCycle)
        {
            _diskSerial.Step(cycle, _floppy, this);
        }

        if (cycle == _diskDma.NextCycle)
        {
            _diskDma.Step(cycle, this);
        }

        // Fixed audio address phases publish ownership before lower-priority
        // DMA can accept the same output CCK.
        if (cycle == _paula.NextCycle)
        {
            _paula.Step(cycle, this);
        }

        if (cycle == _bitplanes.NextCycle)
        {
            _bitplanes.Step(cycle, this);
        }

        if (cycle == _spriteDma.NextCycle)
        {
            _spriteDma.Step(cycle, this);
        }

        if (cycle == _copper.NextCycle)
        {
            _copper.Step(cycle, this);
        }

        if (cycle == _blitter.NextCycle)
        {
            _blitter.Step(cycle, this);
        }

        if (cycle == _sprites.NextCycle)
        {
            _sprites.Step(cycle, this);
        }

        if (cycle >= _nextVerticalBlankLatchCycle)
        {
            _nextVerticalBlankLatchCycle = long.MaxValue;
            LatchHardwareInterrupt(0x0020, cycle, cpuVisibilityDelay: 0);
        }

        if (cycle >= _nextInterruptPinEvaluationCycle)
        {
            ApplyPendingInterruptVisibility(cycle);
        }

        RefreshNextDeviceCycle(afterDeviceTick: true);
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal void OnLineCompleted(long cycle)
    {
        if (_ciaB.TodRunning)
            HandleCiaInterrupt(ciaA: false, _ciaB.PulseTod(cycle));
    }

    internal void OnFrameCompleted(long cycle)
    {
        HandleCiaInterrupt(ciaA: true, _ciaA.PulseTod(cycle));
        var completedFieldLines = _clock.LinesThisField;
        _paula.CompleteFrame(cycle);
        _video.CompleteFrame(cycle, completedFieldLines, this);
        _completedFrames++;
        if ((_video.EffectiveBplcon0 & 0x0004) != 0)
        {
            _clock.SelectLongField(!_clock.IsLongField);
        }
        _bitplanes.OnFrameStart(cycle, this);
        _spriteDma.OnFrameStart(cycle, this);
        _copper.OnFrameStart(cycle, this);
        // The OCS raster strobe is issued at h0; the captured A500 path
        // latches VERTB and presents IPL3 one color clock later.
        _nextVerticalBlankLatchCycle = cycle + LightweightClock.CpuCyclesPerColorClock;
        RefreshCiaInterruptCycle();
    }

    internal bool DispatchPendingCpuInterrupt()
    {
        if (_interruptPinLevel <= 0)
            return false;

        var interruptMask = (_cpu.State.StatusRegister >> 8) & 0x07;
        if (_interruptPinLevel <= interruptMask)
            return false;

        var recognition = _cpu as IM68000InterruptRecognition;
        if (recognition != null &&
            !recognition.HasRecognizedInterrupt(
                _interruptPinLevel,
                _interruptPinChangeCycle,
                interruptMask))
        {
            return false;
        }

        _cpu.RequestInterrupt(
            _interruptPinLevel,
            (uint)((24 + _interruptPinLevel) * 4));
        // Interrupt entry can retire internal cycles after its last bus access.
        // Complete that tail on the same clock before testing a frame boundary.
        AdvanceHardwareTo(_cpu.State.Cycles);
        return true;
    }

    private void LatchHardwareInterrupt(
        ushort bits,
        long cycle,
        int cpuVisibilityDelay)
    {
        _registers.SetHardwareInterruptRequest(bits);
        if (cpuVisibilityDelay == 0)
        {
            _cpuVisibleIntreq |= (ushort)(bits & 0x3FFF);
            UpdateInterruptPin(cycle);
            return;
        }

        ScheduleInterruptRequestVisibility(bits, cycle + cpuVisibilityDelay);
    }

    internal void LatchBlitterInterrupt(long cycle)
        => LatchHardwareInterrupt(0x0040, cycle, cpuVisibilityDelay: 0);

    // Provisional digital-input visibility: register latch at the receiving
    // CCK, CPU IPL through the existing Paula 4-CCK path. Disk-specific
    // physical propagation still requires a hardware-backed timing oracle.
    internal void LatchDiskSync(long cycle)
        => LatchHardwareInterrupt(0x1000, cycle, cpuVisibilityDelay: 8);

    // H6b2 completion visibility is provisional; see its timing defect register.
    internal void LatchDiskBlock(long cycle)
        => LatchHardwareInterrupt(0x0002, cycle, cpuVisibilityDelay: 0);

    internal void LatchDiskIndex(long cycle)
        => HandleCiaInterrupt(ciaA: false, _ciaB.LatchFlag(cycle));

    internal void LatchAudioInterrupt(int channel, long cycle)
        => LatchHardwareInterrupt(
            LightweightPaulaAudio.GetInterruptBit(channel),
            cycle,
            cpuVisibilityDelay: 4 * LightweightClock.CpuCyclesPerColorClock);

    private void ScheduleInterruptRequestVisibility(ushort bits, long cycle)
    {
        cycle = Math.Max(_clock.Cycle, cycle);
        bits &= 0x3FFF;
        if (bits == 0)
            return;
        for (var bit = 0; bit < _interruptRequestVisibilityCycles.Length; bit++)
        {
            if ((bits & (1 << bit)) != 0)
            {
                _interruptRequestVisibilityCycles[bit] = Math.Min(
                    _interruptRequestVisibilityCycles[bit],
                    cycle);
            }
        }

        _nextInterruptPinEvaluationCycle = Math.Min(
            _nextInterruptPinEvaluationCycle,
            cycle);
        _nextDeviceCycle = Math.Min(_nextDeviceCycle, cycle);
    }

    private void ScheduleInterruptEnableVisibility(long cycle)
    {
        _interruptEnableVisibilityCycle = Math.Max(_clock.Cycle, cycle);
        _nextInterruptPinEvaluationCycle = Math.Min(
            _nextInterruptPinEvaluationCycle,
            _interruptEnableVisibilityCycle);
        _nextDeviceCycle = Math.Min(
            _nextDeviceCycle,
            _interruptEnableVisibilityCycle);
    }

    private void ApplyPendingInterruptVisibility(long cycle)
    {
        if (_interruptEnableVisibilityCycle <= cycle)
        {
            _cpuVisibleIntena = _registers.Intena;
            _interruptEnableVisibilityCycle = long.MaxValue;
        }

        for (var bit = 0; bit < _interruptRequestVisibilityCycles.Length; bit++)
        {
            if (_interruptRequestVisibilityCycles[bit] > cycle)
                continue;
            var mask = (ushort)(1 << bit);
            if ((_registers.Intreq & mask) != 0)
                _cpuVisibleIntreq |= mask;
            else
                _cpuVisibleIntreq &= (ushort)~mask;
            _interruptRequestVisibilityCycles[bit] = long.MaxValue;
        }

        _nextInterruptPinEvaluationCycle = _interruptEnableVisibilityCycle;
        for (var bit = 0; bit < _interruptRequestVisibilityCycles.Length; bit++)
        {
            _nextInterruptPinEvaluationCycle = Math.Min(
                _nextInterruptPinEvaluationCycle,
                _interruptRequestVisibilityCycles[bit]);
        }

        UpdateInterruptPin(cycle);
    }

    private void RefreshNextDeviceCycle(bool afterDeviceTick = false)
    {
        var next = _video.NextCycle;
        if (!afterDeviceTick || next == long.MaxValue)
        {
            if (_bitplanes.NextCycle < next) next = _bitplanes.NextCycle;
            if (_spriteDma.NextCycle < next) next = _spriteDma.NextCycle;
            if (_copper.NextCycle < next) next = _copper.NextCycle;
            if (_blitter.NextCycle < next) next = _blitter.NextCycle;
            if (_sprites.NextCycle < next) next = _sprites.NextCycle;
            if (_paula.NextCycle < next) next = _paula.NextCycle;
            if (_diskSerial.NextCycle < next) next = _diskSerial.NextCycle;
            if (_diskDma.NextCycle < next) next = _diskDma.NextCycle;
        }
        else
        {
            System.Diagnostics.Debug.Assert(_bitplanes.NextCycle >= next);
            System.Diagnostics.Debug.Assert(_spriteDma.NextCycle >= next);
            System.Diagnostics.Debug.Assert(_copper.NextCycle >= next);
            System.Diagnostics.Debug.Assert(_blitter.NextCycle >= next);
            System.Diagnostics.Debug.Assert(_sprites.NextCycle >= next);
            System.Diagnostics.Debug.Assert(_paula.NextCycle >= next);
            System.Diagnostics.Debug.Assert(_diskSerial.NextCycle >= next);
            System.Diagnostics.Debug.Assert(_diskDma.NextCycle >= next);
        }

        if (_nextVerticalBlankLatchCycle < next)
            next = _nextVerticalBlankLatchCycle;
        if (_nextCiaInterruptCycle < next)
            next = _nextCiaInterruptCycle;
        if (_nextInterruptPinEvaluationCycle < next)
            next = _nextInterruptPinEvaluationCycle;
        _nextDeviceCycle = next;
    }

    private void UpdateInterruptPin(long cycle)
    {
        var level = LightweightRegisters.GetHighestEnabledInterruptLevel(
            _cpuVisibleIntena,
            _cpuVisibleIntreq);
        if (level == _interruptPinLevel)
            return;
        _interruptPinLevel = level;
        _interruptPinChangeCycle = cycle;
    }

    private ushort ReadWordRaw(uint address)
    {
        address &= 0x00FF_FFFFu;
        if (_overlayEnabled && address + 1 < 0x400)
            return ReadRomWord(_romImageOffset + (int)address);
        if (address + 1 < _chipRam.Length)
            return (ushort)((_chipRam[address] << 8) | _chipRam[address + 1]);
        if (address >= 0x00C00000 && address + 1 < 0x00C00000 + _slowRam.Length)
        {
            var offset = (int)(address - 0x00C00000);
            return (ushort)((_slowRam[offset] << 8) | _slowRam[offset + 1]);
        }
        if (address >= CustomBase && address < CustomBase + 0x200)
        {
            var offset = (ushort)(address - CustomBase);
            return offset switch
            {
                LightweightRegisters.Vposr => (ushort)((_clock.IsLongField ? 0x8000 : 0) |
                    ((_clock.Line >> 8) & 1)),
                LightweightRegisters.Vhposr => (ushort)(((_clock.Line & 0xFF) << 8) |
                    ((_clock.ColorClock + 4) % (LightweightClock.CpuCyclesPerLine /
                        LightweightClock.CpuCyclesPerColorClock))),
                LightweightRegisters.Dmaconr =>
                    (ushort)(_registers.Dmacon | _blitter.StatusBits),
                LightweightRegisters.Dskbytr => _diskSerial.ReadByteStatus(_registers),
                0x00A => _controllers.ReadJoy(port1: false),
                0x00C => _controllers.ReadJoy(port1: true),
                0x012 or 0x014 => ReadUnsupportedPotCounter(offset),
                0x016 => _controllers.ReadPotgor(_registers.Read(0x034)),
                _ => _registers.Read(offset)
            };
        }
        if (address >= RomBase && address + 1 < RomBase + _rom.Length)
            return ReadRomWord((int)(address - RomBase));
        return 0xFFFF;
    }

    private ushort ReadWordAt(uint address, long cycle)
    {
        StrobeCopperRead(address, cycle);
        if (TryDecodeCiaRegister(address, out var ciaA, out var register))
        {
            var value = ReadCiaRegister(ciaA, register, cycle);
            return (ushort)((value << 8) | 0x00FF);
        }

        if (TryDecodeCiaRegister(address + 1, out ciaA, out register))
        {
            var value = ReadCiaRegister(ciaA, register, cycle);
            return (ushort)(0xFF00 | value);
        }

        return ReadWordRaw(address);
    }

    private ushort ReadUnsupportedPotCounter(ushort offset)
    {
        ReportUnsupportedFeature("analog POT counter measurement");
        return _registers.Read(offset);
    }

    private byte ReadByteAt(uint address, long cycle)
    {
        StrobeCopperRead(address, cycle);
        if (TryDecodeCiaRegister(address, out var ciaA, out var register))
            return ReadCiaRegister(ciaA, register, cycle);
        var word = ReadWordRaw(address & ~1u);
        return (address & 1) == 0 ? (byte)(word >> 8) : (byte)word;
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void StrobeCopperRead(uint address, long cycle)
    {
        // COPJMP1/2 are address strobes on reads as well as writes. Keep peeks
        // and the byte-write merge's raw read side-effect-free.
        if ((address & 0x00FF_FFFCu) != CustomBase + LightweightRegisters.Copjmp1)
            return;
        _copper.OnRegisterWrite((ushort)(LightweightRegisters.Copjmp1 + (address & 2)), cycle, this);
        RefreshNextDeviceCycle();
    }

    private ushort ReadRomWord(int offset)
    {
        if (!_romLoaded || offset < _romImageOffset || offset + 1 >= _romImageOffset + _romImageLength)
            return 0xFFFF;
        return (ushort)((_rom[offset] << 8) | _rom[offset + 1]);
    }

    private void WriteByteRaw(uint address, byte value, long cycle)
    {
        if (TryDecodeCiaRegister(address, out var ciaA, out var register))
        {
            WriteCiaRegister(ciaA, register, value, cycle);
            return;
        }

        var aligned = address & ~1u;
        if (IsCiaWordAccess(aligned))
            return;
        var word = ReadWordRaw(aligned);
        word = (address & 1) == 0
            ? (ushort)((word & 0x00FF) | (value << 8))
            : (ushort)((word & 0xFF00) | value);
        WriteWordRaw(aligned, word, cycle);
    }

    private void WriteWordRaw(uint address, ushort value, long cycle = 0)
    {
        address &= 0x00FF_FFFFu;
        if (TryDecodeCiaRegister(address, out var ciaA, out var register))
        {
            WriteCiaRegister(ciaA, register, (byte)(value >> 8), cycle);
            return;
        }
        if (TryDecodeCiaRegister(address + 1, out ciaA, out register))
        {
            WriteCiaRegister(ciaA, register, (byte)value, cycle);
            return;
        }
        if (_overlayEnabled && address + 1 < 0x400)
            return;
        if (address + 1 < _chipRam.Length)
        {
            _chipRam[address] = (byte)(value >> 8);
            _chipRam[address + 1] = (byte)value;
            return;
        }
        if (address >= 0x00C00000 && address + 1 < 0x00C00000 + _slowRam.Length)
        {
            var offset = (int)(address - 0x00C00000);
            _slowRam[offset] = (byte)(value >> 8);
            _slowRam[offset + 1] = (byte)value;
            return;
        }
        if (address >= CustomBase && address < CustomBase + 0x200)
        {
            WriteCustomRegister((ushort)(address - CustomBase), value, cycle);
        }
    }

    private void WriteCustomRegister(ushort offset, ushort value, long cycle)
    {
        offset &= 0x01FE;
        if (offset == 0x036) _controllers.WriteJoytest(value);
        if (offset == LightweightRegisters.Vposw)
        {
            _clock.SelectLongField((value & 0x8000) != 0);
            _registers.Write(offset, value);
            RefreshCiaInterruptCycle();
            return;
        }

        var previousAdkcon = _registers.Adkcon;
        var previousDmacon = offset == LightweightRegisters.DmaconWrite
            ? _registers.Dmacon
            : (ushort)0;
        var previousIntreq = offset == LightweightRegisters.IntreqWrite
            ? _registers.Intreq
            : (ushort)0;
        _registers.Write(offset, value);

        var refreshDeviceCycle = true;
        if (offset >= LightweightRegisters.ColorFirst &&
            offset <= LightweightRegisters.ColorLast)
        {
            _video.OnRegisterWrite(offset, value, cycle);
        }
        else if (offset is LightweightRegisters.Copjmp1 or
            LightweightRegisters.Copjmp2)
        {
            _copper.OnRegisterWrite(offset, cycle, this);
        }
        else if (offset >= LightweightRegisters.SpritePosFirst &&
            offset <= LightweightRegisters.SpriteRegisterLast)
        {
            if (_sprites.OnRegisterWrite(offset, value, cycle))
            {
                _video.ActivateForSprite(cycle);
            }
        }
        else if (offset >= LightweightRegisters.SpritePointerFirst &&
            offset <= LightweightRegisters.SpritePointerLast)
        {
            _spriteDma.OnRegisterWrite(offset, cycle, this);
        }
        else if (offset >= LightweightRegisters.BplPointerFirst &&
            offset <= LightweightRegisters.BplPointerLast)
        {
            _bitplanes.OnRegisterWrite(offset, value, cycle, this);
        }
        else if (offset >= LightweightRegisters.BpldatFirst &&
            offset <= LightweightRegisters.BpldatLast)
        {
            _video.OnRegisterWrite(offset, value, cycle);
        }
        else if (offset >= LightweightRegisters.Aud0lch &&
            offset <= LightweightRegisters.Aud3dat)
        {
            _paula.OnRegisterWrite(offset, value, cycle, this);
        }
        else
        {
            switch (offset)
            {
                case LightweightRegisters.Dsklen:
                    _diskDma.WriteLength(value, cycle, this);
                    break;
                case LightweightRegisters.DmaconWrite:
                    _diskDma.OnDmaconChanged(cycle, this);
                    _bitplanes.OnRegisterWrite(offset, value, cycle, this);
                    _video.OnRegisterWrite(offset, value, cycle);
                    _copper.OnDmaconChanged(
                        previousDmacon,
                        _registers.Dmacon,
                        cycle,
                        this);
                    _blitter.OnDmaconChanged(
                        previousDmacon,
                        _registers.Dmacon,
                        cycle,
                        this);
                    _spriteDma.OnDmaconChanged(
                        previousDmacon,
                        _registers.Dmacon,
                        cycle);
                    _paula.OnDmaconChanged(
                        previousDmacon,
                        _registers.Dmacon,
                        cycle,
                        this);
                    break;
                case LightweightRegisters.AdkconWrite:
                    _paula.OnAdkconChanged(_registers.Adkcon, cycle);
                    _diskDma.OnAdkconChanged(previousAdkcon, cycle, this);
                    break;
                case LightweightRegisters.Dsksync:
                    _diskSerial.CompareSync(cycle, this);
                    break;
                case LightweightRegisters.Bplcon0:
                    _bitplanes.OnRegisterWrite(offset, value, cycle, this);
                    _video.OnRegisterWrite(offset, value, cycle);
                    break;
                case LightweightRegisters.Bplcon1:
                case LightweightRegisters.Bplcon2:
                    _video.OnRegisterWrite(offset, value, cycle);
                    break;
                case LightweightRegisters.Diwstrt:
                case LightweightRegisters.Diwstop:
                    _bitplanes.OnRegisterWrite(offset, value, cycle, this);
                    _video.OnRegisterWrite(offset, value, cycle);
                    break;
                case LightweightRegisters.Ddfstrt:
                case LightweightRegisters.Ddfstop:
                    _bitplanes.OnRegisterWrite(offset, value, cycle, this);
                    break;
                case LightweightRegisters.Bltsize:
                    _blitter.OnRegisterWrite(offset, value, cycle, this);
                    break;
                case LightweightRegisters.IntenaWrite:
                    ScheduleInterruptEnableVisibility(
                        cycle + LightweightClock.CpuCyclesPerColorClock);
                    refreshDeviceCycle = false;
                    break;
                case LightweightRegisters.IntreqWrite:
                    // A CIA may fire again between the handler's ICR read
                    // and its Paula acknowledgement. A still-asserted /IRQ
                    // must survive that clear; there need not be another edge.
                    if ((value & 0x8000) == 0 && (value & 0x2008) != 0)
                    {
                        ushort held = 0;
                        if (_ciaA.InterruptAsserted) held |= 0x0008;
                        if (_ciaB.InterruptAsserted) held |= 0x2000;
                        _registers.SetHardwareInterruptRequest((ushort)(held & value));
                    }
                    ScheduleInterruptRequestVisibility(
                        (ushort)(previousIntreq ^ _registers.Intreq),
                        cycle + LightweightClock.CpuCyclesPerColorClock);
                    refreshDeviceCycle = false;
                    break;
                default:
                    refreshDeviceCycle = false;
                    break;
            }
        }

        if (refreshDeviceCycle)
        {
            RefreshNextDeviceCycle();
        }
    }

    private byte ReadCiaRegister(bool ciaA, int register, long cycle)
    {
        var cia = ciaA ? _ciaA : _ciaB;
        var pins = ciaA && register == 0 ? _floppy.ReadInputPins(cycle) : (byte)0xFF;
        if (ciaA && register == 0) pins &= _controllers.FirePins;
        var value = cia.ReadRegister(register, pins, cycle, out var interruptCycle);
        HandleCiaInterrupt(ciaA, interruptCycle);
        RefreshCiaInterruptCycle();
        return value;
    }

    private void WriteCiaRegister(bool ciaA, int register, byte value, long cycle)
    {
        var cia = ciaA ? _ciaA : _ciaB;
        cia.WriteRegister(register, value, cycle, out var interruptCycle);
        HandleCiaInterrupt(ciaA, interruptCycle);
        if (ciaA && register is 0x0C or 0x0E)
        {
            _keyboard.HostDataChanged(_ciaA.SerialSpHigh, cycle);
        }
        if (ciaA && register is 0x00 or 0x02)
            UpdateOverlayFromCiaA();
        if (!ciaA && register is 0x01 or 0x03)
        {
            var direction = _ciaB.ReadDataDirection(1);
            var pins = (byte)((_ciaB.ReadPortLatch(1) & direction) | (0xFF & ~direction));
            if (!_floppy.WriteControlPins(pins, cycle))
                ReportUnsupportedFeature("DF0 seek beyond the standard ADF cylinder range");
            _diskSerial.OnDriveChanged(_floppy, cycle);
        }
        RefreshCiaInterruptCycle();
    }

    private void AdvanceCiasTo(long cycle)
    {
        HandleCiaInterrupt(ciaA: true, _ciaA.AdvanceTo(cycle));
        HandleCiaInterrupt(ciaA: false, _ciaB.AdvanceTo(cycle));
        if (cycle == _keyboard.NextCycle) _keyboard.Step(cycle, _ciaA, this);
        RefreshCiaInterruptCycle();
    }

    internal void ReceiveKeyboardBit(bool high, long cycle)
    {
        if (_ciaA.UsesUnsupportedKeyboardCntMode)
            ReportUnsupportedFeature("keyboard CNT pulses used with unimplemented CIA external timer mode");
        HandleCiaInterrupt(ciaA: true, _ciaA.ReceiveSerialBit(high, cycle));
    }

    private void HandleCiaInterrupt(bool ciaA, long interruptCycle)
    {
        if ((ciaA ? _ciaA : _ciaB).SerialOutputClockReached)
            ReportUnsupportedFeature("clocked CIA serial output transmission");
        if (interruptCycle == long.MaxValue)
            return;
        LatchHardwareInterrupt(
            ciaA ? (ushort)0x0008 : (ushort)0x2000,
            interruptCycle,
            cpuVisibilityDelay: 8);
    }

    private void RefreshCiaInterruptCycle()
    {
        _nextCiaInterruptCycle = Math.Min(
            _ciaA.GetNextActiveInterruptCycle(),
            _ciaB.GetNextActiveInterruptCycle());
        _nextCiaInterruptCycle = Math.Min(_nextCiaInterruptCycle,
            _ciaA.GetNextTodInterruptCycle(_clock.NextFrameCycle, 0));
        _nextCiaInterruptCycle = Math.Min(_nextCiaInterruptCycle,
            _ciaB.GetNextTodInterruptCycle(_clock.NextLineCycle, LightweightClock.CpuCyclesPerLine));
        _nextCiaInterruptCycle = Math.Min(_nextCiaInterruptCycle, _keyboard.NextCycle);
        RefreshNextDeviceCycle();
    }

    private void UpdateOverlayFromCiaA()
    {
        var latch = _ciaA.ReadPortLatch(0);
        var direction = _ciaA.ReadDataDirection(0);
        var outputPins = (byte)((latch & direction) | (0xFF & ~direction));
        _overlayEnabled = _romLoaded && (outputPins & 1) != 0;
    }

    private static bool TryDecodeCiaRegister(
        uint address,
        out bool ciaA,
        out int register)
    {
        address &= 0x00FF_FFFFu;
        if (address >= 0x00BFE001 && address <= 0x00BFEF01 &&
            (address & 0xFF) == 0x01)
        {
            ciaA = true;
            register = (int)((address >> 8) & 0x0F);
            return true;
        }

        if (address >= 0x00BFD000 && address <= 0x00BFDF00 &&
            (address & 0xFF) == 0)
        {
            ciaA = false;
            register = (int)((address >> 8) & 0x0F);
            return true;
        }

        ciaA = false;
        register = 0;
        return false;
    }

    private uint ReadLongRaw(uint address)
        => ((uint)ReadWordRaw(address) << 16) | ReadWordRaw(address + 2);

    private void InstallResetLoop()
    {
        _chipRam[0x1000] = 0x4E;
        _chipRam[0x1001] = 0x71;
        _chipRam[0x1002] = 0x60;
        _chipRam[0x1003] = 0xFC;
        WriteLongRaw(0, (uint)(_chipRam.Length - 4));
        WriteLongRaw(4, 0x1000);
    }

    private void WriteLongRaw(uint address, uint value)
    {
        WriteWordRaw(address, (ushort)(value >> 16));
        WriteWordRaw(address + 2, (ushort)value);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LightweightA500Machine));
    }
}

/// <summary>Applied once at submission on the hardware owner thread. Joystick
/// bits: up=1, down=2, left=4, right=8, fire=16, second fire=32; port 0 bit 7
/// selects a joystick instead of the default mouse. Mouse buttons: left=1,
/// right=2, middle=4. Deltas are relative counts. KeyCode is 0 for no event or
/// 0x100 | raw key byte (bit 7 release), so raw key zero remains representable.</summary>
public readonly record struct LightweightInputState(
    byte JoystickPort0,
    byte JoystickPort1,
    byte MouseButtons,
    short MouseDeltaX,
    short MouseDeltaY,
    ushort KeyCode);

public readonly record struct LightweightDriveState(
    int Cylinder, int Head, bool MotorOn, bool Selected, bool ActiveDma);
