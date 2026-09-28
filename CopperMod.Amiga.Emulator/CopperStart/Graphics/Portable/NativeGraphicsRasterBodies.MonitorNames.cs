using System.Collections.Generic;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

internal static partial class NativeGraphicsRasterBodies
{
    // A1=request name, D0=original ID. Null name/default alias falls through to
    // the default-pointer path; a list match branches with D1=selected node.
    // All other inputs and callee-saved registers are restored before either
    // continuation. Caller serializes list/lifetime mutation. No allocation or
    // ownership adoption. Bounded admission:256 nodes,64 name bytes including NUL.
    private static int AppendMonitorNameSelection(List<byte> code, int fallbackEntryOffset)
    {
        var failures = new List<int>();
        var earlyFailures = new List<int>();
        var aliasMisses = new List<int>();
        int Branch(ushort opcode)
        { Append(code, opcode); var at = code.Count; Append(code, 0); return at; }
        void Long(uint value) { Append(code, (ushort)(value >> 16)); Append(code, (ushort)value); }
        void Fail(ushort opcode) => failures.Add(Branch(opcode));
        void RequirePointer(ushort move)
        { Append(code, move); Append(code, TestLongD1); Fail(BranchEqualWord); }

        Append(code, 0xB3FC); Long(0); // CMPA.L #0,A1
        var unnamed = Branch(BranchEqualWord);
        Append(code, MoveLongA6ToD1); Append(code, TestLongD1);
        earlyFailures.Add(Branch(BranchEqualWord));
        Append(code, BitTestImmediateD1); Append(code, 0);
        earlyFailures.Add(Branch(BranchNotEqualWord));
        Append(code, CompareImmediateLongD1); Long(uint.MaxValue - (uint)GraphicsLayouts.GfxBaseMonitorListPad);
        earlyFailures.Add(Branch(BranchHighWord));
        Append(code, MoveMultipleLongRegistersToPreDecrementA7); Append(code, 0xB8B0); // D0/D2-D4/A0/A2-A3,28 bytes
        Append(code, 0x2649); // MOVEA.L A1,A3
        foreach (var character in "default.monitor\0")
        {
            RequirePointer(0x220B);
            Append(code, 0x121B);
            if (character is >= 'a' and <= 'z') { Append(code, 0x0001); Append(code, 0x20); }
            Append(code, 0x0C01); Append(code, (ushort)character);
            aliasMisses.Add(Branch(BranchNotEqualWord));
        }
        var aliasMatched = Branch(BranchAlwaysWord);

        foreach (var miss in aliasMisses) PatchBranch(code, miss, code.Count);
        Append(code, 0x4AAE); Append(code, GraphicsLayouts.GfxBaseMonitorListTail); Fail(BranchNotEqualWord);
        Append(code, 0x4A6E); Append(code, GraphicsLayouts.GfxBaseMonitorListType); Fail(BranchNotEqualWord);
        Append(code, MoveLongA6ToD0); Append(code, 0x0680); Long((uint)GraphicsLayouts.GfxBaseMonitorListTail); // D0=tail sentinel
        Append(code, 0x280E); Append(code, 0x0684); Long((uint)GraphicsLayouts.GfxBaseMonitorList); // D4=previous link
        Append(code, 0x206E); Append(code, GraphicsLayouts.GfxBaseMonitorListHead); // A0=current
        Append(code, 0x343C); Append(code, 256); // budget:256 non-sentinel nodes
        var nodeLoop = code.Count;
        Append(code, MoveLongA0ToD1); Append(code, CompareLongD0ToD1);
        var notTail = Branch(BranchNotEqualWord);
        // A complete, consistent public list proves absence. This is an API
        // NULL result; malformed or bounded-out searches still tail-chain.
        Append(code, 0xB8AE); Append(code, GraphicsLayouts.GfxBaseMonitorListTailPred); Fail(BranchNotEqualWord);
        Append(code, MoveMultipleLongA7PostIncrementToRegisters); Append(code, 0x0D1D);
        Append(code, MoveQuickZeroD0); Append(code, ReturnFromSubroutine);
        PatchBranch(code, notTail, code.Count);
        Append(code, 0x5342); Fail(BranchCarryWord); // SUBQ.W #1,D2; reject257th node before dereferencing
        Append(code, TestLongD1); Fail(BranchEqualWord);
        Append(code, BitTestImmediateD1); Append(code, 0); Fail(BranchNotEqualWord);
        Append(code, CompareImmediateLongD1); Long(uint.MaxValue - (GraphicsLayouts.MonitorSpecSize - 1u)); Fail(BranchHighWord);
        Append(code, 0xB8A8); Append(code, 4); Fail(BranchNotEqualWord); // CMP.L pred(A0),D4
        Append(code, 0x0C28); Append(code, 18); Append(code, GraphicsLayouts.MonitorSpecNodeType); Fail(BranchNotEqualWord);
        Append(code, 0x0C68); Append(code, 0x0204); Append(code, GraphicsLayouts.MonitorSpecNodeSubsystem); Fail(BranchNotEqualWord);
        Append(code, MoveLongA6ToD1); Append(code, 0xB2A8); Append(code, GraphicsLayouts.ExtendedNodeLibrary); Fail(BranchNotEqualWord);
        Append(code, 0x2468); Append(code, GraphicsLayouts.MonitorSpecNodeName);
        Append(code, 0x2649); Append(code, 0x763F); // A3=request,D3=63
        var compare = code.Count;
        RequirePointer(0x220A); RequirePointer(0x220B);
        Append(code, 0x121B); Append(code, 0xB21A); // exact case-sensitive bytes
        var mismatch = Branch(BranchNotEqualWord);
        Append(code, 0x4A01); var exactMatched = Branch(BranchEqualWord);
        var repeat = Branch(0x51CB); PatchBranch(code, repeat, compare); // DBRA D3
        var unterminated = Branch(BranchAlwaysWord); failures.Add(unterminated);
        PatchBranch(code, mismatch, code.Count);
        Append(code, 0x2808); Append(code, 0x2050); // previous=A0; A0=succ(A0)
        var next = Branch(BranchAlwaysWord); PatchBranch(code, next, nodeLoop);
        foreach (var failure in failures) PatchBranch(code, failure, code.Count);
        Append(code, MoveMultipleLongA7PostIncrementToRegisters); Append(code, 0x0D1D);
        var fallback = Branch(BranchAlwaysWord); PatchBranch(code, fallback, fallbackEntryOffset);
        foreach (var early in earlyFailures) PatchBranch(code, early, fallbackEntryOffset);

        PatchBranch(code, exactMatched, code.Count);
        Append(code, MoveLongA0ToD1);
        Append(code, MoveMultipleLongA7PostIncrementToRegisters); Append(code, 0x0D1D);
        var selected = Branch(BranchAlwaysWord); // caller patches to common MonitorSpec admission
        PatchBranch(code, aliasMatched, code.Count);
        Append(code, MoveMultipleLongA7PostIncrementToRegisters); Append(code, 0x0D1D);
        PatchBranch(code, unnamed, code.Count);
        return selected;
    }

    // References to a valid library-associated MonitorSpec remain closeable
    // after unlinking (original-ROM proven). List membership grants no ownership;
    // this only decrements OpenCount, never removes/frees a node or invokes drivers.
    // Unrecognized envelopes retain the legacy default/provider admission path.
    private static int AppendCloseMonitorRegistered(List<byte> code, int defaultEntryOffset)
    {
        var start = code.Count;
        var failures = new List<int>();
        void Fail(ushort opcode) { Append(code, opcode); failures.Add(code.Count); Append(code, 0); }
        void Long(uint value) { Append(code, (ushort)(value >> 16)); Append(code, (ushort)value); }
        Append(code, MoveLongA0ToD1); Append(code, TestLongD1); Fail(BranchEqualWord);
        Append(code, BitTestImmediateD1); Append(code, 0); Fail(BranchNotEqualWord);
        Append(code, CompareImmediateLongD1); Long(uint.MaxValue - (GraphicsLayouts.MonitorSpecSize - 1u)); Fail(BranchHighWord);
        Append(code, MoveLongA6ToD1); Append(code, TestLongD1); Fail(BranchEqualWord);
        Append(code, BitTestImmediateD1); Append(code, 0); Fail(BranchNotEqualWord);
        Append(code, CompareImmediateLongD1); Long(uint.MaxValue - (GraphicsLayouts.GfxBaseDefaultMonitor + 3u)); Fail(BranchHighWord);
        Append(code, 0x0C28); Append(code, 18); Append(code, GraphicsLayouts.MonitorSpecNodeType); Fail(BranchNotEqualWord);
        Append(code, 0x0C68); Append(code, 0x0204); Append(code, GraphicsLayouts.MonitorSpecNodeSubsystem); Fail(BranchNotEqualWord);
        Append(code, 0xB2A8); Append(code, GraphicsLayouts.ExtendedNodeLibrary); Fail(BranchNotEqualWord);
        Append(code, MoveWordA0DisplacementToD1); Append(code, GraphicsLayouts.MonitorSpecOpenCount);
        Append(code, 0x4A41); Fail(BranchEqualWord); // TST.W D1
        Append(code, 0x5341); // SUBQ.W #1,D1
        Append(code, MoveWordD1ToA0Displacement); Append(code, GraphicsLayouts.MonitorSpecOpenCount);
        Append(code, MoveQuickZeroD0); Append(code, ReturnFromSubroutine);
        foreach (var failure in failures) PatchBranch(code, failure, defaultEntryOffset);
        return start;
    }
}
