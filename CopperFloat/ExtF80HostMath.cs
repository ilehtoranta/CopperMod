/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace CopperFloat;

internal enum ExtF80HostOperation
{
    Add,
    Subtract,
    Multiply,
    Divide
}

internal static class ExtF80HostMath
{
    public static bool TrySquareRoot(
        ExtF80 value,
        ExtF80Context context,
        out FloatingPointResult<ExtF80> result)
    {
        if (value.Sign || context.RoundingMode != ExtF80RoundingMode.ToNearestEven)
        {
            result = default;
            return false;
        }

        return context.Precision switch
        {
            ExtF80Precision.Single => TrySquareRoot32(value, out result),
            ExtF80Precision.Double => TrySquareRoot64(value, out result),
            _ => Fail(out result)
        };
    }

    public static bool TryBinary(
        ExtF80 left,
        ExtF80 right,
        ExtF80Context context,
        ExtF80HostOperation operation,
        out FloatingPointResult<ExtF80> result)
    {
        if (context.RoundingMode != ExtF80RoundingMode.ToNearestEven)
        {
            result = default;
            return false;
        }

        return context.Precision switch
        {
            ExtF80Precision.Single => TryBinary32(left, right, operation, out result),
            ExtF80Precision.Double => TryBinary64(left, right, operation, out result),
            _ => Fail(out result)
        };
    }

    private static bool TryBinary32(
        ExtF80 left,
        ExtF80 right,
        ExtF80HostOperation operation,
        out FloatingPointResult<ExtF80> result)
    {
        if (!TryEncodeBinary32Normal(left, out var leftBits) ||
            !TryEncodeBinary32Normal(right, out var rightBits))
        {
            result = default;
            return false;
        }

        var value = ExecuteBinary32(leftBits, rightBits, operation);
        var bits = unchecked((uint)BitConverter.SingleToInt32Bits(value));
        var exponent = bits & 0x7F80_0000u;
        if (exponent == 0 || exponent == 0x7F80_0000u)
        {
            result = default;
            return false;
        }

        var exact = operation switch
        {
            ExtF80HostOperation.Add => IsExactBinary32Sum(leftBits, rightBits, bits, subtractRight: false),
            ExtF80HostOperation.Subtract => IsExactBinary32Sum(leftBits, rightBits, bits, subtractRight: true),
            ExtF80HostOperation.Multiply => IsExactBinary32Product(leftBits, rightBits),
            _ => IsExactQuotient(left, right, 24)
        };
        var flags = exact
            ? FloatingPointExceptionFlags.None
            : FloatingPointExceptionFlags.Inexact;
        result = new FloatingPointResult<ExtF80>(ExtF80Math.FromBinary32Bits(bits).Value, flags);
        return true;
    }

    private static bool TryBinary64(
        ExtF80 left,
        ExtF80 right,
        ExtF80HostOperation operation,
        out FloatingPointResult<ExtF80> result)
    {
        if (!TryEncodeBinary64Normal(left, out var leftBits) ||
            !TryEncodeBinary64Normal(right, out var rightBits))
        {
            result = default;
            return false;
        }

        var value = ExecuteBinary64(leftBits, rightBits, operation);
        var bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
        var exponent = bits & 0x7FF0_0000_0000_0000UL;
        if (exponent == 0 || exponent == 0x7FF0_0000_0000_0000UL)
        {
            result = default;
            return false;
        }

        var exact = operation switch
        {
            ExtF80HostOperation.Add => IsExactBinary64Sum(leftBits, rightBits, bits, subtractRight: false),
            ExtF80HostOperation.Subtract => IsExactBinary64Sum(leftBits, rightBits, bits, subtractRight: true),
            ExtF80HostOperation.Multiply => IsExactBinary64Product(leftBits, rightBits),
            _ => IsExactQuotient(left, right, 53)
        };
        var flags = exact
            ? FloatingPointExceptionFlags.None
            : FloatingPointExceptionFlags.Inexact;
        result = new FloatingPointResult<ExtF80>(ExtF80Math.FromBinary64Bits(bits).Value, flags);
        return true;
    }

    private static bool TrySquareRoot32(
        ExtF80 value,
        out FloatingPointResult<ExtF80> result)
    {
        if (!TryEncodeBinary32Normal(value, out var valueBits))
        {
            result = default;
            return false;
        }

        var root = MathF.Sqrt(BitConverter.Int32BitsToSingle(unchecked((int)valueBits)));
        var rootBits = unchecked((uint)BitConverter.SingleToInt32Bits(root));
        var converted = ExtF80Math.FromBinary32Bits(rootBits).Value;
        var flags = IsExactSquareRoot(value, converted, 24)
            ? FloatingPointExceptionFlags.None
            : FloatingPointExceptionFlags.Inexact;
        result = new FloatingPointResult<ExtF80>(converted, flags);
        return true;
    }

    private static bool TrySquareRoot64(
        ExtF80 value,
        out FloatingPointResult<ExtF80> result)
    {
        if (!TryEncodeBinary64Normal(value, out var valueBits))
        {
            result = default;
            return false;
        }

        var root = Math.Sqrt(BitConverter.Int64BitsToDouble(unchecked((long)valueBits)));
        var rootBits = unchecked((ulong)BitConverter.DoubleToInt64Bits(root));
        var converted = ExtF80Math.FromBinary64Bits(rootBits).Value;
        var flags = IsExactSquareRoot(value, converted, 53)
            ? FloatingPointExceptionFlags.None
            : FloatingPointExceptionFlags.Inexact;
        result = new FloatingPointResult<ExtF80>(converted, flags);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ExecuteBinary32(uint leftBits, uint rightBits, ExtF80HostOperation operation)
    {
        var left = BitConverter.Int32BitsToSingle(unchecked((int)leftBits));
        var right = BitConverter.Int32BitsToSingle(unchecked((int)rightBits));
        return operation switch
        {
            ExtF80HostOperation.Add => left + right,
            ExtF80HostOperation.Subtract => left - right,
            ExtF80HostOperation.Multiply => left * right,
            _ => left / right
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double ExecuteBinary64(ulong leftBits, ulong rightBits, ExtF80HostOperation operation)
    {
        var left = BitConverter.Int64BitsToDouble(unchecked((long)leftBits));
        var right = BitConverter.Int64BitsToDouble(unchecked((long)rightBits));
        return operation switch
        {
            ExtF80HostOperation.Add => left + right,
            ExtF80HostOperation.Subtract => left - right,
            ExtF80HostOperation.Multiply => left * right,
            _ => left / right
        };
    }

    private static bool TryEncodeBinary32Normal(ExtF80 value, out uint bits)
    {
        var exponent = value.BiasedExponent - ExtF80.ExponentBias;
        if (value.Classification != ExtF80Class.Normal ||
            exponent is < -126 or > 127 ||
            (value.Significand & 0x0000_00FF_FFFF_FFFFUL) != 0)
        {
            bits = 0;
            return false;
        }

        bits = (value.Sign ? 0x8000_0000u : 0) |
            ((uint)(exponent + 127) << 23) |
            (uint)((value.Significand >> 40) & 0x007F_FFFF);
        return true;
    }

    private static bool TryEncodeBinary64Normal(ExtF80 value, out ulong bits)
    {
        var exponent = value.BiasedExponent - ExtF80.ExponentBias;
        if (value.Classification != ExtF80Class.Normal ||
            exponent is < -1022 or > 1023 ||
            (value.Significand & 0x7FF) != 0)
        {
            bits = 0;
            return false;
        }

        bits = (value.Sign ? 0x8000_0000_0000_0000UL : 0) |
            ((ulong)(exponent + 1023) << 52) |
            ((value.Significand >> 11) & 0x000F_FFFF_FFFF_FFFFUL);
        return true;
    }

    private static bool IsExactQuotient(ExtF80 left, ExtF80 right, int precision)
    {
        var numerator = left.Significand >> (64 - precision);
        var denominator = right.Significand >> (64 - precision);
        var divisor = GreatestCommonDivisor(numerator, denominator);
        denominator /= divisor;
        return BitOperations.IsPow2(denominator);
    }

    private static bool IsExactSquareRoot(ExtF80 value, ExtF80 root, int precision)
    {
        var shift = 64 - precision;
        var valueSignificand = value.Significand >> shift;
        var rootSignificand = root.Significand >> shift;
        var square = (UInt128)rootSignificand * rootSignificand;
        var valueExponent = value.BiasedExponent - ExtF80.ExponentBias - (precision - 1);
        var squareExponent = 2 * (root.BiasedExponent - ExtF80.ExponentBias - (precision - 1));

        var valueTrailingZeros = BitOperations.TrailingZeroCount(valueSignificand);
        valueSignificand >>= valueTrailingZeros;
        valueExponent += valueTrailingZeros;

        var squareTrailingZeros = TrailingZeroCount(square);
        square >>= squareTrailingZeros;
        squareExponent += squareTrailingZeros;
        return square == valueSignificand && squareExponent == valueExponent;
    }

    private static int TrailingZeroCount(UInt128 value)
    {
        var low = (ulong)value;
        return low != 0
            ? BitOperations.TrailingZeroCount(low)
            : 64 + BitOperations.TrailingZeroCount((ulong)(value >> 64));
    }

    private static bool IsExactBinary32Product(uint leftBits, uint rightBits)
    {
        var leftSignificand = (leftBits & 0x007F_FFFFu) | 0x0080_0000u;
        var rightSignificand = (rightBits & 0x007F_FFFFu) | 0x0080_0000u;
        var product = (ulong)leftSignificand * rightSignificand;
        var discardedBits = BitOperations.Log2(product) - 23;
        var discardedMask = (1UL << discardedBits) - 1;
        return (product & discardedMask) == 0;
    }

    private static bool IsExactBinary32Sum(
        uint leftBits,
        uint rightBits,
        uint resultBits,
        bool subtractRight)
    {
        var leftExponent = (int)((leftBits >> 23) & 0xFF);
        var rightExponent = (int)((rightBits >> 23) & 0xFF);
        var resultExponent = (int)((resultBits >> 23) & 0xFF);
        var minimumExponent = Math.Min(leftExponent, Math.Min(rightExponent, resultExponent));
        var maximumShift = Math.Max(leftExponent, Math.Max(rightExponent, resultExponent)) - minimumExponent;
        if (maximumShift > 100)
        {
            return false;
        }

        var left = ScaleBinary32Significand(leftBits, leftExponent - minimumExponent);
        var right = ScaleBinary32Significand(rightBits, rightExponent - minimumExponent);
        if (subtractRight)
        {
            right = -right;
        }

        var result = ScaleBinary32Significand(resultBits, resultExponent - minimumExponent);
        return left + right == result;
    }

    private static Int128 ScaleBinary32Significand(uint bits, int shift)
    {
        var significand = (Int128)((bits & 0x007F_FFFFu) | 0x0080_0000u) << shift;
        return (bits & 0x8000_0000u) != 0 ? -significand : significand;
    }

    private static bool IsExactBinary64Product(ulong leftBits, ulong rightBits)
    {
        var leftSignificand = (leftBits & 0x000F_FFFF_FFFF_FFFFUL) | 0x0010_0000_0000_0000UL;
        var rightSignificand = (rightBits & 0x000F_FFFF_FFFF_FFFFUL) | 0x0010_0000_0000_0000UL;
        var product = (UInt128)leftSignificand * rightSignificand;
        var discardedBits = Log2(product) - 52;
        var discardedMask = ((UInt128)1 << discardedBits) - 1;
        return (product & discardedMask) == 0;
    }

    private static bool IsExactBinary64Sum(
        ulong leftBits,
        ulong rightBits,
        ulong resultBits,
        bool subtractRight)
    {
        var leftExponent = (int)((leftBits >> 52) & 0x7FF);
        var rightExponent = (int)((rightBits >> 52) & 0x7FF);
        var resultExponent = (int)((resultBits >> 52) & 0x7FF);
        var minimumExponent = Math.Min(leftExponent, Math.Min(rightExponent, resultExponent));
        var maximumShift = Math.Max(leftExponent, Math.Max(rightExponent, resultExponent)) - minimumExponent;
        if (maximumShift > 70)
        {
            return false;
        }

        var left = ScaleBinary64Significand(leftBits, leftExponent - minimumExponent);
        var right = ScaleBinary64Significand(rightBits, rightExponent - minimumExponent);
        if (subtractRight)
        {
            right = -right;
        }

        var result = ScaleBinary64Significand(resultBits, resultExponent - minimumExponent);
        return left + right == result;
    }

    private static Int128 ScaleBinary64Significand(ulong bits, int shift)
    {
        var significand = (Int128)((bits & 0x000F_FFFF_FFFF_FFFFUL) | 0x0010_0000_0000_0000UL) << shift;
        return (bits & 0x8000_0000_0000_0000UL) != 0 ? -significand : significand;
    }

    private static int Log2(UInt128 value)
    {
        var high = (ulong)(value >> 64);
        return high != 0
            ? 64 + BitOperations.Log2(high)
            : BitOperations.Log2((ulong)value);
    }

    private static ulong GreatestCommonDivisor(ulong left, ulong right)
    {
        while (right != 0)
        {
            (left, right) = (right, left % right);
        }

        return left;
    }

    private static bool Fail(out FloatingPointResult<ExtF80> result)
    {
        result = default;
        return false;
    }
}
