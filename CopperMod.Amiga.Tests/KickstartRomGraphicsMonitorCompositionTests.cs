using Amiga;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed partial class KickstartRomLayersDifferentialTests
{
    [GraphicsRomTheory]
    [InlineData(false, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 0)]
    [InlineData(true, true, 0)]
    [InlineData(false, false, 1)]
    [InlineData(false, true, 1)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 1)]
    [InlineData(false, false, 2)]
    [InlineData(false, true, 2)]
    [InlineData(true, false, 2)]
    [InlineData(true, true, 2)]
    public void NativeGraphicsMonitorAutoinitUsesOriginalExecAndUnwindsAllocationFailure(
        bool ntsc, bool relocated, int failedAllocation)
    {
        Assert.True(TryLoadConfiguredRom(out var rom));
        var context = CreateNativeIntuitionOracle(rom, suppressInputDeviceOverlay: true,
            ntsc: ntsc, ecs: false, chipRamBytes: 512 * 1024);
        try
        {
            Assert.NotEqual(0u, ContinueUntilNativeIntuitionPublished(context));
            RestoreOriginalExecForGraphicsOracle(context);
            var originalGraphics = FindLibrary(context.Bus, context.ExecBase, "graphics.library");
            AssertOriginalGraphicsRomEntries(context, originalGraphics, rom);
            CheckExecEntries();
            var originalList = ReadBytes(context.Bus, originalGraphics + 0x180, 18);
            var profile = ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal;
            const int size = GraphicsLibraryImageLayout.NativeMonitorImageSize;
            var initializer = NativeGraphicsLibraryInitializer.BuildWithNativeMonitorSpec(size, profile, false,
                releaseLibraryOnFailure: true);
            uint resident, template;
            if (relocated)
            {
                var hunk = NativeGraphicsLibraryHunkBuilder.BuildFromRasterBodies(size, profile,
                    includeNativeRuntimeDescriptor: true, initializeAllocatedImage: true,
                    publishNativeMonitorDatabase: true, includeNativeMonitorDescriptor: true,
                    publishNativeMonitorSpec: true);
                var addresses = new Queue<uint>(new[] { 0x00800000u, 0x00A80000u });
                var loader = new AmigaHunkProgramLoader(context.Bus, bytes =>
                {
                    var address = addresses.Dequeue();
                    context.Bus.MapWritableMemory(address, new byte[bytes]);
                    return address;
                });
                var program = loader.Load(hunk.Bytes);
                resident = program.SegmentBases[1];
                template = program.SegmentBases[0] + (uint)hunk.VectorOffset;
            }
            else
            {
                const uint codeAddress = 0x00400000;
                var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback, defaultMonitorNtsc: ntsc);
                var init = codeAddress + (uint)code.Length;
                context.Bus.MapWritableMemory(codeAddress, code.Concat(initializer).ToArray());
                var image = NativeGraphicsLibraryImageBuilder.Build(0x00700000, 0x00A80000, init,
                    entries.ToDictionary(pair => pair.Key, pair => codeAddress + (uint)pair.Value),
                    codeAddress + (uint)fallback, size, profile, true, true);
                context.Bus.MapWritableMemory(image.VectorBase, image.VectorBytes);
                context.Bus.MapWritableMemory(image.LibraryBase, image.PositiveBytes);
                context.Bus.MapWritableMemory(image.ResidentAddress, image.ResidentBytes);
                resident = image.ResidentAddress;
                template = image.LibraryBase;
            }
            var initializerAddress = context.Bus.ReadLong(context.Bus.ReadLong(resident + 0x16) + 12);
            Assert.Equal(initializer, ReadBytes(context.Bus, initializerAddress, initializer.Length));
            if (failedAllocation != 0)
            {
                // Inject OOM only in the emitted routine. Original Exec vectors,
                // MakeLibrary allocation and CPU implementation remain untouched.
                var calls = Enumerable.Range(0, initializer.Length - 3).Where(i => (i & 1) == 0 &&
                    initializer[i] == 0x4E && initializer[i + 1] == 0xAE &&
                    initializer[i + 2] == 0xFF && initializer[i + 3] == 0x3A).ToArray();
                Assert.Equal(2, calls.Length);
                var call = initializerAddress + (uint)calls[failedAllocation - 1];
                context.Bus.WriteWord(call, 0x7000);
                context.Bus.WriteWord(call + 2, 0x4E71);
            }
            var templateBefore = ReadBytes(context.Bus, template, size);
            var freeBefore = FreeBytes();
            var graphics = context.Invoke(context.ExecBase, (int)ExecLvo.InitResident, state =>
            {
                state.A[1] = resident;
                state.D[1] = 0;
            }).D[0];
            if (failedAllocation != 0)
            {
                Assert.Equal(0u, graphics);
                Assert.Equal(freeBefore, FreeBytes());
                Assert.Equal(originalGraphics, FindLibrary(context.Bus, context.ExecBase, "graphics.library"));
            }
            else
            {
                Assert.NotEqual(0u, graphics);
                Assert.NotEqual(template, graphics);
                Assert.NotEqual(originalGraphics, graphics);
                Assert.Equal(GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag, context.Bus.ReadLong(graphics + 0x250));
                Assert.Equal(graphics, context.Bus.ReadLong(graphics + 0x258));
                Assert.Equal(GraphicsLibraryImageLayout.NativeMonitorDescriptorValidTag, context.Bus.ReadLong(graphics + 0x264));
                Assert.Equal(graphics, context.Bus.ReadLong(graphics + 0x26C));
                var monitor = context.Bus.ReadLong(graphics + 0x270);
                Assert.NotEqual(0u, monitor);
                var database = context.Bus.ReadLong(graphics + 0x25C);
                Assert.Equal(monitor, context.Bus.ReadLong(database + (uint)GraphicsDisplayDatabase.NativeMonitorRegistrationsOffset + (ntsc ? 0u : 4u)));
                Assert.Equal(0u, context.Bus.ReadLong(database + (uint)GraphicsDisplayDatabase.NativeMonitorRegistrationsOffset + (ntsc ? 4u : 0u)));
                Assert.Equal(monitor, context.Bus.ReadLong(graphics + 0x18E));
                Assert.Equal(monitor, context.Bus.ReadLong(graphics + 0x180));
                Assert.Equal(monitor, context.Bus.ReadLong(graphics + 0x188));
                Assert.Equal(2u, context.Bus.ReadLong(graphics + 0x268));
                Assert.Equal(ntsc ? 0x11000u : 0x21000u, context.Bus.ReadLong(graphics + 0x278));
                Assert.Equal(graphics, context.Bus.ReadLong(monitor + (uint)GraphicsLayouts.ExtendedNodeLibrary));
                var freeBeforeIds = FreeBytes();
                foreach (var key in new uint[] { 0, 4, 0x8000, 0x8004, 0x8020, 0x8024, 0x800, 0x804,
                    0x80, 0x84, 0x400, 0x404, 0x8400, 0x8404, 0x8420, 0x8424, 0x440, 0x444,
                    0x8440, 0x8444, 0x8460, 0x8464 })
                foreach (var id in new[] { key, key | 0x1000u, key | (ntsc ? 0x11000u : 0x21000u) })
                {
                    Assert.Equal(monitor, context.Invoke(graphics, (int)GraphicsLvo.OpenMonitor,
                        state => state.D[0] = id).D[0]);
                    Assert.Equal((ushort)1, context.Bus.ReadWord(monitor + (uint)GraphicsLayouts.MonitorSpecOpenCount));
                    Assert.Equal(0u, context.Invoke(graphics, (int)GraphicsLvo.CloseMonitor,
                        state => state.A[0] = monitor).D[0]);
                    Assert.Equal((ushort)0, context.Bus.ReadWord(monitor + (uint)GraphicsLayouts.MonitorSpecOpenCount));
                }
                Assert.Equal(freeBeforeIds, FreeBytes());
                Assert.Equal(0u, context.Invoke(graphics, (int)GraphicsLvo.OpenMonitor,
                    state => state.D[0] = GraphicsModeIds.Invalid).D[0]);
                Assert.Equal((ushort)0, context.Bus.ReadWord(monitor + (uint)GraphicsLayouts.MonitorSpecOpenCount));
                Assert.True(FreeBytes() < freeBefore);
                var nameBuffer = context.Allocate(32);
                var freeBeforeNames = FreeBytes();
                foreach (var name in new[] { "default.monitor", "DEFAULT.MONITOR", "DeFaUlT.MoNiToR", ntsc ? "ntsc.monitor" : "pal.monitor" })
                foreach (var id in new[] { GraphicsModeIds.Invalid, 0xDEADBEEFu })
                {
                    for (var i = 0; i < name.Length; i++) context.Bus.WriteByte(nameBuffer + (uint)i, (byte)name[i], 0);
                    context.Bus.WriteByte(nameBuffer + (uint)name.Length, 0, 0);
                    Assert.Equal(monitor, context.Invoke(graphics, (int)GraphicsLvo.OpenMonitor, state =>
                    {
                        state.A[1] = nameBuffer;
                        state.D[0] = id;
                    }).D[0]);
                    Assert.Equal((ushort)1, context.Bus.ReadWord(monitor + (uint)GraphicsLayouts.MonitorSpecOpenCount));
                    Assert.Equal(0u, context.Invoke(graphics, (int)GraphicsLvo.CloseMonitor, state => state.A[0] = monitor).D[0]);
                    Assert.Equal((ushort)0, context.Bus.ReadWord(monitor + (uint)GraphicsLayouts.MonitorSpecOpenCount));
                }
                Assert.Equal(freeBeforeNames, FreeBytes());
                context.Free(nameBuffer, 32);
            }
            Assert.Equal(templateBefore, ReadBytes(context.Bus, template, size));
            Assert.Equal(originalList, ReadBytes(context.Bus, originalGraphics + 0x180, 18));
            AssertOriginalGraphicsRomEntries(context, originalGraphics, rom);
            CheckExecEntries();

            uint FreeBytes() => context.Invoke(context.ExecBase, (int)ExecLvo.AvailMem, state => state.D[1] = 0).D[0];
            void CheckExecEntries()
            {
                var mapped = ReadBytes(context.Bus, GraphicsProbeRomBase, rom.Length);
                foreach (var lvo in new[] { ExecLvo.InitResident, ExecLvo.MakeLibrary, ExecLvo.MakeFunctions,
                    ExecLvo.InitStruct, ExecLvo.AllocMem, ExecLvo.FreeMem, ExecLvo.AvailMem, ExecLvo.AddLibrary })
                {
                    var vector = unchecked(context.ExecBase + (uint)(int)lvo);
                    Assert.True(IsOriginalGraphicsRomEntry(context.Bus.ReadWord(vector),
                        context.Bus.ReadLong(vector + 2), rom, mapped), $"Exec {lvo} must remain original ROM.");
                }
            }
        }
        finally { context.Machine.Dispose(); }
    }
}
