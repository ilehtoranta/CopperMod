using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace CopperScreen;

internal sealed class FramebufferPresenter : Control
{
	private readonly WriteableBitmap _bitmap;
	private readonly int _width;
	private readonly int _height;
	private PixelRect _sourceRect;

	public FramebufferPresenter(int width, int height)
	{
		_width = width;
		_height = height;
		RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
		_bitmap = new WriteableBitmap(
			new PixelSize(width, height),
			new Vector(96, 96),
			PixelFormat.Bgra8888,
			AlphaFormat.Opaque);
		_sourceRect = new PixelRect(0, 0, width, height);
	}

	public void Update(int[] bgra)
	{
		if (bgra.Length < _width * _height)
		{
			throw new ArgumentException("The framebuffer is too small.", nameof(bgra));
		}

		using var framebuffer = _bitmap.Lock();
		Marshal.Copy(bgra, 0, framebuffer.Address, _width * _height);
		InvalidateVisual();
	}

	public override void Render(DrawingContext context)
	{
		base.Render(context);
		if (!TryCalculateUniformDestination(Bounds.Size, new Size(_sourceRect.Width, _sourceRect.Height), out var destination))
		{
			return;
		}

		context.DrawImage(
			_bitmap,
			new Rect(_sourceRect.X, _sourceRect.Y, _sourceRect.Width, _sourceRect.Height),
			destination);
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
		return TryMapUniformStretchPoint(Bounds.Size, _sourceRect, position, out framebufferPoint);
	}

	internal static bool TryMapUniformStretchPoint(Size bounds, PixelRect sourceRect, Point position, out Point framebufferPoint)
	{
		if (!TryMapUniformStretchPoint(bounds, new Size(sourceRect.Width, sourceRect.Height), position, out var sourcePoint))
		{
			framebufferPoint = default;
			return false;
		}

		framebufferPoint = new Point(sourceRect.X + sourcePoint.X, sourceRect.Y + sourcePoint.Y);
		return true;
	}

	protected override Size MeasureOverride(Size availableSize)
		=> new(_sourceRect.Width, _sourceRect.Height);

	internal static bool TryMapUniformStretchPoint(Size bounds, Size source, Point position, out Point framebufferPoint)
	{
		framebufferPoint = default;
		if (bounds.Width <= 0 || bounds.Height <= 0 || source.Width <= 0 || source.Height <= 0)
		{
			return false;
		}

		var scale = Math.Min(bounds.Width / source.Width, bounds.Height / source.Height);
		if (scale <= 0)
		{
			return false;
		}

		var imageWidth = source.Width * scale;
		var imageHeight = source.Height * scale;
		var offsetX = (bounds.Width - imageWidth) / 2.0;
		var offsetY = (bounds.Height - imageHeight) / 2.0;
		var imageX = position.X - offsetX;
		var imageY = position.Y - offsetY;
		if (imageX < 0 || imageY < 0 || imageX >= imageWidth || imageY >= imageHeight)
		{
			return false;
		}

		framebufferPoint = new Point(
			Math.Clamp(imageX / scale, 0, source.Width - 1),
			Math.Clamp(imageY / scale, 0, source.Height - 1));
		return true;
	}

	private static bool TryCalculateUniformDestination(Size bounds, Size source, out Rect destination)
	{
		destination = default;
		if (bounds.Width <= 0 || bounds.Height <= 0 || source.Width <= 0 || source.Height <= 0)
		{
			return false;
		}

		var scale = Math.Min(bounds.Width / source.Width, bounds.Height / source.Height);
		if (scale <= 0)
		{
			return false;
		}

		var imageWidth = source.Width * scale;
		var imageHeight = source.Height * scale;
		destination = new Rect(
			(bounds.Width - imageWidth) / 2.0,
			(bounds.Height - imageHeight) / 2.0,
			imageWidth,
			imageHeight);
		return true;
	}
}
