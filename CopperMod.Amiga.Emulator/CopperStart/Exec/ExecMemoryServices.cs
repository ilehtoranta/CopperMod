using System;
using Copper68k;

namespace CopperMod.Amiga;

/// <summary>Exec memory LVO entry points over the active guest MemList.</summary>
internal sealed partial class AmigaBootController
{
    private sealed class ExecMemoryServices
    {
        private readonly AmigaBootController _owner;
        public ExecMemoryServices(AmigaBootController owner) => _owner = owner;

        public void AllocMem(M68kCpuState state)
        {
            var size = (int)Math.Min(state.D[0], int.MaxValue);
            var flags = state.D[1];
            state.D[0] = _owner.AllocateMemoryFromMemList(Math.Max(4, size), flags);
            _owner.RecordAllocDiagnostic(size, flags, state.D[0]);
        }

        public void AllocMemAndStore(M68kCpuState state)
        {
            AllocMem(state);
            if (state.A[0] != 0) _owner._machine.Bus.WriteLong(state.A[0], state.D[0], state.Cycles);
        }

        public void AvailMem(M68kCpuState state) => state.D[0] = _owner.QueryAvailableMemory(state.D[1]);

        public void AllocAbs(M68kCpuState state)
        {
            var size = (int)Math.Min(state.D[0], int.MaxValue);
            var location = state.A[1];
            state.D[0] = _owner.AllocateAbsoluteMemoryFromMemList(Math.Max(4, size), location);
            _owner.RecordAllocAbsDiagnostic(size, location, state.D[0]);
        }

        public void FreeMem(M68kCpuState state)
        {
            var address = state.A[1]; var size = (int)Math.Min(state.D[0], int.MaxValue);
            _owner.FreeMemoryToMemList(address, size);
            _owner.RecordFreeDiagnostic(address, size);
            state.D[0] = 0;
        }

        public uint AllocVec(M68kCpuState state)
        {
            var requested = (int)Math.Min(state.D[0], int.MaxValue - 4);
            if (requested < 0) return 0;
            var block = _owner.AllocateMemoryFromMemList(Math.Max(4, requested + 4), state.D[1]);
            if (block == 0) return 0;
            _owner._machine.Bus.WriteLong(block, (uint)Math.Max(4, requested + 4), state.Cycles);
            return block + 4;
        }

        public uint FreeVec(M68kCpuState state)
        {
            var userBlock = state.A[1];
            if (userBlock < 4 || !_owner._machine.Bus.IsMappedMemoryRange(userBlock - 4, 4)) return 0;
            var block = userBlock - 4;
            var bytes = _owner._machine.Bus.ReadLong(block);
            if (bytes >= 4 && bytes <= int.MaxValue) _owner.FreeMemoryToMemList(block, (int)bytes);
            return 0;
        }

        public uint Allocate(M68kCpuState state)
            => _owner.AllocateFromMemoryHeader(state.A[0], (int)Math.Min(state.D[0], int.MaxValue), 0);

        public uint Deallocate(M68kCpuState state)
        {
            _owner.DeallocateToMemoryHeader(state.A[0], state.A[1], (int)Math.Min(state.D[0], int.MaxValue));
            return 0;
        }

        public uint AllocEntry(M68kCpuState state)
        {
            var template = state.A[0];
            if (template == 0 || !_owner._machine.Bus.IsMappedMemoryRange(template, 16)) return 0x8000_0000;
            var count = _owner._machine.Bus.ReadWord(template + 14);
            var bytes = 16 + count * 8;
            if (!_owner._machine.Bus.IsMappedMemoryRange(template, bytes)) return 0x8000_0000;
            var list = _owner.AllocateMemoryFromMemList(bytes, MemfPublic | MemfClear);
            if (list == 0) return 0x8000_0000;
            _owner._machine.Bus.WriteWord(list + 14, count);
            for (var index = 0; index < count; index++)
            {
                var entry = template + 16u + (uint)(index * 8);
                var reqs = _owner._machine.Bus.ReadLong(entry);
                var length = _owner._machine.Bus.ReadLong(entry + 4);
                var memory = length == 0 ? 0 : _owner.AllocateMemoryFromMemList(checked((int)Math.Min(length, int.MaxValue)), reqs);
                if (memory == 0)
                {
                    FreeEntryList(list);
                    return 0x8000_0000 | (reqs & 0x7FFF_FFFF);
                }
                _owner._machine.Bus.WriteLong(list + 16u + (uint)(index * 8), memory);
                _owner._machine.Bus.WriteLong(list + 20u + (uint)(index * 8), length);
            }
            return list;
        }

        public uint FreeEntry(M68kCpuState state) { FreeEntryList(state.A[0]); return 0; }

        private void FreeEntryList(uint list)
        {
            if (list == 0 || !_owner._machine.Bus.IsMappedMemoryRange(list, 16)) return;
            var count = _owner._machine.Bus.ReadWord(list + 14);
            if (!_owner._machine.Bus.IsMappedMemoryRange(list, 16 + count * 8)) return;
            for (var index = 0; index < count; index++)
            {
                var address = _owner._machine.Bus.ReadLong(list + 16u + (uint)(index * 8));
                var length = _owner._machine.Bus.ReadLong(list + 20u + (uint)(index * 8));
                if (address != 0 && length != 0) _owner.FreeMemoryToMemList(address, checked((int)Math.Min(length, int.MaxValue)));
            }
            _owner.FreeMemoryToMemList(list, 16 + count * 8);
        }

        public uint TypeOfMem(M68kCpuState state) => _owner.TypeOfGuestMemory(state.A[1]);

        public uint CopyMem(M68kCpuState state) { Copy(state.A[0], state.A[1], state.D[0]); return 0; }
        public uint CopyMemQuick(M68kCpuState state) { Copy(state.A[0], state.A[1], state.D[0]); return 0; }
        private void Copy(uint source, uint destination, uint length)
        {
            if (length == 0 || length > int.MaxValue || !_owner._machine.Bus.IsMappedMemoryRange(source, (int)length) || !_owner._machine.Bus.IsMappedMemoryRange(destination, (int)length)) return;
            if (_owner._machine.Bus.TryCopyMemory(source, destination, (int)length)) return;
            if (destination > source && destination < source + length)
                CopyBackward(source, destination, length);
            else
                CopyForward(source, destination, length);
        }

        private void CopyForward(uint source, uint destination, uint length)
        {
            var offset = 0u;
            while (length - offset >= 4 && ((source + offset) & 1) == 0 && ((destination + offset) & 1) == 0)
            {
                _owner._machine.Bus.WriteLong(destination + offset, _owner._machine.Bus.ReadLong(source + offset), 0); offset += 4;
            }
            while (length - offset >= 2 && ((source + offset) & 1) == 0 && ((destination + offset) & 1) == 0)
            {
                _owner._machine.Bus.WriteWord(destination + offset, _owner._machine.Bus.ReadWord(source + offset), 0); offset += 2;
            }
            while (offset < length) { _owner._machine.Bus.WriteByte(destination + offset, _owner._machine.Bus.ReadByte(source + offset), 0); offset++; }
        }

        private void CopyBackward(uint source, uint destination, uint length)
        {
            var remaining = length;
            while (remaining >= 4 && ((source + remaining) & 1) == 0 && ((destination + remaining) & 1) == 0)
            {
                remaining -= 4; _owner._machine.Bus.WriteLong(destination + remaining, _owner._machine.Bus.ReadLong(source + remaining), 0);
            }
            while (remaining >= 2 && ((source + remaining) & 1) == 0 && ((destination + remaining) & 1) == 0)
            {
                remaining -= 2; _owner._machine.Bus.WriteWord(destination + remaining, _owner._machine.Bus.ReadWord(source + remaining), 0);
            }
            while (remaining != 0) { remaining--; _owner._machine.Bus.WriteByte(destination + remaining, _owner._machine.Bus.ReadByte(source + remaining), 0); }
        }
    }
}
