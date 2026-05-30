using System.Runtime.InteropServices;

namespace CopperScreen;

internal sealed class WaveOutAudioOutput : ICopperScreenAudioOutput
{
	private const int WaveMapper = -1;
	private const int WaveFormatPcm = 1;
	private const int WhdrDone = 0x00000001;
	private readonly IntPtr _handle;
	private readonly Buffer[] _buffers;
	private readonly bool _timerPeriodRaised;
	private bool _disposed;

	private WaveOutAudioOutput(IntPtr handle, int bytesPerBuffer, int bufferCount, bool timerPeriodRaised)
	{
		_handle = handle;
		_timerPeriodRaised = timerPeriodRaised;
		_buffers = new Buffer[Math.Max(2, bufferCount)];
		for (var i = 0; i < _buffers.Length; i++)
		{
			_buffers[i] = new Buffer(bytesPerBuffer);
			Prepare(_buffers[i]);
		}
	}

	public static WaveOutAudioOutput? TryCreate(int sampleRate, int channels, int framesPerBuffer, int bufferCount)
	{
		if (!OperatingSystem.IsWindows())
		{
			return null;
		}

		var format = new WaveFormatEx
		{
			WFormatTag = WaveFormatPcm,
			NChannels = (ushort)channels,
			NSamplesPerSec = (uint)sampleRate,
			WBitsPerSample = 16,
			NBlockAlign = (ushort)(channels * sizeof(short)),
			NAvgBytesPerSec = (uint)(sampleRate * channels * sizeof(short)),
			CbSize = 0
		};
		var result = waveOutOpen(out var handle, unchecked((uint)WaveMapper), ref format, IntPtr.Zero, IntPtr.Zero, 0);
		if (result != 0)
		{
			return null;
		}

		var timerPeriodRaised = timeBeginPeriod(1) == 0;
		return new WaveOutAudioOutput(handle, framesPerBuffer * channels * sizeof(short), bufferCount, timerPeriodRaised);
	}

	public int QueuedBufferCount
	{
		get
		{
			RefreshCompletedBuffers();
			var count = 0;
			foreach (var buffer in _buffers)
			{
				if (buffer.InUse)
				{
					count++;
				}
			}

			return count;
		}
	}

	public bool Submit(ReadOnlySpan<float> samples)
	{
		if (_disposed || samples.IsEmpty)
		{
			return false;
		}

		var buffer = FindAvailableBuffer();
		if (buffer == null)
		{
			return false;
		}

		var sampleCount = Math.Min(samples.Length, buffer.ByteLength / sizeof(short));
		for (var i = 0; i < sampleCount; i++)
		{
			var value = (short)Math.Clamp((int)MathF.Round(samples[i] * short.MaxValue), short.MinValue, short.MaxValue);
			Marshal.WriteInt16(buffer.Data, i * sizeof(short), value);
		}

		var header = Marshal.PtrToStructure<WaveHeader>(buffer.Header);
		header.DwBufferLength = (uint)(sampleCount * sizeof(short));
		header.DwFlags &= unchecked((uint)~WhdrDone);
		Marshal.StructureToPtr(header, buffer.Header, false);
		if (waveOutWrite(_handle, buffer.Header, (uint)Marshal.SizeOf<WaveHeader>()) != 0)
		{
			return false;
		}

		buffer.InUse = true;
		return true;
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		waveOutReset(_handle);
		foreach (var buffer in _buffers)
		{
			waveOutUnprepareHeader(_handle, buffer.Header, (uint)Marshal.SizeOf<WaveHeader>());
			buffer.Dispose();
		}

		waveOutClose(_handle);
		if (_timerPeriodRaised)
		{
			timeEndPeriod(1);
		}
	}

	private Buffer? FindAvailableBuffer()
	{
		RefreshCompletedBuffers();
		foreach (var buffer in _buffers)
		{
			if (!buffer.InUse)
			{
				return buffer;
			}
		}

		return null;
	}

	private void RefreshCompletedBuffers()
	{
		foreach (var buffer in _buffers)
		{
			if (!buffer.InUse)
			{
				continue;
			}

			var header = Marshal.PtrToStructure<WaveHeader>(buffer.Header);
			if ((header.DwFlags & WhdrDone) != 0)
			{
				buffer.InUse = false;
			}
		}
	}

	private void Prepare(Buffer buffer)
	{
		var header = new WaveHeader
		{
			LpData = buffer.Data,
			DwBufferLength = (uint)buffer.ByteLength
		};
		Marshal.StructureToPtr(header, buffer.Header, false);
		waveOutPrepareHeader(_handle, buffer.Header, (uint)Marshal.SizeOf<WaveHeader>());
	}

	private sealed class Buffer : IDisposable
	{
		public Buffer(int byteLength)
		{
			ByteLength = byteLength;
			Data = Marshal.AllocHGlobal(byteLength);
			Header = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
		}

		public int ByteLength { get; }

		public IntPtr Data { get; }

		public IntPtr Header { get; }

		public bool InUse { get; set; }

		public void Dispose()
		{
			Marshal.FreeHGlobal(Header);
			Marshal.FreeHGlobal(Data);
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct WaveFormatEx
	{
		public ushort WFormatTag;
		public ushort NChannels;
		public uint NSamplesPerSec;
		public uint NAvgBytesPerSec;
		public ushort NBlockAlign;
		public ushort WBitsPerSample;
		public ushort CbSize;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct WaveHeader
	{
		public IntPtr LpData;
		public uint DwBufferLength;
		public uint DwBytesRecorded;
		public IntPtr DwUser;
		public uint DwFlags;
		public uint DwLoops;
		public IntPtr LpNext;
		public IntPtr Reserved;
	}

	[DllImport("winmm.dll")]
	private static extern int waveOutOpen(out IntPtr waveOut, uint deviceId, ref WaveFormatEx format, IntPtr callback, IntPtr instance, uint flags);

	[DllImport("winmm.dll")]
	private static extern int waveOutPrepareHeader(IntPtr waveOut, IntPtr header, uint headerSize);

	[DllImport("winmm.dll")]
	private static extern int waveOutUnprepareHeader(IntPtr waveOut, IntPtr header, uint headerSize);

	[DllImport("winmm.dll")]
	private static extern int waveOutWrite(IntPtr waveOut, IntPtr header, uint headerSize);

	[DllImport("winmm.dll")]
	private static extern int waveOutReset(IntPtr waveOut);

	[DllImport("winmm.dll")]
	private static extern int waveOutClose(IntPtr waveOut);

	[DllImport("winmm.dll")]
	private static extern int timeBeginPeriod(uint period);

	[DllImport("winmm.dll")]
	private static extern int timeEndPeriod(uint period);
}
