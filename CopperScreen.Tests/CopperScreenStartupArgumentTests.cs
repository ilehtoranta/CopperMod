using CopperMod.Amiga;

namespace CopperScreen.Tests;

public sealed class CopperScreenStartupArgumentTests
{
	[Fact]
	public void DeferredChipReadSegmentsRemainAnIndependentKillSwitch()
	{
		var options = CopperScreenStartupOptions.Parse(
			["--cpu-deferred-chip-read-segments"],
			AppContext.BaseDirectory);

		Assert.False(options.DeferredCpuBusBatch);
		Assert.True(options.DeferredCpuChipReadSegments);
		Assert.Null(options.Error);
	}

	[Fact]
	public void PositionalProfileThenDiskStartsWithTheSelectedProfileAndDisk()
	{
		var diskPath = Path.GetTempFileName();
		try
		{
			var options = CopperScreenStartupOptions.Parse(
				["Profiles\\expanded-kickstart13 - singledrive.json", diskPath],
				AppContext.BaseDirectory);

			Assert.True(options.HasExplicitProfile);
			Assert.Equal("expanded-kickstart13", options.Profile.Id);
			Assert.Equal(Path.GetFullPath(diskPath), options.DiskPath);
			Assert.Null(options.Error);
		}
		finally
		{
			File.Delete(diskPath);
		}
	}
}
