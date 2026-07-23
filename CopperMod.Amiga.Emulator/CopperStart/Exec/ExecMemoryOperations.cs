using System;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>Concrete access to the active Exec memory-list implementation.</summary>
internal sealed class ExecMemoryOperations
{
    public ExecMemoryOperations(Func<int, uint, uint> allocate, Action<uint, int> free, Action<uint, int> clear)
    {
        Allocate = allocate ?? throw new ArgumentNullException(nameof(allocate));
        Free = free ?? throw new ArgumentNullException(nameof(free));
        Clear = clear ?? throw new ArgumentNullException(nameof(clear));
    }
    public Func<int, uint, uint> Allocate { get; }
    public Action<uint, int> Free { get; }
    public Action<uint, int> Clear { get; }
}
