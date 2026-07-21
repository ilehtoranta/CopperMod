namespace CopperScreen;

internal enum CopperScreenLacedPresentationMode
{
	StableWeave,
	CrtPhosphor
}

internal enum CopperScreenPixelAspectMode
{
	Lcd,
	CrtCorrect
}

internal readonly record struct CopperScreenPresentationOptions(
	CopperScreenLacedPresentationMode LacedMode,
	CopperScreenPixelAspectMode PixelAspectMode = CopperScreenPixelAspectMode.Lcd)
{
	public static CopperScreenPresentationOptions Default { get; } =
		new(CopperScreenLacedPresentationMode.CrtPhosphor, CopperScreenPixelAspectMode.Lcd);

	public static CopperScreenLacedPresentationMode ParseLacedMode(string? value, string optionName = "presentation.lacedMode")
	{
		if (TryParseLacedMode(value, out var mode))
		{
			return mode;
		}

		throw new InvalidOperationException($"{optionName} must be StableWeave or CrtPhosphor.");
	}

	public static bool TryParseLacedMode(string? value, out CopperScreenLacedPresentationMode mode)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			mode = CopperScreenLacedPresentationMode.CrtPhosphor;
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
			case "crtphosphor":
			case "phosphor":
				mode = CopperScreenLacedPresentationMode.CrtPhosphor;
				return true;
			default:
				mode = CopperScreenLacedPresentationMode.StableWeave;
				return false;
		}
	}

	public static CopperScreenPixelAspectMode ParsePixelAspectMode(string? value, string optionName = "presentation.pixelAspectMode")
	{
		if (TryParsePixelAspectMode(value, out var mode))
		{
			return mode;
		}

		throw new InvalidOperationException($"{optionName} must be Lcd or CrtCorrect.");
	}

	public static bool TryParsePixelAspectMode(string? value, out CopperScreenPixelAspectMode mode)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			mode = CopperScreenPixelAspectMode.Lcd;
			return true;
		}

		var normalized = value.Trim().ToLowerInvariant().Replace("-", string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty);
		switch (normalized)
		{
			case "lcd":
			case "lcdcrisp":
				mode = CopperScreenPixelAspectMode.Lcd;
				return true;
			case "crtcorrect":
			case "crt":
				mode = CopperScreenPixelAspectMode.CrtCorrect;
				return true;
			default:
				mode = CopperScreenPixelAspectMode.Lcd;
				return false;
		}
	}
}
