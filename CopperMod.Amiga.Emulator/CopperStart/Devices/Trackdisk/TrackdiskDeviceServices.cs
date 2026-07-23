using System;
using System.Collections.Generic;
using Copper68k;
using CopperMod.Amiga.Bus;

namespace CopperMod.Amiga.CopperStart.Devices.Trackdisk;

/// <summary>Immutable view of a live drive's encoded MFM track.</summary>
internal readonly record struct TrackdiskRawTrack(ReadOnlyMemory<byte> Data, int BitLength)
{
    public int ByteLength => (BitLength + 7) / 8;
}

internal delegate bool TrackdiskLogicalWriteHandler(int unit, int byteOffset, ReadOnlySpan<byte> source);

/// <summary>
/// Host replacement for the ROM-created <c>trackdisk.device</c> base.
///
/// The KS resident remains responsible for creating and linking the device.
/// Once that live base is visible in Exec's DeviceList, this service installs
/// direct host gateways at its six standard device vectors.  The original
/// bytes are never changed, and all other devices remain native.
/// </summary>
internal sealed class TrackdiskDeviceServices : IDisposable
{
    private const int DeviceListOffset = 0x15E;
    private const int NodeSuccessorOffset = 0x00;
    private const int NodeTypeOffset = 0x08;
    private const int NodeNameOffset = 0x0A;
    private const int LibraryOpenCountOffset = 0x20;
    private const int IoDeviceOffset = 0x14;
    private const int IoUnitOffset = 0x18;
    private const int IoCommandOffset = 0x1C;
    private const int IoFlagsOffset = 0x1E;
    private const int IoErrorOffset = 0x1F;
    private const int IoActualOffset = 0x20;
    private const int IoLengthOffset = 0x24;
    private const int IoDataOffset = 0x28;
    private const int IoOffsetOffset = 0x2C;
    private const int InterruptDataOffset = 0x0E;
    private const int InterruptCodeOffset = 0x12;
    private const ushort CmdRead = 2;
    private const ushort CmdWrite = 3;
    private const ushort CmdUpdate = 4;
    private const ushort CmdClear = 5;
    private const ushort CmdFlush = 8;
    private const ushort TdMotor = 9;
    private const ushort TdSeek = 10;
    private const ushort TdFormat = 11;
    private const ushort TdRemove = 12;
    private const ushort TdChangeNum = 13;
    private const ushort TdChangeState = 14;
    private const ushort TdProtStatus = 15;
    private const ushort TdRawRead = 16;
    private const ushort TdRawWrite = 17;
    private const ushort TdGetDriveType = 18;
    private const ushort TdGetNumTracks = 19;
    private const ushort TdAddChangeInt = 20;
    private const ushort TdRemChangeInt = 21;
    private const ushort TdGetGeometry = 22;
    private const ushort TdEject = 23;
    private const ushort TdRead64 = 24;
    private const ushort TdWrite64 = 25;
    private const ushort TdSeek64 = 26;
    private const ushort TdFormat64 = 27;
    private const byte IoQuick = 0x01;
    private const byte IoErrOpenFail = 0xFF;
    private const byte IoErrWriteProtected = 0xFC;
    private const byte IoErrBadAddress = 0xFD;
    private const byte IoErrUnsupported = 0xFB;

    private readonly AmigaBus _bus;
    private readonly Func<int, byte[]?> _getDriveData;
    private readonly TrackdiskLogicalWriteHandler _writeLogicalData;
    private readonly Func<int, TrackdiskRawTrack?> _getRawTrack;
    private readonly Func<int, TrackdiskRawTrack, bool> _writeRawTrack;
    private readonly Func<int, ulong> _getChangeVersion;
    private readonly Action<int> _ejectDrive;
    private readonly Func<int, bool> _isWriteProtected;
    private readonly Func<int, bool> _isMotorOn;
    private readonly Action<int, bool, long> _setMotor;
    private readonly Action<uint> _replyMessage;
    private readonly Action<string> _diagnostic;
    private readonly List<(uint Address, uint Token)> _gateways = new();
    private readonly Queue<uint> _pending = new();
    private readonly List<(int Unit, uint Interrupt)> _changeInterrupts = new();
    private readonly Queue<(int Unit, uint Interrupt)> _pendingChangeInterrupts = new();
    private readonly ulong[] _changeVersions = new ulong[4];
    private readonly uint[] _removeChangeInterrupts = new uint[4];
    private bool _changeInterruptActive;

    internal const uint ChangeInterruptContinuationAddress = 0x00F0_8500;

    public TrackdiskDeviceServices(
        AmigaBus bus,
        Func<int, byte[]?> getDriveData,
        TrackdiskLogicalWriteHandler writeLogicalData,
        Func<int, TrackdiskRawTrack?> getRawTrack,
        Func<int, TrackdiskRawTrack, bool> writeRawTrack,
        Func<int, ulong> getChangeVersion,
        Action<int> ejectDrive,
        Func<int, bool> isWriteProtected,
        Func<int, bool> isMotorOn,
        Action<int, bool, long> setMotor,
        Action<uint> replyMessage,
        Action<string> diagnostic)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _getDriveData = getDriveData ?? throw new ArgumentNullException(nameof(getDriveData));
        _writeLogicalData = writeLogicalData ?? throw new ArgumentNullException(nameof(writeLogicalData));
        _getRawTrack = getRawTrack ?? throw new ArgumentNullException(nameof(getRawTrack));
        _writeRawTrack = writeRawTrack ?? throw new ArgumentNullException(nameof(writeRawTrack));
        _getChangeVersion = getChangeVersion ?? throw new ArgumentNullException(nameof(getChangeVersion));
        _ejectDrive = ejectDrive ?? throw new ArgumentNullException(nameof(ejectDrive));
        _isWriteProtected = isWriteProtected ?? throw new ArgumentNullException(nameof(isWriteProtected));
        _isMotorOn = isMotorOn ?? throw new ArgumentNullException(nameof(isMotorOn));
        _setMotor = setMotor ?? throw new ArgumentNullException(nameof(setMotor));
        _replyMessage = replyMessage ?? throw new ArgumentNullException(nameof(replyMessage));
        _diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
    }

    public uint DeviceBase { get; private set; }

    public bool IsInstalled => _gateways.Count != 0;

    /// <summary>Installs after the genuine ROM resident has added its device base.</summary>
    public bool TryInstall(uint execBase)
    {
        if (IsInstalled || execBase == 0 || !_bus.IsMappedMemoryRange(execBase + DeviceListOffset, 14))
        {
            return IsInstalled;
        }

        var device = FindDevice(execBase + DeviceListOffset, "trackdisk.device");
        if (device == 0 || device < 36 || !_bus.IsMappedMemoryRange(device - 36, 36))
        {
            return false;
        }

        DeviceBase = device;
        for (var unit = 0; unit < _changeVersions.Length; unit++)
        {
            _changeVersions[unit] = _getChangeVersion(unit);
        }
        Register(-6, Open);
        Register(-12, Close);
        Register(-18, Expunge);
        Register(-24, ExtFunc);
        Register(-30, BeginIo);
        Register(-36, AbortIo);
        RegisterAddress(ChangeInterruptContinuationAddress, ContinueChangeInterrupt);
        return true;
    }

    public void Reset() => Dispose();

    /// <summary>
    /// Completes non-quick requests at an instruction boundary. The transfer is
    /// still atomic HLE, but completion is a normal ReplyMsg after BeginIO has
    /// returned, so native SendIO/WaitIO/CheckIO see ordinary IORequest state.
    /// </summary>
    public void ProcessPending(long cycle)
        => ProcessPendingRequests(cycle);

    /// <summary>Runs queued IO and starts at most one guest media-change callback.</summary>
    public void ProcessPending(M68kCpuState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ProcessPendingRequests(state.Cycles);
        PollChangeInterrupts();
        _ = StartNextChangeInterrupt(state);
    }

    private void ProcessPendingRequests(long cycle)
    {
        while (_pending.Count != 0)
        {
            var request = _pending.Dequeue();
            if (request == 0 || !_bus.IsMappedMemoryRange(request, 0x30))
            {
                continue;
            }

            Execute(request, cycle);
            MarkReply(request, cycle);
            _replyMessage(request);
        }
    }

    public void Dispose()
    {
        for (var index = _gateways.Count - 1; index >= 0; index--)
        {
            _bus.RemoveHostGateway(_gateways[index].Address, _gateways[index].Token);
        }

        _gateways.Clear();
        _pending.Clear();
        _changeInterrupts.Clear();
        _pendingChangeInterrupts.Clear();
        Array.Clear(_changeVersions);
        Array.Clear(_removeChangeInterrupts);
        _changeInterruptActive = false;
        DeviceBase = 0;
    }

    private void Open(M68kCpuState state)
    {
        var request = state.A[1];
        var unit = unchecked((int)state.D[0]);
        if (unit is < 0 or > 3 || request == 0 || !_bus.IsMappedMemoryRange(request, 0x30) || _getDriveData(unit) is null)
        {
            Complete(request, IoErrOpenFail, 0, state.Cycles);
            state.D[0] = IoErrOpenFail;
            return;
        }

        _bus.WriteLong(request + IoDeviceOffset, DeviceBase, state.Cycles);
        _bus.WriteLong(request + IoUnitOffset, unchecked((uint)unit), state.Cycles);
        Complete(request, 0, 0, state.Cycles);
        var openCount = _bus.ReadWord(DeviceBase + LibraryOpenCountOffset);
        _bus.WriteWord(DeviceBase + LibraryOpenCountOffset, unchecked((ushort)(openCount + 1)), state.Cycles);
        state.D[0] = 0;
    }

    private void Close(M68kCpuState state)
    {
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
        if (request == 0 || !_bus.IsMappedMemoryRange(request, 0x30) ||
            _bus.ReadLong(request + IoDeviceOffset) != DeviceBase)
        {
            return;
        }

        var flags = _bus.ReadByte(request + IoFlagsOffset);
        if ((flags & IoQuick) != 0)
        {
            Execute(request, state.Cycles);
            return;
        }

        if (!_pending.Contains(request))
        {
            _pending.Enqueue(request);
        }
    }

    private void AbortIo(M68kCpuState state)
    {
        var request = state.A[1];
        if (request == 0 || !_pending.Contains(request))
        {
            state.D[0] = 0xFFFF_FFFF;
            return;
        }

        var retained = new Queue<uint>();
        while (_pending.Count != 0)
        {
            var pending = _pending.Dequeue();
            if (pending != request)
            {
                retained.Enqueue(pending);
            }
        }

        while (retained.Count != 0)
        {
            _pending.Enqueue(retained.Dequeue());
        }

        Complete(request, 0xFE, 0, state.Cycles);
        MarkReply(request, state.Cycles);
        _replyMessage(request);
        state.D[0] = 0;
    }

    private void Execute(uint request, long cycle)
    {
        var command = _bus.ReadWord(request + IoCommandOffset);
        switch (command)
        {
            case CmdRead:
                BeginRead(request, cycle);
                break;
            case TdRead64:
                BeginRead(request, cycle, uses64BitOffset: true);
                break;
            case CmdWrite:
                BeginWrite(request, cycle);
                break;
            case TdWrite64:
                BeginWrite(request, cycle, uses64BitOffset: true);
                break;
            case TdFormat:
                BeginFormat(request, cycle);
                break;
            case TdFormat64:
                BeginFormat(request, cycle, uses64BitOffset: true);
                break;
            case TdRemove:
                SetRemoveChangeInterrupt(request, cycle);
                break;
            case TdMotor:
                BeginMotor(request, cycle);
                break;
            case TdSeek:
                BeginSeek(request, cycle);
                break;
            case TdSeek64:
                BeginSeek(request, cycle, uses64BitOffset: true);
                break;
            case TdProtStatus:
                Complete(request, 0, IsWriteProtected(request) ? uint.MaxValue : 0, cycle);
                break;
            case TdChangeState:
                Complete(request, 0, HasDisk(request) ? 0 : uint.MaxValue, cycle);
                break;
            case TdRawRead:
                BeginRawRead(request, cycle);
                break;
            case TdRawWrite:
                BeginRawWrite(request, cycle);
                break;
            case TdGetDriveType:
                CompleteQuery(request, cycle, driveType: 0); // DRV_35_DD
                break;
            case TdGetNumTracks:
                CompleteQuery(request, cycle, trackCount: 160);
                break;
            case TdEject:
                Eject(request, cycle);
                break;
            case TdAddChangeInt:
                AddChangeInterrupt(request, cycle);
                break;
            case TdRemChangeInt:
                RemoveChangeInterrupt(request, cycle);
                break;
            case TdGetGeometry:
                BeginGetGeometry(request, cycle);
                break;
            case CmdUpdate:
            case CmdClear:
            case CmdFlush:
                Complete(request, 0, 0, cycle);
                break;
            case TdChangeNum:
                Complete(request, 0, unchecked((uint)GetChangeVersion(request)), cycle);
                break;
            default:
                _diagnostic($"trackdisk.device unsupported command {command} at IORequest 0x{request:X8}.");
                Complete(request, IoErrUnsupported, 0, cycle);
                break;
        }
    }

    private void BeginRead(uint request, long cycle, bool uses64BitOffset = false)
    {
        var unit = unchecked((int)_bus.ReadLong(request + IoUnitOffset));
        var disk = unit is >= 0 and <= 3 ? _getDriveData(unit) : null;
        var length = _bus.ReadLong(request + IoLengthOffset);
        var destination = _bus.ReadLong(request + IoDataOffset);
        var offset = ReadOffset(request, uses64BitOffset);
        if (disk is null || length > int.MaxValue || offset > (ulong)disk.Length ||
            length > (ulong)disk.Length - offset || !_bus.IsMappedMemoryRange(destination, checked((int)length)))
        {
            Complete(request, IoErrBadAddress, 0, cycle);
            return;
        }

        _bus.CopyToMemory(destination, disk.AsSpan(checked((int)offset), (int)length));
        Complete(request, 0, length, cycle);
    }

    /// <summary>
    /// Copies encoded MFM bytes from the drive's current cylinder/head. This is
    /// a functional, atomic raw read; rotation and Paula DMA timing continue to
    /// belong to the disk controller path.
    /// </summary>
    private void BeginRawRead(uint request, long cycle)
    {
        var unit = unchecked((int)_bus.ReadLong(request + IoUnitOffset));
        var track = unit is >= 0 and <= 3 ? _getRawTrack(unit) : null;
        var length = _bus.ReadLong(request + IoLengthOffset);
        var destination = _bus.ReadLong(request + IoDataOffset);
        var offset = _bus.ReadLong(request + IoOffsetOffset);
        if (!track.HasValue || track.Value.BitLength <= 0 || track.Value.BitLength > track.Value.Data.Length * 8 ||
            length > int.MaxValue || offset > (uint)track.Value.ByteLength ||
            length > (uint)track.Value.ByteLength - offset || !_bus.IsMappedMemoryRange(destination, checked((int)length)))
        {
            Complete(request, IoErrBadAddress, 0, cycle);
            return;
        }

        _bus.CopyToMemory(destination, track.Value.Data.Span.Slice((int)offset, (int)length));
        Complete(request, 0, length, cycle);
    }

    /// <summary>
    /// Replaces the current cylinder/head's encoded MFM stream atomically.
    /// This does not emulate write rotation or Paula DMA; the drive callback
    /// owns media validation and cache invalidation.
    /// </summary>
    private void BeginRawWrite(uint request, long cycle)
    {
        var unit = unchecked((int)_bus.ReadLong(request + IoUnitOffset));
        var length = _bus.ReadLong(request + IoLengthOffset);
        var source = _bus.ReadLong(request + IoDataOffset);
        var offset = _bus.ReadLong(request + IoOffsetOffset);
        if (unit is < 0 or > 3 || length == 0 || length > int.MaxValue || offset != 0 ||
            !_bus.IsMappedMemoryRange(source, checked((int)length)))
        {
            Complete(request, IoErrBadAddress, 0, cycle);
            return;
        }

        if (IsWriteProtected(request))
        {
            Complete(request, IoErrWriteProtected, 0, cycle);
            return;
        }

        var encoded = new byte[checked((int)length)];
        _bus.CopyFromMemory(source, encoded);
        if (!_writeRawTrack(unit, new TrackdiskRawTrack(encoded, checked((int)length * 8))))
        {
            Complete(request, IoErrWriteProtected, 0, cycle);
            return;
        }

        Complete(request, 0, length, cycle);
    }

    private void CompleteQuery(uint request, long cycle, uint? driveType = null, uint? trackCount = null)
    {
        var unit = unchecked((int)_bus.ReadLong(request + IoUnitOffset));
        if (unit is < 0 or > 3 || _getDriveData(unit) is null)
        {
            Complete(request, IoErrOpenFail, 0, cycle);
            return;
        }

        Complete(request, 0, driveType ?? trackCount ?? 0, cycle);
    }

    private void AddChangeInterrupt(uint request, long cycle)
    {
        var unit = unchecked((int)_bus.ReadLong(request + IoUnitOffset));
        var interrupt = _bus.ReadLong(request + IoDataOffset);
        if (unit is < 0 or > 3 || interrupt == 0 ||
            !_bus.IsMappedMemoryRange(interrupt, InterruptCodeOffset + 4))
        {
            Complete(request, IoErrBadAddress, 0, cycle);
            return;
        }

        if (!_changeInterrupts.Contains((unit, interrupt)))
        {
            _changeInterrupts.Add((unit, interrupt));
        }

        Complete(request, 0, 0, cycle);
    }

    /// <summary>
    /// Legacy TD_REMOVE installs one change Interrupt per unit. Its return
    /// value is the previous is_Data-style pointer in io_Actual; newer callers
    /// should use TD_ADDCHANGEINT/TD_REMCHANGEINT for multiple registrations.
    /// </summary>
    private void SetRemoveChangeInterrupt(uint request, long cycle)
    {
        var unit = unchecked((int)_bus.ReadLong(request + IoUnitOffset));
        var interrupt = _bus.ReadLong(request + IoDataOffset);
        if (unit is < 0 or > 3 || (interrupt != 0 && !_bus.IsMappedMemoryRange(interrupt, InterruptCodeOffset + 4)))
        {
            Complete(request, IoErrBadAddress, 0, cycle);
            return;
        }

        var previous = _removeChangeInterrupts[unit];
        _removeChangeInterrupts[unit] = interrupt;
        Complete(request, 0, previous, cycle);
    }

    private void RemoveChangeInterrupt(uint request, long cycle)
    {
        var unit = unchecked((int)_bus.ReadLong(request + IoUnitOffset));
        var interrupt = _bus.ReadLong(request + IoDataOffset);
        _changeInterrupts.RemoveAll(entry => entry.Unit == unit && entry.Interrupt == interrupt);
        Complete(request, 0, 0, cycle);
    }

    private void Eject(uint request, long cycle)
    {
        var unit = unchecked((int)_bus.ReadLong(request + IoUnitOffset));
        if (unit is < 0 or > 3)
        {
            Complete(request, IoErrOpenFail, 0, cycle);
            return;
        }

        _ejectDrive(unit);
        Complete(request, 0, 0, cycle);
    }

    private void PollChangeInterrupts()
    {
        for (var unit = 0; unit < _changeVersions.Length; unit++)
        {
            var version = _getChangeVersion(unit);
            if (version == _changeVersions[unit])
            {
                continue;
            }

            _changeVersions[unit] = version;
            var legacyInterrupt = _removeChangeInterrupts[unit];
            if (legacyInterrupt != 0)
            {
                _pendingChangeInterrupts.Enqueue((unit, legacyInterrupt));
            }
            foreach (var registration in _changeInterrupts)
            {
                if (registration.Unit == unit)
                {
                    _pendingChangeInterrupts.Enqueue(registration);
                }
            }
        }
    }

    private bool StartNextChangeInterrupt(M68kCpuState state)
    {
        if (_changeInterruptActive)
        {
            return false;
        }

        while (_pendingChangeInterrupts.Count != 0)
        {
            var registration = _pendingChangeInterrupts.Dequeue();
            var isLegacyRegistration = _removeChangeInterrupts[registration.Unit] == registration.Interrupt;
            if ((!isLegacyRegistration && !_changeInterrupts.Contains(registration)) ||
                !_bus.IsMappedMemoryRange(registration.Interrupt, InterruptCodeOffset + 4))
            {
                continue;
            }

            var code = _bus.ReadLong(registration.Interrupt + InterruptCodeOffset);
            if (code == 0 || !_bus.IsCpuPhysicalAddressMapped(code, 2, AmigaBusAccessKind.CpuInstructionFetch))
            {
                continue;
            }

            var resume = state.ProgramCounter;
            state.A[7] -= 4;
            _bus.WriteLong(state.A[7], resume, state.Cycles);
            state.A[7] -= 4;
            _bus.WriteLong(state.A[7], ChangeInterruptContinuationAddress, state.Cycles);
            state.A[1] = _bus.ReadLong(registration.Interrupt + InterruptDataOffset);
            state.ProgramCounter = code;
            _changeInterruptActive = true;
            return true;
        }

        return false;
    }

    private void ContinueChangeInterrupt(M68kCpuState state)
        => _changeInterruptActive = false;

    private void BeginWrite(uint request, long cycle, bool uses64BitOffset = false)
        => BeginLogicalWrite(request, cycle, uses64BitOffset);

    /// <summary>
    /// TD_FORMAT is the sector-image counterpart of CMD_WRITE. It deliberately
    /// leaves raw MFM formatting to a future encoded-track implementation.
    /// </summary>
    private void BeginFormat(uint request, long cycle, bool uses64BitOffset = false)
        => BeginLogicalWrite(request, cycle, uses64BitOffset);

    /// <summary>
    /// Implements logical sector-image writes. This deliberately does not try
    /// to model a rotating track or raw MFM command: callers receive an atomic
    /// update to the mounted logical image, subject to write protection.
    /// </summary>
    private void BeginLogicalWrite(uint request, long cycle, bool uses64BitOffset)
    {
        var disk = GetDisk(request);
        var length = _bus.ReadLong(request + IoLengthOffset);
        var source = _bus.ReadLong(request + IoDataOffset);
        var offset = ReadOffset(request, uses64BitOffset);
        if (disk is null || length > int.MaxValue || offset > (ulong)disk.Length ||
            length > (ulong)disk.Length - offset || !_bus.IsMappedMemoryRange(source, checked((int)length)))
        {
            Complete(request, IoErrBadAddress, 0, cycle);
            return;
        }

        if (IsWriteProtected(request))
        {
            Complete(request, IoErrWriteProtected, 0, cycle);
            return;
        }

        var bytes = new byte[checked((int)length)];
        _bus.CopyFromMemory(source, bytes);
        if (!_writeLogicalData(unchecked((int)_bus.ReadLong(request + IoUnitOffset)), checked((int)offset), bytes))
        {
            Complete(request, IoErrWriteProtected, 0, cycle);
            return;
        }

        Complete(request, 0, length, cycle);
    }

    private void BeginMotor(uint request, long cycle)
    {
        var unit = unchecked((int)_bus.ReadLong(request + IoUnitOffset));
        if (unit is < 0 or > 3 || _getDriveData(unit) is null)
        {
            Complete(request, IoErrOpenFail, 0, cycle);
            return;
        }

        var previousMotorOn = _isMotorOn(unit) ? 1u : 0u;
        _setMotor(unit, _bus.ReadLong(request + IoLengthOffset) != 0, cycle);
        Complete(request, 0, previousMotorOn, cycle);
    }

    private void BeginSeek(uint request, long cycle, bool uses64BitOffset = false)
    {
        var disk = GetDisk(request);
        var offset = ReadOffset(request, uses64BitOffset);
        if (disk is null || offset > (ulong)disk.Length || (offset & 0x1FF) != 0)
        {
            Complete(request, IoErrBadAddress, 0, cycle);
            return;
        }

        Complete(request, 0, unchecked((uint)offset), cycle);
    }

    private void BeginGetGeometry(uint request, long cycle)
    {
        const int geometrySize = 48;
        const uint sectorSize = 512;
        const uint heads = 2;
        const uint sectorsPerTrack = 11;
        var disk = GetDisk(request);
        var destination = _bus.ReadLong(request + IoDataOffset);
        if (disk is null || !_bus.IsMappedMemoryRange(destination, geometrySize))
        {
            Complete(request, IoErrBadAddress, 0, cycle);
            return;
        }

        var totalSectors = (uint)disk.Length / sectorSize;
        var sectorsPerCylinder = sectorsPerTrack * heads;
        var cylinders = totalSectors / sectorsPerCylinder;
        _bus.WriteLong(destination + 0, sectorSize, cycle);       // dg_SectorSize
        _bus.WriteLong(destination + 4, totalSectors, cycle);     // dg_TotalSectors
        _bus.WriteLong(destination + 8, cylinders, cycle);        // dg_Cylinders
        _bus.WriteLong(destination + 12, sectorsPerCylinder, cycle); // dg_CylSectors
        _bus.WriteLong(destination + 16, sectorsPerTrack, cycle); // dg_TrackSectors
        _bus.WriteLong(destination + 20, 1, cycle);               // dg_BufMemType = MEMF_PUBLIC
        _bus.WriteByte(destination + 24, 0, cycle);               // dg_DeviceType = direct access
        _bus.WriteByte(destination + 25, 1, cycle);               // dg_Flags = removable
        _bus.WriteWord(destination + 26, 0, cycle);
        _bus.WriteLong(destination + 28, 1, cycle);               // dg_SectorPerBlock
        _bus.WriteLong(destination + 32, 0, cycle);               // dg_Interleave
        _bus.WriteLong(destination + 36, 0, cycle);               // dg_TrackSkew
        _bus.WriteLong(destination + 40, 0, cycle);               // dg_CylSkew
        _bus.WriteLong(destination + 44, heads, cycle);           // dg_Heads
        Complete(request, 0, geometrySize, cycle);
    }

    private bool HasDisk(uint request) => GetDisk(request) is not null;

    private ulong ReadOffset(uint request, bool uses64BitOffset)
        => uses64BitOffset
            ? ((ulong)_bus.ReadLong(request + IoActualOffset) << 32) | _bus.ReadLong(request + IoOffsetOffset)
            : _bus.ReadLong(request + IoOffsetOffset);

    private ulong GetChangeVersion(uint request)
    {
        var unit = unchecked((int)_bus.ReadLong(request + IoUnitOffset));
        return unit is >= 0 and <= 3 ? _getChangeVersion(unit) : 0;
    }

    private bool IsWriteProtected(uint request)
    {
        var unit = unchecked((int)_bus.ReadLong(request + IoUnitOffset));
        return unit is >= 0 and <= 3 && _isWriteProtected(unit);
    }

    private byte[]? GetDisk(uint request)
    {
        var unit = unchecked((int)_bus.ReadLong(request + IoUnitOffset));
        return unit is >= 0 and <= 3 ? _getDriveData(unit) : null;
    }

    private void Complete(uint request, byte error, uint actual, long cycle)
    {
        if (request == 0 || !_bus.IsMappedMemoryRange(request + IoErrorOffset, 5))
        {
            return;
        }

        _bus.WriteByte(request + IoErrorOffset, error, cycle);
        _bus.WriteLong(request + IoActualOffset, actual, cycle);
    }

    private void MarkReply(uint request, long cycle)
    {
        if (request != 0 && _bus.IsMappedMemoryRange(request + NodeTypeOffset, 1))
        {
            _bus.WriteByte(request + NodeTypeOffset, 7, cycle); // NT_REPLYMSG
        }
    }

    private void Register(int lvo, Action<M68kCpuState> callback)
    {
        var address = unchecked((uint)((int)DeviceBase + lvo));
        RegisterAddress(address, callback);
    }

    private void RegisterAddress(uint address, Action<M68kCpuState> callback)
        => _gateways.Add((address, _bus.RegisterHostGateway(address, callback)));

    private uint FindDevice(uint list, string name)
    {
        var node = _bus.ReadLong(list + NodeSuccessorOffset);
        for (var count = 0; node != 0 && node != list + 4 && count < 256; count++)
        {
            if (!_bus.IsMappedMemoryRange(node, NodeNameOffset + 4))
            {
                return 0;
            }

            var nameAddress = _bus.ReadLong(node + NodeNameOffset);
            if (string.Equals(ReadName(nameAddress), name, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            node = _bus.ReadLong(node + NodeSuccessorOffset);
        }

        return 0;
    }

    private string ReadName(uint address)
    {
        if (address == 0)
        {
            return string.Empty;
        }

        Span<char> characters = stackalloc char[64];
        var length = 0;
        for (; length < characters.Length && _bus.IsMappedMemoryRange(address + (uint)length, 1); length++)
        {
            var value = _bus.ReadByte(address + (uint)length);
            if (value == 0)
            {
                break;
            }

            characters[length] = (char)value;
        }

        return new string(characters[..length]);
    }
}
