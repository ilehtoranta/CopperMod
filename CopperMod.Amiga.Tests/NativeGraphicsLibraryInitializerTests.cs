using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsLibraryInitializerTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void HunkResidentSelectsRelocatedInitializerWithoutChangingPublicEntries(bool ntsc, bool rasterBodies)
    {
        var profile = ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal;
        const int size = GraphicsLibraryImageLayout.NativeRuntimeImageSize;
        var initializer = NativeGraphicsLibraryInitializer.Build(size, profile, true);
        var hunk = rasterBodies
            ? NativeGraphicsLibraryHunkBuilder.BuildFromRasterBodies(size, profile, true, initializeAllocatedImage: true)
            : NativeGraphicsLibraryHunkBuilder.Build(new byte[] { 0x4E, 0x75 }, size, profile, true, initializeAllocatedImage: true);
        var bus = new AmigaBus();
        var addresses = new Queue<uint>(new[] { 0x00800000u, 0x00A00000u });
        var loader = new AmigaHunkProgramLoader(bus, bytes =>
        {
            var address = addresses.Dequeue();
            bus.MapWritableMemory(address, new byte[bytes]);
            return address;
        });
        var program = loader.Load(hunk.Bytes);
        var imageBase = program.SegmentBases[0] + (uint)hunk.VectorOffset;
        var initTable = bus.ReadLong(program.SegmentBases[1] + 0x16);
        var initAddress = bus.ReadLong(initTable + 12);
        var publicCode = program.SegmentBases[0] + (uint)hunk.NativeCodeOffset;
        Assert.True(initAddress > publicCode);
        Assert.Equal(initializer, Enumerable.Range(0, initializer.Length)
            .Select(i => bus.ReadByte(initAddress + (uint)i)));
        var functions = bus.ReadLong(initTable + 4);
        for (var slot = 0; slot < NativeGraphicsLibraryImageBuilder.VectorSlotCount; slot++)
        {
            var entry = bus.ReadLong(functions + (uint)slot * 4);
            Assert.InRange(entry, publicCode, initAddress - 2);
            Assert.Equal(entry, bus.ReadLong(imageBase - (uint)(slot + 1) * 6 + 2));
        }
        // Execute the actual relocated initializer, with a distinct allocation.
        // This does not replace the remaining real Exec InitResident test.
        var (_, result) = Execute(initializer, initAddress, 0x00600000, 0x280, bus);
        Assert.Equal(0x00600000u, result);
        Assert.Equal(0x00600220u, bus.ReadLong(0x0060000A));
        Assert.Equal(1u, bus.ReadLong(0x00600254));
        Assert.Equal(0u, bus.ReadLong(0x00600250));
        Assert.Equal(imageBase + 0x220, bus.ReadLong(imageBase + 0x0A));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void InitializerRebasesPositiveImageAndPreservesExecSizes(bool ntsc, bool descriptor)
    {
        foreach (var codeAddress in new uint[] { 0x00400000, 0x00800000 })
        {
            var profile = ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal;
            var size = descriptor ? GraphicsLibraryImageLayout.NativeRuntimeImageSize
                : GraphicsLibraryImageLayout.NativeMinimumImageSize;
            var code = NativeGraphicsLibraryInitializer.Build(size, profile, descriptor);
            var (bus, result) = Execute(code, codeAddress, 0x00600000, 0x280);
            Assert.Equal(0x00600000u, result);
            var expected = GraphicsLibraryImageLayout.CreateGuestImage(0x00600000, 0x280,
                0x444, 0x280, 40, 68, "graphics.library", "graphics.library 40.68", profile, descriptor);
            Assert.Equal(expected.Take(size), Enumerable.Range(0, size).Select(i => bus.ReadByte(0x00600000u + (uint)i)));
            Assert.All(Enumerable.Range(size, 0x280 - size).Select(i => bus.ReadByte(0x00600000u + (uint)i)), b => Assert.Equal((byte)0xA5, b));
        }
    }

    [Theory]
    [InlineData(0u, 0x280)]
    [InlineData(0x00600001u, 0x280)]
    [InlineData(0xFFFFFFFEu, 0x280)]
    [InlineData(0x00600000u, 0x24C)]
    public void InitializerRejectsInvalidAllocationBeforeWrites(uint suppliedBase, ushort positiveSize)
    {
        var code = NativeGraphicsLibraryInitializer.Build(GraphicsLibraryImageLayout.NativeRuntimeImageSize,
            GraphicsLibraryImageProfile.NativePal, true);
        var (bus, result) = Execute(code, 0x00400000, suppliedBase, positiveSize);
        Assert.Equal(0u, result);
        Assert.Equal(positiveSize, bus.ReadWord(0x00600012));
        Assert.Equal(0xA5A5A5A5u, bus.ReadLong(0x00600000));
        Assert.Equal(0xA5A5A5A5u, bus.ReadLong(0x00600250));
    }

    private static (AmigaBus Bus, uint Result) Execute(byte[] code, uint codeAddress, uint suppliedBase,
        ushort positiveSize, AmigaBus? preparedBus = null)
    {
        var bus = preparedBus ?? new AmigaBus();
        if (preparedBus == null) bus.MapWritableMemory(codeAddress, code);
        bus.MapWritableMemory(0x005FFFE0, Enumerable.Repeat((byte)0xA5, 0x2C0).ToArray());
        bus.WriteWord(0x00600010, 0x444);
        bus.WriteWord(0x00600012, positiveSize);
        bus.MapWritableMemory(0x00700000, Enumerable.Repeat((byte)0x5A, 0x200).ToArray());
        const uint stack = 0x00700100, returned = 0x00700180;
        bus.WriteLong(stack, returned);
        using var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
        cpu.Reset(codeAddress, stack);
        cpu.State.D[0] = suppliedBase;
        for (var i = 2; i < 8; i++) cpu.State.D[i] = 0xD0000000u + (uint)i;
        for (var i = 2; i < 7; i++) cpu.State.A[i] = 0xA0000000u + (uint)i;
        var returns = 0;
        for (var instruction = 0; instruction < 3000 && cpu.State.ProgramCounter != returned; instruction++)
        {
            Assert.InRange(cpu.State.ProgramCounter, codeAddress, codeAddress + (uint)code.Length - 2);
            if (bus.ReadWord(cpu.State.ProgramCounter) == 0x4E75) returns++;
            cpu.ExecuteInstruction();
        }
        Assert.Equal(returned, cpu.State.ProgramCounter);
        Assert.Equal(1, returns);
        Assert.Equal(stack + 4, cpu.State.A[7]);
        for (var i = 2; i < 8; i++) Assert.Equal(0xD0000000u + (uint)i, cpu.State.D[i]);
        for (var i = 2; i < 7; i++) Assert.Equal(0xA0000000u + (uint)i, cpu.State.A[i]);
        Assert.Equal(0x5A5A5A5Au, bus.ReadLong(stack - 8));
        Assert.Equal(0xA5A5A5A5u, bus.ReadLong(0x005FFFFC));
        Assert.Equal(0xA5A5A5A5u, bus.ReadLong(0x00600280));
        return (bus, cpu.State.D[0]);
    }
}
