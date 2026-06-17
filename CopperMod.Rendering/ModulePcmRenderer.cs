using CopperMod.Abstractions;
using CopperMod.Sid;

namespace CopperMod.Rendering;

public sealed class ModulePcmRenderer : IDisposable
{
	private readonly IModuleSong _song;
	private readonly ModuleRenderSettings _settings;
	private readonly AudioRenderOptions _renderOptions;
	private readonly AmigaOutputStage _amigaOutputStage;
	private readonly C64OutputStage _c64OutputStage;
	private readonly IAmigaHardwareStateProvider? _amigaHardwareStateProvider;
	private readonly IModuleOutputFamilyProvider? _outputFamilyProvider;
	private float[] _pendingSamples = Array.Empty<float>();
	private int _pendingOffset;
	private int _pendingCount;
	private bool _endOfSong;
	private bool _disposed;

	public ModulePcmRenderer(IModuleSong song, ModuleRenderSettings? settings = null)
	{
		_song = song ?? throw new ArgumentNullException(nameof(song));
		_settings = settings ?? new ModuleRenderSettings();
		_renderOptions = _settings.ToAudioRenderOptions();
		_amigaOutputStage = new AmigaOutputStage(_settings.AmigaOutputProfile);
		_c64OutputStage = new C64OutputStage(_settings.C64OutputProfile);
		_amigaHardwareStateProvider = song as IAmigaHardwareStateProvider;
		_outputFamilyProvider = song as IModuleOutputFamilyProvider;
		if (song is ISidEmulationProfileController sidProfileController)
		{
			sidProfileController.SidEmulationProfile = _settings.SidEmulationProfile;
		}
	}

	public IModuleSong Song => _song;

	public ModuleRenderSettings Settings => _settings;

	public PlaybackPosition Position => _song.Position;

	public bool EndOfSong => _endOfSong && _pendingOffset >= _pendingCount;

	public int Read(Span<float> destination)
	{
		ThrowIfDisposed();
		var samplesWritten = 0;
		while (samplesWritten < destination.Length)
		{
			var copied = CopyPending(destination.Slice(samplesWritten));
			samplesWritten += copied;
			if (samplesWritten >= destination.Length || EndOfSong)
			{
				break;
			}

			RenderNextTick();
			if (_pendingCount == _pendingOffset && !_endOfSong)
			{
				break;
			}
		}

		if (samplesWritten < destination.Length)
		{
			destination.Slice(samplesWritten).Clear();
		}

		return samplesWritten;
	}

	public void Reset()
	{
		ThrowIfDisposed();
		_song.Reset();
		_amigaOutputStage.Reset();
		_c64OutputStage.Reset();
		_pendingOffset = 0;
		_pendingCount = 0;
		_endOfSong = false;
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_song.Dispose();
		_disposed = true;
	}

	private int CopyPending(Span<float> destination)
	{
		var available = _pendingCount - _pendingOffset;
		if (available <= 0)
		{
			_pendingOffset = 0;
			_pendingCount = 0;
			return 0;
		}

		var count = Math.Min(destination.Length, available);
		_pendingSamples.AsSpan(_pendingOffset, count).CopyTo(destination);
		_pendingOffset += count;
		if (_pendingOffset >= _pendingCount)
		{
			_pendingOffset = 0;
			_pendingCount = 0;
		}

		return count;
	}

	private void RenderNextTick()
	{
		if (_endOfSong)
		{
			return;
		}

		var frames = _song.GetCurrentTickFrameCount(_renderOptions);
		if (frames <= 0)
		{
			_endOfSong = true;
			return;
		}

		var requestedSamples = _renderOptions.GetSampleCount(frames);
		EnsurePendingCapacity(requestedSamples);
		var result = _song.RenderTick(_pendingSamples.AsSpan(0, requestedSamples), _renderOptions);
		_pendingOffset = 0;
		_pendingCount = Math.Min(result.SamplesWritten, requestedSamples);

		if (_pendingCount > 0 && _settings.OutputMode == ModuleRenderOutputMode.Player)
		{
			ApplyPlayerOutput(_pendingSamples.AsSpan(0, _pendingCount));
		}

		_endOfSong = result.EndOfSong;
	}

	private void ApplyPlayerOutput(Span<float> samples)
	{
		if ((_outputFamilyProvider?.OutputFamily ?? ModuleOutputFamily.Amiga) == ModuleOutputFamily.Commodore64)
		{
			_c64OutputStage.Process(samples, _renderOptions.ChannelCount, _renderOptions.SampleRate);
		}
		else
		{
			_amigaOutputStage.Process(
				samples,
				_renderOptions.ChannelCount,
				_renderOptions.SampleRate,
				_amigaHardwareStateProvider?.AmigaHardwareState.AudioFilterEnabled == true);
		}
	}

	private void EnsurePendingCapacity(int sampleCount)
	{
		if (_pendingSamples.Length < sampleCount)
		{
			_pendingSamples = new float[sampleCount];
		}
	}

	private void ThrowIfDisposed()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(ModulePcmRenderer));
		}
	}
}
