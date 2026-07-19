using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace CopperScreen;

internal sealed class FramebufferPresenter : Control
{
	private const int PresentationBitmapCount = 3;
	private WriteableBitmap[] _bitmaps;
	private int _width;
	private int _height;
	private PixelRect _sourceRect;
	private int _frontBitmapIndex;
	private bool _devicePixelExactLayout;

	public FramebufferPresenter(int width, int height)
	{
		_width = width;
		_height = height;
		UseLayoutRounding = true;
		RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
		_bitmaps = new WriteableBitmap[PresentationBitmapCount];
		for (var i = 0; i < _bitmaps.Length; i++)
		{
			_bitmaps[i] = CreateBitmap(width, height);
		}

		_sourceRect = new PixelRect(0, 0, width, height);
	}

	public void Update(int[] bgra)
	{
		var updateStartTimestamp = Stopwatch.GetTimestamp();
		ValidateFramebufferLength(bgra, _width, _height);

		var nextBitmapIndex = (_frontBitmapIndex + 1) % _bitmaps.Length;
		using (var framebuffer = _bitmaps[nextBitmapIndex].Lock())
		{
			CopyBgraRows(bgra, _width, _height, framebuffer.Address, framebuffer.RowBytes);
		}

		_frontBitmapIndex = nextBitmapIndex;
		InvalidateVisual();
		LastUpdateMilliseconds = Stopwatch.GetElapsedTime(updateStartTimestamp).TotalMilliseconds;
	}

	public int PixelWidth => _width;

	public int PixelHeight => _height;

	public void EnsureDimensions(int width, int height)
	{
		if (width == _width && height == _height)
		{
			return;
		}

		foreach (var bitmap in _bitmaps)
		{
			bitmap.Dispose();
		}

		_width = width;
		_height = height;
		_bitmaps = new WriteableBitmap[PresentationBitmapCount];
		for (var i = 0; i < _bitmaps.Length; i++)
		{
			_bitmaps[i] = CreateBitmap(width, height);
		}

		_frontBitmapIndex = 0;
		_sourceRect = new PixelRect(0, 0, width, height);
		InvalidateMeasure();
		InvalidateVisual();
	}

	public double LastUpdateMilliseconds { get; private set; }

	public double LastRenderMilliseconds { get; private set; }

	public bool DevicePixelExactLayout
	{
		get => _devicePixelExactLayout;
		set
		{
			if (_devicePixelExactLayout == value)
			{
				return;
			}

			_devicePixelExactLayout = value;
			InvalidateMeasure();
			InvalidateVisual();
		}
	}

	public override void Render(DrawingContext context)
	{
		var renderStartTimestamp = Stopwatch.GetTimestamp();
		base.Render(context);
		context.FillRectangle(Brushes.Black, Bounds);
		if (!TryCalculateUniformDestination(
				Bounds.Size,
				new Size(_sourceRect.Width, _sourceRect.Height),
				GetRenderScaling(),
				out var destination))
		{
			LastRenderMilliseconds = Stopwatch.GetElapsedTime(renderStartTimestamp).TotalMilliseconds;
			return;
		}

		context.DrawImage(
			_bitmaps[_frontBitmapIndex],
			new Rect(_sourceRect.X, _sourceRect.Y, _sourceRect.Width, _sourceRect.Height),
			destination);
		LastRenderMilliseconds = Stopwatch.GetElapsedTime(renderStartTimestamp).TotalMilliseconds;
	}

	public void SetSourceViewport(int x, int y, int width, int height)
	{
		x = Math.Clamp(x, 0, _width - 1);
		y = Math.Clamp(y, 0, _height - 1);
		width = Math.Clamp(width, 1, _width - x);
		height = Math.Clamp(height, 1, _height - y);
		_sourceRect = new PixelRect(x, y, width, height);
		InvalidateMeasure();
		InvalidateVisual();
	}

	public bool TryMapPointToFramebuffer(Point position, out Point framebufferPoint)
	{
		return TryMapUniformStretchPoint(Bounds.Size, _sourceRect, position, GetRenderScaling(), out framebufferPoint);
	}

	public bool TryMapPointToFramebufferUnclamped(Point position, out Point framebufferPoint)
	{
		return TryMapUniformStretchPointUnclamped(Bounds.Size, _sourceRect, position, GetRenderScaling(), out framebufferPoint);
	}

	public bool TryGetRenderedImageCenter(out Point center)
	{
		if (!TryCalculateUniformDestination(Bounds.Size, new Size(_sourceRect.Width, _sourceRect.Height), out var destination))
		{
			center = default;
			return false;
		}

		center = destination.Center;
		return true;
	}

	internal static bool TryMapUniformStretchPoint(Size bounds, PixelRect sourceRect, Point position, out Point framebufferPoint)
		=> TryMapUniformStretchPoint(bounds, sourceRect, position, renderScaling: 1.0, out framebufferPoint);

	internal static bool TryMapUniformStretchPoint(Size bounds, PixelRect sourceRect, Point position, double renderScaling, out Point framebufferPoint)
	{
		if (!TryMapUniformStretchPoint(bounds, new Size(sourceRect.Width, sourceRect.Height), position, renderScaling, out var sourcePoint))
		{
			framebufferPoint = default;
			return false;
		}

		framebufferPoint = new Point(sourceRect.X + sourcePoint.X, sourceRect.Y + sourcePoint.Y);
		return true;
	}

	internal static bool TryMapUniformStretchPointUnclamped(Size bounds, PixelRect sourceRect, Point position, out Point framebufferPoint)
		=> TryMapUniformStretchPointUnclamped(bounds, sourceRect, position, renderScaling: 1.0, out framebufferPoint);

	internal static bool TryMapUniformStretchPointUnclamped(Size bounds, PixelRect sourceRect, Point position, double renderScaling, out Point framebufferPoint)
	{
		if (!TryMapUniformStretchPointUnclamped(bounds, new Size(sourceRect.Width, sourceRect.Height), position, renderScaling, out var sourcePoint))
		{
			framebufferPoint = default;
			return false;
		}

		framebufferPoint = new Point(sourceRect.X + sourcePoint.X, sourceRect.Y + sourcePoint.Y);
		return true;
	}

	protected override Size MeasureOverride(Size availableSize)
	{
		var source = new Size(_sourceRect.Width, _sourceRect.Height);
		return _devicePixelExactLayout
			? CalculateDevicePixelExactLogicalSize(source, GetRenderScaling())
			: source;
	}

	internal static bool TryMapUniformStretchPoint(Size bounds, Size source, Point position, out Point framebufferPoint)
		=> TryMapUniformStretchPoint(bounds, source, position, renderScaling: 1.0, out framebufferPoint);

	internal static bool TryMapUniformStretchPoint(Size bounds, Size source, Point position, double renderScaling, out Point framebufferPoint)
	{
		framebufferPoint = default;
		if (!TryCalculateUniformDestination(bounds, source, renderScaling, out var destination))
		{
			return false;
		}

		var imageX = position.X - destination.X;
		var imageY = position.Y - destination.Y;
		if (imageX < 0 || imageY < 0 || imageX >= destination.Width || imageY >= destination.Height)
		{
			return false;
		}

		framebufferPoint = new Point(
			Math.Clamp(imageX / (destination.Width / source.Width), 0, source.Width - 1),
			Math.Clamp(imageY / (destination.Height / source.Height), 0, source.Height - 1));
		return true;
	}

	internal static bool TryMapUniformStretchPointUnclamped(Size bounds, Size source, Point position, out Point framebufferPoint)
		=> TryMapUniformStretchPointUnclamped(bounds, source, position, renderScaling: 1.0, out framebufferPoint);

	internal static bool TryMapUniformStretchPointUnclamped(Size bounds, Size source, Point position, double renderScaling, out Point framebufferPoint)
	{
		framebufferPoint = default;
		if (!TryCalculateUniformDestination(bounds, source, renderScaling, out var destination))
		{
			return false;
		}

		framebufferPoint = new Point(
			(position.X - destination.X) / (destination.Width / source.Width),
			(position.Y - destination.Y) / (destination.Height / source.Height));
		return true;
	}

	internal static bool TryCalculateUniformDestination(Size bounds, Size source, out Rect destination)
		=> TryCalculateUniformDestination(bounds, source, renderScaling: 1.0, out destination);

	internal static Size CalculateDevicePixelExactLogicalSize(Size source, double renderScaling)
	{
		if (source.Width <= 0 ||
			source.Height <= 0 ||
			renderScaling <= 0 ||
			double.IsNaN(renderScaling) ||
			double.IsInfinity(renderScaling))
		{
			return source;
		}

		return new Size(source.Width / renderScaling, source.Height / renderScaling);
	}

	internal static bool TryCalculateUniformDestination(Size bounds, Size source, double renderScaling, out Rect destination)
	{
		destination = default;
		if (bounds.Width <= 0 ||
			bounds.Height <= 0 ||
			source.Width <= 0 ||
			source.Height <= 0 ||
			renderScaling <= 0 ||
			double.IsNaN(renderScaling) ||
			double.IsInfinity(renderScaling))
		{
			return false;
		}

		var scale = Math.Min(bounds.Width / source.Width, bounds.Height / source.Height);
		if (scale <= 0)
		{
			return false;
		}

		var snappedScale = SnapUniformScaleToDevicePixels(scale, renderScaling);
		var imageWidth = Math.Min(bounds.Width, source.Width * snappedScale);
		var imageHeight = Math.Min(bounds.Height, source.Height * snappedScale);
		if (imageWidth <= 0 || imageHeight <= 0)
		{
			return false;
		}

		destination = new Rect(
			SnapOffsetToDevicePixel((bounds.Width - imageWidth) / 2.0, renderScaling),
			SnapOffsetToDevicePixel((bounds.Height - imageHeight) / 2.0, renderScaling),
			imageWidth,
			imageHeight);
		return true;
	}

	private double GetRenderScaling()
		=> TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;

	private static WriteableBitmap CreateBitmap(int width, int height)
		=> new(
			new PixelSize(width, height),
			new Vector(96, 96),
			PixelFormat.Bgra8888,
			AlphaFormat.Opaque);

	private static double SnapUniformScaleToDevicePixels(double scale, double renderScaling)
	{
		var snappedDeviceScale = Math.Floor(scale * renderScaling);
		if (snappedDeviceScale >= 1)
		{
			return snappedDeviceScale / renderScaling;
		}

		return scale;
	}

	private static double SnapOffsetToDevicePixel(double value, double renderScaling)
		=> Math.Round(Math.Max(0, value) * renderScaling) / renderScaling;

	internal static void CopyBgraRows(int[] bgra, int width, int height, IntPtr destination, int destinationRowBytes)
	{
		ValidateFramebufferLength(bgra, width, height);
		if (destination == IntPtr.Zero)
		{
			throw new ArgumentException("The destination pointer must be non-zero.", nameof(destination));
		}

		var sourceRowBytes = checked(width * sizeof(int));
		if (destinationRowBytes < sourceRowBytes)
		{
			throw new ArgumentException("The destination row stride is too small for the framebuffer width.", nameof(destinationRowBytes));
		}

		for (var y = 0; y < height; y++)
		{
			Marshal.Copy(
				bgra,
				y * width,
				IntPtr.Add(destination, y * destinationRowBytes),
				width);
		}
	}

	private static void ValidateFramebufferLength(int[] bgra, int width, int height)
	{
		if (bgra.Length < checked(width * height))
		{
			throw new ArgumentException("The framebuffer is too small.", nameof(bgra));
		}
	}
}
