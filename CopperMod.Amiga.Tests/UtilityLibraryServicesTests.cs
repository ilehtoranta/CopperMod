using Copper68k;
using CopperMod.Amiga;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart.Utility;

namespace CopperMod.Amiga.Tests;

public sealed class UtilityLibraryServicesTests
{
    private const uint ExecBase = 0x3000, Library = 0x3500, Name = 0x3600;

    [Fact]
    public void TagVectorsTraverseChainsAndProvideLookupMappingFilteringAndClones()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        var allocator = new TestAllocator(bus, 0x6000);
        using var service = new UtilityLibraryServices(new CopperStartUtilityContext(bus, new HostGuestMemory(bus), allocator.Allocate, allocator.Free));
        InitializeUtilityLibrary(bus);
        Assert.True(service.TryInstall(ExecBase));
        Assert.Equal(39, service.GatewayRegistrationCountForTests());

        const uint list = 0x4000, tail = 0x4100, statePointer = 0x4200;
        WriteTag(bus, list, 0x8000_1000, 11);
        WriteTag(bus, list + 8, 1, 0);
        WriteTag(bus, list + 16, 3, 1);
        WriteTag(bus, list + 24, 0x8000_1001, 22); // skipped
        WriteTag(bus, list + 32, 2, tail);
        WriteTag(bus, tail, 0x8000_1002, 33);
        WriteTag(bus, tail + 8, 0, 0);

        bus.WriteLong(statePointer, list);
        var next = new M68kCpuState(); next.A[0] = statePointer;
        Assert.True(Invoke(bus, Library - 48, next));
        Assert.Equal(list, next.D[0]);
        Assert.True(Invoke(bus, Library - 48, next));
        Assert.Equal(tail, next.D[0]);
        Assert.True(Invoke(bus, Library - 48, next));
        Assert.Equal(0u, next.D[0]);

        var find = new M68kCpuState(); find.D[0] = 0x8000_1002; find.A[0] = list;
        Assert.True(Invoke(bus, Library - 30, find));
        Assert.Equal(tail, find.D[0]);
        var get = new M68kCpuState(); get.D[0] = 0x8000_1003; get.D[1] = 99; get.A[0] = list;
        Assert.True(Invoke(bus, Library - 36, get));
        Assert.Equal(99u, get.D[0]);

        const uint map = 0x4300;
        WriteTag(bus, map, 0x8000_1000, 0x8000_2000); WriteTag(bus, map + 8, 0, 0);
        var mapState = new M68kCpuState(); mapState.A[0] = list; mapState.A[1] = map; mapState.D[0] = 1;
        Assert.True(Invoke(bus, Library - 60, mapState));
        Assert.Equal(1u, mapState.D[0]);
        Assert.Equal(0x8000_2000u, bus.ReadLong(list));

        const uint changes = 0x4400;
        WriteTag(bus, changes, 0x8000_1002, 44); WriteTag(bus, changes + 8, 0x8000_1FFF, 55); WriteTag(bus, changes + 16, 0, 0);
        var apply = new M68kCpuState(); apply.A[0] = list; apply.A[1] = changes;
        Assert.True(Invoke(bus, Library - 186, apply));
        Assert.Equal(44u, bus.ReadLong(tail + 4));

        var clone = new M68kCpuState(); clone.A[0] = list;
        Assert.True(Invoke(bus, Library - 72, clone));
        Assert.NotEqual(0u, clone.D[0]);
        Assert.Equal(0x8000_2000u, bus.ReadLong(clone.D[0]));
        Assert.Equal(0x8000_1002u, bus.ReadLong(clone.D[0] + 8));
        var free = new M68kCpuState(); free.A[0] = clone.D[0];
        Assert.True(Invoke(bus, Library - 78, free));
        Assert.Contains(clone.D[0] - 4, allocator.Freed);
    }

    [Fact]
    public void StructureTagConvertersHonorWidthsDirectionsSignsBitsAndTagBaseChanges()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        var allocator = new TestAllocator(bus, 0x6000);
        using var service = new UtilityLibraryServices(new CopperStartUtilityContext(bus, new HostGuestMemory(bus), allocator.Allocate, allocator.Free));
        InitializeUtilityLibrary(bus);
        Assert.True(service.TryInstall(ExecBase));

        const uint data = 0x4500, table = 0x4600, tags = 0x4700;
        const uint baseOne = 0x8000_1000, baseTwo = 0x8000_2000;
        WriteTag(bus, tags, baseOne + 1, 0x0000_00AB);
        WriteTag(bus, tags + 8, baseOne + 2, 0x0000_1234);
        WriteTag(bus, tags + 16, baseOne + 3, 0x89AB_CDEF);
        WriteTag(bus, tags + 24, baseOne + 4, 1);
        WriteTag(bus, tags + 32, baseTwo + 2, 0x0000_007E);
        WriteTag(bus, tags + 40, 0, 0);

        WriteLongs(bus, table,
            baseOne,
            0x8001_0000,             // signed byte, tag +1, offset 0
            0x8802_0002,             // signed word, tag +2, offset 2
            0x1003_0004,             // unsigned long, tag +3, offset 4
            0x1804_6008,             // bit 3 at offset 8, tag +4
            0xFFFF_FFFF, baseTwo,
            0x0002_000C,             // unsigned byte, new base +2, offset 12
            0);

        var pack = new M68kCpuState(); pack.A[0] = data; pack.A[1] = table; pack.A[2] = tags;
        Assert.True(Invoke(bus, Library - 210, pack));
        Assert.Equal(5u, pack.D[0]);
        Assert.Equal(0xAB, bus.ReadByte(data));
        Assert.Equal(0x1234, bus.ReadWord(data + 2));
        Assert.Equal(0x89AB_CDEFu, bus.ReadLong(data + 4));
        Assert.Equal(0x08, bus.ReadByte(data + 8));
        Assert.Equal(0x7E, bus.ReadByte(data + 12));

        bus.WriteByte(data, 0xFF, 0);
        bus.WriteWord(data + 2, 0xFF80);
        bus.WriteLong(data + 4, 0x1122_3344);
        bus.WriteByte(data + 8, 0x08, 0);
        bus.WriteByte(data + 12, 0x7F, 0);
        for (var index = 0; index < 5; index++) bus.WriteLong(tags + (uint)(index * 8 + 4), 0);

        var unpack = new M68kCpuState(); unpack.A[0] = data; unpack.A[1] = table; unpack.A[2] = tags;
        Assert.True(Invoke(bus, Library - 216, unpack));
        Assert.Equal(5u, unpack.D[0]);
        Assert.Equal(0xFFFF_FFFFu, bus.ReadLong(tags + 4));
        Assert.Equal(0xFFFF_FF80u, bus.ReadLong(tags + 12));
        Assert.Equal(0x1122_3344u, bus.ReadLong(tags + 20));
        Assert.Equal(1u, bus.ReadLong(tags + 28));
        Assert.Equal(0x7Fu, bus.ReadLong(tags + 36));
    }

    [Fact]
    public void UtilityDateStringAndUniqueIdHelpersUseGuestDataAndTheAmigaEpoch()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        var allocator = new TestAllocator(bus, 0x6000);
        using var service = new UtilityLibraryServices(new CopperStartUtilityContext(bus, new HostGuestMemory(bus), allocator.Allocate, allocator.Free));
        InitializeUtilityLibrary(bus);
        Assert.True(service.TryInstall(ExecBase));
        Assert.Equal(39, service.GatewayRegistrationCountForTests());

        WriteCString(bus, 0x4800, "Hello"); WriteCString(bus, 0x4820, "hELLo"); WriteCString(bus, 0x4840, "Helm");
        var same = new M68kCpuState(); same.A[0] = 0x4800; same.A[1] = 0x4820;
        Assert.True(Invoke(bus, Library - 162, same));
        Assert.Equal(0u, same.D[0]);
        var ordered = new M68kCpuState(); ordered.A[0] = 0x4800; ordered.A[1] = 0x4840; ordered.D[0] = 3;
        Assert.True(Invoke(bus, Library - 168, ordered));
        Assert.Equal(0u, ordered.D[0]);
        ordered.D[0] = 5;
        Assert.True(Invoke(bus, Library - 168, ordered));
        Assert.True(unchecked((int)ordered.D[0]) < 0);
        var upper = new M68kCpuState(); upper.D[0] = (uint)'q';
        Assert.True(Invoke(bus, Library - 174, upper));
        Assert.Equal((uint)'Q', upper.D[0]);
        var lower = new M68kCpuState(); lower.D[0] = (uint)'Q';
        Assert.True(Invoke(bus, Library - 180, lower));
        Assert.Equal((uint)'q', lower.D[0]);

        const uint clock = 0x4900, output = 0x4920;
        WriteWords(bus, clock, 0, 0, 0, 2, 1, 1978, 1);
        var dateToAmiga = new M68kCpuState(); dateToAmiga.A[0] = clock;
        Assert.True(Invoke(bus, Library - 126, dateToAmiga)); Assert.Equal(86_400u, dateToAmiga.D[0]);
        var check = new M68kCpuState(); check.A[0] = clock;
        Assert.True(Invoke(bus, Library - 132, check)); Assert.Equal(86_400u, check.D[0]);
        WriteWords(bus, clock, 0, 0, 0, 31, 2, 1978, 0);
        Assert.True(Invoke(bus, Library - 132, check)); Assert.Equal(0u, check.D[0]);
        var amigaToDate = new M68kCpuState(); amigaToDate.D[0] = 86_400; amigaToDate.A[0] = output;
        Assert.True(Invoke(bus, Library - 120, amigaToDate));
        Assert.Equal(2, bus.ReadWord(output + 6)); Assert.Equal(1, bus.ReadWord(output + 8)); Assert.Equal(1978, bus.ReadWord(output + 10));

        var signedProduct = new M68kCpuState(); signedProduct.D[0] = unchecked((uint)-7); signedProduct.D[1] = 6;
        Assert.True(Invoke(bus, Library - 138, signedProduct)); Assert.Equal(unchecked((uint)-42), signedProduct.D[0]);
        var unsignedProduct = new M68kCpuState(); unsignedProduct.D[0] = 0xFFFF_FFFF; unsignedProduct.D[1] = 2;
        Assert.True(Invoke(bus, Library - 144, unsignedProduct)); Assert.Equal(0xFFFF_FFFE, unsignedProduct.D[0]);
        var signedDivision = new M68kCpuState(); signedDivision.D[0] = unchecked((uint)-17); signedDivision.D[1] = 5;
        Assert.True(Invoke(bus, Library - 150, signedDivision)); Assert.Equal(unchecked((uint)-3), signedDivision.D[0]); Assert.Equal(unchecked((uint)-2), signedDivision.D[1]);
        var unsignedDivision = new M68kCpuState(); unsignedDivision.D[0] = 17; unsignedDivision.D[1] = 5;
        Assert.True(Invoke(bus, Library - 156, unsignedDivision)); Assert.Equal(3u, unsignedDivision.D[0]); Assert.Equal(2u, unsignedDivision.D[1]);
        var signedWideProduct = new M68kCpuState(); signedWideProduct.D[0] = unchecked((uint)-2); signedWideProduct.D[1] = 3;
        Assert.True(Invoke(bus, Library - 198, signedWideProduct)); Assert.Equal(0xFFFF_FFFFu, signedWideProduct.D[0]); Assert.Equal(0xFFFF_FFFAu, signedWideProduct.D[1]);
        var unsignedWideProduct = new M68kCpuState(); unsignedWideProduct.D[0] = 0xFFFF_FFFF; unsignedWideProduct.D[1] = 2;
        Assert.True(Invoke(bus, Library - 204, unsignedWideProduct)); Assert.Equal(1u, unsignedWideProduct.D[0]); Assert.Equal(0xFFFF_FFFEu, unsignedWideProduct.D[1]);

        var firstId = new M68kCpuState(); var secondId = new M68kCpuState();
        Assert.True(Invoke(bus, Library - 270, firstId)); Assert.True(Invoke(bus, Library - 270, secondId));
        Assert.Equal(1u, firstId.D[0]); Assert.Equal(2u, secondId.D[0]);
    }

    [Fact]
    public void NamedObjectsAllocateNestFindAndReleaseThroughTheUtilityVectors()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        var allocator = new TestAllocator(bus, 0x6000);
        using var service = new UtilityLibraryServices(new CopperStartUtilityContext(bus, new HostGuestMemory(bus), allocator.Allocate, allocator.Free));
        InitializeUtilityLibrary(bus);
        Assert.True(service.TryInstall(ExecBase));

        WriteCString(bus, 0x4800, "root"); WriteCString(bus, 0x4820, "child");
        WriteTag(bus, 0x4900, 4000, 1); WriteTag(bus, 0x4908, 4003, 1); WriteTag(bus, 0x4910, 0, 0);
        var createRoot = new M68kCpuState(); createRoot.A[0] = 0x4800; createRoot.A[1] = 0x4900;
        Assert.True(Invoke(bus, Library - 228, createRoot)); var root = createRoot.D[0]; Assert.NotEqual(0u, root);
        var createChild = new M68kCpuState(); createChild.A[0] = 0x4820;
        Assert.True(Invoke(bus, Library - 228, createChild)); var child = createChild.D[0]; Assert.NotEqual(0u, child);
        var add = new M68kCpuState(); add.A[0] = root; add.A[1] = child;
        Assert.True(Invoke(bus, Library - 222, add)); Assert.Equal(1u, add.D[0]);
        var name = new M68kCpuState(); name.A[0] = child;
        Assert.True(Invoke(bus, Library - 252, name)); Assert.Equal("child", ReadCString(bus, name.D[0]));
        var find = new M68kCpuState(); find.A[0] = root; find.A[1] = 0x4820;
        Assert.True(Invoke(bus, Library - 240, find)); Assert.Equal(child, find.D[0]);
        var release = new M68kCpuState(); release.A[0] = child;
        Assert.True(Invoke(bus, Library - 258, release));
        var remove = new M68kCpuState(); remove.A[0] = child;
        Assert.True(Invoke(bus, Library - 234, remove)); Assert.Equal(1u, remove.D[0]);
        var free = new M68kCpuState(); free.A[0] = child;
        Assert.True(Invoke(bus, Library - 246, free)); Assert.Contains(child, allocator.Freed);
    }

    [Fact]
    public void CallHookPktEntersTheGuestHookThroughAContinuation()
    {
        var bus = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false)).Bus;
        var allocator = new TestAllocator(bus, 0x6000);
        M68kCpuState? capturedState = null; uint capturedEntry = 0, capturedContinuation = 0;
        using var service = new UtilityLibraryServices(new CopperStartUtilityContext(
            bus, new HostGuestMemory(bus), allocator.Allocate, allocator.Free,
            (state, entry, continuation) => { capturedState = state; capturedEntry = entry; capturedContinuation = continuation; }));
        InitializeUtilityLibrary(bus);
        Assert.True(service.TryInstall(ExecBase));
        const uint hook = 0x4800, entry = 0x4A00, message = 0x4C00, target = 0x4E00;
        bus.WriteLong(hook + 8, entry);
        var call = new M68kCpuState(); call.A[0] = hook; call.A[1] = message; call.A[2] = target;
        Assert.True(Invoke(bus, Library - 102, call));
        Assert.Same(call, capturedState); Assert.Equal(entry, capturedEntry); Assert.NotEqual(0u, capturedContinuation);
        Assert.Equal(hook, call.A[0]); Assert.Equal(message, call.A[1]); Assert.Equal(target, call.A[2]);
    }

    private static void InitializeUtilityLibrary(AmigaBus bus)
    {
        const uint list = ExecBase + 0x17A;
        bus.WriteLong(list, Library); bus.WriteLong(list + 4, 0); bus.WriteLong(list + 8, Library);
        bus.WriteLong(Library, list + 4); bus.WriteLong(Library + 4, list); bus.WriteLong(Library + 0x0A, Name);
        WriteCString(bus, Name, "utility.library");
    }

    private static void WriteTag(AmigaBus bus, uint address, uint tag, uint data) { bus.WriteLong(address, tag); bus.WriteLong(address + 4, data); }
    private static void WriteLongs(AmigaBus bus, uint address, params uint[] values) { for (var index = 0; index < values.Length; index++) bus.WriteLong(address + (uint)(index * 4), values[index]); }
    private static void WriteWords(AmigaBus bus, uint address, params ushort[] values) { for (var index = 0; index < values.Length; index++) bus.WriteWord(address + (uint)(index * 2), values[index]); }
    private static void WriteCString(AmigaBus bus, uint address, string value) { for (var i = 0; i < value.Length; i++) bus.WriteByte(address + (uint)i, (byte)value[i], 0); bus.WriteByte(address + (uint)value.Length, 0, 0); }
    private static string ReadCString(AmigaBus bus, uint address) { var value = new List<char>(); for (var offset = 0u; offset < 512; offset++) { var character = bus.ReadByte(address + offset); if (character == 0) break; value.Add((char)character); } return new string(value.ToArray()); }
    private static bool Invoke(AmigaBus bus, uint address, M68kCpuState state) => bus.TryInvokeHostGatewayAt(address, state);

    private sealed class TestAllocator(AmigaBus bus, uint next)
    {
        public List<uint> Freed { get; } = new();
        public uint Allocate(int bytes, uint flags) { var result = next; next += (uint)((bytes + 7) & ~7); bus.ClearMemory(result, bytes); return result; }
        public void Free(uint address, int bytes) => Freed.Add(address);
    }
}
