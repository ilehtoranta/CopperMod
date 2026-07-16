/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace CopperFloat;

/// <summary>Provides deterministic arithmetic and conversions for <see cref="ExtF80"/>.</summary>
public static class ExtF80Math
{
    private const int MinimumExponent = 1 - ExtF80.ExponentBias;
    private const int MaximumExponent = 0x7ffe - ExtF80.ExponentBias;
    private static readonly UInt128 ExtraIntegerBit = (UInt128)1 << 66;
    private static readonly UInt128 ExtraCarryBit = (UInt128)1 << 67;

    /// <summary>Converts a signed 32-bit integer exactly.</summary>
    public static ExtF80 FromInt32(int value) => FromInt64(value);

    /// <summary>Converts a signed 64-bit integer exactly.</summary>
    public static ExtF80 FromInt64(long value)
    {
        if (value == 0)
        {
            return ExtF80.PositiveZero;
        }

        var sign = value < 0;
        var magnitude = sign
            ? unchecked((ulong)(-(value + 1)) + 1)
            : (ulong)value;
        var exponent = 63 - BitOperations.LeadingZeroCount(magnitude);
        var significand = magnitude << (63 - exponent);
        return Pack(sign, exponent, significand);
    }

    /// <summary>Converts a raw IEEE binary32 encoding to extended format.</summary>
    public static FloatingPointResult<ExtF80> FromBinary32Bits(uint bits)
    {
        var sign = (bits & 0x8000_0000) != 0;
        var exponent = (int)((bits >> 23) & 0xff);
        var fraction = bits & 0x007f_ffff;
        if (exponent == 0xff)
        {
            if (fraction == 0)
            {
                return Result(sign ? ExtF80.NegativeInfinity : ExtF80.PositiveInfinity);
            }

            var signaling = (fraction & 0x0040_0000) == 0;
            var significand = ExtF80.IntegerBit | ((ulong)fraction << 40) | ExtF80.QuietBit;
            return Result(
                ExtF80.FromBits(SignExponent(sign, 0x7fff), significand),
                signaling ? FloatingPointExceptionFlags.Invalid : FloatingPointExceptionFlags.None);
        }

        if (exponent == 0)
        {
            if (fraction == 0)
            {
                return Result(sign ? ExtF80.NegativeZero : ExtF80.PositiveZero);
            }

            var shift = BitOperations.LeadingZeroCount(fraction) - 8;
            var normalized = (ulong)fraction << shift;
            return Result(Pack(sign, -126 - shift, normalized << 40));
        }

        var extendedSignificand = (ExtF80.IntegerBit | ((ulong)fraction << 40));
        return Result(Pack(sign, exponent - 127, extendedSignificand));
    }

    /// <summary>Converts a raw IEEE binary64 encoding to extended format.</summary>
    public static FloatingPointResult<ExtF80> FromBinary64Bits(ulong bits)
    {
        var sign = (bits & 0x8000_0000_0000_0000) != 0;
        var exponent = (int)((bits >> 52) & 0x7ff);
        var fraction = bits & 0x000f_ffff_ffff_ffff;
        if (exponent == 0x7ff)
        {
            if (fraction == 0)
            {
                return Result(sign ? ExtF80.NegativeInfinity : ExtF80.PositiveInfinity);
            }

            var signaling = (fraction & 0x0008_0000_0000_0000) == 0;
            var significand = ExtF80.IntegerBit | (fraction << 11) | ExtF80.QuietBit;
            return Result(
                ExtF80.FromBits(SignExponent(sign, 0x7fff), significand),
                signaling ? FloatingPointExceptionFlags.Invalid : FloatingPointExceptionFlags.None);
        }

        if (exponent == 0)
        {
            if (fraction == 0)
            {
                return Result(sign ? ExtF80.NegativeZero : ExtF80.PositiveZero);
            }

            var shift = BitOperations.LeadingZeroCount(fraction) - 11;
            var normalized = fraction << shift;
            return Result(Pack(sign, -1022 - shift, normalized << 11));
        }

        return Result(Pack(sign, exponent - 1023, ExtF80.IntegerBit | (fraction << 11)));
    }

    /// <summary>Rounds an extended value according to the supplied context.</summary>
    public static FloatingPointResult<ExtF80> Round(ExtF80 value, ExtF80Context context)
    {
        if (IsNormal(value))
        {
            return RoundPack(
                value.Sign,
                value.BiasedExponent - ExtF80.ExponentBias,
                (UInt128)value.Significand << 3,
                context);
        }

        if (TryPropagateNaN(value, out var special))
        {
            return special;
        }

        if (value.Classification is ExtF80Class.Zero or ExtF80Class.Infinity)
        {
            return Result(value);
        }

        var unpacked = UnpackFinite(value);
        return RoundPack(unpacked.Sign, unpacked.Exponent, (UInt128)unpacked.Significand << 3, context);
    }

    /// <summary>Adds two extended values.</summary>
    public static FloatingPointResult<ExtF80> Add(ExtF80 left, ExtF80 right, ExtF80Context context)
        => ExtF80HostMath.TryBinary(left, right, context, ExtF80HostOperation.Add, out var result)
            ? result
            : AddReference(left, right, context);

    internal static FloatingPointResult<ExtF80> AddReference(
        ExtF80 left,
        ExtF80 right,
        ExtF80Context context)
        => AddMagnitudes(left, right, subtractRight: false, context);

    /// <summary>Subtracts the right extended value from the left.</summary>
    public static FloatingPointResult<ExtF80> Subtract(ExtF80 left, ExtF80 right, ExtF80Context context)
        => ExtF80HostMath.TryBinary(left, right, context, ExtF80HostOperation.Subtract, out var result)
            ? result
            : SubtractReference(left, right, context);

    internal static FloatingPointResult<ExtF80> SubtractReference(
        ExtF80 left,
        ExtF80 right,
        ExtF80Context context)
        => AddMagnitudes(left, right, subtractRight: true, context);

    /// <summary>Multiplies two extended values.</summary>
    public static FloatingPointResult<ExtF80> Multiply(ExtF80 left, ExtF80 right, ExtF80Context context)
        => ExtF80HostMath.TryBinary(left, right, context, ExtF80HostOperation.Multiply, out var result)
            ? result
            : MultiplyReference(left, right, context);

    internal static FloatingPointResult<ExtF80> MultiplyReference(
        ExtF80 left,
        ExtF80 right,
        ExtF80Context context)
    {
        if (IsNormal(left) && IsNormal(right))
        {
            return MultiplyFinite(left, right, context);
        }

        if (TryPropagateNaN(left, right, out var special))
        {
            return special;
        }

        var leftClass = left.Classification;
        var rightClass = right.Classification;
        var sign = left.Sign ^ right.Sign;
        if ((leftClass == ExtF80Class.Infinity && rightClass == ExtF80Class.Zero) ||
            (rightClass == ExtF80Class.Infinity && leftClass == ExtF80Class.Zero))
        {
            return InvalidResult();
        }

        if (leftClass == ExtF80Class.Infinity || rightClass == ExtF80Class.Infinity)
        {
            return Result(sign ? ExtF80.NegativeInfinity : ExtF80.PositiveInfinity);
        }

        if (leftClass == ExtF80Class.Zero || rightClass == ExtF80Class.Zero)
        {
            return Result(sign ? ExtF80.NegativeZero : ExtF80.PositiveZero);
        }

        return MultiplyFinite(left, right, context);
    }

    private static FloatingPointResult<ExtF80> MultiplyFinite(
        ExtF80 left,
        ExtF80 right,
        ExtF80Context context)
    {
        var a = UnpackFinite(left);
        var b = UnpackFinite(right);
        var sign = a.Sign ^ b.Sign;
        var product = (UInt128)a.Significand * b.Significand;
        var carry = (product & ((UInt128)1 << 127)) != 0;
        var exponent = a.Exponent + b.Exponent + (carry ? 1 : 0);
        var significand = ShiftRightJam(product, carry ? 61 : 60);
        return RoundPack(sign, exponent, significand, context);
    }

    /// <summary>Divides the left extended value by the right.</summary>
    public static FloatingPointResult<ExtF80> Divide(ExtF80 left, ExtF80 right, ExtF80Context context)
        => ExtF80HostMath.TryBinary(left, right, context, ExtF80HostOperation.Divide, out var result)
            ? result
            : DivideReference(left, right, context);

    /// <summary>Executes the software reference division path.</summary>
    public static FloatingPointResult<ExtF80> DivideReference(
        ExtF80 left,
        ExtF80 right,
        ExtF80Context context)
    {
        if (IsNormal(left) && IsNormal(right))
        {
            return DivideFinite(left, right, context);
        }

        if (TryPropagateNaN(left, right, out var special))
        {
            return special;
        }

        var leftClass = left.Classification;
        var rightClass = right.Classification;
        var sign = left.Sign ^ right.Sign;
        if ((leftClass == ExtF80Class.Zero && rightClass == ExtF80Class.Zero) ||
            (leftClass == ExtF80Class.Infinity && rightClass == ExtF80Class.Infinity))
        {
            return InvalidResult();
        }

        if (leftClass == ExtF80Class.Infinity)
        {
            return Result(sign ? ExtF80.NegativeInfinity : ExtF80.PositiveInfinity);
        }

        if (rightClass == ExtF80Class.Infinity || leftClass == ExtF80Class.Zero)
        {
            return Result(sign ? ExtF80.NegativeZero : ExtF80.PositiveZero);
        }

        if (rightClass == ExtF80Class.Zero)
        {
            return Result(
                sign ? ExtF80.NegativeInfinity : ExtF80.PositiveInfinity,
                FloatingPointExceptionFlags.DivideByZero);
        }

        return DivideFinite(left, right, context);
    }

    private static FloatingPointResult<ExtF80> DivideFinite(
        ExtF80 left,
        ExtF80 right,
        ExtF80Context context)
    {
        var a = UnpackFinite(left);
        var b = UnpackFinite(right);
        var sign = a.Sign ^ b.Sign;
        var numerator = (UInt128)a.Significand;
        var denominator = (UInt128)b.Significand;
        var exponent = a.Exponent - b.Exponent;
        if (numerator < denominator)
        {
            numerator <<= 1;
            exponent--;
        }

        var remainder = numerator - denominator;
        var division = UInt128.DivRem(remainder << 64, denominator);
        var quotient = ExtraIntegerBit | (division.Quotient << 2);
        remainder = division.Remainder << 1;
        if (remainder >= denominator)
        {
            quotient |= 2;
            remainder -= denominator;
        }

        if (remainder != 0)
        {
            quotient |= 1;
        }

        return RoundPack(sign, exponent, quotient, context);
    }

    /// <summary>Computes the square root of an extended value.</summary>
    public static FloatingPointResult<ExtF80> SquareRoot(ExtF80 value, ExtF80Context context)
    {
        if (IsNormal(value) && !value.Sign)
        {
            if (context.Precision is ExtF80Precision.Single or ExtF80Precision.Double &&
                ExtF80HostMath.TrySquareRoot(value, context, out var accelerated))
            {
                return accelerated;
            }

            return SquareRootFinite(value, context);
        }

        if (TryPropagateNaN(value, out var special))
        {
            return special;
        }

        if (value.Classification == ExtF80Class.Infinity)
        {
            return value.Sign ? InvalidResult() : Result(value);
        }

        if (value.Classification == ExtF80Class.Zero)
        {
            return Result(value);
        }

        if (value.Sign)
        {
            return InvalidResult();
        }

        return SquareRootFinite(value, context);
    }

    internal static FloatingPointResult<ExtF80> SquareRootReference(ExtF80 value, ExtF80Context context)
    {
        if (IsNormal(value) && !value.Sign)
        {
            return SquareRootFiniteReference(value, context);
        }

        if (TryPropagateNaN(value, out var special))
        {
            return special;
        }

        if (value.Classification == ExtF80Class.Infinity)
        {
            return value.Sign ? InvalidResult() : Result(value);
        }

        if (value.Classification == ExtF80Class.Zero)
        {
            return Result(value);
        }

        if (value.Sign)
        {
            return InvalidResult();
        }

        return SquareRootFiniteReference(value, context);
    }

    private static FloatingPointResult<ExtF80> SquareRootFinite(
        ExtF80 value,
        ExtF80Context context)
    {
        var unpacked = UnpackFinite(value);
        var doubled = (unpacked.Exponent & 1) != 0;
        var exponent = doubled ? (unpacked.Exponent - 1) / 2 : unpacked.Exponent / 2;
        var radicand = (UInt128)unpacked.Significand << (doubled ? 64 : 63);
        var root = IntegerSquareRoot(radicand);
        var remainder = radicand - (root * root);
        for (var digit = 0; digit < 2; digit++)
        {
            remainder <<= 2;
            var trial = (root << 2) | (UInt128)1;
            root <<= 1;
            if (remainder >= trial)
            {
                remainder -= trial;
                root |= 1;
            }
        }

        var significand = root << 1;
        if (remainder != 0)
        {
            significand |= 1;
        }

        return RoundPack(false, exponent, significand, context);
    }

    private static FloatingPointResult<ExtF80> SquareRootFiniteReference(
        ExtF80 value,
        ExtF80Context context)
    {
        var unpacked = UnpackFinite(value);
        var doubled = (unpacked.Exponent & 1) != 0;
        var exponent = doubled ? (unpacked.Exponent - 1) / 2 : unpacked.Exponent / 2;
        var radicand = (UInt128)unpacked.Significand << (doubled ? 64 : 63);
        var root = IntegerSquareRootReference(radicand);
        var remainder = radicand - (root * root);
        for (var digit = 0; digit < 2; digit++)
        {
            remainder <<= 2;
            var trial = (root << 2) | (UInt128)1;
            root <<= 1;
            if (remainder >= trial)
            {
                remainder -= trial;
                root |= 1;
            }
        }

        var significand = root << 1;
        if (remainder != 0)
        {
            significand |= 1;
        }

        return RoundPack(false, exponent, significand, context);
    }

    /// <summary>Returns the absolute value, quieting a signaling NaN.</summary>
    public static FloatingPointResult<ExtF80> Absolute(ExtF80 value)
    {
        if (TryPropagateNaN(value, out var special))
        {
            return special with { Value = WithSign(special.Value, false) };
        }

        return Result(WithSign(value, false));
    }

    /// <summary>Negates a value, quieting a signaling NaN.</summary>
    public static FloatingPointResult<ExtF80> Negate(ExtF80 value)
    {
        if (TryPropagateNaN(value, out var special))
        {
            return special with { Value = WithSign(special.Value, !special.Value.Sign) };
        }

        return Result(WithSign(value, !value.Sign));
    }

    /// <summary>Rounds a value to an integral extended value.</summary>
    public static FloatingPointResult<ExtF80> RoundToInteger(ExtF80 value, ExtF80Context context)
    {
        if (TryPropagateNaN(value, out var special))
        {
            return special;
        }

        if (value.Classification is ExtF80Class.Zero or ExtF80Class.Infinity)
        {
            return Result(value);
        }

        var unpacked = UnpackFinite(value);
        if (unpacked.Exponent >= 63)
        {
            return Result(value);
        }

        if (unpacked.Exponent < 0)
        {
            var incrementSmall = context.RoundingMode switch
            {
                ExtF80RoundingMode.ToNearestEven => unpacked.Exponent == -1 && unpacked.Significand > ExtF80.IntegerBit,
                ExtF80RoundingMode.TowardNegativeInfinity => unpacked.Sign,
                ExtF80RoundingMode.TowardPositiveInfinity => !unpacked.Sign,
                _ => false
            };
            return Result(
                incrementSmall ? Pack(unpacked.Sign, 0, ExtF80.IntegerBit) : (unpacked.Sign ? ExtF80.NegativeZero : ExtF80.PositiveZero),
                FloatingPointExceptionFlags.Inexact);
        }

        var fractionalBits = 63 - unpacked.Exponent;
        var fractionalMask = (1UL << fractionalBits) - 1;
        var remainder = unpacked.Significand & fractionalMask;
        if (remainder == 0)
        {
            return Result(value);
        }

        var significand = unpacked.Significand & ~fractionalMask;
        var half = 1UL << (fractionalBits - 1);
        var increment = ShouldIncrement(
            unpacked.Sign,
            context.RoundingMode,
            remainder,
            half,
            (significand >> fractionalBits) != 0 && ((significand >> fractionalBits) & 1) != 0);
        var exponent = unpacked.Exponent;
        if (increment)
        {
            var unit = 1UL << fractionalBits;
            var previous = significand;
            significand += unit;
            if (significand < previous)
            {
                significand = ExtF80.IntegerBit;
                exponent++;
            }
        }

        return Result(Pack(unpacked.Sign, exponent, significand), FloatingPointExceptionFlags.Inexact);
    }

    /// <summary>Compares two values without ordering NaNs.</summary>
    public static FloatingPointResult<ExtF80Comparison> Compare(ExtF80 left, ExtF80 right)
    {
        var leftClass = left.Classification;
        var rightClass = right.Classification;
        if (leftClass == ExtF80Class.Unsupported || rightClass == ExtF80Class.Unsupported)
        {
            return new(ExtF80Comparison.Unordered, FloatingPointExceptionFlags.Invalid);
        }

        if (IsNaN(leftClass) || IsNaN(rightClass))
        {
            var flags = leftClass == ExtF80Class.SignalingNaN || rightClass == ExtF80Class.SignalingNaN
                ? FloatingPointExceptionFlags.Invalid
                : FloatingPointExceptionFlags.None;
            return new(ExtF80Comparison.Unordered, flags);
        }

        if (leftClass == ExtF80Class.Zero && rightClass == ExtF80Class.Zero)
        {
            return new(ExtF80Comparison.Equal, FloatingPointExceptionFlags.None);
        }

        if (left.Sign != right.Sign)
        {
            return new(left.Sign ? ExtF80Comparison.Less : ExtF80Comparison.Greater, FloatingPointExceptionFlags.None);
        }

        var magnitude = CompareMagnitude(left, right);
        if (left.Sign)
        {
            magnitude = -magnitude;
        }

        return new(
            magnitude < 0 ? ExtF80Comparison.Less : magnitude > 0 ? ExtF80Comparison.Greater : ExtF80Comparison.Equal,
            FloatingPointExceptionFlags.None);
    }

    /// <summary>Converts to a raw IEEE binary32 encoding.</summary>
    public static FloatingPointResult<uint> ToBinary32Bits(ExtF80 value, ExtF80RoundingMode roundingMode = ExtF80RoundingMode.ToNearestEven,
        ExtF80TininessMode tininessMode = ExtF80TininessMode.AfterRounding)
    {
        var result = ToBinaryBits(value, 24, 8, 127, roundingMode, tininessMode);
        return new((uint)result.Value, result.Flags);
    }

    /// <summary>Converts to a raw IEEE binary64 encoding.</summary>
    public static FloatingPointResult<ulong> ToBinary64Bits(ExtF80 value, ExtF80RoundingMode roundingMode = ExtF80RoundingMode.ToNearestEven,
        ExtF80TininessMode tininessMode = ExtF80TininessMode.AfterRounding)
        => ToBinaryBits(value, 53, 11, 1023, roundingMode, tininessMode);

    /// <summary>Converts to a host <see cref="double"/> through its raw binary64 encoding.</summary>
    public static FloatingPointResult<double> ToDouble(ExtF80 value, ExtF80RoundingMode roundingMode = ExtF80RoundingMode.ToNearestEven)
    {
        var result = ToBinary64Bits(value, roundingMode);
        return new(BitConverter.Int64BitsToDouble(unchecked((long)result.Value)), result.Flags);
    }

    /// <summary>Converts to a signed byte using the supplied rounding mode.</summary>
    public static FloatingPointResult<sbyte> ToInt8(ExtF80 value, ExtF80RoundingMode roundingMode)
    {
        var result = ToSignedInteger(value, 8, roundingMode);
        return new((sbyte)result.Value, result.Flags);
    }

    /// <summary>Converts to a signed 16-bit integer using the supplied rounding mode.</summary>
    public static FloatingPointResult<short> ToInt16(ExtF80 value, ExtF80RoundingMode roundingMode)
    {
        var result = ToSignedInteger(value, 16, roundingMode);
        return new((short)result.Value, result.Flags);
    }

    /// <summary>Converts to a signed 32-bit integer using the supplied rounding mode.</summary>
    public static FloatingPointResult<int> ToInt32(ExtF80 value, ExtF80RoundingMode roundingMode)
    {
        var result = ToSignedInteger(value, 32, roundingMode);
        return new((int)result.Value, result.Flags);
    }

    /// <summary>Converts to a signed 64-bit integer using the supplied rounding mode.</summary>
    public static FloatingPointResult<long> ToInt64(ExtF80 value, ExtF80RoundingMode roundingMode)
        => ToSignedInteger(value, 64, roundingMode);

    private static FloatingPointResult<ExtF80> AddMagnitudes(
        ExtF80 left,
        ExtF80 right,
        bool subtractRight,
        ExtF80Context context)
    {
        var rightSign = right.Sign ^ subtractRight;
        if (IsNormal(left) && IsNormal(right))
        {
            return AddFinite(left, right, rightSign, context);
        }

        if (TryPropagateNaN(left, right, out var special))
        {
            return special;
        }

        var leftClass = left.Classification;
        var rightClass = right.Classification;
        if (leftClass == ExtF80Class.Infinity || rightClass == ExtF80Class.Infinity)
        {
            if (leftClass == ExtF80Class.Infinity && rightClass == ExtF80Class.Infinity && left.Sign != rightSign)
            {
                return InvalidResult();
            }

            var infinitySign = leftClass == ExtF80Class.Infinity ? left.Sign : rightSign;
            return Result(infinitySign ? ExtF80.NegativeInfinity : ExtF80.PositiveInfinity);
        }

        if (leftClass == ExtF80Class.Zero && rightClass == ExtF80Class.Zero)
        {
            var zeroSign = left.Sign == rightSign
                ? left.Sign
                : context.RoundingMode == ExtF80RoundingMode.TowardNegativeInfinity;
            return Result(zeroSign ? ExtF80.NegativeZero : ExtF80.PositiveZero);
        }

        if (leftClass == ExtF80Class.Zero)
        {
            var b = UnpackFinite(right);
            return RoundPack(rightSign, b.Exponent, (UInt128)b.Significand << 3, context);
        }

        if (rightClass == ExtF80Class.Zero)
        {
            var a = UnpackFinite(left);
            return RoundPack(a.Sign, a.Exponent, (UInt128)a.Significand << 3, context);
        }

        return AddFinite(left, right, rightSign, context);
    }

    private static FloatingPointResult<ExtF80> AddFinite(
        ExtF80 left,
        ExtF80 right,
        bool rightSign,
        ExtF80Context context)
    {
        var first = UnpackFinite(left);
        var second = UnpackFinite(right) with { Sign = rightSign };
        if (first.Exponent < second.Exponent)
        {
            (first, second) = (second, first);
        }

        var exponent = first.Exponent;
        var firstSignificand = (UInt128)first.Significand << 3;
        var secondSignificand = ShiftRightJam((UInt128)second.Significand << 3, exponent - second.Exponent);
        if (first.Sign == second.Sign)
        {
            var sum = firstSignificand + secondSignificand;
            if ((sum & ExtraCarryBit) != 0)
            {
                sum = ShiftRightJam(sum, 1);
                exponent++;
            }

            return RoundPack(first.Sign, exponent, sum, context);
        }

        var sign = first.Sign;
        UInt128 difference;
        if (firstSignificand >= secondSignificand)
        {
            difference = firstSignificand - secondSignificand;
        }
        else
        {
            difference = secondSignificand - firstSignificand;
            sign = second.Sign;
        }

        if (difference == 0)
        {
            sign = context.RoundingMode == ExtF80RoundingMode.TowardNegativeInfinity;
            return Result(sign ? ExtF80.NegativeZero : ExtF80.PositiveZero);
        }

        var shift = LeadingZeroCount(difference) - 61;
        difference <<= shift;
        exponent -= shift;
        return RoundPack(sign, exponent, difference, context);
    }

    private static FloatingPointResult<ExtF80> RoundPack(
        bool sign,
        int exponent,
        UInt128 significand,
        ExtF80Context context,
        FloatingPointExceptionFlags flags = FloatingPointExceptionFlags.None)
    {
        if (significand == 0)
        {
            return Result(sign ? ExtF80.NegativeZero : ExtF80.PositiveZero, flags);
        }

        if ((significand & ExtraCarryBit) != 0)
        {
            significand = ShiftRightJam(significand, 1);
            exponent++;
        }

        if ((significand & ExtraIntegerBit) == 0)
        {
            var shift = LeadingZeroCount(significand) - 61;
            significand <<= shift;
            exponent -= shift;
        }

        var tinyBeforeRounding = exponent < MinimumExponent;
        if (tinyBeforeRounding)
        {
            significand = ShiftRightJam(significand, MinimumExponent - exponent);
            exponent = MinimumExponent;
        }

        var precision = ValidatePrecision(context.Precision);
        var discardedBits = 64 - precision + 3;
        var mask = ((UInt128)1 << discardedBits) - 1;
        var remainder = significand & mask;
        var rounded = significand >> discardedBits;
        var half = (UInt128)1 << (discardedBits - 1);
        if (ShouldIncrement(sign, context.RoundingMode, remainder, half, (rounded & 1) != 0))
        {
            rounded++;
            if (rounded == ((UInt128)1 << precision))
            {
                rounded >>= 1;
                exponent++;
            }
        }

        var inexact = remainder != 0;
        if (exponent > MaximumExponent)
        {
            flags |= FloatingPointExceptionFlags.Overflow | FloatingPointExceptionFlags.Inexact;
            return Result(OverflowResult(sign, context.RoundingMode, precision), flags);
        }

        var packedSignificand = (ulong)(rounded << (64 - precision));
        var subnormal = exponent == MinimumExponent && (packedSignificand & ExtF80.IntegerBit) == 0;
        if (inexact)
        {
            flags |= FloatingPointExceptionFlags.Inexact;
            var tiny = context.TininessMode == ExtF80TininessMode.BeforeRounding
                ? tinyBeforeRounding
                : subnormal;
            if (tiny)
            {
                flags |= FloatingPointExceptionFlags.Underflow;
            }
        }

        if (packedSignificand == 0)
        {
            return Result(sign ? ExtF80.NegativeZero : ExtF80.PositiveZero, flags);
        }

        var biasedExponent = subnormal ? 0 : exponent + ExtF80.ExponentBias;
        return Result(ExtF80.FromBits(SignExponent(sign, biasedExponent), packedSignificand), flags);
    }

    private static FloatingPointResult<ulong> ToBinaryBits(
        ExtF80 value,
        int precision,
        int exponentBits,
        int exponentBias,
        ExtF80RoundingMode roundingMode,
        ExtF80TininessMode tininessMode)
    {
        var fractionBits = precision - 1;
        var signBit = value.Sign ? 1UL << (fractionBits + exponentBits) : 0;
        var maximumExponentField = (1UL << exponentBits) - 1;
        var classification = value.Classification;
        if (classification == ExtF80Class.Unsupported)
        {
            var quiet = 1UL << (fractionBits - 1);
            return new(signBit | (maximumExponentField << fractionBits) | quiet, FloatingPointExceptionFlags.Invalid);
        }

        if (classification == ExtF80Class.Infinity)
        {
            return new(signBit | (maximumExponentField << fractionBits), FloatingPointExceptionFlags.None);
        }

        if (IsNaN(classification))
        {
            var nanFraction = (value.Significand & ExtF80.FractionMask) >> (63 - fractionBits);
            nanFraction |= 1UL << (fractionBits - 1);
            return new(
                signBit | (maximumExponentField << fractionBits) | nanFraction,
                classification == ExtF80Class.SignalingNaN ? FloatingPointExceptionFlags.Invalid : FloatingPointExceptionFlags.None);
        }

        if (classification == ExtF80Class.Zero)
        {
            return new(signBit, FloatingPointExceptionFlags.None);
        }

        var unpacked = UnpackFinite(value);
        var minimumExponent = 1 - exponentBias;
        var maximumExponent = (int)maximumExponentField - 1 - exponentBias;
        var significand = (UInt128)unpacked.Significand << 3;
        var tinyBefore = unpacked.Exponent < minimumExponent;
        var exponent = unpacked.Exponent;
        if (tinyBefore)
        {
            significand = ShiftRightJam(significand, minimumExponent - exponent);
            exponent = minimumExponent;
        }

        var discardedBits = 64 - precision + 3;
        var mask = ((UInt128)1 << discardedBits) - 1;
        var remainder = significand & mask;
        var rounded = significand >> discardedBits;
        var half = (UInt128)1 << (discardedBits - 1);
        if (ShouldIncrement(value.Sign, roundingMode, remainder, half, (rounded & 1) != 0))
        {
            rounded++;
            if (rounded == ((UInt128)1 << precision))
            {
                rounded >>= 1;
                exponent++;
            }
        }

        var flags = remainder != 0 ? FloatingPointExceptionFlags.Inexact : FloatingPointExceptionFlags.None;
        if (exponent > maximumExponent)
        {
            flags |= FloatingPointExceptionFlags.Overflow | FloatingPointExceptionFlags.Inexact;
            var toInfinity = OverflowToInfinity(value.Sign, roundingMode);
            var magnitude = toInfinity
                ? maximumExponentField << fractionBits
                : ((maximumExponentField - 1) << fractionBits) | ((1UL << fractionBits) - 1);
            return new(signBit | magnitude, flags);
        }

        var hiddenBit = (UInt128)1 << (precision - 1);
        var subnormal = exponent == minimumExponent && (rounded & hiddenBit) == 0;
        if (remainder != 0 && (tininessMode == ExtF80TininessMode.BeforeRounding ? tinyBefore : subnormal))
        {
            flags |= FloatingPointExceptionFlags.Underflow;
        }

        var exponentField = subnormal ? 0UL : (ulong)(exponent + exponentBias);
        var fractionMask = hiddenBit - 1;
        var fraction = (ulong)(rounded & fractionMask);
        return new(signBit | (exponentField << fractionBits) | fraction, flags);
    }

    private static FloatingPointResult<long> ToSignedInteger(ExtF80 value, int width, ExtF80RoundingMode roundingMode)
    {
        var classification = value.Classification;
        var indefinite = width == 64 ? long.MinValue : -(1L << (width - 1));
        if (classification is ExtF80Class.Unsupported or ExtF80Class.Infinity or ExtF80Class.QuietNaN or ExtF80Class.SignalingNaN)
        {
            return new(indefinite, FloatingPointExceptionFlags.Invalid);
        }

        var rounded = RoundToInteger(
            value,
            new ExtF80Context(roundingMode, ExtF80Precision.Extended, ExtF80TininessMode.AfterRounding));
        if (rounded.Value.Classification == ExtF80Class.Zero)
        {
            return new(0, rounded.Flags);
        }

        var unpacked = UnpackFinite(rounded.Value);
        if (unpacked.Exponent > 63)
        {
            return new(indefinite, FloatingPointExceptionFlags.Invalid);
        }

        var magnitude = unpacked.Significand >> (63 - unpacked.Exponent);
        var negativeLimit = width == 64 ? 0x8000_0000_0000_0000UL : 1UL << (width - 1);
        var positiveLimit = negativeLimit - 1;
        if ((!unpacked.Sign && magnitude > positiveLimit) || (unpacked.Sign && magnitude > negativeLimit))
        {
            return new(indefinite, FloatingPointExceptionFlags.Invalid);
        }

        var converted = unpacked.Sign
            ? magnitude == 0x8000_0000_0000_0000UL ? long.MinValue : -(long)magnitude
            : (long)magnitude;
        return new(converted, rounded.Flags);
    }

    private static bool TryPropagateNaN(ExtF80 value, out FloatingPointResult<ExtF80> result)
    {
        var classification = value.Classification;
        if (classification == ExtF80Class.Unsupported)
        {
            result = InvalidResult();
            return true;
        }

        if (IsNaN(classification))
        {
            result = Result(
                Quiet(value),
                classification == ExtF80Class.SignalingNaN ? FloatingPointExceptionFlags.Invalid : FloatingPointExceptionFlags.None);
            return true;
        }

        result = default;
        return false;
    }

    private static bool TryPropagateNaN(ExtF80 left, ExtF80 right, out FloatingPointResult<ExtF80> result)
    {
        var leftClass = left.Classification;
        var rightClass = right.Classification;
        if (leftClass == ExtF80Class.Unsupported || rightClass == ExtF80Class.Unsupported)
        {
            result = InvalidResult();
            return true;
        }

        var leftNaN = IsNaN(leftClass);
        var rightNaN = IsNaN(rightClass);
        if (!leftNaN && !rightNaN)
        {
            result = default;
            return false;
        }

        var flags = leftClass == ExtF80Class.SignalingNaN || rightClass == ExtF80Class.SignalingNaN
            ? FloatingPointExceptionFlags.Invalid
            : FloatingPointExceptionFlags.None;
        ExtF80 selected;
        if (!leftNaN)
        {
            selected = right;
        }
        else if (!rightNaN)
        {
            selected = left;
        }
        else
        {
            var leftPayload = left.Significand & ExtF80.FractionMask;
            var rightPayload = right.Significand & ExtF80.FractionMask;
            selected = rightPayload > leftPayload ? right : left;
        }

        result = Result(Quiet(selected), flags);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static UInt128 ShiftRightJam(UInt128 value, int distance)
    {
        if (distance <= 0)
        {
            return value;
        }

        if (distance >= 128)
        {
            return value == 0 ? (UInt128)0 : 1;
        }

        var mask = ((UInt128)1 << distance) - 1;
        return (value >> distance) | ((value & mask) != 0 ? (UInt128)1 : 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ShouldIncrement(
        bool sign,
        ExtF80RoundingMode mode,
        UInt128 remainder,
        UInt128 half,
        bool odd)
    {
        if (remainder == 0)
        {
            return false;
        }

        return mode switch
        {
            ExtF80RoundingMode.ToNearestEven => remainder > half || (remainder == half && odd),
            ExtF80RoundingMode.TowardNegativeInfinity => sign,
            ExtF80RoundingMode.TowardPositiveInfinity => !sign,
            _ => false
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ShouldIncrement(
        bool sign,
        ExtF80RoundingMode mode,
        ulong remainder,
        ulong half,
        bool odd)
        => ShouldIncrement(sign, mode, (UInt128)remainder, half, odd);

    private static UInt128 IntegerSquareRoot(UInt128 value)
    {
        var root = (UInt128)Math.Sqrt((double)value);
        root = (root + (value / root)) >> 1;
        while (root * root > value)
        {
            root--;
        }

        return root;
    }

    private static UInt128 IntegerSquareRootReference(UInt128 value)
    {
        var bitLength = 128 - LeadingZeroCount(value);
        var root = (UInt128)1 << ((bitLength + 1) / 2);
        while (true)
        {
            var next = (root + (value / root)) >> 1;
            if (next >= root)
            {
                return root;
            }

            root = next;
        }
    }

    private static int CompareMagnitude(ExtF80 left, ExtF80 right)
    {
        if (left.BiasedExponent != right.BiasedExponent)
        {
            return left.BiasedExponent < right.BiasedExponent ? -1 : 1;
        }

        return left.Significand < right.Significand ? -1 : left.Significand > right.Significand ? 1 : 0;
    }

    private static ExtF80 OverflowResult(bool sign, ExtF80RoundingMode mode, int precision)
    {
        if (OverflowToInfinity(sign, mode))
        {
            return sign ? ExtF80.NegativeInfinity : ExtF80.PositiveInfinity;
        }

        var significand = precision == 64
            ? ulong.MaxValue
            : (ulong)(((UInt128.One << precision) - 1) << (64 - precision));
        return ExtF80.FromBits(SignExponent(sign, 0x7ffe), significand);
    }

    private static bool OverflowToInfinity(bool sign, ExtF80RoundingMode mode)
        => mode == ExtF80RoundingMode.ToNearestEven ||
            (mode == ExtF80RoundingMode.TowardPositiveInfinity && !sign) ||
            (mode == ExtF80RoundingMode.TowardNegativeInfinity && sign);

    private static int ValidatePrecision(ExtF80Precision precision)
        => precision switch
        {
            ExtF80Precision.Single => 24,
            ExtF80Precision.Double => 53,
            ExtF80Precision.Extended => 64,
            _ => throw new ArgumentOutOfRangeException(nameof(precision))
        };

    private static int LeadingZeroCount(UInt128 value)
    {
        var high = (ulong)(value >> 64);
        return high != 0
            ? BitOperations.LeadingZeroCount(high)
            : 64 + BitOperations.LeadingZeroCount((ulong)value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsNormal(ExtF80 value)
    {
        var exponent = value.SignExponent & 0x7fff;
        return exponent is > 0 and < 0x7fff &&
            (value.Significand & ExtF80.IntegerBit) != 0;
    }

    private static Unpacked UnpackFinite(ExtF80 value)
    {
        var exponent = value.BiasedExponent;
        var significand = value.Significand;
        if (exponent != 0)
        {
            return new(value.Sign, exponent - ExtF80.ExponentBias, significand);
        }

        var shift = BitOperations.LeadingZeroCount(significand);
        return new(value.Sign, MinimumExponent - shift, significand << shift);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ExtF80 Pack(bool sign, int exponent, ulong significand)
        => ExtF80.FromBits(SignExponent(sign, exponent + ExtF80.ExponentBias), significand);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort SignExponent(bool sign, int exponent)
        => (ushort)((sign ? 0x8000 : 0) | exponent);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ExtF80 WithSign(ExtF80 value, bool sign)
        => ExtF80.FromBits(SignExponent(sign, value.BiasedExponent), value.Significand);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ExtF80 Quiet(ExtF80 value)
        => ExtF80.FromBits(value.SignExponent, value.Significand | ExtF80.IntegerBit | ExtF80.QuietBit);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsNaN(ExtF80Class classification)
        => classification is ExtF80Class.QuietNaN or ExtF80Class.SignalingNaN;

    private static FloatingPointResult<ExtF80> InvalidResult()
        => Result(ExtF80.QuietNaN, FloatingPointExceptionFlags.Invalid);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static FloatingPointResult<ExtF80> Result(
        ExtF80 value,
        FloatingPointExceptionFlags flags = FloatingPointExceptionFlags.None)
        => new(value, flags);

    private readonly record struct Unpacked(bool Sign, int Exponent, ulong Significand);
}
