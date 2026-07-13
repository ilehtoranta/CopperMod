namespace CopperMod.Sid.Tests;

public sealed class Voice3ReleaseFixtureTests
{
	[Fact]
	public void Voice3Release9ToZeroFixtureUsesExpectedWriteOrderAndCycleSpacing()
	{
		var module = SidParser.Parse(SidFixtureBuilder.CreateVoice3Release9ToZeroPsid());
		var machine = new C64Machine(module);
		machine.Reset(0);
		machine.Sid.ClearCapturedWrites();

		machine.RunFrame();

		var writes = machine.SidWrites
			.Where(write => write.Register is 0x12 or 0x13 or 0x14)
			.ToArray();

		Assert.Equal(
			new (byte Register, byte Value)[]
			{
				(0x12, 0x81),
				(0x12, 0x80),
				(0x13, 0x00),
				(0x14, 0x00)
			},
			writes.Select(write => (write.Register, write.Value)).ToArray());

		Assert.Equal(1080, writes[1].Cycle - writes[0].Cycle);
		Assert.Equal(973, writes[2].Cycle - writes[1].Cycle);
		Assert.Equal(977, writes[3].Cycle - writes[1].Cycle);
		Assert.Equal(4, writes[3].Cycle - writes[2].Cycle);
	}

	[Fact]
	public void Voice3Release9ToZeroFixtureRunsOnlyOnce()
	{
		var module = SidParser.Parse(SidFixtureBuilder.CreateVoice3Release9ToZeroPsid());
		var machine = new C64Machine(module);
		machine.Reset(0);
		machine.Sid.ClearCapturedWrites();

		machine.RunFrame();
		machine.Sid.ClearCapturedWrites();
		machine.RunFrame();

		Assert.Empty(machine.SidWrites);
	}

	[Fact]
	public void Voice3Release9ToZeroFixtureUsesZeroReleaseAfterSrDropsToZero()
	{
		var module = SidParser.Parse(SidFixtureBuilder.CreateVoice3Release9ToZeroPsid());
		var machine = new C64Machine(module);
		machine.Reset(0);
		machine.Sid.ClearCapturedWrites();
		machine.BeginFrame();
		machine.Sid.AdvanceTo(machine.Cycle);

		var afterPlay = machine.Sid.Chips[0].DebugState.Voices[2];

		Assert.Equal(3, afterPlay.EnvelopeState);
		Assert.Equal(0, afterPlay.EnvelopeCounter);

		machine.RunCycles(SidConstants.PalCyclesPerFrame * 2);

		var afterTwoFrames = machine.Sid.Chips[0].DebugState.Voices[2];

		Assert.Equal(3, afterTwoFrames.EnvelopeState);
		Assert.Equal(0, afterTwoFrames.EnvelopeCounter);
	}
}
