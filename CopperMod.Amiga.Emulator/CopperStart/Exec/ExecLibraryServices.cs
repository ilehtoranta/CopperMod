using System;
using Copper68k;
using CopperMod.Amiga.Bus;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>
/// ROM-owned Exec library, device, and resource list services.  This class
/// deliberately owns no shadow list: every operation reads or writes the
/// corresponding list in the active guest ExecBase.
/// </summary>
internal sealed class ExecLibraryServices
{
    private const int ExecResourceListOffset = 0x150;
    private const int ExecDeviceListOffset = 0x15E;
    private const int ExecLibListOffset = 0x17A;
    private const int LibraryVersionOffset = 0x14;
    private const int LibraryOpenCountOffset = 0x20;
    private const int IoDeviceOffset = 0x14;

    private readonly AmigaBus _bus;
    private readonly Func<uint> _getExecBase;
    private readonly Func<uint, uint, uint> _findName;
    private readonly Action<M68kCpuState, uint, uint> _startGuestSubroutine;
    private readonly Action<uint, uint, bool, M68kCpuState> _addNode;
    private readonly Action<uint, M68kCpuState> _removeNode;

    public ExecLibraryServices(
        AmigaBus bus,
        Func<uint> getExecBase,
        Func<uint, uint, uint> findName,
        Action<M68kCpuState, uint, uint> startGuestSubroutine,
        Action<uint, uint, bool, M68kCpuState> addNode,
        Action<uint, M68kCpuState> removeNode)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _getExecBase = getExecBase ?? throw new ArgumentNullException(nameof(getExecBase));
        _findName = findName ?? throw new ArgumentNullException(nameof(findName));
        _startGuestSubroutine = startGuestSubroutine ?? throw new ArgumentNullException(nameof(startGuestSubroutine));
        _addNode = addNode ?? throw new ArgumentNullException(nameof(addNode));
        _removeNode = removeNode ?? throw new ArgumentNullException(nameof(removeNode));
    }

    public void OpenLibrary(M68kCpuState state, uint continuationAddress)
    {
        var library = _findName(_getExecBase() + ExecLibListOffset, state.A[1]);
        if (library == 0 || !_bus.IsMappedMemoryRange(library, LibraryOpenCountOffset + 2) ||
            _bus.ReadWord(library + LibraryVersionOffset) < state.D[0])
        {
            state.D[0] = 0;
            return;
        }

        _startGuestSubroutine(state, library - 6, continuationAddress);
    }

    public void CloseLibrary(M68kCpuState state, uint continuationAddress)
    {
        var library = state.A[1];
        if (library < 12 || !_bus.IsMappedMemoryRange(library, LibraryOpenCountOffset + 2))
        {
            state.D[0] = 0;
            return;
        }

        _startGuestSubroutine(state, library - 12, continuationAddress);
    }

    public void AddLibrary(M68kCpuState state)
        => _addNode(_getExecBase() + ExecLibListOffset, state.A[1], true, state);

    public void RemLibrary(M68kCpuState state) => _removeNode(state.A[1], state);

    public void AddDevice(M68kCpuState state)
        => _addNode(_getExecBase() + ExecDeviceListOffset, state.A[1], true, state);

    public void RemDevice(M68kCpuState state) => _removeNode(state.A[1], state);

    public void OpenDevice(M68kCpuState state, uint continuationAddress)
    {
        var device = _findName(_getExecBase() + ExecDeviceListOffset, state.A[0]);
        if (device == 0 || !_bus.IsMappedMemoryRange(device, 6))
        {
            state.D[0] = 0xFFFF_FFFF;
            return;
        }

        _startGuestSubroutine(state, device - 6, continuationAddress);
    }

    public void CloseDevice(M68kCpuState state, uint continuationAddress)
    {
        var request = state.A[1];
        if (request == 0 || !_bus.IsMappedMemoryRange(request + IoDeviceOffset, 4))
        {
            state.D[0] = 0;
            return;
        }

        var device = _bus.ReadLong(request + IoDeviceOffset);
        if (device < 12 || !_bus.IsMappedMemoryRange(device, 12))
        {
            state.D[0] = 0;
            return;
        }

        _startGuestSubroutine(state, device - 12, continuationAddress);
    }

    public void AddResource(M68kCpuState state)
        => _addNode(_getExecBase() + ExecResourceListOffset, state.A[1], true, state);

    public void RemResource(M68kCpuState state) => _removeNode(state.A[1], state);

    public uint OpenResource(M68kCpuState state)
        => _findName(_getExecBase() + ExecResourceListOffset, state.A[1]);
}
