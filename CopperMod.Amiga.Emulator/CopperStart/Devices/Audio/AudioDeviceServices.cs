using System;
using System.Collections.Generic;
using System.Numerics;
using Copper68k;
using CopperMod.Amiga.Bus;

namespace CopperMod.Amiga.CopperStart.Devices.Audio;

/// <summary>
/// Host implementation of the allocation half of the ROM-created
/// <c>audio.device</c>.  It deliberately owns a virtual four-channel mixer,
/// rather than programming Paula: direct Paula users therefore retain their
/// hardware state and DMA channels.
/// </summary>
internal sealed class AudioDeviceServices : IDisposable
{
    private const int DeviceListOffset = 0x15E;
    private const int NodeSuccessorOffset = 0x00;
    private const int NodeNameOffset = 0x0A;
    private const int NodeTypeOffset = 0x08;
    private const int LibraryOpenCountOffset = 0x20;
    private const int IoDeviceOffset = 0x14;
    private const int IoUnitOffset = 0x18;
    private const int IoCommandOffset = 0x1C;
    private const int IoFlagsOffset = 0x1E;
    private const int IoErrorOffset = 0x1F;
    private const int IoAudioAllocKeyOffset = 0x28;
    // 68k ABI aligns pointers and ULONGs to words, not four-byte boundaries.
    private const int IoAudioDataOffset = 0x2A;
    private const int IoAudioLengthOffset = 0x2E;
    private const int IoAudioPeriodOffset = 0x32;
    private const int IoAudioCyclesOffset = 0x36;
    private const int IoAudioWriteMessageOffset = 0x38;
    private const int IoActualOffset = 0x20;
    private const byte IoQuick = 0x01;
    private const byte IoErrOpenFail = 0xFF;
    private const byte IoErrNoCommand = 0xFD;
    private const byte IoErrAllocFailed = 0xF9;
    private const byte AudioNoAllocation = 0xF8;
    private const ushort CmdFlush = 8;
    private const ushort AdCmdFree = 9;
    private const ushort AdCmdSetPrec = 10;
    private const ushort AdCmdFinish = 11;
    private const ushort AdCmdPerVol = 12;
    private const ushort AdCmdLock = 13;
    private const ushort AdCmdWaitCycle = 14;
    private const ushort AdCmdAllocate = 32;
    private const ushort CmdRead = 2;
    private const ushort CmdReset = 1;
    private const ushort CmdStart = 6;
    private const ushort CmdStop = 7;
    private const ushort CmdWrite = 3;
    private const byte AdiofNowait = 0x40;
    private const byte AdiofSyncCycle = 0x20;
    private const byte AdiofWriteMessage = 0x80;

    private readonly AmigaBus _bus;
    private readonly Action<uint> _replyMessage;
    private readonly Action<string> _diagnostic;
    private readonly List<(uint Address, uint Token)> _gateways = new();
    private readonly ChannelOwner?[] _channels = new ChannelOwner?[4];
    private readonly Queue<uint> _pendingAllocations = new();
    private readonly ChannelPlayback?[] _playback = new ChannelPlayback?[4];
    private readonly bool[] _stopped = new bool[4];
    private readonly Queue<uint>[] _queuedWrites = { new(), new(), new(), new() };
    private readonly List<(uint Request, int Channel, long Deadline)> _waitCycles = new();
    private readonly uint[] _locks = new uint[4];
    private readonly List<(int Channel, long Deadline)> _pendingFinishes = new();
    private readonly List<(int Channel, ushort Period, ushort Volume, long Deadline)> _pendingPerVol = new();
    private ushort _nextAllocationKey = 1;
    private bool _allocationDeferredForLock;

    private sealed record ChannelOwner(ushort Key, sbyte Priority);
    private sealed record ChannelPlayback(uint Request, uint Data, uint Length, ushort Period, ushort Volume, long StartCycle, long DeadlineCycle, bool Infinite);

    public AudioDeviceServices(AmigaBus bus, Action<uint> replyMessage, Action<string> diagnostic)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _replyMessage = replyMessage ?? throw new ArgumentNullException(nameof(replyMessage));
        _diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
    }

    public uint DeviceBase { get; private set; }
    public bool IsInstalled => _gateways.Count != 0;
    internal int GatewayRegistrationCount => _gateways.Count;

    public long GetNextDeadline(long currentCycle, long targetCycle)
    {
        var next = targetCycle;
        foreach (var playback in _playback)
            if (playback is { Infinite: false } && playback.DeadlineCycle >= currentCycle) next = Math.Min(next, playback.DeadlineCycle);
        foreach (var wait in _waitCycles)
            if (wait.Deadline >= currentCycle) next = Math.Min(next, wait.Deadline);
        foreach (var finish in _pendingFinishes)
            if (finish.Deadline >= currentCycle) next = Math.Min(next, finish.Deadline);
        foreach (var change in _pendingPerVol)
            if (change.Deadline >= currentCycle) next = Math.Min(next, change.Deadline);
        return next;
    }

    public bool TryInstall(uint execBase)
    {
        if (IsInstalled || execBase == 0 || !_bus.IsMappedMemoryRange(execBase + DeviceListOffset, 14)) return IsInstalled;
        var device = FindDevice(execBase + DeviceListOffset, "audio.device");
        if (device < 36 || !_bus.IsMappedMemoryRange(device - 36, 36)) return false;
        DeviceBase = device;
        Register(-6, Open); Register(-12, Close); Register(-18, Expunge);
        Register(-24, ExtFunc); Register(-30, BeginIo); Register(-36, AbortIo);
        return true;
    }

    public void Reset() => Dispose();

    /// <summary>Retries non-NOWAIT allocation requests at an outer boundary.</summary>
    public void ProcessPending(M68kCpuState state)
    {
        var count = _pendingAllocations.Count;
        while (count-- > 0 && _pendingAllocations.Count != 0)
        {
            var request = _pendingAllocations.Dequeue();
            if (!_bus.IsMappedMemoryRange(request, IoAudioLengthOffset + 4)) continue;
            var error = Allocate(request, state);
            if (error != 0) { _pendingAllocations.Enqueue(request); continue; }
            Complete(request, 0, state.Cycles);
            Reply(request, state.Cycles);
        }
        for (var channel = 0; channel < _playback.Length; channel++)
        {
            var playback = _playback[channel];
            if (playback is null || playback.Infinite || playback.DeadlineCycle > state.Cycles) continue;
            _playback[channel] = null;
            Complete(playback.Request, 0, state.Cycles);
            Reply(playback.Request, state.Cycles);
            StartNextQueued(channel, state);
        }
        for (var index = _waitCycles.Count - 1; index >= 0; index--)
        {
            var wait = _waitCycles[index];
            if (state.Cycles < wait.Deadline) continue;
            _waitCycles.RemoveAt(index); Complete(wait.Request, 0, state.Cycles); Reply(wait.Request, state.Cycles);
        }
        for (var index = _pendingFinishes.Count - 1; index >= 0; index--)
        {
            var finish = _pendingFinishes[index];
            if (state.Cycles < finish.Deadline) continue;
            _pendingFinishes.RemoveAt(index); AbortPlayback(finish.Channel, state.Cycles);
        }
        for (var index = _pendingPerVol.Count - 1; index >= 0; index--)
        {
            var change = _pendingPerVol[index];
            if (state.Cycles < change.Deadline) continue;
            _pendingPerVol.RemoveAt(index);
            if (_playback[change.Channel] is { } playback) _playback[change.Channel] = playback with { Period = change.Period, Volume = change.Volume };
        }
    }

    /// <summary>Mixes the virtual audio.device channels at an emulator sample boundary.</summary>
    public void MixSample(long cycle, Span<float> destination, int frameIndex, int channels)
    {
        if (channels < 2 || frameIndex < 0 || frameIndex * channels + 1 >= destination.Length) return;
        for (var channel = 0; channel < _playback.Length; channel++)
        {
            var playback = _playback[channel];
            if (playback is null || cycle < playback.StartCycle || (!playback.Infinite && cycle >= playback.DeadlineCycle)) continue;
            var sampleCycles = Math.Max(1L, (long)playback.Period * _bus.RasterTiming.CpuCyclesPerColorClock);
            var byteIndex = (uint)(((cycle - playback.StartCycle) / sampleCycles) % playback.Length);
            var sample = unchecked((sbyte)_bus.ReadByte(playback.Data + byteIndex)) / 128f;
            var value = sample * (playback.Volume / 64f) * 0.25f;
            var offset = frameIndex * channels;
            destination[offset + (channel is 0 or 3 ? 1 : 0)] += value;
        }
    }

    public void Dispose()
    {
        for (var i = _gateways.Count - 1; i >= 0; i--) _bus.RemoveHostGateway(_gateways[i].Address, _gateways[i].Token);
        _gateways.Clear(); _pendingAllocations.Clear(); _waitCycles.Clear(); _pendingFinishes.Clear(); _pendingPerVol.Clear(); Array.Clear(_locks); Array.Clear(_channels); Array.Clear(_playback); Array.Clear(_stopped); foreach (var queue in _queuedWrites) queue.Clear(); _nextAllocationKey = 1; DeviceBase = 0;
    }

    private void Open(M68kCpuState state)
    {
        var request = state.A[1];
        if (request == 0 || !_bus.IsMappedMemoryRange(request, IoAudioLengthOffset + 4)) { state.D[0] = IoErrOpenFail; return; }
        _bus.WriteLong(request + IoDeviceOffset, DeviceBase, state.Cycles);
        IncrementOpenCount(state.Cycles);
        // Open may perform the first allocation when ioa_Length is non-zero.
        if (_bus.ReadLong(request + IoAudioLengthOffset) != 0) { Allocate(request, state); return; }
        Complete(request, 0, state.Cycles); state.D[0] = 0;
    }

    private void Close(M68kCpuState state)
    {
        var request = state.A[1];
        if (request != 0 && _bus.IsMappedMemoryRange(request + IoAudioAllocKeyOffset, 2)) Free(request, state.Cycles);
        if (DeviceBase != 0 && _bus.IsMappedMemoryRange(DeviceBase + LibraryOpenCountOffset, 2))
        {
            var count = _bus.ReadWord(DeviceBase + LibraryOpenCountOffset); if (count != 0) _bus.WriteWord(DeviceBase + LibraryOpenCountOffset, (ushort)(count - 1), state.Cycles);
        }
        state.D[0] = 0;
    }

    private static void Expunge(M68kCpuState state) => state.D[0] = 0;
    private static void ExtFunc(M68kCpuState state) => state.D[0] = 0;
    private void AbortIo(M68kCpuState state)
    {
        var request = state.A[1]; var removed = _pendingAllocations.Contains(request); if (removed) RemoveQueued(_pendingAllocations, request);
        for (var channel = 0; channel < 4; channel++)
        {
            if (_playback[channel] is { } playback && playback.Request == request) { _playback[channel] = null; StartNextQueued(channel, state); removed = true; }
            if (_queuedWrites[channel].Contains(request)) { RemoveQueued(_queuedWrites[channel], request); removed = true; }
        }
        removed |= _waitCycles.RemoveAll(wait => wait.Request == request) != 0;
        if (removed) { Complete(request, 0xFE, state.Cycles); Reply(request, state.Cycles); }
        state.D[0] = 0;
    }

    private void BeginIo(M68kCpuState state)
    {
        var request = state.A[1];
        if (request == 0 || !_bus.IsMappedMemoryRange(request, IoAudioLengthOffset + 4) || _bus.ReadLong(request + IoDeviceOffset) != DeviceBase) return;
        var command = _bus.ReadWord(request + IoCommandOffset);
        if (command == AdCmdAllocate)
        {
            var allocationError = Allocate(request, state);
            if (allocationError != 0 || _allocationDeferredForLock)
            {
                if ((_bus.ReadByte(request + IoFlagsOffset) & AdiofNowait) == 0 || _allocationDeferredForLock)
                {
                    _pendingAllocations.Enqueue(request);
                    MarkPending(request, state.Cycles);
                    return;
                }
                Complete(request, allocationError, state.Cycles); state.D[0] = allocationError; return;
            }
            Complete(request, 0, state.Cycles); state.D[0] = 0; return;
        }
        if (command == CmdWrite)
        {
            var writeError = StartWrite(request, state);
            if (writeError == 0) { state.D[0] = 0; return; }
            Complete(request, writeError, state.Cycles); state.D[0] = writeError; return;
        }
        byte error = command switch
        {
            AdCmdAllocate => Allocate(request, state),
            AdCmdFree => Free(request, state.Cycles),
            AdCmdSetPrec => SetPrecedence(request, state.Cycles),
            CmdFlush => Flush(request, state.Cycles),
            CmdReset => ResetChannels(request, state.Cycles),
            CmdRead => ReadCurrent(request, state.Cycles),
            AdCmdFinish => Finish(request, state.Cycles),
            AdCmdPerVol => ChangePeriodAndVolume(request, state.Cycles),
            CmdStop => Stop(request, state.Cycles),
            CmdStart => Start(request, state),
            AdCmdWaitCycle => WaitCycle(request, state),
            AdCmdLock => Lock(request, state),
            _ => IoErrNoCommand
        };
        if (error == IoErrNoCommand) _diagnostic($"audio.device command {command} is not implemented yet.");
        Complete(request, error, state.Cycles);
        state.D[0] = error;
    }

    private byte Allocate(uint request, M68kCpuState state)
    {
        _allocationDeferredForLock = false;
        var data = _bus.ReadLong(request + IoAudioDataOffset);
        var length = _bus.ReadLong(request + IoAudioLengthOffset);
        if (data == 0 || length == 0 || length > 64 || !_bus.IsMappedMemoryRange(data, unchecked((int)length))) return IoErrAllocFailed;
        var key = _bus.ReadWord(request + IoAudioAllocKeyOffset);
        if (key == 0) key = NextKey();
        var priority = unchecked((sbyte)_bus.ReadByte(request + 9)); // mn_Node.ln_Pri
        var nowait = (_bus.ReadByte(request + IoFlagsOffset) & AdiofNowait) != 0;
        for (var candidateIndex = 0u; candidateIndex < length; candidateIndex++)
        {
            var mask = (byte)(_bus.ReadByte(data + candidateIndex) & 0x0F);
            if (mask == 0 || !CanClaim(mask, key, priority)) continue;
            if (NotifyLockedOwners(mask, key, state.Cycles))
            {
                _allocationDeferredForLock = true;
                return IoErrAllocFailed;
            }
            Claim(mask, key, priority);
            _bus.WriteLong(request + IoUnitOffset, mask, state.Cycles);
            _bus.WriteWord(request + IoAudioAllocKeyOffset, key, state.Cycles);
            return 0;
        }
        return nowait ? IoErrAllocFailed : IoErrAllocFailed; // pending allocation is added with playback scheduling.
    }

    private byte Free(uint request, long cycle)
    {
        var key = _bus.ReadWord(request + IoAudioAllocKeyOffset);
        var mask = (byte)(_bus.ReadLong(request + IoUnitOffset) & 0x0F);
        var freed = false;
        for (var channel = 0; channel < _channels.Length; channel++)
            if ((mask & (1 << channel)) != 0 && _channels[channel] is { } owner && owner.Key == key) { AbortPlayback(channel, cycle); ReleaseLock(channel, cycle, 0); _channels[channel] = null; freed = true; }
        if (freed) ProcessFreedChannels(cycle);
        return freed || mask == 0 ? (byte)0 : AudioNoAllocation;
    }

    private byte SetPrecedence(uint request, long cycle)
    {
        var key = _bus.ReadWord(request + IoAudioAllocKeyOffset);
        var priority = unchecked((sbyte)_bus.ReadByte(request + 9));
        var mask = (byte)(_bus.ReadLong(request + IoUnitOffset) & 0x0F); var found = false;
        for (var channel = 0; channel < _channels.Length; channel++)
            if ((mask & (1 << channel)) != 0 && _channels[channel] is { } owner && owner.Key == key) { _channels[channel] = owner with { Priority = priority }; found = true; }
        return found ? (byte)0 : AudioNoAllocation;
    }

    private bool CanClaim(byte mask, ushort key, sbyte priority)
    {
        for (var channel = 0; channel < _channels.Length; channel++)
            if ((mask & (1 << channel)) != 0 && _channels[channel] is { } owner && owner.Key != key && owner.Priority >= priority) return false;
        return true;
    }
    private bool NotifyLockedOwners(byte mask, ushort key, long cycle)
    {
        var notified = false;
        for (var channel = 0; channel < 4; channel++)
        {
            if ((mask & (1 << channel)) == 0 || _channels[channel] is not { } owner || owner.Key == key || _locks[channel] == 0) continue;
            ReleaseLock(channel, cycle, 0xF4); // ADIOERR_CHANNELSTOLEN
            notified = true;
        }
        return notified;
    }
    private void Claim(byte mask, ushort key, sbyte priority)
    {
        for (var channel = 0; channel < _channels.Length; channel++) if ((mask & (1 << channel)) != 0) { if (_channels[channel] is { } old && old.Key != key) AbortPlayback(channel, 0); _channels[channel] = new ChannelOwner(key, priority); }
    }
    private ushort NextKey() { while (_nextAllocationKey == 0) _nextAllocationKey++; return _nextAllocationKey++; }
    private void Complete(uint request, byte error, long cycle)
    {
        _bus.WriteByte(request + IoErrorOffset, error, cycle);
    }
    private byte StartWrite(uint request, M68kCpuState state)
    {
        var channel = SingleChannel(_bus.ReadLong(request + IoUnitOffset));
        var key = _bus.ReadWord(request + IoAudioAllocKeyOffset);
        var length = _bus.ReadLong(request + IoAudioLengthOffset);
        var data = _bus.ReadLong(request + IoAudioDataOffset);
        var period = _bus.ReadWord(request + IoAudioPeriodOffset);
        var cycles = _bus.ReadWord(request + IoAudioCyclesOffset);
        if (channel < 0 || _channels[channel] is not { } owner || owner.Key != key) return AudioNoAllocation;
        if (data == 0 || (length & 1) != 0 || length == 0 || !_bus.IsMappedMemoryRange(data, unchecked((int)length)) || period == 0) return IoErrNoCommand;
        if (_stopped[channel] || _playback[channel] is not null)
        {
            _queuedWrites[channel].Enqueue(request); MarkPending(request, state.Cycles); return 0;
        }
        // A period is expressed in Paula colour clocks.  This virtual device
        // uses the same integer machine timebase but does not touch Paula.
        var repeats = cycles == 0 ? 0L : cycles;
        var duration = repeats == 0 ? 0L : SaturatingMultiply(SaturatingMultiply(length, period), _bus.RasterTiming.CpuCyclesPerColorClock);
        _playback[channel] = new ChannelPlayback(request, data, length, period, _bus.ReadWord(request + IoAudioPeriodOffset + 2), state.Cycles, AddSaturating(state.Cycles, duration), repeats == 0);
        if ((_bus.ReadByte(request + IoFlagsOffset) & AdiofWriteMessage) != 0 && _bus.IsMappedMemoryRange(request + IoAudioWriteMessageOffset, 20))
            _replyMessage(request + IoAudioWriteMessageOffset);
        MarkPending(request, state.Cycles);
        return 0;
    }
    private byte Flush(uint request, long cycle)
    {
        var mask = _bus.ReadLong(request + IoUnitOffset);
        AbortSelected(mask, cycle);
        for (var channel = 0; channel < 4; channel++)
        {
            if ((mask & (1u << channel)) == 0) continue;
            while (_queuedWrites[channel].Count != 0) { var queued = _queuedWrites[channel].Dequeue(); Complete(queued, 0xFE, cycle); Reply(queued, cycle); }
        }
        for (var index = _waitCycles.Count - 1; index >= 0; index--)
        {
            var wait = _waitCycles[index]; if ((mask & (1u << wait.Channel)) == 0) continue;
            _waitCycles.RemoveAt(index); Complete(wait.Request, 0xFE, cycle); Reply(wait.Request, cycle);
        }
        return 0;
    }
    private byte ResetChannels(uint request, long cycle)
    {
        var mask = _bus.ReadLong(request + IoUnitOffset);
        AbortSelected(mask, cycle);
        for (var channel = 0; channel < 4; channel++)
        {
            if ((mask & (1u << channel)) == 0) continue;
            _stopped[channel] = false;
            ReleaseLock(channel, cycle, 0);
        }
        return 0;
    }
    private byte ReadCurrent(uint request, long cycle)
    {
        var channel = SingleChannel(_bus.ReadLong(request + IoUnitOffset));
        if (channel < 0) return AudioNoAllocation;
        _bus.WriteLong(request + IoActualOffset, _playback[channel]?.Request ?? 0, cycle); return 0;
    }
    private byte Finish(uint request, long cycle)
    {
        var mask = _bus.ReadLong(request + IoUnitOffset);
        var key = _bus.ReadWord(request + IoAudioAllocKeyOffset);
        var found = false;
        for (var channel = 0; channel < 4; channel++)
        {
            if ((mask & (1u << channel)) == 0 || _channels[channel] is not { } owner || owner.Key != key) continue;
            if ((_bus.ReadByte(request + IoFlagsOffset) & AdiofSyncCycle) != 0 && _playback[channel] is { } playback)
            {
                var waveformCycles = SaturatingMultiply(SaturatingMultiply(playback.Length, playback.Period), _bus.RasterTiming.CpuCyclesPerColorClock);
                var elapsed = Math.Max(0, cycle - playback.StartCycle);
                _pendingFinishes.Add((channel, AddSaturating(playback.StartCycle, ((elapsed / waveformCycles) + 1) * waveformCycles)));
            }
            else AbortPlayback(channel, cycle);
            found = true;
        }
        return found ? (byte)0 : AudioNoAllocation;
    }
    private byte Stop(uint request, long cycle)
    {
        var mask = _bus.ReadLong(request + IoUnitOffset); var key = _bus.ReadWord(request + IoAudioAllocKeyOffset); var found = false;
        for (var channel = 0; channel < 4; channel++) if ((mask & (1u << channel)) != 0 && _channels[channel] is { } owner && owner.Key == key) { AbortPlayback(channel, cycle); _stopped[channel] = true; found = true; }
        return found ? (byte)0 : AudioNoAllocation;
    }
    private byte Start(uint request, M68kCpuState state)
    {
        var mask = _bus.ReadLong(request + IoUnitOffset); var key = _bus.ReadWord(request + IoAudioAllocKeyOffset); var found = false;
        for (var channel = 0; channel < 4; channel++) if ((mask & (1u << channel)) != 0 && _channels[channel] is { } owner && owner.Key == key) { _stopped[channel] = false; StartNextQueued(channel, state); found = true; }
        return found ? (byte)0 : AudioNoAllocation;
    }
    private byte WaitCycle(uint request, M68kCpuState state)
    {
        var channel = SingleChannel(_bus.ReadLong(request + IoUnitOffset)); var key = _bus.ReadWord(request + IoAudioAllocKeyOffset);
        if (channel < 0 || _channels[channel] is not { } owner || owner.Key != key) return AudioNoAllocation;
        if (_playback[channel] is null) return 0;
        var playback = _playback[channel]!;
        var waveformCycles = SaturatingMultiply(SaturatingMultiply(playback.Length, playback.Period), _bus.RasterTiming.CpuCyclesPerColorClock);
        var elapsed = Math.Max(0, state.Cycles - playback.StartCycle);
        var deadline = AddSaturating(playback.StartCycle, ((elapsed / waveformCycles) + 1) * waveformCycles);
        _waitCycles.Add((request, channel, deadline)); MarkPending(request, state.Cycles); return 0;
    }
    private byte Lock(uint request, M68kCpuState state)
    {
        var channel = SingleChannel(_bus.ReadLong(request + IoUnitOffset)); var key = _bus.ReadWord(request + IoAudioAllocKeyOffset);
        if (channel < 0 || _channels[channel] is not { } owner || owner.Key != key || _locks[channel] != 0) return AudioNoAllocation;
        _locks[channel] = request; MarkPending(request, state.Cycles); return 0;
    }
    private byte ChangePeriodAndVolume(uint request, long cycle)
    {
        var mask = _bus.ReadLong(request + IoUnitOffset); var key = _bus.ReadWord(request + IoAudioAllocKeyOffset);
        var period = _bus.ReadWord(request + IoAudioPeriodOffset); var volume = _bus.ReadWord(request + IoAudioPeriodOffset + 2);
        if (period == 0 || volume > 64) return IoErrNoCommand;
        var found = false;
        for (var channel = 0; channel < 4; channel++)
        {
            if ((mask & (1u << channel)) == 0 || _channels[channel] is not { } owner || owner.Key != key) continue;
            if (_playback[channel] is { } playback)
            {
                if ((_bus.ReadByte(request + IoFlagsOffset) & AdiofSyncCycle) == 0) _playback[channel] = playback with { Period = period, Volume = volume };
                else
                {
                    var waveformCycles = SaturatingMultiply(SaturatingMultiply(playback.Length, playback.Period), _bus.RasterTiming.CpuCyclesPerColorClock);
                    var elapsed = Math.Max(0, cycle - playback.StartCycle);
                    _pendingPerVol.Add((channel, period, volume, AddSaturating(playback.StartCycle, ((elapsed / waveformCycles) + 1) * waveformCycles)));
                }
            }
            found = true;
        }
        return found ? (byte)0 : AudioNoAllocation;
    }
    private void AbortSelected(uint mask, long cycle) { for (var channel = 0; channel < 4; channel++) if ((mask & (1u << channel)) != 0) AbortPlayback(channel, cycle); }
    private void AbortPlayback(int channel, long cycle) { if (_playback[channel] is { } playback) { _playback[channel] = null; Complete(playback.Request, 0xFE, cycle); Reply(playback.Request, cycle); } }
    private static int SingleChannel(uint mask) => mask is 1 or 2 or 4 or 8 ? BitOperations.TrailingZeroCount(mask) : -1;
    private void StartNextQueued(int channel, M68kCpuState state)
    {
        if (_stopped[channel] || _playback[channel] is not null || _queuedWrites[channel].Count == 0) return;
        var request = _queuedWrites[channel].Dequeue();
        if (StartWrite(request, state) != 0) { Complete(request, AudioNoAllocation, state.Cycles); Reply(request, state.Cycles); }
    }
    private static void RemoveQueued(Queue<uint> queue, uint request)
    {
        var count = queue.Count;
        while (count-- > 0) { var value = queue.Dequeue(); if (value != request) queue.Enqueue(value); }
    }
    private void ReleaseLock(int channel, long cycle, byte error)
    {
        var request = _locks[channel]; if (request == 0) return;
        _locks[channel] = 0; Complete(request, error, cycle); Reply(request, cycle);
    }
    private static long SaturatingMultiply(long left, long right) => left == 0 || right == 0 ? 0 : left > long.MaxValue / right ? long.MaxValue : left * right;
    private static long AddSaturating(long left, long right) => right > long.MaxValue - left ? long.MaxValue : left + right;
    private void MarkPending(uint request, long cycle) { if (_bus.IsMappedMemoryRange(request + NodeTypeOffset, 1)) _bus.WriteByte(request + NodeTypeOffset, 5, cycle); }
    private void Reply(uint request, long cycle)
    {
        if (_bus.IsMappedMemoryRange(request + NodeTypeOffset, 1)) _bus.WriteByte(request + NodeTypeOffset, 7, cycle);
        _replyMessage(request);
    }
    private void ProcessFreedChannels(long cycle) { /* outer-boundary retry is deliberately deferred; no reentrant replies from Free. */ }
    private void IncrementOpenCount(long cycle) { var count = _bus.ReadWord(DeviceBase + LibraryOpenCountOffset); _bus.WriteWord(DeviceBase + LibraryOpenCountOffset, (ushort)(count + 1), cycle); }
    private void Register(int lvo, Action<M68kCpuState> callback) { var address = unchecked((uint)((int)DeviceBase + lvo)); _gateways.Add((address, _bus.RegisterHostGateway(address, callback))); }
    private uint FindDevice(uint list, string name)
    {
        var node = _bus.ReadLong(list + NodeSuccessorOffset);
        for (var count = 0; node != 0 && node != list + 4 && count < 256; count++)
        {
            if (!_bus.IsMappedMemoryRange(node, NodeNameOffset + 4)) return 0;
            if (string.Equals(ReadName(_bus.ReadLong(node + NodeNameOffset)), name, StringComparison.OrdinalIgnoreCase)) return node;
            node = _bus.ReadLong(node + NodeSuccessorOffset);
        }
        return 0;
    }
    private string ReadName(uint address)
    {
        Span<char> chars = stackalloc char[64]; var length = 0;
        while (address != 0 && length < chars.Length && _bus.IsMappedMemoryRange(address + (uint)length, 1)) { var value = _bus.ReadByte(address + (uint)length); if (value == 0) break; chars[length++] = (char)value; }
        return new string(chars[..length]);
    }
}
