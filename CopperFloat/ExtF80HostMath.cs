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
    public static bool TryBinary(
        ExtF80 left,
        ExtF80 right,
        ExtF80Context context,
        ExtF80HostOperation operation,
        out FloatingPointResult<ExtF80> result)
    {
        if (operation != ExtF80HostOperation.Divide ||
            context.RoundingMode != ExtF80RoundingMode.ToNearestEven)
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

        var flags = IsExactQuotient(left, right, 24)
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

        var flags = IsExactQuotient(left, right, 53)
            ? FloatingPointExceptionFlags.None
            : FloatingPointExceptionFlags.Inexact;
        result = new FloatingPointResult<ExtF80>(ExtF80Math.FromBinary64Bits(bits).Value, flags);
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
