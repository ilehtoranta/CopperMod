using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;

namespace CopperMod.Sid.Benchmarks;

[MemoryDiagnoser]
[DisassemblyDiagnoser(maxDepth: 3, exportCombinedDisassemblyReport: true)]
public class SidChipRenderBenchmarks
{
    private const int CyclesPerInvocation = SidConstants.PalCyclesPerFrame;

    private SidChip _mos6581 = null!;
    private SidChip _mos8580 = null!;
    private long _mos6581Cycle;
    private long _mos8580Cycle;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _mos6581 = CreateChip(SidChipModel.Mos6581, SidFilterProfileId.Mos6581Balanced);
        _mos8580 = CreateChip(SidChipModel.Mos8580, SidFilterProfileId.Mos8580Linear);
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = CyclesPerInvocation)]
    public double Mos6581BalancedRenderFrame()
    {
        return RenderFrame(_mos6581, ref _mos6581Cycle);
    }

    [Benchmark(OperationsPerInvoke = CyclesPerInvocation)]
    public double Mos8580LinearRenderFrame()
    {
        return RenderFrame(_mos8580, ref _mos8580Cycle);
    }

    private static SidChip CreateChip(SidChipModel model, SidFilterProfileId filterProfile)
    {
        var chip = new SidChip(
            model,
            SidConstants.DefaultSidBaseAddress,
            SidConstants.PalCpuCyclesPerSecond,
            filterProfile);

        WriteVoice(chip, registerOffset: 0x00, frequency: 0x1800, pulseWidth: 0x0800, control: 0x21);
        WriteVoice(chip, registerOffset: 0x07, frequency: 0x2435, pulseWidth: 0x0400, control: 0x41);
        WriteVoice(chip, registerOffset: 0x0E, frequency: 0x30A0, pulseWidth: 0x0A00, control: 0x81);

        chip.Write(0x15, 0x07);
        chip.Write(0x16, 0x80);
        chip.Write(0x17, 0xF7);
        chip.Write(0x18, 0x1F);
        chip.Render(128);
        return chip;
    }

    private static void WriteVoice(SidChip chip, byte registerOffset, ushort frequency, ushort pulseWidth, byte control)
    {
        chip.Write((byte)(registerOffset + 0), (byte)frequency);
        chip.Write((byte)(registerOffset + 1), (byte)(frequency >> 8));
        chip.Write((byte)(registerOffset + 2), (byte)pulseWidth);
        chip.Write((byte)(registerOffset + 3), (byte)(pulseWidth >> 8));
        chip.Write((byte)(registerOffset + 4), control);
        chip.Write((byte)(registerOffset + 5), 0x09);
        chip.Write((byte)(registerOffset + 6), 0xA4);
    }

    private static double RenderFrame(SidChip chip, ref long cycle)
    {
        var firstCycle = cycle;
        cycle += CyclesPerInvocation;
        return chip.RenderAndSumFast(firstCycle, CyclesPerInvocation);
    }
}
