using System;
using System.Collections.Generic;
using Amiga;
using Copper68k;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart.Exec;
using CopperMod.Amiga.Input;
using PortableDevices = CopperStart.Devices;

namespace CopperMod.Amiga.CopperStart.Devices.Keyboard;

/// <summary>Host discovery, gateway, and guest-call adapter for portable keyboard.device.</summary>
internal sealed class KeyboardDeviceServices : IDisposable
{
	private const uint InputContinuationAddress = 0x00F0_8600, ResetHandlerContinuationAddress = 0x00F0_8610;
	private const uint InputOpenContinuationAddress = 0x00F0_8620;
	private const uint InputCloseContinuationAddress = 0x00F0_8630;
	private const int ScratchRequestBytes = (int)IOStdReq.Size;
	private const int ScratchEventBytes = (int)InputEvent.Size;
	private readonly AmigaBus _bus;
	private readonly ExecMemoryContext _memory;
	private readonly Action<APTR> _replyMessage;
	private readonly Action<string> _diagnostic;
	private readonly Action<M68kCpuState, uint, uint> _startGuestSubroutine;
	private readonly Action _requestSystemReset;
	private readonly List<(uint Address, uint Token)> _gateways = new();
	private readonly M68kCpuState _inputCallerContext = new();
	private APTR _state, _scratch, _inputReplyPort;
	private bool _inputDispatchActive, _inputCallActive;
	private M68kCpuState? _activeState;

	public KeyboardDeviceServices(AmigaBus bus, ExecMemoryContext memory, Action<uint> replyMessage,
		Action<string> diagnostic, Action<M68kCpuState, uint, uint>? startGuestSubroutine = null,
		Action? requestSystemReset = null)
	{
		_bus = bus ?? throw new ArgumentNullException(nameof(bus)); _memory = memory ?? throw new ArgumentNullException(nameof(memory));
		ArgumentNullException.ThrowIfNull(replyMessage); _replyMessage = request => replyMessage(request.Raw);
		_diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
		_startGuestSubroutine = startGuestSubroutine ?? ((_, _, _) => { }); _requestSystemReset = requestSystemReset ?? (() => { });
	}

	public uint DeviceBase { get; private set; }
	public uint InputDeviceBase { get; private set; }
	public bool IsInstalled => _gateways.Count != 0;

	public bool TryInstall(uint execBase)
	{
		if (IsInstalled || execBase == 0 || !_bus.IsMappedMemoryRange(execBase + (uint)ExecLayout.ExecBase.DeviceList, checked((int)global::Amiga.List.Size))) return IsInstalled;
		var keyboard = FindDevice(execBase + (uint)ExecLayout.ExecBase.DeviceList, KeyboardDevice.Name); var input = FindDevice(execBase + (uint)ExecLayout.ExecBase.DeviceList, InputDevice.Name);
		if (keyboard < 36 || input < 30 || !_bus.IsMappedMemoryRange(keyboard - 36, 36) || !_bus.IsMappedMemoryRange(input - 30, 30)) return false;
		var allocationSize = PortableDevices.KeyboardDeviceCore.StateSize + ScratchRequestBytes + ScratchEventBytes + MsgPort.Size;
		var allocation = _memory.Allocate((int)allocationSize, (uint)(global::Amiga.Exec.MemoryFlags.Public | global::Amiga.Exec.MemoryFlags.Clear | global::Amiga.Exec.MemoryFlags.NoExpunge));
		if (allocation == 0) return false;
		_state = APTR.FromPointer(allocation); _scratch = APTR.FromPointer(allocation + PortableDevices.KeyboardDeviceCore.StateSize);
		_inputReplyPort = APTR.FromPointer(_scratch.Raw + ScratchRequestBytes + ScratchEventBytes);
		var platform = CreatePlatform();
		if (!PortableDevices.KeyboardDeviceCore.Initialize(ref platform, _state)) { _memory.Free(allocation, (int)allocationSize); _state = _scratch = APTR.Null; return false; }
		DeviceBase = keyboard; InputDeviceBase = input;
		Register(KeyboardDevice.Open, Open); Register(KeyboardDevice.Close, Close); Register(KeyboardDevice.Expunge, Expunge); Register(KeyboardDevice.ExtFunc, ExtFunc); Register(KeyboardDevice.BeginIO, BeginIo); Register(KeyboardDevice.AbortIO, AbortIo);
		RegisterAddress(InputContinuationAddress, ContinueNativeInput); RegisterAddress(ResetHandlerContinuationAddress, ContinueResetHandler);
		RegisterAddress(InputOpenContinuationAddress, ContinueInputOpen);
		RegisterAddress(InputCloseContinuationAddress, ContinueInputClose);
		return true;
	}

	public bool QueueKeyDown(AmigaRawKey key, long cycle = 0) { if (!IsInstalled || cycle > uint.MaxValue) return false; var platform = CreatePlatform(); return PortableDevices.KeyboardDeviceCore.QueueKeyDown(ref platform, _state, (byte)key, cycle < 0 ? 0u : (uint)cycle); }
	public bool QueueKeyUp(AmigaRawKey key, long cycle = 0) { if (!IsInstalled || cycle > uint.MaxValue) return false; var platform = CreatePlatform(); return PortableDevices.KeyboardDeviceCore.QueueKeyUp(ref platform, _state, (byte)key, cycle < 0 ? 0u : (uint)cycle); }
	public void ConfigureKeyRepeat(uint seconds, uint microseconds, bool period)
	{
		var micros = (Int128)seconds * 1_000_000 + Math.Min(microseconds, 999_999u); var cycles = (uint)Int128.Clamp((micros * _bus.RasterTiming.CpuClockHz) / 1_000_000, 1, uint.MaxValue);
		var platform = CreatePlatform(); PortableDevices.KeyboardDeviceCore.ConfigureRepeatCycles(ref platform, _state, cycles, period);
	}
	public long GetNextDeadline(long currentCycle, long targetCycle)
	{
		if (_state.IsNull || currentCycle > uint.MaxValue) return targetCycle; var platform = CreatePlatform(); var target = targetCycle < 0 ? 0u : targetCycle > uint.MaxValue ? uint.MaxValue : (uint)targetCycle;
		return PortableDevices.KeyboardDeviceCore.GetNextDeadline(ref platform, _state, currentCycle < 0 ? 0u : (uint)currentCycle, target);
	}

	public void ProcessPending(M68kCpuState state)
	{
		if (!IsInstalled || _inputCallActive || state.Cycles < 0 || state.Cycles > uint.MaxValue) return; _activeState = state;
		try
		{
			var platform = CreatePlatform(state); PortableDevices.KeyboardDeviceCore.ProcessReset(ref platform, _state, (uint)state.Cycles);
			if (PortableDevices.KeyboardDeviceCore.IsResetInProgress(ref platform, _state)) return;
			PortableDevices.KeyboardDeviceCore.ProcessPendingReads(ref platform, _state); PortableDevices.KeyboardDeviceCore.ProcessRepeat(ref platform, _state, (uint)state.Cycles);
			if (_inputDispatchActive)
			{
				var list = ExecMsgPortCodec.MessageListAddress(_inputReplyPort);
				if (ExecListCodec.ReadHead(ref platform, list) == _scratch &&
					ExecNodeCodec.Read(ref platform, _scratch).Type == (byte)NodeType.ReplyMessage)
				{
					_ = global::CopperStart.Exec.ExecListCore.RemHead(ref platform, list);
					_inputCallerContext.CopyTaskContextFrom(state);
					BeginInputCall(state, InputDevice.Close, InputCloseContinuationAddress);
				}
				return;
			}
			if (!_inputDispatchActive && PortableDevices.KeyboardDeviceCore.TryDequeueInput(ref platform, _state, out var raw, out var qualifier, out var seconds, out var microseconds)) StartNativeInput(state, raw, qualifier, seconds, microseconds);
		}
		finally { _activeState = null; }
	}

	public void Reset() => Dispose();
	public void Dispose()
	{
		for (var index = _gateways.Count - 1; index >= 0; index--) _bus.RemoveHostGateway(_gateways[index].Address, _gateways[index].Token); _gateways.Clear();
		if (_state.IsNotNull) _memory.Free(_state.Raw, (int)(PortableDevices.KeyboardDeviceCore.StateSize + ScratchRequestBytes + ScratchEventBytes + MsgPort.Size));
		_state = _scratch = _inputReplyPort = APTR.Null; DeviceBase = InputDeviceBase = 0; _inputDispatchActive = _inputCallActive = false; _activeState = null;
	}

	private AmigaBusExecMemoryPlatform CreatePlatform(M68kCpuState? state = null) => _memory.CreateKeyboardPlatform(_replyMessage, StartResetHandler, _requestSystemReset, state);
	private void Open(M68kCpuState state) { var platform = CreatePlatform(state); state.D[0] = PortableDevices.KeyboardDeviceCore.Open(ref platform, _state, APTR.FromPointer(DeviceBase), APTR.FromPointer(state.A[1]), state.D[0]); }
	private void Close(M68kCpuState state) { var platform = CreatePlatform(state); state.D[0] = PortableDevices.KeyboardDeviceCore.Close(ref platform, _state, APTR.FromPointer(DeviceBase)); }
	private static void Expunge(M68kCpuState state) => state.D[0] = 0;
	private static void ExtFunc(M68kCpuState state) => state.D[0] = 0;
	private void BeginIo(M68kCpuState state) { var platform = CreatePlatform(state); PortableDevices.KeyboardDeviceCore.BeginIo(ref platform, _state, APTR.FromPointer(DeviceBase), APTR.FromPointer(state.A[1])); }
	private void AbortIo(M68kCpuState state) { var platform = CreatePlatform(state); state.D[0] = PortableDevices.KeyboardDeviceCore.AbortIo(ref platform, _state, APTR.FromPointer(state.A[1])); }

	private void StartNativeInput(M68kCpuState state, byte raw, ushort qualifier, uint seconds, uint microseconds)
	{
		var request = _scratch;
		var input = APTR.FromPointer(_scratch.Raw + ScratchRequestBytes);
		var platform = CreatePlatform(state);
		var replyList = ExecMsgPortCodec.MessageListAddress(_inputReplyPort);
		ExecMsgPortCodec.Write(ref platform, _inputReplyPort, new MsgPort
		{
			Node = new Node { Type = (byte)NodeType.MessagePort },
			Flags = PortFlags.Ignore,
			MessageList = new global::Amiga.List
			{
				Head = ExecListCodec.TailAddress(replyList),
				TailPred = replyList,
			},
		});
		ExecIORequestCodec.WriteStandardRequest(ref platform, request, new IOStdReq
		{
			Message = new Message
			{
				Node = new Node { Type = (byte)NodeType.Message },
				Length = (ushort)IOStdReq.Size,
				ReplyPort = _inputReplyPort,
			},
			Device = APTR.FromPointer(InputDeviceBase),
			Command = (DeviceCommand)InputDeviceCommand.WriteEvent,
			Flags = IOFlags.Quick,
			Length = InputEvent.Size,
			Data = input,
		});
		InputEventCodec.Write(ref platform, input, new InputEvent
		{
			Class = InputEventClass.RawKey,
			Code = raw,
			Qualifier = (InputEventQualifier)qualifier,
			TimeStamp = new TimeVal { Seconds = seconds, Microseconds = microseconds },
		});
		// This call is injected between instructions, not issued by the guest.
		// Native BeginIO may clobber volatile registers/CCR; restore the entire
		// interrupted context at its continuation, without rolling back time.
		_inputCallerContext.CopyTaskContextFrom(state);
		_inputDispatchActive = true;
		state.D[0] = 0; // unit number
		state.D[1] = 0; // open flags
		BeginInputCall(state, InputDevice.Open, InputOpenContinuationAddress);
	}

	private void BeginInputCall(M68kCpuState state, int lvo, uint continuation)
	{
		state.A[7] -= 4;
		_bus.WriteLong(state.A[7], continuation, state.Cycles);
		state.A[1] = _scratch.Raw;
		state.A[6] = InputDeviceBase;
		state.ProgramCounter = InputDeviceBase + unchecked((uint)lvo);
		_inputCallActive = true;
	}

	private void ContinueInputOpen(M68kCpuState state)
	{
		var platform = CreatePlatform(state);
		if (ExecIORequestCodec.ReadStandardRequest(ref platform, _scratch).Error != 0)
		{
			ContinueInputClose(state);
			return;
		}
		BeginInputCall(state, InputDevice.BeginIO, InputContinuationAddress);
	}
	private void ContinueNativeInput(M68kCpuState state)
	{
		if (!_inputDispatchActive) return;
		var platform = CreatePlatform(state);
		if ((ExecIORequestCodec.ReadStandardRequest(ref platform, _scratch).Flags & IOFlags.Quick) != 0)
		{
			BeginInputCall(state, InputDevice.Close, InputCloseContinuationAddress);
			return;
		}
		// BeginIO returning is not completion when the driver clears QUICK.
		// Keep both request and InputEvent alive until the private port gets its reply.
		state.CopyTaskContextFrom(_inputCallerContext);
		_inputCallActive = false;
	}

	private void ContinueInputClose(M68kCpuState state)
	{
		state.CopyTaskContextFrom(_inputCallerContext);
		_inputDispatchActive = _inputCallActive = false;
	}
	private void StartResetHandler(APTR code, APTR data, APTR interrupt)
	{
		_ = interrupt; if (_activeState is null) return; _activeState.A[1] = data.Raw; _startGuestSubroutine(_activeState, code.Raw, ResetHandlerContinuationAddress);
	}
	private void ContinueResetHandler(M68kCpuState state)
	{
		_activeState = state;
		try { var platform = CreatePlatform(state); PortableDevices.KeyboardDeviceCore.ContinueResetHandler(ref platform, _state, state.Cycles < 0 ? 0u : (uint)Math.Min(state.Cycles, uint.MaxValue)); }
		finally { _activeState = null; }
	}

	private void Register(int lvo, Action<M68kCpuState> callback) => RegisterAddress(unchecked((uint)((int)DeviceBase + lvo)), callback);
	private void RegisterAddress(uint address, Action<M68kCpuState> callback) => _gateways.Add((address, _bus.RegisterHostGateway(address, callback)));
	private uint FindDevice(uint list, string name) { var node = _bus.ReadLong(list); for (var count = 0; node != 0 && node != list + ExecLayout.List.Tail && count < 256; count++) { if (!_bus.IsMappedMemoryRange(node, ExecLayout.Node.Name + 4)) return 0; if (string.Equals(ReadName(_bus.ReadLong(node + ExecLayout.Node.Name)), name, StringComparison.OrdinalIgnoreCase)) return node; node = _bus.ReadLong(node); } return 0; }
	private string ReadName(uint address) { Span<char> value = stackalloc char[64]; var length = 0; while (address != 0 && length < value.Length && _bus.IsMappedMemoryRange(address + (uint)length, 1)) { var character = _bus.ReadByte(address + (uint)length); if (character == 0) break; value[length++] = (char)character; } return new string(value[..length]); }
}
