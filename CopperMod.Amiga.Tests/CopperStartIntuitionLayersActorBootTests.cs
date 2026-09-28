using Amiga;
using CopperMod.Amiga.CopperStart.Intuition;
using CopperMod.Amiga.CopperStart.Layers;
using Xunit.Abstractions;
using Fixture = CopperMod.Amiga.Tests.CopperStartIntuitionCallbackBootTests;
using GraphicsLvo = CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsLvo;
using PortableIntuition = global::CopperStart.Intuition;
using PortableLayers = global::CopperStart.Layers;

namespace CopperMod.Amiga.Tests;

/// <summary>
/// Ordinary guest Layers Hook instructions, their real RTS, and the outer task
/// scheduler exercise the unpublished Intuition admission boundary. No test
/// callback, actor substitution, lock grant, or completion drives the run.
/// This does not qualify native Exec frames, interrupts, or public profiles.
/// </summary>
public sealed class CopperStartIntuitionLayersActorBootTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void RealLayersHookRefusesBlockingIntuitionBeforeReservationAndResumesItsOriginalActor(int workerPriority)
    {
        using var machine = Fixture.CreateMachine();
        var boot = new AmigaBootController(machine);
        boot.StartBootFromDisk(Fixture.CreateBootableDisk());
        var bus = machine.Bus;
        var actor = Fixture.CurrentTask(bus);
        var library = APTR.FromPointer(boot.CopperStartLayersLibraryBase);
        var layersRoot = APTR.FromPointer(boot.CopperStartLayersRootAddress);
        Assert.True(boot.HasCopperStartLayers);
        Assert.True(library.IsNotNull && layersRoot.IsNotNull && actor.IsNotNull);
        var memory = new LayersTestGuestMemory(bus);
        var layerRead = new ActorObservationMemory(bus);
        var authority = new PortableLayers.LayersActorAuthority
        {
            Root = layersRoot, LibraryBase = library,
            ExecBase = APTR.FromPointer(AmigaKickstartHost.ExecLibraryBase),
        };
        var initialOpenCount = ExecLibraryCodec.Read(ref memory, library).OpenCount;
        var initialLayerInfos = PortableLayers.LayersPrivateRootCore.FirstLayerInfo(ref layerRead, layersRoot);
        AssertRestrictions(ref layerRead, authority, actor, PortableLayers.LayersActorRestrictions.None);

        var borrowed = new Fixture.BorrowAuthority();
        var callerCode = borrowed.Allocate(bus, 0x1000);
        var layerHookCode = borrowed.Allocate(bus, 0x1000);
        var deniedHookCode = borrowed.Allocate(bus, 0x1000);
        var layerHook = borrowed.Hook(bus, layerHookCode.Raw);
        var deniedHook = borrowed.Hook(bus, deniedHookCode.Raw);
        var target = borrowed.Allocate(bus, 8);
        var message = borrowed.Allocate(bus, 8);
        var user = Fixture.GuestStack.Allocate(bus);
        var supervisor = Fixture.GuestStack.Allocate(bus);
        Fixture.SetTaskStack(bus, actor, user);
        Fixture.StartRequester(machine, callerCode.Raw, user, supervisor);
        var initialSignals = Signals.Read(bus, actor);
        var availableBeforeOwnedResources = AvailableSetupMemory(bus);
        var surface = CreateSetupLayer(bus, library);

        var configuration = Fixture.CreateConfiguration();
        Assert.True(PortableIntuition.IntuitionExecutionRootCore.TryMeasure(configuration, out var rootBytes));
        Assert.Equal(PortableIntuition.IntuitionStateResult.Success,
            boot.TryCreateUnpublishedIntuitionExecution(configuration, (sbyte)workerPriority,
                borrowed.AllowsSpan, borrowed.AllowsEntry, out var created));
        Assert.NotNull(created);
        var host = created!;
        var worker = host.Worker;
        var request = host.GetGateway(IntuitionExecutionGateway.Request);
        var stop = host.GetGateway(IntuitionExecutionGateway.Stop);
        var close = Lvo(library, -12); // Standard library management vector.

        var caller = new Fixture.GuestProgram(bus, callerCode.Raw);
        var afterStart = caller.Call(host.GetGateway(IntuitionExecutionGateway.StartWait));
        caller.MoveAddress(6, library.Raw);
        var afterOpen = caller.Call(Lvo(library, -6));
        caller.MoveAddress(0, layerHook.Raw);
        caller.MoveAddress(1, surface.RastPort.Raw);
        caller.MoveAddress(2, 0);
        var layerCall = caller.Address;
        var afterLayer = caller.Call(Lvo(library, LayersLvo.DoHookClipRects));
        var allowed = EmitRequest(caller, request,
            PortableIntuition.IntuitionCommandOperation.SnapshotPublicState,
            deniedHook, target, message);
        caller.MoveAddress(6, library.Raw);
        caller.MoveAddress(1, surface.Layer.Raw);
        var afterDelete = caller.Call(Lvo(library, LayersLvo.DeleteLayer));
        caller.MoveAddress(0, surface.LayerInfo.Raw);
        caller.Call(Lvo(library, LayersLvo.DisposeLayerInfo));
        caller.MoveAddress(6, AmigaKickstartHost.GraphicsLibraryBase);
        caller.MoveAddress(0, surface.BitMap.Raw);
        caller.Call(Lvo(APTR.FromPointer(AmigaKickstartHost.GraphicsLibraryBase), (short)GraphicsLvo.FreeBitMap));
        caller.MoveAddress(6, library.Raw);
        var afterClose = caller.Call(close);
        caller.Call(stop);
        caller.CallUntilNotBusy(host.GetGateway(IntuitionExecutionGateway.Reap));
        var park = caller.Park();

        var hook = new Fixture.GuestProgram(bus, layerHookCode.Raw);
        var refusedSnapshot = EmitRequest(hook, request,
            PortableIntuition.IntuitionCommandOperation.SnapshotPublicState,
            deniedHook, target, message);
        var refusedHook = EmitRequest(hook, request,
            PortableIntuition.IntuitionCommandOperation.InvokeGuestHook,
            deniedHook, target, message);
        hook.MoveData(0, 0xE123_4567);
        var realRts = hook.Return();
        var unentered = new Fixture.GuestProgram(bus, deniedHookCode.Raw);
        unentered.StoreLong(target.Raw, 0xBAD0_0001);
        unentered.MoveData(0, uint.MaxValue);
        unentered.Return();
        Assert.False(bus.HasHostGateway(layerHookCode.Raw));
        Assert.False(bus.HasHostGateway(deniedHookCode.Raw));

        var trace = new OuterTrace(machine, boot, host, output);
        trace.Until(actor, afterStart);
        Assert.Equal(PortableIntuition.IntuitionTaskPhase.Running, host.Snapshot().Phase);
        Assert.Equal((uint)PortableIntuition.IntuitionStateResult.Success, machine.Cpu.State.D[0]);
        trace.Until(actor, afterOpen);
        Assert.Equal(library.Raw, machine.Cpu.State.D[0]);
        Assert.Equal((ushort)(initialOpenCount + 1), ExecLibraryCodec.Read(ref memory, library).OpenCount);
        var beforeLayer = trace.Until(actor, layerCall);
        var entered = trace.Until(actor, layerHookCode.Raw);
        Assert.Equal((ushort)0xFF00, entered.Opcode);
        Assert.Equal(Lvo(library, LayersLvo.DoHookClipRects), entered.PreviousPc);
        Assert.Equal(layerHook.Raw, entered.Address[0]);
        Assert.Equal(surface.RastPort.Raw, entered.Address[2]);
        Assert.Equal(beforeLayer.Sp - 8 - LayersHostCallbackToken.Size, entered.Sp);
        Assert.Equal(LayersHostServices.HookContinuationAddress, bus.ReadLong(entered.Sp));
        Assert.True(LayersHookMessageCodec.TryRead(ref memory, APTR.FromPointer(entered.Address[1]),
            out LayerBackfillMessage hookMessage));
        Assert.Equal(surface.Layer, hookMessage.Layer);
        Assert.Equal(LayersRectangleCodec.Create(0, 0, 15, 15), hookMessage.Bounds);
        var retained = PortableLayers.LayersPrivateRootCore.ActiveContinuation(ref layerRead, layersRoot);
        Assert.True(retained.IsNotNull);
        var retainedRestrictions = PortableLayers.LayersActorRestrictions.MemberLayerLock |
            PortableLayers.LayersActorRestrictions.CallbackContinuation;
        AssertRestrictions(ref layerRead, authority, actor, retainedRestrictions);

        AssertRejected(refusedSnapshot);
        AssertRejected(refusedHook);
        var returning = trace.Until(actor, realRts);
        Assert.Equal(0xE123_4567u, returning.Data[0]);
        AssertRestrictions(ref layerRead, authority, actor, retainedRestrictions);
        var returned = trace.Until(actor, LayersHostServices.HookContinuationAddress);
        Assert.Equal((ushort)0x4E75, returned.Opcode);
        Assert.Equal(realRts, returned.PreviousPc);
        Assert.Equal(returning.Sp + 4, returned.Sp);
        Assert.Equal(0xE123_4567u, returned.Data[0]);
        Assert.Equal(retained, PortableLayers.LayersPrivateRootCore.ActiveContinuation(ref layerRead, layersRoot));
        AssertRestrictions(ref layerRead, authority, actor, retainedRestrictions);
        var resumed = trace.Until(actor, afterLayer);
        Assert.Equal((ushort)0xFF00, resumed.Opcode);
        Assert.Equal(LayersHostServices.HookContinuationAddress, resumed.PreviousPc);
        Assert.Equal(beforeLayer.Sp, resumed.Sp);
        Assert.True(PortableLayers.LayersPrivateRootCore.ActiveContinuation(ref layerRead, layersRoot).IsNull);
        AssertRestrictions(ref layerRead, authority, actor, PortableLayers.LayersActorRestrictions.None);

        var allowedBefore = trace.Until(actor, allowed.Call);
        var allowedAfter = trace.Until(actor, allowed.Return);
        AssertRequestRestored(allowedBefore, allowedAfter, PortableIntuition.IntuitionStateResult.Success);
        Assert.Equal(3u, host.CompletedCount);
        Assert.Equal(PortableIntuition.IntuitionStateResult.Success, host.LastCompletedResult.Status);
        Assert.Equal(PortableIntuition.IntuitionCallbackCompletionKind.None, host.LastCompletedResult.Callback.Kind);
        Assert.Contains(trace.Samples, sample => sample.Task == worker);
        if (workerPriority < 0)
            Assert.Contains(trace.Samples, sample => sample.Task == actor && sample.TaskState == TaskState.Waiting && sample.Signals.Wait != 0);
        trace.Until(actor, afterDelete);
        Assert.NotEqual(0u, machine.Cpu.State.D[0]);
        trace.Until(actor, afterClose);
        Assert.Equal(initialOpenCount, ExecLibraryCodec.Read(ref memory, library).OpenCount);
        Assert.Equal(initialLayerInfos, PortableLayers.LayersPrivateRootCore.FirstLayerInfo(ref layerRead, layersRoot));
        var finished = trace.Until(actor, park);
        Assert.Equal((uint)PortableIntuition.IntuitionStateResult.Success, finished.Data[0]);
        Assert.Equal(PortableIntuition.IntuitionTaskPhase.Idle, host.Snapshot().Phase);
        Assert.Equal((ushort)0, host.Snapshot().CommandsInUse);
        Assert.Equal((ushort)0, host.Snapshot().CallbacksInUse);
        Assert.Equal(user.Upper, finished.Sp);
        Assert.Equal(user.Upper, finished.Usp);
        Assert.Equal(supervisor.Upper, finished.Ssp);
        Assert.Equal(initialSignals, Signals.Read(bus, actor));
        Assert.Equal(PortableIntuition.IntuitionStateResult.Success, host.LastFault);
        Assert.DoesNotContain(trace.Samples, sample => sample.Pc == deniedHookCode.Raw);
        Assert.Equal(0u, bus.ReadLong(target.Raw));
        Assert.Equal(1, trace.Samples.Count(sample => sample.Task == actor && sample.Pc == layerHookCode.Raw));
        Assert.Equal(1, trace.Samples.Count(sample => sample.Task == actor && sample.Pc == LayersHostServices.HookContinuationAddress));
        Assert.Equal(1, trace.Samples.Count(sample => sample.Task == actor && sample.Pc == close));
        Assert.Equal(1, trace.Samples.Count(sample => sample.Task == actor && sample.Pc == stop));
        Assert.All(trace.Samples.Where(sample => sample.Task == actor), sample =>
        {
            Assert.Equal(0, sample.Sr & M68kCpuState.Supervisor);
            Assert.Equal(sample.Sp, sample.Usp);
            Assert.Equal(supervisor.Upper, sample.Ssp);
            Assert.InRange(sample.Sp, user.Lower, user.Upper);
        });
        user.AssertIntact(bus);
        supervisor.AssertIntact(bus);
        Assert.Equal(PortableIntuition.IntuitionStateResult.Success, host.DestroyUnpublished());
        Assert.False(host.Snapshot().Live);
        Assert.Equal(availableBeforeOwnedResources, AvailableSetupMemory(bus));
        trace.Dump();

        void AssertRejected(RequestSite site)
        {
            var before = trace.Until(actor, site.Call);
            var firstSample = trace.Samples.Count - 1;
            var arenaBefore = ReadBytes(bus, host.Root.Address, rootBytes);
            var actorBefore = Signals.Read(bus, actor);
            var workerBefore = Signals.Read(bus, worker);
            var workerState = Fixture.ReadTask(bus, worker).State;
            AssertRestrictions(ref layerRead, authority, actor, retainedRestrictions);
            var after = trace.Until(actor, site.Return);
            AssertRequestRestored(before, after, PortableIntuition.IntuitionStateResult.Busy);
            // This is an opaque equality check, not a second private codec.
            // Even acquire-then-release would advance a slot generation here.
            Assert.Equal(arenaBefore, ReadBytes(bus, host.Root.Address, rootBytes));
            Assert.Equal(actorBefore, Signals.Read(bus, actor));
            Assert.Equal(workerBefore, Signals.Read(bus, worker));
            Assert.Equal(workerState, Fixture.ReadTask(bus, worker).State);
            Assert.Equal((ushort)0, host.Snapshot().CommandsInUse);
            Assert.Equal((ushort)0, host.Snapshot().CallbacksInUse);
            Assert.Equal(PortableIntuition.IntuitionStateResult.Busy, host.LastCompletedResult.Status);
            Assert.Equal(PortableIntuition.IntuitionCallbackCompletionKind.None, host.LastCompletedResult.Callback.Kind);
            Assert.Equal(retained, PortableLayers.LayersPrivateRootCore.ActiveContinuation(ref layerRead, layersRoot));
            AssertRestrictions(ref layerRead, authority, actor, retainedRestrictions);
            Assert.All(trace.Samples.Skip(firstSample), sample =>
            {
                Assert.Equal(actor, sample.Task);
                Assert.Equal(TaskState.Running, sample.TaskState);
                Assert.NotEqual(host.GetGateway(IntuitionExecutionGateway.Collect), sample.Pc);
                Assert.NotEqual(host.GetGateway(IntuitionExecutionGateway.HookReturn), sample.Pc);
            });
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void RealLayersReturnFromAnotherScheduledActorFaultsWithoutConsumingTheOriginalEnvelope(int workerPriority)
        => RunTerminalReturnFault(workerPriority, wrongActor: true);

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void PrematureRealLayersReturnFaultsWithoutApplyingOrRetiringTheOriginalEnvelope(int workerPriority)
        => RunTerminalReturnFault(workerPriority, wrongActor: false);

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void ARealLayersHookReturningToTheBlockedGatewayFaultsWithoutPoppingItsPacketOrConsumingTheEnvelope(int workerPriority)
    {
        using var run = new CallbackReturnRun(workerPriority, twoRectangles: false, output);
        var caller = run.Caller();
        var sites = run.EmitStartAndLayer(caller);
        caller.StoreLong(run.Marker.Raw, 0xBAD0_0001);
        caller.Park();
        // MOVE.L #BlockContinuationAddress,(A7) changes only the real Hook's
        // return LONG. Its typed packet and the private host envelope remain
        // untouched; the following RTS genuinely enters the wrong gateway.
        run.Bus.WriteWord(run.HookCode.Raw, 0x2EBC, 0);
        run.Bus.WriteLong(run.HookCode.Raw + 2, LayersHostServices.BlockContinuationAddress, 0);
        var hook = new Fixture.GuestProgram(run.Bus, run.HookCode.Raw + 6, 0x1000 - 6);
        hook.StoreLong(run.Marker.Raw, 1);
        EmitReturnSentinels(hook);
        var realRts = hook.Return();
        Assert.False(run.Bus.HasHostGateway(run.HookCode.Raw));

        run.Trace.Until(run.Actor, sites.StartReturn);
        var beforeLayer = run.Trace.Until(run.Actor, sites.Call);
        var entered = run.Trace.Until(run.Actor, run.HookCode.Raw);
        var token = AssertActiveReturn(run, beforeLayer, entered);
        var expectedIdentity = run.Boot.CopperStartLayersPendingCallbackIdentityForTest;
        var overwritten = run.Trace.Until(run.Actor, run.HookCode.Raw + 6);
        Assert.Equal((ushort)0x2EBC, overwritten.Opcode);
        Assert.Equal(run.HookCode.Raw, overwritten.PreviousPc);
        Assert.Equal(entered.Sp, overwritten.Sp);
        Assert.Equal(LayersHostServices.BlockContinuationAddress, run.Bus.ReadLong(overwritten.Sp));
        var sdk = new LayersTestGuestMemory(run.Bus);
        Assert.Equal(token, LayersHostCallbackTokenCodec.Read(ref sdk, APTR.FromPointer(overwritten.Sp + 4)));
        var returning = run.Trace.Until(run.Actor, realRts);
        var beforeFault = run.Trace.Until(run.Actor, LayersHostServices.BlockContinuationAddress);
        Assert.Equal((ushort)0x4E75, beforeFault.Opcode);
        Assert.Equal(realRts, beforeFault.PreviousPc);
        Assert.Equal(returning.Sp + 4, beforeFault.Sp);
        Assert.Equal(token, LayersHostCallbackTokenCodec.Read(ref sdk, APTR.FromPointer(beforeFault.Sp)));
        Assert.Equal(0, run.Boot.CopperStartLayersPendingWaitCountForTest);
        Assert.Equal(LayersCallbackReturnFault.None, run.Boot.CopperStartLayersLastCallbackReturnFaultForTest);
        var watched = CaptureReturnFaultStorage(run, token.Continuation, run.Actor, null);

        var afterFault = run.Trace.FaultNext(LayersHostServices.BlockContinuationAddress);
        Assert.Equal(LayersCallbackReturnFault.InvalidBoundary, run.Boot.CopperStartLayersLastCallbackReturnFaultForTest);
        AssertRejectedReturnState(beforeFault, afterFault, LayersHostServices.BlockContinuationAddress);
        foreach (var span in watched) Assert.Equal(span.Bytes, ReadBytes(run.Bus, span.Address, (uint)span.Bytes.Length));
        Assert.Equal(0, run.Boot.CopperStartLayersPendingWaitCountForTest);
        Assert.True(run.Boot.CopperStartLayersHasPendingCallbackForTest);
        Assert.True(PortableLayers.LayersCallbackContextCore.SameIdentity(expectedIdentity,
            run.Boot.CopperStartLayersPendingCallbackIdentityForTest));
        Assert.Equal(token.Nonce, run.Boot.CopperStartLayersPendingCallbackNonceForTest);
        Assert.Equal(token, LayersHostCallbackTokenCodec.Read(ref sdk, APTR.FromPointer(afterFault.Sp)));
        var observation = new ActorObservationMemory(run.Bus);
        Assert.True(PortableLayers.LayersCallbackContextCore.TryRead(ref observation, run.Authority,
            run.Actor, token.ContinuationToken, out var retained));
        Assert.Equal(PortableLayers.LayersCallbackPhase.AwaitingReturn, retained.Phase);
        Assert.True(PortableLayers.LayersCallbackContextCore.SameIdentity(expectedIdentity, retained.Identity));
        AssertRestrictions(ref observation, run.Authority, run.Actor,
            PortableLayers.LayersActorRestrictions.MemberLayerLock | PortableLayers.LayersActorRestrictions.CallbackContinuation);
        Assert.Equal(1u, run.Bus.ReadLong(run.Marker.Raw));
        Assert.DoesNotContain(run.Trace.Samples, sample => sample.Task == run.Actor && sample.Pc == sites.Return);
        Assert.DoesNotContain(run.Trace.Samples, sample => sample.Task == run.Actor && sample.Pc == LayersHostServices.HookContinuationAddress);
        Assert.Equal(PortableIntuition.IntuitionTaskPhase.Running, run.Host.Snapshot().Phase);
        Assert.True(run.Host.Snapshot().Live);
        Assert.Equal((ushort)0, run.Host.Snapshot().CommandsInUse);
        Assert.Equal((ushort)0, run.Host.Snapshot().CallbacksInUse);
        run.User.AssertIntact(run.Bus);
        run.Supervisor.AssertIntact(run.Bus);
        // Fault evidence retains the callback; there is no test-driven CPU
        // repair, synthetic wait receipt, resumed return, or cleanup claim.
        run.Trace.Dump();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void AGuestReplayedIssuedPacketFaultsWhileTheSecondLayersHookEnvelopeRemainsOwned(int workerPriority)
    {
        using var run = new CallbackReturnRun(workerPriority, twoRectangles: true, output);
        var replay = run.Borrowed.Allocate(run.Bus, LayersHostCallbackToken.Size);
        var caller = run.Caller();
        var sites = run.EmitStartAndLayer(caller);
        caller.StoreLong(run.Marker.Raw, 0xBAD0_0001);
        caller.Park();

        // The real Hook chooses its branch from guest RAM. The first invocation
        // copies its issued packet out; the second supplies those old bytes at
        // the current return SP. No host callback or private-state setter drives
        // the two invocations or changes their owned continuations.
        var firstCode = EmitPacketCopy(run, run.HookCode.Raw + 10, replay, toGuestStorage: true);
        var firstHook = new Fixture.GuestProgram(run.Bus, firstCode,
            run.HookCode.Raw + 0x1000 - firstCode);
        firstHook.StoreLong(run.Marker.Raw, 1);
        EmitReturnSentinels(firstHook);
        var firstRts = firstHook.Return();
        var secondCode = firstHook.Address;
        var secondCopyEnd = EmitPacketCopy(run, secondCode, replay, toGuestStorage: false);
        var secondHook = new Fixture.GuestProgram(run.Bus, secondCopyEnd,
            run.HookCode.Raw + 0x1000 - secondCopyEnd);
        secondHook.StoreLong(run.Marker.Raw, 2);
        EmitReturnSentinels(secondHook);
        var secondRts = secondHook.Return();
        run.Bus.WriteWord(run.HookCode.Raw, 0x4AB9, 0); // TST.L marker (absolute long).
        run.Bus.WriteLong(run.HookCode.Raw + 2, run.Marker.Raw, 0);
        run.Bus.WriteWord(run.HookCode.Raw + 6, 0x6600, 0); // BNE.W second invocation.
        var branchDisplacement = checked((int)(secondCode - (run.HookCode.Raw + 8)));
        Assert.InRange(branchDisplacement, 1, short.MaxValue);
        run.Bus.WriteWord(run.HookCode.Raw + 8, (ushort)branchDisplacement, 0);
        Assert.False(run.Bus.HasHostGateway(run.HookCode.Raw));

        run.Trace.Until(run.Actor, sites.StartReturn);
        var beforeLayer = run.Trace.Until(run.Actor, sites.Call);
        var firstEntered = run.Trace.Until(run.Actor, run.HookCode.Raw);
        var firstToken = AssertActiveReturn(run, beforeLayer, firstEntered);
        run.Trace.Until(run.Actor, firstRts);
        var sdk = new LayersTestGuestMemory(run.Bus);
        Assert.Equal(firstToken, LayersHostCallbackTokenCodec.Read(ref sdk, replay));
        var firstReturned = run.Trace.Until(run.Actor, LayersHostServices.HookContinuationAddress);
        Assert.Equal((ushort)0x4E75, firstReturned.Opcode);
        Assert.Equal(firstRts, firstReturned.PreviousPc);
        var secondEntered = run.Trace.Until(run.Actor, run.HookCode.Raw);
        var secondToken = AssertActiveReturn(run, beforeLayer, secondEntered);
        Assert.Equal(firstEntered.Sp, secondEntered.Sp);
        Assert.Equal(firstToken.Continuation, secondToken.Continuation);
        Assert.NotEqual(firstToken.Nonce, secondToken.Nonce);
        Assert.NotEqual(firstToken.ContinuationToken, secondToken.ContinuationToken);
        var expectedIdentity = run.Boot.CopperStartLayersPendingCallbackIdentityForTest;
        run.Trace.Until(run.Actor, secondRts);
        var beforeFault = run.Trace.Until(run.Actor, LayersHostServices.HookContinuationAddress);
        Assert.Equal((ushort)0x4E75, beforeFault.Opcode);
        Assert.Equal(secondRts, beforeFault.PreviousPc);
        Assert.Equal(secondEntered.Sp + 4, beforeFault.Sp);
        Assert.Equal(firstToken, LayersHostCallbackTokenCodec.Read(ref sdk, APTR.FromPointer(beforeFault.Sp)));
        var watched = CaptureReturnFaultStorage(run, secondToken.Continuation, run.Actor, null);

        var afterFault = run.Trace.FaultNext();
        Assert.Equal(LayersCallbackReturnFault.InvalidToken, run.Boot.CopperStartLayersLastCallbackReturnFaultForTest);
        AssertRejectedReturnState(beforeFault, afterFault);
        foreach (var span in watched) Assert.Equal(span.Bytes, ReadBytes(run.Bus, span.Address, (uint)span.Bytes.Length));
        Assert.True(run.Boot.CopperStartLayersHasPendingCallbackForTest);
        Assert.True(PortableLayers.LayersCallbackContextCore.SameIdentity(expectedIdentity,
            run.Boot.CopperStartLayersPendingCallbackIdentityForTest));
        Assert.Equal(secondToken.Nonce, run.Boot.CopperStartLayersPendingCallbackNonceForTest);
        var observation = new ActorObservationMemory(run.Bus);
        Assert.True(PortableLayers.LayersCallbackContextCore.TryRead(ref observation, run.Authority,
            run.Actor, secondToken.ContinuationToken, out var retained));
        Assert.Equal(PortableLayers.LayersCallbackPhase.AwaitingReturn, retained.Phase);
        Assert.True(PortableLayers.LayersCallbackContextCore.SameIdentity(expectedIdentity, retained.Identity));
        AssertRestrictions(ref observation, run.Authority, run.Actor,
            PortableLayers.LayersActorRestrictions.MemberLayerLock | PortableLayers.LayersActorRestrictions.CallbackContinuation);
        Assert.Equal(2u, run.Bus.ReadLong(run.Marker.Raw));
        Assert.Equal(2, run.Trace.Samples.Count(sample => sample.Task == run.Actor && sample.Pc == run.HookCode.Raw));
        Assert.Equal(1, run.Trace.Samples.Count(sample => sample.Task == run.Actor && sample.Pc == firstRts));
        Assert.Equal(1, run.Trace.Samples.Count(sample => sample.Task == run.Actor && sample.Pc == secondRts));
        Assert.DoesNotContain(run.Trace.Samples, sample => sample.Task == run.Actor && sample.Pc == sites.Return);
        Assert.Equal(PortableIntuition.IntuitionTaskPhase.Running, run.Host.Snapshot().Phase);
        Assert.True(run.Host.Snapshot().Live);
        Assert.Equal((ushort)0, run.Host.Snapshot().CommandsInUse);
        Assert.Equal((ushort)0, run.Host.Snapshot().CallbacksInUse);
        run.User.AssertIntact(run.Bus);
        run.Supervisor.AssertIntact(run.Bus);
        // This proves rejection of a supplied stale packet, not an old jump
        // onto a newly valid current packet. The halted second issue is retained.
        run.Trace.Dump();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void ARealLayersReturnGatewayReplayAfterCompletionFaultsWithoutChangingTheRetiredOwner(int workerPriority)
    {
        using var run = new CallbackReturnRun(workerPriority, twoRectangles: false, output);
        var caller = run.Caller();
        var sites = run.EmitStartAndLayer(caller);
        EmitReturnSentinels(caller);
        var replayCall = caller.Address;
        var replayReturn = caller.Call(LayersHostServices.HookContinuationAddress);
        caller.StoreLong(run.Marker.Raw, 0xBAD0_0001);
        caller.Park();
        var hook = run.Callback();
        hook.StoreLong(run.Marker.Raw, 1);
        EmitReturnSentinels(hook);
        var realRts = hook.Return();

        run.Trace.Until(run.Actor, sites.StartReturn);
        var beforeLayer = run.Trace.Until(run.Actor, sites.Call);
        var entered = run.Trace.Until(run.Actor, run.HookCode.Raw);
        AssertActiveReturn(run, beforeLayer, entered);
        run.Trace.Until(run.Actor, realRts);
        run.Trace.Until(run.Actor, LayersHostServices.HookContinuationAddress);
        var completed = run.Trace.Until(run.Actor, sites.Return);
        AssertRegistersUnchanged(beforeLayer, completed);
        Assert.False(run.Boot.CopperStartLayersHasPendingCallbackForTest);
        Assert.Equal(LayersCallbackReturnFault.None, run.Boot.CopperStartLayersLastCallbackReturnFaultForTest);
        var identityAfterCompletion = run.Boot.CopperStartLayersPendingCallbackIdentityForTest;
        var nonceAfterCompletion = run.Boot.CopperStartLayersPendingCallbackNonceForTest;
        var observation = new ActorObservationMemory(run.Bus);
        Assert.True(PortableLayers.LayersPrivateRootCore.ActiveContinuation(ref observation, run.Root).IsNull);
        AssertRestrictions(ref observation, run.Authority, run.Actor, PortableLayers.LayersActorRestrictions.None);
        run.Trace.Until(run.Actor, replayCall);
        var beforeFault = run.Trace.Until(run.Actor, LayersHostServices.HookContinuationAddress);
        Assert.Equal((ushort)0x4EB9, beforeFault.Opcode);
        Assert.Equal(replayCall, beforeFault.PreviousPc);
        Assert.Equal(replayReturn, run.Bus.ReadLong(beforeFault.Sp));
        // The prior continuation is no longer owned: never inspect its freed
        // address merely because the earlier trace retained that pointer.
        var watched = CaptureReturnFaultStorage(run, APTR.Null, run.Actor, null);
        var afterFault = run.Trace.FaultNext();
        Assert.Equal(LayersCallbackReturnFault.NotPending, run.Boot.CopperStartLayersLastCallbackReturnFaultForTest);
        AssertRejectedReturnState(beforeFault, afterFault);
        foreach (var span in watched) Assert.Equal(span.Bytes, ReadBytes(run.Bus, span.Address, (uint)span.Bytes.Length));
        Assert.False(run.Boot.CopperStartLayersHasPendingCallbackForTest);
        Assert.Equal(identityAfterCompletion, run.Boot.CopperStartLayersPendingCallbackIdentityForTest);
        Assert.Equal(nonceAfterCompletion, run.Boot.CopperStartLayersPendingCallbackNonceForTest);
        Assert.True(PortableLayers.LayersPrivateRootCore.ActiveContinuation(ref observation, run.Root).IsNull);
        AssertRestrictions(ref observation, run.Authority, run.Actor, PortableLayers.LayersActorRestrictions.None);
        Assert.Equal(1u, run.Bus.ReadLong(run.Marker.Raw));
        Assert.Equal(1, run.Trace.Samples.Count(sample => sample.Task == run.Actor && sample.Pc == run.HookCode.Raw));
        Assert.Equal(1, run.Trace.Samples.Count(sample => sample.Task == run.Actor && sample.Pc == sites.Return));
        Assert.DoesNotContain(run.Trace.Samples, sample => sample.Task == run.Actor && sample.Pc == replayReturn);
        Assert.Equal(PortableIntuition.IntuitionTaskPhase.Running, run.Host.Snapshot().Phase);
        Assert.True(run.Host.Snapshot().Live);
        Assert.Equal((ushort)0, run.Host.Snapshot().CommandsInUse);
        Assert.Equal((ushort)0, run.Host.Snapshot().CallbacksInUse);
        run.User.AssertIntact(run.Bus);
        run.Supervisor.AssertIntact(run.Bus);
        run.Trace.Dump();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void TwoRealLayersHooksUseDistinctReturnIssuesAndResumeTheirCallerExactlyOnce(int workerPriority)
    {
        using var run = new CallbackReturnRun(workerPriority, twoRectangles: true, output);
        var caller = run.Caller();
        var sites = run.EmitStartAndLayer(caller);
        caller.MoveAddress(6, run.Library.Raw);
        caller.MoveAddress(1, run.Occluder.Raw);
        var afterDeleteOccluder = caller.Call(Lvo(run.Library, LayersLvo.DeleteLayer));
        caller.MoveAddress(1, run.Surface.Layer.Raw);
        var afterDeleteLayer = caller.Call(Lvo(run.Library, LayersLvo.DeleteLayer));
        caller.MoveAddress(0, run.Surface.LayerInfo.Raw);
        caller.Call(Lvo(run.Library, LayersLvo.DisposeLayerInfo));
        caller.MoveAddress(6, AmigaKickstartHost.GraphicsLibraryBase);
        caller.MoveAddress(0, run.Surface.BitMap.Raw);
        caller.Call(Lvo(APTR.FromPointer(AmigaKickstartHost.GraphicsLibraryBase), (short)GraphicsLvo.FreeBitMap));
        caller.MoveAddress(6, run.Library.Raw);
        var close = Lvo(run.Library, -12);
        var afterClose = caller.Call(close);
        var stop = run.Host.GetGateway(IntuitionExecutionGateway.Stop);
        caller.Call(stop);
        caller.CallUntilNotBusy(run.Host.GetGateway(IntuitionExecutionGateway.Reap));
        var park = caller.Park();

        var hook = run.Callback();
        hook.StoreLong(run.Marker.Raw, 1);
        EmitReturnSentinels(hook);
        var realRts = hook.Return();
        Assert.False(run.Bus.HasHostGateway(run.HookCode.Raw));

        run.Trace.Until(run.Actor, sites.StartReturn);
        var beforeLayer = run.Trace.Until(run.Actor, sites.Call);
        var first = run.Trace.Until(run.Actor, run.HookCode.Raw);
        var firstToken = AssertActiveReturn(run, beforeLayer, first);
        var sdk = new LayersTestGuestMemory(run.Bus);
        Assert.True(LayersHookMessageCodec.TryRead(ref sdk, APTR.FromPointer(first.Address[1]),
            out LayerBackfillMessage firstMessage));
        var firstReturning = run.Trace.Until(run.Actor, realRts);
        var firstReturned = run.Trace.Until(run.Actor, LayersHostServices.HookContinuationAddress);
        Assert.Equal((ushort)0x4E75, firstReturned.Opcode);
        Assert.Equal(realRts, firstReturned.PreviousPc);
        Assert.Equal(firstReturning.Sp + 4, firstReturned.Sp);
        Assert.Equal(firstToken, LayersHostCallbackTokenCodec.Read(ref sdk, APTR.FromPointer(firstReturned.Sp)));

        var second = run.Trace.Until(run.Actor, run.HookCode.Raw);
        Assert.Equal((ushort)0xFF00, second.Opcode);
        Assert.Equal(LayersHostServices.HookContinuationAddress, second.PreviousPc);
        var secondToken = AssertActiveReturn(run, beforeLayer, second);
        Assert.Equal(first.Sp, second.Sp);
        Assert.Equal(firstToken.Continuation, secondToken.Continuation);
        Assert.Equal(firstToken.RecordGeneration, secondToken.RecordGeneration);
        Assert.NotEqual(firstToken.ContinuationToken, secondToken.ContinuationToken);
        Assert.NotEqual(firstToken.Nonce, secondToken.Nonce);
        Assert.True(LayersHookMessageCodec.TryRead(ref sdk, APTR.FromPointer(second.Address[1]),
            out LayerBackfillMessage secondMessage));
        Assert.Equal(run.Surface.Layer, firstMessage.Layer);
        Assert.Equal(run.Surface.Layer, secondMessage.Layer);
        Assert.Equal(
            new[] { LayersRectangleCodec.Create(0, 0, 5, 15), LayersRectangleCodec.Create(10, 0, 15, 15) },
            new[] { firstMessage.Bounds, secondMessage.Bounds }.OrderBy(rectangle => rectangle.MinX).ToArray());

        var secondReturning = run.Trace.Until(run.Actor, realRts);
        var secondReturned = run.Trace.Until(run.Actor, LayersHostServices.HookContinuationAddress);
        Assert.Equal((ushort)0x4E75, secondReturned.Opcode);
        Assert.Equal(realRts, secondReturned.PreviousPc);
        Assert.Equal(secondReturning.Sp + 4, secondReturned.Sp);
        Assert.Equal(secondToken, LayersHostCallbackTokenCodec.Read(ref sdk, APTR.FromPointer(secondReturned.Sp)));
        var resumed = run.Trace.Until(run.Actor, sites.Return);
        Assert.Equal((ushort)0xFF00, resumed.Opcode);
        Assert.Equal(LayersHostServices.HookContinuationAddress, resumed.PreviousPc);
        AssertRegistersUnchanged(beforeLayer, resumed);
        Assert.False(run.Boot.CopperStartLayersHasPendingCallbackForTest);
        Assert.Equal(LayersCallbackReturnFault.None, run.Boot.CopperStartLayersLastCallbackReturnFaultForTest);
        var observation = new ActorObservationMemory(run.Bus);
        Assert.True(PortableLayers.LayersPrivateRootCore.ActiveContinuation(ref observation, run.Root).IsNull);
        AssertRestrictions(ref observation, run.Authority, run.Actor, PortableLayers.LayersActorRestrictions.None);

        run.Trace.Until(run.Actor, afterDeleteOccluder);
        Assert.NotEqual(0u, run.Machine.Cpu.State.D[0]);
        run.Trace.Until(run.Actor, afterDeleteLayer);
        Assert.NotEqual(0u, run.Machine.Cpu.State.D[0]);
        run.Trace.Until(run.Actor, afterClose);
        Assert.Equal(run.InitialOpenCount, ExecLibraryCodec.Read(ref sdk, run.Library).OpenCount);
        Assert.Equal(run.InitialLayerInfos, PortableLayers.LayersPrivateRootCore.FirstLayerInfo(ref observation, run.Root));
        var finished = run.Trace.Until(run.Actor, park);
        Assert.Equal((uint)PortableIntuition.IntuitionStateResult.Success, finished.Data[0]);
        Assert.Equal(PortableIntuition.IntuitionTaskPhase.Idle, run.Host.Snapshot().Phase);
        Assert.Equal((ushort)0, run.Host.Snapshot().CommandsInUse);
        Assert.Equal((ushort)0, run.Host.Snapshot().CallbacksInUse);
        Assert.Equal(run.InitialSignals, Signals.Read(run.Bus, run.Actor));
        Assert.Equal(run.User.Upper, finished.Sp);
        Assert.Equal(run.User.Upper, finished.Usp);
        Assert.Equal(run.Supervisor.Upper, finished.Ssp);
        Assert.Equal(2, run.Trace.Samples.Count(sample => sample.Task == run.Actor && sample.Pc == run.HookCode.Raw));
        Assert.Equal(2, run.Trace.Samples.Count(sample => sample.Task == run.Actor && sample.Pc == realRts));
        Assert.Equal(2, run.Trace.Samples.Count(sample => sample.Task == run.Actor && sample.Pc == LayersHostServices.HookContinuationAddress));
        Assert.Equal(1, run.Trace.Samples.Count(sample => sample.Task == run.Actor && sample.Pc == sites.Return));
        Assert.Equal(1, run.Trace.Samples.Count(sample => sample.Task == run.Actor && sample.Pc == close));
        Assert.Equal(1, run.Trace.Samples.Count(sample => sample.Task == run.Actor && sample.Pc == stop));
        Assert.All(run.Trace.Samples.Where(sample => sample.Task == run.Actor), sample =>
        {
            Assert.Equal(0, sample.Sr & M68kCpuState.Supervisor);
            Assert.Equal(sample.Sp, sample.Usp);
            Assert.Equal(run.Supervisor.Upper, sample.Ssp);
            Assert.InRange(sample.Sp, run.User.Lower, run.User.Upper);
        });
        run.User.AssertIntact(run.Bus);
        run.Supervisor.AssertIntact(run.Bus);
        Assert.Equal(PortableIntuition.IntuitionStateResult.Success, run.Host.DestroyUnpublished());
        Assert.False(run.Host.Snapshot().Live);
        Assert.Equal(run.AvailableBeforeOwnedResources, AvailableSetupMemory(run.Bus));
        run.Trace.Dump();
    }

    private void RunTerminalReturnFault(int workerPriority, bool wrongActor)
    {
        using var run = new CallbackReturnRun(workerPriority, twoRectangles: false, output);
        var caller = run.Caller();
        var sites = run.EmitStartAndLayer(caller);
        caller.StoreLong(run.Marker.Raw, 0xBAD0_0001);
        caller.Park();
        var hook = run.Callback();
        hook.StoreLong(run.Marker.Raw, 1);
        var invalidActor = run.Actor;
        Fixture.GuestStack? otherStack = null;
        Fixture.GuestProgram invalidProgram = hook;
        if (wrongActor)
        {
            otherStack = Fixture.GuestStack.Allocate(run.Bus);
            var otherCode = run.Borrowed.Allocate(run.Bus, 0x1000);
            invalidActor = run.Borrowed.Allocate(run.Bus, global::Amiga.Task.Size);
            var sdk = new LayersTestGuestMemory(run.Bus);
            var record = new global::Amiga.Task
            {
                Node = new Node { Type = (byte)NodeType.Task, Priority = 2 },
                IDNestCount = -1, TaskDisableNestCount = -1,
                StackLower = APTR.FromPointer(otherStack.Value.Lower),
                StackUpper = APTR.FromPointer(otherStack.Value.Upper),
                StackPointer = APTR.FromPointer(otherStack.Value.Upper),
            };
            ExecTaskCodec.Write(ref sdk, invalidActor, record);
            hook.MoveAddress(1, invalidActor.Raw);
            hook.MoveAddress(2, otherCode.Raw);
            hook.MoveAddress(3, 0);
            hook.Call(Lvo(APTR.FromPointer(AmigaKickstartHost.ExecLibraryBase), ExecLvo.AddTask));
            invalidProgram = new Fixture.GuestProgram(run.Bus, otherCode.Raw);
        }
        EmitReturnSentinels(invalidProgram);
        var invalidCall = invalidProgram.Address;
        var invalidReturn = invalidProgram.Call(LayersHostServices.HookContinuationAddress);
        invalidProgram.StoreLong(run.Marker.Raw, 0xBAD0_0002);
        invalidProgram.Park();
        var genuineRts = hook.Return();
        Assert.False(run.Bus.HasHostGateway(run.HookCode.Raw));

        run.Trace.Until(run.Actor, sites.StartReturn);
        var beforeLayer = run.Trace.Until(run.Actor, sites.Call);
        var entered = run.Trace.Until(run.Actor, run.HookCode.Raw);
        var token = AssertActiveReturn(run, beforeLayer, entered);
        var originalIdentity = run.Boot.CopperStartLayersPendingCallbackIdentityForTest;
        var originalNonce = run.Boot.CopperStartLayersPendingCallbackNonceForTest;
        var beforeCall = run.Trace.Until(invalidActor, invalidCall);
        var beforeFault = run.Trace.Until(invalidActor, LayersHostServices.HookContinuationAddress);
        Assert.Equal((ushort)0x4EB9, beforeFault.Opcode);
        Assert.Equal(invalidCall, beforeFault.PreviousPc);
        Assert.Equal(beforeCall.Sp - 4, beforeFault.Sp);
        Assert.Equal(invalidReturn, run.Bus.ReadLong(beforeFault.Sp));
        Assert.True(run.Boot.CopperStartLayersHasPendingCallbackForTest);
        Assert.Equal(LayersCallbackReturnFault.None, run.Boot.CopperStartLayersLastCallbackReturnFaultForTest);

        var observation = new ActorObservationMemory(run.Bus);
        var watched = CaptureReturnFaultStorage(run, token.Continuation, invalidActor, otherStack);
        var afterFault = run.Trace.FaultNext();
        Assert.Equal(wrongActor ? LayersCallbackReturnFault.WrongActor : LayersCallbackReturnFault.InvalidStack,
            run.Boot.CopperStartLayersLastCallbackReturnFaultForTest);
        AssertRejectedReturnState(beforeFault, afterFault);
        foreach (var span in watched) Assert.Equal(span.Bytes, ReadBytes(run.Bus, span.Address, (uint)span.Bytes.Length));
        Assert.True(run.Boot.CopperStartLayersHasPendingCallbackForTest);
        Assert.True(PortableLayers.LayersCallbackContextCore.SameIdentity(originalIdentity,
            run.Boot.CopperStartLayersPendingCallbackIdentityForTest));
        Assert.Equal(originalNonce, run.Boot.CopperStartLayersPendingCallbackNonceForTest);
        Assert.Equal(token.Continuation, PortableLayers.LayersPrivateRootCore.ActiveContinuation(ref observation, run.Root));
        if (wrongActor)
        {
            Assert.False(PortableLayers.LayersCallbackContextCore.TryRead(ref observation, run.Authority,
                run.Actor, token.ContinuationToken, out var unavailable));
            Assert.Equal(default(PortableLayers.LayersCallbackSnapshot), unavailable);
            AssertRestrictions(ref observation, run.Authority, invalidActor, PortableLayers.LayersActorRestrictions.None);
            Assert.Equal(TaskState.Ready, Fixture.ReadTask(run.Bus, run.Actor).State);
            Assert.Equal(TaskState.Running, Fixture.ReadTask(run.Bus, invalidActor).State);
        }
        else
        {
            Assert.True(PortableLayers.LayersCallbackContextCore.TryRead(ref observation, run.Authority,
                run.Actor, token.ContinuationToken, out var retained));
            Assert.True(PortableLayers.LayersCallbackContextCore.SameIdentity(originalIdentity, retained.Identity));
            Assert.Equal(PortableLayers.LayersCallbackPhase.AwaitingReturn, retained.Phase);
            AssertRestrictions(ref observation, run.Authority, run.Actor,
                PortableLayers.LayersActorRestrictions.MemberLayerLock | PortableLayers.LayersActorRestrictions.CallbackContinuation);
        }
        Assert.Equal(1u, run.Bus.ReadLong(run.Marker.Raw));
        Assert.DoesNotContain(run.Trace.Samples, sample => sample.Task == run.Actor && sample.Pc == sites.Return);
        Assert.DoesNotContain(run.Trace.Samples, sample => sample.Task == invalidActor && sample.Pc == invalidReturn);
        // AddTask can retire with PC already pointing at this RTS before the
        // next outer boundary schedules the higher-priority actor. That is not
        // execution of the RTS: require the actual previous instruction here.
        Assert.DoesNotContain(run.Trace.Samples,
            sample => sample.PreviousPc == genuineRts && sample.Opcode == 0x4E75);
        Assert.Equal(PortableIntuition.IntuitionTaskPhase.Running, run.Host.Snapshot().Phase);
        Assert.True(run.Host.Snapshot().Live);
        Assert.Equal((ushort)0, run.Host.Snapshot().CommandsInUse);
        Assert.Equal((ushort)0, run.Host.Snapshot().CallbacksInUse);
        run.User.AssertIntact(run.Bus);
        run.Supervisor.AssertIntact(run.Bus);
        otherStack?.AssertIntact(run.Bus);
        // Terminal retained-fault evidence only. Do not clear Halted, substitute
        // Exec.ThisTask, manufacture a valid return, or report normal cleanup.
        run.Trace.Dump();
    }

    private static LayersHostCallbackToken AssertActiveReturn(CallbackReturnRun run, CpuSample beforeLayer, CpuSample entered)
    {
        Assert.Equal(run.Actor, entered.Task);
        Assert.Equal(beforeLayer.Sp - 8 - LayersHostCallbackToken.Size, entered.Sp);
        Assert.Equal(LayersHostServices.HookContinuationAddress, run.Bus.ReadLong(entered.Sp));
        var sdk = new LayersTestGuestMemory(run.Bus);
        var token = LayersHostCallbackTokenCodec.Read(ref sdk, APTR.FromPointer(entered.Sp + 4));
        Assert.Equal(LayersHostCallbackToken.ExpectedMagic, token.Magic);
        Assert.Equal(LayersHostCallbackToken.CurrentVersion, token.Version);
        Assert.Equal(LayersHostCallbackToken.Size, token.Bytes);
        Assert.Equal(run.Library, token.LibraryBase);
        Assert.Equal(run.Root, token.Root);
        Assert.Equal(run.Actor, token.Actor);
        Assert.NotEqual(0u, token.Nonce);
        Assert.NotEqual(0u, token.ContinuationToken);
        Assert.True(run.Boot.CopperStartLayersHasPendingCallbackForTest);
        Assert.Equal(token.Nonce, run.Boot.CopperStartLayersPendingCallbackNonceForTest);
        var observation = new ActorObservationMemory(run.Bus);
        Assert.True(PortableLayers.LayersCallbackContextCore.TryRead(ref observation, run.Authority,
            run.Actor, token.ContinuationToken, out var snapshot));
        Assert.Equal(PortableLayers.LayersCallbackPhase.AwaitingReturn, snapshot.Phase);
        Assert.Equal(token.Continuation, snapshot.Identity.Continuation);
        Assert.Equal(token.RecordGeneration, snapshot.Identity.RecordGeneration);
        Assert.True(PortableLayers.LayersCallbackContextCore.SameIdentity(snapshot.Identity,
            run.Boot.CopperStartLayersPendingCallbackIdentityForTest));
        AssertRestrictions(ref observation, run.Authority, run.Actor,
            PortableLayers.LayersActorRestrictions.MemberLayerLock | PortableLayers.LayersActorRestrictions.CallbackContinuation);
        return token;
    }

    private static void EmitReturnSentinels(Fixture.GuestProgram program)
    {
        for (var register = 0; register < 8; register++) program.MoveData(register, 0xC700_0000u + (uint)register);
        for (var register = 0; register < 7; register++) program.MoveAddress(register, 0xA700_0000u + (uint)register);
        program.SetConditionCodes(M68kCpuState.Extend | M68kCpuState.Negative | M68kCpuState.Overflow);
    }

    private static void AssertRegistersUnchanged(CpuSample before, CpuSample after)
    {
        Assert.Equal(before.Task, after.Task);
        Assert.Equal(before.Data, after.Data);
        Assert.Equal(before.Address, after.Address);
        Assert.Equal(before.Sr, after.Sr);
        Assert.Equal(before.Sp, after.Sp);
        Assert.Equal(before.Usp, after.Usp);
        Assert.Equal(before.Ssp, after.Ssp);
    }

    private static void AssertRejectedReturnState(CpuSample before, CpuSample after,
        uint gateway = LayersHostServices.HookContinuationAddress)
    {
        AssertRegistersUnchanged(before, after);
        Assert.Equal(before.TaskState, after.TaskState);
        Assert.Equal(before.Signals, after.Signals);
        Assert.Equal(gateway + 6, after.Pc);
        Assert.Equal(gateway, after.PreviousPc);
        Assert.Equal((ushort)0xFF00, after.Opcode);
    }

    private static uint EmitPacketCopy(CallbackReturnRun run, uint code, APTR storage, bool toGuestStorage)
    {
        Assert.Equal(0, LayersHostCallbackToken.Size % 4);
        for (var offset = 0u; offset < LayersHostCallbackToken.Size; offset += 4)
        {
            Assert.InRange(code, run.HookCode.Raw, run.HookCode.Raw + 0x1000 - 10);
            if (toGuestStorage)
            {
                run.Bus.WriteWord(code, 0x202F, 0); // MOVE.L d16(A7),D0.
                run.Bus.WriteWord(code + 2, checked((ushort)(offset + 4)), 0);
                run.Bus.WriteWord(code + 4, 0x23C0, 0); // MOVE.L D0,absolute long.
                run.Bus.WriteLong(code + 6, storage.Raw + offset, 0);
            }
            else
            {
                run.Bus.WriteWord(code, 0x2039, 0); // MOVE.L absolute long,D0.
                run.Bus.WriteLong(code + 2, storage.Raw + offset, 0);
                run.Bus.WriteWord(code + 6, 0x2F40, 0); // MOVE.L D0,d16(A7).
                run.Bus.WriteWord(code + 8, checked((ushort)(offset + 4)), 0);
            }
            code += 10;
        }
        return code;
    }

    private static WatchedBytes[] CaptureReturnFaultStorage(CallbackReturnRun run, APTR continuation,
        APTR invalidActor, Fixture.GuestStack? otherStack)
    {
        var observation = new ActorObservationMemory(run.Bus);
        Assert.True(PortableLayers.LayersLayerCore.TryGetDescriptor(ref observation, run.Root, run.Surface.Layer, out var layerDescriptor));
        Assert.True(PortableLayers.LayersLayerInfoCore.TryGetDescriptor(ref observation, run.Root, run.Surface.LayerInfo, out var infoDescriptor));
        Assert.True(PortableIntuition.IntuitionExecutionRootCore.TryMeasure(Fixture.CreateConfiguration(), out var rootBytes));
        var result = new List<WatchedBytes>
        {
            Watch(run.Root, PortableLayers.LayersPrivateRootCore.Size),
            Watch(run.Surface.Layer, global::Amiga.Layer.Size),
            Watch(layerDescriptor, PortableLayers.LayersLayerCore.DescriptorSize),
            Watch(run.Surface.LayerInfo, LayerInfo.Size),
            Watch(infoDescriptor, PortableLayers.LayersLayerInfoCore.DescriptorSize),
            Watch(run.Host.Root.Address, rootBytes),
            Watch(run.Actor, global::Amiga.Task.Size),
            Watch(run.Host.Worker, global::Amiga.Task.Size),
            Watch(APTR.FromPointer(run.User.Storage), run.User.Bytes),
            Watch(APTR.FromPointer(run.Supervisor.Storage), run.Supervisor.Bytes),
        };
        if (continuation.IsNotNull)
        {
            var continuationBytes = PortableLayers.LayersPrivateRecordCore.Size(ref observation, continuation);
            Assert.InRange(continuationBytes, PortableLayers.LayersPrivateRecordCore.HeaderSize,
                PortableLayers.LayersPrivateRecordCore.MaximumPrivateRecordSize);
            result.Add(Watch(continuation, continuationBytes));
        }
        if (invalidActor != run.Actor) result.Add(Watch(invalidActor, global::Amiga.Task.Size));
        if (otherStack is { } stack) result.Add(Watch(APTR.FromPointer(stack.Storage), stack.Bytes));
        return result.ToArray();

        WatchedBytes Watch(APTR address, uint bytes) => new(address, ReadBytes(run.Bus, address, bytes));
    }

    private readonly record struct WatchedBytes(APTR Address, byte[] Bytes);

    private sealed class CallbackReturnRun : IDisposable
    {
        internal Machine Machine { get; }
        internal AmigaBootController Boot { get; }
        internal AmigaBus Bus => Machine.Bus;
        internal APTR Actor { get; }
        internal APTR Library { get; }
        internal APTR Root { get; }
        internal PortableLayers.LayersActorAuthority Authority { get; }
        internal Fixture.BorrowAuthority Borrowed { get; } = new();
        internal APTR CallerCode { get; }
        internal APTR HookCode { get; }
        internal APTR Hook { get; }
        internal APTR Marker { get; }
        internal Fixture.GuestStack User { get; }
        internal Fixture.GuestStack Supervisor { get; }
        internal Surface Surface { get; }
        internal APTR Occluder { get; }
        internal IntuitionExecutionHost Host { get; }
        internal OuterTrace Trace { get; }
        internal ushort InitialOpenCount { get; }
        internal APTR InitialLayerInfos { get; }
        internal Signals InitialSignals { get; }
        internal uint AvailableBeforeOwnedResources { get; }

        internal CallbackReturnRun(int workerPriority, bool twoRectangles, ITestOutputHelper output)
        {
            Machine = Fixture.CreateMachine();
            Boot = new AmigaBootController(Machine);
            Boot.StartBootFromDisk(Fixture.CreateBootableDisk());
            Actor = Fixture.CurrentTask(Bus);
            Library = APTR.FromPointer(Boot.CopperStartLayersLibraryBase);
            Root = APTR.FromPointer(Boot.CopperStartLayersRootAddress);
            Assert.True(Boot.HasCopperStartLayers && Actor.IsNotNull && Library.IsNotNull && Root.IsNotNull);
            Authority = new() { Root = Root, LibraryBase = Library, ExecBase = APTR.FromPointer(AmigaKickstartHost.ExecLibraryBase) };
            var sdk = new LayersTestGuestMemory(Bus);
            var observation = new ActorObservationMemory(Bus);
            InitialOpenCount = ExecLibraryCodec.Read(ref sdk, Library).OpenCount;
            InitialLayerInfos = PortableLayers.LayersPrivateRootCore.FirstLayerInfo(ref observation, Root);
            CallerCode = Borrowed.Allocate(Bus, 0x1000);
            HookCode = Borrowed.Allocate(Bus, 0x1000);
            Hook = Borrowed.Hook(Bus, HookCode.Raw);
            Marker = Borrowed.Allocate(Bus, 8);
            User = Fixture.GuestStack.Allocate(Bus);
            Supervisor = Fixture.GuestStack.Allocate(Bus);
            Fixture.SetTaskStack(Bus, Actor, User);
            Fixture.StartRequester(Machine, CallerCode.Raw, User, Supervisor);
            InitialSignals = Signals.Read(Bus, Actor);
            AvailableBeforeOwnedResources = AvailableSetupMemory(Bus);
            Surface = CreateSetupLayer(Bus, Library);
            if (twoRectangles)
            {
                var create = new M68kCpuState
                {
                    A = { [0] = Surface.LayerInfo.Raw, [1] = Surface.BitMap.Raw },
                    D = { [0] = 6, [2] = 9, [3] = 15, [4] = (uint)LayerCreationFlags.Simple },
                };
                InvokeSetup(Bus, Lvo(Library, LayersLvo.CreateUpfrontLayer), create);
                Occluder = APTR.FromPointer(create.D[0]);
                Assert.True(Occluder.IsNotNull);
            }
            Assert.Equal(PortableIntuition.IntuitionStateResult.Success,
                Boot.TryCreateUnpublishedIntuitionExecution(Fixture.CreateConfiguration(), (sbyte)workerPriority,
                    Borrowed.AllowsSpan, Borrowed.AllowsEntry, out var created));
            Assert.NotNull(created);
            Host = created!;
            Trace = new OuterTrace(Machine, Boot, Host, output);
        }

        internal Fixture.GuestProgram Caller() => new(Bus, CallerCode.Raw);
        internal Fixture.GuestProgram Callback() => new(Bus, HookCode.Raw);
        internal (uint StartReturn, uint Call, uint Return) EmitStartAndLayer(Fixture.GuestProgram caller)
        {
            var started = caller.Call(Host.GetGateway(IntuitionExecutionGateway.StartWait));
            caller.MoveAddress(6, Library.Raw);
            caller.Call(Lvo(Library, -6));
            for (var register = 0; register < 8; register++) caller.MoveData(register, 0xC100_0000u + (uint)register);
            for (var register = 3; register < 6; register++) caller.MoveAddress(register, 0xA100_0000u + (uint)register);
            caller.MoveAddress(0, Hook.Raw);
            caller.MoveAddress(1, Surface.RastPort.Raw);
            caller.MoveAddress(2, 0);
            caller.SetConditionCodes(M68kCpuState.Extend | M68kCpuState.Zero | M68kCpuState.Carry);
            var call = caller.Address;
            return (started, call, caller.Call(Lvo(Library, LayersLvo.DoHookClipRects)));
        }
        public void Dispose() => Machine.Dispose();
    }

    private static RequestSite EmitRequest(Fixture.GuestProgram code, uint gateway,
        PortableIntuition.IntuitionCommandOperation operation, APTR hook, APTR target, APTR message)
    {
        code.MoveData(0, (uint)operation);
        code.MoveData(1, 8); code.MoveData(2, 8);
        for (var register = 3; register < 8; register++) code.MoveData(register, 0xC100_0000u + (uint)register);
        code.MoveAddress(0, hook.Raw); code.MoveAddress(1, message.Raw); code.MoveAddress(2, target.Raw);
        for (var register = 3; register < 7; register++) code.MoveAddress(register, 0xA100_0000u + (uint)register);
        code.SetConditionCodes(M68kCpuState.Extend | M68kCpuState.Zero | M68kCpuState.Carry);
        var call = code.Address;
        return new(call, code.Call(gateway));
    }

    private static void AssertRequestRestored(CpuSample before, CpuSample after, PortableIntuition.IntuitionStateResult status)
    {
        Assert.Equal(before.Task, after.Task);
        Assert.Equal((uint)status, after.Data[0]);
        Assert.Equal(before.Data.Skip(1), after.Data.Skip(1));
        Assert.Equal(before.Address, after.Address);
        Assert.Equal(before.Sr, after.Sr);
        Assert.Equal(before.Sp, after.Sp);
        Assert.Equal(before.Usp, after.Usp);
        Assert.Equal(before.Ssp, after.Ssp);
    }

    private static void AssertRestrictions(ref ActorObservationMemory memory,
        PortableLayers.LayersActorAuthority authority, APTR actor, PortableLayers.LayersActorRestrictions expected)
    {
        Assert.True(PortableLayers.LayersActorContextCore.TryRead(ref memory, authority, actor, out var value));
        Assert.Equal(actor, value.Actor);
        Assert.NotEqual(0u, value.RootGeneration);
        Assert.Equal(expected, value.Restrictions);
    }

    // Direct setup invocations only allocate initial fixtures or observe final
    // allocator balance. Every measured call, return, unlock and free is guest code.
    private static Surface CreateSetupLayer(AmigaBus bus, APTR library)
    {
        var createInfo = new M68kCpuState();
        InvokeSetup(bus, Lvo(library, LayersLvo.NewLayerInfo), createInfo);
        var layerInfo = APTR.FromPointer(createInfo.D[0]);
        var allocateBitmap = new M68kCpuState { D = { [0] = 16, [1] = 16, [2] = 1, [3] = (uint)BitMapFlags.Clear } };
        InvokeSetup(bus, Lvo(APTR.FromPointer(AmigaKickstartHost.GraphicsLibraryBase), (short)GraphicsLvo.AllocBitMap), allocateBitmap);
        var bitmap = APTR.FromPointer(allocateBitmap.D[0]);
        var createLayer = new M68kCpuState
        {
            A = { [0] = layerInfo.Raw, [1] = bitmap.Raw },
            D = { [2] = 15, [3] = 15, [4] = (uint)LayerCreationFlags.Simple },
        };
        InvokeSetup(bus, Lvo(library, LayersLvo.CreateUpfrontLayer), createLayer);
        var layer = APTR.FromPointer(createLayer.D[0]);
        var memory = new LayersTestGuestMemory(bus);
        Assert.True(layerInfo.IsNotNull && bitmap.IsNotNull && layer.IsNotNull);
        var rastPort = LayersLayerCodec.ReadRastPort(ref memory, layer);
        Assert.True(rastPort.IsNotNull && LayersLayerCodec.ReadClipRect(ref memory, layer).IsNotNull);
        return new(layerInfo, bitmap, layer, rastPort);
    }

    private static uint AvailableSetupMemory(AmigaBus bus)
    {
        var state = new M68kCpuState();
        InvokeSetup(bus, Lvo(APTR.FromPointer(AmigaKickstartHost.ExecLibraryBase), ExecLvo.AvailMem), state);
        return state.D[0];
    }

    private static void InvokeSetup(AmigaBus bus, uint address, M68kCpuState state)
    {
        Assert.Equal((ushort)0xFF00, bus.ReadWord(address));
        Assert.True(bus.TryInvokeHostGateway(address, bus.ReadLong(address + 2), state));
    }

    private static uint Lvo(APTR library, int lvo) => unchecked(library.Raw + (uint)lvo);
    private static byte[] ReadBytes(AmigaBus bus, APTR address, uint bytes)
        => Enumerable.Range(0, checked((int)bytes)).Select(offset => bus.ReadByte(address.Raw + (uint)offset)).ToArray();
    private readonly record struct RequestSite(uint Call, uint Return);
    private readonly record struct Surface(APTR LayerInfo, APTR BitMap, APTR Layer, APTR RastPort);
    private readonly record struct Signals(uint Allocated, uint Received, uint Wait)
    {
        internal static Signals Read(AmigaBus bus, APTR task)
        {
            var value = Fixture.ReadTask(bus, task);
            return new(value.SignalAllocated, value.SignalReceived, value.SignalWait);
        }
    }

    private sealed record CpuSample(APTR Task, TaskState TaskState, uint Pc, uint PreviousPc,
        ushort Opcode, ushort Sr, uint Sp, uint Usp, uint Ssp, long Cycles, long NativeCycles,
        Signals Signals, uint[] Data, uint[] Address)
    {
        internal static CpuSample Read(Machine machine)
        {
            var cpu = machine.Cpu.State;
            var task = Fixture.CurrentTask(machine.Bus);
            return new(task, Fixture.ReadTask(machine.Bus, task).State, cpu.ProgramCounter,
                cpu.LastInstructionProgramCounter, cpu.LastOpcode, cpu.StatusRegister,
                cpu.A[7], cpu.UserStackPointer, cpu.SupervisorStackPointer, cpu.Cycles, cpu.NativeCycles,
                CopperStartIntuitionLayersActorBootTests.Signals.Read(machine.Bus, task), cpu.D.ToArray(), cpu.A.ToArray());
        }
        public override string ToString() => $"task={Task.Raw:X8}/{TaskState} pc={Pc:X8} previous={PreviousPc:X8} " +
            $"op={Opcode:X4} sr={Sr:X4} sp={Sp:X8} usp={Usp:X8} ssp={Ssp:X8} D0={Data[0]:X8} wait={Signals.Wait:X8} cycles={Cycles}";
    }

    private sealed class OuterTrace(Machine machine, AmigaBootController boot, IntuitionExecutionHost host, ITestOutputHelper output)
    {
        private const int InstructionBudget = 40_000;
        private const long CycleBudget = 4_000_000;
        private readonly long _firstCycle = machine.Cpu.State.Cycles;
        private int _instructions;
        internal List<CpuSample> Samples { get; } = [CpuSample.Read(machine)];
        internal CpuSample Until(APTR actor, uint pc)
        {
            while (Samples[^1].Task != actor || Samples[^1].Pc != pc)
            {
                var previous = Samples[^1];
                if (_instructions >= InstructionBudget || previous.Cycles - _firstCycle >= CycleBudget)
                { Dump(); Assert.Fail("Real guest program did not reach its boundary within the fixed budget."); }
                var result = boot.ContinueCopperStartRuntimeUntilCycle(_firstCycle + CycleBudget, maxInstructions: 1);
                _instructions += result.InstructionsExecuted;
                var next = CpuSample.Read(machine);
                Samples.Add(next);
                if (machine.Cpu.State.Halted || result.InstructionsExecuted != 1)
                {
                    Dump();
                    foreach (var diagnostic in result.Diagnostics) output.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
                    Assert.False(machine.Cpu.State.Halted);
                    Assert.Equal(1, result.InstructionsExecuted);
                }
                Assert.Equal(PortableIntuition.IntuitionStateResult.Success, host.LastFault);
                Assert.True(next.Cycles > previous.Cycles, next.ToString());
                Assert.True(next.NativeCycles >= previous.NativeCycles, next.ToString());
            }
            return Samples[^1];
        }
        internal CpuSample FaultNext(uint gateway = LayersHostServices.HookContinuationAddress)
        {
            var previous = Samples[^1];
            Assert.Equal(gateway, previous.Pc);
            Assert.False(machine.Cpu.State.Halted);
            Assert.True(_instructions < InstructionBudget && previous.Cycles - _firstCycle < CycleBudget);
            var result = boot.ContinueCopperStartRuntimeUntilCycle(_firstCycle + CycleBudget, maxInstructions: 1);
            _instructions += result.InstructionsExecuted;
            var next = CpuSample.Read(machine);
            Samples.Add(next);
            foreach (var diagnostic in result.Diagnostics) output.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
            Assert.Equal(1, result.InstructionsExecuted);
            Assert.True(machine.Cpu.State.Halted);
            Assert.False(machine.Cpu.State.Stopped);
            Assert.True(next.Cycles > previous.Cycles);
            Assert.True(next.NativeCycles >= previous.NativeCycles);
            Assert.Equal(PortableIntuition.IntuitionStateResult.Success, host.LastFault);
            return next;
        }
        internal void Dump()
        {
            output.WriteLine($"Layers/Intuition outer instructions={_instructions}; cycles={Samples[^1].Cycles - _firstCycle}; samples={Samples.Count}");
            foreach (var sample in Samples.TakeLast(24)) output.WriteLine(sample.ToString());
        }
    }

    /// <summary>Read-only SDK bus adapter. The current actor always comes from real Exec.ThisTask.</summary>
    private readonly struct ActorObservationMemory(AmigaBus bus) : PortableLayers.ILayersMemoryPlatform, PortableLayers.ILayersResourcePlatform
    {
        public bool IsMapped(APTR address, uint bytes) => bytes <= int.MaxValue && address.Raw <= uint.MaxValue - bytes &&
            bus.IsMappedMemoryRange(address.Raw, (int)bytes);
        public byte ReadUInt8(APTR address, int offset = 0) => bus.ReadByte(unchecked(address.Raw + (uint)offset));
        public ushort ReadUInt16(APTR address, int offset = 0) => bus.ReadWord(unchecked(address.Raw + (uint)offset));
        public uint ReadUInt32(APTR address, int offset = 0) => bus.ReadLong(unchecked(address.Raw + (uint)offset));
        public APTR GetCurrentTask(APTR execBase)
        {
            var memory = new LayersTestGuestMemory(bus);
            return ExecBaseCodec.ReadThisTask(ref memory, execBase);
        }
        public void WriteUInt8(APTR address, int offset, byte value) => Forbidden();
        public void WriteUInt16(APTR address, int offset, ushort value) => Forbidden();
        public void WriteUInt32(APTR address, int offset, uint value) => Forbidden();
        public void Clear(APTR address, uint bytes) => Forbidden();
        public void Copy(APTR source, APTR destination, uint bytes) => Forbidden();
        public APTR Allocate(uint bytes, global::Amiga.Exec.MemoryFlags flags) { Forbidden(); return APTR.Null; }
        public void Free(APTR address, uint bytes) => Forbidden();
        public APTR AllocateOpaque(uint bytes, global::Amiga.Exec.MemoryFlags flags) { Forbidden(); return APTR.Null; }
        public bool IsOpaqueAllocation(APTR address, uint bytes) { Forbidden(); return false; }
        public bool FreeOpaque(APTR address) { Forbidden(); return false; }
        public bool ParkCurrentTask(APTR task, APTR waitObject, uint token) { Forbidden(); return false; }
        public void WakeTask(APTR task, uint token) => Forbidden();
        private static void Forbidden() => Assert.Fail("Actor observation must not write memory, allocate, park, wake, or retire resources.");
    }
}
