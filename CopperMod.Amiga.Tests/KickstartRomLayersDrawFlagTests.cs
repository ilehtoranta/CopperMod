using Amiga;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed partial class KickstartRomLayersDifferentialTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopperStartDrawClippingPreservesFirstDotAndPhase(bool zeroWriteMask)
    {
        var context = CreateCopperStartOracle();
        try { _ = TraceDrawClippingFirstDotAndPhase(context, zeroWriteMask); }
        finally { context.Machine.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DrawClippingFirstDotAndPhaseMatchesNativeV4063(bool zeroWriteMask)
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native Draw clipping flags NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            foreach (var lvo in new[] { GraphicsLvo.Move, GraphicsLvo.Draw })
            {
                var vector = unchecked((uint)((int)native.GraphicsBase + (int)lvo));
                Assert.False(native.Bus.HasHostGateway(vector));
                Assert.Equal((ushort)0x4EF9, native.Bus.ReadWord(vector));
                Assert.InRange(native.Bus.ReadLong(vector + 2), 0x00F8_0000u, 0x00FF_FFFFu);
            }
            expected = TraceDrawClippingFirstDotAndPhase(native, zeroWriteMask);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try { Assert.Equal(expected, TraceDrawClippingFirstDotAndPhase(copperStart, zeroWriteMask)); }
        finally { copperStart.Machine.Dispose(); }
    }

    private string[] TraceDrawClippingFirstDotAndPhase(OracleContext context, bool zeroWriteMask)
    {
        const int width = 32;
        const int height = 16;
        const ushort pattern = 0xA640;
        const byte initialPixels = 0xE6;
        // A non-line flag is deliberately set so preservation is observable.
        const RastPortFlags nonLineFlags = RastPortFlags.NoCrossFill;
        var mask = zeroWriteMask ? (byte)0 : (byte)0xFF;
        var trace = new List<string>();
        var memory = new LayersTestGuestMemory(context.Bus);
        var info = context.NewLayerInfo();
        var image = context.CreatePlanarBitMap(width, height, 1, 0,
            planeMemoryFlags: Exec.MemoryFlags.Chip);
        Assert.Equal(image.Planes[0], context.Bus.MaskChipDmaAddress(image.Planes[0]));
        Assert.InRange(image.Planes[0], 1u, checked((uint)(context.Machine.Options.ChipRamSize -
            image.BytesPerRow * image.Height)));
        var layer = context.CreateLayer(LayersLvo.CreateUpfrontLayer, info, image.Address, 0,
            LayerCreationFlags.Simple, 0, 0, width - 1, height - 1);
        var rp = LayersLayerCodec.ReadRastPort(ref memory, APTR.FromPointer(layer));
        var region = context.CreateSingleRectangleRegion(7, 0, 11, height - 1);
        var node = ReadOracleRegionFirst(context.Bus, region);
        Assert.Equal(0u, context.InvokeLayers(LayersLvo.InstallClipRegion,
            state => { state.A[0] = layer; state.A[1] = region; }).D[0]);
        context.WaitForBlitterIdle();

        foreach (var mode in new byte[] { 0, 1, 2 })
        {
            RunCase("unlayered", mode, 2, 4, 9, 4, layered: false,
                retainsFirstDot: false, static (_, _) => true);
            RunCase("visible-start", mode, 7, 4, 9, 4, layered: true,
                retainsFirstDot: false, InCallerRectangle);
            RunCase("clipped-start", mode, 2, 4, 9, 4, layered: true,
                retainsFirstDot: true, InCallerRectangle);
            RunCase("clipped-end", mode, 9, 4, 15, 4, layered: true,
                retainsFirstDot: true, InCallerRectangle);
            RunCase("visible-start-clipped-end", mode, 7, 4, 15, 4, layered: true,
                retainsFirstDot: true, InCallerRectangle);
            RunCase("hidden", mode, 2, 4, 3, 4, layered: true,
                retainsFirstDot: true, InCallerRectangle);
        }

        Assert.Equal(region, context.InvokeLayers(LayersLvo.InstallClipRegion,
            state => { state.A[0] = layer; state.A[1] = 0; }).D[0]);
        var secondNode = context.Allocate(RegionRectangle.Size);
        LayersRegionCodec.WriteBounds(ref memory, APTR.FromPointer(region),
            LayersRectangleCodec.Create(7, 3, 11, 7));
        LayersRegionRectangleCodec.WriteBounds(ref memory, APTR.FromPointer(node),
            LayersRectangleCodec.Create(0, 0, 2, 2));
        LayersRegionRectangleCodec.WriteNext(ref memory, APTR.FromPointer(node), APTR.FromPointer(secondNode));
        LayersRegionRectangleCodec.WritePrevious(ref memory, APTR.FromPointer(secondNode), APTR.FromPointer(node));
        LayersRegionRectangleCodec.WriteNext(ref memory, APTR.FromPointer(secondNode), APTR.Null);
        LayersRegionRectangleCodec.WriteBounds(ref memory, APTR.FromPointer(secondNode),
            LayersRectangleCodec.Create(3, 3, 4, 4));
        Assert.Equal(0u, context.InvokeLayers(LayersLvo.InstallClipRegion,
            state => { state.A[0] = layer; state.A[1] = region; }).D[0]);
        context.WaitForBlitterIdle();
        foreach (var mode in new byte[] { 0, 1, 2 })
        {
            // Every logical diagonal sample is visible, but no one ClipRect
            // contains the complete line. Native retains FRST_DOT here too.
            RunCase("fragmented-full-diagonal", mode, 7, 3, 11, 7, layered: true,
                retainsFirstDot: true, static (x, y) =>
                    (x >= 7 && x <= 9 && y >= 3 && y <= 5) ||
                    (x >= 10 && x <= 11 && y >= 6 && y <= 7));
        }

        Assert.Equal(region, context.InvokeLayers(LayersLvo.InstallClipRegion,
            state => { state.A[0] = layer; state.A[1] = 0; }).D[0]);
        Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = layer).D[0]);
        context.DisposeLayerInfo(info);
        context.Free(secondNode, RegionRectangle.Size);
        context.Free(node, RegionRectangle.Size);
        context.Free(region, Region.Size);
        context.FreePlanarBitMap(image);
        return trace.ToArray();

        void RunCase(string name, byte mode, int startX, int startY, int endX, int endY,
            bool layered, bool retainsFirstDot, Func<int, int, bool> visible)
        {
            LayersRastPortCodec.WriteLayer(ref memory, rp,
                layered ? APTR.FromPointer(layer) : APTR.Null);
            try
            {
                SetPenOrMode(GraphicsLvo.SetAPen, 1);
                SetPenOrMode(GraphicsLvo.SetBPen, 0);
                SetPenOrMode(GraphicsLvo.SetDrMd, mode);
                SetPenOrMode(GraphicsLvo.SetWriteMask, mask);
                // Exact SetDrPt macro semantics, with ONE_DOT disabled.
                // https://d0.se/include/graphics/gfxmacros.h
                LayersRastPortCodec.WriteLinePattern(ref memory, rp, pattern);
                LayersRastPortCodec.WriteFlags(ref memory, rp, nonLineFlags | RastPortFlags.FirstDot);
                LayersRastPortCodec.WriteLinePatternCount(ref memory, rp, 15);
                var expectedPixels = Enumerable.Repeat(initialPixels, image.BytesPerRow * height).ToArray();
                for (var offset = 0; offset < expectedPixels.Length; offset++)
                    context.Bus.WriteByte(image.Planes[0] + checked((uint)offset), initialPixels, 0);
                context.InvokeGraphics((int)GraphicsLvo.Move, state =>
                {
                    state.A[1] = rp.Raw;
                    state.D[0] = checked((uint)startX);
                    state.D[1] = checked((uint)startY);
                });
                AssertState(startX, startY, 15, firstDot: true);
                var headBefore = ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.ClipRect);
                var flagsBefore = ReadOracleLayerFlags(context.Bus, layer);
                var damageBefore = ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.DamageList);
                var damageDescription = DescribeRegion(context, damageBefore);
                var regionBefore = Bytes(region, checked((int)Region.Size));
                var firstNodeBefore = Bytes(node, checked((int)RegionRectangle.Size));
                var nextNode = LayersRegionRectangleCodec.ReadNext(ref memory, APTR.FromPointer(node)).Raw;
                var nextNodeBefore = nextNode == 0 ? Array.Empty<byte>() :
                    Bytes(nextNode, checked((int)RegionRectangle.Size));
                var distance = Math.Max(Math.Abs(endX - startX), Math.Abs(endY - startY));
                // All cases are horizontal or a positive 45-degree diagonal:
                // this independent oracle needs no production line walker.
                for (var step = 0; step <= distance && mask != 0; step++)
                {
                    var x = startX + step;
                    var y = startY + (endY == startY ? 0 : step);
                    if (!visible(x, y)) continue;
                    var set = (pattern & (0x8000 >> step)) != 0;
                    if (!set && mode != 1) continue;
                    var offset = y * image.BytesPerRow + x / 8;
                    var bit = (byte)(0x80 >> (x & 7));
                    if (mode == 2) expectedPixels[offset] ^= bit;
                    else expectedPixels[offset] = set ? (byte)(expectedPixels[offset] | bit) :
                        (byte)(expectedPixels[offset] & ~bit);
                }
                context.InvokeGraphics((int)GraphicsLvo.Draw, state =>
                {
                    state.A[1] = rp.Raw;
                    state.D[0] = checked((uint)endX);
                    state.D[1] = checked((uint)endY);
                });
                context.WaitForBlitterIdle();
                Assert.False(context.Bus.Blitter.Busy);
                var expectedPhase = checked((byte)((15 - distance) & 15));
                var row = $"draw-flags:{name}:mode={mode}:mask={mask:X2}:" +
                    $"cursor={LayersRastPortCodec.ReadCurrentX(ref memory, rp)}," +
                    $"{LayersRastPortCodec.ReadCurrentY(ref memory, rp)}:" +
                    $"phase={LayersRastPortCodec.ReadLinePatternCount(ref memory, rp)}:" +
                    $"flags={LayersRastPortCodec.ReadFlags(ref memory, rp)}:" +
                    $"pixels={Convert.ToHexString(Bytes(image.Planes[0], expectedPixels.Length))}";
                _output.WriteLine((context.Native ? "Native " : "CopperStart ") + row);
                trace.Add(row);
                AssertState(endX, endY, expectedPhase, retainsFirstDot);
                Assert.Equal(expectedPixels, Bytes(image.Planes[0], expectedPixels.Length));
                Assert.Equal(headBefore, ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.ClipRect));
                Assert.Equal(flagsBefore, ReadOracleLayerFlags(context.Bus, layer));
                Assert.Equal(damageBefore, ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.DamageList));
                Assert.Equal(damageDescription, DescribeRegion(context, damageBefore));
                Assert.Equal(region, ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.ClipRegion));
                Assert.Equal(regionBefore, Bytes(region, regionBefore.Length));
                Assert.Equal(firstNodeBefore, Bytes(node, firstNodeBefore.Length));
                if (nextNode != 0) Assert.Equal(nextNodeBefore, Bytes(nextNode, nextNodeBefore.Length));

                void AssertState(int x, int y, byte phase, bool firstDot)
                {
                    Assert.Equal(checked((short)x), LayersRastPortCodec.ReadCurrentX(ref memory, rp));
                    Assert.Equal(checked((short)y), LayersRastPortCodec.ReadCurrentY(ref memory, rp));
                    Assert.Equal(pattern, LayersRastPortCodec.ReadLinePattern(ref memory, rp));
                    Assert.Equal(phase, LayersRastPortCodec.ReadLinePatternCount(ref memory, rp));
                    Assert.Equal(nonLineFlags | (firstDot ? RastPortFlags.FirstDot : RastPortFlags.None),
                        LayersRastPortCodec.ReadFlags(ref memory, rp));
                    Assert.Equal((byte)1, LayersRastPortCodec.ReadForegroundPen(ref memory, rp));
                    Assert.Equal((byte)0, LayersRastPortCodec.ReadBackgroundPen(ref memory, rp));
                    Assert.Equal(mode, LayersRastPortCodec.ReadDrawMode(ref memory, rp));
                    Assert.Equal(mask, LayersRastPortCodec.ReadMask(ref memory, rp));
                }
            }
            finally { LayersRastPortCodec.WriteLayer(ref memory, rp, APTR.FromPointer(layer)); }
        }

        void SetPenOrMode(GraphicsLvo lvo, uint value) => context.InvokeGraphics((int)lvo, state =>
        {
            // SetWriteMask uses A0; the older pen/mode setters use A1.
            state.A[lvo == GraphicsLvo.SetWriteMask ? 0 : 1] = rp.Raw;
            state.D[0] = value;
        });

        static bool InCallerRectangle(int x, int y) => x >= 7 && x <= 11 && y >= 0 && y < height;

        byte[] Bytes(uint address, int count) => Enumerable.Range(0, count)
            .Select(offset => context.Bus.ReadByte(address + checked((uint)offset))).ToArray();
    }
}
