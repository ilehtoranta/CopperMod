using System.Collections.Generic;
using CopperMod.Amiga.Firmware;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

internal static partial class NativeGraphicsRasterBodies
{
    // Null-name requests only. Preserve D0 for any provider decline. Chipset
    // execution capability does not constrain OpenMonitor's ID namespace.
    private static void AppendMonitorIdSyntaxAdmission(List<byte> code, int fallbackEntryOffset)
    {
        void Mask(uint mask)
        {
            Append(code, MoveLongD0ToD1); Append(code, AndImmediateLongD1);
            Append(code, (ushort)(mask >> 16)); Append(code, (ushort)mask);
        }
        void AdmitValues(IEnumerable<uint> values)
        {
            var matches = new List<int>();
            foreach (var value in values)
            {
                Append(code, CompareImmediateLongD1); Append(code, (ushort)(value >> 16)); Append(code, (ushort)value);
                Append(code, BranchEqualWord); matches.Add(code.Count); Append(code, 0);
            }
            Append(code, BranchAlwaysWord); var decline = code.Count; Append(code, 0);
            PatchBranch(code, decline, fallbackEntryOffset);
            foreach (var match in matches) PatchBranch(code, match, code.Count);
        }
        Mask(0xFFFF1000);
        AdmitValues(new uint[] { 0, 0x1000, GraphicsModeIds.NtscMonitor, GraphicsModeIds.PalMonitor });
        Mask(0xEFFF);
        var keys = new List<uint>();
        foreach (var key in GraphicsMonitorSelectors.ModeKeys) keys.Add(key);
        AdmitValues(keys);
    }

    // Syntax-admitted null-name requests only. Exact ID0 falls through to the
    // public default pointer; every other ID reads CMDB3, independently of CMMO,
    // DefaultMonitor, list membership, node name and timing flags. Return a
    // branch with D1=selected node; absent registration returns NULL directly.
    private static int AppendRegisteredMonitorIdSelection(List<byte> code, int fallbackEntryOffset)
    {
        int Branch(ushort opcode)
        { Append(code, opcode); var at = code.Count; Append(code, 0); return at; }
        void Long(uint value) { Append(code, (ushort)(value >> 16)); Append(code, (ushort)value); }
        var failures = new List<int>();
        var earlyFailures = new List<int>();
        void Fail(ushort opcode) => failures.Add(Branch(opcode));
        void CompareField(int offset, uint value)
        { Append(code, 0x0CAE); Long(value); Append(code, (ushort)offset); Fail(BranchNotEqualWord); }

        Append(code, TestLongD0);
        var zero = Branch(BranchEqualWord);
        Append(code, MoveLongA6ToD1); Append(code, TestLongD1);
        earlyFailures.Add(Branch(BranchEqualWord));
        Append(code, BitTestImmediateD1); Append(code, 0);
        earlyFailures.Add(Branch(BranchNotEqualWord));
        Append(code, CompareImmediateLongD1); Long(uint.MaxValue - (GraphicsLibraryImageLayout.NativeRuntimeImageSize - 1u));
        earlyFailures.Add(Branch(BranchHighWord));
        Append(code, MoveMultipleLongRegistersToPreDecrementA7); Append(code, 0x8080); // D0/A0,8 bytes
        Append(code, 0x0C6E); Append(code, GraphicsLibraryImageLayout.NativeRuntimeImageSize); Append(code, 0x12);
        Fail(BranchCarryWord);
        CompareField(GraphicsLibraryImageLayout.NativeRuntimeDescriptorTag, GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag);
        CompareField(GraphicsLibraryImageLayout.NativeRuntimeDescriptorVersion, 1);
        Append(code, 0xB2AE); Append(code, GraphicsLibraryImageLayout.NativeRuntimeDescriptorOwner); Fail(BranchNotEqualWord);
        CompareField(GraphicsLibraryImageLayout.NativeRuntimeDescriptorDatabaseSize, (uint)GraphicsDisplayDatabase.NativeDatabaseSize);
        Append(code, 0x222E); Append(code, GraphicsLibraryImageLayout.NativeRuntimeDescriptorDatabase);
        Append(code, 0xB2AE); Append(code, (ushort)GraphicsLayouts.GfxBaseDisplayInfoDataBase); Fail(BranchNotEqualWord);
        Append(code, TestLongD1); Fail(BranchEqualWord);
        Append(code, BitTestImmediateD1); Append(code, 0); Fail(BranchNotEqualWord);
        Append(code, BitTestImmediateD1); Append(code, 1); Fail(BranchNotEqualWord);
        Append(code, CompareImmediateLongD1); Long(uint.MaxValue - ((uint)GraphicsDisplayDatabase.NativeDatabaseSize - 1)); Fail(BranchHighWord);
        Append(code, 0x2041); // MOVEA.L D1,A0: admitted owned CMDB
        foreach (var (field, value) in new (int, uint)[]
        {
            (0, GraphicsDisplayDatabase.NativeDatabaseMagic), (4, GraphicsDisplayDatabase.NativeDatabaseVersion),
            (8, (uint)GraphicsDisplayDatabase.NativeDatabaseSize),
            (GraphicsDisplayDatabase.NativeMonitorPositionsOffset, GraphicsModeIds.NtscMonitor),
            (GraphicsDisplayDatabase.NativeMonitorPositionsOffset + GraphicsDisplayDatabase.NativeMonitorPositionRecordSize, GraphicsModeIds.PalMonitor)
        })
        { Append(code, 0x0CA8); Long(value); Append(code, (ushort)field); Fail(BranchNotEqualWord); }
        Append(code, 0x2228); Append(code, (ushort)GraphicsDisplayDatabase.NativeDefaultMonitorIdOffset);
        Append(code, CompareImmediateLongD1); Long(GraphicsModeIds.NtscMonitor);
        var validDefault = Branch(BranchEqualWord);
        Append(code, CompareImmediateLongD1); Long(GraphicsModeIds.PalMonitor); Fail(BranchNotEqualWord);
        PatchBranch(code, validDefault, code.Count);
        // D1 retains the stored default family when the requested high word is0.
        Append(code, 0x0280); Long(0xFFFF0000);
        var defaultFamily = Branch(BranchEqualWord);
        Append(code, MoveLongD0ToD1);
        Append(code, 0x0081); Long(0x1000); // syntax already required the explicit marker
        PatchBranch(code, defaultFamily, code.Count);
        Append(code, CompareImmediateLongD1); Long(GraphicsModeIds.NtscMonitor);
        var ntsc = Branch(BranchEqualWord);
        Append(code, 0x2228); Append(code, (ushort)(GraphicsDisplayDatabase.NativeMonitorRegistrationsOffset + 4));
        var selected = Branch(BranchAlwaysWord);
        PatchBranch(code, ntsc, code.Count);
        Append(code, 0x2228); Append(code, (ushort)GraphicsDisplayDatabase.NativeMonitorRegistrationsOffset);
        PatchBranch(code, selected, code.Count);
        Append(code, TestLongD1);
        var absent = Branch(BranchEqualWord);
        Append(code, BitTestImmediateD1); Append(code, 0); Fail(BranchNotEqualWord);
        Append(code, CompareImmediateLongD1); Long(uint.MaxValue - (uint)(GraphicsLayouts.MonitorSpecSize - 1)); Fail(BranchHighWord);
        Append(code, 0x2041); // MOVEA.L D1,A0
        Append(code, 0x0C28); Append(code, 18); Append(code, GraphicsLayouts.MonitorSpecNodeType); Fail(BranchNotEqualWord);
        Append(code, 0x0C68); Append(code, 0x0204); Append(code, GraphicsLayouts.MonitorSpecNodeSubsystem); Fail(BranchNotEqualWord);
        Append(code, MoveLongA6ToD0);
        Append(code, 0xB0A8); Append(code, GraphicsLayouts.ExtendedNodeLibrary); Fail(BranchNotEqualWord);
        Append(code, MoveMultipleLongA7PostIncrementToRegisters); Append(code, 0x0101);
        var matched = Branch(BranchAlwaysWord); // caller patches to shared selected-pointer admission
        PatchBranch(code, absent, code.Count);
        Append(code, MoveMultipleLongA7PostIncrementToRegisters); Append(code, 0x0101);
        Append(code, 0x7000); Append(code, ReturnFromSubroutine);
        foreach (var failure in failures) PatchBranch(code, failure, code.Count);
        Append(code, MoveMultipleLongA7PostIncrementToRegisters); Append(code, 0x0101);
        foreach (var failure in earlyFailures) PatchBranch(code, failure, code.Count);
        var fallback = Branch(BranchAlwaysWord); PatchBranch(code, fallback, fallbackEntryOffset);
        PatchBranch(code, zero, code.Count);
        return matched;
    }
}
