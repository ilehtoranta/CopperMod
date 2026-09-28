using Amiga;
using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed partial class KickstartRomLayersDifferentialTests
{
    [GraphicsRomTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NativeGraphicsOwnMonitorUsesOriginalExecAndNativeOpenCloseReadback(bool ntsc, bool relocated)
    {
        Assert.True(TryLoadConfiguredRom(out var rom));
        var context = CreateNativeIntuitionOracle(rom, suppressInputDeviceOverlay: true,
            ntsc: ntsc, ecs: false, chipRamBytes: 512 * 1024);
        try
        {
            Assert.NotEqual(0u, ContinueUntilNativeIntuitionPublished(context));
            RestoreOriginalExecForGraphicsOracle(context);
            CheckExecEntries();
            var originalGraphics = FindLibrary(context.Bus, context.ExecBase, "graphics.library");
            var originalList = ReadBytes(context.Bus, originalGraphics + 0x180, 18);
            var profile = ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal;
            const int size = GraphicsLibraryImageLayout.NativeMonitorImageSize;
            uint resident, template;
            if (relocated)
            {
                var hunk = NativeGraphicsLibraryHunkBuilder.BuildFromRasterBodies(size, profile,
                    includeNativeRuntimeDescriptor: true, initializeAllocatedImage: true,
                    includeNativeMonitorDescriptor: true);
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
                context.Bus.MapWritableMemory(codeAddress, code.Concat(
                    NativeGraphicsLibraryInitializer.Build(size, profile, true, true)).ToArray());
                var image = NativeGraphicsLibraryImageBuilder.Build(0x00700000, 0x00A80000, init,
                    entries.ToDictionary(pair => pair.Key, pair => codeAddress + (uint)pair.Value),
                    codeAddress + (uint)fallback, size, profile, true, true);
                context.Bus.MapWritableMemory(image.VectorBase, image.VectorBytes);
                context.Bus.MapWritableMemory(image.LibraryBase, image.PositiveBytes);
                context.Bus.MapWritableMemory(image.ResidentAddress, image.ResidentBytes);
                resident = image.ResidentAddress;
                template = image.LibraryBase;
            }
            var graphics = context.Invoke(context.ExecBase, (int)ExecLvo.InitResident, state =>
            {
                state.A[1] = resident;
                state.D[1] = 0;
            }).D[0];
            Assert.NotEqual(0u, graphics);
            Assert.NotEqual(template, graphics);
            Assert.NotEqual(originalGraphics, graphics);
            var templateBefore = ReadBytes(context.Bus, template + 0x250, size - 0x250);
            Assert.Equal(2u, context.Bus.ReadLong(graphics + 0x268));
            Assert.Equal(0u, context.Bus.ReadLong(graphics + 0x264));
            var cmdbCode = NativeGraphicsMonitorStatePublisher.Build(false, ntsc);
            var monitorCode = NativeGraphicsMonitorSpecPublisher.BuildRegistered(ntsc);
            var releaseCode = NativeGraphicsMonitorSpecRelease.BuildRegistered(ntsc);
            var publisherAddress = relocated ? 0x00B80000u : 0x00600000u;
            var monitorPublisher = publisherAddress + (uint)cmdbCode.Length;
            var monitorRelease = monitorPublisher + (uint)monitorCode.Length;
            var cmdbRelease = monitorRelease + (uint)releaseCode.Length;
            context.Bus.MapWritableMemory(publisherAddress,
                cmdbCode.Concat(monitorCode).Concat(releaseCode).Concat(NativeGraphicsMonitorStateRelease.Build()).ToArray());
            var freeBeforeCmdb = FreeBytes();
            Assert.Equal(graphics, Publish(publisherAddress));
            var database = context.Bus.ReadLong(graphics + 0x25C);
            var registration = database + (uint)GraphicsDisplayDatabase.NativeMonitorRegistrationsOffset + (ntsc ? 0u : 4u);
            Assert.Equal(0u, context.Bus.ReadLong(registration));
            var cmdb = ReadBytes(context.Bus, graphics + 0x250, 20);
            var freeBeforeMonitor = FreeBytes();
            Assert.Equal(graphics, Publish(monitorPublisher));
            var monitor = context.Bus.ReadLong(graphics + 0x18E);
            Assert.NotEqual(0u, monitor);
            Assert.Equal(monitor, context.Bus.ReadLong(registration));
            Assert.Equal(GraphicsLibraryImageLayout.NativeMonitorDescriptorValidTag, context.Bus.ReadLong(graphics + 0x264));
            Assert.Equal(graphics, context.Bus.ReadLong(graphics + 0x26C));
            Assert.Equal(monitor, context.Bus.ReadLong(graphics + 0x270));
            Assert.Equal((uint)GraphicsMonitorSpecImage.CreateNativeDefaultImage(ntsc).Length, context.Bus.ReadLong(graphics + 0x274));
            Assert.Equal(ntsc ? 0x11000u : 0x21000u, context.Bus.ReadLong(graphics + 0x278));
            Assert.Equal(monitor, context.Bus.ReadLong(graphics + 0x180));
            Assert.Equal(monitor, context.Bus.ReadLong(graphics + 0x188));
            Assert.Equal(graphics + 0x184, context.Bus.ReadLong(monitor));
            Assert.Equal(graphics + 0x180, context.Bus.ReadLong(monitor + 4));
            Assert.Equal(graphics, context.Bus.ReadLong(monitor + (uint)GraphicsLayouts.ExtendedNodeLibrary));
            Assert.Equal(monitor + GraphicsLayouts.MonitorSpecSize,
                context.Bus.ReadLong(monitor + (uint)GraphicsLayouts.MonitorSpecNodeName));
            var beforeDuplicate = ReadBytes(context.Bus, graphics, size);
            var freeBeforeDuplicate = context.Invoke(context.ExecBase, (int)ExecLvo.AvailMem, state => state.D[1] = 0).D[0];
            Assert.Equal(0u, Publish(monitorPublisher));
            Assert.Equal(beforeDuplicate, ReadBytes(context.Bus, graphics, size));
            Assert.Equal(freeBeforeDuplicate, context.Invoke(context.ExecBase, (int)ExecLvo.AvailMem, state => state.D[1] = 0).D[0]);
            AssertCount(0);
            for (ushort count = 1; count <= 2; count++)
            {
                Assert.Equal(monitor, Open());
                AssertCount(count);
            }
            var beforeBusyRelease = ReadBytes(context.Bus, graphics, size);
            var freeBeforeBusyRelease = FreeBytes();
            Assert.Equal(0u, Publish(monitorRelease)); // Live OpenMonitor references must prevent teardown.
            Assert.Equal(monitor, context.Bus.ReadLong(registration));
            AssertCount(2);
            Assert.Equal(beforeBusyRelease, ReadBytes(context.Bus, graphics, size));
            Assert.Equal(freeBeforeBusyRelease, FreeBytes());
            for (var count = 1; count >= 0; count--)
            {
                Close();
                AssertCount((ushort)count);
            }
            var output = context.Allocate(96);
            for (uint offset = 0; offset < 96; offset++) context.Bus.WriteByte(output + offset, 0xA5, 0);
            Assert.Equal(88u, context.Invoke(graphics, (int)GraphicsLvo.GetDisplayInfoData, state =>
            {
                state.A[1] = output + 2;
                state.D[0] = 88;
                state.D[1] = GraphicsDisplayDatabase.DtagMntr;
            }).D[0]);
            Assert.Equal(monitor, context.Bus.ReadLong(output + 18));
            Assert.Equal(0x0081002Cu, context.Bus.ReadLong(output + 22));
            Assert.Equal(0x0081002Cu, context.Bus.ReadLong(output + 82));
            Assert.Equal((byte)0xA5, context.Bus.ReadByte(output + 1));
            Assert.Equal((byte)0xA5, context.Bus.ReadByte(output + 90));
            AssertCount(0); // Reading MNTR does not acquire an OpenMonitor reference.
            Assert.Equal(monitor, Open());
            Close();
            AssertCount(0);
            Assert.Equal(cmdb, ReadBytes(context.Bus, graphics + 0x250, 20));
            Assert.Equal(templateBefore, ReadBytes(context.Bus, template + 0x250, size - 0x250));
            Assert.Equal(originalList, ReadBytes(context.Bus, originalGraphics + 0x180, 18));
            AssertOriginalGraphicsRomEntries(context, originalGraphics, rom);
            context.Free(output, 96);
            // Exercise the replacement's CMDB3 mapping with an unlinked borrowed
            // node allocated through original Exec. The original-ROM oracle pins
            // these selection semantics separately; this is native integration.
            var readback = context.Allocate(96);
            var borrowed = context.Allocate(GraphicsLayouts.MonitorSpecSize);
            try
            {
                var publicNode = ReadBytes(context.Bus, monitor, GraphicsLayouts.MonitorSpecSize);
                for (var i = 0; i < publicNode.Length; i++) context.Bus.WriteByte(borrowed + (uint)i, publicNode[i], 0);
                context.Bus.WriteLong(borrowed, 0xDEADBEEF);
                context.Bus.WriteLong(borrowed + 4, 0xDEADBEEF);
                context.Bus.WriteLong(borrowed + (uint)GraphicsLayouts.MonitorSpecNodeName, 0xFFFFFFFF);
                context.Bus.WriteWord(borrowed + (uint)GraphicsLayouts.MonitorSpecFlags, 0);
                foreach (var selected in new[] { borrowed, 0u })
                {
                    context.Bus.WriteLong(registration, selected);
                    var freeBeforeSelection = FreeBytes();
                    foreach (var key in new uint[] { 0, 4, 0x8000, 0x8004, 0x8020, 0x8024, 0x800, 0x804,
                        0x80, 0x84, 0x400, 0x404, 0x8400, 0x8404, 0x8420, 0x8424, 0x440, 0x444,
                        0x8440, 0x8444, 0x8460, 0x8464 })
                    foreach (var id in new[] { key, key | 0x1000u, key | (ntsc ? 0x11000u : 0x21000u) })
                    {
                        var expected = id == 0 ? monitor : selected;
                        Assert.Equal(expected, context.Invoke(graphics, (int)GraphicsLvo.OpenMonitor,
                            state => state.D[0] = id).D[0]);
                        if (expected != 0)
                        {
                            Assert.Equal((ushort)1, context.Bus.ReadWord(expected + (uint)GraphicsLayouts.MonitorSpecOpenCount));
                            Assert.Equal(0u, context.Invoke(graphics, (int)GraphicsLvo.CloseMonitor,
                                state => state.A[0] = expected).D[0]);
                        }
                        AssertCount(0);
                        Assert.Equal((ushort)0, context.Bus.ReadWord(borrowed + (uint)GraphicsLayouts.MonitorSpecOpenCount));
                    }
                    Assert.Equal(freeBeforeSelection, FreeBytes());
                    CheckRegisteredReadback(selected);
                }
                // Opaque query values are never passed to OpenMonitor.
                foreach (var raw in new[] { 1u, borrowed + 1, 0xFFFFFF60u, 0xDEADBEEFu, uint.MaxValue })
                {
                    context.Bus.WriteLong(registration, raw);
                    CheckRegisteredReadback(raw);
                }

                void CheckRegisteredReadback(uint pointer)
                {
                    var expected = GraphicsMonitorInfoFamilyRecordTests.Baseline(ntsc);
                    BinaryPrimitives.WriteUInt32BigEndian(expected.AsSpan(16), pointer);
                    var dbBefore = ReadBytes(context.Bus, database, GraphicsDisplayDatabase.NativeDatabaseSize);
                    var freeBefore = FreeBytes();
                    foreach (var (key, _) in GraphicsDisplayInfoTransferBoundaryTests.CapturedOcsEcsDepths)
                    foreach (var id in new[] { key, key | 0x1000u, key | (ntsc ? 0x11000u : 0x21000u) })
                    {
                        for (uint offset = 0; offset < 96; offset++) context.Bus.WriteByte(readback + offset, 0xA5, 0);
                        Assert.Equal(88u, context.Invoke(graphics, (int)GraphicsLvo.GetDisplayInfoData, state =>
                        {
                            state.A[1] = readback + 1;
                            state.D[0] = 96;
                            state.D[1] = GraphicsDisplayDatabase.DtagMntr;
                            state.D[2] = id;
                        }).D[0]);
                        Assert.Equal(expected, ReadBytes(context.Bus, readback + 1, 88));
                        Assert.Equal((byte)0xA5, context.Bus.ReadByte(readback));
                        Assert.All(ReadBytes(context.Bus, readback + 89, 7), value => Assert.Equal((byte)0xA5, value));
                        AssertCount(0);
                        Assert.Equal((ushort)0, context.Bus.ReadWord(borrowed + (uint)GraphicsLayouts.MonitorSpecOpenCount));
                    }
                    Assert.Equal(dbBefore, ReadBytes(context.Bus, database, GraphicsDisplayDatabase.NativeDatabaseSize));
                    Assert.Equal(freeBefore, FreeBytes());
                }
            }
            finally
            {
                context.Bus.WriteLong(registration, monitor);
                context.Free(borrowed, GraphicsLayouts.MonitorSpecSize);
                context.Free(readback, 96);
            }
            Assert.Equal(graphics, Publish(monitorRelease));
            Assert.Equal(freeBeforeMonitor, FreeBytes());
            AssertInertMonitor();
            Assert.Equal(0u, Publish(monitorRelease)); // No second free of an inert descriptor.
            Assert.Equal(freeBeforeMonitor, FreeBytes());
            Assert.Equal(cmdb, ReadBytes(context.Bus, graphics + 0x250, 20));
            Assert.Equal(graphics, Publish(monitorPublisher));
            monitor = context.Bus.ReadLong(graphics + 0x18E);
            Assert.NotEqual(0u, monitor);
            Assert.Equal(monitor, Open());
            AssertCount(1);
            Close();
            AssertCount(0);
            Assert.Equal(graphics, Publish(monitorRelease));
            AssertInertMonitor();
            Assert.Equal(freeBeforeMonitor, FreeBytes());
            Assert.Equal(graphics, Publish(cmdbRelease)); // Reverse-order native sidecar cleanup.
            Assert.Equal(freeBeforeCmdb, FreeBytes());
            Assert.Equal(0u, context.Bus.ReadLong(graphics + 0x250));
            Assert.Equal(0u, context.Bus.ReadLong(graphics + (uint)GraphicsLayouts.GfxBaseDisplayInfoDataBase));
            Assert.Equal(templateBefore, ReadBytes(context.Bus, template + 0x250, size - 0x250));
            Assert.Equal(originalList, ReadBytes(context.Bus, originalGraphics + 0x180, 18));
            CheckExecEntries();
            // The library itself lives until guest disposal. This qualifies
            // both sidecar teardowns, not a complete graphics.library Expunge.

            uint Publish(uint address) => context.Invoke(address, 0, state =>
            {
                state.D[0] = graphics;
                state.A[6] = context.ExecBase;
            }).D[0];
            uint Open() => context.Invoke(graphics, (int)GraphicsLvo.OpenMonitor, state => state.D[0] = 0).D[0];
            void Close() => Assert.Equal(0u, context.Invoke(graphics, (int)GraphicsLvo.CloseMonitor, state => state.A[0] = monitor).D[0]);
            void AssertCount(ushort count) => Assert.Equal(count, context.Bus.ReadWord(monitor + (uint)GraphicsLayouts.MonitorSpecOpenCount));
            uint FreeBytes() => context.Invoke(context.ExecBase, (int)ExecLvo.AvailMem, state => state.D[1] = 0).D[0];
            void AssertInertMonitor()
            {
                Assert.Equal(0u, context.Bus.ReadLong(registration));
                foreach (var offset in new uint[] { 0x264, 0x26C, 0x270, 0x274, 0x278, 0x18E })
                    Assert.Equal(0u, context.Bus.ReadLong(graphics + offset));
                Assert.Equal(2u, context.Bus.ReadLong(graphics + 0x268));
                Assert.Equal(graphics + 0x184, context.Bus.ReadLong(graphics + 0x180));
                Assert.Equal(0u, context.Bus.ReadLong(graphics + 0x184));
                Assert.Equal(graphics + 0x180, context.Bus.ReadLong(graphics + 0x188));
            }
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

    [GraphicsRomTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeGraphicsMonitorSemaphoreUsesOriginalExecInitialization(bool ntsc)
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
            var monitor = context.Bus.ReadLong(originalGraphics + (uint)GraphicsLayouts.GfxBaseDefaultMonitor);
            Assert.NotEqual(0u, monitor);
            var originalSemaphore = monitor + (uint)GraphicsLayouts.MonitorSpecDisplayInfoSemaphore;
            var originalBefore = ReadBytes(context.Bus, originalSemaphore, GraphicsLayouts.PaletteExtraSemaphoreSize);
            var semaphore = context.Allocate(GraphicsLayouts.PaletteExtraSemaphoreSize);
            context.Invoke(context.ExecBase, (int)ExecLvo.InitSemaphore, state => state.A[0] = semaphore);
            var expectedQueueCount = context.Bus.ReadWord(semaphore + (uint)GraphicsLayouts.PaletteExtraSemaphoreQueueCount);
            _output.WriteLine($"monitor-semaphore:ntsc={ntsc}:exec-queue={expectedQueueCount:X4}:rom-queue={context.Bus.ReadWord(originalSemaphore + (uint)GraphicsLayouts.PaletteExtraSemaphoreQueueCount):X4}");
            Assert.Equal(ushort.MaxValue, expectedQueueCount);
            AssertSemaphore(semaphore);
            AssertSemaphore(originalSemaphore);

            var backend = new MonitorPublicationBackend(ntsc);
            var core = new GraphicsLibraryCore(new BusGraphicsMemory(context.Bus),
                new MonitorPublicationAllocator(context), backend, backend);
            var portable = core.OpenMonitor(0, 0);
            Assert.NotEqual(0u, portable);
            AssertSemaphore(portable + (uint)GraphicsLayouts.MonitorSpecDisplayInfoSemaphore);
            var nativeImage = GraphicsMonitorSpecImage.CreateNativeDefaultImage(ntsc);
            var nativeMonitor = context.Allocate((uint)nativeImage.Length);
            const uint initializerAddress = 0x00600000;
            context.Bus.MapWritableMemory(initializerAddress, NativeGraphicsMonitorSpecInitializer.Build(ntsc));
            Assert.Equal(nativeMonitor, context.Invoke(initializerAddress, 0, state =>
            {
                state.D[0] = nativeMonitor;
                state.D[1] = (uint)nativeImage.Length;
                state.A[0] = originalGraphics;
            }).D[0]);
            AssertSemaphore(nativeMonitor + (uint)GraphicsLayouts.MonitorSpecDisplayInfoSemaphore);
            var normalizedPortable = ReadBytes(context.Bus, portable, GraphicsLayouts.MonitorSpecSize);
            foreach (var offset in GraphicsMonitorSpecImage.NativeSelfPointerOffsets)
            {
                var value = offset == GraphicsLayouts.MonitorSpecNodeName
                    ? nativeMonitor + GraphicsLayouts.MonitorSpecSize
                    : BinaryPrimitives.ReadUInt32BigEndian(normalizedPortable.AsSpan(offset, 4)) - portable + nativeMonitor;
                BinaryPrimitives.WriteUInt32BigEndian(normalizedPortable.AsSpan(offset, 4), value);
            }
            BinaryPrimitives.WriteUInt32BigEndian(normalizedPortable.AsSpan(GraphicsLayouts.ExtendedNodeLibrary, 4), originalGraphics);
            BinaryPrimitives.WriteUInt16BigEndian(normalizedPortable.AsSpan(GraphicsLayouts.MonitorSpecOpenCount, 2), 0);
            Assert.Equal(normalizedPortable, ReadBytes(context.Bus, nativeMonitor, GraphicsLayouts.MonitorSpecSize));
            var task = context.Bus.ReadLong(context.ExecBase + (uint)ExecLayout.ExecBase.ThisTask);
            Assert.NotEqual(0u, task);
            foreach (var address in new[] { semaphore,
                portable + (uint)GraphicsLayouts.MonitorSpecDisplayInfoSemaphore,
                nativeMonitor + (uint)GraphicsLayouts.MonitorSpecDisplayInfoSemaphore })
            {
                var idle = ReadBytes(context.Bus, address, GraphicsLayouts.PaletteExtraSemaphoreSize);
                for (ushort depth = 1; depth <= 2; depth++)
                {
                    Assert.NotEqual(0u, context.Invoke(context.ExecBase, (int)ExecLvo.AttemptSemaphore,
                        state => state.A[0] = address).D[0]);
                    Assert.Equal(depth, context.Bus.ReadWord(address + (uint)GraphicsLayouts.PaletteExtraSemaphoreNestCount));
                    Assert.Equal(task, context.Bus.ReadLong(address + (uint)GraphicsLayouts.PaletteExtraSemaphoreOwner));
                }
                context.Invoke(context.ExecBase, (int)ExecLvo.ReleaseSemaphore, state => state.A[0] = address);
                context.Invoke(context.ExecBase, (int)ExecLvo.ReleaseSemaphore, state => state.A[0] = address);
                Assert.Equal(idle, ReadBytes(context.Bus, address, idle.Length));
            }
            context.Free(nativeMonitor, (uint)nativeImage.Length);
            Assert.Equal(originalBefore, ReadBytes(context.Bus, originalSemaphore, originalBefore.Length));
            context.Free(semaphore, GraphicsLayouts.PaletteExtraSemaphoreSize);
            CheckExecEntries();

            void AssertSemaphore(uint address)
            {
                Assert.Equal((byte)15, context.Bus.ReadByte(address + (uint)GraphicsLayouts.PaletteExtraSemaphoreNodeType));
                Assert.Equal((ushort)0, context.Bus.ReadWord(address + (uint)GraphicsLayouts.PaletteExtraSemaphoreNestCount));
                Assert.Equal(0u, context.Bus.ReadLong(address + (uint)GraphicsLayouts.PaletteExtraSemaphoreOwner));
                Assert.Equal(expectedQueueCount, context.Bus.ReadWord(address + (uint)GraphicsLayouts.PaletteExtraSemaphoreQueueCount));
                var wait = address + (uint)GraphicsLayouts.PaletteExtraSemaphoreWaitQueue;
                Assert.Equal(wait + 4, context.Bus.ReadLong(wait));
                Assert.Equal(0u, context.Bus.ReadLong(wait + 4));
                Assert.Equal(wait, context.Bus.ReadLong(wait + 8));
            }

            void CheckExecEntries()
            {
                var mapped = ReadBytes(context.Bus, GraphicsProbeRomBase, rom.Length);
                foreach (var lvo in new[] { ExecLvo.InitSemaphore, ExecLvo.AttemptSemaphore,
                    ExecLvo.ReleaseSemaphore, ExecLvo.AllocMem, ExecLvo.FreeMem })
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
