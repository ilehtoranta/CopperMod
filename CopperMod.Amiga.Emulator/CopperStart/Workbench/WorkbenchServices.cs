using Copper68k;
using PortableWorkbench = CopperStart.Workbench;

namespace CopperMod.Amiga.CopperStart.Workbench;

internal sealed class WorkbenchServices
{
	private readonly CopperStartWorkbenchContext _context;
	public WorkbenchServices(CopperStartWorkbenchContext context) => _context = context;
	public void Invoke(M68kCpuState state, int lvo) { var p = new WorkbenchHostPlatform(_context); state.D[0] = PortableWorkbench.WorkbenchCore.Invoke(ref p, (short)lvo, state.D[0], state.D[1], state.A[0], state.A[1], state.A[2]); }
}

internal readonly struct WorkbenchHostPlatform : PortableWorkbench.IWorkbenchPlatform
{
	private readonly CopperStartWorkbenchContext _context;
	public WorkbenchHostPlatform(CopperStartWorkbenchContext context) => _context = context;
	public uint InvokeWorkbench(short lvo, uint d0, uint d1, uint a0, uint a1, uint a2) { _context.LogCall(lvo); _ = _context.EnsureScreen(); return _context.EnsureHostObject(); }
}
