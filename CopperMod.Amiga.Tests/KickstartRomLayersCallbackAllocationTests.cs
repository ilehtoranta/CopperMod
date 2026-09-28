using Amiga;
using PortableLayers = CopperStart.Layers;

namespace CopperMod.Amiga.Tests;

public sealed partial class KickstartRomLayersDifferentialTests
{
    [Fact]
    public void CopperStartCallbackCreateAllocationFailuresRetireOwnersWithoutReset()
    {
        var context = CreateCopperStartOracle();
        try
        {
            var opaqueBefore = context.Boot.CopperStartLayersOpaqueAllocationCountForTest;
            var availableBefore = AvailableMemory();
            var baseline = CreateCallbackFailureFixture(context);
            int allocationCount;
            uint created;
            using (var fault = context.InstallOrdinalFault(
                       context.ExecBase, ExecLvo.AllocMem, failOrdinal: 0))
            {
                created = InvokeBehindHookCreate(context, baseline);
                allocationCount = fault.Count;
            }
            Assert.NotEqual(0u, created);
            Assert.True(allocationCount > 0);
            Assert.Equal(4, baseline.Hook.CallbackCount);
            AssertReturned();
            DestroyCallbackFailureFixture(context, baseline, created);
            AssertDrained();

            var afterHookFailures = 0;
            for (var ordinal = 1; ordinal <= allocationCount; ordinal++)
            {
                var fixture = CreateCallbackFailureFixture(context);
                using (var fault = context.InstallOrdinalFault(
                           context.ExecBase, ExecLvo.AllocMem, ordinal))
                {
                    created = InvokeBehindHookCreate(context, fixture);
                    Assert.True(fault.Count >= ordinal);
                }
                Assert.Equal(0u, created);
                Assert.Equal(fixture.Blocker,
                    ReadOracleTopLayer(context.Bus, fixture.LayerInfo));
                if (fixture.Hook.CallbackCount != 0)
                    afterHookFailures++;
                AssertReturned();
                DestroyCallbackFailureFixture(context, fixture, created);
                AssertDrained();
                // Deliberately do not reset/reinstall Layers: leaked callback,
                // lock or transaction owners must poison this test, not vanish.
            }
            Assert.True(afterHookFailures > 0);
            _output.WriteLine($"Allocation failures returned and drained: " +
                $"{allocationCount}; after at least one Hook: {afterHookFailures}.");

            uint AvailableMemory() => context.Invoke(context.ExecBase, ExecLvo.AvailMem,
                state => state.D[1] = 0).D[0];

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
}
