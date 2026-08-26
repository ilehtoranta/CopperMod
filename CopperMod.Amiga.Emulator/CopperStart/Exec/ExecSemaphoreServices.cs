using System;
using System.Collections.Generic;
using Copper68k;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>
/// Signal semaphores backed by their guest structures. Contended obtain calls
/// use Exec's normal task-wait transition and resume only through the fixed
/// host wait gateway after ReleaseSemaphore grants ownership.
/// </summary>
internal sealed class ExecSemaphoreServices
{
    private const int NestCountOffset = 0x0E;
    private const int WaitQueueOffset = 0x10;
    private const int OwnerOffset = 0x28;
    private const int QueueCountOffset = 0x2C;
    private const int ReadyListOffset = 0x196;
    private readonly CopperStartExecContext _context;
    private readonly ExecSignalServices _signals;
    private readonly Dictionary<uint, SemaphoreState> _states = new();
    private readonly Dictionary<uint, PendingRequest> _pending = new();

    private sealed class SemaphoreState
    {
        public uint ExclusiveOwner;
        public int ExclusiveNesting;
        public readonly Dictionary<uint, int> SharedOwners = new();
        public readonly Queue<PendingRequest> Waiters = new();
    }

    private sealed class PendingRequest
    {
        public PendingRequest(uint semaphore, uint task, bool shared) { Semaphore = semaphore; Task = task; Shared = shared; }
        public uint Semaphore { get; }
        public uint Task { get; }
        public bool Shared { get; }
        public bool Granted { get; set; }
    }

    public ExecSemaphoreServices(CopperStartExecContext context, ExecSignalServices signals)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _signals = signals ?? throw new ArgumentNullException(nameof(signals));
    }

    public void Reset() { _states.Clear(); _pending.Clear(); }

    public uint InitSemaphore(M68kCpuState state)
    {
        var semaphore = state.A[0];
        if (!IsValid(semaphore)) return 0;
        _states.Remove(semaphore);
        _context.Memory.WriteWord(semaphore + NestCountOffset, 0);
        InitializeMinList(semaphore + WaitQueueOffset);
        _context.Memory.WriteLong(semaphore + OwnerOffset, 0);
        _context.Memory.WriteWord(semaphore + QueueCountOffset, 0);
        return 0;
    }

    public M68kHostGatewayResult ObtainExclusive(M68kCpuState state) => Obtain(state, shared: false, attempt: false);
    public M68kHostGatewayResult ObtainShared(M68kCpuState state) => Obtain(state, shared: true, attempt: false);
    public uint AttemptExclusive(M68kCpuState state) => TryAcquire(state.A[0], _context.GetCurrentTask(), shared: false) ? 1u : 0u;
    public uint AttemptShared(M68kCpuState state) => TryAcquire(state.A[0], _context.GetCurrentTask(), shared: true) ? 1u : 0u;

    public M68kHostGatewayResult Release(M68kCpuState state)
    {
        var semaphore = state.A[0];
        var task = _context.GetCurrentTask();
        if (!IsValid(semaphore) || task == 0 || !_states.TryGetValue(semaphore, out var current))
        {
            return M68kHostGatewayResult.Completed;
        }

        if (current.ExclusiveOwner == task)
        {
            if (--current.ExclusiveNesting > 0)
            {
                Publish(semaphore, current);
                return M68kHostGatewayResult.Completed;
            }

            current.ExclusiveOwner = 0;
            current.ExclusiveNesting = 0;
        }
        else if (current.SharedOwners.TryGetValue(task, out var sharedNesting))
        {
            if (sharedNesting > 1) current.SharedOwners[task] = sharedNesting - 1;
            else current.SharedOwners.Remove(task);
        }
        else
        {
            return M68kHostGatewayResult.Completed;
        }

        var wokeTask = GrantWaiters(semaphore, current, state);
        Publish(semaphore, current);
        return wokeTask ? M68kHostGatewayResult.Reschedule : M68kHostGatewayResult.Completed;
    }

    public bool TryContinueWait(M68kCpuState state, out M68kHostGatewayResult result)
    {
        var task = _context.GetCurrentTask();
        if (!_pending.TryGetValue(task, out var request))
        {
            result = M68kHostGatewayResult.Completed;
            return false;
        }

        if (!request.Granted)
        {
            result = _signals.BlockCurrentTask(state);
            return true;
        }

        _pending.Remove(task);
        result = M68kHostGatewayResult.Completed;
        return true;
    }

    private M68kHostGatewayResult Obtain(M68kCpuState state, bool shared, bool attempt)
    {
        var semaphore = state.A[0];
        var task = _context.GetCurrentTask();
        if (!IsValid(semaphore) || task == 0)
        {
            state.D[0] = attempt ? 0u : state.D[0];
            return M68kHostGatewayResult.Completed;
        }

        if (TryAcquire(semaphore, task, shared))
        {
            state.D[0] = attempt ? 1u : state.D[0];
            return M68kHostGatewayResult.Completed;
        }

        if (attempt)
        {
            state.D[0] = 0;
            return M68kHostGatewayResult.Completed;
        }

        var owner = GetState(semaphore);
        var request = new PendingRequest(semaphore, task, shared);
        owner.Waiters.Enqueue(request);
        _pending[task] = request;
        Publish(semaphore, owner);
        var result = _signals.BlockCurrentTask(state);
        if (result == M68kHostGatewayResult.BlockCurrentTask)
        {
            return result;
        }

        // A malformed fixture or a compatibility profile may not have a
        // native scheduling vector.  Do not leave an otherwise running task
        // recorded as an invisible waiter in that case.
        RemovePendingRequest(request, owner);
        return M68kHostGatewayResult.Completed;
    }

    private bool TryAcquire(uint semaphore, uint task, bool shared)
    {
        if (!IsValid(semaphore) || task == 0) return false;
        var current = GetState(semaphore);
        if (current.ExclusiveOwner == task)
        {
            current.ExclusiveNesting++;
            Publish(semaphore, current);
            return true;
        }

        if (current.SharedOwners.TryGetValue(task, out var sharedNesting))
        {
            if (!shared) return false;
            current.SharedOwners[task] = sharedNesting + 1;
            Publish(semaphore, current);
            return true;
        }

        if (shared)
        {
            // Do not let a newly arriving shared owner overtake requests which
            // are already asleep on this semaphore.  ReleaseSemaphore grants a
            // consecutive shared prefix together, preserving FIFO order.
            if (current.ExclusiveOwner != 0 || current.Waiters.Count != 0) return false;
            current.SharedOwners[task] = 1;
        }
        else
        {
            if (current.ExclusiveOwner != 0 || current.SharedOwners.Count != 0 || current.Waiters.Count != 0) return false;
            current.ExclusiveOwner = task;
            current.ExclusiveNesting = 1;
        }

        Publish(semaphore, current);
        return true;
    }

    private SemaphoreState GetState(uint semaphore)
    {
        if (_states.TryGetValue(semaphore, out var current)) return current;
        current = new SemaphoreState();
        var owner = _context.Memory.ReadLong(semaphore + OwnerOffset);
        var nesting = unchecked((short)_context.Memory.ReadWord(semaphore + NestCountOffset));
        if (owner != 0 && nesting > 0)
        {
            current.ExclusiveOwner = owner;
            current.ExclusiveNesting = nesting;
        }

        _states.Add(semaphore, current);
        return current;
    }

    private bool GrantWaiters(uint semaphore, SemaphoreState current, M68kCpuState state)
    {
        if (current.ExclusiveOwner != 0 || current.SharedOwners.Count != 0 || current.Waiters.Count == 0)
        {
            return false;
        }

        var granted = false;
        var first = current.Waiters.Dequeue();
        Grant(first, current, state);
        granted = true;
        if (first.Shared)
        {
            while (current.Waiters.Count != 0 && current.Waiters.Peek().Shared)
            {
                Grant(current.Waiters.Dequeue(), current, state);
            }
        }

        return granted;
    }

    private void Grant(PendingRequest request, SemaphoreState current, M68kCpuState state)
    {
        request.Granted = true;
        if (request.Shared) current.SharedOwners[request.Task] = 1;
        else { current.ExclusiveOwner = request.Task; current.ExclusiveNesting = 1; }
        _context.MoveTaskToList(request.Task, _context.GetExecBase() + ReadyListOffset, state);
        _context.RequestDispatch();
    }

    private void RemovePendingRequest(PendingRequest request, SemaphoreState current)
    {
        _pending.Remove(request.Task);
        if (current.Waiters.Count == 0)
        {
            Publish(request.Semaphore, current);
            return;
        }

        var retained = new Queue<PendingRequest>();
        while (current.Waiters.Count != 0)
        {
            var candidate = current.Waiters.Dequeue();
            if (!ReferenceEquals(candidate, request)) retained.Enqueue(candidate);
        }

        while (retained.Count != 0) current.Waiters.Enqueue(retained.Dequeue());
        Publish(request.Semaphore, current);
    }

    private void Publish(uint semaphore, SemaphoreState current)
    {
        var sharedCount = 0;
        foreach (var count in current.SharedOwners.Values) sharedCount += count;
        _context.Memory.WriteLong(semaphore + OwnerOffset, current.ExclusiveOwner);
        _context.Memory.WriteWord(semaphore + NestCountOffset,
            unchecked((ushort)(current.ExclusiveOwner != 0 ? current.ExclusiveNesting : -sharedCount)));
        _context.Memory.WriteWord(semaphore + QueueCountOffset, unchecked((ushort)current.Waiters.Count));
    }

    private bool IsValid(uint semaphore) => semaphore != 0 && _context.Memory.IsMapped(semaphore, 0x2E);
    private void InitializeMinList(uint list)
    {
        _context.Memory.WriteLong(list, list + 4);
        _context.Memory.WriteLong(list + 4, 0);
        _context.Memory.WriteLong(list + 8, list);
    }
}
