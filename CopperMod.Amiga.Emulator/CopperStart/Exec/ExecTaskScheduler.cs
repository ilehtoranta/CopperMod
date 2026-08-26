using Copper68k;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>
/// Boundary-only dispatch latch.  KS 3.1 owns task context frames in tc_SPReg;
/// this class deliberately does not cache a managed CPU image for ROM tasks.
/// </summary>
internal sealed class ExecTaskScheduler
{
    public bool DispatchPending { get; private set; }

    public void Reset()
    {
        DispatchPending = false;
    }

    // Kept as migration no-ops for CopperStart's synthetic compatibility path.
    // ROM task contexts are serialized by the native Schedule/Switch routines.
    public void CaptureCurrent(uint task, M68kCpuState state) { }

    public void Register(uint task, M68kCpuState initialState)
    {
        DispatchPending = true;
    }

    public void Remove(uint task)
    {
        DispatchPending = true;
    }

    public void RequestDispatch() => DispatchPending = true;
    public void AcknowledgeDispatch() => DispatchPending = false;
}
