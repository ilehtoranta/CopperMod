namespace CopperScreen;

internal enum CopperScreenLacedPresentationMode
{
	StableWeave,
	CrtFlicker
}

internal readonly record struct CopperScreenPresentationOptions(CopperScreenLacedPresentationMode LacedMode)
{
	public static CopperScreenPresentationOptions Default { get; } =
		new(CopperScreenLacedPresentationMode.CrtFlicker);

	public static CopperScreenLacedPresentationMode ParseLacedMode(string? value, string optionName = "presentation.lacedMode")
	{
		if (TryParseLacedMode(value, out var mode))
		{
			return mode;
		}

		throw new InvalidOperationException($"{optionName} must be StableWeave or CrtFlicker.");
	}

	public static bool TryParseLacedMode(string? value, out CopperScreenLacedPresentationMode mode)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			mode = CopperScreenLacedPresentationMode.CrtFlicker;
			return true;
		}

		var normalized = value.Trim()
			.ToLowerInvariant()
			.Replace("-", string.Empty)
			.Replace("_", string.Empty)
			.Replace(" ", string.Empty);
		switch (normalized)
		{
			case "stableweave":
			case "stable":
			case "weave":
				mode = CopperScreenLacedPresentationMode.StableWeave;
				return true;
			case "crtflicker":
			case "crt":
			case "flicker":
				mode = CopperScreenLacedPresentationMode.CrtFlicker;
				return true;
			default:
				mode = CopperScreenLacedPresentationMode.StableWeave;
				return false;
		}
	}
}
