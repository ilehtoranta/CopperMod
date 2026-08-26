using System.Collections.Generic;
using Copper68k;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>Shared request lifecycle; all blocking completion flows through Exec Wait.</summary>
internal sealed class ExecIoServices
{
    private const int IoErrorOffset = 0x1F;
    private readonly CopperStartExecContext _context;
    private readonly ExecSignalServices _signals;
    private readonly System.Action<M68kCpuState> _completeSynchronously;
    private readonly HashSet<uint> _active = new();

    public ExecIoServices(CopperStartExecContext context, ExecSignalServices signals, System.Action<M68kCpuState> completeSynchronously)
    { _context = context; _signals = signals; _completeSynchronously = completeSynchronously; }

    public bool IsActive(uint io) => _active.Contains(io);
    public void Reset() => _active.Clear();
    public M68kHostGatewayResult SendIo(M68kCpuState state)
    {
        var io = state.A[1];
        if (io == 0 || !_context.Memory.IsMapped(io + IoErrorOffset, 1)) { state.D[0] = 0; return M68kHostGatewayResult.Completed; }
        _active.Add(io); _completeSynchronously(state); _active.Remove(io); state.D[0] = 0;
        return M68kHostGatewayResult.Completed;
    }
    public M68kHostGatewayResult CheckIo(M68kCpuState state) { state.D[0] = _active.Contains(state.A[1]) ? 0u : state.A[1]; return M68kHostGatewayResult.Completed; }
    public M68kHostGatewayResult WaitIo(M68kCpuState state) => _signals.WaitForIo(state, state.A[1]);
    public M68kHostGatewayResult AbortIo(M68kCpuState state) { state.D[0] = _active.Remove(state.A[1]) ? 0u : 0xFFFF_FFFF; return M68kHostGatewayResult.Completed; }
    public M68kHostGatewayResult DoIo(M68kCpuState state)
    {
        var result = SendIo(state);
        return result == M68kHostGatewayResult.Completed ? WaitIo(state) : result;
    }
}
