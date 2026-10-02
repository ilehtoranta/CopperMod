using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsOpenMonitorAdmissionTests
{
    private const int OpenMonitorLvo = -714;
    private const int OpenMonitorFunctionOrdinal = 118;
    private const uint DefaultDisplayId = 0;
    private const uint MonitorAddress = 0x00D2_0000;
    private const uint NameAddress = 0x00D3_0000;
    private const uint AdditionalMonitorAddress = 0x00D4_0000;
    private const uint DatabaseAddress = 0x00D5_0000;
    private const uint HighPhysicalAddress = 0x00FF_FF00;
    private const uint Address0Canary = 0xA0A0_0101;
    private const ushort InitialOpenCount = 3;
    private const int PositiveImageSize = GraphicsLibraryImageLayout.NativeMonitorImageSize;
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

    public static IEnumerable<object[]> RequestAdmissionCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[] { "resident-default", "alias-4", "alias-8000", "alias-1000", "named", "other-display-id", "invalid-id" })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    [Theory]
    [MemberData(nameof(RequestAdmissionCases))]
    public void OpenMonitorRejectsUnmatchedNamesAndOtherDisplayIdsWithoutRegisterDamage(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        using var fixture = new Fixture(relocated, autoInitEntry);
        var name = 0u;
        var displayId = DefaultDisplayId;
        var admitted = true;
        switch (scenario)
        {
            case "resident-default":
                break;
            case "alias-4": displayId = 4; break;
            case "alias-8000": displayId = 0x8000; break;
            case "alias-1000": displayId = 0x1000; break;
            case "named":
                name = NameAddress;
                admitted = false;
                break;
            case "other-display-id":
                displayId = GraphicsModeIds.PalMonitor;
                admitted = false;
                break;
            case "invalid-id":
                displayId = GraphicsModeIds.Invalid;
                admitted = false;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        fixture.SeedDefaultMonitor(fixture.GraphicsBase, MonitorAddress);
        if (scenario.StartsWith("alias-", StringComparison.Ordinal)) fixture.SeedRegistration(false, "success");
        fixture.SeedMonitorOpenCount(MonitorAddress, InitialOpenCount);
        var before = fixture.CaptureMemory();
        var expectedMonitor = before.Monitor.ToArray();
        if (admitted)
            BinaryPrimitives.WriteUInt16BigEndian(
                expectedMonitor.AsSpan(GraphicsLayouts.MonitorSpecOpenCount),
                checked((ushort)(InitialOpenCount + 1)));

        var result = fixture.Invoke(
            name,
            displayId,
            fixture.GraphicsBase,
            publicVector: true);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        Check(failures, "request result and native/fallback provenance", () =>
        {
            Assert.Equal(admitted ? MonitorAddress : scenario is "invalid-id" or "named" ? 0 : displayId, result.Data0);
            Assert.Equal(!admitted && scenario is not "invalid-id" and not "named", result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "only the resident-default spelling increments OpenCount", () =>
            Assert.Equal(expectedMonitor, after.Monitor));
        Check(failures, "all non-monitor memory remains unchanged", () =>
        {
            Assert.Equal(before.Name, after.Name);
            Assert.Equal(before.High, after.High);
            Assert.Equal(before.Low, after.Low);
            Assert.Equal(before.GraphicsImage, after.GraphicsImage);
            Assert.Equal(before.Resident, after.Resident);
            Assert.Equal(before.Caller, after.Caller);
            Assert.Equal(before.StackGuards, after.StackGuards);
        });
        Check(failures, "request caller PC/SP, A1, and A6 return intact", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(name, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
            Assert.Equal(admitted ? MonitorAddress : Address0Canary, result.Address0);
        });
        if (admitted)
            Check(failures, "admitted request preserves D2-D7/A2-A6", () =>
                Assert.True(result.RegisterDifferences.Length == 0,
                    string.Join("; ", result.RegisterDifferences)));
        else
            Check(failures, "request decline preserves D2-D7/A2-A6", () =>
                Assert.True(result.RegisterDifferences.Length == 0,
                    string.Join("; ", result.RegisterDifferences)));

        Assert.True(failures.Count == 0,
            $"{fixture.DescribeRoute(publicVector: true)}, {scenario}:\n" +
            string.Join("\n", failures));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DefaultIdAliasesPreserveAdmissionAndSaturationFallback(bool relocated, bool autoInitEntry)
    {
        using var fixture = new Fixture(relocated, autoInitEntry);
        fixture.SeedResidentNamePointer(MonitorAddress + 0xA0);
        fixture.SeedName(MonitorAddress + 0xA0, "pal.monitor");
        fixture.SeedName(NameAddress, "");
        foreach (var id in new uint[] { 4, 0x8000, 0x1000 })
        foreach (var scenario in new[] { "null-base", "odd-base", "wrapping-base", "null-monitor", "odd-monitor", "wrapping-monitor", "saturated", "name-precedence" })
        {
            fixture.SeedRegistration(false, "success");
            var graphicsBase = scenario switch
            {
                "null-base" => 0u,
                "odd-base" => 3u,
                "wrapping-base" => 0xFFFFFFFEu,
                _ => fixture.GraphicsBase
            };
            fixture.SeedRegisteredMonitor(false, scenario switch
            {
                "null-monitor" => 0u,
                "odd-monitor" => MonitorAddress + 1,
                "wrapping-monitor" => 0xFFFFFFFEu,
                _ => MonitorAddress
            });
            fixture.SeedMonitorOpenCount(MonitorAddress, scenario == "saturated" ? ushort.MaxValue : InitialOpenCount);
            // An empty supplied name must not select by the otherwise valid ID.
            var name = scenario == "name-precedence" ? NameAddress : 0u;
            var before = fixture.CaptureMemory();
            // A malformed A6 cannot locate its own negative vector; exercise
            // direct body admission there, or the independent AUTOINIT entry.
            var result = fixture.Invoke(name, id, graphicsBase, publicVector: graphicsBase == fixture.GraphicsBase);
            var after = fixture.CaptureMemory();
            Assert.Equal(scenario is "name-precedence" or "null-monitor" ? 0 : id, result.Data0);
            Assert.Equal(scenario is not "name-precedence" and not "null-monitor", result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(name, result.Address1);
            Assert.Equal(graphicsBase, result.Address6);
            Assert.Equal(scenario == "saturated" ? MonitorAddress : Address0Canary, result.Address0);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(before.Monitor, after.Monitor);
            Assert.Equal(before.Name, after.Name);
            Assert.Equal(before.High, after.High);
            Assert.Equal(before.Low, after.Low);
            Assert.Equal(before.GraphicsImage, after.GraphicsImage);
            Assert.Equal(before.Resident, after.Resident);
            Assert.Equal(before.Caller, after.Caller);
            Assert.Equal(before.StackGuards, after.StackGuards);
        }
    }

    public static IEnumerable<object[]> RegisteredIdCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var ntsc in new[] { false, true })
        foreach (var scenario in new[] { "success", "renamed", "flags", "foreign-cmmo", "tag", "version", "legacy",
            "owner", "allocation", "allocation-wrap", "size", "id", "backlink", "short-base", "base-wrap", "saturated" })
            yield return new object[] { relocated, autoInitEntry, ntsc, scenario };
    }

    [Theory]
    [MemberData(nameof(RegisteredIdCases))]
    public void ExplicitFamilyUsesRegistrationNotMutableNameOrTiming(bool relocated, bool autoInitEntry, bool ntsc, string scenario)
    {
        using var fixture = new Fixture(relocated, autoInitEntry);
        fixture.SeedRegistration(ntsc, scenario);
        var graphicsBase = scenario == "base-wrap" ? 0xFFFFFE00u : fixture.GraphicsBase;
        if (scenario == "base-wrap") fixture.SeedDefaultMonitor(graphicsBase, MonitorAddress);
        var admitted = scenario is "success" or "renamed" or "flags" or "foreign-cmmo";
        foreach (var key in new uint[] { 0, 4, 0x8000, 0x8004, 0x8020, 0x8024, 0x800, 0x804,
            0x80, 0x84, 0x400, 0x404, 0x8400, 0x8404, 0x8420, 0x8424, 0x440, 0x444,
            0x8440, 0x8444, 0x8460, 0x8464 })
        {
            fixture.SeedMonitorOpenCount(MonitorAddress, scenario == "saturated" ? ushort.MaxValue : InitialOpenCount);
            var id = (ntsc ? 0x11000u : 0x21000u) | key;
            AssertCall(id, admitted);
            // Matching syntax alone must never acquire the opposite resident.
            AssertCall((ntsc ? 0x21000u : 0x11000u) | key, false);
            // The original-ROM matrix requires the explicit-family marker.
            AssertCall(id & ~0x1000u, false);
            if (scenario == "success")
            {
                AssertCall(key, true);
                AssertCall(key | 0x1000u, true);
            }
        }

        void AssertCall(uint id, bool accepted)
        {
            var before = fixture.CaptureMemory();
            var result = fixture.Invoke(0, id, graphicsBase, publicVector: graphicsBase == fixture.GraphicsBase);
            var after = fixture.CaptureMemory();
            var expected = before.Monitor.ToArray();
            if (accepted)
                BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(GraphicsLayouts.MonitorSpecOpenCount),
                    (ushort)(BinaryPrimitives.ReadUInt16BigEndian(expected.AsSpan(GraphicsLayouts.MonitorSpecOpenCount)) + 1));
            var validDatabase = admitted || scenario is "backlink" or "allocation-wrap" or "saturated";
            var absent = validDatabase && (id & 0xFFFF1000) == (ntsc ? 0x21000u : 0x11000u);
            Assert.Equal(accepted ? MonitorAddress : absent ? 0 : id, result.Data0);
            Assert.Equal(!accepted && !absent, result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(0u, result.Address1);
            Assert.Equal(graphicsBase, result.Address6);
            var reachedCount = accepted || (scenario == "saturated" && (id & 0xFFFF1000) == (ntsc ? 0x11000u : 0x21000u));
            Assert.Equal(reachedCount ? MonitorAddress : Address0Canary, result.Address0);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(expected, after.Monitor);
            Assert.Equal(before.Name, after.Name);
            Assert.Equal(before.High, after.High);
            Assert.Equal(before.Low, after.Low);
            Assert.Equal(before.GraphicsImage, after.GraphicsImage);
            Assert.Equal(before.Resident, after.Resident);
            Assert.Equal(before.Caller, after.Caller);
            Assert.Equal(before.StackGuards, after.StackGuards);
            Assert.Equal(before.Database, after.Database);
        }
    }

    public static IEnumerable<object[]> DatabaseSelectionCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var ntsc in new[] { false, true })
        foreach (var scenario in new[] { "borrowed", "default-null", "default-odd", "default-wrap",
            "empty", "opposite", "changed-default", "bad-magic", "bad-version", "bad-size",
            "bad-ntsc-record", "bad-pal-record", "bad-default", "null-db", "odd-db", "word-db", "wrap-db",
            "public-pointer", "bad-node-type", "bad-node-kind", "bad-backlink" })
            yield return new object[] { relocated, autoInitEntry, ntsc, scenario };
    }

    [Theory]
    [MemberData(nameof(DatabaseSelectionCases))]
    public void NonzeroIdsUseDatabaseIndependentlyOfDefaultListAndNodeOwnership(
        bool relocated, bool autoInitEntry, bool ntsc, string scenario)
    {
        using var fixture = new Fixture(relocated, autoInitEntry);
        fixture.SeedRegistration(ntsc, "foreign-cmmo");
        fixture.MapAdditionalNodes(1);
        fixture.SeedList(AdditionalMonitorAddress);
        fixture.SeedList(); // selected node is borrowed and unlinked, not the default
        fixture.SeedLong(AdditionalMonitorAddress, 0, 0xDEADBEEF);
        fixture.SeedLong(AdditionalMonitorAddress, 4, 0xDEADBEEF);
        fixture.SeedLong(AdditionalMonitorAddress, GraphicsLayouts.MonitorSpecNodeName, 0xFFFFFFFF);
        fixture.SeedWord(AdditionalMonitorAddress, GraphicsLayouts.MonitorSpecFlags, 0);
        fixture.SeedRegisteredMonitor(ntsc, scenario is "empty" or "opposite" or "changed-default" ? 0 : AdditionalMonitorAddress);
        fixture.SeedRegisteredMonitor(!ntsc, scenario is "opposite" or "changed-default" ? AdditionalMonitorAddress : 0);
        if (scenario == "changed-default") fixture.SeedLong(DatabaseAddress,
            GraphicsDisplayDatabase.NativeDefaultMonitorIdOffset, ntsc ? 0x21000u : 0x11000u);
        if (scenario.StartsWith("default-", StringComparison.Ordinal))
            fixture.SeedDefaultMonitor(fixture.GraphicsBase, scenario switch
            { "default-null" => 0, "default-odd" => MonitorAddress + 1, _ => 0xFFFFFFFE });
        var badOffset = scenario switch { "bad-magic" => 0, "bad-version" => 4, "bad-size" => 8,
            "bad-ntsc-record" => GraphicsDisplayDatabase.NativeMonitorPositionsOffset,
            "bad-pal-record" => GraphicsDisplayDatabase.NativeMonitorPositionsOffset + 12,
            "bad-default" => GraphicsDisplayDatabase.NativeDefaultMonitorIdOffset, _ => -1 };
        if (badOffset >= 0) fixture.SeedLong(DatabaseAddress, badOffset, 0xBAD0BAD0);
        if (scenario is "null-db" or "odd-db" or "word-db" or "wrap-db")
        {
            var pointer = scenario switch { "null-db" => 0u, "odd-db" => DatabaseAddress + 1,
                "word-db" => DatabaseAddress + 2, _ => 0xFFFFFFFCu };
            fixture.SeedLong(fixture.GraphicsBase, 0x25C, pointer);
            fixture.SeedLong(fixture.GraphicsBase, GraphicsLayouts.GfxBaseDisplayInfoDataBase, pointer);
        }
        if (scenario == "public-pointer") fixture.SeedLong(fixture.GraphicsBase, GraphicsLayouts.GfxBaseDisplayInfoDataBase, DatabaseAddress + 4);
        if (scenario == "bad-node-type") fixture.SeedWord(AdditionalMonitorAddress, GraphicsLayouts.MonitorSpecNodeType, 0);
        if (scenario == "bad-node-kind") fixture.SeedWord(AdditionalMonitorAddress, GraphicsLayouts.MonitorSpecNodeSubsystem, 0);
        if (scenario == "bad-backlink") fixture.SeedLong(AdditionalMonitorAddress, GraphicsLayouts.ExtendedNodeLibrary, 0);
        foreach (var key in new uint[] { 0, 4, 0x8000, 0x8004, 0x8020, 0x8024, 0x800, 0x804,
            0x80, 0x84, 0x400, 0x404, 0x8400, 0x8404, 0x8420, 0x8424, 0x440, 0x444,
            0x8440, 0x8444, 0x8460, 0x8464 })
        foreach (var id in new[] { key, key | 0x1000u, key | 0x11000u, key | 0x21000u })
        {
            fixture.SeedMonitorOpenCount(MonitorAddress, InitialOpenCount);
            fixture.SeedMonitorOpenCount(AdditionalMonitorAddress, InitialOpenCount);
            var before = fixture.CaptureMemory();
            var expectedDefault = before.Monitor.ToArray();
            var expectedBorrowed = fixture.ReadAdditionalNodes(1);
            var invalidDatabase = badOffset >= 0 || scenario is "null-db" or "odd-db" or "word-db" or "wrap-db" or "public-pointer";
            var family = id >> 16;
            if (family == 0) family = (scenario == "changed-default" ? !ntsc : ntsc) ? 1u : 2u;
            var selectedFamily = (scenario is "opposite" or "changed-default" ? !ntsc : ntsc) ? 1u : 2u;
            var absent = scenario == "empty" || family != selectedFamily;
            var declined = id == 0 ? scenario.StartsWith("default-", StringComparison.Ordinal)
                : invalidDatabase || !absent && scenario is "bad-node-type" or "bad-node-kind" or "bad-backlink";
            var expectedResult = declined ? id : id == 0 ? MonitorAddress : absent ? 0 : AdditionalMonitorAddress;
            if (!declined && expectedResult != 0)
                BinaryPrimitives.WriteUInt16BigEndian((expectedResult == MonitorAddress ? expectedDefault : expectedBorrowed)
                    .AsSpan(GraphicsLayouts.MonitorSpecOpenCount, 2), InitialOpenCount + 1);
            var result = fixture.Invoke(0, id, fixture.GraphicsBase, publicVector: true);
            var after = fixture.CaptureMemory();
            Assert.Equal(expectedResult, result.Data0);
            Assert.Equal(declined, result.UsedFallback);
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(0u, result.Address1);
            Assert.Equal(!declined && expectedResult != 0 ? expectedResult : Address0Canary, result.Address0);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(1, result.NativeReturnCount);
            Assert.Equal(expectedDefault, after.Monitor);
            Assert.Equal(expectedBorrowed, fixture.ReadAdditionalNodes(1));
            Assert.Equal(before.Database, after.Database);
            Assert.Equal(before.GraphicsImage, after.GraphicsImage);
            Assert.Equal(before.Name, after.Name);
            Assert.Equal(before.High, after.High);
            Assert.Equal(before.Low, after.Low);
            Assert.Equal(before.Resident, after.Resident);
            Assert.Equal(before.Caller, after.Caller);
            Assert.Equal(before.StackGuards, after.StackGuards);
        }
    }

    public static IEnumerable<object[]> ListCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[] { "second", "third", "duplicate", "reverse-duplicate", "canonical-before-default",
            "alias-collision", "upper-alias", "empty-name", "no-default", "missing", "case", "null-head", "odd-head",
            "wrap-head", "tail", "type", "pad", "pred", "cycle", "kind", "backlink", "null-name", "saturated",
            "empty-list", "tailpred", "limit-last", "limit-miss", "limit-beyond" })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    [Theory]
    [MemberData(nameof(ListCases))]
    public void NamedMonitorUsesListOrderAndPreservesMalformedOrUnmatchedRequests(bool relocated, bool autoInitEntry, string scenario)
    {
        using var fixture = new Fixture(relocated, autoInitEntry);
        var nodes = scenario is "limit-last" or "limit-miss" ? 256 : scenario == "limit-beyond" ? 257 : 2;
        fixture.MapAdditionalNodes(nodes);
        var first = AdditionalMonitorAddress;
        var second = first + 0x100;
        fixture.SeedList(MonitorAddress, first, second);
        fixture.SeedName(first + 0xA0, "second.monitor");
        fixture.SeedName(second + 0xA0, "third.monitor");
        var request = "second.monitor";
        var expected = first;
        var admitted = true;
        switch (scenario)
        {
            case "second": break;
            case "third": request = "third.monitor"; expected = second; break;
            case "duplicate": fixture.SeedName(second + 0xA0, request); break;
            case "reverse-duplicate": fixture.SeedName(second + 0xA0, request); fixture.SeedList(MonitorAddress, second, first); expected = second; break;
            case "canonical-before-default": request = "pal.monitor"; fixture.SeedName(first + 0xA0, request); fixture.SeedList(first, MonitorAddress, second); break;
            case "alias-collision": request = "default.monitor"; fixture.SeedName(first + 0xA0, request); fixture.SeedList(first, MonitorAddress, second); expected = MonitorAddress; break;
            case "upper-alias": request = "DEFAULT.MONITOR"; expected = MonitorAddress; break;
            case "empty-name": request = ""; fixture.SeedName(first + 0xA0, request); break;
            case "no-default": fixture.SeedDefaultMonitor(fixture.GraphicsBase, 0); break;
            case "missing": request = "missing.monitor"; admitted = false; break;
            case "case": request = "SECOND.MONITOR"; admitted = false; break;
            case "null-head": fixture.SeedLong(fixture.GraphicsBase, 0x180, 0); admitted = false; break;
            case "odd-head": fixture.SeedLong(fixture.GraphicsBase, 0x180, first + 1); admitted = false; break;
            case "wrap-head": fixture.SeedLong(fixture.GraphicsBase, 0x180, 0xFFFFFF62); admitted = false; break;
            case "tail": fixture.SeedLong(fixture.GraphicsBase, 0x184, 1); admitted = false; break;
            case "type": fixture.SeedWord(fixture.GraphicsBase, 0x18C, 0x0100); admitted = false; break;
            case "pad": fixture.SeedWord(fixture.GraphicsBase, 0x18C, 1); admitted = false; break;
            case "pred": fixture.SeedLong(first, 4, 0); admitted = false; break;
            case "cycle": request = "missing.monitor"; fixture.SeedLong(second, 0, MonitorAddress); admitted = false; break;
            case "kind": fixture.SeedWord(first, GraphicsLayouts.MonitorSpecNodeSubsystem, 0x0203); admitted = false; break;
            case "backlink": fixture.SeedLong(first, GraphicsLayouts.ExtendedNodeLibrary, 0); admitted = false; break;
            case "null-name": fixture.SeedLong(first, GraphicsLayouts.MonitorSpecNodeName, 0); admitted = false; break;
            case "saturated": admitted = false; break;
            case "empty-list": fixture.SeedList(); admitted = false; break;
            case "tailpred": request = "missing.monitor"; fixture.SeedLong(fixture.GraphicsBase, 0x188, 0); admitted = false; break;
            case "limit-last":
            case "limit-miss":
            case "limit-beyond":
                var many = Enumerable.Range(0, nodes).Select(i => first + (uint)i * 0x100).ToArray();
                fixture.SeedList(many);
                foreach (var node in many) fixture.SeedName(node + 0xA0, "miss");
                expected = many[^1];
                if (scenario != "limit-miss") fixture.SeedName(expected + 0xA0, request);
                admitted = scenario == "limit-last";
                break;
        }
        fixture.SeedName(NameAddress, request);
        foreach (var id in new uint[] { 0xFFFFFFFF, 0xDEADBEEF })
        {
            fixture.SeedMonitorOpenCount(expected, scenario == "saturated" ? ushort.MaxValue : InitialOpenCount);
            var before = fixture.CaptureMemory();
            var extraBefore = fixture.ReadAdditionalNodes(nodes);
            var result = fixture.Invoke(NameAddress, id, fixture.GraphicsBase, publicVector: true);
            var after = fixture.CaptureMemory();
            var expectedDefault = before.Monitor.ToArray();
            var expectedExtra = extraBefore.ToArray();
            if (admitted)
                BinaryPrimitives.WriteUInt16BigEndian((expected == MonitorAddress ? expectedDefault.AsSpan(GraphicsLayouts.MonitorSpecOpenCount)
                    : expectedExtra.AsSpan((int)(expected - first) + GraphicsLayouts.MonitorSpecOpenCount)), InitialOpenCount + 1);
            var absent = scenario is "missing" or "case" or "empty-list" or "limit-miss";
            Assert.Equal(admitted ? expected : absent ? 0 : id, result.Data0);
            Assert.Equal(!admitted && !absent, result.UsedFallback);
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(1, result.NativeReturnCount);
            Assert.Equal(admitted || scenario == "saturated" ? expected : Address0Canary, result.Address0);
            Assert.Equal(NameAddress, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(expectedDefault, after.Monitor);
            Assert.Equal(expectedExtra, fixture.ReadAdditionalNodes(nodes));
            Assert.Equal(before.Name, after.Name); Assert.Equal(before.High, after.High); Assert.Equal(before.Low, after.Low);
            Assert.Equal(before.GraphicsImage, after.GraphicsImage); Assert.Equal(before.Resident, after.Resident);
            Assert.Equal(before.Caller, after.Caller); Assert.Equal(before.StackGuards, after.StackGuards);
        }
    }

    public static IEnumerable<object[]> NamedCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var ntsc in new[] { false, true })
        foreach (var scenario in new[] { "default", "upper-default", "mixed-default", "canonical",
            "upper-canonical", "mixed-canonical", "opposite", "unknown", "empty", "suffix", "prefix",
            "odd-input", "odd-resident", "last-terminator", "input-wrap", "resident-wrap",
            "unterminated", "maximum-name", "alias-null-name", "null-resident-name", "saturated" })
            yield return new object[] { relocated, autoInitEntry, ntsc, scenario };
    }

    [Theory]
    [MemberData(nameof(NamedCases))]
    public void NativeNamedDefaultMatchesRomCaseRulesAndPreservesFallbackRequests(
        bool relocated, bool autoInitEntry, bool ntsc, string scenario)
    {
        using var fixture = new Fixture(relocated, autoInitEntry);
        var canonical = ntsc ? "ntsc.monitor" : "pal.monitor";
        var input = canonical;
        var resident = canonical;
        var inputAddress = NameAddress;
        var residentAddress = MonitorAddress + 0xA0;
        var matches = true;
        switch (scenario)
        {
            case "default": input = "default.monitor"; break;
            case "upper-default": input = "DEFAULT.MONITOR"; break;
            case "mixed-default": input = "DeFaUlT.MoNiToR"; break;
            case "canonical": break;
            case "upper-canonical": input = canonical.ToUpperInvariant(); matches = false; break;
            case "mixed-canonical": input = ntsc ? "nTsc.monitor" : "pAl.monitor"; matches = false; break;
            case "opposite": input = ntsc ? "pal.monitor" : "ntsc.monitor"; matches = false; break;
            case "unknown": input = "unknown.monitor"; matches = false; break;
            case "empty": input = ""; matches = false; break;
            case "suffix": input += "x"; matches = false; break;
            case "prefix": input = input[..^1]; matches = false; break;
            case "odd-input": inputAddress++; break;
            case "odd-resident": residentAddress++; break;
            case "last-terminator": inputAddress = uint.MaxValue - (uint)input.Length; break;
            case "input-wrap": inputAddress = uint.MaxValue; matches = false; break;
            case "resident-wrap": residentAddress = uint.MaxValue; matches = false; break;
            case "unterminated": input = resident = new string('x', 64); matches = false; break;
            case "maximum-name": input = resident = new string('x', 63); break;
            case "alias-null-name": input = "DEFAULT.MONITOR"; residentAddress = 0; break;
            case "null-resident-name": residentAddress = 0; matches = false; break;
            case "saturated": break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        fixture.SeedResidentNamePointer(residentAddress);
        if (residentAddress != 0)
            fixture.SeedName(residentAddress, scenario == "resident-wrap" ? resident[..1] : resident,
                terminate: scenario is not "resident-wrap" and not "unterminated");
        fixture.SeedName(inputAddress, scenario == "input-wrap" ? input[..1] : input,
            terminate: scenario is not "input-wrap" and not "unterminated");
        foreach (var id in new[] { GraphicsModeIds.Invalid, 0xDEADBEEFu })
        {
            var count = scenario == "saturated" ? ushort.MaxValue : InitialOpenCount;
            var admitted = matches && count != ushort.MaxValue;
            fixture.SeedMonitorOpenCount(MonitorAddress, count);
            var before = fixture.CaptureMemory();
            var result = fixture.Invoke(inputAddress, id, fixture.GraphicsBase, publicVector: true);
            var after = fixture.CaptureMemory();
            var expected = before.Monitor.ToArray();
            if (admitted) BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(GraphicsLayouts.MonitorSpecOpenCount), (ushort)(count + 1));
            var absent = scenario is "upper-canonical" or "mixed-canonical" or "opposite" or "unknown" or "empty" or "suffix" or "prefix";
            Assert.Equal(admitted ? MonitorAddress : absent ? 0 : id, result.Data0);
            Assert.Equal(!admitted && !absent, result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(inputAddress, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
            Assert.Equal(matches ? MonitorAddress : Address0Canary, result.Address0);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(expected, after.Monitor);
            Assert.Equal(before.Name, after.Name);
            Assert.Equal(before.High, after.High);
            Assert.Equal(before.Low, after.Low);
            Assert.Equal(before.GraphicsImage, after.GraphicsImage);
            Assert.Equal(before.Resident, after.Resident);
            Assert.Equal(before.Caller, after.Caller);
            Assert.Equal(before.StackGuards, after.StackGuards);
        }
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
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void InvalidIdReturnsNullWithoutReadingGraphicsBaseOrChangingReferences(bool relocated, bool autoInitEntry)
    {
        using var fixture = new Fixture(relocated, autoInitEntry);
        fixture.SeedDefaultMonitor(fixture.GraphicsBase, MonitorAddress);
        fixture.SeedMonitorOpenCount(MonitorAddress, InitialOpenCount);
        foreach (var graphicsBase in new uint[] { 0, 3, 0xFFFFFFFE, 0xFFFFFFFF })
        {
            var before = fixture.CaptureMemory();
            var result = fixture.Invoke(0, GraphicsModeIds.Invalid, graphicsBase, publicVector: false);
            var after = fixture.CaptureMemory();
            Assert.Equal(0u, result.Data0);
            Assert.False(result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(Address0Canary, result.Address0);
            Assert.Equal(0u, result.Address1);
            Assert.Equal(graphicsBase, result.Address6);
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(before.Monitor, after.Monitor);
            Assert.Equal(before.GraphicsImage, after.GraphicsImage);
            Assert.Equal(before.Name, after.Name);
            Assert.Equal(before.High, after.High);
            Assert.Equal(before.Low, after.Low);
            Assert.Equal(before.Resident, after.Resident);
            Assert.Equal(before.Caller, after.Caller);
            Assert.Equal(before.StackGuards, after.StackGuards);
        }
    }

    [Theory]
    [MemberData(nameof(GraphicsBaseAdmissionCases))]
    public void OpenMonitorGuardsTheCompleteDefaultMonitorFieldBeforeCalleeSavedScratch(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        using var fixture = new Fixture(relocated, autoInitEntry);
        var graphicsBase = fixture.GraphicsBase;
        var admitted = true;
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
                graphicsBase = 2;
                break;
            case "null":
                graphicsBase = 0;
                admitted = false;
                break;
            case "odd":
                graphicsBase = 3;
                admitted = false;
                break;
            case "prefix-wrap":
                graphicsBase = lastPrefix + 2u;
                admitted = false;
                break;
            case "last-even":
                graphicsBase = 0xFFFF_FFFEu;
                admitted = false;
                break;
            case "last-odd":
                graphicsBase = 0xFFFF_FFFFu;
                admitted = false;
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
                (ulong)graphicsBase + (uint)GraphicsLayouts.GfxBaseDefaultMonitor + 3u >
                uint.MaxValue);

        fixture.SeedDefaultMonitor(graphicsBase, MonitorAddress);
        fixture.SeedMonitorOpenCount(MonitorAddress, InitialOpenCount);
        var before = fixture.CaptureMemory();
        var expectedMonitor = before.Monitor.ToArray();
        if (admitted)
            BinaryPrimitives.WriteUInt16BigEndian(
                expectedMonitor.AsSpan(GraphicsLayouts.MonitorSpecOpenCount),
                checked((ushort)(InitialOpenCount + 1)));

        var result = fixture.Invoke(
            0,
            DefaultDisplayId,
            graphicsBase,
            publicVector: false);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        Check(failures, "GfxBase result and native/fallback provenance", () =>
        {
            Assert.Equal(admitted ? MonitorAddress : DefaultDisplayId, result.Data0);
            Assert.Equal(!admitted, result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "only an admitted resident default increments OpenCount", () =>
            Assert.Equal(expectedMonitor, after.Monitor));
        Check(failures, "all non-monitor memory remains unchanged", () =>
        {
            Assert.Equal(before.Name, after.Name);
            Assert.Equal(before.High, after.High);
            Assert.Equal(before.Low, after.Low);
            Assert.Equal(before.GraphicsImage, after.GraphicsImage);
            Assert.Equal(before.Resident, after.Resident);
            Assert.Equal(before.Caller, after.Caller);
            Assert.Equal(before.StackGuards, after.StackGuards);
        });
        Check(failures, "GfxBase caller PC/SP, A1, and A6 return intact", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(0u, result.Address1);
            Assert.Equal(graphicsBase, result.Address6);
            Assert.Equal(admitted ? MonitorAddress : Address0Canary, result.Address0);
        });
        if (admitted)
            Check(failures, "admitted GfxBase path preserves D2-D7/A2-A6", () =>
                Assert.True(result.RegisterDifferences.Length == 0,
                    string.Join("; ", result.RegisterDifferences)));
        else
            Check(failures, "pre-admission decline preserves D2-D7/A2-A6", () =>
                Assert.True(result.RegisterDifferences.Length == 0,
                    string.Join("; ", result.RegisterDifferences)));

        Assert.True(failures.Count == 0,
            $"{fixture.DescribeRoute(publicVector: false)}, {scenario}:\n" +
            string.Join("\n", failures));
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
    public void OpenMonitorGuardsTheCompleteSelectedMonitorSpecBeforeCalleeSavedScratch(
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
            fixture.SeedMonitorOpenCount(selectedMonitor, InitialOpenCount);

        var before = fixture.CaptureMemory();
        var expectedMonitor = before.Monitor.ToArray();
        var expectedHigh = before.High.ToArray();
        var expectedLow = before.Low.ToArray();
        if (admitted)
        {
            var physicalOpenCount =
                ((selectedMonitor & 0x00FF_FFFFu) +
                 (uint)GraphicsLayouts.MonitorSpecOpenCount) & 0x00FF_FFFFu;
            var expectedOpenCount = checked((ushort)(InitialOpenCount + 1));
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

        var result = fixture.Invoke(
            0,
            DefaultDisplayId,
            fixture.GraphicsBase,
            publicVector: true);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        Check(failures, "selected result and native/fallback provenance", () =>
        {
            Assert.Equal(admitted ? selectedMonitor : DefaultDisplayId, result.Data0);
            Assert.Equal(!admitted, result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "only an admitted complete MonitorSpec increments OpenCount", () =>
        {
            Assert.Equal(expectedMonitor, after.Monitor);
            Assert.Equal(expectedHigh, after.High);
            Assert.Equal(expectedLow, after.Low);
        });
        Check(failures, "selected non-monitor memory remains unchanged", () =>
        {
            Assert.Equal(before.Name, after.Name);
            Assert.Equal(before.GraphicsImage, after.GraphicsImage);
            Assert.Equal(before.Resident, after.Resident);
            Assert.Equal(before.Caller, after.Caller);
            Assert.Equal(before.StackGuards, after.StackGuards);
        });
        Check(failures, "selected caller PC/SP, A1, and A6 return intact", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(0u, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
            Assert.Equal(admitted ? selectedMonitor : Address0Canary, result.Address0);
        });
        if (admitted)
            Check(failures, "admitted selected path preserves D2-D7/A2-A6", () =>
                Assert.True(result.RegisterDifferences.Length == 0,
                    string.Join("; ", result.RegisterDifferences)));
        else
            Check(failures, "selected pre-admission decline preserves D2-D7/A2-A6", () =>
                Assert.True(result.RegisterDifferences.Length == 0,
                    string.Join("; ", result.RegisterDifferences)));

        Assert.True(failures.Count == 0,
            $"{fixture.DescribeRoute(publicVector: true)}, {scenario}:\n" +
            string.Join("\n", failures));
    }

    public static IEnumerable<object[]> SaturatedFrameCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
            yield return new object[] { relocated, autoInitEntry };
    }

    [Theory]
    [MemberData(nameof(SaturatedFrameCases))]
    public void OpenMonitorPublicFramePreservesCalleeSavedRegistersOnSaturation(
        bool relocated,
        bool autoInitEntry)
    {
        using var fixture = new Fixture(relocated, autoInitEntry);
        fixture.SeedDefaultMonitor(fixture.GraphicsBase, MonitorAddress);
        fixture.SeedMonitorOpenCount(MonitorAddress, ushort.MaxValue);
        var before = fixture.CaptureMemory();

        var result = fixture.Invoke(
            0,
            DefaultDisplayId,
            fixture.GraphicsBase,
            publicVector: true);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        Check(failures, "saturation result and native/fallback provenance", () =>
        {
            Assert.Equal(DefaultDisplayId, result.Data0);
            Assert.True(result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "saturation publishes no memory", () =>
        {
            Assert.Equal(before.Monitor, after.Monitor);
            Assert.Equal(before.Name, after.Name);
            Assert.Equal(before.High, after.High);
            Assert.Equal(before.Low, after.Low);
            Assert.Equal(before.GraphicsImage, after.GraphicsImage);
            Assert.Equal(before.Resident, after.Resident);
            Assert.Equal(before.Caller, after.Caller);
            Assert.Equal(before.StackGuards, after.StackGuards);
        });
        Check(failures, "saturation preserves caller PC/SP, A1, and A6", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(0u, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
        });
        Check(failures, "saturation preserves D2-D7/A2-A6", () =>
            Assert.True(result.RegisterDifferences.Length == 0,
                string.Join("; ", result.RegisterDifferences)));

        Assert.True(failures.Count == 0,
            $"{fixture.DescribeRoute(publicVector: true)}, saturated:\n" +
            string.Join("\n", failures));
    }

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
        byte[] Name,
        byte[] High,
        byte[] Low,
        byte[] GraphicsImage,
        byte[] Resident,
        byte[] Caller,
        byte[] StackGuards,
        byte[] Database);

    private sealed record CallResult(
        uint Data0,
        uint Address0,
        uint Address1,
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
        internal const uint StackPointer = StackAddress + 0x300;
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
            Assert.Equal(OpenMonitorLvo, (int)GraphicsLvo.OpenMonitor);
            Assert.Equal(OpenMonitorFunctionOrdinal, (-OpenMonitorLvo / 6) - 1);
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

            _entry = _nativeCodeAddress + (uint)Image.Value.Entries[GraphicsLvo.OpenMonitor];
            _vectorSlot = checked((uint)((long)GraphicsBase + OpenMonitorLvo));
            Assert.Equal((ushort)0x4EF9, _bus.ReadWord(_vectorSlot));
            Assert.Equal(_entry, _bus.ReadLong(_vectorSlot + 2));
            Assert.Equal((ushort)0x4AFC, _bus.ReadWord(_residentAddress));
            var functionArray = _bus.ReadLong(_bus.ReadLong(_residentAddress + 0x16) + 4);
            _functionEntry = _bus.ReadLong(functionArray + OpenMonitorFunctionOrdinal * 4u);
            Assert.Equal(_entry, _functionEntry);

            _bus.MapWritableMemory(MonitorAddress, Enumerable.Repeat((byte)0x83, 0x100).ToArray());
            _bus.MapWritableMemory(NameAddress, Enumerable.Repeat((byte)0x69, 0x40).ToArray());
            _bus.MapWritableMemory(DatabaseAddress, new byte[GraphicsDisplayDatabase.NativeDatabaseSize]);
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
                _bus.WriteWord(CallerAddress + 2, unchecked((ushort)OpenMonitorLvo));
            }
            _bus.WriteWord(ReturnAddress, 0x4E71);

            SeedMonitorOpenCount(MonitorAddress, InitialOpenCount);
            _bus.WriteLong(GraphicsBase + (uint)GraphicsLayouts.GfxBaseDefaultMonitor, MonitorAddress);
            SeedList(MonitorAddress);
            SeedName(MonitorAddress + 0xA0, "pal.monitor");
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, _bus);
        }

        internal uint GraphicsBase { get; }
        internal uint ReturnAddress { get; }

        internal string DescribeRoute(bool publicVector)
            => "OpenMonitor/" + (_relocated ? "relocated HUNK" : "fixed image") + "/" +
               (_autoInitEntry ? "AUTOINIT JSR(A4)" : publicVector
                   ? "public JSR d16(A6)"
                   : "direct native body");

        internal void SeedDefaultMonitor(uint graphicsBase, uint monitor)
            => SeedLong(graphicsBase, GraphicsLayouts.GfxBaseDefaultMonitor, monitor);

        internal void SeedMonitorOpenCount(uint pointer, ushort openCount)
            => SeedWord(pointer, GraphicsLayouts.MonitorSpecOpenCount, openCount);

        internal void SeedResidentNamePointer(uint name)
            => SeedLong(MonitorAddress, GraphicsLayouts.MonitorSpecNodeName, name);

        internal void SeedRegistration(bool ntsc, string scenario)
        {
            _bus.MapWritableMemory(DatabaseAddress, GraphicsDisplayDatabase.CreateNativeDatabaseImage(false, ntsc));
            SeedLong(GraphicsBase, 0x250, GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag);
            SeedLong(GraphicsBase, 0x254, 1);
            SeedLong(GraphicsBase, 0x258, GraphicsBase);
            SeedLong(GraphicsBase, 0x25C, DatabaseAddress);
            SeedLong(GraphicsBase, 0x260, (uint)GraphicsDisplayDatabase.NativeDatabaseSize);
            SeedLong(GraphicsBase, GraphicsLayouts.GfxBaseDisplayInfoDataBase, DatabaseAddress);
            SeedRegisteredMonitor(ntsc, MonitorAddress);
            SeedLong(MonitorAddress, GraphicsLayouts.ExtendedNodeLibrary, GraphicsBase);
            SeedResidentNamePointer(MonitorAddress + 0xA0);
            SeedName(MonitorAddress + 0xA0, scenario == "renamed" ? "renamed.monitor" : ntsc ? "ntsc.monitor" : "pal.monitor");
            SeedWord(MonitorAddress, GraphicsLayouts.MonitorSpecFlags, scenario == "flags" ? (ushort)0 : ntsc ? (ushort)1 : (ushort)2);
            var offset = scenario switch { "tag" => 0x250, "version" => 0x254, "owner" => 0x258,
                "allocation" => 0x25C, "size" => 0x260, _ => -1 };
            if (offset >= 0) SeedLong(GraphicsBase, offset, 0xCAFE1234);
            if (scenario == "legacy") SeedLong(DatabaseAddress, 4, 2);
            if (scenario == "id") SeedLong(DatabaseAddress, GraphicsDisplayDatabase.NativeDefaultMonitorIdOffset, 0x31000);
            if (scenario == "foreign-cmmo")
                for (var field = 0x264; field < PositiveImageSize; field += 4) SeedLong(GraphicsBase, field, 0xCAFE1234);
            if (scenario == "backlink") SeedLong(MonitorAddress, GraphicsLayouts.ExtendedNodeLibrary, GraphicsBase + 2);
            if (scenario == "short-base") SeedWord(GraphicsBase, 0x12, 0x262);
            if (scenario == "allocation-wrap")
            {
                // Only the public160-byte node matters; its prefix must not wrap.
                SeedRegisteredMonitor(ntsc, 0xFFFFFF62);
            }
        }

        internal void SeedRegisteredMonitor(bool ntsc, uint monitor)
            => SeedLong(DatabaseAddress, GraphicsDisplayDatabase.NativeMonitorRegistrationsOffset + (ntsc ? 0 : 4), monitor);

        internal void SeedName(uint address, string name, bool terminate = true)
        {
            for (var i = 0; i < name.Length; i++) SeedByte(address, i, (byte)name[i]);
            if (terminate) SeedByte(address, name.Length, 0);
        }

        internal void MapAdditionalNodes(int count)
            => _bus.MapWritableMemory(AdditionalMonitorAddress, Enumerable.Repeat((byte)0x91, count * 0x100).ToArray());

        internal byte[] ReadAdditionalNodes(int count) => ReadBytes(AdditionalMonitorAddress, count * 0x100);

        internal void SeedList(params uint[] nodes)
        {
            var list = GraphicsBase + (uint)GraphicsLayouts.GfxBaseMonitorList;
            SeedLong(list, 0, nodes.Length == 0 ? list + 4 : nodes[0]);
            SeedLong(list, 4, 0);
            SeedLong(list, 8, nodes.Length == 0 ? list : nodes[^1]);
            SeedWord(list, 12, 0);
            for (var i = 0; i < nodes.Length; i++)
            {
                SeedLong(nodes[i], 0, i + 1 == nodes.Length ? list + 4 : nodes[i + 1]);
                SeedLong(nodes[i], 4, i == 0 ? list : nodes[i - 1]);
                SeedByte(nodes[i], GraphicsLayouts.MonitorSpecNodeType, 18);
                SeedWord(nodes[i], GraphicsLayouts.MonitorSpecNodeSubsystem, 0x0204);
                SeedLong(nodes[i], GraphicsLayouts.ExtendedNodeLibrary, GraphicsBase);
                SeedLong(nodes[i], GraphicsLayouts.MonitorSpecNodeName, nodes[i] + 0xA0);
                SeedMonitorOpenCount(nodes[i], InitialOpenCount);
            }
        }

        internal void SeedLong(uint pointer, int fieldOffset, uint value)
        {
            for (var index = 0; index < sizeof(uint); index++)
                SeedByte(pointer, fieldOffset + index,
                    unchecked((byte)(value >> (24 - index * 8))));
        }

        internal void SeedWord(uint pointer, int fieldOffset, ushort value)
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
            => Enumerable.Range(0, count)
                .Select(index => _bus.ReadByte(address + (uint)index))
                .ToArray();

        internal MemorySnapshot CaptureMemory()
            => new(
                ReadBytes(MonitorAddress, 0x100),
                ReadBytes(NameAddress, 0x40),
                ReadBytes(HighPhysicalAddress, 0x100),
                ReadBytes(0, 0x400),
                ReadBytes(
                    GraphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize,
                    NativeGraphicsLibraryImageBuilder.VectorTableSize + PositiveImageSize),
                ReadBytes(_residentAddress, _residentByteCount),
                ReadBytes(CallerAddress, 8),
                ReadBytes(StackAddress, 0x2C0).Concat(ReadBytes(StackPointer, 0x300)).ToArray(),
                ReadBytes(DatabaseAddress, GraphicsDisplayDatabase.NativeDatabaseSize));

        internal CallResult Invoke(
            uint name,
            uint displayId,
            uint graphicsBase,
            bool publicVector)
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
            _cpu.State.D[0] = displayId;
            _cpu.State.D[1] = 0xD1D1_0101;
            _cpu.State.A[0] = Address0Canary;
            _cpu.State.A[1] = name;
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
            for (var instruction = 0; instruction < 100_000; instruction++)
            {
                var pc = _cpu.State.ProgramCounter;
                Assert.True(
                    pc >= _nativeCodeAddress &&
                    (ulong)pc < (ulong)_nativeCodeAddress + (uint)Image.Value.Code.Length,
                    $"Unexpected execution address 0x{pc:X8}; no host graphics/Exec gateway is installed.");
                // Include range-linker RTS clones; D0 may legitimately equal the
                // input (notably a successful NULL result for display ID zero).
                usedFallback |= Image.Value.FallbackOffsets.Contains(unchecked((int)(pc - _nativeCodeAddress)));
                if (_bus.ReadWord(pc) == 0x4E75)
                {
                    nativeReturnCount++;
                    // NULL is a legitimate complete result even when the
                    // caller's ID was zero. Only executed fallback PC proves
                    // provider decline; equality of input/output cannot.
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
                    _cpu.State.A[1],
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
