using CopperMod.Amiga;
using CopperScreen;

namespace CopperScreen.Tests;

public sealed class CopperScreenM68020BackendTests
{
	[Theory]
	[InlineData("AccurateM68EC020")]
	[InlineData("m68ec020")]
	[InlineData("68ec020")]
	[InlineData("ec020")]
	[InlineData("020ec")]
	public void CpuBackendParserAcceptsM68EC020Aliases(string backend)
	{
		Assert.Equal(M68kBackendKind.AccurateM68EC020, CopperScreenProfile.ParseCpuBackend(backend));
	}

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

	[Theory]
	[InlineData("AccurateM68030")]
	[InlineData("m68030")]
	[InlineData("68030")]
	[InlineData("030")]
	[InlineData("Ocs68030_14MHz")]
	public void CpuBackendParserAcceptsM68030Aliases(string backend)
	{
		Assert.Equal(M68kBackendKind.AccurateM68030, CopperScreenProfile.ParseCpuBackend(backend));
	}

	[Theory]
	[InlineData("AccurateM68040")]
	[InlineData("m68040")]
	[InlineData("68040")]
	[InlineData("040")]
	[InlineData("Ocs68040_25MHz")]
	public void CpuBackendParserAcceptsM68040Aliases(string backend)
	{
		Assert.Equal(M68kBackendKind.AccurateM68040, CopperScreenProfile.ParseCpuBackend(backend));
	}

	[Theory]
	[InlineData("JitM68040")]
	[InlineData("jit68040")]
	[InlineData("m68040jit")]
	[InlineData("040jit")]
	public void CpuBackendParserAcceptsM68040JitAliases(string backend)
	{
		Assert.Equal(M68kBackendKind.JitM68040, CopperScreenProfile.ParseCpuBackend(backend));
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

	[Theory]
	[InlineData("--m68ec020")]
	[InlineData("--68ec020")]
	public void StartupArgumentParserCanSelectM68EC020Backend(string alias)
	{
		var options = CopperScreenStartupOptions.Parse(
			new[] { "--profile", "expanded-copperstart", alias },
			AppContext.BaseDirectory);

		Assert.Null(options.Error);
		Assert.Equal(M68kBackendKind.AccurateM68EC020, options.CpuBackendOverride);
	}

	[Fact]
	public void StartupArgumentParserCanSelectM68030Backend()
	{
		var options = CopperScreenStartupOptions.Parse(
			new[] { "--profile", "expanded-copperstart", "--m68030" },
			AppContext.BaseDirectory);

		Assert.Null(options.Error);
		Assert.Equal(M68kBackendKind.AccurateM68030, options.CpuBackendOverride);
	}

	[Theory]
	[InlineData("--m68040")]
	[InlineData("--68040")]
	public void StartupArgumentParserCanSelectM68040Backend(string alias)
	{
		var options = CopperScreenStartupOptions.Parse(
			new[] { "--profile", "expanded-copperstart", alias },
			AppContext.BaseDirectory);

		Assert.Null(options.Error);
		Assert.Equal(M68kBackendKind.AccurateM68040, options.CpuBackendOverride);
	}

	[Theory]
	[InlineData("--jit-m68040")]
	[InlineData("--jit-68040")]
	[InlineData("--m68040-jit")]
	public void StartupArgumentParserCanSelectM68040JitBackend(string alias)
	{
		var options = CopperScreenStartupOptions.Parse(
			new[] { "--profile", "expanded-copperstart", alias },
			AppContext.BaseDirectory);

		Assert.Null(options.Error);
		Assert.Equal(M68kBackendKind.JitM68040, options.CpuBackendOverride);
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

	[Fact]
	public void BundledM68030ProfileCreatesM68030MachineOptions()
	{
		Assert.True(
			CopperScreenProfile.TryLoad(
				"expanded-m68030-copperstart",
				AppContext.BaseDirectory,
				out var profile,
				out var error),
			error);

		Assert.Equal(M68kBackendKind.AccurateM68030, profile.CpuBackend);
		Assert.Equal(M68kBackendKind.AccurateM68030, profile.CreateMachineOptions().CpuBackend);
	}

	[Fact]
	public void BundledM68040KickstartProfileCreatesM68040MachineOptions()
	{
		Assert.True(
			CopperScreenProfile.TryLoad(
				"expanded-m68040-kickstart-rom",
				AppContext.BaseDirectory,
				out var profile,
				out var error),
			error);

		Assert.Equal(CopperScreenKickstartSource.KickstartRom, profile.KickstartSource);
		Assert.Equal(M68kBackendKind.AccurateM68040, profile.CpuBackend);
		Assert.Equal(M68kBackendKind.AccurateM68040, profile.CreateMachineOptions().CpuBackend);
		Assert.Equal(8 * 1024 * 1024, profile.RealFastRamSize);
		Assert.Equal(0x0020_0000u, profile.RealFastRamBase);
	}

	[Fact]
	public void BundledM68040JitKickstartProfileCreatesM68040JitMachineOptions()
	{
		Assert.True(
			CopperScreenProfile.TryLoad(
				"expanded-m68040-jit-kickstart-rom",
				AppContext.BaseDirectory,
				out var profile,
				out var error),
			error);

		Assert.Equal(CopperScreenKickstartSource.KickstartRom, profile.KickstartSource);
		Assert.Equal(M68kBackendKind.JitM68040, profile.CpuBackend);
		Assert.Equal(M68kBackendKind.JitM68040, profile.CreateMachineOptions().CpuBackend);
		Assert.Equal(8 * 1024 * 1024, profile.RealFastRamSize);
		Assert.Equal(0x0020_0000u, profile.RealFastRamBase);
	}

	[Fact]
	public void BundledM68040JitRtgProfileCreatesSparseLinearVramMachineOptions()
	{
		Assert.True(
			CopperScreenProfile.TryLoad(
				"expanded-m68040-jit-kickstart31-rtg",
				AppContext.BaseDirectory,
				out var profile,
				out var error),
			error);

		Assert.Equal(CopperScreenKickstartSource.KickstartRom, profile.KickstartSource);
		Assert.Equal(M68kBackendKind.JitM68040, profile.CpuBackend);
		Assert.Equal(256L * 1024 * 1024, profile.RtgVramSize);
		Assert.Equal(profile.RtgVramSize, profile.CreateMachineOptions().RtgVramSize);
		Assert.Equal(256, CopperScreenSettingsDraft.FromProfile(profile).RtgVramMb);
	}
}
