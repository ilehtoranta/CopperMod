using Xunit.Abstractions;

namespace CopperMod.Amiga.Tests;

/// <summary>
/// Opt-in acceptance gate for a legally obtained A1200 Kickstart 3.0 ROM.
/// The ROM is intentionally neither committed nor identified by a fixed hash.
/// </summary>
public sealed class A1200Kickstart30CorpusTests
{
    private const string RomPathVariable = "COPPER_AMIGA_KICKSTART_ROM";
    private const string RomVersionVariable = "COPPER_AMIGA_KICKSTART_VERSION";
    private readonly ITestOutputHelper _output;

    public A1200Kickstart30CorpusTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData((int)MachineProfile.A1200AgaPal)]
    [InlineData((int)MachineProfile.A1200AgaNtsc)]
    public void Revision39106ReachesAnAnimatedEmptyDiskPrompt(int profileValue)
    {
        if (!TryLoadRom(out var rom, out var path))
        {
            return;
        }

        Assert.True(rom.Length >= 16, "Kickstart ROM is too short to contain its resident metadata.");
        Assert.Equal(39, BigEndian.ReadUInt16(rom, 12, "Kickstart version"));
        Assert.Equal(106, BigEndian.ReadUInt16(rom, 14, "Kickstart revision"));

        var profile = (MachineProfile)profileValue;
        using var machine = new Machine(MachineOptions.ForProfile(profile)
            .WithKickstart(KickstartConfiguration.FromRomImage(KickstartVersion.Kickstart30, rom)));
        var boot = new AmigaBootController(machine);
        boot.StartKickstartRomBoot();

        var timing = RasterTiming.For(machine.Options.Chipset.VideoStandard);
        var frameCycles = timing.GetFrameCycles(timing.LongFrameLines);
        var pixels = new uint[machine.Bus.Display.Width * machine.Bus.Display.Height];
        var recentHashes = new Queue<ulong>();
        var observedProgramCounters = new HashSet<uint>();
        var observedCopperPointers = new HashSet<uint>();
        const int maximumFrames = 600;

        for (var frame = 0; frame < maximumFrames; frame++)
        {
            var frameStart = frame * frameCycles;
            var frameStop = frameStart + frameCycles;
            machine.Bus.Display.BeginPresentationFrame(
                new PresentationFrameTarget(pixels), frameStart, frameStop);
            var result = boot.ContinueExecutionUntilCycle(frameStop, maxInstructions: 250_000);
            machine.Bus.Display.CompletePresentationFrame(frameStop);

            Assert.Empty(result.Diagnostics);
            Assert.False(machine.Cpu.State.Halted);
            Assert.Empty(machine.Bus.AgaUnsupportedFeatureTrace);
            observedProgramCounters.Add(machine.Cpu.State.ProgramCounter);
            observedCopperPointers.Add(machine.Bus.AgnusRegisters.CopperListPointer1);

            recentHashes.Enqueue(HashFrame(pixels));
            if (recentHashes.Count > 90)
            {
                recentHashes.Dequeue();
            }

            // The disk prompt has a stable background and a periodically changing
            // disk/drive animation. Requiring multiple late-frame phases avoids
            // accepting a static early-boot display.
            if (frame >= 120 && recentHashes.Distinct().Count() >= 3)
            {
                _output.WriteLine($"{profile} reached animated display phases after {frame + 1} frames using {path}.");
                Assert.True(observedProgramCounters.Count > 8, "CPU did not make sustained progress.");
                Assert.True(observedCopperPointers.Count > 1, "Copper list did not progress.");
                return;
            }
        }

        Assert.Fail($"{profile} did not reach an animated empty-disk display within {maximumFrames} frames.");
    }

    private static bool TryLoadRom(out byte[] rom, out string path)
    {
        rom = Array.Empty<byte>();
        path = Environment.GetEnvironmentVariable(RomPathVariable) ?? string.Empty;
        var version = Environment.GetEnvironmentVariable(RomVersionVariable);
        if (string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new InvalidOperationException($"{RomPathVariable} must name an existing Kickstart ROM.");
        }

        if (!string.Equals(version?.Trim(), "3.0", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{RomVersionVariable} must be exactly '3.0'.");
        }

        rom = File.ReadAllBytes(path);
        return true;
    }

    private static ulong HashFrame(ReadOnlySpan<uint> pixels)
    {
        var hash = 14695981039346656037UL;
        foreach (var pixel in pixels)
        {
            hash ^= pixel;
            hash *= 1099511628211UL;
        }
        return hash;
    }
}
