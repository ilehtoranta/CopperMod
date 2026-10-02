using System.Security.Cryptography;
using System.Text.Json;
using CopperMod.Sid;

public sealed class HardwareDigitalEvidenceTests
{
    [HardwareEvidenceFact]
    public void CapturedReadsMatchAtTheirRecordedCycles()
    {
        var root = Environment.GetEnvironmentVariable("SID_HARDWARE_EVIDENCE_ROOT");
        Assert.False(string.IsNullOrWhiteSpace(root), "Set SID_HARDWARE_EVIDENCE_ROOT to a measured evidence package.");
        var manifestPath = Path.Combine(root!, "manifest.json");
        Assert.True(File.Exists(manifestPath), "Missing hardware evidence manifest: " + manifestPath);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            RespectRequiredConstructorParameters = true
        };
        var manifest = JsonSerializer.Deserialize<EvidenceManifest>(File.ReadAllText(manifestPath), options)!;
        Assert.Equal(1, manifest.Schema);
        Assert.Equal("hardware", manifest.Authority);
        Assert.Equal("cpu-bus-end-write-next-clock-v1", manifest.PhaseConvention);
        Assert.False(string.IsNullOrWhiteSpace(manifest.Specimen));
        Assert.False(string.IsNullOrWhiteSpace(manifest.Revision));
        Assert.False(string.IsNullOrWhiteSpace(manifest.BoardCoupling));
        Assert.False(string.IsNullOrWhiteSpace(manifest.CaptureMethod));
        Assert.InRange(manifest.ClockHz, 900000, 1100000);
        Assert.InRange(manifest.SupplyVolts, 10, 14);
        Assert.InRange(manifest.TemperatureC, -20, 100);
        Assert.InRange(manifest.FilterCapacitancePf, 1, 10000);
        Assert.True(manifest.OutputLoadOhms > 0);
        Assert.NotEmpty(manifest.Cases);
        foreach (var coverage in new[] { "envelope", "osc3", "test-noise", "ring-sync", "open-bus" })
            Assert.Contains(manifest.Cases, c => c.Coverage == coverage);

        foreach (var fixture in manifest.Cases)
        {
            var path = Path.GetFullPath(Path.Combine(root!, fixture.File));
            var relative = Path.GetRelativePath(Path.GetFullPath(root!), path);
            Assert.False(Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar),
                "Evidence cases must stay inside the evidence package.");
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(fixture.Sha256.ToLowerInvariant(), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            var events = JsonSerializer.Deserialize<BusEvent[]>(bytes, options)!;
            Assert.Contains(events, e => e.Operation == "read");
            Assert.Contains(events, e => e.Operation == "write");
            foreach (var profile in new[] { SidEmulationProfile.Balanced, SidEmulationProfile.ReferenceMeasured })
            foreach (var traced in new[] { false, true })
            {
                var sid = new SidSystem([new SidChipPlacement(0, 0xd400)], SidChipModel.Mos6581,
                    cpuCyclesPerSecond: manifest.ClockHz, sidEmulationProfile: profile);
                sid.Reset();
                if (traced) sid.Trace = new SidCycleTrace();
                long previous = 0;
                foreach (var busEvent in events)
                {
                    Assert.InRange(busEvent.Cycle, previous, long.MaxValue);
                    Assert.InRange(busEvent.Register, 0, 31);
                    Assert.InRange(busEvent.Value, 0, 255);
                    previous = busEvent.Cycle;
                    var address = (ushort)(0xd400 + busEvent.Register);
                    if (busEvent.Operation == "write") sid.TryWrite(address, (byte)busEvent.Value, busEvent.Cycle);
                    else
                    {
                        Assert.Equal("read", busEvent.Operation);
                        sid.TryRead(address, busEvent.Cycle, out var actual);
                        Assert.True(actual == busEvent.Value,
                            $"{fixture.File}, {profile}, trace={traced}, cycle={busEvent.Cycle}, register=${busEvent.Register:X2}: expected ${busEvent.Value:X2}, actual ${actual:X2}. " +
                            JsonSerializer.Serialize(sid.GetRegisterChipDebugState(0), options));
                    }
                }
            }
        }
    }

    public sealed record EvidenceManifest(int Schema, string Authority, string PhaseConvention,
        string Specimen, string Revision, string BoardCoupling, string CaptureMethod,
        int ClockHz, double SupplyVolts, double TemperatureC, double FilterCapacitancePf,
        double OutputLoadOhms, EvidenceCase[] Cases);
    public sealed record EvidenceCase(string File, string Sha256, string Coverage);
    public sealed record BusEvent(long Cycle, string Operation, int Register, int Value);
}

public sealed class HardwareEvidenceFactAttribute : FactAttribute
{
    public HardwareEvidenceFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SID_ACCURACY_REQUIRED") != "1" &&
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SID_HARDWARE_EVIDENCE_ROOT")))
            Skip = "Cycle-indexed physical SID captures are unavailable; this is not a hardware accuracy pass.";
    }
}
