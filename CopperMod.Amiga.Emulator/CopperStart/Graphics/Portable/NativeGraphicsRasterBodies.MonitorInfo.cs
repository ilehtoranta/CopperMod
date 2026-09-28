using System.Collections.Generic;
using CopperMod.Amiga.Firmware;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

internal static partial class NativeGraphicsRasterBodies
{
    // Enter after the universal INVALID_ID sentinel. Non-MNTR and compact
    // legacy images fall through unchanged. Registered records never acquire
    // a MonitorSpec reference or dereference their raw Mspc value.
    private static void AppendRegisteredMonitorInfo(
        List<byte> code,
        int fallbackEntryOffset,
        bool enableNativeMonitorMutation = false,
        bool defaultMonitorNtsc = false)
    {
        void Long(uint value) { Append(code, (ushort)(value >> 16)); Append(code, (ushort)value); }
        int Branch(ushort opcode) { Append(code, opcode); var at = code.Count; Append(code, 0); return at; }
        void Jump(ushort opcode, int target) => PatchBranch(code, Branch(opcode), target);
        void Compare(int opcode, int displacement, uint expected, int failure)
        {
            Append(code, (ushort)opcode); Long(expected); Append(code, checked((ushort)displacement));
            Jump(BranchNotEqualWord, failure);
        }
        void RestoreAll() { Append(code, MoveMultipleLongA7PostIncrementToRegisters); Append(code, 0x7FFF); }

        Append(code, CompareImmediateLongD1); Long(GraphicsDisplayDatabase.DtagMntr);
        var otherTag = Branch(BranchNotEqualWord);
        Append(code, MoveMultipleLongRegistersToPreDecrementA7); Append(code, 0xFFFE); // D0-D7/A0-A6,60 bytes
        var begin = Branch(BranchAlwaysWord);
        var provider = code.Count;
        RestoreAll(); Jump(BranchAlwaysWord, fallbackEntryOffset);
        var invalid = code.Count;
        RestoreAll(); Append(code, ReturnFromSubroutine); // native decline, original requested count
        var legacy = code.Count;
        RestoreAll(); var legacyToEnd = Branch(BranchAlwaysWord);
        var empty = code.Count;
        RestoreAll(); Append(code, MoveQuickZeroD0); Append(code, ReturnFromSubroutine);
        PatchBranch(code, begin, code.Count);

        Append(code, 0x2E00); // MOVE.L D0,D7: requested count
        Append(code, MoveLongA0ToD0); Append(code, TestLongD0);
        var hasHandle = Branch(BranchNotEqualWord);
        Append(code, MoveLongD2ToD0);
        var resolved = Branch(BranchAlwaysWord);
        PatchBranch(code, hasHandle, code.Count);
        Append(code, CompareImmediateLongD0); Long(GraphicsDisplayDatabase.DefaultModeHandle);
        var ordinaryHandle = Branch(BranchNotEqualWord);
        Append(code, MoveQuickZeroD0);
        PatchBranch(code, resolved, code.Count); PatchBranch(code, ordinaryHandle, code.Count);
        AppendMonitorIdSyntaxAdmission(code, provider);
        Append(code, 0x2C00); // D6 = authoritative selector, including default-marker alias
        Append(code, 0x4A87); // TST.L D7
        Jump(BranchEqualWord, empty);
        Append(code, CompareAddressImmediateLongA1); Long(0);
        Jump(BranchEqualWord, empty);
        Append(code, CompareImmediateLongD7); Long(GraphicsMonitorInfoImage.TransferSize);
        var bounded = Branch(BranchLowerOrSameWord);
        Append(code, 0x7E58); // MOVEQ #88,D7
        PatchBranch(code, bounded, code.Count);
        Append(code, MoveLongA1ToD0); Append(code, MoveLongD7ToD1);
        Append(code, SubQuickOneLongD1); Append(code, AddLongD1ToD0);
        Jump(BranchCarryWord, invalid); // before any GfxBase or destination access

        // A missing/unaligned/nonwrapping-inadmissible base remains the legacy
        // publisher's responsibility, including its A6-independent headers.
        Append(code, MoveLongA6ToD0); Append(code, TestLongD0); Jump(BranchEqualWord, legacy);
        Append(code, BitTestImmediateD0); Append(code, 0); Jump(BranchNotEqualWord, legacy);
        Append(code, CompareImmediateLongD0); Long(uint.MaxValue - (GraphicsLibraryImageLayout.NativeRuntimeImageSize - 1u));
        Jump(BranchHighWord, legacy);
        Append(code, 0x7A00); Append(code, 0x3A2E); Append(code, 0x12); // D5 = lib_PosSize
        Append(code, 0x202E); Append(code, GraphicsLibraryImageLayout.NativeRuntimeDescriptorVersion);
        Append(code, CompareImmediateLongD0); Long(1);
        var markedVersion = Branch(BranchEqualWord);
        Append(code, 0x202E); Append(code, GraphicsLibraryImageLayout.NativeRuntimeDescriptorTag);
        Append(code, CompareImmediateLongD0); Long(GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag);
        var markedTag = Branch(BranchEqualWord);
        Append(code, 0x0C45); Append(code, GraphicsLibraryImageLayout.NativeRuntimeImageSize);
        Jump(BranchCarryWord, legacy);
        Append(code, TestLongD0); Jump(BranchNotEqualWord, invalid);
        Append(code, 0x4AAE); Append(code, GraphicsLibraryImageLayout.NativeRuntimeDescriptorVersion);
        Jump(BranchNotEqualWord, invalid);
        Jump(BranchAlwaysWord, legacy);
        PatchBranch(code, markedVersion, code.Count); PatchBranch(code, markedTag, code.Count);
        Append(code, 0x0C45); Append(code, GraphicsLibraryImageLayout.NativeRuntimeImageSize);
        Jump(BranchCarryWord, invalid);
        Compare(0x0CAE, GraphicsLibraryImageLayout.NativeRuntimeDescriptorTag, GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag, invalid);
        Compare(0x0CAE, GraphicsLibraryImageLayout.NativeRuntimeDescriptorVersion, 1, invalid);
        Append(code, MoveLongA6ToD0); Append(code, 0xB0AE); Append(code, GraphicsLibraryImageLayout.NativeRuntimeDescriptorOwner);
        Jump(BranchNotEqualWord, invalid);
        var nativeDatabaseSize = enableNativeMonitorMutation
            ? GraphicsDisplayDatabase.NativeDatabaseMutationSize
            : GraphicsDisplayDatabase.NativeDatabaseSize;
        Compare(0x0CAE, GraphicsLibraryImageLayout.NativeRuntimeDescriptorDatabaseSize,
            (uint)nativeDatabaseSize, invalid);
        Append(code, 0x202E); Append(code, GraphicsLibraryImageLayout.NativeRuntimeDescriptorDatabase);
        Append(code, 0xB0AE); Append(code, (ushort)GraphicsLayouts.GfxBaseDisplayInfoDataBase);
        Jump(BranchNotEqualWord, invalid);
        Append(code, TestLongD0); Jump(BranchEqualWord, invalid);
        Append(code, BitTestImmediateD0); Append(code, 0); Jump(BranchNotEqualWord, invalid);
        Append(code, BitTestImmediateD0); Append(code, 1); Jump(BranchNotEqualWord, invalid);
        Append(code, CompareImmediateLongD0); Long(uint.MaxValue - ((uint)nativeDatabaseSize - 1));
        Jump(BranchHighWord, invalid);
        Append(code, 0x2440); // MOVEA.L D0,A2: complete aligned CMDB span
        foreach (var (field, value) in new (int, uint)[] {
            (0, GraphicsDisplayDatabase.NativeDatabaseMagic), (4, GraphicsDisplayDatabase.NativeDatabaseVersion),
            (8, (uint)nativeDatabaseSize),
            (GraphicsDisplayDatabase.NativeMonitorPositionsOffset, GraphicsModeIds.NtscMonitor),
            (GraphicsDisplayDatabase.NativeMonitorPositionsOffset + GraphicsDisplayDatabase.NativeMonitorPositionRecordSize, GraphicsModeIds.PalMonitor) })
            Compare(0x0CAA, field, value, invalid);
        Append(code, 0x242A); Append(code, (ushort)GraphicsDisplayDatabase.NativeDefaultMonitorIdOffset);
        Append(code, CompareImmediateLongD2); Long(GraphicsModeIds.NtscMonitor);
        var validDefault = Branch(BranchEqualWord);
        Append(code, CompareImmediateLongD2); Long(GraphicsModeIds.PalMonitor); Jump(BranchNotEqualWord, invalid);
        PatchBranch(code, validDefault, code.Count);
        Append(code, 0x2006); Append(code, AndImmediateLongD0); Long(0xFFFF0000);
        var defaultFamily = Branch(BranchEqualWord);
        Append(code, MoveLongD0ToD2); Append(code, 0x0082); Long(0x1000);
        PatchBranch(code, defaultFamily, code.Count);
        Append(code, CompareImmediateLongD2); Long(GraphicsModeIds.NtscMonitor);
        var ntscCells = Branch(BranchEqualWord);
        Cells(1);
        var cellsReady = Branch(BranchAlwaysWord);
        PatchBranch(code, ntscCells, code.Count); Cells(0);
        PatchBranch(code, cellsReady, code.Count);

        // A mutation-enabled database redirects its family registration cell
        // to one of the private 88-byte records appended to CMDB3. Keep this
        // marker in D5 until the image arm below; the normal path remains
        // byte-for-byte identical and still emits the canonical record.
        if (enableNativeMonitorMutation)
        {
            Append(code, 0x2C13); // MOVE.L (A3),D6: raw registration pointer
            Append(code, MoveQuickZeroD5);
            Append(code, CompareImmediateLongD2); Long(GraphicsModeIds.NtscMonitor);
            var palCheck = Branch(BranchNotEqualWord);
            Append(code, 0x41EA);
            Append(code, checked((ushort)GraphicsDisplayDatabase.NativeMonitorMutationRecordsOffset));
            Append(code, MoveLongA0ToD0);
            Append(code, CompareLongD6ToD0);
            var ntscNoMatch = Branch(BranchNotEqualWord);
            Append(code, MoveQuickOneD5);
            var mutationMarkerDone = Branch(BranchAlwaysWord);
            var ntscCheck = code.Count;
            PatchBranch(code, ntscNoMatch, ntscCheck);
            PatchBranch(code, palCheck, ntscCheck);
            Append(code, 0x41EA);
            Append(code, checked((ushort)(GraphicsDisplayDatabase.NativeMonitorMutationRecordsOffset +
                GraphicsDisplayDatabase.NativeMonitorMutationRecordSize)));
            Append(code, MoveLongA0ToD0);
            Append(code, CompareLongD6ToD0);
            var palNoMatch = Branch(BranchNotEqualWord);
            Append(code, MoveQuickOneD5);
            PatchBranch(code, palNoMatch, code.Count);
            PatchBranch(code, mutationMarkerDone, code.Count);
        }

        Append(code, MoveQuickZeroD0); Append(code, MoveAddressD0ToA0);
        Append(code, 0x263C); Long(GraphicsMonitorViewPosition.BootDefault.WordPair); // current D3
        Append(code, 0x283C); Long(GraphicsMonitorViewPosition.BootDefault.WordPair); // original D4
        Append(code, CompareImmediateLongD7); Long(16);
        var noPointer = Branch(BranchLowerOrSameWord);
        Append(code, 0x2053); // MOVEA.L (A3),A0: raw Mspc, no null/alignment/node check
        PatchBranch(code, noPointer, code.Count);
        Append(code, CompareImmediateLongD7); Long(20);
        var noCurrent = Branch(BranchLowerOrSameWord);
        Append(code, 0x2614); // MOVE.L (A4),D3
        PatchBranch(code, noCurrent, code.Count);
        Append(code, CompareImmediateLongD7); Long(80);
        var noOriginal = Branch(BranchLowerOrSameWord);
        Append(code, 0x282C); Append(code, 4); // MOVE.L original(A4),D4
        PatchBranch(code, noOriginal, code.Count);

        // Construct a private88-byte image before touching even overlapping
        // output. Static bytes and all dynamic fields use guest big-endian order.
        int? mutableImageBranch = null;
        if (enableNativeMonitorMutation)
        {
            Append(code, TestLongD5);
            mutableImageBranch = Branch(BranchNotEqualWord);
        }

        Append(code, CompareImmediateLongD2); Long(GraphicsModeIds.NtscMonitor);
        var ntscImage = Branch(BranchEqualWord);
        Image(true);
        var imageReady = Branch(BranchAlwaysWord);
        PatchBranch(code, ntscImage, code.Count); Image(false);
        var ntscImageReady = enableNativeMonitorMutation
            ? Branch(BranchAlwaysWord)
            : -1;
        if (enableNativeMonitorMutation)
        {
            var mutableImage = code.Count;
            PatchBranch(code, mutableImageBranch!.Value, mutableImage);
            ImageFromDatabase();
            PatchBranch(code, imageReady, code.Count);
            PatchBranch(code, ntscImageReady, code.Count);
        }
        else
        {
            PatchBranch(code, imageReady, code.Count);
        }
        Append(code, MoveAddressA7ToA2); Append(code, MoveAddressA1ToA3);
        Append(code, 0x2C07); // D6 = result count
        Append(code, SubQuickOneWordD7);
        var copy = code.Count;
        Append(code, MoveByteA2PostIncrementToD0); Append(code, MoveByteD0ToA3PostIncrement);
        Append(code, DecrementBranchD7); var copyBranch = code.Count; Append(code, 0); PatchBranch(code, copyBranch, copy);
        Append(code, LoadEffectiveAddressA7Displacement); Append(code, GraphicsMonitorInfoImage.TransferSize);
        Append(code, 0x2006); // result count
        Append(code, AddQuickFourLongA7); // discard saved original D0 only
        Append(code, MoveMultipleLongA7PostIncrementToRegisters); Append(code, 0x7FFE); // D1-D7/A0-A6
        Append(code, ReturnFromSubroutine);
        PatchBranch(code, otherTag, code.Count); PatchBranch(code, legacyToEnd, code.Count);

        void Cells(int index)
        {
            Append(code, 0x47EA); Append(code, (ushort)(GraphicsDisplayDatabase.NativeMonitorRegistrationsOffset + index * 4));
            Append(code, 0x49EA); Append(code, (ushort)(GraphicsDisplayDatabase.NativeMonitorPositionsOffset + index * GraphicsDisplayDatabase.NativeMonitorPositionRecordSize + 4));
        }
        void Image(bool pal)
        {
            var bytes = GraphicsMonitorInfoImage.Create(pal, 0, default, default);
            for (var field = GraphicsMonitorInfoImage.TransferSize - 4; field >= 0; field -= 4)
            {
                if (field == 0x10) { Append(code, 0x2F08); continue; }
                if (field == 0x14) { Append(code, 0x2F03); continue; }
                if (field == 0x50) { Append(code, 0x2F04); continue; }
                Append(code, 0x2F3C);
                Long(((uint)bytes[field] << 24) | ((uint)bytes[field + 1] << 16) |
                    ((uint)bytes[field + 2] << 8) | bytes[field + 3]);
            }
        }

        void ImageFromDatabase()
        {
            // Re-select the private record from the already admitted CMDB
            // family. Fields are pushed in reverse order so the common copy
            // arm sees an ascending 88-byte image at A7.
            Append(code, CompareImmediateLongD2); Long(GraphicsModeIds.NtscMonitor);
            var palRecord = Branch(BranchNotEqualWord);
            Append(code, 0x41EA);
            Append(code, checked((ushort)GraphicsDisplayDatabase.NativeMonitorMutationRecordsOffset));
            var recordReady = Branch(BranchAlwaysWord);
            PatchBranch(code, palRecord, code.Count);
            Append(code, 0x41EA);
            Append(code, checked((ushort)(GraphicsDisplayDatabase.NativeMonitorMutationRecordsOffset +
                GraphicsDisplayDatabase.NativeMonitorMutationRecordSize)));
            PatchBranch(code, recordReady, code.Count);
            Append(code, 0x2013); // MOVE.L (A3),D0: published private record
            Append(code, MoveAddressD0ToA2);
            for (var field = GraphicsMonitorInfoImage.TransferSize - 4; field >= 0; field -= 4)
            {
                Append(code, MoveLongA2DisplacementToD0);
                Append(code, checked((ushort)field));
                Append(code, MoveLongD0ToPreDecrementA7);
            }
        }
    }
}
