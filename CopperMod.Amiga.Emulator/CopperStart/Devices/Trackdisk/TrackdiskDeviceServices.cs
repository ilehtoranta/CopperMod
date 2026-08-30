using System;
using System.Collections.Generic;
using Amiga;
using Copper68k;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart.Exec;
using PortableDevices = CopperStart.Devices;
using PortableExec = CopperStart.Exec;

namespace CopperMod.Amiga.CopperStart.Devices.Trackdisk;

internal readonly record struct TrackdiskRawTrack(ReadOnlyMemory<byte> Data, int BitLength) { public int ByteLength => (BitLength + 7) / 8; }
internal delegate bool TrackdiskLogicalWriteHandler(int unit, int byteOffset, ReadOnlySpan<byte> source);

/// <summary>Host media/gateway adapter for the portable trackdisk.device core.</summary>
internal sealed class TrackdiskDeviceServices : IDisposable
{
	internal const uint ChangeInterruptContinuationAddress = 0x00F0_8500;
	private readonly AmigaBus _bus; private readonly ExecMemoryContext _memory;
	private readonly Func<int, byte[]?> _getDriveData; private readonly TrackdiskLogicalWriteHandler _writeLogicalData;
	private readonly Func<int, TrackdiskRawTrack?> _getRawTrack; private readonly Func<int, TrackdiskRawTrack, bool> _writeRawTrack;
	private readonly Func<int, ulong> _getChangeVersion; private readonly Action<int> _ejectDrive; private readonly Func<int, bool> _isWriteProtected; private readonly Func<int, bool> _isMotorOn; private readonly Action<int, bool, long> _setMotor;
	private readonly Action<APTR> _replyMessage; private readonly List<(uint Address, uint Token)> _gateways = new();
	private readonly Action<APTR, APTR> _startChangeInterrupt;
	private APTR _state; private M68kCpuState? _activeState;

	public TrackdiskDeviceServices(AmigaBus bus, ExecMemoryContext memory, Func<int, byte[]?> getDriveData,
		TrackdiskLogicalWriteHandler writeLogicalData, Func<int, TrackdiskRawTrack?> getRawTrack,
		Func<int, TrackdiskRawTrack, bool> writeRawTrack, Func<int, ulong> getChangeVersion,
		Action<int> ejectDrive, Func<int, bool> isWriteProtected, Func<int, bool> isMotorOn,
		Action<int, bool, long> setMotor, Action<uint> replyMessage, Action<string> diagnostic)
	{
		_bus = bus ?? throw new ArgumentNullException(nameof(bus)); _memory = memory ?? throw new ArgumentNullException(nameof(memory)); _getDriveData = getDriveData ?? throw new ArgumentNullException(nameof(getDriveData)); _writeLogicalData = writeLogicalData ?? throw new ArgumentNullException(nameof(writeLogicalData)); _getRawTrack = getRawTrack ?? throw new ArgumentNullException(nameof(getRawTrack)); _writeRawTrack = writeRawTrack ?? throw new ArgumentNullException(nameof(writeRawTrack)); _getChangeVersion = getChangeVersion ?? throw new ArgumentNullException(nameof(getChangeVersion)); _ejectDrive = ejectDrive ?? throw new ArgumentNullException(nameof(ejectDrive)); _isWriteProtected = isWriteProtected ?? throw new ArgumentNullException(nameof(isWriteProtected)); _isMotorOn = isMotorOn ?? throw new ArgumentNullException(nameof(isMotorOn)); _setMotor = setMotor ?? throw new ArgumentNullException(nameof(setMotor)); ArgumentNullException.ThrowIfNull(replyMessage); _replyMessage = request => replyMessage(request.Raw); _startChangeInterrupt = StartChangeInterrupt; ArgumentNullException.ThrowIfNull(diagnostic);
	}
	public TrackdiskDeviceServices(AmigaBus bus, Func<int, byte[]?> getDriveData,
		TrackdiskLogicalWriteHandler writeLogicalData, Func<int, TrackdiskRawTrack?> getRawTrack,
		Func<int, TrackdiskRawTrack, bool> writeRawTrack, Func<int, ulong> getChangeVersion,
		Action<int> ejectDrive, Func<int, bool> isWriteProtected, Func<int, bool> isMotorOn,
		Action<int, bool, long> setMotor, Action<uint> replyMessage, Action<string> diagnostic)
		: this(bus, CreateCompatibilityMemory(bus), getDriveData, writeLogicalData, getRawTrack, writeRawTrack,
			getChangeVersion, ejectDrive, isWriteProtected, isMotorOn, setMotor, replyMessage, diagnostic) { }

	public uint DeviceBase { get; private set; }
	public bool IsInstalled => _gateways.Count != 0;
	public bool TryInstall(uint execBase)
	{
		if (IsInstalled || execBase == 0 || !_bus.IsMappedMemoryRange(execBase + (uint)ExecLayout.ExecBase.DeviceList, checked((int)global::Amiga.List.Size))) return IsInstalled;
		var device = FindDevice(execBase + (uint)ExecLayout.ExecBase.DeviceList, TrackDiskDevice.Name); if (device < 36 || !_bus.IsMappedMemoryRange(device - 36, 36)) return false;
		var allocation = _memory.Allocate((int)PortableDevices.TrackDiskDeviceCore.StateSize, (uint)(global::Amiga.Exec.MemoryFlags.Public | global::Amiga.Exec.MemoryFlags.Clear | global::Amiga.Exec.MemoryFlags.NoExpunge)); if (allocation == 0) return false;
		_state = APTR.FromPointer(allocation); var platform = CreatePlatform(); if (!PortableDevices.TrackDiskDeviceCore.Initialize(ref platform, _state)) { _memory.Free(allocation, (int)PortableDevices.TrackDiskDeviceCore.StateSize); _state = APTR.Null; return false; }
		DeviceBase = device; Register(TrackDiskDevice.Open, Open); Register(TrackDiskDevice.Close, Close); Register(TrackDiskDevice.Expunge, Expunge); Register(TrackDiskDevice.ExtFunc, ExtFunc); Register(TrackDiskDevice.BeginIO, BeginIo); Register(TrackDiskDevice.AbortIO, AbortIo); RegisterAddress(ChangeInterruptContinuationAddress, ContinueChangeInterrupt); return true;
	}
	public void Reset() => Dispose();
	public void ProcessPending(long cycle) { if (!IsInstalled || _state.IsNull) return; var platform = CreatePlatform(cycle); PortableDevices.TrackDiskDeviceCore.ProcessPending(ref platform, _state); }
	public void ProcessPending(M68kCpuState state)
	{
		ArgumentNullException.ThrowIfNull(state); if (!IsInstalled || _state.IsNull) return; _activeState = state; try { var platform = CreatePlatform(state.Cycles); PortableDevices.TrackDiskDeviceCore.ProcessPending(ref platform, _state); PortableDevices.TrackDiskDeviceCore.PollChanges(ref platform, _state); PortableDevices.TrackDiskDeviceCore.StartNextChangeInterrupt(ref platform, _state); } finally { _activeState = null; }
	}
	public void Dispose() { for (var index = _gateways.Count - 1; index >= 0; index--) _bus.RemoveHostGateway(_gateways[index].Address, _gateways[index].Token); _gateways.Clear(); if (_state.IsNotNull) _memory.Free(_state.Raw, (int)PortableDevices.TrackDiskDeviceCore.StateSize); _state = APTR.Null; _activeState = null; DeviceBase = 0; }

	private AmigaBusTrackDiskPlatform CreatePlatform(long cycle = 0) => new(_bus, cycle, _getDriveData, _writeLogicalData, _getRawTrack, _writeRawTrack, _getChangeVersion, _ejectDrive, _isWriteProtected, _isMotorOn, _setMotor, _replyMessage, _startChangeInterrupt);
	private void Open(M68kCpuState state) { var platform = CreatePlatform(state.Cycles); state.D[0] = PortableDevices.TrackDiskDeviceCore.Open(ref platform, _state, APTR.FromPointer(DeviceBase), APTR.FromPointer(state.A[1]), unchecked((int)state.D[0])); }
	private void Close(M68kCpuState state) { var platform = CreatePlatform(state.Cycles); state.D[0] = PortableDevices.TrackDiskDeviceCore.Close(ref platform, APTR.FromPointer(DeviceBase)); }
	private static void Expunge(M68kCpuState state) => state.D[0] = 0; private static void ExtFunc(M68kCpuState state) => state.D[0] = 0;
	private void BeginIo(M68kCpuState state) { var platform = CreatePlatform(state.Cycles); PortableDevices.TrackDiskDeviceCore.BeginIo(ref platform, _state, APTR.FromPointer(DeviceBase), APTR.FromPointer(state.A[1])); }
	private void AbortIo(M68kCpuState state) { var platform = CreatePlatform(state.Cycles); state.D[0] = PortableDevices.TrackDiskDeviceCore.AbortIo(ref platform, _state, APTR.FromPointer(state.A[1])); }
	private void StartChangeInterrupt(APTR code, APTR data) { if (_activeState is null) return; var resume = _activeState.ProgramCounter; _activeState.A[7] -= 4; _bus.WriteLong(_activeState.A[7], resume, _activeState.Cycles); _activeState.A[7] -= 4; _bus.WriteLong(_activeState.A[7], ChangeInterruptContinuationAddress, _activeState.Cycles); _activeState.A[1] = data.Raw; _activeState.ProgramCounter = code.Raw; }
	private void ContinueChangeInterrupt(M68kCpuState state) { var platform = CreatePlatform(state.Cycles); PortableDevices.TrackDiskDeviceCore.ContinueChangeInterrupt(ref platform, _state); }
	private void Register(int lvo, Action<M68kCpuState> callback) => RegisterAddress(unchecked((uint)((int)DeviceBase + lvo)), callback); private void RegisterAddress(uint address, Action<M68kCpuState> callback) => _gateways.Add((address, _bus.RegisterHostGateway(address, callback)));
	private uint FindDevice(uint list, string name) { var node = _bus.ReadLong(list); for (var count = 0; node != 0 && node != list + ExecLayout.List.Tail && count < 256; count++) { if (!_bus.IsMappedMemoryRange(node, ExecLayout.Node.Name + 4)) return 0; if (string.Equals(ReadName(_bus.ReadLong(node + ExecLayout.Node.Name)), name, StringComparison.OrdinalIgnoreCase)) return node; node = _bus.ReadLong(node); } return 0; }
	private string ReadName(uint address) { Span<char> value = stackalloc char[64]; var length = 0; while (address != 0 && length < value.Length && _bus.IsMappedMemoryRange(address + (uint)length, 1)) { var character = _bus.ReadByte(address + (uint)length); if (character == 0) break; value[length++] = (char)character; } return new string(value[..length]); }
	private static ExecMemoryContext CreateCompatibilityMemory(AmigaBus bus) => new(
		bus, (_, _) => 0x0007_E000, (_, _) => 0, (_, _) => { }, _ => 0, (_, _, _) => 0, (_, _, _) => { }, _ => 0,
		(_, _, _) => { }, (_, _, _) => { }, (_, _) => { }, () => 0, PortableExec.ExecMemoryAllocatorKind.Classic,
		() => 0, _ => { }, (_, _, _, _, _) => MemoryHandlerResult.DidNothing, _ => { }, _ => { });
}

internal struct AmigaBusTrackDiskPlatform : PortableDevices.ITrackDiskDevicePlatform
{
	private readonly AmigaBus _bus; private readonly long _cycle; private readonly Func<int, byte[]?> _getDriveData; private readonly TrackdiskLogicalWriteHandler _writeLogicalData; private readonly Func<int, TrackdiskRawTrack?> _getRawTrack; private readonly Func<int, TrackdiskRawTrack, bool> _writeRawTrack; private readonly Func<int, ulong> _getChangeVersion; private readonly Action<int> _ejectDrive; private readonly Func<int, bool> _isWriteProtected; private readonly Func<int, bool> _isMotorOn; private readonly Action<int, bool, long> _setMotor; private readonly Action<APTR> _reply; private readonly Action<APTR, APTR> _startInterrupt;
	public AmigaBusTrackDiskPlatform(AmigaBus bus, long cycle, Func<int, byte[]?> getDriveData, TrackdiskLogicalWriteHandler writeLogicalData, Func<int, TrackdiskRawTrack?> getRawTrack, Func<int, TrackdiskRawTrack, bool> writeRawTrack, Func<int, ulong> getChangeVersion, Action<int> ejectDrive, Func<int, bool> isWriteProtected, Func<int, bool> isMotorOn, Action<int, bool, long> setMotor, Action<APTR> reply, Action<APTR, APTR> startInterrupt) { _bus = bus; _cycle = cycle; _getDriveData = getDriveData; _writeLogicalData = writeLogicalData; _getRawTrack = getRawTrack; _writeRawTrack = writeRawTrack; _getChangeVersion = getChangeVersion; _ejectDrive = ejectDrive; _isWriteProtected = isWriteProtected; _isMotorOn = isMotorOn; _setMotor = setMotor; _reply = reply; _startInterrupt = startInterrupt; }
	public byte ReadUInt8(APTR a, int o = 0) => _bus.ReadByte(a.Raw + (uint)o); public ushort ReadUInt16(APTR a, int o = 0) => _bus.ReadWord(a.Raw + (uint)o); public uint ReadUInt32(APTR a, int o = 0) => _bus.ReadLong(a.Raw + (uint)o); public void WriteUInt8(APTR a, int o, byte v) => _bus.WriteByte(a.Raw + (uint)o, v, _cycle); public void WriteUInt16(APTR a, int o, ushort v) => _bus.WriteWord(a.Raw + (uint)o, v, _cycle); public void WriteUInt32(APTR a, int o, uint v) => _bus.WriteLong(a.Raw + (uint)o, v, _cycle); public bool IsMapped(APTR a, uint s) => s <= int.MaxValue && _bus.IsMappedMemoryRange(a.Raw, (int)s);
	public bool TrackDiskHasMedia(byte unit) => _getDriveData(unit) is not null; public uint TrackDiskMediaSize(byte unit) => (uint)(_getDriveData(unit)?.Length ?? 0);
	public bool TrackDiskRead(byte unit, uint offset, APTR destination, uint length) { var disk = _getDriveData(unit); if (disk is null) return false; _bus.CopyToMemory(destination.Raw, disk.AsSpan((int)offset, (int)length)); return true; }
	public bool TrackDiskWrite(byte unit, uint offset, APTR source, uint length) { var bytes = new byte[length]; _bus.CopyFromMemory(source.Raw, bytes); return _writeLogicalData(unit, (int)offset, bytes); }
	public uint TrackDiskRawSize(byte unit) => (uint)(_getRawTrack(unit)?.ByteLength ?? 0);
	public bool TrackDiskRawRead(byte unit, uint offset, APTR destination, uint length) { var track = _getRawTrack(unit); if (!track.HasValue) return false; _bus.CopyToMemory(destination.Raw, track.Value.Data.Span.Slice((int)offset, (int)length)); return true; }
	public bool TrackDiskRawWrite(byte unit, APTR source, uint length) { var bytes = new byte[length]; _bus.CopyFromMemory(source.Raw, bytes); return _writeRawTrack(unit, new TrackdiskRawTrack(bytes, checked((int)length * 8))); }
	public uint TrackDiskChangeVersion(byte unit) => unchecked((uint)_getChangeVersion(unit)); public bool TrackDiskWriteProtected(byte unit) => _isWriteProtected(unit); public bool TrackDiskMotorOn(byte unit) => _isMotorOn(unit); public void SetTrackDiskMotor(byte unit, bool enabled) => _setMotor(unit, enabled, _cycle); public void EjectTrackDisk(byte unit) => _ejectDrive(unit); public bool IsTrackDiskExecutable(APTR code) => _bus.IsCpuPhysicalAddressMapped(code.Raw, 2, AmigaBusAccessKind.CpuInstructionFetch); public void StartTrackDiskChangeInterrupt(APTR code, APTR data) => _startInterrupt(code, data); public void ReplyMessage(APTR request) => _reply(request);
}
