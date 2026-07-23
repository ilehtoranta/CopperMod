using System;

namespace CopperMod.Amiga.CopperStart.Expansion;

/// <summary>Reset-scoped bridge for expansion.library compatibility services.</summary>
internal sealed class CopperStartExpansionContext
{
    public CopperStartExpansionContext(Action<int> logCall, Func<uint> ensureCompatibilityHostObject)
    {
        LogCall = logCall ?? throw new ArgumentNullException(nameof(logCall));
        EnsureCompatibilityHostObject = ensureCompatibilityHostObject ?? throw new ArgumentNullException(nameof(ensureCompatibilityHostObject));
    }

    public Action<int> LogCall { get; }
    public Func<uint> EnsureCompatibilityHostObject { get; }
}
