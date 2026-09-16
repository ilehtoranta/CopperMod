using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using CopperMod.Amiga.Lightweight;

var frames = 10;
var warmup = 0;
var romPath = (string?)null;
var adfPath = (string?)null;
var scalarCpu = false;
var syntheticRomLoop = false;
var syntheticRomStop = false;
var syntheticCiaTimer = false;
var syntheticCopper = false;
var syntheticBitplanes = false;
var syntheticDisplayDma = false;
var syntheticBlitter = false;
var syntheticBlitterFill = false;
var syntheticBlitterLine = false;
var syntheticSprites = false;
var syntheticSpriteDma = false;
var syntheticPaulaManual = false;
var syntheticPaulaDma = false;
var audioDmaOff = false;
var diskSerial = false;
var diskDma = false;
var ciaTod = false;
var inputReplay = false;
var wideOutput = false;
var hires = false;
string? bootProbeDirectory = null;
string? inputScriptPath = null;
var probeContinueUnsupported = false;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--frames" && i + 1 < args.Length && int.TryParse(args[++i], out var parsed)) frames = parsed;
    else if (args[i] == "--warmup" && i + 1 < args.Length && int.TryParse(args[++i], out var warmupFrames)) warmup = warmupFrames;
    else if (args[i] == "--rom" && i + 1 < args.Length) romPath = args[++i];
    else if (args[i] == "--adf" && i + 1 < args.Length) adfPath = args[++i];
    else if (args[i] == "--scalar-cpu") scalarCpu = true;
    else if (args[i] == "--synthetic-rom-loop") syntheticRomLoop = true;
    else if (args[i] == "--synthetic-rom-stop") syntheticRomStop = true;
    else if (args[i] == "--synthetic-cia-timer") syntheticCiaTimer = true;
    else if (args[i] == "--synthetic-copper") syntheticCopper = true;
    else if (args[i] == "--synthetic-bitplanes") syntheticBitplanes = true;
    else if (args[i] == "--synthetic-display-dma") syntheticDisplayDma = true;
    else if (args[i] == "--synthetic-blitter") syntheticBlitter = true;
    else if (args[i] == "--synthetic-blitter-fill") syntheticBlitterFill = true;
    else if (args[i] == "--synthetic-blitter-line") syntheticBlitterLine = true;
    else if (args[i] == "--synthetic-sprites") syntheticSprites = true;
    else if (args[i] == "--synthetic-sprite-dma") syntheticSpriteDma = true;
    else if (args[i] == "--synthetic-paula-manual") syntheticPaulaManual = true;
    else if (args[i] == "--synthetic-paula-dma") syntheticPaulaDma = true;
    else if (args[i] == "--audio-dma-off") audioDmaOff = true;
    else if (args[i] == "--disk-serial") diskSerial = true;
    else if (args[i] == "--disk-dma") diskDma = diskSerial = true;
    else if (args[i] == "--cia-tod") ciaTod = true;
    else if (args[i] == "--input-replay") inputReplay = true;
    else if (args[i] == "--wide-output") wideOutput = true;
    else if (args[i] == "--hires") hires = wideOutput = true;
    else if (args[i] == "--boot-probe" && i + 1 < args.Length) bootProbeDirectory = args[++i];
    else if (args[i] == "--input-script" && i + 1 < args.Length) inputScriptPath = args[++i];
    else if (args[i] == "--probe-continue-unsupported") probeContinueUnsupported = true;
}

if ((syntheticRomLoop ? 1 : 0) + (syntheticRomStop ? 1 : 0) +
    (syntheticCiaTimer ? 1 : 0) + (syntheticCopper ? 1 : 0) +
    (syntheticBitplanes ? 1 : 0) + (syntheticDisplayDma ? 1 : 0) +
    (syntheticBlitter ? 1 : 0) + (syntheticBlitterFill ? 1 : 0) +
    (syntheticBlitterLine ? 1 : 0) + (syntheticSprites ? 1 : 0) +
    (syntheticSpriteDma ? 1 : 0) + (syntheticPaulaManual ? 1 : 0) +
    (syntheticPaulaDma ? 1 : 0) > 1)
    throw new ArgumentException("Choose only one synthetic ROM workload.");
if (audioDmaOff && !syntheticPaulaDma)
    throw new ArgumentException("--audio-dma-off requires --synthetic-paula-dma.");
if (diskSerial && (!syntheticPaulaDma || warmup < 30 || frames < 20))
    throw new ArgumentException("--disk-serial requires --synthetic-paula-dma, at least 30 warmup and 20 measured frames.");
if (ciaTod && !syntheticPaulaDma && !syntheticRomStop)
    throw new ArgumentException("--cia-tod requires --synthetic-paula-dma or --synthetic-rom-stop.");
if (inputReplay && !syntheticPaulaDma)
    throw new ArgumentException("--input-replay requires --synthetic-paula-dma.");
if (hires && !syntheticPaulaDma && !syntheticBitplanes)
    throw new ArgumentException("--hires requires --synthetic-paula-dma or --synthetic-bitplanes.");
if (bootProbeDirectory is not null && (romPath is null || warmup != 0 ||
    args.Any(a => a.StartsWith("--synthetic-", StringComparison.Ordinal))))
    throw new ArgumentException("--boot-probe requires a native --rom, no synthetic workload and zero warmup.");
if (inputScriptPath is not null && (romPath is null || inputReplay ||
    args.Any(a => a.StartsWith("--synthetic-", StringComparison.Ordinal))))
    throw new ArgumentException("--input-script requires a native --rom and no synthetic input/workload.");
if (probeContinueUnsupported && bootProbeDirectory is null)
    throw new ArgumentException("--probe-continue-unsupported requires --boot-probe; it cannot produce a successful benchmark.");
if (syntheticPaulaDma) syntheticSpriteDma = true;

using var machine = new LightweightA500Machine(
    configuration: romPath is null && !wideOutput ? null : new LightweightA500Configuration { FramebufferWidth = 908 },
    enableConservativeCpuLoopBatch: !scalarCpu);
var supportsBlitter = machine.GetType().GetProperty(
    "BlitterActive",
    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is not null;
var supportsDescendingFill = machine.GetType().GetProperty(
    "BlitterSupportsDescendingFill",
    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is not null;
var supportsLineMode = machine.GetType().GetProperty(
    "BlitterSupportsLineMode",
    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is not null;
var supportsSpriteDma = machine.GetType().GetProperty(
    "SpriteDmaLastOutputCycle",
    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is not null;
if (syntheticRomLoop || syntheticRomStop || syntheticCiaTimer || syntheticCopper || syntheticBitplanes || syntheticDisplayDma || syntheticBlitter || syntheticBlitterFill || syntheticBlitterLine || syntheticSprites || syntheticSpriteDma || syntheticPaulaManual)
    machine.LoadKickstart(CreateSyntheticRom(syntheticRomStop || syntheticCiaTimer || syntheticCopper || syntheticBitplanes || syntheticDisplayDma || syntheticBlitter || syntheticBlitterFill || syntheticBlitterLine || syntheticSprites || syntheticSpriteDma || syntheticPaulaManual));
else if (romPath is not null)
    machine.LoadKickstart(ReadImage(romPath, ".rom", ".bin", ".kick"));
if (adfPath is not null) machine.MountAdf(ReadImage(adfPath, ".adf"));
if (syntheticCiaTimer) ConfigureSyntheticCiaTimer(machine);
if (syntheticCopper) ConfigureSyntheticCopper(machine);
if (syntheticBitplanes) ConfigureSyntheticBitplanes(machine, hires);
if (syntheticDisplayDma)
{
    ConfigureSyntheticBitplanes(machine, hires);
    ConfigureSyntheticCopper(machine);
}
if (syntheticBlitter || syntheticBlitterFill || syntheticBlitterLine || syntheticSprites || syntheticSpriteDma || syntheticPaulaManual)
{
    ConfigureSyntheticBitplanes(machine, hires);
    ConfigureSyntheticCopper(machine);
    ConfigureSyntheticBlitter(
        machine,
        syntheticBlitterFill && supportsDescendingFill,
        (syntheticBlitterLine || syntheticSprites || syntheticSpriteDma || syntheticPaulaManual) && supportsLineMode);
}
if (syntheticSprites || syntheticSpriteDma || syntheticPaulaManual)
    ConfigureSyntheticSprites(machine);
if (syntheticSpriteDma || syntheticPaulaManual)
    ConfigureSyntheticSpriteDma(machine);
if (syntheticPaulaManual)
    ConfigureSyntheticPaulaManual(machine);
if (syntheticPaulaDma)
    ConfigureSyntheticPaulaDma(machine, enabled: !audioDmaOff);
if (diskSerial)
{
    if (machine.GetType().GetProperty("DiskSerialNextCycle", BindingFlags.Instance | BindingFlags.NonPublic) is null)
        throw new InvalidOperationException("This engine has no live disk input path.");
    if (!machine.IsAdfMounted) machine.MountAdf(new byte[80 * 2 * 11 * 512]);
    machine.WriteCustomRegisterFromCopper(0x09E, 0x8100, machine.Cycle);
    var driveCycle = machine.Cycle;
    machine.WriteByte(0xBFD100, 0x77, ref driveCycle, Copper68k.M68kBusAccessKind.CpuDataWrite);
    machine.WriteByte(0xBFD300, 0xFF, ref driveCycle, Copper68k.M68kBusAccessKind.CpuDataWrite);
}
var serviceBlitter = (syntheticBlitter || syntheticBlitterFill || syntheticBlitterLine || syntheticSprites || syntheticSpriteDma || syntheticPaulaManual) && supportsBlitter;
var serviceDescendingFill = syntheticBlitterFill && supportsDescendingFill;
var serviceLineMode = (syntheticBlitterLine || syntheticSprites || syntheticSpriteDma || syntheticPaulaManual) && supportsLineMode;
if (diskDma)
{
    if (machine.GetType().GetProperty("DiskDmaActive", BindingFlags.Instance | BindingFlags.NonPublic) is null)
        throw new InvalidOperationException("This engine has no disk RAM DMA path.");
    machine.WriteCustomRegisterFromCopper(0x096, 0x8210, machine.Cycle);
    // Continuous read-DMA cost fixture: WORDSYNC is tested separately. Its
    // legitimate track-gap wait can otherwise leave a whole field without RAM DMA.
    machine.WriteCustomRegisterFromCopper(0x09E, 0x0400, machine.Cycle);
}
if (ciaTod) ConfigureTod(machine);
if (inputReplay) ConfigureInputReplay(machine);
var todStartCycle = machine.Cycle;
var todStartFrame = machine.CompletedFrames;
var bootProbe = bootProbeDirectory is null ? null : new NativeBootProbe(bootProbeDirectory, probeContinueUnsupported);
var inputScript = inputScriptPath is null ? null : new NativeInputScript(inputScriptPath,
    path => ReadImage(path, ".adf"));
if (bootProbe is null && inputScript?.ChangesMediaBetween(warmup, checked(warmup + frames)) == true)
    throw new ArgumentException("Scripted media changes must precede measurement or use --boot-probe.");
for (var frame = 0; frame < warmup; frame++)
{
    inputScript?.Apply(machine, frame);
    if (inputReplay) BeginInputReplayFrame(machine, frame);
    if (diskDma) PrepareDiskDmaFrame(machine);
    ExecuteMeasuredFrame(machine, syntheticCiaTimer, serviceBlitter, serviceDescendingFill, serviceLineMode, syntheticSpriteDma || syntheticPaulaManual, syntheticPaulaManual);
    if (inputReplay) ConsumeInputReplayFrame(machine, frame, 0);
}
var diskPositionBefore = diskSerial ? ReadOptionalMachineProperty<int>(machine, "DiskBitPosition") : 0;
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();
var stopwatch = new Stopwatch();
var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
stopwatch.Start();
ulong checksum = 1469598103934665603UL;
ulong diskRamChecksum = 1469598103934665603UL;
ulong inputChecksum = 1469598103934665603UL;
for (var frame = 0; frame < frames; frame++)
{
    inputScript?.Apply(machine, warmup + frame);
    if (inputReplay) BeginInputReplayFrame(machine, warmup + frame);
    var diskRemainingBefore = diskDma ? PrepareDiskDmaFrame(machine) : 0;
    if (bootProbe is null)
        ExecuteMeasuredFrame(machine, syntheticCiaTimer, serviceBlitter, serviceDescendingFill, serviceLineMode, syntheticSpriteDma || syntheticPaulaManual, syntheticPaulaManual);
    else
        bootProbe.ExecuteFrame(machine, frame == frames - 1);
    checksum = AccumulateOutputChecksum(machine, checksum, frame);
    if (diskDma) diskRamChecksum = ConsumeDiskDmaFrame(machine, diskRemainingBefore, diskRamChecksum);
    if (inputReplay) inputChecksum = ConsumeInputReplayFrame(machine, warmup + frame, inputChecksum);
}
stopwatch.Stop();
if (bootProbe is not null) Console.WriteLine("BOOT_PROBE diagnostic run: FPS/allocation totals are not acceptance measurements; native gameplay is not verified.");
var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
if (inputReplay && machine.CompletedFrames != (long)warmup + frames)
    throw new InvalidOperationException("Input replay advanced beyond its requested complete-field interval.");
var cpuChecksum = CreateCpuChecksum(machine.Cpu);
var hardwareChecksum = CreateHardwareChecksum(
    machine,
    serviceBlitter,
    (syntheticSpriteDma || syntheticPaulaManual) && supportsSpriteDma);

var workload = syntheticCiaTimer ? "cia-timer" : syntheticCopper ? "copper" : syntheticBitplanes ? "bitplanes" : syntheticDisplayDma ? "display-dma" : syntheticBlitter ? "blitter" : syntheticBlitterFill ? "blitter-fill" : syntheticBlitterLine ? "blitter-line" : syntheticPaulaManual ? "paula-manual" : syntheticSpriteDma ? "sprite-dma" : syntheticSprites ? "sprites" : syntheticRomStop ? "rom-stop" : syntheticRomLoop ? "rom-loop" : romPath is null ? "chip-loop" : "image";
var unsupported = ReadOptionalMachineProperty<string>(machine, "UnsupportedActiveFeature") ?? "none";
var producesPcm = ReadOptionalMachineProperty<bool>(machine, "AudioProducesPcm");
if (syntheticPaulaDma)
{
    workload = audioDmaOff ? "paula-dma-dormant" : "paula-dma";
    if (unsupported == "none")
        hardwareChecksum = AccumulatePaulaDmaChecksum(machine, hardwareChecksum, !audioDmaOff);
    if (producesPcm && !audioDmaOff)
    {
        var pcm = machine.AudioSamples.Span;
        var nonzeroLeft = false;
        var nonzeroRight = false;
        var changing = false;
        for (var sample = 0; sample < pcm.Length; sample += 2)
        {
            nonzeroLeft |= pcm[sample] != 0;
            nonzeroRight |= pcm[sample + 1] != 0;
            changing |= sample > 0 && (pcm[sample] != pcm[0] || pcm[sample + 1] != pcm[1]);
        }
        if (!nonzeroLeft || !nonzeroRight || !changing)
            throw new InvalidOperationException("The DMA workload did not produce changing stereo PCM.");
    }
}
if (diskSerial)
{
    workload += "-disk-serial";
    var position = ReadOptionalMachineProperty<int>(machine, "DiskBitPosition");
    var next = ReadOptionalMachineProperty<long>(machine, "DiskSerialNextCycle");
    if (next <= machine.Cycle || next == long.MaxValue || position == diskPositionBefore ||
        (machine.Intreq & 0x1000) == 0 || (machine.CiaBPendingInterrupts & 0x10) == 0)
        throw new InvalidOperationException("Disk input did not advance and produce sync/index signals.");
    hardwareChecksum = (hardwareChecksum ^ (uint)position) * 1099511628211UL;
    hardwareChecksum = (hardwareChecksum ^ ReadOptionalMachineProperty<ushort>(machine, "DiskInputShift")) * 1099511628211UL;
}
if (diskDma)
{
    workload += "-ram-dma";
    hardwareChecksum = (hardwareChecksum ^ diskRamChecksum) * 1099511628211UL;
    foreach (var value in machine.ChipRam.Span.Slice(0x50000, 0x7FFE))
        hardwareChecksum = (hardwareChecksum ^ value) * 1099511628211UL;
}
if (ciaTod)
{
    workload += "-cia-tod";
    var a = ReadOptionalMachineProperty<uint>(machine, "CiaATod");
    var b = ReadOptionalMachineProperty<uint>(machine, "CiaBTod");
    if (a != ((machine.CompletedFrames - todStartFrame) & 0xFFFFFF) ||
        b != ((machine.Cycle / 454 - todStartCycle / 454) & 0xFFFFFF))
        throw new InvalidOperationException("TOD did not count every field/line in the connected workload.");
    hardwareChecksum = (hardwareChecksum ^ a) * 1099511628211UL;
    hardwareChecksum = (hardwareChecksum ^ b) * 1099511628211UL;
}
if (inputReplay)
{
    workload += "-input-replay";
    hardwareChecksum = (hardwareChecksum ^ inputChecksum) * 1099511628211UL;
}
if (wideOutput) workload += "-wide";
if (hires) workload += "-hires";
Console.WriteLine($"engine=lightweight-a500 workload={workload} cpuMode={(scalarCpu ? "scalar" : "conservative-batch")} warmup={warmup} frames={frames} completed={machine.CompletedFrames} fps={frames / stopwatch.Elapsed.TotalSeconds:F2} cycle={machine.Cycle} cpu=0x{cpuChecksum:X16} hardware=0x{hardwareChecksum:X16} output=0x{checksum:X16} pixels={machine.Framebuffer.Length} audioSamples={machine.AudioSamples.Length} pcm={(producesPcm ? "real" : "placeholder")} allocated={allocatedBytes} adf={machine.IsAdfMounted} unsupported={unsupported}");
if (unsupported != "none")
    Environment.ExitCode = 2;

// Separate methods keep the same runner usable with frozen pre-DMA engines.
[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
static void ConfigureInputReplay(LightweightA500Machine m)
{
    if (m.GetType().GetProperty("KeyboardWaitingForHandshake", BindingFlags.Instance | BindingFlags.NonPublic) is null)
        throw new InvalidOperationException("This engine has no input execution path.");
    var cycle = m.Cycle;
    m.WriteByte(0xBFED01, 0x88, ref cycle, Copper68k.M68kBusAccessKind.CpuDataWrite);
    m.WriteCustomRegisterFromCopper(0x09A, 0xC008, m.Cycle);
    m.Cpu.Cycles = m.Cycle;
}

[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
static void BeginInputReplayFrame(LightweightA500Machine m, int frame)
{
    if (frame != 0)
    {
        if (!m.KeyboardWaitingForHandshake)
            throw new InvalidOperationException("Previous keyboard byte was not awaiting a hardware handshake.");
        var cycle = m.Cycle;
        m.WriteByte(0xBFEE01, 0x40, ref cycle, Copper68k.M68kBusAccessKind.CpuDataWrite);
        m.AdvanceHardwareTo(m.Cycle + 604);
        cycle = m.Cycle;
        m.WriteByte(0xBFEE01, 0, ref cycle, Copper68k.M68kBusAccessKind.CpuDataWrite);
        m.WriteCustomRegisterFromCopper(0x09C, 8, m.Cycle);
    }
    var joystick = (byte)((1 << (frame & 3)) | ((frame & 1) * 0x10));
    m.SubmitInput(new(0, joystick, (byte)(frame & 3), (short)(frame % 7 - 3),
        (short)((frame & 1) == 0 ? 1 : -1), (ushort)(0x135 | ((frame & 1) << 7))));
    m.Cpu.Cycles = m.Cycle;
}

[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
static ulong ConsumeInputReplayFrame(LightweightA500Machine m, int frame, ulong hash)
{
    var cycle = m.Cycle;
    var sdr = m.ReadByte(0xBFEC01, ref cycle, Copper68k.M68kBusAccessKind.CpuDataRead);
    var icr = m.ReadByte(0xBFED01, ref cycle, Copper68k.M68kBusAccessKind.CpuDataRead);
    var fire = m.ReadByte(0xBFE001, ref cycle, Copper68k.M68kBusAccessKind.CpuDataRead);
    var joy0 = m.ReadWord(0xDFF00A, ref cycle, Copper68k.M68kBusAccessKind.CpuDataRead);
    var joy1 = m.ReadWord(0xDFF00C, ref cycle, Copper68k.M68kBusAccessKind.CpuDataRead);
    var pot = m.ReadWord(0xDFF016, ref cycle, Copper68k.M68kBusAccessKind.CpuDataRead);
    var remainder = (frame + 1) % 7;
    var x = unchecked((byte)(remainder * (remainder - 1) / 2 - 3 * remainder));
    var expectedJoy0 = (((frame + 1) & 1) << 8) | x;
    var expectedJoy1 = (frame & 3) switch { 0 => 0x0100, 1 => 1, 2 => 0x0300, _ => 3 };
    if (!m.KeyboardWaitingForHandshake || sdr != ((frame & 1) == 0 ? 0x95 : 0x94) ||
        (icr & 0x88) != 0x88 || joy0 != expectedJoy0 || joy1 != expectedJoy1 ||
        (fire & 0xC0) != ((frame & 1) == 0 ? 0xC0 : 0) ||
        (pot & 0x0400) != ((frame & 2) == 0 ? 0x0400 : 0))
        throw new InvalidOperationException($"Input replay mismatch at field {frame}: SDR={sdr:X2}, ICR={icr:X2}, JOY={joy0:X4}/{joy1:X4}.");
    m.Cpu.Cycles = m.Cycle;
    hash = (hash ^ sdr) * 1099511628211UL;
    hash = (hash ^ joy0) * 1099511628211UL;
    hash = (hash ^ joy1) * 1099511628211UL;
    hash = (hash ^ fire) * 1099511628211UL;
    return (hash ^ pot) * 1099511628211UL;
}

static void ConfigureTod(LightweightA500Machine machine)
{
    if (machine.GetType().GetProperty("CiaATod", BindingFlags.Instance | BindingFlags.NonPublic) is null)
        throw new InvalidOperationException("This engine has no TOD execution path.");
    var cycle = machine.Cycle;
    machine.WriteByte(0xBFE801, 0, ref cycle, Copper68k.M68kBusAccessKind.CpuDataWrite);
    machine.WriteByte(0xBFD800, 0, ref cycle, Copper68k.M68kBusAccessKind.CpuDataWrite);
    machine.Cpu.Cycles = cycle;
}

static int PrepareDiskDmaFrame(LightweightA500Machine machine)
{
    if (!machine.DiskDmaActive)
    {
        machine.WriteCustomRegisterFromCopper(0x024, 0x4000, machine.Cycle);
        machine.WriteCustomRegisterFromCopper(0x020, 5, machine.Cycle);
        machine.WriteCustomRegisterFromCopper(0x022, 0, machine.Cycle);
        machine.WriteCustomRegisterFromCopper(0x024, 0xBFFF, machine.Cycle);
        machine.WriteCustomRegisterFromCopper(0x024, 0xBFFF, machine.Cycle);
    }
    return machine.DiskDmaRemaining;
}

static ulong ConsumeDiskDmaFrame(LightweightA500Machine machine, int before, ulong hash)
{
    var after = machine.GetDiskPointer();
    // A last address can be accepted before the frame boundary and commit
    // after it. Count actual completed RAM words, not address advancement.
    if (machine.DiskDmaRemaining >= before || after > 0x57FFE)
        throw new InvalidOperationException($"Disk DMA did not complete RAM words on frame {machine.CompletedFrames}: remaining {before}->{machine.DiskDmaRemaining}, pointer {after:X}, unsupported={machine.UnsupportedActiveFeature}.");
    hash = (hash ^ after) * 1099511628211UL;
    return (hash ^ machine.GetCustomRegister(LightweightRegisters.Dskdatr)) * 1099511628211UL;
}

static ulong CreateCpuChecksum(Copper68k.M68kCpuState cpu)
{
    var hash = 1469598103934665603UL;
    hash = (hash ^ cpu.ProgramCounter) * 1099511628211UL;
    hash = (hash ^ cpu.StatusRegister) * 1099511628211UL;
    hash = (hash ^ (ulong)cpu.Cycles) * 1099511628211UL;
    foreach (var value in cpu.D) hash = (hash ^ value) * 1099511628211UL;
    foreach (var value in cpu.A) hash = (hash ^ value) * 1099511628211UL;
    return hash;
}

static ulong AccumulateOutputChecksum(
    LightweightA500Machine machine,
    ulong hash,
    int frame)
{
    const ulong prime = 1099511628211UL;
    const int pixelSamples = 257;
    const int audioSamples = 61;
    var pixels = machine.Framebuffer.Span;
    var audio = machine.AudioSamples.Span;
    hash = (hash ^ (uint)pixels.Length) * prime;
    hash = (hash ^ (uint)audio.Length) * prime;

    var pixelIndex = (frame * 131) % pixels.Length;
    for (var sample = 0; sample < pixelSamples; sample++)
    {
        hash = (hash ^ (uint)pixels[pixelIndex]) * prime;
        pixelIndex += 557;
        if (pixelIndex >= pixels.Length)
            pixelIndex -= pixels.Length;
    }

    var audioIndex = (frame * 17) % audio.Length;
    for (var sample = 0; sample < audioSamples; sample++)
    {
        hash = (hash ^ (ushort)audio[audioIndex]) * prime;
        audioIndex += 31;
        if (audioIndex >= audio.Length)
            audioIndex -= audio.Length;
    }
    return hash;
}

static ulong CreateHardwareChecksum(
    LightweightA500Machine machine,
    bool includeBlitter,
    bool includeSpriteDma)
{
    var hash = 1469598103934665603UL;
    hash = (hash ^ (ulong)machine.Cycle) * 1099511628211UL;
    hash = (hash ^ (uint)machine.BeamLine) * 1099511628211UL;
    hash = (hash ^ (uint)machine.BeamColorClock) * 1099511628211UL;
    hash = (hash ^ machine.Dmacon) * 1099511628211UL;
    hash = (hash ^ machine.Intena) * 1099511628211UL;
    hash = (hash ^ machine.Intreq) * 1099511628211UL;
    hash = (hash ^ machine.CopperProgramCounter) * 1099511628211UL;
    hash = (hash ^ machine.GetCustomRegister(0x180)) * 1099511628211UL;
    hash = (hash ^ (ulong)machine.BitplaneLastOutputCycle) * 1099511628211UL;
    if (includeBlitter)
    {
        hash = (hash ^ (ReadOptionalMachineProperty<bool>(machine, "BlitterStatusBusy") ? 1UL : 0UL)) * 1099511628211UL;
        hash = (hash ^ (ReadOptionalMachineProperty<bool>(machine, "BlitterActive") ? 1UL : 0UL)) * 1099511628211UL;
        hash = (hash ^ (ulong)ReadOptionalMachineProperty<long>(machine, "BlitterLastOutputCycle")) * 1099511628211UL;
        hash = (hash ^ InvokeOptionalMachineUInt32(machine, "GetBlitterPointer", LightweightRegisters.Bltapth)) * 1099511628211UL;
        hash = (hash ^ InvokeOptionalMachineUInt32(machine, "GetBlitterPointer", LightweightRegisters.Bltdpth)) * 1099511628211UL;
    }
    if (includeSpriteDma)
    {
        hash = (hash ^ (ulong)ReadOptionalMachineProperty<long>(machine,
            "SpriteDmaLastOutputCycle")) * 1099511628211UL;
        for (var sprite = 0; sprite < 8; sprite++)
        {
            hash = (hash ^ InvokeOptionalMachineUInt32Int(
                machine,
                "GetLiveSpritePointer",
                sprite)) * 1099511628211UL;
        }
    }
    for (var plane = 0; plane < 6; plane++)
    {
        hash = (hash ^ machine.GetLiveBitplanePointer(plane)) * 1099511628211UL;
        hash = (hash ^ machine.GetBitplaneDataLatch(plane)) * 1099511628211UL;
    }
    hash = (hash ^ (ReadOptionalMachineProperty<bool>(machine, "VideoActive") ? 1UL : 0UL)) * 1099511628211UL;
    hash = (hash ^ (machine.RomOverlayEnabled ? 1UL : 0UL)) * 1099511628211UL;
    return hash;
}

static T? ReadOptionalMachineProperty<T>(
    LightweightA500Machine machine,
    string propertyName)
{
    var property = machine.GetType().GetProperty(
        propertyName,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    return property?.GetValue(machine) is T value ? value : default;
}

static uint InvokeOptionalMachineUInt32(
    LightweightA500Machine machine,
    string methodName,
    ushort argument)
{
    var method = machine.GetType().GetMethod(
        methodName,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        types: [typeof(ushort)],
        modifiers: null);
    return method?.Invoke(machine, [argument]) is uint value ? value : 0;
}

static uint InvokeOptionalMachineUInt32Int(
    LightweightA500Machine machine,
    string methodName,
    int argument)
{
    var method = machine.GetType().GetMethod(
        methodName,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        types: [typeof(int)],
        modifiers: null);
    return method?.Invoke(machine, [argument]) is uint value ? value : 0;
}

static void ConfigureSyntheticCiaTimer(LightweightA500Machine machine)
{
    long cycle = machine.Cycle;
    machine.WriteByte(0x00BFD400, 0xE8, ref cycle, Copper68k.M68kBusAccessKind.CpuDataWrite);
    machine.WriteByte(0x00BFD500, 0x03, ref cycle, Copper68k.M68kBusAccessKind.CpuDataWrite);
    machine.WriteByte(0x00BFDD00, 0x81, ref cycle, Copper68k.M68kBusAccessKind.CpuDataWrite);
    machine.WriteByte(0x00BFDE00, 0x11, ref cycle, Copper68k.M68kBusAccessKind.CpuDataWrite);
    machine.Cpu.Cycles = cycle;
}

static void ConfigureSyntheticCopper(LightweightA500Machine machine)
{
    const uint list = 0x2000;
    long cycle = machine.Cycle;
    WriteMachineWord(machine, list + 0, 0x0180, ref cycle);
    WriteMachineWord(machine, list + 2, 0x0001, ref cycle);
    WriteMachineWord(machine, list + 4, 0x0180, ref cycle);
    WriteMachineWord(machine, list + 6, 0x0F00, ref cycle);
    WriteMachineWord(machine, list + 8, 0x0088, ref cycle);
    WriteMachineWord(machine, list + 10, 0x0000, ref cycle);
    WriteMachineWord(machine, LightweightA500Machine.CustomBase + 0x080, 0, ref cycle);
    WriteMachineWord(machine, LightweightA500Machine.CustomBase + 0x082, (ushort)list, ref cycle);
    WriteMachineWord(machine, LightweightA500Machine.CustomBase + 0x096, 0x8280, ref cycle);
    WriteMachineWord(machine, LightweightA500Machine.CustomBase + 0x088, 0, ref cycle);
    machine.Cpu.Cycles = cycle;
}

static void ConfigureSyntheticBitplanes(LightweightA500Machine machine, bool hires = false)
{
    var wordsPerLine = hires ? 40 : 20;
    var rewindOneLineModulo = unchecked((ushort)(-2 * wordsPerLine));
    long cycle = machine.Cycle;
    for (var plane = 0; plane < (hires ? 4 : 6); plane++)
    {
        var pointer = 0x10000u + (uint)(plane * 0x8000);
        for (var word = 0; word < wordsPerLine; word++)
        {
            var pattern = (ushort)(0xA55A ^ (plane * 0x1111) ^ (word * 0x2493));
            WriteMachineWord(machine, pointer + (uint)(word * 2), pattern, ref cycle);
        }
        WriteMachineWord(
            machine,
            LightweightA500Machine.CustomBase + 0x0E0u + (uint)(plane * 4),
            (ushort)(pointer >> 16),
            ref cycle);
        WriteMachineWord(
            machine,
            LightweightA500Machine.CustomBase + 0x0E2u + (uint)(plane * 4),
            (ushort)pointer,
            ref cycle);
    }
    WriteMachineWord(machine, LightweightA500Machine.CustomBase + 0x08E, 0x2C81, ref cycle);
    WriteMachineWord(machine, LightweightA500Machine.CustomBase + 0x090, 0x2CC1, ref cycle);
    WriteMachineWord(machine, LightweightA500Machine.CustomBase + 0x092, hires ? (ushort)0x003C : (ushort)0x0038, ref cycle);
    WriteMachineWord(machine, LightweightA500Machine.CustomBase + 0x094, hires ? (ushort)0x00D4 : (ushort)0x00D0, ref cycle);
    WriteMachineWord(machine, LightweightA500Machine.CustomBase + 0x108, rewindOneLineModulo, ref cycle);
    WriteMachineWord(machine, LightweightA500Machine.CustomBase + 0x10A, rewindOneLineModulo, ref cycle);
    for (var color = 1; color < 32; color++)
    {
        var rgb = (ushort)(((color & 7) << 8) |
            (((color * 3) & 15) << 4) | ((color * 5) & 15));
        WriteMachineWord(
            machine,
            LightweightA500Machine.CustomBase + 0x180u + (uint)(color * 2),
            rgb,
            ref cycle);
    }
    WriteMachineWord(machine, LightweightA500Machine.CustomBase + 0x100, hires ? (ushort)0xC200 : (ushort)0x6000, ref cycle);
    WriteMachineWord(machine, LightweightA500Machine.CustomBase + 0x096, 0x8300, ref cycle);
    machine.Cpu.Cycles = cycle;
}

static void ConfigureSyntheticBlitter(
    LightweightA500Machine machine,
    bool descendingFill,
    bool lineMode)
{
    const uint sourceA = 0x10000;
    const uint sourceB = 0x18000;
    const uint sourceC = 0x20000;
    const uint destinationD = 0x28000;
    long cycle = machine.Cycle;
    for (var word = 0; word < 256; word++)
    {
        WriteMachineWord(machine, sourceA + (uint)(word * 2),
            (ushort)(0xA55A ^ (word * 0x1249)), ref cycle);
        WriteMachineWord(machine, sourceB + (uint)(word * 2),
            (ushort)(0x0F0F ^ (word * 0x2493)), ref cycle);
        WriteMachineWord(machine, sourceC + (uint)(word * 2),
            (ushort)(0x3333 ^ (word * 0x1111)), ref cycle);
    }
    WriteMachineWord(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Bltcon0, 0x0FCA, ref cycle);
    WriteMachineWord(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Bltcon1, lineMode ? (ushort)0x0001 : descendingFill ? (ushort)0x0012 : (ushort)0, ref cycle);
    WriteMachineWord(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Bltafwm, 0xFFFF, ref cycle);
    WriteMachineWord(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Bltalwm, 0xFFFF, ref cycle);
    WriteMachineWord(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Bltamod, 0, ref cycle);
    WriteMachineWord(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Bltbmod, 0, ref cycle);
    WriteMachineWord(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Bltcmod, lineMode ? (ushort)0x0020 : (ushort)0, ref cycle);
    WriteMachineWord(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Bltdmod, 0, ref cycle);
    if (lineMode)
        WriteMachineWord(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Bltadat, 0x8000, ref cycle);
    WriteBlitterPointer(machine, LightweightRegisters.Bltapth, lineMode ? 0u : descendingFill ? sourceA + 0x1FFFE : sourceA, ref cycle);
    WriteBlitterPointer(machine, LightweightRegisters.Bltbpth, descendingFill ? sourceB + 0x1FFFE : sourceB, ref cycle);
    WriteBlitterPointer(machine, LightweightRegisters.Bltcpth, descendingFill ? sourceC + 0x1FFFE : sourceC, ref cycle);
    WriteBlitterPointer(machine, LightweightRegisters.Bltdpth, descendingFill ? destinationD + 0x1FFFE : destinationD, ref cycle);
    WriteMachineWord(machine, LightweightA500Machine.CustomBase + LightweightRegisters.DmaconWrite, 0x8640, ref cycle);
    var size = lineMode ? (ushort)0x0002 : (ushort)0;
    WriteMachineWord(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Bltsize, size, ref cycle);
    if (lineMode)
        WriteMachineWord(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Bltsize, size, ref cycle);
    machine.Cpu.Cycles = cycle;
}

static void ConfigureSyntheticSprites(LightweightA500Machine machine)
{
    const int firstX = 129;
    const int firstY = 44;
    long cycle = machine.Cycle;
    WriteMachineWord(
        machine,
        LightweightA500Machine.CustomBase + LightweightRegisters.Bplcon2,
        0x0020,
        ref cycle);
    for (var sprite = 0; sprite < 8; sprite++)
    {
        var x = firstX + (sprite * 32);
        var y = firstY + (sprite & 3);
        var horizontal = x - 1;
        var stop = y + 1;
        var attached = (sprite & 1) != 0 && (sprite == 1 || sprite == 5);
        var pos = (ushort)(((y & 0xFF) << 8) | ((horizontal >> 1) & 0xFF));
        var ctl = (ushort)(((stop & 0xFF) << 8) |
            (horizontal & 1) |
            ((stop & 0x100) != 0 ? 0x0002 : 0) |
            ((y & 0x100) != 0 ? 0x0004 : 0) |
            (attached ? 0x0080 : 0));
        var register = LightweightA500Machine.CustomBase + 0x140u +
            (uint)(sprite * 8);
        WriteMachineWord(machine, register, pos, ref cycle);
        WriteMachineWord(machine, register + 2, ctl, ref cycle);
        WriteMachineWord(machine, register + 6,
            (ushort)(0xA55A ^ (sprite * 0x1111)), ref cycle);
        WriteMachineWord(machine, register + 4,
            (ushort)(0xF00F ^ (sprite * 0x2493)), ref cycle);
    }
    machine.Cpu.Cycles = Math.Max(machine.Cpu.Cycles, cycle);
}

static void ConfigureSyntheticSpriteDma(LightweightA500Machine machine)
{
    const int firstX = 129;
    const int firstY = 44;
    const int height = 240;
    for (var sprite = 0; sprite < 8; sprite++)
    {
        var address = GetSyntheticSpriteDmaAddress(sprite);
        var x = firstX + (sprite * 32);
        var horizontal = x - 1;
        var stop = firstY + height;
        var attached = (sprite & 1) != 0 && (sprite == 1 || sprite == 5);
        var pos = (ushort)(((firstY & 0xFF) << 8) |
            ((horizontal >> 1) & 0xFF));
        var ctl = (ushort)(((stop & 0xFF) << 8) |
            (horizontal & 1) |
            ((stop & 0x100) != 0 ? 0x0002 : 0) |
            ((firstY & 0x100) != 0 ? 0x0004 : 0) |
            (attached ? 0x0080 : 0));
        machine.WriteChipWordDma(address, pos);
        machine.WriteChipWordDma(address + 2, ctl);
        for (var row = 0; row < height; row++)
        {
            machine.WriteChipWordDma(
                address + 4u + (uint)(row * 4),
                (ushort)(0xF00F ^ (sprite * 0x2493) ^ (row * 0x0111)));
            machine.WriteChipWordDma(
                address + 6u + (uint)(row * 4),
                (ushort)(0xA55A ^ (sprite * 0x1111) ^ (row * 0x0249)));
        }
        machine.WriteChipWordDma(address + 4u + (uint)(height * 4), 0);
        machine.WriteChipWordDma(address + 6u + (uint)(height * 4), 0);
    }

    RearmSyntheticSpriteDma(machine);
    long cycle = machine.Cycle;
    WriteMachineWord(
        machine,
        LightweightA500Machine.CustomBase + LightweightRegisters.DmaconWrite,
        0x8220,
        ref cycle);
    machine.Cpu.Cycles = Math.Max(machine.Cpu.Cycles, cycle);
}

static void RearmSyntheticSpriteDma(LightweightA500Machine machine)
{
    long cycle = machine.Cycle;
    for (var sprite = 0; sprite < 8; sprite++)
    {
        var pointer = GetSyntheticSpriteDmaAddress(sprite);
        var highOffset = (ushort)(LightweightRegisters.SpritePointerFirst +
            (sprite * 4));
        WriteMachineWord(
            machine,
            LightweightA500Machine.CustomBase + highOffset,
            (ushort)(pointer >> 16),
            ref cycle);
        WriteMachineWord(
            machine,
            LightweightA500Machine.CustomBase + highOffset + 2u,
            (ushort)pointer,
            ref cycle);
    }
    machine.Cpu.Cycles = Math.Max(machine.Cpu.Cycles, cycle);
}

static uint GetSyntheticSpriteDmaAddress(int sprite)
    => 0x48000u + (uint)(sprite * 0x800);

static void ConfigureSyntheticPaulaManual(LightweightA500Machine machine)
{
    long cycle = machine.Cycle;
    WriteMachineWord(
        machine,
        LightweightA500Machine.CustomBase + LightweightRegisters.IntenaWrite,
        0xC780,
        ref cycle);
    for (var channel = 0; channel < 4; channel++)
    {
        var registerBase = (uint)(LightweightRegisters.Aud0lch + (channel * 0x10));
        WriteMachineWord(
            machine,
            LightweightA500Machine.CustomBase + registerBase + 6u,
            428,
            ref cycle);
        WriteMachineWord(
            machine,
            LightweightA500Machine.CustomBase + registerBase + 8u,
            64,
            ref cycle);
    }
    machine.Cpu.Cycles = Math.Max(machine.Cpu.Cycles, cycle);
    RearmSyntheticPaulaManual(machine);
}

static void ConfigureSyntheticPaulaDma(LightweightA500Machine machine, bool enabled)
{
    long cycle = machine.Cycle;
    for (var channel = 0; channel < 4; channel++)
    {
        var address = 0x4E000u + (uint)channel * 0x100;
        for (var word = 0; word < 64; word++)
            machine.WriteChipWordDma(address + (uint)word * 2,
                (ushort)(0x20E0 ^ word * 0x0103 ^ channel * 0x1111));
        var b = LightweightA500Machine.CustomBase + 0x0A0u + (uint)channel * 16;
        WriteMachineWord(machine, b, (ushort)(address >> 16), ref cycle);
        WriteMachineWord(machine, b + 2, (ushort)address, ref cycle);
        WriteMachineWord(machine, b + 4, 64, ref cycle);
        WriteMachineWord(machine, b + 6, (ushort)(128 + channel * 100), ref cycle);
        WriteMachineWord(machine, b + 8, 64, ref cycle);
    }
    WriteMachineWord(machine, LightweightA500Machine.CustomBase + 0x096,
        enabled ? (ushort)0x820F : (ushort)0x8200, ref cycle);
    machine.Cpu.Cycles = Math.Max(machine.Cpu.Cycles, cycle);
}

static ulong AccumulatePaulaDmaChecksum(LightweightA500Machine machine, ulong hash, bool enabled)
{
    for (var channel = 0; channel < 4; channel++)
    {
        var state = machine.GetPaulaChannelSnapshot(channel);
        if (state.DmaEnabled != enabled || enabled &&
            (!state.HasOutputWord || state.ManualPlayback || state.CurrentAddress == 0))
            throw new InvalidOperationException("The DMA workload did not execute all four audio channels.");
        hash = (hash ^ state.CurrentAddress) * 1099511628211UL;
        hash = (hash ^ (uint)state.RemainingWords) * 1099511628211UL;
        hash = (hash ^ state.OutputLatch) * 1099511628211UL;
        hash = (hash ^ unchecked((byte)state.CurrentSample)) * 1099511628211UL;
        hash = (hash ^ (ulong)state.NextSampleCycle) * 1099511628211UL;
        hash = (hash ^ (state.DelayedInterruptPending ? 1UL : 0UL)) * 1099511628211UL;
    }
    return hash;
}

static void RearmSyntheticPaulaManual(LightweightA500Machine machine)
{
    long cycle = machine.Cycle;
    WriteMachineWord(
        machine,
        LightweightA500Machine.CustomBase + LightweightRegisters.IntreqWrite,
        0x0780,
        ref cycle);
    for (var channel = 0; channel < 4; channel++)
    {
        var registerBase = (uint)(LightweightRegisters.Aud0lch + (channel * 0x10));
        var word = (ushort)(0x20E0 ^ (channel * 0x1111));
        WriteMachineWord(
            machine,
            LightweightA500Machine.CustomBase + registerBase + 0x0Au,
            word,
            ref cycle);
    }
    machine.Cpu.Cycles = Math.Max(machine.Cpu.Cycles, cycle);
}

static void RearmSyntheticBlitter(
    LightweightA500Machine machine,
    bool descendingFill,
    bool lineMode)
{
    if (machine.BlitterActive)
        return;

    long cycle = machine.Cycle;
    WriteBlitterPointer(machine, LightweightRegisters.Bltapth, lineMode ? 0u : descendingFill ? 0x2FFFEu : 0x10000u, ref cycle);
    WriteBlitterPointer(machine, LightweightRegisters.Bltbpth, descendingFill ? 0x37FFEu : 0x18000u, ref cycle);
    WriteBlitterPointer(machine, LightweightRegisters.Bltcpth, descendingFill ? 0x3FFFEu : 0x20000u, ref cycle);
    WriteBlitterPointer(machine, LightweightRegisters.Bltdpth, descendingFill ? 0x47FFEu : 0x28000u, ref cycle);
    WriteMachineWord(machine, LightweightA500Machine.CustomBase + LightweightRegisters.IntreqWrite, 0x0040, ref cycle);
    var size = lineMode ? (ushort)0x0002 : (ushort)0;
    WriteMachineWord(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Bltsize, size, ref cycle);
    if (lineMode)
        WriteMachineWord(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Bltsize, size, ref cycle);
    machine.Cpu.Cycles = Math.Max(machine.Cpu.Cycles, cycle);
}

static void WriteBlitterPointer(
    LightweightA500Machine machine,
    ushort highOffset,
    uint pointer,
    ref long cycle)
{
    WriteMachineWord(machine, LightweightA500Machine.CustomBase + highOffset,
        (ushort)(pointer >> 16), ref cycle);
    WriteMachineWord(machine, LightweightA500Machine.CustomBase + highOffset + 2u,
        (ushort)pointer, ref cycle);
}

static void WriteMachineWord(
    LightweightA500Machine machine,
    uint address,
    ushort value,
    ref long cycle)
    => machine.WriteWord(
        address,
        value,
        ref cycle,
        Copper68k.M68kBusAccessKind.CpuDataWrite);

static void ExecuteMeasuredFrame(
    LightweightA500Machine machine,
    bool serviceCiaTimer,
    bool serviceBlitter,
    bool descendingFill,
    bool lineMode,
    bool serviceSpriteDma,
    bool servicePaulaManual)
{
    machine.ExecuteFrame();
    if (serviceSpriteDma)
        RearmSyntheticSpriteDma(machine);
    if (serviceBlitter)
        RearmSyntheticBlitter(machine, descendingFill, lineMode);
    if (servicePaulaManual)
        RearmSyntheticPaulaManual(machine);
    if (!serviceCiaTimer)
        return;

    long cycle = machine.Cycle;
    _ = machine.ReadByte(0x00BFDD00, ref cycle, Copper68k.M68kBusAccessKind.CpuDataRead);
    machine.WriteWord(
        LightweightA500Machine.CustomBase + 0x09C,
        0x2000,
        ref cycle,
        Copper68k.M68kBusAccessKind.CpuDataWrite);
    machine.Cpu.Cycles = Math.Max(machine.Cpu.Cycles, cycle);
}

static byte[] CreateSyntheticRom(bool stopped)
{
    const int romBytes = 0x40000;
    const int codeOffset = 0x100;
    var rom = new byte[romBytes];
    WriteLong(rom, 0, 0x0007FFF0);
    WriteLong(rom, 4, 0x00FC0100);
    if (stopped)
    {
        WriteWord(rom, codeOffset, 0x4E72);
        WriteWord(rom, codeOffset + 2, 0x2700);
    }
    else
    {
        WriteWord(rom, codeOffset, 0x4E71);
        WriteWord(rom, codeOffset + 2, 0x60FC);
    }

    return rom;
}

static void WriteWord(Span<byte> bytes, int offset, ushort value)
{
    bytes[offset] = (byte)(value >> 8);
    bytes[offset + 1] = (byte)value;
}

static void WriteLong(Span<byte> bytes, int offset, uint value)
{
    bytes[offset] = (byte)(value >> 24);
    bytes[offset + 1] = (byte)(value >> 16);
    bytes[offset + 2] = (byte)(value >> 8);
    bytes[offset + 3] = (byte)value;
}

static byte[] ReadImage(string path, params string[] extensions)
{
    if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        return File.ReadAllBytes(path);

    using var archive = ZipFile.OpenRead(path);
    var entry = archive.Entries
        .Where(candidate => extensions.Any(extension => candidate.FullName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
        .OrderBy(candidate => candidate.FullName, StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault()
        ?? throw new InvalidDataException($"No {string.Join('/', extensions)} image was found in '{path}'.");
    using var stream = entry.Open();
    using var output = new MemoryStream(checked((int)entry.Length));
    stream.CopyTo(output);
    return output.ToArray();
}
