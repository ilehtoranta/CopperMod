using System.Buffers.Binary;
using System.Globalization;

namespace CopperMod.Sid.Tests;

public sealed class PexD418MeasurementTests
{
	private const string MeasurementRootName = "Musik_RunStop_8-bit_sample_measurements_by_Pex_Mahoney_Tufvesson";

	[Fact]
	public void MeasuredAmplitudeTableFilesMatchRuntimeConstantsWhenPresent()
	{
		var root = FindMeasurementRoot();
		if (root == null)
		{
			return;
		}

		AssertAmplitudeTableMatches(
			Path.Combine(root, "amplitude_table_6581.txt"),
			SidChipModel.Mos6581,
			SidAnalog.Mos6581D418MeasuredAmplitude);
		AssertAmplitudeTableMatches(
			Path.Combine(root, "amplitude_table_8580.txt"),
			SidChipModel.Mos8580,
			SidAnalog.Mos8580D418MeasuredAmplitude);
	}

	[Fact]
	public void OptionalPexRawWavCapturesRegenerateMeasuredD418AmplitudeShape()
	{
		if (Environment.GetEnvironmentVariable("SID_REAL_CAPTURE_TESTS") != "1")
		{
			return;
		}

		var root = FindMeasurementRoot();
		if (root == null)
		{
			AssertNotStrict("Pex measurement folder was not found.");
			return;
		}

		AssertCaptureMatchesRuntimeTable(new PexCapture(
			Path.Combine(root, "Pex_testfiles", "Hedning_6581R4_Gubbdata_Compo_96kHz_24bit.wav"),
			SidChipModel.Mos6581,
			219470,
			3476292));
		AssertCaptureMatchesRuntimeTable(new PexCapture(
			Path.Combine(root, "Pex_testfiles", "Bepp_8580R5_bread_96kHz_32bit_fixed.wav"),
			SidChipModel.Mos8580,
			147995,
			3404870));
	}

	[Fact]
	public void OptionalPexRawWavReaderSupportsShort16BitReferenceFiles()
	{
		if (Environment.GetEnvironmentVariable("SID_REAL_CAPTURE_TESTS") != "1")
		{
			return;
		}

		var root = FindMeasurementRoot();
		if (root == null)
		{
			AssertNotStrict("Pex measurement folder was not found.");
			return;
		}

		var path = Path.Combine(root, "thcm_testfiles", "Ref_C64-Short_8580_16bit_48kHz_mono_85gain.wav");
		if (!File.Exists(path))
		{
			AssertNotStrict("Short 16-bit Pex reference WAV was not found.");
			return;
		}

		var wav = MeasurementWavReader.ReadMono(path);

		Assert.Equal(48000, wav.SampleRate);
		Assert.True(wav.Samples.Length > 1000);
		Assert.Contains(wav.Samples, sample => Math.Abs(sample) > 0.001f);
	}

	private static void AssertAmplitudeTableMatches(string path, SidChipModel model, Func<int, double> runtime)
	{
		if (!File.Exists(path))
		{
			return;
		}

		var values = File.ReadLines(path)
			.Select(line => line.Trim())
			.Where(line => line.Length > 0 && !line.StartsWith(';'))
			.Select(line => double.Parse(line, CultureInfo.InvariantCulture))
			.ToArray();

		Assert.Equal(256, values.Length);
		for (var i = 0; i < values.Length; i++)
		{
			Assert.True(
				Math.Abs(values[i] - runtime(i)) <= 0.0000005,
				$"{model} amplitude table mismatch at ${i:X2}: file {values[i]:0.000000}, runtime {runtime(i):0.000000}.");
		}
	}

	private static void AssertCaptureMatchesRuntimeTable(PexCapture capture)
	{
		if (!File.Exists(capture.Path))
		{
			AssertNotStrict("Pex capture WAV was not found: " + capture.Path);
			return;
		}

		var wav = MeasurementWavReader.ReadMono(capture.Path);
		Assert.Equal(96000, wav.SampleRate);

		var normalized = NormalizeFullRange(wav.Samples);
		var measured = BuildNormalizedTargetMeans(normalized, capture);
		ReadOnlySpan<int> targets = stackalloc[] { 0x00, 0x0F, 0x1F, 0x4F, 0x9F, 0xFF };
		var tolerance = capture.Model == SidChipModel.Mos6581 ? 0.13 : 0.16;
		for (var i = 0; i < targets.Length; i++)
		{
			var target = targets[i];
			var runtime = capture.Model == SidChipModel.Mos8580
				? SidAnalog.Mos8580D418MeasuredAmplitude(target)
				: SidAnalog.Mos6581D418MeasuredAmplitude(target);
			var delta = Math.Abs(measured[target] - runtime);
			Assert.True(
				delta <= tolerance,
				$"{capture.Model} raw capture {Path.GetFileName(capture.Path)} regenerated ${target:X2} as {measured[target]:0.000000}, runtime {runtime:0.000000}, delta {delta:0.000000}.");
		}

		AssertSelectedTransitionsHaveExpectedPolarity(normalized, capture);
	}

	private static double[] BuildNormalizedTargetMeans(float[] normalizedSamples, PexCapture capture)
	{
		var means = new double[256];
		for (var target = 0; target < means.Length; target++)
		{
			var fromSum = 0.0;
			var toSum = 0.0;
			for (var other = 0; other < 256; other++)
			{
				fromSum += MeasureTransitionValue(normalizedSamples, capture, target, other).From;
				toSum += MeasureTransitionValue(normalizedSamples, capture, other, target).To;
			}

			means[target] = ((fromSum / 256.0) + (toSum / 256.0)) * 0.5;
		}

		var maxVal = means[0x0F];
		var minVal = means[0x9F];
		if (capture.Model == SidChipModel.Mos8580)
		{
			for (var i = 0; i < means.Length; i++)
			{
				means[i] = (((means[i] - minVal) / (maxVal - minVal)) - 0.5) * 2.0;
			}
		}
		else
		{
			var maxAmplitude = Math.Max(maxVal, -minVal);
			for (var i = 0; i < means.Length; i++)
			{
				means[i] /= maxAmplitude;
			}
		}

		return means;
	}

	private static void AssertSelectedTransitionsHaveExpectedPolarity(float[] normalizedSamples, PexCapture capture)
	{
		var lowToHigh = MeasureTransitionValue(normalizedSamples, capture, 0x00, 0x0F);
		var highToLow = MeasureTransitionValue(normalizedSamples, capture, 0x0F, 0x00);
		var negative = MeasureTransitionValue(normalizedSamples, capture, 0x00, 0x9F);

		Assert.True(lowToHigh.To > lowToHigh.From, $"Expected ${0x00:X2}->${0x0F:X2} to rise in raw capture.");
		Assert.True(highToLow.From > highToLow.To, $"Expected ${0x0F:X2}->${0x00:X2} to fall in raw capture.");
		Assert.True(negative.To < lowToHigh.To, $"Expected ${0x9F:X2} to be below positive full-scale in raw capture.");
	}

	private static (double From, double To) MeasureTransitionValue(float[] samples, PexCapture capture, int from, int to)
	{
		var startIndex = capture.FirstValueIndex + 33.0;
		var endIndex = capture.LastValueIndex - 20.0;
		var transitionIndex = from * 256 + to;
		var index = startIndex + (transitionIndex * (endIndex - startIndex) / 65536.0);
		var zeroLeftIndex = RoundMatlab(index - 13.0);
		var fromIndex = RoundMatlab(index);
		var toIndex = RoundMatlab(index + 13.0);
		var zeroRightIndex = RoundMatlab(index + 26.0);

		var zeroLeft = AverageAtMatlabIndex(samples, zeroLeftIndex);
		var fromValue = AverageAtMatlabIndex(samples, fromIndex);
		var toValue = AverageAtMatlabIndex(samples, toIndex);
		var zeroRight = AverageAtMatlabIndex(samples, zeroRightIndex);

		var fromFraction = (fromIndex - zeroLeftIndex) / (double)(zeroRightIndex - zeroLeftIndex);
		var toFraction = (toIndex - zeroLeftIndex) / (double)(zeroRightIndex - zeroLeftIndex);
		var zeroFrom = (zeroLeft * (1.0 - fromFraction)) + (zeroRight * fromFraction);
		var zeroTo = (zeroLeft * (1.0 - toFraction)) + (zeroRight * toFraction);
		return (fromValue - zeroFrom, toValue - zeroTo);
	}

	private static float[] NormalizeFullRange(float[] samples)
	{
		var min = float.PositiveInfinity;
		var max = float.NegativeInfinity;
		for (var i = 0; i < samples.Length; i++)
		{
			min = Math.Min(min, samples[i]);
			max = Math.Max(max, samples[i]);
		}

		var range = Math.Max(1e-12f, max - min);
		var normalized = new float[samples.Length];
		for (var i = 0; i < samples.Length; i++)
		{
			normalized[i] = (((samples[i] - min) / range) - 0.5f) * 2.0f;
		}

		return normalized;
	}

	private static int RoundMatlab(double value)
	{
		return (int)Math.Round(value, MidpointRounding.AwayFromZero);
	}

	private static double AverageAtMatlabIndex(float[] samples, int matlabIndex)
	{
		var center = matlabIndex - 1;
		Assert.InRange(center, 1, samples.Length - 2);
		return (samples[center - 1] + samples[center] + samples[center + 1]) / 3.0;
	}

	private static string? FindMeasurementRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory != null)
		{
			var candidate = Path.Combine(directory.FullName, "CopperMod.Sid", "Docs", MeasurementRootName);
			if (Directory.Exists(candidate))
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		return null;
	}

	private static void AssertNotStrict(string message)
	{
		if (Environment.GetEnvironmentVariable("SID_REAL_CAPTURE_TESTS_STRICT") == "1")
		{
			Assert.Fail(message);
		}
	}

	private sealed record PexCapture(string Path, SidChipModel Model, int FirstValueIndex, int LastValueIndex);

	private sealed record MeasurementWav(int SampleRate, float[] Samples);

	private static class MeasurementWavReader
	{
		public static MeasurementWav ReadMono(string path)
		{
			using var stream = File.OpenRead(path);
			using var reader = new BinaryReader(stream);
			var riff = new string(reader.ReadChars(4));
			if (riff != "RIFF")
			{
				throw new InvalidDataException("Only RIFF WAV files are supported.");
			}

			_ = reader.ReadUInt32();
			var wave = new string(reader.ReadChars(4));
			if (wave != "WAVE")
			{
				throw new InvalidDataException("Only WAVE files are supported.");
			}

			WavFormat? format = null;
			byte[]? data = null;
			while (stream.Position + 8 <= stream.Length)
			{
				var id = new string(reader.ReadChars(4));
				var size = reader.ReadUInt32();
				var chunkStart = stream.Position;
				if (id == "fmt ")
				{
					var bytes = reader.ReadBytes(checked((int)size));
					format = WavFormat.Parse(bytes);
				}
				else if (id == "data")
				{
					data = reader.ReadBytes(checked((int)size));
				}

				stream.Position = chunkStart + size + (size & 1);
			}

			if (format == null || data == null)
			{
				throw new InvalidDataException("WAV file is missing fmt or data chunk.");
			}

			return new MeasurementWav(format.SampleRate, DecodeMono(format, data));
		}

		private static float[] DecodeMono(WavFormat format, byte[] data)
		{
			var frameCount = data.Length / format.BlockAlign;
			var samples = new float[frameCount];
			for (var frame = 0; frame < frameCount; frame++)
			{
				var frameOffset = frame * format.BlockAlign;
				var sum = 0.0;
				for (var channel = 0; channel < format.Channels; channel++)
				{
					var sampleOffset = frameOffset + (channel * format.BytesPerSample);
					sum += DecodeSample(format, data.AsSpan(sampleOffset, format.BytesPerSample));
				}

				samples[frame] = (float)(sum / format.Channels);
			}

			return samples;
		}

		private static double DecodeSample(WavFormat format, ReadOnlySpan<byte> bytes)
		{
			if (format.AudioFormat == 3 && format.BitsPerSample == 32)
			{
				return BitConverter.ToSingle(bytes);
			}

			if (format.AudioFormat != 1)
			{
				throw new InvalidDataException("Unsupported WAV format tag: " + format.AudioFormat.ToString(CultureInfo.InvariantCulture));
			}

			return format.BitsPerSample switch
			{
				16 => BinaryPrimitives.ReadInt16LittleEndian(bytes) / 32768.0,
				24 => DecodeInt24(bytes) / 8388608.0,
				32 => BinaryPrimitives.ReadInt32LittleEndian(bytes) / 2147483648.0,
				_ => throw new InvalidDataException("Unsupported PCM bit depth: " + format.BitsPerSample.ToString(CultureInfo.InvariantCulture))
			};
		}

		private static int DecodeInt24(ReadOnlySpan<byte> bytes)
		{
			var value = bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);
			if ((value & 0x800000) != 0)
			{
				value |= unchecked((int)0xFF000000);
			}

			return value;
		}

		private sealed record WavFormat(int AudioFormat, int Channels, int SampleRate, int BlockAlign, int BitsPerSample)
		{
			public int BytesPerSample => BitsPerSample / 8;

			public static WavFormat Parse(byte[] bytes)
			{
				if (bytes.Length < 16)
				{
					throw new InvalidDataException("WAV fmt chunk is too short.");
				}

				var audioFormat = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0, 2));
				var channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2, 2));
				var sampleRate = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4));
				var blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(12, 2));
				var bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(14, 2));
				return new WavFormat(audioFormat, channels, sampleRate, blockAlign, bitsPerSample);
			}
		}
	}
}
