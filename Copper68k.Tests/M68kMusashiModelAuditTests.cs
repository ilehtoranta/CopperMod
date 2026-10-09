using System.Security.Cryptography;
using System.Text.Json;
using Copper68k.Tests.Synthetic;
using Xunit.Sdk;

namespace Copper68k.Tests;

public sealed partial class M68kMusashiConformanceTests
{
    private const string ModelAuditRun = "COPPER68K_RUN_MUSASHI_MODEL_AUDIT";

    [EnvironmentFact(ModelAuditRun, "audit pinned Musashi integer programs across selected CPU profiles")]
    public void MusashiIntegerProgramsAcrossSelectedModelsWhenEnabled()
    {
        var root = Environment.GetEnvironmentVariable("COPPER68K_MUSASHI_MODEL_AUDIT_PATH");
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) throw new XunitException("Model audit requires the Musashi repository root.");
        var selection = Environment.GetEnvironmentVariable("COPPER68K_SYNTHETIC_MODELS") ?? string.Join(',', ModelSpec.All.Select(m => m.Id));
        var selected = selection.Split(',', StringSplitOptions.TrimEntries);
        if (selected.Length == 0 || selected.Distinct().Count() != selected.Length || selected.Any(id => !ModelSpec.All.Any(m => m.Id == id)))
            throw new XunitException($"Empty, duplicate or unknown audit model selection: '{selection}'.");
        var rows = new List<ProgramAuditRow>();
        foreach (var id in selected)
        {
            var model = ModelSpec.All.Single(m => m.Id == id);
            foreach (var directory in new[] { "mc68000", "mc68040" })
            {
                var path = Path.Combine(root, "test", directory);
                if (!Directory.Exists(path)) throw new XunitException($"Missing reference fixtures: {path}");
                var files = Directory.GetFiles(path, "*.bin").OrderBy(Path.GetFileName, StringComparer.Ordinal).ToArray();
                var requiredCount = directory == "mc68000" ? 60 : 18;
                if (files.Length != requiredCount) throw new XunitException($"Incomplete pinned reference fixture selection: {path}; expected {requiredCount}, found {files.Length}.");
                foreach (var file in files)
                {
                    var name = Path.GetFileName(file);
                    var reason = ReferenceExclusion(model, directory, name);
                    var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
                    if (reason != null) { rows.Add(new(id, $"{directory}/{name}", hash, "excluded", 0, reason)); continue; }
                    try
                    {
                        var instructions = RunProgram(file, DefaultMaxInstructions, model.Model, MusashiBackend.Interpreter, model.A1200);
                        rows.Add(new(id, $"{directory}/{name}", hash, "passing", instructions, null));
                    }
                    catch (Exception ex) { rows.Add(new(id, $"{directory}/{name}", hash, "mismatching", 0, ex.Message)); }
                }
            }
            if (!rows.Any(r => r.Model == id && r.Status != "excluded")) throw new XunitException($"No executable reference programs selected for {id}.");
        }
        var output = Environment.GetEnvironmentVariable("COPPER68K_SYNTHETIC_REPORT_DIR");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "musashi-model-audit.json"), JsonSerializer.Serialize(new
            {
                schema = 1, reference = "Musashi self-checking programs", selectedModels = selected,
                qualification = "Independent software assertions; program counts are not instruction-combination or hardware qualification counts",
                passing = rows.Count(r => r.Status == "passing"), mismatching = rows.Count(r => r.Status == "mismatching"),
                excluded = rows.Count(r => r.Status == "excluded"), rows
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        foreach (var model in selected)
            _output.WriteLine($"{model}: {rows.Count(r => r.Model == model && r.Status == "passing")} reference programs passed, {rows.Count(r => r.Model == model && r.Status == "excluded")} excluded.");
        var failures = rows.Where(r => r.Status == "mismatching").ToArray();
        Assert.True(failures.Length == 0, string.Join(Environment.NewLine, failures.Select(r => $"{r.Model}/{r.Program}: {r.Detail}")));
    }

    private static string? ReferenceExclusion(ModelSpec model, string directory, string name)
    {
        if (directory == "mc68000")
        {
            if (KnownIncompatiblePrograms.TryGetValue(name, out var reason)) return reason;
            if (!model.FullIndex && name == "move.bin")
                return "Despite its directory, this program encodes 020-only PC-relative CMPI.B (0C3A) at offset 0x160; 000/010 correctly raise vector 4, and the fixture supplies no compatible handler. M68000PM 4-80.";
            if (model.Id == "68060" && name == "movep.bin") return "060 hardware raises vector 61; this program assumes hardware MOVEP execution without a software handler.";
        }
        else
        {
            if (!model.FullIndex) return "Program requires 020+ instructions; no unavailable-instruction handler in the fixture.";
            if (KnownFailingM68040Programs.TryGetValue(name, out var reason)) return reason;
            if (model.Id == "68060" && name is "cas.bin" or "divs_long.bin" or "divu_long.bin" or "mul_long.bin")
                return "Program contains integer forms unimplemented in 060 hardware and supplies no vector-61 software handler.";
            if (model.Id == "68060" && name == "interrupt.bin") return "Program requires the 020/030/040 master stack and paired throwaway frame; 060 ignores M and has no MSP.";
        }
        return null;
    }

    private sealed record ProgramAuditRow(string Model, string Program, string Sha256, string Status, int RetiredInstructions, string? Detail);
}
