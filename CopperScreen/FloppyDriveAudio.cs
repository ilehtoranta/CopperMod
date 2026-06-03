using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CopperScreen;

internal sealed class FloppyDriveAudio : IDisposable
{
	private const int DriveCount = 4;
	private const int MaxStepEventsPerDrivePerFrame = 6;
	private readonly IFloppyDriveSoundSource _source;
	private readonly CopperScreenDriveState[] _previousDrives = new CopperScreenDriveState[DriveCount];
	private readonly float _volume;
	private bool _hasPreviousDriveState;

	private FloppyDriveAudio(IFloppyDriveSoundSource source, float volume)
	{
		_source = source;
		_volume = FloppyDriveAudioOptions.ClampVolume(volume);
	}

	public bool Enabled => _source.Enabled && _volume > 0f;

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

		if (options.Mode == FloppyDriveAudioMode.Synthetic)
		{
			return new FloppyDriveAudio(new SyntheticFloppyDriveSoundSource(sampleRate), options.Volume);
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

		return new FloppyDriveAudio(new SampleFloppyDriveSoundSource(pack, sampleRate), options.Volume);
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
			_source.MixFrame(ref left, ref right);

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
		=> _source.Dispose();

	internal static DriveGain GetDriveGain(int driveIndex)
	{
		return driveIndex switch
		{
			0 => new DriveGain(0.85f, 0.55f),
			1 => new DriveGain(0.55f, 0.85f),
			2 => new DriveGain(0.75f, 0.65f),
			_ => new DriveGain(0.65f, 0.75f)
		};
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
					_source.StartMotor(driveIndex);
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
				_source.StopMotor(driveIndex);
				_previousDrives[driveIndex] = current;
				continue;
			}

			if (!previous.HasDisk && current.HasDisk)
			{
				_source.PlayDiskInsert(driveIndex);
			}
			else if (previous.HasDisk && !current.HasDisk)
			{
				_source.PlayDiskEject(driveIndex);
			}

			if (!previous.MotorOn && current.MotorOn)
			{
				_source.StartMotor(driveIndex);
			}
			else if (previous.MotorOn && !current.MotorOn)
			{
				_source.StopMotor(driveIndex);
			}

			if (previous.HasDisk && current.HasDisk && previous.Cylinder != current.Cylinder)
			{
				_source.QueueSeekOrStep(driveIndex, Math.Abs(current.Cylinder - previous.Cylinder), frames);
			}

			_previousDrives[driveIndex] = current;
		}
	}

	internal readonly record struct DriveGain(float Left, float Right);

	private interface IFloppyDriveSoundSource : IDisposable
	{
		bool Enabled { get; }

		void StartMotor(int driveIndex);

		void StopMotor(int driveIndex);

		void PlayDiskInsert(int driveIndex);

		void PlayDiskEject(int driveIndex);

		void QueueSeekOrStep(int driveIndex, int cylinderDelta, int frames);

		void MixFrame(ref float left, ref float right);
	}

	private sealed class SampleFloppyDriveSoundSource : IFloppyDriveSoundSource
	{
		private const int MaxTransientVoices = 32;
		private const float LoopFadeSeconds = 0.08f;
		private readonly FloppyDriveSoundPack _pack;
		private readonly TransientVoice[] _voices = new TransientVoice[MaxTransientVoices];
		private readonly SampleDriveAudioState[] _driveStates = new SampleDriveAudioState[DriveCount];
		private readonly int _sampleRate;
		private int _stepSampleCursor;
		private int _seekSampleCursor;

		public SampleFloppyDriveSoundSource(FloppyDriveSoundPack pack, int sampleRate)
		{
			_pack = pack;
			_sampleRate = Math.Max(1, sampleRate);
		}

		public bool Enabled => _pack.HasAnySamples;

		public void StartMotor(int driveIndex)
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

		public void StopMotor(int driveIndex)
		{
			StartVoice(_pack.MotorStop, driveIndex, delayFrames: 0);
			var state = _driveStates[driveIndex];
			state.LoopTargetGain = 0f;
			_driveStates[driveIndex] = state;
		}

		public void PlayDiskInsert(int driveIndex)
			=> StartVoice(_pack.DiskInsert, driveIndex, delayFrames: 0);

		public void PlayDiskEject(int driveIndex)
			=> StartVoice(_pack.DiskEject, driveIndex, delayFrames: 0);

		public void QueueSeekOrStep(int driveIndex, int cylinderDelta, int frames)
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

		public void MixFrame(ref float left, ref float right)
		{
			MixMotorLoops(ref left, ref right);
			MixTransientVoices(ref left, ref right);
		}

		public void Dispose()
		{
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
	}

	private sealed class SyntheticFloppyDriveSoundSource : IFloppyDriveSoundSource
	{
		private const int MaxSyntheticVoices = 48;
		private const float TwoPi = 6.283185307179586f;
		private readonly SyntheticVoice[] _voices = new SyntheticVoice[MaxSyntheticVoices];
		private readonly int _sampleRate;
		private uint _noiseState = 0xC0FFEEu;
		private int _voiceSequence;

		public SyntheticFloppyDriveSoundSource(int sampleRate)
			=> _sampleRate = Math.Max(1, sampleRate);

		public bool Enabled => true;

		public void StartMotor(int driveIndex)
		{
			_ = driveIndex;
		}

		public void StopMotor(int driveIndex)
		{
			_ = driveIndex;
		}

		public void PlayDiskInsert(int driveIndex)
			=> StartVoice(SyntheticVoiceKind.DiskInsert, driveIndex, delayFrames: 0);

		public void PlayDiskEject(int driveIndex)
			=> StartVoice(SyntheticVoiceKind.DiskEject, driveIndex, delayFrames: 0);

		public void QueueSeekOrStep(int driveIndex, int cylinderDelta, int frames)
		{
			var eventCount = Math.Min(MaxStepEventsPerDrivePerFrame, Math.Max(1, cylinderDelta));
			var kind = cylinderDelta > 1 ? SyntheticVoiceKind.Seek : SyntheticVoiceKind.Step;
			for (var i = 0; i < eventCount; i++)
			{
				StartVoice(kind, driveIndex, frames <= 1 ? 0 : (i * frames) / eventCount);
			}
		}

		public void MixFrame(ref float left, ref float right)
		{
			MixVoices(ref left, ref right);
		}

		public void Dispose()
		{
		}

		private void StartVoice(SyntheticVoiceKind kind, int driveIndex, int delayFrames)
		{
			for (var i = 0; i < _voices.Length; i++)
			{
				if (_voices[i].Active)
				{
					continue;
				}

				var profile = GetProfile(driveIndex);
				var gain = GetVoiceGain(profile, kind);
				var duration = GetVoiceDurationFrames(kind);
				var frequency = GetVoiceFrequency(profile, kind);
				var detune = ((_voiceSequence++ & 7) - 3) * 11f;
				if (kind is SyntheticVoiceKind.Step or SyntheticVoiceKind.Seek)
				{
					frequency += detune;
				}

				var pan = GetDriveGain(driveIndex);
				_voices[i] = new SyntheticVoice
				{
					Kind = kind,
					DriveIndex = driveIndex,
					DelayFrames = Math.Max(0, delayFrames),
					DurationFrames = duration,
					Frequency = Math.Max(20f, frequency),
					Gain = gain,
					Distortion = profile.Distortion,
					LeftGain = pan.Left,
					RightGain = pan.Right,
					Phase = (_voiceSequence & 15) * 0.07f,
					Active = true
				};
				return;
			}
		}

		private void MixVoices(ref float left, ref float right)
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

				var progress = voice.Frame / (float)Math.Max(1, voice.DurationFrames);
				var envelope = GetEnvelope(voice.Kind, progress);
				var tone = AdvanceSine(ref voice.Phase, voice.Frequency, _sampleRate);
				var noise = NextNoise(ref _noiseState);
				var body = voice.Kind switch
				{
					SyntheticVoiceKind.Step => (tone * 0.52f) + (noise * 0.48f),
					SyntheticVoiceKind.Seek => (tone * 0.43f) + (noise * 0.57f),
					SyntheticVoiceKind.DiskInsert => (tone * 0.25f) + (noise * 0.75f),
					SyntheticVoiceKind.DiskEject => (tone * 0.22f) + (noise * 0.78f),
					_ => (tone * 0.45f) + (noise * 0.55f)
				};
				var sample = ApplySubtleDistortion(body * envelope * voice.Gain, voice.Distortion);
				left += sample * voice.LeftGain;
				right += sample * voice.RightGain;
				voice.Frame++;
				if (voice.Frame >= voice.DurationFrames)
				{
					voice.Active = false;
				}

				_voices[i] = voice;
			}
		}

		private int GetVoiceDurationFrames(SyntheticVoiceKind kind)
		{
			var seconds = kind switch
			{
				SyntheticVoiceKind.Step => 0.026f,
				SyntheticVoiceKind.Seek => 0.021f,
				SyntheticVoiceKind.DiskInsert => 0.12f,
				SyntheticVoiceKind.DiskEject => 0.09f,
				_ => 0.13f
			};
			return Math.Max(1, (int)(_sampleRate * seconds));
		}

		private static float GetVoiceGain(SyntheticDriveProfile profile, SyntheticVoiceKind kind)
		{
			return kind switch
			{
				SyntheticVoiceKind.Step => profile.StepGain,
				SyntheticVoiceKind.Seek => profile.SeekGain,
				SyntheticVoiceKind.DiskInsert => profile.InsertGain,
				_ => profile.EjectGain
			};
		}

		private static float GetVoiceFrequency(SyntheticDriveProfile profile, SyntheticVoiceKind kind)
		{
			return kind switch
			{
				SyntheticVoiceKind.Step => profile.StepHz,
				SyntheticVoiceKind.Seek => profile.SeekHz,
				SyntheticVoiceKind.DiskInsert => 78f,
				_ => 58f
			};
		}

		private static float GetEnvelope(SyntheticVoiceKind kind, float progress)
		{
			progress = Math.Clamp(progress, 0f, 1f);
			if (kind is SyntheticVoiceKind.Step or SyntheticVoiceKind.Seek)
			{
				if (progress < 0.045f)
				{
					return progress / 0.045f;
				}

				var decay = 1f - progress;
				return decay * decay;
			}

			var thump = 1f - progress;
			return thump * thump * thump;
		}

		private static float ApplySubtleDistortion(float sample, float amount)
		{
			var bias = amount * 0.035f;
			var driven = (sample + bias) * (1f + (amount * 1.6f));
			driven = Math.Clamp(driven, -1.2f, 1.2f);
			var saturated = driven - (driven * driven * driven * 0.18f);
			return (sample * (1f - amount)) + ((saturated - bias) * amount);
		}

		private static float AdvanceSine(ref float phase, float frequency, int sampleRate)
		{
			var value = MathF.Sin(phase);
			phase += TwoPi * frequency / sampleRate;
			if (phase >= TwoPi)
			{
				phase -= TwoPi;
			}

			return value;
		}

		private static float NextNoise(ref uint state)
		{
			state ^= state << 13;
			state ^= state >> 17;
			state ^= state << 5;
			return ((state & 0xFFFF) / 32768f) - 1f;
		}

		private static SyntheticDriveProfile GetProfile(int driveIndex)
		{
			return driveIndex == 0
				? new SyntheticDriveProfile(
					StepHz: 390f,
					SeekHz: 620f,
					StepGain: 0.62f,
					SeekGain: 0.68f,
					InsertGain: 0.18f,
					EjectGain: 0.13f,
					Distortion: 0.14f)
				: new SyntheticDriveProfile(
					StepHz: 340f,
					SeekHz: 540f,
					StepGain: 0.09f,
					SeekGain: 0.11f,
					InsertGain: 0.10f,
					EjectGain: 0.07f,
					Distortion: 0.08f);
		}
	}

	private struct SampleDriveAudioState
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

	private struct SyntheticVoice
	{
		public SyntheticVoiceKind Kind;
		public int DriveIndex;
		public int Frame;
		public int DelayFrames;
		public int DurationFrames;
		public float Frequency;
		public float Gain;
		public float Distortion;
		public float LeftGain;
		public float RightGain;
		public float Phase;
		public bool Active;
	}

	private enum SyntheticVoiceKind
	{
		Step,
		Seek,
		DiskInsert,
		DiskEject
	}

	private readonly record struct SyntheticDriveProfile(
		float StepHz,
		float SeekHz,
		float StepGain,
		float SeekGain,
		float InsertGain,
		float EjectGain,
		float Distortion);

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
