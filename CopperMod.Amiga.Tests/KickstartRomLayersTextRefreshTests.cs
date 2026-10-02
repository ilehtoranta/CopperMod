using Amiga;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed partial class KickstartRomLayersDifferentialTests
{
    [Fact]
    public void CopperStartTextDuringNonemptyDamageRefreshPreservesOwnership()
    {
        var context = CreateCopperStartOracle();
        try { _ = TraceTextDuringDamageRefresh(context); }
        finally { context.Machine.Dispose(); }
    }

    [Fact]
    public void TextDuringNonemptyDamageRefreshMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native Text refresh differential NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        var copperStart = CreateCopperStartOracle();
        string[] expected;
        try { expected = TraceTextDuringDamageRefresh(copperStart); }
        finally { copperStart.Machine.Dispose(); }

        // No graphics probe gateways or software BltBitMap leaf: native Text,
        // its delegated graphics calls, and its blits execute unchanged.
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
            Assert.Equal((ushort)40, ReadOracleLibraryVersion(native.Bus, native.LayersBase));
            var nativeMemory = new LayersTestGuestMemory(native.Bus);
            var vector = unchecked((uint)((int)native.GraphicsBase + (int)GraphicsLvo.Text));
            Assert.False(native.Bus.HasHostGateway(vector));
            Assert.Equal((ushort)0x4EF9, native.Bus.ReadWord(vector));
            Assert.InRange(native.Bus.ReadLong(vector + 2), 0x00F8_0000u, 0x00FF_FFFFu);
            _output.WriteLine("G223 configured V40.63: A500 PAL OCS, 512 KiB CHIP, " +
                "AccurateM68000/live Agnus; native graphics vectors uninstrumented. " +
                $"layers={ReadOracleLibraryVersion(native.Bus, native.LayersBase)}." +
                $"{LayersLibraryCodec.ReadRevision(ref nativeMemory, APTR.FromPointer(native.LayersBase))}; " +
                $"graphics={ReadOracleLibraryVersion(native.Bus, native.GraphicsBase)}." +
                $"{LayersLibraryCodec.ReadRevision(ref nativeMemory, APTR.FromPointer(native.GraphicsBase))}");
            Assert.Equal(expected, TraceTextDuringDamageRefresh(native));
        }
        finally { native.Machine.Dispose(); }
    }

    private string[] TraceTextDuringDamageRefresh(OracleContext context)
    {
        const int width = 32;
        const int height = 16;
        const int textX = 2;
        const int baselineY = 7;
        const byte initialPattern = 0x96;
        var trace = new List<string>();
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(width, height, 1, 0,
            planeMemoryFlags: Exec.MemoryFlags.Chip);
        Assert.Equal(display.Planes[0], context.Bus.MaskChipDmaAddress(display.Planes[0]));
        Assert.InRange(display.Planes[0], 1u, checked((uint)(context.Machine.Options.ChipRamSize -
            display.BytesPerRow * display.Height)));
        var layer = context.CreateLayer(LayersLvo.CreateUpfrontLayer, layerInfo,
            display.Address, 0, LayerCreationFlags.Simple, 0, 0, width - 1, height - 1);
        var cover = context.CreateLayer(LayersLvo.CreateUpfrontLayer, layerInfo,
            display.Address, 0, LayerCreationFlags.Simple, 4, 3, 11, 10);
        Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = cover).D[0]);
        context.WaitForBlitterIdle();
        Assert.False(context.Bus.Blitter.Busy);
        var damage = ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.DamageList);
        var damageBefore = DescribeRegion(context, damage);
        Assert.Contains("coverage=[y=3,10:x=4,11]", damageBefore);

        var region = context.CreateSingleRectangleRegion(7, 0, 13, 7);
        var regionNode = ReadOracleRegionFirst(context.Bus, region);
        var regionBytes = Bytes(region, checked((int)Region.Size));
        var nodeBytes = Bytes(regionNode, checked((int)RegionRectangle.Size));
        Assert.Equal(0u, context.InvokeLayers(LayersLvo.InstallClipRegion, state =>
        {
            state.A[0] = layer;
            state.A[1] = region;
        }).D[0]);
        var pendingDamage = DescribeRegion(context, damage);
        var stableCoverage = VisibleCoverage();
        var stableHead = ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.ClipRect);
        var projection = Enumerable.Range(3, 5)
            .SelectMany(y => Enumerable.Range(7, 5).Select(x => y * width + x)).ToArray();
        var rastPort = ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.RastPort);
        var originalFont = context.Bus.ReadLong(rastPort + GraphicsLayout.RastPort.Font);
        using var font = CreateClassicTextFontFixture(context);
        context.InvokeGraphics((int)GraphicsLvo.SetFont, state =>
        {
            state.A[0] = font.Font;
            state.A[1] = rastPort;
        });
        var memory = new LayersTestGuestMemory(context.Bus);
        Assert.Equal(font.Font, context.Bus.ReadLong(rastPort + GraphicsLayout.RastPort.Font));
        Assert.Equal((short)7, LayersRastPortCodec.ReadTextBaseline(ref memory, APTR.FromPointer(rastPort)));
        Assert.Equal((short)8, LayersRastPortCodec.ReadTextHeight(ref memory, APTR.FromPointer(rastPort)));
        SetPenOrMode(GraphicsLvo.SetAPen, 1);
        SetPenOrMode(GraphicsLvo.SetBPen, 0);
        SetPenOrMode(GraphicsLvo.SetDrMd, 1); // JAM2, including native minterm setup.
        SetPenOrMode(GraphicsLvo.SetWriteMask, 0xFF);
        var text = context.Allocate(2);
        var expectedPixels = Enumerable.Repeat(initialPattern,
            display.BytesPerRow * display.Height).ToArray();
        for (var offset = 0; offset < expectedPixels.Length; offset++)
            context.Bus.WriteByte(display.Planes[0] + checked((uint)offset), initialPattern, 0);
        MoveCursor();
        Capture("before-update");

        Begin("first-begin");
        Draw("AB", "first-text");
        End(complete: false, "partial-end");
        Assert.Equal(pendingDamage, DescribeRegion(context, damage));
        Begin("retry-begin");
        Draw("BA", "retry-text");
        End(complete: true, "complete-end");
        var completedDamage = ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.DamageList);
        Assert.Equal(damage, completedDamage);
        Assert.Equal(new byte[checked((int)Region.Size)], Bytes(completedDamage, checked((int)Region.Size)));
        Assert.Equal(expectedPixels, Bytes(display.Planes[0], expectedPixels.Length));

        Assert.Equal(region, context.InvokeLayers(LayersLvo.InstallClipRegion, state =>
        {
            state.A[0] = layer;
            state.A[1] = 0;
        }).D[0]);
        AssertCallerRegion();
        context.InvokeGraphics((int)GraphicsLvo.SetFont, state =>
        {
            state.A[0] = originalFont;
            state.A[1] = rastPort;
        });
        Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = layer).D[0]);
        context.DisposeLayerInfo(layerInfo);
        context.Free(text, 2);
        context.Free(regionNode, RegionRectangle.Size);
        context.Free(region, Region.Size);
        context.FreePlanarBitMap(display);
        return trace.ToArray();

        void SetPenOrMode(GraphicsLvo lvo, uint value) => context.InvokeGraphics((int)lvo, state =>
        {
            // SetWriteMask uses A0; the older pen/mode setters use A1.
            state.A[lvo == GraphicsLvo.SetWriteMask ? 0 : 1] = rastPort;
            state.D[0] = value;
        });

        void MoveCursor() => context.InvokeGraphics((int)GraphicsLvo.Move, state =>
        {
            state.A[1] = rastPort;
            state.D[0] = textX;
            state.D[1] = baselineY;
        });

        void Begin(string phase)
        {
            Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.BeginUpdate,
                state => state.A[0] = layer).D[0]);
            Assert.Equal(projection, VisibleCoverage());
            Assert.Equal(pendingDamage, DescribeRegion(context, damage));
            Assert.True((ReadOracleLayerFlags(context.Bus, layer) & LayerFlags.Updating) != 0);
            // saveClipRects is implementation-private rollback storage, not
            // a native BeginUpdate saved-head ABI (graphics/clip.h).
            if (!context.Native)
                Assert.Equal(stableHead, LayersLayerCodec.ReadSaveClipRects(ref memory,
                    APTR.FromPointer(layer)).Raw);
            Capture(phase);
        }

        void Draw(string characters, string phase)
        {
            MoveCursor();
            var activeHead = ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.ClipRect);
            var activeFlags = ReadOracleLayerFlags(context.Bus, layer);
            var activeDamage = ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.DamageList);
            for (var index = 0; index < characters.Length; index++)
                context.Bus.WriteByte(text + checked((uint)index), (byte)characters[index], 0);
            var setChanges = 0;
            var clearChanges = 0;
            // Independent pixel oracle: do not derive expected pixels from
            // native or portable raster operations, or only compare a hash.
            for (var y = 3; y <= 7; y++)
            for (var x = 7; x <= 11; x++)
            {
                var relative = x - textX;
                var row = font.GlyphRows((byte)characters[relative / 8])[y];
                var set = (row & (0x80 >> (relative & 7))) != 0;
                var offset = y * display.BytesPerRow + x / 8;
                var mask = (byte)(0x80 >> (x & 7));
                var wasSet = (expectedPixels[offset] & mask) != 0;
                if (set && !wasSet) setChanges++;
                if (!set && wasSet) clearChanges++;
                expectedPixels[offset] = set ? (byte)(expectedPixels[offset] | mask)
                    : (byte)(expectedPixels[offset] & ~mask);
            }
            Assert.True(setChanges > 0 && clearChanges > 0,
                "Both JAM2 foreground setting and background clearing must be observable.");
            context.InvokeGraphics((int)GraphicsLvo.Text, state =>
            {
                state.A[0] = text;
                state.A[1] = rastPort;
                state.D[0] = checked((uint)characters.Length);
            });
            context.WaitForBlitterIdle();
            Assert.False(context.Bus.Blitter.Busy);
            Capture(phase);
            Assert.Equal(expectedPixels, Bytes(display.Planes[0], expectedPixels.Length));
            Assert.Equal((short)18, LayersRastPortCodec.ReadCurrentX(ref memory, APTR.FromPointer(rastPort)));
            Assert.Equal((short)baselineY, LayersRastPortCodec.ReadCurrentY(ref memory, APTR.FromPointer(rastPort)));
            Assert.Equal(activeHead, ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.ClipRect));
            Assert.Equal(activeFlags, ReadOracleLayerFlags(context.Bus, layer));
            Assert.Equal(activeDamage, ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.DamageList));
            Assert.Equal(pendingDamage, DescribeRegion(context, damage));
            Assert.Equal(projection, VisibleCoverage());
        }

        void End(bool complete, string phase)
        {
            context.InvokeLayers(LayersLvo.EndUpdate, state =>
            {
                state.A[0] = layer;
                state.D[0] = complete ? 1u : 0u;
            });
            Assert.Equal(stableCoverage, VisibleCoverage());
            if (!context.Native)
            {
                // CopperStart restores its saved nodes. Native may retile
                // the stable coverage, so allocation identity is not parity.
                Assert.Equal(stableHead, ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.ClipRect));
                Assert.True(LayersLayerCodec.ReadSaveClipRects(ref memory, APTR.FromPointer(layer)).IsNull);
            }
            Assert.Equal(damage, ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.DamageList));
            var flags = ReadOracleLayerFlags(context.Bus, layer);
            Assert.True((flags & LayerFlags.Updating) == 0);
            Assert.True((flags & LayerFlags.Refresh) != 0);
            Capture(phase);
        }

        void Capture(string phase)
        {
            AssertCallerRegion();
            Assert.Equal(region, ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.ClipRegion));
            const LayerFlags publicFlags = LayerFlags.Simple | LayerFlags.Smart | LayerFlags.Super |
                LayerFlags.Backdrop | LayerFlags.Updating | LayerFlags.Refresh | LayerFlags.ClipRectsLost;
            var row = $"text-refresh:{phase}:flags={ReadOracleLayerFlags(context.Bus, layer) & publicFlags}:" +
                $"damage={DescribeRegion(context, ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.DamageList))}:" +
                $"visible=[{string.Join(',', VisibleCoverage())}]:" +
                $"pixels={Convert.ToHexString(Bytes(display.Planes[0], expectedPixels.Length))}:" +
                $"cursor={LayersRastPortCodec.ReadCurrentX(ref memory, APTR.FromPointer(rastPort))}," +
                $"{LayersRastPortCodec.ReadCurrentY(ref memory, APTR.FromPointer(rastPort))}:caller-region=unchanged";
            trace.Add(row);
            _output.WriteLine((context.Native ? "Native " : "CopperStart ") + row);
        }

        void AssertCallerRegion()
        {
            Assert.Equal(regionBytes, Bytes(region, regionBytes.Length));
            Assert.Equal(nodeBytes, Bytes(regionNode, nodeBytes.Length));
        }

        byte[] Bytes(uint address, int count) => Enumerable.Range(0, count)
            .Select(offset => context.Bus.ReadByte(address + checked((uint)offset))).ToArray();

        int[] VisibleCoverage()
        {
            var coverage = new SortedSet<int>();
            var seen = new HashSet<uint>();
            for (var node = ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.ClipRect);
                 node != 0; node = ReadOracleClipRectPointer(context.Bus, node, OracleClipRectPointer.Next))
            {
                Assert.True(seen.Count < 256 && seen.Add(node));
                Assert.True((node & 1) == 0 && context.Bus.IsMappedMemoryRange(node, checked((int)ClipRect.Size)));
                if (ReadOracleClipRectPointer(context.Bus, node, OracleClipRectPointer.ObscuringLayer) != 0) continue;
                var rectangle = ReadOracleClipRectBounds(context.Bus, node);
                Assert.InRange((int)rectangle.MinX, 0, width - 1);
                Assert.InRange((int)rectangle.MaxX, rectangle.MinX, width - 1);
                Assert.InRange((int)rectangle.MinY, 0, height - 1);
                Assert.InRange((int)rectangle.MaxY, rectangle.MinY, height - 1);
                for (var y = (int)rectangle.MinY; y <= rectangle.MaxY; y++)
                    for (var x = (int)rectangle.MinX; x <= rectangle.MaxX; x++)
                        Assert.True(coverage.Add(y * width + x), "Visible ClipRects must not overlap.");
            }
            return coverage.ToArray();
        }
    }
}
