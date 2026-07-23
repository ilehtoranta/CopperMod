using System;
using Copper68k;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>Resident lookup and AUTOINIT construction over guest-owned Exec state.</summary>
internal sealed class ExecResidentServices
{
    private const int ResModulesOffset = 0x12C;
    private const int DeviceListOffset = 0x15E;
    private const int LibraryListOffset = 0x17A;
    private const int ResidentMatchTagOffset = 0x02;
    private const int ResidentEndSkipOffset = 0x06;
    private const int ResidentFlagsOffset = 0x0A;
    private const int ResidentTypeOffset = 0x0C;
    private const int ResidentNameOffset = 0x0E;
    private const int ResidentInitOffset = 0x16;
    private const byte ResidentAutoInit = 0x80;
    private const byte NodeTypeDevice = 3;
    private const byte NodeTypeLibrary = 9;
    private readonly CopperStartExecContext _context;
    private readonly ExecListServices _lists;
    private readonly ExecMakeLibraryServices _makeLibrary;
    private readonly uint _continuation;

    public ExecResidentServices(
        CopperStartExecContext context,
        ExecListServices lists,
        ExecMakeLibraryServices makeLibrary,
        uint continuation)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _lists = lists ?? throw new ArgumentNullException(nameof(lists));
        _makeLibrary = makeLibrary ?? throw new ArgumentNullException(nameof(makeLibrary));
        _continuation = continuation;
    }
    public void FindResident(M68kCpuState state)
    {
        if (!_context.UsesRomExec()) { state.D[0] = Contains(state.A[1], "dos") ? _context.EnsureCompatibilityDosResident() : 0; return; }
        if (state.A[1] == 0) { state.D[0] = 0; return; }
        var target = _context.ReadString(state.A[1], 96); var entry = _context.Memory.ReadLong(_context.GetExecBase() + ResModulesOffset);
        for (var scanned = 0; entry != 0 && entry != 0xFFFF_FFFF && scanned < 2048; scanned++, entry += 4)
        {
            if (!_context.Memory.IsMapped(entry, 4)) break; var resident = _context.Memory.ReadLong(entry);
            if (resident == 0 || resident == 0xFFFF_FFFF || !_context.Memory.IsMapped(resident, 0x16) || _context.Memory.ReadWord(resident) != 0x4AFC) continue;
            if (string.Equals(target, _context.ReadString(_context.Memory.ReadLong(resident + ResidentNameOffset), 96), StringComparison.OrdinalIgnoreCase)) { state.D[0] = resident; return; }
        }
        state.D[0] = 0;
    }

    /// <summary>InitResident(resident=A1, segList=D1), limited to AUTOINIT libraries and devices.</summary>
    public void InitResident(M68kCpuState state)
    {
        var resident = state.A[1];
        if (!TryReadResident(resident, out var flags, out var type, out var init))
        {
            state.D[0] = 0;
            return;
        }

        if ((flags & ResidentAutoInit) == 0)
        {
            state.D[0] = 0;
            state.A[0] = state.D[1];
            state.A[6] = _context.GetExecBase();
            _context.StartGuestSubroutine(state, init, _continuation);
            return;
        }

        if (type is not (NodeTypeLibrary or NodeTypeDevice) ||
            !_context.Memory.IsMapped(init, 16))
        {
            state.D[0] = 0;
            return;
        }

        var positiveSize = _context.Memory.ReadLong(init);
        var vectors = _context.Memory.ReadLong(init + 4);
        var structure = _context.Memory.ReadLong(init + 8);
        var libraryInit = _context.Memory.ReadLong(init + 12);
        var segList = state.D[1];
        state.A[0] = vectors;
        state.A[1] = structure;
        state.A[2] = libraryInit;
        state.D[0] = positiveSize;
        state.D[1] = segList;
        _makeLibrary.MakeLibrary(state, (completedState, baseAddress) => AddAutoinitNode(completedState, baseAddress, type));
    }

    /// <summary>Completes a non-AUTOINIT resident init entered through guest code.</summary>
    public void Continue(M68kCpuState state)
    {
    }

    private bool TryReadResident(uint resident, out byte flags, out byte type, out uint init)
    {
        flags = 0;
        type = 0;
        init = 0;
        if (resident == 0 ||
            !_context.Memory.IsMapped(resident, ResidentInitOffset + 4) ||
            _context.Memory.ReadWord(resident) != 0x4AFC ||
            _context.Memory.ReadLong(resident + ResidentMatchTagOffset) != resident)
        {
            return false;
        }

        var endSkip = _context.Memory.ReadLong(resident + ResidentEndSkipOffset);
        init = _context.Memory.ReadLong(resident + ResidentInitOffset);
        if (endSkip <= resident || init == 0)
        {
            return false;
        }

        flags = _context.Memory.ReadByte(resident + ResidentFlagsOffset);
        type = _context.Memory.ReadByte(resident + ResidentTypeOffset);
        return (flags & ResidentAutoInit) != 0 || _context.IsFetchable(init);
    }

    private void AddAutoinitNode(M68kCpuState state, uint baseAddress, byte type)
    {
        if (!_lists.IsValidNode(baseAddress))
        {
            state.D[0] = 0;
            return;
        }

        var list = _context.GetExecBase() + (uint)(type == NodeTypeLibrary ? LibraryListOffset : DeviceListOffset);
        if (!_lists.IsValidList(list))
        {
            state.D[0] = 0;
            return;
        }

        // CopperStart's compact synthetic Exec image does not pre-populate
        // every compatibility list. A real ROM Exec list is already live and
        // therefore passes through unchanged.
        if (!_context.UsesRomExec())
        {
            _lists.Ensure(list);
        }

        _lists.Enqueue(list, baseAddress);
        state.D[0] = baseAddress;
    }
    private bool Contains(uint address, string value) => address != 0 && _context.ReadString(address, 96).Contains(value, StringComparison.OrdinalIgnoreCase);
}
