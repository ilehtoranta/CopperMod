using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CopperScreen;

internal sealed class FloppyDriveAudio : IDisposable
{
	private const int DriveCount = 4;
	private const int MaxTransientVoices = 32;
	private const int MaxStepEventsPerDrivePerFrame = 6;
	private const float LoopFadeSeconds = 0.08f;
	private readonly FloppyDriveSoundPack _pack;
	private readonly TransientVoice[] _voices = new TransientVoice[MaxTransientVoices];
	private readonly DriveAudioState[] _driveStates = new DriveAudioState[DriveCount];
	private readonly CopperScreenDriveState[] _previousDrives = new CopperScreenDriveState[DriveCount];
	private readonly int _sampleRate;
	private readonly float _volume;
	private bool _hasPreviousDriveState;
	private int _stepSampleCursor;
	private int _seekSampleCursor;

	private FloppyDriveAudio(FloppyDriveSoundPack pack, int sampleRate, float volume)
	{
		_pack = pack;
		_sampleRate = sampleRate;
		_volume = FloppyDriveAudioOptions.ClampVolume(volume);
	}

	public bool Enabled => _pack.HasAnySamples && _volume > 0f;

	public static FloppyDriveAudio? TryCreate(
		FloppyDriveAudioOptions options,
		string baseDirectory,
		int sampleRate,
		out string? status)
	{
		status = null;
		if (!options.Enabled)
		{
			return null;
		}

		var soundPackDirectory = ResolveSoundPackDirectory(options.SoundPack, baseDirectory);
		if (!Directory.Exists(soundPackDirectory))
		{
			status = "floppy drive sound pack not found: " + soundPackDirectory;
			return null;
		}

		var pack = FloppyDriveSoundPack.Load(soundPackDirectory, sampleRate);
		if (!pack.HasAnySamples)
		{
			status = "floppy drive sound pack has no loadable samples: " + soundPackDirectory;
			return null;
		}

		return new FloppyDriveAudio(pack, sampleRate, options.Volume);
	}

	public static string ResolveSoundPackDirectory(string soundPack, string baseDirectory)
	{
		var trimmed = string.IsNullOrWhiteSpace(soundPack)
			? FloppyDriveAudioOptions.DefaultSoundPack
			: soundPack.Trim();
		if (Path.IsPathFullyQualified(trimmed))
		{
			return Path.GetFullPath(trimmed);
		}

		if (trimmed.StartsWith(".", StringComparison.Ordinal) ||
			trimmed.Contains(Path.DirectorySeparatorChar) ||
			trimmed.Contains(Path.AltDirectorySeparatorChar))
		{
			return Path.GetFullPath(Path.Combine(baseDirectory, trimmed));
		}

		return Path.GetFullPath(Path.Combine(baseDirectory, "Sounds", "Floppy", trimmed));
	}

	public void Mix(Span<float> interleavedAudio, int frames, int channels, ReadOnlySpan<CopperScreenDriveState> drives)
	{
		if (!Enabled || frames <= 0 || channels <= 0 || interleavedAudio.Length < frames * channels)
		{
			return;
		}

		UpdateDriveTriggers(drives, frames);
		for (var frame = 0; frame < frames; frame++)
		{
			var left = 0f;
			var right = 0f;
			MixMotorLoops(ref left, ref right);
			MixTransientVoices(ref left, ref right);

			left *= _volume;
			right *= _volume;
			var offset = frame * channels;
			if (channels == 1)
			{
				interleavedAudio[offset] += (left + right) * 0.5f;
				continue;
			}

			interleavedAudio[offset] += left;
			interleavedAudio[offset + 1] += right;
			for (var channel = 2; channel < channels; channel++)
			{
				interleavedAudio[offset + channel] += (left + right) * 0.5f;
			}
		}
	}

	public void Dispose()
	{
	}

	private void UpdateDriveTriggers(ReadOnlySpan<CopperScreenDriveState> drives, int frames)
	{
		var count = Math.Min(DriveCount, drives.Length);
		if (!_hasPreviousDriveState)
		{
			for (var driveIndex = 0; driveIndex < count; driveIndex++)
			{
				_previousDrives[driveIndex] = drives[driveIndex];
				if (drives[driveIndex].Connected && drives[driveIndex].HasDisk && drives[driveIndex].MotorOn)
				{
					StartMotor(driveIndex);
				}
			}

			_hasPreviousDriveState = true;
			return;
		}

		for (var driveIndex = 0; driveIndex < count; driveIndex++)
		{
			var previous = _previousDrives[driveIndex];
			var current = drives[driveIndex];
			if (!current.Connected)
			{
				StopMotor(driveIndex);
				_previousDrives[driveIndex] = current;
				continue;
			}

			if (!previous.HasDisk && current.HasDisk)
			{
				StartVoice(_pack.DiskInsert, driveIndex, delayFrames: 0);
			}
			else if (previous.HasDisk && !current.HasDisk)
			{
				StartVoice(_pack.DiskEject, driveIndex, delayFrames: 0);
			}

			if (!previous.MotorOn && current.MotorOn)
			{
				StartMotor(driveIndex);
			}
			else if (previous.MotorOn && !current.MotorOn)
			{
				StopMotor(driveIndex);
			}

			if (previous.HasDisk && current.HasDisk && previous.Cylinder != current.Cylinder)
			{
				QueueSeekOrStep(driveIndex, Math.Abs(current.Cylinder - previous.Cylinder), frames);
			}

			_previousDrives[driveIndex] = current;
		}
	}

	private void StartMotor(int driveIndex)
	{
		StartVoice(_pack.MotorStart, driveIndex, delayFrames: 0);
		var state = _driveStates[driveIndex];
		state.LoopActive = _pack.MotorLoop != null;
		state.LoopTargetGain = state.LoopActive ? 1f : 0f;
		if (!state.LoopActive)
		{
			state.LoopGain = 0f;
			state.LoopFrame = 0;
		}

		_driveStates[driveIndex] = state;
	}

	private void StopMotor(int driveIndex)
	{
		StartVoice(_pack.MotorStop, driveIndex, delayFrames: 0);
		var state = _driveStates[driveIndex];
		state.LoopTargetGain = 0f;
		_driveStates[driveIndex] = state;
	}

	private void QueueSeekOrStep(int driveIndex, int cylinderDelta, int frames)
	{
		var eventCount = Math.Min(MaxStepEventsPerDrivePerFrame, Math.Max(1, cylinderDelta));
		var useSeek = cylinderDelta > 1 && _pack.SeekSamples.Length != 0;
		var samples = useSeek ? _pack.SeekSamples : _pack.StepSamples;
		if (samples.Length == 0)
		{
			return;
		}

		for (var i = 0; i < eventCount; i++)
		{
			var sample = useSeek
				? NextSample(samples, ref _seekSampleCursor)
				: NextSample(samples, ref _stepSampleCursor);
			StartVoice(sample, driveIndex, frames <= 1 ? 0 : (i * frames) / eventCount);
		}
	}

	private static FloppyDriveAudioSample NextSample(FloppyDriveAudioSample[] samples, ref int cursor)
	{
		var sample = samples[cursor % samples.Length];
		cursor = (cursor + 1) & 0x3FFF_FFFF;
		return sample;
	}

	private void StartVoice(FloppyDriveAudioSample? sample, int driveIndex, int delayFrames)
	{
		if (sample == null)
		{
			return;
		}

		for (var i = 0; i < _voices.Length; i++)
		{
			if (_voices[i].Active)
			{
				continue;
			}

			var gain = GetDriveGain(driveIndex);
			_voices[i] = new TransientVoice(sample, 0, Math.Max(0, delayFrames), gain.Left, gain.Right, active: true);
			return;
		}
	}

	private void MixMotorLoops(ref float left, ref float right)
	{
		var sample = _pack.MotorLoop;
		if (sample == null)
		{
			return;
		}

		var fadeStep = 1f / Math.Max(1, (int)(_sampleRate * LoopFadeSeconds));
		for (var driveIndex = 0; driveIndex < _driveStates.Length; driveIndex++)
		{
			var state = _driveStates[driveIndex];
			if (!state.LoopActive && state.LoopGain <= 0f)
			{
				continue;
			}

			if (state.LoopGain < state.LoopTargetGain)
			{
				state.LoopGain = Math.Min(state.LoopTargetGain, state.LoopGain + fadeStep);
			}
			else if (state.LoopGain > state.LoopTargetGain)
			{
				state.LoopGain = Math.Max(state.LoopTargetGain, state.LoopGain - fadeStep);
			}

			if (state.LoopGain <= 0f && state.LoopTargetGain <= 0f)
			{
				state.LoopActive = false;
				state.LoopFrame = 0;
				_driveStates[driveIndex] = state;
				continue;
			}

			var sourceOffset = (state.LoopFrame % sample.FrameCount) * 2;
			var gain = GetDriveGain(driveIndex);
			left += sample.Samples[sourceOffset] * state.LoopGain * gain.Left;
			right += sample.Samples[sourceOffset + 1] * state.LoopGain * gain.Right;
			state.LoopFrame = (state.LoopFrame + 1) % sample.FrameCount;
			_driveStates[driveIndex] = state;
		}
	}

	private void MixTransientVoices(ref float left, ref float right)
	{
		for (var i = 0; i < _voices.Length; i++)
		{
			var voice = _voices[i];
			if (!voice.Active)
			{
				continue;
			}

			if (voice.DelayFrames > 0)
			{
				voice.DelayFrames--;
				_voices[i] = voice;
				continue;
			}

			var sample = voice.Sample;
			var sourceOffset = voice.Frame * 2;
			left += sample.Samples[sourceOffset] * voice.LeftGain;
			right += sample.Samples[sourceOffset + 1] * voice.RightGain;
			voice.Frame++;
			if (voice.Frame >= sample.FrameCount)
			{
				voice.Active = false;
			}

			_voices[i] = voice;
		}
	}

	private static DriveGain GetDriveGain(int driveIndex)
	{
		return driveIndex switch
		{
			0 => new DriveGain(0.85f, 0.55f),
			1 => new DriveGain(0.55f, 0.85f),
			2 => new DriveGain(0.75f, 0.65f),
			_ => new DriveGain(0.65f, 0.75f)
		};
	}

	private readonly record struct DriveGain(float Left, float Right);

	private struct DriveAudioState
	{
		public bool LoopActive;
		public int LoopFrame;
		public float LoopGain;
		public float LoopTargetGain;
	}

	private struct TransientVoice
	{
		public TransientVoice(FloppyDriveAudioSample sample, int frame, int delayFrames, float leftGain, float rightGain, bool active)
		{
			Sample = sample;
			Frame = frame;
			DelayFrames = delayFrames;
			LeftGain = leftGain;
			RightGain = rightGain;
			Active = active;
		}

		public FloppyDriveAudioSample Sample;
		public int Frame;
		public int DelayFrames;
		public float LeftGain;
		public float RightGain;
		public bool Active;
	}

	private sealed class FloppyDriveSoundPack
	{
		private FloppyDriveSoundPack(
			FloppyDriveAudioSample? motorStart,
			FloppyDriveAudioSample? motorLoop,
			FloppyDriveAudioSample? motorStop,
			FloppyDriveAudioSample? diskInsert,
			FloppyDriveAudioSample? diskEject,
			FloppyDriveAudioSample[] stepSamples,
			FloppyDriveAudioSample[] seekSamples)
		{
			MotorStart = motorStart;
			MotorLoop = motorLoop;
			MotorStop = motorStop;
			DiskInsert = diskInsert;
			DiskEject = diskEject;
			StepSamples = stepSamples;
			SeekSamples = seekSamples;
		}

		public FloppyDriveAudioSample? MotorStart { get; }

		public FloppyDriveAudioSample? MotorLoop { get; }

		public FloppyDriveAudioSample? MotorStop { get; }

		public FloppyDriveAudioSample? DiskInsert { get; }

		public FloppyDriveAudioSample? DiskEject { get; }

		public FloppyDriveAudioSample[] StepSamples { get; }

		public FloppyDriveAudioSample[] SeekSamples { get; }

		public bool HasAnySamples =>
			MotorStart != null ||
			MotorLoop != null ||
			MotorStop != null ||
			DiskInsert != null ||
			DiskEject != null ||
			StepSamples.Length != 0 ||
			SeekSamples.Length != 0;

		public static FloppyDriveSoundPack Load(string directory, int sampleRate)
		{
			return new FloppyDriveSoundPack(
				LoadByStem(directory, "motor-start", sampleRate),
				LoadByStem(directory, "motor-loop", sampleRate),
				LoadByStem(directory, "motor-stop", sampleRate),
				LoadByStem(directory, "disk-insert", sampleRate),
				LoadByStem(directory, "disk-eject", sampleRate),
				LoadDirectory(Path.Combine(directory, "step"), sampleRate),
				LoadDirectory(Path.Combine(directory, "seek"), sampleRate));
		}

		private static FloppyDriveAudioSample? LoadByStem(string directory, string stem, int sampleRate)
		{
			if (!Directory.Exists(directory))
			{
				return null;
			}

			foreach (var file in Directory.GetFiles(directory).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
			{
				if (!string.Equals(Path.GetFileNameWithoutExtension(file), stem, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				var sample = LoadSample(file, sampleRate);
				if (sample != null)
				{
					return sample;
				}
			}

			return null;
		}

		private static FloppyDriveAudioSample[] LoadDirectory(string directory, int sampleRate)
		{
			if (!Directory.Exists(directory))
			{
				return [];
			}

			var samples = new List<FloppyDriveAudioSample>();
			foreach (var file in Directory.GetFiles(directory).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
			{
				var sample = LoadSample(file, sampleRate);
				if (sample != null)
				{
					samples.Add(sample);
				}
			}

			return samples.ToArray();
		}

		private static FloppyDriveAudioSample? LoadSample(string path, int targetSampleRate)
		{
			try
			{
				using var reader = new AudioFileReader(path);
				ISampleProvider provider = reader;
				if (provider.WaveFormat.SampleRate != targetSampleRate)
				{
					provider = new WdlResamplingSampleProvider(provider, targetSampleRate);
				}

				var channels = Math.Max(1, provider.WaveFormat.Channels);
				var readBuffer = new float[4096 * channels];
				var samples = new List<float>();
				int read;
				while ((read = provider.Read(readBuffer, 0, readBuffer.Length)) > 0)
				{
					var frames = read / channels;
					for (var frame = 0; frame < frames; frame++)
					{
						var offset = frame * channels;
						var left = readBuffer[offset];
						var right = channels == 1 ? left : readBuffer[offset + 1];
						samples.Add(left);
						samples.Add(right);
					}
				}

				return samples.Count == 0
					? null
					: new FloppyDriveAudioSample(samples.ToArray(), samples.Count / 2);
			}
			catch (Exception)
			{
				return null;
			}
		}
	}

	private sealed class FloppyDriveAudioSample
	{
		public FloppyDriveAudioSample(float[] samples, int frameCount)
		{
			Samples = samples;
			FrameCount = frameCount;
		}

		public float[] Samples { get; }

		public int FrameCount { get; }
	}
}
