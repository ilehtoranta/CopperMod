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
        MoveFromCcr,
        Movep,
        Chk2Cmp2,
        Moveq,
        ClrDataLong,
        ClrLongAddressIndirect,
        ClrLongAddressDisplacement,
        ClrLongAbsoluteLong,
        NegxLongData,
        NegLongData,
        NotByteData,
        ClrLongPostIncrement,
        ClrDataWord,
        ClrWordAddressDisplacement,
        ClrByteAddressIndirect,
        ClrByteAddressDisplacement,
        LeaAbsoluteLong,
        LeaAbsoluteWord,
        LeaAddressDisplacement,
        LeaPcDisplacement,
        MoveImmediateToStatusRegister,
        MoveByteImmediateToAbsoluteLong,
        MoveByteImmediateToAddressIndirect,
        MoveByteImmediateToAddressDisplacement,
        MoveByteImmediateToBriefIndexed,
        MoveWordImmediateToAddressDisplacement,
        MoveWordImmediateToAbsoluteLong,
        MoveLongImmediateToAbsoluteLong,
        MoveLongImmediateToAbsoluteWord,
        MoveLongImmediateToAddressIndirect,
        MoveLongAbsoluteWordToAddressDisplacement,
        MoveLongAbsoluteLongToAddressDisplacement,
        MoveLongImmediateToAddressDisplacement,
        MoveLongImmediateToBriefIndexed,
        MoveLongImmediateToPostIncrement,
        MoveLongImmediateToData,
        MoveLongImmediateToAddress,
        MoveWordImmediateToData,
        MoveByteImmediateToData,
        MoveLongDataToData,
        MoveLongDataToAddress,
        MoveLongDataToAddressIndirect,
        MoveLongDataToAddressDisplacement,
        MoveLongAddressToAddress,
        MoveLongAddressToAddressIndirect,
        MoveLongAddressToAddressDisplacement,
        MoveLongAddressToPostIncrement,
        MoveLongAddressToData,
        MoveLongAddressIndirectToData,
        MoveWordAddressIndirectToData,
        MoveLongAddressIndirectToAddress,
        MoveLongPostIncrementToData,
        MoveLongPostIncrementToAddress,
        MoveLongAddressDisplacementToData,
        MoveLongAddressDisplacementToAddress,
        MoveLongAddressDisplacementToPostIncrement,
        MoveLongBriefIndexedToData,
        MoveLongBriefIndexedToAddress,
        MoveLongAddressIndirectToAddressIndirect,
        MoveLongAbsoluteLongToData,
        MoveLongDataToAbsoluteWord,
        MoveLongDataToAbsoluteLong,
        MoveLongAddressToAbsoluteWord,
        MoveLongAddressToAbsoluteLong,
        MoveByteDataToData,
        MoveByteAddressIndirectToData,
        MoveBytePostIncrementToData,
        MoveByteAddressDisplacementToData,
        MoveByteBriefIndexedToData,
        MoveByteAbsoluteLongToData,
        MoveWordAbsoluteLongToData,
        MoveWordAddressDisplacementToData,
        MoveWordDataToAbsoluteLong,
        MoveWordDataToAddressDisplacement,
        MoveWordAbsoluteLongToAbsoluteLong,
        MoveWordAbsoluteLongToAddressDisplacement,
        MoveByteDataToAbsoluteLong,
        MoveByteDataToAddressIndirect,
        MoveByteDataToAddressDisplacement,
        MoveByteDataToBriefIndexed,
        MoveByteBriefIndexedToPredecrement,
        MoveByteDataToPostIncrement,
        MoveByteDataToPredecrement,
        MoveBytePostIncrementToPostIncrement,
        MoveByteAddressIndirectToAbsoluteLong,
        MoveByteAbsoluteLongToAbsoluteLong,
        ImmediateLogicalByteToAbsoluteLong,
        AddiByteImmediateToData,
        AddiByteImmediateToAddressIndirect,
        AddiByteImmediateToAddressDisplacement,
        AddiWordImmediateToData,
        AddiLongImmediateToData,
        AddiLongImmediateToAbsoluteLong,
        SubiByteImmediateToData,
        SubiByteImmediateToAddressDisplacement,
        SubiLongImmediateToData,
        SubByteDataToData,
        SubLongDataToData,
        SubLongAddressToData,
        SubLongAddressDisplacementToData,
        AddWordDataToData,
        AddLongDataToData,
        AddxLongDataToData,
        AddLongPostIncrementToData,
        AddWordDataToAddressDisplacement,
        AddLongDataToAddressDisplacement,
        AddqLongData,
        AddaLongImmediateToAddress,
        AddaLongDataToAddress,
        AddaLongAddressDisplacementToAddress,
        SubaLongImmediateToAddress,
        ChkWordImmediate,
        LongMultiplyDivide,
        Cas2,
        Cas,
        DivideWordUnsigned,
        DivideWordSigned,
        AndiWordImmediateToData,
        AndLongImmediateToData,
        EoriWordImmediateToData,
        EoriLongImmediateToData,
        MultiplyWordUnsigned,
        MultiplyWordSigned,
        AndByteDataToData,
        BcdByteAdd,
        BcdByteSubtract,
        OriByteImmediateToData,
        BtstByteImmediateAbsoluteLong,
        BchgByteImmediateAbsoluteLong,
        BclrByteImmediateAbsoluteLong,
        BsetByteImmediateAbsoluteLong,
        BsetByteImmediateAddressDisplacement,
        BtstImmediateData,
        BtstDynamicData,
        BsetImmediateData,
        BclrImmediateData,
        BclrDynamicData,
        SwapData,
        ExtWordData,
        TstWordData,
        BitField,
        LsrWordImmediateData,
        AsrLongImmediateData,
        AsrWordImmediateData,
        LsrLongImmediateData,
        AslLongImmediateData,
        AslWordImmediateData,
        RorByteImmediateData,
        RorWordImmediateData,
        RolWordImmediateData,
        CmpiByteImmediateToData,
        CmpiByteImmediateToAddressIndirect,
        CmpiByteImmediateToAddressDisplacement,
        CmpiWordImmediateToData,
        CmpiWordImmediateToAddressDisplacement,
        CmpiWordImmediateToAbsoluteLong,
        CmpiLongImmediateToData,
        CmpiLongImmediateToPostIncrement,
        CmpiLongImmediateToAddressDisplacement,
        CmpaLongImmediateToAddress,
        CmpaLongDataToAddress,
        CmpaLongAddressToAddress,
        CmpaLongAddressIndirectToAddress,
        CmpLongDataToData,
        CmpLongAddressToData,
        CmpLongAddressIndirectToData,
        CmpLongPostIncrementToData,
        CmpByteDataToData,
        CmpByteAddressIndirectToData,
        CmpByteAddressDisplacementToData,
        CmpByteAbsoluteLongToData,
        CmpWordDataToData,
        CmpWordAddressDisplacementToData,
        CmpiLongImmediateToAbsoluteLong,
        Nop,
        Movec,
        Trap,
        Rte,
        Rtd,
        Rts,
        JmpAddressIndirect,
        JsrAbsoluteLong,
        JsrPcDisplacement,
        JmpAbsoluteLong,
        LinkLong,
        NbcdByte,
        ExtbLong,
        MovemLongRegistersToPredecrement,
        MovemLongPostIncrementToRegisters,
        LongBranch,
        ByteBranch,
        WordBranch,
        Trapcc,
        SccAbsoluteLong,
        Dbcc,
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
                return M68020OpcodeKind.Chk2Cmp2;
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

            if ((opcode & 0xFFF8) == 0x4080)
            {
                return M68020OpcodeKind.NegxLongData;
            }

            if ((opcode & 0xFFF8) == 0x4480)
            {
                return M68020OpcodeKind.NegLongData;
            }

            if ((opcode & 0xFFF8) == 0x4600)
            {
                return M68020OpcodeKind.NotByteData;
            }

            if ((opcode & 0xFFF8) == 0x4298)
            {
                return M68020OpcodeKind.ClrLongPostIncrement;
            }

            if ((opcode & 0xFFF8) == 0x4240)
            {
                return M68020OpcodeKind.ClrDataWord;
            }

            if ((opcode & 0xFFF8) == 0x4268)
            {
                return M68020OpcodeKind.ClrWordAddressDisplacement;
            }

            if ((opcode & 0xFFF8) == 0x4210)
            {
                return M68020OpcodeKind.ClrByteAddressIndirect;
            }

            if ((opcode & 0xFFF8) == 0x4228)
            {
                return M68020OpcodeKind.ClrByteAddressDisplacement;
            }

            if ((opcode & 0xF1FF) == 0x41F9)
            {
                return M68020OpcodeKind.LeaAbsoluteLong;
            }

            if ((opcode & 0xF1FF) == 0x41F8)
            {
                return M68020OpcodeKind.LeaAbsoluteWord;
            }

            if ((opcode & 0xF1F8) == 0x41E8)
            {
                return M68020OpcodeKind.LeaAddressDisplacement;
            }

            if ((opcode & 0xF1FF) == 0x41FA)
            {
                return M68020OpcodeKind.LeaPcDisplacement;
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

            if ((opcode & 0xF1FF) == 0x117C)
            {
                return M68020OpcodeKind.MoveByteImmediateToAddressDisplacement;
            }

            if ((opcode & 0xF1FF) == 0x11BC)
            {
                return M68020OpcodeKind.MoveByteImmediateToBriefIndexed;
            }

            if ((opcode & 0xF1FF) == 0x317C)
            {
                return M68020OpcodeKind.MoveWordImmediateToAddressDisplacement;
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

            if ((opcode & 0xF1F8) == 0x20B8)
            {
                return M68020OpcodeKind.MoveLongImmediateToAddressIndirect;
            }

            if ((opcode & 0xF1FF) == 0x2178)
            {
                return M68020OpcodeKind.MoveLongAbsoluteWordToAddressDisplacement;
            }

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

            if ((opcode & 0xF1FF) == 0x203C)
            {
                return M68020OpcodeKind.MoveLongImmediateToData;
            }

            if ((opcode & 0xF1FF) == 0x207C)
            {
                return M68020OpcodeKind.MoveLongImmediateToAddress;
            }

            if ((opcode & 0xF1FF) == 0x303C)
            {
                return M68020OpcodeKind.MoveWordImmediateToData;
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

            if ((opcode & 0xF1F8) == 0x2140)
            {
                return M68020OpcodeKind.MoveLongDataToAddressDisplacement;
            }

            if ((opcode & 0xF1F8) == 0x2048)
            {
                return M68020OpcodeKind.MoveLongAddressToAddress;
            }

            if ((opcode & 0xF1F8) == 0x2088)
            {
                return M68020OpcodeKind.MoveLongAddressToAddressIndirect;
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

            if ((opcode & 0xF1F8) == 0x2058)
            {
                return M68020OpcodeKind.MoveLongPostIncrementToAddress;
            }

            if ((opcode & 0xF1F8) == 0x2028)
            {
                return M68020OpcodeKind.MoveLongAddressDisplacementToData;
            }

            if ((opcode & 0xF1F8) == 0x2068)
            {
                return M68020OpcodeKind.MoveLongAddressDisplacementToAddress;
            }

            if ((opcode & 0xF1F8) == 0x20E8)
            {
                return M68020OpcodeKind.MoveLongAddressDisplacementToPostIncrement;
            }

            if ((opcode & 0xF1F8) == 0x2030)
            {
                return M68020OpcodeKind.MoveLongBriefIndexedToData;
            }

            if ((opcode & 0xF1F8) == 0x2070)
            {
                return M68020OpcodeKind.MoveLongBriefIndexedToAddress;
            }

            if ((opcode & 0xF1F8) == 0x2090)
            {
                return M68020OpcodeKind.MoveLongAddressIndirectToAddressIndirect;
            }

            if ((opcode & 0xF1FF) == 0x2039)
            {
                return M68020OpcodeKind.MoveLongAbsoluteLongToData;
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

            if ((opcode & 0xF1F8) == 0x1000)
            {
                return M68020OpcodeKind.MoveByteDataToData;
            }

            if ((opcode & 0xF1F8) == 0x1010)
            {
                return M68020OpcodeKind.MoveByteAddressIndirectToData;
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

            if ((opcode & 0xF1FF) == 0x1039)
            {
                return M68020OpcodeKind.MoveByteAbsoluteLongToData;
            }

            if ((opcode & 0xF1FF) == 0x3039)
            {
                return M68020OpcodeKind.MoveWordAbsoluteLongToData;
            }

            if ((opcode & 0xF1F8) == 0x3028)
            {
                return M68020OpcodeKind.MoveWordAddressDisplacementToData;
            }

            if ((opcode & 0xF1F8) == 0x31C0)
            {
                return M68020OpcodeKind.MoveWordDataToAbsoluteLong;
            }

            if ((opcode & 0xF1F8) == 0x3140)
            {
                return M68020OpcodeKind.MoveWordDataToAddressDisplacement;
            }

            if (opcode == 0x33F9)
            {
                return M68020OpcodeKind.MoveWordAbsoluteLongToAbsoluteLong;
            }

            if ((opcode & 0xF1FF) == 0x3179)
            {
                return M68020OpcodeKind.MoveWordAbsoluteLongToAddressDisplacement;
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

            if (opcode is 0x0039 or 0x0239)
            {
                return M68020OpcodeKind.ImmediateLogicalByteToAbsoluteLong;
            }

            if ((opcode & 0xFFF8) == 0x0600)
            {
                return M68020OpcodeKind.AddiByteImmediateToData;
            }

            if ((opcode & 0xFFF8) == 0x0610)
            {
                return M68020OpcodeKind.AddiByteImmediateToAddressIndirect;
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

            if (opcode == 0x06B9)
            {
                return M68020OpcodeKind.AddiLongImmediateToAbsoluteLong;
            }

            if ((opcode & 0xFFF8) == 0x0400)
            {
                return M68020OpcodeKind.SubiByteImmediateToData;
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

            if ((opcode & 0xF1F8) == 0xD040)
            {
                return M68020OpcodeKind.AddWordDataToData;
            }

            if ((opcode & 0xF1F8) == 0xD080)
            {
                return M68020OpcodeKind.AddLongDataToData;
            }

            if ((opcode & 0xF1F8) == 0xD180)
            {
                return M68020OpcodeKind.AddxLongDataToData;
            }

            if ((opcode & 0xF1F8) == 0xD098)
            {
                return M68020OpcodeKind.AddLongPostIncrementToData;
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

            if ((opcode & 0xF1FF) == 0xD1FC)
            {
                return M68020OpcodeKind.AddaLongImmediateToAddress;
            }

            if ((opcode & 0xF1F8) == 0xD1C0)
            {
                return M68020OpcodeKind.AddaLongDataToAddress;
            }

            if ((opcode & 0xF1F8) == 0xD1E8)
            {
                return M68020OpcodeKind.AddaLongAddressDisplacementToAddress;
            }

            if ((opcode & 0xF1FF) == 0x91FC)
            {
                return M68020OpcodeKind.SubaLongImmediateToAddress;
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

            if ((opcode & 0xFFF8) == 0x0280)
            {
                return M68020OpcodeKind.AndLongImmediateToData;
            }

            if ((opcode & 0xFFF8) == 0x0A40)
            {
                return M68020OpcodeKind.EoriWordImmediateToData;
            }

            if ((opcode & 0xFFF8) == 0x0A80)
            {
                return M68020OpcodeKind.EoriLongImmediateToData;
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

            if (opcode == 0x0839)
            {
                return M68020OpcodeKind.BtstByteImmediateAbsoluteLong;
            }

            if (opcode == 0x0879)
            {
                return M68020OpcodeKind.BchgByteImmediateAbsoluteLong;
            }

            if (opcode == 0x08B9)
            {
                return M68020OpcodeKind.BclrByteImmediateAbsoluteLong;
            }

            if (opcode == 0x08F9)
            {
                return M68020OpcodeKind.BsetByteImmediateAbsoluteLong;
            }

            if ((opcode & 0xFFF8) == 0x08E8)
            {
                return M68020OpcodeKind.BsetByteImmediateAddressDisplacement;
            }

            if ((opcode & 0xFFF8) == 0x0800)
            {
                return M68020OpcodeKind.BtstImmediateData;
            }

            if ((opcode & 0xF1F8) == 0x0100)
            {
                return M68020OpcodeKind.BtstDynamicData;
            }

            if ((opcode & 0xFFF8) == 0x08C0)
            {
                return M68020OpcodeKind.BsetImmediateData;
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
                return M68020OpcodeKind.ExtWordData;
            }

            if ((opcode & 0xFFF8) == 0x4A40)
            {
                return M68020OpcodeKind.TstWordData;
            }

            if ((opcode & 0xF8C0) == 0xE8C0)
            {
                return M68020OpcodeKind.BitField;
            }

            if ((opcode & 0xF1F8) == 0xE048)
            {
                return M68020OpcodeKind.LsrWordImmediateData;
            }

            if ((opcode & 0xF1F8) == 0xE080)
            {
                return M68020OpcodeKind.AsrLongImmediateData;
            }

            if ((opcode & 0xF1F8) == 0xE040)
            {
                return M68020OpcodeKind.AsrWordImmediateData;
            }

            if ((opcode & 0xF1F8) == 0xE088)
            {
                return M68020OpcodeKind.LsrLongImmediateData;
            }

            if ((opcode & 0xF1F8) == 0xE180)
            {
                return M68020OpcodeKind.AslLongImmediateData;
            }

            if ((opcode & 0xF1F8) == 0xE140)
            {
                return M68020OpcodeKind.AslWordImmediateData;
            }

            if ((opcode & 0xF1F8) == 0xE018)
            {
                return M68020OpcodeKind.RorByteImmediateData;
            }

            if ((opcode & 0xF1F8) == 0xE058)
            {
                return M68020OpcodeKind.RorWordImmediateData;
            }

            if ((opcode & 0xF1F8) == 0xE158)
            {
                return M68020OpcodeKind.RolWordImmediateData;
            }

            if ((opcode & 0xFFF8) == 0x0C00)
            {
                return M68020OpcodeKind.CmpiByteImmediateToData;
            }

            if ((opcode & 0xFFF8) == 0x0C10)
            {
                return M68020OpcodeKind.CmpiByteImmediateToAddressIndirect;
            }

            if ((opcode & 0xFFF8) == 0x0C28)
            {
                return M68020OpcodeKind.CmpiByteImmediateToAddressDisplacement;
            }

            if ((opcode & 0xFFF8) == 0x0C40)
            {
                return M68020OpcodeKind.CmpiWordImmediateToData;
            }

            if ((opcode & 0xFFF8) == 0x0C68)
            {
                return M68020OpcodeKind.CmpiWordImmediateToAddressDisplacement;
            }

            if (opcode == 0x0C79)
            {
                return M68020OpcodeKind.CmpiWordImmediateToAbsoluteLong;
            }

            if ((opcode & 0xFFF8) == 0x0C80)
            {
                return M68020OpcodeKind.CmpiLongImmediateToData;
            }

            if ((opcode & 0xFFF8) == 0x0C98)
            {
                return M68020OpcodeKind.CmpiLongImmediateToPostIncrement;
            }

            if ((opcode & 0xFFF8) == 0x0CA8)
            {
                return M68020OpcodeKind.CmpiLongImmediateToAddressDisplacement;
            }

            if ((opcode & 0xF1FF) == 0xB1FC)
            {
                return M68020OpcodeKind.CmpaLongImmediateToAddress;
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

            if ((opcode & 0xF1F8) == 0xB080)
            {
                return M68020OpcodeKind.CmpLongDataToData;
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

            if ((opcode & 0xF1F8) == 0xB000)
            {
                return M68020OpcodeKind.CmpByteDataToData;
            }

            if ((opcode & 0xF1F8) == 0xB010)
            {
                return M68020OpcodeKind.CmpByteAddressIndirectToData;
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

            if ((opcode & 0xF1F8) == 0xB068)
            {
                return M68020OpcodeKind.CmpWordAddressDisplacementToData;
            }

            if (opcode == 0x0CB9)
            {
                return M68020OpcodeKind.CmpiLongImmediateToAbsoluteLong;
            }

            if (opcode == 0x4E71)
            {
                return M68020OpcodeKind.Nop;
            }

            if (opcode is 0x4E7A or 0x4E7B)
            {
                return M68020OpcodeKind.Movec;
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

            if (opcode == 0x4EB9)
            {
                return M68020OpcodeKind.JsrAbsoluteLong;
            }

            if (opcode == 0x4EBA)
            {
                return M68020OpcodeKind.JsrPcDisplacement;
            }

            if (opcode == 0x4EF9)
            {
                return M68020OpcodeKind.JmpAbsoluteLong;
            }

            if ((opcode & 0xFFF8) == 0x4808)
            {
                return M68020OpcodeKind.LinkLong;
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

            if ((opcode & 0xFFF8) == 0x4CD8)
            {
                return M68020OpcodeKind.MovemLongPostIncrementToRegisters;
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

            if ((opcode & 0xF0FF) == 0x50F9)
            {
                return M68020OpcodeKind.SccAbsoluteLong;
            }

            if ((opcode & 0xF0F8) == 0x50C8)
            {
                return M68020OpcodeKind.Dbcc;
            }

            return M68020OpcodeKind.Unsupported;
        }
    }

    internal class M68020Interpreter : IM68kBatchCore, IM68kInstructionFrequencyProvider
    {
        private const uint SubroutineSentinel = 0xFFFF_FFFC;
        protected const ushort Format0ExceptionFrame = 0x0000;
        protected readonly IM68kBus _bus;
        internal readonly M68020CpuProfile _profile;
        internal readonly M68kInstructionFrequencyMatrix _instructionFrequency;
        internal readonly M68kTimingEngine _timing;
        internal readonly M68kAcceleratorBusBridge _busBridge;
        private readonly M68020OpcodeKind[] _opcodeKinds;
        private readonly bool _hasModelSpecificInstructions;
        private readonly bool _enableM68020StackMode;

        public M68020Interpreter(IM68kBus bus)
            : this(bus, M68020CpuProfile.OcsAccelerator14Mhz)
        {
        }

        internal M68020Interpreter(IM68kBus bus, M68020CpuProfile profile)
            : this(bus, profile, new M68kCpuState())
        {
        }

        internal M68020Interpreter(
            IM68kBus bus,
            M68020CpuProfile profile,
            M68kCpuState state,
            M68kInstructionFrequencyMatrix? instructionFrequency = null,
            bool enableM68020StackMode = true,
            M68020OpcodeKind[]? opcodeKinds = null,
            bool hasModelSpecificInstructions = false)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            State = state ?? throw new ArgumentNullException(nameof(state));
            _opcodeKinds = opcodeKinds ?? M68020OpcodeDispatchTable.M68020Kinds;
            _hasModelSpecificInstructions = hasModelSpecificInstructions;
            _enableM68020StackMode = enableM68020StackMode;
            if (_enableM68020StackMode)
            {
                State.EnableM68020StackMode();
            }

            _instructionFrequency = instructionFrequency ?? new M68kInstructionFrequencyMatrix();
            _timing = new M68kTimingEngine(_profile, State);
            _busBridge = new M68kAcceleratorBusBridge(_bus, _profile, State, _timing);
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

                if (!boundary.BeforeInstruction())
                {
                    break;
                }

                var previousCycle = State.Cycles;
                ExecuteInstruction();
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
            if (State.Halted || State.Stopped)
            {
                CompleteTiming(M68kInstructionTimingKey.Idle);
                return (int)(State.Cycles - startCycles);
            }

            if ((State.ProgramCounter & 1) != 0)
            {
                var instructionPc = State.ProgramCounter;
                BeginInstruction(0);
                RaiseFormat0Exception(3, instructionPc, M68kInstructionTimingKey.IllegalInstruction);
                return (int)(State.Cycles - startCycles);
            }

            if (!TryPeekOpcode(State.ProgramCounter, out var opcode))
            {
                throw new UnsupportedM68kTimingException(0, State.ProgramCounter, _profile);
            }

            if (TryExecuteM68020Instruction(opcode))
            {
                return (int)(State.Cycles - startCycles);
            }

            if (TryExecuteApproximateInstruction(opcode))
            {
                return (int)(State.Cycles - startCycles);
            }

            throw new UnsupportedM68kTimingException(opcode, State.ProgramCounter, _profile);
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
        }

        public void BeginSubroutine(uint address, uint stackPointer, uint returnAddress)
        {
            State.SetActiveStackPointer(stackPointer);
            PushLong(returnAddress);
            State.ProgramCounter = address;
            State.Halted = false;
            State.Stopped = false;
        }

        public void RequestInterrupt(int level, uint vectorAddress)
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
                        var trapId = FetchWord();
                        var returnProgramCounter = State.ProgramCounter;
                        if (_bus.TryInvokeHostTrap(State.LastInstructionProgramCounter, trapId, State))
                        {
                            if (!State.Halted && State.ProgramCounter == returnProgramCounter)
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

                case M68020OpcodeKind.MoveFromCcr:
                    ExecuteMoveFromCcr(opcode);
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

                case M68020OpcodeKind.NegxLongData:
                    ExecuteNegxLongData(opcode);
                    return true;

                case M68020OpcodeKind.NegLongData:
                    ExecuteNegLongData(opcode);
                    return true;

                case M68020OpcodeKind.NotByteData:
                    ExecuteNotByteData(opcode);
                    return true;

                case M68020OpcodeKind.ClrLongPostIncrement:
                    ExecuteClrLongPostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.ClrDataWord:
                    ExecuteClrDataWord(opcode);
                    return true;

                case M68020OpcodeKind.ClrWordAddressDisplacement:
                    ExecuteClrWordAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.ClrByteAddressIndirect:
                    ExecuteClrByteAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.ClrByteAddressDisplacement:
                    ExecuteClrByteAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.LeaAbsoluteLong:
                    ExecuteLeaAbsoluteLong(opcode);
                    return true;

                case M68020OpcodeKind.LeaAbsoluteWord:
                    ExecuteLeaAbsoluteWord(opcode);
                    return true;

                case M68020OpcodeKind.LeaAddressDisplacement:
                    ExecuteLeaAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.LeaPcDisplacement:
                    ExecuteLeaPcDisplacement(opcode);
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

                case M68020OpcodeKind.MoveByteImmediateToAddressDisplacement:
                    ExecuteMoveByteImmediateToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteImmediateToBriefIndexed:
                    ExecuteMoveByteImmediateToBriefIndexed(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordImmediateToAddressDisplacement:
                    ExecuteMoveWordImmediateToAddressDisplacement(opcode);
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

                case M68020OpcodeKind.MoveLongImmediateToData:
                    ExecuteMoveLongImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongImmediateToAddress:
                    ExecuteMoveLongImmediateToAddress(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordImmediateToData:
                    ExecuteMoveWordImmediateToData(opcode);
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

                case M68020OpcodeKind.MoveLongDataToAddressDisplacement:
                    ExecuteMoveLongDataToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressToAddress:
                    ExecuteMoveLongAddressToAddress(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressToAddressIndirect:
                    ExecuteMoveLongAddressToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressToAddressDisplacement:
                    ExecuteMoveLongAddressToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressToPostIncrement:
                    ExecuteMoveLongAddressToPostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressToData:
                    ExecuteMoveLongAddressToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressIndirectToData:
                    ExecuteMoveLongAddressIndirectToData(opcode);
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

                case M68020OpcodeKind.MoveLongPostIncrementToAddress:
                    ExecuteMoveLongPostIncrementToAddress(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressDisplacementToData:
                    ExecuteMoveLongAddressDisplacementToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressDisplacementToAddress:
                    ExecuteMoveLongAddressDisplacementToAddress(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressDisplacementToPostIncrement:
                    ExecuteMoveLongAddressDisplacementToPostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongBriefIndexedToData:
                    ExecuteMoveLongBriefIndexedToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongBriefIndexedToAddress:
                    ExecuteMoveLongBriefIndexedToAddress(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAddressIndirectToAddressIndirect:
                    ExecuteMoveLongAddressIndirectToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.MoveLongAbsoluteLongToData:
                    ExecuteMoveLongAbsoluteLongToData(opcode);
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

                case M68020OpcodeKind.MoveByteDataToData:
                    ExecuteMoveByteDataToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveByteAddressIndirectToData:
                    ExecuteMoveByteAddressIndirectToData(opcode);
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

                case M68020OpcodeKind.MoveByteAbsoluteLongToData:
                    ExecuteMoveByteAbsoluteLongToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordAbsoluteLongToData:
                    ExecuteMoveWordAbsoluteLongToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordAddressDisplacementToData:
                    ExecuteMoveWordAddressDisplacementToData(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordDataToAbsoluteLong:
                    ExecuteMoveWordDataToAbsoluteLong(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordDataToAddressDisplacement:
                    ExecuteMoveWordDataToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.MoveWordAbsoluteLongToAbsoluteLong:
                    ExecuteMoveWordAbsoluteLongToAbsoluteLong();
                    return true;

                case M68020OpcodeKind.MoveWordAbsoluteLongToAddressDisplacement:
                    ExecuteMoveWordAbsoluteLongToAddressDisplacement(opcode);
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
                    ExecuteImmediateLogicalByteToAbsoluteLong(opcode);
                    return true;

                case M68020OpcodeKind.AddiByteImmediateToData:
                    ExecuteAddiByteImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.AddiByteImmediateToAddressIndirect:
                    ExecuteAddiByteImmediateToAddressIndirect(opcode);
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

                case M68020OpcodeKind.AddiLongImmediateToAbsoluteLong:
                    ExecuteAddiLongImmediateToAbsoluteLong();
                    return true;

                case M68020OpcodeKind.SubiByteImmediateToData:
                    ExecuteSubiByteImmediateToData(opcode);
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

                case M68020OpcodeKind.SubLongDataToData:
                    ExecuteSubLongDataToData(opcode);
                    return true;

                case M68020OpcodeKind.SubLongAddressToData:
                    ExecuteSubLongAddressToData(opcode);
                    return true;

                case M68020OpcodeKind.SubLongAddressDisplacementToData:
                    ExecuteSubLongAddressDisplacementToData(opcode);
                    return true;

                case M68020OpcodeKind.AddWordDataToData:
                    ExecuteAddWordDataToData(opcode);
                    return true;

                case M68020OpcodeKind.AddLongDataToData:
                    ExecuteAddLongDataToData(opcode);
                    return true;

                case M68020OpcodeKind.AddxLongDataToData:
                    ExecuteAddxLongDataToData(opcode);
                    return true;

                case M68020OpcodeKind.AddLongPostIncrementToData:
                    ExecuteAddLongPostIncrementToData(opcode);
                    return true;

                case M68020OpcodeKind.AddWordDataToAddressDisplacement:
                    ExecuteAddWordDataToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.AddLongDataToAddressDisplacement:
                    ExecuteAddLongDataToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.AddqLongData:
                    ExecuteAddqLongData(opcode);
                    return true;

                case M68020OpcodeKind.AddaLongImmediateToAddress:
                    ExecuteAddaLongImmediateToAddress(opcode);
                    return true;

                case M68020OpcodeKind.AddaLongDataToAddress:
                    ExecuteAddaLongDataToAddress(opcode);
                    return true;

                case M68020OpcodeKind.AddaLongAddressDisplacementToAddress:
                    ExecuteAddaLongAddressDisplacementToAddress(opcode);
                    return true;

                case M68020OpcodeKind.SubaLongImmediateToAddress:
                    ExecuteSubaLongImmediateToAddress(opcode);
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

                case M68020OpcodeKind.AndLongImmediateToData:
                    ExecuteAndLongImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.EoriWordImmediateToData:
                    ExecuteEoriWordImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.EoriLongImmediateToData:
                    ExecuteEoriLongImmediateToData(opcode);
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

                case M68020OpcodeKind.BcdByteAdd:
                    ExecuteBcdByte(opcode, subtract: false);
                    return true;

                case M68020OpcodeKind.BcdByteSubtract:
                    ExecuteBcdByte(opcode, subtract: true);
                    return true;

                case M68020OpcodeKind.OriByteImmediateToData:
                    ExecuteOriByteImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.BtstByteImmediateAbsoluteLong:
                    ExecuteBtstByteImmediateAbsoluteLong();
                    return true;

                case M68020OpcodeKind.BchgByteImmediateAbsoluteLong:
                    ExecuteBchgByteImmediateAbsoluteLong();
                    return true;

                case M68020OpcodeKind.BclrByteImmediateAbsoluteLong:
                    ExecuteBclrByteImmediateAbsoluteLong();
                    return true;

                case M68020OpcodeKind.BsetByteImmediateAbsoluteLong:
                    ExecuteBsetByteImmediateAbsoluteLong();
                    return true;

                case M68020OpcodeKind.BsetByteImmediateAddressDisplacement:
                    ExecuteBsetByteImmediateAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.BtstImmediateData:
                    ExecuteBtstImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.BtstDynamicData:
                    ExecuteBtstDynamicData(opcode);
                    return true;

                case M68020OpcodeKind.BsetImmediateData:
                    ExecuteBsetImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.BclrImmediateData:
                    ExecuteBclrImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.BclrDynamicData:
                    ExecuteBclrDynamicData(opcode);
                    return true;

                case M68020OpcodeKind.SwapData:
                    ExecuteSwapData(opcode);
                    return true;

                case M68020OpcodeKind.ExtWordData:
                    ExecuteExtWordData(opcode);
                    return true;

                case M68020OpcodeKind.TstWordData:
                    ExecuteTstWordData(opcode);
                    return true;

                case M68020OpcodeKind.BitField:
                    ExecuteBitField(opcode);
                    return true;

                case M68020OpcodeKind.LsrWordImmediateData:
                    ExecuteLsrWordImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.AsrLongImmediateData:
                    ExecuteAsrLongImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.AsrWordImmediateData:
                    ExecuteAsrWordImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.LsrLongImmediateData:
                    ExecuteLsrLongImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.AslLongImmediateData:
                    ExecuteAslLongImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.AslWordImmediateData:
                    ExecuteAslWordImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.RorByteImmediateData:
                    ExecuteRorByteImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.RorWordImmediateData:
                    ExecuteRorWordImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.RolWordImmediateData:
                    ExecuteRolWordImmediateData(opcode);
                    return true;

                case M68020OpcodeKind.CmpiByteImmediateToData:
                    ExecuteCmpiByteImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.CmpiByteImmediateToAddressIndirect:
                    ExecuteCmpiByteImmediateToAddressIndirect(opcode);
                    return true;

                case M68020OpcodeKind.CmpiByteImmediateToAddressDisplacement:
                    ExecuteCmpiByteImmediateToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.CmpiWordImmediateToData:
                    ExecuteCmpiWordImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.CmpiWordImmediateToAddressDisplacement:
                    ExecuteCmpiWordImmediateToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.CmpiWordImmediateToAbsoluteLong:
                    ExecuteCmpiWordImmediateToAbsoluteLong();
                    return true;

                case M68020OpcodeKind.CmpiLongImmediateToData:
                    ExecuteCmpiLongImmediateToData(opcode);
                    return true;

                case M68020OpcodeKind.CmpiLongImmediateToPostIncrement:
                    ExecuteCmpiLongImmediateToPostIncrement(opcode);
                    return true;

                case M68020OpcodeKind.CmpiLongImmediateToAddressDisplacement:
                    ExecuteCmpiLongImmediateToAddressDisplacement(opcode);
                    return true;

                case M68020OpcodeKind.CmpaLongImmediateToAddress:
                    ExecuteCmpaLongImmediateToAddress(opcode);
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

                case M68020OpcodeKind.CmpLongDataToData:
                    ExecuteCmpLongDataToData(opcode);
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

                case M68020OpcodeKind.CmpByteDataToData:
                    ExecuteCmpByteDataToData(opcode);
                    return true;

                case M68020OpcodeKind.CmpByteAddressIndirectToData:
                    ExecuteCmpByteAddressIndirectToData(opcode);
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

                case M68020OpcodeKind.CmpWordAddressDisplacementToData:
                    ExecuteCmpWordAddressDisplacementToData(opcode);
                    return true;

                case M68020OpcodeKind.CmpiLongImmediateToAbsoluteLong:
                    ExecuteCmpiLongImmediateToAbsoluteLong();
                    return true;

                case M68020OpcodeKind.Nop:
                    ExecuteNop();
                    return true;

                case M68020OpcodeKind.Movec:
                    ExecuteMovec(opcode);
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

                case M68020OpcodeKind.JsrAbsoluteLong:
                    ExecuteJsrAbsoluteLong();
                    return true;

                case M68020OpcodeKind.JsrPcDisplacement:
                    ExecuteJsrPcDisplacement();
                    return true;

                case M68020OpcodeKind.JmpAbsoluteLong:
                    ExecuteJmpAbsoluteLong();
                    return true;

                case M68020OpcodeKind.LinkLong:
                    ExecuteLinkLong(opcode);
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

                case M68020OpcodeKind.MovemLongPostIncrementToRegisters:
                    ExecuteMovemLongPostIncrementToRegisters(opcode);
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

                case M68020OpcodeKind.SccAbsoluteLong:
                    ExecuteSccAbsoluteLong(opcode);
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

        private void ExecuteJmpAbsoluteLong()
        {
            BeginInstruction(0x4EF9);
            _ = FetchWord();
            State.ProgramCounter = FetchLong();
            CompleteTiming(M68kInstructionTimingKey.JmpAbsoluteLong);
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

        private void ExecuteMoveLongBriefIndexedToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var baseRegister = opcode & 7;
            var extension = FetchWord();
            var value = ReadLong(CalculateBriefIndexedAddress(baseRegister, extension, opcode));
            State.D[destination] = value;
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongBriefIndexedToData);
        }

        private void ExecuteMoveLongBriefIndexedToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var baseRegister = opcode & 7;
            var extension = FetchWord();
            var value = ReadLong(CalculateBriefIndexedAddress(baseRegister, extension, opcode));
            WriteGeneralRegister(true, destination, value);
            CompleteTiming(M68kInstructionTimingKey.MoveLongBriefIndexedToAddress);
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
            var value = ReadByte(CalculateBriefIndexedAddress(baseRegister, extension, opcode));
            WriteDataRegisterByte(destination, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteBriefIndexedToData);
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

        private void ExecuteMoveByteDataToBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var baseRegister = (opcode >> 9) & 7;
            var source = opcode & 7;
            var extension = FetchWord();
            var value = (byte)State.D[source];
            WriteByte(CalculateBriefIndexedAddress(baseRegister, extension, opcode), value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteDataToBriefIndexed);
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

        private void ExecuteImmediateLogicalByteToAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var immediate = (byte)FetchWord();
            var address = FetchLong();
            var destination = ReadByte(address);
            var result = opcode == 0x0039
                ? (byte)(destination | immediate)
                : (byte)(destination & immediate);
            WriteByte(address, result);
            State.SetNegativeZero(result, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.ImmediateLogicalByteToAbsoluteLong);
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

        private void ExecuteAddiByteImmediateToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var addressRegister = opcode & 7;
            var source = (byte)FetchWord();
            var address = State.A[addressRegister];
            var destination = ReadByte(address);
            var result = (byte)(destination + source);
            WriteByte(address, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.AddiByteImmediateToAddressIndirect);
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

        private void ExecuteAddWordDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var destination = State.D[destinationRegister] & 0xFFFF;
            var source = State.D[sourceRegister] & 0xFFFF;
            var result = (ushort)(destination + source);
            WriteDataRegisterWord(destinationRegister, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.AddWordDataToData);
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

        private void ExecuteAddaLongDataToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var addressRegister = (opcode >> 9) & 7;
            var dataRegister = opcode & 7;
            WriteGeneralRegister(true, addressRegister, State.A[addressRegister] + State.D[dataRegister]);
            CompleteTiming(M68kInstructionTimingKey.AddaLongDataToAddress);
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

        private void ExecuteSubaLongImmediateToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var source = FetchLong();
            WriteGeneralRegister(true, register, State.A[register] - source);
            CompleteTiming(M68kInstructionTimingKey.SubaLongImmediateToAddress);
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

        private void ExecuteMoveFromCcr(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var value = (ushort)(State.StatusRegister & 0x001F);
            WriteWordDestination((opcode >> 3) & 7, opcode & 7, value, opcode);
            CompleteTiming(M68kInstructionTimingKey.Nop);
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

        private void ExecuteExtWordData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var result = unchecked((ushort)(short)(sbyte)(State.D[register] & 0xFF));
            WriteDataRegisterWord(register, result);
            SetMoveFlags(result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.ExtWordData);
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

        private void ExecuteCmpaLongImmediateToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = FetchLong();
            var destination = State.A[(opcode >> 9) & 7];
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpaLongImmediateToAddress);
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

        private void ExecuteCmpLongDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.D[(opcode >> 9) & 7];
            var source = State.D[opcode & 7];
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpLongDataToData);
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

        private void ExecuteCmpByteDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.D[(opcode >> 9) & 7] & 0xFF;
            var source = State.D[opcode & 7] & 0xFF;
            SetCompareFlags(destination, source, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.CmpByteDataToData);
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

        private void ExecuteSccAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var condition = (opcode >> 8) & 0x0F;
            var address = FetchLong();
            WriteByte(address, CheckCondition(condition) ? (byte)0xFF : (byte)0x00);
            CompleteTiming(M68kInstructionTimingKey.SccAbsoluteLong);
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

        protected void BeginInstruction(ushort opcode)
        {
            State.LastInstructionProgramCounter = State.ProgramCounter;
            State.LastOpcode = opcode;
            _instructionFrequency.Record(State.LastInstructionProgramCounter, opcode);
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
            var value = _bus is IM68kCodeReader codeReader && _timing.ProbeInstructionCache(address)
                ? codeReader.ReadHostWord(address)
                : ReadWord(address, M68kBusAccessKind.CpuInstructionFetch);
            State.ProgramCounter += 2;
            return value;
        }

        protected uint FetchLong()
        {
            var high = FetchWord();
            var low = FetchWord();
            return ((uint)high << 16) | low;
        }

        protected byte ReadByte(uint address, M68kBusAccessKind accessKind = M68kBusAccessKind.CpuDataRead)
        {
            return _busBridge.ReadByte(address, accessKind);
        }

        protected ushort ReadWord(uint address, M68kBusAccessKind accessKind = M68kBusAccessKind.CpuDataRead)
        {
            return _busBridge.ReadWord(address, accessKind);
        }

        protected uint ReadLong(uint address)
        {
            return _busBridge.ReadLong(address, M68kBusAccessKind.CpuDataRead);
        }

        protected void WriteByte(uint address, byte value)
        {
            _busBridge.WriteByte(address, value, M68kBusAccessKind.CpuDataWrite);
        }

        protected void WriteWord(uint address, ushort value)
        {
            _busBridge.WriteWord(address, value, M68kBusAccessKind.CpuDataWrite);
        }

        protected void WriteLong(uint address, uint value)
        {
            _busBridge.WriteLong(address, value, M68kBusAccessKind.CpuDataWrite);
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
            _timing.CompleteInstruction(_timing.GetPlan(key));
        }

        private void CompleteOperandShapeTiming(M68kInstructionTimingKey key, string name)
        {
            var plan = M68kTimingFormula.CreateOperandShapePlan(
                key,
                name,
                _profile.Model,
                _profile.FixedInstructionNativeCycles);
            _timing.CompleteInstruction(plan);
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
            _timing.CompleteInstruction(plan);
        }

        private static int CountSetBits(ushort value)
            => M68kIntegerSemantics.CountSetBits(value);

        private void SetCompareFlags(uint destination, uint source, M68kOperandSize size)
        {
            var arithmetic = M68kIntegerSemantics.Subtract(destination, source, size);
            State.SetNegativeZero(arithmetic.Value, size);
            State.SetFlag(M68kCpuState.Overflow, arithmetic.Overflow);
            State.SetFlag(M68kCpuState.Carry, arithmetic.Carry);
        }

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

        private void SetSubtractFlags(uint destination, uint source, uint result, M68kOperandSize size)
        {
            var arithmetic = M68kIntegerSemantics.CalculateSubtractFlags(destination, source, result, size);
            State.SetNegativeZero(arithmetic.Value, size);
            State.SetFlag(M68kCpuState.Overflow, arithmetic.Overflow);
            State.SetFlag(M68kCpuState.Carry, arithmetic.Carry);
            State.SetFlag(M68kCpuState.Extend, arithmetic.Carry);
        }

        private void SetMoveFlags(uint value, M68kOperandSize size)
        {
            State.SetNegativeZero(value, size);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
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
            if ((address & 1) != 0 || _bus is not IM68kCodeReader codeReader)
            {
                return false;
            }

            opcode = codeReader.ReadHostWord(address);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CheckCondition(int condition)
            => M68kIntegerSemantics.EvaluateCondition(State.StatusRegister, condition);
    }
}
