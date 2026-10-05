/* Copyright (C) 2026 Ilkka Lehtoranta. SPDX-License-Identifier: MIT */
using System.Collections.Generic;

namespace Copper68k;

// Exception delivery state belongs to the integer pipeline, not the live FPSR
// or an FSAVE image. Preserve suspended delivery while a handler uses the FPU.
internal sealed class M68040PendingFpuExceptions
{
    internal sealed record Entry(int Format, int Vector, uint ProgramCounter);
    private List<Entry>? _entries;

    internal Entry Begin(int format, int vector, uint pc)
    {
        var entry = new Entry(format, vector, pc);
        (_entries ??= []).Add(entry);
        return entry;
    }

    internal Entry? Find(int format, uint? pc = null)
    {
        if (_entries == null) return null;
        for (var n = _entries.Count - 1; n >= 0; n--)
            if (_entries[n].Format == format && (!pc.HasValue || _entries[n].ProgramCounter == pc.Value)) return _entries[n];
        return null;
    }

    internal void Complete(Entry? entry)
    {
        if (entry == null || _entries == null) return;
        // Equal-valued nested entries still have distinct delivery ownership.
        for (var n = _entries.Count - 1; n >= 0; n--)
            if (ReferenceEquals(_entries[n], entry)) { _entries.RemoveAt(n); return; }
    }

    internal void Reset() => _entries?.Clear();
}
