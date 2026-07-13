using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Miniaudio;
using static Miniaudio.ma_device_type;
using static Miniaudio.ma_format;
using static Miniaudio.ma_result;

namespace CopperScreen;

internal sealed unsafe class MiniaudioAudioOutput : ICopperScreenAudioOutput
{
	private readonly MiniaudioSampleQueue _queue;
	private ma_device* _device;
	private GCHandle _selfHandle;
	private bool _deviceInitialized;
	private int _disposed;

	private MiniaudioAudioOutput(int channels, int samplesPerBuffer, int bufferCount)
	{
		Channels = channels;
		_queue = new MiniaudioSampleQueue(samplesPerBuffer, bufferCount);
	}

	private int Channels { get; }

	public int QueuedBufferCount => _queue.QueuedBufferCount;

	public static MiniaudioAudioOutput? TryCreate(
		int sampleRate,
		int channels,
		int framesPerBuffer,
		int bufferCount)
	{
		if (sampleRate <= 0 || channels <= 0 || framesPerBuffer <= 0 || bufferCount < 2)
		{
			return null;
		}

		MiniaudioAudioOutput? output = null;
		try
		{
			output = new MiniaudioAudioOutput(channels, checked(framesPerBuffer * channels), bufferCount);
			output._selfHandle = GCHandle.Alloc(output);
			output._device = (ma_device*)NativeMemory.AllocZeroed((nuint)sizeof(ma_device));

			var config = ma.device_config_init(ma_device_type_playback);
			config.sampleRate = checked((uint)sampleRate);
			config.periodSizeInFrames = checked((uint)framesPerBuffer);
			config.playback.format = ma_format_f32;
			config.playback.channels = checked((uint)channels);
			config.dataCallback = &DataCallback;
			config.pUserData = (void*)GCHandle.ToIntPtr(output._selfHandle);

			if (ma.device_init(null, &config, output._device) != MA_SUCCESS)
			{
				output.Dispose();
				return null;
			}

			output._deviceInitialized = true;
			if (ma.device_start(output._device) != MA_SUCCESS)
			{
				output.Dispose();
				return null;
			}

			return output;
		}
		catch (Exception ex) when (
			ex is DllNotFoundException or
			EntryPointNotFoundException or
			BadImageFormatException or
			TypeInitializationException)
		{
			output?.Dispose();
			return null;
		}
	}

	public bool Submit(ReadOnlySpan<float> samples)
		=> Volatile.Read(ref _disposed) == 0 && _queue.TryEnqueue(samples);

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		if (_device != null)
		{
			if (_deviceInitialized)
			{
				ma.device_uninit(_device);
				_deviceInitialized = false;
			}

			NativeMemory.Free(_device);
			_device = null;
		}

		if (_selfHandle.IsAllocated)
		{
			_selfHandle.Free();
		}
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static void DataCallback(ma_device* device, void* outputFrames, void* inputFrames, uint frameCount)
	{
		_ = inputFrames;
		if (device == null || outputFrames == null)
		{
			return;
		}

		try
		{
			var handle = GCHandle.FromIntPtr((nint)device->pUserData);
			if (handle.Target is not MiniaudioAudioOutput output || Volatile.Read(ref output._disposed) != 0)
			{
				return;
			}

			var sampleCount = checked((int)frameCount * output.Channels);
			output._queue.Dequeue(new Span<float>(outputFrames, sampleCount));
		}
		catch
		{
			// Exceptions must never escape an unmanaged audio callback.
		}
	}
}

internal sealed class MiniaudioSampleQueue
{
	private readonly float[][] _buffers;
	private readonly int[] _sampleCounts;
	private int _readBufferIndex;
	private int _readSampleOffset;
	private int _writeBufferIndex;
	private int _queuedBufferCount;

	public MiniaudioSampleQueue(int samplesPerBuffer, int bufferCount)
	{
		if (samplesPerBuffer <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(samplesPerBuffer));
		}

		if (bufferCount < 2)
		{
			throw new ArgumentOutOfRangeException(nameof(bufferCount));
		}

		_buffers = new float[bufferCount][];
		_sampleCounts = new int[bufferCount];
		for (var i = 0; i < _buffers.Length; i++)
		{
			_buffers[i] = new float[samplesPerBuffer];
		}
	}

	public int QueuedBufferCount => Volatile.Read(ref _queuedBufferCount);

	public bool TryEnqueue(ReadOnlySpan<float> samples)
	{
		if (samples.IsEmpty || samples.Length > _buffers[_writeBufferIndex].Length)
		{
			return false;
		}

		if (Volatile.Read(ref _queuedBufferCount) >= _buffers.Length)
		{
			return false;
		}

		var writeIndex = _writeBufferIndex;
		samples.CopyTo(_buffers[writeIndex]);
		_sampleCounts[writeIndex] = samples.Length;
		_writeBufferIndex = (writeIndex + 1) % _buffers.Length;
		Interlocked.Increment(ref _queuedBufferCount);
		return true;
	}

	public void Dequeue(Span<float> destination)
	{
		destination.Clear();
		var destinationOffset = 0;
		while (destinationOffset < destination.Length && Volatile.Read(ref _queuedBufferCount) > 0)
		{
			var readIndex = _readBufferIndex;
			var available = _sampleCounts[readIndex] - _readSampleOffset;
			if (available <= 0)
			{
				FinishReadBuffer();
				continue;
			}

			var copyCount = Math.Min(available, destination.Length - destinationOffset);
			_buffers[readIndex].AsSpan(_readSampleOffset, copyCount)
				.CopyTo(destination.Slice(destinationOffset, copyCount));
			_readSampleOffset += copyCount;
			destinationOffset += copyCount;
			if (_readSampleOffset >= _sampleCounts[readIndex])
			{
				FinishReadBuffer();
			}
		}
	}

	private void FinishReadBuffer()
	{
		_sampleCounts[_readBufferIndex] = 0;
		_readSampleOffset = 0;
		_readBufferIndex = (_readBufferIndex + 1) % _buffers.Length;
		Interlocked.Decrement(ref _queuedBufferCount);
	}
}
