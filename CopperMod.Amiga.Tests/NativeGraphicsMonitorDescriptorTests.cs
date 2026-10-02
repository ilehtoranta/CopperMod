using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsMonitorDescriptorTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MonitorDescriptorIsExplicitIndependentAndInitializedInActualAllocation(bool database, bool monitor)
    {
        foreach (var profile in new[] { GraphicsLibraryImageProfile.NativePal, GraphicsLibraryImageProfile.NativeNtsc })
        {
            const uint graphics = 0x00600000, codeAddress = 0x00400000, stack = 0x00700100, returned = 0x00700180;
            const int size = GraphicsLibraryImageLayout.NativeMonitorImageSize;
            var image = GraphicsLibraryImageLayout.CreateGuestImage(graphics, 0x2A0, 0x444, 0x2A0,
                40, 68, "graphics.library", "graphics.library 40.68", profile, database, monitor);
            Assert.Equal(0x264, GraphicsLibraryImageLayout.NativeRuntimeImageSize);
            Assert.Equal(0x27C, size);
            var bus = new AmigaBus();
            var code = NativeGraphicsLibraryInitializer.Build(size, profile, database, monitor);
            bus.MapWritableMemory(codeAddress, code);
            bus.MapWritableMemory(graphics, Enumerable.Repeat((byte)0xA5, 0x2A4).ToArray());
            bus.WriteWord(graphics + 0x10, 0x444);
            bus.WriteWord(graphics + 0x12, 0x2A0);
            bus.MapWritableMemory(stack - 0x100, new byte[0x200]);
            bus.WriteLong(stack, returned);
            using var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
            cpu.Reset(codeAddress, stack);
            cpu.State.D[0] = graphics;
            for (var instruction = 0; instruction < 3000 && cpu.State.ProgramCounter != returned; instruction++)
                cpu.ExecuteInstruction();
            Assert.Equal(returned, cpu.State.ProgramCounter);
            Assert.Equal(graphics, cpu.State.D[0]);
            Assert.Equal(image.Take(size), Enumerable.Range(0, size).Select(i => bus.ReadByte(graphics + (uint)i)));
            Assert.Equal(database ? 1u : 0, bus.ReadLong(graphics + 0x254));
            Assert.Equal(monitor ? 2u : 0, bus.ReadLong(graphics + 0x268));
            foreach (var offset in new uint[] { 0x250, 0x258, 0x25C, 0x260, 0x264, 0x26C, 0x270, 0x274, 0x278 })
                Assert.Equal(0u, bus.ReadLong(graphics + offset));
            Assert.All(Enumerable.Range(size, 0x2A4 - size).Select(i => bus.ReadByte(graphics + (uint)i)),
                value => Assert.Equal((byte)0xA5, value));
        }
    }

    [Fact]
    public void MonitorDescriptorRejectsCompactShortOrOverlappingImages()
    {
        Assert.Throws<ArgumentException>(() => Image(0x27C, 0x27C, GraphicsLibraryImageProfile.CompactHost));
        Assert.Throws<ArgumentException>(() => Image(0x278, 0x278, GraphicsLibraryImageProfile.NativePal));
        Assert.Throws<ArgumentException>(() => Image(0x264, 0x264, GraphicsLibraryImageProfile.NativePal));
        Assert.Throws<ArgumentException>(() => Image(0x27C, 0x278, GraphicsLibraryImageProfile.NativeNtsc));
        Assert.Throws<ArgumentException>(() => GraphicsLibraryImageLayout.CreateGuestImage(0x600000,
            0x27C, 0x420, 0x27C, 40, 68, "graphics.library", new string('x', 29),
            GraphicsLibraryImageProfile.NativePal, includeNativeMonitorDescriptor: true));

        static byte[] Image(int size, int reported, GraphicsLibraryImageProfile profile) =>
            GraphicsLibraryImageLayout.CreateGuestImage(0x600000, size, 0x420, reported,
                40, 68, "graphics.library", "graphics.library 40.68", profile, includeNativeMonitorDescriptor: true);
    }
}
