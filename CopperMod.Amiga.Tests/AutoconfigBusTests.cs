using Copper68k;
using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class AutoconfigBusTests
{
	[Fact]
	public void ZorroIIFastRamIsUnmappedUntilAssignedAtTwoMegabytes()
	{
		var bus = new AmigaBus(realFastRamSize: 8 * 1024 * 1024);
		var map = (IM68kStablePhysicalAddressMap)bus;
		var generation = map.CpuPhysicalAddressMapGeneration;

		Assert.Equal(0xE0, bus.ReadByte(AutoconfigChain.ZorroIIConfigBase));
		Assert.False(bus.AutoconfigFastRam!.IsConfigured);
		bus.WriteByte(AutoconfigChain.ZorroIIConfigBase + 0x4A, 0x00, 0);
		bus.WriteByte(AutoconfigChain.ZorroIIConfigBase + 0x48, 0x20, 0);

		Assert.True(bus.AutoconfigFastRam.IsConfigured);
		Assert.Equal(0x0020_0000u, bus.RealFastRamBase);
		Assert.True(map.CpuPhysicalAddressMapGeneration > generation);
		bus.WriteLong(0x0020_0000u, 0x1234_5678u);
		Assert.Equal(0x1234_5678u, bus.ReadLong(0x0020_0000u));
	}

	[Fact]
	public void ZorroIIIWordAssignmentMapsHighMemoryAndDirectJitBanks()
	{
		const uint baseAddress = 0x1000_0000u;
		var bus = new AmigaBus(realFastRamSize: 16 * 1024 * 1024, realFastRamBase: baseAddress);

		Assert.Equal(0xA0, bus.ReadByte(AutoconfigChain.ZorroIIIConfigBase));
		bus.WriteWord(AutoconfigChain.ZorroIIIConfigBase + 0x44, 0x1000);
		bus.WriteLong(baseAddress + 2, 0x89AB_CDEFu);

		Assert.Equal(0x89AB_CDEFu, bus.ReadLong(baseAddress + 2));
		var directBus = Assert.IsAssignableFrom<IM68kJitDirectRamBus>(bus);
		Assert.True(directBus.TryGetJitDirectRamMap(out var map));
		Assert.Equal(65_536, map.BankKinds.Length);
		Assert.Equal(
			(byte)M68kJitDirectRamBankKind.RealFast,
			map.BankKinds[baseAddress >> map.BankShift]);
	}

	[Fact]
	public void CpuResetUnmapsButPreservesFastRamAndColdResetClearsIt()
	{
		const uint baseAddress = 0x1000_0000u;
		var bus = new AmigaBus(realFastRamSize: 16 * 1024 * 1024, realFastRamBase: baseAddress);
		bus.ConfigureAutoconfigFastRamForHost();
		bus.WriteLong(baseAddress, 0xCAFE_BABEu);

		bus.ResetExternalDevices(10);
		Assert.False(bus.AutoconfigFastRam!.IsConfigured);
		bus.ConfigureAutoconfigFastRamForHost();
		Assert.Equal(0xCAFE_BABEu, bus.ReadLong(baseAddress));

		bus.Reset();
		bus.ConfigureAutoconfigFastRamForHost();
		Assert.Equal(0u, bus.ReadLong(baseAddress));
	}

	[Fact]
	public void RtgBoardExposesSparseLinearMemoryOnlyAfterAutoconfigCompletes()
	{
		var bus = new AmigaBus(rtgVramSize: 2L * 1024 * 1024 * 1024);
		var map = (IM68kStablePhysicalAddressMap)bus;
		var generation = map.CpuPhysicalAddressMapGeneration;

		Assert.Equal(0xC0, bus.ReadByte(AutoconfigChain.ZorroIIConfigBase));
		Assert.Equal(0x10, bus.ReadByte(AutoconfigChain.ZorroIIConfigBase + 2));
		Assert.Equal(0, bus.RtgVram.CommittedPageCount);
		bus.ConfigureAutoconfigRtgForHost();
		var surface = bus.AllocateRtgVram(256 * 1024);

		Assert.Equal(0x8000_0000u, surface);
		Assert.True(bus.RtgVram.Active);
		Assert.Equal(0, bus.RtgVram.CommittedPageCount);
		Assert.True(map.CpuPhysicalAddressMapGeneration > generation);
		Assert.Equal(0u, bus.ReadLong(surface));
		bus.WriteLong(surface + 0xFFFF, 0x1122_3344u);
		Assert.Equal(0x1122_3344u, bus.ReadLong(surface + 0xFFFF));
		Assert.Equal(2, bus.RtgVram.CommittedPageCount);
		Assert.Equal(0u, bus.ReadLong(0xFFFF_FFFCu));
	}

	[Fact]
	public void CpuResetDestroysRtgAllocationsBeforeReopeningTheChain()
	{
		var bus = new AmigaBus(rtgVramSize: 256L * 1024 * 1024);
		bus.ConfigureAutoconfigRtgForHost();
		var surface = bus.AllocateRtgVram(64 * 1024);
		bus.WriteLong(surface, 0xCAFE_BABEu);

		bus.ResetExternalDevices(10);

		Assert.False(bus.RtgVram.Active);
		Assert.Equal(0, bus.RtgVram.ReservedBytes);
		Assert.Equal(0u, bus.ReadLong(surface));
		Assert.Same(bus.AutoconfigRtg, bus.Autoconfig.CurrentBoard);
		bus.ConfigureAutoconfigRtgForHost();
		Assert.Equal(surface, bus.AllocateRtgVram(64 * 1024));
		Assert.Equal(0u, bus.ReadLong(surface));
	}

	[Fact]
	public void BareRtgBoardHasNoDiagnosticFirmware()
	{
		var bus = new AmigaBus(rtgVramSize: 16L * 1024 * 1024);
		var board = Assert.IsType<AutoconfigRtgBoard>(bus.AutoconfigRtg);

		Assert.False(board.HasFirmware);
		Assert.Equal(0, board.Identity.DiagnosticRomVector);
		bus.ConfigureAutoconfigRtgForHost();
		Assert.Equal(0, bus.ReadByte(board.ConfiguredBase + 0x4000));
	}

	[Fact]
	public void RtgFirmwareCanOnlyAttachBeforeAutoconfig()
	{
		var bus = new AmigaBus(rtgVramSize: 16L * 1024 * 1024);
		bus.ConfigureAutoconfigRtgForHost();

		Assert.Throws<InvalidOperationException>(() => bus.AttachRtgFirmware(new TestRtgFirmware()));
	}

	[Fact]
	public void ZeroCapacityRtgUsesNoPageMaps()
	{
		var backend = new RtgVramBackend(0);
		var fields = typeof(RtgVramBackend).GetFields(
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

		Assert.All(
			fields.Where(field => field.Name is "_pages" or "_pageOwners" or "_dirtyPages"),
			field => Assert.Empty(Assert.IsAssignableFrom<Array>(field.GetValue(backend))));
	}

	[Fact]
	public void RtgDiagnosticResidentInstallsCyberGraphicsLibraryFromItsCopiedArea()
	{
		var bus = new AmigaBus(rtgVramSize: 256L * 1024 * 1024);
		var library = new CyberGraphicsLibrary(bus);
		var firmware = new CyberGraphicsRtgFirmware(library);
		bus.AttachRtgFirmware(firmware);
		bus.ConfigureAutoconfigRtgForHost();
		var board = Assert.IsType<AutoconfigRtgBoard>(bus.AutoconfigRtg);
		var diagBase = board.ConfiguredBase + (uint)CyberGraphicsRtgFirmware.DiagAreaOffset;
		const uint copyBase = 0x3000;
		for (var offset = 0; offset < CyberGraphicsRtgFirmware.DiagAreaCopySize; offset++)
		{
			bus.WriteByte(copyBase + (uint)offset, bus.ReadByte(diagBase + (uint)offset), 0);
		}

		const uint execBase = 0x1000;
		bus.WriteLong(4, execBase);
		var state = new M68kCpuState();
		state.A[0] = board.ConfiguredBase;
		state.A[2] = copyBase;
		state.A[6] = execBase;
		var diagPoint = copyBase + (uint)CyberGraphicsRtgFirmware.DiagPointOffset;
		Assert.True(bus.TryInvokeHostTrap(diagPoint, bus.ReadWord(diagPoint + 2), state));
		Assert.Equal(1u, state.D[0]);

		var resident = copyBase + (uint)CyberGraphicsRtgFirmware.ResidentOffset;
		var residentInit = bus.ReadLong(resident + 0x16);
		Assert.True(bus.TryInvokeHostTrap(residentInit, bus.ReadWord(residentInit + 2), state));

		var libraryBase = copyBase + (uint)CyberGraphicsRtgFirmware.LibraryBaseOffset;
		Assert.True(firmware.ResidentInstalled);
		Assert.Equal(libraryBase, firmware.LibraryBase);
		Assert.Equal(libraryBase, state.D[0]);
		Assert.True(bus.HasHostTrapStub(unchecked(libraryBase - 54u)));
		Assert.Equal(libraryBase, bus.ReadLong(execBase + 0x17A));
	}

	[Fact]
	public void TwoGiBRtgArenaReachesTheLastLongwordWithOnlyTouchedPagesCommitted()
	{
		var bus = new AmigaBus(rtgVramSize: 2L * 1024 * 1024 * 1024);
		bus.ConfigureAutoconfigRtgForHost();

		Assert.Equal(0x8000_0000u, bus.AllocateRtgVram(2L * 1024 * 1024 * 1024));
		bus.WriteLong(0x8000_0000u, 0x1122_3344u);
		bus.WriteLong(0xFFFF_FFFCu, 0xAABB_CCDDu);

		Assert.Equal(0x1122_3344u, bus.ReadLong(0x8000_0000u));
		Assert.Equal(0xAABB_CCDDu, bus.ReadLong(0xFFFF_FFFCu));
		Assert.Equal(2, bus.RtgVram.CommittedPageCount);
	}

	[Fact]
	public void RtgCodeRunsInInterpreterAndJitAndSelfModificationInvalidatesIt()
	{
		var interpreterBus = CreateConfiguredRtg();
		var jitBus = CreateConfiguredRtg();
		var interpreterCode = interpreterBus.AllocateRtgVram(128 * 1024);
		var jitCode = jitBus.AllocateRtgVram(128 * 1024);
		Assert.Equal(interpreterCode, jitCode);
		WriteLoop(interpreterBus, interpreterCode);
		WriteLoop(jitBus, jitCode);

		var before = jitBus.GetCodePageGeneration(jitCode);
		jitBus.WriteWord(jitCode, 0x7000);
		Assert.NotEqual(before, jitBus.GetCodePageGeneration(jitCode));

		using var interpreter = new M68040Interpreter(
			interpreterBus,
			M68020CpuProfile.Ocs68040JitMaxSpeed);
		using var jit = M68kJitCore.CreateM68040ForTesting(jitBus, enableV2: true);
		jit.FallbackAttributionEnabled = true;
		interpreter.Reset(interpreterCode, interpreterCode + 0x10000);
		jit.Reset(jitCode, jitCode + 0x10000);
		interpreter.State.CacheControlRegister |= 1;
		jit.State.CacheControlRegister |= 1;

		var interpreted = interpreter.ExecuteInstructions(512, null, new TestBoundary());
		var compiled = jit.ExecuteInstructions(512, 1_000_000, new TestBoundary());

		Assert.Equal(interpreted, compiled);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.D[0], jit.State.D[0]);
		Assert.True(
			jit.Counters.TraceHits + jit.Counters.V2TraceHits > 0,
			$"compiled={jit.Counters.CompiledTraces}, trace={jit.Counters.TraceHits}, v2={jit.Counters.V2TraceHits}, fallback={jit.Counters.FallbackInstructions}, blacklist={jit.Counters.BlacklistCount}, reasons={jit.Counters.FallbackReasonTop}");
	}

	[Theory]
	[InlineData((int)M68kBackendKind.AccurateM68000)]
	[InlineData((int)M68kBackendKind.JitM68000)]
	[InlineData((int)M68kBackendKind.AccurateM68EC020)]
	public void LinearRtgRejectsTwentyFourBitCpuProfiles(int backendValue)
	{
		var options = MachineOptions
			.ForProfile(MachineProfile.A500Pal512KChipOnlyBoot)
			.WithRtgVram(256L * 1024 * 1024)
			.WithCpu(AmigaM68kCoreFactory.Default, (M68kBackendKind)backendValue);

		var exception = Assert.Throws<InvalidOperationException>(() => new Machine(options));
		Assert.Contains("full 32-bit", exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void HighAddressCodeRunsInInterpreterAndJitAndTracksWrites()
	{
		const uint code = 0x1000_0000u;
		var interpreterBus = CreateConfiguredZorroIII();
		var jitBus = CreateConfiguredZorroIII();
		WriteLoop(interpreterBus, code);
		WriteLoop(jitBus, code);
		var before = jitBus.GetCodePageGeneration(code);
		jitBus.WriteWord(code, 0x7000);
		Assert.NotEqual(before, jitBus.GetCodePageGeneration(code));

		using var interpreter = new M68040Interpreter(
			interpreterBus,
			M68020CpuProfile.Ocs68040JitMaxSpeed);
		using var jit = M68kJitCore.CreateM68040ForTesting(jitBus, enableV2: true);
		jit.FallbackAttributionEnabled = true;
		interpreter.Reset(code, code + 0x10000);
		jit.Reset(code, code + 0x10000);
		interpreter.State.CacheControlRegister |= 1;
		jit.State.CacheControlRegister |= 1;

		var interpreted = interpreter.ExecuteInstructions(512, null, new TestBoundary());
		var compiled = jit.ExecuteInstructions(512, 1_000_000, new TestBoundary());

		Assert.Equal(interpreted, compiled);
		Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
		Assert.Equal(interpreter.State.D[0], jit.State.D[0]);
		Assert.True(
			jit.Counters.TraceHits + jit.Counters.V2TraceHits > 0,
			$"compiled={jit.Counters.CompiledTraces}, trace={jit.Counters.TraceHits}, v2={jit.Counters.V2TraceHits}, fallback={jit.Counters.FallbackInstructions}, blacklist={jit.Counters.BlacklistCount}, reasons={jit.Counters.FallbackReasonTop}");
	}

	[Theory]
	[InlineData((int)M68kBackendKind.AccurateM68000)]
	[InlineData((int)M68kBackendKind.JitM68000)]
	[InlineData((int)M68kBackendKind.AccurateM68EC020)]
	public void ZorroIIIRamRejectsTwentyFourBitCpuProfiles(int backendValue)
	{
		var backend = (M68kBackendKind)backendValue;
		var options = MachineOptions
			.ForProfile(MachineProfile.A500Pal512KChipOnlyBoot)
			.WithRealFastRam(16 * 1024 * 1024)
			.WithCpu(AmigaM68kCoreFactory.Default, backend);

		var exception = Assert.Throws<InvalidOperationException>(() => new Machine(options));
		Assert.Contains("full 32-bit", exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Theory]
	[InlineData((int)M68kBackendKind.AccurateM68020)]
	[InlineData((int)M68kBackendKind.AccurateM68030)]
	[InlineData((int)M68kBackendKind.AccurateM68040)]
	public void FullThirtyTwoBitInterpretersFetchFromZorroIII(int backendValue)
	{
		const uint code = 0x1000_0000u;
		var bus = CreateConfiguredZorroIII();
		bus.WriteWord(code, 0x702A); // MOVEQ #42,D0
		using var cpu = AmigaM68kCoreFactory.Default.Create((M68kBackendKind)backendValue, bus);
		cpu.Reset(code, code + 0x10000);

		cpu.ExecuteInstruction();

		Assert.Equal(42u, cpu.State.D[0]);
		Assert.Equal(code + 2, cpu.State.ProgramCounter);
	}

	[Fact]
	public void DefaultAndExplicitAssignmentHintsAreValidated()
	{
		Assert.Equal(0x0020_0000u, AutoconfigFastRamBoard.GetDefaultBase(8 * 1024 * 1024));
		Assert.Equal(0x1000_0000u, AutoconfigFastRamBoard.GetDefaultBase(256 * 1024 * 1024));
		Assert.Equal(0x2000_0000u, AutoconfigFastRamBoard.GetDefaultBase(512 * 1024 * 1024));
		Assert.Equal(0x4000_0000u, AutoconfigFastRamBoard.GetDefaultBase(1024 * 1024 * 1024));
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			MachineOptions.ForProfile(MachineProfile.A500Pal512KChipOnlyBoot)
				.WithRealFastRam(16 * 1024 * 1024, 0x1080_0000u));
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			MachineOptions.ForProfile(MachineProfile.A500Pal512KChipOnlyBoot)
				.WithRealFastRam(1024 * 1024 * 1024, 0x8000_0000u));
	}

	[Fact]
	[Trait("Category", "LargeMemory")]
	public void OneGiBBoardFirstAndLastWordsResetCorrectlyWhenEnabled()
	{
		if (!string.Equals(
			Environment.GetEnvironmentVariable("COPPER_AMIGA_RUN_LARGE_MEMORY_TESTS"),
			"1",
			StringComparison.Ordinal))
		{
			return;
		}

		const int size = 1024 * 1024 * 1024;
		const uint baseAddress = 0x4000_0000u;
		var bus = new AmigaBus(realFastRamSize: size, realFastRamBase: baseAddress);
		bus.ConfigureAutoconfigFastRamForHost();
		bus.WriteLong(baseAddress, 0x1122_3344u);
		bus.WriteLong(0x7FFF_FFFCu, 0xAABB_CCDDu);
		Assert.Equal(0x1122_3344u, bus.ReadLong(baseAddress));
		Assert.Equal(0xAABB_CCDDu, bus.ReadLong(0x7FFF_FFFCu));

		bus.ResetExternalDevices(0);
		bus.ConfigureAutoconfigFastRamForHost();
		Assert.Equal(0x1122_3344u, bus.ReadLong(baseAddress));
		Assert.Equal(0xAABB_CCDDu, bus.ReadLong(0x7FFF_FFFCu));

		bus.Reset();
		bus.ConfigureAutoconfigFastRamForHost();
		Assert.Equal(0u, bus.ReadLong(baseAddress));
		Assert.Equal(0u, bus.ReadLong(0x7FFF_FFFCu));
	}

	private static AmigaBus CreateConfiguredZorroIII()
	{
		var bus = new AmigaBus(
			realFastRamSize: 16 * 1024 * 1024,
			realFastRamBase: 0x1000_0000u);
		bus.ConfigureAutoconfigFastRamForHost();
		return bus;
	}

	private static AmigaBus CreateConfiguredRtg()
	{
		var bus = new AmigaBus(rtgVramSize: 256L * 1024 * 1024);
		bus.ConfigureAutoconfigRtgForHost();
		return bus;
	}

	private static void WriteLoop(AmigaBus bus, uint address)
	{
		bus.WriteWord(address, 0x7000);     // MOVEQ #0,D0
		bus.WriteWord(address + 2, 0x5280); // ADDQ.L #1,D0
		bus.WriteWord(address + 4, 0x60FC); // BRA.S ADDQ
	}

	private sealed class TestBoundary : IM68kPureCpuTraceBatchBoundary
	{
		public bool BeforeInstruction() => true;

		public void AfterInstruction(long previousCycle, long currentCycle)
		{
		}

		public bool TryBeginPureCpuTraceBatch(
			M68kCpuState state,
			long targetCycle,
			out long batchTargetCycle)
		{
			batchTargetCycle = targetCycle;
			return true;
		}

		public void AfterPureCpuTraceBatch(long previousCycle, long currentCycle, int instructionCount)
		{
		}
	}

	private sealed class TestRtgFirmware : IAmigaRtgFirmwareProvider
	{
		public AutoconfigIdentity Identity => AutoconfigIdentity.CreateIoBoard(
			AutoconfigRtgBoard.BoardSize,
			AutoconfigRtgBoard.ManufacturerId,
			AutoconfigRtgBoard.ProductId,
			0x4000);

		public void Attach(AmigaBus bus) => _ = bus;
		public byte ReadBoardByte(int offset) => 0;
		public void OnConfigured(uint baseAddress) => _ = baseAddress;
		public void Reset(bool cold) => _ = cold;
	}
}
