using System.Buffers.Binary;
using CopperMod.Rendering;
using NAudio.Wave;

namespace CopperMod.Tools;

internal sealed class RenderedPcm16WaveProvider : IWaveProvider, IDisposable
{
	private readonly ModulePcmRenderer _renderer;
	private readonly long? _totalSamples;
	private float[] _floatBuffer = Array.Empty<float>();
	private long _samplesProduced;

	public RenderedPcm16WaveProvider(ModulePcmRenderer renderer, TimeSpan? duration)
	{
		_renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
		WaveFormat = new WaveFormat(renderer.Settings.SampleRate, 16, renderer.Settings.ChannelCount);
		if (duration.HasValue)
		{
			_totalSamples = checked((long)Math.Ceiling(duration.Value.TotalSeconds * renderer.Settings.SampleRate) * renderer.Settings.ChannelCount);
		}
	}

	public WaveFormat WaveFormat { get; }

	public int Read(byte[] buffer, int offset, int count)
	{
		if (buffer is null)
		{
			throw new ArgumentNullException(nameof(buffer));
		}

		if (offset < 0 || count < 0 || offset + count > buffer.Length)
		{
			throw new ArgumentOutOfRangeException(nameof(offset));
		}

		var sampleCapacity = count / sizeof(short);
		if (sampleCapacity == 0)
		{
			return 0;
		}

		if (_totalSamples.HasValue)
		{
			var remaining = _totalSamples.Value - _samplesProduced;
			if (remaining <= 0)
			{
				return 0;
			}

			sampleCapacity = (int)Math.Min(sampleCapacity, remaining);
		}

		EnsureFloatCapacity(sampleCapacity);
		var samplesRead = _renderer.Read(_floatBuffer.AsSpan(0, sampleCapacity));
		var samplesToEncode = _totalSamples.HasValue ? sampleCapacity : samplesRead;
		if (samplesToEncode <= 0)
		{
			return 0;
		}

		var bytes = buffer.AsSpan(offset, samplesToEncode * sizeof(short));
		for (var i = 0; i < samplesToEncode; i++)
		{
			BinaryPrimitives.WriteInt16LittleEndian(bytes.Slice(i * sizeof(short), sizeof(short)), ToPcm16(_floatBuffer[i]));
		}

		_samplesProduced += samplesToEncode;
		return samplesToEncode * sizeof(short);
	}

	public void Dispose()
	{
		_renderer.Dispose();
	}

	private void EnsureFloatCapacity(int sampleCount)
	{
		if (_floatBuffer.Length < sampleCount)
		{
			_floatBuffer = new float[sampleCount];
		}
	}

	private static short ToPcm16(float sample)
	{
		if (float.IsNaN(sample))
		{
			return 0;
		}

		var clamped = Math.Clamp(sample, -1.0f, 1.0f);
		return clamped < 0.0f
			? (short)Math.Round(clamped * 32768.0f)
			: (short)Math.Round(clamped * 32767.0f);
	}
}
