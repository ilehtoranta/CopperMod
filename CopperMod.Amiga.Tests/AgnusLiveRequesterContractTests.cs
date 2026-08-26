using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CustomChips.Agnus;

namespace CopperMod.Amiga.Tests;

public sealed class AgnusLiveRequesterContractTests
{
    [Fact]
    public void RequestLatchPublishesPreArbitrationIntentAndConsumesOneExactGrant()
    {
        var latch = new AgnusLiveRequestLatch(AgnusChipSlotOwner.Copper);
        var published = latch.Publish(
            AmigaBusAccessKind.Copper,
            address: 0x1200,
            requestedCycle: 10,
            earliestEligibleCycle: 12,
            AgnusLiveWordTransfer.Read);

        Assert.True(latch.TryPeek(out var pending));
        Assert.Equal(published.Generation, pending.Generation);
        Assert.Equal((uint)0x1200, pending.Address);
        Assert.Equal(10, pending.RequestedCycle);
        Assert.Equal(12, pending.EarliestEligibleCycle);
        Assert.Equal(AgnusLiveWordTransfer.Read, pending.Transfer);

        var grant = new AgnusLiveSlotGrant(
            pending,
            slotCycle: 12,
            completedCycle: 16,
            sampledValue: 0x4E71);
        var consumed = latch.Consume(grant);

        Assert.Equal(pending.Generation, consumed.Generation);
        Assert.False(latch.TryPeek(out _));
    }

    [Fact]
    public void DenialDoesNotMutateOrRepublishPendingIntent()
    {
        var latch = new AgnusLiveRequestLatch(AgnusChipSlotOwner.Blitter);
        var published = latch.Publish(
            AmigaBusAccessKind.Blitter,
            address: 0x2200,
            requestedCycle: 20,
            earliestEligibleCycle: 20,
            AgnusLiveWordTransfer.Write,
            writeValue: 0xA55A);

        for (var deniedSlot = 20; deniedSlot < 36; deniedSlot += 4)
        {
            Assert.True(latch.TryPeek(out var stillPending));
            Assert.Equal(published.Generation, stillPending.Generation);
            Assert.Equal(published.Address, stillPending.Address);
            Assert.Equal(published.WriteValue, stillPending.WriteValue);
        }
    }

    [Fact]
    public void DeferralPreservesIntentAndGenerationWhileMovingEligibilityForward()
    {
        var latch = new AgnusLiveRequestLatch(AgnusChipSlotOwner.Disk);
        var published = latch.Publish(
            AmigaBusAccessKind.DiskDma,
            address: 0x2600,
            requestedCycle: 20,
            earliestEligibleCycle: 24,
            AgnusLiveWordTransfer.Write,
            writeValue: 0x4489);

        var deferred = latch.Defer(published.Generation, 28);

        Assert.Equal(published.Generation, deferred.Generation);
        Assert.Equal(published.Owner, deferred.Owner);
        Assert.Equal(published.Kind, deferred.Kind);
        Assert.Equal(published.Address, deferred.Address);
        Assert.Equal(published.RequestedCycle, deferred.RequestedCycle);
        Assert.Equal(published.Transfer, deferred.Transfer);
        Assert.Equal(published.WriteValue, deferred.WriteValue);
        Assert.Equal(published.Channel, deferred.Channel);
        Assert.Equal(28, deferred.EarliestEligibleCycle);
        Assert.True(latch.TryPeek(out var pending));
        Assert.Equal(deferred, pending);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => latch.Defer(published.Generation, 28));
    }

    [Fact]
    public void DuplicatePublicationAndStaleGrantFailClosed()
    {
        var latch = new AgnusLiveRequestLatch(AgnusChipSlotOwner.Paula);
        var first = latch.Publish(
            AmigaBusAccessKind.PaulaDma,
            address: 0x3000,
            requestedCycle: 24,
            earliestEligibleCycle: 24,
            AgnusLiveWordTransfer.Read,
            channel: 2);

        Assert.Throws<InvalidOperationException>(
            () => latch.Publish(
                AmigaBusAccessKind.PaulaDma,
                address: 0x3002,
                requestedCycle: 28,
                earliestEligibleCycle: 28,
                AgnusLiveWordTransfer.Read,
                channel: 2));

        latch.Consume(new AgnusLiveSlotGrant(first, 24, 28, 0x1111));
        var second = latch.Publish(
            AmigaBusAccessKind.PaulaDma,
            address: 0x3002,
            requestedCycle: 28,
            earliestEligibleCycle: 28,
            AgnusLiveWordTransfer.Read,
            channel: 2);

        Assert.NotEqual(first.Generation, second.Generation);
        Assert.Throws<InvalidOperationException>(
            () => latch.Consume(new AgnusLiveSlotGrant(first, 32, 36, 0x2222)));
        Assert.True(latch.TryPeek(out var stillPending));
        Assert.Equal(second.Generation, stillPending.Generation);
    }

    [Fact]
    public void WrongOwnerAndDoubleCommitFailClosed()
    {
        var latch = new AgnusLiveRequestLatch(AgnusChipSlotOwner.Copper);
        var request = latch.Publish(
            AmigaBusAccessKind.Copper,
            address: 0x3400,
            requestedCycle: 32,
            earliestEligibleCycle: 32,
            AgnusLiveWordTransfer.Read);
        var wrongOwner = new AgnusLiveSlotRequest(
            AgnusChipSlotOwner.Disk,
            request.Generation,
            request.Kind,
            request.Address,
            request.RequestedCycle,
            request.EarliestEligibleCycle,
            request.Transfer);

        Assert.Throws<InvalidOperationException>(
            () => latch.Consume(new AgnusLiveSlotGrant(wrongOwner, 32, 36, 0x1234)));
        Assert.True(latch.HasPending);

        var grant = new AgnusLiveSlotGrant(request, 32, 36, 0x1234);
        latch.Consume(grant);
        Assert.Throws<InvalidOperationException>(() => latch.Consume(grant));
    }

    [Fact]
    public void ExactGenerationCancellationInvalidatesLaterGrant()
    {
        var latch = new AgnusLiveRequestLatch(AgnusChipSlotOwner.Disk);
        var request = latch.Publish(
            AmigaBusAccessKind.DiskDma,
            address: 0x4000,
            requestedCycle: 40,
            earliestEligibleCycle: 44,
            AgnusLiveWordTransfer.Write,
            writeValue: 0x4489);

        var cancelled = latch.Cancel(request.Generation);

        Assert.Equal(request.Generation, cancelled.Generation);
        Assert.False(latch.HasPending);
        Assert.Throws<InvalidOperationException>(
            () => latch.Consume(new AgnusLiveSlotGrant(request, 44, 48, 0x4489)));
    }

    [Fact]
    public void GrantBeforeEligibilityIsRejectedBeforeRequesterMutation()
    {
        var latch = new AgnusLiveRequestLatch(AgnusChipSlotOwner.Bitplane);
        var request = latch.Publish(
            AmigaBusAccessKind.Bitplane,
            address: 0x5000,
            requestedCycle: 50,
            earliestEligibleCycle: 56,
            AgnusLiveWordTransfer.Read,
            channel: 0);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AgnusLiveSlotGrant(request, 52, 56, 0xCAFE));
        Assert.True(latch.TryPeek(out var stillPending));
        Assert.Equal(request.Generation, stillPending.Generation);
    }

    [Fact]
    public void TransitionLatchIsStableMonotonicAndStaleSafe()
    {
        var latch = new AgnusLiveTransitionLatch(AgnusChipSlotOwner.Paula);
        var first = latch.Publish(
            cycle: 80,
            AgnusLiveTransitionPhase.BeforeSlotSelection);

        Assert.True(latch.TryPeek(out var peeked));
        Assert.Equal(first.Generation, peeked.Generation);
        Assert.Equal(first.Cycle, peeked.Cycle);
        Assert.Equal(first.Phase, peeked.Phase);
        latch.Consume(first);

        var second = latch.Publish(
            cycle: 80,
            AgnusLiveTransitionPhase.AfterSlotCommit);
        Assert.True(second.Generation > first.Generation);
        Assert.Throws<InvalidOperationException>(() => latch.Consume(first));
        Assert.True(latch.HasPending);
        latch.Consume(second);
        Assert.False(latch.HasPending);
    }

    [Fact]
    public void SameCycleOrderingPlacesTransitionsAroundSelectionAndCommit()
    {
        var before = AgnusLiveTimelineOrdering.GetTransitionOrder(
            AgnusLiveTransitionPhase.BeforeSlotSelection);
        var after = AgnusLiveTimelineOrdering.GetTransitionOrder(
            AgnusLiveTransitionPhase.AfterSlotCommit);

        Assert.True(before < AgnusLiveTimelineOrdering.SlotSelection);
        Assert.True(
            AgnusLiveTimelineOrdering.SlotSelection <
            AgnusLiveTimelineOrdering.SlotCommit);
        Assert.True(AgnusLiveTimelineOrdering.SlotCommit < after);
    }

    [Fact]
    public void WarmRequestPublishPeekCommitLoopAllocatesNothing()
    {
        var latch = new AgnusLiveRequestLatch(AgnusChipSlotOwner.Copper);
        RunCycles(ref latch, 32);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var checksum = RunCycles(ref latch, 10_000);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.NotEqual(0UL, checksum);
        Assert.Equal(0, allocated);
    }

    private static ulong RunCycles(ref AgnusLiveRequestLatch latch, int count)
    {
        ulong checksum = 0;
        for (var index = 0; index < count; index++)
        {
            var cycle = index * 4L;
            var request = latch.Publish(
                AmigaBusAccessKind.Copper,
                address: (uint)(index * 2),
                requestedCycle: cycle,
                earliestEligibleCycle: cycle,
                AgnusLiveWordTransfer.Read);
            if (!latch.TryPeek(out var pending))
            {
                throw new InvalidOperationException("The published request disappeared.");
            }

            var grant = new AgnusLiveSlotGrant(
                pending,
                cycle,
                cycle + 4,
                sampledValue: (ushort)index);
            var consumed = latch.Consume(grant);
            checksum = unchecked(
                (checksum * 1099511628211UL) ^
                consumed.Generation ^
                consumed.Address);
        }

        return checksum;
    }
}
