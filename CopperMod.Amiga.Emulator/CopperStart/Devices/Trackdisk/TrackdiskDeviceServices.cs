using System;
using System.Collections.Generic;
using Copper68k;
using CopperMod.Amiga.Bus;

namespace CopperMod.Amiga.CopperStart.Devices.Trackdisk;

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
    private const ushort CmdRead = 2;
    private const ushort CmdUpdate = 4;
    private const ushort CmdClear = 5;
    private const ushort CmdFlush = 8;
    private const ushort TdMotor = 9;
    private const ushort TdChangeNum = 13;
    private const byte IoQuick = 0x01;
    private const byte IoErrOpenFail = 0xFF;
    private const byte IoErrBadAddress = 0xFD;
    private const byte IoErrUnsupported = 0xFB;

    private readonly AmigaBus _bus;
    private readonly Func<int, byte[]?> _getDriveData;
    private readonly Func<int, bool> _isMotorOn;
    private readonly Action<int, bool, long> _setMotor;
    private readonly Action<uint> _replyMessage;
    private readonly Action<string> _diagnostic;
    private readonly List<(uint Address, uint Token)> _gateways = new();
    private readonly Queue<uint> _pending = new();

    public TrackdiskDeviceServices(
        AmigaBus bus,
        Func<int, byte[]?> getDriveData,
        Func<int, bool> isMotorOn,
        Action<int, bool, long> setMotor,
        Action<uint> replyMessage,
        Action<string> diagnostic)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _getDriveData = getDriveData ?? throw new ArgumentNullException(nameof(getDriveData));
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
        Register(-6, Open);
        Register(-12, Close);
        Register(-18, Expunge);
        Register(-24, ExtFunc);
        Register(-30, BeginIo);
        Register(-36, AbortIo);
        return true;
    }

    public void Reset() => Dispose();

    /// <summary>
    /// Completes non-quick requests at an instruction boundary. The transfer is
    /// still atomic HLE, but completion is a normal ReplyMsg after BeginIO has
    /// returned, so native SendIO/WaitIO/CheckIO see ordinary IORequest state.
    /// </summary>
    public void ProcessPending(long cycle)
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
            case TdMotor:
                BeginMotor(request, cycle);
                break;
            case CmdUpdate:
            case CmdClear:
            case CmdFlush:
            case TdChangeNum:
                Complete(request, 0, 0, cycle);
                break;
            default:
                _diagnostic($"trackdisk.device unsupported command {command} at IORequest 0x{request:X8}.");
                Complete(request, IoErrUnsupported, 0, cycle);
                break;
        }
    }

    private void BeginRead(uint request, long cycle)
    {
        var unit = unchecked((int)_bus.ReadLong(request + IoUnitOffset));
        var disk = unit is >= 0 and <= 3 ? _getDriveData(unit) : null;
        var length = _bus.ReadLong(request + IoLengthOffset);
        var destination = _bus.ReadLong(request + IoDataOffset);
        var offset = _bus.ReadLong(request + IoOffsetOffset);
        if (disk is null || length > int.MaxValue || offset > (uint)disk.Length ||
            length > (uint)disk.Length - offset || !_bus.IsMappedMemoryRange(destination, checked((int)length)))
        {
            Complete(request, IoErrBadAddress, 0, cycle);
            return;
        }

        _bus.CopyToMemory(destination, disk.AsSpan((int)offset, (int)length));
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
