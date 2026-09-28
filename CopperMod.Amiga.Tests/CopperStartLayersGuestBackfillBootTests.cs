using Amiga;
using Copper68k;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using Fixture = CopperMod.Amiga.Tests.CopperStartIntuitionCallbackBootTests;
using GraphicsLvo = CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsLvo;

namespace CopperMod.Amiga.Tests;

public sealed partial class CopperStartLayersBootTests
{
    [Fact]
    public void GuestBackfillResizeAndShrinkRespectTransparentRegionAcrossPartialRefresh()
    {
        using var machine = CreateMachine();
        var boot = new AmigaBootController(machine);
        boot.StartBootFromDisk(CreateBootableDisk());
        var bus = machine.Bus;
        var libraryBase = boot.CopperStartLayersLibraryBase;
        var layerInfo = NewLayerInfo(bus, libraryBase);
        var displayBitMap = AllocatePlanarBitMap(bus, 32, 20);
        var rectangle = AllocateGuestMemory(
            bus, (uint)GraphicsLayouts.RectangleSize);
        var transparencyRegionResult = new M68kCpuState();
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.NewRegion),
            transparencyRegionResult));
        var transparencyRegion = transparencyRegionResult.D[0];
        Assert.NotEqual(0u, transparencyRegion);
        WriteRectangleBounds(bus, rectangle, 26, 18, 26, 18);
        var addTransparentGrowthPixel = new M68kCpuState
        {
            A = { [0] = transparencyRegion, [1] = rectangle }
        };
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.OrRectRegion),
            addTransparentGrowthPixel));
        Assert.Equal(1u, addTransparentGrowthPixel.D[0]);
        var transparencyRegionBytes = SnapshotGraphicsRegion(
            bus, transparencyRegion);
        var transparencyTags = AllocateGuestMemory(bus, 16);
        bus.WriteLong(transparencyTags,
            (uint)LayerCreationTag.TransparentRegion);
        bus.WriteLong(transparencyTags + 4, transparencyRegion);
        bus.WriteLong(transparencyTags + 8, 0);
        bus.WriteLong(transparencyTags + 12, 0);
        var createBottom = new M68kCpuState
        {
            A =
            {
                [0] = layerInfo,
                [1] = displayBitMap,
                [2] = transparencyTags
            },
            D =
            {
                [0] = 0,
                [1] = 0,
                [2] = 23,
                [3] = 15,
                [4] = (uint)LayerCreationFlags.Simple
            }
        };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.CreateUpfrontLayerTagList),
            createBottom));
        Assert.NotEqual(0u, createBottom.D[0]);
        var bottom = new LayerSurface(
            displayBitMap,
            createBottom.D[0],
            ReadLayerRastPort(bus, createBottom.D[0]),
            ReadLayerClipRect(bus, createBottom.D[0]));
        FreeGuestMemory(bus, transparencyTags, 16);
        var top = CreateLayerOnBitMaps(
            bus, libraryBase, layerInfo, displayBitMap, 0,
            LayerCreationFlags.Simple, 4, 4, 15, 11);
        var layerMemory = new LayersTestGuestMemory(bus);

        var initialMove = new M68kCpuState
        {
            A = { [1] = top.Layer },
            D = { [0] = 2, [1] = 1 }
        };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.MoveLayer), initialMove));
        Assert.Equal(1u, initialMove.D[0]);
        Assert.True(LayersLayerCodec.ReadDamageList(
            ref layerMemory, APTR.FromPointer(bottom.Layer)).IsNotNull);

        var beginInitial = new M68kCpuState { A = { [0] = bottom.Layer } };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.BeginUpdate), beginInitial));
        Assert.Equal(1u, beginInitial.D[0]);
        ClearPlanarBitMap(bus, displayBitMap);
        WriteRastPortForegroundPen(bus, bottom.RastPort, 1);
        var fillInitial = new M68kCpuState
        {
            A = { [1] = bottom.RastPort },
            D = { [0] = 0, [1] = 0, [2] = 31, [3] = 19 }
        };
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.RectFill),
            fillInitial));
        var endInitial = new M68kCpuState
        {
            A = { [0] = bottom.Layer },
            D = { [0] = 1 }
        };
        AssertEndUpdateClearsOwnedDamage(bus, libraryBase, endInitial);
        ClearPlanarBitMap(bus, displayBitMap);

        var regionResult = new M68kCpuState();
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.NewRegion),
            regionResult));
        var callerClipRegion = regionResult.D[0];
        Assert.NotEqual(0u, callerClipRegion);
        void AddClipRectangle(short minX, short minY, short maxX, short maxY)
        {
            WriteRectangleBounds(
                bus, rectangle, minX, minY, maxX, maxY);
            var add = new M68kCpuState
            {
                A = { [0] = callerClipRegion, [1] = rectangle }
            };
            Assert.True(InvokeHostTrap(
                bus,
                Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                    (int)GraphicsLvo.OrRectRegion),
                add));
            Assert.Equal(1u, add.D[0]);
        }

        // The resize area is selected, plus one pixel in the later occluder
        // reveal. The explicit transparent pixel is selected too, but must
        // remain absent from the effective projection. Neither Region is
        // modified by Layers while installed.
        AddClipRectangle(26, 18, 27, 19);
        AddClipRectangle(6, 5, 6, 5);
        var callerClipBytes = SnapshotGraphicsRegion(bus, callerClipRegion);
        var installClip = new M68kCpuState
        {
            A = { [0] = bottom.Layer, [1] = callerClipRegion }
        };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.InstallClipRegion), installClip));
        Assert.Equal(0u, installClip.D[0]);

        var hookAuthority = new Fixture.BorrowAuthority();
        var hookCode = hookAuthority.Allocate(bus, 0x1000);
        var hookMarker = hookAuthority.Allocate(bus, sizeof(uint));
        bus.WriteLong(hookMarker.Raw, 0);
        var hook = hookAuthority.Hook(bus, hookCode.Raw);
        const uint hookMarkerValue = 0xBACCF111;
        var hookProgram = new Fixture.GuestProgram(bus, hookCode.Raw);
        hookProgram.StoreLong(hookMarker.Raw, hookMarkerValue);
        hookProgram.Return();
        Assert.False(bus.HasHostGateway(hookCode.Raw));
        var installHook = new M68kCpuState
        {
            A = { [0] = bottom.Layer, [1] = hook.Raw }
        };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.InstallLayerHook), installHook));

        var callerCode = hookAuthority.Allocate(bus, 0x2000);
        var userStack = Fixture.GuestStack.Allocate(bus);
        var supervisorStack = Fixture.GuestStack.Allocate(bus);
        var actor = Fixture.CurrentTask(bus);
        Fixture.SetTaskStack(bus, actor, userStack);
        var caller = new Fixture.GuestProgram(bus, callerCode.Raw, 0x2000);

        caller.MoveAddress(6, libraryBase);
        caller.MoveAddress(1, bottom.Layer);
        caller.MoveData(0, 4);
        caller.MoveData(1, 4);
        var afterResize = caller.Call(Lvo(libraryBase, LayersLvo.SizeLayer));

        caller.MoveAddress(0, bottom.Layer);
        var afterBeginResizeUpdate = caller.Call(
            Lvo(libraryBase, LayersLvo.BeginUpdate));
        caller.MoveAddress(6, AmigaKickstartHost.GraphicsLibraryBase);
        caller.MoveAddress(1, bottom.RastPort);
        caller.MoveData(0, 1);
        caller.Call(Lvo(
            AmigaKickstartHost.GraphicsLibraryBase,
            (int)GraphicsLvo.SetRast));
        caller.MoveAddress(6, libraryBase);
        caller.MoveAddress(0, bottom.Layer);
        caller.MoveData(0, 0);
        var afterResizePartialEnd = caller.Call(
            Lvo(libraryBase, LayersLvo.EndUpdate));

        caller.MoveAddress(1, bottom.Layer);
        caller.MoveData(0, unchecked((uint)-2));
        caller.MoveData(1, unchecked((uint)-2));
        var afterShrink = caller.Call(Lvo(libraryBase, LayersLvo.SizeLayer));

        caller.MoveAddress(1, top.Layer);
        caller.MoveData(0, 1);
        caller.MoveData(1, 0);
        var afterReveal = caller.Call(Lvo(libraryBase, LayersLvo.MoveLayer));

        caller.MoveAddress(0, bottom.Layer);
        var afterBeginRevealUpdate = caller.Call(
            Lvo(libraryBase, LayersLvo.BeginUpdate));
        caller.MoveAddress(6, AmigaKickstartHost.GraphicsLibraryBase);
        caller.MoveAddress(1, bottom.RastPort);
        caller.MoveData(0, 1);
        caller.Call(Lvo(
            AmigaKickstartHost.GraphicsLibraryBase,
            (int)GraphicsLvo.SetRast));
        caller.MoveAddress(6, libraryBase);
        caller.MoveAddress(0, bottom.Layer);
        caller.MoveData(0, 0);
        var afterRevealPartialEnd = caller.Call(
            Lvo(libraryBase, LayersLvo.EndUpdate));
        caller.Park();

        Fixture.StartRequester(
            machine, callerCode.Raw, userStack, supervisorStack);
        var bitmapBeforeResize = SnapshotPlanarBitMap(bus, displayBitMap);
        var firstCycle = machine.Cpu.State.Cycles;
        const long cycleBudget = 4_000_000;
        var instructionCount = 0;
        var guestHookEntries = 0;
        var lastGuestHookCycle = -1L;
        var callbackMessages = new List<LayerBackfillMessage>();
        void ObserveGuestHookEntry()
        {
            var state = machine.Cpu.State;
            if (state.ProgramCounter != hookCode.Raw ||
                state.Cycles == lastGuestHookCycle) return;
            lastGuestHookCycle = state.Cycles;
            guestHookEntries++;
            Assert.Equal(hook.Raw, state.A[0]);
            Assert.NotEqual(0u, state.A[1]);
            Assert.Equal(bottom.RastPort, state.A[2]);
            var messageMemory = new LayersTestGuestMemory(bus);
            Assert.True(LayersHookMessageCodec.TryRead(
                ref messageMemory, APTR.FromPointer(state.A[1]),
                out LayerBackfillMessage message));
            callbackMessages.Add(message);
        }

        void StepUntil(uint targetPc)
        {
            while (machine.Cpu.State.ProgramCounter != targetPc)
            {
                ObserveGuestHookEntry();
                Assert.False(machine.Cpu.State.Halted);
                Assert.True(instructionCount < 40_000);
                var result = boot.ContinueCopperStartRuntimeUntilCycle(
                    firstCycle + cycleBudget, maxInstructions: 1);
                instructionCount += result.InstructionsExecuted;
                Assert.Equal(1, result.InstructionsExecuted);
            }
            ObserveGuestHookEntry();
        }

        var screenClipAtResize = new HashSet<(int X, int Y)>
        {
            (27, 18), (26, 19), (27, 19)
        };
        HashSet<(int X, int Y)> CaptureClipPixels(uint head)
        {
            var pixels = new HashSet<(int X, int Y)>();
            for (var clipRect = head; clipRect != 0;
                 clipRect = ReadClipRectNext(bus, clipRect))
            {
                var bounds = LayersClipRectCodec.BoundsAddress(
                    APTR.FromPointer(clipRect)).Raw;
                var minX = unchecked((short)bus.ReadWord(bounds));
                var minY = unchecked((short)bus.ReadWord(bounds + 2));
                var maxX = unchecked((short)bus.ReadWord(bounds + 4));
                var maxY = unchecked((short)bus.ReadWord(bounds + 6));
                for (var y = Math.Max(0, (int)minY);
                     y <= Math.Min(19, (int)maxY); y++)
                    for (var x = Math.Max(0, (int)minX);
                         x <= Math.Min(31, (int)maxX); x++)
                        pixels.Add((x, y));
            }
            return pixels;
        }

        void AssertDisplayPixels(HashSet<(int X, int Y)> expected)
        {
            for (var y = 0; y < 20; y++)
                for (var x = 0; x < 32; x++)
                    Assert.Equal(expected.Contains((x, y)),
                        ReadPlanarBitMapPixel(bus, displayBitMap, x, y));
        }

        var messagesBeforeResize = callbackMessages.Count;
        StepUntil(afterResize);
        Assert.Equal(1u, machine.Cpu.State.D[0]);
        Assert.True(guestHookEntries > 0);
        Assert.Equal(hookMarkerValue, bus.ReadLong(hookMarker.Raw));
        Assert.False(boot.CopperStartLayersHasPendingCallbackForTest);
        Assert.Equal(bitmapBeforeResize,
            SnapshotPlanarBitMap(bus, displayBitMap));
        Assert.Equal(callerClipBytes,
            SnapshotGraphicsRegion(bus, callerClipRegion));
        var resizeExposure = new HashSet<(int X, int Y)>();
        foreach (var message in callbackMessages.Skip(messagesBeforeResize)
            .Where(message => message.Layer == APTR.FromPointer(bottom.Layer)))
            for (var y = message.Bounds.MinY; y <= message.Bounds.MaxY; y++)
                for (var x = message.Bounds.MinX; x <= message.Bounds.MaxX; x++)
                    resizeExposure.Add((x, y));
        Assert.True(screenClipAtResize.SetEquals(resizeExposure));
        Assert.Equal(transparencyRegionBytes,
            SnapshotGraphicsRegion(bus, transparencyRegion));

        StepUntil(afterBeginResizeUpdate);
        Assert.True(screenClipAtResize.SetEquals(
            CaptureClipPixels(ReadLayerClipRect(bus, bottom.Layer))));
        Assert.Equal(callerClipBytes,
            SnapshotGraphicsRegion(bus, callerClipRegion));
        var resizeDamage = LayersLayerCodec.ReadDamageList(
            ref layerMemory, APTR.FromPointer(bottom.Layer));
        Assert.True(resizeDamage.IsNotNull);
        var resizeDamageBytes = SnapshotGraphicsRegion(bus, resizeDamage.Raw);
        StepUntil(afterResizePartialEnd);
        Assert.Equal(resizeDamageBytes,
            SnapshotGraphicsRegion(bus, resizeDamage.Raw));
        AssertDisplayPixels(screenClipAtResize);

        StepUntil(afterShrink);
        Assert.Equal(1u, machine.Cpu.State.D[0]);
        var shrunkenBounds = LayersLayerCodec.ReadBounds(
            ref layerMemory, APTR.FromPointer(bottom.Layer));
        Assert.Equal((short)25, shrunkenBounds.MaxX);
        Assert.Equal((short)17, shrunkenBounds.MaxY);
        Assert.Equal(transparencyRegionBytes,
            SnapshotGraphicsRegion(bus, transparencyRegion));
        var shrinkDamage = LayersLayerCodec.ReadDamageList(
            ref layerMemory, APTR.FromPointer(bottom.Layer));
        Assert.True(shrinkDamage.IsNotNull);

        var callbacksBeforeReveal = guestHookEntries;
        var messagesBeforeReveal = callbackMessages.Count;
        StepUntil(hookCode.Raw);
        Assert.True(boot.CopperStartLayersHasPendingCallbackForTest);
        Assert.Equal(hook.Raw, machine.Cpu.State.A[0]);
        Assert.Equal(bottom.RastPort, machine.Cpu.State.A[2]);
		Assert.True(callbackMessages.Count > messagesBeforeReveal);
		Assert.Contains(callbackMessages.Skip(messagesBeforeReveal), message =>
			message.Layer == APTR.FromPointer(bottom.Layer) &&
			message.Bounds.MinX == 6 && message.Bounds.MaxX == 6 &&
			message.Bounds.MinY == 5 && message.Bounds.MaxY == 12);
        StepUntil(afterReveal);
        Assert.True(guestHookEntries > callbacksBeforeReveal);
        Assert.Equal(hookMarkerValue, bus.ReadLong(hookMarker.Raw));
        Assert.False(boot.CopperStartLayersHasPendingCallbackForTest);
        Assert.Equal(callerClipBytes,
            SnapshotGraphicsRegion(bus, callerClipRegion));

        var screenClipAfterReveal = new HashSet<(int X, int Y)>
        {
            (6, 5)
        };
        StepUntil(afterBeginRevealUpdate);
        Assert.True(screenClipAfterReveal.SetEquals(
            CaptureClipPixels(ReadLayerClipRect(bus, bottom.Layer))));
        var damageAfterReveal = LayersLayerCodec.ReadDamageList(
            ref layerMemory, APTR.FromPointer(bottom.Layer));
        Assert.True(damageAfterReveal.IsNotNull);
        var damageAfterRevealBytes = SnapshotGraphicsRegion(
            bus, damageAfterReveal.Raw);
        StepUntil(afterRevealPartialEnd);
        Assert.Equal(damageAfterRevealBytes,
            SnapshotGraphicsRegion(bus, damageAfterReveal.Raw));
        var bitmapPixelsAfterReveal =
            new HashSet<(int X, int Y)>(screenClipAtResize)
            {
                (6, 5)
            };
        AssertDisplayPixels(bitmapPixelsAfterReveal);
        Assert.Equal(callerClipBytes,
            SnapshotGraphicsRegion(bus, callerClipRegion));

        var restoreBackfill = new M68kCpuState
        {
            A =
            {
                [0] = bottom.Layer,
                [1] = LayerBackfillHook.NoBackfill
            }
        };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.InstallLayerHook),
            restoreBackfill));
        var removeClip = new M68kCpuState { A = { [0] = bottom.Layer } };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.InstallClipRegion), removeClip));
        Assert.Equal(callerClipRegion, removeClip.D[0]);

        var expectedDamage = new HashSet<(int X, int Y)>();
        for (var y = 0; y <= 15; y++)
            for (var x = 24; x <= 25; x++)
                expectedDamage.Add((x, y));
        for (var y = 16; y <= 17; y++)
            for (var x = 0; x <= 25; x++)
                expectedDamage.Add((x, y));
        for (var y = 5; y <= 12; y++)
            expectedDamage.Add((6, y));

        var beginComplete = new M68kCpuState { A = { [0] = bottom.Layer } };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.BeginUpdate), beginComplete));
        Assert.Equal(1u, beginComplete.D[0]);
        var finalProjection = CaptureClipPixels(
            ReadLayerClipRect(bus, bottom.Layer));
        Assert.Empty(expectedDamage.Except(finalProjection));
        Assert.Empty(finalProjection.Except(expectedDamage));
        ClearPlanarBitMap(bus, displayBitMap);
        WriteRastPortForegroundPen(bus, bottom.RastPort, 1);
        var fillComplete = new M68kCpuState
        {
            A = { [1] = bottom.RastPort },
            D = { [0] = 0, [1] = 0, [2] = 31, [3] = 19 }
        };
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.RectFill),
            fillComplete));
        AssertDisplayPixels(expectedDamage);
        var endComplete = new M68kCpuState
        {
            A = { [0] = bottom.Layer },
            D = { [0] = 1 }
        };
        AssertEndUpdateClearsOwnedDamage(bus, libraryBase, endComplete);
        Assert.Equal(transparencyRegionBytes,
            SnapshotGraphicsRegion(bus, transparencyRegion));

        var disposeRegion = new M68kCpuState { A = { [0] = callerClipRegion } };
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.DisposeRegion),
            disposeRegion));
        FreeGuestMemory(bus, rectangle,
            (uint)GraphicsLayouts.RectangleSize);
        DeleteLayer(bus, libraryBase, top.Layer);
        DeleteLayer(bus, libraryBase, bottom.Layer);
        var disposeTransparencyRegion = new M68kCpuState
        {
            A = { [0] = transparencyRegion }
        };
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.DisposeRegion),
            disposeTransparencyRegion));
        DisposeLayerInfo(bus, libraryBase, layerInfo);
        FreeBitMap(bus, displayBitMap);
    }

    [Theory]
    [InlineData(false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false)]
    [InlineData(true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false)]
    [InlineData(false, true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false)]
    [InlineData(true, true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false)]
    [InlineData(false, true, true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false)]
    [InlineData(true, true, true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false)]
    [InlineData(true, true, true, true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false)]
    [InlineData(true, true, true, true, true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false)]
    [InlineData(true, true, true, true, true, true, false, false, false, false, false, false, false, false, false, false, false, false, false, false)]
    [InlineData(true, true, true, true, true, true, true, false, false, false, false, false, false, false, false, false, false, false, false, false)]
    [InlineData(true, true, true, true, true, true, true, false, true, false, false, false, false, false, false, false, false, false, false, false)]
    [InlineData(true, true, true, true, true, true, true, true, false, false, false, false, false, false, false, false, false, false, false, false)]
    [InlineData(true, true, true, true, true, true, true, true, false, true, false, false, false, false, false, false, false, false, false, false)]
    [InlineData(true, true, true, true, true, true, true, true, false, true, true, false, false, false, false, false, false, false, false, false)]
    [InlineData(true, true, true, true, true, true, true, true, false, true, false, true, false, false, false, false, false, false, false, false)]
    [InlineData(true, true, true, true, true, true, true, true, false, true, false, false, true, false, false, false, false, false, false, false)]
    [InlineData(true, true, true, true, true, true, true, true, false, true, false, false, false, true, false, false, false, false, false, false)]
    [InlineData(true, true, true, true, true, true, true, true, false, true, false, false, false, false, true, false, false, false, false, false)]
    [InlineData(true, true, true, true, true, true, true, true, false, true, false, false, false, false, false, true, false, false, false, false)]
    [InlineData(true, true, true, true, true, true, true, true, false, true, false, false, false, false, false, false, true, false, false, false)]
    [InlineData(true, true, true, true, true, true, true, true, false, true, false, false, false, false, false, false, false, true, false, false)]
    [InlineData(true, true, true, true, true, true, true, true, false, true, false, false, false, false, false, false, false, false, true, false)]
    [InlineData(true, true, true, true, true, true, true, true, false, true, false, false, false, false, false, false, false, false, false, true)]
    [InlineData(true, true, true, true, true, true, true, true, false, true, false, false, false, false, false, false, false, true, true, false)]
    [InlineData(true, true, true, true, true, true, true, true, false, true, false, false, false, false, false, false, true, false, false, true)]
    [InlineData(true, true, true, true, true, true, true, true, false, true, false, false, false, false, false, false, false, true, false, true)]
    [InlineData(true, true, true, true, true, true, true, true, false, true, false, false, false, false, false, false, true, true, true, true)]
    [InlineData(true, true, true, true, true, true, true, true, false, true, true, false, false, false, false, false, true, true, true, true)]
    public void GuestBackfillDeleteUnionsPriorDamageAcrossRemainingCover(
        bool retainFrontCover, bool retainPriorGrowthDamage,
        bool overlapGrowthDamageWithExposure,
        bool moveCoverAfterPartialRefresh,
        bool deleteCoverAfterPartialRefresh,
        bool shrinkLayerAfterCoverMutation,
        bool regrowLayerAfterShrink,
        bool shrinkAndRegrowBottomEdge,
        bool replaceCallerClipAfterMutations,
        bool replaceCallerClipAfterBottomResize,
        bool splitReplacementClip,
        bool emptyReplacementClipAfterBottomResize,
        bool disjointReplacementClipAfterBottomResize,
        bool includeDisjointReplacementPixelAfterBottomResize,
        bool adjacentReplacementClipAfterBottomResize,
        bool twoPixelReplacementClipAfterBottomResize,
        bool pastRightEdgeReplacementClipAfterBottomResize,
        bool negativeXReplacementClipAfterBottomResize,
        bool negativeYReplacementClipAfterBottomResize,
        bool topAndBottomYReplacementClipAfterBottomResize)
    {
        using var machine = CreateMachine();
        var boot = new AmigaBootController(machine);
        boot.StartBootFromDisk(CreateBootableDisk());
        var bus = machine.Bus;
        var libraryBase = boot.CopperStartLayersLibraryBase;
        var execBase = AmigaKickstartHost.ExecLibraryBase;
        var layerInfo = NewLayerInfo(bus, libraryBase);
        Assert.False(overlapGrowthDamageWithExposure &&
            !retainPriorGrowthDamage);
        Assert.False(moveCoverAfterPartialRefresh &&
            (!retainFrontCover || !overlapGrowthDamageWithExposure));
        Assert.False(deleteCoverAfterPartialRefresh &&
            !moveCoverAfterPartialRefresh);
        Assert.False(shrinkLayerAfterCoverMutation &&
            !deleteCoverAfterPartialRefresh);
        Assert.False(regrowLayerAfterShrink &&
            !shrinkLayerAfterCoverMutation);
        Assert.False(shrinkAndRegrowBottomEdge &&
            !regrowLayerAfterShrink);
        Assert.False(replaceCallerClipAfterMutations &&
            !regrowLayerAfterShrink);
        Assert.False(replaceCallerClipAfterBottomResize &&
            !shrinkAndRegrowBottomEdge);
        Assert.False(splitReplacementClip &&
            !replaceCallerClipAfterBottomResize);
        Assert.False(emptyReplacementClipAfterBottomResize &&
            !replaceCallerClipAfterBottomResize);
        Assert.False(emptyReplacementClipAfterBottomResize &&
            splitReplacementClip);
        Assert.False(disjointReplacementClipAfterBottomResize &&
            !replaceCallerClipAfterBottomResize);
        Assert.False(disjointReplacementClipAfterBottomResize &&
            splitReplacementClip);
        Assert.False(disjointReplacementClipAfterBottomResize &&
            emptyReplacementClipAfterBottomResize);
        Assert.False(includeDisjointReplacementPixelAfterBottomResize &&
            !replaceCallerClipAfterBottomResize);
        Assert.False(includeDisjointReplacementPixelAfterBottomResize &&
            (splitReplacementClip || emptyReplacementClipAfterBottomResize ||
                disjointReplacementClipAfterBottomResize));
        Assert.False(adjacentReplacementClipAfterBottomResize &&
            !replaceCallerClipAfterBottomResize);
        Assert.False(adjacentReplacementClipAfterBottomResize &&
            (splitReplacementClip || emptyReplacementClipAfterBottomResize ||
                disjointReplacementClipAfterBottomResize ||
                includeDisjointReplacementPixelAfterBottomResize));
        Assert.False(twoPixelReplacementClipAfterBottomResize &&
            !replaceCallerClipAfterBottomResize);
        Assert.False(twoPixelReplacementClipAfterBottomResize &&
            (splitReplacementClip || emptyReplacementClipAfterBottomResize ||
                disjointReplacementClipAfterBottomResize ||
                includeDisjointReplacementPixelAfterBottomResize ||
                adjacentReplacementClipAfterBottomResize));
        Assert.False(pastRightEdgeReplacementClipAfterBottomResize &&
            !replaceCallerClipAfterBottomResize);
        Assert.False(pastRightEdgeReplacementClipAfterBottomResize &&
            (emptyReplacementClipAfterBottomResize ||
                disjointReplacementClipAfterBottomResize ||
                includeDisjointReplacementPixelAfterBottomResize ||
                adjacentReplacementClipAfterBottomResize ||
                twoPixelReplacementClipAfterBottomResize));
        Assert.False(negativeXReplacementClipAfterBottomResize &&
            !replaceCallerClipAfterBottomResize);
        Assert.False(negativeXReplacementClipAfterBottomResize &&
            (emptyReplacementClipAfterBottomResize ||
                disjointReplacementClipAfterBottomResize ||
                includeDisjointReplacementPixelAfterBottomResize ||
                adjacentReplacementClipAfterBottomResize ||
                twoPixelReplacementClipAfterBottomResize));
        Assert.False(negativeYReplacementClipAfterBottomResize &&
            !replaceCallerClipAfterBottomResize);
        Assert.False(negativeYReplacementClipAfterBottomResize &&
            (emptyReplacementClipAfterBottomResize ||
                disjointReplacementClipAfterBottomResize ||
                includeDisjointReplacementPixelAfterBottomResize ||
                adjacentReplacementClipAfterBottomResize ||
                twoPixelReplacementClipAfterBottomResize));
        Assert.False(topAndBottomYReplacementClipAfterBottomResize &&
            !replaceCallerClipAfterBottomResize);
        Assert.False(topAndBottomYReplacementClipAfterBottomResize &&
            (emptyReplacementClipAfterBottomResize ||
                disjointReplacementClipAfterBottomResize ||
                includeDisjointReplacementPixelAfterBottomResize ||
                adjacentReplacementClipAfterBottomResize ||
                twoPixelReplacementClipAfterBottomResize));
        var negativeCornerReplacementClipAfterBottomResize =
            negativeXReplacementClipAfterBottomResize &&
            negativeYReplacementClipAfterBottomResize;
        var positiveCornerReplacementClipAfterBottomResize =
            pastRightEdgeReplacementClipAfterBottomResize &&
            topAndBottomYReplacementClipAfterBottomResize;
        var allEdgeReplacementClipAfterBottomResize =
            negativeXReplacementClipAfterBottomResize &&
            topAndBottomYReplacementClipAfterBottomResize;
        var oppositeOverhangFragmentsAfterBottomResize =
            negativeXReplacementClipAfterBottomResize &&
            negativeYReplacementClipAfterBottomResize &&
            pastRightEdgeReplacementClipAfterBottomResize &&
            topAndBottomYReplacementClipAfterBottomResize;
        var repeatPartialUpdateForOppositeOverhangs =
            oppositeOverhangFragmentsAfterBottomResize &&
            splitReplacementClip;
        var displayBitMap = AllocatePlanarBitMap(bus, 32, 20);
        var rectangle = AllocateGuestMemory(
            bus, (uint)GraphicsLayouts.RectangleSize);
        var transparencyRegionResult = new M68kCpuState();
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.NewRegion),
            transparencyRegionResult));
        var transparencyRegion = transparencyRegionResult.D[0];
        Assert.NotEqual(0u, transparencyRegion);
        WriteRectangleBounds(bus, rectangle, 10, 8, 10, 8);
        var addTransparentPixel = new M68kCpuState
        {
            A = { [0] = transparencyRegion, [1] = rectangle }
        };
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.OrRectRegion),
            addTransparentPixel));
        Assert.Equal(1u, addTransparentPixel.D[0]);
        var transparencyRegionBytes = SnapshotGraphicsRegion(
            bus, transparencyRegion);
        var transparencyTags = AllocateGuestMemory(bus, 16);
        bus.WriteLong(transparencyTags,
            (uint)LayerCreationTag.TransparentRegion);
        bus.WriteLong(transparencyTags + 4, transparencyRegion);
        bus.WriteLong(transparencyTags + 8, 0);
        bus.WriteLong(transparencyTags + 12, 0);
        var createBottom = new M68kCpuState
        {
            A =
            {
                [0] = layerInfo,
                [1] = displayBitMap,
                [2] = transparencyTags
            },
            D =
            {
                [0] = 0,
                [1] = 0,
                [2] = 23,
                [3] = 15,
                [4] = (uint)LayerCreationFlags.Simple
            }
        };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.CreateUpfrontLayerTagList),
            createBottom));
        Assert.NotEqual(0u, createBottom.D[0]);
        var bottom = new LayerSurface(
            displayBitMap,
            createBottom.D[0],
            ReadLayerRastPort(bus, createBottom.D[0]),
            ReadLayerClipRect(bus, createBottom.D[0]));
        FreeGuestMemory(bus, transparencyTags, 16);
        var occluderTransparencyResult = new M68kCpuState();
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.NewRegion),
            occluderTransparencyResult));
        var occluderTransparencyRegion = occluderTransparencyResult.D[0];
        Assert.NotEqual(0u, occluderTransparencyRegion);
        // TransparentRegion coordinates are layer-local. In the overlap case,
        // (1,4) maps to screen (24,8) for the occluder at (23,4).
        var occluderTransparentPixel = overlapGrowthDamageWithExposure
            ? (X: 24, Y: 8)
            : (X: 9, Y: 8);
        WriteRectangleBounds(
            bus, rectangle,
            overlapGrowthDamageWithExposure ? (short)1 : (short)5,
            4,
            overlapGrowthDamageWithExposure ? (short)1 : (short)5,
            4);
        var addTransparentOccluderPixel = new M68kCpuState
        {
            A = { [0] = occluderTransparencyRegion, [1] = rectangle }
        };
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.OrRectRegion),
            addTransparentOccluderPixel));
        Assert.Equal(1u, addTransparentOccluderPixel.D[0]);
        var occluderTransparencyBytes = SnapshotGraphicsRegion(
            bus, occluderTransparencyRegion);
        var occluderTransparencyTags = AllocateGuestMemory(bus, 16);
        bus.WriteLong(occluderTransparencyTags,
            (uint)LayerCreationTag.TransparentRegion);
        bus.WriteLong(occluderTransparencyTags + 4,
            occluderTransparencyRegion);
        bus.WriteLong(occluderTransparencyTags + 8, 0);
        bus.WriteLong(occluderTransparencyTags + 12, 0);
        var createTop = new M68kCpuState
        {
            A =
            {
                [0] = layerInfo,
                [1] = displayBitMap,
                [2] = occluderTransparencyTags
            },
            D =
            {
                [0] = overlapGrowthDamageWithExposure ? 23u : 4u,
                [1] = 4,
                [2] = overlapGrowthDamageWithExposure ? 27u : 15u,
                [3] = 11,
                [4] = (uint)LayerCreationFlags.Simple
            }
        };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.CreateUpfrontLayerTagList),
            createTop));
        Assert.NotEqual(0u, createTop.D[0]);
        var deletedOccluder = new LayerSurface(
            displayBitMap,
            createTop.D[0],
            ReadLayerRastPort(bus, createTop.D[0]),
            ReadLayerClipRect(bus, createTop.D[0]));
        FreeGuestMemory(bus, occluderTransparencyTags, 16);
        LayerSurface? survivingFrontCover = retainFrontCover
            ? CreateLayerOnBitMaps(
                bus, libraryBase, layerInfo, displayBitMap, 0,
                LayerCreationFlags.Simple,
                overlapGrowthDamageWithExposure ? (short)23 : (short)4,
                4,
                overlapGrowthDamageWithExposure ? (short)23 : (short)8,
                overlapGrowthDamageWithExposure ? (short)7 : (short)11)
            : null;
        var survivingFrontCoverDeleted = false;
        var layerMemory = new LayersTestGuestMemory(bus);
        if (retainPriorGrowthDamage)
        {
            var seedGrowthDamage = new M68kCpuState
            {
                A = { [1] = bottom.Layer },
                D = { [0] = 1, [1] = 1 }
            };
            Assert.True(InvokeHostTrap(
                bus, Lvo(libraryBase, LayersLvo.SizeLayer), seedGrowthDamage));
            Assert.Equal(1u, seedGrowthDamage.D[0]);
            Assert.True(LayersLayerCodec.ReadDamageList(
                ref layerMemory, APTR.FromPointer(bottom.Layer)).IsNotNull);
        }

        var regionResult = new M68kCpuState();
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.NewRegion),
            regionResult));
        var callerClipRegion = regionResult.D[0];
        Assert.NotEqual(0u, callerClipRegion);
        var hiddenCallerPixel = overlapGrowthDamageWithExposure
            ? (X: 23, Y: 6)
            : (X: 7, Y: 7);
        var newlyExposedCallerPixel = overlapGrowthDamageWithExposure
            ? (X: 24, Y: 6)
            : (X: 11, Y: 7);
        var selectedCallerPixel = retainFrontCover
            ? newlyExposedCallerPixel
            : hiddenCallerPixel;
        WriteRectangleBounds(
            bus, rectangle,
            checked((short)selectedCallerPixel.X),
            checked((short)selectedCallerPixel.Y),
            checked((short)selectedCallerPixel.X),
            checked((short)selectedCallerPixel.Y));
        var addClip = new M68kCpuState
        {
            A = { [0] = callerClipRegion, [1] = rectangle }
        };
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.OrRectRegion),
            addClip));
        Assert.Equal(1u, addClip.D[0]);
        if (retainFrontCover)
        {
            WriteRectangleBounds(
                bus, rectangle,
                checked((short)hiddenCallerPixel.X),
                checked((short)hiddenCallerPixel.Y),
                checked((short)hiddenCallerPixel.X),
                checked((short)hiddenCallerPixel.Y));
            var addClipAtCoveredPixel = new M68kCpuState
            {
                A = { [0] = callerClipRegion, [1] = rectangle }
            };
            Assert.True(InvokeHostTrap(
                bus,
                Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                    (int)GraphicsLvo.OrRectRegion),
                addClipAtCoveredPixel));
            Assert.Equal(1u, addClipAtCoveredPixel.D[0]);
        }
        WriteRectangleBounds(
            bus, rectangle,
            checked((short)occluderTransparentPixel.X),
            checked((short)occluderTransparentPixel.Y),
            checked((short)occluderTransparentPixel.X),
            checked((short)occluderTransparentPixel.Y));
        var addClipAtOccluderTransparentPixel = new M68kCpuState
        {
            A = { [0] = callerClipRegion, [1] = rectangle }
        };
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.OrRectRegion),
            addClipAtOccluderTransparentPixel));
        Assert.Equal(1u, addClipAtOccluderTransparentPixel.D[0]);
        WriteRectangleBounds(bus, rectangle, 10, 8, 10, 8);
        var addClipAtTransparentPixel = new M68kCpuState
        {
            A = { [0] = callerClipRegion, [1] = rectangle }
        };
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.OrRectRegion),
            addClipAtTransparentPixel));
        Assert.Equal(1u, addClipAtTransparentPixel.D[0]);
        var callerClipBytes = SnapshotGraphicsRegion(bus, callerClipRegion);
        var installClip = new M68kCpuState
        {
            A = { [0] = bottom.Layer, [1] = callerClipRegion }
        };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.InstallClipRegion), installClip));
        Assert.Equal(0u, installClip.D[0]);

        var hookAuthority = new Fixture.BorrowAuthority();
        var hookCode = hookAuthority.Allocate(bus, 0x1000);
        var hookMarker = hookAuthority.Allocate(bus, sizeof(uint));
        bus.WriteLong(hookMarker.Raw, 0);
        var hook = hookAuthority.Hook(bus, hookCode.Raw);
        const uint hookMarkerValue = 0xD311E7E0;
        var hookProgram = new Fixture.GuestProgram(bus, hookCode.Raw);
        hookProgram.StoreLong(hookMarker.Raw, hookMarkerValue);
        hookProgram.Return();
        var installHook = new M68kCpuState
        {
            A = { [0] = bottom.Layer, [1] = hook.Raw }
        };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.InstallLayerHook), installHook));

        var callerCode = hookAuthority.Allocate(bus, 0x2000);
        var userStack = Fixture.GuestStack.Allocate(bus);
        var supervisorStack = Fixture.GuestStack.Allocate(bus);
        var actor = Fixture.CurrentTask(bus);
        Fixture.SetTaskStack(bus, actor, userStack);
        var caller = new Fixture.GuestProgram(bus, callerCode.Raw, 0x2000);
        caller.MoveAddress(6, libraryBase);
        caller.MoveAddress(0, deletedOccluder.Layer);
        var afterDelete = caller.Call(Lvo(libraryBase, LayersLvo.DeleteLayer));
        caller.Park();

        Fixture.StartRequester(
            machine, callerCode.Raw, userStack, supervisorStack);
        var initialBitmap = SnapshotPlanarBitMap(bus, displayBitMap);
        var firstCycle = machine.Cpu.State.Cycles;
        const long cycleBudget = 4_000_000;
        var instructionCount = 0;
        var lastGuestHookCycle = -1L;
        var guestHookEntries = 0;
        var callbackMessages = new List<LayerBackfillMessage>();
        void ObserveGuestHookEntry()
        {
            var state = machine.Cpu.State;
            if (state.ProgramCounter != hookCode.Raw ||
                state.Cycles == lastGuestHookCycle) return;
            lastGuestHookCycle = state.Cycles;
            guestHookEntries++;
            Assert.Equal(hook.Raw, state.A[0]);
            Assert.NotEqual(0u, state.A[1]);
            Assert.Equal(bottom.RastPort, state.A[2]);
            var messageMemory = new LayersTestGuestMemory(bus);
            Assert.True(LayersHookMessageCodec.TryRead(
                ref messageMemory, APTR.FromPointer(state.A[1]),
                out LayerBackfillMessage message));
            callbackMessages.Add(message);
        }

        void StepUntil(uint targetPc)
        {
            while (machine.Cpu.State.ProgramCounter != targetPc)
            {
                ObserveGuestHookEntry();
                Assert.False(machine.Cpu.State.Halted);
                Assert.True(instructionCount < 40_000);
                var result = boot.ContinueCopperStartRuntimeUntilCycle(
                    firstCycle + cycleBudget, maxInstructions: 1);
                instructionCount += result.InstructionsExecuted;
                Assert.Equal(1, result.InstructionsExecuted);
            }
            ObserveGuestHookEntry();
        }

        HashSet<(int X, int Y)> CaptureClipPixels(uint head)
        {
            var pixels = new HashSet<(int X, int Y)>();
            for (var clipRect = head; clipRect != 0;
                 clipRect = ReadClipRectNext(bus, clipRect))
            {
                var bounds = LayersClipRectCodec.BoundsAddress(
                    APTR.FromPointer(clipRect)).Raw;
                var minX = unchecked((short)bus.ReadWord(bounds));
                var minY = unchecked((short)bus.ReadWord(bounds + 2));
                var maxX = unchecked((short)bus.ReadWord(bounds + 4));
                var maxY = unchecked((short)bus.ReadWord(bounds + 6));
                for (var y = Math.Max(0, (int)minY);
                     y <= Math.Min(19, (int)maxY); y++)
                    for (var x = Math.Max(0, (int)minX);
                         x <= Math.Min(31, (int)maxX); x++)
                        pixels.Add((x, y));
            }
            return pixels;
        }

        List<(uint Address, uint Next, uint BitMap, uint ObscuringLayer,
            short MinX, short MinY, short MaxX, short MaxY)>
            CaptureClipTopology(uint head)
        {
            var topology = new List<(uint Address, uint Next, uint BitMap,
                uint ObscuringLayer, short MinX, short MinY, short MaxX,
                short MaxY)>();
            for (var clipRect = head; clipRect != 0;
                 clipRect = ReadClipRectNext(bus, clipRect))
            {
                var bounds = LayersClipRectCodec.BoundsAddress(
                    APTR.FromPointer(clipRect)).Raw;
                topology.Add((clipRect,
                    ReadClipRectNext(bus, clipRect),
                    ReadClipRectBitMap(bus, clipRect),
                    ReadClipRectObscuringLayer(bus, clipRect),
                    unchecked((short)bus.ReadWord(bounds)),
                    unchecked((short)bus.ReadWord(bounds + 2)),
                    unchecked((short)bus.ReadWord(bounds + 4)),
                    unchecked((short)bus.ReadWord(bounds + 6))));
            }
            return topology;
        }

        void AssertDisplayPixels(HashSet<(int X, int Y)> expected)
        {
            for (var y = 0; y < 20; y++)
                for (var x = 0; x < 32; x++)
                    Assert.True(expected.Contains((x, y)) ==
                        ReadPlanarBitMapPixel(bus, displayBitMap, x, y),
                        $"Unexpected display pixel at ({x},{y}).");
        }

        HashSet<(int X, int Y)> CaptureRegionPixels(uint region)
        {
            var pixels = new HashSet<(int X, int Y)>();
            var regionAddress = APTR.FromPointer(region);
            var regionBounds = LayersRegionCodec.ReadBounds(
                ref layerMemory, regionAddress);
            var rectangle = LayersRegionCodec.ReadFirst(
                ref layerMemory, regionAddress);
            var visited = new HashSet<uint>();
            while (rectangle.IsNotNull)
            {
                Assert.True(visited.Add(rectangle.Raw),
                    "Damage Region rectangle chain must be acyclic.");
                var bounds = LayersRegionRectangleCodec.ReadBounds(
                    ref layerMemory, rectangle);
                for (var y = regionBounds.MinY + bounds.MinY;
                     y <= regionBounds.MinY + bounds.MaxY; y++)
                    for (var x = regionBounds.MinX + bounds.MinX;
                         x <= regionBounds.MinX + bounds.MaxX; x++)
                        pixels.Add((x, y));
                rectangle = LayersRegionRectangleCodec.ReadNext(
                    ref layerMemory, rectangle);
            }
            return pixels;
        }

        StepUntil(hookCode.Raw);
        Assert.True(boot.CopperStartLayersHasPendingCallbackForTest);
        Assert.Equal(hook.Raw, machine.Cpu.State.A[0]);
        Assert.Contains(callbackMessages, message =>
            message.Layer == APTR.FromPointer(bottom.Layer));
        StepUntil(afterDelete);
        Assert.True(guestHookEntries > 0);
        Assert.Equal(guestHookEntries, callbackMessages.Count);
        var callbackExposure = new HashSet<(int X, int Y)>();
        foreach (var message in callbackMessages.Where(message =>
            message.Layer == APTR.FromPointer(bottom.Layer)))
            for (var y = message.Bounds.MinY; y <= message.Bounds.MaxY; y++)
                for (var x = message.Bounds.MinX; x <= message.Bounds.MaxX; x++)
                    callbackExposure.Add((x, y));
        var expectedCallbackExposure = new HashSet<(int X, int Y)>();
        var exposureMinX = overlapGrowthDamageWithExposure
            ? 23
            : retainFrontCover ? 9 : 4;
        var exposureMaxX = overlapGrowthDamageWithExposure ? 24 : 15;
        for (var y = 4; y <= 11; y++)
            for (var x = exposureMinX; x <= exposureMaxX; x++)
                if ((!overlapGrowthDamageWithExposure || !retainFrontCover ||
                        x != 23 || y > 7) &&
                    (x != occluderTransparentPixel.X ||
                        y != occluderTransparentPixel.Y) &&
                    (x != 10 || y != 8))
                    expectedCallbackExposure.Add((x, y));
        Assert.True(expectedCallbackExposure.SetEquals(callbackExposure));
        Assert.Equal(hookMarkerValue, bus.ReadLong(hookMarker.Raw));
        Assert.False(boot.CopperStartLayersHasPendingCallbackForTest);
        Assert.Equal(initialBitmap, SnapshotPlanarBitMap(bus, displayBitMap));
        Assert.Equal(callerClipBytes,
            SnapshotGraphicsRegion(bus, callerClipRegion));
        Assert.Equal(transparencyRegionBytes,
            SnapshotGraphicsRegion(bus, transparencyRegion));
        Assert.Equal(occluderTransparencyBytes,
            SnapshotGraphicsRegion(bus, occluderTransparencyRegion));
        var expectedFront = survivingFrontCover is { } front
            ? APTR.FromPointer(front.Layer)
            : APTR.Null;
        Assert.Equal(expectedFront, LayersLayerCodec.ReadFront(
            ref layerMemory, APTR.FromPointer(bottom.Layer)));
        Assert.True(LayersLayerCodec.ReadBack(
            ref layerMemory, APTR.FromPointer(bottom.Layer)).IsNull);
        Assert.True(LayersLayerCodec.ReadDamageList(
            ref layerMemory, APTR.FromPointer(bottom.Layer)).IsNotNull);

        var beginPartial = new M68kCpuState { A = { [0] = bottom.Layer } };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.BeginUpdate), beginPartial));
        Assert.Equal(1u, beginPartial.D[0]);
        var selected = new HashSet<(int X, int Y)> { selectedCallerPixel };
        if (overlapGrowthDamageWithExposure)
            selected.Add(occluderTransparentPixel);
        Assert.True(selected.SetEquals(CaptureClipPixels(
            ReadLayerClipRect(bus, bottom.Layer))));
        var damage = LayersLayerCodec.ReadDamageList(
            ref layerMemory, APTR.FromPointer(bottom.Layer));
        var damageBytes = SnapshotGraphicsRegion(bus, damage.Raw);
        WriteRastPortForegroundPen(bus, bottom.RastPort, 1);
        foreach (var pixel in selected)
        {
            var writePixel = new M68kCpuState
            {
                A = { [1] = bottom.RastPort },
                D = { [0] = checked((uint)pixel.X), [1] = checked((uint)pixel.Y) }
            };
            Assert.True(InvokeHostTrap(
                bus,
                Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                    (int)GraphicsLvo.WritePixel),
                writePixel));
        }
        AssertDisplayPixels(selected);
        var endPartial = new M68kCpuState
        {
            A = { [0] = bottom.Layer }, D = { [0] = 0 }
        };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.EndUpdate), endPartial));
        Assert.Equal(damageBytes, SnapshotGraphicsRegion(bus, damage.Raw));

        if (moveCoverAfterPartialRefresh)
        {
            var movedCover = survivingFrontCover!.Value;
            var moveCallerCode = hookAuthority.Allocate(bus, 0x1000);
            var moveCaller = new Fixture.GuestProgram(
                bus, moveCallerCode.Raw, 0x1000);
            moveCaller.MoveAddress(6, libraryBase);
            moveCaller.MoveAddress(1, movedCover.Layer);
            moveCaller.MoveData(0, 1);
            moveCaller.MoveData(1, 0);
            var afterMove = moveCaller.Call(
                Lvo(libraryBase, LayersLvo.MoveLayer));
            moveCaller.Park();

            var bitmapBeforeMove = SnapshotPlanarBitMap(bus, displayBitMap);
            var callbacksBeforeMove = callbackMessages.Count;
            var hooksBeforeMove = guestHookEntries;
            Fixture.StartRequester(
                machine, moveCallerCode.Raw, userStack, supervisorStack);
            StepUntil(afterMove);
            Assert.True(guestHookEntries > hooksBeforeMove);
            Assert.Equal(guestHookEntries - hooksBeforeMove,
                callbackMessages.Count - callbacksBeforeMove);
            Assert.False(boot.CopperStartLayersHasPendingCallbackForTest);
            // MoveLayer makes the bottom pixel at (24,6) newly occluded by
            // the simple front cover, so that one visible bitmap bit is
            // restored to its pre-refresh value. No other pixel may change.
            var expectedBitmapAfterMove = (byte[])bitmapBeforeMove.Clone();
            var bytesPerRow = ReadBitMapBytesPerRow(bus, displayBitMap);
            var newlyOccludedPixelByte = checked(6 * bytesPerRow + (24 >> 3));
            expectedBitmapAfterMove[newlyOccludedPixelByte] &=
                unchecked((byte)~(0x80 >> (24 & 7)));
            Assert.False(ReadPlanarBitMapPixel(
                bus, displayBitMap, 24, 6));
            Assert.Equal(expectedBitmapAfterMove,
                SnapshotPlanarBitMap(bus, displayBitMap));

            var movedCoverExposure = new HashSet<(int X, int Y)>();
            foreach (var message in callbackMessages.Skip(callbacksBeforeMove)
                .Where(message =>
                    message.Layer == APTR.FromPointer(bottom.Layer)))
                for (var y = message.Bounds.MinY; y <= message.Bounds.MaxY; y++)
                    for (var x = message.Bounds.MinX;
                         x <= message.Bounds.MaxX; x++)
                        movedCoverExposure.Add((x, y));
            var expectedMovedCoverExposure = new HashSet<(int X, int Y)>();
            for (var y = 4; y <= 7; y++)
                expectedMovedCoverExposure.Add((23, y));
            Assert.True(expectedMovedCoverExposure.SetEquals(
                movedCoverExposure));
            Assert.Equal(callerClipBytes,
                SnapshotGraphicsRegion(bus, callerClipRegion));
            Assert.Equal(APTR.FromPointer(movedCover.Layer),
                LayersLayerCodec.ReadFront(
                    ref layerMemory, APTR.FromPointer(bottom.Layer)));

            var beginAfterMove = new M68kCpuState
            {
                A = { [0] = bottom.Layer }
            };
            Assert.True(InvokeHostTrap(
                bus, Lvo(libraryBase, LayersLvo.BeginUpdate), beginAfterMove));
            Assert.Equal(1u, beginAfterMove.D[0]);
            var selectedAfterMove = new HashSet<(int X, int Y)>
            {
                (23, 6), occluderTransparentPixel
            };
            Assert.True(selectedAfterMove.SetEquals(CaptureClipPixels(
                ReadLayerClipRect(bus, bottom.Layer))));
            var damageAfterMove = LayersLayerCodec.ReadDamageList(
                ref layerMemory, APTR.FromPointer(bottom.Layer));
            var damageAfterMoveBytes = SnapshotGraphicsRegion(
                bus, damageAfterMove.Raw);
            ClearPlanarBitMap(bus, displayBitMap);
            foreach (var pixel in selectedAfterMove)
            {
                var writePixel = new M68kCpuState
                {
                    A = { [1] = bottom.RastPort },
                    D =
                    {
                        [0] = checked((uint)pixel.X),
                        [1] = checked((uint)pixel.Y)
                    }
                };
                Assert.True(InvokeHostTrap(
                    bus,
                    Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                        (int)GraphicsLvo.WritePixel),
                    writePixel));
            }
            AssertDisplayPixels(selectedAfterMove);
            var endAfterMove = new M68kCpuState
            {
                A = { [0] = bottom.Layer }, D = { [0] = 0 }
            };
            Assert.True(InvokeHostTrap(
                bus, Lvo(libraryBase, LayersLvo.EndUpdate), endAfterMove));
            Assert.Equal(damageAfterMoveBytes,
                SnapshotGraphicsRegion(bus, damageAfterMove.Raw));

            if (deleteCoverAfterPartialRefresh)
            {
                var deleteCallerCode = hookAuthority.Allocate(bus, 0x1000);
                var deleteCaller = new Fixture.GuestProgram(
                    bus, deleteCallerCode.Raw, 0x1000);
                deleteCaller.MoveAddress(6, libraryBase);
                deleteCaller.MoveAddress(0, movedCover.Layer);
                var afterDeleteCover = deleteCaller.Call(
                    Lvo(libraryBase, LayersLvo.DeleteLayer));
                deleteCaller.Park();

                var callbacksBeforeDelete = callbackMessages.Count;
                var hooksBeforeDelete = guestHookEntries;
                Fixture.StartRequester(
                    machine, deleteCallerCode.Raw, userStack, supervisorStack);
                StepUntil(afterDeleteCover);
                Assert.True(guestHookEntries > hooksBeforeDelete);
                Assert.Equal(guestHookEntries - hooksBeforeDelete,
                    callbackMessages.Count - callbacksBeforeDelete);
                Assert.False(boot.CopperStartLayersHasPendingCallbackForTest);

                var deletedCoverExposure = new HashSet<(int X, int Y)>();
                foreach (var message in callbackMessages.Skip(callbacksBeforeDelete)
                    .Where(message =>
                        message.Layer == APTR.FromPointer(bottom.Layer)))
                    for (var y = message.Bounds.MinY; y <= message.Bounds.MaxY; y++)
                        for (var x = message.Bounds.MinX;
                             x <= message.Bounds.MaxX; x++)
                            deletedCoverExposure.Add((x, y));
                var expectedDeletedCoverExposure = new HashSet<(int X, int Y)>();
                for (var y = 4; y <= 7; y++)
                    expectedDeletedCoverExposure.Add((24, y));
                Assert.True(expectedDeletedCoverExposure.SetEquals(
                    deletedCoverExposure));
                Assert.True(LayersLayerCodec.ReadFront(
                    ref layerMemory, APTR.FromPointer(bottom.Layer)).IsNull);
                Assert.Equal(callerClipBytes,
                    SnapshotGraphicsRegion(bus, callerClipRegion));
                survivingFrontCoverDeleted = true;
            }

            if (shrinkLayerAfterCoverMutation)
            {
                var shrinkCallerCode = hookAuthority.Allocate(bus, 0x1000);
                var shrinkCaller = new Fixture.GuestProgram(
                    bus, shrinkCallerCode.Raw, 0x1000);
                shrinkCaller.MoveAddress(6, libraryBase);
                shrinkCaller.MoveAddress(1, bottom.Layer);
                shrinkCaller.MoveData(0, unchecked((uint)-1));
                shrinkCaller.MoveData(1, 0);
                var afterShrink = shrinkCaller.Call(
                    Lvo(libraryBase, LayersLvo.SizeLayer));
                shrinkCaller.Park();

                Fixture.StartRequester(
                    machine, shrinkCallerCode.Raw, userStack, supervisorStack);
                StepUntil(afterShrink);
                Assert.Equal(1u, machine.Cpu.State.D[0]);
                Assert.False(boot.CopperStartLayersHasPendingCallbackForTest);
                var shrunkenBounds = LayersLayerCodec.ReadBounds(
                    ref layerMemory, APTR.FromPointer(bottom.Layer));
                Assert.Equal((short)23, shrunkenBounds.MaxX);
                Assert.Equal((short)16, shrunkenBounds.MaxY);
                Assert.Equal(callerClipBytes,
                    SnapshotGraphicsRegion(bus, callerClipRegion));
                Assert.Equal(transparencyRegionBytes,
                    SnapshotGraphicsRegion(bus, transparencyRegion));
                Assert.Equal(occluderTransparencyBytes,
                    SnapshotGraphicsRegion(bus, occluderTransparencyRegion));

                if (regrowLayerAfterShrink)
                {
                    var expectedDamageAfterShrink = new HashSet<(int X, int Y)>(
                        expectedCallbackExposure);
                    for (var y = 0; y <= 16; y++)
                        expectedDamageAfterShrink.Add((24, y));
                    for (var x = 0; x <= 24; x++)
                        expectedDamageAfterShrink.Add((x, 16));
                    for (var y = 4; y <= 7; y++)
                        expectedDamageAfterShrink.Add((23, y));
                    var damageAfterShrink = LayersLayerCodec.ReadDamageList(
                        ref layerMemory, APTR.FromPointer(bottom.Layer));
                    var actualDamageAfterShrink = CaptureRegionPixels(
                        damageAfterShrink.Raw);
                    Assert.True(expectedDamageAfterShrink.SetEquals(
                        actualDamageAfterShrink),
                        $"Expected-only: {string.Join(", ", expectedDamageAfterShrink.Except(actualDamageAfterShrink).OrderBy(pixel => pixel.Y).ThenBy(pixel => pixel.X))}; " +
                        $"actual-only: {string.Join(", ", actualDamageAfterShrink.Except(expectedDamageAfterShrink).OrderBy(pixel => pixel.Y).ThenBy(pixel => pixel.X))}");
                    var damageAfterShrinkBytes = SnapshotGraphicsRegion(
                        bus, damageAfterShrink.Raw);
                    var beginAfterShrink = new M68kCpuState
                    {
                        A = { [0] = bottom.Layer }
                    };
                    Assert.True(InvokeHostTrap(
                        bus, Lvo(libraryBase, LayersLvo.BeginUpdate),
                        beginAfterShrink));
                    Assert.Equal(1u, beginAfterShrink.D[0]);
                    Assert.True(new HashSet<(int X, int Y)> { (23, 6) }
                        .SetEquals(CaptureClipPixels(
                            ReadLayerClipRect(bus, bottom.Layer))));
                    var endAfterShrinkPartial = new M68kCpuState
                    {
                        A = { [0] = bottom.Layer }, D = { [0] = 0 }
                    };
                    Assert.True(InvokeHostTrap(
                        bus, Lvo(libraryBase, LayersLvo.EndUpdate),
                        endAfterShrinkPartial));
                    Assert.Equal(damageAfterShrinkBytes,
                        SnapshotGraphicsRegion(bus, damageAfterShrink.Raw));

                    var growCallerCode = hookAuthority.Allocate(bus, 0x1000);
                    var growCaller = new Fixture.GuestProgram(
                        bus, growCallerCode.Raw, 0x1000);
                    growCaller.MoveAddress(6, libraryBase);
                    growCaller.MoveAddress(1, bottom.Layer);
                    growCaller.MoveData(0, 1);
                    growCaller.MoveData(1, 0);
                    var afterRegrow = growCaller.Call(
                        Lvo(libraryBase, LayersLvo.SizeLayer));
                    growCaller.Park();

                    Fixture.StartRequester(
                        machine, growCallerCode.Raw, userStack, supervisorStack);
                    StepUntil(afterRegrow);
                    Assert.Equal(1u, machine.Cpu.State.D[0]);
                    Assert.False(
                        boot.CopperStartLayersHasPendingCallbackForTest);
                    var regrownBounds = LayersLayerCodec.ReadBounds(
                        ref layerMemory, APTR.FromPointer(bottom.Layer));
                    Assert.Equal((short)24, regrownBounds.MaxX);
                    Assert.Equal((short)16, regrownBounds.MaxY);
                    Assert.Equal(callerClipBytes,
                        SnapshotGraphicsRegion(bus, callerClipRegion));
                    Assert.Equal(transparencyRegionBytes,
                        SnapshotGraphicsRegion(bus, transparencyRegion));
                    Assert.Equal(occluderTransparencyBytes,
                        SnapshotGraphicsRegion(bus, occluderTransparencyRegion));
                }
            }
        }

        uint replacementClipRegion = 0;
        byte[]? replacementClipBytes = null;
        var replacementClipPixels = new HashSet<(int X, int Y)>();
        if (replaceCallerClipAfterMutations ||
            replaceCallerClipAfterBottomResize)
        {
            var replacementRegionResult = new M68kCpuState();
            Assert.True(InvokeHostTrap(
                bus,
                Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                    (int)GraphicsLvo.NewRegion),
                replacementRegionResult));
            replacementClipRegion = replacementRegionResult.D[0];
            Assert.NotEqual(0u, replacementClipRegion);
            if (!emptyReplacementClipAfterBottomResize)
            {
                var replacementBounds = (MinX: 23, MinY: 6,
                    MaxX: 23, MaxY: 6);
                if (oppositeOverhangFragmentsAfterBottomResize)
                    replacementBounds = (-1, -1, 23, 6);
                else if (allEdgeReplacementClipAfterBottomResize)
                    replacementBounds = (-1, -1, 32, 20);
                else if (positiveCornerReplacementClipAfterBottomResize)
                    replacementBounds = (22, 15, 25, 25);
                else if (negativeCornerReplacementClipAfterBottomResize)
                    replacementBounds = (-1, -1, 23, 6);
                else if (disjointReplacementClipAfterBottomResize)
                    replacementBounds = (0, 0, 0, 0);
                else if (adjacentReplacementClipAfterBottomResize)
                    replacementBounds = (22, 6, 23, 6);
                else if (twoPixelReplacementClipAfterBottomResize)
                    replacementBounds = (22, 6, 24, 6);
                else if (pastRightEdgeReplacementClipAfterBottomResize)
                    replacementBounds = (22, 6, 25, 6);
                else if (negativeXReplacementClipAfterBottomResize)
                    replacementBounds = (-1, 6, 23, 6);
                else if (negativeYReplacementClipAfterBottomResize)
                    replacementBounds = (23, -1, 23, 4);
                else if (topAndBottomYReplacementClipAfterBottomResize)
                    replacementBounds = (23, -1, 23, 25);
                WriteRectangleBounds(
                    bus, rectangle,
                    checked((short)replacementBounds.MinX),
                    checked((short)replacementBounds.MinY),
                    checked((short)replacementBounds.MaxX),
                    checked((short)replacementBounds.MaxY));
                var addReplacementPixel = new M68kCpuState
                {
                    A = { [0] = replacementClipRegion, [1] = rectangle }
                };
                Assert.True(InvokeHostTrap(
                    bus,
                    Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                        (int)GraphicsLvo.OrRectRegion),
                    addReplacementPixel));
                Assert.Equal(1u, addReplacementPixel.D[0]);
            }
            if (oppositeOverhangFragmentsAfterBottomResize)
            {
                WriteRectangleBounds(bus, rectangle, 22, 15, 25, 25);
                var addOppositeOverhangFragment = new M68kCpuState
                {
                    A = { [0] = replacementClipRegion, [1] = rectangle }
                };
                Assert.True(InvokeHostTrap(
                    bus,
                    Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                        (int)GraphicsLvo.OrRectRegion),
                    addOppositeOverhangFragment));
                Assert.Equal(1u, addOppositeOverhangFragment.D[0]);
            }
            if (splitReplacementClip &&
                !repeatPartialUpdateForOppositeOverhangs)
            {
                WriteRectangleBounds(bus, rectangle, 24, 8, 24, 8);
                var addSecondReplacementPixel = new M68kCpuState
                {
                    A = { [0] = replacementClipRegion, [1] = rectangle }
                };
                Assert.True(InvokeHostTrap(
                    bus,
                    Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                        (int)GraphicsLvo.OrRectRegion),
                    addSecondReplacementPixel));
                Assert.Equal(1u, addSecondReplacementPixel.D[0]);
            }
            if (includeDisjointReplacementPixelAfterBottomResize)
            {
                WriteRectangleBounds(bus, rectangle, 0, 0, 0, 0);
                var addDisjointReplacementPixel = new M68kCpuState
                {
                    A = { [0] = replacementClipRegion, [1] = rectangle }
                };
                Assert.True(InvokeHostTrap(
                    bus,
                    Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                        (int)GraphicsLvo.OrRectRegion),
                    addDisjointReplacementPixel));
                Assert.Equal(1u, addDisjointReplacementPixel.D[0]);
            }
            replacementClipBytes = SnapshotGraphicsRegion(
                bus, replacementClipRegion);
            replacementClipPixels = CaptureRegionPixels(replacementClipRegion);
            if (emptyReplacementClipAfterBottomResize)
                Assert.Empty(replacementClipPixels);
            else if (disjointReplacementClipAfterBottomResize)
                Assert.True(new HashSet<(int X, int Y)> { (0, 0) }
                    .SetEquals(replacementClipPixels));
            else if (includeDisjointReplacementPixelAfterBottomResize)
                Assert.True(new HashSet<(int X, int Y)>
                    { (0, 0), (23, 6) }.SetEquals(replacementClipPixels));
            else if (oppositeOverhangFragmentsAfterBottomResize)
            {
                var expectedOppositeOverhangRegion = new HashSet<(int X, int Y)>(
                    Enumerable.Range(-1, 25).SelectMany(x =>
                        Enumerable.Range(-1, 8).Select(y => (x, y))));
                expectedOppositeOverhangRegion.UnionWith(
                    Enumerable.Range(22, 4).SelectMany(x =>
                        Enumerable.Range(15, 11).Select(y => (x, y))));
                Assert.True(expectedOppositeOverhangRegion.SetEquals(
                    replacementClipPixels));
            }
            else if (adjacentReplacementClipAfterBottomResize)
                Assert.True(new HashSet<(int X, int Y)>
                    { (22, 6), (23, 6) }.SetEquals(replacementClipPixels));
            else if (twoPixelReplacementClipAfterBottomResize)
                Assert.True(new HashSet<(int X, int Y)>
                    { (22, 6), (23, 6), (24, 6) }
                    .SetEquals(replacementClipPixels));
            else if (allEdgeReplacementClipAfterBottomResize)
                Assert.True(new HashSet<(int X, int Y)>(
                    Enumerable.Range(-1, 34).SelectMany(x =>
                        Enumerable.Range(-1, 22).Select(y => (x, y))))
                    .SetEquals(replacementClipPixels));
            else if (positiveCornerReplacementClipAfterBottomResize)
                Assert.True(new HashSet<(int X, int Y)>(
                    Enumerable.Range(22, 4).SelectMany(x =>
                        Enumerable.Range(15, 11).Select(y => (x, y))))
                    .SetEquals(replacementClipPixels));
            else if (pastRightEdgeReplacementClipAfterBottomResize)
                Assert.True(new HashSet<(int X, int Y)>
                    { (22, 6), (23, 6), (24, 6), (25, 6) }
                    .SetEquals(replacementClipPixels));
            else if (negativeCornerReplacementClipAfterBottomResize)
                Assert.True(new HashSet<(int X, int Y)>(
                    Enumerable.Range(-1, 25).SelectMany(x =>
                        Enumerable.Range(-1, 8).Select(y => (x, y))))
                    .SetEquals(replacementClipPixels));
            else if (negativeXReplacementClipAfterBottomResize)
                Assert.True(new HashSet<(int X, int Y)>(
                    Enumerable.Range(-1, 25).Select(x => (x, 6)))
                    .SetEquals(replacementClipPixels));
            else if (negativeYReplacementClipAfterBottomResize)
                Assert.True(new HashSet<(int X, int Y)>(
                    Enumerable.Range(-1, 6).Select(y => (23, y)))
                    .SetEquals(replacementClipPixels));
            else if (topAndBottomYReplacementClipAfterBottomResize)
                Assert.True(new HashSet<(int X, int Y)>(
                    Enumerable.Range(-1, 27).Select(y => (23, y)))
                    .SetEquals(replacementClipPixels));
        }

        if (replaceCallerClipAfterMutations)
        {
            var replaceClipCallerCode = hookAuthority.Allocate(bus, 0x1000);
            var replaceClipCaller = new Fixture.GuestProgram(
                bus, replaceClipCallerCode.Raw, 0x1000);
            replaceClipCaller.MoveAddress(6, libraryBase);
            replaceClipCaller.MoveAddress(0, bottom.Layer);
            replaceClipCaller.MoveAddress(1, replacementClipRegion);
            var afterReplaceClip = replaceClipCaller.Call(
                Lvo(libraryBase, LayersLvo.InstallClipRegion));
            replaceClipCaller.Park();

            Fixture.StartRequester(
                machine, replaceClipCallerCode.Raw, userStack, supervisorStack);
            StepUntil(afterReplaceClip);
            Assert.False(boot.CopperStartLayersHasPendingCallbackForTest);
            Assert.Equal(callerClipRegion, machine.Cpu.State.D[0]);
            Assert.Equal(callerClipBytes,
                SnapshotGraphicsRegion(bus, callerClipRegion));
            Assert.Equal(replacementClipBytes,
                SnapshotGraphicsRegion(bus, replacementClipRegion));

            var beginSelectedAfterReplace = new M68kCpuState
            {
                A = { [0] = bottom.Layer }
            };
            Assert.True(InvokeHostTrap(
                bus, Lvo(libraryBase, LayersLvo.BeginUpdate),
                beginSelectedAfterReplace));
            Assert.Equal(1u, beginSelectedAfterReplace.D[0]);
            Assert.True(new HashSet<(int X, int Y)> { (23, 6) }
                .SetEquals(CaptureClipPixels(
                    ReadLayerClipRect(bus, bottom.Layer))));
            var damageBeforeSelectedWrite = LayersLayerCodec.ReadDamageList(
                ref layerMemory, APTR.FromPointer(bottom.Layer));
            var damageBeforeSelectedWriteBytes = SnapshotGraphicsRegion(
                bus, damageBeforeSelectedWrite.Raw);
            WriteRastPortForegroundPen(bus, bottom.RastPort, 1);
            var selectedWrite = new M68kCpuState
            {
                A = { [1] = bottom.RastPort },
                D = { [0] = 23, [1] = 6 }
            };
            Assert.True(InvokeHostTrap(
                bus,
                Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                    (int)GraphicsLvo.WritePixel),
                selectedWrite));
            Assert.True(ReadPlanarBitMapPixel(
                bus, displayBitMap, 23, 6));
            var endSelectedAfterReplace = new M68kCpuState
            {
                A = { [0] = bottom.Layer }, D = { [0] = 0 }
            };
            Assert.True(InvokeHostTrap(
                bus, Lvo(libraryBase, LayersLvo.EndUpdate),
                endSelectedAfterReplace));
            Assert.Equal(damageBeforeSelectedWriteBytes,
                SnapshotGraphicsRegion(bus, damageBeforeSelectedWrite.Raw));
        }

        var restoreBackfill = new M68kCpuState
        {
            A =
            {
                [0] = bottom.Layer,
                [1] = LayerBackfillHook.NoBackfill
            }
        };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.InstallLayerHook), restoreBackfill));
        var removeClip = new M68kCpuState { A = { [0] = bottom.Layer } };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.InstallClipRegion), removeClip));
        Assert.Equal(replaceCallerClipAfterMutations
                ? replacementClipRegion
                : callerClipRegion,
            removeClip.D[0]);
        var expectedDamage = new HashSet<(int X, int Y)>(
            expectedCallbackExposure);
        if (retainPriorGrowthDamage)
        {
            for (var y = 0; y <= 16; y++) expectedDamage.Add((24, y));
            for (var x = 0; x <= 24; x++) expectedDamage.Add((x, 16));
        }
        var expectedDamageProjection = new HashSet<(int X, int Y)>(
            expectedDamage);
        if (moveCoverAfterPartialRefresh)
        {
            for (var y = 4; y <= 7; y++)
            {
                expectedDamage.Add((23, y));
                expectedDamageProjection.Add((23, y));
                if (!deleteCoverAfterPartialRefresh)
                    expectedDamageProjection.Remove((24, y));
            }
        }
        if (shrinkLayerAfterCoverMutation)
        {
            expectedDamage.RemoveWhere(pixel => pixel.X > 23);
            expectedDamageProjection.RemoveWhere(pixel => pixel.X > 23);
        }
        if (regrowLayerAfterShrink)
        {
            for (var y = 0; y <= 16; y++)
            {
                expectedDamage.Add((24, y));
                expectedDamageProjection.Add((24, y));
            }
        }
        if (shrinkAndRegrowBottomEdge)
        {
            var shrinkYCallerCode = hookAuthority.Allocate(bus, 0x1000);
            var shrinkYCaller = new Fixture.GuestProgram(
                bus, shrinkYCallerCode.Raw, 0x1000);
            shrinkYCaller.MoveAddress(6, libraryBase);
            shrinkYCaller.MoveAddress(1, bottom.Layer);
            shrinkYCaller.MoveData(0, 0);
            shrinkYCaller.MoveData(1, unchecked((uint)-1));
            var afterShrinkY = shrinkYCaller.Call(
                Lvo(libraryBase, LayersLvo.SizeLayer));
            shrinkYCaller.Park();
            Fixture.StartRequester(
                machine, shrinkYCallerCode.Raw, userStack, supervisorStack);
            StepUntil(afterShrinkY);
            Assert.Equal(1u, machine.Cpu.State.D[0]);
            Assert.False(boot.CopperStartLayersHasPendingCallbackForTest);
            var bottomShrunkenBounds = LayersLayerCodec.ReadBounds(
                ref layerMemory, APTR.FromPointer(bottom.Layer));
            Assert.Equal((short)24, bottomShrunkenBounds.MaxX);
            Assert.Equal((short)15, bottomShrunkenBounds.MaxY);

            var damageBeforeYShrink = LayersLayerCodec.ReadDamageList(
                ref layerMemory, APTR.FromPointer(bottom.Layer));
            Assert.True(expectedDamage.SetEquals(CaptureRegionPixels(
                damageBeforeYShrink.Raw)));
            var expectedProjectionAfterYShrink = new HashSet<(int X, int Y)>(
                expectedDamageProjection);
            expectedProjectionAfterYShrink.RemoveWhere(pixel => pixel.Y > 15);
            var damageBeforeYPartialBytes = SnapshotGraphicsRegion(
                bus, damageBeforeYShrink.Raw);
            var beginAfterYShrink = new M68kCpuState
            {
                A = { [0] = bottom.Layer }
            };
            Assert.True(InvokeHostTrap(
                bus, Lvo(libraryBase, LayersLvo.BeginUpdate),
                beginAfterYShrink));
            Assert.Equal(1u, beginAfterYShrink.D[0]);
            Assert.True(expectedProjectionAfterYShrink.SetEquals(
                CaptureClipPixels(ReadLayerClipRect(bus, bottom.Layer))));
            var endAfterYShrinkPartial = new M68kCpuState
            {
                A = { [0] = bottom.Layer }, D = { [0] = 0 }
            };
            Assert.True(InvokeHostTrap(
                bus, Lvo(libraryBase, LayersLvo.EndUpdate),
                endAfterYShrinkPartial));
            Assert.Equal(damageBeforeYPartialBytes,
                SnapshotGraphicsRegion(bus, damageBeforeYShrink.Raw));

            var growYCallerCode = hookAuthority.Allocate(bus, 0x1000);
            var growYCaller = new Fixture.GuestProgram(
                bus, growYCallerCode.Raw, 0x1000);
            growYCaller.MoveAddress(6, libraryBase);
            growYCaller.MoveAddress(1, bottom.Layer);
            growYCaller.MoveData(0, 0);
            growYCaller.MoveData(1, 1);
            var afterRegrowY = growYCaller.Call(
                Lvo(libraryBase, LayersLvo.SizeLayer));
            growYCaller.Park();
            Fixture.StartRequester(
                machine, growYCallerCode.Raw, userStack, supervisorStack);
            StepUntil(afterRegrowY);
            Assert.Equal(1u, machine.Cpu.State.D[0]);
            Assert.False(boot.CopperStartLayersHasPendingCallbackForTest);
            var bottomRegrownBounds = LayersLayerCodec.ReadBounds(
                ref layerMemory, APTR.FromPointer(bottom.Layer));
            Assert.Equal((short)24, bottomRegrownBounds.MaxX);
            Assert.Equal((short)16, bottomRegrownBounds.MaxY);
            var damageAfterYRegrow = LayersLayerCodec.ReadDamageList(
                ref layerMemory, APTR.FromPointer(bottom.Layer));
            Assert.True(expectedDamage.SetEquals(CaptureRegionPixels(
                damageAfterYRegrow.Raw)));
        }
        if (replaceCallerClipAfterBottomResize)
        {
            var reinstallCallerClip = new M68kCpuState
            {
                A = { [0] = bottom.Layer, [1] = callerClipRegion }
            };
            Assert.True(InvokeHostTrap(
                bus, Lvo(libraryBase, LayersLvo.InstallClipRegion),
                reinstallCallerClip));
            Assert.Equal(0u, reinstallCallerClip.D[0]);

            var replaceAfterBottomCode = hookAuthority.Allocate(bus, 0x1000);
            var replaceAfterBottom = new Fixture.GuestProgram(
                bus, replaceAfterBottomCode.Raw, 0x1000);
            replaceAfterBottom.MoveAddress(6, libraryBase);
            replaceAfterBottom.MoveAddress(0, bottom.Layer);
            replaceAfterBottom.MoveAddress(1, replacementClipRegion);
            var afterReplaceBottomClip = replaceAfterBottom.Call(
                Lvo(libraryBase, LayersLvo.InstallClipRegion));
            replaceAfterBottom.Park();
            Fixture.StartRequester(
                machine, replaceAfterBottomCode.Raw,
                userStack, supervisorStack);
            StepUntil(afterReplaceBottomClip);
            Assert.False(boot.CopperStartLayersHasPendingCallbackForTest);
            Assert.Equal(callerClipRegion, machine.Cpu.State.D[0]);
            Assert.Equal(callerClipBytes,
                SnapshotGraphicsRegion(bus, callerClipRegion));
            Assert.Equal(replacementClipBytes,
                SnapshotGraphicsRegion(bus, replacementClipRegion));

            if (oppositeOverhangFragmentsAfterBottomResize)
            {
                var layerAddress = APTR.FromPointer(bottom.Layer);
                var publishedClipHeadBeforeFailure =
                    ReadLayerClipRect(bus, bottom.Layer);
                var publishedClipTopologyBeforeFailure =
                    CaptureClipTopology(publishedClipHeadBeforeFailure);
                Assert.NotEmpty(publishedClipTopologyBeforeFailure);
                var publishedClipPixelsBeforeFailure = CaptureClipPixels(
                    publishedClipHeadBeforeFailure);
                var damageBeforeFailedProjection = LayersLayerCodec.ReadDamageList(
                    ref layerMemory, layerAddress);
                var damageBeforeFailedProjectionBytes = SnapshotGraphicsRegion(
                    bus, damageBeforeFailedProjection.Raw);
                var layerFlagsBeforeFailedProjection =
                    LayersLayerCodec.ReadFlags(ref layerMemory, layerAddress);
                Assert.Equal((LayerFlags)0,
                    layerFlagsBeforeFailedProjection & LayerFlags.Updating);
                Assert.True(LayersLayerCodec.ReadSaveClipRects(
                    ref layerMemory, layerAddress).IsNull);

                var allocationsPerSuccessfulProjection = 0;
                using (var countProjectionAllocations =
                    boot.BeginCopperStartLayersMemoryAllocationFaultForTest(0))
                {
                    var beginCountedProjection = new M68kCpuState
                    {
                        A = { [0] = bottom.Layer }
                    };
                    Assert.True(InvokeHostTrap(
                        bus, Lvo(libraryBase, LayersLvo.BeginUpdate),
                        beginCountedProjection));
                    Assert.Equal(1u, beginCountedProjection.D[0]);
                    allocationsPerSuccessfulProjection =
                        countProjectionAllocations.Count;
                    Assert.True(allocationsPerSuccessfulProjection >= 3);
                    Assert.Equal(1, countProjectionAllocations.FreeCount);
                }

                var endCountedProjection = new M68kCpuState
                {
                    A = { [0] = bottom.Layer }, D = { [0] = 0 }
                };
                Assert.True(InvokeHostTrap(
                    bus, Lvo(libraryBase, LayersLvo.EndUpdate),
                    endCountedProjection));
                Assert.Equal(publishedClipHeadBeforeFailure,
                    ReadLayerClipRect(bus, bottom.Layer));
                Assert.Equal(layerFlagsBeforeFailedProjection,
                    LayersLayerCodec.ReadFlags(ref layerMemory, layerAddress));
                Assert.Equal(replacementClipRegion,
                    LayersLayerCodec.ReadClipRegion(
                        ref layerMemory, layerAddress).Raw);
                Assert.Equal(damageBeforeFailedProjection.Raw,
                    LayersLayerCodec.ReadDamageList(
                        ref layerMemory, layerAddress).Raw);
                Assert.Equal(damageBeforeFailedProjectionBytes,
                    SnapshotGraphicsRegion(bus,
                        damageBeforeFailedProjection.Raw));

                // Fail every allocation in the projection build, including
                // the scratch array and each temporary ClipRect node.
                for (var failedAllocationOrdinal = 1;
                     failedAllocationOrdinal <= allocationsPerSuccessfulProjection;
                     failedAllocationOrdinal++)
                {
                    Assert.Equal(layerFlagsBeforeFailedProjection,
                        LayersLayerCodec.ReadFlags(ref layerMemory, layerAddress));
                    Assert.True(LayersLayerCodec.ReadSaveClipRects(
                        ref layerMemory, layerAddress).IsNull);
                    using (var failDamageClipAllocation =
                        boot.BeginCopperStartLayersMemoryAllocationFaultForTest(
                            failedAllocationOrdinal))
                    {
                        var beginWithFailedAllocation = new M68kCpuState
                        {
                            A = { [0] = bottom.Layer }
                        };
                        Assert.True(InvokeHostTrap(
                            bus, Lvo(libraryBase, LayersLvo.BeginUpdate),
                            beginWithFailedAllocation));
                        Assert.Equal(failedAllocationOrdinal,
                            failDamageClipAllocation.Count);
                        Assert.Equal(0u, beginWithFailedAllocation.D[0]);
                        // Every successful allocation before the failure must
                        // be released by the rollback path.
                        Assert.Equal(failedAllocationOrdinal - 1,
                            failDamageClipAllocation.FreeCount);
                    }

                    Assert.Equal(publishedClipHeadBeforeFailure,
                        ReadLayerClipRect(bus, bottom.Layer));
                    Assert.Equal(publishedClipTopologyBeforeFailure,
                        CaptureClipTopology(ReadLayerClipRect(
                            bus, bottom.Layer)));
                    Assert.True(publishedClipPixelsBeforeFailure.SetEquals(
                        CaptureClipPixels(ReadLayerClipRect(
                            bus, bottom.Layer))));
                    Assert.Equal(publishedClipHeadBeforeFailure,
                        LayersLayerCodec.ReadSaveClipRects(
                            ref layerMemory, layerAddress).Raw);
                    Assert.Equal(layerFlagsBeforeFailedProjection |
                            LayerFlags.Updating,
                        LayersLayerCodec.ReadFlags(
                            ref layerMemory, layerAddress));
                    Assert.Equal(ReadCurrentTask(bus, execBase),
                        ReadLayerLockOwner(bus, bottom.Layer));
                    Assert.Equal(replacementClipRegion,
                        LayersLayerCodec.ReadClipRegion(
                            ref layerMemory, layerAddress).Raw);
                    Assert.Equal(damageBeforeFailedProjection.Raw,
                        LayersLayerCodec.ReadDamageList(
                            ref layerMemory, layerAddress).Raw);
                    Assert.Equal(replacementClipBytes,
                        SnapshotGraphicsRegion(bus, replacementClipRegion));
                    Assert.Equal(damageBeforeFailedProjectionBytes,
                        SnapshotGraphicsRegion(bus,
                            damageBeforeFailedProjection.Raw));

                    var endFailedProjection = new M68kCpuState
                    {
                        A = { [0] = bottom.Layer }, D = { [0] = 0 }
                    };
                    Assert.True(InvokeHostTrap(
                        bus, Lvo(libraryBase, LayersLvo.EndUpdate),
                        endFailedProjection));
                    Assert.True(LayersLayerCodec.ReadSaveClipRects(
                        ref layerMemory, layerAddress).IsNull);
                    Assert.Equal(layerFlagsBeforeFailedProjection,
                        LayersLayerCodec.ReadFlags(ref layerMemory, layerAddress));
                    Assert.Equal(0u, ReadLayerLockOwner(bus, bottom.Layer));
                    Assert.Equal(publishedClipHeadBeforeFailure,
                        ReadLayerClipRect(bus, bottom.Layer));
                    Assert.Equal(replacementClipRegion,
                        LayersLayerCodec.ReadClipRegion(
                            ref layerMemory, layerAddress).Raw);
                    Assert.Equal(damageBeforeFailedProjection.Raw,
                        LayersLayerCodec.ReadDamageList(
                            ref layerMemory, layerAddress).Raw);
                    Assert.Equal(replacementClipBytes,
                        SnapshotGraphicsRegion(bus, replacementClipRegion));
                    Assert.Equal(damageBeforeFailedProjectionBytes,
                        SnapshotGraphicsRegion(bus,
                            damageBeforeFailedProjection.Raw));
                }
            }

            var beginSelectedAfterBottomReplace = new M68kCpuState
            {
                A = { [0] = bottom.Layer }
            };
            Assert.True(InvokeHostTrap(
                bus, Lvo(libraryBase, LayersLvo.BeginUpdate),
                beginSelectedAfterBottomReplace));
            Assert.Equal(1u, beginSelectedAfterBottomReplace.D[0]);
            var selectedAfterBottomReplace =
                new HashSet<(int X, int Y)>();
            if (!emptyReplacementClipAfterBottomResize &&
                !disjointReplacementClipAfterBottomResize)
            {
                if (oppositeOverhangFragmentsAfterBottomResize)
                {
                    selectedAfterBottomReplace.UnionWith(
                        expectedDamageProjection.Where(pixel =>
                            (pixel.X >= 0 && pixel.X <= 23 &&
                                pixel.Y >= 0 && pixel.Y <= 6) ||
                            (pixel.X >= 22 && pixel.X <= 24 &&
                                pixel.Y >= 15 && pixel.Y <= 16)));
                    Assert.NotEmpty(selectedAfterBottomReplace);
                }
                else if (allEdgeReplacementClipAfterBottomResize)
                {
                    selectedAfterBottomReplace.UnionWith(
                        expectedDamageProjection.Where(pixel =>
                            pixel.X >= 0 && pixel.X <= 24 &&
                            pixel.Y >= 0 && pixel.Y <= 16));
                    Assert.NotEmpty(selectedAfterBottomReplace);
                }
                else if (positiveCornerReplacementClipAfterBottomResize)
                {
                    selectedAfterBottomReplace.UnionWith(
                        expectedDamageProjection.Where(pixel =>
                            pixel.X >= 22 && pixel.X <= 25 &&
                            pixel.Y >= 15 && pixel.Y <= 25));
                    Assert.NotEmpty(selectedAfterBottomReplace);
                }
                else if (negativeCornerReplacementClipAfterBottomResize)
                {
                    selectedAfterBottomReplace.UnionWith(
                        expectedDamageProjection.Where(pixel =>
                            pixel.X >= 0 && pixel.X <= 23 &&
                            pixel.Y >= 0 && pixel.Y <= 6));
                    Assert.NotEmpty(selectedAfterBottomReplace);
                }
                else if (topAndBottomYReplacementClipAfterBottomResize)
                {
                    selectedAfterBottomReplace.UnionWith(
                        expectedDamageProjection.Where(pixel =>
                            pixel.X == 23));
                    Assert.NotEmpty(selectedAfterBottomReplace);
                }
                else if (negativeYReplacementClipAfterBottomResize)
                    selectedAfterBottomReplace.Add((23, 4));
                else
                    selectedAfterBottomReplace.Add((23, 6));
                if (splitReplacementClip &&
                    !repeatPartialUpdateForOppositeOverhangs)
                    selectedAfterBottomReplace.Add((24, 8));
                if (twoPixelReplacementClipAfterBottomResize)
                    selectedAfterBottomReplace.Add((24, 6));
                if (pastRightEdgeReplacementClipAfterBottomResize &&
                    !positiveCornerReplacementClipAfterBottomResize)
                    selectedAfterBottomReplace.Add((24, 6));
            }
            var actualSelectedAfterBottomReplace = CaptureClipPixels(
                ReadLayerClipRect(bus, bottom.Layer));
            if (oppositeOverhangFragmentsAfterBottomResize)
            {
                Assert.Contains((23, 4), actualSelectedAfterBottomReplace);
                Assert.Contains((23, 6), actualSelectedAfterBottomReplace);
                Assert.Contains((22, 16), actualSelectedAfterBottomReplace);
                Assert.Contains((24, 16), actualSelectedAfterBottomReplace);
                Assert.DoesNotContain((23, 10),
                    actualSelectedAfterBottomReplace);
            }
            Assert.True(selectedAfterBottomReplace.SetEquals(
                    actualSelectedAfterBottomReplace),
                $"Expected {string.Join(",", selectedAfterBottomReplace.OrderBy(pixel => pixel.Y).ThenBy(pixel => pixel.X))}; " +
                $"actual {string.Join(",", actualSelectedAfterBottomReplace.OrderBy(pixel => pixel.Y).ThenBy(pixel => pixel.X))}.");
            var selectedClipRectCount = 0;
            for (var clipRect = ReadLayerClipRect(bus, bottom.Layer);
                 clipRect != 0;
                 clipRect = ReadClipRectNext(bus, clipRect))
                selectedClipRectCount++;
            if (oppositeOverhangFragmentsAfterBottomResize)
            {
                Assert.True(selectedClipRectCount >= 2);
            }
            else if (allEdgeReplacementClipAfterBottomResize ||
                negativeCornerReplacementClipAfterBottomResize ||
                positiveCornerReplacementClipAfterBottomResize)
            {
                Assert.True(selectedClipRectCount > 0);
            }
            else
            {
                var expectedSelectedClipRectCount =
                    emptyReplacementClipAfterBottomResize ||
                        disjointReplacementClipAfterBottomResize
                        ? 0
                        : splitReplacementClip ? 2 : 1;
                if (topAndBottomYReplacementClipAfterBottomResize)
                {
                    expectedSelectedClipRectCount = 0;
                    int? previousSelectedY = null;
                    foreach (var selectedY in selectedAfterBottomReplace
                                 .Select(pixel => pixel.Y)
                                 .Distinct()
                                 .OrderBy(y => y))
                    {
                        if (previousSelectedY is null ||
                            selectedY != previousSelectedY.Value + 1)
                            expectedSelectedClipRectCount++;
                        previousSelectedY = selectedY;
                    }
                }
                Assert.Equal(expectedSelectedClipRectCount,
                    selectedClipRectCount);
            }
            var damageBeforeBottomSelectedWrite =
                LayersLayerCodec.ReadDamageList(
                    ref layerMemory, APTR.FromPointer(bottom.Layer));
            var damageBeforeBottomSelectedWriteBytes = SnapshotGraphicsRegion(
                bus, damageBeforeBottomSelectedWrite.Raw);
            if (disjointReplacementClipAfterBottomResize ||
                includeDisjointReplacementPixelAfterBottomResize ||
                adjacentReplacementClipAfterBottomResize ||
                twoPixelReplacementClipAfterBottomResize ||
                pastRightEdgeReplacementClipAfterBottomResize ||
                negativeXReplacementClipAfterBottomResize ||
                negativeYReplacementClipAfterBottomResize ||
                topAndBottomYReplacementClipAfterBottomResize)
            {
                var pendingDamagePixels = CaptureRegionPixels(
                    damageBeforeBottomSelectedWrite.Raw);
                Assert.NotEmpty(pendingDamagePixels);
                if (oppositeOverhangFragmentsAfterBottomResize)
                {
                    Assert.Contains((23, 4), pendingDamagePixels);
                    Assert.Contains((23, 6), pendingDamagePixels);
                    Assert.Contains((22, 16), pendingDamagePixels);
                    Assert.Contains((24, 16), pendingDamagePixels);
                    Assert.Contains((-1, -1), replacementClipPixels);
                    Assert.Contains((25, 25), replacementClipPixels);
                }
                else if (allEdgeReplacementClipAfterBottomResize)
                {
                    Assert.Contains((23, 4), pendingDamagePixels);
                    Assert.Contains((23, 6), pendingDamagePixels);
                    Assert.Contains((24, 16), pendingDamagePixels);
                    Assert.Contains((-1, -1), replacementClipPixels);
                    Assert.Contains((32, 20), replacementClipPixels);
                }
                else if (positiveCornerReplacementClipAfterBottomResize)
                {
                    Assert.DoesNotContain((25, 16), pendingDamagePixels);
                    Assert.DoesNotContain((24, 17), pendingDamagePixels);
                    Assert.Contains((21, 16), pendingDamagePixels);
                    Assert.Contains((23, 16), pendingDamagePixels);
                    Assert.Contains((24, 16), pendingDamagePixels);
                    Assert.Contains((22, 15), replacementClipPixels);
                    Assert.Contains((25, 25), replacementClipPixels);
                }
                else if (adjacentReplacementClipAfterBottomResize)
                {
                    Assert.DoesNotContain((22, 6), pendingDamagePixels);
                    Assert.Contains((23, 6), pendingDamagePixels);
                    Assert.Contains((22, 6), replacementClipPixels);
                }
                else if (twoPixelReplacementClipAfterBottomResize)
                {
                    Assert.DoesNotContain((22, 6), pendingDamagePixels);
                    Assert.Contains((23, 6), pendingDamagePixels);
                    Assert.Contains((24, 6), pendingDamagePixels);
                    Assert.Contains((22, 6), replacementClipPixels);
                }
                else if (pastRightEdgeReplacementClipAfterBottomResize)
                {
                    Assert.DoesNotContain((22, 6), pendingDamagePixels);
                    Assert.Contains((23, 6), pendingDamagePixels);
                    Assert.Contains((24, 6), pendingDamagePixels);
                    Assert.DoesNotContain((25, 6), pendingDamagePixels);
                    Assert.Contains((22, 6), replacementClipPixels);
                    Assert.Contains((25, 6), replacementClipPixels);
                }
                else if (negativeCornerReplacementClipAfterBottomResize)
                {
                    Assert.DoesNotContain((-1, -1), pendingDamagePixels);
                    Assert.DoesNotContain((-1, 4), pendingDamagePixels);
                    Assert.DoesNotContain((22, 6), pendingDamagePixels);
                    Assert.Contains((23, 4), pendingDamagePixels);
                    Assert.Contains((23, 6), pendingDamagePixels);
                    Assert.Contains((-1, -1), replacementClipPixels);
                    Assert.Contains((-1, 4), replacementClipPixels);
                    Assert.Contains((23, 6), replacementClipPixels);
                }
                else if (negativeXReplacementClipAfterBottomResize)
                {
                    Assert.DoesNotContain((-1, 6), pendingDamagePixels);
                    Assert.DoesNotContain((22, 6), pendingDamagePixels);
                    Assert.Contains((23, 6), pendingDamagePixels);
                    Assert.Contains((-1, 6), replacementClipPixels);
                    Assert.Contains((22, 6), replacementClipPixels);
                }
                else if (negativeYReplacementClipAfterBottomResize)
                {
                    Assert.DoesNotContain((23, -1), pendingDamagePixels);
                    Assert.DoesNotContain((23, 3), pendingDamagePixels);
                    Assert.Contains((23, 4), pendingDamagePixels);
                    Assert.Contains((23, -1), replacementClipPixels);
                    Assert.Contains((23, 3), replacementClipPixels);
                }
                else if (topAndBottomYReplacementClipAfterBottomResize)
                {
                    Assert.DoesNotContain((23, -1), pendingDamagePixels);
                    Assert.DoesNotContain((23, 17), pendingDamagePixels);
                    Assert.DoesNotContain((23, 25), pendingDamagePixels);
                    Assert.Contains((23, 6), pendingDamagePixels);
                    Assert.Contains((23, 16), pendingDamagePixels);
                    Assert.Contains((23, -1), replacementClipPixels);
                    Assert.Contains((23, 25), replacementClipPixels);
                }
                else
                {
                    Assert.DoesNotContain((0, 0), pendingDamagePixels);
                    Assert.Contains((0, 0), replacementClipPixels);
                }
            }
            if (emptyReplacementClipAfterBottomResize ||
                disjointReplacementClipAfterBottomResize ||
                includeDisjointReplacementPixelAfterBottomResize ||
                adjacentReplacementClipAfterBottomResize ||
                twoPixelReplacementClipAfterBottomResize ||
                pastRightEdgeReplacementClipAfterBottomResize ||
                negativeXReplacementClipAfterBottomResize ||
                negativeYReplacementClipAfterBottomResize ||
                topAndBottomYReplacementClipAfterBottomResize)
            {
                ClearPlanarBitMap(bus, displayBitMap);
                WriteRastPortForegroundPen(bus, bottom.RastPort, 1);
                var restrictedClipFill = new M68kCpuState
                {
                    A = { [1] = bottom.RastPort },
                    D =
                    {
                        [0] = 0, [1] = 0, [2] = 31, [3] = 19
                    }
                };
                Assert.True(InvokeHostTrap(
                    bus,
                    Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                        (int)GraphicsLvo.RectFill),
                    restrictedClipFill));
                AssertDisplayPixels(selectedAfterBottomReplace);
            }
            else
            {
                WriteRastPortForegroundPen(bus, bottom.RastPort, 1);
                foreach (var pixel in selectedAfterBottomReplace)
                {
                    var bottomSelectedWrite = new M68kCpuState
                    {
                        A = { [1] = bottom.RastPort },
                        D =
                        {
                            [0] = checked((uint)pixel.X),
                            [1] = checked((uint)pixel.Y)
                        }
                    };
                    Assert.True(InvokeHostTrap(
                        bus,
                        Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                            (int)GraphicsLvo.WritePixel),
                        bottomSelectedWrite));
                    Assert.True(ReadPlanarBitMapPixel(
                        bus, displayBitMap, pixel.X, pixel.Y));
                }
            }
            var endBottomSelected = new M68kCpuState
            {
                A = { [0] = bottom.Layer }, D = { [0] = 0 }
            };
            Assert.True(InvokeHostTrap(
                bus, Lvo(libraryBase, LayersLvo.EndUpdate),
                endBottomSelected));
            Assert.Equal(damageBeforeBottomSelectedWriteBytes,
                SnapshotGraphicsRegion(bus,
                    damageBeforeBottomSelectedWrite.Raw));

            if (repeatPartialUpdateForOppositeOverhangs)
            {
                var beginRepeatedPartial = new M68kCpuState
                {
                    A = { [0] = bottom.Layer }
                };
                Assert.True(InvokeHostTrap(
                    bus, Lvo(libraryBase, LayersLvo.BeginUpdate),
                    beginRepeatedPartial));
                Assert.Equal(1u, beginRepeatedPartial.D[0]);
                Assert.True(selectedAfterBottomReplace.SetEquals(
                    CaptureClipPixels(ReadLayerClipRect(bus, bottom.Layer))));
                var repeatedClipRectCount = 0;
                for (var clipRect = ReadLayerClipRect(bus, bottom.Layer);
                     clipRect != 0;
                     clipRect = ReadClipRectNext(bus, clipRect))
                    repeatedClipRectCount++;
                Assert.Equal(selectedClipRectCount, repeatedClipRectCount);
                Assert.Equal(replacementClipBytes,
                    SnapshotGraphicsRegion(bus, replacementClipRegion));
                Assert.Equal(damageBeforeBottomSelectedWriteBytes,
                    SnapshotGraphicsRegion(bus,
                        damageBeforeBottomSelectedWrite.Raw));

                var endRepeatedPartial = new M68kCpuState
                {
                    A = { [0] = bottom.Layer }, D = { [0] = 0 }
                };
                Assert.True(InvokeHostTrap(
                    bus, Lvo(libraryBase, LayersLvo.EndUpdate),
                    endRepeatedPartial));
                Assert.Equal(damageBeforeBottomSelectedWriteBytes,
                    SnapshotGraphicsRegion(bus,
                        damageBeforeBottomSelectedWrite.Raw));
                Assert.Equal(replacementClipBytes,
                    SnapshotGraphicsRegion(bus, replacementClipRegion));
            }

            var removeReplacementClip = new M68kCpuState
            {
                A = { [0] = bottom.Layer }
            };
            Assert.True(InvokeHostTrap(
                bus, Lvo(libraryBase, LayersLvo.InstallClipRegion),
                removeReplacementClip));
            Assert.Equal(replacementClipRegion, removeReplacementClip.D[0]);
        }
        var beginComplete = new M68kCpuState { A = { [0] = bottom.Layer } };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.BeginUpdate), beginComplete));
        Assert.Equal(1u, beginComplete.D[0]);
        Assert.True(expectedDamageProjection.SetEquals(CaptureClipPixels(
            ReadLayerClipRect(bus, bottom.Layer))));
        ClearPlanarBitMap(bus, displayBitMap);
        WriteRastPortForegroundPen(bus, bottom.RastPort, 1);
        var fill = new M68kCpuState
        {
            A = { [1] = bottom.RastPort },
            D = { [0] = 0, [1] = 0, [2] = 31, [3] = 19 }
        };
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.RectFill),
            fill));
        AssertDisplayPixels(expectedDamageProjection);
        var endComplete = new M68kCpuState
        {
            A = { [0] = bottom.Layer }, D = { [0] = 1 }
        };
        AssertEndUpdateClearsOwnedDamage(bus, libraryBase, endComplete);
        Assert.Equal(transparencyRegionBytes,
            SnapshotGraphicsRegion(bus, transparencyRegion));
        Assert.Equal(occluderTransparencyBytes,
            SnapshotGraphicsRegion(bus, occluderTransparencyRegion));

        var disposeRegion = new M68kCpuState
        {
            A = { [0] = callerClipRegion }
        };
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.DisposeRegion),
            disposeRegion));
        FreeGuestMemory(bus, rectangle,
            (uint)GraphicsLayouts.RectangleSize);
        if (replacementClipRegion != 0)
        {
            var disposeReplacementRegion = new M68kCpuState
            {
                A = { [0] = replacementClipRegion }
            };
            Assert.True(InvokeHostTrap(
                bus,
                Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                    (int)GraphicsLvo.DisposeRegion),
                disposeReplacementRegion));
        }
        if (survivingFrontCover is { } cover &&
            !survivingFrontCoverDeleted)
            DeleteLayer(bus, libraryBase, cover.Layer);
        DeleteLayer(bus, libraryBase, bottom.Layer);
        var disposeTransparencyRegion = new M68kCpuState
        {
            A = { [0] = transparencyRegion }
        };
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.DisposeRegion),
            disposeTransparencyRegion));
        var disposeOccluderTransparencyRegion = new M68kCpuState
        {
            A = { [0] = occluderTransparencyRegion }
        };
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.DisposeRegion),
            disposeOccluderTransparencyRegion));
        DisposeLayerInfo(bus, libraryBase, layerInfo);
        FreeBitMap(bus, displayBitMap);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void GuestBackfillHookRunsForDepthReorderAcrossPartialRefresh(
        bool moveBottomInFrontOfCover, bool retainAnotherOccluder)
    {
        using var machine = CreateMachine();
        var boot = new AmigaBootController(machine);
        boot.StartBootFromDisk(CreateBootableDisk());
        var bus = machine.Bus;
        var libraryBase = boot.CopperStartLayersLibraryBase;
        var layerInfo = NewLayerInfo(bus, libraryBase);
        var displayBitMap = AllocatePlanarBitMap(bus, 32, 20);
        var bottom = CreateLayerOnBitMaps(
            bus, libraryBase, layerInfo, displayBitMap, 0,
            LayerCreationFlags.Simple, 0, 0, 23, 15);
        var top = CreateLayerOnBitMaps(
            bus, libraryBase, layerInfo, displayBitMap, 0,
            LayerCreationFlags.Simple, 4, 4, 15, 11);
        var otherOccluder = 0u;
        if (retainAnotherOccluder)
        {
            var remainingCover = CreateLayerOnBitMaps(
                bus, libraryBase, layerInfo, displayBitMap, 0,
                LayerCreationFlags.Simple, 4, 4, 6, 11);
            otherOccluder = remainingCover.Layer;
        }
        var layerMemory = new LayersTestGuestMemory(bus);

        var regionResult = new M68kCpuState();
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.NewRegion),
            regionResult));
        var callerClipRegion = regionResult.D[0];
        Assert.NotEqual(0u, callerClipRegion);
        var rectangle = AllocateGuestMemory(
            bus, (uint)GraphicsLayouts.RectangleSize);
        WriteRectangleBounds(bus, rectangle, 7, 7, 7, 7);
        var addClip = new M68kCpuState
        {
            A = { [0] = callerClipRegion, [1] = rectangle }
        };
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.OrRectRegion),
            addClip));
        Assert.Equal(1u, addClip.D[0]);
        var callerClipBytes = SnapshotGraphicsRegion(bus, callerClipRegion);
        var installClip = new M68kCpuState
        {
            A = { [0] = bottom.Layer, [1] = callerClipRegion }
        };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.InstallClipRegion), installClip));
        Assert.Equal(0u, installClip.D[0]);

        var hookAuthority = new Fixture.BorrowAuthority();
        var hookCode = hookAuthority.Allocate(bus, 0x1000);
        var hookMarker = hookAuthority.Allocate(bus, sizeof(uint));
        bus.WriteLong(hookMarker.Raw, 0);
        var hook = hookAuthority.Hook(bus, hookCode.Raw);
        const uint hookMarkerValue = 0xF20A71E2;
        var hookProgram = new Fixture.GuestProgram(bus, hookCode.Raw);
        hookProgram.StoreLong(hookMarker.Raw, hookMarkerValue);
        hookProgram.Return();
        var installHook = new M68kCpuState
        {
            A = { [0] = bottom.Layer, [1] = hook.Raw }
        };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.InstallLayerHook), installHook));

        var callerCode = hookAuthority.Allocate(bus, 0x2000);
        var userStack = Fixture.GuestStack.Allocate(bus);
        var supervisorStack = Fixture.GuestStack.Allocate(bus);
        var actor = Fixture.CurrentTask(bus);
        Fixture.SetTaskStack(bus, actor, userStack);
        var caller = new Fixture.GuestProgram(bus, callerCode.Raw, 0x2000);
        caller.MoveAddress(6, libraryBase);
        uint afterReorder;
        if (moveBottomInFrontOfCover)
        {
            caller.MoveAddress(0, bottom.Layer);
            caller.MoveAddress(1, top.Layer);
            afterReorder = caller.Call(
                Lvo(libraryBase, LayersLvo.MoveLayerInFrontOf));
        }
        else
        {
            caller.MoveAddress(1, top.Layer);
            afterReorder = caller.Call(Lvo(libraryBase, LayersLvo.BehindLayer));
        }
        caller.Park();

        Fixture.StartRequester(
            machine, callerCode.Raw, userStack, supervisorStack);
        var initialBitmap = SnapshotPlanarBitMap(bus, displayBitMap);
        var firstCycle = machine.Cpu.State.Cycles;
        const long cycleBudget = 4_000_000;
        var instructionCount = 0;
        var lastGuestHookCycle = -1L;
        var guestHookEntries = 0;
        var callbackMessages = new List<LayerBackfillMessage>();
        void ObserveGuestHookEntry()
        {
            var state = machine.Cpu.State;
            if (state.ProgramCounter != hookCode.Raw ||
                state.Cycles == lastGuestHookCycle) return;
            lastGuestHookCycle = state.Cycles;
            guestHookEntries++;
            Assert.Equal(hook.Raw, state.A[0]);
            Assert.NotEqual(0u, state.A[1]);
            Assert.Equal(bottom.RastPort, state.A[2]);
            var messageMemory = new LayersTestGuestMemory(bus);
            Assert.True(LayersHookMessageCodec.TryRead(
                ref messageMemory, APTR.FromPointer(state.A[1]),
                out LayerBackfillMessage message));
            callbackMessages.Add(message);
        }

        void StepUntil(uint targetPc)
        {
            while (machine.Cpu.State.ProgramCounter != targetPc)
            {
                ObserveGuestHookEntry();
                Assert.False(machine.Cpu.State.Halted);
                Assert.True(instructionCount < 40_000);
                var result = boot.ContinueCopperStartRuntimeUntilCycle(
                    firstCycle + cycleBudget, maxInstructions: 1);
                instructionCount += result.InstructionsExecuted;
                Assert.Equal(1, result.InstructionsExecuted);
            }
            ObserveGuestHookEntry();
        }

        HashSet<(int X, int Y)> CaptureClipPixels(uint head)
        {
            var pixels = new HashSet<(int X, int Y)>();
            for (var clipRect = head; clipRect != 0;
                 clipRect = ReadClipRectNext(bus, clipRect))
            {
                var bounds = LayersClipRectCodec.BoundsAddress(
                    APTR.FromPointer(clipRect)).Raw;
                var minX = unchecked((short)bus.ReadWord(bounds));
                var minY = unchecked((short)bus.ReadWord(bounds + 2));
                var maxX = unchecked((short)bus.ReadWord(bounds + 4));
                var maxY = unchecked((short)bus.ReadWord(bounds + 6));
                for (var y = Math.Max(0, (int)minY);
                     y <= Math.Min(19, (int)maxY); y++)
                    for (var x = Math.Max(0, (int)minX);
                         x <= Math.Min(31, (int)maxX); x++)
                        pixels.Add((x, y));
            }
            return pixels;
        }

        void AssertDisplayPixels(HashSet<(int X, int Y)> expected)
        {
            for (var y = 0; y < 20; y++)
                for (var x = 0; x < 32; x++)
                    Assert.Equal(expected.Contains((x, y)),
                        ReadPlanarBitMapPixel(bus, displayBitMap, x, y));
        }

        StepUntil(hookCode.Raw);
        Assert.True(boot.CopperStartLayersHasPendingCallbackForTest);
        Assert.Contains(callbackMessages, message =>
            message.Layer == APTR.FromPointer(bottom.Layer) &&
            message.Bounds.MinX == (retainAnotherOccluder ? 7 : 4) &&
            message.Bounds.MinY == 4 &&
            message.Bounds.MaxX == 15 && message.Bounds.MaxY == 11);
        StepUntil(afterReorder);
        Assert.Equal(1, guestHookEntries);
        Assert.Equal(hookMarkerValue, bus.ReadLong(hookMarker.Raw));
        Assert.False(boot.CopperStartLayersHasPendingCallbackForTest);
        Assert.Equal(initialBitmap, SnapshotPlanarBitMap(bus, displayBitMap));
        Assert.Equal(callerClipBytes,
            SnapshotGraphicsRegion(bus, callerClipRegion));
        Assert.Equal(otherOccluder,
            LayersLayerCodec.ReadFront(ref layerMemory,
                APTR.FromPointer(bottom.Layer)).Raw);
        Assert.Equal(top.Layer, LayersLayerCodec.ReadBack(
            ref layerMemory, APTR.FromPointer(bottom.Layer)).Raw);

        var beginPartial = new M68kCpuState { A = { [0] = bottom.Layer } };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.BeginUpdate), beginPartial));
        Assert.Equal(1u, beginPartial.D[0]);
        var selected = new HashSet<(int X, int Y)> { (7, 7) };
        Assert.True(selected.SetEquals(CaptureClipPixels(
            ReadLayerClipRect(bus, bottom.Layer))));
        var damage = LayersLayerCodec.ReadDamageList(
            ref layerMemory, APTR.FromPointer(bottom.Layer));
        Assert.True(damage.IsNotNull);
        var damageBytes = SnapshotGraphicsRegion(bus, damage.Raw);
        WriteRastPortForegroundPen(bus, bottom.RastPort, 1);
        var writePixel = new M68kCpuState
        {
            A = { [1] = bottom.RastPort },
            D = { [0] = 7, [1] = 7 }
        };
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.WritePixel),
            writePixel));
        AssertDisplayPixels(selected);
        var endPartial = new M68kCpuState
        {
            A = { [0] = bottom.Layer }, D = { [0] = 0 }
        };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.EndUpdate), endPartial));
        Assert.Equal(damageBytes, SnapshotGraphicsRegion(bus, damage.Raw));

        var restoreBackfill = new M68kCpuState
        {
            A =
            {
                [0] = bottom.Layer,
                [1] = LayerBackfillHook.NoBackfill
            }
        };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.InstallLayerHook), restoreBackfill));
        var removeClip = new M68kCpuState { A = { [0] = bottom.Layer } };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.InstallClipRegion), removeClip));
        Assert.Equal(callerClipRegion, removeClip.D[0]);
        var expectedDamage = new HashSet<(int X, int Y)>();
        for (var y = 4; y <= 11; y++)
            for (var x = retainAnotherOccluder ? 7 : 4; x <= 15; x++)
                expectedDamage.Add((x, y));
        var beginComplete = new M68kCpuState { A = { [0] = bottom.Layer } };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.BeginUpdate), beginComplete));
        Assert.Equal(1u, beginComplete.D[0]);
        Assert.True(expectedDamage.SetEquals(CaptureClipPixels(
            ReadLayerClipRect(bus, bottom.Layer))));
        ClearPlanarBitMap(bus, displayBitMap);
        WriteRastPortForegroundPen(bus, bottom.RastPort, 1);
        var fill = new M68kCpuState
        {
            A = { [1] = bottom.RastPort },
            D = { [0] = 0, [1] = 0, [2] = 31, [3] = 19 }
        };
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.RectFill),
            fill));
        AssertDisplayPixels(expectedDamage);
        var endComplete = new M68kCpuState
        {
            A = { [0] = bottom.Layer }, D = { [0] = 1 }
        };
        AssertEndUpdateClearsOwnedDamage(bus, libraryBase, endComplete);

        var disposeRegion = new M68kCpuState
        {
            A = { [0] = callerClipRegion }
        };
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.DisposeRegion),
            disposeRegion));
        FreeGuestMemory(bus, rectangle,
            (uint)GraphicsLayouts.RectangleSize);
        DeleteLayer(bus, libraryBase, top.Layer);
        if (otherOccluder != 0)
            DeleteLayer(bus, libraryBase, otherOccluder);
        DeleteLayer(bus, libraryBase, bottom.Layer);
        DisposeLayerInfo(bus, libraryBase, layerInfo);
        FreeBitMap(bus, displayBitMap);
    }

    [Fact]
    public void GuestBackfillDepthReorderAndMovePreserveCombinedRegions()
    {
        using var machine = CreateMachine();
        var boot = new AmigaBootController(machine);
        boot.StartBootFromDisk(CreateBootableDisk());
        var bus = machine.Bus;
        var libraryBase = boot.CopperStartLayersLibraryBase;
        var layerInfo = NewLayerInfo(bus, libraryBase);
        var displayBitMap = AllocatePlanarBitMap(bus, 32, 20);
        var bottom = CreateLayerOnBitMaps(
            bus, libraryBase, layerInfo, displayBitMap, 0,
            LayerCreationFlags.Simple, 0, 0, 23, 15);

        var transparencyRegionResult = new M68kCpuState();
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.NewRegion),
            transparencyRegionResult));
        var transparencyRegion = transparencyRegionResult.D[0];
        Assert.NotEqual(0u, transparencyRegion);
        var rectangle = AllocateGuestMemory(
            bus, (uint)GraphicsLayouts.RectangleSize);
        WriteRectangleBounds(bus, rectangle, 12, 10, 12, 10);
        var addTransparentPixel = new M68kCpuState
        {
            A = { [0] = transparencyRegion, [1] = rectangle }
        };
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.OrRectRegion),
            addTransparentPixel));
        Assert.Equal(1u, addTransparentPixel.D[0]);
        var transparencyRegionBytes = SnapshotGraphicsRegion(
            bus, transparencyRegion);

        var transparencyTags = AllocateGuestMemory(bus, 16);
        bus.WriteLong(transparencyTags,
            (uint)LayerCreationTag.TransparentRegion);
        bus.WriteLong(transparencyTags + 4, transparencyRegion);
        bus.WriteLong(transparencyTags + 8, 0);
        bus.WriteLong(transparencyTags + 12, 0);
        var createTarget = new M68kCpuState
        {
            A =
            {
                [0] = layerInfo,
                [1] = displayBitMap,
                [2] = transparencyTags
            },
            D =
            {
                [0] = 0,
                [1] = 0,
                [2] = 23,
                [3] = 15,
                [4] = (uint)LayerCreationFlags.Simple
            }
        };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.CreateUpfrontLayerTagList),
            createTarget));
        Assert.NotEqual(0u, createTarget.D[0]);
        var target = new LayerSurface(
            displayBitMap,
            createTarget.D[0],
            ReadLayerRastPort(bus, createTarget.D[0]),
            ReadLayerClipRect(bus, createTarget.D[0]));
        FreeGuestMemory(bus, transparencyTags, 16);

        var cover = CreateLayerOnBitMaps(
            bus, libraryBase, layerInfo, displayBitMap, 0,
            LayerCreationFlags.Simple, 4, 4, 15, 11);

        var regionResult = new M68kCpuState();
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.NewRegion),
            regionResult));
        var callerClipRegion = regionResult.D[0];
        Assert.NotEqual(0u, callerClipRegion);
        WriteRectangleBounds(bus, rectangle, 7, 7, 7, 7);
        var addClip = new M68kCpuState
        {
            A = { [0] = callerClipRegion, [1] = rectangle }
        };
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.OrRectRegion),
            addClip));
        Assert.Equal(1u, addClip.D[0]);
        WriteRectangleBounds(bus, rectangle, 12, 10, 12, 10);
        var addClipAtTransparentPixel = new M68kCpuState
        {
            A = { [0] = callerClipRegion, [1] = rectangle }
        };
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.OrRectRegion),
            addClipAtTransparentPixel));
        Assert.Equal(1u, addClipAtTransparentPixel.D[0]);
        var callerClipBytes = SnapshotGraphicsRegion(bus, callerClipRegion);
        var installClip = new M68kCpuState
        {
            A = { [0] = target.Layer, [1] = callerClipRegion }
        };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.InstallClipRegion), installClip));
        Assert.Equal(0u, installClip.D[0]);

        var layerMemory = new LayersTestGuestMemory(bus);
        var hookAuthority = new Fixture.BorrowAuthority();
        var hookCode = hookAuthority.Allocate(bus, 0x1000);
        var hookMarker = hookAuthority.Allocate(bus, sizeof(uint));
        bus.WriteLong(hookMarker.Raw, 0);
        var hook = hookAuthority.Hook(bus, hookCode.Raw);
        const uint hookMarkerValue = 0xF20A71E3;
        var hookProgram = new Fixture.GuestProgram(bus, hookCode.Raw);
        hookProgram.StoreLong(hookMarker.Raw, hookMarkerValue);
        hookProgram.Return();
        foreach (var layer in new[] { target.Layer, bottom.Layer })
        {
            var installHook = new M68kCpuState
            {
                A = { [0] = layer, [1] = hook.Raw }
            };
            Assert.True(InvokeHostTrap(
                bus, Lvo(libraryBase, LayersLvo.InstallLayerHook), installHook));
        }

        var callerCode = hookAuthority.Allocate(bus, 0x2000);
        var userStack = Fixture.GuestStack.Allocate(bus);
        var supervisorStack = Fixture.GuestStack.Allocate(bus);
        var actor = Fixture.CurrentTask(bus);
        Fixture.SetTaskStack(bus, actor, userStack);
        var caller = new Fixture.GuestProgram(bus, callerCode.Raw, 0x2000);
        caller.MoveAddress(6, libraryBase);
        caller.MoveAddress(1, cover.Layer);
        var afterReorder = caller.Call(Lvo(libraryBase, LayersLvo.BehindLayer));
        caller.Park();
        Fixture.StartRequester(
            machine, callerCode.Raw, userStack, supervisorStack);

        var firstCycle = machine.Cpu.State.Cycles;
        const long cycleBudget = 4_000_000;
        var instructionCount = 0;
        var lastGuestHookCycle = -1L;
        var guestHookEntries = 0;
        var callbackMessages = new List<LayerBackfillMessage>();
        void ObserveGuestHookEntry()
        {
            var state = machine.Cpu.State;
            if (state.ProgramCounter != hookCode.Raw ||
                state.Cycles == lastGuestHookCycle) return;
            lastGuestHookCycle = state.Cycles;
            guestHookEntries++;
            Assert.Equal(hook.Raw, state.A[0]);
            Assert.NotEqual(0u, state.A[1]);
            var messageMemory = new LayersTestGuestMemory(bus);
            Assert.True(LayersHookMessageCodec.TryRead(
                ref messageMemory, APTR.FromPointer(state.A[1]),
                out LayerBackfillMessage message));
            Assert.True(message.Layer.Raw == target.Layer ||
                message.Layer.Raw == bottom.Layer);
            var expectedRastPort = message.Layer.Raw == target.Layer
                ? target.RastPort : bottom.RastPort;
            Assert.Equal(expectedRastPort, state.A[2]);
            callbackMessages.Add(message);
        }

        void StepUntil(uint targetPc)
        {
            while (machine.Cpu.State.ProgramCounter != targetPc)
            {
                ObserveGuestHookEntry();
                if (targetPc == hookCode.Raw &&
                    machine.Cpu.State.ProgramCounter == afterReorder)
                    Assert.Fail("BehindLayer returned without the visible target hook.");
                Assert.False(machine.Cpu.State.Halted);
                Assert.True(instructionCount < 40_000);
                var result = boot.ContinueCopperStartRuntimeUntilCycle(
                    firstCycle + cycleBudget, maxInstructions: 1);
                instructionCount += result.InstructionsExecuted;
                Assert.Equal(1, result.InstructionsExecuted);
            }
            ObserveGuestHookEntry();
        }

        HashSet<(int X, int Y)> CaptureClipPixels(uint head)
        {
            var pixels = new HashSet<(int X, int Y)>();
            for (var clipRect = head; clipRect != 0;
                 clipRect = ReadClipRectNext(bus, clipRect))
            {
                var bounds = LayersClipRectCodec.BoundsAddress(
                    APTR.FromPointer(clipRect)).Raw;
                var minX = unchecked((short)bus.ReadWord(bounds));
                var minY = unchecked((short)bus.ReadWord(bounds + 2));
                var maxX = unchecked((short)bus.ReadWord(bounds + 4));
                var maxY = unchecked((short)bus.ReadWord(bounds + 6));
                for (var y = Math.Max(0, (int)minY);
                     y <= Math.Min(19, (int)maxY); y++)
                    for (var x = Math.Max(0, (int)minX);
                         x <= Math.Min(31, (int)maxX); x++)
                        pixels.Add((x, y));
            }
            return pixels;
        }

        StepUntil(hookCode.Raw);
        Assert.True(boot.CopperStartLayersHasPendingCallbackForTest);
        StepUntil(afterReorder);
        Assert.Equal(guestHookEntries, callbackMessages.Count);
        Assert.All(callbackMessages, message =>
            Assert.True(message.Layer.Raw == target.Layer ||
                message.Layer.Raw == bottom.Layer));
        var targetExposure = new HashSet<(int X, int Y)>();
        foreach (var message in callbackMessages.Where(message =>
            message.Layer == APTR.FromPointer(target.Layer)))
            for (var y = message.Bounds.MinY; y <= message.Bounds.MaxY; y++)
                for (var x = message.Bounds.MinX; x <= message.Bounds.MaxX; x++)
                    targetExposure.Add((x, y));
        var expectedTargetExposure = new HashSet<(int X, int Y)>();
        for (var y = 4; y <= 11; y++)
            for (var x = 4; x <= 15; x++)
                if (x != 12 || y != 10) expectedTargetExposure.Add((x, y));
        Assert.True(expectedTargetExposure.SetEquals(targetExposure));
        Assert.Contains(callbackMessages, message =>
            message.Layer == APTR.FromPointer(bottom.Layer));
        var lowerExposure = new HashSet<(int X, int Y)>();
        foreach (var message in callbackMessages.Where(message =>
            message.Layer == APTR.FromPointer(bottom.Layer)))
            for (var y = message.Bounds.MinY; y <= message.Bounds.MaxY; y++)
                for (var x = message.Bounds.MinX; x <= message.Bounds.MaxX; x++)
                    lowerExposure.Add((x, y));
        var expectedLowerExposure = new HashSet<(int X, int Y)>();
        for (var y = 4; y <= 11; y++)
            for (var x = 4; x <= 15; x++)
                if (x != 7 || y != 7) expectedLowerExposure.Add((x, y));
        Assert.True(expectedLowerExposure.SetEquals(lowerExposure),
            $"Lower exposure differs; missing=[{string.Join(',', expectedLowerExposure.Except(lowerExposure).OrderBy(pixel => pixel.Y).ThenBy(pixel => pixel.X))}], " +
            $"extra=[{string.Join(',', lowerExposure.Except(expectedLowerExposure).OrderBy(pixel => pixel.Y).ThenBy(pixel => pixel.X))}].");
        Assert.Equal(hookMarkerValue, bus.ReadLong(hookMarker.Raw));
        Assert.False(boot.CopperStartLayersHasPendingCallbackForTest);
        Assert.Equal(callerClipBytes,
            SnapshotGraphicsRegion(bus, callerClipRegion));

        var beginPartial = new M68kCpuState { A = { [0] = target.Layer } };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.BeginUpdate), beginPartial));
        Assert.Equal(1u, beginPartial.D[0]);
        var selected = new HashSet<(int X, int Y)> { (7, 7) };
        Assert.True(selected.SetEquals(CaptureClipPixels(
            ReadLayerClipRect(bus, target.Layer))));
        var damage = LayersLayerCodec.ReadDamageList(
            ref layerMemory, APTR.FromPointer(target.Layer));
        Assert.True(damage.IsNotNull);
        var damageBytes = SnapshotGraphicsRegion(bus, damage.Raw);
        var endPartial = new M68kCpuState
        {
            A = { [0] = target.Layer }, D = { [0] = 0 }
        };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.EndUpdate), endPartial));
        Assert.Equal(damageBytes, SnapshotGraphicsRegion(bus, damage.Raw));

        var replacementRegionResult = new M68kCpuState();
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.NewRegion),
            replacementRegionResult));
        var replacementClipRegion = replacementRegionResult.D[0];
        Assert.NotEqual(0u, replacementClipRegion);
        WriteRectangleBounds(bus, rectangle, 8, 8, 8, 8);
        var addReplacementClipPixel = new M68kCpuState
        {
            A = { [0] = replacementClipRegion, [1] = rectangle }
        };
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.OrRectRegion),
            addReplacementClipPixel));
        Assert.Equal(1u, addReplacementClipPixel.D[0]);
        WriteRectangleBounds(bus, rectangle, 12, 10, 12, 10);
        var addReplacementTransparentPixel = new M68kCpuState
        {
            A = { [0] = replacementClipRegion, [1] = rectangle }
        };
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.OrRectRegion),
            addReplacementTransparentPixel));
        Assert.Equal(1u, addReplacementTransparentPixel.D[0]);
        var replacementClipBytes = SnapshotGraphicsRegion(
            bus, replacementClipRegion);

        var replacementCallerCode = hookAuthority.Allocate(bus, 0x2000);
        var replacementUserStack = Fixture.GuestStack.Allocate(bus);
        var replacementSupervisorStack = Fixture.GuestStack.Allocate(bus);
        Fixture.SetTaskStack(bus, actor, replacementUserStack);
        var replacementCaller = new Fixture.GuestProgram(
            bus, replacementCallerCode.Raw, 0x2000);
        replacementCaller.MoveAddress(6, libraryBase);
        replacementCaller.MoveAddress(0, target.Layer);
        replacementCaller.MoveAddress(1, replacementClipRegion);
        var afterReplacement = replacementCaller.Call(
            Lvo(libraryBase, LayersLvo.InstallClipRegion));
        replacementCaller.Park();
        var callbacksBeforeReplacement = callbackMessages.Count;
        Fixture.StartRequester(
            machine, replacementCallerCode.Raw,
            replacementUserStack, replacementSupervisorStack);
        StepUntil(afterReplacement);
        Assert.True(callbackMessages.Count > callbacksBeforeReplacement);
        Assert.Equal(callerClipRegion, machine.Cpu.State.D[0]);
        Assert.False(boot.CopperStartLayersHasPendingCallbackForTest);
        Assert.Equal(replacementClipRegion, LayersLayerCodec.ReadClipRegion(
            ref layerMemory, APTR.FromPointer(target.Layer)).Raw);
        Assert.Equal(replacementClipBytes,
            SnapshotGraphicsRegion(bus, replacementClipRegion));
        var damageAfterClipReplacement = LayersLayerCodec.ReadDamageList(
            ref layerMemory, APTR.FromPointer(target.Layer));
        Assert.True(damageAfterClipReplacement.IsNotNull);

        var beginReplacementPartial = new M68kCpuState
        {
            A = { [0] = target.Layer }
        };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.BeginUpdate),
            beginReplacementPartial));
        Assert.Equal(1u, beginReplacementPartial.D[0]);
        Assert.True(new HashSet<(int X, int Y)> { (8, 8) }.SetEquals(
            CaptureClipPixels(ReadLayerClipRect(bus, target.Layer))));
        var replacementDamageBytes = SnapshotGraphicsRegion(
            bus, damageAfterClipReplacement.Raw);
        var endReplacementPartial = new M68kCpuState
        {
            A = { [0] = target.Layer }, D = { [0] = 0 }
        };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.EndUpdate),
            endReplacementPartial));
        Assert.Equal(replacementDamageBytes,
            SnapshotGraphicsRegion(bus, damageAfterClipReplacement.Raw));

        var moveCallerCode = hookAuthority.Allocate(bus, 0x2000);
        var moveUserStack = Fixture.GuestStack.Allocate(bus);
        var moveSupervisorStack = Fixture.GuestStack.Allocate(bus);
        Fixture.SetTaskStack(bus, actor, moveUserStack);
        var moveCaller = new Fixture.GuestProgram(
            bus, moveCallerCode.Raw, 0x2000);
        moveCaller.MoveAddress(6, libraryBase);
        moveCaller.MoveAddress(1, target.Layer);
        moveCaller.MoveData(0, 2);
        moveCaller.MoveData(1, 1);
        var afterMove = moveCaller.Call(Lvo(libraryBase, LayersLvo.MoveLayer));
        moveCaller.Park();
        var callbacksBeforeMove = callbackMessages.Count;
        Fixture.StartRequester(
            machine, moveCallerCode.Raw, moveUserStack, moveSupervisorStack);
        StepUntil(afterMove);
        Assert.True(callbackMessages.Count > callbacksBeforeMove);
        Assert.NotEqual(0u, machine.Cpu.State.D[0]);
        Assert.False(boot.CopperStartLayersHasPendingCallbackForTest);
        var movedLowerExposure = new HashSet<(int X, int Y)>();
        foreach (var message in callbackMessages.Skip(callbacksBeforeMove)
            .Where(message => message.Layer == APTR.FromPointer(bottom.Layer)))
            for (var y = message.Bounds.MinY; y <= message.Bounds.MaxY; y++)
                for (var x = message.Bounds.MinX; x <= message.Bounds.MaxX; x++)
                    movedLowerExposure.Add((x, y));
        Assert.True(new HashSet<(int X, int Y)> { (8, 8) }.SetEquals(
            movedLowerExposure));
        var movedBounds = LayersLayerCodec.ReadBounds(
            ref layerMemory, APTR.FromPointer(target.Layer));
        Assert.Equal((short)2, movedBounds.MinX);
        Assert.Equal((short)1, movedBounds.MinY);
        Assert.Equal((short)25, movedBounds.MaxX);
        Assert.Equal((short)16, movedBounds.MaxY);
        Assert.Equal(replacementClipBytes,
            SnapshotGraphicsRegion(bus, replacementClipRegion));
        Assert.Equal(transparencyRegionBytes,
            SnapshotGraphicsRegion(bus, transparencyRegion));

        var damageAfterMove = LayersLayerCodec.ReadDamageList(
            ref layerMemory, APTR.FromPointer(target.Layer));
        Assert.True(damageAfterMove.IsNotNull);
        var movedDamageBytes = SnapshotGraphicsRegion(bus, damageAfterMove.Raw);
        var beginMovedPartial = new M68kCpuState
        {
            A = { [0] = target.Layer }
        };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.BeginUpdate), beginMovedPartial));
        Assert.Equal(1u, beginMovedPartial.D[0]);
        Assert.True(new HashSet<(int X, int Y)> { (10, 9) }.SetEquals(
            CaptureClipPixels(ReadLayerClipRect(bus, target.Layer))));
        var endMovedPartial = new M68kCpuState
        {
            A = { [0] = target.Layer }, D = { [0] = 0 }
        };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.EndUpdate), endMovedPartial));
        Assert.Equal(movedDamageBytes,
            SnapshotGraphicsRegion(bus, damageAfterMove.Raw));

        var restoreBackfill = new M68kCpuState
        {
            A =
            {
                [0] = target.Layer,
                [1] = LayerBackfillHook.NoBackfill
            }
        };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.InstallLayerHook), restoreBackfill));
        var restoreBottomBackfill = new M68kCpuState
        {
            A =
            {
                [0] = bottom.Layer,
                [1] = LayerBackfillHook.NoBackfill
            }
        };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.InstallLayerHook),
            restoreBottomBackfill));
        var removeClip = new M68kCpuState { A = { [0] = target.Layer } };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.InstallClipRegion), removeClip));
        Assert.Equal(replacementClipRegion, removeClip.D[0]);
        var expectedDamage = new HashSet<(int X, int Y)>();
        for (var y = 5; y <= 12; y++)
            for (var x = 6; x <= 17; x++)
                if (x != 14 || y != 11) expectedDamage.Add((x, y));
        var beginComplete = new M68kCpuState { A = { [0] = target.Layer } };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.BeginUpdate), beginComplete));
        Assert.Equal(1u, beginComplete.D[0]);
        Assert.True(expectedDamage.SetEquals(CaptureClipPixels(
            ReadLayerClipRect(bus, target.Layer))));
        var endComplete = new M68kCpuState
        {
            A = { [0] = target.Layer }, D = { [0] = 0 }
        };
        Assert.True(InvokeHostTrap(
            bus, Lvo(libraryBase, LayersLvo.EndUpdate), endComplete));
        var retainedDamage = LayersLayerCodec.ReadDamageList(
            ref layerMemory, APTR.FromPointer(target.Layer));
        Assert.Equal(movedDamageBytes,
            SnapshotGraphicsRegion(bus, retainedDamage.Raw));

        var disposeRegion = new M68kCpuState
        {
            A = { [0] = callerClipRegion }
        };
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.DisposeRegion),
            disposeRegion));
        var disposeReplacementRegion = new M68kCpuState
        {
            A = { [0] = replacementClipRegion }
        };
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.DisposeRegion),
            disposeReplacementRegion));
        DeleteLayer(bus, libraryBase, cover.Layer);
        DeleteLayer(bus, libraryBase, target.Layer);
        DeleteLayer(bus, libraryBase, bottom.Layer);
        var disposeTransparencyRegion = new M68kCpuState
        {
            A = { [0] = transparencyRegion }
        };
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.GraphicsLibraryBase,
                (int)GraphicsLvo.DisposeRegion),
            disposeTransparencyRegion));
        FreeGuestMemory(bus, rectangle,
            (uint)GraphicsLayouts.RectangleSize);
        DisposeLayerInfo(bus, libraryBase, layerInfo);
        FreeBitMap(bus, displayBitMap);
    }
}
