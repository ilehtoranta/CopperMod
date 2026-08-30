using Amiga;
using Copper68k;
using CopperMod.Amiga;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.Core;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using CopperMod.Amiga.CopperStart.Layers;
using CopperMod.Amiga.Firmware;
using CopperMod.Amiga.Runtime;
using CopperMod.Amiga.Video.Rtg.CyberGraphics;
using PortableLayers = CopperStart.Layers;
using AmigaBus = CopperMod.Amiga.Bus.Bus;

namespace CopperMod.Amiga.Layers.Benchmarks;

internal readonly record struct ResourceFingerprint(
    uint PublicFree,
    ulong StateHash,
    int ClipRects,
    int BackingBitMaps,
    string Provider);

internal sealed class BenchmarkContext : IDisposable
{
    private readonly PlanarFixture _planar;
    private readonly RtgFixture _rtg;
    private readonly MorphFixture _morph;
    private readonly ResourceFingerprint[] _baseline;
    private bool _disposed;

    internal BenchmarkContext()
    {
        _planar = new PlanarFixture(this);
        _rtg = new RtgFixture(this);
        _morph = new MorphFixture(this);
        _baseline = new ResourceFingerprint[WorkloadCatalog.Kinds.Length];
        // Prime one complete operation before freezing fingerprints so rows
        // such as InitLayers measure their steady initialized representation.
        foreach (var kind in WorkloadCatalog.Kinds)
            Run(kind);
        Require(
            (_planar.RasterProviderExecutionMask |
                _rtg.RasterProviderExecutionMask) ==
            _planar.CompleteRasterProviderExecutionMask,
            "The benchmark corpus did not execute all 29 admitted layered raster vectors.");
        Require(
            _planar.CompatibilityRasterDispatchCount == 0 &&
                _rtg.CompatibilityRasterDispatchCount == 0,
            "The benchmark corpus reached the fail-closed compatibility raster path.");
        foreach (var kind in WorkloadCatalog.Kinds)
            _baseline[(int)kind] = Compute(kind);
    }

    internal long AssertionCount { get; private set; }

    internal void PrintRasterAllocationProbe()
    {
        Console.WriteLine(
            "allocation_coverage\t" +
            $"provider_mask={(_planar.RasterProviderExecutionMask | _rtg.RasterProviderExecutionMask):X16}\t" +
            $"complete_mask={_planar.CompleteRasterProviderExecutionMask:X16}\t" +
            $"compatibility_dispatches={_planar.CompatibilityRasterDispatchCount + _rtg.CompatibilityRasterDispatchCount}");
        _planar.PrintRasterAllocationProbe();
        _rtg.PrintRasterAllocationProbe();
    }

    internal void Run(WorkloadKind kind)
    {
        switch (kind)
        {
            case WorkloadKind.IdleInit:
                _planar.RunIdleInit();
                break;
            case WorkloadKind.HitTest:
                _planar.RunHitTest();
                break;
            case WorkloadKind.QueryLock:
                _planar.RunQueryLock();
                break;
            case WorkloadKind.CreateDelete:
                _planar.RunCreateDelete();
                break;
            case WorkloadKind.OverlapRebuildSmart:
                _planar.RunOverlapRebuildSmart();
                break;
            case WorkloadKind.MoveSize:
                _planar.RunMoveSize();
                break;
            case WorkloadKind.SuperScrollSyncCopy:
                _planar.RunSuperScrollSyncCopy();
                break;
            case WorkloadKind.RefreshBeginEnd:
                _planar.RunRefreshBeginEnd();
                break;
            case WorkloadKind.GuestHook:
                _planar.RunGuestHook();
                break;
            case WorkloadKind.RasterPlanar:
                _planar.RunRasterPlanar();
                break;
            case WorkloadKind.RasterRtg:
                _rtg.RunRaster();
                break;
            case WorkloadKind.MorphRenderDecline:
                _morph.RunRenderDecline();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    internal ResourceFingerprint AssertStable(WorkloadKind kind)
    {
        var actual = Compute(kind);
        Require(actual == _baseline[(int)kind],
            $"{WorkloadCatalog.Name(kind)} changed its correctness/resource fingerprint.");
        return actual;
    }

    internal void Require(bool condition, string message)
    {
        AssertionCount++;
        if (!condition)
            throw new InvalidOperationException(message);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _morph.Dispose();
        _rtg.Dispose();
        _planar.Dispose();
    }

    private ResourceFingerprint Compute(WorkloadKind kind) => kind switch
    {
        WorkloadKind.IdleInit => _planar.FingerprintIdle(),
        WorkloadKind.HitTest or WorkloadKind.QueryLock => _planar.FingerprintHit(),
        WorkloadKind.CreateDelete => _planar.FingerprintCreate(),
        WorkloadKind.OverlapRebuildSmart => _planar.FingerprintSmart(),
        WorkloadKind.MoveSize => _planar.FingerprintMoveSize(),
        WorkloadKind.SuperScrollSyncCopy => _planar.FingerprintSuper(),
        WorkloadKind.RefreshBeginEnd => _planar.FingerprintRefresh(),
        WorkloadKind.GuestHook => _planar.FingerprintHook(),
        WorkloadKind.RasterPlanar => _planar.FingerprintRaster(),
        WorkloadKind.RasterRtg => _rtg.Fingerprint(),
        WorkloadKind.MorphRenderDecline => _morph.Fingerprint(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private abstract class HostFixture : IDisposable
    {
        private const short AllocBitMapLvo = -918;
        private readonly BenchmarkContext _owner;
        private bool _disposed;

        protected HostFixture(BenchmarkContext owner, MachineOptions options)
        {
            _owner = owner;
            Machine = new Machine(options);
            Boot = new AmigaBootController(Machine);
            Boot.StartBootFromDisk(CreateBootableDisk());
            Bus = Machine.Bus;
            LayersBase = Boot.CopperStartLayersLibraryBase;
            Require(Boot.HasCopperStartLayers && LayersBase != 0,
                "CopperStart Layers did not install for a benchmark fixture.");
        }

        protected Machine Machine { get; }
        protected AmigaBootController Boot { get; }
        protected AmigaBus Bus { get; }
        protected uint LayersBase { get; set; }
        protected uint PublicFree => Boot.CopperStartAvailablePublicMemory;
        internal ulong RasterProviderExecutionMask
            => Boot.CopperStartLayersRasterProviderExecutionMaskForTest;
        internal ulong CompleteRasterProviderExecutionMask
            => Boot.CopperStartLayersCompleteRasterProviderExecutionMaskForTest;
        internal int CompatibilityRasterDispatchCount
            => Boot.CopperStartLayersCompatibilityRasterDispatchCountForTest;

        protected void Require(bool condition, string message)
            => _owner.Require(condition, message);

        protected static void Reset(M68kCpuState state)
        {
            Array.Clear(state.D);
            Array.Clear(state.A);
            state.ProgramCounter = 0;
            state.Halted = false;
            state.Stopped = false;
        }

        protected void InvokeLayers(short lvo, M68kCpuState state)
            => Invoke(LayersBase, lvo, state);

        protected void InvokeGraphics(short lvo, M68kCpuState state)
            => Invoke(AmigaKickstartHost.GraphicsLibraryBase, lvo, state);

        protected void Invoke(uint libraryBase, short lvo, M68kCpuState state)
        {
            var address = Lvo(libraryBase, lvo);
            Require(Bus.ReadWord(address) == 0xFF00,
                "Measured library vector has no host gateway.");
            state.ProgramCounter = address + 6;
            Require(Bus.TryInvokeHostGateway(address, Bus.ReadLong(address + 2), state),
                "Measured host gateway declined dispatch.");
        }

        protected uint Allocate(uint bytes)
        {
            var state = new M68kCpuState();
            state.D[0] = bytes;
            state.D[1] = (uint)(Exec.MemoryFlags.Public | Exec.MemoryFlags.Clear);
            Invoke(AmigaKickstartHost.ExecLibraryBase, ExecLvo.AllocMem, state);
            Require(state.D[0] != 0, "Guest public allocation failed during setup.");
            return state.D[0];
        }

        protected uint NewLayerInfo()
        {
            var state = new M68kCpuState();
            InvokeLayers(LayersLvo.NewLayerInfo, state);
            Require(state.D[0] != 0, "NewLayerInfo failed during benchmark setup.");
            return state.D[0];
        }

        protected BitMapDescriptor AllocateBitMap(
            ushort width,
            ushort height,
            byte depth,
            bool requireRtg)
        {
            var state = new M68kCpuState();
            state.D[0] = width;
            state.D[1] = height;
            state.D[2] = depth;
            state.D[3] = (uint)BitMapFlags.Clear;
            InvokeGraphics(AllocBitMapLvo, state);
            Require(state.D[0] != 0, "AllocBitMap failed during benchmark setup.");
            if (requireRtg)
            {
                Require(Boot.CyberGraphics.TryGetBitMapSurface(state.D[0], out var surface),
                    "RTG benchmark bitmap was not provider-owned.");
                return new BitMapDescriptor(
                    state.D[0], surface.GuestBaseAddress,
                    checked(surface.BytesPerRow * surface.Height), true);
            }

            Require(!Boot.HasCyberGraphics ||
                    !Boot.CyberGraphics.TryGetBitMapSurface(state.D[0], out _),
                "Planar benchmark bitmap unexpectedly came from the RTG provider.");
            var bytesPerRow = Bus.ReadWord(
                state.D[0] + (uint)GraphicsLayout.BitMap.BytesPerRow);
            var rows = Bus.ReadWord(
                state.D[0] + (uint)GraphicsLayout.BitMap.Rows);
            var plane = Bus.ReadLong(
                state.D[0] + (uint)GraphicsLayout.BitMap.Plane0);
            Require(plane != 0, "Planar benchmark bitmap has no plane 0.");
            return new BitMapDescriptor(
                state.D[0], plane, checked(bytesPerRow * rows), false);
        }

        protected LayerSurface CreateLayer(
            uint layerInfo,
            BitMapDescriptor display,
            LayerCreationFlags flags,
            short minX,
            short minY,
            short maxX,
            short maxY,
            BitMapDescriptor super = default,
            bool upfront = true)
        {
            var state = new M68kCpuState();
            state.A[0] = layerInfo;
            state.A[1] = display.Address;
            state.A[2] = super.Address;
            state.D[0] = unchecked((uint)minX);
            state.D[1] = unchecked((uint)minY);
            state.D[2] = unchecked((uint)maxX);
            state.D[3] = unchecked((uint)maxY);
            state.D[4] = (uint)flags;
            InvokeLayers(upfront
                ? LayersLvo.CreateUpfrontLayer
                : LayersLvo.CreateBehindLayer, state);
            Require(state.D[0] != 0, "Layer creation failed during benchmark setup.");
            var layer = state.D[0];
            var rastPort = Bus.ReadLong(layer + (uint)LayersLayout.Layer.RastPort);
            Require(rastPort != 0, "Created benchmark layer has no RastPort.");
            return new LayerSurface(layer, rastPort, minX, minY, maxX, maxY);
        }

        protected ResourceFingerprint Fingerprint(
            uint layerInfo,
            BitMapDescriptor primary,
            BitMapDescriptor secondary,
            string provider)
        {
            var hash = 14695981039346656037UL;
            var clipRects = 0;
            var backing = 0;
            var layer = Bus.ReadLong(layerInfo + (uint)LayersLayout.LayerInfo.TopLayer);
            for (var layerCount = 0; layer != 0 && layerCount < 64; layerCount++)
            {
                HashMemory(ref hash,
                    layer + (uint)LayersLayout.Layer.Bounds,
                    checked((int)Rectangle.Size));
                HashMemory(ref hash,
                    layer + (uint)LayersLayout.Layer.Flags,
                    sizeof(ushort));
                HashMemory(ref hash,
                    layer + (uint)LayersLayout.Layer.ScrollX,
                    sizeof(short) * 2);
                HashMemory(ref hash,
                    layer + (uint)LayersLayout.Layer.Width,
                    sizeof(short) * 2);
                if (Bus.ReadLong(layer + (uint)LayersLayout.Layer.SuperBitMap) != 0)
                {
                    backing++;
                    HashByte(ref hash, 1);
                }
                else
                {
                    HashByte(ref hash, 0);
                }
                CountAndHashClipRects(
                    ref hash,
                    Bus.ReadLong(layer + (uint)LayersLayout.Layer.ClipRect),
                    ref clipRects,
                    ref backing);
                CountAndHashClipRects(
                    ref hash,
                    Bus.ReadLong(layer + (uint)LayersLayout.Layer.SuperClipRect),
                    ref clipRects,
                    ref backing);
                layer = Bus.ReadLong(layer + (uint)LayersLayout.Layer.Back);
            }
            HashBitMap(ref hash, primary);
            HashBitMap(ref hash, secondary);
            return new ResourceFingerprint(PublicFree, hash, clipRects, backing, provider);
        }

        protected ResourceFingerprint FingerprintRaw(
            uint address,
            int bytes,
            string provider)
        {
            var hash = 14695981039346656037UL;
            HashMemory(ref hash, address, bytes);
            return new ResourceFingerprint(PublicFree, hash, 0, 0, provider);
        }

        protected void ReadBounds(uint layer, out short minX, out short minY,
            out short maxX, out short maxY)
        {
            var address = layer + (uint)LayersLayout.Layer.Bounds;
            minX = unchecked((short)Bus.ReadWord(address));
            minY = unchecked((short)Bus.ReadWord(address + 2));
            maxX = unchecked((short)Bus.ReadWord(address + 4));
            maxY = unchecked((short)Bus.ReadWord(address + 6));
        }

        protected void CompleteRefresh(uint layer, M68kCpuState begin, M68kCpuState end)
        {
            Reset(begin);
            begin.A[0] = layer;
            InvokeLayers(LayersLvo.BeginUpdate, begin);
            Require(begin.D[0] != 0, "BeginUpdate did not admit a damaged layer.");
            Require(((LayerFlags)Bus.ReadWord(
                    layer + (uint)LayersLayout.Layer.Flags) & LayerFlags.Updating) != 0,
                "BeginUpdate did not publish Updating.");
            Reset(end);
            end.A[0] = layer;
            end.D[0] = 1;
            InvokeLayers(LayersLvo.EndUpdate, end);
            Require(((LayerFlags)Bus.ReadWord(
                    layer + (uint)LayersLayout.Layer.Flags) & LayerFlags.Updating) == 0,
                "EndUpdate did not clear Updating.");
        }

        protected void CompleteRefreshIfNeeded(
            uint layer,
            M68kCpuState begin,
            M68kCpuState end)
        {
            if (((LayerFlags)Bus.ReadWord(layer +
                    (uint)LayersLayout.Layer.Flags) & LayerFlags.Refresh) != 0)
            {
                CompleteRefresh(layer, begin, end);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Machine.Dispose();
        }

        private void CountAndHashClipRects(
            ref ulong hash,
            uint clipRect,
            ref int count,
            ref int backing)
        {
            for (var index = 0; clipRect != 0 && index < 256; index++)
            {
                count++;
                var hasBacking = Bus.ReadLong(
                    clipRect + (uint)LayersLayout.ClipRect.BitMap) != 0;
                if (hasBacking)
                    backing++;
                HashByte(ref hash, hasBacking ? (byte)1 : (byte)0);
                HashByte(ref hash, Bus.ReadLong(clipRect +
                    (uint)LayersLayout.ClipRect.ObscuringLayer) != 0
                    ? (byte)1
                    : (byte)0);
                HashMemory(ref hash,
                    clipRect + (uint)LayersLayout.ClipRect.Bounds,
                    checked((int)Rectangle.Size));
                clipRect = Bus.ReadLong(clipRect + (uint)LayersLayout.ClipRect.Next);
            }
        }

        private void HashBitMap(ref ulong hash, BitMapDescriptor bitMap)
        {
            if (bitMap.Address == 0)
                return;
            HashMemory(ref hash, bitMap.Address, checked((int)BitMap.Size));
            HashMemory(ref hash, bitMap.PixelAddress, bitMap.PixelBytes);
        }

        private void HashMemory(ref ulong hash, uint address, int bytes)
        {
            for (var offset = 0; offset < bytes; offset++)
                HashByte(ref hash, Bus.ReadByte(address + checked((uint)offset)));
        }

        private static void HashByte(ref ulong hash, byte value)
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }

        private static uint Lvo(uint libraryBase, int displacement)
            => unchecked((uint)((int)libraryBase + displacement));

        private static AmigaDiskImage CreateBootableDisk()
        {
            var data = new byte[AmigaDiskImage.StandardAdfSize];
            data[0] = (byte)'D';
            data[1] = (byte)'O';
            data[2] = (byte)'S';
            BigEndian.WriteUInt32(data, 4,
                CalculateBootChecksum(data.AsSpan(0, 1024)));
            return AmigaDiskImage.FromAdfBytes(data);
        }

        private static uint CalculateBootChecksum(ReadOnlySpan<byte> bootBlock)
        {
            var sum = 0u;
            for (var offset = 0; offset < 1024; offset += 4)
            {
                var value = BigEndian.ReadUInt32(
                    bootBlock, offset, "boot checksum word");
                var previous = sum;
                sum += value;
                if (sum < previous)
                    sum++;
            }
            return ~sum;
        }
    }

    private sealed class PlanarFixture : HostFixture
    {
        private readonly M68kCpuState[] _states = CreateStates(24);
        private readonly uint _idleLayerInfo;
        private readonly uint _hitLayerInfo;
        private readonly BitMapDescriptor _hitBitMap;
        private readonly LayerSurface _hitBottom;
        private readonly LayerSurface _hitTop;
        private readonly uint _createLayerInfo;
        private readonly BitMapDescriptor _createBitMap;
        private readonly uint _smartLayerInfo;
        private readonly BitMapDescriptor _smartBitMap;
        private readonly LayerSurface _smartBottom;
        private readonly LayerSurface _smartBlocker;
        private readonly uint _moveLayerInfo;
        private readonly BitMapDescriptor _moveBitMap;
        private readonly LayerSurface _moveLayer;
        private readonly uint _superLayerInfo;
        private readonly BitMapDescriptor _superDisplay;
        private readonly BitMapDescriptor _superBacking;
        private readonly LayerSurface _superLayer;
        private readonly uint _refreshLayerInfo;
        private readonly BitMapDescriptor _refreshBitMap;
        private readonly LayerSurface _refreshBottom;
        private readonly LayerSurface _refreshBlocker;
        private readonly uint _hookLayerInfo;
        private readonly BitMapDescriptor _hookBitMap;
        private readonly LayerSurface _hookBlocker;
        private readonly uint _hook;
        private readonly uint _hookEntry;
        private readonly uint _hookStack;
        private int _hookCallbackCount;
        private readonly uint _rasterLayerInfo;
        private readonly BitMapDescriptor _rasterBitMap;
        private readonly LayerSurface _rasterLayer;
        private readonly LayerSurface _rasterRestoreLayer;
        private readonly uint _rasterText;
        private readonly uint _rasterPolyPoints;
        private readonly uint _rasterTemplate;
        private readonly uint _rasterAreaInfo;
        private readonly uint _rasterAreaStorage;
        private readonly uint _rasterTmpRas;
        private readonly uint _rasterTmpRaster;
        private readonly uint _rasterAttributeTags;
        private readonly uint _rasterPixelArray;
        private readonly uint _rasterChunkyPixels;

        internal PlanarFixture(BenchmarkContext owner)
            : base(owner, MachineOptions
                .ForProfile(MachineProfile.A500Pal512KBoot)
                .WithLiveAgnusDma(false))
        {
            _idleLayerInfo = Allocate(LayersLayout.LayerInfo.Size);

            _hitLayerInfo = NewLayerInfo();
            _hitBitMap = AllocateBitMap(32, 16, 1, false);
            _hitBottom = CreateLayer(_hitLayerInfo, _hitBitMap,
                LayerCreationFlags.Simple, 0, 0, 31, 15);
            _hitTop = CreateLayer(_hitLayerInfo, _hitBitMap,
                LayerCreationFlags.Simple, 8, 4, 23, 11);

            _createLayerInfo = NewLayerInfo();
            _createBitMap = AllocateBitMap(16, 8, 1, false);

            _smartLayerInfo = NewLayerInfo();
            _smartBitMap = AllocateBitMap(32, 16, 1, false);
            _smartBottom = CreateLayer(_smartLayerInfo, _smartBitMap,
                LayerCreationFlags.Smart, 0, 0, 31, 15);
            _smartBlocker = CreateLayer(_smartLayerInfo, _smartBitMap,
                LayerCreationFlags.Simple, 8, 4, 23, 11);
            CompleteRefreshIfNeeded(
                _smartBottom.Layer, _states[18], _states[19]);

            _moveLayerInfo = NewLayerInfo();
            _moveBitMap = AllocateBitMap(24, 16, 1, false);
            _moveLayer = CreateLayer(_moveLayerInfo, _moveBitMap,
                LayerCreationFlags.Simple, 2, 2, 17, 11);

            _superLayerInfo = NewLayerInfo();
            _superDisplay = AllocateBitMap(16, 8, 1, false);
            _superBacking = AllocateBitMap(32, 16, 1, false);
            _superLayer = CreateLayer(_superLayerInfo, _superDisplay,
                LayerCreationFlags.Super, 0, 0, 15, 7, _superBacking);
            Reset(_states[20]);
            _states[20].A[1] = _superLayer.Layer;
            InvokeLayers(LayersLvo.LockLayer, _states[20]);

            _refreshLayerInfo = NewLayerInfo();
            _refreshBitMap = AllocateBitMap(32, 16, 1, false);
            _refreshBottom = CreateLayer(_refreshLayerInfo, _refreshBitMap,
                LayerCreationFlags.Simple, 0, 0, 31, 15);
            _refreshBlocker = CreateLayer(_refreshLayerInfo, _refreshBitMap,
                LayerCreationFlags.Simple, 8, 4, 23, 11);
            CompleteRefreshIfNeeded(
                _refreshBottom.Layer, _states[18], _states[19]);

            _hookLayerInfo = NewLayerInfo();
            _hookBitMap = AllocateBitMap(32, 16, 1, false);
            _hookBlocker = CreateLayer(_hookLayerInfo, _hookBitMap,
                LayerCreationFlags.Simple, 6, 4, 14, 10);
            _hook = Allocate(Hook.Size);
            _hookEntry = Allocate(6);
            _hookStack = Allocate(64) + 60;
            _ = Bus.RegisterHostGateway(_hookEntry, CaptureHook);
            Bus.WriteLong(_hook + (uint)UtilityLayout.Hook.Entry, _hookEntry);

            _rasterLayerInfo = NewLayerInfo();
            _rasterBitMap = AllocateBitMap(32, 8, 1, false);
            _rasterLayer = CreateLayer(_rasterLayerInfo, _rasterBitMap,
                LayerCreationFlags.Simple, 0, 0, 7, 7);
            _rasterRestoreLayer = CreateLayer(_rasterLayerInfo, _rasterBitMap,
                LayerCreationFlags.Simple, 16, 0, 23, 7);
            ConfigureComplement(_rasterLayer.RastPort);
            // Keep a distinct source byte and a zero restore byte in the same
            // planar bitmap. BltBitMapRastPort copies x=8 into the destination;
            // dual-endpoint ClipBlit copies the restore layer at x=16 back.
            Bus.WriteByte(_rasterBitMap.PixelAddress + 1, 0xA5, 0);
            _rasterText = Allocate(1);
            Bus.WriteByte(_rasterText, (byte)'A', 0);
            _rasterPolyPoints = Allocate(8);
            Bus.WriteWord(_rasterPolyPoints, 7);
            Bus.WriteWord(_rasterPolyPoints + 2, 0);
            Bus.WriteWord(_rasterPolyPoints + 4, 7);
            Bus.WriteWord(_rasterPolyPoints + 6, 7);
            _rasterTemplate = Allocate(4);
            Bus.WriteByte(_rasterTemplate, 0xF0, 0);
            Bus.WriteByte(_rasterTemplate + 2, 0xF0, 0);
            _rasterAreaInfo = Allocate(AreaInfo.Size);
            _rasterAreaStorage = Allocate(64);
            _rasterTmpRas = Allocate(TmpRas.Size);
            _rasterTmpRaster = Allocate(64);
            _rasterAttributeTags = Allocate(24);
            _rasterPixelArray = Allocate(32);
            _rasterChunkyPixels = Allocate(16);
            for (var offset = 0u; offset < 16; offset++)
            {
                Bus.WriteByte(_rasterChunkyPixels + offset, 1, 0);
            }
            Bus.WriteLong(
                _rasterAttributeTags,
                GraphicsRastPortAttributeOperations.RptagDrawBounds,
                0);
            Bus.WriteLong(_rasterAttributeTags + 4, _rasterAttributeTags + 16, 0);
            Bus.WriteLong(
                _rasterAttributeTags + 8,
                GraphicsRastPortAttributeOperations.TagDone,
                0);
            var areaState = _states[22];
            Reset(areaState);
            areaState.A[0] = _rasterAreaInfo;
            areaState.A[1] = _rasterAreaStorage;
            areaState.D[0] = 8;
            InvokeGraphics(-282, areaState);
            Reset(areaState);
            areaState.A[0] = _rasterTmpRas;
            areaState.A[1] = _rasterTmpRaster;
            areaState.D[0] = 64;
            InvokeGraphics(-468, areaState);
            Bus.WriteLong(
                _rasterLayer.RastPort + (uint)GraphicsLayout.RastPort.AreaInfo,
                _rasterAreaInfo,
                0);
            Bus.WriteLong(
                _rasterLayer.RastPort +
                    (uint)GraphicsLayout.RastPort.TemporaryRaster,
                _rasterTmpRas,
                0);
            var fontState = _states[21];
            Reset(fontState);
            InvokeGraphics(-72, fontState);
            Require(fontState.D[0] != 0,
                "Planar raster benchmark could not open the synthetic font.");
            var font = fontState.D[0];
            Reset(fontState);
            fontState.A[0] = font;
            fontState.A[1] = _rasterLayer.RastPort;
            InvokeGraphics(-66, fontState);
        }

        internal void RunIdleInit()
        {
            var state = _states[0];
            Reset(state);
            state.A[0] = _idleLayerInfo;
            state.D[0] = 0x13579BDF;
            InvokeLayers(LayersLvo.InitLayers, state);
            Require(state.D[0] == 0x13579BDF,
                "InitLayers did not preserve the void-result D0 register.");
            Require(Bus.ReadLong(_idleLayerInfo +
                    (uint)LayersLayout.LayerInfo.TopLayer) == 0,
                "InitLayers scratch LayerInfo is not idle.");
        }

        internal void RunHitTest()
        {
            var state = _states[1];
            Reset(state);
            state.A[0] = _hitLayerInfo;
            state.D[0] = 10;
            state.D[1] = 6;
            InvokeLayers(LayersLvo.WhichLayer, state);
            Require(state.D[0] == _hitTop.Layer,
                "WhichLayer did not return the top hit.");
            Reset(state);
            state.A[0] = _hitLayerInfo;
            state.D[0] = 40;
            state.D[1] = 30;
            InvokeLayers(LayersLvo.WhichLayer, state);
            Require(state.D[0] == 0, "WhichLayer miss returned a layer.");
        }

        internal void RunQueryLock()
        {
            var state = _states[2];
            Reset(state);
            state.A[1] = _hitTop.Layer;
            InvokeLayers(LayersLvo.LockLayer, state);
            Reset(state);
            state.A[0] = _hitLayerInfo;
            state.D[0] = 10;
            state.D[1] = 6;
            InvokeLayers(LayersLvo.WhichLayer, state);
            Require(state.D[0] == _hitTop.Layer,
                "Locked WhichLayer returned the wrong layer.");
            Reset(state);
            state.A[0] = _hitTop.Layer;
            InvokeLayers(LayersLvo.UnlockLayer, state);
        }

        internal void RunCreateDelete()
        {
            var state = _states[3];
            Reset(state);
            state.A[0] = _createLayerInfo;
            state.A[1] = _createBitMap.Address;
            state.D[0] = 1;
            state.D[1] = 1;
            state.D[2] = 12;
            state.D[3] = 6;
            state.D[4] = (uint)LayerCreationFlags.Simple;
            InvokeLayers(LayersLvo.CreateUpfrontLayer, state);
            var layer = state.D[0];
            Require(layer != 0, "Measured CreateUpfrontLayer failed.");
            Reset(state);
            state.A[1] = layer;
            InvokeLayers(LayersLvo.DeleteLayer, state);
            Require(state.D[0] != 0, "Measured DeleteLayer failed.");
            Require(Bus.ReadLong(_createLayerInfo +
                    (uint)LayersLayout.LayerInfo.TopLayer) == 0,
                "Create/delete left a linked layer.");
        }

        internal void RunOverlapRebuildSmart()
        {
            Move(_smartBlocker.Layer, 1, 0, _states[4]);
            ReadBounds(_smartBlocker.Layer, out var x0, out _, out _, out _);
            Require(x0 == _smartBlocker.MinX + 1,
                "Smart overlap forward move did not publish bounds.");
            Move(_smartBlocker.Layer, -1, 0, _states[4]);
            ReadBounds(_smartBlocker.Layer, out x0, out _, out _, out _);
            Require(x0 == _smartBlocker.MinX,
                "Smart overlap reverse move did not restore bounds.");
            CompleteRefreshIfNeeded(
                _smartBottom.Layer, _states[5], _states[6]);
            Require(CountBacking(_smartBottom.Layer) > 0,
                "Smart overlap benchmark lost its backing bitmap.");
            ComplementLayeredRectangleTwice(
                _smartBottom.RastPort,
                0,
                0,
                31,
                15,
                _states[23]);
        }

        internal void RunMoveSize()
        {
            Move(_moveLayer.Layer, 1, 1, _states[7]);
            Size(_moveLayer.Layer, 1, 1, _states[8]);
            ReadBounds(_moveLayer.Layer, out var minX, out var minY,
                out var maxX, out var maxY);
            Require(minX == _moveLayer.MinX + 1 && minY == _moveLayer.MinY + 1 &&
                    maxX == _moveLayer.MaxX + 2 && maxY == _moveLayer.MaxY + 2,
                "Move/size forward geometry is incorrect.");
            Size(_moveLayer.Layer, -1, -1, _states[8]);
            Move(_moveLayer.Layer, -1, -1, _states[7]);
            ReadBounds(_moveLayer.Layer, out minX, out minY, out maxX, out maxY);
            Require(minX == _moveLayer.MinX && minY == _moveLayer.MinY &&
                    maxX == _moveLayer.MaxX && maxY == _moveLayer.MaxY,
                "Move/size reverse geometry did not restore the layer.");
            CompleteRefreshIfNeeded(
                _moveLayer.Layer, _states[5], _states[6]);
        }

        internal void RunSuperScrollSyncCopy()
        {
            ScrollSuper(1, 1);
            Require(unchecked((short)Bus.ReadWord(_superLayer.Layer +
                    (uint)LayersLayout.Layer.ScrollX)) == 1 &&
                    unchecked((short)Bus.ReadWord(_superLayer.Layer +
                    (uint)LayersLayout.Layer.ScrollY)) == 1,
                "Positive Super scroll did not publish +1,+1.");
            SyncCopySuper();
            ComplementLayeredRectangleTwice(
                _superLayer.RastPort,
                0,
                0,
                31,
                15,
                _states[23]);
            ScrollSuper(-1, -1);
            SyncCopySuper();
            Require(Bus.ReadWord(_superLayer.Layer +
                    (uint)LayersLayout.Layer.ScrollX) == 0 &&
                    Bus.ReadWord(_superLayer.Layer +
                    (uint)LayersLayout.Layer.ScrollY) == 0,
                "Signed Super scroll did not restore the origin.");
        }

        internal void RunRefreshBeginEnd()
        {
            Move(_refreshBlocker.Layer, 1, 0, _states[9]);
            CompleteRefresh(_refreshBottom.Layer, _states[10], _states[11]);
            Move(_refreshBlocker.Layer, -1, 0, _states[9]);
            CompleteRefresh(_refreshBottom.Layer, _states[10], _states[11]);
            ReadBounds(_refreshBlocker.Layer, out var minX, out _, out _, out _);
            Require(minX == _refreshBlocker.MinX,
                "Refresh workload did not restore blocker geometry.");
        }

        internal void RunGuestHook()
        {
            var callbacksBefore = _hookCallbackCount;
            var state = _states[12];
            Reset(state);
            state.A[0] = _hookLayerInfo;
            state.A[1] = _hookBitMap.Address;
            state.A[3] = _hook;
            state.A[7] = _hookStack;
            state.D[0] = 1;
            state.D[1] = 2;
            state.D[2] = 20;
            state.D[3] = 12;
            state.D[4] = (uint)LayerCreationFlags.Simple;
            InvokeLayers(LayersLvo.CreateBehindHookLayer, state);
            var resumes = 0;
            while (state.ProgramCounter == LayersHostServices.HookContinuationAddress &&
                   resumes++ < 16)
            {
                var continuation = LayersHostServices.HookContinuationAddress;
                state.ProgramCounter = continuation + 6;
                Require(Bus.TryInvokeHostGateway(
                        continuation, Bus.ReadLong(continuation + 2), state),
                    "Guest Hook continuation gateway declined.");
            }
            var layer = state.D[0];
            Require(layer != 0, "Guest Hook layer creation did not finalize.");
            Require(_hookCallbackCount - callbacksBefore == 4,
                "Guest Hook workload did not execute four visible callbacks.");
            Reset(state);
            state.A[1] = layer;
            InvokeLayers(LayersLvo.DeleteLayer, state);
            Require(state.D[0] != 0, "Guest Hook layer deletion failed.");
            Require(Bus.ReadLong(_hookLayerInfo +
                    (uint)LayersLayout.LayerInfo.TopLayer) == _hookBlocker.Layer,
                "Guest Hook workload changed the persistent z-order.");
        }

        internal void RunRasterPlanar()
        {
            var before = RasterPixelHash();
            var destinationBefore = Bus.ReadByte(_rasterBitMap.PixelAddress);
            BltTemplateTwice(_states[13]);
            Require(RasterPixelHash() == before,
                "Two planar BltTemplate operations did not restore pixels.");
            BltPatternTwice(_states[13]);
            Require(RasterPixelHash() == before,
                "Two planar BltPattern operations did not restore pixels.");
            FloodAndRestore(_states[13]);
            Require(RasterPixelHash() == before,
                "Planar Flood did not restore its measured surface.");
            AreaAndRestore(_states[13]);
            Require(RasterPixelHash() == before,
                "Planar AreaEnd did not restore its measured surface.");
            AreaEllipseAndRestore(_states[13]);
            Require(RasterPixelHash() == before,
                "Planar AreaEllipse did not restore its measured surface.");
            GetRasterAttributes(_states[13]);
            SetRast(1, _states[13]);
            Require(Bus.ReadByte(_rasterBitMap.PixelAddress) == 0xFF,
                "Planar layered SetRast did not fill selected pixels.");
            EraseRaster(_states[13]);
            Require(Bus.ReadByte(_rasterBitMap.PixelAddress) == 0,
                "Planar layered EraseRect did not clear selected pixels.");
            ReadRasterPixel(_states[13]);

            RectFill(_rasterLayer.RastPort, _states[13]);
            Require(Bus.ReadByte(_rasterBitMap.PixelAddress) != destinationBefore,
                "Planar layered RectFill did not mutate the selected pixels.");
            RectFill(_rasterLayer.RastPort, _states[13]);
            Require(Bus.ReadByte(_rasterBitMap.PixelAddress) == destinationBefore,
                "Second planar complement RectFill did not restore pixels.");

            DrawOnce(_states[13]);
            Require(Bus.ReadByte(_rasterBitMap.PixelAddress) != destinationBefore,
                "Planar layered Draw did not mutate selected pixels.");
            Bus.WriteByte(_rasterBitMap.PixelAddress, destinationBefore, 0);

            var beforeText = RasterPixelHash();
            TextOnce(_states[13]);
            Require(RasterPixelHash() != beforeText,
                "Planar layered Text did not mutate selected pixels.");
            var rasterBytesPerRow = Bus.ReadWord(_rasterBitMap.Address +
                (uint)GraphicsLayout.BitMap.BytesPerRow);
            for (var row = 0; row < 8; row++)
            {
                Bus.WriteByte(
                    _rasterBitMap.PixelAddress + checked((uint)(row * rasterBytesPerRow)),
                    0,
                    0);
            }

            DrawEllipse(_states[13]);
            Require(RasterPixelHash() != before,
                "Planar layered DrawEllipse did not mutate selected pixels.");
            ClearRasterDestination();

            PolyDraw(_states[13]);
            Require(RasterPixelHash() != before,
                "Planar layered PolyDraw did not mutate selected pixels.");
            ClearRasterDestination();

            SetRast(1, _states[13]);
            ClearTextRaster(GraphicsLayersLvo.ClearEOL, _states[13]);
            Require(Bus.ReadByte(_rasterBitMap.PixelAddress) == 0,
                "Planar layered ClearEOL did not clear the text row.");
            ClearRasterDestination();
            SetRast(1, _states[13]);
            ClearTextRaster(GraphicsLayersLvo.ClearScreen, _states[13]);
            Require(Bus.ReadByte(_rasterBitMap.PixelAddress) == 0,
                "Planar layered ClearScreen did not clear the selected bitmap.");
            ClearRasterDestination();

            ScrollRaster(GraphicsLayersLvo.ScrollRaster, _states[13]);
            Require(Bus.ReadByte(_rasterBitMap.PixelAddress) != 0x55,
                "Planar layered ScrollRaster did not move source pixels.");
            ClearRasterDestination();
            ScrollRaster(GraphicsLayersLvo.ScrollRasterBF, _states[13]);
            Require(Bus.ReadByte(_rasterBitMap.PixelAddress) != 0x55,
                "Planar layered ScrollRasterBF did not move source pixels.");
            ClearRasterDestination();

            PixelArrayOperationsAndRestore(_states[13]);
            Require(RasterPixelHash() == before,
                "Layered pixel-array operations did not restore planar pixels.");
            WriteChunkyPixelsAndRestore(_states[13]);
            Require(RasterPixelHash() == before,
                "Layered WriteChunkyPixels did not restore planar pixels.");

            BltBitMapToRastPort(_states[13]);
            Require(Bus.ReadByte(_rasterBitMap.PixelAddress) == 0xA5,
                "Planar layered BltBitMapRastPort did not copy source pixels.");
            ClipBlitRestore(_states[13]);
            Require(Bus.ReadByte(_rasterBitMap.PixelAddress) == destinationBefore,
                "Dual-endpoint layered ClipBlit did not restore pixels.");
            Require(RasterPixelHash() == before,
                "Representative planar layered raster operations changed persistent pixels.");
        }

        internal void PrintRasterAllocationProbe()
        {
            static void Print(string name, long before)
                => Console.WriteLine($"allocation_probe\tstage={name}\tbytes={GC.GetAllocatedBytesForCurrentThread() - before}");

            for (var index = 0; index < 8; index++)
                RunRasterPlanar();
            long before;
            before = GC.GetAllocatedBytesForCurrentThread();
            BltTemplateTwice(_states[13]);
            Print("planar-template", before);
            before = GC.GetAllocatedBytesForCurrentThread();
            BltPatternTwice(_states[13]);
            Print("planar-pattern", before);
            before = GC.GetAllocatedBytesForCurrentThread();
            FloodAndRestore(_states[13]);
            Print("planar-flood", before);
            before = GC.GetAllocatedBytesForCurrentThread();
            AreaAndRestore(_states[13]);
            Print("planar-area", before);
            before = GC.GetAllocatedBytesForCurrentThread();
            AreaEllipseAndRestore(_states[13]);
            Print("planar-area-ellipse", before);
            before = GC.GetAllocatedBytesForCurrentThread();
            GetRasterAttributes(_states[13]);
            Print("planar-getattrs", before);
            before = GC.GetAllocatedBytesForCurrentThread();
            SetRast(1, _states[13]);
            EraseRaster(_states[13]);
            Print("planar-set-erase", before);
            before = GC.GetAllocatedBytesForCurrentThread();
            ReadRasterPixel(_states[13]);
            Print("planar-read-pixel", before);
            before = GC.GetAllocatedBytesForCurrentThread();
            RectFill(_rasterLayer.RastPort, _states[13]);
            RectFill(_rasterLayer.RastPort, _states[13]);
            Print("planar-rectfill", before);
            before = GC.GetAllocatedBytesForCurrentThread();
            DrawOnce(_states[13]);
            Print("planar-draw", before);
            ClearRasterDestination();
            before = GC.GetAllocatedBytesForCurrentThread();
            TextOnce(_states[13]);
            Print("planar-text", before);
            ClearRasterDestination();
            before = GC.GetAllocatedBytesForCurrentThread();
            DrawEllipse(_states[13]);
            Print("planar-ellipse", before);
            ClearRasterDestination();
            before = GC.GetAllocatedBytesForCurrentThread();
            PolyDraw(_states[13]);
            Print("planar-poly", before);
            ClearRasterDestination();
            before = GC.GetAllocatedBytesForCurrentThread();
            SetRast(1, _states[13]);
            ClearTextRaster(GraphicsLayersLvo.ClearEOL, _states[13]);
            SetRast(1, _states[13]);
            ClearTextRaster(GraphicsLayersLvo.ClearScreen, _states[13]);
            Print("planar-clear-text", before);
            ClearRasterDestination();
            before = GC.GetAllocatedBytesForCurrentThread();
            ScrollRaster(GraphicsLayersLvo.ScrollRaster, _states[13]);
            ClearRasterDestination();
            ScrollRaster(GraphicsLayersLvo.ScrollRasterBF, _states[13]);
            Print("planar-scroll", before);
            ClearRasterDestination();
            before = GC.GetAllocatedBytesForCurrentThread();
            PixelArrayOperationsAndRestore(_states[13]);
            Print("planar-pixel-arrays", before);
            before = GC.GetAllocatedBytesForCurrentThread();
            WriteChunkyPixelsAndRestore(_states[13]);
            Print("planar-chunky", before);
            before = GC.GetAllocatedBytesForCurrentThread();
            BltBitMapToRastPort(_states[13]);
            ClipBlitRestore(_states[13]);
            Print("planar-blits", before);
        }

        internal ResourceFingerprint FingerprintIdle()
            => FingerprintRaw(_idleLayerInfo,
                checked((int)LayersLayout.LayerInfo.Size), "planar");
        internal ResourceFingerprint FingerprintHit()
            => Fingerprint(_hitLayerInfo, _hitBitMap, default, "planar-simple");
        internal ResourceFingerprint FingerprintCreate()
            => Fingerprint(_createLayerInfo, _createBitMap, default, "planar-simple");
        internal ResourceFingerprint FingerprintSmart()
            => Fingerprint(_smartLayerInfo, _smartBitMap, default, "planar-smart");
        internal ResourceFingerprint FingerprintMoveSize()
            => Fingerprint(_moveLayerInfo, _moveBitMap, default, "planar-simple");
        internal ResourceFingerprint FingerprintSuper()
            => Fingerprint(_superLayerInfo, _superDisplay, _superBacking, "planar-super");
        internal ResourceFingerprint FingerprintRefresh()
            => Fingerprint(_refreshLayerInfo, _refreshBitMap, default, "planar-refresh");
        internal ResourceFingerprint FingerprintHook()
            => Fingerprint(_hookLayerInfo, _hookBitMap, default, "planar-guest-hook");
        internal ResourceFingerprint FingerprintRaster()
            => Fingerprint(_rasterLayerInfo, _rasterBitMap, default, "planar-raster");

        private void Move(uint layer, int dx, int dy, M68kCpuState state)
        {
            Reset(state);
            state.A[1] = layer;
            state.D[0] = unchecked((uint)dx);
            state.D[1] = unchecked((uint)dy);
            InvokeLayers(LayersLvo.MoveLayer, state);
            Require(state.D[0] != 0, "MoveLayer reported failure.");
        }

        private void Size(uint layer, int dw, int dh, M68kCpuState state)
        {
            Reset(state);
            state.A[1] = layer;
            state.D[0] = unchecked((uint)dw);
            state.D[1] = unchecked((uint)dh);
            InvokeLayers(LayersLvo.SizeLayer, state);
            Require(state.D[0] != 0, "SizeLayer reported failure.");
        }

        private void ScrollSuper(int dx, int dy)
        {
            var state = _states[14];
            Reset(state);
            state.A[1] = _superLayer.Layer;
            state.D[0] = unchecked((uint)dx);
            state.D[1] = unchecked((uint)dy);
            InvokeLayers(LayersLvo.ScrollLayer, state);
            Require(unchecked((int)state.D[0]) == dx,
                "ScrollLayer did not preserve its void-result D0 operand.");
        }

        private void SyncCopySuper()
        {
            var state = _states[15];
            Reset(state);
            state.A[0] = _superLayer.Layer;
            InvokeGraphics(GraphicsLayersLvo.SyncSBitMap, state);
            Require(state.D[0] == 0, "SyncSBitMap provider path failed.");
            Reset(state);
            state.A[0] = _superLayer.Layer;
            InvokeGraphics(GraphicsLayersLvo.CopySBitMap, state);
            Require(state.D[0] == 0, "CopySBitMap provider path failed.");
        }

        private void RectFill(uint rastPort, M68kCpuState state)
        {
            Reset(state);
            state.A[1] = rastPort;
            state.D[0] = 0;
            state.D[1] = 0;
            state.D[2] = 7;
            state.D[3] = 0;
            InvokeGraphics(GraphicsLayersLvo.RectFill, state);
        }

        private void ComplementLayeredRectangleTwice(
            uint rastPort,
            int minX,
            int minY,
            int maxX,
            int maxY,
            M68kCpuState state)
        {
            ConfigureComplement(rastPort);
            for (var index = 0; index < 2; index++)
            {
                Reset(state);
                state.A[1] = rastPort;
                state.D[0] = unchecked((uint)minX);
                state.D[1] = unchecked((uint)minY);
                state.D[2] = unchecked((uint)maxX);
                state.D[3] = unchecked((uint)maxY);
                InvokeGraphics(GraphicsLayersLvo.RectFill, state);
                Require(state.D[0] == 0,
                    "Layered complement RectFill provider declined.");
            }
        }

        private void BltTemplateTwice(M68kCpuState state)
        {
            ConfigureComplement(_rasterLayer.RastPort);
            for (var index = 0; index < 2; index++)
            {
                Reset(state);
                state.A[0] = _rasterTemplate;
                state.A[1] = _rasterLayer.RastPort;
                state.D[1] = 2;
                state.D[4] = 4;
                state.D[5] = 2;
                InvokeGraphics(GraphicsLayersLvo.BltTemplate, state);
                Require(state.D[0] == 0,
                    "Layered BltTemplate provider declined.");
            }
        }

        private void BltPatternTwice(M68kCpuState state)
        {
            ConfigureComplement(_rasterLayer.RastPort);
            for (var index = 0; index < 2; index++)
            {
                Reset(state);
                state.A[1] = _rasterLayer.RastPort;
                state.D[2] = 3;
                state.D[3] = 1;
                InvokeGraphics(GraphicsLayersLvo.BltPattern, state);
                Require(state.D[0] == 0,
                    "Layered BltPattern provider declined.");
            }
        }

        private void FloodAndRestore(M68kCpuState state)
        {
            ClearRasterDestination();
            Bus.WriteByte(_rasterLayer.RastPort +
                (uint)GraphicsLayout.RastPort.ForegroundPen, 1, 0);
            Bus.WriteByte(_rasterLayer.RastPort +
                (uint)GraphicsLayout.RastPort.BackgroundPen, 0, 0);
            Bus.WriteByte(_rasterLayer.RastPort +
                (uint)GraphicsLayout.RastPort.DrawMode, 0, 0);
            Bus.WriteByte(_rasterLayer.RastPort +
                (uint)GraphicsLayout.RastPort.Mask, 0xFF, 0);
            Reset(state);
            state.A[1] = _rasterLayer.RastPort;
            state.D[0] = 3;
            state.D[1] = 3;
            state.D[2] = 1;
            InvokeGraphics(GraphicsLayersLvo.Flood, state);
            Require(state.D[0] != uint.MaxValue,
                "Layered Flood provider declined.");
            Require(Bus.ReadByte(_rasterBitMap.PixelAddress) != 0,
                "Layered Flood did not mutate its connected region.");
            ClearRasterDestination();
            ConfigureComplement(_rasterLayer.RastPort);
        }

        private void AreaAndRestore(M68kCpuState state)
        {
            ClearRasterDestination();
            var before = RasterPixelHash();
            Bus.WriteByte(_rasterLayer.RastPort +
                (uint)GraphicsLayout.RastPort.ForegroundPen, 1, 0);
            Bus.WriteByte(_rasterLayer.RastPort +
                (uint)GraphicsLayout.RastPort.DrawMode, 0, 0);
            AreaPoint(GraphicsLayersLvo.AreaMove, 1, 1, state);
            AreaPoint(GraphicsLayersLvo.AreaDraw, 6, 1, state);
            AreaPoint(GraphicsLayersLvo.AreaDraw, 6, 6, state);
            AreaPoint(GraphicsLayersLvo.AreaDraw, 1, 6, state);
            Reset(state);
            state.A[1] = _rasterLayer.RastPort;
            InvokeGraphics(GraphicsLayersLvo.AreaEnd, state);
            Require(state.D[0] == 0,
                "Layered AreaEnd provider declined.");
            Require(RasterPixelHash() != before,
                "Layered AreaEnd did not publish polygon pixels.");
            ClearRasterDestination();
            ConfigureComplement(_rasterLayer.RastPort);
        }

        private void AreaEllipseAndRestore(M68kCpuState state)
        {
            ClearRasterDestination();
            var before = RasterPixelHash();
            Bus.WriteByte(_rasterLayer.RastPort +
                (uint)GraphicsLayout.RastPort.ForegroundPen, 1, 0);
            Bus.WriteByte(_rasterLayer.RastPort +
                (uint)GraphicsLayout.RastPort.DrawMode, 0, 0);
            Reset(state);
            state.A[1] = _rasterLayer.RastPort;
            state.D[0] = 3;
            state.D[1] = 3;
            state.D[2] = 2;
            state.D[3] = 2;
            InvokeGraphics(GraphicsLayersLvo.AreaEllipse, state);
            Require(state.D[0] == 0,
                "Layered AreaEllipse collector provider declined.");
            Reset(state);
            state.A[1] = _rasterLayer.RastPort;
            InvokeGraphics(GraphicsLayersLvo.AreaEnd, state);
            Require(state.D[0] == 0,
                "Layered AreaEllipse AreaEnd provider declined.");
            Require(RasterPixelHash() != before,
                "Layered AreaEllipse did not publish pixels.");
            ClearRasterDestination();
            ConfigureComplement(_rasterLayer.RastPort);
        }

        private void AreaPoint(short lvo, short x, short y, M68kCpuState state)
        {
            Reset(state);
            state.A[1] = _rasterLayer.RastPort;
            state.D[0] = unchecked((uint)x);
            state.D[1] = unchecked((uint)y);
            InvokeGraphics(lvo, state);
            Require(state.D[0] == 0,
                "Layered area collector provider declined.");
        }

        private void GetRasterAttributes(M68kCpuState state)
        {
            Reset(state);
            state.A[0] = _rasterAttributeTags;
            state.A[1] = _rasterLayer.RastPort;
            InvokeGraphics(GraphicsLayersLvo.GetRPAttrsA, state);
            Require(state.D[0] == 0,
                "Layered GetRPAttrsA provider declined.");
            Require(Bus.ReadWord(_rasterAttributeTags + 16) == 0 &&
                    Bus.ReadWord(_rasterAttributeTags + 20) == 7,
                "Layered GetRPAttrsA returned the wrong scoped draw bounds.");
        }

        private void SetRast(byte pen, M68kCpuState state)
        {
            Reset(state);
            state.A[1] = _rasterLayer.RastPort;
            state.D[0] = pen;
            InvokeGraphics(GraphicsLayersLvo.SetRast, state);
        }

        private void EraseRaster(M68kCpuState state)
        {
            Reset(state);
            state.A[1] = _rasterLayer.RastPort;
            state.D[0] = 0;
            state.D[1] = 0;
            state.D[2] = 7;
            state.D[3] = 7;
            InvokeGraphics(GraphicsLayersLvo.EraseRect, state);
        }

        private void ReadRasterPixel(M68kCpuState state)
        {
            Reset(state);
            state.A[1] = _rasterLayer.RastPort;
            state.D[0] = 0;
            state.D[1] = 0;
            InvokeGraphics(GraphicsLayersLvo.ReadPixel, state);
            Require(state.D[0] == 0,
                "Layered ReadPixel did not observe the cleared destination.");
        }

        private void DrawOnce(M68kCpuState state)
        {
            Bus.WriteWord(_rasterLayer.RastPort +
                (uint)GraphicsLayout.RastPort.CurrentX, 0);
            Bus.WriteWord(_rasterLayer.RastPort +
                (uint)GraphicsLayout.RastPort.CurrentY, 0);
            Bus.WriteWord(_rasterLayer.RastPort +
                (uint)GraphicsLayout.RastPort.LinePattern, 0xFFFF);
            Bus.WriteByte(_rasterLayer.RastPort +
                (uint)GraphicsLayout.RastPort.LinePatternCount, 15, 0);
            Reset(state);
            state.A[1] = _rasterLayer.RastPort;
            state.D[0] = 7;
            state.D[1] = 0;
            InvokeGraphics(GraphicsLayersLvo.Draw, state);
        }

        private void TextOnce(M68kCpuState state)
        {
            Bus.WriteWord(_rasterLayer.RastPort +
                (uint)GraphicsLayout.RastPort.CurrentX, 0);
            Bus.WriteWord(_rasterLayer.RastPort +
                (uint)GraphicsLayout.RastPort.CurrentY, 7);
            Reset(state);
            state.A[0] = _rasterText;
            state.A[1] = _rasterLayer.RastPort;
            state.D[0] = 1;
            InvokeGraphics(GraphicsLayersLvo.Text, state);
        }

        private void DrawEllipse(M68kCpuState state)
        {
            Reset(state);
            state.A[1] = _rasterLayer.RastPort;
            state.D[0] = 3;
            state.D[1] = 3;
            state.D[2] = 2;
            state.D[3] = 2;
            InvokeGraphics(GraphicsLayersLvo.DrawEllipse, state);
        }

        private void PolyDraw(M68kCpuState state)
        {
            Bus.WriteWord(_rasterLayer.RastPort +
                (uint)GraphicsLayout.RastPort.CurrentX, 0);
            Bus.WriteWord(_rasterLayer.RastPort +
                (uint)GraphicsLayout.RastPort.CurrentY, 0);
            Bus.WriteWord(_rasterLayer.RastPort +
                (uint)GraphicsLayout.RastPort.LinePattern, 0xFFFF);
            Bus.WriteByte(_rasterLayer.RastPort +
                (uint)GraphicsLayout.RastPort.LinePatternCount, 15, 0);
            Reset(state);
            state.A[0] = _rasterPolyPoints;
            state.A[1] = _rasterLayer.RastPort;
            state.D[0] = 2;
            InvokeGraphics(GraphicsLayersLvo.PolyDraw, state);
        }

        private void ClearTextRaster(short lvo, M68kCpuState state)
        {
            Bus.WriteWord(_rasterLayer.RastPort +
                (uint)GraphicsLayout.RastPort.CurrentX, 0);
            Bus.WriteWord(_rasterLayer.RastPort +
                (uint)GraphicsLayout.RastPort.CurrentY, 7);
            Reset(state);
            state.A[1] = _rasterLayer.RastPort;
            InvokeGraphics(lvo, state);
        }

        private void ScrollRaster(short lvo, M68kCpuState state)
        {
            ClearRasterDestination();
            Bus.WriteByte(_rasterBitMap.PixelAddress, 0x55, 0);
            Reset(state);
            state.A[1] = _rasterLayer.RastPort;
            state.D[0] = 1;
            state.D[1] = 0;
            state.D[2] = 0;
            state.D[3] = 0;
            state.D[4] = 7;
            state.D[5] = 0;
            InvokeGraphics(lvo, state);
        }

        private void PixelArrayOperationsAndRestore(M68kCpuState state)
        {
            ClearRasterDestination();
            FillRasterPixelArray(0xCC);
            InvokePixelSpan(
                GraphicsLayersLvo.ReadPixelLine8,
                0,
                0,
                8,
                0,
                state);
            Require(state.D[0] == 8,
                "Layered ReadPixelLine8 returned the wrong visible count.");
            for (var offset = 0u; offset < 8; offset++)
            {
                Require(Bus.ReadByte(_rasterPixelArray + offset) == 0,
                    "Layered ReadPixelLine8 returned the wrong pixel value.");
            }

            FillRasterPixelArray(1);
            InvokePixelSpan(
                GraphicsLayersLvo.WritePixelLine8,
                0,
                0,
                8,
                0,
                state);
            Require(state.D[0] == 8 &&
                    Bus.ReadByte(_rasterBitMap.PixelAddress) == 0xFF,
                "Layered WritePixelLine8 did not publish eight pixels.");
            ClearRasterDestination();

            FillRasterPixelArray(0xCC);
            InvokePixelSpan(
                GraphicsLayersLvo.ReadPixelArray8,
                0,
                0,
                7,
                0,
                state);
            Require(state.D[0] == 8,
                "Layered ReadPixelArray8 returned the wrong visible count.");
            for (var offset = 0u; offset < 8; offset++)
            {
                Require(Bus.ReadByte(_rasterPixelArray + offset) == 0,
                    "Layered ReadPixelArray8 returned the wrong pixel value.");
            }

            FillRasterPixelArray(1);
            InvokePixelSpan(
                GraphicsLayersLvo.WritePixelArray8,
                0,
                0,
                7,
                0,
                state);
            Require(state.D[0] == 8 &&
                    Bus.ReadByte(_rasterBitMap.PixelAddress) == 0xFF,
                "Layered WritePixelArray8 did not publish eight pixels.");
            ClearRasterDestination();
            ConfigureComplement(_rasterLayer.RastPort);
        }

        private void WriteChunkyPixelsAndRestore(M68kCpuState state)
        {
            ClearRasterDestination();
            Reset(state);
            state.A[0] = _rasterLayer.RastPort;
            state.A[2] = _rasterChunkyPixels;
            state.D[0] = 0;
            state.D[1] = 0;
            state.D[2] = 7;
            state.D[3] = 0;
            state.D[4] = 8;
            InvokeGraphics(GraphicsLayersLvo.WriteChunkyPixels, state);
            Require(state.D[0] == 0 &&
                    Bus.ReadByte(_rasterBitMap.PixelAddress) == 0xFF,
                "Layered WriteChunkyPixels did not publish eight pixels.");
            ClearRasterDestination();
            ConfigureComplement(_rasterLayer.RastPort);
        }

        private void InvokePixelSpan(
            short lvo,
            ushort x0,
            ushort y0,
            ushort x1OrWidth,
            ushort y1,
            M68kCpuState state)
        {
            Reset(state);
            state.A[0] = _rasterLayer.RastPort;
            state.A[2] = _rasterPixelArray;
            state.D[0] = x0;
            state.D[1] = y0;
            state.D[2] = x1OrWidth;
            state.D[3] = y1;
            InvokeGraphics(lvo, state);
        }

        private void FillRasterPixelArray(byte value)
        {
            for (var offset = 0u; offset < 32; offset++)
                Bus.WriteByte(_rasterPixelArray + offset, value, 0);
        }

        private void BltBitMapToRastPort(M68kCpuState state)
        {
            Reset(state);
            state.A[0] = _rasterBitMap.Address;
            state.A[1] = _rasterLayer.RastPort;
            state.D[0] = 8;
            state.D[1] = 0;
            state.D[2] = 0;
            state.D[3] = 0;
            state.D[4] = 8;
            state.D[5] = 1;
            state.D[6] = 0xC0;
            InvokeGraphics(GraphicsLayersLvo.BltBitMapRastPort, state);
            Require(state.D[0] == 1,
                "Layered BltBitMapRastPort did not report copied pixels.");
        }

        private void ClipBlitRestore(M68kCpuState state)
        {
            Reset(state);
            state.A[0] = _rasterRestoreLayer.RastPort;
            state.A[1] = _rasterLayer.RastPort;
            state.D[0] = 0;
            state.D[1] = 0;
            state.D[2] = 0;
            state.D[3] = 0;
            state.D[4] = 8;
            state.D[5] = 1;
            state.D[6] = 0xC0;
            InvokeGraphics(GraphicsLayersLvo.ClipBlit, state);
            Require(state.D[0] == 1,
                "Layered ClipBlit did not report copied pixels.");
        }

        private ulong RasterPixelHash()
        {
            var hash = 14695981039346656037UL;
            for (var offset = 0; offset < _rasterBitMap.PixelBytes; offset++)
            {
                hash ^= Bus.ReadByte(
                    _rasterBitMap.PixelAddress + checked((uint)offset));
                hash *= 1099511628211UL;
            }
            return hash;
        }

        private void ClearRasterDestination()
        {
            var bytesPerRow = Bus.ReadWord(_rasterBitMap.Address +
                (uint)GraphicsLayout.BitMap.BytesPerRow);
            for (var row = 0; row < 8; row++)
            {
                Bus.WriteByte(
                    _rasterBitMap.PixelAddress + checked((uint)(row * bytesPerRow)),
                    0,
                    0);
            }
        }

        private void ConfigureComplement(uint rastPort)
        {
            Bus.WriteByte(rastPort +
                (uint)GraphicsLayout.RastPort.ForegroundPen, 1, 0);
            Bus.WriteByte(rastPort +
                (uint)GraphicsLayout.RastPort.DrawMode, 3, 0);
            Bus.WriteByte(rastPort +
                (uint)GraphicsLayout.RastPort.Mask, 0xFF, 0);
        }

        private int CountBacking(uint layer)
        {
            var count = 0;
            var clipRect = Bus.ReadLong(layer + (uint)LayersLayout.Layer.ClipRect);
            for (var index = 0; clipRect != 0 && index < 256; index++)
            {
                if (Bus.ReadLong(clipRect +
                        (uint)LayersLayout.ClipRect.BitMap) != 0)
                    count++;
                clipRect = Bus.ReadLong(clipRect +
                    (uint)LayersLayout.ClipRect.Next);
            }
            return count;
        }

        private void CaptureHook(M68kCpuState state)
        {
            _hookCallbackCount++;
            state.D[0] = 0;
        }
    }

    private sealed class RtgFixture : HostFixture
    {
        private readonly M68kCpuState _state = new();
        private readonly uint _destinationLayerInfo;
        private readonly uint _sourceLayerInfo;
        private readonly BitMapDescriptor _destinationBitMap;
        private readonly BitMapDescriptor _sourceBitMap;
        private readonly LayerSurface _destinationLayer;
        private readonly LayerSurface _sourceLayer;
        private readonly uint _destinationProbe;
        private readonly uint _sourceProbe;
        private readonly uint _maskPlane;

        internal RtgFixture(BenchmarkContext owner)
            : base(owner, MachineOptions
                .ForProfile(MachineProfile.A500Pal512KBoot)
                .WithRtgVram(16L * 1024 * 1024)
                .WithCpu(AmigaM68kCoreFactory.Default,
                    M68kBackendKind.AccurateM68040)
                .WithLiveAgnusDma(false))
        {
            _destinationLayerInfo = NewLayerInfo();
            _sourceLayerInfo = NewLayerInfo();
            _destinationBitMap = AllocateBitMap(48, 24, 8, true);
            _sourceBitMap = AllocateBitMap(48, 24, 8, true);
            _destinationLayer = CreateLayer(
                _destinationLayerInfo,
                _destinationBitMap,
                LayerCreationFlags.Simple,
                20,
                10,
                35,
                17);
            _sourceLayer = CreateLayer(
                _sourceLayerInfo,
                _sourceBitMap,
                LayerCreationFlags.Simple,
                3,
                4,
                18,
                11);
            _maskPlane = Allocate(144);
            for (var offset = 0u; offset < 144; offset++)
                Bus.WriteByte(_maskPlane + offset, 0xFF, 0);
            ScrollLayer(_destinationLayer.Layer, 3, 2);
            ScrollLayer(_sourceLayer.Layer, -2, -1);
            var destinationStride = _destinationBitMap.PixelBytes / 24;
            var sourceStride = _sourceBitMap.PixelBytes / 24;
            _destinationProbe = _destinationBitMap.PixelAddress +
                checked((uint)(10 * destinationStride + 20));
            _sourceProbe = _sourceBitMap.PixelAddress +
                checked((uint)(4 * sourceStride + 3));
            Bus.WriteByte(_sourceProbe, 0xA5, 0);
            Bus.WriteByte(_destinationLayer.RastPort +
                (uint)GraphicsLayout.RastPort.ForegroundPen, 0x5A, 0);
            Bus.WriteByte(_destinationLayer.RastPort +
                (uint)GraphicsLayout.RastPort.DrawMode, 0, 0);
            Bus.WriteByte(_destinationLayer.RastPort +
                (uint)GraphicsLayout.RastPort.Mask, 0xFF, 0);
        }

        internal void RunRaster()
        {
            var before = Bus.ReadByte(_destinationProbe);
            Bus.WriteByte(_destinationLayer.RastPort +
                (uint)GraphicsLayout.RastPort.ForegroundPen, 0x5A, 0);
            WritePixel(3, 2);
            Require(Bus.ReadByte(_destinationProbe) == 0x5A,
                "RTG layered WritePixel did not mutate provider pixels.");
            Bus.WriteByte(_destinationLayer.RastPort +
                (uint)GraphicsLayout.RastPort.ForegroundPen, before, 0);
            WritePixel(3, 2);
            Require(Bus.ReadByte(_destinationProbe) == before,
                "Second RTG WritePixel did not restore provider pixels.");

            Reset(_state);
            _state.A[0] = _sourceLayer.RastPort;
            _state.A[1] = _destinationLayer.RastPort;
            _state.D[0] = unchecked((uint)-2);
            _state.D[1] = unchecked((uint)-1);
            _state.D[2] = 3;
            _state.D[3] = 2;
            _state.D[4] = 1;
            _state.D[5] = 1;
            _state.D[6] = 0xC0;
            InvokeGraphics(GraphicsLayersLvo.ClipBlit, _state);
            Require(_state.D[0] == 1,
                "Translated dual-RTG ClipBlit did not report copied pixels.");
            Require(Bus.ReadByte(_destinationProbe) ==
                    Bus.ReadByte(_sourceProbe),
                "Translated dual-RTG ClipBlit copied the wrong logical pixel.");
            Bus.WriteByte(_destinationLayer.RastPort +
                (uint)GraphicsLayout.RastPort.ForegroundPen, before, 0);
            WritePixel(3, 2);
            Bus.WriteByte(_destinationLayer.RastPort +
                (uint)GraphicsLayout.RastPort.ForegroundPen, 0x5A, 0);
            Require(Bus.ReadByte(_destinationProbe) == before,
                "Translated dual-RTG workload did not restore provider pixels.");

            MaskedBlitAndRestore();
        }

        internal void PrintRasterAllocationProbe()
        {
            static void Print(string name, long before)
                => Console.WriteLine($"allocation_probe\tstage={name}\tbytes={GC.GetAllocatedBytesForCurrentThread() - before}");

            for (var index = 0; index < 16; index++)
                RunRaster();
            var before = GC.GetAllocatedBytesForCurrentThread();
            var original = Bus.ReadByte(_destinationProbe);
            WritePixel(3, 2);
            Bus.WriteByte(_destinationLayer.RastPort +
                (uint)GraphicsLayout.RastPort.ForegroundPen, original, 0);
            WritePixel(3, 2);
            Print("rtg-write", before);
            Bus.WriteByte(_destinationLayer.RastPort +
                (uint)GraphicsLayout.RastPort.ForegroundPen, 0x5A, 0);

            before = GC.GetAllocatedBytesForCurrentThread();
            Reset(_state);
            _state.A[0] = _sourceLayer.RastPort;
            _state.A[1] = _destinationLayer.RastPort;
            _state.D[0] = unchecked((uint)-2);
            _state.D[1] = unchecked((uint)-1);
            _state.D[2] = 3;
            _state.D[3] = 2;
            _state.D[4] = 1;
            _state.D[5] = 1;
            _state.D[6] = 0xC0;
            InvokeGraphics(GraphicsLayersLvo.ClipBlit, _state);
            Bus.WriteByte(_destinationLayer.RastPort +
                (uint)GraphicsLayout.RastPort.ForegroundPen, original, 0);
            WritePixel(3, 2);
            Bus.WriteByte(_destinationLayer.RastPort +
                (uint)GraphicsLayout.RastPort.ForegroundPen, 0x5A, 0);
            Print("rtg-clipblit", before);

            before = GC.GetAllocatedBytesForCurrentThread();
            MaskedBlitAndRestore();
            Print("rtg-maskblit", before);
        }

        internal ResourceFingerprint Fingerprint()
            => Fingerprint(
                _destinationLayerInfo,
                _destinationBitMap,
                _sourceBitMap,
                "rtg-raster-translated-dual");

        private void WritePixel(int x, int y)
        {
            Reset(_state);
            _state.A[1] = _destinationLayer.RastPort;
            _state.D[0] = unchecked((uint)x);
            _state.D[1] = unchecked((uint)y);
            InvokeGraphics(GraphicsLayersLvo.WritePixel, _state);
            Require(_state.D[0] == 0,
                "RTG layered WritePixel provider declined.");
        }

        private void MaskedBlitAndRestore()
        {
            var before = Bus.ReadByte(_destinationProbe);
            Reset(_state);
            _state.A[0] = _sourceBitMap.Address;
            _state.A[1] = _destinationLayer.RastPort;
            _state.A[2] = _maskPlane;
            _state.D[0] = 3;
            _state.D[1] = 4;
            _state.D[2] = 3;
            _state.D[3] = 2;
            _state.D[4] = 1;
            _state.D[5] = 1;
            _state.D[6] = 0xC0;
            InvokeGraphics(GraphicsLayersLvo.BltMaskBitMapRastPort, _state);
            Require(_state.D[0] == 1 &&
                    Bus.ReadByte(_destinationProbe) ==
                        Bus.ReadByte(_sourceProbe),
                "Layered RTG BltMaskBitMapRastPort did not copy its selected pixel.");
            Bus.WriteByte(_destinationLayer.RastPort +
                (uint)GraphicsLayout.RastPort.ForegroundPen, before, 0);
            WritePixel(3, 2);
            Bus.WriteByte(_destinationLayer.RastPort +
                (uint)GraphicsLayout.RastPort.ForegroundPen, 0x5A, 0);
            Require(Bus.ReadByte(_destinationProbe) == before,
                "Layered RTG masked blit did not restore provider pixels.");
        }

        private void ScrollLayer(uint layer, int deltaX, int deltaY)
        {
            Reset(_state);
            _state.A[1] = layer;
            _state.D[0] = unchecked((uint)deltaX);
            _state.D[1] = unchecked((uint)deltaY);
            InvokeLayers(LayersLvo.ScrollLayer, _state);
            Require(unchecked((int)_state.D[0]) == deltaX,
                "RTG setup ScrollLayer did not preserve its D0 operand.");
        }
    }

    private sealed class MorphFixture : HostFixture
    {
        private readonly M68kCpuState _state = new();
        private readonly uint _layerInfo;
        private readonly BitMapDescriptor _bitMap;
        private readonly LayerSurface _layer;
        private readonly uint _tags;

        internal MorphFixture(BenchmarkContext owner)
            : base(owner, MachineOptions
                .ForProfile(MachineProfile.A500Pal512KBoot)
                .WithLiveAgnusDma(false))
        {
            Boot.ResetCopperStartLayersForTest();
            Require(Boot.ReinstallCopperStartLayersForTest(
                    PortableLayers.LayersAbiProfile.Unified),
                "MorphOS M68k Layers profile reinstall failed.");
            LayersBase = Boot.CopperStartLayersLibraryBase;
            _layerInfo = NewLayerInfo();
            _bitMap = AllocateBitMap(16, 8, 1, false);
            _layer = CreateLayer(_layerInfo, _bitMap,
                LayerCreationFlags.Simple, 0, 0, 15, 7);
            _tags = Allocate(16);
            Bus.WriteLong(_tags, (uint)LayerRenderTag.DestinationBitMap);
            Bus.WriteLong(_tags + 4, _bitMap.Address);
            Bus.WriteLong(_tags + 8, 0);
            Bus.WriteLong(_tags + 12, 0);
        }

        internal void RunRenderDecline()
        {
            Reset(_state);
            _state.A[0] = _layerInfo;
            _state.A[1] = _tags;
            _state.D[0] = uint.MaxValue;
            InvokeLayers(LayersLvo.RenderLayerInfoTagList, _state);
            Require(_state.D[0] == 0,
                "Missing Morph render provider did not fail deterministically.");
        }

        internal ResourceFingerprint Fingerprint()
            => Fingerprint(_layerInfo, _bitMap, default, "morph-render-decline");
    }

    private readonly record struct BitMapDescriptor(
        uint Address,
        uint PixelAddress,
        int PixelBytes,
        bool Rtg);

    private readonly record struct LayerSurface(
        uint Layer,
        uint RastPort,
        short MinX,
        short MinY,
        short MaxX,
        short MaxY);

    private static M68kCpuState[] CreateStates(int count)
    {
        var states = new M68kCpuState[count];
        for (var index = 0; index < count; index++)
            states[index] = new M68kCpuState();
        return states;
    }
}
