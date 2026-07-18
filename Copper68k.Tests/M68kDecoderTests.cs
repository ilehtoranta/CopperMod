using Copper68k;

namespace Copper68k.Tests;

public sealed class M68kDecoderTests
{
	[Fact]
	public void DecodesMoveq()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x7012);

		var decoded = Decode(bus, 0x1000);

		Assert.Equal(M68kJitOperation.Moveq, decoded.Operation);
		Assert.Equal(M68kOperandSize.Long, decoded.Size);
		Assert.Equal(0, decoded.Register);
		Assert.Equal(0x12, decoded.QuickValue);
		Assert.Equal(2, decoded.Length);
		Assert.False(decoded.StopsTrace);
	}

	[Fact]
	public void DecodesMoveaLongImmediate()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x207C, 0x1234, 0x5678);

		var decoded = Decode(bus, 0x1000);

		Assert.Equal(M68kJitOperation.Movea, decoded.Operation);
		Assert.Equal(M68kOperandSize.Long, decoded.Size);
		Assert.Equal(M68kJitEaKind.Immediate, decoded.Source.Kind);
		Assert.Equal(0x1234_5678u, decoded.Source.Immediate);
		Assert.Equal(M68kJitEaKind.AddressRegister, decoded.Destination.Kind);
		Assert.Equal(0, decoded.Destination.Register);
		Assert.Equal(6, decoded.Length);
	}

	[Fact]
	public void DecodesPcRelativeLea()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x41FA, 0x0010);

		var decoded = Decode(bus, 0x1000);

		Assert.Equal(M68kJitOperation.Lea, decoded.Operation);
		Assert.Equal(M68kJitEaKind.PcDisplacement, decoded.Source.Kind);
		Assert.Equal(0x1002u, decoded.Source.ExtensionAddress);
		Assert.Equal(0x0010, decoded.Source.Extension0);
		Assert.Equal(4, decoded.Length);
	}

	[Theory]
	[InlineData(0x44DD, (int)M68kJitOperation.MoveToCcr)]
	[InlineData(0x46DD, (int)M68kJitOperation.MoveToSr)]
	public void DecodesMovePostincrementToStatus(ushort opcode, int operation)
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, opcode);

		var decoded = Decode(bus, 0x1000);

		Assert.Equal((M68kJitOperation)operation, decoded.Operation);
		Assert.Equal(M68kOperandSize.Word, decoded.Size);
		Assert.Equal(M68kJitEaKind.AddressPostincrement, decoded.Source.Kind);
		Assert.Equal(5, decoded.Source.Register);
		Assert.Equal(2, decoded.Length);
		Assert.True(decoded.StopsTrace);
	}

	[Fact]
	public void DecodesDbccAsTraceStop()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x51C8, 0xFFFC);

		var decoded = Decode(bus, 0x1000);

		Assert.Equal(M68kJitOperation.Dbcc, decoded.Operation);
		Assert.Equal(1, decoded.Condition);
		Assert.Equal(-4, decoded.Displacement);
		Assert.Equal(0x1002u, decoded.BranchBase);
		Assert.True(decoded.StopsTrace);
	}

	[Fact]
	public void KeepsSystemAndExceptionInstructionsOutOfTraces()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x4E72, 0x2700);
		bus.WriteWords(0x2000, 0xA000);

		Assert.False(M68kDecoder.TryDecode(bus, 0x1000, out _, out var stopReason));
		Assert.Equal(M68kJitBailoutReason.SystemInstruction, stopReason);
		Assert.False(M68kDecoder.TryDecode(bus, 0x2000, out _, out var lineAReason));
		Assert.Equal(M68kJitBailoutReason.ExceptionInstruction, lineAReason);
	}

	[Fact]
	public void KeepsHostTrapRootsOutOfTraces()
	{
		var bus = new Copper68kTestBus();
		bus.RegisterHostTrapStub(0x1000, _ => { });

		Assert.False(M68kDecoder.TryDecode(bus, 0x1000, out _, out var reason));
		Assert.Equal(M68kJitBailoutReason.HostTrap, reason);
		Assert.Equal(0, bus.HostTrapProbeCount);
	}

	private static M68kDecodedInstruction Decode(Copper68kTestBus bus, uint address)
	{
		Assert.True(M68kDecoder.TryDecode(bus, address, out var decoded, out var reason), reason.ToString());
		return decoded;
	}
}
