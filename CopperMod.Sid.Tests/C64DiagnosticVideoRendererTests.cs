namespace CopperMod.Sid.Tests;

public sealed class C64DiagnosticVideoRendererTests
{
	private const int DisplayX = 32;
	private const int DisplayY = 36;

	[Fact]
	public void RenderStandardTextUsesScreenColorAndCharacterMemory()
	{
		var fixture = CreateFixture();
		fixture.Vic[0x18] = 0x18;
		fixture.Ram[0x0400] = 1;
		fixture.Ram[0x2008] = 0x80;
		fixture.ColorRam[0] = 1;

		var frame = Render(fixture);

		Assert.Equal(Color(1), Pixel(frame, DisplayX, DisplayY));
		Assert.Equal(Color(0), Pixel(frame, DisplayX + 1, DisplayY));
	}

	[Fact]
	public void RenderMulticolorTextExpandsTwoBitPixels()
	{
		var fixture = CreateFixture();
		fixture.Vic[0x16] = 0x10;
		fixture.Vic[0x18] = 0x18;
		fixture.Vic[0x22] = 2;
		fixture.Vic[0x23] = 5;
		fixture.Ram[0x0400] = 1;
		fixture.Ram[0x2008] = 0x1B;
		fixture.ColorRam[0] = 0x0B;

		var frame = Render(fixture);

		Assert.Equal(Color(0), Pixel(frame, DisplayX, DisplayY));
		Assert.Equal(Color(2), Pixel(frame, DisplayX + 2, DisplayY));
		Assert.Equal(Color(5), Pixel(frame, DisplayX + 4, DisplayY));
		Assert.Equal(Color(3), Pixel(frame, DisplayX + 6, DisplayY));
	}

	[Fact]
	public void RenderExtendedBackgroundTextUsesScreenCodeBackgroundSelect()
	{
		var fixture = CreateFixture();
		fixture.Vic[0x11] = 0x40;
		fixture.Vic[0x18] = 0x18;
		fixture.Vic[0x23] = 6;
		fixture.Ram[0x0400] = 0x80 | 1;
		fixture.Ram[0x2008] = 0x00;
		fixture.ColorRam[0] = 1;

		var frame = Render(fixture);

		Assert.Equal(Color(6), Pixel(frame, DisplayX, DisplayY));
	}

	[Fact]
	public void RenderStandardBitmapUsesBitmapMemoryAndScreenNibbles()
	{
		var fixture = CreateFixture();
		fixture.Vic[0x11] = 0x20;
		fixture.Vic[0x18] = 0x18;
		fixture.Ram[0x0400] = 0x21;
		fixture.Ram[0x2000] = 0x80;

		var frame = Render(fixture);

		Assert.Equal(Color(2), Pixel(frame, DisplayX, DisplayY));
		Assert.Equal(Color(1), Pixel(frame, DisplayX + 1, DisplayY));
	}

	[Fact]
	public void RenderMulticolorBitmapUsesBackgroundScreenAndColorRam()
	{
		var fixture = CreateFixture();
		fixture.Vic[0x11] = 0x20;
		fixture.Vic[0x16] = 0x10;
		fixture.Vic[0x18] = 0x18;
		fixture.Ram[0x0400] = 0x12;
		fixture.Ram[0x2000] = 0x1B;
		fixture.ColorRam[0] = 3;

		var frame = Render(fixture);

		Assert.Equal(Color(0), Pixel(frame, DisplayX, DisplayY));
		Assert.Equal(Color(1), Pixel(frame, DisplayX + 2, DisplayY));
		Assert.Equal(Color(2), Pixel(frame, DisplayX + 4, DisplayY));
		Assert.Equal(Color(3), Pixel(frame, DisplayX + 6, DisplayY));
	}

	[Fact]
	public void RenderUsesVicBankBaseForScreenAndCharacterMemory()
	{
		var fixture = CreateFixture();
		fixture.Vic[0x18] = 0x18;
		fixture.Ram[0x4400] = 1;
		fixture.Ram[0x6008] = 0x80;
		fixture.ColorRam[0] = 1;

		var frame = Render(fixture, vicBankBase: 0x4000);

		Assert.Equal(Color(1), Pixel(frame, DisplayX, DisplayY));
	}

	[Fact]
	public void RenderUsesBorderColorOutsideDisplayArea()
	{
		var fixture = CreateFixture();
		fixture.Vic[0x20] = 2;

		var frame = Render(fixture);

		Assert.Equal(Color(2), Pixel(frame, 0, 0));
	}

	[Fact]
	public void RenderSpritesUsesPointerDataAndSpriteColor()
	{
		var fixture = CreateFixture();
		fixture.Vic[0x15] = 0x01;
		fixture.Vic[0x18] = 0x10;
		fixture.Vic[0x00] = 24;
		fixture.Vic[0x01] = 50;
		fixture.Vic[0x27] = 1;
		fixture.Ram[0x07F8] = 0x20;
		fixture.Ram[0x0800] = 0x80;

		var frame = Render(fixture);

		Assert.Equal(Color(1), Pixel(frame, DisplayX, DisplayY));
		Assert.Equal(Color(0), Pixel(frame, DisplayX + 1, DisplayY));
	}

	private static VideoFixture CreateFixture()
	{
		var fixture = new VideoFixture();
		fixture.Vic[0x21] = 0;
		return fixture;
	}

	private static C64VideoFrame Render(VideoFixture fixture, int vicBankBase = 0)
	{
		return C64DiagnosticVideoRenderer.Render(
			fixture.Ram,
			fixture.ColorRam,
			fixture.Vic,
			address => fixture.Ram[address],
			vicBankBase,
			spriteRegisterSnapshots: null,
			frameNumber: 12,
			sourceTime: TimeSpan.FromSeconds(1));
	}

	private static uint Pixel(C64VideoFrame frame, int x, int y)
	{
		return frame.Pixels[(y * frame.Width) + x].Value;
	}

	private static uint Color(int index)
	{
		return index switch
		{
			0 => new Argb32(255, 0x00, 0x00, 0x00).Value,
			1 => new Argb32(255, 0xFF, 0xFF, 0xFF).Value,
			2 => new Argb32(255, 0x88, 0x40, 0x00).Value,
			3 => new Argb32(255, 0xAA, 0xFF, 0xEE).Value,
			5 => new Argb32(255, 0x00, 0xCC, 0x55).Value,
			6 => new Argb32(255, 0x00, 0x00, 0xAA).Value,
			_ => throw new ArgumentOutOfRangeException(nameof(index))
		};
	}

	private sealed class VideoFixture
	{
		public byte[] Ram { get; } = new byte[65536];

		public byte[] ColorRam { get; } = new byte[1024];

		public byte[] Vic { get; } = new byte[0x40];
	}
}
