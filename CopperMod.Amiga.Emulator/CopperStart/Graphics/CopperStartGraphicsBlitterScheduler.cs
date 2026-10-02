using System;
using Copper68k;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.CopperStart.Graphics;

/// <summary>
/// CopperStart owner for the timed graphics blitter FIFO.
///
/// The portable queue retains only the classic <c>bltnode</c> addresses and
/// timing metadata.  This scheduler is the host-side boundary that gives one
/// node to the custom-chip scheduler, enters its guest callback with A0 set to
/// the custom register base and A1 set to the node, and observes the
/// callback's D0 completion contract.  No guest
/// callback is invoked from the portable core itself, which keeps a future
/// CopperSharp68k ROM path free to replace this owner.
/// </summary>
internal sealed class CopperStartGraphicsBlitterScheduler :
    IGraphicsTimedBlitterBackend,
    IGraphicsQueuedBlitterLinkBackend
{
    // hardware/blit.h callback convention: A0 is the custom-chip register
    // base and A1 is the current bltnode. Keep this explicit at the host
    // boundary so a future CopperSharp68k implementation can use the same
    // register ABI without depending on a host object address.
    private const uint CustomRegisterBaseAddress = 0x00DFF000;

    // Keep this separate from the Layers continuation slots.  The address is a
    // host-gateway location, not a reusable guest ABI constant.
    internal const uint ContinuationAddress = 0x00F0_8C20;

    private const byte CleanupFlag = 0x40;

    private enum CallbackPhase : byte
    {
        Idle,
        FunctionRunning,
        RepeatReady,
        WaitingForBlitterIdle,
        CleanupRunning
    }

    private readonly record struct ActiveNode(
        uint NodeAddress,
        uint FunctionAddress,
        uint CleanupAddress,
        bool HasCleanup);

    private readonly AmigaBus _bus;
    private readonly HostGuestMemory _memory;
    private readonly Action<M68kCpuState, uint, uint> _startGuestSubroutine;
    private readonly GraphicsBlitterFifoBackend _fifo = new();
    private CallbackPhase _phase;
    private ActiveNode _activeNode;

    public bool PublishesGuestLinks => true;

    internal CopperStartGraphicsBlitterScheduler(
        AmigaBus bus,
        Action<M68kCpuState, uint, uint> startGuestSubroutine)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _memory = new HostGuestMemory(bus);
        _startGuestSubroutine = startGuestSubroutine ??
            throw new ArgumentNullException(nameof(startGuestSubroutine));
        RegisterContinuationGateway();
    }

    /// <summary>Runs one scheduler boundary at the CPU's current cycle.</summary>
    internal void ProcessPending(M68kCpuState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var cycle = Math.Max(0, state.Cycles);
        _bus.SynchronizeBlitterThrough(cycle);

        if (_phase is CallbackPhase.FunctionRunning or CallbackPhase.CleanupRunning)
            return;

        if (_phase == CallbackPhase.RepeatReady)
        {
            if (_bus.Blitter.Busy)
                return;

            DispatchFunction(state);
            return;
        }

        if (_phase == CallbackPhase.WaitingForBlitterIdle)
        {
            if (_bus.Blitter.Busy)
                return;

            CompleteFunctionAndMaybeCleanup(state);
            return;
        }

        if (_phase != CallbackPhase.Idle || _fifo.IsOwned || _bus.Blitter.Busy ||
            !_fifo.TryPeekForSchedule(cycle, beamReady: null, out var candidate))
        {
            return;
        }

        // Peek before dequeueing so the beam predicate can use the node's
        // submission frame.  That distinction is required for a target that
        // is not representable in the active profile: QBS catches such a
        // request up at the next frame boundary instead of retrying forever
        // in the current frame.
        if (candidate.BeamSynchronized &&
            !IsBeamReady(candidate.BeamSync, cycle, candidate.SubmittedCycle))
        {
            return;
        }

        // The scheduler has already applied its profile-aware predicate above;
        // pass an affirmative adapter so the FIFO does not interpret a null
        // provider predicate as an unowned QBS boundary.
        if (!_fifo.TryDequeueReady(
                cycle,
                beamReady: static (_, _) => true,
                out var operation))
        {
            return;
        }

        if (!TryReadNode(operation.OperationAddress, out var active))
        {
            // The node can be changed by the guest after QBlit.  A malformed
            // node is dropped at the same boundary instead of faulting the
            // host or leaving the FIFO permanently in-flight.
            _fifo.CompleteInFlight(cycle);
            return;
        }

        _activeNode = active;
        DispatchFunction(state);
    }

    /// <summary>
    /// Supplies the next wake candidate for the instruction-boundary runtime.
    /// </summary>
    internal long GetNextBoundaryCycle(long currentCycle, long targetCycle)
    {
        if (targetCycle <= currentCycle)
            return targetCycle;

        if (_phase is CallbackPhase.FunctionRunning or CallbackPhase.CleanupRunning ||
            _phase == CallbackPhase.Idle && !_fifo.HasPending)
        {
            return targetCycle;
        }

        if (_fifo.IsOwned)
            return targetCycle;

        var current = Math.Max(0, currentCycle);
        if (_bus.Blitter.Busy)
        {
            var hardwareWake = _bus.Blitter.GetNextWakeCandidateCycle(current, targetCycle);
            return hardwareWake.HasValue
                ? Math.Max(current + 1, hardwareWake.Value)
                : targetCycle;
        }

        if (_phase is CallbackPhase.RepeatReady or CallbackPhase.WaitingForBlitterIdle)
            return Math.Min(targetCycle, current + 1);

        if (!_fifo.TryPeekForSchedule(
                current,
                beamReady: null,
                out var operation))
            return targetCycle;

        if (current < operation.SubmittedCycle)
            return Math.Min(targetCycle, Math.Max(current + 1, operation.SubmittedCycle));

        if (!operation.BeamSynchronized)
            return Math.Min(targetCycle, current + 1);

        if (!IsBeamReady(operation.BeamSync, current, operation.SubmittedCycle))
        {
            return GetBeamWakeCycle(
                operation.BeamSync,
                operation.SubmittedCycle,
                current,
                targetCycle);
        }

        return Math.Min(targetCycle, current + 1);
    }

    public bool TryOwn(long cycle)
    {
        _bus.SynchronizeBlitterThrough(Math.Max(0, cycle));
        // OwnBlitter protects the software ownership gate, not the current
        // BBUSY phase.  Kickstart may wake the caller as soon as the previous
        // owner releases the gate while that owner's final custom-chip blit
        // is still draining; the caller must then issue WaitBlit before
        // touching the registers.  Rejecting BBUSY here would turn a valid
        // ownership acquisition into native fallback and would make the
        // documented OwnBlitter/WaitBlit sequence impossible for a timed
        // CopperStart owner.
        return _fifo.TryOwn(cycle);
    }

    public bool TryDisown(long cycle)
        => _fifo.TryDisown(cycle);

    public bool TryWait(long cycle, out long completionCycle)
    {
        completionCycle = 0;
        var now = Math.Max(0, cycle);
        _bus.SynchronizeBlitterThrough(now);
        // A queued QBlit/QBSBlit node is not an active hardware blit.  The
        // classic WaitBlit contract waits for the current hardware operation
        // only; queued callbacks stay in the scheduler FIFO and are not
        // drained by this synchronous vector.
        if (_phase != CallbackPhase.Idle || _fifo.HasInFlight)
            return false;

        if (_bus.Blitter.Busy)
        {
            var predicted = _bus.Blitter.GetPredictedCompletionCycle();
            if (predicted == long.MaxValue)
                return false;

            // WaitBlit must leave BBUSY clear before the caller touches any
            // blitter register.  Use the bus's canonical DMA drain rather
            // than the scalar synchronization shortcut: the latter may
            // deliberately stop before an unresolved live-slot transition.
            _bus.AdvanceDmaTo(predicted);
            now = Math.Max(now, predicted);
            if (_bus.Blitter.Busy)
                return false;
        }

        return _fifo.TryWait(now, out completionCycle);
    }

    public bool TrySubmitQueued(
        uint operationAddress,
        bool beamSynchronized,
        short beamSync,
        long cycle)
        => _fifo.TrySubmitQueued(
            operationAddress,
            beamSynchronized,
            beamSync,
            cycle);

    public bool TrySubmitQueued(
        IGraphicsMemory memory,
        uint operationAddress,
        bool beamSynchronized,
        short beamSync)
        => _fifo.TrySubmitQueued(
            memory,
            operationAddress,
            beamSynchronized,
            beamSync);

    public bool TrySubmitQueued(
        IGraphicsMemory memory,
        uint operationAddress,
        bool beamSynchronized,
        short beamSync,
        long cycle)
        => _fifo.TrySubmitQueued(
            memory,
            operationAddress,
            beamSynchronized,
            beamSync,
            cycle);

    /// <summary>Clears guest callback state and restores the gateway after bus reset.</summary>
    internal void Reset()
    {
        _fifo.Reset();
        _phase = CallbackPhase.Idle;
        _activeNode = default;
        RegisterContinuationGateway();
    }

    private void RegisterContinuationGateway()
        => _bus.RegisterHostGateway(ContinuationAddress, ContinueCallback);

    private M68kHostGatewayResult ContinueCallback(M68kCpuState state)
    {
        return _phase switch
        {
            CallbackPhase.FunctionRunning => ContinueFunction(state),
            CallbackPhase.CleanupRunning => ContinueCleanup(state),
            _ => M68kHostGatewayResult.Completed
        };
    }

    private M68kHostGatewayResult ContinueFunction(M68kCpuState state)
    {
        _bus.SynchronizeBlitterThrough(Math.Max(0, state.Cycles));
        if (state.D[0] != 0)
        {
            // The classic callback contract asks for another invocation the
            // next time the blitter is idle.  Keep the same node in-flight so
            // later FIFO nodes cannot pass it.
            _phase = CallbackPhase.RepeatReady;
            return M68kHostGatewayResult.Completed;
        }

        _phase = CallbackPhase.WaitingForBlitterIdle;
        if (!_bus.Blitter.Busy)
            CompleteFunctionAndMaybeCleanup(state);

        return M68kHostGatewayResult.Completed;
    }

    private M68kHostGatewayResult ContinueCleanup(M68kCpuState state)
    {
        _fifo.CompleteInFlight(Math.Max(0, state.Cycles));
        _activeNode = default;
        _phase = CallbackPhase.Idle;
        return M68kHostGatewayResult.Completed;
    }

    private void DispatchFunction(M68kCpuState state)
    {
        if (!CanEnterGuest(_activeNode.FunctionAddress, state))
        {
            state.D[0] = 0;
            // The function never ran, so do not invoke a guest cleanup pointer
            // from a node whose callback became unmapped or uncallable after
            // QBlit submission.
            _fifo.CompleteInFlight(Math.Max(0, state.Cycles));
            _activeNode = default;
            _phase = CallbackPhase.Idle;
            return;
        }

        _phase = CallbackPhase.FunctionRunning;
        state.A[0] = CustomRegisterBaseAddress;
        state.A[1] = _activeNode.NodeAddress;
        _startGuestSubroutine(state, _activeNode.FunctionAddress, ContinuationAddress);
    }

    private void CompleteFunctionAndMaybeCleanup(M68kCpuState state)
    {
        if (_activeNode.HasCleanup &&
            CanEnterGuest(_activeNode.CleanupAddress, state))
        {
            _phase = CallbackPhase.CleanupRunning;
            state.A[0] = CustomRegisterBaseAddress;
            state.A[1] = _activeNode.NodeAddress;
            _startGuestSubroutine(state, _activeNode.CleanupAddress, ContinuationAddress);
            return;
        }

        _fifo.CompleteInFlight(Math.Max(0, state.Cycles));
        _activeNode = default;
        _phase = CallbackPhase.Idle;
    }

    private bool CanEnterGuest(uint entry, M68kCpuState state)
    {
        // StartGuestSubroutine pushes the continuation LONG at A7-4 before
        // transferring control.  Preflight that exact stack slot here so a
        // malformed callback frame is declined at the scheduler boundary
        // instead of turning a queued blit into a host-side bus fault.  A
        // 68000 callback also requires the stack pointer to remain WORD
        // aligned; the guest entry itself is subject to the usual aligned
        // instruction-fetch check below.
        var stack = state.A[7];
        return entry != 0 &&
            (entry & 1u) == 0 &&
            (stack & 1u) == 0 &&
            stack >= 4 &&
            _bus.IsWritableMemoryRange(stack - 4, 4) &&
            _bus.IsCpuPhysicalAddressMapped(
                entry,
                2,
                AmigaBusAccessKind.CpuInstructionFetch);
    }

    private bool TryReadNode(uint nodeAddress, out ActiveNode active)
    {
        active = default;
        if ((nodeAddress & 1u) != 0 ||
            !_memory.IsMapped(nodeAddress, GraphicsLayouts.BltNodeSize))
        {
            return false;
        }

        var function = _memory.ReadLong(nodeAddress + (uint)GraphicsLayouts.BltNodeFunction);
        var status = _memory.ReadByte(nodeAddress + (uint)GraphicsLayouts.BltNodeStatus);
        var cleanup = _memory.ReadLong(nodeAddress + (uint)GraphicsLayouts.BltNodeCleanup);
        var hasCleanup = (status & CleanupFlag) != 0;
        if (function == 0 || (function & 1u) != 0 ||
            (hasCleanup && (cleanup == 0 || (cleanup & 1u) != 0)))
        {
            return false;
        }

        active = new ActiveNode(nodeAddress, function, cleanup, hasCleanup);
        return true;
    }

    private bool IsBeamReady(short target, long cycle, long submittedCycle)
    {
        var position = _bus.GetBeamPosition(cycle);
        var targetLine = (ushort)target & 0x01FF;
        if (targetLine < position.RasterLines)
            return position.BeamLine >= targetLine;

        // VBeamPos is a profile-bounded vertical counter.  If a caller gives
        // a line that this profile cannot produce, the classic queue's
        // catch-up rule still releases the node once the beam has wrapped
        // beyond the frame in which it was submitted.
        var submitted = _bus.GetBeamPosition(Math.Max(0, submittedCycle));
        return position.FrameStartCycle > submitted.FrameStartCycle;
    }

    private long GetBeamWakeCycle(
        short target,
        long submittedCycle,
        long current,
        long targetCycle)
    {
        var position = _bus.GetBeamPosition(current);
        var targetLine = (ushort)target & 0x01FF;
        if (targetLine >= position.RasterLines)
        {
            var submitted = _bus.GetBeamPosition(Math.Max(0, submittedCycle));
            var candidateFrame = _bus.GetNextFrameStartCycle(submitted.CurrentCycle);
            if (candidateFrame <= current)
                return Math.Min(targetCycle, current + 1);

            return Math.Min(targetCycle, Math.Max(current + 1, candidateFrame));
        }

        if (position.BeamLine >= targetLine)
            return Math.Min(targetCycle, current + 1);

        var candidate = _bus.GetLineStartCycle(position.FrameStartCycle, targetLine);
        if (candidate <= current)
        {
            var nextFrame = _bus.GetNextFrameStartCycle(current);
            candidate = _bus.GetLineStartCycle(nextFrame, targetLine);
        }

        return candidate <= targetCycle ? candidate : targetCycle;
    }
}
