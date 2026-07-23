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
		Assert.False(options.DeferredCpuBusBatchConfigured);
		Assert.True(options.DeferredCpuChipReadSegments);
		Assert.Null(options.Error);
	}

	[Fact]
	public void DeferredCustomPointerWritesRemainAnIndependentKillSwitch()
	{
		var options = CopperScreenStartupOptions.Parse(
			["--cpu-deferred-custom-pointer-writes"],
			AppContext.BaseDirectory);

		Assert.False(options.DeferredCpuBusBatch);
		Assert.False(options.DeferredCpuBusBatchConfigured);
		Assert.True(options.DeferredCpuCustomPointerWrites);
		Assert.True(options.DeferredCpuCustomPointerWritesConfigured);
		Assert.Null(options.Error);
	}

	[Fact]
	public void DeferredCustomCompositionWritesRemainAnIndependentKillSwitch()
	{
		var options = CopperScreenStartupOptions.Parse(
			["--cpu-deferred-custom-composition-writes"],
			AppContext.BaseDirectory);

		Assert.False(options.DeferredCpuBusBatch);
		Assert.False(options.DeferredCpuBusBatchConfigured);
		Assert.True(options.DeferredCpuCustomCompositionWrites);
		Assert.True(options.DeferredCpuCustomCompositionWritesConfigured);
		Assert.Null(options.Error);
	}

	[Theory]
	[InlineData("--no-cpu-deferred-custom-pointer-writes", true)]
	[InlineData("--no-cpu-deferred-custom-composition-writes", false)]
	public void DeferredCustomWriteStagesHaveExplicitRollback(string argument, bool pointer)
	{
		var options = CopperScreenStartupOptions.Parse([argument], AppContext.BaseDirectory);

		Assert.False(pointer
			? options.DeferredCpuCustomPointerWrites
			: options.DeferredCpuCustomCompositionWrites);
		Assert.True(pointer
			? options.DeferredCpuCustomPointerWritesConfigured
			: options.DeferredCpuCustomCompositionWritesConfigured);
		Assert.Null(options.Error);
	}

	[Theory]
	[InlineData("--cpu-deferred-bus-batch", true)]
	[InlineData("--no-cpu-deferred-bus-batch", false)]
	public void DeferredCpuBusBatchHasExplicitEnableAndRollbackOverrides(string argument, bool enabled)
	{
		var options = CopperScreenStartupOptions.Parse([argument], AppContext.BaseDirectory);

		Assert.True(options.DeferredCpuBusBatchConfigured);
		Assert.Equal(enabled, options.DeferredCpuBusBatch);
		Assert.False(options.DeferredCpuBusBatchVerify);
		Assert.Null(options.Error);
	}

	[Fact]
	public void DeferredCpuChipWriteJournalRemainsAnIndependentStageSwitch()
	{
		var options = CopperScreenStartupOptions.Parse(
			["--cpu-deferred-chip-write-journal"],
			AppContext.BaseDirectory);

		Assert.True(options.DeferredCpuChipWriteJournal);
		Assert.True(options.DeferredCpuChipWriteJournalConfigured);
		Assert.False(options.DeferredCpuBusBatchConfigured);
		Assert.Null(options.Error);
	}

	[Fact]
	public void DeferredCpuChipWriteJournalRollbackIsExplicit()
	{
		var options = CopperScreenStartupOptions.Parse(
			["--no-cpu-deferred-chip-write-journal"],
			AppContext.BaseDirectory);

		Assert.False(options.DeferredCpuChipWriteJournal);
		Assert.True(options.DeferredCpuChipWriteJournalConfigured);
		Assert.Null(options.Error);
	}

	[Fact]
	public void DeferredCpuChipReadSegmentsSupportsEnableAndRollback()
	{
		var enabled = CopperScreenStartupOptions.Parse(
			["--cpu-deferred-chip-read-segments"], AppContext.BaseDirectory);
		var disabled = CopperScreenStartupOptions.Parse(
			["--no-cpu-deferred-chip-read-segments"], AppContext.BaseDirectory);

		Assert.True(enabled.DeferredCpuChipReadSegments);
		Assert.True(enabled.DeferredCpuChipReadSegmentsConfigured);
		Assert.False(disabled.DeferredCpuChipReadSegments);
		Assert.True(disabled.DeferredCpuChipReadSegmentsConfigured);
		Assert.Null(enabled.Error);
		Assert.Null(disabled.Error);
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
