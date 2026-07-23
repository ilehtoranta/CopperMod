using System;
using Copper68k;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>
/// CopperStart's compatibility LVO table.  This is intentionally separate
/// from <see cref="ExecServices"/>, which owns installation and ROM overlays.
/// </summary>
internal sealed class ExecGatewayServices
{
    private readonly Action<int> _logCall;
    private readonly ExecMemoryServices _memory;
    private readonly ExecTaskServices _tasks;
    private readonly ExecListServices _lists;
    private readonly ExecSignalServices _signals;
    private readonly ExecSemaphoreServices _semaphores;
    private readonly ExecTrapServices _traps;
    private readonly ExecPortServices _ports;
    private readonly ExecPoolServices _pools;
    private readonly ExecInitStructServices _initStruct;
    private readonly Func<M68kCpuState, uint> _addInterruptServer;
    private readonly Func<M68kCpuState, uint> _removeInterruptServer;
    private readonly Func<M68kCpuState, uint> _getMessage;
    private readonly Func<M68kCpuState, M68kHostGatewayResult> _waitPort;
    private readonly Func<M68kCpuState, uint> _rawDoFmt;

    public ExecGatewayServices(
        Action<int> logCall, ExecMemoryServices memory, ExecTaskServices tasks,
        ExecListServices lists, ExecSignalServices signals, ExecSemaphoreServices semaphores, ExecTrapServices traps,
        ExecPortServices ports, ExecPoolServices pools, ExecInitStructServices initStruct,
        Func<M68kCpuState, uint> addInterruptServer,
        Func<M68kCpuState, uint> removeInterruptServer,
        Func<M68kCpuState, uint> getMessage,
        Func<M68kCpuState, M68kHostGatewayResult> waitPort,
        Func<M68kCpuState, uint> rawDoFmt)
    {
        _logCall = logCall ?? throw new ArgumentNullException(nameof(logCall));
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        _lists = lists ?? throw new ArgumentNullException(nameof(lists));
        _signals = signals ?? throw new ArgumentNullException(nameof(signals));
        _semaphores = semaphores ?? throw new ArgumentNullException(nameof(semaphores));
        _traps = traps ?? throw new ArgumentNullException(nameof(traps));
        _ports = ports ?? throw new ArgumentNullException(nameof(ports));
        _pools = pools ?? throw new ArgumentNullException(nameof(pools));
        _initStruct = initStruct ?? throw new ArgumentNullException(nameof(initStruct));
        _addInterruptServer = addInterruptServer ?? throw new ArgumentNullException(nameof(addInterruptServer));
        _removeInterruptServer = removeInterruptServer ?? throw new ArgumentNullException(nameof(removeInterruptServer));
        _getMessage = getMessage ?? throw new ArgumentNullException(nameof(getMessage));
        _waitPort = waitPort ?? throw new ArgumentNullException(nameof(waitPort));
        _rawDoFmt = rawDoFmt ?? throw new ArgumentNullException(nameof(rawDoFmt));
    }

    public M68kHostGatewayResult Invoke(M68kCpuState state, int lvo)
    {
        _logCall(lvo);
        if (lvo == -318) return _signals.Wait(state);
        if (lvo == -324) return _signals.Signal(state);
        if (lvo == -288) return _tasks.RemTask(state);
        if (lvo == -384) return _waitPort(state);
        if (lvo == -564) return _semaphores.ObtainExclusive(state);
        if (lvo == -570) return _semaphores.Release(state);
        if (lvo == -678) return _semaphores.ObtainShared(state);
        state.D[0] = lvo switch
        {
            -78 => _initStruct.InitStruct(state), -168 => _addInterruptServer(state), -174 => _removeInterruptServer(state),
            -186 => _memory.Allocate(state), -192 => _memory.Deallocate(state),
            -222 => _memory.AllocEntry(state), -228 => _memory.FreeEntry(state),
            -294 => _tasks.FindTask(state), -282 => _tasks.AddTask(state),
            -300 => _tasks.SetTaskPri(state), -120 => _tasks.Disable(state), -126 => _tasks.Enable(state),
            -132 => _tasks.Forbid(state), -138 => _tasks.Permit(state),
            -234 => _lists.Insert(state), -240 => _lists.AddHead(state), -246 => _lists.AddTail(state),
            -252 => _lists.Remove(state), -258 => _lists.RemHead(state), -264 => _lists.RemTail(state), -270 => _lists.Enqueue(state),
            -306 => _signals.SetSignal(state), -312 => _signals.SetExcept(state), -330 => _signals.AllocSignal(state), -336 => _signals.FreeSignal(state),
            -150 => EnterSuperState(state), -156 => EnterUserState(state),
            -342 => _traps.AllocTrap(state), -348 => _traps.FreeTrap(state),
            -354 => _ports.AddPort(state), -360 => _ports.RemPort(state), -366 => _ports.PutMsg(state),
            -372 => _getMessage(state), -378 => _ports.ReplyMsg(state), -390 => _ports.FindPort(state),
            -522 => _rawDoFmt(state), -534 => _memory.TypeOfMem(state), -624 => _memory.CopyMem(state),
            -630 => _memory.CopyMemQuick(state), -684 => _memory.AllocVec(state), -690 => _memory.FreeVec(state),
            -696 => _pools.CreatePool(state), -702 => _pools.DeletePool(state), -708 => _pools.AllocPooled(state), -714 => _pools.FreePooled(state),
            -558 => _semaphores.InitSemaphore(state), -576 => _semaphores.AttemptExclusive(state), -720 => _semaphores.AttemptShared(state),
            _ => 0
        };
        return M68kHostGatewayResult.Completed;
    }

    private static uint EnterSuperState(M68kCpuState state) => state.EnterSupervisorModeWithUserStack();
    private static uint EnterUserState(M68kCpuState state) { state.ReturnToUserModeWithUserStack(state.D[0]); return 0; }
}
