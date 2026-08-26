using System;

namespace CopperMod.Amiga.CopperStart.Workbench;

/// <summary>Concrete bridge for CopperStart workbench.library compatibility calls.</summary>
internal sealed class CopperStartWorkbenchContext
{
    public CopperStartWorkbenchContext(Action<int> logCall, Func<uint> ensureScreen, Func<uint> ensureHostObject)
    {
        LogCall = logCall ?? throw new ArgumentNullException(nameof(logCall));
        EnsureScreen = ensureScreen ?? throw new ArgumentNullException(nameof(ensureScreen));
        EnsureHostObject = ensureHostObject ?? throw new ArgumentNullException(nameof(ensureHostObject));
    }
    public Action<int> LogCall { get; }
    public Func<uint> EnsureScreen { get; }
    public Func<uint> EnsureHostObject { get; }
}
