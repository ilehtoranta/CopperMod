using Amiga;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed partial class KickstartRomLayersDifferentialTests
{
    [Fact]
    public void MoveSizeLayerOcclusionTransitionMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native MoveSizeLayer comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            Assert.Equal((ushort)40,
                ReadOracleLibraryVersion(native.Bus, native.LayersBase));
            var vector = unchecked((uint)((int)native.LayersBase +
                (int)LayersLvo.MoveSizeLayer));
            Assert.False(native.Bus.HasHostGateway(vector));
            Assert.Equal((ushort)0x4EF9, native.Bus.ReadWord(vector));
            expected = TraceMoveSizeLayerOcclusionTransition(native);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceMoveSizeLayerOcclusionTransition(copperStart);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G244 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G244 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void MovingLowerSimpleLayerWithinOverlapMatchesNativeDamageV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native MoveSizeLayer damage comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceMoveSizeLowerLayerWithinOverlap(native);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceMoveSizeLowerLayerWithinOverlap(copperStart);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native lower-layer MoveSize trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart lower-layer MoveSize trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MoveSizeLayerWithCallerRegionMatchesNativeV4063(
        bool emptyCallerRegion)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native caller-Region MoveSizeLayer comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceMoveSizeLayerWithCallerRegion(native,
                emptyCallerRegion);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceMoveSizeLayerWithCallerRegion(copperStart,
                emptyCallerRegion);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine($"Native G245 trace (empty={emptyCallerRegion}):\n" +
            string.Join("\n", expected));
        _output.WriteLine($"CopperStart G245 trace (empty={emptyCallerRegion}):\n" +
            string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BehindLayerOverlapWithCallerRegionMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native BehindLayer comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceBehindLayerOverlapWithCallerRegion(native);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceBehindLayerOverlapWithCallerRegion(copperStart);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G246 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G246 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void UpfrontLayerOverlapWithCallerRegionMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native UpfrontLayer comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceUpfrontLayerOverlapWithCallerRegion(native);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceUpfrontLayerOverlapWithCallerRegion(copperStart);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G247 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G247 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DeleteTopLayerStillOccludedByMiddleMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native nested-layer deletion comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteTopStillOccludedByMiddle(native);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteTopStillOccludedByMiddle(copperStart);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G248 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G248 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DeleteMiddleLayerExposesOnlyPixelsOutsideTopMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native partial nested-layer deletion comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteMiddleOutsideTop(native);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteMiddleOutsideTop(copperStart);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G249 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G249 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DeleteMiddleLayerHonorsLowerCallerRegionOutsideTopMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native caller-Region nested-layer deletion comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteMiddleWithCallerRegion(native,
                emptyCallerRegion: false);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteMiddleWithCallerRegion(copperStart,
                emptyCallerRegion: false);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G250 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G250 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DeleteMiddleLayerRetainsDamageWithEmptyLowerCallerRegionMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native empty caller-Region nested-layer deletion comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteMiddleWithCallerRegion(native,
                emptyCallerRegion: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteMiddleWithCallerRegion(copperStart,
                emptyCallerRegion: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G251 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G251 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DeleteMiddleLayerClipsExposureAtSurvivingTopMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native partially clipped nested-layer deletion comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteMiddleWithCallerRegion(native,
                emptyCallerRegion: false,
                regionMinX: 6,
                regionMinY: 0,
                regionMaxX: 25,
                regionMaxY: 15);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteMiddleWithCallerRegion(copperStart,
                emptyCallerRegion: false,
                regionMinX: 6,
                regionMinY: 0,
                regionMaxX: 25,
                regionMaxY: 15);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G252 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G252 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RestoreCallerRegionAfterEmptyRegionDeletionPreservesDamageMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native deferred-damage caller-Region restore comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteMiddleWithCallerRegion(native,
                emptyCallerRegion: true,
                restoreEmptyRegionAfterDelete: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteMiddleWithCallerRegion(copperStart,
                emptyCallerRegion: true,
                restoreEmptyRegionAfterDelete: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G253 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G253 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DoHookClipRectsAfterCallerRegionRestoreUsesVisibleExposureMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native deferred-damage clip-hook comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteMiddleWithCallerRegion(native,
                emptyCallerRegion: true,
                restoreEmptyRegionAfterDelete: true,
                invokeDoHookClipRectsAfterRestore: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteMiddleWithCallerRegion(copperStart,
                emptyCallerRegion: true,
                restoreEmptyRegionAfterDelete: true,
                invokeDoHookClipRectsAfterRestore: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G254 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G254 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BeginUpdateAfterDeferredExposureUsesDamageWithinVisibleRegionMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native deferred-exposure BeginUpdate comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteMiddleWithCallerRegion(native,
                emptyCallerRegion: true,
                restoreEmptyRegionAfterDelete: true,
                beginUpdateAfterRestore: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteMiddleWithCallerRegion(copperStart,
                emptyCallerRegion: true,
                restoreEmptyRegionAfterDelete: true,
                beginUpdateAfterRestore: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G255 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G255 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CompleteUpdateAfterDeferredExposureDrawsOnlyPendingDamageMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native completed deferred-exposure refresh comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteMiddleWithCallerRegion(native,
                emptyCallerRegion: true,
                restoreEmptyRegionAfterDelete: true,
                beginUpdateAfterRestore: true,
                completeRefreshAfterUpdate: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteMiddleWithCallerRegion(copperStart,
                emptyCallerRegion: true,
                restoreEmptyRegionAfterDelete: true,
                beginUpdateAfterRestore: true,
                completeRefreshAfterUpdate: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G256 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G256 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CompleteUpdateWithNarrowCallerRegionMatchesNativeDamageRetirementV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native narrow caller-Region damage-retirement comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteMiddleWithCallerRegion(native,
                emptyCallerRegion: true,
                restoreEmptyRegionAfterDelete: true,
                beginUpdateAfterRestore: true,
                completeRefreshAfterUpdate: true,
                restoredRegionMinX: 8,
                restoredRegionMinY: 0,
                restoredRegionMaxX: 9,
                restoredRegionMaxY: 1,
                widenRegionAfterComplete: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteMiddleWithCallerRegion(copperStart,
                emptyCallerRegion: true,
                restoreEmptyRegionAfterDelete: true,
                beginUpdateAfterRestore: true,
                completeRefreshAfterUpdate: true,
                restoredRegionMinX: 8,
                restoredRegionMinY: 0,
                restoredRegionMaxX: 9,
                restoredRegionMaxY: 1,
                widenRegionAfterComplete: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G257 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G257 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IncompleteUpdateWithNarrowCallerRegionRetainsUnselectedDamageMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native incomplete narrow caller-Region comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteMiddleWithCallerRegion(native,
                emptyCallerRegion: true,
                restoreEmptyRegionAfterDelete: true,
                beginUpdateAfterRestore: true,
                restoredRegionMinX: 8,
                restoredRegionMinY: 0,
                restoredRegionMaxX: 9,
                restoredRegionMaxY: 1,
                widenRegionAfterIncomplete: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteMiddleWithCallerRegion(copperStart,
                emptyCallerRegion: true,
                restoreEmptyRegionAfterDelete: true,
                beginUpdateAfterRestore: true,
                restoredRegionMinX: 8,
                restoredRegionMinY: 0,
                restoredRegionMaxX: 9,
                restoredRegionMaxY: 1,
                widenRegionAfterIncomplete: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G258 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G258 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DeleteMiddleThenTopAccumulatesNewlyExposedDamageMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native sequential nested-layer deletion comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteMiddleThenTopExposure(native);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteMiddleThenTopExposure(copperStart);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G259 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G259 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DeleteTopThenMiddleAccumulatesNewlyExposedDamageMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native reverse nested-layer deletion comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteTopThenMiddleExposure(native);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteTopThenMiddleExposure(copperStart);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G260 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G260 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IncompleteUpdateAfterSequentialDeletionRetainsAccumulatedDamageMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native sequential-deletion BeginUpdate comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteMiddleThenTopExposure(native,
                beginIncompleteUpdate: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteMiddleThenTopExposure(copperStart,
                beginIncompleteUpdate: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G261 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G261 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CompleteUpdateAfterSequentialDeletionDrawsAccumulatedDamageMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native sequential-deletion complete refresh comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteMiddleThenTopExposure(native,
                completeUpdateAfterDeletion: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteMiddleThenTopExposure(copperStart,
                completeUpdateAfterDeletion: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G262 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G262 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CompleteUpdateAfterReverseSequentialDeletionDrawsAccumulatedDamageMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native reverse sequential-deletion complete refresh comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteTopThenMiddleExposure(native,
                completeUpdate: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteTopThenMiddleExposure(copperStart,
                completeUpdate: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G263 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G263 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CompleteUpdateWithNarrowRegionAfterReverseDeletionRetiresFullDamageMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native narrow-Region accumulated-damage refresh comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteTopThenMiddleExposure(native,
                completeUpdate: true,
                narrowCallerRegion: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteTopThenMiddleExposure(copperStart,
                completeUpdate: true,
                narrowCallerRegion: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G264 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G264 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IncompleteUpdateWithNarrowRegionAfterReverseDeletionRetainsDamageMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native incomplete narrow-Region accumulated-damage comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteTopThenMiddleExposure(native,
                incompleteUpdateAfterDeletion: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteTopThenMiddleExposure(copperStart,
                incompleteUpdateAfterDeletion: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G265 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G265 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IncompleteNarrowRegionDrawAfterReverseDeletionRetainsDamageMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native incomplete narrow-Region draw comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteTopThenMiddleExposure(native,
                incompleteUpdateAfterDeletion: true,
                drawDuringIncompleteUpdate: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteTopThenMiddleExposure(copperStart,
                incompleteUpdateAfterDeletion: true,
                drawDuringIncompleteUpdate: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G266 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G266 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void EmptyCallerRegionAfterReverseDeletionPreservesAccumulatedDamageMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native empty-Region accumulated-damage comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteTopThenMiddleExposure(native,
                exerciseEmptyRegionAfterDeletion: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteTopThenMiddleExposure(copperStart,
                exerciseEmptyRegionAfterDeletion: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G267 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G267 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BeginUpdateWithEmptyRegionAfterReverseDeletionMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native empty-Region BeginUpdate comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteTopThenMiddleExposure(native,
                exerciseEmptyRegionAfterDeletion: true,
                beginUpdateWhileEmptyRegion: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteTopThenMiddleExposure(copperStart,
                exerciseEmptyRegionAfterDeletion: true,
                beginUpdateWhileEmptyRegion: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G268 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G268 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CompleteUpdateWithEmptyRegionAfterReverseDeletionMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native empty-Region complete EndUpdate comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteTopThenMiddleExposure(native,
                exerciseEmptyRegionAfterDeletion: true,
                completeUpdateWhileEmptyRegion: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteTopThenMiddleExposure(copperStart,
                exerciseEmptyRegionAfterDeletion: true,
                completeUpdateWhileEmptyRegion: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G269 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G269 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DoHookClipRectsWithEmptyAndRestoredRegionsAfterReverseDeletionMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native empty-Region DoHookClipRects comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteTopThenMiddleExposure(native,
                exerciseEmptyRegionAfterDeletion: true,
                invokeDoHookClipRectsAcrossEmptyRegion: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteTopThenMiddleExposure(copperStart,
                exerciseEmptyRegionAfterDeletion: true,
                invokeDoHookClipRectsAcrossEmptyRegion: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G270 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G270 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DoHookClipRectsDuringBeginUpdateWithEmptyRegionMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native empty-Region BeginUpdate DoHookClipRects comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteTopThenMiddleExposure(native,
                exerciseEmptyRegionAfterDeletion: true,
                beginUpdateWhileEmptyRegion: true,
                invokeDoHookClipRectsAcrossEmptyRegion: true,
                invokeDoHookDuringEmptyUpdate: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteTopThenMiddleExposure(copperStart,
                exerciseEmptyRegionAfterDeletion: true,
                beginUpdateWhileEmptyRegion: true,
                invokeDoHookClipRectsAcrossEmptyRegion: true,
                invokeDoHookDuringEmptyUpdate: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G271 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G271 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DoHookClipRectsAfterRestoringCallerRegionDuringUpdateMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native restored-Region BeginUpdate DoHookClipRects comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteTopThenMiddleExposure(native,
                exerciseEmptyRegionAfterDeletion: true,
                beginUpdateWhileEmptyRegion: true,
                invokeDoHookClipRectsAcrossEmptyRegion: true,
                invokeDoHookDuringEmptyUpdate: true,
                restoreCallerRegionDuringEmptyUpdate: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteTopThenMiddleExposure(copperStart,
                exerciseEmptyRegionAfterDeletion: true,
                beginUpdateWhileEmptyRegion: true,
                invokeDoHookClipRectsAcrossEmptyRegion: true,
                invokeDoHookDuringEmptyUpdate: true,
                restoreCallerRegionDuringEmptyUpdate: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G272 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G272 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CompleteEndUpdateAfterRestoringCallerRegionDuringUpdateMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native restored-Region complete EndUpdate comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteTopThenMiddleExposure(native,
                exerciseEmptyRegionAfterDeletion: true,
                beginUpdateWhileEmptyRegion: true,
                invokeDoHookClipRectsAcrossEmptyRegion: true,
                invokeDoHookDuringEmptyUpdate: true,
                completeUpdateWhileEmptyRegion: true,
                restoreCallerRegionDuringEmptyUpdate: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteTopThenMiddleExposure(copperStart,
                exerciseEmptyRegionAfterDeletion: true,
                beginUpdateWhileEmptyRegion: true,
                invokeDoHookClipRectsAcrossEmptyRegion: true,
                invokeDoHookDuringEmptyUpdate: true,
                completeUpdateWhileEmptyRegion: true,
                restoreCallerRegionDuringEmptyUpdate: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G273 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G273 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DoHookClipRectsAfterRestoringNarrowCallerRegionDuringUpdateMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native narrowed restored-Region BeginUpdate DoHookClipRects comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteTopThenMiddleExposure(native,
                narrowCallerRegion: true,
                exerciseEmptyRegionAfterDeletion: true,
                beginUpdateWhileEmptyRegion: true,
                invokeDoHookClipRectsAcrossEmptyRegion: true,
                invokeDoHookDuringEmptyUpdate: true,
                restoreCallerRegionDuringEmptyUpdate: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteTopThenMiddleExposure(copperStart,
                narrowCallerRegion: true,
                exerciseEmptyRegionAfterDeletion: true,
                beginUpdateWhileEmptyRegion: true,
                invokeDoHookClipRectsAcrossEmptyRegion: true,
                invokeDoHookDuringEmptyUpdate: true,
                restoreCallerRegionDuringEmptyUpdate: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G274 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G274 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RepeatedBeginUpdateRegionReplacementWithPendingDamageMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native nested-update Region replacement comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteTopThenMiddleExposure(native,
                exerciseEmptyRegionAfterDeletion: true,
                invokeDoHookClipRectsAcrossEmptyRegion: true,
                exerciseRepeatedBeginUpdateWithRegionReplacement: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteTopThenMiddleExposure(copperStart,
                exerciseEmptyRegionAfterDeletion: true,
                invokeDoHookClipRectsAcrossEmptyRegion: true,
                exerciseRepeatedBeginUpdateWithRegionReplacement: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G275 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G275 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CompleteUpdateAfterRepeatedBeginAndRegionReplacementMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native complete repeated-BeginUpdate Region replacement comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteTopThenMiddleExposure(native,
                exerciseEmptyRegionAfterDeletion: true,
                invokeDoHookClipRectsAcrossEmptyRegion: true,
                exerciseRepeatedBeginUpdateWithRegionReplacement: true,
                completeRepeatedBeginUpdateRegionReplacement: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteTopThenMiddleExposure(copperStart,
                exerciseEmptyRegionAfterDeletion: true,
                invokeDoHookClipRectsAcrossEmptyRegion: true,
                exerciseRepeatedBeginUpdateWithRegionReplacement: true,
                completeRepeatedBeginUpdateRegionReplacement: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G276 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G276 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void EmptyRegionRoundTripDuringUpdatePreservesDamageAndMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native empty-Region update round-trip comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteTopThenMiddleExposure(native,
                exerciseEmptyRegionAfterDeletion: true,
                exerciseEmptyRegionRoundTripDuringUpdate: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteTopThenMiddleExposure(copperStart,
                exerciseEmptyRegionAfterDeletion: true,
                exerciseEmptyRegionRoundTripDuringUpdate: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G277 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G277 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DisjointCallerRegionDuringUpdateProjectsHooksAndDamageMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native disjoint caller-Region update comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteTopThenMiddleExposure(native,
                exerciseEmptyRegionAfterDeletion: true,
                exerciseDisjointCallerRegionDuringUpdate: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteTopThenMiddleExposure(copperStart,
                exerciseEmptyRegionAfterDeletion: true,
                exerciseDisjointCallerRegionDuringUpdate: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G278 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G278 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void PartialDisjointCallerRegionDuringUpdateIntersectsDamageMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native partial-disjoint caller-Region update comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDeleteTopThenMiddleExposure(native,
                exerciseEmptyRegionAfterDeletion: true,
                exerciseDisjointCallerRegionDuringUpdate: true,
                partialDisjointCallerRegion: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDeleteTopThenMiddleExposure(copperStart,
                exerciseEmptyRegionAfterDeletion: true,
                exerciseDisjointCallerRegionDuringUpdate: true,
                partialDisjointCallerRegion: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G279 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G279 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TwoDimensionalDisjointCallerRegionDuringUpdateMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native two-dimensional caller-Region update comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceTwoDimensionalCallerRegionDuringUpdate(native);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceTwoDimensionalCallerRegionDuringUpdate(copperStart);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G280 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G280 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TwoDimensionalCallerRegionCompleteUpdateMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native complete two-dimensional caller-Region update comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceTwoDimensionalCallerRegionDuringUpdate(native,
                completeUpdate: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceTwoDimensionalCallerRegionDuringUpdate(copperStart,
                completeUpdate: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G281 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G281 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DisjointDamageIslandsIntersectDisjointCallerRegionDuringUpdateMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native disjoint-damage caller-Region update comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDisjointDamageIslandsDuringUpdate(native);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDisjointDamageIslandsDuringUpdate(copperStart);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G282 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G282 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DisjointDamageIslandsCompleteUpdateMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native complete disjoint-damage update comparison NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDisjointDamageIslandsDuringUpdate(native,
                completeUpdate: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDisjointDamageIslandsDuringUpdate(copperStart,
                completeUpdate: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G283 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G283 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FullLayerRectFillAcrossDisjointDamageDuringUpdateMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native RectFill across disconnected damage NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceDisjointDamageIslandsDuringUpdate(native,
                completeUpdate: true,
                drawFullLayerDuringUpdate: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceDisjointDamageIslandsDuringUpdate(copperStart,
                completeUpdate: true,
                drawFullLayerDuringUpdate: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G284 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G284 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FullLayerRectFillUsesDisconnectedDamageOnlyMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native broad-Region RectFill damage clipping NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceBroadRegionRectFillAcrossDisjointDamage(native);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceBroadRegionRectFillAcrossDisjointDamage(copperStart);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G285 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G285 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FullLayerRectFillRetainsDisconnectedDamageAfterIncompleteUpdateMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native incomplete RectFill damage retention NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceBroadRegionRectFillAcrossDisjointDamage(native,
                completeUpdate: false);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceBroadRegionRectFillAcrossDisjointDamage(copperStart,
                completeUpdate: false);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G286 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G286 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void EmptyCallerRegionSuppressesRectFillDuringDisconnectedDamageUpdateMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native empty-Region disconnected-damage RectFill NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceBroadRegionRectFillAcrossDisjointDamage(native,
                completeUpdate: false,
                exerciseEmptyRegionDuringUpdate: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceBroadRegionRectFillAcrossDisjointDamage(copperStart,
                completeUpdate: false,
                exerciseEmptyRegionDuringUpdate: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G287 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G287 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void EmptyCallerRegionRectFillCompleteUpdateMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native complete empty-Region disconnected-damage RectFill NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceBroadRegionRectFillAcrossDisjointDamage(native,
                completeUpdate: true,
                exerciseEmptyRegionDuringUpdate: true);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceBroadRegionRectFillAcrossDisjointDamage(copperStart,
                completeUpdate: true,
                exerciseEmptyRegionDuringUpdate: true);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G288 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G288 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SuperBitMapLayerMutationsDoNotPublishDamageMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native SuperBitMap DamageList reachability NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }

        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceSuperBitMapDamageReachability(native);
        }
        finally
        {
            native.Machine.Dispose();
        }

        var copperStart = CreateCopperStartOracle();
        string[] actual;
        try
        {
            actual = TraceSuperBitMapDamageReachability(copperStart);
        }
        finally
        {
            copperStart.Machine.Dispose();
        }

        _output.WriteLine("Native G289 trace:\n" + string.Join("\n", expected));
        _output.WriteLine("CopperStart G289 trace:\n" + string.Join("\n", actual));
        Assert.Equal(expected, actual);
    }

    private static string[] TraceTwoDimensionalCallerRegionDuringUpdate(
        OracleContext context,
        bool completeUpdate = false)
    {
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(48, 24, 1, 0x6B);
        var lower = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
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
            8,
            4,
            23,
            11);

        var originalRegion = context.CreateSingleRectangleRegion(0, 0, 31, 15);
        var originalRegionNode = ReadOracleRegionFirst(context.Bus,
            originalRegion);
        var originalRegionBytes = Enumerable.Range(0,
                checked((int)Region.Size))
            .Select(offset => context.Bus.ReadByte(originalRegion +
                checked((uint)offset))).ToArray();
        var originalRegionNodeBytes = Enumerable.Range(0,
                checked((int)RegionRectangle.Size))
            .Select(offset => context.Bus.ReadByte(originalRegionNode +
                checked((uint)offset))).ToArray();
        Assert.Equal(0u, context.InvokeLayers(LayersLvo.InstallClipRegion,
            state =>
            {
                state.A[0] = lower;
                state.A[1] = originalRegion;
            }).D[0]);

        var deleteBlocker = context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = blocker);
        Assert.NotEqual(0u, deleteBlocker.D[0]);
        context.WaitForBlitterIdle();
        Assert.Equal("y=0,15:x=0,31",
            MoveSizeVisibleCoverage(context, lower));
        var damage = ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.DamageList);
        var expectedDamagePixels = new HashSet<(int X, int Y)>();
        for (var y = 4; y <= 11; y++)
            for (var x = 8; x <= 23; x++)
                expectedDamagePixels.Add((x, y));
        Assert.True(ReadOracleRegionPixels(context, damage)
            .SetEquals(expectedDamagePixels));
        var damageDescription = DescribeRegion(context, damage);
        var damageBytes = Enumerable.Range(0, checked((int)Region.Size))
            .Select(offset => context.Bus.ReadByte(damage + checked((uint)offset)))
            .ToArray();

        var beginUpdate = context.InvokeLayers(LayersLvo.BeginUpdate,
            state => state.A[0] = lower).D[0];
        Assert.NotEqual(0u, beginUpdate);
        Assert.True((ReadOracleLayerFlags(context.Bus, lower) &
            LayerFlags.Updating) != 0);
        Assert.Equal("y=4,11:x=8,23",
            MoveSizeVisibleCoverage(context, lower));

        var callerRegion = context.Allocate(Region.Size);
        var firstNode = context.Allocate(RegionRectangle.Size);
        var secondNode = context.Allocate(RegionRectangle.Size);
        var memory = new LayersTestGuestMemory(context.Bus);
        var callerRegionAddress = APTR.FromPointer(callerRegion);
        var firstNodeAddress = APTR.FromPointer(firstNode);
        var secondNodeAddress = APTR.FromPointer(secondNode);
        LayersRegionCodec.WriteBounds(ref memory, callerRegionAddress,
            LayersRectangleCodec.Create(6, 2, 26, 13));
        LayersRegionCodec.WriteFirst(ref memory, callerRegionAddress,
            firstNodeAddress);
        LayersRegionRectangleCodec.WritePrevious(ref memory, firstNodeAddress,
            LayersRegionCodec.HeadAnchor(callerRegionAddress));
        LayersRegionRectangleCodec.WriteNext(ref memory, firstNodeAddress,
            secondNodeAddress);
        LayersRegionRectangleCodec.WriteBounds(ref memory, firstNodeAddress,
            LayersRectangleCodec.Create(0, 0, 4, 4));
        LayersRegionRectangleCodec.WritePrevious(ref memory, secondNodeAddress,
            firstNodeAddress);
        LayersRegionRectangleCodec.WriteNext(ref memory, secondNodeAddress,
            APTR.Null);
        LayersRegionRectangleCodec.WriteBounds(ref memory, secondNodeAddress,
            LayersRectangleCodec.Create(15, 7, 20, 11));
        var callerRegionBytes = Enumerable.Range(0,
                checked((int)Region.Size))
            .Select(offset => context.Bus.ReadByte(callerRegion +
                checked((uint)offset))).ToArray();
        var firstNodeBytes = Enumerable.Range(0,
                checked((int)RegionRectangle.Size))
            .Select(offset => context.Bus.ReadByte(firstNode +
                checked((uint)offset))).ToArray();
        var secondNodeBytes = Enumerable.Range(0,
                checked((int)RegionRectangle.Size))
            .Select(offset => context.Bus.ReadByte(secondNode +
                checked((uint)offset))).ToArray();

        Assert.Equal(originalRegion,
            context.InvokeLayers(LayersLvo.InstallClipRegion, state =>
            {
                state.A[0] = lower;
                state.A[1] = callerRegion;
            }).D[0]);
        const string activeCoverage = "y=4,6:x=8,10;y=9,11:x=21,23";
        const string stableCoverage = "y=2,6:x=6,10;y=9,13:x=21,26";
        Assert.Equal(callerRegion, ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.ClipRegion));
        Assert.Equal(activeCoverage, MoveSizeVisibleCoverage(context, lower));
        Assert.Equal(damage, ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.DamageList));
        Assert.Equal(damageDescription, DescribeRegion(context, damage));
        Assert.Equal(damageBytes, Enumerable.Range(0, damageBytes.Length)
            .Select(offset => context.Bus.ReadByte(damage + checked((uint)offset)))
            .ToArray());

        var hookTrace = context.InvokeDoHookClipRects(
            lower,
            display.Address,
            0,
            0,
            0,
            31,
            15,
            includeClipRectChain: false);
        Assert.Contains("callbacks=2", hookTrace, StringComparison.Ordinal);
        Assert.Contains("layer=True", hookTrace, StringComparison.Ordinal);
        Assert.Contains("target-final-rp=True", hookTrace,
            StringComparison.Ordinal);
        var sortedHookBounds = SortedDoHookClipRectBounds(hookTrace);
        Assert.Equal("8,4,10,6;21,9,23,11", sortedHookBounds);
        Assert.Equal(damage, ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.DamageList));
        Assert.Equal(damageDescription, DescribeRegion(context, damage));

        context.InvokeLayers(LayersLvo.EndUpdate, state =>
        {
            state.A[0] = lower;
            state.D[0] = completeUpdate ? 1u : 0u;
        });
        var flagsAfterUpdate = ReadOracleLayerFlags(context.Bus, lower);
        Assert.True((flagsAfterUpdate & LayerFlags.Updating) == 0);
        Assert.True((flagsAfterUpdate & LayerFlags.Refresh) != 0);
        Assert.Equal(callerRegion, ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.ClipRegion));
        Assert.Equal(stableCoverage, MoveSizeVisibleCoverage(context, lower));
        Assert.Equal(damage, ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.DamageList));
        var damageBytesAfterUpdate = Enumerable.Range(0, damageBytes.Length)
            .Select(offset => context.Bus.ReadByte(damage + checked((uint)offset)))
            .ToArray();
        var damagePixelsAfterUpdate = ReadOracleRegionPixels(context, damage);
        var damageDescriptionAfterUpdate = DescribeRegion(context, damage);
        if (completeUpdate)
        {
            Assert.Equal(new byte[checked((int)Region.Size)],
                damageBytesAfterUpdate);
            Assert.Empty(damagePixelsAfterUpdate);
        }
        else
        {
            Assert.Equal(damageBytes, damageBytesAfterUpdate);
            Assert.Equal(expectedDamagePixels, damagePixelsAfterUpdate);
            Assert.Equal(damageDescription, damageDescriptionAfterUpdate);
        }
        Assert.Equal(callerRegionBytes,
            Enumerable.Range(0, callerRegionBytes.Length)
                .Select(offset => context.Bus.ReadByte(callerRegion +
                    checked((uint)offset))).ToArray());
        Assert.Equal(firstNodeBytes,
            Enumerable.Range(0, firstNodeBytes.Length)
                .Select(offset => context.Bus.ReadByte(firstNode +
                    checked((uint)offset))).ToArray());
        Assert.Equal(secondNodeBytes,
            Enumerable.Range(0, secondNodeBytes.Length)
                .Select(offset => context.Bus.ReadByte(secondNode +
                    checked((uint)offset))).ToArray());
        Assert.Equal(originalRegionBytes,
            Enumerable.Range(0, originalRegionBytes.Length)
                .Select(offset => context.Bus.ReadByte(originalRegion +
                    checked((uint)offset))).ToArray());
        Assert.Equal(originalRegionNodeBytes,
            Enumerable.Range(0, originalRegionNodeBytes.Length)
                .Select(offset => context.Bus.ReadByte(originalRegionNode +
                    checked((uint)offset))).ToArray());

        var trace = new[]
        {
            $"delete:blocker=true:damage={damageDescription}",
            $"begin-update:success={beginUpdate != 0}:active-before-replacement=y=4,11:x=8,23",
            $"install-region:previous=original:active=[{activeCoverage}]",
            $"hook:callbacks=2:sorted-bounds={sortedHookBounds}",
            $"end-update:complete={completeUpdate}:updating=false:refresh=true:stable=[{stableCoverage}]",
            completeUpdate
                ? "damage:identity=retained:contents=retired:caller-regions=preserved"
                : "damage:identity-and-content=retained:caller-regions=preserved"
        };

        Assert.Equal(callerRegion,
            context.InvokeLayers(LayersLvo.InstallClipRegion, state =>
            {
                state.A[0] = lower;
                state.A[1] = 0;
            }).D[0]);
        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = lower);
        context.DisposeLayerInfo(layerInfo);
        context.FreePlanarBitMap(display);
        context.Free(originalRegionNode, RegionRectangle.Size);
        context.Free(originalRegion, Region.Size);
        context.Free(firstNode, RegionRectangle.Size);
        context.Free(secondNode, RegionRectangle.Size);
        context.Free(callerRegion, Region.Size);
        return trace;
    }

    private static string[] TraceSuperBitMapDamageReachability(
        OracleContext context)
    {
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(48, 24, 1, 0x6B);
        var superBitMap = context.CreatePlanarBitMap(48, 24, 1, 0xB2);
        var superLayer = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            superBitMap.Address,
            LayerCreationFlags.Super,
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
            8,
            4,
            23,
            11);
        var initialDamage = ReadOracleLayerPointer(context.Bus, superLayer,
            OracleLayerPointer.DamageList);
        Assert.Empty(ReadOracleRegionPixels(context, initialDamage));
        var initialDamageDescription = DescribeRegion(context, initialDamage);

        var delete = context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = blocker);
        Assert.NotEqual(0u, delete.D[0]);
        context.WaitForBlitterIdle();
        var damageAfterDelete = ReadOracleLayerPointer(context.Bus, superLayer,
            OracleLayerPointer.DamageList);
        Assert.Empty(ReadOracleRegionPixels(context, damageAfterDelete));
        var afterDeleteDescription = DescribeRegion(context, damageAfterDelete);

        context.InvokeLayers(LayersLvo.ScrollLayer, state =>
        {
            state.A[1] = superLayer;
            state.D[0] = 1;
            state.D[1] = 1;
        });
        context.WaitForBlitterIdle();
        var damageAfterScroll = ReadOracleLayerPointer(context.Bus, superLayer,
            OracleLayerPointer.DamageList);
        Assert.Empty(ReadOracleRegionPixels(context, damageAfterScroll));
        var afterScrollDescription = DescribeRegion(context, damageAfterScroll);

        var moveSize = context.InvokeLayers(LayersLvo.MoveSizeLayer,
            state =>
            {
                state.A[0] = superLayer;
                state.D[0] = 1;
                state.D[1] = 0;
                state.D[2] = 0;
                state.D[3] = 0;
            });
        Assert.NotEqual(0u, moveSize.D[0]);
        context.WaitForBlitterIdle();
        var damageAfterMove = ReadOracleLayerPointer(context.Bus, superLayer,
            OracleLayerPointer.DamageList);
        Assert.Empty(ReadOracleRegionPixels(context, damageAfterMove));
        var afterMoveDescription = DescribeRegion(context, damageAfterMove);

        var beginResult = context.InvokeLayers(LayersLvo.BeginUpdate,
            state => state.A[0] = superLayer).D[0];
        var flagsAfterBegin = ReadOracleLayerFlags(context.Bus, superLayer);
        var activeDamage = ReadOracleLayerPointer(context.Bus, superLayer,
            OracleLayerPointer.DamageList);
        Assert.Empty(ReadOracleRegionPixels(context, activeDamage));
        var activeDamageDescription = DescribeRegion(context, activeDamage);
        var completedUpdate = (flagsAfterBegin & LayerFlags.Updating) != 0;
        if (completedUpdate)
        {
            context.InvokeLayers(LayersLvo.EndUpdate, state =>
            {
                state.A[0] = superLayer;
                state.D[0] = 0;
            });
            Assert.True((ReadOracleLayerFlags(context.Bus, superLayer) &
                LayerFlags.Updating) == 0);
        }

        var trace = new[]
        {
            $"create:super-layer=true:damage={initialDamageDescription}",
            $"delete:blocker=true:damage={afterDeleteDescription}",
            $"scroll:damage={afterScrollDescription}",
            $"move-size:result={moveSize.D[0] != 0}:damage={afterMoveDescription}",
            $"begin-update:result={beginResult != 0}:updating={completedUpdate}:damage={activeDamageDescription}",
            "clipblit:nonempty-active-super-damage=unreachable-through-tested-layer-operations"
        };

        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = superLayer);
        context.DisposeLayerInfo(layerInfo);
        context.FreePlanarBitMap(display);
        context.FreePlanarBitMap(superBitMap);
        return trace;
    }

    private static string SortedDoHookClipRectBounds(string hookTrace)
    {
        const string marker = "bounds=";
        var bounds = new List<(int MinX, int MinY, int MaxX, int MaxY)>();
        foreach (var callback in hookTrace.Split('|',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var markerIndex = callback.IndexOf(marker,
                StringComparison.Ordinal);
            if (markerIndex < 0)
                continue;
            var text = callback[(markerIndex + marker.Length)..]
                .Split(':', 2)[0];
            var coordinates = text.Split(',').Select(int.Parse).ToArray();
            Assert.Equal(4, coordinates.Length);
            bounds.Add((coordinates[0], coordinates[1], coordinates[2],
                coordinates[3]));
        }

        return string.Join(';', bounds.OrderBy(rectangle => rectangle.MinY)
            .ThenBy(rectangle => rectangle.MinX)
            .ThenBy(rectangle => rectangle.MaxY)
            .ThenBy(rectangle => rectangle.MaxX)
            .Select(rectangle =>
                $"{rectangle.MinX},{rectangle.MinY},{rectangle.MaxX},{rectangle.MaxY}"));
    }

    private static string[] TraceDisjointDamageIslandsDuringUpdate(
        OracleContext context,
        bool completeUpdate = false,
        bool drawFullLayerDuringUpdate = false)
    {
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(48, 24, 1, 0x6B,
            drawFullLayerDuringUpdate
                ? Exec.MemoryFlags.Chip
                : default);
        var lower = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            0,
            0,
            31,
            15);
        var originalRegion = context.CreateSingleRectangleRegion(0, 0, 31, 15);
        var originalRegionNode = ReadOracleRegionFirst(context.Bus,
            originalRegion);
        var originalRegionBytes = Enumerable.Range(0,
                checked((int)Region.Size))
            .Select(offset => context.Bus.ReadByte(originalRegion +
                checked((uint)offset))).ToArray();
        var originalRegionNodeBytes = Enumerable.Range(0,
                checked((int)RegionRectangle.Size))
            .Select(offset => context.Bus.ReadByte(originalRegionNode +
                checked((uint)offset))).ToArray();
        Assert.Equal(0u, context.InvokeLayers(LayersLvo.InstallClipRegion,
            state =>
            {
                state.A[0] = lower;
                state.A[1] = originalRegion;
            }).D[0]);

        var firstBlocker = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            8,
            2,
            13,
            5);
        var secondBlocker = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            19,
            10,
            24,
            13);

        var deleteSecond = context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = secondBlocker);
        Assert.NotEqual(0u, deleteSecond.D[0]);
        context.WaitForBlitterIdle();
        var firstDamage = ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.DamageList);
        var firstDamagePixels = new HashSet<(int X, int Y)>();
        for (var y = 10; y <= 13; y++)
            for (var x = 19; x <= 24; x++)
                firstDamagePixels.Add((x, y));
        Assert.True(ReadOracleRegionPixels(context, firstDamage)
            .SetEquals(firstDamagePixels));
        var firstDamageDescription = DescribeRegion(context, firstDamage);

        var deleteFirst = context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = firstBlocker);
        Assert.NotEqual(0u, deleteFirst.D[0]);
        context.WaitForBlitterIdle();
        Assert.Equal("y=0,15:x=0,31",
            MoveSizeVisibleCoverage(context, lower));
        var damage = ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.DamageList);
        var expectedDamagePixels = new HashSet<(int X, int Y)>(firstDamagePixels);
        for (var y = 2; y <= 5; y++)
            for (var x = 8; x <= 13; x++)
                expectedDamagePixels.Add((x, y));
        Assert.True(ReadOracleRegionPixels(context, damage)
            .SetEquals(expectedDamagePixels));
        var damageDescription = DescribeRegion(context, damage);
        var damageBytes = Enumerable.Range(0, checked((int)Region.Size))
            .Select(offset => context.Bus.ReadByte(damage + checked((uint)offset)))
            .ToArray();

        var beginUpdate = context.InvokeLayers(LayersLvo.BeginUpdate,
            state => state.A[0] = lower).D[0];
        Assert.NotEqual(0u, beginUpdate);
        Assert.True((ReadOracleLayerFlags(context.Bus, lower) &
            LayerFlags.Updating) != 0);
        const string activeDamageCoverage =
            "y=2,5:x=8,13;y=10,13:x=19,24";
        Assert.Equal(activeDamageCoverage,
            MoveSizeVisibleCoverage(context, lower));

        var callerRegion = context.Allocate(Region.Size);
        var firstNode = context.Allocate(RegionRectangle.Size);
        var secondNode = context.Allocate(RegionRectangle.Size);
        var memory = new LayersTestGuestMemory(context.Bus);
        var callerRegionAddress = APTR.FromPointer(callerRegion);
        var firstNodeAddress = APTR.FromPointer(firstNode);
        var secondNodeAddress = APTR.FromPointer(secondNode);
        LayersRegionCodec.WriteBounds(ref memory, callerRegionAddress,
            LayersRectangleCodec.Create(6, 1, 27, 15));
        LayersRegionCodec.WriteFirst(ref memory, callerRegionAddress,
            firstNodeAddress);
        LayersRegionRectangleCodec.WritePrevious(ref memory, firstNodeAddress,
            LayersRegionCodec.HeadAnchor(callerRegionAddress));
        LayersRegionRectangleCodec.WriteNext(ref memory, firstNodeAddress,
            secondNodeAddress);
        LayersRegionRectangleCodec.WriteBounds(ref memory, firstNodeAddress,
            LayersRectangleCodec.Create(0, 0, 4, 2));
        LayersRegionRectangleCodec.WritePrevious(ref memory, secondNodeAddress,
            firstNodeAddress);
        LayersRegionRectangleCodec.WriteNext(ref memory, secondNodeAddress,
            APTR.Null);
        LayersRegionRectangleCodec.WriteBounds(ref memory, secondNodeAddress,
            LayersRectangleCodec.Create(16, 11, 21, 14));
        var callerRegionBytes = Enumerable.Range(0,
                checked((int)Region.Size))
            .Select(offset => context.Bus.ReadByte(callerRegion +
                checked((uint)offset))).ToArray();
        var firstNodeBytes = Enumerable.Range(0,
                checked((int)RegionRectangle.Size))
            .Select(offset => context.Bus.ReadByte(firstNode +
                checked((uint)offset))).ToArray();
        var secondNodeBytes = Enumerable.Range(0,
                checked((int)RegionRectangle.Size))
            .Select(offset => context.Bus.ReadByte(secondNode +
                checked((uint)offset))).ToArray();

        Assert.Equal(originalRegion,
            context.InvokeLayers(LayersLvo.InstallClipRegion, state =>
            {
                state.A[0] = lower;
                state.A[1] = callerRegion;
            }).D[0]);
        const string activeCoverage = "y=2,3:x=8,10;y=12,13:x=22,24";
        const string stableCoverage = "y=1,3:x=6,10;y=12,15:x=22,27";
        Assert.Equal(callerRegion, ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.ClipRegion));
        Assert.Equal(activeCoverage, MoveSizeVisibleCoverage(context, lower));
        Assert.Equal(damage, ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.DamageList));
        Assert.Equal(damageDescription, DescribeRegion(context, damage));

        var hookTrace = context.InvokeDoHookClipRects(
            lower,
            display.Address,
            0,
            0,
            0,
            31,
            15,
            includeClipRectChain: false);
        Assert.Contains("callbacks=2", hookTrace, StringComparison.Ordinal);
        Assert.Contains("layer=True", hookTrace, StringComparison.Ordinal);
        Assert.Contains("target-final-rp=True", hookTrace,
            StringComparison.Ordinal);
        var sortedHookBounds = SortedDoHookClipRectBounds(hookTrace);
        Assert.Equal("8,2,10,3;22,12,24,13", sortedHookBounds);

        var drawingTrace = string.Empty;
        if (drawFullLayerDuringUpdate)
        {
            var drawRastPort = ReadOracleLayerPointer(context.Bus, lower,
                OracleLayerPointer.RastPort);
            context.InvokeGraphics((int)GraphicsLvo.SetAPen, state =>
            {
                state.A[1] = drawRastPort;
                state.D[0] = 1;
            });
            context.InvokeGraphics((int)GraphicsLvo.SetDrMd, state =>
            {
                state.A[1] = drawRastPort;
                state.D[0] = 0;
            });
            context.InvokeGraphics((int)GraphicsLvo.SetWriteMask, state =>
            {
                state.A[0] = drawRastPort;
                state.D[0] = 1;
            });
            var expectedPixels = Enumerable.Range(0,
                    checked(display.BytesPerRow * display.Height))
                .Select(offset => context.Bus.ReadByte(
                    display.Planes[0] + checked((uint)offset)))
                .ToArray();
            foreach (var (x, y) in expectedDamagePixels.Where(pixel =>
                         (pixel.Y is >= 2 and <= 3 && pixel.X is >= 8 and <= 10) ||
                         (pixel.Y is >= 12 and <= 13 && pixel.X is >= 22 and <= 24)))
            {
                var offset = y * display.BytesPerRow + x / 8;
                expectedPixels[offset] |= (byte)(0x80 >> (x & 7));
            }
            var updateClipHead = ReadOracleLayerPointer(context.Bus, lower,
                OracleLayerPointer.ClipRect);
            context.InvokeGraphics((int)GraphicsLvo.RectFill, state =>
            {
                state.A[1] = drawRastPort;
                state.D[0] = 0;
                state.D[1] = 0;
                state.D[2] = 31;
                state.D[3] = 15;
            });
            context.WaitForBlitterIdle();
            Assert.False(context.Bus.Blitter.Busy);
            var actualPixels = Enumerable.Range(0, expectedPixels.Length)
                .Select(offset => context.Bus.ReadByte(
                    display.Planes[0] + checked((uint)offset)))
                .ToArray();
            Assert.Equal(expectedPixels, actualPixels);
            Assert.Equal(updateClipHead, ReadOracleLayerPointer(context.Bus,
                lower, OracleLayerPointer.ClipRect));
            Assert.Equal(damageDescription, DescribeRegion(context,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList)));
            drawingTrace = $":draw=RectFill(0,0,31,15):bitmap={HashBitMap(context, display)}";
        }

        context.InvokeLayers(LayersLvo.EndUpdate, state =>
        {
            state.A[0] = lower;
            state.D[0] = completeUpdate ? 1u : 0u;
        });
        var flagsAfterUpdate = ReadOracleLayerFlags(context.Bus, lower);
        Assert.True((flagsAfterUpdate & LayerFlags.Updating) == 0);
        if (completeUpdate)
            Assert.True((flagsAfterUpdate & LayerFlags.Refresh) != 0);
        Assert.Equal(callerRegion, ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.ClipRegion));
        Assert.Equal(stableCoverage, MoveSizeVisibleCoverage(context, lower));
        Assert.Equal(damage, ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.DamageList));
        var damageBytesAfterUpdate = Enumerable.Range(0, damageBytes.Length)
            .Select(offset => context.Bus.ReadByte(damage + checked((uint)offset)))
            .ToArray();
        var damagePixelsAfterUpdate = ReadOracleRegionPixels(context, damage);
        var damageDescriptionAfterUpdate = DescribeRegion(context, damage);
        if (completeUpdate)
        {
            Assert.Equal(new byte[checked((int)Region.Size)],
                damageBytesAfterUpdate);
            Assert.Empty(damagePixelsAfterUpdate);
        }
        else
        {
            Assert.Equal(damageBytes, damageBytesAfterUpdate);
            Assert.Equal(expectedDamagePixels, damagePixelsAfterUpdate);
            Assert.Equal(damageDescription, damageDescriptionAfterUpdate);
        }
        Assert.Equal(callerRegionBytes,
            Enumerable.Range(0, callerRegionBytes.Length)
                .Select(offset => context.Bus.ReadByte(callerRegion +
                    checked((uint)offset))).ToArray());
        Assert.Equal(firstNodeBytes,
            Enumerable.Range(0, firstNodeBytes.Length)
                .Select(offset => context.Bus.ReadByte(firstNode +
                    checked((uint)offset))).ToArray());
        Assert.Equal(secondNodeBytes,
            Enumerable.Range(0, secondNodeBytes.Length)
                .Select(offset => context.Bus.ReadByte(secondNode +
                    checked((uint)offset))).ToArray());
        Assert.Equal(originalRegionBytes,
            Enumerable.Range(0, originalRegionBytes.Length)
                .Select(offset => context.Bus.ReadByte(originalRegion +
                    checked((uint)offset))).ToArray());
        Assert.Equal(originalRegionNodeBytes,
            Enumerable.Range(0, originalRegionNodeBytes.Length)
                .Select(offset => context.Bus.ReadByte(originalRegionNode +
                    checked((uint)offset))).ToArray());

        var trace = new[]
        {
            $"delete:second-blocker=true:damage={firstDamageDescription}",
            $"delete:first-blocker=true:damage={damageDescription}",
            $"begin-update:success={beginUpdate != 0}:active=[{activeDamageCoverage}]",
            $"install-region:previous=original:active=[{activeCoverage}]",
            $"hook:callbacks=2:sorted-bounds={sortedHookBounds}{drawingTrace}",
            $"end-update:complete={completeUpdate}:updating=false:refresh={(flagsAfterUpdate & LayerFlags.Refresh) != 0}:stable=[{stableCoverage}]",
            completeUpdate
                ? "damage:identity=retained:disconnected-contents=retired:caller-region=preserved"
                : "damage:disconnected-areas=retained:caller-region=preserved"
        };

        Assert.Equal(callerRegion,
            context.InvokeLayers(LayersLvo.InstallClipRegion, state =>
            {
                state.A[0] = lower;
                state.A[1] = 0;
            }).D[0]);
        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = lower);
        context.DisposeLayerInfo(layerInfo);
        context.FreePlanarBitMap(display);
        context.Free(originalRegionNode, RegionRectangle.Size);
        context.Free(originalRegion, Region.Size);
        context.Free(firstNode, RegionRectangle.Size);
        context.Free(secondNode, RegionRectangle.Size);
        context.Free(callerRegion, Region.Size);
        return trace;
    }

    private static string[] TraceBroadRegionRectFillAcrossDisjointDamage(
        OracleContext context,
        bool completeUpdate = true,
        bool exerciseEmptyRegionDuringUpdate = false)
    {
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(48, 24, 1, 0x6B,
            Exec.MemoryFlags.Chip);
        var lower = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            0,
            0,
            31,
            15);
        var callerRegion = context.CreateSingleRectangleRegion(0, 0, 31, 15);
        var callerRegionNode = ReadOracleRegionFirst(context.Bus,
            callerRegion);
        var callerRegionBytes = Enumerable.Range(0,
                checked((int)Region.Size))
            .Select(offset => context.Bus.ReadByte(callerRegion +
                checked((uint)offset))).ToArray();
        var callerRegionNodeBytes = Enumerable.Range(0,
                checked((int)RegionRectangle.Size))
            .Select(offset => context.Bus.ReadByte(callerRegionNode +
                checked((uint)offset))).ToArray();
        Assert.Equal(0u, context.InvokeLayers(LayersLvo.InstallClipRegion,
            state =>
            {
                state.A[0] = lower;
                state.A[1] = callerRegion;
            }).D[0]);

        var firstBlocker = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            8,
            2,
            13,
            5);
        var secondBlocker = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            19,
            10,
            24,
            13);
        Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = secondBlocker).D[0]);
        context.WaitForBlitterIdle();
        Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = firstBlocker).D[0]);
        context.WaitForBlitterIdle();

        var damage = ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.DamageList);
        var expectedDamagePixels = new HashSet<(int X, int Y)>();
        for (var y = 2; y <= 5; y++)
            for (var x = 8; x <= 13; x++)
                expectedDamagePixels.Add((x, y));
        for (var y = 10; y <= 13; y++)
            for (var x = 19; x <= 24; x++)
                expectedDamagePixels.Add((x, y));
        Assert.True(ReadOracleRegionPixels(context, damage)
            .SetEquals(expectedDamagePixels));
        var damageDescription = DescribeRegion(context, damage);
        var damageBytes = Enumerable.Range(0, checked((int)Region.Size))
            .Select(offset => context.Bus.ReadByte(damage + checked((uint)offset)))
            .ToArray();

        Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.BeginUpdate,
            state => state.A[0] = lower).D[0]);
        Assert.True((ReadOracleLayerFlags(context.Bus, lower) &
            LayerFlags.Updating) != 0);
        const string damageCoverage = "y=2,5:x=8,13;y=10,13:x=19,24";
        Assert.Equal(damageCoverage, MoveSizeVisibleCoverage(context, lower));
        var emptyRegion = 0u;
        var emptyRegionDrawTrace = string.Empty;
        if (exerciseEmptyRegionDuringUpdate)
        {
            emptyRegion = context.Allocate(Region.Size);
            var memory = new LayersTestGuestMemory(context.Bus);
            var emptyRegionAddress = APTR.FromPointer(emptyRegion);
            var emptyBounds = default(Rectangle);
            LayersRegionCodec.WriteBounds(ref memory, emptyRegionAddress,
                in emptyBounds);
            LayersRegionCodec.WriteFirst(ref memory, emptyRegionAddress,
                APTR.Null);
            Assert.Equal(callerRegion,
                context.InvokeLayers(LayersLvo.InstallClipRegion, state =>
                {
                    state.A[0] = lower;
                    state.A[1] = emptyRegion;
                }).D[0]);
            Assert.Equal(string.Empty,
                MoveSizeVisibleCoverage(context, lower));
            var emptyHookTrace = context.InvokeDoHookClipRects(
                lower,
                display.Address,
                0,
                0,
                0,
                31,
                15,
                includeClipRectChain: false);
            Assert.Contains("callbacks=0", emptyHookTrace,
                StringComparison.Ordinal);

            var emptyRegionRastPort = ReadOracleLayerPointer(context.Bus, lower,
                OracleLayerPointer.RastPort);
            context.InvokeGraphics((int)GraphicsLvo.SetAPen, state =>
            {
                state.A[1] = emptyRegionRastPort;
                state.D[0] = 1;
            });
            context.InvokeGraphics((int)GraphicsLvo.SetDrMd, state =>
            {
                state.A[1] = emptyRegionRastPort;
                state.D[0] = 0;
            });
            context.InvokeGraphics((int)GraphicsLvo.SetWriteMask, state =>
            {
                state.A[0] = emptyRegionRastPort;
                state.D[0] = 1;
            });
            var pixelsBeforeEmptyDraw = Enumerable.Range(0,
                    checked(display.BytesPerRow * display.Height))
                .Select(offset => context.Bus.ReadByte(
                    display.Planes[0] + checked((uint)offset)))
                .ToArray();
            context.InvokeGraphics((int)GraphicsLvo.RectFill, state =>
            {
                state.A[1] = emptyRegionRastPort;
                state.D[0] = 0;
                state.D[1] = 0;
                state.D[2] = 31;
                state.D[3] = 15;
            });
            context.WaitForBlitterIdle();
            Assert.Equal(pixelsBeforeEmptyDraw,
                Enumerable.Range(0, pixelsBeforeEmptyDraw.Length)
                    .Select(offset => context.Bus.ReadByte(
                        display.Planes[0] + checked((uint)offset)))
                    .ToArray());
            Assert.Equal(damage, ReadOracleLayerPointer(context.Bus, lower,
                OracleLayerPointer.DamageList));
            Assert.Equal(damageDescription, DescribeRegion(context, damage));
            Assert.Equal(damageBytes, Enumerable.Range(0, damageBytes.Length)
                .Select(offset => context.Bus.ReadByte(damage +
                    checked((uint)offset))).ToArray());

            Assert.Equal(emptyRegion,
                context.InvokeLayers(LayersLvo.InstallClipRegion, state =>
                {
                    state.A[0] = lower;
                    state.A[1] = callerRegion;
                }).D[0]);
            Assert.Equal(damageCoverage,
                MoveSizeVisibleCoverage(context, lower));
            emptyRegionDrawTrace =
                ":empty-region=callbacks-0:RectFill-writes=none:restored=broad";
        }
        var hookTrace = context.InvokeDoHookClipRects(
            lower,
            display.Address,
            0,
            0,
            0,
            31,
            15,
            includeClipRectChain: false);
        Assert.Contains("callbacks=2", hookTrace, StringComparison.Ordinal);
        var sortedHookBounds = SortedDoHookClipRectBounds(hookTrace);
        Assert.Equal("8,2,13,5;19,10,24,13", sortedHookBounds);

        var rastPort = ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.RastPort);
        context.InvokeGraphics((int)GraphicsLvo.SetAPen, state =>
        {
            state.A[1] = rastPort;
            state.D[0] = 1;
        });
        context.InvokeGraphics((int)GraphicsLvo.SetDrMd, state =>
        {
            state.A[1] = rastPort;
            state.D[0] = 0;
        });
        context.InvokeGraphics((int)GraphicsLvo.SetWriteMask, state =>
        {
            state.A[0] = rastPort;
            state.D[0] = 1;
        });
        var expectedPixels = Enumerable.Range(0,
                checked(display.BytesPerRow * display.Height))
            .Select(offset => context.Bus.ReadByte(
                display.Planes[0] + checked((uint)offset)))
            .ToArray();
        foreach (var (x, y) in expectedDamagePixels)
        {
            var offset = y * display.BytesPerRow + x / 8;
            expectedPixels[offset] |= (byte)(0x80 >> (x & 7));
        }
        var updateClipHead = ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.ClipRect);
        context.InvokeGraphics((int)GraphicsLvo.RectFill, state =>
        {
            state.A[1] = rastPort;
            state.D[0] = 0;
            state.D[1] = 0;
            state.D[2] = 31;
            state.D[3] = 15;
        });
        context.WaitForBlitterIdle();
        Assert.False(context.Bus.Blitter.Busy);
        Assert.Equal(expectedPixels, Enumerable.Range(0, expectedPixels.Length)
            .Select(offset => context.Bus.ReadByte(
                display.Planes[0] + checked((uint)offset)))
            .ToArray());
        Assert.Equal(updateClipHead, ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.ClipRect));
        Assert.Equal(damage, ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.DamageList));
        Assert.Equal(damageDescription, DescribeRegion(context, damage));
        Assert.Equal(damageBytes, Enumerable.Range(0, damageBytes.Length)
            .Select(offset => context.Bus.ReadByte(damage + checked((uint)offset)))
            .ToArray());

        context.InvokeLayers(LayersLvo.EndUpdate, state =>
        {
            state.A[0] = lower;
            state.D[0] = completeUpdate ? 1u : 0u;
        });
        var flagsAfterUpdate = ReadOracleLayerFlags(context.Bus, lower);
        Assert.True((flagsAfterUpdate & LayerFlags.Updating) == 0);
        if (completeUpdate)
            Assert.True((flagsAfterUpdate & LayerFlags.Refresh) != 0);
        Assert.Equal("y=0,15:x=0,31",
            MoveSizeVisibleCoverage(context, lower));
        Assert.Equal(callerRegion, ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.ClipRegion));
        Assert.Equal(damage, ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.DamageList));
        var damageBytesAfterUpdate = Enumerable.Range(0,
                checked((int)Region.Size))
            .Select(offset => context.Bus.ReadByte(damage +
                checked((uint)offset))).ToArray();
        if (completeUpdate)
        {
            Assert.Equal(new byte[checked((int)Region.Size)],
                damageBytesAfterUpdate);
            Assert.Empty(ReadOracleRegionPixels(context, damage));
        }
        else
        {
            Assert.Equal(damageBytes, damageBytesAfterUpdate);
            Assert.True(ReadOracleRegionPixels(context, damage)
                .SetEquals(expectedDamagePixels));
            Assert.Equal(damageDescription, DescribeRegion(context, damage));
        }
        Assert.Equal(callerRegionBytes,
            Enumerable.Range(0, callerRegionBytes.Length)
                .Select(offset => context.Bus.ReadByte(callerRegion +
                    checked((uint)offset))).ToArray());
        Assert.Equal(callerRegionNodeBytes,
            Enumerable.Range(0, callerRegionNodeBytes.Length)
                .Select(offset => context.Bus.ReadByte(callerRegionNode +
                    checked((uint)offset))).ToArray());

        var trace = new[]
        {
            $"damage:disconnected={damageDescription}",
            $"begin-update:active=[{damageCoverage}]",
            $"hook:callbacks=2:sorted-bounds={sortedHookBounds}{emptyRegionDrawTrace}",
            $"draw:RectFill(0,0,31,15):bitmap={HashBitMap(context, display)}",
            $"end-update:complete={completeUpdate}:damage-{(completeUpdate ? "retired" : "retained")}=true:updating=false:refresh={(flagsAfterUpdate & LayerFlags.Refresh) != 0}",
            "caller-region:broad-and-preserved=true"
        };

        Assert.Equal(callerRegion,
            context.InvokeLayers(LayersLvo.InstallClipRegion, state =>
            {
                state.A[0] = lower;
                state.A[1] = 0;
            }).D[0]);
        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = lower);
        context.DisposeLayerInfo(layerInfo);
        context.FreePlanarBitMap(display);
        context.Free(callerRegionNode, RegionRectangle.Size);
        context.Free(callerRegion, Region.Size);
        if (emptyRegion != 0)
            context.Free(emptyRegion, Region.Size);
        return trace;
    }

    private static string[] TraceMoveSizeLayerOcclusionTransition(
        OracleContext context)
    {
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(48, 24, 1, 0x6B);
        var lower = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            0,
            0,
            31,
            15);
        var upper = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            32,
            4,
            47,
            11);

        var beforeLowerDamage = DescribeRegion(context,
            ReadOracleLayerPointer(context.Bus, lower, OracleLayerPointer.DamageList));
        var beforeLowerRefresh = (ReadOracleLayerFlags(context.Bus, lower) &
            LayerFlags.Refresh) != 0;
        var beforeLowerCoverage = MoveSizeVisibleCoverage(context, lower);
        var beforeLower = DescribeMoveSizeLayerState(context, lower);
        var beforeUpper = DescribeMoveSizeLayerState(context, upper);
        var move = context.InvokeLayers(LayersLvo.MoveSizeLayer, state =>
        {
            state.A[0] = upper;
            state.D[0] = unchecked((uint)-8);
            state.D[1] = 0;
            state.D[2] = 0;
            state.D[3] = 0;
        });
        Assert.NotEqual(0u, move.D[0]);
        Assert.Equal("0,0,31,15", FormatOracleRectangle(
            ReadOracleLayerBounds(context.Bus, lower)));
        Assert.Equal("24,4,39,11", FormatOracleRectangle(
            ReadOracleLayerBounds(context.Bus, upper)));
        var afterLowerDamage = DescribeRegion(context,
            ReadOracleLayerPointer(context.Bus, lower, OracleLayerPointer.DamageList));
        var afterLowerRefresh = (ReadOracleLayerFlags(context.Bus, lower) &
            LayerFlags.Refresh) != 0;
        var afterLowerCoverage = MoveSizeVisibleCoverage(context, lower);
        var afterLower = DescribeMoveSizeLayerState(context, lower);
        var afterUpper = DescribeMoveSizeLayerState(context, upper);
        Assert.False(beforeLowerRefresh);
        Assert.False(afterLowerRefresh);
        Assert.Equal(beforeLowerDamage, afterLowerDamage);
        Assert.NotEqual(beforeLowerCoverage, afterLowerCoverage);

        var trace = new[]
        {
            "before:lower=" + beforeLower,
            "before:upper=" + beforeUpper,
            "move:success=true",
            "after:lower=" + afterLower,
            "after:upper=" + afterUpper
        };

        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = upper);
        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = lower);
        context.DisposeLayerInfo(layerInfo);
        context.FreePlanarBitMap(display);
        return trace;
    }

    private static string[] TraceMoveSizeLowerLayerWithinOverlap(
        OracleContext context)
    {
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(48, 24, 1, 0x6B);
        var lower = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            0,
            0,
            31,
            15);
        var upper = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            8,
            4,
            23,
            11);

        var trace = new List<string>
        {
            "before:lower=" + DescribeMoveSizeLayerState(context, lower),
            "before:upper=" + DescribeMoveSizeLayerState(context, upper)
        };
        var move = context.InvokeLayers(LayersLvo.MoveSizeLayer, state =>
        {
            state.A[0] = lower;
            state.D[0] = 1;
            state.D[1] = 1;
            state.D[2] = unchecked((uint)-1);
            state.D[3] = unchecked((uint)-1);
        });
        Assert.NotEqual(0u, move.D[0]);
        trace.Add("move:success=true");
        trace.Add("after:lower=" + DescribeMoveSizeLayerState(context, lower));
        trace.Add("after:upper=" + DescribeMoveSizeLayerState(context, upper));

        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = upper);
        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = lower);
        context.DisposeLayerInfo(layerInfo);
        context.FreePlanarBitMap(display);
        return trace.ToArray();
    }

    private static string[] TraceMoveSizeLayerWithCallerRegion(
        OracleContext context,
        bool emptyCallerRegion)
    {
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(48, 24, 1, 0x6B);
        var lower = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            0,
            0,
            31,
            15);
        var upper = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            4,
            4,
            15,
            15);

        var region = emptyCallerRegion
            ? context.Allocate(Region.Size)
            : context.CreateSingleRectangleRegion(4, 4, 4, 4);
        var regionAddress = APTR.FromPointer(region);
        var memory = new LayersTestGuestMemory(context.Bus);
        byte[] ReadBytes(uint address, int count) => Enumerable.Range(0, count)
            .Select(offset => context.Bus.ReadByte(address +
                checked((uint)offset))).ToArray();
        if (emptyCallerRegion)
        {
            var emptyBounds = default(Rectangle);
            LayersRegionCodec.WriteBounds(ref memory, regionAddress,
                in emptyBounds);
            LayersRegionCodec.WriteFirst(ref memory, regionAddress, APTR.Null);
        }
        var regionBytes = ReadBytes(region, checked((int)Region.Size));
        var regionNode = ReadOracleRegionFirst(context.Bus, region);
        var regionNodeBytes = regionNode == 0
            ? Array.Empty<byte>()
            : ReadBytes(regionNode, checked((int)RegionRectangle.Size));

        Assert.Equal(0u, context.InvokeLayers(LayersLvo.InstallClipRegion,
            state =>
            {
                state.A[0] = lower;
                state.A[1] = region;
            }).D[0]);
        Assert.Equal(region, ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.ClipRegion));
        var beforeLower = DescribeMoveSizeLayerState(context, lower);
        var beforeUpper = DescribeMoveSizeLayerState(context, upper);
        var beforeDamage = DescribeRegion(context, ReadOracleLayerPointer(
            context.Bus, lower, OracleLayerPointer.DamageList));

        var move = context.InvokeLayers(LayersLvo.MoveSizeLayer, state =>
        {
            state.A[0] = upper;
            state.D[0] = 2;
            state.D[1] = 1;
            state.D[2] = 0;
            state.D[3] = 0;
        });
        Assert.NotEqual(0u, move.D[0]);
        Assert.Equal("0,0,31,15", FormatOracleRectangle(
            ReadOracleLayerBounds(context.Bus, lower)));
        Assert.Equal("6,5,17,16", FormatOracleRectangle(
            ReadOracleLayerBounds(context.Bus, upper)));
        Assert.Equal(region, ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.ClipRegion));
        Assert.Equal(regionBytes, ReadBytes(region, regionBytes.Length));
        if (regionNode != 0)
            Assert.Equal(regionNodeBytes, ReadBytes(regionNode,
                checked((int)RegionRectangle.Size)));

        var afterDamage = DescribeRegion(context, ReadOracleLayerPointer(
            context.Bus, lower, OracleLayerPointer.DamageList));
        var afterLower = DescribeMoveSizeLayerState(context, lower);
        var afterUpper = DescribeMoveSizeLayerState(context, upper);
        var trace = new[]
        {
            $"caller-region:empty={emptyCallerRegion}:bounds={FormatOracleRectangle(ReadOracleRegionBounds(context.Bus, region))}:nodes={regionNodeBytes.Length / checked((int)RegionRectangle.Size)}",
            "before:lower=" + beforeLower,
            "before:upper=" + beforeUpper,
            "before:lower-damage=" + beforeDamage,
            "move:success=true",
            "after:lower=" + afterLower,
            "after:upper=" + afterUpper,
            "after:lower-damage=" + afterDamage,
            "caller-region:preserved=true"
        };

        Assert.Equal(region, context.InvokeLayers(LayersLvo.InstallClipRegion,
            state =>
            {
                state.A[0] = lower;
                state.A[1] = 0;
            }).D[0]);
        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = upper);
        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = lower);
        context.DisposeLayerInfo(layerInfo);
        context.FreePlanarBitMap(display);
        if (regionNode != 0)
            context.Free(regionNode, RegionRectangle.Size);
        context.Free(region, Region.Size);
        return trace;
    }

    private static string[] TraceBehindLayerOverlapWithCallerRegion(
        OracleContext context)
    {
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(48, 24, 1, 0x6B);
        var lower = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            0,
            0,
            31,
            15);
        var upper = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            8,
            4,
            23,
            11);

        var region = context.CreateSingleRectangleRegion(8, 4, 8, 4);
        var regionNode = ReadOracleRegionFirst(context.Bus, region);
        var regionBytes = Enumerable.Range(0, checked((int)Region.Size))
            .Select(offset => context.Bus.ReadByte(region + checked((uint)offset)))
            .ToArray();
        var regionNodeBytes = Enumerable.Range(0,
                checked((int)RegionRectangle.Size))
            .Select(offset => context.Bus.ReadByte(regionNode + checked((uint)offset)))
            .ToArray();
        Assert.Equal(0u, context.InvokeLayers(LayersLvo.InstallClipRegion,
            state =>
            {
                state.A[0] = lower;
                state.A[1] = region;
            }).D[0]);
        Assert.Equal(region, ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.ClipRegion));

        var beforeLowerCoverage = MoveSizeVisibleCoverage(context, lower);
        var beforeLower = DescribeMoveSizeLayerState(context, lower);
        var beforeUpper = DescribeMoveSizeLayerState(context, upper);
        var beforeDamage = DescribeRegion(context, ReadOracleLayerPointer(
            context.Bus, lower, OracleLayerPointer.DamageList));
        var behind = context.InvokeLayers(LayersLvo.BehindLayer,
            state => state.A[1] = upper);
        Assert.NotEqual(0u, behind.D[0]);
        var afterLowerCoverage = MoveSizeVisibleCoverage(context, lower);
        var afterLower = DescribeMoveSizeLayerState(context, lower);
        var afterUpper = DescribeMoveSizeLayerState(context, upper);
        var afterDamage = DescribeRegion(context, ReadOracleLayerPointer(
            context.Bus, lower, OracleLayerPointer.DamageList));
        Assert.NotEqual(beforeLowerCoverage, afterLowerCoverage);
        Assert.Equal(region, ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.ClipRegion));
        Assert.Equal(regionBytes, Enumerable.Range(0, regionBytes.Length)
            .Select(offset => context.Bus.ReadByte(region + checked((uint)offset)))
            .ToArray());
        Assert.Equal(regionNodeBytes,
            Enumerable.Range(0, regionNodeBytes.Length)
                .Select(offset => context.Bus.ReadByte(regionNode + checked((uint)offset)))
                .ToArray());

        var trace = new[]
        {
            "before:lower=" + beforeLower,
            "before:upper=" + beforeUpper,
            "before:lower-damage=" + beforeDamage,
            "behind:success=true",
            "after:lower=" + afterLower,
            "after:upper=" + afterUpper,
            "after:lower-damage=" + afterDamage,
            "caller-region:preserved=true"
        };

        Assert.Equal(region, context.InvokeLayers(LayersLvo.InstallClipRegion,
            state =>
            {
                state.A[0] = lower;
                state.A[1] = 0;
            }).D[0]);
        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = upper);
        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = lower);
        context.DisposeLayerInfo(layerInfo);
        context.FreePlanarBitMap(display);
        context.Free(regionNode, RegionRectangle.Size);
        context.Free(region, Region.Size);
        return trace;
    }

    private static string[] TraceUpfrontLayerOverlapWithCallerRegion(
        OracleContext context)
    {
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(48, 24, 1, 0x6B);
        var lower = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            0,
            0,
            31,
            15);
        var upper = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            8,
            4,
            23,
            11);

        Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.BehindLayer,
            state => state.A[1] = upper).D[0]);
        var region = context.CreateSingleRectangleRegion(8, 4, 8, 4);
        var regionNode = ReadOracleRegionFirst(context.Bus, region);
        var regionBytes = Enumerable.Range(0, checked((int)Region.Size))
            .Select(offset => context.Bus.ReadByte(region + checked((uint)offset)))
            .ToArray();
        var regionNodeBytes = Enumerable.Range(0,
                checked((int)RegionRectangle.Size))
            .Select(offset => context.Bus.ReadByte(regionNode + checked((uint)offset)))
            .ToArray();
        Assert.Equal(0u, context.InvokeLayers(LayersLvo.InstallClipRegion,
            state =>
            {
                state.A[0] = lower;
                state.A[1] = region;
            }).D[0]);

        var beforeLowerCoverage = MoveSizeVisibleCoverage(context, lower);
        var beforeLower = DescribeMoveSizeLayerState(context, lower);
        var beforeUpper = DescribeMoveSizeLayerState(context, upper);
        var beforeDamage = DescribeRegion(context, ReadOracleLayerPointer(
            context.Bus, lower, OracleLayerPointer.DamageList));
        var upfront = context.InvokeLayers(LayersLvo.UpfrontLayer,
            state => state.A[1] = upper);
        Assert.NotEqual(0u, upfront.D[0]);
        var afterLowerCoverage = MoveSizeVisibleCoverage(context, lower);
        var afterLower = DescribeMoveSizeLayerState(context, lower);
        var afterUpper = DescribeMoveSizeLayerState(context, upper);
        var afterDamage = DescribeRegion(context, ReadOracleLayerPointer(
            context.Bus, lower, OracleLayerPointer.DamageList));
        Assert.NotEqual(beforeLowerCoverage, afterLowerCoverage);
        Assert.Equal(region, ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.ClipRegion));
        Assert.Equal(regionBytes, Enumerable.Range(0, regionBytes.Length)
            .Select(offset => context.Bus.ReadByte(region + checked((uint)offset)))
            .ToArray());
        Assert.Equal(regionNodeBytes,
            Enumerable.Range(0, regionNodeBytes.Length)
                .Select(offset => context.Bus.ReadByte(regionNode + checked((uint)offset)))
                .ToArray());

        var trace = new[]
        {
            "before:lower=" + beforeLower,
            "before:upper=" + beforeUpper,
            "before:lower-damage=" + beforeDamage,
            "upfront:success=true",
            "after:lower=" + afterLower,
            "after:upper=" + afterUpper,
            "after:lower-damage=" + afterDamage,
            "caller-region:preserved=true"
        };

        Assert.Equal(region, context.InvokeLayers(LayersLvo.InstallClipRegion,
            state =>
            {
                state.A[0] = lower;
                state.A[1] = 0;
            }).D[0]);
        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = upper);
        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = lower);
        context.DisposeLayerInfo(layerInfo);
        context.FreePlanarBitMap(display);
        context.Free(regionNode, RegionRectangle.Size);
        context.Free(region, Region.Size);
        return trace;
    }

    private static string[] TraceDeleteTopStillOccludedByMiddle(
        OracleContext context)
    {
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(48, 24, 1, 0x6B);
        var lower = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            0,
            0,
            31,
            15);
        var middle = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            8,
            0,
            23,
            15);
        var transientTop = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            12,
            4,
            19,
            11);

        var beforeLower = DescribeMoveSizeLayerState(context, lower);
        var beforeMiddle = DescribeMoveSizeLayerState(context, middle);
        var beforeTop = DescribeMoveSizeLayerState(context, transientTop);
        var beforeLowerCoverage = MoveSizeVisibleCoverage(context, lower);
        var beforeMiddleCoverage = MoveSizeVisibleCoverage(context, middle);
        var delete = context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = transientTop);
        Assert.NotEqual(0u, delete.D[0]);

        var afterLower = DescribeMoveSizeLayerState(context, lower);
        var afterMiddle = DescribeMoveSizeLayerState(context, middle);
        var afterLowerCoverage = MoveSizeVisibleCoverage(context, lower);
        var afterMiddleCoverage = MoveSizeVisibleCoverage(context, middle);
        Assert.Equal(beforeLowerCoverage, afterLowerCoverage);
        Assert.NotEqual(beforeMiddleCoverage, afterMiddleCoverage);

        var trace = new[]
        {
            "before:lower=" + beforeLower,
            "before:middle=" + beforeMiddle,
            "before:transient-top=" + beforeTop,
            "delete:success=true",
            "after:lower=" + afterLower,
            "after:middle=" + afterMiddle
        };

        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = middle);
        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = lower);
        context.DisposeLayerInfo(layerInfo);
        context.FreePlanarBitMap(display);
        return trace;
    }

    private static string[] TraceDeleteMiddleOutsideTop(
        OracleContext context)
    {
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(48, 24, 1, 0x6B);
        var lower = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            0,
            0,
            31,
            15);
        var middle = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            8,
            0,
            23,
            15);
        var top = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            12,
            4,
            19,
            11);

        var beforeLower = DescribeMoveSizeLayerState(context, lower);
        var beforeMiddle = DescribeMoveSizeLayerState(context, middle);
        var beforeTop = DescribeMoveSizeLayerState(context, top);
        var beforeLowerCoverage = MoveSizeVisibleCoverage(context, lower);
        var delete = context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = middle);
        Assert.NotEqual(0u, delete.D[0]);

        var afterLowerCoverage = MoveSizeVisibleCoverage(context, lower);
        const string expectedLowerCoverage =
            "y=0,3:x=0,31;y=4,11:x=0,11|20,31;y=12,15:x=0,31";
        Assert.Equal(expectedLowerCoverage, afterLowerCoverage);
        Assert.NotEqual(beforeLowerCoverage, afterLowerCoverage);
        var lowerDamage = ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.DamageList);
        var lowerDamagePixels = new HashSet<(int X, int Y)>();
        if (lowerDamage != 0)
        {
            var bounds = ReadOracleRegionBounds(context.Bus, lowerDamage);
            var node = ReadOracleRegionFirst(context.Bus, lowerDamage);
            var seen = new HashSet<uint>();
            while (node != 0)
            {
                Assert.True(seen.Add(node),
                    "Damage Region rectangle chain must be acyclic.");
                var rectangle = ReadOracleRegionRectangleBounds(context.Bus,
                    node);
                for (var y = bounds.MinY + rectangle.MinY;
                     y <= bounds.MinY + rectangle.MaxY; y++)
                    for (var x = bounds.MinX + rectangle.MinX;
                         x <= bounds.MinX + rectangle.MaxX; x++)
                        lowerDamagePixels.Add((x, y));
                node = ReadOracleRegionRectanglePointer(context.Bus, node,
                    previous: false);
            }
        }
        for (var y = 4; y <= 11; y++)
            for (var x = 12; x <= 19; x++)
                Assert.DoesNotContain((x, y), lowerDamagePixels);

        var afterLower = DescribeMoveSizeLayerState(context, lower);
        var afterTop = DescribeMoveSizeLayerState(context, top);
        var trace = new[]
        {
            "before:lower=" + beforeLower,
            "before:middle=" + beforeMiddle,
            "before:top=" + beforeTop,
            "delete:success=true",
            "after:lower=" + afterLower,
            "after:top=" + afterTop,
            "damage:excludes-still-covered-top=true"
        };

        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = top);
        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = lower);
        context.DisposeLayerInfo(layerInfo);
        context.FreePlanarBitMap(display);
        return trace;
    }

    private static string[] TraceDeleteMiddleWithCallerRegion(
        OracleContext context,
        bool emptyCallerRegion,
        short regionMinX = 8,
        short regionMinY = 0,
        short regionMaxX = 9,
        short regionMaxY = 1,
        bool restoreEmptyRegionAfterDelete = false,
        bool invokeDoHookClipRectsAfterRestore = false,
        bool beginUpdateAfterRestore = false,
        bool completeRefreshAfterUpdate = false,
        short restoredRegionMinX = 6,
        short restoredRegionMinY = 0,
        short restoredRegionMaxX = 25,
        short restoredRegionMaxY = 15,
        bool widenRegionAfterComplete = false,
        bool widenRegionAfterIncomplete = false)
    {
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(48, 24, 1, 0x6B,
            completeRefreshAfterUpdate ? Exec.MemoryFlags.Chip : default);
        var lower = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            0,
            0,
            31,
            15);
        var middle = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            8,
            0,
            23,
            15);
        var top = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            12,
            4,
            19,
            11);

        var region = emptyCallerRegion
            ? context.Allocate(Region.Size)
            : context.CreateSingleRectangleRegion(
                regionMinX, regionMinY, regionMaxX, regionMaxY);
        if (emptyCallerRegion)
        {
            var memory = new LayersTestGuestMemory(context.Bus);
            var emptyBounds = default(Rectangle);
            LayersRegionCodec.WriteBounds(ref memory, APTR.FromPointer(region),
                in emptyBounds);
            LayersRegionCodec.WriteFirst(ref memory,
                APTR.FromPointer(region), APTR.Null);
        }
        var regionNode = ReadOracleRegionFirst(context.Bus, region);
        var regionBytes = Enumerable.Range(0, checked((int)Region.Size))
            .Select(offset => context.Bus.ReadByte(region + checked((uint)offset)))
            .ToArray();
        var regionNodeBytes = regionNode == 0
            ? Array.Empty<byte>()
            : Enumerable.Range(0, checked((int)RegionRectangle.Size))
                .Select(offset => context.Bus.ReadByte(regionNode +
                    checked((uint)offset))).ToArray();
        Assert.Equal(0u, context.InvokeLayers(LayersLvo.InstallClipRegion,
            state =>
            {
                state.A[0] = lower;
                state.A[1] = region;
            }).D[0]);
        Assert.Equal(region, ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.ClipRegion));

        var beforeLower = DescribeMoveSizeLayerState(context, lower);
        var beforeMiddle = DescribeMoveSizeLayerState(context, middle);
        var beforeTop = DescribeMoveSizeLayerState(context, top);
        var beforeDamage = DescribeRegion(context, ReadOracleLayerPointer(
            context.Bus, lower, OracleLayerPointer.DamageList));
        var delete = context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = middle);
        Assert.NotEqual(0u, delete.D[0]);
        var expectedLowerCoverage = emptyCallerRegion
            ? string.Empty
            : (regionMinX, regionMinY, regionMaxX, regionMaxY) ==
                ((short)8, (short)0, (short)9, (short)1)
                ? "y=0,1:x=8,9"
                : "y=0,3:x=6,25;y=4,11:x=6,11|20,25;y=12,15:x=6,25";
        Assert.Equal(expectedLowerCoverage,
            MoveSizeVisibleCoverage(context, lower));
        Assert.Equal(region, ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.ClipRegion));
        Assert.Equal(regionBytes, Enumerable.Range(0, regionBytes.Length)
            .Select(offset => context.Bus.ReadByte(region + checked((uint)offset)))
            .ToArray());
        if (regionNode != 0)
            Assert.Equal(regionNodeBytes, Enumerable.Range(0,
                    regionNodeBytes.Length)
                .Select(offset => context.Bus.ReadByte(regionNode +
                    checked((uint)offset))).ToArray());

        var lowerDamage = ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.DamageList);
        var lowerDamagePixels = new HashSet<(int X, int Y)>();
        if (lowerDamage != 0)
        {
            var bounds = ReadOracleRegionBounds(context.Bus, lowerDamage);
            var node = ReadOracleRegionFirst(context.Bus, lowerDamage);
            var seen = new HashSet<uint>();
            while (node != 0)
            {
                Assert.True(seen.Add(node),
                    "Damage Region rectangle chain must be acyclic.");
                var rectangle = ReadOracleRegionRectangleBounds(context.Bus,
                    node);
                for (var y = bounds.MinY + rectangle.MinY;
                     y <= bounds.MinY + rectangle.MaxY; y++)
                    for (var x = bounds.MinX + rectangle.MinX;
                         x <= bounds.MinX + rectangle.MaxX; x++)
                        lowerDamagePixels.Add((x, y));
                node = ReadOracleRegionRectanglePointer(context.Bus, node,
                    previous: false);
            }
        }
        var expectedLowerDamagePixels = new HashSet<(int X, int Y)>();
        for (var y = 0; y <= 15; y++)
            for (var x = 8; x <= 23; x++)
                if (y is < 4 or > 11 || x is < 12 or > 19)
                    expectedLowerDamagePixels.Add((x, y));
        Assert.True(lowerDamagePixels.SetEquals(expectedLowerDamagePixels),
            "Damage pixels were [" + string.Join(",", lowerDamagePixels
            .OrderBy(pixel => pixel.Y).ThenBy(pixel => pixel.X)
            .Select(pixel => $"{pixel.X},{pixel.Y}")) + "].");
        for (var y = 4; y <= 11; y++)
            for (var x = 12; x <= 19; x++)
                Assert.DoesNotContain((x, y), lowerDamagePixels);

        uint restoredRegion = 0;
        uint widenedRegion = 0;
        string restoreTrace = string.Empty;
        if (restoreEmptyRegionAfterDelete)
        {
            Assert.True(emptyCallerRegion);
            var damagePointerBeforeRestore = lowerDamage;
            var damageBeforeRestore = DescribeRegion(context, lowerDamage);
            var refreshBeforeRestore = (ReadOracleLayerFlags(context.Bus, lower) &
                LayerFlags.Refresh) != 0;
            var lowerBeforeRestore = DescribeMoveSizeLayerState(context, lower);
            restoredRegion = context.CreateSingleRectangleRegion(
                restoredRegionMinX, restoredRegionMinY,
                restoredRegionMaxX, restoredRegionMaxY);
            Assert.Equal(region, context.InvokeLayers(LayersLvo.InstallClipRegion,
                state =>
                {
                    state.A[0] = lower;
                    state.A[1] = restoredRegion;
                }).D[0]);
            Assert.Equal(restoredRegion, ReadOracleLayerPointer(context.Bus,
                lower, OracleLayerPointer.ClipRegion));
            var expectedRestoredCoverage =
                (restoredRegionMinX, restoredRegionMinY,
                    restoredRegionMaxX, restoredRegionMaxY) ==
                ((short)8, (short)0, (short)9, (short)1)
                    ? "y=0,1:x=8,9"
                    : "y=0,3:x=6,25;y=4,11:x=6,11|20,25;y=12,15:x=6,25";
            Assert.Equal(expectedRestoredCoverage,
                MoveSizeVisibleCoverage(context, lower));
            Assert.Equal(damagePointerBeforeRestore,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList));
            Assert.Equal(damageBeforeRestore, DescribeRegion(context,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList)));
            Assert.Equal(refreshBeforeRestore,
                (ReadOracleLayerFlags(context.Bus, lower) &
                    LayerFlags.Refresh) != 0);
            restoreTrace = "restore:success=true:damage-preserved=true:" +
                $"visible=[{MoveSizeVisibleCoverage(context, lower)}]:" +
                $"before={lowerBeforeRestore}";
            if (invokeDoHookClipRectsAfterRestore)
            {
                var damageBeforeHook = DescribeRegion(context,
                    ReadOracleLayerPointer(context.Bus, lower,
                        OracleLayerPointer.DamageList));
                var damagePointerBeforeHook = ReadOracleLayerPointer(
                    context.Bus, lower, OracleLayerPointer.DamageList);
                var hookTrace = context.InvokeDoHookClipRects(
                    lower,
                    display.Address,
                    0,
                    0,
                    0,
                    31,
                    15,
                    includeClipRectChain: false);
                Assert.DoesNotContain("callbacks=0", hookTrace,
                    StringComparison.Ordinal);
                Assert.Equal(damagePointerBeforeHook,
                    ReadOracleLayerPointer(context.Bus, lower,
                        OracleLayerPointer.DamageList));
                Assert.Equal(damageBeforeHook, DescribeRegion(context,
                    ReadOracleLayerPointer(context.Bus, lower,
                        OracleLayerPointer.DamageList)));
                restoreTrace += ":do-hook=" + hookTrace +
                    ":damage-retained=true";
            }
            if (beginUpdateAfterRestore)
            {
                Assert.True(restoreEmptyRegionAfterDelete);
                var stableCoverage = MoveSizeVisibleCoverage(context, lower);
                var damagePointerBeforeUpdate = ReadOracleLayerPointer(
                    context.Bus, lower, OracleLayerPointer.DamageList);
                var damageBeforeUpdate = DescribeRegion(context,
                    damagePointerBeforeUpdate);
                Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.BeginUpdate,
                    state => state.A[0] = lower).D[0]);
                var expectedUpdateCoverage =
                    (restoredRegionMinX, restoredRegionMinY,
                        restoredRegionMaxX, restoredRegionMaxY) ==
                    ((short)8, (short)0, (short)9, (short)1)
                        ? "y=0,1:x=8,9"
                        : "y=0,3:x=8,23;y=4,11:x=8,11|20,23;y=12,15:x=8,23";
                Assert.Equal(expectedUpdateCoverage,
                    MoveSizeVisibleCoverage(context, lower));
                Assert.True((ReadOracleLayerFlags(context.Bus, lower) &
                    LayerFlags.Updating) != 0);
                Assert.Equal(damagePointerBeforeUpdate,
                    ReadOracleLayerPointer(context.Bus, lower,
                        OracleLayerPointer.DamageList));
                Assert.Equal(damageBeforeUpdate, DescribeRegion(context,
                    ReadOracleLayerPointer(context.Bus, lower,
                        OracleLayerPointer.DamageList)));
                var updateHookTrace = context.InvokeDoHookClipRects(
                    lower,
                    display.Address,
                    0,
                    0,
                    0,
                    31,
                    15,
                    includeClipRectChain: false);
                Assert.DoesNotContain("callbacks=0", updateHookTrace,
                    StringComparison.Ordinal);
                string updateEndTrace;
                if (completeRefreshAfterUpdate)
                {
                    var rastPort = ReadOracleLayerPointer(context.Bus, lower,
                        OracleLayerPointer.RastPort);
                    context.InvokeGraphics((int)GraphicsLvo.SetAPen, state =>
                    {
                        state.A[1] = rastPort;
                        state.D[0] = 1;
                    });
                    context.InvokeGraphics((int)GraphicsLvo.SetDrMd, state =>
                    {
                        state.A[1] = rastPort;
                        state.D[0] = 0;
                    });
                    context.InvokeGraphics((int)GraphicsLvo.SetWriteMask, state =>
                    {
                        state.A[0] = rastPort;
                        state.D[0] = 1;
                    });
                    var expectedPixels = Enumerable.Range(0,
                            checked(display.BytesPerRow * display.Height))
                        .Select(offset => context.Bus.ReadByte(
                            display.Planes[0] + checked((uint)offset)))
                        .ToArray();
                    for (var y = 0; y <= 15; y++)
                        for (var x = 8; x <= 23; x++)
                            if (x >= restoredRegionMinX &&
                                x <= restoredRegionMaxX &&
                                y >= restoredRegionMinY &&
                                y <= restoredRegionMaxY &&
                                (y is < 4 or > 11 || x is < 12 or > 19))
                            {
                                var offset = y * display.BytesPerRow + x / 8;
                                expectedPixels[offset] |=
                                    (byte)(0x80 >> (x & 7));
                            }
                    var updateClipHead = ReadOracleLayerPointer(context.Bus,
                        lower, OracleLayerPointer.ClipRect);
                    context.InvokeGraphics((int)GraphicsLvo.RectFill, state =>
                    {
                        state.A[1] = rastPort;
                        state.D[0] = 0;
                        state.D[1] = 0;
                        state.D[2] = 31;
                        state.D[3] = 15;
                    });
                    context.WaitForBlitterIdle();
                    Assert.False(context.Bus.Blitter.Busy);
                    Assert.Equal(expectedPixels, Enumerable.Range(0,
                            expectedPixels.Length)
                        .Select(offset => context.Bus.ReadByte(
                            display.Planes[0] + checked((uint)offset)))
                        .ToArray());
                    Assert.Equal(updateClipHead,
                        ReadOracleLayerPointer(context.Bus, lower,
                            OracleLayerPointer.ClipRect));
                    Assert.Equal(damageBeforeUpdate, DescribeRegion(context,
                        ReadOracleLayerPointer(context.Bus, lower,
                            OracleLayerPointer.DamageList)));
                    context.InvokeLayers(LayersLvo.EndUpdate, state =>
                    {
                        state.A[0] = lower;
                        state.D[0] = 1;
                    });
                    Assert.Equal(stableCoverage,
                        MoveSizeVisibleCoverage(context, lower));
                    Assert.True((ReadOracleLayerFlags(context.Bus, lower) &
                        LayerFlags.Updating) == 0);
                    Assert.True((ReadOracleLayerFlags(context.Bus, lower) &
                        LayerFlags.Refresh) != 0);
                    Assert.Equal(damagePointerBeforeUpdate,
                        ReadOracleLayerPointer(context.Bus, lower,
                            OracleLayerPointer.DamageList));
                    var retiredDamageBytes = Enumerable.Range(0,
                            checked((int)Region.Size))
                        .Select(offset => context.Bus.ReadByte(
                            damagePointerBeforeUpdate + checked((uint)offset)))
                        .ToArray();
                    Assert.Equal(new byte[checked((int)Region.Size)],
                        retiredDamageBytes);
                    updateEndTrace = ":draw=RectFill(0,0,31,15):pixels=" +
                        HashBitMap(context, display) +
                        ":end-update=complete:damage-retired=true:" +
                        "updating=false:refresh=true";
                }
                else
                {
                    context.InvokeLayers(LayersLvo.EndUpdate, state =>
                    {
                        state.A[0] = lower;
                        state.D[0] = 0;
                    });
                    Assert.Equal(stableCoverage,
                        MoveSizeVisibleCoverage(context, lower));
                    Assert.True((ReadOracleLayerFlags(context.Bus, lower) &
                        LayerFlags.Updating) == 0);
                    Assert.Equal(damagePointerBeforeUpdate,
                        ReadOracleLayerPointer(context.Bus, lower,
                            OracleLayerPointer.DamageList));
                    Assert.Equal(damageBeforeUpdate, DescribeRegion(context,
                        ReadOracleLayerPointer(context.Bus, lower,
                            OracleLayerPointer.DamageList)));
                    updateEndTrace = ":end-update=incomplete:stable-visible=[" +
                        stableCoverage + "]:damage-retained=true";
                }
                if (widenRegionAfterComplete || widenRegionAfterIncomplete)
                {
                    Assert.False(widenRegionAfterComplete &&
                        widenRegionAfterIncomplete);
                    Assert.Equal(widenRegionAfterComplete,
                        completeRefreshAfterUpdate);
                    widenedRegion = context.CreateSingleRectangleRegion(
                        6, 0, 25, 15);
                    Assert.Equal(restoredRegion,
                        context.InvokeLayers(LayersLvo.InstallClipRegion,
                            state =>
                            {
                                state.A[0] = lower;
                                state.A[1] = widenedRegion;
                            }).D[0]);
                    Assert.Equal(widenedRegion,
                        ReadOracleLayerPointer(context.Bus, lower,
                            OracleLayerPointer.ClipRegion));
                    const string widenedCoverage =
                        "y=0,3:x=6,25;y=4,11:x=6,11|20,25;y=12,15:x=6,25";
                    Assert.Equal(widenedCoverage,
                        MoveSizeVisibleCoverage(context, lower));
                    if (widenRegionAfterIncomplete)
                    {
                        Assert.Equal(damagePointerBeforeUpdate,
                            ReadOracleLayerPointer(context.Bus, lower,
                                OracleLayerPointer.DamageList));
                        Assert.Equal(damageBeforeUpdate, DescribeRegion(context,
                            ReadOracleLayerPointer(context.Bus, lower,
                                OracleLayerPointer.DamageList)));
                    }
                    updateEndTrace += ":widen-after-" +
                        (widenRegionAfterComplete ? "complete" : "incomplete") +
                        ":success:visible=[" + widenedCoverage + "]:damage=" +
                        DescribeRegion(context, ReadOracleLayerPointer(context.Bus,
                            lower, OracleLayerPointer.DamageList));
                }
                var expectedFinalCoverage = widenedRegion == 0
                    ? stableCoverage
                    : "y=0,3:x=6,25;y=4,11:x=6,11|20,25;y=12,15:x=6,25";
                Assert.Equal(expectedFinalCoverage,
                    MoveSizeVisibleCoverage(context, lower));
                restoreTrace += ":begin-update=success:active-visible=[" +
                    expectedUpdateCoverage + "]:hook=" + updateHookTrace +
                    updateEndTrace;
            }
        }

        var afterLower = DescribeMoveSizeLayerState(context, lower);
        var afterTop = DescribeMoveSizeLayerState(context, top);
        var afterDamage = DescribeRegion(context, lowerDamage);
        var trace = new[]
        {
            $"caller-region:empty={emptyCallerRegion}:bounds={FormatOracleRectangle(ReadOracleRegionBounds(context.Bus, region))}:nodes={regionNodeBytes.Length / checked((int)RegionRectangle.Size)}",
            "before:lower=" + beforeLower,
            "before:middle=" + beforeMiddle,
            "before:top=" + beforeTop,
            "before:lower-damage=" + beforeDamage,
            "delete:success=true",
            "after:lower=" + afterLower,
            "after:top=" + afterTop,
            "after:lower-damage=" + afterDamage,
            "damage:full-new-exposure-excludes-top=true",
            $"visible-cliprects:empty={emptyCallerRegion}:restricted-to-caller-region=true",
            restoreTrace,
            "caller-region:preserved=true"
        };

        var installedRegion = widenedRegion != 0
            ? widenedRegion
            : restoredRegion == 0 ? region : restoredRegion;
        Assert.Equal(installedRegion, context.InvokeLayers(LayersLvo.InstallClipRegion,
            state =>
            {
                state.A[0] = lower;
                state.A[1] = 0;
            }).D[0]);
        var restoredRegionNode = restoredRegion == 0
            ? 0
            : ReadOracleRegionFirst(context.Bus, restoredRegion);
        var widenedRegionNode = widenedRegion == 0
            ? 0
            : ReadOracleRegionFirst(context.Bus, widenedRegion);
        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = top);
        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = lower);
        context.DisposeLayerInfo(layerInfo);
        context.FreePlanarBitMap(display);
        if (regionNode != 0)
            context.Free(regionNode, RegionRectangle.Size);
        if (restoredRegionNode != 0)
            context.Free(restoredRegionNode, RegionRectangle.Size);
        if (widenedRegionNode != 0)
            context.Free(widenedRegionNode, RegionRectangle.Size);
        if (restoredRegion != 0)
            context.Free(restoredRegion, Region.Size);
        if (widenedRegion != 0)
            context.Free(widenedRegion, Region.Size);
        context.Free(region, Region.Size);
        return trace;
    }

    private static string[] TraceDeleteMiddleThenTopExposure(
        OracleContext context,
        bool beginIncompleteUpdate = false,
        bool completeUpdateAfterDeletion = false)
    {
        Assert.False(beginIncompleteUpdate && completeUpdateAfterDeletion);
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(48, 24, 1, 0x6B,
            completeUpdateAfterDeletion ? Exec.MemoryFlags.Chip : default);
        var lower = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            0,
            0,
            31,
            15);
        var middle = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            8,
            0,
            23,
            15);
        var top = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            12,
            4,
            19,
            11);

        var callerRegion = context.CreateSingleRectangleRegion(6, 0, 25, 15);
        var callerRegionNode = ReadOracleRegionFirst(context.Bus, callerRegion);
        var callerRegionBytes = Enumerable.Range(0, checked((int)Region.Size))
            .Select(offset => context.Bus.ReadByte(callerRegion +
                checked((uint)offset))).ToArray();
        var callerRegionNodeBytes = Enumerable.Range(0,
                checked((int)RegionRectangle.Size))
            .Select(offset => context.Bus.ReadByte(callerRegionNode +
                checked((uint)offset))).ToArray();
        Assert.Equal(0u, context.InvokeLayers(LayersLvo.InstallClipRegion,
            state =>
            {
                state.A[0] = lower;
                state.A[1] = callerRegion;
            }).D[0]);

        var beforeLower = DescribeMoveSizeLayerState(context, lower);
        var beforeMiddle = DescribeMoveSizeLayerState(context, middle);
        var beforeTop = DescribeMoveSizeLayerState(context, top);
        var beforeDamage = DescribeRegion(context, ReadOracleLayerPointer(
            context.Bus, lower, OracleLayerPointer.DamageList));

        var deleteMiddle = context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = middle);
        Assert.NotEqual(0u, deleteMiddle.D[0]);
        context.WaitForBlitterIdle();
        Assert.Equal("y=0,3:x=6,25;y=4,11:x=6,11|20,25;y=12,15:x=6,25",
            MoveSizeVisibleCoverage(context, lower));
        var middleDeleteDamage = ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.DamageList);
        var middleDeleteDamageDescription = DescribeRegion(context,
            middleDeleteDamage);
        var middleDeleteDamagePixels = ReadOracleRegionPixels(context,
            middleDeleteDamage);
        var expectedMiddleDeleteDamage = new HashSet<(int X, int Y)>();
        for (var y = 0; y <= 15; y++)
            for (var x = 8; x <= 23; x++)
                if (y is < 4 or > 11 || x is < 12 or > 19)
                    expectedMiddleDeleteDamage.Add((x, y));
        Assert.True(middleDeleteDamagePixels.SetEquals(
            expectedMiddleDeleteDamage));
        var afterMiddleDelete = DescribeMoveSizeLayerState(context, lower);

        var deleteTop = context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = top);
        Assert.NotEqual(0u, deleteTop.D[0]);
        context.WaitForBlitterIdle();
        Assert.Equal("y=0,15:x=6,25", MoveSizeVisibleCoverage(context, lower));
        var finalDamage = ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.DamageList);
        var finalDamagePixels = ReadOracleRegionPixels(context, finalDamage);
        var expectedFinalDamage = new HashSet<(int X, int Y)>();
        for (var y = 0; y <= 15; y++)
            for (var x = 8; x <= 23; x++)
                expectedFinalDamage.Add((x, y));
        Assert.True(finalDamagePixels.SetEquals(expectedFinalDamage));
        Assert.Equal(callerRegion, ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.ClipRegion));
        Assert.Equal(callerRegionBytes, Enumerable.Range(0,
                callerRegionBytes.Length)
            .Select(offset => context.Bus.ReadByte(callerRegion +
                checked((uint)offset))).ToArray());
        Assert.Equal(callerRegionNodeBytes, Enumerable.Range(0,
                callerRegionNodeBytes.Length)
            .Select(offset => context.Bus.ReadByte(callerRegionNode +
                checked((uint)offset))).ToArray());

        string updateTrace = string.Empty;
        if (beginIncompleteUpdate || completeUpdateAfterDeletion)
        {
            var damageBeforeUpdate = DescribeRegion(context, finalDamage);
            var damagePointerBeforeUpdate = ReadOracleLayerPointer(context.Bus,
                lower, OracleLayerPointer.DamageList);
            var coverageBeforeUpdate = MoveSizeVisibleCoverage(context, lower);
            Assert.Equal("y=0,15:x=6,25", coverageBeforeUpdate);
            Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.BeginUpdate,
                state => state.A[0] = lower).D[0]);
            const string updateCoverage = "y=0,15:x=8,23";
            Assert.Equal(updateCoverage,
                MoveSizeVisibleCoverage(context, lower));
            Assert.True((ReadOracleLayerFlags(context.Bus, lower) &
                LayerFlags.Updating) != 0);
            Assert.Equal(damagePointerBeforeUpdate,
                ReadOracleLayerPointer(context.Bus,
                lower, OracleLayerPointer.DamageList));
            Assert.Equal(damageBeforeUpdate, DescribeRegion(context,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList)));

            if (completeUpdateAfterDeletion)
            {
                var rastPort = ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.RastPort);
                context.InvokeGraphics((int)GraphicsLvo.SetAPen, state =>
                {
                    state.A[1] = rastPort;
                    state.D[0] = 1;
                });
                context.InvokeGraphics((int)GraphicsLvo.SetDrMd, state =>
                {
                    state.A[1] = rastPort;
                    state.D[0] = 0;
                });
                context.InvokeGraphics((int)GraphicsLvo.SetWriteMask, state =>
                {
                    state.A[0] = rastPort;
                    state.D[0] = 1;
                });
                var expectedPixels = Enumerable.Range(0,
                        checked(display.BytesPerRow * display.Height))
                    .Select(offset => context.Bus.ReadByte(
                        display.Planes[0] + checked((uint)offset)))
                    .ToArray();
                for (var y = 0; y <= 15; y++)
                    for (var x = 8; x <= 23; x++)
                    {
                        var offset = y * display.BytesPerRow + x / 8;
                        expectedPixels[offset] |= (byte)(0x80 >> (x & 7));
                    }
                var updateClipHead = ReadOracleLayerPointer(context.Bus,
                    lower, OracleLayerPointer.ClipRect);
                context.InvokeGraphics((int)GraphicsLvo.RectFill, state =>
                {
                    state.A[1] = rastPort;
                    state.D[0] = 0;
                    state.D[1] = 0;
                    state.D[2] = 31;
                    state.D[3] = 15;
                });
                context.WaitForBlitterIdle();
                Assert.False(context.Bus.Blitter.Busy);
                Assert.Equal(expectedPixels, Enumerable.Range(0,
                        expectedPixels.Length)
                    .Select(offset => context.Bus.ReadByte(
                        display.Planes[0] + checked((uint)offset)))
                    .ToArray());
                Assert.Equal(updateClipHead, ReadOracleLayerPointer(context.Bus,
                    lower, OracleLayerPointer.ClipRect));
                Assert.Equal(damageBeforeUpdate, DescribeRegion(context,
                    ReadOracleLayerPointer(context.Bus, lower,
                        OracleLayerPointer.DamageList)));
                context.InvokeLayers(LayersLvo.EndUpdate, state =>
                {
                    state.A[0] = lower;
                    state.D[0] = 1;
                });
                Assert.Equal(coverageBeforeUpdate,
                    MoveSizeVisibleCoverage(context, lower));
                Assert.True((ReadOracleLayerFlags(context.Bus, lower) &
                    LayerFlags.Updating) == 0);
                Assert.True((ReadOracleLayerFlags(context.Bus, lower) &
                    LayerFlags.Refresh) != 0);
                Assert.Equal(damagePointerBeforeUpdate,
                    ReadOracleLayerPointer(context.Bus, lower,
                        OracleLayerPointer.DamageList));
                var retiredDamageBytes = Enumerable.Range(0,
                        checked((int)Region.Size))
                    .Select(offset => context.Bus.ReadByte(
                        damagePointerBeforeUpdate + checked((uint)offset)))
                    .ToArray();
                Assert.Equal(new byte[checked((int)Region.Size)],
                    retiredDamageBytes);
                updateTrace = "begin-update=success:active-visible=[" +
                    updateCoverage + "]:draw=RectFill(0,0,31,15):pixels=" +
                    HashBitMap(context, display) +
                    ":end-update=complete:damage-retired=true:updating=false:" +
                    "refresh=true";
            }
            else
            {
            context.InvokeLayers(LayersLvo.EndUpdate, state =>
            {
                state.A[0] = lower;
                state.D[0] = 0;
            });
            Assert.Equal(coverageBeforeUpdate,
                MoveSizeVisibleCoverage(context, lower));
            Assert.True((ReadOracleLayerFlags(context.Bus, lower) &
                LayerFlags.Updating) == 0);
            Assert.Equal(finalDamage, ReadOracleLayerPointer(context.Bus,
                lower, OracleLayerPointer.DamageList));
            Assert.Equal(damageBeforeUpdate, DescribeRegion(context,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList)));
            updateTrace = "begin-update=success:active-visible=[" +
                updateCoverage + "]:end-update=incomplete:restored-visible=[" +
                coverageBeforeUpdate + "]:damage-retained=true";
            }
        }

        var finalLower = DescribeMoveSizeLayerState(context, lower);
        var trace = new[]
        {
            "caller-region:bounds=6,0,25,15:nodes=1",
            "before:lower=" + beforeLower,
            "before:middle=" + beforeMiddle,
            "before:top=" + beforeTop,
            "before:lower-damage=" + beforeDamage,
            "delete:middle=true:damage=" + middleDeleteDamageDescription,
            "after:middle-delete:lower=" + afterMiddleDelete,
            "delete:top=true:damage=" + DescribeRegion(context, finalDamage),
            "after:both-deleted:lower=" + finalLower,
            "damage:union-equals-full-middle=true",
            updateTrace,
            "caller-region:preserved=true"
        };

        Assert.Equal(callerRegion, context.InvokeLayers(LayersLvo.InstallClipRegion,
            state =>
            {
                state.A[0] = lower;
                state.A[1] = 0;
            }).D[0]);
        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = lower);
        context.DisposeLayerInfo(layerInfo);
        context.FreePlanarBitMap(display);
        context.Free(callerRegionNode, RegionRectangle.Size);
        context.Free(callerRegion, Region.Size);
        return trace;
    }

    private static string[] TraceDeleteTopThenMiddleExposure(
        OracleContext context,
        bool completeUpdate = false,
        bool narrowCallerRegion = false,
        bool incompleteUpdateAfterDeletion = false,
        bool drawDuringIncompleteUpdate = false,
        bool exerciseEmptyRegionAfterDeletion = false,
        bool beginUpdateWhileEmptyRegion = false,
        bool completeUpdateWhileEmptyRegion = false,
        bool invokeDoHookClipRectsAcrossEmptyRegion = false,
        bool invokeDoHookDuringEmptyUpdate = false,
        bool restoreCallerRegionDuringEmptyUpdate = false,
        bool exerciseRepeatedBeginUpdateWithRegionReplacement = false,
        bool completeRepeatedBeginUpdateRegionReplacement = false,
        bool exerciseEmptyRegionRoundTripDuringUpdate = false,
        bool exerciseDisjointCallerRegionDuringUpdate = false,
        bool partialDisjointCallerRegion = false)
    {
        Assert.False(completeUpdate && incompleteUpdateAfterDeletion);
        Assert.False(drawDuringIncompleteUpdate && !incompleteUpdateAfterDeletion);
        Assert.False(exerciseEmptyRegionAfterDeletion &&
            (completeUpdate || incompleteUpdateAfterDeletion));
        Assert.False(beginUpdateWhileEmptyRegion &&
            !exerciseEmptyRegionAfterDeletion);
        Assert.False(completeUpdateWhileEmptyRegion &&
            !exerciseEmptyRegionAfterDeletion);
        Assert.False(invokeDoHookClipRectsAcrossEmptyRegion &&
            !exerciseEmptyRegionAfterDeletion);
        Assert.False(invokeDoHookDuringEmptyUpdate &&
            !beginUpdateWhileEmptyRegion);
        Assert.False(restoreCallerRegionDuringEmptyUpdate &&
            !beginUpdateWhileEmptyRegion);
        Assert.False(exerciseRepeatedBeginUpdateWithRegionReplacement &&
            !exerciseEmptyRegionAfterDeletion);
        Assert.False(exerciseRepeatedBeginUpdateWithRegionReplacement &&
            (beginUpdateWhileEmptyRegion || completeUpdateWhileEmptyRegion ||
                restoreCallerRegionDuringEmptyUpdate));
        Assert.False(completeRepeatedBeginUpdateRegionReplacement &&
            !exerciseRepeatedBeginUpdateWithRegionReplacement);
        Assert.False(exerciseEmptyRegionRoundTripDuringUpdate &&
            !exerciseEmptyRegionAfterDeletion);
        Assert.False(exerciseEmptyRegionRoundTripDuringUpdate &&
            (beginUpdateWhileEmptyRegion || completeUpdateWhileEmptyRegion ||
                restoreCallerRegionDuringEmptyUpdate ||
                exerciseRepeatedBeginUpdateWithRegionReplacement));
        Assert.False(exerciseDisjointCallerRegionDuringUpdate &&
            !exerciseEmptyRegionAfterDeletion);
        Assert.False(exerciseDisjointCallerRegionDuringUpdate &&
            (beginUpdateWhileEmptyRegion || completeUpdateWhileEmptyRegion ||
                restoreCallerRegionDuringEmptyUpdate ||
                exerciseRepeatedBeginUpdateWithRegionReplacement ||
                exerciseEmptyRegionRoundTripDuringUpdate));
        Assert.False(partialDisjointCallerRegion &&
            !exerciseDisjointCallerRegionDuringUpdate);
        Assert.False(beginUpdateWhileEmptyRegion &&
            completeUpdateWhileEmptyRegion &&
            !restoreCallerRegionDuringEmptyUpdate);
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(48, 24, 1, 0x6B,
            completeUpdate || drawDuringIncompleteUpdate
                ? Exec.MemoryFlags.Chip
                : default);
        var lower = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            0,
            0,
            31,
            15);
        var middle = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            8,
            0,
            23,
            15);
        var top = context.CreateLayer(
            LayersLvo.CreateUpfrontLayer,
            layerInfo,
            display.Address,
            0,
            LayerCreationFlags.Simple,
            12,
            4,
            19,
            11);

        var useNarrowCallerRegion = narrowCallerRegion ||
            incompleteUpdateAfterDeletion;
        var callerRegion = useNarrowCallerRegion
            ? context.CreateSingleRectangleRegion(8, 0, 9, 1)
            : context.CreateSingleRectangleRegion(6, 0, 25, 15);
        var callerRegionCoverage = useNarrowCallerRegion
            ? "y=0,1:x=8,9"
            : "y=0,15:x=6,25";
        var callerRegionBounds = useNarrowCallerRegion
            ? "8,0,9,1"
            : "6,0,25,15";
        var callerRegionNode = ReadOracleRegionFirst(context.Bus, callerRegion);
        var callerRegionBytes = Enumerable.Range(0, checked((int)Region.Size))
            .Select(offset => context.Bus.ReadByte(callerRegion +
                checked((uint)offset))).ToArray();
        Assert.Equal(0u, context.InvokeLayers(LayersLvo.InstallClipRegion,
            state =>
            {
                state.A[0] = lower;
                state.A[1] = callerRegion;
            }).D[0]);

        var beforeLower = DescribeMoveSizeLayerState(context, lower);
        var beforeDamage = DescribeRegion(context, ReadOracleLayerPointer(
            context.Bus, lower, OracleLayerPointer.DamageList));
        var beforeCoverage = MoveSizeVisibleCoverage(context, lower);
        var deleteTop = context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = top);
        Assert.NotEqual(0u, deleteTop.D[0]);
        context.WaitForBlitterIdle();
        Assert.Equal(beforeCoverage, MoveSizeVisibleCoverage(context, lower));
        Assert.Equal(beforeDamage, DescribeRegion(context,
            ReadOracleLayerPointer(context.Bus, lower,
                OracleLayerPointer.DamageList)));
        var afterTopDelete = DescribeMoveSizeLayerState(context, lower);

        var deleteMiddle = context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = middle);
        Assert.NotEqual(0u, deleteMiddle.D[0]);
        context.WaitForBlitterIdle();
        Assert.Equal(callerRegionCoverage, MoveSizeVisibleCoverage(context, lower));
        var finalDamage = ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.DamageList);
        var finalDamagePixels = ReadOracleRegionPixels(context, finalDamage);
        var expectedFinalDamage = new HashSet<(int X, int Y)>();
        for (var y = 0; y <= 15; y++)
            for (var x = 8; x <= 23; x++)
                expectedFinalDamage.Add((x, y));
        Assert.True(finalDamagePixels.SetEquals(expectedFinalDamage));
        Assert.Equal(callerRegion, ReadOracleLayerPointer(context.Bus, lower,
            OracleLayerPointer.ClipRegion));
        Assert.Equal(callerRegionBytes, Enumerable.Range(0,
                callerRegionBytes.Length)
            .Select(offset => context.Bus.ReadByte(callerRegion +
                checked((uint)offset))).ToArray());

        uint emptyRegion = 0;
        string regionTransitionTrace = string.Empty;
        uint replacementCallerRegion = 0;
        uint replacementCallerRegionNode = 0;
        string updateRegionReplacementTrace = string.Empty;
        string emptyRegionRoundTripTrace = string.Empty;
        uint disjointCallerRegion = 0;
        uint disjointFirstNode = 0;
        uint disjointSecondNode = 0;
        string disjointRegionUpdateTrace = string.Empty;
        if (exerciseEmptyRegionAfterDeletion)
        {
            var damagePointerBeforeEmptyRegion = ReadOracleLayerPointer(
                context.Bus, lower, OracleLayerPointer.DamageList);
            var damageBeforeEmptyRegion = DescribeRegion(context,
                damagePointerBeforeEmptyRegion);
            emptyRegion = context.Allocate(Region.Size);
            var memory = new LayersTestGuestMemory(context.Bus);
            var emptyBounds = default(Rectangle);
            LayersRegionCodec.WriteBounds(ref memory,
                APTR.FromPointer(emptyRegion), in emptyBounds);
            LayersRegionCodec.WriteFirst(ref memory,
                APTR.FromPointer(emptyRegion), APTR.Null);
            Assert.Equal(callerRegion,
                context.InvokeLayers(LayersLvo.InstallClipRegion, state =>
                {
                    state.A[0] = lower;
                    state.A[1] = emptyRegion;
                }).D[0]);
            Assert.Equal(emptyRegion,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.ClipRegion));
            Assert.Equal(string.Empty, MoveSizeVisibleCoverage(context, lower));
            Assert.Equal(damagePointerBeforeEmptyRegion,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList));
            Assert.Equal(damageBeforeEmptyRegion, DescribeRegion(context,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList)));
            var emptyRegionHookTrace = string.Empty;
            if (invokeDoHookClipRectsAcrossEmptyRegion)
            {
                emptyRegionHookTrace = context.InvokeDoHookClipRects(
                    lower,
                    display.Address,
                    0,
                    0,
                    0,
                    31,
                    15,
                    includeClipRectChain: false);
                Assert.Contains("callbacks=0", emptyRegionHookTrace,
                    StringComparison.Ordinal);
                Assert.Equal(damagePointerBeforeEmptyRegion,
                    ReadOracleLayerPointer(context.Bus, lower,
                        OracleLayerPointer.DamageList));
                Assert.Equal(damageBeforeEmptyRegion, DescribeRegion(context,
                    ReadOracleLayerPointer(context.Bus, lower,
                        OracleLayerPointer.DamageList)));
            }
            var damageAfterEmptyRegionUpdate = damageBeforeEmptyRegion;
            var emptyRegionUpdateTrace = string.Empty;
            var activeUpdateClipRegionTrace = string.Empty;
            if (beginUpdateWhileEmptyRegion || completeUpdateWhileEmptyRegion)
            {
                var beginUpdateResult = context.InvokeLayers(
                    LayersLvo.BeginUpdate,
                    state => state.A[0] = lower).D[0];
                var updatingAfterBegin =
                    (ReadOracleLayerFlags(context.Bus, lower) &
                        LayerFlags.Updating) != 0;
                Assert.NotEqual(0u, beginUpdateResult);
                Assert.True(updatingAfterBegin);
                Assert.Equal(string.Empty,
                    MoveSizeVisibleCoverage(context, lower));
                Assert.Equal(damagePointerBeforeEmptyRegion,
                    ReadOracleLayerPointer(context.Bus, lower,
                        OracleLayerPointer.DamageList));
                Assert.Equal(damageBeforeEmptyRegion, DescribeRegion(context,
                    ReadOracleLayerPointer(context.Bus, lower,
                        OracleLayerPointer.DamageList)));
                var emptyUpdateHookTrace = string.Empty;
                if (invokeDoHookDuringEmptyUpdate)
                {
                    emptyUpdateHookTrace = context.InvokeDoHookClipRects(
                        lower,
                        display.Address,
                        0,
                        0,
                        0,
                        31,
                        15,
                        includeClipRectChain: false);
                    Assert.Contains("callbacks=0", emptyUpdateHookTrace,
                        StringComparison.Ordinal);
                    Assert.Equal(damagePointerBeforeEmptyRegion,
                        ReadOracleLayerPointer(context.Bus, lower,
                            OracleLayerPointer.DamageList));
                    Assert.Equal(damageBeforeEmptyRegion, DescribeRegion(context,
                        ReadOracleLayerPointer(context.Bus, lower,
                            OracleLayerPointer.DamageList)));
                }
                var restoredRegionHookDuringUpdateTrace = string.Empty;
                if (restoreCallerRegionDuringEmptyUpdate)
                {
                    var previousClipRegion = context.InvokeLayers(
                        LayersLvo.InstallClipRegion,
                        state =>
                        {
                            state.A[0] = lower;
                            state.A[1] = callerRegion;
                        }).D[0];
                    var previousRegionName = previousClipRegion == 0
                        ? "null"
                        : previousClipRegion == emptyRegion
                            ? "empty"
                            : previousClipRegion == callerRegion
                                ? "caller"
                                : "other";
                    var clipRegionAfterInstall = ReadOracleLayerPointer(
                        context.Bus, lower, OracleLayerPointer.ClipRegion);
                    var installedRegionName = clipRegionAfterInstall == 0
                        ? "null"
                        : clipRegionAfterInstall == emptyRegion
                            ? "empty"
                            : clipRegionAfterInstall == callerRegion
                                ? "caller"
                                : "other";
                    var visibleAfterInstall = MoveSizeVisibleCoverage(context,
                        lower);
                    restoredRegionHookDuringUpdateTrace =
                        context.InvokeDoHookClipRects(
                            lower,
                            display.Address,
                            0,
                            0,
                            0,
                            31,
                            15,
                            includeClipRectChain: false);
                    var expectedUpdateCoverage = narrowCallerRegion
                        ? callerRegionCoverage
                        : "y=0,15:x=8,23";
                    var expectedHookBounds = narrowCallerRegion
                        ? "8,0,9,1"
                        : "8,0,23,15";
                    Assert.Equal(expectedUpdateCoverage, visibleAfterInstall);
                    Assert.Contains("callbacks=1",
                        restoredRegionHookDuringUpdateTrace,
                        StringComparison.Ordinal);
                    Assert.Contains($"bounds={expectedHookBounds}",
                        restoredRegionHookDuringUpdateTrace,
                        StringComparison.Ordinal);
                    activeUpdateClipRegionTrace =
                        $":install-return={previousRegionName}:installed={installedRegionName}" +
                        $":visible=[{visibleAfterInstall}]";
                    Assert.Equal(damagePointerBeforeEmptyRegion,
                        ReadOracleLayerPointer(context.Bus, lower,
                            OracleLayerPointer.DamageList));
                    Assert.Equal(damageBeforeEmptyRegion, DescribeRegion(context,
                        ReadOracleLayerPointer(context.Bus, lower,
                            OracleLayerPointer.DamageList)));
                }
                context.InvokeLayers(LayersLvo.EndUpdate, state =>
                {
                    state.A[0] = lower;
                    state.D[0] = completeUpdateWhileEmptyRegion ? 1u : 0u;
                });
                Assert.True((ReadOracleLayerFlags(context.Bus, lower) &
                    LayerFlags.Updating) == 0);
                var coverageAfterEmptyRegionUpdate = MoveSizeVisibleCoverage(
                    context, lower);
                if (!restoreCallerRegionDuringEmptyUpdate)
                    Assert.Equal(string.Empty, coverageAfterEmptyRegionUpdate);
                else
                    Assert.Equal(callerRegionCoverage,
                        coverageAfterEmptyRegionUpdate);
                Assert.Equal(damagePointerBeforeEmptyRegion,
                    ReadOracleLayerPointer(context.Bus, lower,
                        OracleLayerPointer.DamageList));
                if (completeUpdateWhileEmptyRegion)
                {
                    Assert.True((ReadOracleLayerFlags(context.Bus, lower) &
                        LayerFlags.Refresh) != 0);
                    var retiredDamageBytes = Enumerable.Range(0,
                            checked((int)Region.Size))
                        .Select(offset => context.Bus.ReadByte(
                            damagePointerBeforeEmptyRegion +
                                checked((uint)offset)))
                        .ToArray();
                    Assert.Equal(new byte[checked((int)Region.Size)],
                        retiredDamageBytes);
                    damageAfterEmptyRegionUpdate = DescribeRegion(context,
                        ReadOracleLayerPointer(context.Bus, lower,
                            OracleLayerPointer.DamageList));
                }
                else
                {
                    Assert.Equal(damageBeforeEmptyRegion,
                        DescribeRegion(context, ReadOracleLayerPointer(
                            context.Bus, lower,
                            OracleLayerPointer.DamageList)));
                }
                var endUpdateTrace = completeUpdateWhileEmptyRegion
                    ? "complete:damage-retired"
                    : "incomplete:empty-visible:damage-retained";
                emptyRegionUpdateTrace = $":begin-update-result={beginUpdateResult}:" +
                    $"updating={updatingAfterBegin}:end-update={endUpdateTrace}:" +
                    $"visible-after-end=[{coverageAfterEmptyRegionUpdate}]" +
                    (invokeDoHookDuringEmptyUpdate
                        ? $":hook-during-update=[{emptyUpdateHookTrace}]"
                        : string.Empty) +
                    (restoreCallerRegionDuringEmptyUpdate
                        ? activeUpdateClipRegionTrace +
                            $":restored-region-hook-during-update=[{restoredRegionHookDuringUpdateTrace}]"
                        : string.Empty);
            }
            if (!restoreCallerRegionDuringEmptyUpdate)
            {
                Assert.Equal(emptyRegion,
                    context.InvokeLayers(LayersLvo.InstallClipRegion, state =>
                    {
                        state.A[0] = lower;
                        state.A[1] = callerRegion;
                    }).D[0]);
            }
            var clipRegionAfterUpdate = ReadOracleLayerPointer(context.Bus,
                lower, OracleLayerPointer.ClipRegion);
            var coverageAfterRegionTransition = MoveSizeVisibleCoverage(
                context, lower);
            if (!restoreCallerRegionDuringEmptyUpdate)
            {
                Assert.Equal(callerRegion, clipRegionAfterUpdate);
                Assert.Equal(callerRegionCoverage, coverageAfterRegionTransition);
            }
            else
            {
                Assert.Equal(callerRegion, clipRegionAfterUpdate);
                Assert.Equal(callerRegionCoverage, coverageAfterRegionTransition);
            }
            Assert.Equal(damagePointerBeforeEmptyRegion,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList));
            Assert.Equal(damageAfterEmptyRegionUpdate, DescribeRegion(context,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList)));
            var restoredRegionHookTrace = string.Empty;
            if (invokeDoHookClipRectsAcrossEmptyRegion)
            {
                restoredRegionHookTrace = context.InvokeDoHookClipRects(
                    lower,
                    display.Address,
                    0,
                    0,
                    0,
                    31,
                    15,
                    includeClipRectChain: false);
                if (!restoreCallerRegionDuringEmptyUpdate)
                    Assert.DoesNotContain("callbacks=0", restoredRegionHookTrace,
                        StringComparison.Ordinal);
                Assert.Equal(damageAfterEmptyRegionUpdate,
                    DescribeRegion(context, ReadOracleLayerPointer(context.Bus,
                        lower, OracleLayerPointer.DamageList)));
            }
            Assert.Equal(callerRegionBytes, Enumerable.Range(0,
                    callerRegionBytes.Length)
                .Select(offset => context.Bus.ReadByte(callerRegion +
                    checked((uint)offset))).ToArray());
            var regionAfterUpdateName = clipRegionAfterUpdate == callerRegion
                ? "caller"
                : clipRegionAfterUpdate == emptyRegion
                    ? "empty"
                    : clipRegionAfterUpdate == 0
                        ? "null"
                        : "other";
            regionTransitionTrace = "empty-region:visible=[]:damage-retained=true:" +
                "restore-caller-region:visible=[" + callerRegionCoverage +
                "]:damage=" + (completeUpdateWhileEmptyRegion
                    ? "retired"
                    : "retained") + emptyRegionUpdateTrace +
                (invokeDoHookClipRectsAcrossEmptyRegion
                    ? ":hooks-empty=[" + emptyRegionHookTrace +"]-restored=[" +
                        restoredRegionHookTrace + "]:damage-preserved=true"
                    : string.Empty) +
                (restoreCallerRegionDuringEmptyUpdate
                    ? $":region-after-update={regionAfterUpdateName}" +
                        $":visible-after-update=[{coverageAfterRegionTransition}]"
                    : string.Empty);
        }

        if (exerciseEmptyRegionRoundTripDuringUpdate)
        {
            var damagePointerBeforeUpdate = ReadOracleLayerPointer(context.Bus,
                lower, OracleLayerPointer.DamageList);
            var damageBeforeUpdate = DescribeRegion(context,
                damagePointerBeforeUpdate);
            var beginUpdateResult = context.InvokeLayers(LayersLvo.BeginUpdate,
                state => state.A[0] = lower).D[0];
            Assert.NotEqual(0u, beginUpdateResult);
            Assert.True((ReadOracleLayerFlags(context.Bus, lower) &
                LayerFlags.Updating) != 0);
            Assert.Equal("y=0,15:x=8,23",
                MoveSizeVisibleCoverage(context, lower));

            var clipToEmptyResult = context.InvokeLayers(
                LayersLvo.InstallClipRegion,
                state =>
                {
                    state.A[0] = lower;
                    state.A[1] = emptyRegion;
                }).D[0];
            Assert.Equal(callerRegion, clipToEmptyResult);
            Assert.Equal(emptyRegion,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.ClipRegion));
            Assert.Equal(string.Empty,
                MoveSizeVisibleCoverage(context, lower));
            Assert.Equal(damagePointerBeforeUpdate,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList));
            Assert.Equal(damageBeforeUpdate, DescribeRegion(context,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList)));
            var emptyUpdateHookTrace = context.InvokeDoHookClipRects(
                lower,
                display.Address,
                0,
                0,
                0,
                31,
                15,
                includeClipRectChain: false);
            Assert.Contains("callbacks=0", emptyUpdateHookTrace,
                StringComparison.Ordinal);

            var restoreCallerResult = context.InvokeLayers(
                LayersLvo.InstallClipRegion,
                state =>
                {
                    state.A[0] = lower;
                    state.A[1] = callerRegion;
                }).D[0];
            Assert.Equal(emptyRegion, restoreCallerResult);
            Assert.Equal(callerRegion,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.ClipRegion));
            Assert.Equal("y=0,15:x=8,23",
                MoveSizeVisibleCoverage(context, lower));
            var restoredUpdateHookTrace = context.InvokeDoHookClipRects(
                lower,
                display.Address,
                0,
                0,
                0,
                31,
                15,
                includeClipRectChain: false);
            Assert.Contains("callbacks=1", restoredUpdateHookTrace,
                StringComparison.Ordinal);
            Assert.Contains("bounds=8,0,23,15", restoredUpdateHookTrace,
                StringComparison.Ordinal);
            Assert.Equal(damagePointerBeforeUpdate,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList));
            Assert.Equal(damageBeforeUpdate, DescribeRegion(context,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList)));

            context.InvokeLayers(LayersLvo.EndUpdate, state =>
            {
                state.A[0] = lower;
                state.D[0] = 0;
            });
            Assert.True((ReadOracleLayerFlags(context.Bus, lower) &
                LayerFlags.Updating) == 0);
            Assert.Equal(callerRegion,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.ClipRegion));
            Assert.NotEqual(0u, ReadOracleLayerPointer(context.Bus, lower,
                OracleLayerPointer.ClipRect));
            Assert.Equal(callerRegionCoverage,
                MoveSizeVisibleCoverage(context, lower));
            Assert.Equal(damagePointerBeforeUpdate,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList));
            Assert.Equal(damageBeforeUpdate, DescribeRegion(context,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList)));
            emptyRegionRoundTripTrace =
                $"active-start=[y=0,15:x=8,23]:empty=[{emptyUpdateHookTrace}]:" +
                $"restored=[{restoredUpdateHookTrace}]:end=incomplete:" +
                $"stable-visible=[{callerRegionCoverage}]:damage-retained";
        }

        if (exerciseDisjointCallerRegionDuringUpdate)
        {
            var damagePointerBeforeUpdate = ReadOracleLayerPointer(context.Bus,
                lower, OracleLayerPointer.DamageList);
            var damageBeforeUpdate = DescribeRegion(context,
                damagePointerBeforeUpdate);
            var damageBytesBeforeUpdate = Enumerable.Range(0,
                    checked((int)Region.Size))
                .Select(offset => context.Bus.ReadByte(damagePointerBeforeUpdate +
                    checked((uint)offset))).ToArray();
            var beginUpdateResult = context.InvokeLayers(LayersLvo.BeginUpdate,
                state => state.A[0] = lower).D[0];
            Assert.NotEqual(0u, beginUpdateResult);
            Assert.True((ReadOracleLayerFlags(context.Bus, lower) &
                LayerFlags.Updating) != 0);
            Assert.Equal("y=0,15:x=8,23",
                MoveSizeVisibleCoverage(context, lower));

            disjointCallerRegion = context.Allocate(Region.Size);
            disjointFirstNode = context.Allocate(RegionRectangle.Size);
            disjointSecondNode = context.Allocate(RegionRectangle.Size);
            var memory = new LayersTestGuestMemory(context.Bus);
            var regionAddress = APTR.FromPointer(disjointCallerRegion);
            var firstNode = APTR.FromPointer(disjointFirstNode);
            var secondNode = APTR.FromPointer(disjointSecondNode);
            short regionMinX = partialDisjointCallerRegion ? (short)6 : (short)8;
            short regionMaxX = partialDisjointCallerRegion ? (short)27 : (short)23;
            short regionMaxY = partialDisjointCallerRegion ? (short)12 : (short)15;
            short firstLocalMaxX = partialDisjointCallerRegion ? (short)3 : (short)2;
            short secondLocalMinX = partialDisjointCallerRegion ? (short)19 : (short)13;
            short secondLocalMaxX = partialDisjointCallerRegion ? (short)21 : (short)15;
            LayersRegionCodec.WriteBounds(ref memory, regionAddress,
                LayersRectangleCodec.Create(regionMinX, 0, regionMaxX,
                    regionMaxY));
            LayersRegionCodec.WriteFirst(ref memory, regionAddress, firstNode);
            LayersRegionRectangleCodec.WritePrevious(ref memory, firstNode,
                LayersRegionCodec.HeadAnchor(regionAddress));
            LayersRegionRectangleCodec.WriteNext(ref memory, firstNode,
                secondNode);
            LayersRegionRectangleCodec.WriteBounds(ref memory, firstNode,
                LayersRectangleCodec.Create(0, 0, firstLocalMaxX, 2));
            LayersRegionRectangleCodec.WritePrevious(ref memory, secondNode,
                firstNode);
            LayersRegionRectangleCodec.WriteBounds(ref memory, secondNode,
                LayersRectangleCodec.Create(secondLocalMinX, 10,
                    secondLocalMaxX, 12));
            var regionBytes = Enumerable.Range(0, checked((int)Region.Size))
                .Select(offset => context.Bus.ReadByte(disjointCallerRegion +
                    checked((uint)offset))).ToArray();
            var firstNodeBytes = Enumerable.Range(0,
                    checked((int)RegionRectangle.Size))
                .Select(offset => context.Bus.ReadByte(disjointFirstNode +
                    checked((uint)offset))).ToArray();
            var secondNodeBytes = Enumerable.Range(0,
                    checked((int)RegionRectangle.Size))
                .Select(offset => context.Bus.ReadByte(disjointSecondNode +
                    checked((uint)offset))).ToArray();

            Assert.Equal(callerRegion,
                context.InvokeLayers(LayersLvo.InstallClipRegion, state =>
                {
                    state.A[0] = lower;
                    state.A[1] = disjointCallerRegion;
                }).D[0]);
            var expectedActiveCoverage = partialDisjointCallerRegion
                ? "y=0,2:x=8,9"
                : "y=0,2:x=8,10;y=10,12:x=21,23";
            var expectedStableCoverage = partialDisjointCallerRegion
                ? "y=0,2:x=6,9;y=10,12:x=25,27"
                : expectedActiveCoverage;
            var expectedCallbackCount = partialDisjointCallerRegion
                ? "callbacks=1"
                : "callbacks=2";
            Assert.Equal(disjointCallerRegion,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.ClipRegion));
            Assert.Equal(expectedActiveCoverage,
                MoveSizeVisibleCoverage(context, lower));
            var disjointHookTrace = context.InvokeDoHookClipRects(
                lower,
                display.Address,
                0,
                0,
                0,
                31,
                15,
                includeClipRectChain: false);
            Assert.Contains(expectedCallbackCount, disjointHookTrace,
                StringComparison.Ordinal);
            if (partialDisjointCallerRegion)
            {
                Assert.Contains("bounds=8,0,9,2", disjointHookTrace,
                    StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains("bounds=8,0,10,2", disjointHookTrace,
                    StringComparison.Ordinal);
                Assert.Contains("bounds=21,10,23,12", disjointHookTrace,
                    StringComparison.Ordinal);
            }
            Assert.Equal(damagePointerBeforeUpdate,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList));
            Assert.Equal(damageBeforeUpdate, DescribeRegion(context,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList)));

            context.InvokeLayers(LayersLvo.EndUpdate, state =>
            {
                state.A[0] = lower;
                state.D[0] = 0;
            });
            Assert.True((ReadOracleLayerFlags(context.Bus, lower) &
                LayerFlags.Updating) == 0);
            Assert.Equal(disjointCallerRegion,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.ClipRegion));
            Assert.Equal(expectedStableCoverage,
                MoveSizeVisibleCoverage(context, lower));
            Assert.Equal(damagePointerBeforeUpdate,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList));
            Assert.Equal(damageBeforeUpdate, DescribeRegion(context,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList)));
            Assert.Equal(regionBytes, Enumerable.Range(0, regionBytes.Length)
                .Select(offset => context.Bus.ReadByte(disjointCallerRegion +
                    checked((uint)offset))).ToArray());
            Assert.Equal(firstNodeBytes,
                Enumerable.Range(0, firstNodeBytes.Length)
                    .Select(offset => context.Bus.ReadByte(disjointFirstNode +
                        checked((uint)offset))).ToArray());
            Assert.Equal(secondNodeBytes,
                Enumerable.Range(0, secondNodeBytes.Length)
                    .Select(offset => context.Bus.ReadByte(disjointSecondNode +
                        checked((uint)offset))).ToArray());
            Assert.Equal(damageBytesBeforeUpdate,
                Enumerable.Range(0, damageBytesBeforeUpdate.Length)
                    .Select(offset => context.Bus.ReadByte(
                        damagePointerBeforeUpdate + checked((uint)offset)))
                    .ToArray());
            disjointRegionUpdateTrace =
                $"active-visible=[y=0,15:x=8,23]:active-installed=[{expectedActiveCoverage}]:" +
                $"stable-after-end=[{expectedStableCoverage}]:" +
                $"hook=[{disjointHookTrace}]:end=incomplete:damage-retained";
        }

        if (exerciseRepeatedBeginUpdateWithRegionReplacement)
        {
            var damagePointerBeforeNestedUpdate = ReadOracleLayerPointer(
                context.Bus, lower, OracleLayerPointer.DamageList);
            var damageBeforeNestedUpdate = DescribeRegion(context,
                damagePointerBeforeNestedUpdate);
            Assert.Equal(callerRegion,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.ClipRegion));
            Assert.Equal(callerRegionCoverage,
                MoveSizeVisibleCoverage(context, lower));

            var outerBeginResult = context.InvokeLayers(LayersLvo.BeginUpdate,
                state => state.A[0] = lower).D[0];
            Assert.NotEqual(0u, outerBeginResult);
            Assert.True((ReadOracleLayerFlags(context.Bus, lower) &
                LayerFlags.Updating) != 0);
            Assert.Equal("y=0,15:x=8,23",
                MoveSizeVisibleCoverage(context, lower));
            Assert.Equal(damagePointerBeforeNestedUpdate,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList));
            Assert.Equal(damageBeforeNestedUpdate, DescribeRegion(context,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList)));

            var nestedBeginResult = context.InvokeLayers(LayersLvo.BeginUpdate,
                state => state.A[0] = lower).D[0];
            Assert.NotEqual(0u, nestedBeginResult);
            Assert.True((ReadOracleLayerFlags(context.Bus, lower) &
                LayerFlags.Updating) != 0);
            Assert.Equal("y=0,15:x=8,23",
                MoveSizeVisibleCoverage(context, lower));

            replacementCallerRegion = context.CreateSingleRectangleRegion(
                8, 0, 9, 1);
            replacementCallerRegionNode = ReadOracleRegionFirst(context.Bus,
                replacementCallerRegion);
            Assert.Equal(callerRegion,
                context.InvokeLayers(LayersLvo.InstallClipRegion, state =>
                {
                    state.A[0] = lower;
                    state.A[1] = replacementCallerRegion;
                }).D[0]);
            Assert.Equal(replacementCallerRegion,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.ClipRegion));
            Assert.Equal("y=0,1:x=8,9",
                MoveSizeVisibleCoverage(context, lower));
            var updateRegionHookTrace = context.InvokeDoHookClipRects(
                lower,
                display.Address,
                0,
                0,
                0,
                31,
                15,
                includeClipRectChain: false);
            Assert.Contains("callbacks=1", updateRegionHookTrace,
                StringComparison.Ordinal);
            Assert.Contains("bounds=8,0,9,1", updateRegionHookTrace,
                StringComparison.Ordinal);
            Assert.Equal(damagePointerBeforeNestedUpdate,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList));
            Assert.Equal(damageBeforeNestedUpdate, DescribeRegion(context,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList)));

            context.InvokeLayers(LayersLvo.EndUpdate, state =>
            {
                state.A[0] = lower;
                state.D[0] = completeRepeatedBeginUpdateRegionReplacement
                    ? 1u
                    : 0u;
            });
            Assert.True((ReadOracleLayerFlags(context.Bus, lower) &
                LayerFlags.Updating) == 0);
            Assert.Equal(replacementCallerRegion,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.ClipRegion));
            Assert.Equal("y=0,1:x=8,9",
                MoveSizeVisibleCoverage(context, lower));
            Assert.Equal(damagePointerBeforeNestedUpdate,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList));
            var damageAfterNestedUpdate = DescribeRegion(context,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList));
            if (completeRepeatedBeginUpdateRegionReplacement)
            {
                var retiredDamageBytes = Enumerable.Range(0,
                        checked((int)Region.Size))
                    .Select(offset => context.Bus.ReadByte(
                        damagePointerBeforeNestedUpdate + checked((uint)offset)))
                    .ToArray();
                Assert.Equal(new byte[checked((int)Region.Size)],
                    retiredDamageBytes);
                Assert.NotEqual(damageBeforeNestedUpdate,
                    damageAfterNestedUpdate);
            }
            else
            {
                Assert.Equal(damageBeforeNestedUpdate,
                    damageAfterNestedUpdate);
            }
            Assert.Equal(callerRegionBytes, Enumerable.Range(0,
                    callerRegionBytes.Length)
                .Select(offset => context.Bus.ReadByte(callerRegion +
                    checked((uint)offset))).ToArray());
            updateRegionReplacementTrace =
                "outer-update=active-visible[y=0,15:x=8,23]:" +
                "repeated-begin=accepted:install=one-pixel:" +
                $"hook=[{updateRegionHookTrace}]:" +
                $"end={(completeRepeatedBeginUpdateRegionReplacement ? "complete" : "incomplete")}:" +
                $"updating-cleared:damage={(completeRepeatedBeginUpdateRegionReplacement ? "retired" : "retained")}:" +
                "visible=[y=0,1:x=8,9]";
        }

        uint widenedRegion = 0;
        uint widenedRegionNode = 0;
        string updateTrace = string.Empty;
        if (completeUpdate)
        {
            var damagePointerBeforeUpdate = ReadOracleLayerPointer(context.Bus,
                lower, OracleLayerPointer.DamageList);
            var damageBeforeUpdate = DescribeRegion(context,
                damagePointerBeforeUpdate);
            Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.BeginUpdate,
                state => state.A[0] = lower).D[0]);
            var updateCoverage = narrowCallerRegion
                ? "y=0,1:x=8,9"
                : "y=0,15:x=8,23";
            Assert.Equal(updateCoverage,
                MoveSizeVisibleCoverage(context, lower));
            Assert.True((ReadOracleLayerFlags(context.Bus, lower) &
                LayerFlags.Updating) != 0);
            Assert.Equal(damagePointerBeforeUpdate,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList));
            Assert.Equal(damageBeforeUpdate, DescribeRegion(context,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList)));

            var rastPort = ReadOracleLayerPointer(context.Bus, lower,
                OracleLayerPointer.RastPort);
            context.InvokeGraphics((int)GraphicsLvo.SetAPen, state =>
            {
                state.A[1] = rastPort;
                state.D[0] = 1;
            });
            context.InvokeGraphics((int)GraphicsLvo.SetDrMd, state =>
            {
                state.A[1] = rastPort;
                state.D[0] = 0;
            });
            context.InvokeGraphics((int)GraphicsLvo.SetWriteMask, state =>
            {
                state.A[0] = rastPort;
                state.D[0] = 1;
            });
            var expectedPixels = Enumerable.Range(0,
                    checked(display.BytesPerRow * display.Height))
                .Select(offset => context.Bus.ReadByte(
                    display.Planes[0] + checked((uint)offset)))
                .ToArray();
            for (var y = 0; y <= 15; y++)
                for (var x = 8; x <= 23; x++)
                {
                    if (narrowCallerRegion && (x > 9 || y > 1))
                        continue;
                    var offset = y * display.BytesPerRow + x / 8;
                    expectedPixels[offset] |= (byte)(0x80 >> (x & 7));
                }
            var updateClipHead = ReadOracleLayerPointer(context.Bus,
                lower, OracleLayerPointer.ClipRect);
            context.InvokeGraphics((int)GraphicsLvo.RectFill, state =>
            {
                state.A[1] = rastPort;
                state.D[0] = 0;
                state.D[1] = 0;
                state.D[2] = 31;
                state.D[3] = 15;
            });
            context.WaitForBlitterIdle();
            Assert.False(context.Bus.Blitter.Busy);
            Assert.Equal(expectedPixels, Enumerable.Range(0,
                    expectedPixels.Length)
                .Select(offset => context.Bus.ReadByte(
                    display.Planes[0] + checked((uint)offset)))
                .ToArray());
            Assert.Equal(updateClipHead, ReadOracleLayerPointer(context.Bus,
                lower, OracleLayerPointer.ClipRect));
            Assert.Equal(damageBeforeUpdate, DescribeRegion(context,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList)));
            context.InvokeLayers(LayersLvo.EndUpdate, state =>
            {
                state.A[0] = lower;
                state.D[0] = 1;
            });
            Assert.Equal(callerRegionCoverage,
                MoveSizeVisibleCoverage(context, lower));
            Assert.True((ReadOracleLayerFlags(context.Bus, lower) &
                LayerFlags.Updating) == 0);
            Assert.True((ReadOracleLayerFlags(context.Bus, lower) &
                LayerFlags.Refresh) != 0);
            Assert.Equal(damagePointerBeforeUpdate,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList));
            var retiredDamageBytes = Enumerable.Range(0,
                    checked((int)Region.Size))
                .Select(offset => context.Bus.ReadByte(
                    damagePointerBeforeUpdate + checked((uint)offset)))
                .ToArray();
            Assert.Equal(new byte[checked((int)Region.Size)],
                retiredDamageBytes);
            Assert.Equal(callerRegion,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.ClipRegion));
            Assert.Equal(callerRegionBytes, Enumerable.Range(0,
                    callerRegionBytes.Length)
                .Select(offset => context.Bus.ReadByte(callerRegion +
                    checked((uint)offset))).ToArray());
            updateTrace = "begin-update=success:active-visible=[" +
                updateCoverage + "]:draw=RectFill(0,0,31,15):pixels=" +
                HashBitMap(context, display) +
                ":end-update=complete:damage-retired=true:updating=false:" +
                "refresh=true";
        }
        else if (incompleteUpdateAfterDeletion)
        {
            var damagePointerBeforeUpdate = ReadOracleLayerPointer(context.Bus,
                lower, OracleLayerPointer.DamageList);
            var damageBeforeUpdate = DescribeRegion(context,
                damagePointerBeforeUpdate);
            Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.BeginUpdate,
                state => state.A[0] = lower).D[0]);
            Assert.Equal(callerRegionCoverage,
                MoveSizeVisibleCoverage(context, lower));
            Assert.True((ReadOracleLayerFlags(context.Bus, lower) &
                LayerFlags.Updating) != 0);
            Assert.Equal(damagePointerBeforeUpdate,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList));
            Assert.Equal(damageBeforeUpdate, DescribeRegion(context,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList)));
            if (drawDuringIncompleteUpdate)
            {
                var rastPort = ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.RastPort);
                context.InvokeGraphics((int)GraphicsLvo.SetAPen, state =>
                {
                    state.A[1] = rastPort;
                    state.D[0] = 1;
                });
                context.InvokeGraphics((int)GraphicsLvo.SetDrMd, state =>
                {
                    state.A[1] = rastPort;
                    state.D[0] = 0;
                });
                context.InvokeGraphics((int)GraphicsLvo.SetWriteMask, state =>
                {
                    state.A[0] = rastPort;
                    state.D[0] = 1;
                });
                var expectedPixels = Enumerable.Range(0,
                        checked(display.BytesPerRow * display.Height))
                    .Select(offset => context.Bus.ReadByte(
                        display.Planes[0] + checked((uint)offset)))
                    .ToArray();
                for (var y = 0; y <= 1; y++)
                    for (var x = 8; x <= 9; x++)
                    {
                        var offset = y * display.BytesPerRow + x / 8;
                        expectedPixels[offset] |= (byte)(0x80 >> (x & 7));
                    }
                var updateClipHead = ReadOracleLayerPointer(context.Bus,
                    lower, OracleLayerPointer.ClipRect);
                context.InvokeGraphics((int)GraphicsLvo.RectFill, state =>
                {
                    state.A[1] = rastPort;
                    state.D[0] = 0;
                    state.D[1] = 0;
                    state.D[2] = 31;
                    state.D[3] = 15;
                });
                context.WaitForBlitterIdle();
                Assert.False(context.Bus.Blitter.Busy);
                Assert.Equal(expectedPixels, Enumerable.Range(0,
                        expectedPixels.Length)
                    .Select(offset => context.Bus.ReadByte(
                        display.Planes[0] + checked((uint)offset)))
                    .ToArray());
                Assert.Equal(updateClipHead, ReadOracleLayerPointer(context.Bus,
                    lower, OracleLayerPointer.ClipRect));
                Assert.Equal(damageBeforeUpdate, DescribeRegion(context,
                    ReadOracleLayerPointer(context.Bus, lower,
                        OracleLayerPointer.DamageList)));
            }
            context.InvokeLayers(LayersLvo.EndUpdate, state =>
            {
                state.A[0] = lower;
                state.D[0] = 0;
            });
            Assert.Equal(callerRegionCoverage,
                MoveSizeVisibleCoverage(context, lower));
            Assert.True((ReadOracleLayerFlags(context.Bus, lower) &
                LayerFlags.Updating) == 0);
            Assert.Equal(damagePointerBeforeUpdate,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList));
            Assert.Equal(damageBeforeUpdate, DescribeRegion(context,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList)));

            widenedRegion = context.CreateSingleRectangleRegion(6, 0, 25, 15);
            widenedRegionNode = ReadOracleRegionFirst(context.Bus,
                widenedRegion);
            Assert.Equal(callerRegion,
                context.InvokeLayers(LayersLvo.InstallClipRegion, state =>
                {
                    state.A[0] = lower;
                    state.A[1] = widenedRegion;
                }).D[0]);
            Assert.Equal(widenedRegion,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.ClipRegion));
            const string widenedCoverage = "y=0,15:x=6,25";
            Assert.Equal(widenedCoverage,
                MoveSizeVisibleCoverage(context, lower));
            Assert.Equal(damagePointerBeforeUpdate,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList));
            Assert.Equal(damageBeforeUpdate, DescribeRegion(context,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList)));
            Assert.Equal(callerRegionBytes, Enumerable.Range(0,
                    callerRegionBytes.Length)
                .Select(offset => context.Bus.ReadByte(callerRegion +
                    checked((uint)offset))).ToArray());
            updateTrace = "begin-update=success:active-visible=[" +
                callerRegionCoverage + "]:" +
                (drawDuringIncompleteUpdate
                    ? "draw=RectFill(0,0,31,15):pixels=" +
                        HashBitMap(context, display) + ":"
                    : string.Empty) +
                "end-update=incomplete:damage-retained=true:widened-visible=[" +
                widenedCoverage + "]:damage-still-pending=true";
        }

        var finalLower = DescribeMoveSizeLayerState(context, lower);
        var trace = new[]
        {
            $"caller-region:bounds={callerRegionBounds}:nodes=1",
            "before:lower=" + beforeLower,
            "before:lower-damage=" + beforeDamage,
            "before:visible=" + beforeCoverage,
            "delete:top=true:damage=" + DescribeRegion(context,
                ReadOracleLayerPointer(context.Bus, lower,
                    OracleLayerPointer.DamageList)),
            "after:top-delete:lower=" + afterTopDelete,
            "delete:middle=true:damage=" + DescribeRegion(context, finalDamage),
            "after:both-deleted:lower=" + finalLower,
            "damage:union-equals-full-middle=true",
            regionTransitionTrace,
            emptyRegionRoundTripTrace,
            disjointRegionUpdateTrace,
            updateRegionReplacementTrace,
            updateTrace,
            "caller-region:preserved=true"
        };

        var installedRegion = disjointCallerRegion != 0
            ? disjointCallerRegion
            : replacementCallerRegion != 0
                ? replacementCallerRegion
                : widenedRegion == 0
                    ? callerRegion
                    : widenedRegion;
        var removedCallerRegion = context.InvokeLayers(
            LayersLvo.InstallClipRegion, state =>
            {
                state.A[0] = lower;
                state.A[1] = 0;
            }).D[0];
        if (!restoreCallerRegionDuringEmptyUpdate)
            Assert.Equal(installedRegion, removedCallerRegion);
        context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = lower);
        context.DisposeLayerInfo(layerInfo);
        context.FreePlanarBitMap(display);
        if (emptyRegion != 0)
            context.Free(emptyRegion, Region.Size);
        if (replacementCallerRegionNode != 0)
            context.Free(replacementCallerRegionNode, RegionRectangle.Size);
        if (replacementCallerRegion != 0)
            context.Free(replacementCallerRegion, Region.Size);
        if (disjointFirstNode != 0)
            context.Free(disjointFirstNode, RegionRectangle.Size);
        if (disjointSecondNode != 0)
            context.Free(disjointSecondNode, RegionRectangle.Size);
        if (disjointCallerRegion != 0)
            context.Free(disjointCallerRegion, Region.Size);
        if (widenedRegionNode != 0)
            context.Free(widenedRegionNode, RegionRectangle.Size);
        if (widenedRegion != 0)
            context.Free(widenedRegion, Region.Size);
        context.Free(callerRegionNode, RegionRectangle.Size);
        context.Free(callerRegion, Region.Size);
        return trace;
    }

    private static HashSet<(int X, int Y)> ReadOracleRegionPixels(
        OracleContext context,
        uint region)
    {
        var pixels = new HashSet<(int X, int Y)>();
        if (region == 0)
            return pixels;
        var bounds = ReadOracleRegionBounds(context.Bus, region);
        var seen = new HashSet<uint>();
        for (var node = ReadOracleRegionFirst(context.Bus, region);
             node != 0;
             node = ReadOracleRegionRectanglePointer(context.Bus, node,
                 previous: false))
        {
            Assert.True(seen.Add(node),
                "Damage Region rectangle chain must be acyclic.");
            var rectangle = ReadOracleRegionRectangleBounds(context.Bus, node);
            for (var y = bounds.MinY + rectangle.MinY;
                 y <= bounds.MinY + rectangle.MaxY; y++)
                for (var x = bounds.MinX + rectangle.MinX;
                     x <= bounds.MinX + rectangle.MaxX; x++)
                    pixels.Add((x, y));
        }
        return pixels;
    }

    private static string DescribeMoveSizeLayerState(
        OracleContext context,
        uint layer)
    {
        var refresh = (ReadOracleLayerFlags(context.Bus, layer) &
            LayerFlags.Refresh) != 0;
        var damage = ReadOracleLayerPointer(
            context.Bus, layer, OracleLayerPointer.DamageList);
        return $"bounds={FormatOracleRectangle(ReadOracleLayerBounds(context.Bus, layer))}:" +
            $"refresh={refresh}:damage={DescribeRegion(context, damage)}:" +
            $"visible=[{MoveSizeVisibleCoverage(context, layer)}]";
    }

    private static string MoveSizeVisibleCoverage(
        OracleContext context,
        uint layer)
    {
        const int displayWidth = 48;
        const int displayHeight = 24;
        var rectangles = new List<(int MinX, int MinY, int MaxX, int MaxY)>();
        var seen = new HashSet<uint>();
        for (var node = ReadOracleLayerPointer(context.Bus, layer,
                 OracleLayerPointer.ClipRect); node != 0;
             node = ReadOracleClipRectPointer(context.Bus, node,
                 OracleClipRectPointer.Next))
        {
            Assert.True(seen.Count < 256 && seen.Add(node));
            var bounds = ReadOracleClipRectBounds(context.Bus, node);
            Assert.InRange((int)bounds.MinX, 0, displayWidth - 1);
            Assert.InRange((int)bounds.MaxX, bounds.MinX, displayWidth - 1);
            Assert.InRange((int)bounds.MinY, 0, displayHeight - 1);
            Assert.InRange((int)bounds.MaxY, bounds.MinY, displayHeight - 1);
            if (ReadOracleClipRectPointer(context.Bus, node,
                    OracleClipRectPointer.ObscuringLayer) != 0)
                continue;
            rectangles.Add((bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY));
        }
        return CanonicalRegionCoverage(rectangles,
            "Visible ClipRect coverage must be nonoverlapping.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopperStartClipBlitDuringDualDamageRefreshPreservesBothOwners(bool poisonUnusedRegisterBits)
    {
        var context = CreateCopperStartOracle();
        try { _ = TraceClipBlitDuringDualDamageRefresh(context, poisonUnusedRegisterBits); }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClipBlitDuringDualDamageRefreshMatchesNativeV4063(bool poisonUnusedRegisterBits)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native dual-damage ClipBlit NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;

        // Execute the native wrapper, its source/destination clipping, and
        // delegated blits without a graphics probe or software BltBitMap leaf.
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            foreach (var lvo in new[] { GraphicsLvo.ClipBlit, GraphicsLvo.BltBitMap })
            {
                var vector = unchecked((uint)((int)native.GraphicsBase + (int)lvo));
                Assert.False(native.Bus.HasHostGateway(vector));
                Assert.Equal((ushort)0x4EF9, native.Bus.ReadWord(vector));
                Assert.InRange(native.Bus.ReadLong(vector + 2), 0x00F8_0000u, 0x00FF_FFFFu);
            }
            var memory = new LayersTestGuestMemory(native.Bus);
            _output.WriteLine("G237 configured V40.63: A500 PAL OCS, 512 KiB CHIP, " +
                "AccurateM68000/live Agnus; native graphics vectors uninstrumented. " +
                $"layers={ReadOracleLibraryVersion(native.Bus, native.LayersBase)}." +
                $"{LayersLibraryCodec.ReadRevision(ref memory, APTR.FromPointer(native.LayersBase))}; " +
                $"graphics={ReadOracleLibraryVersion(native.Bus, native.GraphicsBase)}." +
                $"{LayersLibraryCodec.ReadRevision(ref memory, APTR.FromPointer(native.GraphicsBase))}");
            expected = TraceClipBlitDuringDualDamageRefresh(native, poisonUnusedRegisterBits);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try { Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(copperStart, poisonUnusedRegisterBits)); }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopperStartClipBlitDuringDualDisconnectedDamageRefreshPreservesBothOwners(
        bool poisonUnusedRegisterBits)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, disconnectedDamage: true);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClipBlitDuringDualDisconnectedDamageRefreshMatchesNativeV4063(
        bool poisonUnusedRegisterBits)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native dual-disconnected-damage ClipBlit NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, disconnectedDamage: true);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, disconnectedDamage: true));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopperStartClipBlitDuringDualDisconnectedDamageWithDisjointCallerRegionsPreservesBothOwners(
        bool poisonUnusedRegisterBits)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, disconnectedDamage: true,
                disjointCallerClipRegions: true);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClipBlitDuringDualDisconnectedDamageWithDisjointCallerRegionsMatchesNativeV4063(
        bool poisonUnusedRegisterBits)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native dual-disconnected-damage ClipBlit with disjoint caller Regions NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, disconnectedDamage: true,
                disjointCallerClipRegions: true);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, disconnectedDamage: true,
                disjointCallerClipRegions: true));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopperStartClipBlitAfterDualActiveUpdateRegionReplacementPreservesBothOwners(
        bool poisonUnusedRegisterBits)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClipBlitAfterDualActiveUpdateRegionReplacementMatchesNativeV4063(
        bool poisonUnusedRegisterBits)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native dual active-update Region replacement ClipBlit NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopperStartClipBlitAfterDualActiveUpdateRegionReplacementIncompleteEndUpdatePreservesBothOwners(
        bool poisonUnusedRegisterBits)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClipBlitAfterDualActiveUpdateRegionReplacementIncompleteEndUpdateMatchesNativeV4063(
        bool poisonUnusedRegisterBits)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native dual active-update Region replacement with incomplete EndUpdate NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopperStartClipBlitRetryAfterDualActiveUpdateRegionReplacementPreservesBothOwners(
        bool poisonUnusedRegisterBits)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClipBlitRetryAfterDualActiveUpdateRegionReplacementMatchesNativeV4063(
        bool poisonUnusedRegisterBits)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native ClipBlit retry after dual incomplete update NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopperStartClipBlitRetryCompletesSourceBeforeDestinationPreservesBothOwners(
        bool poisonUnusedRegisterBits)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination: true);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClipBlitRetryCompletesSourceBeforeDestinationMatchesNativeV4063(
        bool poisonUnusedRegisterBits)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native source-first ClipBlit retry after dual incomplete updates NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination: true);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination: true));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterDualRegionReplacementPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterDualRegionReplacementMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after dual Region replacement NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterDualRegionReplacementWithDifferentNonzeroOriginsPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 44, destinationOriginY: 20,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterDualRegionReplacementWithDifferentNonzeroOriginsMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry with different nonzero origins NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 44, destinationOriginY: 20,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 44, destinationOriginY: 20,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterDualRegionReplacementWithSharedLayerInfoPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 20, destinationOriginY: 14,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterDualRegionReplacementWithSharedLayerInfoMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry with shared LayerInfo NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 20, destinationOriginY: 14,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 20, destinationOriginY: 14,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterDualRegionReplacementWithSharedLayerInfoSourceOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 20, destinationOriginY: 14,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterDualRegionReplacementWithSharedLayerInfoSourceOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry with source atop shared LayerInfo NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 20, destinationOriginY: 14,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 20, destinationOriginY: 14,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAtSharedLayerOcclusionBoundaryDestinationOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 9,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAtSharedLayerOcclusionBoundaryDestinationOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry at shared-layer occlusion boundary NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 9,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 9,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAtSharedLayerOcclusionBoundarySourceOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 9,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAtSharedLayerOcclusionBoundarySourceOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry at shared-layer occlusion boundary NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 9,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 9,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterOnePixelScrollShiftAtSharedOcclusionBoundaryDestinationOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 9,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 0,
                destinationScrollXOverride: -2);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterOnePixelScrollShiftAtSharedOcclusionBoundaryDestinationOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after one-pixel destination scroll shift NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 9,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 0,
                destinationScrollXOverride: -2);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 9,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 0,
                destinationScrollXOverride: -2));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterOnePixelScrollShiftAtSharedOcclusionBoundarySourceOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 9,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterOnePixelScrollShiftAtSharedOcclusionBoundarySourceOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after one-pixel destination scroll shift NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 9,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 9,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterOnePixelVerticalScrollShiftAtSharedOcclusionBoundaryDestinationOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 9,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 0,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterOnePixelVerticalScrollShiftAtSharedOcclusionBoundaryDestinationOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after one-pixel vertical scroll shift NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 9,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 0,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 9,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 0,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterOnePixelVerticalScrollShiftAtSharedOcclusionBoundarySourceOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 9,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 1,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterOnePixelVerticalScrollShiftAtSharedOcclusionBoundarySourceOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after one-pixel vertical scroll shift NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 9,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 1,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 9,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 1,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterOnePixelBothAxesScrollShiftAtSharedOcclusionBoundaryDestinationOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 9,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 0,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterOnePixelBothAxesScrollShiftAtSharedOcclusionBoundaryDestinationOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after one-pixel two-axis scroll shift NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 9,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 0,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 9,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 0,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterOnePixelBothAxesScrollShiftAtSharedOcclusionBoundarySourceOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 9,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterOnePixelBothAxesScrollShiftAtSharedOcclusionBoundarySourceOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after one-pixel two-axis scroll shift NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 9,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 9,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterOnePixelOriginShiftAtSharedOcclusionBoundaryDestinationOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 19, destinationOriginY: 9,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 1,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterOnePixelOriginShiftAtSharedOcclusionBoundaryDestinationOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after one-pixel destination-origin shift NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 19, destinationOriginY: 9,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 1,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 19, destinationOriginY: 9,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 1,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterOnePixelOriginShiftAtSharedOcclusionBoundarySourceOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 19, destinationOriginY: 9,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterOnePixelOriginShiftAtSharedOcclusionBoundarySourceOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after one-pixel destination-origin shift NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 19, destinationOriginY: 9,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 19, destinationOriginY: 9,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterOnePixelVerticalOriginShiftAtSharedOcclusionBoundaryDestinationOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 19, destinationOriginY: 10,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 1,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterOnePixelVerticalOriginShiftAtSharedOcclusionBoundaryDestinationOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after one-pixel vertical destination-origin shift NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 19, destinationOriginY: 10,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 1,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 19, destinationOriginY: 10,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 1,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterOnePixelVerticalOriginShiftAtSharedOcclusionBoundarySourceOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 19, destinationOriginY: 10,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterOnePixelVerticalOriginShiftAtSharedOcclusionBoundarySourceOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after one-pixel vertical destination-origin shift NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 19, destinationOriginY: 10,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 19, destinationOriginY: 10,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterOneFurtherVerticalOriginShiftAtSharedOcclusionBoundaryDestinationOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 19, destinationOriginY: 11,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 1,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterOneFurtherVerticalOriginShiftAtSharedOcclusionBoundaryDestinationOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after one-further-pixel vertical destination-origin shift NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 19, destinationOriginY: 11,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 1,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 19, destinationOriginY: 11,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 1,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterOneFurtherVerticalOriginShiftAtSharedOcclusionBoundarySourceOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 19, destinationOriginY: 11,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterOneFurtherVerticalOriginShiftAtSharedOcclusionBoundarySourceOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after one-further-pixel vertical destination-origin shift NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 19, destinationOriginY: 11,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 19, destinationOriginY: 11,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterOriginMovesPastSourceCandidateRowDestinationOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 19, destinationOriginY: 12,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterOriginMovesPastSourceCandidateRowDestinationOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after destination origin moves past the source candidate row NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 19, destinationOriginY: 12,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 19, destinationOriginY: 12,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterOriginMovesPastSourceCandidateRowSourceOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 19, destinationOriginY: 12,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterOriginMovesPastSourceCandidateRowSourceOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after destination origin moves past the source candidate row NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 19, destinationOriginY: 12,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 19, destinationOriginY: 12,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterOnePixelHorizontalOriginShiftPastSourceCandidateColumnDestinationOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 20, destinationOriginY: 11,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterOnePixelHorizontalOriginShiftPastSourceCandidateColumnDestinationOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after one-pixel horizontal destination-origin shift past the source candidate column NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 20, destinationOriginY: 11,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 20, destinationOriginY: 11,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterOnePixelHorizontalOriginShiftPastSourceCandidateColumnSourceOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 20, destinationOriginY: 11,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterOnePixelHorizontalOriginShiftPastSourceCandidateColumnSourceOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after one-pixel horizontal destination-origin shift past the source candidate column NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 20, destinationOriginY: 11,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 20, destinationOriginY: 11,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterOnePixelHorizontalOriginShiftAcrossSourceCandidateColumnDestinationOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 11,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 0,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterOnePixelHorizontalOriginShiftAcrossSourceCandidateColumnDestinationOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after one-pixel horizontal destination-origin shift across the source candidate column NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 11,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 0,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 11,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 0,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterOnePixelHorizontalOriginShiftAcrossSourceCandidateColumnSourceOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 11,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterOnePixelHorizontalOriginShiftAcrossSourceCandidateColumnSourceOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after one-pixel horizontal destination-origin shift across the source candidate column NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 11,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 11,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterOnePixelSourceOriginShiftAcrossCandidateColumnDestinationOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 3, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 11,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 1,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterOnePixelSourceOriginShiftAcrossCandidateColumnDestinationOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after one-pixel source-origin shift across the candidate column NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 3, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 11,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 1,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 3, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 11,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 1,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterOnePixelSourceOriginShiftAcrossCandidateColumnSourceOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 3, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 11,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterOnePixelSourceOriginShiftAcrossCandidateColumnSourceOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after one-pixel source-origin shift across the candidate column NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 3, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 11,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 3, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 11,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterOneFurtherPixelSourceOriginShiftAcrossCandidateColumnPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 11,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterOneFurtherPixelSourceOriginShiftAcrossCandidateColumnMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after one-further-pixel source-origin shift across the candidate column NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 11,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 11,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterOneFurtherPixelSourceOriginShiftAcrossCandidateColumnSourceOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 11,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterOneFurtherPixelSourceOriginShiftAcrossCandidateColumnSourceOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after one-further-pixel source-origin shift across the candidate column NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 11,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 3,
                destinationOriginX: 18, destinationOriginY: 11,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterDestinationOriginAtSingleSourceCandidateEdgeDestinationOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 3,
                destinationOriginX: 17, destinationOriginY: 11,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 1,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterDestinationOriginAtSingleSourceCandidateEdgeDestinationOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after one-pixel destination-origin shift across the source candidate column NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 3,
                destinationOriginX: 17, destinationOriginY: 11,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 1,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 3,
                destinationOriginX: 17, destinationOriginY: 11,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 1,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterDestinationOriginAtSingleSourceCandidateEdgeSourceOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 3,
                destinationOriginX: 17, destinationOriginY: 11,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterDestinationOriginAtSingleSourceCandidateEdgeSourceOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after one-pixel destination-origin shift across the source candidate column NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 3,
                destinationOriginX: 17, destinationOriginY: 11,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 3,
                destinationOriginX: 17, destinationOriginY: 11,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterDestinationOriginMovesPastBothSourceCandidatesDestinationOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 3,
                destinationOriginX: 16, destinationOriginY: 11,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 0,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterDestinationOriginMovesPastBothSourceCandidatesDestinationOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after one-pixel destination-origin shift across the source candidate column NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 3,
                destinationOriginX: 16, destinationOriginY: 11,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 0,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 3,
                destinationOriginX: 16, destinationOriginY: 11,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 0,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterDestinationOriginMovesPastBothSourceCandidatesSourceOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 3,
                destinationOriginX: 16, destinationOriginY: 11,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterDestinationOriginMovesPastBothSourceCandidatesSourceOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after one-pixel destination-origin shift across the source candidate column NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 3,
                destinationOriginX: 16, destinationOriginY: 11,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 3,
                destinationOriginX: 16, destinationOriginY: 11,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterVerticalOriginMovesPastSourceCandidateRowAtHorizontalOverlapPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 3,
                destinationOriginX: 16, destinationOriginY: 12,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterVerticalOriginMovesPastSourceCandidateRowAtHorizontalOverlapMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after vertical destination-origin shift past the source candidate row at horizontal overlap NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 3,
                destinationOriginX: 16, destinationOriginY: 12,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 3,
                destinationOriginX: 16, destinationOriginY: 12,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterVerticalOriginMovesPastSourceCandidateRowAtHorizontalOverlapSourceOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 3,
                destinationOriginX: 16, destinationOriginY: 12,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterVerticalOriginMovesPastSourceCandidateRowAtHorizontalOverlapSourceOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after vertical destination-origin shift past the source candidate row at horizontal overlap NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 3,
                destinationOriginX: 16, destinationOriginY: 12,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 3,
                destinationOriginX: 16, destinationOriginY: 12,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterSourceOriginMovesAboveDestinationTopEdgeAtHorizontalOverlapPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 2,
                destinationOriginX: 16, destinationOriginY: 11,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterSourceOriginMovesAboveDestinationTopEdgeAtHorizontalOverlapMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after source origin moves above the destination top edge at horizontal overlap NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 2,
                destinationOriginX: 16, destinationOriginY: 11,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 2,
                destinationOriginX: 16, destinationOriginY: 11,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterSourceOriginMovesAboveDestinationTopEdgeAtHorizontalOverlapSourceOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 2,
                destinationOriginX: 16, destinationOriginY: 11,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterSourceOriginMovesAboveDestinationTopEdgeAtHorizontalOverlapSourceOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after source origin moves above the destination top edge at horizontal overlap NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 2,
                destinationOriginX: 16, destinationOriginY: 11,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 2,
                destinationOriginX: 16, destinationOriginY: 11,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterSourceOriginMovesIntoDestinationVerticalOverlapAtHorizontalOverlapPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 11,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 0,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterSourceOriginMovesIntoDestinationVerticalOverlapAtHorizontalOverlapMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after source origin moves into destination vertical overlap at horizontal overlap NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 11,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 0,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 11,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 0,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterSourceOriginMovesIntoDestinationVerticalOverlapAtHorizontalOverlapSourceOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 11,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterSourceOriginMovesIntoDestinationVerticalOverlapAtHorizontalOverlapSourceOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after source origin moves into destination vertical overlap at horizontal overlap NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 11,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 11,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterSourceOriginMovesIntoDeepDestinationVerticalOverlapAtHorizontalOverlapPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 13,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterSourceOriginMovesIntoDeepDestinationVerticalOverlapAtHorizontalOverlapMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after source origin moves into deep destination vertical overlap at horizontal overlap NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 13,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 13,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterSourceOriginMovesIntoDeepDestinationVerticalOverlapAtHorizontalOverlapSourceOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 13,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterSourceOriginMovesIntoDeepDestinationVerticalOverlapAtHorizontalOverlapSourceOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after source origin moves into deep destination vertical overlap at horizontal overlap with source on top NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 13,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 13,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterOnePixelDestinationOriginShiftAtSourceVerticalOverlapPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 12,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 0,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterOnePixelDestinationOriginShiftAtSourceVerticalOverlapMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after one-pixel destination-origin shift at source vertical overlap NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 12,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 0,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 12,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 0,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterOnePixelDestinationOriginShiftAtSourceVerticalOverlapSourceOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 12,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterOnePixelDestinationOriginShiftAtSourceVerticalOverlapSourceOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after one-pixel destination-origin shift at source vertical overlap with source on top NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 12,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 12,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterDestinationOriginMovesBelowSourceCandidatesAtVerticalOverlapPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 14,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterDestinationOriginMovesBelowSourceCandidatesAtVerticalOverlapMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after destination origin moves below source candidates at vertical overlap NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 14,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 14,
                shareLayerInfo: true, disconnectedDamage: true,
                disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopperStartScrolledClipBlitRetryAfterDestinationOriginMovesBelowSourceCandidatesAtVerticalOverlapSourceOnTopPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 14,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrolledClipBlitRetryAfterDestinationOriginMovesBelowSourceCandidatesAtVerticalOverlapSourceOnTopMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after destination origin moves below source candidates at vertical overlap with source on top NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 14,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 14,
                shareLayerInfo: true, sourceLayerOnTop: true,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void CopperStartScrolledClipBlitRetryAfterDestinationOriginMovesOnePixelFartherBelowSourceCandidatesPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 15,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ScrolledClipBlitRetryAfterDestinationOriginMovesOnePixelFartherBelowSourceCandidatesMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry after destination origin moves one pixel farther below source candidates NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 15,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 15,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void CopperStartScrolledClipBlitRetryAtBottomOfSharedVerticalLayerOverlapPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 16,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ScrolledClipBlitRetryAtBottomOfSharedVerticalLayerOverlapMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry at the bottom of the shared vertical layer overlap NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 16,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 16,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void CopperStartScrolledClipBlitRetryNearBottomOfSharedVerticalLayerOverlapPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 17,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ScrolledClipBlitRetryNearBottomOfSharedVerticalLayerOverlapMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry near the bottom of the shared vertical layer overlap NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 17,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 17,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void CopperStartScrolledClipBlitRetryAtNarrowBottomOfSharedVerticalLayerOverlapPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 18,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ScrolledClipBlitRetryAtNarrowBottomOfSharedVerticalLayerOverlapMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry near the bottom edge of the shared vertical layer overlap NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 18,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 18,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void CopperStartScrolledClipBlitRetryAtLastSharedVerticalLayerRowPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 19,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ScrolledClipBlitRetryAtLastSharedVerticalLayerRowMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry at the last shared vertical layer row NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 19,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 4,
                destinationOriginX: 16, destinationOriginY: 19,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void CopperStartScrolledClipBlitRetryWithTranslatedOneRowLayerOverlapPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 5,
                destinationOriginX: 16, destinationOriginY: 20,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ScrolledClipBlitRetryWithTranslatedOneRowLayerOverlapMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry with a translated one-row layer overlap NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 5,
                destinationOriginX: 16, destinationOriginY: 20,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 5,
                destinationOriginX: 16, destinationOriginY: 20,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void CopperStartScrolledClipBlitRetryWithTranslatedOneRowOverlapAtNextOriginPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 6,
                destinationOriginX: 16, destinationOriginY: 21,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ScrolledClipBlitRetryWithTranslatedOneRowOverlapAtNextOriginMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry with translated one-row overlap at the next origin NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 6,
                destinationOriginX: 16, destinationOriginY: 21,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 6,
                destinationOriginX: 16, destinationOriginY: 21,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void CopperStartScrolledClipBlitRetryWithTranslatedOneRowOverlapAtThirdOriginPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 7,
                destinationOriginX: 16, destinationOriginY: 22,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ScrolledClipBlitRetryWithTranslatedOneRowOverlapAtThirdOriginMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry with translated one-row overlap at the third origin NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 7,
                destinationOriginX: 16, destinationOriginY: 22,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 7,
                destinationOriginX: 16, destinationOriginY: 22,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void CopperStartScrolledClipBlitRetryWithTranslatedOneRowOverlapAtFourthOriginPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 8,
                destinationOriginX: 16, destinationOriginY: 23,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ScrolledClipBlitRetryWithTranslatedOneRowOverlapAtFourthOriginMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry with translated one-row overlap at the fourth origin NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 8,
                destinationOriginX: 16, destinationOriginY: 23,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 8,
                destinationOriginX: 16, destinationOriginY: 23,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void CopperStartScrolledClipBlitRetryWithTranslatedOneRowOverlapAtFifthOriginPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 9,
                destinationOriginX: 16, destinationOriginY: 24,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ScrolledClipBlitRetryWithTranslatedOneRowOverlapAtFifthOriginMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry with translated one-row overlap at the fifth origin NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 9,
                destinationOriginX: 16, destinationOriginY: 24,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 9,
                destinationOriginX: 16, destinationOriginY: 24,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void CopperStartScrolledClipBlitRetryWithTranslatedOneRowOverlapAtSixthOriginPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 10,
                destinationOriginX: 16, destinationOriginY: 25,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ScrolledClipBlitRetryWithTranslatedOneRowOverlapAtSixthOriginMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry with translated one-row overlap at the sixth origin NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 10,
                destinationOriginX: 16, destinationOriginY: 25,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 10,
                destinationOriginX: 16, destinationOriginY: 25,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void CopperStartScrolledClipBlitRetryWithTranslatedOneRowOverlapAtSeventhOriginPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 11,
                destinationOriginX: 16, destinationOriginY: 26,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ScrolledClipBlitRetryWithTranslatedOneRowOverlapAtSeventhOriginMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry with translated one-row overlap at the seventh origin NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 11,
                destinationOriginX: 16, destinationOriginY: 26,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 11,
                destinationOriginX: 16, destinationOriginY: 26,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void CopperStartScrolledClipBlitRetryWithTranslatedOneRowOverlapAtEighthOriginPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 12,
                destinationOriginX: 16, destinationOriginY: 27,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ScrolledClipBlitRetryWithTranslatedOneRowOverlapAtEighthOriginMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry with translated one-row overlap at the eighth origin NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 12,
                destinationOriginX: 16, destinationOriginY: 27,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 12,
                destinationOriginX: 16, destinationOriginY: 27,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void CopperStartScrolledClipBlitRetryWithTranslatedOneRowOverlapAtNinthOriginPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 13,
                destinationOriginX: 16, destinationOriginY: 28,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ScrolledClipBlitRetryWithTranslatedOneRowOverlapAtNinthOriginMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry with translated one-row overlap at the ninth origin NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 13,
                destinationOriginX: 16, destinationOriginY: 28,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 13,
                destinationOriginX: 16, destinationOriginY: 28,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void CopperStartScrolledClipBlitRetryWithTranslatedOneRowOverlapAtTenthOriginPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 14,
                destinationOriginX: 16, destinationOriginY: 29,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ScrolledClipBlitRetryWithTranslatedOneRowOverlapAtTenthOriginMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry with translated one-row overlap at the tenth origin NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 14,
                destinationOriginX: 16, destinationOriginY: 29,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 14,
                destinationOriginX: 16, destinationOriginY: 29,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void CopperStartScrolledClipBlitRetryWithTranslatedOneRowOverlapAtEleventhOriginPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 15,
                destinationOriginX: 16, destinationOriginY: 30,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ScrolledClipBlitRetryWithTranslatedOneRowOverlapAtEleventhOriginMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry with translated one-row overlap at the eleventh origin NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 15,
                destinationOriginX: 16, destinationOriginY: 30,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 15,
                destinationOriginX: 16, destinationOriginY: 30,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void CopperStartScrolledClipBlitRetryWithTranslatedOneRowOverlapAtTwelfthOriginPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 16,
                destinationOriginX: 16, destinationOriginY: 31,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ScrolledClipBlitRetryWithTranslatedOneRowOverlapAtTwelfthOriginMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry with translated one-row overlap at the twelfth origin NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 16,
                destinationOriginX: 16, destinationOriginY: 31,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 16,
                destinationOriginX: 16, destinationOriginY: 31,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void CopperStartScrolledClipBlitRetryWithTranslatedOneRowOverlapAtThirteenthOriginPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 17,
                destinationOriginX: 16, destinationOriginY: 32,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ScrolledClipBlitRetryWithTranslatedOneRowOverlapAtThirteenthOriginMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry with translated one-row overlap at the thirteenth origin NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 17,
                destinationOriginX: 16, destinationOriginY: 32,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 17,
                destinationOriginX: 16, destinationOriginY: 32,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void CopperStartScrolledClipBlitRetryWithTranslatedOneRowOverlapAtFourteenthOriginPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 18,
                destinationOriginX: 16, destinationOriginY: 33,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ScrolledClipBlitRetryWithTranslatedOneRowOverlapAtFourteenthOriginMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry with translated one-row overlap at the fourteenth origin NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 18,
                destinationOriginX: 16, destinationOriginY: 33,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 18,
                destinationOriginX: 16, destinationOriginY: 33,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void CopperStartScrolledClipBlitRetryWithTranslatedOneRowOverlapAtFifteenthOriginPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 19,
                destinationOriginX: 16, destinationOriginY: 34,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ScrolledClipBlitRetryWithTranslatedOneRowOverlapAtFifteenthOriginMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry with translated one-row overlap at the fifteenth origin NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 19,
                destinationOriginX: 16, destinationOriginY: 34,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 19,
                destinationOriginX: 16, destinationOriginY: 34,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void CopperStartScrolledClipBlitRetryWithTranslatedOneRowOverlapAtSixteenthOriginPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 20,
                destinationOriginX: 16, destinationOriginY: 35,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ScrolledClipBlitRetryWithTranslatedOneRowOverlapAtSixteenthOriginMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry with translated one-row overlap at the sixteenth origin NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 20,
                destinationOriginX: 16, destinationOriginY: 35,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 20,
                destinationOriginX: 16, destinationOriginY: 35,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void CopperStartScrolledClipBlitRetryWithTranslatedOneRowOverlapAtSeventeenthOriginPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 21,
                destinationOriginX: 16, destinationOriginY: 36,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ScrolledClipBlitRetryWithTranslatedOneRowOverlapAtSeventeenthOriginMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry with translated one-row overlap at the seventeenth origin NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 21,
                destinationOriginX: 16, destinationOriginY: 36,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 21,
                destinationOriginX: 16, destinationOriginY: 36,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void CopperStartScrolledClipBlitRetryWithTranslatedOneRowOverlapAtEighteenthOriginPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 22,
                destinationOriginX: 16, destinationOriginY: 37,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ScrolledClipBlitRetryWithTranslatedOneRowOverlapAtEighteenthOriginMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry with translated one-row overlap at the eighteenth origin NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 22,
                destinationOriginX: 16, destinationOriginY: 37,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 22,
                destinationOriginX: 16, destinationOriginY: 37,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void CopperStartScrolledClipBlitRetryWithTranslatedOneRowOverlapAtNineteenthOriginPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 23,
                destinationOriginX: 16, destinationOriginY: 38,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ScrolledClipBlitRetryWithTranslatedOneRowOverlapAtNineteenthOriginMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry with translated one-row overlap at the nineteenth origin NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 23,
                destinationOriginX: 16, destinationOriginY: 38,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 23,
                destinationOriginX: 16, destinationOriginY: 38,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void CopperStartScrolledClipBlitRetryWithTranslatedOneRowOverlapAtTwentiethOriginPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 24,
                destinationOriginX: 16, destinationOriginY: 39,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ScrolledClipBlitRetryWithTranslatedOneRowOverlapAtTwentiethOriginMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry with translated one-row overlap at the twentieth origin NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 24,
                destinationOriginX: 16, destinationOriginY: 39,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 24,
                destinationOriginX: 16, destinationOriginY: 39,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void CopperStartScrolledClipBlitRetryWithTranslatedOneRowOverlapAtTwentyFirstOriginPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 25,
                destinationOriginX: 16, destinationOriginY: 40,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ScrolledClipBlitRetryWithTranslatedOneRowOverlapAtTwentyFirstOriginMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry with translated one-row overlap at the twenty-first origin NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 25,
                destinationOriginX: 16, destinationOriginY: 40,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 25,
                destinationOriginX: 16, destinationOriginY: 40,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void CopperStartScrolledClipBlitRetryWithTranslatedOneRowOverlapAtTwentySecondOriginPreservesBothOwners(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 26,
                destinationOriginX: 16, destinationOriginY: 41,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ScrolledClipBlitRetryWithTranslatedOneRowOverlapAtTwentySecondOriginMatchesNativeV4063(
        bool poisonUnusedRegisterBits,
        bool sourceLayerOnTop,
        bool sourceCompletesBeforeDestination)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit retry with translated one-row overlap at the twenty-second origin NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            expected = TraceClipBlitDuringDualDamageRefresh(native,
                poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 26,
                destinationOriginX: 16, destinationOriginY: 41,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(
                copperStart, poisonUnusedRegisterBits, applyScroll: true,
                applyCallerClipRegions: true,
                sourceOriginX: 2, sourceOriginY: 26,
                destinationOriginX: 16, destinationOriginY: 41,
                shareLayerInfo: true, sourceLayerOnTop: sourceLayerOnTop,
                disconnectedDamage: true, disjointCallerClipRegions: true,
                replaceCallerRegionsDuringUpdate: true,
                incompleteEndUpdate: true,
                retryAfterIncompleteEndUpdate: true,
                sourceCompletesBeforeDestination:
                    sourceCompletesBeforeDestination,
                expectedTransferredPixelsOverride: 2,
                destinationScrollXOverride: -2,
                destinationScrollYOverride: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopperStartScrolledClipBlitDuringDualDamageRefreshPreservesBothOwners(bool poisonUnusedRegisterBits)
    {
        var context = CreateCopperStartOracle();
        try { _ = TraceClipBlitDuringDualDamageRefresh(context, poisonUnusedRegisterBits, applyScroll: true); }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScrolledClipBlitDuringDualDamageRefreshMatchesNativeV4063(bool poisonUnusedRegisterBits)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled dual-damage ClipBlit NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            foreach (var lvo in new[] { GraphicsLvo.ClipBlit, GraphicsLvo.BltBitMap })
            {
                var vector = unchecked((uint)((int)native.GraphicsBase + (int)lvo));
                Assert.False(native.Bus.HasHostGateway(vector));
                Assert.Equal((ushort)0x4EF9, native.Bus.ReadWord(vector));
                Assert.InRange(native.Bus.ReadLong(vector + 2), 0x00F8_0000u, 0x00FF_FFFFu);
            }
            var memory = new LayersTestGuestMemory(native.Bus);
            _output.WriteLine("G238 configured V40.63: A500 PAL OCS, 512 KiB CHIP, " +
                "AccurateM68000/live Agnus; native graphics vectors uninstrumented. " +
                $"layers={ReadOracleLibraryVersion(native.Bus, native.LayersBase)}." +
                $"{LayersLibraryCodec.ReadRevision(ref memory, APTR.FromPointer(native.LayersBase))}; " +
                $"graphics={ReadOracleLibraryVersion(native.Bus, native.GraphicsBase)}." +
                $"{LayersLibraryCodec.ReadRevision(ref memory, APTR.FromPointer(native.GraphicsBase))}");
            expected = TraceClipBlitDuringDualDamageRefresh(native, poisonUnusedRegisterBits, applyScroll: true);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try { Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(copperStart, poisonUnusedRegisterBits, applyScroll: true)); }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopperStartScrolledClipBlitWithCallerRegionsDuringDualDamageRefreshPreservesBothOwners(
        bool poisonUnusedRegisterBits)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context, poisonUnusedRegisterBits,
                applyScroll: true, applyCallerClipRegions: true);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScrolledClipBlitWithCallerRegionsDuringDualDamageRefreshMatchesNativeV4063(
        bool poisonUnusedRegisterBits)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled ClipBlit with caller regions NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            foreach (var lvo in new[] { GraphicsLvo.ClipBlit, GraphicsLvo.BltBitMap })
            {
                var vector = unchecked((uint)((int)native.GraphicsBase + (int)lvo));
                Assert.False(native.Bus.HasHostGateway(vector));
                Assert.Equal((ushort)0x4EF9, native.Bus.ReadWord(vector));
                Assert.InRange(native.Bus.ReadLong(vector + 2), 0x00F8_0000u, 0x00FF_FFFFu);
            }
            var memory = new LayersTestGuestMemory(native.Bus);
            _output.WriteLine("G239 configured V40.63: A500 PAL OCS, 512 KiB CHIP, " +
                "AccurateM68000/live Agnus; native graphics vectors uninstrumented. " +
                $"layers={ReadOracleLibraryVersion(native.Bus, native.LayersBase)}." +
                $"{LayersLibraryCodec.ReadRevision(ref memory, APTR.FromPointer(native.LayersBase))}; " +
                $"graphics={ReadOracleLibraryVersion(native.Bus, native.GraphicsBase)}." +
                $"{LayersLibraryCodec.ReadRevision(ref memory, APTR.FromPointer(native.GraphicsBase))}");
            expected = TraceClipBlitDuringDualDamageRefresh(native, poisonUnusedRegisterBits,
                applyScroll: true, applyCallerClipRegions: true);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(copperStart,
                poisonUnusedRegisterBits, applyScroll: true, applyCallerClipRegions: true));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopperStartScrolledClipBlitWithCallerRegionsAndNonzeroLayerOriginPreservesBothOwners(
        bool poisonUnusedRegisterBits)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context, poisonUnusedRegisterBits,
                applyScroll: true, applyCallerClipRegions: true,
                sourceOriginX: 1, sourceOriginY: 1,
                destinationOriginX: 1, destinationOriginY: 1);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScrolledClipBlitWithCallerRegionsAndNonzeroLayerOriginMatchesNativeV4063(
        bool poisonUnusedRegisterBits)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native nonzero-origin scrolled ClipBlit NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            foreach (var lvo in new[] { GraphicsLvo.ClipBlit, GraphicsLvo.BltBitMap })
            {
                var vector = unchecked((uint)((int)native.GraphicsBase + (int)lvo));
                Assert.False(native.Bus.HasHostGateway(vector));
                Assert.Equal((ushort)0x4EF9, native.Bus.ReadWord(vector));
                Assert.InRange(native.Bus.ReadLong(vector + 2), 0x00F8_0000u, 0x00FF_FFFFu);
            }
            _output.WriteLine("G241 configured V40.63: scrolled dual-damage ClipBlit with " +
                "caller Regions and shared layer origin (1,1); native graphics vectors uninstrumented.");
            expected = TraceClipBlitDuringDualDamageRefresh(native, poisonUnusedRegisterBits,
                applyScroll: true, applyCallerClipRegions: true,
                sourceOriginX: 1, sourceOriginY: 1,
                destinationOriginX: 1, destinationOriginY: 1);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(copperStart,
                poisonUnusedRegisterBits, applyScroll: true, applyCallerClipRegions: true,
                sourceOriginX: 1, sourceOriginY: 1,
                destinationOriginX: 1, destinationOriginY: 1));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopperStartScrolledClipBlitWithDifferentNonzeroOriginsDuringDualDamageRefreshPreservesBothOwners(
        bool poisonUnusedRegisterBits)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context, poisonUnusedRegisterBits,
                applyScroll: true, applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 44, destinationOriginY: 20);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScrolledClipBlitWithDifferentNonzeroOriginsDuringDualDamageRefreshMatchesNativeV4063(
        bool poisonUnusedRegisterBits)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native differently-originated scrolled ClipBlit NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            foreach (var lvo in new[] { GraphicsLvo.ClipBlit, GraphicsLvo.BltBitMap })
            {
                var vector = unchecked((uint)((int)native.GraphicsBase + (int)lvo));
                Assert.False(native.Bus.HasHostGateway(vector));
                Assert.Equal((ushort)0x4EF9, native.Bus.ReadWord(vector));
                Assert.InRange(native.Bus.ReadLong(vector + 2), 0x00F8_0000u, 0x00FF_FFFFu);
            }
            _output.WriteLine("G242 configured V40.63: source bounds (4,3)..(35,18), " +
                "destination bounds (44,20)..(75,35); uninstrumented native vectors.");
            expected = TraceClipBlitDuringDualDamageRefresh(native, poisonUnusedRegisterBits,
                applyScroll: true, applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 44, destinationOriginY: 20);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(copperStart,
                poisonUnusedRegisterBits, applyScroll: true, applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 44, destinationOriginY: 20));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopperStartScrolledClipBlitWithPartialOcclusionDuringDualDamageRefreshPreservesBothOwners(
        bool poisonUnusedRegisterBits)
    {
        var context = CreateCopperStartOracle();
        try
        {
            _ = TraceClipBlitDuringDualDamageRefresh(context, poisonUnusedRegisterBits,
                applyScroll: true, applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 14, destinationOriginY: 8,
                shareLayerInfo: true);
        }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScrolledClipBlitWithPartialOcclusionDuringDualDamageRefreshMatchesNativeV4063(
        bool poisonUnusedRegisterBits)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native partially occluded scrolled ClipBlit NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            foreach (var lvo in new[] { GraphicsLvo.ClipBlit, GraphicsLvo.BltBitMap })
            {
                var vector = unchecked((uint)((int)native.GraphicsBase + (int)lvo));
                Assert.False(native.Bus.HasHostGateway(vector));
                Assert.Equal((ushort)0x4EF9, native.Bus.ReadWord(vector));
                Assert.InRange(native.Bus.ReadLong(vector + 2), 0x00F8_0000u, 0x00FF_FFFFu);
            }
            _output.WriteLine("G243 configured V40.63: source (4,3)..(35,18), destination " +
                "(14,8)..(45,23) in one Simple-layer stack; destination partially occludes source.");
            expected = TraceClipBlitDuringDualDamageRefresh(native, poisonUnusedRegisterBits,
                applyScroll: true, applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 14, destinationOriginY: 8,
                shareLayerInfo: true);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceClipBlitDuringDualDamageRefresh(copperStart,
                poisonUnusedRegisterBits, applyScroll: true, applyCallerClipRegions: true,
                sourceOriginX: 4, sourceOriginY: 3,
                destinationOriginX: 14, destinationOriginY: 8,
                shareLayerInfo: true));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    [Fact]
    public void ScrolledSimpleLayerCallerClipRegionAtNonzeroOriginMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native scrolled Simple-layer ClipRegion NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            _output.WriteLine("G240 configured V40.63: Simple layer origin/scroll ClipRegion " +
                "projection; native graphics vectors uninstrumented.");
            expected = TraceScrolledSimpleLayerCallerClipRegion(native);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceScrolledSimpleLayerCallerClipRegion(copperStart));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    private string[] TraceScrolledSimpleLayerCallerClipRegion(OracleContext context)
    {
        const int width = 32;
        const int height = 16;
        const short layerMinX = 5;
        const short layerMinY = 3;
        const short layerMaxX = 24;
        const short layerMaxY = 12;
        const short scrollX = 2;
        const short scrollY = 1;
        var trace = new List<string>();
        var memory = new LayersTestGuestMemory(context.Bus);
        var info = context.NewLayerInfo();
        var bitmap = context.CreatePlanarBitMap(width, height, 1, 0,
            planeMemoryFlags: Exec.MemoryFlags.Chip);
        var layer = context.CreateLayer(LayersLvo.CreateUpfrontLayer, info,
            bitmap.Address, 0, LayerCreationFlags.Simple, layerMinX, layerMinY,
            layerMaxX, layerMaxY);
        context.InvokeLayers(LayersLvo.ScrollLayer, state =>
        {
            state.A[1] = layer;
            state.D[0] = unchecked((uint)(int)scrollX);
            state.D[1] = unchecked((uint)(int)scrollY);
        });
        context.WaitForBlitterIdle();
        Assert.False(context.Bus.Blitter.Busy);
        Assert.Equal(scrollX, LayersLayerCodec.ReadScrollX(ref memory,
            APTR.FromPointer(layer)));
        Assert.Equal(scrollY, LayersLayerCodec.ReadScrollY(ref memory,
            APTR.FromPointer(layer)));

        var region = context.CreateSingleRectangleRegion(2, 1, 7, 5);
        var regionNode = ReadOracleRegionFirst(context.Bus, region);
        var regionBytes = Bytes(region, checked((int)Region.Size));
        var regionNodeBytes = Bytes(regionNode, checked((int)RegionRectangle.Size));
        Assert.Equal(0u, context.InvokeLayers(LayersLvo.InstallClipRegion, state =>
        {
            state.A[0] = layer;
            state.A[1] = region;
        }).D[0]);

        var expectedCallerCoverage = RectangleCoverage(7, 4, 12, 8);
        var expectedFullCoverage = RectangleCoverage(layerMinX, layerMinY,
            layerMaxX, layerMaxY);
        Assert.Equal(expectedCallerCoverage, VisibleCoverage(layer));
        Assert.Equal(scrollX, LayersLayerCodec.ReadScrollX(ref memory,
            APTR.FromPointer(layer)));
        Assert.Equal(scrollY, LayersLayerCodec.ReadScrollY(ref memory,
            APTR.FromPointer(layer)));
        Assert.Equal(regionBytes, Bytes(region, regionBytes.Length));
        Assert.Equal(regionNodeBytes, Bytes(regionNode, regionNodeBytes.Length));

        var callerCoverage = VisibleCoverage(layer);
        Assert.Equal(region, context.InvokeLayers(LayersLvo.InstallClipRegion, state =>
        {
            state.A[0] = layer;
            state.A[1] = 0;
        }).D[0]);
        var fullCoverage = VisibleCoverage(layer);
        Assert.Equal(expectedFullCoverage, fullCoverage);
        Assert.Equal(regionBytes, Bytes(region, regionBytes.Length));
        Assert.Equal(regionNodeBytes, Bytes(regionNode, regionNodeBytes.Length));
        trace.Add("simple-clipregion-scroll:" +
            $"caller=[{string.Join(',', callerCoverage)}]:" +
            $"full=[{string.Join(',', fullCoverage)}]:" +
            $"scroll={LayersLayerCodec.ReadScrollX(ref memory, APTR.FromPointer(layer))}," +
            $"{LayersLayerCodec.ReadScrollY(ref memory, APTR.FromPointer(layer))}");

        Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = layer).D[0]);
        context.DisposeLayerInfo(info);
        context.FreePlanarBitMap(bitmap);
        context.Free(regionNode, RegionRectangle.Size);
        context.Free(region, Region.Size);
        return trace.ToArray();

        int[] VisibleCoverage(uint layerAddress)
        {
            var pixels = new SortedSet<int>();
            var seen = new HashSet<uint>();
            for (var node = ReadOracleLayerPointer(context.Bus, layerAddress,
                     OracleLayerPointer.ClipRect); node != 0;
                 node = ReadOracleClipRectPointer(context.Bus, node,
                     OracleClipRectPointer.Next))
            {
                Assert.True(seen.Count < 256 && seen.Add(node));
                if (ReadOracleClipRectPointer(context.Bus, node,
                        OracleClipRectPointer.ObscuringLayer) != 0) continue;
                var bounds = ReadOracleClipRectBounds(context.Bus, node);
                Assert.InRange((int)bounds.MinX, 0, width - 1);
                Assert.InRange((int)bounds.MaxX, bounds.MinX, width - 1);
                Assert.InRange((int)bounds.MinY, 0, height - 1);
                Assert.InRange((int)bounds.MaxY, bounds.MinY, height - 1);
                foreach (var pixel in RectangleCoverage(bounds.MinX, bounds.MinY,
                             bounds.MaxX, bounds.MaxY))
                    Assert.True(pixels.Add(pixel), "Visible ClipRects must not overlap.");
            }
            return pixels.ToArray();
        }

        int[] RectangleCoverage(int minX, int minY, int maxX, int maxY) =>
            Enumerable.Range(minY, maxY - minY + 1).SelectMany(y =>
                Enumerable.Range(minX, maxX - minX + 1).Select(x => y * width + x)).ToArray();

        byte[] Bytes(uint address, int count) => Enumerable.Range(0, count)
            .Select(offset => context.Bus.ReadByte(address + checked((uint)offset))).ToArray();
    }

    private string[] TraceClipBlitDuringDualDamageRefresh(
        OracleContext context,
        bool poisonUnusedRegisterBits,
        bool applyScroll = false,
        bool? applyCallerClipRegions = null,
        int sourceOriginX = 0,
        int sourceOriginY = 0,
        int destinationOriginX = 0,
        int destinationOriginY = 0,
        bool shareLayerInfo = false,
        bool sourceLayerOnTop = false,
        bool disconnectedDamage = false,
        bool disjointCallerClipRegions = false,
        bool replaceCallerRegionsDuringUpdate = false,
        bool incompleteEndUpdate = false,
        bool retryAfterIncompleteEndUpdate = false,
        bool sourceCompletesBeforeDestination = false,
        int? expectedTransferredPixelsOverride = null,
        short? sourceScrollXOverride = null,
        short? sourceScrollYOverride = null,
        short? destinationScrollXOverride = null,
        short? destinationScrollYOverride = null)
    {
        const int width = 32;
        const int height = 16;
        Assert.InRange(sourceOriginX, 0, short.MaxValue - width);
        Assert.InRange(sourceOriginY, 0, short.MaxValue - height);
        Assert.InRange(destinationOriginX, 0, short.MaxValue - width);
        Assert.InRange(destinationOriginY, 0, short.MaxValue - height);
        var trace = new List<string>();
        var memory = new LayersTestGuestMemory(context.Bus);
        var useCallerClipRegions = applyCallerClipRegions ?? !applyScroll;
        Assert.False(disjointCallerClipRegions && !useCallerClipRegions,
            "Disjoint caller Regions require caller clipping to be enabled.");
        Assert.False(replaceCallerRegionsDuringUpdate &&
            (!disjointCallerClipRegions || !useCallerClipRegions ||
                !disconnectedDamage),
            "Active Region replacement requires disjoint caller Regions and disconnected damage at both endpoints.");
        Assert.False(incompleteEndUpdate && !replaceCallerRegionsDuringUpdate,
            "The incomplete-update variant requires active caller-Region replacement.");
        Assert.False(retryAfterIncompleteEndUpdate && !incompleteEndUpdate,
            "A retry requires a preceding incomplete EndUpdate.");
        Assert.False(sourceCompletesBeforeDestination &&
            !retryAfterIncompleteEndUpdate,
            "Source-first completion applies to the retry pass.");
        Assert.False(shareLayerInfo && (!applyScroll || !useCallerClipRegions),
            "The shared-stack occlusion case requires scrolling and caller ClipRegions.");
        Assert.False(sourceLayerOnTop && !shareLayerInfo,
            "The source-on-top variant requires a shared LayerInfo.");
        if (shareLayerInfo)
        {
            Assert.True(sourceOriginX < destinationOriginX + width &&
                destinationOriginX < sourceOriginX + width &&
                sourceOriginY < destinationOriginY + height &&
                destinationOriginY < sourceOriginY + height,
                "G243 must exercise a partial source/destination layer overlap.");
        }
        var sharedInfo = shareLayerInfo ? context.NewLayerInfo() : 0u;
        var sourceFirst = !sourceLayerOnTop;
        var first = sourceFirst
            ? CreateEndpoint(4, 2, 15, 10, sourceOriginX, sourceOriginY,
                sharedInfo, disconnectedDamage)
            : CreateEndpoint(10, 5, 21, 13, destinationOriginX,
                destinationOriginY, sharedInfo, disconnectedDamage);
        var second = sourceFirst
            ? CreateEndpoint(10, 5, 21, 13, destinationOriginX,
                destinationOriginY, sharedInfo, disconnectedDamage)
            : CreateEndpoint(4, 2, 15, 10, sourceOriginX, sourceOriginY,
                sharedInfo, disconnectedDamage);
        var source = sourceFirst ? first : second;
        var destination = sourceFirst ? second : first;
        var endpoints = new[] { source, destination };
        var fullCoverage = Enumerable.Range(0, width * height).ToArray();
        var sourceScrollX = applyScroll ? sourceScrollXOverride ?? (short)2 : (short)0;
        var sourceScrollY = applyScroll ? sourceScrollYOverride ?? (short)1 : (short)0;
        var destinationScrollX = applyScroll
            ? destinationScrollXOverride ?? (short)-1
            : (short)0;
        var destinationScrollY = applyScroll
            ? destinationScrollYOverride ?? (short)2
            : (short)0;
        if (applyScroll && useCallerClipRegions)
        {
            Scroll(source.Layer, sourceScrollX, sourceScrollY);
            Scroll(destination.Layer, destinationScrollX, destinationScrollY);
            context.WaitForBlitterIdle();
            Assert.False(context.Bus.Blitter.Busy);
        }
        else if (applyScroll)
        {
            LayersLayerCodec.WriteScroll(
                ref memory,
                APTR.FromPointer(source.Layer),
                sourceScrollX,
                sourceScrollY);
            LayersLayerCodec.WriteScroll(
                ref memory,
                APTR.FromPointer(destination.Layer),
                destinationScrollX,
                destinationScrollY);
        }

        uint sourceClipRegion = 0;
        uint[] sourceClipNodes = Array.Empty<uint>();
        byte[] sourceClipBytes = Array.Empty<byte>();
        byte[][] sourceClipNodeBytes = Array.Empty<byte[]>();
        uint destinationClipRegion = 0;
        uint[] destinationClipNodes = Array.Empty<uint>();
        byte[] destinationClipBytes = Array.Empty<byte>();
        byte[][] destinationClipNodeBytes = Array.Empty<byte[]>();
        (int MinX, int MinY, int MaxX, int MaxY)[] sourceCallerRectangles =
            disjointCallerClipRegions
                ? [(7, 4, 9, 6), (12, 8, 14, 9)]
                : [(8, 4, 13, 8)];
        (int MinX, int MinY, int MaxX, int MaxY)[] destinationCallerRectangles =
            disjointCallerClipRegions
                ? [(11, 7, 13, 9), (18, 11, 20, 12)]
                : [(9, 6, 12, 11)];
        if (useCallerClipRegions)
        {
            sourceClipRegion = disjointCallerClipRegions
                ? CreateRectangleRegion(sourceCallerRectangles, out sourceClipNodes)
                : context.CreateSingleRectangleRegion(8, 4, 13, 8);
            destinationClipRegion = disjointCallerClipRegions
                ? CreateRectangleRegion(destinationCallerRectangles,
                    out destinationClipNodes)
                : context.CreateSingleRectangleRegion(9, 6, 12, 11);
            if (!disjointCallerClipRegions)
            {
                sourceClipNodes = [ReadOracleRegionFirst(context.Bus,
                    sourceClipRegion)];
                destinationClipNodes = [ReadOracleRegionFirst(context.Bus,
                    destinationClipRegion)];
            }
            sourceClipBytes = Bytes(sourceClipRegion, checked((int)Region.Size));
            sourceClipNodeBytes = sourceClipNodes.Select(node =>
                Bytes(node, checked((int)RegionRectangle.Size))).ToArray();
            destinationClipBytes = Bytes(destinationClipRegion, checked((int)Region.Size));
            destinationClipNodeBytes = destinationClipNodes.Select(node =>
                Bytes(node, checked((int)RegionRectangle.Size))).ToArray();
            Assert.Equal(0u, context.InvokeLayers(LayersLvo.InstallClipRegion, state =>
            {
                state.A[0] = source.Layer;
                state.A[1] = sourceClipRegion;
            }).D[0]);
            Assert.Equal(0u, context.InvokeLayers(LayersLvo.InstallClipRegion, state =>
            {
                state.A[0] = destination.Layer;
                state.A[1] = destinationClipRegion;
            }).D[0]);
        }

        var sourceClipAllocations = useCallerClipRegions
            ? new List<(uint Region, uint[] Nodes)> { (sourceClipRegion, sourceClipNodes) }
            : new List<(uint Region, uint[] Nodes)>();
        var destinationClipAllocations = useCallerClipRegions
            ? new List<(uint Region, uint[] Nodes)>
                { (destinationClipRegion, destinationClipNodes) }
            : new List<(uint Region, uint[] Nodes)>();
        var initialSourceClipRegion = sourceClipRegion;
        var initialSourceClipNodes = sourceClipNodes;
        var initialSourceClipBytes = sourceClipBytes;
        var initialSourceClipNodeBytes = sourceClipNodeBytes;
        var initialDestinationClipRegion = destinationClipRegion;
        var initialDestinationClipNodes = destinationClipNodes;
        var initialDestinationClipBytes = destinationClipBytes;
        var initialDestinationClipNodeBytes = destinationClipNodeBytes;

        var sourceCallerCoverage = useCallerClipRegions
            ? sourceCallerRectangles.SelectMany(rectangle =>
                    RectangleCoverage(rectangle.MinX, rectangle.MinY,
                        rectangle.MaxX, rectangle.MaxY))
                .Distinct().OrderBy(pixel => pixel).ToArray()
            : fullCoverage;
        var destinationCallerCoverage = useCallerClipRegions
            ? destinationCallerRectangles.SelectMany(rectangle =>
                    RectangleCoverage(rectangle.MinX, rectangle.MinY,
                        rectangle.MaxX, rectangle.MaxY))
                .Distinct().OrderBy(pixel => pixel).ToArray()
            : fullCoverage;
        var sourceStableCoverage = sourceCallerCoverage;
        sourceStableCoverage = sourceLayerOnTop
            ? sourceStableCoverage
            : RemoveDestinationOcclusion(sourceStableCoverage);
        var destinationStableCoverage = sourceLayerOnTop
            ? RemoveSourceOcclusion(destinationCallerCoverage)
            : destinationCallerCoverage;
        var sourceActiveCoverage = useCallerClipRegions
            ? sourceCallerCoverage
                .Intersect(source.DamageCoverage).ToArray()
            : source.DamageCoverage;
        sourceActiveCoverage = sourceLayerOnTop
            ? sourceActiveCoverage
            : RemoveDestinationOcclusion(sourceActiveCoverage);
        var destinationActiveCoverage = useCallerClipRegions
            ? destinationCallerCoverage
                .Intersect(destination.DamageCoverage).ToArray()
            : destination.DamageCoverage;
        if (sourceLayerOnTop)
            destinationActiveCoverage = RemoveSourceOcclusion(
                destinationActiveCoverage);
        if (shareLayerInfo) Capture("caller-regions-installed");
        Assert.Equal(sourceStableCoverage, VisibleCoverage(source.Layer,
            sourceOriginX, sourceOriginY));
        Assert.Equal(destinationStableCoverage, VisibleCoverage(destination.Layer,
            destinationOriginX, destinationOriginY));

        Assert.NotEmpty(source.DamageCoverage.Except(destination.DamageCoverage));
        Assert.NotEmpty(destination.DamageCoverage.Except(source.DamageCoverage));

        var sourcePixels = MakePixels(source.Image, sourceOriginX, sourceOriginY,
            (x, y) => (x + 2 * y) % 3 == 0);
        if (destinationScrollYOverride == 1)
        {
            // Mark the one-pixel vertical-boundary source candidate so that
            // admitting or suppressing it is observable in the planar output.
            SetPixel(sourcePixels, source.Image.BytesPerRow,
                sourceOriginX + 15, sourceOriginY + 8, true);
        }
        var destinationPixels = MakePixels(destination.Image,
            destinationOriginX, destinationOriginY, (x, y) => applyScroll
            ? (x + 2 * y) % 3 == 0
            : (x + 2 * y) % 3 != 0);
        var expectedPixels = (byte[])destinationPixels.Clone();
        var sourceX = applyScroll ? (short)2 : (short)0;
        var sourceY = applyScroll ? (short)1 : (short)0;
        var destinationX = applyScroll ? (short)1 : (short)0;
        var destinationY = applyScroll ? (short)3 : (short)0;
        var transferWidth = applyScroll ? (short)26 : (short)width;
        var transferHeight = applyScroll ? (short)14 : (short)height;
        var sourceScrollXValue = applyScroll ? sourceScrollX : (short)0;
        var sourceScrollYValue = applyScroll ? sourceScrollY : (short)0;
        var destinationScrollXValue = applyScroll ? destinationScrollX : (short)0;
        var destinationScrollYValue = applyScroll ? destinationScrollY : (short)0;
        WriteBytes(source.Image.Planes[0], sourcePixels);
        WriteBytes(destination.Image.Planes[0], destinationPixels);
        Capture("before-update");

        Begin(source.Layer, sourceActiveCoverage, sourceOriginX, sourceOriginY);
        Capture("source-begin");
        Begin(destination.Layer, destinationActiveCoverage,
            destinationOriginX, destinationOriginY);
        Capture("destination-begin");
        if (replaceCallerRegionsDuringUpdate)
        {
            var replacementSourceRectangles = applyScroll
                ? new[]
                {
                    (MinX: 6, MinY: 3, MaxX: 8, MaxY: 5),
                    (MinX: 13, MinY: 8, MaxX: 15, MaxY: 9)
                }
                : new[]
                {
                    (MinX: 6, MinY: 3, MaxX: 8, MaxY: 5),
                    (MinX: 12, MinY: 9, MaxX: 14, MaxY: 10)
                };
            var replacementDestinationRectangles = applyScroll
                ? new[]
                {
                    (MinX: 13, MinY: 9, MaxX: 15, MaxY: 9),
                    (MinX: 17, MinY: 10, MaxX: 18, MaxY: 10)
                }
                : new[]
                {
                    (MinX: 12, MinY: 8, MaxX: 14, MaxY: 9),
                    (MinX: 19, MinY: 11, MaxX: 21, MaxY: 12)
                };
            var replacementSourceRegion = CreateRectangleRegion(
                replacementSourceRectangles, out var replacementSourceNodes);
            var replacementDestinationRegion = CreateRectangleRegion(
                replacementDestinationRectangles,
                out var replacementDestinationNodes);
            Assert.Equal(sourceClipRegion,
                context.InvokeLayers(LayersLvo.InstallClipRegion, state =>
                {
                    state.A[0] = source.Layer;
                    state.A[1] = replacementSourceRegion;
                }).D[0]);
            Assert.Equal(destinationClipRegion,
                context.InvokeLayers(LayersLvo.InstallClipRegion, state =>
                {
                    state.A[0] = destination.Layer;
                    state.A[1] = replacementDestinationRegion;
                }).D[0]);
            sourceClipRegion = replacementSourceRegion;
            sourceClipNodes = replacementSourceNodes;
            sourceClipAllocations.Add((sourceClipRegion, sourceClipNodes));
            sourceClipBytes = Bytes(sourceClipRegion, checked((int)Region.Size));
            sourceClipNodeBytes = sourceClipNodes.Select(node =>
                Bytes(node, checked((int)RegionRectangle.Size))).ToArray();
            destinationClipRegion = replacementDestinationRegion;
            destinationClipNodes = replacementDestinationNodes;
            destinationClipAllocations.Add((destinationClipRegion,
                destinationClipNodes));
            destinationClipBytes = Bytes(destinationClipRegion,
                checked((int)Region.Size));
            destinationClipNodeBytes = destinationClipNodes.Select(node =>
                Bytes(node, checked((int)RegionRectangle.Size))).ToArray();
            sourceCallerRectangles = replacementSourceRectangles;
            destinationCallerRectangles = replacementDestinationRectangles;
            sourceCallerCoverage = sourceCallerRectangles.SelectMany(rectangle =>
                    RectangleCoverage(rectangle.MinX, rectangle.MinY,
                        rectangle.MaxX, rectangle.MaxY))
                .Distinct().OrderBy(pixel => pixel).ToArray();
            destinationCallerCoverage = destinationCallerRectangles.SelectMany(rectangle =>
                    RectangleCoverage(rectangle.MinX, rectangle.MinY,
                        rectangle.MaxX, rectangle.MaxY))
                .Distinct().OrderBy(pixel => pixel).ToArray();
            sourceStableCoverage = sourceLayerOnTop
                ? sourceCallerCoverage
                : RemoveDestinationOcclusion(sourceCallerCoverage);
            destinationStableCoverage = sourceLayerOnTop
                ? RemoveSourceOcclusion(destinationCallerCoverage)
                : destinationCallerCoverage;
            sourceActiveCoverage = sourceCallerCoverage
                .Intersect(source.DamageCoverage).ToArray();
            sourceActiveCoverage = sourceLayerOnTop
                ? sourceActiveCoverage
                : RemoveDestinationOcclusion(sourceActiveCoverage);
            destinationActiveCoverage = destinationCallerCoverage
                .Intersect(destination.DamageCoverage).ToArray();
            if (sourceLayerOnTop)
                destinationActiveCoverage = RemoveSourceOcclusion(
                    destinationActiveCoverage);
            AssertClipRegionUnchanged(initialSourceClipRegion,
                initialSourceClipBytes, initialSourceClipNodes,
                initialSourceClipNodeBytes);
            AssertClipRegionUnchanged(initialDestinationClipRegion,
                initialDestinationClipBytes, initialDestinationClipNodes,
                initialDestinationClipNodeBytes);
            Assert.Equal(sourceActiveCoverage, VisibleCoverage(source.Layer,
                sourceOriginX, sourceOriginY));
            Assert.Equal(destinationActiveCoverage,
                VisibleCoverage(destination.Layer, destinationOriginX,
                    destinationOriginY));
            Capture("caller-regions-replaced-during-update");
        }

        var sourceActiveSet = sourceActiveCoverage.ToHashSet();
        var destinationActiveSet = destinationActiveCoverage.ToHashSet();
        var setChanges = 0;
        var clearChanges = 0;
        var transferredPixels = 0;
        var sourceDamageBypassWouldChangePixels = false;
        var destinationDamageBypassWouldChangePixels = false;
        // Independent logical-to-screen oracle. ClipRects and caller regions
        // are admitted by their expected coordinate coverage; neither
        // implementation's output contributes to the expected destination.
        for (var destinationScreenY = 0; destinationScreenY < height; destinationScreenY++)
        for (var destinationScreenX = 0; destinationScreenX < width; destinationScreenX++)
        {
            var destinationLogicalX = destinationScreenX + destinationScrollXValue;
            var destinationLogicalY = destinationScreenY + destinationScrollYValue;
            var destinationOffsetX = destinationLogicalX - destinationX;
            var destinationOffsetY = destinationLogicalY - destinationY;
            if (destinationOffsetX < 0 || destinationOffsetX >= transferWidth ||
                destinationOffsetY < 0 || destinationOffsetY >= transferHeight)
            {
                continue;
            }

            var sourceLogicalX = sourceX + destinationOffsetX;
            var sourceLogicalY = sourceY + destinationOffsetY;
            var sourceScreenX = sourceLogicalX - sourceScrollXValue;
            var sourceScreenY = sourceLogicalY - sourceScrollYValue;
            if (sourceScreenX < 0 || sourceScreenX >= width ||
                sourceScreenY < 0 || sourceScreenY >= height)
            {
                continue;
            }

            var destinationPixel = destinationScreenY * width + destinationScreenX;
            var sourcePixel = sourceScreenY * width + sourceScreenX;
            var destinationBitmapX = destinationOriginX + destinationScreenX;
            var destinationBitmapY = destinationOriginY + destinationScreenY;
            var sourceBitmapX = sourceOriginX + sourceScreenX;
            var sourceBitmapY = sourceOriginY + sourceScreenY;
            var sourceIsActive = sourceActiveSet.Contains(sourcePixel);
            var destinationIsActive = destinationActiveSet.Contains(destinationPixel);
            var sourceSet = PixelIsSet(sourcePixels, source.Image.BytesPerRow,
                sourceBitmapX, sourceBitmapY);
            var previousSet = PixelIsSet(destinationPixels, destination.Image.BytesPerRow,
                destinationBitmapX, destinationBitmapY);
            if (destinationIsActive && !sourceIsActive && sourceSet != previousSet)
                sourceDamageBypassWouldChangePixels = true;
            if (sourceIsActive && !destinationIsActive && sourceSet != previousSet)
                destinationDamageBypassWouldChangePixels = true;
            if (!destinationIsActive || !sourceIsActive)
            {
                continue;
            }

            var set = sourceSet;
            var previous = PixelIsSet(expectedPixels, destination.Image.BytesPerRow,
                destinationBitmapX, destinationBitmapY);
            if (set && !previous) setChanges++;
            if (!set && previous) clearChanges++;
            SetPixel(expectedPixels, destination.Image.BytesPerRow,
                destinationBitmapX, destinationBitmapY, set);
            transferredPixels++;
        }
        var expectedTransferredPixels = expectedTransferredPixelsOverride ??
            (replaceCallerRegionsDuringUpdate
                ? applyScroll ? 2 : 3
                : disconnectedDamage
                    ? 4
                    : shareLayerInfo
                        ? 8
                        : !applyScroll ? 9 : useCallerClipRegions ? 12 : 56);
        Assert.Equal(expectedTransferredPixels, transferredPixels);
        if (expectedTransferredPixelsOverride is null)
        {
            Assert.True(setChanges > 0);
            Assert.True(clearChanges > 0);
            if (disconnectedDamage)
            {
                Assert.Equal(expectedTransferredPixels, setChanges + clearChanges);
            }
            else if (!applyScroll)
            {
                Assert.Equal(3, setChanges);
                Assert.Equal(6, clearChanges);
            }
            else if (shareLayerInfo)
            {
                Assert.Equal(3, setChanges);
                Assert.Equal(2, clearChanges);
            }
            else if (useCallerClipRegions)
            {
                Assert.Equal(4, setChanges);
                Assert.Equal(4, clearChanges);
            }
        }
        else if (expectedTransferredPixelsOverride is > 0)
        {
            Assert.True(setChanges + clearChanges > 0,
                "The explicit boundary fixture must change at least one transferred pixel.");
        }
        // Each one-sided admission bug must have observable pixels to change.
        if (expectedTransferredPixelsOverride is null)
        {
            Assert.True(sourceDamageBypassWouldChangePixels,
                "Ignoring source damage must admit pixels that visibly differ at the mapped source coordinate.");
            Assert.True(destinationDamageBypassWouldChangePixels,
                "Ignoring destination damage must admit pixels that visibly differ at the mapped source coordinate.");
        }

        var heads = endpoints.Select(endpoint => ReadOracleLayerPointer(context.Bus,
            endpoint.Layer, OracleLayerPointer.ClipRect)).ToArray();
        var flags = endpoints.Select(endpoint => ReadOracleLayerFlags(context.Bus,
            endpoint.Layer)).ToArray();

        // ClipBlit takes six WORD arguments and one UBYTE minterm. Their
        // unused upper bits must not change clipping or copy dimensions.
        // https://d0.se/autodocs/graphics.library/ClipBlit
        uint WordArgument(ushort value) => (poisonUnusedRegisterBits ? 0xFFFF_0000u : 0u) | value;
        var arguments = new[]
        {
            WordArgument(unchecked((ushort)sourceX)), WordArgument(unchecked((ushort)sourceY)),
            WordArgument(unchecked((ushort)destinationX)), WordArgument(unchecked((ushort)destinationY)),
            WordArgument(unchecked((ushort)transferWidth)), WordArgument(unchecked((ushort)transferHeight)),
            poisonUnusedRegisterBits ? 0xA5FF_12C0u : 0xC0u, 0xB2B2_7007u
        };
        var blitResult = context.InvokeGraphics((int)GraphicsLvo.ClipBlit, state =>
        {
            state.A[0] = source.RastPort;
            state.A[1] = destination.RastPort;
            for (var index = 0; index < arguments.Length; index++) state.D[index] = arguments[index];
        });
        context.WaitForBlitterIdle();
        Assert.False(context.Bus.Blitter.Busy);
        Capture("clipblit");
        Assert.Equal(expectedPixels, Bytes(destination.Image.Planes[0], expectedPixels.Length));
        Assert.Equal(sourcePixels, Bytes(source.Image.Planes[0], sourcePixels.Length));
        for (var index = 2; index < arguments.Length; index++)
            Assert.Equal(arguments[index], blitResult.D[index]);
        for (var index = 0; index < endpoints.Length; index++)
        {
            var endpoint = endpoints[index];
            Assert.Equal(heads[index], ReadOracleLayerPointer(context.Bus, endpoint.Layer,
                OracleLayerPointer.ClipRect));
            Assert.Equal(flags[index], ReadOracleLayerFlags(context.Bus, endpoint.Layer));
            Assert.Equal(endpoint.Damage, ReadOracleLayerPointer(context.Bus, endpoint.Layer,
                OracleLayerPointer.DamageList));
            foreach (var (address, saved) in endpoint.DamageBytes)
                Assert.Equal(saved, Bytes(address, saved.Length));
            var expectedCoverage = index == 0
                ? sourceActiveCoverage
                : destinationActiveCoverage;
            Assert.Equal(expectedCoverage, VisibleCoverage(endpoint.Layer,
                index == 0 ? sourceOriginX : destinationOriginX,
                index == 0 ? sourceOriginY : destinationOriginY));
        }
        if (useCallerClipRegions)
        {
            AssertClipRegionUnchanged(sourceClipRegion, sourceClipBytes,
                sourceClipNodes, sourceClipNodeBytes);
            AssertClipRegionUnchanged(destinationClipRegion,
                destinationClipBytes, destinationClipNodes,
                destinationClipNodeBytes);
            if (replaceCallerRegionsDuringUpdate)
            {
                AssertClipRegionUnchanged(initialSourceClipRegion,
                    initialSourceClipBytes, initialSourceClipNodes,
                    initialSourceClipNodeBytes);
                AssertClipRegionUnchanged(initialDestinationClipRegion,
                    initialDestinationClipBytes, initialDestinationClipNodes,
                    initialDestinationClipNodeBytes);
            }
        }

        End(destination.Layer, destination.Damage, destinationStableCoverage,
            destinationOriginX, destinationOriginY, destination.DamageBytes,
            !incompleteEndUpdate);
        Capture(incompleteEndUpdate
            ? "destination-end-incomplete"
            : "destination-end-complete");
        // Finishing the destination must not finish or consume source refresh.
        Assert.True((ReadOracleLayerFlags(context.Bus, source.Layer) & LayerFlags.Updating) != 0);
        Assert.Equal(sourceActiveCoverage, VisibleCoverage(source.Layer,
            sourceOriginX, sourceOriginY));
        foreach (var (address, saved) in source.DamageBytes)
            Assert.Equal(saved, Bytes(address, saved.Length));
        End(source.Layer, source.Damage, sourceStableCoverage,
            sourceOriginX, sourceOriginY, source.DamageBytes,
            !incompleteEndUpdate);
        Capture(incompleteEndUpdate
            ? "source-end-incomplete"
            : "source-end-complete");
        if (retryAfterIncompleteEndUpdate)
        {
            Begin(source.Layer, sourceActiveCoverage, sourceOriginX,
                sourceOriginY);
            Capture("source-retry-begin");
            Begin(destination.Layer, destinationActiveCoverage,
                destinationOriginX, destinationOriginY);
            Capture("destination-retry-begin");
            var retryHeads = endpoints.Select(endpoint =>
                ReadOracleLayerPointer(context.Bus, endpoint.Layer,
                    OracleLayerPointer.ClipRect)).ToArray();
            var retryFlags = endpoints.Select(endpoint =>
                ReadOracleLayerFlags(context.Bus, endpoint.Layer)).ToArray();
            var retryResult = context.InvokeGraphics(
                (int)GraphicsLvo.ClipBlit, state =>
                {
                    state.A[0] = source.RastPort;
                    state.A[1] = destination.RastPort;
                    for (var index = 0; index < arguments.Length; index++)
                        state.D[index] = arguments[index];
                });
            context.WaitForBlitterIdle();
            Assert.False(context.Bus.Blitter.Busy);
            Capture("clipblit-retry");
            Assert.Equal(expectedPixels,
                Bytes(destination.Image.Planes[0], expectedPixels.Length));
            Assert.Equal(sourcePixels,
                Bytes(source.Image.Planes[0], sourcePixels.Length));
            for (var index = 2; index < arguments.Length; index++)
                Assert.Equal(arguments[index], retryResult.D[index]);
            for (var index = 0; index < endpoints.Length; index++)
            {
                var endpoint = endpoints[index];
                Assert.Equal(retryHeads[index],
                    ReadOracleLayerPointer(context.Bus, endpoint.Layer,
                        OracleLayerPointer.ClipRect));
                Assert.Equal(retryFlags[index],
                    ReadOracleLayerFlags(context.Bus, endpoint.Layer));
                Assert.Equal(endpoint.Damage,
                    ReadOracleLayerPointer(context.Bus, endpoint.Layer,
                        OracleLayerPointer.DamageList));
                foreach (var (address, saved) in endpoint.DamageBytes)
                    Assert.Equal(saved, Bytes(address, saved.Length));
            }
            AssertClipRegionUnchanged(sourceClipRegion, sourceClipBytes,
                sourceClipNodes, sourceClipNodeBytes);
            AssertClipRegionUnchanged(destinationClipRegion,
                destinationClipBytes, destinationClipNodes,
                destinationClipNodeBytes);
            if (sourceCompletesBeforeDestination)
            {
                End(source.Layer, source.Damage, sourceStableCoverage,
                    sourceOriginX, sourceOriginY, source.DamageBytes,
                    complete: true);
                Capture("source-retry-end-complete");
                Assert.False((ReadOracleLayerFlags(context.Bus, source.Layer) &
                    LayerFlags.Updating) != 0);
                Assert.True((ReadOracleLayerFlags(context.Bus,
                    destination.Layer) & LayerFlags.Updating) != 0);
                Assert.Equal(destination.Damage,
                    ReadOracleLayerPointer(context.Bus, destination.Layer,
                        OracleLayerPointer.DamageList));
                Assert.Equal(destinationActiveCoverage,
                    VisibleCoverage(destination.Layer, destinationOriginX,
                        destinationOriginY));
                foreach (var (address, saved) in destination.DamageBytes)
                    Assert.Equal(saved, Bytes(address, saved.Length));
                End(destination.Layer, destination.Damage,
                    destinationStableCoverage, destinationOriginX,
                    destinationOriginY, destination.DamageBytes,
                    complete: true);
                Capture("destination-retry-end-complete");
            }
            else
            {
                End(destination.Layer, destination.Damage,
                    destinationStableCoverage, destinationOriginX,
                    destinationOriginY, destination.DamageBytes,
                    complete: true);
                Capture("destination-retry-end-complete");
                Assert.True((ReadOracleLayerFlags(context.Bus, source.Layer) &
                    LayerFlags.Updating) != 0);
                foreach (var (address, saved) in source.DamageBytes)
                    Assert.Equal(saved, Bytes(address, saved.Length));
                End(source.Layer, source.Damage, sourceStableCoverage,
                    sourceOriginX, sourceOriginY, source.DamageBytes,
                    complete: true);
                Capture("source-retry-end-complete");
            }
        }
        Assert.Equal(expectedPixels, Bytes(destination.Image.Planes[0], expectedPixels.Length));
        Assert.Equal(sourcePixels, Bytes(source.Image.Planes[0], sourcePixels.Length));
        if (useCallerClipRegions)
        {
            AssertClipRegionUnchanged(sourceClipRegion, sourceClipBytes,
                sourceClipNodes, sourceClipNodeBytes);
            AssertClipRegionUnchanged(destinationClipRegion,
                destinationClipBytes, destinationClipNodes,
                destinationClipNodeBytes);
            if (replaceCallerRegionsDuringUpdate)
            {
                AssertClipRegionUnchanged(initialSourceClipRegion,
                    initialSourceClipBytes, initialSourceClipNodes,
                    initialSourceClipNodeBytes);
                AssertClipRegionUnchanged(initialDestinationClipRegion,
                    initialDestinationClipBytes, initialDestinationClipNodes,
                    initialDestinationClipNodeBytes);
            }

            Assert.Equal(sourceClipRegion, context.InvokeLayers(LayersLvo.InstallClipRegion, state =>
            {
                state.A[0] = source.Layer;
                state.A[1] = 0;
            }).D[0]);
            Assert.Equal(destinationClipRegion, context.InvokeLayers(LayersLvo.InstallClipRegion, state =>
            {
                state.A[0] = destination.Layer;
                state.A[1] = 0;
            }).D[0]);
            AssertClipRegionUnchanged(sourceClipRegion, sourceClipBytes,
                sourceClipNodes, sourceClipNodeBytes);
            AssertClipRegionUnchanged(destinationClipRegion,
                destinationClipBytes, destinationClipNodes,
                destinationClipNodeBytes);
            if (replaceCallerRegionsDuringUpdate)
            {
                AssertClipRegionUnchanged(initialSourceClipRegion,
                    initialSourceClipBytes, initialSourceClipNodes,
                    initialSourceClipNodeBytes);
                AssertClipRegionUnchanged(initialDestinationClipRegion,
                    initialDestinationClipBytes, initialDestinationClipNodes,
                    initialDestinationClipNodeBytes);
            }
        }
        if (applyScroll)
        {
            var sourceLayerAddress = APTR.FromPointer(source.Layer);
            var destinationLayerAddress = APTR.FromPointer(destination.Layer);
            Assert.Equal(sourceScrollX, LayersLayerCodec.ReadScrollX(ref memory, sourceLayerAddress));
            Assert.Equal(sourceScrollY, LayersLayerCodec.ReadScrollY(ref memory, sourceLayerAddress));
            Assert.Equal(destinationScrollX, LayersLayerCodec.ReadScrollX(ref memory, destinationLayerAddress));
            Assert.Equal(destinationScrollY, LayersLayerCodec.ReadScrollY(ref memory, destinationLayerAddress));
        }
        Assert.Equal(sourceLayerOnTop
                ? fullCoverage
                : RemoveDestinationOcclusion(fullCoverage),
            VisibleCoverage(source.Layer, sourceOriginX, sourceOriginY));
        Assert.Equal(sourceLayerOnTop
                ? RemoveSourceOcclusion(fullCoverage)
                : fullCoverage,
            VisibleCoverage(destination.Layer, destinationOriginX,
                destinationOriginY));

        foreach (var endpoint in endpoints.Reverse())
        {
            Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.DeleteLayer,
                state => state.A[1] = endpoint.Layer).D[0]);
            context.FreePlanarBitMap(endpoint.Image);
            if (shareLayerInfo && endpoint.Layer == destination.Layer)
                Assert.Equal(fullCoverage, VisibleCoverage(source.Layer,
                    sourceOriginX, sourceOriginY));
        }
        foreach (var layerInfo in endpoints.Select(endpoint => endpoint.Info).Distinct())
            context.DisposeLayerInfo(layerInfo);
        if (useCallerClipRegions)
        {
            foreach (var allocation in sourceClipAllocations)
            {
                foreach (var node in allocation.Nodes)
                    context.Free(node, RegionRectangle.Size);
                context.Free(allocation.Region, Region.Size);
            }
            foreach (var allocation in destinationClipAllocations)
            {
                foreach (var node in allocation.Nodes)
                    context.Free(node, RegionRectangle.Size);
                context.Free(allocation.Region, Region.Size);
            }
        }
        return trace.ToArray();

        void Scroll(uint layer, short deltaX, short deltaY)
        {
            _ = context.InvokeLayers(LayersLvo.ScrollLayer, state =>
            {
                state.A[1] = layer;
                state.D[0] = unchecked((uint)(int)deltaX);
                state.D[1] = unchecked((uint)(int)deltaY);
            });
        }

        uint CreateRectangleRegion(
            (int MinX, int MinY, int MaxX, int MaxY)[] rectangles,
            out uint[] nodes)
        {
            Assert.True(rectangles.Length > 1);
            var region = context.Allocate(Region.Size);
            nodes = rectangles.Select(_ =>
                context.Allocate(RegionRectangle.Size)).ToArray();
            var regionAddress = APTR.FromPointer(region);
            var regionMinX = rectangles.Min(rectangle => rectangle.MinX);
            var regionMinY = rectangles.Min(rectangle => rectangle.MinY);
            var regionMaxX = rectangles.Max(rectangle => rectangle.MaxX);
            var regionMaxY = rectangles.Max(rectangle => rectangle.MaxY);
            LayersRegionCodec.WriteBounds(ref memory, regionAddress,
                LayersRectangleCodec.Create(checked((short)regionMinX),
                    checked((short)regionMinY), checked((short)regionMaxX),
                    checked((short)regionMaxY)));
            LayersRegionCodec.WriteFirst(ref memory, regionAddress,
                APTR.FromPointer(nodes[0]));
            for (var index = 0; index < nodes.Length; index++)
            {
                var node = APTR.FromPointer(nodes[index]);
                LayersRegionRectangleCodec.WritePrevious(ref memory, node,
                    index == 0
                        ? LayersRegionCodec.HeadAnchor(regionAddress)
                        : APTR.FromPointer(nodes[index - 1]));
                LayersRegionRectangleCodec.WriteNext(ref memory, node,
                    index + 1 < nodes.Length
                        ? APTR.FromPointer(nodes[index + 1])
                        : APTR.Null);
                var rectangle = rectangles[index];
                LayersRegionRectangleCodec.WriteBounds(ref memory, node,
                    LayersRectangleCodec.Create(
                        checked((short)(rectangle.MinX - regionMinX)),
                        checked((short)(rectangle.MinY - regionMinY)),
                        checked((short)(rectangle.MaxX - regionMinX)),
                        checked((short)(rectangle.MaxY - regionMinY))));
            }
            return region;
        }

        void AssertClipRegionUnchanged(uint region, byte[] regionBytes,
            uint[] nodes, byte[][] nodeBytes)
        {
            Assert.Equal(regionBytes, Bytes(region, regionBytes.Length));
            Assert.Equal(nodes.Length, nodeBytes.Length);
            for (var index = 0; index < nodes.Length; index++)
                Assert.Equal(nodeBytes[index], Bytes(nodes[index],
                    checked((int)RegionRectangle.Size)));
        }

        (uint Info, PlanarBitMap Image, uint Layer, uint RastPort, uint Damage,
            Dictionary<uint, byte[]> DamageBytes, int[] DamageCoverage)
            CreateEndpoint(int minX, int minY, int maxX, int maxY,
                int originX, int originY, uint existingLayerInfo,
                bool createDisconnectedDamage)
        {
            var info = existingLayerInfo == 0
                ? context.NewLayerInfo()
                : existingLayerInfo;
            var bitmap = context.CreatePlanarBitMap(width + originX,
                height + originY, 1, 0,
                planeMemoryFlags: Exec.MemoryFlags.Chip);
            Assert.Equal(bitmap.Planes[0], context.Bus.MaskChipDmaAddress(bitmap.Planes[0]));
            Assert.InRange(bitmap.Planes[0], 1u, checked((uint)(context.Machine.Options.ChipRamSize -
                bitmap.BytesPerRow * bitmap.Height)));
            var layer = context.CreateLayer(LayersLvo.CreateUpfrontLayer, info,
                bitmap.Address, 0, LayerCreationFlags.Simple, originX, originY,
                originX + width - 1, originY + height - 1);
            (int MinX, int MinY, int MaxX, int MaxY)[] damageRectangles =
                createDisconnectedDamage
                    ? [(minX, minY, minX + 5, minY + 4),
                        (minX + 7, minY + 5, maxX, maxY)]
                    : [(minX, minY, maxX, maxY)];
            foreach (var rectangle in damageRectangles)
            {
                var cover = context.CreateLayer(LayersLvo.CreateUpfrontLayer,
                    info, bitmap.Address, 0, LayerCreationFlags.Simple,
                    originX + rectangle.MinX, originY + rectangle.MinY,
                    originX + rectangle.MaxX, originY + rectangle.MaxY);
                Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.DeleteLayer,
                    state => state.A[1] = cover).D[0]);
            }
            context.WaitForBlitterIdle();
            Assert.False(context.Bus.Blitter.Busy);
            var damage = ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.DamageList);
            var damageCoverage = damageRectangles.SelectMany(rectangle =>
                    RectangleCoverage(rectangle.MinX, rectangle.MinY,
                        rectangle.MaxX, rectangle.MaxY))
                .Distinct().OrderBy(pixel => pixel).ToArray();
            Assert.Equal(damageCoverage,
                ReadOracleRegionPixels(context, damage)
                    .Select(pixel => pixel.Y * width + pixel.X)
                    .OrderBy(pixel => pixel).ToArray());
            var saved = new Dictionary<uint, byte[]>
            {
                [damage] = Bytes(damage, checked((int)Region.Size))
            };
            for (var node = ReadOracleRegionFirst(context.Bus, damage); node != 0;
                 node = LayersRegionRectangleCodec.ReadNext(ref memory, APTR.FromPointer(node)).Raw)
            {
                Assert.True(saved.Count < 256 && !saved.ContainsKey(node));
                saved.Add(node, Bytes(node, checked((int)RegionRectangle.Size)));
            }
            var rastPort = ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.RastPort);
            context.InvokeGraphics((int)GraphicsLvo.SetWriteMask, state =>
            {
                state.A[1] = rastPort;
                state.D[0] = 0xFF;
            });
            return (info, bitmap, layer, rastPort, damage, saved,
                damageCoverage);
        }

        void Begin(uint layer, int[] expectedCoverage, int originX, int originY)
        {
            Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.BeginUpdate,
                state => state.A[0] = layer).D[0]);
            Assert.True((ReadOracleLayerFlags(context.Bus, layer) & LayerFlags.Updating) != 0);
            Assert.Equal(expectedCoverage, VisibleCoverage(layer, originX, originY));
        }

        void End(uint layer, uint damage, int[] expectedCoverage,
            int originX, int originY, Dictionary<uint, byte[]> damageBytes,
            bool complete)
        {
            context.InvokeLayers(LayersLvo.EndUpdate, state =>
            {
                state.A[0] = layer;
                state.D[0] = complete ? 1u : 0u;
            });
            var flags = ReadOracleLayerFlags(context.Bus, layer);
            Assert.True((flags & LayerFlags.Updating) == 0);
            Assert.True((flags & LayerFlags.Refresh) != 0);
            Assert.Equal(expectedCoverage, VisibleCoverage(layer, originX, originY));
            Assert.Equal(damage, ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.DamageList));
            if (complete)
            {
                Assert.Equal(new byte[checked((int)Region.Size)],
                    Bytes(damage, checked((int)Region.Size)));
            }
            else
            {
                foreach (var (address, saved) in damageBytes)
                    Assert.Equal(saved, Bytes(address, saved.Length));
            }
        }

        void Capture(string phase)
        {
            const LayerFlags publicFlags = LayerFlags.Simple | LayerFlags.Smart | LayerFlags.Super |
                LayerFlags.Backdrop | LayerFlags.Updating | LayerFlags.Refresh | LayerFlags.ClipRectsLost;
            var row = $"clipblit-refresh:{phase}:" + string.Join(';', endpoints.Select((endpoint, index) =>
                $"{(index == 0 ? "source" : "destination")}:flags={ReadOracleLayerFlags(context.Bus, endpoint.Layer) & publicFlags}:" +
                $"damage={DescribeRegion(context, ReadOracleLayerPointer(context.Bus, endpoint.Layer, OracleLayerPointer.DamageList))}:" +
                $"coverage=[{string.Join(',', VisibleCoverage(endpoint.Layer,
                    index == 0 ? sourceOriginX : destinationOriginX,
                    index == 0 ? sourceOriginY : destinationOriginY))}]"));
            trace.Add(row);
            _output.WriteLine((context.Native ? "Native " : "CopperStart ") + row);
        }

        int[] VisibleCoverage(uint layer, int originX, int originY)
        {
            var pixels = new SortedSet<int>();
            var seen = new HashSet<uint>();
            for (var node = ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.ClipRect); node != 0;
                 node = ReadOracleClipRectPointer(context.Bus, node, OracleClipRectPointer.Next))
            {
                Assert.True(seen.Count < 256 && seen.Add(node));
                Assert.True((node & 1) == 0 && context.Bus.IsMappedMemoryRange(node, checked((int)ClipRect.Size)));
                if (ReadOracleClipRectPointer(context.Bus, node, OracleClipRectPointer.ObscuringLayer) != 0) continue;
                var bounds = ReadOracleClipRectBounds(context.Bus, node);
                Assert.InRange((int)bounds.MinX, originX, originX + width - 1);
                Assert.InRange((int)bounds.MaxX, bounds.MinX, originX + width - 1);
                Assert.InRange((int)bounds.MinY, originY, originY + height - 1);
                Assert.InRange((int)bounds.MaxY, bounds.MinY, originY + height - 1);
                foreach (var pixel in RectangleCoverage(
                             bounds.MinX - originX, bounds.MinY - originY,
                             bounds.MaxX - originX, bounds.MaxY - originY))
                    Assert.True(pixels.Add(pixel), "Visible ClipRects must not overlap.");
            }
            return pixels.ToArray();
        }

        int[] RectangleCoverage(int minX, int minY, int maxX, int maxY) =>
            Enumerable.Range(minY, maxY - minY + 1).SelectMany(y =>
                Enumerable.Range(minX, maxX - minX + 1).Select(x => y * width + x)).ToArray();

        int[] RemoveDestinationOcclusion(int[] coverage)
            => RemoveOcclusion(coverage, sourceOriginX, sourceOriginY,
                destinationOriginX, destinationOriginY);

        int[] RemoveSourceOcclusion(int[] coverage)
            => RemoveOcclusion(coverage, destinationOriginX,
                destinationOriginY, sourceOriginX, sourceOriginY);

        int[] RemoveOcclusion(int[] coverage, int coveredOriginX,
            int coveredOriginY, int coveringOriginX, int coveringOriginY)
        {
            if (!shareLayerInfo) return coverage;
            var minX = Math.Max(0, coveringOriginX - coveredOriginX);
            var maxX = Math.Min(width - 1,
                coveringOriginX + width - 1 - coveredOriginX);
            var minY = Math.Max(0, coveringOriginY - coveredOriginY);
            var maxY = Math.Min(height - 1,
                coveringOriginY + height - 1 - coveredOriginY);
            if (minX > maxX || minY > maxY) return coverage;
            return coverage.Where(pixel =>
            {
                var x = pixel % width;
                var y = pixel / width;
                return x < minX || x > maxX || y < minY || y > maxY;
            }).ToArray();
        }

        byte[] MakePixels(PlanarBitMap bitmap, int originX, int originY,
            Func<int, int, bool> pattern)
        {
            var bytes = new byte[bitmap.BytesPerRow * bitmap.Height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                if (pattern(x, y)) SetPixel(bytes, bitmap.BytesPerRow,
                    originX + x, originY + y, true);
            return bytes;
        }

        bool PixelIsSet(byte[] pixels, int bytesPerRow, int x, int y)
        {
            var offset = checked(y * bytesPerRow + x / 8);
            return (pixels[offset] & (0x80 >> (x & 7))) != 0;
        }

        void SetPixel(byte[] pixels, int bytesPerRow, int x, int y, bool set)
        {
            var offset = checked(y * bytesPerRow + x / 8);
            var mask = (byte)(0x80 >> (x & 7));
            pixels[offset] = set
                ? (byte)(pixels[offset] | mask)
                : (byte)(pixels[offset] & ~mask);
        }

        byte[] Bytes(uint address, int count) => Enumerable.Range(0, count)
            .Select(offset => context.Bus.ReadByte(address + checked((uint)offset))).ToArray();

        void WriteBytes(uint address, byte[] bytes)
        {
            for (var index = 0; index < bytes.Length; index++)
                context.Bus.WriteByte(address + checked((uint)index), bytes[index], 0);
        }
    }
}
