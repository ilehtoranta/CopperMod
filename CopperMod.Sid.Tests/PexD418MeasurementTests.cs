using System.Buffers.Binary;
using System.Globalization;

namespace CopperMod.Sid.Tests;

public sealed class PexD418MeasurementTests
{
	private const string MeasurementRootName = "Musik_RunStop_8-bit_sample_measurements_by_Pex_Mahoney_Tufvesson";
	private const int EnvelopeWindowSampleCount = 6;
	private const int EnvelopeFitSampleCount = 4;
	private const double Mos6581EnvelopeFitThreshold = 0.010;
	private const double Mos8580EnvelopeFitThreshold = 0.002;

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
	public void OptionalPexRawWavCapturesRegenerateMeasuredD418TransitionMatrices()
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

		var captures = GetFullMatrixCaptures(root);
		foreach (var capture in captures)
		{
			if (!File.Exists(capture.Path))
			{
				AssertNotStrict("Pex capture WAV was not found: " + capture.Path);
				return;
			}
		}

		var regenerated = BuildNormalizedTransitionMatrices(captures);

		AssertMatrixMatches("6581 pre-write", regenerated.Mos6581PreWrite, SidD418TransitionMatrices.Mos6581PreWrite);
		AssertMatrixMatches("6581 post-write", regenerated.Mos6581PostWrite, SidD418TransitionMatrices.Mos6581PostWrite);
		AssertMatrixMatches("8580 pre-write", regenerated.Mos8580PreWrite, SidD418TransitionMatrices.Mos8580PreWrite);
		AssertMatrixMatches("8580 post-write", regenerated.Mos8580PostWrite, SidD418TransitionMatrices.Mos8580PostWrite);
		Assert.Equal(SidD418TransitionMatrices.Mos6581TransientAttackSeconds, regenerated.Mos6581TransientAttackSeconds, precision: 15);
		Assert.Equal(SidD418TransitionMatrices.Mos6581TransientDecaySeconds, regenerated.Mos6581TransientDecaySeconds, precision: 12);
		Assert.Equal(SidD418TransitionMatrices.Mos8580TransientAttackSeconds, regenerated.Mos8580TransientAttackSeconds, precision: 15);
		Assert.Equal(SidD418TransitionMatrices.Mos8580TransientDecaySeconds, regenerated.Mos8580TransientDecaySeconds, precision: 12);
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

	private static PexCapture[] GetFullMatrixCaptures(string root)
	{
		return new[]
		{
			new PexCapture(
				Path.Combine(root, "Pex_testfiles", "Hedning_6581R4_Gubbdata_Compo_96kHz_24bit.wav"),
				SidChipModel.Mos6581,
				219470,
				3476292),
			new PexCapture(
				Path.Combine(root, "Pex_testfiles", "Hedning_6581R4_SX64_96kHz_24bit.wav"),
				SidChipModel.Mos6581,
				193964,
				3450805),
			new PexCapture(
				Path.Combine(root, "Pex_testfiles", "Bepp_8580R5_Pepp_96kHz_24bit.wav"),
				SidChipModel.Mos8580,
				147995,
				3404880),
			new PexCapture(
				Path.Combine(root, "Pex_testfiles", "Bepp_6581_1185_sn1532716_96kHz_24bit.wav"),
				SidChipModel.Mos6581,
				143217,
				3399978),
			new PexCapture(
				Path.Combine(root, "Pex_testfiles", "Bepp_6581_3684_brokenesc_96kHz_24bit.wav"),
				SidChipModel.Mos6581,
				233037,
				3489947),
			new PexCapture(
				Path.Combine(root, "Pex_testfiles", "Bepp_8580R5_bread_96kHz_32bit_fixed.wav"),
				SidChipModel.Mos8580,
				147995,
				3404870)
		};
	}

	private static MeasuredTransitionMatrices BuildNormalizedTransitionMatrices(PexCapture[] captures)
	{
		var mos6581 = new MatrixAccumulator(Mos6581EnvelopeFitThreshold);
		var mos8580 = new MatrixAccumulator(Mos8580EnvelopeFitThreshold);

		foreach (var capture in captures)
		{
			var wav = MeasurementWavReader.ReadMono(capture.Path);
			Assert.Equal(96000, wav.SampleRate);
			var normalized = NormalizeFullRange(wav.Samples);
			var matrices = MeasureTransitionMatrix(normalized, capture);
			NormalizeTransitionMatrices(matrices, capture.Model);

			if (capture.Model == SidChipModel.Mos8580)
			{
				mos8580.Add(matrices);
			}
			else
			{
				mos6581.Add(matrices);
			}
		}

		return new MeasuredTransitionMatrices(
			mos6581.BuildPreWriteAverage(),
			mos6581.BuildPostWriteAverage(),
			mos8580.BuildPreWriteAverage(),
			mos8580.BuildPostWriteAverage(),
			(float)(1.0 / 96000.0),
			mos6581.BuildDecaySeconds(96000),
			(float)(1.0 / 96000.0),
			mos8580.BuildDecaySeconds(96000));
	}

	private static TransitionMatrices MeasureTransitionMatrix(float[] samples, PexCapture capture)
	{
		var matrices = new TransitionMatrices();
		for (var from = 0; from < 256; from++)
		{
			for (var to = 0; to < 256; to++)
			{
				MeasureTransitionValue(samples, capture, from, to, matrices);
			}
		}

		return matrices;
	}

	private static void NormalizeTransitionMatrices(TransitionMatrices matrices, SidChipModel model)
	{
		var means = new double[256];
		for (var value = 0; value < means.Length; value++)
		{
			var fromSum = 0.0;
			var toSum = 0.0;
			for (var other = 0; other < 256; other++)
			{
				fromSum += matrices.PreWrite[(value << 8) | other];
				toSum += matrices.PostWrite[(other << 8) | value];
			}

			means[value] = ((fromSum / 256.0) + (toSum / 256.0)) * 0.5;
		}

		var maxValue = means[0x0F];
		var minValue = means[0x9F];
		if (model == SidChipModel.Mos8580)
		{
			var scale = 2.0 / (maxValue - minValue);
			var bias = -1.0 - (minValue * scale);
			NormalizeAffine(matrices.PreWrite, scale, bias);
			NormalizeAffine(matrices.PostWrite, scale, bias);
			NormalizeWindows(matrices.PostWriteWindow, scale, bias);
			NormalizeAffine(means, scale, bias);
			matrices.MeanOutput = means;
		}
		else
		{
			var maxAmplitude = Math.Max(maxValue, -minValue);
			NormalizeAffine(matrices.PreWrite, 1.0 / maxAmplitude, 0.0);
			NormalizeAffine(matrices.PostWrite, 1.0 / maxAmplitude, 0.0);
			NormalizeWindows(matrices.PostWriteWindow, 1.0 / maxAmplitude, 0.0);
			NormalizeAffine(means, 1.0 / maxAmplitude, 0.0);
			matrices.MeanOutput = means;
		}
	}

	private static void NormalizeAffine(double[] values, double scale, double bias)
	{
		for (var i = 0; i < values.Length; i++)
		{
			values[i] = (values[i] * scale) + bias;
		}
	}

	private static void NormalizeWindows(double[][] windows, double scale, double bias)
	{
		for (var i = 0; i < windows.Length; i++)
		{
			NormalizeAffine(windows[i], scale, bias);
		}
	}

	private static void AssertMatrixMatches(string name, double[] regenerated, ReadOnlySpan<float> runtime)
	{
		Assert.Equal(SidAnalog.D418TransitionMatrixLength, regenerated.Length);
		Assert.Equal(SidAnalog.D418TransitionMatrixLength, runtime.Length);
		for (var i = 0; i < regenerated.Length; i++)
		{
			var delta = Math.Abs(regenerated[i] - runtime[i]);
			Assert.True(delta <= 0.000001, $"{name} matrix mismatch at transition ${i >> 8:X2}->${i & 0xFF:X2}: regenerated {regenerated[i]:0.000000000}, runtime {runtime[i]:0.000000000}, delta {delta:0.000000000}.");
		}
	}

	private static (double From, double To) MeasureTransitionValue(float[] samples, PexCapture capture, int from, int to)
	{
		var matrices = new TransitionMatrices();
		MeasureTransitionValue(samples, capture, from, to, matrices);
		var index = (from << 8) | to;
		return (matrices.PreWrite[index], matrices.PostWrite[index]);
	}

	private static void MeasureTransitionValue(float[] samples, PexCapture capture, int from, int to, TransitionMatrices matrices)
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
		matrices.PreWrite[transitionIndex] = fromValue - zeroFrom;
		matrices.PostWrite[transitionIndex] = toValue - zeroTo;
		for (var sample = 0; sample < EnvelopeWindowSampleCount; sample++)
		{
			var windowIndex = RoundMatlab(index + 13.0 + sample);
			var windowFraction = (windowIndex - zeroLeftIndex) / (double)(zeroRightIndex - zeroLeftIndex);
			var zeroWindow = (zeroLeft * (1.0 - windowFraction)) + (zeroRight * windowFraction);
			matrices.PostWriteWindow[sample][transitionIndex] = AverageAtMatlabIndex(samples, windowIndex) - zeroWindow;
		}
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

	private sealed class TransitionMatrices
	{
		public double[] PreWrite { get; } = new double[SidAnalog.D418TransitionMatrixLength];

		public double[] PostWrite { get; } = new double[SidAnalog.D418TransitionMatrixLength];

		public double[][] PostWriteWindow { get; } = CreatePostWriteWindow();

		public double[] MeanOutput { get; set; } = Array.Empty<double>();

		private static double[][] CreatePostWriteWindow()
		{
			var window = new double[EnvelopeWindowSampleCount][];
			for (var i = 0; i < window.Length; i++)
			{
				window[i] = new double[SidAnalog.D418TransitionMatrixLength];
			}

			return window;
		}
	}

	private sealed record MeasuredTransitionMatrices(
		double[] Mos6581PreWrite,
		double[] Mos6581PostWrite,
		double[] Mos8580PreWrite,
		double[] Mos8580PostWrite,
		double Mos6581TransientAttackSeconds,
		double Mos6581TransientDecaySeconds,
		double Mos8580TransientAttackSeconds,
		double Mos8580TransientDecaySeconds);

	private sealed class MatrixAccumulator
	{
		private readonly double[] _preWrite = new double[SidAnalog.D418TransitionMatrixLength];
		private readonly double[] _postWrite = new double[SidAnalog.D418TransitionMatrixLength];
		private readonly double _envelopeFitThreshold;
		private readonly List<double>[] _envelopeRatios;
		private int _count;

		public MatrixAccumulator(double envelopeFitThreshold)
		{
			_envelopeFitThreshold = envelopeFitThreshold;
			_envelopeRatios = new List<double>[EnvelopeFitSampleCount];
			for (var i = 0; i < _envelopeRatios.Length; i++)
			{
				_envelopeRatios[i] = new List<double>();
			}
		}

		public void Add(TransitionMatrices matrices)
		{
			for (var i = 0; i < SidAnalog.D418TransitionMatrixLength; i++)
			{
				_preWrite[i] += matrices.PreWrite[i];
				_postWrite[i] += matrices.PostWrite[i];
			}

			AddEnvelopeRatios(matrices);
			_count++;
		}

		public double[] BuildPreWriteAverage()
		{
			return BuildAverage(_preWrite);
		}

		public double[] BuildPostWriteAverage()
		{
			return BuildAverage(_postWrite);
		}

		public double BuildDecaySeconds(int sampleRate)
		{
			Assert.True(_count > 0);
			var sumXy = 0.0;
			var sumXx = 0.0;
			for (var i = 0; i < _envelopeRatios.Length; i++)
			{
				var median = Median(_envelopeRatios[i]);
				var sampleOffset = i + 1.0;
				sumXy += sampleOffset * Math.Log(median);
				sumXx += sampleOffset * sampleOffset;
			}

			var slope = sumXy / sumXx;
			Assert.True(slope < 0.0, "Expected regenerated D418 envelope to decay.");
			return (float)((-1.0 / slope) / sampleRate);
		}

		private void AddEnvelopeRatios(TransitionMatrices matrices)
		{
			for (var i = 0; i < SidAnalog.D418TransitionMatrixLength; i++)
			{
				var target = matrices.MeanOutput[i & 0xFF];
				var initialResidual = matrices.PostWriteWindow[0][i] - target;
				if (Math.Abs(initialResidual) < _envelopeFitThreshold)
				{
					continue;
				}

				for (var sample = 1; sample <= EnvelopeFitSampleCount; sample++)
				{
					var residual = matrices.PostWriteWindow[sample][i] - target;
					if (Math.Sign(residual) != Math.Sign(initialResidual))
					{
						continue;
					}

					var ratio = residual / initialResidual;
					if (ratio > 0.0 && ratio < 2.0)
					{
						_envelopeRatios[sample - 1].Add(ratio);
					}
				}
			}
		}

		private static double Median(List<double> values)
		{
			Assert.NotEmpty(values);
			values.Sort();
			var middle = values.Count / 2;
			return (values.Count & 1) == 0
				? (values[middle - 1] + values[middle]) * 0.5
				: values[middle];
		}

		private double[] BuildAverage(double[] source)
		{
			Assert.True(_count > 0);
			var average = new double[SidAnalog.D418TransitionMatrixLength];
			for (var i = 0; i < average.Length; i++)
			{
				average[i] = source[i] / _count;
			}

			return average;
		}
	}

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
