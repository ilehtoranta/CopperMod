using Copper68k;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>FindName dispatch for native Exec lists and CopperStart's library compatibility names.</summary>
internal sealed class ExecNameServices
{
    private readonly CopperStartExecContext _context;
    private readonly ExecListServices _lists;
    public ExecNameServices(CopperStartExecContext context, ExecListServices lists) { _context = context; _lists = lists; }
    public void FindName(M68kCpuState state)
    {
        if (_context.UsesRomExec()) { state.D[0] = _lists.FindName(state.A[0], state.A[1]); return; }
        var name = state.A[1] == 0 ? null : _context.ReadString(state.A[1], 96);
        state.D[0] = _context.FindCompatibilityName(name, state.A[1]);
    }
}
