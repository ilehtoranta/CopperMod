using Amiga;
using Copper68k;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed partial class CopperStartLayersBootTests
{
    [Fact]
    public void InstalledGraphicsGatewayExecutesAllTwentyNineLayerRasterVectorsUnderGuards()
    {
        var machine = CreateRtgMachine();
        var boot = new AmigaBootController(machine);
        boot.StartBootFromDisk(CreateBootableDisk());
        var bus = machine.Bus;
        var libraryBase = boot.CopperStartLayersLibraryBase;
        var destinationInfo = NewLayerInfo(bus, libraryBase);
        var sourceInfo = NewLayerInfo(bus, libraryBase);
        var destinationBitMap = AllocateRtgBitMap(boot, bus, 16, 16);
        var sourceBitMap = AllocateRtgBitMap(boot, bus, 16, 16);
        var destination = CreateLayerOnBitMaps(
            bus,
            libraryBase,
            destinationInfo,
            destinationBitMap,
            0,
            LayerCreationFlags.Simple,
            0,
            0,
            15,
            15);
        var source = CreateLayerOnBitMaps(
            bus,
            libraryBase,
            sourceInfo,
            sourceBitMap,
            0,
            LayerCreationFlags.Simple,
            0,
            0,
            15,
            15);
        var areaInfo = AllocateGuestMemory(bus, AreaInfo.Size);
        const uint areaStorageBytes = 64;
        var areaStorage = AllocateGuestMemory(bus, areaStorageBytes);
        var tmpRas = AllocateGuestMemory(bus, TmpRas.Size);
        const uint tmpRasterBytes = 512;
        var tmpRaster = AllocateGuestMemory(bus, tmpRasterBytes);
        const uint templateBytes = 4;
        var template = AllocateGuestMemory(bus, templateBytes);
        const uint pointBytes = 8;
        var points = AllocateGuestMemory(bus, pointBytes);
        const uint pixelArrayBytes = 32;
        var pixelArray = AllocateGuestMemory(bus, pixelArrayBytes);
        const uint tagBytes = 24;
        var tags = AllocateGuestMemory(bus, tagBytes);
        const uint maskBytes = 32;
        var maskPlane = AllocateGuestMemory(bus, maskBytes);
        const uint chunkyBytes = 16;
        var chunky = AllocateGuestMemory(bus, chunkyBytes);

        try
        {
            FillRtgBitMap(
                boot,
                bus,
                sourceBitMap,
                (x, y) => checked((byte)(1 + ((y * 16 + x) & 0x7F))));
            FillRtgBitMap(boot, bus, destinationBitMap, (_, _) => 0);
            ConfigureRastPort(destination.RastPort);

            // The scheduler-aware layer gateway must preserve the same
            // empty-string Text contract as the ordinary graphics adapter.
            // Use an unmapped text pointer and keep the valid layered
            // RastPort so the call reaches TryInvokeGraphicsRaster rather
            // than bypassing the layer gateway altogether.
            var zeroText = new M68kCpuState
            {
                A =
                {
                    [0] = 0xFFFF_FFFCu,
                    [1] = destination.RastPort
                },
                D = { [0] = 0u, [1] = 0xA5A5_5A5Au },
                Cycles = 211
            };
            var zeroTextMask = boot.CopperStartLayersRasterProviderExecutionMaskForTest;
            Assert.True(InvokeHostTrap(
                bus,
                Lvo(AmigaKickstartHost.GraphicsLibraryBase, (int)GraphicsLvo.Text),
                zeroText));
            Assert.Equal(0u, zeroText.D[0]);
            Assert.Equal(211, zeroText.Cycles);
            Assert.Equal(
                zeroTextMask,
                boot.CopperStartLayersRasterProviderExecutionMaskForTest);

            InitializeAreaAndTemporaryRaster();
            InitializeFont();
            InitializeSources();

            InvokeRaster(GraphicsLvo.BltTemplate, state =>
            {
                state.A[0] = template;
                state.A[1] = destination.RastPort;
                state.D[1] = 2;
                state.D[4] = 4;
                state.D[5] = 2;
            });
            InvokeRaster(GraphicsLvo.ClearEOL,
                state => state.A[1] = destination.RastPort);
            InvokeRaster(GraphicsLvo.ClearScreen,
                state => state.A[1] = destination.RastPort);
            bus.WriteByte(chunky, (byte)'A', 0);
            InvokeRaster(GraphicsLvo.Text, state =>
            {
                state.A[0] = chunky;
                state.A[1] = destination.RastPort;
                state.D[0] = 1;
            });
            InvokeRaster(GraphicsLvo.DrawEllipse, state =>
            {
                state.A[1] = destination.RastPort;
                SetD0D3(state, 4, 4, 2, 2);
            });
            InvokeRaster(GraphicsLvo.AreaEllipse, state =>
            {
                state.A[1] = destination.RastPort;
                SetD0D3(state, 8, 8, 2, 2);
            });
            InvokeRaster(GraphicsLvo.SetRast, state =>
            {
                state.A[1] = destination.RastPort;
                state.D[0] = 0;
            });

            var move = new M68kCpuState
            {
                A = { [1] = destination.RastPort }
            };
            Assert.True(InvokeHostTrap(
                bus,
                Lvo(AmigaKickstartHost.GraphicsLibraryBase, (int)GraphicsLvo.Move),
                move));
            InvokeRaster(GraphicsLvo.Draw, state =>
            {
                state.A[1] = destination.RastPort;
                state.D[0] = 4;
            });
            InvokeRaster(GraphicsLvo.AreaMove, state =>
            {
                state.A[1] = destination.RastPort;
                state.D[0] = 1;
                state.D[1] = 1;
            });
            InvokeRaster(GraphicsLvo.AreaDraw, state =>
            {
                state.A[1] = destination.RastPort;
                state.D[0] = 6;
                state.D[1] = 1;
            });
            InvokeRaster(GraphicsLvo.AreaDraw, state =>
            {
                state.A[1] = destination.RastPort;
                state.D[0] = 6;
                state.D[1] = 6;
            }, expectNewBit: false);
            InvokeRaster(GraphicsLvo.AreaDraw, state =>
            {
                state.A[1] = destination.RastPort;
                state.D[0] = 1;
                state.D[1] = 6;
            }, expectNewBit: false);
            InvokeRaster(GraphicsLvo.AreaEnd,
                state => state.A[1] = destination.RastPort);
            InvokeRaster(GraphicsLvo.RectFill, state =>
            {
                state.A[1] = destination.RastPort;
                SetD0D3(state, 0, 0, 3, 3);
            });
            InvokeRaster(GraphicsLvo.BltPattern, state =>
            {
                state.A[1] = destination.RastPort;
                SetD0D3(state, 0, 0, 3, 1);
            });
            InvokeRaster(GraphicsLvo.ReadPixel, state =>
            {
                state.A[1] = destination.RastPort;
            });
            InvokeRaster(GraphicsLvo.WritePixel, state =>
            {
                state.A[1] = destination.RastPort;
            });
            InvokeRaster(GraphicsLvo.Flood, state =>
            {
                state.A[1] = destination.RastPort;
                state.D[0] = 7;
                state.D[1] = 7;
                state.D[2] = 1;
            });
            InvokeRaster(GraphicsLvo.PolyDraw, state =>
            {
                state.A[0] = points;
                state.A[1] = destination.RastPort;
                state.D[0] = 2;
            });
            InvokeRaster(GraphicsLvo.ScrollRaster, state =>
            {
                state.A[1] = destination.RastPort;
                state.D[0] = 1;
                SetD2D5(state, 0, 0, 15, 15);
            });
            InvokeRaster(GraphicsLvo.ClipBlit, state =>
            {
                state.A[0] = source.RastPort;
                state.A[1] = destination.RastPort;
                SetD2D5(state, 0, 0, 4, 4);
                state.D[6] = 0xC0;
            }, expectedGuards: 2);
            InvokeRaster(GraphicsLvo.BltBitMapRastPort, state =>
            {
                state.A[0] = sourceBitMap;
                state.A[1] = destination.RastPort;
                SetD2D5(state, 4, 4, 4, 4);
                state.D[6] = 0xC0;
            });
            InvokeRaster(GraphicsLvo.BltMaskBitMapRastPort, state =>
            {
                state.A[0] = sourceBitMap;
                state.A[1] = destination.RastPort;
                state.A[2] = maskPlane;
                SetD2D5(state, 8, 8, 4, 4);
                state.D[6] = 0xC0;
            });
            InvokePixelSpan(GraphicsLvo.ReadPixelLine8, 0, 0, 4, 0);
            InvokePixelSpan(GraphicsLvo.WritePixelLine8, 0, 1, 4, 0);
            InvokePixelSpan(GraphicsLvo.ReadPixelArray8, 0, 0, 1, 1);
            InvokePixelSpan(GraphicsLvo.WritePixelArray8, 2, 2, 3, 3);
            InvokeRaster(GraphicsLvo.EraseRect, state =>
            {
                state.A[1] = destination.RastPort;
                SetD0D3(state, 0, 0, 1, 1);
            });
            InvokeRaster(GraphicsLvo.ScrollRasterBF, state =>
            {
                state.A[1] = destination.RastPort;
                state.D[0] = unchecked((uint)-1);
                SetD2D5(state, 0, 0, 15, 15);
            });
            InvokeRaster(GraphicsLvo.GetRPAttrsA, state =>
            {
                state.A[0] = tags;
                state.A[1] = destination.RastPort;
            });
            Assert.Equal((ushort)0, bus.ReadWord(tags + 16));
            Assert.Equal((ushort)15, bus.ReadWord(tags + 20));
            InvokeRaster(GraphicsLvo.WriteChunkyPixels, state =>
            {
                state.A[0] = destination.RastPort;
                state.A[2] = chunky;
                SetD0D3(state, 0, 0, 3, 1);
                state.D[4] = 8;
            });

            Assert.Equal(
                boot.CopperStartLayersCompleteRasterProviderExecutionMaskForTest,
                boot.CopperStartLayersRasterProviderExecutionMaskForTest);
            Assert.Equal(
                0,
                boot.CopperStartLayersCompatibilityRasterDispatchCountForTest);
        }
        finally
        {
            DeleteLayer(bus, libraryBase, source.Layer);
            DeleteLayer(bus, libraryBase, destination.Layer);
            DisposeLayerInfo(bus, libraryBase, sourceInfo);
            DisposeLayerInfo(bus, libraryBase, destinationInfo);
            FreeBitMap(bus, sourceBitMap);
            FreeBitMap(bus, destinationBitMap);
            FreeGuestMemory(bus, chunky, chunkyBytes);
            FreeGuestMemory(bus, maskPlane, maskBytes);
            FreeGuestMemory(bus, tags, tagBytes);
            FreeGuestMemory(bus, pixelArray, pixelArrayBytes);
            FreeGuestMemory(bus, points, pointBytes);
            FreeGuestMemory(bus, template, templateBytes);
            FreeGuestMemory(bus, tmpRaster, tmpRasterBytes);
            FreeGuestMemory(bus, tmpRas, TmpRas.Size);
            FreeGuestMemory(bus, areaStorage, areaStorageBytes);
            FreeGuestMemory(bus, areaInfo, AreaInfo.Size);
        }

        void ConfigureRastPort(uint rastPort)
        {
            var memory = new LayersTestGuestMemory(bus);
            var address = APTR.FromPointer(rastPort);
            LayersRastPortCodec.WriteForegroundPen(ref memory, address, 0x5A);
            LayersRastPortCodec.WriteBackgroundPen(ref memory, address, 0x11);
            LayersRastPortCodec.WriteOutlinePen(ref memory, address, 0xE0);
            LayersRastPortCodec.WriteDrawMode(ref memory, address, 0);
            LayersRastPortCodec.WriteMask(ref memory, address, 0xFF);
            LayersRastPortCodec.WriteLinePattern(ref memory, address, 0xFFFF);
        }

        void InitializeAreaAndTemporaryRaster()
        {
            var initializeArea = new M68kCpuState
            {
                A = { [0] = areaInfo, [1] = areaStorage },
                D = { [0] = 8 }
            };
            Assert.True(InvokeHostTrap(
                bus,
                Lvo(AmigaKickstartHost.GraphicsLibraryBase, (int)GraphicsLvo.InitArea),
                initializeArea));
            var initializeTmpRas = new M68kCpuState
            {
                A = { [0] = tmpRas, [1] = tmpRaster },
                D = { [0] = tmpRasterBytes }
            };
            Assert.True(InvokeHostTrap(
                bus,
                Lvo(AmigaKickstartHost.GraphicsLibraryBase, (int)GraphicsLvo.InitTmpRas),
                initializeTmpRas));
            var rastPortMemory = new LayersTestGuestMemory(bus);
            var destinationRastPort = APTR.FromPointer(destination.RastPort);
            LayersRastPortCodec.WriteAreaInfo(
                ref rastPortMemory,
                destinationRastPort,
                APTR.FromPointer(areaInfo));
            LayersRastPortCodec.WriteTemporaryRaster(
                ref rastPortMemory,
                destinationRastPort,
                APTR.FromPointer(tmpRas));
        }

        void InitializeFont()
        {
            var openFont = new M68kCpuState();
            Assert.True(InvokeHostTrap(
                bus,
                Lvo(AmigaKickstartHost.GraphicsLibraryBase, (int)GraphicsLvo.OpenFont),
                openFont));
            Assert.NotEqual(0u, openFont.D[0]);
            var setFont = new M68kCpuState
            {
                A = { [0] = openFont.D[0], [1] = destination.RastPort }
            };
            Assert.True(InvokeHostTrap(
                bus,
                Lvo(AmigaKickstartHost.GraphicsLibraryBase, (int)GraphicsLvo.SetFont),
                setFont));
            Assert.Equal(
                openFont.D[0],
                bus.ReadLong(destination.RastPort + (uint)GraphicsLayouts.RastPortFont));
        }

        void InitializeSources()
        {
            bus.WriteByte(template, 0xF0, 0);
            bus.WriteByte(template + 2, 0xF0, 0);
            bus.WriteWord(points, 0, 0);
            bus.WriteWord(points + 2, 0, 0);
            bus.WriteWord(points + 4, 3, 0);
            bus.WriteWord(points + 6, 3, 0);
            for (var offset = 0u; offset < pixelArrayBytes; offset++)
                bus.WriteByte(pixelArray + offset, checked((byte)(offset + 1)), 0);
            for (var offset = 0u; offset < maskBytes; offset++)
                bus.WriteByte(maskPlane + offset, 0xFF, 0);
            for (var offset = 0u; offset < chunkyBytes; offset++)
                bus.WriteByte(chunky + offset, checked((byte)(0x80 + offset)), 0);
            bus.WriteLong(
                tags,
                GraphicsRastPortAttributeOperations.RptagDrawBounds,
                0);
            bus.WriteLong(tags + 4, tags + 16, 0);
            bus.WriteLong(tags + 8, GraphicsRastPortAttributeOperations.TagDone, 0);
        }

        void InvokePixelSpan(
            GraphicsLvo lvo,
            ushort x0,
            ushort y0,
            ushort x1OrWidth,
            ushort y1)
        {
            InvokeRaster(lvo, state =>
            {
                state.A[0] = destination.RastPort;
                state.A[2] = pixelArray;
                state.D[0] = x0;
                state.D[1] = y0;
                state.D[2] = x1OrWidth;
                state.D[3] = y1;
            });
        }

        void InvokeRaster(
            GraphicsLvo lvo,
            Action<M68kCpuState> initialize,
            int expectedGuards = 1,
            bool expectNewBit = true)
        {
            var before = boot.CopperStartLayersRasterProviderExecutionMaskForTest;
            var state = new M68kCpuState();
            initialize(state);
            Assert.True(InvokeHostTrap(
                bus,
                Lvo(AmigaKickstartHost.GraphicsLibraryBase, (int)lvo),
                state));
            var after = boot.CopperStartLayersRasterProviderExecutionMaskForTest;
            if (expectNewBit)
                Assert.NotEqual(before, after);
            else
                Assert.Equal(before, after);
            Assert.Equal(expectedGuards,
                boot.CopperStartLayersLastRasterProviderGuardCountForTest);
            Assert.Equal((ushort)1,
                boot.CopperStartLayersLastRasterProviderPrimaryGuardDepthForTest);
            Assert.Equal(
                expectedGuards == 2 ? (ushort)1 : (ushort)0,
                boot.CopperStartLayersLastRasterProviderSecondaryGuardDepthForTest);
            var guardMemory = new LayersTestGuestMemory(bus);
            Assert.True(LayersSignalSemaphoreCodec.ReadOwner(
                ref guardMemory,
                LayersLayerCodec.LockAddress(
                    APTR.FromPointer(destination.Layer))).IsNull);
            Assert.True(LayersSignalSemaphoreCodec.ReadOwner(
                ref guardMemory,
                LayersLayerCodec.LockAddress(
                    APTR.FromPointer(source.Layer))).IsNull);
            Assert.Equal(
                0,
                boot.CopperStartLayersCompatibilityRasterDispatchCountForTest);
        }

        static void SetD0D3(M68kCpuState state, int d0, int d1, int d2, int d3)
        {
            state.D[0] = unchecked((uint)d0);
            state.D[1] = unchecked((uint)d1);
            state.D[2] = unchecked((uint)d2);
            state.D[3] = unchecked((uint)d3);
        }

        static void SetD2D5(M68kCpuState state, int d2, int d3, int d4, int d5)
        {
            state.D[2] = unchecked((uint)d2);
            state.D[3] = unchecked((uint)d3);
            state.D[4] = unchecked((uint)d4);
            state.D[5] = unchecked((uint)d5);
        }
    }
}
