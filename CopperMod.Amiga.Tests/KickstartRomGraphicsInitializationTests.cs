using Amiga;
using System.Reflection;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed partial class KickstartRomLayersDifferentialTests
{
    [GraphicsRomTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, true)]
    [InlineData(false, false, false, false, true)]
    [InlineData(false, true, false, false, true)]
    [InlineData(true, false, false, false, true)]
    [InlineData(true, true, false, false, true)]
    [InlineData(false, false, false, false, true, true)]
    [InlineData(false, true, false, false, true, true)]
    [InlineData(true, false, false, false, true, true)]
    [InlineData(true, true, false, false, true, true)]
    public void NativeGraphicsResidentInitializesNewAllocationThroughOriginalExec(
        bool ntsc, bool relocated, bool publishMonitorState = false, bool nativePublisher = false,
        bool initializeMonitor = false, bool failMonitorAllocation = false)
    {
        Assert.True(TryLoadConfiguredRom(out var rom));
        var context = CreateNativeIntuitionOracle(rom, suppressInputDeviceOverlay: true,
            ntsc: ntsc, ecs: false, chipRamBytes: 512 * 1024);
        try
        {
            Assert.NotEqual(0u, ContinueUntilNativeIntuitionPublished(context));
            // The normal boot fixture overlays mature Exec memory services.
            // Restore their saved vectors only in this disposable oracle; do
            // not change production takeover policy or weaken ROM provenance.
            RestoreOriginalExecForGraphicsOracle(context);
            var originalGraphics = FindLibrary(context.Bus, context.ExecBase, "graphics.library");
            Assert.NotEqual(0u, originalGraphics);
            AssertOriginalGraphicsRomEntries(context, originalGraphics, rom);
            CheckExecEntries();
            var profile = ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal;
            const int size = GraphicsLibraryImageLayout.NativeRuntimeImageSize;
            uint resident, templateBase;
            if (relocated)
            {
                var hunk = NativeGraphicsLibraryHunkBuilder.BuildFromRasterBodies(size, profile,
                    includeNativeRuntimeDescriptor: true, initializeAllocatedImage: true,
                    publishNativeMonitorDatabase: initializeMonitor);
                var addresses = new Queue<uint>(new[] { 0x00800000u, 0x00A80000u });
                var loader = new AmigaHunkProgramLoader(context.Bus, bytes =>
                {
                    var address = addresses.Dequeue();
                    context.Bus.MapWritableMemory(address, new byte[bytes]);
                    return address;
                });
                var program = loader.Load(hunk.Bytes);
                resident = program.SegmentBases[1];
                templateBase = program.SegmentBases[0] + (uint)hunk.VectorOffset;
            }
            else
            {
                const uint codeAddress = 0x00400000;
                var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback,
                    defaultMonitorNtsc: ntsc);
                var initializerAddress = codeAddress + (uint)code.Length;
                var initializer = initializeMonitor
                    ? NativeGraphicsLibraryInitializer.BuildWithNativeMonitorState(size, profile, false, releaseLibraryOnFailure: true)
                    : NativeGraphicsLibraryInitializer.Build(size, profile, true);
                context.Bus.MapWritableMemory(codeAddress, code.Concat(initializer).ToArray());
                var image = NativeGraphicsLibraryImageBuilder.Build(0x00700000, 0x00A80000,
                    initializerAddress, entries.ToDictionary(pair => pair.Key, pair => codeAddress + (uint)pair.Value),
                    codeAddress + (uint)fallback, size, profile, true);
                context.Bus.MapWritableMemory(image.VectorBase, image.VectorBytes);
                context.Bus.MapWritableMemory(image.LibraryBase, image.PositiveBytes);
                context.Bus.MapWritableMemory(image.ResidentAddress, image.ResidentBytes);
                resident = image.ResidentAddress;
                templateBase = image.LibraryBase;
            }

            uint availableBeforeFailure = 0;
            if (failMonitorAllocation)
            {
                // Deterministic test-only OOM injection in the emitted publisher,
                // not in any original Exec vector or CPU implementation. MakeLibrary
                // still allocates GfxBase normally; only CMDB AllocMem returns zero.
                var initializer = NativeGraphicsLibraryInitializer.BuildWithNativeMonitorState(size, profile, false,
                    releaseLibraryOnFailure: true);
                var allocationCalls = Enumerable.Range(0, initializer.Length - 3).Where(i => (i & 1) == 0 &&
                    initializer[i] == 0x4E && initializer[i + 1] == 0xAE &&
                    initializer[i + 2] == 0xFF && initializer[i + 3] == 0x3A).ToArray();
                var offset = Assert.Single(allocationCalls);
                var init = context.Bus.ReadLong(context.Bus.ReadLong(resident + 0x16) + 12);
                context.Bus.WriteWord(init + (uint)offset, 0x7000); // MOVEQ #0,D0
                context.Bus.WriteWord(init + (uint)offset + 2, 0x4E71); // NOP
                availableBeforeFailure = context.Invoke(context.ExecBase, (int)ExecLvo.AvailMem,
                    registers => registers.D[1] = 0).D[0];
            }
            var state = context.Invoke(context.ExecBase, (int)ExecLvo.InitResident, registers =>
            {
                registers.A[1] = resident;
                registers.D[1] = 0;
            });
            var graphics = state.D[0];
            if (failMonitorAllocation)
            {
                Assert.Equal(0u, graphics);
                Assert.Equal(availableBeforeFailure, context.Invoke(context.ExecBase, (int)ExecLvo.AvailMem,
                    registers => registers.D[1] = 0).D[0]);
                Assert.Equal(originalGraphics, FindLibrary(context.Bus, context.ExecBase, "graphics.library"));
                AssertOriginalGraphicsRomEntries(context, originalGraphics, rom);
                CheckExecEntries();
                return;
            }
            Assert.NotEqual(0u, graphics);
            Assert.NotEqual(templateBase, graphics);
            Assert.NotEqual(originalGraphics, graphics);
            Assert.Equal((byte)9, context.Bus.ReadByte(graphics + 8));
            Assert.Equal(graphics + 0x220, context.Bus.ReadLong(graphics + 0x0A));
            Assert.Equal(graphics + 0x234, context.Bus.ReadLong(graphics + 0x18));
            Assert.Equal((ushort)40, context.Bus.ReadWord(graphics + 0x14));
            Assert.Equal((ushort)68, context.Bus.ReadWord(graphics + 0x16));
            Assert.True(context.Bus.ReadWord(graphics + 0x10) >= NativeGraphicsLibraryImageBuilder.VectorTableSize);
            Assert.True(context.Bus.ReadWord(graphics + 0x12) >= size);
            Assert.Equal(ntsc ? (ushort)1 : (ushort)4,
                context.Bus.ReadWord(graphics + (uint)GraphicsLibraryImageLayout.GfxBaseDisplayFlags));
            foreach (var listOffset in new[] { GraphicsLibraryImageLayout.GfxBaseTextFonts, GraphicsLibraryImageLayout.GfxBaseMonitorList })
            {
                var list = graphics + (uint)listOffset;
                Assert.Equal(list + 4, context.Bus.ReadLong(list));
                Assert.Equal(0u, context.Bus.ReadLong(list + 4));
                Assert.Equal(list, context.Bus.ReadLong(list + 8));
            }
            if (!initializeMonitor)
                Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                    ReadBytes(context.Bus, graphics + 0x250, 20));
            var functionArray = context.Bus.ReadLong(context.Bus.ReadLong(resident + 0x16) + 4);
            for (var slot = 0; slot < NativeGraphicsLibraryImageBuilder.VectorSlotCount; slot++)
            {
                var vector = graphics - (uint)(slot + 1) * 6;
                Assert.Equal((ushort)0x4EF9, context.Bus.ReadWord(vector));
                Assert.Equal(context.Bus.ReadLong(functionArray + (uint)slot * 4), context.Bus.ReadLong(vector + 2));
            }
            Assert.NotEqual(0u, context.Bus.ReadLong(graphics));
            Assert.NotEqual(0u, context.Bus.ReadLong(graphics + 4));
            if (publishMonitorState)
                VerifyPublishedMonitorState(context, graphics, originalGraphics, templateBase, ntsc);
            if (nativePublisher || initializeMonitor)
                VerifyNativePublishedMonitorState(context, graphics, templateBase, ntsc, relocated, initializeMonitor);
            AssertOriginalGraphicsRomEntries(context, originalGraphics, rom);
            CheckExecEntries();
            _output.WriteLine($"native-graphics-init:ntsc={ntsc}:relocated={relocated}:resident={resident:X8}:template={templateBase:X8}:allocated={graphics:X8}");

            void CheckExecEntries()
            {
                var mapped = ReadBytes(context.Bus, GraphicsProbeRomBase, rom.Length);
                foreach (var lvo in new[] { ExecLvo.InitResident, ExecLvo.MakeLibrary,
                    ExecLvo.MakeFunctions, ExecLvo.InitStruct, ExecLvo.AllocMem, ExecLvo.FreeMem,
                    ExecLvo.AvailMem, ExecLvo.AddLibrary })
                {
                    var vector = unchecked(context.ExecBase + (uint)(int)lvo);
                    Assert.True(IsOriginalGraphicsRomEntry(context.Bus.ReadWord(vector),
                        context.Bus.ReadLong(vector + 2), rom, mapped),
                        $"Exec {lvo} is not an original ROM entry; this cannot qualify native initialization.");
                }
            }
        }
        finally { context.Machine.Dispose(); }
    }

    private static void RestoreOriginalExecForGraphicsOracle(OracleContext context)
    {
        const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        var runtime = typeof(AmigaBootController).GetField("_copperStartRuntime", privateInstance)!
            .GetValue(context.Boot)!;
        var overlay = runtime.GetType().GetField("_activeRomExec", privateInstance)!
            .GetValue(runtime) as IDisposable;
        Assert.NotNull(overlay);
        overlay.Dispose();
    }

    private static void VerifyNativePublishedMonitorState(OracleContext context, uint graphics,
        uint templateBase, bool ntsc, bool relocated, bool alreadyPublished = false)
    {
        // No GraphicsLibraryCore participates in CMDB ownership. The native
        // constructor calls the restored original Exec AllocMem directly.
        var codeAddress = relocated ? 0x00B80000u : 0x00600000u;
        var publisher = NativeGraphicsMonitorStatePublisher.Build(false, ntsc);
        var releaseAddress = codeAddress + (uint)publisher.Length;
        context.Bus.MapWritableMemory(codeAddress, publisher.Concat(NativeGraphicsMonitorStateRelease.Build()).ToArray());
        var templateBefore = ReadBytes(context.Bus, templateBase + 0x250, 20);
        if (!alreadyPublished) Assert.Equal(graphics, Publish());
        var database = context.Bus.ReadLong(graphics + 0x25C);
        Assert.NotEqual(0u, database);
        Assert.Equal(GraphicsDisplayDatabase.CreateNativeDatabaseImage(false, ntsc),
            ReadBytes(context.Bus, database, GraphicsDisplayDatabase.NativeDatabaseSize));
        Assert.Equal(GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag, context.Bus.ReadLong(graphics + 0x250));
        Assert.Equal(graphics, context.Bus.ReadLong(graphics + 0x258));
        Assert.Equal(database, context.Bus.ReadLong(graphics + (uint)GraphicsLayouts.GfxBaseDisplayInfoDataBase));
        var ownedDescriptor = ReadBytes(context.Bus, graphics + 0x250, 20);
        Assert.Equal(0u, Publish()); // A second constructor must not replace its live owner.
        Assert.Equal(ownedDescriptor, ReadBytes(context.Bus, graphics + 0x250, 20));

        // MonitorSpec lifecycle is a separate unit. Supply only the reader's
        // non-null pointer precondition; no claim of native OpenMonitor here.
        var monitor = context.Allocate(GraphicsLayouts.MonitorSpecSize);
        var output = context.Allocate(104);
        context.Bus.WriteLong(graphics + (uint)GraphicsLayouts.GfxBaseDefaultMonitor, monitor);
        foreach (var requested in new uint[] { 21, 22, 23, 24, 88, 96 })
        foreach (var odd in requested < 88 ? new[] { false, true } : new[] { false })
        {
            var bytes = Query(requested, odd);
            Assert.Equal(new byte[] { 0, 129, 0, 44 }.Take((int)Math.Min(requested - 20, 4)),
                bytes.Skip(20).Take(4));
            if (requested >= 88) Assert.Equal(new byte[] { 0, 129, 0, 44 }, bytes.Skip(80).Take(4));
        }
        var selected = database + (uint)(GraphicsDisplayDatabase.NativeMonitorPositionsOffset +
            (ntsc ? 0 : GraphicsDisplayDatabase.NativeMonitorPositionRecordSize));
        context.Bus.WriteLong(selected + 4, 0xFF8501C8);
        var changed = Query(88, false);
        Assert.Equal(new byte[] { 0xFF, 0x85, 1, 0xC8 }, changed.Skip(20).Take(4));
        Assert.Equal(new byte[] { 0, 129, 0, 44 }, changed.Skip(80).Take(4));
        Assert.Equal(templateBefore, ReadBytes(context.Bus, templateBase + 0x250, 20));

        Assert.Equal(graphics, Release());
        foreach (var offset in new[] { 0x250, 0x258, 0x25C, 0x260, GraphicsLayouts.GfxBaseDisplayInfoDataBase })
            Assert.Equal(0u, context.Bus.ReadLong(graphics + (uint)offset));
        Assert.Equal(1u, context.Bus.ReadLong(graphics + 0x254));
        Assert.All(Query(88, false), value => Assert.Equal((byte)0xA5, value));
        Assert.Equal(0u, Release()); // Do not double-free a now-inert descriptor.
        Assert.Equal(graphics, Publish());
        var recreated = Query(88, false);
        // Destruction/recreation resets boot state; this is not host rebind's
        // capture-and-preserve operation, nor an Intuition preference replay.
        Assert.Equal(new byte[] { 0, 129, 0, 44 }, recreated.Skip(20).Take(4));
        Assert.Equal(new byte[] { 0, 129, 0, 44 }, recreated.Skip(80).Take(4));
        Assert.Equal(graphics, Release());
        Assert.Equal(templateBefore, ReadBytes(context.Bus, templateBase + 0x250, 20));
        context.Bus.WriteLong(graphics + (uint)GraphicsLayouts.GfxBaseDefaultMonitor, 0);
        context.Free(monitor, GraphicsLayouts.MonitorSpecSize);
        context.Free(output, 104);

        uint Publish() => context.Invoke(codeAddress, 0, registers =>
        {
            registers.A[6] = context.ExecBase;
            registers.D[0] = graphics;
        }).D[0];

        uint Release() => context.Invoke(releaseAddress, 0, registers =>
        {
            registers.A[6] = context.ExecBase;
            registers.D[0] = graphics;
        }).D[0];

        byte[] Query(uint requested, bool odd)
        {
            for (uint i = 0; i < 104; i++) context.Bus.WriteByte(output + i, 0xA5, 0);
            var destination = output + (odd ? 3u : 2u);
            var result = context.Invoke(graphics, (int)GraphicsLvo.GetDisplayInfoData, registers =>
            {
                registers.A[1] = destination;
                registers.D[0] = requested;
                registers.D[1] = GraphicsDisplayDatabase.DtagMntr;
                registers.D[2] = 0;
            });
            var count = Math.Min(requested, 88);
            Assert.Equal(count, result.D[0]);
            Assert.Equal((byte)0xA5, context.Bus.ReadByte(destination - 1));
            Assert.Equal((byte)0xA5, context.Bus.ReadByte(destination + count));
            return ReadBytes(context.Bus, destination, (int)count);
        }
    }

    private static void VerifyPublishedMonitorState(OracleContext context, uint graphics,
        uint originalGraphics, uint templateBase, bool ntsc)
    {
        // Publication is intentionally the existing portable host owner. Only
        // allocations/free and vector readback below execute original/native68k
        // code; this must not be described as a native CMDB publisher.
        var allocator = new MonitorPublicationAllocator(context);
        var backend = new MonitorPublicationBackend(ntsc);
        // Create the host-owned resident before handing ownership to the
        // registered native database. OpenMonitor on an admitted empty CMDB
        // is a lookup, not an implicit registration/allocation operation.
        const uint compatibilitySize = 0x264;
        var compatibility = context.Allocate(compatibilitySize);
        for (uint offset = 0; offset < compatibilitySize; offset++)
            context.Bus.WriteByte(compatibility + offset, 0, 0);
        context.Bus.WriteWord(compatibility + 0x12, 0x250);
        var compatibilityList = compatibility + (uint)GraphicsLayouts.GfxBaseMonitorList;
        context.Bus.WriteLong(compatibilityList, compatibilityList + 4);
        context.Bus.WriteLong(compatibilityList + 8, compatibilityList);
        var core = new GraphicsLibraryCore(new BusGraphicsMemory(context.Bus), allocator,
            backend, backend, graphicsLibraryBase: compatibility);
        var monitor = core.OpenMonitor(0, 0);
        Assert.NotEqual(0u, monitor);
        Assert.Equal(compatibility, core.RebindGraphicsLibraryBase(graphics));
        Assert.True(core.IsBoundToGraphicsLibraryBase(graphics));
        Assert.Equal(0u, context.Bus.ReadLong(compatibility + (uint)GraphicsLayouts.GfxBaseDefaultMonitor));
        Assert.Equal(compatibilityList + 4, context.Bus.ReadLong(compatibilityList));
        context.Free(compatibility, compatibilitySize);
        var output = context.Allocate(104);
        var originalBefore = Query(originalGraphics, 88, false);
        var templateBefore = ReadBytes(context.Bus, templateBase + 0x250, 20);
        Assert.True(core.HasNativeGfxBaseEnvelope());
        Assert.True(core.TrySetMonitorViewPosition(0x11000, 130, 46));
        Assert.True(core.TrySetMonitorViewPosition(0x21000, -2, 7));
        Assert.Equal(monitor, core.OpenMonitor(0, 0));
        Assert.Equal(0, core.CloseMonitor(monitor));
        Assert.Equal(monitor, context.Bus.ReadLong(graphics + (uint)GraphicsLayouts.GfxBaseDefaultMonitor));
        var database = context.Bus.ReadLong(graphics + (uint)GraphicsLayouts.GfxBaseDisplayInfoDataBase);
        Assert.NotEqual(0u, database);
        Assert.Equal(monitor, context.Bus.ReadLong(database +
            (uint)GraphicsDisplayDatabase.NativeMonitorRegistrationsOffset + (ntsc ? 0u : 4u)));
        Assert.Equal(GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag, context.Bus.ReadLong(graphics + 0x250));
        Assert.Equal(graphics, context.Bus.ReadLong(graphics + 0x258));
        Assert.Equal(database, context.Bus.ReadLong(graphics + 0x25C));
        Assert.Equal((uint)GraphicsDisplayDatabase.NativeDatabaseSize, context.Bus.ReadLong(graphics + 0x260));
        var current = ntsc ? new byte[] { 0, 130, 0, 46 } : new byte[] { 0xFF, 0xFE, 0, 7 };
        foreach (var requested in new uint[] { 21, 22, 23, 24, 88, 96 })
        foreach (var odd in requested < 88 ? new[] { false, true } : new[] { false })
        {
            var bytes = Query(graphics, requested, odd);
            Assert.Equal(monitor, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4)));
            Assert.Equal(current.Take((int)Math.Min(requested - 20, 4)), bytes.Skip(20).Take(4));
            if (requested >= 88) Assert.Equal(new byte[] { 0, 129, 0, 44 }, bytes.Skip(80).Take(4));
        }
        Assert.True(core.TrySetMonitorViewPosition(ntsc ? 0x11000u : 0x21000u, -123, 456));
        Assert.Equal(new byte[] { 0xFF, 0x85, 1, 0xC8 }, Query(graphics, 88, false).Skip(20).Take(4));
        core.ReleaseNativeDisplayDatabase();
        Assert.Contains(database, allocator.Freed);
        Assert.Equal(0u, context.Bus.ReadLong(graphics + 0x250));
        Assert.Equal(0u, context.Bus.ReadLong(graphics + (uint)GraphicsLayouts.GfxBaseDisplayInfoDataBase));
        Assert.All(Query(graphics, 88, false), value => Assert.Equal((byte)0xA5, value));
        Assert.True(core.TryPublishNativeDisplayDatabase());
        var republished = Query(graphics, 88, false);
        Assert.Equal(new byte[] { 0xFF, 0x85, 1, 0xC8 }, republished.Skip(20).Take(4));
        Assert.Equal(new byte[] { 0, 129, 0, 44 }, republished.Skip(80).Take(4));
        Assert.Equal(templateBefore, ReadBytes(context.Bus, templateBase + 0x250, 20));
        Assert.Equal(originalBefore, Query(originalGraphics, 88, false));
        core.ReleaseNativeDisplayDatabase();
        context.Free(output, 104);

        byte[] Query(uint library, uint requested, bool odd)
        {
            for (uint i = 0; i < 104; i++) context.Bus.WriteByte(output + i, 0xA5, 0);
            var destination = output + (odd ? 3u : 2u);
            var returned = context.Invoke(library, (int)GraphicsLvo.GetDisplayInfoData, registers =>
            {
                registers.A[0] = 0;
                registers.A[1] = destination;
                registers.D[0] = requested;
                registers.D[1] = GraphicsDisplayDatabase.DtagMntr;
                registers.D[2] = 0;
            });
            var count = Math.Min(requested, 88);
            Assert.Equal(count, returned.D[0]);
            Assert.Equal((byte)0xA5, context.Bus.ReadByte(destination - 1));
            Assert.Equal((byte)0xA5, context.Bus.ReadByte(destination + count));
            return ReadBytes(context.Bus, destination, (int)count);
        }
    }

    private sealed class MonitorPublicationAllocator(OracleContext context) : IGraphicsAllocatorBackend
    {
        internal List<uint> Freed { get; } = new();
        public bool TryAllocate(uint byteCount, GraphicsMemoryClass memoryClass, out uint address)
        {
            address = context.Invoke(context.ExecBase, ExecLvo.AllocMem, registers =>
            {
                registers.D[0] = byteCount;
                registers.D[1] = (uint)(Exec.MemoryFlags.Clear |
                    (memoryClass == GraphicsMemoryClass.Chip ? Exec.MemoryFlags.Chip : Exec.MemoryFlags.Public));
            }).D[0];
            return address != 0;
        }
        public void Free(uint address, uint byteCount, GraphicsMemoryClass memoryClass)
        {
            context.Free(address, byteCount);
            Freed.Add(address);
        }
    }

    private sealed class MonitorPublicationBackend(bool ntsc) : IGraphicsBlitterBackend,
        IGraphicsDisplayBackend, IGraphicsDisplayProfileBackend, IGraphicsDisplayChipsetBackend
    {
        public bool IsNtsc => ntsc;
        public bool SupportsEcsDisplay => false;
        public void Own() => throw new InvalidOperationException("Unexpected blitter operation.");
        public void Disown() => throw new InvalidOperationException("Unexpected blitter operation.");
        public void Wait() => throw new InvalidOperationException("Unexpected blitter operation.");
        public void Submit(uint operationAddress) => throw new InvalidOperationException("Unexpected blitter operation.");
        public void PublishView(uint viewAddress) => throw new InvalidOperationException("Unexpected display operation.");
        public void WaitForTopOfFrame() => throw new InvalidOperationException("Unexpected display operation.");
        public void WaitForBeginningOfVerticalBlank(uint viewPortAddress) => throw new InvalidOperationException("Unexpected display operation.");
        public ushort GetBeamPosition() => throw new InvalidOperationException("Unexpected display operation.");
    }
}
