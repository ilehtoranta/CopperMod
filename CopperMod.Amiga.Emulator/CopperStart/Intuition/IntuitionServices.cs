using Copper68k;

namespace CopperMod.Amiga.CopperStart.Intuition;

/// <summary>CopperStart intuition.library compatibility contribution.</summary>
internal sealed class IntuitionServices
{
    private readonly CopperStartIntuitionContext _context;
    public IntuitionServices(CopperStartIntuitionContext context) => _context = context;
    public void Invoke(M68kCpuState state, int lvo)
    {
        _context.LogCall(lvo);
        switch (lvo)
        {
            case -198: _context.ConfigureScreen(state.A[0]); state.D[0] = _context.EnsureScreen(); _ = _context.RethinkDisplay(state.Cycles); return;
            case -204: _context.ConfigureWindow(state.A[0]); state.D[0] = _context.EnsureWindow(); _ = _context.EnsureScreen(); _ = _context.RethinkDisplay(state.Cycles); return;
            case -150: _context.ModifyIdcmp(state); state.D[0] = 1; return;
            case -234: state.D[0] = 0; return;
            case -252: if (state.A[0] != 0) _context.SelectFrontViewPort(state.A[0] + 0x2C); state.D[0] = 0; return;
            case -276: _context.SetWindowTitles(state); state.D[0] = 0; return;
            case -378 or -384 or -390: state.D[0] = _context.RethinkDisplay(state.Cycles); return;
            case -396: _context.AllocRemember(state); return;
            case -408: state.D[0] = 0; return;
            case -294: state.D[0] = _context.GetViewAddress() != 0 ? _context.GetViewAddress() : _context.EnsureView(); return;
            case -300: state.D[0] = _context.GetViewPortAddress(); return;
            case -438: _context.AddGList(state); return;
            case -432: state.D[0] = 0; return;
            default: state.D[0] = _context.EnsureHostObject(); return;
        }
    }
}
