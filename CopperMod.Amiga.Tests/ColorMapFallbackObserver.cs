namespace CopperMod.Amiga.Tests;

internal struct ColorMapFallbackObserver
{
    private bool _branched;

    internal bool Observe(AmigaBus bus, uint pc, uint fallback)
    {
        // A distant fallback can be a local BRA into an RTS. The public void
        // wrapper also returns by RTS, after restoring the incoming D0.
        var opcode = bus.ReadWord(pc);
        var declined = pc == fallback || (_branched && opcode == 0x4E75);
        _branched = (opcode & 0xFF00) == 0x6000;
        return declined;
    }
}
