/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Copper68k
{
    internal static class M68kIntegerSemantics
    {
        internal const ushort NegativeZeroFlags = M68kCpuState.Negative | M68kCpuState.Zero;
        internal const ushort LogicFlags = NegativeZeroFlags | M68kCpuState.Overflow | M68kCpuState.Carry;
        internal const ushort ArithmeticFlags = LogicFlags | M68kCpuState.Extend;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static uint AddressIncrement(int register, M68kOperandSize size)
            => size == M68kOperandSize.Byte && register == 7 ? 2u : (uint)size;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ushort GetNegativeZeroFlags(uint value, M68kOperandSize size)
        {
            value &= M68kCpuState.Mask(size);
            var flags = (ushort)0;
            if (value == 0)
            {
                flags |= M68kCpuState.Zero;
            }

            if ((value & M68kCpuState.SignBit(size)) != 0)
            {
                flags |= M68kCpuState.Negative;
            }

            return flags;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int WithNegativeZeroFlags(int status, uint value, M68kOperandSize size)
            => (status & ~NegativeZeroFlags) | GetNegativeZeroFlags(value, size);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool EvaluateCondition(ushort status, int condition)
        {
            return condition switch
            {
                0x0 => true,
                0x1 => false,
                0x2 => (status & (M68kCpuState.Carry | M68kCpuState.Zero)) == 0,
                0x3 => (status & (M68kCpuState.Carry | M68kCpuState.Zero)) != 0,
                0x4 => (status & M68kCpuState.Carry) == 0,
                0x5 => (status & M68kCpuState.Carry) != 0,
                0x6 => (status & M68kCpuState.Zero) == 0,
                0x7 => (status & M68kCpuState.Zero) != 0,
                0x8 => (status & M68kCpuState.Overflow) == 0,
                0x9 => (status & M68kCpuState.Overflow) != 0,
                0xA => (status & M68kCpuState.Negative) == 0,
                0xB => (status & M68kCpuState.Negative) != 0,
                0xC => ((status ^ (status << 2)) & M68kCpuState.Negative) == 0,
                0xD => ((status ^ (status << 2)) & M68kCpuState.Negative) != 0,
                0xE => (status & M68kCpuState.Zero) == 0 &&
                    ((status ^ (status << 2)) & M68kCpuState.Negative) == 0,
                0xF => (status & M68kCpuState.Zero) != 0 ||
                    ((status ^ (status << 2)) & M68kCpuState.Negative) != 0,
                _ => false
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool EvaluateCondition(bool carry, bool zero, bool negative, bool overflow, int condition)
        {
            return condition switch
            {
                0x0 => true,
                0x1 => false,
                0x2 => !carry && !zero,
                0x3 => carry || zero,
                0x4 => !carry,
                0x5 => carry,
                0x6 => !zero,
                0x7 => zero,
                0x8 => !overflow,
                0x9 => overflow,
                0xA => !negative,
                0xB => negative,
                0xC => negative == overflow,
                0xD => negative != overflow,
                0xE => !zero && negative == overflow,
                0xF => zero || negative != overflow,
                _ => false
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static M68kArithmeticResult Add(uint destination, uint source, M68kOperandSize size, uint carryIn = 0)
        {
            var mask = M68kCpuState.Mask(size);
            destination &= mask;
            source &= mask;
            var full = (ulong)destination + source + carryIn;
            var result = (uint)full & mask;
            return new M68kArithmeticResult(
                result,
                overflow: CalculateAddOverflow(destination, source, result, size),
                carry: full > mask);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static M68kArithmeticResult Subtract(uint destination, uint source, M68kOperandSize size, uint borrowIn = 0)
        {
            var mask = M68kCpuState.Mask(size);
            destination &= mask;
            source &= mask;
            var subtrahend = (ulong)source + borrowIn;
            var result = (uint)(((ulong)destination - subtrahend) & mask);
            return new M68kArithmeticResult(
                result,
                overflow: CalculateSubtractOverflow(destination, source, result, size),
                carry: subtrahend > destination);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong Negx(uint value, int status, int sizeValue)
        {
            var size = (M68kOperandSize)sizeValue;
            var arithmetic = Subtract(
                0,
                value,
                size,
                (status & M68kCpuState.Extend) != 0 ? 1u : 0u);
            var nextStatus = status & unchecked((int)~ArithmeticFlags);
            if (arithmetic.Value == 0 && (status & M68kCpuState.Zero) != 0)
            {
                nextStatus |= M68kCpuState.Zero;
            }

            if ((arithmetic.Value & M68kCpuState.SignBit(size)) != 0)
            {
                nextStatus |= M68kCpuState.Negative;
            }

            if (arithmetic.Overflow)
            {
                nextStatus |= M68kCpuState.Overflow;
            }

            if (arithmetic.Carry)
            {
                nextStatus |= M68kCpuState.Carry | M68kCpuState.Extend;
            }

            return ((ulong)(uint)nextStatus << 32) | arithmetic.Value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static M68kArithmeticResult CalculateAddFlags(
            uint destination,
            uint source,
            uint result,
            M68kOperandSize size,
            uint carryIn = 0)
        {
            var mask = M68kCpuState.Mask(size);
            destination &= mask;
            source &= mask;
            result &= mask;
            var full = (ulong)destination + source + carryIn;
            return new M68kArithmeticResult(
                result,
                overflow: CalculateAddOverflow(destination, source, result, size),
                carry: full > mask);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static M68kArithmeticResult CalculateSubtractFlags(
            uint destination,
            uint source,
            uint result,
            M68kOperandSize size,
            uint borrowIn = 0)
        {
            var mask = M68kCpuState.Mask(size);
            destination &= mask;
            source &= mask;
            result &= mask;
            var subtrahend = (ulong)source + borrowIn;
            return new M68kArithmeticResult(
                result,
                overflow: CalculateSubtractOverflow(destination, source, result, size),
                carry: subtrahend > destination);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static M68kShiftResult Shift(
            uint value,
            int count,
            M68kOperandSize size,
            int type,
            bool left,
            bool extendIn)
        {
            value &= M68kCpuState.Mask(size);
            if (count == 0)
            {
                return new M68kShiftResult(value, carry: false, extend: extendIn, extendChanged: false);
            }

            var bits = size == M68kOperandSize.Long ? 32 : size == M68kOperandSize.Word ? 16 : 8;
            var mask = M68kCpuState.Mask(size);
            var carry = false;
            var extend = extendIn;
            for (var i = 0; i < count; i++)
            {
                if (left)
                {
                    carry = (value & (1u << (bits - 1))) != 0;
                    value = type switch
                    {
                        2 => ((value << 1) & mask) | (extend ? 1u : 0u),
                        3 => ((value << 1) & mask) | (carry ? 1u : 0u),
                        _ => (value << 1) & mask
                    };
                }
                else
                {
                    carry = (value & 1) != 0;
                    if (type == 0)
                    {
                        var sign = value & (1u << (bits - 1));
                        value = (value >> 1) | sign;
                    }
                    else if (type == 2)
                    {
                        value = (value >> 1) | (extend ? 1u << (bits - 1) : 0u);
                    }
                    else if (type == 3)
                    {
                        value = (value >> 1) | (carry ? 1u << (bits - 1) : 0u);
                    }
                    else
                    {
                        value >>= 1;
                    }
                }

                if (type == 2)
                {
                    extend = carry;
                }
            }

            return new M68kShiftResult(value & mask, carry, carry, extendChanged: type != 3);
        }

        internal static byte AddBcdByte(byte destination, byte source, int extend, out bool carry)
        {
            var low = (destination & 0x0F) + (source & 0x0F) + extend;
            var high = (destination >> 4) + (source >> 4);
            if (low > 9)
            {
                low -= 10;
                high++;
            }

            carry = high > 9;
            if (carry)
            {
                high -= 10;
            }

            return (byte)((high << 4) | low);
        }

        internal static byte SubtractBcdByte(byte destination, byte source, int extend, out bool carry)
        {
            var low = (destination & 0x0F) - (source & 0x0F) - extend;
            var high = (destination >> 4) - (source >> 4);
            if (low < 0)
            {
                low += 10;
                high--;
            }

            carry = high < 0;
            if (carry)
            {
                high += 10;
            }

            return (byte)((high << 4) | low);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int CountBits(uint value)
            => BitOperations.PopCount(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int CountSetBits(ushort value)
            => BitOperations.PopCount(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetMultiplyCycles(int sourceEaCycles, uint sourceValue, bool signed)
            => sourceEaCycles + GetMultiplyCoreCycles(sourceValue, signed);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetMultiplyCoreCycles(uint sourceValue, bool signed)
        {
            sourceValue &= 0xFFFF;
            return signed
                ? 38 + (CountSignedMultiplyTransitions((ushort)sourceValue) * 2)
                : 38 + (CountBits(sourceValue) * 2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetDivideCycles(int sourceEaCycles, uint dividend, ushort divisor, bool signed)
            => sourceEaCycles + GetDivideCoreCycles(dividend, divisor, signed);

        internal static int GetDivideCoreCycles(uint dividend, ushort divisor, bool signed)
        {
            if (divisor == 0)
            {
                return 0;
            }

            return signed
                ? GetSignedDivideCoreCycles(unchecked((int)dividend), unchecked((short)divisor))
                : GetUnsignedDivideCoreCycles(dividend, divisor);
        }

        private static int GetUnsignedDivideCoreCycles(uint dividend, ushort divisor)
        {
            if ((dividend >> 16) >= divisor)
            {
                return 6;
            }

            var microcycles = 38;
            var alignedDivisor = (uint)divisor << 16;
            for (var bit = 0; bit < 15; bit++)
            {
                var previousDividend = dividend;
                dividend <<= 1;
                if ((previousDividend & 0x8000_0000u) != 0)
                {
                    dividend -= alignedDivisor;
                }
                else
                {
                    microcycles += 2;
                    if (dividend >= alignedDivisor)
                    {
                        dividend -= alignedDivisor;
                        microcycles--;
                    }
                }
            }

            return (microcycles * 2) - 4;
        }

        private static int GetSignedDivideCoreCycles(int dividend, short divisor)
        {
            var microcycles = dividend < 0 ? 7 : 6;
            var absoluteDividend = AbsAsUInt32(dividend);
            var absoluteDivisor = AbsAsUInt16(divisor);
            if ((absoluteDividend >> 16) >= absoluteDivisor)
            {
                return ((microcycles + 2) * 2) - 4;
            }

            var absoluteQuotient = absoluteDividend / absoluteDivisor;
            microcycles += 55;
            if (divisor >= 0)
            {
                microcycles += dividend >= 0 ? -1 : 1;
            }

            for (var bit = 0; bit < 15; bit++)
            {
                if ((absoluteQuotient & 0x8000u) == 0)
                {
                    microcycles++;
                }

                absoluteQuotient <<= 1;
            }

            return (microcycles * 2) - 4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint AbsAsUInt32(int value)
            => value < 0 ? unchecked((uint)-((long)value)) : unchecked((uint)value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ushort AbsAsUInt16(short value)
            => value < 0 ? unchecked((ushort)-((int)value)) : unchecked((ushort)value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int CountSignedMultiplyTransitions(ushort sourceValue)
        {
            var value = (uint)sourceValue;
            return BitOperations.PopCount((value ^ (value << 1)) & 0xFFFFu);
        }

        internal static uint CalculateM68000BriefIndexedAddress(
            uint baseAddress,
            ushort extension,
            ReadOnlySpan<uint> dataRegisters,
            ReadOnlySpan<uint> addressRegisters)
        {
            var displacement = unchecked((sbyte)(extension & 0xFF));
            return unchecked(baseAddress + CalculateM68000BriefIndexValue(extension, dataRegisters, addressRegisters) + (uint)displacement);
        }

        internal static uint CalculateM68000BriefIndexValue(
            ushort extension,
            ReadOnlySpan<uint> dataRegisters,
            ReadOnlySpan<uint> addressRegisters)
        {
            var indexRegister = (extension >> 12) & 7;
            var indexValue = (extension & 0x8000) != 0
                ? addressRegisters[indexRegister]
                : dataRegisters[indexRegister];
            return (extension & 0x0800) == 0
                ? M68kCpuState.SignExtend(indexValue, M68kOperandSize.Word)
                : indexValue;
        }

        internal static bool TryCalculateM68020BriefIndexedAddress(
            uint baseAddress,
            ushort extension,
            ReadOnlySpan<uint> dataRegisters,
            ReadOnlySpan<uint> addressRegisters,
            out uint address)
        {
            if ((extension & 0x0100) != 0)
            {
                address = 0;
                return false;
            }

            var indexRegister = (extension >> 12) & 7;
            var usesAddressRegister = (extension & 0x8000) != 0;
            var usesLongIndex = (extension & 0x0800) != 0;
            var scale = 1 << ((extension >> 9) & 0x3);
            var displacement = unchecked((int)(sbyte)(extension & 0xFF));
            var rawIndex = usesAddressRegister ? addressRegisters[indexRegister] : dataRegisters[indexRegister];
            var index = usesLongIndex
                ? unchecked((int)rawIndex)
                : unchecked((int)(short)(rawIndex & 0xFFFF));
            address = unchecked((uint)(baseAddress + displacement + (index * scale)));
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool CalculateAddOverflow(uint destination, uint source, uint result, M68kOperandSize size)
            => (~(destination ^ source) & (destination ^ result) & M68kCpuState.SignBit(size)) != 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool CalculateSubtractOverflow(uint destination, uint source, uint result, M68kOperandSize size)
            => ((destination ^ source) & (destination ^ result) & M68kCpuState.SignBit(size)) != 0;
    }

    internal readonly struct M68kArithmeticResult
    {
        public M68kArithmeticResult(uint value, bool overflow, bool carry)
        {
            Value = value;
            Overflow = overflow;
            Carry = carry;
        }

        public uint Value { get; }

        public bool Overflow { get; }

        public bool Carry { get; }
    }

    internal readonly struct M68kShiftResult
    {
        public M68kShiftResult(uint value, bool carry, bool extend, bool extendChanged)
        {
            Value = value;
            Carry = carry;
            Extend = extend;
            ExtendChanged = extendChanged;
        }

        public uint Value { get; }

        public bool Carry { get; }

        public bool Extend { get; }

        public bool ExtendChanged { get; }
    }
}
