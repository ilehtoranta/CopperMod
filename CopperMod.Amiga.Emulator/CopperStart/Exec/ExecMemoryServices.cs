using System;
using Copper68k;
using CopperMod.Amiga.Bus;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>Concrete access required by Exec memory LVO services.</summary>
internal sealed class ExecMemoryContext
{
    public ExecMemoryContext(AmigaBus bus, Func<int, uint, uint> allocate, Func<int, uint, uint> allocateAbsolute, Action<uint, int> free,
        Func<uint, uint> available, Func<uint, int, uint, uint> allocateFromHeader,
        Action<uint, uint, int> deallocateToHeader, Func<uint, uint> typeOfMemory,
        Action<int, uint, uint> recordAlloc, Action<int, uint, uint> recordAllocAbs,
        Action<uint, int> recordFree)
    {
        Bus = bus ?? throw new ArgumentNullException(nameof(bus)); Allocate = allocate ?? throw new ArgumentNullException(nameof(allocate));
        AllocateAbsolute = allocateAbsolute ?? throw new ArgumentNullException(nameof(allocateAbsolute)); Free = free ?? throw new ArgumentNullException(nameof(free)); Available = available ?? throw new ArgumentNullException(nameof(available));
        AllocateFromHeader = allocateFromHeader ?? throw new ArgumentNullException(nameof(allocateFromHeader));
        DeallocateToHeader = deallocateToHeader ?? throw new ArgumentNullException(nameof(deallocateToHeader));
        TypeOfMemory = typeOfMemory ?? throw new ArgumentNullException(nameof(typeOfMemory));
        RecordAlloc = recordAlloc ?? throw new ArgumentNullException(nameof(recordAlloc)); RecordAllocAbs = recordAllocAbs ?? throw new ArgumentNullException(nameof(recordAllocAbs));
        RecordFree = recordFree ?? throw new ArgumentNullException(nameof(recordFree));
    }
    public AmigaBus Bus { get; } public Func<int, uint, uint> Allocate { get; } public Func<int, uint, uint> AllocateAbsolute { get; } public Action<uint, int> Free { get; }
    public Func<uint, uint> Available { get; } public Func<uint, int, uint, uint> AllocateFromHeader { get; }
    public Action<uint, uint, int> DeallocateToHeader { get; } public Func<uint, uint> TypeOfMemory { get; }
    public Action<int, uint, uint> RecordAlloc { get; } public Action<int, uint, uint> RecordAllocAbs { get; } public Action<uint, int> RecordFree { get; }
}

/// <summary>Exec memory LVO entry points over the active guest MemList.</summary>
internal sealed class ExecMemoryServices
{
    private const uint MemfPublicClear = 0x0001_0001;
    private readonly ExecMemoryContext _context;
    public ExecMemoryServices(ExecMemoryContext context) => _context = context ?? throw new ArgumentNullException(nameof(context));
    public void AllocMem(M68kCpuState state) { var size = (int)Math.Min(state.D[0], int.MaxValue); var flags = state.D[1]; state.D[0] = _context.Allocate(Math.Max(4, size), flags); _context.RecordAlloc(size, flags, state.D[0]); }
    public void AllocMemAndStore(M68kCpuState state) { AllocMem(state); if (state.A[0] != 0) _context.Bus.WriteLong(state.A[0], state.D[0], state.Cycles); }
    public void AvailMem(M68kCpuState state) => state.D[0] = _context.Available(state.D[1]);
    public void AllocAbs(M68kCpuState state) { var size = (int)Math.Min(state.D[0], int.MaxValue); var location = state.A[1]; state.D[0] = _context.AllocateAbsolute(Math.Max(4, size), location); _context.RecordAllocAbs(size, location, state.D[0]); }
    public void FreeMem(M68kCpuState state) { var address = state.A[1]; var size = (int)Math.Min(state.D[0], int.MaxValue); _context.Free(address, size); _context.RecordFree(address, size); state.D[0] = 0; }
    public uint AllocVec(M68kCpuState state) { var requested = (int)Math.Min(state.D[0], int.MaxValue - 4); if (requested < 0) return 0; var bytes = Math.Max(4, requested + 4); var block = _context.Allocate(bytes, state.D[1]); if (block == 0) return 0; _context.Bus.WriteLong(block, (uint)bytes, state.Cycles); return block + 4; }
    public uint FreeVec(M68kCpuState state) { var user = state.A[1]; if (user < 4 || !_context.Bus.IsMappedMemoryRange(user - 4, 4)) return 0; var block = user - 4; var bytes = _context.Bus.ReadLong(block); if (bytes is >= 4 and <= int.MaxValue) _context.Free(block, (int)bytes); return 0; }
    public uint Allocate(M68kCpuState state) => _context.AllocateFromHeader(state.A[0], (int)Math.Min(state.D[0], int.MaxValue), 0);
    public uint Deallocate(M68kCpuState state) { _context.DeallocateToHeader(state.A[0], state.A[1], (int)Math.Min(state.D[0], int.MaxValue)); return 0; }
    public uint AllocEntry(M68kCpuState state)
    {
        var template = state.A[0]; if (template == 0 || !_context.Bus.IsMappedMemoryRange(template, 16)) return 0x8000_0000;
        var count = _context.Bus.ReadWord(template + 14); var bytes = 16 + count * 8;
        if (!_context.Bus.IsMappedMemoryRange(template, bytes)) return 0x8000_0000;
        var list = _context.Allocate(bytes, MemfPublicClear); if (list == 0) return 0x8000_0000;
        _context.Bus.WriteWord(list + 14, count);
        for (var index = 0; index < count; index++)
        {
            var entry = template + 16u + (uint)(index * 8); var reqs = _context.Bus.ReadLong(entry); var length = _context.Bus.ReadLong(entry + 4);
            var memory = length == 0 ? 0 : _context.Allocate(checked((int)Math.Min(length, int.MaxValue)), reqs);
            if (memory == 0) { FreeEntryList(list); return 0x8000_0000 | (reqs & 0x7FFF_FFFF); }
            _context.Bus.WriteLong(list + 16u + (uint)(index * 8), memory); _context.Bus.WriteLong(list + 20u + (uint)(index * 8), length);
        }
        return list;
    }
    public uint FreeEntry(M68kCpuState state) { FreeEntryList(state.A[0]); return 0; }
    private void FreeEntryList(uint list) { if (list == 0 || !_context.Bus.IsMappedMemoryRange(list, 16)) return; var count = _context.Bus.ReadWord(list + 14); if (!_context.Bus.IsMappedMemoryRange(list, 16 + count * 8)) return; for (var index = 0; index < count; index++) { var address = _context.Bus.ReadLong(list + 16u + (uint)(index * 8)); var length = _context.Bus.ReadLong(list + 20u + (uint)(index * 8)); if (address != 0 && length != 0) _context.Free(address, checked((int)Math.Min(length, int.MaxValue))); } _context.Free(list, 16 + count * 8); }
    public uint TypeOfMem(M68kCpuState state) => _context.TypeOfMemory(state.A[1]);
    public uint CopyMem(M68kCpuState state) { Copy(state.A[0], state.A[1], state.D[0]); return 0; }
    public uint CopyMemQuick(M68kCpuState state) { Copy(state.A[0], state.A[1], state.D[0]); return 0; }
    private void Copy(uint source, uint destination, uint length)
    {
        if (length == 0 || length > int.MaxValue || !_context.Bus.IsMappedMemoryRange(source, (int)length) || !_context.Bus.IsMappedMemoryRange(destination, (int)length)) return;
        if (_context.Bus.TryCopyMemory(source, destination, (int)length)) return;
        if (destination > source && destination < source + length) for (var i = length; i != 0; i--) _context.Bus.WriteByte(destination + i - 1, _context.Bus.ReadByte(source + i - 1), 0);
        else for (var i = 0u; i < length; i++) _context.Bus.WriteByte(destination + i, _context.Bus.ReadByte(source + i), 0);
    }
}
