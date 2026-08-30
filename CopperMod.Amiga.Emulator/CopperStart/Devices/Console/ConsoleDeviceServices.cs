using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Amiga;
using Copper68k;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart.Devices.Input;
using CopperMod.Amiga.CopperStart.Devices.Clipboard;
using CopperMod.Amiga.CopperStart.Exec;
using PortableDevices = CopperStart.Devices;

namespace CopperMod.Amiga.CopperStart.Devices.Console;

/// <summary>
/// Host implementation of the ROM-created console.device.  The device base
/// and the caller supplied Intuition Window remain guest owned; only the
/// console unit state and its I/O queues live here.
/// </summary>
internal sealed class ConsoleDeviceServices : IDisposable
{
    private const int WindowWidthOffset = 0x08, WindowHeightOffset = 0x0A, WindowFlagsOffset = 0x18, WindowBorderLeftOffset = 0x1A, WindowBorderTopOffset = 0x1C, WindowBorderRightOffset = 0x1E, WindowBorderBottomOffset = 0x20;
    private const int WindowRPortOffset = 0x32, RastPortFgPenOffset = 0x19, RastPortBgPenOffset = 0x1A, RastPortDrawModeOffset = 0x1C, RastPortCpXOffset = 0x24, RastPortCpYOffset = 0x26, RastPortTextHeightOffset = 0x3A, RastPortTextWidthOffset = 0x3C, RastPortTextBaselineOffset = 0x3E;
    private const int IntuitionActiveWindowOffset = 0x34;
    private const ushort CmdReset = 1, CmdRead = 2, CmdWrite = 3, CmdUpdate = 4, CmdClear = 5, CmdStop = 6, CmdStart = 7, CmdFlush = 8, CdAskKeyMap = 9, CdSetKeyMap = 10, CdAskDefaultKeyMap = 11, CdSetDefaultKeyMap = 12;
    private const byte IoQuick = 1, IoErrOpenFail = 0xFF, IoErrAborted = 0xFE, IoErrBadAddress = 0xFB, IeClassRawKey = 1, IeClassRawMouse = 2, IeCodeNoButton = 0xFF, IeCodeLeftButton = 0x68, IeCodeUpPrefix = 0x80;
	private const uint PublicClear = (uint)(global::Amiga.Exec.MemoryFlags.Public | global::Amiga.Exec.MemoryFlags.Clear);
    private const int DefaultColumns = 80, DefaultRows = 25;
    private const int ConuLibrary = -1, ConuStandard = 0, ConuCharMap = 1, ConuSnipMap = 3;
    private const uint ConFlagNoDrawOnNewSize = 1;
    private const uint WflgSimpleRefresh = 0x40;
    private const int ClipboardRequestBytes = 0x34, ClipboardBufferBytes = 4096;
    private const uint MapRawKeyContinuationAddress = 0x00F0_8910;
    // Public console.device RawKeyConvert() needs the same guest keymap
    // lookup as input delivery, but it enters it from a library gateway.
    // Keep its completion distinct from the input-handler conversion.
    private const uint RawKeyConvertContinuationAddress = 0x00F0_8918;
    private const uint GraphicsTextContinuationAddress = 0x00F0_8920;
    private const uint GraphicsClearContinuationAddress = 0x00F0_8930;
    private const uint GraphicsUnderlineContinuationAddress = 0x00F0_8940;
    private const uint IntuitionBeepContinuationAddress = 0x00F0_8950;
    private const uint GraphicsSoftStyleSetContinuationAddress = 0x00F0_8960;
    private const uint GraphicsSoftStyleResetContinuationAddress = 0x00F0_8968;
    private const uint FsfItalic = 0x04;
    private readonly AmigaBus _bus;
    private readonly ExecMemoryOperations _memory;
    private readonly InputDeviceServices _input;
    private readonly Action<uint> _reply;
    private readonly Action<M68kCpuState, uint, uint, uint> _drawText;
    private readonly Action<M68kCpuState, uint, uint> _startGuestSubroutine;
    private readonly List<(uint Address, uint Token)> _gateways = new();
    private readonly Dictionary<uint, ConsoleUnit> _units = new();
    private readonly List<uint> _windowUnitsInOpenOrder = new();
    private readonly Queue<QueuedInput> _rawInput = new();
    private readonly Queue<PendingWrite> _pendingWrites = new();
    private PendingKeyMapCall? _pendingKeyMapCall;
    private PendingRawKeyConvert? _pendingRawKeyConvert;
    private PendingRender? _pendingRender;
    private PendingClear? _pendingClear;
    private PendingRender? _pendingBell;
    private PendingSoftStyle? _pendingSoftStyle;
    private bool _resetSoftStyleAfterText;
    private uint _softStyleRastPort;
    private uint _defaultKeyMap;
    private uint _activeWindowUnit;
    private uint _execBase;
	private APTR _portableState;
	private M68kCpuState? _cpuState;
    private readonly record struct QueuedInput(InputDeviceServices.ObservedInputEvent Event, uint Unit);
    private readonly record struct PendingWrite(uint Request, uint Unit);
    private readonly record struct PendingKeyMapCall(uint Unit, uint InputGeneration, InputDeviceServices.ObservedInputEvent Input);
    private readonly record struct PendingRawKeyConvert(uint Buffer, uint Capacity);
    private readonly record struct RenderStyle(byte ForegroundPen, byte BackgroundPen, byte DrawMode, bool Bold, bool Underline, bool Italic);
    private readonly record struct RenderRun(int X, int Y, byte ForegroundPen, byte BackgroundPen, byte DrawMode, byte[] Bytes, bool UnderlineFill, bool CursorFill, bool Italic);
    private readonly record struct PendingRender(uint Request, uint Unit, uint Actual);
    private readonly record struct PendingClear(uint Request, uint Unit, bool RedrawAfterClear);
    private readonly record struct PendingSoftStyle(RenderRun Run);

    private sealed class ConsoleUnit
    {
		public ConsoleUnit(uint request, int number, uint window, uint rastPort, uint scratch, uint portableState = 0) { Request = request; Number = number; Window = window; RastPort = rastPort; Scratch = scratch; PortableState = portableState; }
		public uint Request { get; } public int Number { get; } public uint Window { get; } public uint RastPort { get; } public uint Scratch { get; } public uint PortableState { get; }
        public uint KeyMap { get; set; }
        public uint ClipboardRequest { get; set; } public uint ClipboardBuffer { get; set; }
        public Queue<byte> Input { get; } = new(); public uint InputGeneration { get; set; } public List<byte> History { get; } = new();
        public List<List<byte>> Lines { get; } = new() { new List<byte>() };
        public List<List<RenderStyle>> Styles { get; } = new() { new List<RenderStyle>() };
        public int CursorX { get; set; } public int CursorY { get; set; }
        public int SavedCursorX { get; set; } public int SavedCursorY { get; set; }
        public int RunStartX { get; set; } public int RunStartY { get; set; }
        public int TopMargin { get; set; } public int BottomMargin { get; set; } = int.MaxValue;
        public int Columns { get; set; } = DefaultColumns; public int Rows { get; set; } = DefaultRows;
        public int TextWidth { get; set; } = 8; public int TextHeight { get; set; } = 8; public int TextBaseline { get; set; } = 7;
        public int OriginX { get; set; } public int OriginBaseline { get; set; } = 7;
        // Amiga-private CSI t/u/x/y controls.  A zero value means that the
        // window's ordinary inner size supplies that dimension/offset.
        public int PageHeightPixels { get; set; }
        public int LineLengthCharacters { get; set; }
        public int LeftOffsetPixels { get; set; }
        public int TopOffsetPixels { get; set; }
        public bool InsertMode { get; set; } public bool WrapMode { get; set; } = true; public bool OriginMode { get; set; } public bool ScrollEnabled { get; set; } = true;
        public bool ReturnOnLineFeed { get; set; } public bool CursorVisible { get; set; } = true;
        public bool HighBitCharacters { get; set; }
        public byte ForegroundPen { get; set; } = 1; public byte BackgroundPen { get; set; } public bool BackgroundActive { get; set; }
        public byte ConsoleBackgroundPen { get; set; }
        public byte DefaultForegroundPen { get; set; } = 1; public byte DefaultBackgroundPen { get; set; }
        public bool DefaultBackgroundActive { get; set; } public bool DefaultBold { get; set; } public bool DefaultFaint { get; set; } public bool DefaultUnderline { get; set; } public bool DefaultItalic { get; set; } public bool DefaultInverse { get; set; } public bool DefaultConcealed { get; set; }
        public bool Bold { get; set; } public bool Faint { get; set; } public bool Underline { get; set; } public bool Italic { get; set; } public bool Inverse { get; set; } public bool Concealed { get; set; }
        public byte SavedForegroundPen { get; set; } public byte SavedBackgroundPen { get; set; }
        public bool SavedBackgroundActive { get; set; } public bool SavedBold { get; set; } public bool SavedFaint { get; set; } public bool SavedUnderline { get; set; } public bool SavedItalic { get; set; } public bool SavedInverse { get; set; } public bool SavedConcealed { get; set; }
        public bool Escape { get; set; } public bool CsiActive { get; set; } public List<byte> Csi { get; } = new();
        public bool Selecting { get; set; }
        public int SelectionAnchorX { get; set; } public int SelectionAnchorY { get; set; }
        public int SelectionCaretX { get; set; } public int SelectionCaretY { get; set; }
        public int PointerX { get; set; } public int PointerY { get; set; }
        public HashSet<int> TabStops { get; } = new();
        public HashSet<int> RawEventTypes { get; } = new();
        public Queue<RenderRun> PendingRenders { get; } = new();
        public bool NeedsRedraw { get; set; }
        public bool NoDrawOnNewSize { get; set; }
        public bool Stopped { get; set; }
        public int PendingBells { get; set; }
    }

    public ConsoleDeviceServices(CopperStartConsoleContext context)
        : this(context.Bus, context.Memory, context.Input, context.Reply, context.DrawText, context.StartGuestSubroutine) { }

    public ConsoleDeviceServices(AmigaBus bus, ExecMemoryOperations memory, InputDeviceServices input, Action<uint> reply, Action<M68kCpuState, uint, uint, uint> drawText)
        : this(bus, memory, input, reply, drawText, (_, _, _) => { }) { }

    private ConsoleDeviceServices(AmigaBus bus, ExecMemoryOperations memory, InputDeviceServices input, Action<uint> reply, Action<M68kCpuState, uint, uint, uint> drawText, Action<M68kCpuState, uint, uint> startGuestSubroutine)
    { _bus = bus; _memory = memory; _input = input; _reply = reply; _drawText = drawText; _startGuestSubroutine = startGuestSubroutine; _input.InputEventObserved += ObserveInputEvent; }

    public uint DeviceBase { get; private set; }
    public bool IsInstalled => _gateways.Count != 0;

    /// <summary>
    /// Lets Intuition ownership select the console attached to a newly active
    /// Window.  Until that notification is available, opening a windowed
    /// console makes it active, matching the common single-CON: case.
    /// </summary>
    public void SetActiveWindow(uint window)
    {
        var unit = _units.Values.LastOrDefault(candidate => candidate.Window == window && candidate.Window != 0);
        if (unit is not null) _activeWindowUnit = unit.Request;
    }

    /// <summary>
    /// CopperStart's DOS compatibility layer uses the same parser and input
    /// queue as console.device.  It deliberately has no synthetic Window:
    /// ROM console opens remain the only path that owns an Intuition window.
    /// </summary>
    public bool OpenCompatibilitySession(uint session)
    {
        if (_units.ContainsKey(session)) return true;
		var scratch = _memory.Allocate(256, PublicClear);
        if (scratch == 0) return false;
        _units.Add(session, new ConsoleUnit(session, -1, 0, 0, scratch) { KeyMap = _defaultKeyMap });
        return true;
    }

    public void CloseCompatibilitySession(uint session)
    {
        if (_units.Remove(session, out var unit)) FreeUnitMemory(unit);
    }

    public uint WriteCompatibility(M68kCpuState state, uint session, uint source, uint length)
    {
        if (!_units.TryGetValue(session, out var unit) || source == 0 || length > 0x10000 || !_bus.IsMappedMemoryRange(source, checked((int)length))) return 0;
        var run = new List<byte>();
        for (uint index = 0; index < length; index++) ParseOutput(unit, _bus.ReadByte(source + index), run, state);
        FlushRun(unit, run, state);
        return length;
    }

    public uint ReadCompatibility(M68kCpuState state, uint session, uint destination, uint length)
    {
        if (!_units.TryGetValue(session, out var unit) || destination == 0 || !_bus.IsMappedMemoryRange(destination, checked((int)Math.Min(length, int.MaxValue)))) return 0;
        var actual = 0u;
        while (actual < length && unit.Input.Count != 0) _bus.WriteByte(destination + actual++, unit.Input.Dequeue(), state.Cycles);
        return actual;
    }

	public bool HasCompatibilityInput(uint session) =>
		_units.TryGetValue(session, out var unit) && unit.Input.Count != 0;

    /// <summary>Completes input queued by the input handler at an instruction boundary.</summary>
    public void ProcessPending(M68kCpuState state)
    {
		_cpuState = state;
        StartNextKeyMapCall(state);
        foreach (var unit in _units.Values)
        {
            // Standard consoles deliberately do not retain/replay an exposed
            // window.  Character-map units do, which is their defining extra
            // cost and is why they require SIMPLE_REFRESH windows.
            if (UpdateGeometry(unit) && (unit.Number is ConuCharMap or ConuSnipMap) && !unit.NoDrawOnNewSize) unit.NeedsRedraw = true;
            CompleteUnitReads(unit, state.Cycles);
        }
        if (_pendingRender is null && _pendingClear is null)
        {
            var redraw = _units.Values.FirstOrDefault(unit => unit.NeedsRedraw && unit.RastPort != 0);
            if (redraw is not null) StartRedraw(0, redraw, state);
        }
    }

    public bool TryInstall(uint execBase)
    {
		if (IsInstalled || execBase == 0 || !_bus.IsMappedMemoryRange(execBase + ExecLayout.ExecBase.DeviceList, (int)global::Amiga.List.Size)) return IsInstalled;
		var device = FindDevice(execBase + ExecLayout.ExecBase.DeviceList, "console.device");
        if (device < 48 || !_bus.IsMappedMemoryRange(device - 48, 48)) return false;
		var portableState = _memory.Allocate((int)PortableDevices.ConsoleDeviceCore.StateSize, PublicClear); if (portableState == 0) return false;
		_portableState = APTR.FromPointer(portableState); var portablePlatform = new AmigaBusConsolePlatform(_bus, 0, _reply); if (!PortableDevices.ConsoleDeviceCore.Initialize(ref portablePlatform, _portableState)) { _memory.Free(portableState, (int)PortableDevices.ConsoleDeviceCore.StateSize); _portableState = APTR.Null; return false; }
        DeviceBase = device; _execBase = execBase;
        Register(-6, Open); Register(-12, Close); Register(-18, Expunge); Register(-24, ExtFunc); Register(-30, BeginIo); Register(-36, AbortIo);
        Register(-42, CdInputHandler); Register(-48, RawKeyConvert);
        RegisterAddress(MapRawKeyContinuationAddress, ContinueMapRawKey);
        RegisterAddress(RawKeyConvertContinuationAddress, ContinueRawKeyConvert);
        RegisterAddress(GraphicsTextContinuationAddress, ContinueGraphicsText);
        RegisterAddress(GraphicsClearContinuationAddress, ContinueGraphicsClear);
        RegisterAddress(GraphicsUnderlineContinuationAddress, ContinueGraphicsUnderline);
        RegisterAddress(IntuitionBeepContinuationAddress, ContinueIntuitionBeep);
        RegisterAddress(GraphicsSoftStyleSetContinuationAddress, ContinueGraphicsSoftStyleSet);
        RegisterAddress(GraphicsSoftStyleResetContinuationAddress, ContinueGraphicsSoftStyleReset);
        return true;
    }

    public void Reset() => ClearResetState();
    public void Dispose()
    {
        ClearResetState();
        _input.InputEventObserved -= ObserveInputEvent;
    }

    private void ClearResetState()
    {
        for (var index = _gateways.Count - 1; index >= 0; index--) _bus.RemoveHostGateway(_gateways[index].Address, _gateways[index].Token);
        foreach (var unit in _units.Values) FreeUnitMemory(unit);
		if (_portableState.IsNotNull) _memory.Free(_portableState.Raw, (int)PortableDevices.ConsoleDeviceCore.StateSize);
		_gateways.Clear(); _units.Clear(); _windowUnitsInOpenOrder.Clear(); _pendingWrites.Clear(); _rawInput.Clear(); _pendingKeyMapCall = null; _pendingRawKeyConvert = null; _pendingRender = null; _pendingClear = null; _pendingBell = null; _pendingSoftStyle = null; _resetSoftStyleAfterText = false; _softStyleRastPort = 0; _activeWindowUnit = 0; _portableState = APTR.Null; _cpuState = null; DeviceBase = 0; _execBase = 0;
    }

    private void Open(M68kCpuState state)
    {
		_cpuState = state;
        var request = state.A[1]; var number = unchecked((int)state.D[0]);
		var window = request != 0 && _bus.IsMappedMemoryRange(request + ExecLayout.IOStdReq.Data, 4) ? _bus.ReadLong(request + ExecLayout.IOStdReq.Data) : 0;
        var libraryOnly = number == ConuLibrary;
        var rastPort = window != 0 && _bus.IsMappedMemoryRange(window + WindowRPortOffset, 4) ? _bus.ReadLong(window + WindowRPortOffset) : 0;
		var scratch = _memory.Allocate(256, PublicClear);
		var portableUnit = _memory.Allocate((int)PortableDevices.ConsoleDeviceCore.UnitStateSize, PublicClear);
        var needsCharacterMap = number is ConuCharMap or ConuSnipMap;
        var simpleRefresh = window != 0 && _bus.IsMappedMemoryRange(window + WindowFlagsOffset, 4) && (_bus.ReadLong(window + WindowFlagsOffset) & WflgSimpleRefresh) != 0;
		if (request == 0 || scratch == 0 || portableUnit == 0 || _units.ContainsKey(request) || number is not (ConuLibrary or ConuStandard or ConuCharMap or ConuSnipMap) || (!libraryOnly && (window == 0 || rastPort == 0)) || (needsCharacterMap && !simpleRefresh)) { if (scratch != 0) _memory.Free(scratch, 256); if (portableUnit != 0) _memory.Free(portableUnit, (int)PortableDevices.ConsoleDeviceCore.UnitStateSize); Complete(request, IoErrOpenFail, 0, state.Cycles, false); state.D[0] = IoErrOpenFail; return; }
		var portablePlatform = new AmigaBusConsolePlatform(_bus, state.Cycles, _reply);
		var openResult = PortableDevices.ConsoleDeviceCore.Open(ref portablePlatform, _portableState, APTR.FromPointer(DeviceBase), APTR.FromPointer(request), APTR.FromPointer(portableUnit), number, state.D[1], APTR.FromPointer(window), APTR.FromPointer(rastPort), APTR.FromPointer(_defaultKeyMap));
		if (openResult != 0) { _memory.Free(scratch, 256); _memory.Free(portableUnit, (int)PortableDevices.ConsoleDeviceCore.UnitStateSize); state.D[0] = openResult; return; }
		var unit = new ConsoleUnit(request, number, window, rastPort, scratch, portableUnit)
        {
            KeyMap = _defaultKeyMap,
            NoDrawOnNewSize = (number is ConuCharMap or ConuSnipMap) && (state.D[1] & ConFlagNoDrawOnNewSize) != 0
        };
        ResetTabs(unit);
        UpdateGeometry(unit);
        _units[request] = unit;
        if (!libraryOnly) { _windowUnitsInOpenOrder.Add(request); _activeWindowUnit = request; }
        Complete(request, 0, 0, state.Cycles, false); state.D[0] = 0;
    }

    private void Close(M68kCpuState state)
    {
        var request = state.A[1];
        if (TryGetUnit(request, out var unit))
        {
			CancelRender(unit.Request, state.Cycles, completeRequest: false);
			CancelClear(unit.Request, state.Cycles, completeRequest: false);
			RemoveQueuedPresentationWrites(unit.Request);
			var portablePlatform = new AmigaBusConsolePlatform(_bus, state.Cycles, _reply); PortableDevices.ConsoleDeviceCore.Close(ref portablePlatform, _portableState, APTR.FromPointer(DeviceBase), APTR.FromPointer(request));
            _units.Remove(unit.Request);
            _windowUnitsInOpenOrder.Remove(unit.Request);
            if (_activeWindowUnit == unit.Request) _activeWindowUnit = _windowUnitsInOpenOrder.LastOrDefault();
            FreeUnitMemory(unit);
        }
		state.D[0] = 0;
    }
    private static void Expunge(M68kCpuState state) => state.D[0] = 0;
    private static void ExtFunc(M68kCpuState state) => state.D[0] = 0;

    private void BeginIo(M68kCpuState state)
    {
		_cpuState = state;
		var request = state.A[1];
		if (!TryGetUnit(request, out var unit) || _bus.ReadLong(request + ExecLayout.IORequest.Device) != DeviceBase) { state.D[0] = uint.MaxValue; return; }
		var command = _bus.ReadWord(request + ExecLayout.IORequest.Command);
		if (command is CmdReset or CmdFlush)
		{
			CancelRender(unit.Request, state.Cycles, completeRequest: false);
			CancelClear(unit.Request, state.Cycles, completeRequest: false);
			RemoveQueuedPresentationWrites(unit.Request);
		}
		if (command is CmdReset or CmdClear) ClearInput(unit);
		var platform = new AmigaBusConsolePlatform(_bus, state.Cycles, _reply, this, state);
		state.D[0] = 0;
		var result = PortableDevices.ConsoleDeviceCore.BeginIo(ref platform, _portableState,
			APTR.FromPointer(DeviceBase), APTR.FromPointer(request));
		if (result != 0) state.D[0] = result;
		if (result == 0 && command == CdSetKeyMap) unit.KeyMap = _bus.ReadLong(request + ExecLayout.IOStdReq.Data);
		if (result == 0 && command == CdSetDefaultKeyMap) _defaultKeyMap = _bus.ReadLong(request + ExecLayout.IOStdReq.Data);
		if (command == CmdStop) unit.Stopped = true;
		if (command == CmdStart) unit.Stopped = false;
		CompleteUnitReads(unit, state.Cycles);
    }

	internal bool StartPortableWrite(APTR unitState, APTR request, APTR data, uint length, M68kCpuState state)
	{
		var unit = _units.Values.FirstOrDefault(candidate => candidate.PortableState == unitState.Raw);
		if (unit is null) return false;
		Write(request.Raw, unit, state, portableCore: true);
		return false;
	}

	internal bool StartPortableClear(APTR unitState, APTR request, bool redraw, M68kCpuState state)
	{
		var unit = _units.Values.FirstOrDefault(candidate => candidate.PortableState == unitState.Raw);
		if (unit is null) return false;
		if (redraw) StartRedraw(request.Raw, unit, state);
		else { ClearUnit(unit); StartVisualClear(request.Raw, unit, state); }
		return false;
	}

    private void StartVisualClear(uint request, ConsoleUnit unit, M68kCpuState state, bool redrawAfterClear = false)
    {
        var graphics = FindLibrary(_execBase, "graphics.library");
        if (unit.RastPort == 0 || graphics == 0 ||
            !_bus.IsCpuPhysicalAddressMapped(graphics - 0xEA, 2, AmigaBusAccessKind.CpuInstructionFetch))
        {
            if (redrawAfterClear) StartNextRender(state);
            else Complete(request, 0, 0, state.Cycles, true);
            return;
        }

        _pendingClear = new PendingClear(request, unit.Request, redrawAfterClear);
        MarkAsynchronous(request, state.Cycles);
        state.A[1] = unit.RastPort;
        state.D[0] = unit.ConsoleBackgroundPen;
        _startGuestSubroutine(state, graphics - 0xEA, GraphicsClearContinuationAddress);
    }

    private void ContinueGraphicsClear(M68kCpuState state)
    {
        var pending = _pendingClear;
        _pendingClear = null;
        if (pending is not { } clear) return;
        if (clear.RedrawAfterClear) StartNextRender(state);
        else { Complete(clear.Request, 0, 0, state.Cycles, true); StartNextQueuedWrite(state); }
    }

    private void CancelClear(uint unitAddress, long cycle, bool completeRequest = true)
    {
        if (_pendingClear is not { } pending || pending.Unit != unitAddress) return;
        _pendingClear = null;
        if (completeRequest) Complete(pending.Request, IoErrAborted, 0, cycle, true);
    }

	private void RemoveQueuedPresentationWrites(uint unitAddress)
	{
		if (_pendingWrites.Count == 0) return;
		var retained = new Queue<PendingWrite>();
		while (_pendingWrites.TryDequeue(out var pending)) if (pending.Unit != unitAddress) retained.Enqueue(pending);
		while (retained.TryDequeue(out var pending)) _pendingWrites.Enqueue(pending);
	}

	private void RemoveQueuedPresentationWrite(uint request)
	{
		if (_pendingWrites.Count == 0) return;
		var retained = new Queue<PendingWrite>();
		while (_pendingWrites.TryDequeue(out var pending)) if (pending.Request != request) retained.Enqueue(pending);
		while (retained.TryDequeue(out var pending)) _pendingWrites.Enqueue(pending);
	}

    private void StartNextQueuedWrite(M68kCpuState state)
    {
        if (_pendingRender is not null || _pendingClear is not null) return;
        var remaining = _pendingWrites.Count;
        while (remaining-- != 0 && _pendingWrites.TryDequeue(out var pending))
        {
            if (_units.TryGetValue(pending.Unit, out var unit))
            {
                if (unit.Stopped) { _pendingWrites.Enqueue(pending); continue; }
                Write(pending.Request, unit, state, portableCore: true);
                return;
            }
            Complete(pending.Request, IoErrAborted, 0, state.Cycles, true);
        }
    }

    private static void ClearUnit(ConsoleUnit unit)
    {
        unit.Input.Clear(); unit.InputGeneration++; unit.History.Clear(); ClearScreen(unit); unit.NeedsRedraw = false; unit.Csi.Clear();
        unit.SavedCursorX = unit.SavedCursorY = 0;
        unit.Escape = unit.CsiActive = false; unit.RawEventTypes.Clear();
        unit.Selecting = false; unit.SelectionAnchorX = unit.SelectionAnchorY = unit.SelectionCaretX = unit.SelectionCaretY = 0;
        unit.HighBitCharacters = false;
        ResetTabs(unit);
        unit.InsertMode = unit.OriginMode = unit.ReturnOnLineFeed = false; unit.WrapMode = unit.ScrollEnabled = unit.CursorVisible = true;
        unit.ForegroundPen = unit.DefaultForegroundPen = 1;
        unit.BackgroundPen = unit.DefaultBackgroundPen = unit.ConsoleBackgroundPen = 0;
        unit.BackgroundActive = unit.DefaultBackgroundActive = false;
        unit.Bold = unit.DefaultBold = unit.Faint = unit.DefaultFaint = false;
        unit.Underline = unit.DefaultUnderline = unit.Italic = unit.DefaultItalic = false;
        unit.Inverse = unit.DefaultInverse = unit.Concealed = unit.DefaultConcealed = false;
        unit.SavedForegroundPen = 1; unit.SavedBackgroundPen = 0;
        unit.SavedBackgroundActive = unit.SavedBold = unit.SavedFaint = unit.SavedUnderline = unit.SavedItalic = unit.SavedInverse = unit.SavedConcealed = false;
        unit.TopMargin = 0; unit.BottomMargin = int.MaxValue;
        unit.PageHeightPixels = unit.LineLengthCharacters = unit.LeftOffsetPixels = unit.TopOffsetPixels = 0;
        unit.Stopped = false;
        unit.PendingBells = 0;
    }

    private void ClearInput(ConsoleUnit unit)
    {
        unit.Input.Clear();
        unit.InputGeneration++;
        if (_rawInput.Count == 0) return;
        var retained = new Queue<QueuedInput>();
        while (_rawInput.TryDequeue(out var input))
        {
            if (input.Unit != unit.Request) retained.Enqueue(input);
        }
        while (retained.TryDequeue(out var input)) _rawInput.Enqueue(input);
    }

    private static void ClearScreen(ConsoleUnit unit)
    {
        unit.Lines.Clear();
        unit.Lines.Add(new List<byte>());
        unit.Styles.Clear();
        unit.Styles.Add(new List<RenderStyle>());
        unit.CursorX = unit.CursorY = 0;
        unit.NeedsRedraw = false;
    }

    private static void EnsureRow(ConsoleUnit unit, int row)
    {
        while (unit.Lines.Count <= row)
        {
            unit.Lines.Add(new List<byte>());
            unit.Styles.Add(new List<RenderStyle>());
        }
    }

    private static RenderStyle CurrentStyle(ConsoleUnit unit)
    {
        var foreground = unit.Inverse ? unit.BackgroundPen : unit.Faint ? unit.BackgroundPen : unit.ForegroundPen;
        var background = unit.Inverse ? unit.ForegroundPen : unit.BackgroundPen;
        if (unit.Concealed) foreground = background;
        return new RenderStyle(foreground, background, unit.BackgroundActive || unit.Inverse ? (byte)2 : (byte)1, unit.Bold, unit.Underline, unit.Italic);
    }

    private void AbortIo(M68kCpuState state)
    {
		_cpuState = state;
        var request = state.A[1];
		if (TryGetUnit(request, out var unit))
		{
			RemoveQueuedPresentationWrite(request);
			if (_pendingRender is { } pending && pending.Request == request) CancelRender(unit.Request, state.Cycles, completeRequest: false);
			if (_pendingClear is { } clear && clear.Request == request) CancelClear(unit.Request, state.Cycles, completeRequest: false);
		}
		var platform = new AmigaBusConsolePlatform(_bus, state.Cycles, _reply, this, state);
		state.D[0] = PortableDevices.ConsoleDeviceCore.AbortIo(ref platform, _portableState, APTR.FromPointer(request));
    }

    private bool TryGetUnit(uint request, out ConsoleUnit unit)
    {
        unit = null!;
		if (request == 0 || !_bus.IsMappedMemoryRange(request + ExecLayout.IORequest.Unit, 4)) return false;
		var unitAddress = _bus.ReadLong(request + ExecLayout.IORequest.Unit);
        if (unitAddress != 0) return _units.TryGetValue(unitAddress, out unit!);
        return _units.TryGetValue(request, out unit!) && unit.Number == ConuLibrary;
    }

    private void Write(uint request, ConsoleUnit unit, M68kCpuState state, bool portableCore = false)
    {
        if ((!portableCore && unit.Stopped) || _pendingRender is not null || _pendingClear is not null)
        {
            _pendingWrites.Enqueue(new PendingWrite(request, unit.Request));
            MarkAsynchronous(request, state.Cycles);
            return;
        }
		var source = _bus.ReadLong(request + ExecLayout.IOStdReq.Data);
		if (!TryGetWriteLength(source, _bus.ReadLong(request + ExecLayout.IOStdReq.Length), out var length)) { Complete(request, IoErrBadAddress, 0, state.Cycles, true); return; }
        var run = new List<byte>();
        for (uint index = 0; index < length; index++) ParseOutput(unit, _bus.ReadByte(source + index), run, state);
        FlushRun(unit, run, state);
        if (unit.CursorVisible) unit.NeedsRedraw = true;
        if (unit.NeedsRedraw)
        {
            StartRedraw(request, unit, state, length);
            return;
        }
        if (unit.PendingRenders.Count == 0 && unit.PendingBells == 0) { Complete(request, 0, length, state.Cycles, true); return; }
        _pendingRender = new PendingRender(request, unit.Request, length);
        MarkAsynchronous(request, state.Cycles);
        StartNextRender(state);
    }

    private void StartRedraw(uint request, ConsoleUnit unit, M68kCpuState state, uint actual = 0)
    {
        if (_pendingRender is not null || _pendingClear is not null) return;
        unit.NeedsRedraw = false;
        unit.PendingRenders.Clear();
        for (var row = 0; row < unit.Lines.Count && row < unit.Rows; row++)
        {
            var line = unit.Lines[row];
            if (line.Count == 0) continue;
            QueueStyledLine(unit, row, line);
        }
        if (unit.CursorVisible)
        {
            var row = Math.Clamp(unit.CursorY, 0, Math.Max(0, unit.Rows - 1));
            var column = Math.Clamp(unit.CursorX, 0, Math.Max(0, unit.Columns - 1));
            var style = CurrentStyle(unit);
            unit.PendingRenders.Enqueue(new RenderRun(
                unit.OriginX + (column * unit.TextWidth),
                unit.OriginBaseline + (row * unit.TextHeight),
                style.ForegroundPen, style.BackgroundPen, style.DrawMode,
                new byte[1], UnderlineFill: false, CursorFill: true, Italic: false));
        }

        _pendingRender = new PendingRender(request, unit.Request, actual);
        if (request != 0) MarkAsynchronous(request, state.Cycles);
        StartVisualClear(request, unit, state, redrawAfterClear: true);
    }

    /// <summary>
    /// Console's documented length of -1 means a NUL-terminated byte string.
    /// Keep the scan bounded so a corrupt guest pointer cannot turn a host
    /// gateway into an unbounded memory walk.
    /// </summary>
    private bool TryGetWriteLength(uint source, uint requestedLength, out uint length)
    {
        length = 0;
        if (source == 0) return false;
        if (requestedLength != uint.MaxValue)
        {
            length = Math.Min(requestedLength, 0x10000u);
            return _bus.IsMappedMemoryRange(source, checked((int)length));
        }

        while (length < 0x10000 && _bus.IsMappedMemoryRange(source + length, 1))
        {
            if (_bus.ReadByte(source + length) == 0) return true;
            length++;
        }

        return false;
    }

    private void ParseOutput(ConsoleUnit unit, byte value, List<byte> run, M68kCpuState state)
    {
        // ECMA-48 CAN and SUB cancel an in-progress escape sequence.  ESC is
        // also a fresh escape introducer, even if it arrives in a malformed
        // CSI sequence.  Without this, a truncated application control
        // sequence can consume the next valid console command.
        if (value is 0x18 or 0x1A)
        {
            FlushRun(unit, run, state);
            unit.Csi.Clear();
            unit.CsiActive = unit.Escape = false;
            return;
        }
        if (value == 0x1B)
        {
            FlushRun(unit, run, state);
            unit.Csi.Clear();
            unit.CsiActive = false;
            unit.Escape = true;
            return;
        }
        if (unit.CsiActive)
        {
            unit.Csi.Add(value); if (value is >= 0x40 and <= 0x7E) { ApplyCsi(unit, value); unit.Csi.Clear(); unit.CsiActive = false; unit.Escape = false; } return;
        }
        if (unit.Escape)
        {
            unit.Escape = false;
            if (value == (byte)'[') { unit.Csi.Clear(); unit.CsiActive = true; return; }
            if (value == (byte)'7') { SaveCursorAndRendition(unit); return; }
            if (value == (byte)'8') { RestoreCursorAndRendition(unit); return; }
            if (value == (byte)'D') { AdvanceLine(unit); return; }
            if (value == (byte)'M') { ReverseIndex(unit); return; }
            if (value == (byte)'E') { unit.CursorX = 0; AdvanceLine(unit); return; }
            if (value == (byte)'H') { unit.TabStops.Add(unit.CursorX); return; }
            if (value == (byte)'c') { ClearUnit(unit); unit.NeedsRedraw = true; return; }
            return;
        }
        if (value == 0x9B) { FlushRun(unit, run, state); unit.Csi.Clear(); unit.CsiActive = true; return; }
        if (value == 0x84) { FlushRun(unit, run, state); AdvanceLine(unit); return; } // IND
        if (value == 0x85) { FlushRun(unit, run, state); unit.CursorX = 0; AdvanceLine(unit); return; } // NEL
        if (value == 0x88) { FlushRun(unit, run, state); unit.TabStops.Add(unit.CursorX); return; } // HTS
        if (value == 0x8D) { FlushRun(unit, run, state); ReverseIndex(unit); return; } // RI
        if (value == 0x07) { FlushRun(unit, run, state); unit.PendingBells++; return; }
        if (value == 0x0E) { FlushRun(unit, run, state); unit.HighBitCharacters = false; return; } // Shift In
        if (value == 0x0F) { FlushRun(unit, run, state); unit.HighBitCharacters = true; return; } // Shift Out
        if (value == '\r') { FlushRun(unit, run, state); unit.CursorX = 0; return; }
        if (value == '\n') { FlushRun(unit, run, state); if (unit.ReturnOnLineFeed) unit.CursorX = 0; AdvanceLine(unit); return; }
        if (value is 0x0B or 0x0C)
        {
            FlushRun(unit, run, state);
            if (value == 0x0C) { ClearScreen(unit); unit.NeedsRedraw = true; }
            else ReverseIndex(unit);
            return;
        }
        if (value == '\b') { FlushRun(unit, run, state); unit.CursorX = Math.Max(0, unit.CursorX - 1); return; }
        if (value == '\t') { FlushRun(unit, run, state); unit.CursorX = NextTabStop(unit); return; }
        // ECMA-94 reserves 0x80..0x9F for C1 controls.  console.device
        // deliberately treats DEL (0x7F) as a visible G0 graphic.
        if (value is >= 0x20 and <= 0x7F or >= 0xA0)
        {
            value = unit.HighBitCharacters ? (byte)(value | 0x80) : value;
            if (run.Count == 0) { unit.RunStartX = unit.CursorX; unit.RunStartY = unit.CursorY; }
            run.Add(value); PutCharacter(unit, value);
        }
    }

    private static void PutCharacter(ConsoleUnit unit, byte value)
    {
        EnsureRow(unit, unit.CursorY);
        var line = unit.Lines[unit.CursorY];
        var styles = unit.Styles[unit.CursorY];
        var style = CurrentStyle(unit);
        while (line.Count < unit.CursorX) { line.Add((byte)' '); styles.Add(style); }
        if (unit.InsertMode && unit.CursorX < line.Count) { line.Insert(unit.CursorX, value); styles.Insert(unit.CursorX, style); }
        else if (unit.CursorX < line.Count) { line[unit.CursorX] = value; styles[unit.CursorX] = style; }
        else { line.Add(value); styles.Add(style); }
        unit.History.Add(value);
        unit.CursorX++;
        if (unit.CursorX < unit.Columns) return;
        if (!unit.WrapMode) { unit.CursorX = unit.Columns - 1; return; }
        unit.CursorX = 0;
        AdvanceLine(unit);
    }

    private bool UpdateGeometry(ConsoleUnit unit)
    {
        if (unit.Window == 0 || unit.RastPort == 0 ||
            !_bus.IsMappedMemoryRange(unit.Window + WindowRPortOffset, 4) ||
            !_bus.IsMappedMemoryRange(unit.RastPort + RastPortTextWidthOffset, 2)) return false;
        var textWidth = _bus.ReadWord(unit.RastPort + RastPortTextWidthOffset);
        var textHeight = _bus.ReadWord(unit.RastPort + RastPortTextHeightOffset);
        if (textWidth == 0 || textHeight == 0) return false;
        var width = _bus.ReadWord(unit.Window + WindowWidthOffset);
        var height = _bus.ReadWord(unit.Window + WindowHeightOffset);
        var horizontal = _bus.ReadWord(unit.Window + WindowBorderLeftOffset) + _bus.ReadWord(unit.Window + WindowBorderRightOffset);
        var vertical = _bus.ReadWord(unit.Window + WindowBorderTopOffset) + _bus.ReadWord(unit.Window + WindowBorderBottomOffset);
        if (width <= horizontal || height <= vertical) return false;
        var availableWidth = width - horizontal;
        var availableHeight = height - vertical;
        var columns = unit.LineLengthCharacters != 0
            ? unit.LineLengthCharacters
            : Math.Max(1, availableWidth / textWidth);
        var rows = unit.PageHeightPixels != 0
            ? Math.Max(1, unit.PageHeightPixels / textHeight)
            : Math.Max(1, availableHeight / textHeight);
        var changed = columns != unit.Columns || rows != unit.Rows || textWidth != unit.TextWidth || textHeight != unit.TextHeight;
        unit.Columns = columns;
        unit.Rows = rows;
        unit.TextWidth = textWidth;
        unit.TextHeight = textHeight;
        unit.TextBaseline = _bus.IsMappedMemoryRange(unit.RastPort + RastPortTextBaselineOffset, 2)
            ? _bus.ReadWord(unit.RastPort + RastPortTextBaselineOffset)
            : Math.Max(0, textHeight - 1);
        unit.OriginX = _bus.ReadWord(unit.Window + WindowBorderLeftOffset) + unit.LeftOffsetPixels;
        unit.OriginBaseline = _bus.ReadWord(unit.Window + WindowBorderTopOffset) + unit.TopOffsetPixels + unit.TextBaseline;
        unit.BottomMargin = Math.Min(unit.BottomMargin, unit.Rows - 1);
        unit.CursorX = Math.Clamp(unit.CursorX, 0, unit.Columns - 1);
        unit.CursorY = Math.Clamp(unit.CursorY, 0, unit.Rows - 1);
        return changed;
    }

    private static void AdvanceLine(ConsoleUnit unit)
    {
        var bottom = Math.Min(unit.BottomMargin, unit.Rows - 1);
        if (unit.CursorY < bottom) { unit.CursorY++; return; }
        if (!unit.ScrollEnabled) return;
        var top = Math.Clamp(unit.TopMargin, 0, bottom);
        if (unit.Lines.Count > top) { unit.Lines.RemoveAt(top); unit.Styles.RemoveAt(top); }
        unit.Lines.Insert(Math.Min(bottom, unit.Lines.Count), new List<byte>());
        unit.Styles.Insert(Math.Min(bottom, unit.Styles.Count), new List<RenderStyle>());
        while (unit.Lines.Count > unit.Rows) { unit.Lines.RemoveAt(0); unit.Styles.RemoveAt(0); }
        unit.CursorY = bottom;
        unit.NeedsRedraw = true;
    }

    private static void ReverseIndex(ConsoleUnit unit)
    {
        var top = Math.Clamp(unit.TopMargin, 0, unit.Rows - 1);
        var bottom = Math.Clamp(unit.BottomMargin, top, unit.Rows - 1);
        if (unit.CursorY > top) { unit.CursorY--; return; }
        if (!unit.ScrollEnabled) return;
        while (unit.Lines.Count <= bottom)
        {
            unit.Lines.Add(new List<byte>());
            unit.Styles.Add(new List<RenderStyle>());
        }
        unit.Lines.RemoveAt(bottom);
        unit.Styles.RemoveAt(bottom);
        unit.Lines.Insert(top, new List<byte>());
        unit.Styles.Insert(top, new List<RenderStyle>());
        unit.NeedsRedraw = true;
    }

    private void ApplyCsi(ConsoleUnit unit, byte final)
    {
        var text = System.Text.Encoding.ASCII.GetString(CollectionsMarshal.AsSpan(unit.Csi));
        var parts = text[..^1].Split(';');
        int Parameter(int index, int defaultValue)
            => index >= parts.Length || string.IsNullOrWhiteSpace(parts[index]) || !int.TryParse(parts[index].Trim(), out var value) ? defaultValue : value;
        var first = Math.Max(1, Parameter(0, 1));
        switch ((char)final)
        {
            case 'A': unit.CursorY = Math.Max(unit.OriginMode ? unit.TopMargin : 0, unit.CursorY - first); break;
            case 'B': unit.CursorY += first; break;
            case 'C': unit.CursorX += first; break;
            case 'D': unit.CursorX = Math.Max(0, unit.CursorX - first); break;
            case 'a': unit.CursorX += first; break;
            case 'd': unit.CursorY = Parameter(0, 1) - 1 + (unit.OriginMode ? unit.TopMargin : 0); break;
            case 'e': unit.CursorY += first; break;
            case 'E': unit.CursorY += first; unit.CursorX = 0; break;
            case 'F': unit.CursorY = Math.Max(0, unit.CursorY - first); unit.CursorX = 0; break;
            case 'G': unit.CursorX = first - 1; break;
            case 'I': for (var index = 0; index < first; index++) unit.CursorX = NextTabStop(unit); break;
            case 'H': case 'f': unit.CursorY = Parameter(0, 1) - 1 + (unit.OriginMode ? unit.TopMargin : 0); unit.CursorX = Parameter(1, 1) - 1; break;
            case 'r': unit.TopMargin = Math.Clamp(Parameter(0, 1) - 1, 0, unit.Rows - 1); unit.BottomMargin = Math.Clamp(Parameter(1, unit.Rows) - 1, unit.TopMargin, unit.Rows - 1); unit.CursorX = 0; unit.CursorY = unit.OriginMode ? unit.TopMargin : 0; break;
            case 'J': EraseDisplay(unit, Parameter(0, 0)); break;
            case 'K': EraseLineMode(unit, Parameter(0, 0)); break;
            case 'P': DeleteCharacters(unit, first); break;
            case 'X': EraseLine(unit, unit.CursorY, unit.CursorX, unit.CursorX + first - 1); break;
            case '@': InsertCharacters(unit, first); break;
            case 'L': InsertLines(unit, first); break;
            case 'M': DeleteLines(unit, first); break;
            case 'S': ScrollUp(unit, first); break;
            case 'T': ScrollDown(unit, first); break;
            case 'W':
                switch (Parameter(0, 0))
                {
                    case 0: unit.TabStops.Add(unit.CursorX); break;
                    case 2: unit.TabStops.Remove(unit.CursorX); break;
                    case 5: unit.TabStops.Clear(); break;
                }
                break;
            case 'Z': for (var index = 0; index < first; index++) unit.CursorX = PreviousTabStop(unit); break;
            case 'm': ApplySgr(unit, parts); break;
            case 'g':
                if (Parameter(0, 0) == 0) unit.TabStops.Remove(unit.CursorX);
                else if (Parameter(0, 0) == 3) unit.TabStops.Clear();
                break;
            case '{': SetRawEvents(unit, parts, true); break;
            case '}': SetRawEvents(unit, parts, false); break;
            case 'n':
                if (first == 5) QueueReply(unit, "\u009B0n");
                else if (first == 6) QueueReply(unit, $"\u009B{unit.CursorY + 1};{unit.CursorX + 1}R");
                break;
            case 'c': QueueReply(unit, "\u009B?1;0c"); break;
            case 'q': if (Parameter(0, 0) == 0) QueueReply(unit, $"\u009B1;1;{unit.Rows};{unit.Columns} r"); break;
            case 'p':
            {
                // This private command is CSI [N] SP p.  The intermediate
                // space is not part of its numeric parameter.
                var visible = !string.Equals(text[..^1].Trim(), "0", StringComparison.Ordinal);
                if (unit.CursorVisible != visible) { unit.CursorVisible = visible; unit.NeedsRedraw = true; }
                break;
            }
            case 't': unit.PageHeightPixels = Math.Max(0, Parameter(0, 0)); break;
            case 'u': unit.LineLengthCharacters = Math.Max(0, Parameter(0, 0)); break;
            case 'x': unit.LeftOffsetPixels = Math.Max(0, Parameter(0, 0)); break;
            case 'y': unit.TopOffsetPixels = Math.Max(0, Parameter(0, 0)); break;
            case 'h': SetModes(unit, text[..^1], true); break;
            case 'l': SetModes(unit, text[..^1], false); break;
            case 's':
                if (text[..^1] == " ") SetDefaultSgr(unit);
                else { unit.SavedCursorX = unit.CursorX; unit.SavedCursorY = unit.CursorY; }
                break;
        }
        unit.CursorX = Math.Clamp(unit.CursorX, 0, unit.Columns - 1);
        unit.CursorY = Math.Clamp(unit.CursorY, 0, unit.Rows - 1);
        if (UpdateGeometry(unit)) unit.NeedsRedraw = true;
    }

    private static void SetModes(ConsoleUnit unit, string text, bool enabled)
    {
        var prefix = text.Length != 0 && text[0] is '?' or '>' ? text[0] : '\0';
        var values = (prefix == '\0' ? text : text[1..])
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.TryParse(value, out var parsed) ? parsed : -1);
        foreach (var value in values)
        {
            if (prefix == '?' && value == 7) unit.WrapMode = enabled;
            else if (prefix == '?' && value == 6) unit.OriginMode = enabled;
            else if (prefix == '>' && value == 1) unit.ScrollEnabled = enabled;
            else if (prefix == '\0' && value == 20) unit.ReturnOnLineFeed = enabled;
            else if (prefix == '\0' && value == 4) unit.InsertMode = enabled;
        }
    }

    private static void EraseDisplay(ConsoleUnit unit, int mode)
    {
        switch (mode)
        {
            case 0:
                EraseLine(unit, unit.CursorY, unit.CursorX, int.MaxValue);
                EraseLines(unit, unit.CursorY + 1, int.MaxValue);
                break;
            case 1:
                EraseLines(unit, 0, unit.CursorY - 1);
                EraseLine(unit, unit.CursorY, 0, unit.CursorX);
                break;
            case 2: EraseLines(unit, 0, int.MaxValue); break;
        }
    }

    private static void EraseLineMode(ConsoleUnit unit, int mode)
    {
        switch (mode)
        {
            case 0: EraseLine(unit, unit.CursorY, unit.CursorX, int.MaxValue); break;
            case 1: EraseLine(unit, unit.CursorY, 0, unit.CursorX); break;
            case 2: EraseLine(unit, unit.CursorY, 0, int.MaxValue); break;
        }
    }

    private static void EraseLines(ConsoleUnit unit, int first, int last)
    {
        first = Math.Max(0, first); last = Math.Min(last, unit.Lines.Count - 1);
        for (var index = first; index <= last; index++) { unit.Lines[index].Clear(); unit.Styles[index].Clear(); }
        if (first <= last) unit.NeedsRedraw = true;
    }
    private static void EraseLine(ConsoleUnit unit, int row, int first, int last)
    {
        if ((uint)row >= (uint)unit.Lines.Count) return;
        var line = unit.Lines[row]; first = Math.Max(0, first); last = Math.Min(last, line.Count - 1);
        var styles = unit.Styles[row]; var style = CurrentStyle(unit);
        for (var index = first; index <= last; index++) { line[index] = (byte)' '; styles[index] = style; }
        if (first <= last) unit.NeedsRedraw = true;
    }
    private static void DeleteCharacters(ConsoleUnit unit, int count)
    {
        if ((uint)unit.CursorY >= (uint)unit.Lines.Count || unit.CursorX >= unit.Columns) return;
        var line = unit.Lines[unit.CursorY];
        if (unit.CursorX >= line.Count) return;
        var actual = Math.Min(Math.Min(count, unit.Columns - unit.CursorX), line.Count - unit.CursorX);
        if (actual == 0) return;
        line.RemoveRange(unit.CursorX, actual);
        unit.Styles[unit.CursorY].RemoveRange(unit.CursorX, actual);
        if (line.Count > unit.Columns)
        {
            line.RemoveRange(unit.Columns, line.Count - unit.Columns);
            unit.Styles[unit.CursorY].RemoveRange(unit.Columns, unit.Styles[unit.CursorY].Count - unit.Columns);
        }
        unit.NeedsRedraw = true;
    }

    private static void InsertCharacters(ConsoleUnit unit, int count)
    {
        if (unit.CursorX >= unit.Columns) return;
        EnsureRow(unit, unit.CursorY);
        var line = unit.Lines[unit.CursorY]; var styles = unit.Styles[unit.CursorY]; var style = CurrentStyle(unit);
        while (line.Count < unit.CursorX) { line.Add((byte)' '); styles.Add(style); }
        var actual = Math.Min(count, unit.Columns - unit.CursorX);
        line.InsertRange(unit.CursorX, Enumerable.Repeat((byte)' ', actual));
        styles.InsertRange(unit.CursorX, Enumerable.Repeat(style, actual));
        if (line.Count > unit.Columns)
        {
            line.RemoveRange(unit.Columns, line.Count - unit.Columns);
            styles.RemoveRange(unit.Columns, styles.Count - unit.Columns);
        }
        unit.NeedsRedraw = true;
    }

    private static void InsertLines(ConsoleUnit unit, int count)
    {
        var top = Math.Clamp(unit.TopMargin, 0, unit.Rows - 1);
        var bottom = Math.Clamp(unit.BottomMargin, top, unit.Rows - 1);
        if (unit.CursorY < top || unit.CursorY > bottom) return;
        while (unit.Lines.Count <= bottom) { unit.Lines.Add(new List<byte>()); unit.Styles.Add(new List<RenderStyle>()); }
        var actual = Math.Min(count, bottom - unit.CursorY + 1);
        for (var index = 0; index < actual; index++)
        {
            unit.Lines.RemoveAt(bottom);
            unit.Styles.RemoveAt(bottom);
            unit.Lines.Insert(unit.CursorY, new List<byte>());
            unit.Styles.Insert(unit.CursorY, new List<RenderStyle>());
        }
        if (actual != 0) unit.NeedsRedraw = true;
    }

    private static void DeleteLines(ConsoleUnit unit, int count)
    {
        var top = Math.Clamp(unit.TopMargin, 0, unit.Rows - 1);
        var bottom = Math.Clamp(unit.BottomMargin, top, unit.Rows - 1);
        if (unit.CursorY < top || unit.CursorY > bottom) return;
        while (unit.Lines.Count <= bottom) { unit.Lines.Add(new List<byte>()); unit.Styles.Add(new List<RenderStyle>()); }
        var actual = Math.Min(count, bottom - unit.CursorY + 1);
        for (var index = 0; index < actual; index++)
        {
            unit.Lines.RemoveAt(unit.CursorY);
            unit.Styles.RemoveAt(unit.CursorY);
            unit.Lines.Insert(bottom, new List<byte>());
            unit.Styles.Insert(bottom, new List<RenderStyle>());
        }
        if (actual != 0) unit.NeedsRedraw = true;
    }
    private static void ScrollUp(ConsoleUnit unit, int count)
    {
        var top = Math.Clamp(unit.TopMargin, 0, unit.Rows - 1);
        var bottom = Math.Clamp(unit.BottomMargin, top, unit.Rows - 1);
        while (unit.Lines.Count <= bottom) { unit.Lines.Add(new List<byte>()); unit.Styles.Add(new List<RenderStyle>()); }
        for (var index = 0; index < count; index++)
        {
            unit.Lines.RemoveAt(top);
            unit.Styles.RemoveAt(top);
            unit.Lines.Insert(bottom, new List<byte>());
            unit.Styles.Insert(bottom, new List<RenderStyle>());
        }
        unit.NeedsRedraw = true;
    }
    private static void ScrollDown(ConsoleUnit unit, int count)
    {
        var top = Math.Clamp(unit.TopMargin, 0, unit.Rows - 1);
        var bottom = Math.Clamp(unit.BottomMargin, top, unit.Rows - 1);
        while (unit.Lines.Count <= bottom) { unit.Lines.Add(new List<byte>()); unit.Styles.Add(new List<RenderStyle>()); }
        for (var index = 0; index < count; index++)
        {
            unit.Lines.RemoveAt(bottom);
            unit.Styles.RemoveAt(bottom);
            unit.Lines.Insert(top, new List<byte>());
            unit.Styles.Insert(top, new List<RenderStyle>());
        }
        unit.NeedsRedraw = true;
    }
    private static void QueueReply(ConsoleUnit unit, string text) { foreach (var value in text) unit.Input.Enqueue((byte)value); }
    private static void SetRawEvents(ConsoleUnit unit, string[] parts, bool enabled)
    {
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var eventType) || eventType < 1 || eventType > 21) continue;
            if (enabled) unit.RawEventTypes.Add(eventType); else unit.RawEventTypes.Remove(eventType);
        }
    }
    private static void ApplySgr(ConsoleUnit unit, string[] parts)
    {
        if (parts.Length == 0) { ResetSgr(unit); return; }
        foreach (var part in parts)
        {
            if (part.Length > 1 && part[0] == '>' && int.TryParse(part[1..], out var consoleBackground) && consoleBackground is >= 0 and <= 7)
            {
                unit.ConsoleBackgroundPen = (byte)consoleBackground;
                continue;
            }
            var code = string.IsNullOrEmpty(part) ? 0 : int.TryParse(part, out var value) ? value : -1;
            switch (code)
            {
                case 0: ResetSgr(unit); break;
                case 1: unit.Bold = true; break;
                case 2: unit.Faint = true; break;
                case 3: unit.Italic = true; break;
                case 4: unit.Underline = true; break;
                case 7: unit.Inverse = true; break;
                case 8: unit.Concealed = true; break;
                case 22: unit.Bold = false; unit.Faint = false; break;
                case 23: unit.Italic = false; break;
                case 24: unit.Underline = false; break;
                case 27: unit.Inverse = false; break;
                case 28: unit.Concealed = false; break;
                case >= 30 and <= 37: unit.ForegroundPen = (byte)(code - 30); break;
                case 39: unit.ForegroundPen = unit.DefaultForegroundPen; break;
                case >= 40 and <= 47: unit.BackgroundPen = (byte)(code - 40); unit.BackgroundActive = true; break;
                case 49: unit.BackgroundPen = unit.DefaultBackgroundPen; unit.BackgroundActive = unit.DefaultBackgroundActive; break;
            }
        }
    }
    private static void SetDefaultSgr(ConsoleUnit unit)
    {
        unit.DefaultForegroundPen = unit.ForegroundPen; unit.DefaultBackgroundPen = unit.BackgroundPen;
        unit.DefaultBackgroundActive = unit.BackgroundActive; unit.DefaultBold = unit.Bold; unit.DefaultFaint = unit.Faint;
        unit.DefaultUnderline = unit.Underline; unit.DefaultItalic = unit.Italic; unit.DefaultInverse = unit.Inverse; unit.DefaultConcealed = unit.Concealed;
    }
    private static void ResetSgr(ConsoleUnit unit)
    {
        unit.ForegroundPen = unit.DefaultForegroundPen; unit.BackgroundPen = unit.DefaultBackgroundPen;
        unit.BackgroundActive = unit.DefaultBackgroundActive; unit.Bold = unit.DefaultBold; unit.Faint = unit.DefaultFaint;
        unit.Underline = unit.DefaultUnderline; unit.Italic = unit.DefaultItalic; unit.Inverse = unit.DefaultInverse; unit.Concealed = unit.DefaultConcealed;
    }
    private static void SaveCursorAndRendition(ConsoleUnit unit)
    {
        unit.SavedCursorX = unit.CursorX; unit.SavedCursorY = unit.CursorY;
        unit.SavedForegroundPen = unit.ForegroundPen; unit.SavedBackgroundPen = unit.BackgroundPen;
        unit.SavedBackgroundActive = unit.BackgroundActive; unit.SavedBold = unit.Bold; unit.SavedFaint = unit.Faint;
        unit.SavedUnderline = unit.Underline; unit.SavedItalic = unit.Italic; unit.SavedInverse = unit.Inverse; unit.SavedConcealed = unit.Concealed;
    }
    private static void RestoreCursorAndRendition(ConsoleUnit unit)
    {
        unit.CursorX = unit.SavedCursorX; unit.CursorY = unit.SavedCursorY;
        unit.ForegroundPen = unit.SavedForegroundPen; unit.BackgroundPen = unit.SavedBackgroundPen;
        unit.BackgroundActive = unit.SavedBackgroundActive; unit.Bold = unit.SavedBold; unit.Faint = unit.SavedFaint;
        unit.Underline = unit.SavedUnderline; unit.Italic = unit.SavedItalic; unit.Inverse = unit.SavedInverse; unit.Concealed = unit.SavedConcealed;
        unit.NeedsRedraw = true;
    }
    private static void ResetTabs(ConsoleUnit unit)
    {
        unit.TabStops.Clear();
        for (var column = 8; column < Math.Max(unit.Columns, DefaultColumns); column += 8) unit.TabStops.Add(column);
    }
    private static int NextTabStop(ConsoleUnit unit)
    {
        var next = unit.TabStops.Where(column => column > unit.CursorX).DefaultIfEmpty(unit.Columns - 1).Min();
        return Math.Clamp(next, 0, unit.Columns - 1);
    }
    private static int PreviousTabStop(ConsoleUnit unit)
    {
        var previous = unit.TabStops.Where(column => column < unit.CursorX).DefaultIfEmpty(0).Max();
        return Math.Clamp(previous, 0, unit.Columns - 1);
    }

    private void FlushRun(ConsoleUnit unit, List<byte> run, M68kCpuState? state)
    {
        if (run.Count == 0 || state is null || unit.RastPort == 0) { run.Clear(); return; }
        while (run.Count != 0)
        {
            var count = Math.Min(run.Count, 224);
            var style = CurrentStyle(unit);
            QueueRenderRun(unit, new RenderRun(
                unit.OriginX + (unit.RunStartX * unit.TextWidth),
                unit.OriginBaseline + (unit.RunStartY * unit.TextHeight),
                style.ForegroundPen,
                style.BackgroundPen,
                style.DrawMode,
                run.GetRange(0, count).ToArray(),
                UnderlineFill: false, CursorFill: false, Italic: style.Italic), style.Bold, style.Underline);
            run.RemoveRange(0, count);
            unit.RunStartX += count;
        }
    }

    private static void QueueStyledLine(ConsoleUnit unit, int row, List<byte> line)
    {
        var styles = row < unit.Styles.Count ? unit.Styles[row] : null;
        for (var start = 0; start < line.Count;)
        {
            var style = StyleAt(unit, row, start, styles);
            var end = start + 1;
            while (end < line.Count && StyleAt(unit, row, end, styles).Equals(style)) end++;
            QueueRenderRun(unit, new RenderRun(
                unit.OriginX + (start * unit.TextWidth),
                unit.OriginBaseline + (row * unit.TextHeight),
                style.ForegroundPen,
                style.BackgroundPen,
                style.DrawMode,
                line.GetRange(start, end - start).ToArray(),
                UnderlineFill: false, CursorFill: false, Italic: style.Italic), style.Bold, style.Underline);
            start = end;
        }
    }

    private static RenderStyle StyleAt(ConsoleUnit unit, int row, int column, List<RenderStyle>? styles)
    {
        var style = styles is not null && column < styles.Count ? styles[column] : CurrentStyle(unit);
        if (!IsSelected(unit, row, column)) return style;
        return style with { ForegroundPen = style.BackgroundPen, BackgroundPen = style.ForegroundPen, DrawMode = 2 };
    }

    private static bool IsSelected(ConsoleUnit unit, int row, int column)
    {
        var start = (unit.SelectionAnchorY, unit.SelectionAnchorX);
        var end = (unit.SelectionCaretY, unit.SelectionCaretX);
        if (start == end) return false;
        if (start.CompareTo(end) > 0) (start, end) = (end, start);
        if (row < start.Item1 || row > end.Item1) return false;
        return row == start.Item1 && row == end.Item1
            ? column >= start.Item2 && column < end.Item2
            : row == start.Item1 ? column >= start.Item2
            : row == end.Item1 ? column < end.Item2
            : true;
    }

    private static void QueueRenderRun(ConsoleUnit unit, RenderRun text, bool bold, bool underline)
    {
        unit.PendingRenders.Enqueue(text);
        // A one-pixel offset is the conventional bitmap-font bold treatment.
        if (bold) unit.PendingRenders.Enqueue(text with { X = text.X + 1 });
        if (underline) unit.PendingRenders.Enqueue(text with { UnderlineFill = true });
    }

    private void StartNextRender(M68kCpuState state)
    {
        if (_pendingRender is not { } pending || !_units.TryGetValue(pending.Unit, out var unit)) { _pendingRender = null; return; }
        if (unit.PendingRenders.Count == 0)
        {
            if (StartNextBell(pending, unit, state)) return;
            _pendingRender = null; Complete(pending.Request, 0, pending.Actual, state.Cycles, true); StartNextQueuedWrite(state); return;
        }
        var run = unit.PendingRenders.Dequeue();
        if (run.UnderlineFill || run.CursorFill)
        {
            var underlineGraphics = FindLibrary(_execBase, "graphics.library");
            if (underlineGraphics == 0 || !_bus.IsCpuPhysicalAddressMapped(underlineGraphics - 0x132, 2, AmigaBusAccessKind.CpuInstructionFetch))
            {
                StartNextRender(state);
                return;
            }
            _bus.WriteByte(unit.RastPort + RastPortFgPenOffset, run.ForegroundPen, state.Cycles);
            state.A[1] = unit.RastPort;
            state.D[0] = unchecked((uint)run.X);
            state.D[1] = unchecked((uint)(run.CursorFill ? run.Y - unit.TextBaseline : run.Y + 1));
            state.D[2] = unchecked((uint)(run.X + (run.Bytes.Length * unit.TextWidth) - 1));
            state.D[3] = unchecked((uint)(run.CursorFill ? run.Y - unit.TextBaseline + unit.TextHeight - 1 : run.Y + 1));
            _startGuestSubroutine(state, underlineGraphics - 0x132, GraphicsUnderlineContinuationAddress);
            return;
        }
        if (run.Italic && StartSoftStyle(run, state)) return;
        StartTextRun(run, state);
    }

    private bool StartSoftStyle(RenderRun run, M68kCpuState state)
    {
        var graphics = FindLibrary(_execBase, "graphics.library");
        if (graphics == 0 || !_bus.IsCpuPhysicalAddressMapped(graphics - 0x5A, 2, AmigaBusAccessKind.CpuInstructionFetch)) return false;
        _pendingSoftStyle = new PendingSoftStyle(run);
        _softStyleRastPort = _units[_pendingRender!.Value.Unit].RastPort;
        state.A[1] = _softStyleRastPort;
        state.D[0] = FsfItalic;
        state.D[1] = FsfItalic;
        _startGuestSubroutine(state, graphics - 0x5A, GraphicsSoftStyleSetContinuationAddress);
        return true;
    }

    private void StartTextRun(RenderRun run, M68kCpuState state)
    {
        if (_pendingRender is not { } pending || !_units.TryGetValue(pending.Unit, out var unit)) { _pendingRender = null; return; }
        for (var index = 0; index < run.Bytes.Length; index++) _bus.WriteByte(unit.Scratch + (uint)index, run.Bytes[index], state.Cycles);
        if (_bus.IsMappedMemoryRange(unit.RastPort + RastPortCpYOffset, 2))
        {
            _bus.WriteWord(unit.RastPort + RastPortCpXOffset, unchecked((ushort)run.X), state.Cycles);
            _bus.WriteWord(unit.RastPort + RastPortCpYOffset, unchecked((ushort)run.Y), state.Cycles);
        }
        _bus.WriteByte(unit.RastPort + RastPortFgPenOffset, run.ForegroundPen, state.Cycles);
        _bus.WriteByte(unit.RastPort + RastPortBgPenOffset, run.BackgroundPen, state.Cycles);
        _bus.WriteByte(unit.RastPort + RastPortDrawModeOffset, run.DrawMode, state.Cycles);

        var graphics = FindLibrary(_execBase, "graphics.library");
        if (graphics == 0 || !_bus.IsCpuPhysicalAddressMapped(graphics - 0x3C, 2, AmigaBusAccessKind.CpuInstructionFetch))
        {
            // CopperStart's synthetic compatibility profile has no native
            // graphics.library. Its injected renderer is the local equivalent.
            _drawText(state, unit.RastPort, unit.Scratch, (uint)run.Bytes.Length);
            StartNextRender(state);
            return;
        }

        state.A[0] = unit.Scratch;
        state.A[1] = unit.RastPort;
        state.D[0] = (uint)run.Bytes.Length;
        _startGuestSubroutine(state, graphics - 0x3C, GraphicsTextContinuationAddress);
    }

    private void ContinueGraphicsText(M68kCpuState state)
    {
        if (!_resetSoftStyleAfterText) { StartNextRender(state); return; }
        _resetSoftStyleAfterText = false;
        StartSoftStyleReset(state);
    }

    private void StartSoftStyleReset(M68kCpuState state)
    {
        var graphics = FindLibrary(_execBase, "graphics.library");
        if (_softStyleRastPort == 0 || graphics == 0 || !_bus.IsCpuPhysicalAddressMapped(graphics - 0x5A, 2, AmigaBusAccessKind.CpuInstructionFetch)) { _softStyleRastPort = 0; StartNextRender(state); return; }
        state.A[1] = _softStyleRastPort;
        state.D[0] = 0;
        state.D[1] = FsfItalic;
        _startGuestSubroutine(state, graphics - 0x5A, GraphicsSoftStyleResetContinuationAddress);
    }
    private void ContinueGraphicsUnderline(M68kCpuState state) => StartNextRender(state);
    private void ContinueGraphicsSoftStyleSet(M68kCpuState state)
    {
        var pending = _pendingSoftStyle;
        _pendingSoftStyle = null;
        if (pending is not { } style) { StartNextRender(state); return; }
        _resetSoftStyleAfterText = true;
        if (_pendingRender is null) { _resetSoftStyleAfterText = false; StartSoftStyleReset(state); return; }
        StartTextRun(style.Run, state);
    }
    private void ContinueGraphicsSoftStyleReset(M68kCpuState state) { _softStyleRastPort = 0; StartNextRender(state); }

    private bool StartNextBell(PendingRender pending, ConsoleUnit unit, M68kCpuState state)
    {
        if (unit.PendingBells == 0) return false;
        unit.PendingBells--;
        var intuition = FindLibrary(_execBase, "intuition.library");
        if (intuition == 0 || !_bus.IsCpuPhysicalAddressMapped(intuition - 0x60, 2, AmigaBusAccessKind.CpuInstructionFetch)) return false;
        _pendingBell = pending;
        state.A[0] = 0; // DisplayBeep(NULL) means the active screen.
        _startGuestSubroutine(state, intuition - 0x60, IntuitionBeepContinuationAddress);
        return true;
    }

    private void ContinueIntuitionBeep(M68kCpuState state)
    {
        var pending = _pendingBell;
        _pendingBell = null;
        if (pending is not { } bell || _pendingRender is not { } render || bell != render) return;
        StartNextRender(state);
    }

    private void CancelRender(uint unitAddress, long cycle, bool completeRequest = true)
    {
        if (_units.TryGetValue(unitAddress, out var unit)) { unit.PendingRenders.Clear(); unit.PendingBells = 0; }
        if (_pendingRender is not { } pending || pending.Unit != unitAddress) return;
        _pendingRender = null;
        if (_pendingBell is { } bell && bell == pending) _pendingBell = null;
        // A redraw owns both the preceding clear and the render queue.  They
        // are one I/O request, so flushing it must reply exactly once.
        if (_pendingClear is { } clear && clear.Unit == unitAddress && clear.Request == pending.Request)
            _pendingClear = null;
        if (completeRequest) Complete(pending.Request, IoErrAborted, 0, cycle, true);
    }

    private void ObserveInputEvent(InputDeviceServices.ObservedInputEvent input) => _ = ObserveInput(input);

    private bool ObserveInput(InputDeviceServices.ObservedInputEvent input)
    {
        if (!TryGetInputTargetUnit(input, out var unit)) return false;
        if (input.Class != IeClassRawKey &&
            input.Class != IeClassRawMouse &&
            !unit.RawEventTypes.Contains(input.Class)) return false;
        if (input.Class == IeClassRawMouse &&
            unit.Number != ConuSnipMap &&
            !unit.RawEventTypes.Contains(input.Class)) return false;
        _rawInput.Enqueue(new QueuedInput(input, unit.Request));
        return true;
    }

    private void StartNextKeyMapCall(M68kCpuState state)
    {
        if (_pendingKeyMapCall is not null || _rawInput.Count == 0) return;
        while (_rawInput.Count != 0)
        {
            var queued = _rawInput.Dequeue();
            var input = queued.Event;
            if (!_units.TryGetValue(queued.Unit, out var unit)) continue;
            if (input.Class != IeClassRawKey)
            {
                if (input.Class == IeClassRawMouse && unit.Number == ConuSnipMap) HandleSelectionMouse(unit, input);
                if (unit.RawEventTypes.Contains(input.Class)) QueueRawInput(unit, input);
                continue;
            }
            if (unit.Number == ConuSnipMap && input.Code == 0x34 && (input.Qualifier & 0x0080) != 0)
            {
                // CopperStart deliberately crosses this boundary through
                // clipboard.device.  That device owns the host clipboard
                // bridge; console.device only sees normal guest I/O.
                if (!PasteClipboard(unit, state)) QueueReply(unit, "\u009B0 v");
                continue;
            }
            if (unit.RawEventTypes.Contains(1)) { QueueRawInput(unit, input); continue; }
            if ((input.Code & 0x80) != 0) continue;
            if ((input.Qualifier & 0x0080) != 0 && input.Code == 0x33)
            {
                if (unit.Number == ConuSnipMap)
                {
                    // Snip-map owns Right-Amiga+C. A missing selection is a
                    // no-op, not a request to type a literal 'c'.
                    _ = CopySelectionToClipboard(unit, state);
                    continue;
                }
                if (CopySelectionToClipboard(unit, state)) continue;
            }
            if (TryQueueSpecialKey(unit, input)) continue;
            var keyMapLibrary = FindLibrary(_execBase, "keymap.library");
            if (keyMapLibrary == 0 || !_bus.IsCpuPhysicalAddressMapped(keyMapLibrary - 36, 2, AmigaBusAccessKind.CpuInstructionFetch))
            {
                QueueFallbackCharacter(unit, input);
                continue;
            }

            // MapRawKey(event, buffer, bufferBytes, keyMap) is guest code.
            // Its completion is re-entered only through the outer CPU loop.
            _bus.WriteLong(unit.Scratch, 0, state.Cycles);
            _bus.WriteByte(unit.Scratch + 4, input.Class, state.Cycles);
            _bus.WriteByte(unit.Scratch + 5, input.SubClass, state.Cycles);
            _bus.WriteWord(unit.Scratch + 6, input.Code, state.Cycles);
            _bus.WriteWord(unit.Scratch + 8, input.Qualifier, state.Cycles);
            _bus.WriteLong(unit.Scratch + 10, input.Address, state.Cycles);
            state.A[0] = unit.Scratch;
            state.A[1] = unit.Scratch + 32;
            state.A[2] = unit.KeyMap;
            state.D[1] = 224;
            _pendingKeyMapCall = new PendingKeyMapCall(unit.Request, unit.InputGeneration, input);
            _startGuestSubroutine(state, keyMapLibrary - 36, MapRawKeyContinuationAddress);
            return;
        }
    }

    private bool TryGetActiveWindowUnit(out ConsoleUnit unit)
    {
        var intuition = FindLibrary(_execBase, "intuition.library");
        if (intuition != 0 && _bus.IsMappedMemoryRange(intuition + IntuitionActiveWindowOffset, 4))
        {
            var activeWindow = _bus.ReadLong(intuition + IntuitionActiveWindowOffset);
            var active = _units.Values.LastOrDefault(candidate => candidate.Window == activeWindow && activeWindow != 0);
            if (active is not null) _activeWindowUnit = active.Request;
        }
        return _units.TryGetValue(_activeWindowUnit, out unit!);
    }

    private bool TryGetInputTargetUnit(InputDeviceServices.ObservedInputEvent input, out ConsoleUnit unit)
    {
        // Intuition encodes the source Window address in X/Y for these
        // classes. They therefore belong to the matching console unit, even
        // while another window owns the keyboard focus.
        if (input.Class is 9 or 12 or 13 or 17 or 18 or 21)
        {
            var window = ((uint)(ushort)input.X << 16) | (ushort)input.Y;
            var target = _units.Values.LastOrDefault(candidate => candidate.Window == window && candidate.Window != 0);
            if (target is not null) { unit = target; return true; }
        }
        return TryGetActiveWindowUnit(out unit);
    }

    private void ContinueMapRawKey(M68kCpuState state)
    {
        var call = _pendingKeyMapCall;
        _pendingKeyMapCall = null;
        if (call is not { } pending || !_units.TryGetValue(pending.Unit, out var unit) || unit.InputGeneration != pending.InputGeneration) return;
        var count = Math.Min(state.D[0], 224u);
        for (uint index = 0; index < count && _bus.IsMappedMemoryRange(unit.Scratch + 32 + index, 1); index++) unit.Input.Enqueue(_bus.ReadByte(unit.Scratch + 32 + index));
        if (count == 0) QueueFallbackCharacter(unit, pending.Input);
    }

    // Console accesses clipboard.device only through a guest IOClipReq. The
    // service never calls the platform clipboard; clipboard.device owns that bridge.
    private void HandleSelectionMouse(ConsoleUnit unit, InputDeviceServices.ObservedInputEvent input)
    {
        if (input.Code == IeCodeLeftButton)
        {
            unit.PointerX = input.X; unit.PointerY = input.Y;
            var (column, row) = PointerToCell(unit);
            unit.SelectionAnchorX = unit.SelectionCaretX = column;
            unit.SelectionAnchorY = unit.SelectionCaretY = row;
            unit.Selecting = true; unit.NeedsRedraw = true;
            return;
        }

        if (input.Code == IeCodeNoButton && unit.Selecting)
        {
            unit.PointerX += input.X; unit.PointerY += input.Y;
            var (column, row) = PointerToCell(unit);
            if (column != unit.SelectionCaretX || row != unit.SelectionCaretY)
            {
                unit.SelectionCaretX = column; unit.SelectionCaretY = row; unit.NeedsRedraw = true;
            }
            return;
        }

        if (input.Code == (IeCodeLeftButton | IeCodeUpPrefix)) unit.Selecting = false;
    }

    private static (int Column, int Row) PointerToCell(ConsoleUnit unit)
    {
        var column = Math.Clamp((unit.PointerX - unit.OriginX) / Math.Max(1, unit.TextWidth), 0, unit.Columns);
        var top = unit.OriginBaseline - unit.TextBaseline;
        var row = Math.Clamp((unit.PointerY - top) / Math.Max(1, unit.TextHeight), 0, Math.Max(0, unit.Rows - 1));
        return (column, row);
    }

    private bool CopySelectionToClipboard(ConsoleUnit unit, M68kCpuState state)
    {
        // A highlighted snip takes precedence. The retained console history
        // is the established fallback for an unselected copy request.
        var text = GetSelectedText(unit) ?? System.Text.Encoding.Latin1.GetString(CollectionsMarshal.AsSpan(unit.History));
        if (string.IsNullOrEmpty(text) || !TryOpenClipboard(unit, state)) return false;
        var bytes = ClipboardIffText.Encode(text);
        if (bytes.Length > ClipboardBufferBytes) return false;
        for (var index = 0; index < bytes.Length; index++) _bus.WriteByte(unit.ClipboardBuffer + (uint)index, bytes[index], state.Cycles);
        return InvokeClipboardIo(unit, CmdWrite, unit.ClipboardBuffer, (uint)bytes.Length, 0, 0, state, out var id) &&
            InvokeClipboardIo(unit, CmdUpdate, 0, 0, 0, id, state, out _);
    }

    private bool PasteClipboard(ConsoleUnit unit, M68kCpuState state)
    {
        if (!TryOpenClipboard(unit, state) || !InvokeClipboardIo(unit, CmdRead, unit.ClipboardBuffer, ClipboardBufferBytes, 0, 0, state, out _)) return false;
		var length = _bus.ReadLong(unit.ClipboardRequest + ExecLayout.IOStdReq.Actual);
        if (length > ClipboardBufferBytes) return false;
        var bytes = new byte[(int)length];
        for (var index = 0u; index < length; index++) bytes[index] = _bus.ReadByte(unit.ClipboardBuffer + index);
        if (!ClipboardIffText.TryDecode(bytes, out var text)) return false;
        foreach (var character in text) unit.Input.Enqueue(character <= byte.MaxValue ? (byte)character : (byte)'?');
        return true;
    }

    private static string? GetSelectedText(ConsoleUnit unit)
    {
        var start = (unit.SelectionAnchorY, unit.SelectionAnchorX);
        var end = (unit.SelectionCaretY, unit.SelectionCaretX);
        if (start == end) return null;
        if (start.CompareTo(end) > 0) (start, end) = (end, start);
        var result = new System.Text.StringBuilder();
        for (var row = start.Item1; row <= end.Item1 && row < unit.Lines.Count; row++)
        {
            var line = unit.Lines[row];
            var first = row == start.Item1 ? Math.Clamp(start.Item2, 0, line.Count) : 0;
            var last = row == end.Item1 ? Math.Clamp(end.Item2, 0, line.Count) : line.Count;
            for (var column = first; column < last; column++) result.Append((char)line[column]);
            if (row != end.Item1) result.Append('\n');
        }
        return result.ToString();
    }

    private bool TryOpenClipboard(ConsoleUnit unit, M68kCpuState state)
    {
        if (unit.ClipboardRequest != 0) return true;
		var device = FindDevice(_execBase + ExecLayout.ExecBase.DeviceList, "clipboard.device");
        if (device < 36 || !_bus.IsCpuPhysicalAddressMapped(device - 36, 6, AmigaBusAccessKind.CpuInstructionFetch)) return false;
		var request = _memory.Allocate(ClipboardRequestBytes, PublicClear);
		var buffer = _memory.Allocate(ClipboardBufferBytes, PublicClear);
        if (request == 0 || buffer == 0) { if (request != 0) _memory.Free(request, ClipboardRequestBytes); if (buffer != 0) _memory.Free(buffer, ClipboardBufferBytes); return false; }
        _bus.ClearMemory(request, ClipboardRequestBytes); _bus.ClearMemory(buffer, ClipboardBufferBytes);
        var call = new M68kCpuState { Cycles = state.Cycles }; call.A[1] = request; call.D[0] = 0;
		if (!_bus.TryInvokeHostGatewayAt(device - 6, call) || call.D[0] != 0 || _bus.ReadLong(request + ExecLayout.IORequest.Device) != device)
        { _memory.Free(request, ClipboardRequestBytes); _memory.Free(buffer, ClipboardBufferBytes); return false; }
        unit.ClipboardRequest = request; unit.ClipboardBuffer = buffer; return true;
    }

    private bool InvokeClipboardIo(ConsoleUnit unit, ushort command, uint data, uint length, uint offset, uint clipId, M68kCpuState state, out uint resultingId)
    {
        resultingId = 0;
		var device = _bus.ReadLong(unit.ClipboardRequest + ExecLayout.IORequest.Device);
        if (device < 30 || !_bus.IsCpuPhysicalAddressMapped(device - 30, 6, AmigaBusAccessKind.CpuInstructionFetch)) return false;
		_bus.WriteWord(unit.ClipboardRequest + ExecLayout.IORequest.Command, command, state.Cycles);
		_bus.WriteByte(unit.ClipboardRequest + ExecLayout.IORequest.Flags, IoQuick, state.Cycles);
		_bus.WriteLong(unit.ClipboardRequest + ExecLayout.IOStdReq.Data, data, state.Cycles);
		_bus.WriteLong(unit.ClipboardRequest + ExecLayout.IOStdReq.Length, length, state.Cycles);
        _bus.WriteLong(unit.ClipboardRequest + 0x2C, offset, state.Cycles);
        _bus.WriteLong(unit.ClipboardRequest + 0x30, clipId, state.Cycles);
        var call = new M68kCpuState { Cycles = state.Cycles }; call.A[1] = unit.ClipboardRequest;
		if (!_bus.TryInvokeHostGatewayAt(device - 30, call) || _bus.ReadByte(unit.ClipboardRequest + ExecLayout.IORequest.Error) != 0) return false;
        resultingId = _bus.ReadLong(unit.ClipboardRequest + 0x30); return true;
    }

    private void FreeUnitMemory(ConsoleUnit unit)
    {
        if (unit.Scratch != 0) _memory.Free(unit.Scratch, 256);
		if (unit.PortableState != 0) _memory.Free(unit.PortableState, (int)PortableDevices.ConsoleDeviceCore.UnitStateSize);
        if (unit.ClipboardRequest != 0) _memory.Free(unit.ClipboardRequest, ClipboardRequestBytes);
        if (unit.ClipboardBuffer != 0) _memory.Free(unit.ClipboardBuffer, ClipboardBufferBytes);
    }

    private static void QueueFallbackCharacter(ConsoleUnit unit, InputDeviceServices.ObservedInputEvent input)
    {
        var character = Translate((byte)input.Code, input.Qualifier);
        if (character != 0) unit.Input.Enqueue(character);
    }

    /// <summary>
    /// The console input contract reserves function, Help, and cursor keys
    /// for Amiga CSI reports.  They are not ordinary printable keymap output.
    /// </summary>
    private static bool TryQueueSpecialKey(ConsoleUnit unit, InputDeviceServices.ObservedInputEvent input)
    {
        var shifted = (input.Qualifier & 0x0003) != 0;
        if (input.Code is >= 0x50 and <= 0x59)
        {
            var number = input.Code - 0x50 + (shifted ? 10 : 0);
            QueueReply(unit, $"\u009B{number}~");
            return true;
        }

        var report = input.Code switch
        {
            0x47 => shifted ? "\u009B50~" : "\u009B40~", // Insert
            0x48 => shifted ? "\u009B51~" : "\u009B41~", // Page Up
            0x49 => shifted ? "\u009B52~" : "\u009B42~", // Page Down
            0x4C => shifted ? "\u009BT" : "\u009BA", // cursor up
            0x4D => shifted ? "\u009BS" : "\u009BB", // cursor down
            0x4E => shifted ? "\u009B @" : "\u009BC", // cursor right
            0x4F => shifted ? "\u009B A" : "\u009BD", // cursor left
            0x5F => "\u009B?~", // Help
            _ => null
        };
        if (report is null) return false;
        QueueReply(unit, report);
        return true;
    }
    private static void QueueRawInput(ConsoleUnit unit, InputDeviceServices.ObservedInputEvent input)
    {
        QueueReply(unit, $"\u009B{input.Class};{input.SubClass};{input.Code};{input.Qualifier};{input.X};{input.Y};{input.Seconds};{input.Microseconds}|");
    }
    private void CdInputHandler(M68kCpuState state)
    {
        // CDInputHandler is an input-handler vector, not a device command:
        // it returns the still-unconsumed event chain.  Host delivery uses the
        // same ObserveInput path, but direct guest callers need the native
        // filtering contract as well.
        var address = state.A[0];
        var firstUnconsumed = 0u;
        var previousUnconsumed = 0u;
        for (var count = 0; address != 0 && count < 256 && _bus.IsMappedMemoryRange(address, 22); count++)
        {
            var next = _bus.ReadLong(address);
            var input = new InputDeviceServices.ObservedInputEvent(address, _bus.ReadByte(address + 4), _bus.ReadByte(address + 5), _bus.ReadWord(address + 6), _bus.ReadWord(address + 8), unchecked((short)_bus.ReadWord(address + 10)), unchecked((short)_bus.ReadWord(address + 12)), _bus.ReadLong(address + 14), _bus.ReadLong(address + 18), next);
            if (ObserveInput(input))
            {
                // The producer owns event storage; unlinking is sufficient.
                // Do not free or overwrite the consumed event itself.
            }
            else
            {
                if (previousUnconsumed == 0) firstUnconsumed = address;
                else _bus.WriteLong(previousUnconsumed, address, state.Cycles);
                previousUnconsumed = address;
            }
            address = next;
        }
        if (previousUnconsumed != 0) _bus.WriteLong(previousUnconsumed, 0, state.Cycles);
        state.D[0] = firstUnconsumed;
    }
    /// <summary>
    /// Converts through the currently selected guest keymap.library.  This is
    /// deliberately not the host physical-key mapping: applications can
    /// install another Amiga keymap at runtime and must see that keymap here.
    /// </summary>
    private void RawKeyConvert(M68kCpuState state)
    {
        var eventAddress = state.A[0];
        var destination = state.A[1];
        var capacity = state.D[1];
        var keyMapLibrary = FindLibrary(_execBase, "keymap.library");
        if (_pendingRawKeyConvert is not null || eventAddress == 0 || destination == 0 || capacity == 0 ||
            !_bus.IsMappedMemoryRange(eventAddress, 10) || !_bus.IsMappedMemoryRange(destination, 1) ||
            keyMapLibrary == 0 || !_bus.IsCpuPhysicalAddressMapped(keyMapLibrary - 36, 2, AmigaBusAccessKind.CpuInstructionFetch))
        {
            state.D[0] = 0;
            return;
        }

        // MapRawKey(InputEvent *, STRPTR, LONG, KeyMap *) must retain a null
        // A2.  Native keymap.library interprets that as the *current guest*
        // keymap; replacing it with the console's private default would make
        // RawKeyConvert ignore an application-selected system keymap.
        _pendingRawKeyConvert = new PendingRawKeyConvert(destination, capacity);
        _startGuestSubroutine(state, keyMapLibrary - 36, RawKeyConvertContinuationAddress);
    }

    private void ContinueRawKeyConvert(M68kCpuState state)
    {
        var pending = _pendingRawKeyConvert;
        _pendingRawKeyConvert = null;
        if (pending is not { } call)
        {
            state.D[0] = 0;
            return;
        }

        // RawKeyConvert's documented overflow result is -1.  Do not let a
        // broken or over-eager guest map claim that a truncated buffer is a
        // valid conversion.
        if (state.D[0] != uint.MaxValue && state.D[0] > call.Capacity) state.D[0] = uint.MaxValue;
    }
    private static byte Translate(byte raw, ushort qualifier) { var value = raw switch { 0x00 => (byte)'`', >= 0x01 and <= 0x0A => (byte)("1234567890"[raw - 1]), 0x10 => (byte)'q', 0x11 => (byte)'w', 0x12 => (byte)'e', 0x13 => (byte)'r', 0x14 => (byte)'t', 0x15 => (byte)'y', 0x16 => (byte)'u', 0x17 => (byte)'i', 0x18 => (byte)'o', 0x19 => (byte)'p', 0x20 => (byte)'a', 0x21 => (byte)'s', 0x22 => (byte)'d', 0x23 => (byte)'f', 0x24 => (byte)'g', 0x25 => (byte)'h', 0x26 => (byte)'j', 0x27 => (byte)'k', 0x28 => (byte)'l', 0x31 => (byte)'z', 0x32 => (byte)'x', 0x33 => (byte)'c', 0x34 => (byte)'v', 0x35 => (byte)'b', 0x36 => (byte)'n', 0x37 => (byte)'m', 0x40 => (byte)' ', 0x44 => (byte)'\r', _ => (byte)0 }; return (qualifier & 3) != 0 && value is >= (byte)'a' and <= (byte)'z' ? (byte)(value - 32) : value; }

	private void CompleteUnitReads(ConsoleUnit unit, long cycle)
	{
		if (unit.PortableState == 0 || unit.Input.Count == 0) return;
		var platform = new AmigaBusConsolePlatform(_bus, cycle, _reply);
		while (unit.Input.Count != 0)
		{
			var staged = Math.Min(unit.Input.Count, 256); var offset = 0;
			foreach (var value in unit.Input) { if (offset == staged) break; _bus.WriteByte(unit.Scratch + (uint)offset++, value, cycle); }
			var accepted = PortableDevices.ConsoleDeviceCore.EnqueueInput(ref platform, APTR.FromPointer(unit.PortableState), APTR.FromPointer(unit.Scratch), (uint)staged);
			for (var index = 0u; index < accepted; index++) unit.Input.Dequeue();
			if (accepted < staged) break;
		}
	}
	private void MarkAsynchronous(uint request, long cycle) { if (_bus.IsMappedMemoryRange(request + ExecLayout.IORequest.Flags, 1)) _bus.WriteByte(request + ExecLayout.IORequest.Flags, (byte)(_bus.ReadByte(request + ExecLayout.IORequest.Flags) & ~IoQuick), cycle); }
    private void Complete(uint request, byte error, uint actual, long cycle, bool reply)
    {
		if (request == 0 || !_bus.IsMappedMemoryRange(request + ExecLayout.IORequest.Error, 9)) return;
		if (TryGetUnit(request, out var portableUnit) && portableUnit.PortableState != 0)
		{
			var platform = _cpuState is null
				? new AmigaBusConsolePlatform(_bus, cycle, _reply)
				: new AmigaBusConsolePlatform(_bus, cycle, _reply, this, _cpuState);
			var unitState = APTR.FromPointer(portableUnit.PortableState); var requestAddress = APTR.FromPointer(request);
			var portableError = (IoError)unchecked((sbyte)error);
			if (PortableDevices.ConsoleDeviceCore.IsActiveWrite(ref platform, unitState, requestAddress))
			{
				PortableDevices.ConsoleDeviceCore.ContinueWrite(ref platform, unitState, portableError, actual);
				return;
			}
			if (PortableDevices.ConsoleDeviceCore.IsActiveControl(ref platform, unitState, requestAddress))
			{
				PortableDevices.ConsoleDeviceCore.ContinueControl(ref platform, unitState, requestAddress, portableError);
				return;
			}
		}
		if (error == 0 && _bus.ReadWord(request + ExecLayout.IORequest.Command) == CmdWrite)
        {
			var source = _bus.ReadLong(request + ExecLayout.IOStdReq.Data);
			_bus.WriteLong(request + ExecLayout.IOStdReq.Data, source + actual, cycle);
			_bus.WriteLong(request + ExecLayout.IOStdReq.Length, 0, cycle);
        }
		_bus.WriteByte(request + ExecLayout.IORequest.Error, error, cycle);
		_bus.WriteLong(request + ExecLayout.IOStdReq.Actual, error == 0 ? actual : 0, cycle);
		if (reply && (_bus.ReadByte(request + ExecLayout.IORequest.Flags) & IoQuick) == 0) _reply(request);
    }
    private void Register(int lvo, Action<M68kCpuState> callback) { var address = unchecked((uint)((int)DeviceBase + lvo)); RegisterAddress(address, callback); }
    private void RegisterAddress(uint address, Action<M68kCpuState> callback) => _gateways.Add((address, _bus.RegisterHostGateway(address, callback)));
	private uint FindDevice(uint list, string name) { for (var node = _bus.ReadLong(list); node != 0 && node != list + ExecLayout.List.Tail && _bus.IsMappedMemoryRange(node, ExecLayout.Node.Name + 4); node = _bus.ReadLong(node)) if (string.Equals(ReadName(_bus.ReadLong(node + ExecLayout.Node.Name)), name, StringComparison.OrdinalIgnoreCase)) return node; return 0; }
	private uint FindLibrary(uint execBase, string name) => execBase == 0 || !_bus.IsMappedMemoryRange(execBase + ExecLayout.ExecBase.LibraryList, (int)global::Amiga.List.Size) ? 0 : FindDevice(execBase + ExecLayout.ExecBase.LibraryList, name);
    private string ReadName(uint address) { Span<char> value = stackalloc char[64]; var length = 0; while (address != 0 && length < value.Length && _bus.IsMappedMemoryRange(address + (uint)length, 1)) { var c = _bus.ReadByte(address + (uint)length); if (c == 0) break; value[length++] = (char)c; } return new string(value[..length]); }
}

internal struct AmigaBusConsolePlatform : PortableDevices.IConsoleDevicePlatform
{
	private readonly AmigaBus _bus; private readonly long _cycle; private readonly Action<uint> _reply;
	private readonly ConsoleDeviceServices? _owner; private readonly M68kCpuState? _state;
	public AmigaBusConsolePlatform(AmigaBus bus, long cycle, Action<uint> reply) { _bus = bus; _cycle = cycle; _reply = reply; _owner = null; _state = null; }
	public AmigaBusConsolePlatform(AmigaBus bus, long cycle, Action<uint> reply, ConsoleDeviceServices owner, M68kCpuState state) { _bus = bus; _cycle = cycle; _reply = reply; _owner = owner; _state = state; }
	public byte ReadUInt8(APTR address, int offset = 0) => _bus.ReadByte(address.Raw + (uint)offset);
	public ushort ReadUInt16(APTR address, int offset = 0) => _bus.ReadWord(address.Raw + (uint)offset);
	public uint ReadUInt32(APTR address, int offset = 0) => _bus.ReadLong(address.Raw + (uint)offset);
	public void WriteUInt8(APTR address, int offset, byte value) => _bus.WriteByte(address.Raw + (uint)offset, value, _cycle);
	public void WriteUInt16(APTR address, int offset, ushort value) => _bus.WriteWord(address.Raw + (uint)offset, value, _cycle);
	public void WriteUInt32(APTR address, int offset, uint value) => _bus.WriteLong(address.Raw + (uint)offset, value, _cycle);
	public void Clear(APTR address, uint byteCount) => _bus.ClearMemory(address.Raw, checked((int)byteCount));
	public bool IsMapped(APTR address, uint byteSize) => byteSize <= int.MaxValue && _bus.IsMappedMemoryRange(address.Raw, (int)byteSize);
	public void Copy(APTR source, APTR destination, uint byteCount) { for (var offset = 0u; offset < byteCount; offset++) _bus.WriteByte(destination.Raw + offset, _bus.ReadByte(source.Raw + offset), _cycle); }
	public void ReplyMessage(APTR request) => _reply(request.Raw);
	public bool StartConsoleWrite(APTR unitState, APTR request, APTR data, uint length) => _owner is not null && _state is not null && _owner.StartPortableWrite(unitState, request, data, length, _state);
	public bool StartConsoleClear(APTR unitState, APTR request, bool redraw) => _owner is not null && _state is not null && _owner.StartPortableClear(unitState, request, redraw, _state);
}
