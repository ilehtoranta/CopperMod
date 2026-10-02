/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.CompilerServices;

namespace Copper68k
{
    internal enum M68020OpcodeKind : ushort
    {
        Unsupported = 0,
        LineAException,
        LineFException,
        IllegalInstruction,
        ImmediateLogicalToStatusRegister,
        MoveStatusRegisterToAddressIndirect,
        MoveStatusRegisterToData,
        MoveDataToStatusRegister,
        MovePostIncrementToStatusRegister,
        AndPostIncrementToData,
        OrSizedAbsoluteLongToData,
        MoveFromCcr,
        MoveImmediateToCcr,
        Movep,
        Chk2Cmp2,
        Moveq,
        ClrDataLong,
        ClrLongAddressIndirect,
        ClrLongAddressDisplacement,
        ClrLongAbsoluteLong,
        ClrWordAbsoluteLong,
        ClrLongAbsoluteWord,
        NegxLongData,
        NegByteData,
        NegLongData,
        NegWordData,
        NegAbsoluteLong,
        NotByteData,
        NotByteAddressDisplacement,
        NotLongAddressDisplacement,
        NotWordData,
        NotLongData,
        ClrLongPostIncrement,
        ClrLongPredecrement,
        ClrDataByte,
        ClrDataWord,
        ClrWordPostIncrement,
        ClrWordAddressDisplacement,
        ClrWordPredecrement,
        ClrBytePredecrement,
        ClrByteAddressIndirect,
        ClrWordAddressIndirect,
        ClrBytePostIncrement,
        ClrByteAddressDisplacement,
        ClrByteAbsoluteLong,
        LeaAbsoluteLong,
        LeaAbsoluteWord,
        LeaAddressIndirect,
        LeaAddressDisplacement,
        LeaPcDisplacement,
        LeaBriefIndexed,
        LeaPcBriefIndexed,
        MoveImmediateToStatusRegister,
        MoveByteImmediateToAbsoluteLong,
        MoveByteImmediateToAddressIndirect,
        MoveByteImmediateToAddressDisplacement,
        MoveByteImmediateToBriefIndexed,
        MoveWordImmediateToBriefIndexed,
        MoveWordImmediateToAddressIndirect,
        MoveWordImmediateToAddressDisplacement,
        MoveWordImmediateToPredecrement,
        MoveByteImmediateToPredecrement,
        MoveWordImmediateToPostIncrement,
        MoveIndexedToIndexed,
        MoveIndexedToPostIncrement,
        MoveIndexedToAbsoluteLong,
        MoveIndirectToPostIncrement,
        MoveIndirectToIndexed,
        MoveAbsoluteLongToIndexed,
        MovePredecrementToData,
        MovePredecrementToPredecrement,
        MoveAbsoluteWordToData,
        MoveAbsoluteWordToAbsoluteWord,
        MovePostIncrementToAbsoluteLong,
        MovePredecrementToPostIncrement,
        MovePredecrementToAddressDisplacement,
        MovePostIncrementToAddressIndirect,
        MoveBriefIndexedToAddressIndirect,
        MoveWordDataToPredecrement,
        MoveWordImmediateToAbsoluteLong,
        MoveWordDataToAddressIndirect,
        MoveWordAddressToAddressIndirect,
        MoveLongImmediateToAbsoluteLong,
        MoveLongImmediateToAbsoluteWord,
        MoveLongImmediateToAddressIndirect,
        MoveLongAbsoluteWordToAddressDisplacement,
        MoveByteAbsoluteWordToAddressDisplacement,
        MoveByteAbsoluteLongToAddressDisplacement,
        MoveWordAbsoluteWordToAddressDisplacement,
        MoveLongAbsoluteLongToAddressDisplacement,
        MoveLongImmediateToAddressDisplacement,
        MoveLongImmediateToBriefIndexed,
        MoveLongImmediateToPostIncrement,
        MoveLongImmediateToPredecrement,
        MoveLongImmediateToData,
        MoveLongImmediateToAddress,
        MoveWordImmediateToAddress,
        MoveWordImmediateToData,
        MoveWordDataToData,
        MoveWordAddressToData,
        MoveWordDataToAddress,
        MoveByteImmediateToData,
        MoveLongDataToData,
        MoveLongDataToAddress,
        MoveLongDataToAddressIndirect,
        MoveLongDataToPostIncrement,
        MoveLongDataToPredecrement,
        MoveLongDataToAddressDisplacement,
        MoveLongDataToBriefIndexed,
        MoveLongAddressToAddress,
        MoveLongAddressToAddressIndirect,
        MoveLongAddressToPredecrement,
        MoveLongAddressToBriefIndexed,
        MoveLongAddressToAddressDisplacement,
        MoveLongAddressToPostIncrement,
        MoveLongAddressIndirectToPredecrement,
        MoveByteOrWordAddressIndirectToPredecrement,
        MoveLongPostIncrementToPredecrement,
        MoveBytePostIncrementToPredecrement,
        MoveLongAddressIndirectToAddressDisplacement,
        MoveLongAddressToData,
        MoveLongAddressIndirectToData,
        MoveLongPredecrementToData,
        MoveWordAddressIndirectToData,
        MoveWordAddressIndirectToAddressIndirect,
        MoveLongExtendedToAddressIndirect,
        MoveLongAddressIndirectToAddress,
        MoveLongPostIncrementToData,
        MoveLongPostIncrementToAddress,
        MoveLongPredecrementToAddress,
        MoveLongPostIncrementToPostIncrement,
        MoveLongPostIncrementToAddressDisplacement,
        MoveLongAddressDisplacementToData,
        MoveLongAddressDisplacementToAddress,
        MoveLongAddressDisplacementToAddressIndirect,
        MoveLongAddressDisplacementToAddressDisplacement,
        MoveLongAddressDisplacementToBriefIndexed,
        MoveByteAddressDisplacementToBriefIndexed,
        MoveLongAddressDisplacementToPredecrement,
        MoveByteOrWordAddressDisplacementToPostIncrement,
        MoveLongAddressDisplacementToPostIncrement,
        MoveLongPcDisplacementToData,
        MoveLongPcDisplacementToAddress,
        MoveLongPcDisplacementToAddressDisplacement,
        MoveLongPcDisplacementToPredecrement,
        MoveLongPcDisplacementToPostIncrement,
        MoveLongBriefIndexedToData,
        MoveLongBriefIndexedToAddress,
        MoveLongPcBriefIndexedToAddress,
        MoveLongBriefIndexedToAddressDisplacement,
        MoveLongAddressBriefIndexedToAddressDisplacement,
        MoveLongBriefIndexedToBriefIndexed,
        MoveLongBriefIndexedToPredecrement,
        MoveLongAddressIndirectToAddressIndirect,
        MoveLongAddressIndirectToPostIncrement,
        MoveLongAbsoluteWordToData,
        MoveLongAbsoluteWordToAddress,
        MoveLongAbsoluteLongToData,
        MoveLongAbsoluteLongToAddress,
        MoveLongAbsoluteLongToPredecrement,
        MoveLongAbsoluteWordToAbsoluteLong,
        MoveLongDataToAbsoluteWord,
        MoveLongDataToAbsoluteLong,
        MoveLongAddressToAbsoluteWord,
        MoveLongAddressToAbsoluteLong,
        MoveLongAddressIndirectToAbsoluteLong,
        MoveWordAddressIndirectToAbsoluteLong,
        MoveWordAbsoluteLongToPostIncrement,
        MoveWordAddressToPostIncrement,
        MoveWordPcDisplacementToAbsoluteLong,
        MoveSizedPcDisplacementToAbsoluteLong,
        MoveWordPcBriefIndexedToAddressDisplacement,
        MoveWordAddressDisplacementToBriefIndexed,
        MoveLongAbsoluteLongToAbsoluteLong,
        MoveLongAddressDisplacementToAbsoluteLong,
        MoveByteDataToData,
        MoveByteAddressIndirectToData,
        MoveByteAddressIndirectToAddressIndirect,
        MoveBytePostIncrementToData,
        MoveByteAddressDisplacementToData,
        MoveByteBriefIndexedToData,
        MoveBytePcBriefIndexedToData,
        MoveByteAbsoluteLongToData,
        MoveWordAbsoluteLongToData,
        MoveWordAbsoluteLongToAddress,
        MoveWordAddressIndirectToAddress,
        MoveWordAddressDisplacementToAddress,
        MoveWordPcBriefIndexedToAddress,
        MoveWordBriefIndexedToAddress,
        MoveWordAddressDisplacementToData,
        MoveWordAddressDisplacementToAddressIndirect,
        MoveByteAddressDisplacementToAddressIndirect,
        MoveWordAddressIndirectToAddressDisplacement,
        MoveWordAddressBriefIndexedToData,
        MoveWordBriefIndexedToAddressDisplacement,
        MoveWordPcBriefIndexedToData,
        MoveLongPcBriefIndexedToData,
        MoveWordPcBriefIndexedToPredecrement,
        MoveWordPostIncrementToData,
        MoveWordPostIncrementToAddress,
        MoveWordPostIncrementToPostIncrement,
        MoveWordDataToPostIncrement,
        MoveBytePostIncrementToAddressDisplacement,
        MoveWordPostIncrementToAddressDisplacement,
        MoveWordDataToAbsoluteLong,
        MoveWordDataToAddressDisplacement,
        MoveWordDataToBriefIndexed,
        MoveWordAddressToAddressDisplacement,
        MoveWordAddressDisplacementToAddressDisplacement,
        MoveWordAddressDisplacementToAbsoluteLong,
        MoveByteAddressDisplacementToAbsoluteLong,
        MoveWordPcDisplacementToAddressDisplacement,
        MovePcDisplacementToData,
        MoveWordAbsoluteLongToAbsoluteLong,
        MoveWordAbsoluteLongToAddressDisplacement,
        MoveWordAbsoluteLongToPredecrement,
        MoveWordPostIncrementToPredecrement,
        MoveWordAddressDisplacementToPredecrement,
        MoveByteAddressDisplacementToPredecrement,
        MoveByteDataToAbsoluteLong,
        MoveByteDataToAddressIndirect,
        MoveByteDataToAddressDisplacement,
        MoveByteAddressDisplacementToAddressDisplacement,
        MoveByteBriefIndexedToAddressDisplacement,
        MoveByteDataToBriefIndexed,
        MoveByteBriefIndexedToPredecrement,
        MoveByteDataToPostIncrement,
        MoveByteDataToPredecrement,
        MoveBytePostIncrementToPostIncrement,
        MoveByteAddressIndirectToAbsoluteLong,
        MoveByteAbsoluteLongToAbsoluteLong,
        ImmediateLogicalByteToAbsoluteLong,
        OriWordImmediateToAddressDisplacement,
        OriLongImmediateToAddressDisplacement,
        OriWordImmediateToAddressIndirect,
        AddiByteImmediateToData,
        AddiByteImmediateToAddressIndirect,
        AddiWordImmediateToAddressIndirect,
        AddiLongImmediateToAddressIndirect,
        AddiByteImmediateToAddressDisplacement,
        AddiWordImmediateToAddressDisplacement,
        AddiWordImmediateToData,
        AddiLongImmediateToData,
        AddiLongImmediateToAddressDisplacement,
        AddiLongImmediateToAbsoluteLong,
        AddiSmallImmediateToAbsoluteLong,
        SubiImmediateToAbsoluteLong,
        BitDynamicAddressIndirect,
        SubiByteImmediateToData,
        SubiWordImmediateToData,
        SubiByteImmediateToAddressDisplacement,
        SubiLongImmediateToAddressDisplacement,
        SubiWordImmediateToAddressDisplacement,
        SubiLongImmediateToData,
        SubByteDataToData,
        SubByteAddressDisplacementToData,
        SubByteDataToAddressDisplacement,
        SubWordDataToAddressDisplacement,
        SubWordDataToData,
        NegAddressDisplacement,
        CmpBriefIndexedToData,
        SubBriefIndexedToData,
        SubDataToAddressIndirect,
        SubPostIncrementToData,
        SubWordAddressIndirectToData,
        SubIndirectToData,
        SubWordAddressDisplacementToData,
        SubWordDataToPostIncrement,
        SubLongDataToData,
        SubLongAddressToData,
        SubLongAddressDisplacementToData,
        SubLongImmediateToData,
        SubImmediateToData,
        SubLongDataToAddressDisplacement,
        AddByteDataToData,
        AddWordDataToData,
        AddWordAddressToData,
        AddByteAddressIndirectToData,
        AddWordAddressIndirectToData,
        AddWordPredecrementToData,
        AddWordPostIncrementToData,
        AddByteAddressDisplacementToData,
        AddWordAddressDisplacementToData,
        AddWordImmediateToData,
        AddByteImmediateToData,
        AddLongDataToData,
        AddLongDataToAddressIndirect,
        AddSmallDataToAddressIndirect,
        AddDataToPostIncrement,
        AddLongDataToAbsoluteLong,
        SubDataToAbsoluteLong,
        AddLongAddressIndirectToData,
        AddByteOrWordBriefIndexedToData,
        AddLongBriefIndexedToData,
        AddLongAbsoluteLongToData,
        AddLongAddressToData,
        AddxByteDataToData,
        AddxWordDataToData,
        AddxLongDataToData,
        SubxByteDataToData,
        SubxWordDataToData,
        SubxLongDataToData,
        AddLongPostIncrementToData,
        AddBytePostIncrementToData,
        AddLongAddressDisplacementToData,
        AddLongPcDisplacementToData,
        AddSmallPcDisplacementToData,
        AddSmallAbsoluteLongToData,
        SubAbsoluteLongToData,
        AddPcBriefIndexedToData,
        SubPcBriefIndexedToData,
        AddqWordAbsoluteLong,
        SubPcDisplacementToData,
        AddLongImmediateToData,
        AddByteDataToAddressDisplacement,
        AddWordDataToAddressDisplacement,
        AddLongDataToAddressDisplacement,
        AddDataToBriefIndexed,
        CmpiImmediateToBriefIndexed,
        CmpPcDisplacementOrAbsoluteWordToData,
        BitDynamicAbsoluteLong,
        BitModifyDynamicBriefIndexed,
        BitImmediateIndirect,
        BitImmediatePostIncrement,
        NotAbsoluteWord,
        NotAbsoluteLong,
        NotAddressIndirect,
        NotPostIncrement,
        TstLongAbsoluteLong,
        AddqWordData,
        AddqLongData,
        AddqWordAddress,
        AddqLongAddress,
        AddqLongAddressIndirect,
        AddqByteAddressDisplacement,
        AddqByteAddressIndirect,
        AddqByteData,
        AddqWordAddressDisplacement,
        QuickWordBriefIndexed,
        AddqLongAddressDisplacement,
        AddqLongAbsoluteLong,
        SubqAbsoluteLong,
        SubqByteData,
        SubqWordData,
        SubqLongData,
        SubqWordAddress,
        SubqLongAddress,
        SubqLongAddressIndirect,
        QuickIndirect,
        QuickPostIncrement,
        SubqLongAddressDisplacement,
        SubqByteAddressDisplacement,
        SubqWordAddressDisplacement,
        AddaWordImmediateToAddress,
        AddaWordDataToAddress,
        AddaWordAddressToAddress,
        AddaWordAddressDisplacementToAddress,
        AddaWordBriefIndexedToAddress,
        AddaPcBriefIndexedToAddress,
        AddaLongBriefIndexedToAddress,
        AddaLongImmediateToAddress,
        AddaLongDataToAddress,
        AddaLongAddressToAddress,
        AddaLongAddressDisplacementToAddress,
        AddaPostIncrementToAddress,
        AddaAddressIndirectToAddress,
        AddaAbsoluteLongToAddress,
        SubaAbsoluteLongToAddress,
        SubaPostIncrementToAddress,
        SubaAddressIndirectToAddress,
        SubaLongImmediateToAddress,
        SubaLongDataToAddress,
        SubaLongAddressToAddress,
        SubaLongAddressDisplacementToAddress,
        SubaLongPcDisplacementToAddress,
        SubaWordAddressDisplacementToAddress,
        SubaWordImmediateToAddress,
        SubaWordDataToAddress,
        ChkWordImmediate,
        LongMultiplyDivide,
        Cas2,
        Cas,
        DivideWordUnsigned,
        DivideWordSigned,
        AndiWordImmediateToData,
        AndiWordImmediateToAddressDisplacement,
        AndiLongImmediateToAddressDisplacement,
        AndiByteImmediateToData,
        AndiByteImmediateToAddressIndirect,
        AndiWideImmediateToAddressIndirect,
        AndiByteImmediateToAddressDisplacement,
        AndByteImmediateToData,
        AndWordImmediateToData,
        AndLongImmediateToData,
        AndLongEffectiveAddressToData,
        OrWordImmediateToData,
        OrByteImmediateToData,
        OrWordAddressDisplacementToData,
        OrLongEffectiveAddressToData,
        EoriWordImmediateToData,
        EoriLongImmediateToData,
        EorLongDataToAddressDisplacement,
        EorLongDataToData,
        EorByteDataToAddressDisplacement,
        EorWordDataToData,
        EorByteDataToData,
        MultiplyWordUnsigned,
        MultiplyWordSigned,
        OrByteDataToData,
        OrWordDataToData,
        OrDataToAbsoluteLong,
        OrDataToPostIncrement,
        AndBriefIndexedToData,
        AndPcBriefIndexedToData,
        AndSmallAbsoluteLongToData,
        AndDataToPostIncrement,
        AndiImmediateToPostIncrement,
        OrWordDataToAddressIndirect,
        OrWordDataToAddressDisplacement,
        OrByteAddressDisplacementToData,
        OrByteDataToAddressIndirect,
        OrByteDataToAddressDisplacement,
        OrLongDataToAddressIndirect,
        OrLongDataToAddressDisplacement,
        AndByteDataToData,
        AndByteAddressDisplacementToData,
        AndByteDataToAddressDisplacement,
        AndDataToAddressIndirect,
        ClrBriefIndexed,
        AndWordDataToData,
        AndWordAddressDisplacementToData,
        AndAddressIndirectToData,
        AndWordDataToAddressDisplacement,
        AndLongDataToAddressDisplacement,
        ExgDataData,
        ExgDataAddress,
        ExgAddressAddress,
        BcdByteAdd,
        BcdByteSubtract,
        OriByteImmediateToData,
        OriByteImmediateToAddressIndirect,
        OriByteImmediateToAddressDisplacement,
        OriWordImmediateToData,
        ImmediateLogicalData,
        EoriImmediateToAddressDisplacement,
        BtstByteImmediateAbsoluteLong,
        BitImmediateBriefIndexed,
        BtstByteImmediateAddressIndirect,
        BtstByteImmediatePostIncrement,
        BtstByteImmediateAddressDisplacement,
        BchgByteImmediateAbsoluteLong,
        BchgByteImmediateAddressDisplacement,
        BclrByteImmediateAbsoluteLong,
        BclrByteImmediateAddressDisplacement,
        BsetByteImmediateAbsoluteLong,
        BsetByteImmediateAddressDisplacement,
        BsetByteDynamicAddressDisplacement,
        BitModifyDynamicAddressDisplacement,
        BtstImmediateData,
        BtstDynamicData,
        BtstByteDynamicAddressDisplacement,
        BtstByteDynamicAddressIndirect,
        BtstByteDynamicBriefIndexed,
        BtstByteDynamicAbsoluteLong,
        BchgByteDynamicAddressIndirect,
        BsetImmediateData,
        BsetDynamicData,
        BclrImmediateData,
        BchgImmediateData,
        BclrDynamicData,
        SwapData,
        ExtWordData,
        ExtLongData,
        TstByteData,
        TstPcDisplacement,
        TstWordData,
        TstWordAbsoluteLong,
        TstByteAbsoluteLong,
        TstWordAddressIndirect,
        TstByteAddressIndirect,
        TstWordOrLongPostIncrement,
        TstBytePostIncrement,
        TstByteAddressDisplacement,
        TstByteBriefIndexed,
        TstWordBriefIndexed,
        TstWordAddressDisplacement,
        TstLongData,
        TstLongAddressIndirect,
        TstLongAddressDisplacement,
        TstLongBriefIndexed,
        BitField,
        LsrByteImmediateData,
        LsrWordImmediateData,
        LsrWordRegisterData,
        LsrByteRegisterData,
        LsrWordAddressDisplacement,
        AsrByteImmediateData,
        AsrLongImmediateData,
        AsrLongRegisterData,
        AsrWordImmediateData,
        LsrLongImmediateData,
        LsrLongRegisterData,
        AslLongImmediateData,
        AslLongRegisterData,
        AslByteRegisterData,
        AslWordImmediateData,
        LslLongImmediateData,
        LslLongRegisterData,
        LslWordRegisterData,
        LslByteImmediateData,
        LslWordImmediateData,
        RorByteImmediateData,
        RotateRegisterData,
        RotateExtendData,
        ShiftRegisterData,
        RorWordImmediateData,
        RorLongImmediateData,
        RolWordImmediateData,
        RolLongImmediateData,
        CmpiByteImmediateToData,
        CmpiByteImmediateToAddressIndirect,
        CmpiByteImmediateToAddressDisplacement,
        CmpiByteImmediateToPredecrement,
        CmpiWordImmediateToData,
        CmpiWordImmediateToAddressIndirect,
        CmpiWordImmediateToAddressDisplacement,
        CmpiByteImmediateToAbsoluteLong,
        CmpiWordImmediateToAbsoluteLong,
        CmpiLongImmediateToData,
        CmpiLongImmediateToPostIncrement,
        CmpiSmallImmediateToPostIncrement,
        CmpiLongImmediateToAddressIndirect,
        CmpiLongImmediateToAddressDisplacement,
        CmpaWordImmediateToAddress,
        CmpaWordDataToAddress,
        CmpaWordAddressToAddress,
        CmpaLongImmediateToAddress,
        CmpaLongDataToAddress,
        CmpaLongAddressToAddress,
        CmpaLongAddressIndirectToAddress,
        CmpaLongAddressDisplacementToAddress,
        CmpaWordAddressDisplacementToAddress,
        CmpaLongPredecrementToAddress,
        CmpaLongPostIncrementToAddress,
        CmpaAbsoluteLongToAddress,
        CmpLongDataToData,
        CmpLongImmediateToData,
        CmpLongAbsoluteLongToData,
        CmpLongAddressToData,
        CmpLongAddressIndirectToData,
        CmpLongPostIncrementToData,
        CmpLongAddressDisplacementToData,
        CmpByteDataToData,
        CmpByteImmediateToData,
        CmpByteAddressIndirectToData,
        CmpBytePostIncrementToData,
        CmpByteAddressDisplacementToData,
        CmpByteAbsoluteLongToData,
        CmpWordDataToData,
        CmpWordAddressToData,
        CmpWordImmediateToData,
        CmpWordAddressIndirectToData,
        CmpWordPostIncrementToData,
        CmpWordAddressDisplacementToData,
        CmpmBytePostIncrement,
        CmpmWordPostIncrement,
        CmpmLongPostIncrement,
        CmpiLongImmediateToAbsoluteLong,
        CmpiLongImmediateToAbsoluteWord,
        Nop,
        Reset,
        Stop,
        Movec,
        MoveUsp,
        Trap,
        Rte,
        Rtd,
        Rts,
        JmpAddressIndirect,
        JmpAddressDisplacement,
        JmpPcDisplacement,
        JmpBriefIndexed,
        JmpAddressBriefIndexed,
        JsrAddressIndirect,
        JsrAbsoluteLong,
        JumpAbsoluteWord,
        JsrAddressDisplacement,
        JsrBriefIndexed,
        JsrPcDisplacement,
        JsrPcBriefIndexed,
        JmpAbsoluteLong,
        PeaAddressDisplacement,
        PeaAddressIndirect,
        PeaBriefIndexed,
        PeaPcBriefIndexed,
        PeaAbsoluteWord,
        PeaAbsoluteLong,
        PeaPcDisplacement,
        LinkWord,
        LinkLong,
        Unlink,
        NbcdByte,
        ExtbLong,
        MovemLongRegistersToPredecrement,
        MovemWordRegistersToPredecrement,
        MovemWordRegistersToAddressDisplacement,
        MovemWordAddressDisplacementToRegisters,
        MovemWordPcDisplacementToRegisters,
        MovemWordAddressIndirectToRegisters,
        MovemLongRegistersToAddressIndirect,
        MovemLongRegistersToAddressDisplacement,
        MovemLongRegistersToBriefIndexed,
        MovemLongAddressIndirectToRegisters,
        MovemIndexedToRegisters,
        MovemLongAddressDisplacementToRegisters,
        MovemLongPcDisplacementToRegisters,
        MovemLongPostIncrementToRegisters,
        MovemWordPostIncrementToRegisters,
        LongBranch,
        ByteBranch,
        WordBranch,
        Trapcc,
        SccData,
        SccAbsoluteLong,
        SccAbsoluteWord,
        SccAddressMemory,
        Dbcc,
        MovemLongRegistersToAbsoluteLong,
        MoveByteImmediateToPostIncrement,
    }

    internal static class M68020OpcodeDispatchTable
    {
        // Model-specific legality is baked into these tables so the hot path stays lookup + switch.
        internal static readonly M68020OpcodeKind[] M68020Kinds = CreateKinds();
        internal static readonly M68020OpcodeKind[] M68010Kinds = CreateM68010Kinds(M68020Kinds);
        internal static readonly M68020OpcodeKind[] M68030Kinds = M68020Kinds;
        internal static readonly M68020OpcodeKind[] M68040Kinds = M68020Kinds;

        private static M68020OpcodeKind[] CreateKinds()
        {
            var kinds = new M68020OpcodeKind[0x10000];
            for (var opcode = 0; opcode < kinds.Length; opcode++)
            {
                kinds[opcode] = ClassifyBaseOpcode((ushort)opcode);
            }

            return kinds;
        }

        private static M68020OpcodeKind[] CreateM68010Kinds(M68020OpcodeKind[] m68020Kinds)
        {
            var kinds = (M68020OpcodeKind[])m68020Kinds.Clone();
            for (var opcode = 0; opcode < kinds.Length; opcode++)
            {
                if (IsM68020Only(kinds[opcode]))
                {
                    kinds[opcode] = M68020OpcodeKind.IllegalInstruction;
                }
            }

            return kinds;
        }

        private static bool IsM68020Only(M68020OpcodeKind kind)
            => kind is M68020OpcodeKind.Chk2Cmp2
                or M68020OpcodeKind.LongMultiplyDivide
                or M68020OpcodeKind.Cas2
                or M68020OpcodeKind.Cas
                or M68020OpcodeKind.BitField
                or M68020OpcodeKind.LinkLong
                or M68020OpcodeKind.ExtbLong
                or M68020OpcodeKind.LongBranch
                or M68020OpcodeKind.Trapcc;

        private static M68020OpcodeKind ClassifyBaseOpcode(ushort opcode)
        {
            if ((opcode & 0xF000) == 0xA000)
            {
                return M68020OpcodeKind.LineAException;
            }

            if ((opcode & 0xF000) == 0xF000)
            {
                return M68020OpcodeKind.LineFException;
            }

            if (opcode == 0x4AFC)
            {
                return M68020OpcodeKind.IllegalInstruction;
            }

            if (opcode is 0x003C or 0x007C or 0x023C or 0x027C or 0x0A3C or 0x0A7C)
            {
                return M68020OpcodeKind.ImmediateLogicalToStatusRegister;
            }

            if ((opcode & 0xFFF8) == 0x40D0)
            {
                return M68020OpcodeKind.MoveStatusRegisterToAddressIndirect;
            }

            if ((opcode & 0xFFF8) == 0x40C0)
            {
                return M68020OpcodeKind.MoveStatusRegisterToData;
            }

            if ((opcode & 0xFFF8) == 0x46C0)
            {
                return M68020OpcodeKind.MoveDataToStatusRegister;
            }

            if ((opcode & 0xFFF8) == 0x46D8)
            {
                return M68020OpcodeKind.MovePostIncrementToStatusRegister;
            }

            if ((opcode & 0xF138) == 0xC018 && ((opcode >> 6) & 3) != 3)
            {
                return M68020OpcodeKind.AndPostIncrementToData;
            }

            if ((opcode & 0xF13F) == 0x8039 && ((opcode >> 6) & 3) < 2)
            {
                return M68020OpcodeKind.OrSizedAbsoluteLongToData;
            }
            if ((opcode & 0xFF3F) == 0x4439 && ((opcode >> 6) & 3) != 3)
            {
                return M68020OpcodeKind.NegAbsoluteLong;
            }
            if ((opcode & 0xF1FF) is 0xB0F9 or 0xB1F9)
            {
                return M68020OpcodeKind.CmpaAbsoluteLongToAddress;
            }
            if ((opcode & 0xFF3F) == 0x4A3A && ((opcode >> 6) & 3) != 3)
            {
                return M68020OpcodeKind.TstPcDisplacement;
            }
            if (opcode is 0x13FA or 0x23FA)
            {
                return M68020OpcodeKind.MoveSizedPcDisplacementToAbsoluteLong;
            }
            if ((opcode & 0xF1FF) is 0xD0FB or 0xD1FB)
            {
                return M68020OpcodeKind.AddaPcBriefIndexedToAddress;
            }

            if ((opcode & 0xFFC0) == 0x42C0)
            {
                return M68020OpcodeKind.MoveFromCcr;
            }

            if ((opcode & 0xF138) == 0x0108)
            {
                return M68020OpcodeKind.Movep;
            }

            if ((opcode & 0xF9C0) is 0x00C0 or 0x02C0 or 0x04C0)
            {
                var mode = (opcode >> 3) & 7;
                var register = opcode & 7;
                return (opcode & 0x0600) != 0x0600 &&
                    (mode is 2 or 5 or 6 || (mode == 7 && register <= 3))
                        ? M68020OpcodeKind.Chk2Cmp2
                        : M68020OpcodeKind.IllegalInstruction;
            }

            if ((opcode & 0xF100) == 0x7000)
            {
                return M68020OpcodeKind.Moveq;
            }

            if ((opcode & 0xFFF8) == 0x4280)
            {
                return M68020OpcodeKind.ClrDataLong;
            }

            if ((opcode & 0xFFF8) == 0x4290)
            {
                return M68020OpcodeKind.ClrLongAddressIndirect;
            }

            if ((opcode & 0xFFF8) == 0x42A8)
            {
                return M68020OpcodeKind.ClrLongAddressDisplacement;
            }

            if (opcode == 0x42B9)
            {
                return M68020OpcodeKind.ClrLongAbsoluteLong;
            }

            if (opcode == 0x42B8)
            {
                return M68020OpcodeKind.ClrLongAbsoluteWord;
            }

            if ((opcode & 0xFFF8) == 0x4080)
            {
                return M68020OpcodeKind.NegxLongData;
            }

            if ((opcode & 0xFFF8) == 0x4400)
            {
                return M68020OpcodeKind.NegByteData;
            }

            if ((opcode & 0xFFF8) == 0x4480)
            {
                return M68020OpcodeKind.NegLongData;
            }

            if ((opcode & 0xFFF8) == 0x4440)
            {
                return M68020OpcodeKind.NegWordData;
            }

            if ((opcode & 0xFFF8) == 0x4600)
            {
                return M68020OpcodeKind.NotByteData;
            }

            if ((opcode & 0xFFF8) == 0x4628)
            {
                return M68020OpcodeKind.NotByteAddressDisplacement;
            }

            if ((opcode & 0xFFF8) == 0x46A8)
            {
                return M68020OpcodeKind.NotLongAddressDisplacement;
            }

            if ((opcode & 0xFFF8) == 0x4640)
            {
                return M68020OpcodeKind.NotWordData;
            }

            if ((opcode & 0xFFF8) == 0x4680)
            {
                return M68020OpcodeKind.NotLongData;
            }

            if ((opcode & 0xFFF8) == 0x4298)
            {
                return M68020OpcodeKind.ClrLongPostIncrement;
            }

            if ((opcode & 0xFFF8) == 0x42A0)
            {
                return M68020OpcodeKind.ClrLongPredecrement;
            }

            if ((opcode & 0xFFF8) == 0x4220)
            {
                return M68020OpcodeKind.ClrBytePredecrement;
            }

            if ((opcode & 0xFFF8) == 0x4260)
            {
                return M68020OpcodeKind.ClrWordPredecrement;
            }

            if ((opcode & 0xFFF8) == 0x4258)
            {
                return M68020OpcodeKind.ClrWordPostIncrement;
            }

            if ((opcode & 0xFFF8) == 0x4240)
            {
                return M68020OpcodeKind.ClrDataWord;
            }

            if ((opcode & 0xFFF8) == 0x4200)
            {
                return M68020OpcodeKind.ClrDataByte;
            }

            if ((opcode & 0xFFF8) == 0x4268)
            {
                return M68020OpcodeKind.ClrWordAddressDisplacement;
            }

            if ((opcode & 0xFFF8) == 0x4250)
            {
                return M68020OpcodeKind.ClrWordAddressIndirect;
            }

            if ((opcode & 0xFFF8) == 0x4210)
            {
                return M68020OpcodeKind.ClrByteAddressIndirect;
            }

            if ((opcode & 0xFFF8) == 0x4218)
            {
                return M68020OpcodeKind.ClrBytePostIncrement;
            }

            if ((opcode & 0xFFF8) == 0x4228)
            {
                return M68020OpcodeKind.ClrByteAddressDisplacement;
            }

            if (opcode == 0x4239)
            {
                return M68020OpcodeKind.ClrByteAbsoluteLong;
            }

            if ((opcode & 0xF1FF) == 0x41F9)
            {
                return M68020OpcodeKind.LeaAbsoluteLong;
            }

            if ((opcode & 0xF1FF) == 0x41F8)
            {
                return M68020OpcodeKind.LeaAbsoluteWord;
            }

            if ((opcode & 0xF1F8) == 0x41D0)
            {
                return M68020OpcodeKind.LeaAddressIndirect;
            }

            if ((opcode & 0xF1F8) == 0x41E8)
            {
                return M68020OpcodeKind.LeaAddressDisplacement;
            }

            if ((opcode & 0xF1FF) == 0x41FA)
            {
                return M68020OpcodeKind.LeaPcDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x41F0)
            {
                return M68020OpcodeKind.LeaBriefIndexed;
            }
            if ((opcode & 0xF1FF) == 0x41FB)
            {
                return M68020OpcodeKind.LeaPcBriefIndexed;
            }

            if (opcode == 0x46FC)
            {
                return M68020OpcodeKind.MoveImmediateToStatusRegister;
            }

            if (opcode == 0x13FC)
            {
                return M68020OpcodeKind.MoveByteImmediateToAbsoluteLong;
            }

            if ((opcode & 0xF1FF) == 0x10BC)
            {
                return M68020OpcodeKind.MoveByteImmediateToAddressIndirect;
            }

            if ((opcode & 0xF1FF) == 0x10FC)
            {
                return M68020OpcodeKind.MoveByteImmediateToPostIncrement;
            }

            if ((opcode & 0xF1FF) == 0x117C)
            {
                return M68020OpcodeKind.MoveByteImmediateToAddressDisplacement;
            }

            if ((opcode & 0xF1FF) == 0x11BC)
            {
                return M68020OpcodeKind.MoveByteImmediateToBriefIndexed;
            }

            if ((opcode & 0xF1FF) == 0x31BC)
                return M68020OpcodeKind.MoveWordImmediateToBriefIndexed;

            if ((opcode & 0xF1FF) == 0x30BC)
            {
                return M68020OpcodeKind.MoveWordImmediateToAddressIndirect;
            }

            if ((opcode & 0xF1FF) == 0x317C)
            {
                return M68020OpcodeKind.MoveWordImmediateToAddressDisplacement;
            }

            if ((opcode & 0xF1FF) == 0x313C)
            {
                return M68020OpcodeKind.MoveWordImmediateToPredecrement;
            }
            if ((opcode & 0xF1FF) == 0x113C)
            {
                return M68020OpcodeKind.MoveByteImmediateToPredecrement;
            }

            if ((opcode & 0xF1F8) == 0x3100)
            {
                return M68020OpcodeKind.MoveWordDataToPredecrement;
            }

            if ((opcode & 0xF1F8) == 0x3080)
            {
                return M68020OpcodeKind.MoveWordDataToAddressIndirect;
            }
            if ((opcode & 0xF1FF) == 0x30FC)
            {
                return M68020OpcodeKind.MoveWordImmediateToPostIncrement;
            }
            if ((opcode & 0xFFF8) is 0x13D8 or 0x33D8 or 0x23D8)
            {
                return M68020OpcodeKind.MovePostIncrementToAbsoluteLong;
            }
            if ((opcode & 0xFFF8) is 0x13F0 or 0x33F0 or 0x23F0 || opcode is 0x13FB or 0x33FB or 0x23FB)
                return M68020OpcodeKind.MoveIndexedToAbsoluteLong;
            if ((opcode & 0xF1FF) is 0x11BB or 0x31BB or 0x21BB ||
                (opcode & 0xF1F8) is 0x11B0 or 0x31B0)
            {
                return M68020OpcodeKind.MoveIndexedToIndexed;
            }
            if ((opcode & 0xF1FF) is 0x10FB or 0x30FB or 0x20FB ||
                (opcode & 0xF1F8) is 0x10F0 or 0x30F0 or 0x20F0)
            {
                return M68020OpcodeKind.MoveIndexedToPostIncrement;
            }
            if ((opcode & 0xF1F8) is 0x9010 or 0x9090)
            {
                return M68020OpcodeKind.SubIndirectToData;
            }
            if ((opcode & 0xF1F8) is 0x10D0 or 0x30D0)
            {
                return M68020OpcodeKind.MoveIndirectToPostIncrement;
            }
            if ((opcode & 0xF1F8) is 0x1190 or 0x3190 or 0x2190)
            {
                return M68020OpcodeKind.MoveIndirectToIndexed;
            }
            if ((opcode & 0xF1FF) is 0x11B9 or 0x31B9 or 0x21B9)
            {
                return M68020OpcodeKind.MoveAbsoluteLongToIndexed;
            }
            if ((opcode & 0xF1F8) is 0x1020 or 0x3020)
            {
                return M68020OpcodeKind.MovePredecrementToData;
            }
            if ((opcode & 0xF1F8) is 0x1120 or 0x3120 or 0x2120)
            {
                return M68020OpcodeKind.MovePredecrementToPredecrement;
            }
            if ((opcode & 0xF1FF) is 0x1038 or 0x3038)
            {
                return M68020OpcodeKind.MoveAbsoluteWordToData;
            }
            if ((opcode & 0xF1FF) is 0x103A or 0x303A)
            {
                return M68020OpcodeKind.MovePcDisplacementToData;
            }
            if (opcode is 0x11F8 or 0x31F8 or 0x21F8)
            {
                return M68020OpcodeKind.MoveAbsoluteWordToAbsoluteWord;
            }
            if ((opcode & 0xF038) == 0xE038 && (opcode & 0x00C0) != 0x00C0)
            {
                return M68020OpcodeKind.RotateRegisterData;
            }
            if ((opcode & 0xF018) == 0xE010 && (opcode & 0x00C0) != 0x00C0)
            {
                return M68020OpcodeKind.RotateExtendData;
            }
            if ((opcode & 0xF1F8) is 0xE020 or 0xE060 or 0xE160 or 0xE128)
            {
                return M68020OpcodeKind.ShiftRegisterData;
            }
            if ((opcode & 0xF138) == 0xD130 && (opcode & 0x00C0) != 0x00C0)
            {
                return M68020OpcodeKind.AddDataToBriefIndexed;
            }
            if ((opcode & 0xFF38) == 0x0C30 && (opcode & 0x00C0) != 0x00C0)
            {
                return M68020OpcodeKind.CmpiImmediateToBriefIndexed;
            }
            if ((opcode & 0xF13F) is 0xB038 or 0xB03A && (opcode & 0x00C0) != 0x00C0)
            {
                return M68020OpcodeKind.CmpPcDisplacementOrAbsoluteWordToData;
            }
            if ((opcode & 0xF1F8) is 0x0170 or 0x01B0 or 0x01F0)
            {
                return M68020OpcodeKind.BitModifyDynamicBriefIndexed;
            }
            if ((opcode & 0xF1FF) is 0x0179 or 0x01B9 or 0x01F9)
            {
                return M68020OpcodeKind.BitDynamicAbsoluteLong;
            }
            if ((opcode & 0xFFF8) is 0x0890 or 0x08D0)
            {
                return M68020OpcodeKind.BitImmediateIndirect;
            }
            if ((opcode & 0xFFF8) is 0x0858 or 0x0898 or 0x08D8)
            {
                return M68020OpcodeKind.BitImmediatePostIncrement;
            }
            if ((opcode & 0xFFF8) == 0x0840)
            {
                return M68020OpcodeKind.BchgImmediateData;
            }
            if ((opcode & 0xF1F8) is 0x5110 or 0x5150 or 0x5050)
            {
                return M68020OpcodeKind.QuickIndirect;
            }
            if ((opcode & 0xFF38) == 0x0A28 && (opcode & 0xC0) != 0xC0)
            {
                return M68020OpcodeKind.EoriImmediateToAddressDisplacement;
            }
            if (opcode == 0x44FC)
            {
                return M68020OpcodeKind.MoveImmediateToCcr;
            }
            if ((opcode & 0xF038) == 0x5018 && (opcode & 0xC0) != 0xC0)
            {
                return M68020OpcodeKind.QuickPostIncrement;
            }
            if (opcode is 0x4EB8 or 0x4EF8)
            {
                return M68020OpcodeKind.JumpAbsoluteWord;
            }
            if (opcode is 0x4638 or 0x4678 or 0x46B8)
            {
                return M68020OpcodeKind.NotAbsoluteWord;
            }
            if ((opcode & 0xF1F8) == 0x3088)
            {
                return M68020OpcodeKind.MoveWordAddressToAddressIndirect;
            }

            if (opcode == 0x33FC)
            {
                return M68020OpcodeKind.MoveWordImmediateToAbsoluteLong;
            }

            if (opcode == 0x23FC)
            {
                return M68020OpcodeKind.MoveLongImmediateToAbsoluteLong;
            }

            if (opcode == 0x21FC)
            {
                return M68020OpcodeKind.MoveLongImmediateToAbsoluteWord;
            }

            if ((opcode & 0xF1FF) == 0x20BC)
            {
                return M68020OpcodeKind.MoveLongImmediateToAddressIndirect;
            }

            if ((opcode & 0xF1FC) == 0x20B8)
            {
                return M68020OpcodeKind.MoveLongExtendedToAddressIndirect;
            }

            if ((opcode & 0xF1FF) == 0x2178)
            {
                return M68020OpcodeKind.MoveLongAbsoluteWordToAddressDisplacement;
            }

            if ((opcode & 0xF1FF) == 0x1178)
                return M68020OpcodeKind.MoveByteAbsoluteWordToAddressDisplacement;
            if ((opcode & 0xF1FF) == 0x1179)
                return M68020OpcodeKind.MoveByteAbsoluteLongToAddressDisplacement;
            if ((opcode & 0xF1FF) == 0x3178)
                return M68020OpcodeKind.MoveWordAbsoluteWordToAddressDisplacement;

            if ((opcode & 0xF1FF) == 0x2179)
            {
                return M68020OpcodeKind.MoveLongAbsoluteLongToAddressDisplacement;
            }

            if ((opcode & 0xF1FF) == 0x217C)
            {
                return M68020OpcodeKind.MoveLongImmediateToAddressDisplacement;
            }

            if ((opcode & 0xF1FF) == 0x21BC)
            {
                return M68020OpcodeKind.MoveLongImmediateToBriefIndexed;
            }

            if ((opcode & 0xF1FF) == 0x20FC)
            {
                return M68020OpcodeKind.MoveLongImmediateToPostIncrement;
            }

            if ((opcode & 0xF1FF) == 0x213C)
            {
                return M68020OpcodeKind.MoveLongImmediateToPredecrement;
            }

            if ((opcode & 0xF1FF) == 0x203C)
            {
                return M68020OpcodeKind.MoveLongImmediateToData;
            }

            if ((opcode & 0xF1FF) == 0x207C)
            {
                return M68020OpcodeKind.MoveLongImmediateToAddress;
            }

            if ((opcode & 0xF1FF) == 0x307C)
            {
                return M68020OpcodeKind.MoveWordImmediateToAddress;
            }

            if ((opcode & 0xF1FF) == 0x303C)
            {
                return M68020OpcodeKind.MoveWordImmediateToData;
            }

            if ((opcode & 0xF1F8) == 0x3000)
            {
                return M68020OpcodeKind.MoveWordDataToData;
            }

            if ((opcode & 0xF1F8) == 0x3008)
            {
                return M68020OpcodeKind.MoveWordAddressToData;
            }

            if ((opcode & 0xF1F8) == 0x3040)
            {
                return M68020OpcodeKind.MoveWordDataToAddress;
            }

            if ((opcode & 0xF1FF) == 0x103C)
            {
                return M68020OpcodeKind.MoveByteImmediateToData;
            }

            if ((opcode & 0xF1F8) == 0x2000)
            {
                return M68020OpcodeKind.MoveLongDataToData;
            }

            if ((opcode & 0xF1F8) == 0x2040)
            {
                return M68020OpcodeKind.MoveLongDataToAddress;
            }

            if ((opcode & 0xF1F8) == 0x2080)
            {
                return M68020OpcodeKind.MoveLongDataToAddressIndirect;
            }

            if ((opcode & 0xF1F8) == 0x20C0)
            {
                return M68020OpcodeKind.MoveLongDataToPostIncrement;
            }

            if ((opcode & 0xF1F8) == 0x2100)
            {
                return M68020OpcodeKind.MoveLongDataToPredecrement;
            }

            if ((opcode & 0xF1F8) == 0x2140)
            {
                return M68020OpcodeKind.MoveLongDataToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x2180)
            {
                return M68020OpcodeKind.MoveLongDataToBriefIndexed;
            }

            if ((opcode & 0xF1F8) == 0x2048)
            {
                return M68020OpcodeKind.MoveLongAddressToAddress;
            }

            if ((opcode & 0xF1F8) == 0x2088)
            {
                return M68020OpcodeKind.MoveLongAddressToAddressIndirect;
            }

            if ((opcode & 0xF1F8) == 0x2108)
            {
                return M68020OpcodeKind.MoveLongAddressToPredecrement;
            }

            if ((opcode & 0xF1F8) == 0x2188)
            {
                return M68020OpcodeKind.MoveLongAddressToBriefIndexed;
            }

            if ((opcode & 0xF1F8) is 0x1110 or 0x3110)
            {
                return M68020OpcodeKind.MoveByteOrWordAddressIndirectToPredecrement;
            }

            if ((opcode & 0xF1F8) == 0x2110)
            {
                return M68020OpcodeKind.MoveLongAddressIndirectToPredecrement;
            }

            if ((opcode & 0xF1F8) == 0x2118)
            {
                return M68020OpcodeKind.MoveLongPostIncrementToPredecrement;
            }
            if ((opcode & 0xF1F8) == 0x1118)
            {
                return M68020OpcodeKind.MoveBytePostIncrementToPredecrement;
            }

            if ((opcode & 0xF1F8) == 0x2150)
            {
                return M68020OpcodeKind.MoveLongAddressIndirectToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x2148)
            {
                return M68020OpcodeKind.MoveLongAddressToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x20C8)
            {
                return M68020OpcodeKind.MoveLongAddressToPostIncrement;
            }

            if ((opcode & 0xF1F8) == 0x2008)
            {
                return M68020OpcodeKind.MoveLongAddressToData;
            }

            if ((opcode & 0xF1F8) == 0x2010)
            {
                return M68020OpcodeKind.MoveLongAddressIndirectToData;
            }

            if ((opcode & 0xF1F8) == 0x2020)
            {
                return M68020OpcodeKind.MoveLongPredecrementToData;
            }

            if ((opcode & 0xF1F8) == 0x3010)
            {
                return M68020OpcodeKind.MoveWordAddressIndirectToData;
            }

            if ((opcode & 0xF1F8) == 0x2050)
            {
                return M68020OpcodeKind.MoveLongAddressIndirectToAddress;
            }

            if ((opcode & 0xF1F8) == 0x2018)
            {
                return M68020OpcodeKind.MoveLongPostIncrementToData;
            }

            if ((opcode & 0xF1F8) == 0x2060)
            {
                return M68020OpcodeKind.MoveLongPredecrementToAddress;
            }

            if ((opcode & 0xF1F8) == 0x2058)
            {
                return M68020OpcodeKind.MoveLongPostIncrementToAddress;
            }

            if ((opcode & 0xF1F8) == 0x20D8)
            {
                return M68020OpcodeKind.MoveLongPostIncrementToPostIncrement;
            }

            if ((opcode & 0xF1F8) == 0x2158)
            {
                return M68020OpcodeKind.MoveLongPostIncrementToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x2028)
            {
                return M68020OpcodeKind.MoveLongAddressDisplacementToData;
            }

            if ((opcode & 0xF1F8) == 0x2068)
            {
                return M68020OpcodeKind.MoveLongAddressDisplacementToAddress;
            }

            if ((opcode & 0xF1F8) == 0x20A8)
            {
                return M68020OpcodeKind.MoveLongAddressDisplacementToAddressIndirect;
            }

            if ((opcode & 0xF1F8) == 0x2168)
            {
                return M68020OpcodeKind.MoveLongAddressDisplacementToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x21A8)
            {
                return M68020OpcodeKind.MoveLongAddressDisplacementToBriefIndexed;
            }

            if ((opcode & 0xF1F8) == 0x11A8)
            {
                return M68020OpcodeKind.MoveByteAddressDisplacementToBriefIndexed;
            }

            if ((opcode & 0xF1F8) == 0x2128)
            {
                return M68020OpcodeKind.MoveLongAddressDisplacementToPredecrement;
            }

            if ((opcode & 0xF1F8) is 0x10E8 or 0x30E8)
            {
                return M68020OpcodeKind.MoveByteOrWordAddressDisplacementToPostIncrement;
            }

            if ((opcode & 0xF1F8) == 0x20E8)
            {
                return M68020OpcodeKind.MoveLongAddressDisplacementToPostIncrement;
            }

            if ((opcode & 0xF1FF) == 0x203A)
            {
                return M68020OpcodeKind.MoveLongPcDisplacementToData;
            }

            if ((opcode & 0xF1FF) == 0x207A)
            {
                return M68020OpcodeKind.MoveLongPcDisplacementToAddress;
            }

            if ((opcode & 0xF1FF) == 0x217A)
            {
                return M68020OpcodeKind.MoveLongPcDisplacementToAddressDisplacement;
            }

            if ((opcode & 0xF1FF) == 0x213A)
            {
                return M68020OpcodeKind.MoveLongPcDisplacementToPredecrement;
            }

            if ((opcode & 0xF1FF) == 0x20FA)
            {
                return M68020OpcodeKind.MoveLongPcDisplacementToPostIncrement;
            }

            if ((opcode & 0xF1F8) == 0x2030)
            {
                return M68020OpcodeKind.MoveLongBriefIndexedToData;
            }

            if ((opcode & 0xF1F8) == 0x2070)
            {
                return M68020OpcodeKind.MoveLongBriefIndexedToAddress;
            }

            if ((opcode & 0xF1FF) == 0x207B)
            {
                return M68020OpcodeKind.MoveLongPcBriefIndexedToAddress;
            }

            if ((opcode & 0xF1F8) == 0x2130)
            {
                return M68020OpcodeKind.MoveLongBriefIndexedToPredecrement;
            }

            if ((opcode & 0xF1F8) == 0x2170)
            {
                return M68020OpcodeKind.MoveLongAddressBriefIndexedToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x21B0)
            {
                return M68020OpcodeKind.MoveLongBriefIndexedToBriefIndexed;
            }

            if ((opcode & 0xF1FF) == 0x217B)
            {
                return M68020OpcodeKind.MoveLongBriefIndexedToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x2090)
            {
                return M68020OpcodeKind.MoveLongAddressIndirectToAddressIndirect;
            }

            if ((opcode & 0xF1F8) == 0x20D0)
            {
                return M68020OpcodeKind.MoveLongAddressIndirectToPostIncrement;
            }

            if ((opcode & 0xF1FF) == 0x2038)
            {
                return M68020OpcodeKind.MoveLongAbsoluteWordToData;
            }

            if ((opcode & 0xF1FF) == 0x2078)
            {
                return M68020OpcodeKind.MoveLongAbsoluteWordToAddress;
            }

            if ((opcode & 0xF1FF) == 0x2039)
            {
                return M68020OpcodeKind.MoveLongAbsoluteLongToData;
            }

            if ((opcode & 0xF1FF) == 0x2079)
            {
                return M68020OpcodeKind.MoveLongAbsoluteLongToAddress;
            }

            if ((opcode & 0xF1FF) == 0x2139)
            {
                return M68020OpcodeKind.MoveLongAbsoluteLongToPredecrement;
            }

            if (opcode == 0x23F8)
            {
                return M68020OpcodeKind.MoveLongAbsoluteWordToAbsoluteLong;
            }

            if ((opcode & 0xFFF8) == 0x21C0)
            {
                return M68020OpcodeKind.MoveLongDataToAbsoluteWord;
            }

            if ((opcode & 0xFFF8) == 0x23C0)
            {
                return M68020OpcodeKind.MoveLongDataToAbsoluteLong;
            }

            if ((opcode & 0xFFF8) == 0x21C8)
            {
                return M68020OpcodeKind.MoveLongAddressToAbsoluteWord;
            }

            if ((opcode & 0xFFF8) == 0x23C8)
            {
                return M68020OpcodeKind.MoveLongAddressToAbsoluteLong;
            }

            if ((opcode & 0xFFF8) == 0x23D0)
            {
                return M68020OpcodeKind.MoveLongAddressIndirectToAbsoluteLong;
            }

            if ((opcode & 0xFFF8) == 0x23E8)
            {
                return M68020OpcodeKind.MoveLongAddressDisplacementToAbsoluteLong;
            }

            if ((opcode & 0xF1F8) == 0x1000)
            {
                return M68020OpcodeKind.MoveByteDataToData;
            }

            if ((opcode & 0xF1F8) == 0x1010)
            {
                return M68020OpcodeKind.MoveByteAddressIndirectToData;
            }

            if ((opcode & 0xF1F8) == 0x1090)
            {
                return M68020OpcodeKind.MoveByteAddressIndirectToAddressIndirect;
            }

            if ((opcode & 0xF1F8) == 0x1018)
            {
                return M68020OpcodeKind.MoveBytePostIncrementToData;
            }

            if ((opcode & 0xF1F8) == 0x1028)
            {
                return M68020OpcodeKind.MoveByteAddressDisplacementToData;
            }

            if ((opcode & 0xF1F8) == 0x1030)
            {
                return M68020OpcodeKind.MoveByteBriefIndexedToData;
            }

            if ((opcode & 0xF1FF) == 0x103B)
            {
                return M68020OpcodeKind.MoveBytePcBriefIndexedToData;
            }

            if ((opcode & 0xF1FF) == 0x1039)
            {
                return M68020OpcodeKind.MoveByteAbsoluteLongToData;
            }

            if ((opcode & 0xF1FF) == 0x3039)
            {
                return M68020OpcodeKind.MoveWordAbsoluteLongToData;
            }

            if ((opcode & 0xF1FF) == 0x3079)
            {
                return M68020OpcodeKind.MoveWordAbsoluteLongToAddress;
            }

            if ((opcode & 0xF1F8) == 0x3050)
            {
                return M68020OpcodeKind.MoveWordAddressIndirectToAddress;
            }

            if ((opcode & 0xF1F8) == 0x3068)
            {
                return M68020OpcodeKind.MoveWordAddressDisplacementToAddress;
            }

            if ((opcode & 0xF1FF) == 0x307B)
            {
                return M68020OpcodeKind.MoveWordPcBriefIndexedToAddress;
            }

            if ((opcode & 0xF1F8) is 0x1160 or 0x2160 or 0x3160)
            {
                return M68020OpcodeKind.MovePredecrementToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) is 0x10E0 or 0x20E0 or 0x30E0)
            {
                return M68020OpcodeKind.MovePredecrementToPostIncrement;
            }

            if ((opcode & 0xF1F8) is 0x10B0 or 0x20B0 or 0x30B0)
            {
                return M68020OpcodeKind.MoveBriefIndexedToAddressIndirect;
            }

            if ((opcode & 0xF1F8) is 0x1098 or 0x2098 or 0x3098)
            {
                return M68020OpcodeKind.MovePostIncrementToAddressIndirect;
            }

            if ((opcode & 0xF1F8) == 0x3090)
            {
                return M68020OpcodeKind.MoveWordAddressIndirectToAddressIndirect;
            }

            if ((opcode & 0xF1F8) == 0x3028)
            {
                return M68020OpcodeKind.MoveWordAddressDisplacementToData;
            }

            if ((opcode & 0xF1F8) == 0x10A8)
            {
                return M68020OpcodeKind.MoveByteAddressDisplacementToAddressIndirect;
            }

            if ((opcode & 0xF1F8) == 0x30A8)
            {
                return M68020OpcodeKind.MoveWordAddressDisplacementToAddressIndirect;
            }

            if ((opcode & 0xF1F8) is 0x1150 or 0x3150)
            {
                return M68020OpcodeKind.MoveWordAddressIndirectToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x3170)
            {
                return M68020OpcodeKind.MoveWordBriefIndexedToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x3030)
            {
                return M68020OpcodeKind.MoveWordAddressBriefIndexedToData;
            }

            if ((opcode & 0xF1FF) == 0x203B)
            {
                return M68020OpcodeKind.MoveLongPcBriefIndexedToData;
            }

            if ((opcode & 0xF1FF) == 0x303B)
            {
                return M68020OpcodeKind.MoveWordPcBriefIndexedToData;
            }

            if ((opcode & 0xF1FF) == 0x313B)
            {
                return M68020OpcodeKind.MoveWordPcBriefIndexedToPredecrement;
            }

            if ((opcode & 0xF1F8) == 0x3018)
            {
                return M68020OpcodeKind.MoveWordPostIncrementToData;
            }

            if ((opcode & 0xF1F8) == 0x3058)
            {
                return M68020OpcodeKind.MoveWordPostIncrementToAddress;
            }

            if ((opcode & 0xF1F8) == 0x30D8)
            {
                return M68020OpcodeKind.MoveWordPostIncrementToPostIncrement;
            }

            if ((opcode & 0xF1F8) == 0x30C0)
            {
                return M68020OpcodeKind.MoveWordDataToPostIncrement;
            }

            if ((opcode & 0xF1F8) == 0x1158)
            {
                return M68020OpcodeKind.MoveBytePostIncrementToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x3158)
            {
                return M68020OpcodeKind.MoveWordPostIncrementToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x31C0)
            {
                return M68020OpcodeKind.MoveWordDataToAbsoluteLong;
            }

            if ((opcode & 0xF1F8) == 0x3140)
            {
                return M68020OpcodeKind.MoveWordDataToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x3180)
            {
                return M68020OpcodeKind.MoveWordDataToBriefIndexed;
            }

            if ((opcode & 0xF1F8) == 0x3148)
            {
                return M68020OpcodeKind.MoveWordAddressToAddressDisplacement;
            }

            if ((opcode & 0xF1FF) == 0x317A)
            {
                return M68020OpcodeKind.MoveWordPcDisplacementToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x3168)
            {
                return M68020OpcodeKind.MoveWordAddressDisplacementToAddressDisplacement;
            }

            if ((opcode & 0xFFF8) == 0x13E8)
            {
                return M68020OpcodeKind.MoveByteAddressDisplacementToAbsoluteLong;
            }

            if ((opcode & 0xFFF8) == 0x33E8)
            {
                return M68020OpcodeKind.MoveWordAddressDisplacementToAbsoluteLong;
            }

            if (opcode == 0x33F9)
            {
                return M68020OpcodeKind.MoveWordAbsoluteLongToAbsoluteLong;
            }

            if ((opcode & 0xF1FF) == 0x3179)
            {
                return M68020OpcodeKind.MoveWordAbsoluteLongToAddressDisplacement;
            }

            if ((opcode & 0xF1FF) == 0x3139)
            {
                return M68020OpcodeKind.MoveWordAbsoluteLongToPredecrement;
            }

            if ((opcode & 0xF1F8) == 0x3118)
            {
                return M68020OpcodeKind.MoveWordPostIncrementToPredecrement;
            }

            if ((opcode & 0xF1F8) == 0x3128)
            {
                return M68020OpcodeKind.MoveWordAddressDisplacementToPredecrement;
            }

            if ((opcode & 0xF1F8) == 0x1128)
            {
                return M68020OpcodeKind.MoveByteAddressDisplacementToPredecrement;
            }

            if ((opcode & 0xF1F8) == 0x11C0)
            {
                return M68020OpcodeKind.MoveByteDataToAbsoluteLong;
            }

            if ((opcode & 0xF1F8) == 0x1080)
            {
                return M68020OpcodeKind.MoveByteDataToAddressIndirect;
            }

            if ((opcode & 0xF1F8) == 0x1140)
            {
                return M68020OpcodeKind.MoveByteDataToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x1168)
            {
                return M68020OpcodeKind.MoveByteAddressDisplacementToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x1170)
            {
                return M68020OpcodeKind.MoveByteBriefIndexedToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x1180)
            {
                return M68020OpcodeKind.MoveByteDataToBriefIndexed;
            }

            if ((opcode & 0xF1F8) == 0x1130)
            {
                return M68020OpcodeKind.MoveByteBriefIndexedToPredecrement;
            }

            if ((opcode & 0xF1F8) == 0x10C0)
            {
                return M68020OpcodeKind.MoveByteDataToPostIncrement;
            }

            if ((opcode & 0xF1F8) == 0x1100)
            {
                return M68020OpcodeKind.MoveByteDataToPredecrement;
            }

            if ((opcode & 0xF1F8) == 0x10D8)
            {
                return M68020OpcodeKind.MoveBytePostIncrementToPostIncrement;
            }

            if ((opcode & 0xF1F8) == 0x11D0)
            {
                return M68020OpcodeKind.MoveByteAddressIndirectToAbsoluteLong;
            }

            if (opcode == 0x13F9)
            {
                return M68020OpcodeKind.MoveByteAbsoluteLongToAbsoluteLong;
            }

            if ((opcode & 0xFF3F) is 0x0039 or 0x0239 && (opcode & 0xC0) != 0xC0)
            {
                return M68020OpcodeKind.ImmediateLogicalByteToAbsoluteLong;
            }

            if ((opcode & 0xFFF8) == 0x0068)
            {
                return M68020OpcodeKind.OriWordImmediateToAddressDisplacement;
            }

            if ((opcode & 0xFFF8) == 0x00A8)
            {
                return M68020OpcodeKind.OriLongImmediateToAddressDisplacement;
            }

            if ((opcode & 0xFFF8) == 0x0050)
            {
                return M68020OpcodeKind.OriWordImmediateToAddressIndirect;
            }

            if ((opcode & 0xFFF8) == 0x0600)
            {
                return M68020OpcodeKind.AddiByteImmediateToData;
            }

            if ((opcode & 0xFFF8) == 0x0610)
            {
                return M68020OpcodeKind.AddiByteImmediateToAddressIndirect;
            }

            if ((opcode & 0xFFF8) == 0x0650)
            {
                return M68020OpcodeKind.AddiWordImmediateToAddressIndirect;
            }

            if ((opcode & 0xFFF8) == 0x0690)
            {
                return M68020OpcodeKind.AddiLongImmediateToAddressIndirect;
            }

            if ((opcode & 0xFFF8) == 0x0668)
            {
                return M68020OpcodeKind.AddiWordImmediateToAddressDisplacement;
            }

            if ((opcode & 0xFFF8) == 0x0628)
            {
                return M68020OpcodeKind.AddiByteImmediateToAddressDisplacement;
            }

            if ((opcode & 0xFFF8) == 0x0640)
            {
                return M68020OpcodeKind.AddiWordImmediateToData;
            }

            if ((opcode & 0xFFF8) == 0x0680)
            {
                return M68020OpcodeKind.AddiLongImmediateToData;
            }

            if ((opcode & 0xFFF8) == 0x06A8)
            {
                return M68020OpcodeKind.AddiLongImmediateToAddressDisplacement;
            }

            if (opcode == 0x06B9)
            {
                return M68020OpcodeKind.AddiLongImmediateToAbsoluteLong;
            }

            if ((opcode & 0xFFF8) == 0x0400)
            {
                return M68020OpcodeKind.SubiByteImmediateToData;
            }

            if ((opcode & 0xFFF8) == 0x0440)
            {
                return M68020OpcodeKind.SubiWordImmediateToData;
            }

            if ((opcode & 0xFFF8) == 0x04A8)
            {
                return M68020OpcodeKind.SubiLongImmediateToAddressDisplacement;
            }

            if ((opcode & 0xFFF8) == 0x0468)
            {
                return M68020OpcodeKind.SubiWordImmediateToAddressDisplacement;
            }

            if ((opcode & 0xFFF8) == 0x0428)
            {
                return M68020OpcodeKind.SubiByteImmediateToAddressDisplacement;
            }

            if ((opcode & 0xFFF8) == 0x0480)
            {
                return M68020OpcodeKind.SubiLongImmediateToData;
            }

            if ((opcode & 0xF1F8) == 0x9000)
            {
                return M68020OpcodeKind.SubByteDataToData;
            }

            if ((opcode & 0xF1F8) == 0x9028)
            {
                return M68020OpcodeKind.SubByteAddressDisplacementToData;
            }

            if ((opcode & 0xF1F8) == 0x9168)
            {
                return M68020OpcodeKind.SubWordDataToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x9128)
            {
                return M68020OpcodeKind.SubByteDataToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x9040)
            {
                return M68020OpcodeKind.SubWordDataToData;
            }

            if ((opcode & 0xFF38) == 0x4428 && ((opcode >> 6) & 3) != 3)
            {
                return M68020OpcodeKind.NegAddressDisplacement;
            }

            if ((opcode & 0xF138) == 0xB030 && ((opcode >> 6) & 3) != 3)
            {
                return M68020OpcodeKind.CmpBriefIndexedToData;
            }

            if ((opcode & 0xF138) == 0x9030 && ((opcode >> 6) & 3) != 3)
            {
                return M68020OpcodeKind.SubBriefIndexedToData;
            }

            if ((opcode & 0xF138) == 0x9110 && ((opcode >> 6) & 3) != 3)
            {
                return M68020OpcodeKind.SubDataToAddressIndirect;
            }

            if ((opcode & 0xF138) == 0x9018 && ((opcode >> 6) & 3) != 3)
            {
                return M68020OpcodeKind.SubPostIncrementToData;
            }

            if ((opcode & 0xF1F8) == 0x9050)
            {
                return M68020OpcodeKind.SubWordAddressIndirectToData;
            }

            if ((opcode & 0xF1F8) == 0x9068)
            {
                return M68020OpcodeKind.SubWordAddressDisplacementToData;
            }

            if ((opcode & 0xF1F8) == 0x9158)
            {
                return M68020OpcodeKind.SubWordDataToPostIncrement;
            }

            if ((opcode & 0xF1F8) == 0x9080)
            {
                return M68020OpcodeKind.SubLongDataToData;
            }

            if ((opcode & 0xF1F8) == 0x9088)
            {
                return M68020OpcodeKind.SubLongAddressToData;
            }

            if ((opcode & 0xF1F8) == 0x90A8)
            {
                return M68020OpcodeKind.SubLongAddressDisplacementToData;
            }

            if ((opcode & 0xF1FF) == 0x90BC)
            {
                return M68020OpcodeKind.SubLongImmediateToData;
            }

            if ((opcode & 0xF1BF) == 0x903C)
            {
                return M68020OpcodeKind.SubImmediateToData;
            }

            if ((opcode & 0xF1F8) == 0x91A8)
            {
                return M68020OpcodeKind.SubLongDataToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0xD000)
            {
                return M68020OpcodeKind.AddByteDataToData;
            }

            if ((opcode & 0xF1F8) == 0xD040)
            {
                return M68020OpcodeKind.AddWordDataToData;
            }

            if ((opcode & 0xF1F8) == 0xD048)
            {
                return M68020OpcodeKind.AddWordAddressToData;
            }

            if ((opcode & 0xF1F8) == 0xD010)
            {
                return M68020OpcodeKind.AddByteAddressIndirectToData;
            }

            if ((opcode & 0xF1F8) == 0xD050)
            {
                return M68020OpcodeKind.AddWordAddressIndirectToData;
            }

            if ((opcode & 0xF1F8) == 0xD060)
            {
                return M68020OpcodeKind.AddWordPredecrementToData;
            }

            if ((opcode & 0xF1F8) == 0xD058)
            {
                return M68020OpcodeKind.AddWordPostIncrementToData;
            }

            if ((opcode & 0xF1F8) == 0xD028)
            {
                return M68020OpcodeKind.AddByteAddressDisplacementToData;
            }

            if ((opcode & 0xF1F8) == 0xD068)
            {
                return M68020OpcodeKind.AddWordAddressDisplacementToData;
            }

            if ((opcode & 0xF1FF) == 0xD07C)
            {
                return M68020OpcodeKind.AddWordImmediateToData;
            }

            if ((opcode & 0xF1FF) == 0xD03C)
            {
                return M68020OpcodeKind.AddByteImmediateToData;
            }

            if ((opcode & 0xF1F8) == 0xD080)
            {
                return M68020OpcodeKind.AddLongDataToData;
            }

            if ((opcode & 0xF138) == 0xD118 && ((opcode >> 6) & 3) != 3)
            {
                return M68020OpcodeKind.AddDataToPostIncrement;
            }

            if ((opcode & 0xF1B8) == 0xD110)
            {
                return M68020OpcodeKind.AddSmallDataToAddressIndirect;
            }

            if ((opcode & 0xF1F8) == 0xD190)
            {
                return M68020OpcodeKind.AddLongDataToAddressIndirect;
            }

            if ((opcode & 0xF13F) == 0xD139 && (opcode & 0xC0) != 0xC0)
            {
                return M68020OpcodeKind.AddLongDataToAbsoluteLong;
            }
            if ((opcode & 0xF13F) == 0x9139 && (opcode & 0xC0) != 0xC0)
            {
                return M68020OpcodeKind.SubDataToAbsoluteLong;
            }

            if ((opcode & 0xF1F8) == 0xD090)
            {
                return M68020OpcodeKind.AddLongAddressIndirectToData;
            }

            if ((opcode & 0xF1F8) is 0xD030 or 0xD070)
            {
                return M68020OpcodeKind.AddByteOrWordBriefIndexedToData;
            }

            if ((opcode & 0xF1F8) == 0xD0B0)
            {
                return M68020OpcodeKind.AddLongBriefIndexedToData;
            }

            if ((opcode & 0xF1FF) == 0xD0B9)
            {
                return M68020OpcodeKind.AddLongAbsoluteLongToData;
            }

            if ((opcode & 0xF1F8) == 0xD088)
            {
                return M68020OpcodeKind.AddLongAddressToData;
            }

            if ((opcode & 0xF1F8) == 0xD180)
            {
                return M68020OpcodeKind.AddxLongDataToData;
            }

            if ((opcode & 0xF1F8) == 0x9180)
            {
                return M68020OpcodeKind.SubxLongDataToData;
            }

            if ((opcode & 0xF1F8) == 0x9100)
            {
                return M68020OpcodeKind.SubxByteDataToData;
            }

            if ((opcode & 0xF1F8) == 0x9140)
            {
                return M68020OpcodeKind.SubxWordDataToData;
            }

            if ((opcode & 0xF1F8) == 0xD100)
            {
                return M68020OpcodeKind.AddxByteDataToData;
            }

            if ((opcode & 0xF1F8) == 0xD140)
            {
                return M68020OpcodeKind.AddxWordDataToData;
            }

            if ((opcode & 0xF1F8) == 0xD018)
            {
                return M68020OpcodeKind.AddBytePostIncrementToData;
            }

            if ((opcode & 0xF1F8) == 0xD098)
            {
                return M68020OpcodeKind.AddLongPostIncrementToData;
            }

            if ((opcode & 0xF1F8) == 0xD0A8)
            {
                return M68020OpcodeKind.AddLongAddressDisplacementToData;
            }

            if ((opcode & 0xF1FF) == 0xD0BA)
            {
                return M68020OpcodeKind.AddLongPcDisplacementToData;
            }

            if ((opcode & 0xF1FF) == 0xD0BC)
            {
                return M68020OpcodeKind.AddLongImmediateToData;
            }

            if ((opcode & 0xF1F8) == 0xD128)
            {
                return M68020OpcodeKind.AddByteDataToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0xD168)
            {
                return M68020OpcodeKind.AddWordDataToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0xD1A8)
            {
                return M68020OpcodeKind.AddLongDataToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x5080)
            {
                return M68020OpcodeKind.AddqLongData;
            }

            if ((opcode & 0xF1F8) == 0x5048)
            {
                return M68020OpcodeKind.AddqWordAddress;
            }

            if ((opcode & 0xF1F8) == 0x5040)
            {
                return M68020OpcodeKind.AddqWordData;
            }

            if ((opcode & 0xF1F8) == 0x5088)
            {
                return M68020OpcodeKind.AddqLongAddress;
            }

            if ((opcode & 0xF1F8) == 0x5090)
            {
                return M68020OpcodeKind.AddqLongAddressIndirect;
            }

            if ((opcode & 0xF1F8) == 0x5028)
            {
                return M68020OpcodeKind.AddqByteAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x5000)
            {
                return M68020OpcodeKind.AddqByteData;
            }

            if ((opcode & 0xF1F8) == 0x5010)
            {
                return M68020OpcodeKind.AddqByteAddressIndirect;
            }

            if ((opcode & 0xF0F8) == 0x5070)
            {
                return M68020OpcodeKind.QuickWordBriefIndexed;
            }

            if ((opcode & 0xF1F8) == 0x5068)
            {
                return M68020OpcodeKind.AddqWordAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x50A8)
            {
                return M68020OpcodeKind.AddqLongAddressDisplacement;
            }

            if ((opcode & 0xF13F) == 0x5139 && ((opcode >> 6) & 3) != 3)
            {
                return M68020OpcodeKind.SubqAbsoluteLong;
            }

            if ((opcode & 0xF1FF) == 0x50B9)
            {
                return M68020OpcodeKind.AddqLongAbsoluteLong;
            }

            if ((opcode & 0xF1F8) == 0x5100)
            {
                return M68020OpcodeKind.SubqByteData;
            }

            if ((opcode & 0xF1F8) == 0x5140)
            {
                return M68020OpcodeKind.SubqWordData;
            }

            if ((opcode & 0xF1F8) == 0x5180)
            {
                return M68020OpcodeKind.SubqLongData;
            }

            if ((opcode & 0xF1F8) == 0x5148)
            {
                return M68020OpcodeKind.SubqWordAddress;
            }

            if ((opcode & 0xF1F8) == 0x5188)
            {
                return M68020OpcodeKind.SubqLongAddress;
            }

            if ((opcode & 0xF1F8) == 0x5190)
            {
                return M68020OpcodeKind.SubqLongAddressIndirect;
            }

            if ((opcode & 0xF1F8) == 0x51A8)
            {
                return M68020OpcodeKind.SubqLongAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x5128)
            {
                return M68020OpcodeKind.SubqByteAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x5168)
            {
                return M68020OpcodeKind.SubqWordAddressDisplacement;
            }

            if ((opcode & 0xF1FF) == 0xD0FC)
            {
                return M68020OpcodeKind.AddaWordImmediateToAddress;
            }

            if ((opcode & 0xF1F8) == 0xD0C0)
            {
                return M68020OpcodeKind.AddaWordDataToAddress;
            }

            if ((opcode & 0xF1F8) == 0xD0C8)
            {
                return M68020OpcodeKind.AddaWordAddressToAddress;
            }

            if ((opcode & 0xF1F8) == 0xD0E8)
            {
                return M68020OpcodeKind.AddaWordAddressDisplacementToAddress;
            }

            if ((opcode & 0xF1F8) == 0xD1F0)
            {
                return M68020OpcodeKind.AddaLongBriefIndexedToAddress;
            }

            if ((opcode & 0xF1F8) == 0xD0F0)
            {
                return M68020OpcodeKind.AddaWordBriefIndexedToAddress;
            }

            if ((opcode & 0xF1FF) == 0xD1FC)
            {
                return M68020OpcodeKind.AddaLongImmediateToAddress;
            }

            if ((opcode & 0xF1F8) == 0xD1C0)
            {
                return M68020OpcodeKind.AddaLongDataToAddress;
            }

            if ((opcode & 0xF1F8) == 0xD1C8)
            {
                return M68020OpcodeKind.AddaLongAddressToAddress;
            }

            if ((opcode & 0xF1F8) == 0xD1E8)
            {
                return M68020OpcodeKind.AddaLongAddressDisplacementToAddress;
            }

            if ((opcode & 0xF0F8) == 0x90D0)
            {
                return M68020OpcodeKind.SubaAddressIndirectToAddress;
            }

            if ((opcode & 0xF1F8) is 0x90D8 or 0x91D8)
            {
                return M68020OpcodeKind.SubaPostIncrementToAddress;
            }

            if ((opcode & 0xF1FF) == 0x91FC)
            {
                return M68020OpcodeKind.SubaLongImmediateToAddress;
            }

            if ((opcode & 0xF1F8) == 0x91C0)
            {
                return M68020OpcodeKind.SubaLongDataToAddress;
            }

            if ((opcode & 0xF1F8) == 0x91C8)
            {
                return M68020OpcodeKind.SubaLongAddressToAddress;
            }

            if ((opcode & 0xF1F8) == 0x90E8)
            {
                return M68020OpcodeKind.SubaWordAddressDisplacementToAddress;
            }

            if ((opcode & 0xF1F8) == 0x91E8)
            {
                return M68020OpcodeKind.SubaLongAddressDisplacementToAddress;
            }

            if ((opcode & 0xF1FF) == 0x91FA)
            {
                return M68020OpcodeKind.SubaLongPcDisplacementToAddress;
            }

            if ((opcode & 0xF1FF) == 0x90FC)
            {
                return M68020OpcodeKind.SubaWordImmediateToAddress;
            }

            if ((opcode & 0xF1F8) == 0x90C0)
            {
                return M68020OpcodeKind.SubaWordDataToAddress;
            }

            if ((opcode & 0xF1FF) == 0x41BC)
            {
                return M68020OpcodeKind.ChkWordImmediate;
            }

            if ((opcode & 0xFFC0) is 0x4C00 or 0x4C40)
            {
                return M68020OpcodeKind.LongMultiplyDivide;
            }

            if (opcode is 0x0CFC or 0x0EFC)
            {
                return M68020OpcodeKind.Cas2;
            }

            if ((opcode & 0xFFC0) is 0x0AC0 or 0x0CC0 or 0x0EC0)
            {
                return M68020OpcodeKind.Cas;
            }

            if ((opcode & 0xF1C0) == 0x80C0)
            {
                return M68020OpcodeKind.DivideWordUnsigned;
            }

            if ((opcode & 0xF1C0) == 0x81C0)
            {
                return M68020OpcodeKind.DivideWordSigned;
            }

            if ((opcode & 0xFFF8) == 0x0240)
            {
                return M68020OpcodeKind.AndiWordImmediateToData;
            }

            if ((opcode & 0xFFF8) == 0x0268)
            {
                return M68020OpcodeKind.AndiWordImmediateToAddressDisplacement;
            }

            if ((opcode & 0xFFF8) == 0x02A8)
            {
                return M68020OpcodeKind.AndiLongImmediateToAddressDisplacement;
            }

            if ((opcode & 0xFFF8) == 0x0200)
            {
                return M68020OpcodeKind.AndiByteImmediateToData;
            }

            if ((opcode & 0xFFF8) is 0x0250 or 0x0290)
            {
                return M68020OpcodeKind.AndiWideImmediateToAddressIndirect;
            }

            if ((opcode & 0xFFF8) == 0x0210)
            {
                return M68020OpcodeKind.AndiByteImmediateToAddressIndirect;
            }

            if ((opcode & 0xFFF8) == 0x0228)
            {
                return M68020OpcodeKind.AndiByteImmediateToAddressDisplacement;
            }

            if ((opcode & 0xF1FF) == 0xC03C)
            {
                return M68020OpcodeKind.AndByteImmediateToData;
            }

            if ((opcode & 0xF1FF) == 0xC07C)
            {
                return M68020OpcodeKind.AndWordImmediateToData;
            }

            if ((opcode & 0xFFF8) == 0x0280)
            {
                return M68020OpcodeKind.AndLongImmediateToData;
            }

            if ((opcode & 0xF1C0) == 0xC080)
            {
                var mode = (opcode >> 3) & 7;
                var register = opcode & 7;
                return mode != 1 && (mode != 7 || register <= 4)
                    ? M68020OpcodeKind.AndLongEffectiveAddressToData
                    : M68020OpcodeKind.IllegalInstruction;
            }

            if ((opcode & 0xF1C0) == 0x8080)
            {
                var mode = (opcode >> 3) & 7;
                var register = opcode & 7;
                return mode != 1 && (mode != 7 || register <= 4)
                    ? M68020OpcodeKind.OrLongEffectiveAddressToData
                    : M68020OpcodeKind.IllegalInstruction;
            }

            if ((opcode & 0xF1FF) == 0x807C)
            {
                return M68020OpcodeKind.OrWordImmediateToData;
            }

            if ((opcode & 0xF1FF) == 0x803C)
            {
                return M68020OpcodeKind.OrByteImmediateToData;
            }

            if ((opcode & 0xF1F8) == 0x8068)
            {
                return M68020OpcodeKind.OrWordAddressDisplacementToData;
            }

            if ((opcode & 0xFFF8) == 0x0040)
            {
                return M68020OpcodeKind.OriWordImmediateToData;
            }
            if ((opcode & 0xFFF8) is 0x0080 or 0x0280 or 0x0A00)
            {
                return M68020OpcodeKind.ImmediateLogicalData;
            }

            if ((opcode & 0xFFF8) == 0x0028)
            {
                return M68020OpcodeKind.OriByteImmediateToAddressDisplacement;
            }

            if ((opcode & 0xFFF8) == 0x0010)
            {
                return M68020OpcodeKind.OriByteImmediateToAddressIndirect;
            }

            if ((opcode & 0xFFF8) == 0x0A40)
            {
                return M68020OpcodeKind.EoriWordImmediateToData;
            }

            if ((opcode & 0xFFF8) == 0x0A80)
            {
                return M68020OpcodeKind.EoriLongImmediateToData;
            }

            if ((opcode & 0xF1F8) == 0xB1A8)
            {
                return M68020OpcodeKind.EorLongDataToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0xB180)
            {
                return M68020OpcodeKind.EorLongDataToData;
            }

            if ((opcode & 0xF1F8) == 0xB128)
            {
                return M68020OpcodeKind.EorByteDataToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0xB100)
            {
                return M68020OpcodeKind.EorByteDataToData;
            }

            if ((opcode & 0xF1F8) == 0xB140)
            {
                return M68020OpcodeKind.EorWordDataToData;
            }

            if ((opcode & 0xF1C0) == 0xC0C0)
            {
                return M68020OpcodeKind.MultiplyWordUnsigned;
            }

            if ((opcode & 0xF1C0) == 0xC1C0)
            {
                return M68020OpcodeKind.MultiplyWordSigned;
            }

            if ((opcode & 0xF1F8) == 0xC000)
            {
                return M68020OpcodeKind.AndByteDataToData;
            }

            if ((opcode & 0xF1F8) == 0xC028)
            {
                return M68020OpcodeKind.AndByteAddressDisplacementToData;
            }

            if ((opcode & 0xF1F8) == 0xC128)
            {
                return M68020OpcodeKind.AndByteDataToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x8000)
            {
                return M68020OpcodeKind.OrByteDataToData;
            }

            if ((opcode & 0xF1F8) == 0x8040)
            {
                return M68020OpcodeKind.OrWordDataToData;
            }

            if ((opcode & 0xF138) == 0xC030 && ((opcode >> 6) & 3) != 3)
            {
                return M68020OpcodeKind.AndBriefIndexedToData;
            }

            if ((opcode & 0xFF38) == 0x0218 && ((opcode >> 6) & 3) != 3)
            {
                return M68020OpcodeKind.AndiImmediateToPostIncrement;
            }

            if ((opcode & 0xF138) == 0xC118 && ((opcode >> 6) & 3) != 3)
            {
                return M68020OpcodeKind.AndDataToPostIncrement;
            }

            if ((opcode & 0xF13F) == 0x8139 && ((opcode >> 6) & 3) != 3)
            {
                return M68020OpcodeKind.OrDataToAbsoluteLong;
            }

            if ((opcode & 0xF138) == 0x8118 && ((opcode >> 6) & 3) != 3)
            {
                return M68020OpcodeKind.OrDataToPostIncrement;
            }

            if ((opcode & 0xF1F8) == 0x8150)
            {
                return M68020OpcodeKind.OrWordDataToAddressIndirect;
            }

            if ((opcode & 0xF1F8) == 0x8168)
            {
                return M68020OpcodeKind.OrWordDataToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x8028)
            {
                return M68020OpcodeKind.OrByteAddressDisplacementToData;
            }

            if ((opcode & 0xF1F8) == 0x8110)
            {
                return M68020OpcodeKind.OrByteDataToAddressIndirect;
            }

            if ((opcode & 0xF1F8) == 0x8128)
            {
                return M68020OpcodeKind.OrByteDataToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x8190)
            {
                return M68020OpcodeKind.OrLongDataToAddressIndirect;
            }

            if ((opcode & 0xF1F8) == 0x81A8)
            {
                return M68020OpcodeKind.OrLongDataToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0xC040)
            {
                return M68020OpcodeKind.AndWordDataToData;
            }

            if ((opcode & 0xF1B8) == 0xC010 || (opcode & 0xF1F8) == 0xC090)
            {
                return M68020OpcodeKind.AndAddressIndirectToData;
            }

            if ((opcode & 0xF1F8) == 0xC068)
            {
                return M68020OpcodeKind.AndWordAddressDisplacementToData;
            }

            if ((opcode & 0xF1F8) == 0xC168)
            {
                return M68020OpcodeKind.AndWordDataToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0xC1A8)
            {
                return M68020OpcodeKind.AndLongDataToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0xC188)
            {
                return M68020OpcodeKind.ExgDataAddress;
            }

            if ((opcode & 0xF1F8) == 0xC140)
            {
                return M68020OpcodeKind.ExgDataData;
            }

            if ((opcode & 0xF1F8) == 0xC148)
            {
                return M68020OpcodeKind.ExgAddressAddress;
            }

            if ((opcode & 0xF1F0) == 0xC100)
            {
                return M68020OpcodeKind.BcdByteAdd;
            }

            if ((opcode & 0xF1F0) == 0x8100)
            {
                return M68020OpcodeKind.BcdByteSubtract;
            }

            if ((opcode & 0xFFF8) == 0x0000)
            {
                return M68020OpcodeKind.OriByteImmediateToData;
            }

            if ((opcode & 0xFF38) == 0x0830)
            {
                return M68020OpcodeKind.BitImmediateBriefIndexed;
            }

            if (opcode == 0x0839)
            {
                return M68020OpcodeKind.BtstByteImmediateAbsoluteLong;
            }

            if ((opcode & 0xFFF8) == 0x0810)
            {
                return M68020OpcodeKind.BtstByteImmediateAddressIndirect;
            }

            if ((opcode & 0xFFF8) == 0x0818)
            {
                return M68020OpcodeKind.BtstByteImmediatePostIncrement;
            }

            if ((opcode & 0xFFF8) == 0x0828)
            {
                return M68020OpcodeKind.BtstByteImmediateAddressDisplacement;
            }

            if (opcode == 0x0879)
            {
                return M68020OpcodeKind.BchgByteImmediateAbsoluteLong;
            }

            if ((opcode & 0xFFF8) == 0x0868)
            {
                return M68020OpcodeKind.BchgByteImmediateAddressDisplacement;
            }

            if (opcode == 0x08B9)
            {
                return M68020OpcodeKind.BclrByteImmediateAbsoluteLong;
            }

            if ((opcode & 0xFFF8) == 0x08A8)
            {
                return M68020OpcodeKind.BclrByteImmediateAddressDisplacement;
            }

            if (opcode == 0x08F9)
            {
                return M68020OpcodeKind.BsetByteImmediateAbsoluteLong;
            }

            if ((opcode & 0xFFF8) == 0x08E8)
            {
                return M68020OpcodeKind.BsetByteImmediateAddressDisplacement;
            }

            if ((opcode & 0xF1F8) is 0x0168 or 0x01A8)
            {
                return M68020OpcodeKind.BitModifyDynamicAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x01E8)
            {
                return M68020OpcodeKind.BsetByteDynamicAddressDisplacement;
            }

            if ((opcode & 0xFFF8) == 0x0800)
            {
                return M68020OpcodeKind.BtstImmediateData;
            }

            if ((opcode & 0xF1F8) == 0x0100)
            {
                return M68020OpcodeKind.BtstDynamicData;
            }

            if ((opcode & 0xF1F8) == 0x0110)
            {
                return M68020OpcodeKind.BtstByteDynamicAddressIndirect;
            }

            if ((opcode & 0xF1F8) == 0x0128)
            {
                return M68020OpcodeKind.BtstByteDynamicAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x0130)
            {
                return M68020OpcodeKind.BtstByteDynamicBriefIndexed;
            }

            if ((opcode & 0xF1FF) == 0x0139)
            {
                return M68020OpcodeKind.BtstByteDynamicAbsoluteLong;
            }

            if ((opcode & 0xF1F8) == 0x0150)
            {
                return M68020OpcodeKind.BchgByteDynamicAddressIndirect;
            }

            if ((opcode & 0xFFF8) == 0x08C0)
            {
                return M68020OpcodeKind.BsetImmediateData;
            }

            if ((opcode & 0xF1F8) == 0x01C0)
            {
                return M68020OpcodeKind.BsetDynamicData;
            }

            if ((opcode & 0xFFF8) == 0x0880)
            {
                return M68020OpcodeKind.BclrImmediateData;
            }

            if ((opcode & 0xF1F8) == 0x0180)
            {
                return M68020OpcodeKind.BclrDynamicData;
            }

            if ((opcode & 0xFFF8) == 0x4840)
            {
                return M68020OpcodeKind.SwapData;
            }

            if ((opcode & 0xFFF8) == 0x48C0)
            {
                return M68020OpcodeKind.ExtLongData;
            }

            if ((opcode & 0xFFF8) == 0x4880)
            {
                return M68020OpcodeKind.ExtWordData;
            }

            if ((opcode & 0xFFF8) == 0x4A00)
            {
                return M68020OpcodeKind.TstByteData;
            }

            if ((opcode & 0xFFF8) == 0x4A40)
            {
                return M68020OpcodeKind.TstWordData;
            }

            if (opcode == 0x4A79)
            {
                return M68020OpcodeKind.TstWordAbsoluteLong;
            }

            if (opcode == 0x4A39)
            {
                return M68020OpcodeKind.TstByteAbsoluteLong;
            }

            if ((opcode & 0xFFF8) == 0x4A28)
            {
                return M68020OpcodeKind.TstByteAddressDisplacement;
            }

            if ((opcode & 0xFFF8) == 0x4A70)
            {
                return M68020OpcodeKind.TstWordBriefIndexed;
            }

            if ((opcode & 0xFFF8) == 0x4A30)
            {
                return M68020OpcodeKind.TstByteBriefIndexed;
            }

            if ((opcode & 0xFFF8) == 0x4A50)
            {
                return M68020OpcodeKind.TstWordAddressIndirect;
            }

            if ((opcode & 0xFFF8) == 0x4A10)
            {
                return M68020OpcodeKind.TstByteAddressIndirect;
            }

            if ((opcode & 0xFFF8) is 0x4A58 or 0x4A98)
            {
                return M68020OpcodeKind.TstWordOrLongPostIncrement;
            }

            if ((opcode & 0xFFF8) == 0x4A18)
            {
                return M68020OpcodeKind.TstBytePostIncrement;
            }

            if ((opcode & 0xFFF8) == 0x4A68)
            {
                return M68020OpcodeKind.TstWordAddressDisplacement;
            }

            if ((opcode & 0xFFF8) == 0x4A80)
            {
                return M68020OpcodeKind.TstLongData;
            }

            if ((opcode & 0xFFF8) == 0x4A90)
            {
                return M68020OpcodeKind.TstLongAddressIndirect;
            }

            if ((opcode & 0xFFF8) == 0x4AA8)
            {
                return M68020OpcodeKind.TstLongAddressDisplacement;
            }

            if ((opcode & 0xFFF8) == 0x4AB0)
            {
                return M68020OpcodeKind.TstLongBriefIndexed;
            }

            if ((opcode & 0xF8C0) == 0xE8C0)
            {
                return M68020OpcodeKind.BitField;
            }

            if ((opcode & 0xF1F8) == 0xE048)
            {
                return M68020OpcodeKind.LsrWordImmediateData;
            }

            if ((opcode & 0xF1F8) == 0xE028)
            {
                return M68020OpcodeKind.LsrByteRegisterData;
            }

            if ((opcode & 0xF1F8) == 0xE068)
            {
                return M68020OpcodeKind.LsrWordRegisterData;
            }

            if ((opcode & 0xF1F8) == 0xE008)
            {
                return M68020OpcodeKind.LsrByteImmediateData;
            }

            if ((opcode & 0xF1F8) == 0xE000)
            {
                return M68020OpcodeKind.AsrByteImmediateData;
            }

            if ((opcode & 0xF1F8) == 0xE080)
            {
                return M68020OpcodeKind.AsrLongImmediateData;
            }

            if ((opcode & 0xF1F8) == 0xE0A0)
            {
                return M68020OpcodeKind.AsrLongRegisterData;
            }

            if ((opcode & 0xF1F8) == 0xE040)
            {
                return M68020OpcodeKind.AsrWordImmediateData;
            }

            if ((opcode & 0xF1F8) == 0xE088)
            {
                return M68020OpcodeKind.LsrLongImmediateData;
            }

            if ((opcode & 0xF1F8) == 0xE0A8)
            {
                return M68020OpcodeKind.LsrLongRegisterData;
            }

            if ((opcode & 0xF1F8) == 0xE180)
            {
                return M68020OpcodeKind.AslLongImmediateData;
            }

            if ((opcode & 0xF1F8) == 0xE1A0)
            {
                return M68020OpcodeKind.AslLongRegisterData;
            }

            if ((opcode & 0xF1F8) == 0xE120)
            {
                return M68020OpcodeKind.AslByteRegisterData;
            }

            if ((opcode & 0xF1F8) == 0xE140)
            {
                return M68020OpcodeKind.AslWordImmediateData;
            }

            if ((opcode & 0xF1F8) == 0xE188)
            {
                return M68020OpcodeKind.LslLongImmediateData;
            }

            if ((opcode & 0xF1F8) == 0xE1A8)
            {
                return M68020OpcodeKind.LslLongRegisterData;
            }

            if ((opcode & 0xF1F8) == 0xE168)
            {
                return M68020OpcodeKind.LslWordRegisterData;
            }

            if ((opcode & 0xF1F8) == 0xE108)
            {
                return M68020OpcodeKind.LslByteImmediateData;
            }

            if ((opcode & 0xF1F8) == 0xE148)
            {
                return M68020OpcodeKind.LslWordImmediateData;
            }

            if ((opcode & 0xF1F8) == 0xE018)
            {
                return M68020OpcodeKind.RorByteImmediateData;
            }

            if ((opcode & 0xF1F8) == 0xE058)
            {
                return M68020OpcodeKind.RorWordImmediateData;
            }

            if ((opcode & 0xF1F8) == 0xE098)
            {
                return M68020OpcodeKind.RorLongImmediateData;
            }

            if ((opcode & 0xF1F8) == 0xE158)
            {
                return M68020OpcodeKind.RolWordImmediateData;
            }

            if ((opcode & 0xF1F8) == 0xE198)
            {
                return M68020OpcodeKind.RolLongImmediateData;
            }

            if ((opcode & 0xFFF8) == 0xE2E8)
            {
                return M68020OpcodeKind.LsrWordAddressDisplacement;
            }

            if ((opcode & 0xFFF8) == 0x0C00)
            {
                return M68020OpcodeKind.CmpiByteImmediateToData;
            }

            if ((opcode & 0xFFF8) == 0x0C10)
            {
                return M68020OpcodeKind.CmpiByteImmediateToAddressIndirect;
            }

            if ((opcode & 0xFFF8) == 0x0C20)
            {
                return M68020OpcodeKind.CmpiByteImmediateToPredecrement;
            }

            if ((opcode & 0xFFF8) == 0x0C28)
            {
                return M68020OpcodeKind.CmpiByteImmediateToAddressDisplacement;
            }

            if ((opcode & 0xFFF8) == 0x0C40)
            {
                return M68020OpcodeKind.CmpiWordImmediateToData;
            }

            if ((opcode & 0xFFF8) == 0x0C50)
            {
                return M68020OpcodeKind.CmpiWordImmediateToAddressIndirect;
            }

            if ((opcode & 0xFFF8) == 0x0C68)
            {
                return M68020OpcodeKind.CmpiWordImmediateToAddressDisplacement;
            }

            if (opcode == 0x0C39)
            {
                return M68020OpcodeKind.CmpiByteImmediateToAbsoluteLong;
            }

            if (opcode == 0x0C79)
            {
                return M68020OpcodeKind.CmpiWordImmediateToAbsoluteLong;
            }

            if ((opcode & 0xFFF8) == 0x0C80)
            {
                return M68020OpcodeKind.CmpiLongImmediateToData;
            }

            if ((opcode & 0xFFB8) == 0x0C18)
            {
                return M68020OpcodeKind.CmpiSmallImmediateToPostIncrement;
            }

            if ((opcode & 0xFFF8) == 0x0C98)
            {
                return M68020OpcodeKind.CmpiLongImmediateToPostIncrement;
            }

            if ((opcode & 0xFFF8) == 0x0C90)
            {
                return M68020OpcodeKind.CmpiLongImmediateToAddressIndirect;
            }

            if ((opcode & 0xFFF8) == 0x0CA8)
            {
                return M68020OpcodeKind.CmpiLongImmediateToAddressDisplacement;
            }

            if ((opcode & 0xF1FF) == 0xB1FC)
            {
                return M68020OpcodeKind.CmpaLongImmediateToAddress;
            }

            if ((opcode & 0xF1FF) == 0xB0FC)
            {
                return M68020OpcodeKind.CmpaWordImmediateToAddress;
            }

            if ((opcode & 0xF1F8) == 0xB0C0)
            {
                return M68020OpcodeKind.CmpaWordDataToAddress;
            }

            if ((opcode & 0xF1F8) == 0xB0C8)
            {
                return M68020OpcodeKind.CmpaWordAddressToAddress;
            }

            if ((opcode & 0xF1F8) == 0xB1C0)
            {
                return M68020OpcodeKind.CmpaLongDataToAddress;
            }

            if ((opcode & 0xF1F8) == 0xB1C8)
            {
                return M68020OpcodeKind.CmpaLongAddressToAddress;
            }

            if ((opcode & 0xF1F8) == 0xB1D0)
            {
                return M68020OpcodeKind.CmpaLongAddressIndirectToAddress;
            }

            if ((opcode & 0xF1F8) == 0xB0E8)
            {
                return M68020OpcodeKind.CmpaWordAddressDisplacementToAddress;
            }

            if ((opcode & 0xF1F8) == 0xB1E8)
            {
                return M68020OpcodeKind.CmpaLongAddressDisplacementToAddress;
            }

            if ((opcode & 0xF1F8) == 0xB1E0)
            {
                return M68020OpcodeKind.CmpaLongPredecrementToAddress;
            }

            if ((opcode & 0xF1F8) == 0xB1D8)
            {
                return M68020OpcodeKind.CmpaLongPostIncrementToAddress;
            }

            if ((opcode & 0xF1F8) == 0xB080)
            {
                return M68020OpcodeKind.CmpLongDataToData;
            }

            if ((opcode & 0xF1FF) == 0xB0BC)
            {
                return M68020OpcodeKind.CmpLongImmediateToData;
            }

            if ((opcode & 0xF1FF) is 0xB0B9 or 0xB079)
            {
                return M68020OpcodeKind.CmpLongAbsoluteLongToData;
            }

            if ((opcode & 0xF1F8) == 0xB088)
            {
                return M68020OpcodeKind.CmpLongAddressToData;
            }

            if ((opcode & 0xF1F8) == 0xB090)
            {
                return M68020OpcodeKind.CmpLongAddressIndirectToData;
            }

            if ((opcode & 0xF1F8) == 0xB098)
            {
                return M68020OpcodeKind.CmpLongPostIncrementToData;
            }

            if ((opcode & 0xF1F8) == 0xB0A8)
            {
                return M68020OpcodeKind.CmpLongAddressDisplacementToData;
            }

            if ((opcode & 0xF1F8) == 0xB000)
            {
                return M68020OpcodeKind.CmpByteDataToData;
            }

            if ((opcode & 0xF1FF) == 0xB03C)
            {
                return M68020OpcodeKind.CmpByteImmediateToData;
            }

            if ((opcode & 0xF1F8) == 0xB010)
            {
                return M68020OpcodeKind.CmpByteAddressIndirectToData;
            }

            if ((opcode & 0xF1F8) == 0xB018)
            {
                return M68020OpcodeKind.CmpBytePostIncrementToData;
            }

            if ((opcode & 0xF1F8) == 0xB028)
            {
                return M68020OpcodeKind.CmpByteAddressDisplacementToData;
            }

            if ((opcode & 0xF1FF) == 0xB039)
            {
                return M68020OpcodeKind.CmpByteAbsoluteLongToData;
            }

            if ((opcode & 0xF1F8) == 0xB040)
            {
                return M68020OpcodeKind.CmpWordDataToData;
            }

            if ((opcode & 0xF1F8) == 0xB048)
            {
                return M68020OpcodeKind.CmpWordAddressToData;
            }

            if ((opcode & 0xF1FF) == 0xB07C)
            {
                return M68020OpcodeKind.CmpWordImmediateToData;
            }

            if ((opcode & 0xF1F8) == 0xB050)
            {
                return M68020OpcodeKind.CmpWordAddressIndirectToData;
            }

            if ((opcode & 0xF1F8) == 0xB058)
            {
                return M68020OpcodeKind.CmpWordPostIncrementToData;
            }

            if ((opcode & 0xF1F8) == 0xB068)
            {
                return M68020OpcodeKind.CmpWordAddressDisplacementToData;
            }

            if ((opcode & 0xF0F8) == 0xD0D8)
            {
                return M68020OpcodeKind.AddaPostIncrementToAddress;
            }

            if ((opcode & 0xFFF8) == 0x33D0)
            {
                return M68020OpcodeKind.MoveWordAddressIndirectToAbsoluteLong;
            }

            if ((opcode & 0xF1FF) is 0x10F9 or 0x20F9 or 0x30F9)
            {
                return M68020OpcodeKind.MoveWordAbsoluteLongToPostIncrement;
            }

            if (opcode == 0x33FA)
            {
                return M68020OpcodeKind.MoveWordPcDisplacementToAbsoluteLong;
            }

            if (opcode == 0x23F9)
            {
                return M68020OpcodeKind.MoveLongAbsoluteLongToAbsoluteLong;
            }

            if ((opcode & 0xF1FF) == 0x317B)
            {
                return M68020OpcodeKind.MoveWordPcBriefIndexedToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x31A8)
            {
                return M68020OpcodeKind.MoveWordAddressDisplacementToBriefIndexed;
            }

            if ((opcode & 0xF1FF) == 0xD039 || (opcode & 0xF1FF) == 0xD079)
            {
                return M68020OpcodeKind.AddSmallAbsoluteLongToData;
            }

            if ((opcode & 0xF13F) == 0x9039 && ((opcode >> 6) & 3) != 3)
            {
                return M68020OpcodeKind.SubAbsoluteLongToData;
            }

            if ((opcode & 0xF1F8) == 0x3070)
            {
                return M68020OpcodeKind.MoveWordBriefIndexedToAddress;
            }

            if ((opcode & 0xF1F8) == 0x30C8)
            {
                return M68020OpcodeKind.MoveWordAddressToPostIncrement;
            }

            if (opcode == 0x0639 || opcode == 0x0679)
            {
                return M68020OpcodeKind.AddiSmallImmediateToAbsoluteLong;
            }

            if ((opcode & 0xFF3F) == 0x0439 && (opcode & 0xC0) != 0xC0)
            {
                return M68020OpcodeKind.SubiImmediateToAbsoluteLong;
            }

            if ((opcode & 0xF1F8) == 0x01D0 || (opcode & 0xF1F8) == 0x0190)
            {
                return M68020OpcodeKind.BitDynamicAddressIndirect;
            }

            if ((opcode & 0xFFF8) == 0x4C90)
            {
                return M68020OpcodeKind.MovemWordAddressIndirectToRegisters;
            }
            if ((opcode & 0xFFB8) == 0x4CB0 || opcode is 0x4CBB or 0x4CFB)
            {
                return M68020OpcodeKind.MovemIndexedToRegisters;
            }

            if (opcode == 0x4279)
            {
                return M68020OpcodeKind.ClrWordAbsoluteLong;
            }

            if ((opcode & 0xFF3F) == 0x4639 && ((opcode >> 6) & 3) != 3)
            {
                return M68020OpcodeKind.NotAbsoluteLong;
            }

            if ((opcode & 0xFF38) == 0x4610 && ((opcode >> 6) & 3) != 3)
            {
                return M68020OpcodeKind.NotAddressIndirect;
            }

            if ((opcode & 0xFF38) == 0x4618 && ((opcode >> 6) & 3) != 3)
            {
                return M68020OpcodeKind.NotPostIncrement;
            }

            if (opcode == 0x4AB9)
            {
                return M68020OpcodeKind.TstLongAbsoluteLong;
            }

            if ((opcode & 0xF0F8) == 0xD0D0)
            {
                return M68020OpcodeKind.AddaAddressIndirectToAddress;
            }

            if ((opcode & 0xF0FF) == 0xD0F9)
            {
                return M68020OpcodeKind.AddaAbsoluteLongToAddress;
            }
            if ((opcode & 0xF0FF) == 0x90F9)
            {
                return M68020OpcodeKind.SubaAbsoluteLongToAddress;
            }

            if ((opcode & 0xF1FF) == 0xD03A || (opcode & 0xF1FF) == 0xD07A)
            {
                return M68020OpcodeKind.AddSmallPcDisplacementToData;
            }

            if ((opcode & 0xF13F) == 0x903A && ((opcode >> 6) & 3) != 3)
            {
                return M68020OpcodeKind.SubPcDisplacementToData;
            }

            if ((opcode & 0xF13F) == 0xD03B && ((opcode >> 6) & 3) != 3)
            {
                return M68020OpcodeKind.AddPcBriefIndexedToData;
            }

            if ((opcode & 0xF13F) == 0x903B && ((opcode >> 6) & 3) != 3)
            {
                return M68020OpcodeKind.SubPcBriefIndexedToData;
            }

            if ((opcode & 0xF1FF) == 0x5079)
            {
                return M68020OpcodeKind.AddqWordAbsoluteLong;
            }

            if ((opcode & 0xF1FF) == 0xC03B || (opcode & 0xF1FF) == 0xC07B)
            {
                return M68020OpcodeKind.AndPcBriefIndexedToData;
            }

            if ((opcode & 0xF1FF) == 0xC039 || (opcode & 0xF1FF) == 0xC079)
            {
                return M68020OpcodeKind.AndSmallAbsoluteLongToData;
            }

            if ((opcode & 0xF1F8) == 0xB108)
            {
                return M68020OpcodeKind.CmpmBytePostIncrement;
            }

            if ((opcode & 0xF1F8) == 0xB148)
            {
                return M68020OpcodeKind.CmpmWordPostIncrement;
            }

            if ((opcode & 0xF1F8) == 0xB188)
            {
                return M68020OpcodeKind.CmpmLongPostIncrement;
            }

            if (opcode == 0x0CB9)
            {
                return M68020OpcodeKind.CmpiLongImmediateToAbsoluteLong;
            }

            if (opcode == 0x0CB8)
            {
                return M68020OpcodeKind.CmpiLongImmediateToAbsoluteWord;
            }

            if (opcode == 0x4E71)
            {
                return M68020OpcodeKind.Nop;
            }

            if (opcode == 0x4E70)
            {
                return M68020OpcodeKind.Reset;
            }

            if (opcode == 0x4E72)
            {
                return M68020OpcodeKind.Stop;
            }

            if (opcode is 0x4E7A or 0x4E7B)
            {
                return M68020OpcodeKind.Movec;
            }

            if ((opcode & 0xFFF0) == 0x4E60)
            {
                return M68020OpcodeKind.MoveUsp;
            }

            if ((opcode & 0xFFF0) == 0x4E40)
            {
                return M68020OpcodeKind.Trap;
            }

            if (opcode == 0x4E73)
            {
                return M68020OpcodeKind.Rte;
            }

            if (opcode == 0x4E74)
            {
                return M68020OpcodeKind.Rtd;
            }

            if (opcode == 0x4E75)
            {
                return M68020OpcodeKind.Rts;
            }

            if ((opcode & 0xFFF8) == 0x4ED0)
            {
                return M68020OpcodeKind.JmpAddressIndirect;
            }

            if ((opcode & 0xFFF8) == 0x4EE8)
            {
                return M68020OpcodeKind.JmpAddressDisplacement;
            }

            if (opcode == 0x4EFB)
            {
                return M68020OpcodeKind.JmpBriefIndexed;
            }

            if ((opcode & 0xFFF8) == 0x4EF0)
            {
                return M68020OpcodeKind.JmpAddressBriefIndexed;
            }

            if ((opcode & 0xFFF8) == 0x4E90)
            {
                return M68020OpcodeKind.JsrAddressIndirect;
            }

            if (opcode == 0x4EB9)
            {
                return M68020OpcodeKind.JsrAbsoluteLong;
            }

            if ((opcode & 0xFFF8) == 0x4EA8)
            {
                return M68020OpcodeKind.JsrAddressDisplacement;
            }

            if ((opcode & 0xFFF8) == 0x4EB0)
            {
                return M68020OpcodeKind.JsrBriefIndexed;
            }

            if (opcode == 0x4EBA)
            {
                return M68020OpcodeKind.JsrPcDisplacement;
            }

            if (opcode == 0x4EBB)
            {
                return M68020OpcodeKind.JsrPcBriefIndexed;
            }

            if (opcode == 0x4EFA)
            {
                return M68020OpcodeKind.JmpPcDisplacement;
            }

            if (opcode == 0x4EF9)
            {
                return M68020OpcodeKind.JmpAbsoluteLong;
            }

            if ((opcode & 0xFFF8) == 0x4868)
            {
                return M68020OpcodeKind.PeaAddressDisplacement;
            }

            if ((opcode & 0xFFF8) == 0x4850)
            {
                return M68020OpcodeKind.PeaAddressIndirect;
            }

            if ((opcode & 0xFFF8) == 0x4870)
            {
                return M68020OpcodeKind.PeaBriefIndexed;
            }

            if (opcode == 0x4878)
            {
                return M68020OpcodeKind.PeaAbsoluteWord;
            }

            if (opcode == 0x4879)
            {
                return M68020OpcodeKind.PeaAbsoluteLong;
            }

            if (opcode == 0x487A)
            {
                return M68020OpcodeKind.PeaPcDisplacement;
            }

            if (opcode == 0x487B)
            {
                return M68020OpcodeKind.PeaPcBriefIndexed;
            }

            if ((opcode & 0xFFF8) == 0x4808)
            {
                return M68020OpcodeKind.LinkLong;
            }

            if ((opcode & 0xFFF8) == 0x4E50)
            {
                return M68020OpcodeKind.LinkWord;
            }

            if ((opcode & 0xFFF8) == 0x4E58)
            {
                return M68020OpcodeKind.Unlink;
            }

            if ((opcode & 0xFFC0) == 0x4800)
            {
                return M68020OpcodeKind.NbcdByte;
            }

            if ((opcode & 0xFFF8) == 0x49C0)
            {
                return M68020OpcodeKind.ExtbLong;
            }

            if ((opcode & 0xFFF8) == 0x48E0)
            {
                return M68020OpcodeKind.MovemLongRegistersToPredecrement;
            }

            if ((opcode & 0xFFF8) == 0x48A0)
            {
                return M68020OpcodeKind.MovemWordRegistersToPredecrement;
            }

            if ((opcode & 0xFFF8) == 0x48A8)
            {
                return M68020OpcodeKind.MovemWordRegistersToAddressDisplacement;
            }

            if ((opcode & 0xFFF8) == 0x48D0)
            {
                return M68020OpcodeKind.MovemLongRegistersToAddressIndirect;
            }

            if ((opcode & 0xFFF8) == 0x48E8)
            {
                return M68020OpcodeKind.MovemLongRegistersToAddressDisplacement;
            }

            if ((opcode & 0xFFF8) == 0x48F0)
            {
                return M68020OpcodeKind.MovemLongRegistersToBriefIndexed;
            }

            if (opcode == 0x48F9)
            {
                return M68020OpcodeKind.MovemLongRegistersToAbsoluteLong;
            }

            if ((opcode & 0xFFF8) == 0x4C98)
            {
                return M68020OpcodeKind.MovemWordPostIncrementToRegisters;
            }

            if ((opcode & 0xFFF8) == 0x4CD8)
            {
                return M68020OpcodeKind.MovemLongPostIncrementToRegisters;
            }

            if ((opcode & 0xFFF8) == 0x4CD0)
            {
                return M68020OpcodeKind.MovemLongAddressIndirectToRegisters;
            }

            if ((opcode & 0xFFF8) == 0x4CE8)
            {
                return M68020OpcodeKind.MovemLongAddressDisplacementToRegisters;
            }

            if (opcode == 0x4CFA)
            {
                return M68020OpcodeKind.MovemLongPcDisplacementToRegisters;
            }

            if (opcode == 0x4CBA)
            {
                return M68020OpcodeKind.MovemWordPcDisplacementToRegisters;
            }

            if ((opcode & 0xFFF8) == 0x4CA8)
            {
                return M68020OpcodeKind.MovemWordAddressDisplacementToRegisters;
            }

            if ((opcode & 0xF000) == 0x6000 && (opcode & 0x00FF) == 0x00FF)
            {
                return M68020OpcodeKind.LongBranch;
            }

            if ((opcode & 0xF000) == 0x6000 && (opcode & 0x00FF) != 0x0000)
            {
                return M68020OpcodeKind.ByteBranch;
            }

            if ((opcode & 0xF000) == 0x6000 && (opcode & 0x00FF) == 0x0000)
            {
                return M68020OpcodeKind.WordBranch;
            }

            if ((opcode & 0xF0FF) is 0x50FA or 0x50FB or 0x50FC)
            {
                return M68020OpcodeKind.Trapcc;
            }

            if ((opcode & 0xF0FF) == 0x50F8)
            {
                return M68020OpcodeKind.SccAbsoluteWord;
            }
            if ((opcode & 0xF0C0) == 0x50C0 && ((opcode >> 3) & 7) is >= 2 and <= 6)
            {
                return M68020OpcodeKind.SccAddressMemory;
            }

            if ((opcode & 0xF0FF) == 0x50F9)
            {
                return M68020OpcodeKind.SccAbsoluteLong;
            }

            if ((opcode & 0xF0F8) == 0x50C0)
            {
                return M68020OpcodeKind.SccData;
            }

            if ((opcode & 0xF0F8) == 0x50C8)
            {
                return M68020OpcodeKind.Dbcc;
            }

            if ((opcode & 0xF138) == 0xC110 && (opcode & 0x00C0) != 0x00C0)
                return M68020OpcodeKind.AndDataToAddressIndirect;
            if ((opcode & 0xFF38) == 0x4230 && (opcode & 0x00C0) != 0x00C0)
                return M68020OpcodeKind.ClrBriefIndexed;
            return M68020OpcodeKind.Unsupported;
        }
    }

    internal enum M68kAdvancedFastKind : byte
    {
        None,
        Nop,
        Moveq,
        MoveLongDataToData,
        AddLongDataToData,
        SubLongDataToData,
        AddqLongData,
        CmpLongDataToData,
        ExtLongData,
        SwapData,
        AndByteDataToData,
        MoveLongImmediateToAddress,
        MoveLongPostIncrementToData,
        MoveLongDataToAbsoluteLong,
        ShortUnconditionalBranch,
        ByteBranch,
        Dbcc,
        M68040FpuRegister
    }

    internal readonly record struct M68kAdvancedHotInstruction(
        uint Address,
        ushort Opcode,
        M68kAdvancedFastKind Kind,
        int Length,
        ushort Extension = 0);

    internal struct M68kAdvancedHotBlock
    {
        internal uint StartAddress;
        internal uint EndAddressExclusive;
        internal uint StartGeneration;
        internal uint EndGeneration;
        internal int Count;
        internal bool Valid;
        internal bool GenerationGuarded;
        internal bool HasModelSpecificInstruction;
    }

    internal readonly record struct M68kInstructionFetchMetadata(
        bool CacheHit,
        bool RequiresSynchronization,
        long MachineCycle,
        long NativeCycle);

    internal struct M68kAdvancedInstructionPipe
    {
        internal const int Capacity = 3;

        private ushort _word0;
        private ushort _word1;
        private ushort _word2;
        private uint _address0;
        private uint _address1;
        private uint _address2;
        private M68kInstructionFetchMetadata _metadata0;
        private M68kInstructionFetchMetadata _metadata1;
        private M68kInstructionFetchMetadata _metadata2;

        internal int Count { get; private set; }

        internal uint NextAddress { get; private set; }

        internal bool IsEmpty => Count == 0;

        internal void Reset(uint nextAddress = 0)
        {
            Count = 0;
            NextAddress = nextAddress;
        }

        internal bool Contains(uint address)
            => Count != 0 && _address0 == address;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryPeek(uint address, out ushort word)
        {
            if (Count == 0 || _address0 != address)
            {
                word = 0;
                return false;
            }

            word = _word0;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryConsumeHead(
            uint address,
            out ushort word,
            out M68kInstructionFetchMetadata metadata)
        {
            if (Count != 1 || _address0 != address)
            {
                word = 0;
                metadata = default;
                return false;
            }

            word = _word0;
            metadata = _metadata0;
            Count = 0;
            NextAddress = unchecked(address + 2u);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryConsumeKnownHead(
            out ushort word,
            out M68kInstructionFetchMetadata metadata)
        {
            if (Count == 0)
            {
                word = 0;
                metadata = default;
                return false;
            }

            var address = _address0;
            word = _word0;
            metadata = _metadata0;
            ConsumeHead(address);

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryConsume(
            uint address,
            out ushort word,
            out M68kInstructionFetchMetadata metadata)
        {
            if (Count == 0 || _address0 != address)
            {
                word = 0;
                metadata = default;
                return false;
            }

            word = _word0;
            metadata = _metadata0;
            ConsumeHead(address);

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Append(
            uint address,
            ushort word,
            M68kInstructionFetchMetadata metadata)
        {
            if (Count >= Capacity)
            {
                throw new InvalidOperationException("The MC68020/030 instruction pipe is full.");
            }

            switch (Count)
            {
                case 0:
                    _address0 = address;
                    _word0 = word;
                    _metadata0 = metadata;
                    break;
                case 1:
                    _address1 = address;
                    _word1 = word;
                    _metadata1 = metadata;
                    break;
                default:
                    _address2 = address;
                    _word2 = word;
                    _metadata2 = metadata;
                    break;
            }

            Count++;
            NextAddress = unchecked(address + 2u);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ConsumeHead(uint address)
        {
            if (Count > 1)
            {
                _word0 = _word1;
                _address0 = _address1;
                _metadata0 = _metadata1;
                if (Count > 2)
                {
                    _word1 = _word2;
                    _address1 = _address2;
                    _metadata1 = _metadata2;
                }
            }

            Count--;
            if (Count == 0)
            {
                NextAddress = unchecked(address + 2u);
            }
        }
    }

    internal class M68kAdvancedTimingInterpreter : IM68kBatchCore, IM68kInstructionFrequencyProvider
    {
        private const uint SubroutineSentinel = 0xFFFF_FFFC;
        private const int HotBlockCacheSize = 64;
        private const int HotBlockMaxInstructions = 16;
        protected const ushort Format0ExceptionFrame = 0x0000;
        protected readonly IM68kBus _bus;
        internal readonly M68020CpuProfile _profile;
        internal readonly M68kInstructionFrequencyMatrix _instructionFrequency;
        internal readonly M68kTimingEngine _timing;
        internal readonly M68kTimedBusAdapter _timedBus;
        private readonly M68020OpcodeKind[] _opcodeKinds;
        private readonly byte[] _fastKinds;
        private M68kAdvancedInstructionPipe _instructionPipe;
        private bool _directHotOpcodeFetched;
        private bool _directHotOpcodeRequiresSynchronization;
        private readonly IM68kCodeReader? _codeReader;
        private readonly IM68kJitBus? _jitBus;
        private readonly IM68kPhysicalAddressMap? _physicalAddressMap;
        private readonly M68kAdvancedHotBlock[]? _hotBlocks;
        private readonly M68kAdvancedHotInstruction[]? _hotInstructions;
        private readonly bool _hasModelSpecificInstructions;
        private readonly bool _enableAdvancedFastPath;
        private readonly bool _enableM68020StackMode;

        private static readonly byte[] M68020FastKinds =
            CreateFastKinds(M68020OpcodeDispatchTable.M68020Kinds);

        private static readonly byte[] M68030FastKinds =
            CreateFastKinds(M68020OpcodeDispatchTable.M68030Kinds);

        private static readonly byte[] M68040FastKinds = CreateM68040FastKinds();

        private static byte[] CreateM68040FastKinds()
        {
            var kinds = CreateFastKinds(M68020OpcodeDispatchTable.M68040Kinds);
            kinds[0xF200] = (byte)M68kAdvancedFastKind.M68040FpuRegister;
            return kinds;
        }

        private static byte[] CreateFastKinds(M68020OpcodeKind[] opcodeKinds)
        {
            var kinds = new byte[0x10000];
            for (var opcode = 0; opcode < kinds.Length; opcode++)
            {
                var kind = opcodeKinds[opcode] switch
                {
                    M68020OpcodeKind.Nop => M68kAdvancedFastKind.Nop,
                    M68020OpcodeKind.Moveq => M68kAdvancedFastKind.Moveq,
                    M68020OpcodeKind.MoveLongDataToData => M68kAdvancedFastKind.MoveLongDataToData,
                    M68020OpcodeKind.AddLongDataToData => M68kAdvancedFastKind.AddLongDataToData,
                    M68020OpcodeKind.SubLongDataToData => M68kAdvancedFastKind.SubLongDataToData,
                    M68020OpcodeKind.AddqLongData => M68kAdvancedFastKind.AddqLongData,
                    M68020OpcodeKind.CmpLongDataToData => M68kAdvancedFastKind.CmpLongDataToData,
                    M68020OpcodeKind.ExtLongData => M68kAdvancedFastKind.ExtLongData,
                    M68020OpcodeKind.SwapData => M68kAdvancedFastKind.SwapData,
                    M68020OpcodeKind.AndByteDataToData => M68kAdvancedFastKind.AndByteDataToData,
                    M68020OpcodeKind.MoveLongImmediateToAddress => M68kAdvancedFastKind.MoveLongImmediateToAddress,
                    M68020OpcodeKind.MoveLongPostIncrementToData => M68kAdvancedFastKind.MoveLongPostIncrementToData,
                    M68020OpcodeKind.MoveLongDataToAbsoluteLong => M68kAdvancedFastKind.MoveLongDataToAbsoluteLong,
                    M68020OpcodeKind.ByteBranch => M68kAdvancedFastKind.ByteBranch,
                    M68020OpcodeKind.Dbcc => M68kAdvancedFastKind.Dbcc,
                    _ => M68kAdvancedFastKind.None
                };
                if (kind == M68kAdvancedFastKind.ByteBranch &&
                    (opcode & 0xFF00) == 0x6000 &&
                    (opcode & 0x00FF) != 0)
                {
                    kind = M68kAdvancedFastKind.ShortUnconditionalBranch;
                }

                kinds[opcode] = (byte)kind;
            }

            return kinds;
        }

        public M68kAdvancedTimingInterpreter(IM68kBus bus)
            : this(bus, M68020CpuProfile.OcsAccelerator14Mhz)
        {
        }

        internal M68kAdvancedTimingInterpreter(IM68kBus bus, M68020CpuProfile profile)
            : this(bus, profile, new M68kCpuState())
        {
        }

        internal M68kAdvancedTimingInterpreter(
            IM68kBus bus,
            M68020CpuProfile profile,
            M68kCpuState state,
            M68kInstructionFrequencyMatrix? instructionFrequency = null,
            bool enableM68020StackMode = true,
            M68020OpcodeKind[]? opcodeKinds = null,
            bool hasModelSpecificInstructions = false,
            bool enableAdvancedFastPath = true)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            State = state ?? throw new ArgumentNullException(nameof(state));
            _opcodeKinds = opcodeKinds ?? M68020OpcodeDispatchTable.M68020Kinds;
            _fastKinds = profile.Model switch
            {
                M68kAcceleratorModel.M68030 => M68030FastKinds,
                M68kAcceleratorModel.M68040 => M68040FastKinds,
                _ => M68020FastKinds
            };
            _codeReader = bus as IM68kCodeReader;
            _jitBus = bus as IM68kJitBus;
            _physicalAddressMap = bus as IM68kPhysicalAddressMap;
            _hasModelSpecificInstructions = hasModelSpecificInstructions;
            _enableAdvancedFastPath =
                enableAdvancedFastPath &&
                profile.Model is
                    M68kAcceleratorModel.M68020 or
                    M68kAcceleratorModel.M68030 or
                    M68kAcceleratorModel.M68040;
            if (_enableAdvancedFastPath && _codeReader is not null)
            {
                _hotBlocks = new M68kAdvancedHotBlock[HotBlockCacheSize];
                _hotInstructions = new M68kAdvancedHotInstruction[
                    HotBlockCacheSize * HotBlockMaxInstructions];
            }

            _enableM68020StackMode = enableM68020StackMode;
            if (_enableM68020StackMode)
            {
                State.EnableM68020StackMode();
            }

            _instructionFrequency = instructionFrequency ?? new M68kInstructionFrequencyMatrix();
            _timing = new M68kTimingEngine(_profile, State);
            _timedBus = new M68kTimedBusAdapter(_bus, _profile, State, _timing);
        }

        public M68kCpuState State { get; }

        internal M68020CpuProfile Profile => _profile;

        internal M68kTimingEngine Timing => _timing;

        internal bool InstructionFrequencyEnabled
        {
            get => _instructionFrequency.Enabled;
            set => _instructionFrequency.Enabled = value;
        }

        internal M68kInstructionFrequencySnapshot CaptureInstructionFrequency()
            => _instructionFrequency.CaptureSnapshot();

        internal void ResetInstructionFrequency()
            => _instructionFrequency.Reset();

        bool IM68kInstructionFrequencyProvider.InstructionFrequencyEnabled
        {
            get => InstructionFrequencyEnabled;
            set => InstructionFrequencyEnabled = value;
        }

        M68kInstructionFrequencySnapshot IM68kInstructionFrequencyProvider.CaptureInstructionFrequency()
            => CaptureInstructionFrequency();

        void IM68kInstructionFrequencyProvider.ResetInstructionFrequency()
            => ResetInstructionFrequency();

        public void Dispose()
        {
        }

        internal int ExecuteInstructions(int maxInstructions, long? targetCycle, IM68kInstructionBoundary boundary)
        {
            ArgumentNullException.ThrowIfNull(boundary);
            var instructions = 0;
            while (!State.Halted &&
                instructions < maxInstructions &&
                (!targetCycle.HasValue || State.Cycles < targetCycle.Value))
            {
                if (State.Stopped &&
                    targetCycle.HasValue &&
                    boundary is IM68kStoppedCpuFastForwardBoundary stoppedBoundary)
                {
                    if (!stoppedBoundary.TryFastForwardStoppedInstruction(State, targetCycle.Value, out _))
                    {
                        break;
                    }

                    SynchronizeNativeToMachine();
                    instructions++;
                    continue;
                }

                if (!State.Stopped &&
                    TryExecuteHotBlock(
                        maxInstructions - instructions,
                        targetCycle,
                        boundary,
                        out var hotInstructions,
                        out var stopBatch))
                {
                    instructions += hotInstructions;
                    if (stopBatch)
                    {
                        break;
                    }

                    continue;
                }

                if (!boundary.BeforeInstruction())
                {
                    break;
                }

                var previousCycle = State.Cycles;
                ExecuteInstructionCore();
                boundary.AfterInstruction(previousCycle, State.Cycles);
                instructions++;
            }

            return instructions;
        }

        int IM68kBatchCore.ExecuteInstructions(int maxInstructions, long? targetCycle, IM68kInstructionBoundary boundary)
            => ExecuteInstructions(maxInstructions, targetCycle, boundary);

        public virtual int ExecuteInstruction()
        {
            var startCycles = State.Cycles;
            ExecuteInstructionCore();
            return (int)(State.Cycles - startCycles);
        }

        private void ExecuteInstructionCore()
        {
            if (State.Halted || State.Stopped)
            {
                CompleteTiming(M68kInstructionTimingKey.Idle);
                return;
            }

            if ((State.ProgramCounter & 1) != 0)
            {
                var instructionPc = State.ProgramCounter;
                BeginInstruction(0);
                RaiseFormat0Exception(3, instructionPc, M68kInstructionTimingKey.IllegalInstruction);
                return;
            }

            if (!TryPeekOpcode(State.ProgramCounter, out var opcode))
            {
                throw new UnsupportedM68kTimingException(0, State.ProgramCounter, _profile);
            }

            if (TryExecuteFastInstruction(opcode))
            {
                return;
            }

            if (TryExecuteM68020Instruction(opcode))
            {
                return;
            }

            if (TryExecuteApproximateInstruction(opcode))
            {
                return;
            }

            throw new UnsupportedM68kTimingException(opcode, State.ProgramCounter, _profile);
        }

        private bool TryExecuteHotBlock(
            int maxInstructions,
            long? targetCycle,
            IM68kInstructionBoundary boundary,
            out int executedInstructions,
            out bool stopBatch)
        {
            executedInstructions = 0;
            stopBatch = false;
            if (maxInstructions <= 0 ||
                State.Halted ||
                State.Stopped ||
                (State.ProgramCounter & 1) != 0 ||
                !TryGetHotBlock(State.ProgramCounter, out var cacheSlot, out var block))
            {
                return false;
            }

            var hotInstructions = _hotInstructions!;
            var blockOffset = cacheSlot * HotBlockMaxInstructions;
            if (TryExecuteShortSelfBranchBlock(
                maxInstructions,
                targetCycle,
                boundary,
                cacheSlot,
                in block,
                hotInstructions[blockOffset],
                out executedInstructions,
                out stopBatch))
            {
                return true;
            }

            if (block.HasModelSpecificInstruction)
            {
                return TryExecuteModelSpecificHotBlock(
                    maxInstructions,
                    targetCycle,
                    boundary,
                    cacheSlot,
                    in block,
                    out executedInstructions,
                    out stopBatch);
            }

            var instructionIndex = 0;
            while (executedInstructions < maxInstructions)
            {
                if (targetCycle.HasValue && State.Cycles >= targetCycle.Value)
                {
                    stopBatch = true;
                    return true;
                }

                var hotInstruction = hotInstructions[blockOffset + instructionIndex];
                if (State.ProgramCounter != hotInstruction.Address)
                {
                    if (State.ProgramCounter != block.StartAddress)
                    {
                        return true;
                    }

                    instructionIndex = 0;
                    hotInstruction = hotInstructions[blockOffset];
                }

                if (!boundary.BeforeInstruction())
                {
                    stopBatch = true;
                    return true;
                }

                var previousCycle = State.Cycles;
                if (!TryFetchHotOpcode(in hotInstruction, out var opcode) ||
                    opcode != hotInstruction.Opcode)
                {
                    _hotBlocks![cacheSlot].Valid = false;
                    ExecuteInstructionCore();
                    boundary.AfterInstruction(previousCycle, State.Cycles);
                    executedInstructions++;
                    return true;
                }

                ExecuteHotInstruction(hotInstruction.Kind, opcode);
                boundary.AfterInstruction(previousCycle, State.Cycles);
                executedInstructions++;
                if (State.Halted || State.Stopped)
                {
                    return true;
                }

                var nextIndex = instructionIndex + 1;
                if (nextIndex < block.Count &&
                    State.ProgramCounter == hotInstructions[blockOffset + nextIndex].Address)
                {
                    instructionIndex = nextIndex;
                    continue;
                }

                if (State.ProgramCounter == block.StartAddress)
                {
                    instructionIndex = 0;
                    continue;
                }

                return true;
            }

            return true;
        }

        private bool TryExecuteModelSpecificHotBlock(
            int maxInstructions,
            long? targetCycle,
            IM68kInstructionBoundary boundary,
            int cacheSlot,
            in M68kAdvancedHotBlock block,
            out int executedInstructions,
            out bool stopBatch)
        {
            executedInstructions = 0;
            stopBatch = false;
            var hotInstructions = _hotInstructions!;
            var blockOffset = cacheSlot * HotBlockMaxInstructions;
            var instructionIndex = 0;
            while (executedInstructions < maxInstructions)
            {
                if (targetCycle.HasValue && State.Cycles >= targetCycle.Value)
                {
                    stopBatch = true;
                    return true;
                }

                var hotInstruction = hotInstructions[blockOffset + instructionIndex];
                if (State.ProgramCounter != hotInstruction.Address)
                {
                    if (State.ProgramCounter != block.StartAddress)
                    {
                        return true;
                    }

                    instructionIndex = 0;
                    hotInstruction = hotInstructions[blockOffset];
                }

                if (!boundary.BeforeInstruction())
                {
                    stopBatch = true;
                    return true;
                }

                var previousCycle = State.Cycles;
                if (!TryFetchHotOpcode(in hotInstruction, out var opcode) ||
                    opcode != hotInstruction.Opcode)
                {
                    _hotBlocks![cacheSlot].Valid = false;
                    ExecuteInstructionCore();
                    boundary.AfterInstruction(previousCycle, State.Cycles);
                    executedInstructions++;
                    return true;
                }

                if (hotInstruction.Kind == M68kAdvancedFastKind.M68040FpuRegister)
                {
                    if (!TryExecuteFastModelSpecificInstruction(opcode))
                    {
                        throw new InvalidOperationException("The MC68040 FPU hot instruction was not handled.");
                    }
                }
                else
                {
                    ExecuteHotInstruction(hotInstruction.Kind, opcode);
                }

                boundary.AfterInstruction(previousCycle, State.Cycles);
                executedInstructions++;
                if (State.Halted || State.Stopped)
                {
                    return true;
                }

                var nextIndex = instructionIndex + 1;
                if (nextIndex < block.Count &&
                    State.ProgramCounter == hotInstructions[blockOffset + nextIndex].Address)
                {
                    instructionIndex = nextIndex;
                    continue;
                }

                if (State.ProgramCounter == block.StartAddress)
                {
                    instructionIndex = 0;
                    continue;
                }

                return true;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryFetchHotOpcode(
            in M68kAdvancedHotInstruction hotInstruction,
            out ushort opcode)
        {
            if (_timing.InstructionCache.Enabled || !_instructionPipe.IsEmpty)
            {
                return TryPeekOpcode(hotInstruction.Address, out opcode);
            }

            opcode = _timedBus.ReadInstructionFetchWordHot(
                hotInstruction.Address,
                out var cacheHit,
                out _directHotOpcodeRequiresSynchronization,
                out var completedMachineCycle);
            _directHotOpcodeFetched = opcode == hotInstruction.Opcode;
            if (!_directHotOpcodeFetched)
            {
                _instructionPipe.Reset(hotInstruction.Address);
                _instructionPipe.Append(
                    hotInstruction.Address,
                    opcode,
                    new M68kInstructionFetchMetadata(
                        cacheHit,
                        _directHotOpcodeRequiresSynchronization,
                        completedMachineCycle,
                        State.NativeCycles));
            }

            return true;
        }

        private bool TryExecuteShortSelfBranchBlock(
            int maxInstructions,
            long? targetCycle,
            IM68kInstructionBoundary boundary,
            int cacheSlot,
            in M68kAdvancedHotBlock block,
            M68kAdvancedHotInstruction hotInstruction,
            out int executedInstructions,
            out bool stopBatch)
        {
            executedInstructions = 0;
            stopBatch = false;
            if (block.Count != 1 ||
                hotInstruction.Kind != M68kAdvancedFastKind.ShortUnconditionalBranch ||
                _timing.InstructionCache.Enabled)
            {
                return false;
            }

            var target = unchecked((uint)(hotInstruction.Address + 2 +
                (sbyte)(hotInstruction.Opcode & 0xFF)));
            if (target != hotInstruction.Address)
            {
                return false;
            }

            while (executedInstructions < maxInstructions)
            {
                if (State.ProgramCounter != hotInstruction.Address)
                {
                    return true;
                }

                if (targetCycle.HasValue && State.Cycles >= targetCycle.Value)
                {
                    stopBatch = true;
                    return true;
                }

                if (!boundary.BeforeInstruction())
                {
                    stopBatch = true;
                    return true;
                }

                var previousCycle = State.Cycles;
                var opcode = _timedBus.ReadInstructionFetchWordHot(
                    hotInstruction.Address,
                    out var cacheHit,
                    out var requiresSynchronization,
                    out var completedMachineCycle);
                if (opcode != hotInstruction.Opcode)
                {
                    _hotBlocks![cacheSlot].Valid = false;
                    _instructionPipe.Reset(hotInstruction.Address);
                    _instructionPipe.Append(
                        hotInstruction.Address,
                        opcode,
                        new M68kInstructionFetchMetadata(
                            cacheHit,
                            requiresSynchronization,
                            completedMachineCycle,
                            State.NativeCycles));
                    ExecuteInstructionCore();
                    boundary.AfterInstruction(previousCycle, State.Cycles);
                    executedInstructions++;
                    return true;
                }

                BeginInstruction(opcode);
                if (requiresSynchronization)
                {
                    _timing.SynchronizeNativeToBus();
                }

                State.ProgramCounter = unchecked(hotInstruction.Address + 2);
                if (_instructionFrequency.Enabled)
                {
                    _instructionFrequency.RecordTakenBranch(
                        hotInstruction.Address,
                        opcode,
                        target,
                        2);
                }

                State.ProgramCounter = target;
                _timing.CompleteHotBranchByteTaken();
                _instructionPipe.Reset(State.ProgramCounter);
                boundary.AfterInstruction(previousCycle, State.Cycles);
                executedInstructions++;
                if (State.Halted || State.Stopped)
                {
                    return true;
                }
            }

            return true;
        }

        private bool TryGetHotBlock(
            uint startAddress,
            out int cacheSlot,
            out M68kAdvancedHotBlock block)
        {
            var hotBlocks = _hotBlocks;
            if (hotBlocks is null || _hotInstructions is null || _codeReader is null)
            {
                cacheSlot = 0;
                block = default;
                return false;
            }

            cacheSlot = (int)((startAddress >> 1) & (HotBlockCacheSize - 1));
            block = hotBlocks[cacheSlot];
            if (block.Valid &&
                block.StartAddress == startAddress &&
                IsHotBlockGenerationValid(in block))
            {
                return true;
            }

            block = BuildHotBlock(cacheSlot, startAddress);
            return block.Valid;
        }

        private M68kAdvancedHotBlock BuildHotBlock(int cacheSlot, uint startAddress)
        {
            var hotInstructions = _hotInstructions!;
            var blockOffset = cacheSlot * HotBlockMaxInstructions;
            var address = startAddress;
            var endAddress = startAddress;
            var count = 0;
            var hasModelSpecificInstruction = false;
            while (count < HotBlockMaxInstructions && CanDecodeHotBlockAddress(address))
            {
                ushort opcode;
                try
                {
                    opcode = _codeReader!.ReadHostWord(address);
                }
                catch (M68kCodeReadException)
                {
                    break;
                }
                catch (ArgumentOutOfRangeException)
                {
                    break;
                }
                catch (IndexOutOfRangeException)
                {
                    break;
                }

                var kind = (M68kAdvancedFastKind)_fastKinds[opcode];
                if (kind == M68kAdvancedFastKind.None)
                {
                    break;
                }

                ushort extension = 0;
                if (kind == M68kAdvancedFastKind.M68040FpuRegister)
                {
                    hasModelSpecificInstruction = true;
                    try
                    {
                        extension = _codeReader!.ReadHostWord(unchecked(address + 2));
                    }
                    catch (M68kCodeReadException)
                    {
                        break;
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        break;
                    }
                    catch (IndexOutOfRangeException)
                    {
                        break;
                    }

                    if (!M68040FpuHelpers.IsRegisterOperationCommand(extension))
                    {
                        break;
                    }
                }

                var length = kind switch
                {
                    M68kAdvancedFastKind.MoveLongImmediateToAddress => 6,
                    M68kAdvancedFastKind.MoveLongDataToAbsoluteLong => 6,
                    M68kAdvancedFastKind.Dbcc => 4,
                    M68kAdvancedFastKind.M68040FpuRegister => 4,
                    _ => 2
                };
                hotInstructions[blockOffset + count] = new M68kAdvancedHotInstruction(
                    address,
                    opcode,
                    kind,
                    length,
                    extension);
                count++;
                var nextAddress = unchecked(address + (uint)length);
                if (nextAddress <= address)
                {
                    break;
                }

                endAddress = nextAddress;
                if (kind is M68kAdvancedFastKind.ShortUnconditionalBranch or
                    M68kAdvancedFastKind.ByteBranch or
                    M68kAdvancedFastKind.Dbcc)
                {
                    break;
                }

                address = nextAddress;
            }

            var block = new M68kAdvancedHotBlock
            {
                StartAddress = startAddress,
                EndAddressExclusive = endAddress,
                Count = count,
                Valid = count != 0,
                HasModelSpecificInstruction = hasModelSpecificInstruction
            };
            var byteCount = endAddress >= startAddress
                ? endAddress - startAddress
                : 0;
            if (block.Valid &&
                byteCount != 0 &&
                byteCount <= int.MaxValue &&
                _jitBus is { } jitBus &&
                jitBus.IsJitCodeAddress(
                    startAddress,
                    (int)byteCount,
                    M68kBusAccessKind.CpuInstructionFetch))
            {
                block.StartGeneration = jitBus.GetJitCodePageGeneration(startAddress);
                block.EndGeneration = jitBus.GetJitCodePageGeneration(endAddress - 1);
                block.GenerationGuarded = true;
                block.Valid = jitBus.JitCodeRangeGenerationMatches(
                    startAddress,
                    (int)byteCount,
                    block.StartGeneration,
                    block.EndGeneration);
            }

            _hotBlocks![cacheSlot] = block;
            return block;
        }

        private bool IsHotBlockGenerationValid(in M68kAdvancedHotBlock block)
        {
            if (!block.GenerationGuarded)
            {
                return true;
            }

            var byteCount = block.EndAddressExclusive - block.StartAddress;
            return _jitBus!.JitCodeRangeGenerationMatches(
                block.StartAddress,
                (int)byteCount,
                block.StartGeneration,
                block.EndGeneration);
        }

        private bool CanDecodeHotBlockAddress(uint address)
        {
            if (_physicalAddressMap is { } addressMap &&
                !addressMap.IsCpuPhysicalAddressMapped(
                    address,
                    2,
                    M68kBusAccessKind.CpuInstructionFetch))
            {
                return false;
            }

            var target = _profile.GetBusTimingRule(address).Target;
            if (target is
                M68020MemoryTarget.CustomRegisters or
                M68020MemoryTarget.Cia or
                M68020MemoryTarget.HostTrap or
                M68020MemoryTarget.Unmapped)
            {
                return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecuteHotInstruction(M68kAdvancedFastKind kind, ushort opcode)
        {
            switch (kind)
            {
                case M68kAdvancedFastKind.Nop:
                    ExecuteFastNop(opcode);
                    return;
                case M68kAdvancedFastKind.Moveq:
                    ExecuteFastMoveq(opcode);
                    return;
                case M68kAdvancedFastKind.MoveLongDataToData:
                    ExecuteFastMoveLongDataToData(opcode);
                    return;
                case M68kAdvancedFastKind.AddLongDataToData:
                    ExecuteFastAddLongDataToData(opcode);
                    return;
                case M68kAdvancedFastKind.SubLongDataToData:
                    ExecuteFastSubLongDataToData(opcode);
                    return;
                case M68kAdvancedFastKind.AddqLongData:
                    ExecuteFastAddqLongData(opcode);
                    return;
                case M68kAdvancedFastKind.CmpLongDataToData:
                    ExecuteFastCmpLongDataToData(opcode);
                    return;
                case M68kAdvancedFastKind.ExtLongData:
                    ExecuteFastExtLongData(opcode);
                    return;
                case M68kAdvancedFastKind.SwapData:
                    ExecuteFastSwapData(opcode);
                    return;
                case M68kAdvancedFastKind.AndByteDataToData:
                    ExecuteFastAndByteDataToData(opcode);
                    return;
                case M68kAdvancedFastKind.MoveLongImmediateToAddress:
                    ExecuteFastMoveLongImmediateToAddress(opcode);
                    return;
                case M68kAdvancedFastKind.MoveLongPostIncrementToData:
                    ExecuteFastMoveLongPostIncrementToData(opcode);
                    return;
                case M68kAdvancedFastKind.MoveLongDataToAbsoluteLong:
                    ExecuteFastMoveLongDataToAbsoluteLong(opcode);
                    return;
                case M68kAdvancedFastKind.ShortUnconditionalBranch:
                    ExecuteFastShortUnconditionalBranch(opcode);
                    return;
                case M68kAdvancedFastKind.ByteBranch:
                    ExecuteFastByteBranch(opcode);
                    return;
                case M68kAdvancedFastKind.Dbcc:
                    ExecuteFastDbcc(opcode);
                    return;
                default:
                    throw new InvalidOperationException("Unsupported MC68020/030 hot-block instruction kind.");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryExecuteFastInstruction(ushort opcode)
        {
            if (!_enableAdvancedFastPath)
            {
                return false;
            }

            switch ((M68kAdvancedFastKind)_fastKinds[opcode])
            {
                case M68kAdvancedFastKind.Nop:
                    ExecuteFastNop(opcode);
                    return true;

                case M68kAdvancedFastKind.Moveq:
                    ExecuteFastMoveq(opcode);
                    return true;

                case M68kAdvancedFastKind.MoveLongDataToData:
                    ExecuteFastMoveLongDataToData(opcode);
                    return true;

                case M68kAdvancedFastKind.AddLongDataToData:
                    ExecuteFastAddLongDataToData(opcode);
                    return true;

                case M68kAdvancedFastKind.SubLongDataToData:
                    ExecuteFastSubLongDataToData(opcode);
                    return true;

                case M68kAdvancedFastKind.AddqLongData:
                    ExecuteFastAddqLongData(opcode);
                    return true;

                case M68kAdvancedFastKind.CmpLongDataToData:
                    ExecuteFastCmpLongDataToData(opcode);
                    return true;

                case M68kAdvancedFastKind.ExtLongData:
                    ExecuteFastExtLongData(opcode);
                    return true;

                case M68kAdvancedFastKind.SwapData:
                    ExecuteFastSwapData(opcode);
                    return true;

                case M68kAdvancedFastKind.AndByteDataToData:
                    ExecuteFastAndByteDataToData(opcode);
                    return true;

                case M68kAdvancedFastKind.MoveLongImmediateToAddress:
                    ExecuteFastMoveLongImmediateToAddress(opcode);
                    return true;

                case M68kAdvancedFastKind.MoveLongPostIncrementToData:
                    ExecuteFastMoveLongPostIncrementToData(opcode);
                    return true;

                case M68kAdvancedFastKind.MoveLongDataToAbsoluteLong:
                    ExecuteFastMoveLongDataToAbsoluteLong(opcode);
                    return true;

                case M68kAdvancedFastKind.ShortUnconditionalBranch:
                    ExecuteFastShortUnconditionalBranch(opcode);
                    return true;

                case M68kAdvancedFastKind.ByteBranch:
                    ExecuteFastByteBranch(opcode);
                    return true;

                case M68kAdvancedFastKind.Dbcc:
                    ExecuteFastDbcc(opcode);
                    return true;

                default:
                    return false;
            }
        }

        protected virtual bool TryExecuteFastModelSpecificInstruction(ushort opcode)
        {
            _ = opcode;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void ConsumeFastOpcode(ushort opcode)
        {
            if (_directHotOpcodeFetched)
            {
                _directHotOpcodeFetched = false;
                if (_directHotOpcodeRequiresSynchronization)
                {
                    _timing.SynchronizeNativeToBus();
                }

                State.ProgramCounter += 2;
                return;
            }

            var address = State.ProgramCounter;
            if (_instructionPipe.TryConsumeKnownHead(out _, out var metadata))
            {
                if (metadata.RequiresSynchronization)
                {
                    _timing.SynchronizeNativeToBus();
                }

                State.ProgramCounter += 2;
                return;
            }

            var fetched = FetchWord();
            if (fetched != opcode)
            {
                throw new InvalidOperationException("The fast interpreter opcode does not match the instruction pipe head.");
            }
        }

        private void ExecuteFastNop(ushort opcode)
        {
            BeginInstruction(opcode);
            ConsumeFastOpcode(opcode);
            CompleteTiming(M68kInstructionTimingKey.Nop);
        }

        private void ExecuteFastMoveq(ushort opcode)
        {
            BeginInstruction(opcode);
            ConsumeFastOpcode(opcode);
            var register = (opcode >> 9) & 7;
            var value = unchecked((uint)(int)(sbyte)(opcode & 0xFF));
            State.D[register] = value;
            State.SetLongLogicFlags(value);
            CompleteFastTiming(M68kInstructionTimingKey.Moveq);
        }

        private void ExecuteFastMoveLongDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            ConsumeFastOpcode(opcode);
            var destination = (opcode >> 9) & 7;
            var value = State.D[opcode & 7];
            State.D[destination] = value;
            State.SetLongLogicFlags(value);
            CompleteFastTiming(M68kInstructionTimingKey.MoveLongDataToData);
        }

        private void ExecuteFastAddLongDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            ConsumeFastOpcode(opcode);
            var destination = State.D[(opcode >> 9) & 7];
            var source = State.D[opcode & 7];
            var result = destination + source;
            State.D[(opcode >> 9) & 7] = result;
            SetFastLongAddFlags(destination, source, result);
            CompleteFastTiming(M68kInstructionTimingKey.AddLongDataToData);
        }

        private void ExecuteFastSubLongDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            ConsumeFastOpcode(opcode);
            var destination = State.D[(opcode >> 9) & 7];
            var source = State.D[opcode & 7];
            var result = destination - source;
            State.D[(opcode >> 9) & 7] = result;
            SetFastLongSubtractFlags(destination, source, result);
            CompleteFastTiming(M68kInstructionTimingKey.SubLongDataToData);
        }

        private void ExecuteFastAddqLongData(ushort opcode)
        {
            BeginInstruction(opcode);
            ConsumeFastOpcode(opcode);
            var destinationRegister = opcode & 7;
            var source = (uint)((opcode >> 9) & 7);
            if (source == 0)
            {
                source = 8;
            }

            var destination = State.D[destinationRegister];
            var result = destination + source;
            State.D[destinationRegister] = result;
            SetFastLongAddFlags(destination, source, result);
            CompleteFastTiming(M68kInstructionTimingKey.AddLongDataToData);
        }

        private void ExecuteFastCmpLongDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            ConsumeFastOpcode(opcode);
            var destination = State.D[(opcode >> 9) & 7];
            var source = State.D[opcode & 7];
            var result = destination - source;
            SetFastLongCompareFlags(destination, source, result);
            CompleteFastTiming(M68kInstructionTimingKey.CmpLongDataToData);
        }

        private void ExecuteFastExtLongData(ushort opcode)
        {
            BeginInstruction(opcode);
            ConsumeFastOpcode(opcode);
            var register = opcode & 7;
            var result = M68kCpuState.SignExtend(State.D[register] & 0xFFFF, M68kOperandSize.Word);
            State.D[register] = result;
            SetMoveFlags(result, M68kOperandSize.Long);
            CompleteFastTiming(M68kInstructionTimingKey.ExtLongData);
        }

        private void ExecuteFastSwapData(ushort opcode)
        {
            BeginInstruction(opcode);
            ConsumeFastOpcode(opcode);
            var register = opcode & 7;
            var value = State.D[register];
            var result = (value << 16) | (value >> 16);
            State.D[register] = result;
            State.SetLongLogicFlags(result);
            CompleteFastTiming(M68kInstructionTimingKey.SwapData);
        }

        private void ExecuteFastAndByteDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            ConsumeFastOpcode(opcode);
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var result = (byte)(State.D[destinationRegister] & State.D[sourceRegister]);
            WriteDataRegisterByte(destinationRegister, result);
            SetMoveFlags(result, M68kOperandSize.Byte);
            CompleteFastTiming(M68kInstructionTimingKey.AndByteDataToData);
        }

        private void ExecuteFastMoveLongImmediateToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            ConsumeFastOpcode(opcode);
            WriteGeneralRegister(true, (opcode >> 9) & 7, FetchLong());
            CompleteFastTiming(M68kInstructionTimingKey.MoveLongImmediateToAddress);
        }

        private void ExecuteFastMoveLongPostIncrementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            ConsumeFastOpcode(opcode);
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var address = State.A[source];
            var value = _timedBus.ReadDataLongHot(address);
            State.D[destination] = value;
            WriteGeneralRegister(true, source, address + 4);
            State.SetLongLogicFlags(value);
            CompleteFastTiming(M68kInstructionTimingKey.MoveLongPostIncrementToData);
        }

        private void ExecuteFastMoveLongDataToAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            ConsumeFastOpcode(opcode);
            var source = opcode & 7;
            var address = FetchLong();
            var value = State.D[source];
            _timedBus.WriteDataLongHot(address, value);
            State.SetLongLogicFlags(value);
            CompleteFastTiming(M68kInstructionTimingKey.MoveLongDataToAbsoluteLong);
        }

        private void ExecuteFastShortUnconditionalBranch(ushort opcode)
        {
            BeginInstruction(opcode);
            ConsumeFastOpcode(opcode);
            var target = unchecked((uint)(State.ProgramCounter + (sbyte)opcode));
            if (_instructionFrequency.Enabled)
            {
                _instructionFrequency.RecordTakenBranch(
                    State.LastInstructionProgramCounter,
                    opcode,
                    target,
                    2);
            }

            State.ProgramCounter = target;
            _timing.CompleteHotBranchByteTaken();
            _instructionPipe.Reset(State.ProgramCounter);
        }

        private void ExecuteFastByteBranch(ushort opcode)
        {
            BeginInstruction(opcode);
            ConsumeFastOpcode(opcode);
            var condition = (opcode >> 8) & 0x0F;
            var displacement = unchecked((int)(sbyte)(opcode & 0xFF));
            var branchBase = State.ProgramCounter;

            if (condition == 0x1)
            {
                PushLong(branchBase);
                State.ProgramCounter = unchecked((uint)(branchBase + displacement));
                CompleteTiming(M68kInstructionTimingKey.BsrByte);
                return;
            }

            if (CheckCondition(condition))
            {
                var target = unchecked((uint)(branchBase + displacement));
                if (_instructionFrequency.Enabled)
                {
                    _instructionFrequency.RecordTakenBranch(
                        State.LastInstructionProgramCounter,
                        opcode,
                        target,
                        2);
                }

                State.ProgramCounter = target;
                CompleteTiming(M68kInstructionTimingKey.BranchByteTaken);
                return;
            }

            CompleteTiming(M68kInstructionTimingKey.BranchByteNotTaken);
        }

        private void ExecuteFastDbcc(ushort opcode)
        {
            BeginInstruction(opcode);
            ConsumeFastOpcode(opcode);
            var branchBase = State.ProgramCounter;
            var condition = (opcode >> 8) & 0x0F;
            var displacement = unchecked((int)(short)FetchWord());
            if (CheckCondition(condition))
            {
                CompleteTiming(M68kInstructionTimingKey.DbccConditionTrue);
                return;
            }

            var register = opcode & 7;
            var counter = (ushort)(State.D[register] - 1);
            State.D[register] = (State.D[register] & 0xFFFF_0000u) | counter;
            if (counter != 0xFFFF)
            {
                var target = unchecked((uint)(branchBase + displacement));
                if (_instructionFrequency.Enabled)
                {
                    _instructionFrequency.RecordTakenBranch(
                        State.LastInstructionProgramCounter,
                        opcode,
                        target,
                        4);
                }

                State.ProgramCounter = target;
                CompleteTiming(M68kInstructionTimingKey.DbccBranchTaken);
                return;
            }

            CompleteTiming(M68kInstructionTimingKey.DbccExpired);
        }

        public virtual void Reset(uint programCounter, uint stackPointer)
        {
            Array.Clear(State.D);
            Array.Clear(State.A);
            State.ProgramCounter = programCounter;
            State.ResetStackPointers(stackPointer, 0, supervisorMode: true);
            if (_enableM68020StackMode)
            {
                State.EnableM68020StackMode();
            }
            else
            {
                State.DisableM68020StackMode();
            }

            State.StatusRegister = M68kCpuState.ResetStatusRegister;
            State.VectorBaseRegister = 0;
            State.SourceFunctionCode = 0;
            State.DestinationFunctionCode = 0;
            State.CacheControlRegister = 0;
            State.CacheAddressRegister = 0;
            State.M68040Fpu.Reset();
            State.M68040Mmu.Reset();
            State.Cycles = 0;
            State.NativeCycles = 0;
            State.Halted = false;
            State.Stopped = false;
            State.LastOpcode = 0;
            State.LastInstructionProgramCounter = 0;
            State.RecordException(-1, 0, 0);
            _timing.Reset();
            _timedBus.ResetInstructionFetchBuffer();
            _instructionPipe.Reset(programCounter);
            if (_hotBlocks is not null)
            {
                Array.Clear(_hotBlocks);
            }
        }

        public void BeginSubroutine(uint address, uint stackPointer, uint returnAddress)
        {
            State.SetActiveStackPointer(stackPointer);
            PushLong(returnAddress);
            State.ProgramCounter = address;
            State.Halted = false;
            State.Stopped = false;
            _instructionPipe.Reset(address);
        }

        protected void DiscardInstructionPrefetch() => _instructionPipe.Reset(State.ProgramCounter);

        public virtual void RequestInterrupt(int level, uint vectorAddress)
        {
            if (level <= 0)
            {
                return;
            }

            var mask = (State.StatusRegister >> 8) & 0x07;
            if (level <= mask)
            {
                return;
            }

            State.Stopped = false;
            _instructionPipe.Reset(State.ProgramCounter);
            var savedStatusRegister = State.StatusRegister;
            var vectorTarget = ReadLong(State.VectorBaseRegister + vectorAddress);
            State.RecordException((int)(vectorAddress / 4), State.ProgramCounter, savedStatusRegister);
            if (State.M68020StackModeEnabled &&
                (savedStatusRegister & (M68kCpuState.Supervisor | M68kCpuState.Master)) ==
                (M68kCpuState.Supervisor | M68kCpuState.Master))
            {
                var interruptStackPointer = State.InterruptStackPointer;
                PushFrameAt(
                    ref interruptStackPointer,
                    savedStatusRegister,
                    State.ProgramCounter,
                    (ushort)(0x1000 | ((int)vectorAddress & 0x0FFF)));
                State.SetInterruptStackPointer(interruptStackPointer);

                var masterStackPointer = State.MasterStackPointer;
                PushFrameAt(
                    ref masterStackPointer,
                    savedStatusRegister,
                    State.ProgramCounter,
                    (ushort)(Format0ExceptionFrame | ((int)vectorAddress & 0x0FFF)));
                State.SetMasterStackPointer(masterStackPointer);

                State.StatusRegister = (ushort)((savedStatusRegister & 0xF8FF) | ((level & 7) << 8) | M68kCpuState.Supervisor);
                State.ProgramCounter = vectorTarget;
                CompleteTiming(M68kInstructionTimingKey.InterruptAcknowledge);
                return;
            }

            State.StatusRegister = (ushort)((State.StatusRegister & 0xE8FF) | ((level & 7) << 8) | M68kCpuState.Supervisor);
            PushWord((ushort)(Format0ExceptionFrame | ((int)vectorAddress & 0x0FFF)));
            PushLong(State.ProgramCounter);
            PushWord(savedStatusRegister);
            State.ProgramCounter = vectorTarget;
            CompleteTiming(M68kInstructionTimingKey.InterruptAcknowledge);
        }

        private void PushFrameAt(ref uint stackPointer, ushort statusRegister, uint programCounter, ushort formatWord)
        {
            stackPointer -= 2;
            WriteWord(stackPointer, formatWord);
            stackPointer -= 4;
            WriteLong(stackPointer, programCounter);
            stackPointer -= 2;
            WriteWord(stackPointer, statusRegister);
        }

        protected virtual bool TryExecuteM68020Instruction(ushort opcode)
        {
            if (_hasModelSpecificInstructions && TryExecuteModelSpecificInstruction(opcode))
            {
                return true;
            }

            return ExecuteDispatchedInstruction(opcode, _opcodeKinds[opcode]);
        }

        private bool ExecuteDispatchedInstruction(ushort opcode, M68020OpcodeKind kind)
        {
            switch (kind)
            {
                case M68020OpcodeKind.Unsupported:
                    return false;

                case M68020OpcodeKind.LineAException:
                    BeginInstruction(opcode);
                    _ = FetchWord();
                    RaiseFormat0Exception(10, State.LastInstructionProgramCounter, M68kInstructionTimingKey.LineAException);
                    return true;

                case M68020OpcodeKind.LineFException:
                    BeginInstruction(opcode);
                    _ = FetchWord();
                    if (opcode == 0xFF00)
                    {
                        var token = FetchLong();
                        var returnProgramCounter = State.ProgramCounter;
                        var gateway = _bus.InvokeHostGateway(State.LastInstructionProgramCounter, token, State);
                        if (gateway.Handled)
                        {
                            if (gateway.Result != M68kHostGatewayResult.BlockCurrentTask &&
                                !State.Halted && State.ProgramCounter == returnProgramCounter)
                            {
                                State.ProgramCounter = PullLong();
                            }

                            CompleteTiming(M68kInstructionTimingKey.Nop);
                            return true;
                        }

                        State.ProgramCounter = returnProgramCounter;
                    }

                    RaiseFormat0Exception(11, State.LastInstructionProgramCounter, M68kInstructionTimingKey.LineFException);
                    return true;

                case M68020OpcodeKind.IllegalInstruction:
                    BeginInstruction(opcode);
                    _ = FetchWord();
                    RaiseFormat0Exception(4, State.LastInstructionProgramCounter, M68kInstructionTimingKey.IllegalInstruction);
                    return true;

                case M68020OpcodeKind.ImmediateLogicalToStatusRegister:
                    return TryExecuteImmediateLogicalToStatusRegister(opcode);

                case M68020OpcodeKind.MoveStatusRegisterToAddressIndirect:
                    ExecuteMoveStatusRegisterToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.MoveStatusRegisterToData:
                    ExecuteMoveStatusRegisterToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveDataToStatusRegister:
                    ExecuteMoveDataToStatusRegister(opcode);
                    return true;

                case M68020OpcodeKind.MovePostIncrementToStatusRegister:
                    ExecuteMovePostIncrementToStatusRegister(opcode);
                    return true;

                case M68020OpcodeKind.AndPostIncrementToData:
                    ExecuteAndPostIncrementToData(opcode);
                    return true;

                case M68020OpcodeKind.OrSizedAbsoluteLongToData:
                    ExecuteOrSizedAbsoluteLongToData(opcode);
                    return true;
                case M68020OpcodeKind.NegAbsoluteLong:
                    ExecuteNegAbsoluteLong(opcode);
                    return true;
                case M68020OpcodeKind.CmpaAbsoluteLongToAddress:
                    ExecuteCmpaAbsoluteLongToAddress(opcode);
                    return true;
                case M68020OpcodeKind.TstPcDisplacement:
                    ExecuteTstPcDisplacement(opcode);
                    return true;
                case M68020OpcodeKind.MoveSizedPcDisplacementToAbsoluteLong:
                    ExecuteMoveSizedPcDisplacementToAbsoluteLong(opcode);
                    return true;
                case M68020OpcodeKind.AddaPcBriefIndexedToAddress:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteAddaPcBriefIndexedToAddress(opcode);
                    return true;

                case M68020OpcodeKind.MoveFromCcr:
                    ExecuteMoveFromCcr(opcode);
                    return true;
                case M68020OpcodeKind.MoveImmediateToCcr:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    BeginInstruction(opcode);
                    _ = FetchWord();
                    State.StatusRegister = (ushort)((State.StatusRegister & 0xFFE0) | (FetchWord() & 0x001F));
                    CompleteTiming(M68kInstructionTimingKey.ImmediateWordToConditionCodeRegister);
                    return true;

                case M68020OpcodeKind.Movep:
                    ExecuteMovep(opcode);
                    return true;

                case M68020OpcodeKind.Chk2Cmp2:
                    ExecuteChk2Cmp2(opcode);
                    return true;

                case M68020OpcodeKind.Moveq:
                    ExecuteMoveq(opcode);
                    return true;

                case M68020OpcodeKind.ClrDataLong:
                    ExecuteClrDataLong(opcode);
                    return true;

                case M68020OpcodeKind.ClrLongAddressIndirect:
                    ExecuteClrLongAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.ClrLongAddressDisplacement:
                    ExecuteClrLongAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.ClrLongAbsoluteLong:
                    ExecuteClrLongAbsoluteLong();
                    return true;

                case M68020OpcodeKind.ClrLongAbsoluteWord:
                    ExecuteClrLongAbsoluteWord();
                    return true;

                case M68020OpcodeKind.NegxLongData:
                    ExecuteNegxLongData(opcode);
                    return true;

                case M68020OpcodeKind.NegByteData:
                    ExecuteNegByteData(opcode);
                    return true;

                case M68020OpcodeKind.NegLongData:
                    ExecuteNegLongData(opcode);
                    return true;

                case M68020OpcodeKind.NegWordData:
                    ExecuteNegWordData(opcode);
                    return true;

                case M68020OpcodeKind.NotByteData:
                    ExecuteNotByteData(opcode);
                    return true;

                case M68020OpcodeKind.NotByteAddressDisplacement:
                    ExecuteNotByteAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.NotLongAddressDisplacement:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteNotLongAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.NotWordData:
                    ExecuteNotWordData(opcode);
                    return true;

                case M68020OpcodeKind.NotLongData:
                    ExecuteNotLongData(opcode);
                    return true;

                case M68020OpcodeKind.ClrLongPostIncrement:
                    ExecuteClrLongPostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.ClrLongPredecrement:
                    ExecuteClrLongPredecrement(opcode);
                    return true;

                case M68020OpcodeKind.ClrDataWord:
                    ExecuteClrDataWord(opcode);
                    return true;

                case M68020OpcodeKind.ClrDataByte:
                    ExecuteClrDataByte(opcode);
                    return true;

                case M68020OpcodeKind.ClrWordPostIncrement:
                    ExecuteClrWordPostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.ClrWordAddressDisplacement:
                    ExecuteClrWordAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.ClrWordPredecrement:
                    ExecuteClrWordPredecrement(opcode);
                    return true;

                case M68020OpcodeKind.ClrBytePredecrement:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    BeginInstruction(opcode);
                    _ = FetchWord();
                    var clearRegister = opcode & 7;
                    var clearAddress = unchecked(State.A[clearRegister] - M68kIntegerSemantics.AddressIncrement(clearRegister, M68kOperandSize.Byte));
                    WriteGeneralRegister(true, clearRegister, clearAddress);
                    WriteByte(clearAddress, 0);
                    SetMoveFlags(0, M68kOperandSize.Byte);
                    CompleteTiming(M68kInstructionTimingKey.ClrBytePredecrement);
                    return true;

                case M68020OpcodeKind.ClrWordAddressIndirect:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteClrWordAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.ClrByteAddressIndirect:
                    ExecuteClrByteAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.ClrBytePostIncrement:
                    ExecuteClrBytePostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.ClrByteAddressDisplacement:
                    ExecuteClrByteAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.ClrByteAbsoluteLong:
                    ExecuteClrByteAbsoluteLong();
                    return true;

                case M68020OpcodeKind.LeaAbsoluteLong:
                    ExecuteLeaAbsoluteLong(opcode);
                    return true;

                case M68020OpcodeKind.LeaAbsoluteWord:
                    ExecuteLeaAbsoluteWord(opcode);
                    return true;

                case M68020OpcodeKind.LeaAddressIndirect:
                    ExecuteLeaAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.LeaAddressDisplacement:
                    ExecuteLeaAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.LeaPcDisplacement:
                    ExecuteLeaPcDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.LeaBriefIndexed:
                    ExecuteLeaBriefIndexed(opcode);
                    return true;
                case M68020OpcodeKind.LeaPcBriefIndexed:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteLeaPcBriefIndexed(opcode);
                    return true;

                case M68020OpcodeKind.MoveImmediateToStatusRegister:
                    ExecuteMoveImmediateToStatusRegister();
                    return true;

                case M68020OpcodeKind.MoveByteImmediateToAbsoluteLong:
                    ExecuteMoveByteImmediateToAbsoluteLong();
                    return true;

                case M68020OpcodeKind.MoveByteImmediateToAddressIndirect:
                    ExecuteMoveByteImmediateToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteImmediateToPostIncrement:
                    // Keep the 040's existing fallback and fixed timing policy.
                    if (_profile.Model == M68kAcceleratorModel.M68040)
                    {
                        return false;
                    }
                    ExecuteMoveByteImmediateToPostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteImmediateToAddressDisplacement:
                    ExecuteMoveByteImmediateToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteImmediateToBriefIndexed:
                    ExecuteMoveByteImmediateToBriefIndexed(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordImmediateToAddressIndirect:
                    ExecuteMoveWordImmediateToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordImmediateToAddressDisplacement:
                    ExecuteMoveWordImmediateToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordImmediateToPredecrement:
                    ExecuteMoveWordImmediateToPredecrement(opcode);
                    return true;
                case M68020OpcodeKind.MoveByteImmediateToPredecrement:
                    ExecuteMoveByteImmediateToPredecrement(opcode);
                    return true;
                case M68020OpcodeKind.MoveWordImmediateToPostIncrement:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMoveWordImmediateToPostIncrement(opcode);
                    return true;
                case M68020OpcodeKind.MovePostIncrementToAbsoluteLong:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMovePostIncrementToAbsoluteLong(opcode);
                    return true;
                case M68020OpcodeKind.MoveIndexedToIndexed:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMoveIndexedToIndexed(opcode);
                    return true;
                case M68020OpcodeKind.MoveIndexedToPostIncrement:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMoveIndexedToPostIncrement(opcode);
                    return true;
                case M68020OpcodeKind.MoveIndexedToAbsoluteLong:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMoveIndexedToAbsoluteLong(opcode);
                    return true;
                case M68020OpcodeKind.MoveIndirectToPostIncrement:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMoveIndirectToPostIncrement(opcode);
                    return true;
                case M68020OpcodeKind.MoveIndirectToIndexed:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMoveIndirectToIndexed(opcode);
                    return true;
                case M68020OpcodeKind.MoveAbsoluteLongToIndexed:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMoveAbsoluteLongToIndexed(opcode);
                    return true;
                case M68020OpcodeKind.MovePredecrementToData:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMovePredecrementToData(opcode);
                    return true;
                case M68020OpcodeKind.MovePredecrementToPredecrement:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMovePredecrementToPredecrement(opcode);
                    return true;
                case M68020OpcodeKind.MoveAbsoluteWordToData:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMoveAbsoluteWordToData(opcode);
                    return true;
                case M68020OpcodeKind.MoveAbsoluteWordToAbsoluteWord:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMoveAbsoluteWordToAbsoluteWord(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordDataToPredecrement:
                    ExecuteMoveWordDataToPredecrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordDataToAddressIndirect:
                    ExecuteMoveWordDataToAddressIndirect(opcode);
                    return true;
                case M68020OpcodeKind.MoveWordAddressToAddressIndirect:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMoveWordAddressToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordImmediateToAbsoluteLong:
                    ExecuteMoveWordImmediateToAbsoluteLong();
                    return true;

                case M68020OpcodeKind.MoveLongImmediateToAbsoluteLong:
                    ExecuteMoveLongImmediateToAbsoluteLong();
                    return true;

                case M68020OpcodeKind.MoveLongImmediateToAbsoluteWord:
                    ExecuteMoveLongImmediateToAbsoluteWord();
                    return true;

                case M68020OpcodeKind.MoveLongImmediateToAddressIndirect:
                    ExecuteMoveLongImmediateToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAbsoluteWordToAddressDisplacement:
                    ExecuteMoveLongAbsoluteWordToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordImmediateToBriefIndexed:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMoveWordImmediateToBriefIndexed(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteAbsoluteWordToAddressDisplacement:
                case M68020OpcodeKind.MoveByteAbsoluteLongToAddressDisplacement:
                case M68020OpcodeKind.MoveWordAbsoluteWordToAddressDisplacement:
                    // Keep the 040's existing approximate execution policy.
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMoveAbsoluteToDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAbsoluteLongToAddressDisplacement:
                    ExecuteMoveLongAbsoluteLongToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongImmediateToAddressDisplacement:
                    ExecuteMoveLongImmediateToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongImmediateToBriefIndexed:
                    ExecuteMoveLongImmediateToBriefIndexed(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongImmediateToPostIncrement:
                    ExecuteMoveLongImmediateToPostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongImmediateToPredecrement:
                    ExecuteMoveLongImmediateToPredecrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongImmediateToData:
                    ExecuteMoveLongImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongImmediateToAddress:
                    ExecuteMoveLongImmediateToAddress(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordImmediateToAddress:
                    ExecuteMoveWordImmediateToAddress(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordImmediateToData:
                    ExecuteMoveWordImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordDataToData:
                    ExecuteMoveWordDataToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordAddressToData:
                    ExecuteMoveWordAddressToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordDataToAddress:
                    ExecuteMoveWordDataToAddress(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteImmediateToData:
                    ExecuteMoveByteImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongDataToData:
                    ExecuteMoveLongDataToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongDataToAddress:
                    ExecuteMoveLongDataToAddress(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongDataToAddressIndirect:
                    ExecuteMoveLongDataToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongDataToPostIncrement:
                    ExecuteMoveLongDataToPostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongDataToPredecrement:
                    ExecuteMoveLongDataToPredecrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongDataToAddressDisplacement:
                    ExecuteMoveLongDataToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongDataToBriefIndexed:
                    ExecuteMoveLongDataToBriefIndexed(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressToAddress:
                    ExecuteMoveLongAddressToAddress(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressToAddressIndirect:
                    ExecuteMoveLongAddressToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressToPredecrement:
                    ExecuteMoveLongAddressToPredecrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressToBriefIndexed:
                    ExecuteMoveLongAddressToBriefIndexed(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressToAddressDisplacement:
                    ExecuteMoveLongAddressToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressToPostIncrement:
                    ExecuteMoveLongAddressToPostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteOrWordAddressIndirectToPredecrement:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMoveByteOrWordAddressIndirectToPredecrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressIndirectToPredecrement:
                    ExecuteMoveLongAddressIndirectToPredecrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongPostIncrementToPredecrement:
                    ExecuteMoveLongPostIncrementToPredecrement(opcode);
                    return true;
                case M68020OpcodeKind.MoveBytePostIncrementToPredecrement:
                    ExecuteMoveBytePostIncrementToPredecrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressIndirectToAddressDisplacement:
                    ExecuteMoveLongAddressIndirectToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressToData:
                    ExecuteMoveLongAddressToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressIndirectToData:
                    ExecuteMoveLongAddressIndirectToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongPredecrementToData:
                    ExecuteMoveLongPredecrementToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordAddressIndirectToData:
                    ExecuteMoveWordAddressIndirectToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressIndirectToAddress:
                    ExecuteMoveLongAddressIndirectToAddress(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongPostIncrementToData:
                    ExecuteMoveLongPostIncrementToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongPredecrementToAddress:
                    ExecuteMoveLongPredecrementToAddress(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongPostIncrementToAddress:
                    ExecuteMoveLongPostIncrementToAddress(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongPostIncrementToPostIncrement:
                    ExecuteMoveLongPostIncrementToPostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongPostIncrementToAddressDisplacement:
                    ExecuteMoveLongPostIncrementToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressDisplacementToData:
                    ExecuteMoveLongAddressDisplacementToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressDisplacementToAddress:
                    ExecuteMoveLongAddressDisplacementToAddress(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressDisplacementToAddressIndirect:
                    ExecuteMoveLongAddressDisplacementToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressDisplacementToAddressDisplacement:
                    ExecuteMoveLongAddressDisplacementToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressDisplacementToBriefIndexed:
                    ExecuteMoveLongAddressDisplacementToBriefIndexed(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteAddressDisplacementToBriefIndexed:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMoveByteAddressDisplacementToBriefIndexed(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressDisplacementToPredecrement:
                    ExecuteMoveLongAddressDisplacementToPredecrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteOrWordAddressDisplacementToPostIncrement:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMoveByteOrWordAddressDisplacementToPostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressDisplacementToPostIncrement:
                    ExecuteMoveLongAddressDisplacementToPostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongPcDisplacementToData:
                    ExecuteMoveLongPcDisplacementToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongPcDisplacementToAddress:
                    ExecuteMoveLongPcDisplacementToAddress(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongPcDisplacementToAddressDisplacement:
                    ExecuteMoveLongPcDisplacementToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongPcDisplacementToPredecrement:
                    ExecuteMoveLongPcDisplacementToPredecrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongPcDisplacementToPostIncrement:
                    ExecuteMoveLongPcDisplacementToPostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongBriefIndexedToData:
                    ExecuteMoveLongBriefIndexedToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongBriefIndexedToAddress:
                    ExecuteMoveLongBriefIndexedToAddress(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongPcBriefIndexedToAddress:
                    ExecuteMoveLongPcBriefIndexedToAddress(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongBriefIndexedToPredecrement:
                    ExecuteMoveLongBriefIndexedToPredecrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongBriefIndexedToAddressDisplacement:
                    ExecuteMoveLongPcBriefIndexedToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressBriefIndexedToAddressDisplacement:
                    ExecuteMoveLongAddressBriefIndexedToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongBriefIndexedToBriefIndexed:
                    ExecuteMoveLongBriefIndexedToBriefIndexed(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressIndirectToAddressIndirect:
                    ExecuteMoveLongAddressIndirectToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressIndirectToPostIncrement:
                    ExecuteMoveLongAddressIndirectToPostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAbsoluteWordToData:
                    ExecuteMoveLongAbsoluteWordToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAbsoluteWordToAddress:
                    ExecuteMoveLongAbsoluteWordToAddress(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAbsoluteLongToData:
                    ExecuteMoveLongAbsoluteLongToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAbsoluteLongToAddress:
                    ExecuteMoveLongAbsoluteLongToAddress(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAbsoluteLongToPredecrement:
                    ExecuteMoveLongAbsoluteLongToPredecrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAbsoluteWordToAbsoluteLong:
                    ExecuteMoveLongAbsoluteWordToAbsoluteLong();
                    return true;

                case M68020OpcodeKind.MoveLongDataToAbsoluteWord:
                    ExecuteMoveLongDataToAbsoluteWord(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongDataToAbsoluteLong:
                    ExecuteMoveLongDataToAbsoluteLong(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressToAbsoluteWord:
                    ExecuteMoveLongAddressToAbsoluteWord(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressToAbsoluteLong:
                    ExecuteMoveLongAddressToAbsoluteLong(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressIndirectToAbsoluteLong:
                    ExecuteMoveLongAddressIndirectToAbsoluteLong(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressDisplacementToAbsoluteLong:
                    ExecuteMoveLongAddressDisplacementToAbsoluteLong(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteDataToData:
                    ExecuteMoveByteDataToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteAddressIndirectToData:
                    ExecuteMoveByteAddressIndirectToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteAddressIndirectToAddressIndirect:
                    ExecuteMoveByteAddressIndirectToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.MoveBytePostIncrementToData:
                    ExecuteMoveBytePostIncrementToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteAddressDisplacementToData:
                    ExecuteMoveByteAddressDisplacementToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteBriefIndexedToData:
                    ExecuteMoveByteBriefIndexedToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveBytePcBriefIndexedToData:
                    ExecuteMoveBytePcBriefIndexedToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteAbsoluteLongToData:
                    ExecuteMoveByteAbsoluteLongToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordAbsoluteLongToData:
                    ExecuteMoveWordAbsoluteLongToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordAbsoluteLongToAddress:
                    ExecuteMoveWordAbsoluteLongToAddress(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordAddressIndirectToAddress:
                    ExecuteMoveWordAddressIndirectToAddress(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordAddressDisplacementToAddress:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMoveWordAddressDisplacementToAddress(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordPcBriefIndexedToAddress:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMoveWordPcBriefIndexedToAddress(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordAddressDisplacementToData:
                    ExecuteMoveWordAddressDisplacementToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteAddressDisplacementToAddressIndirect:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMoveByteAddressDisplacementToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordAddressDisplacementToAddressIndirect:
                    ExecuteMoveWordAddressDisplacementToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordAddressIndirectToAddressIndirect:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMoveWordAddressIndirectToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongExtendedToAddressIndirect:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMoveLongExtendedToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.MovePredecrementToPostIncrement:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMovePredecrementToMemory(opcode, postIncrement: true);
                    return true;

                case M68020OpcodeKind.MovePredecrementToAddressDisplacement:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMovePredecrementToMemory(opcode, postIncrement: false);
                    return true;

                case M68020OpcodeKind.MoveBriefIndexedToAddressIndirect:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMoveBriefIndexedToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.MovePostIncrementToAddressIndirect:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMovePostIncrementToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordAddressIndirectToAddressDisplacement:
                    ExecuteMoveIndirectToDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordAddressBriefIndexedToData:
                    ExecuteMoveWordAddressBriefIndexedToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordBriefIndexedToAddressDisplacement:
                    ExecuteMoveWordBriefIndexedToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongPcBriefIndexedToData:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMoveLongPcBriefIndexedToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordPcBriefIndexedToData:
                    ExecuteMoveWordPcBriefIndexedToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordPcBriefIndexedToPredecrement:
                    ExecuteMoveWordPcBriefIndexedToPredecrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordPostIncrementToData:
                    ExecuteMoveWordPostIncrementToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordPostIncrementToAddress:
                    ExecuteMoveWordPostIncrementToAddress(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordPostIncrementToPostIncrement:
                    ExecuteMoveWordPostIncrementToPostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordDataToPostIncrement:
                    ExecuteMoveWordDataToPostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveBytePostIncrementToAddressDisplacement:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMoveBytePostIncrementToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordPostIncrementToAddressDisplacement:
                    ExecuteMoveWordPostIncrementToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordDataToAbsoluteLong:
                    ExecuteMoveWordDataToAbsoluteLong(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordDataToAddressDisplacement:
                    ExecuteMoveWordDataToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordDataToBriefIndexed:
                    ExecuteMoveWordDataToBriefIndexed(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordAddressToAddressDisplacement:
                    ExecuteMoveWordAddressToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordAddressDisplacementToAddressDisplacement:
                    ExecuteMoveWordAddressDisplacementToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteAddressDisplacementToAbsoluteLong:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMoveByteAddressDisplacementToAbsoluteLong(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordAddressDisplacementToAbsoluteLong:
                    ExecuteMoveWordAddressDisplacementToAbsoluteLong(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordPcDisplacementToAddressDisplacement:
                    ExecuteMoveWordPcDisplacementToAddressDisplacement(opcode);
                    return true;
                case M68020OpcodeKind.MovePcDisplacementToData:
                    ExecuteMovePcDisplacementToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordAbsoluteLongToAbsoluteLong:
                    ExecuteMoveWordAbsoluteLongToAbsoluteLong();
                    return true;

                case M68020OpcodeKind.MoveWordAbsoluteLongToAddressDisplacement:
                    ExecuteMoveWordAbsoluteLongToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordAbsoluteLongToPredecrement:
                    ExecuteMoveWordAbsoluteLongToPredecrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordPostIncrementToPredecrement:
                    ExecuteMoveWordPostIncrementToPredecrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordAddressDisplacementToPredecrement:
                    ExecuteMoveWordAddressDisplacementToPredecrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteAddressDisplacementToPredecrement:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMoveByteAddressDisplacementToPredecrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteDataToAbsoluteLong:
                    ExecuteMoveByteDataToAbsoluteLong(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteDataToAddressIndirect:
                    ExecuteMoveByteDataToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteDataToAddressDisplacement:
                    ExecuteMoveByteDataToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteAddressDisplacementToAddressDisplacement:
                    ExecuteMoveByteAddressDisplacementToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteBriefIndexedToAddressDisplacement:
                    ExecuteMoveByteBriefIndexedToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteDataToBriefIndexed:
                    ExecuteMoveByteDataToBriefIndexed(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteBriefIndexedToPredecrement:
                    ExecuteMoveByteBriefIndexedToPredecrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteDataToPostIncrement:
                    ExecuteMoveByteDataToPostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteDataToPredecrement:
                    ExecuteMoveByteDataToPredecrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveBytePostIncrementToPostIncrement:
                    ExecuteMoveBytePostIncrementToPostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteAddressIndirectToAbsoluteLong:
                    ExecuteMoveByteAddressIndirectToAbsoluteLong(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteAbsoluteLongToAbsoluteLong:
                    ExecuteMoveByteAbsoluteLongToAbsoluteLong();
                    return true;

                case M68020OpcodeKind.ImmediateLogicalByteToAbsoluteLong:
                    ExecuteImmediateLogicalToAbsoluteLong(opcode);
                    return true;

                case M68020OpcodeKind.OriWordImmediateToAddressDisplacement:
                    ExecuteOriWordImmediateToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.OriLongImmediateToAddressDisplacement:
                    ExecuteOriLongImmediateToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.OriWordImmediateToAddressIndirect:
                    ExecuteOriWordImmediateToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.AddiByteImmediateToData:
                    ExecuteAddiByteImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.AddiByteImmediateToAddressIndirect:
                case M68020OpcodeKind.AddiWordImmediateToAddressIndirect:
                case M68020OpcodeKind.AddiLongImmediateToAddressIndirect:
                    ExecuteAddiImmediateToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.AddiWordImmediateToAddressDisplacement:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteAddiWordImmediateToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.AddiByteImmediateToAddressDisplacement:
                    ExecuteAddiByteImmediateToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.AddiWordImmediateToData:
                    ExecuteAddiWordImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.AddiLongImmediateToData:
                    ExecuteAddiLongImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.AddiLongImmediateToAddressDisplacement:
                    ExecuteAddiLongImmediateToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.AddiLongImmediateToAbsoluteLong:
                    ExecuteAddiLongImmediateToAbsoluteLong();
                    return true;

                case M68020OpcodeKind.SubiByteImmediateToData:
                    ExecuteSubiByteImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.SubiWordImmediateToData:
                    ExecuteSubiWordImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.SubiLongImmediateToAddressDisplacement:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteSubiLongImmediateToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.SubiWordImmediateToAddressDisplacement:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteSubiWordImmediateToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.SubiByteImmediateToAddressDisplacement:
                    ExecuteSubiByteImmediateToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.SubiLongImmediateToData:
                    ExecuteSubiLongImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.SubByteDataToData:
                    ExecuteSubByteDataToData(opcode);
                    return true;

                case M68020OpcodeKind.SubByteAddressDisplacementToData:
                    ExecuteSubByteAddressDisplacementToData(opcode);
                    return true;

                case M68020OpcodeKind.SubWordDataToAddressDisplacement:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteSubWordDataToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.SubByteDataToAddressDisplacement:
                    ExecuteSubByteDataToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.SubWordDataToData:
                    ExecuteSubWordDataToData(opcode);
                    return true;

                case M68020OpcodeKind.NegAddressDisplacement:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteNegAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.CmpBriefIndexedToData:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteCmpBriefIndexedToData(opcode);
                    return true;

                case M68020OpcodeKind.SubBriefIndexedToData:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteSubBriefIndexedToData(opcode);
                    return true;

                case M68020OpcodeKind.SubDataToAddressIndirect:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteSubDataToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.SubPostIncrementToData:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteSubPostIncrementToData(opcode);
                    return true;

                case M68020OpcodeKind.SubWordAddressIndirectToData:
                    ExecuteSubWordAddressIndirectToData(opcode);
                    return true;
                case M68020OpcodeKind.SubIndirectToData:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteSubIndirectToData(opcode);
                    return true;

                case M68020OpcodeKind.SubWordAddressDisplacementToData:
                    ExecuteSubWordAddressDisplacementToData(opcode);
                    return true;

                case M68020OpcodeKind.SubWordDataToPostIncrement:
                    ExecuteSubWordDataToPostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.SubLongDataToData:
                    ExecuteSubLongDataToData(opcode);
                    return true;

                case M68020OpcodeKind.SubLongAddressToData:
                    ExecuteSubLongAddressToData(opcode);
                    return true;

                case M68020OpcodeKind.SubLongAddressDisplacementToData:
                    ExecuteSubLongAddressDisplacementToData(opcode);
                    return true;

                case M68020OpcodeKind.SubLongImmediateToData:
                    ExecuteSubLongImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.SubImmediateToData:
                    ExecuteSubImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.SubLongDataToAddressDisplacement:
                    ExecuteSubLongDataToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.AddByteDataToData:
                    ExecuteAddByteDataToData(opcode);
                    return true;

                case M68020OpcodeKind.AddWordDataToData:
                    ExecuteAddWordDataToData(opcode);
                    return true;

                case M68020OpcodeKind.AddWordAddressToData:
                    ExecuteAddWordDataToData(opcode, addressSource: true);
                    return true;

                case M68020OpcodeKind.AddByteAddressIndirectToData:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteAddByteAddressIndirectToData(opcode);
                    return true;

                case M68020OpcodeKind.AddWordAddressIndirectToData:
                    ExecuteAddWordAddressIndirectToData(opcode);
                    return true;

                case M68020OpcodeKind.AddWordPredecrementToData:
                    ExecuteAddWordPredecrementToData(opcode);
                    return true;

                case M68020OpcodeKind.AddWordPostIncrementToData:
                    ExecuteAddWordPostIncrementToData(opcode);
                    return true;

                case M68020OpcodeKind.AddByteAddressDisplacementToData:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteAddByteAddressDisplacementToData(opcode);
                    return true;

                case M68020OpcodeKind.AddWordAddressDisplacementToData:
                    ExecuteAddWordAddressDisplacementToData(opcode);
                    return true;

                case M68020OpcodeKind.AddWordImmediateToData:
                    ExecuteAddWordImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.AddByteImmediateToData:
                    ExecuteAddByteImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.AddLongDataToData:
                    ExecuteAddLongDataToData(opcode);
                    return true;

                case M68020OpcodeKind.AddSmallDataToAddressIndirect:
                    ExecuteAddSmallDataToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.AddLongDataToAddressIndirect:
                    ExecuteAddLongDataToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.AddDataToPostIncrement:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteAddDataToPostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.AddLongDataToAbsoluteLong:
                    ExecuteArithmeticDataToAbsoluteLong(opcode, subtract: false);
                    return true;

                case M68020OpcodeKind.SubDataToAbsoluteLong:
                    ExecuteArithmeticDataToAbsoluteLong(opcode, subtract: true);
                    return true;

                case M68020OpcodeKind.AddLongAddressIndirectToData:
                    ExecuteAddLongAddressIndirectToData(opcode);
                    return true;

                case M68020OpcodeKind.AddByteOrWordBriefIndexedToData:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteAddByteOrWordBriefIndexedToData(opcode);
                    return true;

                case M68020OpcodeKind.AddLongBriefIndexedToData:
                    ExecuteAddLongBriefIndexedToData(opcode);
                    return true;

                case M68020OpcodeKind.AddLongAbsoluteLongToData:
                    ExecuteAddLongAbsoluteLongToData(opcode);
                    return true;

                case M68020OpcodeKind.AddLongAddressToData:
                    ExecuteAddLongAddressToData(opcode);
                    return true;

                case M68020OpcodeKind.AddxLongDataToData:
                    ExecuteAddxLongDataToData(opcode);
                    return true;

                case M68020OpcodeKind.SubxByteDataToData:
                case M68020OpcodeKind.SubxWordDataToData:
                case M68020OpcodeKind.SubxLongDataToData:
                    ExecuteSubxDataToData(opcode);
                    return true;

                case M68020OpcodeKind.AddxByteDataToData:
                    ExecuteAddxByteDataToData(opcode);
                    return true;

                case M68020OpcodeKind.AddxWordDataToData:
                    ExecuteAddxWordDataToData(opcode);
                    return true;

                case M68020OpcodeKind.AddBytePostIncrementToData:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteAddBytePostIncrementToData(opcode);
                    return true;

                case M68020OpcodeKind.AddLongPostIncrementToData:
                    ExecuteAddLongPostIncrementToData(opcode);
                    return true;

                case M68020OpcodeKind.AddLongAddressDisplacementToData:
                    ExecuteAddLongAddressDisplacementToData(opcode);
                    return true;

                case M68020OpcodeKind.AddLongPcDisplacementToData:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteAddLongPcDisplacementToData(opcode);
                    return true;

                case M68020OpcodeKind.AddLongImmediateToData:
                    ExecuteAddLongImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.AddByteDataToAddressDisplacement:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteAddByteDataToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.AddWordDataToAddressDisplacement:
                    ExecuteAddWordDataToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.AddLongDataToAddressDisplacement:
                    ExecuteAddLongDataToAddressDisplacement(opcode);
                    return true;
                case M68020OpcodeKind.AddDataToBriefIndexed:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteAddDataToBriefIndexed(opcode);
                    return true;
                case M68020OpcodeKind.CmpiImmediateToBriefIndexed:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteCmpiImmediateToBriefIndexed(opcode);
                    return true;
                case M68020OpcodeKind.CmpPcDisplacementOrAbsoluteWordToData:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteCmpPcDisplacementOrAbsoluteWordToData(opcode);
                    return true;
                case M68020OpcodeKind.BitModifyDynamicBriefIndexed:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteBitModifyDynamicBriefIndexed(opcode);
                    return true;
                case M68020OpcodeKind.BitDynamicAbsoluteLong:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteBitDynamicAbsoluteLong(opcode);
                    return true;
                case M68020OpcodeKind.BitImmediateIndirect:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteBitImmediateIndirect(opcode);
                    return true;
                case M68020OpcodeKind.NotAbsoluteWord:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteNotAbsoluteWord(opcode);
                    return true;

                case M68020OpcodeKind.AddqLongData:
                    ExecuteAddqLongData(opcode);
                    return true;

                case M68020OpcodeKind.AddqWordAddress:
                    ExecuteAddqWordAddress(opcode);
                    return true;

                case M68020OpcodeKind.AddqWordData:
                    ExecuteAddqWordData(opcode);
                    return true;

                case M68020OpcodeKind.AddqLongAddress:
                    ExecuteAddqLongAddress(opcode);
                    return true;

                case M68020OpcodeKind.AddqLongAddressIndirect:
                    ExecuteAddqLongAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.AddqByteAddressDisplacement:
                    ExecuteAddqByteAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.AddqByteAddressIndirect:
                    ExecuteAddqByteAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.AddqByteData:
                    ExecuteAddqByteData(opcode);
                    return true;

                case M68020OpcodeKind.QuickWordBriefIndexed:
                    ExecuteQuickWordBriefIndexed(opcode);
                    return true;

                case M68020OpcodeKind.AddqWordAddressDisplacement:
                    ExecuteAddqWordAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.AddqLongAddressDisplacement:
                    ExecuteAddqLongAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.AddqLongAbsoluteLong:
                    ExecuteAddqLongAbsoluteLong(opcode);
                    return true;

                case M68020OpcodeKind.SubqAbsoluteLong:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteSubqAbsoluteLong(opcode);
                    return true;

                case M68020OpcodeKind.SubqByteData:
                    ExecuteSubqByteData(opcode);
                    return true;

                case M68020OpcodeKind.SubqWordData:
                    ExecuteSubqWordData(opcode);
                    return true;

                case M68020OpcodeKind.SubqLongData:
                    ExecuteSubqLongData(opcode);
                    return true;

                case M68020OpcodeKind.SubqWordAddress:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteSubqWordAddress(opcode);
                    return true;

                case M68020OpcodeKind.SubqLongAddress:
                    ExecuteSubqLongAddress(opcode);
                    return true;

                case M68020OpcodeKind.SubqLongAddressIndirect:
                    ExecuteSubqLongAddressIndirect(opcode);
                    return true;
                case M68020OpcodeKind.QuickIndirect:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteQuickIndirect(opcode);
                    return true;
                case M68020OpcodeKind.BitImmediatePostIncrement:
                    ExecuteBitImmediatePostIncrement(opcode);
                    return true;
                case M68020OpcodeKind.EoriImmediateToAddressDisplacement:
                    ExecuteEoriImmediateToAddressDisplacement(opcode);
                    return true;
                case M68020OpcodeKind.QuickPostIncrement:
                    ExecuteQuickPostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.SubqLongAddressDisplacement:
                    ExecuteSubqLongAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.SubqByteAddressDisplacement:
                    ExecuteSubqByteAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.SubqWordAddressDisplacement:
                    ExecuteSubqWordAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.AddaWordImmediateToAddress:
                    ExecuteAddaWordImmediateToAddress(opcode);
                    return true;

                case M68020OpcodeKind.AddaWordDataToAddress:
                    ExecuteAddaWordDataToAddress(opcode);
                    return true;

                case M68020OpcodeKind.AddaWordAddressToAddress:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteAddaWordAddressToAddress(opcode);
                    return true;

                case M68020OpcodeKind.AddaWordAddressDisplacementToAddress:
                    ExecuteAddaWordAddressDisplacementToAddress(opcode);
                    return true;

                case M68020OpcodeKind.AddaLongBriefIndexedToAddress:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteAddaLongBriefIndexedToAddress(opcode);
                    return true;

                case M68020OpcodeKind.AddaWordBriefIndexedToAddress:
                    ExecuteAddaWordBriefIndexedToAddress(opcode);
                    return true;

                case M68020OpcodeKind.AddaLongImmediateToAddress:
                    ExecuteAddaLongImmediateToAddress(opcode);
                    return true;

                case M68020OpcodeKind.AddaLongDataToAddress:
                    ExecuteAddaLongDataToAddress(opcode);
                    return true;

                case M68020OpcodeKind.AddaLongAddressToAddress:
                    ExecuteAddaLongAddressToAddress(opcode);
                    return true;

                case M68020OpcodeKind.AddaLongAddressDisplacementToAddress:
                    ExecuteAddaLongAddressDisplacementToAddress(opcode);
                    return true;

                case M68020OpcodeKind.SubaAddressIndirectToAddress:
                    ExecuteSubaAddressIndirectToAddress(opcode);
                    return true;

                case M68020OpcodeKind.SubaPostIncrementToAddress:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteSubaPostIncrementToAddress(opcode);
                    return true;

                case M68020OpcodeKind.SubaLongImmediateToAddress:
                    ExecuteSubaLongImmediateToAddress(opcode);
                    return true;

                case M68020OpcodeKind.SubaLongDataToAddress:
                    ExecuteSubaLongDataToAddress(opcode);
                    return true;

                case M68020OpcodeKind.SubaLongAddressToAddress:
                    ExecuteSubaLongAddressToAddress(opcode);
                    return true;

                case M68020OpcodeKind.SubaWordAddressDisplacementToAddress:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteSubaWordAddressDisplacementToAddress(opcode);
                    return true;

                case M68020OpcodeKind.SubaLongAddressDisplacementToAddress:
                    ExecuteSubaLongAddressDisplacementToAddress(opcode);
                    return true;

                case M68020OpcodeKind.SubaLongPcDisplacementToAddress:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteSubaLongPcDisplacementToAddress(opcode);
                    return true;

                case M68020OpcodeKind.SubaWordImmediateToAddress:
                    ExecuteSubaWordImmediateToAddress(opcode);
                    return true;

                case M68020OpcodeKind.SubaWordDataToAddress:
                    ExecuteSubaWordDataToAddress(opcode);
                    return true;

                case M68020OpcodeKind.ChkWordImmediate:
                    ExecuteChkWordImmediate(opcode);
                    return true;

                case M68020OpcodeKind.LongMultiplyDivide:
                    ExecuteLongMultiplyDivide(opcode);
                    return true;

                case M68020OpcodeKind.Cas2:
                    ExecuteCas2(opcode);
                    return true;

                case M68020OpcodeKind.Cas:
                    ExecuteCas(opcode);
                    return true;

                case M68020OpcodeKind.DivideWordUnsigned:
                    ExecuteDivideWord(opcode, signed: false);
                    return true;

                case M68020OpcodeKind.DivideWordSigned:
                    ExecuteDivideWord(opcode, signed: true);
                    return true;

                case M68020OpcodeKind.AndiWordImmediateToData:
                    ExecuteAndiWordImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.AndiWordImmediateToAddressDisplacement:
                    ExecuteAndiWordImmediateToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.AndiLongImmediateToAddressDisplacement:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteAndiLongImmediateToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.AndiByteImmediateToData:
                    ExecuteAndiByteImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.AndiWideImmediateToAddressIndirect:
                    ExecuteAndiWideImmediateToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.AndiByteImmediateToAddressIndirect:
                    ExecuteAndiByteImmediateToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.AndiByteImmediateToAddressDisplacement:
                    ExecuteAndiByteImmediateToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.AndByteImmediateToData:
                    ExecuteAndByteImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.AndWordImmediateToData:
                    ExecuteAndWordImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.AndLongImmediateToData:
                    ExecuteAndLongImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.AndLongEffectiveAddressToData:
                    ExecuteAndLongEffectiveAddressToData(opcode);
                    return true;

                case M68020OpcodeKind.OrWordImmediateToData:
                    ExecuteOrWordImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.OrByteImmediateToData:
                    ExecuteOrByteImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.OrWordAddressDisplacementToData:
                    ExecuteOrWordAddressDisplacementToData(opcode);
                    return true;

                case M68020OpcodeKind.OriWordImmediateToData:
                    ExecuteOriWordImmediateToData(opcode);
                    return true;
                case M68020OpcodeKind.ImmediateLogicalData:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteImmediateLogicalDataForm(opcode);
                    return true;

                case M68020OpcodeKind.OrLongEffectiveAddressToData:
                    ExecuteOrLongEffectiveAddressToData(opcode);
                    return true;

                case M68020OpcodeKind.EoriWordImmediateToData:
                    ExecuteEoriWordImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.EoriLongImmediateToData:
                    ExecuteEoriLongImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.EorLongDataToAddressDisplacement:
                    ExecuteEorLongDataToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.EorLongDataToData:
                    ExecuteEorLongDataToData(opcode);
                    return true;

                case M68020OpcodeKind.EorByteDataToAddressDisplacement:
                    ExecuteEorByteDataToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.EorByteDataToData:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteEorByteDataToData(opcode);
                    return true;

                case M68020OpcodeKind.EorWordDataToData:
                    ExecuteEorWordDataToData(opcode);
                    return true;

                case M68020OpcodeKind.MultiplyWordUnsigned:
                    ExecuteMultiplyWord(opcode, signed: false);
                    return true;

                case M68020OpcodeKind.MultiplyWordSigned:
                    ExecuteMultiplyWord(opcode, signed: true);
                    return true;

                case M68020OpcodeKind.AndByteDataToData:
                    ExecuteAndByteDataToData(opcode);
                    return true;

                case M68020OpcodeKind.AndByteAddressDisplacementToData:
                    ExecuteAndByteAddressDisplacementToData(opcode);
                    return true;

                case M68020OpcodeKind.AndByteDataToAddressDisplacement:
                    ExecuteAndByteDataToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.AndDataToAddressIndirect:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteAndDataToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.ClrBriefIndexed:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteClrBriefIndexed(opcode);
                    return true;

                case M68020OpcodeKind.OrByteDataToData:
                    ExecuteOrByteDataToData(opcode);
                    return true;

                case M68020OpcodeKind.OrWordDataToData:
                    ExecuteOrWordDataToData(opcode);
                    return true;

                case M68020OpcodeKind.OrDataToAbsoluteLong:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteOrDataToAbsoluteLong(opcode);
                    return true;

                case M68020OpcodeKind.OrDataToPostIncrement:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteLogicalDataToPostIncrement(opcode, and: false);
                    return true;

                case M68020OpcodeKind.AndBriefIndexedToData:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteAndBriefIndexedToData(opcode);
                    return true;

                case M68020OpcodeKind.AndiImmediateToPostIncrement:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteAndiImmediateToPostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.AndDataToPostIncrement:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteLogicalDataToPostIncrement(opcode, and: true);
                    return true;

                case M68020OpcodeKind.OrWordDataToAddressIndirect:
                    ExecuteOrWordDataToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.OrWordDataToAddressDisplacement:
                    ExecuteOrWordDataToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.OrByteAddressDisplacementToData:
                    ExecuteOrByteAddressDisplacementToData(opcode);
                    return true;

                case M68020OpcodeKind.OrByteDataToAddressIndirect:
                    ExecuteOrByteDataToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.OrByteDataToAddressDisplacement:
                    ExecuteOrByteDataToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.OrLongDataToAddressIndirect:
                    ExecuteOrLongDataToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.OrLongDataToAddressDisplacement:
                    ExecuteOrLongDataToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.AndWordDataToData:
                    ExecuteAndWordDataToData(opcode);
                    return true;

                case M68020OpcodeKind.AndAddressIndirectToData:
                    ExecuteAndAddressIndirectToData(opcode);
                    return true;

                case M68020OpcodeKind.AndWordAddressDisplacementToData:
                    ExecuteAndWordAddressDisplacementToData(opcode);
                    return true;

                case M68020OpcodeKind.AndWordDataToAddressDisplacement:
                    ExecuteAndWordDataToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.AndLongDataToAddressDisplacement:
                    ExecuteAndLongDataToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.ExgDataAddress:
                    ExecuteExgDataAddress(opcode);
                    return true;

                case M68020OpcodeKind.ExgDataData:
                    ExecuteExgDataData(opcode);
                    return true;

                case M68020OpcodeKind.ExgAddressAddress:
                    ExecuteExgAddressAddress(opcode);
                    return true;

                case M68020OpcodeKind.BcdByteAdd:
                    ExecuteBcdByte(opcode, subtract: false);
                    return true;

                case M68020OpcodeKind.BcdByteSubtract:
                    ExecuteBcdByte(opcode, subtract: true);
                    return true;

                case M68020OpcodeKind.OriByteImmediateToData:
                    ExecuteOriByteImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.OriByteImmediateToAddressIndirect:
                    ExecuteOriByteImmediateToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.OriByteImmediateToAddressDisplacement:
                    ExecuteOriByteImmediateToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.BitImmediateBriefIndexed:
                    ExecuteBitImmediateBriefIndexed(opcode);
                    return true;

                case M68020OpcodeKind.BtstByteImmediateAbsoluteLong:
                    ExecuteBtstByteImmediateAbsoluteLong();
                    return true;

                case M68020OpcodeKind.BtstByteImmediateAddressIndirect:
                    ExecuteBtstByteImmediateAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.BtstByteImmediatePostIncrement:
                    ExecuteBtstByteImmediatePostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.BtstByteImmediateAddressDisplacement:
                    ExecuteBtstByteImmediateAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.BchgByteImmediateAbsoluteLong:
                    ExecuteBchgByteImmediateAbsoluteLong();
                    return true;

                case M68020OpcodeKind.BchgByteImmediateAddressDisplacement:
                    ExecuteBchgByteImmediateAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.BclrByteImmediateAbsoluteLong:
                    ExecuteBclrByteImmediateAbsoluteLong();
                    return true;

                case M68020OpcodeKind.BclrByteImmediateAddressDisplacement:
                    ExecuteBclrByteImmediateAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.BsetByteImmediateAbsoluteLong:
                    ExecuteBsetByteImmediateAbsoluteLong();
                    return true;

                case M68020OpcodeKind.BsetByteImmediateAddressDisplacement:
                    ExecuteBsetByteImmediateAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.BitModifyDynamicAddressDisplacement:
                    ExecuteBitModifyDynamicAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.BsetByteDynamicAddressDisplacement:
                    ExecuteBsetByteDynamicAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.BtstImmediateData:
                    ExecuteBtstImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.BtstDynamicData:
                    ExecuteBtstDynamicData(opcode);
                    return true;

                case M68020OpcodeKind.BtstByteDynamicAddressIndirect:
                    ExecuteBtstByteDynamicAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.BtstByteDynamicAddressDisplacement:
                    ExecuteBtstByteDynamicAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.BtstByteDynamicBriefIndexed:
                    ExecuteBtstByteDynamicBriefIndexed(opcode);
                    return true;

                case M68020OpcodeKind.BtstByteDynamicAbsoluteLong:
                    ExecuteBtstByteDynamicAbsoluteLong(opcode);
                    return true;

                case M68020OpcodeKind.BchgByteDynamicAddressIndirect:
                    ExecuteBchgByteDynamicAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.BsetImmediateData:
                    ExecuteBsetImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.BsetDynamicData:
                    ExecuteBsetDynamicData(opcode);
                    return true;

                case M68020OpcodeKind.BclrImmediateData:
                    ExecuteBclrImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.BchgImmediateData:
                    ExecuteBchgImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.BclrDynamicData:
                    ExecuteBclrDynamicData(opcode);
                    return true;

                case M68020OpcodeKind.SwapData:
                    ExecuteSwapData(opcode);
                    return true;

                case M68020OpcodeKind.ExtLongData:
                    ExecuteExtLongData(opcode);
                    return true;

                case M68020OpcodeKind.ExtWordData:
                    ExecuteExtWordData(opcode);
                    return true;

                case M68020OpcodeKind.TstByteData:
                    ExecuteTstByteData(opcode);
                    return true;

                case M68020OpcodeKind.TstWordData:
                    ExecuteTstWordData(opcode);
                    return true;

                case M68020OpcodeKind.TstWordAbsoluteLong:
                    ExecuteTstWordAbsoluteLong();
                    return true;

                case M68020OpcodeKind.TstByteAbsoluteLong:
                    ExecuteTstByteAbsoluteLong();
                    return true;

                case M68020OpcodeKind.TstWordAddressIndirect:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteTstWordAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.TstByteAddressIndirect:
                    ExecuteTstByteAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.TstWordOrLongPostIncrement:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteTstWordOrLongPostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.TstBytePostIncrement:
                    ExecuteTstBytePostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.TstByteAddressDisplacement:
                    ExecuteTstByteAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.TstWordBriefIndexed:
                    ExecuteTstWordBriefIndexed(opcode);
                    return true;

                case M68020OpcodeKind.TstByteBriefIndexed:
                    ExecuteTstByteBriefIndexed(opcode);
                    return true;

                case M68020OpcodeKind.TstWordAddressDisplacement:
                    ExecuteTstWordAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.TstLongData:
                    ExecuteTstLongData(opcode);
                    return true;

                case M68020OpcodeKind.TstLongAddressIndirect:
                    ExecuteTstLongAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.TstLongAddressDisplacement:
                    ExecuteTstLongAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.TstLongBriefIndexed:
                    ExecuteTstLongBriefIndexed(opcode);
                    return true;

                case M68020OpcodeKind.BitField:
                    ExecuteBitField(opcode);
                    return true;

                case M68020OpcodeKind.LsrWordImmediateData:
                    ExecuteLsrWordImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.LsrByteRegisterData:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteLsrByteRegisterData(opcode);
                    return true;

                case M68020OpcodeKind.LsrWordRegisterData:
                    ExecuteLsrWordRegisterData(opcode);
                    return true;

                case M68020OpcodeKind.LsrByteImmediateData:
                    ExecuteLsrByteImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.LsrWordAddressDisplacement:
                    ExecuteLsrWordAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.AsrByteImmediateData:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteAsrByteImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.AsrLongImmediateData:
                    ExecuteAsrLongImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.AsrLongRegisterData:
                    ExecuteAsrLongRegisterData(opcode);
                    return true;

                case M68020OpcodeKind.AsrWordImmediateData:
                    ExecuteAsrWordImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.LsrLongImmediateData:
                    ExecuteLsrLongImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.LsrLongRegisterData:
                    ExecuteLsrLongRegisterData(opcode);
                    return true;

                case M68020OpcodeKind.AslLongImmediateData:
                    ExecuteAslLongImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.AslLongRegisterData:
                    ExecuteAslLongRegisterData(opcode);
                    return true;

                case M68020OpcodeKind.AslByteRegisterData:
                    ExecuteAslByteRegisterData(opcode);
                    return true;

                case M68020OpcodeKind.AslWordImmediateData:
                    ExecuteAslWordImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.LslLongImmediateData:
                    ExecuteLslLongImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.LslLongRegisterData:
                    ExecuteLslLongRegisterData(opcode);
                    return true;

                case M68020OpcodeKind.LslWordRegisterData:
                    ExecuteLslWordRegisterData(opcode);
                    return true;

                case M68020OpcodeKind.LslByteImmediateData:
                    ExecuteLslByteImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.LslWordImmediateData:
                    ExecuteLslWordImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.RorByteImmediateData:
                    ExecuteRorByteImmediateData(opcode);
                    return true;
                case M68020OpcodeKind.RotateRegisterData:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteRotateRegisterData(opcode);
                    return true;
                case M68020OpcodeKind.RotateExtendData:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteRotateExtendData(opcode);
                    return true;
                case M68020OpcodeKind.ShiftRegisterData:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteShiftRegisterData(opcode);
                    return true;

                case M68020OpcodeKind.RorWordImmediateData:
                    ExecuteRorWordImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.RorLongImmediateData:
                    ExecuteRorLongImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.RolWordImmediateData:
                    ExecuteRolWordImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.RolLongImmediateData:
                    ExecuteRolLongImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.CmpiByteImmediateToData:
                    ExecuteCmpiByteImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.CmpiByteImmediateToAddressIndirect:
                    ExecuteCmpiByteImmediateToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.CmpiByteImmediateToPredecrement:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteCmpiByteImmediateToPredecrement(opcode);
                    return true;

                case M68020OpcodeKind.CmpiByteImmediateToAddressDisplacement:
                    ExecuteCmpiByteImmediateToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.CmpiWordImmediateToData:
                    ExecuteCmpiWordImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.CmpiWordImmediateToAddressIndirect:
                    ExecuteCmpiWordImmediateToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.CmpiWordImmediateToAddressDisplacement:
                    ExecuteCmpiWordImmediateToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.CmpiByteImmediateToAbsoluteLong:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteCmpiByteImmediateToAbsoluteLong();
                    return true;

                case M68020OpcodeKind.CmpiWordImmediateToAbsoluteLong:
                    ExecuteCmpiWordImmediateToAbsoluteLong();
                    return true;

                case M68020OpcodeKind.CmpiLongImmediateToData:
                    ExecuteCmpiLongImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.CmpiSmallImmediateToPostIncrement:
                    ExecuteCmpiSmallImmediateToPostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.CmpiLongImmediateToPostIncrement:
                    ExecuteCmpiLongImmediateToPostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.CmpiLongImmediateToAddressIndirect:
                    ExecuteCmpiLongImmediateToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.CmpiLongImmediateToAddressDisplacement:
                    ExecuteCmpiLongImmediateToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.CmpaLongImmediateToAddress:
                    ExecuteCmpaLongImmediateToAddress(opcode);
                    return true;

                case M68020OpcodeKind.CmpaWordImmediateToAddress:
                    ExecuteCmpaWordImmediateToAddress(opcode);
                    return true;

                case M68020OpcodeKind.CmpaWordDataToAddress:
                    ExecuteCmpaWordDataToAddress(opcode);
                    return true;

                case M68020OpcodeKind.CmpaWordAddressToAddress:
                    ExecuteCmpaWordAddressToAddress(opcode);
                    return true;

                case M68020OpcodeKind.CmpaLongDataToAddress:
                    ExecuteCmpaLongDataToAddress(opcode);
                    return true;

                case M68020OpcodeKind.CmpaLongAddressToAddress:
                    ExecuteCmpaLongAddressToAddress(opcode);
                    return true;

                case M68020OpcodeKind.CmpaLongAddressIndirectToAddress:
                    ExecuteCmpaLongAddressIndirectToAddress(opcode);
                    return true;

                case M68020OpcodeKind.CmpaWordAddressDisplacementToAddress:
                    ExecuteCmpaWordAddressDisplacementToAddress(opcode);
                    return true;

                case M68020OpcodeKind.CmpaLongAddressDisplacementToAddress:
                    ExecuteCmpaLongAddressDisplacementToAddress(opcode);
                    return true;

                case M68020OpcodeKind.CmpaLongPredecrementToAddress:
                    ExecuteCmpaLongPredecrementToAddress(opcode);
                    return true;

                case M68020OpcodeKind.CmpaLongPostIncrementToAddress:
                    ExecuteCmpaLongPostIncrementToAddress(opcode);
                    return true;

                case M68020OpcodeKind.CmpLongDataToData:
                    ExecuteCmpLongDataToData(opcode);
                    return true;

                case M68020OpcodeKind.CmpLongImmediateToData:
                    ExecuteCmpLongImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.CmpLongAbsoluteLongToData:
                    ExecuteCmpAbsoluteLongToData(opcode);
                    return true;

                case M68020OpcodeKind.CmpLongAddressToData:
                    ExecuteCmpLongAddressToData(opcode);
                    return true;

                case M68020OpcodeKind.CmpLongAddressIndirectToData:
                    ExecuteCmpLongAddressIndirectToData(opcode);
                    return true;

                case M68020OpcodeKind.CmpLongPostIncrementToData:
                    ExecuteCmpLongPostIncrementToData(opcode);
                    return true;

                case M68020OpcodeKind.CmpLongAddressDisplacementToData:
                    ExecuteCmpLongAddressDisplacementToData(opcode);
                    return true;

                case M68020OpcodeKind.CmpByteDataToData:
                    ExecuteCmpByteDataToData(opcode);
                    return true;

                case M68020OpcodeKind.CmpByteImmediateToData:
                    ExecuteCmpByteImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.CmpByteAddressIndirectToData:
                    ExecuteCmpByteAddressIndirectToData(opcode);
                    return true;

                case M68020OpcodeKind.CmpBytePostIncrementToData:
                    ExecuteCmpBytePostIncrementToData(opcode);
                    return true;

                case M68020OpcodeKind.CmpByteAddressDisplacementToData:
                    ExecuteCmpByteAddressDisplacementToData(opcode);
                    return true;

                case M68020OpcodeKind.CmpByteAbsoluteLongToData:
                    ExecuteCmpByteAbsoluteLongToData(opcode);
                    return true;

                case M68020OpcodeKind.CmpWordDataToData:
                    ExecuteCmpWordDataToData(opcode);
                    return true;

                case M68020OpcodeKind.CmpWordAddressToData:
                    ExecuteCmpWordAddressToData(opcode);
                    return true;

                case M68020OpcodeKind.CmpWordImmediateToData:
                    ExecuteCmpWordImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.CmpWordAddressIndirectToData:
                    ExecuteCmpWordAddressIndirectToData(opcode);
                    return true;

                case M68020OpcodeKind.CmpWordPostIncrementToData:
                    ExecuteCmpWordPostIncrementToData(opcode);
                    return true;

                case M68020OpcodeKind.CmpWordAddressDisplacementToData:
                    ExecuteCmpWordAddressDisplacementToData(opcode);
                    return true;

                case M68020OpcodeKind.CmpmBytePostIncrement:
                    ExecuteCmpmPostIncrement(opcode, M68kOperandSize.Byte, M68kInstructionTimingKey.CmpmBytePostIncrement);
                    return true;

                case M68020OpcodeKind.AndPcBriefIndexedToData:
                    ExecuteAndPcBriefIndexedToData(opcode);
                    return true;

                case M68020OpcodeKind.ClrWordAbsoluteLong:
                    ExecuteClrWordAbsoluteLong(opcode);
                    return true;

                case M68020OpcodeKind.AndSmallAbsoluteLongToData:
                    ExecuteAndSmallAbsoluteLongToData(opcode);
                    return true;

                case M68020OpcodeKind.NotAbsoluteLong:
                    ExecuteNotMemory(opcode, addressIndirect: false);
                    return true;

                case M68020OpcodeKind.NotAddressIndirect:
                case M68020OpcodeKind.NotPostIncrement:
                    ExecuteNotMemory(opcode, addressIndirect: true);
                    return true;

                case M68020OpcodeKind.TstLongAbsoluteLong:
                    ExecuteTstLongAbsoluteLong(opcode);
                    return true;

                case M68020OpcodeKind.AddaPostIncrementToAddress:
                    ExecuteAddaIndirectToAddress(opcode, postIncrement: true);
                    return true;

                case M68020OpcodeKind.AddaAddressIndirectToAddress:
                    ExecuteAddaIndirectToAddress(opcode, postIncrement: false);
                    return true;

                case M68020OpcodeKind.AddaAbsoluteLongToAddress:
                    ExecuteAddaAbsoluteLongToAddress(opcode);
                    return true;
                case M68020OpcodeKind.SubaAbsoluteLongToAddress:
                    ExecuteAddaAbsoluteLongToAddress(opcode, subtract: true);
                    return true;

                case M68020OpcodeKind.MoveWordAddressIndirectToAbsoluteLong:
                    ExecuteMoveWordAddressIndirectToAbsoluteLong(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordAbsoluteLongToPostIncrement:
                    ExecuteMoveAbsoluteLongToPostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordPcDisplacementToAbsoluteLong:
                    ExecuteMoveWordPcDisplacementToAbsoluteLong(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordAddressToPostIncrement:
                    ExecuteMoveWordAddressToPostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAbsoluteLongToAbsoluteLong:
                    ExecuteMoveLongAbsoluteLongToAbsoluteLong(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordPcBriefIndexedToAddressDisplacement:
                    ExecuteMoveWordPcBriefIndexedToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordAddressDisplacementToBriefIndexed:
                    ExecuteMoveWordAddressDisplacementToBriefIndexed(opcode);
                    return true;

                case M68020OpcodeKind.AddSmallAbsoluteLongToData:
                    ExecuteArithmeticAbsoluteLongToData(opcode);
                    return true;

                case M68020OpcodeKind.SubAbsoluteLongToData:
                    ExecuteArithmeticAbsoluteLongToData(opcode, subtract: true);
                    return true;

                case M68020OpcodeKind.MoveWordBriefIndexedToAddress:
                    ExecuteMoveWordBriefIndexedToAddress(opcode);
                    return true;

                case M68020OpcodeKind.AddiSmallImmediateToAbsoluteLong:
                    ExecuteImmediateArithmeticToAbsoluteLong(opcode, subtract: false);
                    return true;

                case M68020OpcodeKind.SubiImmediateToAbsoluteLong:
                    ExecuteImmediateArithmeticToAbsoluteLong(opcode, subtract: true);
                    return true;

                case M68020OpcodeKind.BitDynamicAddressIndirect:
                    ExecuteBitDynamicAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.AddSmallPcDisplacementToData:
                    ExecuteArithmeticPcDisplacementToData(opcode, subtract: false);
                    return true;

                case M68020OpcodeKind.SubPcDisplacementToData:
                    ExecuteArithmeticPcDisplacementToData(opcode, subtract: true);
                    return true;

                case M68020OpcodeKind.AddPcBriefIndexedToData:
                    ExecuteArithmeticPcDisplacementToData(opcode, subtract: false, indexed: true);
                    return true;

                case M68020OpcodeKind.SubPcBriefIndexedToData:
                    ExecuteArithmeticPcDisplacementToData(opcode, subtract: true, indexed: true);
                    return true;

                case M68020OpcodeKind.AddqWordAbsoluteLong:
                    ExecuteAddqWordAbsoluteLong(opcode);
                    return true;

                case M68020OpcodeKind.CmpmWordPostIncrement:
                    ExecuteCmpmPostIncrement(opcode, M68kOperandSize.Word, M68kInstructionTimingKey.CmpmWordPostIncrement);
                    return true;

                case M68020OpcodeKind.CmpmLongPostIncrement:
                    ExecuteCmpmPostIncrement(opcode, M68kOperandSize.Long, M68kInstructionTimingKey.CmpmLongPostIncrement);
                    return true;

                case M68020OpcodeKind.CmpiLongImmediateToAbsoluteLong:
                    ExecuteCmpiLongImmediateToAbsoluteLong();
                    return true;

                case M68020OpcodeKind.CmpiLongImmediateToAbsoluteWord:
                    ExecuteCmpiLongImmediateToAbsoluteWord();
                    return true;

                case M68020OpcodeKind.Nop:
                    ExecuteNop();
                    return true;

                case M68020OpcodeKind.Reset:
                    ExecuteReset();
                    return true;

                case M68020OpcodeKind.Stop:
                    ExecuteStop();
                    return true;

                case M68020OpcodeKind.Movec:
                    ExecuteMovec(opcode);
                    return true;

                case M68020OpcodeKind.MoveUsp:
                    ExecuteMoveUsp(opcode);
                    return true;

                case M68020OpcodeKind.Trap:
                    ExecuteTrap(opcode);
                    return true;

                case M68020OpcodeKind.Rte:
                    ExecuteRte();
                    return true;

                case M68020OpcodeKind.Rtd:
                    ExecuteRtd();
                    return true;

                case M68020OpcodeKind.Rts:
                    ExecuteRts();
                    return true;

                case M68020OpcodeKind.JmpAddressIndirect:
                    ExecuteJmpAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.JmpAddressDisplacement:
                    ExecuteJmpAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.JmpPcDisplacement:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    BeginInstruction(opcode);
                    _ = FetchWord();
                    State.ProgramCounter = GetPcDisplacementAddress();
                    CompleteTiming(M68kInstructionTimingKey.JmpPcDisplacement);
                    return true;

                case M68020OpcodeKind.JmpBriefIndexed:
                    ExecuteJmpBriefIndexed(opcode);
                    return true;

                case M68020OpcodeKind.JmpAddressBriefIndexed:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteJmpAddressBriefIndexed(opcode);
                    return true;

                case M68020OpcodeKind.JsrAddressIndirect:
                    ExecuteJsrAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.JsrAbsoluteLong:
                    ExecuteJsrAbsoluteLong();
                    return true;
                case M68020OpcodeKind.JumpAbsoluteWord:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteJumpAbsoluteWord(opcode);
                    return true;

                case M68020OpcodeKind.JsrAddressDisplacement:
                    ExecuteJsrAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.JsrBriefIndexed:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteJsrBriefIndexed(opcode);
                    return true;

                case M68020OpcodeKind.JsrPcDisplacement:
                    ExecuteJsrPcDisplacement();
                    return true;

                case M68020OpcodeKind.JsrPcBriefIndexed:
                    ExecuteJsrPcBriefIndexed();
                    return true;

                case M68020OpcodeKind.JmpAbsoluteLong:
                    ExecuteJmpAbsoluteLong();
                    return true;

                case M68020OpcodeKind.PeaPcDisplacement:
                    ExecutePeaPcDisplacement();
                    return true;

                case M68020OpcodeKind.PeaAddressDisplacement:
                    ExecutePeaAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.PeaAddressIndirect:
                    ExecutePeaAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.PeaBriefIndexed:
                    ExecutePeaBriefIndexed(opcode);
                    return true;

                case M68020OpcodeKind.PeaPcBriefIndexed:
                    ExecutePeaPcBriefIndexed(opcode);
                    return true;

                case M68020OpcodeKind.PeaAbsoluteWord:
                    ExecutePeaAbsoluteWord();
                    return true;

                case M68020OpcodeKind.PeaAbsoluteLong:
                    ExecutePeaAbsoluteLong();
                    return true;

                case M68020OpcodeKind.LinkLong:
                    ExecuteLinkLong(opcode);
                    return true;

                case M68020OpcodeKind.LinkWord:
                    ExecuteLinkWord(opcode);
                    return true;

                case M68020OpcodeKind.Unlink:
                    ExecuteUnlink(opcode);
                    return true;

                case M68020OpcodeKind.NbcdByte:
                    ExecuteNbcdByte(opcode);
                    return true;

                case M68020OpcodeKind.ExtbLong:
                    ExecuteExtbLong(opcode);
                    return true;

                case M68020OpcodeKind.MovemLongRegistersToPredecrement:
                    ExecuteMovemLongRegistersToPredecrement(opcode);
                    return true;

                case M68020OpcodeKind.MovemWordRegistersToPredecrement:
                    ExecuteMovemWordRegistersToPredecrement(opcode);
                    return true;

                case M68020OpcodeKind.MovemWordRegistersToAddressDisplacement:
                    ExecuteMovemWordRegistersToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MovemLongRegistersToAddressIndirect:
                    ExecuteMovemLongRegistersToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.MovemLongRegistersToAddressDisplacement:
                    ExecuteMovemLongRegistersToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MovemLongRegistersToBriefIndexed:
                    ExecuteMovemLongRegistersToBriefIndexed(opcode);
                    return true;

                case M68020OpcodeKind.MovemLongRegistersToAbsoluteLong:
                    // This exact timing path is qualified for the 020 family.
                    // Preserve the other models' existing fallback/timing policies.
                    if (_profile.Model != M68kAcceleratorModel.M68020)
                    {
                        return false;
                    }
                    ExecuteMovemLongRegistersToAbsoluteLong(opcode);
                    return true;

                case M68020OpcodeKind.MovemWordPostIncrementToRegisters:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMovemWordPostIncrementToRegisters(opcode);
                    return true;

                case M68020OpcodeKind.MovemLongPostIncrementToRegisters:
                    ExecuteMovemLongPostIncrementToRegisters(opcode);
                    return true;

                case M68020OpcodeKind.MovemLongAddressIndirectToRegisters:
                    ExecuteMovemLongAddressIndirectToRegisters(opcode);
                    return true;

                case M68020OpcodeKind.MovemIndexedToRegisters:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMovemIndexedToRegisters(opcode);
                    return true;

                case M68020OpcodeKind.MovemLongAddressDisplacementToRegisters:
                    ExecuteMovemLongDisplacementToRegisters(opcode);
                    return true;

                case M68020OpcodeKind.MovemLongPcDisplacementToRegisters:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteMovemLongDisplacementToRegisters(opcode);
                    return true;

                case M68020OpcodeKind.MovemWordPcDisplacementToRegisters:
                case M68020OpcodeKind.MovemWordAddressDisplacementToRegisters:
                case M68020OpcodeKind.MovemWordAddressIndirectToRegisters:
                    ExecuteMovemWordAddressDisplacementToRegisters(opcode);
                    return true;

                case M68020OpcodeKind.LongBranch:
                    ExecuteLongBranch(opcode);
                    return true;

                case M68020OpcodeKind.ByteBranch:
                    ExecuteByteBranch(opcode);
                    return true;

                case M68020OpcodeKind.WordBranch:
                    ExecuteWordBranch(opcode);
                    return true;

                case M68020OpcodeKind.Trapcc:
                    ExecuteTrapcc(opcode);
                    return true;

                case M68020OpcodeKind.SccAbsoluteWord:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteSccAbsoluteWord(opcode);
                    return true;
                case M68020OpcodeKind.SccAddressMemory:
                    if (_profile.Model == M68kAcceleratorModel.M68040) return false;
                    ExecuteSccAddressMemory(opcode);
                    return true;

                case M68020OpcodeKind.SccAbsoluteLong:
                    ExecuteSccAbsoluteLong(opcode);
                    return true;

                case M68020OpcodeKind.SccData:
                    ExecuteSccData(opcode);
                    return true;

                case M68020OpcodeKind.Dbcc:
                    ExecuteDbcc(opcode);
                    return true;

                default:
                    throw new InvalidOperationException($"Unknown MC68020 opcode dispatch kind {kind}.");
            }
        }

        protected virtual bool TryExecuteApproximateInstruction(ushort opcode)
        {
            _ = opcode;
            return false;
        }

        protected virtual bool TryExecuteModelSpecificInstruction(ushort opcode)
        {
            _ = opcode;
            return false;
        }

        private bool TryExecuteImmediateLogicalToStatusRegister(ushort opcode)
        {
            if (opcode is not (0x003C or 0x007C or 0x023C or 0x027C or 0x0A3C or 0x0A7C))
            {
                return false;
            }

            BeginInstruction(opcode);
            var instructionPc = State.ProgramCounter;
            _ = FetchWord();
            var immediate = FetchWord();
            var operation = opcode & 0x0F00;
            var status = State.StatusRegister;
            var result = operation switch
            {
                0x0000 => status | immediate,
                0x0200 => status & immediate,
                0x0A00 => status ^ immediate,
                _ => status
            };

            if ((opcode & 0x0040) == 0)
            {
                State.StatusRegister = (ushort)((State.StatusRegister & 0xFFE0) | (result & 0x001F));
                CompleteTiming(M68kInstructionTimingKey.ImmediateWordToConditionCodeRegister);
                return true;
            }

            if ((State.StatusRegister & M68kCpuState.Supervisor) == 0)
            {
                RaiseFormat0Exception(8, instructionPc, M68kInstructionTimingKey.PrivilegeViolation);
                return true;
            }

            State.StatusRegister = (ushort)result;
            CompleteTiming(M68kInstructionTimingKey.ImmediateWordToStatusRegister);
            return true;
        }

        private void ExecuteNop()
        {
            BeginInstruction(0x4E71);
            _ = FetchWord();
            CompleteTiming(M68kInstructionTimingKey.Nop);
        }

        private void ExecuteReset()
        {
            BeginInstruction(0x4E70);
            var instructionPc = State.ProgramCounter;
            _ = FetchWord();
            if ((State.StatusRegister & M68kCpuState.Supervisor) == 0)
            {
                RaiseFormat0Exception(8, instructionPc, M68kInstructionTimingKey.PrivilegeViolation);
                return;
            }

            _timing.SynchronizeNativeToBus();
            _bus.ResetExternalDevices(State.Cycles);
            CompleteTiming(M68kInstructionTimingKey.Reset);
        }

        private void ExecuteStop()
        {
            BeginInstruction(0x4E72);
            var instructionPc = State.ProgramCounter;
            _ = FetchWord();
            if ((State.StatusRegister & M68kCpuState.Supervisor) == 0)
            {
                RaiseFormat0Exception(8, instructionPc, M68kInstructionTimingKey.PrivilegeViolation);
                return;
            }

            State.StatusRegister = FetchWord();
            CompleteTiming(M68kInstructionTimingKey.Stop);
            State.Stopped = true;
        }

        private void ExecuteMovep(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var dataRegister = (opcode >> 9) & 7;
            var addressRegister = opcode & 7;
            var address = unchecked((uint)(State.A[addressRegister] + unchecked((int)(short)FetchWord())));
            var isLong = (opcode & 0x0040) != 0;
            var registerToMemory = (opcode & 0x0080) != 0;

            if (registerToMemory)
            {
                var value = State.D[dataRegister];
                if (isLong)
                {
                    WriteByte(address, (byte)(value >> 24));
                    WriteByte(unchecked(address + 2), (byte)(value >> 16));
                    WriteByte(unchecked(address + 4), (byte)(value >> 8));
                    WriteByte(unchecked(address + 6), (byte)value);
                }
                else
                {
                    WriteByte(address, (byte)(value >> 8));
                    WriteByte(unchecked(address + 2), (byte)value);
                }
            }
            else if (isLong)
            {
                State.D[dataRegister] =
                    ((uint)ReadByte(address) << 24) |
                    ((uint)ReadByte(unchecked(address + 2)) << 16) |
                    ((uint)ReadByte(unchecked(address + 4)) << 8) |
                    ReadByte(unchecked(address + 6));
            }
            else
            {
                var value = (ushort)((ReadByte(address) << 8) | ReadByte(unchecked(address + 2)));
                WriteDataRegisterWord(dataRegister, value);
            }

            CompleteTiming(M68kInstructionTimingKey.Nop);
        }

        private void ExecuteMoveUsp(ushort opcode)
        {
            BeginInstruction(opcode);
            var instructionPc = State.ProgramCounter;
            _ = FetchWord();
            if ((State.StatusRegister & M68kCpuState.Supervisor) == 0)
            {
                RaiseFormat0Exception(8, instructionPc, M68kInstructionTimingKey.PrivilegeViolation);
                return;
            }

            var register = opcode & 7;
            if ((opcode & 0x0008) == 0)
            {
                State.SetUserStackPointer(State.A[register]);
            }
            else
            {
                WriteGeneralRegister(true, register, State.UserStackPointer);
            }

            CompleteTiming(M68kInstructionTimingKey.MoveUsp);
        }

        protected virtual void ExecuteMovec(ushort opcode)
        {
            BeginInstruction(opcode);
            var instructionPc = State.ProgramCounter;
            _ = FetchWord();
            if ((State.StatusRegister & M68kCpuState.Supervisor) == 0)
            {
                _ = FetchWord();
                RaiseFormat0Exception(8, instructionPc, M68kInstructionTimingKey.PrivilegeViolation);
                return;
            }

            var extension = FetchWord();
            var generalRegister = (extension >> 12) & 7;
            var useAddressRegister = (extension & 0x8000) != 0;
            var controlRegister = extension & 0x0FFF;

            if (opcode == 0x4E7A)
            {
                if (!TryReadControlRegister(controlRegister, instructionPc, out var value))
                {
                    return;
                }

                WriteGeneralRegister(useAddressRegister, generalRegister, value);
            }
            else
            {
                var value = ReadGeneralRegister(useAddressRegister, generalRegister);
                if (!TryWriteControlRegister(controlRegister, value, instructionPc))
                {
                    return;
                }
            }

            CompleteTiming(M68kInstructionTimingKey.Movec);
        }

        private void ExecuteRte()
        {
            BeginInstruction(0x4E73);
            var instructionPc = State.ProgramCounter;
            _ = FetchWord();
            if ((State.StatusRegister & M68kCpuState.Supervisor) == 0)
            {
                RaiseFormat0Exception(8, instructionPc, M68kInstructionTimingKey.PrivilegeViolation);
                return;
            }

            var framePointer = State.A[7];
            var restoredStatus = ReadWord(framePointer);
            var restoredPc = ReadLong(framePointer + 2);
            var format = ReadWord(framePointer + 6);
            if ((format & 0xF000) != Format0ExceptionFrame)
            {
                RaiseFormat0Exception(14, instructionPc, M68kInstructionTimingKey.FormatError);
                return;
            }

            State.SetActiveStackPointer(framePointer + 8);
            State.ProgramCounter = restoredPc;
            State.StatusRegister = restoredStatus;
            CompleteTiming(M68kInstructionTimingKey.Rte);
        }

        private void ExecuteTrap(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            RaiseFormat0Exception(32 + (opcode & 0x0F), State.ProgramCounter, M68kInstructionTimingKey.IllegalInstruction);
        }

        private void ExecuteRtd()
        {
            BeginInstruction(0x4E74);
            _ = FetchWord();
            var displacement = unchecked((short)FetchWord());
            var target = PullLong();
            State.SetActiveStackPointer(State.A[7] + unchecked((uint)displacement));
            State.ProgramCounter = target;
            CompleteTiming(M68kInstructionTimingKey.Rtd);
        }

        private void ExecuteRts()
        {
            BeginInstruction(0x4E75);
            _ = FetchWord();
            State.ProgramCounter = PullLong();
            CompleteTiming(M68kInstructionTimingKey.Rts);
        }

        private void ExecuteJmpAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            State.ProgramCounter = State.A[opcode & 7];
            CompleteTiming(M68kInstructionTimingKey.JmpAddressIndirect);
        }

        private void ExecuteJmpAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var displacement = unchecked((int)(short)FetchWord());
            State.ProgramCounter = unchecked((uint)(State.A[opcode & 7] + displacement));
            CompleteTiming(M68kInstructionTimingKey.JmpAddressDisplacement);
        }

        private void ExecuteJmpBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var extensionAddress = State.ProgramCounter;
            var extension = FetchWord();
            State.ProgramCounter = CalculateIndexedOperandAddress(extensionAddress, extension, opcode);
            CompleteIndexedCalculationTiming(M68kInstructionTimingKey.JmpBriefIndexed, extension,
                M68kInstructionTimingKey.FullIndexedJump, "JMP <full-indexed>", operationCycles: 1,
                barriers: M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch);
        }

        private void ExecuteJmpAbsoluteLong()
        {
            BeginInstruction(0x4EF9);
            _ = FetchWord();
            State.ProgramCounter = FetchLong();
            CompleteTiming(M68kInstructionTimingKey.JmpAbsoluteLong);
        }

        private void ExecuteJumpAbsoluteWord(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var target = unchecked((uint)(int)(short)FetchWord());
            var subroutine = opcode == 0x4EB8;
            if (subroutine) PushLong(State.ProgramCounter);
            State.ProgramCounter = target;
            CompleteTiming(subroutine ? M68kInstructionTimingKey.JsrAbsoluteWord : M68kInstructionTimingKey.JmpAbsoluteWord);
        }

        private void ExecuteJsrAbsoluteLong()
        {
            BeginInstruction(0x4EB9);
            _ = FetchWord();
            var target = FetchLong();
            PushLong(State.ProgramCounter);
            State.ProgramCounter = target;
            CompleteTiming(M68kInstructionTimingKey.JsrAbsoluteLong);
        }

        private void ExecuteJmpAddressBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var extension = FetchWord();
            State.ProgramCounter = CalculateIndexedOperandAddress(State.A[opcode & 7], extension, opcode);
            CompleteIndexedCalculationTiming(M68kInstructionTimingKey.JmpBriefIndexed, extension,
                M68kInstructionTimingKey.FullIndexedJump, "JMP <full-indexed>", operationCycles: 1,
                barriers: M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch);
        }

        private void ExecuteJsrAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var target = State.A[opcode & 7];
            PushLong(State.ProgramCounter);
            State.ProgramCounter = target;
            CompleteTiming(M68kInstructionTimingKey.JsrAddressIndirect);
        }

        private void ExecuteJsrAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var target = unchecked((uint)(State.A[register] + displacement));
            PushLong(State.ProgramCounter);
            State.ProgramCounter = target;
            CompleteTiming(M68kInstructionTimingKey.JsrAddressDisplacement);
        }

        private void ExecuteJsrBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var extension = FetchWord();
            // A7 can be either the base or index: resolve before changing the stack.
            var target = CalculateIndexedOperandAddress(State.A[opcode & 7], extension, opcode);
            PushLong(State.ProgramCounter);
            State.ProgramCounter = target;
            CompleteIndexedCalculationTiming(M68kInstructionTimingKey.JsrBriefIndexed, extension,
                M68kInstructionTimingKey.FullIndexedSubroutine, "JSR <full-indexed>", operationCycles: 3,
                barriers: M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch);
        }

        private void ExecuteJsrPcDisplacement()
        {
            BeginInstruction(0x4EBA);
            _ = FetchWord();
            var extensionAddress = State.ProgramCounter;
            var target = unchecked((uint)(extensionAddress + unchecked((int)(short)FetchWord())));
            PushLong(State.ProgramCounter);
            State.ProgramCounter = target;
            CompleteTiming(M68kInstructionTimingKey.JsrAbsoluteLong);
        }

        private void ExecuteJsrPcBriefIndexed()
        {
            BeginInstruction(0x4EBB);
            _ = FetchWord();
            var extensionAddress = State.ProgramCounter;
            var extension = FetchWord();
            var target = CalculateIndexedOperandAddress(extensionAddress, extension, 0x4EBB);
            PushLong(State.ProgramCounter);
            State.ProgramCounter = target;
            CompleteIndexedCalculationTiming(M68kInstructionTimingKey.JsrPcBriefIndexed, extension,
                M68kInstructionTimingKey.FullIndexedSubroutine, "JSR <full-indexed>", operationCycles: 3,
                barriers: M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch);
        }

        private void ExecutePeaPcDisplacement()
        {
            BeginInstruction(0x487A);
            _ = FetchWord();
            var extensionAddress = State.ProgramCounter;
            var address = unchecked((uint)(extensionAddress + unchecked((int)(short)FetchWord())));
            PushLong(address);
            CompleteTiming(M68kInstructionTimingKey.PeaPcDisplacement);
        }

        private void ExecutePeaAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[opcode & 7] + displacement));
            PushLong(address);
            CompleteTiming(M68kInstructionTimingKey.PeaAddressDisplacement);
        }

        private void ExecutePeaAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            PushLong(State.A[opcode & 7]);
            CompleteTiming(M68kInstructionTimingKey.PeaAddressIndirect);
        }

        private void ExecutePeaBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var baseRegister = opcode & 7;
            var extension = FetchWord();
            PushLong(CalculateIndexedOperandAddress(State.A[baseRegister], extension, opcode));
            CompleteIndexedCalculationTiming(M68kInstructionTimingKey.PeaBriefIndexed, extension,
                M68kInstructionTimingKey.FullIndexedPea, "PEA <full-indexed>", operationCycles: 3);
        }

        private void ExecutePeaPcBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var extensionAddress = State.ProgramCounter;
            var extension = FetchWord();
            PushLong(CalculateIndexedOperandAddress(extensionAddress, extension, opcode));
            CompleteIndexedCalculationTiming(M68kInstructionTimingKey.PeaPcBriefIndexed, extension,
                M68kInstructionTimingKey.FullIndexedPea, "PEA <full-indexed>", operationCycles: 3);
        }

        private void ExecutePeaAbsoluteWord()
        {
            BeginInstruction(0x4878);
            _ = FetchWord();
            var address = unchecked((uint)(int)(short)FetchWord());
            PushLong(address);
            CompleteTiming(M68kInstructionTimingKey.PeaAbsoluteWord);
        }

        private void ExecutePeaAbsoluteLong()
        {
            BeginInstruction(0x4879);
            _ = FetchWord();
            PushLong(FetchLong());
            CompleteTiming(M68kInstructionTimingKey.PeaAbsoluteLong);
        }

        private void ExecuteLinkLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var displacement = unchecked((int)FetchLong());
            PushLong(State.A[register]);
            State.A[register] = State.A[7];
            State.SetActiveStackPointer(State.A[7] + unchecked((uint)displacement));
            CompleteTiming(M68kInstructionTimingKey.LinkLong);
        }

        private void ExecuteLinkWord(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var displacement = unchecked((short)FetchWord());
            PushLong(State.A[register]);
            State.A[register] = State.A[7];
            State.SetActiveStackPointer(State.A[7] + unchecked((uint)displacement));
            CompleteTiming(M68kInstructionTimingKey.LinkLong);
        }

        private void ExecuteUnlink(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            State.SetActiveStackPointer(State.A[register]);
            var restoredRegister = PullLong();
            State.A[register] = restoredRegister;
            CompleteTiming(M68kInstructionTimingKey.LinkLong);
        }

        private void ExecuteExtbLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var value = unchecked((uint)(int)(sbyte)(State.D[register] & 0xFF));
            State.D[register] = value;
            State.SetNegativeZero(value, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.ExtbLong);
        }

        private void ExecuteMoveq(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var value = unchecked((uint)(int)(sbyte)(opcode & 0xFF));
            State.D[register] = value;
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.Moveq);
        }

        private void ExecuteNegByteData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var destination = (byte)State.D[register];
            var result = (byte)(0 - destination);
            WriteDataRegisterByte(register, result);
            State.SetNegativeZero(result, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, destination == 0x80);
            State.SetFlag(M68kCpuState.Carry, destination != 0);
            State.SetFlag(M68kCpuState.Extend, destination != 0);
            CompleteTiming(M68kInstructionTimingKey.NegByteData);
        }

        private void ExecuteNegLongData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var destination = State.D[register];
            var result = unchecked(0u - destination);
            State.D[register] = result;
            State.SetNegativeZero(result, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, destination == 0x8000_0000u);
            State.SetFlag(M68kCpuState.Carry, destination != 0);
            State.SetFlag(M68kCpuState.Extend, destination != 0);
            CompleteTiming(M68kInstructionTimingKey.NegLongData);
        }

        private void ExecuteNegWordData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var destination = (ushort)State.D[register];
            var result = (ushort)(0 - destination);
            WriteDataRegisterWord(register, result);
            State.SetNegativeZero(result, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, destination == 0x8000);
            State.SetFlag(M68kCpuState.Carry, destination != 0);
            State.SetFlag(M68kCpuState.Extend, destination != 0);
            CompleteTiming(M68kInstructionTimingKey.NegWordData);
        }

        private void ExecuteNegxLongData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var packed = M68kIntegerSemantics.Negx(State.D[register], State.StatusRegister, (int)M68kOperandSize.Long);
            State.D[register] = (uint)packed;
            State.StatusRegister = (ushort)(packed >> 32);
            CompleteTiming(M68kInstructionTimingKey.NegxLongData);
        }

        private void ExecuteNotByteData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var result = (byte)~State.D[register];
            WriteDataRegisterByte(register, result);
            State.SetNegativeZero(result, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.NotByteData);
        }

        private void ExecuteNotLongAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var displacement = unchecked((short)FetchWord());
            var address = unchecked((uint)(State.A[opcode & 7] + displacement));
            var result = ~ReadLong(address);
            WriteLong(address, result);
            SetMoveFlags(result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.NotLongAddressDisplacement);
        }

        private void ExecuteNotByteAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var addressRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[addressRegister] + displacement));
            var result = (byte)~ReadByte(address);
            WriteByte(address, result);
            State.SetNegativeZero(result, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.NotByteAddressDisplacement);
        }

        private void ExecuteNotWordData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var result = (ushort)~State.D[register];
            WriteDataRegisterWord(register, result);
            State.SetNegativeZero(result, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.NotWordData);
        }

        private void ExecuteNotLongData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var result = ~State.D[register];
            State.D[register] = result;
            State.SetNegativeZero(result, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.NotLongData);
        }

        private void ExecuteMovemLongRegistersToPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var mask = FetchWord();
            var addressRegister = opcode & 7;
            var address = State.A[addressRegister];
            var dataSnapshot = new uint[8];
            var addressSnapshot = new uint[8];
            Array.Copy(State.D, dataSnapshot, dataSnapshot.Length);
            Array.Copy(State.A, addressSnapshot, addressSnapshot.Length);

            for (var bit = 0; bit < 16; bit++)
            {
                if ((mask & (1 << bit)) == 0)
                {
                    continue;
                }

                var value = bit < 8
                    ? addressSnapshot[7 - bit]
                    : dataSnapshot[15 - bit];
                address -= 4;
                WriteLong(address, value);
            }

            WriteGeneralRegister(true, addressRegister, address);
            CompleteMovemLongTiming(
                M68kInstructionTimingKey.MovemLongRegistersToPredecrement,
                "MOVEM.L <list>,-(An)",
                CountSetBits(mask),
                registerToMemory: true);
        }

        private void ExecuteMovemWordRegistersToPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var mask = FetchWord();
            var addressRegister = opcode & 7;
            var address = State.A[addressRegister];
            var dataSnapshot = new uint[8];
            var addressSnapshot = new uint[8];
            Array.Copy(State.D, dataSnapshot, dataSnapshot.Length);
            Array.Copy(State.A, addressSnapshot, addressSnapshot.Length);

            for (var bit = 0; bit < 16; bit++)
            {
                if ((mask & (1 << bit)) == 0)
                {
                    continue;
                }

                var value = bit < 8
                    ? addressSnapshot[7 - bit]
                    : dataSnapshot[15 - bit];
                address = unchecked(address - 2u);
                WriteWord(address, (ushort)value);
            }

            State.A[addressRegister] = address;
            CompleteMovemWordTiming(
                M68kInstructionTimingKey.MovemWordRegistersToPredecrement,
                "MOVEM.W <list>,-(An)",
                CountSetBits(mask));
        }

        private void ExecuteMovemWordRegistersToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var mask = FetchWord();
            var addressRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[addressRegister] + displacement));
            var dataSnapshot = new uint[8];
            var addressSnapshot = new uint[8];
            Array.Copy(State.D, dataSnapshot, dataSnapshot.Length);
            Array.Copy(State.A, addressSnapshot, addressSnapshot.Length);

            for (var register = 0; register < 8; register++)
            {
                if ((mask & (1 << register)) != 0)
                {
                    WriteWord(address, (ushort)dataSnapshot[register]);
                    address += 2;
                }
            }

            for (var register = 0; register < 8; register++)
            {
                if ((mask & (1 << (8 + register))) != 0)
                {
                    WriteWord(address, (ushort)addressSnapshot[register]);
                    address += 2;
                }
            }

            CompleteMovemWordTiming(
                M68kInstructionTimingKey.MovemWordRegistersToAddressDisplacement,
                "MOVEM.W <list>,(d16,An)",
                CountSetBits(mask),
                effectiveAddressCycles: 2);
        }

        private void ExecuteMovemLongRegistersToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var mask = FetchWord();
            var addressRegister = opcode & 7;
            var address = State.A[addressRegister];
            var dataSnapshot = new uint[8];
            var addressSnapshot = new uint[8];
            Array.Copy(State.D, dataSnapshot, dataSnapshot.Length);
            Array.Copy(State.A, addressSnapshot, addressSnapshot.Length);

            for (var register = 0; register < 8; register++)
            {
                if ((mask & (1 << register)) != 0)
                {
                    WriteLong(address, dataSnapshot[register]);
                    address += 4;
                }
            }

            for (var register = 0; register < 8; register++)
            {
                if ((mask & (1 << (8 + register))) != 0)
                {
                    WriteLong(address, addressSnapshot[register]);
                    address += 4;
                }
            }

            CompleteMovemLongTiming(
                M68kInstructionTimingKey.MovemLongRegistersToAddressIndirect,
                "MOVEM.L <list>,(An)",
                CountSetBits(mask),
                registerToMemory: true);
        }

        private void ExecuteMovemLongRegistersToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var mask = FetchWord();
            var addressRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[addressRegister] + displacement));
            var dataSnapshot = new uint[8];
            var addressSnapshot = new uint[8];
            Array.Copy(State.D, dataSnapshot, dataSnapshot.Length);
            Array.Copy(State.A, addressSnapshot, addressSnapshot.Length);

            for (var register = 0; register < 8; register++)
            {
                if ((mask & (1 << register)) != 0)
                {
                    WriteLong(address, dataSnapshot[register]);
                    address += 4;
                }
            }

            for (var register = 0; register < 8; register++)
            {
                if ((mask & (1 << (8 + register))) != 0)
                {
                    WriteLong(address, addressSnapshot[register]);
                    address += 4;
                }
            }

            CompleteMovemLongTiming(
                M68kInstructionTimingKey.MovemLongRegistersToAddressDisplacement,
                "MOVEM.L <list>,(d16,An)",
                CountSetBits(mask),
                registerToMemory: true);
        }

        private void ExecuteMovemLongRegistersToBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var mask = FetchWord();
            var addressRegister = opcode & 7;
            var extension = FetchWord();
            var address = CalculateBriefIndexedAddress(addressRegister, extension, opcode);
            var dataSnapshot = new uint[8];
            var addressSnapshot = new uint[8];
            Array.Copy(State.D, dataSnapshot, dataSnapshot.Length);
            Array.Copy(State.A, addressSnapshot, addressSnapshot.Length);

            for (var register = 0; register < 8; register++)
            {
                if ((mask & (1 << register)) != 0)
                {
                    WriteLong(address, dataSnapshot[register]);
                    address += 4;
                }
            }

            for (var register = 0; register < 8; register++)
            {
                if ((mask & (1 << (8 + register))) != 0)
                {
                    WriteLong(address, addressSnapshot[register]);
                    address += 4;
                }
            }

            CompleteMovemLongTiming(
                M68kInstructionTimingKey.MovemLongRegistersToBriefIndexed,
                "MOVEM.L <list>,(d8,An,Xn)",
                CountSetBits(mask),
                registerToMemory: true);
        }

        private void ExecuteMovemLongRegistersToAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var mask = FetchWord();
            var address = FetchLong();
            var dataSnapshot = new uint[8];
            var addressSnapshot = new uint[8];
            Array.Copy(State.D, dataSnapshot, dataSnapshot.Length);
            Array.Copy(State.A, addressSnapshot, addressSnapshot.Length);

            for (var register = 0; register < 8; register++)
            {
                if ((mask & (1 << register)) != 0)
                {
                    WriteLong(address, dataSnapshot[register]);
                    address += 4;
                }
            }

            for (var register = 0; register < 8; register++)
            {
                if ((mask & (1 << (8 + register))) != 0)
                {
                    WriteLong(address, addressSnapshot[register]);
                    address += 4;
                }
            }

            // MC68020UM 8.2.7 cache case4+3n plus the word mask/absolute-long
            // calculate-immediate address cost4 from8.2.4:8+3n. The existing
            // MOVEM formula supplies this base cost without another EA increment.
            CompleteMovemLongTiming(
                M68kInstructionTimingKey.MovemLongRegistersToAbsoluteLong,
                "MOVEM.L <list>,(xxx).L",
                CountSetBits(mask),
                registerToMemory: true);
        }

        private void ExecuteMovemWordPostIncrementToRegisters(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var mask = FetchWord();
            var addressRegister = opcode & 7;
            var address = State.A[addressRegister];
            for (var register = 0; register < 8; register++)
            {
                if ((mask & (1 << register)) == 0)
                {
                    continue;
                }

                State.D[register] = unchecked((uint)(int)(short)ReadWord(address));
                address += 2;
            }

            for (var register = 0; register < 8; register++)
            {
                if ((mask & (1 << (8 + register))) == 0)
                {
                    continue;
                }

                WriteGeneralRegister(true, register, unchecked((uint)(int)(short)ReadWord(address)));
                address += 2;
            }

            WriteGeneralRegister(true, addressRegister, address);
            CompleteMovemWordTiming(
                M68kInstructionTimingKey.MovemWordPostIncrementToRegisters,
                "MOVEM.W (An)+,<list>",
                CountSetBits(mask),
                memoryToRegister: true);
        }

        private void ExecuteMovemLongPostIncrementToRegisters(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var mask = FetchWord();
            var addressRegister = opcode & 7;
            var address = State.A[addressRegister];
            for (var register = 0; register < 8; register++)
            {
                if ((mask & (1 << register)) == 0)
                {
                    continue;
                }

                State.D[register] = ReadLong(address);
                address += 4;
            }

            for (var register = 0; register < 8; register++)
            {
                if ((mask & (1 << (8 + register))) == 0)
                {
                    continue;
                }

                WriteGeneralRegister(true, register, ReadLong(address));
                address += 4;
            }

            WriteGeneralRegister(true, addressRegister, address);
            CompleteMovemLongTiming(
                M68kInstructionTimingKey.MovemLongPostIncrementToRegisters,
                "MOVEM.L (An)+,<list>",
                CountSetBits(mask),
                registerToMemory: false);
        }

        private void ExecuteMovemLongAddressIndirectToRegisters(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var mask = FetchWord();
            var address = State.A[opcode & 7];
            for (var register = 0; register < 8; register++)
            {
                if ((mask & (1 << register)) == 0)
                {
                    continue;
                }

                State.D[register] = ReadLong(address);
                address += 4;
            }

            for (var register = 0; register < 8; register++)
            {
                if ((mask & (1 << (8 + register))) == 0)
                {
                    continue;
                }

                WriteGeneralRegister(true, register, ReadLong(address));
                address += 4;
            }

            CompleteMovemLongTiming(
                M68kInstructionTimingKey.MovemLongAddressIndirectToRegisters,
                "MOVEM.L (An),<list>",
                CountSetBits(mask),
                registerToMemory: false);
        }

        private void ExecuteMovemIndexedToRegisters(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var mask = FetchWord();
            var pcRelative = (opcode & 7) == 3 && (opcode & 0x38) == 0x38;
            var sourceBase = pcRelative ? State.ProgramCounter : State.A[opcode & 7];
            var extension = FetchWord();
            var address = CalculateBriefIndexedAddress(sourceBase, extension, opcode);
            var size = (opcode & 0x40) == 0 ? M68kOperandSize.Word : M68kOperandSize.Long;
            // Latch the EA before loading any base/index register in the mask.
            for (var register = 0; register < 16; register++)
            {
                if ((mask & (1 << register)) == 0) continue;
                var value = ReadSized(address, size);
                if (size == M68kOperandSize.Word) value = unchecked((uint)(int)(short)value);
                WriteGeneralRegister(register >= 8, register & 7, value);
                address = unchecked(address + (uint)(size == M68kOperandSize.Word ? 2 : 4));
            }
            if (size == M68kOperandSize.Word)
                CompleteMovemWordTiming(M68kInstructionTimingKey.MovemWordBriefIndexedToRegisters,
                    "MOVEM.W <brief-indexed>,<list>", CountSetBits(mask), effectiveAddressCycles: 4, memoryToRegister: true);
            else
                CompleteMovemLongTiming(M68kInstructionTimingKey.MovemLongBriefIndexedToRegisters,
                    "MOVEM.L <brief-indexed>,<list>", CountSetBits(mask), registerToMemory: false);
        }

        private void ExecuteMovemLongDisplacementToRegisters(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var mask = FetchWord();
            var pcRelative = opcode == 0x4CFA;
            var baseAddress = pcRelative ? State.ProgramCounter : State.A[opcode & 7];
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(baseAddress + displacement));
            for (var register = 0; register < 8; register++)
            {
                if ((mask & (1 << register)) == 0)
                {
                    continue;
                }

                State.D[register] = ReadLong(address);
                address += 4;
            }

            for (var register = 0; register < 8; register++)
            {
                if ((mask & (1 << (8 + register))) == 0)
                {
                    continue;
                }

                WriteGeneralRegister(true, register, ReadLong(address));
                address += 4;
            }

            CompleteMovemLongTiming(
                pcRelative ? M68kInstructionTimingKey.MovemLongPcDisplacementToRegisters : M68kInstructionTimingKey.MovemLongAddressDisplacementToRegisters,
                pcRelative ? "MOVEM.L (d16,PC),<list>" : "MOVEM.L (d16,An),<list>",
                CountSetBits(mask),
                registerToMemory: false);
        }

        private void ExecuteMovemWordAddressDisplacementToRegisters(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var mask = FetchWord();
            var pcRelative = opcode == 0x4CBA;
            var indirect = (opcode & 0xFFF8) == 0x4C90;
            var baseAddress = pcRelative ? State.ProgramCounter : State.A[opcode & 7];
            var displacement = indirect ? 0 : unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(baseAddress + displacement));

            for (var register = 0; register < 8; register++)
            {
                if ((mask & (1 << register)) != 0)
                {
                    State.D[register] = unchecked((uint)(int)(short)ReadWord(address));
                    address += 2;
                }
            }

            for (var register = 0; register < 8; register++)
            {
                if ((mask & (1 << (8 + register))) != 0)
                {
                    WriteGeneralRegister(true, register, unchecked((uint)(int)(short)ReadWord(address)));
                    address += 2;
                }
            }

            CompleteMovemWordTiming(
                indirect ? M68kInstructionTimingKey.MovemWordAddressIndirectToRegisters : M68kInstructionTimingKey.MovemWordAddressDisplacementToRegisters,
                indirect ? "MOVEM.W (An),<list>" : pcRelative ? "MOVEM.W (d16,PC),<list>" : "MOVEM.W (d16,An),<list>",
                CountSetBits(mask),
                effectiveAddressCycles: indirect ? 0 : 2,
                memoryToRegister: true);
        }

        private void ExecuteClrDataLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            State.D[register] = 0;
            State.SetNegativeZero(0, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.ClrDataLong);
        }

        private void ExecuteClrDataWord(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            WriteDataRegisterWord(opcode & 7, 0);
            State.SetNegativeZero(0, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.ClrDataWord);
        }

        private void ExecuteClrDataByte(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            WriteDataRegisterByte(opcode & 7, 0);
            State.SetNegativeZero(0, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.ClrDataByte);
        }

        private void ExecuteClrLongAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            WriteLong(State.A[register], 0);
            State.SetNegativeZero(0, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.ClrLongAddressIndirect);
        }

        private void ExecuteClrLongAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            WriteLong(unchecked((uint)(State.A[register] + displacement)), 0);
            State.SetNegativeZero(0, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.ClrLongAddressDisplacement);
        }

        private void ExecuteClrLongAbsoluteLong()
        {
            BeginInstruction(0x42B9);
            _ = FetchWord();
            var address = FetchLong();
            WriteLong(address, 0);
            State.SetNegativeZero(0, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.ClrLongAbsoluteLong);
        }

        private void ExecuteClrLongAbsoluteWord()
        {
            BeginInstruction(0x42B8);
            _ = FetchWord();
            var address = unchecked((uint)(int)(short)FetchWord());
            WriteLong(address, 0);
            SetMoveFlags(0, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.ClrLongAbsoluteWord);
        }

        private void ExecuteClrWordAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            WriteWord(State.A[register], 0);
            State.SetNegativeZero(0, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.ClrWordAddressIndirect);
        }

        private void ExecuteClrByteAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            WriteByte(State.A[register], 0);
            State.SetNegativeZero(0, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.ClrByteAddressIndirect);
        }

        private void ExecuteClrBytePostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var address = State.A[register];
            WriteByte(address, 0);
            State.A[register] = unchecked(
                address + M68kIntegerSemantics.AddressIncrement(register, M68kOperandSize.Byte));
            SetMoveFlags(0, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.ClrBytePostIncrement);
        }

        private void ExecuteClrByteAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            WriteByte(unchecked((uint)(State.A[register] + displacement)), 0);
            State.SetNegativeZero(0, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.ClrByteAddressDisplacement);
        }

        private void ExecuteClrByteAbsoluteLong()
        {
            BeginInstruction(0x4239);
            _ = FetchWord();
            var address = FetchLong();
            WriteByte(address, 0);
            State.SetNegativeZero(0, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.ClrByteAbsoluteLong);
        }

        private void ExecuteClrWordAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            WriteWord(unchecked((uint)(State.A[register] + displacement)), 0);
            State.SetNegativeZero(0, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.ClrWordAddressDisplacement);
        }

        private void ExecuteClrWordPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var address = State.A[register];
            WriteWord(address, 0);
            WriteGeneralRegister(true, register, address + 2);
            State.SetNegativeZero(0, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.ClrWordPostIncrement);
        }

        private void ExecuteClrLongPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var address = State.A[register];
            WriteLong(address, 0);
            State.A[register] = address + 4;
            State.SetNegativeZero(0, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.ClrLongPostIncrement);
        }

        private void ExecuteClrLongPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var address = State.A[register] - 4;
            WriteGeneralRegister(true, register, address);
            WriteLong(address, 0);
            State.SetNegativeZero(0, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.ClrLongPredecrement);
        }

        private void ExecuteClrWordPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var address = State.A[register] - 2;
            WriteGeneralRegister(true, register, address);
            WriteWord(address, 0);
            State.SetNegativeZero(0, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.ClrWordPredecrement);
        }

        private void ExecuteLeaAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var address = FetchLong();
            WriteGeneralRegister(true, register, address);
            CompleteTiming(M68kInstructionTimingKey.LeaAbsoluteLong);
        }

        private void ExecuteLeaAbsoluteWord(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var address = unchecked((uint)(int)(short)FetchWord());
            WriteGeneralRegister(true, register, address);
            CompleteTiming(M68kInstructionTimingKey.LeaAbsoluteWord);
        }

        private void ExecuteLeaAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            WriteGeneralRegister(true, destination, State.A[source]);
            CompleteTiming(M68kInstructionTimingKey.LeaAddressIndirect);
        }

        private void ExecuteLeaAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            WriteGeneralRegister(true, destination, unchecked((uint)(State.A[source] + displacement)));
            CompleteTiming(M68kInstructionTimingKey.LeaAddressDisplacement);
        }

        private void ExecuteLeaPcDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var extensionAddress = State.ProgramCounter;
            var displacement = unchecked((int)(short)FetchWord());
            WriteGeneralRegister(true, destination, unchecked((uint)(extensionAddress + displacement)));
            CompleteTiming(M68kInstructionTimingKey.LeaAddressDisplacement);
        }

        private void ExecuteLeaBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var baseRegister = opcode & 7;
            var extension = FetchWord();
            WriteGeneralRegister(true, destinationRegister,
                CalculateIndexedOperandAddress(State.A[baseRegister], extension, opcode));
            CompleteIndexedCalculationTiming(M68kInstructionTimingKey.LeaBriefIndexed, extension,
                M68kInstructionTimingKey.FullIndexedLea, "LEA <full-indexed>,An", operationCycles: 2);
        }

        private void ExecuteLeaPcBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var extensionAddress = State.ProgramCounter;
            var extension = FetchWord();
            WriteGeneralRegister(true, (opcode >> 9) & 7,
                CalculateIndexedOperandAddress(extensionAddress, extension, opcode));
            CompleteIndexedCalculationTiming(M68kInstructionTimingKey.LeaBriefIndexed, extension,
                M68kInstructionTimingKey.FullIndexedLea, "LEA <full-indexed>,An", operationCycles: 2);
        }

        private void ExecuteMoveImmediateToStatusRegister()
        {
            BeginInstruction(0x46FC);
            var instructionPc = State.ProgramCounter;
            _ = FetchWord();
            if ((State.StatusRegister & M68kCpuState.Supervisor) == 0)
            {
                _ = FetchWord();
                RaiseFormat0Exception(8, instructionPc, M68kInstructionTimingKey.PrivilegeViolation);
                return;
            }

            State.StatusRegister = FetchWord();
            CompleteTiming(M68kInstructionTimingKey.ImmediateWordToStatusRegister);
        }

        private void ExecuteMoveByteImmediateToAbsoluteLong()
        {
            BeginInstruction(0x13FC);
            _ = FetchWord();
            var value = (byte)FetchWord();
            var address = FetchLong();
            WriteByte(address, value);
            State.SetNegativeZero(value, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.MoveByteImmediateToAbsoluteLong);
        }

        private void ExecuteMoveByteImmediateToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var value = (byte)FetchWord();
            WriteByte(State.A[destination], value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteImmediateToAddressIndirect);
        }

        private void ExecuteMoveByteImmediateToPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var value = (byte)FetchWord();
            var address = State.A[destination];
            WriteByte(address, value);
            WriteGeneralRegister(true, destination,
                unchecked(address + M68kIntegerSemantics.AddressIncrement(destination, M68kOperandSize.Byte)));
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteImmediateToPostIncrement);
        }

        private void ExecuteMoveWordImmediateToAbsoluteLong()
        {
            BeginInstruction(0x33FC);
            _ = FetchWord();
            var value = FetchWord();
            var address = FetchLong();
            WriteWord(address, value);
            State.SetNegativeZero(value, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.MoveWordImmediateToAbsoluteLong);
        }

        private void ExecuteMoveLongImmediateToAbsoluteLong()
        {
            BeginInstruction(0x23FC);
            _ = FetchWord();
            var value = FetchLong();
            var address = FetchLong();
            WriteLong(address, value);
            State.SetNegativeZero(value, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.MoveLongImmediateToAbsoluteLong);
        }

        private void ExecuteMoveLongImmediateToAbsoluteWord()
        {
            BeginInstruction(0x21FC);
            _ = FetchWord();
            var value = FetchLong();
            var address = unchecked((uint)(int)(short)FetchWord());
            WriteLong(address, value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongImmediateToAbsoluteLong);
        }

        private void ExecuteMoveLongImmediateToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var value = FetchLong();
            WriteLong(State.A[destination], value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongImmediateToAddressIndirect);
        }

        private void ExecuteMoveLongImmediateToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var value = FetchLong();
            var displacement = unchecked((int)(short)FetchWord());
            WriteLong(unchecked((uint)(State.A[destination] + displacement)), value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongImmediateToAddressDisplacement);
        }

        private void ExecuteMoveWordImmediateToBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var value = FetchWord();
            var extension = FetchWord();
            WriteWord(CalculateBriefIndexedAddress((opcode >> 9) & 7, extension, opcode), value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordImmediateToBriefIndexed);
        }

        private void ExecuteMoveLongImmediateToBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var value = FetchLong();
            var extension = FetchWord();
            WriteLong(CalculateBriefIndexedAddress(destination, extension, opcode), value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.Nop);
        }

        private void ExecuteMoveAbsoluteToDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = (opcode >> 12) == 1 ? M68kOperandSize.Byte : M68kOperandSize.Word;
            var longAddress = (opcode & 7) == 1;
            var source = longAddress ? FetchLong() : unchecked((uint)(int)(short)FetchWord());
            var displacement = (short)FetchWord();
            var value = ReadSized(source, size);
            WriteSized(unchecked((uint)(State.A[(opcode >> 9) & 7] + displacement)), value, size);
            SetMoveFlags(value, size);
            CompleteTiming(size == M68kOperandSize.Word
                ? M68kInstructionTimingKey.MoveWordAbsoluteWordToAddressDisplacement
                : longAddress ? M68kInstructionTimingKey.MoveByteAbsoluteLongToAddressDisplacement
                : M68kInstructionTimingKey.MoveByteAbsoluteWordToAddressDisplacement);
        }

        private void ExecuteMoveLongAbsoluteWordToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var sourceAddress = unchecked((uint)(short)FetchWord());
            var displacement = unchecked((int)(short)FetchWord());
            var value = ReadLong(sourceAddress);
            WriteLong(unchecked((uint)(State.A[destination] + displacement)), value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAbsoluteWordToAddressDisplacement);
        }

        private void ExecuteMoveLongAbsoluteLongToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var sourceAddress = FetchLong();
            var displacement = unchecked((int)(short)FetchWord());
            var value = ReadLong(sourceAddress);
            WriteLong(unchecked((uint)(State.A[destination] + displacement)), value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAbsoluteLongToAddressDisplacement);
        }

        private void ExecuteMoveLongImmediateToPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var value = FetchLong();
            var address = State.A[destination];
            WriteLong(address, value);
            WriteGeneralRegister(true, destination, address + 4);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongImmediateToPostIncrement);
        }

        private void ExecuteMoveLongImmediateToPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var value = FetchLong();
            var address = unchecked(State.A[destinationRegister] - 4u);
            State.A[destinationRegister] = address;
            WriteLong(address, value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongImmediateToPredecrement);
        }

        private void ExecuteMoveLongImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var value = FetchLong();
            State.D[register] = value;
            State.SetNegativeZero(value, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.MoveLongImmediateToData);
        }

        private void ExecuteMoveLongImmediateToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var value = FetchLong();
            WriteGeneralRegister(true, register, value);
            CompleteTiming(M68kInstructionTimingKey.MoveLongImmediateToAddress);
        }

        private void ExecuteMoveWordImmediateToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var value = unchecked((uint)(int)(short)FetchWord());
            WriteGeneralRegister(true, register, value);
            CompleteTiming(M68kInstructionTimingKey.MoveWordImmediateToAddress);
        }

        private void ExecuteMoveWordImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var value = FetchWord();
            WriteDataRegisterWord(register, value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordImmediateToData);
        }

        private void ExecuteMoveWordDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var value = (ushort)State.D[sourceRegister];
            WriteDataRegisterWord(destinationRegister, value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordDataToData);
        }

        private void ExecuteMoveWordAddressToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = opcode & 7;
            var destination = (opcode >> 9) & 7;
            var value = (ushort)State.A[source];
            WriteDataRegisterWord(destination, value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordAddressToData);
        }

        private void ExecuteMoveWordDataToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            WriteGeneralRegister(true, destination, unchecked((uint)(int)(short)State.D[source]));
            CompleteTiming(M68kInstructionTimingKey.MoveWordDataToAddress);
        }

        private void ExecuteMoveByteImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var value = (byte)FetchWord();
            WriteDataRegisterByte(register, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteImmediateToData);
        }

        private void ExecuteMoveByteImmediateToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var addressRegister = (opcode >> 9) & 7;
            var value = (byte)FetchWord();
            var displacement = unchecked((int)(short)FetchWord());
            WriteByte(unchecked((uint)(State.A[addressRegister] + displacement)), value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteImmediateToAddressDisplacement);
        }

        private void ExecuteMoveByteImmediateToBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var baseRegister = (opcode >> 9) & 7;
            var value = (byte)FetchWord();
            var extension = FetchWord();
            WriteByte(CalculateBriefIndexedAddress(baseRegister, extension, opcode), value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteImmediateToBriefIndexed);
        }

        private void ExecuteMoveWordImmediateToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var addressRegister = (opcode >> 9) & 7;
            var value = FetchWord();
            var displacement = unchecked((int)(short)FetchWord());
            WriteWord(unchecked((uint)(State.A[addressRegister] + displacement)), value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordImmediateToAddressDisplacement);
        }

        private void ExecuteMoveWordImmediateToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var value = FetchWord();
            WriteWord(State.A[(opcode >> 9) & 7], value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordImmediateToAddressIndirect);
        }

        private void ExecuteMoveAbsoluteWordToAbsoluteWord(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = (opcode >> 12) switch { 1 => M68kOperandSize.Byte, 3 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var source = unchecked((uint)(int)(short)FetchWord());
            var value = ReadSized(source, size);
            var destination = unchecked((uint)(int)(short)FetchWord());
            WriteSized(destination, value, size);
            SetMoveFlags(value, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.MoveByteAbsoluteWordToAbsoluteWord,
                M68kOperandSize.Word => M68kInstructionTimingKey.MoveWordAbsoluteWordToAbsoluteWord,
                _ => M68kInstructionTimingKey.MoveLongAbsoluteWordToAbsoluteWord
            });
        }

        private void ExecuteMoveAbsoluteWordToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = (opcode >> 12) == 1 ? M68kOperandSize.Byte : M68kOperandSize.Word;
            var address = unchecked((uint)(int)(short)FetchWord());
            var value = ReadSized(address, size);
            WriteDataRegisterSized((opcode >> 9) & 7, value, size);
            SetMoveFlags(value, size);
            CompleteTiming(size == M68kOperandSize.Byte ? M68kInstructionTimingKey.MoveByteAbsoluteWordToData : M68kInstructionTimingKey.MoveWordAbsoluteWordToData);
        }

        private void ExecuteMovePredecrementToPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = (opcode >> 12) switch { 1 => M68kOperandSize.Byte, 3 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var source = opcode & 7;
            var destination = (opcode >> 9) & 7;
            var sourceAddress = unchecked(State.A[source] - M68kIntegerSemantics.AddressIncrement(source, size));
            WriteGeneralRegister(true, source, sourceAddress);
            var value = ReadSized(sourceAddress, size);
            var destinationAddress = unchecked(State.A[destination] - M68kIntegerSemantics.AddressIncrement(destination, size));
            WriteGeneralRegister(true, destination, destinationAddress);
            WriteSized(destinationAddress, value, size);
            SetMoveFlags(value, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.MoveBytePredecrementToPredecrement,
                M68kOperandSize.Word => M68kInstructionTimingKey.MoveWordPredecrementToPredecrement,
                _ => M68kInstructionTimingKey.MoveLongPredecrementToPredecrement
            });
        }

        private void ExecuteMovePredecrementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = (opcode >> 12) == 1 ? M68kOperandSize.Byte : M68kOperandSize.Word;
            var source = opcode & 7;
            var address = unchecked(State.A[source] - M68kIntegerSemantics.AddressIncrement(source, size));
            WriteGeneralRegister(true, source, address);
            var value = ReadSized(address, size);
            WriteDataRegisterSized((opcode >> 9) & 7, value, size);
            SetMoveFlags(value, size);
            CompleteTiming(size == M68kOperandSize.Byte ? M68kInstructionTimingKey.MoveBytePredecrementToData : M68kInstructionTimingKey.MoveWordPredecrementToData);
        }

        private void ExecuteMoveAbsoluteLongToIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = (opcode >> 12) switch { 1 => M68kOperandSize.Byte, 3 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var source = FetchLong();
            var value = ReadSized(source, size);
            var extension = FetchWord();
            var address = CalculateBriefIndexedAddress((opcode >> 9) & 7, extension, opcode);
            WriteSized(address, value, size);
            SetMoveFlags(value, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.MoveByteAbsoluteLongToBriefIndexed,
                M68kOperandSize.Word => M68kInstructionTimingKey.MoveWordAbsoluteLongToBriefIndexed,
                _ => M68kInstructionTimingKey.MoveLongAbsoluteLongToBriefIndexed
            });
        }

        private void ExecuteMoveIndirectToIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = (opcode >> 12) switch { 1 => M68kOperandSize.Byte, 3 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var value = ReadSized(State.A[opcode & 7], size);
            var extension = FetchWord();
            var address = CalculateBriefIndexedAddress((opcode >> 9) & 7, extension, opcode);
            WriteSized(address, value, size);
            SetMoveFlags(value, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.MoveByteAddressIndirectToBriefIndexed,
                M68kOperandSize.Word => M68kInstructionTimingKey.MoveWordAddressIndirectToBriefIndexed,
                _ => M68kInstructionTimingKey.MoveLongAddressIndirectToBriefIndexed
            });
        }

        private void ExecuteMoveIndirectToPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = (opcode >> 12) == 1 ? M68kOperandSize.Byte : M68kOperandSize.Word;
            var value = ReadSized(State.A[opcode & 7], size);
            var destination = (opcode >> 9) & 7;
            var address = State.A[destination];
            WriteSized(address, value, size);
            WriteGeneralRegister(true, destination, unchecked(address + M68kIntegerSemantics.AddressIncrement(destination, size)));
            SetMoveFlags(value, size);
            CompleteTiming(size == M68kOperandSize.Byte ? M68kInstructionTimingKey.MoveByteAddressIndirectToPostIncrement : M68kInstructionTimingKey.MoveWordAddressIndirectToPostIncrement);
        }

        private void ExecuteMoveIndexedToPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = (opcode >> 12) switch { 1 => M68kOperandSize.Byte, 3 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var sourceBase = (opcode & 0x3F) == 0x3B ? State.ProgramCounter : State.A[opcode & 7];
            var sourceExtension = FetchWord();
            var value = ReadSized(CalculateIndexedOperandAddress(sourceBase, sourceExtension, opcode), size);
            var destination = (opcode >> 9) & 7;
            var address = State.A[destination];
            WriteSized(address, value, size);
            WriteGeneralRegister(true, destination, unchecked(address + M68kIntegerSemantics.AddressIncrement(destination, size)));
            SetMoveFlags(value, size);
            CompleteIndexedMoveTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.MoveByteBriefIndexedToPostIncrement,
                M68kOperandSize.Word => M68kInstructionTimingKey.MoveWordBriefIndexedToPostIncrement,
                _ => M68kInstructionTimingKey.MoveLongBriefIndexedToPostIncrement
            }, sourceExtension, memoryDestination: true);
        }

        private void ExecuteMoveIndexedToAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = (opcode >> 12) switch { 1 => M68kOperandSize.Byte, 3 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var sourceBase = (opcode & 0x3F) == 0x3B ? State.ProgramCounter : State.A[opcode & 7];
            var extension = FetchWord();
            var value = ReadSized(CalculateIndexedOperandAddress(sourceBase, extension, opcode), size);
            WriteSized(FetchLong(), value, size);
            SetMoveFlags(value, size);
            CompleteIndexedMoveTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.MoveByteBriefIndexedToAbsoluteLong,
                M68kOperandSize.Word => M68kInstructionTimingKey.MoveWordBriefIndexedToAbsoluteLong,
                _ => M68kInstructionTimingKey.MoveLongBriefIndexedToAbsoluteLong
            }, extension, memoryDestination: true, destinationExtraCycles: 2);
        }

        private void ExecuteMoveIndexedToIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = (opcode >> 12) switch { 1 => M68kOperandSize.Byte, 3 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var sourceBase = (opcode & 0x3F) == 0x3B ? State.ProgramCounter : State.A[opcode & 7];
            var sourceExtension = FetchWord();
            var value = ReadSized(CalculateBriefIndexedAddress(sourceBase, sourceExtension, opcode), size);
            var destinationExtension = FetchWord();
            WriteSized(CalculateBriefIndexedAddress((opcode >> 9) & 7, destinationExtension, opcode), value, size);
            SetMoveFlags(value, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.MoveByteBriefIndexedToBriefIndexed,
                M68kOperandSize.Word => M68kInstructionTimingKey.MoveWordBriefIndexedToBriefIndexed,
                _ => M68kInstructionTimingKey.MoveLongBriefIndexedToBriefIndexed
            });
        }

        private void ExecuteMovePostIncrementToAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = (opcode >> 12) switch { 1 => M68kOperandSize.Byte, 3 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var source = opcode & 7;
            var address = State.A[source];
            var destination = FetchLong();
            var value = ReadSized(address, size);
            WriteGeneralRegister(true, source, unchecked(address + M68kIntegerSemantics.AddressIncrement(source, size)));
            WriteSized(destination, value, size);
            SetMoveFlags(value, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.MoveBytePostIncrementToAbsoluteLong,
                M68kOperandSize.Word => M68kInstructionTimingKey.MoveWordPostIncrementToAbsoluteLong,
                _ => M68kInstructionTimingKey.MoveLongPostIncrementToAbsoluteLong
            });
        }

        private void ExecuteMoveWordImmediateToPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var value = FetchWord();
            var address = State.A[destination];
            WriteWord(address, value);
            WriteGeneralRegister(true, destination, unchecked(address + 2));
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordImmediateToPostIncrement);
        }

        private void ExecuteMoveByteImmediateToPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var value = (byte)FetchWord();
            var destination = (opcode >> 9) & 7;
            var address = unchecked(State.A[destination] - M68kIntegerSemantics.AddressIncrement(destination, M68kOperandSize.Byte));
            WriteGeneralRegister(true, destination, address); WriteByte(address, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteImmediateToPredecrement);
        }

        private void ExecuteMoveWordImmediateToPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var addressRegister = (opcode >> 9) & 7;
            var value = FetchWord();
            State.A[addressRegister] = unchecked(State.A[addressRegister] - 2u);
            WriteWord(State.A[addressRegister], value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordImmediateToPredecrement);
        }

        private void ExecuteMoveWordDataToPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var address = State.A[destination] - 2u;
            State.A[destination] = address;
            var value = (ushort)State.D[source];
            WriteWord(address, value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordDataToPredecrement);
        }

        private void ExecuteMoveLongDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var value = State.D[source];
            State.D[destination] = value;
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongDataToData);
        }

        private void ExecuteMoveLongDataToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            WriteGeneralRegister(true, destination, State.D[source]);
            CompleteTiming(M68kInstructionTimingKey.MoveLongDataToAddress);
        }

        private void ExecuteMoveLongDataToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var value = State.D[source];
            WriteLong(State.A[destination], value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongDataToAddressIndirect);
        }

        private void ExecuteMoveLongDataToPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var value = State.D[sourceRegister];
            var address = State.A[destinationRegister];
            WriteLong(address, value);
            State.A[destinationRegister] = unchecked(address + 4u);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongDataToPostIncrement);
        }

        private void ExecuteMoveLongDataToPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var address = State.A[destination] - 4;
            WriteGeneralRegister(true, destination, address);
            var value = State.D[source];
            WriteLong(address, value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongDataToPredecrement);
        }

        private void ExecuteMoveLongDataToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var value = State.D[source];
            WriteLong(unchecked((uint)(State.A[destination] + displacement)), value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongDataToAddressDisplacement);
        }

        private void ExecuteMoveLongDataToBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var extension = FetchWord();
            var value = State.D[source];
            WriteLong(CalculateIndexedOperandAddress(State.A[destination], extension, opcode), value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteIndexedRegisterStoreTiming(M68kInstructionTimingKey.MoveLongDataToBriefIndexed, extension);
        }

        private void ExecuteMoveLongAddressToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            WriteGeneralRegister(true, destination, State.A[source]);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressToAddress);
        }

        private void ExecuteMoveLongAddressToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var value = State.A[source];
            WriteLong(State.A[destination], value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressToAddressIndirect);
        }

        private void ExecuteMoveLongAddressToPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var value = State.A[source];
            var address = State.A[destination] - 4;
            WriteGeneralRegister(true, destination, address);
            WriteLong(address, value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressToPredecrement);
        }

        private void ExecuteMoveLongAddressToBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var extension = FetchWord();
            var value = State.A[source];
            WriteLong(CalculateIndexedOperandAddress(State.A[destination], extension, opcode), value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteIndexedRegisterStoreTiming(M68kInstructionTimingKey.MoveLongAddressToBriefIndexed, extension);
        }

        private void ExecuteMoveByteOrWordAddressIndirectToPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = (opcode >> 12) == 1 ? M68kOperandSize.Byte : M68kOperandSize.Word;
            var value = ReadSized(State.A[opcode & 7], size);
            var destination = (opcode >> 9) & 7;
            var address = unchecked(State.A[destination] - M68kIntegerSemantics.AddressIncrement(destination, size));
            WriteGeneralRegister(true, destination, address);
            WriteSized(address, value, size);
            SetMoveFlags(value, size);
            CompleteTiming(size == M68kOperandSize.Byte
                ? M68kInstructionTimingKey.MoveByteAddressIndirectToPredecrement
                : M68kInstructionTimingKey.MoveWordAddressIndirectToPredecrement);
        }

        private void ExecuteMoveLongAddressIndirectToPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var value = ReadLong(State.A[source]);
            var destinationAddress = unchecked(State.A[destination] - 4);
            WriteGeneralRegister(true, destination, destinationAddress);
            WriteLong(destinationAddress, value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressIndirectToPredecrement);
        }

        private void ExecuteMoveLongPostIncrementToPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = opcode & 7;
            var destination = (opcode >> 9) & 7;
            var value = ReadLong(State.A[source]);
            State.A[source] = unchecked(State.A[source] + 4u);
            var destinationAddress = unchecked(State.A[destination] - 4u);
            State.A[destination] = destinationAddress;
            WriteLong(destinationAddress, value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongPostIncrementToPredecrement);
        }

        private void ExecuteMoveLongAddressIndirectToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var value = ReadLong(State.A[source]);
            var destinationAddress = unchecked((uint)(State.A[destination] + displacement));
            WriteLong(destinationAddress, value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressIndirectToAddressDisplacement);
        }

        private void ExecuteMoveLongAddressToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var value = State.A[source];
            WriteLong(unchecked((uint)(State.A[destination] + displacement)), value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressToAddressDisplacement);
        }

        private void ExecuteMoveLongAddressToPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var address = State.A[destination];
            var value = State.A[source];
            WriteLong(address, value);
            State.A[destination] = address + 4;
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressToPostIncrement);
        }

        private void ExecuteMoveLongAddressToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var value = State.A[source];
            State.D[destination] = value;
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressToData);
        }

        private void ExecuteMoveLongAddressIndirectToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var value = ReadLong(State.A[source]);
            State.D[destination] = value;
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressIndirectToData);
        }

        private void ExecuteMoveLongPredecrementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var sourceAddress = State.A[source] - 4u;
            WriteGeneralRegister(true, source, sourceAddress);
            var value = ReadLong(sourceAddress);
            State.D[destination] = value;
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongPredecrementToData);
        }

        private void ExecuteMoveWordAddressIndirectToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var address = State.A[source];
            if ((address & 1) != 0 &&
                TryRaiseMisalignedWordDataRead(address, State.LastInstructionProgramCounter))
            {
                return;
            }

            var value = ReadWord(address);
            WriteDataRegisterWord(destination, value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressIndirectToData);
        }

        private void ExecuteMoveLongAddressIndirectToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var value = ReadLong(State.A[source]);
            WriteGeneralRegister(true, destination, value);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressIndirectToAddress);
        }

        private void ExecuteMoveLongPostIncrementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var address = State.A[source];
            var value = ReadLong(address);
            State.D[destination] = value;
            WriteGeneralRegister(true, source, address + 4);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongPostIncrementToData);
        }

        private void ExecuteMoveLongPredecrementToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var value = ReadLongPredecrement(opcode & 7);
            WriteGeneralRegister(true, (opcode >> 9) & 7, value);
            CompleteTiming(M68kInstructionTimingKey.MoveLongPredecrementToAddress);
        }

        private void ExecuteMoveLongPostIncrementToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var address = State.A[source];
            var value = ReadLong(address);
            WriteGeneralRegister(true, source, address + 4);
            WriteGeneralRegister(true, destination, value);
            CompleteTiming(M68kInstructionTimingKey.MoveLongPostIncrementToAddress);
        }

        private void ExecuteMoveLongPostIncrementToPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = opcode & 7;
            var destination = (opcode >> 9) & 7;
            var sourceAddress = State.A[source];
            var value = ReadLong(sourceAddress);
            WriteGeneralRegister(true, source, sourceAddress + 4u);
            var destinationAddress = State.A[destination];
            WriteLong(destinationAddress, value);
            WriteGeneralRegister(true, destination, destinationAddress + 4u);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongPostIncrementToPostIncrement);
        }

        private void ExecuteMoveLongPostIncrementToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var sourceAddress = State.A[source];
            var value = ReadLong(sourceAddress);
            WriteGeneralRegister(true, source, sourceAddress + 4);
            var destinationAddress = unchecked((uint)(State.A[destination] + displacement));
            WriteLong(destinationAddress, value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongPostIncrementToAddressDisplacement);
        }

        private void ExecuteMoveLongAddressDisplacementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var value = ReadLong(unchecked((uint)(State.A[source] + displacement)));
            State.D[destination] = value;
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressDisplacementToData);
        }

        private void ExecuteMoveLongAddressDisplacementToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var value = ReadLong(unchecked((uint)(State.A[source] + displacement)));
            WriteGeneralRegister(true, destination, value);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressDisplacementToAddress);
        }

        private void ExecuteMoveLongAddressDisplacementToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var value = ReadLong(unchecked((uint)(State.A[source] + displacement)));
            WriteLong(State.A[destination], value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressDisplacementToAddressIndirect);
        }

        private void ExecuteMoveLongAddressDisplacementToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var sourceDisplacement = unchecked((int)(short)FetchWord());
            var value = ReadLong(unchecked((uint)(State.A[sourceRegister] + sourceDisplacement)));
            var destinationDisplacement = unchecked((int)(short)FetchWord());
            WriteLong(unchecked((uint)(State.A[destinationRegister] + destinationDisplacement)), value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressDisplacementToAddressDisplacement);
        }

        private void ExecuteMoveByteAddressDisplacementToBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceDisplacement = unchecked((short)FetchWord());
            var value = ReadByte(unchecked((uint)(State.A[opcode & 7] + sourceDisplacement)));
            var destinationExtension = FetchWord();
            var destinationAddress = CalculateBriefIndexedAddress((opcode >> 9) & 7, destinationExtension, opcode);
            WriteByte(destinationAddress, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteAddressDisplacementToBriefIndexed);
        }

        private void ExecuteMoveLongAddressDisplacementToBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var sourceDisplacement = unchecked((int)(short)FetchWord());
            var value = ReadLong(unchecked((uint)(State.A[sourceRegister] + sourceDisplacement)));
            var destinationExtension = FetchWord();
            var destinationAddress = CalculateBriefIndexedAddress(destinationRegister, destinationExtension, opcode);
            WriteLong(destinationAddress, value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressDisplacementToBriefIndexed);
        }

        private void ExecuteMoveLongAddressDisplacementToPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var sourceAddress = unchecked((uint)(State.A[source] + displacement));
            var value = ReadLong(sourceAddress);
            var destinationAddress = State.A[destination] - 4;
            WriteGeneralRegister(true, destination, destinationAddress);
            WriteLong(destinationAddress, value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressDisplacementToPredecrement);
        }

        private void ExecuteMoveByteOrWordAddressDisplacementToPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = (opcode >> 12) == 1 ? M68kOperandSize.Byte : M68kOperandSize.Word;
            var displacement = unchecked((int)(short)FetchWord());
            var value = ReadSized(unchecked((uint)(State.A[opcode & 7] + displacement)), size);
            var destination = (opcode >> 9) & 7;
            var address = State.A[destination];
            WriteSized(address, value, size);
            WriteGeneralRegister(true, destination, unchecked(address + M68kIntegerSemantics.AddressIncrement(destination, size)));
            SetMoveFlags(value, size);
            CompleteTiming(size == M68kOperandSize.Byte
                ? M68kInstructionTimingKey.MoveByteAddressDisplacementToPostIncrement
                : M68kInstructionTimingKey.MoveWordAddressDisplacementToPostIncrement);
        }

        private void ExecuteMoveLongAddressDisplacementToPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var value = ReadLong(unchecked((uint)(State.A[source] + displacement)));
            var destinationAddress = State.A[destination];
            WriteLong(destinationAddress, value);
            WriteGeneralRegister(true, destination, destinationAddress + 4);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressDisplacementToPostIncrement);
        }

        private void ExecuteMoveLongPcDisplacementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var sourceAddress = GetPcDisplacementAddress();
            var value = ReadLong(sourceAddress);
            State.D[destination] = value;
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressDisplacementToData);
        }

        private void ExecuteMoveLongPcDisplacementToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var sourceAddress = GetPcDisplacementAddress();
            var value = ReadLong(sourceAddress);
            WriteGeneralRegister(true, destination, value);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressDisplacementToAddress);
        }

        private void ExecuteMoveLongPcDisplacementToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var sourceAddress = GetPcDisplacementAddress();
            var value = ReadLong(sourceAddress);
            var destinationDisplacement = unchecked((int)(short)FetchWord());
            WriteLong(unchecked((uint)(State.A[destination] + destinationDisplacement)), value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongPcDisplacementToAddressDisplacement);
        }

        private void ExecuteMoveLongPcDisplacementToPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var sourceAddress = GetPcDisplacementAddress();
            var value = ReadLong(sourceAddress);
            var destinationAddress = State.A[destination] - 4;
            WriteGeneralRegister(true, destination, destinationAddress);
            WriteLong(destinationAddress, value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressDisplacementToPredecrement);
        }

        private void ExecuteMoveLongPcDisplacementToPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var sourceAddress = GetPcDisplacementAddress();
            var value = ReadLong(sourceAddress);
            var destinationAddress = State.A[destination];
            WriteLong(destinationAddress, value);
            WriteGeneralRegister(true, destination, destinationAddress + 4);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressDisplacementToPostIncrement);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint GetPcDisplacementAddress()
        {
            var extensionAddress = State.ProgramCounter;
            return unchecked((uint)(extensionAddress + (int)(short)FetchWord()));
        }

        private void ExecuteMoveLongBriefIndexedToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var baseRegister = opcode & 7;
            var extension = FetchWord();
            var value = ReadLong(CalculateIndexedOperandAddress(State.A[baseRegister], extension, opcode));
            State.D[destination] = value;
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteIndexedMoveTiming(M68kInstructionTimingKey.MoveLongBriefIndexedToData, extension);
        }

        private void ExecuteMoveLongBriefIndexedToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var baseRegister = opcode & 7;
            var extension = FetchWord();
            var value = ReadLong(CalculateIndexedOperandAddress(State.A[baseRegister], extension, opcode));
            WriteGeneralRegister(true, destination, value);
            CompleteIndexedMoveTiming(M68kInstructionTimingKey.MoveLongBriefIndexedToAddress, extension);
        }

        private void ExecuteMoveLongPcBriefIndexedToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var extensionAddress = State.ProgramCounter;
            var extension = FetchWord();
            var value = ReadLong(CalculateIndexedOperandAddress(extensionAddress, extension, opcode));
            WriteGeneralRegister(true, destination, value);
            CompleteIndexedMoveTiming(M68kInstructionTimingKey.MoveLongBriefIndexedToAddress, extension);
        }

        private void ExecuteMoveLongBriefIndexedToPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var baseRegister = opcode & 7;
            var extension = FetchWord();
            var sourceAddress = CalculateBriefIndexedAddress(baseRegister, extension, opcode);
            var value = ReadLong(sourceAddress);
            var destinationAddress = State.A[destination] - 4;
            WriteGeneralRegister(true, destination, destinationAddress);
            WriteLong(destinationAddress, value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongBriefIndexedToPredecrement);
        }

        private void ExecuteMoveLongPcBriefIndexedToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var extensionAddress = State.ProgramCounter;
            var sourceExtension = FetchWord();
            var value = ReadLong(CalculateIndexedOperandAddress(extensionAddress, sourceExtension, opcode));
            var destinationDisplacement = unchecked((int)(short)FetchWord());
            WriteLong(unchecked((uint)(State.A[destination] + destinationDisplacement)), value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteIndexedMoveTiming(M68kInstructionTimingKey.MoveLongBriefIndexedToAddressDisplacement, sourceExtension, memoryDestination: true);
        }

        private void ExecuteMoveLongAddressBriefIndexedToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = opcode & 7;
            var destination = (opcode >> 9) & 7;
            var sourceExtension = FetchWord();
            var value = ReadLong(CalculateIndexedOperandAddress(State.A[source], sourceExtension, opcode));
            var destinationDisplacement = unchecked((int)(short)FetchWord());
            WriteLong(unchecked((uint)(State.A[destination] + destinationDisplacement)), value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteIndexedMoveTiming(M68kInstructionTimingKey.MoveLongBriefIndexedToAddressDisplacement, sourceExtension, memoryDestination: true);
        }

        private void ExecuteMoveLongBriefIndexedToBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = opcode & 7;
            var destination = (opcode >> 9) & 7;
            var sourceExtension = FetchWord();
            var value = ReadLong(CalculateBriefIndexedAddress(source, sourceExtension, opcode));
            var destinationExtension = FetchWord();
            WriteLong(CalculateBriefIndexedAddress(destination, destinationExtension, opcode), value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongBriefIndexedToBriefIndexed);
        }

        private void ExecuteMoveLongAddressIndirectToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var value = ReadLong(State.A[source]);
            WriteLong(State.A[destination], value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressIndirectToAddressIndirect);
        }

        private void ExecuteMoveLongAddressIndirectToPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var destinationAddress = State.A[destination];
            var value = ReadLong(State.A[source]);
            WriteLong(destinationAddress, value);
            State.A[destination] = destinationAddress + 4;
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressIndirectToPostIncrement);
        }

        private void ExecuteMoveLongAbsoluteWordToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var address = unchecked((uint)(short)FetchWord());
            var value = ReadLong(address);
            State.D[destination] = value;
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAbsoluteWordToData);
        }

        private void ExecuteMoveLongAbsoluteWordToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var address = unchecked((uint)(short)FetchWord());
            State.A[destination] = ReadLong(address);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAbsoluteWordToAddress);
        }

        private void ExecuteMoveLongAbsoluteLongToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var address = FetchLong();
            var value = ReadLong(address);
            State.D[destination] = value;
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAbsoluteLongToData);
        }

        private void ExecuteMoveLongAbsoluteLongToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var address = FetchLong();
            State.A[destination] = ReadLong(address);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAbsoluteLongToAddress);
        }

        private void ExecuteMoveLongAbsoluteLongToPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var sourceAddress = FetchLong();
            var value = ReadLong(sourceAddress);
            var destinationAddress = State.A[destination] - 4;
            WriteGeneralRegister(true, destination, destinationAddress);
            WriteLong(destinationAddress, value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAbsoluteLongToPredecrement);
        }

        private void ExecuteMoveLongAbsoluteWordToAbsoluteLong()
        {
            BeginInstruction(0x23F8);
            _ = FetchWord();
            var sourceAddress = unchecked((uint)(short)FetchWord());
            var value = ReadLong(sourceAddress);
            var destinationAddress = FetchLong();
            WriteLong(destinationAddress, value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAbsoluteWordToAbsoluteLong);
        }

        private void ExecuteMoveLongDataToAbsoluteWord(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = opcode & 7;
            var address = unchecked((uint)(short)FetchWord());
            var value = State.D[source];
            WriteLong(address, value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongDataToAbsoluteLong);
        }

        private void ExecuteMoveLongDataToAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = opcode & 7;
            var address = FetchLong();
            var value = State.D[source];
            WriteLong(address, value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongDataToAbsoluteLong);
        }

        private void ExecuteMoveLongAddressToAbsoluteWord(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = opcode & 7;
            var address = unchecked((uint)(short)FetchWord());
            var value = State.A[source];
            WriteLong(address, value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressToAbsoluteLong);
        }

        private void ExecuteMoveLongAddressToAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = opcode & 7;
            var address = FetchLong();
            var value = State.A[source];
            WriteLong(address, value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressToAbsoluteLong);
        }

        private void ExecuteMoveLongAddressIndirectToAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var value = ReadLong(State.A[opcode & 7]);
            var destinationAddress = FetchLong();
            WriteLong(destinationAddress, value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressIndirectToAbsoluteLong);
        }

        private void ExecuteMoveLongAddressDisplacementToAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var sourceAddress = unchecked((uint)(State.A[sourceRegister] + displacement));
            var destinationAddress = FetchLong();
            var value = ReadLong(sourceAddress);
            WriteLong(destinationAddress, value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressDisplacementToAbsoluteLong);
        }

        private void ExecuteMoveByteDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var value = (byte)State.D[source];
            WriteDataRegisterByte(destination, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteDataToData);
        }

        private void ExecuteMoveByteAddressIndirectToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var value = ReadByte(State.A[source]);
            WriteDataRegisterByte(destination, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteAddressIndirectToData);
        }

        private void ExecuteMoveByteAddressIndirectToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var value = ReadByte(State.A[source]);
            WriteByte(State.A[destination], value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteAddressIndirectToAddressIndirect);
        }

        private void ExecuteMoveBytePostIncrementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var value = ReadByte(State.A[source]);
            State.A[source] += source == 7 ? 2u : 1u;
            WriteDataRegisterByte(destination, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveBytePostIncrementToData);
        }

        private void ExecuteMoveByteAddressDisplacementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var value = ReadByte(unchecked((uint)(State.A[source] + displacement)));
            WriteDataRegisterByte(destination, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteAddressDisplacementToData);
        }

        private void ExecuteMoveByteBriefIndexedToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var baseRegister = opcode & 7;
            var extension = FetchWord();
            var value = ReadByte(CalculateIndexedOperandAddress(State.A[baseRegister], extension, opcode));
            WriteDataRegisterByte(destination, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteIndexedMoveTiming(M68kInstructionTimingKey.MoveByteBriefIndexedToData, extension);
        }

        private void ExecuteMoveBytePcBriefIndexedToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var extensionAddress = State.ProgramCounter;
            var extension = FetchWord();
            var value = ReadByte(CalculateIndexedOperandAddress(extensionAddress, extension, opcode));
            WriteDataRegisterByte(destination, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteIndexedMoveTiming(M68kInstructionTimingKey.MoveByteBriefIndexedToData, extension);
        }

        private void ExecuteMoveByteAbsoluteLongToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var address = FetchLong();
            var value = ReadByte(address);
            WriteDataRegisterByte(destination, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteAbsoluteLongToData);
        }

        private void ExecuteMoveWordAbsoluteLongToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var address = FetchLong();
            var value = ReadWord(address);
            WriteDataRegisterWord(destination, value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordAbsoluteLongToData);
        }

        private void ExecuteMoveWordAbsoluteLongToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var sourceAddress = FetchLong();
            var value = unchecked((uint)(int)(short)ReadWord(sourceAddress));
            WriteGeneralRegister(true, destination, value);
            CompleteTiming(M68kInstructionTimingKey.MoveWordAbsoluteLongToAddress);
        }

        private void ExecuteMoveWordPcBriefIndexedToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var extensionAddress = State.ProgramCounter;
            var extension = FetchWord();
            var value = unchecked((uint)(int)(short)ReadWord(CalculateIndexedOperandAddress(extensionAddress, extension, opcode)));
            WriteGeneralRegister(true, (opcode >> 9) & 7, value);
            CompleteIndexedMoveTiming(M68kInstructionTimingKey.MoveWordPcBriefIndexedToAddress, extension);
        }

        private void ExecuteMoveWordAddressDisplacementToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var displacement = unchecked((short)FetchWord());
            var value = unchecked((uint)(int)(short)ReadWord(unchecked((uint)(State.A[opcode & 7] + displacement))));
            WriteGeneralRegister(true, (opcode >> 9) & 7, value);
            CompleteTiming(M68kInstructionTimingKey.MoveWordAddressDisplacementToAddress);
        }

        private void ExecuteMoveWordAddressIndirectToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var value = M68kCpuState.SignExtend(ReadWord(State.A[source]), M68kOperandSize.Word);
            WriteGeneralRegister(true, destination, value);
            CompleteTiming(M68kInstructionTimingKey.MoveWordAddressIndirectToAddress);
        }

        private void ExecuteMoveWordAddressDisplacementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var value = ReadWord(unchecked((uint)(State.A[source] + displacement)));
            WriteDataRegisterWord(destination, value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordAddressDisplacementToData);
        }

        private void ExecuteMoveByteAddressDisplacementToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = opcode & 7;
            var destination = (opcode >> 9) & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var value = ReadByte(unchecked((uint)(State.A[source] + displacement)));
            WriteByte(State.A[destination], value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteAddressDisplacementToAddressIndirect);
        }

        private void ExecuteMoveWordAddressDisplacementToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = opcode & 7;
            var destination = (opcode >> 9) & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var value = ReadWord(unchecked((uint)(State.A[source] + displacement)));
            WriteWord(State.A[destination], value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordAddressDisplacementToAddressIndirect);
        }

        private void ExecuteMoveWordAddressIndirectToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var value = ReadWord(State.A[opcode & 7]);
            WriteWord(State.A[(opcode >> 9) & 7], value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordAddressIndirectToAddressIndirect);
        }

        private void ExecuteMoveLongExtendedToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceForm = opcode & 7;
            var value = ReadLongExtendedSource(sourceForm, opcode);
            WriteLong(State.A[(opcode >> 9) & 7], value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(sourceForm switch
            {
                0 => M68kInstructionTimingKey.MoveLongAbsoluteWordToAddressIndirect,
                1 => M68kInstructionTimingKey.MoveLongAbsoluteLongToAddressIndirect,
                2 => M68kInstructionTimingKey.MoveLongPcDisplacementToAddressIndirect,
                _ => M68kInstructionTimingKey.MoveLongPcBriefIndexedToAddressIndirect
            });
        }

        private void ExecuteMovePredecrementToMemory(ushort opcode, bool postIncrement)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = (opcode >> 12) switch { 1 => M68kOperandSize.Byte, 3 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var source = opcode & 7;
            var sourceAddress = unchecked(State.A[source] - M68kIntegerSemantics.AddressIncrement(source, size));
            WriteGeneralRegister(true, source, sourceAddress);
            var value = ReadSized(sourceAddress, size);
            var destination = (opcode >> 9) & 7;
            var displacement = postIncrement ? 0 : unchecked((int)(short)FetchWord());
            var destinationAddress = unchecked((uint)(State.A[destination] + displacement));
            WriteSized(destinationAddress, value, size);
            if (postIncrement)
                WriteGeneralRegister(true, destination, unchecked(destinationAddress + M68kIntegerSemantics.AddressIncrement(destination, size)));
            SetMoveFlags(value, size);
            CompleteTiming((postIncrement, size) switch
            {
                (false, M68kOperandSize.Byte) => M68kInstructionTimingKey.MoveBytePredecrementToAddressDisplacement,
                (false, M68kOperandSize.Word) => M68kInstructionTimingKey.MoveWordPredecrementToAddressDisplacement,
                (false, _) => M68kInstructionTimingKey.MoveLongPredecrementToAddressDisplacement,
                (true, M68kOperandSize.Byte) => M68kInstructionTimingKey.MoveBytePredecrementToPostIncrement,
                (true, M68kOperandSize.Word) => M68kInstructionTimingKey.MoveWordPredecrementToPostIncrement,
                _ => M68kInstructionTimingKey.MoveLongPredecrementToPostIncrement
            });
        }

        private void ExecuteMoveBriefIndexedToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = (opcode >> 12) switch { 1 => M68kOperandSize.Byte, 3 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var extension = FetchWord();
            var value = ReadSized(CalculateBriefIndexedAddress(opcode & 7, extension, opcode), size);
            WriteSized(State.A[(opcode >> 9) & 7], value, size);
            SetMoveFlags(value, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.MoveByteBriefIndexedToAddressIndirect,
                M68kOperandSize.Word => M68kInstructionTimingKey.MoveWordBriefIndexedToAddressIndirect,
                _ => M68kInstructionTimingKey.MoveLongBriefIndexedToAddressIndirect
            });
        }

        private void ExecuteMovePostIncrementToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = (opcode >> 12) switch { 1 => M68kOperandSize.Byte, 3 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var sourceRegister = opcode & 7;
            var address = State.A[sourceRegister];
            var value = ReadSized(address, size);
            WriteGeneralRegister(true, sourceRegister, unchecked(address + M68kIntegerSemantics.AddressIncrement(sourceRegister, size)));
            // Resolve the destination after source EA side effects, including An aliasing.
            WriteSized(State.A[(opcode >> 9) & 7], value, size);
            SetMoveFlags(value, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.MoveBytePostIncrementToAddressIndirect,
                M68kOperandSize.Word => M68kInstructionTimingKey.MoveWordPostIncrementToAddressIndirect,
                _ => M68kInstructionTimingKey.MoveLongPostIncrementToAddressIndirect
            });
        }

        private void ExecuteMoveIndirectToDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = opcode & 7;
            var destination = (opcode >> 9) & 7;
            var size = (opcode >> 12) == 1 ? M68kOperandSize.Byte : M68kOperandSize.Word;
            var value = ReadSized(State.A[source], size);
            var displacement = unchecked((int)(short)FetchWord());
            WriteSized(unchecked((uint)(State.A[destination] + displacement)), value, size);
            SetMoveFlags(value, size);
            CompleteTiming(size == M68kOperandSize.Byte ? M68kInstructionTimingKey.MoveByteAddressIndirectToAddressDisplacement : M68kInstructionTimingKey.MoveWordAddressIndirectToAddressDisplacement);
        }

        private void ExecuteMoveWordAddressBriefIndexedToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var extension = FetchWord();
            var value = ReadWord(CalculateIndexedOperandAddress(State.A[source], extension, opcode));
            WriteDataRegisterWord(destination, value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteIndexedMoveTiming(M68kInstructionTimingKey.MoveWordBriefIndexedToData, extension);
        }

        private void ExecuteMoveWordBriefIndexedToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var sourceExtension = FetchWord();
            var value = ReadWord(CalculateBriefIndexedAddress(sourceRegister, sourceExtension, opcode));
            var destinationDisplacement = unchecked((int)(short)FetchWord());
            WriteWord(unchecked((uint)(State.A[destinationRegister] + destinationDisplacement)), value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordBriefIndexedToAddressDisplacement);
        }

        private void ExecuteMoveLongPcBriefIndexedToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var extensionAddress = State.ProgramCounter;
            var extension = FetchWord();
            var value = ReadLong(CalculateIndexedOperandAddress(extensionAddress, extension, opcode));
            State.D[destination] = value;
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteIndexedMoveTiming(M68kInstructionTimingKey.MoveLongBriefIndexedToData, extension);
        }

        private void ExecuteMoveWordPcBriefIndexedToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var extensionAddress = State.ProgramCounter;
            var extension = FetchWord();
            var value = ReadWord(CalculateIndexedOperandAddress(extensionAddress, extension, opcode));
            WriteDataRegisterWord(destination, value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteIndexedMoveTiming(M68kInstructionTimingKey.MoveWordBriefIndexedToData, extension);
        }

        private void ExecuteMoveWordPcBriefIndexedToPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var extensionAddress = State.ProgramCounter;
            var extension = FetchWord();
            var value = ReadWord(CalculateBriefIndexedAddress(extensionAddress, extension, opcode));
            var destinationAddress = unchecked(State.A[destinationRegister] - 2u);
            State.A[destinationRegister] = destinationAddress;
            WriteWord(destinationAddress, value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordBriefIndexedToPredecrement);
        }

        private void ExecuteMoveWordPostIncrementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var sourceAddress = State.A[sourceRegister];
            var value = ReadWord(sourceAddress);
            State.A[sourceRegister] = sourceAddress + 2;
            WriteDataRegisterWord(destinationRegister, value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordPostIncrementToData);
        }

        private void ExecuteMoveWordPostIncrementToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var sourceAddress = State.A[source];
            var value = ReadWord(sourceAddress);
            WriteGeneralRegister(true, source, sourceAddress + 2);
            WriteGeneralRegister(true, destination, M68kCpuState.SignExtend(value, M68kOperandSize.Word));
            CompleteTiming(M68kInstructionTimingKey.MoveWordPostIncrementToAddress);
        }

        private void ExecuteMoveWordPostIncrementToPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = opcode & 7;
            var destination = (opcode >> 9) & 7;
            var sourceAddress = State.A[source];
            var value = ReadWord(sourceAddress);
            WriteGeneralRegister(true, source, sourceAddress + 2u);
            var destinationAddress = State.A[destination];
            WriteWord(destinationAddress, value);
            WriteGeneralRegister(true, destination, destinationAddress + 2u);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordPostIncrementToPostIncrement);
        }

        private void ExecuteMoveWordDataToPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = opcode & 7;
            var destination = (opcode >> 9) & 7;
            var address = State.A[destination];
            var value = (ushort)State.D[source];
            WriteWord(address, value);
            WriteGeneralRegister(true, destination, address + 2);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordDataToPostIncrement);
        }

        private void ExecuteMoveBytePostIncrementToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var sourceAddress = State.A[sourceRegister];
            var value = ReadByte(sourceAddress);
            WriteGeneralRegister(true, sourceRegister, unchecked(sourceAddress + M68kIntegerSemantics.AddressIncrement(sourceRegister, M68kOperandSize.Byte)));
            var displacement = unchecked((int)(short)FetchWord());
            WriteByte(unchecked((uint)(State.A[destinationRegister] + displacement)), value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveBytePostIncrementToAddressDisplacement);
        }

        private void ExecuteMoveWordPostIncrementToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var sourceAddress = State.A[sourceRegister];
            var value = ReadWord(sourceAddress);
            WriteGeneralRegister(true, sourceRegister, sourceAddress + 2u);
            var displacement = unchecked((int)(short)FetchWord());
            WriteWord(unchecked((uint)(State.A[destinationRegister] + displacement)), value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordPostIncrementToAddressDisplacement);
        }

        private void ExecuteMoveWordDataToAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = opcode & 7;
            var address = FetchLong();
            var value = (ushort)State.D[source];
            WriteWord(address, value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordDataToAbsoluteLong);
        }

        private void ExecuteMoveWordAddressToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var value = (ushort)State.A[opcode & 7];
            WriteWord(State.A[(opcode >> 9) & 7], value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordAddressToAddressIndirect);
        }

        private void ExecuteMoveWordDataToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var value = (ushort)State.D[source];
            WriteWord(State.A[destination], value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordDataToAddressIndirect);
        }

        private void ExecuteMoveWordDataToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var value = (ushort)State.D[source];
            WriteWord(unchecked((uint)(State.A[destination] + displacement)), value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordDataToAddressDisplacement);
        }

        private void ExecuteMoveWordDataToBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var extension = FetchWord();
            var value = (ushort)State.D[source];
            WriteWord(CalculateIndexedOperandAddress(State.A[destination], extension, opcode), value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteIndexedRegisterStoreTiming(M68kInstructionTimingKey.MoveWordDataToBriefIndexed, extension);
        }

        private void ExecuteMoveWordAddressToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var destinationDisplacement = unchecked((int)(short)FetchWord());
            var value = (ushort)State.A[sourceRegister];
            WriteWord(unchecked((uint)(State.A[destinationRegister] + destinationDisplacement)), value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordAddressToAddressDisplacement);
        }

        private void ExecuteMoveWordPcDisplacementToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceExtensionAddress = State.ProgramCounter;
            var sourceDisplacement = unchecked((int)(short)FetchWord());
            var value = ReadWord(unchecked((uint)(sourceExtensionAddress + sourceDisplacement)));
            var destinationDisplacement = unchecked((int)(short)FetchWord());
            WriteWord(unchecked((uint)(State.A[destinationRegister] + destinationDisplacement)), value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordPcDisplacementToAddressDisplacement);
        }

        private void ExecuteMoveWordAddressDisplacementToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var sourceDisplacement = unchecked((int)(short)FetchWord());
            var value = ReadWord(unchecked((uint)(State.A[sourceRegister] + sourceDisplacement)));
            var destinationDisplacement = unchecked((int)(short)FetchWord());
            WriteWord(unchecked((uint)(State.A[destinationRegister] + destinationDisplacement)), value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordAddressDisplacementToAddressDisplacement);
        }

        private void ExecuteMoveByteAddressDisplacementToAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var sourceAddress = unchecked((uint)(State.A[sourceRegister] + displacement));
            var destinationAddress = FetchLong();
            var value = ReadByte(sourceAddress);
            WriteByte(destinationAddress, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteAddressDisplacementToAbsoluteLong);
        }

        private void ExecuteMoveWordAddressDisplacementToAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var sourceAddress = unchecked((uint)(State.A[sourceRegister] + displacement));
            var destinationAddress = FetchLong();
            var value = ReadWord(sourceAddress);
            WriteWord(destinationAddress, value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordAddressDisplacementToAbsoluteLong);
        }

        private void ExecuteMoveWordAbsoluteLongToAbsoluteLong()
        {
            BeginInstruction(0x33F9);
            _ = FetchWord();
            var sourceAddress = FetchLong();
            var destinationAddress = FetchLong();
            var value = ReadWord(sourceAddress);
            WriteWord(destinationAddress, value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordAbsoluteLongToAbsoluteLong);
        }

        private void ExecuteMoveWordAbsoluteLongToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var sourceAddress = FetchLong();
            var displacement = unchecked((int)(short)FetchWord());
            var value = ReadWord(sourceAddress);
            WriteWord(unchecked((uint)(State.A[destination] + displacement)), value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordAbsoluteLongToAddressDisplacement);
        }

        private void ExecuteMoveWordAbsoluteLongToPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceAddress = FetchLong();
            var value = ReadWord(sourceAddress);
            var destinationAddress = State.A[destinationRegister] - 2u;
            State.A[destinationRegister] = destinationAddress;
            WriteWord(destinationAddress, value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordAbsoluteLongToPredecrement);
        }

        private void ExecuteMoveBytePostIncrementToPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = opcode & 7;
            var destination = (opcode >> 9) & 7;
            var value = ReadByte(State.A[source]);
            WriteGeneralRegister(true, source, unchecked(State.A[source] + M68kIntegerSemantics.AddressIncrement(source, M68kOperandSize.Byte)));
            var address = unchecked(State.A[destination] - M68kIntegerSemantics.AddressIncrement(destination, M68kOperandSize.Byte));
            WriteGeneralRegister(true, destination, address); WriteByte(address, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveBytePostIncrementToPredecrement);
        }

        private void ExecuteMoveWordPostIncrementToPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var value = ReadWord(State.A[sourceRegister]);
            State.A[sourceRegister] = unchecked(State.A[sourceRegister] + 2u);
            var destinationAddress = unchecked(State.A[destinationRegister] - 2u);
            State.A[destinationRegister] = destinationAddress;
            WriteWord(destinationAddress, value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordPostIncrementToPredecrement);
        }

        private void ExecuteMoveByteAddressDisplacementToPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var displacement = unchecked((short)FetchWord());
            var value = ReadByte(unchecked((uint)(State.A[opcode & 7] + displacement)));
            var address = unchecked(State.A[destination] - M68kIntegerSemantics.AddressIncrement(destination, M68kOperandSize.Byte));
            WriteGeneralRegister(true, destination, address);
            WriteByte(address, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteAddressDisplacementToPredecrement);
        }

        private void ExecuteMoveWordAddressDisplacementToPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var value = ReadWord(unchecked((uint)(State.A[sourceRegister] + displacement)));
            var destinationAddress = unchecked(State.A[destinationRegister] - 2u);
            State.A[destinationRegister] = destinationAddress;
            WriteWord(destinationAddress, value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordAddressDisplacementToPredecrement);
        }

        private void ExecuteMoveByteDataToAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = opcode & 7;
            var address = FetchLong();
            var value = (byte)State.D[source];
            WriteByte(address, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteDataToAbsoluteLong);
        }

        private void ExecuteMoveByteDataToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var value = (byte)State.D[source];
            WriteByte(State.A[destination], value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteDataToAddressIndirect);
        }

        private void ExecuteMoveByteDataToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var value = (byte)State.D[source];
            WriteByte(unchecked((uint)(State.A[destination] + displacement)), value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteDataToAddressDisplacement);
        }

        private void ExecuteMoveByteAddressDisplacementToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var sourceDisplacement = unchecked((int)(short)FetchWord());
            var value = ReadByte(unchecked((uint)(State.A[sourceRegister] + sourceDisplacement)));
            var destinationDisplacement = unchecked((int)(short)FetchWord());
            WriteByte(unchecked((uint)(State.A[destinationRegister] + destinationDisplacement)), value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteAddressDisplacementToAddressDisplacement);
        }

        private void ExecuteMoveByteBriefIndexedToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var sourceExtension = FetchWord();
            var value = ReadByte(CalculateBriefIndexedAddress(sourceRegister, sourceExtension, opcode));
            var destinationDisplacement = unchecked((int)(short)FetchWord());
            WriteByte(unchecked((uint)(State.A[destinationRegister] + destinationDisplacement)), value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteBriefIndexedToAddressDisplacement);
        }

        private void ExecuteMoveByteDataToBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var baseRegister = (opcode >> 9) & 7;
            var source = opcode & 7;
            var extension = FetchWord();
            var value = (byte)State.D[source];
            WriteByte(CalculateIndexedOperandAddress(State.A[baseRegister], extension, opcode), value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteIndexedRegisterStoreTiming(M68kInstructionTimingKey.MoveByteDataToBriefIndexed, extension);
        }

        private void ExecuteMoveByteBriefIndexedToPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceBase = opcode & 7;
            var destination = (opcode >> 9) & 7;
            var extension = FetchWord();
            var value = ReadByte(CalculateBriefIndexedAddress(sourceBase, extension, opcode));
            var address = State.A[destination] - (destination == 7 ? 2u : 1u);
            WriteGeneralRegister(true, destination, address);
            WriteByte(address, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteBriefIndexedToPredecrement);
        }

        private void ExecuteMoveByteDataToPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var address = State.A[destination];
            var value = (byte)State.D[source];
            WriteByte(address, value);
            WriteGeneralRegister(true, destination, address + (destination == 7 ? 2u : 1u));
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteDataToPostIncrement);
        }

        private void ExecuteMoveByteDataToPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var address = State.A[destination] - (destination == 7 ? 2u : 1u);
            var value = (byte)State.D[source];
            WriteGeneralRegister(true, destination, address);
            WriteByte(address, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteDataToPredecrement);
        }

        private void ExecuteMoveBytePostIncrementToPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = opcode & 7;
            var destination = (opcode >> 9) & 7;
            var sourceAddress = State.A[source];
            var value = ReadByte(sourceAddress);
            WriteGeneralRegister(true, source, sourceAddress + (source == 7 ? 2u : 1u));
            var destinationAddress = State.A[destination];
            WriteByte(destinationAddress, value);
            WriteGeneralRegister(true, destination, destinationAddress + (destination == 7 ? 2u : 1u));
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveBytePostIncrementToPostIncrement);
        }

        private void ExecuteMoveByteAddressIndirectToAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = opcode & 7;
            var address = FetchLong();
            var value = ReadByte(State.A[source]);
            WriteByte(address, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteAddressIndirectToAbsoluteLong);
        }

        private void ExecuteMoveByteAbsoluteLongToAbsoluteLong()
        {
            BeginInstruction(0x13F9);
            _ = FetchWord();
            var sourceAddress = FetchLong();
            var destinationAddress = FetchLong();
            var value = ReadByte(sourceAddress);
            WriteByte(destinationAddress, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteAbsoluteLongToAbsoluteLong);
        }

        private void ExecuteImmediateLogicalToAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = ((opcode >> 6) & 3) switch { 0 => M68kOperandSize.Byte, 1 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var immediate = size == M68kOperandSize.Long ? FetchLong() : FetchWord();
            var address = FetchLong();
            var destination = ReadSized(address, size);
            var result = (opcode & 0x0200) == 0 ? destination | immediate : destination & immediate;
            WriteSized(address, result, size);
            State.SetNegativeZero(result, size);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.ImmediateLogicalByteToAbsoluteLong,
                M68kOperandSize.Word => M68kInstructionTimingKey.ImmediateLogicalWordToAbsoluteLong,
                _ => M68kInstructionTimingKey.ImmediateLogicalLongToAbsoluteLong
            });
        }

        private void ExecuteOriWordImmediateToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var immediate = FetchWord();
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[opcode & 7] + displacement));
            var result = (ushort)(ReadWord(address) | immediate);
            WriteWord(address, result);
            State.SetNegativeZero(result, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.OriWordImmediateToAddressDisplacement);
        }

        private void ExecuteOriLongImmediateToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var immediate = FetchLong();
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[opcode & 7] + displacement));
            var result = ReadLong(address) | immediate;
            WriteLong(address, result);
            State.SetNegativeZero(result, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.OriLongImmediateToAddressDisplacement);
        }

        private void ExecuteOriWordImmediateToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var immediate = FetchWord();
            var address = State.A[opcode & 7];
            var result = (ushort)(ReadWord(address) | immediate);
            WriteWord(address, result);
            SetMoveFlags(result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.OriWordImmediateToAddressIndirect);
        }

        private void ExecuteAddiWordImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var source = FetchWord();
            var destination = State.D[register] & 0xFFFF;
            var result = (destination + source) & 0xFFFF;
            WriteDataRegisterWord(register, (ushort)result);
            SetAddFlags(destination, source, result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.AddiWordImmediateToData);
        }

        private void ExecuteAddiByteImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var source = (byte)FetchWord();
            var destination = State.D[register] & 0xFF;
            var result = (byte)(destination + source);
            WriteDataRegisterByte(register, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.AddiByteImmediateToData);
        }

        private void ExecuteSubiByteImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var source = (byte)FetchWord();
            var destination = State.D[register] & 0xFF;
            var result = (byte)(destination - source);
            WriteDataRegisterByte(register, result);
            SetSubtractFlags(destination, source, result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.SubiByteImmediateToData);
        }

        private void ExecuteSubiWordImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var source = FetchWord();
            var destination = State.D[register] & 0xFFFF;
            var result = (ushort)(destination - source);
            WriteDataRegisterWord(register, result);
            SetSubtractFlags(destination, source, result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.SubiWordImmediateToData);
        }

        private void ExecuteSubiLongImmediateToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = FetchLong();
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[opcode & 7] + displacement));
            var destination = ReadLong(address);
            var result = unchecked(destination - source);
            WriteLong(address, result);
            SetSubtractFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.SubiLongImmediateToAddressDisplacement);
        }

        private void ExecuteSubiWordImmediateToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var source = (ushort)FetchWord();
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[register] + displacement));
            var destination = ReadWord(address);
            var result = (ushort)(destination - source);
            WriteWord(address, result);
            SetSubtractFlags(destination, source, result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.SubiWordImmediateToAddressDisplacement);
        }

        private void ExecuteSubiByteImmediateToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var source = (byte)FetchWord();
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[register] + displacement));
            var destination = ReadByte(address);
            var result = (byte)(destination - source);
            WriteByte(address, result);
            SetSubtractFlags(destination, source, result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.SubiByteImmediateToAddressDisplacement);
        }

        private void ExecuteAddiImmediateToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var addressRegister = opcode & 7;
            var size = ((opcode >> 6) & 3) switch
            {
                0 => M68kOperandSize.Byte,
                1 => M68kOperandSize.Word,
                _ => M68kOperandSize.Long
            };
            var source = size == M68kOperandSize.Long ? FetchLong() : FetchWord();
            source &= M68kCpuState.Mask(size);
            var address = State.A[addressRegister];
            var destination = ReadSized(address, size);
            var result = unchecked(destination + source) & M68kCpuState.Mask(size);
            WriteSized(address, result, size);
            SetAddFlags(destination, source, result, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.AddiByteImmediateToAddressIndirect,
                M68kOperandSize.Word => M68kInstructionTimingKey.AddiWordImmediateToAddressIndirect,
                _ => M68kInstructionTimingKey.AddiLongImmediateToAddressIndirect
            });
        }

        private void ExecuteAddiWordImmediateToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var addressRegister = opcode & 7;
            var source = (ushort)FetchWord();
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[addressRegister] + displacement));
            var destination = ReadWord(address);
            var result = (ushort)(destination + source);
            WriteWord(address, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.AddiWordImmediateToAddressDisplacement);
        }

        private void ExecuteAddiByteImmediateToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var addressRegister = opcode & 7;
            var source = (byte)FetchWord();
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[addressRegister] + displacement));
            var destination = ReadByte(address);
            var result = (byte)(destination + source);
            WriteByte(address, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.AddiByteImmediateToAddressDisplacement);
        }

        private void ExecuteAddiLongImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var source = FetchLong();
            var destination = State.D[register];
            var result = destination + source;
            State.D[register] = result;
            SetAddFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.AddiLongImmediateToData);
        }

        private void ExecuteAddiLongImmediateToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = FetchLong();
            var addressRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[addressRegister] + displacement));
            var destination = ReadLong(address);
            var result = destination + source;
            WriteLong(address, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.AddiLongImmediateToAddressDisplacement);
        }

        private void ExecuteAddiLongImmediateToAbsoluteLong()
        {
            BeginInstruction(0x06B9);
            _ = FetchWord();
            var source = FetchLong();
            var address = FetchLong();
            var destination = ReadLong(address);
            var result = destination + source;
            WriteLong(address, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.AddiLongImmediateToAbsoluteLong);
        }

        private void ExecuteMovePcDisplacementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var pc = State.ProgramCounter;
            var displacement = unchecked((short)FetchWord());
            var word = (opcode & 0xF000) == 0x3000;
            var size = word ? M68kOperandSize.Word : M68kOperandSize.Byte;
            var value = ReadSized(unchecked(pc + (uint)displacement), size);
            var mask = word ? 0xFFFFu : 0xFFu;
            var register = (opcode >> 9) & 7;
            State.D[register] = (State.D[register] & ~mask) | (value & mask);
            SetMoveFlags(value, size);
            CompleteTiming(word ? M68kInstructionTimingKey.MoveWordAddressDisplacementToData : M68kInstructionTimingKey.MoveByteAddressDisplacementToData);
        }

        private void ExecuteSubImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var word = (opcode & 0x40) != 0;
            var size = word ? M68kOperandSize.Word : M68kOperandSize.Byte;
            var mask = word ? 0xFFFFu : 0xFFu;
            var source = FetchWord() & mask;
            var destination = State.D[register] & mask;
            var result = unchecked(destination - source) & mask;
            State.D[register] = (State.D[register] & ~mask) | result;
            SetSubtractFlags(destination, source, result, size);
            CompleteTiming(word ? M68kInstructionTimingKey.SubiWordImmediateToData : M68kInstructionTimingKey.SubiByteImmediateToData);
        }

        private void ExecuteSubiLongImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var source = FetchLong();
            var destination = State.D[register];
            var result = destination - source;
            State.D[register] = result;
            SetSubtractFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.SubiLongImmediateToData);
        }

        private void ExecuteSubLongDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var destination = State.D[destinationRegister];
            var source = State.D[sourceRegister];
            var result = destination - source;
            State.D[destinationRegister] = result;
            SetSubtractFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.SubLongDataToData);
        }

        private void ExecuteSubByteDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var destination = State.D[destinationRegister] & 0xFF;
            var source = State.D[sourceRegister] & 0xFF;
            var result = (byte)(destination - source);
            WriteDataRegisterByte(destinationRegister, result);
            SetSubtractFlags(destination, source, result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.SubByteDataToData);
        }

        private void ExecuteSubLongAddressToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var destination = State.D[destinationRegister];
            var source = State.A[sourceRegister];
            var result = destination - source;
            State.D[destinationRegister] = result;
            SetSubtractFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.SubLongAddressToData);
        }

        private void ExecuteSubLongAddressDisplacementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var destination = State.D[destinationRegister];
            var source = ReadLong(unchecked((uint)(State.A[sourceRegister] + displacement)));
            var result = destination - source;
            State.D[destinationRegister] = result;
            SetSubtractFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.SubLongAddressDisplacementToData);
        }

        private void ExecuteSubLongImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var destination = State.D[destinationRegister];
            var source = FetchLong();
            var result = destination - source;
            State.D[destinationRegister] = result;
            SetSubtractFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.SubLongImmediateToData);
        }

        private void ExecuteSubLongDataToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = (opcode >> 9) & 7;
            var destinationRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[destinationRegister] + displacement));
            var destination = ReadLong(address);
            var source = State.D[sourceRegister];
            var result = destination - source;
            WriteLong(address, result);
            SetSubtractFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.SubLongDataToAddressDisplacement);
        }

        private void ExecuteAddLongDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var destination = State.D[destinationRegister];
            var source = State.D[sourceRegister];
            var result = destination + source;
            State.D[destinationRegister] = result;
            SetAddFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.AddLongDataToData);
        }

        private void ExecuteAddqLongData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = opcode & 7;
            var source = (uint)((opcode >> 9) & 7);
            if (source == 0)
            {
                source = 8;
            }

            var destination = State.D[destinationRegister];
            var result = destination + source;
            State.D[destinationRegister] = result;
            SetAddFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.AddLongDataToData);
        }

        private void ExecuteAddSmallDataToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var word = (opcode & 0x40) != 0;
            var size = word ? M68kOperandSize.Word : M68kOperandSize.Byte;
            var mask = word ? 0xFFFFu : 0xFFu;
            var source = State.D[(opcode >> 9) & 7] & mask;
            var address = State.A[opcode & 7];
            var destination = ReadSized(address, size);
            var result = unchecked(destination + source) & mask;
            if (word) WriteWord(address, (ushort)result); else WriteByte(address, (byte)result);
            SetAddFlags(destination, source, result, size);
            CompleteTiming(word ? M68kInstructionTimingKey.AddWordDataToAddressIndirect : M68kInstructionTimingKey.AddByteDataToAddressIndirect);
        }

        private void ExecuteAddLongDataToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = State.D[(opcode >> 9) & 7];
            var address = State.A[opcode & 7];
            var destination = ReadLong(address);
            var result = unchecked(destination + source);
            WriteLong(address, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.AddLongDataToAddressIndirect);
        }

        private void ExecuteArithmeticDataToAbsoluteLong(ushort opcode, bool subtract)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = ((opcode >> 6) & 3) switch { 0 => M68kOperandSize.Byte, 1 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var source = State.D[(opcode >> 9) & 7] & M68kCpuState.Mask(size);
            var address = FetchLong();
            var destination = ReadSized(address, size);
            var result = subtract ? unchecked(destination - source) : unchecked(destination + source);
            WriteSized(address, result, size);
            if (subtract) SetSubtractFlags(destination, source, result, size);
            else SetAddFlags(destination, source, result, size);
            CompleteTiming(subtract ? size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.SubByteDataToAbsoluteLong,
                M68kOperandSize.Word => M68kInstructionTimingKey.SubWordDataToAbsoluteLong,
                _ => M68kInstructionTimingKey.SubLongDataToAbsoluteLong
            } : size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.AddByteDataToAbsoluteLong,
                M68kOperandSize.Word => M68kInstructionTimingKey.AddWordDataToAbsoluteLong,
                _ => M68kInstructionTimingKey.AddLongDataToAbsoluteLong
            });
        }

        private void ExecuteAddLongAddressIndirectToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceAddress = State.A[opcode & 7];
            var destination = State.D[destinationRegister];
            var source = ReadLong(sourceAddress);
            var result = unchecked(destination + source);
            State.D[destinationRegister] = result;
            SetAddFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.AddLongAddressIndirectToData);
        }

        private void ExecuteAddByteOrWordBriefIndexedToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = (opcode & 0x40) == 0 ? M68kOperandSize.Byte : M68kOperandSize.Word;
            var register = (opcode >> 9) & 7;
            var extension = FetchWord();
            var source = ReadSized(CalculateBriefIndexedAddress(opcode & 7, extension, opcode), size);
            var destination = size == M68kOperandSize.Byte ? (byte)State.D[register] : (ushort)State.D[register];
            var result = destination + source;
            WriteDataRegisterSized(register, result, size);
            SetAddFlags(destination, source, result, size);
            CompleteTiming(size == M68kOperandSize.Byte
                ? M68kInstructionTimingKey.AddByteBriefIndexedToData
                : M68kInstructionTimingKey.AddWordBriefIndexedToData);
        }

        private void ExecuteAddLongBriefIndexedToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var addressRegister = opcode & 7;
            var extension = FetchWord();
            var destination = State.D[destinationRegister];
            var source = ReadLong(CalculateBriefIndexedAddress(addressRegister, extension, opcode));
            var result = unchecked(destination + source);
            State.D[destinationRegister] = result;
            SetAddFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.AddLongBriefIndexedToData);
        }

        private void ExecuteAddLongPcDisplacementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var extensionAddress = State.ProgramCounter;
            var displacement = unchecked((short)FetchWord());
            var source = ReadLong(unchecked((uint)(extensionAddress + displacement)));
            var destination = State.D[register];
            var result = unchecked(destination + source);
            State.D[register] = result;
            SetAddFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.AddLongPcDisplacementToData);
        }

        private void ExecuteAddLongAbsoluteLongToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var destination = State.D[destinationRegister];
            var source = ReadLong(FetchLong());
            var result = unchecked(destination + source);
            State.D[destinationRegister] = result;
            SetAddFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.AddLongAbsoluteLongToData);
        }

        private void ExecuteAddqLongAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = (uint)((opcode >> 9) & 7);
            if (source == 0)
            {
                source = 8;
            }

            var address = FetchLong();
            var destination = ReadLong(address);
            var result = unchecked(destination + source);
            WriteLong(address, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.AddqLongAbsoluteLong);
        }

        private void ExecuteAddqWordData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = opcode & 7;
            var source = (ushort)((opcode >> 9) & 7);
            if (source == 0)
            {
                source = 8;
            }

            var destination = (ushort)State.D[destinationRegister];
            var result = (ushort)(destination + source);
            WriteDataRegisterWord(destinationRegister, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.AddWordDataToData);
        }

        private void ExecuteAddqWordAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = opcode & 7;
            var source = (uint)((opcode >> 9) & 7);
            if (source == 0)
            {
                source = 8;
            }

            State.A[destinationRegister] = unchecked(State.A[destinationRegister] + source);
            CompleteTiming(M68kInstructionTimingKey.AddqWordAddress);
        }

        private void ExecuteAddLongAddressToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var destination = State.D[destinationRegister];
            var source = State.A[sourceRegister];
            var result = unchecked(destination + source);
            State.D[destinationRegister] = result;
            SetAddFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.AddLongAddressToData);
        }

        private void ExecuteAddqLongAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = opcode & 7;
            var source = (uint)((opcode >> 9) & 7);
            if (source == 0)
            {
                source = 8;
            }

            State.A[destinationRegister] = unchecked(State.A[destinationRegister] + source);
            CompleteTiming(M68kInstructionTimingKey.AddqLongAddress);
        }

        private void ExecuteAddqLongAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = (uint)((opcode >> 9) & 7);
            if (source == 0)
            {
                source = 8;
            }

            var address = State.A[opcode & 7];
            var destination = ReadLong(address);
            var result = unchecked(destination + source);
            WriteLong(address, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.AddqLongAddressIndirect);
        }

        private void ExecuteAddqByteAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var addressRegister = opcode & 7;
            var source = (byte)((opcode >> 9) & 7);
            if (source == 0)
            {
                source = 8;
            }

            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[addressRegister] + displacement));
            var destination = ReadByte(address);
            var result = (byte)(destination + source);
            WriteByte(address, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.AddqByteAddressDisplacement);
        }

        private void ExecuteAddqByteAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = (byte)((opcode >> 9) & 7);
            if (source == 0)
            {
                source = 8;
            }

            var address = State.A[opcode & 7];
            var destination = ReadByte(address);
            var result = (byte)(destination + source);
            WriteByte(address, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.AddqByteAddressIndirect);
        }

        private void ExecuteAddqByteData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = opcode & 7;
            var source = (byte)((opcode >> 9) & 7);
            if (source == 0)
            {
                source = 8;
            }

            var destination = (byte)State.D[destinationRegister];
            var result = (byte)(destination + source);
            WriteDataRegisterByte(destinationRegister, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.AddByteDataToData);
        }

        private void ExecuteQuickWordBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = (uint)((opcode >> 9) & 7); if (source == 0) source = 8;
            var extension = FetchWord();
            var address = CalculateBriefIndexedAddress(opcode & 7, extension, opcode);
            var destination = ReadWord(address);
            var subtract = (opcode & 0x100) != 0;
            var result = unchecked((ushort)(subtract ? destination - source : destination + source));
            WriteWord(address, result);
            if (subtract) SetSubtractFlags(destination, source, result, M68kOperandSize.Word);
            else SetAddFlags(destination, source, result, M68kOperandSize.Word);
            CompleteTiming(subtract ? M68kInstructionTimingKey.SubqWordBriefIndexed : M68kInstructionTimingKey.AddqWordBriefIndexed);
        }

        private void ExecuteAddqWordAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = (ushort)((opcode >> 9) & 7);
            if (source == 0)
            {
                source = 8;
            }

            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[opcode & 7] + displacement));
            var destination = ReadWord(address);
            var result = (ushort)(destination + source);
            WriteWord(address, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.AddqWordAddressDisplacement);
        }

        private void ExecuteAddqLongAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = (uint)((opcode >> 9) & 7);
            if (source == 0)
            {
                source = 8;
            }

            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[opcode & 7] + displacement));
            var destination = ReadLong(address);
            var result = unchecked(destination + source);
            WriteLong(address, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.AddqLongAddressDisplacement);
        }

        private void ExecuteSubqLongData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = opcode & 7;
            var source = (uint)((opcode >> 9) & 7);
            if (source == 0)
            {
                source = 8;
            }

            var destination = State.D[destinationRegister];
            var result = destination - source;
            State.D[destinationRegister] = result;
            SetSubtractFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.SubLongDataToData);
        }

        private void ExecuteSubqByteData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = opcode & 7;
            var source = (byte)((opcode >> 9) & 7);
            if (source == 0)
            {
                source = 8;
            }

            var destination = (byte)State.D[destinationRegister];
            var result = (byte)(destination - source);
            WriteDataRegisterByte(destinationRegister, result);
            SetSubtractFlags(destination, source, result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.SubByteDataToData);
        }

        private void ExecuteSubByteAddressDisplacementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var source = ReadByte(unchecked((uint)(State.A[sourceRegister] + displacement)));
            var destination = (byte)State.D[destinationRegister];
            var result = (byte)(destination - source);
            WriteDataRegisterByte(destinationRegister, result);
            SetSubtractFlags(destination, source, result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.SubByteAddressDisplacementToData);
        }

        private void ExecuteSubWordDataToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = (opcode >> 9) & 7;
            var addressRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[addressRegister] + displacement));
            var destination = ReadWord(address);
            var source = (ushort)State.D[sourceRegister];
            var result = (ushort)(destination - source);
            WriteWord(address, result);
            SetSubtractFlags(destination, source, result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.SubWordDataToAddressDisplacement);
        }

        private void ExecuteSubByteDataToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = (opcode >> 9) & 7;
            var addressRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[addressRegister] + displacement));
            var destination = ReadByte(address);
            var source = (byte)State.D[sourceRegister];
            var result = (byte)(destination - source);
            WriteByte(address, result);
            SetSubtractFlags(destination, source, result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.SubByteDataToAddressDisplacement);
        }

        private void ExecuteSubWordDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var source = (ushort)State.D[opcode & 7];
            var destination = (ushort)State.D[destinationRegister];
            var result = (ushort)(destination - source);
            WriteDataRegisterWord(destinationRegister, result);
            SetSubtractFlags(destination, source, result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.SubWordDataToData);
        }

        private void ExecuteSubIndirectToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = (opcode & 0x80) != 0 ? M68kOperandSize.Long : M68kOperandSize.Byte;
            var register = (opcode >> 9) & 7;
            var source = ReadSized(State.A[opcode & 7], size);
            var destination = size == M68kOperandSize.Byte ? (byte)State.D[register] : State.D[register];
            var result = unchecked(destination - source);
            WriteDataRegisterSized(register, result, size);
            SetSubtractFlags(destination, source, result, size);
            CompleteTiming(size == M68kOperandSize.Byte ? M68kInstructionTimingKey.SubByteAddressIndirectToData : M68kInstructionTimingKey.SubLongAddressIndirectToData);
        }

        private void ExecuteCmpaAbsoluteLongToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var address = FetchLong();
            var word = (opcode & 0x0100) == 0;
            var source = word ? unchecked((uint)(int)(short)ReadWord(address)) : ReadLong(address);
            SetCompareFlags(State.A[(opcode >> 9) & 7], source, M68kOperandSize.Long);
            CompleteTiming(word ? M68kInstructionTimingKey.CmpaWordAbsoluteLongToAddress
                : M68kInstructionTimingKey.CmpaLongAbsoluteLongToAddress);
        }

        private void ExecuteNegAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = ((opcode >> 6) & 3) switch { 0 => M68kOperandSize.Byte, 1 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var address = FetchLong();
            var source = ReadSized(address, size);
            var result = unchecked(0u - source);
            WriteSized(address, result, size);
            SetSubtractFlags(0, source, result, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.NegByteAbsoluteLong,
                M68kOperandSize.Word => M68kInstructionTimingKey.NegWordAbsoluteLong,
                _ => M68kInstructionTimingKey.NegLongAbsoluteLong
            });
        }

        private void ExecuteNegAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = ((opcode >> 6) & 3) switch { 0 => M68kOperandSize.Byte, 1 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[opcode & 7] + displacement));
            var source = ReadSized(address, size);
            var result = unchecked(0u - source);
            WriteSized(address, result, size);
            SetSubtractFlags(0, source, result, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.NegByteAddressDisplacement,
                M68kOperandSize.Word => M68kInstructionTimingKey.NegWordAddressDisplacement,
                _ => M68kInstructionTimingKey.NegLongAddressDisplacement
            });
        }

        private void ExecuteCmpBriefIndexedToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = ((opcode >> 6) & 3) switch { 0 => M68kOperandSize.Byte, 1 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var extension = FetchWord();
            var source = ReadSized(CalculateBriefIndexedAddress(opcode & 7, extension, opcode), size);
            SetCompareFlags(State.D[(opcode >> 9) & 7], source, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.CmpByteBriefIndexedToData,
                M68kOperandSize.Word => M68kInstructionTimingKey.CmpWordBriefIndexedToData,
                _ => M68kInstructionTimingKey.CmpLongBriefIndexedToData
            });
        }

        private void ExecuteSubBriefIndexedToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = ((opcode >> 6) & 3) switch { 0 => M68kOperandSize.Byte, 1 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var register = (opcode >> 9) & 7;
            var extension = FetchWord();
            var source = ReadSized(CalculateBriefIndexedAddress(opcode & 7, extension, opcode), size);
            var destination = size switch { M68kOperandSize.Byte => (byte)State.D[register], M68kOperandSize.Word => (ushort)State.D[register], _ => State.D[register] };
            var result = unchecked(destination - source);
            WriteDataRegisterSized(register, result, size);
            SetSubtractFlags(destination, source, result, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.SubByteBriefIndexedToData,
                M68kOperandSize.Word => M68kInstructionTimingKey.SubWordBriefIndexedToData,
                _ => M68kInstructionTimingKey.SubLongBriefIndexedToData
            });
        }

        private void ExecuteSubDataToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = ((opcode >> 6) & 3) switch { 0 => M68kOperandSize.Byte, 1 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var address = State.A[opcode & 7];
            var destination = ReadSized(address, size);
            var rawSource = State.D[(opcode >> 9) & 7];
            var source = size switch { M68kOperandSize.Byte => (byte)rawSource, M68kOperandSize.Word => (ushort)rawSource, _ => rawSource };
            var result = unchecked(destination - source);
            WriteSized(address, result, size);
            SetSubtractFlags(destination, source, result, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.SubByteDataToAddressIndirect,
                M68kOperandSize.Word => M68kInstructionTimingKey.SubWordDataToAddressIndirect,
                _ => M68kInstructionTimingKey.SubLongDataToAddressIndirect
            });
        }

        private void ExecuteSubPostIncrementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = ((opcode >> 6) & 3) switch { 0 => M68kOperandSize.Byte, 1 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var addressRegister = opcode & 7;
            var address = State.A[addressRegister];
            var source = ReadSized(address, size);
            WriteGeneralRegister(true, addressRegister, unchecked(address + M68kIntegerSemantics.AddressIncrement(addressRegister, size)));
            var register = (opcode >> 9) & 7;
            var destination = size switch { M68kOperandSize.Byte => (byte)State.D[register], M68kOperandSize.Word => (ushort)State.D[register], _ => State.D[register] };
            var result = unchecked(destination - source);
            WriteDataRegisterSized(register, result, size);
            SetSubtractFlags(destination, source, result, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.SubBytePostIncrementToData,
                M68kOperandSize.Word => M68kInstructionTimingKey.SubWordPostIncrementToData,
                _ => M68kInstructionTimingKey.SubLongPostIncrementToData
            });
        }

        private void ExecuteSubWordAddressIndirectToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var source = ReadWord(State.A[opcode & 7]);
            var destination = (ushort)State.D[destinationRegister];
            var result = (ushort)(destination - source);
            WriteDataRegisterWord(destinationRegister, result);
            SetSubtractFlags(destination, source, result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.SubWordAddressIndirectToData);
        }

        private void ExecuteSubWordAddressDisplacementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var source = ReadWord(unchecked((uint)(State.A[sourceRegister] + displacement)));
            var destination = (ushort)State.D[destinationRegister];
            var result = (ushort)(destination - source);
            WriteDataRegisterWord(destinationRegister, result);
            SetSubtractFlags(destination, source, result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.SubWordAddressDisplacementToData);
        }

        private void ExecuteSubWordDataToPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = (opcode >> 9) & 7;
            var addressRegister = opcode & 7;
            var address = State.A[addressRegister];
            var destination = ReadWord(address);
            var source = (ushort)State.D[sourceRegister];
            var result = (ushort)(destination - source);
            WriteWord(address, result);
            WriteGeneralRegister(true, addressRegister, address + 2u);
            SetSubtractFlags(destination, source, result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.SubWordDataToPostIncrement);
        }

        private void ExecuteSubqWordData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = opcode & 7;
            var source = (ushort)((opcode >> 9) & 7);
            if (source == 0)
            {
                source = 8;
            }

            var destination = (ushort)State.D[destinationRegister];
            var result = (ushort)(destination - source);
            WriteDataRegisterWord(destinationRegister, result);
            SetSubtractFlags(destination, source, result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.SubWordDataToData);
        }

        private void ExecuteSubqWordAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var immediate = (uint)((opcode >> 9) & 7);
            if (immediate == 0) immediate = 8;
            // Address-register quick arithmetic is always 32-bit and preserves CCR.
            WriteGeneralRegister(true, register, unchecked(State.A[register] - immediate));
            CompleteTiming(M68kInstructionTimingKey.SubqWordAddress);
        }

        private void ExecuteSubqLongAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = opcode & 7;
            var source = (uint)((opcode >> 9) & 7);
            if (source == 0)
            {
                source = 8;
            }

            State.A[destinationRegister] = unchecked(State.A[destinationRegister] - source);
            CompleteTiming(M68kInstructionTimingKey.SubqLongAddress);
        }

        private void ExecuteQuickIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = (opcode & 0x40) == 0 ? M68kOperandSize.Byte : M68kOperandSize.Word;
            var subtract = (opcode & 0x100) != 0;
            var source = (uint)((opcode >> 9) & 7);
            if (source == 0) source = 8;
            var address = State.A[opcode & 7];
            var destination = ReadSized(address, size);
            var result = subtract ? unchecked(destination - source) : unchecked(destination + source);
            WriteSized(address, result, size);
            if (subtract) SetSubtractFlags(destination, source, result, size);
            else SetAddFlags(destination, source, result, size);
            CompleteTiming(!subtract ? M68kInstructionTimingKey.AddqWordAddressIndirect : size == M68kOperandSize.Byte ? M68kInstructionTimingKey.SubqByteAddressIndirect : M68kInstructionTimingKey.SubqWordAddressIndirect);
        }

        private void ExecuteEoriImmediateToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = ((opcode >> 6) & 3) switch { 0 => M68kOperandSize.Byte, 1 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var immediate = size == M68kOperandSize.Long ? FetchLong() : FetchWord();
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[opcode & 7] + displacement));
            var result = ReadSized(address, size) ^ immediate;
            WriteSized(address, result, size); SetMoveFlags(result, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.EoriByteImmediateToAddressDisplacement,
                M68kOperandSize.Word => M68kInstructionTimingKey.EoriWordImmediateToAddressDisplacement,
                _ => M68kInstructionTimingKey.EoriLongImmediateToAddressDisplacement
            });
        }

        private void ExecuteBitImmediatePostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var bit = FetchWord() & 7;
            var register = opcode & 7;
            var address = State.A[register];
            var value = ReadByte(address);
            var mask = 1 << bit;
            State.SetFlag(M68kCpuState.Zero, (value & mask) == 0);
            var operation = (opcode >> 6) & 3;
            var result = operation switch { 1 => value ^ mask, 2 => value & ~mask, _ => value | mask };
            WriteByte(address, (byte)result);
            WriteGeneralRegister(true, register, unchecked(address + M68kIntegerSemantics.AddressIncrement(register, M68kOperandSize.Byte)));
            CompleteTiming(operation switch
            {
                1 => M68kInstructionTimingKey.BchgByteImmediatePostIncrement,
                2 => M68kInstructionTimingKey.BclrByteImmediatePostIncrement,
                _ => M68kInstructionTimingKey.BsetByteImmediatePostIncrement
            });
        }

        private void ExecuteQuickPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = ((opcode >> 6) & 3) switch { 0 => M68kOperandSize.Byte, 1 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var subtract = (opcode & 0x100) != 0;
            var source = (uint)((opcode >> 9) & 7);
            if (source == 0) source = 8;
            var register = opcode & 7;
            var address = State.A[register];
            var destination = ReadSized(address, size);
            var result = subtract ? unchecked(destination - source) : unchecked(destination + source);
            WriteSized(address, result, size);
            WriteGeneralRegister(true, register, unchecked(address + M68kIntegerSemantics.AddressIncrement(register, size)));
            if (subtract) SetSubtractFlags(destination, source, result, size);
            else SetAddFlags(destination, source, result, size);
            CompleteTiming(subtract ? size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.SubqBytePostIncrement,
                M68kOperandSize.Word => M68kInstructionTimingKey.SubqWordPostIncrement,
                _ => M68kInstructionTimingKey.SubqLongPostIncrement
            } : size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.AddqBytePostIncrement,
                M68kOperandSize.Word => M68kInstructionTimingKey.AddqWordPostIncrement,
                _ => M68kInstructionTimingKey.AddqLongPostIncrement
            });
        }

        private void ExecuteSubqAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = ((opcode >> 6) & 3) switch { 0 => M68kOperandSize.Byte, 1 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var source = (uint)((opcode >> 9) & 7);
            if (source == 0) source = 8;
            var address = FetchLong();
            var destination = ReadSized(address, size);
            var result = unchecked(destination - source);
            WriteSized(address, result, size);
            SetSubtractFlags(destination, source, result, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.SubqByteAbsoluteLong,
                M68kOperandSize.Word => M68kInstructionTimingKey.SubqWordAbsoluteLong,
                _ => M68kInstructionTimingKey.SubqLongAbsoluteLong
            });
        }

        private void ExecuteSubqLongAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = (uint)((opcode >> 9) & 7);
            if (source == 0)
            {
                source = 8;
            }

            var address = State.A[opcode & 7];
            var destination = ReadLong(address);
            var result = unchecked(destination - source);
            WriteLong(address, result);
            SetSubtractFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.SubqLongAddressIndirect);
        }

        private void ExecuteSubqLongAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = (uint)((opcode >> 9) & 7);
            if (source == 0)
            {
                source = 8;
            }

            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[opcode & 7] + displacement));
            var destination = ReadLong(address);
            var result = unchecked(destination - source);
            WriteLong(address, result);
            SetSubtractFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.SubqLongAddressDisplacement);
        }

        private void ExecuteSubqByteAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var addressRegister = opcode & 7;
            var source = (byte)((opcode >> 9) & 7);
            if (source == 0)
            {
                source = 8;
            }

            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[addressRegister] + displacement));
            var destination = ReadByte(address);
            var result = (byte)(destination - source);
            WriteByte(address, result);
            SetSubtractFlags(destination, source, result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.SubqByteAddressDisplacement);
        }

        private void ExecuteSubqWordAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = (ushort)((opcode >> 9) & 7);
            if (source == 0)
            {
                source = 8;
            }

            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[opcode & 7] + displacement));
            var destination = ReadWord(address);
            var result = (ushort)(destination - source);
            WriteWord(address, result);
            SetSubtractFlags(destination, source, result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.SubqWordAddressDisplacement);
        }

        private void ExecuteAddWordDataToData(ushort opcode, bool addressSource = false)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var destination = State.D[destinationRegister] & 0xFFFF;
            var source = (addressSource ? State.A[sourceRegister] : State.D[sourceRegister]) & 0xFFFF;
            var result = (ushort)(destination + source);
            WriteDataRegisterWord(destinationRegister, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.AddWordDataToData);
        }

        private void ExecuteAddByteAddressIndirectToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var source = ReadByte(State.A[opcode & 7]);
            var destination = State.D[register] & 0xFF;
            var result = destination + source;
            WriteDataRegisterByte(register, (byte)result);
            SetAddFlags(destination, source, result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.AddByteAddressIndirectToData);
        }

        private void ExecuteAddWordAddressIndirectToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var source = ReadWord(State.A[opcode & 7]);
            var destination = (ushort)State.D[destinationRegister];
            var result = (ushort)(destination + source);
            WriteDataRegisterWord(destinationRegister, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.AddWordAddressIndirectToData);
        }

        private void ExecuteAddByteDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var destination = (byte)State.D[destinationRegister];
            var source = (byte)State.D[sourceRegister];
            var result = (byte)(destination + source);
            WriteDataRegisterByte(destinationRegister, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.AddByteDataToData);
        }

        private void ExecuteAddWordPredecrementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var sourceAddress = unchecked(State.A[sourceRegister] - 2u);
            WriteGeneralRegister(true, sourceRegister, sourceAddress);
            var source = ReadWord(sourceAddress);
            var destination = (ushort)State.D[destinationRegister];
            var result = (ushort)(destination + source);
            WriteDataRegisterWord(destinationRegister, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.AddWordPredecrementToData);
        }

        private void ExecuteAddWordPostIncrementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var sourceAddress = State.A[sourceRegister];
            var source = ReadWord(sourceAddress);
            WriteGeneralRegister(true, sourceRegister, sourceAddress + 2);
            var destination = (ushort)State.D[destinationRegister];
            var result = (ushort)(destination + source);
            WriteDataRegisterWord(destinationRegister, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.AddWordPostIncrementToData);
        }

        private void ExecuteAddByteAddressDisplacementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var source = ReadByte(unchecked((uint)(State.A[opcode & 7] + displacement)));
            var destination = State.D[destinationRegister] & 0xFF;
            var result = destination + source;
            WriteDataRegisterByte(destinationRegister, (byte)result);
            SetAddFlags(destination, source, result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.AddByteAddressDisplacementToData);
        }

        private void ExecuteAddWordAddressDisplacementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var source = ReadWord(unchecked((uint)(State.A[sourceRegister] + displacement)));
            var destination = (ushort)State.D[destinationRegister];
            var result = (ushort)(destination + source);
            WriteDataRegisterWord(destinationRegister, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.AddWordAddressDisplacementToData);
        }

        private void ExecuteAddWordImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var source = FetchWord();
            var destination = (ushort)State.D[destinationRegister];
            var result = (ushort)(destination + source);
            WriteDataRegisterWord(destinationRegister, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.AddWordImmediateToData);
        }

        private void ExecuteAddByteImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var source = (byte)FetchWord();
            var destination = (byte)State.D[destinationRegister];
            var result = (byte)(destination + source);
            WriteDataRegisterByte(destinationRegister, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.AddByteImmediateToData);
        }

        private void ExecuteAddxLongDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var destination = State.D[destinationRegister];
            var source = State.D[sourceRegister];
            var extend = State.GetFlag(M68kCpuState.Extend) ? 1u : 0u;
            var result = unchecked(destination + source + extend);
            State.D[destinationRegister] = result;
            SetAddxFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.AddxLongDataToData);
        }

        private void ExecuteSubxDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var size = ((opcode >> 6) & 3) switch
            {
                0 => M68kOperandSize.Byte,
                1 => M68kOperandSize.Word,
                _ => M68kOperandSize.Long
            };
            var mask = M68kCpuState.Mask(size);
            var destination = State.D[destinationRegister] & mask;
            var source = State.D[sourceRegister] & mask;
            var extend = State.GetFlag(M68kCpuState.Extend) ? 1u : 0u;
            var result = unchecked(destination - source - extend) & mask;
            WriteDataRegisterSized(destinationRegister, result, size);
            SetSubxFlags(destination, source, result, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.SubxByteDataToData,
                M68kOperandSize.Word => M68kInstructionTimingKey.SubxWordDataToData,
                _ => M68kInstructionTimingKey.SubxLongDataToData
            });
        }

        private void ExecuteAddxByteDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var destination = (byte)State.D[destinationRegister];
            var source = (byte)State.D[sourceRegister];
            var extend = State.GetFlag(M68kCpuState.Extend) ? 1u : 0u;
            var result = (byte)(destination + source + extend);
            WriteDataRegisterByte(destinationRegister, result);
            SetAddxFlags(destination, source, result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.AddxByteDataToData);
        }

        private void ExecuteAddxWordDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var destination = (ushort)State.D[destinationRegister];
            var source = (ushort)State.D[sourceRegister];
            var extend = State.GetFlag(M68kCpuState.Extend) ? 1u : 0u;
            var result = (ushort)(destination + source + extend);
            WriteDataRegisterWord(destinationRegister, result);
            SetAddxFlags(destination, source, result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.AddxWordDataToData);
        }

        private void ExecuteAddBytePostIncrementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var address = State.A[sourceRegister];
            var source = ReadByte(address);
            WriteGeneralRegister(true, sourceRegister, unchecked(address + M68kIntegerSemantics.AddressIncrement(sourceRegister, M68kOperandSize.Byte)));
            var destination = (byte)State.D[destinationRegister];
            var result = (uint)(destination + source);
            WriteDataRegisterByte(destinationRegister, (byte)result);
            SetAddFlags(destination, source, result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.AddBytePostIncrementToData);
        }

        private void ExecuteAddLongPostIncrementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var sourceAddress = State.A[sourceRegister];
            var destination = State.D[destinationRegister];
            var source = ReadLong(sourceAddress);
            WriteGeneralRegister(true, sourceRegister, sourceAddress + 4);
            var result = destination + source;
            State.D[destinationRegister] = result;
            SetAddFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.AddLongPostIncrementToData);
        }

        private void ExecuteAddLongAddressDisplacementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var destination = State.D[destinationRegister];
            var source = ReadLong(unchecked((uint)(State.A[sourceRegister] + displacement)));
            var result = destination + source;
            State.D[destinationRegister] = result;
            SetAddFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.AddLongAddressDisplacementToData);
        }

        private void ExecuteAddLongImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var source = FetchLong();
            var destination = State.D[destinationRegister];
            var result = unchecked(destination + source);
            State.D[destinationRegister] = result;
            SetAddFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.AddLongImmediateToData);
        }

        private void ExecuteAddByteDataToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = (opcode >> 9) & 7;
            var destinationRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[destinationRegister] + displacement));
            var destination = ReadByte(address);
            var source = State.D[sourceRegister] & 0xFF;
            var result = destination + source;
            WriteByte(address, (byte)result);
            SetAddFlags(destination, source, result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.AddByteDataToAddressDisplacement);
        }

        private void ExecuteAddWordDataToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = (opcode >> 9) & 7;
            var destinationRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[destinationRegister] + displacement));
            var destination = ReadWord(address);
            var source = State.D[sourceRegister] & 0xFFFF;
            var result = destination + source;
            WriteWord(address, (ushort)result);
            SetAddFlags(destination, source, result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.AddWordDataToAddressDisplacement);
        }

        private void ExecuteNotAbsoluteWord(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = ((opcode >> 6) & 3) switch { 0 => M68kOperandSize.Byte, 1 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var address = unchecked((uint)(int)(short)FetchWord());
            var result = ~ReadSized(address, size);
            WriteSized(address, result, size);
            SetMoveFlags(result, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.NotByteAbsoluteWord,
                M68kOperandSize.Word => M68kInstructionTimingKey.NotWordAbsoluteWord,
                _ => M68kInstructionTimingKey.NotLongAbsoluteWord
            });
        }

        private void ExecuteBitImmediateIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var mask = 1 << (FetchWord() & 7);
            var address = State.A[opcode & 7];
            var original = ReadByte(address);
            var set = (opcode & 0x40) != 0;
            WriteByte(address, (byte)(set ? original | mask : original & ~mask));
            State.SetFlag(M68kCpuState.Zero, (original & mask) == 0);
            CompleteTiming(set ? M68kInstructionTimingKey.BsetByteImmediateAddressIndirect : M68kInstructionTimingKey.BclrByteImmediateAddressIndirect);
        }

        private void ExecuteBitModifyDynamicBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var bit = (int)(State.D[(opcode >> 9) & 7] & 7);
            var mask = 1 << bit;
            var extension = FetchWord();
            var address = CalculateBriefIndexedAddress(opcode & 7, extension, opcode);
            var original = ReadByte(address);
            var operation = (opcode >> 6) & 3;
            var result = operation switch { 1 => original ^ mask, 2 => original & ~mask, _ => original | mask };
            WriteByte(address, (byte)result);
            State.SetFlag(M68kCpuState.Zero, (original & mask) == 0);
            CompleteTiming(operation switch
            {
                1 => M68kInstructionTimingKey.BchgByteDynamicBriefIndexed,
                2 => M68kInstructionTimingKey.BclrByteDynamicBriefIndexed,
                _ => M68kInstructionTimingKey.BsetByteDynamicBriefIndexed
            });
        }

        private void ExecuteBitDynamicAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var bit = (int)(State.D[(opcode >> 9) & 7] & 7);
            var mask = 1 << bit;
            var address = FetchLong();
            var original = ReadByte(address);
            var operation = (opcode >> 6) & 3;
            var result = operation switch { 1 => original ^ mask, 2 => original & ~mask, _ => original | mask };
            WriteByte(address, (byte)result);
            State.SetFlag(M68kCpuState.Zero, (original & mask) == 0);
            CompleteTiming(operation switch
            {
                1 => M68kInstructionTimingKey.BchgByteDynamicAbsoluteLong,
                2 => M68kInstructionTimingKey.BclrByteDynamicAbsoluteLong,
                _ => M68kInstructionTimingKey.BsetByteDynamicAbsoluteLong
            });
        }

        private void ExecuteCmpPcDisplacementOrAbsoluteWordToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = ((opcode >> 6) & 3) switch { 0 => M68kOperandSize.Byte, 1 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var extensionPc = State.ProgramCounter;
            var displacement = (short)FetchWord();
            var address = (opcode & 0x3F) == 0x38
                ? unchecked((uint)(int)displacement) : unchecked(extensionPc + (uint)displacement);
            var source = ReadSized(address, size);
            var destination = State.D[(opcode >> 9) & 7];
            SetCompareFlags(destination, source, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.CmpByteAddressDisplacementToData,
                M68kOperandSize.Word => M68kInstructionTimingKey.CmpWordAddressDisplacementToData,
                _ => M68kInstructionTimingKey.CmpLongAddressDisplacementToData
            });
        }

        private void ExecuteCmpiImmediateToBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = ((opcode >> 6) & 3) switch { 0 => M68kOperandSize.Byte, 1 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var source = size == M68kOperandSize.Long ? FetchLong() : FetchWord();
            if (size == M68kOperandSize.Byte) source = (byte)source;
            var extension = FetchWord();
            var destination = ReadSized(CalculateBriefIndexedAddress(opcode & 7, extension, opcode), size);
            SetCompareFlags(destination, source, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.CmpiByteImmediateToBriefIndexed,
                M68kOperandSize.Word => M68kInstructionTimingKey.CmpiWordImmediateToBriefIndexed,
                _ => M68kInstructionTimingKey.CmpiLongImmediateToBriefIndexed
            });
        }

        private void ExecuteAddDataToBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = ((opcode >> 6) & 3) switch { 0 => M68kOperandSize.Byte, 1 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var extension = FetchWord();
            var address = CalculateBriefIndexedAddress(opcode & 7, extension, opcode);
            var destination = ReadSized(address, size);
            var rawSource = State.D[(opcode >> 9) & 7];
            var source = size switch { M68kOperandSize.Byte => (byte)rawSource, M68kOperandSize.Word => (ushort)rawSource, _ => rawSource };
            var result = unchecked(destination + source);
            WriteSized(address, result, size);
            SetAddFlags(destination, source, result, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.AddByteDataToBriefIndexed,
                M68kOperandSize.Word => M68kInstructionTimingKey.AddWordDataToBriefIndexed,
                _ => M68kInstructionTimingKey.AddLongDataToBriefIndexed
            });
        }

        private void ExecuteAddDataToPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = ((opcode >> 6) & 3) switch { 0 => M68kOperandSize.Byte, 1 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var register = opcode & 7;
            var address = State.A[register];
            var destination = ReadSized(address, size);
            var rawSource = State.D[(opcode >> 9) & 7];
            var source = size switch { M68kOperandSize.Byte => (byte)rawSource, M68kOperandSize.Word => (ushort)rawSource, _ => rawSource };
            var result = unchecked(destination + source);
            WriteSized(address, result, size);
            WriteGeneralRegister(true, register, unchecked(address + M68kIntegerSemantics.AddressIncrement(register, size)));
            SetAddFlags(destination, source, result, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.AddByteDataToPostIncrement,
                M68kOperandSize.Word => M68kInstructionTimingKey.AddWordDataToPostIncrement,
                _ => M68kInstructionTimingKey.AddLongDataToPostIncrement
            });
        }

        private void ExecuteAddLongDataToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = (opcode >> 9) & 7;
            var destinationRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[destinationRegister] + displacement));
            var destination = ReadLong(address);
            var source = State.D[sourceRegister];
            var result = destination + source;
            WriteLong(address, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.AddLongDataToAddressDisplacement);
        }

        private void ExecuteAddaLongImmediateToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var source = FetchLong();
            WriteGeneralRegister(true, register, State.A[register] + source);
            CompleteTiming(M68kInstructionTimingKey.AddaLongImmediateToAddress);
        }

        private void ExecuteAddaWordImmediateToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var source = unchecked((uint)(int)(short)FetchWord());
            WriteGeneralRegister(true, register, State.A[register] + source);
            CompleteTiming(M68kInstructionTimingKey.AddaWordImmediateToAddress);
        }

        private void ExecuteAddaWordAddressToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = unchecked((short)State.A[opcode & 7]);
            WriteGeneralRegister(true, destination, unchecked((uint)(State.A[destination] + source)));
            CompleteTiming(M68kInstructionTimingKey.AddaWordAddressToAddress);
        }

        private void ExecuteAddaWordDataToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var addressRegister = (opcode >> 9) & 7;
            var dataRegister = opcode & 7;
            var source = unchecked((uint)(int)(short)State.D[dataRegister]);
            WriteGeneralRegister(true, addressRegister, State.A[addressRegister] + source);
            CompleteTiming(M68kInstructionTimingKey.AddaWordDataToAddress);
        }

        private void ExecuteAddaPcBriefIndexedToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var baseAddress = State.ProgramCounter;
            var extension = FetchWord();
            var address = CalculateBriefIndexedAddress(baseAddress, extension, opcode);
            var word = (opcode & 0x0100) == 0;
            var source = word ? unchecked((uint)(int)(short)ReadWord(address)) : ReadLong(address);
            var destination = (opcode >> 9) & 7;
            WriteGeneralRegister(true, destination, unchecked(State.A[destination] + source));
            CompleteTiming(word ? M68kInstructionTimingKey.AddaWordPcBriefIndexedToAddress
                : M68kInstructionTimingKey.AddaLongPcBriefIndexedToAddress);
        }

        private void ExecuteAddaWordAddressDisplacementToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var source = unchecked((uint)(int)(short)ReadWord(
                unchecked((uint)(State.A[sourceRegister] + displacement))));
            WriteGeneralRegister(
                true,
                destinationRegister,
                unchecked(State.A[destinationRegister] + source));
            CompleteTiming(M68kInstructionTimingKey.AddaWordAddressDisplacementToAddress);
        }

        private void ExecuteAddaLongBriefIndexedToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var extension = FetchWord();
            var source = ReadLong(CalculateBriefIndexedAddress(sourceRegister, extension, opcode));
            WriteGeneralRegister(true, destinationRegister, unchecked((uint)(State.A[destinationRegister] + source)));
            CompleteTiming(M68kInstructionTimingKey.AddaLongBriefIndexedToAddress);
        }

        private void ExecuteAddaWordBriefIndexedToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var extension = FetchWord();
            var source = unchecked((int)(short)ReadWord(CalculateBriefIndexedAddress(sourceRegister, extension, opcode)));
            WriteGeneralRegister(true, destinationRegister, unchecked((uint)(State.A[destinationRegister] + source)));
            CompleteTiming(M68kInstructionTimingKey.AddaWordBriefIndexedToAddress);
        }

        private void ExecuteAddaLongDataToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var addressRegister = (opcode >> 9) & 7;
            var dataRegister = opcode & 7;
            WriteGeneralRegister(true, addressRegister, State.A[addressRegister] + State.D[dataRegister]);
            CompleteTiming(M68kInstructionTimingKey.AddaLongDataToAddress);
        }

        private void ExecuteAddaLongAddressToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            State.A[destinationRegister] = unchecked(
                State.A[destinationRegister] + State.A[sourceRegister]);
            CompleteTiming(M68kInstructionTimingKey.AddaLongAddressToAddress);
        }

        private void ExecuteAddaLongAddressDisplacementToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var addressRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var source = ReadLong(unchecked((uint)(State.A[addressRegister] + displacement)));
            WriteGeneralRegister(true, destinationRegister, State.A[destinationRegister] + source);
            CompleteTiming(M68kInstructionTimingKey.AddaLongAddressDisplacementToAddress);
        }

        private void ExecuteSubaAddressIndirectToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var isLong = (opcode & 0x100) != 0;
            var source = isLong ? ReadLong(State.A[opcode & 7]) : unchecked((uint)(int)(short)ReadWord(State.A[opcode & 7]));
            var destination = (opcode >> 9) & 7;
            WriteGeneralRegister(true, destination, unchecked(State.A[destination] - source));
            CompleteTiming(isLong ? M68kInstructionTimingKey.SubaLongAddressIndirectToAddress : M68kInstructionTimingKey.SubaWordAddressIndirectToAddress);
        }

        private void ExecuteSubaPostIncrementToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var longSize = (opcode & 0x100) != 0;
            var sourceRegister = opcode & 7;
            var address = State.A[sourceRegister];
            var source = longSize ? ReadLong(address) : unchecked((uint)(int)(short)ReadWord(address));
            WriteGeneralRegister(true, sourceRegister, unchecked(address + (longSize ? 4u : 2u)));
            var destination = (opcode >> 9) & 7;
            WriteGeneralRegister(true, destination, unchecked(State.A[destination] - source));
            CompleteTiming(longSize
                ? M68kInstructionTimingKey.SubaLongPostIncrementToAddress
                : M68kInstructionTimingKey.SubaWordPostIncrementToAddress);
        }

        private void ExecuteSubaLongImmediateToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var source = FetchLong();
            WriteGeneralRegister(true, register, State.A[register] - source);
            CompleteTiming(M68kInstructionTimingKey.SubaLongImmediateToAddress);
        }

        private void ExecuteSubaLongDataToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            WriteGeneralRegister(
                true,
                destinationRegister,
                State.A[destinationRegister] - State.D[sourceRegister]);
            CompleteTiming(M68kInstructionTimingKey.SubaLongDataToAddress);
        }

        private void ExecuteSubaLongAddressToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            State.A[destinationRegister] -= State.A[sourceRegister];
            CompleteTiming(M68kInstructionTimingKey.SubaLongAddressToAddress);
        }

        private void ExecuteSubaWordAddressDisplacementToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var addressRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var source = unchecked((uint)(int)(short)ReadWord(unchecked((uint)(State.A[addressRegister] + displacement))));
            WriteGeneralRegister(true, destinationRegister, State.A[destinationRegister] - source);
            CompleteTiming(M68kInstructionTimingKey.SubaWordAddressDisplacementToAddress);
        }

        private void ExecuteSubaLongPcDisplacementToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var extensionAddress = State.ProgramCounter;
            var displacement = unchecked((short)FetchWord());
            var source = ReadLong(unchecked((uint)(extensionAddress + displacement)));
            WriteGeneralRegister(true, register, unchecked(State.A[register] - source));
            CompleteTiming(M68kInstructionTimingKey.SubaLongPcDisplacementToAddress);
        }

        private void ExecuteSubaLongAddressDisplacementToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var addressRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var source = ReadLong(unchecked((uint)(State.A[addressRegister] + displacement)));
            WriteGeneralRegister(true, destinationRegister, State.A[destinationRegister] - source);
            CompleteTiming(M68kInstructionTimingKey.SubaLongAddressDisplacementToAddress);
        }

        private void ExecuteSubaWordImmediateToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var source = unchecked((uint)(int)(short)FetchWord());
            State.A[destinationRegister] -= source;
            CompleteTiming(M68kInstructionTimingKey.SubaWordImmediateToAddress);
        }

        private void ExecuteSubaWordDataToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var source = M68kCpuState.SignExtend(State.D[sourceRegister], M68kOperandSize.Word);
            WriteGeneralRegister(true, destinationRegister, unchecked(State.A[destinationRegister] - source));
            CompleteTiming(M68kInstructionTimingKey.SubaWordDataToAddress);
        }

        private void ExecuteLongMultiplyDivide(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var extension = FetchWord();
            if ((extension & 0x83F8) != 0)
            {
                throw new UnsupportedM68kTimingException(opcode, State.LastInstructionProgramCounter, _profile);
            }

            if (_profile.Model == M68kAcceleratorModel.M68060 && (extension & 0x0400) != 0)
            {
                // MC68060UM C.2: neither 64-bit multiply nor 64-bit dividend
                // is implemented. Trap before reading or updating the operand EA.
                RaiseFormat0Exception(61, State.LastInstructionProgramCounter, M68kInstructionTimingKey.IllegalInstruction);
                return;
            }

            var mode = (opcode >> 3) & 7;
            var register = opcode & 7;
            var source = ReadLongDataSource(mode, register, opcode);
            var primaryDestination = (extension >> 12) & 7;
            var secondaryDestination = extension & 7;
            var signed = (extension & 0x0800) != 0;
            var extendedResult = (extension & 0x0400) != 0;

            if ((opcode & 0xFFC0) == 0x4C00)
            {
                ExecuteMultiplyLong(source, primaryDestination, secondaryDestination, signed, extendedResult);
                CompleteTiming(signed ? M68kInstructionTimingKey.MulsLong : M68kInstructionTimingKey.MuluLong);
                return;
            }

            ExecuteDivideLong(source, primaryDestination, secondaryDestination, signed, extendedResult);
            CompleteTiming(signed ? M68kInstructionTimingKey.DivsLong : M68kInstructionTimingKey.DivuLong);
        }

        private void ExecuteMultiplyLong(
            uint source,
            int lowRegister,
            int highRegister,
            bool signed,
            bool extendedResult)
        {
            if (signed)
            {
                var product = (long)unchecked((int)State.D[lowRegister]) * unchecked((int)source);
                var rawProduct = unchecked((ulong)product);
                if (extendedResult)
                {
                    State.D[highRegister] = (uint)(rawProduct >> 32);
                    State.D[lowRegister] = (uint)rawProduct;
                    State.SetFlag(M68kCpuState.Negative, product < 0);
                    State.SetFlag(M68kCpuState.Zero, product == 0);
                    State.SetFlag(M68kCpuState.Overflow, false);
                }
                else
                {
                    State.D[lowRegister] = (uint)rawProduct;
                    State.SetNegativeZero((uint)rawProduct, M68kOperandSize.Long);
                    State.SetFlag(M68kCpuState.Overflow, product < int.MinValue || product > int.MaxValue);
                }
            }
            else
            {
                var product = (ulong)State.D[lowRegister] * source;
                if (extendedResult)
                {
                    State.D[highRegister] = (uint)(product >> 32);
                    State.D[lowRegister] = (uint)product;
                    State.SetFlag(M68kCpuState.Negative, (product & 0x8000_0000_0000_0000ul) != 0);
                    State.SetFlag(M68kCpuState.Zero, product == 0);
                    State.SetFlag(M68kCpuState.Overflow, false);
                }
                else
                {
                    State.D[lowRegister] = (uint)product;
                    State.SetNegativeZero((uint)product, M68kOperandSize.Long);
                    State.SetFlag(M68kCpuState.Overflow, (product >> 32) != 0);
                }
            }

            State.SetFlag(M68kCpuState.Carry, false);
        }

        private void ExecuteDivideLong(
            uint source,
            int quotientRegister,
            int remainderRegister,
            bool signed,
            bool extendedDividend)
        {
            if (source == 0)
            {
                RaiseFormat0Exception(5, State.ProgramCounter, signed ? M68kInstructionTimingKey.DivsLong : M68kInstructionTimingKey.DivuLong);
                return;
            }

            if (signed)
            {
                var divisor = unchecked((int)source);
                var dividend = extendedDividend
                    ? unchecked((long)(((ulong)State.D[remainderRegister] << 32) | State.D[quotientRegister]))
                    : unchecked((int)State.D[quotientRegister]);
                if (dividend == long.MinValue && divisor == -1)
                {
                    State.SetFlag(M68kCpuState.Overflow, true);
                    State.SetFlag(M68kCpuState.Carry, false);
                    return;
                }

                var quotient = dividend / divisor;
                if (quotient < int.MinValue || quotient > int.MaxValue)
                {
                    State.SetFlag(M68kCpuState.Overflow, true);
                    State.SetFlag(M68kCpuState.Carry, false);
                    return;
                }

                var remainder = dividend % divisor;
                if (remainderRegister != quotientRegister)
                {
                    State.D[remainderRegister] = unchecked((uint)(int)remainder);
                }

                State.D[quotientRegister] = unchecked((uint)(int)quotient);
                State.SetNegativeZero((uint)quotient, M68kOperandSize.Long);
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
                return;
            }

            var unsignedDivisor = source;
            var unsignedDividend = extendedDividend
                ? ((ulong)State.D[remainderRegister] << 32) | State.D[quotientRegister]
                : State.D[quotientRegister];
            var unsignedQuotient = unsignedDividend / unsignedDivisor;
            if (unsignedQuotient > uint.MaxValue)
            {
                State.SetFlag(M68kCpuState.Overflow, true);
                State.SetFlag(M68kCpuState.Carry, false);
                return;
            }

            var unsignedRemainder = unsignedDividend % unsignedDivisor;
            if (remainderRegister != quotientRegister)
            {
                State.D[remainderRegister] = (uint)unsignedRemainder;
            }

            State.D[quotientRegister] = (uint)unsignedQuotient;
            State.SetNegativeZero((uint)unsignedQuotient, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
        }

        private void ExecuteMultiplyWord(ushort opcode, bool signed)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            if (!TryReadWordDataSource((opcode >> 3) & 7, opcode & 7, opcode, out var source))
            {
                return;
            }

            uint result;
            if (signed)
            {
                var destination = unchecked((short)State.D[register]);
                var signedSource = unchecked((short)source);
                result = unchecked((uint)(destination * signedSource));
            }
            else
            {
                result = (uint)((ushort)State.D[register] * source);
            }

            State.D[register] = result;
            State.SetNegativeZero(result, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteOperandShapeTiming(
                signed
                    ? M68kInstructionTimingKey.MulsWordEffectiveAddressToData
                    : M68kInstructionTimingKey.MuluWordEffectiveAddressToData,
                signed ? "MULS.W <ea>,Dn" : "MULU.W <ea>,Dn");
        }

        private void ExecuteChkWordImmediate(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var upperBound = unchecked((short)FetchWord());
            var value = unchecked((short)State.D[register]);
            if (value < 0 || value > upperBound)
            {
                State.SetFlag(M68kCpuState.Negative, value < 0);
                RaiseFormat0Exception(6, State.ProgramCounter, M68kInstructionTimingKey.IllegalInstruction);
                return;
            }

            State.SetFlag(M68kCpuState.Negative, false);
            CompleteTiming(M68kInstructionTimingKey.Nop);
        }

        private void ExecuteDivideWord(ushort opcode, bool signed)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            if (!TryReadWordDataSource((opcode >> 3) & 7, opcode & 7, opcode, out var divisor))
            {
                return;
            }

            var timingKey = signed
                ? M68kInstructionTimingKey.DivsWordEffectiveAddressToData
                : M68kInstructionTimingKey.DivuWordEffectiveAddressToData;
            if (divisor == 0)
            {
                RaiseFormat0Exception(5, State.ProgramCounter, timingKey);
                return;
            }

            if (signed)
            {
                var signedDivisor = unchecked((short)divisor);
                var signedDividend = unchecked((int)State.D[register]);
                var signedQuotient = signedDividend / signedDivisor;
                var signedRemainder = signedDividend % signedDivisor;
                if (signedQuotient < short.MinValue || signedQuotient > short.MaxValue)
                {
                    State.SetFlag(M68kCpuState.Overflow, true);
                }
                else
                {
                    var quotient = unchecked((uint)signedQuotient);
                    var remainder = unchecked((uint)signedRemainder);
                    State.D[register] = ((remainder & 0xFFFF) << 16) | (quotient & 0xFFFF);
                    State.SetNegativeZero(quotient, M68kOperandSize.Word);
                    State.SetFlag(M68kCpuState.Overflow, false);
                    State.SetFlag(M68kCpuState.Carry, false);
                }
            }
            else
            {
                var dividend = State.D[register];
                var quotient = dividend / divisor;
                var remainder = dividend % divisor;
                if (quotient > 0xFFFF)
                {
                    State.SetFlag(M68kCpuState.Overflow, true);
                }
                else
                {
                    State.D[register] = ((remainder & 0xFFFF) << 16) | (quotient & 0xFFFF);
                    State.SetNegativeZero(quotient, M68kOperandSize.Word);
                    State.SetFlag(M68kCpuState.Overflow, false);
                    State.SetFlag(M68kCpuState.Carry, false);
                }
            }

            CompleteOperandShapeTiming(
                timingKey,
                signed ? "DIVS.W <ea>,Dn" : "DIVU.W <ea>,Dn");
        }

        private void ExecuteBitField(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var extension = FetchWord();
            var mode = (opcode >> 3) & 7;
            var register = opcode & 7;
            var operation = (opcode >> 8) & 7;
            var operandRegister = (extension >> 12) & 7;
            var offset = DecodeBitFieldOffset(extension);
            var width = DecodeBitFieldWidth(extension);

            if (mode == 0)
            {
                ExecuteDataRegisterBitField(operation, register, operandRegister, offset, width);
            }
            else
            {
                var baseAddress = CalculateBitFieldBaseAddress(mode, register, opcode);
                ExecuteMemoryBitField(operation, baseAddress, operandRegister, offset, width);
            }

            CompleteTiming(M68kInstructionTimingKey.Nop);
        }

        private int DecodeBitFieldOffset(ushort extension)
        {
            if ((extension & 0x0800) != 0)
            {
                return unchecked((int)State.D[(extension >> 6) & 7]);
            }

            return (extension >> 6) & 0x1F;
        }

        private int DecodeBitFieldWidth(ushort extension)
        {
            if ((extension & 0x0020) != 0)
            {
                var registerWidth = (int)(State.D[extension & 7] & 0x1F);
                return registerWidth == 0 ? 32 : registerWidth;
            }

            var immediateWidth = extension & 0x1F;
            return immediateWidth == 0 ? 32 : immediateWidth;
        }

        private void ExecuteCas(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var extension = FetchWord();
            var size = DecodeCasSize(opcode);
            var address = CalculateBitFieldBaseAddress((opcode >> 3) & 7, opcode & 7, opcode);
            var compareRegister = extension & 7;
            if (_profile.Model == M68kAcceleratorModel.M68060 && (address & ((uint)size - 1)) != 0)
            {
                RaiseFormat0Exception(61, State.LastInstructionProgramCounter, M68kInstructionTimingKey.IllegalInstruction);
                return;
            }
            var updateRegister = (extension >> 6) & 7;
            var destination = ReadSized(address, size);
            var compare = State.D[compareRegister] & M68kCpuState.Mask(size);
            SetCompareFlagsPreserveExtend(destination, compare, size);

            if ((destination & M68kCpuState.Mask(size)) == compare)
            {
                WriteSized(address, State.D[updateRegister], size);
            }
            else
            {
                WriteDataRegisterSized(compareRegister, destination, size);
            }

            CompleteTiming(M68kInstructionTimingKey.Nop);
        }

        private void ExecuteCas2(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var extension1 = FetchWord();
            var extension2 = FetchWord();
            var size = opcode == 0x0CFC ? M68kOperandSize.Word : M68kOperandSize.Long;
            var address1 = ReadCas2Address(extension1);
            var address2 = ReadCas2Address(extension2);
            var destination1 = ReadSized(address1, size);
            var destination2 = ReadSized(address2, size);
            var compareRegister1 = extension1 & 7;
            var compareRegister2 = extension2 & 7;
            var updateRegister1 = (extension1 >> 6) & 7;
            var updateRegister2 = (extension2 >> 6) & 7;
            var compare1 = State.D[compareRegister1] & M68kCpuState.Mask(size);
            var compare2 = State.D[compareRegister2] & M68kCpuState.Mask(size);

            if (destination1 == compare1 && destination2 == compare2)
            {
                SetCompareFlagsPreserveExtend(destination2, compare2, size);
                WriteSized(address1, State.D[updateRegister1], size);
                WriteSized(address2, State.D[updateRegister2], size);
            }
            else
            {
                SetCompareFlagsPreserveExtend(
                    destination1 == compare1 ? destination2 : destination1,
                    destination1 == compare1 ? compare2 : compare1,
                    size);
                WriteDataRegisterSized(compareRegister1, destination1, size);
                WriteDataRegisterSized(compareRegister2, destination2, size);
            }

            CompleteTiming(M68kInstructionTimingKey.Nop);
        }

        private static M68kOperandSize DecodeCasSize(ushort opcode)
            => (opcode & 0x0E00) switch
            {
                0x0A00 => M68kOperandSize.Byte,
                0x0C00 => M68kOperandSize.Word,
                0x0E00 => M68kOperandSize.Long,
                _ => throw new InvalidOperationException($"Invalid CAS opcode 0x{opcode:X4}.")
            };

        private uint ReadCas2Address(ushort extension)
        {
            var register = (extension >> 12) & 7;
            return (extension & 0x8000) != 0
                ? State.A[register]
                : State.D[register];
        }

        private uint ReadSized(uint address, M68kOperandSize size)
            => size switch
            {
                M68kOperandSize.Byte => ReadByte(address),
                M68kOperandSize.Word => ReadWord(address),
                M68kOperandSize.Long => ReadLong(address),
                _ => throw new ArgumentOutOfRangeException(nameof(size), size, null)
            };

        private void WriteSized(uint address, uint value, M68kOperandSize size)
        {
            switch (size)
            {
                case M68kOperandSize.Byte:
                    WriteByte(address, (byte)value);
                    return;
                case M68kOperandSize.Word:
                    WriteWord(address, (ushort)value);
                    return;
                case M68kOperandSize.Long:
                    WriteLong(address, value);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(size), size, null);
            }
        }

        private void WriteDataRegisterSized(int register, uint value, M68kOperandSize size)
        {
            switch (size)
            {
                case M68kOperandSize.Byte:
                    WriteDataRegisterByte(register, (byte)value);
                    return;
                case M68kOperandSize.Word:
                    WriteDataRegisterWord(register, (ushort)value);
                    return;
                case M68kOperandSize.Long:
                    State.D[register] = value;
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(size), size, null);
            }
        }

        private void SetCompareFlagsPreserveExtend(uint destination, uint source, M68kOperandSize size)
        {
            var extend = State.GetFlag(M68kCpuState.Extend);
            SetSubtractFlags(destination, source, destination - source, size);
            State.SetFlag(M68kCpuState.Extend, extend);
        }

        private void ExecuteChk2Cmp2(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var extension = FetchWord();
            var size = DecodeChk2Cmp2Size(opcode);
            var address = CalculateBitFieldBaseAddress((opcode >> 3) & 7, opcode & 7, opcode);
            var register = (extension >> 12) & 7;
            var useAddressRegister = (extension & 0x8000) != 0;
            var trapOnOutOfRange = (extension & 0x0800) != 0;
            var lower = ReadSignedSized(address, size);
            var upper = ReadSignedSized(address + (uint)size, size);
            var value = SignExtendForSize(
                useAddressRegister ? State.A[register] : State.D[register],
                size);
            var outOfRange = lower <= upper
                ? value < lower || value > upper
                : value > upper && value < lower;

            State.SetFlag(M68kCpuState.Carry, outOfRange);
            if (trapOnOutOfRange && outOfRange)
            {
                RaiseFormat0Exception(6, State.ProgramCounter, M68kInstructionTimingKey.IllegalInstruction);
                return;
            }

            CompleteTiming(M68kInstructionTimingKey.Nop);
        }

        private static M68kOperandSize DecodeChk2Cmp2Size(ushort opcode)
            => (opcode & 0x0600) switch
            {
                0x0000 => M68kOperandSize.Byte,
                0x0200 => M68kOperandSize.Word,
                0x0400 => M68kOperandSize.Long,
                _ => throw new InvalidOperationException($"Invalid CHK2/CMP2 opcode 0x{opcode:X4}.")
            };

        private long ReadSignedSized(uint address, M68kOperandSize size)
            => size switch
            {
                M68kOperandSize.Byte => unchecked((sbyte)ReadByte(address)),
                M68kOperandSize.Word => unchecked((short)ReadWord(address)),
                M68kOperandSize.Long => unchecked((int)ReadLong(address)),
                _ => throw new ArgumentOutOfRangeException(nameof(size), size, null)
            };

        private static long SignExtendForSize(uint value, M68kOperandSize size)
            => size switch
            {
                M68kOperandSize.Byte => unchecked((sbyte)(byte)value),
                M68kOperandSize.Word => unchecked((short)(ushort)value),
                M68kOperandSize.Long => unchecked((int)value),
                _ => throw new ArgumentOutOfRangeException(nameof(size), size, null)
            };

        private void ExecuteDataRegisterBitField(
            int operation,
            int targetRegister,
            int operandRegister,
            int offset,
            int width)
        {
            var normalizedOffset = offset & 31;
            var field = ExtractRegisterBitField(State.D[targetRegister], normalizedOffset, width);
            SetBitFieldFlags(field, width);

            switch (operation)
            {
                case 0:
                    return;
                case 1:
                    State.D[operandRegister] = field;
                    return;
                case 2:
                    State.D[targetRegister] = InsertRegisterBitField(
                        State.D[targetRegister],
                        field ^ BitFieldMask(width),
                        normalizedOffset,
                        width);
                    return;
                case 3:
                    State.D[operandRegister] = SignExtendBitField(field, width);
                    return;
                case 4:
                    State.D[targetRegister] = InsertRegisterBitField(
                        State.D[targetRegister],
                        0,
                        normalizedOffset,
                        width);
                    return;
                case 5:
                    State.D[operandRegister] = unchecked((uint)(offset + FindFirstSetBitOffset(field, width)));
                    return;
                case 6:
                    State.D[targetRegister] = InsertRegisterBitField(
                        State.D[targetRegister],
                        BitFieldMask(width),
                        normalizedOffset,
                        width);
                    return;
                case 7:
                {
                    var source = State.D[operandRegister] & BitFieldMask(width);
                    SetBitFieldFlags(source, width);
                    State.D[targetRegister] = InsertRegisterBitField(
                        State.D[targetRegister],
                        source,
                        normalizedOffset,
                        width);
                    return;
                }
                default:
                    throw new UnsupportedM68kTimingException(State.LastOpcode, State.LastInstructionProgramCounter, _profile);
            }
        }

        private void ExecuteMemoryBitField(
            int operation,
            uint baseAddress,
            int operandRegister,
            int offset,
            int width)
        {
            var byteOffset = FloorDivideBy8(offset);
            var bitOffset = offset - (byteOffset * 8);
            var address = unchecked((uint)(baseAddress + byteOffset));
            var byteCount = (bitOffset + width + 7) / 8;
            var value = ReadBitFieldBytes(address, byteCount);
            var field = ExtractMemoryBitField(value, bitOffset, width, byteCount);
            SetBitFieldFlags(field, width);

            switch (operation)
            {
                case 0:
                    return;
                case 1:
                    State.D[operandRegister] = field;
                    return;
                case 2:
                    WriteBitFieldBytes(
                        address,
                        byteCount,
                        InsertMemoryBitField(value, field ^ BitFieldMask(width), bitOffset, width, byteCount));
                    return;
                case 3:
                    State.D[operandRegister] = SignExtendBitField(field, width);
                    return;
                case 4:
                    WriteBitFieldBytes(
                        address,
                        byteCount,
                        InsertMemoryBitField(value, 0, bitOffset, width, byteCount));
                    return;
                case 5:
                    State.D[operandRegister] = unchecked((uint)(offset + FindFirstSetBitOffset(field, width)));
                    return;
                case 6:
                    WriteBitFieldBytes(
                        address,
                        byteCount,
                        InsertMemoryBitField(value, BitFieldMask(width), bitOffset, width, byteCount));
                    return;
                case 7:
                {
                    var source = State.D[operandRegister] & BitFieldMask(width);
                    SetBitFieldFlags(source, width);
                    WriteBitFieldBytes(
                        address,
                        byteCount,
                        InsertMemoryBitField(value, source, bitOffset, width, byteCount));
                    return;
                }
                default:
                    throw new UnsupportedM68kTimingException(State.LastOpcode, State.LastInstructionProgramCounter, _profile);
            }
        }

        private void ExecuteLsrWordImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var count = (opcode >> 9) & 7;
            if (count == 0)
            {
                count = 8;
            }

            var value = (ushort)State.D[register];
            var result = (ushort)(value >> count);
            WriteDataRegisterWord(register, result);
            var carry = ((value >> (count - 1)) & 1) != 0;
            State.SetNegativeZero(result, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, carry);
            State.SetFlag(M68kCpuState.Extend, carry);
            CompleteTiming(M68kInstructionTimingKey.Nop);
        }

        private void ExecuteLsrByteImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var count = (opcode >> 9) & 7;
            if (count == 0)
            {
                count = 8;
            }

            var value = (byte)State.D[register];
            var result = (byte)(value >> count);
            var carry = ((value >> (count - 1)) & 1) != 0;
            WriteDataRegisterByte(register, result);
            State.SetNegativeZero(result, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, carry);
            State.SetFlag(M68kCpuState.Extend, carry);
            CompleteTiming(M68kInstructionTimingKey.LsrByteImmediateData);
        }

        private void ExecuteLsrWordAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var addressRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[addressRegister] + displacement));
            var value = ReadWord(address);
            var carry = (value & 1) != 0;
            var result = (ushort)(value >> 1);
            WriteWord(address, result);
            State.SetNegativeZero(result, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, carry);
            State.SetFlag(M68kCpuState.Extend, carry);
            CompleteTiming(M68kInstructionTimingKey.LsrWordAddressDisplacement);
        }

        private uint CalculateBitFieldBaseAddress(int mode, int register, ushort opcode)
        {
            switch (mode)
            {
                case 2:
                    return State.A[register];
                case 5:
                    return unchecked((uint)(State.A[register] + unchecked((int)(short)FetchWord())));
                case 6:
                    return CalculateBriefIndexedAddress(register, FetchWord(), opcode);
                case 7 when register == 0:
                    return unchecked((uint)(int)(short)FetchWord());
                case 7 when register == 1:
                    return FetchLong();
                case 7 when register == 2:
                {
                    var extensionAddress = State.ProgramCounter;
                    var displacement = unchecked((int)(short)FetchWord());
                    return unchecked((uint)(extensionAddress + displacement));
                }
                case 7 when register == 3:
                {
                    var extensionAddress = State.ProgramCounter;
                    var extension = FetchWord();
                    return CalculateBriefIndexedAddress(extensionAddress, extension, opcode);
                }
                default:
                    throw new UnsupportedM68kTimingException(opcode, State.LastInstructionProgramCounter, _profile);
            }
        }

        private static int FloorDivideBy8(int value)
            => value >= 0 ? value / 8 : -((7 - value) / 8);

        private static uint BitFieldMask(int width)
            => width == 32 ? 0xFFFF_FFFFu : (1u << width) - 1u;

        private static uint ExtractRegisterBitField(uint value, int offset, int width)
        {
            var result = 0u;
            for (var bit = 0; bit < width; bit++)
            {
                var sourceBit = 31 - ((offset + bit) & 31);
                result = (result << 1) | ((value >> sourceBit) & 1u);
            }

            return result;
        }

        private static uint InsertRegisterBitField(uint current, uint field, int offset, int width)
        {
            for (var bit = 0; bit < width; bit++)
            {
                var targetBit = 31 - ((offset + bit) & 31);
                var fieldBit = (field >> (width - 1 - bit)) & 1u;
                current = fieldBit == 0
                    ? current & ~(1u << targetBit)
                    : current | (1u << targetBit);
            }

            return current;
        }

        private static uint ExtractMemoryBitField(ulong value, int bitOffset, int width, int byteCount)
        {
            var shift = (byteCount * 8) - bitOffset - width;
            return (uint)((value >> shift) & BitFieldMask(width));
        }

        private static ulong InsertMemoryBitField(ulong current, uint field, int bitOffset, int width, int byteCount)
        {
            var shift = (byteCount * 8) - bitOffset - width;
            var mask = (ulong)BitFieldMask(width) << shift;
            return (current & ~mask) | (((ulong)field << shift) & mask);
        }

        private ulong ReadBitFieldBytes(uint address, int byteCount)
        {
            var value = 0ul;
            for (var index = 0; index < byteCount; index++)
            {
                value = (value << 8) | ReadByte(address + (uint)index);
            }

            return value;
        }

        private void WriteBitFieldBytes(uint address, int byteCount, ulong value)
        {
            for (var index = byteCount - 1; index >= 0; index--)
            {
                WriteByte(address + (uint)index, (byte)value);
                value >>= 8;
            }
        }

        private static uint SignExtendBitField(uint field, int width)
            => width == 32 || (field & (1u << (width - 1))) == 0
                ? field
                : field | ~BitFieldMask(width);

        private void SetBitFieldFlags(uint field, int width)
        {
            State.SetFlag(M68kCpuState.Negative, (field & (1u << (width - 1))) != 0);
            State.SetFlag(M68kCpuState.Zero, field == 0);
        }

        private static int FindFirstSetBitOffset(uint field, int width)
        {
            for (var bit = 0; bit < width; bit++)
            {
                if ((field & (1u << (width - 1 - bit))) != 0)
                {
                    return bit;
                }
            }

            return width;
        }

        private void ExecuteAndLongImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var immediate = FetchLong();
            var result = State.D[register] & immediate;
            State.D[register] = result;
            State.SetNegativeZero(result, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.AndLongImmediateToData);
        }

        private void ExecuteAndWordImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var immediate = FetchWord();
            var result = (ushort)(State.D[register] & immediate);
            WriteDataRegisterWord(register, result);
            State.SetNegativeZero(result, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.AndWordImmediateToData);
        }

        private void ExecuteAndByteImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var immediate = (byte)FetchWord();
            var result = (byte)(State.D[register] & immediate);
            WriteDataRegisterByte(register, result);
            State.SetNegativeZero(result, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.AndByteImmediateToData);
        }

        private void ExecuteAndiByteImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var immediate = (byte)FetchWord();
            var result = (byte)(State.D[register] & immediate);
            WriteDataRegisterByte(register, result);
            SetMoveFlags(result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.AndiByteImmediateToData);
        }

        private void ExecuteAndiWideImmediateToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var isLong = (opcode & 0x80) != 0;
            var size = isLong ? M68kOperandSize.Long : M68kOperandSize.Word;
            var source = isLong ? FetchLong() : FetchWord();
            var address = State.A[opcode & 7];
            var result = ReadSized(address, size) & source;
            if (isLong) WriteLong(address, result); else WriteWord(address, (ushort)result);
            SetMoveFlags(result, size);
            CompleteTiming(isLong ? M68kInstructionTimingKey.AndiLongImmediateToAddressIndirect : M68kInstructionTimingKey.AndiWordImmediateToAddressIndirect);
        }

        private void ExecuteAndiByteImmediateToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var immediate = (byte)FetchWord();
            var address = State.A[opcode & 7];
            var result = (byte)(ReadByte(address) & immediate);
            WriteByte(address, result);
            SetMoveFlags(result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.AndiByteImmediateToAddressIndirect);
        }

        private void ExecuteAndiByteImmediateToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var immediate = (byte)FetchWord();
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[opcode & 7] + displacement));
            var result = (byte)(ReadByte(address) & immediate);
            WriteByte(address, result);
            SetMoveFlags(result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.AndiByteImmediateToAddressDisplacement);
        }

        private void ExecuteAndLongEffectiveAddressToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var mode = (opcode >> 3) & 7;
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var source = ReadLongDataSource(mode, sourceRegister, opcode);
            var result = State.D[destinationRegister] & source;
            State.D[destinationRegister] = result;
            State.SetNegativeZero(result, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteOperandShapeTiming(
                M68kInstructionTimingKey.AndLongEffectiveAddressToData,
                GetAndLongTimingLabel(mode, sourceRegister));
        }

        private static string GetAndLongTimingLabel(int mode, int register)
            => mode switch
            {
                0 => "AND.L Dn,Dn",
                2 => "AND.L (An),Dn",
                3 => "AND.L (An)+,Dn",
                4 => "AND.L -(An),Dn",
                5 => "AND.L (d16,An),Dn",
                6 => "AND.L (d8,An,Xn),Dn",
                7 when register == 0 => "AND.L (xxx).W,Dn",
                7 when register == 1 => "AND.L (xxx).L,Dn",
                7 when register == 2 => "AND.L (d16,PC),Dn",
                7 when register == 3 => "AND.L (d8,PC,Xn),Dn",
                7 when register == 4 => "AND.L #<data>,Dn",
                _ => "AND.L <ea>,Dn"
            };

        private void ExecuteOrLongEffectiveAddressToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var mode = (opcode >> 3) & 7;
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var source = ReadLongDataSource(mode, sourceRegister, opcode);
            var result = State.D[destinationRegister] | source;
            State.D[destinationRegister] = result;
            State.SetNegativeZero(result, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteOperandShapeTiming(
                M68kInstructionTimingKey.OrLongEffectiveAddressToData,
                GetOrLongTimingLabel(mode, sourceRegister));
        }

        private void ExecuteOrWordImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var immediate = FetchWord();
            var result = (ushort)(State.D[destinationRegister] | immediate);
            WriteDataRegisterWord(destinationRegister, result);
            State.SetNegativeZero(result, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.OrWordImmediateToData);
        }

        private void ExecuteOrByteImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var source = (byte)FetchWord();
            var result = (byte)(State.D[destinationRegister] | source);
            WriteDataRegisterByte(destinationRegister, result);
            SetMoveFlags(result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.OrByteImmediateToData);
        }

        private void ExecuteOrWordAddressDisplacementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var source = ReadWord(unchecked((uint)(State.A[sourceRegister] + displacement)));
            var result = (ushort)(State.D[destinationRegister] | source);
            WriteDataRegisterWord(destinationRegister, result);
            SetMoveFlags(result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.OrWordAddressDisplacementToData);
        }

        private void ExecuteImmediateLogicalDataForm(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var operation = opcode & 0x0F00;
            var size = operation == 0x0A00 ? M68kOperandSize.Byte : M68kOperandSize.Long;
            var immediate = size == M68kOperandSize.Byte ? (uint)(byte)FetchWord() : FetchLong();
            var original = State.D[register];
            var result = operation switch { 0x0000 => original | immediate, 0x0200 => original & immediate, _ => original ^ immediate };
            WriteDataRegisterSized(register, result, size);
            SetMoveFlags(result, size);
            CompleteTiming(operation switch
            {
                0x0000 => M68kInstructionTimingKey.OriLongImmediateToData,
                0x0200 => M68kInstructionTimingKey.AndiLongImmediateToData,
                _ => M68kInstructionTimingKey.EoriByteImmediateToData
            });
        }

        private void ExecuteOriWordImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = opcode & 7;
            var immediate = FetchWord();
            var result = (ushort)(State.D[destinationRegister] | immediate);
            WriteDataRegisterWord(destinationRegister, result);
            SetMoveFlags(result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.OriWordImmediateToData);
        }

        private static string GetOrLongTimingLabel(int mode, int register)
            => mode switch
            {
                0 => "OR.L Dn,Dn",
                2 => "OR.L (An),Dn",
                3 => "OR.L (An)+,Dn",
                4 => "OR.L -(An),Dn",
                5 => "OR.L (d16,An),Dn",
                6 => "OR.L (d8,An,Xn),Dn",
                7 when register == 0 => "OR.L (xxx).W,Dn",
                7 when register == 1 => "OR.L (xxx).L,Dn",
                7 when register == 2 => "OR.L (d16,PC),Dn",
                7 when register == 3 => "OR.L (d8,PC,Xn),Dn",
                7 when register == 4 => "OR.L #<data>,Dn",
                _ => "OR.L <ea>,Dn"
            };

        private void ExecuteAndiWordImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var immediate = FetchWord();
            var result = (ushort)(State.D[register] & immediate);
            WriteDataRegisterWord(register, result);
            State.SetNegativeZero(result, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.AndWordImmediateToData);
        }

        private void ExecuteAndiLongImmediateToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var immediate = FetchLong();
            var displacement = unchecked((short)FetchWord());
            var address = unchecked((uint)(State.A[opcode & 7] + displacement));
            var result = ReadLong(address) & immediate;
            WriteLong(address, result);
            SetMoveFlags(result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.AndiLongImmediateToAddressDisplacement);
        }

        private void ExecuteAndiWordImmediateToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var immediate = FetchWord();
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[opcode & 7] + displacement));
            var result = (ushort)(ReadWord(address) & immediate);
            WriteWord(address, result);
            State.SetNegativeZero(result, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.AndiWordImmediateToAddressDisplacement);
        }

        private void ExecuteMoveFromCcr(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var value = (ushort)(State.StatusRegister & 0x001F);
            WriteWordDestination((opcode >> 3) & 7, opcode & 7, value, opcode);
            CompleteTiming(M68kInstructionTimingKey.Nop);
        }

        private void ExecuteMoveStatusRegisterToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            if ((State.StatusRegister & M68kCpuState.Supervisor) == 0)
            {
                RaiseFormat0Exception(8, State.LastInstructionProgramCounter, M68kInstructionTimingKey.PrivilegeViolation);
                return;
            }

            WriteWord(State.A[opcode & 7], State.StatusRegister);
            CompleteTiming(M68kInstructionTimingKey.MoveWordStatusRegisterToAddressIndirect);
        }

        private void ExecuteMoveStatusRegisterToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            if ((State.StatusRegister & M68kCpuState.Supervisor) == 0)
            {
                RaiseFormat0Exception(8, State.LastInstructionProgramCounter, M68kInstructionTimingKey.PrivilegeViolation);
                return;
            }

            WriteDataRegisterWord(opcode & 7, State.StatusRegister);
            CompleteTiming(M68kInstructionTimingKey.MoveWordStatusRegisterToData);
        }

        private void ExecuteMoveDataToStatusRegister(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            if ((State.StatusRegister & M68kCpuState.Supervisor) == 0)
            {
                RaiseFormat0Exception(8, State.LastInstructionProgramCounter, M68kInstructionTimingKey.PrivilegeViolation);
                return;
            }

            State.StatusRegister = (ushort)State.D[opcode & 7];
            CompleteTiming(M68kInstructionTimingKey.MoveWordDataToStatusRegister);
        }

        private void ExecuteMovePostIncrementToStatusRegister(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            if ((State.StatusRegister & M68kCpuState.Supervisor) == 0)
            {
                RaiseFormat0Exception(8, State.LastInstructionProgramCounter, M68kInstructionTimingKey.PrivilegeViolation);
                return;
            }

            var source = opcode & 7;
            var address = State.A[source];
            var value = ReadWord(address);
            // Retire the increment in the old stack bank before SR selects another one.
            WriteGeneralRegister(true, source, unchecked(address + 2));
            State.StatusRegister = value;
            CompleteTiming(M68kInstructionTimingKey.MoveWordPostIncrementToStatusRegister);
        }

        private void WriteWordDestination(int mode, int register, ushort value, ushort opcode)
        {
            switch (mode)
            {
                case 0:
                    WriteDataRegisterWord(register, value);
                    return;
                case 2:
                    WriteWord(State.A[register], value);
                    return;
                case 3:
                    WriteWord(State.A[register], value);
                    WriteGeneralRegister(true, register, State.A[register] + M68kIntegerSemantics.AddressIncrement(register, M68kOperandSize.Word));
                    return;
                case 4:
                {
                    var address = State.A[register] - M68kIntegerSemantics.AddressIncrement(register, M68kOperandSize.Word);
                    WriteGeneralRegister(true, register, address);
                    WriteWord(address, value);
                    return;
                }
                case 5:
                    WriteWord(unchecked((uint)(State.A[register] + unchecked((int)(short)FetchWord()))), value);
                    return;
                case 6:
                    WriteWord(CalculateBriefIndexedAddress(register, FetchWord(), opcode), value);
                    return;
                case 7 when register == 0:
                    WriteWord(unchecked((uint)(int)(short)FetchWord()), value);
                    return;
                case 7 when register == 1:
                    WriteWord(FetchLong(), value);
                    return;
                default:
                    throw new UnsupportedM68kTimingException(opcode, State.LastInstructionProgramCounter, _profile);
            }
        }

        private void ExecuteEoriLongImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var immediate = FetchLong();
            var result = State.D[register] ^ immediate;
            State.D[register] = result;
            State.SetNegativeZero(result, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.EoriLongImmediateToData);
        }

        private void ExecuteEorLongDataToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = (opcode >> 9) & 7;
            var addressRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[addressRegister] + displacement));
            var result = ReadLong(address) ^ State.D[sourceRegister];
            WriteLong(address, result);
            SetMoveFlags(result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.EorLongDataToAddressDisplacement);
        }

        private void ExecuteEorLongDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = (opcode >> 9) & 7;
            var destinationRegister = opcode & 7;
            var result = State.D[destinationRegister] ^ State.D[sourceRegister];
            State.D[destinationRegister] = result;
            SetMoveFlags(result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.EorLongDataToData);
        }

        private void ExecuteEorByteDataToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = (opcode >> 9) & 7;
            var addressRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[addressRegister] + displacement));
            var result = (byte)(ReadByte(address) ^ State.D[sourceRegister]);
            WriteByte(address, result);
            SetMoveFlags(result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.EorByteDataToAddressDisplacement);
        }

        private void ExecuteEorByteDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = (opcode >> 9) & 7;
            var destinationRegister = opcode & 7;
            var result = (byte)(State.D[destinationRegister] ^ State.D[sourceRegister]);
            WriteDataRegisterByte(destinationRegister, result);
            SetMoveFlags(result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.EorByteDataToData);
        }

        private void ExecuteEorWordDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = (opcode >> 9) & 7;
            var destinationRegister = opcode & 7;
            var result = (ushort)(State.D[destinationRegister] ^ State.D[sourceRegister]);
            WriteDataRegisterWord(destinationRegister, result);
            SetMoveFlags(result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.EorWordDataToData);
        }

        private void ExecuteEoriWordImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var immediate = FetchWord();
            var result = (ushort)(State.D[register] ^ immediate);
            WriteDataRegisterWord(register, result);
            State.SetNegativeZero(result, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.EoriWordImmediateToData);
        }

        private void ExecuteAndByteDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var result = (byte)(State.D[destinationRegister] & State.D[sourceRegister]);
            WriteDataRegisterByte(destinationRegister, result);
            SetMoveFlags(result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.AndByteDataToData);
        }

        private void ExecuteClrBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var extension = FetchWord();
            var size = ((opcode >> 6) & 3) switch
            {
                0 => M68kOperandSize.Byte,
                1 => M68kOperandSize.Word,
                _ => M68kOperandSize.Long
            };
            var address = CalculateIndexedOperandAddress(State.A[opcode & 7], extension, opcode);
            WriteSized(address, 0, size);
            SetMoveFlags(0, size);
            CompleteIndexedCalculationTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.ClrByteBriefIndexed,
                M68kOperandSize.Word => M68kInstructionTimingKey.ClrWordBriefIndexed,
                _ => M68kInstructionTimingKey.ClrLongBriefIndexed
            }, extension, M68kInstructionTimingKey.FullIndexedClear, "CLR <full-indexed>", operationCycles: 4);
        }

        private void ExecuteAndDataToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = ((opcode >> 6) & 3) switch
            {
                0 => M68kOperandSize.Byte,
                1 => M68kOperandSize.Word,
                _ => M68kOperandSize.Long
            };
            var address = State.A[opcode & 7];
            var result = ReadSized(address, size) & State.D[(opcode >> 9) & 7];
            WriteSized(address, result, size);
            SetMoveFlags(result, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.AndByteDataToAddressIndirect,
                M68kOperandSize.Word => M68kInstructionTimingKey.AndWordDataToAddressIndirect,
                _ => M68kInstructionTimingKey.AndLongDataToAddressIndirect
            });
        }

        private void ExecuteAndByteDataToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = (opcode >> 9) & 7;
            var addressRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[addressRegister] + displacement));
            var result = (byte)(ReadByte(address) & State.D[sourceRegister]);
            WriteByte(address, result);
            SetMoveFlags(result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.AndByteDataToAddressDisplacement);
        }

        private void ExecuteAndByteAddressDisplacementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var addressRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var source = ReadByte(unchecked((uint)(State.A[addressRegister] + displacement)));
            var result = (byte)(State.D[destinationRegister] & source);
            WriteDataRegisterByte(destinationRegister, result);
            SetMoveFlags(result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.AndByteAddressDisplacementToData);
        }

        private void ExecuteOrByteDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var result = (byte)(State.D[destinationRegister] | State.D[sourceRegister]);
            WriteDataRegisterByte(destinationRegister, result);
            State.SetNegativeZero(result, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.OrByteDataToData);
        }

        private void ExecuteOrWordDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var result = (ushort)(State.D[destinationRegister] | State.D[sourceRegister]);
            WriteDataRegisterWord(destinationRegister, result);
            SetMoveFlags(result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.OrWordDataToData);
        }

        private void ExecuteAndBriefIndexedToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = ((opcode >> 6) & 3) switch { 0 => M68kOperandSize.Byte, 1 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var register = (opcode >> 9) & 7;
            var extension = FetchWord();
            var source = ReadSized(CalculateBriefIndexedAddress(opcode & 7, extension, opcode), size);
            var result = State.D[register] & source;
            WriteDataRegisterSized(register, result, size);
            SetMoveFlags(result, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.AndByteBriefIndexedToData,
                M68kOperandSize.Word => M68kInstructionTimingKey.AndWordBriefIndexedToData,
                _ => M68kInstructionTimingKey.AndLongBriefIndexedToData
            });
        }

        private void ExecuteMoveWordPcBriefIndexedToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var baseAddress = State.ProgramCounter;
            var sourceExtension = FetchWord();
            var value = ReadWord(CalculateBriefIndexedAddress(baseAddress, sourceExtension, opcode));
            var displacement = unchecked((short)FetchWord());
            var destination = unchecked((uint)(State.A[(opcode >> 9) & 7] + displacement));
            WriteWord(destination, value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordPcBriefIndexedToAddressDisplacement);
        }

        private void ExecuteMoveWordAddressDisplacementToBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var displacement = unchecked((short)FetchWord());
            var value = ReadWord(unchecked((uint)(State.A[opcode & 7] + displacement)));
            var extension = FetchWord();
            WriteWord(CalculateBriefIndexedAddress((opcode >> 9) & 7, extension, opcode), value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordAddressDisplacementToBriefIndexed);
        }

        private void ExecuteArithmeticAbsoluteLongToData(ushort opcode, bool subtract = false)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = ((opcode >> 6) & 3) switch
            {
                0 => M68kOperandSize.Byte,
                1 => M68kOperandSize.Word,
                _ => M68kOperandSize.Long
            };
            var address = FetchLong();
            var register = (opcode >> 9) & 7;
            var destination = State.D[register] & M68kCpuState.Mask(size);
            var source = ReadSized(address, size);
            var result = subtract ? unchecked(destination - source) : unchecked(destination + source);
            WriteDataRegisterSized(register, result, size);
            if (subtract) SetSubtractFlags(destination, source, result, size);
            else SetAddFlags(destination, source, result, size);
            CompleteTiming(subtract ? size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.SubByteAbsoluteLongToData,
                M68kOperandSize.Word => M68kInstructionTimingKey.SubWordAbsoluteLongToData,
                _ => M68kInstructionTimingKey.SubLongAbsoluteLongToData
            } : size == M68kOperandSize.Byte ? M68kInstructionTimingKey.AddByteAbsoluteLongToData : M68kInstructionTimingKey.AddWordAbsoluteLongToData);
        }

        private void ExecuteImmediateArithmeticToAbsoluteLong(ushort opcode, bool subtract)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = (opcode & 0xC0) == 0 ? M68kOperandSize.Byte : (opcode & 0xC0) == 0x40 ? M68kOperandSize.Word : M68kOperandSize.Long;
            var immediate = size == M68kOperandSize.Long ? FetchLong() : FetchWord();
            var source = size == M68kOperandSize.Byte ? (byte)immediate : immediate;
            var address = FetchLong();
            var destination = ReadSized(address, size);
            var result = subtract ? unchecked(destination - source) : unchecked(destination + source);
            WriteSized(address, result, size);
            if (subtract) SetSubtractFlags(destination, source, result, size);
            else SetAddFlags(destination, source, result, size);
            CompleteTiming(subtract ? size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.SubiByteImmediateToAbsoluteLong,
                M68kOperandSize.Word => M68kInstructionTimingKey.SubiWordImmediateToAbsoluteLong,
                _ => M68kInstructionTimingKey.SubiLongImmediateToAbsoluteLong
            } : size == M68kOperandSize.Byte ? M68kInstructionTimingKey.AddiByteImmediateToAbsoluteLong : M68kInstructionTimingKey.AddiWordImmediateToAbsoluteLong);
        }

        private void ExecuteBitDynamicAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var address = State.A[opcode & 7];
            var value = ReadByte(address);
            var mask = 1 << (int)(State.D[(opcode >> 9) & 7] & 7);
            State.SetFlag(M68kCpuState.Zero, (value & mask) == 0);
            var set = (opcode & 0x40) != 0;
            WriteByte(address, (byte)(set ? value | mask : value & ~mask));
            CompleteTiming(set ? M68kInstructionTimingKey.BsetDynamicAddressIndirect : M68kInstructionTimingKey.BclrDynamicAddressIndirect);
        }

        private void ExecuteMoveLongAbsoluteLongToAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = FetchLong();
            var value = ReadLong(source);
            var destination = FetchLong();
            WriteLong(destination, value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAbsoluteLongToAbsoluteLong);
        }

        private void ExecuteMoveWordAddressToPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var value = (ushort)State.A[opcode & 7];
            var register = (opcode >> 9) & 7;
            var destination = State.A[register];
            WriteWord(destination, value);
            State.A[register] = unchecked(destination + 2);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordAddressToPostIncrement);
        }

        private void ExecuteMoveSizedPcDisplacementToAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var baseAddress = State.ProgramCounter;
            var displacement = unchecked((short)FetchWord());
            var size = (opcode >> 12) == 1 ? M68kOperandSize.Byte : M68kOperandSize.Long;
            var value = ReadSized(unchecked((uint)(baseAddress + displacement)), size);
            WriteSized(FetchLong(), value, size);
            SetMoveFlags(value, size);
            CompleteTiming(size == M68kOperandSize.Byte ? M68kInstructionTimingKey.MoveBytePcDisplacementToAbsoluteLong
                : M68kInstructionTimingKey.MoveLongPcDisplacementToAbsoluteLong);
        }

        private void ExecuteMoveWordPcDisplacementToAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var baseAddress = State.ProgramCounter;
            var displacement = unchecked((short)FetchWord());
            var value = ReadWord(unchecked((uint)(baseAddress + displacement)));
            var destination = FetchLong();
            WriteWord(destination, value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordPcDisplacementToAbsoluteLong);
        }

        private void ExecuteMoveWordAddressIndirectToAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var value = ReadWord(State.A[opcode & 7]);
            var address = FetchLong();
            WriteWord(address, value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordAddressIndirectToAbsoluteLong);
        }

        private void ExecuteMoveAbsoluteLongToPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = FetchLong();
            var size = (opcode >> 12) switch { 1 => M68kOperandSize.Byte, 3 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var value = ReadSized(source, size);
            var register = (opcode >> 9) & 7;
            var destination = State.A[register];
            WriteSized(destination, value, size);
            WriteGeneralRegister(true, register, unchecked(destination + M68kIntegerSemantics.AddressIncrement(register, size)));
            SetMoveFlags(value, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.MoveByteAbsoluteLongToPostIncrement,
                M68kOperandSize.Word => M68kInstructionTimingKey.MoveWordAbsoluteLongToPostIncrement,
                _ => M68kInstructionTimingKey.MoveLongAbsoluteLongToPostIncrement
            });
        }

        private void ExecuteAndSmallAbsoluteLongToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = (opcode & 0x40) == 0 ? M68kOperandSize.Byte : M68kOperandSize.Word;
            var address = FetchLong();
            var register = (opcode >> 9) & 7;
            var result = State.D[register] & ReadSized(address, size);
            WriteDataRegisterSized(register, result, size);
            SetMoveFlags(result, size);
            CompleteTiming(size == M68kOperandSize.Byte ? M68kInstructionTimingKey.AndByteAbsoluteLongToData : M68kInstructionTimingKey.AndWordAbsoluteLongToData);
        }

        private void ExecuteClrWordAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var address = FetchLong();
            WriteWord(address, 0);
            SetMoveFlags(0, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.ClrWordAbsoluteLong);
        }

        private void ExecuteTstLongAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var address = FetchLong();
            SetMoveFlags(ReadLong(address), M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.TstLongAbsoluteLong);
        }

        private void ExecuteNotMemory(ushort opcode, bool addressIndirect)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = ((opcode >> 6) & 3) switch { 0 => M68kOperandSize.Byte, 1 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var address = addressIndirect ? State.A[opcode & 7] : FetchLong();
            var result = ~ReadSized(address, size);
            WriteSized(address, result, size);
            SetMoveFlags(result, size);
            var postIncrement = (opcode & 0x38) == 0x18;
            if (postIncrement) State.A[opcode & 7] = unchecked(address +
                (size == M68kOperandSize.Byte && (opcode & 7) == 7 ? 2u : (uint)size));
            CompleteTiming(postIncrement ? size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.NotBytePostIncrement,
                M68kOperandSize.Word => M68kInstructionTimingKey.NotWordPostIncrement,
                _ => M68kInstructionTimingKey.NotLongPostIncrement
            } : addressIndirect ? size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.NotByteAddressIndirect,
                M68kOperandSize.Word => M68kInstructionTimingKey.NotWordAddressIndirect,
                _ => M68kInstructionTimingKey.NotLongAddressIndirect
            } : size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.NotByteAbsoluteLong,
                M68kOperandSize.Word => M68kInstructionTimingKey.NotWordAbsoluteLong,
                _ => M68kInstructionTimingKey.NotLongAbsoluteLong
            });
        }

        private void ExecuteAndPcBriefIndexedToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = (opcode & 0x40) == 0 ? M68kOperandSize.Byte : M68kOperandSize.Word;
            var register = (opcode >> 9) & 7;
            var extensionAddress = State.ProgramCounter;
            var extension = FetchWord();
            var source = ReadSized(CalculateBriefIndexedAddress(extensionAddress, extension, opcode), size);
            var result = State.D[register] & source;
            WriteDataRegisterSized(register, result, size);
            SetMoveFlags(result, size);
            CompleteTiming(size == M68kOperandSize.Byte ?
                M68kInstructionTimingKey.AndBytePcBriefIndexedToData : M68kInstructionTimingKey.AndWordPcBriefIndexedToData);
        }

        private void ExecuteAddqWordAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var address = FetchLong();
            var source = (uint)((opcode >> 9) & 7);
            if (source == 0) source = 8;
            var destination = ReadWord(address);
            var result = unchecked(destination + source);
            WriteWord(address, (ushort)result);
            SetAddFlags(destination, source, result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.AddqWordAbsoluteLong);
        }

        private void ExecuteArithmeticPcDisplacementToData(ushort opcode, bool subtract, bool indexed = false)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = ((opcode >> 6) & 3) switch { 0 => M68kOperandSize.Byte, 1 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var register = (opcode >> 9) & 7;
            var extensionAddress = State.ProgramCounter;
            var extension = FetchWord();
            var address = indexed ? CalculateBriefIndexedAddress(extensionAddress, extension, opcode) :
                unchecked((uint)(extensionAddress + (short)extension));
            var source = ReadSized(address, size);
            var destination = size switch { M68kOperandSize.Byte => (byte)State.D[register], M68kOperandSize.Word => (ushort)State.D[register], _ => State.D[register] };
            var result = subtract ? unchecked(destination - source) : unchecked(destination + source);
            WriteDataRegisterSized(register, result, size);
            if (subtract) SetSubtractFlags(destination, source, result, size);
            else SetAddFlags(destination, source, result, size);
            CompleteTiming(indexed ? (subtract ? size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.SubBytePcBriefIndexedToData,
                M68kOperandSize.Word => M68kInstructionTimingKey.SubWordPcBriefIndexedToData,
                _ => M68kInstructionTimingKey.SubLongPcBriefIndexedToData
            } : size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.AddBytePcBriefIndexedToData,
                M68kOperandSize.Word => M68kInstructionTimingKey.AddWordPcBriefIndexedToData,
                _ => M68kInstructionTimingKey.AddLongPcBriefIndexedToData
            }) : subtract ? size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.SubBytePcDisplacementToData,
                M68kOperandSize.Word => M68kInstructionTimingKey.SubWordPcDisplacementToData,
                _ => M68kInstructionTimingKey.SubLongPcDisplacementToData
            } : (size == M68kOperandSize.Byte ? M68kInstructionTimingKey.AddBytePcDisplacementToData : M68kInstructionTimingKey.AddWordPcDisplacementToData));
        }

        private void ExecuteAddaAbsoluteLongToAddress(ushort opcode, bool subtract = false)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = (opcode & 0x100) == 0 ? M68kOperandSize.Word : M68kOperandSize.Long;
            var value = ReadSized(FetchLong(), size);
            var source = size == M68kOperandSize.Word ? unchecked((uint)(int)(short)value) : value;
            var destination = (opcode >> 9) & 7;
            WriteGeneralRegister(true, destination, subtract ? unchecked(State.A[destination] - source) : unchecked(State.A[destination] + source));
            CompleteTiming(subtract ? (size == M68kOperandSize.Word ?
                M68kInstructionTimingKey.SubaWordAbsoluteLongToAddress : M68kInstructionTimingKey.SubaLongAbsoluteLongToAddress) :
                (size == M68kOperandSize.Word ? M68kInstructionTimingKey.AddaWordAbsoluteLongToAddress : M68kInstructionTimingKey.AddaLongAbsoluteLongToAddress));
        }

        private void ExecuteAddaIndirectToAddress(ushort opcode, bool postIncrement)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = (opcode & 0x100) == 0 ? M68kOperandSize.Word : M68kOperandSize.Long;
            var sourceRegister = opcode & 7;
            var value = ReadSized(State.A[sourceRegister], size);
            if (postIncrement) State.A[sourceRegister] = unchecked(State.A[sourceRegister] + (uint)size);
            var source = size == M68kOperandSize.Word ? unchecked((uint)(int)(short)value) : value;
            var destination = (opcode >> 9) & 7;
            WriteGeneralRegister(true, destination, unchecked(State.A[destination] + source));
            CompleteTiming(postIncrement ? (size == M68kOperandSize.Word ?
                M68kInstructionTimingKey.AddaWordPostIncrementToAddress : M68kInstructionTimingKey.AddaLongPostIncrementToAddress) :
                (size == M68kOperandSize.Word ? M68kInstructionTimingKey.AddaWordAddressIndirectToAddress : M68kInstructionTimingKey.AddaLongAddressIndirectToAddress));
        }

        private void ExecuteAndiImmediateToPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = ((opcode >> 6) & 3) switch { 0 => M68kOperandSize.Byte, 1 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var immediate = size == M68kOperandSize.Long ? FetchLong() : FetchWord();
            var register = opcode & 7;
            var address = State.A[register];
            var result = ReadSized(address, size) & immediate;
            WriteSized(address, result, size);
            WriteGeneralRegister(true, register, unchecked(address + M68kIntegerSemantics.AddressIncrement(register, size)));
            SetMoveFlags(result, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.AndiByteImmediateToPostIncrement,
                M68kOperandSize.Word => M68kInstructionTimingKey.AndiWordImmediateToPostIncrement,
                _ => M68kInstructionTimingKey.AndiLongImmediateToPostIncrement
            });
        }

        private void ExecuteOrDataToAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = ((opcode >> 6) & 3) switch { 0 => M68kOperandSize.Byte, 1 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var address = FetchLong();
            var result = ReadSized(address, size) | State.D[(opcode >> 9) & 7];
            WriteSized(address, result, size);
            SetMoveFlags(result, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.OrByteDataToAbsoluteLong,
                M68kOperandSize.Word => M68kInstructionTimingKey.OrWordDataToAbsoluteLong,
                _ => M68kInstructionTimingKey.OrLongDataToAbsoluteLong
            });
        }

        private void ExecuteLogicalDataToPostIncrement(ushort opcode, bool and)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = ((opcode >> 6) & 3) switch { 0 => M68kOperandSize.Byte, 1 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var register = opcode & 7;
            var address = State.A[register];
            var destination = ReadSized(address, size);
            var source = State.D[(opcode >> 9) & 7];
            var result = and ? destination & source : destination | source;
            WriteSized(address, result, size);
            WriteGeneralRegister(true, register, unchecked(address + M68kIntegerSemantics.AddressIncrement(register, size)));
            SetMoveFlags(result, size);
            CompleteTiming((and, size) switch
            {
                (true, M68kOperandSize.Byte) => M68kInstructionTimingKey.AndByteDataToPostIncrement,
                (true, M68kOperandSize.Word) => M68kInstructionTimingKey.AndWordDataToPostIncrement,
                (true, _) => M68kInstructionTimingKey.AndLongDataToPostIncrement,
                (false, M68kOperandSize.Byte) => M68kInstructionTimingKey.OrByteDataToPostIncrement,
                (false, M68kOperandSize.Word) => M68kInstructionTimingKey.OrWordDataToPostIncrement,
                _ => M68kInstructionTimingKey.OrLongDataToPostIncrement
            });
        }

        private void ExecuteOrSizedAbsoluteLongToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = (opcode & 0x0040) == 0 ? M68kOperandSize.Byte : M68kOperandSize.Word;
            var address = FetchLong();
            var destination = (opcode >> 9) & 7;
            var result = ReadSized(address, size) | State.D[destination];
            WriteDataRegisterSized(destination, result, size);
            SetMoveFlags(result, size);
            CompleteTiming(size == M68kOperandSize.Byte
                ? M68kInstructionTimingKey.OrByteAbsoluteLongToData
                : M68kInstructionTimingKey.OrWordAbsoluteLongToData);
        }

        private void ExecuteAndPostIncrementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = ((opcode >> 6) & 3) switch { 0 => M68kOperandSize.Byte, 1 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var source = opcode & 7;
            var address = State.A[source];
            var value = ReadSized(address, size) & State.D[(opcode >> 9) & 7];
            WriteGeneralRegister(true, source, unchecked(address + M68kIntegerSemantics.AddressIncrement(source, size)));
            WriteDataRegisterSized((opcode >> 9) & 7, value, size);
            SetMoveFlags(value, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.AndBytePostIncrementToData,
                M68kOperandSize.Word => M68kInstructionTimingKey.AndWordPostIncrementToData,
                _ => M68kInstructionTimingKey.AndLongPostIncrementToData
            });
        }

        private void ExecuteOrWordDataToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = (opcode >> 9) & 7;
            var address = State.A[opcode & 7];
            var result = (ushort)(ReadWord(address) | State.D[sourceRegister]);
            WriteWord(address, result);
            SetMoveFlags(result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.OrWordDataToAddressIndirect);
        }

        private void ExecuteOrWordDataToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = (opcode >> 9) & 7;
            var addressRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[addressRegister] + displacement));
            var result = (ushort)(ReadWord(address) | State.D[sourceRegister]);
            WriteWord(address, result);
            SetMoveFlags(result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.OrWordDataToAddressDisplacement);
        }

        private void ExecuteOrByteAddressDisplacementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var source = ReadByte(unchecked((uint)(State.A[sourceRegister] + displacement)));
            var result = (byte)(State.D[destinationRegister] | source);
            WriteDataRegisterByte(destinationRegister, result);
            SetMoveFlags(result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.OrByteAddressDisplacementToData);
        }

        private void ExecuteOrByteDataToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = (opcode >> 9) & 7;
            var address = State.A[opcode & 7];
            var result = (byte)(ReadByte(address) | State.D[sourceRegister]);
            WriteByte(address, result);
            SetMoveFlags(result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.OrByteDataToAddressIndirect);
        }

        private void ExecuteOrByteDataToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = (opcode >> 9) & 7;
            var addressRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[addressRegister] + displacement));
            var result = (byte)(ReadByte(address) | State.D[sourceRegister]);
            WriteByte(address, result);
            SetMoveFlags(result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.OrByteDataToAddressDisplacement);
        }

        private void ExecuteOrLongDataToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = (opcode >> 9) & 7;
            var addressRegister = opcode & 7;
            var address = State.A[addressRegister];
            var result = ReadLong(address) | State.D[sourceRegister];
            WriteLong(address, result);
            SetMoveFlags(result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.OrLongDataToAddressIndirect);
        }

        private void ExecuteOrLongDataToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = (opcode >> 9) & 7;
            var addressRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[addressRegister] + displacement));
            var result = ReadLong(address) | State.D[sourceRegister];
            WriteLong(address, result);
            SetMoveFlags(result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.OrLongDataToAddressDisplacement);
        }

        private void ExecuteAndWordDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var result = (ushort)(State.D[destinationRegister] & State.D[sourceRegister]);
            WriteDataRegisterWord(destinationRegister, result);
            SetMoveFlags(result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.AndWordDataToData);
        }

        private void ExecuteAndAddressIndirectToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = (M68kOperandSize)(1 << ((opcode >> 6) & 3));
            var register = (opcode >> 9) & 7;
            var value = ReadSized(State.A[opcode & 7], size) & State.D[register];
            var mask = size == M68kOperandSize.Byte ? 0xFFu : size == M68kOperandSize.Word ? 0xFFFFu : uint.MaxValue;
            State.D[register] = (State.D[register] & ~mask) | value;
            SetMoveFlags(value, size);
            CompleteTiming(size == M68kOperandSize.Byte ? M68kInstructionTimingKey.AndByteAddressIndirectToData :
                size == M68kOperandSize.Word ? M68kInstructionTimingKey.AndWordAddressIndirectToData : M68kInstructionTimingKey.AndLongAddressIndirectToData);
        }

        private void ExecuteAndWordAddressDisplacementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var addressRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var source = ReadWord(unchecked((uint)(State.A[addressRegister] + displacement)));
            var result = (ushort)(State.D[destinationRegister] & source);
            WriteDataRegisterWord(destinationRegister, result);
            SetMoveFlags(result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.AndWordAddressDisplacementToData);
        }

        private void ExecuteAndWordDataToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = (opcode >> 9) & 7;
            var addressRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[addressRegister] + displacement));
            var result = (ushort)(ReadWord(address) & State.D[sourceRegister]);
            WriteWord(address, result);
            SetMoveFlags(result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.AndWordDataToAddressDisplacement);
        }

        private void ExecuteAndLongDataToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = (opcode >> 9) & 7;
            var addressRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[addressRegister] + displacement));
            var result = ReadLong(address) & State.D[sourceRegister];
            WriteLong(address, result);
            SetMoveFlags(result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.AndLongDataToAddressDisplacement);
        }

        private void ExecuteExgDataAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var dataRegister = (opcode >> 9) & 7;
            var addressRegister = opcode & 7;
            (State.D[dataRegister], State.A[addressRegister]) =
                (State.A[addressRegister], State.D[dataRegister]);
            CompleteTiming(M68kInstructionTimingKey.ExgDataAddress);
        }

        private void ExecuteExgDataData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var leftRegister = (opcode >> 9) & 7;
            var rightRegister = opcode & 7;
            (State.D[leftRegister], State.D[rightRegister]) =
                (State.D[rightRegister], State.D[leftRegister]);
            CompleteTiming(M68kInstructionTimingKey.ExgDataData);
        }

        private void ExecuteExgAddressAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var leftRegister = (opcode >> 9) & 7;
            var rightRegister = opcode & 7;
            (State.A[leftRegister], State.A[rightRegister]) =
                (State.A[rightRegister], State.A[leftRegister]);
            CompleteTiming(M68kInstructionTimingKey.ExgAddressAddress);
        }

        private void ExecuteBcdByte(ushort opcode, bool subtract)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var memoryMode = (opcode & 0x0008) != 0;
            byte source;
            byte destination;
            uint destinationAddress = 0;

            if (memoryMode)
            {
                var sourceAddress = State.A[sourceRegister] - (sourceRegister == 7 ? 2u : 1u);
                WriteGeneralRegister(true, sourceRegister, sourceAddress);
                var address = State.A[destinationRegister] - (destinationRegister == 7 ? 2u : 1u);
                WriteGeneralRegister(true, destinationRegister, address);
                source = ReadByte(State.A[sourceRegister]);
                destinationAddress = State.A[destinationRegister];
                destination = ReadByte(destinationAddress);
            }
            else
            {
                source = (byte)State.D[sourceRegister];
                destination = (byte)State.D[destinationRegister];
            }

            var extend = State.GetFlag(M68kCpuState.Extend) ? 1 : 0;
            var result = subtract
                ? M68kIntegerSemantics.SubtractBcdByte(destination, source, extend, out var carry)
                : M68kIntegerSemantics.AddBcdByte(destination, source, extend, out carry);

            if (memoryMode)
            {
                WriteByte(destinationAddress, result);
            }
            else
            {
                WriteDataRegisterByte(destinationRegister, result);
            }

            SetBcdFlags(result, carry);
            CompleteTiming(memoryMode
                ? subtract ? M68kInstructionTimingKey.SbcdBytePredecrementMemory : M68kInstructionTimingKey.AbcdBytePredecrementMemory
                : subtract ? M68kInstructionTimingKey.SbcdByteDataToData : M68kInstructionTimingKey.AbcdByteDataToData);
        }

        private void ExecuteNbcdByte(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var mode = (opcode >> 3) & 7;
            var register = opcode & 7;
            byte destination;
            uint address = 0;
            M68kInstructionTimingKey key;

            switch (mode)
            {
                case 0:
                    destination = (byte)State.D[register];
                    key = M68kInstructionTimingKey.NbcdByteData;
                    break;
                case 2:
                    address = State.A[register];
                    destination = ReadByte(address);
                    key = M68kInstructionTimingKey.NbcdByteAddressIndirect;
                    break;
                case 3:
                    address = State.A[register];
                    destination = ReadByte(address);
                    WriteGeneralRegister(true, register, address + (register == 7 ? 2u : 1u));
                    key = M68kInstructionTimingKey.NbcdBytePostIncrement;
                    break;
                case 4:
                    address = State.A[register] - (register == 7 ? 2u : 1u);
                    WriteGeneralRegister(true, register, address);
                    destination = ReadByte(address);
                    key = M68kInstructionTimingKey.NbcdBytePredecrement;
                    break;
                case 5:
                {
                    var displacement = unchecked((int)(short)FetchWord());
                    address = unchecked((uint)(State.A[register] + displacement));
                    destination = ReadByte(address);
                    key = M68kInstructionTimingKey.NbcdByteAddressDisplacement;
                    break;
                }
                case 6:
                {
                    var extension = FetchWord();
                    address = CalculateBriefIndexedAddress(register, extension, opcode);
                    destination = ReadByte(address);
                    key = M68kInstructionTimingKey.NbcdByteBriefIndexed;
                    break;
                }
                case 7 when register == 0:
                    address = unchecked((uint)(int)(short)FetchWord());
                    destination = ReadByte(address);
                    key = M68kInstructionTimingKey.NbcdByteAbsoluteWord;
                    break;
                case 7 when register == 1:
                    address = FetchLong();
                    destination = ReadByte(address);
                    key = M68kInstructionTimingKey.NbcdByteAbsoluteLong;
                    break;
                default:
                    RaiseFormat0Exception(4, State.LastInstructionProgramCounter, M68kInstructionTimingKey.IllegalInstruction);
                    return;
            }

            var extend = State.GetFlag(M68kCpuState.Extend) ? 1 : 0;
            var result = M68kIntegerSemantics.SubtractBcdByte(0, destination, extend, out var carry);
            if (mode == 0)
            {
                WriteDataRegisterByte(register, result);
            }
            else
            {
                WriteByte(address, result);
            }

            SetBcdFlags(result, carry);
            CompleteTiming(key);
        }

        private void ExecuteOriByteImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var immediate = (byte)FetchWord();
            var result = (byte)((State.D[register] & 0xFF) | immediate);
            State.D[register] = (State.D[register] & 0xFFFF_FF00u) | result;
            State.SetNegativeZero(result, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.OriByteImmediateToData);
        }

        private void ExecuteOriByteImmediateToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var immediate = (byte)FetchWord();
            var address = State.A[opcode & 7];
            var result = (byte)(ReadByte(address) | immediate);
            WriteByte(address, result);
            SetMoveFlags(result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.OriByteImmediateToAddressIndirect);
        }

        private void ExecuteOriByteImmediateToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var immediate = (byte)FetchWord();
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[opcode & 7] + displacement));
            var result = (byte)(ReadByte(address) | immediate);
            WriteByte(address, result);
            SetMoveFlags(result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.OriByteImmediateToAddressDisplacement);
        }

        private void ExecuteBitImmediateBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var mask = 1 << (FetchWord() & 7);
            var extension = FetchWord();
            var address = CalculateBriefIndexedAddress(opcode & 7, extension, opcode);
            var original = ReadByte(address);
            var operation = (opcode >> 6) & 3;
            if (operation != 0)
            {
                var result = operation switch { 1 => original ^ mask, 2 => original & ~mask, _ => original | mask };
                WriteByte(address, (byte)result);
            }
            State.SetFlag(M68kCpuState.Zero, (original & mask) == 0);
            CompleteTiming(operation switch
            {
                0 => M68kInstructionTimingKey.BtstByteImmediateBriefIndexed,
                1 => M68kInstructionTimingKey.BchgByteImmediateBriefIndexed,
                2 => M68kInstructionTimingKey.BclrByteImmediateBriefIndexed,
                _ => M68kInstructionTimingKey.BsetByteImmediateBriefIndexed
            });
        }

        private void ExecuteBtstByteImmediateAbsoluteLong()
        {
            BeginInstruction(0x0839);
            _ = FetchWord();
            var bit = FetchWord() & 7;
            var address = FetchLong();
            var value = ReadByte(address);
            State.SetFlag(M68kCpuState.Zero, (value & (1 << bit)) == 0);
            CompleteTiming(M68kInstructionTimingKey.BtstByteImmediateAbsoluteLong);
        }

        private void ExecuteBtstByteImmediateAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var bit = FetchWord() & 7;
            var value = ReadByte(State.A[opcode & 7]);
            State.SetFlag(M68kCpuState.Zero, (value & (1 << bit)) == 0);
            CompleteTiming(M68kInstructionTimingKey.BtstByteImmediateAddressIndirect);
        }

        private void ExecuteBtstByteImmediatePostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var bit = FetchWord() & 7;
            var addressRegister = opcode & 7;
            var address = State.A[addressRegister];
            var value = ReadByte(address);
            WriteGeneralRegister(true, addressRegister, address + (addressRegister == 7 ? 2u : 1u));
            State.SetFlag(M68kCpuState.Zero, (value & (1 << bit)) == 0);
            CompleteTiming(M68kInstructionTimingKey.BtstByteImmediatePostIncrement);
        }

        private void ExecuteBtstByteImmediateAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var addressRegister = opcode & 7;
            var bit = FetchWord() & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var value = ReadByte(unchecked((uint)(State.A[addressRegister] + displacement)));
            State.SetFlag(M68kCpuState.Zero, (value & (1 << bit)) == 0);
            CompleteTiming(M68kInstructionTimingKey.BtstByteImmediateAddressDisplacement);
        }

        private void ExecuteBchgByteImmediateAbsoluteLong()
        {
            BeginInstruction(0x0879);
            _ = FetchWord();
            var bit = FetchWord() & 7;
            var address = FetchLong();
            var value = ReadByte(address);
            var mask = (byte)(1 << bit);
            State.SetFlag(M68kCpuState.Zero, (value & mask) == 0);
            WriteByte(address, (byte)(value ^ mask));
            CompleteTiming(M68kInstructionTimingKey.BchgByteImmediateAbsoluteLong);
        }

        private void ExecuteBchgByteImmediateAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var bit = FetchWord() & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[opcode & 7] + displacement));
            var value = ReadByte(address);
            var mask = (byte)(1 << bit);
            State.SetFlag(M68kCpuState.Zero, (value & mask) == 0);
            WriteByte(address, (byte)(value ^ mask));
            CompleteTiming(M68kInstructionTimingKey.BchgByteImmediateAddressDisplacement);
        }

        private void ExecuteBclrByteImmediateAbsoluteLong()
        {
            BeginInstruction(0x08B9);
            _ = FetchWord();
            var bit = FetchWord() & 7;
            var address = FetchLong();
            var value = ReadByte(address);
            var mask = (byte)(1 << bit);
            State.SetFlag(M68kCpuState.Zero, (value & mask) == 0);
            WriteByte(address, (byte)(value & ~mask));
            CompleteTiming(M68kInstructionTimingKey.BclrByteImmediateAbsoluteLong);
        }

        private void ExecuteBclrByteImmediateAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var bit = FetchWord() & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[opcode & 7] + displacement));
            var value = ReadByte(address);
            var mask = (byte)(1 << bit);
            State.SetFlag(M68kCpuState.Zero, (value & mask) == 0);
            WriteByte(address, (byte)(value & ~mask));
            CompleteTiming(M68kInstructionTimingKey.BclrByteImmediateAddressDisplacement);
        }

        private void ExecuteBsetByteImmediateAbsoluteLong()
        {
            BeginInstruction(0x08F9);
            _ = FetchWord();
            var bit = FetchWord() & 7;
            var address = FetchLong();
            var value = ReadByte(address);
            var mask = (byte)(1 << bit);
            State.SetFlag(M68kCpuState.Zero, (value & mask) == 0);
            WriteByte(address, (byte)(value | mask));
            CompleteTiming(M68kInstructionTimingKey.BsetByteImmediateAbsoluteLong);
        }

        private void ExecuteBsetByteImmediateAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var addressRegister = opcode & 7;
            var bit = FetchWord() & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[addressRegister] + displacement));
            var value = ReadByte(address);
            var mask = (byte)(1 << bit);
            State.SetFlag(M68kCpuState.Zero, (value & mask) == 0);
            WriteByte(address, (byte)(value | mask));
            CompleteTiming(M68kInstructionTimingKey.BsetByteImmediateAddressDisplacement);
        }

        private void ExecuteBitModifyDynamicAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var mask = 1 << (int)(State.D[(opcode >> 9) & 7] & 7);
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[opcode & 7] + displacement));
            var original = ReadByte(address);
            var clear = (opcode & 0x80) != 0;
            WriteByte(address, (byte)(clear ? original & ~mask : original ^ mask));
            State.SetFlag(M68kCpuState.Zero, (original & mask) == 0);
            CompleteTiming(clear ? M68kInstructionTimingKey.BclrByteDynamicAddressDisplacement : M68kInstructionTimingKey.BchgByteDynamicAddressDisplacement);
        }

        private void ExecuteBsetByteDynamicAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var bit = (int)(State.D[(opcode >> 9) & 7] & 7);
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[opcode & 7] + displacement));
            var value = ReadByte(address);
            var mask = (byte)(1 << bit);
            State.SetFlag(M68kCpuState.Zero, (value & mask) == 0);
            WriteByte(address, (byte)(value | mask));
            CompleteTiming(M68kInstructionTimingKey.BsetByteDynamicAddressDisplacement);
        }

        private void ExecuteBtstImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var bit = FetchWord() & 31;
            State.SetFlag(M68kCpuState.Zero, (State.D[register] & (1u << bit)) == 0);
            CompleteTiming(M68kInstructionTimingKey.BtstImmediateData);
        }

        private void ExecuteBtstDynamicData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var bitRegister = (opcode >> 9) & 7;
            var register = opcode & 7;
            var bit = (int)(State.D[bitRegister] & 31);
            State.SetFlag(M68kCpuState.Zero, (State.D[register] & (1u << bit)) == 0);
            CompleteTiming(M68kInstructionTimingKey.BtstDynamicData);
        }

        private void ExecuteBtstByteDynamicAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var bit = (int)(State.D[(opcode >> 9) & 7] & 7);
            var value = ReadByte(State.A[opcode & 7]);
            State.SetFlag(M68kCpuState.Zero, (value & (1 << bit)) == 0);
            CompleteTiming(M68kInstructionTimingKey.BtstByteDynamicAddressIndirect);
        }

        private void ExecuteBtstByteDynamicAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var bit = (int)(State.D[(opcode >> 9) & 7] & 7);
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[opcode & 7] + displacement));
            var value = ReadByte(address);
            State.SetFlag(M68kCpuState.Zero, (value & (1 << bit)) == 0);
            CompleteTiming(M68kInstructionTimingKey.BtstByteDynamicAddressDisplacement);
        }

        private void ExecuteBtstByteDynamicBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var bit = (int)(State.D[(opcode >> 9) & 7] & 7);
            var extension = FetchWord();
            var address = CalculateBriefIndexedAddress(opcode & 7, extension, opcode);
            var value = ReadByte(address);
            State.SetFlag(M68kCpuState.Zero, (value & (1 << bit)) == 0);
            CompleteTiming(M68kInstructionTimingKey.BtstByteDynamicBriefIndexed);
        }

        private void ExecuteBtstByteDynamicAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var bitRegister = (opcode >> 9) & 7;
            var address = FetchLong();
            var value = ReadByte(address);
            var bit = (int)(State.D[bitRegister] & 7);
            State.SetFlag(M68kCpuState.Zero, (value & (1 << bit)) == 0);
            CompleteTiming(M68kInstructionTimingKey.BtstByteDynamicAbsoluteLong);
        }

        private void ExecuteBchgByteDynamicAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var bit = (int)(State.D[(opcode >> 9) & 7] & 7);
            var address = State.A[opcode & 7];
            var value = ReadByte(address);
            var mask = (byte)(1 << bit);
            State.SetFlag(M68kCpuState.Zero, (value & mask) == 0);
            WriteByte(address, (byte)(value ^ mask));
            CompleteTiming(M68kInstructionTimingKey.BchgByteDynamicAddressIndirect);
        }

        private void ExecuteBsetImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var bit = FetchWord() & 31;
            var mask = 1u << bit;
            State.SetFlag(M68kCpuState.Zero, (State.D[register] & mask) == 0);
            State.D[register] |= mask;
            CompleteTiming(M68kInstructionTimingKey.BsetImmediateData);
        }

        private void ExecuteBsetDynamicData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var bitRegister = (opcode >> 9) & 7;
            var register = opcode & 7;
            var bit = (int)(State.D[bitRegister] & 31);
            var mask = 1u << bit;
            State.SetFlag(M68kCpuState.Zero, (State.D[register] & mask) == 0);
            State.D[register] |= mask;
            CompleteTiming(M68kInstructionTimingKey.BsetDynamicData);
        }

        private void ExecuteBclrImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var bit = FetchWord() & 31;
            var mask = 1u << bit;
            State.SetFlag(M68kCpuState.Zero, (State.D[register] & mask) == 0);
            State.D[register] &= ~mask;
            CompleteTiming(M68kInstructionTimingKey.BclrImmediateData);
        }

        private void ExecuteBchgImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var mask = 1u << (FetchWord() & 31);
            var register = opcode & 7;
            var before = State.D[register];
            State.SetFlag(M68kCpuState.Zero, (before & mask) == 0);
            State.D[register] = before ^ mask;
            CompleteTiming(M68kInstructionTimingKey.BchgImmediateData);
        }

        private void ExecuteBclrDynamicData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var bitRegister = (opcode >> 9) & 7;
            var register = opcode & 7;
            var bit = (int)(State.D[bitRegister] & 31);
            var mask = 1u << bit;
            State.SetFlag(M68kCpuState.Zero, (State.D[register] & mask) == 0);
            State.D[register] &= ~mask;
            CompleteTiming(M68kInstructionTimingKey.BclrDynamicData);
        }

        private void ExecuteSwapData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var value = State.D[register];
            var result = (value << 16) | (value >> 16);
            State.D[register] = result;
            SetMoveFlags(result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.SwapData);
        }

        private void ExecuteExtLongData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var result = M68kCpuState.SignExtend(State.D[register] & 0xFFFF, M68kOperandSize.Word);
            State.D[register] = result;
            SetMoveFlags(result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.ExtLongData);
        }

        private void ExecuteExtWordData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var result = unchecked((ushort)(short)(sbyte)State.D[register]);
            WriteDataRegisterWord(register, result);
            SetMoveFlags(result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.ExtWordData);
        }

        private void ExecuteTstPcDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var baseAddress = State.ProgramCounter;
            var displacement = unchecked((int)(short)FetchWord());
            var size = ((opcode >> 6) & 3) switch { 0 => M68kOperandSize.Byte, 1 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var value = ReadSized(unchecked((uint)(baseAddress + displacement)), size);
            SetMoveFlags(value, size);
            CompleteTiming(size switch
            {
                M68kOperandSize.Byte => M68kInstructionTimingKey.TstBytePcDisplacement,
                M68kOperandSize.Word => M68kInstructionTimingKey.TstWordPcDisplacement,
                _ => M68kInstructionTimingKey.TstLongPcDisplacement
            });
        }

        private void ExecuteTstWordData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var value = State.D[opcode & 7] & 0xFFFF;
            State.SetNegativeZero(value, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.TstWordData);
        }

        private void ExecuteTstByteData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var value = State.D[opcode & 7] & 0xFF;
            State.SetNegativeZero(value, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.TstByteData);
        }

        private void ExecuteTstWordAbsoluteLong()
        {
            BeginInstruction(0x4A79);
            _ = FetchWord();
            var value = ReadWord(FetchLong());
            State.SetNegativeZero(value, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.TstWordAbsoluteLong);
        }

        private void ExecuteTstByteAbsoluteLong()
        {
            BeginInstruction(0x4A39);
            _ = FetchWord();
            var value = ReadByte(FetchLong());
            State.SetNegativeZero(value, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.TstByteAbsoluteLong);
        }

        private void ExecuteTstByteAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var value = ReadByte(unchecked((uint)(State.A[register] + displacement)));
            State.SetNegativeZero(value, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.TstByteAddressDisplacement);
        }

        private void ExecuteTstWordBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var extension = FetchWord();
            var value = ReadWord(CalculateBriefIndexedAddress(opcode & 7, extension, opcode));
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.TstWordBriefIndexed);
        }

        private void ExecuteTstByteBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var baseRegister = opcode & 7;
            var extension = FetchWord();
            var value = ReadByte(CalculateBriefIndexedAddress(baseRegister, extension, opcode));
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.TstByteBriefIndexed);
        }

        private void ExecuteTstWordAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var value = ReadWord(State.A[opcode & 7]);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.TstWordAddressIndirect);
        }

        private void ExecuteTstByteAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var value = ReadByte(State.A[opcode & 7]);
            State.SetNegativeZero(value, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.TstByteAddressIndirect);
        }

        private void ExecuteTstWordOrLongPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = (opcode & 0x80) == 0 ? M68kOperandSize.Word : M68kOperandSize.Long;
            var register = opcode & 7;
            var address = State.A[register];
            var value = ReadSized(address, size);
            WriteGeneralRegister(true, register, unchecked(address + M68kIntegerSemantics.AddressIncrement(register, size)));
            SetMoveFlags(value, size);
            CompleteTiming(size == M68kOperandSize.Word
                ? M68kInstructionTimingKey.TstWordPostIncrement
                : M68kInstructionTimingKey.TstLongPostIncrement);
        }

        private void ExecuteTstBytePostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var value = ReadByte(State.A[register]);
            State.A[register] += M68kIntegerSemantics.AddressIncrement(register, M68kOperandSize.Byte);
            State.SetNegativeZero(value, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.TstBytePostIncrement);
        }

        private void ExecuteTstWordAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var value = ReadWord(unchecked((uint)(State.A[register] + displacement)));
            State.SetNegativeZero(value, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.TstWordAddressDisplacement);
        }

        private void ExecuteTstLongData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var value = State.D[opcode & 7];
            State.SetNegativeZero(value, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.TstLongData);
        }

        private void ExecuteTstLongAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var value = ReadLong(State.A[opcode & 7]);
            State.SetNegativeZero(value, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.TstLongAddressIndirect);
        }

        private void ExecuteTstLongAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var value = ReadLong(unchecked((uint)(State.A[register] + displacement)));
            State.SetNegativeZero(value, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.TstLongAddressDisplacement);
        }

        private void ExecuteTstLongBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var baseRegister = opcode & 7;
            var extension = FetchWord();
            var value = ReadLong(CalculateBriefIndexedAddress(baseRegister, extension, opcode));
            State.SetNegativeZero(value, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.TstLongBriefIndexed);
        }

        private void ExecuteAsrByteImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var count = (opcode >> 9) & 7;
            if (count == 0) count = 8;
            var value = (byte)State.D[register];
            var result = unchecked((byte)((sbyte)value >> count));
            var carry = ((value >> (count - 1)) & 1) != 0;
            WriteDataRegisterByte(register, result);
            State.SetNegativeZero(result, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, carry);
            State.SetFlag(M68kCpuState.Extend, carry);
            CompleteTiming(M68kInstructionTimingKey.AsrByteImmediateData);
        }

        private void ExecuteAsrLongImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var count = (opcode >> 9) & 7;
            if (count == 0)
            {
                count = 8;
            }

            var value = State.D[register];
            var result = unchecked((uint)((int)value >> count));
            var carry = ((value >> (count - 1)) & 1) != 0;
            State.D[register] = result;
            State.SetNegativeZero(result, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, carry);
            State.SetFlag(M68kCpuState.Extend, carry);
            CompleteTiming(M68kInstructionTimingKey.AsrLongImmediateData);
        }

        private void ExecuteAsrLongRegisterData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var countRegister = (opcode >> 9) & 7;
            var destinationRegister = opcode & 7;
            var count = (int)(State.D[countRegister] & 63);
            var shifted = M68kIntegerSemantics.Shift(
                State.D[destinationRegister],
                count,
                M68kOperandSize.Long,
                type: 0,
                left: false,
                State.GetFlag(M68kCpuState.Extend));

            State.D[destinationRegister] = shifted.Value;
            State.SetNegativeZero(shifted.Value, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, shifted.Overflow);
            State.SetFlag(M68kCpuState.Carry, shifted.Carry);
            if (shifted.ExtendChanged)
            {
                State.SetFlag(M68kCpuState.Extend, shifted.Extend);
            }

            CompleteTiming(M68kInstructionTimingKey.AsrLongRegisterData);
        }

        private void ExecuteAsrWordImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var count = (opcode >> 9) & 7;
            if (count == 0)
            {
                count = 8;
            }

            var value = (ushort)(State.D[register] & 0xFFFF);
            var result = (ushort)((short)value >> count);
            var carry = ((value >> (count - 1)) & 1) != 0;
            WriteDataRegisterWord(register, result);
            State.SetNegativeZero(result, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, carry);
            State.SetFlag(M68kCpuState.Extend, carry);
            CompleteTiming(M68kInstructionTimingKey.AsrWordImmediateData);
        }

        private void ExecuteLsrLongImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var count = (opcode >> 9) & 7;
            if (count == 0)
            {
                count = 8;
            }

            var value = State.D[register];
            var result = value >> count;
            var carry = ((value >> (count - 1)) & 1) != 0;
            State.D[register] = result;
            State.SetNegativeZero(result, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, carry);
            State.SetFlag(M68kCpuState.Extend, carry);
            CompleteTiming(M68kInstructionTimingKey.LsrLongImmediateData);
        }

        private void ExecuteLsrLongRegisterData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var countRegister = (opcode >> 9) & 7;
            var destinationRegister = opcode & 7;
            var count = (int)(State.D[countRegister] & 63);
            var shifted = M68kIntegerSemantics.Shift(
                State.D[destinationRegister],
                count,
                M68kOperandSize.Long,
                type: 1,
                left: false,
                State.GetFlag(M68kCpuState.Extend));

            State.D[destinationRegister] = shifted.Value;
            State.SetNegativeZero(shifted.Value, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, shifted.Overflow);
            State.SetFlag(M68kCpuState.Carry, shifted.Carry);
            if (shifted.ExtendChanged)
            {
                State.SetFlag(M68kCpuState.Extend, shifted.Extend);
            }

            CompleteTiming(M68kInstructionTimingKey.LsrLongRegisterData);
        }

        private void ExecuteAslLongImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var count = (opcode >> 9) & 7;
            if (count == 0)
            {
                count = 8;
            }

            var result = State.D[register];
            var carry = false;
            var overflow = false;
            for (var i = 0; i < count; i++)
            {
                var oldSign = (result & 0x8000_0000) != 0;
                carry = oldSign;
                result <<= 1;
                var newSign = (result & 0x8000_0000) != 0;
                overflow |= oldSign != newSign;
            }

            State.D[register] = result;
            State.SetNegativeZero(result, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, overflow);
            State.SetFlag(M68kCpuState.Carry, carry);
            State.SetFlag(M68kCpuState.Extend, carry);
            CompleteTiming(M68kInstructionTimingKey.AslLongImmediateData);
        }

        private void ExecuteAslLongRegisterData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var countRegister = (opcode >> 9) & 7;
            var destinationRegister = opcode & 7;
            var count = (int)(State.D[countRegister] & 63);
            var shifted = M68kIntegerSemantics.Shift(
                State.D[destinationRegister],
                count,
                M68kOperandSize.Long,
                type: 0,
                left: true,
                State.GetFlag(M68kCpuState.Extend));

            State.D[destinationRegister] = shifted.Value;
            State.SetNegativeZero(shifted.Value, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, shifted.Overflow);
            State.SetFlag(M68kCpuState.Carry, shifted.Carry);
            if (shifted.ExtendChanged)
            {
                State.SetFlag(M68kCpuState.Extend, shifted.Extend);
            }

            CompleteTiming(M68kInstructionTimingKey.AslLongRegisterData);
        }

        private void ExecuteAslByteRegisterData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var countRegister = (opcode >> 9) & 7;
            var destinationRegister = opcode & 7;
            var count = (int)(State.D[countRegister] & 63);
            var shifted = M68kIntegerSemantics.Shift(
                State.D[destinationRegister],
                count,
                M68kOperandSize.Byte,
                type: 0,
                left: true,
                State.GetFlag(M68kCpuState.Extend));

            WriteDataRegisterByte(destinationRegister, (byte)shifted.Value);
            State.SetNegativeZero(shifted.Value, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, shifted.Overflow);
            State.SetFlag(M68kCpuState.Carry, shifted.Carry);
            if (shifted.ExtendChanged)
            {
                State.SetFlag(M68kCpuState.Extend, shifted.Extend);
            }

            CompleteTiming(M68kInstructionTimingKey.AslByteRegisterData);
        }

        private void ExecuteLslLongRegisterData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var countRegister = (opcode >> 9) & 7;
            var destinationRegister = opcode & 7;
            var count = (int)(State.D[countRegister] & 63);
            var shifted = M68kIntegerSemantics.Shift(
                State.D[destinationRegister],
                count,
                M68kOperandSize.Long,
                type: 1,
                left: true,
                State.GetFlag(M68kCpuState.Extend));

            State.D[destinationRegister] = shifted.Value;
            State.SetNegativeZero(shifted.Value, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, shifted.Overflow);
            State.SetFlag(M68kCpuState.Carry, shifted.Carry);
            if (shifted.ExtendChanged)
            {
                State.SetFlag(M68kCpuState.Extend, shifted.Extend);
            }

            CompleteTiming(M68kInstructionTimingKey.LslLongRegisterData);
        }

        private void ExecuteLslWordRegisterData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var countRegister = (opcode >> 9) & 7;
            var destinationRegister = opcode & 7;
            var count = (int)(State.D[countRegister] & 63);
            var shifted = M68kIntegerSemantics.Shift(
                State.D[destinationRegister],
                count,
                M68kOperandSize.Word,
                type: 1,
                left: true,
                State.GetFlag(M68kCpuState.Extend));

            WriteDataRegisterWord(destinationRegister, (ushort)shifted.Value);
            State.SetNegativeZero(shifted.Value, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, shifted.Overflow);
            State.SetFlag(M68kCpuState.Carry, shifted.Carry);
            if (shifted.ExtendChanged)
            {
                State.SetFlag(M68kCpuState.Extend, shifted.Extend);
            }

            CompleteTiming(M68kInstructionTimingKey.LslWordRegisterData);
        }

        private void ExecuteLsrByteRegisterData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var countRegister = (opcode >> 9) & 7;
            var destinationRegister = opcode & 7;
            var count = (int)(State.D[countRegister] & 63);
            var shifted = M68kIntegerSemantics.Shift(
                State.D[destinationRegister],
                count,
                M68kOperandSize.Byte,
                type: 1,
                left: false,
                State.GetFlag(M68kCpuState.Extend));

            WriteDataRegisterByte(destinationRegister, (byte)shifted.Value);
            State.SetNegativeZero(shifted.Value, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, shifted.Overflow);
            State.SetFlag(M68kCpuState.Carry, shifted.Carry);
            if (shifted.ExtendChanged)
            {
                State.SetFlag(M68kCpuState.Extend, shifted.Extend);
            }

            CompleteTiming(M68kInstructionTimingKey.LsrByteRegisterData);
        }

        private void ExecuteLsrWordRegisterData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var countRegister = (opcode >> 9) & 7;
            var destinationRegister = opcode & 7;
            var count = (int)(State.D[countRegister] & 63);
            var shifted = M68kIntegerSemantics.Shift(
                State.D[destinationRegister],
                count,
                M68kOperandSize.Word,
                type: 1,
                left: false,
                State.GetFlag(M68kCpuState.Extend));

            WriteDataRegisterWord(destinationRegister, (ushort)shifted.Value);
            State.SetNegativeZero(shifted.Value, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, shifted.Overflow);
            State.SetFlag(M68kCpuState.Carry, shifted.Carry);
            if (shifted.ExtendChanged)
            {
                State.SetFlag(M68kCpuState.Extend, shifted.Extend);
            }

            CompleteTiming(M68kInstructionTimingKey.LsrWordRegisterData);
        }

        private void ExecuteAslWordImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var count = (opcode >> 9) & 7;
            if (count == 0)
            {
                count = 8;
            }

            var value = (ushort)(State.D[register] & 0xFFFF);
            var result = value;
            var carry = false;
            var overflow = false;
            for (var i = 0; i < count; i++)
            {
                var oldSign = (result & 0x8000) != 0;
                carry = oldSign;
                result = (ushort)(result << 1);
                var newSign = (result & 0x8000) != 0;
                overflow |= oldSign != newSign;
            }

            WriteDataRegisterWord(register, result);
            State.SetNegativeZero(result, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, overflow);
            State.SetFlag(M68kCpuState.Carry, carry);
            State.SetFlag(M68kCpuState.Extend, carry);
            CompleteTiming(M68kInstructionTimingKey.AslWordImmediateData);
        }

        private void ExecuteLslLongImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var count = (opcode >> 9) & 7;
            if (count == 0)
            {
                count = 8;
            }

            var result = State.D[register];
            var carry = false;
            for (var i = 0; i < count; i++)
            {
                carry = (result & 0x8000_0000) != 0;
                result <<= 1;
            }

            State.D[register] = result;
            State.SetNegativeZero(result, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, carry);
            State.SetFlag(M68kCpuState.Extend, carry);
            CompleteTiming(M68kInstructionTimingKey.LslLongImmediateData);
        }

        private void ExecuteLslWordImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var count = (opcode >> 9) & 7;
            if (count == 0)
            {
                count = 8;
            }

            var result = (ushort)State.D[register];
            var carry = false;
            for (var i = 0; i < count; i++)
            {
                carry = (result & 0x8000) != 0;
                result = (ushort)(result << 1);
            }

            WriteDataRegisterWord(register, result);
            State.SetNegativeZero(result, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, carry);
            State.SetFlag(M68kCpuState.Extend, carry);
            CompleteTiming(M68kInstructionTimingKey.LslWordImmediateData);
        }

        private void ExecuteLslByteImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var count = (opcode >> 9) & 7;
            if (count == 0)
            {
                count = 8;
            }

            var result = (byte)State.D[register];
            var carry = false;
            for (var i = 0; i < count; i++)
            {
                carry = (result & 0x80) != 0;
                result = (byte)(result << 1);
            }

            WriteDataRegisterByte(register, result);
            State.SetNegativeZero(result, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, carry);
            State.SetFlag(M68kCpuState.Extend, carry);
            CompleteTiming(M68kInstructionTimingKey.LslByteImmediateData);
        }

        private void ExecuteShiftRegisterData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = (opcode & 0x40) != 0 ? M68kOperandSize.Word : M68kOperandSize.Byte;
            var left = (opcode & 0x100) != 0;
            var type = (opcode >> 3) & 3;
            var destination = opcode & 7;
            var count = (int)(State.D[(opcode >> 9) & 7] & 63);
            var shifted = M68kIntegerSemantics.Shift(State.D[destination], count, size, type, left, State.GetFlag(M68kCpuState.Extend));
            WriteDataRegisterSized(destination, shifted.Value, size);
            State.SetNegativeZero(shifted.Value, size);
            State.SetFlag(M68kCpuState.Overflow, shifted.Overflow);
            State.SetFlag(M68kCpuState.Carry, shifted.Carry);
            if (shifted.ExtendChanged) State.SetFlag(M68kCpuState.Extend, shifted.Extend);
            CompleteTiming((size, left) switch
            {
                (M68kOperandSize.Byte, false) => M68kInstructionTimingKey.AsrByteRegisterData,
                (M68kOperandSize.Word, false) => M68kInstructionTimingKey.AsrWordRegisterData,
                (M68kOperandSize.Word, true) => M68kInstructionTimingKey.AslWordRegisterData,
                _ => M68kInstructionTimingKey.LslByteRegisterData
            });
        }

        private void ExecuteRotateExtendData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = ((opcode >> 6) & 3) switch { 0 => M68kOperandSize.Byte, 1 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var left = (opcode & 0x100) != 0;
            var dynamicCount = (opcode & 0x20) != 0;
            var encodedCount = (opcode >> 9) & 7;
            var count = dynamicCount ? (int)(State.D[encodedCount] & 63) : encodedCount == 0 ? 8 : encodedCount;
            var destination = opcode & 7;
            var shifted = M68kIntegerSemantics.Shift(State.D[destination], count, size, 2, left, State.GetFlag(M68kCpuState.Extend));
            WriteDataRegisterSized(destination, shifted.Value, size);
            State.SetNegativeZero(shifted.Value, size);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, shifted.Carry);
            if (shifted.ExtendChanged) State.SetFlag(M68kCpuState.Extend, shifted.Extend);
            CompleteTiming((size, left, dynamicCount) switch
            {
                (M68kOperandSize.Byte, false, false) => M68kInstructionTimingKey.RoxrByteImmediateData,
                (M68kOperandSize.Word, false, false) => M68kInstructionTimingKey.RoxrWordImmediateData,
                (M68kOperandSize.Long, false, false) => M68kInstructionTimingKey.RoxrLongImmediateData,
                (M68kOperandSize.Byte, true, false) => M68kInstructionTimingKey.RoxlByteImmediateData,
                (M68kOperandSize.Word, true, false) => M68kInstructionTimingKey.RoxlWordImmediateData,
                (M68kOperandSize.Long, true, false) => M68kInstructionTimingKey.RoxlLongImmediateData,
                (M68kOperandSize.Byte, false, true) => M68kInstructionTimingKey.RoxrByteRegisterData,
                (M68kOperandSize.Word, false, true) => M68kInstructionTimingKey.RoxrWordRegisterData,
                (M68kOperandSize.Long, false, true) => M68kInstructionTimingKey.RoxrLongRegisterData,
                (M68kOperandSize.Byte, true, true) => M68kInstructionTimingKey.RoxlByteRegisterData,
                (M68kOperandSize.Word, true, true) => M68kInstructionTimingKey.RoxlWordRegisterData,
                _ => M68kInstructionTimingKey.RoxlLongRegisterData
            });
        }

        private void ExecuteRotateRegisterData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var size = ((opcode >> 6) & 3) switch { 0 => M68kOperandSize.Byte, 1 => M68kOperandSize.Word, _ => M68kOperandSize.Long };
            var left = (opcode & 0x0100) != 0;
            var destination = opcode & 7;
            var count = (int)(State.D[(opcode >> 9) & 7] & 63);
            var shifted = M68kIntegerSemantics.Shift(State.D[destination], count, size, 3, left, State.GetFlag(M68kCpuState.Extend));
            WriteDataRegisterSized(destination, shifted.Value, size);
            State.SetNegativeZero(shifted.Value, size);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, shifted.Carry);
            CompleteTiming((size, left) switch
            {
                (M68kOperandSize.Byte, false) => M68kInstructionTimingKey.RorByteRegisterData,
                (M68kOperandSize.Word, false) => M68kInstructionTimingKey.RorWordRegisterData,
                (M68kOperandSize.Long, false) => M68kInstructionTimingKey.RorLongRegisterData,
                (M68kOperandSize.Byte, true) => M68kInstructionTimingKey.RolByteRegisterData,
                (M68kOperandSize.Word, true) => M68kInstructionTimingKey.RolWordRegisterData,
                _ => M68kInstructionTimingKey.RolLongRegisterData
            });
        }

        private void ExecuteRorByteImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var count = (opcode >> 9) & 7;
            if (count == 0)
            {
                count = 8;
            }

            var value = (byte)State.D[register];
            var result = (byte)((value >> count) | (value << (8 - count)));
            WriteDataRegisterByte(register, result);
            State.SetNegativeZero(result, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, (result & 0x80) != 0);
            CompleteTiming(M68kInstructionTimingKey.RorByteImmediateData);
        }

        private void ExecuteRorWordImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var count = (opcode >> 9) & 7;
            if (count == 0)
            {
                count = 8;
            }

            var value = (ushort)State.D[register];
            var result = (ushort)((value >> count) | (value << (16 - count)));
            WriteDataRegisterWord(register, result);
            State.SetNegativeZero(result, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, (result & 0x8000) != 0);
            CompleteTiming(M68kInstructionTimingKey.RorWordImmediateData);
        }

        private void ExecuteRorLongImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var count = (opcode >> 9) & 7;
            if (count == 0)
            {
                count = 8;
            }

            var shifted = M68kIntegerSemantics.Shift(
                State.D[register],
                count,
                M68kOperandSize.Long,
                type: 3,
                left: false,
                State.GetFlag(M68kCpuState.Extend));
            State.D[register] = shifted.Value;
            State.SetNegativeZero(shifted.Value, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, shifted.Carry);
            CompleteTiming(M68kInstructionTimingKey.RorLongImmediateData);
        }

        private void ExecuteRolWordImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var count = (opcode >> 9) & 7;
            if (count == 0)
            {
                count = 8;
            }

            var value = (ushort)State.D[register];
            var result = (ushort)((value << count) | (value >> (16 - count)));
            WriteDataRegisterWord(register, result);
            State.SetNegativeZero(result, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, (result & 0x0001) != 0);
            CompleteTiming(M68kInstructionTimingKey.RolWordImmediateData);
        }

        private void ExecuteRolLongImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var count = (opcode >> 9) & 7;
            if (count == 0)
            {
                count = 8;
            }

            var value = State.D[register];
            var result = (value << count) | (value >> (32 - count));
            State.D[register] = result;
            State.SetNegativeZero(result, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, (result & 0x0000_0001) != 0);
            CompleteTiming(M68kInstructionTimingKey.RolLongImmediateData);
        }

        private void ExecuteByteBranch(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var condition = (opcode >> 8) & 0x0F;
            var displacement = unchecked((int)(sbyte)(opcode & 0xFF));
            var branchBase = State.ProgramCounter;

            if (condition == 0x1)
            {
                PushLong(branchBase);
                State.ProgramCounter = unchecked((uint)(branchBase + displacement));
                CompleteTiming(M68kInstructionTimingKey.BsrByte);
                return;
            }

            if (CheckCondition(condition))
            {
                var target = unchecked((uint)(branchBase + displacement));
                _instructionFrequency.RecordTakenBranch(State.LastInstructionProgramCounter, opcode, target, 2);
                State.ProgramCounter = target;
                CompleteTiming(M68kInstructionTimingKey.BranchByteTaken);
                return;
            }

            CompleteTiming(M68kInstructionTimingKey.BranchByteNotTaken);
        }

        private void ExecuteCmpiByteImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = (byte)FetchWord();
            var destination = State.D[opcode & 7] & 0xFF;
            SetCompareFlags(destination, source, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.CmpiByteImmediateToData);
        }

        private void ExecuteCmpiByteImmediateToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = (byte)FetchWord();
            var destination = ReadByte(State.A[opcode & 7]);
            SetCompareFlags(destination, source, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.CmpiByteImmediateToAddressIndirect);
        }

        private void ExecuteCmpiByteImmediateToPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var immediate = (byte)FetchWord();
            var register = opcode & 7;
            var address = unchecked(State.A[register] - M68kIntegerSemantics.AddressIncrement(register, M68kOperandSize.Byte));
            WriteGeneralRegister(true, register, address);
            SetCompareFlagsPreserveExtend(ReadByte(address), immediate, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.CmpiByteImmediateToPredecrement);
        }

        private void ExecuteCmpiByteImmediateToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var addressRegister = opcode & 7;
            var source = (byte)FetchWord();
            var displacement = unchecked((int)(short)FetchWord());
            var destination = ReadByte(unchecked((uint)(State.A[addressRegister] + displacement)));
            SetCompareFlags(destination, source, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.CmpiByteImmediateToAddressDisplacement);
        }

        private void ExecuteCmpiWordImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = FetchWord();
            var destination = State.D[opcode & 7] & 0xFFFF;
            SetCompareFlags(destination, source, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.CmpiWordImmediateToData);
        }

        private void ExecuteCmpiWordImmediateToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = FetchWord();
            var destination = ReadWord(State.A[opcode & 7]);
            SetCompareFlags(destination, source, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.CmpiWordImmediateToAddressIndirect);
        }

        private void ExecuteCmpiWordImmediateToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var addressRegister = opcode & 7;
            var source = FetchWord();
            var displacement = unchecked((int)(short)FetchWord());
            var destination = ReadWord(unchecked((uint)(State.A[addressRegister] + displacement)));
            SetCompareFlags(destination, source, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.CmpiWordImmediateToAddressDisplacement);
        }

        private void ExecuteCmpiByteImmediateToAbsoluteLong()
        {
            BeginInstruction(0x0C39);
            _ = FetchWord();
            var source = (byte)FetchWord();
            var address = FetchLong();
            SetCompareFlags(ReadByte(address), source, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.CmpiByteImmediateToAbsoluteLong);
        }

        private void ExecuteCmpiWordImmediateToAbsoluteLong()
        {
            BeginInstruction(0x0C79);
            _ = FetchWord();
            var source = FetchWord();
            var address = FetchLong();
            var destination = ReadWord(address);
            SetCompareFlags(destination, source, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.CmpiWordImmediateToAbsoluteLong);
        }

        private void ExecuteCmpiLongImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = FetchLong();
            var destination = State.D[opcode & 7];
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpiLongImmediateToData);
        }

        private void ExecuteCmpiSmallImmediateToPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var word = (opcode & 0x40) != 0;
            var size = word ? M68kOperandSize.Word : M68kOperandSize.Byte;
            var source = FetchWord() & (word ? 0xFFFFu : 0xFFu);
            var register = opcode & 7;
            var destination = ReadSized(State.A[register], size);
            State.A[register] += word || register == 7 ? 2u : 1u;
            SetCompareFlags(destination, source, size);
            CompleteTiming(word ? M68kInstructionTimingKey.CmpiWordImmediateToPostIncrement : M68kInstructionTimingKey.CmpiByteImmediateToPostIncrement);
        }

        private void ExecuteCmpiLongImmediateToPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = FetchLong();
            var register = opcode & 7;
            var destination = ReadLong(State.A[register]);
            State.A[register] += 4;
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpiLongImmediateToPostIncrement);
        }

        private void ExecuteCmpiLongImmediateToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = FetchLong();
            var destination = ReadLong(State.A[opcode & 7]);
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpiLongImmediateToAddressIndirect);
        }

        private void ExecuteCmpiLongImmediateToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = FetchLong();
            var addressRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var destination = ReadLong(unchecked((uint)(State.A[addressRegister] + displacement)));
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpiLongImmediateToAddressDisplacement);
        }

        private void ExecuteCmpiLongImmediateToAbsoluteLong()
        {
            BeginInstruction(0x0CB9);
            _ = FetchWord();
            var source = FetchLong();
            var address = FetchLong();
            var destination = ReadLong(address);
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpiLongImmediateToAbsoluteLong);
        }

        private void ExecuteCmpiLongImmediateToAbsoluteWord()
        {
            BeginInstruction(0x0CB8);
            _ = FetchWord();
            var source = FetchLong();
            var address = unchecked((uint)(short)FetchWord());
            var destination = ReadLong(address);
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpiLongImmediateToAbsoluteWord);
        }

        private void ExecuteCmpaLongImmediateToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = FetchLong();
            var destination = State.A[(opcode >> 9) & 7];
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpaLongImmediateToAddress);
        }

        private void ExecuteCmpaWordImmediateToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = unchecked((uint)(int)(short)FetchWord());
            var destination = State.A[(opcode >> 9) & 7];
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpaWordImmediateToAddress);
        }

        private void ExecuteCmpaWordDataToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var source = M68kCpuState.SignExtend(State.D[sourceRegister], M68kOperandSize.Word);
            SetCompareFlags(State.A[destinationRegister], source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpaWordDataToAddress);
        }

        private void ExecuteCmpaWordAddressToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var source = M68kCpuState.SignExtend(State.A[sourceRegister], M68kOperandSize.Word);
            SetCompareFlags(State.A[destinationRegister], source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpaWordAddressToAddress);
        }

        private void ExecuteCmpaLongDataToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.A[(opcode >> 9) & 7];
            var source = State.D[opcode & 7];
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpaLongDataToAddress);
        }

        private void ExecuteCmpaLongAddressToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.A[(opcode >> 9) & 7];
            var source = State.A[opcode & 7];
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpaLongAddressToAddress);
        }

        private void ExecuteCmpaLongAddressIndirectToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.A[(opcode >> 9) & 7];
            var source = ReadLong(State.A[opcode & 7]);
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpaLongAddressIndirectToAddress);
        }

        private void ExecuteCmpaWordAddressDisplacementToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var displacement = unchecked((int)(short)FetchWord());
            var source = unchecked((uint)(int)(short)ReadWord(unchecked((uint)(State.A[opcode & 7] + displacement))));
            SetCompareFlags(State.A[(opcode >> 9) & 7], source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpaWordAddressDisplacementToAddress);
        }

        private void ExecuteCmpaLongAddressDisplacementToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.A[(opcode >> 9) & 7];
            var sourceRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var source = ReadLong(unchecked((uint)(State.A[sourceRegister] + displacement)));
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpaLongAddressDisplacementToAddress);
        }

        private void ExecuteCmpaLongPredecrementToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = opcode & 7;
            State.A[sourceRegister] = unchecked(State.A[sourceRegister] - 4);
            var source = ReadLong(State.A[sourceRegister]);
            var destination = State.A[(opcode >> 9) & 7];
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpaLongPredecrementToAddress);
        }

        private void ExecuteCmpaLongPostIncrementToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = opcode & 7;
            var sourceAddress = State.A[sourceRegister];
            var source = ReadLong(sourceAddress);
            State.A[sourceRegister] = unchecked(sourceAddress + 4);
            var destination = State.A[(opcode >> 9) & 7];
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpaLongPostIncrementToAddress);
        }

        private void ExecuteCmpLongDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.D[(opcode >> 9) & 7];
            var source = State.D[opcode & 7];
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpLongDataToData);
        }

        private void ExecuteCmpLongImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.D[(opcode >> 9) & 7];
            var source = FetchLong();
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpLongImmediateToData);
        }

        private void ExecuteCmpAbsoluteLongToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.D[(opcode >> 9) & 7];
            var size = (opcode & 0x80) != 0 ? M68kOperandSize.Long : M68kOperandSize.Word;
            var source = ReadSized(FetchLong(), size);
            SetCompareFlags(destination, source, size);
            CompleteTiming(size == M68kOperandSize.Long ? M68kInstructionTimingKey.CmpLongAbsoluteLongToData : M68kInstructionTimingKey.CmpWordAbsoluteLongToData);
        }

        private void ExecuteCmpLongAddressToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.D[(opcode >> 9) & 7];
            var source = State.A[opcode & 7];
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpLongAddressToData);
        }

        private void ExecuteCmpLongAddressIndirectToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.D[(opcode >> 9) & 7];
            var source = ReadLong(State.A[opcode & 7]);
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpLongAddressIndirectToData);
        }

        private void ExecuteCmpLongPostIncrementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.D[(opcode >> 9) & 7];
            var sourceRegister = opcode & 7;
            var address = State.A[sourceRegister];
            var source = ReadLong(address);
            WriteGeneralRegister(true, sourceRegister, address + 4);
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpLongPostIncrementToData);
        }

        private void ExecuteCmpLongAddressDisplacementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var source = ReadLong(unchecked((uint)(State.A[sourceRegister] + displacement)));
            SetCompareFlags(State.D[destinationRegister], source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpLongAddressDisplacementToData);
        }

        private void ExecuteCmpByteDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.D[(opcode >> 9) & 7] & 0xFF;
            var source = State.D[opcode & 7] & 0xFF;
            SetCompareFlags(destination, source, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.CmpByteDataToData);
        }

        private void ExecuteCmpByteImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.D[(opcode >> 9) & 7] & 0xFF;
            var source = FetchWord() & 0xFFu;
            SetCompareFlags(destination, source, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.CmpByteImmediateToData);
        }

        private void ExecuteCmpByteAddressIndirectToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.D[(opcode >> 9) & 7] & 0xFF;
            var source = ReadByte(State.A[opcode & 7]);
            SetCompareFlags(destination, source, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.CmpByteAddressIndirectToData);
        }

        private void ExecuteCmpBytePostIncrementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.D[(opcode >> 9) & 7] & 0xFF;
            var sourceRegister = opcode & 7;
            var sourceAddress = State.A[sourceRegister];
            var source = ReadByte(sourceAddress);
            WriteGeneralRegister(true, sourceRegister, sourceAddress + (sourceRegister == 7 ? 2u : 1u));
            SetCompareFlags(destination, source, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.CmpBytePostIncrementToData);
        }

        private void ExecuteCmpByteAddressDisplacementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.D[(opcode >> 9) & 7] & 0xFF;
            var sourceRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var source = ReadByte(unchecked((uint)(State.A[sourceRegister] + displacement)));
            SetCompareFlags(destination, source, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.CmpByteAddressDisplacementToData);
        }

        private void ExecuteCmpByteAbsoluteLongToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.D[(opcode >> 9) & 7] & 0xFF;
            var address = FetchLong();
            var source = ReadByte(address);
            SetCompareFlags(destination, source, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.CmpByteAbsoluteLongToData);
        }

        private void ExecuteCmpWordDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.D[(opcode >> 9) & 7] & 0xFFFF;
            var source = State.D[opcode & 7] & 0xFFFF;
            SetCompareFlags(destination, source, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.CmpWordDataToData);
        }

        private void ExecuteCmpWordAddressToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var destination = (ushort)State.D[destinationRegister];
            var source = (ushort)State.A[sourceRegister];
            SetCompareFlags(destination, source, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.CmpWordAddressToData);
        }

        private void ExecuteCmpWordImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.D[(opcode >> 9) & 7] & 0xFFFF;
            var source = FetchWord();
            SetCompareFlags(destination, source, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.CmpWordImmediateToData);
        }

        private void ExecuteCmpWordAddressIndirectToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.D[(opcode >> 9) & 7] & 0xFFFF;
            var source = ReadWord(State.A[opcode & 7]);
            SetCompareFlags(destination, source, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.CmpWordAddressIndirectToData);
        }

        private void ExecuteCmpWordPostIncrementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.D[(opcode >> 9) & 7] & 0xFFFF;
            var sourceRegister = opcode & 7;
            var source = ReadWord(State.A[sourceRegister]);
            State.A[sourceRegister] = unchecked(State.A[sourceRegister] + 2u);
            SetCompareFlags(destination, source, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.CmpWordPostIncrementToData);
        }

        private void ExecuteCmpWordAddressDisplacementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.D[(opcode >> 9) & 7] & 0xFFFF;
            var sourceRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var source = ReadWord(unchecked((uint)(State.A[sourceRegister] + displacement)));
            SetCompareFlags(destination, source, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.CmpWordAddressDisplacementToData);
        }

        private void ExecuteCmpmPostIncrement(ushort opcode, M68kOperandSize size, M68kInstructionTimingKey timingKey)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var source = ReadSized(State.A[sourceRegister], size);
            State.A[sourceRegister] = unchecked(State.A[sourceRegister] +
                (size == M68kOperandSize.Byte && sourceRegister == 7 ? 2u : (uint)size));
            var destination = ReadSized(State.A[destinationRegister], size);
            State.A[destinationRegister] = unchecked(State.A[destinationRegister] +
                (size == M68kOperandSize.Byte && destinationRegister == 7 ? 2u : (uint)size));
            SetCompareFlags(destination, source, size);
            CompleteTiming(timingKey);
        }

        private void ExecuteWordBranch(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var branchBase = State.ProgramCounter;
            var condition = (opcode >> 8) & 0x0F;
            var displacement = unchecked((int)(short)FetchWord());
            var returnAddress = State.ProgramCounter;

            if (condition == 0x1)
            {
                PushLong(returnAddress);
                State.ProgramCounter = unchecked((uint)(branchBase + displacement));
                CompleteTiming(M68kInstructionTimingKey.BsrWord);
                return;
            }

            if (CheckCondition(condition))
            {
                var target = unchecked((uint)(branchBase + displacement));
                _instructionFrequency.RecordTakenBranch(State.LastInstructionProgramCounter, opcode, target, 4);
                State.ProgramCounter = target;
                CompleteTiming(M68kInstructionTimingKey.BranchWordTaken);
                return;
            }

            CompleteTiming(M68kInstructionTimingKey.BranchWordNotTaken);
        }

        private void ExecuteLongBranch(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var branchBase = State.ProgramCounter;
            var condition = (opcode >> 8) & 0x0F;
            var displacement = unchecked((int)FetchLong());
            var returnAddress = State.ProgramCounter;

            if (condition == 0x1)
            {
                PushLong(returnAddress);
                State.ProgramCounter = unchecked((uint)(branchBase + displacement));
                CompleteTiming(M68kInstructionTimingKey.BsrLong);
                return;
            }

            if (CheckCondition(condition))
            {
                var target = unchecked((uint)(branchBase + displacement));
                _instructionFrequency.RecordTakenBranch(State.LastInstructionProgramCounter, opcode, target, 6);
                State.ProgramCounter = target;
                CompleteTiming(M68kInstructionTimingKey.BranchLongTaken);
                return;
            }

            CompleteTiming(M68kInstructionTimingKey.BranchLongNotTaken);
        }

        private void ExecuteSccAddressMemory(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var mode = (opcode >> 3) & 7;
            var address = State.A[register];
            if (mode == 4)
            {
                address = unchecked(address - M68kIntegerSemantics.AddressIncrement(register, M68kOperandSize.Byte));
                WriteGeneralRegister(true, register, address);
            }
            else if (mode == 5) address = unchecked(address + (uint)(int)(short)FetchWord());
            else if (mode == 6) address = CalculateBriefIndexedAddress(register, FetchWord(), opcode);
            WriteByte(address, CheckCondition((opcode >> 8) & 15) ? (byte)0xFF : (byte)0);
            if (mode == 3) WriteGeneralRegister(true, register, unchecked(address + M68kIntegerSemantics.AddressIncrement(register, M68kOperandSize.Byte)));
            CompleteTiming(mode switch
            {
                2 => M68kInstructionTimingKey.SccAddressIndirect,
                3 => M68kInstructionTimingKey.SccPostIncrement,
                4 => M68kInstructionTimingKey.SccPredecrement,
                5 => M68kInstructionTimingKey.SccAddressDisplacement,
                _ => M68kInstructionTimingKey.SccBriefIndexed
            });
        }

        private void ExecuteSccAbsoluteWord(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var condition = (opcode >> 8) & 0x0F;
            var address = unchecked((uint)(int)(short)FetchWord());
            WriteByte(address, CheckCondition(condition) ? (byte)0xFF : (byte)0x00);
            CompleteTiming(M68kInstructionTimingKey.SccAbsoluteWord);
        }

        private void ExecuteSccAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var condition = (opcode >> 8) & 0x0F;
            var address = FetchLong();
            WriteByte(address, CheckCondition(condition) ? (byte)0xFF : (byte)0x00);
            CompleteTiming(M68kInstructionTimingKey.SccAbsoluteLong);
        }

        private void ExecuteSccData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var condition = (opcode >> 8) & 0x0F;
            WriteDataRegisterByte(opcode & 7, CheckCondition(condition) ? (byte)0xFF : (byte)0x00);
            CompleteTiming(M68kInstructionTimingKey.SccData);
        }

        private void ExecuteDbcc(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var branchBase = State.ProgramCounter;
            var condition = (opcode >> 8) & 0x0F;
            var displacement = unchecked((int)(short)FetchWord());
            if (CheckCondition(condition))
            {
                CompleteTiming(M68kInstructionTimingKey.DbccConditionTrue);
                return;
            }

            var register = opcode & 7;
            var counter = (ushort)(State.D[register] - 1);
            State.D[register] = (State.D[register] & 0xFFFF_0000u) | counter;
            if (counter != 0xFFFF)
            {
                var target = unchecked((uint)(branchBase + displacement));
                _instructionFrequency.RecordTakenBranch(State.LastInstructionProgramCounter, opcode, target, 4);
                State.ProgramCounter = target;
                CompleteTiming(M68kInstructionTimingKey.DbccBranchTaken);
                return;
            }

            CompleteTiming(M68kInstructionTimingKey.DbccExpired);
        }

        private void ExecuteTrapcc(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            switch (opcode & 0x0003)
            {
                case 0x0002:
                    _ = FetchWord();
                    break;
                case 0x0003:
                    _ = FetchLong();
                    break;
            }

            if (CheckCondition((opcode >> 8) & 0x0F))
            {
                RaiseFormat0Exception(7, State.ProgramCounter, M68kInstructionTimingKey.IllegalInstruction);
                return;
            }

            CompleteTiming(M68kInstructionTimingKey.Nop);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void BeginInstruction(ushort opcode)
        {
            State.LastInstructionProgramCounter = State.ProgramCounter;
            State.LastOpcode = opcode;
            if (_instructionFrequency.Enabled)
            {
                _instructionFrequency.Record(State.LastInstructionProgramCounter, opcode);
            }
        }

        internal virtual void RaiseFormat0Exception(int vector, uint stackedProgramCounter, M68kInstructionTimingKey timingKey)
        {
            var savedStatusRegister = State.StatusRegister;
            State.RecordException(vector, stackedProgramCounter, savedStatusRegister);
            State.StatusRegister = (ushort)((State.StatusRegister | M68kCpuState.Supervisor) & ~M68kCpuState.Master);
            PushWord((ushort)(Format0ExceptionFrame | ((vector * 4) & 0x0FFF)));
            PushLong(stackedProgramCounter);
            PushWord(savedStatusRegister);
            State.ProgramCounter = ReadLong(State.VectorBaseRegister + ((uint)vector * 4));
            CompleteTiming(timingKey);
        }

        protected virtual bool TryRaiseMisalignedWordDataRead(uint address, uint instructionPc)
        {
            _ = address;
            _ = instructionPc;
            return false;
        }

        // M68000PRM 6-3 and 6-8. Data writes are currently write-through in the
        // bounded cache policy, so CPUSH has no deferred dirty bytes to emit.
        protected bool TryExecuteCacheMaintenance(ushort opcode)
        {
            if ((opcode & 0xFF00) != 0xF400) return false;
            BeginInstruction(opcode);
            var pc = State.ProgramCounter;
            _ = FetchWord();
            var scope = (opcode >> 3) & 3;
            if (scope == 0)
            {
                RaiseFormat0Exception(4, pc, M68kInstructionTimingKey.IllegalInstruction);
                return true;
            }
            if ((State.StatusRegister & M68kCpuState.Supervisor) == 0)
            {
                RaiseFormat0Exception(8, pc, M68kInstructionTimingKey.PrivilegeViolation);
                return true;
            }
            var caches = (opcode >> 6) & 3;
            var length = scope == 1 ? 16u : (State.M68040Mmu.TranslationControl & 0x4000) != 0 ? 8192u : 4096u;
            var address = State.A[opcode & 7] & ~(length - 1);
            if ((caches & 2) != 0)
            {
                Invalidate(_timing.InstructionCache);
                _timedBus.ResetInstructionFetchBuffer();
                _instructionPipe.Reset(State.ProgramCounter);
                if (_hotBlocks is not null) Array.Clear(_hotBlocks);
            }
            var retainData = _profile.Model == M68kAcceleratorModel.M68060 &&
                (opcode & 0x20) != 0 && (State.CacheControlRegister & 0x1000_0000) != 0;
            if (!retainData && (caches & 1) != 0 && _timing.DataCache is { } dataCache) Invalidate(dataCache);
            CompleteTiming(M68kInstructionTimingKey.Nop); // Approximate fixed instruction policy.
            return true;

            void Invalidate(M68kInstructionCache cache)
            {
                if (scope == 3) cache.Clear();
                else for (uint offset = 0; offset < length; offset += (uint)cache.LineSize)
                    cache.ClearEntry(address + offset);
            }
        }

        protected virtual bool TryReadControlRegister(int register, uint instructionPc, out uint value)
        {
            switch (register)
            {
                case 0x000:
                    value = State.SourceFunctionCode;
                    return true;
                case 0x001:
                    value = State.DestinationFunctionCode;
                    return true;
                case 0x002:
                    value = State.CacheControlRegister;
                    return true;
                case 0x801:
                    value = State.VectorBaseRegister;
                    return true;
                case 0x802:
                    value = State.CacheAddressRegister;
                    return true;
                case 0x803:
                    value = State.MasterStackPointer;
                    return true;
                case 0x804:
                    value = State.InterruptStackPointer;
                    return true;
                default:
                    value = RaiseIllegalControlRegister(instructionPc);
                    return false;
            }
        }

        protected virtual bool TryWriteControlRegister(int register, uint value, uint instructionPc)
        {
            switch (register)
            {
                case 0x000:
                    State.SourceFunctionCode = value & 0x7;
                    return true;
                case 0x001:
                    State.DestinationFunctionCode = value & 0x7;
                    return true;
                case 0x002:
                    State.CacheControlRegister = value;
                    _timedBus.ResetInstructionFetchBuffer();
                    _timing.ApplyCacheControl(State.CacheControlRegister, State.CacheAddressRegister);
                    return true;
                case 0x801:
                    State.VectorBaseRegister = value;
                    return true;
                case 0x802:
                    State.CacheAddressRegister = value;
                    _timing.ApplyCacheControl(State.CacheControlRegister, State.CacheAddressRegister);
                    return true;
                case 0x803:
                    State.SetMasterStackPointer(value);
                    return true;
                case 0x804:
                    State.SetInterruptStackPointer(value);
                    return true;
                default:
                    _ = RaiseIllegalControlRegister(instructionPc);
                    return false;
            }
        }

        protected uint RaiseIllegalControlRegister(uint instructionPc)
        {
            RaiseFormat0Exception(4, instructionPc, M68kInstructionTimingKey.IllegalInstruction);
            return 0;
        }

        protected uint ReadGeneralRegister(bool addressRegister, int register)
            => addressRegister ? State.A[register] : State.D[register];

        protected void WriteGeneralRegister(bool addressRegister, int register, uint value)
        {
            if (addressRegister)
            {
                if (register == 7)
                {
                    State.SetActiveStackPointer(value);
                }
                else
                {
                    State.A[register] = value;
                }
            }
            else
            {
                State.D[register] = value;
            }
        }

        protected ushort FetchWord()
        {
            var address = State.ProgramCounter;
            if (!_instructionPipe.TryConsumeHead(address, out var value, out var metadata) &&
                !_instructionPipe.TryConsume(address, out value, out metadata))
            {
                _instructionPipe.Reset(address);
                AppendInstructionPipeWord(address);
                if (!_instructionPipe.TryConsumeHead(address, out value, out metadata))
                {
                    throw new InvalidOperationException("Unable to consume the MC68020/030 instruction pipe head.");
                }
            }

            if (metadata.RequiresSynchronization)
            {
                _timing.SynchronizeNativeToBus();
            }

            State.ProgramCounter += 2;
            return value;
        }

        private void RefillInstructionPipe()
        {
            if ((State.ProgramCounter & 1) != 0)
            {
                _instructionPipe.Reset(State.ProgramCounter);
                return;
            }

            if (!_instructionPipe.IsEmpty && !_instructionPipe.Contains(State.ProgramCounter))
            {
                _instructionPipe.Reset(State.ProgramCounter);
            }

            var canReadAhead = CanReadAheadInstructionWords();
            if (_instructionPipe.IsEmpty)
            {
                return;
            }

            var targetCount = canReadAhead ? M68kAdvancedInstructionPipe.Capacity : 1;
            var constrainToCurrentCacheLine =
                _timing.InstructionCache.Enabled &&
                _profile.IsInstructionCacheableAddress(State.ProgramCounter);
            var currentCacheLine = State.ProgramCounter & ~3u;
            while (_instructionPipe.Count < targetCount)
            {
                if (constrainToCurrentCacheLine &&
                    _instructionPipe.Count != 0 &&
                    (_instructionPipe.NextAddress & ~3u) != currentCacheLine)
                {
                    break;
                }

                if (!AppendInstructionPipeWord(_instructionPipe.NextAddress))
                {
                    break;
                }
            }
        }

        private bool AppendInstructionPipeWord(uint address)
        {
            if ((address & 1) != 0)
            {
                return false;
            }

            var word = _timedBus.ReadInstructionFetchWordHot(
                address,
                out var cacheHit,
                out var requiresSynchronization,
                out var completedMachineCycle);
            var metadata = new M68kInstructionFetchMetadata(
                cacheHit,
                requiresSynchronization,
                completedMachineCycle,
                State.NativeCycles);
            _instructionPipe.Append(address, word, metadata);
            return cacheHit;
        }

        private bool CanReadAheadInstructionWords()
        {
            if (_codeReader is null ||
                !_timing.InstructionCache.Enabled ||
                !_profile.IsInstructionCacheableAddress(State.ProgramCounter))
            {
                return false;
            }

            var target = _profile.GetBusTimingRule(State.ProgramCounter).Target;
            if (target is
                (M68020MemoryTarget.CustomRegisters or
                 M68020MemoryTarget.Cia or
                 M68020MemoryTarget.HostTrap or
                 M68020MemoryTarget.Unmapped))
            {
                return false;
            }

            return target is not
                (M68020MemoryTarget.CustomRegisters or
                 M68020MemoryTarget.Cia or
                 M68020MemoryTarget.HostTrap or
                 M68020MemoryTarget.Unmapped);
        }

        protected uint FetchLong()
        {
            var high = FetchWord();
            var low = FetchWord();
            return ((uint)high << 16) | low;
        }

        protected byte ReadByte(uint address, M68kBusAccessKind accessKind = M68kBusAccessKind.CpuDataRead)
        {
            return _timedBus.ReadByte(address, accessKind);
        }

        protected ushort ReadWord(uint address, M68kBusAccessKind accessKind = M68kBusAccessKind.CpuDataRead)
        {
            return _timedBus.ReadWord(address, accessKind);
        }

        protected uint ReadLong(uint address)
        {
            return _timedBus.ReadLong(address, M68kBusAccessKind.CpuDataRead);
        }

        protected void WriteByte(uint address, byte value)
        {
            _timedBus.WriteByte(address, value, M68kBusAccessKind.CpuDataWrite);
        }

        protected void WriteWord(uint address, ushort value)
        {
            _timedBus.WriteWord(address, value, M68kBusAccessKind.CpuDataWrite);
        }

        protected void WriteLong(uint address, uint value)
        {
            _timedBus.WriteLong(address, value, M68kBusAccessKind.CpuDataWrite);
        }

        protected void PushWord(ushort value)
        {
            State.SetActiveStackPointer(State.A[7] - 2);
            WriteWord(State.A[7], value);
        }

        protected void PushLong(uint value)
        {
            State.SetActiveStackPointer(State.A[7] - 4);
            WriteLong(State.A[7], value);
        }

        protected uint PullLong()
        {
            var value = ReadLong(State.A[7]);
            State.SetActiveStackPointer(State.A[7] + 4);
            return value;
        }

        internal void CompleteTiming(M68kInstructionTimingKey key)
        {
            CompleteTimingPlan(_timing.GetPlan(key));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void CompleteFastTiming(M68kInstructionTimingKey key)
        {
            var plan = _timing.GetPlan(key);
            _timing.CompleteFlatInstruction(plan);
            if (_instructionPipe.IsEmpty && !_timing.InstructionCache.Enabled)
            {
                return;
            }

            CompleteInstructionPipeAfterTiming(plan);
        }

        private void CompleteOperandShapeTiming(M68kInstructionTimingKey key, string name)
        {
            var plan = M68kTimingFormula.CreateOperandShapePlan(
                key,
                name,
                _profile.Model,
                _profile.FixedInstructionNativeCycles);
            CompleteTimingPlan(plan);
        }

        private void CompleteMovemLongTiming(
            M68kInstructionTimingKey key,
            string name,
            int registerCount,
            bool registerToMemory)
        {
            var plan = M68kTimingFormula.CreateMovemLongPlan(
                key,
                name,
                registerCount,
                registerToMemory,
                _profile.Model,
                _profile.FixedInstructionNativeCycles);
            CompleteTimingPlan(plan);
        }

        private void CompleteMovemWordTiming(
            M68kInstructionTimingKey key,
            string name,
            int registerCount,
            int effectiveAddressCycles = 0,
            bool memoryToRegister = false)
        {
            var nativeCycles = _profile.FixedInstructionNativeCycles ??
                8 + (memoryToRegister ? 4 : 0) + effectiveAddressCycles + (2 * registerCount);
            var plan = _profile.Model == M68kAcceleratorModel.M68030
                ? M68kInstructionPlan.CreateHeadTail(
                    key,
                    name,
                    nativeCycles,
                    headCycles: 2,
                    tailCycles: 0)
                : M68kInstructionPlan.CreateFlat(key, name, nativeCycles);
            CompleteTimingPlan(plan);
        }

        private void CompleteTimingPlan(M68kInstructionPlan plan)
        {
            _timing.CompleteInstruction(plan);
            CompleteInstructionPipeAfterTiming(plan);
        }

        private void CompleteInstructionPipeAfterTiming(M68kInstructionPlan plan)
        {
            if ((plan.Barriers &
                (M68kTimingBarrier.FlushPipeline |
                 M68kTimingBarrier.Exception |
                 M68kTimingBarrier.Branch |
                 M68kTimingBarrier.CacheControl)) != 0)
            {
                _instructionPipe.Reset(State.ProgramCounter);
                return;
            }

            RefillInstructionPipe();
        }

        private static int CountSetBits(ushort value)
            => M68kIntegerSemantics.CountSetBits(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetCompareFlags(uint destination, uint source, M68kOperandSize size)
        {
            var arithmetic = M68kIntegerSemantics.Subtract(destination, source, size);
            State.SetNegativeZero(arithmetic.Value, size);
            State.SetFlag(M68kCpuState.Overflow, arithmetic.Overflow);
            State.SetFlag(M68kCpuState.Carry, arithmetic.Carry);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetAddFlags(uint destination, uint source, uint result, M68kOperandSize size)
        {
            var arithmetic = M68kIntegerSemantics.CalculateAddFlags(destination, source, result, size);
            State.SetNegativeZero(result, size);
            State.SetFlag(M68kCpuState.Overflow, arithmetic.Overflow);
            State.SetFlag(M68kCpuState.Carry, arithmetic.Carry);
            State.SetFlag(M68kCpuState.Extend, arithmetic.Carry);
        }

        private void SetAddxFlags(uint destination, uint source, uint result, M68kOperandSize size)
        {
            var extend = State.GetFlag(M68kCpuState.Extend) ? 1u : 0u;
            var arithmetic = M68kIntegerSemantics.CalculateAddFlags(destination, source, result, size, extend);
            if (result != 0)
            {
                State.SetFlag(M68kCpuState.Zero, false);
            }

            State.SetFlag(M68kCpuState.Negative, (result & M68kCpuState.SignBit(size)) != 0);
            State.SetFlag(M68kCpuState.Overflow, arithmetic.Overflow);
            State.SetFlag(M68kCpuState.Carry, arithmetic.Carry);
            State.SetFlag(M68kCpuState.Extend, arithmetic.Carry);
        }

        private void SetSubxFlags(uint destination, uint source, uint result, M68kOperandSize size)
        {
            var extend = State.GetFlag(M68kCpuState.Extend) ? 1u : 0u;
            var arithmetic = M68kIntegerSemantics.CalculateSubtractFlags(destination, source, result, size, extend);
            if (result != 0)
            {
                State.SetFlag(M68kCpuState.Zero, false);
            }

            State.SetFlag(M68kCpuState.Negative, (result & M68kCpuState.SignBit(size)) != 0);
            State.SetFlag(M68kCpuState.Overflow, arithmetic.Overflow);
            State.SetFlag(M68kCpuState.Carry, arithmetic.Carry);
            State.SetFlag(M68kCpuState.Extend, arithmetic.Carry);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetSubtractFlags(uint destination, uint source, uint result, M68kOperandSize size)
        {
            var arithmetic = M68kIntegerSemantics.CalculateSubtractFlags(destination, source, result, size);
            State.SetNegativeZero(arithmetic.Value, size);
            State.SetFlag(M68kCpuState.Overflow, arithmetic.Overflow);
            State.SetFlag(M68kCpuState.Carry, arithmetic.Carry);
            State.SetFlag(M68kCpuState.Extend, arithmetic.Carry);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetMoveFlags(uint value, M68kOperandSize size)
        {
            State.SetNegativeZero(value, size);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetFastLongAddFlags(uint destination, uint source, uint result)
        {
            var overflow = ((~(destination ^ source) & (destination ^ result) & 0x8000_0000u) != 0);
            State.SetLongArithmeticFlags(result, overflow, result < destination);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetFastLongSubtractFlags(uint destination, uint source, uint result)
        {
            var overflow = (((destination ^ source) & (destination ^ result) & 0x8000_0000u) != 0);
            State.SetLongArithmeticFlags(result, overflow, source > destination);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetFastLongCompareFlags(uint destination, uint source, uint result)
        {
            var overflow = (((destination ^ source) & (destination ^ result) & 0x8000_0000u) != 0);
            State.SetLongCompareFlags(result, overflow, source > destination);
        }

        private void SetBcdFlags(byte result, bool carry)
        {
            if (result != 0)
            {
                State.SetFlag(M68kCpuState.Zero, false);
            }

            State.SetFlag(M68kCpuState.Negative, (result & 0x80) != 0);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, carry);
            State.SetFlag(M68kCpuState.Extend, carry);
        }

        private void WriteDataRegisterByte(int register, byte value)
        {
            State.D[register] = (State.D[register] & 0xFFFF_FF00u) | value;
        }

        private void WriteDataRegisterWord(int register, ushort value)
        {
            State.D[register] = (State.D[register] & 0xFFFF_0000u) | value;
        }

        private uint ReadLongDataSource(int mode, int register, ushort opcode)
        {
            return mode switch
            {
                0 => State.D[register],
                2 => ReadLong(State.A[register]),
                3 => ReadLongPostIncrement(register),
                4 => ReadLongPredecrement(register),
                5 => ReadLong(unchecked((uint)(State.A[register] + unchecked((int)(short)FetchWord())))),
                6 => ReadLong(CalculateBriefIndexedAddress(register, FetchWord(), opcode)),
                7 => ReadLongExtendedSource(register, opcode),
                _ => throw new UnsupportedM68kTimingException(opcode, State.LastInstructionProgramCounter, _profile)
            };
        }

        private bool TryReadWordDataSource(int mode, int register, ushort opcode, out ushort value)
        {
            switch (mode)
            {
                case 0:
                    value = (ushort)State.D[register];
                    return true;
                case 2:
                    value = ReadWord(State.A[register]);
                    return true;
                case 3:
                {
                    var address = State.A[register];
                    value = ReadWord(address);
                    WriteGeneralRegister(true, register, address + M68kIntegerSemantics.AddressIncrement(register, M68kOperandSize.Word));
                    return true;
                }
                case 4:
                {
                    var address = State.A[register] - M68kIntegerSemantics.AddressIncrement(register, M68kOperandSize.Word);
                    WriteGeneralRegister(true, register, address);
                    value = ReadWord(address);
                    return true;
                }
                case 5:
                {
                    var displacement = unchecked((int)(short)FetchWord());
                    value = ReadWord(unchecked((uint)(State.A[register] + displacement)));
                    return true;
                }
                case 6:
                {
                    var extension = FetchWord();
                    value = ReadWord(CalculateBriefIndexedAddress(register, extension, opcode));
                    return true;
                }
                case 7:
                    return TryReadWordExtendedSource(register, opcode, out value);
                default:
                    value = 0;
                    RaiseFormat0Exception(4, State.LastInstructionProgramCounter, M68kInstructionTimingKey.IllegalInstruction);
                    return false;
            }
        }

        private bool TryReadWordExtendedSource(int register, ushort opcode, out ushort value)
        {
            switch (register)
            {
                case 0:
                    value = ReadWord(unchecked((uint)(int)(short)FetchWord()));
                    return true;
                case 1:
                    value = ReadWord(FetchLong());
                    return true;
                case 2:
                {
                    var extensionAddress = State.ProgramCounter;
                    var displacement = unchecked((int)(short)FetchWord());
                    value = ReadWord(unchecked((uint)(extensionAddress + displacement)));
                    return true;
                }
                case 3:
                {
                    var extensionAddress = State.ProgramCounter;
                    var extension = FetchWord();
                    value = ReadWord(CalculateBriefIndexedAddress(extensionAddress, extension, opcode));
                    return true;
                }
                case 4:
                    value = FetchWord();
                    return true;
                default:
                    value = 0;
                    RaiseFormat0Exception(4, State.LastInstructionProgramCounter, M68kInstructionTimingKey.IllegalInstruction);
                    return false;
            }
        }

        private uint ReadLongPostIncrement(int register)
        {
            var address = State.A[register];
            var value = ReadLong(address);
            WriteGeneralRegister(true, register, address + 4);
            return value;
        }

        private uint ReadLongPredecrement(int register)
        {
            WriteGeneralRegister(true, register, State.A[register] - 4);
            return ReadLong(State.A[register]);
        }

        private uint ReadLongExtendedSource(int register, ushort opcode)
        {
            switch (register)
            {
                case 0:
                    return ReadLong(unchecked((uint)(short)FetchWord()));
                case 1:
                    return ReadLong(FetchLong());
                case 2:
                {
                    var extensionAddress = State.ProgramCounter;
                    var displacement = unchecked((int)(short)FetchWord());
                    return ReadLong(unchecked((uint)(extensionAddress + displacement)));
                }
                case 3:
                {
                    var extensionAddress = State.ProgramCounter;
                    var extension = FetchWord();
                    return ReadLong(CalculateBriefIndexedAddress(extensionAddress, extension, opcode));
                }
                case 4:
                    return FetchLong();
                default:
                    throw new UnsupportedM68kTimingException(opcode, State.LastInstructionProgramCounter, _profile);
            }
        }

        private void ExecuteMoveWordBriefIndexedToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var extension = FetchWord();
            var address = CalculateIndexedOperandAddress(State.A[opcode & 7], extension, opcode);
            var value = unchecked((uint)(int)(short)ReadWord(address));
            WriteGeneralRegister(true, (opcode >> 9) & 7, value);
            CompleteIndexedMoveTiming(M68kInstructionTimingKey.MoveWordBriefIndexedToAddress, extension);
        }

        private uint CalculateIndexedOperandAddress(uint baseAddress, ushort extension, ushort opcode)
        {
            if ((extension & 0x0100) == 0)
                return CalculateBriefIndexedAddress(baseAddress, extension, opcode);

            // M68000PM 2-2/2-4: validate reserved fields before consuming displacement words.
            var baseSize = (extension >> 4) & 3;
            var indirect = extension & 7;
            var suppressIndex = (extension & 0x40) != 0;
            if (baseSize == 0 || (extension & 8) != 0 || indirect == 4 || (suppressIndex && indirect >= 4))
                throw new UnsupportedM68kTimingException(opcode, State.LastInstructionProgramCounter, _profile);

            var index = suppressIndex ? 0u : M68kIntegerSemantics.CalculateM68020BriefIndexedIndexValue(extension, State.D, State.A);
            var baseDisplacement = baseSize == 2 ? unchecked((uint)(int)(short)FetchWord()) : baseSize == 3 ? FetchLong() : 0u;
            var outerSize = indirect & 3;
            var outerDisplacement = outerSize == 2 ? unchecked((uint)(int)(short)FetchWord()) : outerSize == 3 ? FetchLong() : 0u;
            var address = unchecked(((extension & 0x80) != 0 ? 0u : baseAddress) + baseDisplacement);
            if (indirect == 0) return unchecked(address + index);
            // Both levels use ordinary timed data reads on the host's single bus clock.
            var pointer = ReadLong(unchecked(address + (indirect < 4 ? index : 0u)));
            return unchecked(pointer + outerDisplacement + (indirect >= 5 ? index : 0u));
        }

        private void CompleteIndexedMoveTiming(M68kInstructionTimingKey briefKey, ushort extension, bool memoryDestination = false, int destinationExtraCycles = 0)
        {
            if ((extension & 0x0100) == 0)
            {
                CompleteTiming(briefKey);
                return;
            }

            // MC68020UM 8.2.6, cache-case MOVE/MOVEA source -> Rn, printed 8-23.
            // Retained as an approximate instruction policy; bus waits and 030 overlap are separate.
            var baseSize = (extension >> 4) & 3;
            var indirect = extension & 7;
            var nativeCycles = _profile.FixedInstructionNativeCycles ??
                9 + (baseSize == 2 ? 2 : baseSize == 3 ? 6 : 0) +
                (indirect == 0 ? 0 : 5 + ((indirect & 3) >= 2 ? 2 : 0)) + (memoryDestination ? 1 + destinationExtraCycles : 0);
            var key = memoryDestination ? M68kInstructionTimingKey.FullIndexedMoveToMemory : M68kInstructionTimingKey.FullIndexedMoveToRegister;
            var label = memoryDestination ? "MOVE <full-indexed>,memory" : "MOVE/MOVEA <full-indexed>,Rn";
            var plan = _profile.Model == M68kAcceleratorModel.M68030
                ? M68kInstructionPlan.CreateHeadTail(key, label, nativeCycles, headCycles: 2, tailCycles: 0)
                : M68kInstructionPlan.CreateFlat(key, label, nativeCycles);
            CompleteTimingPlan(plan);
        }

        private void CompleteIndexedCalculationTiming(M68kInstructionTimingKey briefKey, ushort extension,
            M68kInstructionTimingKey fullKey, string label, int operationCycles, M68kTimingBarrier barriers = M68kTimingBarrier.None)
        {
            if ((extension & 0x100) == 0) { CompleteTiming(briefKey); return; }
            // MC68020UM 8.2.3 calculate EA plus the instruction's cache-case cost (8.2.11/8.2.16).
            var baseSize = (extension >> 4) & 3;
            var indirect = extension & 7;
            var cycles = _profile.FixedInstructionNativeCycles ??
                6 + operationCycles + (baseSize == 2 ? 2 : baseSize == 3 ? 6 : 0) +
                (indirect == 0 ? 0 : 5 + ((indirect & 3) >= 2 ? 2 : 0));
            var plan = _profile.Model == M68kAcceleratorModel.M68030
                ? M68kInstructionPlan.CreateHeadTail(fullKey, label, cycles, headCycles: 2, tailCycles: 0, barriers)
                : M68kInstructionPlan.CreateFlat(fullKey, label, cycles, barriers);
            CompleteTimingPlan(plan);
        }

        private void CompleteIndexedRegisterStoreTiming(M68kInstructionTimingKey briefKey, ushort extension)
        {
            if ((extension & 0x100) == 0) { CompleteTiming(briefKey); return; }
            // MC68020UM 8.2.6 cache-case Rn -> full indexed destination, printed 8-24/8-25.
            var baseSize = (extension >> 4) & 3;
            var indirect = extension & 7;
            var nativeCycles = _profile.FixedInstructionNativeCycles ??
                8 + (baseSize == 2 ? 2 : baseSize == 3 ? 6 : 0) +
                (indirect == 0 ? 0 : 4 + ((indirect & 3) == 3 ? 3 : (indirect & 3) == 2 ? 2 : 0));
            var plan = _profile.Model == M68kAcceleratorModel.M68030
                ? M68kInstructionPlan.CreateHeadTail(M68kInstructionTimingKey.FullIndexedRegisterToMemory,
                    "MOVE Rn,<full-indexed>", nativeCycles, headCycles: 2, tailCycles: 0)
                : M68kInstructionPlan.CreateFlat(M68kInstructionTimingKey.FullIndexedRegisterToMemory,
                    "MOVE Rn,<full-indexed>", nativeCycles);
            CompleteTimingPlan(plan);
        }

        private uint CalculateBriefIndexedAddress(int baseRegister, ushort extension, ushort opcode)
            => CalculateBriefIndexedAddress(State.A[baseRegister], extension, opcode);

        private uint CalculateBriefIndexedAddress(uint baseAddress, ushort extension, ushort opcode)
        {
            if (!M68kIntegerSemantics.TryCalculateM68020BriefIndexedAddress(
                baseAddress,
                extension,
                State.D,
                State.A,
                out var address))
            {
                throw new UnsupportedM68kTimingException(opcode, State.LastInstructionProgramCounter, _profile);
            }

            return address;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SynchronizeNativeToMachine()
        {
            _timing.SynchronizeNativeToMachine();
        }

        private bool TryPeekOpcode(uint address, out ushort opcode)
        {
            opcode = 0;
            if ((address & 1) != 0)
            {
                return false;
            }

            if (!_instructionPipe.TryPeek(address, out opcode))
            {
                _instructionPipe.Reset(address);
                AppendInstructionPipeWord(address);
                if (CanReadAheadInstructionWords())
                {
                    RefillInstructionPipe();
                }

                return _instructionPipe.TryPeek(address, out opcode);
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CheckCondition(int condition)
            => M68kIntegerSemantics.EvaluateCondition(State.StatusRegister, condition);
    }
}
