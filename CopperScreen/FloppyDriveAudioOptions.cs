namespace CopperScreen;

internal enum FloppyDriveAudioMode
{
	Synthetic,
	Samples
}

internal readonly record struct FloppyDriveAudioOptions(
	bool Enabled,
	FloppyDriveAudioMode Mode,
	string SoundPack,
	float Volume)
{
	public const string DefaultSoundPack = "default";
	public const float DefaultVolume = 0.25f;
	public const FloppyDriveAudioMode DefaultMode = FloppyDriveAudioMode.Synthetic;

	public FloppyDriveAudioOptions(bool enabled, string soundPack, float volume)
		: this(enabled, DefaultMode, soundPack, volume)
	{
	}

	public static FloppyDriveAudioOptions Default { get; } = new(false, DefaultMode, DefaultSoundPack, DefaultVolume);

	public FloppyDriveAudioOptions WithOverrides(bool? enabled, FloppyDriveAudioMode? mode, string? soundPack, float? volume)
	{
		return new FloppyDriveAudioOptions(
			enabled ?? Enabled,
			mode ?? Mode,
			string.IsNullOrWhiteSpace(soundPack) ? SoundPack : soundPack.Trim(),
			ClampVolume(volume ?? Volume));
	}

	public static bool TryParseMode(string? value, out FloppyDriveAudioMode mode)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			mode = DefaultMode;
			return true;
		}

		switch (value.Trim().ToLowerInvariant())
		{
			case "synthetic":
			case "synth":
			case "generated":
				mode = FloppyDriveAudioMode.Synthetic;
				return true;
			case "samples":
			case "sample":
			case "pack":
			case "soundpack":
				mode = FloppyDriveAudioMode.Samples;
				return true;
			default:
				mode = DefaultMode;
				return false;
		}
	}

	public static float ClampVolume(float volume)
	{
		return float.IsNaN(volume) ? DefaultVolume : Math.Clamp(volume, 0f, 1f);
	}
}
