using Avalonia;
using Avalonia.Input;
using CopperScreen;

namespace CopperScreen.Tests;

public sealed class MainWindowMouseInputTests
{
	[Fact]
	public void FractionalFramebufferMouseDeltasAccumulateIntoWholeCounts()
	{
		var remainder = 0.0;

		Assert.Equal(0, MainWindow.ConsumeWholeMouseDelta(ref remainder, 0.375));
		Assert.Equal(0.375, remainder, precision: 6);
		Assert.Equal(0, MainWindow.ConsumeWholeMouseDelta(ref remainder, 0.375));
		Assert.Equal(0.75, remainder, precision: 6);
		Assert.Equal(1, MainWindow.ConsumeWholeMouseDelta(ref remainder, 0.375));
		Assert.Equal(0.125, remainder, precision: 6);
	}

	[Fact]
	public void NegativeFractionalFramebufferMouseDeltasAccumulateIntoWholeCounts()
	{
		var remainder = 0.0;

		Assert.Equal(0, MainWindow.ConsumeWholeMouseDelta(ref remainder, -0.375));
		Assert.Equal(-0.375, remainder, precision: 6);
		Assert.Equal(0, MainWindow.ConsumeWholeMouseDelta(ref remainder, -0.375));
		Assert.Equal(-0.75, remainder, precision: 6);
		Assert.Equal(-1, MainWindow.ConsumeWholeMouseDelta(ref remainder, -0.375));
		Assert.Equal(-0.125, remainder, precision: 6);
	}

	[Fact]
	public void MouseGrabCenterHandshakeAcceptsOnlyCenteredPointerEvents()
	{
		var center = new Point(176, 144);
		var initial = new Point(20, 80);

		Assert.False(MainWindow.IsMouseGrabCenterPoint(initial, center));
		Assert.False(MainWindow.IsMouseGrabCenterPoint(new Point(174, 144), center));
		Assert.True(MainWindow.IsMouseGrabCenterPoint(center, center));
		Assert.True(MainWindow.IsMouseGrabCenterPoint(new Point(176.5, 143.5), center));
	}

	[Fact]
	public void MouseGrabScreenCenterHandshakeAllowsRoundedCursorWarpPosition()
	{
		var center = new PixelPoint(500, 400);

		Assert.True(MainWindow.IsNearScreenPoint(center, center));
		Assert.True(MainWindow.IsNearScreenPoint(new PixelPoint(501, 399), center));
		Assert.False(MainWindow.IsNearScreenPoint(new PixelPoint(503, 400), center));
	}

	[Fact]
	public void MousePortUsesOneBasedAmigaLabels()
	{
		var defaultInput = CopperScreenInputOptions.Default;
		var swappedInput = CopperScreenInputOptions.Create(2);

		Assert.Equal(1, defaultInput.MousePort);
		Assert.Equal(0, defaultInput.MousePortIndex);
		Assert.Equal(1, defaultInput.JoystickPortIndex);
		Assert.Equal(2, swappedInput.MousePort);
		Assert.Equal(1, swappedInput.MousePortIndex);
		Assert.Equal(0, swappedInput.JoystickPortIndex);
	}

	[Fact]
	public void DefaultJoystickMappingMatchesCurrentNumpadKeys()
	{
		var mapping = CopperScreenJoystickKeyMap.Default;

		Assert.Equal(CopperScreenJoystickActions.Down | CopperScreenJoystickActions.Left, mapping.GetActions(Key.NumPad1, PhysicalKey.NumPad1));
		Assert.Equal(CopperScreenJoystickActions.Fire, mapping.GetActions(Key.NumPad5, PhysicalKey.NumPad5));
		Assert.Equal(CopperScreenJoystickActions.SecondFire, mapping.GetActions(Key.Delete, PhysicalKey.None));
	}

	[Fact]
	public void CustomJoystickMappingIgnoresReservedHostShortcuts()
	{
		var mapping = CopperScreenJoystickKeyMap.Create(
			["W", "F12"],
			["S"],
			["A"],
			["D"],
			["Space"],
			["LeftCtrl"]);

		Assert.Equal(CopperScreenJoystickActions.Up, mapping.GetActions(Key.W, PhysicalKey.W));
		Assert.Equal(CopperScreenJoystickActions.None, mapping.GetActions(Key.F12, PhysicalKey.F12));
		Assert.Equal(CopperScreenJoystickActions.Fire, mapping.GetActions(Key.Space, PhysicalKey.Space));
	}
}
