using System;
using System.Collections.Generic;
using Copper68k;
using CopperMod.Amiga.Bus;

namespace CopperMod.Amiga.CopperStart.Devices.Timer;

/// <summary>
/// Deterministic host replacement for the ROM-created <c>timer.device</c>.
/// It intentionally uses the emulator clock only: neither CIA timer is
/// allocated, programmed, or reserved by this service. <c>UNIT_VBLANK</c>
/// uses the fixed PAL (50 Hz) or NTSC (60 Hz) timer cadence, not beam state.
/// </summary>
internal sealed class TimerDeviceServices : IDisposable
{
    private const int DeviceListOffset = 0x15E;
    private const int NodeSuccessorOffset = 0x00;
    private const int NodeNameOffset = 0x0A;
    private const int LibraryOpenCountOffset = 0x20;
    private const int IoDeviceOffset = 0x14;
    private const int IoUnitOffset = 0x18;
    private const int IoCommandOffset = 0x1C;
    private const int IoFlagsOffset = 0x1E;
    private const int IoErrorOffset = 0x1F;
    private const int TimeSecondsOffset = 0x20;
    private const int TimeMicrosecondsOffset = 0x24;
    private const int ReplyPortOffset = 0x0E;
    private const byte IoQuick = 0x01;
    private const byte IoErrOpenFail = 0xFF;
    private const byte IoErrAborted = 0xFE;
    private const byte IoErrNoCommand = 0xFD;
    private const byte IoErrBadAddress = 0xFC;
    private const ushort TrAddRequest = 9;
    private const ushort TrGetSysTime = 10;
    private const ushort TrSetSysTime = 11;
    private const ushort TrAddTime = 12;
    private const ushort TrSubTime = 13;
    private const int UnitMicroHz = 0;
    private const int UnitVBlank = 1;
    private const int UnitEClock = 2;
    private const int UnitWaitUntil = 3;
    private const int UnitWaitEClock = 4;
    private const uint MicrosecondsPerSecond = 1_000_000;

    private readonly AmigaBus _bus;
    private readonly Action<uint> _replyMessage;
    private readonly Action<string> _diagnostic;
    private readonly List<(uint Address, uint Token)> _gateways = new();
    private readonly Dictionary<uint, PendingRequest> _pending = new();
    private Int128 _systemTimeOffsetMicroseconds;
    private long _sequence;

    private readonly record struct PendingRequest(
        uint Address,
        int Unit,
        long NotBeforeCycle,
        long Sequence);

    public TimerDeviceServices(AmigaBus bus, Action<uint> replyMessage, Action<string> diagnostic)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _replyMessage = replyMessage ?? throw new ArgumentNullException(nameof(replyMessage));
        _diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
    }

    public uint DeviceBase { get; private set; }

    public bool IsInstalled => _gateways.Count != 0;

    /// <summary>Installs only after the genuine ROM resident linked timer.device.</summary>
    public bool TryInstall(uint execBase)
    {
        if (IsInstalled || execBase == 0 || !_bus.IsMappedMemoryRange(execBase + DeviceListOffset, 14))
        {
            return IsInstalled;
        }

        var device = FindDevice(execBase + DeviceListOffset, "timer.device");
        if (device == 0 || device < 42 || !_bus.IsMappedMemoryRange(device - 42, 42))
        {
            return false;
        }

        DeviceBase = device;
        Register(-6, Open);
        Register(-12, Close);
        Register(-18, Expunge);
        Register(-24, ExtFunc);
        Register(-30, BeginIo);
        Register(-36, AbortIo);
        Register(-42, ReadEClock);
        return true;
    }

    /// <summary>Returns the earliest host timer wake, or <paramref name="targetCycle"/>.</summary>
    public long GetNextDeadline(long currentCycle, long targetCycle)
    {
        var next = targetCycle;
        foreach (var pending in _pending.Values)
        {
            var deadline = GetDeadlineCycle(pending, currentCycle);
            if (deadline <= currentCycle)
            {
                return currentCycle;
            }

            if (deadline < next)
            {
                next = deadline;
            }
        }

        return next;
    }

    /// <summary>Completes all requests due at this instruction boundary.</summary>
    public void ProcessPending(M68kCpuState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_pending.Count == 0)
        {
            return;
        }

        var due = new List<PendingRequest>();
        foreach (var pending in _pending.Values)
        {
            if (GetDeadlineCycle(pending, state.Cycles) <= state.Cycles)
            {
                due.Add(pending);
            }
        }

        due.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));
        foreach (var pending in due)
        {
            if (!_pending.Remove(pending.Address))
            {
                continue;
            }

            CompleteAndReply(pending.Address, 0, state.Cycles);
        }
    }

    public void Reset() => Dispose();

    public void Dispose()
    {
        for (var index = _gateways.Count - 1; index >= 0; index--)
        {
            _bus.RemoveHostGateway(_gateways[index].Address, _gateways[index].Token);
        }

        _gateways.Clear();
        _pending.Clear();
        _systemTimeOffsetMicroseconds = 0;
        _sequence = 0;
        DeviceBase = 0;
    }

    private void Open(M68kCpuState state)
    {
        var request = state.A[1];
        var unit = unchecked((int)state.D[0]);
        if (request == 0 || !IsSupportedUnit(unit) || !_bus.IsMappedMemoryRange(request, 0x28))
        {
            Complete(request, IoErrOpenFail, state.Cycles);
            state.D[0] = IoErrOpenFail;
            return;
        }

        _bus.WriteLong(request + IoDeviceOffset, DeviceBase, state.Cycles);
        _bus.WriteLong(request + IoUnitOffset, unchecked((uint)unit), state.Cycles);
        Complete(request, 0, state.Cycles);
        var openCount = _bus.ReadWord(DeviceBase + LibraryOpenCountOffset);
        _bus.WriteWord(DeviceBase + LibraryOpenCountOffset, unchecked((ushort)(openCount + 1)), state.Cycles);
        state.D[0] = 0;
    }

    private void Close(M68kCpuState state)
    {
        var request = state.A[1];
        if (request != 0 && _pending.Remove(request))
        {
            CompleteAndReply(request, IoErrAborted, state.Cycles);
        }

        if (DeviceBase != 0 && _bus.IsMappedMemoryRange(DeviceBase + LibraryOpenCountOffset, 2))
        {
            var openCount = _bus.ReadWord(DeviceBase + LibraryOpenCountOffset);
            if (openCount != 0)
            {
                _bus.WriteWord(DeviceBase + LibraryOpenCountOffset, unchecked((ushort)(openCount - 1)), state.Cycles);
            }
        }

        state.D[0] = 0;
    }

    private static void Expunge(M68kCpuState state) => state.D[0] = 0;

    private static void ExtFunc(M68kCpuState state) => state.D[0] = 0;

    private void BeginIo(M68kCpuState state)
    {
        var request = state.A[1];
        if (request == 0 || !_bus.IsMappedMemoryRange(request, TimeMicrosecondsOffset + 4) ||
            _bus.ReadLong(request + IoDeviceOffset) != DeviceBase)
        {
            return;
        }

        var command = _bus.ReadWord(request + IoCommandOffset);
        if (command == TrAddRequest)
        {
            BeginRequest(request, state);
            return;
        }

        if (_pending.ContainsKey(request))
        {
            CompleteAndReply(request, IoErrBadAddress, state.Cycles);
            return;
        }

        var quick = (_bus.ReadByte(request + IoFlagsOffset) & IoQuick) != 0;
        byte error;
        switch (command)
        {
            case TrGetSysTime: error = GetSystemTime(request, state.Cycles); break;
            case TrSetSysTime: error = SetSystemTime(request, state.Cycles); break;
            case TrAddTime: error = AddTime(request, state.Cycles); break;
            case TrSubTime: error = SubtractTime(request, state.Cycles); break;
            default:
                _diagnostic($"timer.device unsupported command {command} at IORequest 0x{request:X8}.");
                error = IoErrNoCommand;
                break;
        }

        if (quick)
        {
            Complete(request, error, state.Cycles);
        }
        else
        {
            CompleteAndReply(request, error, state.Cycles);
        }
    }

    private void BeginRequest(uint request, M68kCpuState state)
    {
        if (_pending.ContainsKey(request))
        {
            CompleteAndReply(request, IoErrBadAddress, state.Cycles);
            return;
        }

        var unit = unchecked((int)_bus.ReadLong(request + IoUnitOffset));
        if (!IsSupportedUnit(unit) || !TryReadTime(request, out var first, out var second))
        {
            CompleteAndReply(request, IoErrBadAddress, state.Cycles);
            return;
        }

        if (!TryGetNotBeforeCycle(unit, first, second, state.Cycles, out var notBefore))
        {
            CompleteAndReply(request, IoErrBadAddress, state.Cycles);
            return;
        }

        // A timer request must reply even when called through DoIO, so force
        // Exec to use its normal reply-port/WaitIO route.
        _bus.WriteByte(request + IoFlagsOffset, (byte)(_bus.ReadByte(request + IoFlagsOffset) & ~IoQuick), state.Cycles);
        Complete(request, 0, state.Cycles);
        _pending.Add(request, new PendingRequest(request, unit, notBefore, _sequence++));
    }

    private void AbortIo(M68kCpuState state)
    {
        var request = state.A[1];
        if (request == 0 || !_pending.Remove(request))
        {
            state.D[0] = uint.MaxValue;
            return;
        }

        CompleteAndReply(request, IoErrAborted, state.Cycles);
        state.D[0] = 0;
    }

    private void ReadEClock(M68kCpuState state)
    {
        var destination = state.A[0];
        if (destination != 0 && _bus.IsMappedMemoryRange(destination, 8))
        {
            var ticks = GetEClockTicks(state.Cycles);
            _bus.WriteLong(destination, unchecked((uint)(ticks >> 32)), state.Cycles);
            _bus.WriteLong(destination + 4, unchecked((uint)ticks), state.Cycles);
        }

        state.D[0] = unchecked((uint)GetEClockHz());
    }

    private byte GetSystemTime(uint request, long cycle)
    {
        WriteTime(request, GetSystemTimeMicroseconds(cycle), cycle);
        return 0;
    }

    private byte SetSystemTime(uint request, long cycle)
    {
        if (!TryReadTime(request, out var seconds, out var microseconds) || microseconds >= MicrosecondsPerSecond)
        {
            return IoErrBadAddress;
        }

        _systemTimeOffsetMicroseconds = ToMicroseconds(seconds, microseconds) - ElapsedMicroseconds(cycle);
        return 0;
    }

    private byte AddTime(uint request, long cycle)
    {
        if (!TryReadTime(request, out var seconds, out var microseconds) || microseconds >= MicrosecondsPerSecond)
        {
            return IoErrBadAddress;
        }

        _systemTimeOffsetMicroseconds += ToMicroseconds(seconds, microseconds);
        WriteTime(request, GetSystemTimeMicroseconds(cycle), cycle);
        return 0;
    }

    private byte SubtractTime(uint request, long cycle)
    {
        if (!TryReadTime(request, out var seconds, out var microseconds) || microseconds >= MicrosecondsPerSecond)
        {
            return IoErrBadAddress;
        }

        _systemTimeOffsetMicroseconds = Int128.Max(
            -ElapsedMicroseconds(cycle),
            _systemTimeOffsetMicroseconds - ToMicroseconds(seconds, microseconds));
        WriteTime(request, GetSystemTimeMicroseconds(cycle), cycle);
        return 0;
    }

    private bool TryGetNotBeforeCycle(int unit, uint first, uint second, long cycle, out long notBefore)
    {
        notBefore = cycle;
        switch (unit)
        {
            case UnitMicroHz:
            case UnitVBlank:
            case UnitEClock:
                if (second >= MicrosecondsPerSecond)
                {
                    return false;
                }

                notBefore = AddSaturating(cycle, MicrosecondsToCycles(ToMicroseconds(first, second)));
                return true;
            case UnitWaitUntil:
            {
                if (second >= MicrosecondsPerSecond)
                {
                    return false;
                }

                var target = ToMicroseconds(first, second);
                var current = GetSystemTimeMicroseconds(cycle);
                notBefore = target <= current ? cycle : AddSaturating(cycle, MicrosecondsToCycles(target - current));
                return true;
            }
            case UnitWaitEClock:
            {
                var target = ((ulong)first << 32) | second;
                var current = GetEClockTicks(cycle);
                notBefore = target <= current ? cycle : EClockTicksToCycles(target - current);
                notBefore = AddSaturating(cycle, notBefore);
                return true;
            }
            default:
                return false;
        }
    }

    private long GetDeadlineCycle(PendingRequest pending, long currentCycle)
    {
        if (pending.Unit != UnitVBlank)
        {
            return pending.NotBeforeCycle;
        }

        return GetNextTimerVBlankCycle(pending.NotBeforeCycle, currentCycle);
    }

    // This is a timer.device tick, not the custom-chip VERTB event. Keep the
    // numerator intact until the final division so PAL's non-integral number
    // of CPU cycles per 20 ms cannot accumulate rounding drift.
    private long GetNextTimerVBlankCycle(long notBeforeCycle, long currentCycle)
    {
        var frequency = _bus.RasterTiming.IsCanonicalNtsc ? 60L : 50L;
        var clock = _bus.RasterTiming.CpuClockHz;
        var threshold = Math.Max(notBeforeCycle, currentCycle);
        var tick = ((Int128)Math.Max(0, threshold) * frequency) / clock + 1;
        var deadline = (tick * clock + (frequency - 1)) / frequency;
        return deadline >= long.MaxValue ? long.MaxValue : (long)deadline;
    }

    private void CompleteAndReply(uint request, byte error, long cycle)
    {
        Complete(request, error, cycle);
        if (request != 0 && _bus.IsMappedMemoryRange(request + NodeSuccessorOffset, 9))
        {
            _bus.WriteByte(request + 0x08, 7, cycle); // NT_REPLYMSG
        }

        if (request != 0 && _bus.IsMappedMemoryRange(request + ReplyPortOffset, 4) && _bus.ReadLong(request + ReplyPortOffset) != 0)
        {
            _replyMessage(request);
        }
    }

    private void Complete(uint request, byte error, long cycle)
    {
        if (request == 0 || !_bus.IsMappedMemoryRange(request + IoErrorOffset, 5))
        {
            return;
        }

        // timerrequest extends IORequest with timeval at offset 0x20; unlike
        // IOStdReq, that storage is not an io_Actual result field.
        _bus.WriteByte(request + IoErrorOffset, error, cycle);
    }

    private bool TryReadTime(uint request, out uint seconds, out uint microseconds)
    {
        seconds = 0;
        microseconds = 0;
        if (!_bus.IsMappedMemoryRange(request + TimeMicrosecondsOffset, 4))
        {
            return false;
        }

        seconds = _bus.ReadLong(request + TimeSecondsOffset);
        microseconds = _bus.ReadLong(request + TimeMicrosecondsOffset);
        return true;
    }

    private void WriteTime(uint request, Int128 microseconds, long cycle)
    {
        microseconds = Int128.Clamp(microseconds, 0, (Int128)uint.MaxValue * MicrosecondsPerSecond + (MicrosecondsPerSecond - 1));
        _bus.WriteLong(request + TimeSecondsOffset, unchecked((uint)(microseconds / MicrosecondsPerSecond)), cycle);
        _bus.WriteLong(request + TimeMicrosecondsOffset, unchecked((uint)(microseconds % MicrosecondsPerSecond)), cycle);
    }

    private Int128 GetSystemTimeMicroseconds(long cycle)
        => Int128.Max(0, ElapsedMicroseconds(cycle) + _systemTimeOffsetMicroseconds);

    private Int128 ElapsedMicroseconds(long cycle)
        => ((Int128)Math.Max(0, cycle) * MicrosecondsPerSecond) / _bus.RasterTiming.CpuClockHz;

    private Int128 ToMicroseconds(uint seconds, uint microseconds)
        => ((Int128)seconds * MicrosecondsPerSecond) + microseconds;

    private long MicrosecondsToCycles(Int128 microseconds)
    {
        if (microseconds <= 0)
        {
            return 0;
        }

        var cycles = (microseconds * _bus.RasterTiming.CpuClockHz + (MicrosecondsPerSecond - 1)) / MicrosecondsPerSecond;
        return cycles >= long.MaxValue ? long.MaxValue : (long)cycles;
    }

    private long GetEClockHz() => _bus.RasterTiming.CpuClockHz / 10;

    private ulong GetEClockTicks(long cycle)
        => unchecked((ulong)(((Int128)Math.Max(0, cycle) * GetEClockHz()) / _bus.RasterTiming.CpuClockHz));

    private long EClockTicksToCycles(ulong ticks)
    {
        var cycles = ((Int128)ticks * _bus.RasterTiming.CpuClockHz + (GetEClockHz() - 1)) / GetEClockHz();
        return cycles >= long.MaxValue ? long.MaxValue : (long)cycles;
    }

    private static long AddSaturating(long left, long right)
        => right > long.MaxValue - left ? long.MaxValue : left + right;

    private static bool IsSupportedUnit(int unit)
        => unit is UnitMicroHz or UnitVBlank or UnitEClock or UnitWaitUntil or UnitWaitEClock;

    private void Register(int lvo, Action<M68kCpuState> callback)
    {
        var address = unchecked((uint)((int)DeviceBase + lvo));
        _gateways.Add((address, _bus.RegisterHostGateway(address, callback)));
    }

    private uint FindDevice(uint list, string name)
    {
        var node = _bus.ReadLong(list + NodeSuccessorOffset);
        for (var count = 0; node != 0 && node != list + 4 && count < 256; count++)
        {
            if (!_bus.IsMappedMemoryRange(node, NodeNameOffset + 4))
            {
                return 0;
            }

            if (string.Equals(ReadName(_bus.ReadLong(node + NodeNameOffset)), name, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            node = _bus.ReadLong(node + NodeSuccessorOffset);
        }

        return 0;
    }

    private string ReadName(uint address)
    {
        Span<char> characters = stackalloc char[64];
        var length = 0;
        while (address != 0 && length < characters.Length && _bus.IsMappedMemoryRange(address + (uint)length, 1))
        {
            var value = _bus.ReadByte(address + (uint)length);
            if (value == 0)
            {
                break;
            }

            characters[length++] = (char)value;
        }

        return new string(characters[..length]);
    }
}
