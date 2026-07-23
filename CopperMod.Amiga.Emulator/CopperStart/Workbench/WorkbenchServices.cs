using Copper68k;

namespace CopperMod.Amiga.CopperStart.Workbench;

/// <summary>CopperStart workbench.library compatibility contribution.</summary>
internal sealed class WorkbenchServices
{
    private readonly CopperStartWorkbenchContext _context;
    public WorkbenchServices(CopperStartWorkbenchContext context) => _context = context;
    public void Invoke(M68kCpuState state, int lvo)
    {
        _context.LogCall(lvo);
        _ = _context.EnsureScreen();
        state.D[0] = _context.EnsureHostObject();
    }
}
