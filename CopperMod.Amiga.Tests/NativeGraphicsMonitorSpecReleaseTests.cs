using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsMonitorSpecReleaseTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var ntsc in new[] { false, true })
        foreach (var codeAddress in new[] { 0x00400000u, 0x00900000u })
        foreach (var scenario in new[] { "success", "word-allocation", "new-owner-during-free",
            "null-base", "odd-base", "wrap-base", "short-base", "tag", "version", "legacy-version", "owner", "size", "id",
            "null-allocation", "odd-allocation", "wrap-allocation", "default", "head", "tailpred", "tail", "type", "pad",
            "active-view", "current-monitor", "open-count", "backlink", "succ", "pred", "node-type", "node-kind", "flags",
            "name-pointer", "name-bytes", "display-head", "display-tail", "display-pred", "display-type", "sem-node",
            "sem-nest", "sem-owner", "sem-queue", "sem-head", "sem-tail", "sem-pred", "sem-multiple",
            "init", "special", "transform", "translate", "scale", "maxoscan", "videoscan", "compatible", "merge", "load", "kill" })
            yield return new object[] { ntsc, codeAddress, scenario };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ReleaseRequiresQuiescentExactOwnerAndUnlinksBeforeFree(bool ntsc, uint codeAddress, string scenario)
    {
        const uint graphics = 0x00600000, heap = 0x00500000, exec = 0x00700000,
            stack = 0x00800100, returned = 0x00800180;
        var monitor = scenario == "word-allocation" ? heap + 2 : heap;
        var bus = new AmigaBus();
        var code = NativeGraphicsMonitorSpecRelease.Build(ntsc);
        var image = GraphicsMonitorSpecImage.CreateNativeDefaultImage(ntsc);
        foreach (var offset in GraphicsMonitorSpecImage.NativeSelfPointerOffsets)
            ImageLong(offset, BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(offset, 4)) + monitor);
        ImageLong(0, graphics + 0x184);
        ImageLong(4, graphics + 0x180);
        ImageLong(GraphicsLayouts.ExtendedNodeLibrary, graphics);
        var heapBytes = Enumerable.Repeat((byte)0xC7, image.Length + 24).ToArray();
        image.CopyTo(heapBytes, (int)(monitor - heap) + 4);
        var positive = GraphicsLibraryImageLayout.CreateGuestImage(graphics,
            GraphicsLibraryImageLayout.NativeMonitorImageSize, 0x420,
            GraphicsLibraryImageLayout.NativeMonitorImageSize, 40, 68,
            "graphics.library", "graphics.library 40.68",
            ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal, true, true);
        bus.MapWritableMemory(codeAddress, code);
        bus.MapWritableMemory(graphics, positive);
        bus.MapWritableMemory(heap - 4, heapBytes);
        bus.MapWritableMemory(stack - 0x100, Enumerable.Repeat((byte)0x5A, 0x200).ToArray());
        bus.WriteLong(stack, returned);
        for (uint offset = 0x250; offset < 0x264; offset += 4) bus.WriteLong(graphics + offset, 0xCAFE0000 + offset);
        bus.WriteLong(graphics + (uint)GraphicsLayouts.GfxBaseDisplayInfoDataBase, 0xDEADBEEF);
        foreach (var offset in new uint[] { 0x180, 0x188, 0x18E, 0x270 }) bus.WriteLong(graphics + offset, monitor);
        bus.WriteLong(graphics + 0x264, GraphicsLibraryImageLayout.NativeMonitorDescriptorValidTag);
        bus.WriteLong(graphics + 0x26C, graphics);
        bus.WriteLong(graphics + 0x274, (uint)image.Length);
        bus.WriteLong(graphics + 0x278, ntsc ? 0x11000u : 0x21000u);
        if (scenario == "legacy-version") bus.WriteLong(graphics + 0x268, 1);
        var libraryOffset = scenario switch { "short-base" => 0x12, "tag" => 0x264, "version" => 0x268,
            "owner" => 0x26C, "size" => 0x274, "id" => 0x278, "default" => 0x18E, "head" => 0x180, "tailpred" => 0x188,
            "tail" => 0x184, "type" => 0x18C, "pad" => 0x18D, "active-view" => 0x22, "current-monitor" => 0x17C, _ => -1 };
        if (libraryOffset >= 0)
        {
            if (scenario == "short-base") bus.WriteWord(graphics + 0x12, 0x264);
            else if (scenario is "type" or "pad") bus.WriteByte(graphics + (uint)libraryOffset, 1, 0);
            else bus.WriteLong(graphics + (uint)libraryOffset, 0xCAFE1234);
        }
        if (scenario is "null-allocation" or "odd-allocation" or "wrap-allocation")
        {
            var bad = scenario switch { "null-allocation" => 0u, "odd-allocation" => heap + 1, _ => 0xFFFFFFFEu };
            foreach (var offset in new uint[] { 0x180, 0x188, 0x18E, 0x270 }) bus.WriteLong(graphics + offset, bad);
        }
        var nodeOffset = scenario switch
        {
            "open-count" => GraphicsLayouts.MonitorSpecOpenCount, "backlink" => GraphicsLayouts.ExtendedNodeLibrary,
            "succ" => 0, "pred" => 4, "node-type" => GraphicsLayouts.MonitorSpecNodeType,
            "node-kind" => GraphicsLayouts.MonitorSpecNodeSubsystem, "flags" => GraphicsLayouts.MonitorSpecFlags,
            "name-pointer" => GraphicsLayouts.MonitorSpecNodeName, "name-bytes" => GraphicsLayouts.MonitorSpecSize,
            "display-head" => GraphicsLayouts.MonitorSpecDisplayInfoDataBaseHead,
            "display-tail" => GraphicsLayouts.MonitorSpecDisplayInfoDataBaseTail,
            "display-pred" => GraphicsLayouts.MonitorSpecDisplayInfoDataBaseTailPred,
            "display-type" => GraphicsLayouts.MonitorSpecDisplayInfoDataBaseType,
            "sem-node" => GraphicsLayouts.MonitorSpecDisplayInfoSemaphoreNodeType,
            "sem-nest" => GraphicsLayouts.MonitorSpecDisplayInfoSemaphoreNestCount,
            "sem-owner" => GraphicsLayouts.MonitorSpecDisplayInfoSemaphoreOwner,
            "sem-queue" => GraphicsLayouts.MonitorSpecDisplayInfoSemaphoreQueueCount,
            "sem-head" => GraphicsLayouts.MonitorSpecDisplayInfoSemaphoreWaitQueue,
            "sem-tail" => GraphicsLayouts.MonitorSpecDisplayInfoSemaphoreWaitQueue + 4,
            "sem-pred" => GraphicsLayouts.MonitorSpecDisplayInfoSemaphoreWaitQueue + 8,
            "sem-multiple" => GraphicsLayouts.MonitorSpecDisplayInfoSemaphore + 0x1C,
            "init" => GraphicsLayouts.ExtendedNodeInit, "special" => GraphicsLayouts.MonitorSpecSpecial, "transform" => GraphicsLayouts.MonitorSpecTransform,
            "translate" => GraphicsLayouts.MonitorSpecTranslate, "scale" => GraphicsLayouts.MonitorSpecScale,
            "maxoscan" => GraphicsLayouts.MonitorSpecMaxOScan, "videoscan" => GraphicsLayouts.MonitorSpecVideoScan,
            "compatible" => GraphicsLayouts.MonitorSpecDisplayCompatible, "merge" => GraphicsLayouts.MonitorSpecMergeCopper,
            "load" => GraphicsLayouts.MonitorSpecLoadView, "kill" => GraphicsLayouts.MonitorSpecKillView, _ => -1
        };
        if (nodeOffset >= 0)
        {
            if (scenario is "node-type" or "name-bytes" or "display-type" or "sem-node")
                bus.WriteByte(monitor + (uint)nodeOffset, 1, 0);
            else if (scenario is "open-count" or "node-kind" or "flags" or "sem-nest" or "sem-queue")
                bus.WriteWord(monitor + (uint)nodeOffset, 0xFFFF);
            else bus.WriteLong(monitor + (uint)nodeOffset, 0xCAFE1234);
            if (scenario == "sem-queue") bus.WriteWord(monitor + (uint)nodeOffset, 0);
        }
        var expectedLibrary = Read(graphics, positive.Length);
        var expectedHeap = Read(heap - 4, heapBytes.Length);
        var frees = 0;
        bus.RegisterHostGateway(exec - 210, state =>
        {
            frees++;
            Assert.Equal(exec, state.A[6]);
            Assert.Equal(monitor, state.A[1]);
            Assert.Equal((uint)image.Length, state.D[0]);
            foreach (var offset in new[] { 0x264, 0x26C, 0x270, 0x274, 0x278, 0x18E })
            {
                Assert.Equal(0u, bus.ReadLong(graphics + (uint)offset));
                ExpectedLong(offset, 0);
            }
            Assert.Equal(graphics + 0x184, bus.ReadLong(graphics + 0x180));
            Assert.Equal(graphics + 0x180, bus.ReadLong(graphics + 0x188));
            Assert.Equal(2u, bus.ReadLong(graphics + 0x268));
            ExpectedLong(0x180, graphics + 0x184);
            ExpectedLong(0x188, graphics + 0x180);
            if (scenario == "new-owner-during-free")
                foreach (var offset in new[] { 0x264, 0x268, 0x26C, 0x270, 0x274, 0x278, 0x180, 0x188, 0x18E })
                {
                    bus.WriteLong(graphics + (uint)offset, 0xCAFE1234);
                    ExpectedLong(offset, 0xCAFE1234);
                }
            state.D[0] = state.D[1] = state.A[0] = state.A[1] = 0xDEADBEEF;
        });
        using var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
        cpu.Reset(codeAddress, stack);
        cpu.State.D[0] = scenario switch { "null-base" => 0, "odd-base" => graphics + 1,
            "wrap-base" => 0xFFFFFFFE, _ => graphics };
        for (var i = 2; i < 8; i++) cpu.State.D[i] = 0xD0000000u + (uint)i;
        for (var i = 2; i < 6; i++) cpu.State.A[i] = 0xA0000000u + (uint)i;
        cpu.State.A[6] = exec;
        var writes = 0;
        for (var instruction = 0; instruction < 2000 && cpu.State.ProgramCounter != returned; instruction++)
        {
            var pc = cpu.State.ProgramCounter;
            if (pc >= codeAddress && pc < codeAddress + code.Length && bus.ReadWord(pc) is 0x42AA or 0x2549)
            {
                if (writes++ == 0)
                {
                    Assert.Equal((ushort)0x264, bus.ReadWord(pc + 2));
                    Assert.Equal(expectedLibrary, Read(graphics, positive.Length));
                }
            }
            cpu.ExecuteInstruction();
        }
        var success = scenario is "success" or "word-allocation" or "new-owner-during-free";
        Assert.Equal(returned, cpu.State.ProgramCounter);
        Assert.Equal(success ? graphics : 0, cpu.State.D[0]);
        Assert.Equal(success ? 1 : 0, frees);
        Assert.Equal(success ? 8 : 0, writes);
        Assert.Equal(stack + 4, cpu.State.A[7]);
        for (var i = 2; i < 8; i++) Assert.Equal(0xD0000000u + (uint)i, cpu.State.D[i]);
        for (var i = 2; i < 6; i++) Assert.Equal(0xA0000000u + (uint)i, cpu.State.A[i]);
        Assert.Equal(exec, cpu.State.A[6]);
        Assert.Equal(expectedLibrary, Read(graphics, positive.Length));
        Assert.Equal(expectedHeap, Read(heap - 4, heapBytes.Length));
        Assert.Equal(0x5A5A5A5Au, bus.ReadLong(stack - 20));

        byte[] Read(uint address, int count) => Enumerable.Range(0, count).Select(i => bus.ReadByte(address + (uint)i)).ToArray();
        void ExpectedLong(int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(expectedLibrary.AsSpan(offset, 4), value);
        void ImageLong(int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(offset, 4), value);
    }
}
