using Avalonia;
using CopperScreen;
using CopperMod.Amiga.Core;
using System.Runtime.InteropServices;

namespace CopperScreen.Tests;

public sealed class FramebufferPresenterTests
{
	[Fact]
	public void CopyBgraRowsCopiesTightlyPackedFrame()
	{
		var source = new[]
		{
			unchecked((int)0xFF010203),
			unchecked((int)0xFF040506),
			unchecked((int)0xFF070809),
			unchecked((int)0xFF0A0B0C),
			unchecked((int)0xFF0D0E0F)
		};
		const int width = 2;
		const int height = 2;
		const int strideBytes = width * sizeof(int);
		var destination = Marshal.AllocHGlobal(strideBytes * height);
		try
		{
			FramebufferPresenter.CopyBgraRows(source, width, height, destination, strideBytes);

			var copied = new int[width * height];
			Marshal.Copy(destination, copied, 0, copied.Length);
			Assert.Equal(source[..copied.Length], copied);
		}
		finally
		{
			Marshal.FreeHGlobal(destination);
		}
	}

	[Fact]
	public void CopyBgraRowsPreservesDestinationStridePadding()
	{
		var source = new[]
		{
			unchecked((int)0xFF010203),
			unchecked((int)0xFF040506),
			unchecked((int)0xFF070809),
			unchecked((int)0xFF0A0B0C)
		};
		const int width = 2;
		const int height = 2;
		const int strideBytes = 12;
		var destination = Marshal.AllocHGlobal(strideBytes * height);
		try
		{
			var bytes = Enumerable.Repeat((byte)0xCC, strideBytes * height).ToArray();
			Marshal.Copy(bytes, 0, destination, bytes.Length);

			FramebufferPresenter.CopyBgraRows(source, width, height, destination, strideBytes);

			var copied = new byte[bytes.Length];
			Marshal.Copy(destination, copied, 0, copied.Length);
			Assert.Equal(0x03, copied[0]);
			Assert.Equal(0x02, copied[1]);
			Assert.Equal(0x01, copied[2]);
			Assert.Equal(0xFF, copied[3]);
			Assert.Equal(0x06, copied[4]);
			Assert.Equal(0x05, copied[5]);
			Assert.Equal(0x04, copied[6]);
			Assert.Equal(0xFF, copied[7]);
			Assert.All(copied[8..strideBytes], value => Assert.Equal(0xCC, value));
			Assert.Equal(0x09, copied[strideBytes]);
			Assert.Equal(0x08, copied[strideBytes + 1]);
			Assert.Equal(0x07, copied[strideBytes + 2]);
			Assert.Equal(0xFF, copied[strideBytes + 3]);
			Assert.Equal(0x0C, copied[strideBytes + 4]);
			Assert.Equal(0x0B, copied[strideBytes + 5]);
			Assert.Equal(0x0A, copied[strideBytes + 6]);
			Assert.Equal(0xFF, copied[strideBytes + 7]);
			Assert.All(copied[(strideBytes + 8)..(strideBytes * height)], value => Assert.Equal(0xCC, value));
		}
		finally
		{
			Marshal.FreeHGlobal(destination);
		}
	}

	[Fact]
	public void UniformDestinationUsesIntegerDevicePixelScale()
	{
		var bounds = new Size(1200, 960);
		var source = new Size(352, 288);

		Assert.True(FramebufferPresenter.TryCalculateUniformDestination(bounds, source, out var destination));
		Assert.Equal(72, destination.X, precision: 6);
		Assert.Equal(48, destination.Y, precision: 6);
		Assert.Equal(1056, destination.Width, precision: 6);
		Assert.Equal(864, destination.Height, precision: 6);
	}

	[Fact]
	public void UniformDestinationSnapsToRenderScalingDevicePixels()
	{
		var bounds = new Size(1200, 960);
		var source = new Size(704, 576);

		Assert.True(FramebufferPresenter.TryCalculateUniformDestination(bounds, source, renderScaling: 1.25, out var destination));

		Assert.Equal(Math.Round(destination.X * 1.25), destination.X * 1.25, precision: 6);
		Assert.Equal(Math.Round(destination.Y * 1.25), destination.Y * 1.25, precision: 6);
		Assert.Equal(Math.Round(destination.Width * 1.25), destination.Width * 1.25, precision: 6);
		Assert.Equal(Math.Round(destination.Height * 1.25), destination.Height * 1.25, precision: 6);
		Assert.Equal(2, (destination.Width * 1.25) / source.Width, precision: 6);
		Assert.Equal(2, (destination.Height * 1.25) / source.Height, precision: 6);
		Assert.True(destination.Right <= bounds.Width);
		Assert.True(destination.Bottom <= bounds.Height);
	}

	[Fact]
	public void DevicePixelExactLogicalSizeKeepsSourcePixelDimensionsOnScaledDisplays()
	{
		var source = new Size(704, 576);

		var size = FramebufferPresenter.CalculateDevicePixelExactLogicalSize(source, renderScaling: 1.25);

		Assert.Equal(704, size.Width * 1.25, precision: 6);
		Assert.Equal(576, size.Height * 1.25, precision: 6);
	}

	[Fact]
	public void UniformStretchMouseMappingIgnoresHorizontalLetterbox()
	{
		var bounds = new Size(1200, 960);
		var source = new Size(352, 288);
		Assert.True(FramebufferPresenter.TryCalculateUniformDestination(bounds, source, out var destination));

		Assert.False(FramebufferPresenter.TryMapUniformStretchPoint(
			bounds,
			source,
			new Point(destination.X - 1, bounds.Height / 2.0),
			out _));

		Assert.True(FramebufferPresenter.TryMapUniformStretchPoint(
			bounds,
			source,
			new Point(destination.X, destination.Y),
			out var topLeft));
		Assert.Equal(0, topLeft.X, precision: 6);
		Assert.Equal(0, topLeft.Y, precision: 6);
	}

	[Fact]
	public void UniformStretchMouseMappingMapsCenterToFramebufferCenter()
	{
		var bounds = new Size(1200, 960);
		var source = new Size(352, 288);
		Assert.True(FramebufferPresenter.TryCalculateUniformDestination(bounds, source, out var destination));

		Assert.True(FramebufferPresenter.TryMapUniformStretchPoint(
			bounds,
			source,
			destination.Center,
			out var center));

		Assert.Equal(176, center.X, precision: 6);
		Assert.Equal(144, center.Y, precision: 6);
	}

	[Fact]
	public void PresentationViewportUsesTimingDerivedCroppedCoordinates()
	{
		var palHighRes = CopperScreenPresentationGeometry.FromRasterTiming(RasterTiming.Pal, superHighRes: false);
		var palSuperHighRes = CopperScreenPresentationGeometry.FromRasterTiming(RasterTiming.Pal, superHighRes: true);
		var ntscSuperHighRes = CopperScreenPresentationGeometry.FromRasterTiming(RasterTiming.Ntsc, superHighRes: true);

		Assert.Equal(new PixelRect(64, 32, 640, 512), MainWindow.GetCroppedPresentationViewport(palHighRes));
		Assert.Equal(new PixelRect(128, 32, 1280, 512), MainWindow.GetCroppedPresentationViewport(palSuperHighRes));
		Assert.Equal(new PixelRect(128, 32, 1280, 400), MainWindow.GetCroppedPresentationViewport(ntscSuperHighRes));
	}

	[Fact]
	public void UniformStretchMouseMappingAddsHighResolutionCroppedViewportOrigin()
	{
		Assert.True(FramebufferPresenter.TryMapUniformStretchPoint(
			new Size(960, 768),
			new PixelRect(64, 32, 640, 512),
			new Point(480, 384),
			out var center));

		Assert.Equal(384, center.X, precision: 6);
		Assert.Equal(288, center.Y, precision: 6);
	}

	[Theory]
	[InlineData(true, false, 0.5)]
	[InlineData(false, false, 1.0)]
	[InlineData(true, true, 8.0 / 15.0)]
	[InlineData(false, true, 16.0 / 15.0)]
	public void PalPresentationGeometryUsesExpectedHorizontalPixelAspect(bool superHighRes, bool crtCorrect, double expected)
	{
		var geometry = CopperScreenPresentationGeometry.FromRasterTiming(RasterTiming.Pal, superHighRes);
		var mode = crtCorrect ? CopperScreenPixelAspectMode.CrtCorrect : CopperScreenPixelAspectMode.Lcd;
		Assert.Equal(expected, geometry.GetHorizontalPixelAspect(mode), precision: 6);
	}

	[Theory]
	[InlineData(false, 5.0 / 6.0)]
	[InlineData(true, 5.0 / 12.0)]
	public void NtscCrtPresentationGeometryUsesExpectedHorizontalPixelAspect(bool superHighRes, double expected)
	{
		var geometry = CopperScreenPresentationGeometry.FromRasterTiming(RasterTiming.Ntsc, superHighRes);
		Assert.Equal(expected, geometry.GetHorizontalPixelAspect(CopperScreenPixelAspectMode.CrtCorrect), precision: 6);
	}

	[Fact]
	public void SuperHighResAspectMapsRenderedCenterBackToRawFramebufferCenter()
	{
		var source = new PixelRect(128, 32, 1280, 512);
		Assert.True(FramebufferPresenter.TryMapUniformStretchPoint(
			new Size(1280, 512), source, new Point(640, 256), horizontalPixelAspect: 0.5, renderScaling: 1.0, out var point));

		Assert.Equal(768, point.X, precision: 6);
		Assert.Equal(288, point.Y, precision: 6);
	}

	[Fact]
	public void UnclampedUniformStretchMouseMappingKeepsRelativeDeltasOutsideImage()
	{
		var bounds = new Size(1200, 960);
		var source = new Size(352, 288);
		Assert.True(FramebufferPresenter.TryCalculateUniformDestination(bounds, source, out var destination));
		var scale = destination.Width / source.Width;

		Assert.False(FramebufferPresenter.TryMapUniformStretchPoint(
			bounds,
			source,
			new Point(destination.X - scale, destination.Center.Y),
			out _));

		Assert.True(FramebufferPresenter.TryMapUniformStretchPointUnclamped(
			bounds,
			source,
			new Point(destination.X - scale, destination.Center.Y),
			out var point));
		Assert.Equal(-1, point.X, precision: 6);
		Assert.Equal(144, point.Y, precision: 6);
	}
}
