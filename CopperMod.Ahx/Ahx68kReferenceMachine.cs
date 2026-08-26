using System.Diagnostics;
using System.Globalization;
using System.Text;
using Copper68k;
using CopperMod.Abstractions;
using CopperMod.Amiga;
using CopperMod.Amiga.Diagnostics;

namespace CopperMod.Ahx;

internal enum Ahx68kEntryPoint : uint
{
    InitCia = 0,
    InitPlayer = 4,
    InitModule = 8,
    InitSubSong = 12,
    Interrupt = 16,
    Stop = 20,
    KillPlayer = 24,
    KillCia = 28,
    NextPattern = 32,
    PreviousPattern = 36
}

internal enum AhxTraceKind
{
    Routine,
    CustomWrite,
    PaulaDmaRead,
    ChannelState,
    PublicState
}

internal readonly record struct AhxTraceEvent(
    long Cycle,
    AhxTraceKind Kind,
    string Source,
    ushort Register,
    uint Value,
    int Channel,
    string State)
{
    public string ToStableText()
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{Cycle}|{Kind}|{Source}|{Register:X3}|{Value:X8}|{Channel}|{State}");
}

internal readonly record struct Ahx68kCallResult(long StartCycle, long EndCycle, uint D0, uint D1, int Instructions);

/// <summary>
/// Hosts the original AHX 2.3d 68000 binary on Copper68k and the cycle-aware
/// Amiga bus. The binary is supplied by the caller and is never embedded here.
/// </summary>
internal sealed class Ahx68kReferenceMachine : IDisposable
{
    internal const int PublicMemorySize = 412_150;
    internal const int ChipMemorySize = 2_560;
    internal const uint PlayerBase = 0x00C0_0000;
    internal const uint TempoCallbackAddress = 0x00C0_3000;
    internal const uint TempoWordAddress = 0x00C0_3100;
    internal const uint PublicMemoryAddress = 0x00C1_0000;
    internal const uint ModuleAddress = 0x00C8_0000;
    internal const uint ChipMemoryAddress = 0x0001_0000;
    internal const uint StackTopAddress = 0x00CF_F000;
    private const uint DmaconAddress = 0x00DF_F096;
    private const uint ReturnAddress = 0xFFFF_FFFC;
    private const int ExpansionRamSize = 0x0010_0000;
    private const int DefaultInstructionBudget = 2_000_000;
    private const long DefaultCycleBudget = 40_000_000;
    private const int InitPlayerInstructionBudget = 30_000_000;
    private const long InitPlayerCycleBudget = 500_000_000;
    private const int WallClockBudgetMilliseconds = 30_000;
    private const int GuardSize = 32;
    internal const int TraceEventCapacity = 65_536;

    private readonly Machine _machine;
    private readonly List<AhxTraceEvent> _trace = new();
    private readonly List<GuardRegion> _guards = new();
    private readonly int _playerLength;
    private readonly int _moduleLength;
    private readonly bool _traceCaptureAvailable;
    private int _callIndex;
    private long _nextCiaCycle;
    private long _lastTraceCycle;
    private long _renderCycle;
    private long _sampleCycleRemainder;
    private int _renderSampleRate;
    private bool _hostInitialized;
    private bool _disposed;
    private bool _traceEnabled;

    public Ahx68kReferenceMachine(
        ReadOnlySpan<byte> player,
        ReadOnlySpan<byte> module,
        int moduleCapacity = 0,
        bool enableTracing = false)
    {
        if (player.Length < 40)
        {
            throw new ArgumentException("The AHX player must contain the ten-entry jump table.", nameof(player));
        }

        if (player.Length > (int)(TempoCallbackAddress - PlayerBase))
        {
            throw new ArgumentException("The AHX player overlaps the reserved tempo callback.", nameof(player));
        }

        var requestedModuleCapacity = Math.Max(module.Length, moduleCapacity);
        if (module.IsEmpty || requestedModuleCapacity > (int)(StackTopAddress - ModuleAddress) - GuardSize)
        {
            throw new ArgumentException("The AHX module does not fit in the reference memory map.", nameof(module));
        }

        ValidateJumpTable(player);
        _playerLength = player.Length;
        _moduleLength = requestedModuleCapacity;
        _traceCaptureAvailable = enableTracing;
        _traceEnabled = enableTracing;
        _machine = new Machine(
            MachineOptions
                .ForProfile(MachineProfile.A500PalCustPlayback)
                .WithExpansionRam(ExpansionRamSize)
                .WithBusAccessLogging(enableTracing)
                .WithLiveDisplayDma(false)
                .WithCpu(M68kCoreFactory.Default, M68kBackendKind.AccurateM68000));
        _machine.ResetHardware();
        EnableHostDmaMaster();
        CopyToHostMemory(PlayerBase, player);
        CopyToHostMemory(ModuleAddress, module);
        InstallTempoCallback();
        InstallGuards();
        _machine.Cpu.Reset(PlayerBase, StackTopAddress);
    }

    public IReadOnlyList<AhxTraceEvent> Trace => _trace;

    public bool TraceEnabled
    {
        get => _traceEnabled;
        set
        {
            if (value && !_traceCaptureAvailable)
            {
                throw new InvalidOperationException(
                    "AHX tracing must be enabled when the reference machine is constructed.");
            }

            _traceEnabled = value;
        }
    }

    public long Cycle => _machine.Cpu.State.Cycles;

    public ushort TempoWord => _machine.Bus.ReadHostWord(TempoWordAddress);

    public long CiaIntervalCycles => Math.Max(1, ((long)TempoWord + 1) * 10);

    public int SubSongCount => ReadPublicByte(2) + 1;

    public bool SongEnded => ReadPublicByte(3) != 0;

    public bool Playing => ReadPublicByte(4) != 0;

    public bool AudioFilterEnabled => _machine.Bus.AudioFilterEnabled;

    public ModuleChannelWaveform? LastChannelWaveform { get; private set; }

    public byte ReadPublicByte(int offset)
    {
        if ((uint)offset >= PublicMemorySize)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        return _machine.Bus.ReadHostByte(PublicMemoryAddress + (uint)offset);
    }

    public Ahx68kCallResult Initialize(int subSong = 0)
    {
        ThrowIfDisposed();
        InitializeHost();
        return InitializeLoadedModule(subSong);
    }

    public void InitializeHost()
    {
        ThrowIfDisposed();
        if (_hostInitialized)
        {
            return;
        }

        var cia = Call(
            Ahx68kEntryPoint.InitCia,
            state =>
            {
                state.D[0] = 1;
                state.A[0] = TempoCallbackAddress;
            });
        EnsureSuccess(cia, Ahx68kEntryPoint.InitCia);

        var player = Call(
            Ahx68kEntryPoint.InitPlayer,
            state =>
            {
                state.A[0] = PublicMemoryAddress;
                state.A[1] = ChipMemoryAddress;
                state.D[0] = 1;
                state.D[1] = 0;
            },
            cycleBudget: InitPlayerCycleBudget,
            instructionBudget: InitPlayerInstructionBudget);
        EnsureSuccess(player, Ahx68kEntryPoint.InitPlayer);

        _hostInitialized = true;
    }

    public Ahx68kCallResult LoadModule(ReadOnlySpan<byte> module, int subSong = 0)
    {
        ThrowIfDisposed();
        if (!_hostInitialized)
        {
            throw new InvalidOperationException("InitializeHost must complete before loading an AHX module.");
        }

        if (module.IsEmpty || module.Length > _moduleLength)
        {
            throw new ArgumentException("The AHX module does not fit in the allocated guest module block.", nameof(module));
        }

        for (var i = 0; i < _moduleLength; i++)
        {
            _machine.Bus.WriteHostByte(ModuleAddress + (uint)i, i < module.Length ? module[i] : (byte)0);
        }

        return InitializeLoadedModule(subSong);
    }

    private Ahx68kCallResult InitializeLoadedModule(int subSong)
    {
        ClearRenderState();

        var module = Call(
            Ahx68kEntryPoint.InitModule,
            state => state.A[0] = ModuleAddress);
        EnsureSuccess(module, Ahx68kEntryPoint.InitModule);

        var subSongResult = Call(
            Ahx68kEntryPoint.InitSubSong,
            state =>
            {
                state.D[0] = unchecked((uint)subSong);
                state.D[1] = 0;
            });
        _nextCiaCycle = subSongResult.EndCycle + CiaIntervalCycles;
        _renderCycle = subSongResult.EndCycle;
        return subSongResult;
    }

    public Ahx68kCallResult Interrupt()
    {
        if (_nextCiaCycle == 0)
        {
            throw new InvalidOperationException("Initialize must complete before the first AHX interrupt.");
        }

        AdvanceIdleTo(_nextCiaCycle);
        var result = Call(Ahx68kEntryPoint.Interrupt);
        _nextCiaCycle += CiaIntervalCycles;
        return result;
    }

    public Ahx68kCallResult Stop()
        => Call(Ahx68kEntryPoint.Stop);

    public void RenderFrames(Span<float> destination, int frames, int channels, int sampleRate, bool captureChannels)
    {
        ThrowIfDisposed();
        if (frames < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frames));
        }

        if (channels <= 0 || destination.Length < checked(frames * channels))
        {
            throw new ArgumentException("The destination is too small for the requested AHX frames.", nameof(destination));
        }

        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        if (frames == 0)
        {
            return;
        }

        if (_nextCiaCycle == 0)
        {
            throw new InvalidOperationException("Initialize must complete before rendering AHX audio.");
        }

        if (_renderSampleRate != sampleRate)
        {
            _renderSampleRate = sampleRate;
            _sampleCycleRemainder = 0;
            _renderCycle = Math.Max(_renderCycle, Cycle);
        }

        var targets = new long[frames];
        var rendered = new float[checked(frames * channels)];
        for (var i = 0; i < targets.Length; i++)
        {
            _sampleCycleRemainder += AmigaConstants.A500PalCpuCyclesPerSecond;
            _renderCycle += _sampleCycleRemainder / sampleRate;
            _sampleCycleRemainder %= sampleRate;
            targets[i] = _renderCycle;
        }

        if (captureChannels)
        {
            _machine.Bus.Paula.BeginChannelCapture(frames, sampleRate);
        }

        var frame = 0;
        while (frame < frames)
        {
            if (targets[frame] <= _nextCiaCycle)
            {
                RenderObservation(targets[frame], rendered, frame, channels);
                frame++;
                continue;
            }

            AdvanceIdleTo(_nextCiaCycle);
            Call(
                Ahx68kEntryPoint.Interrupt,
                observeInstruction: (_, instructionEndCycle) =>
                {
                    while (frame < frames && targets[frame] <= instructionEndCycle)
                    {
                        RenderObservation(targets[frame], rendered, frame, channels);
                        frame++;
                    }
                });
            _nextCiaCycle += CiaIntervalCycles;
        }

        LastChannelWaveform = captureChannels
            ? ConvertWaveform(_machine.Bus.Paula.FinishChannelCapture())
            : null;
        rendered.AsSpan().CopyTo(destination);
    }

    public void Shutdown()
    {
        Stop();
        Call(Ahx68kEntryPoint.KillPlayer);
        Call(Ahx68kEntryPoint.KillCia);
    }

    public void ValidateGuards()
    {
        foreach (var guard in _guards)
        {
            for (var i = 0; i < guard.Length; i++)
            {
                var actual = _machine.Bus.ReadHostByte(guard.Address + (uint)i);
                if (actual != guard.Value)
                {
                    throw new InvalidOperationException(
                        $"AHX guard '{guard.Name}' changed at 0x{guard.Address + (uint)i:X8}: expected 0x{guard.Value:X2}, actual 0x{actual:X2}.");
                }
            }
        }
    }

    public string GetStableTraceText()
    {
        var result = new StringBuilder(_trace.Count * 48);
        foreach (var entry in _trace)
        {
            result.AppendLine(entry.ToStableText());
        }

        return result.ToString();
    }

    public void ClearTrace()
    {
        _trace.Clear();
        _lastTraceCycle = Cycle;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _machine.Dispose();
    }

    private Ahx68kCallResult Call(
        Ahx68kEntryPoint entryPoint,
        Action<M68kCpuState>? prepare = null,
        long cycleBudget = DefaultCycleBudget,
        int instructionBudget = DefaultInstructionBudget,
        Action<long, long>? observeInstruction = null)
    {
        ThrowIfDisposed();
        var cpu = _machine.Cpu;
        var startCycle = cpu.State.Cycles;
        var callIndex = _callIndex++;
        cpu.BeginSubroutine(PlayerBase + (uint)entryPoint, StackTopAddress, ReturnAddress);
        prepare?.Invoke(cpu.State);
        var instructions = 0;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            while (!cpu.State.Halted &&
                   cpu.State.ProgramCounter != ReturnAddress &&
                   cpu.State.Cycles - startCycle < cycleBudget &&
                   instructions < instructionBudget &&
                   stopwatch.ElapsedMilliseconds < WallClockBudgetMilliseconds)
            {
                var instructionStartCycle = cpu.State.Cycles;
                cpu.ExecuteInstruction();
                observeInstruction?.Invoke(instructionStartCycle, cpu.State.Cycles);
                AdvanceHardwareTo(cpu.State.Cycles);
                instructions++;
            }
        }
        catch (Exception ex) when (ex is UnsupportedM68kOpcodeException or AmigaEmulationException)
        {
            throw new InvalidOperationException(
                $"AHX 68k call {entryPoint} faulted at cycle {cpu.State.Cycles}, PC 0x{cpu.State.LastInstructionProgramCounter:X8}, opcode 0x{cpu.State.LastOpcode:X4}.",
                ex);
        }

        if (cpu.State.ProgramCounter != ReturnAddress)
        {
            throw new InvalidOperationException(
                $"AHX 68k call {entryPoint} exceeded its execution budget at cycle {cpu.State.Cycles}, PC 0x{cpu.State.LastInstructionProgramCounter:X8}, opcode 0x{cpu.State.LastOpcode:X4}.");
        }

        AdvanceHardwareTo(cpu.State.Cycles);
        ValidateGuards();
        CaptureTrace(_lastTraceCycle, cpu.State.Cycles);
        _lastTraceCycle = cpu.State.Cycles;
        if (TraceEnabled)
        {
            _trace.Add(new AhxTraceEvent(
                cpu.State.Cycles,
                AhxTraceKind.Routine,
                entryPoint.ToString(),
                cpu.State.LastOpcode,
                unchecked((uint)callIndex),
                -1,
                $"entry={startCycle};return={cpu.State.Cycles};instructions={instructions};pc={cpu.State.ProgramCounter:X8}"));
            SortTrace();
        }
        return new Ahx68kCallResult(startCycle, cpu.State.Cycles, cpu.State.D[0], cpu.State.D[1], instructions);
    }

    private void CaptureTrace(long startCycle, long endCycle)
    {
        if (!TraceEnabled)
        {
            return;
        }

        var customRequests = new Dictionary<(long Cycle, ushort Register), long>();
        foreach (var access in _machine.Bus.BusAccesses)
        {
            if (access.Request.Kind == AmigaBusAccessKind.CustomRegister &&
                access.Request.IsWrite &&
                access.GrantedCycle > startCycle &&
                access.GrantedCycle <= endCycle)
            {
                customRequests[(access.GrantedCycle, (ushort)(access.Request.Address & 0x01FE))] = access.RequestedCycle;
            }
        }

        foreach (var write in _machine.Bus.CustomRegisterWrites)
        {
            if (write.Cycle <= startCycle || write.Cycle > endCycle)
            {
                continue;
            }

            customRequests.TryGetValue((write.Cycle, write.Address), out var requestedCycle);
            _trace.Add(new AhxTraceEvent(
                write.Cycle,
                AhxTraceKind.CustomWrite,
                "CPU",
                write.Address,
                write.Value,
                AudioChannelForRegister(write.Address),
                $"request={requestedCycle};grant={write.Cycle}"));
        }

        foreach (var access in _machine.Bus.BusAccesses)
        {
            if (access.Request.Kind != AmigaBusAccessKind.PaulaDma ||
                access.GrantedCycle <= startCycle ||
                access.GrantedCycle > endCycle)
            {
                continue;
            }

            _trace.Add(new AhxTraceEvent(
                access.GrantedCycle,
                AhxTraceKind.PaulaDmaRead,
                "Paula",
                0,
                _machine.Bus.ReadHostWord(access.Request.Address),
                access.Request.Channel,
                $"request={access.RequestedCycle};grant={access.GrantedCycle};complete={access.CompletedCycle};address={access.Request.Address - ChipMemoryAddress:X8}"));
        }

        for (var channel = 0; channel < 4; channel++)
        {
            var state = _machine.Bus.Paula.GetChannelSnapshot(channel);
            _trace.Add(new AhxTraceEvent(
                endCycle,
                AhxTraceKind.ChannelState,
                "Paula",
                0,
                unchecked((byte)state.CurrentSample),
                channel,
                $"dma={(state.DmaEnabled ? 1 : 0)};loc={state.Location - ChipMemoryAddress:X8};ptr={state.CurrentAddress - ChipMemoryAddress:X8};len={state.LengthWords};remaining={state.RemainingWords};per={state.Period};vol={state.Volume};word={state.DataWord:X4};low={(state.NextByteIsLow ? 1 : 0)};next={state.NextSampleCycle}"));
        }

        var publicState = ReadPublicState();
        _trace.Add(new AhxTraceEvent(
            endCycle,
            AhxTraceKind.PublicState,
            "AHX",
            0,
            publicState.ExternalTiming,
            -1,
            $"playing={publicState.Playing};end={publicState.SongEnd};subsongs={publicState.SubSongs};voices={publicState.Voices}"));
        SortTrace();
    }

    private void SortTrace()
    {
        _trace.Sort(static (left, right) =>
        {
            var cycle = left.Cycle.CompareTo(right.Cycle);
            if (cycle != 0)
            {
                return cycle;
            }

            var kind = left.Kind.CompareTo(right.Kind);
            if (kind != 0)
            {
                return kind;
            }

            var channel = left.Channel.CompareTo(right.Channel);
            if (channel != 0)
            {
                return channel;
            }

            var register = left.Register.CompareTo(right.Register);
            if (register != 0)
            {
                return register;
            }

            var value = left.Value.CompareTo(right.Value);
            if (value != 0)
            {
                return value;
            }

            var source = string.CompareOrdinal(left.Source, right.Source);
            return source != 0 ? source : string.CompareOrdinal(left.State, right.State);
        });

        if (_trace.Count > TraceEventCapacity)
        {
            _trace.RemoveRange(0, _trace.Count - TraceEventCapacity);
        }
    }

    private AhxPublicTraceState ReadPublicState()
    {
        var voices = new StringBuilder(128);
        ReadOnlySpan<int> offsets = [14, 246, 478, 710];
        for (var channel = 0; channel < offsets.Length; channel++)
        {
            if (channel != 0)
            {
                voices.Append(',');
            }

            var address = PublicMemoryAddress + (uint)offsets[channel];
            voices.Append(channel)
                .Append(':')
                .Append((_machine.Bus.ReadHostLong(address + 92) - ChipMemoryAddress).ToString("X8", CultureInfo.InvariantCulture))
                .Append('/')
                .Append(_machine.Bus.ReadHostWord(address + 100).ToString(CultureInfo.InvariantCulture))
                .Append('/')
                .Append(_machine.Bus.ReadHostWord(address + 102).ToString(CultureInfo.InvariantCulture));
        }

        return new AhxPublicTraceState(
            ReadPublicByte(0),
            ReadPublicByte(2),
            ReadPublicByte(3),
            ReadPublicByte(4),
            voices.ToString());
    }

    private void AdvanceIdleTo(long targetCycle)
    {
        var startCycle = _machine.Cpu.State.Cycles;
        if (targetCycle <= startCycle)
        {
            return;
        }

        AdvanceHardwareTo(targetCycle);
        _machine.Cpu.State.Cycles = targetCycle;
        CaptureTrace(_lastTraceCycle, targetCycle);
        _lastTraceCycle = targetCycle;
        ValidateGuards();
    }

    private void RenderObservation(long targetCycle, float[] destination, int frame, int channels)
    {
        if (targetCycle > _machine.Cpu.State.Cycles)
        {
            AdvanceHardwareTo(targetCycle);
            _machine.Cpu.State.Cycles = targetCycle;
        }
        else
        {
            _machine.Bus.AdvanceRasterTo(targetCycle);
        }

        _machine.Bus.Paula.RenderSample(targetCycle, destination, frame, channels);
    }

    private static ModuleChannelWaveform? ConvertWaveform(AmigaChannelWaveform? waveform)
    {
        if (waveform is null)
        {
            return null;
        }

        var channels = new ModuleChannelWaveformChannel[waveform.Channels.Count];
        for (var i = 0; i < channels.Length; i++)
        {
            var channel = waveform.Channels[i];
            channels[i] = new ModuleChannelWaveformChannel(channel.Index, channel.Samples, channel.IsActive);
        }

        return new ModuleChannelWaveform(channels, waveform.FrameCount, waveform.SampleRate);
    }

    private void AdvanceHardwareTo(long targetCycle)
    {
        _machine.Bus.AdvanceRasterTo(targetCycle);
        _machine.Bus.Paula.AdvanceTo(targetCycle);
    }

    private void CopyToHostMemory(uint address, ReadOnlySpan<byte> source)
    {
        for (var i = 0; i < source.Length; i++)
        {
            _machine.Bus.WriteHostByte(address + (uint)i, source[i]);
        }
    }

    private void InstallTempoCallback()
    {
        // move.w d0,(TempoWordAddress).l ; rts
        ReadOnlySpan<byte> callback =
        [
            0x33, 0xC0,
            (byte)((TempoWordAddress >> 24) & 0xFF),
            (byte)((TempoWordAddress >> 16) & 0xFF),
            (byte)((TempoWordAddress >> 8) & 0xFF),
            (byte)(TempoWordAddress & 0xFF),
            0x4E, 0x75
        ];
        CopyToHostMemory(TempoCallbackAddress, callback);
        _machine.Bus.WriteHostWord(TempoWordAddress, 0);
    }

    private void EnableHostDmaMaster()
    {
        _machine.Bus.WriteDeviceWord(
            AmigaBusRequester.Host,
            AmigaBusAccessKind.CustomRegister,
            DmaconAddress,
            0x8200,
            0);
        _machine.Bus.Paula.AdvanceTo(0);
    }

    private void InstallGuards()
    {
        AddGuard("player-after", PlayerBase + (uint)_playerLength, GuardSize, 0xA1);
        AddGuard("public-before", PublicMemoryAddress - GuardSize, GuardSize, 0xA2);
        AddGuard("public-after", PublicMemoryAddress + PublicMemorySize, GuardSize, 0xA3);
        AddGuard("module-before", ModuleAddress - GuardSize, GuardSize, 0xA4);
        AddGuard("module-after", ModuleAddress + (uint)_moduleLength, GuardSize, 0xA5);
        AddGuard("chip-before", ChipMemoryAddress - GuardSize, GuardSize, 0xA6);
        AddGuard("chip-after", ChipMemoryAddress + ChipMemorySize, GuardSize, 0xA7);
        AddGuard("stack-above", StackTopAddress, GuardSize, 0xA8);
    }

    private void AddGuard(string name, uint address, int length, byte value)
    {
        for (var i = 0; i < length; i++)
        {
            _machine.Bus.WriteHostByte(address + (uint)i, value);
        }

        _guards.Add(new GuardRegion(name, address, length, value));
    }

    private void ClearRenderState()
    {
        _nextCiaCycle = 0;
        _renderCycle = Cycle;
        _sampleCycleRemainder = 0;
        _renderSampleRate = 0;
        LastChannelWaveform = null;
    }

    private static int AudioChannelForRegister(ushort register)
        => register is >= 0x0A0 and <= 0x0DF ? (register - 0x0A0) / 0x10 : -1;

    private static void ValidateJumpTable(ReadOnlySpan<byte> player)
    {
        for (var offset = 0; offset < 40; offset += 4)
        {
            if (player[offset] != 0x60 || player[offset + 1] != 0x00)
            {
                throw new ArgumentException($"AHX jump-table entry at offset {offset} is not BRA.W.", nameof(player));
            }
        }
    }

    private static void EnsureSuccess(Ahx68kCallResult result, Ahx68kEntryPoint entryPoint)
    {
        if (result.D0 != 0)
        {
            throw new InvalidOperationException($"AHX 68k call {entryPoint} returned 0x{result.D0:X8}.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private readonly record struct GuardRegion(string Name, uint Address, int Length, byte Value);

    private readonly record struct AhxPublicTraceState(
        byte ExternalTiming,
        byte SubSongs,
        byte SongEnd,
        byte Playing,
        string Voices);
}
