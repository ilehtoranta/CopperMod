using CopperFloat;

namespace CopperFloat.Tests;

public sealed class ExtF80MathTests
{
    private static readonly ExtF80Context Extended = ExtF80Context.Default;

    [Fact]
    public void IntegerAndBinaryConversionsProduceExactExtendedValues()
    {
        Assert.Equal(F80(0x3fff, 0x8000_0000_0000_0000), ExtF80Math.FromInt32(1));
        Assert.Equal(F80(0xc000, 0x8000_0000_0000_0000), ExtF80Math.FromInt32(-2));
        Assert.Equal(F80(0x3fff, 0xc000_0000_0000_0000), ExtF80Math.FromBinary64Bits(0x3ff8_0000_0000_0000).Value);
        Assert.Equal(F80(0x3fff, 0xc000_0000_0000_0000), ExtF80Math.FromBinary32Bits(0x3fc0_0000).Value);
    }

    [Fact]
    public void BasicArithmeticProducesExactRawResults()
    {
        var one = ExtF80Math.FromInt32(1);
        var two = ExtF80Math.FromInt32(2);
        var three = ExtF80Math.FromInt32(3);
        var four = ExtF80Math.FromInt32(4);

        AssertResult(F80(0x4000, 0xc000_0000_0000_0000), ExtF80Math.Add(one, two, Extended));
        AssertResult(one, ExtF80Math.Subtract(three, two, Extended));
        AssertResult(F80(0x4001, 0xc000_0000_0000_0000), ExtF80Math.Multiply(two, three, Extended));
        AssertResult(F80(0x3fff, 0xc000_0000_0000_0000), ExtF80Math.Divide(three, two, Extended));
        AssertResult(two, ExtF80Math.SquareRoot(four, Extended));
    }

    [Fact]
    public void SquareRootTwoRoundsToKnownExtendedEncoding()
    {
        var result = ExtF80Math.SquareRoot(ExtF80Math.FromInt32(2), Extended);

        Assert.Equal(F80(0x3fff, 0xb504_f333_f9de_6484), result.Value);
        Assert.Equal(FloatingPointExceptionFlags.Inexact, result.Flags);
    }

    [Fact]
    public void AcceleratedSquareRootMatchesIntegerReferenceAcrossContexts()
    {
        var state = 0xD1B5_4A32_D192_ED03ul;
        foreach (var rounding in Enum.GetValues<ExtF80RoundingMode>())
        foreach (var precision in Enum.GetValues<ExtF80Precision>())
        foreach (var tininess in Enum.GetValues<ExtF80TininessMode>())
        {
            var context = new ExtF80Context(rounding, precision, tininess);
            for (var index = 0; index < 512; index++)
            {
                var bits = NextRandom(ref state);
                var exponent = (ushort)(1 + (bits % 0x7ffe));
                var normal = ExtF80.FromBits(exponent, NextRandom(ref state) | 0x8000_0000_0000_0000ul);
                Assert.Equal(
                    ExtF80Math.SquareRootReference(normal, context),
                    ExtF80Math.SquareRoot(normal, context));

                var subnormal = ExtF80.FromBits(0, NextRandom(ref state) & 0x7fff_ffff_ffff_fffful);
                Assert.Equal(
                    ExtF80Math.SquareRootReference(subnormal, context),
                    ExtF80Math.SquareRoot(subnormal, context));
            }
        }
    }

    [Fact]
    public void PrecisionAndDirectedRoundingUseGuardRoundAndStickyBits()
    {
        var halfSingleUlp = F80(0x3fe7, 0x8000_0000_0000_0000); // 2^-24
        var one = ExtF80Math.FromInt32(1);
        var exact = ExtF80Math.Add(one, halfSingleUlp, Extended).Value;
        var nearest = new ExtF80Context(
            ExtF80RoundingMode.ToNearestEven,
            ExtF80Precision.Single,
            ExtF80TininessMode.AfterRounding);
        var upward = nearest with { RoundingMode = ExtF80RoundingMode.TowardPositiveInfinity };

        Assert.Equal(F80(0x3fff, 0x8000_0000_0000_0000), ExtF80Math.Round(exact, nearest).Value);
        Assert.Equal(F80(0x3fff, 0x8000_0100_0000_0000), ExtF80Math.Round(exact, upward).Value);
        Assert.Equal(FloatingPointExceptionFlags.Inexact, ExtF80Math.Round(exact, nearest).Flags);
    }

    [Fact]
    public void InvalidAndDivideByZeroProduceCanonicalResultsAndFlags()
    {
        var zero = ExtF80.PositiveZero;
        var infinity = ExtF80.PositiveInfinity;

        AssertResult(
            ExtF80.QuietNaN,
            ExtF80Math.Multiply(zero, infinity, Extended),
            FloatingPointExceptionFlags.Invalid);
        AssertResult(
            infinity,
            ExtF80Math.Divide(ExtF80Math.FromInt32(1), zero, Extended),
            FloatingPointExceptionFlags.DivideByZero);
    }

    [Fact]
    public void SignalingNanIsQuietedAndPayloadIsPreserved()
    {
        var signaling = F80(0xffff, 0x8000_1234_5678_9abc);

        var result = ExtF80Math.Add(signaling, ExtF80Math.FromInt32(1), Extended);

        Assert.Equal(F80(0xffff, 0xc000_1234_5678_9abc), result.Value);
        Assert.Equal(FloatingPointExceptionFlags.Invalid, result.Flags);
    }

    [Fact]
    public void CancellationAndExactZeroPreserveDocumentedSigns()
    {
        var one = ExtF80Math.FromInt32(1);
        var negativeOne = ExtF80Math.FromInt32(-1);
        var downward = Extended with { RoundingMode = ExtF80RoundingMode.TowardNegativeInfinity };

        Assert.Equal(ExtF80.PositiveZero, ExtF80Math.Add(one, negativeOne, Extended).Value);
        Assert.Equal(ExtF80.NegativeZero, ExtF80Math.Add(one, negativeOne, downward).Value);
        Assert.Equal(ExtF80.NegativeZero, ExtF80Math.Multiply(ExtF80.NegativeZero, one, Extended).Value);
    }

    [Fact]
    public void BinaryAndIntegerDestinationsRoundWithoutHostArithmetic()
    {
        var oneAndHalf = F80(0x3fff, 0xc000_0000_0000_0000);
        var minusOneAndHalf = F80(0xbfff, 0xc000_0000_0000_0000);

        Assert.Equal(0x3fc0_0000u, ExtF80Math.ToBinary32Bits(oneAndHalf).Value);
        Assert.Equal(0x3ff8_0000_0000_0000ul, ExtF80Math.ToBinary64Bits(oneAndHalf).Value);
        Assert.Equal(2, ExtF80Math.ToInt32(oneAndHalf, ExtF80RoundingMode.ToNearestEven).Value);
        Assert.Equal(-1, ExtF80Math.ToInt32(minusOneAndHalf, ExtF80RoundingMode.TowardZero).Value);
    }

    [Fact]
    public void ComparisonHandlesSignedZeroInfinityAndNan()
    {
        Assert.Equal(ExtF80Comparison.Equal, ExtF80Math.Compare(ExtF80.PositiveZero, ExtF80.NegativeZero).Value);
        Assert.Equal(ExtF80Comparison.Less, ExtF80Math.Compare(ExtF80.NegativeInfinity, ExtF80Math.FromInt32(-1)).Value);
        Assert.Equal(ExtF80Comparison.Unordered, ExtF80Math.Compare(ExtF80.QuietNaN, ExtF80Math.FromInt32(1)).Value);
    }

    [Fact]
    public void ArithmeticAndConversionsAllocateNoSteadyStateMemory()
    {
        _ = RunAllocationLoop(16);
        var before = GC.GetAllocatedBytesForCurrentThread();

        var checksum = RunAllocationLoop(10_000);

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.NotEqual(0ul, checksum);
    }

    private static ulong RunAllocationLoop(int iterations)
    {
        var left = ExtF80.FromBits(0x4001, 0xC000_0000_0000_0000);
        var right = ExtF80.FromBits(0x3FFF, 0xA000_0000_0000_0000);
        var checksum = 0ul;
        for (var i = 0; i < iterations; i++)
        {
            var sum = ExtF80Math.Add(left, right, Extended);
            var product = ExtF80Math.Multiply(sum.Value, right, Extended);
            var quotient = ExtF80Math.Divide(product.Value, left, Extended);
            var root = ExtF80Math.SquareRoot(quotient.Value, Extended);
            var rounded = ExtF80Math.RoundToInteger(root.Value, Extended);
            var comparison = ExtF80Math.Compare(root.Value, rounded.Value);
            var binary64 = ExtF80Math.ToBinary64Bits(root.Value);
            checksum ^= root.Value.Significand ^ binary64.Value ^ (ulong)comparison.Value;
            left = ExtF80Math.Negate(ExtF80Math.Absolute(left).Value).Value;
        }

        return checksum;
    }

    private static ExtF80 F80(ushort signExponent, ulong significand)
        => ExtF80.FromBits(signExponent, significand);

    private static ulong NextRandom(ref ulong state)
    {
        state ^= state >> 12;
        state ^= state << 25;
        state ^= state >> 27;
        return state * 0x2545_F491_4F6C_DD1Dul;
    }

    private static void AssertResult(
        ExtF80 expected,
        FloatingPointResult<ExtF80> actual,
        FloatingPointExceptionFlags flags = FloatingPointExceptionFlags.None)
    {
        Assert.Equal(expected, actual.Value);
        Assert.Equal(flags, actual.Flags);
    }
}
