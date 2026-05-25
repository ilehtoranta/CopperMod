using System.Text;

namespace AmigaTracker.Sid.Tests;

internal static class SidFixtureBuilder
{
	public static byte[] CreatePsid(
		byte[] payload,
		ushort loadAddress = 0x1000,
		ushort initAddress = 0x1000,
		ushort playAddress = 0x1004,
		ushort songs = 2,
		ushort startSong = 2,
		ushort flags = (1 << 2) | (2 << 4),
		string title = "Generated SID",
		string author = "CopperMod",
		string released = "2026")
	{
		var data = new byte[0x7C + payload.Length];
		WriteAscii(data, 0, "PSID");
		WriteBigEndian(data, 4, (ushort)2);
		WriteBigEndian(data, 6, (ushort)0x7C);
		WriteBigEndian(data, 8, loadAddress);
		WriteBigEndian(data, 0x0A, initAddress);
		WriteBigEndian(data, 0x0C, playAddress);
		WriteBigEndian(data, 0x0E, songs);
		WriteBigEndian(data, 0x10, startSong);
		WriteBigEndian(data, 0x12, 0U);
		WriteFixed(data, 0x16, title);
		WriteFixed(data, 0x36, author);
		WriteFixed(data, 0x56, released);
		WriteBigEndian(data, 0x76, flags);
		payload.CopyTo(data, 0x7C);
		return data;
	}

	public static byte[] CreateRsid(byte[] program, ushort loadAddress = 0x1000, ushort initAddress = 0x1000)
	{
		var payload = new byte[program.Length + 2];
		payload[0] = (byte)loadAddress;
		payload[1] = (byte)(loadAddress >> 8);
		program.CopyTo(payload, 2);
		var data = new byte[0x7C + payload.Length];
		WriteAscii(data, 0, "RSID");
		WriteBigEndian(data, 4, (ushort)2);
		WriteBigEndian(data, 6, (ushort)0x7C);
		WriteBigEndian(data, 8, (ushort)0);
		WriteBigEndian(data, 0x0A, initAddress);
		WriteBigEndian(data, 0x0C, (ushort)0);
		WriteBigEndian(data, 0x0E, (ushort)1);
		WriteBigEndian(data, 0x10, (ushort)1);
		WriteBigEndian(data, 0x12, 0U);
		WriteFixed(data, 0x16, "Generated RSID");
		WriteFixed(data, 0x36, "CopperMod");
		WriteFixed(data, 0x56, "2026");
		WriteBigEndian(data, 0x76, (ushort)((1 << 2) | (1 << 4)));
		payload.CopyTo(data, 0x7C);
		return data;
	}

	public static byte[] SimpleToneProgram()
	{
		return new byte[]
		{
			0x8D, 0x00, 0xD4, // STA $D400; init writes selected subtune into frequency low
			0x60,             // RTS
			0xA9, 0x20,       // LDA #$20
			0x8D, 0x01, 0xD4, // STA $D401
			0xA9, 0x00,       // LDA #$00
			0x8D, 0x02, 0xD4, // STA $D402
			0xA9, 0x08,       // LDA #$08
			0x8D, 0x03, 0xD4, // STA $D403
			0xA9, 0x41,       // LDA #$41
			0x8D, 0x04, 0xD4, // STA $D404
			0xA9, 0xF0,       // LDA #$F0
			0x8D, 0x05, 0xD4, // STA $D405
			0xA9, 0xF0,       // LDA #$F0
			0x8D, 0x06, 0xD4, // STA $D406
			0xA9, 0x0F,       // LDA #$0F
			0x8D, 0x18, 0xD4, // STA $D418
			0x60              // RTS
		};
	}

	public static byte[] InitThenPlayToneProgram()
	{
		return new byte[]
		{
			0xA2, 0x80,       // LDX #$80
			0x8E, 0x00, 0x02, // STX $0200
			0xCA,             // DEX
			0xD0, 0xFA,       // BNE $1002; burn enough init cycles to catch stale SID clocks
			0x60,             // RTS
			0xA9, 0x00,       // LDA #$00
			0x8D, 0x00, 0xD4, // STA $D400
			0xA9, 0x80,       // LDA #$80
			0x8D, 0x01, 0xD4, // STA $D401
			0xA9, 0x00,       // LDA #$00
			0x8D, 0x02, 0xD4, // STA $D402
			0xA9, 0x08,       // LDA #$08
			0x8D, 0x03, 0xD4, // STA $D403
			0xA9, 0x41,       // LDA #$41
			0x8D, 0x04, 0xD4, // STA $D404
			0xA9, 0x00,       // LDA #$00
			0x8D, 0x05, 0xD4, // STA $D405
			0xA9, 0xF0,       // LDA #$F0
			0x8D, 0x06, 0xD4, // STA $D406
			0xA9, 0x0F,       // LDA #$0F
			0x8D, 0x18, 0xD4, // STA $D418
			0x60              // RTS
		};
	}

	private static void WriteAscii(byte[] data, int offset, string text)
	{
		Encoding.ASCII.GetBytes(text, data.AsSpan(offset, text.Length));
	}

	private static void WriteFixed(byte[] data, int offset, string text)
	{
		Encoding.ASCII.GetBytes(text, data.AsSpan(offset, Math.Min(32, text.Length)));
	}

	private static void WriteBigEndian(byte[] data, int offset, ushort value)
	{
		data[offset] = (byte)(value >> 8);
		data[offset + 1] = (byte)value;
	}

	private static void WriteBigEndian(byte[] data, int offset, uint value)
	{
		data[offset] = (byte)(value >> 24);
		data[offset + 1] = (byte)(value >> 16);
		data[offset + 2] = (byte)(value >> 8);
		data[offset + 3] = (byte)value;
	}
}
