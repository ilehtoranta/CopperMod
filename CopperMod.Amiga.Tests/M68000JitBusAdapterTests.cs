using Copper68k;
using CopperMod.Amiga.Jit.M68000;

namespace CopperMod.Amiga.Tests;

public sealed class M68000JitBusAdapterTests
{
	[Fact]
	public void FastRamWriteInvalidatesOnlyTheAdapterLocalCodePage()
	{
		var bus = new AmigaBus(expansionRamSize: 0x2000, captureBusAccesses: false);
		var adapter = new M68000JitBusAdapter(bus);
		var address = bus.ExpansionRamBase + 0x120;
		var notifications = 0;
		adapter.JitCodeRangeWritten += (writtenAddress, byteCount) =>
		{
			notifications++;
			Assert.Equal(address, writtenAddress);
			Assert.Equal(2, byteCount);
		};

		Assert.True(adapter.IsJitCodeAddress(address, 2, M68kBusAccessKind.CpuInstructionFetch));
		Assert.Equal(0u, adapter.GetJitCodePageGeneration(address));

		var cycle = 0L;
		adapter.WriteWord(address, 0x4E71, ref cycle, M68kBusAccessKind.CpuDataWrite);

		Assert.Equal(1, notifications);
		Assert.Equal(1u, adapter.GetJitCodePageGeneration(address));
		Assert.True(adapter.JitCodeRangeGenerationMatches(address, 2, 1, 1));
	}

	[Fact]
	public void ChipRamIsNotJitEligibleAndDoesNotRaiseAnAdapterInvalidation()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		var adapter = new M68000JitBusAdapter(bus);
		var notifications = 0;
		adapter.JitCodeRangeWritten += (_, _) => notifications++;
		var cycle = 0L;

		Assert.False(adapter.IsJitCodeAddress(0x1000, 2, M68kBusAccessKind.CpuInstructionFetch));
		adapter.WriteWord(0x1000, 0x4E71, ref cycle, M68kBusAccessKind.CpuDataWrite);

		Assert.Equal(0, notifications);
		Assert.Equal(0u, adapter.GetJitCodePageGeneration(0x1000));
	}

	[Fact]
	public void M68000JitCoreCompilesAgainstTheGenericAdapterContract()
	{
		var bus = new AmigaBus(expansionRamSize: 0x2000, captureBusAccesses: false);
		var adapter = new M68000JitBusAdapter(bus);
		var code = bus.ExpansionRamBase + 0x100;
		var cycle = 0L;
		adapter.WriteWord(code, 0x7001, ref cycle, M68kBusAccessKind.CpuDataWrite); // MOVEQ #1,D0
		adapter.WriteWord(code + 2, 0x60FC, ref cycle, M68kBusAccessKind.CpuDataWrite); // BRA.S MOVEQ

		using var cpu = new M68kJitCore(adapter, enableV2: false);
		cpu.Reset(code, 0x4000);
		cpu.ExecuteInstructions(256, cpu.State.Cycles + 100_000, new TestBoundary());

		Assert.True(cpu.Counters.CompiledTraces > 0);
		Assert.True(cpu.Counters.TraceHits > 0);
	}

	[Fact]
	public void SelfModifyingFastRamInvalidatesTheCompiledTraceBeforeItsNextEntry()
	{
		var bus = new AmigaBus(expansionRamSize: 0x2000, captureBusAccesses: false);
		var adapter = new M68000JitBusAdapter(bus);
		var code = bus.ExpansionRamBase + 0x100;
		var cycle = 0L;
		adapter.WriteWord(code, 0x7001, ref cycle, M68kBusAccessKind.CpuDataWrite); // MOVEQ #1,D0
		adapter.WriteWord(code + 2, 0x60FC, ref cycle, M68kBusAccessKind.CpuDataWrite); // BRA.S MOVEQ

		using var cpu = new M68kJitCore(adapter, enableV2: false);
		cpu.Reset(code, 0x4000);
		cpu.ExecuteInstructions(256, cpu.State.Cycles + 100_000, new TestBoundary());
		Assert.True(cpu.Counters.TraceHits > 0);

		adapter.WriteWord(code, 0x7007, ref cycle, M68kBusAccessKind.CpuDataWrite); // MOVEQ #7,D0
		// The current MOVEQ was already in the MC68000 prefetch queue. It executes once
		// from that queue; the following loop entry must use the replacement opcode.
		cpu.ExecuteInstructions(4, cpu.State.Cycles + 100_000, new TestBoundary());

		Assert.Equal(7u, cpu.State.D[0]);
		Assert.True(cpu.Counters.Invalidations > 0 || cpu.Counters.GenerationGuardExits > 0);
	}

	private sealed class TestBoundary : IM68kInstructionBoundary
	{
		public bool BeforeInstruction() => true;

		public void AfterInstruction(long previousCycle, long currentCycle)
		{
		}
	}
}
