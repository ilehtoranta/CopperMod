using System;
using System.Collections.Generic;
using Amiga;
using Copper68k;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart.Exec;
using PortableDevices = CopperStart.Devices;
using PortableExec = CopperStart.Exec;

namespace CopperMod.Amiga.CopperStart.Devices.Audio;

/// <summary>Host discovery, gateway, and virtual-mixer adapter for the portable audio.device core.</summary>
internal sealed class AudioDeviceServices : IDisposable
{
	private readonly AmigaBus _bus; private readonly ExecMemoryContext _memory;
	private readonly Action<APTR> _replyMessage; private readonly List<(uint Address, uint Token)> _gateways = new();
	private APTR _state;

	public AudioDeviceServices(AmigaBus bus, ExecMemoryContext memory, Action<uint> replyMessage, Action<string> diagnostic)
	{
		_bus = bus ?? throw new ArgumentNullException(nameof(bus)); _memory = memory ?? throw new ArgumentNullException(nameof(memory));
		ArgumentNullException.ThrowIfNull(replyMessage); ArgumentNullException.ThrowIfNull(diagnostic); _replyMessage = request => replyMessage(request.Raw);
	}
	public AudioDeviceServices(AmigaBus bus, Action<uint> replyMessage, Action<string> diagnostic)
		: this(bus, CreateCompatibilityMemory(bus), replyMessage, diagnostic) { }

	public uint DeviceBase { get; private set; }
	public bool IsInstalled => _gateways.Count != 0;
	internal int GatewayRegistrationCount => _gateways.Count;

	public bool TryInstall(uint execBase)
	{
		if (IsInstalled || execBase == 0 || !_bus.IsMappedMemoryRange(execBase + (uint)ExecLayout.ExecBase.DeviceList, checked((int)global::Amiga.List.Size))) return IsInstalled;
		var device = FindDevice(execBase + (uint)ExecLayout.ExecBase.DeviceList, AudioDevice.Name); if (device < 36 || !_bus.IsMappedMemoryRange(device - 36, 36)) return false;
		var allocation = _memory.Allocate((int)PortableDevices.AudioDeviceCore.StateSize, (uint)(global::Amiga.Exec.MemoryFlags.Public | global::Amiga.Exec.MemoryFlags.Clear | global::Amiga.Exec.MemoryFlags.NoExpunge)); if (allocation == 0) return false;
		_state = APTR.FromPointer(allocation); var platform = CreatePlatform(); if (!PortableDevices.AudioDeviceCore.Initialize(ref platform, _state)) { _memory.Free(allocation, (int)PortableDevices.AudioDeviceCore.StateSize); _state = APTR.Null; return false; }
		DeviceBase = device; Register(-6, Open); Register(-12, Close); Register(-18, Expunge); Register(-24, ExtFunc); Register(-30, BeginIo); Register(-36, AbortIo); return true;
	}

	public long GetNextDeadline(long currentCycle, long targetCycle)
	{
		if (_state.IsNull) return targetCycle; var platform = CreatePlatform(); return PortableDevices.AudioDeviceCore.GetNextDeadline(ref platform, _state, ToCycle(currentCycle), ToCycle(targetCycle));
	}
	public void ProcessPending(M68kCpuState state) { ArgumentNullException.ThrowIfNull(state); if (_state.IsNull) return; var platform = CreatePlatform(state); PortableDevices.AudioDeviceCore.ProcessPending(ref platform, _state, ToCycle(state.Cycles)); }

	public void MixSample(long cycle, Span<float> destination, int frameIndex, int channels)
	{
		if (_state.IsNull || channels < 2 || frameIndex < 0 || frameIndex * channels + 1 >= destination.Length) return; var platform = CreatePlatform(); var now = ToCycle(cycle);
		for (var channel = 0; channel < 4; channel++) if (PortableDevices.AudioDeviceCore.TryGetPlayback(ref platform, _state, channel, now, out var data, out var byteIndex, out var volume)) { var sample = unchecked((sbyte)_bus.ReadByte(data.Raw + byteIndex)) / 128f; destination[frameIndex * channels + (channel is 0 or 3 ? 1 : 0)] += sample * (volume / 64f) * 0.25f; }
	}

	public void Reset() => Dispose();
	public void Dispose() { for (var index = _gateways.Count - 1; index >= 0; index--) _bus.RemoveHostGateway(_gateways[index].Address, _gateways[index].Token); _gateways.Clear(); if (_state.IsNotNull) _memory.Free(_state.Raw, (int)PortableDevices.AudioDeviceCore.StateSize); _state = APTR.Null; DeviceBase = 0; }

	private AmigaBusExecMemoryPlatform CreatePlatform(M68kCpuState? state = null) => _memory.CreateAudioPlatform(_replyMessage, _replyMessage, state);
	private void Open(M68kCpuState state) { var platform = CreatePlatform(state); state.D[0] = PortableDevices.AudioDeviceCore.Open(ref platform, _state, APTR.FromPointer(DeviceBase), APTR.FromPointer(state.A[1])); }
	private void Close(M68kCpuState state) { var platform = CreatePlatform(state); state.D[0] = PortableDevices.AudioDeviceCore.Close(ref platform, _state, APTR.FromPointer(DeviceBase), APTR.FromPointer(state.A[1]), ToCycle(state.Cycles)); }
	private static void Expunge(M68kCpuState state) => state.D[0] = 0; private static void ExtFunc(M68kCpuState state) => state.D[0] = 0;
	private void BeginIo(M68kCpuState state) { var platform = CreatePlatform(state); state.D[0] = PortableDevices.AudioDeviceCore.BeginIo(ref platform, _state, APTR.FromPointer(DeviceBase), APTR.FromPointer(state.A[1]), ToCycle(state.Cycles)); }
	private void AbortIo(M68kCpuState state) { var platform = CreatePlatform(state); state.D[0] = PortableDevices.AudioDeviceCore.AbortIo(ref platform, _state, APTR.FromPointer(state.A[1]), ToCycle(state.Cycles)); }
	private void Register(int lvo, Action<M68kCpuState> callback) { var address = unchecked((uint)((int)DeviceBase + lvo)); _gateways.Add((address, _bus.RegisterHostGateway(address, callback))); }
	private uint FindDevice(uint list, string name) { var node = _bus.ReadLong(list); for (var count = 0; node != 0 && node != list + ExecLayout.List.Tail && count < 256; count++) { if (!_bus.IsMappedMemoryRange(node, ExecLayout.Node.Name + 4)) return 0; if (string.Equals(ReadName(_bus.ReadLong(node + ExecLayout.Node.Name)), name, StringComparison.OrdinalIgnoreCase)) return node; node = _bus.ReadLong(node); } return 0; }
	private string ReadName(uint address) { Span<char> value = stackalloc char[64]; var length = 0; while (address != 0 && length < value.Length && _bus.IsMappedMemoryRange(address + (uint)length, 1)) { var character = _bus.ReadByte(address + (uint)length); if (character == 0) break; value[length++] = (char)character; } return new string(value[..length]); }
	private static uint ToCycle(long cycle) => cycle <= 0 ? 0 : cycle >= uint.MaxValue ? uint.MaxValue : (uint)cycle;
	private static ExecMemoryContext CreateCompatibilityMemory(AmigaBus bus) => new(
		bus, (_, _) => 0x0007_D000, (_, _) => 0, (_, _) => { }, _ => 0, (_, _, _) => 0, (_, _, _) => { }, _ => 0,
		(_, _, _) => { }, (_, _, _) => { }, (_, _) => { }, () => 0, PortableExec.ExecMemoryAllocatorKind.Classic,
		() => 0, _ => { }, (_, _, _, _, _) => MemoryHandlerResult.DidNothing, _ => { }, _ => { });
}
