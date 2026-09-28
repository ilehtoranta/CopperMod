using System.Collections.Generic;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

internal static partial class NativeGraphicsRasterBodies
{
    private const ushort MoveAddressA1ToA2 = 0x2449;

    // Private native -750 arm.  It is emitted only by the opt-in native
    // monitor-mutation image and is never added to GraphicsLvo's public map.
    // ABI observed from Kickstart 3.1: A0=handle, A1=source MonitorInfo,
    // D0=source bytes, D1=DTAG_*, D2=family selector.  The body owns only
    // the appended CMDB record; no Exec allocation is performed.
    private static int AppendSetDisplayInfoDataMutation(List<byte> code)
    {
        var offset = code.Count;
        var declineBranches = new List<int>();
        var zeroBranches = new List<int>();
        var resultBranches = new List<int>();

        void Long(uint value)
        {
            Append(code, (ushort)(value >> 16));
            Append(code, (ushort)value);
        }

        int Branch(ushort opcode)
        {
            Append(code, opcode);
            var extension = code.Count;
            Append(code, 0);
            return extension;
        }

        void PatchAll(IEnumerable<int> branches, int target)
        {
            foreach (var branch in branches)
                PatchBranch(code, branch, target);
        }

        // Preserve the complete caller frame. D0 is restored with the
        // 0x7FFE mask only on successful/claimed returns.
        Append(code, MoveMultipleLongRegistersToPreDecrementA7);
        Append(code, 0xFFFE);
        Append(code, MoveLongD0ToD7); // original requested source size

        Append(code, CompareImmediateLongD1);
        Long(GraphicsDisplayDatabase.DtagMntr);
        var nonMonitorTag = Branch(BranchNotEqualWord);
        var monitorTag = Branch(BranchAlwaysWord);
        var tagDispatch = code.Count;

        // The ROM's other private data arm recognizes DTAG_DISP and returns
        // its 48-byte DisplayInfo payload. This mutation slice does not own
        // that record, but preserving the scalar keeps provider admission
        // deterministic for callers that probe -750 with the public tag.
        Append(code, CompareImmediateLongD1);
        Long(GraphicsDisplayDatabase.DtagDisp);
        var displayTag = Branch(BranchEqualWord);
        var unsupportedTag = Branch(BranchAlwaysWord);

        var displayTagResult = code.Count;
        Append(code, MoveLongImmediateD0);
        Long(48);
        var restoreResult = Branch(BranchAlwaysWord);
        PatchBranch(code, nonMonitorTag, tagDispatch);
        PatchBranch(code, displayTag, displayTagResult);
        declineBranches.Add(unsupportedTag);
        var monitorBody = code.Count;
        PatchBranch(code, monitorTag, monitorBody);

        // Sizes below the MonitorInfo header/pointer prefix are claimed with
        // no source or database access and return zero.
        Append(code, CompareImmediateLongD7);
        Long(20);
        var sizeTooSmall = Branch(BranchCarryWord);

        // The native registered form uses a null handle for the family
        // selector. Non-null handles remain provider-owned in this slice.
        Append(code, CompareAddressImmediateLongA0);
        Long(0);
        declineBranches.Add(Branch(BranchNotEqualWord));

        Append(code, CompareImmediateLongD7);
        Long(GraphicsMonitorInfoImage.TransferSize);
        var bounded = Branch(BranchLowerOrSameWord);
        Append(code, MoveLongImmediateD7);
        Long(GraphicsMonitorInfoImage.TransferSize);
        PatchBranch(code, bounded, code.Count);

        // A1 is a caller-owned source span. Validate non-null and the
        // maximum 88-byte envelope before the first guest read.
        Append(code, CompareAddressImmediateLongA1);
        Long(0);
        declineBranches.Add(Branch(BranchEqualWord));
        Append(code, CompareAddressImmediateLongA1);
        Long(uint.MaxValue - (GraphicsMonitorInfoImage.TransferSize - 1u));
        declineBranches.Add(Branch(BranchHighWord));

        // Resolve the published CMDB and require the mutation-enabled extent.
        Append(code, MoveLongA6DisplacementToD4);
        Append(code, checked((ushort)GraphicsLayouts.GfxBaseDisplayInfoDataBase));
        Append(code, TestLongD4);
        zeroBranches.Add(Branch(BranchEqualWord));
        Append(code, MoveAddressD4ToA2);
        Append(code, MoveLongA2DisplacementToD6);
        Append(code, 8);
        Append(code, CompareImmediateLongD6);
        Long((uint)GraphicsDisplayDatabase.NativeDatabaseMutationSize);
        zeroBranches.Add(Branch(BranchNotEqualWord));
        Append(code, MoveLongA2DisplacementToD6);
        Append(code, 0);
        Append(code, CompareImmediateLongD6);
        Long(GraphicsDisplayDatabase.NativeDatabaseMagic);
        zeroBranches.Add(Branch(BranchNotEqualWord));
        Append(code, MoveLongA2DisplacementToD6);
        Append(code, 4);
        Append(code, CompareImmediateLongD6);
        Long(GraphicsDisplayDatabase.NativeDatabaseVersion);
        zeroBranches.Add(Branch(BranchNotEqualWord));

        // D2 selects the canonical family. A3 is the registration cell and
        // A4 the corresponding appended 88-byte record.
        Append(code, CompareImmediateLongD2);
        Long(GraphicsModeIds.NtscMonitor);
        var palFamily = Branch(BranchNotEqualWord);
        Append(code, 0x47EA);
        Append(code, checked((ushort)GraphicsDisplayDatabase.NativeMonitorRegistrationsOffset));
        Append(code, 0x49EA);
        Append(code, checked((ushort)GraphicsDisplayDatabase.NativeMonitorMutationRecordsOffset));
        var familyReady = Branch(BranchAlwaysWord);
        PatchBranch(code, palFamily, code.Count);
        Append(code, CompareImmediateLongD2);
        Long(GraphicsModeIds.PalMonitor);
        zeroBranches.Add(Branch(BranchNotEqualWord));
        Append(code, 0x47EA);
        Append(code, checked((ushort)(GraphicsDisplayDatabase.NativeMonitorRegistrationsOffset + 4)));
        Append(code, 0x49EA);
        Append(code, checked((ushort)(GraphicsDisplayDatabase.NativeMonitorMutationRecordsOffset +
            GraphicsDisplayDatabase.NativeMonitorMutationRecordSize)));
        PatchBranch(code, familyReady, code.Count);

        // Copy the admitted source prefix into the private family record.
        // Registration-cell publication is last, so readers cannot observe a
        // partially transferred record.
        Append(code, MoveAddressA1ToA2); // source cursor
        Append(code, MoveAddressA4ToA0); // record destination
        Append(code, MoveLongD7ToD6);    // transfer count/result basis
        Append(code, SubQuickOneWordD7);
        var copy = code.Count;
        Append(code, MoveByteA2PostIncrementToD0);
        Append(code, 0x10C0); // MOVE.B D0,(A0)+
        Append(code, DecrementBranchD7);
        var copyBranch = code.Count;
        Append(code, 0);
        PatchBranch(code, copyBranch, copy);
        Append(code, 0x268C); // MOVE.L A4,(A3): publish record base after copy
        Append(code, MoveLongD6ToD0);
        Append(code, SubImmediateLongD0);
        Long(20);
        resultBranches.Add(Branch(BranchAlwaysWord));

        var zeroResult = code.Count;
        Append(code, MoveQuickZeroD0);
        resultBranches.Add(Branch(BranchAlwaysWord));

        var decline = code.Count;
        PatchAll(declineBranches, decline);
        Append(code, MoveMultipleLongA7PostIncrementToRegisters);
        Append(code, 0x7FFF); // restore D0-D7/A0-A6 and the original stack
        Append(code, ReturnFromSubroutine);

        var zero = code.Count;
        PatchAll(zeroBranches, zero);
        Append(code, MoveQuickZeroD0);
        Append(code, AddQuickFourLongA7); // discard saved original D0
        Append(code, MoveMultipleLongA7PostIncrementToRegisters);
        Append(code, 0x7FFE);
        Append(code, ReturnFromSubroutine);

        PatchBranch(code, sizeTooSmall, zeroResult);
        PatchBranch(code, restoreResult, code.Count);
        PatchAll(resultBranches, code.Count);
        Append(code, AddQuickFourLongA7); // discard saved original D0
        Append(code, MoveMultipleLongA7PostIncrementToRegisters);
        Append(code, 0x7FFE);
        Append(code, ReturnFromSubroutine);

        return offset;
    }
}
