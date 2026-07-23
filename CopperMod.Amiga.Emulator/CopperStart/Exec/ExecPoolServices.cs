using System;
using System.Collections.Generic;
using Copper68k;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>Kickstart V39 Exec memory pools backed by the active guest MemList.</summary>
internal sealed class ExecPoolServices
{
    private const int PoolHeaderBytes = 16;
    private const uint MemfPublic = 0x0000_0001, MemfClear = 0x0001_0000;
    private readonly CopperStartExecContext _context;
    private readonly Dictionary<uint, Pool> _pools = new();
    public ExecPoolServices(CopperStartExecContext context) => _context = context;
    public uint CreatePool(M68kCpuState state)
    {
        var puddle = state.D[1]; var threshold = state.D[2]; if (puddle != 0 && threshold > puddle) return 0;
        var header = _context.MemoryOperations.Allocate(PoolHeaderBytes, MemfPublic | MemfClear); if (header == 0) return 0;
        _pools.Add(header, new Pool(state.D[0], puddle, threshold)); _context.Memory.WriteLong(header, state.D[0]); _context.Memory.WriteLong(header + 4, puddle); _context.Memory.WriteLong(header + 8, threshold); return header;
    }
    public uint AllocPooled(M68kCpuState state)
    {
        if (!_pools.TryGetValue(state.A[0], out var pool) || state.D[0] == 0) return 0; var bytes = Align(state.D[0]); if (bytes == 0) return 0;
        if (pool.PuddleSize == 0 || bytes > pool.Threshold) { var direct = _context.MemoryOperations.Allocate(checked((int)bytes), pool.Flags); if (direct != 0) pool.Allocations[direct] = new(direct, bytes, true); return direct; }
        foreach (var puddle in pool.Puddles) if (puddle.Remaining >= bytes) { var memory = puddle.Base + puddle.Next; puddle.Next += bytes; pool.Allocations[memory] = new(puddle.Base, bytes, false); if ((pool.Flags & MemfClear) != 0) _context.MemoryOperations.Clear(memory, checked((int)bytes)); return memory; }
        var puddleBytes = Math.Max(pool.PuddleSize, bytes); if (puddleBytes > int.MaxValue) return 0; var puddleBase = _context.MemoryOperations.Allocate((int)puddleBytes, pool.Flags); if (puddleBase == 0) return 0;
        pool.Puddles.Add(new Puddle(puddleBase, puddleBytes) { Next = bytes }); pool.Allocations[puddleBase] = new(puddleBase, bytes, false); if ((pool.Flags & MemfClear) != 0) _context.MemoryOperations.Clear(puddleBase, checked((int)bytes)); return puddleBase;
    }
    public uint FreePooled(M68kCpuState state) { if (_pools.TryGetValue(state.A[0], out var pool) && pool.Allocations.Remove(state.A[1], out var allocation) && allocation.Direct) _context.MemoryOperations.Free(allocation.Base, checked((int)allocation.Bytes)); return 0; }
    public uint DeletePool(M68kCpuState state)
    {
        if (!_pools.Remove(state.A[0], out var pool)) return 0; foreach (var allocation in pool.Allocations.Values) if (allocation.Direct) _context.MemoryOperations.Free(allocation.Base, checked((int)allocation.Bytes)); foreach (var puddle in pool.Puddles) _context.MemoryOperations.Free(puddle.Base, checked((int)puddle.Size)); _context.MemoryOperations.Free(state.A[0], PoolHeaderBytes); return 0;
    }
    public void Reset() => _pools.Clear();
    private static uint Align(uint value) => value > uint.MaxValue - 7 ? 0 : (value + 7) & ~7u;
    private sealed class Pool(uint flags, uint puddleSize, uint threshold) { public uint Flags { get; } = flags; public uint PuddleSize { get; } = puddleSize; public uint Threshold { get; } = threshold; public List<Puddle> Puddles { get; } = new(); public Dictionary<uint, Allocation> Allocations { get; } = new(); }
    private sealed class Puddle(uint @base, uint size) { public uint Base { get; } = @base; public uint Size { get; } = size; public uint Next { get; set; } public uint Remaining => Size - Next; }
    private readonly record struct Allocation(uint Base, uint Bytes, bool Direct);
}
