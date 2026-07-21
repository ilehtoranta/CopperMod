using System.Diagnostics;
using System.Globalization;

internal static class AmigaBusSaturationBenchmark
{
    private const string ModeArgument = "--amiga-bus-saturation";
    private const int ChipRamSize = 1024 * 1024;
    private const int ExpansionRamSize = 512 * 1024;
    private const int RealFastRamSize = 1024 * 1024;
    private const int DefaultWarmupPasses = 8;
    private const int DefaultMeasuredPasses = 256;
    private const int DefaultRepeats = 3;
    private const int DefaultWritesPerPass = 256;
    private const int CopperWaitMoveCount = 4_096;
    private const uint ChipProgramAddress = 0x0000_1000;
    private const uint ChipCpuTargetAddress = 0x000F_0000;
    private const int ExpansionProgramOffset = 0x0000_8000;
    private const int ExpansionCpuTargetOffset = 0x0002_0000;
    private const int RealFastProgramOffset = 0x0000_8000;
    private const int RealFastCpuTargetOffset = 0x0002_0000;
    private const uint BitplaneBaseAddress = 0x0001_0000;
    private const uint SpriteBaseAddress = 0x0007_2000;
    private const uint CopperListAddress = 0x0008_0000;
    private const uint BlitterSourceA = 0x000C_0000;
    private const uint BlitterSourceB = 0x000C_1000;
    private const uint BlitterSourceC = 0x000C_2000;
    private const uint BlitterDestinationD = 0x000C_3000;
    private const uint AudioBaseAddress = 0x0000_8000;
    private const ushort SaturatedDmaMask = 0x83EF;
    private const ushort BlitterNastyDmaBit = 0x0400;

    public static bool TryRun(string[] args)
    {
        if (!args.Contains(ModeArgument, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        var options = ParseOptions(args);
        var scenarios = CreateScenarios();
        var sizes = new[] { BusWriteSize.Byte, BusWriteSize.Word, BusWriteSize.Long };

        Console.WriteLine(
            $"CopperMod.Amiga bus saturation benchmark, profile=OCS-PAL/A500, " +
            $"warmup-passes={options.WarmupPasses}, measured-passes={options.MeasuredPasses}, " +
            $"writes-per-pass={options.WritesPerPass}, repeats={options.Repeats}, " +
            $"capture-bus={options.CaptureBusAccesses}, scheduler-profile={options.ProfileScheduler}, " +
            $"deferred-cpu={options.DeferredCpuBusBatch}, " +
            $"cpu-modes={string.Join(',', options.CpuModes.Select(FormatCpuMode))}, " +
            $"dma-profiles={string.Join(',', options.DmaProfiles.Select(FormatDmaProfile))}, " +
            $"phase-sweep={options.PhaseSweep}, Release={IsRelease()}");
        Console.WriteLine(
            "scenario\tcpu-mode\tdma-profile\tphase\tcode-memory\twrite-memory\tsize\trepeat\twrites\tinstructions\tms\twrite/sec\tcycles\tcycles/write\t" +
            "allocated-bytes\tchecksum\tdma-saturated\t" +
            "agnus-reservations(total/cpu/blitter/copper/paula/disk/sprite/bitplane/refresh)\t" +
            "agnus-grants(total/cpu/blitter/copper/paula/disk/sprite/bitplane/refresh)\t" +
            "agnus-denied(total/cpu/blitter/copper/paula/disk/sprite/bitplane/refresh)\t" +
            "agnus-denied-blockers(total/cpu/blitter/copper/paula/disk/sprite/bitplane/refresh)\t" +
            "cpu-chip-stall-cycles\tlive-bitplane-fetches\tlive-sprite-fetches\tlive-missed-sprite-slots\t" +
            "paula-dma-words\tblitter-busy\tblitter-completed-microops\tblitter-advance-words\tblitter-advance-calls\t" +
            "blitter-advance-denied-slots\tblitter-advance-fallbacks\tblitter-kernel-hits\tblitter-kernel-misses\t" +
            "scheduler-drains\tscheduler-bus-drains\tscheduler-same-cycle-drains\tscheduler-raster-events\t" +
            "scheduler-cia-events\tscheduler-paula-events\tscheduler-disk-events\tscheduler-agnus-events\t" +
            "scheduler-blitter-events\thost-drain-ticks\thost-wake-ticks\thost-agnus-ticks\t" +
            "captured-accesses\tcpu-captured-accesses\tcpu-fetches\tcpu-data-reads\tcpu-data-writes\tcpu-phase-writes\t" +
            "cpu-bus-byte-writes\tcpu-bus-word-writes\tcpu-bus-long-writes\tcpu-wait-cycles\tcpu-max-wait-cycles\t" +
            "cpu-chip-target-accesses\tcpu-expansion-target-accesses\tcpu-real-fast-target-accesses\tcpu-custom-target-accesses\t" +
            "cpu-chip-write-targets\tcpu-expansion-write-targets\tcpu-real-fast-write-targets\tcapture-may-be-truncated");

        for (var repeat = 1; repeat <= options.Repeats; repeat++)
        {
            foreach (var cpuMode in options.CpuModes)
            {
                foreach (var dmaProfile in options.DmaProfiles)
                {
                    for (var phase = 0; phase < options.PhaseSweep; phase++)
                    {
                        foreach (var scenario in scenarios)
                        {
                            foreach (var size in sizes)
                            {
                                var result = Run(scenario, size, repeat, cpuMode, dmaProfile, phase, options);
                                Console.WriteLine(result.ToTsv());
                            }
                        }
                    }
                }
            }
        }

        return true;
    }

    private static BusSaturationResult Run(
        BusSaturationScenario scenario,
        BusWriteSize size,
        int repeat,
        InterpreterExecutionMode cpuMode,
        DmaProfile dmaProfile,
        int phase,
        BusSaturationOptions options)
    {
        if (options.WarmupPasses > 0)
        {
            using var warmup = CreateRun(scenario, size, cpuMode, dmaProfile, phase, options);
            _ = warmup.ExecutePasses(options.WarmupPasses);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using var run = CreateRun(scenario, size, cpuMode, dmaProfile, phase, options);
        var schedulerBefore = run.Bus.CaptureHardwareSchedulerSnapshot();
        var agnusBefore = run.Bus.Agnus.CaptureSnapshot();
        var blitterBefore = run.Bus.Blitter.CaptureSnapshot();
        var paulaWordsBefore = run.Bus.Paula.PaulaDmaWordExecutionCount;
        var cyclesBefore = run.Cpu.State.Cycles;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        var executedInstructions = run.ExecutePasses(options.MeasuredPasses);
        var elapsed = Stopwatch.GetElapsedTime(start);
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var schedulerAfter = run.Bus.CaptureHardwareSchedulerSnapshot();
        var agnusAfter = run.Bus.Agnus.CaptureSnapshot();
        var blitterAfter = run.Bus.Blitter.CaptureSnapshot();
        var paulaWords = run.Bus.Paula.PaulaDmaWordExecutionCount - paulaWordsBefore;
        var cycleDelta = run.Cpu.State.Cycles - cyclesBefore;
        var expectedWrites = (long)options.MeasuredPasses * options.WritesPerPass;
        var checksum = CreateChecksum(run, scenario, size);
        var agnus = DmaStats.Delta(agnusBefore, agnusAfter);
        var scheduler = SchedulerStats.Delta(schedulerBefore, schedulerAfter);
        var access = CaptureAccessStats(run.Bus, cyclesBefore, run.Cpu.State.Cycles, options, expectedWrites);

        // Full-Nasty is intentionally allowed to expose starvation (and
        // capture mode intentionally changes the exact arbitration path); in
        // both cases the per-owner reservation/grant counters remain the
        // authoritative diagnostic rather than aborting the run.
        if (dmaProfile != DmaProfile.None &&
            !agnus.IsSaturated &&
            !options.CaptureBusAccesses &&
            dmaProfile != DmaProfile.FullNasty)
        {
            throw new InvalidOperationException(
                $"DMA saturation fixture failed for {scenario.Name}/{size}: " +
                $"copper={agnus.CopperGrants}, blitter={agnus.BlitterGrants}, " +
                $"sprite={agnus.SpriteGrants}, bitplane={agnus.BitplaneGrants}, paula={agnus.PaulaGrants}, cycles={cycleDelta}.");
        }

        if (options.CaptureBusAccesses && !access.CaptureMayBeTruncated)
        {
            ValidateCapturedAccesses(scenario, size, access, expectedWrites, run.Cpu.State);
        }

        return new BusSaturationResult(
            scenario,
            size,
            repeat,
            cpuMode,
            dmaProfile,
            phase,
            expectedWrites,
            executedInstructions,
            elapsed,
            cycleDelta,
            allocatedBytes,
            checksum,
            agnus,
            scheduler,
            paulaWords,
            BlitterStats.Delta(blitterBefore, blitterAfter),
            access,
            options.WritesPerPass);
    }

    private static SaturationRun CreateRun(
        BusSaturationScenario scenario,
        BusWriteSize size,
        InterpreterExecutionMode cpuMode,
        DmaProfile dmaProfile,
        int phase,
        BusSaturationOptions options)
    {
        var usesRealFastRam = scenario.CodeMemory == BusMemory.RealFast ||
            scenario.WriteMemory == BusMemory.RealFast;
        var bus = new AmigaBus(
            chipRamSize: ChipRamSize,
            expansionRamSize: ExpansionRamSize,
            captureBusAccesses: options.CaptureBusAccesses,
            enableLiveAgnusDma: dmaProfile != DmaProfile.None,
            enableLiveDisplayDma: dmaProfile != DmaProfile.None,
            audioDmaMinimumPeriod: 1,
            realFastRamSize: usesRealFastRam ? RealFastRamSize : 0,
            enableDeferredCpuBusBatch: options.DeferredCpuBusBatch,
            chipset: AmigaChipset.OcsPal);

        if (usesRealFastRam)
        {
            bus.ConfigureAutoconfigFastRamForHost();
        }

        ConfigureDmaSaturation(bus, dmaProfile);
        var codeAddress = GetProgramAddress(bus, scenario.CodeMemory);
        var targetAddress = GetTargetAddress(bus, scenario.WriteMemory);
        var program = CreateProgram(codeAddress, targetAddress, size, options.WritesPerPass, phase);
        CopyProgram(bus, scenario.CodeMemory, program);

        bus.AdvanceDmaTo(0);
        bus.SetHardwareSchedulerHostProfilingEnabled(options.ProfileScheduler);
        var opcodePlanDispatch = cpuMode == InterpreterExecutionMode.Scalar
            ? M68kOpcodePlanDispatch.Scalar
            : M68kOpcodePlanDispatch.KindTable;
        var cpu = (IM68kBatchCore)M68kCoreFactory.CreateM68000Core(
            bus,
            default(AmigaCpuDataAccess),
            enableInstructionFetchWindow: !options.CaptureBusAccesses,
            enableCpuBusPhaseTrace: options.CaptureBusAccesses,
            opcodePlanDispatch: opcodePlanDispatch);
        cpu.Reset(codeAddress, 0x0000_7000);
        return new SaturationRun(
            bus,
            cpu,
            scenario,
            targetAddress,
            size,
            cpuMode,
            dmaProfile,
            phase,
            options.WritesPerPass,
            checked(options.WritesPerPass + 3 + phase));
    }

    private static uint GetProgramAddress(AmigaBus bus, BusMemory memory)
        => memory switch
        {
            BusMemory.Chip => ChipProgramAddress,
            BusMemory.Expansion => bus.ExpansionRamBase + ExpansionProgramOffset,
            BusMemory.RealFast => bus.RealFastRamBase + RealFastProgramOffset,
            _ => throw new ArgumentOutOfRangeException(nameof(memory), memory, null)
        };

    private static uint GetTargetAddress(AmigaBus bus, BusMemory memory)
        => memory switch
        {
            BusMemory.Chip => ChipCpuTargetAddress,
            BusMemory.Expansion => bus.ExpansionRamBase + ExpansionCpuTargetOffset,
            BusMemory.RealFast => bus.RealFastRamBase + RealFastCpuTargetOffset,
            _ => throw new ArgumentOutOfRangeException(nameof(memory), memory, null)
        };

    private static void CopyProgram(AmigaBus bus, BusMemory memory, byte[] program)
    {
        switch (memory)
        {
            case BusMemory.Chip:
                program.CopyTo(bus.ChipRam.AsSpan((int)ChipProgramAddress));
                break;
            case BusMemory.Expansion:
                program.CopyTo(bus.ExpansionRam.AsSpan(ExpansionProgramOffset));
                break;
            case BusMemory.RealFast:
                program.CopyTo(bus.RealFastRam.AsSpan(RealFastProgramOffset));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(memory), memory, null);
        }
    }

    private static void ConfigureDmaSaturation(AmigaBus bus, DmaProfile profile)
    {
        if (profile == DmaProfile.None)
        {
            return;
        }

        bus.ChipRam.AsSpan().Fill(0);
        bus.ExpansionRam.AsSpan().Fill(0);
        bus.ChipRam.AsSpan((int)BlitterSourceA, 0x20_000).Fill(0xFF);

        // Full-width, six-plane display DMA.
        // Start the visible window at the standard PAL overscan origin so the
        // short benchmark passes still exercise bitplane and sprite DMA.
        bus.WriteWord(0x00DFF08E, 0x0081);
        bus.WriteWord(0x00DFF090, 0xF4C1);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x00D0);
        bus.WriteWord(0x00DFF102, 0x0000);
        bus.WriteWord(0x00DFF104, 0x0000);
        for (var plane = 0; plane < 6; plane++)
        {
            WriteCustomPointer(bus, 0x00DFF0E0u + (uint)(plane * 4), BitplaneBaseAddress + (uint)(plane * 0x10_000));
        }

        bus.WriteWord(0x00DFF100, 0x1000);

        // Eight visible sprites with long data streams.
        for (var sprite = 6; sprite < 8; sprite++)
        {
            var address = SpriteBaseAddress + (uint)(sprite * 0x1000);
            WriteSpriteDmaBlock(
                bus,
                address,
                AmigaConstants.PalLowResOverscanBorderX + 16 + sprite * 32,
                -26,
                200);
            WriteCustomPointer(bus, 0x00DFF120u + (uint)(sprite * 4), address);
        }

        // Four Paula channels, minimum period, one-word looping buffers.
        for (var channel = 0; channel < 4; channel++)
        {
            var address = AudioBaseAddress + (uint)(channel * 0x100);
            WriteCustomPointer(bus, 0x00DFF0A0u + (uint)(channel * 0x10), address);
            bus.WriteWord(0x00DFF0A4u + (uint)(channel * 0x10), 1);
            bus.WriteWord(0x00DFF0A6u + (uint)(channel * 0x10), 1);
            bus.WriteWord(0x00DFF0A8u + (uint)(channel * 0x10), 64);
        }

        // A large, four-channel area blit keeps the blitter busy beyond the measured window.
        bus.WriteWord(0x00DFF040, 0x09F0);
        bus.WriteWord(0x00DFF042, 0x0000);
        WriteCustomPointer(bus, 0x00DFF050, BlitterSourceA);
        WriteCustomPointer(bus, 0x00DFF04C, BlitterSourceB);
        WriteCustomPointer(bus, 0x00DFF048, BlitterSourceC);
        WriteCustomPointer(bus, 0x00DFF054, BlitterDestinationD);

        // Copper MOVE stream; its list is in Chip RAM and runs from frame start.
        for (var index = 0; index < CopperWaitMoveCount; index++)
        {
            var offset = (int)(CopperListAddress + (uint)(index * 8));
            WriteWord(bus.ChipRam, offset, 0x0180);
            WriteWord(bus.ChipRam, offset + 2, (ushort)(index & 0x0FFF));
            WriteWord(bus.ChipRam, offset + 4, (ushort)(((index % AmigaConstants.A500PalRasterLines) << 8) | 0x0021));
            WriteWord(bus.ChipRam, offset + 6, 0xFFFE);
        }

        var copperEnd = CopperListAddress + (uint)(CopperWaitMoveCount * 8);
        WriteWord(bus.ChipRam, (int)copperEnd, 0xFFFF);
        WriteWord(bus.ChipRam, (int)copperEnd + 2, 0xFFFE);
        WriteCustomPointer(bus, 0x00DFF080, CopperListAddress);

        // Master + bitplanes + copper + blitter + sprites + all four audio channels + BLTNASTY.
        var dmaMask = profile == DmaProfile.FullNasty
            ? (ushort)(SaturatedDmaMask | BlitterNastyDmaBit)
            : SaturatedDmaMask;
        bus.WriteWord(0x00DFF096, dmaMask);
        bus.WriteWord(0x00DFF058, 0xFFFF);
        bus.EnableLiveAgnusDma();
    }

    private static void WriteSpriteDmaBlock(AmigaBus bus, uint address, int x, int y, int height)
    {
        var hStart = x + 129 - AmigaConstants.PalLowResOverscanBorderX;
        var vStart = y + (0x2C - AmigaConstants.PalLowResOverscanBorderY);
        var vStop = vStart + height;
        var pos = (ushort)(((vStart & 0xFF) << 8) | ((hStart >> 1) & 0xFF));
        var ctl = (ushort)(((vStop & 0xFF) << 8) |
            (hStart & 0x0001) |
            ((vStop & 0x100) != 0 ? 0x0002 : 0) |
            ((vStart & 0x100) != 0 ? 0x0004 : 0));
        WriteWord(bus.ChipRam, (int)address, pos);
        WriteWord(bus.ChipRam, (int)address + 2, ctl);
        for (var line = 0; line < height; line++)
        {
            WriteWord(bus.ChipRam, (int)address + 4 + line * 4, 0xFFFF);
            WriteWord(bus.ChipRam, (int)address + 6 + line * 4, 0xFFFF);
        }

        WriteWord(bus.ChipRam, (int)address + 4 + height * 4, 0);
        WriteWord(bus.ChipRam, (int)address + 6 + height * 4, 0);
    }

    private static void WriteCustomPointer(AmigaBus bus, uint register, uint address)
    {
        bus.WriteWord(register, (ushort)(address >> 16));
        bus.WriteWord(register + 2, (ushort)address);
    }

    private static byte[] CreateProgram(
        uint codeAddress,
        uint targetAddress,
        BusWriteSize size,
        int writesPerPass,
        int phase)
    {
        var writeOpcode = size switch
        {
            BusWriteSize.Byte => (ushort)0x10C0,
            BusWriteSize.Word => (ushort)0x30C0,
            BusWriteSize.Long => (ushort)0x20C0,
            _ => throw new ArgumentOutOfRangeException(nameof(size), size, null)
        };
        // Keep the transfer stream deliberately simple and deterministic.  An
        // unrolled stream makes every requested write independently visible to
        // both the scalar interpreter and the batched interpreter, including
        // the two word transfers used by a Chip RAM longword write.
        var words = new ushort[phase + writesPerPass + 7];
        var index = 0;
        for (; index < phase; index++)
        {
            words[index] = 0x4E71;                                         // NOP; phase sweep prelude
        }

        words[index++] = 0x207C;
        words[index++] = (ushort)(targetAddress >> 16);
        words[index++] = (ushort)targetAddress;                            // MOVEA.L #target,A0
        words[index++] = 0x7000;                                           // MOVEQ #0,D0
        for (var write = 0; write < writesPerPass; write++)
        {
            words[index++] = writeOpcode;                                  // MOVE.<size> D0,(A0)+
        }
        words[index++] = 0x4EF9;
        words[index++] = (ushort)(codeAddress >> 16);
        words[index] = (ushort)codeAddress;                                // JMP start
        var program = new byte[words.Length * 2];
        for (index = 0; index < words.Length; index++)
        {
            WriteWord(program, index * 2, words[index]);
        }

        return program;
    }

    private static AccessStats CaptureAccessStats(
        AmigaBus bus,
        long firstCycle,
        long lastCycle,
        BusSaturationOptions options,
        long expectedWrites)
    {
        if (!options.CaptureBusAccesses)
        {
            return AccessStats.Disabled;
        }

        var captured = bus.BusAccesses;
        var cpuPhaseWrites = 0L;
        var cpuAccesses = 0L;
        var cpuFetches = 0L;
        var cpuReads = 0L;
        var cpuWrites = 0L;
        var byteWrites = 0L;
        var wordWrites = 0L;
        var longWrites = 0L;
        var waitCycles = 0L;
        var maxWaitCycles = 0L;
        var chipTargets = 0L;
        var expansionTargets = 0L;
        var realFastTargets = 0L;
        var customTargets = 0L;
        var chipWriteTargets = 0L;
        var expansionWriteTargets = 0L;
        var realFastWriteTargets = 0L;
        foreach (var access in captured)
        {
            // The run owns a fresh bus-access buffer.  Do not reject a valid
            // trailing transfer merely because the CPU architectural cycle
            // cursor retired before the bus completion cursor was observed.
            // That distinction is especially visible for prefetched fetches
            // and long writes under live DMA.
            if (access.Request.Requester != AmigaBusRequester.Cpu)
            {
                continue;
            }

            cpuAccesses++;
            waitCycles += access.WaitCycles;
            maxWaitCycles = Math.Max(maxWaitCycles, access.WaitCycles);
            switch (access.Request.Kind)
            {
                case AmigaBusAccessKind.CpuInstructionFetch:
                    cpuFetches++;
                    break;
                case AmigaBusAccessKind.CpuDataRead:
                    cpuReads++;
                    break;
                case AmigaBusAccessKind.CpuDataWrite:
                    cpuWrites++;
                    switch (access.Request.Size)
                    {
                        case AmigaBusAccessSize.Byte:
                            byteWrites++;
                            break;
                        case AmigaBusAccessSize.Word:
                            wordWrites++;
                            break;
                        case AmigaBusAccessSize.Long:
                            longWrites++;
                            break;
                    }

                    switch (access.Request.Target)
                    {
                        case AmigaBusAccessTarget.ChipRam:
                            chipWriteTargets++;
                            break;
                        case AmigaBusAccessTarget.ExpansionRam:
                            expansionWriteTargets++;
                            break;
                        case AmigaBusAccessTarget.RealFastRam:
                            realFastWriteTargets++;
                            break;
                    }

                    break;
            }

            switch (access.Request.Target)
            {
                case AmigaBusAccessTarget.ChipRam:
                    chipTargets++;
                    break;
                case AmigaBusAccessTarget.ExpansionRam:
                    expansionTargets++;
                    break;
                case AmigaBusAccessTarget.RealFastRam:
                    realFastTargets++;
                    break;
                case AmigaBusAccessTarget.CustomRegisters:
                    customTargets++;
                    break;
            }
        }

        foreach (var phase in bus.CpuBusPhases)
        {
            if (phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuDataWrite)
            {
                cpuPhaseWrites++;
            }
        }

        return new AccessStats(
            captured.Count,
            cpuAccesses,
            cpuFetches,
            cpuReads,
            cpuWrites,
            cpuPhaseWrites,
            byteWrites,
            wordWrites,
            longWrites,
            waitCycles,
            maxWaitCycles,
            chipTargets,
            expansionTargets,
            realFastTargets,
            customTargets,
            chipWriteTargets,
            expansionWriteTargets,
            realFastWriteTargets,
            captured.Count == 65_536,
            expectedWrites);
    }

    private static void ValidateCapturedAccesses(
        BusSaturationScenario scenario,
        BusWriteSize size,
        AccessStats access,
        long expectedWrites,
        M68kCpuState state)
    {
        if (access.CpuPhaseWrites != expectedWrites)
        {
            throw new InvalidOperationException(
                $"Captured CPU write-phase count mismatch for {scenario.Name}/{size}: " +
                $"expected={expectedWrites}, actual={access.CpuPhaseWrites}, " +
                $"bus-writes={access.CpuWrites}, " +
                $"phase-writes={access.CpuPhaseWrites}, " +
                $"captured={access.CapturedAccesses}, cpu-accesses={access.CpuAccesses}, " +
                $"fetches={access.CpuFetches}, reads={access.CpuReads}, " +
                $"pc=0x{state.ProgramCounter:X8}, d0=0x{state.D[0]:X8}, d1=0x{state.D[1]:X8}.");
        }

        var expectedBusWrites = size == BusWriteSize.Long && scenario.WriteMemory == BusMemory.Chip
            ? checked(expectedWrites * 2)
            : expectedWrites;
        if (access.CpuWrites != expectedBusWrites)
        {
            throw new InvalidOperationException(
                $"Captured CPU bus-transfer count mismatch for {scenario.Name}/{size}: " +
                $"expected={expectedBusWrites}, actual={access.CpuWrites}, " +
                $"phase-writes={access.CpuPhaseWrites}.");
        }

        var expectedByteWrites = size == BusWriteSize.Byte ? expectedWrites : 0;
        var expectedWordWrites = size == BusWriteSize.Word ||
            (size == BusWriteSize.Long && scenario.WriteMemory == BusMemory.Chip)
            ? size == BusWriteSize.Word ? expectedWrites : checked(expectedWrites * 2)
            : 0;
        var expectedLongWrites = size == BusWriteSize.Long && scenario.WriteMemory != BusMemory.Chip
            ? expectedWrites
            : 0;
        if (access.ByteWrites != expectedByteWrites ||
            access.WordWrites != expectedWordWrites ||
            access.LongWrites != expectedLongWrites)
        {
            throw new InvalidOperationException(
                $"Captured CPU write-size mismatch for {scenario.Name}/{size}: " +
                $"byte={access.ByteWrites}, word={access.WordWrites}, long={access.LongWrites}.");
        }

        var chipWrites = scenario.WriteMemory == BusMemory.Chip
            ? expectedBusWrites
            : 0;
        var expansionWrites = scenario.WriteMemory == BusMemory.Expansion
            ? expectedBusWrites
            : 0;
        var realFastWrites = scenario.WriteMemory == BusMemory.RealFast
            ? expectedBusWrites
            : 0;
        if (access.ChipWriteTargets != chipWrites ||
            access.ExpansionWriteTargets != expansionWrites ||
            access.RealFastWriteTargets != realFastWrites)
        {
            throw new InvalidOperationException(
                $"Captured CPU write-target mismatch for {scenario.Name}/{size}: " +
                $"chip={access.ChipWriteTargets}, expansion={access.ExpansionWriteTargets}, " +
                $"real-fast={access.RealFastWriteTargets}.");
        }
    }

    private static uint CreateChecksum(SaturationRun run, BusSaturationScenario scenario, BusWriteSize size)
    {
        var target = run.TargetAddress;
        var byteCount = Math.Min(32, run.WritesPerPass * (int)size);
        var data = scenario.WriteMemory switch
        {
            BusMemory.Chip => run.Bus.ChipRam.AsSpan((int)target, byteCount),
            BusMemory.Expansion => run.Bus.ExpansionRam.AsSpan((int)(target - run.Bus.ExpansionRamBase), byteCount),
            BusMemory.RealFast => run.Bus.RealFastRam.AsSpan((int)(target - run.Bus.RealFastRamBase), byteCount),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
        };
        var checksum = 2166136261u;
        foreach (var value in data)
        {
            checksum = unchecked((checksum ^ value) * 16777619u);
        }

        return unchecked((checksum ^ run.Cpu.State.D[0]) * 16777619u ^ run.Cpu.State.ProgramCounter);
    }

    private static BusSaturationScenario[] CreateScenarios()
        =>
        [
            new("slow-fast-code-slow-fast-write", BusMemory.Expansion, BusMemory.Expansion),
            new("chip-code-slow-fast-write", BusMemory.Chip, BusMemory.Expansion),
            new("chip-code-chip-write", BusMemory.Chip, BusMemory.Chip),
            new("real-fast-code-real-fast-write", BusMemory.RealFast, BusMemory.RealFast),
            new("chip-code-real-fast-write", BusMemory.Chip, BusMemory.RealFast)
        ];

    private static BusSaturationOptions ParseOptions(string[] args)
    {
        var warmup = DefaultWarmupPasses;
        var measured = DefaultMeasuredPasses;
        var repeats = DefaultRepeats;
        var writes = DefaultWritesPerPass;
        var capture = false;
        var profile = false;
        var deferredCpu = false;
        var cpuModes = new[] { InterpreterExecutionMode.Scalar, InterpreterExecutionMode.Batched };
        var dmaProfiles = new[] { DmaProfile.Full };
        var phaseSweep = 1;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case ModeArgument:
                    break;
                case "--bus-warmup-passes":
                    warmup = ParseInt(args, ref index);
                    break;
                case "--bus-passes":
                    measured = ParseInt(args, ref index);
                    break;
                case "--bus-repeats":
                    repeats = ParseInt(args, ref index);
                    break;
                case "--bus-writes-per-pass":
                    writes = ParseInt(args, ref index);
                    break;
                case "--bus-capture":
                    capture = true;
                    break;
                case "--bus-profile-scheduler":
                    profile = true;
                    break;
                case "--bus-deferred-cpu":
                    deferredCpu = true;
                    break;
                case "--bus-cpu-mode":
                    cpuModes = ParseCpuModes(args, ref index);
                    break;
                case "--bus-dma-profile":
                    dmaProfiles = ParseDmaProfiles(args, ref index);
                    break;
                case "--bus-phase-sweep":
                    phaseSweep = ParseInt(args, ref index);
                    break;
                case "--bus-help":
                    PrintHelp();
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown bus saturation option '{args[index]}'.");
            }
        }

        if (warmup < 0 || measured <= 0 || repeats <= 0 || writes <= 0 || writes > 65_536 ||
            phaseSweep <= 0 || phaseSweep > 64 || cpuModes.Length == 0 || dmaProfiles.Length == 0)
        {
            throw new ArgumentOutOfRangeException(
                "options",
                "Bus saturation warmup/passes/repeats must be non-negative/positive, writes-per-pass must be 1..65536, phase sweep must be 1..64, and CPU/DMA selections must not be empty.");
        }

        var instructionsPerPass = checked((long)writes + 3 + phaseSweep - 1);
        if (instructionsPerPass * measured > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException("options", "The requested benchmark exceeds the CPU instruction budget.");
        }

        return new BusSaturationOptions(
            warmup,
            measured,
            repeats,
            writes,
            capture,
            profile,
            deferredCpu,
            cpuModes,
            dmaProfiles,
            phaseSweep);
    }

    private static InterpreterExecutionMode[] ParseCpuModes(string[] args, ref int index)
    {
        var value = ParseString(args, ref index);
        if (value.Equals("both", StringComparison.OrdinalIgnoreCase))
        {
            return [InterpreterExecutionMode.Scalar, InterpreterExecutionMode.Batched];
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseCpuMode)
            .Distinct()
            .ToArray();
    }

    private static DmaProfile[] ParseDmaProfiles(string[] args, ref int index)
    {
        var value = ParseString(args, ref index);
        if (value.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return [DmaProfile.None, DmaProfile.Full, DmaProfile.FullNasty];
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseDmaProfile)
            .Distinct()
            .ToArray();
    }

    private static InterpreterExecutionMode ParseCpuMode(string value)
        => value.ToLowerInvariant() switch
        {
            "scalar" => InterpreterExecutionMode.Scalar,
            "batched" or "batch" => InterpreterExecutionMode.Batched,
            _ => throw new ArgumentException($"Unknown bus CPU mode '{value}'.")
        };

    private static DmaProfile ParseDmaProfile(string value)
        => value.ToLowerInvariant() switch
        {
            "none" or "off" => DmaProfile.None,
            "full" => DmaProfile.Full,
            "full-nasty" or "nasty" => DmaProfile.FullNasty,
            _ => throw new ArgumentException($"Unknown bus DMA profile '{value}'.")
        };

    private static string ParseString(string[] args, ref int index)
    {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new ArgumentException($"Expected a value after '{args[index - 1]}'.");
        }

        return args[index];
    }

    private static int ParseInt(string[] args, ref int index)
    {
        if (++index >= args.Length || !int.TryParse(args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new ArgumentException($"Expected an integer after '{args[index - 1]}'.");
        }

        return value;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            "--amiga-bus-saturation [--bus-warmup-passes N] [--bus-passes N] [--bus-repeats N] " +
            "[--bus-writes-per-pass N] [--bus-capture] [--bus-profile-scheduler] [--bus-deferred-cpu] " +
            "[--bus-cpu-mode scalar|batched|both] [--bus-dma-profile none|full|full-nasty|all] [--bus-phase-sweep N]");
    }

    private static string FormatCpuMode(InterpreterExecutionMode mode)
        => mode == InterpreterExecutionMode.Scalar ? "scalar" : "batched";

    private static string FormatDmaProfile(DmaProfile profile)
        => profile switch
        {
            DmaProfile.None => "none",
            DmaProfile.Full => "full",
            DmaProfile.FullNasty => "full-nasty",
            _ => profile.ToString().ToLowerInvariant()
        };

    private static bool IsRelease()
    {
#if DEBUG
        return false;
#else
        return true;
#endif
    }

    private static void WriteWord(byte[] target, int offset, ushort value)
    {
        target[offset] = (byte)(value >> 8);
        target[offset + 1] = (byte)value;
    }

    private static void WriteWord(Span<byte> target, int offset, ushort value)
    {
        target[offset] = (byte)(value >> 8);
        target[offset + 1] = (byte)value;
    }

    private sealed class SaturationRun : IDisposable
    {
        public SaturationRun(
            AmigaBus bus,
            IM68kBatchCore cpu,
            BusSaturationScenario scenario,
            uint targetAddress,
            BusWriteSize size,
            InterpreterExecutionMode cpuMode,
            DmaProfile dmaProfile,
            int phase,
            int writesPerPass,
            int instructionsPerPass)
        {
            Bus = bus;
            Cpu = cpu;
            Scenario = scenario;
            TargetAddress = targetAddress;
            Size = size;
            CpuMode = cpuMode;
            DmaProfile = dmaProfile;
            Phase = phase;
            WritesPerPass = writesPerPass;
            InstructionsPerPass = instructionsPerPass;
        }

        public AmigaBus Bus { get; }

        public IM68kBatchCore Cpu { get; }

        public BusSaturationScenario Scenario { get; }

        public uint TargetAddress { get; }

        public BusWriteSize Size { get; }

        public InterpreterExecutionMode CpuMode { get; }

        public DmaProfile DmaProfile { get; }

        public int Phase { get; }

        public int WritesPerPass { get; }

        public int InstructionsPerPass { get; }

        public int ExecutePasses(int passes)
        {
            var remaining = checked(passes * InstructionsPerPass);
            var executedTotal = 0;
            if (CpuMode == InterpreterExecutionMode.Scalar)
            {
                while (remaining-- > 0)
                {
                    _ = Cpu.ExecuteInstruction();
                    AdvanceLiveDmaToCpuCycle();
                    executedTotal++;
                }

                return executedTotal;
            }

            while (remaining > 0)
            {
                var executed = Cpu.ExecuteInstructions(remaining, null, SyntheticBenchmarkBoundary.Instance);
                if (executed <= 0)
                {
                    throw new InvalidOperationException("MC68000 stopped before the bus saturation budget was consumed.");
                }

                remaining -= executed;
                executedTotal += executed;
                AdvanceLiveDmaToCpuCycle();
            }

            return executedTotal;
        }

        private void AdvanceLiveDmaToCpuCycle()
        {
            if (DmaProfile != DmaProfile.None)
            {
                Bus.AdvanceDmaTo(Cpu.State.Cycles);
            }
        }

        public void Dispose()
            => Cpu.Dispose();
    }

    private enum BusMemory
    {
        Chip,
        Expansion,
        RealFast
    }

    private enum InterpreterExecutionMode
    {
        Scalar,
        Batched
    }

    private enum DmaProfile
    {
        None,
        Full,
        FullNasty
    }

    private enum BusWriteSize
    {
        Byte = 1,
        Word = 2,
        Long = 4
    }

    private readonly record struct BusSaturationScenario(string Name, BusMemory CodeMemory, BusMemory WriteMemory);

    private readonly record struct BusSaturationOptions(
        int WarmupPasses,
        int MeasuredPasses,
        int Repeats,
        int WritesPerPass,
        bool CaptureBusAccesses,
        bool ProfileScheduler,
        bool DeferredCpuBusBatch,
        InterpreterExecutionMode[] CpuModes,
        DmaProfile[] DmaProfiles,
        int PhaseSweep);

    private readonly record struct AccessStats(
        long CapturedAccesses,
        long CpuAccesses,
        long CpuFetches,
        long CpuReads,
        long CpuWrites,
        long CpuPhaseWrites,
        long ByteWrites,
        long WordWrites,
        long LongWrites,
        long WaitCycles,
        long MaxWaitCycles,
        long ChipTargets,
        long ExpansionTargets,
        long RealFastTargets,
        long CustomTargets,
        long ChipWriteTargets,
        long ExpansionWriteTargets,
        long RealFastWriteTargets,
        bool CaptureMayBeTruncated,
        long ExpectedWrites)
    {
        public static AccessStats Disabled => new(
            -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, false, -1);
    }

    private readonly record struct DmaStats(
        long Reservations,
        long CpuReservations,
        long BlitterReservations,
        long CopperReservations,
        long PaulaReservations,
        long DiskReservations,
        long SpriteReservations,
        long BitplaneReservations,
        long RefreshReservations,
        long Grants,
        long CpuGrants,
        long BlitterGrants,
        long CopperGrants,
        long PaulaGrants,
        long DiskGrants,
        long SpriteGrants,
        long BitplaneGrants,
        long RefreshGrants,
        long Denied,
        long CpuDenied,
        long BlitterDenied,
        long CopperDenied,
        long PaulaDenied,
        long DiskDenied,
        long SpriteDenied,
        long BitplaneDenied,
        long RefreshDenied,
        long DeniedBlockers,
        long CpuDeniedBlockers,
        long BlitterDeniedBlockers,
        long CopperDeniedBlockers,
        long PaulaDeniedBlockers,
        long DiskDeniedBlockers,
        long SpriteDeniedBlockers,
        long BitplaneDeniedBlockers,
        long RefreshDeniedBlockers,
        long CpuChipStallCycles,
        long LiveBitplaneFetches,
        long LiveSpriteFetches,
        long LiveMissedSpriteSlots)
    {
        // Saturation means every requested DMA engine is active.  A nasty
        // blitter or a heavily contended phase may legitimately deny another
        // engine's slot; the reservation counters are the authoritative
        // evidence that the engine was still participating in arbitration.
        public bool IsSaturated => CopperReservations > 0 &&
            BlitterReservations > 0 &&
            SpriteReservations > 0 &&
            BitplaneReservations > 0 &&
            PaulaReservations > 0;

        public static DmaStats Delta(AgnusBeamDmaSnapshot before, AgnusBeamDmaSnapshot after)
            => new(
                after.SlotReservationCount - before.SlotReservationCount,
                after.CpuSlotReservationCount - before.CpuSlotReservationCount,
                after.BlitterSlotReservationCount - before.BlitterSlotReservationCount,
                after.CopperSlotReservationCount - before.CopperSlotReservationCount,
                after.PaulaSlotReservationCount - before.PaulaSlotReservationCount,
                after.DiskSlotReservationCount - before.DiskSlotReservationCount,
                after.SpriteSlotReservationCount - before.SpriteSlotReservationCount,
                after.BitplaneSlotReservationCount - before.BitplaneSlotReservationCount,
                after.RefreshSlotReservationCount - before.RefreshSlotReservationCount,
                after.SlotGrantCount - before.SlotGrantCount,
                after.CpuSlotGrantCount - before.CpuSlotGrantCount,
                after.BlitterSlotGrantCount - before.BlitterSlotGrantCount,
                after.CopperSlotGrantCount - before.CopperSlotGrantCount,
                after.PaulaSlotGrantCount - before.PaulaSlotGrantCount,
                after.DiskSlotGrantCount - before.DiskSlotGrantCount,
                after.SpriteSlotGrantCount - before.SpriteSlotGrantCount,
                after.BitplaneSlotGrantCount - before.BitplaneSlotGrantCount,
                after.RefreshSlotGrantCount - before.RefreshSlotGrantCount,
                after.DeniedFixedSlotCount - before.DeniedFixedSlotCount,
                after.CpuDeniedFixedSlotCount - before.CpuDeniedFixedSlotCount,
                after.BlitterDeniedFixedSlotCount - before.BlitterDeniedFixedSlotCount,
                after.CopperDeniedFixedSlotCount - before.CopperDeniedFixedSlotCount,
                after.PaulaDeniedFixedSlotCount - before.PaulaDeniedFixedSlotCount,
                after.DiskDeniedFixedSlotCount - before.DiskDeniedFixedSlotCount,
                after.SpriteDeniedFixedSlotCount - before.SpriteDeniedFixedSlotCount,
                after.BitplaneDeniedFixedSlotCount - before.BitplaneDeniedFixedSlotCount,
                after.RefreshDeniedFixedSlotCount - before.RefreshDeniedFixedSlotCount,
                after.CpuDeniedFixedSlotBlockerCount - before.CpuDeniedFixedSlotBlockerCount +
                    after.BlitterDeniedFixedSlotBlockerCount - before.BlitterDeniedFixedSlotBlockerCount +
                    after.CopperDeniedFixedSlotBlockerCount - before.CopperDeniedFixedSlotBlockerCount +
                    after.PaulaDeniedFixedSlotBlockerCount - before.PaulaDeniedFixedSlotBlockerCount +
                    after.DiskDeniedFixedSlotBlockerCount - before.DiskDeniedFixedSlotBlockerCount +
                    after.SpriteDeniedFixedSlotBlockerCount - before.SpriteDeniedFixedSlotBlockerCount +
                    after.BitplaneDeniedFixedSlotBlockerCount - before.BitplaneDeniedFixedSlotBlockerCount +
                    after.RefreshDeniedFixedSlotBlockerCount - before.RefreshDeniedFixedSlotBlockerCount,
                after.CpuDeniedFixedSlotBlockerCount - before.CpuDeniedFixedSlotBlockerCount,
                after.BlitterDeniedFixedSlotBlockerCount - before.BlitterDeniedFixedSlotBlockerCount,
                after.CopperDeniedFixedSlotBlockerCount - before.CopperDeniedFixedSlotBlockerCount,
                after.PaulaDeniedFixedSlotBlockerCount - before.PaulaDeniedFixedSlotBlockerCount,
                after.DiskDeniedFixedSlotBlockerCount - before.DiskDeniedFixedSlotBlockerCount,
                after.SpriteDeniedFixedSlotBlockerCount - before.SpriteDeniedFixedSlotBlockerCount,
                after.BitplaneDeniedFixedSlotBlockerCount - before.BitplaneDeniedFixedSlotBlockerCount,
                after.RefreshDeniedFixedSlotBlockerCount - before.RefreshDeniedFixedSlotBlockerCount,
                after.CpuChipStallCycles - before.CpuChipStallCycles,
                after.LiveBitplaneFetches,
                after.LiveSpriteFetches,
                after.LiveMissedSpriteSlots);
    }

    private readonly record struct SchedulerStats(
        long Drains,
        long BusDrains,
        long SameCycleDrains,
        long RasterEvents,
        long CiaEvents,
        long PaulaEvents,
        long DiskEvents,
        long AgnusEvents,
        long BlitterEvents,
        long HostDrainTicks,
        long HostWakeTicks,
        long HostAgnusTicks)
    {
        public static SchedulerStats Delta(AmigaHardwareSchedulerSnapshot before, AmigaHardwareSchedulerSnapshot after)
            => new(
                after.DrainCount - before.DrainCount,
                after.BusAccessDrainCount - before.BusAccessDrainCount,
                after.SameCycleDrainCount - before.SameCycleDrainCount,
                after.RasterEvents - before.RasterEvents,
                after.CiaEvents - before.CiaEvents,
                after.PaulaEvents - before.PaulaEvents,
                after.DiskEvents - before.DiskEvents,
                after.AgnusEvents - before.AgnusEvents,
                after.BlitterEvents - before.BlitterEvents,
                after.HostDrainTicks - before.HostDrainTicks,
                after.HostWakeQueryTicks - before.HostWakeQueryTicks,
                after.HostAgnusTicks - before.HostAgnusTicks);
    }

    private readonly record struct BlitterStats(
        bool Busy,
        long CompletedMicroOps,
        long AdvanceWords,
        long AdvanceCalls,
        long AdvanceDeniedSlots,
        long AdvanceFallbacks,
        long KernelHits,
        long KernelMisses)
    {
        public static BlitterStats Delta(AmigaBlitterSnapshot before, AmigaBlitterSnapshot after)
            => new(
                after.Busy,
                after.CompletedMicroOps - before.CompletedMicroOps,
                after.AdvanceCounters.WordsCompleted - before.AdvanceCounters.WordsCompleted,
                after.AdvanceCounters.Calls - before.AdvanceCounters.Calls,
                after.AdvanceCounters.DeniedSlots - before.AdvanceCounters.DeniedSlots,
                after.AdvanceCounters.Fallbacks - before.AdvanceCounters.Fallbacks,
                after.SpecializationCounters.KernelHits - before.SpecializationCounters.KernelHits,
                after.SpecializationCounters.KernelMisses - before.SpecializationCounters.KernelMisses);
    }

    private readonly record struct BusSaturationResult(
        BusSaturationScenario Scenario,
        BusWriteSize Size,
        int Repeat,
        InterpreterExecutionMode CpuMode,
        DmaProfile DmaProfile,
        int Phase,
        long Writes,
        int Instructions,
        TimeSpan Elapsed,
        long Cycles,
        long AllocatedBytes,
        uint Checksum,
        DmaStats Dma,
        SchedulerStats Scheduler,
        long PaulaDmaWords,
        BlitterStats Blitter,
        AccessStats Access,
        int WritesPerPass)
    {
        public string ToTsv()
        {
            var writePerSecond = Writes / Elapsed.TotalSeconds;
            var cyclePerWrite = (double)Cycles / Writes;
            return string.Join(
                '\t',
                Scenario.Name,
                FormatCpuMode(CpuMode),
                FormatDmaProfile(DmaProfile),
                Phase,
                Scenario.CodeMemory.ToString().ToLowerInvariant(),
                Scenario.WriteMemory.ToString().ToLowerInvariant(),
                Size.ToString().ToLowerInvariant(),
                Repeat,
                Writes,
                Instructions,
                Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
                writePerSecond.ToString("F0", CultureInfo.InvariantCulture),
                Cycles,
                cyclePerWrite.ToString("F3", CultureInfo.InvariantCulture),
                AllocatedBytes,
                $"0x{Checksum:X8}",
                Dma.IsSaturated ? 1 : 0,
                FormatCounts(Dma.Reservations, Dma.CpuReservations, Dma.BlitterReservations, Dma.CopperReservations, Dma.PaulaReservations, Dma.DiskReservations, Dma.SpriteReservations, Dma.BitplaneReservations, Dma.RefreshReservations),
                FormatCounts(Dma.Grants, Dma.CpuGrants, Dma.BlitterGrants, Dma.CopperGrants, Dma.PaulaGrants, Dma.DiskGrants, Dma.SpriteGrants, Dma.BitplaneGrants, Dma.RefreshGrants),
                FormatCounts(Dma.Denied, Dma.CpuDenied, Dma.BlitterDenied, Dma.CopperDenied, Dma.PaulaDenied, Dma.DiskDenied, Dma.SpriteDenied, Dma.BitplaneDenied, Dma.RefreshDenied),
                FormatCounts(Dma.DeniedBlockers, Dma.CpuDeniedBlockers, Dma.BlitterDeniedBlockers, Dma.CopperDeniedBlockers, Dma.PaulaDeniedBlockers, Dma.DiskDeniedBlockers, Dma.SpriteDeniedBlockers, Dma.BitplaneDeniedBlockers, Dma.RefreshDeniedBlockers),
                Dma.CpuChipStallCycles,
                Dma.LiveBitplaneFetches,
                Dma.LiveSpriteFetches,
                Dma.LiveMissedSpriteSlots,
                PaulaDmaWords,
                Blitter.Busy ? 1 : 0,
                Blitter.CompletedMicroOps,
                Blitter.AdvanceWords,
                Blitter.AdvanceCalls,
                Blitter.AdvanceDeniedSlots,
                Blitter.AdvanceFallbacks,
                Blitter.KernelHits,
                Blitter.KernelMisses,
                Scheduler.Drains,
                Scheduler.BusDrains,
                Scheduler.SameCycleDrains,
                Scheduler.RasterEvents,
                Scheduler.CiaEvents,
                Scheduler.PaulaEvents,
                Scheduler.DiskEvents,
                Scheduler.AgnusEvents,
                Scheduler.BlitterEvents,
                Scheduler.HostDrainTicks,
                Scheduler.HostWakeTicks,
                Scheduler.HostAgnusTicks,
                Access.CapturedAccesses,
                Access.CpuAccesses,
                Access.CpuFetches,
                Access.CpuReads,
                Access.CpuWrites,
                Access.CpuPhaseWrites,
                Access.ByteWrites,
                Access.WordWrites,
                Access.LongWrites,
                Access.WaitCycles,
                Access.MaxWaitCycles,
                Access.ChipTargets,
                Access.ExpansionTargets,
                Access.RealFastTargets,
                Access.CustomTargets,
                Access.ChipWriteTargets,
                Access.ExpansionWriteTargets,
                Access.RealFastWriteTargets,
                Access.CaptureMayBeTruncated ? 1 : 0);
        }

        private static string FormatCounts(params long[] values)
            => string.Join('/', values);
    }
}
