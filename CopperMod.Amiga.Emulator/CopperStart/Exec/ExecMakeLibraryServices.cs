using System;
using Copper68k;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>Builds guest library/device bases and their six-byte 68000 jump tables.</summary>
internal sealed class ExecMakeLibraryServices
{
    private const uint MemfPublicClear = 0x0001_0001;
    private readonly CopperStartExecContext _context;
    private readonly ExecInitStructServices _initStruct;
    private readonly uint _continuation;
    private PendingInit? _pending;

    public ExecMakeLibraryServices(CopperStartExecContext context, ExecInitStructServices initStruct, uint continuation)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _initStruct = initStruct ?? throw new ArgumentNullException(nameof(initStruct));
        _continuation = continuation;
    }

    public void Reset() => _pending = null;

    /// <summary>MakeFunctions(target=A0, functionArray=A1, functionDisplacementBase=A2).</summary>
    public uint MakeFunctions(M68kCpuState state)
    {
        var target = state.A[0];
        var vectors = state.A[1];
        var displacementBase = state.A[2];
        if (target == 0 || vectors == 0 || !_context.Memory.IsMapped(vectors, 2))
        {
            return 0;
        }

        var relative = displacementBase != 0;
        if (!TryCountVectors(vectors, relative, out var count) ||
            !WriteVectors(vectors, relative, vectors, displacementBase, count, target))
        {
            return 0;
        }

        return checked((uint)(count * 6));
    }

    /// <summary>MakeLibrary(vectors=A0, structure=A1, init=A2, size=D0, segList=D1).</summary>
    public void MakeLibrary(M68kCpuState state, Action<M68kCpuState, uint>? completed = null)
    {
        var vectors = state.A[0]; var structure = state.A[1]; var init = state.A[2];
        var positiveSize = state.D[0]; var segList = state.D[1];
        if (vectors == 0 || positiveSize is 0 or > 0xFFFF || !_context.Memory.IsMapped(vectors, 2)) { state.D[0] = 0; return; }
        var relative = _context.Memory.ReadWord(vectors) == 0xFFFF;
        var vectorStart = vectors + (relative ? 2u : 0u);
        if (!TryCountVectors(vectorStart, relative, out var count)) { state.D[0] = 0; return; }
        var negativeSize = checked(count * 6);
        var alignedPositiveSize = (positiveSize + 3u) & ~3u;
        var total = checked(negativeSize + (int)alignedPositiveSize);
        var allocation = _context.MemoryOperations.Allocate(total, MemfPublicClear);
        if (allocation == 0 || !_context.Memory.IsMapped(allocation, total)) { state.D[0] = 0; return; }
        var baseAddress = allocation + (uint)negativeSize;
        if (!WriteVectors(vectors, relative, vectorStart, vectors, count, baseAddress)) { _context.MemoryOperations.Free(allocation, total); state.D[0] = 0; return; }
        if (structure != 0)
        {
            var initState = new M68kCpuState(); initState.A[1] = structure; initState.A[2] = baseAddress; initState.D[0] = alignedPositiveSize;
            _ = _initStruct.InitStruct(initState);
        }
        // A Library base is always longword aligned.  The standard Library
        // header owns these sizes even when the supplied InitStruct omits them.
        _context.Memory.WriteWord(baseAddress + 0x10, (ushort)negativeSize);
        _context.Memory.WriteWord(baseAddress + 0x12, (ushort)alignedPositiveSize);
        if (init == 0) { state.D[0] = baseAddress; completed?.Invoke(state, baseAddress); return; }
        _pending = new PendingInit(allocation, total, completed);
        state.D[0] = baseAddress; state.A[0] = segList; state.A[6] = _context.GetExecBase();
        _context.StartGuestSubroutine(state, init, _continuation);
    }

    public void Continue(M68kCpuState state)
    {
        var pending = _pending; _pending = null;
        if (pending == null) return;
        if (state.D[0] == 0)
        {
            _context.MemoryOperations.Free(pending.Allocation, pending.TotalSize);
            return;
        }

        pending.Completed?.Invoke(state, state.D[0]);
    }

    private bool TryCountVectors(uint table, bool relative, out int count)
    {
        count = 0; var cursor = table;
        for (; count < 5461; count++, cursor += relative ? 2u : 4u)
        {
            if (!_context.Memory.IsMapped(cursor, relative ? 2 : 4)) return false;
            if (relative ? _context.Memory.ReadWord(cursor) == 0xFFFF : _context.Memory.ReadLong(cursor) == 0xFFFF_FFFF) return true;
        }
        return false;
    }

    private bool WriteVectors(uint table, bool relative, uint vectorStart, uint displacementBase, int count, uint baseAddress)
    {
        var cursor = vectorStart;
        for (var index = 0; index < count; index++, cursor += relative ? 2u : 4u)
        {
            var entry = relative ? unchecked((uint)((int)displacementBase + (short)_context.Memory.ReadWord(cursor))) : _context.Memory.ReadLong(cursor);
            if (!_context.IsFetchable(entry)) return false;
            var stub = baseAddress - (uint)((index + 1) * 6);
            _context.Memory.WriteWord(stub, 0x4EF9); _context.Memory.WriteLong(stub + 2, entry);
        }
        return true;
    }

    private sealed record PendingInit(uint Allocation, int TotalSize, Action<M68kCpuState, uint>? Completed);
}
