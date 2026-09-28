using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsCloseMonitorAdmissionTests
{
    private const int CloseMonitorLvo = -720;
    private const int CloseMonitorFunctionOrdinal = 119;
    private const uint CapturedD0 = 0xCAFE_BABEu;
    private const uint MonitorAddress = 0x00D2_0000;
    private const uint ForeignMonitorAddress = 0x00D3_0000;
    private const uint HighPhysicalAddress = 0x00FF_FF00;
    private const ushort InitialOpenCount = 3;
    private const int PositiveImageSize = GraphicsLayouts.GfxBaseDefaultMonitor + sizeof(uint);
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback, out var audit);
        return new NativeImage(code, entries, fallback, audit.LocalFallbackOffsets.Append(fallback).ToHashSet());
    });
    private static readonly Lazy<NativeGraphicsLibraryHunkImage> Hunk = new(() =>
        NativeGraphicsLibraryHunkBuilder.Build(
            Image.Value.Code,
            Image.Value.Entries,
            Image.Value.Fallback,
            PositiveImageSize));

    public static IEnumerable<object[]> NullCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[] { "library-base", "null", "odd", "wrapped" })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    [Theory]
    [MemberData(nameof(NullCases))]
    public void CloseMonitorNullIsReadFreeBeforeGraphicsBaseAdmission(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        using var fixture = new Fixture(relocated, autoInitEntry);
        var graphicsBase = scenario switch
        {
            "library-base" => fixture.GraphicsBase,
            "null" => 0u,
            "odd" => 3u,
            "wrapped" => 0xFFFF_FFFEu,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var publicVector = scenario == "library-base";
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(0, graphicsBase, publicVector);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        Check(failures, "NULL result and native provenance", () =>
        {
            Assert.Equal(0u, result.Data0);
            Assert.False(result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
        });
        CheckMemoryUnchanged(failures, before, after, "NULL close is read-free");
        Check(failures, "NULL caller PC/SP, A0, and A6 return intact", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(fixture.ExpectedStackPointer(publicVector), result.StackPointer);
            Assert.Equal(0u, result.Address0);
            Assert.Equal(graphicsBase, result.Address6);
        });
        Check(failures, "NULL close preserves D2-D7/A2-A6", () =>
            Assert.True(result.RegisterDifferences.Length == 0,
                string.Join("; ", result.RegisterDifferences)));

        Assert.True(failures.Count == 0,
            $"{fixture.DescribeRoute(publicVector)}, {scenario}:\n" + string.Join("\n", failures));
    }

    public static IEnumerable<object[]> GraphicsBaseAdmissionCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "ordinary",
            "before-last-prefix",
            "last-prefix",
            "first-even",
            "null",
            "odd",
            "prefix-wrap",
            "last-even",
            "last-odd"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    [Theory]
    [MemberData(nameof(GraphicsBaseAdmissionCases))]
    public void CloseMonitorGuardsTheCompleteDefaultMonitorFieldBeforeCalleeSavedScratch(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        using var fixture = new Fixture(relocated, autoInitEntry);
        var graphicsBase = fixture.GraphicsBase;
        var success = true;
        var lastPrefix = uint.MaxValue - (uint)(GraphicsLayouts.GfxBaseDefaultMonitor + 3);
        switch (scenario)
        {
            case "ordinary":
                break;
            case "before-last-prefix":
                graphicsBase = lastPrefix - 2u;
                break;
            case "last-prefix":
                graphicsBase = lastPrefix;
                break;
            case "first-even":
                graphicsBase = 0x0000_0002u;
                break;
            case "null":
                graphicsBase = 0;
                success = false;
                break;
            case "odd":
                graphicsBase = 3;
                success = false;
                break;
            case "prefix-wrap":
                graphicsBase = lastPrefix + 2u;
                success = false;
                break;
            case "last-even":
                graphicsBase = 0xFFFF_FFFEu;
                success = false;
                break;
            case "last-odd":
                graphicsBase = 0xFFFF_FFFFu;
                success = false;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "last-prefix")
            Assert.Equal(
                (ulong)uint.MaxValue,
                (ulong)graphicsBase + (uint)GraphicsLayouts.GfxBaseDefaultMonitor + 3u);
        if (scenario == "prefix-wrap")
            Assert.True(
                (ulong)graphicsBase + (uint)GraphicsLayouts.GfxBaseDefaultMonitor + 3u > uint.MaxValue);

        fixture.SeedDefaultMonitor(graphicsBase, MonitorAddress);
        var before = fixture.CaptureMemory();
        var expectedMonitor = before.Monitor.ToArray();
        if (success)
            BinaryPrimitives.WriteUInt16BigEndian(
                expectedMonitor.AsSpan(GraphicsLayouts.MonitorSpecOpenCount),
                checked((ushort)(InitialOpenCount - 1)));

        var result = fixture.Invoke(MonitorAddress, graphicsBase, publicVector: false);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        Check(failures, "result and native/fallback provenance", () =>
        {
            Assert.Equal(success ? 0u : CapturedD0, result.Data0);
            Assert.Equal(!success, result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "only an admitted resident close decrements OpenCount", () =>
            Assert.Equal(expectedMonitor, after.Monitor));
        Check(failures, "non-monitor memory remains unchanged", () =>
        {
            Assert.Equal(before.High, after.High);
            Assert.Equal(before.Low, after.Low);
            Assert.Equal(before.GraphicsImage, after.GraphicsImage);
            Assert.Equal(before.Resident, after.Resident);
            Assert.Equal(before.Caller, after.Caller);
            Assert.Equal(before.StackGuards, after.StackGuards);
        });
        Check(failures, "caller PC/SP, A0, and A6 return intact", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(fixture.ExpectedStackPointer(publicVector: false), result.StackPointer);
            Assert.Equal(MonitorAddress, result.Address0);
            Assert.Equal(graphicsBase, result.Address6);
        });
        if (success)
            Check(failures, "admitted close preserves D2-D7/A2-A6", () =>
                Assert.True(result.RegisterDifferences.Length == 0,
                    string.Join("; ", result.RegisterDifferences)));
        else
            Check(failures, "pre-admission decline preserves D2-D7/A2-A6", () =>
                Assert.True(result.RegisterDifferences.Length == 0,
                    string.Join("; ", result.RegisterDifferences)));

        Assert.True(failures.Count == 0,
            $"{fixture.DescribeRoute(publicVector: false)}, {scenario}:\n" + string.Join("\n", failures));
    }

    public static IEnumerable<object[]> DefaultMonitorAdmissionCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "ordinary",
            "before-last-envelope",
            "last-envelope",
            "address-two",
            "null",
            "odd",
            "first-wrapped-envelope",
            "last-even",
            "last-odd"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    [Theory]
    [MemberData(nameof(DefaultMonitorAdmissionCases))]
    public void CloseMonitorGuardsTheCompleteSelectedMonitorSpecBeforeCalleeSavedScratch(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        using var fixture = new Fixture(relocated, autoInitEntry);
        var maximumMonitor = uint.MaxValue - (uint)(GraphicsLayouts.MonitorSpecSize - 1);
        var selectedMonitor = MonitorAddress;
        var admitted = true;
        switch (scenario)
        {
            case "ordinary":
                break;
            case "before-last-envelope":
                selectedMonitor = maximumMonitor - 2u;
                break;
            case "last-envelope":
                selectedMonitor = maximumMonitor;
                break;
            case "address-two":
                selectedMonitor = 2u;
                break;
            case "null":
                selectedMonitor = 0;
                admitted = false;
                break;
            case "odd":
                selectedMonitor = MonitorAddress + 1u;
                admitted = false;
                break;
            case "first-wrapped-envelope":
                selectedMonitor = maximumMonitor + 2u;
                admitted = false;
                break;
            case "last-even":
                selectedMonitor = 0xFFFF_FFFEu;
                admitted = false;
                break;
            case "last-odd":
                selectedMonitor = 0xFFFF_FFFFu;
                admitted = false;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "last-envelope")
            Assert.Equal(
                (ulong)uint.MaxValue,
                (ulong)selectedMonitor + (uint)GraphicsLayouts.MonitorSpecSize - 1u);
        if (scenario == "first-wrapped-envelope")
            Assert.True(
                (ulong)selectedMonitor + (uint)GraphicsLayouts.MonitorSpecSize - 1u > uint.MaxValue);

        fixture.SeedDefaultMonitor(fixture.GraphicsBase, selectedMonitor);
        if (admitted)
            fixture.SeedMonitor(selectedMonitor);

        var inputMonitor = selectedMonitor == 0 ? MonitorAddress : selectedMonitor;
        var before = fixture.CaptureMemory();
        var expectedMonitor = before.Monitor.ToArray();
        var expectedHigh = before.High.ToArray();
        var expectedLow = before.Low.ToArray();
        if (admitted)
        {
            var physicalOpenCount =
                ((selectedMonitor & 0x00FF_FFFFu) +
                 (uint)GraphicsLayouts.MonitorSpecOpenCount) & 0x00FF_FFFFu;
            var expectedOpenCount = checked((ushort)(InitialOpenCount - 1));
            if (physicalOpenCount >= MonitorAddress &&
                physicalOpenCount + 1u < MonitorAddress + (uint)expectedMonitor.Length)
                BinaryPrimitives.WriteUInt16BigEndian(
                    expectedMonitor.AsSpan(checked((int)(physicalOpenCount - MonitorAddress))),
                    expectedOpenCount);
            else if (physicalOpenCount >= HighPhysicalAddress &&
                     physicalOpenCount + 1u < HighPhysicalAddress + (uint)expectedHigh.Length)
                BinaryPrimitives.WriteUInt16BigEndian(
                    expectedHigh.AsSpan(checked((int)(physicalOpenCount - HighPhysicalAddress))),
                    expectedOpenCount);
            else
            {
                Assert.True(physicalOpenCount + 1u < (uint)expectedLow.Length);
                BinaryPrimitives.WriteUInt16BigEndian(
                    expectedLow.AsSpan(checked((int)physicalOpenCount)),
                    expectedOpenCount);
            }
        }

        var result = fixture.Invoke(inputMonitor, fixture.GraphicsBase, publicVector: false);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        Check(failures, "result and native/fallback provenance", () =>
        {
            Assert.Equal(admitted ? 0u : CapturedD0, result.Data0);
            Assert.Equal(!admitted, result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "only an admitted complete MonitorSpec decrements OpenCount", () =>
        {
            Assert.Equal(expectedMonitor, after.Monitor);
            Assert.Equal(expectedHigh, after.High);
            Assert.Equal(expectedLow, after.Low);
        });
        Check(failures, "non-monitor memory remains unchanged", () =>
        {
            Assert.Equal(before.GraphicsImage, after.GraphicsImage);
            Assert.Equal(before.Resident, after.Resident);
            Assert.Equal(before.Caller, after.Caller);
            Assert.Equal(before.StackGuards, after.StackGuards);
        });
        Check(failures, "caller PC/SP, A0, and A6 return intact", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(fixture.ExpectedStackPointer(publicVector: false), result.StackPointer);
            Assert.Equal(inputMonitor, result.Address0);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
        });
        if (admitted)
            Check(failures, "admitted close preserves D2-D7/A2-A6", () =>
                Assert.True(result.RegisterDifferences.Length == 0,
                    string.Join("; ", result.RegisterDifferences)));
        else
            Check(failures, "pre-admission decline preserves D2-D7/A2-A6", () =>
                Assert.True(result.RegisterDifferences.Length == 0,
                    string.Join("; ", result.RegisterDifferences)));

        Assert.True(failures.Count == 0,
            $"{fixture.DescribeRoute(publicVector: false)}, {scenario}:\n" + string.Join("\n", failures));
    }

    public static IEnumerable<object[]> LateFrameCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[] { "foreign-monitor", "zero-open-count" })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    [Theory]
    [MemberData(nameof(LateFrameCases))]
    public void CloseMonitorPublicFramePreservesCalleeSavedRegistersAfterAdmission(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        using var fixture = new Fixture(relocated, autoInitEntry);
        fixture.SeedDefaultMonitor(fixture.GraphicsBase, MonitorAddress);
        var inputMonitor = MonitorAddress;
        switch (scenario)
        {
            case "foreign-monitor":
                fixture.SeedMonitorOpenCount(MonitorAddress, InitialOpenCount);
                inputMonitor = ForeignMonitorAddress;
                break;
            case "zero-open-count":
                fixture.SeedMonitorOpenCount(MonitorAddress, 0);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(inputMonitor, fixture.GraphicsBase, publicVector: false);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        Check(failures, "late result and native/fallback provenance", () =>
        {
            Assert.Equal(CapturedD0, result.Data0);
            Assert.True(result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
        });
        CheckMemoryUnchanged(failures, before, after,
            "late identity/count decline publishes no memory");
        Check(failures, "late result preserves A0, caller PC/SP, and A6", () =>
        {
            Assert.Equal(inputMonitor, result.Address0);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(fixture.ExpectedStackPointer(publicVector: false), result.StackPointer);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
        });
        Check(failures, "late result preserves D2-D7/A2-A6", () =>
            Assert.True(result.RegisterDifferences.Length == 0,
                string.Join("; ", result.RegisterDifferences)));

        Assert.True(failures.Count == 0,
            $"{fixture.DescribeRoute(publicVector: false)}, {scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CloseAssociatedNonDefaultReferencesWithoutAdoptingOrFreeingThem(bool relocated, bool autoInitEntry)
    {
        using var fixture = new Fixture(relocated, autoInitEntry);
        foreach (var scenario in new[] { "linked", "unlinked", "null-default", "maximum-count", "zero-count", "type", "kind", "backlink" })
        {
            fixture.SeedAssociatedMonitor(scenario);
            var admitted = scenario is "linked" or "unlinked" or "null-default" or "maximum-count";
            var before = fixture.CaptureMemory();
            var foreignBefore = fixture.ReadAssociatedMonitor();
            var result = fixture.Invoke(ForeignMonitorAddress, fixture.GraphicsBase, publicVector: true);
            var after = fixture.CaptureMemory();
            var expected = foreignBefore.ToArray();
            if (admitted)
                BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(GraphicsLayouts.MonitorSpecOpenCount),
                    scenario == "maximum-count" ? (ushort)0xFFFE : (ushort)(InitialOpenCount - 1));
            Assert.Equal(admitted ? 0u : CapturedD0, result.Data0);
            Assert.Equal(!admitted, result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(ForeignMonitorAddress, result.Address0);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(fixture.ExpectedStackPointer(true), result.StackPointer);
            Assert.Equal(expected, fixture.ReadAssociatedMonitor());
            var failures = new List<string>();
            CheckMemoryUnchanged(failures, before, after, scenario);
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }
    }

    private static void CheckMemoryUnchanged(
        List<string> failures,
        MemorySnapshot before,
        MemorySnapshot after,
        string label)
        => Check(failures, label, () =>
        {
            Assert.Equal(before.Monitor, after.Monitor);
            Assert.Equal(before.High, after.High);
            Assert.Equal(before.Low, after.Low);
            Assert.Equal(before.GraphicsImage, after.GraphicsImage);
            Assert.Equal(before.Resident, after.Resident);
            Assert.Equal(before.Caller, after.Caller);
            Assert.Equal(before.StackGuards, after.StackGuards);
        });

    private static void Check(List<string> failures, string label, Action assertion)
    {
        try { assertion(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failures.Add(label + ": " + exception.Message);
        }
    }

    private sealed record NativeImage(
        byte[] Code,
        IReadOnlyDictionary<GraphicsLvo, int> Entries,
        int Fallback,
        IReadOnlySet<int> FallbackOffsets);

    private sealed record MemorySnapshot(
        byte[] Monitor,
        byte[] High,
        byte[] Low,
        byte[] GraphicsImage,
        byte[] Resident,
        byte[] Caller,
        byte[] StackGuards);

    private sealed record CallResult(
        uint Data0,
        uint Address0,
        uint Address6,
        uint ProgramCounter,
        uint StackPointer,
        bool UsedFallback,
        int NativeReturnCount,
        string[] RegisterDifferences);

    private sealed class Fixture : IDisposable
    {
        private const uint FixedCodeAddress = 0x0040_0000;
        private const uint FixedGraphicsBase = 0x0070_0000;
        private const uint FixedResidentAddress = 0x0072_0000;
        private const uint HunkSegmentAddress = 0x0080_0000;
        private const uint HunkResidentAddress = 0x00A8_0000;
        private const uint CallerAddress = 0x00B8_0000;
        private const uint StackAddress = 0x00C7_0000;
        private const uint StackPointer = StackAddress + 0x300;
        private static readonly uint[] DataCanaries =
        {
            0xD2D2_0202, 0xD3D3_0303, 0xD4D4_0404,
            0xD5D5_0505, 0xD6D6_0606, 0xD7D7_0707
        };
        private static readonly uint[] AddressCanaries =
        {
            0xA2A2_0202, 0xA3A3_0303, 0xA4A4_0404, 0xA5A5_0505
        };
        private readonly AmigaBus _bus = new();
        private readonly IM68kCore _cpu;
        private readonly bool _autoInitEntry;
        private readonly bool _relocated;
        private readonly uint _nativeCodeAddress;
        private readonly uint _entry;
        private readonly uint _vectorSlot;
        private readonly uint _functionEntry;
        private readonly uint _residentAddress;
        private readonly int _residentByteCount;

        internal Fixture(bool relocated, bool autoInitEntry)
        {
            Assert.Equal(CloseMonitorLvo, (int)GraphicsLvo.CloseMonitor);
            Assert.Equal(CloseMonitorFunctionOrdinal, (-CloseMonitorLvo / 6) - 1);
            _relocated = relocated;
            _autoInitEntry = autoInitEntry;

            if (relocated)
            {
                var residentByteCount = 0;
                var addresses = new Queue<uint>(new[] { HunkSegmentAddress, HunkResidentAddress });
                var loader = new AmigaHunkProgramLoader(_bus, size =>
                {
                    var address = addresses.Dequeue();
                    var limit = address == HunkSegmentAddress ? HunkResidentAddress : CallerAddress;
                    Assert.True((ulong)address + (uint)size <= limit, "HUNK fixture segments overlap.");
                    _bus.MapWritableMemory(address, new byte[size]);
                    if (address == HunkResidentAddress) residentByteCount = size;
                    return address;
                });
                var program = loader.Load(Hunk.Value.Bytes);
                Assert.Empty(addresses);
                Assert.True(residentByteCount > 0);
                GraphicsBase = program.SegmentBases[0] + (uint)Hunk.Value.VectorOffset;
                _nativeCodeAddress = program.SegmentBases[0] + (uint)Hunk.Value.NativeCodeOffset;
                _residentAddress = program.SegmentBases[1];
                _residentByteCount = residentByteCount;
            }
            else
            {
                var entries = Image.Value.Entries.ToDictionary(
                    item => item.Key,
                    item => FixedCodeAddress + (uint)item.Value);
                var fallback = FixedCodeAddress + (uint)Image.Value.Fallback;
                var library = NativeGraphicsLibraryImageBuilder.Build(
                    FixedGraphicsBase,
                    FixedResidentAddress,
                    fallback,
                    entries,
                    fallback,
                    PositiveImageSize);
                Assert.True((ulong)FixedCodeAddress + (uint)Image.Value.Code.Length <= library.VectorBase);
                _bus.MapWritableMemory(FixedCodeAddress, Image.Value.Code);
                _bus.MapWritableMemory(library.VectorBase, library.VectorBytes);
                _bus.MapWritableMemory(library.LibraryBase, library.PositiveBytes);
                _bus.MapWritableMemory(library.ResidentAddress, library.ResidentBytes);
                GraphicsBase = library.LibraryBase;
                _nativeCodeAddress = FixedCodeAddress;
                _residentAddress = library.ResidentAddress;
                _residentByteCount = library.ResidentBytes.Length;
            }

            _entry = _nativeCodeAddress + (uint)Image.Value.Entries[GraphicsLvo.CloseMonitor];
            _vectorSlot = checked((uint)((long)GraphicsBase + CloseMonitorLvo));
            Assert.Equal((ushort)0x4EF9, _bus.ReadWord(_vectorSlot));
            Assert.Equal(_entry, _bus.ReadLong(_vectorSlot + 2));
            Assert.Equal((ushort)0x4AFC, _bus.ReadWord(_residentAddress));
            var functionArray = _bus.ReadLong(_bus.ReadLong(_residentAddress + 0x16) + 4);
            _functionEntry = _bus.ReadLong(functionArray + CloseMonitorFunctionOrdinal * 4u);
            Assert.Equal(_entry, _functionEntry);

            _bus.MapWritableMemory(MonitorAddress, Enumerable.Repeat((byte)0x83, 0x100).ToArray());
            _bus.MapWritableMemory(HighPhysicalAddress, Enumerable.Repeat((byte)0xB7, 0x100).ToArray());
            for (var offset = 0; offset < 0x400; offset++)
                _bus.WriteByte((uint)offset, 0xC3, 0);
            _bus.MapWritableMemory(StackAddress, Enumerable.Repeat((byte)0x5A, 0x600).ToArray());
            _bus.MapWritableMemory(CallerAddress, Enumerable.Repeat((byte)0x91, 8).ToArray());
            ReturnAddress = CallerAddress + (autoInitEntry ? 2u : 4u);
            if (autoInitEntry)
                _bus.WriteWord(CallerAddress, 0x4E94); // JSR(A4), allowing a malformed A6.
            else
            {
                _bus.WriteWord(CallerAddress, 0x4EAE); // Real negative-vector JSR d16(A6).
                _bus.WriteWord(CallerAddress + 2, unchecked((ushort)CloseMonitorLvo));
            }
            _bus.WriteWord(ReturnAddress, 0x4E71);

            SeedMonitor(MonitorAddress);
            _bus.WriteLong(GraphicsBase + (uint)GraphicsLayouts.GfxBaseDefaultMonitor, MonitorAddress);
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, _bus);
        }

        internal uint GraphicsBase { get; }
        internal uint ReturnAddress { get; }

        internal string DescribeRoute(bool publicVector)
            => "CloseMonitor/" + (_relocated ? "relocated HUNK" : "fixed image") + "/" +
               (_autoInitEntry ? "AUTOINIT JSR(A4)" : publicVector ? "public JSR d16(A6)" : "direct native body");

        internal uint ExpectedStackPointer(bool publicVector)
            => _autoInitEntry || publicVector ? StackPointer : StackPointer - 4u + 4u;

        internal void SeedDefaultMonitor(uint graphicsBase, uint monitor)
            => SeedLong(graphicsBase, GraphicsLayouts.GfxBaseDefaultMonitor, monitor);

        internal void SeedMonitor(uint pointer)
            => SeedWord(pointer, GraphicsLayouts.MonitorSpecOpenCount, InitialOpenCount);

        internal void SeedMonitorOpenCount(uint pointer, ushort openCount)
            => SeedWord(pointer, GraphicsLayouts.MonitorSpecOpenCount, openCount);

        internal void SeedAssociatedMonitor(string scenario)
        {
            _bus.MapWritableMemory(ForeignMonitorAddress, Enumerable.Repeat((byte)0x91, 0x100).ToArray());
            SeedDefaultMonitor(GraphicsBase, scenario == "null-default" ? 0 : MonitorAddress);
            SeedByte(ForeignMonitorAddress, GraphicsLayouts.MonitorSpecNodeType, scenario == "type" ? (byte)0 : (byte)18);
            SeedWord(ForeignMonitorAddress, GraphicsLayouts.MonitorSpecNodeSubsystem, scenario == "kind" ? (ushort)0x0203 : (ushort)0x0204);
            SeedLong(ForeignMonitorAddress, GraphicsLayouts.ExtendedNodeLibrary, scenario == "backlink" ? 0 : GraphicsBase);
            SeedMonitorOpenCount(ForeignMonitorAddress, scenario == "zero-count" ? (ushort)0 : scenario == "maximum-count" ? ushort.MaxValue : InitialOpenCount);
            var list = GraphicsBase + (uint)GraphicsLayouts.GfxBaseMonitorList;
            SeedLong(list, 0, scenario == "linked" ? ForeignMonitorAddress : list + 4);
            SeedLong(list, 4, 0);
            SeedLong(list, 8, scenario == "linked" ? ForeignMonitorAddress : list);
            SeedWord(list, 12, 0);
            SeedLong(ForeignMonitorAddress, 0, scenario == "linked" ? list + 4 : 0);
            SeedLong(ForeignMonitorAddress, 4, scenario == "linked" ? list : 0);
        }

        internal byte[] ReadAssociatedMonitor() => ReadBytes(ForeignMonitorAddress, 0x100);

        private void SeedLong(uint pointer, int fieldOffset, uint value)
        {
            for (var index = 0; index < sizeof(uint); index++)
                SeedByte(pointer, fieldOffset + index,
                    unchecked((byte)(value >> (24 - index * 8))));
        }

        private void SeedWord(uint pointer, int fieldOffset, ushort value)
        {
            SeedByte(pointer, fieldOffset, unchecked((byte)(value >> 8)));
            SeedByte(pointer, fieldOffset + 1, unchecked((byte)value));
        }

        private void SeedByte(uint pointer, int fieldOffset, byte value)
        {
            var physical = ((pointer & 0x00FF_FFFFu) + (uint)fieldOffset) & 0x00FF_FFFFu;
            _bus.WriteByte(physical, value, 0);
            Assert.Equal(value, _bus.ReadByte(physical));
        }

        private byte[] ReadBytes(uint address, int count)
            => Enumerable.Range(0, count).Select(index => _bus.ReadByte(address + (uint)index)).ToArray();

        internal MemorySnapshot CaptureMemory()
            => new(
                ReadBytes(MonitorAddress, 0x100),
                ReadBytes(HighPhysicalAddress, 0x100),
                ReadBytes(0, 0x400),
                ReadBytes(
                    GraphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize,
                    NativeGraphicsLibraryImageBuilder.VectorTableSize +
                    PositiveImageSize),
                ReadBytes(_residentAddress, _residentByteCount),
                ReadBytes(CallerAddress, 8),
                ReadBytes(StackAddress, 0x2C0).Concat(ReadBytes(StackPointer, 0x300)).ToArray());

        internal CallResult Invoke(uint monitor, uint graphicsBase, bool publicVector)
        {
            var expectedAddresses = AddressCanaries.ToArray();
            if (_autoInitEntry) expectedAddresses[2] = _functionEntry;
            var entryStackPointer = StackPointer;
            var entryProgramCounter = CallerAddress;
            if (!_autoInitEntry && !publicVector)
            {
                entryStackPointer -= 4u;
                _bus.WriteLong(entryStackPointer, ReturnAddress);
                entryProgramCounter = _entry;
            }

            _cpu.Reset(entryProgramCounter, entryStackPointer);
            Assert.Equal(M68kCpuState.ResetStatusRegister, _cpu.State.StatusRegister);
            _cpu.State.D[0] = CapturedD0;
            _cpu.State.D[1] = 0xD1D1_0101;
            _cpu.State.A[0] = monitor;
            _cpu.State.A[1] = 0xA1A1_0101;
            for (var index = 0; index < DataCanaries.Length; index++)
                _cpu.State.D[index + 2] = DataCanaries[index];
            for (var index = 0; index < expectedAddresses.Length; index++)
                _cpu.State.A[index + 2] = expectedAddresses[index];
            _cpu.State.A[6] = graphicsBase;

            if (_autoInitEntry || publicVector)
            {
                _cpu.ExecuteInstruction();
                Assert.Equal(_autoInitEntry ? _functionEntry : _vectorSlot, _cpu.State.ProgramCounter);
                Assert.Equal(StackPointer - 4, _cpu.State.A[7]);
                Assert.Equal(ReturnAddress, _bus.ReadLong(_cpu.State.A[7]));
                if (!_autoInitEntry) _cpu.ExecuteInstruction();
            }
            Assert.Equal(_entry, _cpu.State.ProgramCounter);

            var usedFallback = false;
            var nativeReturnCount = 0;
            for (var instruction = 0; instruction < 1_000; instruction++)
            {
                var pc = _cpu.State.ProgramCounter;
                Assert.True(
                    pc >= _nativeCodeAddress &&
                    (ulong)pc < (ulong)_nativeCodeAddress + (uint)Image.Value.Code.Length,
                    $"Unexpected execution address 0x{pc:X8}; no host graphics/Exec gateway is installed.");
                usedFallback |= Image.Value.FallbackOffsets.Contains(unchecked((int)(pc - _nativeCodeAddress)));
                if (_bus.ReadWord(pc) == 0x4E75)
                {
                    nativeReturnCount++;
                }
                _cpu.ExecuteInstruction();
                if (_cpu.State.ProgramCounter != ReturnAddress) continue;

                var differences = new List<string>();
                for (var index = 0; index < DataCanaries.Length; index++)
                {
                    if (_cpu.State.D[index + 2] != DataCanaries[index])
                        differences.Add($"D{index + 2} expected {DataCanaries[index]:X8}, " +
                            $"actual {_cpu.State.D[index + 2]:X8}");
                }
                for (var index = 0; index < expectedAddresses.Length; index++)
                {
                    if (_cpu.State.A[index + 2] != expectedAddresses[index])
                        differences.Add($"A{index + 2} expected {expectedAddresses[index]:X8}, " +
                            $"actual {_cpu.State.A[index + 2]:X8}");
                }
                if (_cpu.State.A[6] != graphicsBase)
                    differences.Add($"A6 expected {graphicsBase:X8}, actual {_cpu.State.A[6]:X8}");

                return new CallResult(
                    _cpu.State.D[0],
                    _cpu.State.A[0],
                    _cpu.State.A[6],
                    _cpu.State.ProgramCounter,
                    _cpu.State.A[7],
                    usedFallback,
                    nativeReturnCount,
                    differences.ToArray());
            }

            throw new InvalidOperationException(
                $"{DescribeRoute(publicVector)} did not return: PC={_cpu.State.ProgramCounter:X8}, " +
                $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
