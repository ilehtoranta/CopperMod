using Copper68k;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class GraphicsAskSoftStyleHostRastPortBoundaryTests
{
    // Keep the unsigned AskSoftStyle result distinct from a failed query.
    // The classic Font LONG is at RP+0x34 and intrinsic style is at Font+0x16.
    // Sources: d0.se/include/graphics/rastport.h, graphics/text.h and the
    // graphics.library/AskSoftStyle autodoc. No full-object mapping is needed.
    private const uint RastPort = 0x00D0_0100;
    private const uint DecoyRastPort = 0x00D0_0200;
    private const uint Font = 0x00D1_0000;
    private const uint DecoyFont = 0x00D2_0000;
    private static readonly string[] ScenarioNames =
    {
        "ordinary", "last-font-slot", "before-last-font-slot", "first-even-port",
        "ordinary-null-font", "last-slot-null-font", "null-port", "null-port-empty-font",
        "odd-port", "font-slot-wrap", "last-even-port", "last-odd-port",
        "undefined-style-bits", "unreadable-style"
    };

    public static IEnumerable<object[]> HostCases()
    {
        foreach (var nativeOverlay in new[] { false, true })
        {
            foreach (var scenario in ScenarioNames)
                yield return new object[] { nativeOverlay, scenario };
            yield return new object[] { nativeOverlay, "provider-owned" };
        }
    }

    public static IEnumerable<object[]> PortableCases()
        => ScenarioNames.Select(scenario => new object[] { scenario });

    [Theory]
    [MemberData(nameof(HostCases))]
    public void HostAskStyleLeavesInvalidRastPortsUnclaimedAndRetainsUnsignedResults(
        bool nativeOverlay, string scenario)
    {
        const uint argument = 0xF00D_0011;
        var data = Resolve(scenario);
        var providerOwned = scenario == "provider-owned";
        var expectedHandled = data.Handled && !providerOwned;
        var memory = MakeMemory(data);
        var unused = new UnusedBackends();
        var core = new GraphicsLibraryCore(memory, unused, unused, unused);
        var adapter = new CopperStartGraphicsRegisterAdapter(
            core, isRtgRastPort: address => providerOwned && address == data.Pointer);
        var state = MakeState(data.Pointer, argument);
        var originalData = state.D.ToArray();
        var originalAddresses = state.A.ToArray();
        var originalPc = state.ProgramCounter;
        var originalSr = state.StatusRegister;
        var originalCycles = state.Cycles;
        var before = memory.Snapshot();

        Assert.Equal(-84, (int)GraphicsLvo.AskSoftStyle);
        var handled = adapter.TryInvoke(state, -84, nativeOverlay);
        var failures = new List<string>();
        Check(failures, "handled versus malformed/provider-owned", () =>
            Assert.Equal(expectedHandled, handled));
        Check(failures, "public ULONG result or complete original decline D0", () =>
            Assert.Equal(expectedHandled ? data.Result : argument, state.D[0]));
        Check(failures, "read-only query preserves every seeded byte", () =>
            Assert.Equal(before, memory.Snapshot()));
        Check(failures, "all other registers and host call-state fields remain unchanged", () =>
        {
            Assert.Equal(originalData.Skip(1).ToArray(), state.D.Skip(1).ToArray());
            Assert.Equal(originalAddresses, state.A);
            Assert.Equal(originalPc, state.ProgramCounter);
            Assert.Equal(originalSr, state.StatusRegister);
            Assert.Equal(originalCycles, state.Cycles);
        });
        Assert.True(failures.Count == 0,
            $"{(nativeOverlay ? "native-overlay adapter" : "host adapter")}, {scenario}:\n"
            + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(PortableCases))]
    public void PortableAskStyleReportsAdmissionSeparatelyFromItsUnsignedValue(string scenario)
    {
        var data = Resolve(scenario);
        var memory = MakeMemory(data);
        var before = memory.Snapshot();
        var handled = GraphicsTextOperations.TryAskSoftStyle(
            memory, data.Pointer, new GraphicsMemoryFontBackend(memory), out var style);
        Assert.Equal(data.Handled, handled);
        Assert.Equal(data.Handled ? data.Result : 0u, style);
        Assert.Equal(before, memory.Snapshot());
    }

    private sealed record Scenario(uint Pointer, uint FontAddress, byte Intrinsic,
        bool Handled, uint Result, bool MissingStyle = false);

    private static Scenario Resolve(string scenario) => scenario switch
    {
        "ordinary" or "provider-owned" => new(RastPort, Font, 0x05, true, 0xFFFF_FFFA),
        "last-font-slot" => new(0xFFFF_FFC8, Font, 0x05, true, 0xFFFF_FFFA),
        "before-last-font-slot" => new(0xFFFF_FFC6, Font, 0x05, true, 0xFFFF_FFFA),
        "first-even-port" => new(2, Font, 0x05, true, 0xFFFF_FFFA),
        "ordinary-null-font" => new(RastPort, 0, 0x05, true, 0),
        "last-slot-null-font" => new(0xFFFF_FFC8, 0, 0x05, true, 0),
        "null-port" => new(0, Font, 0x05, false, 0),
        "null-port-empty-font" => new(0, 0, 0x05, false, 0),
        "odd-port" => new(RastPort + 1, Font, 0x05, false, 0),
        "font-slot-wrap" => new(0xFFFF_FFCA, Font, 0x05, false, 0),
        "last-even-port" => new(0xFFFF_FFFE, Font, 0x05, false, 0),
        "last-odd-port" => new(uint.MaxValue, Font, 0x05, false, 0),
        "undefined-style-bits" => new(RastPort, Font, 0x00, true, uint.MaxValue),
        "unreadable-style" => new(RastPort, Font, 0x05, false, 0, MissingStyle: true),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario))
    };

    private static ByteAddressableMemory MakeMemory(Scenario data)
    {
        var memory = new ByteAddressableMemory();
        Assert.True(memory.TryWriteLong(unchecked(data.Pointer + 0x34u), data.FontAddress));
        Assert.True(memory.TryReadLong(unchecked(data.Pointer + 0x34u), out var seededFont));
        Assert.Equal(data.FontAddress, seededFont);
        if (!data.MissingStyle)
            Assert.True(memory.TryWriteByte(data.FontAddress + 0x16u, data.Intrinsic));
        Assert.True(memory.TryWriteLong(DecoyRastPort + 0x34u, DecoyFont));
        Assert.True(memory.TryWriteByte(DecoyFont + 0x16u, 0xA5));
        return memory;
    }

    private static M68kCpuState MakeState(uint pointer, uint argument)
    {
        var state = new M68kCpuState
        {
            ProgramCounter = 0x00B8_0000,
            StatusRegister = 0x2700,
            Cycles = 137
        };
        for (var index = 0; index < 8; index++)
        {
            state.D[index] = 0xD0D0_0000u + (uint)index * 0x0101_0101u;
            state.A[index] = 0xA0A0_0000u + (uint)index * 0x0101_0101u;
        }
        state.D[0] = argument;
        state.A[0] = DecoyRastPort;
        state.A[1] = pointer;
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

    private sealed class ByteAddressableMemory : IGraphicsMemory
    {
        private readonly Dictionary<uint, byte> _bytes = new();

        // This host memory intentionally permits byte-assembled odd and
        // wrapped LONGs. The public graphics boundary, not an incidental
        // missing cell or memory-alignment check, must reject bad RastPorts.
        public bool TryReadByte(uint address, out byte value)
            => _bytes.TryGetValue(address, out value);

        public bool TryReadWord(uint address, out ushort value)
        {
            value = 0;
            if (!TryReadByte(address, out var high) ||
                !TryReadByte(unchecked(address + 1), out var low))
                return false;
            value = (ushort)((high << 8) | low);
            return true;
        }

        public bool TryReadLong(uint address, out uint value)
        {
            value = 0;
            if (!TryReadWord(address, out var high) ||
                !TryReadWord(unchecked(address + 2), out var low))
                return false;
            value = ((uint)high << 16) | low;
            return true;
        }

        public bool TryWriteByte(uint address, byte value)
        {
            _bytes[address] = value;
            return true;
        }

        public bool TryWriteWord(uint address, ushort value)
            => TryWriteByte(address, (byte)(value >> 8)) &&
               TryWriteByte(unchecked(address + 1), (byte)value);

        public bool TryWriteLong(uint address, uint value)
            => TryWriteWord(address, (ushort)(value >> 16)) &&
               TryWriteWord(unchecked(address + 2), (ushort)value);

        internal KeyValuePair<uint, byte>[] Snapshot()
            => _bytes.OrderBy(item => item.Key).ToArray();
    }

    private sealed class UnusedBackends : IGraphicsAllocatorBackend, IGraphicsBlitterBackend, IGraphicsDisplayBackend
    {
        public bool TryAllocate(uint byteCount, GraphicsMemoryClass memoryClass, out uint address)
        {
            address = 0;
            throw new InvalidOperationException("AskSoftStyle must not allocate.");
        }
        public void Free(uint address, uint byteCount, GraphicsMemoryClass memoryClass)
            => throw new InvalidOperationException("AskSoftStyle must not free resources.");
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
