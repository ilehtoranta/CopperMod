/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using CopperFloat;

namespace CopperFloat.Tests;

public sealed class ExtF80HostAccelerationTests
{
    private static readonly ExtF80Context SingleContext = new(
        ExtF80RoundingMode.ToNearestEven,
        ExtF80Precision.Single,
        ExtF80TininessMode.BeforeRounding);

    private static readonly ExtF80Context DoubleContext = new(
        ExtF80RoundingMode.ToNearestEven,
        ExtF80Precision.Double,
        ExtF80TininessMode.BeforeRounding);

    [Fact]
    public void Binary32HostDivisionMatchesIntegerReference()
    {
        const ExtF80HostOperation operation = ExtF80HostOperation.Divide;
        var random = new Random(0x68040 + (int)operation);
        var accelerated = 0;
        for (var index = 0; index < 20_000; index++)
        {
            var left = ExtF80Math.FromBinary32Bits(CreateNormalBinary32(random)).Value;
            var right = ExtF80Math.FromBinary32Bits(CreateNormalBinary32(random)).Value;
            if (!ExtF80HostMath.TryBinary(left, right, SingleContext, operation, out var actual))
            {
                continue;
            }

            accelerated++;
            Assert.Equal(Reference(operation, left, right, SingleContext), actual);
        }

        Assert.True(accelerated > 10_000, $"Only {accelerated} binary32 cases used host arithmetic.");
    }

    [Fact]
    public void Binary64HostDivisionMatchesIntegerReference()
    {
        const ExtF80HostOperation operation = ExtF80HostOperation.Divide;
        var random = new Random(0x68881 + (int)operation);
        var accelerated = 0;
        for (var index = 0; index < 20_000; index++)
        {
            var left = ExtF80Math.FromBinary64Bits(CreateNormalBinary64(random)).Value;
            var right = ExtF80Math.FromBinary64Bits(CreateNormalBinary64(random)).Value;
            if (!ExtF80HostMath.TryBinary(left, right, DoubleContext, operation, out var actual))
            {
                continue;
            }

            accelerated++;
            Assert.Equal(Reference(operation, left, right, DoubleContext), actual);
        }

        Assert.True(accelerated > 10_000, $"Only {accelerated} binary64 cases used host arithmetic.");
    }

    [Fact]
    public void HostAccelerationRejectsContextsAndOperandsOutsideExactContract()
    {
        var one = ExtF80Math.FromInt32(1);
        var nonBinary64 = ExtF80.FromBits(0x3FFF, 0x8000_0000_0000_0001);
        var huge = ExtF80.FromBits(0x7FFE, ExtF80.IntegerBit);

        Assert.False(ExtF80HostMath.TryBinary(
            one,
            one,
            DoubleContext with { RoundingMode = ExtF80RoundingMode.TowardZero },
            ExtF80HostOperation.Add,
            out _));
        Assert.False(ExtF80HostMath.TryBinary(
            one,
            one,
            DoubleContext,
            ExtF80HostOperation.Multiply,
            out _));
        Assert.False(ExtF80HostMath.TryBinary(
            one,
            one,
            DoubleContext with { Precision = ExtF80Precision.Extended },
            ExtF80HostOperation.Add,
            out _));
        Assert.False(ExtF80HostMath.TryBinary(
            nonBinary64,
            one,
            DoubleContext,
            ExtF80HostOperation.Add,
            out _));
        Assert.False(ExtF80HostMath.TryBinary(
            huge,
            one,
            DoubleContext,
            ExtF80HostOperation.Add,
            out _));
    }

    [Fact]
    public void HostAccelerationRejectsBinaryRangeTransitions()
    {
        var maxSingle = ExtF80Math.FromBinary32Bits(0x7F7F_FFFF).Value;
        var minSingle = ExtF80Math.FromBinary32Bits(0x0080_0000).Value;
        var half = ExtF80Math.FromBinary32Bits(0x3F00_0000).Value;

        Assert.False(ExtF80HostMath.TryBinary(
            maxSingle,
            maxSingle,
            SingleContext,
            ExtF80HostOperation.Add,
            out _));
        Assert.False(ExtF80HostMath.TryBinary(
            minSingle,
            half,
            SingleContext,
            ExtF80HostOperation.Multiply,
            out _));
    }

    private static FloatingPointResult<ExtF80> Reference(
        ExtF80HostOperation operation,
        ExtF80 left,
        ExtF80 right,
        ExtF80Context context)
        => operation switch
        {
            ExtF80HostOperation.Add => ExtF80Math.AddReference(left, right, context),
            ExtF80HostOperation.Subtract => ExtF80Math.SubtractReference(left, right, context),
            ExtF80HostOperation.Multiply => ExtF80Math.MultiplyReference(left, right, context),
            _ => ExtF80Math.DivideReference(left, right, context)
        };

    private static uint CreateNormalBinary32(Random random)
    {
        var sign = (uint)random.Next(2) << 31;
        var exponent = (uint)random.Next(1, 255) << 23;
        var fraction = (uint)random.Next(1 << 23);
        return sign | exponent | fraction;
    }

    private static ulong CreateNormalBinary64(Random random)
    {
        var sign = (ulong)random.Next(2) << 63;
        var exponent = (ulong)random.Next(1, 2047) << 52;
        var fraction = (ulong)random.NextInt64() & 0x000F_FFFF_FFFF_FFFFUL;
        return sign | exponent | fraction;
    }
}
