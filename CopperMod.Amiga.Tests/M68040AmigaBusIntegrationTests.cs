using Copper68k;
using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class M68040AmigaBusIntegrationTests
{
	private const uint CodeBase = 0x1000;
	private const uint StackBase = 0x8000;

	[Fact]
	public void M68040JitMaxSpeedProfileUsesFastRomInstructionFetches()
	{
		const uint RomBase = 0x00F8_0000;
		var bus = new AmigaBus();
		bus.MapReadOnlyMemory(RomBase, new byte[] { 0x70, 0x01 }); // MOVEQ #1,D0
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040JitMaxSpeed);
		cpu.Reset(RomBase, StackBase);

		cpu.ExecuteInstruction();

		Assert.Equal(1u, cpu.State.D[0]);
		Assert.Equal(1, cpu.Timing.LastInstructionTiming.Plan.NativeCycles);
		Assert.Equal(1, cpu.Timing.LastInstructionTiming.ElapsedNativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
		Assert.Empty(bus.BusAccesses);
	}

	[Fact]
	public void M68040JitMaxSpeedProfileUsesFastRealFastRamDataAccesses()
	{
		const uint RomBase = 0x00F8_0000;
		const uint FastBase = 0x0020_0000;
		var bus = new AmigaBus(realFastRamSize: 0x10000, realFastRamBase: FastBase);
		bus.ConfigureAutoconfigFastRamForHost();
		bus.MapReadOnlyMemory(
			RomBase,
			new byte[]
			{
				0x23, 0xFC, // MOVE.L #$12345678,$00200000.L
				0x12, 0x34,
				0x56, 0x78,
				0x00, 0x20,
				0x00, 0x00
			});
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040JitMaxSpeed);
		cpu.Reset(RomBase, StackBase);

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5678u, bus.ReadLong(FastBase));
		Assert.Equal(1, cpu.Timing.LastInstructionTiming.Plan.NativeCycles);
		Assert.Equal(1, cpu.Timing.LastInstructionTiming.ElapsedNativeCycles);
		Assert.Equal(1, cpu.State.Cycles);
		Assert.Empty(bus.BusAccesses);
	}

	[Fact]
	public void M68040JitMaxSpeedProfileUsesFastCiaAPortAAccesses()
	{
		const uint RomBase = 0x00F8_0000;
		var bus = new AmigaBus();
		bus.MapReadOnlyMemory(
			RomBase,
			new byte[]
			{
				0x08, 0xB9, // BCLR #1,$00BFE001.L
				0x00, 0x01,
				0x00, 0xBF,
				0xE0, 0x01
			});
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040JitMaxSpeed);
		cpu.Reset(RomBase, StackBase);

		cpu.ExecuteInstruction();

		Assert.True(bus.AudioFilterEnabled);
		Assert.Equal(1, cpu.Timing.LastInstructionTiming.Plan.NativeCycles);
		Assert.Equal(1, cpu.Timing.LastInstructionTiming.ElapsedNativeCycles);
		Assert.True(cpu.State.Cycles > 0);
		Assert.Empty(bus.BusAccesses);
	}

	[Fact]
	public void M68040JitMaxSpeedProfileUsesFastCiaAPortAWriteAccesses()
	{
		const uint RomBase = 0x00F8_0000;
		var bus = new AmigaBus();
		bus.MapReadOnlyMemory(
			RomBase,
			new byte[]
			{
				0x08, 0xF9, // BSET #1,$00BFE001.L
				0x00, 0x01,
				0x00, 0xBF,
				0xE0, 0x01
			});
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040JitMaxSpeed);
		cpu.Reset(RomBase, StackBase);

		cpu.ExecuteInstruction();

		Assert.False(bus.AudioFilterEnabled);
		Assert.Equal(1, cpu.Timing.LastInstructionTiming.Plan.NativeCycles);
		Assert.Equal(1, cpu.Timing.LastInstructionTiming.ElapsedNativeCycles);
		Assert.True(cpu.State.Cycles > 0);
		Assert.Empty(bus.BusAccesses);
	}

	[Fact]
	public void ApproximateIntegerFallbackStillUsesTranslatedChipBusAccesses()
	{
		const uint chipAddress = 0x3000;
		var bus = new AmigaBus();
		WriteWords(bus, CodeBase, 0x5290); // ADDQ.L #1,(A0)
		bus.WriteLong(chipAddress, 0x0000_0004);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.A[0] = chipAddress;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_0005u, bus.ReadLong(chipAddress));
		Assert.Contains(bus.BusAccesses, access =>
			access.Request.Requester == AmigaBusRequester.Cpu &&
			access.Request.Kind == AmigaBusAccessKind.CpuDataRead &&
			access.Request.Address == chipAddress &&
			access.Request.Size == AmigaBusAccessSize.Long);
		Assert.Contains(bus.BusAccesses, access =>
			access.Request.Requester == AmigaBusRequester.Cpu &&
			access.Request.Kind == AmigaBusAccessKind.CpuDataWrite &&
			access.Request.Address == chipAddress &&
			access.Request.Size == AmigaBusAccessSize.Long);
	}

	[Fact]
	public void CpuFetchBeyondConfiguredChipRamRaisesBusErrorInsteadOfMirroring()
	{
		var bus = new AmigaBus(chipRamSize: 512 * 1024);
		bus.WriteLong(2u * 4, 0x0000_2400);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(0x0010_0004, StackBase);

		cpu.ExecuteInstruction();

		Assert.Equal(0x2400u, cpu.State.ProgramCounter);
		Assert.Equal(StackBase - 8u, cpu.State.A[7]);
		Assert.Equal(0x0010_0004u, bus.ReadLong(StackBase - 6u));
		Assert.Equal(2 * 4, bus.ReadWord(StackBase - 2u));
		Assert.NotEqual(0u, cpu.State.M68040Mmu.Status);
	}

	[Fact]
	public void CpuDataAccessBeyondConfiguredChipRamKeepsExistingMirrorBehavior()
	{
		var bus = new AmigaBus(chipRamSize: 512 * 1024);
		bus.WriteLong(0x0000_0004, 0x00F1_0000);
		WriteWords(bus, CodeBase, 0x2039, 0x0010, 0x0004); // MOVE.L $00100004.L,D0
		bus.WriteLong(2u * 4, 0x0000_2400);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);

		cpu.ExecuteInstruction();

		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(0x00F1_0000u, cpu.State.D[0]);
		Assert.Equal(StackBase, cpu.State.A[7]);
		Assert.Equal(0u, cpu.State.M68040Mmu.Status);
	}

	[Fact]
	public void CpuHighUnmappedDataProbeUsesOpenBusInsteadOfBusError()
	{
		var bus = new AmigaBus(chipRamSize: 512 * 1024);
		WriteWords(bus, CodeBase, 0x2039, 0x00F0, 0x0000); // MOVE.L $00F00000.L,D0
		bus.WriteLong(2u * 4, 0x0000_2400);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);

		cpu.ExecuteInstruction();

		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(0u, cpu.State.D[0]);
		Assert.Equal(StackBase, cpu.State.A[7]);
		Assert.Equal(0u, cpu.State.M68040Mmu.Status);
	}

	[Fact]
	public void CpuHighThirtyTwoBitUnmappedDataProbeUsesOpenBusInsteadOfBusError()
	{
		var bus = new AmigaBus(chipRamSize: 512 * 1024);
		WriteWords(bus, CodeBase, 0x2039, 0xFFA0, 0x4A80); // MOVE.L $FFA04A80.L,D0
		bus.WriteLong(2u * 4, 0x0000_2400);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);

		cpu.ExecuteInstruction();

		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);
		Assert.Equal(0u, cpu.State.D[0]);
		Assert.Equal(StackBase, cpu.State.A[7]);
		Assert.Equal(0u, cpu.State.M68040Mmu.Status);
	}

	[Fact]
	public void PhysicalMapCacheObservesStrictMappingChanges()
	{
		var bus = new AmigaBus(chipRamSize: 512 * 1024);
		WriteWords(
			bus,
			CodeBase,
			0x2039, 0x00F0, 0x0000, // MOVE.L $00F00000.L,D0
			0x2239, 0x00F0, 0x0000); // MOVE.L $00F00000.L,D1
		bus.WriteLong(2u * 4, 0x0000_2400);
		var cpu = new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
		cpu.Reset(CodeBase, StackBase);

		cpu.ExecuteInstruction();
		Assert.Equal(CodeBase + 6u, cpu.State.ProgramCounter);

		bus.StrictCpuPhysicalDataMapping = true;
		cpu.ExecuteInstruction();

		Assert.Equal(0x2400u, cpu.State.ProgramCounter);
		Assert.Equal(StackBase - 8u, cpu.State.A[7]);
		Assert.NotEqual(0u, cpu.State.M68040Mmu.Status);
	}

	private static void WriteWords(AmigaBus bus, uint address, params ushort[] words)
	{
		for (var i = 0; i < words.Length; i++)
		{
			bus.WriteWord(address + (uint)(i * 2), words[i]);
		}
	}
}
