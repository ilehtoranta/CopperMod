using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsMonitorInitializationTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var ntsc in new[] { false, true })
        foreach (var ecs in new[] { false, true })
        foreach (var construction in new[] { "fixed", "hunk", "raster-hunk" })
        foreach (var scenario in new[] { "success", "no-memory", "short-base" })
            yield return new object[] { ntsc, ecs, construction, scenario };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ExplicitInitializerPublishesOnlyAfterPositiveImageAdmission(
        bool ntsc, bool ecs, string construction, string scenario)
    {
        const uint graphics = 0x00600000, heap = 0x00500000, exec = 0x00700000,
            stack = 0x00780100, returned = 0x00780180;
        var profile = ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal;
        const int size = GraphicsLibraryImageLayout.NativeRuntimeImageSize;
        var code = NativeGraphicsLibraryInitializer.BuildWithNativeMonitorState(size, profile, ecs,
            releaseLibraryOnFailure: construction != "fixed");
        var image = GraphicsDisplayDatabase.CreateNativeDatabaseImage(ecs, ntsc);
        var bus = new AmigaBus();
        uint entry = 0x00400000;
        if (construction == "fixed") bus.MapWritableMemory(entry, code);
        else
        {
            var hunk = construction == "hunk"
                ? NativeGraphicsLibraryHunkBuilder.Build(new byte[] { 0x4E, 0x75 }, size, profile,
                    true, true, publishNativeMonitorDatabase: true, supportsEcsDisplay: ecs)
                : NativeGraphicsLibraryHunkBuilder.BuildFromRasterBodies(size, profile,
                    true, true, publishNativeMonitorDatabase: true, supportsEcsDisplay: ecs);
            var addresses = new Queue<uint>(new[] { 0x00800000u, 0x00A80000u });
            var loader = new AmigaHunkProgramLoader(bus, bytes =>
            {
                var address = addresses.Dequeue();
                bus.MapWritableMemory(address, new byte[bytes]);
                return address;
            });
            var program = loader.Load(hunk.Bytes);
            var table = bus.ReadLong(program.SegmentBases[1] + 0x16);
            entry = bus.ReadLong(table + 12);
            Assert.Equal(code, Read(entry, code.Length));
            var functions = bus.ReadLong(table + 4);
            var template = program.SegmentBases[0] + (uint)hunk.VectorOffset;
            for (var slot = 0; slot < NativeGraphicsLibraryImageBuilder.VectorSlotCount; slot++)
            {
                var function = bus.ReadLong(functions + (uint)slot * 4);
                Assert.InRange(function, program.SegmentBases[0] + (uint)hunk.NativeCodeOffset, entry - 2);
                Assert.Equal(function, bus.ReadLong(template - (uint)(slot + 1) * 6 + 2));
            }
            Assert.Equal(0u, bus.ReadLong(template + 0x250));
        }
        bus.MapWritableMemory(graphics - 4, Enumerable.Repeat((byte)0xA5, 0x288).ToArray());
        bus.WriteWord(graphics + 0x10, 0x444);
        bus.WriteWord(graphics + 0x12, scenario == "short-base" ? (ushort)0x24C : (ushort)0x280);
        var before = Read(graphics, 0x280);
        bus.MapWritableMemory(heap, Enumerable.Repeat((byte)0xC7, image.Length + 4).ToArray());
        bus.MapWritableMemory(stack - 0x100, Enumerable.Repeat((byte)0x5A, 0x200).ToArray());
        bus.WriteLong(stack, returned);
        var allocations = 0;
        bus.RegisterHostGateway(exec - 198, state =>
        {
            allocations++;
            Assert.Equal(exec, state.A[6]);
            Assert.Equal((uint)image.Length, state.D[0]);
            Assert.Equal(0x10001u, state.D[1]);
            Assert.Equal(graphics + 0x220, bus.ReadLong(graphics + 0x0A));
            Assert.Equal(1u, bus.ReadLong(graphics + 0x254));
            Assert.Equal(0u, bus.ReadLong(graphics + 0x250));
            state.D[0] = scenario == "no-memory" ? 0 : heap;
            state.D[1] = state.A[0] = state.A[1] = 0xDEADBEEF;
        });
        var frees = 0;
        bus.RegisterHostGateway(exec - 210, state =>
        {
            frees++;
            Assert.NotEqual("fixed", construction);
            Assert.Equal("no-memory", scenario);
            Assert.Equal(exec, state.A[6]);
            Assert.Equal(graphics - 0x444, state.A[1]);
            Assert.Equal(0x444u + 0x280u, state.D[0]);
            Assert.Equal(0u, bus.ReadLong(graphics + 0x250));
            state.D[0] = state.D[1] = state.A[0] = state.A[1] = 0xDEADBEEF;
        });
        using var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
        cpu.Reset(entry, stack);
        cpu.State.D[0] = graphics;
        for (var i = 2; i < 8; i++) cpu.State.D[i] = 0xD0000000u + (uint)i;
        for (var i = 2; i < 6; i++) cpu.State.A[i] = 0xA0000000u + (uint)i;
        cpu.State.A[6] = exec;
        for (var instruction = 0; instruction < 5000 && cpu.State.ProgramCounter != returned; instruction++)
            cpu.ExecuteInstruction();
        Assert.Equal(returned, cpu.State.ProgramCounter);
        Assert.Equal(scenario == "success" ? graphics : 0, cpu.State.D[0]);
        Assert.Equal(scenario == "short-base" ? 0 : 1, allocations);
        Assert.Equal(construction != "fixed" && scenario == "no-memory" ? 1 : 0, frees);
        Assert.Equal(stack + 4, cpu.State.A[7]);
        for (var i = 2; i < 8; i++) Assert.Equal(0xD0000000u + (uint)i, cpu.State.D[i]);
        for (var i = 2; i < 6; i++) Assert.Equal(0xA0000000u + (uint)i, cpu.State.A[i]);
        Assert.Equal(exec, cpu.State.A[6]);
        if (scenario == "short-base") Assert.Equal(before, Read(graphics, 0x280));
        else
        {
            Assert.Equal((ushort)0x444, bus.ReadWord(graphics + 0x10));
            Assert.Equal((ushort)0x280, bus.ReadWord(graphics + 0x12));
            Assert.Equal(ntsc ? (ushort)1 : (ushort)4,
                bus.ReadWord(graphics + (uint)GraphicsLibraryImageLayout.GfxBaseDisplayFlags));
            Assert.Equal(scenario == "success" ? GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag : 0,
                bus.ReadLong(graphics + 0x250));
            Assert.Equal(scenario == "success" ? heap : 0, bus.ReadLong(graphics + 0x25C));
        }
        if (scenario == "success") Assert.Equal(image, Read(heap, image.Length));
        else Assert.All(Read(heap, image.Length), value => Assert.Equal((byte)0xC7, value));
        Assert.Equal(0xC7C7C7C7u, bus.ReadLong(heap + (uint)image.Length));
        Assert.Equal(0xA5A5A5A5u, bus.ReadLong(graphics - 4));
        Assert.All(Read(graphics + size, 0x280 - size + 4), value => Assert.Equal((byte)0xA5, value));
        Assert.Equal(0x5A5A5A5Au, bus.ReadLong(stack - 32));

        byte[] Read(uint address, int count) => Enumerable.Range(0, count).Select(i => bus.ReadByte(address + (uint)i)).ToArray();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void HunkPublicationRequiresExplicitInitializationAndDescriptor(bool descriptor, bool initialize)
        => Assert.Throws<ArgumentException>(() => NativeGraphicsLibraryHunkBuilder.Build(new byte[] { 0x4E, 0x75 },
            GraphicsLibraryImageLayout.NativeRuntimeImageSize, GraphicsLibraryImageProfile.NativePal,
            descriptor, initialize, publishNativeMonitorDatabase: true));
}
