using Amiga;
using PortableLayers = CopperStart.Layers;

namespace CopperMod.Amiga.Tests;

public sealed partial class KickstartRomLayersDifferentialTests
{
    [Fact]
    public void CopperStartCallbackDeleteAllocationFailuresRetireOwnersWithoutReset()
    {
        const byte initialPattern = 0x3C;
        const byte hookPattern = 0xA5;
        var context = CreateCopperStartOracle();
        try
        {
            var opaqueBefore = context.Boot.CopperStartLayersOpaqueAllocationCountForTest;
            var availableBefore = AvailableMemory();
            var baseline = CreateMutationFailureFixture(context, initialPattern, hookPattern);
            int allocationCount;
            uint deleted;
            using (var fault = context.InstallOrdinalFault(
                       context.ExecBase, ExecLvo.AllocMem, failOrdinal: 0))
            {
                deleted = InvokeDelete(context, baseline.Cover);
                allocationCount = fault.Count;
            }
            Assert.NotEqual(0u, deleted);
            Assert.True(allocationCount > 0);
            Assert.Equal(1, baseline.Hook.CallbackCount);
            Assert.Equal(baseline.Bottom,
                ReadOracleTopLayer(context.Bus, baseline.LayerInfo));
            AssertReturned();
            AssertPixels(baseline, hookPattern);
            DestroyMutationFailureFixture(context, baseline, coverRemains: false);
            AssertDrained();

            var afterHookFailures = 0;
            for (var ordinal = 1; ordinal <= allocationCount; ordinal++)
            {
                var fixture = CreateMutationFailureFixture(context, initialPattern, hookPattern);
                var fixtureOpaque = context.Boot.CopperStartLayersOpaqueAllocationCountForTest;
                var fixtureAvailable = AvailableMemory();
                var bottomBefore = DescribeLayer(context, fixture.Bottom, fixture.Display.Address, 0);
                var coverBefore = DescribeLayer(context, fixture.Cover, fixture.Display.Address, 0);
                using (var fault = context.InstallOrdinalFault(
                           context.ExecBase, ExecLvo.AllocMem, ordinal))
                {
                    deleted = InvokeDelete(context, fixture.Cover);
                    Assert.True(fault.Count >= ordinal,
                        $"Delete allocation fault {ordinal}/{allocationCount} was not reached.");
                }
                // The baseline has no cleanup retry: all its allocations precede
                // commit. A one-shot failure must abort this Delete, even when
                // the Hook already returned and cleanup needed another admission.
                Assert.Equal(0u, deleted);
                AssertReturned();
                Assert.Equal(fixture.Cover,
                    ReadOracleTopLayer(context.Bus, fixture.LayerInfo));
                Assert.Equal(bottomBefore,
                    DescribeLayer(context, fixture.Bottom, fixture.Display.Address, 0));
                Assert.Equal(coverBefore,
                    DescribeLayer(context, fixture.Cover, fixture.Display.Address, 0));
                Assert.InRange(fixture.Hook.CallbackCount, 0, 1);
                if (fixture.Hook.CallbackCount != 0)
                    afterHookFailures++;
                AssertPixels(fixture,
                    fixture.Hook.CallbackCount == 0 ? initialPattern : hookPattern);
                AssertLocksFree(fixture);
                Assert.Equal(fixtureOpaque,
                    context.Boot.CopperStartLayersOpaqueAllocationCountForTest);
                Assert.Equal(fixtureAvailable, AvailableMemory());

                DestroyMutationFailureFixture(context, fixture, coverRemains: true);
                AssertDrained();
                // No reset/reinstall between cases: unfinished callback, lock,
                // transaction or memory ownership must remain observable.
            }
            Assert.True(afterHookFailures > 0);
            _output.WriteLine($"Delete allocation failures returned and drained: " +
                $"{allocationCount}; after the Hook: {afterHookFailures}; " +
                $"available bytes restored: {availableBefore}.");

            uint AvailableMemory() => context.Invoke(context.ExecBase, ExecLvo.AvailMem,
                state => state.D[1] = 0).D[0];

            void AssertPixels(MutationFailureFixture fixture, byte expected)
            {
                foreach (var plane in fixture.Display.Planes)
                    for (var offset = 0; offset < fixture.Display.BytesPerRow * fixture.Display.Height; offset++)
                        Assert.Equal(expected, context.Bus.ReadByte(plane + checked((uint)offset)));
            }

            void AssertLocksFree(MutationFailureFixture fixture)
            {
                var memory = new LayersTestGuestMemory(context.Bus);
                Assert.True(LayersSignalSemaphoreCodec.ReadOwner(ref memory,
                    LayersLayerInfoCodec.LockAddress(APTR.FromPointer(fixture.LayerInfo))).IsNull);
                Assert.True(LayersSignalSemaphoreCodec.ReadOwner(ref memory,
                    LayersLayerCodec.LockAddress(APTR.FromPointer(fixture.Bottom))).IsNull);
                Assert.True(LayersSignalSemaphoreCodec.ReadOwner(ref memory,
                    LayersLayerCodec.LockAddress(APTR.FromPointer(fixture.Cover))).IsNull);
            }

            void AssertReturned()
            {
                context.AssertCopperStartCallerStackRestored();
                Assert.False(context.Machine.Cpu.State.Halted);
                Assert.False(context.Boot.CopperStartLayersHasPendingCallbackForTest);
                Assert.Equal(0, context.Boot.CopperStartLayersPendingWaitCountForTest);
                Assert.Equal(default,
                    context.Boot.CopperStartLayersLastCallbackReturnFaultForTest);
                Assert.Equal(0, context.Boot.CopperStartLayersPixelTransactionCountForTest);
                var memory = new LayersTestGuestMemory(context.Bus);
                Assert.True(PortableLayers.LayersPrivateRootCore.ActiveContinuation(
                    ref memory, APTR.FromPointer(
                        context.Boot.CopperStartLayersRootAddress)).IsNull);
            }

            void AssertDrained()
            {
                AssertReturned();
                Assert.Equal(opaqueBefore,
                    context.Boot.CopperStartLayersOpaqueAllocationCountForTest);
                Assert.Equal(availableBefore, AvailableMemory());
            }
        }
        finally
        {
            context.Machine.Dispose();
        }
    }

    private static MutationFailureFixture CreateMutationFailureFixture(
        OracleContext context, byte initialPattern, byte hookPattern)
    {
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(16, 16, 1, initialPattern,
            planeMemoryFlags: Exec.MemoryFlags.Chip);
        var bottom = context.CreateLayer(LayersLvo.CreateUpfrontLayer,
            layerInfo, display.Address, 0, LayerCreationFlags.Simple, 0, 0, 15, 15);
        var cover = context.CreateLayer(LayersLvo.CreateUpfrontLayer,
            layerInfo, display.Address, 0, LayerCreationFlags.Simple, 4, 4, 11, 11);
        var hook = context.CreateCallbackProbe(display.Address, 0, hookPattern);
        context.InvokeLayers(LayersLvo.InstallLayerHook, state =>
        {
            state.A[0] = bottom;
            state.A[1] = hook.Hook;
        });
        foreach (var plane in display.Planes)
            for (var offset = 0; offset < display.BytesPerRow * display.Height; offset++)
                context.Bus.WriteByte(plane + checked((uint)offset), initialPattern, 0);
        Assert.Equal(0, hook.CallbackCount);
        return new MutationFailureFixture(layerInfo, display, bottom, cover, hook);
    }

    private static uint InvokeDelete(OracleContext context, uint layer)
        => context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = layer).D[0];

    private static void DestroyMutationFailureFixture(
        OracleContext context, MutationFailureFixture fixture, bool coverRemains)
    {
        // Cleanup must not create another callback and hide a retained owner.
        context.InvokeLayers(LayersLvo.InstallLayerHook, state =>
        {
            state.A[0] = fixture.Bottom;
            state.A[1] = LayerBackfillHook.NoBackfill;
        });
        if (coverRemains)
            Assert.NotEqual(0u, InvokeDelete(context, fixture.Cover));
        Assert.NotEqual(0u, InvokeDelete(context, fixture.Bottom));
        context.DisposeLayerInfo(fixture.LayerInfo);
        fixture.Hook.Dispose();
        context.FreePlanarBitMap(fixture.Display);
    }

    private readonly record struct MutationFailureFixture(
        uint LayerInfo, PlanarBitMap Display, uint Bottom, uint Cover, CallbackProbe Hook);
}
