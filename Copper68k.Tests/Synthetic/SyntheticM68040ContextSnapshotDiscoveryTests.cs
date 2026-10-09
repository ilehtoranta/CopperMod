using Copper68k;

namespace Copper68k.Tests.Synthetic;

// Emulator snapshot ownership contract; not a silicon context/FSAVE ABI.
public sealed class SyntheticM68040ContextSnapshotDiscoveryTests
{
    [EnvironmentFact("COPPER68K_RUN_040_CONTEXT_SNAPSHOT_DISCOVERY", "qualify independent suspended-delivery snapshots"), Trait("Suite", "ReferenceDiscovery")]
    public void SnapshotsOwnIndependentNestedPendingDeliveries()
    {
        foreach (var taskOnly in new[] { false, true })
        {
            var source = new M68kCpuState { Cycles = 100, NativeCycles = 200, ProgramCounter = 0x1000 };
            source.M68040PendingFpuExceptions.Begin(3, 49, 0x6000);
            source.M68040PendingFpuExceptions.Begin(3, 55, 0x6000);
            var originalTop = source.M68040PendingFpuExceptions.Begin(3, 55, 0x6000);
            var saved = new M68kCpuState { Cycles = 500, NativeCycles = 700 };
            saved.M68040PendingFpuExceptions.Begin(3, 53, 0x6000);
            Copy(saved, source); Copy(saved, source); // Replace, never append.
            Assert.Equal(taskOnly ? 500 : 100, saved.Cycles);
            Assert.Equal(taskOnly ? 700 : 200, saved.NativeCycles);
            Assert.Equal(0x1000u, saved.ProgramCounter);
            var top = saved.M68040PendingFpuExceptions.Find(3);
            Assert.NotNull(top); Assert.Equal(55, top.Vector); Assert.NotSame(originalTop, top);
            Copy(saved, saved); // Self-copy must retain the same delivery ownership.
            Assert.Same(top, saved.M68040PendingFpuExceptions.Find(3));
            saved.M68040PendingFpuExceptions.Complete(top);
            var equal = saved.M68040PendingFpuExceptions.Find(3);
            Assert.NotNull(equal); Assert.Equal(55, equal.Vector); Assert.NotSame(top, equal);
            saved.M68040PendingFpuExceptions.Complete(top); // Already consumed identity.
            Assert.Same(equal, saved.M68040PendingFpuExceptions.Find(3));
            saved.M68040PendingFpuExceptions.Complete(equal);
            Assert.Equal(49, saved.M68040PendingFpuExceptions.Find(3)!.Vector);
            Assert.Same(originalTop, source.M68040PendingFpuExceptions.Find(3));
            saved.M68040PendingFpuExceptions.Reset();
            Assert.Null(saved.M68040PendingFpuExceptions.Find(3));
            Assert.Same(originalTop, source.M68040PendingFpuExceptions.Find(3));
            Copy(saved, source);
            source.M68040PendingFpuExceptions.Reset();
            Assert.Equal(55, saved.M68040PendingFpuExceptions.Find(3)!.Vector);
            Copy(saved, new M68kCpuState()); // Empty incoming context clears stale delivery.
            Assert.Null(saved.M68040PendingFpuExceptions.Find(3));

            void Copy(M68kCpuState destination, M68kCpuState from)
            {
                if (taskOnly) destination.CopyTaskContextFrom(from);
                else destination.CopyFrom(from);
            }
        }
    }
}
