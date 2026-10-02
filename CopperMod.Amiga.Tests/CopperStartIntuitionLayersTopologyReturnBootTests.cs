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
/// Actual guest Hook/RTS and LockLayerInfo/UnlockLayerInfo instructions force a
/// topology return to park before its acceptance receipt. Only the ordinary
/// outer scheduler changes actors. This is the synthetic-Exec host boundary,
/// not native Exec/ROM frames, interrupt dispatch, or a public Intuition ABI.
/// </summary>
public sealed class CopperStartIntuitionLayersTopologyReturnBootTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void RealTopologyHookReturnWaitsForAnotherTaskThenResumesItsRetainedIssueExactlyOnce(int workerPriority)
    {
        using var machine = Fixture.CreateMachine();
        var boot = new AmigaBootController(machine);
        boot.StartBootFromDisk(Fixture.CreateBootableDisk());
        var bus = machine.Bus;
        var actor = Fixture.CurrentTask(bus);
        var library = APTR.FromPointer(boot.CopperStartLayersLibraryBase);
        var root = APTR.FromPointer(boot.CopperStartLayersRootAddress);
        Assert.True(boot.HasCopperStartLayers && actor.IsNotNull && library.IsNotNull && root.IsNotNull);
        Assert.Equal((sbyte)0, Fixture.ReadTask(bus, actor).Node.Priority);
        var authority = new PortableLayers.LayersActorAuthority
        {
            Root = root, LibraryBase = library,
            ExecBase = APTR.FromPointer(AmigaKickstartHost.ExecLibraryBase),
        };
        var sdk = new LayersTestGuestMemory(bus);
        var observation = new ObservationMemory(bus);
        var initialOpenCount = ExecLibraryCodec.Read(ref sdk, library).OpenCount;
        var initialLayerInfos = PortableLayers.LayersPrivateRootCore.FirstLayerInfo(ref observation, root);
        var initialOpaqueCount = boot.CopperStartLayersOpaqueAllocationCountForTest;
        var borrowed = new Fixture.BorrowAuthority();
        var callerCode = borrowed.Allocate(bus, 0x1000);
        var hookCode = borrowed.Allocate(bus, 0x1000);
        var otherCode = borrowed.Allocate(bus, 0x1000);
        var hook = borrowed.Hook(bus, hookCode.Raw);
        var markers = borrowed.Allocate(bus, 8);
        var user = Fixture.GuestStack.Allocate(bus);
        var supervisor = Fixture.GuestStack.Allocate(bus);
        var otherStack = Fixture.GuestStack.Allocate(bus);
        var other = borrowed.Allocate(bus, global::Amiga.Task.Size);
        ExecTaskCodec.Write(ref sdk, other, new global::Amiga.Task
        {
            Node = new Node { Type = (byte)NodeType.Task, Priority = -1 },
            IDNestCount = -1, TaskDisableNestCount = -1,
            StackLower = APTR.FromPointer(otherStack.Lower),
            StackUpper = APTR.FromPointer(otherStack.Upper),
            StackPointer = APTR.FromPointer(otherStack.Upper),
        });
        Fixture.SetTaskStack(bus, actor, user);
        Fixture.StartRequester(machine, callerCode.Raw, user, supervisor);
        var initialSignals = Signals.Read(bus, actor);
        var availableBeforeOwners = AvailableSetupMemory(bus);
        var surface = CreateEmptySetupSurface(bus, library);
        Assert.Equal(PortableIntuition.IntuitionStateResult.Success,
            boot.TryCreateUnpublishedIntuitionExecution(Fixture.CreateConfiguration(), (sbyte)workerPriority,
                borrowed.AllowsSpan, borrowed.AllowsEntry, out var created));
        Assert.NotNull(created);
        var host = created!;
        var worker = host.Worker;
        var workerStack = Fixture.ReadTask(bus, worker);

        var caller = new Fixture.GuestProgram(bus, callerCode.Raw);
        var afterStarted = caller.Call(host.GetGateway(IntuitionExecutionGateway.StartWait));
        caller.MoveAddress(6, library.Raw);
        caller.Call(Lvo(library, -6));
        for (var register = 5; register < 8; register++)
            caller.MoveData(register, 0xC100_0000u + (uint)register);
        caller.MoveAddress(4, 0xA100_0004);
        caller.MoveAddress(5, 0xA100_0005);
        caller.MoveAddress(0, surface.LayerInfo.Raw);
        caller.MoveAddress(1, surface.BitMap.Raw);
        caller.MoveAddress(2, 0);
        caller.MoveAddress(3, hook.Raw);
        caller.MoveData(0, 0); caller.MoveData(1, 0);
        caller.MoveData(2, 15); caller.MoveData(3, 15);
        caller.MoveData(4, (uint)LayerCreationFlags.Simple);
        caller.SetConditionCodes(M68kCpuState.Extend | M68kCpuState.Zero | M68kCpuState.Carry);
        var createCall = caller.Address;
        var createGateway = Lvo(library, LayersLvo.CreateUpfrontHookLayer);
        var afterCreate = caller.Call(createGateway);
        // Keep the actual guest-returned Layer in A4 while giving the lower
        // priority helper a turn to finish its real Unlock LVO and RemTask.
        caller = AppendWord(bus, caller, callerCode, 0x2840); // MOVEA.L D0,A4
        EmitPriority(caller, actor, -4);
        caller.MoveData(7, 0xC1EA_0001);
        var afterCleanupResumed = caller.Address;
        EmitPriority(caller, actor, 0);
        caller = AppendWord(bus, caller, callerCode, 0x224C); // MOVEA.L A4,A1
        caller.MoveAddress(6, library.Raw);
        var afterDelete = caller.Call(Lvo(library, LayersLvo.DeleteLayer));
        caller.MoveAddress(0, surface.LayerInfo.Raw);
        var afterDispose = caller.Call(Lvo(library, LayersLvo.DisposeLayerInfo));
        caller.MoveAddress(6, AmigaKickstartHost.GraphicsLibraryBase);
        caller.MoveAddress(0, surface.BitMap.Raw);
        caller.Call(Lvo(APTR.FromPointer(AmigaKickstartHost.GraphicsLibraryBase), (short)GraphicsLvo.FreeBitMap));
        caller.MoveAddress(6, library.Raw);
        var closeGateway = Lvo(library, -12);
        var afterClose = caller.Call(closeGateway);
        var stopGateway = host.GetGateway(IntuitionExecutionGateway.Stop);
        caller.Call(stopGateway);
        caller.CallUntilNotBusy(host.GetGateway(IntuitionExecutionGateway.Reap));
        var park = caller.Park();

        var callback = new Fixture.GuestProgram(bus, hookCode.Raw);
        callback.StoreLong(markers.Raw, 1);
        callback.MoveAddress(6, authority.ExecBase.Raw);
        callback.MoveAddress(1, other.Raw);
        callback.MoveAddress(2, otherCode.Raw);
        callback.MoveAddress(3, 0);
        var addTaskGateway = Lvo(authority.ExecBase, ExecLvo.AddTask);
        var afterAddTask = callback.Call(addTaskGateway);
        EmitPriority(callback, actor, -2);
        callback.MoveData(7, 0xC411_0001);
        var afterCallerResumed = callback.Address;
        EmitPriority(callback, actor, 0);
        callback.MoveData(0, 0xA11C_0001); // An opaque Hook result, not a status.
        var hookRts = callback.Return();

        var helper = new Fixture.GuestProgram(bus, otherCode.Raw);
        helper.MoveAddress(6, library.Raw);
        helper.MoveAddress(0, surface.LayerInfo.Raw);
        var lockGateway = Lvo(library, LayersLvo.LockLayerInfo);
        var afterLock = helper.Call(lockGateway);
        helper.StoreLong(markers.Raw + 4, 1);
        var afterHelperYield = EmitPriority(helper, other, -3);
        helper.MoveAddress(6, library.Raw);
        helper.MoveAddress(0, surface.LayerInfo.Raw);
        var unlockCall = helper.Address;
        var unlockGateway = Lvo(library, LayersLvo.UnlockLayerInfo);
        var afterUnlock = helper.Call(unlockGateway);
        helper.StoreLong(markers.Raw + 4, 2);
        helper.MoveAddress(6, authority.ExecBase.Raw);
        helper.MoveAddress(1, 0);
        var removeCall = helper.Address;
        var removeGateway = Lvo(authority.ExecBase, ExecLvo.RemTask);
        var afterRemove = helper.Call(removeGateway);
        helper.Park(); // A retired task must never execute this instruction.
        Assert.False(bus.HasHostGateway(hookCode.Raw));
        Assert.False(bus.HasHostGateway(otherCode.Raw));

        var trace = new Trace(machine, boot, host, output);
        var started = trace.Until(actor, afterStarted);
        Assert.Equal((uint)PortableIntuition.IntuitionStateResult.Success, started.Data[0]);
        Assert.Equal(PortableIntuition.IntuitionTaskPhase.Running, host.Snapshot().Phase);
        var beforeCreate = trace.Until(actor, createCall);
        var entered = trace.Until(actor, hookCode.Raw);
        Assert.Equal(beforeCreate.Sp - 8u - LayersHostCallbackToken.Size, entered.Sp);
        Assert.Equal(LayersHostServices.HookContinuationAddress, bus.ReadLong(entered.Sp));
        Assert.Equal(hook.Raw, entered.Address[0]);
        Assert.True(LayersHookMessageCodec.TryRead(ref sdk, APTR.FromPointer(entered.Address[1]),
            out LayerBackfillMessage message));
        Assert.True(message.Layer.IsNotNull);
        Assert.Equal(LayersRectangleCodec.Create(0, 0, 15, 15), message.Bounds);
        Assert.Equal(message.Layer, LayersLayerInfoCodec.ReadTopLayer(ref sdk, surface.LayerInfo));
        Assert.Equal(LayersLayerCodec.ReadRastPort(ref sdk, message.Layer).Raw, entered.Address[2]);
        var tokenAddress = APTR.FromPointer(entered.Sp + 4u);
        var issued = LayersHostCallbackTokenCodec.Read(ref sdk, tokenAddress);
        Assert.Equal(LayersHostCallbackToken.ExpectedMagic, issued.Magic);
        Assert.Equal(LayersHostCallbackToken.CurrentVersion, issued.Version);
        Assert.Equal(LayersHostCallbackToken.Size, issued.Bytes);
        Assert.Equal(actor, issued.Actor);
        Assert.Equal(library, issued.LibraryBase);
        Assert.Equal(root, issued.Root);
        Assert.NotEqual(0u, issued.Nonce);
        Assert.Equal(afterCreate, bus.ReadLong(tokenAddress.Raw + LayersHostCallbackToken.Size));
        var identity = boot.CopperStartLayersPendingCallbackIdentityForTest;
        Assert.Equal(issued.Nonce, boot.CopperStartLayersPendingCallbackNonceForTest);
        Assert.True(PortableLayers.LayersCallbackContextCore.TryRead(ref observation, authority,
            actor, issued.ContinuationToken, out var pending));
        Assert.Equal(PortableLayers.LayersCallbackPhase.AwaitingReturn, pending.Phase);
        Assert.True(PortableLayers.LayersCallbackContextCore.SameIdentity(identity, pending.Identity));
        Assert.Equal(issued.Continuation, identity.Continuation);
        Assert.Equal(issued.RecordGeneration, identity.RecordGeneration);
        Assert.Equal(issued.ContinuationToken, identity.Token);
        output.WriteLine($"Topology Hook: workerPriority={workerPriority}; actor={actor.Raw:X8}; helper={other.Raw:X8}; " +
            $"continuation={issued.Continuation.Raw:X8}; generation={issued.RecordGeneration:X8}; " +
            $"token={issued.ContinuationToken:X8}; nonce={issued.Nonce:X8}; packet={tokenAddress.Raw:X8}.");
        AssertRestrictions(ref observation, authority, actor,
            PortableLayers.LayersActorRestrictions.MemberLayerLock | PortableLayers.LayersActorRestrictions.CallbackContinuation);
        var topologySemaphore = LayersLayerInfoCodec.LockAddress(surface.LayerInfo);
        AssertSemaphore(ref sdk, topologySemaphore, APTR.Null, -1, -1);
        Assert.Equal(0, boot.CopperStartLayersPendingWaitCountForTest);

        var added = trace.Until(actor, afterAddTask);
        Assert.Equal(other.Raw, added.Data[0]);
        Assert.Equal(TaskState.Ready, Fixture.ReadTask(bus, other).State);
        trace.Until(other, afterLock);
        Assert.Equal((sbyte)-2, Fixture.ReadTask(bus, actor).Node.Priority);
        Assert.Equal(TaskState.Ready, Fixture.ReadTask(bus, actor).State);
        AssertSemaphore(ref sdk, topologySemaphore, other, 0, -1);
        AssertRestrictions(ref observation, authority, other, PortableLayers.LayersActorRestrictions.LayerInfoLock);
        // Observe an instruction executed after the context was restored, not
        // the saved next PC that was already visible before priority dispatch.
        trace.Until(actor, afterCallerResumed);
        Assert.Equal((sbyte)-2, Fixture.ReadTask(bus, actor).Node.Priority);
        Assert.Equal(TaskState.Ready, Fixture.ReadTask(bus, other).State);
        Assert.Equal((sbyte)-3, Fixture.ReadTask(bus, other).Node.Priority);
        Assert.Equal(1u, bus.ReadLong(markers.Raw + 4));
        Assert.Equal(0, boot.CopperStartLayersPendingWaitCountForTest);
        var returning = trace.Until(actor, hookRts);
        Assert.Equal((sbyte)0, Fixture.ReadTask(bus, actor).Node.Priority);
        Assert.Equal(entered.Sp, returning.Sp);
        Assert.Equal(0xA11C_0001u, returning.Data[0]);
        var returned = trace.Until(actor, LayersHostServices.HookContinuationAddress);
        Assert.Equal(hookRts, returned.PreviousPc);
        Assert.Equal((ushort)0x4E75, returned.Opcode);
        Assert.Equal(tokenAddress.Raw, returned.Sp);
        Assert.Equal(issued, LayersHostCallbackTokenCodec.Read(ref sdk, tokenAddress));
        var continuationBytes = PortableLayers.LayersPrivateRecordCore.Size(ref observation, issued.Continuation);
        Assert.InRange(continuationBytes, PortableLayers.LayersPrivateRecordCore.HeaderSize,
            PortableLayers.LayersPrivateRecordCore.MaximumPrivateRecordSize);
        var continuationBeforeWait = ReadBytes(bus, issued.Continuation, continuationBytes);
        var tokenBeforeWait = ReadBytes(bus, tokenAddress, LayersHostCallbackToken.Size);

        // No test action supplies the grant. The Hook-return FF00 has to enqueue
        // and park the caller before the lower-priority helper can reach here.
        var releasing = trace.Until(other, unlockCall);
        Assert.Equal(TaskState.Waiting, Fixture.ReadTask(bus, actor).State);
        Assert.Equal(TaskState.Running, releasing.TaskState);
        Assert.Equal((sbyte)-3, Fixture.ReadTask(bus, other).Node.Priority);
        Assert.Equal(1, boot.CopperStartLayersPendingWaitCountForTest);
        AssertSemaphore(ref sdk, topologySemaphore, other, 0, 0); // One real private Layers waiter, projected by the SDK codec.
        Assert.True(boot.CopperStartLayersHasPendingCallbackForTest);
        Assert.True(PortableLayers.LayersCallbackContextCore.SameIdentity(identity,
            boot.CopperStartLayersPendingCallbackIdentityForTest));
        Assert.Equal(issued.Nonce, boot.CopperStartLayersPendingCallbackNonceForTest);
        Assert.Equal(tokenBeforeWait, ReadBytes(bus, tokenAddress, LayersHostCallbackToken.Size));
        Assert.Equal(continuationBeforeWait, ReadBytes(bus, issued.Continuation, continuationBytes));
        Assert.Equal(afterCreate, bus.ReadLong(tokenAddress.Raw + LayersHostCallbackToken.Size));
        Assert.Equal(1u, bus.ReadLong(markers.Raw));
        Assert.Equal(1u, bus.ReadLong(markers.Raw + 4));
        Assert.DoesNotContain(trace.Samples, sample => sample.Task == actor && sample.Pc == afterCreate);
        Assert.Contains(trace.Samples, sample => sample.Task == actor && sample.TaskState == TaskState.Waiting &&
            sample.Pc == LayersHostServices.BlockContinuationAddress &&
            sample.PreviousPc == LayersHostServices.HookContinuationAddress && sample.Opcode == 0xFF00 &&
            sample.Sp == tokenAddress.Raw);
        output.WriteLine($"Before actual Unlock: caller={Fixture.ReadTask(bus, actor).State}; helper={releasing}; " +
            $"pendingWaits={boot.CopperStartLayersPendingWaitCountForTest}; issued packet and {continuationBytes}-byte continuation unchanged.");

        // The actual Unlock publishes the caller's grant; the helper can be
        // preempted with PC at its next instruction, before that instruction ran.
        trace.Until(other, afterUnlock);
        Assert.Equal(TaskState.Ready, Fixture.ReadTask(bus, actor).State);
        AssertSemaphore(ref sdk, topologySemaphore, actor, 0, -1);
        Assert.Equal(1, boot.CopperStartLayersPendingWaitCountForTest);
        Assert.Equal(issued, LayersHostCallbackTokenCodec.Read(ref sdk, tokenAddress));
        Assert.Equal(continuationBeforeWait, ReadBytes(bus, issued.Continuation, continuationBytes));
        Assert.Equal(issued.Nonce, boot.CopperStartLayersPendingCallbackNonceForTest);
        Assert.True(PortableLayers.LayersCallbackContextCore.SameIdentity(identity,
            boot.CopperStartLayersPendingCallbackIdentityForTest));
        output.WriteLine($"After actual Unlock: caller={Fixture.ReadTask(bus, actor).State}; SDK owner=" +
            $"{LayersSignalSemaphoreCodec.ReadOwner(ref sdk, topologySemaphore).Raw:X8}; return not yet consumed.");
        var resumed = trace.Until(actor, afterCreate);
        Assert.Equal(LayersHostServices.BlockContinuationAddress, resumed.PreviousPc);
        Assert.Equal((ushort)0xFF00, resumed.Opcode);
        Assert.Equal(message.Layer.Raw, resumed.Data[0]);
        AssertReturnedFrame(beforeCreate, resumed);
        Assert.False(boot.CopperStartLayersHasPendingCallbackForTest);
        Assert.Equal(0, boot.CopperStartLayersPendingWaitCountForTest);
        Assert.Equal(LayersCallbackReturnFault.None, boot.CopperStartLayersLastCallbackReturnFaultForTest);
        Assert.True(PortableLayers.LayersCallbackContextCore.TryReadRetired(ref observation, authority, actor, out var retired));
        Assert.True(retired);
        AssertRestrictions(ref observation, authority, actor, PortableLayers.LayersActorRestrictions.None);
        AssertSemaphore(ref sdk, topologySemaphore, APTR.Null, -1, -1);
        AssertSemaphore(ref sdk, LayersLayerCodec.LockAddress(message.Layer), APTR.Null, -1, -1);
        output.WriteLine($"Accepted blocked return: {resumed}; retired={retired}; pendingWaits={boot.CopperStartLayersPendingWaitCountForTest}.");

        // Let the helper really finish and remove itself; its allocated SDK
        // Task/stack belong to fixture setup and remain readable afterwards.
        trace.Until(other, removeCall);
        Assert.Equal(2u, bus.ReadLong(markers.Raw + 4));
        Assert.Equal((sbyte)-4, Fixture.ReadTask(bus, actor).Node.Priority);
        Assert.Equal(TaskState.Ready, Fixture.ReadTask(bus, actor).State);
        Assert.Equal(otherStack.Upper - 4u, machine.Cpu.State.A[7]);
        trace.Until(actor, afterCleanupResumed);
        Assert.Equal(TaskState.Removed, Fixture.ReadTask(bus, other).State);
        Assert.Equal(0u, Fixture.ReadTask(bus, other).SignalAllocated);
        trace.Until(actor, afterDelete);
        Assert.NotEqual(0u, machine.Cpu.State.D[0]);
        Assert.True(LayersLayerInfoCodec.ReadTopLayer(ref sdk, surface.LayerInfo).IsNull);
        trace.Until(actor, afterDispose);
        Assert.Equal(initialLayerInfos, PortableLayers.LayersPrivateRootCore.FirstLayerInfo(ref observation, root));
        trace.Until(actor, afterClose);
        Assert.Equal(initialOpenCount, ExecLibraryCodec.Read(ref sdk, library).OpenCount);
        var finished = trace.Until(actor, park);
        Assert.Equal((uint)PortableIntuition.IntuitionStateResult.Success, finished.Data[0]);
        Assert.Equal(PortableIntuition.IntuitionTaskPhase.Idle, host.Snapshot().Phase);
        Assert.Equal((ushort)0, host.Snapshot().CommandsInUse);
        Assert.Equal((ushort)0, host.Snapshot().CallbacksInUse);
        Assert.Equal((sbyte)0, Fixture.ReadTask(bus, actor).Node.Priority);
        Assert.Equal(initialSignals, Signals.Read(bus, actor));
        Assert.Equal(user.Upper, finished.Sp);
        Assert.Equal(user.Upper, finished.Usp);
        Assert.Equal(supervisor.Upper, finished.Ssp);
        Assert.Equal(1u, bus.ReadLong(markers.Raw));
        Assert.Equal(2u, bus.ReadLong(markers.Raw + 4));
        Assert.Equal(initialOpaqueCount, boot.CopperStartLayersOpaqueAllocationCountForTest);
        Assert.Equal(1, trace.Samples.Count(sample => sample.Task == actor && sample.Pc == hookCode.Raw));
        Assert.Equal(1, trace.Samples.Count(sample => sample.Task == actor && sample.Pc == afterCreate));
        AssertRetiredOnce(trace, actor, createGateway, 0xFF00);
        AssertRetiredOnce(trace, actor, hookRts, 0x4E75);
        AssertRetiredOnce(trace, actor, LayersHostServices.HookContinuationAddress, 0xFF00);
        AssertRetiredOnce(trace, actor, LayersHostServices.BlockContinuationAddress, 0xFF00);
        AssertRetiredOnce(trace, actor, addTaskGateway, 0xFF00);
        AssertRetiredOnce(trace, other, lockGateway, 0xFF00);
        AssertRetiredOnce(trace, other, unlockGateway, 0xFF00);
        AssertRetiredOnce(trace, other, removeGateway, 0xFF00);
        AssertRetiredOnce(trace, actor, closeGateway, 0xFF00);
        AssertRetiredOnce(trace, actor, stopGateway, 0xFF00);
        Assert.DoesNotContain(trace.Samples, sample => sample.Task == other && sample.PreviousPc == afterRemove && sample.Opcode == 0x60FE);
        Assert.Equal(3, trace.Samples.Select(sample => sample.Task).Distinct().Count());
        Assert.Contains(trace.Samples, sample => sample.Task == other && sample.Pc == afterHelperYield);
        Assert.All(trace.Samples.Where(sample => sample.Task == actor), sample =>
        {
            Assert.Equal(0, sample.Sr & M68kCpuState.Supervisor);
            Assert.Equal(sample.Sp, sample.Usp);
            Assert.Equal(supervisor.Upper, sample.Ssp);
            Assert.InRange(sample.Sp, user.Lower, user.Upper);
        });
        Assert.All(trace.Samples.Where(sample => sample.Task == other), sample =>
        {
            Assert.Equal(0, sample.Sr & M68kCpuState.Supervisor);
            Assert.Equal(sample.Sp, sample.Usp);
            Assert.InRange(sample.Sp, otherStack.Lower, otherStack.Upper);
        });
        Assert.All(trace.Samples.Where(sample => sample.Task == worker), sample =>
        {
            Assert.Equal(0, sample.Sr & M68kCpuState.Supervisor);
            Assert.Equal(sample.Sp, sample.Usp);
            Assert.InRange(sample.Sp, workerStack.StackLower.Raw, workerStack.StackUpper.Raw);
        });
        user.AssertIntact(bus);
        supervisor.AssertIntact(bus);
        otherStack.AssertIntact(bus);
        Assert.Equal(PortableIntuition.IntuitionStateResult.Success, host.DestroyUnpublished());
        Assert.False(host.Snapshot().Live);
        Assert.Equal(availableBeforeOwners, AvailableSetupMemory(bus));
        trace.Dump();
    }

    private static uint EmitPriority(Fixture.GuestProgram code, APTR actor, sbyte priority)
    {
        code.MoveAddress(6, AmigaKickstartHost.ExecLibraryBase);
        code.MoveAddress(1, actor.Raw);
        code.MoveData(0, unchecked((uint)(int)priority));
        return code.Call(Lvo(APTR.FromPointer(AmigaKickstartHost.ExecLibraryBase), ExecLvo.SetTaskPri));
    }

    private static Fixture.GuestProgram AppendWord(AmigaBus bus, Fixture.GuestProgram code, APTR allocation, ushort word)
    {
        Assert.InRange(code.Address, allocation.Raw, allocation.Raw + 0x1000u - 2u);
        bus.WriteWord(code.Address, word, 0);
        var next = code.Address + 2u;
        return new(bus, next, allocation.Raw + 0x1000u - next);
    }

    private static void AssertReturnedFrame(Sample before, Sample after)
    {
        Assert.Equal(before.Task, after.Task);
        Assert.Equal(TaskState.Running, after.TaskState);
        Assert.Equal(before.Data.Skip(1), after.Data.Skip(1));
        Assert.Equal(before.Address, after.Address);
        Assert.Equal(before.Sr, after.Sr);
        Assert.Equal(before.Sp, after.Sp);
        Assert.Equal(before.Usp, after.Usp);
        Assert.Equal(before.Ssp, after.Ssp);
    }

    private static void AssertRetiredOnce(Trace trace, APTR actor, uint pc, ushort opcode)
        => Assert.Single(trace.Samples.Where(sample => sample.Task == actor && sample.PreviousPc == pc && sample.Opcode == opcode));

    private static void AssertSemaphore(ref LayersTestGuestMemory memory, APTR address, APTR owner, short nest, short queued)
    {
        Assert.Equal(owner, LayersSignalSemaphoreCodec.ReadOwner(ref memory, address));
        Assert.Equal(nest, LayersSignalSemaphoreCodec.ReadNestCount(ref memory, address));
        Assert.Equal(queued, LayersSignalSemaphoreCodec.ReadQueueCount(ref memory, address));
    }

    private static void AssertRestrictions(ref ObservationMemory memory, PortableLayers.LayersActorAuthority authority,
        APTR actor, PortableLayers.LayersActorRestrictions expected)
    {
        Assert.True(PortableLayers.LayersActorContextCore.TryRead(ref memory, authority, actor, out var snapshot));
        Assert.Equal(actor, snapshot.Actor);
        Assert.Equal(expected, snapshot.Restrictions);
    }

    // Direct calls below only construct initial fixture resources or read the
    // final allocator balance. No measured lock, return, grant or cleanup uses them.
    private static (APTR LayerInfo, APTR BitMap) CreateEmptySetupSurface(AmigaBus bus, APTR library)
    {
        var info = new M68kCpuState();
        InvokeSetup(bus, Lvo(library, LayersLvo.NewLayerInfo), info);
        var bitmap = new M68kCpuState { D = { [0] = 16, [1] = 16, [2] = 1, [3] = (uint)BitMapFlags.Clear } };
        InvokeSetup(bus, Lvo(APTR.FromPointer(AmigaKickstartHost.GraphicsLibraryBase), (short)GraphicsLvo.AllocBitMap), bitmap);
        Assert.NotEqual(0u, info.D[0]);
        Assert.NotEqual(0u, bitmap.D[0]);
        return (APTR.FromPointer(info.D[0]), APTR.FromPointer(bitmap.D[0]));
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

    private readonly record struct Signals(uint Allocated, uint Received, uint Wait)
    {
        internal static Signals Read(AmigaBus bus, APTR task)
        {
            var value = Fixture.ReadTask(bus, task);
            return new(value.SignalAllocated, value.SignalReceived, value.SignalWait);
        }
    }

    private sealed record Sample(APTR Task, TaskState TaskState, uint Pc, uint PreviousPc, ushort Opcode,
        ushort Sr, uint Sp, uint Usp, uint Ssp, long Cycles, long NativeCycles, uint[] Data, uint[] Address)
    {
        internal static Sample Read(Machine machine)
        {
            var cpu = machine.Cpu.State;
            var task = Fixture.CurrentTask(machine.Bus);
            return new(task, Fixture.ReadTask(machine.Bus, task).State, cpu.ProgramCounter,
                cpu.LastInstructionProgramCounter, cpu.LastOpcode, cpu.StatusRegister, cpu.A[7],
                cpu.UserStackPointer, cpu.SupervisorStackPointer, cpu.Cycles, cpu.NativeCycles,
                cpu.D.ToArray(), cpu.A.ToArray());
        }
        public override string ToString() => $"task={Task.Raw:X8}/{TaskState} pc={Pc:X8} previous={PreviousPc:X8} " +
            $"op={Opcode:X4} sr={Sr:X4} sp={Sp:X8} usp={Usp:X8} ssp={Ssp:X8} D0={Data[0]:X8} cycles={Cycles}";
    }

    private sealed class Trace(Machine machine, AmigaBootController boot, IntuitionExecutionHost host, ITestOutputHelper output)
    {
        private const int InstructionBudget = 40_000;
        private const long CycleBudget = 4_000_000;
        private readonly long _firstCycle = machine.Cpu.State.Cycles;
        private int _instructions;
        internal List<Sample> Samples { get; } = [Sample.Read(machine)];
        internal Sample Until(APTR actor, uint pc)
        {
            while (Samples[^1].Task != actor || Samples[^1].Pc != pc)
            {
                var previous = Samples[^1];
                if (_instructions >= InstructionBudget || previous.Cycles - _firstCycle >= CycleBudget)
                { Dump(); Assert.Fail("Real topology-return program did not reach its boundary within the fixed budget."); }
                var result = boot.ContinueCopperStartRuntimeUntilCycle(_firstCycle + CycleBudget, maxInstructions: 1);
                _instructions += result.InstructionsExecuted;
                var next = Sample.Read(machine);
                Samples.Add(next);
                if (machine.Cpu.State.Halted || result.InstructionsExecuted != 1)
                {
                    Dump();
                    foreach (var diagnostic in result.Diagnostics) output.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
                    Assert.False(machine.Cpu.State.Halted);
                    Assert.Equal(1, result.InstructionsExecuted);
                }
                Assert.Equal(PortableIntuition.IntuitionStateResult.Success, host.LastFault);
                Assert.Equal(LayersCallbackReturnFault.None, boot.CopperStartLayersLastCallbackReturnFaultForTest);
                Assert.True(next.Cycles > previous.Cycles, next.ToString());
                Assert.True(next.NativeCycles >= previous.NativeCycles, next.ToString());
            }
            return Samples[^1];
        }
        internal void Dump()
        {
            output.WriteLine($"Topology return: outer instructions={_instructions}; cycles={Samples[^1].Cycles - _firstCycle}; samples={Samples.Count}");
            foreach (var sample in Samples.TakeLast(24)) output.WriteLine(sample.ToString());
        }
    }

    /// <summary>Observation only: current task comes from the real SDK ExecBase.</summary>
    private readonly struct ObservationMemory(AmigaBus bus) : PortableLayers.ILayersMemoryPlatform, PortableLayers.ILayersResourcePlatform
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
        private static void Forbidden() => Assert.Fail("A topology-return observer cannot write, allocate, grant, park, wake, or clean up.");
    }
}
