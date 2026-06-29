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
	public void HighResolutionPresentationCoordinatesMapToOriginalAmigaMouseDeltas()
	{
		var previous = MainWindow.MapPresentationPointToAmigaMousePoint(new Point(352, 288));
		var current = MainWindow.MapPresentationPointToAmigaMousePoint(new Point(354, 286));
		var remainderX = 0.0;
		var remainderY = 0.0;

		var deltaX = MainWindow.ConsumeWholeMouseDelta(ref remainderX, current.X - previous.X);
		var deltaY = MainWindow.ConsumeWholeMouseDelta(ref remainderY, current.Y - previous.Y);

		Assert.Equal(1, deltaX);
		Assert.Equal(-1, deltaY);
		Assert.Equal(0, remainderX, precision: 6);
		Assert.Equal(0, remainderY, precision: 6);
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
	public void MouseGrabRecenterEchoMatchesOnlyNearCenteredWarpEvents()
	{
		var center = new Point(176, 144);
		var centerScreen = new PixelPoint(500, 400);

		Assert.True(MainWindow.IsMouseGrabRecenterEcho(
			new Point(176.5, 143.5),
			center,
			centerScreen,
			centerScreen));
		Assert.False(MainWindow.IsMouseGrabRecenterEcho(
			new Point(178, 144),
			center,
			centerScreen,
			centerScreen));
		Assert.False(MainWindow.IsMouseGrabRecenterEcho(
			center,
			center,
			new PixelPoint(501, 400),
			centerScreen));
	}

	[Fact]
	public void MousePortUsesOneBasedAmigaLabels()
	{
		var defaultInput = CopperScreenInputOptions.Default;
		var swappedInput = CopperScreenInputOptions.Create(2);

		Assert.Equal("mouse", defaultInput.Port1ProfileId);
		Assert.Equal("numpad-joystick", defaultInput.Port2ProfileId);
		Assert.Equal(1, defaultInput.MousePort);
		Assert.Equal(0, defaultInput.MousePortIndex);
		Assert.Equal(1, defaultInput.JoystickPortIndex);
		Assert.Equal(2, swappedInput.MousePort);
		Assert.Equal(1, swappedInput.MousePortIndex);
		Assert.Equal(0, swappedInput.JoystickPortIndex);
	}

	[Fact]
	public void InputPortsCanBeExplicitlyLeftEmpty()
	{
		var input = CopperScreenInputOptions.Create(
			CopperScreenControllerProfile.None.Id,
			CopperScreenControllerProfile.None.Id,
			CopperScreenInputOptions.DefaultControllerProfiles);

		Assert.Equal("none", input.Port1ProfileId);
		Assert.Equal("none", input.Port2ProfileId);
		Assert.Equal(CopperScreenControllerKind.None, input.GetProfileForPort(1).Kind);
		Assert.Equal(CopperScreenControllerKind.None, input.GetProfileForPort(2).Kind);
		Assert.False(input.IsMousePort(0));
		Assert.False(input.IsMousePort(1));
		Assert.False(input.TryGetKeyboardJoystickMap(0, out _));
		Assert.False(input.TryGetKeyboardJoystickMap(1, out _));
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
