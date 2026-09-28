using Amiga;
using Copper68k;
using CopperMod.Amiga;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace CopperMod.Amiga.Tests;

/// <summary>
/// Opt-in, normalized differential probes against a user-supplied Kickstart
/// 3.1 image. The fixture remains local; this corpus records no ROM bytes or
/// hashes. Native residents are initialized through the ROM's own Exec vectors
/// after the headless early-boot path has published a valid ExecBase.
/// </summary>
public sealed partial class KickstartRomLayersDifferentialTests
{
    private const string RomPathVariable = "COPPER_AMIGA_KICKSTART_ROM";
    private const string RomVersionVariable = "COPPER_AMIGA_KICKSTART_VERSION";
    private const uint SentinelAddress = AmigaBootController.BootBlockAddress;
    private const uint StackAddress = SentinelAddress + 0x0F00;
    private const int MaximumVectorInstructions = 1_000_000;
    private const int GraphicsAllocBitMapLvo = -918;
    private readonly ITestOutputHelper _output;

    public KickstartRomLayersDifferentialTests(ITestOutputHelper output)
        => _output = output;

    [Fact]
    public void CopperStartBehindHookCreateFinalizesAfterEveryVisiblePartitionCallback()
    {
        var context = CreateCopperStartOracle();
        try
        {
            var layerInfo = context.NewLayerInfo();
            var display = context.CreatePlanarBitMap(32, 16, 1, 0x72);
            var blocker = context.CreateLayer(
                LayersLvo.CreateUpfrontLayer,
                layerInfo,
                display.Address,
                0,
                LayerCreationFlags.Simple,
                6,
                4,
                14,
                10);
            using var hook = context.CreateCallbackProbe(display.Address, 0);
            var created = context.InvokeLayers(LayersLvo.CreateBehindHookLayer, state =>
            {
                state.A[0] = layerInfo;
                state.A[1] = display.Address;
                state.A[2] = 0;
                state.A[3] = hook.Hook;
                state.D[0] = 1;
                state.D[1] = 2;
                state.D[2] = 20;
                state.D[3] = 12;
                state.D[4] = (uint)LayerCreationFlags.Simple;
            }).D[0];

            Assert.Equal(4, hook.CallbackCount);
            Assert.NotEqual(0u, created);
            context.AssertCopperStartCallerStackRestored();

            if (created != 0)
            {
                context.InvokeLayers(LayersLvo.DeleteLayer,
                    state => state.A[1] = created);
            }
            context.InvokeLayers(LayersLvo.DeleteLayer,
                state => state.A[1] = blocker);
            context.DisposeLayerInfo(layerInfo);
            context.FreePlanarBitMap(display);
            context.AssertCopperStartCallerStackRestored();
        }
        finally
        {
            context.Machine.Dispose();
        }
    }

    [Fact]
    public void NativeBltBitMapLeafInterpositionCopiesSynchronouslyAndRestoresVector()
    {
        if (!TryLoadConfiguredRom(out var rom))
            return;

        var context = CreateNativeOracle(rom);
        try
        {
            var source = context.CreatePlanarBitMap(16, 8, 1, 0xA7);
            var destination = context.CreatePlanarBitMap(16, 8, 1, 0x31);
            var before = HashBitMap(context, destination);
            var expected = HashBitMap(context, source);
            Assert.NotEqual(expected, before);

            var leaf = context.InstallDeterministicNativeBltBitMapLeaf();
            var result = context.InvokeGraphics((int)GraphicsLvo.BltBitMap, state =>
            {
                state.A[0] = source.Address;
                state.A[1] = destination.Address;
                state.A[2] = 0;
                state.D[0] = 0;
                state.D[1] = 0;
                state.D[2] = 0;
                state.D[3] = 0;
                state.D[4] = 16;
                state.D[5] = 8;
                state.D[6] = 0xC0;
                state.D[7] = 0xFF;
            });
            Assert.Equal(1u, result.D[0]);
            Assert.Equal(expected, HashBitMap(context, destination));
            Assert.Equal(1, leaf.CallCount);

            // Dispose asserts byte/token-exact restoration of the existing
            // native-vector probe gateway. It is deliberately explicit here
            // so restoration is proven before any guest allocation teardown.
            leaf.Dispose();
            context.FreePlanarBitMap(destination);
            context.FreePlanarBitMap(source);
        }
        finally
        {
            context.Machine.Dispose();
        }
    }

    [Fact]
    public void UnifiedSortLayerCrPreservesClassicLowWordDirections()
    {
        var context = CreateMorphOracle();
        try
        {
            // The unified surface adds MorphOS vectors to the Classic ABI.
            // SortLayerCR is a shared Classic vector, so its WORD arguments
            // retain Classic semantics even when their upper halves contain
            // caller garbage.
            var garbageUpper = RunIrregularSortOrder(
                context, 0x0000FFFF, 0xFFFF0001);
            var canonicalUpRight = RunIrregularSortOrder(
                context, 1, unchecked((uint)-1));
            var canonicalDownLeft = RunIrregularSortOrder(
                context, unchecked((uint)-1), 1);

            Assert.Equal(canonicalDownLeft, garbageUpper);
            Assert.NotEqual(canonicalUpRight, garbageUpper);
        }
        finally
        {
            context.Machine.Dispose();
        }
    }

    [Fact]
    public void CopperStartInstalledLockRoundTripsPreserveFlagsAndBeginUpdateLifecycle()
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = context.Invoke(context.LayersBase, -6);
            _ = context.Invoke(context.LayersBase, -12);
            // Match the corpus prefix so a combined SMART|SUPER create/delete
            // cannot hide root-level state contamination from the mode row.
            TraceCreationFlagMatrix(context, new List<string>());
            var layerInfo = context.NewLayerInfo();
            var fatten = context.InvokeLayers(LayersLvo.FattenLayerInfo,
                state => state.A[0] = layerInfo);
            Assert.NotEqual(0u, fatten.D[0]);
            context.InvokeLayers(LayersLvo.ThinLayerInfo,
                state => state.A[0] = layerInfo);
            var display = context.CreatePlanarBitMap(32, 16, 1, 0x31);
            _ = HashBitMap(context, display);
            var layer = context.CreateLayer(
                LayersLvo.CreateUpfrontLayer,
                layerInfo,
                display.Address,
                0,
                LayerCreationFlags.Simple,
                2,
                3,
                25,
                13);
            _ = HashBitMap(context, display);
            _ = context.Bus.Blitter.Busy;
            _ = context.LastGraphicsCallDelta;
            _ = context.LastGraphicsCallOrder;
            context.WaitForBlitterIdle();
            _ = DescribeLayer(context, layer, display.Address, 0);
            _ = HashBitMap(context, display);
            var expectedFlags = (ushort)ReadOracleLayerFlags(context.Bus, layer);
            Assert.Equal((ushort)LayerCreationFlags.Simple, expectedFlags);

            InvokeAndAssertFlags("LockLayer", LayersLvo.LockLayer,
                state => state.A[1] = layer);
            InvokeAndAssertFlags("UnlockLayer", LayersLvo.UnlockLayer,
                state => state.A[0] = layer);
            InvokeAndAssertFlags("LockLayers", LayersLvo.LockLayers,
                state => state.A[0] = layerInfo);
            InvokeAndAssertFlags("UnlockLayers", LayersLvo.UnlockLayers,
                state => state.A[0] = layerInfo);
            InvokeAndAssertFlags("LockLayerInfo", LayersLvo.LockLayerInfo,
                state => state.A[0] = layerInfo);
            InvokeAndAssertFlags("UnlockLayerInfo", LayersLvo.UnlockLayerInfo,
                state => state.A[0] = layerInfo);

            var begin = context.InvokeLayers(LayersLvo.BeginUpdate,
                state =>
                {
                    state.A[0] = layer;
                    state.D[0] = 0xA5A5_A5A5;
                });
            Assert.Equal(1u, begin.D[0]);
            Assert.Equal((ushort)(expectedFlags | (ushort)LayerFlags.Updating),
                (ushort)ReadOracleLayerFlags(context.Bus, layer));
            context.InvokeLayers(LayersLvo.EndUpdate, state =>
            {
                state.A[0] = layer;
                state.D[0] = 1;
            });
            Assert.Equal(expectedFlags,
                (ushort)ReadOracleLayerFlags(context.Bus, layer));

            context.InvokeLayers(LayersLvo.DeleteLayer,
                state => state.A[1] = layer);
            context.DisposeLayerInfo(layerInfo);
            context.FreePlanarBitMap(display);

            void InvokeAndAssertFlags(
                string operation,
                int lvo,
                Action<M68kCpuState> initialize)
            {
                context.InvokeLayers(lvo, initialize);
                Assert.True(
                    expectedFlags == (ushort)ReadOracleLayerFlags(
                        context.Bus, layer),
                    $"{operation} changed no-damage Layer.Flags.");
            }
        }
        finally
        {
            context.Machine.Dispose();
        }
    }

    [Fact]
    public void ClassicVectorFamiliesMatchNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
            return;

        var native = CreateNativeOracle(rom);
        var copperStart = CreateCopperStartOracle();
        using var deterministicNativeGraphics =
            native.InstallDeterministicNativeBltBitMapLeaf();

        Assert.Equal((ushort)40,
            ReadOracleLibraryVersion(native.Bus, native.LayersBase));
        Assert.Equal(LayersAbiConstants.MorphOsV52,
            ReadOracleLibraryVersion(copperStart.Bus, copperStart.LayersBase));
        for (var lvo = LayersLvo.InitLayers; lvo >= LayersLvo.DoHookClipRects; lvo -= 6)
        {
            Assert.Equal((ushort)0x4EF9, native.Bus.ReadWord(
                unchecked((uint)((int)native.LayersBase + lvo))));
            Assert.False(native.Bus.HasHostGateway(
                unchecked((uint)((int)native.LayersBase + lvo))));
            Assert.True(copperStart.Bus.HasHostGateway(
                unchecked((uint)((int)copperStart.LayersBase + lvo))));
        }

        var nativeTrace = RunClassicTrace(native);
        deterministicNativeGraphics.Dispose();
        var copperStartTrace = RunClassicTrace(copperStart);
        var nativeAllocationFailureTrace = TraceNativeAllocationFailureMatrix(native);
        _output.WriteLine("Native allocation-failure trace:\n" +
            string.Join("\n", nativeAllocationFailureTrace));
        var copperStartAllocationFailureTrace =
            TraceNativeAllocationFailureMatrix(copperStart);

        _output.WriteLine("Native trace:\n" + string.Join("\n", nativeTrace));
        _output.WriteLine("CopperStart trace:\n" + string.Join("\n", copperStartTrace));
        _output.WriteLine("CopperStart allocation-failure trace:\n" +
            string.Join("\n", copperStartAllocationFailureTrace));
        _output.WriteLine("Native delegated graphics calls:\n" +
            string.Join("\n", native.Diagnostics));
        _output.WriteLine("CopperStart raw diagnostics:\n" +
            string.Join("\n", copperStart.Diagnostics));
        _output.WriteLine(
            "Native deterministic BltBitMap leaf calls=" +
            deterministicNativeGraphics.CallCount);
        _output.WriteLine(
            "Native deterministic BltBitMap leaf trace:\n" +
            deterministicNativeGraphics.DescribeCalls());
        Assert.True(deterministicNativeGraphics.CallCount > 0);

        var mismatchIndexes = Enumerable.Range(
                0,
                Math.Max(nativeTrace.Length, copperStartTrace.Length))
            .Where(index => index >= nativeTrace.Length ||
                index >= copperStartTrace.Length ||
                nativeTrace[index] != copperStartTrace[index])
            .ToArray();
        var mismatchFamilies = mismatchIndexes
            .SelectMany(index => new[]
            {
                index < nativeTrace.Length ? TraceFamily(nativeTrace[index]) : "<missing-native>",
                index < copperStartTrace.Length ? TraceFamily(copperStartTrace[index]) : "<missing-copperstart>"
            })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static family => family, StringComparer.Ordinal);
        var nativeByIdentity = nativeTrace
            .GroupBy(TraceIdentity, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToArray(),
                StringComparer.Ordinal);
        var copperStartByIdentity = copperStartTrace
            .GroupBy(TraceIdentity, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToArray(),
                StringComparer.Ordinal);
        var semanticMismatchCount = 0;
        var semanticMismatchFamilies = new HashSet<string>(StringComparer.Ordinal);
        var semanticMismatches = new List<(
            string Identity,
            string Native,
            string CopperStart)>();
        foreach (var identity in nativeByIdentity.Keys
                     .Concat(copperStartByIdentity.Keys)
                     .Distinct(StringComparer.Ordinal))
        {
            nativeByIdentity.TryGetValue(identity, out var nativeRows);
            copperStartByIdentity.TryGetValue(identity, out var copperStartRows);
            nativeRows ??= [];
            copperStartRows ??= [];
            var rowCount = Math.Max(nativeRows.Length, copperStartRows.Length);
            for (var index = 0; index < rowCount; index++)
            {
                if (index < nativeRows.Length &&
                    index < copperStartRows.Length &&
                    nativeRows[index] == copperStartRows[index])
                {
                    continue;
                }
                semanticMismatchCount++;
                semanticMismatchFamilies.Add(TraceFamily(identity));
                semanticMismatches.Add((
                    identity,
                    index < nativeRows.Length ? nativeRows[index] : "<missing>",
                    index < copperStartRows.Length ? copperStartRows[index] : "<missing>"));
            }
        }
        _output.WriteLine(
            $"Differential positional-mismatch-count={mismatchIndexes.Length}; " +
            $"semantic-row-mismatch-count={semanticMismatchCount}; " +
            $"native-rows={nativeTrace.Length}; copperstart-rows={copperStartTrace.Length}; " +
            $"positional-families=[{string.Join(',', mismatchFamilies)}]; " +
            "semantic-families=[" +
            string.Join(',', semanticMismatchFamilies.OrderBy(
                static family => family,
                StringComparer.Ordinal)) + "]");
        _output.WriteLine(
            "Differential semantic-mismatch-identities=[" +
            string.Join(',', semanticMismatches
                .Select(static mismatch => mismatch.Identity)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static identity => identity, StringComparer.Ordinal)) +
            "]");
        var normalizedNative = nativeTrace
            .Select(NormalizeSemanticObservation)
            .GroupBy(TraceIdentity, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(),
                StringComparer.Ordinal);
        var normalizedCopperStart = copperStartTrace
            .Select(NormalizeSemanticObservation)
            .GroupBy(TraceIdentity, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(),
                StringComparer.Ordinal);
        var normalizedMismatchIdentities = new List<string>();
        foreach (var identity in normalizedNative.Keys
                     .Concat(normalizedCopperStart.Keys)
                     .Distinct(StringComparer.Ordinal))
        {
            normalizedNative.TryGetValue(identity, out var nativeRows);
            normalizedCopperStart.TryGetValue(identity, out var copperStartRows);
            nativeRows ??= [];
            copperStartRows ??= [];
            if (!nativeRows.SequenceEqual(copperStartRows, StringComparer.Ordinal))
                normalizedMismatchIdentities.Add(identity);
        }
        _output.WriteLine(
            $"Differential semantic-policy-normalized-count=" +
            $"{normalizedMismatchIdentities.Count}; identities=[" +
            string.Join(',', normalizedMismatchIdentities.OrderBy(
                static identity => identity, StringComparer.Ordinal)) + "]");
        foreach (var example in semanticMismatches
                     .GroupBy(static mismatch => TraceFamily(mismatch.Identity),
                         StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal)
                     .Select(static group => group.First()))
        {
            _output.WriteLine(
                $"Differential semantic-example family={TraceFamily(example.Identity)}; " +
                $"identity={example.Identity}\n" +
                $"native={example.Native}\n" +
                $"copperstart={example.CopperStart}");
        }

        Assert.Equal(
            nativeTrace.Select(NormalizeSemanticObservation),
            copperStartTrace.Select(NormalizeSemanticObservation));
    }

    [Fact]
    public void NonemptyDamageRefreshWithClipRegionMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine(
                "Native refresh differential NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        var copperStart = CreateCopperStartOracle();
        string[] copperStartTrace;
        try
        {
            copperStartTrace = TraceNonemptyDamageRefresh(copperStart);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        var native = CreateNativeOracle(rom);
        try
        {
            Assert.Equal((ushort)40,
                ReadOracleLibraryVersion(native.Bus, native.LayersBase));
            var nativeMemory = new LayersTestGuestMemory(native.Bus);
            _output.WriteLine(
                "G222 configured ROM execution: " +
                $"Kickstart={BigEndian.ReadUInt16(rom, 12, "version")}; " +
                $"layers.library={ReadOracleLibraryVersion(native.Bus, native.LayersBase)}." +
                $"{LayersLibraryCodec.ReadRevision(ref nativeMemory, APTR.FromPointer(native.LayersBase))}; " +
                $"graphics.library={ReadOracleLibraryVersion(native.Bus, native.GraphicsBase)}." +
                $"{LayersLibraryCodec.ReadRevision(ref nativeMemory, APTR.FromPointer(native.GraphicsBase))}");
            foreach (var lvo in new[] { LayersLvo.BeginUpdate, LayersLvo.EndUpdate })
                Assert.False(native.Bus.HasHostGateway(
                    unchecked((uint)((int)native.LayersBase + lvo))));
            using var deterministicNativeGraphics =
                native.InstallDeterministicNativeBltBitMapLeaf();
            var nativeTrace = TraceNonemptyDamageRefresh(native);
            // Rows already use semantic pixel unions and contain no provider
            // counters or private addresses. Compare every observation directly.
            Assert.Equal(nativeTrace, copperStartTrace);
        }
        finally
        {
            native.Machine.Dispose();
        }
    }

    private string[] TraceNonemptyDamageRefresh(OracleContext context)
    {
        // Damage is produced by public Layers operations. Only ClipRegion is
        // caller-owned; do not replace the library's DamageList with a synthetic
        // Region or rely on private descriptor/ownership layout in either oracle.
        const int width = 16;
        const int height = 16;
        var trace = new List<string>();
        var layerInfo = context.NewLayerInfo();
        // This probe executes native RectFill through the hardware blitter.
        // PUBLIC memory alone may be allocated in the A500's pseudo-fast RAM,
        // whose CPU address is not the blitter's chip-DMA destination address.
        var display = context.CreatePlanarBitMap(
            width, height, 1, 0, planeMemoryFlags: Exec.MemoryFlags.Chip);
        _output.WriteLine(
            $"G222 {(context.Native ? "Native" : "CopperStart")} plane=" +
            $"0x{display.Planes[0]:X8}; DMA-address=" +
            $"0x{context.Bus.MaskChipDmaAddress(display.Planes[0]):X8}; " +
            $"chip-bytes={context.Machine.Options.ChipRamSize}; " +
            $"plane-bytes={display.BytesPerRow * display.Height}");
        Assert.Equal(display.Planes[0],
            context.Bus.MaskChipDmaAddress(display.Planes[0]));
        Assert.InRange(display.Planes[0], 1u,
            checked((uint)(context.Machine.Options.ChipRamSize -
                display.BytesPerRow * display.Height)));
        var layer = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer, layerInfo, display.Address, 0,
            LayerCreationFlags.Simple, 0, 0, width - 1, height - 1);
        var cover = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer, layerInfo, display.Address, 0,
            LayerCreationFlags.Simple, 4, 3, 11, 10);
        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = cover);
        context.WaitForBlitterIdle();
        Assert.False(context.Bus.Blitter.Busy);

        var damageBefore = ReadDamageCoverage();
        Assert.NotEmpty(damageBefore);
        Assert.True((ReadOracleLayerFlags(context.Bus, layer) &
            LayerFlags.Refresh) != 0);
        var callerRegion = context.CreateSingleRectangleRegion(7, 0, 13, 7);
        var callerRectangle = ReadOracleRegionFirst(context.Bus, callerRegion);
        var callerRegionBytes = ReadBytes(callerRegion, checked((int)Region.Size));
        var callerRectangleBytes = ReadBytes(
            callerRectangle, checked((int)RegionRectangle.Size));
        var oldRegion = context.InvokeLayers(LayersLvo.InstallClipRegion, state =>
        {
            state.A[0] = layer;
            state.A[1] = callerRegion;
        }).D[0];
        Assert.Equal(0u, oldRegion);
        var stableCoverage = ReadVisibleCoverage();
        var pendingDamage = ReadDamageCoverage();
        var expectedProjection = stableCoverage.Intersect(pendingDamage)
            .OrderBy(static pixel => pixel).ToArray();
        Assert.NotEmpty(expectedProjection);
        Assert.True(expectedProjection.Length < pendingDamage.Length,
            "The caller ClipRegion must exclude some pending damage.");
        var rastPort = ReadOracleLayerPointer(
            context.Bus, layer, OracleLayerPointer.RastPort);
        context.InvokeGraphics((int)GraphicsLvo.SetAPen, state =>
        {
            state.A[1] = rastPort;
            state.D[0] = 1;
        });
        for (var offset = 0u; offset < display.BytesPerRow * display.Height; offset++)
            context.Bus.WriteByte(display.Planes[0] + offset, 0, 0);
        Capture("before-update");

        BeginAndCapture("first-begin");
        var activeHead = ReadOracleLayerPointer(
            context.Bus, layer, OracleLayerPointer.ClipRect);
        var activeFlags = ReadOracleLayerFlags(context.Bus, layer);
        var activeDamage = ReadOracleLayerPointer(
            context.Bus, layer, OracleLayerPointer.DamageList);
        context.InvokeGraphics((int)GraphicsLvo.RectFill, state =>
        {
            state.A[1] = rastPort;
            state.D[0] = 0;
            state.D[1] = 0;
            state.D[2] = width - 1;
            state.D[3] = height - 1;
        });
        context.WaitForBlitterIdle();
        Capture("draw");
        Assert.False(context.Bus.Blitter.Busy);
        Assert.Equal(expectedProjection, ReadSetPixels());
        Assert.Equal(activeHead, ReadOracleLayerPointer(
            context.Bus, layer, OracleLayerPointer.ClipRect));
        Assert.Equal(activeFlags, ReadOracleLayerFlags(context.Bus, layer));
        Assert.Equal(activeDamage, ReadOracleLayerPointer(
            context.Bus, layer, OracleLayerPointer.DamageList));
        Assert.Equal(pendingDamage, ReadDamageCoverage());

        context.InvokeLayers(LayersLvo.EndUpdate, state =>
        {
            state.A[0] = layer;
            state.D[0] = 0;
        });
        Capture("partial-end");
        Assert.Equal(stableCoverage, ReadVisibleCoverage());
        Assert.Equal(pendingDamage, ReadDamageCoverage());
        Assert.True((ReadOracleLayerFlags(context.Bus, layer) &
            LayerFlags.Updating) == 0);
        Assert.True((ReadOracleLayerFlags(context.Bus, layer) &
            LayerFlags.Refresh) != 0);

        BeginAndCapture("retry-begin");
        context.InvokeLayers(LayersLvo.EndUpdate, state =>
        {
            state.A[0] = layer;
            state.D[0] = 1;
        });
        Capture("complete-end");
        Assert.Equal(stableCoverage, ReadVisibleCoverage());
        Assert.Empty(ReadDamageCoverage());
        Assert.True((ReadOracleLayerFlags(context.Bus, layer) &
            LayerFlags.Updating) == 0);
        // EndUpdate retires damage, not the caller's refresh notification.
        // Native V40 preserves this bit; Intuition EndRefresh clears it.
        Assert.True((ReadOracleLayerFlags(context.Bus, layer) &
            LayerFlags.Refresh) != 0);
        Assert.Equal(expectedProjection, ReadSetPixels());

        var removedRegion = context.InvokeLayers(LayersLvo.InstallClipRegion, state =>
        {
            state.A[0] = layer;
            state.A[1] = 0;
        }).D[0];
        Assert.Equal(callerRegion, removedRegion);
        AssertCallerRegionUnchanged();
        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = layer);
        context.DisposeLayerInfo(layerInfo);
        context.Free(callerRectangle, RegionRectangle.Size);
        context.Free(callerRegion, Region.Size);
        context.FreePlanarBitMap(display);
        return trace.ToArray();

        void BeginAndCapture(string phase)
        {
            var result = context.InvokeLayers(LayersLvo.BeginUpdate,
                state => state.A[0] = layer).D[0];
            Capture(phase);
            Assert.NotEqual(0u, result);
            Assert.True((ReadOracleLayerFlags(context.Bus, layer) &
                LayerFlags.Updating) != 0);
            Assert.Equal(expectedProjection, ReadVisibleCoverage());
            Assert.Equal(pendingDamage, ReadDamageCoverage());
        }

        void Capture(string phase)
        {
            AssertCallerRegionUnchanged();
            Assert.Equal(callerRegion, ReadOracleLayerPointer(
                context.Bus, layer, OracleLayerPointer.ClipRegion));
            const LayerFlags publicMask = LayerFlags.Simple | LayerFlags.Smart |
                LayerFlags.Super | LayerFlags.Backdrop | LayerFlags.Updating |
                LayerFlags.Refresh | LayerFlags.ClipRectsLost;
            var row = $"refresh:{phase}:flags=" +
                $"{ReadOracleLayerFlags(context.Bus, layer) & publicMask}:" +
                $"damage=[{string.Join(',', ReadDamageCoverage())}]:" +
                $"visible=[{string.Join(',', ReadVisibleCoverage())}]:" +
                $"pixels=[{string.Join(',', ReadSetPixels())}]:caller-region=unchanged";
            trace.Add(row);
            _output.WriteLine((context.Native ? "Native " : "CopperStart ") + row);
        }

        void AssertCallerRegionUnchanged()
        {
            Assert.Equal(callerRegionBytes,
                ReadBytes(callerRegion, checked((int)Region.Size)));
            Assert.Equal(callerRectangleBytes,
                ReadBytes(callerRectangle, checked((int)RegionRectangle.Size)));
        }

        byte[] ReadBytes(uint address, int count) => Enumerable.Range(0, count)
            .Select(offset => context.Bus.ReadByte(address + checked((uint)offset)))
            .ToArray();

        int[] ReadSetPixels()
        {
            var pixels = new List<int>();
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    if ((context.Bus.ReadByte(display.Planes[0] +
                        checked((uint)(y * display.BytesPerRow + x / 8))) &
                        (0x80 >> (x & 7))) != 0)
                        pixels.Add(y * width + x);
            return pixels.ToArray();
        }

        int[] ReadDamageCoverage()
        {
            var coverage = new SortedSet<int>();
            var region = ReadOracleLayerPointer(
                context.Bus, layer, OracleLayerPointer.DamageList);
            if (region == 0)
                return [];
            var bounds = ReadOracleRegionBounds(context.Bus, region);
            var seen = new HashSet<uint>();
            for (var node = ReadOracleRegionFirst(context.Bus, region); node != 0;
                 node = ReadOracleRegionRectanglePointer(context.Bus, node, previous: false))
            {
                Assert.True(seen.Count < 256 && seen.Add(node),
                    "DamageList must be a bounded acyclic Region chain.");
                var rectangle = ReadOracleRegionRectangleBounds(context.Bus, node);
                AddRectangle(coverage, bounds.MinX + rectangle.MinX,
                    bounds.MinY + rectangle.MinY, bounds.MinX + rectangle.MaxX,
                    bounds.MinY + rectangle.MaxY);
            }
            return coverage.ToArray();
        }

        int[] ReadVisibleCoverage()
        {
            var coverage = new SortedSet<int>();
            var seen = new HashSet<uint>();
            for (var node = ReadOracleLayerPointer(context.Bus, layer,
                     OracleLayerPointer.ClipRect); node != 0;
                 node = ReadOracleClipRectPointer(context.Bus, node, OracleClipRectPointer.Next))
            {
                Assert.True(seen.Count < 256 && seen.Add(node),
                    "ClipRects must be a bounded acyclic chain.");
                if (ReadOracleClipRectPointer(context.Bus, node,
                    OracleClipRectPointer.ObscuringLayer) != 0)
                    continue;
                var rectangle = ReadOracleClipRectBounds(context.Bus, node);
                AddRectangle(coverage, rectangle.MinX, rectangle.MinY,
                    rectangle.MaxX, rectangle.MaxY);
            }
            return coverage.ToArray();
        }

        static void AddRectangle(SortedSet<int> coverage,
            int minX, int minY, int maxX, int maxY)
        {
            Assert.InRange(minX, 0, width - 1);
            Assert.InRange(minY, 0, height - 1);
            Assert.InRange(maxX, minX, width - 1);
            Assert.InRange(maxY, minY, height - 1);
            for (var y = minY; y <= maxY; y++)
                for (var x = minX; x <= maxX; x++)
                    coverage.Add(y * width + x);
        }
    }

    private static string TraceFamily(string row)
    {
        var separator = row.IndexOf(':');
        return separator < 0 ? row : row[..separator];
    }

    private static string TraceIdentity(string row)
    {
        var separator = row.IndexOf('=');
        return separator < 0 ? row : row[..separator];
    }

    private static string NormalizeProviderInstrumentation(string row)
        => Regex.Replace(
            row,
            ":gfx=[^:]*:order=[^:]*",
            ":gfx=<provider>",
            RegexOptions.CultureInvariant);

    private static string NormalizeSemanticObservation(string row)
    {
        row = NormalizeProviderInstrumentation(row);
        // InitLayers is declared VOID. D0 is caller-clobber observation in the
        // native resident, while CopperStart deliberately preserves it.
        if (row.StartsWith("coverage-probe:init:", StringComparison.Ordinal))
            row = Regex.Replace(row, ":d0=[0-9]+$", ":d0=<void>");
        if (row.StartsWith("sort-matrix:", StringComparison.Ordinal))
            row = Regex.Replace(
                row,
                ":preserved-d0=-?[0-9]+:",
                ":preserved-d0=<void>:");
        // ScrollLayer is also declared VOID. V40 clobbers D0 differently by
        // direction, while the installed CopperStart gateway preserves the
        // caller frame. Scroll state, complement topology, and provider copy
        // events remain independently compared by these rows.
        if (row.StartsWith("super-scroll-matrix:", StringComparison.Ordinal))
            row = Regex.Replace(row, ":d0=-?[0-9]+:", ":d0=<void>:");
        // NDK exposes ClipRect.Next as the public chain. Offset 4 is the
        // reserved/unused link, so retain its raw trace but exclude it from
        // semantic comparison. Public Next validity remains in next-valid.
        // Require the ClipRect bitmap/obscurer fields before its reserved
        // predecessor so this cannot consume a RegionRectangle delimiter and
        // the following rectangle's bounds.
        row = Regex.Replace(
            row,
            @"\[count=([0-9]+):links=(?:True|False):",
            "[count=$1:",
            RegexOptions.CultureInvariant);
        return Regex.Replace(
            row,
            @"(:[NDUO]:[O-]):prev=[^:\]]+:",
            "$1:",
            RegexOptions.CultureInvariant);
    }

    private static string[] TraceNativeAllocationFailureMatrix(OracleContext context)
    {
        var trace = new List<string>();

        var baselineLayerInfo = 0u;
        int newLayerInfoAllocations;
        using (var fault = context.InstallOrdinalFault(
                   context.ExecBase,
                   ExecLvo.AllocMem,
                   failOrdinal: 0))
        {
            baselineLayerInfo = context.InvokeLayers(LayersLvo.NewLayerInfo).D[0];
            newLayerInfoAllocations = fault.Count;
        }
        Assert.NotEqual(0u, baselineLayerInfo);
        context.DisposeLayerInfo(baselineLayerInfo);
        context.ResetPortableLayersAfterFaultCase();
        trace.Add($"alloc-fault:new-layer-info:baseline-calls={newLayerInfoAllocations}");
        for (var ordinal = 1; ordinal <= newLayerInfoAllocations; ordinal++)
        {
            uint result;
            int calls;
            using (var fault = context.InstallOrdinalFault(
                       context.ExecBase,
                       ExecLvo.AllocMem,
                       ordinal))
            {
                result = context.InvokeLayers(LayersLvo.NewLayerInfo).D[0];
                calls = fault.Count;
            }
            trace.Add($"alloc-fault:new-layer-info:{ordinal}/{newLayerInfoAllocations}:" +
                $"calls={calls}:null={result == 0}");
            if (result != 0)
                context.DisposeLayerInfo(result);
            context.ResetPortableLayersAfterFaultCase();
        }

        TraceNativeFattenAllocationFailures(context, trace);
        TraceNativeCreateAllocationFailures(context, trace);
        TraceNativeCallbackAllocationFailures(context, trace);
        TraceNativeBackingBitMapAllocationFailures(context, trace);
        TraceNativeRepartitionAllocationFailures(context, trace);
        return trace.ToArray();
    }

    private static void TraceNativeFattenAllocationFailures(
        OracleContext context,
        List<string> trace)
    {
        var layerInfo = context.NewLayerInfo();
        uint baselineResult;
        int baselineCalls;
        using (var fault = context.InstallOrdinalFault(
                   context.ExecBase,
                   ExecLvo.AllocMem,
                   failOrdinal: 0))
        {
            baselineResult = context.InvokeLayers(
                LayersLvo.FattenLayerInfo,
                state => state.A[0] = layerInfo).D[0];
            baselineCalls = fault.Count;
        }
        if (baselineResult != 0)
        {
            context.InvokeLayers(
                LayersLvo.ThinLayerInfo,
                state => state.A[0] = layerInfo);
        }
        context.DisposeLayerInfo(layerInfo);
        context.ResetPortableLayersAfterFaultCase();
        trace.Add($"alloc-fault:fatten:baseline-calls={baselineCalls}:" +
            $"success={baselineResult != 0}");

        for (var ordinal = 1; ordinal <= baselineCalls; ordinal++)
        {
            layerInfo = context.NewLayerInfo();
            uint result;
            int calls;
            using (var fault = context.InstallOrdinalFault(
                       context.ExecBase,
                       ExecLvo.AllocMem,
                       ordinal))
            {
                result = context.InvokeLayers(
                    LayersLvo.FattenLayerInfo,
                    state => state.A[0] = layerInfo).D[0];
                calls = fault.Count;
            }
            trace.Add($"alloc-fault:fatten:{ordinal}/{baselineCalls}:" +
                $"calls={calls}:success={result != 0}");
            if (result != 0)
            {
                context.InvokeLayers(
                    LayersLvo.ThinLayerInfo,
                    state => state.A[0] = layerInfo);
            }
            context.DisposeLayerInfo(layerInfo);
            context.ResetPortableLayersAfterFaultCase();
        }
    }

    private static void TraceNativeCreateAllocationFailures(
        OracleContext context,
        List<string> trace)
    {
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(16, 16, 1, 0x93);
        uint baselineLayer;
        int baselineCalls;
        using (var fault = context.InstallOrdinalFault(
                   context.ExecBase,
                   ExecLvo.AllocMem,
                   failOrdinal: 0))
        {
            baselineLayer = InvokeCreateSimple(context, layerInfo, display.Address);
            baselineCalls = fault.Count;
        }
        Assert.NotEqual(0u, baselineLayer);
        context.InvokeLayers(
            LayersLvo.DeleteLayer,
            state => state.A[1] = baselineLayer);
        trace.Add($"alloc-fault:create:baseline-calls={baselineCalls}");

        for (var ordinal = 1; ordinal <= baselineCalls; ordinal++)
        {
            uint result;
            int calls;
            using (var fault = context.InstallOrdinalFault(
                       context.ExecBase,
                       ExecLvo.AllocMem,
                       ordinal))
            {
                result = InvokeCreateSimple(context, layerInfo, display.Address);
                calls = fault.Count;
            }
            var top = ReadOracleTopLayer(context.Bus, layerInfo);
            trace.Add($"alloc-fault:create:{ordinal}/{baselineCalls}:calls={calls}:" +
                $"null={result == 0}:top-null={top == 0}");
            if (result != 0)
            {
                context.InvokeLayers(
                    LayersLvo.DeleteLayer,
                    state => state.A[1] = result);
            }
        }
        context.DisposeLayerInfo(layerInfo);
        context.FreePlanarBitMap(display);
        context.ResetPortableLayersAfterFaultCase();
    }

    private static uint InvokeCreateSimple(
        OracleContext context,
        uint layerInfo,
        uint bitMap)
        => context.InvokeLayers(LayersLvo.CreateUpfrontLayer, state =>
        {
            state.A[0] = layerInfo;
            state.A[1] = bitMap;
            state.D[0] = 0;
            state.D[1] = 0;
            state.D[2] = 15;
            state.D[3] = 15;
            state.D[4] = (uint)LayerCreationFlags.Simple;
        }).D[0];

    private static void TraceNativeRepartitionAllocationFailures(
        OracleContext context,
        List<string> trace)
    {
        var baseline = CreateRepartitionFixture(context);
        uint baselineResult;
        int baselineCalls;
        using (var fault = context.InstallOrdinalFault(
                   context.ExecBase,
                   ExecLvo.AllocMem,
                   failOrdinal: 0))
        {
            baselineResult = context.InvokeLayers(LayersLvo.MoveLayer, state =>
            {
                state.A[1] = baseline.Blocker;
                state.D[0] = 1;
                state.D[1] = 0;
            }).D[0];
            baselineCalls = fault.Count;
        }
        trace.Add($"alloc-fault:repartition:baseline-calls={baselineCalls}:" +
            $"success={baselineResult != 0}:" +
            $"bounds={FormatOracleRectangle(ReadOracleLayerBounds(context.Bus, baseline.Blocker))}");
        DestroyRepartitionFixture(context, baseline);
        context.ResetPortableLayersAfterFaultCase();

        for (var ordinal = 1; ordinal <= baselineCalls; ordinal++)
        {
            var fixture = CreateRepartitionFixture(context);
            uint result;
            int calls;
            bool reachedSentinel;
            uint finalProgramCounter;
            using (var fault = context.InstallOrdinalFault(
                       context.ExecBase,
                       ExecLvo.AllocMem,
                       ordinal))
            {
                var outcome = context.InvokeLayersAllowingBudgetExhaustion(
                    LayersLvo.MoveLayer,
                    state =>
                    {
                        state.A[1] = fixture.Blocker;
                        state.D[0] = 1;
                        state.D[1] = 0;
                    });
                result = outcome.State.D[0];
                reachedSentinel = outcome.ReachedSentinel;
                finalProgramCounter = outcome.State.ProgramCounter;
                calls = fault.Count;
            }
            if (!reachedSentinel)
            {
                trace.Add($"alloc-fault:repartition:{ordinal}/{baselineCalls}:" +
                    $"calls={calls}:sentinel=False:pc=0x{finalProgramCounter:X8}");
                return;
            }
            trace.Add($"alloc-fault:repartition:{ordinal}/{baselineCalls}:" +
                $"calls={calls}:sentinel=True:success={result != 0}:" +
                $"bounds={FormatOracleRectangle(ReadOracleLayerBounds(context.Bus, fixture.Blocker))}:" +
                $"cr={DescribeClipRectOrder(context, fixture.Smart, [fixture.Blocker])}");
            DestroyRepartitionFixture(context, fixture);
            context.ResetPortableLayersAfterFaultCase();
        }
    }

    private static void TraceNativeBackingBitMapAllocationFailures(
        OracleContext context,
        List<string> trace)
    {
        var baseline = CreateRepartitionFixture(context);
        uint baselineResult;
        int baselineCalls;
        using (var fault = context.InstallOrdinalFault(
                   context.GraphicsBase,
                   GraphicsAllocBitMapLvo,
                   failOrdinal: 0))
        {
            baselineResult = context.InvokeLayers(LayersLvo.MoveLayer, state =>
            {
                state.A[1] = baseline.Blocker;
                state.D[0] = 1;
                state.D[1] = 0;
            }).D[0];
            baselineCalls = fault.Count;
        }
        trace.Add($"bitmap-fault:repartition:baseline-calls={baselineCalls}:" +
            $"success={baselineResult != 0}");
        DestroyRepartitionFixture(context, baseline);
        context.ResetPortableLayersAfterFaultCase();

        for (var ordinal = 1; ordinal <= baselineCalls; ordinal++)
        {
            var fixture = CreateRepartitionFixture(context);
            uint result;
            int calls;
            bool reachedSentinel;
            uint finalProgramCounter;
            using (var fault = context.InstallOrdinalFault(
                       context.GraphicsBase,
                       GraphicsAllocBitMapLvo,
                       ordinal))
            {
                var outcome = context.InvokeLayersAllowingBudgetExhaustion(
                    LayersLvo.MoveLayer,
                    state =>
                    {
                        state.A[1] = fixture.Blocker;
                        state.D[0] = 1;
                        state.D[1] = 0;
                    });
                result = outcome.State.D[0];
                reachedSentinel = outcome.ReachedSentinel;
                finalProgramCounter = outcome.State.ProgramCounter;
                calls = fault.Count;
            }
            trace.Add($"bitmap-fault:repartition:{ordinal}/{baselineCalls}:" +
                $"calls={calls}:sentinel={reachedSentinel}:" +
                (reachedSentinel
                    ? $"success={result != 0}:bounds=" +
                      FormatOracleRectangle(
                          ReadOracleLayerBounds(context.Bus, fixture.Blocker))
                    : $"pc=0x{finalProgramCounter:X8}"));
            if (!reachedSentinel)
                return;
            DestroyRepartitionFixture(context, fixture);
            context.ResetPortableLayersAfterFaultCase();
        }
    }

    private static RepartitionFixture CreateRepartitionFixture(OracleContext context)
    {
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(32, 16, 1, 0xA1);
        var smart = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Smart,
            0,
            0,
            31,
            15);
        var blocker = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            6,
            4,
            13,
            11);
        return new RepartitionFixture(layerInfo, display, smart, blocker);
    }

    private static void DestroyRepartitionFixture(
        OracleContext context,
        RepartitionFixture fixture)
    {
        context.InvokeLayers(
            LayersLvo.DeleteLayer,
            state => state.A[1] = fixture.Blocker);
        context.InvokeLayers(
            LayersLvo.DeleteLayer,
            state => state.A[1] = fixture.Smart);
        context.DisposeLayerInfo(fixture.LayerInfo);
        context.FreePlanarBitMap(fixture.Display);
    }

    private static void TraceNativeCallbackAllocationFailures(
        OracleContext context,
        List<string> trace)
    {
        var baseline = CreateCallbackFailureFixture(context);
        uint baselineResult;
        int baselineCalls;
        using (var fault = context.InstallOrdinalFault(
                   context.ExecBase,
                   ExecLvo.AllocMem,
                   failOrdinal: 0))
        {
            baselineResult = InvokeBehindHookCreate(context, baseline);
            baselineCalls = fault.Count;
        }
        trace.Add($"alloc-fault:callback-create:baseline-calls={baselineCalls}:" +
            $"success={baselineResult != 0}:callbacks={baseline.Hook.CallbackCount}");
        DestroyCallbackFailureFixture(context, baseline, baselineResult);
        context.ResetPortableLayersAfterFaultCase();

        for (var ordinal = 1; ordinal <= baselineCalls; ordinal++)
        {
            var fixture = CreateCallbackFailureFixture(context);
            uint result;
            int calls;
            using (var fault = context.InstallOrdinalFault(
                       context.ExecBase,
                       ExecLvo.AllocMem,
                       ordinal))
            {
                try
                {
                    result = InvokeBehindHookCreate(context, fixture);
                }
                catch (Exception error)
                {
                    // The matrix is printed only after completion. Preserve
                    // the exact failed ordinal and retained callback state if
                    // the guest cannot return, without treating that as an
                    // expected allocation-failure outcome.
                    var state = context.Machine.Cpu.State;
                    var diagnostics = string.Join(" | ", context.Boot.Diagnostics
                        .Where(static item => item.Code == "AMIGA_BOOT_LAYERS_CALLBACK_RETURN")
                        .Select(static item => item.Message));
                    throw new InvalidOperationException(
                        $"Callback allocation matrix failed: native={context.Native}, " +
                        $"failOrdinal={ordinal}/{baselineCalls}, allocations={fault.Count}, " +
                        $"callbacks={fixture.Hook.CallbackCount}, " +
                        $"fault={context.Boot.CopperStartLayersLastCallbackReturnFaultForTest}, " +
                        $"pending={context.Boot.CopperStartLayersHasPendingCallbackForTest}, " +
                        $"pc=0x{state.ProgramCounter:X8}, sp=0x{state.A[7]:X8}, " +
                        $"usp=0x{state.UserStackPointer:X8}, ssp=0x{state.SupervisorStackPointer:X8}, " +
                        $"diagnostics=[{diagnostics}]", error);
                }
                calls = fault.Count;
            }
            var top = ReadOracleTopLayer(context.Bus, fixture.LayerInfo);
            trace.Add($"alloc-fault:callback-create:{ordinal}/{baselineCalls}:" +
                $"calls={calls}:success={result != 0}:callbacks={fixture.Hook.CallbackCount}:" +
                $"top-is-blocker={top == fixture.Blocker}");
            DestroyCallbackFailureFixture(context, fixture, result);
            context.ResetPortableLayersAfterFaultCase();
        }
    }

    private static CallbackFailureFixture CreateCallbackFailureFixture(
        OracleContext context)
    {
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(32, 16, 1, 0xB7);
        var blocker = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            6,
            4,
            14,
            10);
        var hook = context.CreateCallbackProbe(display.Address, 0);
        return new CallbackFailureFixture(layerInfo, display, blocker, hook);
    }

    private static uint InvokeBehindHookCreate(
        OracleContext context,
        CallbackFailureFixture fixture)
        => context.InvokeLayers(LayersLvo.CreateBehindHookLayer, state =>
        {
            state.A[0] = fixture.LayerInfo;
            state.A[1] = fixture.Display.Address;
            state.A[3] = fixture.Hook.Hook;
            state.D[0] = 1;
            state.D[1] = 2;
            state.D[2] = 20;
            state.D[3] = 12;
            state.D[4] = (uint)LayerCreationFlags.Simple;
        }).D[0];

    private static void DestroyCallbackFailureFixture(
        OracleContext context,
        CallbackFailureFixture fixture,
        uint createdLayer)
    {
        if (createdLayer != 0)
        {
            context.InvokeLayers(
                LayersLvo.DeleteLayer,
                state => state.A[1] = createdLayer);
        }
        context.InvokeLayers(
            LayersLvo.DeleteLayer,
            state => state.A[1] = fixture.Blocker);
        context.DisposeLayerInfo(fixture.LayerInfo);
        fixture.Hook.Dispose();
        context.FreePlanarBitMap(fixture.Display);
    }

    private static string[] RunClassicTrace(OracleContext context)
    {
        var trace = new List<string>();
        var opened = context.Invoke(context.LayersBase, -6);
        trace.Add($"open:self={opened.D[0] == context.LayersBase}");
        var closed = context.Invoke(context.LayersBase, -12);
        trace.Add($"close:result-zero={closed.D[0] == 0}");

        TraceCreationFlagMatrix(context, trace);
        TraceCreationMode(context, trace, LayerCreationFlags.Simple);
        TraceCreationMode(context, trace, LayerCreationFlags.Smart);
        TraceCreationMode(context, trace, LayerCreationFlags.Super);
        TraceCreateHookLayers(context, trace);
        TraceSuperCreateHookOrdering(context, trace);
        TraceSuperScrollBoundaryMatrix(context, trace);
        TraceClipRectSplitOrderMatrix(context, trace);
        TraceSortLayerCrMatrix(context, trace);
        TraceOverlapMode(context, trace, LayerCreationFlags.Simple);
        TraceOverlapMode(context, trace, LayerCreationFlags.Smart);
        TraceOverlapMode(context, trace, LayerCreationFlags.Super);
        TraceTopologyRefreshHooksAndQueries(context, trace);
        TraceFailureSurface(context, trace);
        TraceCoverageOnlyVectors(context, trace);
        TraceClassicCoverageLedger(context, trace);
        return trace.ToArray();
    }

    private static void TraceCoverageOnlyVectors(
        OracleContext context,
        List<string> trace)
    {
        var initialized = context.Allocate(LayerInfo.Size);
        var init = context.InvokeLayers(LayersLvo.InitLayers,
            state =>
            {
                state.A[0] = initialized;
                state.D[0] = 0x1357_9BDF;
            });
        trace.Add($"coverage-probe:init:sentinel=True:input-d0=324508639:" +
            $"d0={init.D[0]}");
        context.DisposeLayerInfo(initialized);

        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(16, 8, 1, 0x6A);
        var behind = context.CreateLayer(
            LayersLvo.CreateBehindLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            0,
            0,
            15,
            7);
        trace.Add("coverage-probe:create-behind:sentinel=True:" +
            DescribeLayer(context, behind, display.Address, 0));
        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = behind);
        context.DisposeLayerInfo(layerInfo);

        layerInfo = context.NewLayerInfo();
        display = context.CreatePlanarBitMap(16, 8, 1, 0x71);
        var smart = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Smart,
            0,
            0,
            15,
            7);
        var blocker = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            4,
            0,
            11,
            7);
        var obscured = ReadOracleLayerPointer(
            context.Bus, smart, OracleLayerPointer.ClipRect);
        while (obscured != 0 && ReadOracleClipRectPointer(
                   context.Bus, obscured, OracleClipRectPointer.ObscuringLayer) == 0)
        {
            obscured = ReadOracleClipRectPointer(
                context.Bus, obscured, OracleClipRectPointer.Next);
        }
        Assert.NotEqual(0u, obscured);
        var rastPort = ReadOracleLayerPointer(
            context.Bus, smart, OracleLayerPointer.RastPort);
        context.InvokeLayers(LayersLvo.LockLayer,
            state => state.A[1] = smart);
        var swap = context.InvokeLayers(
            LayersLvo.SwapBitsRastPortClipRect,
            state =>
            {
                state.A[0] = rastPort;
                state.A[1] = obscured;
            });
        context.InvokeLayers(LayersLvo.UnlockLayer,
            state => state.A[0] = smart);
        trace.Add($"coverage-probe:swap:sentinel=True:d0={swap.D[0]}:" +
            $"cr={FormatOracleRectangle(ReadOracleClipRectBounds(context.Bus, obscured))}");
        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = blocker);
        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = smart);
        context.DisposeLayerInfo(layerInfo);
    }

    private static void TraceClassicCoverageLedger(
        OracleContext context,
        List<string> trace)
    {
        var classicCount = 0;
        for (var lvo = LayersLvo.InitLayers;
             lvo >= LayersLvo.DoHookClipRects;
             lvo -= 6)
        {
            classicCount++;
            var executed = context.ExecutedLayersLvos.Contains(lvo);
            trace.Add($"coverage:{ClassicLvoName((short)lvo)}({lvo}):" +
                $"sentinel={executed}");
            Assert.True(executed, $"Classic LVO {lvo} has no executed sentinel trace.");
        }
        Assert.Equal(32, classicCount);
    }

    private static string ClassicLvoName(short lvo)
        => typeof(LayersLvo)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .First(field => Convert.ToInt16(field.GetValue(null)) == lvo)
            .Name;

    private static void TraceCreationFlagMatrix(
        OracleContext context,
        List<string> trace)
    {
        // SUPER without bm2 enters native private error handling that does
        // not return in this early-resident harness.  Pin the documented
        // SMART|SUPER valid-bm2 edge in the normalized corpus; the broader
        // failure matrix is kept out of this shared oracle state.
        foreach (var rawFlags in new[] { 6 })
        {
            foreach (var withSuperBitMap in new[] { true })
            {
                var layerInfo = context.NewLayerInfo();
                var display = context.CreatePlanarBitMap(16, 8, 1, 0x41);
                var super = withSuperBitMap
                    ? context.CreatePlanarBitMap(16, 8, 1, 0xB2)
                    : default;
                var create = context.InvokeLayers(
                    LayersLvo.CreateUpfrontLayer,
                    state =>
                    {
                        state.A[0] = layerInfo;
                        state.A[1] = display.Address;
                        state.A[2] = super.Address;
                        state.D[0] = 0;
                        state.D[1] = 0;
                        state.D[2] = 15;
                        state.D[3] = 7;
                        state.D[4] = unchecked((uint)rawFlags);
                    });
                var layer = create.D[0];
                context.WaitForBlitterIdle();
                var resultFlags = layer == 0
                    ? 0
                    : ReadOracleLayerFlags(context.Bus, layer);
                const LayerFlags modeMask = LayerFlags.Simple |
                    LayerFlags.Smart | LayerFlags.Super;
                trace.Add($"flag-matrix:{rawFlags}:bm2={withSuperBitMap}:" +
                    $"success={layer != 0}:flags={resultFlags & modeMask}:" +
                    $"super={(layer != 0 && ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.SuperBitMap) == super.Address)}");
                if (layer != 0)
                {
                    context.InvokeLayers(LayersLvo.DeleteLayer,
                        state => state.A[1] = layer);
                }
                context.DisposeLayerInfo(layerInfo);
            }
        }
    }

    private static void TraceCreateHookLayers(
        OracleContext context,
        List<string> trace)
    {
        TraceCreateHookLayer(
            context,
            trace,
            "upfront",
            LayersLvo.CreateUpfrontHookLayer,
            1,
            2,
            20,
            12,
            blocker: false,
            mode: LayerCreationFlags.Simple);
        TraceCreateHookLayer(
            context,
            trace,
            "behind-partial",
            LayersLvo.CreateBehindHookLayer,
            1,
            2,
            20,
            12,
            blocker: true,
            mode: LayerCreationFlags.Simple);
        TraceCreateHookLayer(
            context,
            trace,
            "behind-smart",
            LayersLvo.CreateBehindHookLayer,
            1,
            2,
            20,
            12,
            blocker: true,
            mode: LayerCreationFlags.Smart);
        TraceCreateHookLayer(
            context,
            trace,
            "upfront-offscreen",
            LayersLvo.CreateUpfrontHookLayer,
            -4,
            -3,
            12,
            9,
            blocker: false,
            mode: LayerCreationFlags.Simple);
    }

    private static void TraceCreateHookLayer(
        OracleContext context,
        List<string> trace,
        string label,
        int createLvo,
        int minX,
        int minY,
        int maxX,
        int maxY,
        bool blocker,
        LayerCreationFlags mode)
    {
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(32, 16, 1, 0x72);
        var blockingLayer = blocker
            ? context.CreateLayer(
                LayersLvo.CreateUpfrontLayer,
                layerInfo,
                display.Address,
                0,
                LayerCreationFlags.Simple,
                6,
                4,
                14,
                10)
            : 0;
        var hook = context.CreateCallbackProbe(display.Address, 0);
        var before = HashBitMap(context, display);
        var layer = context.InvokeLayers(createLvo, state =>
        {
            state.A[0] = layerInfo;
            state.A[1] = display.Address;
            state.A[2] = 0;
            state.A[3] = hook.Hook;
            state.D[0] = unchecked((uint)minX);
            state.D[1] = unchecked((uint)minY);
            state.D[2] = unchecked((uint)maxX);
            state.D[3] = unchecked((uint)maxY);
            state.D[4] = (uint)mode;
        }).D[0];
        if (layer == 0)
        {
            trace.Add($"create-hook:{label}:create-null:" + hook.Describe(0));
            if (blockingLayer != 0)
            {
                context.InvokeLayers(LayersLvo.DeleteLayer,
                    state => state.A[1] = blockingLayer);
            }
            context.DisposeLayerInfo(layerInfo);
            return;
        }
        var immediate = HashBitMap(context, display);
        var busyBeforeWait = context.Bus.Blitter.Busy;
        var graphicsDelta = context.LastGraphicsCallDelta;
        var graphicsOrder = context.LastGraphicsCallOrder;
        context.WaitForBlitterIdle();
        trace.Add($"create-hook-summary:{label}:" + hook.DescribeCompact() +
            $":gfx={graphicsDelta}:order={graphicsOrder}");
        trace.Add($"create-hook:{label}:" +
            hook.Describe(layer) + ":" +
            DescribeLayer(context, layer, display.Address, 0) +
            $":pixels={before}->{immediate}->{HashBitMap(context, display)}:" +
            $"busy-before-wait={busyBeforeWait}:gfx={graphicsDelta}:" +
            $"order={graphicsOrder}");
        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = layer);
        if (blockingLayer != 0)
        {
            context.InvokeLayers(LayersLvo.DeleteLayer,
                state => state.A[1] = blockingLayer);
        }
        context.DisposeLayerInfo(layerInfo);
    }

    private static void TraceSuperCreateHookOrdering(
        OracleContext context,
        List<string> trace)
    {
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(16, 8, 1, 0x24);
        var super = context.CreatePlanarBitMap(16, 8, 1, 0xA6);
        var hook = context.CreateCallbackProbe(
            display.Address,
            super.Address,
            writePattern: 0xE3);
        var before = HashBitMap(context, display);
        var superHash = HashBitMap(context, super);
        var create = context.InvokeLayers(
            LayersLvo.CreateUpfrontHookLayer,
            state =>
            {
                state.A[0] = layerInfo;
                state.A[1] = display.Address;
                state.A[2] = super.Address;
                state.A[3] = hook.Hook;
                state.D[0] = 0;
                state.D[1] = 0;
                state.D[2] = 15;
                state.D[3] = 7;
                state.D[4] = (uint)(LayerCreationFlags.Smart |
                    LayerCreationFlags.Super);
            });
        var layer = create.D[0];
        var immediate = HashBitMap(context, display);
        var busyBeforeWait = context.Bus.Blitter.Busy;
        var graphicsDelta = context.LastGraphicsCallDelta;
        var graphicsOrder = context.LastGraphicsCallOrder;
        context.WaitForBlitterIdle();
        var final = HashBitMap(context, display);
        var hookHash = HashRepeated(0xE3, display.BytesPerRow * display.Height);
        var winner = final == hookHash ? "hook" :
            final == superHash ? "super" : "other";
        trace.Add("create-hook:super-smart:" +
            $"success={layer != 0}:" + hook.Describe(layer) + ":" +
            $"pixels={before}->{immediate}->{final}:" +
            $"super={superHash}:hook={hookHash}:winner={winner}:" +
            $"busy-before-wait={busyBeforeWait}:gfx={graphicsDelta}:" +
            $"order={graphicsOrder}");
        if (layer != 0)
        {
            context.InvokeLayers(LayersLvo.DeleteLayer,
                state => state.A[1] = layer);
        }
        context.DisposeLayerInfo(layerInfo);
    }

    private static void TraceOverlapMode(
        OracleContext context,
        List<string> trace,
        LayerCreationFlags mode)
    {
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(32, 16, 1, 0x19);
        var super = mode == LayerCreationFlags.Super
            ? context.CreatePlanarBitMap(32, 16, 1, 0xD1)
            : default;
        var bottom = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            super.Address,
            mode,
            0,
            0,
            15,
            15);
        var blocker = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            4,
            4,
            11,
            11);
        trace.Add($"overlap-mode:{mode}:obscured=" +
            DescribeLayer(context, bottom, display.Address, super.Address));
        context.InvokeLayers(LayersLvo.ScrollLayer, state =>
        {
            state.A[1] = bottom;
            state.D[0] = 2;
            state.D[1] = 1;
        });
        context.WaitForBlitterIdle();
        trace.Add($"overlap-mode:{mode}:scroll-state=" +
            DescribeLayer(context, bottom, display.Address, super.Address));
        trace.Add($"overlap-mode:{mode}:hook-scroll=" +
            context.InvokeDoHookClipRects(
                bottom,
                display.Address,
                super.Address,
                4,
                4,
                11,
                11));
        if (mode == LayerCreationFlags.Super)
        {
            trace.Add($"overlap-mode:{mode}:hook-scroll-wide=" +
                context.InvokeDoHookClipRects(
                    bottom,
                    display.Address,
                    super.Address,
                    0,
                    0,
                    31,
                    15));
        }
        var beforeReveal = HashBitMap(context, display);
        var deleted = context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = blocker);
        trace.Add($"overlap-mode:{mode}:reveal-delete={deleted.D[0] != 0}:" +
            DescribeLayer(context, bottom, display.Address, super.Address) +
            $":pixels={beforeReveal}->{HashBitMap(context, display)}");
        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = bottom);
        context.DisposeLayerInfo(layerInfo);
    }

    private static void TraceSuperScrollBoundaryMatrix(
        OracleContext context,
        List<string> trace)
    {
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(32, 16, 1, 0x35);
        var super = context.CreatePlanarBitMap(32, 16, 1, 0xC4);
        var layer = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            super.Address,
            LayerCreationFlags.Super,
            0,
            0,
            15,
            15);
        trace.Add("super-scroll-matrix:bitmap=32x16:layer=0,0,15,15:" +
            DescribeSuperScrollState(context, layer, display.Address, super.Address));

        (short X, short Y, string Name)[] targets =
        [
            (0, 1, "partial-bottom"),
            (0, 16, "full-bottom"),
            (0, -1, "partial-top"),
            (0, -16, "full-top"),
            (17, 0, "partial-right"),
            (32, 0, "full-right"),
            (-1, 0, "partial-left"),
            (-16, 0, "full-left"),
            (0, 0, "origin")
        ];
        short currentX = 0;
        short currentY = 0;
        foreach (var target in targets)
        {
            var deltaX = checked((short)(target.X - currentX));
            var deltaY = checked((short)(target.Y - currentY));
            var result = context.InvokeLayers(LayersLvo.ScrollLayer, state =>
            {
                state.A[1] = layer;
                state.D[0] = unchecked((uint)deltaX);
                state.D[1] = unchecked((uint)deltaY);
            });
            context.WaitForBlitterIdle();
            var publishedX = ReadOracleLayerScrollX(context.Bus, layer);
            var publishedY = ReadOracleLayerScrollY(context.Bus, layer);
            trace.Add($"super-scroll-matrix:{target.Name}:delta={deltaX},{deltaY}:" +
                $"sentinel=True:d0={(int)result.D[0]}:" +
                DescribeSuperScrollState(
                    context,
                    layer,
                    display.Address,
                    super.Address) +
                $":gfx={context.LastGraphicsCallDelta}:" +
                $"order={context.LastGraphicsCallOrder}");
            if (context.Native && target.Name == "partial-bottom")
            {
                Assert.Equal((short)0, publishedX);
                Assert.Equal((short)1, publishedY);
                Assert.Equal((ushort)16,
                    ReadOracleBitMapRows(context.Bus, super.Address));
            }
            currentX = publishedX;
            currentY = publishedY;
        }

        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = layer);
        context.DisposeLayerInfo(layerInfo);
    }

    private static string DescribeSuperScrollState(
        OracleContext context,
        uint layer,
        uint displayBitMap,
        uint superBitMap)
        => $"scroll={ReadOracleLayerScrollX(context.Bus, layer)}," +
           $"{ReadOracleLayerScrollY(context.Bus, layer)}:" +
           "super-cr=" + DescribeClipRectChain(
               context,
               ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.SuperClipRect),
               displayBitMap,
               superBitMap);

    private static void TraceClipRectSplitOrderMatrix(
        OracleContext context,
        List<string> trace)
    {
        TraceClipRectSplitOrder(
            context,
            trace,
            "center",
            [(4, 4, 11, 11)]);
        TraceClipRectSplitOrder(
            context,
            trace,
            "left-edge",
            [(0, 4, 7, 11)]);
        TraceClipRectSplitOrder(
            context,
            trace,
            "two-higher",
            [(2, 2, 7, 9), (6, 6, 13, 13)]);
    }

    private static void TraceSortLayerCrMatrix(
        OracleContext context,
        List<string> trace)
    {
        (short X, short Y, uint RawX, uint RawY, string Name)[] directions =
        [
            (-1, -1, unchecked((uint)-1), unchecked((uint)-1), "up-left"),
            (0, -1, 0, unchecked((uint)-1), "up"),
            (1, -1, 1, unchecked((uint)-1), "up-right"),
            (-1, 0, unchecked((uint)-1), 0, "left"),
            (0, 0, 0, 0, "zero"),
            (1, 0, 1, 0, "right"),
            (-1, 1, unchecked((uint)-1), 1, "down-left"),
            (0, 1, 0, 1, "down"),
            (1, 1, 1, 1, "down-right"),
            (-1, 1, 0xA55AFFFF, 0x5AA50001, "abi-low-word")
        ];
        foreach (var direction in directions)
            TraceSortLayerCrDirection(context, trace, direction);
    }

    private static string RunIrregularSortOrder(
        OracleContext context,
        uint rawX,
        uint rawY)
    {
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(16, 16, 1, 0x44);
        var bottom = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            0,
            0,
            15,
            15);
        (short X0, short Y0, short X1, short Y1)[] blockers =
        [
            (0, 0, 3, 3),
            (0, 4, 5, 10),
            (4, 0, 9, 5)
        ];
        var higher = new uint[blockers.Length];
        for (var index = 0; index < blockers.Length; index++)
        {
            var bounds = blockers[index];
            higher[index] = context.CreateLayer(
                LayersLvo.CreateUpfrontLayer,
                layerInfo,
                display.Address,
                0,
                LayerCreationFlags.Simple,
                bounds.X0,
                bounds.Y0,
                bounds.X1,
                bounds.Y1);
        }
        context.InvokeLayers(LayersLvo.LockLayer,
            state => state.A[1] = bottom);
        context.InvokeLayers(LayersLvo.SortLayerCR, state =>
        {
            state.A[0] = bottom;
            state.D[0] = rawX;
            state.D[1] = rawY;
        });
        context.InvokeLayers(LayersLvo.UnlockLayer,
            state => state.A[0] = bottom);
        var result = DescribeClipRectOrder(context, bottom, higher);
        for (var index = higher.Length - 1; index >= 0; index--)
        {
            context.InvokeLayers(LayersLvo.DeleteLayer,
                state => state.A[1] = higher[index]);
        }
        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = bottom);
        context.DisposeLayerInfo(layerInfo);
        context.FreePlanarBitMap(display);
        return result;
    }

    private static void TraceSortLayerCrDirection(
        OracleContext context,
        List<string> trace,
        (short X, short Y, uint RawX, uint RawY, string Name) direction)
    {
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(16, 16, 1, 0x44);
        var bottom = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            0,
            0,
            15,
            15);
        (short X0, short Y0, short X1, short Y1)[] blockers =
        [
            (0, 0, 3, 3),
            (0, 4, 5, 10),
            (4, 0, 9, 5)
        ];
        var higher = new uint[blockers.Length];
        for (var index = 0; index < blockers.Length; index++)
        {
            var bounds = blockers[index];
            higher[index] = context.CreateLayer(
                LayersLvo.CreateUpfrontLayer,
                layerInfo,
                display.Address,
                0,
                LayerCreationFlags.Simple,
                bounds.X0,
                bounds.Y0,
                bounds.X1,
                bounds.Y1);
            if (direction.Name == "zero" && index < 2)
            {
                trace.Add($"sort-pre:h{index + 1}:next=" +
                    DescribeClipRectOrder(context, bottom, higher));
            }
        }
        Assert.Equal(10, CountClipRectChain(context, bottom));
        var preSortOrder = DescribeClipRectOrder(context, bottom, higher);
        if (direction.Name == "zero")
        {
            // The topology is recreated identically for every direction. Keep
            // its publication order as one independent oracle row so it does
            // not make every comparator row appear red when only partition
            // publication differs.
            trace.Add($"sort-pre:irregular:next={preSortOrder}");
        }
        context.InvokeLayers(LayersLvo.LockLayer,
            state => state.A[1] = bottom);
        var sorted = context.InvokeLayers(LayersLvo.SortLayerCR, state =>
        {
            state.A[0] = bottom;
            state.D[0] = direction.RawX;
            state.D[1] = direction.RawY;
        });
        context.InvokeLayers(LayersLvo.UnlockLayer,
            state => state.A[0] = bottom);
        trace.Add($"sort-matrix:{direction.Name}:dx={direction.X}:dy={direction.Y}:" +
            $"raw-dx=0x{direction.RawX:X8}:raw-dy=0x{direction.RawY:X8}:" +
            $"preserved-d0={unchecked((int)sorted.D[0])}:" +
            $"next={DescribeClipRectOrder(context, bottom, higher)}:" +
            "raw=" + DescribeClipRectChain(
                context,
                ReadOracleLayerPointer(
                    context.Bus, bottom, OracleLayerPointer.ClipRect),
                display.Address,
                0));
        for (var index = higher.Length - 1; index >= 0; index--)
        {
            context.InvokeLayers(LayersLvo.DeleteLayer,
                state => state.A[1] = higher[index]);
        }
        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = bottom);
        context.DisposeLayerInfo(layerInfo);
        context.FreePlanarBitMap(display);
    }

    private static void TraceClipRectSplitOrder(
        OracleContext context,
        List<string> trace,
        string name,
        (short MinX, short MinY, short MaxX, short MaxY)[] blockers)
    {
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(16, 16, 1, 0x52);
        var bottom = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            0,
            0,
            15,
            15);
        var higher = new uint[blockers.Length];
        for (var index = 0; index < blockers.Length; index++)
        {
            var bounds = blockers[index];
            higher[index] = context.CreateLayer(
                LayersLvo.CreateUpfrontLayer,
                layerInfo,
                display.Address,
                0,
                LayerCreationFlags.Simple,
                bounds.MinX,
                bounds.MinY,
                bounds.MaxX,
                bounds.MaxY);
        }
        trace.Add($"clip-order:{name}:" +
            DescribeClipRectOrder(context, bottom, higher));
        for (var index = higher.Length - 1; index >= 0; index--)
        {
            context.InvokeLayers(LayersLvo.DeleteLayer,
                state => state.A[1] = higher[index]);
        }
        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = bottom);
        context.DisposeLayerInfo(layerInfo);
    }

    private static string DescribeClipRectOrder(
        OracleContext context,
        uint layer,
        uint[] higher)
    {
        var entries = new List<string>();
        var seen = new HashSet<uint>();
        var clipRect = ReadOracleLayerPointer(
            context.Bus, layer, OracleLayerPointer.ClipRect);
        while (clipRect != 0 && entries.Count < 64 && seen.Add(clipRect))
        {
            var obscurer = ReadOracleClipRectPointer(
                context.Bus, clipRect, OracleClipRectPointer.ObscuringLayer);
            var label = obscurer == 0
                ? "-"
                : Array.IndexOf(higher, obscurer) is var index && index >= 0
                    ? $"H{index + 1}"
                    : "O";
            var bitMap = ReadOracleClipRectPointer(
                context.Bus, clipRect, OracleClipRectPointer.BitMap);
            entries.Add(
                FormatOracleRectangle(
                    ReadOracleClipRectBounds(context.Bus, clipRect)) +
                $"/{label}/" + (bitMap == 0 ? "N" : "B"));
            clipRect = ReadOracleClipRectPointer(
                context.Bus, clipRect, OracleClipRectPointer.Next);
        }
        return $"count={entries.Count}:next=[{string.Join(';', entries)}]:" +
            $"terminated={clipRect == 0}";
    }

    private static void TraceCreationMode(
        OracleContext context,
        List<string> trace,
        LayerCreationFlags mode)
    {
        var layerInfo = context.NewLayerInfo();
        var fatten = context.InvokeLayers(LayersLvo.FattenLayerInfo,
            state => state.A[0] = layerInfo);
        context.InvokeLayers(LayersLvo.ThinLayerInfo,
            state => state.A[0] = layerInfo);
        var display = context.CreatePlanarBitMap(32, 16, 1, 0x31);
        var super = mode == LayerCreationFlags.Super
            ? context.CreatePlanarBitMap(32, 16, 1, 0xA7)
            : default;
        var displayBeforeCreate = HashBitMap(context, display);
        var superBeforeCreate = mode == LayerCreationFlags.Super
            ? HashBitMap(context, super)
            : "none";
        var layer = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            super.Address,
            mode,
            2,
            3,
            25,
            13);
        var displayImmediatelyAfterCreate = HashBitMap(context, display);
        var busyBeforeWait = context.Bus.Blitter.Busy;
        var graphicsDelta = context.LastGraphicsCallDelta;
        var graphicsOrder = context.LastGraphicsCallOrder;
        context.WaitForBlitterIdle();

        trace.Add(
            $"mode:{mode}:fatten={fatten.D[0] != 0}:" +
            DescribeLayer(context, layer, display.Address, super.Address) +
            $":create-pixels={displayBeforeCreate}->{displayImmediatelyAfterCreate}->" +
            $"{HashBitMap(context, display)}:busy-before-wait={busyBeforeWait}:" +
            $"gfx={graphicsDelta}:order={graphicsOrder}:" +
            $"super-source={superBeforeCreate}");

        var lockFlags = new List<ushort>
        {
            (ushort)ReadOracleLayerFlags(context.Bus, layer)
        };
        InvokeLockAndCapture(LayersLvo.LockLayer,
            state => state.A[1] = layer);
        InvokeLockAndCapture(LayersLvo.UnlockLayer,
            state => state.A[0] = layer);
        InvokeLockAndCapture(LayersLvo.LockLayers,
            state => state.A[0] = layerInfo);
        InvokeLockAndCapture(LayersLvo.UnlockLayers,
            state => state.A[0] = layerInfo);
        InvokeLockAndCapture(LayersLvo.LockLayerInfo,
            state => state.A[0] = layerInfo);
        InvokeLockAndCapture(LayersLvo.UnlockLayerInfo,
            state => state.A[0] = layerInfo);
        trace.Add($"mode:{mode}:locks=returned");

        var flagsBeforeBegin = (ushort)ReadOracleLayerFlags(context.Bus, layer);
        var begin = context.InvokeLayers(LayersLvo.BeginUpdate,
            state => state.A[0] = layer);
        // OracleContext exposes the emulator's reusable CPU state. Capture a
        // non-VOID result before EndUpdate reuses that same frame; otherwise
        // CopperStart's deliberate VOID-frame preservation makes the earlier
        // BeginUpdate observation appear spuriously TRUE.
        var beginResult = begin.D[0];
        var flagsAfterBegin = (ushort)ReadOracleLayerFlags(context.Bus, layer);
        context.InvokeLayers(LayersLvo.EndUpdate, state =>
        {
            state.A[0] = layer;
            state.D[0] = 1;
        });
        var flagsAfterEnd = (ushort)ReadOracleLayerFlags(context.Bus, layer);
        trace.Add($"mode:{mode}:refresh={beginResult != 0}:" +
            $"raw-d0={beginResult}:lock-flags=[{string.Join(',', lockFlags.Select(static value => $"{value:X4}"))}]:" +
            $"begin-flags={flagsBeforeBegin:X4}>{flagsAfterBegin:X4}>{flagsAfterEnd:X4}:" +
            $"pixels={HashBitMap(context, display)}");

        var deleted = context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = layer);
        trace.Add($"mode:{mode}:delete={deleted.D[0] != 0}");
        context.DisposeLayerInfo(layerInfo);

        void InvokeLockAndCapture(int lvo, Action<M68kCpuState> initialize)
        {
            context.InvokeLayers(lvo, initialize);
            lockFlags.Add((ushort)ReadOracleLayerFlags(context.Bus, layer));
        }
    }

    private static void TraceTopologyRefreshHooksAndQueries(
        OracleContext context,
        List<string> trace)
    {
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(64, 32, 1, 0x5D);
        var bottom = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            0,
            0,
            31,
            15);
        var top = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            8,
            4,
            23,
            11);

        trace.Add("overlap:bottom=" + DescribeLayer(
            context, bottom, display.Address, 0));
        trace.Add("overlap:top=" + DescribeLayer(
            context, top, display.Address, 0));
        trace.Add("overlap:z=" + DescribeZOrder(context, layerInfo, bottom, top));
        trace.Add("query:top=" + WhichLabel(context, layerInfo, 10, 6, bottom, top));
        trace.Add("query:none=" + WhichLabel(context, layerInfo, 50, 25, bottom, top));

        var behind = context.InvokeLayers(LayersLvo.BehindLayer,
            state => state.A[1] = top);
        trace.Add($"z:behind={behind.D[0] != 0}:" +
            DescribeZOrder(context, layerInfo, bottom, top));
        var upfront = context.InvokeLayers(LayersLvo.UpfrontLayer,
            state => state.A[1] = top);
        trace.Add($"z:upfront={upfront.D[0] != 0}:" +
            DescribeZOrder(context, layerInfo, bottom, top));

        var move = context.InvokeLayers(LayersLvo.MoveLayer, state =>
        {
            state.A[1] = top;
            state.D[0] = 2;
            state.D[1] = 1;
        });
        var size = context.InvokeLayers(LayersLvo.SizeLayer, state =>
        {
            state.A[1] = top;
            state.D[0] = 2;
            state.D[1] = 1;
        });
        var moveSize = context.InvokeLayers(LayersLvo.MoveSizeLayer, state =>
        {
            state.A[0] = bottom;
            state.D[0] = 1;
            state.D[1] = 1;
            state.D[2] = unchecked((uint)-1);
            state.D[3] = unchecked((uint)-1);
        });
        context.InvokeLayers(LayersLvo.ScrollLayer, state =>
        {
            state.A[1] = bottom;
            state.D[0] = 1;
            state.D[1] = 1;
        });
        context.InvokeLayers(LayersLvo.LockLayer,
            state => state.A[1] = bottom);
        context.InvokeLayers(LayersLvo.SortLayerCR, state =>
        {
            state.A[0] = bottom;
            state.D[0] = 0;
            state.D[1] = 0;
        });
        context.InvokeLayers(LayersLvo.UnlockLayer,
            state => state.A[0] = bottom);
        trace.Add($"geometry:move={move.D[0] != 0}:size={size.D[0] != 0}:" +
            $"movesize={moveSize.D[0] != 0}:bottom=" +
            DescribeLayer(context, bottom, display.Address, 0) +
            ":top=" + DescribeLayer(context, top, display.Address, 0) +
            $":pixels={HashBitMap(context, display)}");

        var inFront = context.InvokeLayers(LayersLvo.MoveLayerInFrontOf, state =>
        {
            state.A[0] = bottom;
            state.A[1] = top;
        });
        trace.Add($"z:in-front={inFront.D[0] != 0}:" +
            DescribeZOrder(context, layerInfo, bottom, top));
        trace.Add("damage-stage:in-front=" +
            DescribeLayerDamage(context, bottom));

        var region = context.CreateSingleRectangleRegion(3, 3, 12, 9);
        var install = context.InvokeLayers(LayersLvo.InstallClipRegion, state =>
        {
            state.A[0] = bottom;
            state.A[1] = region;
        });
        var installed = ReadOracleLayerPointer(
            context.Bus, bottom, OracleLayerPointer.ClipRegion) == region;
        trace.Add("damage-stage:clip-install=" +
            DescribeLayerDamage(context, bottom));
        var remove = context.InvokeLayers(LayersLvo.InstallClipRegion, state =>
        {
            state.A[0] = bottom;
            state.A[1] = 0;
        });
        trace.Add("damage-stage:clip-remove=" +
            DescribeLayerDamage(context, bottom));
        trace.Add($"region:first-null={install.D[0] == 0}:installed={installed}:" +
            $"remove-old={remove.D[0] == region}");

        var installLayerHook = context.InvokeLayers(LayersLvo.InstallLayerHook, state =>
        {
            state.A[0] = bottom;
            state.A[1] = LayerBackfillHook.NoBackfill;
        });
        var restoreLayerHook = context.InvokeLayers(LayersLvo.InstallLayerHook, state =>
        {
            state.A[0] = bottom;
            state.A[1] = LayerBackfillHook.Backfill;
        });
        var installInfoHook = context.InvokeLayers(LayersLvo.InstallLayerInfoHook, state =>
        {
            state.A[0] = layerInfo;
            state.A[1] = LayerBackfillHook.NoBackfill;
        });
        var restoreInfoHook = context.InvokeLayers(LayersLvo.InstallLayerInfoHook, state =>
        {
            state.A[0] = layerInfo;
            state.A[1] = LayerBackfillHook.Backfill;
        });
        trace.Add($"hook-install:layer-old={installLayerHook.D[0]}:" +
            $"layer-restored={restoreLayerHook.D[0] == LayerBackfillHook.NoBackfill}:" +
            $"info-old={installInfoHook.D[0]}:" +
            $"info-restored={restoreInfoHook.D[0] == LayerBackfillHook.NoBackfill}");
        trace.Add("damage-stage:hooks-restored=" +
            DescribeLayerDamage(context, bottom));

        var hookTrace = context.InvokeDoHookClipRects(
            bottom,
            display.Address,
            0,
            2,
            2,
            14,
            10);
        trace.Add("hook-callback:" + hookTrace);
        trace.Add("damage-stage:do-hook=" +
            DescribeLayerDamage(context, bottom));

        var deleteTop = context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = top);
        var deleteBottom = context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = bottom);
        trace.Add($"overlap:delete={deleteTop.D[0] != 0}/{deleteBottom.D[0] != 0}");
        context.DisposeLayerInfo(layerInfo);
    }

    private static void TraceFailureSurface(OracleContext context, List<string> trace)
    {
        var layerInfo = context.NewLayerInfo();
        var bitMap = context.CreatePlanarBitMap(16, 16, 1, 0xC3);
        var invalidBounds = context.InvokeLayers(LayersLvo.CreateUpfrontLayer, state =>
        {
            state.A[0] = layerInfo;
            state.A[1] = bitMap.Address;
            state.D[0] = 10;
            state.D[1] = 10;
            state.D[2] = 5;
            state.D[3] = 5;
            state.D[4] = (uint)LayerCreationFlags.Simple;
        });
        var nullBitMap = context.InvokeLayers(LayersLvo.CreateUpfrontLayer, state =>
        {
            state.A[0] = layerInfo;
            state.A[1] = 0;
            state.D[0] = 0;
            state.D[1] = 0;
            state.D[2] = 7;
            state.D[3] = 7;
            state.D[4] = (uint)LayerCreationFlags.Simple;
        });
        trace.Add($"failure:bounds={invalidBounds.D[0] == 0}:" +
            $"null-bitmap={nullBitMap.D[0] == 0}:" +
            "low-memory=not-safely-injectable-in-early-resident-harness");
        context.DisposeLayerInfo(layerInfo);
    }

    private static string DescribeLayer(
        OracleContext context,
        uint layer,
        uint displayBitMap,
        uint superBitMap)
    {
        var bus = context.Bus;
        var bounds = FormatOracleRectangle(ReadOracleLayerBounds(bus, layer));
        var flags = ReadOracleLayerFlags(bus, layer);
        const LayerFlags publicMask = LayerFlags.Simple | LayerFlags.Smart |
            LayerFlags.Super | LayerFlags.Backdrop | LayerFlags.Updating |
            LayerFlags.Refresh | LayerFlags.ClipRectsLost;
        var rastPort = ReadOracleLayerPointer(bus, layer, OracleLayerPointer.RastPort);
        var clipRect = ReadOracleLayerPointer(bus, layer, OracleLayerPointer.ClipRect);
        var superClipRect = ReadOracleLayerPointer(
            bus, layer, OracleLayerPointer.SuperClipRect);
        var damageList = ReadOracleLayerPointer(
            bus, layer, OracleLayerPointer.DamageList);
        return $"bounds={bounds}:flags={flags & publicMask}:" +
            $"scroll={ReadOracleLayerScrollX(bus, layer)}," +
            $"{ReadOracleLayerScrollY(bus, layer)}:" +
            $"rp-layer={ReadOracleRastPortPointer(bus, rastPort, bitMap: false) == layer}:" +
            $"rp-bitmap={ReadOracleRastPortPointer(bus, rastPort, bitMap: true) == displayBitMap}:" +
            $"super={ReadOracleLayerPointer(bus, layer, OracleLayerPointer.SuperBitMap) == superBitMap}:" +
            $"damage={DescribeRegion(context, damageList)}:" +
            $"cr={DescribeClipRectChain(context, clipRect, displayBitMap, superBitMap)}:" +
            $"super-cr={DescribeClipRectChain(context, superClipRect, displayBitMap, superBitMap)}";
    }

    private static string DescribeLayerDamage(
        OracleContext context,
        uint layer)
    {
        var flags = ReadOracleLayerFlags(context.Bus, layer);
        var damageList = ReadOracleLayerPointer(
            context.Bus, layer, OracleLayerPointer.DamageList);
        return $"refresh={(flags & LayerFlags.Refresh) != 0}:" +
            DescribeRegion(context, damageList);
    }

    private static string DescribeClipRectChain(
        OracleContext context,
        uint head,
        uint displayBitMap,
        uint superBitMap)
    {
        if (head == 0)
            return "[]";
        var bus = context.Bus;
        var entries = new List<string>();
        var seen = new HashSet<uint>();
        var chain = new List<uint>();
        for (var scan = head;
             scan != 0 && chain.Count < 256 && !chain.Contains(scan) &&
             IsOracleClipRectMapped(bus, scan);
             scan = ReadOracleClipRectPointer(bus, scan, OracleClipRectPointer.Next))
        {
            chain.Add(scan);
        }
        var previous = 0u;
        var clipRect = head;
        var reservedLinksValid = true;
        while (clipRect != 0 && entries.Count < 256 && seen.Add(clipRect))
        {
            if (!IsOracleClipRectMapped(bus, clipRect))
            {
                entries.Add("unmapped");
                break;
            }
            var actualPrevious = ReadOracleClipRectPointer(
                bus, clipRect, OracleClipRectPointer.ReservedLink);
            var previousIndex = DescribeClipRectPointer(
                bus,
                actualPrevious,
                chain);
            reservedLinksValid &= actualPrevious == previous;
            var clipBounds = FormatOracleRectangle(
                ReadOracleClipRectBounds(bus, clipRect));
            var clipBitMap = ReadOracleClipRectPointer(
                bus, clipRect, OracleClipRectPointer.BitMap);
            var bitMapKind = clipBitMap == 0
                ? 'N'
                : clipBitMap == displayBitMap
                    ? 'D'
                    : superBitMap != 0 && clipBitMap == superBitMap
                        ? 'U'
                        : 'O';
            var obscured = ReadOracleClipRectPointer(
                bus, clipRect, OracleClipRectPointer.ObscuringLayer) != 0;
            entries.Add($"{clipBounds}:{bitMapKind}:{(obscured ? 'O' : '-')}:" +
                $"prev={ClassifyLink(actualPrevious, clipRect, previous, head)}/" +
                $"{previousIndex}:" +
                DescribeBitMap(context, clipBitMap, displayBitMap, superBitMap));
            previous = clipRect;
            clipRect = ReadOracleClipRectPointer(
                bus, clipRect, OracleClipRectPointer.Next);
        }
        // A cycle, unmapped successor, or traversal limit leaves a non-null
        // Next pointer. Keep that public-chain failure independent of the
        // reserved predecessor diagnostics excluded by normalization.
        return $"[count={entries.Count}:links={reservedLinksValid}:" +
            $"next-valid={clipRect == 0}:" +
            string.Join(';', entries) + "]";
    }

    private static int CountClipRectChain(OracleContext context, uint layer)
    {
        var count = 0;
        var seen = new HashSet<uint>();
        for (var current = ReadOracleLayerPointer(
                 context.Bus, layer, OracleLayerPointer.ClipRect);
             current != 0 && count < 256 && seen.Add(current);
             current = ReadOracleClipRectPointer(
                 context.Bus, current, OracleClipRectPointer.Next))
        {
            count++;
        }
        return count;
    }

    private static string DescribeClipRectPointer(
        AmigaBus bus,
        uint pointer,
        IReadOnlyList<uint> chain)
    {
        if (pointer == 0)
            return "null";
        for (var index = 0; index < chain.Count; index++)
        {
            if (chain[index] == pointer)
                return $"i{index}";
        }
        if (!IsOracleClipRectMapped(bus, pointer))
            return "external-unmapped";
        var next = ReadOracleClipRectPointer(bus, pointer, OracleClipRectPointer.Next);
        var previous = ReadOracleClipRectPointer(
            bus, pointer, OracleClipRectPointer.ReservedLink);
        return $"external({FormatOracleRectangle(ReadOracleClipRectBounds(bus, pointer))};" +
            $"next={DescribeChainPointer(next, chain)};" +
            $"prev={DescribeChainPointer(previous, chain)})";
    }

    private static string DescribeChainPointer(
        uint pointer,
        IReadOnlyList<uint> chain)
    {
        if (pointer == 0)
            return "null";
        for (var index = 0; index < chain.Count; index++)
        {
            if (chain[index] == pointer)
                return $"i{index}";
        }
        return "external";
    }

    private static string DescribeBitMap(
        OracleContext context,
        uint bitMap,
        uint displayBitMap,
        uint superBitMap)
    {
        if (bitMap == 0)
            return "null";
        var bus = context.Bus;
        if (!bus.IsMappedMemoryRange(bitMap, checked((int)BitMap.Size)))
            return "unmapped";
        var bytesPerRow = ReadOracleBitMapBytesPerRow(bus, bitMap);
        var rows = ReadOracleBitMapRows(bus, bitMap);
        var depth = ReadOracleBitMapDepth(bus, bitMap);
        var kind = bitMap == displayBitMap
            ? "D"
            : superBitMap != 0 && bitMap == superBitMap
                ? "U"
                : "O";
        var planes = new List<string>();
        for (var plane = 0; plane < Math.Min(depth, (byte)8); plane++)
        {
            var pointer = ReadOracleBitMapPlane(bus, bitMap, plane);
            planes.Add(ClassifyPlane(
                context,
                pointer,
                displayBitMap,
                superBitMap));
        }
        return $"{kind}({bytesPerRow}x{rows}x{depth};{string.Join(',', planes)})";
    }

    private static string ClassifyPlane(
        OracleContext context,
        uint pointer,
        uint displayBitMap,
        uint superBitMap)
    {
        if (pointer == 0)
            return "N";
        if (TryClassifyPlane(context, pointer, displayBitMap, 'D', out var label) ||
            TryClassifyPlane(context, pointer, superBitMap, 'U', out label))
        {
            return label;
        }
        return "O";
    }

    private static bool TryClassifyPlane(
        OracleContext context,
        uint pointer,
        uint referenceBitMap,
        char label,
        out string classification)
    {
        classification = string.Empty;
        if (referenceBitMap == 0 ||
            !context.Bus.IsMappedMemoryRange(referenceBitMap, checked((int)BitMap.Size)))
        {
            return false;
        }
        var bytesPerRow = ReadOracleBitMapBytesPerRow(context.Bus, referenceBitMap);
        var rows = ReadOracleBitMapRows(context.Bus, referenceBitMap);
        var depth = ReadOracleBitMapDepth(context.Bus, referenceBitMap);
        var bytes = checked((uint)bytesPerRow * rows);
        for (var plane = 0; plane < Math.Min(depth, (byte)8); plane++)
        {
            var baseAddress = ReadOracleBitMapPlane(
                context.Bus, referenceBitMap, plane);
            if (pointer >= baseAddress && pointer - baseAddress < bytes)
            {
                classification = $"{label}{plane}+{pointer - baseAddress}";
                return true;
            }
        }
        return false;
    }

    private static string DescribeRegion(OracleContext context, uint region)
    {
        if (region == 0)
            return "null";
        var bus = context.Bus;
        Assert.True((region & 1) == 0 && region <= uint.MaxValue - Region.Size &&
            bus.IsMappedMemoryRange(region, checked((int)Region.Size)),
            $"Region 0x{region:X8} must be aligned and mapped.");
        var regionBounds = ReadOracleRegionBounds(bus, region);
        var bounds = FormatOracleRectangle(regionBounds);
        var node = ReadOracleRegionFirst(bus, region);
        var previous = 0u;
        var rawLinksValid = true;
        // The SDK head anchor is Region.RegionRectangle, not a null predecessor.
        // Native v40.63 diagnostics confirm the first Previous points to this field.
        var expectedPrevious = LayersRegionCodec.HeadAnchor(APTR.FromPointer(region)).Raw;
        var publicLinksValid = true;
        var nodes = new List<string>();
        var rectangles = new List<(int MinX, int MinY, int MaxX, int MaxY)>();
        var seen = new HashSet<uint>();
        while (node != 0 && nodes.Count < 4096 && seen.Add(node))
        {
            if ((node & 1) != 0 || node > uint.MaxValue - RegionRectangle.Size ||
                !bus.IsMappedMemoryRange(node, checked((int)RegionRectangle.Size)))
            {
                nodes.Add($"unmapped-or-unaligned(0x{node:X8})");
                rawLinksValid = false;
                break;
            }
            var actualPrevious = ReadOracleRegionRectanglePointer(
                bus, node, previous: true);
            rawLinksValid &= actualPrevious == previous;
            publicLinksValid &= actualPrevious == expectedPrevious;
            if (actualPrevious != previous)
            {
                var insideRegion = actualPrevious >= region &&
                    actualPrevious < region + Region.Size;
                var mappedLong = actualPrevious != 0 && (actualPrevious & 1) == 0 &&
                    bus.IsMappedMemoryRange(actualPrevious, 4);
                context.Diagnostics.Add(
                    $"region-predecessor:region=0x{region:X8}:" +
                    $"head=0x{ReadOracleRegionFirst(bus, region):X8}:" +
                    $"node-index={nodes.Count}:node=0x{node:X8}:" +
                    $"next=0x{ReadOracleRegionRectanglePointer(bus, node, previous: false):X8}:" +
                    $"previous=0x{actualPrevious:X8}:" +
                    $"inside-region={insideRegion}:" +
                    $"region-offset={(insideRegion ? actualPrevious - region : uint.MaxValue)}:" +
                    $"mapped-long={mappedLong}:" +
                    $"target-long=0x{(mappedLong ? bus.ReadLong(actualPrevious) : 0):X8}");
            }
            var rectangle = ReadOracleRegionRectangleBounds(bus, node);
            nodes.Add(FormatOracleRectangle(rectangle) +
                $":prev={ClassifyLink(actualPrevious, node, previous, head: ReadOracleRegionFirst(bus, region))}");
            rectangles.Add((regionBounds.MinX + rectangle.MinX,
                regionBounds.MinY + rectangle.MinY,
                regionBounds.MinX + rectangle.MaxX,
                regionBounds.MinY + rectangle.MaxY));
            previous = node;
            expectedPrevious = node;
            node = ReadOracleRegionRectanglePointer(
                bus, node, previous: false);
        }
        if (node != 0)
            rawLinksValid = false;
        var raw = $"{{bounds={bounds}:count={nodes.Count}:links={rawLinksValid}:" +
            $"rects=[{string.Join(';', nodes)}]}}";
        context.Diagnostics.Add($"region:0x{region:X8}:raw={raw}");
        Assert.True(node == 0,
            $"Region Next chain must terminate, be mapped/aligned, and contain no cycles (limit 4096). {raw}");
        Assert.True(publicLinksValid,
            $"Region Previous must reference the head anchor, then the preceding node. {raw}");
        foreach (var rectangle in rectangles)
        {
            Assert.True(rectangle.MinX <= rectangle.MaxX &&
                rectangle.MinY <= rectangle.MaxY &&
                rectangle.MinX >= regionBounds.MinX && rectangle.MaxX <= regionBounds.MaxX &&
                rectangle.MinY >= regionBounds.MinY && rectangle.MaxY <= regionBounds.MaxY,
                $"Region rectangles must be nonempty and contained in Region.Bounds. {raw}");
        }

        // RegionRectangle partition/order is not public geometry. Compare the exact
        // union, retaining bounds and rejecting overlapping or malformed partitions.
        // Raw decomposition remains independently available above; ClipRects and
        // callback order are deliberately not canonicalized by this policy.
        return $"{{bounds={bounds}:links=True:next-valid=True:" +
            $"coverage=[{CanonicalRegionCoverage(rectangles, raw)}]}}";
    }

    private static string CanonicalRegionCoverage(
        List<(int MinX, int MinY, int MaxX, int MaxY)> rectangles,
        string raw)
    {
        // Sweep rectangle edges instead of enumerating pixels, so large WORD
        // coordinate ranges cannot turn an observation into a huge allocation.
        var yEdges = rectangles.SelectMany(static rectangle =>
            new[] { rectangle.MinY, rectangle.MaxY + 1 }).Distinct().Order().ToArray();
        var bands = new List<(int MinY, int MaxY, string Intervals)>();
        for (var edge = 0; edge + 1 < yEdges.Length; edge++)
        {
            var minY = yEdges[edge];
            var maxY = yEdges[edge + 1] - 1;
            var active = rectangles.Where(rectangle =>
                    rectangle.MinY <= minY && rectangle.MaxY >= maxY)
                .OrderBy(static rectangle => rectangle.MinX)
                .ThenBy(static rectangle => rectangle.MaxX).ToArray();
            if (active.Length == 0)
                continue;
            var intervals = new List<string>();
            var minX = active[0].MinX;
            var maxX = active[0].MaxX;
            foreach (var rectangle in active.Skip(1))
            {
                Assert.True(rectangle.MinX > maxX,
                    $"Region rectangles must not overlap. {raw}");
                if (rectangle.MinX == maxX + 1)
                    maxX = rectangle.MaxX;
                else
                {
                    intervals.Add($"{minX},{maxX}");
                    (minX, maxX) = (rectangle.MinX, rectangle.MaxX);
                }
            }
            intervals.Add($"{minX},{maxX}");
            var coverage = string.Join('|', intervals);
            if (bands.Count != 0 && bands[^1].MaxY + 1 == minY &&
                bands[^1].Intervals == coverage)
                bands[^1] = (bands[^1].MinY, maxY, coverage);
            else
                bands.Add((minY, maxY, coverage));
        }
        return string.Join(';', bands.Select(static band =>
            $"y={band.MinY},{band.MaxY}:x={band.Intervals}"));
    }

    private static string ClassifyLink(
        uint actual,
        uint current,
        uint expectedPrevious,
        uint head)
    {
        if (actual == expectedPrevious)
            return expectedPrevious == 0 ? "N" : "P";
        if (actual == 0)
            return "N!";
        if (actual == current)
            return "self";
        if (actual == head)
            return "head";
        return "other";
    }

    private static string DescribeLayerSurfaceHashes(
        OracleContext context,
        uint layer,
        PlanarBitMap display,
        PlanarBitMap super)
    {
        var hashes = new List<string>
        {
            "D=" + HashBitMap(context, display)
        };
        if (super.Address != 0)
            hashes.Add("U=" + HashBitMap(context, super));
        var bitMaps = new HashSet<uint>();
        AddClipRectBitMaps(
            context,
            ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.ClipRect),
            bitMaps);
        AddClipRectBitMaps(
            context,
            ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.SuperClipRect),
            bitMaps);
        bitMaps.Remove(0);
        bitMaps.Remove(display.Address);
        bitMaps.Remove(super.Address);
        var owned = bitMaps
            .Select(bitMap => HashBitMap(context, bitMap))
            .OrderBy(hash => hash, StringComparer.Ordinal)
            .ToArray();
        hashes.Add("O=[" + string.Join(',', owned) + "]");
        return string.Join(',', hashes);
    }

    private static void AddClipRectBitMaps(
        OracleContext context,
        uint head,
        HashSet<uint> bitMaps)
    {
        var seen = new HashSet<uint>();
        for (var clipRect = head;
             clipRect != 0 && seen.Count < 256 && seen.Add(clipRect);
             clipRect = ReadOracleClipRectPointer(
                 context.Bus, clipRect, OracleClipRectPointer.Next))
        {
            if (!IsOracleClipRectMapped(context.Bus, clipRect))
            {
                break;
            }
            bitMaps.Add(ReadOracleClipRectPointer(
                context.Bus, clipRect, OracleClipRectPointer.BitMap));
        }
    }

    private static string DescribeZOrder(
        OracleContext context,
        uint layerInfo,
        uint bottom,
        uint top)
    {
        var labels = new List<string>();
        var layer = ReadOracleTopLayer(context.Bus, layerInfo);
        for (var count = 0; layer != 0 && count < 16; count++)
        {
            labels.Add(layer == bottom ? "bottom" : layer == top ? "top" : "other");
            layer = ReadOracleLayerPointer(
                context.Bus, layer, OracleLayerPointer.Back);
        }
        return string.Join('>', labels);
    }

    private static string WhichLabel(
        OracleContext context,
        uint layerInfo,
        int x,
        int y,
        uint bottom,
        uint top)
    {
        var result = context.InvokeLayers(LayersLvo.WhichLayer, state =>
        {
            state.A[0] = layerInfo;
            state.D[0] = unchecked((uint)x);
            state.D[1] = unchecked((uint)y);
        }).D[0];
        return result == 0 ? "none" : result == bottom ? "bottom" :
            result == top ? "top" : "other";
    }

    private static string ReadBounds(AmigaBus bus, uint address)
        => FormatOracleRectangle(ReadOracleRectangle(bus, address));

    private static string HashBitMap(OracleContext context, PlanarBitMap surface)
    {
        var hash = 14695981039346656037UL;
        var bytesPerPlane = checked(surface.BytesPerRow * surface.Height);
        for (var plane = 0; plane < surface.Planes.Length; plane++)
        {
            for (var offset = 0; offset < bytesPerPlane; offset++)
            {
                hash ^= context.Bus.ReadByte(
                    surface.Planes[plane] + checked((uint)offset));
                hash *= 1099511628211UL;
            }
        }
        return hash.ToString("X16");
    }

    private static string HashRepeated(byte value, int count)
    {
        var hash = 14695981039346656037UL;
        for (var offset = 0; offset < count; offset++)
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }
        return hash.ToString("X16");
    }

    private static string HashBitMap(OracleContext context, uint bitMap)
    {
        if (bitMap == 0 ||
            !context.Bus.IsMappedMemoryRange(bitMap, checked((int)BitMap.Size)))
        {
            return "invalid";
        }
        var bytesPerRow = ReadOracleBitMapBytesPerRow(context.Bus, bitMap);
        var rows = ReadOracleBitMapRows(context.Bus, bitMap);
        var depth = ReadOracleBitMapDepth(context.Bus, bitMap);
        if (bytesPerRow == 0 || rows == 0 || depth is 0 or > 8)
            return "invalid";
        var bytes = checked((int)bytesPerRow * rows);
        var planes = new uint[depth];
        for (var plane = 0; plane < depth; plane++)
        {
            planes[plane] = ReadOracleBitMapPlane(context.Bus, bitMap, plane);
            if (!context.Bus.IsMappedMemoryRange(planes[plane], bytes))
                return "invalid";
        }
        return HashBitMap(
            context,
            new PlanarBitMap(bitMap, bytesPerRow, rows, planes));
    }

    private static OracleContext CreateNativeOracle(byte[] rom, bool instrumentGraphics = true)
    {
        var machine = new Machine(MachineOptions
            .ForProfile(MachineProfile.A500Pal512KBoot)
            .WithCpu(AmigaM68kCoreFactory.Default, M68kBackendKind.AccurateM68000)
            .WithKickstart(KickstartConfiguration.FromRomImage(
                KickstartVersion.Kickstart31,
                rom))
            .WithLiveAgnusDma(true));
        var boot = new AmigaBootController(machine);
        boot.StartKickstartRomBoot();

        uint execBase = 0;
        for (var chunk = 0; chunk < 32; chunk++)
        {
            _ = boot.ContinueExecution(250_000);
            execBase = machine.Bus.ReadLong(4);
            if (FindLibrary(machine.Bus, execBase, "exec.library") != 0)
                break;
        }
        Assert.NotEqual(0u, execBase);
        Assert.NotEqual(0u, FindLibrary(machine.Bus, execBase, "exec.library"));

        var bootstrap = new OracleContext(machine, boot, execBase, 0, 0, true);
        // Direct-resident bootstrap bypasses the normal hardware-startup path;
        // explicitly enable master and blitter DMA before native graphics init.
        machine.Bus.WriteWord(0x00DF_F096, 0x8240);
        var graphicsResident = FindResident(machine.Bus, "graphics.library");
        var layersResident = FindResident(machine.Bus, "layers.library");
        Assert.NotEqual(0u, graphicsResident);
        Assert.NotEqual(0u, layersResident);

        // graphics.library links utility and itself before entering a headless
        // hardware wait in this early-boot configuration. Let native code reach
        // that stable point, then initialize layers through native InitResident.
        _ = bootstrap.InvokeAllowingBudgetExhaustion(
            execBase,
            ExecLvo.InitResident,
            state =>
            {
                state.A[1] = graphicsResident;
                state.D[1] = 0;
            });
        var graphicsBase = FindLibrary(machine.Bus, execBase, "graphics.library");
        Assert.NotEqual(0u, graphicsBase);

        var layersInit = bootstrap.Invoke(
            execBase,
            ExecLvo.InitResident,
            state =>
            {
                state.A[1] = layersResident;
                state.D[1] = 0;
            });
        var layersBase = FindLibrary(machine.Bus, execBase, "layers.library");
        Assert.NotEqual(0u, layersBase);
        Assert.Equal(layersBase, layersInit.D[0]);
        var context = new OracleContext(
            machine,
            boot,
            execBase,
            graphicsBase,
            layersBase,
            true);
        if (instrumentGraphics)
            context.AttachNativeGraphicsProbe();
        return context;
    }

    private static OracleContext CreateCopperStartOracle()
    {
        var machine = new Machine(MachineOptions
            .ForProfile(MachineProfile.A500Pal512KBoot)
            .WithLiveAgnusDma(true));
        var boot = new AmigaBootController(machine);
        boot.StartBootFromDisk(
            KickstartRomCyberGraphicsCorpusTests.CreateCyberGraphicsOpenLibraryProbeAdf());
        Assert.True(boot.HasCopperStartLayers);
        boot.EnableCopperStartLayersProviderOperationTraceForTest();
        var context = new OracleContext(
            machine,
            boot,
            machine.Bus.ReadLong(4),
            AmigaKickstartHost.GraphicsLibraryBase,
            boot.CopperStartLayersLibraryBase,
            false);
        context.PrepareCopperStartCallerStack();
        return context;
    }

    private static OracleContext CreateMorphOracle()
    {
        var machine = new Machine(MachineOptions
            .ForProfile(MachineProfile.A500Pal512KBoot)
            .WithLiveAgnusDma(false));
        var boot = new AmigaBootController(machine);
        boot.StartBootFromDisk(
            KickstartRomCyberGraphicsCorpusTests.CreateCyberGraphicsOpenLibraryProbeAdf());
        Assert.True(boot.HasCopperStartLayers);
        boot.ResetCopperStartLayersForTest();
        Assert.True(boot.ReinstallCopperStartLayersForTest(
            global::CopperStart.Layers.LayersAbiProfile.Unified));
        return new OracleContext(
            machine,
            boot,
            machine.Bus.ReadLong(4),
            AmigaKickstartHost.GraphicsLibraryBase,
            boot.CopperStartLayersLibraryBase,
            false);
    }

    private static uint FindResident(AmigaBus bus, string target)
    {
        for (var address = 0x00F8_0000u;
             address + Resident.Size <= 0x0100_0000u;
             address += 2)
        {
            if (bus.ReadWord(address) != 0x4AFC ||
                bus.ReadLong(address + 2) != address)
            {
                continue;
            }
            if (ReadString(bus, bus.ReadLong(address + 14)) == target)
                return address;
        }
        return 0;
    }

    private static uint FindLibrary(AmigaBus bus, uint execBase, string target)
    {
        if (execBase == 0)
            return 0;
        var list = LayersExecBaseCodec.LibraryListAddress(
            APTR.FromPointer(execBase)).Raw;
		if (!bus.IsMappedMemoryRange(list, checked((int)global::Amiga.List.Size)))
            return 0;
        var tail = OracleLibraryListTailAddress(execBase);
        var node = ReadOracleLibraryListHead(bus, execBase);
        for (var count = 0; count < 256 && node != 0 && node != tail; count++)
        {
            if (!bus.IsMappedMemoryRange(node, checked((int)Library.Size)))
                return 0;
            if (ReadString(bus, ReadOracleExecNodeName(bus, node)) == target)
                return node;
            node = ReadOracleExecNodeNext(bus, node);
        }
        return 0;
    }

    private static string ReadString(AmigaBus bus, uint address)
    {
        if (address == 0 || !bus.IsMappedMemoryRange(address, 1))
            return string.Empty;
        var chars = new List<char>();
        for (var offset = 0u;
             offset < 64 && bus.IsMappedMemoryRange(address + offset, 1);
             offset++)
        {
            var value = bus.ReadByte(address + offset);
            if (value == 0)
                break;
            chars.Add(value is >= 32 and < 127 ? (char)value : '?');
        }
        return new string(chars.ToArray());
    }

    private static bool TryLoadConfiguredRom(out byte[] rom)
    {
        rom = Array.Empty<byte>();
        var path = Environment.GetEnvironmentVariable(RomPathVariable);
        var version = Environment.GetEnvironmentVariable(RomVersionVariable);
        if (string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(version))
            return false;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new InvalidOperationException(
                $"{RomPathVariable} must name an existing, legally obtained Kickstart ROM.");
        if (!string.Equals(version?.Trim(), "3.1", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"{RomVersionVariable} must be exactly '3.1'.");
        rom = File.ReadAllBytes(path);
        if (rom.Length != 512 * 1024 || BigEndian.ReadUInt16(rom, 12, "version") != 40)
            throw new InvalidOperationException(
                "The configured fixture must be a 512 KiB Kickstart 3.1 image.");
        return true;
    }

    private sealed class OracleContext
    {
        internal OracleContext(
            Machine machine,
            AmigaBootController boot,
            uint execBase,
            uint graphicsBase,
            uint layersBase,
            bool native)
        {
            Machine = machine;
            Boot = boot;
            ExecBase = execBase;
            GraphicsBase = graphicsBase;
            LayersBase = layersBase;
            Native = native;
        }

        internal Machine Machine { get; }
        internal AmigaBootController Boot { get; }
        internal AmigaBus Bus => Machine.Bus;
        internal uint ExecBase { get; }
        internal uint GraphicsBase { get; }
        internal uint LayersBase { get; private set; }
        internal bool Native { get; }
        internal List<string> Diagnostics { get; } = new();
        internal HashSet<int> ExecutedLayersLvos { get; } = new();
        internal string LastGraphicsCallDelta { get; private set; } = "none";
        internal string LastGraphicsCallOrder { get; private set; } = "none";

        private NativeGraphicsProbe? _graphicsProbe;
        private DeterministicNativeBltBitMapLeaf? _deterministicBltBitMap;
        private const uint CallerStackBytes = 0x2000;
        private const uint CallerStackFence = 0xCA11_57AC;
        private uint _callerStackStorage;
        private uint _callerStackPointer = StackAddress;
        private uint _callerSupervisorStackPointer;

        internal void PrepareCopperStartCallerStack()
        {
            Assert.False(Native);
            Assert.Equal(0u, _callerStackStorage);
            // The synthetic boot task describes almost all Chip RAM as its
            // stack. A callback needs a dedicated span that cannot overlap
            // Layers ownership records or other guest allocations. Retain this
            // allocation for the lifetime of the disposable oracle machine.
            _callerStackStorage = Allocate(CallerStackBytes);
            var lower = _callerStackStorage + 4;
            var upper = _callerStackStorage + CallerStackBytes - 4;
            Bus.WriteLong(_callerStackStorage, CallerStackFence);
            Bus.WriteLong(upper, CallerStackFence);
            var memory = new LayersTestGuestMemory(Bus);
            var actor = LayersExecBaseCodec.ReadCurrentTask(
                ref memory, APTR.FromPointer(ExecBase));
            Assert.True(actor.IsNotNull);
            var task = ExecTaskCodec.Read(ref memory, actor);
            task.StackLower = APTR.FromPointer(lower);
            task.StackUpper = APTR.FromPointer(upper);
            task.StackPointer = APTR.FromPointer(upper);
            ExecTaskCodec.Write(ref memory, actor, task);
            _callerStackPointer = upper - 4;
            _callerSupervisorStackPointer = Machine.Cpu.State.SupervisorStackPointer;
        }

        internal void AssertCopperStartCallerStackRestored()
        {
            Assert.False(Native);
            Assert.NotEqual(0u, _callerStackStorage);
            var state = Machine.Cpu.State;
            Assert.Equal(0, state.StatusRegister & M68kCpuState.Supervisor);
            Assert.Equal(_callerStackPointer + 4, state.A[7]);
            Assert.Equal(state.A[7], state.UserStackPointer);
            Assert.Equal(_callerSupervisorStackPointer, state.SupervisorStackPointer);
            Assert.Equal(CallerStackFence, Bus.ReadLong(_callerStackStorage));
            Assert.Equal(CallerStackFence, Bus.ReadLong(_callerStackPointer + 4));
        }

        internal void AttachNativeGraphicsProbe()
        {
            Assert.True(Native);
            _graphicsProbe = new NativeGraphicsProbe(Bus, GraphicsBase);
        }

        internal DeterministicNativeBltBitMapLeaf
            InstallDeterministicNativeBltBitMapLeaf()
        {
            Assert.True(Native);
            Assert.NotNull(_graphicsProbe);
            _deterministicBltBitMap = new DeterministicNativeBltBitMapLeaf(
                Bus,
                GraphicsBase,
                _graphicsProbe);
            return _deterministicBltBitMap;
        }

        internal M68kCpuState InvokeLayers(
            int lvo,
            Action<M68kCpuState>? initialize = null)
        {
            var before = _graphicsProbe?.Snapshot();
            var sequenceStart = _graphicsProbe?.SequenceCount ?? 0;
            var deterministicStart = _deterministicBltBitMap?.CallCount ?? 0;
            var providerStart = Native
                ? -1
                : Boot.CaptureCopperStartLayersProviderOperationTraceForTest();
            var result = Invoke(LayersBase, lvo, initialize);
            ExecutedLayersLvos.Add(lvo);
            if (_graphicsProbe is not null && before is not null)
            {
                var calls = _graphicsProbe.DescribeDelta(before);
                LastGraphicsCallDelta = calls;
                LastGraphicsCallOrder = _graphicsProbe.DescribeOrder(sequenceStart);
                if (calls != "none")
                    Diagnostics.Add($"layers:{lvo}:gfx={calls}:order={LastGraphicsCallOrder}");
                if (_deterministicBltBitMap is not null &&
                    _deterministicBltBitMap.CallCount != deterministicStart)
                {
                    Diagnostics.Add(
                        $"layers:{lvo}:deterministic-blt=" +
                        _deterministicBltBitMap.DescribeCalls(deterministicStart));
                }
            }
            else if (!Native)
            {
                LastGraphicsCallDelta =
                    Boot.DescribeCopperStartLayersProviderOperationTraceForTest(
                        providerStart);
                LastGraphicsCallOrder =
                    Boot.DescribeCopperStartLayersProviderOperationOrderForTest(
                        providerStart);
            }
            else
            {
                LastGraphicsCallDelta = "uninstrumented-native";
                LastGraphicsCallOrder = "uninstrumented-native";
            }
            return result;
        }

        internal M68kCpuState InvokeGraphics(
            int lvo,
            Action<M68kCpuState>? initialize = null)
        {
            var before = _graphicsProbe?.Snapshot();
            var result = Invoke(GraphicsBase, lvo, initialize);
            if (_graphicsProbe is not null && before is not null)
            {
                Diagnostics.Add($"graphics:{lvo}:delegated=" +
                    _graphicsProbe.DescribeDelta(before));
            }
            return result;
        }

        internal void WaitForBlitterIdle()
        {
            for (var instruction = 0;
                 instruction < MaximumVectorInstructions && Bus.Blitter.Busy;
                 instruction++)
            {
                Machine.Cpu.ExecuteInstruction();
            }
            if (Bus.Blitter.Busy)
                Diagnostics.Add("blitter:still-busy-after-budget");
        }

        internal M68kCpuState Invoke(
            uint libraryBase,
            int lvo,
            Action<M68kCpuState>? initialize = null)
        {
            var (state, reachedSentinel) = InvokeCore(
                libraryBase,
                lvo,
                initialize,
                MaximumVectorInstructions);
            Assert.True(
                reachedSentinel,
                $"Vector {lvo} at 0x{libraryBase:X8} did not reach the explicit return sentinel; " +
                $"pc=0x{state.ProgramCounter:X8}, d4=0x{state.D[4]:X8}, " +
                $"a2=0x{state.A[2]:X8}.");
            return state;
        }

        internal M68kCpuState InvokeAllowingBudgetExhaustion(
            uint libraryBase,
            int lvo,
            Action<M68kCpuState>? initialize = null)
            => InvokeCore(
                libraryBase,
                lvo,
                initialize,
                MaximumVectorInstructions).State;

        internal (M68kCpuState State, bool ReachedSentinel)
            InvokeLayersAllowingBudgetExhaustion(
                int lvo,
                Action<M68kCpuState>? initialize = null)
        {
            var outcome = InvokeCore(
                LayersBase,
                lvo,
                initialize,
                MaximumVectorInstructions);
            if (outcome.ReachedSentinel)
                ExecutedLayersLvos.Add(lvo);
            return outcome;
        }

        internal (M68kCpuState State, bool ReachedSentinel) InvokeCore(
            uint libraryBase,
            int lvo,
            Action<M68kCpuState>? initialize,
            int instructionBudget,
            Func<M68kCpuState, bool>? stopAfterInstruction = null)
        {
            Bus.WriteWord(SentinelAddress, 0x60FE);
            Bus.WriteLong(_callerStackPointer, SentinelAddress);
            var state = Machine.Cpu.State;
            Array.Clear(state.D);
            Array.Clear(state.A);
            state.A[6] = libraryBase;
            if (Native)
                state.A[7] = StackAddress;
            else
                state.SetActiveStackPointer(_callerStackPointer);
            state.Halted = false;
            state.Stopped = false;
            initialize?.Invoke(state);
            state.ProgramCounter = unchecked((uint)((int)libraryBase + lvo));
            for (var executed = 0;
                 executed < instructionBudget &&
                 state.ProgramCounter != SentinelAddress &&
                 !state.Halted;
                 executed++)
            {
                if (!Native && Boot.CopperStartTaskDispatchPending)
                {
                    // A Layers retry has retired its gateway and requested an
                    // outer task dispatch. Let the ordinary runtime select the
                    // runnable actor before executing its resume instruction;
                    // the oracle must not manufacture Task.State or CPU banks.
                    var step = Boot.ContinueCopperStartRuntimeUntilCycle(
                        checked(state.Cycles + 1_000_000), maxInstructions: 1);
                    Assert.True(step.InstructionsExecuted == 1,
                        $"Oracle dispatch did not execute exactly one instruction: " +
                        $"count={step.InstructionsExecuted}, pc=0x{state.ProgramCounter:X8}, " +
                        $"halted={state.Halted}, pending={Boot.CopperStartTaskDispatchPending}; " +
                        string.Join(" | ", step.Diagnostics.TakeLast(4)
                            .Select(static item => $"{item.Code}: {item.Message}")));
                }
                else
                {
                    Machine.Cpu.ExecuteInstruction();
                }
                if (stopAfterInstruction?.Invoke(state) == true)
                    break;
            }
            return (state, state.ProgramCounter == SentinelAddress);
        }

        internal uint Allocate(uint bytes, Exec.MemoryFlags requiredFlags = 0)
        {
            var state = Invoke(ExecBase, ExecLvo.AllocMem, registers =>
            {
                registers.D[0] = bytes;
                registers.D[1] = (uint)(Exec.MemoryFlags.Public |
                    Exec.MemoryFlags.Clear | requiredFlags);
            });
            Assert.NotEqual(0u, state.D[0]);
            return state.D[0];
        }

        internal void Free(uint address, uint bytes)
            => Invoke(ExecBase, ExecLvo.FreeMem, registers =>
            {
                registers.A[1] = address;
                registers.D[0] = bytes;
            });

        internal IOrdinalFault InstallOrdinalFault(
            uint libraryBase,
            int lvo,
            int failOrdinal)
        {
            if (Native)
                return new GuestVectorOrdinalFault(this, libraryBase, lvo, failOrdinal);
            if (libraryBase == ExecBase && lvo == ExecLvo.AllocMem)
            {
                var scope = Boot.BeginCopperStartLayersMemoryAllocationFaultForTest(
                    failOrdinal);
                return new PortableOrdinalFault(scope, () => scope.Count);
            }
            if (libraryBase == GraphicsBase && lvo == GraphicsAllocBitMapLvo)
            {
                var scope = Boot.BeginCopperStartLayersBitMapAllocationFaultForTest(
                    failOrdinal);
                return new PortableOrdinalFault(scope, () => scope.Count);
            }
            throw new InvalidOperationException(
                $"No allocation-fault boundary exists for 0x{libraryBase:X8}/{lvo}.");
        }

        internal void ResetPortableLayersAfterFaultCase()
        {
            if (Native)
                return;
            Boot.ResetCopperStartLayersForTest();
            Assert.True(Boot.ReinstallCopperStartLayersForTest());
            LayersBase = Boot.CopperStartLayersLibraryBase;
            Assert.NotEqual(0u, LayersBase);
        }

        internal List<(uint Address, uint Bytes)> ExhaustPublicMemory()
        {
            var allocations = new List<(uint Address, uint Bytes)>();
            for (var bytes = 64u * 1024; bytes >= 64; bytes >>= 1)
            {
                while (true)
                {
                    var state = Invoke(ExecBase, ExecLvo.AllocMem, registers =>
                    {
                        registers.D[0] = bytes;
                        registers.D[1] = (uint)(Exec.MemoryFlags.Public |
                            Exec.MemoryFlags.Clear);
                    });
                    if (state.D[0] == 0)
                        break;
                    allocations.Add((state.D[0], bytes));
                }
            }
            return allocations;
        }

        internal void ReleaseAllocations(
            IReadOnlyList<(uint Address, uint Bytes)> allocations)
        {
            for (var index = allocations.Count - 1; index >= 0; index--)
            {
                var allocation = allocations[index];
                Invoke(ExecBase, ExecLvo.FreeMem, state =>
                {
                    state.A[1] = allocation.Address;
                    state.D[0] = allocation.Bytes;
                });
            }
        }

        internal uint NewLayerInfo()
        {
            var state = InvokeLayers(LayersLvo.NewLayerInfo);
            Assert.NotEqual(0u, state.D[0]);
            return state.D[0];
        }

        internal void DisposeLayerInfo(uint layerInfo)
            => InvokeLayers(LayersLvo.DisposeLayerInfo,
                state => state.A[0] = layerInfo);

        internal PlanarBitMap CreatePlanarBitMap(
            int width,
            int height,
            byte depth,
            byte seed,
            Exec.MemoryFlags planeMemoryFlags = 0)
        {
            Assert.InRange(width, 1, ushort.MaxValue);
            Assert.InRange(height, 1, ushort.MaxValue);
            Assert.InRange(depth, (byte)1, (byte)8);
            var bytesPerRow = checked((ushort)(((width + 15) / 16) * 2));
            var bitMap = Allocate(BitMap.Size);
            var planes = new uint[depth];
            var bytesPerPlane = checked((uint)(bytesPerRow * height));
            WriteOracleBitMap(
                Bus, bitMap, bytesPerRow, checked((ushort)height), depth);
            for (var plane = 0; plane < depth; plane++)
            {
                planes[plane] = Allocate(bytesPerPlane, planeMemoryFlags);
                WriteOracleBitMapPlane(Bus, bitMap, plane, planes[plane]);
                for (var offset = 0u; offset < bytesPerPlane; offset++)
                {
                    Bus.WriteByte(
                        planes[plane] + offset,
                        unchecked((byte)(seed + plane * 37 + offset * 13)),
                        0);
                }
            }
            return new PlanarBitMap(bitMap, bytesPerRow, height, planes);
        }

        internal void FreePlanarBitMap(PlanarBitMap bitMap)
        {
            var planeBytes = checked((uint)(bitMap.BytesPerRow * bitMap.Height));
            for (var index = bitMap.Planes.Length - 1; index >= 0; index--)
                Free(bitMap.Planes[index], planeBytes);
            Free(bitMap.Address, BitMap.Size);
        }

        internal uint CreateLayer(
            int lvo,
            uint layerInfo,
            uint bitMap,
            uint superBitMap,
            LayerCreationFlags flags,
            int minX,
            int minY,
            int maxX,
            int maxY,
            Action<M68kCpuState>? customize = null)
        {
            var state = InvokeLayers(lvo, registers =>
            {
                registers.A[0] = layerInfo;
                registers.A[1] = bitMap;
                registers.A[2] = superBitMap;
                registers.D[0] = unchecked((uint)minX);
                registers.D[1] = unchecked((uint)minY);
                registers.D[2] = unchecked((uint)maxX);
                registers.D[3] = unchecked((uint)maxY);
                registers.D[4] = (uint)flags;
                customize?.Invoke(registers);
            });
            Assert.NotEqual(0u, state.D[0]);
            return state.D[0];
        }

        internal CallbackProbe CreateCallbackProbe(
            uint displayBitMap,
            uint superBitMap,
            byte? writePattern = null)
            => new(this, displayBitMap, superBitMap, writePattern);

        internal uint CreateSingleRectangleRegion(
            short minX,
            short minY,
            short maxX,
            short maxY)
        {
            var region = Allocate(Region.Size);
            var rectangle = Allocate(RegionRectangle.Size);
            var memory = new LayersTestGuestMemory(Bus);
            var regionAddress = APTR.FromPointer(region);
            var rectangleAddress = APTR.FromPointer(rectangle);
            LayersRegionCodec.WriteBounds(ref memory, regionAddress,
                LayersRectangleCodec.Create(minX, minY, maxX, maxY));
            LayersRegionCodec.WriteFirst(ref memory, regionAddress, rectangleAddress);
            LayersRegionRectangleCodec.WritePrevious(
                ref memory,
                rectangleAddress,
                LayersRegionCodec.HeadAnchor(regionAddress));
            LayersRegionRectangleCodec.WriteBounds(ref memory, rectangleAddress,
                LayersRectangleCodec.Create(
                    0, 0,
                    checked((short)(maxX - minX)),
                    checked((short)(maxY - minY))));
            return region;
        }

        internal string InvokeDoHookClipRects(
            uint layer,
            uint displayBitMap,
            uint superBitMap,
            short minX,
            short minY,
            short maxX,
            short maxY,
            bool includeClipRectChain = true)
        {
            var hook = CreateCallbackProbe(displayBitMap, superBitMap);
            var bounds = Allocate(Rectangle.Size);
            WriteBounds(Bus, bounds, minX, minY, maxX, maxY);

            var rastPort = ReadOracleLayerPointer(
                Bus, layer, OracleLayerPointer.RastPort);
            InvokeLayers(LayersLvo.DoHookClipRects, state =>
            {
                state.A[0] = hook.Hook;
                state.A[1] = rastPort;
                state.A[2] = bounds;
            });
            return includeClipRectChain
                ? hook.Describe(layer)
                : hook.DescribeHookCalls(layer);
        }
    }

    private sealed class CallbackProbe : IDisposable
    {
        private readonly OracleContext _context;
        private readonly uint _displayBitMap;
        private readonly uint _superBitMap;
        private readonly byte? _writePattern;
        private readonly List<CallbackObservation> _observations = new();
        private readonly uint _entry;
        private readonly uint _entryToken;
        private bool _disposed;

        internal CallbackProbe(
            OracleContext context,
            uint displayBitMap,
            uint superBitMap,
            byte? writePattern)
        {
            _context = context;
            _displayBitMap = displayBitMap;
            _superBitMap = superBitMap;
            _writePattern = writePattern;
            Hook = context.Allocate(global::Amiga.Hook.Size);
            _entry = context.Allocate(6);
            _entryToken = context.Bus.RegisterHostGateway(_entry, Capture);
            WriteOracleHookEntry(context.Bus, Hook, _entry);
        }

        internal uint Hook { get; }
        internal int CallbackCount => _observations.Count;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _context.Bus.RemoveHostGateway(_entry, _entryToken);
            _context.Free(_entry, 6);
            _context.Free(Hook, global::Amiga.Hook.Size);
        }

        internal string Describe(uint expectedLayer)
        {
            var finalRastPort = expectedLayer == 0
                ? 0
                : ReadOracleLayerPointer(
                    _context.Bus, expectedLayer, OracleLayerPointer.RastPort);
            var finalClipRect = expectedLayer == 0
                ? 0
                : ReadOracleLayerPointer(
                    _context.Bus, expectedLayer, OracleLayerPointer.ClipRect);
            var messages = _observations.Select((observation, index) =>
                $"m{index}:layer={observation.Layer == expectedLayer}:" +
                $"target-rp={observation.TargetRastPort == observation.RastPortDuring}:" +
                $"target-final-rp={observation.TargetRastPort == finalRastPort}:" +
                $"rp-bitmap={observation.RastPortBitMapKind}:" +
                $"chain={observation.Chain}:" +
                $"bounds={observation.Bounds}:" +
                $"offset={observation.OffsetX},{observation.OffsetY}:" +
                $"linked={observation.Linked}:top={observation.TopLayer == expectedLayer}:" +
                $"head-final={observation.ClipRect == finalClipRect}:" +
                $"layer-bounds={observation.LayerBounds}:flags={observation.Flags}:" +
                $"cr={observation.ClipRects}:" +
                $"super-cr={observation.SuperClipRects}:" +
                $"damage={observation.Damage}");
            return $"callbacks={_observations.Count}:[{string.Join('|', messages)}]";
        }

        internal string DescribeHookCalls(uint expectedLayer)
        {
            var finalRastPort = expectedLayer == 0
                ? 0
                : ReadOracleLayerPointer(
                    _context.Bus, expectedLayer, OracleLayerPointer.RastPort);
            var messages = _observations
                .OrderBy(observation => observation.Bounds, StringComparer.Ordinal)
                .Select((observation, index) =>
                $"m{index}:layer={observation.Layer == expectedLayer}:" +
                $"target-rp={observation.TargetRastPort == observation.RastPortDuring}:" +
                $"target-final-rp={observation.TargetRastPort == finalRastPort}:" +
                $"rp-bitmap={observation.RastPortBitMapKind}:" +
                $"bounds={observation.Bounds}:" +
                $"offset={observation.OffsetX},{observation.OffsetY}:" +
                $"linked={observation.Linked}:damage={observation.Damage}");
            return $"callbacks={_observations.Count}:[{string.Join('|', messages)}]";
        }

        internal string DescribeCompact()
            => $"callbacks={_observations.Count}:[" +
                string.Join('|', _observations.Select((observation, index) =>
                    $"m{index}:{observation.RastPortBitMapKind}/" +
                    $"{observation.Chain}/{observation.Bounds}/" +
                    $"{observation.OffsetX},{observation.OffsetY}")) + "]";

        private void Capture(M68kCpuState state)
        {
            var bus = _context.Bus;
            var message = state.A[1];
            var memory = new LayersTestGuestMemory(bus);
            if (!LayersHookMessageCodec.TryRead(
                    ref memory,
                    APTR.FromPointer(message),
                    out LayerBackfillMessage callbackMessage))
            {
                _observations.Add(default);
                state.D[0] = 0;
                return;
            }
            var layer = callbackMessage.Layer.Raw;
            var validLayer = layer != 0 &&
                bus.IsMappedMemoryRange(layer, checked((int)Layer.Size));
            var layerInfo = validLayer
                ? ReadOracleLayerPointer(bus, layer, OracleLayerPointer.LayerInfo)
                : 0;
            var topLayer = layerInfo != 0 &&
                bus.IsMappedMemoryRange(layerInfo, checked((int)LayerInfo.Size))
                    ? ReadOracleTopLayer(bus, layerInfo)
                    : 0;
            var clipRect = validLayer
                ? ReadOracleLayerPointer(bus, layer, OracleLayerPointer.ClipRect)
                : 0;
            var superClipRect = validLayer
                ? ReadOracleLayerPointer(bus, layer, OracleLayerPointer.SuperClipRect)
                : 0;
            var damage = validLayer
                ? ReadOracleLayerPointer(bus, layer, OracleLayerPointer.DamageList)
                : 0;
            var rastPort = validLayer
                ? ReadOracleLayerPointer(bus, layer, OracleLayerPointer.RastPort)
                : 0;
            var rastPortBitMap = rastPort != 0 &&
                bus.IsMappedMemoryRange(rastPort, checked((int)RastPort.Size))
                    ? ReadOracleRastPortPointer(bus, rastPort, bitMap: true)
                    : 0;
            var flags = validLayer
                ? ReadOracleLayerFlags(bus, layer)
                : 0;
            const LayerFlags publicMask = LayerFlags.Simple | LayerFlags.Smart |
                LayerFlags.Super | LayerFlags.Backdrop | LayerFlags.Updating |
                LayerFlags.Refresh | LayerFlags.ClipRectsLost;
            _observations.Add(new CallbackObservation(
                layer,
                state.A[2],
                rastPort,
                rastPortBitMap == 0 ? "N" :
                    rastPortBitMap == _displayBitMap ? "D" :
                    rastPortBitMap == _superBitMap ? "U" : "O",
                ClassifyCallbackChain(
                    bus,
                    clipRect,
                    superClipRect,
                    callbackMessage.Bounds),
                FormatOracleRectangle(callbackMessage.Bounds),
                callbackMessage.OffsetX,
                callbackMessage.OffsetY,
                topLayer,
                IsLinked(bus, topLayer, layer),
                clipRect,
                validLayer
                    ? FormatOracleRectangle(ReadOracleLayerBounds(bus, layer))
                    : "invalid",
                flags & publicMask,
                DescribeClipRectChain(
                    _context,
                    clipRect,
                    _displayBitMap,
                    _superBitMap),
                DescribeClipRectChain(
                    _context,
                    superClipRect,
                    _displayBitMap,
                    _superBitMap),
                DescribeRegion(_context, damage)));
            if (_writePattern.HasValue)
                FillBitMap(bus, rastPortBitMap, _writePattern.Value);
            state.D[0] = 0;
        }

        private static string ClassifyCallbackChain(
            AmigaBus bus,
            uint publicHead,
            uint superHead,
            Rectangle messageBounds)
        {
            var inPublic = ChainContainsBounds(bus, publicHead, messageBounds);
            var inSuper = ChainContainsBounds(bus, superHead, messageBounds);
            return inPublic ? inSuper ? "P+S" : "P" : inSuper ? "S" : "N";
        }

        private static bool ChainContainsBounds(
            AmigaBus bus,
            uint head,
            Rectangle messageBounds)
        {
            var seen = new HashSet<uint>();
            for (var node = head;
                 node != 0 && seen.Count < 256 && seen.Add(node);
                 node = ReadOracleClipRectPointer(
                     bus, node, OracleClipRectPointer.Next))
            {
                var bounds = ReadOracleClipRectBounds(bus, node);
                if (messageBounds.MinX >= bounds.MinX &&
                    messageBounds.MinY >= bounds.MinY &&
                    messageBounds.MaxX <= bounds.MaxX &&
                    messageBounds.MaxY <= bounds.MaxY)
                {
                    return true;
                }
            }
            return false;
        }

        private static void FillBitMap(AmigaBus bus, uint bitMap, byte value)
        {
            if (bitMap == 0 ||
                !bus.IsMappedMemoryRange(bitMap, checked((int)BitMap.Size)))
                return;
            var bytesPerRow = ReadOracleBitMapBytesPerRow(bus, bitMap);
            var rows = ReadOracleBitMapRows(bus, bitMap);
            var depth = ReadOracleBitMapDepth(bus, bitMap);
            var byteCount = checked((int)bytesPerRow * rows);
            for (var plane = 0; plane < Math.Min(depth, (byte)8); plane++)
            {
                var address = ReadOracleBitMapPlane(bus, bitMap, plane);
                if (!bus.IsMappedMemoryRange(address, byteCount))
                    continue;
                for (var offset = 0; offset < byteCount; offset++)
                    bus.WriteByte(address + checked((uint)offset), value, 0);
            }
        }

        private static bool IsLinked(AmigaBus bus, uint topLayer, uint layer)
        {
            var seen = new HashSet<uint>();
            for (var candidate = topLayer;
                 candidate != 0 && seen.Count < 256 && seen.Add(candidate);
                 candidate = ReadOracleLayerPointer(
                     bus, candidate, OracleLayerPointer.Back))
            {
                if (candidate == layer)
                    return true;
                if (!bus.IsMappedMemoryRange(candidate, checked((int)Layer.Size)))
                    return false;
            }
            return false;
        }
    }

    private readonly record struct CallbackObservation(
        uint Layer,
        uint TargetRastPort,
        uint RastPortDuring,
        string RastPortBitMapKind,
        string Chain,
        string Bounds,
        int OffsetX,
        int OffsetY,
        uint TopLayer,
        bool Linked,
        uint ClipRect,
        string LayerBounds,
        LayerFlags Flags,
        string ClipRects,
        string SuperClipRects,
        string Damage);

    private sealed class NativeGraphicsProbe
    {
        private readonly AmigaBus _bus;
        private readonly Dictionary<int, int> _calls = new();
        private readonly List<int> _sequence = new();
        private readonly List<(uint Address, uint Token)> _gateways = new();
        private readonly Dictionary<int, NativeGraphicsVector> _nativeVectors = new();

        internal NativeGraphicsProbe(AmigaBus bus, uint graphicsBase)
        {
            _bus = bus;
            var negativeSize = ReadOracleLibraryNegativeSize(bus, graphicsBase);
            for (var magnitude = 6; magnitude <= negativeSize; magnitude += 6)
            {
                var lvo = -magnitude;
                var vector = unchecked((uint)((int)graphicsBase + lvo));
                if (bus.ReadWord(vector) != 0x4EF9)
                    continue;
                var target = bus.ReadLong(vector + 2);
                _nativeVectors.Add(lvo, new NativeGraphicsVector(vector, target));
                var captured = lvo;
                var token = bus.RegisterHostGateway(vector, state =>
                {
                    Record(captured);
                    state.ProgramCounter = target;
                });
                _gateways.Add((vector, token));
            }
        }

        internal NativeGraphicsVector GetNativeVector(int lvo)
        {
            Assert.True(_nativeVectors.TryGetValue(lvo, out var vector));
            return vector;
        }

        internal void Record(int lvo)
        {
            _calls[lvo] = _calls.GetValueOrDefault(lvo) + 1;
            _sequence.Add(lvo);
        }

        internal Dictionary<int, int> Snapshot()
            => new(_calls);

        internal int SequenceCount => _sequence.Count;

        internal string DescribeOrder(int start)
            => start >= _sequence.Count
                ? "none"
                : string.Join('>', _sequence.Skip(start).Select(GraphicsName));

        internal string DescribeDelta(IReadOnlyDictionary<int, int> before)
        {
            var deltas = _calls
                .Select(pair => new KeyValuePair<int, int>(
                    pair.Key,
                    pair.Value - before.GetValueOrDefault(pair.Key)))
                .Where(pair => pair.Value != 0)
                .OrderByDescending(pair => pair.Key)
                .Select(pair => $"{GraphicsName(pair.Key)}({pair.Key})x{pair.Value}")
                .ToArray();
            return deltas.Length == 0 ? "none" : string.Join(',', deltas);
        }

        private static string GraphicsName(int lvo)
            => lvo switch
            {
                -30 => "BltBitMap",
                -66 => "SetFont",
                -198 => "InitRastPort",
                -228 => "WaitBlit",
                -234 => "SetRast",
                -300 => "BltClear",
                -306 => "RectFill",
                -396 => "ScrollRaster",
                -432 => "LockLayerRom",
                -438 => "UnlockLayerRom",
                -444 => "SyncSBitMap",
                -450 => "CopySBitMap",
                -456 => "OwnBlitter",
                -462 => "DisownBlitter",
                -516 => "NewRegion",
                -528 => "ClearRegion",
                -552 => "ClipBlit",
                -606 => "BltBitMapRastPort",
                -918 => "AllocBitMap",
                -924 => "FreeBitMap",
                -960 => "GetBitMapAttr",
                _ => "Lvo"
            };
    }

    private readonly record struct NativeGraphicsVector(uint Address, uint Target);

    /// <summary>
    /// Reversible native-oracle leaf for planar pixel effects. Native V40
    /// Layers and graphics code still chooses every CopySBitMap/SyncSBitMap
    /// rectangle and call order; only graphics.library/BltBitMap is completed
    /// synchronously through the production software planar operation. This
    /// avoids treating an early-resident, headless custom-chip blit as complete
    /// before its destination bytes are authoritative.
    /// </summary>
    private sealed class DeterministicNativeBltBitMapLeaf : IDisposable
    {
        private const int BltBitMapLvo = -30;
        private readonly AmigaBus _bus;
        private readonly NativeGraphicsProbe _probe;
        private readonly uint _vector;
        private readonly ushort _hostOpcodeBefore;
        private readonly uint _hostTokenBefore;
        private readonly IDisposable _interposition;
        private readonly BusGraphicsMemory _memory;
        private readonly List<DeterministicBltBitMapCall> _calls = new();
        private bool _disposed;

        internal DeterministicNativeBltBitMapLeaf(
            AmigaBus bus,
            uint graphicsBase,
            NativeGraphicsProbe probe)
        {
            _bus = bus;
            _probe = probe;
            var native = probe.GetNativeVector(BltBitMapLvo);
            _vector = unchecked((uint)((int)graphicsBase + BltBitMapLvo));
            Assert.Equal(_vector, native.Address);
            Assert.InRange(native.Target, 0x00F8_0000u, 0x00FF_FFFFu);
            Assert.True(bus.HasHostGateway(_vector));
            _hostOpcodeBefore = bus.ReadWord(_vector);
            _hostTokenBefore = bus.ReadLong(_vector + 2);
            _memory = new BusGraphicsMemory(bus);
            _interposition = bus.InterposeHostGateway(
                _vector,
                Execute);
            Assert.True(bus.HasHostGateway(_vector));
            Assert.True(
                _hostOpcodeBefore != bus.ReadWord(_vector) ||
                _hostTokenBefore != bus.ReadLong(_vector + 2),
                "The deterministic native graphics interposition did not " +
                "publish a distinct host gateway token.");
        }

        internal int CallCount { get; private set; }

        internal string DescribeCalls()
            => DescribeCalls(0);

        internal string DescribeCalls(int start)
            => _calls.Count == 0
                ? "none"
                : string.Join('\n', _calls.Skip(start).Select((call, index) =>
                    $"{start + index}:src=0x{call.Source:X8}@{call.SourceX},{call.SourceY}:" +
                    $"dst=0x{call.Destination:X8}@{call.DestinationX},{call.DestinationY}:" +
                    $"size={call.Width}x{call.Height}:minterm=0x{call.Minterm:X2}:" +
                    $"mask=0x{call.Mask:X2}:temp=0x{call.TempA:X8}:" +
                    $"result={call.Result}"));

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _interposition.Dispose();
            Assert.True(_bus.HasHostGateway(_vector));
            Assert.Equal(_hostOpcodeBefore, _bus.ReadWord(_vector));
            Assert.Equal(_hostTokenBefore, _bus.ReadLong(_vector + 2));
        }

        private M68kHostGatewayResult Execute(
            M68kCpuState state,
            Func<M68kCpuState, M68kHostGatewayResult> native)
        {
            var result = GraphicsBlitOperations.BltBitMap(
                _memory,
                state.A[0],
                unchecked((short)state.D[0]),
                unchecked((short)state.D[1]),
                state.A[1],
                unchecked((short)state.D[2]),
                unchecked((short)state.D[3]),
                unchecked((short)state.D[4]),
                unchecked((short)state.D[5]),
                (byte)state.D[6],
                (byte)state.D[7],
                state.A[2]);
            // Preserve native failure and malformed-endpoint behavior. The
            // deterministic leaf claims only calls accepted by the same
            // guarded planar model used by CopperStart; declined calls resume
            // through the original native vector and are still probe-counted.
            if (result == GraphicsRasterOperations.Failure)
                return native(state);

            CallCount++;
            _probe.Record(BltBitMapLvo);
            _calls.Add(new DeterministicBltBitMapCall(
                state.A[0],
                unchecked((short)state.D[0]),
                unchecked((short)state.D[1]),
                state.A[1],
                unchecked((short)state.D[2]),
                unchecked((short)state.D[3]),
                unchecked((short)state.D[4]),
                unchecked((short)state.D[5]),
                (byte)state.D[6],
                (byte)state.D[7],
                state.A[2],
                result));
            state.D[0] = unchecked((uint)result);
            return M68kHostGatewayResult.Completed;
        }
    }

    private readonly record struct DeterministicBltBitMapCall(
        uint Source,
        short SourceX,
        short SourceY,
        uint Destination,
        short DestinationX,
        short DestinationY,
        short Width,
        short Height,
        byte Minterm,
        byte Mask,
        uint TempA,
        int Result);

    private sealed class BusGraphicsMemory : IGraphicsMemory
    {
        private readonly AmigaBus _bus;

        internal BusGraphicsMemory(AmigaBus bus) => _bus = bus;

        public bool TryReadByte(uint address, out byte value)
        {
            if (!_bus.IsMappedMemoryRange(address, 1))
            {
                value = 0;
                return false;
            }
            value = _bus.ReadByte(address);
            return true;
        }

        public bool TryReadWord(uint address, out ushort value)
        {
            if (!_bus.IsMappedMemoryRange(address, 2))
            {
                value = 0;
                return false;
            }
            value = _bus.ReadWord(address);
            return true;
        }

        public bool TryReadLong(uint address, out uint value)
        {
            if (!_bus.IsMappedMemoryRange(address, 4))
            {
                value = 0;
                return false;
            }
            value = _bus.ReadLong(address);
            return true;
        }

        public bool TryWriteByte(uint address, byte value)
        {
            if (!_bus.IsMappedMemoryRange(address, 1))
                return false;
            _bus.WriteByte(address, value, 0);
            return true;
        }

        public bool TryWriteWord(uint address, ushort value)
        {
            if (!_bus.IsMappedMemoryRange(address, 2))
                return false;
            _bus.WriteWord(address, value);
            return true;
        }

        public bool TryWriteLong(uint address, uint value)
        {
            if (!_bus.IsMappedMemoryRange(address, 4))
                return false;
            _bus.WriteLong(address, value);
            return true;
        }
    }

    private interface IOrdinalFault : IDisposable
    {
        int Count { get; }
    }

    private sealed class PortableOrdinalFault : IOrdinalFault
    {
        private readonly IDisposable _scope;
        private readonly Func<int> _count;

        internal PortableOrdinalFault(IDisposable scope, Func<int> count)
        {
            _scope = scope;
            _count = count;
        }

        public int Count => _count();

        public void Dispose() => _scope.Dispose();
    }

    private sealed class GuestVectorOrdinalFault : IOrdinalFault
    {
        private const uint AllocationBytes = 64;
        private readonly OracleContext _context;
        private readonly uint _libraryBase;
        private readonly int _lvo;
        private readonly uint _vector;
        private readonly ushort _originalOpcode;
        private readonly uint _originalVectorLong;
        private readonly uint _originalFunction;
        private readonly ulong _originalHash;
        private readonly uint _wrapper;
        private readonly uint _count;
        private readonly IDisposable? _hostInterposition;
        private int _hostCount;
        private bool _disposed;

        internal GuestVectorOrdinalFault(
            OracleContext context,
            uint libraryBase,
            int lvo,
            int failOrdinal)
        {
            Assert.True(context.Native);
            Assert.True(failOrdinal >= 0);
            _context = context;
            _libraryBase = libraryBase;
            _lvo = lvo;
            _vector = unchecked((uint)((int)libraryBase + lvo));
            _originalOpcode = context.Bus.ReadWord(_vector);
            _originalVectorLong = context.Bus.ReadLong(_vector + 2);
            _originalHash = HashVector(context.Bus, _vector);

            if (context.Bus.HasHostGateway(_vector))
            {
                _wrapper = 0;
                _count = 0;
                _originalFunction = 0;
                _hostInterposition = context.Bus.InterposeHostGateway(
                    _vector,
                    (state, next) =>
                    {
                        _hostCount++;
                        if (_hostCount == failOrdinal)
                        {
                            state.D[0] = 0;
                            return M68kHostGatewayResult.Completed;
                        }
                        return next(state);
                    });
                Assert.NotEqual(_originalHash, HashVector(context.Bus, _vector));
                return;
            }

            _hostInterposition = null;
            _wrapper = context.Allocate(AllocationBytes);
            _count = _wrapper + 40;
            var fail = _wrapper + 44;
            var offset = 0u;
            WriteWord(context.Bus, _wrapper, ref offset, 0x2F00); // MOVE.L D0,-(SP)
            WriteWord(context.Bus, _wrapper, ref offset, 0x2039); // MOVE.L count,D0
            WriteLong(context.Bus, _wrapper, ref offset, _count);
            WriteWord(context.Bus, _wrapper, ref offset, 0x5280); // ADDQ.L #1,D0
            WriteWord(context.Bus, _wrapper, ref offset, 0x23C0); // MOVE.L D0,count
            WriteLong(context.Bus, _wrapper, ref offset, _count);
            WriteWord(context.Bus, _wrapper, ref offset, 0xB0B9); // CMP.L fail,D0
            WriteLong(context.Bus, _wrapper, ref offset, fail);
            WriteWord(context.Bus, _wrapper, ref offset, 0x6708); // BEQ.S fail
            WriteWord(context.Bus, _wrapper, ref offset, 0x201F); // MOVE.L (SP)+,D0
            WriteWord(context.Bus, _wrapper, ref offset, 0x4EF9); // JMP original
            var originalFunctionOperand = _wrapper + offset;
            WriteLong(context.Bus, _wrapper, ref offset, 0);
            WriteWord(context.Bus, _wrapper, ref offset, 0x201F); // fail: MOVE.L (SP)+,D0
            WriteWord(context.Bus, _wrapper, ref offset, 0x7000); // MOVEQ #0,D0
            WriteWord(context.Bus, _wrapper, ref offset, 0x4E75); // RTS
            Assert.Equal(38u, offset);
            context.Bus.WriteLong(_count, 0);
            context.Bus.WriteLong(fail, checked((uint)failOrdinal));

            _originalFunction = context.Invoke(
                context.ExecBase,
                ExecLvo.SetFunction,
                state =>
                {
                    state.A[1] = libraryBase;
                    state.A[0] = unchecked((uint)lvo);
                    state.D[0] = _wrapper;
                }).D[0];
            Assert.NotEqual(0u, _originalFunction);
            context.Bus.WriteLong(originalFunctionOperand, _originalFunction);
        }

        public int Count => _hostInterposition is not null
            ? _hostCount
            : checked((int)_context.Bus.ReadLong(_count));

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_hostInterposition is not null)
            {
                _hostInterposition.Dispose();
                Assert.Equal(_originalHash, HashVector(_context.Bus, _vector));
                return;
            }
            _ = _context.Invoke(
                _context.ExecBase,
                ExecLvo.SetFunction,
                state =>
                {
                    state.A[1] = _libraryBase;
                    state.A[0] = unchecked((uint)_lvo);
                    state.D[0] = _originalFunction;
                });
            _context.Bus.WriteWord(_vector, _originalOpcode);
            _context.Bus.WriteLong(_vector + 2, _originalVectorLong);
            var restoredHash = HashVector(_context.Bus, _vector);
            Assert.True(
                restoredHash == _originalHash,
                $"Vector restore mismatch at 0x{_vector:X8}: " +
                $"original={_originalOpcode:X4}/{_originalVectorLong:X8}/" +
                $"{_originalHash:X16}, restored=" +
                $"{_context.Bus.ReadWord(_vector):X4}/" +
                $"{_context.Bus.ReadLong(_vector + 2):X8}/{restoredHash:X16}.");
            _context.Free(_wrapper, AllocationBytes);
        }

        private static ulong HashVector(AmigaBus bus, uint vector)
        {
            var hash = 14695981039346656037UL;
            var opcode = bus.ReadWord(vector);
            var target = bus.ReadLong(vector + 2);
            Span<byte> bytes = stackalloc byte[6]
            {
                (byte)(opcode >> 8),
                (byte)opcode,
                (byte)(target >> 24),
                (byte)(target >> 16),
                (byte)(target >> 8),
                (byte)target
            };
            foreach (var value in bytes)
            {
                hash ^= value;
                hash *= 1099511628211UL;
            }
            return hash;
        }
    }

    private readonly record struct PlanarBitMap(
        uint Address,
        int BytesPerRow,
        int Height,
        uint[] Planes);

    private readonly record struct RepartitionFixture(
        uint LayerInfo,
        PlanarBitMap Display,
        uint Smart,
        uint Blocker);

    private readonly record struct CallbackFailureFixture(
        uint LayerInfo,
        PlanarBitMap Display,
        uint Blocker,
        CallbackProbe Hook);

    private static void WriteBounds(
        AmigaBus bus,
        uint address,
        short minX,
        short minY,
        short maxX,
        short maxY)
    {
        WriteOracleRectangle(bus, address, minX, minY, maxX, maxY);
    }

    private static void WriteWord(AmigaBus bus, uint address, ref uint offset, ushort value)
    {
        bus.WriteWord(address + offset, value);
        offset += 2;
    }

    private static void WriteLong(AmigaBus bus, uint address, ref uint offset, uint value)
    {
        bus.WriteLong(address + offset, value);
        offset += 4;
    }

    private static void WriteMoveLongA1DisplacementToAbsolute(
        AmigaBus bus,
        uint address,
        ref uint offset,
        ushort displacement,
        uint destination)
    {
        WriteWord(bus, address, ref offset, 0x23E9); // MOVE.L d16(A1),(abs).L
        WriteWord(bus, address, ref offset, displacement);
        WriteLong(bus, address, ref offset, destination);
    }
}
