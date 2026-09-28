using Amiga;
using Copper68k;
using CopperMod.Amiga.Bus;
using PortableLayers = CopperStart.Layers;

namespace CopperMod.Amiga.CopperStart.Layers;

internal sealed partial class LayersHostServices
{
    private enum CallbackPhase : byte
    {
        Prepared,
        Armed,
        ReturnObserved,
        ReturnAccepted
    }

    private readonly record struct CallbackStack(
        uint Pointer, uint Lower, uint Upper, uint CallerReturn,
        uint User, uint Supervisor, uint Master, ushort Mode, bool M68020)
    {
        internal uint TokenPointer => Pointer - LayersHostCallbackToken.Size;
    }

    private readonly record struct PendingCallback(
        short Lvo,
        uint Token,
        PortableLayers.LayersRegisterFrame Frame,
        PortableLayers.LayersCallbackIdentity Identity,
        LayersHostCallbackToken IssuedToken,
        CallbackStack Stack,
        uint Entry,
        CallbackPhase Phase)
    {
        internal bool IsActive => Token != 0;
    }

    // A successor may be prepared by portable Resume before its acceptance
    // receipt has reached this boundary. Do not overwrite the retained return
    // or touch the guest stack until that receipt is available.
    private PendingCallback _nextCallback;
    private bool _dispatchingCallbackReturn;
    // Do not reset this on Reset/reinstall: old guest stacks may still exist.
    // Exhaustion rejects another Hook rather than reusing an issuance identity.
    private uint _callbackNonce;

    internal LayersCallbackReturnFault LastCallbackReturnFault { get; private set; }
    internal bool HasPendingCallbackForTest => _pendingCallback.IsActive;
    internal PortableLayers.LayersCallbackIdentity PendingCallbackIdentityForTest => _pendingCallback.Identity;
    internal uint PendingCallbackNonceForTest => _pendingCallback.IssuedToken.Nonce;

    private bool PrepareCallback(M68kCpuState state, APTR hook, APTR target,
        APTR message, uint token)
    {
        if (!ReferenceEquals(state, _dispatchState) || state.Halted || state.Stopped ||
            LastCallbackReturnFault != LayersCallbackReturnFault.None ||
            _callbackNonce == uint.MaxValue || token == 0 ||
            !CallbackSpan(hook.Raw, Hook.Size) ||
            !TryReadCallbackOwner(token, out var observed)) return false;
        var identity = observed.Identity;
        if (identity.Hook != hook || identity.Target != target || identity.Message != message)
            return false;
        var platform = CreatePlatform(state);
        var entry = LayersHookCodec.ReadEntry(ref platform, hook).Raw;
        if (!CallbackCode(entry) || entry == HookContinuationAddress ||
            entry == BlockContinuationAddress) return false;

        CallbackStack stack;
        if (_dispatchingCallbackReturn)
        {
            if (!_pendingCallback.IsActive || _nextCallback.IsActive ||
                (_pendingCallback.Phase != CallbackPhase.ReturnObserved &&
                    _pendingCallback.Phase != CallbackPhase.ReturnAccepted) ||
                identity.Actor != _pendingCallback.Identity.Actor) return false;
            stack = _pendingCallback.Stack;
            // A parked return still has its packet, not the vector return LONG,
            // at the active SP. Reuse the independently retained caller frame.
            if (!CallbackBanksMatch(state, stack, stack.TokenPointer) ||
                !CallbackStackStorage(identity, stack)) return false;
        }
        else
        {
            if (_pendingCallback.IsActive || !ReadCallbackStack(state, identity, out stack))
                return false;
        }
        var scratch = stack.TokenPointer - 4u;
        const uint scratchBytes = LayersHostCallbackToken.Size + 8u;
        if (CallbackOverlap(scratch, scratchBytes, hook.Raw, Hook.Size) ||
            CallbackOverlap(scratch, scratchBytes, entry, 2) ||
            CallbackOverlap(scratch, scratchBytes, stack.CallerReturn, 2) ||
            (identity.CallbackKind != PortableLayers.LayersCallbackKind.Transparency &&
                CallbackOverlap(scratch, scratchBytes, target.Raw, RastPort.Size)) ||
            (target.IsNotNull && CallbackOverlap(scratch, scratchBytes, target.Raw, 1)))
            return false;
        var packet = new LayersHostCallbackToken
        {
            Magic = LayersHostCallbackToken.ExpectedMagic,
            Version = LayersHostCallbackToken.CurrentVersion,
            Bytes = LayersHostCallbackToken.Size,
            Nonce = ++_callbackNonce,
            LibraryBase = identity.Authority.LibraryBase,
            Root = identity.Authority.Root,
            Actor = identity.Actor,
            Continuation = identity.Continuation,
            RecordGeneration = identity.RecordGeneration,
            ContinuationToken = token
        };
        var pending = new PendingCallback(_dispatchLvo, token, _dispatchOriginalFrame,
            identity, packet, stack, entry, CallbackPhase.Prepared);
        if (_dispatchingCallbackReturn) _nextCallback = pending;
        else _pendingCallback = pending;
        // Prepare only. The portable wrapper must finish publishing its guards
        // and Pending state before the outer CPU can enter this Hook.
        return true;
    }

    private M68kHostGatewayResult ActivatePreparedCallback(M68kCpuState state, uint token,
        bool fromReturn = false)
    {
        var pending = _pendingCallback;
        if (!pending.IsActive || pending.Phase != CallbackPhase.Prepared || pending.Token != token ||
            !TryReadCallbackOwner(token, out var observed) ||
            observed.Phase != PortableLayers.LayersCallbackPhase.AwaitingReturn ||
            !PortableLayers.LayersCallbackContextCore.SameIdentity(pending.Identity, observed.Identity) ||
            !CallbackStackStorage(pending.Identity, pending.Stack) ||
            !CallbackBanksMatch(state, pending.Stack,
                fromReturn ? pending.Stack.TokenPointer : pending.Stack.Pointer))
            return FaultCallbackReturn(state, LayersCallbackReturnFault.InvalidOwner);
        var memory = CreatePlatform(state);
        LayersHostCallbackTokenCodec.Write(ref memory, APTR.FromPointer(pending.Stack.TokenPointer),
            pending.IssuedToken);
        _memory.WriteLong(pending.Stack.TokenPointer - 4u, HookContinuationAddress);
        state.A[0] = pending.Identity.Hook.Raw;
        state.A[1] = pending.Identity.Message.Raw;
        state.A[2] = pending.Identity.Target.Raw;
        state.SetActiveStackPointer(pending.Stack.TokenPointer - 4u);
        state.ProgramCounter = pending.Entry;
        _pendingCallback = pending with { Phase = CallbackPhase.Armed };
        // Suppress FF00's implicit RTS; the next outer instruction is the Hook.
        return M68kHostGatewayResult.BlockCurrentTask;
    }

    private LayersCallbackReturnFault ValidateCallbackReturn(M68kCpuState state,
        uint gateway, CallbackPhase phase, bool requirePendingOwner)
    {
        var pending = _pendingCallback;
        if (!_active || !pending.IsActive || pending.Phase != phase)
            return LayersCallbackReturnFault.NotPending;
        if (_getCurrentTask() != pending.Identity.Actor.Raw)
            return LayersCallbackReturnFault.WrongActor;
        if (state.Halted || state.Stopped || state.LastOpcode != 0xFF00 ||
            state.LastInstructionProgramCounter != gateway || state.ProgramCounter != gateway + 6u ||
            !_memory.Bus.HasHostGateway(gateway) ||
            !_memory.Bus.IsCpuPhysicalAddressMapped(gateway, 6, AmigaBusAccessKind.CpuInstructionFetch))
            return LayersCallbackReturnFault.InvalidBoundary;
        if (state.A[7] != pending.Stack.TokenPointer ||
            !CallbackBanksMatch(state, pending.Stack, pending.Stack.TokenPointer) ||
            !CallbackStackStorage(pending.Identity, pending.Stack))
            return LayersCallbackReturnFault.InvalidStack;
        var memory = CreatePlatform(state);
        var packet = LayersHostCallbackTokenCodec.Read(ref memory,
            APTR.FromPointer(pending.Stack.TokenPointer));
        if (!LayersHostCallbackTokenCodec.Same(packet, pending.IssuedToken))
            return LayersCallbackReturnFault.InvalidToken;
        if (!_linked || _libraryBase != pending.Identity.Authority.LibraryBase.Raw ||
            _root != pending.Identity.Authority.Root.Raw ||
            _getExecBase() != pending.Identity.Authority.ExecBase.Raw)
            return LayersCallbackReturnFault.InvalidOwner;
        if (requirePendingOwner && (!TryReadCallbackOwner(pending.Token, out var observed) ||
            observed.Phase != PortableLayers.LayersCallbackPhase.AwaitingReturn ||
            !PortableLayers.LayersCallbackContextCore.SameIdentity(pending.Identity, observed.Identity)))
            return LayersCallbackReturnFault.InvalidOwner;
        return LayersCallbackReturnFault.None;
    }

    private M68kHostGatewayResult ContinueCallbackWait(M68kCpuState state, PendingWait wait)
    {
        var phase = _pendingCallback.Phase;
        if (phase != CallbackPhase.ReturnObserved && phase != CallbackPhase.ReturnAccepted)
            return FaultCallbackReturn(state, LayersCallbackReturnFault.NotPending);
        var fault = ValidateCallbackReturn(state, BlockContinuationAddress, phase,
            requirePendingOwner: phase == CallbackPhase.ReturnObserved);
        if (fault != LayersCallbackReturnFault.None) return FaultCallbackReturn(state, fault);
        if (wait.Lvo != _pendingCallback.Lvo || wait.Token == 0)
            return FaultCallbackReturn(state, LayersCallbackReturnFault.InvalidOwner);
        // Retain the wait entry too until owner acceptance/cleanup is known.
        // A refused portable resume must not silently consume this host owner.
        return DispatchCallbackReturn(state, wait.Token);
    }

    private M68kHostGatewayResult DispatchCallbackReturn(M68kCpuState state, uint resumeToken)
    {
        var pending = _pendingCallback;
        var alreadyAccepted = pending.Phase == CallbackPhase.ReturnAccepted;
        var frame = pending.Frame;
        var platform = CreatePlatform(state);
        _dispatchState = state;
        _dispatchLvo = pending.Lvo;
        _dispatchOriginalFrame = pending.Frame;
        _dispatchingCallbackReturn = true;
        _parkAccepted = false;
        var result = PortableLayers.LayersVectorRouter.Dispatch(ref platform,
            APTR.FromPointer(_root), pending.Lvo, ref frame, resumeToken,
            callbackSucceeded: true, out var accepted);
        _dispatchState = null;
        _dispatchingCallbackReturn = false;

        if (!alreadyAccepted && !accepted)
        {
            if (_nextCallback.IsActive || result.Disposition !=
                    PortableLayers.LayersGatewayDisposition.BlockCurrentTask)
                return FaultCallbackReturn(state, LayersCallbackReturnFault.UnexpectedDispatch);
            return RetainCallbackWait(state, result);
        }
        _pendingCallback = pending with { Phase = CallbackPhase.ReturnAccepted };
        if (_nextCallback.IsActive)
        {
            if (result.Disposition != PortableLayers.LayersGatewayDisposition.InvokeGuestCallback ||
                result.ContinuationToken != _nextCallback.Token)
                return FaultCallbackReturn(state, LayersCallbackReturnFault.UnexpectedDispatch);
            _pendingCallback = _nextCallback;
            _nextCallback = default;
            var activated = ActivatePreparedCallback(state, result.ContinuationToken, fromReturn: true);
            if (LastCallbackReturnFault == LayersCallbackReturnFault.None)
                _pendingWaits.Remove(pending.Identity.Actor.Raw);
            return activated;
        }
        if (result.Disposition == PortableLayers.LayersGatewayDisposition.Completed &&
            result.ContinuationToken == 0)
        {
            // Acceptance is not a cleanup receipt. A locked wrapper or a
            // malformed cleanup retry can return failure/token-zero while a
            // private operation remains. Inspect current owners, never the
            // potentially freed address from the earlier Hook identity.
            if (!PortableLayers.LayersCallbackContextCore.TryReadRetired(ref platform,
                    pending.Identity.Authority, pending.Identity.Actor, out var retired) || !retired)
                return FaultCallbackReturn(state, LayersCallbackReturnFault.UnexpectedDispatch);
            _pendingWaits.Remove(pending.Identity.Actor.Raw);
            // The result policy is already applied to frame by the one public
            // router. Restore banks and consume the original caller LONG once,
            // explicitly; an FF00 implicit RTS would consume the token instead.
            Apply(state, frame);
            state.SetUserStackPointer(pending.Stack.User);
            state.SetInterruptStackPointer(pending.Stack.Supervisor);
            state.SetMasterStackPointer(pending.Stack.Master);
            state.SetActiveStackPointer(pending.Stack.Pointer + 4u);
            state.ProgramCounter = pending.Stack.CallerReturn;
            _pendingCallback = default;
            return M68kHostGatewayResult.BlockCurrentTask;
        }
        return RetainCallbackWait(state, result);
    }

    private M68kHostGatewayResult RetainCallbackWait(M68kCpuState state,
        PortableLayers.LayersGatewayResult result)
    {
        if (result.ContinuationToken == 0)
            return FaultCallbackReturn(state, LayersCallbackReturnFault.UnexpectedDispatch);
        var retry = result.Disposition == PortableLayers.LayersGatewayDisposition.Completed;
        if ((retry && !_suspendTask(state, BlockContinuationAddress)) ||
            (!retry && (result.Disposition != PortableLayers.LayersGatewayDisposition.BlockCurrentTask ||
                !_parkAccepted)))
            return FaultCallbackReturn(state, LayersCallbackReturnFault.UnexpectedDispatch);
        var pending = _pendingCallback;
        _pendingWaits[pending.Identity.Actor.Raw] = new PendingWait(pending.Lvo,
            result.ContinuationToken, pending.Frame, PendingWaitKind.CallbackReturn, 0, 0);
        if (retry) _wakeTask(pending.Identity.Actor.Raw);
        return M68kHostGatewayResult.BlockCurrentTask;
    }

    private M68kHostGatewayResult FaultCallbackReturn(M68kCpuState state, LayersCallbackReturnFault fault)
    {
        LastCallbackReturnFault = fault;
        _diagnose($"Layers callback return stopped: {fault}; PC=0x{state.ProgramCounter:X8}, SP=0x{state.A[7]:X8}.");
        // Preserve CPU registers/stack and all retained owners. Never synthesize
        // a return or retire another actor's continuation on a protocol fault.
        state.Halted = true;
        return M68kHostGatewayResult.BlockCurrentTask;
    }

    private bool TryReadCallbackOwner(uint token, out PortableLayers.LayersCallbackSnapshot snapshot)
    {
        snapshot = default;
        if (!_active || !_linked || _root == 0 || _libraryBase == 0) return false;
        var platform = CreatePlatform();
        return PortableLayers.LayersCallbackContextCore.TryRead(ref platform,
            new PortableLayers.LayersActorAuthority
            {
                Root = APTR.FromPointer(_root), LibraryBase = APTR.FromPointer(_libraryBase),
                ExecBase = APTR.FromPointer(_getExecBase())
            }, APTR.FromPointer(_getCurrentTask()), token, out snapshot);
    }

    private bool ReadCallbackStack(M68kCpuState state, PortableLayers.LayersCallbackIdentity identity,
        out CallbackStack stack)
    {
        stack = default;
        if (!CallbackSpan(identity.Actor.Raw, global::Amiga.Task.Size) ||
            !CallbackSpan(state.A[7], 4)) return false;
        var memory = CreatePlatform(state);
        var task = ExecTaskCodec.Read(ref memory, identity.Actor);
        stack = new CallbackStack(state.A[7], task.StackLower.Raw, task.StackUpper.Raw,
            _memory.ReadLong(state.A[7]), state.UserStackPointer, state.SupervisorStackPointer,
            state.MasterStackPointer, CallbackMode(state), state.M68020StackModeEnabled);
        return CallbackBanksMatch(state, stack, stack.Pointer) && CallbackStackStorage(identity, stack);
    }

    private bool CallbackStackStorage(PortableLayers.LayersCallbackIdentity identity, CallbackStack stack)
    {
        if (_getCurrentTask() != identity.Actor.Raw ||
            !CallbackSpan(identity.Actor.Raw, global::Amiga.Task.Size) ||
            stack.Lower == 0 || (stack.Lower & 1) != 0 || (stack.Upper & 1) != 0 ||
            stack.Upper <= stack.Lower || stack.Upper - stack.Lower < LayersHostCallbackToken.Size + 8u ||
            stack.Pointer < stack.Lower || stack.Pointer - stack.Lower < LayersHostCallbackToken.Size + 4u ||
            stack.Pointer > stack.Upper - 4u || !CallbackSpan(stack.Pointer, 4)) return false;
        var memory = CreatePlatform();
        var task = ExecTaskCodec.Read(ref memory, identity.Actor);
        if ((task.Node.Type != (byte)NodeType.Task && task.Node.Type != (byte)NodeType.Process) ||
            task.State != TaskState.Running || task.StackLower.Raw != stack.Lower || task.StackUpper.Raw != stack.Upper ||
            !CallbackCode(stack.CallerReturn) || stack.CallerReturn == HookContinuationAddress ||
            stack.CallerReturn == HookContinuationAddress + 6u || stack.CallerReturn == BlockContinuationAddress ||
            stack.CallerReturn == BlockContinuationAddress + 6u ||
            _memory.ReadLong(stack.Pointer) != stack.CallerReturn) return false;
        var bytes = stack.Upper - stack.Lower;
        if (!CallbackSpan(stack.Lower, bytes) || !_memory.Bus.IsWritableMemoryRange(stack.Lower, (int)bytes) ||
            CallbackOverlap(stack.Lower, bytes, identity.Actor.Raw, global::Amiga.Task.Size) ||
            CallbackOverlap(stack.Lower, bytes, _root, PortableLayers.LayersPrivateRootCore.Size) ||
            CallbackOverlap(stack.Lower, bytes, _allocation, _allocationSize) ||
            CallbackOverlap(stack.Lower, bytes, identity.Authority.ExecBase.Raw, LayersExecBaseCodec.Size))
            return false;
        // After acceptance the old continuation may already be retired. Never
        // dereference it during a cleanup retry solely because it was captured.
        if (_pendingCallback.IsActive && _pendingCallback.Phase == CallbackPhase.ReturnAccepted)
            return true;
        if (!CallbackSpan(identity.Continuation.Raw, PortableLayers.LayersPrivateRecordCore.HeaderSize)) return false;
        var recordBytes = PortableLayers.LayersPrivateRecordCore.Size(ref memory, identity.Continuation);
        return CallbackSpan(identity.Continuation.Raw, recordBytes) &&
            !CallbackOverlap(stack.Lower, bytes, identity.Continuation.Raw, recordBytes);
    }

    private bool CallbackCode(uint address) => CallbackSpan(address, 2) &&
        _memory.Bus.IsCpuPhysicalAddressMapped(address, 2, AmigaBusAccessKind.CpuInstructionFetch);

    private bool CallbackSpan(uint address, uint bytes) => address != 0 && (address & 1) == 0 &&
        bytes != 0 && bytes <= int.MaxValue && address <= uint.MaxValue - (bytes - 1u) &&
        _memory.IsMapped(address, (int)bytes);

    private static bool CallbackOverlap(uint left, uint leftBytes, uint right, uint rightBytes) =>
        leftBytes != 0 && rightBytes != 0 && left < (ulong)right + rightBytes && right < (ulong)left + leftBytes;

    private static ushort CallbackMode(M68kCpuState state) =>
        (ushort)(state.StatusRegister & (M68kCpuState.Supervisor |
            (state.M68020StackModeEnabled ? M68kCpuState.Master : 0)));

    private static bool CallbackBanksMatch(M68kCpuState state, CallbackStack stack, uint pointer)
    {
        if (state.M68020StackModeEnabled != stack.M68020 || CallbackMode(state) != stack.Mode ||
            (pointer & 1) != 0 || state.A[7] != pointer) return false;
        var user = (stack.Mode & M68kCpuState.Supervisor) == 0;
        var master = !user && stack.M68020 && (stack.Mode & M68kCpuState.Master) != 0;
        return state.UserStackPointer == (user ? pointer : stack.User) &&
            state.SupervisorStackPointer == (!user && !master ? pointer : stack.Supervisor) &&
            state.MasterStackPointer == (master ? pointer : stack.Master);
    }
}
