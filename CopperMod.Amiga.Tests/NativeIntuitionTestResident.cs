using CopperStart.Intuition.Native;

namespace CopperMod.Amiga.Tests;

// Instruction fixture for the shipping ROM emitter. The tests enter assembled
// guest code on the running CPU and use the booted machine's Exec services.
internal sealed class NativeIntuitionTestResident : IDisposable
{
    private readonly AmigaBus bus;
    private readonly uint storage;
    private readonly uint size;
    private readonly NativeIntuitionRomImage image;

    internal NativeIntuitionTestResident(AmigaBus bus)
    {
        this.bus = bus;
        var codeSize = ((uint)NativeIntuitionRomImage.Build().Bytes.Length + 3) & ~3u;
        size = codeSize + NativeIntuitionRomImage.LibraryBytes;
        storage = CopperStartLayersBootTests.AllocateGuestMemory(bus, size);
        image = NativeIntuitionRomImage.Build(storage);
        for (int i = 0; i < image.Bytes.Length; i++) bus.WriteHostByte(storage + (uint)i, image.Bytes[i]);
        Library = storage + codeSize;
        for (uint i = 0; i < NativeIntuitionRomImage.LibraryBytes; i += 4) bus.WriteLong(Library + i, 0);
        bus.WriteLong(Library + 80, bus.ReadLong(4));
        bus.WriteLong(Library + 84, AmigaKickstartHost.GraphicsLibraryBase);
    }

    internal uint Library { get; }
    internal uint Open => image.Entries[-6];
    internal uint Close => image.Entries[-12];
    internal ushort OpenCount => bus.ReadWord(Library + 32);
    internal uint Entry(int lvo) => image.Entries[lvo];
    public void Dispose() => CopperStartLayersBootTests.FreeGuestMemory(bus, storage, size);
}
