using System.Runtime.InteropServices;
using Miniaudio;
using static Miniaudio.ma_format;
using static Miniaudio.ma_result;

namespace CopperScreen;

internal static unsafe class MiniaudioSampleDecoder
{
	private const int Channels = 2;
	private const int FramesPerRead = 4096;

	public static float[] DecodeStereo(string path, int sampleRate)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

		var decoder = (ma_decoder*)NativeMemory.AllocZeroed((nuint)sizeof(ma_decoder));
		var initialized = false;
		try
		{
			var config = ma.decoder_config_init(ma_format_f32, Channels, checked((uint)sampleRate));
			var result = InitializeFile(path, &config, decoder);
			if (result != MA_SUCCESS)
			{
				throw new InvalidDataException($"miniaudio could not decode '{path}' ({result}).");
			}

			initialized = true;
			var readBuffer = new float[FramesPerRead * Channels];
			var samples = new List<float>();
			fixed (float* buffer = readBuffer)
			{
				while (true)
				{
					ulong framesRead = 0;
					result = ma.decoder_read_pcm_frames(decoder, buffer, FramesPerRead, &framesRead);
					if (framesRead > 0)
					{
						var sampleCount = checked((int)framesRead * Channels);
						for (var i = 0; i < sampleCount; i++)
						{
							samples.Add(readBuffer[i]);
						}
					}

					if (framesRead == 0 || result != MA_SUCCESS)
					{
						break;
					}
				}
			}

			return samples.ToArray();
		}
		finally
		{
			if (initialized)
			{
				ma.decoder_uninit(decoder);
			}

			NativeMemory.Free(decoder);
		}
	}

	private static ma_result InitializeFile(string path, ma_decoder_config* config, ma_decoder* decoder)
	{
		if (OperatingSystem.IsWindows())
		{
			fixed (char* pathPointer = path)
			{
				return ma.decoder_init_file_w((ushort*)pathPointer, config, decoder);
			}
		}

		var utf8Path = Marshal.StringToCoTaskMemUTF8(path);
		try
		{
			return ma.decoder_init_file((sbyte*)utf8Path, config, decoder);
		}
		finally
		{
			Marshal.FreeCoTaskMem(utf8Path);
		}
	}
}
