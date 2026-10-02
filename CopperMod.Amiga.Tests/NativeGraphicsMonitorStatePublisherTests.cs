using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsMonitorStatePublisherTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var ecs in new[] { false, true })
        foreach (var ntsc in new[] { false, true })
        foreach (var scenario in new[] { "success", "no-memory", "foreign", "tag", "owner",
            "version-zero", "version-two", "odd-return", "word-return", "wrap-return",
            "provider-change", "owner-change", "null-base", "odd-base", "wrap-base", "short-base" })
            yield return new object[] { ecs, ntsc, scenario };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void FreshPublisherPreservesOwnershipAndCommitsValidityLast(bool ecs, bool ntsc, string scenario)
    {
        const uint graphics = 0x00600000, heap = 0x00500000, exec = 0x00700000,
            codeAddress = 0x00400000, stack = 0x00800100, returned = 0x00800180;
        var bus = new AmigaBus();
        var code = NativeGraphicsMonitorStatePublisher.Build(ecs, ntsc);
        var image = GraphicsDisplayDatabase.CreateNativeDatabaseImage(ecs, ntsc);
        var positive = GraphicsLibraryImageLayout.CreateGuestImage(graphics,
            GraphicsLibraryImageLayout.NativeRuntimeImageSize, 0x420,
            GraphicsLibraryImageLayout.NativeRuntimeImageSize, 40, 68,
            "graphics.library", "graphics.library 40.68", GraphicsLibraryImageProfile.NativePal, true);
        bus.MapWritableMemory(codeAddress, code);
        bus.MapWritableMemory(graphics, positive);
        bus.MapWritableMemory(heap, Enumerable.Repeat((byte)0xC7, image.Length + 16).ToArray());
        bus.MapWritableMemory(stack - 0x100, Enumerable.Repeat((byte)0x5A, 0x200).ToArray());
        bus.WriteLong(stack, returned);
        var publicOffset = GraphicsLayouts.GfxBaseDisplayInfoDataBase;
        switch (scenario)
        {
            case "foreign": bus.WriteLong(graphics + (uint)publicOffset, 0xCAFE1234); break;
            case "tag": bus.WriteLong(graphics + 0x250, GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag); break;
            case "owner": bus.WriteLong(graphics + 0x258, graphics); break;
            case "version-zero": bus.WriteLong(graphics + 0x254, 0); break;
            case "version-two": bus.WriteLong(graphics + 0x254, 2); break;
            case "short-base": bus.WriteWord(graphics + 0x12, 0x24C); break;
        }
        var expectedLibrary = Read(graphics, positive.Length);
        var candidate = scenario switch { "no-memory" => 0u, "odd-return" => heap + 1,
            "word-return" => heap + 2, "wrap-return" => 0xFFFFFFFCu, _ => heap };
        var allocations = 0;
        var frees = new List<uint>();
        bus.RegisterHostGateway(exec - 198, state =>
        {
            Assert.Equal(exec, state.A[6]);
            Assert.Equal((uint)image.Length, state.D[0]);
            Assert.Equal(0x10001u, state.D[1]);
            allocations++;
            if (scenario is "provider-change" or "owner-change")
            {
                var offset = scenario == "provider-change" ? publicOffset : 0x258;
                bus.WriteLong(graphics + (uint)offset, 0xCAFE1234);
                ExpectLong(offset, 0xCAFE1234);
            }
            state.D[0] = candidate;
            state.D[1] = 0xDEADBEEF;
            state.A[0] = 0xA0A0A0A0;
            state.A[1] = 0xA1A1A1A1;
        });
        bus.RegisterHostGateway(exec - 210, state =>
        {
            Assert.Equal(exec, state.A[6]);
            Assert.Equal(candidate, state.A[1]);
            Assert.Equal((uint)image.Length, state.D[0]);
            frees.Add(state.A[1]);
            state.D[0] = 0xDEADBEEF;
            state.D[1] = 0xCAFEBABE;
            state.A[0] = 0xA0A0A0A0;
            state.A[1] = 0xA1A1A1A1;
        });
        using var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
        cpu.Reset(codeAddress, stack);
        cpu.State.D[0] = scenario switch { "null-base" => 0, "odd-base" => graphics + 1,
            "wrap-base" => 0xFFFFFFFE, _ => graphics };
        for (var i = 2; i < 8; i++) cpu.State.D[i] = 0xD0000000u + (uint)i;
        for (var i = 2; i < 6; i++) cpu.State.A[i] = 0xA0000000u + (uint)i;
        cpu.State.A[6] = exec;
        var commits = 0;
        for (var instruction = 0; instruction < 3000 && cpu.State.ProgramCounter != returned; instruction++)
        {
            var pc = cpu.State.ProgramCounter;
            if (pc >= codeAddress && pc < codeAddress + code.Length &&
                bus.ReadWord(pc) == 0x2540 && bus.ReadWord(pc + 2) == 0x250)
            {
                commits++;
                Assert.Equal(image, Read(heap, image.Length));
                Assert.Equal(graphics, bus.ReadLong(graphics + 0x258));
                Assert.Equal(heap, bus.ReadLong(graphics + 0x25C));
                Assert.Equal((uint)image.Length, bus.ReadLong(graphics + 0x260));
                Assert.Equal(heap, bus.ReadLong(graphics + (uint)publicOffset));
                Assert.Equal(0u, bus.ReadLong(graphics + 0x250));
            }
            cpu.ExecuteInstruction();
        }
        Assert.Equal(returned, cpu.State.ProgramCounter);
        Assert.Equal(stack + 4, cpu.State.A[7]);
        for (var i = 2; i < 8; i++) Assert.Equal(0xD0000000u + (uint)i, cpu.State.D[i]);
        for (var i = 2; i < 6; i++) Assert.Equal(0xA0000000u + (uint)i, cpu.State.A[i]);
        Assert.Equal(exec, cpu.State.A[6]);
        var allocated = scenario is "success" or "no-memory" or "odd-return" or "word-return" or
            "wrap-return" or "provider-change" or "owner-change";
        Assert.Equal(allocated ? 1 : 0, allocations);
        Assert.Equal(allocated && scenario is not ("success" or "no-memory") ? 1 : 0, frees.Count);
        Assert.Equal(scenario == "success" ? graphics : 0, cpu.State.D[0]);
        Assert.Equal(scenario == "success" ? 1 : 0, commits);
        if (scenario == "success")
        {
            ExpectLong(0x250, GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag);
            ExpectLong(0x258, graphics);
            ExpectLong(0x25C, heap);
            ExpectLong(0x260, (uint)image.Length);
            ExpectLong(publicOffset, heap);
            Assert.Equal(image, Read(heap, image.Length));
        }
        else Assert.All(Read(heap, image.Length), value => Assert.Equal((byte)0xC7, value));
        Assert.Equal(expectedLibrary, Read(graphics, positive.Length));
        Assert.Equal(0xC7C7C7C7u, bus.ReadLong(heap + (uint)image.Length));
        Assert.Equal(0x5A5A5A5Au, bus.ReadLong(stack - 20));

        byte[] Read(uint address, int count) => Enumerable.Range(0, count).Select(i => bus.ReadByte(address + (uint)i)).ToArray();
        void ExpectLong(int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(expectedLibrary.AsSpan(offset, 4), value);
    }
}
