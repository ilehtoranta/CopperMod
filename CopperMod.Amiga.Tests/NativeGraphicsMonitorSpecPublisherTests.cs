using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsMonitorSpecPublisherTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var ntsc in new[] { false, true })
        foreach (var codeAddress in new[] { 0x00400000u, 0x00900000u })
        foreach (var scenario in new[] { "success", "word-return", "no-memory", "foreign-default",
            "head", "tail", "pred", "type", "pad", "tag", "version", "legacy-version", "owner", "allocation", "size", "id",
            "odd-return", "wrap-return", "changed-head", "changed-default", "changed-version", "changed-size", "changed-id",
            "null-base", "odd-base", "wrap-base", "short-base" })
            yield return new object[] { ntsc, codeAddress, scenario };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void NativeMonitorPublicationGuardsOwnershipAndPublishesCompleteNodeLast(bool ntsc, uint codeAddress, string scenario)
    {
        const uint graphics = 0x00600000, heap = 0x00500000, exec = 0x00700000,
            stack = 0x00800100, returned = 0x00800180;
        var bus = new AmigaBus();
        var code = NativeGraphicsMonitorSpecPublisher.Build(ntsc);
        var image = GraphicsMonitorSpecImage.CreateNativeDefaultImage(ntsc);
        var positive = GraphicsLibraryImageLayout.CreateGuestImage(graphics,
            GraphicsLibraryImageLayout.NativeMonitorImageSize, 0x420,
            GraphicsLibraryImageLayout.NativeMonitorImageSize, 40, 68,
            "graphics.library", "graphics.library 40.68",
            ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal,
            includeNativeRuntimeDescriptor: true, includeNativeMonitorDescriptor: true);
        bus.MapWritableMemory(codeAddress, code);
        bus.MapWritableMemory(graphics, positive);
        bus.MapWritableMemory(heap - 4, Enumerable.Repeat((byte)0xC7, image.Length + 24).ToArray());
        bus.MapWritableMemory(stack - 0x100, Enumerable.Repeat((byte)0x5A, 0x200).ToArray());
        bus.WriteLong(stack, returned);
        // CMDO and public display database belong to a different owner. The
        // monitor constructor must not read/adopt/overwrite this independent claim.
        for (uint offset = 0x250; offset < 0x264; offset += 4)
            bus.WriteLong(graphics + offset, 0xCAFE0000 + offset);
        bus.WriteLong(graphics + (uint)GraphicsLayouts.GfxBaseDisplayInfoDataBase, 0xDEADBEEF);
        var beforeOffset = scenario switch { "foreign-default" => 0x18E, "head" => 0x180,
            "tail" => 0x184, "pred" => 0x188, "tag" => 0x264, "version" => 0x268,
            "owner" => 0x26C, "allocation" => 0x270, "size" => 0x274, "id" => 0x278, _ => -1 };
        if (beforeOffset >= 0) bus.WriteLong(graphics + (uint)beforeOffset, 0xCAFE1234);
        if (scenario == "legacy-version") bus.WriteLong(graphics + 0x268, 1);
        if (scenario is "type" or "pad") bus.WriteByte(graphics + (scenario == "type" ? 0x18Cu : 0x18Du), 1, 0);
        if (scenario == "short-base") bus.WriteWord(graphics + 0x12, 0x264);
        var expectedLibrary = Read(graphics, positive.Length);
        var candidate = scenario switch { "no-memory" => 0u, "word-return" => heap + 2,
            "odd-return" => heap + 1, "wrap-return" => 0xFFFFFFFEu, _ => heap };
        var success = scenario is "success" or "word-return";
        if (success)
        {
            foreach (var offset in GraphicsMonitorSpecImage.NativeSelfPointerOffsets)
                ImageLong(offset, BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(offset, 4)) + candidate);
            ImageLong(GraphicsLayouts.ExtendedNodeLibrary, graphics);
            ImageLong(0, graphics + 0x184);
            ImageLong(4, graphics + 0x180);
        }
        var allocations = 0;
        var frees = 0;
        bus.RegisterHostGateway(exec - 198, state =>
        {
            allocations++;
            Assert.Equal(exec, state.A[6]);
            Assert.Equal((uint)image.Length, state.D[0]);
            Assert.Equal(0x10001u, state.D[1]);
            var changed = scenario switch { "changed-head" => 0x180, "changed-default" => 0x18E,
                "changed-version" => 0x268, "changed-id" => 0x278, _ => -1 };
            if (changed >= 0)
            {
                bus.WriteLong(graphics + (uint)changed, 0xCAFE1234);
                ExpectedLong(changed, 0xCAFE1234);
            }
            if (scenario == "changed-size")
            {
                bus.WriteWord(graphics + 0x12, 0x264);
                BinaryPrimitives.WriteUInt16BigEndian(expectedLibrary.AsSpan(0x12, 2), 0x264);
            }
            state.D[0] = candidate;
            state.D[1] = state.A[0] = state.A[1] = 0xDEADBEEF;
        });
        bus.RegisterHostGateway(exec - 210, state =>
        {
            frees++;
            Assert.Equal(exec, state.A[6]);
            Assert.Equal(candidate, state.A[1]);
            Assert.Equal((uint)image.Length, state.D[0]);
            Assert.Equal(expectedLibrary, Read(graphics, positive.Length));
            state.D[0] = state.D[1] = state.A[0] = state.A[1] = 0xDEADBEEF;
        });
        using var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
        cpu.Reset(codeAddress, stack);
        cpu.State.D[0] = scenario switch { "null-base" => 0, "odd-base" => graphics + 1,
            "wrap-base" => 0xFFFFFFFE, _ => graphics };
        for (var i = 2; i < 8; i++) cpu.State.D[i] = 0xD0000000u + (uint)i;
        for (var i = 2; i < 6; i++) cpu.State.A[i] = 0xA0000000u + (uint)i;
        cpu.State.A[6] = exec;
        var publications = 0;
        var commits = 0;
        for (var instruction = 0; instruction < 3000 && cpu.State.ProgramCounter != returned; instruction++)
        {
            var pc = cpu.State.ProgramCounter;
            if (pc >= codeAddress && pc < codeAddress + code.Length)
            {
                var opcode = bus.ReadWord(pc);
                var offset = bus.ReadWord(pc + 2);
                if (opcode == 0x2543 && offset == 0x180)
                {
                    publications++;
                    Assert.Equal(image, Read(candidate, image.Length));
                    Assert.Equal(0u, bus.ReadLong(graphics + 0x264));
                    Assert.Equal(graphics + 0x184, bus.ReadLong(graphics + 0x180));
                }
                if (opcode == 0x2540 && offset == 0x264)
                {
                    commits++;
                    Assert.Equal(image, Read(candidate, image.Length));
                    Assert.Equal(candidate, bus.ReadLong(graphics + 0x180));
                    Assert.Equal(candidate, bus.ReadLong(graphics + 0x188));
                    Assert.Equal(candidate, bus.ReadLong(graphics + 0x18E));
                    Assert.Equal(graphics, bus.ReadLong(graphics + 0x26C));
                    Assert.Equal(candidate, bus.ReadLong(graphics + 0x270));
                    Assert.Equal((uint)image.Length, bus.ReadLong(graphics + 0x274));
                    Assert.Equal(ntsc ? 0x11000u : 0x21000u, bus.ReadLong(graphics + 0x278));
                    Assert.Equal(0u, bus.ReadLong(graphics + 0x264));
                }
            }
            cpu.ExecuteInstruction();
        }
        var allocated = success || scenario is "no-memory" or "odd-return" or "wrap-return" or
            "changed-head" or "changed-default" or "changed-version" or "changed-size" or "changed-id";
        Assert.Equal(returned, cpu.State.ProgramCounter);
        Assert.Equal(success ? graphics : 0, cpu.State.D[0]);
        Assert.Equal(allocated ? 1 : 0, allocations);
        Assert.Equal(allocated && !success && scenario != "no-memory" ? 1 : 0, frees);
        Assert.Equal(success ? 1 : 0, publications);
        Assert.Equal(success ? 1 : 0, commits);
        Assert.Equal(stack + 4, cpu.State.A[7]);
        for (var i = 2; i < 8; i++) Assert.Equal(0xD0000000u + (uint)i, cpu.State.D[i]);
        for (var i = 2; i < 6; i++) Assert.Equal(0xA0000000u + (uint)i, cpu.State.A[i]);
        Assert.Equal(exec, cpu.State.A[6]);
        var expectedHeap = Enumerable.Repeat((byte)0xC7, image.Length + 24).ToArray();
        if (success)
        {
            image.CopyTo(expectedHeap, (int)(candidate - heap) + 4);
            foreach (var offset in new[] { 0x180, 0x188, 0x18E, 0x270 }) ExpectedLong(offset, candidate);
            ExpectedLong(0x264, GraphicsLibraryImageLayout.NativeMonitorDescriptorValidTag);
            ExpectedLong(0x26C, graphics);
            ExpectedLong(0x274, (uint)image.Length);
            ExpectedLong(0x278, ntsc ? 0x11000u : 0x21000u);
        }
        Assert.Equal(expectedHeap, Read(heap - 4, expectedHeap.Length));
        Assert.Equal(expectedLibrary, Read(graphics, positive.Length));
        Assert.Equal(0x5A5A5A5Au, bus.ReadLong(stack - 24));

        byte[] Read(uint address, int count) => Enumerable.Range(0, count).Select(i => bus.ReadByte(address + (uint)i)).ToArray();
        void ExpectedLong(int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(expectedLibrary.AsSpan(offset, 4), value);
        void ImageLong(int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(offset, 4), value);
    }
}
