using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class OcsRegressionFixtureTests
{
    private const uint CustomBase = 0x00DFF000;

    [Fact]
    public void ExplicitOcsPalMatchesLegacyConstructorAcrossRegistersGrantsPixelsAndWaits()
    {
        var legacy = Capture(new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true));
        var explicitOcs = Capture(new AmigaBus(
            captureBusAccesses: true,
            enableLiveAgnusDma: true,
            chipset: AmigaChipset.OcsPal));

        Assert.Equal(legacy.RegisterTrace, explicitOcs.RegisterTrace);
        Assert.Equal(legacy.BusGrants, explicitOcs.BusGrants);
        Assert.Equal(legacy.CpuWaitCycles, explicitOcs.CpuWaitCycles);
        Assert.Equal(legacy.FrameHash, explicitOcs.FrameHash);

        var firstDifference = FindFirstDifference(legacy.Frame, explicitOcs.Frame);
        var differenceMessage = firstDifference < 0
            ? string.Empty
            : $"first framebuffer difference at pixel {firstDifference}: " +
                $"legacy=0x{legacy.Frame[firstDifference]:X8},explicit=0x{explicitOcs.Frame[firstDifference]:X8}";
        Assert.True(firstDifference < 0, differenceMessage);
    }

    private static OcsCapture Capture(AmigaBus bus)
    {
        bus.ChipRam[0x2000] = 0xAA;
        bus.ChipRam[0x2001] = 0x55;
        bus.WriteWord(CustomBase + 0x0E0, 0x0000, 0);
        bus.WriteWord(CustomBase + 0x0E2, 0x2000, 0);
        bus.WriteWord(CustomBase + 0x180, 0x0123, 0);
        bus.WriteWord(CustomBase + 0x182, 0x0FED, 0);
        bus.WriteWord(CustomBase + 0x100, 0x1200, 0);
        bus.WriteWord(CustomBase + 0x096, 0x8300, 0);

        var cpuCycle = 64L;
        bus.WriteWord(0x00003000, 0xC0DE, ref cpuCycle, AmigaBusAccessKind.CpuDataWrite);
        _ = bus.ReadWord(0x00003000, ref cpuCycle, AmigaBusAccessKind.CpuDataRead);
        _ = bus.ReadWord(CustomBase + 0x006, ref cpuCycle, AmigaBusAccessKind.CpuDataRead);

        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
        bus.Display.RenderFrame(frame, 0, AmigaConstants.A500PalCpuCyclesPerFrame);

        var registerTrace = bus.CustomRegisterWrites
            .Select(write => (write.Cycle, write.Address, write.Value))
            .ToArray();
        var grants = bus.BusAccesses
            .Select(access => (
                access.Request.Requester,
                access.Request.Kind,
                access.Request.Target,
                access.Request.Address,
                access.Request.Size,
                access.RequestedCycle,
                access.GrantedCycle,
                access.CompletedCycle))
            .ToArray();
        var waits = bus.BusAccesses
            .Where(access => access.Request.Requester == AmigaBusRequester.Cpu)
            .Select(access => access.WaitCycles)
            .ToArray();

        return new OcsCapture(registerTrace, grants, waits, Hash(frame), frame);
    }

    private static ulong Hash(ReadOnlySpan<uint> pixels)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        foreach (var pixel in pixels)
        {
            hash = (hash ^ pixel) * prime;
        }

        return hash;
    }

    private static int FindFirstDifference(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right)
    {
        var length = Math.Min(left.Length, right.Length);
        for (var index = 0; index < length; index++)
        {
            if (left[index] != right[index])
            {
                return index;
            }
        }

        return left.Length == right.Length ? -1 : length;
    }

    private sealed record OcsCapture(
        (long Cycle, ushort Address, ushort Value)[] RegisterTrace,
        (AmigaBusRequester Requester, AmigaBusAccessKind Kind, AmigaBusAccessTarget Target,
            uint Address, AmigaBusAccessSize Size, long Requested, long Granted, long Completed)[] BusGrants,
        long[] CpuWaitCycles,
        ulong FrameHash,
        uint[] Frame);
}
