/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using CopperFloat;

namespace CopperFloat.Tests;

/// <summary>
/// Differential coverage for the Extended-precision square-root path.
/// <see cref="ExtF80Math.SquareRoot"/> seeds its integer square root from
/// <see cref="Math.Sqrt"/> and corrects the estimate, while
/// <see cref="ExtF80Math.SquareRootReference"/> uses a pure-integer Newton
/// iteration. The two must agree bit-for-bit on both value and flags.
/// </summary>
public sealed class ExtF80SquareRootTests
{
    private const int CasesPerRoundingMode = 50_000;

    private static readonly ExtF80TininessMode[] TininessModes =
    [
        ExtF80TininessMode.BeforeRounding,
        ExtF80TininessMode.AfterRounding
    ];

    [Theory]
    [InlineData((int)ExtF80RoundingMode.ToNearestEven)]
    [InlineData((int)ExtF80RoundingMode.TowardZero)]
    [InlineData((int)ExtF80RoundingMode.TowardNegativeInfinity)]
    [InlineData((int)ExtF80RoundingMode.TowardPositiveInfinity)]
    public void ExtendedSquareRootMatchesIntegerReferenceForNormalValues(int roundingModeValue)
    {
        var roundingMode = (ExtF80RoundingMode)roundingModeValue;
        var random = new Random(0x5A17 + roundingModeValue);

        for (var index = 0; index < CasesPerRoundingMode; index++)
        {
            var context = new ExtF80Context(
                roundingMode,
                ExtF80Precision.Extended,
                TininessModes[index & 1]);
            var value = CreatePositiveNormal(random);

            AssertReferenceAgreement(value, context);
        }
    }

    [Theory]
    [InlineData((int)ExtF80RoundingMode.ToNearestEven)]
    [InlineData((int)ExtF80RoundingMode.TowardZero)]
    [InlineData((int)ExtF80RoundingMode.TowardNegativeInfinity)]
    [InlineData((int)ExtF80RoundingMode.TowardPositiveInfinity)]
    public void ExtendedSquareRootMatchesIntegerReferenceForSubnormalAndTinyValues(int roundingModeValue)
    {
        var roundingMode = (ExtF80RoundingMode)roundingModeValue;
        var random = new Random(0x68882 + roundingModeValue);

        for (var index = 0; index < CasesPerRoundingMode; index++)
        {
            var context = new ExtF80Context(
                roundingMode,
                ExtF80Precision.Extended,
                TininessModes[index & 1]);
            var value = (index & 2) == 0
                ? CreatePositiveSubnormal(random)
                : CreatePositiveTinyNormal(random);

            AssertReferenceAgreement(value, context);
        }
    }

    [Fact]
    public void ExtendedSquareRootMatchesIntegerReferenceForBoundaryEncodings()
    {
        ExtF80[] values =
        [
            ExtF80.PositiveZero,
            ExtF80.NegativeZero,
            ExtF80.PositiveInfinity,
            ExtF80.NegativeInfinity,
            ExtF80.QuietNaN,
            ExtF80.FromBits(0x7FFF, 0x8000_0000_0000_0001),
            ExtF80.FromBits(0x0000, 0x0000_0000_0000_0001),
            ExtF80.FromBits(0x0000, 0x7FFF_FFFF_FFFF_FFFF),
            ExtF80.FromBits(0x0001, ExtF80.IntegerBit),
            ExtF80.FromBits(0x7FFE, 0xFFFF_FFFF_FFFF_FFFF),
            ExtF80.FromBits(0x3FFF, ExtF80.IntegerBit),
            ExtF80.FromBits(0x4000, ExtF80.IntegerBit),
            ExtF80.FromBits(0x3FFE, ExtF80.IntegerBit),
            ExtF80.FromBits(0x8000, 0x0000_0000_0000_0001),
            ExtF80.FromBits(0xBFFF, ExtF80.IntegerBit),
            ExtF80.FromBits(0x3FFF, 0x0000_0000_0000_0001)
        ];

        foreach (var roundingMode in Enum.GetValues<ExtF80RoundingMode>())
        {
            foreach (var tininess in TininessModes)
            {
                var context = new ExtF80Context(roundingMode, ExtF80Precision.Extended, tininess);
                foreach (var value in values)
                {
                    AssertReferenceAgreement(value, context);
                }
            }
        }
    }

    private static void AssertReferenceAgreement(ExtF80 value, ExtF80Context context)
    {
        var actual = ExtF80Math.SquareRoot(value, context);
        var expected = ExtF80Math.SquareRootReference(value, context);

        var equivalentValue = actual.Value == expected.Value ||
            (actual.Value.Classification == ExtF80Class.QuietNaN &&
                expected.Value.Classification == ExtF80Class.QuietNaN);
        Assert.True(
            equivalentValue && actual.Flags == expected.Flags,
            $"Extended square-root mismatch for {value.SignExponent:X4}:{value.Significand:X16} " +
            $"({context.RoundingMode}, {context.TininessMode}): " +
            $"actual={actual.Value.SignExponent:X4}:{actual.Value.Significand:X16}/{(byte)actual.Flags:X2} " +
            $"expected={expected.Value.SignExponent:X4}:{expected.Value.Significand:X16}/{(byte)expected.Flags:X2}");
    }

    private static ExtF80 CreatePositiveNormal(Random random)
        => ExtF80.FromBits(
            (ushort)random.Next(1, 0x7FFF),
            unchecked((ulong)random.NextInt64()) | ExtF80.IntegerBit);

    private static ExtF80 CreatePositiveSubnormal(Random random)
    {
        var significand = unchecked((ulong)random.NextInt64()) & ExtF80.FractionMask;
        return ExtF80.FromBits(0, significand == 0 ? 1 : significand);
    }

    private static ExtF80 CreatePositiveTinyNormal(Random random)
        => ExtF80.FromBits(
            (ushort)random.Next(1, 128),
            unchecked((ulong)random.NextInt64()) | ExtF80.IntegerBit);
}
