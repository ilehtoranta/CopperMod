using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace CopperMod.Tools;

internal static class SidD418MatrixGenerator
{
	public const string CommandName = "generate-sid-d418-matrices";

	private const int MatrixLength = 256 * 256;
	private const int EnvelopeWindowSampleCount = 6;
	private const int EnvelopeFitSampleCount = 4;
	private const double Mos6581EnvelopeFitThreshold = 0.010;
	private const double Mos8580EnvelopeFitThreshold = 0.002;

	private static readonly PexCapture[] Captures =
	{
		new("Pex_testfiles/Hedning_6581R4_Gubbdata_Compo_96kHz_24bit.wav", MeasuredSidModel.Mos6581, 219470, 3476292),
		new("Pex_testfiles/Hedning_6581R4_SX64_96kHz_24bit.wav", MeasuredSidModel.Mos6581, 193964, 3450805),
		new("Pex_testfiles/Bepp_8580R5_Pepp_96kHz_24bit.wav", MeasuredSidModel.Mos8580, 147995, 3404880),
		new("Pex_testfiles/Bepp_6581_1185_sn1532716_96kHz_24bit.wav", MeasuredSidModel.Mos6581, 143217, 3399978),
		new("Pex_testfiles/Bepp_6581_3684_brokenesc_96kHz_24bit.wav", MeasuredSidModel.Mos6581, 233037, 3489947),
		new("Pex_testfiles/Bepp_8580R5_bread_96kHz_32bit_fixed.wav", MeasuredSidModel.Mos8580, 147995, 3404870)
	};

	public static void GenerateFile(SidD418MatrixGeneratorOptions options)
	{
		var matrices = BuildDefaultMatrices(options.InputRoot);
		WriteSource(options.OutputPath, matrices);
	}

	internal static SidD418MatrixSet BuildDefaultMatrices(string inputRoot)
	{
		var mos6581 = new MatrixAccumulator(Mos6581EnvelopeFitThreshold);
		var mos8580 = new MatrixAccumulator(Mos8580EnvelopeFitThreshold);

		foreach (var capture in Captures)
		{
			var capturePath = Path.Combine(inputRoot, capture.RelativePath.Replace('/', Path.DirectorySeparatorChar));
			if (!File.Exists(capturePath))
			{
				throw new FileNotFoundException("Pex SID measurement capture was not found.", capturePath);
			}

			var wav = MeasurementWavReader.ReadMono(capturePath);
			if (wav.SampleRate != 96000)
			{
				throw new InvalidDataException("Pex SID measurement captures must be 96000 Hz: " + capturePath);
			}

			NormalizeFullRangeInPlace(wav.Samples);
			var matrices = MeasureCapture(wav.Samples, capture);
			NormalizeCaptureMatrices(matrices, capture.Model);

			if (capture.Model == MeasuredSidModel.Mos8580)
			{
				mos8580.Add(matrices);
			}
			else
			{
				mos6581.Add(matrices);
			}
		}

		return new SidD418MatrixSet(
			mos6581.BuildPreWriteAverage(),
			mos6581.BuildPostWriteAverage(),
			mos8580.BuildPreWriteAverage(),
			mos8580.BuildPostWriteAverage(),
			1.0f / 96000.0f,
			mos6581.BuildDecaySeconds(96000),
			1.0f / 96000.0f,
			mos8580.BuildDecaySeconds(96000));
	}

	internal static void WriteSource(string outputPath, SidD418MatrixSet matrices)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".");

		using var writer = new StreamWriter(outputPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		writer.WriteLine("using System;");
		writer.WriteLine("using System.CodeDom.Compiler;");
		writer.WriteLine();
		writer.WriteLine("namespace CopperMod.Sid");
		writer.WriteLine("{");
		writer.WriteLine("\t[GeneratedCode(\"CopperMod.Tools generate-sid-d418-matrices\", \"1\")]");
		writer.WriteLine("\tinternal static class SidD418TransitionMatrices");
		writer.WriteLine("\t{");
		writer.WriteLine("\t\tpublic const int MatrixLength = 256 * 256;");
		writer.WriteLine();
		writer.WriteLine("\t\tpublic const double Mos6581TransientAttackSeconds = " + FormatDouble(matrices.Mos6581TransientAttackSeconds) + ";");
		writer.WriteLine("\t\tpublic const double Mos6581TransientDecaySeconds = " + FormatDouble(matrices.Mos6581TransientDecaySeconds) + ";");
		writer.WriteLine("\t\tpublic const double Mos8580TransientAttackSeconds = " + FormatDouble(matrices.Mos8580TransientAttackSeconds) + ";");
		writer.WriteLine("\t\tpublic const double Mos8580TransientDecaySeconds = " + FormatDouble(matrices.Mos8580TransientDecaySeconds) + ";");
		writer.WriteLine();
		WriteArray(writer, "Mos6581PreWriteData", matrices.Mos6581PreWrite);
		WriteArray(writer, "Mos6581PostWriteData", matrices.Mos6581PostWrite);
		WriteArray(writer, "Mos8580PreWriteData", matrices.Mos8580PreWrite);
		WriteArray(writer, "Mos8580PostWriteData", matrices.Mos8580PostWrite);
		writer.WriteLine("\t\tpublic static ReadOnlySpan<float> Mos6581PreWrite => Mos6581PreWriteData;");
		writer.WriteLine();
		writer.WriteLine("\t\tpublic static ReadOnlySpan<float> Mos6581PostWrite => Mos6581PostWriteData;");
		writer.WriteLine();
		writer.WriteLine("\t\tpublic static ReadOnlySpan<float> Mos8580PreWrite => Mos8580PreWriteData;");
		writer.WriteLine();
		writer.WriteLine("\t\tpublic static ReadOnlySpan<float> Mos8580PostWrite => Mos8580PostWriteData;");
		writer.WriteLine();
		WriteGetter(writer, "GetMos6581PreWrite", "Mos6581PreWriteData");
		WriteGetter(writer, "GetMos6581PostWrite", "Mos6581PostWriteData");
		WriteGetter(writer, "GetMos8580PreWrite", "Mos8580PreWriteData");
		WriteGetter(writer, "GetMos8580PostWrite", "Mos8580PostWriteData");
		writer.WriteLine("\t\tprivate static int Index(int previousRegisterValue, int nextRegisterValue)");
		writer.WriteLine("\t\t{");
		writer.WriteLine("\t\t\treturn ((previousRegisterValue & 0xFF) << 8) | (nextRegisterValue & 0xFF);");
		writer.WriteLine("\t\t}");
		writer.WriteLine("\t}");
		writer.WriteLine("}");
	}

	internal static string FormatFloat(float value)
	{
		if (!float.IsFinite(value))
		{
			throw new InvalidDataException("Generated SID transition matrices must contain only finite values.");
		}

		return value.ToString("G9", CultureInfo.InvariantCulture);
	}

	internal static string FormatDouble(double value)
	{
		if (!double.IsFinite(value))
		{
			throw new InvalidDataException("Generated SID transition envelope constants must contain only finite values.");
		}

		return value.ToString("G17", CultureInfo.InvariantCulture);
	}

	private static void WriteArray(TextWriter writer, string name, float[] values)
	{
		if (values.Length != MatrixLength)
		{
			throw new InvalidDataException(name + " must contain 65536 values.");
		}

		writer.WriteLine("\t\tprivate static readonly float[] " + name + " = new float[]");
		writer.WriteLine("\t\t{");
		for (var i = 0; i < values.Length; i += 8)
		{
			writer.Write("\t\t\t");
			var count = Math.Min(8, values.Length - i);
			for (var j = 0; j < count; j++)
			{
				if (j > 0)
				{
					writer.Write(", ");
				}

				writer.Write(FormatFloat(values[i + j]));
				writer.Write('f');
			}

			writer.WriteLine(",");
		}

		writer.WriteLine("\t\t};");
		writer.WriteLine();
	}

	private static void WriteGetter(TextWriter writer, string name, string dataName)
	{
		writer.WriteLine("\t\tpublic static float " + name + "(int previousRegisterValue, int nextRegisterValue)");
		writer.WriteLine("\t\t{");
		writer.WriteLine("\t\t\treturn " + dataName + "[Index(previousRegisterValue, nextRegisterValue)];");
		writer.WriteLine("\t\t}");
		writer.WriteLine();
	}

	private static TransitionMatrices MeasureCapture(float[] samples, PexCapture capture)
	{
		var matrices = new TransitionMatrices();
		var startIndex = capture.FirstValueIndex + 33.0;
		var endIndex = capture.LastValueIndex - 20.0;

		for (var from = 0; from < 256; from++)
		{
			for (var to = 0; to < 256; to++)
			{
				var transitionIndex = (from << 8) | to;
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
		}

		return matrices;
	}

	private static void NormalizeCaptureMatrices(TransitionMatrices matrices, MeasuredSidModel model)
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
		if (model == MeasuredSidModel.Mos8580)
		{
			var scale = 2.0 / (maxValue - minValue);
			var bias = -1.0 - (minValue * scale);
			NormalizeAffine(matrices.PreWrite, scale, bias);
			NormalizeAffine(matrices.PostWrite, scale, bias);
			NormalizeWindows(matrices.PostWriteWindow, scale, bias);
			NormalizeAffine(means, scale, bias);
			matrices.MeanOutput = means;
			return;
		}

		var maxAmplitude = Math.Max(maxValue, -minValue);
		NormalizeAffine(matrices.PreWrite, 1.0 / maxAmplitude, 0.0);
		NormalizeAffine(matrices.PostWrite, 1.0 / maxAmplitude, 0.0);
		NormalizeWindows(matrices.PostWriteWindow, 1.0 / maxAmplitude, 0.0);
		NormalizeAffine(means, 1.0 / maxAmplitude, 0.0);
		matrices.MeanOutput = means;
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

	private static void NormalizeFullRangeInPlace(float[] samples)
	{
		var min = float.PositiveInfinity;
		var max = float.NegativeInfinity;
		for (var i = 0; i < samples.Length; i++)
		{
			min = Math.Min(min, samples[i]);
			max = Math.Max(max, samples[i]);
		}

		var range = Math.Max(1e-12f, max - min);
		for (var i = 0; i < samples.Length; i++)
		{
			samples[i] = (((samples[i] - min) / range) - 0.5f) * 2.0f;
		}
	}

	private static int RoundMatlab(double value)
	{
		return (int)Math.Round(value, MidpointRounding.AwayFromZero);
	}

	private static double AverageAtMatlabIndex(float[] samples, int matlabIndex)
	{
		var center = matlabIndex - 1;
		if (center < 1 || center > samples.Length - 2)
		{
			throw new InvalidDataException("Pex SID measurement index is outside the WAV data.");
		}

		return (samples[center - 1] + samples[center] + samples[center + 1]) / 3.0;
	}

	private sealed class MatrixAccumulator
	{
		private readonly double[] _preWrite = new double[MatrixLength];
		private readonly double[] _postWrite = new double[MatrixLength];
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
			for (var i = 0; i < MatrixLength; i++)
			{
				_preWrite[i] += matrices.PreWrite[i];
				_postWrite[i] += matrices.PostWrite[i];
			}

			AddEnvelopeRatios(matrices);
			_count++;
		}

		public float[] BuildPreWriteAverage()
		{
			return BuildAverage(_preWrite);
		}

		public float[] BuildPostWriteAverage()
		{
			return BuildAverage(_postWrite);
		}

		public float BuildDecaySeconds(int sampleRate)
		{
			if (_count == 0)
			{
				throw new InvalidDataException("No Pex SID measurement captures were accumulated.");
			}

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
			if (slope >= 0.0)
			{
				throw new InvalidDataException("Measured SID transition envelope decay fit did not decay.");
			}

			return (float)((-1.0 / slope) / sampleRate);
		}

		private void AddEnvelopeRatios(TransitionMatrices matrices)
		{
			for (var i = 0; i < MatrixLength; i++)
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
			if (values.Count == 0)
			{
				throw new InvalidDataException("Measured SID transition envelope fit had no usable ratios.");
			}

			values.Sort();
			var middle = values.Count / 2;
			return (values.Count & 1) == 0
				? (values[middle - 1] + values[middle]) * 0.5
				: values[middle];
		}

		private float[] BuildAverage(double[] source)
		{
			if (_count == 0)
			{
				throw new InvalidDataException("No Pex SID measurement captures were accumulated.");
			}

			var average = new float[MatrixLength];
			for (var i = 0; i < average.Length; i++)
			{
				average[i] = (float)(source[i] / _count);
			}

			return average;
		}
	}

	private sealed class TransitionMatrices
	{
		public double[] PreWrite { get; } = new double[MatrixLength];

		public double[] PostWrite { get; } = new double[MatrixLength];

		public double[][] PostWriteWindow { get; } = CreatePostWriteWindow();

		public double[] MeanOutput { get; set; } = Array.Empty<double>();

		private static double[][] CreatePostWriteWindow()
		{
			var window = new double[EnvelopeWindowSampleCount][];
			for (var i = 0; i < window.Length; i++)
			{
				window[i] = new double[MatrixLength];
			}

			return window;
		}
	}

	private enum MeasuredSidModel
	{
		Mos6581,
		Mos8580
	}

	private sealed record PexCapture(string RelativePath, MeasuredSidModel Model, int FirstValueIndex, int LastValueIndex);

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

internal sealed record SidD418MatrixSet(
	float[] Mos6581PreWrite,
	float[] Mos6581PostWrite,
	float[] Mos8580PreWrite,
	float[] Mos8580PostWrite,
	float Mos6581TransientAttackSeconds,
	float Mos6581TransientDecaySeconds,
	float Mos8580TransientAttackSeconds,
	float Mos8580TransientDecaySeconds);
