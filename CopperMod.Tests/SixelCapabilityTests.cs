namespace CopperMod.Tests;

public sealed class SixelCapabilityTests
{
	[Fact]
	public void SupportedOnlyWhenWindowsBuildAndTerminalBothSupportSixels()
	{
		Assert.True(SixelCapability.IsSupported(new Version(10, 0, 22000), isWindows: true, terminalSupportsSixel: true));
		Assert.True(SixelCapability.IsSupported(new Version(10, 0, 26100), isWindows: true, terminalSupportsSixel: true));
		Assert.False(SixelCapability.IsSupported(new Version(10, 0, 19045), isWindows: true, terminalSupportsSixel: true));
		Assert.False(SixelCapability.IsSupported(new Version(10, 0, 22000), isWindows: true, terminalSupportsSixel: false));
		Assert.False(SixelCapability.IsSupported(new Version(10, 0, 22000), isWindows: false, terminalSupportsSixel: true));
	}
}
