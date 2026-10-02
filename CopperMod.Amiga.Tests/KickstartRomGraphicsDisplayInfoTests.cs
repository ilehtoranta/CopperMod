using System.Buffers.Binary;
using Amiga;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed partial class KickstartRomLayersDifferentialTests
{
    private const uint GraphicsProbeRomBase = 0x00F80000;

    [GraphicsRomTheory]
    [InlineData(false, 1, 2, 234, 1)]
    [InlineData(true, 1, 2, 234, 1)]
    [InlineData(false, -2, -1, 234, 1)]
    [InlineData(true, -2, -1, 234, 1)]
    [InlineData(false, 1, 2, 118, 1)]
    [InlineData(true, 1, 2, 118, 1)]
    [InlineData(false, 1, 2, 119, 1)]
    [InlineData(true, 1, 2, 119, 1)]
    [InlineData(false, 1, 2, 120, 1)]
    [InlineData(true, 1, 2, 120, 1)]
    [InlineData(false, 1, 2, 234, 2)]
    [InlineData(true, 1, 2, 234, 2)]
    [InlineData(false, 1, 2, 234, 1, true)]
    [InlineData(true, 1, 2, 234, 1, true)]
    public void NativeGraphicsMonitorViewPositionPreferenceObservations(
        bool ntsc, int xOffset, int yOffset, uint updateSize, int repetitions, bool openScreen = false)
    {
        Assert.True(TryLoadConfiguredRom(out var rom));
        var context = CreateNativeIntuitionOracle(rom, suppressInputDeviceOverlay: true,
            ntsc: ntsc, ecs: false, chipRamBytes: 512 * 1024);
        try
        {
            var intuition = ContinueUntilNativeIntuitionPublished(context);
            Assert.NotEqual(0u, intuition);
            var graphics = FindLibrary(context.Bus, context.ExecBase, "graphics.library");
            Assert.NotEqual(0u, graphics);
            AssertOriginalGraphicsRomEntries(context, graphics, rom);
            CheckIntuitionEntries();
            CaptureIntuitionState("before");
            var preferences = context.Allocate(Preferences.Size);
            var output = context.Allocate(92);
            Assert.Equal(preferences, context.Invoke(intuition, IntuitionLvo.GetPrefs, state =>
            {
                state.A[0] = preferences;
                state.D[0] = Preferences.Size;
            }).D[0]);
            var initialPreferences = ReadBytes(context.Bus, preferences, (int)Preferences.Size);
            _output.WriteLine($"graphics:mntr-prefs:initial:ntsc={ntsc}:bytes={Convert.ToHexString(initialPreferences)}");
            var bootMonitor = ntsc ? 0x11000u : 0x21000u;
            var modes = new[] { 0u, bootMonitor, bootMonitor | 0x8000u, ntsc ? 0x21000u : 0x11000u };
            var before = modes.Select(mode => Capture(mode, "before")).ToArray();
            // SDK Preferences and guest codec agree on signed bytes118/119.
            // Copy all other current preferences unchanged; affect only this
            // disposable guest, and do not broadcast IDCMP_NEWPREFS messages.
            context.Bus.WriteByte(preferences + 118, unchecked((byte)(sbyte)xOffset), 0);
            context.Bus.WriteByte(preferences + 119, unchecked((byte)(sbyte)yOffset), 0);
            // SetPrefs can schedule/wait: the direct sentinel fixture is not
            // a valid completion oracle. Use the existing synchronous Exec
            // task fixture, retaining its native return value in guest RAM.
            var code = context.Allocate(0x200);
            var result = context.Allocate(4);
            var returnedPreferences = context.Allocate(Preferences.Size);
            var taskOutputs = context.Allocate(4 * 96);
            for (uint offset = 0; offset < 4 * 96; offset++)
                context.Bus.WriteByte(taskOutputs + offset, 0xA5, 0);
            var writer = new NativeTaskInstructionWriter(context.Bus, code);
            var screenResult = context.Allocate(4);
            if (openScreen)
            {
                var memory = new LayersTestGuestMemory(context.Bus);
                var newScreen = context.Allocate(NewScreen.Size);
                IntuitionScreenWindowGuestCodec.WriteNewScreen(ref memory,
                    APTR.FromPointer(newScreen), new NewScreen
                    {
                        Width = 320, Height = 200, Depth = 2,
                        DetailPen = 0, BlockPen = 1,
                        ViewModes = ScreenViewModes.None, Type = ScreenType.Custom,
                    });
                writer.MoveAddressImmediate(6, intuition);
                writer.MoveAddressImmediate(0, newScreen);
                writer.JumpSubroutine(unchecked((uint)((int)intuition + (int)IntuitionLvo.OpenScreen)));
                writer.MoveDataRegisterToAbsoluteLong(0, screenResult);
            }
            for (var repetition = 0; repetition < repetitions; repetition++)
            {
                writer.MoveAddressImmediate(6, intuition);
                writer.MoveAddressImmediate(0, preferences);
                writer.MoveDataImmediate(0, updateSize);
                writer.MoveDataImmediate(1, 0);
                writer.JumpSubroutine(unchecked((uint)((int)intuition + (int)IntuitionLvo.SetPrefs)));
                writer.MoveDataRegisterToAbsoluteLong(0, result);
            }
            writer.MoveAddressImmediate(6, intuition);
            writer.MoveAddressImmediate(0, returnedPreferences);
            writer.MoveDataImmediate(0, Preferences.Size);
            writer.JumpSubroutine(unchecked((uint)((int)intuition + (int)IntuitionLvo.GetPrefs)));
            for (var index = 0; index < modes.Length; index++)
            {
                writer.MoveAddressImmediate(6, graphics);
                writer.MoveAddressImmediate(0, 0);
                writer.MoveAddressImmediate(1, taskOutputs + (uint)index * 96 + 2);
                writer.MoveDataImmediate(0, 88);
                writer.MoveDataImmediate(1, 0x80002000);
                writer.MoveDataImmediate(2, modes[index]);
                writer.JumpSubroutine(unchecked((uint)((int)graphics + (int)GraphicsLvo.GetDisplayInfoData)));
                writer.MoveDataRegisterToAbsoluteLong(0, taskOutputs + (uint)index * 96 + 92);
            }
            writer.MoveDataImmediate(7, 0x4D4E5452);
            writer.BranchToSelf();
            _ = RunNativeTask(context, code, 0x4D4E5452);
            Assert.Equal(preferences, context.Bus.ReadLong(result));
            if (openScreen)
            {
                var screen = context.Bus.ReadLong(screenResult);
                Assert.NotEqual(0u, screen);
                _output.WriteLine($"graphics:mntr-prefs:opened-screen:ntsc={ntsc}:screen=0x{screen:X8}");
            }
            CaptureIntuitionState("after");
            var finalPreferences = ReadBytes(context.Bus, returnedPreferences, (int)Preferences.Size);
            _output.WriteLine($"graphics:mntr-prefs:returned:ntsc={ntsc}:bytes={Convert.ToHexString(finalPreferences)}");
            Assert.Equal(updateSize > 118 ? unchecked((byte)(sbyte)xOffset) : initialPreferences[118], finalPreferences[118]);
            Assert.Equal(updateSize > 119 ? unchecked((byte)(sbyte)yOffset) : initialPreferences[119], finalPreferences[119]);
            for (var index = 0; index < initialPreferences.Length; index++)
                if (index != 118 && index != 119)
                    Assert.Equal(initialPreferences[index], finalPreferences[index]);
            for (var index = 0; index < modes.Length; index++)
            {
                var after = Capture(modes[index], "after", taskOutputs + (uint)index * 96);
                // Captures361/364 establish this fixture's NTSC target,
                // including PAL boots and a successfully opened custom screen.
                // This is not a universal SetPrefs monitor-selection rule.
                // Repeating identical offsets replaces, rather than adds to,
                // the original Point; partial copies retain excluded axes.
                var resolvedMode = modes[index] == 0 ? bootMonitor : modes[index];
                var affected = (resolvedMode & 0xFFFF0000u) == 0x00010000u;
                Assert.Equal((short)(129 + (affected && updateSize > 118 ? xOffset : 0)),
                    BinaryPrimitives.ReadInt16BigEndian(after.AsSpan(20)));
                Assert.Equal((short)(44 + (affected && updateSize > 119 ? yOffset : 0)),
                    BinaryPrimitives.ReadInt16BigEndian(after.AsSpan(22)));
                Assert.Equal(before[index].AsSpan(80, 4).ToArray(), after.AsSpan(80, 4).ToArray());
            }
            AssertOriginalGraphicsRomEntries(context, graphics, rom);
            CheckIntuitionEntries();
            context.Free(output, 92);
            context.Free(preferences, Preferences.Size);

            void CaptureIntuitionState(string phase)
            {
                var memory = new LayersTestGuestMemory(context.Bus);
                var state = IntuitionBaseGuestCodec.Read(ref memory, APTR.FromPointer(intuition));
                _output.WriteLine($"graphics:mntr-prefs:intuition:ntsc={ntsc}:phase={phase}:first-screen=0x{state.FirstScreen.Raw:X8}:active-screen=0x{state.ActiveScreen.Raw:X8}:view-port=0x{state.ViewLord.ViewPort.Raw:X8}:view-modes=0x{state.ViewLord.Modes:X4}:view-x={state.ViewLord.XOffset}:view-y={state.ViewLord.YOffset}");
            }

            byte[] Capture(uint mode, string phase, uint taskOutput = 0)
            {
                var buffer = taskOutput == 0 ? output : taskOutput;
                if (taskOutput == 0)
                    for (uint offset = 0; offset < 92; offset++)
                        context.Bus.WriteByte(buffer + offset, 0xA5, 0);
                var copied = taskOutput != 0 ? context.Bus.ReadLong(taskOutput + 92) :
                    context.Invoke(graphics, (int)GraphicsLvo.GetDisplayInfoData, state =>
                {
                    state.A[0] = 0;
                    state.A[1] = output + 2;
                    state.D[0] = 88;
                    state.D[1] = 0x80002000;
                    state.D[2] = mode;
                }).D[0];
                Assert.Equal(88u, copied);
                var bytes = ReadBytes(context.Bus, buffer + 2, 88);
                var x = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(20));
                var y = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(22));
                _output.WriteLine($"graphics:mntr-prefs:ntsc={ntsc}:requested-x={xOffset}:requested-y={yOffset}:mode=0x{mode:X8}:phase={phase}:x={x}:y={y}:default={Convert.ToHexString(bytes.AsSpan(80, 4))}");
                Assert.Equal((byte)0xA5, context.Bus.ReadByte(buffer));
                Assert.Equal((byte)0xA5, context.Bus.ReadByte(buffer + 1));
                Assert.Equal((byte)0xA5, context.Bus.ReadByte(buffer + 90));
                Assert.Equal((byte)0xA5, context.Bus.ReadByte(buffer + 91));
                return bytes;
            }

            void CheckIntuitionEntries()
            {
                var mapped = ReadBytes(context.Bus, GraphicsProbeRomBase, rom.Length);
                foreach (var lvo in new[] { IntuitionLvo.GetPrefs, IntuitionLvo.SetPrefs })
                {
                    var vector = unchecked((uint)((int)intuition + (int)lvo));
                    Assert.True(IsOriginalGraphicsRomEntry(context.Bus.ReadWord(vector),
                        context.Bus.ReadLong(vector + 2), rom, mapped));
                }
            }
        }
        finally { context.Machine.Dispose(); }
    }

    [GraphicsRomTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeGraphicsModeSelectionDepthBoundaryObservations(bool ntsc)
    {
        Assert.True(TryLoadConfiguredRom(out var rom));
        var context = CreateNativeIntuitionOracle(rom, suppressInputDeviceOverlay: true, ntsc: ntsc);
        try
        {
            Assert.NotEqual(0u, ContinueUntilNativeIntuitionPublished(context));
            var graphicsBase = FindLibrary(context.Bus, context.ExecBase, "graphics.library");
            Assert.NotEqual(0u, graphicsBase);
            CheckEntries();
            var allocation = context.Allocate(256);
            try
            {
                var tags = allocation;
                var viewport = allocation + 80;
                var rasInfo = allocation + 128;
                var bitmap = allocation + 160;
                for (uint offset = 0; offset < 256; offset++)
                    context.Bus.WriteByte(allocation + offset, 0, 0);
                var monitor = ntsc ? GraphicsModeIds.NtscMonitor : GraphicsModeIds.PalMonitor;
                foreach (var depth in new uint[] { 4, 5, 6 })
                {
                    Tag(0, GraphicsDisplayDatabase.BidTagNominalWidth, 640);
                    Tag(8, GraphicsDisplayDatabase.BidTagNominalHeight, ntsc ? 200u : 256u);
                    Tag(16, GraphicsDisplayDatabase.BidTagDesiredWidth, 640);
                    Tag(24, GraphicsDisplayDatabase.BidTagDesiredHeight, ntsc ? 200u : 256u);
                    Tag(32, GraphicsDisplayDatabase.BidTagDepth, depth);
                    Tag(40, GraphicsDisplayDatabase.BidTagMonitorId, monitor);
                    var result = context.Invoke(graphicsBase, (int)GraphicsLvo.BestModeIDA,
                        state => state.A[0] = tags).D[0];
                    _output.WriteLine($"graphics:raw-bestmode:ntsc={ntsc}:width=640:depth={depth}:result=0x{result:X8}");
                    Assert.Equal(depth == 6 ? uint.MaxValue :
                        monitor | (depth == 4 ? 0x8000u : 0u), result);
                }
                context.Bus.WriteWord(viewport + (uint)GraphicsLayouts.ViewPortDWidth, 320, 0);
                context.Bus.WriteLong(viewport + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo, 0);
                context.Bus.WriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, bitmap, 0);
                context.Bus.WriteWord(bitmap + (uint)GraphicsLayouts.BitMapBytesPerRow, 40, 0);
                context.Bus.WriteWord(bitmap + (uint)GraphicsLayouts.BitMapRows, 512, 0);
                foreach (var height in new ushort[] { 256, 512 })
                foreach (var depth in new byte[] { 5, 6 })
                foreach (var flags in new uint[] { 0, GraphicsDisplayDatabase.CoercePreserveColors })
                {
                    context.Bus.WriteWord(viewport + (uint)GraphicsLayouts.ViewPortDHeight, height, 0);
                    context.Bus.WriteByte(bitmap + (uint)GraphicsLayouts.BitMapDepth, depth, 0);
                    var result = context.Invoke(graphicsBase, (int)GraphicsLvo.CoerceMode, state =>
                    {
                        state.A[0] = viewport;
                        state.D[0] = monitor;
                        state.D[1] = flags;
                    }).D[0];
                    _output.WriteLine($"graphics:raw-coerce:ntsc={ntsc}:height={height}:depth={depth}:flags={flags}:result=0x{result:X8}");
                    // Independently captured on both original 3.1 boot profiles.
                    // A six-plane ordinary source is not silently reinterpreted
                    // as HAM, even when PRESERVE_COLORS was not requested.
                    Assert.Equal(depth == 6 ? uint.MaxValue :
                        monitor | (height == 512 ? 4u : 0u), result);
                }
                // Observation-only extension: do not infer feature-source
                // validity from replacement MaxDepth or selection policy.
                var colorMap = context.Invoke(graphicsBase, (int)GraphicsLvo.GetColorMap,
                    state => state.D[0] = 256).D[0];
                Assert.NotEqual(0u, colorMap);
                foreach (var colorMapState in new[] { 0, 1, 2 })
                foreach (var mode in new ushort[] { 0x8000, 0x0800, 0x0080, 0x0400 })
                foreach (var viewportMode in new ushort[] { mode, 0 })
                foreach (var width in new ushort[] { 320, 640 })
                foreach (var depth in new byte[] { 4, 5, 6, 7 })
                foreach (var flags in new uint[] { 0, 1 })
                {
                    context.Bus.WriteWord(viewport + (uint)GraphicsLayouts.ViewPortDWidth, width, 0);
                    context.Bus.WriteWord(viewport + (uint)GraphicsLayouts.ViewPortDHeight, ntsc ? (ushort)200 : (ushort)256, 0);
                    context.Bus.WriteWord(viewport + (uint)GraphicsLayouts.ViewPortModes, viewportMode, 0);
                    context.Bus.WriteLong(viewport + (uint)GraphicsLayouts.ViewPortColorMap,
                        colorMapState != 0 ? colorMap : 0, 0);
                    if (colorMapState == 2)
                    {
                        var displayInfo = context.Invoke(graphicsBase, (int)GraphicsLvo.FindDisplayInfo,
                            state => state.D[0] = monitor | mode).D[0];
                        Assert.NotEqual(0u, displayInfo);
                        Tag(0, 0x80000010, displayInfo); // VTAG_NORMAL_DISP_SET
                        Tag(8, 0, 0);
                        Assert.Equal(0u, context.Invoke(graphicsBase, (int)GraphicsLvo.VideoControl, state =>
                        {
                            state.A[0] = colorMap;
                            state.A[1] = tags;
                        }).D[0]);
                    }
                    context.Bus.WriteWord(bitmap + (uint)GraphicsLayouts.BitMapBytesPerRow, (ushort)(width / 8), 0);
                    context.Bus.WriteByte(bitmap + (uint)GraphicsLayouts.BitMapDepth, depth, 0);
                    var result = context.Invoke(graphicsBase, (int)GraphicsLvo.CoerceMode, state =>
                    {
                        state.A[0] = viewport;
                        state.D[0] = monitor;
                        state.D[1] = flags;
                    }).D[0];
                    var sourceId = context.Invoke(graphicsBase, (int)GraphicsLvo.GetVPModeID,
                        state => state.A[0] = viewport).D[0];
                    _output.WriteLine($"graphics:raw-coerce-feature:ntsc={ntsc}:colormap={colorMapState}:mode=0x{mode:X4}:viewport-mode=0x{viewportMode:X4}:source=0x{sourceId:X8}:width={width}:depth={depth}:flags={flags}:result=0x{result:X8}");
                    if (colorMapState == 2)
                        Assert.Equal(monitor | mode, sourceId);
                    var specialSource = colorMapState == 2 && mode != 0x8000;
                    var selectionWidth = colorMapState == 2 ? (mode == 0x8000 ? 640 : 320) : width;
                    var expected = depth > (specialSource ? 6 : 5) ? uint.MaxValue :
                        monitor | (specialSource ? mode : selectionWidth == 640 && depth == 4 ? 0x8000u : 0u);
                    Assert.Equal(expected, result);
                }
                context.Bus.WriteLong(viewport + (uint)GraphicsLayouts.ViewPortColorMap, 0, 0);
                context.Invoke(graphicsBase, (int)GraphicsLvo.FreeColorMap, state => state.A[0] = colorMap);
                CheckEntries();

                void Tag(uint offset, uint tag, uint value)
                {
                    context.Bus.WriteLong(tags + offset, tag, 0);
                    context.Bus.WriteLong(tags + offset + 4, value, 0);
                }
            }
            finally { context.Free(allocation, 256); }

            void CheckEntries()
            {
                var mapped = ReadBytes(context.Bus, GraphicsProbeRomBase, rom.Length);
                foreach (var lvo in new[] { GraphicsLvo.BestModeIDA, GraphicsLvo.CoerceMode,
                    GraphicsLvo.GetVPModeID, GraphicsLvo.GetColorMap, GraphicsLvo.FreeColorMap,
                    GraphicsLvo.FindDisplayInfo, GraphicsLvo.VideoControl })
                {
                    var vector = unchecked((uint)((int)graphicsBase + (int)lvo));
                    Assert.True(IsOriginalGraphicsRomEntry(context.Bus.ReadWord(vector),
                        context.Bus.ReadLong(vector + 2), rom, mapped));
                }
            }
        }
        finally { context.Machine.Dispose(); }
    }

    // Missing media is unavailable coverage, not a passing ROM comparison.
    public sealed class GraphicsRomFactAttribute : FactAttribute
    {
        public GraphicsRomFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RomPathVariable)) &&
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RomVersionVariable)))
                Skip = "Requires a legally obtained COPPER_AMIGA_KICKSTART_ROM and COPPER_AMIGA_KICKSTART_VERSION=3.1.";
        }
    }

    public sealed class GraphicsRomTheoryAttribute : TheoryAttribute
    {
        public GraphicsRomTheoryAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RomPathVariable)) &&
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RomVersionVariable)))
                Skip = "Requires a legally obtained COPPER_AMIGA_KICKSTART_ROM and COPPER_AMIGA_KICKSTART_VERSION=3.1.";
        }
    }

    [GraphicsRomFact]
    public void NativeGraphicsDimensionRecordsCaptureMaxDepthAndPartialPrefixes()
        => CaptureNativeGraphicsDimensions(ecs: false, ntsc: false);

    [GraphicsRomTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NativeGraphicsDimensionDepthProfileObservations(bool ecs, bool ntsc)
        => CaptureNativeGraphicsDimensions(ecs, ntsc);

    [GraphicsRomTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeGraphicsDimensionDepthEcsOneMegObservations(bool ntsc)
        => CaptureNativeGraphicsDimensions(ecs: true, ntsc: ntsc, chipRamBytes: 1024 * 1024);

    // Fresh boots complement the dense probe above; they do not replace its
    // continuous-execution coverage or hide its recorded Copper reservation
    // failure. Each aligned row captures both default and PAL-owned records.
    public static IEnumerable<object[]> GraphicsDimensionColdBootKeys()
    {
        foreach (var key in new uint[]
        {
            0x0000, 0x0004, 0x0080, 0x0084, 0x0400, 0x0404, 0x0440, 0x0444,
            0x0800, 0x0804, 0x8000, 0x8004, 0x8020, 0x8024, 0x8400, 0x8404,
            0x8420, 0x8424, 0x8440, 0x8444, 0x8460, 0x8464,
        })
            yield return new object[] { key };
    }

    [GraphicsRomTheory]
    [MemberData(nameof(GraphicsDimensionColdBootKeys))]
    public void NativeGraphicsDimensionOcsPalColdBootTransfers(uint key)
        => CaptureNativeGraphicsDimensions(ecs: false, ntsc: false, onlyKey: key);

    //343's paired0440 capture hit the retained reservation failure after69
    // successful transfers. Collect its remaining PAL owner independently;
    // neither the paired nor dense probe is removed or reclassified.
    [GraphicsRomFact]
    public void NativeGraphicsDimensionOcsPalDualPlayfieldTwoOwnerColdBootTransfers()
        => CaptureNativeGraphicsDimensions(ecs: false, ntsc: false, onlyMode: 0x00021440);

    [GraphicsRomTheory]
    [MemberData(nameof(GraphicsDimensionColdBootKeys))]
    public void NativeGraphicsDimensionOddColdBootAddressErrorObservations(uint key)
    {
        Assert.True(TryLoadConfiguredRom(out var rom));
        var context = CreateNativeIntuitionOracle(rom, suppressInputDeviceOverlay: true,
            ntsc: false, ecs: false, chipRamBytes: 512 * 1024);
        try
        {
            Assert.NotEqual(0u, ContinueUntilNativeIntuitionPublished(context));
            var graphicsBase = FindLibrary(context.Bus, context.ExecBase, "graphics.library");
            Assert.NotEqual(0u, graphicsBase);
            AssertOriginalGraphicsRomEntries(context, graphicsBase, rom);
            var allocation = context.Allocate(92);
            var destination = allocation + 3;
            var handler = context.Bus.ReadLong(12); // RAM vector, not ROM instruction bytes
            var before = context.Machine.Cpu.State;
            var priorException = (before.LastExceptionVector,
                before.LastExceptionInstructionProgramCounter, before.LastExceptionA7);
            var observed = false;
            var outcome = context.InvokeCore(graphicsBase, (int)GraphicsLvo.GetDisplayInfoData,
                state =>
                {
                    state.A[0] = 0;
                    state.A[1] = destination;
                    state.D[0] = 88;
                    state.D[1] = 0x80001000;
                    state.D[2] = key;
                }, MaximumVectorInstructions, state =>
                {
                    var currentException = (state.LastExceptionVector,
                        state.LastExceptionInstructionProgramCounter, state.LastExceptionA7);
                    if (currentException == priorException || state.LastExceptionVector != 3)
                        return false;
                    observed = true;
                    var frame = state.A[7];
                    Assert.Equal(0u, frame & 1);
                    Assert.True(frame <= (uint)context.Machine.Options.ChipRamSize - 14 ||
                        frame >= 0x00C00000 && frame <= 0x00C80000 - 14,
                        "The captured 68000 address-error frame must be in configured RAM.");
                    // Do not read the instruction word or emit ROM bytes.
                    var status = context.Bus.ReadWord(frame) & 0x1F;
                    var address = context.Bus.ReadLong(frame + 2);
                    var savedSr = context.Bus.ReadWord(frame + 8);
                    var savedPc = context.Bus.ReadLong(frame + 10);
                    _output.WriteLine($"graphics:raw-dims-odd-fault:mode=0x{key:X8}:requested=88:destination=0x{destination:X8}:vector=3:fault=0x{address:X8}:status=0x{status:X2}:frame=0x{frame:X8}:sr=0x{savedSr:X4}:pc=0x{savedPc:X8}:handler=0x{state.ProgramCounter:X8}");
                    Assert.Equal(handler, state.ProgramCounter);
                    Assert.Equal(state.LastExceptionStatusRegister, savedSr);
                    Assert.Equal(state.LastExceptionStackedProgramCounter, savedPc);
                    Assert.Equal(1u, address & 1);
                    Assert.InRange(address, destination, destination + 65);
                    Assert.Equal(0, status & 0x10); // data write, not read
                    return true;
                });
            Assert.False(outcome.ReachedSentinel);
            Assert.True(observed, "No new address-error boundary was captured for the odd DIMS buffer.");
            AssertOriginalGraphicsRomEntries(context, graphicsBase, rom);
            // The fresh machine is discarded at the exception boundary. Do
            // not invoke FreeMem or resume ROM error handling on this frame.
        }
        finally { context.Machine.Dispose(); }
    }

    public static IEnumerable<object[]> GraphicsMonitorPointerPrefixCases()
    {
        foreach (var ntsc in new[] { false, true })
        foreach (var odd in new[] { false, true })
        foreach (var size in new uint[] { 17, 18, 19, 20 })
            yield return new object[] { ntsc, odd, size };
    }

    // One partial call per fresh machine. Capture349 returned all sizes17..20
    // for both destination parities on PAL/NTSC. Keep address-error diagnostics,
    // but freeze that observed return contract: a fault is now a regression.
    [GraphicsRomTheory]
    [MemberData(nameof(GraphicsMonitorPointerPrefixCases))]
    public void NativeGraphicsMonitorPointerPrefixObservations(bool ntsc, bool odd, uint size)
        => CaptureNativeGraphicsMonitorPrefix(ntsc, odd, size, pointerContract: true);

    public static IEnumerable<object[]> GraphicsMonitorViewPositionPrefixCases()
    {
        foreach (var ntsc in new[] { false, true })
        foreach (var odd in new[] { false, true })
        foreach (var size in new uint[] { 21, 22, 23, 24 })
            yield return new object[] { ntsc, odd, size };
    }

    // Capture352 proved exact21..24 transfers at both parities on PAL/NTSC,
    // with boot ViewPosition=(129,44). Freeze that observed contract.
    [GraphicsRomTheory]
    [MemberData(nameof(GraphicsMonitorViewPositionPrefixCases))]
    public void NativeGraphicsMonitorViewPositionPrefixObservations(bool ntsc, bool odd, uint size)
        => CaptureNativeGraphicsMonitorPrefix(ntsc, odd, size, pointerContract: false);

    private void CaptureNativeGraphicsMonitorPrefix(bool ntsc, bool odd, uint size, bool pointerContract)
    {
        var probeName = pointerContract ? "pointer" : "view-position";
        Assert.True(TryLoadConfiguredRom(out var rom));
        var context = CreateNativeIntuitionOracle(rom, suppressInputDeviceOverlay: true,
            ntsc: ntsc, ecs: false, chipRamBytes: 512 * 1024);
        try
        {
            Assert.NotEqual(0u, ContinueUntilNativeIntuitionPublished(context));
            var graphicsBase = FindLibrary(context.Bus, context.ExecBase, "graphics.library");
            Assert.NotEqual(0u, graphicsBase);
            AssertOriginalGraphicsRomEntries(context, graphicsBase, rom);
            var allocation = context.Allocate(92);
            var fullCount = context.Invoke(graphicsBase, (int)GraphicsLvo.GetDisplayInfoData, state =>
            {
                state.A[0] = 0;
                state.A[1] = allocation + 2;
                state.D[0] = 88;
                state.D[1] = 0x80002000;
                state.D[2] = 0;
            }).D[0];
            Assert.Equal(88u, fullCount);
            var full = ReadBytes(context.Bus, allocation + 2, 88);
            Assert.Equal(0x80002000u, BinaryPrimitives.ReadUInt32BigEndian(full));
            Assert.Equal(ntsc ? 0x11000u : 0x21000u,
                BinaryPrimitives.ReadUInt32BigEndian(full.AsSpan(4)));
            var pointer = BinaryPrimitives.ReadUInt32BigEndian(full.AsSpan(16));
            Assert.NotEqual(0u, pointer);
            if (!pointerContract)
            {
                var x = BinaryPrimitives.ReadInt16BigEndian(full.AsSpan(20));
                var y = BinaryPrimitives.ReadInt16BigEndian(full.AsSpan(22));
                _output.WriteLine($"graphics:raw-mntr-view-position-reference:ntsc={ntsc}:x={x}:y={y}:bytes={Convert.ToHexString(full.AsSpan(20, 4))}");
                Assert.Equal((short)129, x);
                Assert.Equal((short)44, y);
            }
            var destinationOffset = odd ? 3u : 2u;
            var destination = allocation + destinationOffset;
            for (uint offset = 0; offset < 92; offset++)
                context.Bus.WriteByte(allocation + offset, 0xA5, 0);
            var handler = context.Bus.ReadLong(12);
            var before = context.Machine.Cpu.State;
            var priorException = (before.LastExceptionVector,
                before.LastExceptionInstructionProgramCounter, before.LastExceptionA7);
            var faultObserved = false;
            var outcome = context.InvokeCore(graphicsBase, (int)GraphicsLvo.GetDisplayInfoData,
                state =>
                {
                    state.A[0] = 0;
                    state.A[1] = destination;
                    state.D[0] = size;
                    state.D[1] = 0x80002000;
                    state.D[2] = 0;
                }, MaximumVectorInstructions, state =>
                {
                    var currentException = (state.LastExceptionVector,
                        state.LastExceptionInstructionProgramCounter, state.LastExceptionA7);
                    if (currentException == priorException || state.LastExceptionVector != 3)
                        return false;
                    faultObserved = true;
                    var frame = state.A[7];
                    Assert.Equal(0u, frame & 1);
                    Assert.True(frame <= (uint)context.Machine.Options.ChipRamSize - 14 ||
                        frame >= 0x00C00000 && frame <= 0x00C80000 - 14);
                    var status = context.Bus.ReadWord(frame) & 0x1F;
                    var address = context.Bus.ReadLong(frame + 2);
                    var savedPc = context.Bus.ReadLong(frame + 10);
                    _output.WriteLine($"graphics:raw-mntr-{probeName}-fault:ntsc={ntsc}:odd={odd}:requested={size}:destination=0x{destination:X8}:vector=3:fault=0x{address:X8}:status=0x{status:X2}:pc=0x{savedPc:X8}");
                    Assert.True(odd);
                    Assert.Equal(handler, state.ProgramCounter);
                    Assert.Equal(state.LastExceptionStackedProgramCounter, savedPc);
                    Assert.Equal(state.LastExceptionStatusRegister, context.Bus.ReadWord(frame + 8));
                    Assert.Equal(1u, address & 1);
                    Assert.InRange(address, destination, destination + size - 1);
                    Assert.Equal(0, status & 0x10);
                    return true;
                });
            Assert.False(faultObserved, "The captured MNTR17..24 contract returns even at an odd destination.");
            if (faultObserved)
                Assert.False(outcome.ReachedSentinel);
            if (!faultObserved)
            {
                Assert.True(outcome.ReachedSentinel, "MNTR prefix neither returned nor raised an observed destination address error.");
                var copied = outcome.State.D[0];
                _output.WriteLine($"graphics:raw-mntr-{probeName}-return:ntsc={ntsc}:odd={odd}:requested={size}:copied={copied}:pointer=0x{pointer:X8}:buffer={Convert.ToHexString(ReadBytes(context.Bus, destination, (int)size))}");
                Assert.InRange(copied, 0u, size);
                Assert.Equal(size, copied);
                Assert.Equal(full.Take((int)copied), ReadBytes(context.Bus, destination, (int)copied));
                for (uint offset = 0; offset < destinationOffset; offset++)
                    Assert.Equal((byte)0xA5, context.Bus.ReadByte(allocation + offset));
                for (var offset = destinationOffset + copied; offset < 92; offset++)
                    Assert.Equal((byte)0xA5, context.Bus.ReadByte(allocation + offset));
            }
            AssertOriginalGraphicsRomEntries(context, graphicsBase, rom);
            // Never resume the guest's exception handler simply to free the
            // short-lived capture buffer. Machine disposal owns this boot.
            if (!faultObserved)
                context.Free(allocation, 92);
        }
        finally { context.Machine.Dispose(); }
    }

    private void CaptureNativeGraphicsDimensions(bool ecs, bool ntsc, int chipRamBytes = 512 * 1024,
        uint? onlyKey = null, uint? onlyMode = null)
    {
        Assert.True(TryLoadConfiguredRom(out var rom));
        // Unlike the Layers bootstrap, follow cold-start into Intuition instead
        // of abandoning graphics InitResident at its early hardware wait.
        // Retain512K expansion and explicitly select chip RAM and chipset/video
        // profile. Existing callers remain512K chip. This does not establish AGA.
        var context = CreateNativeIntuitionOracle(rom, suppressInputDeviceOverlay: true,
            ntsc: ntsc, ecs: ecs, chipRamBytes: chipRamBytes);
        try
        {
            Assert.Equal(chipRamBytes, context.Machine.Options.ChipRamSize);
            Assert.Equal(512 * 1024, context.Machine.Options.ExpansionRamSize);
            Assert.NotEqual(0u, ContinueUntilNativeIntuitionPublished(context));
            var graphicsBase = FindLibrary(context.Bus, context.ExecBase, "graphics.library");
            Assert.NotEqual(0u, graphicsBase);
            AssertOriginalEntries();
            WriteIntuitionMachineState("graphics-dimensions-depth", context);
            var allocation = context.Allocate(92);
            try
            {
                var seen = new HashSet<uint>();
                var captured = 0;
                var mode = uint.MaxValue;
                _output.WriteLine($"graphics:raw-records:profile=A500Boot:cpu=AccurateM68000:chip={chipRamBytes / 1024}K:expansion=512K:live-dma=true:chipset={(ecs ? "ECS" : "OCS")}:video={(ntsc ? "NTSC" : "PAL")}:fixture=3.1:chip-rev=0x{context.Bus.ReadByte(graphicsBase + (uint)GraphicsLayouts.GfxBaseChipRevBits0):X2}");
                while (true)
                {
                    mode = context.Invoke(graphicsBase, (int)GraphicsLvo.NextDisplayInfo,
                        state => state.D[0] = mode).D[0];
                    if (mode == uint.MaxValue)
                        break;
                    Assert.True(seen.Count < 512 && seen.Add(mode),
                        $"Display database enumeration did not terminate uniquely; mode=0x{mode:X8}.");

                    if (onlyKey is { } selectedKey && (mode & 0x0000EFFFu) != selectedKey)
                        continue;
                    if (onlyMode is { } selectedMode && mode != selectedMode)
                        continue;

                    var full = QueryDimensions(mode, 88);
                    // Record observations before assertions; no portable values
                    // or expected depth table participate in the ROM oracle.
                    _output.WriteLine($"graphics:raw-dims:mode=0x{mode:X8}:max-depth={BinaryPrimitives.ReadUInt16BigEndian(full.AsSpan(16))}:record={Convert.ToHexString(full)}");
                    Assert.Equal(0x80001000u, BinaryPrimitives.ReadUInt32BigEndian(full));
                    var recordId = (mode & 0xFFFF1000u) == 0 ? mode | (ntsc ? 0x00011000u : 0x00021000u) : mode;
                    Assert.Equal(recordId, BinaryPrimitives.ReadUInt32BigEndian(full.AsSpan(4)));
                    Assert.Equal(3u, BinaryPrimitives.ReadUInt32BigEndian(full.AsSpan(8)));
                    Assert.Equal(8u, BinaryPrimitives.ReadUInt32BigEndian(full.AsSpan(12)));
                    foreach (var length in Enumerable.Range(16, 50))
                    {
                        // Scalar bytes transfer individually; a rectangle is
                        // published only when all eight bytes fit. Compare the
                        // returned prefix against this ROM's own full record.
                        var copiedLength = length <= 26 ? length : 26 + 8 * ((length - 26) / 8);
                        Assert.Equal(full.Take(copiedLength), QueryDimensions(mode, length));
                    }
                    captured++;
                }
                // Merely publishing graphics.library is not proof of usable
                // display records. Empty/uninitialized databases fail explicitly.
                Assert.NotEmpty(seen);
                if (onlyMode.HasValue)
                    Assert.Equal(1, captured);
                else if (onlyKey.HasValue)
                    Assert.Equal(2, captured);
                else
                    Assert.Equal(seen.Count, captured);
                AssertOriginalEntries();
                _output.WriteLine($"graphics:raw-records:completed={captured}:enumerated={seen.Count}:scope={(ecs ? "ECS" : "OCS")}-{(ntsc ? "NTSC" : "PAL")}");
            }
            finally
            {
                context.Free(allocation, 92);
            }

            byte[] QueryDimensions(uint id, int length)
            {
                const uint destinationOffset = 2;
                for (uint offset = 0; offset < 92; offset++)
                    context.Bus.WriteByte(allocation + offset, 0xA5, 0);
                var copied = context.Invoke(graphicsBase, (int)GraphicsLvo.GetDisplayInfoData, state =>
                {
                    state.A[0] = 0;
                    state.A[1] = allocation + destinationOffset;
                    state.D[0] = (uint)length;
                    state.D[1] = 0x80001000;
                    state.D[2] = id;
                }).D[0];
                // Capture the observation even when a compatibility assertion
                // fails; structure allocation size is not transfer evidence.
                _output.WriteLine($"graphics:raw-dims-transfer:mode=0x{id:X8}:requested={length}:copied={copied}:buffer={Convert.ToHexString(ReadBytes(context.Bus, allocation + destinationOffset, length))}");
                // Independently captured331 ROM transfers: bytewise scalar
                // prefix, followed by complete eight-byte rectangles only.
                var expected = length <= 26 ? length : 26 + 8 * ((Math.Min(length, 66) - 26) / 8);
                Assert.Equal((uint)expected, copied);
                for (uint offset = 0; offset < destinationOffset; offset++)
                    Assert.Equal((byte)0xA5, context.Bus.ReadByte(allocation + offset));
                for (var offset = (uint)expected + destinationOffset; offset < 92; offset++)
                    Assert.Equal((byte)0xA5, context.Bus.ReadByte(allocation + offset));
                return ReadBytes(context.Bus, allocation + destinationOffset, expected);
            }

            void AssertOriginalEntries()
                => AssertOriginalGraphicsRomEntries(context, graphicsBase, rom);
        }
        finally
        {
            context.Machine.Dispose();
        }
    }

    // Observation collection deliberately does not assert the replacement's
    // structure-size/header assumptions. The compatibility fact above now uses
    // independently captured331 header/transfer rules; original failed TRXs
    // remain historical evidence of the corrected assumptions.
    [GraphicsRomFact]
    public void NativeGraphicsDisplayRecordTransferObservations()
        => CaptureNativeGraphicsDisplayRecordObservations(onlyMode: null);

    // Independent fresh-boot samples complement, rather than replace, the
    // continuous enumeration probe and its hardware-timing regression signal.
    [GraphicsRomTheory]
    [InlineData(0x00000000u)]
    [InlineData(0x00008000u)]
    [InlineData(0x00008020u)]
    [InlineData(0x00021000u)]
    [InlineData(0x00029000u)]
    [InlineData(0x00029020u)]
    [InlineData(0x00011000u)]
    [InlineData(0x00019000u)]
    [InlineData(0x00019020u)]
    public void NativeGraphicsDisplayRecordFreshBootObservations(uint mode)
        => CaptureNativeGraphicsDisplayRecordObservations(mode);

    // A bounded fresh boot per mode/profile fills every gap in the earlier
    // sparse transfer observations without hiding the continuous sweep failure.
    [GraphicsRomTheory]
    [InlineData(false, 0x00000000u)]
    [InlineData(false, 0x00008000u)]
    [InlineData(false, 0x00008020u)]
    [InlineData(false, 0x00021000u)]
    [InlineData(false, 0x00029000u)]
    [InlineData(false, 0x00029020u)]
    [InlineData(false, 0x00011000u)]
    [InlineData(false, 0x00019000u)]
    [InlineData(false, 0x00019020u)]
    [InlineData(true, 0x00000000u)]
    [InlineData(true, 0x00008000u)]
    [InlineData(true, 0x00008020u)]
    [InlineData(true, 0x00021000u)]
    [InlineData(true, 0x00029000u)]
    [InlineData(true, 0x00029020u)]
    [InlineData(true, 0x00011000u)]
    [InlineData(true, 0x00019000u)]
    [InlineData(true, 0x00019020u)]
    public void NativeGraphicsDisplayRecordDenseTransferObservations(bool ntsc, uint mode)
        => CaptureNativeGraphicsDenseTransfers(ntsc, mode, onlyTag: null);

    public static IEnumerable<object[]> DenseTransferRecordCases()
    {
        foreach (var ntsc in new[] { false, true })
        foreach (var mode in new uint[] { 0, 0x8000, 0x8020, 0x21000, 0x29000, 0x29020, 0x11000, 0x19000, 0x19020 })
        foreach (var tag in new uint[] { 0x80000000, 0x80001000, 0x80002000 })
            yield return new object[] { ntsc, mode, tag };
    }

    // Complementary independent samples, not a replacement for the longer
    // dense probe above and its retained Copper-reservation failure evidence.
    [GraphicsRomTheory]
    [MemberData(nameof(DenseTransferRecordCases))]
    public void NativeGraphicsDisplayRecordPerTagDenseTransferObservations(bool ntsc, uint mode, uint tag)
        => CaptureNativeGraphicsDenseTransfers(ntsc, mode, tag);

    private void CaptureNativeGraphicsDenseTransfers(bool ntsc, uint mode, uint? onlyTag)
    {
        Assert.True(TryLoadConfiguredRom(out var rom));
        var context = CreateNativeIntuitionOracle(rom, suppressInputDeviceOverlay: true, ntsc: ntsc);
        try
        {
            Assert.NotEqual(0u, ContinueUntilNativeIntuitionPublished(context));
            var graphicsBase = FindLibrary(context.Bus, context.ExecBase, "graphics.library");
            Assert.NotEqual(0u, graphicsBase);
            AssertOriginalGraphicsRomEntries(context, graphicsBase, rom);
            _output.WriteLine($"graphics:dense-profile:video={(ntsc ? "NTSC" : "PAL")}:cpu=AccurateM68000:chip=512K:expansion=512K:chipset=OCS:live-dma=true:mode=0x{mode:X8}");
            WriteIntuitionMachineState("graphics-dense-transfer", context);
            var allocation = context.Allocate(132);
            try
            {
                var queries = 0;
                foreach (var tag in onlyTag.HasValue ? new[] { onlyTag.Value } : new uint[] { 0x80000000, 0x80001000, 0x80002000 })
                {
                    var full = Query(tag, 128);
                    Assert.NotEmpty(full);
                    for (uint length = 0; length <= 128; length++)
                    {
                        var prefix = Query(tag, length);
                        Assert.True(prefix.Length <= full.Length);
                        Assert.Equal(full.Take(prefix.Length), prefix);
                    }
                }
                Assert.Equal(onlyTag.HasValue ? 130 : 390, queries);
                AssertOriginalGraphicsRomEntries(context, graphicsBase, rom);
                _output.WriteLine($"graphics:dense-complete:queries={queries}:scope=OCS-only:replacement-conformance=false");

                byte[] Query(uint tag, uint length)
                {
                    for (uint offset = 0; offset < 132; offset++)
                        context.Bus.WriteByte(allocation + offset, 0xA5, 0);
                    var copied = context.Invoke(graphicsBase, (int)GraphicsLvo.GetDisplayInfoData, state =>
                    {
                        state.A[0] = 0;
                        state.A[1] = allocation + 2;
                        state.D[0] = length;
                        state.D[1] = tag;
                        state.D[2] = mode;
                    }).D[0];
                    _output.WriteLine($"graphics:dense-transfer:video={(ntsc ? "NTSC" : "PAL")}:mode=0x{mode:X8}:tag=0x{tag:X8}:requested={length}:copied={copied}");
                    Assert.InRange(copied, 0u, length);
                    Assert.Equal((byte)0xA5, context.Bus.ReadByte(allocation));
                    Assert.Equal((byte)0xA5, context.Bus.ReadByte(allocation + 1));
                    for (var offset = copied + 2; offset < 132; offset++)
                        Assert.Equal((byte)0xA5, context.Bus.ReadByte(allocation + offset));
                    queries++;
                    return ReadBytes(context.Bus, allocation + 2, (int)copied);
                }
            }
            finally
            {
                context.Free(allocation, 132);
            }
        }
        finally
        {
            context.Machine.Dispose();
        }
    }

    private void CaptureNativeGraphicsDisplayRecordObservations(uint? onlyMode)
    {
        Assert.True(TryLoadConfiguredRom(out var rom));
        var context = CreateNativeIntuitionOracle(rom, suppressInputDeviceOverlay: true);
        try
        {
            Assert.NotEqual(0u, ContinueUntilNativeIntuitionPublished(context));
            var graphicsBase = FindLibrary(context.Bus, context.ExecBase, "graphics.library");
            Assert.NotEqual(0u, graphicsBase);
            AssertOriginalGraphicsRomEntries(context, graphicsBase, rom);
            var allocation = context.Allocate(132);
            try
            {
                var modes = new HashSet<uint>();
                var mode = uint.MaxValue;
                var queries = 0;
                var transfers = 0;
                _output.WriteLine("graphics:observation:profile=A500Pal512KBoot:cpu=AccurateM68000:live-dma=true:chipset=OCS:fixture=3.1");
                while (true)
                {
                    if (onlyMode.HasValue && modes.Count != 0)
                        break;
                    mode = onlyMode ?? context.Invoke(graphicsBase, (int)GraphicsLvo.NextDisplayInfo,
                        state => state.D[0] = mode).D[0];
                    if (mode == uint.MaxValue)
                        break;
                    Assert.True(modes.Count < 512 && modes.Add(mode),
                        $"Display enumeration is not bounded and unique: 0x{mode:X8}.");
                    foreach (var tag in new uint[] { 0x80000000, 0x80001000, 0x80002000, 0x80003000 })
                    foreach (var length in new uint[] { 0, 1, 15, 16, 17, 18, 19, 20, 43, 44, 55, 56, 65, 66, 67, 87, 88, 95, 96, 128 })
                    {
                        for (uint offset = 0; offset < 132; offset++)
                            context.Bus.WriteByte(allocation + offset, 0xA5, 0);
                        var copied = context.Invoke(graphicsBase, (int)GraphicsLvo.GetDisplayInfoData, state =>
                        {
                            state.A[0] = 0;
                            state.A[1] = allocation + 2;
                            state.D[0] = length;
                            state.D[1] = tag;
                            state.D[2] = mode;
                        }).D[0];
                        _output.WriteLine($"graphics:observation:mode=0x{mode:X8}:tag=0x{tag:X8}:requested={length}:copied={copied}:buffer={Convert.ToHexString(ReadBytes(context.Bus, allocation + 2, (int)length))}");
                        Assert.InRange(copied, 0u, length);
                        Assert.Equal((byte)0xA5, context.Bus.ReadByte(allocation));
                        Assert.Equal((byte)0xA5, context.Bus.ReadByte(allocation + 1));
                        for (var offset = copied + 2; offset < 132; offset++)
                            Assert.Equal((byte)0xA5, context.Bus.ReadByte(allocation + offset));
                        queries++;
                        if (copied > 0)
                            transfers++;
                    }
                }
                Assert.NotEmpty(modes);
                Assert.True(transfers > 0, "Published libraries did not provide usable display data.");
                AssertOriginalGraphicsRomEntries(context, graphicsBase, rom);
                _output.WriteLine($"graphics:observation:completed-modes={modes.Count}:queries={queries}:positive-transfers={transfers}:scope=OCS-only:not-a-replacement-conformance-pass");
            }
            finally
            {
                context.Free(allocation, 132);
            }
        }
        finally
        {
            context.Machine.Dispose();
        }
    }

    [GraphicsRomTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeGraphicsDefaultAliasBootAndDisplayFlagsObservations(bool ntsc)
    {
        Assert.True(TryLoadConfiguredRom(out var rom));
        var context = CreateNativeIntuitionOracle(rom, suppressInputDeviceOverlay: true, ntsc: ntsc);
        try
        {
            Assert.NotEqual(0u, ContinueUntilNativeIntuitionPublished(context));
            var graphicsBase = FindLibrary(context.Bus, context.ExecBase, "graphics.library");
            Assert.NotEqual(0u, graphicsBase);
            AssertOriginalGraphicsRomEntries(context, graphicsBase, rom);
            const uint displayFlagsOffset = 0xCE; // SDK gfxbase.i, independent of replacement constants
            var flagsAddress = graphicsBase + displayFlagsOffset;
            var bootFlags = context.Bus.ReadWord(flagsAddress);
            _output.WriteLine($"graphics:default-profile:OCS:video={(ntsc ? "NTSC" : "PAL")}:cpu=AccurateM68000:chip=512K:expansion=512K:live-dma=true:flags=0x{bootFlags:X4}");
            WriteIntuitionMachineState("graphics-default-profile", context);
            Assert.Equal(ntsc ? 1 : 4, bootFlags & 5);
            var allocation = context.Allocate(20);
            try
            {
                Observe("boot");
                // Observe, do not assume, whether this writable power-on field
                // remaps already constructed ROM display-database aliases.
                context.Bus.WriteWord(flagsAddress, (ushort)((bootFlags & ~5) | (ntsc ? 4 : 1)), 0);
                Observe("opposite-display-flags");
                context.Bus.WriteWord(flagsAddress, bootFlags, 0);
                Observe("restored-display-flags");
                AssertOriginalGraphicsRomEntries(context, graphicsBase, rom);
            }
            finally
            {
                context.Bus.WriteWord(flagsAddress, bootFlags, 0);
                context.Free(allocation, 20);
            }

            void Observe(string phase)
            {
                foreach (var mode in new uint[] { 0, 0x8000, 0x8020 })
                foreach (var tag in new uint[] { 0x80000000, 0x80001000, 0x80002000 })
                foreach (var length in new uint[] { 5, 6, 16 })
                {
                    for (uint offset = 0; offset < 20; offset++)
                        context.Bus.WriteByte(allocation + offset, 0xA5, 0);
                    var copied = context.Invoke(graphicsBase, (int)GraphicsLvo.GetDisplayInfoData, state =>
                    {
                        state.A[0] = 0;
                        state.A[1] = allocation + 2;
                        state.D[0] = length;
                        state.D[1] = tag;
                        state.D[2] = mode;
                    }).D[0];
                    _output.WriteLine($"graphics:alias-selection:video={(ntsc ? "NTSC" : "PAL")}:phase={phase}:flags=0x{context.Bus.ReadWord(flagsAddress):X4}:mode=0x{mode:X8}:tag=0x{tag:X8}:requested={length}:copied={copied}:buffer={Convert.ToHexString(ReadBytes(context.Bus, allocation + 2, (int)length))}");
                    Assert.Equal(length, copied);
                    // Independently captured on both OCS boot profiles: changing
                    // DisplayFlags alone does not remap constructed aliases.
                    var expectedHeader = new byte[16];
                    var owner = ntsc ? 0x00011000u : 0x00021000u;
                    var recordId = tag == 0x80002000u ? owner : owner | mode;
                    BinaryPrimitives.WriteUInt32BigEndian(expectedHeader, tag);
                    BinaryPrimitives.WriteUInt32BigEndian(expectedHeader.AsSpan(4), recordId);
                    BinaryPrimitives.WriteUInt32BigEndian(expectedHeader.AsSpan(8), 3);
                    BinaryPrimitives.WriteUInt32BigEndian(expectedHeader.AsSpan(12),
                        tag == 0x80000000u ? 4u : tag == 0x80001000u ? 8u : 9u);
                    Assert.Equal(expectedHeader.Take((int)length),
                        ReadBytes(context.Bus, allocation + 2, (int)length));
                    Assert.Equal((byte)0xA5, context.Bus.ReadByte(allocation));
                    Assert.Equal((byte)0xA5, context.Bus.ReadByte(allocation + 1));
                    for (var offset = copied + 2; offset < 20; offset++)
                        Assert.Equal((byte)0xA5, context.Bus.ReadByte(allocation + offset));
                }
            }
        }
        finally
        {
            context.Machine.Dispose();
        }
    }

    private static void AssertOriginalGraphicsRomEntries(OracleContext context, uint graphicsBase, byte[] rom)
    {
        var mappedRom = ReadBytes(context.Bus, GraphicsProbeRomBase, rom.Length);
        foreach (var lvo in new[] { GraphicsLvo.NextDisplayInfo, GraphicsLvo.GetDisplayInfoData })
        {
            var vector = unchecked((uint)((int)graphicsBase + (int)lvo));
            Assert.True(IsOriginalGraphicsRomEntry(context.Bus.ReadWord(vector),
                context.Bus.ReadLong(vector + 2), rom, mappedRom),
                $"{lvo} is not an absolute jump into the unchanged supplied ROM; no raw-ROM evidence may be claimed.");
        }
    }

    private static bool IsOriginalGraphicsRomEntry(
        ushort opcode, uint target, ReadOnlySpan<byte> suppliedRom, ReadOnlySpan<byte> mappedRom)
        => opcode == 0x4EF9 && suppliedRom.Length == 512 * 1024 &&
           target >= GraphicsProbeRomBase && target < 0x01000000 &&
           (target & 1) == 0 && suppliedRom.SequenceEqual(mappedRom);

    [Theory]
    [InlineData("original", true)]
    [InlineData("ram-target", false)]
    [InlineData("past-rom", false)]
    [InlineData("odd-target", false)]
    [InlineData("wrong-opcode", false)]
    [InlineData("patched-rom", false)]
    [InlineData("truncated-map", false)]
    [InlineData("wrong-size", false)]
    public void GraphicsRomProbeRejectsNonOriginalEntryEvidence(string scenario, bool expected)
    {
        var supplied = new byte[512 * 1024];
        var mapped = new byte[supplied.Length];
        ushort opcode = 0x4EF9;
        uint target = GraphicsProbeRomBase;
        switch (scenario)
        {
            case "ram-target": target = 0x00010000; break;
            case "past-rom": target = 0x01000000; break;
            case "odd-target": target++; break;
            case "wrong-opcode": opcode = 0x4E75; break;
            case "patched-rom": mapped[128] = 1; break;
            case "truncated-map": mapped = mapped[..^1]; break;
            case "wrong-size": supplied = supplied[..^1]; mapped = mapped[..^1]; break;
        }
        Assert.Equal(expected, IsOriginalGraphicsRomEntry(opcode, target, supplied, mapped));
    }
}
