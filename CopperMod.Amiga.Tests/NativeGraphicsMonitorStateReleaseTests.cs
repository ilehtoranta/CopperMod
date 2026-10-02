using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsMonitorStateReleaseTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var ecs in new[] { false, true })
        foreach (var scenario in new[] { "success", "new-owner-during-free", "foreign", "inert",
            "owner", "version", "size", "null-database", "odd-database", "word-database",
            "wrap-database", "magic", "db-version", "db-version-two", "db-size", "ntsc-id", "pal-id",
            "null-base", "odd-base", "wrap-base", "short-base" })
            yield return new object[] { ecs, scenario };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ReleaseInvalidatesBeforeFreeAndNeverChangesForeignOwnership(bool ecs, string scenario)
    {
        const uint graphics = 0x00600000, heap = 0x00500000, exec = 0x00700000,
            codeAddress = 0x00400000, stack = 0x00800100, returned = 0x00800180;
        var bus = new AmigaBus();
        var code = NativeGraphicsMonitorStateRelease.Build();
        var image = GraphicsDisplayDatabase.CreateNativeDatabaseImage(ecs);
        var positive = GraphicsLibraryImageLayout.CreateGuestImage(graphics,
            GraphicsLibraryImageLayout.NativeRuntimeImageSize, 0x420,
            GraphicsLibraryImageLayout.NativeRuntimeImageSize, 40, 68,
            "graphics.library", "graphics.library 40.68", GraphicsLibraryImageProfile.NativePal, true);
        bus.MapWritableMemory(codeAddress, code);
        bus.MapWritableMemory(graphics, positive);
        bus.MapWritableMemory(heap, image.Concat(new byte[] { 0xC7, 0xC7, 0xC7, 0xC7 }).ToArray());
        bus.MapWritableMemory(stack - 0x100, Enumerable.Repeat((byte)0x5A, 0x200).ToArray());
        bus.WriteLong(stack, returned);
        var publicOffset = GraphicsLayouts.GfxBaseDisplayInfoDataBase;
        bus.WriteLong(graphics + 0x250, GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag);
        bus.WriteLong(graphics + 0x258, graphics);
        bus.WriteLong(graphics + 0x25C, heap);
        bus.WriteLong(graphics + 0x260, (uint)image.Length);
        bus.WriteLong(graphics + (uint)publicOffset, heap);
        switch (scenario)
        {
            case "foreign": bus.WriteLong(graphics + (uint)publicOffset, 0xCAFE1234); break;
            case "inert": bus.WriteLong(graphics + 0x250, 0); break;
            case "owner": bus.WriteLong(graphics + 0x258, 0xCAFE1234); break;
            case "version": bus.WriteLong(graphics + 0x254, 2); break;
            case "size": bus.WriteLong(graphics + 0x260, 4); break;
            case "short-base": bus.WriteWord(graphics + 0x12, 0x24C); break;
            case "magic": bus.WriteLong(heap, 0); break;
            case "db-version": bus.WriteLong(heap + 4, 1); break;
            case "db-version-two": bus.WriteLong(heap + 4, 2); break;
            case "db-size": bus.WriteLong(heap + 8, 4); break;
            case "ntsc-id": bus.WriteLong(heap + (uint)GraphicsDisplayDatabase.NativeMonitorPositionsOffset, 0); break;
            case "pal-id": bus.WriteLong(heap + (uint)(GraphicsDisplayDatabase.NativeMonitorPositionsOffset + 12), 0); break;
        }
        if (scenario is "null-database" or "odd-database" or "word-database" or "wrap-database")
        {
            var pointer = scenario switch { "null-database" => 0u, "odd-database" => heap + 1,
                "word-database" => heap + 2, _ => 0xFFFFFFFCu };
            bus.WriteLong(graphics + 0x25C, pointer);
            bus.WriteLong(graphics + (uint)publicOffset, pointer);
        }
        var expectedLibrary = Read(graphics, positive.Length);
        var expectedDatabase = Read(heap, image.Length + 4);
        var frees = 0;
        bus.RegisterHostGateway(exec - 210, state =>
        {
            frees++;
            Assert.Equal(exec, state.A[6]);
            Assert.Equal(heap, state.A[1]);
            Assert.Equal((uint)image.Length, state.D[0]);
            foreach (var offset in new[] { 0x250, 0x258, 0x25C, 0x260, publicOffset })
            {
                Assert.Equal(0u, bus.ReadLong(graphics + (uint)offset));
                ExpectLong(offset, 0);
            }
            Assert.Equal(1u, bus.ReadLong(graphics + 0x254));
            if (scenario == "new-owner-during-free")
            {
                foreach (var offset in new[] { 0x250, 0x254, 0x258, 0x25C, 0x260, publicOffset })
                {
                    bus.WriteLong(graphics + (uint)offset, 0xCAFE1234);
                    ExpectLong(offset, 0xCAFE1234);
                }
            }
            state.D[0] = state.D[1] = 0xDEADBEEF;
            state.A[0] = state.A[1] = 0xCAFEBABE;
        });
        using var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
        cpu.Reset(codeAddress, stack);
        cpu.State.D[0] = scenario switch { "null-base" => 0, "odd-base" => graphics + 1,
            "wrap-base" => 0xFFFFFFFE, _ => graphics };
        for (var i = 2; i < 8; i++) cpu.State.D[i] = 0xD0000000u + (uint)i;
        for (var i = 2; i < 6; i++) cpu.State.A[i] = 0xA0000000u + (uint)i;
        cpu.State.A[6] = exec;
        var invalidations = 0;
        for (var instruction = 0; instruction < 1000 && cpu.State.ProgramCounter != returned; instruction++)
        {
            var pc = cpu.State.ProgramCounter;
            if (pc >= codeAddress && pc < codeAddress + code.Length && bus.ReadWord(pc) == 0x42AA)
            {
                if (invalidations++ == 0)
                {
                    Assert.Equal((ushort)0x250, bus.ReadWord(pc + 2));
                    Assert.Equal(expectedLibrary, Read(graphics, positive.Length));
                }
            }
            cpu.ExecuteInstruction();
        }
        var success = scenario is "success" or "new-owner-during-free";
        Assert.Equal(returned, cpu.State.ProgramCounter);
        Assert.Equal(success ? graphics : 0, cpu.State.D[0]);
        Assert.Equal(success ? 1 : 0, frees);
        Assert.Equal(success ? 5 : 0, invalidations);
        Assert.Equal(stack + 4, cpu.State.A[7]);
        for (var i = 2; i < 8; i++) Assert.Equal(0xD0000000u + (uint)i, cpu.State.D[i]);
        for (var i = 2; i < 6; i++) Assert.Equal(0xA0000000u + (uint)i, cpu.State.A[i]);
        Assert.Equal(exec, cpu.State.A[6]);
        Assert.Equal(expectedLibrary, Read(graphics, positive.Length));
        Assert.Equal(expectedDatabase, Read(heap, image.Length + 4));
        Assert.Equal(0x5A5A5A5Au, bus.ReadLong(stack - 20));

        byte[] Read(uint address, int count) => Enumerable.Range(0, count).Select(i => bus.ReadByte(address + (uint)i)).ToArray();
        void ExpectLong(int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(expectedLibrary.AsSpan(offset, 4), value);
    }

    [Fact]
    public void MutationEnabledReleaseUsesTheExtendedDatabaseExtent()
    {
        const uint graphics = 0x00600000, heap = 0x00500000, exec = 0x00700000,
            codeAddress = 0x00400000, stack = 0x00800100, returned = 0x00800180;
        var bus = new AmigaBus();
        var code = NativeGraphicsMonitorStateRelease.Build(includeMonitorMutation: true);
        var image = GraphicsDisplayDatabase.CreateNativeDatabaseImage(
            supportsEcsDisplay: false, defaultMonitorNtsc: false, includeMonitorMutation: true);
        var positive = GraphicsLibraryImageLayout.CreateGuestImage(graphics,
            GraphicsLibraryImageLayout.NativeRuntimeImageSize, 0x420,
            GraphicsLibraryImageLayout.NativeRuntimeImageSize, 40, 68,
            "graphics.library", "graphics.library 40.68", GraphicsLibraryImageProfile.NativePal, true);
        bus.MapWritableMemory(codeAddress, code);
        bus.MapWritableMemory(graphics, positive);
        bus.MapWritableMemory(heap, image.Concat(new byte[] { 0xC7, 0xC7, 0xC7, 0xC7 }).ToArray());
        bus.MapWritableMemory(stack - 0x100, Enumerable.Repeat((byte)0x5A, 0x200).ToArray());
        bus.WriteLong(stack, returned);
        var publicOffset = GraphicsLayouts.GfxBaseDisplayInfoDataBase;
        bus.WriteLong(graphics + 0x250, GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag);
        bus.WriteLong(graphics + 0x254, GraphicsLibraryImageLayout.NativeRuntimeDescriptorCurrentVersion);
        bus.WriteLong(graphics + 0x258, graphics);
        bus.WriteLong(graphics + 0x25C, heap);
        bus.WriteLong(graphics + 0x260, (uint)image.Length);
        bus.WriteLong(graphics + (uint)publicOffset, heap);
        var expectedLibrary = Read(graphics, positive.Length);
        foreach (var offset in new[] { 0x250, 0x258, 0x25C, 0x260, publicOffset })
            BinaryPrimitives.WriteUInt32BigEndian(expectedLibrary.AsSpan(offset, 4), 0);
        var expectedDatabase = Read(heap, image.Length + 4);
        var frees = 0;
        bus.RegisterHostGateway(exec - 210, state =>
        {
            frees++;
            Assert.Equal(exec, state.A[6]);
            Assert.Equal(heap, state.A[1]);
            Assert.Equal((uint)image.Length, state.D[0]);
            foreach (var offset in new[] { 0x250, 0x258, 0x25C, 0x260, publicOffset })
                Assert.Equal(0u, bus.ReadLong(graphics + (uint)offset));
            Assert.Equal(1u, bus.ReadLong(graphics + 0x254));
            state.D[0] = state.D[1] = 0xDEADBEEF;
            state.A[0] = state.A[1] = 0xCAFEBABE;
        });
        using var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
        cpu.Reset(codeAddress, stack);
        cpu.State.D[0] = graphics;
        cpu.State.A[6] = exec;
        for (var instruction = 0; instruction < 1000 && cpu.State.ProgramCounter != returned; instruction++)
            cpu.ExecuteInstruction();

        Assert.Equal(returned, cpu.State.ProgramCounter);
        Assert.Equal(graphics, cpu.State.D[0]);
        Assert.Equal(1, frees);
        Assert.Equal(expectedLibrary, Read(graphics, positive.Length));
        Assert.Equal(expectedDatabase, Read(heap, image.Length + 4));
        Assert.Equal(stack + 4, cpu.State.A[7]);

        byte[] Read(uint address, int count) =>
            Enumerable.Range(0, count).Select(i => bus.ReadByte(address + (uint)i)).ToArray();
    }
}
