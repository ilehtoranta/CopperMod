using System;
using Copper68k;

namespace CopperMod.Amiga.CopperStart.Expansion;

/// <summary>CopperStart expansion.library compatibility gateway behavior.</summary>
internal sealed class ExpansionServices
{
    private readonly CopperStartExpansionContext _context;

    public ExpansionServices(CopperStartExpansionContext context)
        => _context = context ?? throw new ArgumentNullException(nameof(context));

    public void Invoke(M68kCpuState state, int displacement)
    {
        _context.LogCall(displacement);
        state.D[0] = _context.EnsureCompatibilityHostObject();
    }
}
