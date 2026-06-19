using System.Text;

namespace CopperMod.Sid.Tests;

internal static class SidFixtureBuilder
{
	public static byte[] CreatePsid(
		byte[] payload,
		ushort loadAddress = 0x1000,
		ushort initAddress = 0x1000,
		ushort playAddress = 0x1004,
		ushort songs = 2,
		ushort startSong = 2,
		uint speed = 0,
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
		WriteBigEndian(data, 0x12, speed);
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
		payload[0] = (byte)(loadAddress & 0xFF);
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

	public static byte[] CreateBasicRsid(params (ushort LineNumber, byte[] Tokens)[] lines)
	{
		return CreateBasicRsid(lines, Array.Empty<byte>(), 0);
	}

	public static byte[] CreateBasicPrg(params (ushort LineNumber, byte[] Tokens)[] lines)
	{
		const ushort loadAddress = 0x0801;
		var program = BuildBasicProgram(lines);
		var payload = new byte[program.Length + 2];
		payload[0] = (byte)(loadAddress & 0xFF);
		payload[1] = (byte)(loadAddress >> 8);
		program.CopyTo(payload, 2);
		return payload;
	}

	public static byte[] CreateBasicRsid((ushort LineNumber, byte[] Tokens)[] lines, byte[] trailingBytes, ushort trailingAddress)
	{
		const ushort loadAddress = 0x0801;
		var program = BuildBasicProgram(lines);
		var payloadLength = program.Length;
		if (trailingBytes.Length > 0)
		{
			payloadLength = Math.Max(payloadLength, trailingAddress - loadAddress + trailingBytes.Length);
		}

		var payload = new byte[payloadLength + 2];
		payload[0] = (byte)(loadAddress & 0xFF);
		payload[1] = (byte)(loadAddress >> 8);
		program.CopyTo(payload, 2);
		if (trailingBytes.Length > 0)
		{
			trailingBytes.CopyTo(payload, 2 + trailingAddress - loadAddress);
		}

		var data = new byte[0x7C + payload.Length];
		WriteAscii(data, 0, "RSID");
		WriteBigEndian(data, 4, (ushort)2);
		WriteBigEndian(data, 6, (ushort)0x7C);
		WriteBigEndian(data, 8, (ushort)0);
		WriteBigEndian(data, 0x0A, (ushort)0);
		WriteBigEndian(data, 0x0C, (ushort)0);
		WriteBigEndian(data, 0x0E, (ushort)1);
		WriteBigEndian(data, 0x10, (ushort)1);
		WriteBigEndian(data, 0x12, 0U);
		WriteFixed(data, 0x16, "Generated BASIC RSID");
		WriteFixed(data, 0x36, "CopperMod");
		WriteFixed(data, 0x56, "2026");
		WriteBigEndian(data, 0x76, (ushort)((1 << 1) | (1 << 2) | (1 << 4)));
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

	public static byte[] CreateVoice3Release9ToZeroPsid()
	{
		return CreatePsid(
			Voice3Release9ToZeroProgram(),
			loadAddress: 0x1000,
			initAddress: 0x1000,
			playAddress: 0x1029,
			songs: 1,
			startSong: 1,
			title: "Voice3 Release 9 To 0",
			author: "CopperMod",
			released: "2026");
	}

	public static byte[] CreateCommandoLikeDryPulseMixPsid()
	{
		var program = CommandoLikeDryPulseMixProgram(out var playOffset);
		return CreatePsid(
			program,
			loadAddress: 0x1000,
			initAddress: 0x1000,
			playAddress: (ushort)(0x1000 + playOffset),
			songs: 1,
			startSong: 1,
			title: "Commando-Like Dry Pulse Mix",
			author: "CopperMod",
			released: "2026");
	}

	private static byte[] Voice3Release9ToZeroProgram()
	{
		return new byte[]
		{
			0xA9, 0x00,       // init: LDA #$00
			0x8D, 0x00, 0x02, // STA $0200; one-shot play flag
			0xA2, 0x18,       // LDX #$18
			0xA9, 0x00,       // LDA #$00
			0x9D, 0x00, 0xD4, // clear: STA $D400,X
			0xCA,             // DEX
			0x10, 0xFA,       // BPL clear
			0xA9, 0x0F,       // LDA #$0F
			0x8D, 0x18, 0xD4, // STA $D418
			0xA9, 0xA9,       // LDA #$A9
			0x8D, 0x0E, 0xD4, // STA $D40E
			0xA9, 0x03,       // LDA #$03
			0x8D, 0x0F, 0xD4, // STA $D40F
			0xA9, 0x0A,       // LDA #$0A; attack 0, decay A
			0x8D, 0x13, 0xD4, // STA $D413
			0xA9, 0x09,       // LDA #$09; sustain 0, release 9
			0x8D, 0x14, 0xD4, // STA $D414
			0x60,             // RTS

			0xAD, 0x00, 0x02, // play: LDA $0200
			0xD0, 0x25,       // BNE done
			0xEE, 0x00, 0x02, // INC $0200
			0xA9, 0x81,       // LDA #$81; noise + gate on
			0x8D, 0x12, 0xD4, // STA $D412
			0xA2, 0xD6,       // LDX #$D6; 214
			0xCA,             // delay1: DEX
			0xD0, 0xFD,       // BNE delay1
			0x24, 0x02,       // BIT $02; first delay = 1074 cycles
			0xA9, 0x80,       // LDA #$80; gate off, keep noise selected
			0x8D, 0x12, 0xD4, // STA $D412
			0xA2, 0xC0,       // LDX #$C0; 192
			0xCA,             // delay2: DEX
			0xD0, 0xFD,       // BNE delay2
			0x2C, 0x00, 0x02, // BIT $0200
			0xEA,             // NOP; second delay = 967 cycles
			0xA9, 0x00,       // LDA #$00
			0x8D, 0x13, 0xD4, // STA $D413
			0x8D, 0x14, 0xD4, // STA $D414
			0x60              // done: RTS
		};
	}

	private static byte[] CommandoLikeDryPulseMixProgram(out int playOffset)
	{
		var program = new List<byte>
		{
			0xA9, 0x00,       // init: LDA #$00
			0x8D, 0x00, 0x02, // STA $0200; voice 3 pulse-width LFO seed
			0xA2, 0x18,       // LDX #$18
			0xA9, 0x00,       // LDA #$00
			0x9D, 0x00, 0xD4, // clear: STA $D400,X
			0xCA,             // DEX
			0x10, 0xFA        // BPL clear
		};

		AddWrite(0x00, 0xD415); // cutoff low
		AddWrite(0x00, 0xD416); // cutoff high
		AddWrite(0x00, 0xD417); // no voices routed through the filter
		AddWrite(0x0F, 0xD418); // full volume, filter modes off, voice 3 audible

		AddVoice(0xD400, frequency: 0x1220, pulseWidth: 0x0800, control: 0x21); // voice 1 saw lead
		AddVoice(0xD407, frequency: 0x0A90, pulseWidth: 0x0620, control: 0x41); // voice 2 pulse companion
		AddVoice(0xD40E, frequency: 0x03A9, pulseWidth: 0x0180, control: 0x41); // voice 3 low pulse bass

		program.Add(0x60); // RTS
		playOffset = program.Count;
		program.AddRange(new byte[]
		{
			0xAD, 0x00, 0x02, // play: LDA $0200
			0x18,             // CLC
			0x69, 0x16,       // ADC #$16
			0x8D, 0x00, 0x02, // STA $0200
			0x8D, 0x10, 0xD4, // STA $D410; voice 3 pulse width low byte
			0xA9, 0x01,       // LDA #$01
			0x8D, 0x11, 0xD4, // STA $D411; keep voice 3 pulse width in low bass range
			0x60              // RTS
		});
		return program.ToArray();

		void AddVoice(ushort baseAddress, ushort frequency, ushort pulseWidth, byte control)
		{
			AddWrite((byte)frequency, baseAddress);
			AddWrite((byte)(frequency >> 8), (ushort)(baseAddress + 1));
			AddWrite((byte)pulseWidth, (ushort)(baseAddress + 2));
			AddWrite((byte)(pulseWidth >> 8), (ushort)(baseAddress + 3));
			AddWrite(0x00, (ushort)(baseAddress + 5)); // fastest attack, fastest decay
			AddWrite(0xF0, (ushort)(baseAddress + 6)); // full sustain
			AddWrite(control, (ushort)(baseAddress + 4));
		}

		void AddWrite(byte value, ushort address)
		{
			program.Add(0xA9);
			program.Add(value);
			program.Add(0x8D);
			program.Add((byte)address);
			program.Add((byte)(address >> 8));
		}
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

	private static byte[] BuildBasicProgram((ushort LineNumber, byte[] Tokens)[] lines)
	{
		const ushort loadAddress = 0x0801;
		var bytes = new List<byte>();
		var address = loadAddress;
		for (var i = 0; i < lines.Length; i++)
		{
			var tokens = lines[i].Tokens;
			var next = (ushort)(address + 4 + tokens.Length + 1);
			bytes.Add((byte)next);
			bytes.Add((byte)(next >> 8));
			bytes.Add((byte)lines[i].LineNumber);
			bytes.Add((byte)(lines[i].LineNumber >> 8));
			bytes.AddRange(tokens);
			bytes.Add(0);
			address = next;
		}

		bytes.Add(0);
		bytes.Add(0);
		return bytes.ToArray();
	}
}
