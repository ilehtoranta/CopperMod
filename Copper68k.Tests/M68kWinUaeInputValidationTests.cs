using System.Buffers.Binary;
using System.Security.Cryptography;
using Copper68k.Tests.Synthetic;
using Xunit.Sdk;
using static Copper68k.Tests.M68kWinUaeCpuTesterConformanceTests;

namespace Copper68k.Tests;

public sealed class M68kWinUaeInputValidationTests
{
    [Theory]
    [InlineData("missing-memory", "memory image")]
    [InlineData("empty-selection", "Incomplete or incompatible")]
    [InlineData("changed-input", "Changed WinUAE fixture")]
    [InlineData("missing-sequence", "header/data sequence")]
    [InlineData("wrong-width", "model/address width")]
    [InlineData("empty-data", "Incomplete or incompatible WinUAE data")]
    public void PreflightRejectsIncompleteOrChangedInputs(string defect, string diagnostic)
    {
        var root = Path.Combine(Path.GetTempPath(), "copper68k-winuae-" + Guid.NewGuid().ToString("N"));
        var model = ModelSpec.All.Single(m => m.Id == "68000");
        var cpuPath = Path.Combine(root, model.Id, model.Id);
        Directory.CreateDirectory(cpuPath);
        try
        {
            foreach (var memory in new[] { ("lmem.dat", 0x8000), ("hmem.dat", 0x8000), ("tmem.dat", 0x40000) })
                File.WriteAllBytes(Path.Combine(cpuPath, memory.Item1), new byte[memory.Item2]);
            // Structural placeholders only: these files are never passed to native code.
            var opcodes = new[] { "NOP" }.Concat(Enumerable.Range(1, 145).Select(n => $"STRUCTURAL-{n:D3}")).ToArray();
            foreach (var opcode in opcodes)
            {
                var directory = Path.Combine(cpuPath, opcode);
                Directory.CreateDirectory(directory);
                var header = new byte[98];
                BinaryPrimitives.WriteUInt32BigEndian(header, 16);
                File.WriteAllBytes(Path.Combine(directory, "0000.dat"), header);
                var data = new byte[18];
                BinaryPrimitives.WriteUInt32BigEndian(data, 16);
                File.WriteAllBytes(Path.Combine(directory, "0001.dat"), data);
            }
            WinUaeManifest Manifest(string[]? selection = null) => new(1, GeneratorPin, RunnerPin, "unused", new[] {
                new WinUaeProfile(model.Id, model.Id, 0, 24, selection ?? opcodes,
                    Directory.GetFiles(cpuPath, "*.dat", SearchOption.AllDirectories).Select(file =>
                        new WinUaeInput(Path.GetRelativePath(Path.Combine(root, model.Id), file).Replace('\\', '/'),
                            new FileInfo(file).Length, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant())).ToArray())
            });
            var manifest = Manifest();
            ValidateWinUaeProfile(root, manifest, model);
            var nopPath = Path.Combine(cpuPath, "NOP");
            switch (defect)
            {
                case "missing-memory": File.Delete(Path.Combine(cpuPath, "tmem.dat")); manifest = Manifest(); break;
                case "empty-selection": manifest = Manifest(Array.Empty<string>()); break;
                case "changed-input": File.AppendAllText(Path.Combine(nopPath, "0001.dat"), "changed"); break;
                case "missing-sequence": File.Move(Path.Combine(nopPath, "0001.dat"), Path.Combine(nopPath, "0002.dat")); manifest = Manifest(); break;
                case "wrong-width":
                    var header = File.ReadAllBytes(Path.Combine(nopPath, "0000.dat"));
                    BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(24), 0x80000000);
                    File.WriteAllBytes(Path.Combine(nopPath, "0000.dat"), header);
                    manifest = Manifest(); break;
                case "empty-data": File.WriteAllBytes(Path.Combine(nopPath, "0001.dat"), Array.Empty<byte>()); manifest = Manifest(); break;
            }
            var error = Assert.Throws<XunitException>(() => ValidateWinUaeProfile(root, manifest, model));
            Assert.Contains(diagnostic, error.Message, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
