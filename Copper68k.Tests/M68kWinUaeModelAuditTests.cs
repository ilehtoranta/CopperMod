using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Copper68k.Tests.Synthetic;
using Xunit.Sdk;

namespace Copper68k.Tests;

public sealed partial class M68kWinUaeCpuTesterConformanceTests
{
    private const string ModelAuditRun = "COPPER68K_RUN_WINUAE_MODEL_AUDIT";
    internal const string GeneratorPin = "025b999239800357e95065fe5b9a15ea5b300fa7";
    internal const string RunnerPin = "7a83745d6c6159bc74ab0471578ffc8bc244e66e";

    [EnvironmentFact(ModelAuditRun, "audit pinned WinUAE integer fixtures across selected CPU profiles")]
    public void WinUaeIntegerFixturesAcrossSelectedModelsWhenEnabled()
    {
        var root = Environment.GetEnvironmentVariable("COPPER68K_WINUAE_MODEL_PATH");
        var library = Environment.GetEnvironmentVariable(LibraryVariable);
        var output = Environment.GetEnvironmentVariable("COPPER68K_SYNTHETIC_REPORT_DIR");
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(library) || string.IsNullOrWhiteSpace(output))
            throw new XunitException("Model audit requires fixture root, native library and report directory.");
        var selection = Environment.GetEnvironmentVariable("COPPER68K_SYNTHETIC_MODELS") ?? string.Join(',', ModelSpec.All.Select(m => m.Id));
        var ids = selection.Split(',', StringSplitOptions.TrimEntries);
        if (ids.Length == 0 || ids.Distinct().Count() != ids.Length || ids.Any(id => !ModelSpec.All.Any(m => m.Id == id)))
            throw new XunitException($"Empty, duplicate or unknown model selection: {selection}");
        var manifest = JsonSerializer.Deserialize<WinUaeManifest>(File.ReadAllText(Path.Combine(root, "manifest.json")))
            ?? throw new XunitException("Missing WinUAE input manifest.");
        if (manifest.Schema != 1 || manifest.GeneratorCommit != GeneratorPin || manifest.RunnerCommit != RunnerPin)
            throw new XunitException("WinUAE source identity does not match the qualified bridge pins.");
        if (!File.Exists(library) || Hash(library) != manifest.NativeLibrarySha256)
            throw new XunitException("WinUAE native library differs from the generated input manifest.");
        // Preflight every requested profile before loading native code, which can
        // terminate its process for malformed/missing binary fixture records.
        foreach (var id in ids) ValidateWinUaeProfile(root, manifest, ModelSpec.All.Single(m => m.Id == id));
        var rows = new List<WinUaeModelRow>();
        var probes = new List<WinUaeProbeRow>();
        Directory.CreateDirectory(output);
        using var tester = NativeTester.Load(library);
        foreach (var id in ids)
        {
            var model = ModelSpec.All.Single(m => m.Id == id);
            var fixture = manifest.Profiles.Single(p => p.Id == FixtureId(id));
            var path = Path.Combine(root, fixture.Id);
            // A native 'success' contract must detect a deliberately wrong result.
            var probe = tester.Run(path, "NOP", fixture.CpuLevel, false, false, model, corruptResult: true);
            var detected = !probe.Passed && probe.ExecutedCases > 0 && probe.Detail.Contains("WinUAE cputest reported failure", StringComparison.Ordinal);
            probes.Add(new(id, "register", detected, probe.ExecutedCases, probe.Detail));
            if (!detected) throw new XunitException($"Native register-corruption probe was not detected for {id}: {probe.Detail}");
            var srProbe = tester.Run(path, "NOP", fixture.CpuLevel, false, false, model, corruptSr: 0x10);
            var srDetected = !srProbe.Passed && srProbe.ExecutedCases > 0 && srProbe.Detail.Contains("SR:", StringComparison.Ordinal);
            probes.Add(new(id, "defined-sr", srDetected, srProbe.ExecutedCases, srProbe.Detail));
            if (!srDetected) throw new XunitException($"Native defined-SR corruption probe was not detected for {id}: {srProbe.Detail}");
            var frameProbe = tester.Run(path, "TRAP", fixture.CpuLevel, false, false, model, corruptFrame: true);
            var frameDetected = !frameProbe.Passed && tester.FrameChecks > 0 && frameProbe.Detail.Contains("frame byte", StringComparison.Ordinal);
            probes.Add(new(id, "exception-frame", frameDetected, frameProbe.ExecutedCases, frameProbe.Detail));
            if (!frameDetected) throw new XunitException($"Native exception-frame corruption probe was not detected for {id}: {frameProbe.Detail}");
            var undefinedProbe = tester.Run(path, "CHK.W", fixture.CpuLevel, false, false, model, corruptIgnoredSr: true);
            var undefinedVerified = undefinedProbe.Passed && undefinedProbe.ExecutedCases > 0 && tester.MaskedCases == undefinedProbe.ExecutedCases;
            probes.Add(new(id, "undefined-sr-ignored", undefinedVerified, undefinedProbe.ExecutedCases, undefinedProbe.Detail));
            if (!undefinedVerified) throw new XunitException($"Undefined-SR comparison control failed for {id}: {undefinedProbe.Detail}");
            foreach (var opcode in fixture.Opcodes)
            {
                try
                {
                    var result = tester.Run(path, opcode, fixture.CpuLevel, false, false, model);
                    rows.Add(new(id, opcode, result.Passed ? result.ExecutedCases > 0 ? "passing" : "untested" : tester.UnsupportedExecution ? "unsupported" : "mismatching",
                        result.ExecutedCases, result.UnmappedReads, result.UnmappedWrites, tester.FrameChecks, tester.MaskedCases, tester.TerminalCases, result.Detail));
                }
                catch (Exception ex) { rows.Add(new(id, opcode, "mismatching", 0, 0, 0, 0, 0, 0, ex.Message)); }
            }
            _output.WriteLine($"{id}: {rows.Count(r => r.Model == id && r.Status == "passing")} directories passed; {rows.Where(r => r.Model == id).Sum(r => (long)r.ExecutedCases)} callbacks executed.");
        }
        File.WriteAllText(Path.Combine(output, "winuae-model-audit.json"), JsonSerializer.Serialize(new
        {
            schema = 2, reference = "WinUAE generator through Copperline native assertion runner", selectedModels = ids,
            manifest.GeneratorCommit, manifest.RunnerCommit, manifest.NativeLibrarySha256,
            manifestSha256 = Hash(Path.Combine(root, "manifest.json")),
            adapterAssemblySha256 = Hash(typeof(M68kWinUaeCpuTesterConformanceTests).Assembly.Location),
            cpuAssemblySha256 = Hash(typeof(Copper68k.M68kCoreFactory).Assembly.Location),
            flagAuthority = "M68000PM 4-2, 4-69/71, 4-92/96, 4-141, 4-170; comparison masks only, actual results unchanged",
            qualification = "Pinned software assertions; integer state, independently defined SR masks, memory and Basic format 0/2/3/4 exception frames (68000 six-byte frames). Other exception records fail instead of being skipped. Basic preset omits bus/address faults, trace/M rounds, physical timing, cache/MMU/FPU and advanced RTE restart protocols. Callbacks differ from generator candidate counts.",
            passing = rows.Count(r => r.Status == "passing"), mismatching = rows.Count(r => r.Status == "mismatching"),
            unsupported = rows.Count(r => r.Status == "unsupported"),
            untested = rows.Count(r => r.Status == "untested"), executedCases = rows.Sum(r => (long)r.ExecutedCases),
            exceptionFrames = rows.Sum(r => (long)r.ExceptionFrames), maskedSrCases = rows.Sum(r => (long)r.MaskedSrCases),
            terminalCases = rows.Sum(r => (long)r.TerminalCases), probes, rows
        }, new JsonSerializerOptions { WriteIndented = true }));
        var failures = rows.Where(r => r.Status != "passing").ToArray();
        Assert.True(failures.Length == 0, string.Join(Environment.NewLine, failures.Select(r => $"{r.Model}/{r.Opcode}: {r.Detail}")));
    }

    private static string FixtureId(string id) => id == "A1200" ? "68EC020" : id;
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    internal static void ValidateWinUaeProfile(string root, WinUaeManifest manifest, ModelSpec model, bool traceTraps = false, string[]? requiredOpcodes = null)
    {
        var fixture = manifest.Profiles.SingleOrDefault(p => p.Id == FixtureId(model.Id))
            ?? throw new XunitException($"Missing WinUAE profile: {model.Id}");
        var level = model.Id switch { "68000" => 0, "68010" => 1, "68030" => 3, "68040" => 4, "68060" => 5, _ => 2 };
        var cpu = level == 5 ? "68060" : $"{68000 + level * 10}";
        var requiredCount = requiredOpcodes?.Length ?? (traceTraps ? 1 : model.Id switch { "68000" => 146, "68010" => 153, "68030" => 180, "68040" => 181, "68060" => 184, _ => 179 });
        if (fixture.CpuLevel != level || fixture.CpuDirectory != cpu || fixture.AddressBits != model.AddressBits ||
            fixture.Opcodes.Length != requiredCount || fixture.Opcodes.Distinct().Count() != requiredCount || (requiredOpcodes is null ? !fixture.Opcodes.Contains(traceTraps ? "TRAP" : "NOP") : !fixture.Opcodes.Order().SequenceEqual(requiredOpcodes.Order())))
            throw new XunitException($"Incomplete or incompatible WinUAE profile: {model.Id}");
        var path = Path.Combine(root, fixture.Id, cpu);
        foreach (var memory in new[] { ("lmem.dat", 0x8000), ("hmem.dat", 0x8000), ("tmem.dat", 0x40000) })
        {
            var file = Path.Combine(path, memory.Item1);
            if (!File.Exists(file) || new FileInfo(file).Length != memory.Item2)
                throw new XunitException($"Missing or incomplete WinUAE memory image: {model.Id}/{memory.Item1}");
        }
        var actual = Directory.GetDirectories(path).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(fixture.Opcodes.OrderBy(n => n, StringComparer.Ordinal)))
            throw new XunitException($"Changed WinUAE directory selection: {model.Id}");
        if (fixture.Inputs.Length == 0 || fixture.Inputs.Select(i => i.Path).Distinct().Count() != fixture.Inputs.Length)
            throw new XunitException($"Empty or duplicate WinUAE input selection: {model.Id}");
        var actualInputs = Directory.GetFiles(path, "*.dat", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(Path.Combine(root, fixture.Id), p).Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal);
        if (!actualInputs.SequenceEqual(fixture.Inputs.Select(i => i.Path).OrderBy(p => p, StringComparer.Ordinal)))
            throw new XunitException($"Changed WinUAE input selection: {model.Id}");
        foreach (var input in fixture.Inputs)
        {
            var file = Path.Combine(root, fixture.Id, input.Path);
            if (new FileInfo(file).Length != input.Bytes || Hash(file) != input.Sha256)
                throw new XunitException($"Changed WinUAE fixture: {model.Id}/{input.Path}");
        }
        foreach (var opcode in fixture.Opcodes)
        {
            var directory = Path.Combine(path, opcode);
            var files = Directory.GetFiles(directory, "*.dat").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            if (files.Length < 2 || !files.SequenceEqual(Enumerable.Range(0, files.Length).Select(n => $"{n:D4}.dat")))
                throw new XunitException($"Missing WinUAE header/data sequence: {model.Id}/{opcode}");
            var header = File.ReadAllBytes(Path.Combine(directory, "0000.dat"));
            if (header.Length < 98 || BinaryPrimitives.ReadUInt32BigEndian(header) != 16)
                throw new XunitException($"Invalid WinUAE header: {model.Id}/{opcode}");
            var flags = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(24));
            if (((flags >> 16) & 15) != level || ((flags & 0x80000000) == 0 ? 24 : 32) != model.AddressBits)
                throw new XunitException($"Incompatible WinUAE model/address width: {model.Id}/{opcode}");
            foreach (var dataName in files.Skip(1))
            {
                var data = File.ReadAllBytes(Path.Combine(directory, dataName!));
                if (data.Length <= 16 || !data.AsSpan(0, 8).SequenceEqual(header.AsSpan(0, 8)))
                    throw new XunitException($"Incomplete or incompatible WinUAE data: {model.Id}/{opcode}/{dataName}");
            }
        }
    }

    internal sealed record WinUaeManifest(int Schema, string GeneratorCommit, string RunnerCommit, string NativeLibrarySha256, WinUaeProfile[] Profiles);
    internal sealed record WinUaeProfile(string Id, string CpuDirectory, byte CpuLevel, int AddressBits, string[] Opcodes, WinUaeInput[] Inputs);
    internal sealed record WinUaeInput(string Path, long Bytes, string Sha256);
    private sealed record WinUaeModelRow(string Model, string Opcode, string Status, int ExecutedCases, int UnmappedReads, int UnmappedWrites, uint ExceptionFrames, uint MaskedSrCases, int TerminalCases, string Detail);
    private sealed record WinUaeProbeRow(string Model, string Kind, bool Detected, int ExecutedCases, string Detail);
}
