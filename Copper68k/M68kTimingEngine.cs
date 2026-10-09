/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.CompilerServices;

namespace Copper68k
{
    internal enum M68kAcceleratorModel
    {
        M68020,
        M68030,
        M68040,
        M68060
    }

    [Flags]
    internal enum M68kTimingBarrier
    {
        None = 0,
        FlushPipeline = 1 << 0,
        SynchronizeBus = 1 << 1,
        Exception = 1 << 2,
        Branch = 1 << 3,
        CacheControl = 1 << 4,
        ReadModifyWrite = 1 << 5
    }

    internal enum M68kInstructionTimingKey
    {
        Idle,
        Nop,
        Reset,
        Stop,
        LineAException,
        LineFException,
        IllegalInstruction,
        PrivilegeViolation,
        FormatError,
        InterruptAcknowledge,
        Movec,
        MoveUsp,
        FpuMove,
        FpuMoveOut,
        FpuControlMove,
        FpuMovem,
        FpuAdd,
        FpuSubtract,
        FpuMultiply,
        FpuDivide,
        FpuSquareRoot,
        FpuAbsolute,
        FpuNegate,
        FpuCompare,
        FpuTest,
        FpuSave,
        FpuRestore,
        ImmediateWordToConditionCodeRegister,
        ImmediateWordToStatusRegister,
        Rte,
        Rtd,
        Rts,
        LinkLong,
        ExtbLong,
        ExtWordData,
        ExtLongData,
        TstByteData,
        TstBytePcDisplacement,
        TstWordPcDisplacement,
        TstLongPcDisplacement,
        TstWordData,
        TstWordAbsoluteLong,
        TstByteAbsoluteLong,
        TstWordAddressIndirect,
        TstByteAddressIndirect,
        TstWordPostIncrement,
        TstLongPostIncrement,
        TstBytePostIncrement,
        TstByteAddressDisplacement,
        TstByteBriefIndexed,
        TstWordBriefIndexed,
        TstWordAddressDisplacement,
        TstLongAddressIndirect,
        TstLongAddressDisplacement,
        TstLongBriefIndexed,
        Moveq,
        NegxLongData,
        NegByteData,
        NegLongData,
        NegWordData,
        NegByteAbsoluteLong,
        NegWordAbsoluteLong,
        NegLongAbsoluteLong,
        NotByteData,
        NotByteAddressDisplacement,
        NotLongAddressDisplacement,
        NotWordData,
        NotLongData,
        ClrDataLong,
        ClrDataByte,
        ClrDataWord,
        ClrLongAddressIndirect,
        ClrLongAddressDisplacement,
        ClrLongAbsoluteLong,
        ClrWordAbsoluteLong,
        ClrLongAbsoluteWord,
        ClrByteAddressIndirect,
        ClrWordAddressIndirect,
        ClrBytePostIncrement,
        ClrByteAddressDisplacement,
        ClrByteAbsoluteLong,
        ClrWordAddressDisplacement,
        ClrWordPostIncrement,
        ClrWordPredecrement,
        ClrBytePredecrement,
        ClrLongPostIncrement,
        ClrLongPredecrement,
        ClrByteBriefIndexed,
        ClrWordBriefIndexed,
        ClrLongBriefIndexed,
        LeaAbsoluteLong,
        LeaAddressIndirect,
        LeaAddressDisplacement,
        LeaBriefIndexed,
        MoveByteImmediateToAbsoluteLong,
        MoveWordImmediateToAbsoluteLong,
        MoveWordImmediateToAddressIndirect,
        MoveWordImmediateToBriefIndexed,
        MoveLongImmediateToAbsoluteLong,
        MoveLongImmediateToAddressIndirect,
        MoveLongImmediateToAddressDisplacement,
        MoveLongImmediateToPostIncrement,
        MoveLongImmediateToPredecrement,
        MoveLongImmediateToData,
        MoveLongImmediateToAddress,
        MoveWordImmediateToAddress,
        MoveWordStatusRegisterToAddressIndirect,
        MoveWordStatusRegisterToData,
        MoveWordDataToStatusRegister,
        MoveWordPostIncrementToStatusRegister,
        AndBytePostIncrementToData,
        AndWordPostIncrementToData,
        AndLongPostIncrementToData,
        OrByteAbsoluteLongToData,
        OrWordAbsoluteLongToData,
        MoveLongDataToData,
        MoveLongDataToAddress,
        MoveLongDataToAddressIndirect,
        MoveLongDataToPostIncrement,
        MoveLongDataToAddressDisplacement,
        MoveLongDataToBriefIndexed,
        MoveLongAddressToData,
        MoveLongAddressToAddress,
        MoveLongAddressToAddressIndirect,
        MoveLongAddressToBriefIndexed,
        MoveLongAddressToAddressDisplacement,
        MoveLongAddressToPostIncrement,
        MoveLongAddressIndirectToData,
        MoveLongPredecrementToData,
        MoveBytePredecrementToData,
        MoveWordPredecrementToData,
        MoveBytePredecrementToPredecrement,
        MoveWordPredecrementToPredecrement,
        MoveLongPredecrementToPredecrement,
        MoveLongAddressIndirectToAddress,
        MoveLongPostIncrementToData,
        MoveLongPostIncrementToAddress,
        MoveLongPredecrementToAddress,
        MoveLongPostIncrementToPostIncrement,
        MoveLongAddressDisplacementToData,
        MoveLongAddressDisplacementToAddress,
        MoveLongAddressDisplacementToAddressIndirect,
        MoveLongAddressDisplacementToAddressDisplacement,
        MoveLongPcDisplacementToAddressDisplacement,
        MoveLongAddressDisplacementToBriefIndexed,
        MoveByteAddressDisplacementToBriefIndexed,
        MoveByteAddressDisplacementToPostIncrement,
        MoveWordAddressDisplacementToPostIncrement,
        MoveLongAddressDisplacementToPostIncrement,
        MoveLongBriefIndexedToData,
        MoveLongBriefIndexedToAddress,
        MoveLongBriefIndexedToAddressDisplacement,
        MoveLongBriefIndexedToBriefIndexed,
        MoveByteBriefIndexedToBriefIndexed,
        MoveWordBriefIndexedToBriefIndexed,
        MoveByteBriefIndexedToPostIncrement,
        MoveWordBriefIndexedToPostIncrement,
        MoveLongBriefIndexedToPostIncrement,
        MoveByteBriefIndexedToAbsoluteLong,
        MoveWordBriefIndexedToAbsoluteLong,
        MoveLongBriefIndexedToAbsoluteLong,
        MoveLongBriefIndexedToPredecrement,
        MoveLongAddressIndirectToAddressIndirect,
        MoveLongAddressIndirectToPostIncrement,
        MoveByteAddressIndirectToBriefIndexed,
        MoveWordAddressIndirectToBriefIndexed,
        MoveLongAddressIndirectToBriefIndexed,
        MoveByteAbsoluteLongToBriefIndexed,
        MoveWordAbsoluteLongToBriefIndexed,
        MoveLongAbsoluteLongToBriefIndexed,
        MoveByteAddressIndirectToPostIncrement,
        MoveWordAddressIndirectToPostIncrement,
        MoveLongAbsoluteWordToData,
        MoveByteAbsoluteWordToAbsoluteWord,
        MoveWordAbsoluteWordToAbsoluteWord,
        MoveLongAbsoluteWordToAbsoluteWord,
        MoveByteAbsoluteWordToData,
        MoveWordAbsoluteWordToData,
        MoveLongAbsoluteWordToAddress,
        MoveLongAbsoluteLongToData,
        MoveLongAbsoluteLongToAddress,
        MoveLongAbsoluteLongToPredecrement,
        MoveLongAbsoluteWordToAbsoluteLong,
        MoveLongAbsoluteWordToAddressDisplacement,
        MoveByteAbsoluteWordToAddressDisplacement,
        MoveByteAbsoluteLongToAddressDisplacement,
        MoveWordAbsoluteWordToAddressDisplacement,
        MoveLongAbsoluteLongToAddressDisplacement,
        MoveLongDataToAbsoluteLong,
        MoveLongAddressToAbsoluteLong,
        MoveLongAddressIndirectToAbsoluteLong,
        MoveWordAddressIndirectToAbsoluteLong,
        MoveWordAbsoluteLongToPostIncrement,
        MoveByteAbsoluteLongToPostIncrement,
        MoveLongAbsoluteLongToPostIncrement,
        MoveWordAddressToPostIncrement,
        MoveWordPcDisplacementToAbsoluteLong,
        MoveBytePcDisplacementToAbsoluteLong,
        MoveLongPcDisplacementToAbsoluteLong,
        MoveWordPcBriefIndexedToAddressDisplacement,
        MoveWordAddressDisplacementToBriefIndexed,
        MoveLongAbsoluteLongToAbsoluteLong,
        MoveLongAddressDisplacementToAbsoluteLong,
        MoveByteDataToData,
        MoveByteImmediateToData,
        MoveByteImmediateToAddressIndirect,
        MoveByteImmediateToAddressDisplacement,
        MoveByteImmediateToBriefIndexed,
        MoveByteAddressIndirectToData,
        MoveByteAddressIndirectToAddressIndirect,
        MoveBytePostIncrementToData,
        MoveByteAddressDisplacementToData,
        MoveByteAbsoluteLongToData,
        MoveByteBriefIndexedToData,
        MoveByteDataToAbsoluteLong,
        MoveByteDataToAddressIndirect,
        MoveByteDataToAddressDisplacement,
        MoveByteAddressDisplacementToAddressDisplacement,
        MoveByteBriefIndexedToAddressDisplacement,
        MoveByteDataToBriefIndexed,
        MoveByteDataToPostIncrement,
        MoveByteDataToPredecrement,
        MoveByteBriefIndexedToPredecrement,
        MoveBytePostIncrementToPostIncrement,
        MoveByteAddressIndirectToAbsoluteLong,
        MoveByteAbsoluteLongToAbsoluteLong,
        MoveWordAbsoluteLongToData,
        MoveWordAbsoluteLongToAddress,
        MoveWordAddressIndirectToAddress,
        MoveWordAddressDisplacementToAddress,
        MoveWordPcBriefIndexedToAddress,
        MoveWordBriefIndexedToAddress,
        FullIndexedMoveToRegister,
        GeneralMove,
        GeneralMovem,
        GeneralArithmetic,
        GeneralLogical,
        FullIndexedMoveToMemory,
        FullIndexedRegisterToMemory,
        FullIndexedClear,
        FullIndexedLea,
        FullIndexedPea,
        FullIndexedJump,
        FullIndexedSubroutine,
        MoveWordAddressDisplacementToData,
        MoveWordAddressDisplacementToAddressIndirect,
        MoveWordAddressIndirectToAddressIndirect,
        MoveLongAbsoluteWordToAddressIndirect,
        MoveLongAbsoluteLongToAddressIndirect,
        MoveLongPcDisplacementToAddressIndirect,
        MoveLongPcBriefIndexedToAddressIndirect,
        MoveBytePredecrementToAddressDisplacement,
        MoveWordPredecrementToAddressDisplacement,
        MoveLongPredecrementToAddressDisplacement,
        MoveBytePredecrementToPostIncrement,
        MoveWordPredecrementToPostIncrement,
        MoveLongPredecrementToPostIncrement,
        MoveByteBriefIndexedToAddressIndirect,
        MoveWordBriefIndexedToAddressIndirect,
        MoveLongBriefIndexedToAddressIndirect,
        MoveBytePostIncrementToAddressIndirect,
        MoveWordPostIncrementToAddressIndirect,
        MoveLongPostIncrementToAddressIndirect,
        MoveByteAddressDisplacementToAddressIndirect,
        MoveWordAddressIndirectToAddressDisplacement,
        MoveByteAddressIndirectToAddressDisplacement,
        MoveWordBriefIndexedToData,
        MoveWordBriefIndexedToAddressDisplacement,
        MoveWordBriefIndexedToPredecrement,
        MoveWordPostIncrementToData,
        MoveWordPostIncrementToAddress,
        MoveWordPostIncrementToPostIncrement,
        MoveWordDataToPostIncrement,
        MoveBytePostIncrementToAddressDisplacement,
        MoveWordPostIncrementToAddressDisplacement,
        MoveWordImmediateToData,
        MoveWordDataToData,
        MoveWordAddressToData,
        MoveWordDataToAddress,
        MoveWordDataToAddressIndirect,
        MoveWordAddressToAddressIndirect,
        MoveWordImmediateToAddressDisplacement,
        MoveWordImmediateToPredecrement,
        MoveByteImmediateToPredecrement,
        MoveWordImmediateToPostIncrement,
        MoveBytePostIncrementToAbsoluteLong,
        MoveWordPostIncrementToAbsoluteLong,
        MoveLongPostIncrementToAbsoluteLong,
        MoveWordDataToPredecrement,
        MoveWordDataToAddressDisplacement,
        MoveWordDataToBriefIndexed,
        MoveWordAddressToAddressDisplacement,
        MoveWordAddressDisplacementToAddressDisplacement,
        MoveWordAddressDisplacementToAbsoluteLong,
        MoveByteAddressDisplacementToAbsoluteLong,
        MoveWordPcDisplacementToAddressDisplacement,
        MoveWordDataToAbsoluteLong,
        MoveWordAbsoluteLongToAbsoluteLong,
        MoveWordAbsoluteLongToAddressDisplacement,
        MoveWordAbsoluteLongToPredecrement,
        MoveWordPostIncrementToPredecrement,
        MoveBytePostIncrementToPredecrement,
        MoveWordAddressDisplacementToPredecrement,
        MoveByteAddressDisplacementToPredecrement,
        ImmediateLogicalByteToAbsoluteLong,
        ImmediateLogicalWordToAbsoluteLong,
        ImmediateLogicalLongToAbsoluteLong,
        EoriByteImmediateToAddressDisplacement,
        EoriWordImmediateToAddressDisplacement,
        EoriLongImmediateToAddressDisplacement,
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
        AddiByteImmediateToAbsoluteLong,
        AddiWordImmediateToAbsoluteLong,
        SubiByteImmediateToAbsoluteLong,
        SubiWordImmediateToAbsoluteLong,
        SubiLongImmediateToAbsoluteLong,
        BsetDynamicAddressIndirect,
        BclrDynamicAddressIndirect,
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
        SubWordAddressToData,
        NegByteAddressDisplacement,
        NegWordAddressDisplacement,
        NegLongAddressDisplacement,
        CmpByteBriefIndexedToData,
        CmpWordBriefIndexedToData,
        CmpLongBriefIndexedToData,
        SubByteBriefIndexedToData,
        SubWordBriefIndexedToData,
        SubLongBriefIndexedToData,
        SubByteDataToAddressIndirect,
        SubWordDataToAddressIndirect,
        SubLongDataToAddressIndirect,
        SubBytePostIncrementToData,
        SubWordPostIncrementToData,
        SubLongPostIncrementToData,
        SubWordAddressIndirectToData,
        SubByteAddressIndirectToData,
        SubLongAddressIndirectToData,
        SubWordAddressDisplacementToData,
        SubWordDataToPostIncrement,
        SubLongDataToData,
        SubLongAddressToData,
        SubLongAddressDisplacementToData,
        SubLongImmediateToData,
        SubLongDataToAddressDisplacement,
        AddByteDataToData,
        AddWordDataToData,
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
        AddByteDataToAddressIndirect,
        AddWordDataToAddressIndirect,
        AddByteDataToPostIncrement,
        AddWordDataToPostIncrement,
        AddLongDataToPostIncrement,
        AddLongDataToAbsoluteLong,
        SubByteDataToAbsoluteLong,
        SubWordDataToAbsoluteLong,
        SubLongDataToAbsoluteLong,
        AddByteDataToAbsoluteLong,
        AddWordDataToAbsoluteLong,
        AddLongAddressIndirectToData,
        AddByteBriefIndexedToData,
        AddWordBriefIndexedToData,
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
        AddLongImmediateToData,
        AddByteDataToAddressDisplacement,
        AddWordDataToAddressDisplacement,
        AddLongDataToAddressDisplacement,
        AddByteDataToBriefIndexed,
        AddWordDataToBriefIndexed,
        AddLongDataToBriefIndexed,
        AddaWordImmediateToAddress,
        AddaWordDataToAddress,
        AddaWordAddressToAddress,
        AddaWordAddressDisplacementToAddress,
        AddaWordBriefIndexedToAddress,
        AddaWordPcBriefIndexedToAddress,
        AddaLongPcBriefIndexedToAddress,
        AddaLongBriefIndexedToAddress,
        AddaLongImmediateToAddress,
        AddaLongDataToAddress,
        AddaLongAddressToAddress,
        AddaLongAddressDisplacementToAddress,
        AddaWordAbsoluteLongToAddress,
        AddaLongAbsoluteLongToAddress,
        AddqWordAddress,
        AddqLongAddress,
        AddqLongAddressIndirect,
        AddqBytePostIncrement,
        AddqWordPostIncrement,
        AddqLongPostIncrement,
        SubqBytePostIncrement,
        SubqWordPostIncrement,
        SubqLongPostIncrement,
        SubaWordAbsoluteLongToAddress,
        SubaLongAbsoluteLongToAddress,
        AddqByteAddressDisplacement,
        AddqByteAddressIndirect,
        AddqWordAddressDisplacement,
        AddqWordBriefIndexed,
        SubqWordBriefIndexed,
        AddqLongAddressDisplacement,
        AddqLongAbsoluteLong,
        SubqByteAbsoluteLong,
        SubqWordAbsoluteLong,
        SubqLongAbsoluteLong,
        SubaWordPostIncrementToAddress,
        SubaWordAddressIndirectToAddress,
        SubaLongAddressIndirectToAddress,
        SubaLongPostIncrementToAddress,
        SubaLongImmediateToAddress,
        SubaLongDataToAddress,
        SubqWordAddress,
        SubqLongAddress,
        SubqLongAddressIndirect,
        AddqWordAddressIndirect,
        SubqByteAddressIndirect,
        SubqWordAddressIndirect,
        SubqLongAddressDisplacement,
        SubqByteAddressDisplacement,
        SubqWordAddressDisplacement,
        SubaLongAddressToAddress,
        SubaLongAddressDisplacementToAddress,
        SubaLongPcDisplacementToAddress,
        SubaWordAddressDisplacementToAddress,
        SubaWordImmediateToAddress,
        SubaWordDataToAddress,
        DivuWordEffectiveAddressToData,
        DivsWordEffectiveAddressToData,
        DivuWordImmediateToData,
        DivsWordImmediateToData,
        MuluWordEffectiveAddressToData,
        MulsWordEffectiveAddressToData,
        MuluLong,
        MulsLong,
        DivuLong,
        DivsLong,
        AbcdByteDataToData,
        AbcdBytePredecrementMemory,
        SbcdByteDataToData,
        SbcdBytePredecrementMemory,
        NbcdByteData,
        NbcdByteAddressIndirect,
        NbcdBytePostIncrement,
        NbcdBytePredecrement,
        NbcdByteAddressDisplacement,
        NbcdByteBriefIndexed,
        NbcdByteAbsoluteWord,
        NbcdByteAbsoluteLong,
        OriWordImmediateToData,
        OriLongImmediateToData,
        AndiLongImmediateToData,
        EoriByteImmediateToData,
        OrByteDataToData,
        OrWordDataToData,
        AndByteBriefIndexedToData,
        AndWordBriefIndexedToData,
        AndLongBriefIndexedToData,
        AndBytePcBriefIndexedToData,
        AndWordPcBriefIndexedToData,
        AndByteAbsoluteLongToData,
        AndWordAbsoluteLongToData,
        AddaWordPostIncrementToAddress,
        AddaLongPostIncrementToAddress,
        AddaWordAddressIndirectToAddress,
        AddaLongAddressIndirectToAddress,
        AddBytePcDisplacementToData,
        AddByteAbsoluteLongToData,
        AddWordAbsoluteLongToData,
        AddWordPcDisplacementToData,
        AddBytePcBriefIndexedToData,
        AddWordPcBriefIndexedToData,
        AddLongPcBriefIndexedToData,
        SubBytePcBriefIndexedToData,
        SubWordPcBriefIndexedToData,
        SubLongPcBriefIndexedToData,
        AddqWordAbsoluteLong,
        SubBytePcDisplacementToData,
        SubWordPcDisplacementToData,
        SubLongPcDisplacementToData,
        SubByteAbsoluteLongToData,
        SubWordAbsoluteLongToData,
        SubLongAbsoluteLongToData,
        AndiByteImmediateToPostIncrement,
        AndiWordImmediateToPostIncrement,
        AndiLongImmediateToPostIncrement,
        AndByteDataToPostIncrement,
        AndWordDataToPostIncrement,
        AndLongDataToPostIncrement,
        OrByteDataToAbsoluteLong,
        OrWordDataToAbsoluteLong,
        OrLongDataToAbsoluteLong,
        OrByteDataToPostIncrement,
        OrWordDataToPostIncrement,
        OrLongDataToPostIncrement,
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
        AndByteDataToAddressIndirect,
        AndWordDataToAddressIndirect,
        AndLongDataToAddressIndirect,
        AndWordDataToData,
        AndWordAddressDisplacementToData,
        AndByteAddressIndirectToData,
        AndWordAddressIndirectToData,
        AndLongAddressIndirectToData,
        AndWordDataToAddressDisplacement,
        AndLongDataToAddressDisplacement,
        ExgDataData,
        ExgDataAddress,
        ExgAddressAddress,
        AndiByteImmediateToData,
        AndiByteImmediateToAddressIndirect,
        AndiWordImmediateToAddressIndirect,
        AndiLongImmediateToAddressIndirect,
        AndiByteImmediateToAddressDisplacement,
        AndiWordImmediateToAddressDisplacement,
        AndiLongImmediateToAddressDisplacement,
        AndByteImmediateToData,
        AndWordImmediateToData,
        AndLongImmediateToData,
        AndLongEffectiveAddressToData,
        OrWordImmediateToData,
        OrByteImmediateToData,
        OrWordAddressDisplacementToData,
        OrLongEffectiveAddressToData,
        MuluWordImmediateToData,
        EoriWordImmediateToData,
        EoriLongImmediateToData,
        EorLongDataToAddressDisplacement,
        EorLongDataToData,
        EorByteDataToAddressDisplacement,
        EorWordDataToData,
        EorByteDataToData,
        CmpiLongImmediateToData,
        CmpiLongImmediateToPostIncrement,
        CmpiByteImmediateToPostIncrement,
        CmpiWordImmediateToPostIncrement,
        CmpiLongImmediateToAddressIndirect,
        CmpiLongImmediateToAddressDisplacement,
        CmpiByteImmediateToBriefIndexed,
        CmpiWordImmediateToBriefIndexed,
        CmpiLongImmediateToBriefIndexed,
        CmpiLongImmediateToAbsoluteLong,
        CmpiLongImmediateToAbsoluteWord,
        CmpiByteImmediateToData,
        CmpiByteImmediateToAddressIndirect,
        CmpiByteImmediateToAddressDisplacement,
        CmpiByteImmediateToPredecrement,
        CmpiWordImmediateToData,
        CmpiWordImmediateToAddressIndirect,
        CmpiWordImmediateToAddressDisplacement,
        CmpiByteImmediateToAbsoluteLong,
        CmpiWordImmediateToAbsoluteLong,
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
        CmpaWordAbsoluteLongToAddress,
        CmpaLongAbsoluteLongToAddress,
        CmpLongDataToData,
        CmpLongImmediateToData,
        CmpLongAbsoluteLongToData,
        CmpWordAbsoluteLongToData,
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
        LeaAbsoluteWord,
        SwapData,
        LsrByteImmediateData,
        LsrWordRegisterData,
        AsrByteRegisterData,
        AsrWordRegisterData,
        AslWordRegisterData,
        LslByteRegisterData,
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
        RorByteRegisterData,
        RoxrByteImmediateData,
        RoxrWordImmediateData,
        RoxrLongImmediateData,
        RoxlByteImmediateData,
        RoxlWordImmediateData,
        RoxlLongImmediateData,
        RoxrByteRegisterData,
        RoxrWordRegisterData,
        RoxrLongRegisterData,
        RoxlByteRegisterData,
        RoxlWordRegisterData,
        RoxlLongRegisterData,
        RorWordRegisterData,
        RorLongRegisterData,
        RolByteRegisterData,
        RolWordRegisterData,
        RolLongRegisterData,
        RorWordImmediateData,
        RorLongImmediateData,
        RolWordImmediateData,
        RolLongImmediateData,
        JsrAddressIndirect,
        JsrAbsoluteLong,
        JsrAbsoluteWord,
        JmpAbsoluteWord,
        JsrAddressDisplacement,
        JsrBriefIndexed,
        JsrPcBriefIndexed,
        JmpAddressIndirect,
        JmpAddressDisplacement,
        JmpPcDisplacement,
        JmpBriefIndexed,
        JmpAbsoluteLong,
        PeaAddressDisplacement,
        PeaAddressIndirect,
        PeaBriefIndexed,
        PeaPcBriefIndexed,
        PeaAbsoluteWord,
        PeaAbsoluteLong,
        PeaPcDisplacement,
        MovemLongRegistersToPredecrement,
        MovemWordRegistersToPredecrement,
        MovemWordRegistersToAddressDisplacement,
        MovemWordAddressDisplacementToRegisters,
        MovemWordAddressIndirectToRegisters,
        MovemLongRegistersToAddressIndirect,
        MovemLongRegistersToAddressDisplacement,
        MovemLongRegistersToBriefIndexed,
        MovemLongAddressIndirectToRegisters,
        MovemWordBriefIndexedToRegisters,
        MovemLongBriefIndexedToRegisters,
        MovemLongAddressDisplacementToRegisters,
        MovemLongPcDisplacementToRegisters,
        MovemLongPostIncrementToRegisters,
        MovemWordPostIncrementToRegisters,
        OriByteImmediateToData,
        OriByteImmediateToAddressIndirect,
        OriByteImmediateToAddressDisplacement,
        BtstByteImmediateAbsoluteLong,
        BtstByteImmediateBriefIndexed,
        BchgByteImmediateBriefIndexed,
        BclrByteImmediateBriefIndexed,
        BsetByteImmediateBriefIndexed,
        BtstByteImmediateAddressIndirect,
        BtstByteImmediatePostIncrement,
        BchgByteImmediatePostIncrement,
        BclrByteImmediatePostIncrement,
        BsetByteImmediatePostIncrement,
        BtstByteImmediateAddressDisplacement,
        BchgByteImmediateAbsoluteLong,
        BchgByteImmediateAddressDisplacement,
        BclrByteImmediateAbsoluteLong,
        BclrByteImmediateAddressDisplacement,
        BsetByteImmediateAbsoluteLong,
        BsetByteImmediateAddressDisplacement,
        BsetByteDynamicAddressDisplacement,
        BclrByteDynamicAddressDisplacement,
        BchgByteDynamicAddressDisplacement,
        BtstImmediateData,
        BclrImmediateData,
        BchgImmediateData,
        BsetImmediateData,
        BsetDynamicData,
        BtstDynamicData,
        BtstByteDynamicAddressDisplacement,
        BtstByteDynamicAddressIndirect,
        BchgByteDynamicBriefIndexed,
        BclrByteDynamicBriefIndexed,
        BsetByteDynamicBriefIndexed,
        BtstByteDynamicBriefIndexed,
        BtstByteDynamicAbsoluteLong,
        BsetByteImmediateAddressIndirect,
        BclrByteImmediateAddressIndirect,
        NotByteAbsoluteWord,
        NotWordAbsoluteWord,
        NotLongAbsoluteWord,
        NotByteAbsoluteLong,
        NotWordAbsoluteLong,
        NotLongAbsoluteLong,
        NotByteAddressIndirect,
        NotWordAddressIndirect,
        NotLongAddressIndirect,
        NotBytePostIncrement,
        NotWordPostIncrement,
        NotLongPostIncrement,
        TstLongAbsoluteLong,
        BchgByteDynamicAbsoluteLong,
        BclrByteDynamicAbsoluteLong,
        BsetByteDynamicAbsoluteLong,
        BchgByteDynamicAddressIndirect,
        BclrDynamicData,
        SccData,
        SccAbsoluteLong,
        SccAbsoluteWord,
        SccAddressIndirect,
        SccPostIncrement,
        SccPredecrement,
        SccAddressDisplacement,
        SccBriefIndexed,
        BranchByteTaken,
        BranchByteNotTaken,
        BsrByte,
        BranchWordTaken,
        BranchWordNotTaken,
        BsrWord,
        DbccConditionTrue,
        DbccBranchTaken,
        DbccExpired,
        BranchLongTaken,
        BranchLongNotTaken,
        BsrLong,
        TstLongData,
        MoveLongDataToPredecrement,
        MoveLongAddressToPredecrement,
        MoveByteAddressIndirectToPredecrement,
        MoveWordAddressIndirectToPredecrement,
        MoveLongAddressIndirectToPredecrement,
        MoveLongPostIncrementToPredecrement,
        MoveLongAddressIndirectToAddressDisplacement,
        MoveLongPostIncrementToAddressDisplacement,
        MoveLongAddressDisplacementToPredecrement,
        MovemLongRegistersToAbsoluteLong,
        MoveByteImmediateToPostIncrement,
        CmpBytePredecrementToData,
        CmpWordPredecrementToData,
        CmpLongPredecrementToData,
        MoveByteAbsoluteWordToPostIncrement,
        MoveWordAbsoluteWordToPostIncrement,
        MoveLongAbsoluteWordToPostIncrement,
        MoveByteAbsoluteLongToAddressIndirect,
        MoveWordAbsoluteLongToAddressIndirect,
        MoveBytePostIncrementToAbsoluteWord,
        MoveWordPostIncrementToAbsoluteWord,
        MoveLongPostIncrementToAbsoluteWord,
        EorByteDataToAddressIndirect,
        EorWordDataToAddressIndirect,
        EorLongDataToAddressIndirect,
        AddiByteImmediateToPostIncrement,
        AddiWordImmediateToPostIncrement,
        AddiLongImmediateToPostIncrement,
        OrByteAddressIndirectToData,
        OrWordAddressIndirectToData,
        AddqByteAbsoluteLong,
        AslByteImmediateData,
        MoveWordAddressToBriefIndexed,
        OrBytePcBriefIndexedToData,
        OrWordPcBriefIndexedToData,
        ClrByteAbsoluteWord,
        ClrWordAbsoluteWord,
        MoveBytePcBriefIndexedToAbsoluteWord,
        MoveWordPcBriefIndexedToAbsoluteWord,
        MoveLongPcBriefIndexedToAbsoluteWord,
        TstByteAbsoluteWord,
        TstWordAbsoluteWord,
        TstLongAbsoluteWord,
        MovemLongRegistersToAbsoluteWord,
        SubqByteAbsoluteWord,
        SubqWordAbsoluteWord,
        SubqLongAbsoluteWord,
        MoveByteImmediateToAbsoluteWord,
        MoveWordImmediateToAbsoluteWord,
        EoriByteImmediateToAddressIndirect,
        EoriWordImmediateToAddressIndirect,
        EoriLongImmediateToAddressIndirect,
        BtstByteImmediateAbsoluteWord,
        SubiByteImmediateToAddressIndirect,
        SubiWordImmediateToAddressIndirect,
        SubiLongImmediateToAddressIndirect,
        MoveByteDataToAbsoluteWord,
        MoveWordDataToAbsoluteWord,
        MoveByteAddressDisplacementToAbsoluteWord,
        MoveWordAddressDisplacementToAbsoluteWord,
        MoveLongAddressDisplacementToAbsoluteWord,
        EoriByteImmediateToAbsoluteLong,
        EoriWordImmediateToAbsoluteLong,
        EoriLongImmediateToAbsoluteLong,
        OrByteBriefIndexedToData,
        OrWordBriefIndexedToData,
        MoveBytePostIncrementToBriefIndexed,
        MoveWordPostIncrementToBriefIndexed,
        MoveLongPostIncrementToBriefIndexed,
        AsrWordAddressDisplacement
    }

    internal readonly record struct M68kInstructionPlan(
        M68kInstructionTimingKey Key,
        string Name,
        int NativeCycles,
        int HeadCycles,
        int TailCycles,
        bool UsesHeadTail,
        M68kTimingBarrier Barriers)
    {
        public static M68kInstructionPlan CreateFlat(
            M68kInstructionTimingKey key,
            string name,
            int nativeCycles,
            M68kTimingBarrier barriers = M68kTimingBarrier.None)
            => new(key, name, nativeCycles, 0, 0, UsesHeadTail: false, barriers);

        public static M68kInstructionPlan CreateHeadTail(
            M68kInstructionTimingKey key,
            string name,
            int cacheCaseCycles,
            int headCycles,
            int tailCycles,
            M68kTimingBarrier barriers = M68kTimingBarrier.None)
            => new(key, name, cacheCaseCycles, headCycles, tailCycles, UsesHeadTail: true, barriers);
    }

    internal readonly record struct M68kExecutedInstructionTiming(
        M68kInstructionPlan Plan,
        int OverlapCycles,
        long StartNativeCycle,
        long EndNativeCycle,
        long PendingBusNativeCycle)
    {
        public long ElapsedNativeCycles => EndNativeCycle - StartNativeCycle;
    }

    internal sealed class M68kPipelineState
    {
        public long InstructionBoundaryNativeCycle { get; set; }

        public long BusControllerAvailableNativeCycle { get; set; }

        public int PendingTailCycles { get; set; }

        public bool SuppressNextOverlap { get; set; }

        public void Reset()
        {
            InstructionBoundaryNativeCycle = 0;
            BusControllerAvailableNativeCycle = 0;
            PendingTailCycles = 0;
            SuppressNextOverlap = false;
        }
    }

    internal sealed class M68kInstructionCache
    {
        private readonly uint[] _tags;
        private readonly bool[] _valid;
        private readonly ushort[] _instructionWords;
        private readonly int _lineSize;
        private readonly int _wordsPerLine;

        public M68kInstructionCache(int byteSize = 256, int lineSize = 4)
        {
            if (byteSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(byteSize));
            }

            if (lineSize <= 0 || (lineSize & (lineSize - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(lineSize));
            }

            if ((lineSize & 1) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(lineSize));
            }

            _lineSize = lineSize;
            _wordsPerLine = lineSize / 2;
            _tags = new uint[Math.Max(1, byteSize / lineSize)];
            _valid = new bool[_tags.Length];
            _instructionWords = new ushort[_tags.Length * _wordsPerLine];
        }

        public bool Enabled { get; private set; }

        public bool Frozen { get; private set; }

        public int LineSize => _lineSize;

        public void Reset()
        {
            Clear();
            Enabled = false;
            Frozen = false;
        }

        public void ApplyControl(bool enabled, bool frozen, bool clearAll, bool clearEntry, uint entryAddress)
        {
            Enabled = enabled;
            Frozen = frozen;
            if (clearAll)
            {
                Clear();
            }
            else if (clearEntry)
            {
                ClearEntry(entryAddress);
            }
        }

        public bool Probe(uint address)
        {
            if (!Enabled)
            {
                return false;
            }

            var lineAddress = address & ~((uint)_lineSize - 1u);
            var index = GetIndex(lineAddress);
            if (_valid[index] && _tags[index] == lineAddress)
            {
                return true;
            }

            if (!Frozen)
            {
                _valid[index] = true;
                _tags[index] = lineAddress;
            }

            return false;
        }

        /// <summary>
        /// Returns a word from a previously captured instruction-cache line.
        /// A cache hit deliberately returns the captured guest bytes rather than
        /// rereading backing memory: a 68020+ write is not visible to instruction
        /// execution until a guest cache-control operation discards that line.
        /// </summary>
        public bool TryReadInstructionWord(uint address, out ushort value)
        {
            value = 0;
            if (!Enabled)
            {
                return false;
            }

            var lineAddress = address & ~((uint)_lineSize - 1u);
            var index = GetIndex(lineAddress);
            if (!_valid[index] || _tags[index] != lineAddress)
            {
                return false;
            }

            var wordOffset = (int)((address - lineAddress) >> 1);
            if ((uint)wordOffset >= (uint)_wordsPerLine)
            {
                return false;
            }

            value = _instructionWords[(index * _wordsPerLine) + wordOffset];
            return true;
        }

        /// <summary>
        /// Captures one complete instruction-cache line from the guest-visible
        /// code reader. Frozen caches retain their existing contents on a miss.
        /// </summary>
        public void CaptureInstructionLine(uint address, IM68kCodeReader codeReader)
        {
            if (!Enabled || Frozen)
            {
                return;
            }

            var lineAddress = address & ~((uint)_lineSize - 1u);
            var index = GetIndex(lineAddress);
            var wordBase = index * _wordsPerLine;
            for (var word = 0; word < _wordsPerLine; word++)
            {
                _instructionWords[wordBase + word] = codeReader.ReadHostWord(
                    unchecked(lineAddress + (uint)(word * 2)));
            }

            _tags[index] = lineAddress;
            _valid[index] = true;
        }

        public void Clear()
            => Array.Clear(_valid);

        public void ClearEntry(uint address)
        {
            var lineAddress = address & ~((uint)_lineSize - 1u);
            var index = GetIndex(lineAddress);
            if (_tags[index] == lineAddress)
            {
                _valid[index] = false;
            }
        }

        private int GetIndex(uint lineAddress)
            => (int)((lineAddress / (uint)_lineSize) % (uint)_tags.Length);
    }

    internal sealed class M68kTimingEngine
    {
        private readonly M68020CpuProfile _profile;
        private readonly M68kCpuState _state;
        private readonly M68kInstructionPlan _branchByteTakenPlan;
        private readonly int _nativeCyclesPerMachineCycle;
        private M68kExecutedInstructionTiming _lastInstructionTiming;
        private M68kInstructionTimingKey _lastFlatTimingKey;
        private int _lastFlatOverlapCycles;
        private long _lastFlatStartNativeCycle;
        private long _lastFlatEndNativeCycle;
        private long _lastFlatPendingBusNativeCycle;
        private bool _lastTimingIsFlat;

        public M68kTimingEngine(M68020CpuProfile profile, M68kCpuState state)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _branchByteTakenPlan = profile.FixedInstructionNativeCycles is int fixedCycles
                ? fixedCycles == 1
                    ? M68040FixedTimingModel.GetPlan(M68kInstructionTimingKey.BranchByteTaken)
                    : M68kTimingFormula.CreateFixedPlan(M68kInstructionTimingKey.BranchByteTaken, fixedCycles)
                : profile.Model is M68kAcceleratorModel.M68030 or M68kAcceleratorModel.M68040
                    ? M68030TimingModel.GetPlan(M68kInstructionTimingKey.BranchByteTaken)
                    : M68020TimingModel.GetPlan(M68kInstructionTimingKey.BranchByteTaken);
            _nativeCyclesPerMachineCycle = profile.NativeCyclesPerMachineCycle;
            InstructionCache = new M68kInstructionCache();
            DataCache = profile.Model is M68kAcceleratorModel.M68030 or M68kAcceleratorModel.M68040 or M68kAcceleratorModel.M68060
                ? new M68kInstructionCache()
                : null;
        }

        public M68kPipelineState Pipeline { get; } = new M68kPipelineState();

        public M68kInstructionCache InstructionCache { get; }

        public M68kInstructionCache? DataCache { get; }

        public M68kExecutedInstructionTiming LastInstructionTiming
            => _lastTimingIsFlat
                ? new M68kExecutedInstructionTiming(
                    GetPlan(_lastFlatTimingKey),
                    _lastFlatOverlapCycles,
                    _lastFlatStartNativeCycle,
                    _lastFlatEndNativeCycle,
                    _lastFlatPendingBusNativeCycle)
                : _lastInstructionTiming;

        public long BusControllerAvailableNativeCycle => Pipeline.BusControllerAvailableNativeCycle;

        public void Reset()
        {
            Pipeline.Reset();
            InstructionCache.Reset();
            DataCache?.Reset();
            _lastInstructionTiming = default;
            _lastTimingIsFlat = false;
        }

        public M68kInstructionPlan GetPlan(M68kInstructionTimingKey key)
            => _profile.FixedInstructionNativeCycles is int fixedCycles
                ? fixedCycles == 1
                    ? M68040FixedTimingModel.GetPlan(key)
                    : M68kTimingFormula.CreateFixedPlan(key, fixedCycles)
                : _profile.Model is M68kAcceleratorModel.M68030 or M68kAcceleratorModel.M68040
                ? M68030TimingModel.GetPlan(key)
                : M68020TimingModel.GetPlan(key);

        public bool TryReadInstructionCacheWord(uint address, out ushort value)
        {
            value = 0;
            return _profile.IsInstructionCacheableAddress(address) &&
                InstructionCache.TryReadInstructionWord(address, out value);
        }

        public void CaptureInstructionCacheLine(uint address, IM68kCodeReader codeReader)
        {
            if (_profile.IsInstructionCacheableAddress(address))
            {
                InstructionCache.CaptureInstructionLine(address, codeReader);
            }
        }

        public bool ProbeDataCache(uint address)
            => DataCache?.Probe(address) == true;

        public void ApplyCacheControl(uint cacheControlRegister, uint cacheAddressRegister)
        {
            if (_profile.Model is M68kAcceleratorModel.M68040 or M68kAcceleratorModel.M68060)
            {
                // MC68040UM 2.2.2.5. Invalidation uses CINV/CPUSH, not 030 CACR bits.
                var is060 = _profile.Model == M68kAcceleratorModel.M68060;
                InstructionCache.ApplyControl((cacheControlRegister & 0x8000) != 0,
                    is060 && (cacheControlRegister & 0x4000) != 0, false, false, 0);
                DataCache?.ApplyControl((cacheControlRegister & 0x8000_0000) != 0,
                    is060 && (cacheControlRegister & 0x4000_0000) != 0, false, false, 0);
                return;
            }
            if (_profile.Model == M68kAcceleratorModel.M68030)
            {
                InstructionCache.ApplyControl(
                    enabled: (cacheControlRegister & 0x0000_0001) != 0,
                    frozen: (cacheControlRegister & 0x0000_0002) != 0,
                    clearAll: (cacheControlRegister & 0x0000_0008) != 0,
                    clearEntry: (cacheControlRegister & 0x0000_0004) != 0,
                    cacheAddressRegister);
                DataCache?.ApplyControl(
                    enabled: (cacheControlRegister & 0x0000_0100) != 0,
                    frozen: (cacheControlRegister & 0x0000_0200) != 0,
                    clearAll: (cacheControlRegister & 0x0000_0800) != 0,
                    clearEntry: (cacheControlRegister & 0x0000_0400) != 0,
                    cacheAddressRegister);
                return;
            }

            InstructionCache.ApplyControl(
                enabled: (cacheControlRegister & 0x0000_0001) != 0,
                frozen: (cacheControlRegister & 0x0000_0002) != 0,
                clearAll: (cacheControlRegister & 0x0000_0008) != 0,
                clearEntry: (cacheControlRegister & 0x0000_0004) != 0,
                cacheAddressRegister);
        }

        public void RecordPostedBusCompletion(long completedMachineCycle)
        {
            var nativeCompletion = _profile.MachineToNativeCycles(completedMachineCycle);
            if (Pipeline.BusControllerAvailableNativeCycle < nativeCompletion)
            {
                Pipeline.BusControllerAvailableNativeCycle = nativeCompletion;
            }
        }

        public void CompleteBlockingBusAccess(long completedMachineCycle)
        {
            RecordPostedBusCompletion(completedMachineCycle);
            SynchronizeNativeToBus();
        }

        public void SynchronizeNativeToMachine()
        {
            var nativeCycles = _profile.MachineToNativeCycles(_state.Cycles);
            if (_state.NativeCycles < nativeCycles)
            {
                _state.NativeCycles = nativeCycles;
            }
        }

        public void SynchronizeNativeToBus()
        {
            if (_state.NativeCycles < Pipeline.BusControllerAvailableNativeCycle)
            {
                _state.NativeCycles = Pipeline.BusControllerAvailableNativeCycle;
            }

            SynchronizeMachineToNative();
        }

        public M68kExecutedInstructionTiming CompleteInstruction(M68kInstructionPlan plan)
        {
            if (plan.NativeCycles < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(plan), plan.NativeCycles, "Instruction cycles must be non-negative.");
            }

            var start = Math.Max(_state.NativeCycles, _profile.MachineToNativeCycles(_state.Cycles));
            _state.NativeCycles = start;

            if ((plan.Barriers & M68kTimingBarrier.SynchronizeBus) != 0)
            {
                SynchronizeNativeToBus();
                start = _state.NativeCycles;
            }

            var overlap = 0;
            if (plan.UsesHeadTail &&
                !Pipeline.SuppressNextOverlap &&
                (plan.Barriers & (M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Exception | M68kTimingBarrier.Branch)) == 0)
            {
                overlap = Math.Min(Pipeline.PendingTailCycles, Math.Max(0, plan.HeadCycles));
            }

            var elapsed = Math.Max(0, plan.NativeCycles - overlap);
            _state.NativeCycles += elapsed;

            if ((plan.Barriers & M68kTimingBarrier.SynchronizeBus) != 0)
            {
                SynchronizeNativeToBus();
            }

            SynchronizeMachineToNative();
            Pipeline.InstructionBoundaryNativeCycle = _state.NativeCycles;
            Pipeline.PendingTailCycles =
                (plan.Barriers & (M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Exception | M68kTimingBarrier.Branch)) == 0
                    ? Math.Max(0, plan.TailCycles)
                    : 0;
            Pipeline.SuppressNextOverlap =
                (plan.Barriers & (M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Exception | M68kTimingBarrier.Branch | M68kTimingBarrier.CacheControl | M68kTimingBarrier.ReadModifyWrite)) != 0;

            _lastInstructionTiming = new M68kExecutedInstructionTiming(
                plan,
                overlap,
                start,
                _state.NativeCycles,
                Pipeline.BusControllerAvailableNativeCycle);
            _lastTimingIsFlat = false;
            return _lastInstructionTiming;
        }

        internal void CompleteFlatInstruction(M68kInstructionPlan plan)
        {
            if (_profile.Model is not (
                    M68kAcceleratorModel.M68020 or
                    M68kAcceleratorModel.M68030 or
                    M68kAcceleratorModel.M68040) ||
                plan.Barriers != M68kTimingBarrier.None)
            {
                CompleteInstruction(plan);
                return;
            }

            var machineNativeCycles = _state.Cycles * _nativeCyclesPerMachineCycle;
            var start = _state.NativeCycles >= machineNativeCycles
                ? _state.NativeCycles
                : machineNativeCycles;
            var pipeline = Pipeline;
            var overlap = 0;
            if (plan.UsesHeadTail && !pipeline.SuppressNextOverlap)
            {
                overlap = Math.Min(pipeline.PendingTailCycles, Math.Max(0, plan.HeadCycles));
            }

            var elapsed = plan.NativeCycles - overlap;
            if (elapsed < 0)
            {
                elapsed = 0;
            }

            var end = start + elapsed;
            _state.NativeCycles = end;
            long machineCycles;
            if (_nativeCyclesPerMachineCycle == 2)
            {
                machineCycles = (end + 1) >> 1;
            }
            else if (_nativeCyclesPerMachineCycle == 4)
            {
                machineCycles = (end + 3) >> 2;
            }
            else
            {
                machineCycles = (end + _nativeCyclesPerMachineCycle - 1) /
                    _nativeCyclesPerMachineCycle;
            }

            if (_state.Cycles < machineCycles)
            {
                _state.Cycles = machineCycles;
            }

            pipeline.InstructionBoundaryNativeCycle = end;
            pipeline.PendingTailCycles = Math.Max(0, plan.TailCycles);
            pipeline.SuppressNextOverlap = false;
            SetLastFlatTiming(
                plan.Key,
                overlap,
                start,
                end,
                pipeline.BusControllerAvailableNativeCycle);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void CompleteHotBranchByteTaken()
        {
            var plan = _branchByteTakenPlan;
            var machineNativeCycles = _state.Cycles * _nativeCyclesPerMachineCycle;
            var start = _state.NativeCycles >= machineNativeCycles
                ? _state.NativeCycles
                : machineNativeCycles;
            var end = start + plan.NativeCycles;
            _state.NativeCycles = end;
            long machineCycles;
            if (_nativeCyclesPerMachineCycle == 2)
            {
                machineCycles = (end + 1) >> 1;
            }
            else if (_nativeCyclesPerMachineCycle == 4)
            {
                machineCycles = (end + 3) >> 2;
            }
            else
            {
                machineCycles = (end + _nativeCyclesPerMachineCycle - 1) /
                    _nativeCyclesPerMachineCycle;
            }

            if (_state.Cycles < machineCycles)
            {
                _state.Cycles = machineCycles;
            }

            Pipeline.InstructionBoundaryNativeCycle = end;
            Pipeline.PendingTailCycles = 0;
            Pipeline.SuppressNextOverlap = true;
            SetLastFlatTiming(
                plan.Key,
                0,
                start,
                end,
                Pipeline.BusControllerAvailableNativeCycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetLastFlatTiming(
            M68kInstructionTimingKey key,
            int overlapCycles,
            long startNativeCycle,
            long endNativeCycle,
            long pendingBusNativeCycle)
        {
            _lastFlatTimingKey = key;
            _lastFlatOverlapCycles = overlapCycles;
            _lastFlatStartNativeCycle = startNativeCycle;
            _lastFlatEndNativeCycle = endNativeCycle;
            _lastFlatPendingBusNativeCycle = pendingBusNativeCycle;
            _lastTimingIsFlat = true;
        }

        private void SynchronizeMachineToNative()
        {
            var machineCycles = _profile.NativeToMachineCycles(_state.NativeCycles);
            if (_state.Cycles < machineCycles)
            {
                _state.Cycles = machineCycles;
            }
        }
    }

    internal static class M68kTimingDescriptorCache
    {
        public static M68kTimingDescriptor[] Create(bool useHeadTail)
        {
            var keys = Enum.GetValues<M68kInstructionTimingKey>();
            var descriptors = new M68kTimingDescriptor[keys.Length];
            foreach (var key in keys)
            {
                if (M68kTimingDescriptor.TryCreateSpecialControlDescriptor(key, useHeadTail, out var descriptor) ||
                    M68kTimingDescriptor.TryCreateOperandShapeDescriptor(key, useHeadTail, out descriptor))
                {
                    descriptors[(int)key] = descriptor;
                }
            }

            return descriptors;
        }
    }

    internal static class M68020TimingModel
    {
        private static readonly M68kTimingDescriptor[] Descriptors = M68kTimingDescriptorCache.Create(useHeadTail: false);
        private static readonly M68kInstructionPlan[] Plans = CreatePlans(Descriptors);

        public static M68kInstructionPlan GetPlan(M68kInstructionTimingKey key)
        {
            var index = (int)key;
            if ((uint)index < (uint)Plans.Length &&
                Plans[index].Name is not null)
            {
                return Plans[index];
            }

            throw new UnsupportedM68kTimingException(key, M68kAcceleratorModel.M68020);
        }

        internal static M68kTimingDescriptor GetDescriptor(M68kInstructionTimingKey key)
        {
            var index = (int)key;
            if ((uint)index < (uint)Descriptors.Length &&
                Descriptors[index].LegacyLabel is not null)
            {
                return Descriptors[index];
            }

            throw new UnsupportedM68kTimingException(key, M68kAcceleratorModel.M68020);
        }

        internal static M68kInstructionPlan GetCompatibilityPlan(M68kInstructionTimingKey key)
        {
            throw new UnsupportedM68kTimingException(key, M68kAcceleratorModel.M68020);
        }

        private static M68kInstructionPlan[] CreatePlans(M68kTimingDescriptor[] descriptors)
        {
            var plans = new M68kInstructionPlan[descriptors.Length];
            for (var i = 0; i < descriptors.Length; i++)
            {
                if (descriptors[i].LegacyLabel is not null)
                {
                    plans[i] = M68kTimingFormula.CreatePlan(descriptors[i]);
                }
            }

            return plans;
        }
    }

    internal static class M68030TimingModel
    {
        private static readonly M68kTimingDescriptor[] Descriptors = M68kTimingDescriptorCache.Create(useHeadTail: true);
        private static readonly M68kInstructionPlan[] Plans = CreatePlans(Descriptors);

        public static M68kInstructionPlan GetPlan(M68kInstructionTimingKey key)
        {
            var index = (int)key;
            if ((uint)index < (uint)Plans.Length &&
                Plans[index].Name is not null)
            {
                return Plans[index];
            }

            throw new UnsupportedM68kTimingException(key, M68kAcceleratorModel.M68030);
        }

        internal static M68kTimingDescriptor GetDescriptor(M68kInstructionTimingKey key)
        {
            var index = (int)key;
            if ((uint)index < (uint)Descriptors.Length &&
                Descriptors[index].LegacyLabel is not null)
            {
                return Descriptors[index];
            }

            throw new UnsupportedM68kTimingException(key, M68kAcceleratorModel.M68030);
        }

        internal static M68kInstructionPlan GetCompatibilityPlan(M68kInstructionTimingKey key)
        {
            throw new UnsupportedM68kTimingException(key, M68kAcceleratorModel.M68030);
        }

        private static M68kInstructionPlan[] CreatePlans(M68kTimingDescriptor[] descriptors)
        {
            var plans = new M68kInstructionPlan[descriptors.Length];
            for (var i = 0; i < descriptors.Length; i++)
            {
                if (descriptors[i].LegacyLabel is not null)
                {
                    plans[i] = M68kTimingFormula.CreatePlan(descriptors[i]);
                }
            }

            return plans;
        }
    }

    internal static class M68040FixedTimingModel
    {
        private static readonly M68kInstructionPlan[] Plans = CreatePlans();

        public static M68kInstructionPlan GetPlan(M68kInstructionTimingKey key)
        {
            var index = (int)key;
            if ((uint)index < (uint)Plans.Length && Plans[index].Name is not null)
            {
                return Plans[index];
            }

            throw new UnsupportedM68kTimingException(key, M68kAcceleratorModel.M68040);
        }

        private static M68kInstructionPlan[] CreatePlans()
        {
            var keys = Enum.GetValues<M68kInstructionTimingKey>();
            var plans = new M68kInstructionPlan[(int)keys[^1] + 1];
            foreach (var key in keys)
            {
                plans[(int)key] = M68kTimingFormula.CreateFixedPlan(key, nativeCycles: 1);
            }

            return plans;
        }
    }

    internal sealed class M68kTimedBusAdapter
    {
        private readonly IM68kBus _bus;
        private readonly M68020CpuProfile _profile;
        private readonly M68kCpuState _state;
        private readonly M68kTimingEngine _timing;
        private readonly IM68kCodeReader? _codeReader;
        private readonly IM68kFastMemoryBus? _fastMemoryBus;
        private readonly M68040LogicalBus? _m68040LogicalBus;
        private readonly IM68kBus? _m68040PhysicalBus;
        private readonly bool _directUncachedInstructionFetch;
        private readonly bool _hasInstructionFetchWaitStates;
        private readonly bool _useM68040UncachedHalfLine;
        private readonly IM68kStablePhysicalAddressMap? _physicalAddressMap;
        private uint _fetchHalfLineAddress;
        private uint _fetchFirstLong;
        private uint _fetchSecondLong;
        private int _fetchValidLongs;
        private uint _fetchMmuGeneration;
        private uint _fetchMapGeneration;
        private bool _fetchSupervisor;
        private uint _exceptionFetchBase;
        private uint _exceptionFetchNext;
        private uint _exceptionFetchFirst;
        private uint _exceptionFetchSecond;
        private uint _exceptionFetchThird;
        private uint _exceptionFetchFourth;
        private bool _exceptionFetchValid;

        public M68kTimedBusAdapter(
            IM68kBus bus,
            M68020CpuProfile profile,
            M68kCpuState state,
            M68kTimingEngine timing)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _timing = timing ?? throw new ArgumentNullException(nameof(timing));
            _codeReader = bus as IM68kCodeReader;
            _fastMemoryBus = bus as IM68kFastMemoryBus;
            _m68040LogicalBus = bus as M68040LogicalBus;
            _m68040PhysicalBus = _m68040LogicalBus?.PhysicalBus;
            _directUncachedInstructionFetch = !profile.FastInstructionFetch;
            _useM68040UncachedHalfLine =
                profile.Model == M68kAcceleratorModel.M68040 && !profile.FastInstructionFetch;
            _physicalAddressMap = _m68040PhysicalBus as IM68kStablePhysicalAddressMap;
            for (var i = 0; i < profile.BusTiming.Count; i++)
            {
                if (profile.BusTiming[i].WaitStates != 0)
                {
                    _hasInstructionFetchWaitStates = true;
                    break;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ushort ReadInstructionFetchWordHot(
            uint address,
            out bool cacheHit,
            out bool requiresSynchronization,
            out long completedMachineCycle)
        {
            if (!_directUncachedInstructionFetch || _timing.InstructionCache.Enabled)
            {
                ResetInstructionFetchBuffer();
                return ReadInstructionFetchWord(
                    address,
                    out cacheHit,
                    out requiresSynchronization,
                    out completedMachineCycle);
            }

            if (_useM68040UncachedHalfLine)
            {
                return ReadM68040UncachedInstructionWord(
                    address, out cacheHit, out requiresSynchronization, out completedMachineCycle);
            }

            cacheHit = false;
            var cycle = GetBusRequestMachineCycle();
            var value = _m68040LogicalBus is { } logicalBus
                ? logicalBus.CanUseDirectIdentityAccess(address, byteCount: 2)
                    ? _m68040PhysicalBus!.ReadWord(
                        address,
                        ref cycle,
                        M68kBusAccessKind.CpuInstructionFetch)
                    : logicalBus.ReadWord(
                        address,
                        ref cycle,
                        M68kBusAccessKind.CpuInstructionFetch)
                : _bus.ReadWord(address, ref cycle, M68kBusAccessKind.CpuInstructionFetch);
            if (_hasInstructionFetchWaitStates)
            {
                AddProfileWaitStates(address, M68020BusWidth.Word, ref cycle);
            }

            _timing.RecordPostedBusCompletion(cycle);
            requiresSynchronization = true;
            completedMachineCycle = cycle;
            return value;
        }

        internal void ResetInstructionFetchBuffer()
        {
            _fetchValidLongs = 0;
            _exceptionFetchValid = false;
        }

        internal bool PrefetchM68040AccessErrorHandler(uint address)
        {
            ResetInstructionFetchBuffer();
            if (!_useM68040UncachedHalfLine || _timing.InstructionCache.Enabled || _state.M68040Mmu.Enabled)
                return false;
            // MC68040UM 8.1, figure 8-1: four longwords precede handler
            // execution. Table 7-3 starts uncached fetches at a half-line.
            // Publish only a complete window; a failed entry remains fatal.
            _exceptionFetchBase = address & ~7u;
            _exceptionFetchFirst = ReadM68040InstructionLong(_exceptionFetchBase, out _);
            _exceptionFetchSecond = ReadM68040InstructionLong(unchecked(_exceptionFetchBase + 4), out _);
            _exceptionFetchThird = ReadM68040InstructionLong(unchecked(_exceptionFetchBase + 8), out _);
            _exceptionFetchFourth = ReadM68040InstructionLong(unchecked(_exceptionFetchBase + 12), out _);
            _exceptionFetchNext = address;
            _fetchSupervisor = (_state.StatusRegister & M68kCpuState.Supervisor) != 0;
            _fetchMmuGeneration = _state.M68040Mmu.Generation;
            _fetchMapGeneration = _physicalAddressMap?.CpuPhysicalAddressMapGeneration ?? 0;
            _exceptionFetchValid = true;
            return true;
        }

        private ushort ReadM68040UncachedInstructionWord(
            uint address,
            out bool cacheHit,
            out bool requiresSynchronization,
            out long completedMachineCycle)
        {
            // MC68040UM table 7-3 and following paragraph: instruction transfers are
            // aligned longwords, starting at the eight-byte half-line boundary.
            // The host bus splits each longword if its physical port is 16-bit.
            var halfLine = address & ~7u;
            var supervisor = (_state.StatusRegister & M68kCpuState.Supervisor) != 0;
            var mmuGeneration = _state.M68040Mmu.Generation;
            var mapGeneration = _physicalAddressMap?.CpuPhysicalAddressMapGeneration ?? 0;
            if (_exceptionFetchValid)
            {
                var offset = unchecked(address - _exceptionFetchBase);
                if (address == _exceptionFetchNext && offset < 16 &&
                    _fetchSupervisor == supervisor && _fetchMmuGeneration == mmuGeneration &&
                    _fetchMapGeneration == mapGeneration)
                {
                    var retained = (offset >> 2) switch {
                        0 => _exceptionFetchFirst, 1 => _exceptionFetchSecond,
                        2 => _exceptionFetchThird, _ => _exceptionFetchFourth
                    };
                    _exceptionFetchNext = unchecked(address + 2);
                    if (offset == 14) _exceptionFetchValid = false;
                    cacheHit = false;
                    requiresSynchronization = false;
                    completedMachineCycle = _state.Cycles;
                    return (ushort)(retained >> ((address & 2) == 0 ? 16 : 0));
                }
                // A changed flow/context must not turn the entry window into
                // an instruction cache, including a branch back into it.
                ResetInstructionFetchBuffer();
            }
            if (_fetchHalfLineAddress != halfLine || _fetchSupervisor != supervisor ||
                _fetchMmuGeneration != mmuGeneration || _fetchMapGeneration != mapGeneration)
            {
                _fetchValidLongs = 0;
            }

            cacheHit = false; // The holding register is independent of CACR.IE.
            requiresSynchronization = false;
            completedMachineCycle = _state.Cycles;
            if (_fetchValidLongs == 0)
            {
                _fetchFirstLong = ReadM68040InstructionLong(halfLine, out completedMachineCycle);
                _fetchHalfLineAddress = halfLine;
                _fetchSupervisor = supervisor;
                _fetchMmuGeneration = mmuGeneration;
                _fetchMapGeneration = mapGeneration;
                _fetchValidLongs = 1;
                requiresSynchronization = true;
            }
            if ((address & 4) != 0 && _fetchValidLongs == 1)
            {
                _fetchSecondLong = ReadM68040InstructionLong(halfLine + 4, out completedMachineCycle);
                _fetchValidLongs = 2;
                requiresSynchronization = true;
            }

            var data = (address & 4) == 0 ? _fetchFirstLong : _fetchSecondLong;
            var word = (ushort)(data >> ((address & 2) == 0 ? 16 : 0));
            // MC68040UM 4.2 guarantees retention for loops within the first six
            // bytes. Retire at the fourth word in this demand-driven frontend;
            // speculative next-half-line traffic and pipeline overlap are not
            // modeled here. Do not turn this into an eight-byte instruction cache.
            if ((address & 7) == 6) _fetchValidLongs = 0;
            return word;
        }

        private uint ReadM68040InstructionLong(uint address, out long completedMachineCycle)
        {
            var cycle = GetBusRequestMachineCycle();
            var value = _m68040LogicalBus is { } logicalBus &&
                logicalBus.CanUseDirectIdentityAccess(address, byteCount: 4)
                    ? _m68040PhysicalBus!.ReadLong(address, ref cycle, M68kBusAccessKind.CpuInstructionFetch)
                    : _bus.ReadLong(address, ref cycle, M68kBusAccessKind.CpuInstructionFetch);
            if (_hasInstructionFetchWaitStates)
            {
                AddProfileWaitStates(address, M68020BusWidth.Long, ref cycle);
            }
            _timing.RecordPostedBusCompletion(cycle);
            completedMachineCycle = cycle;
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal uint ReadDataLongHot(uint address)
        {
            if (_hasInstructionFetchWaitStates || _profile.FastNonChipMemoryAccess)
            {
                return ReadLong(address, M68kBusAccessKind.CpuDataRead);
            }

            var cycle = GetBusRequestMachineCycle();
            var value = _m68040LogicalBus is { } logicalBus
                ? logicalBus.CanUseDirectIdentityAccess(address, byteCount: 4)
                    ? _m68040PhysicalBus!.ReadLong(
                        address,
                        ref cycle,
                        M68kBusAccessKind.CpuDataRead)
                    : logicalBus.ReadLong(address, ref cycle, M68kBusAccessKind.CpuDataRead)
                : _bus.ReadLong(address, ref cycle, M68kBusAccessKind.CpuDataRead);
            _timing.CompleteBlockingBusAccess(cycle);
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void WriteDataLongHot(uint address, uint value)
        {
            if (_hasInstructionFetchWaitStates || _profile.FastNonChipMemoryAccess)
            {
                WriteLong(address, value, M68kBusAccessKind.CpuDataWrite);
                return;
            }

            var cycle = GetBusRequestMachineCycle();
            if (_m68040LogicalBus is { } logicalBus)
            {
                if (logicalBus.CanUseDirectIdentityAccess(address, byteCount: 4))
                {
                    _m68040PhysicalBus!.WriteLong(
                        address,
                        value,
                        ref cycle,
                        M68kBusAccessKind.CpuDataWrite);
                }
                else
                {
                    logicalBus.WriteLong(
                        address,
                        value,
                        ref cycle,
                        M68kBusAccessKind.CpuDataWrite);
                }
            }
            else
            {
                _bus.WriteLong(address, value, ref cycle, M68kBusAccessKind.CpuDataWrite);
            }

            _timing.RecordPostedBusCompletion(cycle);
        }

        internal ushort ReadInstructionFetchWord(
            uint address,
            out bool cacheHit,
            out bool requiresSynchronization,
            out long completedMachineCycle)
        {
            if (_codeReader is not null && _timing.TryReadInstructionCacheWord(address, out var cachedValue))
            {
                cacheHit = true;
                requiresSynchronization = false;
                completedMachineCycle = _state.Cycles;
                return cachedValue;
            }

            if (CanUseFastInstructionFetch(address, M68kBusAccessKind.CpuInstructionFetch) &&
                _codeReader is not null)
            {
                cacheHit = false;
                requiresSynchronization = false;
                completedMachineCycle = _state.Cycles;
                var directValue = _codeReader.ReadHostWord(address);
                _timing.CaptureInstructionCacheLine(address, _codeReader);
                return directValue;
            }

            cacheHit = false;
            var cycle = GetBusRequestMachineCycle();
            var value = _bus.ReadWord(address, ref cycle, M68kBusAccessKind.CpuInstructionFetch);
            AddProfileWaitStates(address, M68020BusWidth.Word, ref cycle);
            _timing.RecordPostedBusCompletion(cycle);
            requiresSynchronization = true;
            completedMachineCycle = cycle;
            if (_codeReader is not null)
            {
                _timing.CaptureInstructionCacheLine(address, _codeReader);
            }
            return value;
        }

        public byte ReadByte(uint address, M68kBusAccessKind accessKind)
        {
            if (CanUseFastCiaAPortAAccess(address, accessKind) &&
                _fastMemoryBus is not null &&
                _fastMemoryBus.TryReadFastByte(address, accessKind, out var ciaFastValue))
            {
                CompleteMinimalBlockingFastAccess();
                return ciaFastValue;
            }

            if (CanUseFastNonChipMemoryAccess(address, accessKind) &&
                _fastMemoryBus is not null &&
                _fastMemoryBus.TryReadFastByte(address, accessKind, out var fastValue))
            {
                return fastValue;
            }

            var cycle = GetBusRequestMachineCycle();
            var value = _bus.ReadByte(address, ref cycle, accessKind);
            AddProfileWaitStates(address, M68020BusWidth.Byte, ref cycle);
            _timing.CompleteBlockingBusAccess(cycle);
            return value;
        }

        public ushort ReadWord(uint address, M68kBusAccessKind accessKind)
        {
            if (CanUseFastInstructionFetch(address, accessKind) &&
                _codeReader is not null)
            {
                return _codeReader.ReadHostWord(address);
            }

            if (CanUseFastNonChipMemoryAccess(address, accessKind) &&
                _fastMemoryBus is not null &&
                _fastMemoryBus.TryReadFastWord(address, accessKind, out var fastValue))
            {
                return fastValue;
            }

            var cycle = GetBusRequestMachineCycle();
            var value = _bus.ReadWord(address, ref cycle, accessKind);
            AddProfileWaitStates(address, M68020BusWidth.Word, ref cycle);
            _timing.CompleteBlockingBusAccess(cycle);
            return value;
        }

        public uint ReadLong(uint address, M68kBusAccessKind accessKind)
        {
            if (CanUseFastNonChipMemoryAccess(address, accessKind) &&
                _fastMemoryBus is not null &&
                _fastMemoryBus.TryReadFastLong(address, accessKind, out var fastValue))
            {
                return fastValue;
            }

            var cycle = GetBusRequestMachineCycle();
            var value = _bus.ReadLong(address, ref cycle, accessKind);
            AddProfileWaitStates(address, M68020BusWidth.Long, ref cycle);
            _timing.CompleteBlockingBusAccess(cycle);
            return value;
        }

        public void WriteByte(uint address, byte value, M68kBusAccessKind accessKind)
        {
            if (CanUseFastCiaAPortAAccess(address, accessKind) &&
                _fastMemoryBus is not null &&
                _fastMemoryBus.TryWriteFastByte(address, value, accessKind))
            {
                RecordMinimalPostedFastAccess();
                return;
            }

            if (CanUseFastNonChipMemoryAccess(address, accessKind) &&
                _fastMemoryBus is not null &&
                _fastMemoryBus.TryWriteFastByte(address, value, accessKind))
            {
                return;
            }

            var cycle = GetBusRequestMachineCycle();
            _bus.WriteByte(address, value, ref cycle, accessKind);
            AddProfileWaitStates(address, M68020BusWidth.Byte, ref cycle);
            _timing.RecordPostedBusCompletion(cycle);
        }

        public void WriteWord(uint address, ushort value, M68kBusAccessKind accessKind)
        {
            if (CanUseFastNonChipMemoryAccess(address, accessKind) &&
                _fastMemoryBus is not null &&
                _fastMemoryBus.TryWriteFastWord(address, value, accessKind))
            {
                return;
            }

            var cycle = GetBusRequestMachineCycle();
            _bus.WriteWord(address, value, ref cycle, accessKind);
            AddProfileWaitStates(address, M68020BusWidth.Word, ref cycle);
            _timing.RecordPostedBusCompletion(cycle);
        }

        public void WriteLong(uint address, uint value, M68kBusAccessKind accessKind)
        {
            if (CanUseFastNonChipMemoryAccess(address, accessKind) &&
                _fastMemoryBus is not null &&
                _fastMemoryBus.TryWriteFastLong(address, value, accessKind))
            {
                return;
            }

            var cycle = GetBusRequestMachineCycle();
            _bus.WriteLong(address, value, ref cycle, accessKind);
            AddProfileWaitStates(address, M68020BusWidth.Long, ref cycle);
            _timing.RecordPostedBusCompletion(cycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long GetBusRequestMachineCycle()
        {
            var nativeReady = Math.Max(_state.NativeCycles, _timing.BusControllerAvailableNativeCycle);
            return Math.Max(_state.Cycles, _profile.NativeToMachineCycles(nativeReady));
        }

        private bool CanUseFastInstructionFetch(uint address, M68kBusAccessKind accessKind)
        {
            if (!_profile.FastInstructionFetch ||
                accessKind != M68kBusAccessKind.CpuInstructionFetch)
            {
                return false;
            }

            return M68020CpuProfile.IsInstructionCacheableTarget(_profile.GetBusTimingRule(address).Target);
        }

        private bool CanUseFastNonChipMemoryAccess(uint address, M68kBusAccessKind accessKind)
        {
            if (!_profile.FastNonChipMemoryAccess ||
                accessKind == M68kBusAccessKind.CpuInstructionFetch)
            {
                return false;
            }

            var target = _profile.GetBusTimingRule(address).Target;
            return target is
                M68020MemoryTarget.ExpansionRam or
                M68020MemoryTarget.RealFastRam or
                M68020MemoryTarget.Rom;
        }

        private bool CanUseFastCiaAPortAAccess(uint address, M68kBusAccessKind accessKind)
            => _profile.FastCiaAPortAAccess &&
                accessKind != M68kBusAccessKind.CpuInstructionFetch &&
                (address & 0x00FF_FFFFu) == 0x00BF_E001u;

        private void CompleteMinimalBlockingFastAccess()
        {
            _timing.CompleteBlockingBusAccess(GetBusRequestMachineCycle() + 1);
        }

        private void RecordMinimalPostedFastAccess()
        {
            _timing.RecordPostedBusCompletion(GetBusRequestMachineCycle() + 1);
        }

        private void AddProfileWaitStates(uint address, M68020BusWidth transferWidth, ref long cycle)
        {
            var rule = _profile.GetBusTimingRule(address);
            var transfers = CountProfileTransfers(address, transferWidth, rule.Width);
            cycle += (long)Math.Max(0, rule.WaitStates) * transfers;
        }

        private static int CountProfileTransfers(uint address, M68020BusWidth transferWidth, M68020BusWidth targetWidth)
        {
            var bytes = (int)transferWidth;
            var width = Math.Max(1, (int)targetWidth);
            var startLane = (int)(address % (uint)width);
            return (startLane + bytes + width - 1) / width;
        }
    }

    internal sealed class UnsupportedM68kTimingException : M68kEmulationException
    {
        public UnsupportedM68kTimingException(ushort opcode, uint programCounter, M68020CpuProfile profile)
            : base($"Unsupported exact {profile.ModelName} timing for opcode 0x{opcode:X4} at 0x{programCounter:X8} in profile {profile.Name}.")
        {
            Opcode = opcode;
            ProgramCounter = programCounter;
            ProfileName = profile.Name;
        }

        public UnsupportedM68kTimingException(M68kInstructionTimingKey key, M68kAcceleratorModel model)
            : base($"Unsupported exact {model} timing plan for {key}.")
        {
            TimingKey = key;
            ProfileName = model.ToString();
        }

        public ushort Opcode { get; }

        public uint ProgramCounter { get; }

        public M68kInstructionTimingKey TimingKey { get; }

        public string ProfileName { get; }
    }
}
