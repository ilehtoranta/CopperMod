using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class GraphicsDBufInfoInputRegisterTests
{
    private const uint TargetInfo = 0x00D0_0100;
    private const uint DecoyInfo = 0x00D0_0200;
    private const uint ForeignInfo = 0x00D0_0300;
    private const uint CapturedD0 = 0xA1B2_C3D4;

    [Theory]
    [InlineData(false, "owned-vs-owned")]
    [InlineData(true, "owned-vs-owned")]
    [InlineData(false, "owned-vs-odd")]
    [InlineData(true, "owned-vs-odd")]
    [InlineData(false, "null-vs-owned")]
    [InlineData(true, "null-vs-owned")]
    [InlineData(false, "foreign-vs-owned")]
    [InlineData(true, "foreign-vs-owned")]
    public void HostFreeDBufInfoRetiresOnlyItsA1ObjectAcrossDirectAndMappedDispatch(
        bool mapped, string scenario)
    {
        // Public synopsis and NULL behavior:
        // https://d0.se/autodocs/graphics.library/FreeDBufInfo
        using var fixture = new Fixture(mapped);
        var (a0, a1) = scenario switch
        {
            "owned-vs-owned" => (DecoyInfo, TargetInfo),
            "owned-vs-odd" => (1u, TargetInfo),
            "null-vs-owned" => (DecoyInfo, 0u),
            "foreign-vs-owned" => (DecoyInfo, ForeignInfo),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var targetRetired = a1 == TargetInfo;
        var claimed = a1 != ForeignInfo;
        var before = fixture.CaptureMemory();
        var state = fixture.Free(a0, a1);
        var failures = new List<string>();
        Check(failures, "exact original retirement", () => Assert.Equal(
            targetRetired ? new[] { (TargetInfo, GraphicsLayouts.DBufInfoSize) } : Array.Empty<(uint, int)>(),
            fixture.Frees));
        Check(failures, "cancellation before intended release", () => Assert.Equal(
            targetRetired ? RetirementEvents(TargetInfo) : Array.Empty<string>(), fixture.Events));
        Check(failures, "guarded register/cycle state", () => fixture.AssertState(state, a0, a1, claimed));
        Check(failures, "no memory mutation", () => AssertMemoryEqual(before, fixture.CaptureMemory()));

        // Equal A0/A1 follow-ups make registry observations independent of
        // the routing fix. A successfully retired target cannot be freed
        // twice; the ignored A0 object must still be owned and releasable.
        ProbeOwnership(TargetInfo, expectedOwned: !targetRetired);
        ProbeOwnership(DecoyInfo, expectedOwned: true);
        Check(failures, "only the two setup allocations", () => Assert.Equal(new[]
        {
            (TargetInfo, GraphicsLayouts.DBufInfoSize, 1u),
            (DecoyInfo, GraphicsLayouts.DBufInfoSize, 1u)
        }, fixture.Allocations));
        Check(failures, "all retirement snapshots", () =>
        {
            Assert.Equal(fixture.Frees.Count, fixture.MemoryAtFrees.Count);
            foreach (var snapshot in fixture.MemoryAtFrees)
                AssertMemoryEqual(before, snapshot);
        });
        Check(failures, "final guest bytes", () => AssertMemoryEqual(before, fixture.CaptureMemory()));
        Assert.True(failures.Count == 0,
            $"{(mapped ? "mapped ROM overlay" : "direct host")}, {scenario}:\n" + string.Join("\n", failures));

        void ProbeOwnership(uint info, bool expectedOwned)
        {
            var freeCount = fixture.Frees.Count;
            var eventCount = fixture.Events.Count;
            var followUp = fixture.Free(info, info);
            Check(failures, $"ownership of {info:X8}", () => Assert.Equal(
                expectedOwned ? new[] { (info, GraphicsLayouts.DBufInfoSize) } : Array.Empty<(uint, int)>(),
                fixture.Frees.Skip(freeCount)));
            Check(failures, $"cancellation ownership of {info:X8}", () => Assert.Equal(
                expectedOwned ? RetirementEvents(info) : Array.Empty<string>(), fixture.Events.Skip(eventCount)));
            Check(failures, $"follow-up state of {info:X8}", () =>
                fixture.AssertState(followUp, info, info, expectedOwned));
        }
    }

    private static string[] RetirementEvents(uint address)
        => new[] { $"cancel:{address:X8}", $"free:{address:X8}:{GraphicsLayouts.DBufInfoSize}" };

    private static void Check(List<string> failures, string contract, Action assertion)
    {
        try { assertion(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failures.Add(contract + ": " + exception.Message);
        }
    }

    private sealed record MemorySnapshot(byte[] Guest, byte[] LibraryImage);
    private static void AssertMemoryEqual(MemorySnapshot expected, MemorySnapshot actual)
    {
        Assert.Equal(expected.Guest, actual.Guest);
        Assert.Equal(expected.LibraryImage, actual.LibraryImage);
    }

    private sealed class Fixture : IDisposable
    {
        private const uint GuestBase = 0x00D0_0000;
        private const int GuestSize = 0x500;
        private const uint ViewPort = GuestBase;
        private const uint RasInfo = GuestBase + 0x60;
        private const uint ImageBase = 0x00F8_F000;
        private const int ImageSize = 0x2000;
        private const uint GraphicsBase = 0x00F9_0000;
        private const uint ProviderTarget = 0x00FA_A100;
        private const uint CallerPc = 0x0012_A100;
        private const uint StackPointer = 0x00C7_0020;
        private readonly bool _mapped;
        private readonly GraphicsServices _services;
        private readonly Queue<uint> _allocationResults = new(new[] { TargetInfo, DecoyInfo });

        internal Fixture(bool mapped)
        {
            _mapped = mapped;
            Bus.MapWritableMemory(GuestBase, new byte[GuestSize]);
            var image = new byte[ImageSize];
            var vectorAddress = checked((uint)((long)GraphicsBase + (int)GraphicsLvo.FreeDBufInfo));
            var slot = checked((int)(vectorAddress - ImageBase));
            image[slot] = 0x4E;
            image[slot + 1] = 0xF9;
            image[slot + 2] = unchecked((byte)(ProviderTarget >> 24));
            image[slot + 3] = unchecked((byte)(ProviderTarget >> 16));
            image[slot + 4] = unchecked((byte)(ProviderTarget >> 8));
            image[slot + 5] = unchecked((byte)ProviderTarget);
            Bus.MapReadOnlyMemory(ImageBase, image);
            var memory = new HostGuestMemory(Bus);
            memory.WriteLong(ViewPort + (uint)GraphicsLayouts.ViewPortRasInfo, RasInfo);
            memory.WriteLong(RasInfo + (uint)GraphicsLayouts.RasInfoNext, 0);
            memory.WriteLong(RasInfo + (uint)GraphicsLayouts.RasInfoBitMap, 0);
            var context = new CopperStartGraphicsContext(
                memory: memory,
                waitTof: null,
                writeCustomRegister: static (_, _, _) => { },
                selectFrontViewPort: static _ => { },
                requestDisplayRebuild: null,
                initializeCompatibilityViewPort: static _ => { },
                isMappedRastPort: address => memory.IsMapped(address, GraphicsLayouts.RastPortMinimumSize),
                ensureCompatibilityFont: static () => 0,
                logCall: static (_, _) => { },
                bltBitMap: static _ => 0,
                clipBlit: static _ => 0,
                bltBitMapRastPort: static _ => 0,
                allocBitMap: static _ => 0,
                freeBitMap: static _ => { },
                getBitMapAttr: static (_, _) => 0,
                changeViewPortBitMap: static (_, _) => 0,
                mergeCopperLists: static _ => 0,
                makeViewPort: static _ => 0,
                loadView: null,
                loadRgb4: static _ => { },
                setRgb4: static _ => { },
                ensureCompatibilityHostObject: static () => 0,
                draw: static _ => { },
                text: static _ => { },
                setRast: static _ => { },
                rectFill: static _ => { },
                allocateMemory: (bytes, flags) =>
                {
                    Assert.Equal(GraphicsLayouts.DBufInfoSize, bytes);
                    Assert.Equal(1u, flags); // MEMF_PUBLIC
                    var address = _allocationResults.Dequeue();
                    Allocations.Add((address, bytes, flags));
                    return address;
                },
                freeMemory: (address, bytes) =>
                {
                    Frees.Add((address, bytes));
                    Events.Add($"free:{address:X8}:{bytes}");
                    MemoryAtFrees.Add(CaptureMemory());
                },
                cancelDoubleBufferMessages: address => Events.Add($"cancel:{address:X8}"));
            _services = new GraphicsServices(context);
            if (mapped)
                Assert.True(_services.InstallKickstartRomOverlay(GraphicsBase));

            foreach (var expected in new[] { TargetInfo, DecoyInfo })
            {
                var allocated = Invoke(GraphicsLvo.AllocDBufInfo, ViewPort, 0);
                Assert.Equal(expected, allocated.D[0]);
                Assert.Equal((ushort)GraphicsLayouts.ExecMessageSize,
                    memory.ReadWord(expected + (uint)GraphicsLayouts.DBufInfoSafeMessage +
                        (uint)GraphicsLayouts.ExecMessageLength));
            }
            Assert.Empty(_allocationResults);
            Assert.Empty(Frees);
            Assert.Empty(Events);
        }

        private AmigaBus Bus { get; } = new();
        internal List<(uint Address, int Size, uint Flags)> Allocations { get; } = new();
        internal List<(uint Address, int Size)> Frees { get; } = new();
        internal List<string> Events { get; } = new();
        internal List<MemorySnapshot> MemoryAtFrees { get; } = new();
        private byte[] ReadBytes(uint address, int length)
            => Enumerable.Range(0, length).Select(index => Bus.ReadByte(address + (uint)index)).ToArray();
        internal MemorySnapshot CaptureMemory()
            => new(ReadBytes(GuestBase, GuestSize), ReadBytes(ImageBase, ImageSize));

        internal M68kCpuState Free(uint a0, uint a1) => Invoke(GraphicsLvo.FreeDBufInfo, a0, a1);

        private M68kCpuState Invoke(GraphicsLvo vector, uint a0, uint a1)
        {
            var state = CreateState(a0, a1);
            if (_mapped)
            {
                var vectorAddress = checked((uint)((long)GraphicsBase + (int)vector));
                Assert.True(Bus.TryInvokeHostGatewayAt(vectorAddress, state));
            }
            else
            {
                _services.Invoke(state, (int)vector);
            }
            return state;
        }

        private static M68kCpuState CreateState(uint a0, uint a1)
        {
            var state = new M68kCpuState
            {
                Cycles = 211,
                ProgramCounter = CallerPc,
                StatusRegister = 0x2501
            };
            for (var index = 0; index < 8; index++)
            {
                state.D[index] = 0xD0D0_0100u + (uint)index;
                state.A[index] = 0xA0A0_0100u + (uint)index;
            }
            state.D[0] = CapturedD0;
            state.A[0] = a0;
            state.A[1] = a1;
            state.A[6] = GraphicsBase;
            state.A[7] = StackPointer;
            return state;
        }

        internal void AssertState(M68kCpuState actual, uint a0, uint a1, bool claimed)
        {
            var expected = CreateState(a0, a1);
            expected.D[0] = claimed ? 0u : CapturedD0;
            Assert.Equal(expected.D, actual.D);
            Assert.Equal(expected.A, actual.A);
            Assert.Equal(expected.StatusRegister, actual.StatusRegister);
            Assert.Equal(expected.Cycles, actual.Cycles);
            Assert.Equal(expected.NativeCycles, actual.NativeCycles);
            Assert.Equal(_mapped && !claimed ? ProviderTarget : CallerPc, actual.ProgramCounter);
        }

        public void Dispose() => _services.Dispose();
    }
}
