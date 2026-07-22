using System;
using Copper68k;
using CopperMod.Amiga.Bus;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>
/// Reset-scoped, concrete bridge between Exec services and the running Amiga.
/// Guest RAM/ROM structures use <see cref="Memory"/> directly; callers which
/// enter guest code always use <see cref="StartGuestSubroutine"/>.
/// </summary>
internal sealed class CopperStartExecContext
{
    public CopperStartExecContext(
        HostGuestMemory memory,
        Func<uint> execBase,
        Func<uint> currentTask,
        Func<uint, int, string> readString,
        Action<uint, uint, M68kCpuState> moveTaskToList,
        Action requestDispatch,
        Func<uint, bool> isFetchable,
        Action<uint, M68kCpuState> registerTask,
        Action<uint> removeTask,
        Action<M68kCpuState, uint, uint> startGuestSubroutine)
    {
        Memory = memory ?? throw new ArgumentNullException(nameof(memory));
        GetExecBase = execBase ?? throw new ArgumentNullException(nameof(execBase));
        GetCurrentTask = currentTask ?? throw new ArgumentNullException(nameof(currentTask));
        ReadString = readString ?? throw new ArgumentNullException(nameof(readString));
        MoveTaskToList = moveTaskToList ?? throw new ArgumentNullException(nameof(moveTaskToList));
        RequestDispatch = requestDispatch ?? throw new ArgumentNullException(nameof(requestDispatch));
        IsFetchable = isFetchable ?? throw new ArgumentNullException(nameof(isFetchable));
        RegisterTask = registerTask ?? throw new ArgumentNullException(nameof(registerTask));
        RemoveTask = removeTask ?? throw new ArgumentNullException(nameof(removeTask));
        StartGuestSubroutine = startGuestSubroutine ?? throw new ArgumentNullException(nameof(startGuestSubroutine));
    }

    public HostGuestMemory Memory { get; }
    public Func<uint> GetExecBase { get; }
    public Func<uint> GetCurrentTask { get; }
    public Func<uint, int, string> ReadString { get; }
    public Action<uint, uint, M68kCpuState> MoveTaskToList { get; }
    public Action RequestDispatch { get; }
    public Func<uint, bool> IsFetchable { get; }
    public Action<uint, M68kCpuState> RegisterTask { get; }
    public Action<uint> RemoveTask { get; }
    public Action<M68kCpuState, uint, uint> StartGuestSubroutine { get; }
}
