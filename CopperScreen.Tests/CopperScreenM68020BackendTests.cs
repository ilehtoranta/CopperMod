using CopperMod.Amiga;
using CopperScreen;

namespace CopperScreen.Tests;

public sealed class CopperScreenM68020BackendTests
{
	[Theory]
	[InlineData("AccurateM68020")]
	[InlineData("m68020")]
	[InlineData("68020")]
	[InlineData("020")]
	[InlineData("Ocs68020_14MHz")]
	public void CpuBackendParserAcceptsM68020Aliases(string backend)
	{
		Assert.Equal(M68kBackendKind.AccurateM68020, CopperScreenProfile.ParseCpuBackend(backend));
	}

	[Fact]
	public void StartupArgumentParserCanSelectM68020Backend()
	{
		var options = CopperScreenStartupOptions.Parse(
			new[] { "--profile", "expanded-copperstart", "--m68020" },
			AppContext.BaseDirectory);

		Assert.Null(options.Error);
		Assert.Equal(M68kBackendKind.AccurateM68020, options.CpuBackendOverride);
	}

	[Fact]
	public void BundledM68020ProfileCreatesM68020MachineOptions()
	{
		Assert.True(
			CopperScreenProfile.TryLoad(
				"expanded-m68020-copperstart",
				AppContext.BaseDirectory,
				out var profile,
				out var error),
			error);

		Assert.Equal(M68kBackendKind.AccurateM68020, profile.CpuBackend);
		Assert.Equal(M68kBackendKind.AccurateM68020, profile.CreateMachineOptions().CpuBackend);
	}
}
