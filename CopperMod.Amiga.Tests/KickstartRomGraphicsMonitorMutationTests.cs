using System.Buffers.Binary;
using Amiga;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed partial class KickstartRomLayersDifferentialTests
{
    [GraphicsRomTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeReplacementMonitorSetDisplayInfoDataPublishesPrivateRecord(bool ntsc)
    {
        Assert.True(TryLoadConfiguredRom(out var rom));
        var context = CreateNativeIntuitionOracle(rom, suppressInputDeviceOverlay: true,
            ntsc: ntsc, ecs: false, chipRamBytes: 512 * 1024);
        try
        {
            Assert.NotEqual(0u, ContinueUntilNativeIntuitionPublished(context));
            RestoreOriginalExecForGraphicsOracle(context);
            var profile = ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal;
            const int size = GraphicsLibraryImageLayout.NativeMonitorImageSize;
            const uint codeAddress = 0x00400000;
            var code = NativeGraphicsRasterBodies.BuildCodeWithMonitorMutation(
                out _, out _, out _, defaultMonitorNtsc: ntsc);
            var initializer = NativeGraphicsLibraryInitializer.BuildWithNativeMonitorSpec(
                size, profile, supportsEcsDisplay: false, releaseLibraryOnFailure: true,
                includeNativeMonitorMutation: true);
            var initAddress = codeAddress + (uint)code.Length;
            context.Bus.MapWritableMemory(codeAddress, code.Concat(initializer).ToArray());
            var image = NativeGraphicsLibraryImageBuilder.BuildFromRasterBodies(
                0x00700000, 0x00A80000, codeAddress,
                initAddress, size, profile,
                includeNativeRuntimeDescriptor: true,
                includeNativeMonitorDescriptor: true,
                includeNativeMonitorMutation: true);
            context.Bus.MapWritableMemory(image.VectorBase, image.VectorBytes);
            context.Bus.MapWritableMemory(image.LibraryBase, image.PositiveBytes);
            context.Bus.MapWritableMemory(image.ResidentAddress, image.ResidentBytes);

            var graphics = context.Invoke(context.ExecBase, (int)ExecLvo.InitResident, state =>
            {
                state.A[1] = image.ResidentAddress;
                state.D[1] = 0;
            }).D[0];
            Assert.NotEqual(0u, graphics);
            var family = ntsc ? 0x11000u : 0x21000u;
            var source = context.Allocate(88);
            try
            {
                var baseline = Query(88);
                var beforeSource = ReadBytes(context.Bus, source, 88);
                context.Bus.WriteLong(source + 24, 0x23456789u);
                beforeSource = ReadBytes(context.Bus, source, 88);
                var beforeFree = FreeBytes();
                var changed = Set(88);
                Assert.Equal(68u, changed);
                Assert.Equal(beforeSource, ReadBytes(context.Bus, source, 88));
                var actual = Query(88);
                var expected = baseline.ToArray();
                BinaryPrimitives.WriteUInt32BigEndian(expected.AsSpan(24), 0x23456789u);
                Assert.Equal(expected, actual);
                Assert.Equal(beforeFree, FreeBytes());

                WriteRecord(baseline);
                Assert.Equal(68u, Set(88));
                WriteRecord(baseline);
                var tooShort = Set(19);
                Assert.Equal(0u, tooShort);
                Assert.Equal(baseline, Query(88));
                Assert.Equal(beforeFree, FreeBytes());

                WriteRecord(baseline);
                context.Bus.WriteByte(source + 20, 0xA5, 0);
                var partialExpected = baseline.ToArray();
                partialExpected[20] = 0xA5;
                var partialBefore = ReadBytes(context.Bus, source, 88);
                var partial = Set(21);
                Assert.Equal(1u, partial);
                Assert.Equal(partialBefore, ReadBytes(context.Bus, source, 88));
                Assert.Equal(partialExpected, Query(88));
                Assert.Equal(beforeFree, FreeBytes());
            }
            finally
            {
                context.Free(source, 88);
            }

            uint Set(uint count) => context.Invoke(graphics, GraphicsPrivateLvo.SetDisplayInfoData, state =>
            {
                state.A[0] = 0;
                state.A[1] = source;
                state.D[0] = count;
                state.D[1] = GraphicsDisplayDatabase.DtagMntr;
                state.D[2] = family;
            }).D[0];

            void WriteRecord(byte[] bytes)
            {
                for (var index = 0; index < bytes.Length; index++)
                    context.Bus.WriteByte(source + (uint)index, bytes[index], 0);
            }

            byte[] Query(uint count)
            {
                var queryState = context.Invoke(graphics, (int)GraphicsLvo.GetDisplayInfoData, state =>
                {
                    state.A[0] = 0;
                    state.A[1] = source;
                    state.D[0] = count;
                    state.D[1] = GraphicsDisplayDatabase.DtagMntr;
                    state.D[2] = family;
                });
                var result = queryState.D[0];
                Assert.Equal(Math.Min(count, 88u), result);
                return ReadBytes(context.Bus, source, 88);
            }

            uint FreeBytes() => context.Invoke(context.ExecBase, (int)ExecLvo.AvailMem,
                state => state.D[1] = 0).D[0];
        }
        finally
        {
            context.Machine.Dispose();
        }
    }

    [GraphicsRomTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeGraphicsMonitorSetDisplayInfoDataSizeOracle(bool ntsc)
    {
        Assert.True(TryLoadConfiguredRom(out var rom));
        var context = CreateNativeIntuitionOracle(rom, suppressInputDeviceOverlay: true,
            ntsc: ntsc, ecs: false, chipRamBytes: 512 * 1024);
        try
        {
            Assert.NotEqual(0u, ContinueUntilNativeIntuitionPublished(context));
            RestoreOriginalExecForGraphicsOracle(context);
            var graphics = FindLibrary(context.Bus, context.ExecBase, "graphics.library");
            Assert.NotEqual(0u, graphics);
            AssertOriginalGraphicsRomEntries(context, graphics, rom);

            const int setDisplayInfoData = -750;
            var family = ntsc ? 0x11000u : 0x21000u;
            var source = context.Allocate(88);
            Assert.NotEqual(0u, source);
            try
            {
                var baseline = Query(88, source, family);
                var freeBefore = Available();
                var sizes = new uint[] { 0, 1, 4, 8, 15, 16, 17, 20, 21, 24, 25,
                    43, 44, 76, 80, 81, 84, 85, 87, 88, 89, 96 };
                foreach (var size in sizes)
                {
                    Write(source, baseline);
                    if (size >= 20)
                        context.Bus.WriteLong(source + 16, unchecked(0xA0000000u + size));
                    var beforeSource = ReadBytes(context.Bus, source, 128);
                    var result = Transfer(setDisplayInfoData, size, source, family);
                    var afterSource = ReadBytes(context.Bus, source, 128);
                    Assert.Equal(beforeSource, afterSource);
                    var readback = Query(88, source, family);
                    var mspc = BinaryPrimitives.ReadUInt32BigEndian(readback.AsSpan(16, 4));
                    _output.WriteLine($"monitor-mutation:size={size}:set={result}:mspc={mspc:X8}:record={Convert.ToHexString(readback)}");
                    var expectedResult = size < 20 ? 0u : Math.Min(size, 88u) - 20u;
                    Assert.Equal(expectedResult, result);
                    var expectedMspc = size < 20
                        ? BinaryPrimitives.ReadUInt32BigEndian(baseline.AsSpan(16, 4))
                        : unchecked(0xA0000000u + size);
                    Assert.Equal(expectedMspc, mspc);
                    Assert.Equal(freeBefore, Available());
                    Restore(source, baseline, family);
                }
                AssertOriginalGraphicsRomEntries(context, graphics, rom);
            }
            finally
            {
                Restore(source, baseline: null, family);
                context.Free(source, 88);
            }

            uint Transfer(int lvo, uint size, uint buffer, uint id) => context.Invoke(graphics, lvo, state =>
            {
                state.A[0] = 0;
                state.A[1] = buffer;
                state.D[0] = size;
                state.D[1] = GraphicsDisplayDatabase.DtagMntr;
                state.D[2] = id;
            }).D[0];

            byte[] Query(uint size, uint buffer, uint id)
            {
                var result = Transfer((int)GraphicsLvo.GetDisplayInfoData, size, buffer, id);
                Assert.Equal(Math.Min(size, 88u), result);
                return ReadBytes(context.Bus, buffer, 88);
            }

            uint Available() => context.Invoke(context.ExecBase, (int)ExecLvo.AvailMem,
                state => state.D[1] = 0).D[0];

            void Restore(uint buffer, byte[]? baseline, uint id)
            {
                if (baseline is null) return;
                Write(buffer, baseline);
                Assert.Equal(68u, Transfer(setDisplayInfoData, 88, buffer, id));
            }

            void Write(uint address, byte[] bytes)
            {
                for (var index = 0; index < bytes.Length; index++)
                    context.Bus.WriteByte(address + (uint)index, bytes[index], 0);
            }
        }
        finally
        {
            context.Machine.Dispose();
        }
    }

    [GraphicsRomTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeGraphicsMonitorSetDisplayInfoDataFieldOracle(bool ntsc)
    {
        Assert.True(TryLoadConfiguredRom(out var rom));
        var context = CreateNativeIntuitionOracle(rom, suppressInputDeviceOverlay: true,
            ntsc: ntsc, ecs: false, chipRamBytes: 512 * 1024);
        try
        {
            Assert.NotEqual(0u, ContinueUntilNativeIntuitionPublished(context));
            RestoreOriginalExecForGraphicsOracle(context);
            var graphics = FindLibrary(context.Bus, context.ExecBase, "graphics.library");
            var family = ntsc ? 0x11000u : 0x21000u;
            const int setDisplayInfoData = -750;
            var source = context.Allocate(88);
            var candidate = context.Allocate(224);
            var candidateImage = GraphicsMonitorSpecImage.Create(candidate, candidate + 160, !ntsc, 0, graphics);
            for (var index = 0; index < candidateImage.Length; index++)
                context.Bus.WriteByte(candidate + (uint)index, candidateImage[index], 0);
            var candidateName = System.Text.Encoding.ASCII.GetBytes("mutation.monitor\0");
            for (var index = 0; index < candidateName.Length; index++)
                context.Bus.WriteByte(candidate + 160u + (uint)index, candidateName[index], 0);
            Assert.NotEqual(0u, source);
            try
            {
                var baseline = Query(88);
                foreach (var patch in new[]
                {
                    (Offset: 20, Value: 0x12345678u),
                    (Offset: 24, Value: 0x23456789u),
                    (Offset: 44, Value: 0x3456789Au),
                    (Offset: 76, Value: 0x456789ABu),
                    (Offset: 80, Value: 0x56789ABCu),
                    (Offset: 84, Value: 0x6789ABCDu),
                })
                {
                    Write(source, baseline);
                    context.Bus.WriteLong(source + (uint)patch.Offset, patch.Value);
                    var beforeFree = Available();
                    var changed = context.Invoke(graphics, setDisplayInfoData, state =>
                    {
                        state.A[0] = 0;
                        state.A[1] = source;
                        state.D[0] = 88;
                        state.D[1] = GraphicsDisplayDatabase.DtagMntr;
                        state.D[2] = family;
                    }).D[0];
                    var readback = Query(88);
                    _output.WriteLine($"monitor-field-mutation:offset={patch.Offset}:set={changed}:record={Convert.ToHexString(readback)}");
                    Assert.Equal(68u, changed);
                    var expected = baseline.ToArray();
                    BinaryPrimitives.WriteUInt32BigEndian(expected.AsSpan(patch.Offset), patch.Value);
                    Assert.Equal(expected, readback);
                    Assert.Equal(beforeFree, Available());
                    Write(source, baseline);
                    Assert.Equal(68u, context.Invoke(graphics, setDisplayInfoData, state =>
                    {
                        state.A[0] = 0;
                        state.A[1] = source;
                        state.D[0] = 88;
                        state.D[1] = GraphicsDisplayDatabase.DtagMntr;
                        state.D[2] = family;
                    }).D[0]);
                }
            }
            finally
            {
                context.Free(source, 88);
                context.Free(candidate, 224);
            }

            byte[] Query(uint size)
            {
                var result = context.Invoke(graphics, (int)GraphicsLvo.GetDisplayInfoData, state =>
                {
                    state.A[0] = 0;
                    state.A[1] = source;
                    state.D[0] = size;
                    state.D[1] = GraphicsDisplayDatabase.DtagMntr;
                    state.D[2] = family;
                }).D[0];
                _output.WriteLine($"monitor-field-query:size={size}:result={result}:graphics={graphics:X8}:source={source:X8}:family={family:X8}");
                return ReadBytes(context.Bus, source, 88);
            }

            void Write(uint address, byte[] bytes)
            {
                for (var index = 0; index < bytes.Length; index++)
                    context.Bus.WriteByte(address + (uint)index, bytes[index], 0);
            }

            uint Available() => context.Invoke(context.ExecBase, (int)ExecLvo.AvailMem,
                state => state.D[1] = 0).D[0];
        }
        finally
        {
            context.Machine.Dispose();
        }
    }

    [GraphicsRomTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeGraphicsMonitorSetDisplayInfoDataAdmissionOracle(bool ntsc)
    {
        Assert.True(TryLoadConfiguredRom(out var rom));
        var context = CreateNativeIntuitionOracle(rom, suppressInputDeviceOverlay: true,
            ntsc: ntsc, ecs: false, chipRamBytes: 512 * 1024);
        try
        {
            Assert.NotEqual(0u, ContinueUntilNativeIntuitionPublished(context));
            RestoreOriginalExecForGraphicsOracle(context);
            var graphics = FindLibrary(context.Bus, context.ExecBase, "graphics.library");
            var family = ntsc ? 0x11000u : 0x21000u;
            var source = context.Allocate(88);
            Assert.NotEqual(0u, source);
            try
            {
                var baseline = Query(family, source);
                var freeBefore = Available();
                var cases = new[]
                {
                    (Name: "wrong-tag", Handle: 0u, Buffer: source, Size: 88u,
                        Tag: GraphicsDisplayDatabase.DtagDisp, Id: family),
                    (Name: "unknown-family", Handle: 0u, Buffer: source, Size: 88u,
                        Tag: GraphicsDisplayDatabase.DtagMntr, Id: 0xDEAD_BEEFu),
                };
                foreach (var test in cases)
                {
                    Write(source, baseline);
                    var beforeSource = ReadBytes(context.Bus, source, 88);
                    var result = Transfer(test.Handle, test.Buffer, test.Size, test.Tag, test.Id);
                    var afterSource = ReadBytes(context.Bus, source, 88);
                    var readback = Query(family, source);
                    _output.WriteLine($"monitor-mutation-admission:ntsc={ntsc}:case={test.Name}:set={result}:query={Convert.ToHexString(readback)}");
                    Assert.Equal(beforeSource, afterSource);
                    Assert.Equal(freeBefore, Available());
                    Restore(source, baseline, family);
                }

                // Source-header identity is not an admission key: the ROM
                // accepts the caller's selected family and transfers bytes
                // even when the source tag is later observed as malformed.
                Write(source, baseline);
                context.Bus.WriteLong(source, 0x9000_0001u);
                var malformed = Transfer(0, source, 88, GraphicsDisplayDatabase.DtagMntr, family);
                var malformedQueryResult = context.Invoke(graphics, (int)GraphicsLvo.GetDisplayInfoData, state =>
                {
                    state.A[0] = 0;
                    state.A[1] = source;
                    state.D[0] = 88;
                    state.D[1] = GraphicsDisplayDatabase.DtagMntr;
                    state.D[2] = family;
                }).D[0];
                var malformedReadback = ReadBytes(context.Bus, source, 88);
                _output.WriteLine($"monitor-mutation-admission:ntsc={ntsc}:case=source-tag-mutated:set={malformed}:query-result={malformedQueryResult}:query={Convert.ToHexString(malformedReadback)}");
                Assert.Equal(68u, malformed);
                Assert.Equal(freeBefore, Available());
            }
            finally
            {
                context.Free(source, 88);
            }

            uint Transfer(uint handle, uint buffer, uint size, uint tag, uint id)
                => context.Invoke(graphics, -750, state =>
                {
                    state.A[0] = handle;
                    state.A[1] = buffer;
                    state.D[0] = size;
                    state.D[1] = tag;
                    state.D[2] = id;
                }).D[0];

            byte[] Query(uint id, uint buffer)
            {
                var result = context.Invoke(graphics, (int)GraphicsLvo.GetDisplayInfoData, state =>
                {
                    state.A[0] = 0;
                    state.A[1] = buffer;
                    state.D[0] = 88;
                    state.D[1] = GraphicsDisplayDatabase.DtagMntr;
                    state.D[2] = id;
                }).D[0];
                Assert.Equal(88u, result);
                return ReadBytes(context.Bus, buffer, 88);
            }

            void Restore(uint buffer, byte[] baseline, uint id)
            {
                Write(buffer, baseline);
                Assert.Equal(68u, Transfer(0, buffer, 88, GraphicsDisplayDatabase.DtagMntr, id));
            }

            uint Available() => context.Invoke(context.ExecBase, (int)ExecLvo.AvailMem,
                state => state.D[1] = 0).D[0];

            void Write(uint address, byte[] bytes)
            {
                for (var index = 0; index < bytes.Length; index++)
                    context.Bus.WriteByte(address + (uint)index, bytes[index], 0);
            }
        }
        finally
        {
            context.Machine.Dispose();
        }
    }
}
