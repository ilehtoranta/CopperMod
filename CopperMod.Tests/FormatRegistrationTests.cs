using AmigaTracker.ProTracker;

namespace CopperMod.Tests;

public sealed class FormatRegistrationTests
{
	[Fact]
	public void CopperModRegistersProTrackerFormat()
	{
		Assert.Contains(ModuleAudioPlayer.SupportedFormats, format => format is ProTrackerFormat);
	}
}
