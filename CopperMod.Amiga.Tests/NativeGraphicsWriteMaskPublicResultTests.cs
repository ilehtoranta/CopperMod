using Copper68k;
using CopperMod.Amiga.CopperStart;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsWriteMaskPublicResultTests
{
    // The V39 autodoc defines an ULONG success result; zero means unsupported
    // per-bit masking. AROS returns -1 after a supported mask publication.
    // Portable 0/-1 helper status is internal, not this public result shape.
    // Sources: d0.se/autodocs/graphics.library/SetWriteMask and AROS's
    // rom/graphics/setwritemask.c. Native call routes live in the byte ABI fixture.
    private const uint ArenaAddress = 0x00D4_0000;
    private const int ArenaSize = 0x400;
    private const uint RastPort = ArenaAddress + 0x40;
    private const uint NonArgumentRastPort = ArenaAddress + 0x200;
    private const int MaskIndex = 0x40 + 0x18;

    public static IEnumerable<object[]> HostCases()
    {
        foreach (var nativeOverlay in new[] { false, true })
        {
            foreach (var value in new[] { 0u, 1u, 0x80u, 0xCAFE_00A5u, 0xFFFFu, uint.MaxValue })
                yield return new object[] { nativeOverlay, "success", value };
            foreach (var scenario in new[] { "null", "odd", "wrapped", "read-only", "provider-owned" })
                yield return new object[] { nativeOverlay, scenario, 0xCAFE_BAE1u };
        }
    }

    [Theory]
    [MemberData(nameof(HostCases))]
    public void HostMaskResultIsNonzeroOnSuccessAndLeavesDeclinesUnclaimed(
        bool nativeOverlay, string scenario, uint value)
    {
        var success = scenario == "success";
        var pointer = scenario switch
        {
            "null" => 0u,
            "odd" => RastPort + 1u,
            "wrapped" => 0xFFFF_FFE8u,
            "success" or "read-only" or "provider-owned" => RastPort,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var fixture = new Fixture(scenario);
        var before = fixture.ReadArena();
        var expected = before.ToArray();
        if (success) expected[MaskIndex] = unchecked((byte)value);
        var state = MakeState(pointer, value);
        var originalData = state.D.ToArray();
        var originalAddresses = state.A.ToArray();
        var originalPc = state.ProgramCounter;
        var originalSr = state.StatusRegister;
        var originalCycles = state.Cycles;

        var handled = fixture.Adapter.TryInvoke(state, -984, nativeOverlay);
        var failures = new List<string>();
        Check(failures, "handled versus unclaimed provider/guard route", () => Assert.Equal(success, handled));
        Check(failures, "public ULONG result, not portable helper status", () =>
            Assert.Equal(success ? uint.MaxValue : value, state.D[0]));
        Check(failures, "exactly the owned mask byte changes", () =>
            Assert.Equal(expected, fixture.ReadArena()));
        Check(failures, "every other register and host call-state field is unchanged", () =>
        {
            Assert.Equal(originalData.Skip(1).ToArray(), state.D.Skip(1).ToArray());
            Assert.Equal(originalAddresses, state.A);
            Assert.Equal(originalPc, state.ProgramCounter);
            Assert.Equal(originalSr, state.StatusRegister);
            Assert.Equal(originalCycles, state.Cycles);
        });
        Assert.True(failures.Count == 0,
            $"{(nativeOverlay ? "native overlay adapter" : "host adapter")}, {scenario}, {value:X8}:\n"
            + string.Join("\n", failures));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(0xCAFE_00A5u)]
    [InlineData(uint.MaxValue)]
    public void PortableMaskHelperKeepsItsInternalZeroSuccessStatus(uint value)
    {
        var fixture = new Fixture("success");
        var expected = fixture.ReadArena();
        expected[MaskIndex] = unchecked((byte)value);
        Assert.Equal(0, fixture.Core.SetWriteMask(RastPort, value));
        Assert.Equal(expected, fixture.ReadArena());
    }

    private static M68kCpuState MakeState(uint pointer, uint value)
    {
        var state = new M68kCpuState
        {
            ProgramCounter = 0x00B8_0000,
            StatusRegister = 0x2700,
            Cycles = 125
        };
        for (var index = 0; index < 8; index++)
        {
            state.D[index] = 0xD0D0_0000u + (uint)index * 0x0101_0101u;
            state.A[index] = 0xA0A0_0000u + (uint)index * 0x0101_0101u;
        }
        state.D[0] = value;
        state.A[0] = pointer;
        state.A[1] = NonArgumentRastPort;
        state.A[6] = 0x0070_0000;
        state.A[7] = 0x00C0_0000;
        return state;
    }

    private static void Check(List<string> failures, string label, Action assertion)
    {
        try { assertion(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failures.Add(label + ": " + exception.Message);
        }
    }

    private sealed class Fixture
    {
        private readonly AmigaBus _bus = new();

        internal Fixture(string scenario)
        {
            var bytes = Enumerable.Repeat((byte)0xA5, ArenaSize).ToArray();
            bytes.AsSpan((int)(RastPort - ArenaAddress), 100).Clear();
            bytes[MaskIndex] = 0x5A;
            if (scenario == "read-only")
                _bus.MapReadOnlyMemory(ArenaAddress, bytes);
            else
                _bus.MapWritableMemory(ArenaAddress, bytes);
            var memory = new CopperStartGraphicsMemoryAdapter(new HostGuestMemory(_bus));
            var unused = new UnusedBackends();
            Core = new GraphicsLibraryCore(memory, unused, unused, unused);
            Adapter = new CopperStartGraphicsRegisterAdapter(
                Core,
                isRtgRastPort: address => scenario == "provider-owned" && address == RastPort,
                isWritableMemoryRange: (address, count) => scenario != "read-only"
                    && address >= ArenaAddress
                    && (ulong)address + count <= (ulong)ArenaAddress + ArenaSize);
            Assert.Equal(-984, (int)GraphicsLvo.SetWriteMask);
            Assert.Equal((byte)0x5A, _bus.ReadByte(RastPort + 0x18));
        }

        internal GraphicsLibraryCore Core { get; }
        internal CopperStartGraphicsRegisterAdapter Adapter { get; }

        internal byte[] ReadArena()
            => Enumerable.Range(0, ArenaSize)
                .Select(index => _bus.ReadByte(ArenaAddress + (uint)index)).ToArray();
    }

    private sealed class UnusedBackends : IGraphicsAllocatorBackend, IGraphicsBlitterBackend, IGraphicsDisplayBackend
    {
        public bool TryAllocate(uint byteCount, GraphicsMemoryClass memoryClass, out uint address)
        {
            address = 0;
            throw new InvalidOperationException("SetWriteMask must not allocate.");
        }
        public void Free(uint address, uint byteCount, GraphicsMemoryClass memoryClass)
            => throw new InvalidOperationException("SetWriteMask must not free resources.");
        public void Own() => throw new InvalidOperationException("No blitter ownership is in scope.");
        public void Disown() => throw new InvalidOperationException("No blitter ownership is in scope.");
        public void Wait() => throw new InvalidOperationException("No blitter wait is in scope.");
        public void Submit(uint operationAddress) => throw new InvalidOperationException("No blitter submission is in scope.");
        public void PublishView(uint viewAddress) => throw new InvalidOperationException("No display publication is in scope.");
        public void WaitForTopOfFrame() => throw new InvalidOperationException("No display wait is in scope.");
        public void WaitForBeginningOfVerticalBlank(uint viewPortAddress) => throw new InvalidOperationException("No display wait is in scope.");
        public ushort GetBeamPosition() => throw new InvalidOperationException("No beam access is in scope.");
    }
}
