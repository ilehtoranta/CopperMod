using CopperMod.Abstractions;

namespace CopperMod.Ahx.Tests;

public sealed class AhxRobustnessTests
{
    [Fact]
    public void EveryRealModuleTruncationFailsCleanlyOrRemainsStructurallyParseable()
    {
        var module = FindRealModule();
        if (module is null)
        {
            AhxTestInputs.ReportMissing("a real AHX module is unavailable.");
            return;
        }

        for (var length = 0; length < module.Length; length++)
        {
            try
            {
                AhxParser.Parse(module.AsSpan(0, length));
            }
            catch (ModuleLoadException)
            {
            }
            catch (Exception ex)
            {
                throw new Xunit.Sdk.XunitException($"AHX prefix length {length} escaped as {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    [Fact]
    public void RandomRecognizedInputsNeverEscapeAsHostIndexOrOverflowFaults()
    {
        var random = new Random(0x4158);
        for (var iteration = 0; iteration < 2_000; iteration++)
        {
            var data = new byte[random.Next(14, 768)];
            random.NextBytes(data);
            data[0] = (byte)'T';
            data[1] = (byte)'H';
            data[2] = (byte)'X';
            data[3] &= 1;
            try
            {
                AhxParser.Parse(data);
            }
            catch (ModuleLoadException)
            {
            }
            catch (Exception ex)
            {
                throw new Xunit.Sdk.XunitException($"AHX fuzz case {iteration} escaped as {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    [Fact]
    public void RejectsUnavailableSubsongTrackAndInstrumentReferences()
    {
        var badSubsong = MinimalModule(subSongs: [1]);
        Assert.Throws<ModuleLoadException>(() => AhxParser.Parse(badSubsong));

        var badTrack = MinimalModule();
        badTrack[14] = 1;
        Assert.Throws<ModuleLoadException>(() => AhxParser.Parse(badTrack));

        var badInstrument = MinimalModule(maxTrack: 1, includeTrack: true);
        WriteTrackStep(badInstrument, 22, note: 24, instrument: 1, command: 0, parameter: 0);
        Assert.Throws<ModuleLoadException>(() => AhxParser.Parse(badInstrument));
    }

    [Fact]
    public void ParsesPackedFilterVibratoAndHardCutFields()
    {
        var data = MinimalModule(instrumentCount: 1);
        const int instrumentOffset = 22;
        data[instrumentOffset] = 64;
        data[instrumentOffset + 1] = (byte)((0x1A << 3) | 5);
        data[instrumentOffset + 2] = 1;
        data[instrumentOffset + 3] = 64;
        data[instrumentOffset + 4] = 1;
        data[instrumentOffset + 5] = 48;
        data[instrumentOffset + 6] = 2;
        data[instrumentOffset + 7] = 3;
        data[instrumentOffset + 8] = 0;
        data[instrumentOffset + 12] = 0x92;
        data[instrumentOffset + 13] = 4;
        data[instrumentOffset + 14] = 0xD7;
        data[instrumentOffset + 15] = 8;
        data[instrumentOffset + 16] = 8;
        data[instrumentOffset + 17] = 48;
        data[instrumentOffset + 18] = 4;
        data[instrumentOffset + 19] = 42;
        data[instrumentOffset + 20] = 2;

        var instrument = AhxParser.Parse(data).Instruments[1];

        Assert.Equal(5, instrument.WaveLength);
        Assert.Equal(0x12, instrument.FilterLower);
        Assert.Equal(42, instrument.FilterUpper);
        Assert.Equal(0x3A, instrument.FilterSpeed);
        Assert.Equal(4, instrument.VibratoDelay);
        Assert.Equal(7, instrument.VibratoDepth);
        Assert.Equal(8, instrument.VibratoSpeed);
        Assert.Equal(5, instrument.HardCutReleaseFrames);
        Assert.True(instrument.HardCutRelease);
    }

    [Fact]
    public void ReferenceHarnessRejectsCorruptJumpTableAndOversizedModuleBeforeExecution()
    {
        var paths = FindReferencePaths();
        if (paths is null)
        {
            AhxTestInputs.ReportMissing("the AHX reference files are unavailable.");
            return;
        }

        var player = File.ReadAllBytes(paths.Value.Player);
        var module = File.ReadAllBytes(paths.Value.Module);
        var corrupt = (byte[])player.Clone();
        corrupt[0] = 0x4E;
        Assert.Throws<ArgumentException>(() => new Ahx68kReferenceMachine(corrupt, module));
        Assert.Throws<ArgumentException>(() => new Ahx68kReferenceMachine(player, module, 0x80000));
    }

    private static byte[] MinimalModule(int[]? subSongs = null, int maxTrack = 0, bool includeTrack = false, int instrumentCount = 0)
    {
        subSongs ??= [];
        var trackBytes = includeTrack ? 3 : 0;
        var instrumentBytes = instrumentCount * 22;
        var data = new byte[14 + (subSongs.Length * 2) + 8 + trackBytes + instrumentBytes];
        data[0] = (byte)'T';
        data[1] = (byte)'H';
        data[2] = (byte)'X';
        data[3] = 1;
        data[6] = 0x80;
        data[7] = 1;
        data[10] = 1;
        data[11] = (byte)maxTrack;
        data[12] = (byte)instrumentCount;
        data[13] = (byte)subSongs.Length;
        var cursor = 14;
        foreach (var subSong in subSongs)
        {
            data[cursor++] = (byte)(subSong >> 8);
            data[cursor++] = (byte)subSong;
        }

        if (includeTrack)
        {
            data[cursor] = 1;
        }

        return data;
    }

    private static void WriteTrackStep(byte[] data, int offset, int note, int instrument, int command, int parameter)
    {
        var value = (note << 18) | (instrument << 12) | (command << 8) | parameter;
        data[offset] = (byte)(value >> 16);
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)value;
    }

    private static byte[]? FindRealModule()
    {
        var paths = FindReferencePaths();
        return paths is null ? null : File.ReadAllBytes(paths.Value.Module);
    }

    private static (string Player, string Module)? FindReferencePaths()
    {
        var player = Path.Combine(AhxTestInputs.Root, "ahx-reference", "Players", "AHX-Replayer000.BIN");
        var module = Path.Combine(AhxTestInputs.Root, "ahx-reference", "Songs", "AHX.Agony End");
        return File.Exists(player) && File.Exists(module) ? (player, module) : null;
    }
}
