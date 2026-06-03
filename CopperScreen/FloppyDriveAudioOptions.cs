namespace CopperScreen;

internal readonly record struct FloppyDriveAudioOptions(
	bool Enabled,
	string SoundPack,
	float Volume)
{
	public const string DefaultSoundPack = "default";
	public const float DefaultVolume = 0.25f;

	public static FloppyDriveAudioOptions Default { get; } = new(false, DefaultSoundPack, DefaultVolume);

	public FloppyDriveAudioOptions WithOverrides(bool? enabled, string? soundPack, float? volume)
	{
		return new FloppyDriveAudioOptions(
			enabled ?? Enabled,
			string.IsNullOrWhiteSpace(soundPack) ? SoundPack : soundPack.Trim(),
			ClampVolume(volume ?? Volume));
	}

	public static float ClampVolume(float volume)
	{
		return float.IsNaN(volume) ? DefaultVolume : Math.Clamp(volume, 0f, 1f);
	}
}
