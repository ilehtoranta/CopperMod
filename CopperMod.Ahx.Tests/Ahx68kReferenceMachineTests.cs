namespace CopperMod.Ahx.Tests;

public sealed class Ahx68kReferenceMachineTests
{
    [Fact]
    public void Original68000PlayerBootsAndProducesGrantedCycleTrace()
    {
        var playerPath = Path.Combine(AhxTestInputs.Root, "ahx-reference", "Players", "AHX-Replayer000.BIN");
        var modulePath = Path.Combine(AhxTestInputs.Root, "ahx-reference", "Songs", "AHX.Agony End");
        if (!File.Exists(playerPath) || !File.Exists(modulePath))
        {
            AhxTestInputs.ReportMissing("AHX-Replayer000.BIN and AHX.Agony End are unavailable.");
            return;
        }

        using var machine = new Ahx68kReferenceMachine(
            File.ReadAllBytes(playerPath),
            File.ReadAllBytes(modulePath),
            enableTracing: true);

        var initialized = machine.Initialize();
        var tick = machine.Interrupt();
        var pcm = new float[4_410 * 2];
        machine.RenderFrames(pcm, 4_410, 2, 44_100, captureChannels: true);

        Assert.True(initialized.EndCycle > initialized.StartCycle);
        Assert.True(tick.EndCycle > tick.StartCycle);
        Assert.All(pcm, sample => Assert.True(float.IsFinite(sample)));
        Assert.True(
            pcm.Any(sample => Math.Abs(sample) > 0.0001f),
            string.Join(Environment.NewLine, machine.Trace.TakeLast(40).Select(entry => entry.ToStableText())));
        Assert.NotNull(machine.LastChannelWaveform);
        Assert.Contains(machine.Trace, entry => entry.Kind == AhxTraceKind.CustomWrite);
        var audioWrites = machine.Trace
            .Where(entry => entry.Kind == AhxTraceKind.CustomWrite && entry.Register is >= 0x0A0 and <= 0x0DF)
            .ToArray();
        Assert.True(audioWrites.Length > 4);
        Assert.Equal(audioWrites.Select(entry => entry.Cycle).OrderBy(cycle => cycle), audioWrites.Select(entry => entry.Cycle));
        Assert.True(audioWrites.Select(entry => entry.Cycle).Distinct().Count() > 1);
        Assert.Contains(machine.Trace, entry => entry.Kind == AhxTraceKind.PaulaDmaRead);
        Assert.Contains(machine.Trace, entry => entry.Kind == AhxTraceKind.ChannelState);
        Assert.Equal(machine.Trace.OrderBy(entry => entry.Cycle), machine.Trace);
        Assert.InRange(machine.Trace.Count, 1, Ahx68kReferenceMachine.TraceEventCapacity);
        Assert.NotEmpty(machine.GetStableTraceText());

        using var productionMachine = new Ahx68kReferenceMachine(
            File.ReadAllBytes(playerPath),
            File.ReadAllBytes(modulePath));
        Assert.False(productionMachine.TraceEnabled);
        Assert.Throws<InvalidOperationException>(() => productionMachine.TraceEnabled = true);
        var productionInitialized = productionMachine.Initialize();
        var productionTick = productionMachine.Interrupt();
        var productionPcm = new float[pcm.Length];
        productionMachine.RenderFrames(productionPcm, 4_410, 2, 44_100, captureChannels: true);

        Assert.Equal(initialized.EndCycle, productionInitialized.EndCycle);
        Assert.Equal(tick.EndCycle, productionTick.EndCycle);
        Assert.Equal(machine.Cycle, productionMachine.Cycle);
        Assert.Equal(machine.TempoWord, productionMachine.TempoWord);
        Assert.Equal(pcm, productionPcm);
        Assert.Empty(productionMachine.Trace);
    }

    [Fact]
    public void TwoSecondReferenceTraceIsDeterministicAcrossHostBatchPartitions()
    {
        var paths = FindReferenceFiles();
        if (paths is null)
        {
            AhxTestInputs.ReportMissing("the AHX reference files are unavailable.");
            return;
        }

        var oneBatch = CaptureTrace(paths.Value.Player, paths.Value.Module, [100]);
        var splitBatches = CaptureTrace(paths.Value.Player, paths.Value.Module, [17, 1, 32, 50]);

        Assert.Equal(oneBatch, splitBatches);
    }

    [Fact]
    public void NormalSongApiRendersPartitionIndependentStereoPcm()
    {
        var paths = FindReferenceFiles();
        if (paths is null)
        {
            AhxTestInputs.ReportMissing("the AHX reference files are unavailable.");
            return;
        }

        var player = File.ReadAllBytes(paths.Value.Player);
        var module = File.ReadAllBytes(paths.Value.Module);
        var options = new CopperMod.Abstractions.AudioRenderOptions(44_100, 2);
        var oneBuffer = new float[8_820 * 2];
        var splitBuffer = new float[oneBuffer.Length];
        using var oneCall = new AhxFormat(player).Load(module);
        using var splitCalls = new AhxFormat(player).Load(module);

        var oneResult = oneCall.Render(oneBuffer, options);
        var offsetFrames = 0;
        foreach (var frames in new[] { 997, 2_501, 3_113, 2_209 })
        {
            var result = splitCalls.Render(splitBuffer.AsSpan(offsetFrames * 2, frames * 2), options);
            Assert.Equal(frames, result.FramesWritten);
            offsetFrames += frames;
        }

        Assert.Equal(8_820, oneResult.FramesWritten);
        Assert.Equal(8_820, offsetFrames);
        Assert.Equal(oneBuffer, splitBuffer);
        Assert.Empty(Assert.IsType<Ahx68kSong>(oneCall).Trace);
        Assert.Empty(Assert.IsType<Ahx68kSong>(splitCalls).Trace);
        Assert.All(oneBuffer, sample => Assert.True(float.IsFinite(sample)));
        Assert.Contains(oneBuffer, sample => Math.Abs(sample) > 0.0001f);
        Assert.IsAssignableFrom<CopperMod.Abstractions.IAmigaHardwareStateProvider>(oneCall);
        Assert.IsAssignableFrom<CopperMod.Abstractions.IModuleSubSongSelector>(oneCall);
        Assert.Equal("Original AHX 2.3d 68000 binary", oneCall.Metadata.Tags["Replay"]);
    }

    [Fact]
    public void Mono48KhzResetRerenderIsDeterministic()
    {
        var paths = FindReferenceFiles();
        if (paths is null)
        {
            AhxTestInputs.ReportMissing("the AHX reference files are unavailable.");
            return;
        }

        var options = new CopperMod.Abstractions.AudioRenderOptions(48_000, 1);
        using var song = new Ahx68kSong(
            File.ReadAllBytes(paths.Value.Module),
            File.ReadAllBytes(paths.Value.Player));
        var first = new float[4_800];
        var second = new float[first.Length];

        song.Render(first, options);
        song.Reset();
        song.Render(second, options);

        Assert.Equal(first, second);
        Assert.Contains(first, sample => Math.Abs(sample) > 0.0001f);
    }

    [Fact]
    public void SeekingTwiceToSameTimeProducesSameFollowingPcm()
    {
        var paths = FindReferenceFiles();
        if (paths is null)
        {
            AhxTestInputs.ReportMissing("the AHX reference files are unavailable.");
            return;
        }

        var options = CopperMod.Abstractions.AudioRenderOptions.Default;
        using var song = new Ahx68kSong(
            File.ReadAllBytes(paths.Value.Module),
            File.ReadAllBytes(paths.Value.Player));
        var first = new float[2_000 * options.ChannelCount];
        var second = new float[first.Length];

        song.Seek(TimeSpan.FromMilliseconds(75));
        song.Render(first, options);
        song.Seek(TimeSpan.FromMilliseconds(75));
        song.Render(second, options);

        Assert.Equal(first, second);
        Assert.Equal(0, song.CurrentSubSongIndex);
        Assert.True(song.SubSongCount >= 1);
    }

    private static string CaptureTrace(string playerPath, string modulePath, int[] batches)
    {
        using var machine = new Ahx68kReferenceMachine(
            File.ReadAllBytes(playerPath),
            File.ReadAllBytes(modulePath),
            enableTracing: true);
        machine.Initialize();
        Assert.True(machine.SubSongCount > 0);
        Assert.NotEqual(0, machine.ReadPublicByte(4));

        foreach (var batch in batches)
        {
            for (var i = 0; i < batch; i++)
            {
                machine.Interrupt();
            }
        }

        machine.ValidateGuards();
        return machine.GetStableTraceText();
    }

    private static (string Player, string Module)? FindReferenceFiles()
    {
        var playerPath = Path.Combine(AhxTestInputs.Root, "ahx-reference", "Players", "AHX-Replayer000.BIN");
        var modulePath = Path.Combine(AhxTestInputs.Root, "ahx-reference", "Songs", "AHX.Agony End");
        return File.Exists(playerPath) && File.Exists(modulePath)
            ? (playerPath, modulePath)
            : null;
    }
}
