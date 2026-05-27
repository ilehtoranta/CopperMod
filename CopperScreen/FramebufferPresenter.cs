using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace CopperScreen;

internal sealed class FramebufferPresenter : Image
{
	private readonly WriteableBitmap _bitmap;
	private readonly int _width;
	private readonly int _height;

	public FramebufferPresenter(int width, int height)
	{
		_width = width;
		_height = height;
		Stretch = Stretch.Uniform;
		_bitmap = new WriteableBitmap(
			new PixelSize(width, height),
			new Vector(96, 96),
			PixelFormat.Bgra8888,
			AlphaFormat.Opaque);
		Source = _bitmap;
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
}
