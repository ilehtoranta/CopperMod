/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CopperFloat;
using HotPathGuard;

namespace Copper68k
{
    internal enum M68040FpuFrameKind
    {
        Null = 0,
        Idle = 1,
        Unimplemented = 2,
        Busy = 3
    }

    internal enum M68040FpuExceptionStage
    {
        None = 0,
        PreInstruction = 1,
        PostInstruction = 2,
        UnimplementedInstruction = 3
    }

    internal sealed class M68040FpuState
    {
        public const uint ConditionNan = 0x0100_0000;
        public const uint ConditionInfinity = 0x0200_0000;
        public const uint ConditionZero = 0x0400_0000;
        public const uint ConditionNegative = 0x0800_0000;
        public const uint ExceptionInexact1 = 0x0000_0100;
        public const uint ExceptionInexact2 = 0x0000_0200;
        public const uint ExceptionDivideByZero = 0x0000_0400;
        public const uint ExceptionUnderflow = 0x0000_0800;
        public const uint ExceptionOverflow = 0x0000_1000;
        public const uint ExceptionOperandError = 0x0000_2000;
        public const uint ExceptionSignalingNan = 0x0000_4000;
        public const uint ExceptionBranchUnordered = 0x0000_8000;
        public const uint AccruedInexact = 0x0000_0008;
        public const uint AccruedDivideByZero = 0x0000_0010;
        public const uint AccruedUnderflow = 0x0000_0020;
        public const uint AccruedOverflow = 0x0000_0040;
        public const uint AccruedInvalid = 0x0000_0080;
        private const uint ConditionMask = ConditionNan | ConditionInfinity | ConditionZero | ConditionNegative;
        private const uint ExceptionMask = 0x0000_FF00;
		private const uint FpcrStorageMask = 0x0000_FFFF;
        internal static readonly ExtF80 DefaultNan = ExtF80.FromBits(0x7fff, ulong.MaxValue);
        private uint _fpcr;
        private uint _enabledExceptionMask;
        private ExtF80Context _context = new(
            ExtF80RoundingMode.ToNearestEven,
            ExtF80Precision.Extended,
            ExtF80TininessMode.BeforeRounding);

        public ExtF80[] FP { get; } = new ExtF80[8];

        public uint Fpcr
        {
            get => _fpcr;
            set
            {
                _fpcr = value & FpcrStorageMask;
                _enabledExceptionMask = _fpcr & ExceptionMask;
                _context = new ExtF80Context(
                    ((_fpcr >> 4) & 3) switch
                    {
                        1 => ExtF80RoundingMode.TowardZero,
                        2 => ExtF80RoundingMode.TowardNegativeInfinity,
                        3 => ExtF80RoundingMode.TowardPositiveInfinity,
                        _ => ExtF80RoundingMode.ToNearestEven
                    },
                    ((_fpcr >> 6) & 3) switch
                    {
                        1 => ExtF80Precision.Single,
                        2 or 3 => ExtF80Precision.Double,
                        _ => ExtF80Precision.Extended
                    },
                    ExtF80TininessMode.BeforeRounding);
            }
        }

        public uint Fpsr { get; set; }

        public uint Fpiar { get; set; }

        public uint LastStateFrameAddress { get; set; }

        public ushort LastStateFrameHeader { get; set; }

        public uint LastStateFrameSize { get; set; }

        public bool LastStateFrameRestore { get; set; }

        public bool HasExecutedNonConditionalInstruction { get; set; }

        public M68040FpuFrameKind StateFrameKind { get; set; }

        public ushort StateFrameCommand { get; set; }

        public uint StateFrameEffectiveAddress { get; set; }

        public ExtF80 StateFrameSource { get; set; }

        public ExtF80 StateFrameDestination { get; set; }

        public ExtF80 StateFrameWriteback { get; set; }

        public byte StateFrameSourceTag { get; set; }

        public byte StateFrameDestinationTag { get; set; }

        public byte StateFrameGrs { get; set; }

        public bool StateFrameE1 { get; set; }

        public bool StateFrameE3 { get; set; }

        public bool StateFrameT { get; set; }

        public bool StateFrameWritebackExponentBit { get; set; }

        public void Reset()
        {
            Array.Fill(FP, DefaultNan);
            Fpcr = 0;
            Fpsr = 0;
            Fpiar = 0;
            LastStateFrameAddress = 0;
            LastStateFrameHeader = 0;
            LastStateFrameSize = 0;
            LastStateFrameRestore = false;
            HasExecutedNonConditionalInstruction = false;
            StateFrameKind = M68040FpuFrameKind.Null;
            ClearStateFrameData();
        }

        public void MarkInstructionExecuted()
        {
            HasExecutedNonConditionalInstruction = true;
            if (StateFrameKind == M68040FpuFrameKind.Null)
            {
                StateFrameKind = M68040FpuFrameKind.Idle;
            }
        }

        public void PrepareUnimplementedInstruction(
            ushort command,
            uint effectiveAddress,
            ExtF80 source,
            ExtF80 destination)
        {
            MarkInstructionExecuted();
            StateFrameKind = M68040FpuFrameKind.Unimplemented;
            StateFrameCommand = command;
            StateFrameEffectiveAddress = effectiveAddress;
            StateFrameSource = source;
            StateFrameDestination = destination;
            StateFrameSourceTag = M68040FpuHelpers.GetStateFrameTag(source);
            StateFrameDestinationTag = M68040FpuHelpers.GetStateFrameTag(destination);
            StateFrameGrs = 1;
        }

        public void PrepareArithmeticException(
            int vector,
            ushort command,
            uint effectiveAddress,
            ExtF80 source,
            ExtF80 destination)
        {
            MarkInstructionExecuted();
            StateFrameKind = (vector is 49 or 51 or 53) && M68040FpuHelpers.IsBusyFrameOperation(command & 0x7F)
                ? M68040FpuFrameKind.Busy
                : M68040FpuFrameKind.Unimplemented;
            StateFrameCommand = command;
            StateFrameEffectiveAddress = effectiveAddress;
            StateFrameSource = source;
            StateFrameDestination = destination;
            StateFrameWriteback = destination;
            StateFrameSourceTag = M68040FpuHelpers.GetStateFrameTag(source);
            StateFrameDestinationTag = M68040FpuHelpers.GetStateFrameTag(destination);
            StateFrameGrs = vector == 54 ? (byte)7 : (byte)1;
            StateFrameE1 = StateFrameKind == M68040FpuFrameKind.Unimplemented;
            StateFrameE3 = StateFrameKind == M68040FpuFrameKind.Busy;
            StateFrameWritebackExponentBit = vector is 51 or 54;
        }

        public void PrepareUnsupportedData(
            ushort command,
            uint effectiveAddress,
            ExtF80 source,
            ExtF80 destination,
            bool postInstruction)
        {
            MarkInstructionExecuted();
            StateFrameKind = M68040FpuFrameKind.Busy;
            StateFrameCommand = command;
            StateFrameEffectiveAddress = effectiveAddress;
            StateFrameSource = source;
            StateFrameDestination = destination;
            StateFrameWriteback = destination;
            StateFrameSourceTag = M68040FpuHelpers.GetStateFrameTag(source);
            StateFrameDestinationTag = M68040FpuHelpers.GetStateFrameTag(destination);
            StateFrameGrs = 1;
            StateFrameE1 = postInstruction;
            StateFrameT = postInstruction;
        }

        public void ConsumeSavedStateFrame()
        {
            StateFrameKind = HasExecutedNonConditionalInstruction
                ? M68040FpuFrameKind.Idle
                : M68040FpuFrameKind.Null;
            ClearStateFrameData();
        }

        public void RestoreStateFrame(M68040FpuFrameKind kind)
        {
            StateFrameKind = kind;
            HasExecutedNonConditionalInstruction = kind != M68040FpuFrameKind.Null;
            if (kind is M68040FpuFrameKind.Null or M68040FpuFrameKind.Idle)
            {
                ClearStateFrameData();
            }
        }

        private void ClearStateFrameData()
        {
            StateFrameCommand = 0;
            StateFrameEffectiveAddress = 0;
            StateFrameSource = ExtF80.PositiveZero;
            StateFrameDestination = ExtF80.PositiveZero;
            StateFrameWriteback = ExtF80.PositiveZero;
            StateFrameSourceTag = 0;
            StateFrameDestinationTag = 0;
            StateFrameGrs = 0;
            StateFrameE1 = false;
            StateFrameE3 = false;
            StateFrameT = false;
            StateFrameWritebackExponentBit = false;
        }

        public ExtF80Context Context => _context;

        public void SetCondition(ExtF80 value)
        {
            value = M68040FpuHelpers.CanonicalizeOperand(value);
            var condition = 0u;
            var classification = value.Classification;
            if (classification is ExtF80Class.QuietNaN or ExtF80Class.SignalingNaN or ExtF80Class.Unsupported)
            {
                condition |= ConditionNan;
            }

            if (value.Sign)
            {
                condition |= ConditionNegative;
            }

            if (classification == ExtF80Class.Zero)
            {
                condition |= ConditionZero;
            }

            if (classification == ExtF80Class.Infinity)
            {
                condition |= ConditionInfinity;
            }

            Fpsr = (Fpsr & ~ConditionMask) | condition;
        }

        public void SetCompareCondition(ExtF80Comparison comparison)
        {
            var condition = comparison switch
            {
                ExtF80Comparison.Less => ConditionNegative,
                ExtF80Comparison.Equal => ConditionZero,
                ExtF80Comparison.Unordered => ConditionNan,
                _ => 0u
            };
            Fpsr = (Fpsr & ~ConditionMask) | condition;
        }

        public int ApplyExceptions(
            FloatingPointExceptionFlags flags,
            bool signalingNan = false,
            bool branchUnordered = false)
        {
            var status = 0u;
            if (branchUnordered)
            {
                status |= ExceptionBranchUnordered;
            }

            if ((flags & FloatingPointExceptionFlags.Invalid) != 0)
            {
                status |= signalingNan ? ExceptionSignalingNan : ExceptionOperandError;
            }

            if ((flags & FloatingPointExceptionFlags.Overflow) != 0)
            {
                status |= ExceptionOverflow;
            }

            if ((flags & FloatingPointExceptionFlags.Underflow) != 0)
            {
                status |= ExceptionUnderflow;
            }

            if ((flags & FloatingPointExceptionFlags.DivideByZero) != 0)
            {
                status |= ExceptionDivideByZero;
            }

            if ((flags & FloatingPointExceptionFlags.Inexact) != 0)
            {
                status |= ExceptionInexact2;
            }

            var accrued = 0u;
            if ((status & (ExceptionBranchUnordered | ExceptionSignalingNan | ExceptionOperandError)) != 0)
            {
                accrued |= AccruedInvalid;
            }

            if ((status & ExceptionOverflow) != 0)
            {
                accrued |= AccruedOverflow;
            }

            if ((status & ExceptionUnderflow) != 0)
            {
                accrued |= AccruedUnderflow;
            }

            if ((status & ExceptionDivideByZero) != 0)
            {
                accrued |= AccruedDivideByZero;
            }

            if ((status & (ExceptionInexact1 | ExceptionInexact2)) != 0)
            {
                accrued |= AccruedInexact;
            }

            Fpsr = (Fpsr & ~ExceptionMask) | status | accrued;
            var enabled = status & _enabledExceptionMask;
            if ((enabled & ExceptionBranchUnordered) != 0) return 48;
            if ((enabled & ExceptionSignalingNan) != 0) return 54;
            if ((enabled & ExceptionOperandError) != 0) return 52;
            if ((status & ExceptionOverflow) != 0) return 53;
            if ((status & ExceptionUnderflow) != 0) return 51;
            if ((enabled & ExceptionDivideByZero) != 0) return 50;
            if ((enabled & (ExceptionInexact1 | ExceptionInexact2)) != 0) return 49;
            return 0;
        }

        public bool CheckCondition(int condition)
        {
            var nan = (Fpsr & ConditionNan) != 0;
            var zero = (Fpsr & ConditionZero) != 0;
            var negative = (Fpsr & ConditionNegative) != 0;
            return (condition & 0x0F) switch
            {
                0x0 => false,
                0x1 => zero,
                0x2 => !(nan || zero || negative),
                0x3 => zero || !(nan || negative),
                0x4 => negative && !nan,
                0x5 => zero || (negative && !nan),
                0x6 => !zero && !nan,
                0x7 => !nan,
                0x8 => nan,
                0x9 => nan || zero,
                0xA => nan || !(zero || negative),
                0xB => nan || zero || !negative,
                0xC => nan || negative,
                0xD => nan || zero || negative,
                0xE => nan || !zero,
                _ => true
            };
        }

        public bool EvaluateCondition(int condition, out int exceptionVector)
        {
            var unordered = (Fpsr & ConditionNan) != 0;
            var signaling = (condition & 0x10) != 0;
            exceptionVector = signaling && unordered
                ? ApplyExceptions(FloatingPointExceptionFlags.None, branchUnordered: true)
                : 0;
            return CheckCondition(condition);
        }
    }

    internal enum M68040FpuJitKind
    {
        Operation = 0,
        MoveToEa = 1,
        MoveToControl = 2,
        MoveFromControl = 3,
        LineFTrap = 4,
        SaveState = 5,
        RestoreState = 6,
        UnimplementedOperation = 7
    }

    internal readonly record struct M68040FpuExecutionResult(
        bool Supported,
        int ExceptionVector,
        M68040FpuExceptionStage ExceptionStage = M68040FpuExceptionStage.None)
    {
        public static M68040FpuExecutionResult Unsupported { get; } = new(false, 0);
    }

    internal readonly record struct M68040FpuOperand(
        ExtF80 Value,
        FloatingPointExceptionFlags Flags,
        uint EffectiveAddress = 0,
        bool HasEffectiveAddress = false,
        bool Unsupported = false);

    internal static class M68040FpuHelpers
    {
        private const ulong IntegerBit = 0x8000_0000_0000_0000UL;

        public const uint NullStateFrame = 0x0000_0000;
        public const uint NullStateFrameSize = 4;
        public const uint IdleStateFrame = 0x4100_0000;
        public const uint IdleStateFrameSize = 4;
        public const uint UnimplementedStateFrame = 0x4130_0000;
        public const uint UnimplementedStateFrameSize = 0x34;
        public const uint BusyStateFrame = 0x4160_0000;
        public const uint BusyStateFrameSize = 0x64;

        public static bool IsSupportedOperation(int opmode)
            => opmode is
                0x00 or 0x40 or 0x44 or
                0x04 or 0x41 or 0x45 or
                0x18 or 0x58 or 0x5C or
                0x1A or 0x5A or 0x5E or
                0x20 or 0x60 or 0x64 or
                0x22 or 0x62 or 0x66 or
                0x23 or 0x63 or 0x67 or
                0x24 or 0x27 or
                0x28 or 0x68 or 0x6C or
                0x38 or 0x3A;

        public static bool IsRegisterOperationCommand(ushort extension)
            => (extension & 0xE000) == 0 && IsSupportedOperation(extension & 0x007F);

        public static bool IsRecognizedUnimplementedOperation(int opmode)
            => opmode is
                0x01 or 0x02 or 0x03 or 0x06 or 0x08 or 0x09 or 0x0A or 0x0C or 0x0D or 0x0E or 0x0F or
                0x10 or 0x11 or 0x12 or 0x14 or 0x15 or 0x16 or 0x19 or 0x1C or 0x1D or 0x1E or 0x1F or
                0x21 or 0x25 or 0x26 or
                >= 0x30 and <= 0x37;

        public static bool IsBusyFrameOperation(int opmode)
            => opmode is 0x04 or 0x20 or 0x22 or 0x23 or 0x24 or 0x27 or 0x28 or
                0x41 or 0x45 or 0x60 or 0x62 or 0x63 or 0x64 or 0x66 or 0x67 or 0x68 or 0x6C;

        public static M68kInstructionTimingKey OperationTimingKey(int opmode)
        {
            return opmode switch
            {
                0x00 or 0x40 or 0x44 => M68kInstructionTimingKey.FpuMove,
                0x04 or 0x41 or 0x45 => M68kInstructionTimingKey.FpuSquareRoot,
                0x18 or 0x58 or 0x5C => M68kInstructionTimingKey.FpuAbsolute,
                0x1A or 0x5A or 0x5E => M68kInstructionTimingKey.FpuNegate,
                0x20 or 0x60 or 0x64 or 0x24 => M68kInstructionTimingKey.FpuDivide,
                0x22 or 0x62 or 0x66 => M68kInstructionTimingKey.FpuAdd,
                0x23 or 0x63 or 0x67 or 0x27 => M68kInstructionTimingKey.FpuMultiply,
                0x28 or 0x68 or 0x6C => M68kInstructionTimingKey.FpuSubtract,
                0x38 => M68kInstructionTimingKey.FpuCompare,
                0x3A => M68kInstructionTimingKey.FpuTest,
                _ => M68kInstructionTimingKey.LineFException
            };
        }

        public static bool IsSupportedDataRegisterFormat(int format)
            => format is 0 or 1 or 4 or 6;

        public static bool IsSupportedMemoryFormat(int format)
            => format is >= 0 and <= 7;

        public static bool UsesBus(M68kDecodedInstruction instruction)
            => (M68040FpuJitKind)instruction.Variant is M68040FpuJitKind.LineFTrap or M68040FpuJitKind.SaveState or M68040FpuJitKind.RestoreState ||
                instruction.Source.IsMemory ||
                instruction.Destination.IsMemory;

        public static uint StateFrameSize(ushort header)
        {
            var bodySize = header & 0x00FFu;
            return bodySize + 4;
        }

        public static bool IsNullStateFrameHeader(ushort header)
            => header == 0;

        public static uint CurrentStateFrame(M68040FpuState fpu)
            => fpu.StateFrameKind switch
            {
                M68040FpuFrameKind.Null => NullStateFrame,
                M68040FpuFrameKind.Idle => IdleStateFrame,
                M68040FpuFrameKind.Unimplemented => UnimplementedStateFrame,
                M68040FpuFrameKind.Busy => BusyStateFrame,
                _ => throw new InvalidOperationException()
            };

        public static bool TryGetStateFrameKind(ushort header, out M68040FpuFrameKind kind)
        {
            kind = header switch
            {
                0x0000 => M68040FpuFrameKind.Null,
                0x4100 => M68040FpuFrameKind.Idle,
                0x4130 or 0x4028 => M68040FpuFrameKind.Unimplemented,
                0x4160 => M68040FpuFrameKind.Busy,
                _ => M68040FpuFrameKind.Null
            };
            return header is 0x0000 or 0x4100 or 0x4130 or 0x4028 or 0x4160;
        }

        public static uint GetStateFrameLong(M68040FpuState fpu, uint offset)
        {
            if (offset == 0)
            {
                return CurrentStateFrame(fpu);
            }

            if (fpu.StateFrameKind is M68040FpuFrameKind.Null or M68040FpuFrameKind.Idle)
            {
                return 0;
            }

            var commonOffset = fpu.StateFrameKind == M68040FpuFrameKind.Busy ? 0x34u : 0x04u;
            if (fpu.StateFrameKind == M68040FpuFrameKind.Busy)
            {
                switch (offset)
                {
                    case 0x18:
                        return ExtendedWord0(fpu.StateFrameWriteback) |
                            (fpu.StateFrameWritebackExponentBit ? 0x0001_0000u : 0);
                    case 0x1C:
                        return (uint)(fpu.StateFrameWriteback.Significand >> 32);
                    case 0x20:
                        return (uint)fpu.StateFrameWriteback.Significand;
                    case 0x28:
                        return fpu.Fpiar;
                }
            }

            var relative = offset - commonOffset;
            return relative switch
            {
                0x00 => (uint)GetCommandReg3B(fpu.StateFrameCommand) << 16,
                0x08 => ((uint)fpu.StateFrameSourceTag << 29) | ((uint)fpu.StateFrameGrs << 23),
                0x0C => (uint)fpu.StateFrameCommand << 16,
                0x10 => (uint)fpu.StateFrameDestinationTag << 29,
                0x14 => (fpu.StateFrameE1 ? 1u << 26 : 0) |
                    (fpu.StateFrameE3 ? 1u << 25 : 0) |
                    (fpu.StateFrameT ? 1u << 20 : 0),
                0x18 => ExtendedWord0(fpu.StateFrameDestination),
                0x1C => (uint)(fpu.StateFrameDestination.Significand >> 32),
                0x20 => (uint)fpu.StateFrameDestination.Significand,
                0x24 => ExtendedWord0(fpu.StateFrameSource),
                0x28 => (uint)(fpu.StateFrameSource.Significand >> 32),
                0x2C => (uint)fpu.StateFrameSource.Significand,
                _ => 0
            };
        }

        public static void RestoreStateFrameLong(M68040FpuState fpu, uint offset, uint value)
        {
            if (offset == 0)
            {
                return;
            }

            var commonOffset = fpu.StateFrameKind == M68040FpuFrameKind.Busy ? 0x34u : 0x04u;
            if (fpu.StateFrameKind == M68040FpuFrameKind.Busy)
            {
                switch (offset)
                {
                    case 0x18:
                        fpu.StateFrameWriteback = WithExtendedWord0(fpu.StateFrameWriteback, value);
                        fpu.StateFrameWritebackExponentBit = (value & 0x0001_0000) != 0;
                        return;
                    case 0x1C:
                        fpu.StateFrameWriteback = WithSignificandHigh(fpu.StateFrameWriteback, value);
                        return;
                    case 0x20:
                        fpu.StateFrameWriteback = WithSignificandLow(fpu.StateFrameWriteback, value);
                        return;
                    case 0x28:
                        fpu.Fpiar = value;
                        return;
                }
            }

            switch (offset - commonOffset)
            {
                case 0x08:
                    fpu.StateFrameSourceTag = (byte)((value >> 29) & 7);
                    fpu.StateFrameGrs = (byte)((value >> 23) & 7);
                    break;
                case 0x0C:
                    fpu.StateFrameCommand = (ushort)(value >> 16);
                    break;
                case 0x10:
                    fpu.StateFrameDestinationTag = (byte)((value >> 29) & 7);
                    break;
                case 0x14:
                    fpu.StateFrameE1 = (value & (1u << 26)) != 0;
                    fpu.StateFrameE3 = (value & (1u << 25)) != 0;
                    fpu.StateFrameT = (value & (1u << 20)) != 0;
                    break;
                case 0x18:
                    fpu.StateFrameDestination = WithExtendedWord0(fpu.StateFrameDestination, value);
                    break;
                case 0x1C:
                    fpu.StateFrameDestination = WithSignificandHigh(fpu.StateFrameDestination, value);
                    break;
                case 0x20:
                    fpu.StateFrameDestination = WithSignificandLow(fpu.StateFrameDestination, value);
                    break;
                case 0x24:
                    fpu.StateFrameSource = WithExtendedWord0(fpu.StateFrameSource, value);
                    break;
                case 0x28:
                    fpu.StateFrameSource = WithSignificandHigh(fpu.StateFrameSource, value);
                    break;
                case 0x2C:
                    fpu.StateFrameSource = WithSignificandLow(fpu.StateFrameSource, value);
                    break;
            }
        }

        public static byte GetStateFrameTag(ExtF80 value)
        {
            value = CanonicalizeOperand(value);
            return value.Classification switch
            {
                ExtF80Class.Zero => 1,
                ExtF80Class.Infinity => 2,
                ExtF80Class.QuietNaN or ExtF80Class.SignalingNaN => 3,
                ExtF80Class.Subnormal or ExtF80Class.Unsupported => 4,
                _ => 0
            };
        }

        private static ushort GetCommandReg3B(ushort command)
            => (ushort)((command & 0x03C3) | ((command & 0x0038) >> 1) | ((command & 0x0004) << 3));

        private static uint ExtendedWord0(ExtF80 value)
            => (uint)value.SignExponent << 16;

        private static ExtF80 WithExtendedWord0(ExtF80 value, uint word)
            => ExtF80.FromBits((ushort)(word >> 16), value.Significand);

        private static ExtF80 WithSignificandHigh(ExtF80 value, uint word)
            => ExtF80.FromBits(value.SignExponent, ((ulong)word << 32) | (uint)value.Significand);

        private static ExtF80 WithSignificandLow(ExtF80 value, uint word)
            => ExtF80.FromBits(value.SignExponent, (value.Significand & 0xFFFF_FFFF_0000_0000UL) | word);

        public static uint FormatByteSize(int format)
        {
            return format switch
            {
                0 or 1 => 4,
                2 or 3 or 7 => 12,
                4 => 2,
                5 => 8,
                6 => 1,
                _ => 4
            };
        }

        public static FloatingPointResult<ExtF80> ReadDataRegister(uint value, int format)
        {
            return format switch
            {
                0 => Exact(ExtF80Math.FromInt32(unchecked((int)value))),
                1 => ExtF80Math.FromBinary32Bits(value),
                4 => Exact(ExtF80Math.FromInt32(unchecked((short)(value & 0xFFFF)))),
                6 => Exact(ExtF80Math.FromInt32(unchecked((sbyte)(value & 0xFF)))),
                _ => throw new M68kEmulationException($"Unsupported MC68040 FPU data-register format {format}.")
            };
        }

        public static FloatingPointResult<uint> WriteDataRegister(
            uint currentValue,
            int format,
            ExtF80 value,
            ExtF80RoundingMode roundingMode)
        {
            switch (format)
            {
                case 0:
                {
                    var converted = ExtF80Math.ToInt32(value, roundingMode);
                    return new FloatingPointResult<uint>(
                        ResolveIntegerDestination(value, format, converted.Value, converted.Flags),
                        converted.Flags);
                }
                case 1:
                    return ExtF80Math.ToBinary32Bits(value, roundingMode);
                case 4:
                {
                    var converted = ExtF80Math.ToInt16(value, roundingMode);
                    return new FloatingPointResult<uint>(
                        (currentValue & 0xFFFF_0000u) |
                            ResolveIntegerDestination(value, format, converted.Value, converted.Flags),
                        converted.Flags);
                }
                case 6:
                {
                    var converted = ExtF80Math.ToInt8(value, roundingMode);
                    return new FloatingPointResult<uint>(
                        (currentValue & 0xFFFF_FF00u) |
                            ResolveIntegerDestination(value, format, converted.Value, converted.Flags),
                        converted.Flags);
                }
                default:
                    throw new M68kEmulationException($"Unsupported MC68040 FPU data-register format {format}.");
            }
        }

        public static FloatingPointResult<ExtF80> ReadImmediate(
            int format,
            ushort extension0,
            ushort extension1,
            uint immediate)
        {
            return format switch
            {
                0 => Exact(ExtF80Math.FromInt32(unchecked((int)immediate))),
                1 => ExtF80Math.FromBinary32Bits(immediate),
                4 => Exact(ExtF80Math.FromInt32(unchecked((short)immediate))),
                5 => ExtF80Math.FromBinary64Bits(
                    ((ulong)extension0 << 48) |
                    ((ulong)extension1 << 32) |
                    immediate),
                6 => Exact(ExtF80Math.FromInt32(unchecked((sbyte)(byte)immediate))),
                _ => throw new M68kEmulationException($"Unsupported MC68040 FPU immediate format {format}.")
            };
        }

        public static M68040FpuExecutionResult ApplyOperation(
            M68040FpuState fpu,
            int destination,
            int opmode,
            ExtF80 source,
            FloatingPointExceptionFlags sourceFlags = FloatingPointExceptionFlags.None,
            bool unsupportedSource = false)
        {
            if (!IsSupportedOperation(opmode))
            {
                return M68040FpuExecutionResult.Unsupported;
            }

            return ApplyOperationCore(
                fpu,
                destination,
                opmode,
                source,
                sourceFlags,
                unsupportedSource);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static M68040FpuExecutionResult ApplyRegisterOperation(
            M68040FpuState fpu,
            int destination,
            int opmode,
            ExtF80 source)
            => ApplyOperationCore(
                fpu,
                destination,
                opmode,
                source,
                FloatingPointExceptionFlags.None,
                unsupportedSource: false);

        private static M68040FpuExecutionResult ApplyOperationCore(
            M68040FpuState fpu,
            int destination,
            int opmode,
            ExtF80 source,
            FloatingPointExceptionFlags sourceFlags,
            bool unsupportedSource)
        {
            var destinationValue = fpu.FP[destination];
            if (sourceFlags == FloatingPointExceptionFlags.None &&
                !unsupportedSource &&
                IsCanonicalNormal(source) &&
                (!OperationReadsDestination(opmode) || IsCanonicalNormal(destinationValue)))
            {
                return ApplyNormalOperation(
                    fpu,
                    destination,
                    opmode,
                    source,
                    destinationValue);
            }

            fpu.MarkInstructionExecuted();
            var rawSource = source;
            if (unsupportedSource ||
                IsUnsupportedDataType(source) ||
                (OperationReadsDestination(opmode) && IsUnsupportedDataType(destinationValue)))
            {
                return new M68040FpuExecutionResult(
                    true,
                    55,
                    M68040FpuExceptionStage.PreInstruction);
            }

            source = CanonicalizeOperand(source);
            destinationValue = CanonicalizeOperand(destinationValue);
            // MC68040 FSGLMUL truncates input mantissas before multiplying; its
            // FSGLDIV uses the extended inputs and only rounds the quotient.
            if (opmode == 0x27)
            {
                source = TruncateSinglePrecisionOperand(source);
                destinationValue = TruncateSinglePrecisionOperand(destinationValue);
            }

            var context = OperationContext(fpu.Context, opmode);
            var signalingNan = source.Classification == ExtF80Class.SignalingNaN ||
                destinationValue.Classification == ExtF80Class.SignalingNaN ||
                (sourceFlags & FloatingPointExceptionFlags.Invalid) != 0;
            if (opmode == 0x38)
            {
                var comparison = ExtF80Math.Compare(destinationValue, source);
                fpu.SetCompareCondition(comparison.Value);
                var vector = fpu.ApplyExceptions(sourceFlags | comparison.Flags, signalingNan);
                return new M68040FpuExecutionResult(
                    true,
                    vector,
                    vector == 0 ? M68040FpuExceptionStage.None : M68040FpuExceptionStage.PostInstruction);
            }

            if (opmode == 0x3A)
            {
                fpu.SetCondition(source);
                var vector = fpu.ApplyExceptions(sourceFlags, signalingNan);
                return new M68040FpuExecutionResult(
                    true,
                    vector,
                    vector == 0 ? M68040FpuExceptionStage.None : M68040FpuExceptionStage.PostInstruction);
            }

            var operation = opmode switch
            {
                0x00 or 0x40 or 0x44 => ExtF80Math.Round(source, context),
                0x04 or 0x41 or 0x45 => ExtF80Math.SquareRoot(source, context),
                0x18 or 0x58 or 0x5C => RoundUnary(ExtF80Math.Absolute(source), context),
                0x1A or 0x5A or 0x5E => RoundUnary(ExtF80Math.Negate(source), context),
                0x20 or 0x60 or 0x64 or 0x24 => ExtF80Math.Divide(destinationValue, source, context),
                0x22 or 0x62 or 0x66 => ExtF80Math.Add(destinationValue, source, context),
                0x23 or 0x63 or 0x67 or 0x27 => ExtF80Math.Multiply(destinationValue, source, context),
                0x28 or 0x68 or 0x6C => ExtF80Math.Subtract(destinationValue, source, context),
                _ => throw new InvalidOperationException()
            };
            if (opmode is not (0x24 or 0x27))
            {
                operation = ConstrainPrecisionRange(operation, context);
            }
            operation = PreserveUnarySpecialEncoding(rawSource, opmode, operation);
            operation = PreserveBinaryInfinityEncoding(rawSource, fpu.FP[destination], opmode, operation);
            if ((operation.Flags & FloatingPointExceptionFlags.Invalid) != 0 &&
                !IsNan(source.Classification) &&
                !IsNan(destinationValue.Classification))
            {
                operation = new FloatingPointResult<ExtF80>(M68040FpuState.DefaultNan, operation.Flags);
            }

            var flags = sourceFlags | operation.Flags;
            var exceptionVector = fpu.ApplyExceptions(flags, signalingNan);
            fpu.SetCondition(operation.Value);
            if (exceptionVector == 0)
            {
                fpu.FP[destination] = operation.Value;
            }

            return new M68040FpuExecutionResult(
                true,
                exceptionVector,
                exceptionVector == 0 ? M68040FpuExceptionStage.None : M68040FpuExceptionStage.PostInstruction);
        }

        private static M68040FpuExecutionResult ApplyNormalOperation(
            M68040FpuState fpu,
            int destination,
            int opmode,
            ExtF80 source,
            ExtF80 destinationValue)
        {
            fpu.MarkInstructionExecuted();
            if (opmode == 0x38)
            {
                var comparison = ExtF80Math.Compare(destinationValue, source);
                fpu.SetCompareCondition(comparison.Value);
                _ = fpu.ApplyExceptions(comparison.Flags);
                return new M68040FpuExecutionResult(true, 0);
            }

            if (opmode == 0x3A)
            {
                fpu.SetCondition(source);
                _ = fpu.ApplyExceptions(FloatingPointExceptionFlags.None);
                return new M68040FpuExecutionResult(true, 0);
            }

            if (opmode == 0x27)
            {
                source = TruncateSinglePrecisionOperand(source);
                destinationValue = TruncateSinglePrecisionOperand(destinationValue);
            }

            var context = OperationContext(fpu.Context, opmode);
            var operation = opmode switch
            {
                0x00 or 0x40 or 0x44 => ExtF80Math.Round(source, context),
                0x04 or 0x41 or 0x45 => ExtF80Math.SquareRoot(source, context),
                0x18 or 0x58 or 0x5C => RoundUnary(ExtF80Math.Absolute(source), context),
                0x1A or 0x5A or 0x5E => RoundUnary(ExtF80Math.Negate(source), context),
                0x20 or 0x60 or 0x64 or 0x24 => ExtF80Math.Divide(destinationValue, source, context),
                0x22 or 0x62 or 0x66 => ExtF80Math.Add(destinationValue, source, context),
                0x23 or 0x63 or 0x67 or 0x27 => ExtF80Math.Multiply(destinationValue, source, context),
                0x28 or 0x68 or 0x6C => ExtF80Math.Subtract(destinationValue, source, context),
                _ => throw new InvalidOperationException()
            };
            if (opmode is not (0x24 or 0x27))
            {
                operation = ConstrainPrecisionRange(operation, context);
            }

            if ((operation.Flags & FloatingPointExceptionFlags.Invalid) != 0)
            {
                operation = new FloatingPointResult<ExtF80>(M68040FpuState.DefaultNan, operation.Flags);
            }

            var exceptionVector = fpu.ApplyExceptions(operation.Flags);
            fpu.SetCondition(operation.Value);
            if (exceptionVector == 0)
            {
                fpu.FP[destination] = operation.Value;
            }

            return new M68040FpuExecutionResult(
                true,
                exceptionVector,
                exceptionVector == 0 ? M68040FpuExceptionStage.None : M68040FpuExceptionStage.PostInstruction);
        }

        private static bool OperationReadsDestination(int opmode)
            => opmode is
                0x20 or 0x60 or 0x64 or 0x24 or
                0x22 or 0x62 or 0x66 or
                0x23 or 0x63 or 0x67 or 0x27 or
                0x28 or 0x68 or 0x6C or
                0x38;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsCanonicalNormal(ExtF80 value)
            => value.BiasedExponent is > 0 and < 0x7fff &&
                (value.Significand & IntegerBit) != 0;

        private static ExtF80 TruncateSinglePrecisionOperand(ExtF80 value)
            => value.Classification == ExtF80Class.Normal
                ? ExtF80.FromBits(value.SignExponent, value.Significand & 0xFFFF_FF00_0000_0000UL)
                : value;

        public static int ApplyDestinationExceptions(
            M68040FpuState fpu,
            ExtF80 value,
            FloatingPointExceptionFlags flags,
            int format)
        {
            var integerDestination = format is 0 or 4 or 6;
            var signalingNan = value.Classification == ExtF80Class.SignalingNaN;
            var forcedOperandError = integerDestination &&
                (((flags & FloatingPointExceptionFlags.Invalid) != 0) || IsErroneousMinimumInteger(value, format));
            var vector = fpu.ApplyExceptions(
                forcedOperandError ? flags | FloatingPointExceptionFlags.Invalid : flags,
                signalingNan);
            if (integerDestination && signalingNan)
            {
                return 54;
            }

            return forcedOperandError ? 52 : vector;
        }

        public static bool ShouldCommitDestination(int format, int exceptionVector)
            => exceptionVector == 0 ||
                (format is 0 or 4 or 6 && exceptionVector is 52 or 54) ||
                (format is 1 or 5 && exceptionVector is 51 or 53);

        public static uint ResolveIntegerDestination(
            ExtF80 value,
            int format,
            long converted,
            FloatingPointExceptionFlags flags)
        {
            var width = format switch
            {
                0 => 32,
                4 => 16,
                6 => 8,
                _ => throw new M68kEmulationException($"Unsupported MC68040 integer destination format {format}.")
            };
            var mask = width == 32 ? uint.MaxValue : (1u << width) - 1;
            if ((flags & FloatingPointExceptionFlags.Invalid) == 0)
            {
                return unchecked((uint)converted) & mask;
            }

            if (value.Classification is ExtF80Class.QuietNaN or ExtF80Class.SignalingNaN)
            {
                return (uint)(value.Significand >> (64 - width)) & mask;
            }

            var signBit = 1u << (width - 1);
            return value.Sign ? signBit : signBit - 1;
        }

        public static bool IsUnsupportedDataType(ExtF80 value)
        {
            var exponent = value.BiasedExponent;
            var significand = value.Significand;
            if (significand == 0 || exponent == 0x7fff)
            {
                return false;
            }

            return (significand & IntegerBit) == 0;
        }

        public static ExtF80 CanonicalizeOperand(ExtF80 value)
        {
            var exponent = value.BiasedExponent;
            var significand = value.Significand;
            if (exponent == 0x7fff)
            {
                return ExtF80.FromBits(value.SignExponent, significand | IntegerBit);
            }

            if (significand == 0)
            {
                return value.Sign ? ExtF80.NegativeZero : ExtF80.PositiveZero;
            }

            if (exponent == 0 && (significand & IntegerBit) != 0)
            {
                return ExtF80.FromBits((ushort)((value.Sign ? 0x8000 : 0) | 1), significand);
            }

            return value;
        }

        private static ExtF80Context OperationContext(ExtF80Context context, int opmode)
        {
            if (opmode is 0x40 or 0x41 or 0x58 or 0x5A or 0x60 or 0x62 or 0x63 or 0x68 or 0x24 or 0x27)
            {
                return context with { Precision = ExtF80Precision.Single };
            }

            if (opmode is 0x44 or 0x45 or 0x5C or 0x5E or 0x64 or 0x66 or 0x67 or 0x6C)
            {
                return context with { Precision = ExtF80Precision.Double };
            }

            return context;
        }

        private static FloatingPointResult<ExtF80> RoundUnary(
            FloatingPointResult<ExtF80> unary,
            ExtF80Context context)
        {
            var rounded = ExtF80Math.Round(unary.Value, context);
            return new FloatingPointResult<ExtF80>(rounded.Value, unary.Flags | rounded.Flags);
        }

        private static FloatingPointResult<ExtF80> PreserveUnarySpecialEncoding(
            ExtF80 source,
            int opmode,
            FloatingPointResult<ExtF80> result)
        {
            if (source.BiasedExponent != 0x7fff ||
                opmode is not (0x00 or 0x40 or 0x44 or 0x18 or 0x58 or 0x5C or 0x1A or 0x5A or 0x5E))
            {
                return result;
            }

            var significand = source.Significand;
            var nan = (significand << 1) != 0;
            if (nan)
            {
                significand |= 0x4000_0000_0000_0000UL;
            }

            var sign = nan
                ? source.Sign
                : opmode switch
                {
                    0x18 or 0x58 or 0x5C => false,
                    0x1A or 0x5A or 0x5E => !source.Sign,
                    _ => source.Sign
                };

            return result with
            {
                Value = ExtF80.FromBits((ushort)((sign ? 0x8000 : 0) | 0x7fff), significand)
            };
        }

        private static FloatingPointResult<ExtF80> PreserveBinaryInfinityEncoding(
            ExtF80 source,
            ExtF80 destination,
            int opmode,
            FloatingPointResult<ExtF80> result)
        {
            if (opmode is not (0x20 or 0x60 or 0x64 or 0x22 or 0x62 or 0x66 or
                    0x23 or 0x63 or 0x67 or 0x24 or 0x27 or 0x28 or 0x68 or 0x6C) ||
                result.Value.Classification != ExtF80Class.Infinity)
            {
                return result;
            }

            var significand = IsRawInfinity(destination)
                ? destination.Significand
                : IsRawInfinity(source)
                    ? source.Significand
                    : result.Value.Significand;
            return result with
            {
                Value = ExtF80.FromBits(result.Value.SignExponent, significand)
            };
        }

        private static bool IsRawInfinity(ExtF80 value)
            => value.BiasedExponent == 0x7fff && (value.Significand << 1) == 0;

        private static FloatingPointResult<ExtF80> ConstrainPrecisionRange(
            FloatingPointResult<ExtF80> result,
            ExtF80Context context)
        {
            if (context.Precision == ExtF80Precision.Extended ||
                result.Value.Classification is ExtF80Class.Zero or ExtF80Class.Infinity or
                    ExtF80Class.QuietNaN or ExtF80Class.SignalingNaN)
            {
                return result;
            }

            if (context.Precision == ExtF80Precision.Single)
            {
                var encoded = ExtF80Math.ToBinary32Bits(
                    result.Value,
                    context.RoundingMode,
                    context.TininessMode);
                var decoded = ExtF80Math.FromBinary32Bits(encoded.Value);
                return new FloatingPointResult<ExtF80>(
                    decoded.Value,
                    result.Flags | encoded.Flags | decoded.Flags);
            }

            var doubleEncoded = ExtF80Math.ToBinary64Bits(
                result.Value,
                context.RoundingMode,
                context.TininessMode);
            var doubleDecoded = ExtF80Math.FromBinary64Bits(doubleEncoded.Value);
            return new FloatingPointResult<ExtF80>(
                doubleDecoded.Value,
                result.Flags | doubleEncoded.Flags | doubleDecoded.Flags);
        }

        private static FloatingPointResult<ExtF80> Exact(ExtF80 value)
            => new(value, FloatingPointExceptionFlags.None);

        private static bool IsNan(ExtF80Class classification)
            => classification is ExtF80Class.QuietNaN or ExtF80Class.SignalingNaN;

        private static bool IsErroneousMinimumInteger(ExtF80 value, int format)
        {
            var minimum = format switch
            {
                0 => ExtF80Math.FromInt32(int.MinValue),
                4 => ExtF80Math.FromInt32(short.MinValue),
                6 => ExtF80Math.FromInt32(sbyte.MinValue),
                _ => ExtF80.PositiveZero
            };
            return format is 0 or 4 or 6 && value == minimum;
        }

    }

    internal sealed class M68040MmuState
    {
        private const uint TranslationEnable = 0x8000_0000;
        private readonly Dictionary<ulong, uint> _atc = [];
        private uint _translationControl;
        private uint _supervisorRootPointer;
        private uint _userRootPointer;
        private uint _instructionTransparentTranslation0;
        private uint _instructionTransparentTranslation1;
        private uint _dataTransparentTranslation0;
        private uint _dataTransparentTranslation1;
        private uint _status;
        private bool _bypassTranslation;
        private bool _directIdentityAccessEnabled = true;

        public uint TranslationControl
        {
            get => _translationControl;
            set
            {
                SetTranslationRegister(ref _translationControl, value);
                UpdateDirectIdentityAccess();
            }
        }

        public uint SupervisorRootPointer
        {
            get => _supervisorRootPointer;
            set => SetTranslationRegister(ref _supervisorRootPointer, value);
        }

        public uint UserRootPointer
        {
            get => _userRootPointer;
            set => SetTranslationRegister(ref _userRootPointer, value);
        }

        public uint InstructionTransparentTranslation0
        {
            get => _instructionTransparentTranslation0;
            set => SetTranslationRegister(ref _instructionTransparentTranslation0, value);
        }

        public uint InstructionTransparentTranslation1
        {
            get => _instructionTransparentTranslation1;
            set => SetTranslationRegister(ref _instructionTransparentTranslation1, value);
        }

        public uint DataTransparentTranslation0
        {
            get => _dataTransparentTranslation0;
            set => SetTranslationRegister(ref _dataTransparentTranslation0, value);
        }

        public uint DataTransparentTranslation1
        {
            get => _dataTransparentTranslation1;
            set => SetTranslationRegister(ref _dataTransparentTranslation1, value);
        }

        public uint Status
        {
            get => _status;
            set
            {
                if (_status == value)
                {
                    return;
                }

                _status = value;
                UpdateDirectIdentityAccess();
            }
        }

        public bool BypassTranslation
        {
            get => _bypassTranslation;
            set
            {
                if (_bypassTranslation == value)
                {
                    return;
                }

                _bypassTranslation = value;
                UpdateDirectIdentityAccess();
            }
        }

        public uint Generation { get; private set; }

        public bool Enabled => (TranslationControl & TranslationEnable) != 0;

        internal bool DirectIdentityAccessEnabled => _directIdentityAccessEnabled;

        internal bool CanBypassTranslation(
            uint logicalAddress,
            M68kBusAccessKind accessKind,
            bool write,
            bool supervisor)
            => !Enabled || MatchesTransparent(logicalAddress, accessKind, write, supervisor);

        public void Reset()
        {
            TranslationControl = 0;
            SupervisorRootPointer = 0;
            UserRootPointer = 0;
            InstructionTransparentTranslation0 = 0;
            InstructionTransparentTranslation1 = 0;
            DataTransparentTranslation0 = 0;
            DataTransparentTranslation1 = 0;
            Status = 0;
            BypassTranslation = false;
            Generation = 0;
            _atc.Clear();
            UpdateDirectIdentityAccess();
        }

        public void Flush()
            => _atc.Clear();

        private void SetTranslationRegister(ref uint register, uint value)
        {
            if (register == value)
            {
                return;
            }

            register = value;
            Generation++;
            Flush();
        }

        private void UpdateDirectIdentityAccess()
            => _directIdentityAccessEnabled =
                _bypassTranslation ||
                ((_translationControl & TranslationEnable) == 0 && _status == 0);

        public bool TryTranslate(
            uint logicalAddress,
            M68kBusAccessKind accessKind,
            bool write,
            bool supervisor,
            Func<uint, uint> readPhysicalLong,
            out uint physicalAddress,
            out M68040MmuFault fault)
        {
            physicalAddress = logicalAddress;
            fault = default;

            if (BypassTranslation)
            {
                return TryAcceptPhysical(logicalAddress, accessKind, write, out physicalAddress, out fault);
            }

            Status = 0;
            if (CanBypassTranslation(logicalAddress, accessKind, write, supervisor))
            {
                return TryAcceptPhysical(logicalAddress, accessKind, write, out physicalAddress, out fault);
            }

            var page = logicalAddress & 0xFFFF_F000u;
            var key = CreateAtcKey(page, accessKind, supervisor);
            if (_atc.TryGetValue(key, out var cachedBase))
            {
                return TryAcceptPhysical(cachedBase | (logicalAddress & 0x0000_0FFFu), accessKind, write, out physicalAddress, out fault);
            }

            var root = supervisor ? SupervisorRootPointer : UserRootPointer;
            if ((root & 0xFFFF_F000u) == 0)
            {
                return Fault(logicalAddress, accessKind, write, supervisor, 0x0000_0001, out fault);
            }

            var descriptorAddress = (root & 0xFFFF_F000u) + (((logicalAddress >> 12) & 0x000F_FFFFu) * 4u);
            uint descriptor;
            try
            {
                descriptor = readPhysicalLong(descriptorAddress);
            }
            catch (Exception ex) when (ex is M68kEmulationException or IndexOutOfRangeException)
            {
                return Fault(logicalAddress, accessKind, write, supervisor, 0x0000_0002, out fault);
            }

            if ((descriptor & 0x0000_0001u) == 0)
            {
                return Fault(logicalAddress, accessKind, write, supervisor, 0x0000_0004, out fault);
            }

            if (write && (descriptor & 0x0000_0004u) != 0)
            {
                return Fault(logicalAddress, accessKind, write, supervisor, 0x0000_0008, out fault);
            }

            var physicalBase = descriptor & 0xFFFF_F000u;
            _atc[key] = physicalBase;
            return TryAcceptPhysical(physicalBase | (logicalAddress & 0x0000_0FFFu), accessKind, write, out physicalAddress, out fault);
        }

        public void Probe(
            uint logicalAddress,
            M68kBusAccessKind accessKind,
            bool write,
            bool supervisor,
            Func<uint, uint> readPhysicalLong)
        {
            _ = TryTranslate(logicalAddress, accessKind, write, supervisor, readPhysicalLong, out _, out _);
        }

        private bool TryAcceptPhysical(
            uint candidate,
            M68kBusAccessKind accessKind,
            bool write,
            out uint physicalAddress,
            out M68040MmuFault fault)
        {
            physicalAddress = candidate;
            fault = default;
            return true;
        }

        private bool Fault(
            uint logicalAddress,
            M68kBusAccessKind accessKind,
            bool write,
            bool supervisor,
            uint status,
            out M68040MmuFault fault)
        {
            Status = status | (write ? 0x0000_0100u : 0) | (supervisor ? 0x0000_0200u : 0);
            fault = new M68040MmuFault(logicalAddress, accessKind, write, Status);
            return false;
        }

        private bool MatchesTransparent(uint address, M68kBusAccessKind accessKind, bool write, bool supervisor)
        {
            var instruction = accessKind == M68kBusAccessKind.CpuInstructionFetch;
            return MatchesTransparentRegister(address, instruction ? InstructionTransparentTranslation0 : DataTransparentTranslation0, write, supervisor) ||
                MatchesTransparentRegister(address, instruction ? InstructionTransparentTranslation1 : DataTransparentTranslation1, write, supervisor);
        }

        private static bool MatchesTransparentRegister(uint address, uint register, bool write, bool supervisor)
        {
            if ((register & 0x0000_8000u) == 0 && (register & 0x8000_0000u) == 0)
            {
                return false;
            }

            var userSupervisor = (register >> 13) & 0x3;
            if (userSupervisor == 0x1 && supervisor)
            {
                return false;
            }

            if (userSupervisor == 0x2 && !supervisor)
            {
                return false;
            }

            if (write && (register & 0x0000_0004u) != 0)
            {
                return false;
            }

            var baseByte = (register >> 24) & 0xFFu;
            var maskByte = (register >> 16) & 0xFFu;
            var addressByte = (address >> 24) & 0xFFu;
            return (addressByte & ~maskByte) == (baseByte & ~maskByte);
        }

        private static ulong CreateAtcKey(uint page, M68kBusAccessKind accessKind, bool supervisor)
            => ((ulong)page << 8) |
                ((ulong)(accessKind == M68kBusAccessKind.CpuInstructionFetch ? 1u : 0u) << 1) |
                (supervisor ? 1u : 0u);
    }

    internal readonly record struct M68040MmuFault(
        uint LogicalAddress,
        M68kBusAccessKind AccessKind,
        bool Write,
        uint Status,
        uint? StackedProgramCounter = null);

    internal sealed class UnsupportedM68040InstructionException : M68kEmulationException
    {
        public UnsupportedM68040InstructionException(
            ushort opcode,
            uint programCounter,
            string profileName,
            Exception? innerException = null)
            : base($"Unsupported MC68040 instruction form for opcode 0x{opcode:X4} at 0x{programCounter:X8} in profile {profileName}.")
        {
            Opcode = opcode;
            ProgramCounter = programCounter;
            ProfileName = profileName;
            OriginalException = innerException;
        }

        public ushort Opcode { get; }

        public uint ProgramCounter { get; }

        public string ProfileName { get; }

        public Exception? OriginalException { get; }
    }

    internal sealed class M68040MmuFaultException : M68kEmulationException
    {
        public M68040MmuFaultException(M68040MmuFault fault)
            : base($"MC68040 MMU fault at logical address 0x{fault.LogicalAddress:X8}.")
        {
            Fault = fault;
        }

        public M68040MmuFault Fault { get; }
    }

    internal sealed class M68040LogicalBus : IM68kBus, IM68kCodeReader, IM68kFastMemoryBus
    {
        private const uint PhysicalMapPageMask = 0xFFFF_FF00u;
        private const int PhysicalMapPageSize = 0x100;

        private readonly IM68kBus _physicalBus;
        private readonly IM68kCodeReader? _codeReader;
        private readonly IM68kFastMemoryBus? _fastMemoryBus;
        private readonly IM68kPhysicalAddressMap? _physicalAddressMap;
        private readonly IM68kStablePhysicalAddressMap? _stablePhysicalAddressMap;
        private readonly PhysicalMapRange _instructionMapRange;
        private readonly PhysicalMapRange _dataReadMapRange;
        private readonly PhysicalMapRange _dataWriteMapRange;
        private readonly bool _allPhysicalAddressesMapped;
        private readonly Func<uint, uint> _readPhysicalLong;
        private readonly M68kCpuState _state;
        private readonly M68040MmuState _mmu;
        private PhysicalMapPageCache _instructionMapCache;
        private PhysicalMapPageCache _dataReadMapCache;
        private PhysicalMapPageCache _dataWriteMapCache;

        public M68040LogicalBus(IM68kBus physicalBus, M68kCpuState state)
        {
            _physicalBus = physicalBus ?? throw new ArgumentNullException(nameof(physicalBus));
            _codeReader = physicalBus as IM68kCodeReader;
            _fastMemoryBus = physicalBus as IM68kFastMemoryBus;
            _physicalAddressMap = physicalBus as IM68kPhysicalAddressMap;
            _stablePhysicalAddressMap = physicalBus as IM68kStablePhysicalAddressMap;
            if (physicalBus is IM68kFixedPhysicalAddressMap fixedMap)
            {
                _instructionMapRange = GetFixedPhysicalMapRange(
                    fixedMap,
                    M68kBusAccessKind.CpuInstructionFetch);
                _dataReadMapRange = GetFixedPhysicalMapRange(
                    fixedMap,
                    M68kBusAccessKind.CpuDataRead);
                _dataWriteMapRange = GetFixedPhysicalMapRange(
                    fixedMap,
                    M68kBusAccessKind.CpuDataWrite);
                _allPhysicalAddressesMapped =
                    _instructionMapRange.CoversAllAddresses &&
                    _dataReadMapRange.CoversAllAddresses &&
                    _dataWriteMapRange.CoversAllAddresses;
            }

            _state = state ?? throw new ArgumentNullException(nameof(state));
            _mmu = state.M68040Mmu;
            _readPhysicalLong = ReadPhysicalLong;
        }

        internal IM68kBus PhysicalBus => _physicalBus;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool CanUseDirectIdentityAccess(uint address, int byteCount)
            => _allPhysicalAddressesMapped &&
                _mmu.DirectIdentityAccessEnabled &&
                (uint)(byteCount - 1) <= uint.MaxValue - address;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
            => _physicalBus.ReadByte(Translate(address, accessKind, write: false, byteCount: 1), ref cycle, accessKind);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
            => _physicalBus.ReadWord(Translate(address, accessKind, write: false, byteCount: 2), ref cycle, accessKind);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind)
            => _physicalBus.ReadLong(Translate(address, accessKind, write: false, byteCount: 4), ref cycle, accessKind);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
            => _physicalBus.WriteByte(Translate(address, accessKind, write: true, byteCount: 1), value, ref cycle, accessKind);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
            => _physicalBus.WriteWord(Translate(address, accessKind, write: true, byteCount: 2), value, ref cycle, accessKind);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
            => _physicalBus.WriteLong(Translate(address, accessKind, write: true, byteCount: 4), value, ref cycle, accessKind);

        public bool HasHostTrapStub(uint address)
        {
            try
            {
                return _physicalBus.HasHostTrapStub(
                    Translate(address, M68kBusAccessKind.CpuInstructionFetch, write: false, byteCount: 2));
            }
            catch (M68040MmuFaultException)
            {
                return false;
            }
        }

        public bool TryInvokeHostTrap(uint instructionProgramCounter, ushort trapId, M68kCpuState state)
        {
            var physicalPc = Translate(
                instructionProgramCounter,
                M68kBusAccessKind.CpuInstructionFetch,
                write: false,
                byteCount: 2);
            return _physicalBus.TryInvokeHostTrap(physicalPc, trapId, state);
        }

        public void ResetExternalDevices(long cycle)
            => _physicalBus.ResetExternalDevices(cycle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort ReadHostWord(uint address)
        {
            var physical = Translate(address, M68kBusAccessKind.CpuInstructionFetch, write: false, byteCount: 2);
            if (_codeReader != null)
            {
                return _codeReader.ReadHostWord(physical);
            }

            var cycle = _state.Cycles;
            return _physicalBus.ReadWord(physical, ref cycle, M68kBusAccessKind.CpuInstructionFetch);
        }

        public bool TryReadFastByte(uint address, M68kBusAccessKind accessKind, out byte value)
        {
            var physical = Translate(address, accessKind, write: false, byteCount: 1);
            if (_fastMemoryBus is not null && CanFastReadPhysical(physical))
            {
                return _fastMemoryBus.TryReadFastByte(physical, accessKind, out value);
            }

            value = 0;
            return false;
        }

        public bool TryReadFastWord(uint address, M68kBusAccessKind accessKind, out ushort value)
        {
            var physical = Translate(address, accessKind, write: false, byteCount: 2);
            if (_fastMemoryBus is not null && CanFastReadPhysical(physical))
            {
                return _fastMemoryBus.TryReadFastWord(physical, accessKind, out value);
            }

            value = 0;
            return false;
        }

        public bool TryReadFastLong(uint address, M68kBusAccessKind accessKind, out uint value)
        {
            var physical = Translate(address, accessKind, write: false, byteCount: 4);
            if (_fastMemoryBus is not null && CanFastReadPhysical(physical))
            {
                return _fastMemoryBus.TryReadFastLong(physical, accessKind, out value);
            }

            value = 0;
            return false;
        }

        public bool TryWriteFastByte(uint address, byte value, M68kBusAccessKind accessKind)
        {
            var physical = Translate(address, accessKind, write: true, byteCount: 1);
            if (_fastMemoryBus is null || !CanFastWritePhysical(physical))
            {
                return false;
            }

            return _fastMemoryBus.TryWriteFastByte(physical, value, accessKind);
        }

        public bool TryWriteFastWord(uint address, ushort value, M68kBusAccessKind accessKind)
        {
            var physical = Translate(address, accessKind, write: true, byteCount: 2);
            if (_fastMemoryBus is null || !CanFastWritePhysical(physical))
            {
                return false;
            }

            return _fastMemoryBus.TryWriteFastWord(physical, value, accessKind);
        }

        public bool TryWriteFastLong(uint address, uint value, M68kBusAccessKind accessKind)
        {
            var physical = Translate(address, accessKind, write: true, byteCount: 4);
            if (_fastMemoryBus is null || !CanFastWritePhysical(physical))
            {
                return false;
            }

            return _fastMemoryBus.TryWriteFastLong(physical, value, accessKind);
        }

        private static bool CanFastReadPhysical(uint physical)
            => (physical & 0x00FF_FFFFu) == 0x00BF_E001u ||
                M68020CpuProfile.ClassifyTarget(physical) is
                M68020MemoryTarget.ExpansionRam or
                M68020MemoryTarget.RealFastRam or
                M68020MemoryTarget.Rom;

        private static bool CanFastWritePhysical(uint physical)
            => (physical & 0x00FF_FFFFu) == 0x00BF_E001u ||
                M68020CpuProfile.ClassifyTarget(physical) is
                M68020MemoryTarget.ExpansionRam or
                M68020MemoryTarget.RealFastRam;

        [HotPath]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint Translate(uint address, M68kBusAccessKind accessKind, bool write, int byteCount)
        {
            var mmu = _mmu;
            if (mmu.BypassTranslation)
            {
                return _allPhysicalAddressesMapped
                    ? address
                    : AcceptPhysicalAddress(address, byteCount, accessKind, write);
            }

            if (!mmu.Enabled)
            {
                if (mmu.Status != 0)
                {
                    mmu.Status = 0;
                }

                return _allPhysicalAddressesMapped
                    ? address
                    : AcceptPhysicalAddress(address, byteCount, accessKind, write);
            }

            return TranslateEnabled(address, accessKind, write, byteCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint AcceptPhysicalAddress(
            uint address,
            int byteCount,
            M68kBusAccessKind accessKind,
            bool write)
        {
            if (IsPhysicalAddressMapped(address, byteCount, accessKind))
            {
                return address;
            }

            return ThrowPhysicalAddressFault(address, accessKind, write);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private uint TranslateEnabled(uint address, M68kBusAccessKind accessKind, bool write, int byteCount)
        {
            var supervisor = (_state.StatusRegister & M68kCpuState.Supervisor) != 0;
            if (_mmu.TryTranslate(
                address,
                accessKind,
                write,
                supervisor,
                _readPhysicalLong,
                out var physical,
                out var fault))
            {
                if (!IsPhysicalAddressMapped(physical, byteCount, accessKind))
                {
                    throw new M68040MmuFaultException(
                        WithStackedProgramCounter(
                            CreatePhysicalAddressFault(address, accessKind, write, supervisor),
                            address,
                            accessKind));
                }

                return physical;
            }

            throw new M68040MmuFaultException(
                WithStackedProgramCounter(fault, address, accessKind));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private uint ThrowPhysicalAddressFault(
            uint address,
            M68kBusAccessKind accessKind,
            bool write)
        {
            var supervisor = (_state.StatusRegister & M68kCpuState.Supervisor) != 0;
            throw new M68040MmuFaultException(
                WithStackedProgramCounter(
                    CreatePhysicalAddressFault(address, accessKind, write, supervisor),
                    address,
                    accessKind));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsPhysicalAddressMapped(
            uint physicalAddress,
            int byteCount,
            M68kBusAccessKind accessKind)
        {
            var fixedRange = accessKind switch
            {
                M68kBusAccessKind.CpuInstructionFetch => _instructionMapRange,
                M68kBusAccessKind.CpuDataRead => _dataReadMapRange,
                M68kBusAccessKind.CpuDataWrite => _dataWriteMapRange,
                _ => default
            };
            if (fixedRange.Contains(physicalAddress, byteCount))
            {
                return true;
            }

            var stableMap = _stablePhysicalAddressMap;
            if (stableMap is null ||
                (physicalAddress & ~PhysicalMapPageMask) > PhysicalMapPageSize - byteCount)
            {
                return _physicalAddressMap == null ||
                    _physicalAddressMap.IsCpuPhysicalAddressMapped(
                        physicalAddress,
                        byteCount,
                        accessKind);
            }

            return accessKind switch
            {
                M68kBusAccessKind.CpuInstructionFetch => IsPhysicalAddressMappedCached(
                    stableMap,
                    physicalAddress,
                    byteCount,
                    accessKind,
                    ref _instructionMapCache),
                M68kBusAccessKind.CpuDataRead => IsPhysicalAddressMappedCached(
                    stableMap,
                    physicalAddress,
                    byteCount,
                    accessKind,
                    ref _dataReadMapCache),
                M68kBusAccessKind.CpuDataWrite => IsPhysicalAddressMappedCached(
                    stableMap,
                    physicalAddress,
                    byteCount,
                    accessKind,
                    ref _dataWriteMapCache),
                _ => stableMap.IsCpuPhysicalAddressMapped(physicalAddress, byteCount, accessKind)
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsPhysicalAddressMappedCached(
            IM68kStablePhysicalAddressMap stableMap,
            uint physicalAddress,
            int byteCount,
            M68kBusAccessKind accessKind,
            ref PhysicalMapPageCache cache)
        {
            var page = physicalAddress & PhysicalMapPageMask;
            var generation = stableMap.CpuPhysicalAddressMapGeneration;
            if (!cache.Initialized || cache.Page != page || cache.Generation != generation)
            {
                cache.Page = page;
                cache.Generation = generation;
                cache.FullyMapped = stableMap.IsCpuPhysicalAddressMapped(
                    page,
                    PhysicalMapPageSize,
                    accessKind);
                cache.Initialized = true;
            }

            return cache.FullyMapped ||
                stableMap.IsCpuPhysicalAddressMapped(physicalAddress, byteCount, accessKind);
        }

        private struct PhysicalMapPageCache
        {
            public uint Page;
            public uint Generation;
            public bool FullyMapped;
            public bool Initialized;
        }

        private readonly record struct PhysicalMapRange(uint StartAddress, uint EndAddress, bool Valid)
        {
            public bool CoversAllAddresses
                => Valid && StartAddress == 0 && EndAddress == uint.MaxValue;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Contains(uint address, int byteCount)
                => Valid &&
                    byteCount > 0 &&
                    address >= StartAddress &&
                    address <= EndAddress &&
                    (uint)(byteCount - 1) <= EndAddress - address;
        }

        private static PhysicalMapRange GetFixedPhysicalMapRange(
            IM68kFixedPhysicalAddressMap fixedMap,
            M68kBusAccessKind accessKind)
            => fixedMap.TryGetCpuPhysicalAddressMappedRange(
                accessKind,
                out var startAddress,
                out var endAddress)
                ? new PhysicalMapRange(startAddress, endAddress, Valid: true)
                : default;

        private static M68040MmuFault CreatePhysicalAddressFault(
            uint logicalAddress,
            M68kBusAccessKind accessKind,
            bool write,
            bool supervisor)
        {
            var status = 0x0000_0010u |
                (write ? 0x0000_0100u : 0) |
                (supervisor ? 0x0000_0200u : 0);
            return new M68040MmuFault(logicalAddress, accessKind, write, status);
        }

        private M68040MmuFault WithStackedProgramCounter(
            M68040MmuFault fault,
            uint logicalAddress,
            M68kBusAccessKind accessKind)
        {
            var stackedProgramCounter = accessKind == M68kBusAccessKind.CpuInstructionFetch
                ? logicalAddress
                : _state.LastInstructionProgramCounter;
            return fault with { StackedProgramCounter = stackedProgramCounter };
        }

        private uint ReadPhysicalLong(uint physicalAddress)
        {
            var bypass = _state.M68040Mmu.BypassTranslation;
            _state.M68040Mmu.BypassTranslation = true;
            try
            {
                if (!IsPhysicalAddressMapped(physicalAddress, 4, M68kBusAccessKind.CpuDataRead))
                {
                    throw new M68kEmulationException(
                        $"MC68040 MMU table read from unmapped physical address 0x{physicalAddress:X8}.");
                }

                var cycle = _state.Cycles;
                return _physicalBus.ReadLong(physicalAddress, ref cycle, M68kBusAccessKind.CpuDataRead);
            }
            finally
            {
                _state.M68040Mmu.BypassTranslation = bypass;
            }
        }
    }

    internal readonly struct M68040ApproximateFallbackCheckpoint
    {
        private readonly uint _d0;
        private readonly uint _d1;
        private readonly uint _d2;
        private readonly uint _d3;
        private readonly uint _d4;
        private readonly uint _d5;
        private readonly uint _d6;
        private readonly uint _d7;
        private readonly uint _a0;
        private readonly uint _a1;
        private readonly uint _a2;
        private readonly uint _a3;
        private readonly uint _a4;
        private readonly uint _a5;
        private readonly uint _a6;
        private readonly uint _a7;
        private readonly uint _programCounter;
        private readonly ushort _statusRegister;
        private readonly uint _userStackPointer;
        private readonly uint _supervisorStackPointer;
        private readonly uint _masterStackPointer;
        private readonly uint _vectorBaseRegister;
        private readonly uint _sourceFunctionCode;
        private readonly uint _destinationFunctionCode;
        private readonly uint _cacheControlRegister;
        private readonly uint _cacheAddressRegister;
        private readonly long _cycles;
        private readonly long _nativeCycles;
        private readonly bool _halted;
        private readonly bool _stopped;
        private readonly ushort _lastOpcode;
        private readonly uint _lastInstructionProgramCounter;

        private M68040ApproximateFallbackCheckpoint(M68kCpuState state)
        {
            _d0 = state.D[0];
            _d1 = state.D[1];
            _d2 = state.D[2];
            _d3 = state.D[3];
            _d4 = state.D[4];
            _d5 = state.D[5];
            _d6 = state.D[6];
            _d7 = state.D[7];
            _a0 = state.A[0];
            _a1 = state.A[1];
            _a2 = state.A[2];
            _a3 = state.A[3];
            _a4 = state.A[4];
            _a5 = state.A[5];
            _a6 = state.A[6];
            _a7 = state.A[7];
            _programCounter = state.ProgramCounter;
            _statusRegister = state.StatusRegister;
            _userStackPointer = state.UserStackPointer;
            _supervisorStackPointer = state.SupervisorStackPointer;
            _masterStackPointer = state.MasterStackPointer;
            _vectorBaseRegister = state.VectorBaseRegister;
            _sourceFunctionCode = state.SourceFunctionCode;
            _destinationFunctionCode = state.DestinationFunctionCode;
            _cacheControlRegister = state.CacheControlRegister;
            _cacheAddressRegister = state.CacheAddressRegister;
            _cycles = state.Cycles;
            _nativeCycles = state.NativeCycles;
            _halted = state.Halted;
            _stopped = state.Stopped;
            _lastOpcode = state.LastOpcode;
            _lastInstructionProgramCounter = state.LastInstructionProgramCounter;
        }

        public static M68040ApproximateFallbackCheckpoint Capture(M68kCpuState state)
            => new(state);

        public void Restore(M68kCpuState state)
        {
            state.D[0] = _d0;
            state.D[1] = _d1;
            state.D[2] = _d2;
            state.D[3] = _d3;
            state.D[4] = _d4;
            state.D[5] = _d5;
            state.D[6] = _d6;
            state.D[7] = _d7;
            state.A[0] = _a0;
            state.A[1] = _a1;
            state.A[2] = _a2;
            state.A[3] = _a3;
            state.A[4] = _a4;
            state.A[5] = _a5;
            state.A[6] = _a6;
            state.ResetStackPointers(
                _supervisorStackPointer,
                _userStackPointer,
                (_statusRegister & M68kCpuState.Supervisor) != 0);
            state.EnableM68020StackMode();
            state.SetMasterStackPointer(_masterStackPointer);
            state.SetInterruptStackPointer(_supervisorStackPointer);
            state.StatusRegister = _statusRegister;
            state.SetActiveStackPointer(_a7);
            state.ProgramCounter = _programCounter;
            state.VectorBaseRegister = _vectorBaseRegister;
            state.SourceFunctionCode = _sourceFunctionCode;
            state.DestinationFunctionCode = _destinationFunctionCode;
            state.CacheControlRegister = _cacheControlRegister;
            state.CacheAddressRegister = _cacheAddressRegister;
            state.Cycles = _cycles;
            state.NativeCycles = _nativeCycles;
            state.Halted = _halted;
            state.Stopped = _stopped;
            state.LastOpcode = _lastOpcode;
            state.LastInstructionProgramCounter = _lastInstructionProgramCounter;
        }
    }

    internal sealed class M68040Interpreter : M68kAdvancedTimingInterpreter
    {
        private const int VectorBusError = 2;
        private const int VectorLineF = 11;
        private readonly IM68kBus _physicalBus;
        private readonly M68kInterpreter _approximateIntegerFallback;

        public M68040Interpreter(IM68kBus bus)
            : this(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz)
        {
        }

        internal M68040Interpreter(IM68kBus bus, M68020CpuProfile profile)
            : this(bus, profile, new M68kCpuState())
        {
        }

        internal M68040Interpreter(
            IM68kBus bus,
            M68020CpuProfile profile,
            M68kCpuState state,
            M68kInstructionFrequencyMatrix? instructionFrequency = null,
            bool enableAdvancedFastPath = true)
            : base(
                new M68040LogicalBus(bus, state),
                profile,
                state,
                instructionFrequency,
                opcodeKinds: M68020OpcodeDispatchTable.M68040Kinds,
                hasModelSpecificInstructions: true,
                enableAdvancedFastPath: enableAdvancedFastPath)
        {
            _physicalBus = bus ?? throw new ArgumentNullException(nameof(bus));
            if (profile.Model != M68kAcceleratorModel.M68040)
            {
                throw new ArgumentException("The MC68040 interpreter requires an MC68040 CPU profile.", nameof(profile));
            }

            _approximateIntegerFallback = new M68kInterpreter(
                _bus,
                State,
                _instructionFrequency,
                enableOpcodePlan: false);
        }

        public override int ExecuteInstruction()
        {
            var startCycles = State.Cycles;
            try
            {
                return base.ExecuteInstruction();
            }
            catch (M68040MmuFaultException ex)
            {
                RaiseMmuFault(ex.Fault);
                return (int)(State.Cycles - startCycles);
            }
            catch (UnsupportedM68kTimingException ex)
            {
                throw new UnsupportedM68040InstructionException(ex.Opcode, ex.ProgramCounter, _profile.Name, ex);
            }
        }

        public override void Reset(uint programCounter, uint stackPointer)
        {
            base.Reset(programCounter, stackPointer);
            State.M68040Fpu.Reset();
            State.M68040Mmu.Reset();
        }

        protected override bool TryExecuteFastModelSpecificInstruction(ushort opcode)
        {
            if (opcode != 0xF200)
            {
                return false;
            }

            BeginInstruction(opcode);
            ConsumeFastOpcode(opcode);
            var extension = FetchWord();
            return ExecuteFpuCommand(opcode, extension, useFastTiming: true);
        }

        protected override bool TryExecuteModelSpecificInstruction(ushort opcode)
        {
            if ((opcode & 0xFF00) == 0x0E00 && (opcode & 0x00C0) != 0x00C0)
            {
                if ((State.StatusRegister & M68kCpuState.Supervisor) != 0)
                {
                    return false;
                }

                BeginInstruction(opcode);
                var instructionPc = State.ProgramCounter;
                _ = FetchWord();
                RaiseFormat0Exception(8, instructionPc, M68kInstructionTimingKey.PrivilegeViolation);
                return true;
            }

            if ((opcode & 0xF000) != 0xF000)
            {
                return false;
            }

            if (TryExecuteMove16(opcode))
            {
                return true;
            }

            if (TryExecuteMmuInstruction(opcode))
            {
                return true;
            }

            return TryExecuteFpuInstruction(opcode);
        }

        protected override bool TryExecuteApproximateInstruction(ushort opcode)
        {
            if ((opcode & 0xF000) is 0xA000 or 0xF000 || opcode == 0x4AFC)
            {
                return false;
            }

            var checkpoint = M68040ApproximateFallbackCheckpoint.Capture(State);
            var startCycles = State.Cycles;
            var startNativeCycles = State.NativeCycles;
            try
            {
                _approximateIntegerFallback.ExecuteInstruction();
                if (_profile.FixedInstructionNativeCycles is int fixedCycles)
                {
                    State.Cycles = startCycles;
                    State.NativeCycles = startNativeCycles;
                    _timing.CompleteInstruction(M68kInstructionPlan.CreateFlat(
                        M68kInstructionTimingKey.Nop,
                        "fixed JIT fallback",
                        fixedCycles));
                }
                else
                {
                    _timing.SynchronizeNativeToMachine();
                }

                State.EnableM68020StackMode();
                return true;
            }
            catch (UnsupportedM68kOpcodeException)
            {
                checkpoint.Restore(State);
                return false;
            }
        }

        protected override bool TryReadControlRegister(int register, uint instructionPc, out uint value)
        {
            switch (register)
            {
                case 0x003:
                    value = State.M68040Mmu.TranslationControl;
                    return true;
                case 0x004:
                    value = State.M68040Mmu.InstructionTransparentTranslation0;
                    return true;
                case 0x005:
                    value = State.M68040Mmu.InstructionTransparentTranslation1;
                    return true;
                case 0x006:
                    value = State.M68040Mmu.DataTransparentTranslation0;
                    return true;
                case 0x007:
                    value = State.M68040Mmu.DataTransparentTranslation1;
                    return true;
                case 0x805:
                    value = State.M68040Mmu.Status;
                    return true;
                case 0x806:
                    value = State.M68040Mmu.UserRootPointer;
                    return true;
                case 0x807:
                    value = State.M68040Mmu.SupervisorRootPointer;
                    return true;
                case 0x808:
                    value = RaiseLineFControlRegister(instructionPc);
                    return false;
                default:
                    return base.TryReadControlRegister(register, instructionPc, out value);
            }
        }

        protected override bool TryWriteControlRegister(int register, uint value, uint instructionPc)
        {
            switch (register)
            {
                case 0x003:
                    State.M68040Mmu.TranslationControl = value;
                    State.M68040Mmu.Flush();
                    return true;
                case 0x004:
                    State.M68040Mmu.InstructionTransparentTranslation0 = value;
                    State.M68040Mmu.Flush();
                    return true;
                case 0x005:
                    State.M68040Mmu.InstructionTransparentTranslation1 = value;
                    State.M68040Mmu.Flush();
                    return true;
                case 0x006:
                    State.M68040Mmu.DataTransparentTranslation0 = value;
                    State.M68040Mmu.Flush();
                    return true;
                case 0x007:
                    State.M68040Mmu.DataTransparentTranslation1 = value;
                    State.M68040Mmu.Flush();
                    return true;
                case 0x805:
                    State.M68040Mmu.Status = value;
                    return true;
                case 0x806:
                    State.M68040Mmu.UserRootPointer = value;
                    State.M68040Mmu.Flush();
                    return true;
                case 0x807:
                    State.M68040Mmu.SupervisorRootPointer = value;
                    State.M68040Mmu.Flush();
                    return true;
                case 0x808:
                    _ = RaiseLineFControlRegister(instructionPc);
                    return false;
                default:
                    return base.TryWriteControlRegister(register, value, instructionPc);
            }
        }

        private uint RaiseLineFControlRegister(uint instructionPc)
        {
            RaiseFormat0Exception(VectorLineF, instructionPc, M68kInstructionTimingKey.LineFException);
            return 0;
        }

        private bool TryExecuteMove16(ushort opcode)
        {
            if ((opcode & 0xFFF8) != 0xF620)
            {
                return false;
            }

            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = opcode & 7;
            var extension = FetchWord();
            var destinationRegister = (extension >> 12) & 7;
            var source = State.A[sourceRegister] & 0xFFFF_FFF0u;
            var destination = State.A[destinationRegister] & 0xFFFF_FFF0u;
            for (var offset = 0u; offset < 16; offset += 4)
            {
                WriteLong(destination + offset, ReadLong(source + offset));
            }

            State.A[sourceRegister] += 16;
            if (destinationRegister != sourceRegister)
            {
                State.A[destinationRegister] += 16;
            }
            CompleteTiming(M68kInstructionTimingKey.Movec);
            return true;
        }

        private bool TryExecuteMmuInstruction(ushort opcode)
        {
            if ((opcode & 0xFFC0) == 0xF500)
            {
                BeginInstruction(opcode);
                _ = FetchWord();
                _ = FetchWord();
                State.M68040Mmu.Flush();
                CompleteTiming(M68kInstructionTimingKey.Movec);
                return true;
            }

            if ((opcode & 0xFFC0) == 0xF548)
            {
                BeginInstruction(opcode);
                _ = FetchWord();
                var extension = FetchWord();
                var write = (extension & 0x0200) != 0;
                var address = State.A[opcode & 7];
                State.M68040Mmu.Probe(
                    address,
                    M68kBusAccessKind.CpuDataRead,
                    write,
                    (State.StatusRegister & M68kCpuState.Supervisor) != 0,
                    ReadPhysicalLong);
                CompleteTiming(M68kInstructionTimingKey.Movec);
                return true;
            }

            if ((opcode & 0xFF20) is 0xF400 or 0xF420 &&
                ((opcode >> 6) & 3) != 0 &&
                ((opcode >> 3) & 3) != 0)
            {
                BeginInstruction(opcode);
                _ = FetchWord();
                State.M68040Mmu.Flush();
                _timing.Reset();
                CompleteTiming(M68kInstructionTimingKey.Movec);
                return true;
            }

            return false;
        }

        private bool TryExecuteFpuInstruction(ushort opcode)
        {
            if ((opcode & 0xFFC0) == 0xF280)
            {
                ExecuteFbcc(opcode, longDisplacement: false);
                return true;
            }

            if ((opcode & 0xFFC0) == 0xF2C0)
            {
                ExecuteFbcc(opcode, longDisplacement: true);
                return true;
            }

            if ((opcode & 0xFFC0) == 0xF240)
            {
                ExecuteFpuConditional(opcode);
                return true;
            }

            if ((opcode & 0xFFC0) == 0xF300)
            {
                BeginInstruction(opcode);
                _ = FetchWord();
                if (!ExecuteFsave(opcode))
                {
                    CompleteTiming(M68kInstructionTimingKey.FpuSave);
                }

                return true;
            }

            if ((opcode & 0xFFC0) == 0xF340)
            {
                BeginInstruction(opcode);
                _ = FetchWord();
                if (!ExecuteFrestore(opcode))
                {
                    CompleteTiming(M68kInstructionTimingKey.FpuRestore);
                }

                return true;
            }

            if ((opcode & 0xFFC0) != 0xF200)
            {
                return false;
            }

            BeginInstruction(opcode);
            _ = FetchWord();
            var extension = FetchWord();
            return ExecuteFpuCommand(opcode, extension, useFastTiming: false);
        }

        private bool ExecuteFpuCommand(ushort opcode, ushort extension, bool useFastTiming)
        {
            if (M68040FpuHelpers.IsRegisterOperationCommand(extension))
            {
                return ExecuteFpuRegisterCommand(extension, useFastTiming);
            }

            if ((extension & 0xE000) == 0xE000)
            {
                if (!ExecuteFmovem(opcode, extension, registersToMemory: true))
                {
                    CompleteTiming(M68kInstructionTimingKey.FpuMovem);
                }

                return true;
            }

            if ((extension & 0xE000) == 0xC000)
            {
                if (!ExecuteFmovem(opcode, extension, registersToMemory: false))
                {
                    CompleteTiming(M68kInstructionTimingKey.FpuMovem);
                }

                return true;
            }

            if ((extension & 0xE000) == 0xA000)
            {
                if (!ExecuteFmoveControl(opcode, extension, toControl: false))
                {
                    CompleteTiming(M68kInstructionTimingKey.FpuControlMove);
                }

                return true;
            }

            if ((extension & 0xE000) == 0x8000)
            {
                if (!ExecuteFmoveControl(opcode, extension, toControl: true))
                {
                    CompleteTiming(M68kInstructionTimingKey.FpuControlMove);
                }

                return true;
            }

            if ((extension & 0x6000) == 0x6000)
            {
                var format = (extension >> 10) & 7;
                if (!IsValidFpuMoveDestinationEa(opcode, format))
                {
                    RaiseFormat0Exception(VectorLineF, State.LastInstructionProgramCounter, M68kInstructionTimingKey.LineFException);
                    return true;
                }

                if (!ExecuteFmoveToEa(opcode, extension))
                {
                    CompleteTiming(M68kInstructionTimingKey.FpuMoveOut);
                }

                return true;
            }

            if ((extension & 0xE000) is not (0x0000 or 0x4000))
            {
                RaiseFormat0Exception(VectorLineF, State.LastInstructionProgramCounter, M68kInstructionTimingKey.LineFException);
                return true;
            }

            var destination = (extension >> 7) & 7;
            var opmode = extension & 0x007F;
            var sourceFormat = (extension >> 10) & 7;
            if ((extension & 0xFC00) != 0x5C00 &&
                (extension & 0x4000) != 0 &&
                !IsValidFpuSourceEa(opcode, sourceFormat))
            {
                RaiseFormat0Exception(VectorLineF, State.LastInstructionProgramCounter, M68kInstructionTimingKey.LineFException);
                return true;
            }

            var source = (extension & 0xFC00) == 0x5C00
                ? new M68040FpuOperand(ExtF80.PositiveZero, FloatingPointExceptionFlags.None)
                : ((extension & 0x4000) == 0)
                    ? new M68040FpuOperand(
                        State.M68040Fpu.FP[(extension >> 10) & 7],
                        FloatingPointExceptionFlags.None)
                    : ReadFpuEa(opcode, (extension >> 10) & 7);
            var previousDestination = State.M68040Fpu.FP[destination];
            if ((extension & 0xFC00) == 0x5C00 || M68040FpuHelpers.IsRecognizedUnimplementedOperation(opmode))
            {
                State.M68040Fpu.Fpiar = State.LastInstructionProgramCounter;
                State.M68040Fpu.PrepareUnimplementedInstruction(
                    extension,
                    source.EffectiveAddress,
                    source.Value,
                    previousDestination);
                RaiseFpuFormat2Exception(VectorLineF, State.ProgramCounter, source.EffectiveAddress);
                return true;
            }

            var execution = M68040FpuHelpers.ApplyOperation(
                State.M68040Fpu,
                destination,
                opmode,
                source.Value,
                source.Flags,
                source.Unsupported);
            if (!execution.Supported)
            {
                RaiseFormat0Exception(VectorLineF, State.LastInstructionProgramCounter, M68kInstructionTimingKey.LineFException);
                return true;
            }

            State.M68040Fpu.Fpiar = State.LastInstructionProgramCounter;

            if (execution.ExceptionVector != 0)
            {
                if (execution.ExceptionStage == M68040FpuExceptionStage.PreInstruction)
                {
                    State.M68040Fpu.PrepareUnsupportedData(
                        extension,
                        source.EffectiveAddress,
                        source.Value,
                        previousDestination,
                        postInstruction: false);
                    RaiseFormat0Exception(
                        execution.ExceptionVector,
                        State.LastInstructionProgramCounter,
                        M68kInstructionTimingKey.LineFException);
                    return true;
                }

                State.M68040Fpu.PrepareArithmeticException(
                    execution.ExceptionVector,
                    extension,
                    source.EffectiveAddress,
                    source.Value,
                    previousDestination);
                RaiseFpuFormat3Exception(
                    execution.ExceptionVector,
                    State.ProgramCounter,
                    source.EffectiveAddress);
                return true;
            }

            if (useFastTiming)
            {
                CompleteFastTiming(M68040FpuHelpers.OperationTimingKey(opmode));
            }
            else
            {
                CompleteTiming(M68040FpuHelpers.OperationTimingKey(opmode));
            }

            return true;
        }

        private bool ExecuteFpuRegisterCommand(ushort extension, bool useFastTiming)
        {
            var destination = (extension >> 7) & 7;
            var source = State.M68040Fpu.FP[(extension >> 10) & 7];
            var previousDestination = State.M68040Fpu.FP[destination];
            var opmode = extension & 0x007F;
            var execution = M68040FpuHelpers.ApplyRegisterOperation(
                State.M68040Fpu,
                destination,
                opmode,
                source);
            State.M68040Fpu.Fpiar = State.LastInstructionProgramCounter;
            if (execution.ExceptionVector != 0)
            {
                if (execution.ExceptionStage == M68040FpuExceptionStage.PreInstruction)
                {
                    State.M68040Fpu.PrepareUnsupportedData(
                        extension,
                        0,
                        source,
                        previousDestination,
                        postInstruction: false);
                    RaiseFormat0Exception(
                        execution.ExceptionVector,
                        State.LastInstructionProgramCounter,
                        M68kInstructionTimingKey.LineFException);
                    return true;
                }

                State.M68040Fpu.PrepareArithmeticException(
                    execution.ExceptionVector,
                    extension,
                    0,
                    source,
                    previousDestination);
                RaiseFpuFormat3Exception(
                    execution.ExceptionVector,
                    State.ProgramCounter,
                    0);
                return true;
            }

            var timingKey = M68040FpuHelpers.OperationTimingKey(opmode);
            if (useFastTiming)
            {
                CompleteFastTiming(timingKey);
            }
            else
            {
                CompleteTiming(timingKey);
            }

            return true;
        }

        private static bool IsValidFpuSourceEa(ushort opcode, int format)
        {
            var mode = (opcode >> 3) & 7;
            var register = opcode & 7;
            if (mode == 0)
            {
                return M68040FpuHelpers.IsSupportedDataRegisterFormat(format);
            }

            return mode is >= 2 and <= 6 ||
                (mode == 7 && register <= 4);
        }

        private static bool IsValidFpuMoveDestinationEa(ushort opcode, int format)
        {
            var mode = (opcode >> 3) & 7;
            var register = opcode & 7;
            if (mode == 0)
            {
                return M68040FpuHelpers.IsSupportedDataRegisterFormat(format);
            }

            return mode is >= 2 and <= 6 ||
                (mode == 7 && register <= 1);
        }

        private void ExecuteFbcc(ushort opcode, bool longDisplacement)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var branchBase = State.ProgramCounter;
            var displacement = longDisplacement
                ? unchecked((int)FetchLong())
                : unchecked((int)(short)FetchWord());
            var condition = opcode & 0x3F;
            if ((condition & 0x20) != 0)
            {
                RaiseFormat0Exception(
                    VectorLineF,
                    State.LastInstructionProgramCounter,
                    M68kInstructionTimingKey.LineFException);
                return;
            }

            var takeBranch = State.M68040Fpu.EvaluateCondition(condition, out var exceptionVector);
            if (exceptionVector != 0)
            {
                RaiseFpuFormat3Exception(exceptionVector, State.ProgramCounter, 0);
                return;
            }

            if (takeBranch)
            {
                State.ProgramCounter = unchecked((uint)(branchBase + displacement));
                CompleteTiming(longDisplacement
                    ? M68kInstructionTimingKey.BranchLongTaken
                    : M68kInstructionTimingKey.BranchWordTaken);
                return;
            }

            CompleteTiming(longDisplacement
                ? M68kInstructionTimingKey.BranchLongNotTaken
                : M68kInstructionTimingKey.BranchWordNotTaken);
        }

        private void ExecuteFpuConditional(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var mode = (opcode >> 3) & 7;
            var register = opcode & 7;
            var extension = FetchWord();
            if ((extension & 0x0020) != 0)
            {
                RaiseFormat0Exception(
                    VectorLineF,
                    State.LastInstructionProgramCounter,
                    M68kInstructionTimingKey.LineFException);
                return;
            }

            var condition = extension & 0x1F;
            if (mode == 1)
            {
                ExecuteFdbcc(opcode, register, condition);
                return;
            }

            if (mode == 7 && register is >= 2 and <= 4)
            {
                ExecuteFtrapcc(register, condition);
                return;
            }

            ExecuteFscc(opcode, mode, register, condition);
        }

        private void ExecuteFdbcc(ushort opcode, int register, int condition)
        {
            var branchBase = State.ProgramCounter;
            var displacement = unchecked((int)(short)FetchWord());
            var conditionTrue = State.M68040Fpu.EvaluateCondition(condition, out var exceptionVector);
            if (exceptionVector != 0)
            {
                RaiseFpuFormat3Exception(exceptionVector, State.ProgramCounter, 0);
                return;
            }

            if (conditionTrue)
            {
                CompleteTiming(M68kInstructionTimingKey.DbccConditionTrue);
                return;
            }

            var counter = unchecked((ushort)(State.D[register] - 1));
            State.D[register] = (State.D[register] & 0xFFFF_0000u) | counter;
            if (counter != 0xFFFF)
            {
                State.ProgramCounter = unchecked((uint)(branchBase + displacement));
                CompleteTiming(M68kInstructionTimingKey.DbccBranchTaken);
                return;
            }

            CompleteTiming(M68kInstructionTimingKey.DbccExpired);
        }

        private void ExecuteFtrapcc(int register, int condition)
        {
            if (register == 2)
            {
                _ = FetchWord();
            }
            else if (register == 3)
            {
                _ = FetchLong();
            }

            var trap = State.M68040Fpu.EvaluateCondition(condition, out var exceptionVector);
            if (exceptionVector != 0)
            {
                RaiseFpuFormat3Exception(exceptionVector, State.ProgramCounter, 0);
                return;
            }

            if (trap)
            {
                RaiseFormat0Exception(7, State.ProgramCounter, M68kInstructionTimingKey.IllegalInstruction);
                return;
            }

            CompleteTiming(M68kInstructionTimingKey.Nop);
        }

        private void ExecuteFscc(ushort opcode, int mode, int register, int condition)
        {
            var set = State.M68040Fpu.EvaluateCondition(condition, out var exceptionVector);
            if (exceptionVector != 0)
            {
                RaiseFpuFormat3Exception(exceptionVector, State.ProgramCounter, 0);
                return;
            }

            var value = set ? (byte)0xFF : (byte)0;
            if (mode == 0)
            {
                State.D[register] = (State.D[register] & 0xFFFF_FF00u) | value;
            }
            else
            {
                var address = GetFpuMemoryAddress(mode, register, 1, allowPcRelative: false);
                WriteByte(address, value);
            }

            CompleteTiming(M68kInstructionTimingKey.Nop);
        }

        private bool ExecuteFsave(ushort opcode)
        {
            var mode = (opcode >> 3) & 7;
            var register = opcode & 7;
            if ((State.StatusRegister & M68kCpuState.Supervisor) == 0)
            {
                RaiseFormat0Exception(8, State.LastInstructionProgramCounter, M68kInstructionTimingKey.PrivilegeViolation);
                return true;
            }

            if (!IsValidFpuStateFrameEa(mode, register, restore: false))
            {
                RaiseFormat0Exception(VectorLineF, State.LastInstructionProgramCounter, M68kInstructionTimingKey.LineFException);
                return true;
            }

            var stateFrame = M68040FpuHelpers.CurrentStateFrame(State.M68040Fpu);
            var header = (ushort)(stateFrame >> 16);
            var size = M68040FpuHelpers.StateFrameSize(header);
            var address = GetFpuStateFrameAddress(mode, register, size, restore: false);
            State.M68040Fpu.LastStateFrameAddress = address;
            State.M68040Fpu.LastStateFrameHeader = header;
            State.M68040Fpu.LastStateFrameSize = size;
            State.M68040Fpu.LastStateFrameRestore = false;
            for (var offset = 0u; offset < size; offset += 4)
            {
                WriteLong(address + offset, M68040FpuHelpers.GetStateFrameLong(State.M68040Fpu, offset));
            }

            if (mode == 3)
            {
                State.A[register] = address + size;
            }

            State.M68040Fpu.ConsumeSavedStateFrame();
            return false;
        }

        private bool ExecuteFrestore(ushort opcode)
        {
            var mode = (opcode >> 3) & 7;
            var register = opcode & 7;
            if ((State.StatusRegister & M68kCpuState.Supervisor) == 0)
            {
                RaiseFormat0Exception(8, State.LastInstructionProgramCounter, M68kInstructionTimingKey.PrivilegeViolation);
                return true;
            }

            if (!IsValidFpuStateFrameEa(mode, register, restore: true))
            {
                RaiseFormat0Exception(VectorLineF, State.LastInstructionProgramCounter, M68kInstructionTimingKey.LineFException);
                return true;
            }

            var address = GetFpuStateFrameAddress(
                mode,
                register,
                M68040FpuHelpers.IdleStateFrameSize,
                restore: true);
            var header = ReadWord(address);
            if (!M68040FpuHelpers.TryGetStateFrameKind(header, out var kind))
            {
                RaiseFormat0Exception(14, State.LastInstructionProgramCounter, M68kInstructionTimingKey.FormatError);
                return true;
            }

            var size = M68040FpuHelpers.StateFrameSize(header);
            if (kind == M68040FpuFrameKind.Null)
            {
                State.M68040Fpu.Reset();
            }
            else
            {
                State.M68040Fpu.RestoreStateFrame(kind);
                for (var offset = 4u; offset < size; offset += 4)
                {
                    M68040FpuHelpers.RestoreStateFrameLong(
                        State.M68040Fpu,
                        offset,
                        ReadLong(address + offset));
                }
            }

            State.M68040Fpu.LastStateFrameAddress = address;
            State.M68040Fpu.LastStateFrameHeader = header;
            State.M68040Fpu.LastStateFrameSize = size;
            State.M68040Fpu.LastStateFrameRestore = true;
            if (mode == 3)
            {
                State.A[register] = address + size;
            }

            return false;
        }

        private bool ExecuteFmoveControl(ushort opcode, ushort extension, bool toControl)
        {
            State.M68040Fpu.MarkInstructionExecuted();
            var mode = (opcode >> 3) & 7;
            var register = opcode & 7;
            var mask = extension & 0x1C00;
            if (mask == 0)
            {
                if (mode is 0 or 1)
                {
                    mask = 0x0400;
                }
                else
                {
                    if (!toControl && mode == 7 && register >= 2)
                    {
                        RaiseFormat0Exception(
                            VectorLineF,
                            State.LastInstructionProgramCounter,
                            M68kInstructionTimingKey.LineFException);
                        return true;
                    }

                    if (mode != 7 || register != 4)
                    {
                        ConsumeFpuMemoryAddressExtension(mode, register, allowPcRelative: toControl);
                    }

                    return false;
                }
            }

            if (mode is 0 or 1)
            {
                if (CountFpuControlMaskBits(mask) != 1 || (mode == 1 && mask != 0x0400))
                {
                    RaiseFormat0Exception(VectorLineF, State.LastInstructionProgramCounter, M68kInstructionTimingKey.LineFException);
                    return true;
                }

                var value = mode == 0 ? State.D[register] : State.A[register];
                if (toControl)
                {
                    SetFpuControlRegister(mask, value);
                }
                else
                {
                    value = GetFpuControlRegister(mask);
                    if (mode == 0)
                    {
                        State.D[register] = value;
                    }
                    else
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
                }

                return false;
            }

            if (mode == 7 && register == 4)
            {
                if (!toControl)
                {
                    RaiseFormat0Exception(VectorLineF, State.LastInstructionProgramCounter, M68kInstructionTimingKey.LineFException);
                    return true;
                }

                var fpcr = (mask & 0x1000) != 0 ? FetchLong() : 0;
                var fpsr = (mask & 0x0800) != 0 ? FetchLong() : 0;
                var fpiar = (mask & 0x0400) != 0 ? FetchLong() : 0;
                if ((mask & 0x1000) != 0)
                {
                    State.M68040Fpu.Fpcr = fpcr;
                }

                if ((mask & 0x0800) != 0)
                {
                    State.M68040Fpu.Fpsr = fpsr;
                }

                if ((mask & 0x0400) != 0)
                {
                    State.M68040Fpu.Fpiar = fpiar;
                }

                return false;
            }

            if (!toControl && mode == 7 && register >= 2)
            {
                RaiseFormat0Exception(
                    VectorLineF,
                    State.LastInstructionProgramCounter,
                    M68kInstructionTimingKey.LineFException);
                return true;
            }

            var byteSize = (uint)(CountFpuControlMaskBits(mask) * 4);
            var address = GetFpuMemoryAddress(
                mode,
                register,
                byteSize,
                allowPcRelative: toControl);
            if (toControl)
            {
                var fpcr = 0u;
                var fpsr = 0u;
                var fpiar = 0u;
                if ((mask & 0x1000) != 0)
                {
                    fpcr = ReadLong(address);
                    address += 4;
                }

                if ((mask & 0x0800) != 0)
                {
                    fpsr = ReadLong(address);
                    address += 4;
                }

                if ((mask & 0x0400) != 0)
                {
                    fpiar = ReadLong(address);
                }

                if ((mask & 0x1000) != 0) State.M68040Fpu.Fpcr = fpcr;
                if ((mask & 0x0800) != 0) State.M68040Fpu.Fpsr = fpsr;
                if ((mask & 0x0400) != 0) State.M68040Fpu.Fpiar = fpiar;
                return false;
            }

            if ((mask & 0x1000) != 0)
            {
                WriteLong(address, State.M68040Fpu.Fpcr);
                address += 4;
            }

            if ((mask & 0x0800) != 0)
            {
                WriteLong(address, State.M68040Fpu.Fpsr);
                address += 4;
            }

            if ((mask & 0x0400) != 0)
            {
                WriteLong(address, State.M68040Fpu.Fpiar);
            }

            return false;
        }

        private uint GetFpuControlRegister(int mask)
            => mask switch
            {
                0x1000 => State.M68040Fpu.Fpcr,
                0x0800 => State.M68040Fpu.Fpsr,
                0x0400 => State.M68040Fpu.Fpiar,
                _ => 0
            };

        private void SetFpuControlRegister(int mask, uint value)
        {
            switch (mask)
            {
                case 0x1000:
                    State.M68040Fpu.Fpcr = value;
                    break;
                case 0x0800:
                    State.M68040Fpu.Fpsr = value;
                    break;
                case 0x0400:
                    State.M68040Fpu.Fpiar = value;
                    break;
            }
        }

        private static int CountFpuControlMaskBits(int mask)
            => ((mask & 0x1000) != 0 ? 1 : 0) +
                ((mask & 0x0800) != 0 ? 1 : 0) +
                ((mask & 0x0400) != 0 ? 1 : 0);

        private bool ExecuteFmovem(ushort opcode, ushort extension, bool registersToMemory)
        {
            var mode = (opcode >> 3) & 7;
            var register = opcode & 7;
            if ((registersToMemory && (mode == 3 || (mode == 7 && register >= 2))) ||
                (!registersToMemory && (mode == 4 || (mode == 7 && register == 4))) ||
                mode is 0 or 1)
            {
                RaiseFormat0Exception(VectorLineF, State.LastInstructionProgramCounter, M68kInstructionTimingKey.LineFException);
                return true;
            }

            var maskMode = (extension >> 11) & 3;
            State.M68040Fpu.MarkInstructionExecuted();
            var mask = (byte)((maskMode & 1) == 0
                ? extension
                : State.D[(extension >> 4) & 3]);
            var byteSize = (uint)(CountFpuRegisterMaskBits(mask) * 12);
            var address = GetFpuMemoryAddress(
                mode,
                register,
                byteSize,
                allowPcRelative: !registersToMemory);
            var predecrementEa = mode == 4;
            var reverseRegisterOrder = predecrementEa && maskMode < 2;
            var selectedCount = CountFpuRegisterMaskBits(mask);
            var selectedIndex = 0;
            for (var slot = 0; slot < 8; slot++)
            {
                if ((mask & (0x80 >> slot)) == 0)
                {
                    continue;
                }

                var fpRegister = reverseRegisterOrder ? 7 - slot : slot;
                var slotAddress = predecrementEa
                    ? address + (uint)((selectedCount - 1 - selectedIndex) * 12)
                    : address + (uint)(selectedIndex * 12);
                if (registersToMemory)
                {
                    WriteFpuExtendedSlot(slotAddress, State.M68040Fpu.FP[fpRegister]);
                }
                else
                {
                    State.M68040Fpu.FP[fpRegister] = ReadFpuExtendedSlot(slotAddress);
                }

                selectedIndex++;
            }

            return false;
        }

        private void WriteFpuExtendedSlot(uint address, ExtF80 value)
        {
            var word0 = (uint)value.SignExponent << 16;
            var word1 = (uint)(value.Significand >> 32);
            var word2 = (uint)value.Significand;
            WriteLong(address, word0);
            WriteLong(address + 4, word1);
            WriteLong(address + 8, word2);
        }

        private ExtF80 ReadFpuExtendedSlot(uint address)
        {
            var signExponent = (ushort)(ReadLong(address) >> 16);
            var significand = ((ulong)ReadLong(address + 4) << 32) | ReadLong(address + 8);
            return ExtF80.FromBits(signExponent, significand);
        }

        private static int CountFpuRegisterMaskBits(byte mask)
        {
            var count = 0;
            while (mask != 0)
            {
                mask &= (byte)(mask - 1);
                count++;
            }

            return count;
        }

        private bool ExecuteFmoveToEa(ushort opcode, ushort extension)
        {
            State.M68040Fpu.MarkInstructionExecuted();
            State.M68040Fpu.Fpiar = State.LastInstructionProgramCounter;
            var source = (extension >> 7) & 7;
            var format = (extension >> 10) & 7;
            var sourceValue = State.M68040Fpu.FP[source];
            var exceptionVector = WriteFpuEa(opcode, format, sourceValue, out var effectiveAddress);
            if (exceptionVector != 0)
            {
                if (exceptionVector == 55)
                {
                    State.M68040Fpu.PrepareUnsupportedData(
                        extension,
                        effectiveAddress,
                        sourceValue,
                        sourceValue,
                        postInstruction: true);
                }
                else
                {
                    State.M68040Fpu.PrepareArithmeticException(
                        exceptionVector,
                        extension,
                        effectiveAddress,
                        sourceValue,
                        sourceValue);
                }

                RaiseFpuFormat3Exception(exceptionVector, State.ProgramCounter, effectiveAddress);
                return true;
            }

            return false;
        }

        private M68040FpuOperand ReadFpuEa(ushort opcode, int format)
        {
            var mode = (opcode >> 3) & 7;
            var register = opcode & 7;
            if (mode == 0)
            {
                return ReadFpuDataRegister(State.D[register], format);
            }

            if (mode == 7 && register == 4)
            {
                return ReadFpuImmediate(format);
            }

            var address = GetFpuMemoryAddress(mode, register, FpuFormatByteSize(format));
            switch (format)
            {
                case 0:
                    return ExactFpuOperand(
                        ExtF80Math.FromInt32(unchecked((int)ReadLong(address))),
                        address);
                case 1:
                    return Binary32FpuOperand(ReadLong(address), address);
                case 2:
                    return ExtendedFpuOperand(ReadFpuExtendedSlot(address), address);
                case 3:
                case 7:
                    return PackedFpuOperand(
                        ReadLong(address),
                        ReadLong(address + 4),
                        ReadLong(address + 8),
                        address);
                case 4:
                    return ExactFpuOperand(
                        ExtF80Math.FromInt32(unchecked((short)ReadWord(address))),
                        address);
                case 5:
                    return Binary64FpuOperand(
                        ((ulong)ReadLong(address) << 32) | ReadLong(address + 4),
                        address);
                case 6:
                    return ExactFpuOperand(
                        ExtF80Math.FromInt32(unchecked((sbyte)ReadByte(address))),
                        address);
                default:
                    throw new UnsupportedM68kTimingException(opcode, State.LastInstructionProgramCounter, _profile);
            }
        }

        private int WriteFpuEa(ushort opcode, int format, ExtF80 value, out uint effectiveAddress)
        {
            var mode = (opcode >> 3) & 7;
            var register = opcode & 7;
            if (M68040FpuHelpers.IsUnsupportedDataType(value))
            {
                effectiveAddress = mode == 0
                    ? 0
                    : GetFpuMemoryAddress(mode, register, FpuFormatByteSize(format), allowPcRelative: false);
                return 55;
            }

            if (format != 2)
            {
                value = M68040FpuHelpers.CanonicalizeOperand(value);
            }

            if (mode == 0)
            {
                effectiveAddress = 0;
                if (format is not (0 or 1 or 4 or 6))
                {
                    throw new UnsupportedM68kTimingException(opcode, State.LastInstructionProgramCounter, _profile);
                }

                var converted = M68040FpuHelpers.WriteDataRegister(
                    State.D[register],
                    format,
                    value,
                    State.M68040Fpu.Context.RoundingMode);
                var vector = ApplyFpuDestinationExceptions(value, converted.Flags, format);
                if (M68040FpuHelpers.ShouldCommitDestination(format, vector))
                {
                    State.D[register] = converted.Value;
                }

                return vector;
            }

            var address = GetFpuMemoryAddress(mode, register, FpuFormatByteSize(format), allowPcRelative: false);
            effectiveAddress = address;
            switch (format)
            {
                case 0:
                {
                    var converted = ExtF80Math.ToInt32(value, State.M68040Fpu.Context.RoundingMode);
                    var vector = ApplyFpuDestinationExceptions(value, converted.Flags, format);
                    if (M68040FpuHelpers.ShouldCommitDestination(format, vector))
                    {
                        WriteLong(
                            address,
                            M68040FpuHelpers.ResolveIntegerDestination(
                                value,
                                format,
                                converted.Value,
                                converted.Flags));
                    }
                    return vector;
                }
                case 1:
                {
                    var converted = ExtF80Math.ToBinary32Bits(value, State.M68040Fpu.Context.RoundingMode);
                    var vector = ApplyFpuDestinationExceptions(value, converted.Flags, format);
                    if (M68040FpuHelpers.ShouldCommitDestination(format, vector))
                    {
                        WriteLong(address, converted.Value);
                    }
                    return vector;
                }
                case 2:
                {
                    var flags = value.Classification == ExtF80Class.SignalingNaN
                        ? FloatingPointExceptionFlags.Invalid
                        : FloatingPointExceptionFlags.None;
                    var vector = ApplyFpuDestinationExceptions(value, flags, format);
                    if (vector == 0)
                    {
                        var stored = value.Classification == ExtF80Class.SignalingNaN
                            ? ExtF80.FromBits(value.SignExponent, value.Significand | 0x4000_0000_0000_0000UL)
                            : value;
                        WriteFpuExtendedSlot(address, stored);
                    }
                    return vector;
                }
                case 3:
                case 7:
                    return 55;
                case 4:
                {
                    var converted = ExtF80Math.ToInt16(value, State.M68040Fpu.Context.RoundingMode);
                    var vector = ApplyFpuDestinationExceptions(value, converted.Flags, format);
                    if (M68040FpuHelpers.ShouldCommitDestination(format, vector))
                    {
                        WriteWord(
                            address,
                            (ushort)M68040FpuHelpers.ResolveIntegerDestination(
                                value,
                                format,
                                converted.Value,
                                converted.Flags));
                    }
                    return vector;
                }
                case 5:
                {
                    var converted = ExtF80Math.ToBinary64Bits(value, State.M68040Fpu.Context.RoundingMode);
                    var vector = ApplyFpuDestinationExceptions(value, converted.Flags, format);
                    if (M68040FpuHelpers.ShouldCommitDestination(format, vector))
                    {
                        WriteLong(address, (uint)(converted.Value >> 32));
                        WriteLong(address + 4, (uint)converted.Value);
                    }

                    return vector;
                }
                case 6:
                {
                    var converted = ExtF80Math.ToInt8(value, State.M68040Fpu.Context.RoundingMode);
                    var vector = ApplyFpuDestinationExceptions(value, converted.Flags, format);
                    if (M68040FpuHelpers.ShouldCommitDestination(format, vector))
                    {
                        WriteByte(
                            address,
                            (byte)M68040FpuHelpers.ResolveIntegerDestination(
                                value,
                                format,
                                converted.Value,
                                converted.Flags));
                    }
                    return vector;
                }
                default:
                    throw new UnsupportedM68kTimingException(opcode, State.LastInstructionProgramCounter, _profile);
            }
        }

        private M68040FpuOperand ReadFpuDataRegister(uint value, int format)
        {
            return format switch
            {
                0 => ExactFpuOperand(ExtF80Math.FromInt32(unchecked((int)value))),
                1 => Binary32FpuOperand(value),
                4 => ExactFpuOperand(ExtF80Math.FromInt32(unchecked((short)value))),
                6 => ExactFpuOperand(ExtF80Math.FromInt32(unchecked((sbyte)value))),
                _ => throw new UnsupportedM68kTimingException(State.LastOpcode, State.LastInstructionProgramCounter, _profile)
            };
        }

        private M68040FpuOperand ReadFpuImmediate(int format)
        {
            switch (format)
            {
                case 0:
                    return ExactFpuOperand(ExtF80Math.FromInt32(unchecked((int)FetchLong())));
                case 1:
                    return Binary32FpuOperand(FetchLong());
                case 2:
                    return ReadFpuExtendedImmediate();
                case 3:
                case 7:
                    return PackedFpuOperand(FetchLong(), FetchLong(), FetchLong());
                case 4:
                    return ExactFpuOperand(ExtF80Math.FromInt32(unchecked((short)FetchWord())));
                case 5:
                    return Binary64FpuOperand(((ulong)FetchLong() << 32) | FetchLong());
                case 6:
                    return ExactFpuOperand(ExtF80Math.FromInt32(unchecked((sbyte)(byte)FetchWord())));
                default:
                    throw new UnsupportedM68kTimingException(State.LastOpcode, State.LastInstructionProgramCounter, _profile);
            }
        }

        private M68040FpuOperand ReadFpuExtendedImmediate()
        {
            var signExponent = FetchWord();
            _ = FetchWord();
            var significand = ((ulong)FetchLong() << 32) | FetchLong();
            return ExtendedFpuOperand(ExtF80.FromBits(signExponent, significand));
        }

        private int ApplyFpuDestinationExceptions(ExtF80 value, FloatingPointExceptionFlags flags, int format)
            => M68040FpuHelpers.ApplyDestinationExceptions(State.M68040Fpu, value, flags, format);

        private static M68040FpuOperand ExactFpuOperand(ExtF80 value, uint effectiveAddress = 0)
            => new(
                value,
                FloatingPointExceptionFlags.None,
                effectiveAddress,
                effectiveAddress != 0);

        private static M68040FpuOperand ExtendedFpuOperand(ExtF80 value, uint effectiveAddress = 0)
            => new(
                value,
                FloatingPointExceptionFlags.None,
                effectiveAddress,
                effectiveAddress != 0,
                M68040FpuHelpers.IsUnsupportedDataType(value));

        private static M68040FpuOperand Binary32FpuOperand(uint bits, uint effectiveAddress = 0)
        {
            var converted = ExtF80Math.FromBinary32Bits(bits);
            return new M68040FpuOperand(
                converted.Value,
                converted.Flags,
                effectiveAddress,
                effectiveAddress != 0,
                (bits & 0x7F80_0000u) == 0 && (bits & 0x007F_FFFFu) != 0);
        }

        private static M68040FpuOperand Binary64FpuOperand(ulong bits, uint effectiveAddress = 0)
        {
            var converted = ExtF80Math.FromBinary64Bits(bits);
            return new M68040FpuOperand(
                converted.Value,
                converted.Flags,
                effectiveAddress,
                effectiveAddress != 0,
                (bits & 0x7FF0_0000_0000_0000UL) == 0 && (bits & 0x000F_FFFF_FFFF_FFFFUL) != 0);
        }

        private static M68040FpuOperand PackedFpuOperand(
            uint word0,
            uint word1,
            uint word2,
            uint effectiveAddress = 0)
            => new(
                ExtF80.FromBits((ushort)(word0 >> 16), ((ulong)word1 << 32) | word2),
                FloatingPointExceptionFlags.None,
                effectiveAddress,
                effectiveAddress != 0,
                Unsupported: true);

        private static uint FpuFormatByteSize(int format)
        {
            return M68040FpuHelpers.FormatByteSize(format);
        }

        private uint GetFpuMemoryAddress(int mode, int register, uint byteSize, bool allowPcRelative = true)
        {
            return mode switch
            {
                2 => State.A[register],
                3 => PostIncrementAddress(register, byteSize),
                4 => PredecrementAddress(register, byteSize),
                5 => unchecked((uint)(State.A[register] + (int)(short)FetchWord())),
                6 => GetFpuIndexedAddress(State.A[register]),
                7 when register == 0 => unchecked((uint)(int)(short)FetchWord()),
                7 when register == 1 => FetchLong(),
                7 when register == 2 && allowPcRelative => GetFpuPcDisplacementAddress(),
                7 when register == 3 && allowPcRelative => GetFpuPcIndexedAddress(),
                _ => throw new UnsupportedM68kTimingException(State.LastOpcode, State.LastInstructionProgramCounter, _profile)
            };
        }

        private void ConsumeFpuMemoryAddressExtension(int mode, int register, bool allowPcRelative)
        {
            switch (mode)
            {
                case 2:
                case 3:
                case 4:
                    return;
                case 5:
                    _ = FetchWord();
                    return;
                case 6:
                    ConsumeFpuIndexedExtension();
                    return;
                case 7 when register == 0:
                    _ = FetchWord();
                    return;
                case 7 when register == 1:
                    _ = FetchLong();
                    return;
                case 7 when register == 2 && allowPcRelative:
                    _ = FetchWord();
                    return;
                case 7 when register == 3 && allowPcRelative:
                    ConsumeFpuIndexedExtension();
                    return;
                default:
                    throw new UnsupportedM68kTimingException(
                        State.LastOpcode,
                        State.LastInstructionProgramCounter,
                        _profile);
            }
        }

        private void ConsumeFpuIndexedExtension()
        {
            var extension = FetchWord();
            if ((extension & 0x0100) == 0)
            {
                return;
            }

            if ((extension & 0x0008) != 0)
            {
                throw new UnsupportedM68kTimingException(
                    State.LastOpcode,
                    State.LastInstructionProgramCounter,
                    _profile);
            }

            ConsumeFpuIndexedDisplacement((extension >> 4) & 3, baseDisplacement: true);
            var indirectSelection = extension & 7;
            if (indirectSelection == 4)
            {
                throw new UnsupportedM68kTimingException(
                    State.LastOpcode,
                    State.LastInstructionProgramCounter,
                    _profile);
            }

            if (indirectSelection != 0)
            {
                ConsumeFpuIndexedDisplacement(indirectSelection & 3, baseDisplacement: false);
            }
        }

        private void ConsumeFpuIndexedDisplacement(int size, bool baseDisplacement)
        {
            if (size == 0 && baseDisplacement)
            {
                throw new UnsupportedM68kTimingException(
                    State.LastOpcode,
                    State.LastInstructionProgramCounter,
                    _profile);
            }

            if (size == 2)
            {
                _ = FetchWord();
            }
            else if (size == 3)
            {
                _ = FetchLong();
            }
        }

        private uint GetFpuPcDisplacementAddress()
        {
            var extensionAddress = State.ProgramCounter;
            return unchecked((uint)(extensionAddress + (int)(short)FetchWord()));
        }

        private uint GetFpuPcIndexedAddress()
        {
            var extensionAddress = State.ProgramCounter;
            return GetFpuIndexedAddress(extensionAddress);
        }

        private uint GetFpuIndexedAddress(uint baseAddress)
        {
            var extension = FetchWord();
            if ((extension & 0x0100) == 0)
            {
                if (!M68kIntegerSemantics.TryCalculateM68020BriefIndexedAddress(
                    baseAddress,
                    extension,
                    State.D,
                    State.A,
                    out var briefAddress))
                {
                    throw new UnsupportedM68kTimingException(State.LastOpcode, State.LastInstructionProgramCounter, _profile);
                }

                return briefAddress;
            }

            if ((extension & 0x0008) != 0)
            {
                throw new UnsupportedM68kTimingException(State.LastOpcode, State.LastInstructionProgramCounter, _profile);
            }

            var baseValue = (extension & 0x0080) != 0 ? 0u : baseAddress;
            var indexValue = (extension & 0x0040) != 0 ? 0u : GetFpuIndexValue(extension);
            var baseDisplacement = ReadFpuIndexedDisplacement((extension >> 4) & 3, baseDisplacement: true);
            var indirectSelection = extension & 7;
            if (indirectSelection == 0)
            {
                return unchecked(baseValue + indexValue + baseDisplacement);
            }

            if (indirectSelection == 4)
            {
                throw new UnsupportedM68kTimingException(State.LastOpcode, State.LastInstructionProgramCounter, _profile);
            }

            var outerDisplacement = ReadFpuIndexedDisplacement(indirectSelection & 3, baseDisplacement: false);
            if (indirectSelection >= 5)
            {
                var pointer = ReadLong(unchecked(baseValue + baseDisplacement + indexValue));
                return unchecked(pointer + outerDisplacement);
            }

            var postindexedPointer = ReadLong(unchecked(baseValue + baseDisplacement));
            return unchecked(postindexedPointer + indexValue + outerDisplacement);
        }

        private uint GetFpuIndexValue(ushort extension)
        {
            var register = (extension >> 12) & 7;
            var raw = (extension & 0x8000) != 0 ? State.A[register] : State.D[register];
            var value = (extension & 0x0800) != 0
                ? unchecked((int)raw)
                : unchecked((int)(short)raw);
            return unchecked((uint)(value * (1 << ((extension >> 9) & 3))));
        }

        private uint ReadFpuIndexedDisplacement(int size, bool baseDisplacement)
        {
            if (size == 0 && baseDisplacement)
            {
                throw new UnsupportedM68kTimingException(State.LastOpcode, State.LastInstructionProgramCounter, _profile);
            }

            return size switch
            {
                0 or 1 => 0,
                2 => unchecked((uint)(int)(short)FetchWord()),
                3 => FetchLong(),
                _ => 0
            };
        }

        private static bool IsValidFpuStateFrameEa(int mode, int register, bool restore)
            => mode is 2 or 5 or 6 ||
                (restore ? mode == 3 : mode == 4) ||
                (mode == 7 && register is 0 or 1);

        private uint GetFpuStateFrameAddress(int mode, int register, uint byteSize, bool restore)
        {
            return mode switch
            {
                2 => State.A[register],
                3 when restore => State.A[register],
                4 when !restore => PredecrementAddress(register, byteSize),
                5 => unchecked((uint)(State.A[register] + (int)(short)FetchWord())),
                6 => GetFpuIndexedAddress(State.A[register]),
                7 when register == 0 => unchecked((uint)(int)(short)FetchWord()),
                7 when register == 1 => FetchLong(),
                _ => throw new UnsupportedM68kTimingException(State.LastOpcode, State.LastInstructionProgramCounter, _profile)
            };
        }

        private uint PostIncrementAddress(int register, uint byteSize)
        {
            var address = State.A[register];
            var updated = address + (register == 7 && byteSize == 1 ? 2u : byteSize);
            if (register == 7)
            {
                State.SetActiveStackPointer(updated);
            }
            else
            {
                State.A[register] = updated;
            }
            return address;
        }

        private uint PredecrementAddress(int register, uint byteSize)
        {
            var updated = State.A[register] - (register == 7 && byteSize == 1 ? 2u : byteSize);
            if (register == 7)
            {
                State.SetActiveStackPointer(updated);
            }
            else
            {
                State.A[register] = updated;
            }
            return updated;
        }

        private void RaiseFpuFormat2Exception(int vector, uint stackedProgramCounter, uint effectiveAddress)
            => RaiseFpuFormatException(2, vector, stackedProgramCounter, effectiveAddress);

        private void RaiseFpuFormat3Exception(int vector, uint stackedProgramCounter, uint effectiveAddress)
            => RaiseFpuFormatException(3, vector, stackedProgramCounter, effectiveAddress);

        private void RaiseFpuFormatException(
            int format,
            int vector,
            uint stackedProgramCounter,
            uint effectiveAddress)
        {
            var savedStatusRegister = State.StatusRegister;
            State.RecordException(vector, stackedProgramCounter, savedStatusRegister);
            State.StatusRegister = (ushort)((State.StatusRegister | M68kCpuState.Supervisor) & ~M68kCpuState.Master);
            PushLong(effectiveAddress);
            PushWord((ushort)((format << 12) | ((vector * 4) & 0x0FFF)));
            PushLong(stackedProgramCounter);
            PushWord(savedStatusRegister);
            State.ProgramCounter = ReadLong(State.VectorBaseRegister + ((uint)vector * 4));
            CompleteTiming(M68kInstructionTimingKey.LineFException);
        }

        private void RaiseMmuFault(M68040MmuFault fault)
        {
            var bypass = State.M68040Mmu.BypassTranslation;
            State.M68040Mmu.BypassTranslation = true;
            try
            {
                State.M68040Mmu.Status = fault.Status;
                var stackedProgramCounter = fault.StackedProgramCounter ??
                    (fault.AccessKind == M68kBusAccessKind.CpuInstructionFetch
                        ? fault.LogicalAddress
                        : State.LastInstructionProgramCounter);
                RaiseFormat0Exception(VectorBusError, stackedProgramCounter, M68kInstructionTimingKey.IllegalInstruction);
            }
            finally
            {
                State.M68040Mmu.BypassTranslation = bypass;
            }
        }

        private uint ReadPhysicalLong(uint physicalAddress)
        {
            var cycle = State.Cycles;
            return _physicalBus.ReadLong(physicalAddress, ref cycle, M68kBusAccessKind.CpuDataRead);
        }
    }
}
