using Xunit.Sdk;

namespace Copper68k.Tests;

public sealed partial class M68kWinUaeCpuTesterConformanceTests
{
    [EnvironmentFact("COPPER68K_RUN_WINUAE_LPSTOP_AUDIT", "audit qualified WinUAE LPSTOP encoding and privilege exception frames")]
    public void WinUaeLowPowerStopExceptionsWhenEnabled() => RunQualifiedExceptionPreset(new(
        "LowPowerStop", "LPSTOP", "gencpu-lpstop.cpp", "lpstop-fetch-pc.patch", "LowPowerStop",
        "69ceec2d63bf35e27990142ca2e9e72f9c36dd2f4fba5113d17552c1ccbc35ca", "64cb8ff58d5a3a4fb4f217e3327fb1748d73f85521e3c9be98af7fd08f3a47bf",
        "winuae-lpstop-audit.json", ["68060"], ["68060"], _ => ["LPSTOP"], (_, _) => (245760, 245760),
        "MC68060UM D-19/20 and 8.2.4/5: fixed F800/01C0 encoding, malformed second-word Line-F priority, original SR and opcode-PC privilege frames. A copied generator uses the existing nonadvancing instruction-fetch helper for LPSTOP fixed offsets; get_wordi_test advances PC as a side effect, corrupting the second fetch address and saved PC. Basic and CPU results are unchanged. One seeded round, incoming CCR 0/31 and user/supervisor, no incoming trace. The generator skips stopped outcomes: this preset qualifies exceptions only; stopped state, full CCR, trace and complete malformed-word enumeration remain synthetic coverage. No physical broadcast/pin/timing qualification.",
        FormCounts: (_, _) => 14, ClassifyWords: (_, opcode, extension, immediate, sr, _) => QualifyLowPowerStopEncoding(opcode, extension, immediate, sr),
        ExpectedForms: (_, _) => LowPowerStopFormDistribution()));

    internal static IReadOnlyDictionary<string, int> LowPowerStopFormDistribution()
    {
        // Pinned one-round corpus: extension recognition, immediate S and
        // incoming S/CCR are reported separately. Stopped candidates are skipped.
        var forms = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var sr in new ushort[] { 0, 0x001f, 0x2000, 0x201f })
        foreach (var immediateS in new[] { 0, 1 })
        {
            forms[$"LPSTOP/encoding=unrecognized/immediateS={immediateS}/sr={sr:X4}"] = 24576;
            if ((sr & 0x2000) == 0 || immediateS == 0)
                forms[$"LPSTOP/encoding=recognized/immediateS={immediateS}/sr={sr:X4}"] = 8192;
        }
        return forms;
    }

    internal static string QualifyLowPowerStopEncoding(ushort opcode, ushort extension, ushort immediate, ushort inputSr)
    {
        if (opcode != 0xf800 || inputSr is not (0x0000 or 0x001f or 0x2000 or 0x201f))
            throw new XunitException($"Unqualified LPSTOP encoding/profile: {opcode:X4}/{extension:X4}/{immediate:X4}/{inputSr:X4}");
        if (extension == 0x01c0 && (inputSr & 0x2000) != 0 && (immediate & 0x2000) != 0)
            throw new XunitException("Stopped LPSTOP outcomes are absent from the qualified exception preset.");
        return $"LPSTOP/encoding={(extension == 0x01c0 ? "recognized" : "unrecognized")}/immediateS={(immediate >> 13) & 1}/sr={inputSr:X4}";
    }
}

public sealed class M68kWinUaeLowPowerStopEncodingTests
{
    [Fact]
    public void PinnedExceptionDistributionIncludesBothPrivilegeStatesAndExcludesStoppedForms()
    {
        var forms = M68kWinUaeCpuTesterConformanceTests.LowPowerStopFormDistribution();
        Assert.Equal(14, forms.Count);
        Assert.Equal(245760, forms.Values.Sum());
        Assert.Equal(196608, forms.Where(x => x.Key.Contains("encoding=unrecognized/", StringComparison.Ordinal)).Sum(x => x.Value));
        Assert.DoesNotContain("LPSTOP/encoding=recognized/immediateS=1/sr=201F", forms.Keys);
    }
    [Theory]
    [InlineData(0xf800, 0x01c0, 0x0000, 0x0000, "LPSTOP/encoding=recognized/immediateS=0/sr=0000")]
    [InlineData(0xf800, 0x01c0, 0x271f, 0x001f, "LPSTOP/encoding=recognized/immediateS=1/sr=001F")]
    [InlineData(0xf800, 0x01c0, 0x071f, 0x201f, "LPSTOP/encoding=recognized/immediateS=0/sr=201F")]
    [InlineData(0xf800, 0x01c1, 0x271f, 0x2000, "LPSTOP/encoding=unrecognized/immediateS=1/sr=2000")]
    [InlineData(0xf800, 0xffff, 0xffff, 0x001f, "LPSTOP/encoding=unrecognized/immediateS=1/sr=001F")]
    public void FixedManualExamplesDistinguishEncodingAndPrivilege(int opcode, int extension, int immediate, int sr, string expected) =>
        Assert.Equal(expected, M68kWinUaeCpuTesterConformanceTests.QualifyLowPowerStopEncoding((ushort)opcode, (ushort)extension, (ushort)immediate, (ushort)sr));

    [Theory]
    [InlineData(0xf801, 0x01c0, 0, 0)]
    [InlineData(0xf800, 0x01c0, 0, 0xa000)]
    [InlineData(0xf800, 0x01c0, 0, 0x0001)]
    [InlineData(0xf800, 0x01c0, 0x2000, 0x2000)]
    public void ForeignOpcodesTraceCcrAndStoppedOutcomesAreRejected(int opcode, int extension, int immediate, int sr) =>
        Assert.Throws<XunitException>(() => M68kWinUaeCpuTesterConformanceTests.QualifyLowPowerStopEncoding((ushort)opcode, (ushort)extension, (ushort)immediate, (ushort)sr));
}
