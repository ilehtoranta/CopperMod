using System;

namespace Copper68k;

// Shared 040/060 three-level paging. Cache placement is a bounded host policy;
// descriptor layouts and permission checks follow MC68040UM/MC68060UM.
internal sealed class M68040MmuState
{
    private struct Entry
    {
        internal ulong Key;
        internal uint Descriptor, DescriptorAddress;
        internal bool Valid, UpperWriteProtected;
    }
    private readonly Entry[] _atc = new Entry[128];
    private uint _tc, _srp, _urp, _itt0, _itt1, _dtt0, _dtt1;
    public uint TranslationControl { get => _tc; set => Set(ref _tc, value & 0xFFFF); }
    public uint SupervisorRootPointer { get => _srp; set => Set(ref _srp, value & 0xFFFFFE00); }
    public uint UserRootPointer { get => _urp; set => Set(ref _urp, value & 0xFFFFFE00); }
    public uint InstructionTransparentTranslation0 { get => _itt0; set => Set(ref _itt0, value); }
    public uint InstructionTransparentTranslation1 { get => _itt1; set => Set(ref _itt1, value); }
    public uint DataTransparentTranslation0 { get => _dtt0; set => Set(ref _dtt0, value); }
    public uint DataTransparentTranslation1 { get => _dtt1; set => Set(ref _dtt1, value); }
    public uint Status { get; set; }
    public bool BypassTranslation { get; set; }
    public uint Generation { get; private set; }
    public bool Enabled => (_tc & 0x8000) != 0;
    internal bool DirectIdentityAccessEnabled => BypassTranslation ||
        (!Enabled && ((_itt0 | _itt1 | _dtt0 | _dtt1) & 0x8000) == 0);
    private uint OffsetMask => (_tc & 0x4000) != 0 ? 0x1FFFu : 0xFFFu;

    private void Set(ref uint field, uint value)
    {
        if (field == value) return;
        // MOVEC changes the translation context, but software must PFLUSH the
        // ATCs. In particular, replacing SRP/URP does not evict old entries.
        field = value; Generation++;
    }
    public void Reset()
    {
        _tc = _srp = _urp = _itt0 = _itt1 = _dtt0 = _dtt1 = Status = 0;
        BypassTranslation = false; Generation = 0; Flush();
    }
    public void Flush() { Array.Clear(_atc); Generation++; }
    internal void Flush(uint? address, bool supervisor, bool includeGlobal)
    {
        Generation++;
        var mask = OffsetMask;
        for (var i = 0; i < _atc.Length; i++)
            if (_atc[i].Valid && (includeGlobal || (_atc[i].Descriptor & 0x400) == 0) &&
                (address is null || ((_atc[i].Key >> 2) == (address.Value & ~mask) &&
                    (_atc[i].Key & 1) == (supervisor ? 1ul : 0ul)))) _atc[i].Valid = false;
    }

    internal bool CanBypassTranslation(uint address, M68kBusAccessKind kind, bool write, bool supervisor)
    {
        if (BypassTranslation) return true;
        if (TryTransparent(address, kind, supervisor, out var attributes)) return !write || (attributes & 4) == 0;
        return !Enabled;
    }

    public bool TryTranslate(uint address, M68kBusAccessKind kind, bool write, bool supervisor,
        Func<uint, uint> read, out uint physical, out M68040MmuFault fault, Action<uint, uint>? store = null)
    {
        physical = address; fault = default;
        if (BypassTranslation) return true;
        if (TryTransparent(address, kind, supervisor, out var transparent))
        {
            if (write && (transparent & 4) != 0) return Fail(address, kind, write, 4, out fault);
            return true;
        }
        if (!Enabled) return true;
        var mask = OffsetMask;
        var page = address & ~mask;
        var instruction = kind == M68kBusAccessKind.CpuInstructionFetch;
        var key = ((ulong)page << 2) | (instruction ? 2ul : 0ul) | (supervisor ? 1ul : 0ul);
        ref var cached = ref _atc[((page >> (mask == 0xFFF ? 12 : 13)) & 63) + (instruction ? 64 : 0)];
        uint descriptor, descriptorAddress; bool upperWriteProtected;
        try
        {
            if (cached.Valid && cached.Key == key)
            {
                descriptor = cached.Descriptor; descriptorAddress = cached.DescriptorAddress;
                upperWriteProtected = cached.UpperWriteProtected;
            }
            else
            {
                var rootAddress = (supervisor ? _srp : _urp) + ((address >> 25) * 4);
                var root = read(rootAddress);
                if ((root & 2) == 0) return Fail(address, kind, write, 0, out fault, 0x2000);
                if ((root & 8) == 0 && store is not null) store(rootAddress, root | 8);
                var pointerAddress = (root & 0xFFFFFE00) + (((address >> 18) & 127) * 4);
                var pointer = read(pointerAddress);
                if ((pointer & 2) == 0) return Fail(address, kind, write, 0, out fault, 0x1000);
                if ((pointer & 8) == 0 && store is not null) store(pointerAddress, pointer | 8);
                upperWriteProtected = ((root | pointer) & 4) != 0;
                var pageShift = mask == 0xFFF ? 12 : 13;
                var pageCountMask = mask == 0xFFF ? 63u : 31u;
                var tableMask = mask == 0xFFF ? 0xFFFFFF00u : 0xFFFFFF80u;
                descriptorAddress = (pointer & tableMask) + (((address >> pageShift) & pageCountMask) * 4);
                descriptor = read(descriptorAddress);
                if ((descriptor & 3) == 2)
                {
                    descriptorAddress = descriptor & 0xFFFFFFFC;
                    descriptor = read(descriptorAddress);
                    if ((descriptor & 3) == 2) return Fail(address, kind, write, 0, out fault, 0x800);
                }
                if ((descriptor & 1) == 0) return Fail(address, kind, write, 0, out fault, 0x400);
            }
            if (!supervisor && (descriptor & 0x80) != 0) return Fail(address, kind, write, 0x80, out fault, 0x200);
            if (write && (upperWriteProtected || (descriptor & 4) != 0)) return Fail(address, kind, write, 4, out fault, 0x100);
            var required = 8u | (write ? 0x10u : 0);
            if ((descriptor & required) != required && store is not null)
            {
                descriptor |= required; store(descriptorAddress, descriptor);
            }
            // A debugger/compiler peek must not install an ATC entry: the real
            // access still has to perform timed table reads and U/M writes.
            if (store is not null)
                cached = new Entry { Key = key, Descriptor = descriptor, DescriptorAddress = descriptorAddress,
                    UpperWriteProtected = upperWriteProtected, Valid = true };
            physical = (descriptor & ~mask) | (address & mask);
            return true;
        }
        catch (Exception ex) when (ex is M68kEmulationException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return Fail(address, kind, write, 0x800, out fault, 0x80);
        }
    }

    public void Probe(uint address, M68kBusAccessKind kind, bool write, bool supervisor, Func<uint, uint> read, Action<uint, uint>? store = null)
    {
        // PTEST explicitly replaces the matching ATC entry.
        var key = ((ulong)(address & ~OffsetMask) << 2) |
            (kind == M68kBusAccessKind.CpuInstructionFetch ? 2ul : 0ul) | (supervisor ? 1ul : 0ul);
        Generation++;
        for (var i = 0; i < _atc.Length; i++)
            if (_atc[i].Valid && _atc[i].Key == key) _atc[i].Valid = false;
        if (TryTranslate(address, kind, write, supervisor, read, out var physical, out var fault, store))
        {
            var transparent = TryTransparent(address, kind, supervisor, out var attributes);
            var instruction = kind == M68kBusAccessKind.CpuInstructionFetch;
            var entry = _atc[((address >> (OffsetMask == 0xFFF ? 12 : 13)) & 63) + (instruction ? 64 : 0)];
            Status = transparent ? 2u : (physical & ~OffsetMask) | 1u |
                (entry.Descriptor & 0x7F4) | (entry.UpperWriteProtected ? 4u : 0u);
        }
        else Status = fault.Status;
    }

    private static bool Fail(uint address, M68kBusAccessKind kind, bool write, uint status, out M68040MmuFault fault, uint reason = 0x100)
    { fault = new(address, kind, write, status, FaultReason: reason); return false; }
    private bool TryTransparent(uint address, M68kBusAccessKind kind, bool supervisor, out uint attributes)
    {
        var instruction = kind == M68kBusAccessKind.CpuInstructionFetch;
        var first = instruction ? _itt0 : _dtt0; var second = instruction ? _itt1 : _dtt1;
        if (Matches(address, first, supervisor)) { attributes = first; return true; }
        if (Matches(address, second, supervisor)) { attributes = second; return true; }
        attributes = 0; return false;
    }
    private static bool Matches(uint address, uint register, bool supervisor)
    {
        if ((register & 0x8000) == 0) return false;
        var s = (register >> 13) & 3;
        if (s < 2 && (s == 1) != supervisor) return false;
        var mask = (register >> 16) & 255;
        return (((address >> 24) ^ (register >> 24)) & ~mask) == 0;
    }
}
