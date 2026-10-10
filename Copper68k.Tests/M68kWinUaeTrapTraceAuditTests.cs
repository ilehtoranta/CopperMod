using System.Text.Json;
using Copper68k.Tests.Synthetic;
using Xunit.Sdk;

namespace Copper68k.Tests;

public sealed partial class M68kWinUaeCpuTesterConformanceTests
{
    private const string TracePresetNormalizedSourceHash = "863cc62fb5f3aa0b0a59a4380f2240e7f853e1207188015c086adb7f56050a49";
    private const string TracePresetPatchHash = "c8d648de711ba31f4340c911b3ddd91e6ea883375329a84eecdf5b537c076ade";

    [EnvironmentFact("COPPER68K_RUN_WINUAE_TRAP_TRACE_AUDIT", "audit qualified WinUAE TRAP trace priority on 040/060")]
    public void WinUaeTrapTracePriorityAcross040And060WhenEnabled()
    {
        var root = Environment.GetEnvironmentVariable("COPPER68K_WINUAE_TRAP_TRACE_PATH");
        var library = Environment.GetEnvironmentVariable(LibraryVariable);
        var output = Environment.GetEnvironmentVariable("COPPER68K_SYNTHETIC_REPORT_DIR");
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(library) || string.IsNullOrWhiteSpace(output))
            throw new XunitException("Trace audit requires qualified fixture root, native library and report directory.");
        var manifestFile = Path.Combine(root, "manifest.json");
        var json = File.ReadAllText(manifestFile);
        var manifest = JsonSerializer.Deserialize<WinUaeManifest>(json) ?? throw new XunitException("Missing trace input manifest.");
        using var identity = JsonDocument.Parse(json);
        var fields = identity.RootElement;
        var sourcePath = Path.Combine(root, "cputest-trace.cpp");
        var normalizedSourceHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(sourcePath).Replace("\r\n", "\n")))).ToLowerInvariant();
        if (manifest.Schema != 1 || manifest.GeneratorCommit != GeneratorPin || manifest.RunnerCommit != RunnerPin ||
            fields.GetProperty("Preset").GetString() != "TraceTraps" ||
            normalizedSourceHash != TracePresetNormalizedSourceHash ||
            fields.GetProperty("TracePriorityPatchSha256").GetString() != TracePresetPatchHash ||
            Hash(sourcePath) != fields.GetProperty("TracePrioritySourceSha256").GetString() ||
            Hash(Path.Combine(root, "trace-priority.patch")) != TracePresetPatchHash ||
            Hash(Path.Combine(root, "cputester.exe")) != fields.GetProperty("GeneratorExecutableSha256").GetString() ||
            Hash(library) != manifest.NativeLibrarySha256)
            throw new XunitException("Trace preset source, generator, patch or native identity is unqualified.");
        if (manifest.Profiles.Length != 2 || !manifest.Profiles.Select(x => x.Id).Order().SequenceEqual(new[] { "68040", "68060" }))
            throw new XunitException("Trace audit requires both 040 and 060, without empty or duplicate selections.");
        var models = ModelSpec.All.Where(x => x.Id is "68040" or "68060").ToArray();
        foreach (var model in models) ValidateWinUaeProfile(root, manifest, model, traceTraps: true);
        var rows = new List<WinUaeTrapTraceRow>();
        var failures = new List<string>();
        using var tester = NativeTester.Load(library);
        foreach (var model in models)
        {
            var fixture = manifest.Profiles.Single(x => x.Id == model.Id);
            var path = Path.Combine(root, model.Id);
            // The focused preset must still compare registers, defined X and frames.
            var registerProbe = tester.Run(path, "TRAP", fixture.CpuLevel, false, false, model, corruptResult: true);
            var srProbe = tester.Run(path, "TRAP", fixture.CpuLevel, false, false, model, corruptSr: 0x10);
            var frameProbe = tester.Run(path, "TRAP", fixture.CpuLevel, false, false, model, corruptFrame: true);
            var frameDetected = !frameProbe.Passed && tester.FrameChecks > 0 && frameProbe.Detail.Contains("frame byte", StringComparison.Ordinal);
            var controls = !registerProbe.Passed && registerProbe.ExecutedCases > 0 &&
                !srProbe.Passed && srProbe.ExecutedCases > 0 && srProbe.Detail.Contains("SR:", StringComparison.Ordinal) && frameDetected;
            var result = tester.Run(path, "TRAP", fixture.CpuLevel, false, false, model);
            var passing = controls && result.Passed && result.ExecutedCases == 256 && tester.TraceInputCases == 128 && tester.FrameChecks == 256;
            rows.Add(new(model.Id, "TRAP", passing ? "passing" : tester.UnsupportedExecution ? "unsupported" : "mismatching",
                result.ExecutedCases, tester.TraceInputCases, tester.FrameChecks, controls, result.Detail));
            if (!passing) failures.Add($"{model.Id}: controls={controls}, traced={tester.TraceInputCases}, frames={tester.FrameChecks}; {result.Detail}");
            _output.WriteLine($"{model.Id}: {result.ExecutedCases} TRAP callbacks, {tester.TraceInputCases} with incoming T1, {tester.FrameChecks} frame checks.");
        }
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "winuae-trap-trace-audit.json"), JsonSerializer.Serialize(new
        {
            schema = 1, reference = "WinUAE TRAP trace preset with explicit Motorola-qualified generator correction",
            manifest.GeneratorCommit, manifest.RunnerCommit, manifest.NativeLibrarySha256,
            manifestSha256 = Hash(manifestFile), generatorSourceSha256 = Hash(sourcePath),
            generatorNormalizedSourceSha256 = TracePresetNormalizedSourceHash, generatorPatchSha256 = TracePresetPatchHash,
            adapterAssemblySha256 = Hash(typeof(M68kWinUaeCpuTesterConformanceTests).Assembly.Location),
            cpuAssemblySha256 = Hash(typeof(Copper68k.M68kCoreFactory).Assembly.Location),
            qualification = "TRAP only, 040/060, incoming T1/S and CCR 0/31; frame 0 and defined integer effects. Patched reference, not unchanged upstream or silicon qualification. Other traced families/models remain untested by this preset.",
            passing = rows.Count(x => x.Status == "passing"), mismatching = rows.Count(x => x.Status == "mismatching"),
            unsupported = rows.Count(x => x.Status == "unsupported"), executedCases = rows.Sum(x => x.ExecutedCases),
            traceInputCases = rows.Sum(x => x.TraceInputCases), exceptionFrames = rows.Sum(x => (long)x.ExceptionFrames),
            rows
        }, new JsonSerializerOptions { WriteIndented = true }));
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private sealed record WinUaeTrapTraceRow(string Model, string Family, string Status, int ExecutedCases,
        int TraceInputCases, uint ExceptionFrames, bool Controls, string Detail);
}
