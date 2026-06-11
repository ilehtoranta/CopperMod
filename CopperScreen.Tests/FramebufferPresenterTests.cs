using Avalonia;
using CopperScreen;

namespace CopperScreen.Tests;

public sealed class FramebufferPresenterTests
{
	[Fact]
	public void UniformDestinationUsesRenderedLetterboxRect()
	{
		var bounds = new Size(1200, 960);
		var source = new Size(352, 288);
		var scale = bounds.Height / source.Height;
		var offsetX = (bounds.Width - (source.Width * scale)) / 2.0;

		Assert.True(FramebufferPresenter.TryCalculateUniformDestination(bounds, source, out var destination));
		Assert.Equal(offsetX, destination.X, precision: 6);
		Assert.Equal(0, destination.Y, precision: 6);
		Assert.Equal(source.Width * scale, destination.Width, precision: 6);
		Assert.Equal(bounds.Height, destination.Height, precision: 6);
	}

	[Fact]
	public void UniformStretchMouseMappingIgnoresHorizontalLetterbox()
	{
		var bounds = new Size(1200, 960);
		var source = new Size(352, 288);
		var scale = bounds.Height / source.Height;
		var offsetX = (bounds.Width - (source.Width * scale)) / 2.0;

		Assert.False(FramebufferPresenter.TryMapUniformStretchPoint(
			bounds,
			source,
			new Point(offsetX - 1, bounds.Height / 2.0),
			out _));

		Assert.True(FramebufferPresenter.TryMapUniformStretchPoint(
			bounds,
			source,
			new Point(offsetX, 0),
			out var topLeft));
		Assert.Equal(0, topLeft.X, precision: 6);
		Assert.Equal(0, topLeft.Y, precision: 6);
	}

	[Fact]
	public void UniformStretchMouseMappingMapsCenterToFramebufferCenter()
	{
		Assert.True(FramebufferPresenter.TryMapUniformStretchPoint(
			new Size(1200, 960),
			new Size(352, 288),
			new Point(600, 480),
			out var center));

		Assert.Equal(176, center.X, precision: 6);
		Assert.Equal(144, center.Y, precision: 6);
	}

	[Fact]
	public void UniformStretchMouseMappingAddsCroppedViewportOrigin()
	{
		Assert.True(FramebufferPresenter.TryMapUniformStretchPoint(
			new Size(960, 768),
			new PixelRect(16, 16, 320, 256),
			new Point(480, 384),
			out var center));

		Assert.Equal(176, center.X, precision: 6);
		Assert.Equal(144, center.Y, precision: 6);
	}

	[Fact]
	public void UnclampedUniformStretchMouseMappingKeepsRelativeDeltasOutsideImage()
	{
		var bounds = new Size(1200, 960);
		var source = new Size(352, 288);
		var scale = bounds.Height / source.Height;
		var offsetX = (bounds.Width - (source.Width * scale)) / 2.0;

		Assert.False(FramebufferPresenter.TryMapUniformStretchPoint(
			bounds,
			source,
			new Point(offsetX - scale, bounds.Height / 2.0),
			out _));

		Assert.True(FramebufferPresenter.TryMapUniformStretchPointUnclamped(
			bounds,
			source,
			new Point(offsetX - scale, bounds.Height / 2.0),
			out var point));
		Assert.Equal(-1, point.X, precision: 6);
		Assert.Equal(144, point.Y, precision: 6);
	}
}
