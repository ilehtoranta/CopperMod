using System;
using System.Collections.Generic;
using CopperMod.Amiga.Firmware;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>Position-independent 68000 initializer for an Exec-allocated positive image.</summary>
internal static class NativeGraphicsLibraryInitializer
{
    // Private, unpublished allocation only; caller serializes initialization.
    // Publish CMDB before the default MonitorSpec. Failure of the latter releases
    // CMDB before optionally freeing the actual Exec library extent. A declined
    // CMDB release retains the library for recovery rather than dangling its owner.
    // Success does not install a view or implement a complete library Expunge.
    internal static byte[] BuildWithNativeMonitorSpec(int positiveSize,
        GraphicsLibraryImageProfile profile, bool supportsEcsDisplay,
        bool releaseLibraryOnFailure = false,
        bool includeNativeMonitorMutation = false)
    {
        var initializer = Build(positiveSize, profile, includeNativeRuntimeDescriptor: true,
            includeNativeMonitorDescriptor: true);
        var databasePublisher = NativeGraphicsMonitorStatePublisher.Build(supportsEcsDisplay,
            profile == GraphicsLibraryImageProfile.NativeNtsc,
            includeNativeMonitorMutation);
        var monitorPublisher = NativeGraphicsMonitorSpecPublisher.BuildRegistered(
            profile == GraphicsLibraryImageProfile.NativeNtsc,
            includeNativeMonitorMutation);
        var databaseRelease = NativeGraphicsMonitorStateRelease.Build(includeNativeMonitorMutation);
        var code = new List<byte>();
        void Word(ushort value) { code.Add((byte)(value >> 8)); code.Add((byte)value); }
        void Patch(int extension, int target)
        {
            var displacement = checked((short)(target - extension));
            code[extension] = (byte)((ushort)displacement >> 8);
            code[extension + 1] = (byte)displacement;
        }
        int Branch(ushort opcode) { Word(opcode); var extension = code.Count; Word(0); return extension; }

        Word(0x48E7); Word(0x2020); // MOVEM.L D2/A2,-(SP)
        Word(0x2440); // MOVEA.L D0,A2: actual library allocation
        var initialize = Branch(0x6100);
        Word(0x4A80);
        var invalid = Branch(0x6700);
        var publishDatabase = Branch(0x6100);
        Word(0x4A80);
        var failedDatabase = Branch(0x6700);
        var publishMonitor = Branch(0x6100);
        Word(0x4A80);
        var success = Branch(0x6600);
        Word(0x200A); // MOVE.L A2,D0
        var releaseDatabase = Branch(0x6100);
        Word(0x4A80);
        var retainedDatabase = Branch(0x6700);
        Patch(failedDatabase, code.Count);
        if (releaseLibraryOnFailure)
        {
            Word(0x7000); Word(0x302A); Word(0x10); // actual lib_NegSize
            Word(0x2400);
            Word(0x7200); Word(0x322A); Word(0x12); // actual lib_PosSize
            Word(0xD081); Word(0x224A); Word(0x93C2);
            Word(0x4EAE); Word(unchecked((ushort)-210)); // FreeMem(base-neg,neg+pos)
        }
        Word(0x7000);
        Patch(invalid, code.Count);
        Patch(retainedDatabase, code.Count);
        Patch(success, code.Count);
        Word(0x4CDF); Word(0x0404); Word(0x4E75);
        Patch(initialize, code.Count); code.AddRange(initializer);
        Patch(publishDatabase, code.Count); code.AddRange(databasePublisher);
        Patch(publishMonitor, code.Count); code.AddRange(monitorPublisher);
        Patch(releaseDatabase, code.Count); code.AddRange(databaseRelease);
        return code.ToArray();
    }

    // Explicit composition for a fresh native owner. Failure returns zero.
    // AUTOINIT requires manual library-base cleanup after initialization fails:
    // https://developer.amigaos3.net/autodocs/exec.library/InitResident.html
    // Direct callers retain their allocation unless they opt into that contract.
    // This only publishes CMDB, not MonitorSpec or a complete screen lifecycle.
    internal static byte[] BuildWithNativeMonitorState(int positiveSize,
        GraphicsLibraryImageProfile profile, bool supportsEcsDisplay,
        bool releaseLibraryOnFailure = false, bool includeNativeMonitorDescriptor = false,
        bool includeNativeMonitorMutation = false)
    {
        var initializer = Build(positiveSize, profile, includeNativeRuntimeDescriptor: true,
            includeNativeMonitorDescriptor: includeNativeMonitorDescriptor);
        var publisher = NativeGraphicsMonitorStatePublisher.Build(supportsEcsDisplay,
            profile == GraphicsLibraryImageProfile.NativeNtsc,
            includeNativeMonitorMutation);
        var code = new List<byte>();
        void Word(ushort value) { code.Add((byte)(value >> 8)); code.Add((byte)value); }
        void Patch(int extension, int target)
        {
            var displacement = checked((short)(target - extension));
            code[extension] = (byte)((ushort)displacement >> 8);
            code[extension + 1] = (byte)displacement;
        }
        Word(0x48E7); Word(0x2020); // MOVEM.L D2/A2,-(SP)
        Word(0x2440); // MOVEA.L D0,A2: retain actual allocation across both calls
        Word(0x6100); var initialize = code.Count; Word(0);
        Word(0x4A80); // TST.L D0
        Word(0x6700); var invalid = code.Count; Word(0);
        Word(0x6100); var publish = code.Count; Word(0);
        Word(0x4A80);
        Word(0x6600); var success = code.Count; Word(0);
        if (releaseLibraryOnFailure)
        {
            // The positive-image constructor admitted and preserved these
            // actual Exec sizes. Never derive FreeMem's extent from the template.
            // Invalid caller envelopes rejected by that constructor aren't freed.
            Word(0x7000); Word(0x302A); Word(0x10); // D0=zero-extended lib_NegSize
            Word(0x2400); // MOVE.L D0,D2
            Word(0x7200); Word(0x322A); Word(0x12); // D1=zero-extended lib_PosSize
            Word(0xD081); // ADD.L D1,D0
            Word(0x224A); // MOVEA.L A2,A1
            Word(0x93C2); // SUBA.L D2,A1
            Word(0x4EAE); Word(unchecked((ushort)-210)); // FreeMem(base-neg,neg+pos)
            Word(0x7000);
        }
        Patch(invalid, code.Count);
        Patch(success, code.Count);
        Word(0x4CDF); Word(0x0404); // MOVEM.L (SP)+,D2/A2
        Word(0x4E75);
        Patch(initialize, code.Count);
        code.AddRange(initializer);
        Patch(publish, code.Count);
        code.AddRange(publisher);
        return code.ToArray();
    }

    // Exec supplies D0=allocated base, A0=segment list, A6=ExecBase. Return D0
    // unchanged on success or zero on admission failure. Only D1/A0/A1 change.
    // The descriptor stays inert: this constructor does not claim a CMDB.
    internal static byte[] Build(int positiveSize, GraphicsLibraryImageProfile profile,
        bool includeNativeRuntimeDescriptor = false, bool includeNativeMonitorDescriptor = false)
    {
        var template = GraphicsLibraryImageLayout.CreateGuestImage(0, positiveSize,
            NativeGraphicsLibraryImageBuilder.VectorTableSize, positiveSize, 40, 68,
            "graphics.library", "graphics.library 40.68", profile, includeNativeRuntimeDescriptor,
            includeNativeMonitorDescriptor);
        var code = new List<byte>();
        var failures = new List<int>();
        void Word(ushort value) { code.Add((byte)(value >> 8)); code.Add((byte)value); }
        void Long(uint value) { Word((ushort)(value >> 16)); Word((ushort)value); }
        void Fail(ushort branch) { Word(branch); failures.Add(code.Count); Word(0); }
        void Patch(int extension, int target)
        {
            var displacement = checked((short)(target - extension));
            code[extension] = (byte)((ushort)displacement >> 8);
            code[extension + 1] = (byte)displacement;
        }
        Word(0x4A80); // TST.L D0
        Fail(0x6700); // BEQ
        Word(0x0800); Word(0); // BTST #0,D0
        Fail(0x6600); // BNE
        Word(0x0C80); Long(uint.MaxValue - ((uint)positiveSize - 1)); // CMPI.L #last-base,D0
        Fail(0x6200); // BHI
        Word(0x2040); // MOVEA.L D0,A0
        Word(0x0C68); Word((ushort)positiveSize); Word(0x12); // CMPI.W #size,lib_PosSize(A0)
        Fail(0x6500); // BCS
        Word(0x2F28); Word(0x10); // MOVE.L lib_NegSize(A0),-(SP): preserve both Exec sizes
        Word(0x43FA); // LEA template(PC),A1
        var templateExtension = code.Count;
        Word(0);
        Word(0x323C); Word(checked((ushort)(positiveSize - 1))); // MOVE.W #byte-count-1,D1
        var copy = code.Count;
        Word(0x10D9); // MOVE.B (A1)+,(A0)+
        Word(0x51C9); // DBRA D1,copy
        var loopExtension = code.Count;
        Word(0);
        Patch(loopExtension, copy);
        Word(0x2040); // MOVEA.L D0,A0
        Word(0x215F); Word(0x10); // MOVE.L (SP)+,lib_NegSize(A0)
        foreach (var offset in new[]
        {
            0x0A, 0x18, GraphicsLibraryImageLayout.GfxBaseTextFontsHead,
            GraphicsLibraryImageLayout.GfxBaseTextFontsTailPred
        })
        {
            Word(0xD1A8); Word((ushort)offset); // ADD.L D0,self-relative pointer(A0)
        }
        if (positiveSize >= GraphicsLibraryImageLayout.GfxBaseNativeSize)
            foreach (var offset in new[]
            {
                GraphicsLibraryImageLayout.GfxBaseMonitorListHead,
                GraphicsLibraryImageLayout.GfxBaseMonitorListTailPred
            })
            {
                Word(0xD1A8); Word((ushort)offset);
            }
        Word(0x4E75); // RTS
        foreach (var failure in failures) Patch(failure, code.Count);
        Word(0x7000); Word(0x4E75); // MOVEQ #0,D0; RTS
        Patch(templateExtension, code.Count);
        code.AddRange(template);
        if ((code.Count & 1) != 0) code.Add(0);
        return code.ToArray();
    }
}
