using System.Globalization;
using System.Diagnostics;
using CopperFloat;

namespace CopperFloat.Tests;

public sealed class TestFloatFixtureTests
{
    [Fact]
    public void BerkeleyTestFloatLevel1FixtureMatchesRawResultsAndFlags()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "testfloat-extf80-level1.tsv");
        var lineNumber = 1;
        foreach (var line in File.ReadLines(path).Skip(1))
        {
            lineNumber++;
            var columns = line.Split('\t');
            var context = new ExtF80Context(
                ParseRounding(columns[1]),
                ParsePrecision(columns[2]),
                columns[3] == "before"
                    ? ExtF80TininessMode.BeforeRounding
                    : ExtF80TininessMode.AfterRounding);
            var left = ParseExtF80(columns[4], columns[5]);
            var right = ParseExtF80(columns[6], columns[7]);
            var expected = ParseExtF80(columns[8], columns[9]);
            var expectedFlags = (FloatingPointExceptionFlags)byte.Parse(
                columns[10],
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture);

            var actual = columns[0] switch
            {
                "add" => ExtF80Math.Add(left, right, context),
                "sub" => ExtF80Math.Subtract(left, right, context),
                "mul" => ExtF80Math.Multiply(left, right, context),
                "div" => ExtF80Math.Divide(left, right, context),
                "sqrt" => ExtF80Math.SquareRoot(left, context),
                "roundToInt" => ExtF80Math.RoundToInteger(left, context),
                _ => throw new InvalidDataException($"Unknown fixture operation '{columns[0]}'.")
            };

            var equivalentValue = actual.Value == expected ||
                (actual.Value.Classification == ExtF80Class.QuietNaN &&
                    expected.Classification == ExtF80Class.QuietNaN);
            Assert.True(
                equivalentValue && actual.Flags == expectedFlags,
                $"TestFloat mismatch at fixture line {lineNumber}: {line}{Environment.NewLine}" +
                $"actual={actual.Value.SignExponent:X4}{actual.Value.Significand:X16} flags={(byte)actual.Flags:X2}");
        }
    }

    [Fact]
    public void BerkeleyTestFloatConversionFixtureMatchesBitsAndFlags()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "testfloat-extf80-conversions-level1.tsv");
        var lineNumber = 1;
        foreach (var line in File.ReadLines(path).Skip(1))
        {
            lineNumber++;
            var columns = line.Split('\t');
            var rounding = ParseRounding(columns[1]);
            var tininess = columns[2] == "before"
                ? ExtF80TininessMode.BeforeRounding
                : ExtF80TininessMode.AfterRounding;
            var input = ParseExtF80(columns[3], columns[4]);
            var expected = ulong.Parse(columns[5], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var expectedFlags = (FloatingPointExceptionFlags)byte.Parse(
                columns[6],
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture);

            ulong actual;
            FloatingPointExceptionFlags actualFlags;
            switch (columns[0])
            {
                case "to_i32":
                {
                    var result = ExtF80Math.ToInt32(input, rounding);
                    actual = unchecked((uint)result.Value);
                    actualFlags = result.Flags;
                    break;
                }
                case "to_i64":
                {
                    var result = ExtF80Math.ToInt64(input, rounding);
                    actual = unchecked((ulong)result.Value);
                    actualFlags = result.Flags;
                    break;
                }
                case "to_f32":
                {
                    var result = ExtF80Math.ToBinary32Bits(input, rounding, tininess);
                    actual = result.Value;
                    actualFlags = result.Flags;
                    break;
                }
                case "to_f64":
                {
                    var result = ExtF80Math.ToBinary64Bits(input, rounding, tininess);
                    actual = result.Value;
                    actualFlags = result.Flags;
                    break;
                }
                default:
                    throw new InvalidDataException($"Unknown fixture conversion '{columns[0]}'.");
            }

            var equivalentValue = actual == expected || columns[0] switch
            {
                "to_f32" => IsBinary32NaN((uint)actual) && IsBinary32NaN((uint)expected),
                "to_f64" => IsBinary64NaN(actual) && IsBinary64NaN(expected),
                _ => false
            };
            Assert.True(
                equivalentValue && actualFlags == expectedFlags,
                $"TestFloat conversion mismatch at fixture line {lineNumber}: {line}{Environment.NewLine}" +
                $"actual={actual:X16} flags={(byte)actualFlags:X2}");
        }
    }

    [Fact]
    public void ExternalTestFloatLevel2OracleMatchesRandomizedOperations()
    {
        var oracle = Environment.GetEnvironmentVariable("COPPERFLOAT_TESTFLOAT_ORACLE");
        if (string.IsNullOrWhiteSpace(oracle))
        {
            return;
        }

        var caseCount = int.TryParse(
            Environment.GetEnvironmentVariable("COPPERFLOAT_TESTFLOAT_LEVEL2_CASES"),
            out var configuredCaseCount)
            ? Math.Max(1, configuredCaseCount)
            : 64;
        var operations = new[] { "add", "sub", "mul", "div", "sqrt", "round" };
        var roundings = new[]
        {
            (SoftFloat: 0, Copper: ExtF80RoundingMode.ToNearestEven),
            (SoftFloat: 1, Copper: ExtF80RoundingMode.TowardZero),
            (SoftFloat: 2, Copper: ExtF80RoundingMode.TowardNegativeInfinity),
            (SoftFloat: 3, Copper: ExtF80RoundingMode.TowardPositiveInfinity)
        };
        var precisions = new[]
        {
            (SoftFloat: 32, Copper: ExtF80Precision.Single),
            (SoftFloat: 64, Copper: ExtF80Precision.Double),
            (SoftFloat: 80, Copper: ExtF80Precision.Extended)
        };

        foreach (var operation in operations)
        foreach (var rounding in roundings)
        foreach (var precision in precisions)
        foreach (var tininess in new[] { 0, 1 })
        {
            var context = new ExtF80Context(
                rounding.Copper,
                precision.Copper,
                tininess == 0 ? ExtF80TininessMode.BeforeRounding : ExtF80TininessMode.AfterRounding);
            RunExternalBatch(
                oracle,
                operation,
                rounding.SoftFloat,
                precision.SoftFloat,
                tininess,
                context,
                caseCount);
        }
    }

    private static ExtF80 ParseExtF80(string signExponent, string significand)
        => ExtF80.FromBits(
            ushort.Parse(signExponent, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            ulong.Parse(significand, NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private static ExtF80RoundingMode ParseRounding(string value)
        => value switch
        {
            "near_even" => ExtF80RoundingMode.ToNearestEven,
            "minMag" => ExtF80RoundingMode.TowardZero,
            "min" => ExtF80RoundingMode.TowardNegativeInfinity,
            "max" => ExtF80RoundingMode.TowardPositiveInfinity,
            _ => throw new InvalidDataException($"Unknown fixture rounding mode '{value}'.")
        };

    private static ExtF80Precision ParsePrecision(string value)
        => value switch
        {
            "24" => ExtF80Precision.Single,
            "53" => ExtF80Precision.Double,
            "64" => ExtF80Precision.Extended,
            _ => throw new InvalidDataException($"Unknown fixture precision '{value}'.")
        };

    private static bool IsBinary32NaN(uint bits)
        => (bits & 0x7F80_0000u) == 0x7F80_0000u && (bits & 0x007F_FFFFu) != 0;

    private static bool IsBinary64NaN(ulong bits)
        => (bits & 0x7FF0_0000_0000_0000UL) == 0x7FF0_0000_0000_0000UL &&
            (bits & 0x000F_FFFF_FFFF_FFFFUL) != 0;

    private static void RunExternalBatch(
        string oracle,
        string operation,
        int rounding,
        int precision,
        int tininess,
        ExtF80Context context,
        int caseCount)
    {
        var startInfo = new ProcessStartInfo(oracle)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("batch");
        startInfo.ArgumentList.Add(operation);
        startInfo.ArgumentList.Add(rounding.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(precision.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(tininess.ToString(CultureInfo.InvariantCulture));

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException($"Could not start TestFloat oracle '{oracle}'.");
        var operationSeed = operation switch
        {
            "add" => 1,
            "sub" => 2,
            "mul" => 3,
            "div" => 4,
            "sqrt" => 5,
            "round" => 6,
            _ => 0
        };
        var random = new Random(
            operationSeed * 100_000 + rounding * 10_000 + precision * 10 + tininess + 0x68040);
        var operands = new (ExtF80 Left, ExtF80 Right)[caseCount];
        for (var index = 0; index < operands.Length; index++)
        {
            operands[index] = (CreateLevel2Operand(random, index), CreateLevel2Operand(random, index + 7));
            process.StandardInput.WriteLine(
                $"{operands[index].Left.SignExponent:X4} {operands[index].Left.Significand:X16} " +
                $"{operands[index].Right.SignExponent:X4} {operands[index].Right.Significand:X16}");
        }

        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"TestFloat oracle failed: {error}");

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(operands.Length, lines.Length);
        for (var index = 0; index < lines.Length; index++)
        {
            var columns = lines[index].Split('\t');
            var expected = ParseExtF80(columns[4], columns[5]);
            var expectedFlags = (FloatingPointExceptionFlags)byte.Parse(
                columns[6],
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture);
            var actual = operation switch
            {
                "add" => ExtF80Math.Add(operands[index].Left, operands[index].Right, context),
                "sub" => ExtF80Math.Subtract(operands[index].Left, operands[index].Right, context),
                "mul" => ExtF80Math.Multiply(operands[index].Left, operands[index].Right, context),
                "div" => ExtF80Math.Divide(operands[index].Left, operands[index].Right, context),
                "sqrt" => ExtF80Math.SquareRoot(operands[index].Left, context),
                "round" => ExtF80Math.RoundToInteger(operands[index].Left, context),
                _ => throw new InvalidOperationException()
            };
            var equivalentValue = actual.Value == expected ||
                (actual.Value.Classification == ExtF80Class.QuietNaN &&
                    expected.Classification == ExtF80Class.QuietNaN);
            Assert.True(
                equivalentValue && actual.Flags == expectedFlags,
                $"External TestFloat mismatch: op={operation}, rounding={rounding}, precision={precision}, " +
                $"tininess={tininess}, index={index}, expected={columns[4]}{columns[5]}/{columns[6]}, " +
                $"actual={actual.Value.SignExponent:X4}{actual.Value.Significand:X16}/{(byte)actual.Flags:X2}");
        }
    }

    private static ExtF80 CreateLevel2Operand(Random random, int index)
    {
        return (index & 15) switch
        {
            0 => ExtF80.PositiveZero,
            1 => ExtF80.NegativeZero,
            2 => ExtF80.PositiveInfinity,
            3 => ExtF80.NegativeInfinity,
            4 => ExtF80.QuietNaN,
            5 => ExtF80.FromBits(0x7FFF, 0x8000_0000_0000_0001),
            6 => ExtF80.FromBits(0x0000, 0x0000_0000_0000_0001),
            7 => ExtF80.FromBits(0x7FFE, 0xFFFF_FFFF_FFFF_FFFF),
            _ => ExtF80.FromBits(
                (ushort)(random.Next(1, 0x7FFF) | (random.Next(2) << 15)),
                unchecked((ulong)random.NextInt64()) | 0x8000_0000_0000_0000UL)
        };
    }
}
