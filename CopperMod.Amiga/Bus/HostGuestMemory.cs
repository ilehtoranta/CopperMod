using System;

namespace CopperMod.Amiga.Bus;

/// <summary>Guarded zero-cycle RAM access for host library implementations.</summary>
internal sealed class HostGuestMemory
{
    private readonly Bus _bus;
    public HostGuestMemory(Bus bus) => _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    public bool IsMapped(uint address, int size) => !IsHardware(address, size) && _bus.IsMappedMemoryRange(address, size);
    public byte ReadByte(uint address) { Check(address, 1); return _bus.ReadHostByte(address); }
    public ushort ReadWord(uint address) { Check(address, 2); return _bus.ReadHostWord(address); }
    public uint ReadLong(uint address) { Check(address, 4); return _bus.ReadHostLong(address); }
    public void WriteByte(uint address, byte value) { Check(address, 1); _bus.WriteHostByte(address, value); }
    public void WriteWord(uint address, ushort value) { Check(address, 2); _bus.WriteHostWord(address, value); }
    public void WriteLong(uint address, uint value) { Check(address, 4); _bus.WriteHostLong(address, value); }
    private void Check(uint address, int size) { if (!IsMapped(address, size)) throw new InvalidOperationException($"Host guest-memory access is not valid at 0x{address:X8}."); }
    private static bool IsHardware(uint address, int size)
    {
        var last = address + (uint)Math.Max(0, size - 1);
        return (address <= 0x00DF_FFFF && last >= 0x00DF_0000) || (address <= 0x00BF_FFFF && last >= 0x00BF_0000);
    }
}
