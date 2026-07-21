using System;
using System.Collections.Generic;
using Copper68k;

namespace CopperMod.Amiga;

/// <summary>Kickstart 3.0 (V39) Exec memory pools backed by guest MemList blocks.</summary>
internal sealed partial class AmigaBootController
{
    private sealed class ExecPoolServices
    {
        private const int PoolHeaderBytes = 16;
        private readonly AmigaBootController _owner;
        private readonly Dictionary<uint, Pool> _pools = new();

        public ExecPoolServices(AmigaBootController owner) => _owner = owner;

        public uint CreatePool(M68kCpuState state)
        {
            var puddleSize = state.D[1];
            var threshold = state.D[2];
            if (puddleSize != 0 && threshold > puddleSize) return 0;
            var header = _owner.AllocateMemoryFromMemList(PoolHeaderBytes, MemfPublic | MemfClear);
            if (header == 0) return 0;
            _pools.Add(header, new Pool(state.D[0], puddleSize, threshold));
            _owner._machine.Bus.WriteLong(header + 0, state.D[0], state.Cycles);
            _owner._machine.Bus.WriteLong(header + 4, puddleSize, state.Cycles);
            _owner._machine.Bus.WriteLong(header + 8, threshold, state.Cycles);
            return header;
        }

        public uint AllocPooled(M68kCpuState state)
        {
            if (!_pools.TryGetValue(state.A[0], out var pool) || state.D[0] == 0) return 0;
            var bytes = Align(state.D[0]);
            if (bytes == 0) return 0;
            if (pool.PuddleSize == 0 || bytes > pool.Threshold)
            {
                var direct = _owner.AllocateMemoryFromMemList(checked((int)bytes), pool.Flags);
                if (direct != 0) pool.Allocations[direct] = new Allocation(direct, bytes, true);
                return direct;
            }

            foreach (var puddle in pool.Puddles)
            {
                if (puddle.Remaining < bytes) continue;
                var memory = puddle.Base + puddle.Next;
                puddle.Next += bytes;
                pool.Allocations[memory] = new Allocation(puddle.Base, bytes, false);
                if ((pool.Flags & MemfClear) != 0) _owner._machine.Bus.ClearMemory(memory, checked((int)bytes));
                return memory;
            }

            var puddleBytes = Math.Max(pool.PuddleSize, bytes);
            if (puddleBytes > int.MaxValue) return 0;
            var puddleBase = _owner.AllocateMemoryFromMemList((int)puddleBytes, pool.Flags);
            if (puddleBase == 0) return 0;
            var newPuddle = new Puddle(puddleBase, puddleBytes) { Next = bytes };
            pool.Puddles.Add(newPuddle);
            pool.Allocations[puddleBase] = new Allocation(puddleBase, bytes, false);
            if ((pool.Flags & MemfClear) != 0) _owner._machine.Bus.ClearMemory(puddleBase, checked((int)bytes));
            return puddleBase;
        }

        public uint FreePooled(M68kCpuState state)
        {
            if (!_pools.TryGetValue(state.A[0], out var pool) || !pool.Allocations.Remove(state.A[1], out var allocation)) return 0;
            if (allocation.Direct) _owner.FreeMemoryToMemList(allocation.Base, checked((int)allocation.Bytes));
            return 0;
        }

        public uint DeletePool(M68kCpuState state)
        {
            var header = state.A[0];
            if (!_pools.Remove(header, out var pool)) return 0;
            foreach (var allocation in pool.Allocations.Values)
                if (allocation.Direct) _owner.FreeMemoryToMemList(allocation.Base, checked((int)allocation.Bytes));
            foreach (var puddle in pool.Puddles) _owner.FreeMemoryToMemList(puddle.Base, checked((int)puddle.Size));
            _owner.FreeMemoryToMemList(header, PoolHeaderBytes);
            return 0;
        }

        public void Reset() => _pools.Clear();
        private static uint Align(uint value) => value > uint.MaxValue - 7 ? 0 : (value + 7) & ~7u;

        private sealed class Pool(uint flags, uint puddleSize, uint threshold)
        {
            public uint Flags { get; } = flags;
            public uint PuddleSize { get; } = puddleSize;
            public uint Threshold { get; } = threshold;
            public List<Puddle> Puddles { get; } = new();
            public Dictionary<uint, Allocation> Allocations { get; } = new();
        }
        private sealed class Puddle(uint @base, uint size) { public uint Base { get; } = @base; public uint Size { get; } = size; public uint Next { get; set; } public uint Remaining => Size - Next; }
        private readonly record struct Allocation(uint Base, uint Bytes, bool Direct);
    }
}
