using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsMonitorSpecCompositionTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var ntsc in new[] { false, true })
        foreach (var ecs in new[] { false, true })
        foreach (var construction in new[] { "direct", "autoinit", "hunk", "raster-hunk" })
        foreach (var scenario in new[] { "success", "database-oom", "monitor-oom", "monitor-odd", "damaged-database", "short-base" })
            yield return new object[] { ntsc, ecs, construction, scenario };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void NativeCompositionPublishesBothOrUnwindsInReverseOrder(
        bool ntsc, bool ecs, string construction, string scenario)
    {
        const uint graphics = 0x00600000, database = 0x00500000, monitor = 0x00510000,
            exec = 0x00700000, stack = 0x00780100, returned = 0x00780180;
        const int size = GraphicsLibraryImageLayout.NativeMonitorImageSize;
        var profile = ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal;
        var ownsLibrary = construction != "direct";
        var code = NativeGraphicsLibraryInitializer.BuildWithNativeMonitorSpec(size, profile, ecs, ownsLibrary);
        var databaseImage = GraphicsDisplayDatabase.CreateNativeDatabaseImage(ecs, ntsc);
        var monitorImage = GraphicsMonitorSpecImage.CreateNativeDefaultImage(ntsc);
        var bus = new AmigaBus();
        uint entry = construction == "direct" ? 0x00400000u : 0x00420000u;
        if (construction is "direct" or "autoinit") bus.MapWritableMemory(entry, code);
        else
        {
            var hunk = construction == "hunk"
                ? NativeGraphicsLibraryHunkBuilder.Build(new byte[] { 0x4E, 0x75 }, size, profile,
                    true, true, true, ecs, true, publishNativeMonitorSpec: true)
                : NativeGraphicsLibraryHunkBuilder.BuildFromRasterBodies(size, profile,
                    true, true, true, ecs, true, publishNativeMonitorSpec: true);
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
            var template = program.SegmentBases[0] + (uint)hunk.VectorOffset;
            Assert.Equal(0u, bus.ReadLong(template + 0x250));
            Assert.Equal(0u, bus.ReadLong(template + 0x264));
        }
        bus.MapWritableMemory(graphics - 4, Enumerable.Repeat((byte)0xA5, 0x288).ToArray());
        bus.WriteWord(graphics + 0x10, 0x444);
        bus.WriteWord(graphics + 0x12, scenario == "short-base" ? (ushort)0x264 : (ushort)0x280);
        var before = Read(graphics, 0x280);
        bus.MapWritableMemory(database, Enumerable.Repeat((byte)0xC7, databaseImage.Length + 4).ToArray());
        bus.MapWritableMemory(monitor, Enumerable.Repeat((byte)0xC7, monitorImage.Length + 4).ToArray());
        bus.MapWritableMemory(stack - 0x100, Enumerable.Repeat((byte)0x5A, 0x200).ToArray());
        bus.WriteLong(stack, returned);
        var allocations = 0;
        var frees = new List<uint>();
        bus.RegisterHostGateway(exec - 198, state =>
        {
            allocations++;
            Assert.Equal(exec, state.A[6]);
            Assert.Equal(0x10001u, state.D[1]);
            Assert.Equal(1u, bus.ReadLong(graphics + 0x254));
            Assert.Equal(2u, bus.ReadLong(graphics + 0x268));
            Assert.Equal(0u, bus.ReadLong(graphics + 0x264));
            if (allocations == 1)
            {
                Assert.Equal((uint)databaseImage.Length, state.D[0]);
                Assert.Equal(0u, bus.ReadLong(graphics + 0x250));
                state.D[0] = scenario == "database-oom" ? 0 : database;
            }
            else
            {
                Assert.Equal(2, allocations);
                Assert.Equal((uint)monitorImage.Length, state.D[0]);
                Assert.Equal(GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag, bus.ReadLong(graphics + 0x250));
                Assert.Equal(databaseImage, Read(database, databaseImage.Length));
                if (scenario == "damaged-database") bus.WriteLong(database, 0xBAD0BAD0);
                state.D[0] = scenario is "monitor-oom" or "damaged-database" ? 0
                    : scenario == "monitor-odd" ? monitor + 1 : monitor;
            }
            state.D[1] = state.A[0] = state.A[1] = 0xDEADBEEF;
        });
        bus.RegisterHostGateway(exec - 210, state =>
        {
            Assert.Equal(exec, state.A[6]);
            frees.Add(state.A[1]);
            if (state.A[1] == monitor + 1)
            {
                Assert.Equal("monitor-odd", scenario);
                Assert.Single(frees);
                Assert.Equal((uint)monitorImage.Length, state.D[0]);
                Assert.Equal(GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag, bus.ReadLong(graphics + 0x250));
            }
            else if (state.A[1] == database)
            {
                Assert.Equal((uint)databaseImage.Length, state.D[0]);
                Assert.Equal(0u, bus.ReadLong(graphics + 0x250));
                Assert.Equal(0u, bus.ReadLong(graphics + 0x25C));
                Assert.Equal(0u, bus.ReadLong(graphics + (uint)GraphicsLayouts.GfxBaseDisplayInfoDataBase));
            }
            else
            {
                Assert.True(ownsLibrary);
                Assert.Equal(graphics - 0x444, state.A[1]);
                Assert.Equal(0x444u + 0x280u, state.D[0]);
                Assert.Equal(0u, bus.ReadLong(graphics + 0x250));
                Assert.Equal(0u, bus.ReadLong(graphics + 0x264));
            }
            state.D[0] = state.D[1] = state.A[0] = state.A[1] = 0xDEADBEEF;
        });
        using var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
        cpu.Reset(entry, stack);
        cpu.State.D[0] = graphics;
        for (var i = 2; i < 8; i++) cpu.State.D[i] = 0xD0000000u + (uint)i;
        for (var i = 2; i < 6; i++) cpu.State.A[i] = 0xA0000000u + (uint)i;
        cpu.State.A[6] = exec;
        for (var i = 0; i < 10000 && cpu.State.ProgramCounter != returned; i++) cpu.ExecuteInstruction();
        Assert.Equal(returned, cpu.State.ProgramCounter);
        Assert.Equal(scenario == "success" ? graphics : 0, cpu.State.D[0]);
        Assert.Equal(scenario == "short-base" ? 0 : scenario == "database-oom" ? 1 : 2, allocations);
        var expectedFrees = new List<uint>();
        if (scenario == "monitor-odd") expectedFrees.Add(monitor + 1);
        if (scenario is "monitor-oom" or "monitor-odd") expectedFrees.Add(database);
        if (ownsLibrary && scenario is "database-oom" or "monitor-oom" or "monitor-odd") expectedFrees.Add(graphics - 0x444);
        Assert.Equal(expectedFrees, frees);
        Assert.Equal(stack + 4, cpu.State.A[7]);
        for (var i = 2; i < 8; i++) Assert.Equal(0xD0000000u + (uint)i, cpu.State.D[i]);
        for (var i = 2; i < 6; i++) Assert.Equal(0xA0000000u + (uint)i, cpu.State.A[i]);
        Assert.Equal(exec, cpu.State.A[6]);
        if (scenario == "short-base") Assert.Equal(before, Read(graphics, 0x280));
        else
        {
            var retainedDatabase = scenario is "success" or "damaged-database";
            Assert.Equal(retainedDatabase ? GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag : 0, bus.ReadLong(graphics + 0x250));
            Assert.Equal(retainedDatabase ? database : 0, bus.ReadLong(graphics + 0x25C));
            Assert.Equal(scenario == "success" ? GraphicsLibraryImageLayout.NativeMonitorDescriptorValidTag : 0, bus.ReadLong(graphics + 0x264));
            Assert.Equal(scenario == "success" ? monitor : 0, bus.ReadLong(graphics + 0x270));
            Assert.Equal(scenario == "success" ? ntsc ? 0x11000u : 0x21000u : 0, bus.ReadLong(graphics + 0x278));
            Assert.Equal(scenario == "success" ? monitor : graphics + 0x184, bus.ReadLong(graphics + 0x180));
            Assert.Equal(scenario == "success" ? monitor : 0, bus.ReadLong(graphics + 0x18E));
        }
        if (scenario == "success")
        {
            Assert.Equal(monitor, bus.ReadLong(database + (uint)GraphicsDisplayDatabase.NativeMonitorRegistrationsOffset + (ntsc ? 0u : 4u)));
            Assert.Equal(0u, bus.ReadLong(database + (uint)GraphicsDisplayDatabase.NativeMonitorRegistrationsOffset + (ntsc ? 4u : 0u)));
            Assert.Equal(graphics, bus.ReadLong(monitor + 0x10));
            Assert.Equal(graphics + 0x184, bus.ReadLong(monitor));
            Assert.Equal(graphics + 0x180, bus.ReadLong(monitor + 4));
            Assert.Equal((ushort)0, bus.ReadWord(monitor + (uint)GraphicsLayouts.MonitorSpecOpenCount));
        }
        else Assert.All(Read(monitor, monitorImage.Length), value => Assert.Equal((byte)0xC7, value));
        Assert.Equal(0xC7C7C7C7u, bus.ReadLong(database + (uint)databaseImage.Length));
        Assert.Equal(0xC7C7C7C7u, bus.ReadLong(monitor + (uint)monitorImage.Length));
        Assert.Equal(0xA5A5A5A5u, bus.ReadLong(graphics - 4));
        Assert.All(Read(graphics + size, 0x280 - size + 4), value => Assert.Equal((byte)0xA5, value));
        Assert.Equal(0x5A5A5A5Au, bus.ReadLong(stack - 36));

        byte[] Read(uint address, int count) => Enumerable.Range(0, count).Select(i => bus.ReadByte(address + (uint)i)).ToArray();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void MonitorPublicationRequiresBothExplicitOwnershipOptions(bool database, bool monitorDescriptor)
        => Assert.Throws<ArgumentException>(() => NativeGraphicsLibraryHunkBuilder.Build(new byte[] { 0x4E, 0x75 },
            GraphicsLibraryImageLayout.NativeMonitorImageSize, GraphicsLibraryImageProfile.NativePal,
            true, true, database, false, monitorDescriptor, publishNativeMonitorSpec: true));
}
