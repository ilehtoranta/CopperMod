using CopperMod.Cust;
using CopperMod.ProTracker;
using CopperMod.Sid;

namespace CopperMod.Tests;

public sealed class FormatRegistrationTests
{
	[Fact]
	public void CopperModRegistersProTrackerFormat()
	{
		Assert.Contains(ModuleAudioPlayer.SupportedFormats, format => format is ProTrackerFormat);
	}

	[Fact]
	public void CopperModRegistersSidFormat()
	{
		Assert.Contains(ModuleAudioPlayer.SupportedFormats, format => format is SidFormat);
	}

	[Fact]
	public void CopperModRegistersCustFormat()
	{
		Assert.Contains(ModuleAudioPlayer.SupportedFormats, format => format is CustFormat);
	}
}
