namespace CopperMod.Sid.Tests;

public sealed class SidReadbackTests
{
	[Fact]
	public void OscillatorThreeReadUsesCurrentWaveformOutputAtReadCycle()
	{
		var sid = CreateSid();
		Assert.True(sid.TryWrite(0xD40E, 0x00, 0));
		Assert.True(sid.TryWrite(0xD40F, 0x80, 0));
		Assert.True(sid.TryWrite(0xD412, 0x20, 0));

		Assert.True(sid.TryRead(0xD41B, cycle: 3, out var value));

		Assert.Equal(0x01, value);
		Assert.Equal(0x00018000u, sid.Chips[0].DebugState.Voices[2].Accumulator);
	}

	[Fact]
	public void EnvelopeThreeReadUsesCurrentEnvelopeAtReadCycle()
	{
		var sid = CreateSid();
		Assert.True(sid.TryWrite(0xD413, 0x00, 0));
		Assert.True(sid.TryWrite(0xD414, 0xF0, 0));
		Assert.True(sid.TryWrite(0xD412, 0x11, 0));

		Assert.True(sid.TryRead(0xD41C, cycle: 8, out var beforeStep));
		Assert.True(sid.TryRead(0xD41C, cycle: 9, out var step));

		Assert.Equal(0x00, beforeStep);
		Assert.Equal(0x01, step);
	}

	[Fact]
	public void ReadOnlyPotRegistersReturnUnconnectedHighAndRefreshOpenBus()
	{
		var sid = CreateSid();
		Assert.True(sid.TryWrite(0xD400, 0x55, 0));

		Assert.True(sid.TryRead(0xD400, out var writtenBus));
		Assert.True(sid.TryRead(0xD419, out var potX));
		Assert.True(sid.TryRead(0xD401, out var openBusAfterPot));

		Assert.Equal(0x55, writtenBus);
		Assert.Equal(0xFF, potX);
		Assert.Equal(0xFF, openBusAfterPot);
	}

	[Fact]
	public void MirroredDefaultSidReadUsesSameReadbackRegister()
	{
		var sid = CreateSid();
		Assert.True(sid.TryWrite(0xD40E, 0x00, 0));
		Assert.True(sid.TryWrite(0xD40F, 0x80, 0));
		Assert.True(sid.TryWrite(0xD412, 0x20, 0));

		Assert.True(sid.TryRead(0xD43B, cycle: 3, out var mirroredOscillator));

		Assert.Equal(0x01, mirroredOscillator);
	}

	private static SidSystem CreateSid()
	{
		return new SidSystem(new[] { new SidChipPlacement(0, SidConstants.DefaultSidBaseAddress) }, SidChipModel.Mos6581);
	}
}
