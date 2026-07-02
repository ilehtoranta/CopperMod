/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace Copper68k
{
    internal enum M68kJitOperation
    {
        Nop,
        Moveq,
        Move,
        Movea,
        Lea,
        Tst,
        Clr,
        Neg,
        Not,
        Cmp,
        Cmpi,
        Cmpa,
        Cmpm,
        Add,
        Addi,
        Addq,
        Sub,
        Subi,
        Subq,
        Abcd,
        Sbcd,
        Nbcd,
        And,
        Andi,
        Or,
        Ori,
        Eor,
        Eori,
        Bra,
        Bcc,
        Bsr,
        Dbcc,
        Jmp,
        Jsr,
        Rts,
        Movem,
        ExtWord,
        ExtLong,
        Swap,
        Exg,
        Mulu,
        Muls,
        Divu,
        Divs,
        ShiftRegister,
        BitImmediate,
        BitDynamic,
        OriToCcr,
        OriToSr,
        AndiToCcr,
        AndiToSr,
        EoriToCcr,
        EoriToSr,
        MoveToCcr,
        MoveToSr,
        Pea,
        M68040Fpu,
        M68040Fallback
    }

    internal enum M68kJitEaKind
    {
        None,
        DataRegister,
        AddressRegister,
        AddressIndirect,
        AddressPostincrement,
        AddressPredecrement,
        AddressDisplacement,
        AddressIndex,
        AbsoluteWord,
        AbsoluteLong,
        PcDisplacement,
        PcIndex,
        Immediate
    }

    internal enum M68kJitBailoutReason
    {
        None,
        UnsupportedOpcode,
        UnsupportedEa,
        HostTrap,
        SystemInstruction,
        ExceptionInstruction,
        GenerationMismatch,
        InterruptOrTargetCycle,
        SelfModifiedCode
    }

    internal readonly struct M68kDecodedEa
    {
        public M68kDecodedEa(
            M68kJitEaKind kind,
            int register,
            uint extensionAddress,
            ushort extension0,
            ushort extension1,
            uint immediate)
        {
            Kind = kind;
            Register = register;
            ExtensionAddress = extensionAddress;
            Extension0 = extension0;
            Extension1 = extension1;
            Immediate = immediate;
        }

        public static M68kDecodedEa None { get; } = new M68kDecodedEa(M68kJitEaKind.None, 0, 0, 0, 0, 0);

        public M68kJitEaKind Kind { get; }

        public int Register { get; }

        public uint ExtensionAddress { get; }

        public ushort Extension0 { get; }

        public ushort Extension1 { get; }

        public uint Immediate { get; }

        public bool IsMemory =>
            Kind is M68kJitEaKind.AddressIndirect or
                M68kJitEaKind.AddressPostincrement or
                M68kJitEaKind.AddressPredecrement or
                M68kJitEaKind.AddressDisplacement or
                M68kJitEaKind.AddressIndex or
                M68kJitEaKind.AbsoluteWord or
                M68kJitEaKind.AbsoluteLong or
                M68kJitEaKind.PcDisplacement or
                M68kJitEaKind.PcIndex;

        public bool IsWritable =>
            Kind is M68kJitEaKind.DataRegister or
                M68kJitEaKind.AddressRegister or
                M68kJitEaKind.AddressIndirect or
                M68kJitEaKind.AddressPostincrement or
                M68kJitEaKind.AddressPredecrement or
                M68kJitEaKind.AddressDisplacement or
                M68kJitEaKind.AddressIndex or
                M68kJitEaKind.AbsoluteWord or
                M68kJitEaKind.AbsoluteLong;
    }

    internal readonly struct M68kDecodedInstruction
    {
        public M68kDecodedInstruction(
            uint programCounter,
            ushort opcode,
            M68kJitOperation operation,
            M68kOperandSize size,
            M68kDecodedEa source,
            M68kDecodedEa destination,
            int register,
            int quickValue,
            int condition,
            int displacement,
            int variant,
            ushort registerMask,
            uint branchBase,
            int extensionCount,
            ushort extension0,
            ushort extension1,
            ushort extension2,
            ushort extension3,
            ushort extension4,
            bool stopsTrace)
        {
            ProgramCounter = programCounter;
            Opcode = opcode;
            Operation = operation;
            Size = size;
            Source = source;
            Destination = destination;
            Register = register;
            QuickValue = quickValue;
            Condition = condition;
            Displacement = displacement;
            Variant = variant;
            RegisterMask = registerMask;
            BranchBase = branchBase;
            ExtensionCount = extensionCount;
            Extension0 = extension0;
            Extension1 = extension1;
            Extension2 = extension2;
            Extension3 = extension3;
            Extension4 = extension4;
            StopsTrace = stopsTrace;
        }

        public uint ProgramCounter { get; }

        public ushort Opcode { get; }

        public M68kJitOperation Operation { get; }

        public M68kOperandSize Size { get; }

        public M68kDecodedEa Source { get; }

        public M68kDecodedEa Destination { get; }

        public int Register { get; }

        public int QuickValue { get; }

        public int Condition { get; }

        public int Displacement { get; }

        public int Variant { get; }

        public ushort RegisterMask { get; }

        public uint BranchBase { get; }

        public int ExtensionCount { get; }

        public ushort Extension0 { get; }

        public ushort Extension1 { get; }

        public ushort Extension2 { get; }

        public ushort Extension3 { get; }

        public ushort Extension4 { get; }

        public int Length => 2 + (ExtensionCount * 2);

        public bool StopsTrace { get; }
    }

    internal interface IM68kCodeReader
    {
        bool HasHostTrapStub(uint address);

        ushort ReadHostWord(uint address);
    }

    internal sealed class M68kCodeReadException : Exception
    {
        public static M68kCodeReadException Instance { get; } = new M68kCodeReadException();

        private M68kCodeReadException()
            : base("The MC68000 JIT decoder attempted to read outside the captured code snapshot.")
        {
        }
    }

    internal static class M68kDecoder
    {
        [Flags]
        private enum EaAllowed
        {
            None = 0,
            DataRegister = 1,
            AddressRegister = 2,
            Memory = 4,
            PrePost = 8,
            PcMemory = 16,
            Immediate = 32
        }

        public static bool TryDecode(
            IM68kCodeReader codeReader,
            uint programCounter,
            out M68kDecodedInstruction instruction,
            out M68kJitBailoutReason reason,
            M68kJitCpuModel cpuModel = M68kJitCpuModel.M68000)
        {
            instruction = default;
            reason = M68kJitBailoutReason.None;
            try
            {
                programCounter = Normalize(programCounter);
                if ((programCounter & 1) != 0 || codeReader.HasHostTrapStub(programCounter))
                {
                    reason = M68kJitBailoutReason.HostTrap;
                    return false;
                }

                var opcode = codeReader.ReadHostWord(programCounter);
                if ((opcode & 0xF000) == 0xF000 && cpuModel == M68kJitCpuModel.M68040)
                {
                    return TryDecodeM68040LineF(programCounter, opcode, new DecodeCursor(codeReader, programCounter + 2), out instruction);
                }

                if ((opcode & 0xF000) is 0xA000 or 0xF000 || opcode is 0x4AFC)
                {
                    reason = M68kJitBailoutReason.ExceptionInstruction;
                    return false;
                }

                if (opcode is 0x4E70 or 0x4E72 or 0x4E73 or 0x4E76 or 0x4E77 or 0x4E7A or 0x4E7B ||
                    (opcode & 0xFFF0) == 0x4E40 ||
                    (opcode & 0xFFF0) == 0x4E60)
                {
                    reason = M68kJitBailoutReason.SystemInstruction;
                    return false;
                }

                var cursor = new DecodeCursor(codeReader, programCounter + 2);
                var decoded = (opcode >> 12) switch
                {
                    0x0 => TryDecodeLine0(programCounter, opcode, ref cursor, out instruction, out reason),
                    0x1 or 0x2 or 0x3 => TryDecodeMove(programCounter, opcode, ref cursor, out instruction, out reason),
                    0x4 => TryDecodeLine4(programCounter, opcode, ref cursor, out instruction, out reason),
                    0x5 => TryDecodeLine5(programCounter, opcode, ref cursor, out instruction, out reason),
                    0x6 => TryDecodeBranch(programCounter, opcode, ref cursor, out instruction, out reason),
                    0x7 => TryDecodeMoveq(programCounter, opcode, ref cursor, out instruction),
                    0x8 or 0x9 or 0xB or 0xC or 0xD => TryDecodeArithmetic(programCounter, opcode, ref cursor, out instruction, out reason),
                    0xE => TryDecodeShiftRotate(programCounter, opcode, ref cursor, out instruction, out reason),
                    _ => false
                };
                if (decoded)
                {
                    return true;
                }

                if (reason == M68kJitBailoutReason.None)
                {
                    reason = M68kJitBailoutReason.UnsupportedOpcode;
                }

                return false;
            }
            catch (M68kCodeReadException)
            {
                reason = M68kJitBailoutReason.UnsupportedOpcode;
                instruction = default;
                return false;
            }
        }

        private static bool TryDecodeM68040LineF(
            uint pc,
            ushort opcode,
            DecodeCursor cursor,
            out M68kDecodedInstruction instruction)
        {
            if ((opcode & 0xFFC0) == 0xF300 || (opcode & 0xFFC0) == 0xF340)
            {
                var stateCursor = cursor;
                var stateMode = (opcode >> 3) & 7;
                var stateRegister = opcode & 7;
                if (!TryDecodeFpuStateEa(ref stateCursor, stateMode, stateRegister, out var ea))
                {
                    instruction = CreateM68040Fallback(pc, opcode, cursor);
                    return true;
                }

                var save = (opcode & 0xFFC0) == 0xF300;
                instruction = Create(
                    pc,
                    opcode,
                    M68kJitOperation.M68040Fpu,
                    M68kOperandSize.Long,
                    save ? M68kDecodedEa.None : ea,
                    save ? ea : M68kDecodedEa.None,
                    0,
                    0,
                    0,
                    0,
                    save ? (int)M68040FpuJitKind.SaveState : (int)M68040FpuJitKind.RestoreState,
                    0,
                    pc + 2,
                    stateCursor,
                    stopsTrace: false);
                return true;
            }

            if ((opcode & 0xFFC0) != 0xF200)
            {
                instruction = CreateM68040Fallback(pc, opcode, cursor);
                return true;
            }

            var local = cursor;
            var extension = local.FetchWord();
            var mode = (opcode >> 3) & 7;
            var register = opcode & 7;
            if ((extension & 0xE000) == 0xA000)
            {
                if (mode != 0)
                {
                    instruction = CreateM68040Fallback(pc, opcode, cursor);
                    return true;
                }

                instruction = Create(
                    pc,
                    opcode,
                    M68kJitOperation.M68040Fpu,
                    M68kOperandSize.Long,
                    new M68kDecodedEa(M68kJitEaKind.DataRegister, register, 0, 0, 0, 0),
                    M68kDecodedEa.None,
                    register,
                    0,
                    0,
                    0,
                    (int)M68040FpuJitKind.MoveToControl,
                    (ushort)(extension & 0x1C00),
                    pc + 2,
                    local,
                    stopsTrace: false);
                return true;
            }

            if ((extension & 0xE000) == 0x8000)
            {
                if (mode != 0)
                {
                    instruction = CreateM68040Fallback(pc, opcode, cursor);
                    return true;
                }

                instruction = Create(
                    pc,
                    opcode,
                    M68kJitOperation.M68040Fpu,
                    M68kOperandSize.Long,
                    M68kDecodedEa.None,
                    new M68kDecodedEa(M68kJitEaKind.DataRegister, register, 0, 0, 0, 0),
                    register,
                    0,
                    0,
                    0,
                    (int)M68040FpuJitKind.MoveFromControl,
                    (ushort)(extension & 0x1C00),
                    pc + 2,
                    local,
                    stopsTrace: false);
                return true;
            }

            if ((extension & 0x6000) == 0x6000)
            {
                var format = (extension >> 10) & 7;
                if (!TryDecodeFpuEa(ref local, mode, register, format, write: true, out var destination))
                {
                    instruction = CreateM68040Fallback(pc, opcode, cursor);
                    return true;
                }

                instruction = Create(
                    pc,
                    opcode,
                    M68kJitOperation.M68040Fpu,
                    M68kOperandSize.Long,
                    M68kDecodedEa.None,
                    destination,
                    (extension >> 7) & 7,
                    format,
                    0,
                    0,
                    (int)M68040FpuJitKind.MoveToEa,
                    0,
                    pc + 2,
                    local,
                    stopsTrace: false);
                return true;
            }

            var sourceIsEa = (extension & 0x4000) != 0;
            var formatOrSource = (extension >> 10) & 7;
            var source = M68kDecodedEa.None;
            if (sourceIsEa &&
                !TryDecodeFpuEa(ref local, mode, register, formatOrSource, write: false, out source))
            {
                instruction = CreateM68040Fallback(pc, opcode, cursor);
                return true;
            }

            var opmode = extension & 0x007F;
            instruction = Create(
                pc,
                opcode,
                M68kJitOperation.M68040Fpu,
                M68kOperandSize.Long,
                source,
                M68kDecodedEa.None,
                (extension >> 7) & 7,
                formatOrSource,
                opmode,
                sourceIsEa ? 1 : 0,
                M68040FpuHelpers.IsSupportedOperation(opmode)
                    ? (int)M68040FpuJitKind.Operation
                    : (int)M68040FpuJitKind.LineFTrap,
                0,
                pc + 2,
                local,
                stopsTrace: false);
            return true;
        }

        private static M68kDecodedInstruction CreateM68040Fallback(uint pc, ushort opcode, DecodeCursor cursor)
        {
            return Create(
                pc,
                opcode,
                M68kJitOperation.M68040Fallback,
                M68kOperandSize.Word,
                new M68kDecodedEa(M68kJitEaKind.Immediate, 0, 0, 0, 0, pc),
                M68kDecodedEa.None,
                0,
                0,
                0,
                0,
                0,
                0,
                pc + 2,
                cursor,
                stopsTrace: true);
        }

        private static bool TryDecodeFpuEa(
            ref DecodeCursor cursor,
            int mode,
            int register,
            int format,
            bool write,
            out M68kDecodedEa ea)
        {
            ea = default;
            if (mode == 0)
            {
                if (!M68040FpuHelpers.IsSupportedDataRegisterFormat(format))
                {
                    return false;
                }

                ea = new M68kDecodedEa(M68kJitEaKind.DataRegister, register, 0, 0, 0, 0);
                return true;
            }

            if (!M68040FpuHelpers.IsSupportedMemoryFormat(format))
            {
                return false;
            }

            if (mode == 7 && register == 4)
            {
                if (write)
                {
                    return false;
                }

                return TryDecodeFpuImmediate(ref cursor, format, out ea);
            }

            return mode switch
            {
                2 => DecodeSimpleFpuEa(M68kJitEaKind.AddressIndirect, register, out ea),
                3 => DecodeSimpleFpuEa(M68kJitEaKind.AddressPostincrement, register, out ea),
                4 => DecodeSimpleFpuEa(M68kJitEaKind.AddressPredecrement, register, out ea),
                5 => DecodeExtensionFpuEa(ref cursor, M68kJitEaKind.AddressDisplacement, register, out ea),
                7 when register == 1 => DecodeAbsoluteLongFpuEa(ref cursor, out ea),
                _ => false
            };
        }

        private static bool TryDecodeFpuStateEa(
            ref DecodeCursor cursor,
            int mode,
            int register,
            out M68kDecodedEa ea)
        {
            ea = default;
            return mode switch
            {
                2 => DecodeSimpleFpuEa(M68kJitEaKind.AddressIndirect, register, out ea),
                3 => DecodeSimpleFpuEa(M68kJitEaKind.AddressPostincrement, register, out ea),
                4 => DecodeSimpleFpuEa(M68kJitEaKind.AddressPredecrement, register, out ea),
                5 => DecodeExtensionFpuEa(ref cursor, M68kJitEaKind.AddressDisplacement, register, out ea),
                7 when register == 1 => DecodeAbsoluteLongFpuEa(ref cursor, out ea),
                _ => false
            };
        }

        private static bool DecodeSimpleFpuEa(M68kJitEaKind kind, int register, out M68kDecodedEa ea)
        {
            ea = new M68kDecodedEa(kind, register, 0, 0, 0, 0);
            return true;
        }

        private static bool DecodeExtensionFpuEa(
            ref DecodeCursor cursor,
            M68kJitEaKind kind,
            int register,
            out M68kDecodedEa ea)
        {
            var extensionAddress = cursor.Address;
            var extension = cursor.FetchWord();
            ea = new M68kDecodedEa(kind, register, extensionAddress, extension, 0, 0);
            return true;
        }

        private static bool DecodeAbsoluteLongFpuEa(ref DecodeCursor cursor, out M68kDecodedEa ea)
        {
            var extensionAddress = cursor.Address;
            var high = cursor.FetchWord();
            var low = cursor.FetchWord();
            ea = new M68kDecodedEa(M68kJitEaKind.AbsoluteLong, 0, extensionAddress, high, low, 0);
            return true;
        }

        private static bool TryDecodeFpuImmediate(ref DecodeCursor cursor, int format, out M68kDecodedEa ea)
        {
            var extensionAddress = cursor.Address;
            switch (format)
            {
                case 0:
                case 1:
                {
                    var high = cursor.FetchWord();
                    var low = cursor.FetchWord();
                    ea = new M68kDecodedEa(
                        M68kJitEaKind.Immediate,
                        0,
                        extensionAddress,
                        high,
                        low,
                        ((uint)high << 16) | low);
                    return true;
                }
                case 4:
                {
                    var value = cursor.FetchWord();
                    ea = new M68kDecodedEa(M68kJitEaKind.Immediate, 0, extensionAddress, value, 0, value);
                    return true;
                }
                case 5:
                {
                    var word0 = cursor.FetchWord();
                    var word1 = cursor.FetchWord();
                    var word2 = cursor.FetchWord();
                    var word3 = cursor.FetchWord();
                    ea = new M68kDecodedEa(
                        M68kJitEaKind.Immediate,
                        0,
                        extensionAddress,
                        word0,
                        word1,
                        ((uint)word2 << 16) | word3);
                    return true;
                }
                case 6:
                {
                    var value = cursor.FetchWord();
                    ea = new M68kDecodedEa(M68kJitEaKind.Immediate, 0, extensionAddress, value, 0, value & 0xFFu);
                    return true;
                }
                default:
                    ea = default;
                    return false;
            }
        }

        private static bool TryDecodeMove(
            uint pc,
            ushort opcode,
            ref DecodeCursor cursor,
            out M68kDecodedInstruction instruction,
            out M68kJitBailoutReason reason)
        {
            instruction = default;
            reason = M68kJitBailoutReason.None;
            var line = opcode & 0xF000;
            if (line is not (0x1000 or 0x2000 or 0x3000))
            {
                return false;
            }

            var size = ((opcode >> 12) & 0x03) switch
            {
                1 => M68kOperandSize.Byte,
                2 => M68kOperandSize.Long,
                3 => M68kOperandSize.Word,
                _ => (M68kOperandSize)0
            };
            if (size == 0)
            {
                return false;
            }

            var sourceAllowed = EaAllowed.DataRegister | EaAllowed.Memory | EaAllowed.PrePost | EaAllowed.PcMemory | EaAllowed.Immediate;
            if (size != M68kOperandSize.Byte)
            {
                sourceAllowed |= EaAllowed.AddressRegister;
            }

            var local = cursor;
            if (!TryDecodeEa(ref local, (opcode >> 3) & 7, opcode & 7, size, sourceAllowed, out var source))
            {
                reason = M68kJitBailoutReason.UnsupportedEa;
                return false;
            }

            var destAllowed = EaAllowed.DataRegister | EaAllowed.Memory | EaAllowed.PrePost;
            if (size != M68kOperandSize.Byte)
            {
                destAllowed |= EaAllowed.AddressRegister;
            }

            if (!TryDecodeEa(ref local, (opcode >> 6) & 7, (opcode >> 9) & 7, size, destAllowed, out var destination))
            {
                reason = M68kJitBailoutReason.UnsupportedEa;
                return false;
            }

            cursor = local;
            var operation = destination.Kind == M68kJitEaKind.AddressRegister ? M68kJitOperation.Movea : M68kJitOperation.Move;
            instruction = Create(
                pc,
                opcode,
                operation,
                size,
                source,
                destination,
                destination.Register,
                0,
                0,
                0,
                0,
                0,
                pc + 2,
                cursor,
                stopsTrace: false);
            return true;
        }

        private static bool TryDecodeMoveq(
            uint pc,
            ushort opcode,
            ref DecodeCursor cursor,
            out M68kDecodedInstruction instruction)
        {
            instruction = default;
            if ((opcode & 0xF100) != 0x7000)
            {
                return false;
            }

            instruction = Create(
                pc,
                opcode,
                M68kJitOperation.Moveq,
                M68kOperandSize.Long,
                M68kDecodedEa.None,
                M68kDecodedEa.None,
                (opcode >> 9) & 7,
                unchecked((sbyte)(opcode & 0xFF)),
                0,
                0,
                0,
                0,
                pc + 2,
                cursor,
                stopsTrace: false);
            return true;
        }

        private static bool TryDecodeBranch(
            uint pc,
            ushort opcode,
            ref DecodeCursor cursor,
            out M68kDecodedInstruction instruction,
            out M68kJitBailoutReason reason)
        {
            instruction = default;
            reason = M68kJitBailoutReason.None;
            if ((opcode & 0xF000) != 0x6000)
            {
                return false;
            }

            var condition = (opcode >> 8) & 0x0F;
            var displacement = opcode & 0xFF;
            var branchBase = pc + 2;
            int offset;
            var local = cursor;
            if (displacement == 0)
            {
                offset = unchecked((short)local.FetchWord());
            }
            else
            {
                offset = unchecked((sbyte)displacement);
            }

            cursor = local;
            var operation = condition switch
            {
                0 => M68kJitOperation.Bra,
                1 => M68kJitOperation.Bsr,
                _ => M68kJitOperation.Bcc
            };
            instruction = Create(
                pc,
                opcode,
                operation,
                M68kOperandSize.Word,
                M68kDecodedEa.None,
                M68kDecodedEa.None,
                0,
                0,
                condition,
                offset,
                0,
                0,
                branchBase,
                cursor,
                stopsTrace: true);
            return true;
        }

        private static bool TryDecodeLine0(
            uint pc,
            ushort opcode,
            ref DecodeCursor cursor,
            out M68kDecodedInstruction instruction,
            out M68kJitBailoutReason reason)
        {
            instruction = default;
            reason = M68kJitBailoutReason.None;
            if (TryDecodeImmediateToStatus(pc, opcode, ref cursor, out instruction))
            {
                return true;
            }

            if ((opcode & 0xFF00) is 0x0800 or 0x0840 or 0x0880 or 0x08C0)
            {
                var local = cursor;
                var bit = local.FetchWord() & 31;
                var operation = (opcode >> 6) & 3;
                var allowed = operation == 0
                    ? EaAllowed.DataRegister | EaAllowed.Memory | EaAllowed.PrePost | EaAllowed.PcMemory
                    : EaAllowed.DataRegister | EaAllowed.Memory | EaAllowed.PrePost;
                var bitSize = ((opcode >> 3) & 7) == 0 ? M68kOperandSize.Long : M68kOperandSize.Byte;
                if (!TryDecodeEa(ref local, (opcode >> 3) & 7, opcode & 7, bitSize, allowed, out var ea))
                {
                    reason = M68kJitBailoutReason.UnsupportedEa;
                    return false;
                }

                cursor = local;
                instruction = Create(
                    pc,
                    opcode,
                    M68kJitOperation.BitImmediate,
                    bitSize,
                    ea,
                    ea,
                    0,
                    bit,
                    0,
                    0,
                    operation,
                    0,
                    pc + 2,
                    cursor,
                    stopsTrace: false);
                return true;
            }

            if ((opcode & 0xF100) == 0x0100)
            {
                var operation = (opcode >> 6) & 3;
                var allowed = operation == 0
                    ? EaAllowed.DataRegister | EaAllowed.Memory | EaAllowed.PrePost | EaAllowed.PcMemory
                    : EaAllowed.DataRegister | EaAllowed.Memory | EaAllowed.PrePost;
                var bitSize = ((opcode >> 3) & 7) == 0 ? M68kOperandSize.Long : M68kOperandSize.Byte;
                var local = cursor;
                if (!TryDecodeEa(ref local, (opcode >> 3) & 7, opcode & 7, bitSize, allowed, out var ea))
                {
                    reason = M68kJitBailoutReason.UnsupportedEa;
                    return false;
                }

                cursor = local;
                instruction = Create(
                    pc,
                    opcode,
                    M68kJitOperation.BitDynamic,
                    bitSize,
                    ea,
                    ea,
                    (opcode >> 9) & 7,
                    0,
                    0,
                    0,
                    operation,
                    0,
                    pc + 2,
                    cursor,
                    stopsTrace: false);
                return true;
            }

            var high = opcode & 0xFF00;
            var operationKind = high switch
            {
                0x0000 => M68kJitOperation.Ori,
                0x0200 => M68kJitOperation.Andi,
                0x0400 => M68kJitOperation.Subi,
                0x0600 => M68kJitOperation.Addi,
                0x0A00 => M68kJitOperation.Eori,
                0x0C00 => M68kJitOperation.Cmpi,
                _ => (M68kJitOperation)(-1)
            };
            if ((int)operationKind < 0)
            {
                return false;
            }

            var size = DecodeImmediateSize(opcode);
            if (size == 0)
            {
                reason = M68kJitBailoutReason.UnsupportedOpcode;
                return false;
            }

            var localCursor = cursor;
            var immediate = FetchImmediate(ref localCursor, size);
            var immediateEa = new M68kDecodedEa(M68kJitEaKind.Immediate, 0, 0, 0, 0, immediate);
            if (!TryDecodeEa(
                    ref localCursor,
                    (opcode >> 3) & 7,
                    opcode & 7,
                    size,
                    EaAllowed.DataRegister | EaAllowed.Memory | EaAllowed.PrePost,
                    out var destination))
            {
                reason = M68kJitBailoutReason.UnsupportedEa;
                return false;
            }

            cursor = localCursor;
            instruction = Create(
                pc,
                opcode,
                operationKind,
                size,
                immediateEa,
                destination,
                0,
                0,
                0,
                0,
                0,
                0,
                pc + 2,
                cursor,
                stopsTrace: false);
            return true;
        }

        private static bool TryDecodeImmediateToStatus(
            uint pc,
            ushort opcode,
            ref DecodeCursor cursor,
            out M68kDecodedInstruction instruction)
        {
            instruction = default;
            var operation = opcode switch
            {
                0x003C => M68kJitOperation.OriToCcr,
                0x007C => M68kJitOperation.OriToSr,
                0x023C => M68kJitOperation.AndiToCcr,
                0x027C => M68kJitOperation.AndiToSr,
                0x0A3C => M68kJitOperation.EoriToCcr,
                0x0A7C => M68kJitOperation.EoriToSr,
                _ => (M68kJitOperation)(-1)
            };
            if ((int)operation < 0)
            {
                return false;
            }

            var local = cursor;
            var immediate = local.FetchWord();
            cursor = local;
            instruction = Create(
                pc,
                opcode,
                operation,
                M68kOperandSize.Word,
                new M68kDecodedEa(M68kJitEaKind.Immediate, 0, 0, 0, 0, immediate),
                M68kDecodedEa.None,
                0,
                0,
                0,
                0,
                0,
                0,
                pc + 2,
                cursor,
                stopsTrace: operation is M68kJitOperation.OriToSr or M68kJitOperation.AndiToSr or M68kJitOperation.EoriToSr);
            return true;
        }

        private static bool TryDecodeLine4(
            uint pc,
            ushort opcode,
            ref DecodeCursor cursor,
            out M68kDecodedInstruction instruction,
            out M68kJitBailoutReason reason)
        {
            instruction = default;
            reason = M68kJitBailoutReason.None;
            if (opcode == 0x4E71)
            {
                instruction = Create(pc, opcode, M68kJitOperation.Nop, M68kOperandSize.Word, M68kDecodedEa.None, M68kDecodedEa.None, 0, 0, 0, 0, 0, 0, pc + 2, cursor, false);
                return true;
            }

            if (opcode == 0x4E75)
            {
                instruction = Create(pc, opcode, M68kJitOperation.Rts, M68kOperandSize.Long, M68kDecodedEa.None, M68kDecodedEa.None, 0, 0, 0, 0, 0, 0, pc + 2, cursor, true);
                return true;
            }

            if (opcode is 0x44FC or 0x46FC)
            {
                var local = cursor;
                var immediate = local.FetchWord();
                cursor = local;
                var operation = opcode == 0x44FC ? M68kJitOperation.MoveToCcr : M68kJitOperation.MoveToSr;
                instruction = Create(
                    pc,
                    opcode,
                    operation,
                    M68kOperandSize.Word,
                    new M68kDecodedEa(M68kJitEaKind.Immediate, 0, 0, 0, 0, immediate),
                    M68kDecodedEa.None,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    pc + 2,
                    cursor,
                    stopsTrace: operation == M68kJitOperation.MoveToSr);
                return true;
            }

            if ((opcode & 0xFB80) == 0x4880 && ((opcode >> 3) & 7) != 0)
            {
                var local = cursor;
                var mask = local.FetchWord();
                if (!TryDecodeEa(
                        ref local,
                        (opcode >> 3) & 7,
                        opcode & 7,
                        (opcode & 0x0040) == 0 ? M68kOperandSize.Word : M68kOperandSize.Long,
                        EaAllowed.Memory | EaAllowed.PrePost,
                        out var ea))
                {
                    reason = M68kJitBailoutReason.UnsupportedEa;
                    return false;
                }

                cursor = local;
                instruction = Create(
                    pc,
                    opcode,
                    M68kJitOperation.Movem,
                    (opcode & 0x0040) == 0 ? M68kOperandSize.Word : M68kOperandSize.Long,
                    ea,
                    ea,
                    0,
                    0,
                    0,
                    0,
                    (opcode & 0x0400) != 0 ? 1 : 0,
                    mask,
                    pc + 2,
                    cursor,
                    stopsTrace: false);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x4840)
            {
                instruction = Create(pc, opcode, M68kJitOperation.Swap, M68kOperandSize.Long, M68kDecodedEa.None, M68kDecodedEa.None, opcode & 7, 0, 0, 0, 0, 0, pc + 2, cursor, false);
                return true;
            }

            if ((opcode & 0xFFC0) == 0x4840)
            {
                var local = cursor;
                if (!TryDecodeEa(
                        ref local,
                        (opcode >> 3) & 7,
                        opcode & 7,
                        M68kOperandSize.Long,
                        EaAllowed.Memory | EaAllowed.PcMemory,
                        out var ea))
                {
                    reason = M68kJitBailoutReason.UnsupportedEa;
                    return false;
                }

                cursor = local;
                instruction = Create(pc, opcode, M68kJitOperation.Pea, M68kOperandSize.Long, ea, M68kDecodedEa.None, 0, 0, 0, 0, 0, 0, pc + 2, cursor, false);
                return true;
            }

            if ((opcode & 0xFFC0) == 0x4880)
            {
                instruction = Create(pc, opcode, M68kJitOperation.ExtWord, M68kOperandSize.Word, M68kDecodedEa.None, M68kDecodedEa.None, opcode & 7, 0, 0, 0, 0, 0, pc + 2, cursor, false);
                return true;
            }

            if ((opcode & 0xFFC0) == 0x48C0)
            {
                instruction = Create(pc, opcode, M68kJitOperation.ExtLong, M68kOperandSize.Long, M68kDecodedEa.None, M68kDecodedEa.None, opcode & 7, 0, 0, 0, 0, 0, pc + 2, cursor, false);
                return true;
            }

            if ((opcode & 0xF1C0) == 0x41C0)
            {
                var local = cursor;
                if (!TryDecodeEa(
                        ref local,
                        (opcode >> 3) & 7,
                        opcode & 7,
                        M68kOperandSize.Long,
                        EaAllowed.Memory | EaAllowed.PcMemory,
                        out var ea))
                {
                    reason = M68kJitBailoutReason.UnsupportedEa;
                    return false;
                }

                cursor = local;
                instruction = Create(pc, opcode, M68kJitOperation.Lea, M68kOperandSize.Long, ea, M68kDecodedEa.None, (opcode >> 9) & 7, 0, 0, 0, 0, 0, pc + 2, cursor, false);
                return true;
            }

            if ((opcode & 0xFFC0) is 0x4E80 or 0x4EC0)
            {
                var local = cursor;
                if (!TryDecodeEa(
                        ref local,
                        (opcode >> 3) & 7,
                        opcode & 7,
                        M68kOperandSize.Long,
                        EaAllowed.Memory | EaAllowed.PcMemory,
                        out var ea))
                {
                    reason = M68kJitBailoutReason.UnsupportedEa;
                    return false;
                }

                cursor = local;
                instruction = Create(
                    pc,
                    opcode,
                    (opcode & 0xFFC0) == 0x4E80 ? M68kJitOperation.Jsr : M68kJitOperation.Jmp,
                    M68kOperandSize.Long,
                    ea,
                    M68kDecodedEa.None,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    pc + 2,
                    cursor,
                    stopsTrace: true);
                return true;
            }

            if ((opcode & 0xFFC0) == 0x4800)
            {
                var local = cursor;
                if (!TryDecodeEa(
                        ref local,
                        (opcode >> 3) & 7,
                        opcode & 7,
                        M68kOperandSize.Byte,
                        EaAllowed.DataRegister | EaAllowed.Memory | EaAllowed.PrePost,
                        out var ea))
                {
                    reason = M68kJitBailoutReason.UnsupportedEa;
                    return false;
                }

                cursor = local;
                instruction = Create(
                    pc,
                    opcode,
                    M68kJitOperation.Nbcd,
                    M68kOperandSize.Byte,
                    ea,
                    ea,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    pc + 2,
                    cursor,
                    stopsTrace: false);
                return true;
            }

            var unary = opcode & 0xFF00;
            if (unary is 0x4200 or 0x4400 or 0x4600 or 0x4A00)
            {
                var size = DecodeImmediateSize(opcode);
                if (size == 0)
                {
                    reason = M68kJitBailoutReason.UnsupportedOpcode;
                    return false;
                }

                var local = cursor;
                var allowed = unary == 0x4A00
                    ? EaAllowed.DataRegister | EaAllowed.Memory | EaAllowed.PrePost
                    : EaAllowed.DataRegister | EaAllowed.Memory | EaAllowed.PrePost;
                if (!TryDecodeEa(ref local, (opcode >> 3) & 7, opcode & 7, size, allowed, out var ea))
                {
                    reason = M68kJitBailoutReason.UnsupportedEa;
                    return false;
                }

                cursor = local;
                var operation = unary switch
                {
                    0x4200 => M68kJitOperation.Clr,
                    0x4400 => M68kJitOperation.Neg,
                    0x4600 => M68kJitOperation.Not,
                    _ => M68kJitOperation.Tst
                };
                instruction = Create(
                    pc,
                    opcode,
                    operation,
                    size,
                    ea,
                    ea,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    pc + 2,
                    cursor,
                    stopsTrace: false);
                return true;
            }

            return false;
        }

        private static bool TryDecodeLine5(
            uint pc,
            ushort opcode,
            ref DecodeCursor cursor,
            out M68kDecodedInstruction instruction,
            out M68kJitBailoutReason reason)
        {
            instruction = default;
            reason = M68kJitBailoutReason.None;
            if ((opcode & 0xF0F8) == 0x50C8)
            {
                var local = cursor;
                var displacement = unchecked((short)local.FetchWord());
                cursor = local;
                instruction = Create(
                    pc,
                    opcode,
                    M68kJitOperation.Dbcc,
                    M68kOperandSize.Word,
                    M68kDecodedEa.None,
                    M68kDecodedEa.None,
                    opcode & 7,
                    0,
                    (opcode >> 8) & 0x0F,
                    displacement,
                    0,
                    0,
                    pc + 2,
                    cursor,
                    stopsTrace: true);
                return true;
            }

            if ((opcode & 0xF0C0) == 0x50C0)
            {
                reason = M68kJitBailoutReason.UnsupportedOpcode;
                return false;
            }

            if ((opcode & 0xF000) != 0x5000)
            {
                return false;
            }

            var size = ((opcode >> 6) & 3) switch
            {
                0 => M68kOperandSize.Byte,
                1 => M68kOperandSize.Word,
                2 => M68kOperandSize.Long,
                _ => (M68kOperandSize)0
            };
            if (size == 0)
            {
                reason = M68kJitBailoutReason.UnsupportedOpcode;
                return false;
            }

            var mode = (opcode >> 3) & 7;
            var reg = opcode & 7;
            var allowed = EaAllowed.DataRegister | EaAllowed.Memory | EaAllowed.PrePost;
            if (mode == 1)
            {
                if (size == M68kOperandSize.Byte)
                {
                    reason = M68kJitBailoutReason.UnsupportedEa;
                    return false;
                }

                allowed |= EaAllowed.AddressRegister;
            }

            var localCursor = cursor;
            if (!TryDecodeEa(ref localCursor, mode, reg, size, allowed, out var destination))
            {
                reason = M68kJitBailoutReason.UnsupportedEa;
                return false;
            }

            cursor = localCursor;
            var value = (opcode >> 9) & 7;
            if (value == 0)
            {
                value = 8;
            }

            instruction = Create(
                pc,
                opcode,
                (opcode & 0x0100) == 0 ? M68kJitOperation.Addq : M68kJitOperation.Subq,
                size,
                M68kDecodedEa.None,
                destination,
                0,
                value,
                0,
                0,
                0,
                0,
                pc + 2,
                cursor,
                stopsTrace: false);
            return true;
        }

        private static bool TryDecodeArithmetic(
            uint pc,
            ushort opcode,
            ref DecodeCursor cursor,
            out M68kDecodedInstruction instruction,
            out M68kJitBailoutReason reason)
        {
            instruction = default;
            reason = M68kJitBailoutReason.None;
            var line = (opcode >> 12) & 0x0F;
            if (line is not (0x8 or 0x9 or 0xB or 0xC or 0xD))
            {
                return false;
            }

            if (line == 0xC && TryDecodeExchange(pc, opcode, cursor, out instruction))
            {
                return true;
            }

            var reg = (opcode >> 9) & 7;
            var opmode = (opcode >> 6) & 7;
            var mode = (opcode >> 3) & 7;
            var eaReg = opcode & 7;
            if ((opcode & 0xF1F0) is 0x8100 or 0xC100)
            {
                var memoryMode = (opcode & 0x0008) != 0;
                instruction = Create(
                    pc,
                    opcode,
                    (opcode & 0xF000) == 0x8000 ? M68kJitOperation.Sbcd : M68kJitOperation.Abcd,
                    M68kOperandSize.Byte,
                    new M68kDecodedEa(memoryMode ? M68kJitEaKind.AddressPredecrement : M68kJitEaKind.DataRegister, eaReg, 0, 0, 0, 0),
                    new M68kDecodedEa(memoryMode ? M68kJitEaKind.AddressPredecrement : M68kJitEaKind.DataRegister, reg, 0, 0, 0, 0),
                    reg,
                    0,
                    0,
                    0,
                    memoryMode ? 1 : 0,
                    0,
                    pc + 2,
                    cursor,
                    stopsTrace: false);
                return true;
            }

            if ((line == 0x9 || line == 0xD) && opmode is 4 or 5 or 6 && mode is 0 or 1)
            {
                reason = M68kJitBailoutReason.UnsupportedOpcode;
                return false;
            }

            if (line == 0xB && (opcode & 0xF138) == 0xB108)
            {
                var cmpmSize = ((opcode >> 6) & 3) switch
                {
                    0 => M68kOperandSize.Byte,
                    1 => M68kOperandSize.Word,
                    2 => M68kOperandSize.Long,
                    _ => (M68kOperandSize)0
                };
                if (cmpmSize != 0)
                {
                    instruction = Create(
                        pc,
                        opcode,
                        M68kJitOperation.Cmpm,
                        cmpmSize,
                        new M68kDecodedEa(M68kJitEaKind.AddressPostincrement, eaReg, 0, 0, 0, 0),
                        new M68kDecodedEa(M68kJitEaKind.AddressPostincrement, reg, 0, 0, 0, 0),
                        reg,
                        0,
                        0,
                        0,
                        0,
                        0,
                        pc + 2,
                        cursor,
                        stopsTrace: false);
                    return true;
                }
            }

            if ((line == 0xC && opmode is 3 or 7) ||
                (line == 0x8 && opmode is 3 or 7))
            {
                var localCursor = cursor;
                if (!TryDecodeEa(
                        ref localCursor,
                        mode,
                        eaReg,
                        M68kOperandSize.Word,
                        EaAllowed.DataRegister | EaAllowed.Memory | EaAllowed.PrePost | EaAllowed.PcMemory | EaAllowed.Immediate,
                        out var source))
                {
                    reason = M68kJitBailoutReason.UnsupportedEa;
                    return false;
                }

                cursor = localCursor;
                var operation = line == 0xC
                    ? opmode == 3 ? M68kJitOperation.Mulu : M68kJitOperation.Muls
                    : opmode == 3 ? M68kJitOperation.Divu : M68kJitOperation.Divs;
                instruction = Create(
                    pc,
                    opcode,
                    operation,
                    M68kOperandSize.Word,
                    source,
                    M68kDecodedEa.None,
                    reg,
                    0,
                    0,
                    0,
                    0,
                    0,
                    pc + 2,
                    cursor,
                    stopsTrace: false);
                return true;
            }

            if ((line == 0x9 || line == 0xD || line == 0xB) && opmode is 3 or 7)
            {
                var addressSize = opmode == 3 ? M68kOperandSize.Word : M68kOperandSize.Long;
                var localCursor = cursor;
                if (!TryDecodeEa(
                        ref localCursor,
                        mode,
                        eaReg,
                        addressSize,
                        EaAllowed.DataRegister | EaAllowed.AddressRegister | EaAllowed.Memory | EaAllowed.PrePost | EaAllowed.PcMemory | EaAllowed.Immediate,
                        out var source))
                {
                    reason = M68kJitBailoutReason.UnsupportedEa;
                    return false;
                }

                cursor = localCursor;
                var operation = line switch
                {
                    0x9 => M68kJitOperation.Sub,
                    0xB => M68kJitOperation.Cmpa,
                    _ => M68kJitOperation.Add
                };
                instruction = Create(
                    pc,
                    opcode,
                    operation == M68kJitOperation.Add ? M68kJitOperation.Add : operation == M68kJitOperation.Sub ? M68kJitOperation.Sub : M68kJitOperation.Cmpa,
                    addressSize,
                    source,
                    M68kDecodedEa.None,
                    reg,
                    0,
                    0,
                    0,
                    2,
                    0,
                    pc + 2,
                    cursor,
                    stopsTrace: false);
                return true;
            }

            var operandSize = opmode switch
            {
                0 or 4 => M68kOperandSize.Byte,
                1 or 5 => M68kOperandSize.Word,
                2 or 6 => M68kOperandSize.Long,
                _ => (M68kOperandSize)0
            };
            if (operandSize == 0)
            {
                reason = M68kJitBailoutReason.UnsupportedOpcode;
                return false;
            }

            var registerToEa = opmode >= 4 || line == 0xB;
            var allowed = registerToEa && line != 0xB
                ? EaAllowed.DataRegister | EaAllowed.Memory | EaAllowed.PrePost
                : EaAllowed.DataRegister | EaAllowed.Memory | EaAllowed.PrePost | EaAllowed.PcMemory | EaAllowed.Immediate;
            if ((!registerToEa || line == 0xB) && operandSize != M68kOperandSize.Byte)
            {
                allowed |= EaAllowed.AddressRegister;
            }

            var local = cursor;
            if (!TryDecodeEa(ref local, mode, eaReg, operandSize, allowed, out var ea))
            {
                reason = M68kJitBailoutReason.UnsupportedEa;
                return false;
            }

            cursor = local;
            var jitOperation = line switch
            {
                0x8 => M68kJitOperation.Or,
                0x9 => M68kJitOperation.Sub,
                0xB => registerToEa && opmode >= 4 ? M68kJitOperation.Eor : M68kJitOperation.Cmp,
                0xC => M68kJitOperation.And,
                _ => M68kJitOperation.Add
            };
            instruction = Create(
                pc,
                opcode,
                jitOperation,
                operandSize,
                ea,
                ea,
                reg,
                0,
                0,
                0,
                registerToEa ? 1 : 0,
                0,
                pc + 2,
                cursor,
                stopsTrace: false);
            return true;
        }

        private static bool TryDecodeExchange(
            uint pc,
            ushort opcode,
            DecodeCursor cursor,
            out M68kDecodedInstruction instruction)
        {
            instruction = default;
            var left = (opcode >> 9) & 7;
            var right = opcode & 7;
            int variant;
            if ((opcode & 0xF1F8) == 0xC140)
            {
                variant = 0;
            }
            else if ((opcode & 0xF1F8) == 0xC148)
            {
                variant = 1;
            }
            else if ((opcode & 0xF1F8) == 0xC188)
            {
                variant = 2;
            }
            else
            {
                return false;
            }

            instruction = Create(pc, opcode, M68kJitOperation.Exg, M68kOperandSize.Long, M68kDecodedEa.None, M68kDecodedEa.None, left, right, 0, 0, variant, 0, pc + 2, cursor, false);
            return true;
        }

        private static bool TryDecodeShiftRotate(
            uint pc,
            ushort opcode,
            ref DecodeCursor cursor,
            out M68kDecodedInstruction instruction,
            out M68kJitBailoutReason reason)
        {
            instruction = default;
            reason = M68kJitBailoutReason.None;
            if ((opcode & 0xF000) != 0xE000)
            {
                return false;
            }

            if ((opcode & 0x00C0) == 0x00C0)
            {
                reason = M68kJitBailoutReason.UnsupportedOpcode;
                return false;
            }

            var size = ((opcode >> 6) & 3) switch
            {
                0 => M68kOperandSize.Byte,
                1 => M68kOperandSize.Word,
                2 => M68kOperandSize.Long,
                _ => (M68kOperandSize)0
            };
            if (size == 0)
            {
                reason = M68kJitBailoutReason.UnsupportedOpcode;
                return false;
            }

            var count = (opcode >> 9) & 7;
            var countIsRegister = (opcode & 0x0020) != 0;
            if (!countIsRegister && count == 0)
            {
                count = 8;
            }

            var type = (opcode >> 3) & 3;
            var left = (opcode & 0x0100) != 0;
            var variant = type | (left ? 4 : 0) | (countIsRegister ? 8 : 0);
            instruction = Create(
                pc,
                opcode,
                M68kJitOperation.ShiftRegister,
                size,
                M68kDecodedEa.None,
                M68kDecodedEa.None,
                opcode & 7,
                count,
                0,
                0,
                variant,
                0,
                pc + 2,
                cursor,
                stopsTrace: false);
            return true;
        }

        private static bool TryDecodeEa(
            ref DecodeCursor cursor,
            int mode,
            int reg,
            M68kOperandSize size,
            EaAllowed allowed,
            out M68kDecodedEa ea)
        {
            ea = default;
            switch (mode)
            {
                case 0 when allowed.HasFlag(EaAllowed.DataRegister):
                    ea = new M68kDecodedEa(M68kJitEaKind.DataRegister, reg, 0, 0, 0, 0);
                    return true;
                case 1 when allowed.HasFlag(EaAllowed.AddressRegister):
                    ea = new M68kDecodedEa(M68kJitEaKind.AddressRegister, reg, 0, 0, 0, 0);
                    return true;
                case 2 when allowed.HasFlag(EaAllowed.Memory):
                    ea = new M68kDecodedEa(M68kJitEaKind.AddressIndirect, reg, 0, 0, 0, 0);
                    return true;
                case 3 when allowed.HasFlag(EaAllowed.Memory) && allowed.HasFlag(EaAllowed.PrePost):
                    ea = new M68kDecodedEa(M68kJitEaKind.AddressPostincrement, reg, 0, 0, 0, 0);
                    return true;
                case 4 when allowed.HasFlag(EaAllowed.Memory) && allowed.HasFlag(EaAllowed.PrePost):
                    ea = new M68kDecodedEa(M68kJitEaKind.AddressPredecrement, reg, 0, 0, 0, 0);
                    return true;
                case 5 when allowed.HasFlag(EaAllowed.Memory):
                {
                    var extensionAddress = cursor.Address;
                    var extension = cursor.FetchWord();
                    ea = new M68kDecodedEa(M68kJitEaKind.AddressDisplacement, reg, extensionAddress, extension, 0, 0);
                    return true;
                }

                case 6 when allowed.HasFlag(EaAllowed.Memory):
                {
                    var extensionAddress = cursor.Address;
                    var extension = cursor.FetchWord();
                    ea = new M68kDecodedEa(M68kJitEaKind.AddressIndex, reg, extensionAddress, extension, 0, 0);
                    return true;
                }

                case 7:
                    return TryDecodeMode7(ref cursor, reg, size, allowed, out ea);
                default:
                    return false;
            }
        }

        private static bool TryDecodeMode7(
            ref DecodeCursor cursor,
            int reg,
            M68kOperandSize size,
            EaAllowed allowed,
            out M68kDecodedEa ea)
        {
            ea = default;
            switch (reg)
            {
                case 0 when allowed.HasFlag(EaAllowed.Memory):
                {
                    var extensionAddress = cursor.Address;
                    var extension = cursor.FetchWord();
                    ea = new M68kDecodedEa(M68kJitEaKind.AbsoluteWord, 0, extensionAddress, extension, 0, 0);
                    return true;
                }

                case 1 when allowed.HasFlag(EaAllowed.Memory):
                {
                    var extensionAddress = cursor.Address;
                    var high = cursor.FetchWord();
                    var low = cursor.FetchWord();
                    ea = new M68kDecodedEa(M68kJitEaKind.AbsoluteLong, 0, extensionAddress, high, low, 0);
                    return true;
                }

                case 2 when allowed.HasFlag(EaAllowed.PcMemory):
                {
                    var extensionAddress = cursor.Address;
                    var extension = cursor.FetchWord();
                    ea = new M68kDecodedEa(M68kJitEaKind.PcDisplacement, 0, extensionAddress, extension, 0, 0);
                    return true;
                }

                case 3 when allowed.HasFlag(EaAllowed.PcMemory):
                {
                    var extensionAddress = cursor.Address;
                    var extension = cursor.FetchWord();
                    ea = new M68kDecodedEa(M68kJitEaKind.PcIndex, 0, extensionAddress, extension, 0, 0);
                    return true;
                }

                case 4 when allowed.HasFlag(EaAllowed.Immediate):
                {
                    var extensionAddress = cursor.Address;
                    var immediate = FetchImmediate(ref cursor, size);
                    ea = new M68kDecodedEa(M68kJitEaKind.Immediate, 0, extensionAddress, 0, 0, immediate);
                    return true;
                }

                default:
                    return false;
            }
        }

        private static M68kOperandSize DecodeImmediateSize(ushort opcode)
        {
            return ((opcode >> 6) & 3) switch
            {
                0 => M68kOperandSize.Byte,
                1 => M68kOperandSize.Word,
                2 => M68kOperandSize.Long,
                _ => (M68kOperandSize)0
            };
        }

        private static uint FetchImmediate(ref DecodeCursor cursor, M68kOperandSize size)
        {
            return size switch
            {
                M68kOperandSize.Byte => (uint)(cursor.FetchWord() & 0xFF),
                M68kOperandSize.Word => cursor.FetchWord(),
                _ => ((uint)cursor.FetchWord() << 16) | cursor.FetchWord()
            };
        }

        private static M68kDecodedInstruction Create(
            uint pc,
            ushort opcode,
            M68kJitOperation operation,
            M68kOperandSize size,
            M68kDecodedEa source,
            M68kDecodedEa destination,
            int register,
            int quickValue,
            int condition,
            int displacement,
            int variant,
            ushort registerMask,
            uint branchBase,
            DecodeCursor cursor,
            bool stopsTrace)
        {
            return new M68kDecodedInstruction(
                Normalize(pc),
                opcode,
                operation,
                size,
                source,
                destination,
                register,
                quickValue,
                condition,
                displacement,
                variant,
                registerMask,
                Normalize(branchBase),
                cursor.ExtensionCount,
                cursor.Extension0,
                cursor.Extension1,
                cursor.Extension2,
                cursor.Extension3,
                cursor.Extension4,
                stopsTrace);
        }

        private static uint Normalize(uint address)
        {
            return address & 0x00FF_FFFF;
        }

        private struct DecodeCursor
        {
            private readonly IM68kCodeReader _codeReader;

            public DecodeCursor(IM68kCodeReader codeReader, uint address)
            {
                _codeReader = codeReader;
                Address = Normalize(address);
                ExtensionCount = 0;
                Extension0 = 0;
                Extension1 = 0;
                Extension2 = 0;
                Extension3 = 0;
                Extension4 = 0;
            }

            public uint Address { get; private set; }

            public int ExtensionCount { get; private set; }

            public ushort Extension0 { get; private set; }

            public ushort Extension1 { get; private set; }

            public ushort Extension2 { get; private set; }

            public ushort Extension3 { get; private set; }

            public ushort Extension4 { get; private set; }

            public ushort FetchWord()
            {
                var word = _codeReader.ReadHostWord(Address);
                switch (ExtensionCount)
                {
                    case 0:
                        Extension0 = word;
                        break;
                    case 1:
                        Extension1 = word;
                        break;
                    case 2:
                        Extension2 = word;
                        break;
                    case 3:
                        Extension3 = word;
                        break;
                    case 4:
                        Extension4 = word;
                        break;
                    default:
                        throw new InvalidOperationException("The MC68000 JIT decoder exceeded the supported extension word budget.");
                }

                ExtensionCount++;
                Address = Normalize(Address + 2);
                return word;
            }
        }
    }
}
