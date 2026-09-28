using System.Collections.Generic;
using CopperMod.Amiga.Firmware;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

internal static partial class NativeGraphicsRasterBodies
{
    // Called inside the public MOVEM frame, before any destination writes.
    // D0 receives current Point; optional D5 receives original. D3/A2 are scratch.
    // A0 retains MonitorSpec without
    // dereferencing it. Invalid owned state returns the original request count.
    private static void AppendOwnedMonitorPoint(List<byte> code, bool ntsc, bool includeOriginal = false)
    {
        var decline = new List<int>();
        var boot = new List<int>();
        void Long(uint value)
        {
            Append(code, (ushort)(value >> 16));
            Append(code, (ushort)value);
        }
        void Branch(ushort opcode, List<int> targets)
        {
            Append(code, opcode);
            targets.Add(code.Count);
            Append(code, 0);
        }
        void CompareField(ushort opcode, int offset, uint value)
        {
            Append(code, opcode);
            Long(value);
            Append(code, checked((ushort)offset));
            Branch(BranchNotEqualWord, decline);
        }

        Append(code, 0x7600); // MOVEQ #0,D3
        Append(code, 0x362E); // MOVE.W lib_PosSize(A6),D3
        Append(code, 0x0012);
        Append(code, 0x0C43); // CMPI.W #runtime-size,D3
        Append(code, GraphicsLibraryImageLayout.NativeRuntimeImageSize);
        Branch(BranchCarryWord, boot);
        Append(code, 0x260E); // MOVE.L A6,D3
        Append(code, 0x0C83); // CMPI.L #last-nonwrapping-base,D3
        Long(uint.MaxValue - (GraphicsLibraryImageLayout.NativeRuntimeImageSize - 1u));
        Branch(BranchHighWord, boot);
        // Size alone is not opt-in. Legacy callers may expose arbitrary bytes
        // beyond the public fields. Version1 or CMDO identifies our descriptor;
        // without either marker retain the legacy boot-only publisher.
        Append(code, 0x262E); // MOVE.L version(A6),D3
        Append(code, GraphicsLibraryImageLayout.NativeRuntimeDescriptorVersion);
        Append(code, 0x0C83); // CMPI.L #1,D3
        Long(1);
        Append(code, BranchEqualWord);
        var enabledVersion = code.Count;
        Append(code, 0);
        Append(code, 0x0CAE); // CMPI.L #CMDO,tag(A6)
        Long(GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag);
        Append(code, GraphicsLibraryImageLayout.NativeRuntimeDescriptorTag);
        Branch(BranchEqualWord, decline);
        Branch(BranchAlwaysWord, boot);
        PatchBranch(code, enabledVersion, code.Count);
        CompareField(0x0CAE, GraphicsLibraryImageLayout.NativeRuntimeDescriptorTag,
            GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag);
        Append(code, 0x260E); // MOVE.L A6,D3
        Append(code, 0xB6AE); // CMP.L owner(A6),D3
        Append(code, GraphicsLibraryImageLayout.NativeRuntimeDescriptorOwner);
        Branch(BranchNotEqualWord, decline);
        CompareField(0x0CAE, GraphicsLibraryImageLayout.NativeRuntimeDescriptorDatabaseSize,
            (uint)GraphicsDisplayDatabase.NativeDatabaseSize);
        Append(code, 0x262E); // MOVE.L owned database(A6),D3
        Append(code, GraphicsLibraryImageLayout.NativeRuntimeDescriptorDatabase);
        Append(code, 0xB6AE); // CMP.L public database(A6),D3 -- before dereference
        Append(code, checked((ushort)GraphicsLayouts.GfxBaseDisplayInfoDataBase));
        Branch(BranchNotEqualWord, decline);
        Append(code, 0x4A83); // TST.L D3
        Branch(BranchEqualWord, decline);
        Append(code, 0x0803); // BTST #0,D3
        Append(code, 0);
        Branch(BranchNotEqualWord, decline);
        Append(code, 0x0C83);
        Long(uint.MaxValue - ((uint)GraphicsDisplayDatabase.NativeDatabaseSize - 1));
        Branch(BranchHighWord, decline);
        Append(code, 0x2443); // MOVEA.L D3,A2
        CompareField(0x0CAA, 0, GraphicsDisplayDatabase.NativeDatabaseMagic);
        CompareField(0x0CAA, 4, GraphicsDisplayDatabase.NativeDatabaseVersion);
        CompareField(0x0CAA, 8, (uint)GraphicsDisplayDatabase.NativeDatabaseSize);
        var record = GraphicsDisplayDatabase.NativeMonitorPositionsOffset +
            (ntsc ? 0 : GraphicsDisplayDatabase.NativeMonitorPositionRecordSize);
        CompareField(0x0CAA, record, ntsc ? 0x11000u : 0x21000u);
        Append(code, 0x202A); // MOVE.L currentPoint(A2),D0
        Append(code, checked((ushort)(record + 4)));
        if (includeOriginal)
        {
            Append(code, 0x2A2A); // MOVE.L originalPoint(A2),D5, before any output
            Append(code, checked((ushort)(record + 8)));
        }
        Append(code, BranchAlwaysWord);
        var success = code.Count;
        Append(code, 0);

        foreach (var target in decline)
            PatchBranch(code, target, code.Count);
        Append(code, MoveLongA7DisplacementToD0);
        Append(code, 0x002C);
        Append(code, MoveMultipleLongA7PostIncrementToRegisters);
        Append(code, 0x7CFC);
        Append(code, AddQuickFourLongA7);
        Append(code, ReturnFromSubroutine);

        foreach (var target in boot)
            PatchBranch(code, target, code.Count);
        Append(code, MoveLongImmediateD0);
        Long(GraphicsMonitorViewPosition.BootDefault.WordPair);
        if (includeOriginal)
        {
            Append(code, 0x2A3C); // MOVE.L #boot-original,D5
            Long(GraphicsMonitorViewPosition.BootDefault.WordPair);
        }
        PatchBranch(code, success, code.Count);
    }
}
