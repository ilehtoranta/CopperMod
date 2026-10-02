using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>
/// Deterministic scheduler-side queues for QBlit/QBSBlit.
///
/// The queue deliberately stops at the custom-chip boundary: a scheduler
/// dequeues one ready node, executes its guest function through its own 68k
/// gateway, and calls <see cref="CompleteInFlight"/> after the node has
/// completed.  V39+ keeps QBlit and QBSBlit requests in one FIFO; a
/// beam-synchronized head therefore gates later ordinary requests until its
/// beam predicate is satisfied.  This keeps guest callback execution and bus
/// arbitration in the owner while making queue order, beam gating, ownership,
/// and completion visible and testable in the portable graphics layer.
/// </summary>
internal sealed class GraphicsBlitterFifoBackend :
    IGraphicsBlitterBackend,
    IGraphicsQueuedBlitterBackend,
    IGraphicsQueuedBlitterLinkBackend,
    IGraphicsBlitterOwnershipStatusBackend,
    IGraphicsQueuedBlitterStatusBackend,
    IGraphicsTimedBlitterBackend
{
    private readonly Queue<GraphicsQueuedBlitOperation> _queue = new();
    private readonly int _capacity;
    private GraphicsQueuedBlitOperation? _inFlight;
    private uint _tailAddress;
    private bool _owned;
    private long _lastCompletionCycle;

    internal GraphicsBlitterFifoBackend(int capacity = 256)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _capacity = capacity;
    }

    internal int Count => _queue.Count;
    internal bool IsOwned => _owned;
    internal bool HasInFlight => _inFlight.HasValue;
    internal bool HasPending => _inFlight.HasValue || Count != 0;
    internal long LastCompletionCycle => _lastCompletionCycle;

    public bool PublishesGuestLinks => true;

    internal bool TryGetInFlight(out GraphicsQueuedBlitOperation operation)
    {
        if (_inFlight.HasValue)
        {
            operation = _inFlight.Value;
            return true;
        }

        operation = default;
        return false;
    }

    internal bool TryPeek(out GraphicsQueuedBlitOperation operation)
    {
        if (_queue.Count != 0)
        {
            operation = _queue.Peek();
            return true;
        }

        operation = default;
        return false;
    }

    /// <summary>
    /// Selects the FIFO head that can make progress at a scheduler boundary.
    /// Submission-cycle admission and the in-flight boundary are owned here;
    /// an optional beam predicate is applied when supplied.  A scheduler that
    /// has a profile-aware beam owner may leave the predicate null, inspect the
    /// returned QBS head itself, and then call <see cref="TryDequeueReady"/>
    /// with an affirmative adapter.  A not-yet-ready QBS head still gates
    /// later ordinary requests, matching the V39+ single-queue contract.
    /// </summary>
    internal bool TryPeekForSchedule(
        long cycle,
        Func<short, long, bool>? beamReady,
        out GraphicsQueuedBlitOperation operation)
    {
        operation = default;
        if (_inFlight.HasValue || _queue.Count == 0)
            return false;

        var candidate = _queue.Peek();
        if (cycle < candidate.SubmittedCycle)
            return false;

        if (candidate.BeamSynchronized &&
            beamReady is not null &&
            !beamReady(candidate.BeamSync, cycle))
        {
            return false;
        }

        operation = candidate;
        return true;
    }

    internal void Reset()
    {
        _queue.Clear();
        _inFlight = null;
        _tailAddress = 0;
        _owned = false;
        _lastCompletionCycle = 0;
    }

    public void Own() => _ = TryOwn(0);

    public void Disown() => _ = TryDisown(0);

    public void Wait() => _ = TryWait(0, out _);

    public void Submit(uint operationAddress)
    {
        if (operationAddress == 0)
            return;

        _ = TryEnqueue(new GraphicsQueuedBlitOperation(
            operationAddress,
            BeamSynchronized: false,
            BeamSync: 0,
            SubmittedCycle: 0));
    }

    public void SubmitQueued(uint operationAddress, bool beamSynchronized, short beamSync)
        => _ = TrySubmitQueued(operationAddress, beamSynchronized, beamSync, 0);

    public bool TryOwn()
        => TryOwn(0);

    public bool TryDisown()
        => TryDisown(0);

    public bool TryWait()
        => TryWait(0, out _);

    public bool TrySubmitQueued(uint operationAddress, bool beamSynchronized, short beamSync)
        => TrySubmitQueued(operationAddress, beamSynchronized, beamSync, 0);

    public bool TrySubmitQueued(
        IGraphicsMemory memory,
        uint operationAddress,
        bool beamSynchronized,
        short beamSync)
        => TrySubmitQueued(memory, operationAddress, beamSynchronized, beamSync, 0);

    public bool TrySubmitQueued(
        IGraphicsMemory memory,
        uint operationAddress,
        bool beamSynchronized,
        short beamSync,
        long cycle)
    {
        if (memory is null || operationAddress == 0)
            return false;

        return TryEnqueueLinked(
            memory,
            new GraphicsQueuedBlitOperation(
                operationAddress,
                beamSynchronized,
                beamSync,
                cycle));
    }

    public bool TryOwn(long cycle)
    {
        if (_owned || HasPending)
            return false;

        _owned = true;
        return true;
    }

    public bool TryDisown(long cycle)
    {
        if (!_owned)
            return false;

        _owned = false;
        return true;
    }

    public bool TryWait(long cycle, out long completionCycle)
    {
        completionCycle = 0;
        // WaitBlit synchronizes the hardware operation that is currently in
        // flight.  It intentionally does not drain requests that are still
        // waiting in the QBlit/QBSBlit queues; those callbacks remain owned by
        // the asynchronous scheduler boundary.
        if (_inFlight.HasValue)
            return false;

        completionCycle = Math.Max(cycle, _lastCompletionCycle);
        return true;
    }

    public bool TrySubmitQueued(
        uint operationAddress,
        bool beamSynchronized,
        short beamSync,
        long cycle)
    {
        if (operationAddress == 0)
            return false;

        return TryEnqueue(new GraphicsQueuedBlitOperation(
            operationAddress,
            beamSynchronized,
            beamSync,
            cycle));
    }

    /// <summary>
    /// Dequeues the due FIFO head. A QBSBlit head stays blocked until the
    /// scheduler supplies a matching beam predicate; later QBlit requests do
    /// not bypass it. No host-side beam value is fabricated when the display
    /// owner is absent.
    /// </summary>
    internal bool TryDequeueReady(
        long cycle,
        Func<short, long, bool>? beamReady,
        out GraphicsQueuedBlitOperation operation)
    {
        operation = default;
        if (_inFlight.HasValue)
            return false;

        if (_queue.Count == 0)
            return false;

        var candidate = _queue.Peek();
        if (cycle < candidate.SubmittedCycle)
            return false;

        if (candidate.BeamSynchronized &&
            (beamReady is null || !beamReady(candidate.BeamSync, cycle)))
        {
            return false;
        }

        operation = _queue.Dequeue();
        _inFlight = operation;
        if (_queue.Count == 0)
            _tailAddress = 0;
        return true;
    }

    /// <summary>Marks the scheduler-owned head complete at a guest cycle.</summary>
    internal bool CompleteInFlight(long cycle)
    {
        if (!_inFlight.HasValue)
            return false;

        _inFlight = null;
        _lastCompletionCycle = Math.Max(_lastCompletionCycle, cycle);
        return true;
    }

    private bool TryEnqueue(GraphicsQueuedBlitOperation operation)
    {
        if (Count + (_inFlight.HasValue ? 1 : 0) >= _capacity)
            return false;

        _queue.Enqueue(operation);
        // This overload is the legacy address-only path. It cannot publish a
        // guest link, so do not let a later linked submission chain through an
        // untracked host-only tail.
        _tailAddress = 0;
        return true;
    }

    /// <summary>
    /// Publishes the system-owned guest <c>bn_Next</c> link and enqueues the
    /// operation as one transaction. The new node is terminated first; when a
    /// previous pending tail exists, that tail is then linked to the new node.
    /// If either guest write fails, the original bytes are restored and the
    /// host queue remains unchanged.
    /// </summary>
    private bool TryEnqueueLinked(
        IGraphicsMemory memory,
        GraphicsQueuedBlitOperation operation)
    {
        if (Count + (_inFlight.HasValue ? 1 : 0) >= _capacity ||
            ContainsAddress(operation.OperationAddress))
        {
            return false;
        }

        var nodeNextAddress = operation.OperationAddress +
            (uint)GraphicsLayouts.BltNodeNext;
        if (!memory.TryReadLong(nodeNextAddress, out var originalNodeNext))
            return false;

        var hasTail = _tailAddress != 0;
        uint originalTailNext = 0;
        var tailNextAddress = _tailAddress + (uint)GraphicsLayouts.BltNodeNext;
        if (hasTail && !memory.TryReadLong(tailNextAddress, out originalTailNext))
            return false;

        if (!memory.TryWriteLong(nodeNextAddress, 0))
        {
            RestoreLong(memory, nodeNextAddress, originalNodeNext);
            return false;
        }

        if (hasTail && !memory.TryWriteLong(tailNextAddress, operation.OperationAddress))
        {
            RestoreLong(memory, tailNextAddress, originalTailNext);
            RestoreLong(memory, nodeNextAddress, originalNodeNext);
            return false;
        }

        _queue.Enqueue(operation);
        _tailAddress = operation.OperationAddress;
        return true;
    }

    private static void RestoreLong(
        IGraphicsMemory memory,
        uint address,
        uint value)
    {
        _ = memory.TryWriteByte(address, (byte)(value >> 24));
        _ = memory.TryWriteByte(address + 1u, (byte)(value >> 16));
        _ = memory.TryWriteByte(address + 2u, (byte)(value >> 8));
        _ = memory.TryWriteByte(address + 3u, (byte)value);
    }

    private bool ContainsAddress(uint operationAddress)
    {
        if (_inFlight is { } inFlight && inFlight.OperationAddress == operationAddress)
            return true;

        foreach (var queued in _queue)
        {
            if (queued.OperationAddress == operationAddress)
                return true;
        }

        return false;
    }
}

internal readonly record struct GraphicsQueuedBlitOperation(
    uint OperationAddress,
    bool BeamSynchronized,
    short BeamSync,
    long SubmittedCycle);
