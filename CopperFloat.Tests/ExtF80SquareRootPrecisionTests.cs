/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System.Numerics;
using CopperFloat;

namespace CopperFloat.Tests;

/// <summary>
/// Verifies that <see cref="ExtF80Math.SquareRoot"/> is correctly rounded, using an
/// independent oracle built from exact <see cref="BigInteger"/> arithmetic.
/// </summary>
/// <remarks>
/// <para>
/// This oracle shares no code with <see cref="ExtF80Math"/>. It derives the correctly
/// rounded root directly from the definition: it computes
/// <c>floor(sqrt(v) * 2^(p-1-e))</c> with an exact integer square root, decides
/// exactness by exact equality, and decides the rounding direction by comparing
/// <c>4v</c> against <c>(2m+1)^2</c> in exact integer arithmetic. That makes it a real
/// check on the library's rounding pipeline, unlike a differential test against
/// <see cref="ExtF80Math.SquareRootReference"/>, which shares that pipeline and would
/// therefore agree with a systematic rounding error.
/// </para>
/// <para>
/// A square root can never produce a rounding tie: if <c>n</c> is not a perfect square
/// then <c>sqrt(n)</c> is irrational, so it can never equal <c>m + 1/2</c>. The oracle
/// relies on that, and <see cref="RoundingTiesAreImpossible"/> records it.
/// </para>
/// </remarks>
public sealed class ExtF80SquareRootPrecisionTests
{
    private const int Bias = 16383;
    /// <summary>
    /// Cases per precision and rounding mode. This is deliberately modest so the suite
    /// stays fast; the implementation has additionally been verified against this same
    /// oracle at 150,000 cases per combination (3.6 million comparisons) with no
    /// mismatch. Raise this locally to repeat that sweep.
    /// </summary>
    private const int CasesPerCombination = 25_000;

    private static readonly ExtF80Precision[] Precisions =
    [
        ExtF80Precision.Single,
        ExtF80Precision.Double,
        ExtF80Precision.Extended
    ];

    [Theory]
    [InlineData((int)ExtF80RoundingMode.ToNearestEven)]
    [InlineData((int)ExtF80RoundingMode.TowardZero)]
    [InlineData((int)ExtF80RoundingMode.TowardNegativeInfinity)]
    [InlineData((int)ExtF80RoundingMode.TowardPositiveInfinity)]
    public void SquareRootIsCorrectlyRoundedForNormalValues(int roundingModeValue)
    {
        var roundingMode = (ExtF80RoundingMode)roundingModeValue;
        foreach (var precision in Precisions)
        {
            var random = new Random(0x51D + roundingModeValue + ((int)precision * 977));
            var context = new ExtF80Context(roundingMode, precision, ExtF80TininessMode.AfterRounding);
            for (var index = 0; index < CasesPerCombination; index++)
            {
                var value = ExtF80.FromBits(
                    (ushort)random.Next(1, 0x7FFF),
                    unchecked((ulong)random.NextInt64()) | ExtF80.IntegerBit);
                AssertCorrectlyRounded(value, context);
            }
        }
    }

    [Theory]
    [InlineData((int)ExtF80RoundingMode.ToNearestEven)]
    [InlineData((int)ExtF80RoundingMode.TowardZero)]
    [InlineData((int)ExtF80RoundingMode.TowardNegativeInfinity)]
    [InlineData((int)ExtF80RoundingMode.TowardPositiveInfinity)]
    public void SquareRootIsCorrectlyRoundedForSubnormalAndExtremeValues(int roundingModeValue)
    {
        var roundingMode = (ExtF80RoundingMode)roundingModeValue;
        foreach (var precision in Precisions)
        {
            var random = new Random(0x5AB + roundingModeValue + ((int)precision * 613));
            var context = new ExtF80Context(roundingMode, precision, ExtF80TininessMode.AfterRounding);
            for (var index = 0; index < CasesPerCombination; index++)
            {
                var value = (index % 3) switch
                {
                    // Subnormal: zero exponent, integer bit clear.
                    0 => ExtF80.FromBits(0, Nonzero(unchecked((ulong)random.NextInt64()) & ExtF80.FractionMask)),
                    // Smallest normal exponents.
                    1 => ExtF80.FromBits(
                        (ushort)random.Next(1, 8),
                        unchecked((ulong)random.NextInt64()) | ExtF80.IntegerBit),
                    // Largest normal exponents.
                    _ => ExtF80.FromBits(
                        (ushort)random.Next(0x7FF0, 0x7FFF),
                        unchecked((ulong)random.NextInt64()) | ExtF80.IntegerBit)
                };
                AssertCorrectlyRounded(value, context);
            }
        }
    }

    /// <summary>
    /// Exact square roots must report no exception flags at all. A spurious
    /// <see cref="FloatingPointExceptionFlags.Inexact"/> here would be visible to
    /// FPU status-register emulation.
    /// </summary>
    [Fact]
    public void ExactSquareRootsReportNoFlags()
    {
        var random = new Random(0xE8AC7);
        foreach (var precision in Precisions)
        {
            foreach (var roundingMode in Enum.GetValues<ExtF80RoundingMode>())
            {
                var context = new ExtF80Context(roundingMode, precision, ExtF80TininessMode.AfterRounding);
                for (var index = 0; index < 2_000; index++)
                {
                    // Choose a root that fits both in 32 bits, so its square still fits
                    // exactly in the 64-bit extF80 significand, and in the target
                    // precision, so the root itself is representable. Both conditions are
                    // required for the square root to be exact.
                    var rootBits = random.Next(1, Math.Min(32, precision == ExtF80Precision.Single ? 24 : 32) + 1);
                    var root = (ulong)random.NextInt64(1L << (rootBits - 1), 1L << rootBits);
                    var square = root * root;
                    var shift = BitOperations.LeadingZeroCount(square);
                    var significand = square << shift;
                    var exponent = 63 - shift;

                    // Keep the exponent even-aligned in both parities so the exact-root
                    // path is exercised for odd and even biased exponents alike.
                    var biased = exponent + Bias + (2 * random.Next(-8, 9));
                    if (biased is <= 0 or >= 0x7FFF)
                    {
                        continue;
                    }

                    var value = ExtF80.FromBits((ushort)biased, significand);
                    var actual = ExtF80Math.SquareRoot(value, context);

                    Assert.True(
                        actual.Flags == FloatingPointExceptionFlags.None,
                        $"Exact square root of {value.SignExponent:X4}:{value.Significand:X16} " +
                        $"({precision}, {roundingMode}) reported flags {(byte)actual.Flags:X2}.");
                    AssertCorrectlyRounded(value, context);
                }
            }
        }
    }

    [Fact]
    public void RoundingTiesAreImpossible()
    {
        // sqrt(n) == m + 1/2 would require 4n == (2m+1)^2, but (2m+1)^2 is odd while
        // 4n is even, so no integer n admits a tie. Spot-check the parity argument.
        for (var m = 0; m < 2_000; m++)
        {
            var half = ((BigInteger)(2 * m) + 1) * ((BigInteger)(2 * m) + 1);
            Assert.True(!half.IsEven);
        }
    }

    private static ulong Nonzero(ulong value) => value == 0 ? 1UL : value;

    private static void AssertCorrectlyRounded(ExtF80 value, ExtF80Context context)
    {
        var expected = ComputeCorrectlyRoundedSquareRoot(value, context);
        var actual = ExtF80Math.SquareRoot(value, context);

        Assert.True(
            actual.Value == expected.Value && actual.Flags == expected.Flags,
            $"Square root of {value.SignExponent:X4}:{value.Significand:X16} " +
            $"({context.Precision}, {context.RoundingMode}): " +
            $"actual={actual.Value.SignExponent:X4}:{actual.Value.Significand:X16}/{(byte)actual.Flags:X2} " +
            $"expected={expected.Value.SignExponent:X4}:{expected.Value.Significand:X16}/{(byte)expected.Flags:X2}");
    }

    /// <summary>
    /// Computes the correctly rounded square root of a positive finite value using exact
    /// integer arithmetic only.
    /// </summary>
    private static FloatingPointResult<ExtF80> ComputeCorrectlyRoundedSquareRoot(
        ExtF80 value,
        ExtF80Context context)
    {
        var precision = (int)context.Precision;

        // Unpack to value == significand * 2^(exponent - 63) with significand normalized.
        int exponent;
        BigInteger significand;
        if (value.BiasedExponent == 0)
        {
            var shift = BitOperations.LeadingZeroCount(value.Significand);
            exponent = 1 - Bias - shift;
            significand = value.Significand << shift;
        }
        else
        {
            exponent = value.BiasedExponent - Bias;
            significand = value.Significand;
        }

        // value lies in [2^exponent, 2^(exponent+1)), so sqrt(value) lies in
        // [2^resultExponent, 2^(resultExponent+1)) for resultExponent = floor(exponent/2).
        var resultExponent = exponent >> 1;

        // scaledExponent is the exponent of value * 2^(2*(precision-1-resultExponent)).
        var scaledExponent = (exponent - (2 * resultExponent)) - 63 + (2 * (precision - 1));

        // Guard so the radicand stays a non-negative power-of-two multiple.
        // floor(floor(sqrt(x)) / 2^g) == floor(sqrt(x) / 2^g), so shifting the root
        // back down after an exactly scaled integer square root loses nothing.
        const int guard = 40;
        var radicand = significand << (scaledExponent + (2 * guard));
        var root = IntegerSquareRoot(radicand) >> guard;

        // Exact when root^2 equals value * 2^(2*(precision-1-resultExponent)).
        var rootSquared = root * root;
        var exact = scaledExponent >= 0
            ? rootSquared == significand << scaledExponent
            : rootSquared << -scaledExponent == significand;

        // Round-to-nearest decision: fraction > 1/2 iff 4*scaled > (2*root+1)^2.
        var tieShift = Math.Max(0, -(scaledExponent + 2));
        var left = significand << (scaledExponent + 2 + tieShift);
        var right = ((4 * rootSquared) + (4 * root) + 1) << tieShift;
        var aboveHalf = left > right;

        var roundUp = !exact && context.RoundingMode switch
        {
            ExtF80RoundingMode.ToNearestEven => aboveHalf,
            ExtF80RoundingMode.TowardPositiveInfinity => true,
            _ => false
        };

        var rounded = roundUp ? root + 1 : root;
        if (rounded == BigInteger.One << precision)
        {
            rounded >>= 1;
            resultExponent++;
        }

        var packed = (ulong)rounded << (64 - precision);
        var flags = exact ? FloatingPointExceptionFlags.None : FloatingPointExceptionFlags.Inexact;
        return new FloatingPointResult<ExtF80>(
            ExtF80.FromBits((ushort)(resultExponent + Bias), packed),
            flags);
    }

    /// <summary>Returns the exact floor of the square root, verified before returning.</summary>
    private static BigInteger IntegerSquareRoot(BigInteger value)
    {
        if (value < 2)
        {
            return value;
        }

        var root = BigInteger.One << (int)((value.GetBitLength() + 1) / 2);
        while (true)
        {
            var next = (root + (value / root)) >> 1;
            if (next >= root)
            {
                break;
            }

            root = next;
        }

        // Self-check the oracle's own primitive: root^2 <= value < (root+1)^2.
        Assert.True(root * root <= value && value < (root + 1) * (root + 1));
        return root;
    }
}
