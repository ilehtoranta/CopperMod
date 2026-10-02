using System.Buffers.Binary;
using System.Text;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsMonitorSpecInitializerTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var ntsc in new[] { false, true })
        foreach (var codeAddress in new[] { 0x00400000u, 0x00800000u })
        foreach (var scenario in new[] { "success", "null-monitor", "odd-monitor", "wrap-monitor",
            "short-size", "null-library", "odd-library", "wrap-library" })
            yield return new object[] { ntsc, codeAddress, scenario };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void NativeDefaultMonitorInitializesItsOwnPointersAndPreservesGuards(bool ntsc, uint codeAddress, string scenario)
    {
        const uint monitor = 0x00500000, graphics = 0x00600000, stack = 0x00700100, returned = 0x00700180;
        var image = GraphicsMonitorSpecImage.CreateNativeDefaultImage(ntsc);
        var code = NativeGraphicsMonitorSpecInitializer.Build(ntsc);
        var bus = new AmigaBus();
        bus.MapWritableMemory(codeAddress, code);
        bus.MapWritableMemory(monitor - 4, Enumerable.Repeat((byte)0xA5, image.Length + 12).ToArray());
        bus.MapWritableMemory(stack - 0x100, Enumerable.Repeat((byte)0x5A, 0x200).ToArray());
        bus.WriteLong(stack, returned);
        using var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
        cpu.Reset(codeAddress, stack);
        cpu.State.D[0] = scenario switch { "null-monitor" => 0, "odd-monitor" => monitor + 1,
            "wrap-monitor" => 0xFFFFFFFE, _ => monitor };
        cpu.State.D[1] = (uint)(image.Length + (scenario == "short-size" ? -1 : 8));
        cpu.State.A[0] = scenario switch { "null-library" => 0, "odd-library" => graphics + 1,
            "wrap-library" => 0xFFFFFFFE, _ => graphics };
        for (var i = 2; i < 8; i++) cpu.State.D[i] = 0xD0000000u + (uint)i;
        for (var i = 2; i < 7; i++) cpu.State.A[i] = 0xA0000000u + (uint)i;
        for (var instruction = 0; instruction < 2000 && cpu.State.ProgramCounter != returned; instruction++)
            cpu.ExecuteInstruction();
        Assert.Equal(returned, cpu.State.ProgramCounter);
        Assert.Equal(scenario == "success" ? monitor : 0, cpu.State.D[0]);
        Assert.Equal(stack + 4, cpu.State.A[7]);
        for (var i = 2; i < 8; i++) Assert.Equal(0xD0000000u + (uint)i, cpu.State.D[i]);
        for (var i = 2; i < 7; i++) Assert.Equal(0xA0000000u + (uint)i, cpu.State.A[i]);
        if (scenario == "success")
        {
            foreach (var offset in GraphicsMonitorSpecImage.NativeSelfPointerOffsets)
                Put(offset, BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(offset, 4)) + monitor);
            Put(GraphicsLayouts.ExtendedNodeLibrary, graphics);
            Assert.Equal(image, Read(monitor, image.Length));
            Assert.Equal(ushort.MaxValue, bus.ReadWord(monitor + (uint)GraphicsLayouts.MonitorSpecDisplayInfoSemaphoreQueueCount));
            Assert.Equal((ushort)0, bus.ReadWord(monitor + (uint)GraphicsLayouts.MonitorSpecOpenCount));
            Assert.Equal(ntsc ? (ushort)262 : (ushort)312, bus.ReadWord(monitor + (uint)GraphicsLayouts.MonitorSpecTotalRows));
            Assert.Equal(Encoding.ASCII.GetBytes(ntsc ? "ntsc.monitor\0" : "pal.monitor\0"),
                Read(monitor + GraphicsLayouts.MonitorSpecSize, ntsc ? 13 : 12));
            foreach (var offset in new[] { GraphicsLayouts.MonitorSpecTransform, GraphicsLayouts.MonitorSpecTranslate,
                GraphicsLayouts.MonitorSpecScale, GraphicsLayouts.MonitorSpecMergeCopper,
                GraphicsLayouts.MonitorSpecLoadView, GraphicsLayouts.MonitorSpecKillView })
                Assert.Equal(0u, bus.ReadLong(monitor + (uint)offset));
        }
        else Assert.All(Read(monitor, image.Length), value => Assert.Equal((byte)0xA5, value));
        Assert.Equal(0xA5A5A5A5u, bus.ReadLong(monitor - 4));
        Assert.Equal(0xA5A5A5A5u, bus.ReadLong(monitor + (uint)image.Length));
        Assert.Equal(0x5A5A5A5Au, bus.ReadLong(stack - 8));

        byte[] Read(uint address, int count) => Enumerable.Range(0, count).Select(i => bus.ReadByte(address + (uint)i)).ToArray();
        void Put(int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(offset, 4), value);
    }
}
