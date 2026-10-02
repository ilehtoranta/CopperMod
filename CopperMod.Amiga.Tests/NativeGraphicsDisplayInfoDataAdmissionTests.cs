using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed partial class NativeGraphicsDisplayInfoDataAdmissionTests
{
    private const int GetDisplayInfoDataLvo = -756;
    private const uint ModeId = GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey;
    private const uint UnsupportedTag = 0x8000_5000;
    private const int PositiveImageSize = GraphicsLayouts.GfxBaseDefaultMonitor + sizeof(uint);
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback, out var audit);
        return new NativeImage(code, entries, fallback, audit.LocalFallbackOffsets.ToHashSet());
    });
    private static readonly Lazy<NativeImage> NtscImage = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback, out var audit, defaultMonitorNtsc: true);
        return new NativeImage(code, entries, fallback, audit.LocalFallbackOffsets.ToHashSet());
    });
    private static readonly Lazy<NativeGraphicsLibraryHunkImage> Hunk = new(() =>
        NativeGraphicsLibraryHunkBuilder.Build(
            Image.Value.Code,
            Image.Value.Entries,
            Image.Value.Fallback,
            PositiveImageSize));

    public static IEnumerable<object[]> DestinationAdmissionCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "unsupported-null",
            "short-disp-null",
            "disp-first-even",
            "disp-null",
            "disp-odd",
            "disp-exact-end",
            "disp-first-wrap",
            "dims-exact-end",
            "dims-first-wrap",
            "mntr-exact-end",
            "mntr-first-wrap",
            "name-final-even",
            "name-final-odd"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> ModeAdmissionCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "null-handle-default",
            "matching-public-handle",
            "public-handle-overrides-canonical-d2",
            "final-public-handle-overrides-foreign-d2",
            "private-default-handle-overrides-foreign-d2",
            "null-handle-foreign-mode",
            "foreign-handle-overrides-canonical-d2",
            "matching-foreign-handle",
            "nondefault-monitor"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> CapabilityAdmissionCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "name-null",
            "name-odd",
            "name-final",
            "disp-library-base",
            "disp-first-even",
            "disp-last-aligned",
            "disp-null",
            "disp-odd",
            "disp-exact-end",
            "disp-first-wrap",
            "disp-final-even",
            "disp-final-odd",
            "dims-library-base",
            "dims-last-aligned",
            "dims-null",
            "dims-first-wrap"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> DefaultMonitorAdmissionCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "mntr-library-base",
            "mntr-first-even",
            "mntr-last-base",
            "mntr-null",
            "mntr-odd",
            "mntr-first-wrap",
            "mntr-final-even",
            "mntr-final-odd",
            "mntr-missing-library",
            "mntr-missing-first-even",
            "mntr-missing-last-base",
            "nondefault-mntr-null",
            "nondefault-mntr-final",
            "name-final",
            "disp-library-base",
            "dims-last-aligned"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> PublicFrameCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "disp-default-ecs",
            "disp-pal-hires-ecs",
            "disp-ntsc-final-aga",
            "dims-default-ecs",
            "dims-pal-hires-aga",
            "dims-ntsc-final-ecs",
            "name-default-invalid-a6",
            "name-ntsc-final-invalid-a6",
            "mntr-default-resident",
            "foreign-name",
            "nondefault-mntr-null",
            "malformed-disp-null-a6",
            "missing-mntr-resident"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> InvalidModeZeroQueryCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "invalid-disp-full",
            "invalid-name-full",
            "invalid-mntr-full",
            "invalid-unsupported-full",
            "invalid-vec-full",
            "invalid-null-destination",
            "invalid-odd-destination",
            "invalid-short-disp",
            "invalid-zero-size",
            "canonical-handle-invalid-d2",
            "private-handle-invalid-d2",
            "foreign-handle-invalid-d2"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> EmptyQueryOwnershipCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "null-canonical-disp",
            "null-foreign-handle-disp",
            "zero-foreign-mode-name",
            "null-canonical-unsupported",
            "zero-foreign-mode-unsupported",
            "null-canonical-vec",
            "zero-canonical-vec",
            "null-nondefault-mntr",
            "null-private-default-handle",
            "zero-canonical-name",
            "zero-nondefault-mntr",
            "sentinel-vec",
            "nonempty-canonical-name"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> UnsupportedTagOwnershipCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "canonical-handle-foreign-d2",
            "canonical-d2-tag-zero",
            "private-default-vec-plus-one",
            "final-canonical-high-tag",
            "foreign-handle-canonical-d2",
            "foreign-d2",
            "exact-vec",
            "supported-name"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> NameIdentityPrefixCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "canonical-handle-eight",
            "canonical-d2-eight",
            "private-default-eight",
            "exact-end-eight",
            "first-wrap-eight-control",
            "foreign-handle-eight",
            "full-name-control"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> NameHeaderPrefixCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "one-byte-final",
            "two-byte-even",
            "three-byte-odd",
            "four-byte-exact-end",
            "five-byte-odd",
            "six-byte-even",
            "seven-byte-final-mode",
            "eight-byte-even-control",
            "eight-byte-odd",
            "nine-byte-private-default",
            "ten-byte-even",
            "eleven-byte-odd",
            "twelve-byte-even",
            "thirteen-byte-odd",
            "fourteen-byte-even",
            "fifteen-byte-odd",
            "sixteen-byte-exact-end",
            "sixteen-first-wrap-control",
            "foreign-sixteen",
            "zero-byte-control",
            "full-name-control"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> DisplayHeaderPrefixCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        {
            for (var requestedBytes = 1u; requestedBytes <= 16u; requestedBytes++)
            {
                yield return new object[]
                {
                    relocated,
                    autoInitEntry,
                    $"header-{requestedBytes}",
                    requestedBytes
                };
            }

            foreach (var (scenario, requestedBytes) in new[]
            {
                ("exact-end-1", 1u),
                ("exact-end-16", 16u),
                ("null-handle-final-canonical-7", 7u),
                ("private-default-9", 9u),
                ("foreign-provider-16", 16u),
                ("first-wrap-16-control", 16u),
                ("zero-size-control", 0u),
                ("null-destination-16-control", 16u),
                ("disp-55-control", 55u),
                ("full-disp-56-control", 56u),
                ("name-16-control", 16u),
                ("dims-16-control", 16u),
                ("mntr-16-control", 16u),
                ("unsupported-16-control", 16u),
                ("vec-16-provider-control", 16u),
                ("sentinel-16-control", 16u)
            })
            {
                yield return new object[]
                {
                    relocated,
                    autoInitEntry,
                    scenario,
                    requestedBytes
                };
            }
        }
    }

    public static IEnumerable<object[]> DimensionHeaderPrefixCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        {
            for (var size = 1u; size <= 16u; size++)
            {
                foreach (var scenario in new[]
                {
                    "header", "exact-end", "null-a6", "superhires-odd-a6", "foreign-provider"
                })
                    yield return new object[] { relocated, autoInitEntry, scenario, size };
                if (size > 1)
                {
                    yield return new object[] { relocated, autoInitEntry, "first-wrap", size };
                    yield return new object[] { relocated, autoInitEntry, "foreign-first-wrap", size };
                }
            }
            foreach (var (scenario, size) in new[]
            {
                ("null-handle-final-canonical", 7u), ("private-default", 9u),
                ("zero-size", 0u), ("null-destination", 16u),
                ("dims17-control", 17u), ("dims87-control", 87u),
                ("full-dims88-control", 88u), ("disp16-control", 16u),
                ("name16-control", 16u), ("mntr16-control", 16u),
                ("unsupported-control", 16u), ("vec-provider-control", 16u),
                ("sentinel-control", 16u)
            })
                yield return new object[] { relocated, autoInitEntry, scenario, size };
        }
    }

    public static IEnumerable<object[]> MonitorHeaderPrefixCases()
    {
        foreach (var source in DimensionHeaderPrefixCases())
        {
            var row = (object[])source.Clone();
            switch ((string)row[2])
            {
                case "dims17-control": row[2] = "mntr17-control"; break;
                case "dims87-control": row[2] = "mntr95-control"; row[3] = 95u; break;
                case "mntr16-control": row[2] = "dims16-control"; break;
            }
            yield return row;
        }
    }

    public static IEnumerable<object[]> DisplaySeventeenByteCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var (scenario, requestedBytes) in new[]
        {
            ("disp17-even-authoritative", 17u),
            ("disp17-odd", 17u),
            ("disp17-exact-end", 17u),
            ("disp17-null-final-canonical", 17u),
            ("disp17-private-default", 17u),
            ("disp17-superhires-null-a6", 17u),
            ("disp17-superhires-odd-a6", 17u),
            ("disp17-superhires-final-a6", 17u),
            ("disp17-foreign-provider", 17u),
            ("disp17-first-wrap-control", 17u),
            ("disp17-foreign-first-wrap-control", 17u),
            ("disp16-control", 16u),
            ("disp55-control", 55u),
            ("disp56-control", 56u),
            ("name17-control", 17u),
            ("dims17-control", 17u),
            ("mntr17-control", 17u),
            ("unsupported17-control", 17u),
            ("vec17-provider-control", 17u),
            ("sentinel17-control", 17u)
        })
            yield return new object[]
            {
                relocated,
                autoInitEntry,
                scenario,
                requestedBytes
            };
    }

    public static IEnumerable<object[]> DisplayEighteenByteCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var (scenario, requestedBytes) in new[]
        {
            ("disp18-even-authoritative-final-a6", 18u),
            ("disp18-odd", 18u),
            ("disp18-exact-end", 18u),
            ("disp18-null-final-canonical", 18u),
            ("disp18-private-default", 18u),
            ("disp18-superhires-first-even-ocs", 18u),
            ("disp18-superhires-library-agnus", 18u),
            ("disp18-superhires-library-denise", 18u),
            ("disp18-superhires-last-aligned-ecs", 18u),
            ("disp18-superhires-library-aga", 18u),
            ("disp18-foreign-provider", 18u),
            ("disp18-superhires-null-a6-control", 18u),
            ("disp18-superhires-odd-a6-control", 18u),
            ("disp18-superhires-exact-end-a6-control", 18u),
            ("disp18-superhires-first-wrap-a6-control", 18u),
            ("disp18-first-wrap-control", 18u),
            ("disp18-superhires-first-wrap-destination-control", 18u),
            ("disp18-foreign-first-wrap-control", 18u),
            ("disp17-control", 17u),
            ("disp55-control", 55u),
            ("disp56-control", 56u),
            ("name18-control", 18u),
            ("dims18-control", 18u),
            ("mntr18-control", 18u),
            ("unsupported18-control", 18u),
            ("vec18-provider-control", 18u),
            ("sentinel18-control", 18u)
        })
            yield return new object[]
            {
                relocated,
                autoInitEntry,
                scenario,
                requestedBytes
            };
    }

    public static IEnumerable<object[]> DisplayNineteenByteCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var (scenario, requestedBytes) in new[]
        {
            ("disp19-even-authoritative-final-a6", 19u),
            ("disp19-odd", 19u),
            ("disp19-exact-end", 19u),
            ("disp19-null-final-canonical", 19u),
            ("disp19-private-default", 19u),
            ("disp19-superhires-first-even-ocs", 19u),
            ("disp19-superhires-library-agnus", 19u),
            ("disp19-superhires-library-denise", 19u),
            ("disp19-superhires-last-aligned-ecs", 19u),
            ("disp19-superhires-library-aga", 19u),
            ("disp19-foreign-provider", 19u),
            ("disp19-superhires-null-a6-control", 19u),
            ("disp19-superhires-odd-a6-control", 19u),
            ("disp19-superhires-exact-end-a6-control", 19u),
            ("disp19-superhires-first-wrap-a6-control", 19u),
            ("disp19-first-wrap-control", 19u),
            ("disp19-superhires-first-wrap-destination-control", 19u),
            ("disp19-foreign-first-wrap-control", 19u),
            ("disp18-control", 18u),
            ("disp55-control", 55u),
            ("disp56-control", 56u),
            ("name19-control", 19u),
            ("dims19-control", 19u),
            ("mntr19-control", 19u),
            ("unsupported19-control", 19u),
            ("vec19-provider-control", 19u),
            ("sentinel19-control", 19u)
        })
            yield return new object[]
            {
                relocated,
                autoInitEntry,
                scenario,
                requestedBytes
            };
    }

    public static IEnumerable<object[]> DisplayTwentyByteCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var (scenario, requestedBytes) in new[]
        {
            ("disp20-ordinary-even-authoritative-ecs", 20u),
            ("disp20-ordinary-odd-alice", 20u),
            ("disp20-ordinary-exact-end-lisa", 20u),
            ("disp20-ordinary-null-final-canonical-aa", 20u),
            ("disp20-ordinary-private-default-aa-pair", 20u),
            ("disp20-ordinary-first-even-ocs", 20u),
            ("disp20-ordinary-last-aligned-setaa", 20u),
            ("disp20-superhires-ocs", 20u),
            ("disp20-superhires-ecs", 20u),
            ("disp20-superhires-alice", 20u),
            ("disp20-superhires-lisa", 20u),
            ("disp20-superhires-aa-pair", 20u),
            ("disp20-superhires-setaa", 20u),
            ("disp20-foreign-provider", 20u),
            ("disp20-ordinary-null-a6-control", 20u),
            ("disp20-ordinary-odd-a6-control", 20u),
            ("disp20-ordinary-exact-end-a6-control", 20u),
            ("disp20-ordinary-first-wrap-a6-control", 20u),
            ("disp20-first-wrap-control", 20u),
            ("disp20-superhires-first-wrap-destination-control", 20u),
            ("disp20-foreign-first-wrap-control", 20u),
            ("disp18-ordinary-invalid-a6-control", 18u),
            ("disp19-ordinary-invalid-a6-control", 19u),
            ("disp18-superhires-valid-control", 18u),
            ("disp19-superhires-valid-control", 19u),
            ("disp55-control", 55u),
            ("disp56-control", 56u),
            ("name20-control", 20u),
            ("dims20-control", 20u),
            ("mntr20-control", 20u),
            ("unsupported20-control", 20u),
            ("vec20-provider-control", 20u),
            ("sentinel20-control", 20u)
        })
            yield return new object[]
            {
                relocated,
                autoInitEntry,
                scenario,
                requestedBytes
            };
    }

    public static IEnumerable<object[]> DisplayTwentyOneByteCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var (scenario, requestedBytes) in new[]
        {
            ("disp21-ordinary-even-authoritative-ecs", 21u),
            ("disp21-ordinary-odd-alice", 21u),
            ("disp21-ordinary-exact-end-lisa", 21u),
            ("disp21-null-final-canonical-aa", 21u),
            ("disp21-ordinary-private-default-aa-pair", 21u),
            ("disp21-ordinary-last-aligned-setaa", 21u),
            ("disp21-ehb-ocs", 21u),
            ("disp21-ehb-setaa", 21u),
            ("disp21-superhires-ocs", 21u),
            ("disp21-superhires-ecs", 21u),
            ("disp21-superhires-aa-pair", 21u),
            ("disp21-superhires-setaa", 21u),
            ("disp21-foreign-provider", 21u),
            ("disp21-ordinary-null-a6-control", 21u),
            ("disp21-ordinary-odd-a6-control", 21u),
            ("disp21-ordinary-exact-end-a6-control", 21u),
            ("disp21-ordinary-first-wrap-a6-control", 21u),
            ("disp21-first-wrap-control", 21u),
            ("disp21-superhires-first-wrap-destination-control", 21u),
            ("disp21-foreign-first-wrap-control", 21u),
            ("disp20-ordinary-valid-control", 20u),
            ("disp20-superhires-valid-control", 20u),
            ("disp55-control", 55u),
            ("disp56-control", 56u),
            ("name21-control", 21u),
            ("dims21-control", 21u),
            ("mntr21-control", 21u),
            ("unsupported21-control", 21u),
            ("vec21-provider-control", 21u),
            ("sentinel21-control", 21u)
        })
            yield return new object[]
            {
                relocated,
                autoInitEntry,
                scenario,
                requestedBytes
            };
    }

    public static IEnumerable<object[]> DisplayTwentyTwoByteCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "disp22-pal-ordinary-even-ecs",
            "disp22-ntsc-ordinary-odd-alice",
            "disp22-ntsc-lace-exact-end-lisa",
            "disp22-null-ntsc-ehb-lace-aa-pair",
            "disp22-private-default-aa-pair",
            "disp22-pal-dpf-last-aligned-setaa",
            "disp22-ntsc-dpf2-first-even-ocs",
            "disp22-pal-ham-setaa",
            "disp22-ntsc-ehb-ocs",
            "disp22-pal-superhires-ocs",
            "disp22-ntsc-superhires-ecs",
            "disp22-pal-superhires-dpf2-lace-aa-pair",
            "disp22-foreign-provider",
            "disp22-null-a6-control",
            "disp22-odd-a6-control",
            "disp22-exact-end-a6-control",
            "disp22-first-wrap-a6-control",
            "disp22-first-wrap-control",
            "disp22-superhires-first-wrap-control",
            "disp22-foreign-first-wrap-control",
            "disp20-ordinary-valid-control",
            "disp21-superhires-valid-control",
            "disp55-control",
            "disp56-control",
            "name22-control",
            "dims22-control",
            "mntr22-control",
            "unsupported22-control",
            "vec22-provider-control",
            "sentinel22-control"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> DisplayTwentyThreeByteCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "disp23-pal-ordinary-even-ecs",
            "disp23-ntsc-ordinary-odd-alice",
            "disp23-ntsc-lace-exact-end-lisa",
            "disp23-null-ntsc-ehb-lace-aa-pair",
            "disp23-private-default-aa-pair",
            "disp23-pal-dpf-last-aligned-setaa",
            "disp23-ntsc-dpf2-first-even-ocs",
            "disp23-pal-ham-setaa",
            "disp23-ntsc-ehb-ocs",
            "disp23-pal-superhires-ocs",
            "disp23-ntsc-superhires-ecs",
            "disp23-pal-superhires-dpf2-lace-aa-pair",
            "disp23-foreign-provider",
            "disp23-null-a6-control",
            "disp23-odd-a6-control",
            "disp23-exact-end-a6-control",
            "disp23-first-wrap-a6-control",
            "disp23-first-wrap-control",
            "disp23-superhires-first-wrap-control",
            "disp23-foreign-first-wrap-control",
            "disp21-ordinary-valid-control",
            "disp22-superhires-valid-control",
            "disp55-control",
            "disp56-control",
            "name23-control",
            "dims23-control",
            "mntr23-control",
            "unsupported23-control",
            "vec23-provider-control",
            "sentinel23-control"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> DisplayTwentyFourByteCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "disp24-pal-ordinary-even-ecs",
            "disp24-ntsc-ordinary-odd-alice",
            "disp24-ntsc-lace-exact-end-lisa",
            "disp24-null-ntsc-ehb-lace-aa-pair",
            "disp24-private-default-aa-pair",
            "disp24-pal-dpf-last-aligned-setaa",
            "disp24-ntsc-dpf2-first-even-ocs",
            "disp24-default-ham-setaa",
            "disp24-ntsc-ehb-ocs",
            "disp24-pal-superhires-ocs",
            "disp24-ntsc-superhires-ecs",
            "disp24-pal-superhires-dpf2-lace-aa-pair",
            "disp24-foreign-provider",
            "disp24-null-a6-control",
            "disp24-odd-a6-control",
            "disp24-exact-end-a6-control",
            "disp24-first-wrap-a6-control",
            "disp24-first-wrap-control",
            "disp24-superhires-first-wrap-control",
            "disp24-foreign-first-wrap-control",
            "disp22-ordinary-valid-control",
            "disp23-superhires-valid-control",
            "disp55-control",
            "disp56-control",
            "name24-control",
            "dims24-control",
            "mntr24-control",
            "unsupported24-control",
            "vec24-provider-control",
            "sentinel24-control"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> DisplayTwentyFiveByteCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "disp25-pal-ordinary-even-ecs",
            "disp25-ntsc-ordinary-odd-alice",
            "disp25-ntsc-lace-exact-end-lisa",
            "disp25-null-ntsc-ehb-lace-aa-pair",
            "disp25-private-default-aa-pair",
            "disp25-pal-dpf-last-aligned-setaa",
            "disp25-ntsc-dpf2-first-even-ocs",
            "disp25-default-ham-setaa",
            "disp25-ntsc-ehb-ocs",
            "disp25-pal-superhires-ocs",
            "disp25-ntsc-superhires-ecs",
            "disp25-pal-superhires-dpf2-lace-aa-pair",
            "disp25-foreign-provider",
            "disp25-null-a6-control",
            "disp25-odd-a6-control",
            "disp25-exact-end-a6-control",
            "disp25-first-wrap-a6-control",
            "disp25-first-wrap-control",
            "disp25-superhires-first-wrap-control",
            "disp25-foreign-first-wrap-control",
            "disp23-ordinary-valid-control",
            "disp24-superhires-valid-control",
            "disp55-control",
            "disp56-control",
            "name25-control",
            "dims25-control",
            "mntr25-control",
            "unsupported25-control",
            "vec25-provider-control",
            "sentinel25-control"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> DisplayTwentySixByteCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "disp26-pal-ordinary-even-ecs",
            "disp26-ntsc-ordinary-odd-alice",
            "disp26-ntsc-lace-exact-end-lisa",
            "disp26-null-ntsc-ehb-lace-aa-pair",
            "disp26-private-default-aa-pair",
            "disp26-pal-dpf-last-aligned-setaa",
            "disp26-ntsc-dpf2-first-even-ocs",
            "disp26-default-ham-setaa",
            "disp26-ntsc-ehb-ocs",
            "disp26-pal-superhires-ocs",
            "disp26-ntsc-superhires-ecs",
            "disp26-pal-superhires-dpf2-lace-aa-pair",
            "disp26-foreign-provider",
            "disp26-null-a6-control",
            "disp26-odd-a6-control",
            "disp26-exact-end-a6-control",
            "disp26-first-wrap-a6-control",
            "disp26-first-wrap-control",
            "disp26-superhires-first-wrap-control",
            "disp26-foreign-first-wrap-control",
            "disp24-ordinary-valid-control",
            "disp25-superhires-valid-control",
            "disp55-control",
            "disp56-control",
            "name26-control",
            "dims26-control",
            "mntr26-control",
            "unsupported26-control",
            "vec26-provider-control",
            "sentinel26-control"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> DisplayTwentySevenByteCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "disp27-pal-ordinary-even-ecs",
            "disp27-ntsc-ordinary-odd-alice",
            "disp27-ntsc-lace-exact-end-lisa",
            "disp27-null-ntsc-ehb-lace-aa-pair",
            "disp27-private-default-aa-pair",
            "disp27-pal-dpf-last-aligned-setaa",
            "disp27-ntsc-dpf2-first-even-ocs",
            "disp27-default-ham-setaa",
            "disp27-ntsc-ehb-ocs",
            "disp27-pal-superhires-ocs",
            "disp27-ntsc-superhires-ecs",
            "disp27-pal-superhires-dpf2-lace-aa-pair",
            "disp27-foreign-provider",
            "disp27-null-a6-control",
            "disp27-odd-a6-control",
            "disp27-exact-end-a6-control",
            "disp27-first-wrap-a6-control",
            "disp27-first-wrap-control",
            "disp27-superhires-first-wrap-control",
            "disp27-foreign-first-wrap-control",
            "disp25-ordinary-valid-control",
            "disp26-superhires-valid-control",
            "disp55-control",
            "disp56-control",
            "name27-control",
            "dims27-control",
            "mntr27-control",
            "unsupported27-control",
            "vec27-provider-control",
            "sentinel27-control"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> DisplayTwentyEightByteCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "disp28-pal-ordinary-even-ecs",
            "disp28-ntsc-ordinary-odd-alice",
            "disp28-ntsc-lace-exact-end-lisa",
            "disp28-null-ntsc-ehb-lace-aa-pair",
            "disp28-private-default-aa-pair",
            "disp28-pal-dpf-last-aligned-setaa",
            "disp28-ntsc-dpf2-first-even-ocs",
            "disp28-default-ham-setaa",
            "disp28-ntsc-ehb-ocs",
            "disp28-pal-superhires-ocs",
            "disp28-ntsc-superhires-ecs",
            "disp28-pal-superhires-dpf2-lace-aa-pair",
            "disp28-foreign-provider",
            "disp28-null-a6-control",
            "disp28-odd-a6-control",
            "disp28-exact-end-a6-control",
            "disp28-first-wrap-a6-control",
            "disp28-first-wrap-control",
            "disp28-superhires-first-wrap-control",
            "disp28-foreign-first-wrap-control",
            "disp26-ordinary-valid-control",
            "disp27-superhires-valid-control",
            "disp55-control",
            "disp56-control",
            "name28-control",
            "dims28-control",
            "mntr28-control",
            "unsupported28-control",
            "vec28-provider-control",
            "sentinel28-control"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> DisplayTwentyNineByteCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "disp29-pal-ordinary-even-ecs",
            "disp29-ntsc-ordinary-odd-alice",
            "disp29-ntsc-lace-exact-end-lisa",
            "disp29-null-ntsc-ehb-lace-aa-pair",
            "disp29-private-default-aa-pair",
            "disp29-pal-dpf-last-aligned-setaa",
            "disp29-ntsc-dpf2-first-even-ocs",
            "disp29-default-ham-setaa",
            "disp29-ntsc-ehb-ocs",
            "disp29-pal-superhires-ocs",
            "disp29-ntsc-superhires-ecs",
            "disp29-pal-superhires-dpf2-lace-aa-pair",
            "disp29-foreign-provider",
            "disp29-null-a6-control",
            "disp29-odd-a6-control",
            "disp29-exact-end-a6-control",
            "disp29-first-wrap-a6-control",
            "disp29-first-wrap-control",
            "disp29-superhires-first-wrap-control",
            "disp29-foreign-first-wrap-control",
            "disp27-ordinary-valid-control",
            "disp28-superhires-valid-control",
            "disp55-control",
            "disp56-control",
            "name29-control",
            "dims29-control",
            "mntr29-control",
            "unsupported29-control",
            "vec29-provider-control",
            "sentinel29-control"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> DisplayThirtyByteCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "disp30-pal-ordinary-even-ecs",
            "disp30-ntsc-ordinary-odd-alice",
            "disp30-ntsc-lace-exact-end-lisa",
            "disp30-null-ntsc-ehb-lace-aa-pair",
            "disp30-private-default-aa-pair",
            "disp30-pal-dpf-last-aligned-setaa",
            "disp30-ntsc-dpf2-first-even-ocs",
            "disp30-default-ham-setaa",
            "disp30-ntsc-ehb-ocs",
            "disp30-pal-superhires-ocs",
            "disp30-ntsc-superhires-ecs",
            "disp30-pal-superhires-dpf2-lace-aa-pair",
            "disp30-foreign-provider",
            "disp30-null-a6-control",
            "disp30-odd-a6-control",
            "disp30-exact-end-a6-control",
            "disp30-first-wrap-a6-control",
            "disp30-first-wrap-control",
            "disp30-superhires-first-wrap-control",
            "disp30-foreign-first-wrap-control",
            "disp28-ordinary-valid-control",
            "disp29-superhires-valid-control",
            "disp55-control",
            "disp56-control",
            "name30-control",
            "dims30-control",
            "mntr30-control",
            "unsupported30-control",
            "vec30-provider-control",
            "sentinel30-control"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> DisplayThirtyOneByteCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "disp31-pal-ordinary-even-ecs",
            "disp31-ntsc-ordinary-odd-alice",
            "disp31-ntsc-superhires-lace-exact-end-lisa",
            "disp31-null-ntsc-ehb-lace-aa-pair",
            "disp31-private-default-aa-pair",
            "disp31-pal-dpf-last-aligned-setaa",
            "disp31-ntsc-dpf2-first-even-ocs",
            "disp31-default-ham-setaa",
            "disp31-ntsc-superhires-setaa",
            "disp31-pal-superhires-ocs",
            "disp31-ntsc-superhires-ecs",
            "disp31-pal-superhires-dpf2-lace-aa-pair",
            "disp31-foreign-provider",
            "disp31-null-a6-control",
            "disp31-odd-a6-control",
            "disp31-exact-end-a6-control",
            "disp31-first-wrap-a6-control",
            "disp31-first-wrap-control",
            "disp31-superhires-first-wrap-control",
            "disp31-foreign-first-wrap-control",
            "disp29-ordinary-valid-control",
            "disp30-superhires-valid-control",
            "disp55-control",
            "disp56-control",
            "name31-control",
            "dims31-control",
            "mntr31-control",
            "unsupported31-control",
            "vec31-provider-control",
            "sentinel31-control"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> DisplayThirtyTwoByteCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "disp32-pal-ordinary-even-ecs",
            "disp32-ntsc-ordinary-odd-alice",
            "disp32-ntsc-superhires-lace-exact-end-lisa",
            "disp32-null-ntsc-ehb-lace-aa-pair",
            "disp32-private-default-aa-pair",
            "disp32-pal-dpf-last-aligned-setaa",
            "disp32-ntsc-dpf2-first-even-ocs",
            "disp32-default-ham-setaa",
            "disp32-ntsc-superhires-setaa",
            "disp32-pal-superhires-ocs",
            "disp32-ntsc-superhires-ecs",
            "disp32-pal-superhires-dpf2-lace-aa-pair",
            "disp32-foreign-provider",
            "disp32-null-a6-control",
            "disp32-odd-a6-control",
            "disp32-exact-end-a6-control",
            "disp32-first-wrap-a6-control",
            "disp32-first-wrap-control",
            "disp32-superhires-first-wrap-control",
            "disp32-foreign-first-wrap-control",
            "disp30-ordinary-valid-control",
            "disp31-superhires-valid-control",
            "disp55-control",
            "disp56-control",
            "name32-control",
            "dims32-control",
            "mntr32-control",
            "unsupported32-control",
            "vec32-provider-control",
            "sentinel32-control"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> DisplayThirtyThreeByteCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "disp33-pal-ordinary-even-ecs",
            "disp33-ntsc-ordinary-odd-alice",
            "disp33-ntsc-superhires-lace-exact-end-lisa",
            "disp33-null-ntsc-ehb-lace-aa-pair",
            "disp33-private-default-aa-pair",
            "disp33-pal-dpf-last-aligned-setaa",
            "disp33-ntsc-dpf2-first-even-ocs",
            "disp33-default-ham-setaa",
            "disp33-ntsc-superhires-setaa",
            "disp33-pal-superhires-ocs",
            "disp33-ntsc-superhires-ecs",
            "disp33-pal-superhires-dpf2-lace-aa-pair",
            "disp33-foreign-provider",
            "disp33-null-a6-control",
            "disp33-odd-a6-control",
            "disp33-exact-end-a6-control",
            "disp33-first-wrap-a6-control",
            "disp33-first-wrap-control",
            "disp33-superhires-first-wrap-control",
            "disp33-foreign-first-wrap-control",
            "disp31-ordinary-valid-control",
            "disp32-superhires-valid-control",
            "disp55-control",
            "disp56-control",
            "name33-control",
            "dims33-control",
            "mntr33-control",
            "unsupported33-control",
            "vec33-provider-control",
            "sentinel33-control"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> DisplayThirtyFourByteCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "disp34-pal-ordinary-even-ecs",
            "disp34-ntsc-ordinary-odd-alice",
            "disp34-ntsc-superhires-lace-exact-end-lisa",
            "disp34-null-ntsc-ehb-lace-aa-pair",
            "disp34-private-default-aa-pair",
            "disp34-pal-dpf-last-aligned-setaa",
            "disp34-ntsc-dpf2-first-even-ocs",
            "disp34-default-ham-setaa",
            "disp34-ntsc-superhires-setaa",
            "disp34-pal-superhires-ocs",
            "disp34-ntsc-superhires-ecs",
            "disp34-pal-superhires-dpf2-lace-aa-pair",
            "disp34-foreign-provider",
            "disp34-null-a6-control",
            "disp34-odd-a6-control",
            "disp34-exact-end-a6-control",
            "disp34-first-wrap-a6-control",
            "disp34-first-wrap-control",
            "disp34-superhires-first-wrap-control",
            "disp34-foreign-first-wrap-control",
            "disp32-ordinary-valid-control",
            "disp33-superhires-valid-control",
            "disp55-control",
            "disp56-control",
            "name34-control",
            "dims34-control",
            "mntr34-control",
            "unsupported34-control",
            "vec34-provider-control",
            "sentinel34-control"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> DisplayThirtyFiveByteCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "disp35-pal-ordinary-even-ecs",
            "disp35-ntsc-ordinary-odd-alice",
            "disp35-ntsc-superhires-lace-exact-end-lisa",
            "disp35-null-ntsc-ehb-lace-aa-pair",
            "disp35-private-default-aa-pair",
            "disp35-pal-dpf-last-aligned-setaa",
            "disp35-ntsc-dpf2-first-even-ocs",
            "disp35-default-ham-setaa",
            "disp35-ntsc-superhires-setaa",
            "disp35-pal-superhires-ocs",
            "disp35-ntsc-superhires-ecs",
            "disp35-pal-superhires-dpf2-lace-aa-pair",
            "disp35-foreign-provider",
            "disp35-null-a6-control",
            "disp35-odd-a6-control",
            "disp35-exact-end-a6-control",
            "disp35-first-wrap-a6-control",
            "disp35-first-wrap-control",
            "disp35-superhires-first-wrap-control",
            "disp35-foreign-first-wrap-control",
            "disp33-ordinary-valid-control",
            "disp34-superhires-valid-control",
            "disp55-control",
            "disp56-control",
            "name35-control",
            "dims35-control",
            "mntr35-control",
            "unsupported35-control",
            "vec35-provider-control",
            "sentinel35-control"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> DisplayThirtySixByteCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "disp36-pal-ordinary-even-ecs",
            "disp36-ntsc-ordinary-odd-alice",
            "disp36-ntsc-superhires-lace-exact-end-lisa",
            "disp36-null-ntsc-ehb-lace-aa-pair",
            "disp36-private-default-aa-pair",
            "disp36-pal-dpf-last-aligned-setaa",
            "disp36-ntsc-dpf2-first-even-ocs",
            "disp36-default-ham-setaa",
            "disp36-ntsc-superhires-setaa",
            "disp36-pal-superhires-ocs",
            "disp36-ntsc-superhires-ecs",
            "disp36-pal-superhires-dpf2-lace-aa-pair",
            "disp36-foreign-provider",
            "disp36-null-a6-control",
            "disp36-odd-a6-control",
            "disp36-exact-end-a6-control",
            "disp36-first-wrap-a6-control",
            "disp36-first-wrap-control",
            "disp36-superhires-first-wrap-control",
            "disp36-foreign-first-wrap-control",
            "disp34-ordinary-valid-control",
            "disp35-superhires-valid-control",
            "disp55-control",
            "disp56-control",
            "name36-control",
            "dims36-control",
            "mntr36-control",
            "unsupported36-control",
            "vec36-provider-control",
            "sentinel36-control"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> DisplayThirtySevenByteCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "disp37-pal-ordinary-even-ecs",
            "disp37-ntsc-ordinary-odd-alice",
            "disp37-ntsc-superhires-lace-exact-end-lisa",
            "disp37-null-ntsc-ehb-lace-aa-pair",
            "disp37-private-default-aa-pair",
            "disp37-pal-dpf-last-aligned-setaa",
            "disp37-ntsc-dpf2-first-even-ocs",
            "disp37-default-ham-setaa",
            "disp37-ntsc-superhires-setaa",
            "disp37-pal-superhires-ocs",
            "disp37-ntsc-superhires-ecs",
            "disp37-pal-superhires-dpf2-lace-aa-pair",
            "disp37-foreign-provider",
            "disp37-null-a6-control",
            "disp37-odd-a6-control",
            "disp37-exact-end-a6-control",
            "disp37-first-wrap-a6-control",
            "disp37-first-wrap-control",
            "disp37-superhires-first-wrap-control",
            "disp37-foreign-first-wrap-control",
            "disp35-ordinary-valid-control",
            "disp36-superhires-valid-control",
            "disp55-control",
            "disp56-control",
            "name37-control",
            "dims37-control",
            "mntr37-control",
            "unsupported37-control",
            "vec37-provider-control",
            "sentinel37-control"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> DisplayThirtyEightByteCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "disp38-pal-ordinary-even-ecs",
            "disp38-ntsc-ordinary-odd-alice",
            "disp38-ntsc-superhires-lace-exact-end-lisa",
            "disp38-null-ntsc-ehb-lace-aa-pair",
            "disp38-private-default-aa-pair",
            "disp38-pal-dpf-last-aligned-setaa",
            "disp38-ntsc-dpf2-first-even-ocs",
            "disp38-default-ham-setaa",
            "disp38-ntsc-superhires-setaa",
            "disp38-pal-superhires-ocs",
            "disp38-ntsc-superhires-ecs",
            "disp38-pal-superhires-dpf2-lace-aa-pair",
            "disp38-foreign-provider",
            "disp38-null-a6-control",
            "disp38-odd-a6-control",
            "disp38-exact-end-a6-control",
            "disp38-first-wrap-a6-control",
            "disp38-first-wrap-control",
            "disp38-superhires-first-wrap-control",
            "disp38-foreign-first-wrap-control",
            "disp36-ordinary-valid-control",
            "disp37-superhires-valid-control",
            "disp55-control",
            "disp56-control",
            "name38-control",
            "dims38-control",
            "mntr38-control",
            "unsupported38-control",
            "vec38-provider-control",
            "sentinel38-control"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> DisplayThirtyNineByteCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "disp39-pal-ordinary-even-ecs",
            "disp39-ntsc-ordinary-odd-alice",
            "disp39-ntsc-superhires-lace-exact-end-lisa",
            "disp39-null-ntsc-ehb-lace-aa-pair",
            "disp39-private-default-aa-pair",
            "disp39-pal-dpf-last-aligned-setaa",
            "disp39-ntsc-dpf2-first-even-ocs",
            "disp39-default-ham-setaa",
            "disp39-ntsc-superhires-setaa",
            "disp39-pal-superhires-ocs",
            "disp39-ntsc-superhires-ecs",
            "disp39-pal-superhires-dpf2-lace-aa-pair",
            "disp39-foreign-provider",
            "disp39-null-a6-control",
            "disp39-odd-a6-control",
            "disp39-exact-end-a6-control",
            "disp39-first-wrap-a6-control",
            "disp39-first-wrap-control",
            "disp39-superhires-first-wrap-control",
            "disp39-foreign-first-wrap-control",
            "disp37-ordinary-valid-control",
            "disp38-superhires-valid-control",
            "disp55-control",
            "disp56-control",
            "name39-control",
            "dims39-control",
            "mntr39-control",
            "unsupported39-control",
            "vec39-provider-control",
            "sentinel39-control"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> DisplayFortyByteCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "disp40-pal-ordinary-even-ecs",
            "disp40-ntsc-ordinary-odd-alice",
            "disp40-ntsc-superhires-lace-exact-end-lisa",
            "disp40-null-ntsc-ehb-lace-aa-pair",
            "disp40-private-default-aa-pair",
            "disp40-pal-dpf-last-aligned-setaa",
            "disp40-ntsc-dpf2-first-even-ocs",
            "disp40-default-ham-setaa",
            "disp40-ntsc-superhires-setaa",
            "disp40-pal-superhires-ocs",
            "disp40-ntsc-superhires-ecs",
            "disp40-pal-superhires-dpf2-lace-aa-pair",
            "disp40-foreign-provider",
            "disp40-null-a6-control",
            "disp40-odd-a6-control",
            "disp40-exact-end-a6-control",
            "disp40-first-wrap-a6-control",
            "disp40-first-wrap-control",
            "disp40-superhires-first-wrap-control",
            "disp40-foreign-first-wrap-control",
            "disp38-ordinary-valid-control",
            "disp39-superhires-valid-control",
            "disp55-control",
            "disp56-control",
            "name40-control",
            "dims40-control",
            "mntr40-control",
            "unsupported40-control",
            "vec40-provider-control",
            "sentinel40-control"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> DisplayFortyOneByteCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "disp41-pal-ordinary-even-ecs",
            "disp41-ntsc-ordinary-odd-alice",
            "disp41-ntsc-superhires-lace-exact-end-lisa",
            "disp41-null-ntsc-ehb-lace-aa-pair",
            "disp41-private-default-aa-pair",
            "disp41-pal-dpf-last-aligned-setaa",
            "disp41-ntsc-dpf2-first-even-ocs",
            "disp41-default-ham-setaa",
            "disp41-ntsc-superhires-setaa",
            "disp41-pal-superhires-ocs",
            "disp41-ntsc-superhires-ecs",
            "disp41-pal-superhires-dpf2-lace-aa-pair",
            "disp41-foreign-provider",
            "disp41-null-a6-control",
            "disp41-odd-a6-control",
            "disp41-exact-end-a6-control",
            "disp41-first-wrap-a6-control",
            "disp41-first-wrap-control",
            "disp41-superhires-first-wrap-control",
            "disp41-foreign-first-wrap-control",
            "disp39-ordinary-valid-control",
            "disp40-superhires-valid-control",
            "disp55-control",
            "disp56-control",
            "name41-control",
            "dims41-control",
            "mntr41-control",
            "unsupported41-control",
            "vec41-provider-control",
            "sentinel41-control"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> DisplayFortyTwoByteCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "disp42-pal-ordinary-even-ecs",
            "disp42-ntsc-ordinary-odd-alice",
            "disp42-ntsc-superhires-lace-exact-end-lisa",
            "disp42-null-ntsc-ehb-lace-aa-pair",
            "disp42-private-default-aa-pair",
            "disp42-pal-dpf-last-aligned-setaa",
            "disp42-ntsc-dpf2-first-even-ocs",
            "disp42-default-ham-setaa",
            "disp42-ntsc-superhires-setaa",
            "disp42-pal-superhires-ocs",
            "disp42-ntsc-superhires-ecs",
            "disp42-pal-superhires-dpf2-lace-aa-pair",
            "disp42-foreign-provider",
            "disp42-null-a6-control",
            "disp42-odd-a6-control",
            "disp42-exact-end-a6-control",
            "disp42-first-wrap-a6-control",
            "disp42-first-wrap-control",
            "disp42-superhires-first-wrap-control",
            "disp42-foreign-first-wrap-control",
            "disp40-ordinary-valid-control",
            "disp41-superhires-valid-control",
            "disp55-control",
            "disp56-control",
            "name42-control",
            "dims42-control",
            "mntr42-control",
            "unsupported42-control",
            "vec42-provider-control",
            "sentinel42-control"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    public static IEnumerable<object[]> DisplayFortyThreeByteCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "disp43-pal-ordinary-even-ecs",
            "disp43-ntsc-ordinary-odd-alice",
            "disp43-ntsc-superhires-lace-exact-end-lisa",
            "disp43-null-ntsc-ehb-lace-aa-pair",
            "disp43-private-default-aa-pair",
            "disp43-pal-dpf-last-aligned-setaa",
            "disp43-ntsc-dpf2-first-even-ocs",
            "disp43-default-ham-setaa",
            "disp43-ntsc-superhires-setaa",
            "disp43-pal-superhires-ocs",
            "disp43-ntsc-superhires-ecs",
            "disp43-pal-superhires-dpf2-lace-aa-pair",
            "disp43-foreign-provider",
            "disp43-null-a6-control",
            "disp43-odd-a6-control",
            "disp43-exact-end-a6-control",
            "disp43-first-wrap-a6-control",
            "disp43-first-wrap-control",
            "disp43-superhires-first-wrap-control",
            "disp43-foreign-first-wrap-control",
            "disp41-ordinary-valid-control",
            "disp42-superhires-valid-control",
            "disp56-control",
            "name43-control",
            "dims43-control",
            "mntr43-control",
            "unsupported43-control",
            "vec43-provider-control",
            "sentinel43-control"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }


    public static IEnumerable<object[]> DisplayReservedTailCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (uint prefixSize in Enumerable.Range(44, 12).Select(size => (uint)size))
        foreach (var scenario in new[]
        {
            "tail-pal-ordinary-even-ecs",
            "tail-ntsc-ordinary-odd-alice",
            "tail-ntsc-superhires-lace-exact-end-lisa",
            "tail-null-ntsc-ehb-lace-aa-pair",
            "tail-private-default-aa-pair",
            "tail-pal-dpf-last-aligned-setaa",
            "tail-ntsc-dpf2-first-even-ocs",
            "tail-default-ham-setaa",
            "tail-ntsc-superhires-setaa",
            "tail-pal-superhires-ocs",
            "tail-ntsc-superhires-ecs",
            "tail-pal-superhires-dpf2-lace-aa-pair",
            "tail-foreign-provider",
            "tail-null-a6-control",
            "tail-odd-a6-control",
            "tail-exact-end-a6-control",
            "tail-first-wrap-a6-control",
            "tail-first-wrap-control",
            "tail-superhires-first-wrap-control",
            "tail-foreign-first-wrap-control",
            "disp42-ordinary-valid-control",
            "disp43-superhires-valid-control",
            "disp56-control",
            "disp57-control",
            "name-tail-control",
            "dims-tail-control",
            "mntr-tail-control",
            "unsupported-tail-control",
            "vec-tail-provider-control",
            "sentinel-tail-control"
        })
            yield return new object[] { relocated, autoInitEntry, prefixSize, scenario };
    }

    public static IEnumerable<object[]> NamePayloadPrefixCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        {
            for (var requestedBytes = 17u; requestedBytes <= 55u; requestedBytes++)
            {
                yield return new object[]
                {
                    relocated,
                    autoInitEntry,
                    $"payload-{requestedBytes}",
                    requestedBytes
                };
            }

            foreach (var (scenario, requestedBytes) in new[]
            {
                ("exact-end-17", 17u),
                ("first-wrap-17", 17u),
                ("exact-end-55", 55u),
                ("first-wrap-55", 55u),
                ("mode-authority-47", 47u),
                ("private-default-48", 48u),
                ("final-canonical-49", 49u),
                ("foreign-provider-55", 55u),
                ("name-16-control", 16u),
                ("name-56-control", 56u),
                ("name-57-control", 57u)
            })
            {
                yield return new object[]
                {
                    relocated,
                    autoInitEntry,
                    scenario,
                    requestedBytes
                };
            }
        }
    }

    [Theory]
    [MemberData(nameof(DestinationAdmissionCases))]
    public void GetDisplayInfoDataAdmitsTheCompleteTagSelectedDestinationBeforeD7(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        using var fixture = new Fixture(relocated, autoInitEntry);
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var requestedBytes = (uint)GraphicsDisplayDatabase.DisplayInfoChunkSize;
        var destination = 2u;
        var outputBytes = 48;
        var handle = ModeId;
        var expectedOutputModeId = ModeId;
        uint? expectedResult = null;
        var admitted = true;

        switch (scenario)
        {
            case "unsupported-null":
                tag = UnsupportedTag;
                requestedBytes = 0x60;
                destination = 0;
                outputBytes = 0;
                expectedResult = 0;
                admitted = false;
                break;
            case "short-disp-null":
                requestedBytes =
                    (uint)GraphicsDisplayDatabase.DisplayInfoChunkSize - 1u;
                destination = 0;
                expectedResult = 0;
                admitted = false;
                break;
            case "disp-first-even":
                break;
            case "disp-null":
                destination = 0;
                expectedResult = 0;
                admitted = false;
                break;
            case "disp-odd":
                destination = 3;
                break;
            case "disp-exact-end":
                destination = LastBase(outputBytes);
                break;
            case "disp-first-wrap":
                destination = FirstEvenWrap(outputBytes);
                admitted = false;
                break;
            case "dims-exact-end":
                tag = GraphicsDisplayDatabase.DtagDims;
                requestedBytes = 0x58;
                outputBytes = 66;
                destination = LastBase(outputBytes);
                break;
            case "dims-first-wrap":
                tag = GraphicsDisplayDatabase.DtagDims;
                requestedBytes = 0x58;
                outputBytes = 66;
                destination = FirstEvenWrap(outputBytes);
                admitted = false;
                break;
            case "mntr-exact-end":
                tag = GraphicsDisplayDatabase.DtagMntr;
                requestedBytes = 0x60;
                outputBytes = 88;
                destination = LastBase(outputBytes);
                handle = GraphicsDisplayDatabase.DefaultModeHandle;
                expectedOutputModeId = GraphicsModeIds.DefaultMonitor;
                break;
            case "mntr-first-wrap":
                tag = GraphicsDisplayDatabase.DtagMntr;
                requestedBytes = 0x60;
                outputBytes = 88;
                destination = FirstEvenWrap(outputBytes);
                handle = GraphicsDisplayDatabase.DefaultModeHandle;
                expectedOutputModeId = GraphicsModeIds.DefaultMonitor;
                admitted = false;
                break;
            case "name-final-even":
                tag = GraphicsDisplayDatabase.DtagName;
                destination = 0xFFFF_FFFEu;
                admitted = false;
                break;
            case "name-final-odd":
                tag = GraphicsDisplayDatabase.DtagName;
                destination = uint.MaxValue;
                admitted = false;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario.EndsWith("exact-end", StringComparison.Ordinal))
        {
            Assert.Equal(0u, destination & 1u);
            Assert.Equal(
                (ulong)uint.MaxValue,
                (ulong)destination + (uint)outputBytes - 1u);
        }
        if (scenario.EndsWith("first-wrap", StringComparison.Ordinal))
        {
            Assert.Equal(0u, destination & 1u);
            Assert.True(
                (ulong)destination + (uint)outputBytes - 1u > uint.MaxValue);
        }

        if (tag == GraphicsDisplayDatabase.DtagMntr)
            fixture.SeedDefaultMonitor(0x0060_0000);
        var returnedBytes = expectedResult ?? (admitted ? (uint)outputBytes : requestedBytes);
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(
            handle,
            destination,
            requestedBytes,
            tag,
            modeId: ModeId);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();

        Check(failures, "result and native provenance", () =>
        {
            Assert.Equal(returnedBytes, result.Data0);
            Assert.False(result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "caller PC/SP, A1, and A6", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
        });
        if (admitted)
        {
            Check(failures, "only the admitted output span changes", () =>
                fixture.AssertOnlyOutputSpanChanged(
                    before,
                    after,
                    destination,
                    outputBytes,
                    tag,
                    expectedOutputModeId));
        }
        else
        {
            Check(failures, "all output and guard memory remains unchanged", () =>
                AssertMemoryEqual(before, after));
            Check(failures, "pre-admission preserves D2-D7/A2-A6", () =>
                Assert.True(
                    result.RegisterDifferences.Length == 0,
                    string.Join("; ", result.RegisterDifferences)));
        }

        Assert.True(
            failures.Count == 0,
            $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(ModeAdmissionCases))]
    public void GetDisplayInfoDataAdmitsTheAuthoritativeCanonicalModeBeforeD7(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const uint foreignMode = 0xDEAD_BEEFu;
        var otherCanonicalMode = GraphicsModeIds.PalMonitor | GraphicsModeIds.LoresKey;
        var finalCanonicalMode =
            GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteLaceKey;
        using var fixture = new Fixture(relocated, autoInitEntry);
        var handle = 0u;
        var modeId = GraphicsModeIds.DefaultMonitor;
        var selectedMode = GraphicsModeIds.DefaultMonitor;
        var tag = GraphicsDisplayDatabase.DtagName;
        var requestedBytes = (uint)GraphicsDisplayDatabase.DisplayInfoChunkSize;
        var outputBytes = GraphicsDisplayDatabase.DisplayInfoChunkSize;
        var admitted = true;

        switch (scenario)
        {
            case "null-handle-default":
                break;
            case "matching-public-handle":
                handle = ModeId;
                modeId = ModeId;
                selectedMode = ModeId;
                break;
            case "public-handle-overrides-canonical-d2":
                handle = ModeId;
                modeId = otherCanonicalMode;
                selectedMode = ModeId;
                break;
            case "final-public-handle-overrides-foreign-d2":
                handle = finalCanonicalMode;
                modeId = foreignMode;
                selectedMode = finalCanonicalMode;
                break;
            case "private-default-handle-overrides-foreign-d2":
                handle = GraphicsDisplayDatabase.DefaultModeHandle;
                modeId = foreignMode;
                break;
            case "null-handle-foreign-mode":
                modeId = foreignMode;
                admitted = false;
                break;
            case "foreign-handle-overrides-canonical-d2":
                handle = foreignMode;
                modeId = ModeId;
                admitted = false;
                break;
            case "matching-foreign-handle":
                handle = foreignMode;
                modeId = foreignMode;
                admitted = false;
                break;
            case "nondefault-monitor":
                modeId = ModeId;
                selectedMode = ModeId;
                tag = GraphicsDisplayDatabase.DtagMntr;
                requestedBytes = 0x60;
                outputBytes = 88;
                admitted = false;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        const uint destination = 0x200;
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(handle, destination, requestedBytes, tag, modeId);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();

        Check(failures, "result and native provenance", () =>
        {
            Assert.Equal(requestedBytes, result.Data0);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "caller PC/SP, A1, and A6", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
        });
        if (admitted)
        {
            Check(failures, "the authoritative mode owns the output", () =>
                fixture.AssertOnlyOutputSpanChanged(
                    before,
                    after,
                    destination,
                    outputBytes,
                    tag,
                    selectedMode));
            Check(failures, "pre-frame admission preserves D2-D6/A2-A6", () =>
            {
                var unexpected = result.RegisterDifferences
                    .Where(difference =>
                        !difference.StartsWith("D7 ", StringComparison.Ordinal))
                    .ToArray();
                Assert.True(unexpected.Length == 0, string.Join("; ", unexpected));
            });
        }
        else
        {
            Check(failures, "decline leaves every output and guard byte intact", () =>
                AssertMemoryEqual(before, after));
            Check(failures, "pre-admission decline preserves D2-D7/A2-A6", () =>
                Assert.True(
                    result.RegisterDifferences.Length == 0,
                    string.Join("; ", result.RegisterDifferences)));
        }

        Assert.True(
            failures.Count == 0,
            $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(CapabilityAdmissionCases))]
    public void GetDisplayInfoDataAdmitsTheCapabilityByteBeforeD7(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        using var fixture = new Fixture(relocated, autoInitEntry);
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var requestedBytes = (uint)GraphicsDisplayDatabase.DisplayInfoChunkSize;
        var outputBytes = GraphicsDisplayDatabase.DisplayInfoChunkSize;
        var graphicsBase = fixture.GraphicsBase;
        var admitted = true;
        var expectedCapabilityReads = 1;
        var maximumBase = uint.MaxValue - (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        var lastAlignedBase = maximumBase & ~1u;

        switch (scenario)
        {
            case "name-null":
                tag = GraphicsDisplayDatabase.DtagName;
                graphicsBase = 0;
                expectedCapabilityReads = 0;
                break;
            case "name-odd":
                tag = GraphicsDisplayDatabase.DtagName;
                graphicsBase = 3;
                expectedCapabilityReads = 0;
                break;
            case "name-final":
                tag = GraphicsDisplayDatabase.DtagName;
                graphicsBase = uint.MaxValue;
                expectedCapabilityReads = 0;
                break;
            case "disp-library-base":
                break;
            case "disp-first-even":
                graphicsBase = 2;
                break;
            case "disp-last-aligned":
                graphicsBase = lastAlignedBase;
                break;
            case "disp-null":
                graphicsBase = 0;
                admitted = false;
                expectedCapabilityReads = 0;
                break;
            case "disp-odd":
                graphicsBase = 3;
                admitted = false;
                expectedCapabilityReads = 0;
                break;
            case "disp-exact-end":
                graphicsBase = maximumBase;
                admitted = false;
                expectedCapabilityReads = 0;
                break;
            case "disp-first-wrap":
                graphicsBase = maximumBase + 1u;
                admitted = false;
                expectedCapabilityReads = 0;
                break;
            case "disp-final-even":
                graphicsBase = 0xFFFF_FFFEu;
                admitted = false;
                expectedCapabilityReads = 0;
                break;
            case "disp-final-odd":
                graphicsBase = uint.MaxValue;
                admitted = false;
                expectedCapabilityReads = 0;
                break;
            case "dims-library-base":
                tag = GraphicsDisplayDatabase.DtagDims;
                requestedBytes = 0x58;
                outputBytes = 66;
                break;
            case "dims-last-aligned":
                tag = GraphicsDisplayDatabase.DtagDims;
                requestedBytes = 0x58;
                outputBytes = 66;
                graphicsBase = lastAlignedBase;
                break;
            case "dims-null":
                tag = GraphicsDisplayDatabase.DtagDims;
                requestedBytes = 0x58;
                outputBytes = 66;
                graphicsBase = 0;
                admitted = false;
                expectedCapabilityReads = 0;
                break;
            case "dims-first-wrap":
                tag = GraphicsDisplayDatabase.DtagDims;
                requestedBytes = 0x58;
                outputBytes = 66;
                graphicsBase = maximumBase + 1u;
                admitted = false;
                expectedCapabilityReads = 0;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        Assert.Equal(0u, lastAlignedBase & 1u);
        Assert.Equal(
            (ulong)uint.MaxValue - 1u,
            (ulong)lastAlignedBase + (uint)GraphicsLayouts.GfxBaseChipRevBits0);
        if (scenario == "disp-exact-end")
        {
            Assert.Equal(1u, graphicsBase & 1u);
            Assert.Equal(
                (ulong)uint.MaxValue,
                (ulong)graphicsBase + (uint)GraphicsLayouts.GfxBaseChipRevBits0);
        }
        if (scenario.EndsWith("first-wrap", StringComparison.Ordinal))
        {
            Assert.Equal(0u, graphicsBase & 1u);
            Assert.True(
                (ulong)graphicsBase + (uint)GraphicsLayouts.GfxBaseChipRevBits0 >
                uint.MaxValue);
        }

        fixture.SeedChipRevision(graphicsBase, (byte)GraphicsChipRevision.SetEcs);
        const uint destination = 0x200;
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(
            ModeId,
            destination,
            requestedBytes,
            tag,
            ModeId,
            graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        var expectedObservableReads = expectedCapabilityReads;

        if (tag == GraphicsDisplayDatabase.DtagDisp) outputBytes = 48;

        Check(failures, "result and native provenance", () =>
        {
            Assert.Equal(admitted ? (uint)outputBytes : requestedBytes, result.Data0);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "capability-byte read count", () =>
            Assert.True(
                result.CapabilityReadCount == expectedObservableReads,
                $"Expected {expectedObservableReads}, actual " +
                $"{result.CapabilityReadCount}; CPU byte reads: " +
                string.Join(", ", result.CpuByteDataReadAddresses.Select(
                    address => $"{address:X8}"))));
        Check(failures, "caller PC/SP, A1, and supplied A6", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(graphicsBase, result.Address6);
        });
        if (admitted)
        {
            Check(failures, "the admitted output span is complete", () =>
                fixture.AssertOnlyOutputSpanChanged(
                    before,
                    after,
                    destination,
                    outputBytes,
                    tag,
                    ModeId));
            Check(failures, "pre-frame admission changes only D6/D7", () =>
            {
                var unexpected = result.RegisterDifferences
                    .Where(difference =>
                        !difference.StartsWith("D6 ", StringComparison.Ordinal) &&
                        !difference.StartsWith("D7 ", StringComparison.Ordinal))
                    .ToArray();
                Assert.True(unexpected.Length == 0, string.Join("; ", unexpected));
            });
        }
        else
        {
            Check(failures, "decline leaves every output and guard byte intact", () =>
                AssertMemoryEqual(before, after));
            Check(failures, "pre-admission decline preserves D2-D7/A2-A6", () =>
                Assert.True(
                    result.RegisterDifferences.Length == 0,
                    string.Join("; ", result.RegisterDifferences)));
        }

        Assert.True(
            failures.Count == 0,
            $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(DefaultMonitorAdmissionCases))]
    public void GetDisplayInfoDataAdmitsTheDefaultMonitorFieldBeforeD7(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const uint residentMonitor = 0x0060_0000;
        using var fixture = new Fixture(relocated, autoInitEntry);
        var tag = GraphicsDisplayDatabase.DtagMntr;
        var requestedBytes = 0x60u;
        var outputBytes = 88;
        var handle = GraphicsDisplayDatabase.DefaultModeHandle;
        var modeId = GraphicsModeIds.DefaultMonitor;
        var selectedMode = GraphicsModeIds.DefaultMonitor;
        var graphicsBase = fixture.GraphicsBase;
        var residentPointer = residentMonitor;
        var admitted = true;
        var expectedMonitorReads = 1;
        var maximumBase = uint.MaxValue -
                          ((uint)GraphicsLayouts.GfxBaseDefaultMonitor + 3u);
        var firstWrappingBase = maximumBase + 2u;

        switch (scenario)
        {
            case "mntr-library-base":
                break;
            case "mntr-first-even":
                graphicsBase = 2;
                break;
            case "mntr-last-base":
                graphicsBase = maximumBase;
                break;
            case "mntr-null":
                graphicsBase = 0;
                admitted = false;
                expectedMonitorReads = 0;
                break;
            case "mntr-odd":
                graphicsBase = 3;
                admitted = false;
                expectedMonitorReads = 0;
                break;
            case "mntr-first-wrap":
                graphicsBase = firstWrappingBase;
                admitted = false;
                expectedMonitorReads = 0;
                break;
            case "mntr-final-even":
                graphicsBase = 0xFFFF_FFFEu;
                admitted = false;
                expectedMonitorReads = 0;
                break;
            case "mntr-final-odd":
                graphicsBase = uint.MaxValue;
                admitted = false;
                expectedMonitorReads = 0;
                break;
            case "mntr-missing-library":
                residentPointer = 0;
                admitted = false;
                break;
            case "mntr-missing-first-even":
                graphicsBase = 2;
                residentPointer = 0;
                admitted = false;
                break;
            case "mntr-missing-last-base":
                graphicsBase = maximumBase;
                residentPointer = 0;
                admitted = false;
                break;
            case "nondefault-mntr-null":
                handle = ModeId;
                modeId = ModeId;
                selectedMode = ModeId;
                graphicsBase = 0;
                admitted = false;
                expectedMonitorReads = 0;
                break;
            case "nondefault-mntr-final":
                handle = ModeId;
                modeId = ModeId;
                selectedMode = ModeId;
                graphicsBase = uint.MaxValue;
                admitted = false;
                expectedMonitorReads = 0;
                break;
            case "name-final":
                tag = GraphicsDisplayDatabase.DtagName;
                requestedBytes = (uint)GraphicsDisplayDatabase.DisplayInfoChunkSize;
                outputBytes = GraphicsDisplayDatabase.DisplayInfoChunkSize;
                handle = ModeId;
                modeId = ModeId;
                selectedMode = ModeId;
                graphicsBase = uint.MaxValue;
                expectedMonitorReads = 0;
                break;
            case "disp-library-base":
                tag = GraphicsDisplayDatabase.DtagDisp;
                requestedBytes = (uint)GraphicsDisplayDatabase.DisplayInfoChunkSize;
                outputBytes = GraphicsDisplayDatabase.DisplayInfoChunkSize;
                handle = ModeId;
                modeId = ModeId;
                selectedMode = ModeId;
                expectedMonitorReads = 0;
                break;
            case "dims-last-aligned":
                tag = GraphicsDisplayDatabase.DtagDims;
                requestedBytes = 0x58;
                outputBytes = 66;
                handle = ModeId;
                modeId = ModeId;
                selectedMode = ModeId;
                graphicsBase = (uint.MaxValue -
                                (uint)GraphicsLayouts.GfxBaseChipRevBits0) & ~1u;
                expectedMonitorReads = 0;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        Assert.Equal(0u, maximumBase & 1u);
        Assert.Equal(
            (ulong)uint.MaxValue,
            (ulong)maximumBase + (uint)GraphicsLayouts.GfxBaseDefaultMonitor + 3u);
        Assert.Equal(0u, firstWrappingBase & 1u);
        Assert.True(
            (ulong)firstWrappingBase +
            (uint)GraphicsLayouts.GfxBaseDefaultMonitor + 3u > uint.MaxValue);

        if (tag == GraphicsDisplayDatabase.DtagMntr &&
            selectedMode == GraphicsModeIds.DefaultMonitor)
        {
            fixture.SeedDefaultMonitor(graphicsBase, residentPointer);
        }
        else if (tag is GraphicsDisplayDatabase.DtagDisp or
                 GraphicsDisplayDatabase.DtagDims)
        {
            fixture.SeedChipRevision(
                graphicsBase,
                (byte)GraphicsChipRevision.SetEcs);
        }

        const uint destination = 0x280;
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(
            handle,
            destination,
            requestedBytes,
            tag,
            modeId,
            graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        var expectedObservableReads = expectedMonitorReads;

        if (tag == GraphicsDisplayDatabase.DtagDisp) outputBytes = 48;

        Check(failures, "result and native provenance", () =>
        {
            Assert.Equal(admitted ? (uint)outputBytes : requestedBytes, result.Data0);
            Assert.False(result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "DefaultMonitor LONG read count", () =>
            Assert.True(
                result.DefaultMonitorReadCount == expectedObservableReads,
                $"Expected {expectedObservableReads}, actual " +
                $"{result.DefaultMonitorReadCount}; CPU data reads: " +
                string.Join(", ", result.CpuDataReadAddresses.Select(
                    access => $"{access.Size}@{access.Address:X8}"))));
        Check(failures, "caller PC/SP, A1, and supplied A6", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(graphicsBase, result.Address6);
        });
        if (admitted)
        {
            Check(failures, "the admitted output span is complete", () =>
                fixture.AssertOnlyOutputSpanChanged(
                    before,
                    after,
                    destination,
                    outputBytes,
                    tag,
                    selectedMode));
            Check(failures, "pre-frame admission changes only D6/D7", () =>
            {
                var unexpected = result.RegisterDifferences
                    .Where(difference =>
                        !difference.StartsWith("D6 ", StringComparison.Ordinal) &&
                        !difference.StartsWith("D7 ", StringComparison.Ordinal))
                    .ToArray();
                Assert.True(unexpected.Length == 0, string.Join("; ", unexpected));
            });
        }
        else
        {
            Check(failures, "decline leaves every output and guard byte intact", () =>
                AssertMemoryEqual(before, after));
            Check(failures, "pre-admission decline preserves D2-D7/A2-A6", () =>
                Assert.True(
                    result.RegisterDifferences.Length == 0,
                    string.Join("; ", result.RegisterDifferences)));
        }

        Assert.True(
            failures.Count == 0,
            $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(PublicFrameCases))]
    public void GetDisplayInfoDataRestoresTheAdmittedPublicFrame(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const uint foreignMode = 0xDEAD_BEEFu;
        const uint residentMonitor = 0x0060_0000;
        var finalCanonicalMode =
            GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteLaceKey;
        using var fixture = new Fixture(relocated, autoInitEntry);
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var requestedBytes = (uint)GraphicsDisplayDatabase.DisplayInfoChunkSize;
        var outputBytes = GraphicsDisplayDatabase.DisplayInfoChunkSize;
        var handle = GraphicsDisplayDatabase.DefaultModeHandle;
        var modeId = GraphicsModeIds.DefaultMonitor;
        var selectedMode = GraphicsModeIds.DefaultMonitor;
        var graphicsBase = fixture.GraphicsBase;
        var chipRevision = (byte)GraphicsChipRevision.SetEcs;
        var admitted = true;
        var seedCapability = true;
        var seedMonitor = false;
        var monitorPointer = residentMonitor;

        switch (scenario)
        {
            case "disp-default-ecs":
                break;
            case "disp-pal-hires-ecs":
                handle = ModeId;
                modeId = ModeId;
                selectedMode = ModeId;
                break;
            case "disp-ntsc-final-aga":
                handle = finalCanonicalMode;
                modeId = finalCanonicalMode;
                selectedMode = finalCanonicalMode;
                chipRevision = (byte)GraphicsChipRevision.SetAa;
                break;
            case "dims-default-ecs":
                tag = GraphicsDisplayDatabase.DtagDims;
                requestedBytes = 0x58;
                outputBytes = 66;
                break;
            case "dims-pal-hires-aga":
                tag = GraphicsDisplayDatabase.DtagDims;
                requestedBytes = 0x58;
                outputBytes = 66;
                handle = ModeId;
                modeId = ModeId;
                selectedMode = ModeId;
                chipRevision = (byte)GraphicsChipRevision.SetAa;
                break;
            case "dims-ntsc-final-ecs":
                tag = GraphicsDisplayDatabase.DtagDims;
                requestedBytes = 0x58;
                outputBytes = 66;
                handle = finalCanonicalMode;
                modeId = finalCanonicalMode;
                selectedMode = finalCanonicalMode;
                break;
            case "name-default-invalid-a6":
                tag = GraphicsDisplayDatabase.DtagName;
                graphicsBase = uint.MaxValue;
                seedCapability = false;
                break;
            case "name-ntsc-final-invalid-a6":
                tag = GraphicsDisplayDatabase.DtagName;
                handle = finalCanonicalMode;
                modeId = finalCanonicalMode;
                selectedMode = finalCanonicalMode;
                graphicsBase = uint.MaxValue;
                seedCapability = false;
                break;
            case "mntr-default-resident":
                tag = GraphicsDisplayDatabase.DtagMntr;
                requestedBytes = 0x60;
                outputBytes = 88;
                seedCapability = false;
                seedMonitor = true;
                break;
            case "foreign-name":
                tag = GraphicsDisplayDatabase.DtagName;
                handle = foreignMode;
                modeId = foreignMode;
                selectedMode = foreignMode;
                graphicsBase = uint.MaxValue;
                admitted = false;
                seedCapability = false;
                break;
            case "nondefault-mntr-null":
                tag = GraphicsDisplayDatabase.DtagMntr;
                requestedBytes = 0x60;
                outputBytes = 88;
                handle = ModeId;
                modeId = ModeId;
                selectedMode = ModeId;
                graphicsBase = 0;
                admitted = false;
                seedCapability = false;
                break;
            case "malformed-disp-null-a6":
                graphicsBase = 0;
                admitted = false;
                seedCapability = false;
                break;
            case "missing-mntr-resident":
                tag = GraphicsDisplayDatabase.DtagMntr;
                requestedBytes = 0x60;
                outputBytes = 88;
                admitted = false;
                seedCapability = false;
                seedMonitor = true;
                monitorPointer = 0;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (seedCapability)
            fixture.SeedChipRevision(graphicsBase, chipRevision);
        if (seedMonitor)
            fixture.SeedDefaultMonitor(graphicsBase, monitorPointer);

        if (tag == GraphicsDisplayDatabase.DtagDisp) outputBytes = 48;

        const uint destination = 0x300;
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(
            handle,
            destination,
            requestedBytes,
            tag,
            modeId,
            graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();

        Check(failures, "result and native provenance", () =>
        {
            Assert.Equal(admitted ? (uint)outputBytes : requestedBytes, result.Data0);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "caller PC/SP, A1, and supplied A6", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(graphicsBase, result.Address6);
        });
        Check(failures, "D2-D7/A2-A6 public frame", () =>
            Assert.True(
                result.RegisterDifferences.Length == 0,
                string.Join("; ", result.RegisterDifferences)));
        if (admitted)
        {
            Check(failures, "the admitted output span is complete", () =>
                fixture.AssertOnlyOutputSpanChanged(
                    before,
                    after,
                    destination,
                    outputBytes,
                    tag,
                    selectedMode));
            if (scenario == "disp-ntsc-final-aga")
            {
                Check(failures, "AGA display component precision", () =>
                    Assert.Equal((byte)8, fixture.ReadOutputByte(destination + 0x28)));
            }
            if (scenario == "dims-pal-hires-aga")
            {
                Check(failures, "AGA dimensions depth", () =>
                    Assert.Equal((ushort)8, fixture.ReadOutputWord(destination + 0x10)));
            }
        }
        else
        {
            Check(failures, "decline leaves every output and guard byte intact", () =>
                AssertMemoryEqual(before, after));
        }

        Assert.True(
            failures.Count == 0,
            $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(InvalidModeZeroQueryCases))]
    public void GetDisplayInfoDataOwnsTheNullHandleInvalidIdZeroQueryBeforeTagClassification(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const uint foreignMode = 0xDEAD_BEEFu;
        using var fixture = new Fixture(relocated, autoInitEntry);
        var handle = 0u;
        var destination = 0x300u;
        var requestedBytes = (uint)GraphicsDisplayDatabase.DisplayInfoChunkSize;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = GraphicsModeIds.Invalid;
        var expectedResult = 0u;
        var expectedFallback = false;
        var expectedOutputBytes = 0;
        var expectedOutputMode = GraphicsModeIds.Invalid;
        var graphicsBase = uint.MaxValue;

        switch (scenario)
        {
            case "invalid-disp-full":
                break;
            case "invalid-name-full":
                tag = GraphicsDisplayDatabase.DtagName;
                break;
            case "invalid-mntr-full":
                tag = GraphicsDisplayDatabase.DtagMntr;
                requestedBytes = 0x60;
                break;
            case "invalid-unsupported-full":
                tag = UnsupportedTag;
                break;
            case "invalid-vec-full":
                tag = GraphicsDisplayDatabase.DtagVec;
                break;
            case "invalid-null-destination":
                destination = 0;
                break;
            case "invalid-odd-destination":
                destination++;
                break;
            case "invalid-short-disp":
                requestedBytes = 0x30;
                break;
            case "invalid-zero-size":
                tag = GraphicsDisplayDatabase.DtagMntr;
                requestedBytes = 0;
                break;
            case "canonical-handle-invalid-d2":
                handle = ModeId;
                tag = GraphicsDisplayDatabase.DtagName;
                expectedResult = requestedBytes;
                expectedOutputBytes = GraphicsDisplayDatabase.DisplayInfoChunkSize;
                expectedOutputMode = ModeId;
                break;
            case "private-handle-invalid-d2":
                handle = GraphicsDisplayDatabase.DefaultModeHandle;
                tag = GraphicsDisplayDatabase.DtagName;
                expectedResult = requestedBytes;
                expectedOutputBytes = GraphicsDisplayDatabase.DisplayInfoChunkSize;
                expectedOutputMode = GraphicsModeIds.DefaultMonitor;
                break;
            case "foreign-handle-invalid-d2":
                handle = foreignMode;
                tag = GraphicsDisplayDatabase.DtagName;
                expectedResult = requestedBytes;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(
            handle,
            destination,
            requestedBytes,
            tag,
            modeId,
            graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();

        Check(failures, "result and native/provider provenance", () =>
        {
            Assert.Equal(expectedResult, result.Data0);
            Assert.Equal(expectedFallback, result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "caller PC/SP, A1, and supplied A6", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(graphicsBase, result.Address6);
        });
        Check(failures, "D2-D7/A2-A6 public frame", () =>
            Assert.True(
                result.RegisterDifferences.Length == 0,
                string.Join("; ", result.RegisterDifferences)));

        if (expectedOutputBytes != 0)
        {
            Check(failures, "authoritative non-null handle remains an ordinary query", () =>
                fixture.AssertOnlyOutputSpanChanged(
                    before,
                    after,
                    destination,
                    expectedOutputBytes,
                    tag,
                    expectedOutputMode));
        }
        else
        {
            Check(failures, "zero query or provider decline leaves memory intact", () =>
                AssertMemoryEqual(before, after));
        }

        if (handle == 0 && modeId == GraphicsModeIds.Invalid)
        {
            Check(failures, "NULL/INVALID_ID never probes the destination", () =>
            {
                var physicalStart = destination & 0x00FF_FFFFu;
                var physicalLength = Math.Max(0x60u, requestedBytes);
                var physicalEnd = physicalStart + physicalLength - 1u;
                Assert.DoesNotContain(
                    result.CpuDataReadAddresses,
                    access =>
                    {
                        var physical = access.Address & 0x00FF_FFFFu;
                        return physical >= physicalStart && physical <= physicalEnd;
                    });
                Assert.DoesNotContain(
                    result.CpuDataWriteAddresses,
                    access =>
                    {
                        var physical = access.Address & 0x00FF_FFFFu;
                        return physical >= physicalStart && physical <= physicalEnd;
                    });
            });
            Check(failures, "NULL/INVALID_ID never reaches A6 capability state", () =>
            {
                Assert.Equal(0, result.CapabilityReadCount);
                Assert.Equal(0, result.DefaultMonitorReadCount);
            });
        }

        Assert.True(
            failures.Count == 0,
            $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(EmptyQueryOwnershipCases))]
    public void GetDisplayInfoDataResolvesProviderOwnershipBeforeOrdinaryEmptyQueries(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        using var fixture = new Fixture(relocated, autoInitEntry);
        var handle = ModeId;
        var destination = 0x300u;
        var requestedBytes = 0u;
        var tag = GraphicsDisplayDatabase.DtagName;
        var modeId = foreignMode;
        var graphicsBase = uint.MaxValue;
        var expectedResult = 0u;
        ushort? expectedReturnPredecessorOpcode = null;
        var expectedOutputBytes = 0;
        var expectedOutputMode = ModeId;

        switch (scenario)
        {
            case "null-canonical-disp":
                destination = 0;
                requestedBytes = (uint)GraphicsDisplayDatabase.DisplayInfoChunkSize;
                tag = GraphicsDisplayDatabase.DtagDisp;
                expectedReturnPredecessorOpcode = MoveQuickZeroD0Opcode;
                break;
            case "null-foreign-handle-disp":
                handle = foreignMode;
                destination = 0;
                requestedBytes = (uint)GraphicsDisplayDatabase.DisplayInfoChunkSize;
                tag = GraphicsDisplayDatabase.DtagDisp;
                modeId = ModeId;
                expectedResult = requestedBytes;
                expectedReturnPredecessorOpcode = BranchAlwaysWordOpcode;
                break;
            case "zero-foreign-mode-name":
                handle = 0;
                expectedReturnPredecessorOpcode = BranchAlwaysWordOpcode;
                break;
            case "null-canonical-unsupported":
                destination = 0;
                requestedBytes = 0x60;
                tag = UnsupportedTag;
                expectedReturnPredecessorOpcode = MoveQuickZeroD0Opcode;
                break;
            case "zero-foreign-mode-unsupported":
                handle = 0;
                tag = UnsupportedTag;
                expectedReturnPredecessorOpcode = BranchAlwaysWordOpcode;
                break;
            case "null-canonical-vec":
                destination = 0;
                requestedBytes = 0x60;
                tag = GraphicsDisplayDatabase.DtagVec;
                expectedResult = requestedBytes;
                expectedReturnPredecessorOpcode = BranchAlwaysWordOpcode;
                break;
            case "zero-canonical-vec":
                tag = GraphicsDisplayDatabase.DtagVec;
                expectedReturnPredecessorOpcode = BranchAlwaysWordOpcode;
                break;
            case "null-nondefault-mntr":
                handle = 0;
                destination = 0;
                requestedBytes = 0x60;
                tag = GraphicsDisplayDatabase.DtagMntr;
                modeId = ModeId;
                expectedReturnPredecessorOpcode = MoveQuickZeroD0Opcode;
                break;
            case "null-private-default-handle":
                handle = GraphicsDisplayDatabase.DefaultModeHandle;
                destination = 0;
                requestedBytes = (uint)GraphicsDisplayDatabase.DisplayInfoChunkSize;
                expectedReturnPredecessorOpcode = MoveQuickZeroD0Opcode;
                break;
            case "zero-canonical-name":
                break;
            case "zero-nondefault-mntr":
                handle = 0;
                tag = GraphicsDisplayDatabase.DtagMntr;
                modeId = ModeId;
                break;
            case "sentinel-vec":
                handle = 0;
                requestedBytes = 0x60;
                tag = GraphicsDisplayDatabase.DtagVec;
                modeId = GraphicsModeIds.Invalid;
                expectedReturnPredecessorOpcode = MoveQuickZeroD0Opcode;
                break;
            case "nonempty-canonical-name":
                requestedBytes = (uint)GraphicsDisplayDatabase.DisplayInfoChunkSize;
                expectedResult = requestedBytes;
                expectedReturnPredecessorOpcode = AddQuickFourLongA7Opcode;
                expectedOutputBytes = GraphicsDisplayDatabase.DisplayInfoChunkSize;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(
            handle,
            destination,
            requestedBytes,
            tag,
            modeId,
            graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();

        Check(failures, "result and native/provider terminal", () =>
        {
            Assert.Equal(expectedResult, result.Data0);
            Assert.Equal(1, result.NativeReturnCount);
            if (expectedReturnPredecessorOpcode.HasValue)
            {
                Assert.Equal(
                    expectedReturnPredecessorOpcode.Value,
                    result.NativeReturnPredecessorOpcode);
            }
        });
        Check(failures, "caller PC/SP, A1, and supplied A6", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(graphicsBase, result.Address6);
        });
        Check(failures, "D2-D7/A2-A6 public frame", () =>
            Assert.True(
                result.RegisterDifferences.Length == 0,
                string.Join("; ", result.RegisterDifferences)));

        if (expectedOutputBytes != 0)
        {
            Check(failures, "ordinary nonempty publication remains intact", () =>
                fixture.AssertOnlyOutputSpanChanged(
                    before,
                    after,
                    destination,
                    expectedOutputBytes,
                    tag,
                    expectedOutputMode));
        }
        else
        {
            Check(failures, "empty query or provider decline leaves memory intact", () =>
                AssertMemoryEqual(before, after));
            Check(failures, "empty query or provider decline never probes A1", () =>
            {
                var physicalStart = destination & 0x00FF_FFFFu;
                var physicalLength = Math.Max(0x60u, requestedBytes);
                var physicalEnd = physicalStart + physicalLength - 1u;
                Assert.DoesNotContain(
                    result.CpuDataReadAddresses,
                    access =>
                    {
                        var physical = access.Address & 0x00FF_FFFFu;
                        return physical >= physicalStart && physical <= physicalEnd;
                    });
                Assert.DoesNotContain(
                    result.CpuDataWriteAddresses,
                    access =>
                    {
                        var physical = access.Address & 0x00FF_FFFFu;
                        return physical >= physicalStart && physical <= physicalEnd;
                    });
            });
            Check(failures, "empty query or provider decline never reaches A6 state", () =>
            {
                Assert.Equal(0, result.CapabilityReadCount);
                Assert.Equal(0, result.DefaultMonitorReadCount);
            });
        }

        Assert.True(
            failures.Count == 0,
            $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(UnsupportedTagOwnershipCases))]
    public void GetDisplayInfoDataResolvesModeBeforeNonemptyUnsupportedPublicTags(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        var finalCanonicalMode =
            GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteLaceKey;
        using var fixture = new Fixture(relocated, autoInitEntry);
        var handle = ModeId;
        var destination = 0x301u;
        var requestedBytes = 0x60u;
        var tag = UnsupportedTag;
        var modeId = foreignMode;
        var expectedResult = 0u;
        ushort? expectedReturnPredecessorOpcode = MoveQuickZeroD0Opcode;
        var expectedOutputBytes = 0;
        var expectedOutputMode = ModeId;

        switch (scenario)
        {
            case "canonical-handle-foreign-d2":
                break;
            case "canonical-d2-tag-zero":
                handle = 0;
                tag = 0;
                modeId = ModeId;
                break;
            case "private-default-vec-plus-one":
                handle = GraphicsDisplayDatabase.DefaultModeHandle;
                tag = GraphicsDisplayDatabase.DtagVec + 1u;
                expectedOutputMode = GraphicsModeIds.DefaultMonitor;
                break;
            case "final-canonical-high-tag":
                handle = 0;
                tag = 0x7FFF_FFFFu;
                modeId = finalCanonicalMode;
                expectedOutputMode = finalCanonicalMode;
                break;
            case "foreign-handle-canonical-d2":
                handle = foreignMode;
                modeId = ModeId;
                expectedResult = requestedBytes;
                expectedReturnPredecessorOpcode = null;
                break;
            case "foreign-d2":
                handle = 0;
                expectedResult = requestedBytes;
                expectedReturnPredecessorOpcode = null;
                break;
            case "exact-vec":
                tag = GraphicsDisplayDatabase.DtagVec;
                expectedResult = requestedBytes;
                expectedReturnPredecessorOpcode = null;
                break;
            case "supported-name":
                destination = 0x300u;
                requestedBytes = (uint)GraphicsDisplayDatabase.DisplayInfoChunkSize;
                tag = GraphicsDisplayDatabase.DtagName;
                expectedResult = requestedBytes;
                expectedReturnPredecessorOpcode = AddQuickFourLongA7Opcode;
                expectedOutputBytes = GraphicsDisplayDatabase.DisplayInfoChunkSize;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(
            handle,
            destination,
            requestedBytes,
            tag,
            modeId,
            graphicsBase: uint.MaxValue);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();

        Check(failures, "result and native/provider terminal", () =>
        {
            Assert.Equal(expectedResult, result.Data0);
            Assert.Equal(1, result.NativeReturnCount);
            if (expectedReturnPredecessorOpcode.HasValue)
            {
                Assert.Equal(
                    expectedReturnPredecessorOpcode.Value,
                    result.NativeReturnPredecessorOpcode);
            }
        });
        Check(failures, "caller PC/SP, A1, and supplied A6", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(uint.MaxValue, result.Address6);
        });
        Check(failures, "D2-D7/A2-A6 public frame", () =>
            Assert.True(
                result.RegisterDifferences.Length == 0,
                string.Join("; ", result.RegisterDifferences)));

        if (expectedOutputBytes != 0)
        {
            Check(failures, "supported nonempty publication remains intact", () =>
                fixture.AssertOnlyOutputSpanChanged(
                    before,
                    after,
                    destination,
                    expectedOutputBytes,
                    tag,
                    expectedOutputMode));
        }
        else
        {
            Check(failures, "unknown tag or provider decline leaves memory intact", () =>
                AssertMemoryEqual(before, after));
            Check(failures, "unknown tag or provider decline never probes A1", () =>
            {
                var physicalStart = destination & 0x00FF_FFFFu;
                var physicalEnd = physicalStart + 0x5Fu;
                Assert.DoesNotContain(
                    result.CpuDataReadAddresses,
                    access =>
                    {
                        var physical = access.Address & 0x00FF_FFFFu;
                        return physical >= physicalStart && physical <= physicalEnd;
                    });
                Assert.DoesNotContain(
                    result.CpuDataWriteAddresses,
                    access =>
                    {
                        var physical = access.Address & 0x00FF_FFFFu;
                        return physical >= physicalStart && physical <= physicalEnd;
                    });
            });
            Check(failures, "unknown tag or provider decline never reaches A6 state", () =>
            {
                Assert.Equal(0, result.CapabilityReadCount);
                Assert.Equal(0, result.DefaultMonitorReadCount);
            });
        }

        Assert.True(
            failures.Count == 0,
            $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(NameIdentityPrefixCases))]
    public void GetDisplayInfoDataPublishesTheEightByteNameIdentityPrefix(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const uint foreignMode = 0xDEAD_BEEFu;
        var finalCanonicalMode =
            GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteLaceKey;
        using var fixture = new Fixture(relocated, autoInitEntry);
        var handle = ModeId;
        var destination = 0x300u;
        var requestedBytes = 8u;
        var tag = GraphicsDisplayDatabase.DtagName;
        var modeId = foreignMode;
        var expectedResult = requestedBytes;
        ushort? expectedReturnPredecessorOpcode = AddQuickFourLongA7Opcode;
        var expectedOutputBytes = 8;
        var expectedOutputMode = ModeId;

        switch (scenario)
        {
            case "canonical-handle-eight":
                break;
            case "canonical-d2-eight":
                handle = 0;
                modeId = finalCanonicalMode;
                expectedOutputMode = finalCanonicalMode;
                break;
            case "private-default-eight":
                handle = GraphicsDisplayDatabase.DefaultModeHandle;
                expectedOutputMode = GraphicsModeIds.DefaultMonitor;
                break;
            case "exact-end-eight":
                destination = LastBase(expectedOutputBytes);
                break;
            case "first-wrap-eight-control":
                destination = FirstEvenWrap(expectedOutputBytes);
                expectedReturnPredecessorOpcode = null;
                expectedOutputBytes = 0;
                break;
            case "foreign-handle-eight":
                handle = foreignMode;
                modeId = ModeId;
                expectedReturnPredecessorOpcode = BranchAlwaysWordOpcode;
                expectedOutputBytes = 0;
                break;
            case "full-name-control":
                requestedBytes = (uint)GraphicsDisplayDatabase.DisplayInfoChunkSize;
                expectedResult = requestedBytes;
                expectedOutputBytes = GraphicsDisplayDatabase.DisplayInfoChunkSize;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "exact-end-eight")
        {
            Assert.Equal(0u, destination & 1u);
            Assert.Equal(uint.MaxValue, destination + (uint)expectedOutputBytes - 1u);
        }

        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(
            handle,
            destination,
            requestedBytes,
            tag,
            modeId,
            graphicsBase: uint.MaxValue);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();

        Check(failures, "result and native/provider terminal", () =>
        {
            Assert.Equal(expectedResult, result.Data0);
            Assert.False(result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
            if (expectedReturnPredecessorOpcode.HasValue)
            {
                Assert.Equal(
                    expectedReturnPredecessorOpcode.Value,
                    result.NativeReturnPredecessorOpcode);
            }
        });
        Check(failures, "caller PC/SP, A1, and supplied A6", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(uint.MaxValue, result.Address6);
        });
        Check(failures, "D2-D7/A2-A6 public frame", () =>
            Assert.True(
                result.RegisterDifferences.Length == 0,
                string.Join("; ", result.RegisterDifferences)));
        Check(failures, "short query remains A6-read-free", () =>
        {
            Assert.Equal(0, result.CapabilityReadCount);
            Assert.Equal(0, result.DefaultMonitorReadCount);
        });

        if (expectedOutputBytes != 0)
        {
            Check(failures, "only the requested name prefix changes", () =>
                fixture.AssertOnlyOutputSpanChanged(
                    before,
                    after,
                    destination,
                    expectedOutputBytes,
                    tag,
                    expectedOutputMode));
        }
        else
        {
            Check(failures, "out-of-slice or provider-owned calls leave memory intact", () =>
                AssertMemoryEqual(before, after));
            Check(failures, "out-of-slice or provider-owned calls never probe A1", () =>
            {
                var physicalStart = destination & 0x00FF_FFFFu;
                var physicalLength = (uint)GraphicsDisplayDatabase.DisplayInfoChunkSize;
                Assert.DoesNotContain(
                    result.CpuDataReadAddresses,
                    access =>
                    {
                        var physical = access.Address & 0x00FF_FFFFu;
                        return ((physical - physicalStart) & 0x00FF_FFFFu) < physicalLength;
                    });
                Assert.DoesNotContain(
                    result.CpuDataWriteAddresses,
                    access =>
                    {
                        var physical = access.Address & 0x00FF_FFFFu;
                        return ((physical - physicalStart) & 0x00FF_FFFFu) < physicalLength;
                    });
            });
        }

        Assert.True(
            failures.Count == 0,
            $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(NameHeaderPrefixCases))]
    public void GetDisplayInfoDataPublishesEveryNameQueryHeaderPrefixLength(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const uint foreignMode = 0xDEAD_BEEFu;
        var finalCanonicalMode =
            GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteLaceKey;
        using var fixture = new Fixture(relocated, autoInitEntry);
        var handle = ModeId;
        var destination = 0x300u;
        var requestedBytes = 1u;
        var tag = GraphicsDisplayDatabase.DtagName;
        var modeId = foreignMode;
        var expectedResult = requestedBytes;
        ushort? expectedReturnPredecessorOpcode = AddQuickFourLongA7Opcode;
        var expectedOutputBytes = 1;
        var expectedOutputMode = ModeId;

        switch (scenario)
        {
            case "one-byte-final":
                destination = LastBase(expectedOutputBytes);
                break;
            case "two-byte-even":
                requestedBytes = 2;
                expectedResult = requestedBytes;
                expectedOutputBytes = 2;
                break;
            case "three-byte-odd":
                destination = 0x301u;
                requestedBytes = 3;
                expectedResult = requestedBytes;
                expectedOutputBytes = 3;
                break;
            case "four-byte-exact-end":
                requestedBytes = 4;
                expectedResult = requestedBytes;
                expectedOutputBytes = 4;
                destination = LastBase(expectedOutputBytes);
                break;
            case "five-byte-odd":
                destination = 0x301u;
                requestedBytes = 5;
                expectedResult = requestedBytes;
                expectedOutputBytes = 5;
                break;
            case "six-byte-even":
                requestedBytes = 6;
                expectedResult = requestedBytes;
                expectedOutputBytes = 6;
                break;
            case "seven-byte-final-mode":
                handle = 0;
                modeId = finalCanonicalMode;
                requestedBytes = 7;
                expectedResult = requestedBytes;
                expectedOutputBytes = 7;
                expectedOutputMode = finalCanonicalMode;
                break;
            case "eight-byte-even-control":
                requestedBytes = 8;
                expectedResult = requestedBytes;
                expectedOutputBytes = 8;
                break;
            case "eight-byte-odd":
                destination = 0x301u;
                requestedBytes = 8;
                expectedResult = requestedBytes;
                expectedOutputBytes = 8;
                break;
            case "nine-byte-private-default":
                handle = GraphicsDisplayDatabase.DefaultModeHandle;
                requestedBytes = 9;
                expectedResult = requestedBytes;
                expectedOutputBytes = 9;
                expectedOutputMode = GraphicsModeIds.DefaultMonitor;
                break;
            case "ten-byte-even":
                requestedBytes = 10;
                expectedResult = requestedBytes;
                expectedOutputBytes = 10;
                break;
            case "eleven-byte-odd":
                destination = 0x301u;
                requestedBytes = 11;
                expectedResult = requestedBytes;
                expectedOutputBytes = 11;
                break;
            case "twelve-byte-even":
                requestedBytes = 12;
                expectedResult = requestedBytes;
                expectedOutputBytes = 12;
                break;
            case "thirteen-byte-odd":
                destination = 0x301u;
                requestedBytes = 13;
                expectedResult = requestedBytes;
                expectedOutputBytes = 13;
                break;
            case "fourteen-byte-even":
                requestedBytes = 14;
                expectedResult = requestedBytes;
                expectedOutputBytes = 14;
                break;
            case "fifteen-byte-odd":
                destination = 0x301u;
                requestedBytes = 15;
                expectedResult = requestedBytes;
                expectedOutputBytes = 15;
                break;
            case "sixteen-byte-exact-end":
                requestedBytes = 16;
                expectedResult = requestedBytes;
                expectedOutputBytes = 16;
                destination = LastBase(expectedOutputBytes);
                break;
            case "sixteen-first-wrap-control":
                requestedBytes = 16;
                expectedResult = requestedBytes;
                expectedOutputBytes = 16;
                destination = checked(LastBase(expectedOutputBytes) + 1u);
                expectedReturnPredecessorOpcode = null;
                expectedOutputBytes = 0;
                break;
            case "foreign-sixteen":
                handle = foreignMode;
                modeId = ModeId;
                requestedBytes = 16;
                expectedResult = requestedBytes;
                expectedReturnPredecessorOpcode = BranchAlwaysWordOpcode;
                expectedOutputBytes = 0;
                break;
            case "zero-byte-control":
                requestedBytes = 0;
                expectedResult = 0;
                expectedReturnPredecessorOpcode = null;
                expectedOutputBytes = 0;
                break;
            case "full-name-control":
                requestedBytes = (uint)GraphicsDisplayDatabase.DisplayInfoChunkSize;
                expectedResult = requestedBytes;
                expectedOutputBytes = GraphicsDisplayDatabase.DisplayInfoChunkSize;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario is "one-byte-final" or
            "four-byte-exact-end" or
            "sixteen-byte-exact-end")
        {
            Assert.Equal(
                (ulong)uint.MaxValue,
                (ulong)destination + (uint)expectedOutputBytes - 1u);
        }
        if (scenario == "sixteen-first-wrap-control")
        {
            Assert.True(
                (ulong)destination + requestedBytes - 1u > uint.MaxValue);
        }

        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(
            handle,
            destination,
            requestedBytes,
            tag,
            modeId,
            graphicsBase: uint.MaxValue);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();

        Check(failures, "result and native/provider terminal", () =>
        {
            Assert.Equal(expectedResult, result.Data0);
            Assert.False(result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
            if (expectedReturnPredecessorOpcode.HasValue)
            {
                Assert.Equal(
                    expectedReturnPredecessorOpcode.Value,
                    result.NativeReturnPredecessorOpcode);
            }
        });
        Check(failures, "caller PC/SP, D1, A1, and supplied A6", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(uint.MaxValue, result.Address6);
        });
        Check(failures, "D2-D7/A2-A6 public frame", () =>
            Assert.True(
                result.RegisterDifferences.Length == 0,
                string.Join("; ", result.RegisterDifferences)));
        Check(failures, "short query remains A6-read-free", () =>
        {
            Assert.Equal(0, result.CapabilityReadCount);
            Assert.Equal(0, result.DefaultMonitorReadCount);
        });

        if (expectedOutputBytes != 0)
        {
            Check(failures, "only the requested QueryHeader prefix changes", () =>
            {
                if (expectedOutputBytes == GraphicsDisplayDatabase.DisplayInfoChunkSize)
                {
                    fixture.AssertOnlyOutputSpanChanged(
                        before,
                        after,
                        destination,
                        expectedOutputBytes,
                        tag,
                        expectedOutputMode);
                    return;
                }

                fixture.AssertOnlyOutputPrefixChanged(
                    before,
                    after,
                    destination,
                    BuildQueryHeader(
                            GraphicsDisplayDatabase.DtagName,
                            expectedOutputMode)
                        .Take(expectedOutputBytes)
                        .ToArray());
            });
        }
        else
        {
            Check(failures, "out-of-slice or provider-owned calls leave memory intact", () =>
                AssertMemoryEqual(before, after));
            Check(failures, "out-of-slice or provider-owned calls never probe A1", () =>
            {
                var physicalStart = destination & 0x00FF_FFFFu;
                var physicalLength = (uint)GraphicsDisplayDatabase.DisplayInfoChunkSize;
                Assert.DoesNotContain(
                    result.CpuDataReadAddresses,
                    access =>
                    {
                        var physical = access.Address & 0x00FF_FFFFu;
                        return ((physical - physicalStart) & 0x00FF_FFFFu) < physicalLength;
                    });
                Assert.DoesNotContain(
                    result.CpuDataWriteAddresses,
                    access =>
                    {
                        var physical = access.Address & 0x00FF_FFFFu;
                        return ((physical - physicalStart) & 0x00FF_FFFFu) < physicalLength;
                    });
            });
        }

        Assert.True(
            failures.Count == 0,
            $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(DisplayHeaderPrefixCases))]
    public void GetDisplayInfoDataPublishesEveryDisplayQueryHeaderPrefixLength(
        bool relocated,
        bool autoInitEntry,
        string scenario,
        uint requestedBytes)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        var finalCanonicalMode =
            GraphicsModeIds.NtscMonitor |
            GraphicsModeIds.ExtraHalfBriteLaceKey;
        using var fixture = new Fixture(relocated, autoInitEntry);
        var handle = ModeId;
        var destination = (requestedBytes & 1u) == 0 ? 0x300u : 0x301u;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = foreignMode;
        var graphicsBase = uint.MaxValue;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var publishes = scenario.StartsWith("header-", StringComparison.Ordinal);
        var providerOwned = false;
        var ownedZero = false;

        if (!publishes)
        {
            switch (scenario)
            {
                case "exact-end-1":
                case "exact-end-16":
                    publishes = true;
                    destination = LastBase(checked((int)requestedBytes));
                    break;
                case "null-handle-final-canonical-7":
                    publishes = true;
                    handle = 0;
                    modeId = finalCanonicalMode;
                    expectedMode = finalCanonicalMode;
                    break;
                case "private-default-9":
                    publishes = true;
                    handle = GraphicsDisplayDatabase.DefaultModeHandle;
                    expectedMode = GraphicsModeIds.DefaultMonitor;
                    break;
                case "foreign-provider-16":
                    handle = foreignMode;
                    modeId = ModeId;
                    providerOwned = true;
                    break;
                case "first-wrap-16-control":
                    destination = unchecked(LastBase(checked((int)requestedBytes)) + 1u);
                    break;
                case "zero-size-control":
                    ownedZero = true;
                    break;
                case "null-destination-16-control":
                    destination = 0;
                    expectedResult = 0;
                    ownedZero = true;
                    break;
                case "disp-55-control":
                    break;
                case "full-disp-56-control":
                    publishes = true;
                    graphicsBase = fixture.GraphicsBase;
                    break;
                case "name-16-control":
                    publishes = true;
                    tag = GraphicsDisplayDatabase.DtagName;
                    break;
                case "dims-16-control":
                    publishes = true;
                    tag = GraphicsDisplayDatabase.DtagDims;
                    break;
                case "mntr-16-control":
                    publishes = true;
                    tag = GraphicsDisplayDatabase.DtagMntr;
                    break;
                case "unsupported-16-control":
                    tag = UnsupportedTag;
                    expectedResult = 0;
                    ownedZero = true;
                    break;
                case "vec-16-provider-control":
                    tag = GraphicsDisplayDatabase.DtagVec;
                    providerOwned = true;
                    break;
                case "sentinel-16-control":
                    handle = 0;
                    modeId = GraphicsModeIds.Invalid;
                    expectedResult = 0;
                    ownedZero = true;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario));
            }
        }

        if (scenario.StartsWith("header-", StringComparison.Ordinal))
        {
            Assert.Equal(
                (requestedBytes & 1u) == 0 ? 0u : 1u,
                destination & 1u);
        }
        if (scenario.StartsWith("exact-end-", StringComparison.Ordinal))
        {
            Assert.Equal(
                (ulong)uint.MaxValue,
                (ulong)destination + requestedBytes - 1u);
        }
        if (scenario == "first-wrap-16-control")
        {
            Assert.Equal(1u, destination & 1u);
            Assert.Equal(
                (ulong)uint.MaxValue + 1u,
                (ulong)destination + requestedBytes - 1u);
        }

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(tag, handle, modeId, requestedBytes);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);

            var expectedHeader = BuildQueryHeader(tag, expectedMode);
            var expectedHeaderBytes = Math.Min(expectedHeader.Length, expectedBytes.Length);
            Assert.Equal(
                expectedHeader.Take(expectedHeaderBytes),
                expectedBytes.Take(expectedHeaderBytes));
            if (requestedBytes <= 16u)
                Assert.Equal(checked((int)requestedBytes), expectedBytes.Length);
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes
            ? fixture.ReadOutputByte(followingAddress)
            : (byte)0;
        var result = fixture.Invoke(
            handle,
            destination,
            requestedBytes,
            tag,
            modeId,
            graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();

        Check(failures, "result and native/provider terminal", () =>
        {
            Assert.Equal(expectedResult, result.Data0);
            Assert.False(result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned)
            {
                Assert.Equal(
                    BranchAlwaysWordOpcode,
                    result.NativeReturnPredecessorOpcode);
            }
            else if (publishes)
            {
                Assert.Equal(
                    AddQuickFourLongA7Opcode,
                    result.NativeReturnPredecessorOpcode);
            }
            else if (ownedZero)
            {
                Assert.Equal(
                    MoveQuickZeroD0Opcode,
                    result.NativeReturnPredecessorOpcode);
            }
        });
        Check(failures, "caller PC/SP, D1, A1, and supplied A6", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(graphicsBase, result.Address6);
        });
        Check(failures, "D2-D7/A2-A6 public frame", () =>
            Assert.True(
                result.RegisterDifferences.Length == 0,
                string.Join("; ", result.RegisterDifferences)));
        if (graphicsBase == uint.MaxValue)
        {
            Check(failures, "partial and pre-admission routes remain A6-read-free", () =>
            {
                Assert.Equal(0, result.CapabilityReadCount);
                Assert.Equal(0, result.DefaultMonitorReadCount);
            });
        }

        if (publishes)
        {
            Check(failures, "only N exact portable bytes change", () =>
                fixture.AssertOnlyOutputPrefixChanged(
                    before,
                    after,
                    destination,
                    expectedBytes));
            Check(failures, "the byte following the output remains untouched", () =>
                Assert.Equal(
                    followingByte,
                    fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "control or provider-owned calls leave memory intact", () =>
                AssertMemoryEqual(before, after));
            Check(failures, "control or provider-owned calls never access A1", () =>
                AssertNoDestinationAccess(
                    result,
                    destination,
                    Math.Max(1u, requestedBytes)));
        }

        Assert.True(
            failures.Count == 0,
            $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(DisplaySeventeenByteCases))]
    public void GetDisplayInfoDataPublishesTheSeventeenByteDisplayPrefix(
        bool relocated,
        bool autoInitEntry,
        string scenario,
        uint requestedBytes)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        var finalCanonicalMode =
            GraphicsModeIds.NtscMonitor |
            GraphicsModeIds.ExtraHalfBriteLaceKey;
        var superHiresMode =
            GraphicsModeIds.PalMonitor |
            GraphicsModeIds.SuperHiresKey;
        AssertDisplaySeventeenBytePortableSeam(superHiresMode);

        using var fixture = new Fixture(relocated, autoInitEntry);
        var handle = ModeId;
        var destination = 0x300u;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = foreignMode;
        var graphicsBase = uint.MaxValue;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var publishes = false;
        var providerOwned = false;
        var ownedZero = false;
        var malformedForeignSpan = false;

        switch (scenario)
        {
            case "disp17-even-authoritative":
                publishes = true;
                break;
            case "disp17-odd":
                publishes = true;
                destination = 0x301u;
                break;
            case "disp17-exact-end":
                publishes = true;
                destination = 0xFFFF_FFEFu;
                break;
            case "disp17-null-final-canonical":
                publishes = true;
                handle = 0;
                modeId = finalCanonicalMode;
                expectedMode = finalCanonicalMode;
                break;
            case "disp17-private-default":
                publishes = true;
                handle = GraphicsDisplayDatabase.DefaultModeHandle;
                expectedMode = GraphicsModeIds.DefaultMonitor;
                break;
            case "disp17-superhires-null-a6":
                publishes = true;
                handle = superHiresMode;
                expectedMode = superHiresMode;
                graphicsBase = 0;
                break;
            case "disp17-superhires-odd-a6":
                publishes = true;
                handle = superHiresMode;
                expectedMode = superHiresMode;
                graphicsBase = 1;
                break;
            case "disp17-superhires-final-a6":
                publishes = true;
                handle = superHiresMode;
                expectedMode = superHiresMode;
                break;
            case "disp17-foreign-provider":
                handle = foreignMode;
                modeId = ModeId;
                providerOwned = true;
                break;
            case "disp17-first-wrap-control":
                destination = 0xFFFF_FFF0u;
                break;
            case "disp17-foreign-first-wrap-control":
                handle = foreignMode;
                modeId = ModeId;
                destination = 0xFFFF_FFF0u;
                malformedForeignSpan = true;
                break;
            case "disp16-control":
                publishes = true;
                break;
            case "disp55-control":
                break;
            case "disp56-control":
                publishes = true;
                graphicsBase = fixture.GraphicsBase;
                break;
            case "name17-control":
                publishes = true;
                tag = GraphicsDisplayDatabase.DtagName;
                break;
            case "dims17-control":
                publishes = true; // invariant high depth byte remains A6-read-free
                tag = GraphicsDisplayDatabase.DtagDims;
                break;
            case "mntr17-control":
                tag = GraphicsDisplayDatabase.DtagMntr;
                break;
            case "unsupported17-control":
                tag = UnsupportedTag;
                expectedResult = 0;
                ownedZero = true;
                break;
            case "vec17-provider-control":
                tag = GraphicsDisplayDatabase.DtagVec;
                providerOwned = true;
                break;
            case "sentinel17-control":
                handle = 0;
                modeId = GraphicsModeIds.Invalid;
                expectedResult = 0;
                ownedZero = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "disp17-exact-end")
        {
            Assert.Equal(
                (ulong)uint.MaxValue,
                (ulong)destination + requestedBytes - 1u);
        }
        if (scenario.EndsWith("first-wrap-control", StringComparison.Ordinal))
        {
            Assert.Equal(0xFFFF_FFF0u, destination);
            Assert.Equal(
                (ulong)uint.MaxValue + 1u,
                (ulong)destination + requestedBytes - 1u);
        }

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(tag, handle, modeId, requestedBytes);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);
            if (tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes == 17u)
                Assert.Equal((byte)0, expectedBytes[16]);

            var expectedHeader = BuildQueryHeader(tag, expectedMode);
            Assert.Equal(
                expectedHeader,
                expectedBytes.Take(expectedHeader.Length));
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes
            ? fixture.ReadOutputByte(followingAddress)
            : (byte)0;
        var result = fixture.Invoke(
            handle,
            destination,
            requestedBytes,
            tag,
            modeId,
            graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();

        Check(failures, "D0 result and native/provider terminal", () =>
        {
            Assert.Equal(expectedResult, result.Data0);
            Assert.False(result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned)
            {
                Assert.Equal(
                    BranchAlwaysWordOpcode,
                    result.NativeReturnPredecessorOpcode);
            }
            else if (publishes)
            {
                Assert.Equal(
                    AddQuickFourLongA7Opcode,
                    result.NativeReturnPredecessorOpcode);
            }
            else if (ownedZero)
            {
                Assert.Equal(
                    MoveQuickZeroD0Opcode,
                    result.NativeReturnPredecessorOpcode);
            }
            else if (malformedForeignSpan)
            {
                Assert.NotEqual(
                    BranchAlwaysWordOpcode,
                    result.NativeReturnPredecessorOpcode);
            }
        });
        Check(failures, "caller PC/SP, D1, A1, and supplied A6", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(graphicsBase, result.Address6);
        });
        Check(failures, "D2-D7/A2-A6 public frame", () =>
            Assert.True(
                result.RegisterDifferences.Length == 0,
                string.Join("; ", result.RegisterDifferences)));
        if (graphicsBase != fixture.GraphicsBase)
        {
            Check(failures, "partial and pre-admission routes remain A6-read-free", () =>
            {
                Assert.Equal(0, result.CapabilityReadCount);
                Assert.Equal(0, result.DefaultMonitorReadCount);
            });
        }

        if (publishes)
        {
            Check(failures, "only the exact portable prefix changes", () =>
                fixture.AssertOnlyOutputPrefixChanged(
                    before,
                    after,
                    destination,
                    expectedBytes));
            Check(failures, "the byte following the output remains untouched", () =>
                Assert.Equal(
                    followingByte,
                    fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "control or provider-owned calls leave memory intact", () =>
                AssertMemoryEqual(before, after));
            Check(failures, "control or provider-owned calls never access A1", () =>
                AssertNoDestinationAccess(result, destination, requestedBytes));
        }

        Assert.True(
            failures.Count == 0,
            $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(DisplayEighteenByteCases))]
    public void GetDisplayInfoDataPublishesTheEighteenByteDisplayPrefix(
        bool relocated,
        bool autoInitEntry,
        string scenario,
        uint requestedBytes)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        var finalCanonicalMode =
            GraphicsModeIds.NtscMonitor |
            GraphicsModeIds.ExtraHalfBriteLaceKey;
        var superHiresMode =
            GraphicsModeIds.PalMonitor |
            GraphicsModeIds.SuperHiresKey;
        AssertDisplaySeventeenBytePortableSeam(superHiresMode);
        AssertDisplayEighteenBytePortableSeam(ModeId, superHiresMode);

        using var fixture = new Fixture(relocated, autoInitEntry);
        var maximumCapabilityBase =
            uint.MaxValue - (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        var lastAlignedCapabilityBase = maximumCapabilityBase & ~1u;
        var firstWrapCapabilityBase = maximumCapabilityBase + 1u;
        Assert.Equal(0xFFFF_FF13u, maximumCapabilityBase);
        Assert.Equal(0xFFFF_FF12u, lastAlignedCapabilityBase);
        Assert.Equal(0xFFFF_FF14u, firstWrapCapabilityBase);

        var handle = ModeId;
        var destination = 0x300u;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = foreignMode;
        var graphicsBase = uint.MaxValue;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var publishes = false;
        var providerOwned = false;
        var ownedZero = false;
        var malformedForeignSpan = false;
        var supportsEcsOracle = true;
        var supportsAgaOracle = false;
        byte? chipRevision = null;
        var logicalCapabilityReads = 0;

        switch (scenario)
        {
            case "disp18-even-authoritative-final-a6":
                publishes = true;
                break;
            case "disp18-odd":
                publishes = true;
                destination = 0x301u;
                break;
            case "disp18-exact-end":
                publishes = true;
                destination = uint.MaxValue - 17u;
                break;
            case "disp18-null-final-canonical":
                publishes = true;
                handle = 0;
                modeId = finalCanonicalMode;
                expectedMode = finalCanonicalMode;
                break;
            case "disp18-private-default":
                publishes = true;
                handle = GraphicsDisplayDatabase.DefaultModeHandle;
                expectedMode = GraphicsModeIds.DefaultMonitor;
                break;
            case "disp18-superhires-first-even-ocs":
                publishes = true;
                handle = superHiresMode;
                expectedMode = superHiresMode;
                graphicsBase = 2;
                supportsEcsOracle = false;
                chipRevision = 0;
                logicalCapabilityReads = 1;
                break;
            case "disp18-superhires-library-agnus":
                publishes = true;
                handle = superHiresMode;
                expectedMode = superHiresMode;
                graphicsBase = fixture.GraphicsBase;
                supportsEcsOracle = false;
                chipRevision = (byte)GraphicsChipRevision.HrAgnus;
                logicalCapabilityReads = 1;
                break;
            case "disp18-superhires-library-denise":
                publishes = true;
                handle = superHiresMode;
                expectedMode = superHiresMode;
                graphicsBase = fixture.GraphicsBase;
                supportsEcsOracle = false;
                chipRevision = (byte)GraphicsChipRevision.HrDenise;
                logicalCapabilityReads = 1;
                break;
            case "disp18-superhires-last-aligned-ecs":
                publishes = true;
                handle = superHiresMode;
                expectedMode = superHiresMode;
                graphicsBase = lastAlignedCapabilityBase;
                chipRevision = (byte)GraphicsChipRevision.SetEcs;
                logicalCapabilityReads = 1;
                break;
            case "disp18-superhires-library-aga":
                publishes = true;
                handle = superHiresMode;
                expectedMode = superHiresMode;
                graphicsBase = fixture.GraphicsBase;
                supportsAgaOracle = true;
                chipRevision = (byte)GraphicsChipRevision.SetAa;
                logicalCapabilityReads = 1;
                break;
            case "disp18-foreign-provider":
                handle = foreignMode;
                modeId = ModeId;
                providerOwned = true;
                break;
            case "disp18-superhires-null-a6-control":
                handle = superHiresMode;
                expectedMode = superHiresMode;
                graphicsBase = 0;
                break;
            case "disp18-superhires-odd-a6-control":
                handle = superHiresMode;
                expectedMode = superHiresMode;
                graphicsBase = 3;
                break;
            case "disp18-superhires-exact-end-a6-control":
                handle = superHiresMode;
                expectedMode = superHiresMode;
                graphicsBase = maximumCapabilityBase;
                break;
            case "disp18-superhires-first-wrap-a6-control":
                handle = superHiresMode;
                expectedMode = superHiresMode;
                graphicsBase = firstWrapCapabilityBase;
                break;
            case "disp18-first-wrap-control":
                destination = 0xFFFF_FFEFu;
                break;
            case "disp18-superhires-first-wrap-destination-control":
                handle = superHiresMode;
                expectedMode = superHiresMode;
                destination = 0xFFFF_FFEFu;
                graphicsBase = fixture.GraphicsBase;
                chipRevision = (byte)GraphicsChipRevision.SetEcs;
                break;
            case "disp18-foreign-first-wrap-control":
                handle = foreignMode;
                modeId = ModeId;
                destination = 0xFFFF_FFEFu;
                malformedForeignSpan = true;
                break;
            case "disp17-control":
                publishes = true;
                break;
            case "disp55-control":
                break;
            case "disp56-control":
                publishes = true;
                graphicsBase = fixture.GraphicsBase;
                chipRevision = (byte)GraphicsChipRevision.SetEcs;
                logicalCapabilityReads = 1;
                break;
            case "name18-control":
                publishes = true;
                tag = GraphicsDisplayDatabase.DtagName;
                break;
            case "dims18-control":
                tag = GraphicsDisplayDatabase.DtagDims;
                break;
            case "mntr18-control":
                tag = GraphicsDisplayDatabase.DtagMntr;
                break;
            case "unsupported18-control":
                tag = UnsupportedTag;
                expectedResult = 0;
                ownedZero = true;
                break;
            case "vec18-provider-control":
                tag = GraphicsDisplayDatabase.DtagVec;
                providerOwned = true;
                break;
            case "sentinel18-control":
                handle = 0;
                modeId = GraphicsModeIds.Invalid;
                expectedResult = 0;
                ownedZero = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "disp18-exact-end")
        {
            Assert.Equal(
                (ulong)uint.MaxValue,
                (ulong)destination + requestedBytes - 1u);
        }
        if (scenario.EndsWith("first-wrap-control", StringComparison.Ordinal) ||
            scenario == "disp18-superhires-first-wrap-destination-control")
        {
            Assert.Equal(0xFFFF_FFEFu, destination);
            Assert.Equal(
                (ulong)uint.MaxValue + 1u,
                (ulong)destination + requestedBytes - 1u);
        }

        if (chipRevision.HasValue)
            fixture.SeedChipRevision(graphicsBase, chipRevision.Value);

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(
                tag,
                handle,
                modeId,
                requestedBytes,
                supportsEcsOracle,
                supportsAgaOracle);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);

            var expectedHeader = BuildQueryHeader(tag, expectedMode);
            Assert.Equal(
                expectedHeader,
                expectedBytes.Take(expectedHeader.Length));
            if (tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes == 18u)
            {
                Assert.Equal((byte)0, expectedBytes[16]);
                Assert.Equal(
                    supportsEcsOracle ? (byte)0 : (byte)1,
                    expectedBytes[17]);
            }
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes
            ? fixture.ReadOutputByte(followingAddress)
            : (byte)0;
        var result = fixture.Invoke(
            handle,
            destination,
            requestedBytes,
            tag,
            modeId,
            graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        var expectedCapabilityReads = logicalCapabilityReads;

        Check(failures, "D0 result and native/provider terminal", () =>
        {
            Assert.Equal(expectedResult, result.Data0);
            Assert.False(result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned)
            {
                Assert.Equal(
                    BranchAlwaysWordOpcode,
                    result.NativeReturnPredecessorOpcode);
            }
            else if (publishes)
            {
                Assert.Equal(
                    AddQuickFourLongA7Opcode,
                    result.NativeReturnPredecessorOpcode);
            }
            else if (ownedZero)
            {
                Assert.Equal(
                    MoveQuickZeroD0Opcode,
                    result.NativeReturnPredecessorOpcode);
            }
            else if (malformedForeignSpan)
            {
                Assert.NotEqual(
                    BranchAlwaysWordOpcode,
                    result.NativeReturnPredecessorOpcode);
            }
        });
        Check(failures, "capability/default-monitor read count", () =>
        {
            Assert.Equal(expectedCapabilityReads, result.CapabilityReadCount);
            Assert.Equal(0, result.DefaultMonitorReadCount);
        });
        Check(failures, "caller PC/SP, D1, A1, and supplied A6", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(graphicsBase, result.Address6);
        });
        Check(failures, "D2-D7/A2-A6 public frame", () =>
            Assert.True(
                result.RegisterDifferences.Length == 0,
                string.Join("; ", result.RegisterDifferences)));

        if (publishes)
        {
            Check(failures, "only the exact portable prefix changes", () =>
                fixture.AssertOnlyOutputPrefixChanged(
                    before,
                    after,
                    destination,
                    expectedBytes));
            Check(failures, "the byte following the output remains untouched", () =>
                Assert.Equal(
                    followingByte,
                    fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "control or provider-owned calls leave memory intact", () =>
                AssertMemoryEqual(before, after));
            Check(failures, "control or provider-owned calls never access A1", () =>
                AssertNoDestinationAccess(result, destination, requestedBytes));
        }

        Assert.True(
            failures.Count == 0,
            $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(DisplayNineteenByteCases))]
    public void GetDisplayInfoDataPublishesTheNineteenByteDisplayPrefix(
        bool relocated,
        bool autoInitEntry,
        string scenario,
        uint requestedBytes)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        var finalCanonicalMode =
            GraphicsModeIds.NtscMonitor |
            GraphicsModeIds.ExtraHalfBriteLaceKey;
        var superHiresMode =
            GraphicsModeIds.PalMonitor |
            GraphicsModeIds.SuperHiresKey;
        AssertDisplayEighteenBytePortableSeam(ModeId, superHiresMode);
        AssertDisplayNineteenBytePortableSeam(ModeId, superHiresMode);

        using var fixture = new Fixture(relocated, autoInitEntry);
        var maximumCapabilityBase =
            uint.MaxValue - (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        var lastAlignedCapabilityBase = maximumCapabilityBase & ~1u;
        var firstWrapCapabilityBase = maximumCapabilityBase + 1u;
        Assert.Equal(0xFFFF_FF13u, maximumCapabilityBase);
        Assert.Equal(0xFFFF_FF12u, lastAlignedCapabilityBase);
        Assert.Equal(0xFFFF_FF14u, firstWrapCapabilityBase);

        var handle = ModeId;
        var destination = 0x300u;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = foreignMode;
        var graphicsBase = uint.MaxValue;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var publishes = false;
        var providerOwned = false;
        var ownedZero = false;
        var malformedForeignSpan = false;
        var supportsEcsOracle = true;
        var supportsAgaOracle = false;
        byte? chipRevision = null;
        var logicalCapabilityReads = 0;

        switch (scenario)
        {
            case "disp19-even-authoritative-final-a6":
                publishes = true;
                break;
            case "disp19-odd":
                publishes = true;
                destination = 0x301u;
                break;
            case "disp19-exact-end":
                publishes = true;
                destination = 0xFFFF_FFEDu;
                break;
            case "disp19-null-final-canonical":
                publishes = true;
                handle = 0;
                modeId = finalCanonicalMode;
                expectedMode = finalCanonicalMode;
                break;
            case "disp19-private-default":
                publishes = true;
                handle = GraphicsDisplayDatabase.DefaultModeHandle;
                expectedMode = GraphicsModeIds.DefaultMonitor;
                break;
            case "disp19-superhires-first-even-ocs":
                publishes = true;
                handle = superHiresMode;
                expectedMode = superHiresMode;
                graphicsBase = 2;
                supportsEcsOracle = false;
                chipRevision = 0;
                logicalCapabilityReads = 1;
                break;
            case "disp19-superhires-library-agnus":
                publishes = true;
                handle = superHiresMode;
                expectedMode = superHiresMode;
                graphicsBase = fixture.GraphicsBase;
                supportsEcsOracle = false;
                chipRevision = (byte)GraphicsChipRevision.HrAgnus;
                logicalCapabilityReads = 1;
                break;
            case "disp19-superhires-library-denise":
                publishes = true;
                handle = superHiresMode;
                expectedMode = superHiresMode;
                graphicsBase = fixture.GraphicsBase;
                supportsEcsOracle = false;
                chipRevision = (byte)GraphicsChipRevision.HrDenise;
                logicalCapabilityReads = 1;
                break;
            case "disp19-superhires-last-aligned-ecs":
                publishes = true;
                handle = superHiresMode;
                expectedMode = superHiresMode;
                graphicsBase = lastAlignedCapabilityBase;
                chipRevision = (byte)GraphicsChipRevision.SetEcs;
                logicalCapabilityReads = 1;
                break;
            case "disp19-superhires-library-aga":
                publishes = true;
                handle = superHiresMode;
                expectedMode = superHiresMode;
                graphicsBase = fixture.GraphicsBase;
                supportsAgaOracle = true;
                chipRevision = (byte)GraphicsChipRevision.SetAa;
                logicalCapabilityReads = 1;
                break;
            case "disp19-foreign-provider":
                handle = foreignMode;
                modeId = ModeId;
                providerOwned = true;
                break;
            case "disp19-superhires-null-a6-control":
                handle = superHiresMode;
                expectedMode = superHiresMode;
                graphicsBase = 0;
                break;
            case "disp19-superhires-odd-a6-control":
                handle = superHiresMode;
                expectedMode = superHiresMode;
                graphicsBase = 3;
                break;
            case "disp19-superhires-exact-end-a6-control":
                handle = superHiresMode;
                expectedMode = superHiresMode;
                graphicsBase = maximumCapabilityBase;
                break;
            case "disp19-superhires-first-wrap-a6-control":
                handle = superHiresMode;
                expectedMode = superHiresMode;
                graphicsBase = firstWrapCapabilityBase;
                break;
            case "disp19-first-wrap-control":
                destination = 0xFFFF_FFEEu;
                break;
            case "disp19-superhires-first-wrap-destination-control":
                handle = superHiresMode;
                expectedMode = superHiresMode;
                destination = 0xFFFF_FFEEu;
                graphicsBase = fixture.GraphicsBase;
                chipRevision = (byte)GraphicsChipRevision.SetEcs;
                break;
            case "disp19-foreign-first-wrap-control":
                handle = foreignMode;
                modeId = ModeId;
                destination = 0xFFFF_FFEEu;
                malformedForeignSpan = true;
                break;
            case "disp18-control":
                publishes = true;
                break;
            case "disp55-control":
                break;
            case "disp56-control":
                publishes = true;
                graphicsBase = fixture.GraphicsBase;
                chipRevision = (byte)GraphicsChipRevision.SetEcs;
                logicalCapabilityReads = 1;
                break;
            case "name19-control":
                publishes = true;
                tag = GraphicsDisplayDatabase.DtagName;
                break;
            case "dims19-control":
                tag = GraphicsDisplayDatabase.DtagDims;
                break;
            case "mntr19-control":
                tag = GraphicsDisplayDatabase.DtagMntr;
                break;
            case "unsupported19-control":
                tag = UnsupportedTag;
                expectedResult = 0;
                ownedZero = true;
                break;
            case "vec19-provider-control":
                tag = GraphicsDisplayDatabase.DtagVec;
                providerOwned = true;
                break;
            case "sentinel19-control":
                handle = 0;
                modeId = GraphicsModeIds.Invalid;
                expectedResult = 0;
                ownedZero = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "disp19-exact-end")
        {
            Assert.Equal(
                (ulong)uint.MaxValue,
                (ulong)destination + requestedBytes - 1u);
        }
        if (scenario.EndsWith("first-wrap-control", StringComparison.Ordinal) ||
            scenario == "disp19-superhires-first-wrap-destination-control")
        {
            Assert.Equal(0xFFFF_FFEEu, destination);
            Assert.Equal(
                (ulong)uint.MaxValue + 1u,
                (ulong)destination + requestedBytes - 1u);
        }

        if (chipRevision.HasValue)
            fixture.SeedChipRevision(graphicsBase, chipRevision.Value);

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(
                tag,
                handle,
                modeId,
                requestedBytes,
                supportsEcsOracle,
                supportsAgaOracle);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);

            var expectedHeader = BuildQueryHeader(tag, expectedMode);
            Assert.Equal(
                expectedHeader,
                expectedBytes.Take(expectedHeader.Length));
            if (tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes == 19u)
            {
                Assert.Equal((byte)0, expectedBytes[16]);
                Assert.Equal(
                    supportsEcsOracle ? (byte)0 : (byte)1,
                    expectedBytes[17]);
                Assert.Equal((byte)0, expectedBytes[18]);
            }
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes
            ? fixture.ReadOutputByte(followingAddress)
            : (byte)0;
        var result = fixture.Invoke(
            handle,
            destination,
            requestedBytes,
            tag,
            modeId,
            graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        var expectedCapabilityReads = logicalCapabilityReads;

        Check(failures, "D0 result and native/provider terminal", () =>
        {
            Assert.Equal(expectedResult, result.Data0);
            Assert.False(result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned)
            {
                Assert.Equal(
                    BranchAlwaysWordOpcode,
                    result.NativeReturnPredecessorOpcode);
            }
            else if (publishes)
            {
                Assert.Equal(
                    AddQuickFourLongA7Opcode,
                    result.NativeReturnPredecessorOpcode);
            }
            else if (ownedZero)
            {
                Assert.Equal(
                    MoveQuickZeroD0Opcode,
                    result.NativeReturnPredecessorOpcode);
            }
            else if (malformedForeignSpan)
            {
                Assert.NotEqual(
                    BranchAlwaysWordOpcode,
                    result.NativeReturnPredecessorOpcode);
            }
        });
        Check(failures, "capability/default-monitor read count", () =>
        {
            Assert.Equal(expectedCapabilityReads, result.CapabilityReadCount);
            Assert.Equal(0, result.DefaultMonitorReadCount);
        });
        Check(failures, "caller PC/SP, D1, A1, and supplied A6", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(graphicsBase, result.Address6);
        });
        Check(failures, "D2-D7/A2-A6 public frame", () =>
            Assert.True(
                result.RegisterDifferences.Length == 0,
                string.Join("; ", result.RegisterDifferences)));

        if (publishes)
        {
            Check(failures, "only the exact portable prefix changes", () =>
                fixture.AssertOnlyOutputPrefixChanged(
                    before,
                    after,
                    destination,
                    expectedBytes));
            Check(failures, "the byte following the output remains untouched", () =>
                Assert.Equal(
                    followingByte,
                    fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "control or provider-owned calls leave memory intact", () =>
                AssertMemoryEqual(before, after));
            Check(failures, "control or provider-owned calls never access A1", () =>
                AssertNoDestinationAccess(result, destination, requestedBytes));
        }

        Assert.True(
            failures.Count == 0,
            $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(DisplayTwentyByteCases))]
    public void GetDisplayInfoDataPublishesTheTwentyByteDisplayPrefix(
        bool relocated,
        bool autoInitEntry,
        string scenario,
        uint requestedBytes)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        var finalCanonicalMode =
            GraphicsModeIds.NtscMonitor |
            GraphicsModeIds.ExtraHalfBriteLaceKey;
        var superHiresMode =
            GraphicsModeIds.PalMonitor |
            GraphicsModeIds.SuperHiresKey;
        AssertDisplayNineteenBytePortableSeam(ModeId, superHiresMode);
        AssertDisplayTwentyBytePortableSeam(ModeId, superHiresMode);

        using var fixture = new Fixture(relocated, autoInitEntry);
        var maximumCapabilityBase =
            uint.MaxValue - (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        var lastAlignedCapabilityBase = maximumCapabilityBase & ~1u;
        var firstWrapCapabilityBase = maximumCapabilityBase + 1u;
        Assert.Equal(0xFFFF_FF13u, maximumCapabilityBase);
        Assert.Equal(0xFFFF_FF12u, lastAlignedCapabilityBase);
        Assert.Equal(0xFFFF_FF14u, firstWrapCapabilityBase);

        var handle = ModeId;
        var destination = 0x300u;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = foreignMode;
        var graphicsBase = fixture.GraphicsBase;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var publishes = false;
        var providerOwned = false;
        var ownedZero = false;
        var malformedForeignSpan = false;
        var supportsEcsOracle = true;
        var supportsAgaOracle = false;
        byte? chipRevision = null;
        var logicalCapabilityReads = 0;

        switch (scenario)
        {
            case "disp20-ordinary-even-authoritative-ecs":
                publishes = true;
                chipRevision = (byte)GraphicsChipRevision.SetEcs;
                logicalCapabilityReads = 1;
                break;
            case "disp20-ordinary-odd-alice":
                publishes = true;
                destination = 0x301u;
                supportsEcsOracle = false;
                chipRevision = (byte)GraphicsChipRevision.AaAlice;
                logicalCapabilityReads = 1;
                break;
            case "disp20-ordinary-exact-end-lisa":
                publishes = true;
                destination = 0xFFFF_FFECu;
                supportsEcsOracle = false;
                chipRevision = (byte)GraphicsChipRevision.AaLisa;
                logicalCapabilityReads = 1;
                break;
            case "disp20-ordinary-null-final-canonical-aa":
                publishes = true;
                handle = 0;
                modeId = finalCanonicalMode;
                expectedMode = finalCanonicalMode;
                supportsEcsOracle = false;
                supportsAgaOracle = true;
                chipRevision = (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa);
                logicalCapabilityReads = 1;
                break;
            case "disp20-ordinary-private-default-aa-pair":
                publishes = true;
                handle = GraphicsDisplayDatabase.DefaultModeHandle;
                expectedMode = GraphicsModeIds.DefaultMonitor;
                supportsEcsOracle = false;
                supportsAgaOracle = true;
                chipRevision = (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa);
                logicalCapabilityReads = 1;
                break;
            case "disp20-ordinary-first-even-ocs":
                publishes = true;
                graphicsBase = 2;
                supportsEcsOracle = false;
                chipRevision = 0;
                logicalCapabilityReads = 1;
                break;
            case "disp20-ordinary-last-aligned-setaa":
                publishes = true;
                graphicsBase = lastAlignedCapabilityBase;
                supportsAgaOracle = true;
                chipRevision = (byte)GraphicsChipRevision.SetAa;
                logicalCapabilityReads = 1;
                break;
            case "disp20-superhires-ocs":
                publishes = true;
                handle = superHiresMode;
                expectedMode = superHiresMode;
                graphicsBase = 2;
                supportsEcsOracle = false;
                chipRevision = 0;
                logicalCapabilityReads = 1;
                break;
            case "disp20-superhires-ecs":
                publishes = true;
                handle = superHiresMode;
                expectedMode = superHiresMode;
                chipRevision = (byte)GraphicsChipRevision.SetEcs;
                logicalCapabilityReads = 1;
                break;
            case "disp20-superhires-alice":
                publishes = true;
                handle = superHiresMode;
                expectedMode = superHiresMode;
                supportsEcsOracle = false;
                chipRevision = (byte)GraphicsChipRevision.AaAlice;
                logicalCapabilityReads = 1;
                break;
            case "disp20-superhires-lisa":
                publishes = true;
                handle = superHiresMode;
                expectedMode = superHiresMode;
                supportsEcsOracle = false;
                chipRevision = (byte)GraphicsChipRevision.AaLisa;
                logicalCapabilityReads = 1;
                break;
            case "disp20-superhires-aa-pair":
                publishes = true;
                handle = superHiresMode;
                expectedMode = superHiresMode;
                supportsEcsOracle = false;
                supportsAgaOracle = true;
                chipRevision = (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa);
                logicalCapabilityReads = 1;
                break;
            case "disp20-superhires-setaa":
                publishes = true;
                handle = superHiresMode;
                expectedMode = superHiresMode;
                supportsAgaOracle = true;
                chipRevision = (byte)GraphicsChipRevision.SetAa;
                logicalCapabilityReads = 1;
                break;
            case "disp20-foreign-provider":
                handle = foreignMode;
                modeId = ModeId;
                graphicsBase = uint.MaxValue;
                providerOwned = true;
                break;
            case "disp20-ordinary-null-a6-control":
                graphicsBase = 0;
                break;
            case "disp20-ordinary-odd-a6-control":
                graphicsBase = 3;
                break;
            case "disp20-ordinary-exact-end-a6-control":
                graphicsBase = maximumCapabilityBase;
                break;
            case "disp20-ordinary-first-wrap-a6-control":
                graphicsBase = firstWrapCapabilityBase;
                break;
            case "disp20-first-wrap-control":
                destination = 0xFFFF_FFEDu;
                graphicsBase = uint.MaxValue;
                break;
            case "disp20-superhires-first-wrap-destination-control":
                handle = superHiresMode;
                expectedMode = superHiresMode;
                destination = 0xFFFF_FFEDu;
                chipRevision = (byte)GraphicsChipRevision.SetEcs;
                break;
            case "disp20-foreign-first-wrap-control":
                handle = foreignMode;
                modeId = ModeId;
                destination = 0xFFFF_FFEDu;
                graphicsBase = uint.MaxValue;
                malformedForeignSpan = true;
                break;
            case "disp18-ordinary-invalid-a6-control":
            case "disp19-ordinary-invalid-a6-control":
                publishes = true;
                graphicsBase = uint.MaxValue;
                break;
            case "disp18-superhires-valid-control":
            case "disp19-superhires-valid-control":
                publishes = true;
                handle = superHiresMode;
                expectedMode = superHiresMode;
                chipRevision = (byte)GraphicsChipRevision.SetEcs;
                logicalCapabilityReads = 1;
                break;
            case "disp55-control":
                graphicsBase = uint.MaxValue;
                break;
            case "disp56-control":
                publishes = true;
                chipRevision = (byte)GraphicsChipRevision.SetEcs;
                logicalCapabilityReads = 1;
                break;
            case "name20-control":
                publishes = true;
                tag = GraphicsDisplayDatabase.DtagName;
                graphicsBase = uint.MaxValue;
                break;
            case "dims20-control":
                tag = GraphicsDisplayDatabase.DtagDims;
                graphicsBase = uint.MaxValue;
                break;
            case "mntr20-control":
                tag = GraphicsDisplayDatabase.DtagMntr;
                graphicsBase = uint.MaxValue;
                break;
            case "unsupported20-control":
                tag = UnsupportedTag;
                graphicsBase = uint.MaxValue;
                expectedResult = 0;
                ownedZero = true;
                break;
            case "vec20-provider-control":
                tag = GraphicsDisplayDatabase.DtagVec;
                graphicsBase = uint.MaxValue;
                providerOwned = true;
                break;
            case "sentinel20-control":
                handle = 0;
                modeId = GraphicsModeIds.Invalid;
                graphicsBase = uint.MaxValue;
                expectedResult = 0;
                ownedZero = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "disp20-ordinary-exact-end-lisa")
        {
            Assert.Equal(
                (ulong)uint.MaxValue,
                (ulong)destination + requestedBytes - 1u);
        }
        if (scenario.EndsWith("first-wrap-control", StringComparison.Ordinal) ||
            scenario == "disp20-superhires-first-wrap-destination-control")
        {
            Assert.Equal(0xFFFF_FFEDu, destination);
            Assert.Equal(
                (ulong)uint.MaxValue + 1u,
                (ulong)destination + requestedBytes - 1u);
        }

        if (chipRevision.HasValue)
            fixture.SeedChipRevision(graphicsBase, chipRevision.Value);

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(
                tag,
                handle,
                modeId,
                requestedBytes,
                supportsEcsOracle,
                supportsAgaOracle);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);

            var expectedHeader = BuildQueryHeader(tag, expectedMode);
            Assert.Equal(expectedHeader, expectedBytes.Take(expectedHeader.Length));
            if (tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes == 20u)
            {
                var superHires =
                    (expectedMode & GraphicsModeIds.SuperHiresMode) != 0;
                Assert.Equal((byte)0, expectedBytes[16]);
                Assert.Equal(
                    superHires && !supportsEcsOracle ? (byte)1 : (byte)0,
                    expectedBytes[17]);
                Assert.Equal((byte)0, expectedBytes[18]);
                Assert.Equal(
                    supportsAgaOracle ? (byte)0x11 : (byte)0x10,
                    expectedBytes[19]);
            }
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes
            ? fixture.ReadOutputByte(followingAddress)
            : (byte)0;
        var result = fixture.Invoke(
            handle,
            destination,
            requestedBytes,
            tag,
            modeId,
            graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        var expectedCapabilityReads = logicalCapabilityReads;

        Check(failures, "D0 result and native/provider terminal", () =>
        {
            Assert.Equal(expectedResult, result.Data0);
            Assert.False(result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned)
            {
                Assert.Equal(
                    BranchAlwaysWordOpcode,
                    result.NativeReturnPredecessorOpcode);
            }
            else if (publishes)
            {
                Assert.Equal(
                    AddQuickFourLongA7Opcode,
                    result.NativeReturnPredecessorOpcode);
            }
            else if (ownedZero)
            {
                Assert.Equal(
                    MoveQuickZeroD0Opcode,
                    result.NativeReturnPredecessorOpcode);
            }
            else if (malformedForeignSpan)
            {
                Assert.NotEqual(
                    BranchAlwaysWordOpcode,
                    result.NativeReturnPredecessorOpcode);
            }
        });
        Check(failures, "capability/default-monitor read count", () =>
        {
            Assert.Equal(expectedCapabilityReads, result.CapabilityReadCount);
            Assert.Equal(0, result.DefaultMonitorReadCount);
        });
        Check(failures, "caller PC/SP, D1, A1, and supplied A6", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(graphicsBase, result.Address6);
        });
        Check(failures, "D2-D7/A2-A6 public frame", () =>
            Assert.True(
                result.RegisterDifferences.Length == 0,
                string.Join("; ", result.RegisterDifferences)));

        if (publishes)
        {
            Check(failures, "only the exact portable prefix changes", () =>
                fixture.AssertOnlyOutputPrefixChanged(
                    before,
                    after,
                    destination,
                    expectedBytes));
            Check(failures, "the byte following the output remains untouched", () =>
                Assert.Equal(
                    followingByte,
                    fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "control or provider-owned calls leave memory intact", () =>
                AssertMemoryEqual(before, after));
            Check(failures, "control or provider-owned calls never access A1", () =>
                AssertNoDestinationAccess(result, destination, requestedBytes));
        }

        Assert.True(
            failures.Count == 0,
            $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(DisplayTwentyOneByteCases))]
    public void GetDisplayInfoDataPublishesTheTwentyOneByteDisplayPrefix(
        bool relocated,
        bool autoInitEntry,
        string scenario,
        uint requestedBytes)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        var finalCanonicalMode =
            GraphicsModeIds.NtscMonitor |
            GraphicsModeIds.ExtraHalfBriteLaceKey;
        var ehbMode =
            GraphicsModeIds.PalMonitor |
            GraphicsModeIds.ExtraHalfBriteKey;
        var superHiresMode =
            GraphicsModeIds.PalMonitor |
            GraphicsModeIds.SuperHiresKey;
        AssertDisplayTwentyBytePortableSeam(ModeId, superHiresMode);
        AssertDisplayTwentyOneBytePortableSeam(ModeId, ehbMode, superHiresMode);

        using var fixture = new Fixture(relocated, autoInitEntry);
        var maximumCapabilityBase =
            uint.MaxValue - (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        var lastAlignedCapabilityBase = maximumCapabilityBase & ~1u;
        var firstWrapCapabilityBase = maximumCapabilityBase + 1u;
        Assert.Equal(0xFFFF_FF13u, maximumCapabilityBase);
        Assert.Equal(0xFFFF_FF12u, lastAlignedCapabilityBase);
        Assert.Equal(0xFFFF_FF14u, firstWrapCapabilityBase);

        var handle = ModeId;
        var destination = 0x300u;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = foreignMode;
        var graphicsBase = fixture.GraphicsBase;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var expectedByte20 = (byte)0x21;
        var publishes = false;
        var providerOwned = false;
        var ownedZero = false;
        var malformedForeignSpan = false;
        var supportsEcsOracle = true;
        var supportsAgaOracle = false;
        byte? chipRevision = null;
        var logicalCapabilityReads = 0;

        switch (scenario)
        {
            case "disp21-ordinary-even-authoritative-ecs":
                publishes = true;
                chipRevision = (byte)GraphicsChipRevision.SetEcs;
                logicalCapabilityReads = 1;
                break;
            case "disp21-ordinary-odd-alice":
                publishes = true;
                destination = 0x301u;
                supportsEcsOracle = false;
                chipRevision = (byte)GraphicsChipRevision.AaAlice;
                logicalCapabilityReads = 1;
                break;
            case "disp21-ordinary-exact-end-lisa":
                publishes = true;
                destination = 0xFFFF_FFEBu;
                supportsEcsOracle = false;
                chipRevision = (byte)GraphicsChipRevision.AaLisa;
                logicalCapabilityReads = 1;
                break;
            case "disp21-null-final-canonical-aa":
                publishes = true;
                handle = 0;
                modeId = finalCanonicalMode;
                expectedMode = finalCanonicalMode;
                expectedByte20 = 0x31;
                supportsEcsOracle = false;
                supportsAgaOracle = true;
                chipRevision = (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa);
                logicalCapabilityReads = 1;
                break;
            case "disp21-ordinary-private-default-aa-pair":
                publishes = true;
                handle = GraphicsDisplayDatabase.DefaultModeHandle;
                expectedMode = GraphicsModeIds.DefaultMonitor;
                supportsEcsOracle = false;
                supportsAgaOracle = true;
                chipRevision = (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa);
                logicalCapabilityReads = 1;
                break;
            case "disp21-ordinary-last-aligned-setaa":
                publishes = true;
                graphicsBase = lastAlignedCapabilityBase;
                supportsAgaOracle = true;
                chipRevision = (byte)GraphicsChipRevision.SetAa;
                logicalCapabilityReads = 1;
                break;
            case "disp21-ehb-ocs":
                publishes = true;
                handle = ehbMode;
                expectedMode = ehbMode;
                expectedByte20 = 0x31;
                graphicsBase = 2;
                supportsEcsOracle = false;
                chipRevision = 0;
                logicalCapabilityReads = 1;
                break;
            case "disp21-ehb-setaa":
                publishes = true;
                handle = ehbMode;
                expectedMode = ehbMode;
                expectedByte20 = 0x31;
                supportsAgaOracle = true;
                chipRevision = (byte)GraphicsChipRevision.SetAa;
                logicalCapabilityReads = 1;
                break;
            case "disp21-superhires-ocs":
                publishes = true;
                handle = superHiresMode;
                expectedMode = superHiresMode;
                expectedByte20 = 0x01;
                graphicsBase = 2;
                supportsEcsOracle = false;
                chipRevision = 0;
                logicalCapabilityReads = 1;
                break;
            case "disp21-superhires-ecs":
                publishes = true;
                handle = superHiresMode;
                expectedMode = superHiresMode;
                expectedByte20 = 0x01;
                chipRevision = (byte)GraphicsChipRevision.SetEcs;
                logicalCapabilityReads = 1;
                break;
            case "disp21-superhires-aa-pair":
                publishes = true;
                handle = superHiresMode;
                expectedMode = superHiresMode;
                expectedByte20 = 0x01;
                supportsEcsOracle = false;
                supportsAgaOracle = true;
                chipRevision = (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa);
                logicalCapabilityReads = 1;
                break;
            case "disp21-superhires-setaa":
                publishes = true;
                handle = superHiresMode;
                expectedMode = superHiresMode;
                expectedByte20 = 0x01;
                supportsAgaOracle = true;
                chipRevision = (byte)GraphicsChipRevision.SetAa;
                logicalCapabilityReads = 1;
                break;
            case "disp21-foreign-provider":
                handle = foreignMode;
                modeId = ModeId;
                graphicsBase = uint.MaxValue;
                providerOwned = true;
                break;
            case "disp21-ordinary-null-a6-control":
                graphicsBase = 0;
                break;
            case "disp21-ordinary-odd-a6-control":
                graphicsBase = 3;
                break;
            case "disp21-ordinary-exact-end-a6-control":
                graphicsBase = maximumCapabilityBase;
                break;
            case "disp21-ordinary-first-wrap-a6-control":
                graphicsBase = firstWrapCapabilityBase;
                break;
            case "disp21-first-wrap-control":
                destination = 0xFFFF_FFECu;
                graphicsBase = uint.MaxValue;
                break;
            case "disp21-superhires-first-wrap-destination-control":
                handle = superHiresMode;
                expectedMode = superHiresMode;
                destination = 0xFFFF_FFECu;
                chipRevision = (byte)GraphicsChipRevision.SetEcs;
                break;
            case "disp21-foreign-first-wrap-control":
                handle = foreignMode;
                modeId = ModeId;
                destination = 0xFFFF_FFECu;
                graphicsBase = uint.MaxValue;
                malformedForeignSpan = true;
                break;
            case "disp20-ordinary-valid-control":
                publishes = true;
                chipRevision = (byte)GraphicsChipRevision.SetEcs;
                logicalCapabilityReads = 1;
                break;
            case "disp20-superhires-valid-control":
                publishes = true;
                handle = superHiresMode;
                expectedMode = superHiresMode;
                chipRevision = (byte)GraphicsChipRevision.SetEcs;
                logicalCapabilityReads = 1;
                break;
            case "disp55-control":
                graphicsBase = uint.MaxValue;
                break;
            case "disp56-control":
                publishes = true;
                chipRevision = (byte)GraphicsChipRevision.SetEcs;
                logicalCapabilityReads = 1;
                break;
            case "name21-control":
                publishes = true;
                tag = GraphicsDisplayDatabase.DtagName;
                graphicsBase = uint.MaxValue;
                break;
            case "dims21-control":
                tag = GraphicsDisplayDatabase.DtagDims;
                graphicsBase = uint.MaxValue;
                break;
            case "mntr21-control":
                tag = GraphicsDisplayDatabase.DtagMntr;
                graphicsBase = uint.MaxValue;
                break;
            case "unsupported21-control":
                tag = UnsupportedTag;
                graphicsBase = uint.MaxValue;
                expectedResult = 0;
                ownedZero = true;
                break;
            case "vec21-provider-control":
                tag = GraphicsDisplayDatabase.DtagVec;
                graphicsBase = uint.MaxValue;
                providerOwned = true;
                break;
            case "sentinel21-control":
                handle = 0;
                modeId = GraphicsModeIds.Invalid;
                graphicsBase = uint.MaxValue;
                expectedResult = 0;
                ownedZero = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "disp21-ordinary-exact-end-lisa")
        {
            Assert.Equal(
                (ulong)uint.MaxValue,
                (ulong)destination + requestedBytes - 1u);
        }
        if (scenario.EndsWith("first-wrap-control", StringComparison.Ordinal) ||
            scenario == "disp21-superhires-first-wrap-destination-control")
        {
            Assert.Equal(0xFFFF_FFECu, destination);
            Assert.Equal(
                (ulong)uint.MaxValue + 1u,
                (ulong)destination + requestedBytes - 1u);
        }

        if (chipRevision.HasValue)
            fixture.SeedChipRevision(graphicsBase, chipRevision.Value);

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(
                tag,
                handle,
                modeId,
                requestedBytes,
                supportsEcsOracle,
                supportsAgaOracle);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);

            var expectedHeader = BuildQueryHeader(tag, expectedMode);
            Assert.Equal(expectedHeader, expectedBytes.Take(expectedHeader.Length));
            if (tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes == 21u)
            {
                var superHires =
                    (expectedMode & GraphicsModeIds.SuperHiresMode) != 0;
                Assert.Equal((byte)0, expectedBytes[16]);
                Assert.Equal(
                    superHires && !supportsEcsOracle ? (byte)1 : (byte)0,
                    expectedBytes[17]);
                Assert.Equal((byte)0, expectedBytes[18]);
                Assert.Equal(
                    supportsAgaOracle ? (byte)0x11 : (byte)0x10,
                    expectedBytes[19]);
                Assert.Equal(expectedByte20, expectedBytes[20]);
            }
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes
            ? fixture.ReadOutputByte(followingAddress)
            : (byte)0;
        var result = fixture.Invoke(
            handle,
            destination,
            requestedBytes,
            tag,
            modeId,
            graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        var expectedCapabilityReads = logicalCapabilityReads;

        Check(failures, "D0 result and native/provider terminal", () =>
        {
            Assert.Equal(expectedResult, result.Data0);
            Assert.False(result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned)
            {
                Assert.Equal(
                    BranchAlwaysWordOpcode,
                    result.NativeReturnPredecessorOpcode);
            }
            else if (publishes)
            {
                Assert.Equal(
                    AddQuickFourLongA7Opcode,
                    result.NativeReturnPredecessorOpcode);
            }
            else if (ownedZero)
            {
                Assert.Equal(
                    MoveQuickZeroD0Opcode,
                    result.NativeReturnPredecessorOpcode);
            }
            else if (malformedForeignSpan)
            {
                Assert.NotEqual(
                    BranchAlwaysWordOpcode,
                    result.NativeReturnPredecessorOpcode);
            }
        });
        Check(failures, "capability/default-monitor read count", () =>
        {
            Assert.Equal(expectedCapabilityReads, result.CapabilityReadCount);
            Assert.Equal(0, result.DefaultMonitorReadCount);
        });
        Check(failures, "caller PC/SP, D1, A1, and supplied A6", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(graphicsBase, result.Address6);
        });
        Check(failures, "D2-D7/A2-A6 public frame", () =>
            Assert.True(
                result.RegisterDifferences.Length == 0,
                string.Join("; ", result.RegisterDifferences)));

        if (publishes)
        {
            Check(failures, "only the exact portable prefix changes", () =>
                fixture.AssertOnlyOutputPrefixChanged(
                    before,
                    after,
                    destination,
                    expectedBytes));
            Check(failures, "the byte following the output remains untouched", () =>
                Assert.Equal(
                    followingByte,
                    fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "control or provider-owned calls leave memory intact", () =>
                AssertMemoryEqual(before, after));
            Check(failures, "control or provider-owned calls never access A1", () =>
                AssertNoDestinationAccess(result, destination, requestedBytes));
        }

        Assert.True(
            failures.Count == 0,
            $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(DisplayTwentyTwoByteCases))]
    public void GetDisplayInfoDataPublishesTheTwentyTwoByteDisplayPrefix(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        const uint requestedBytes22 = 22;
        AssertDisplayTwentyTwoBytePortableSeam();

        using var fixture = new Fixture(relocated, autoInitEntry);
        var maximumCapabilityBase = uint.MaxValue - (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        var lastAlignedCapabilityBase = maximumCapabilityBase & ~1u;
        var firstWrapCapabilityBase = maximumCapabilityBase + 1u;
        var handle = ModeId;
        var destination = 0x300u;
        var requestedBytes = requestedBytes22;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = foreignMode;
        var graphicsBase = fixture.GraphicsBase;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var publishes = false;
        var providerOwned = false;
        var ownedZero = false;
        var malformedForeignSpan = false;
        var supportsEcsOracle = true;
        var supportsAgaOracle = false;
        byte? chipRevision = null;
        var logicalCapabilityReads = 0;

        void Publish(uint mode, byte chip, bool ecs, bool aga)
        {
            publishes = true;
            handle = mode;
            expectedMode = mode;
            chipRevision = chip;
            supportsEcsOracle = ecs;
            supportsAgaOracle = aga;
            logicalCapabilityReads = 1;
        }

        switch (scenario)
        {
            case "disp22-pal-ordinary-even-ecs":
                Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey,
                    (byte)GraphicsChipRevision.SetEcs, true, false);
                break;
            case "disp22-ntsc-ordinary-odd-alice":
                Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresKey,
                    (byte)GraphicsChipRevision.AaAlice, false, false);
                destination = 0x301;
                break;
            case "disp22-ntsc-lace-exact-end-lisa":
                Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresLaceKey,
                    (byte)GraphicsChipRevision.AaLisa, false, false);
                destination = 0xFFFF_FFEA;
                break;
            case "disp22-null-ntsc-ehb-lace-aa-pair":
                expectedMode = GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteLaceKey;
                Publish(expectedMode, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true);
                handle = 0;
                modeId = expectedMode;
                break;
            case "disp22-private-default-aa-pair":
                Publish(GraphicsModeIds.DefaultMonitor,
                    (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true);
                handle = GraphicsDisplayDatabase.DefaultModeHandle;
                break;
            case "disp22-pal-dpf-last-aligned-setaa":
                Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresDualPlayfieldKey,
                    (byte)GraphicsChipRevision.SetAa, true, true);
                graphicsBase = lastAlignedCapabilityBase;
                break;
            case "disp22-ntsc-dpf2-first-even-ocs":
                Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresDualPlayfieldTwoKey, 0, false, false);
                graphicsBase = 2;
                break;
            case "disp22-pal-ham-setaa":
                Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HamKey,
                    (byte)GraphicsChipRevision.SetAa, true, true);
                break;
            case "disp22-ntsc-ehb-ocs":
                Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteKey, 0, false, false);
                graphicsBase = 2;
                break;
            case "disp22-pal-superhires-ocs":
                Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, 0, false, false);
                graphicsBase = 2;
                break;
            case "disp22-ntsc-superhires-ecs":
                Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey,
                    (byte)GraphicsChipRevision.SetEcs, true, false);
                break;
            case "disp22-pal-superhires-dpf2-lace-aa-pair":
                Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresDualPlayfieldTwoLaceKey,
                    (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true);
                break;
            case "disp22-foreign-provider":
                handle = foreignMode; modeId = ModeId; graphicsBase = uint.MaxValue; providerOwned = true;
                break;
            case "disp22-null-a6-control": graphicsBase = 0; break;
            case "disp22-odd-a6-control": graphicsBase = 3; break;
            case "disp22-exact-end-a6-control": graphicsBase = maximumCapabilityBase; break;
            case "disp22-first-wrap-a6-control": graphicsBase = firstWrapCapabilityBase; break;
            case "disp22-first-wrap-control":
                destination = 0xFFFF_FFEB; graphicsBase = uint.MaxValue; break;
            case "disp22-superhires-first-wrap-control":
                handle = GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey;
                expectedMode = handle; destination = 0xFFFF_FFEB; break;
            case "disp22-foreign-first-wrap-control":
                handle = foreignMode; modeId = ModeId; destination = 0xFFFF_FFEB;
                graphicsBase = uint.MaxValue; malformedForeignSpan = true; break;
            case "disp20-ordinary-valid-control": requestedBytes = 20; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "disp21-superhires-valid-control":
                requestedBytes = 21; Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey,
                    (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp55-control": requestedBytes = 55; expectedResult = requestedBytes; graphicsBase = uint.MaxValue; break;
            case "disp56-control": requestedBytes = 56; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "name22-control": tag = GraphicsDisplayDatabase.DtagName; publishes = true; graphicsBase = uint.MaxValue; break;
            case "dims22-control": tag = GraphicsDisplayDatabase.DtagDims; graphicsBase = uint.MaxValue; break;
            case "mntr22-control": tag = GraphicsDisplayDatabase.DtagMntr; graphicsBase = uint.MaxValue; break;
            case "unsupported22-control": tag = UnsupportedTag; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            case "vec22-provider-control": tag = GraphicsDisplayDatabase.DtagVec; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "sentinel22-control": handle = 0; modeId = GraphicsModeIds.Invalid; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "disp22-ntsc-lace-exact-end-lisa")
            Assert.Equal((ulong)uint.MaxValue, (ulong)destination + requestedBytes - 1);
        if (scenario is "disp22-first-wrap-control" or "disp22-superhires-first-wrap-control" or "disp22-foreign-first-wrap-control")
            Assert.Equal((ulong)uint.MaxValue + 1, (ulong)destination + requestedBytes - 1);
        if (chipRevision.HasValue)
            fixture.SeedChipRevision(graphicsBase, chipRevision.Value);

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(tag, handle, modeId, requestedBytes, supportsEcsOracle, supportsAgaOracle);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);
            Assert.Equal(BuildQueryHeader(tag, expectedMode), expectedBytes.Take(16));
            if (tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes == 22)
            {
                Assert.Equal(ExpectedDisplayByte15(expectedMode), expectedBytes[21]);
                Assert.Equal(supportsAgaOracle ? (byte)0x11 : (byte)0x10, expectedBytes[19]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 && !supportsEcsOracle ? (byte)1 : (byte)0, expectedBytes[17]);
            }
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes ? fixture.ReadOutputByte(followingAddress) : (byte)0;
        var result = fixture.Invoke(handle, destination, requestedBytes, tag, modeId, graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        var expectedCapabilityReads = logicalCapabilityReads;
        Check(failures, "result and terminal provenance", () =>
        {
            Assert.Equal(expectedResult, result.Data0); Assert.False(result.UsedFallback); Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned) Assert.Equal(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
            else if (publishes) Assert.Equal(AddQuickFourLongA7Opcode, result.NativeReturnPredecessorOpcode);
            else if (ownedZero) Assert.Equal(MoveQuickZeroD0Opcode, result.NativeReturnPredecessorOpcode);
            else if (malformedForeignSpan) Assert.NotEqual(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
        });
        Check(failures, "capability/default-monitor reads", () => { Assert.Equal(expectedCapabilityReads, result.CapabilityReadCount); Assert.Equal(0, result.DefaultMonitorReadCount); });
        Check(failures, "caller ABI", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter); Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1); Assert.Equal(destination, result.Address1); Assert.Equal(graphicsBase, result.Address6);
            if (publishes && tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes is 22 or 23)
                Assert.Equal((ushort)0, (ushort)(result.StatusRegister & M68kCpuState.Extend));
            Assert.Empty(result.RegisterDifferences);
        });
        if (publishes)
        {
            Check(failures, "exact portable prefix", () => fixture.AssertOnlyOutputPrefixChanged(before, after, destination, expectedBytes));
            Check(failures, "following byte untouched", () => Assert.Equal(followingByte, fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "memory intact", () => AssertMemoryEqual(before, after));
            Check(failures, "no destination access", () => AssertNoDestinationAccess(result, destination, requestedBytes));
        }
        Assert.True(failures.Count == 0, $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(DisplayTwentyThreeByteCases))]
    public void GetDisplayInfoDataPublishesTheTwentyThreeByteDisplayPrefix(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        AssertDisplayTwentyThreeBytePortableSeam();

        using var fixture = new Fixture(relocated, autoInitEntry);
        var maximumCapabilityBase = uint.MaxValue - (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        var lastAlignedCapabilityBase = maximumCapabilityBase & ~1u;
        var firstWrapCapabilityBase = maximumCapabilityBase + 1u;
        var handle = ModeId;
        var destination = 0x300u;
        var requestedBytes = 23u;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = foreignMode;
        var graphicsBase = fixture.GraphicsBase;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var publishes = false;
        var providerOwned = false;
        var ownedZero = false;
        var malformedForeignSpan = false;
        var supportsEcsOracle = true;
        var supportsAgaOracle = false;
        byte? chipRevision = null;
        var logicalCapabilityReads = 0;

        void Publish(uint mode, byte chip, bool ecs, bool aga)
        {
            publishes = true;
            handle = mode;
            expectedMode = mode;
            chipRevision = chip;
            supportsEcsOracle = ecs;
            supportsAgaOracle = aga;
            logicalCapabilityReads = 1;
        }

        switch (scenario)
        {
            case "disp23-pal-ordinary-even-ecs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp23-ntsc-ordinary-odd-alice": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.AaAlice, false, false); destination = 0x301; break;
            case "disp23-ntsc-lace-exact-end-lisa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresLaceKey, (byte)GraphicsChipRevision.AaLisa, false, false); destination = 0xFFFF_FFE9; break;
            case "disp23-null-ntsc-ehb-lace-aa-pair":
                expectedMode = GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteLaceKey;
                Publish(expectedMode, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true);
                handle = 0; modeId = expectedMode; break;
            case "disp23-private-default-aa-pair": Publish(GraphicsModeIds.DefaultMonitor, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); handle = GraphicsDisplayDatabase.DefaultModeHandle; break;
            case "disp23-pal-dpf-last-aligned-setaa": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresDualPlayfieldKey, (byte)GraphicsChipRevision.SetAa, true, true); graphicsBase = lastAlignedCapabilityBase; break;
            case "disp23-ntsc-dpf2-first-even-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresDualPlayfieldTwoKey, 0, false, false); graphicsBase = 2; break;
            case "disp23-pal-ham-setaa": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HamKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp23-ntsc-ehb-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteKey, 0, false, false); graphicsBase = 2; break;
            case "disp23-pal-superhires-ocs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, 0, false, false); graphicsBase = 2; break;
            case "disp23-ntsc-superhires-ecs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp23-pal-superhires-dpf2-lace-aa-pair": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresDualPlayfieldTwoLaceKey, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); break;
            case "disp23-foreign-provider": handle = foreignMode; modeId = ModeId; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "disp23-null-a6-control": graphicsBase = 0; break;
            case "disp23-odd-a6-control": graphicsBase = 3; break;
            case "disp23-exact-end-a6-control": graphicsBase = maximumCapabilityBase; break;
            case "disp23-first-wrap-a6-control": graphicsBase = firstWrapCapabilityBase; break;
            case "disp23-first-wrap-control": destination = 0xFFFF_FFEA; graphicsBase = uint.MaxValue; break;
            case "disp23-superhires-first-wrap-control": handle = GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey; expectedMode = handle; destination = 0xFFFF_FFEA; break;
            case "disp23-foreign-first-wrap-control": handle = foreignMode; modeId = ModeId; destination = 0xFFFF_FFEA; graphicsBase = uint.MaxValue; malformedForeignSpan = true; break;
            case "disp21-ordinary-valid-control": requestedBytes = 21; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "disp22-superhires-valid-control": requestedBytes = 22; Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp55-control": requestedBytes = 55; expectedResult = requestedBytes; graphicsBase = uint.MaxValue; break;
            case "disp56-control": requestedBytes = 56; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "name23-control": tag = GraphicsDisplayDatabase.DtagName; publishes = true; graphicsBase = uint.MaxValue; break;
            case "dims23-control": tag = GraphicsDisplayDatabase.DtagDims; graphicsBase = uint.MaxValue; break;
            case "mntr23-control": tag = GraphicsDisplayDatabase.DtagMntr; graphicsBase = uint.MaxValue; break;
            case "unsupported23-control": tag = UnsupportedTag; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            case "vec23-provider-control": tag = GraphicsDisplayDatabase.DtagVec; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "sentinel23-control": handle = 0; modeId = GraphicsModeIds.Invalid; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "disp23-ntsc-lace-exact-end-lisa")
            Assert.Equal((ulong)uint.MaxValue, (ulong)destination + requestedBytes - 1);
        if (scenario is "disp23-first-wrap-control" or "disp23-superhires-first-wrap-control" or "disp23-foreign-first-wrap-control")
            Assert.Equal((ulong)uint.MaxValue + 1, (ulong)destination + requestedBytes - 1);
        if (chipRevision.HasValue)
            fixture.SeedChipRevision(graphicsBase, chipRevision.Value);

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(tag, handle, modeId, requestedBytes, supportsEcsOracle, supportsAgaOracle);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);
            Assert.Equal(BuildQueryHeader(tag, expectedMode), expectedBytes.Take(16));
            if (tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes == 23)
            {
                Assert.Equal((byte)0, expectedBytes[22]);
                Assert.Equal(ExpectedDisplayByte15(expectedMode), expectedBytes[21]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 ? (byte)0x01 :
                    (expectedMode & GraphicsModeIds.ExtraHalfBriteMode) != 0 ? (byte)0x31 : (byte)0x21, expectedBytes[20]);
                Assert.Equal(supportsAgaOracle ? (byte)0x11 : (byte)0x10, expectedBytes[19]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 && !supportsEcsOracle ? (byte)1 : (byte)0, expectedBytes[17]);
            }
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes ? fixture.ReadOutputByte(followingAddress) : (byte)0;
        var result = fixture.Invoke(handle, destination, requestedBytes, tag, modeId, graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        var expectedCapabilityReads = logicalCapabilityReads;
        Check(failures, "result and terminal provenance", () =>
        {
            Assert.Equal(expectedResult, result.Data0); Assert.False(result.UsedFallback); Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned) Assert.Equal(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
            else if (publishes) Assert.Equal(AddQuickFourLongA7Opcode, result.NativeReturnPredecessorOpcode);
            else if (ownedZero) Assert.Equal(MoveQuickZeroD0Opcode, result.NativeReturnPredecessorOpcode);
            else if (malformedForeignSpan) Assert.NotEqual(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
        });
        Check(failures, "capability/default-monitor reads", () => { Assert.Equal(expectedCapabilityReads, result.CapabilityReadCount); Assert.Equal(0, result.DefaultMonitorReadCount); });
        Check(failures, "caller ABI", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter); Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1); Assert.Equal(destination, result.Address1); Assert.Equal(graphicsBase, result.Address6);
            if (publishes && tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes is 22 or 23)
                Assert.Equal((ushort)0, (ushort)(result.StatusRegister & M68kCpuState.Extend));
            Assert.Empty(result.RegisterDifferences);
        });
        if (publishes)
        {
            Check(failures, "exact portable prefix", () => fixture.AssertOnlyOutputPrefixChanged(before, after, destination, expectedBytes));
            Check(failures, "following byte untouched", () => Assert.Equal(followingByte, fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "memory intact", () => AssertMemoryEqual(before, after));
            Check(failures, "no destination access", () => AssertNoDestinationAccess(result, destination, requestedBytes));
        }
        Assert.True(failures.Count == 0, $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(DisplayTwentyFourByteCases))]
    public void GetDisplayInfoDataPublishesTheTwentyFourByteDisplayPrefix(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        AssertDisplayTwentyFourBytePortableSeam();

        using var fixture = new Fixture(relocated, autoInitEntry);
        var maximumCapabilityBase = uint.MaxValue - (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        var lastAlignedCapabilityBase = maximumCapabilityBase & ~1u;
        var firstWrapCapabilityBase = maximumCapabilityBase + 1u;
        var handle = ModeId;
        var destination = 0x300u;
        var requestedBytes = 24u;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = foreignMode;
        var graphicsBase = fixture.GraphicsBase;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var publishes = false;
        var providerOwned = false;
        var ownedZero = false;
        var malformedForeignSpan = false;
        var supportsEcsOracle = true;
        var supportsAgaOracle = false;
        byte? chipRevision = null;
        var logicalCapabilityReads = 0;

        void Publish(uint mode, byte chip, bool ecs, bool aga)
        {
            publishes = true;
            handle = mode;
            expectedMode = mode;
            chipRevision = chip;
            supportsEcsOracle = ecs;
            supportsAgaOracle = aga;
            logicalCapabilityReads = 1;
        }

        switch (scenario)
        {
            case "disp24-pal-ordinary-even-ecs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp24-ntsc-ordinary-odd-alice": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.AaAlice, false, false); destination = 0x301; break;
            case "disp24-ntsc-lace-exact-end-lisa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresLaceKey, (byte)GraphicsChipRevision.AaLisa, false, false); destination = 0xFFFF_FFE8; break;
            case "disp24-null-ntsc-ehb-lace-aa-pair":
                expectedMode = GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteLaceKey;
                Publish(expectedMode, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true);
                handle = 0; modeId = expectedMode; break;
            case "disp24-private-default-aa-pair": Publish(GraphicsModeIds.DefaultMonitor, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); handle = GraphicsDisplayDatabase.DefaultModeHandle; break;
            case "disp24-pal-dpf-last-aligned-setaa": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresDualPlayfieldKey, (byte)GraphicsChipRevision.SetAa, true, true); graphicsBase = lastAlignedCapabilityBase; break;
            case "disp24-ntsc-dpf2-first-even-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresDualPlayfieldTwoKey, 0, false, false); graphicsBase = 2; break;
            case "disp24-default-ham-setaa": Publish(GraphicsModeIds.DefaultMonitor | GraphicsModeIds.HamKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp24-ntsc-ehb-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteKey, 0, false, false); graphicsBase = 2; break;
            case "disp24-pal-superhires-ocs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, 0, false, false); graphicsBase = 2; break;
            case "disp24-ntsc-superhires-ecs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp24-pal-superhires-dpf2-lace-aa-pair": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresDualPlayfieldTwoLaceKey, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); break;
            case "disp24-foreign-provider": handle = foreignMode; modeId = ModeId; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "disp24-null-a6-control": graphicsBase = 0; break;
            case "disp24-odd-a6-control": graphicsBase = 3; break;
            case "disp24-exact-end-a6-control": graphicsBase = maximumCapabilityBase; break;
            case "disp24-first-wrap-a6-control": graphicsBase = firstWrapCapabilityBase; break;
            case "disp24-first-wrap-control": destination = 0xFFFF_FFE9; graphicsBase = uint.MaxValue; break;
            case "disp24-superhires-first-wrap-control": handle = GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey; expectedMode = handle; destination = 0xFFFF_FFE9; break;
            case "disp24-foreign-first-wrap-control": handle = foreignMode; modeId = ModeId; destination = 0xFFFF_FFE9; graphicsBase = uint.MaxValue; malformedForeignSpan = true; break;
            case "disp22-ordinary-valid-control": requestedBytes = 22; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "disp23-superhires-valid-control": requestedBytes = 23; Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp55-control": requestedBytes = 55; expectedResult = requestedBytes; graphicsBase = uint.MaxValue; break;
            case "disp56-control": requestedBytes = 56; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "name24-control": tag = GraphicsDisplayDatabase.DtagName; publishes = true; graphicsBase = uint.MaxValue; break;
            case "dims24-control": tag = GraphicsDisplayDatabase.DtagDims; graphicsBase = uint.MaxValue; break;
            case "mntr24-control": tag = GraphicsDisplayDatabase.DtagMntr; graphicsBase = uint.MaxValue; break;
            case "unsupported24-control": tag = UnsupportedTag; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            case "vec24-provider-control": tag = GraphicsDisplayDatabase.DtagVec; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "sentinel24-control": handle = 0; modeId = GraphicsModeIds.Invalid; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "disp24-ntsc-lace-exact-end-lisa")
            Assert.Equal((ulong)uint.MaxValue, (ulong)destination + requestedBytes - 1);
        if (scenario is "disp24-first-wrap-control" or "disp24-superhires-first-wrap-control" or "disp24-foreign-first-wrap-control")
            Assert.Equal((ulong)uint.MaxValue + 1, (ulong)destination + requestedBytes - 1);
        if (chipRevision.HasValue)
            fixture.SeedChipRevision(graphicsBase, chipRevision.Value);

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(tag, handle, modeId, requestedBytes, supportsEcsOracle, supportsAgaOracle);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);
            Assert.Equal(BuildQueryHeader(tag, expectedMode), expectedBytes.Take(16));
            if (tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes == 24)
            {
                Assert.Equal(ExpectedDisplayByte17(expectedMode), expectedBytes[23]);
                Assert.Equal((byte)0, expectedBytes[22]);
                Assert.Equal(ExpectedDisplayByte15(expectedMode), expectedBytes[21]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 ? (byte)0x01 :
                    (expectedMode & GraphicsModeIds.ExtraHalfBriteMode) != 0 ? (byte)0x31 : (byte)0x21, expectedBytes[20]);
                Assert.Equal(supportsAgaOracle ? (byte)0x11 : (byte)0x10, expectedBytes[19]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 && !supportsEcsOracle ? (byte)1 : (byte)0, expectedBytes[17]);
            }
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes ? fixture.ReadOutputByte(followingAddress) : (byte)0;
        var result = fixture.Invoke(handle, destination, requestedBytes, tag, modeId, graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        var expectedCapabilityReads = logicalCapabilityReads;
        Check(failures, "result and terminal provenance", () =>
        {
            Assert.Equal(expectedResult, result.Data0); Assert.False(result.UsedFallback); Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned) Assert.Equal(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
            else if (publishes) Assert.Equal(AddQuickFourLongA7Opcode, result.NativeReturnPredecessorOpcode);
            else if (ownedZero) Assert.Equal(MoveQuickZeroD0Opcode, result.NativeReturnPredecessorOpcode);
            else if (malformedForeignSpan) Assert.NotEqual(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
        });
        Check(failures, "capability/default-monitor reads", () => { Assert.Equal(expectedCapabilityReads, result.CapabilityReadCount); Assert.Equal(0, result.DefaultMonitorReadCount); });
        Check(failures, "caller ABI", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter); Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1); Assert.Equal(destination, result.Address1); Assert.Equal(graphicsBase, result.Address6);
            if (publishes && tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes is 23 or 24)
                Assert.Equal((ushort)0, (ushort)(result.StatusRegister & M68kCpuState.Extend));
            Assert.Empty(result.RegisterDifferences);
        });
        if (publishes)
        {
            Check(failures, "exact portable prefix", () => fixture.AssertOnlyOutputPrefixChanged(before, after, destination, expectedBytes));
            Check(failures, "following byte untouched", () => Assert.Equal(followingByte, fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "memory intact", () => AssertMemoryEqual(before, after));
            Check(failures, "no destination access", () => AssertNoDestinationAccess(result, destination, requestedBytes));
        }
        Assert.True(failures.Count == 0, $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(DisplayTwentyFiveByteCases))]
    public void GetDisplayInfoDataPublishesTheTwentyFiveByteDisplayPrefix(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        AssertDisplayTwentyFiveBytePortableSeam();

        using var fixture = new Fixture(relocated, autoInitEntry);
        var maximumCapabilityBase = uint.MaxValue - (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        var lastAlignedCapabilityBase = maximumCapabilityBase & ~1u;
        var firstWrapCapabilityBase = maximumCapabilityBase + 1u;
        var handle = ModeId;
        var destination = 0x300u;
        var requestedBytes = 25u;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = foreignMode;
        var graphicsBase = fixture.GraphicsBase;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var publishes = false;
        var providerOwned = false;
        var ownedZero = false;
        var malformedForeignSpan = false;
        var supportsEcsOracle = true;
        var supportsAgaOracle = false;
        byte? chipRevision = null;
        var logicalCapabilityReads = 0;

        void Publish(uint mode, byte chip, bool ecs, bool aga)
        {
            publishes = true;
            handle = mode;
            expectedMode = mode;
            chipRevision = chip;
            supportsEcsOracle = ecs;
            supportsAgaOracle = aga;
            logicalCapabilityReads = 1;
        }

        switch (scenario)
        {
            case "disp25-pal-ordinary-even-ecs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp25-ntsc-ordinary-odd-alice": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.AaAlice, false, false); destination = 0x301; break;
            case "disp25-ntsc-lace-exact-end-lisa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresLaceKey, (byte)GraphicsChipRevision.AaLisa, false, false); destination = 0xFFFF_FFE7; break;
            case "disp25-null-ntsc-ehb-lace-aa-pair":
                expectedMode = GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteLaceKey;
                Publish(expectedMode, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true);
                handle = 0; modeId = expectedMode; break;
            case "disp25-private-default-aa-pair": Publish(GraphicsModeIds.DefaultMonitor, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); handle = GraphicsDisplayDatabase.DefaultModeHandle; break;
            case "disp25-pal-dpf-last-aligned-setaa": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresDualPlayfieldKey, (byte)GraphicsChipRevision.SetAa, true, true); graphicsBase = lastAlignedCapabilityBase; break;
            case "disp25-ntsc-dpf2-first-even-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresDualPlayfieldTwoKey, 0, false, false); graphicsBase = 2; break;
            case "disp25-default-ham-setaa": Publish(GraphicsModeIds.DefaultMonitor | GraphicsModeIds.HamKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp25-ntsc-ehb-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteKey, 0, false, false); graphicsBase = 2; break;
            case "disp25-pal-superhires-ocs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, 0, false, false); graphicsBase = 2; break;
            case "disp25-ntsc-superhires-ecs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp25-pal-superhires-dpf2-lace-aa-pair": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresDualPlayfieldTwoLaceKey, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); break;
            case "disp25-foreign-provider": handle = foreignMode; modeId = ModeId; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "disp25-null-a6-control": graphicsBase = 0; break;
            case "disp25-odd-a6-control": graphicsBase = 3; break;
            case "disp25-exact-end-a6-control": graphicsBase = maximumCapabilityBase; break;
            case "disp25-first-wrap-a6-control": graphicsBase = firstWrapCapabilityBase; break;
            case "disp25-first-wrap-control": destination = 0xFFFF_FFE8; graphicsBase = uint.MaxValue; break;
            case "disp25-superhires-first-wrap-control": handle = GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey; expectedMode = handle; destination = 0xFFFF_FFE8; break;
            case "disp25-foreign-first-wrap-control": handle = foreignMode; modeId = ModeId; destination = 0xFFFF_FFE8; graphicsBase = uint.MaxValue; malformedForeignSpan = true; break;
            case "disp23-ordinary-valid-control": requestedBytes = 23; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "disp24-superhires-valid-control": requestedBytes = 24; Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp55-control": requestedBytes = 55; expectedResult = requestedBytes; graphicsBase = uint.MaxValue; break;
            case "disp56-control": requestedBytes = 56; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "name25-control": tag = GraphicsDisplayDatabase.DtagName; publishes = true; graphicsBase = uint.MaxValue; break;
            case "dims25-control": tag = GraphicsDisplayDatabase.DtagDims; graphicsBase = uint.MaxValue; break;
            case "mntr25-control": tag = GraphicsDisplayDatabase.DtagMntr; graphicsBase = uint.MaxValue; break;
            case "unsupported25-control": tag = UnsupportedTag; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            case "vec25-provider-control": tag = GraphicsDisplayDatabase.DtagVec; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "sentinel25-control": handle = 0; modeId = GraphicsModeIds.Invalid; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "disp25-ntsc-lace-exact-end-lisa")
            Assert.Equal((ulong)uint.MaxValue, (ulong)destination + requestedBytes - 1);
        if (scenario is "disp25-first-wrap-control" or "disp25-superhires-first-wrap-control" or "disp25-foreign-first-wrap-control")
            Assert.Equal((ulong)uint.MaxValue + 1, (ulong)destination + requestedBytes - 1);
        if (scenario == "disp25-default-ham-setaa")
            Assert.NotEqual(0u, expectedMode);
        if (chipRevision.HasValue)
            fixture.SeedChipRevision(graphicsBase, chipRevision.Value);

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(tag, handle, modeId, requestedBytes, supportsEcsOracle, supportsAgaOracle);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);
            Assert.Equal(BuildQueryHeader(tag, expectedMode), expectedBytes.Take(16));
            if (tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes == 25)
            {
                Assert.Equal((byte)0, expectedBytes[24]);
                Assert.Equal(ExpectedDisplayByte17(expectedMode), expectedBytes[23]);
                Assert.Equal((byte)0, expectedBytes[22]);
                Assert.Equal(ExpectedDisplayByte15(expectedMode), expectedBytes[21]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 ? (byte)0x01 :
                    (expectedMode & GraphicsModeIds.ExtraHalfBriteMode) != 0 ? (byte)0x31 : (byte)0x21, expectedBytes[20]);
                Assert.Equal(supportsAgaOracle ? (byte)0x11 : (byte)0x10, expectedBytes[19]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 && !supportsEcsOracle ? (byte)1 : (byte)0, expectedBytes[17]);
            }
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes ? fixture.ReadOutputByte(followingAddress) : (byte)0;
        var result = fixture.Invoke(handle, destination, requestedBytes, tag, modeId, graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        var expectedCapabilityReads = logicalCapabilityReads;
        Check(failures, "result and terminal provenance", () =>
        {
            Assert.Equal(expectedResult, result.Data0); Assert.False(result.UsedFallback); Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned) Assert.Equal(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
            else if (publishes) Assert.Equal(AddQuickFourLongA7Opcode, result.NativeReturnPredecessorOpcode);
            else if (ownedZero) Assert.Equal(MoveQuickZeroD0Opcode, result.NativeReturnPredecessorOpcode);
            else if (malformedForeignSpan) Assert.NotEqual(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
        });
        Check(failures, "capability/default-monitor reads", () => { Assert.Equal(expectedCapabilityReads, result.CapabilityReadCount); Assert.Equal(0, result.DefaultMonitorReadCount); });
        Check(failures, "caller ABI", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter); Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1); Assert.Equal(destination, result.Address1); Assert.Equal(graphicsBase, result.Address6);
            if (publishes && tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes is 24 or 25)
                Assert.Equal((ushort)0, (ushort)(result.StatusRegister & M68kCpuState.Extend));
            Assert.Empty(result.RegisterDifferences);
        });
        if (publishes)
        {
            Check(failures, "exact portable prefix", () => fixture.AssertOnlyOutputPrefixChanged(before, after, destination, expectedBytes));
            Check(failures, "following byte untouched", () => Assert.Equal(followingByte, fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "memory intact", () => AssertMemoryEqual(before, after));
            Check(failures, "no destination access", () => AssertNoDestinationAccess(result, destination, requestedBytes));
        }
        Assert.True(failures.Count == 0, $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(DisplayTwentySixByteCases))]
    public void GetDisplayInfoDataPublishesTheTwentySixByteDisplayPrefix(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        AssertDisplayTwentySixBytePortableSeam();

        using var fixture = new Fixture(relocated, autoInitEntry);
        var maximumCapabilityBase = uint.MaxValue - (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        var lastAlignedCapabilityBase = maximumCapabilityBase & ~1u;
        var firstWrapCapabilityBase = maximumCapabilityBase + 1u;
        var handle = ModeId;
        var destination = 0x300u;
        var requestedBytes = 26u;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = foreignMode;
        var graphicsBase = fixture.GraphicsBase;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var publishes = false;
        var providerOwned = false;
        var ownedZero = false;
        var malformedForeignSpan = false;
        var supportsEcsOracle = true;
        var supportsAgaOracle = false;
        byte? chipRevision = null;
        var logicalCapabilityReads = 0;

        void Publish(uint mode, byte chip, bool ecs, bool aga)
        {
            publishes = true;
            handle = mode;
            expectedMode = mode;
            chipRevision = chip;
            supportsEcsOracle = ecs;
            supportsAgaOracle = aga;
            logicalCapabilityReads = 1;
        }

        switch (scenario)
        {
            case "disp26-pal-ordinary-even-ecs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp26-ntsc-ordinary-odd-alice": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.AaAlice, false, false); destination = 0x301; break;
            case "disp26-ntsc-lace-exact-end-lisa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresLaceKey, (byte)GraphicsChipRevision.AaLisa, false, false); destination = 0xFFFF_FFE6; break;
            case "disp26-null-ntsc-ehb-lace-aa-pair":
                expectedMode = GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteLaceKey;
                Publish(expectedMode, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true);
                handle = 0; modeId = expectedMode; break;
            case "disp26-private-default-aa-pair": Publish(GraphicsModeIds.DefaultMonitor, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); handle = GraphicsDisplayDatabase.DefaultModeHandle; break;
            case "disp26-pal-dpf-last-aligned-setaa": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresDualPlayfieldKey, (byte)GraphicsChipRevision.SetAa, true, true); graphicsBase = lastAlignedCapabilityBase; break;
            case "disp26-ntsc-dpf2-first-even-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresDualPlayfieldTwoKey, 0, false, false); graphicsBase = 2; break;
            case "disp26-default-ham-setaa": Publish(GraphicsModeIds.DefaultMonitor | GraphicsModeIds.HamKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp26-ntsc-ehb-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteKey, 0, false, false); graphicsBase = 2; break;
            case "disp26-pal-superhires-ocs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, 0, false, false); graphicsBase = 2; break;
            case "disp26-ntsc-superhires-ecs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp26-pal-superhires-dpf2-lace-aa-pair": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresDualPlayfieldTwoLaceKey, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); break;
            case "disp26-foreign-provider": handle = foreignMode; modeId = ModeId; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "disp26-null-a6-control": graphicsBase = 0; break;
            case "disp26-odd-a6-control": graphicsBase = 3; break;
            case "disp26-exact-end-a6-control": graphicsBase = maximumCapabilityBase; break;
            case "disp26-first-wrap-a6-control": graphicsBase = firstWrapCapabilityBase; break;
            case "disp26-first-wrap-control": destination = 0xFFFF_FFE7; graphicsBase = uint.MaxValue; break;
            case "disp26-superhires-first-wrap-control": handle = GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey; expectedMode = handle; destination = 0xFFFF_FFE7; break;
            case "disp26-foreign-first-wrap-control": handle = foreignMode; modeId = ModeId; destination = 0xFFFF_FFE7; graphicsBase = uint.MaxValue; malformedForeignSpan = true; break;
            case "disp24-ordinary-valid-control": requestedBytes = 24; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "disp25-superhires-valid-control": requestedBytes = 25; Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp55-control": requestedBytes = 55; expectedResult = requestedBytes; graphicsBase = uint.MaxValue; break;
            case "disp56-control": requestedBytes = 56; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "name26-control": tag = GraphicsDisplayDatabase.DtagName; publishes = true; graphicsBase = uint.MaxValue; break;
            case "dims26-control": tag = GraphicsDisplayDatabase.DtagDims; graphicsBase = uint.MaxValue; break;
            case "mntr26-control": tag = GraphicsDisplayDatabase.DtagMntr; graphicsBase = uint.MaxValue; break;
            case "unsupported26-control": tag = UnsupportedTag; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            case "vec26-provider-control": tag = GraphicsDisplayDatabase.DtagVec; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "sentinel26-control": handle = 0; modeId = GraphicsModeIds.Invalid; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "disp26-ntsc-lace-exact-end-lisa")
            Assert.Equal((ulong)uint.MaxValue, (ulong)destination + requestedBytes - 1);
        if (scenario is "disp26-first-wrap-control" or "disp26-superhires-first-wrap-control" or "disp26-foreign-first-wrap-control")
            Assert.Equal((ulong)uint.MaxValue + 1, (ulong)destination + requestedBytes - 1);
        if (scenario == "disp26-default-ham-setaa")
            Assert.NotEqual(0u, expectedMode);
        if (chipRevision.HasValue)
            fixture.SeedChipRevision(graphicsBase, chipRevision.Value);

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(tag, handle, modeId, requestedBytes, supportsEcsOracle, supportsAgaOracle);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);
            Assert.Equal(BuildQueryHeader(tag, expectedMode), expectedBytes.Take(16));
            if (tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes == 26)
            {
                Assert.Equal((byte)1, expectedBytes[25]);
                Assert.Equal((byte)0, expectedBytes[24]);
                Assert.Equal(ExpectedDisplayByte17(expectedMode), expectedBytes[23]);
                Assert.Equal((byte)0, expectedBytes[22]);
                Assert.Equal(ExpectedDisplayByte15(expectedMode), expectedBytes[21]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 ? (byte)0x01 :
                    (expectedMode & GraphicsModeIds.ExtraHalfBriteMode) != 0 ? (byte)0x31 : (byte)0x21, expectedBytes[20]);
                Assert.Equal(supportsAgaOracle ? (byte)0x11 : (byte)0x10, expectedBytes[19]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 && !supportsEcsOracle ? (byte)1 : (byte)0, expectedBytes[17]);
            }
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes ? fixture.ReadOutputByte(followingAddress) : (byte)0;
        var result = fixture.Invoke(handle, destination, requestedBytes, tag, modeId, graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        var expectedCapabilityReads = logicalCapabilityReads;
        Check(failures, "result and terminal provenance", () =>
        {
            Assert.Equal(expectedResult, result.Data0); Assert.False(result.UsedFallback); Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned) Assert.Equal(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
            else if (publishes) Assert.Equal(AddQuickFourLongA7Opcode, result.NativeReturnPredecessorOpcode);
            else if (ownedZero) Assert.Equal(MoveQuickZeroD0Opcode, result.NativeReturnPredecessorOpcode);
            else if (malformedForeignSpan) Assert.NotEqual(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
        });
        Check(failures, "capability/default-monitor reads", () => { Assert.Equal(expectedCapabilityReads, result.CapabilityReadCount); Assert.Equal(0, result.DefaultMonitorReadCount); });
        Check(failures, "caller ABI", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter); Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1); Assert.Equal(destination, result.Address1); Assert.Equal(graphicsBase, result.Address6);
            if (publishes && tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes is 25 or 26)
                Assert.Equal((ushort)0, (ushort)(result.StatusRegister & M68kCpuState.Extend));
            Assert.Empty(result.RegisterDifferences);
        });
        if (publishes)
        {
            Check(failures, "exact portable prefix", () => fixture.AssertOnlyOutputPrefixChanged(before, after, destination, expectedBytes));
            Check(failures, "following byte untouched", () => Assert.Equal(followingByte, fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "memory intact", () => AssertMemoryEqual(before, after));
            Check(failures, "no destination access", () => AssertNoDestinationAccess(result, destination, requestedBytes));
        }
        Assert.True(failures.Count == 0, $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(DisplayTwentySevenByteCases))]
    public void GetDisplayInfoDataPublishesTheTwentySevenByteDisplayPrefix(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        AssertDisplayTwentySevenBytePortableSeam();

        using var fixture = new Fixture(relocated, autoInitEntry);
        var maximumCapabilityBase = uint.MaxValue - (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        var lastAlignedCapabilityBase = maximumCapabilityBase & ~1u;
        var firstWrapCapabilityBase = maximumCapabilityBase + 1u;
        var handle = ModeId;
        var destination = 0x300u;
        var requestedBytes = 27u;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = foreignMode;
        var graphicsBase = fixture.GraphicsBase;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var publishes = false;
        var providerOwned = false;
        var ownedZero = false;
        var malformedForeignSpan = false;
        var supportsEcsOracle = true;
        var supportsAgaOracle = false;
        byte? chipRevision = null;
        var logicalCapabilityReads = 0;

        void Publish(uint mode, byte chip, bool ecs, bool aga)
        {
            publishes = true;
            handle = mode;
            expectedMode = mode;
            chipRevision = chip;
            supportsEcsOracle = ecs;
            supportsAgaOracle = aga;
            logicalCapabilityReads = 1;
        }

        switch (scenario)
        {
            case "disp27-pal-ordinary-even-ecs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp27-ntsc-ordinary-odd-alice": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.AaAlice, false, false); destination = 0x301; break;
            case "disp27-ntsc-lace-exact-end-lisa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresLaceKey, (byte)GraphicsChipRevision.AaLisa, false, false); destination = 0xFFFF_FFE5; break;
            case "disp27-null-ntsc-ehb-lace-aa-pair":
                expectedMode = GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteLaceKey;
                Publish(expectedMode, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true);
                handle = 0; modeId = expectedMode; break;
            case "disp27-private-default-aa-pair": Publish(GraphicsModeIds.DefaultMonitor, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); handle = GraphicsDisplayDatabase.DefaultModeHandle; break;
            case "disp27-pal-dpf-last-aligned-setaa": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresDualPlayfieldKey, (byte)GraphicsChipRevision.SetAa, true, true); graphicsBase = lastAlignedCapabilityBase; break;
            case "disp27-ntsc-dpf2-first-even-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresDualPlayfieldTwoKey, 0, false, false); graphicsBase = 2; break;
            case "disp27-default-ham-setaa": Publish(GraphicsModeIds.DefaultMonitor | GraphicsModeIds.HamKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp27-ntsc-ehb-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteKey, 0, false, false); graphicsBase = 2; break;
            case "disp27-pal-superhires-ocs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, 0, false, false); graphicsBase = 2; break;
            case "disp27-ntsc-superhires-ecs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp27-pal-superhires-dpf2-lace-aa-pair": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresDualPlayfieldTwoLaceKey, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); break;
            case "disp27-foreign-provider": handle = foreignMode; modeId = ModeId; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "disp27-null-a6-control": graphicsBase = 0; break;
            case "disp27-odd-a6-control": graphicsBase = 3; break;
            case "disp27-exact-end-a6-control": graphicsBase = maximumCapabilityBase; break;
            case "disp27-first-wrap-a6-control": graphicsBase = firstWrapCapabilityBase; break;
            case "disp27-first-wrap-control": destination = 0xFFFF_FFE6; graphicsBase = uint.MaxValue; break;
            case "disp27-superhires-first-wrap-control": handle = GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey; expectedMode = handle; destination = 0xFFFF_FFE6; break;
            case "disp27-foreign-first-wrap-control": handle = foreignMode; modeId = ModeId; destination = 0xFFFF_FFE6; graphicsBase = uint.MaxValue; malformedForeignSpan = true; break;
            case "disp25-ordinary-valid-control": requestedBytes = 25; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "disp26-superhires-valid-control": requestedBytes = 26; Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp55-control": requestedBytes = 55; expectedResult = requestedBytes; graphicsBase = uint.MaxValue; break;
            case "disp56-control": requestedBytes = 56; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "name27-control": tag = GraphicsDisplayDatabase.DtagName; publishes = true; graphicsBase = uint.MaxValue; break;
            case "dims27-control": tag = GraphicsDisplayDatabase.DtagDims; graphicsBase = uint.MaxValue; break;
            case "mntr27-control": tag = GraphicsDisplayDatabase.DtagMntr; graphicsBase = uint.MaxValue; break;
            case "unsupported27-control": tag = UnsupportedTag; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            case "vec27-provider-control": tag = GraphicsDisplayDatabase.DtagVec; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "sentinel27-control": handle = 0; modeId = GraphicsModeIds.Invalid; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "disp27-ntsc-lace-exact-end-lisa")
            Assert.Equal((ulong)uint.MaxValue, (ulong)destination + requestedBytes - 1);
        if (scenario is "disp27-first-wrap-control" or "disp27-superhires-first-wrap-control" or "disp27-foreign-first-wrap-control")
            Assert.Equal((ulong)uint.MaxValue + 1, (ulong)destination + requestedBytes - 1);
        if (scenario == "disp27-default-ham-setaa")
            Assert.NotEqual(0u, expectedMode);
        if (chipRevision.HasValue)
            fixture.SeedChipRevision(graphicsBase, chipRevision.Value);

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(tag, handle, modeId, requestedBytes, supportsEcsOracle, supportsAgaOracle);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);
            Assert.Equal(BuildQueryHeader(tag, expectedMode), expectedBytes.Take(16));
            if (tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes == 27)
            {
                Assert.Equal((byte)0, expectedBytes[26]);
                Assert.Equal((byte)1, expectedBytes[25]);
                Assert.Equal((byte)0, expectedBytes[24]);
                Assert.Equal(ExpectedDisplayByte17(expectedMode), expectedBytes[23]);
                Assert.Equal((byte)0, expectedBytes[22]);
                Assert.Equal(ExpectedDisplayByte15(expectedMode), expectedBytes[21]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 ? (byte)0x01 :
                    (expectedMode & GraphicsModeIds.ExtraHalfBriteMode) != 0 ? (byte)0x31 : (byte)0x21, expectedBytes[20]);
                Assert.Equal(supportsAgaOracle ? (byte)0x11 : (byte)0x10, expectedBytes[19]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 && !supportsEcsOracle ? (byte)1 : (byte)0, expectedBytes[17]);
            }
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes ? fixture.ReadOutputByte(followingAddress) : (byte)0;
        var result = fixture.Invoke(handle, destination, requestedBytes, tag, modeId, graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        var expectedCapabilityReads = logicalCapabilityReads;
        Check(failures, "result and terminal provenance", () =>
        {
            Assert.Equal(expectedResult, result.Data0); Assert.False(result.UsedFallback); Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned) Assert.Equal(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
            else if (publishes) Assert.Equal(AddQuickFourLongA7Opcode, result.NativeReturnPredecessorOpcode);
            else if (ownedZero) Assert.Equal(MoveQuickZeroD0Opcode, result.NativeReturnPredecessorOpcode);
            else if (malformedForeignSpan) Assert.NotEqual(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
        });
        Check(failures, "capability/default-monitor reads", () => { Assert.Equal(expectedCapabilityReads, result.CapabilityReadCount); Assert.Equal(0, result.DefaultMonitorReadCount); });
        Check(failures, "caller ABI", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter); Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1); Assert.Equal(destination, result.Address1); Assert.Equal(graphicsBase, result.Address6);
            if (publishes && tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes is 26 or 27)
                Assert.Equal((ushort)0, (ushort)(result.StatusRegister & M68kCpuState.Extend));
            Assert.Empty(result.RegisterDifferences);
        });
        if (publishes)
        {
            Check(failures, "exact portable prefix", () => fixture.AssertOnlyOutputPrefixChanged(before, after, destination, expectedBytes));
            Check(failures, "following byte untouched", () => Assert.Equal(followingByte, fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "memory intact", () => AssertMemoryEqual(before, after));
            Check(failures, "no destination access", () => AssertNoDestinationAccess(result, destination, requestedBytes));
        }
        Assert.True(failures.Count == 0, $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(DisplayTwentyEightByteCases))]
    public void GetDisplayInfoDataPublishesTheTwentyEightByteDisplayPrefix(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        AssertDisplayTwentyEightBytePortableSeam();

        using var fixture = new Fixture(relocated, autoInitEntry);
        var maximumCapabilityBase = uint.MaxValue - (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        var lastAlignedCapabilityBase = maximumCapabilityBase & ~1u;
        var firstWrapCapabilityBase = maximumCapabilityBase + 1u;
        var handle = ModeId;
        var destination = 0x300u;
        var requestedBytes = 28u;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = foreignMode;
        var graphicsBase = fixture.GraphicsBase;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var publishes = false;
        var providerOwned = false;
        var ownedZero = false;
        var malformedForeignSpan = false;
        var supportsEcsOracle = true;
        var supportsAgaOracle = false;
        byte? chipRevision = null;
        var logicalCapabilityReads = 0;

        void Publish(uint mode, byte chip, bool ecs, bool aga)
        {
            publishes = true;
            handle = mode;
            expectedMode = mode;
            chipRevision = chip;
            supportsEcsOracle = ecs;
            supportsAgaOracle = aga;
            logicalCapabilityReads = 1;
        }

        switch (scenario)
        {
            case "disp28-pal-ordinary-even-ecs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp28-ntsc-ordinary-odd-alice": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.AaAlice, false, false); destination = 0x301; break;
            case "disp28-ntsc-lace-exact-end-lisa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresLaceKey, (byte)GraphicsChipRevision.AaLisa, false, false); destination = 0xFFFF_FFE4; break;
            case "disp28-null-ntsc-ehb-lace-aa-pair":
                expectedMode = GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteLaceKey;
                Publish(expectedMode, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true);
                handle = 0; modeId = expectedMode; break;
            case "disp28-private-default-aa-pair": Publish(GraphicsModeIds.DefaultMonitor, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); handle = GraphicsDisplayDatabase.DefaultModeHandle; break;
            case "disp28-pal-dpf-last-aligned-setaa": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresDualPlayfieldKey, (byte)GraphicsChipRevision.SetAa, true, true); graphicsBase = lastAlignedCapabilityBase; break;
            case "disp28-ntsc-dpf2-first-even-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresDualPlayfieldTwoKey, 0, false, false); graphicsBase = 2; break;
            case "disp28-default-ham-setaa": Publish(GraphicsModeIds.DefaultMonitor | GraphicsModeIds.HamKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp28-ntsc-ehb-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteKey, 0, false, false); graphicsBase = 2; break;
            case "disp28-pal-superhires-ocs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, 0, false, false); graphicsBase = 2; break;
            case "disp28-ntsc-superhires-ecs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp28-pal-superhires-dpf2-lace-aa-pair": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresDualPlayfieldTwoLaceKey, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); break;
            case "disp28-foreign-provider": handle = foreignMode; modeId = ModeId; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "disp28-null-a6-control": graphicsBase = 0; break;
            case "disp28-odd-a6-control": graphicsBase = 3; break;
            case "disp28-exact-end-a6-control": graphicsBase = maximumCapabilityBase; break;
            case "disp28-first-wrap-a6-control": graphicsBase = firstWrapCapabilityBase; break;
            case "disp28-first-wrap-control": destination = 0xFFFF_FFE5; graphicsBase = uint.MaxValue; break;
            case "disp28-superhires-first-wrap-control": handle = GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey; expectedMode = handle; destination = 0xFFFF_FFE5; break;
            case "disp28-foreign-first-wrap-control": handle = foreignMode; modeId = ModeId; destination = 0xFFFF_FFE5; graphicsBase = uint.MaxValue; malformedForeignSpan = true; break;
            case "disp26-ordinary-valid-control": requestedBytes = 26; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "disp27-superhires-valid-control": requestedBytes = 27; Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp55-control": requestedBytes = 55; expectedResult = requestedBytes; graphicsBase = uint.MaxValue; break;
            case "disp56-control": requestedBytes = 56; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "name28-control": tag = GraphicsDisplayDatabase.DtagName; publishes = true; graphicsBase = uint.MaxValue; break;
            case "dims28-control": tag = GraphicsDisplayDatabase.DtagDims; graphicsBase = uint.MaxValue; break;
            case "mntr28-control": tag = GraphicsDisplayDatabase.DtagMntr; graphicsBase = uint.MaxValue; break;
            case "unsupported28-control": tag = UnsupportedTag; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            case "vec28-provider-control": tag = GraphicsDisplayDatabase.DtagVec; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "sentinel28-control": handle = 0; modeId = GraphicsModeIds.Invalid; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "disp28-ntsc-lace-exact-end-lisa")
            Assert.Equal((ulong)uint.MaxValue, (ulong)destination + requestedBytes - 1);
        if (scenario is "disp28-first-wrap-control" or "disp28-superhires-first-wrap-control" or "disp28-foreign-first-wrap-control")
            Assert.Equal((ulong)uint.MaxValue + 1, (ulong)destination + requestedBytes - 1);
        if (scenario == "disp28-default-ham-setaa")
            Assert.NotEqual(0u, expectedMode);
        if (chipRevision.HasValue)
            fixture.SeedChipRevision(graphicsBase, chipRevision.Value);

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(tag, handle, modeId, requestedBytes, supportsEcsOracle, supportsAgaOracle);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);
            Assert.Equal(BuildQueryHeader(tag, expectedMode), expectedBytes.Take(16));
            if (tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes == 28)
            {
                Assert.Equal((byte)(ExpectedDisplayPixelSpeed(expectedMode) & 0x00FF), expectedBytes[27]);
                Assert.Equal((byte)0, expectedBytes[26]);
                Assert.Equal((byte)1, expectedBytes[25]);
                Assert.Equal((byte)0, expectedBytes[24]);
                Assert.Equal(ExpectedDisplayByte17(expectedMode), expectedBytes[23]);
                Assert.Equal((byte)0, expectedBytes[22]);
                Assert.Equal(ExpectedDisplayByte15(expectedMode), expectedBytes[21]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 ? (byte)0x01 :
                    (expectedMode & GraphicsModeIds.ExtraHalfBriteMode) != 0 ? (byte)0x31 : (byte)0x21, expectedBytes[20]);
                Assert.Equal(supportsAgaOracle ? (byte)0x11 : (byte)0x10, expectedBytes[19]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 && !supportsEcsOracle ? (byte)1 : (byte)0, expectedBytes[17]);
            }
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes ? fixture.ReadOutputByte(followingAddress) : (byte)0;
        var result = fixture.Invoke(handle, destination, requestedBytes, tag, modeId, graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        var expectedCapabilityReads = logicalCapabilityReads;
        Check(failures, "result and terminal provenance", () =>
        {
            Assert.Equal(expectedResult, result.Data0); Assert.False(result.UsedFallback); Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned) Assert.Equal(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
            else if (publishes) Assert.Equal(AddQuickFourLongA7Opcode, result.NativeReturnPredecessorOpcode);
            else if (ownedZero) Assert.Equal(MoveQuickZeroD0Opcode, result.NativeReturnPredecessorOpcode);
            else if (malformedForeignSpan) Assert.NotEqual(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
        });
        Check(failures, "capability/default-monitor reads", () => { Assert.Equal(expectedCapabilityReads, result.CapabilityReadCount); Assert.Equal(0, result.DefaultMonitorReadCount); });
        Check(failures, "caller ABI", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter); Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1); Assert.Equal(destination, result.Address1); Assert.Equal(graphicsBase, result.Address6);
            if (publishes && tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes is 27 or 28)
                Assert.Equal((ushort)0, (ushort)(result.StatusRegister & M68kCpuState.Extend));
            Assert.Empty(result.RegisterDifferences);
        });
        if (publishes)
        {
            Check(failures, "exact portable prefix", () => fixture.AssertOnlyOutputPrefixChanged(before, after, destination, expectedBytes));
            Check(failures, "following byte untouched", () => Assert.Equal(followingByte, fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "memory intact", () => AssertMemoryEqual(before, after));
            Check(failures, "no destination access", () => AssertNoDestinationAccess(result, destination, requestedBytes));
        }
        Assert.True(failures.Count == 0, $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(DisplayTwentyNineByteCases))]
    public void GetDisplayInfoDataPublishesTheTwentyNineByteDisplayPrefix(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        AssertDisplayTwentyNineBytePortableSeam();

        using var fixture = new Fixture(relocated, autoInitEntry);
        var maximumCapabilityBase = uint.MaxValue - (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        var lastAlignedCapabilityBase = maximumCapabilityBase & ~1u;
        var firstWrapCapabilityBase = maximumCapabilityBase + 1u;
        var handle = ModeId;
        var destination = 0x300u;
        var requestedBytes = 29u;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = foreignMode;
        var graphicsBase = fixture.GraphicsBase;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var publishes = false;
        var providerOwned = false;
        var ownedZero = false;
        var malformedForeignSpan = false;
        var supportsEcsOracle = true;
        var supportsAgaOracle = false;
        byte? chipRevision = null;
        var logicalCapabilityReads = 0;

        void Publish(uint mode, byte chip, bool ecs, bool aga)
        {
            publishes = true;
            handle = mode;
            expectedMode = mode;
            chipRevision = chip;
            supportsEcsOracle = ecs;
            supportsAgaOracle = aga;
            logicalCapabilityReads = 1;
        }

        switch (scenario)
        {
            case "disp29-pal-ordinary-even-ecs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp29-ntsc-ordinary-odd-alice": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.AaAlice, false, false); destination = 0x301; break;
            case "disp29-ntsc-lace-exact-end-lisa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresLaceKey, (byte)GraphicsChipRevision.AaLisa, false, false); destination = 0xFFFF_FFE3; break;
            case "disp29-null-ntsc-ehb-lace-aa-pair":
                expectedMode = GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteLaceKey;
                Publish(expectedMode, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true);
                handle = 0; modeId = expectedMode; break;
            case "disp29-private-default-aa-pair": Publish(GraphicsModeIds.DefaultMonitor, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); handle = GraphicsDisplayDatabase.DefaultModeHandle; break;
            case "disp29-pal-dpf-last-aligned-setaa": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresDualPlayfieldKey, (byte)GraphicsChipRevision.SetAa, true, true); graphicsBase = lastAlignedCapabilityBase; break;
            case "disp29-ntsc-dpf2-first-even-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresDualPlayfieldTwoKey, 0, false, false); graphicsBase = 2; break;
            case "disp29-default-ham-setaa": Publish(GraphicsModeIds.DefaultMonitor | GraphicsModeIds.HamKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp29-ntsc-ehb-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteKey, 0, false, false); graphicsBase = 2; break;
            case "disp29-pal-superhires-ocs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, 0, false, false); graphicsBase = 2; break;
            case "disp29-ntsc-superhires-ecs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp29-pal-superhires-dpf2-lace-aa-pair": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresDualPlayfieldTwoLaceKey, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); break;
            case "disp29-foreign-provider": handle = foreignMode; modeId = ModeId; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "disp29-null-a6-control": graphicsBase = 0; break;
            case "disp29-odd-a6-control": graphicsBase = 3; break;
            case "disp29-exact-end-a6-control": graphicsBase = maximumCapabilityBase; break;
            case "disp29-first-wrap-a6-control": graphicsBase = firstWrapCapabilityBase; break;
            case "disp29-first-wrap-control": destination = 0xFFFF_FFE4; graphicsBase = uint.MaxValue; break;
            case "disp29-superhires-first-wrap-control": handle = GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey; expectedMode = handle; destination = 0xFFFF_FFE4; break;
            case "disp29-foreign-first-wrap-control": handle = foreignMode; modeId = ModeId; destination = 0xFFFF_FFE4; graphicsBase = uint.MaxValue; malformedForeignSpan = true; break;
            case "disp27-ordinary-valid-control": requestedBytes = 27; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "disp28-superhires-valid-control": requestedBytes = 28; Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp55-control": requestedBytes = 55; expectedResult = requestedBytes; graphicsBase = uint.MaxValue; break;
            case "disp56-control": requestedBytes = 56; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "name29-control": tag = GraphicsDisplayDatabase.DtagName; publishes = true; graphicsBase = uint.MaxValue; break;
            case "dims29-control": tag = GraphicsDisplayDatabase.DtagDims; graphicsBase = uint.MaxValue; break;
            case "mntr29-control": tag = GraphicsDisplayDatabase.DtagMntr; graphicsBase = uint.MaxValue; break;
            case "unsupported29-control": tag = UnsupportedTag; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            case "vec29-provider-control": tag = GraphicsDisplayDatabase.DtagVec; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "sentinel29-control": handle = 0; modeId = GraphicsModeIds.Invalid; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "disp29-ntsc-lace-exact-end-lisa")
            Assert.Equal((ulong)uint.MaxValue, (ulong)destination + requestedBytes - 1);
        if (scenario is "disp29-first-wrap-control" or "disp29-superhires-first-wrap-control" or "disp29-foreign-first-wrap-control")
            Assert.Equal((ulong)uint.MaxValue + 1, (ulong)destination + requestedBytes - 1);
        if (scenario == "disp29-default-ham-setaa")
            Assert.NotEqual(0u, expectedMode);
        if (chipRevision.HasValue)
            fixture.SeedChipRevision(graphicsBase, chipRevision.Value);

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(tag, handle, modeId, requestedBytes, supportsEcsOracle, supportsAgaOracle);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);
            Assert.Equal(BuildQueryHeader(tag, expectedMode), expectedBytes.Take(16));
            if (tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes == 29)
            {
                Assert.Equal((byte)0, expectedBytes[28]);
                Assert.Equal((byte)(ExpectedDisplayPixelSpeed(expectedMode) & 0x00FF), expectedBytes[27]);
                Assert.Equal((byte)0, expectedBytes[26]);
                Assert.Equal((byte)1, expectedBytes[25]);
                Assert.Equal((byte)0, expectedBytes[24]);
                Assert.Equal(ExpectedDisplayByte17(expectedMode), expectedBytes[23]);
                Assert.Equal((byte)0, expectedBytes[22]);
                Assert.Equal(ExpectedDisplayByte15(expectedMode), expectedBytes[21]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 ? (byte)0x01 :
                    (expectedMode & GraphicsModeIds.ExtraHalfBriteMode) != 0 ? (byte)0x31 : (byte)0x21, expectedBytes[20]);
                Assert.Equal(supportsAgaOracle ? (byte)0x11 : (byte)0x10, expectedBytes[19]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 && !supportsEcsOracle ? (byte)1 : (byte)0, expectedBytes[17]);
            }
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes ? fixture.ReadOutputByte(followingAddress) : (byte)0;
        var result = fixture.Invoke(handle, destination, requestedBytes, tag, modeId, graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        var expectedCapabilityReads = logicalCapabilityReads;
        Check(failures, "result and terminal provenance", () =>
        {
            Assert.Equal(expectedResult, result.Data0); Assert.False(result.UsedFallback); Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned) Assert.Equal(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
            else if (publishes) Assert.Equal(AddQuickFourLongA7Opcode, result.NativeReturnPredecessorOpcode);
            else if (ownedZero) Assert.Equal(MoveQuickZeroD0Opcode, result.NativeReturnPredecessorOpcode);
            else if (malformedForeignSpan) Assert.NotEqual(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
        });
        Check(failures, "capability/default-monitor reads", () => { Assert.Equal(expectedCapabilityReads, result.CapabilityReadCount); Assert.Equal(0, result.DefaultMonitorReadCount); });
        Check(failures, "caller ABI", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter); Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1); Assert.Equal(destination, result.Address1); Assert.Equal(graphicsBase, result.Address6);
            if (publishes && tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes is 27 or 28 or 29)
                Assert.Equal((ushort)0, (ushort)(result.StatusRegister & M68kCpuState.Extend));
            Assert.Empty(result.RegisterDifferences);
        });
        if (publishes)
        {
            Check(failures, "exact portable prefix", () => fixture.AssertOnlyOutputPrefixChanged(before, after, destination, expectedBytes));
            Check(failures, "following byte untouched", () => Assert.Equal(followingByte, fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "memory intact", () => AssertMemoryEqual(before, after));
            Check(failures, "no destination access", () => AssertNoDestinationAccess(result, destination, requestedBytes));
        }
        Assert.True(failures.Count == 0, $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(DisplayThirtyByteCases))]
    public void GetDisplayInfoDataPublishesTheThirtyByteDisplayPrefix(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        AssertDisplayThirtyBytePortableSeam();

        using var fixture = new Fixture(relocated, autoInitEntry);
        var maximumCapabilityBase = uint.MaxValue - (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        var lastAlignedCapabilityBase = maximumCapabilityBase & ~1u;
        var firstWrapCapabilityBase = maximumCapabilityBase + 1u;
        var handle = ModeId;
        var destination = 0x300u;
        var requestedBytes = 30u;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = foreignMode;
        var graphicsBase = fixture.GraphicsBase;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var publishes = false;
        var providerOwned = false;
        var ownedZero = false;
        var malformedForeignSpan = false;
        var supportsEcsOracle = true;
        var supportsAgaOracle = false;
        byte? chipRevision = null;
        var logicalCapabilityReads = 0;

        void Publish(uint mode, byte chip, bool ecs, bool aga)
        {
            publishes = true;
            handle = mode;
            expectedMode = mode;
            chipRevision = chip;
            supportsEcsOracle = ecs;
            supportsAgaOracle = aga;
            logicalCapabilityReads = 1;
        }

        switch (scenario)
        {
            case "disp30-pal-ordinary-even-ecs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp30-ntsc-ordinary-odd-alice": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.AaAlice, false, false); destination = 0x301; break;
            case "disp30-ntsc-lace-exact-end-lisa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresLaceKey, (byte)GraphicsChipRevision.AaLisa, false, false); destination = 0xFFFF_FFE2; break;
            case "disp30-null-ntsc-ehb-lace-aa-pair":
                expectedMode = GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteLaceKey;
                Publish(expectedMode, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true);
                handle = 0; modeId = expectedMode; break;
            case "disp30-private-default-aa-pair": Publish(GraphicsModeIds.DefaultMonitor, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); handle = GraphicsDisplayDatabase.DefaultModeHandle; break;
            case "disp30-pal-dpf-last-aligned-setaa": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresDualPlayfieldKey, (byte)GraphicsChipRevision.SetAa, true, true); graphicsBase = lastAlignedCapabilityBase; break;
            case "disp30-ntsc-dpf2-first-even-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresDualPlayfieldTwoKey, 0, false, false); graphicsBase = 2; break;
            case "disp30-default-ham-setaa": Publish(GraphicsModeIds.DefaultMonitor | GraphicsModeIds.HamKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp30-ntsc-ehb-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteKey, 0, false, false); graphicsBase = 2; break;
            case "disp30-pal-superhires-ocs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, 0, false, false); graphicsBase = 2; break;
            case "disp30-ntsc-superhires-ecs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp30-pal-superhires-dpf2-lace-aa-pair": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresDualPlayfieldTwoLaceKey, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); break;
            case "disp30-foreign-provider": handle = foreignMode; modeId = ModeId; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "disp30-null-a6-control": graphicsBase = 0; break;
            case "disp30-odd-a6-control": graphicsBase = 3; break;
            case "disp30-exact-end-a6-control": graphicsBase = maximumCapabilityBase; break;
            case "disp30-first-wrap-a6-control": graphicsBase = firstWrapCapabilityBase; break;
            case "disp30-first-wrap-control": destination = 0xFFFF_FFE3; graphicsBase = uint.MaxValue; break;
            case "disp30-superhires-first-wrap-control": handle = GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey; expectedMode = handle; destination = 0xFFFF_FFE3; break;
            case "disp30-foreign-first-wrap-control": handle = foreignMode; modeId = ModeId; destination = 0xFFFF_FFE3; graphicsBase = uint.MaxValue; malformedForeignSpan = true; break;
            case "disp28-ordinary-valid-control": requestedBytes = 28; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "disp29-superhires-valid-control": requestedBytes = 29; Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp55-control": requestedBytes = 55; expectedResult = requestedBytes; graphicsBase = uint.MaxValue; break;
            case "disp56-control": requestedBytes = 56; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "name30-control": tag = GraphicsDisplayDatabase.DtagName; publishes = true; graphicsBase = uint.MaxValue; break;
            case "dims30-control": tag = GraphicsDisplayDatabase.DtagDims; graphicsBase = uint.MaxValue; break;
            case "mntr30-control": tag = GraphicsDisplayDatabase.DtagMntr; graphicsBase = uint.MaxValue; break;
            case "unsupported30-control": tag = UnsupportedTag; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            case "vec30-provider-control": tag = GraphicsDisplayDatabase.DtagVec; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "sentinel30-control": handle = 0; modeId = GraphicsModeIds.Invalid; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "disp30-ntsc-lace-exact-end-lisa")
            Assert.Equal((ulong)uint.MaxValue, (ulong)destination + requestedBytes - 1);
        if (scenario is "disp30-first-wrap-control" or "disp30-superhires-first-wrap-control" or "disp30-foreign-first-wrap-control")
            Assert.Equal((ulong)uint.MaxValue + 1, (ulong)destination + requestedBytes - 1);
        if (scenario == "disp30-default-ham-setaa")
            Assert.NotEqual(0u, expectedMode);
        if (chipRevision.HasValue)
            fixture.SeedChipRevision(graphicsBase, chipRevision.Value);

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(tag, handle, modeId, requestedBytes, supportsEcsOracle, supportsAgaOracle);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);
            Assert.Equal(BuildQueryHeader(tag, expectedMode), expectedBytes.Take(16));
            if (tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes == 30)
            {
                Assert.Equal((byte)8, expectedBytes[29]);
                Assert.Equal((byte)0, expectedBytes[28]);
                Assert.Equal((byte)(ExpectedDisplayPixelSpeed(expectedMode) & 0x00FF), expectedBytes[27]);
                Assert.Equal((byte)0, expectedBytes[26]);
                Assert.Equal((byte)1, expectedBytes[25]);
                Assert.Equal((byte)0, expectedBytes[24]);
                Assert.Equal(ExpectedDisplayByte17(expectedMode), expectedBytes[23]);
                Assert.Equal((byte)0, expectedBytes[22]);
                Assert.Equal(ExpectedDisplayByte15(expectedMode), expectedBytes[21]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 ? (byte)0x01 :
                    (expectedMode & GraphicsModeIds.ExtraHalfBriteMode) != 0 ? (byte)0x31 : (byte)0x21, expectedBytes[20]);
                Assert.Equal(supportsAgaOracle ? (byte)0x11 : (byte)0x10, expectedBytes[19]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 && !supportsEcsOracle ? (byte)1 : (byte)0, expectedBytes[17]);
            }
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes ? fixture.ReadOutputByte(followingAddress) : (byte)0;
        var result = fixture.Invoke(handle, destination, requestedBytes, tag, modeId, graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        var expectedCapabilityReads = logicalCapabilityReads;
        Check(failures, "result and terminal provenance", () =>
        {
            Assert.Equal(expectedResult, result.Data0); Assert.False(result.UsedFallback); Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned) Assert.Equal(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
            else if (publishes) Assert.Equal(AddQuickFourLongA7Opcode, result.NativeReturnPredecessorOpcode);
            else if (ownedZero) Assert.Equal(MoveQuickZeroD0Opcode, result.NativeReturnPredecessorOpcode);
            else if (malformedForeignSpan) Assert.NotEqual(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
        });
        Check(failures, "capability/default-monitor reads", () => { Assert.Equal(expectedCapabilityReads, result.CapabilityReadCount); Assert.Equal(0, result.DefaultMonitorReadCount); });
        Check(failures, "caller ABI", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter); Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1); Assert.Equal(destination, result.Address1); Assert.Equal(graphicsBase, result.Address6);
            if (publishes && tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes is 27 or 28 or 29 or 30)
                Assert.Equal((ushort)0, (ushort)(result.StatusRegister & M68kCpuState.Extend));
            Assert.Empty(result.RegisterDifferences);
        });
        if (publishes)
        {
            Check(failures, "exact portable prefix", () => fixture.AssertOnlyOutputPrefixChanged(before, after, destination, expectedBytes));
            Check(failures, "following byte untouched", () => Assert.Equal(followingByte, fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "memory intact", () => AssertMemoryEqual(before, after));
            Check(failures, "no destination access", () => AssertNoDestinationAccess(result, destination, requestedBytes));
        }
        Assert.True(failures.Count == 0, $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(DisplayThirtyOneByteCases))]
    public void GetDisplayInfoDataPublishesTheThirtyOneByteDisplayPrefix(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        AssertDisplayThirtyOneBytePortableSeam();

        using var fixture = new Fixture(relocated, autoInitEntry);
        var maximumCapabilityBase = uint.MaxValue - (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        var lastAlignedCapabilityBase = maximumCapabilityBase & ~1u;
        var firstWrapCapabilityBase = maximumCapabilityBase + 1u;
        var handle = ModeId;
        var destination = 0x300u;
        var requestedBytes = 31u;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = foreignMode;
        var graphicsBase = fixture.GraphicsBase;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var publishes = false;
        var providerOwned = false;
        var ownedZero = false;
        var malformedForeignSpan = false;
        var supportsEcsOracle = true;
        var supportsAgaOracle = false;
        byte? chipRevision = null;
        var logicalCapabilityReads = 0;

        void Publish(uint mode, byte chip, bool ecs, bool aga)
        {
            publishes = true;
            handle = mode;
            expectedMode = mode;
            chipRevision = chip;
            supportsEcsOracle = ecs;
            supportsAgaOracle = aga;
            logicalCapabilityReads = 1;
        }

        switch (scenario)
        {
            case "disp31-pal-ordinary-even-ecs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp31-ntsc-ordinary-odd-alice": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.AaAlice, false, false); destination = 0x301; break;
            case "disp31-ntsc-superhires-lace-exact-end-lisa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresLaceKey, (byte)GraphicsChipRevision.AaLisa, false, false); destination = 0xFFFF_FFE1; break;
            case "disp31-null-ntsc-ehb-lace-aa-pair":
                expectedMode = GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteLaceKey;
                Publish(expectedMode, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true);
                handle = 0; modeId = expectedMode; break;
            case "disp31-private-default-aa-pair": Publish(GraphicsModeIds.DefaultMonitor, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); handle = GraphicsDisplayDatabase.DefaultModeHandle; break;
            case "disp31-pal-dpf-last-aligned-setaa": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresDualPlayfieldKey, (byte)GraphicsChipRevision.SetAa, true, true); graphicsBase = lastAlignedCapabilityBase; break;
            case "disp31-ntsc-dpf2-first-even-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresDualPlayfieldTwoKey, 0, false, false); graphicsBase = 2; break;
            case "disp31-default-ham-setaa": Publish(GraphicsModeIds.DefaultMonitor | GraphicsModeIds.HamKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp31-ntsc-superhires-setaa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp31-pal-superhires-ocs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, 0, false, false); graphicsBase = 2; break;
            case "disp31-ntsc-superhires-ecs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp31-pal-superhires-dpf2-lace-aa-pair": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresDualPlayfieldTwoLaceKey, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); break;
            case "disp31-foreign-provider": handle = foreignMode; modeId = ModeId; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "disp31-null-a6-control": graphicsBase = 0; break;
            case "disp31-odd-a6-control": graphicsBase = 3; break;
            case "disp31-exact-end-a6-control": graphicsBase = maximumCapabilityBase; break;
            case "disp31-first-wrap-a6-control": graphicsBase = firstWrapCapabilityBase; break;
            case "disp31-first-wrap-control": destination = 0xFFFF_FFE2; graphicsBase = uint.MaxValue; break;
            case "disp31-superhires-first-wrap-control": handle = GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey; expectedMode = handle; destination = 0xFFFF_FFE2; break;
            case "disp31-foreign-first-wrap-control": handle = foreignMode; modeId = ModeId; destination = 0xFFFF_FFE2; graphicsBase = uint.MaxValue; malformedForeignSpan = true; break;
            case "disp29-ordinary-valid-control": requestedBytes = 29; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "disp30-superhires-valid-control": requestedBytes = 30; Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp55-control": requestedBytes = 55; expectedResult = requestedBytes; graphicsBase = uint.MaxValue; break;
            case "disp56-control": requestedBytes = 56; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "name31-control": tag = GraphicsDisplayDatabase.DtagName; publishes = true; graphicsBase = uint.MaxValue; break;
            case "dims31-control": tag = GraphicsDisplayDatabase.DtagDims; graphicsBase = uint.MaxValue; break;
            case "mntr31-control": tag = GraphicsDisplayDatabase.DtagMntr; graphicsBase = uint.MaxValue; break;
            case "unsupported31-control": tag = UnsupportedTag; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            case "vec31-provider-control": tag = GraphicsDisplayDatabase.DtagVec; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "sentinel31-control": handle = 0; modeId = GraphicsModeIds.Invalid; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "disp31-ntsc-superhires-lace-exact-end-lisa")
            Assert.Equal((ulong)uint.MaxValue, (ulong)destination + requestedBytes - 1);
        if (scenario is "disp31-first-wrap-control" or "disp31-superhires-first-wrap-control" or "disp31-foreign-first-wrap-control")
            Assert.Equal((ulong)uint.MaxValue + 1, (ulong)destination + requestedBytes - 1);
        if (scenario == "disp31-default-ham-setaa")
            Assert.NotEqual(0u, expectedMode);
        if (chipRevision.HasValue)
            fixture.SeedChipRevision(graphicsBase, chipRevision.Value);

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(tag, handle, modeId, requestedBytes, supportsEcsOracle, supportsAgaOracle);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);
            Assert.Equal(BuildQueryHeader(tag, expectedMode), expectedBytes.Take(16));
            if (tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes == 31)
            {
                var expectedPaletteRange = ExpectedDisplayPaletteRange(expectedMode, supportsAgaOracle);
                Assert.Equal((byte)(expectedPaletteRange >> 8), expectedBytes[30]);
                Assert.Equal((byte)8, expectedBytes[29]);
                Assert.Equal((byte)0, expectedBytes[28]);
                Assert.Equal((byte)(ExpectedDisplayPixelSpeed(expectedMode) & 0x00FF), expectedBytes[27]);
                Assert.Equal((byte)0, expectedBytes[26]);
                Assert.Equal((byte)1, expectedBytes[25]);
                Assert.Equal((byte)0, expectedBytes[24]);
                Assert.Equal(ExpectedDisplayByte17(expectedMode), expectedBytes[23]);
                Assert.Equal((byte)0, expectedBytes[22]);
                Assert.Equal(ExpectedDisplayByte15(expectedMode), expectedBytes[21]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 ? (byte)0x01 :
                    (expectedMode & GraphicsModeIds.ExtraHalfBriteMode) != 0 ? (byte)0x31 : (byte)0x21, expectedBytes[20]);
                Assert.Equal(supportsAgaOracle ? (byte)0x11 : (byte)0x10, expectedBytes[19]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 && !supportsEcsOracle ? (byte)1 : (byte)0, expectedBytes[17]);
            }
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes ? fixture.ReadOutputByte(followingAddress) : (byte)0;
        var result = fixture.Invoke(handle, destination, requestedBytes, tag, modeId, graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        var expectedCapabilityReads = logicalCapabilityReads;
        Check(failures, "result and terminal provenance", () =>
        {
            Assert.Equal(expectedResult, result.Data0); Assert.False(result.UsedFallback); Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned) Assert.Equal(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
            else if (publishes) Assert.Equal(AddQuickFourLongA7Opcode, result.NativeReturnPredecessorOpcode);
            else if (ownedZero) Assert.Equal(MoveQuickZeroD0Opcode, result.NativeReturnPredecessorOpcode);
            else if (malformedForeignSpan) Assert.NotEqual(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
        });
        Check(failures, "capability/default-monitor reads", () => { Assert.Equal(expectedCapabilityReads, result.CapabilityReadCount); Assert.Equal(0, result.DefaultMonitorReadCount); });
        Check(failures, "caller ABI", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter); Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1); Assert.Equal(destination, result.Address1); Assert.Equal(graphicsBase, result.Address6);
            if (publishes && tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes is 27 or 28 or 29 or 30 or 31)
                Assert.Equal((ushort)0, (ushort)(result.StatusRegister & M68kCpuState.Extend));
            Assert.Empty(result.RegisterDifferences);
        });
        if (publishes)
        {
            Check(failures, "exact portable prefix", () => fixture.AssertOnlyOutputPrefixChanged(before, after, destination, expectedBytes));
            Check(failures, "following byte untouched", () => Assert.Equal(followingByte, fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "memory intact", () => AssertMemoryEqual(before, after));
            Check(failures, "no destination access", () => AssertNoDestinationAccess(result, destination, requestedBytes));
        }
        Assert.True(failures.Count == 0, $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(DisplayThirtyTwoByteCases))]
    public void GetDisplayInfoDataPublishesTheThirtyTwoByteDisplayPrefix(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        AssertDisplayThirtyTwoBytePortableSeam();

        using var fixture = new Fixture(relocated, autoInitEntry);
        var maximumCapabilityBase = uint.MaxValue - (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        var lastAlignedCapabilityBase = maximumCapabilityBase & ~1u;
        var firstWrapCapabilityBase = maximumCapabilityBase + 1u;
        var handle = ModeId;
        var destination = 0x300u;
        var requestedBytes = 32u;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = foreignMode;
        var graphicsBase = fixture.GraphicsBase;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var publishes = false;
        var providerOwned = false;
        var ownedZero = false;
        var malformedForeignSpan = false;
        var supportsEcsOracle = true;
        var supportsAgaOracle = false;
        byte? chipRevision = null;
        var logicalCapabilityReads = 0;

        void Publish(uint mode, byte chip, bool ecs, bool aga)
        {
            publishes = true;
            handle = mode;
            expectedMode = mode;
            chipRevision = chip;
            supportsEcsOracle = ecs;
            supportsAgaOracle = aga;
            logicalCapabilityReads = 1;
        }

        switch (scenario)
        {
            case "disp32-pal-ordinary-even-ecs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp32-ntsc-ordinary-odd-alice": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.AaAlice, false, false); destination = 0x301; break;
            case "disp32-ntsc-superhires-lace-exact-end-lisa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresLaceKey, (byte)GraphicsChipRevision.AaLisa, false, false); destination = 0xFFFF_FFE0; break;
            case "disp32-null-ntsc-ehb-lace-aa-pair":
                expectedMode = GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteLaceKey;
                Publish(expectedMode, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true);
                handle = 0; modeId = expectedMode; break;
            case "disp32-private-default-aa-pair": Publish(GraphicsModeIds.DefaultMonitor, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); handle = GraphicsDisplayDatabase.DefaultModeHandle; break;
            case "disp32-pal-dpf-last-aligned-setaa": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresDualPlayfieldKey, (byte)GraphicsChipRevision.SetAa, true, true); graphicsBase = lastAlignedCapabilityBase; break;
            case "disp32-ntsc-dpf2-first-even-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresDualPlayfieldTwoKey, 0, false, false); graphicsBase = 2; break;
            case "disp32-default-ham-setaa": Publish(GraphicsModeIds.DefaultMonitor | GraphicsModeIds.HamKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp32-ntsc-superhires-setaa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp32-pal-superhires-ocs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, 0, false, false); graphicsBase = 2; break;
            case "disp32-ntsc-superhires-ecs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp32-pal-superhires-dpf2-lace-aa-pair": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresDualPlayfieldTwoLaceKey, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); break;
            case "disp32-foreign-provider": handle = foreignMode; modeId = ModeId; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "disp32-null-a6-control": graphicsBase = 0; break;
            case "disp32-odd-a6-control": graphicsBase = 3; break;
            case "disp32-exact-end-a6-control": graphicsBase = maximumCapabilityBase; break;
            case "disp32-first-wrap-a6-control": graphicsBase = firstWrapCapabilityBase; break;
            case "disp32-first-wrap-control": destination = 0xFFFF_FFE1; graphicsBase = uint.MaxValue; break;
            case "disp32-superhires-first-wrap-control": handle = GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey; expectedMode = handle; destination = 0xFFFF_FFE1; break;
            case "disp32-foreign-first-wrap-control": handle = foreignMode; modeId = ModeId; destination = 0xFFFF_FFE1; graphicsBase = uint.MaxValue; malformedForeignSpan = true; break;
            case "disp30-ordinary-valid-control": requestedBytes = 30; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "disp31-superhires-valid-control": requestedBytes = 31; Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp55-control": requestedBytes = 55; expectedResult = requestedBytes; graphicsBase = uint.MaxValue; break;
            case "disp56-control": requestedBytes = 56; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "name32-control": tag = GraphicsDisplayDatabase.DtagName; publishes = true; graphicsBase = uint.MaxValue; break;
            case "dims32-control": tag = GraphicsDisplayDatabase.DtagDims; graphicsBase = uint.MaxValue; break;
            case "mntr32-control": tag = GraphicsDisplayDatabase.DtagMntr; graphicsBase = uint.MaxValue; break;
            case "unsupported32-control": tag = UnsupportedTag; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            case "vec32-provider-control": tag = GraphicsDisplayDatabase.DtagVec; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "sentinel32-control": handle = 0; modeId = GraphicsModeIds.Invalid; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "disp32-ntsc-superhires-lace-exact-end-lisa")
            Assert.Equal((ulong)uint.MaxValue, (ulong)destination + requestedBytes - 1);
        if (scenario is "disp32-first-wrap-control" or "disp32-superhires-first-wrap-control" or "disp32-foreign-first-wrap-control")
            Assert.Equal((ulong)uint.MaxValue + 1, (ulong)destination + requestedBytes - 1);
        if (scenario == "disp32-default-ham-setaa")
            Assert.NotEqual(0u, expectedMode);
        if (chipRevision.HasValue)
            fixture.SeedChipRevision(graphicsBase, chipRevision.Value);

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(tag, handle, modeId, requestedBytes, supportsEcsOracle, supportsAgaOracle);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);
            Assert.Equal(BuildQueryHeader(tag, expectedMode), expectedBytes.Take(16));
            if (tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes == 32)
            {
                var expectedPaletteRange = ExpectedDisplayPaletteRange(expectedMode, supportsAgaOracle);
                Assert.Equal((byte)(expectedPaletteRange >> 8), expectedBytes[30]);
                Assert.Equal((byte)(expectedPaletteRange & 0x00FF), expectedBytes[31]);
                Assert.Equal((byte)8, expectedBytes[29]);
                Assert.Equal((byte)0, expectedBytes[28]);
                Assert.Equal((byte)(ExpectedDisplayPixelSpeed(expectedMode) & 0x00FF), expectedBytes[27]);
                Assert.Equal((byte)0, expectedBytes[26]);
                Assert.Equal((byte)1, expectedBytes[25]);
                Assert.Equal((byte)0, expectedBytes[24]);
                Assert.Equal(ExpectedDisplayByte17(expectedMode), expectedBytes[23]);
                Assert.Equal((byte)0, expectedBytes[22]);
                Assert.Equal(ExpectedDisplayByte15(expectedMode), expectedBytes[21]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 ? (byte)0x01 :
                    (expectedMode & GraphicsModeIds.ExtraHalfBriteMode) != 0 ? (byte)0x31 : (byte)0x21, expectedBytes[20]);
                Assert.Equal(supportsAgaOracle ? (byte)0x11 : (byte)0x10, expectedBytes[19]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 && !supportsEcsOracle ? (byte)1 : (byte)0, expectedBytes[17]);
            }
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes ? fixture.ReadOutputByte(followingAddress) : (byte)0;
        var result = fixture.Invoke(handle, destination, requestedBytes, tag, modeId, graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        var expectedCapabilityReads = logicalCapabilityReads;
        Check(failures, "result and terminal provenance", () =>
        {
            Assert.Equal(expectedResult, result.Data0); Assert.False(result.UsedFallback); Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned) Assert.Equal(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
            else if (publishes) Assert.Equal(AddQuickFourLongA7Opcode, result.NativeReturnPredecessorOpcode);
            else if (ownedZero) Assert.Equal(MoveQuickZeroD0Opcode, result.NativeReturnPredecessorOpcode);
            else if (malformedForeignSpan) Assert.NotEqual(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
        });
        Check(failures, "capability/default-monitor reads", () => { Assert.Equal(expectedCapabilityReads, result.CapabilityReadCount); Assert.Equal(0, result.DefaultMonitorReadCount); });
        Check(failures, "caller ABI", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter); Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1); Assert.Equal(destination, result.Address1); Assert.Equal(graphicsBase, result.Address6);
            if (publishes && tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes is 27 or 28 or 29 or 30 or 31 or 32)
                Assert.Equal((ushort)0, (ushort)(result.StatusRegister & M68kCpuState.Extend));
            Assert.Empty(result.RegisterDifferences);
        });
        if (publishes)
        {
            Check(failures, "exact portable prefix", () => fixture.AssertOnlyOutputPrefixChanged(before, after, destination, expectedBytes));
            Check(failures, "following byte untouched", () => Assert.Equal(followingByte, fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "memory intact", () => AssertMemoryEqual(before, after));
            Check(failures, "no destination access", () => AssertNoDestinationAccess(result, destination, requestedBytes));
        }
        Assert.True(failures.Count == 0, $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(DisplayThirtyThreeByteCases))]
    public void GetDisplayInfoDataPublishesTheThirtyThreeByteDisplayPrefix(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        AssertDisplayThirtyThreeBytePortableSeam();

        using var fixture = new Fixture(relocated, autoInitEntry);
        var maximumCapabilityBase = uint.MaxValue - (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        var lastAlignedCapabilityBase = maximumCapabilityBase & ~1u;
        var firstWrapCapabilityBase = maximumCapabilityBase + 1u;
        var handle = ModeId;
        var destination = 0x300u;
        var requestedBytes = 33u;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = foreignMode;
        var graphicsBase = fixture.GraphicsBase;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var publishes = false;
        var providerOwned = false;
        var ownedZero = false;
        var malformedForeignSpan = false;
        var supportsEcsOracle = true;
        var supportsAgaOracle = false;
        byte? chipRevision = null;
        var logicalCapabilityReads = 0;

        void Publish(uint mode, byte chip, bool ecs, bool aga)
        {
            publishes = true;
            handle = mode;
            expectedMode = mode;
            chipRevision = chip;
            supportsEcsOracle = ecs;
            supportsAgaOracle = aga;
            logicalCapabilityReads = 1;
        }

        switch (scenario)
        {
            case "disp33-pal-ordinary-even-ecs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp33-ntsc-ordinary-odd-alice": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.AaAlice, false, false); destination = 0x301; break;
            case "disp33-ntsc-superhires-lace-exact-end-lisa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresLaceKey, (byte)GraphicsChipRevision.AaLisa, false, false); destination = 0xFFFF_FFDF; break;
            case "disp33-null-ntsc-ehb-lace-aa-pair":
                expectedMode = GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteLaceKey;
                Publish(expectedMode, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true);
                handle = 0; modeId = expectedMode; break;
            case "disp33-private-default-aa-pair": Publish(GraphicsModeIds.DefaultMonitor, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); handle = GraphicsDisplayDatabase.DefaultModeHandle; break;
            case "disp33-pal-dpf-last-aligned-setaa": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresDualPlayfieldKey, (byte)GraphicsChipRevision.SetAa, true, true); graphicsBase = lastAlignedCapabilityBase; break;
            case "disp33-ntsc-dpf2-first-even-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresDualPlayfieldTwoKey, 0, false, false); graphicsBase = 2; break;
            case "disp33-default-ham-setaa": Publish(GraphicsModeIds.DefaultMonitor | GraphicsModeIds.HamKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp33-ntsc-superhires-setaa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp33-pal-superhires-ocs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, 0, false, false); graphicsBase = 2; break;
            case "disp33-ntsc-superhires-ecs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp33-pal-superhires-dpf2-lace-aa-pair": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresDualPlayfieldTwoLaceKey, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); break;
            case "disp33-foreign-provider": handle = foreignMode; modeId = ModeId; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "disp33-null-a6-control": graphicsBase = 0; break;
            case "disp33-odd-a6-control": graphicsBase = 3; break;
            case "disp33-exact-end-a6-control": graphicsBase = maximumCapabilityBase; break;
            case "disp33-first-wrap-a6-control": graphicsBase = firstWrapCapabilityBase; break;
            case "disp33-first-wrap-control": destination = 0xFFFF_FFE0; graphicsBase = uint.MaxValue; break;
            case "disp33-superhires-first-wrap-control": handle = GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey; expectedMode = handle; destination = 0xFFFF_FFE0; break;
            case "disp33-foreign-first-wrap-control": handle = foreignMode; modeId = ModeId; destination = 0xFFFF_FFE0; graphicsBase = uint.MaxValue; malformedForeignSpan = true; break;
            case "disp31-ordinary-valid-control": requestedBytes = 31; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "disp32-superhires-valid-control": requestedBytes = 32; Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp55-control": requestedBytes = 55; expectedResult = requestedBytes; graphicsBase = uint.MaxValue; break;
            case "disp56-control": requestedBytes = 56; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "name33-control": tag = GraphicsDisplayDatabase.DtagName; publishes = true; graphicsBase = uint.MaxValue; break;
            case "dims33-control": tag = GraphicsDisplayDatabase.DtagDims; graphicsBase = uint.MaxValue; break;
            case "mntr33-control": tag = GraphicsDisplayDatabase.DtagMntr; graphicsBase = uint.MaxValue; break;
            case "unsupported33-control": tag = UnsupportedTag; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            case "vec33-provider-control": tag = GraphicsDisplayDatabase.DtagVec; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "sentinel33-control": handle = 0; modeId = GraphicsModeIds.Invalid; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "disp33-ntsc-superhires-lace-exact-end-lisa")
            Assert.Equal((ulong)uint.MaxValue, (ulong)destination + requestedBytes - 1);
        if (scenario is "disp33-first-wrap-control" or "disp33-superhires-first-wrap-control" or "disp33-foreign-first-wrap-control")
            Assert.Equal((ulong)uint.MaxValue + 1, (ulong)destination + requestedBytes - 1);
        if (scenario == "disp33-default-ham-setaa")
            Assert.NotEqual(0u, expectedMode);
        if (chipRevision.HasValue)
            fixture.SeedChipRevision(graphicsBase, chipRevision.Value);

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(tag, handle, modeId, requestedBytes, supportsEcsOracle, supportsAgaOracle);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);
            Assert.Equal(BuildQueryHeader(tag, expectedMode), expectedBytes.Take(16));
            if (tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes == 33)
            {
                var expectedPaletteRange = ExpectedDisplayPaletteRange(expectedMode, supportsAgaOracle);
                Assert.Equal((byte)0, expectedBytes[32]);
                Assert.Equal((byte)(expectedPaletteRange >> 8), expectedBytes[30]);
                Assert.Equal((byte)(expectedPaletteRange & 0x00FF), expectedBytes[31]);
                Assert.Equal((byte)8, expectedBytes[29]);
                Assert.Equal((byte)0, expectedBytes[28]);
                Assert.Equal((byte)(ExpectedDisplayPixelSpeed(expectedMode) & 0x00FF), expectedBytes[27]);
                Assert.Equal((byte)0, expectedBytes[26]);
                Assert.Equal((byte)1, expectedBytes[25]);
                Assert.Equal((byte)0, expectedBytes[24]);
                Assert.Equal(ExpectedDisplayByte17(expectedMode), expectedBytes[23]);
                Assert.Equal((byte)0, expectedBytes[22]);
                Assert.Equal(ExpectedDisplayByte15(expectedMode), expectedBytes[21]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 ? (byte)0x01 :
                    (expectedMode & GraphicsModeIds.ExtraHalfBriteMode) != 0 ? (byte)0x31 : (byte)0x21, expectedBytes[20]);
                Assert.Equal(supportsAgaOracle ? (byte)0x11 : (byte)0x10, expectedBytes[19]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 && !supportsEcsOracle ? (byte)1 : (byte)0, expectedBytes[17]);
            }
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes ? fixture.ReadOutputByte(followingAddress) : (byte)0;
        var result = fixture.Invoke(handle, destination, requestedBytes, tag, modeId, graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        var expectedCapabilityReads = logicalCapabilityReads;
        Check(failures, "result and terminal provenance", () =>
        {
            Assert.Equal(expectedResult, result.Data0); Assert.False(result.UsedFallback); Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned) Assert.Equal(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
            else if (publishes) Assert.Equal(AddQuickFourLongA7Opcode, result.NativeReturnPredecessorOpcode);
            else if (ownedZero) Assert.Equal(MoveQuickZeroD0Opcode, result.NativeReturnPredecessorOpcode);
            else if (malformedForeignSpan) Assert.NotEqual(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
        });
        Check(failures, "capability/default-monitor reads", () => { Assert.Equal(expectedCapabilityReads, result.CapabilityReadCount); Assert.Equal(0, result.DefaultMonitorReadCount); });
        Check(failures, "caller ABI", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter); Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1); Assert.Equal(destination, result.Address1); Assert.Equal(graphicsBase, result.Address6);
            if (publishes && tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes is 27 or 28 or 29 or 30 or 31 or 32 or 33)
                Assert.Equal((ushort)0, (ushort)(result.StatusRegister & M68kCpuState.Extend));
            Assert.Empty(result.RegisterDifferences);
        });
        if (publishes)
        {
            Check(failures, "exact portable prefix", () => fixture.AssertOnlyOutputPrefixChanged(before, after, destination, expectedBytes));
            Check(failures, "following byte untouched", () => Assert.Equal(followingByte, fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "memory intact", () => AssertMemoryEqual(before, after));
            Check(failures, "no destination access", () => AssertNoDestinationAccess(result, destination, requestedBytes));
        }
        Assert.True(failures.Count == 0, $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(DisplayThirtyFourByteCases))]
    public void GetDisplayInfoDataPublishesTheThirtyFourByteDisplayPrefix(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        AssertDisplayThirtyFourBytePortableSeam();

        using var fixture = new Fixture(relocated, autoInitEntry);
        var maximumCapabilityBase = uint.MaxValue - (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        var lastAlignedCapabilityBase = maximumCapabilityBase & ~1u;
        var firstWrapCapabilityBase = maximumCapabilityBase + 1u;
        var handle = ModeId;
        var destination = 0x300u;
        var requestedBytes = 34u;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = foreignMode;
        var graphicsBase = fixture.GraphicsBase;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var publishes = false;
        var providerOwned = false;
        var ownedZero = false;
        var malformedForeignSpan = false;
        var supportsEcsOracle = true;
        var supportsAgaOracle = false;
        byte? chipRevision = null;
        var logicalCapabilityReads = 0;

        void Publish(uint mode, byte chip, bool ecs, bool aga)
        {
            publishes = true;
            handle = mode;
            expectedMode = mode;
            chipRevision = chip;
            supportsEcsOracle = ecs;
            supportsAgaOracle = aga;
            logicalCapabilityReads = 1;
        }

        switch (scenario)
        {
            case "disp34-pal-ordinary-even-ecs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp34-ntsc-ordinary-odd-alice": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.AaAlice, false, false); destination = 0x301; break;
            case "disp34-ntsc-superhires-lace-exact-end-lisa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresLaceKey, (byte)GraphicsChipRevision.AaLisa, false, false); destination = 0xFFFF_FFDE; break;
            case "disp34-null-ntsc-ehb-lace-aa-pair":
                expectedMode = GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteLaceKey;
                Publish(expectedMode, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true);
                handle = 0; modeId = expectedMode; break;
            case "disp34-private-default-aa-pair": Publish(GraphicsModeIds.DefaultMonitor, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); handle = GraphicsDisplayDatabase.DefaultModeHandle; break;
            case "disp34-pal-dpf-last-aligned-setaa": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresDualPlayfieldKey, (byte)GraphicsChipRevision.SetAa, true, true); graphicsBase = lastAlignedCapabilityBase; break;
            case "disp34-ntsc-dpf2-first-even-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresDualPlayfieldTwoKey, 0, false, false); graphicsBase = 2; break;
            case "disp34-default-ham-setaa": Publish(GraphicsModeIds.DefaultMonitor | GraphicsModeIds.HamKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp34-ntsc-superhires-setaa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp34-pal-superhires-ocs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, 0, false, false); graphicsBase = 2; break;
            case "disp34-ntsc-superhires-ecs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp34-pal-superhires-dpf2-lace-aa-pair": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresDualPlayfieldTwoLaceKey, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); break;
            case "disp34-foreign-provider": handle = foreignMode; modeId = ModeId; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "disp34-null-a6-control": graphicsBase = 0; break;
            case "disp34-odd-a6-control": graphicsBase = 3; break;
            case "disp34-exact-end-a6-control": graphicsBase = maximumCapabilityBase; break;
            case "disp34-first-wrap-a6-control": graphicsBase = firstWrapCapabilityBase; break;
            case "disp34-first-wrap-control": destination = 0xFFFF_FFDF; graphicsBase = uint.MaxValue; break;
            case "disp34-superhires-first-wrap-control": handle = GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey; expectedMode = handle; destination = 0xFFFF_FFDF; break;
            case "disp34-foreign-first-wrap-control": handle = foreignMode; modeId = ModeId; destination = 0xFFFF_FFDF; graphicsBase = uint.MaxValue; malformedForeignSpan = true; break;
            case "disp32-ordinary-valid-control": requestedBytes = 32; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "disp33-superhires-valid-control": requestedBytes = 33; Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp55-control": requestedBytes = 55; expectedResult = requestedBytes; graphicsBase = uint.MaxValue; break;
            case "disp56-control": requestedBytes = 56; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "name34-control": tag = GraphicsDisplayDatabase.DtagName; publishes = true; graphicsBase = uint.MaxValue; break;
            case "dims34-control": tag = GraphicsDisplayDatabase.DtagDims; graphicsBase = uint.MaxValue; break;
            case "mntr34-control": tag = GraphicsDisplayDatabase.DtagMntr; graphicsBase = uint.MaxValue; break;
            case "unsupported34-control": tag = UnsupportedTag; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            case "vec34-provider-control": tag = GraphicsDisplayDatabase.DtagVec; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "sentinel34-control": handle = 0; modeId = GraphicsModeIds.Invalid; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "disp34-ntsc-superhires-lace-exact-end-lisa")
            Assert.Equal((ulong)uint.MaxValue, (ulong)destination + requestedBytes - 1);
        if (scenario is "disp34-first-wrap-control" or "disp34-superhires-first-wrap-control" or "disp34-foreign-first-wrap-control")
            Assert.Equal((ulong)uint.MaxValue + 1, (ulong)destination + requestedBytes - 1);
        if (scenario == "disp34-default-ham-setaa")
            Assert.NotEqual(0u, expectedMode);
        if (chipRevision.HasValue)
            fixture.SeedChipRevision(graphicsBase, chipRevision.Value);

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(tag, handle, modeId, requestedBytes, supportsEcsOracle, supportsAgaOracle);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);
            Assert.Equal(BuildQueryHeader(tag, expectedMode), expectedBytes.Take(16));
            if (tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes == 34)
            {
                var expectedSpriteResolution = ExpectedDisplaySpriteResolution(expectedMode);
                var expectedPaletteRange = ExpectedDisplayPaletteRange(expectedMode, supportsAgaOracle);
                Assert.Equal((byte)0, expectedBytes[32]);
                Assert.Equal((byte)expectedSpriteResolution, expectedBytes[33]);
                Assert.Equal((byte)(expectedPaletteRange >> 8), expectedBytes[30]);
                Assert.Equal((byte)(expectedPaletteRange & 0x00FF), expectedBytes[31]);
                Assert.Equal((byte)8, expectedBytes[29]);
                Assert.Equal((byte)0, expectedBytes[28]);
                Assert.Equal((byte)(ExpectedDisplayPixelSpeed(expectedMode) & 0x00FF), expectedBytes[27]);
                Assert.Equal((byte)0, expectedBytes[26]);
                Assert.Equal((byte)1, expectedBytes[25]);
                Assert.Equal((byte)0, expectedBytes[24]);
                Assert.Equal(ExpectedDisplayByte17(expectedMode), expectedBytes[23]);
                Assert.Equal((byte)0, expectedBytes[22]);
                Assert.Equal(ExpectedDisplayByte15(expectedMode), expectedBytes[21]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 ? (byte)0x01 :
                    (expectedMode & GraphicsModeIds.ExtraHalfBriteMode) != 0 ? (byte)0x31 : (byte)0x21, expectedBytes[20]);
                Assert.Equal(supportsAgaOracle ? (byte)0x11 : (byte)0x10, expectedBytes[19]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 && !supportsEcsOracle ? (byte)1 : (byte)0, expectedBytes[17]);
            }
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes ? fixture.ReadOutputByte(followingAddress) : (byte)0;
        var result = fixture.Invoke(handle, destination, requestedBytes, tag, modeId, graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        var expectedCapabilityReads = logicalCapabilityReads;
        Check(failures, "result and terminal provenance", () =>
        {
            Assert.Equal(expectedResult, result.Data0); Assert.False(result.UsedFallback); Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned) Assert.Equal(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
            else if (publishes) Assert.Equal(AddQuickFourLongA7Opcode, result.NativeReturnPredecessorOpcode);
            else if (ownedZero) Assert.Equal(MoveQuickZeroD0Opcode, result.NativeReturnPredecessorOpcode);
            else if (malformedForeignSpan) Assert.NotEqual(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
        });
        Check(failures, "capability/default-monitor reads", () => { Assert.Equal(expectedCapabilityReads, result.CapabilityReadCount); Assert.Equal(0, result.DefaultMonitorReadCount); });
        Check(failures, "caller ABI", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter); Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1); Assert.Equal(destination, result.Address1); Assert.Equal(graphicsBase, result.Address6);
            if (publishes && tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes is 27 or 28 or 29 or 30 or 31 or 32 or 33 or 34)
                Assert.Equal((ushort)0, (ushort)(result.StatusRegister & M68kCpuState.Extend));
            Assert.Empty(result.RegisterDifferences);
        });
        if (publishes)
        {
            Check(failures, "exact portable prefix", () => fixture.AssertOnlyOutputPrefixChanged(before, after, destination, expectedBytes));
            Check(failures, "following byte untouched", () => Assert.Equal(followingByte, fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "memory intact", () => AssertMemoryEqual(before, after));
            Check(failures, "no destination access", () => AssertNoDestinationAccess(result, destination, requestedBytes));
        }
        Assert.True(failures.Count == 0, $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(DisplayThirtyFiveByteCases))]
    public void GetDisplayInfoDataPublishesTheThirtyFiveByteDisplayPrefix(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        AssertDisplayThirtyFiveBytePortableSeam();

        using var fixture = new Fixture(relocated, autoInitEntry);
        var maximumCapabilityBase = uint.MaxValue - (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        var lastAlignedCapabilityBase = maximumCapabilityBase & ~1u;
        var firstWrapCapabilityBase = maximumCapabilityBase + 1u;
        var handle = ModeId;
        var destination = 0x300u;
        var requestedBytes = 35u;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = foreignMode;
        var graphicsBase = fixture.GraphicsBase;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var publishes = false;
        var providerOwned = false;
        var ownedZero = false;
        var malformedForeignSpan = false;
        var supportsEcsOracle = true;
        var supportsAgaOracle = false;
        byte? chipRevision = null;
        var logicalCapabilityReads = 0;

        void Publish(uint mode, byte chip, bool ecs, bool aga)
        {
            publishes = true;
            handle = mode;
            expectedMode = mode;
            chipRevision = chip;
            supportsEcsOracle = ecs;
            supportsAgaOracle = aga;
            logicalCapabilityReads = 1;
        }

        switch (scenario)
        {
            case "disp35-pal-ordinary-even-ecs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp35-ntsc-ordinary-odd-alice": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.AaAlice, false, false); destination = 0x301; break;
            case "disp35-ntsc-superhires-lace-exact-end-lisa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresLaceKey, (byte)GraphicsChipRevision.AaLisa, false, false); destination = 0xFFFF_FFDD; break;
            case "disp35-null-ntsc-ehb-lace-aa-pair":
                expectedMode = GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteLaceKey;
                Publish(expectedMode, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true);
                handle = 0; modeId = expectedMode; break;
            case "disp35-private-default-aa-pair": Publish(GraphicsModeIds.DefaultMonitor, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); handle = GraphicsDisplayDatabase.DefaultModeHandle; break;
            case "disp35-pal-dpf-last-aligned-setaa": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresDualPlayfieldKey, (byte)GraphicsChipRevision.SetAa, true, true); graphicsBase = lastAlignedCapabilityBase; break;
            case "disp35-ntsc-dpf2-first-even-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresDualPlayfieldTwoKey, 0, false, false); graphicsBase = 2; break;
            case "disp35-default-ham-setaa": Publish(GraphicsModeIds.DefaultMonitor | GraphicsModeIds.HamKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp35-ntsc-superhires-setaa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp35-pal-superhires-ocs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, 0, false, false); graphicsBase = 2; break;
            case "disp35-ntsc-superhires-ecs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp35-pal-superhires-dpf2-lace-aa-pair": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresDualPlayfieldTwoLaceKey, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); break;
            case "disp35-foreign-provider": handle = foreignMode; modeId = ModeId; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "disp35-null-a6-control": graphicsBase = 0; break;
            case "disp35-odd-a6-control": graphicsBase = 3; break;
            case "disp35-exact-end-a6-control": graphicsBase = maximumCapabilityBase; break;
            case "disp35-first-wrap-a6-control": graphicsBase = firstWrapCapabilityBase; break;
            case "disp35-first-wrap-control": destination = 0xFFFF_FFDE; graphicsBase = uint.MaxValue; break;
            case "disp35-superhires-first-wrap-control": handle = GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey; expectedMode = handle; destination = 0xFFFF_FFDE; break;
            case "disp35-foreign-first-wrap-control": handle = foreignMode; modeId = ModeId; destination = 0xFFFF_FFDE; graphicsBase = uint.MaxValue; malformedForeignSpan = true; break;
            case "disp33-ordinary-valid-control": requestedBytes = 33; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "disp34-superhires-valid-control": requestedBytes = 34; Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp55-control": requestedBytes = 55; expectedResult = requestedBytes; graphicsBase = uint.MaxValue; break;
            case "disp56-control": requestedBytes = 56; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "name35-control": tag = GraphicsDisplayDatabase.DtagName; publishes = true; graphicsBase = uint.MaxValue; break;
            case "dims35-control": tag = GraphicsDisplayDatabase.DtagDims; graphicsBase = uint.MaxValue; break;
            case "mntr35-control": tag = GraphicsDisplayDatabase.DtagMntr; graphicsBase = uint.MaxValue; break;
            case "unsupported35-control": tag = UnsupportedTag; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            case "vec35-provider-control": tag = GraphicsDisplayDatabase.DtagVec; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "sentinel35-control": handle = 0; modeId = GraphicsModeIds.Invalid; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "disp35-ntsc-superhires-lace-exact-end-lisa")
            Assert.Equal((ulong)uint.MaxValue, (ulong)destination + requestedBytes - 1);
        if (scenario is "disp35-first-wrap-control" or "disp35-superhires-first-wrap-control" or "disp35-foreign-first-wrap-control")
            Assert.Equal((ulong)uint.MaxValue + 1, (ulong)destination + requestedBytes - 1);
        if (scenario == "disp35-default-ham-setaa")
            Assert.NotEqual(0u, expectedMode);
        if (chipRevision.HasValue)
            fixture.SeedChipRevision(graphicsBase, chipRevision.Value);

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(tag, handle, modeId, requestedBytes, supportsEcsOracle, supportsAgaOracle);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);
            Assert.Equal(BuildQueryHeader(tag, expectedMode), expectedBytes.Take(16));
            if (tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes == 35)
            {
                var expectedSpriteResolution = ExpectedDisplaySpriteResolution(expectedMode);
                var expectedPaletteRange = ExpectedDisplayPaletteRange(expectedMode, supportsAgaOracle);
                Assert.Equal((byte)0, expectedBytes[34]);
                Assert.Equal((byte)0, expectedBytes[32]);
                Assert.Equal((byte)expectedSpriteResolution, expectedBytes[33]);
                Assert.Equal((byte)(expectedPaletteRange >> 8), expectedBytes[30]);
                Assert.Equal((byte)(expectedPaletteRange & 0x00FF), expectedBytes[31]);
                Assert.Equal((byte)8, expectedBytes[29]);
                Assert.Equal((byte)0, expectedBytes[28]);
                Assert.Equal((byte)(ExpectedDisplayPixelSpeed(expectedMode) & 0x00FF), expectedBytes[27]);
                Assert.Equal((byte)0, expectedBytes[26]);
                Assert.Equal((byte)1, expectedBytes[25]);
                Assert.Equal((byte)0, expectedBytes[24]);
                Assert.Equal(ExpectedDisplayByte17(expectedMode), expectedBytes[23]);
                Assert.Equal((byte)0, expectedBytes[22]);
                Assert.Equal(ExpectedDisplayByte15(expectedMode), expectedBytes[21]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 ? (byte)0x01 :
                    (expectedMode & GraphicsModeIds.ExtraHalfBriteMode) != 0 ? (byte)0x31 : (byte)0x21, expectedBytes[20]);
                Assert.Equal(supportsAgaOracle ? (byte)0x11 : (byte)0x10, expectedBytes[19]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 && !supportsEcsOracle ? (byte)1 : (byte)0, expectedBytes[17]);
            }
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes ? fixture.ReadOutputByte(followingAddress) : (byte)0;
        var result = fixture.Invoke(handle, destination, requestedBytes, tag, modeId, graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        var expectedCapabilityReads = logicalCapabilityReads;
        Check(failures, "result and terminal provenance", () =>
        {
            Assert.Equal(expectedResult, result.Data0); Assert.False(result.UsedFallback); Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned) Assert.Equal(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
            else if (publishes) Assert.Equal(AddQuickFourLongA7Opcode, result.NativeReturnPredecessorOpcode);
            else if (ownedZero) Assert.Equal(MoveQuickZeroD0Opcode, result.NativeReturnPredecessorOpcode);
            else if (malformedForeignSpan) Assert.NotEqual(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
        });
        Check(failures, "capability/default-monitor reads", () => { Assert.Equal(expectedCapabilityReads, result.CapabilityReadCount); Assert.Equal(0, result.DefaultMonitorReadCount); });
        Check(failures, "caller ABI", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter); Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1); Assert.Equal(destination, result.Address1); Assert.Equal(graphicsBase, result.Address6);
            if (publishes && tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes is 27 or 28 or 29 or 30 or 31 or 32 or 33 or 34 or 35)
                Assert.Equal((ushort)0, (ushort)(result.StatusRegister & M68kCpuState.Extend));
            Assert.Empty(result.RegisterDifferences);
        });
        if (publishes)
        {
            Check(failures, "exact portable prefix", () => fixture.AssertOnlyOutputPrefixChanged(before, after, destination, expectedBytes));
            Check(failures, "following byte untouched", () => Assert.Equal(followingByte, fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "memory intact", () => AssertMemoryEqual(before, after));
            Check(failures, "no destination access", () => AssertNoDestinationAccess(result, destination, requestedBytes));
        }
        Assert.True(failures.Count == 0, $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(DisplayThirtySixByteCases))]
    public void GetDisplayInfoDataPublishesTheThirtySixByteDisplayPrefix(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        AssertDisplayThirtySixBytePortableSeam();

        using var fixture = new Fixture(relocated, autoInitEntry);
        var maximumCapabilityBase = uint.MaxValue - (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        var lastAlignedCapabilityBase = maximumCapabilityBase & ~1u;
        var firstWrapCapabilityBase = maximumCapabilityBase + 1u;
        var handle = ModeId;
        var destination = 0x300u;
        var requestedBytes = 36u;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = foreignMode;
        var graphicsBase = fixture.GraphicsBase;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var publishes = false;
        var providerOwned = false;
        var ownedZero = false;
        var malformedForeignSpan = false;
        var supportsEcsOracle = true;
        var supportsAgaOracle = false;
        byte? chipRevision = null;
        var logicalCapabilityReads = 0;

        void Publish(uint mode, byte chip, bool ecs, bool aga)
        {
            publishes = true;
            handle = mode;
            expectedMode = mode;
            chipRevision = chip;
            supportsEcsOracle = ecs;
            supportsAgaOracle = aga;
            logicalCapabilityReads = 1;
        }

        switch (scenario)
        {
            case "disp36-pal-ordinary-even-ecs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp36-ntsc-ordinary-odd-alice": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.AaAlice, false, false); destination = 0x301; break;
            case "disp36-ntsc-superhires-lace-exact-end-lisa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresLaceKey, (byte)GraphicsChipRevision.AaLisa, false, false); destination = 0xFFFF_FFDC; break;
            case "disp36-null-ntsc-ehb-lace-aa-pair":
                expectedMode = GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteLaceKey;
                Publish(expectedMode, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true);
                handle = 0; modeId = expectedMode; break;
            case "disp36-private-default-aa-pair": Publish(GraphicsModeIds.DefaultMonitor, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); handle = GraphicsDisplayDatabase.DefaultModeHandle; break;
            case "disp36-pal-dpf-last-aligned-setaa": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresDualPlayfieldKey, (byte)GraphicsChipRevision.SetAa, true, true); graphicsBase = lastAlignedCapabilityBase; break;
            case "disp36-ntsc-dpf2-first-even-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresDualPlayfieldTwoKey, 0, false, false); graphicsBase = 2; break;
            case "disp36-default-ham-setaa": Publish(GraphicsModeIds.DefaultMonitor | GraphicsModeIds.HamKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp36-ntsc-superhires-setaa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp36-pal-superhires-ocs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, 0, false, false); graphicsBase = 2; break;
            case "disp36-ntsc-superhires-ecs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp36-pal-superhires-dpf2-lace-aa-pair": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresDualPlayfieldTwoLaceKey, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); break;
            case "disp36-foreign-provider": handle = foreignMode; modeId = ModeId; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "disp36-null-a6-control": graphicsBase = 0; break;
            case "disp36-odd-a6-control": graphicsBase = 3; break;
            case "disp36-exact-end-a6-control": graphicsBase = maximumCapabilityBase; break;
            case "disp36-first-wrap-a6-control": graphicsBase = firstWrapCapabilityBase; break;
            case "disp36-first-wrap-control": destination = 0xFFFF_FFDD; graphicsBase = uint.MaxValue; break;
            case "disp36-superhires-first-wrap-control": handle = GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey; expectedMode = handle; destination = 0xFFFF_FFDD; break;
            case "disp36-foreign-first-wrap-control": handle = foreignMode; modeId = ModeId; destination = 0xFFFF_FFDD; graphicsBase = uint.MaxValue; malformedForeignSpan = true; break;
            case "disp34-ordinary-valid-control": requestedBytes = 34; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "disp35-superhires-valid-control": requestedBytes = 35; Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp55-control": requestedBytes = 55; expectedResult = requestedBytes; graphicsBase = uint.MaxValue; break;
            case "disp56-control": requestedBytes = 56; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "name36-control": tag = GraphicsDisplayDatabase.DtagName; publishes = true; graphicsBase = uint.MaxValue; break;
            case "dims36-control": tag = GraphicsDisplayDatabase.DtagDims; graphicsBase = uint.MaxValue; break;
            case "mntr36-control": tag = GraphicsDisplayDatabase.DtagMntr; graphicsBase = uint.MaxValue; break;
            case "unsupported36-control": tag = UnsupportedTag; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            case "vec36-provider-control": tag = GraphicsDisplayDatabase.DtagVec; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "sentinel36-control": handle = 0; modeId = GraphicsModeIds.Invalid; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "disp36-ntsc-superhires-lace-exact-end-lisa")
            Assert.Equal((ulong)uint.MaxValue, (ulong)destination + requestedBytes - 1);
        if (scenario is "disp36-first-wrap-control" or "disp36-superhires-first-wrap-control" or "disp36-foreign-first-wrap-control")
            Assert.Equal((ulong)uint.MaxValue + 1, (ulong)destination + requestedBytes - 1);
        if (scenario == "disp36-default-ham-setaa")
            Assert.NotEqual(0u, expectedMode);
        if (chipRevision.HasValue)
            fixture.SeedChipRevision(graphicsBase, chipRevision.Value);

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(tag, handle, modeId, requestedBytes, supportsEcsOracle, supportsAgaOracle);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);
            Assert.Equal(BuildQueryHeader(tag, expectedMode), expectedBytes.Take(16));
            if (tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes == 36)
            {
                var expectedSpriteResolution = ExpectedDisplaySpriteResolution(expectedMode);
                var expectedPaletteRange = ExpectedDisplayPaletteRange(expectedMode, supportsAgaOracle);
                Assert.Equal((byte)0, expectedBytes[34]);
                Assert.Equal((byte)1, expectedBytes[35]);
                Assert.Equal((byte)0, expectedBytes[32]);
                Assert.Equal((byte)expectedSpriteResolution, expectedBytes[33]);
                Assert.Equal((byte)(expectedPaletteRange >> 8), expectedBytes[30]);
                Assert.Equal((byte)(expectedPaletteRange & 0x00FF), expectedBytes[31]);
                Assert.Equal((byte)8, expectedBytes[29]);
                Assert.Equal((byte)0, expectedBytes[28]);
                Assert.Equal((byte)(ExpectedDisplayPixelSpeed(expectedMode) & 0x00FF), expectedBytes[27]);
                Assert.Equal((byte)0, expectedBytes[26]);
                Assert.Equal((byte)1, expectedBytes[25]);
                Assert.Equal((byte)0, expectedBytes[24]);
                Assert.Equal(ExpectedDisplayByte17(expectedMode), expectedBytes[23]);
                Assert.Equal((byte)0, expectedBytes[22]);
                Assert.Equal(ExpectedDisplayByte15(expectedMode), expectedBytes[21]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 ? (byte)0x01 :
                    (expectedMode & GraphicsModeIds.ExtraHalfBriteMode) != 0 ? (byte)0x31 : (byte)0x21, expectedBytes[20]);
                Assert.Equal(supportsAgaOracle ? (byte)0x11 : (byte)0x10, expectedBytes[19]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 && !supportsEcsOracle ? (byte)1 : (byte)0, expectedBytes[17]);
            }
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes ? fixture.ReadOutputByte(followingAddress) : (byte)0;
        var result = fixture.Invoke(handle, destination, requestedBytes, tag, modeId, graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        var expectedCapabilityReads = logicalCapabilityReads;
        Check(failures, "result and terminal provenance", () =>
        {
            Assert.Equal(expectedResult, result.Data0); Assert.False(result.UsedFallback); Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned) Assert.Equal(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
            else if (publishes) Assert.Equal(AddQuickFourLongA7Opcode, result.NativeReturnPredecessorOpcode);
            else if (ownedZero) Assert.Equal(MoveQuickZeroD0Opcode, result.NativeReturnPredecessorOpcode);
            else if (malformedForeignSpan) Assert.NotEqual(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
        });
        Check(failures, "capability/default-monitor reads", () => { Assert.Equal(expectedCapabilityReads, result.CapabilityReadCount); Assert.Equal(0, result.DefaultMonitorReadCount); });
        Check(failures, "caller ABI", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter); Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1); Assert.Equal(destination, result.Address1); Assert.Equal(graphicsBase, result.Address6);
            if (publishes && tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes is 27 or 28 or 29 or 30 or 31 or 32 or 33 or 34 or 35 or 36)
                Assert.Equal((ushort)0, (ushort)(result.StatusRegister & M68kCpuState.Extend));
            Assert.Empty(result.RegisterDifferences);
        });
        if (publishes)
        {
            Check(failures, "exact portable prefix", () => fixture.AssertOnlyOutputPrefixChanged(before, after, destination, expectedBytes));
            Check(failures, "following byte untouched", () => Assert.Equal(followingByte, fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "memory intact", () => AssertMemoryEqual(before, after));
            Check(failures, "no destination access", () => AssertNoDestinationAccess(result, destination, requestedBytes));
        }
        Assert.True(failures.Count == 0, $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(DisplayThirtySevenByteCases))]
    public void GetDisplayInfoDataPublishesTheThirtySevenByteDisplayPrefix(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        AssertDisplayThirtySevenBytePortableSeam();

        using var fixture = new Fixture(relocated, autoInitEntry);
        var maximumCapabilityBase = uint.MaxValue - (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        var lastAlignedCapabilityBase = maximumCapabilityBase & ~1u;
        var firstWrapCapabilityBase = maximumCapabilityBase + 1u;
        var handle = ModeId;
        var destination = 0x300u;
        var requestedBytes = 37u;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = foreignMode;
        var graphicsBase = fixture.GraphicsBase;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var publishes = false;
        var providerOwned = false;
        var ownedZero = false;
        var malformedForeignSpan = false;
        var supportsEcsOracle = true;
        var supportsAgaOracle = false;
        byte? chipRevision = null;
        var logicalCapabilityReads = 0;

        void Publish(uint mode, byte chip, bool ecs, bool aga)
        {
            publishes = true;
            handle = mode;
            expectedMode = mode;
            chipRevision = chip;
            supportsEcsOracle = ecs;
            supportsAgaOracle = aga;
            logicalCapabilityReads = 1;
        }

        switch (scenario)
        {
            case "disp37-pal-ordinary-even-ecs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp37-ntsc-ordinary-odd-alice": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.AaAlice, false, false); destination = 0x301; break;
            case "disp37-ntsc-superhires-lace-exact-end-lisa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresLaceKey, (byte)GraphicsChipRevision.AaLisa, false, false); destination = 0xFFFF_FFDB; break;
            case "disp37-null-ntsc-ehb-lace-aa-pair":
                expectedMode = GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteLaceKey;
                Publish(expectedMode, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true);
                handle = 0; modeId = expectedMode; break;
            case "disp37-private-default-aa-pair": Publish(GraphicsModeIds.DefaultMonitor, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); handle = GraphicsDisplayDatabase.DefaultModeHandle; break;
            case "disp37-pal-dpf-last-aligned-setaa": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresDualPlayfieldKey, (byte)GraphicsChipRevision.SetAa, true, true); graphicsBase = lastAlignedCapabilityBase; break;
            case "disp37-ntsc-dpf2-first-even-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresDualPlayfieldTwoKey, 0, false, false); graphicsBase = 2; break;
            case "disp37-default-ham-setaa": Publish(GraphicsModeIds.DefaultMonitor | GraphicsModeIds.HamKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp37-ntsc-superhires-setaa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp37-pal-superhires-ocs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, 0, false, false); graphicsBase = 2; break;
            case "disp37-ntsc-superhires-ecs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp37-pal-superhires-dpf2-lace-aa-pair": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresDualPlayfieldTwoLaceKey, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); break;
            case "disp37-foreign-provider": handle = foreignMode; modeId = ModeId; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "disp37-null-a6-control": graphicsBase = 0; break;
            case "disp37-odd-a6-control": graphicsBase = 3; break;
            case "disp37-exact-end-a6-control": graphicsBase = maximumCapabilityBase; break;
            case "disp37-first-wrap-a6-control": graphicsBase = firstWrapCapabilityBase; break;
            case "disp37-first-wrap-control": destination = 0xFFFF_FFDC; graphicsBase = uint.MaxValue; break;
            case "disp37-superhires-first-wrap-control": handle = GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey; expectedMode = handle; destination = 0xFFFF_FFDC; break;
            case "disp37-foreign-first-wrap-control": handle = foreignMode; modeId = ModeId; destination = 0xFFFF_FFDC; graphicsBase = uint.MaxValue; malformedForeignSpan = true; break;
            case "disp35-ordinary-valid-control": requestedBytes = 35; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "disp36-superhires-valid-control": requestedBytes = 36; Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp55-control": requestedBytes = 55; expectedResult = requestedBytes; graphicsBase = uint.MaxValue; break;
            case "disp56-control": requestedBytes = 56; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "name37-control": tag = GraphicsDisplayDatabase.DtagName; publishes = true; graphicsBase = uint.MaxValue; break;
            case "dims37-control": tag = GraphicsDisplayDatabase.DtagDims; graphicsBase = uint.MaxValue; break;
            case "mntr37-control": tag = GraphicsDisplayDatabase.DtagMntr; graphicsBase = uint.MaxValue; break;
            case "unsupported37-control": tag = UnsupportedTag; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            case "vec37-provider-control": tag = GraphicsDisplayDatabase.DtagVec; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "sentinel37-control": handle = 0; modeId = GraphicsModeIds.Invalid; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "disp37-ntsc-superhires-lace-exact-end-lisa")
            Assert.Equal((ulong)uint.MaxValue, (ulong)destination + requestedBytes - 1);
        if (scenario is "disp37-first-wrap-control" or "disp37-superhires-first-wrap-control" or "disp37-foreign-first-wrap-control")
            Assert.Equal((ulong)uint.MaxValue + 1, (ulong)destination + requestedBytes - 1);
        if (scenario == "disp37-default-ham-setaa")
            Assert.NotEqual(0u, expectedMode);
        if (chipRevision.HasValue)
            fixture.SeedChipRevision(graphicsBase, chipRevision.Value);

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(tag, handle, modeId, requestedBytes, supportsEcsOracle, supportsAgaOracle);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);
            Assert.Equal(BuildQueryHeader(tag, expectedMode), expectedBytes.Take(16));
            if (tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes == 37)
            {
                var expectedSpriteResolution = ExpectedDisplaySpriteResolution(expectedMode);
                var expectedPaletteRange = ExpectedDisplayPaletteRange(expectedMode, supportsAgaOracle);
                Assert.Equal((byte)0, expectedBytes[36]);
                Assert.Equal((byte)0, expectedBytes[34]);
                Assert.Equal((byte)1, expectedBytes[35]);
                Assert.Equal((byte)0, expectedBytes[32]);
                Assert.Equal((byte)expectedSpriteResolution, expectedBytes[33]);
                Assert.Equal((byte)(expectedPaletteRange >> 8), expectedBytes[30]);
                Assert.Equal((byte)(expectedPaletteRange & 0x00FF), expectedBytes[31]);
                Assert.Equal((byte)8, expectedBytes[29]);
                Assert.Equal((byte)0, expectedBytes[28]);
                Assert.Equal((byte)(ExpectedDisplayPixelSpeed(expectedMode) & 0x00FF), expectedBytes[27]);
                Assert.Equal((byte)0, expectedBytes[26]);
                Assert.Equal((byte)1, expectedBytes[25]);
                Assert.Equal((byte)0, expectedBytes[24]);
                Assert.Equal(ExpectedDisplayByte17(expectedMode), expectedBytes[23]);
                Assert.Equal((byte)0, expectedBytes[22]);
                Assert.Equal(ExpectedDisplayByte15(expectedMode), expectedBytes[21]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 ? (byte)0x01 :
                    (expectedMode & GraphicsModeIds.ExtraHalfBriteMode) != 0 ? (byte)0x31 : (byte)0x21, expectedBytes[20]);
                Assert.Equal(supportsAgaOracle ? (byte)0x11 : (byte)0x10, expectedBytes[19]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 && !supportsEcsOracle ? (byte)1 : (byte)0, expectedBytes[17]);
            }
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes ? fixture.ReadOutputByte(followingAddress) : (byte)0;
        var result = fixture.Invoke(handle, destination, requestedBytes, tag, modeId, graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        var expectedCapabilityReads = logicalCapabilityReads;
        Check(failures, "result and terminal provenance", () =>
        {
            Assert.Equal(expectedResult, result.Data0); Assert.False(result.UsedFallback); Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned) Assert.Equal(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
            else if (publishes) Assert.Equal(AddQuickFourLongA7Opcode, result.NativeReturnPredecessorOpcode);
            else if (ownedZero) Assert.Equal(MoveQuickZeroD0Opcode, result.NativeReturnPredecessorOpcode);
            else if (malformedForeignSpan) Assert.NotEqual(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
        });
        Check(failures, "capability/default-monitor reads", () => { Assert.Equal(expectedCapabilityReads, result.CapabilityReadCount); Assert.Equal(0, result.DefaultMonitorReadCount); });
        Check(failures, "caller ABI", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter); Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1); Assert.Equal(destination, result.Address1); Assert.Equal(graphicsBase, result.Address6);
            if (publishes && tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes is 27 or 28 or 29 or 30 or 31 or 32 or 33 or 34 or 35 or 36 or 37)
                Assert.Equal((ushort)0, (ushort)(result.StatusRegister & M68kCpuState.Extend));
            Assert.Empty(result.RegisterDifferences);
        });
        if (publishes)
        {
            Check(failures, "exact portable prefix", () => fixture.AssertOnlyOutputPrefixChanged(before, after, destination, expectedBytes));
            Check(failures, "following byte untouched", () => Assert.Equal(followingByte, fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "memory intact", () => AssertMemoryEqual(before, after));
            Check(failures, "no destination access", () => AssertNoDestinationAccess(result, destination, requestedBytes));
        }
        Assert.True(failures.Count == 0, $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(NamePayloadPrefixCases))]
    public void GetDisplayInfoDataPublishesEveryPartialNamePayloadPrefixLength(
        bool relocated,
        bool autoInitEntry,
        string scenario,
        uint requestedBytes)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const uint foreignMode = 0xDEAD_BEEFu;
        var longNameMode =
            GraphicsModeIds.PalMonitor |
            GraphicsModeIds.HiresDualPlayfieldTwoLaceKey;
        var finalCanonicalMode =
            GraphicsModeIds.NtscMonitor |
            GraphicsModeIds.ExtraHalfBriteLaceKey;
        using var fixture = new Fixture(relocated, autoInitEntry);
        var handle = longNameMode;
        var destination = (requestedBytes & 1u) == 0 ? 0x300u : 0x301u;
        var tag = GraphicsDisplayDatabase.DtagName;
        var modeId = foreignMode;
        var publishes = true;
        var providerOwned = false;

        if (!scenario.StartsWith("payload-", StringComparison.Ordinal))
        {
            switch (scenario)
            {
                case "exact-end-17":
                case "exact-end-55":
                    destination = LastBase(checked((int)requestedBytes));
                    break;
                case "first-wrap-17":
                case "first-wrap-55":
                    destination = unchecked(
                        LastBase(checked((int)requestedBytes)) + 1u);
                    publishes = false;
                    break;
                case "mode-authority-47":
                    handle = ModeId;
                    modeId = longNameMode;
                    break;
                case "private-default-48":
                    handle = GraphicsDisplayDatabase.DefaultModeHandle;
                    break;
                case "final-canonical-49":
                    handle = 0;
                    modeId = finalCanonicalMode;
                    break;
                case "foreign-provider-55":
                    handle = foreignMode;
                    modeId = ModeId;
                    publishes = false;
                    providerOwned = true;
                    break;
                case "name-16-control":
                case "name-56-control":
                    break;
                case "name-57-control":
                    destination = 0x300u;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario));
            }
        }

        if (scenario.StartsWith("payload-", StringComparison.Ordinal))
        {
            Assert.Equal(
                (requestedBytes & 1u) == 0 ? 0u : 1u,
                destination & 1u);
        }
        if (scenario.StartsWith("exact-end-", StringComparison.Ordinal))
        {
            Assert.Equal(
                (ulong)uint.MaxValue,
                (ulong)destination + requestedBytes - 1u);
        }
        if (scenario.StartsWith("first-wrap-", StringComparison.Ordinal))
        {
            Assert.Equal(
                (ulong)uint.MaxValue + 1u,
                (ulong)destination + requestedBytes - 1u);
        }

        var expectedResult = requestedBytes;
        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableNameQuery(handle, modeId, requestedBytes);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);

            if (requestedBytes is > 16u and < 56u)
            {
                Assert.Equal(checked((int)requestedBytes), expectedBytes.Length);
                Assert.Equal((byte)0, expectedBytes[^1]);
            }

            if (scenario.StartsWith("payload-", StringComparison.Ordinal))
            {
                var fullName = BuildPortableNameQuery(handle, modeId, 56u).Bytes;
                if (requestedBytes <= 47u)
                {
                    Assert.NotEqual(
                        fullName[checked((int)requestedBytes) - 1],
                        expectedBytes[^1]);
                }
                else
                {
                    Assert.Equal(
                        fullName[checked((int)requestedBytes) - 1],
                        expectedBytes[^1]);
                }
            }
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes
            ? fixture.ReadOutputByte(followingAddress)
            : (byte)0;
        var result = fixture.Invoke(
            handle,
            destination,
            requestedBytes,
            tag,
            modeId,
            graphicsBase: uint.MaxValue);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();

        Check(failures, "result and native/provider terminal", () =>
        {
            Assert.Equal(expectedResult, result.Data0);
            Assert.False(result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned)
            {
                Assert.Equal(
                    BranchAlwaysWordOpcode,
                    result.NativeReturnPredecessorOpcode);
            }
            else if (publishes)
            {
                Assert.Equal(
                    AddQuickFourLongA7Opcode,
                    result.NativeReturnPredecessorOpcode);
            }
        });
        Check(failures, "caller PC/SP, D1, A1, and supplied A6", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(uint.MaxValue, result.Address6);
        });
        Check(failures, "D2-D7/A2-A6 public frame", () =>
            Assert.True(
                result.RegisterDifferences.Length == 0,
                string.Join("; ", result.RegisterDifferences)));
        Check(failures, "name payload remains A6-read-free", () =>
        {
            Assert.Equal(0, result.CapabilityReadCount);
            Assert.Equal(0, result.DefaultMonitorReadCount);
        });

        if (publishes)
        {
            Check(failures, "only N exact portable bytes change", () =>
                fixture.AssertOnlyOutputPrefixChanged(
                    before,
                    after,
                    destination,
                    expectedBytes));
            Check(failures, "the byte following the output remains untouched", () =>
                Assert.Equal(
                    followingByte,
                    fixture.ReadOutputByte(followingAddress)));
            if (requestedBytes is > 16u and < 56u)
            {
                Check(failures, "the partial payload ends in a forced NUL", () =>
                    Assert.Equal(
                        (byte)0,
                        fixture.ReadOutputByte(
                            unchecked(destination + requestedBytes - 1u))));
            }
        }
        else
        {
            Check(failures, "declined or provider-owned calls leave memory intact", () =>
                AssertMemoryEqual(before, after));
            Check(failures, "declined or provider-owned calls never access A1", () =>
                AssertNoDestinationAccess(result, destination, requestedBytes));
        }

        Assert.True(
            failures.Count == 0,
            $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    private static byte[] BuildQueryHeader(uint tag, uint modeId)
    {
        if (tag is GraphicsDisplayDatabase.DtagDisp or GraphicsDisplayDatabase.DtagDims or GraphicsDisplayDatabase.DtagMntr &&
            (modeId & 0xFFFF1000u) == 0)
            modeId |= 0x00021000u; // existing native/default portable PAL profile
        if (tag == GraphicsDisplayDatabase.DtagMntr && (modeId & 0xFFFF1000u) != 0)
            modeId &= 0xFFFF1000u;
        return
        [
            unchecked((byte)(tag >> 24)),
            unchecked((byte)(tag >> 16)),
            unchecked((byte)(tag >> 8)),
            unchecked((byte)tag),
            (byte)(modeId >> 24),
            (byte)(modeId >> 16),
            (byte)(modeId >> 8),
            (byte)modeId,
            0, 0, 0, 3,
            0, 0, 0, tag switch
            {
                GraphicsDisplayDatabase.DtagDisp => (byte)4,
                GraphicsDisplayDatabase.DtagDims => (byte)8,
                GraphicsDisplayDatabase.DtagMntr => (byte)9,
                _ => (byte)5
            }
        ];
    }

    private static (uint Result, byte[] Bytes) BuildPortableNameQuery(
        uint handle,
        uint modeId,
        uint requestedBytes)
        => BuildPortableQuery(
            GraphicsDisplayDatabase.DtagName,
            handle,
            modeId,
            requestedBytes);

    private static (uint Result, byte[] Bytes) BuildPortableQuery(
        uint tag,
        uint handle,
        uint modeId,
        uint requestedBytes,
        bool supportsEcsDisplay = true,
        bool supportsAgaDisplay = false)
    {
        const uint buffer = 0x20;
        var memory = new NameOracleMemory();
        var result = GraphicsDisplayDatabase.GetDisplayInfoData(
            memory,
            handle,
            buffer,
            requestedBytes,
            tag,
            modeId,
            supportsEcsDisplay: supportsEcsDisplay,
            supportsAgaDisplay: supportsAgaDisplay);
        Assert.InRange(
            result,
            0,
            tag switch
            {
                GraphicsDisplayDatabase.DtagDisp or GraphicsDisplayDatabase.DtagName => 0x38,
                GraphicsDisplayDatabase.DtagDims => 66,
                GraphicsDisplayDatabase.DtagMntr => 88,
                _ => 0
            });
        return (
            checked((uint)result),
            memory.ReadBytes(buffer, result));
    }

    private static void AssertDisplaySeventeenBytePortableSeam(uint superHiresMode)
    {
        var ocs17 = BuildPortableQuery(
            GraphicsDisplayDatabase.DtagDisp,
            superHiresMode,
            superHiresMode,
            17,
            supportsEcsDisplay: false);
        var ecs17 = BuildPortableQuery(
            GraphicsDisplayDatabase.DtagDisp,
            superHiresMode,
            superHiresMode,
            17);
        var aga17 = BuildPortableQuery(
            GraphicsDisplayDatabase.DtagDisp,
            superHiresMode,
            superHiresMode,
            17,
            supportsAgaDisplay: true);

        foreach (var portable in new[] { ocs17, ecs17, aga17 })
        {
            Assert.Equal(17u, portable.Result);
            Assert.Equal(17, portable.Bytes.Length);
            Assert.Equal((byte)0, portable.Bytes[16]);
        }
        Assert.Equal(ocs17.Bytes, ecs17.Bytes);
        Assert.Equal(ocs17.Bytes, aga17.Bytes);

        var ocs18 = BuildPortableQuery(
            GraphicsDisplayDatabase.DtagDisp,
            superHiresMode,
            superHiresMode,
            18,
            supportsEcsDisplay: false);
        Assert.Equal(18u, ocs18.Result);
        Assert.Equal(18, ocs18.Bytes.Length);
        Assert.Equal((byte)1, ocs18.Bytes[17]);
    }

    private static void AssertDisplayEighteenBytePortableSeam(
        uint ordinaryMode,
        uint superHiresMode)
    {
        var ordinaryOcs = BuildPortableQuery(
            GraphicsDisplayDatabase.DtagDisp,
            ordinaryMode,
            ordinaryMode,
            18,
            supportsEcsDisplay: false);
        var ordinaryEcs = BuildPortableQuery(
            GraphicsDisplayDatabase.DtagDisp,
            ordinaryMode,
            ordinaryMode,
            18);
        var ordinaryAga = BuildPortableQuery(
            GraphicsDisplayDatabase.DtagDisp,
            ordinaryMode,
            ordinaryMode,
            18,
            supportsAgaDisplay: true);
        foreach (var portable in new[] { ordinaryOcs, ordinaryEcs, ordinaryAga })
        {
            Assert.Equal(18u, portable.Result);
            Assert.Equal(18, portable.Bytes.Length);
            Assert.Equal((byte)0, portable.Bytes[16]);
            Assert.Equal((byte)0, portable.Bytes[17]);
        }
        Assert.Equal(ordinaryOcs.Bytes, ordinaryEcs.Bytes);
        Assert.Equal(ordinaryOcs.Bytes, ordinaryAga.Bytes);

        var superOcs = BuildPortableQuery(
            GraphicsDisplayDatabase.DtagDisp,
            superHiresMode,
            superHiresMode,
            18,
            supportsEcsDisplay: false);
        var superEcs = BuildPortableQuery(
            GraphicsDisplayDatabase.DtagDisp,
            superHiresMode,
            superHiresMode,
            18);
        var superAga = BuildPortableQuery(
            GraphicsDisplayDatabase.DtagDisp,
            superHiresMode,
            superHiresMode,
            18,
            supportsAgaDisplay: true);
        foreach (var portable in new[] { superOcs, superEcs, superAga })
        {
            Assert.Equal(18u, portable.Result);
            Assert.Equal(18, portable.Bytes.Length);
            Assert.Equal((byte)0, portable.Bytes[16]);
        }
        Assert.Equal((byte)1, superOcs.Bytes[17]);
        Assert.Equal((byte)0, superEcs.Bytes[17]);
        Assert.Equal((byte)0, superAga.Bytes[17]);
        Assert.Equal(superOcs.Bytes.Take(17), superEcs.Bytes.Take(17));
        Assert.Equal(superOcs.Bytes.Take(17), superAga.Bytes.Take(17));
    }

    private static void AssertDisplayNineteenBytePortableSeam(
        uint ordinaryMode,
        uint superHiresMode)
    {
        var ordinaryOcs = BuildPortableQuery(
            GraphicsDisplayDatabase.DtagDisp,
            ordinaryMode,
            ordinaryMode,
            19,
            supportsEcsDisplay: false);
        var ordinaryEcs = BuildPortableQuery(
            GraphicsDisplayDatabase.DtagDisp,
            ordinaryMode,
            ordinaryMode,
            19);
        var ordinaryAga = BuildPortableQuery(
            GraphicsDisplayDatabase.DtagDisp,
            ordinaryMode,
            ordinaryMode,
            19,
            supportsAgaDisplay: true);
        foreach (var portable in new[] { ordinaryOcs, ordinaryEcs, ordinaryAga })
        {
            Assert.Equal(19u, portable.Result);
            Assert.Equal(19, portable.Bytes.Length);
            Assert.Equal((byte)0, portable.Bytes[16]);
            Assert.Equal((byte)0, portable.Bytes[17]);
            Assert.Equal((byte)0, portable.Bytes[18]);
        }
        Assert.Equal(ordinaryOcs.Bytes, ordinaryEcs.Bytes);
        Assert.Equal(ordinaryOcs.Bytes, ordinaryAga.Bytes);

        var superOcs = BuildPortableQuery(
            GraphicsDisplayDatabase.DtagDisp,
            superHiresMode,
            superHiresMode,
            19,
            supportsEcsDisplay: false);
        var superEcs = BuildPortableQuery(
            GraphicsDisplayDatabase.DtagDisp,
            superHiresMode,
            superHiresMode,
            19);
        var superAga = BuildPortableQuery(
            GraphicsDisplayDatabase.DtagDisp,
            superHiresMode,
            superHiresMode,
            19,
            supportsAgaDisplay: true);
        foreach (var portable in new[] { superOcs, superEcs, superAga })
        {
            Assert.Equal(19u, portable.Result);
            Assert.Equal(19, portable.Bytes.Length);
            Assert.Equal((byte)0, portable.Bytes[16]);
            Assert.Equal((byte)0, portable.Bytes[18]);
        }
        Assert.Equal((byte)1, superOcs.Bytes[17]);
        Assert.Equal((byte)0, superEcs.Bytes[17]);
        Assert.Equal((byte)0, superAga.Bytes[17]);
        Assert.Equal(superOcs.Bytes.Take(17), superEcs.Bytes.Take(17));
        Assert.Equal(superOcs.Bytes.Take(17), superAga.Bytes.Take(17));

        var modeCount = 0;
        for (var mode = GraphicsDisplayDatabase.NextDisplayInfo(GraphicsModeIds.Invalid);
             mode != GraphicsModeIds.Invalid;
             mode = GraphicsDisplayDatabase.NextDisplayInfo(mode))
        {
            var ocs19 = BuildPortableQuery(
                GraphicsDisplayDatabase.DtagDisp,
                mode,
                mode,
                19,
                supportsEcsDisplay: false);
            var ecs19 = BuildPortableQuery(
                GraphicsDisplayDatabase.DtagDisp,
                mode,
                mode,
                19);
            var aga19 = BuildPortableQuery(
                GraphicsDisplayDatabase.DtagDisp,
                mode,
                mode,
                19,
                supportsAgaDisplay: true);
            foreach (var portable in new[] { ocs19, ecs19, aga19 })
            {
                Assert.Equal(19u, portable.Result);
                Assert.Equal(19, portable.Bytes.Length);
                Assert.Equal((byte)0, portable.Bytes[18]);
            }

            var ocs20 = BuildPortableQuery(
                GraphicsDisplayDatabase.DtagDisp,
                mode,
                mode,
                20,
                supportsEcsDisplay: false);
            var ecs20 = BuildPortableQuery(
                GraphicsDisplayDatabase.DtagDisp,
                mode,
                mode,
                20);
            var aga20 = BuildPortableQuery(
                GraphicsDisplayDatabase.DtagDisp,
                mode,
                mode,
                20,
                supportsAgaDisplay: true);
            foreach (var portable in new[] { ocs20, ecs20, aga20 })
            {
                Assert.Equal(20u, portable.Result);
                Assert.Equal(20, portable.Bytes.Length);
                Assert.Equal((byte)0, portable.Bytes[18]);
            }
            Assert.Equal((byte)0x10, ocs20.Bytes[19]);
            Assert.Equal((byte)0x10, ecs20.Bytes[19]);
            Assert.Equal((byte)0x11, aga20.Bytes[19]);
            modeCount++;
            Assert.InRange(modeCount, 1, GraphicsDisplayDatabase.NativeDatabaseRecordCount);
        }
        Assert.Equal(GraphicsDisplayDatabase.NativeDatabaseRecordCount, modeCount);
        Assert.Equal(66, modeCount);
    }

    private static void AssertDisplayTwentyBytePortableSeam(
        uint ordinaryMode,
        uint superHiresMode)
    {
        var profiles = new[]
        {
            (Ecs: false, Aga: false, Last: (byte)0x10), // OCS / Alice / Lisa
            (Ecs: true, Aga: false, Last: (byte)0x10),  // complete ECS
            (Ecs: false, Aga: true, Last: (byte)0x11),  // AA pair without ECS
            (Ecs: true, Aga: true, Last: (byte)0x11)    // complete SetAA
        };

        foreach (var profile in profiles)
        {
            var ordinary = BuildPortableQuery(
                GraphicsDisplayDatabase.DtagDisp,
                ordinaryMode,
                ordinaryMode,
                20,
                profile.Ecs,
                profile.Aga);
            Assert.Equal(20u, ordinary.Result);
            Assert.Equal(20, ordinary.Bytes.Length);
            Assert.Equal(new byte[] { 0, 0, 0, profile.Last }, ordinary.Bytes[16..20]);

            var superHires = BuildPortableQuery(
                GraphicsDisplayDatabase.DtagDisp,
                superHiresMode,
                superHiresMode,
                20,
                profile.Ecs,
                profile.Aga);
            Assert.Equal(20u, superHires.Result);
            Assert.Equal(20, superHires.Bytes.Length);
            Assert.Equal(
                new byte[]
                {
                    0,
                    profile.Ecs ? (byte)0 : (byte)1,
                    0,
                    profile.Last
                },
                superHires.Bytes[16..20]);
        }

        var aaPairWithoutEcs = BuildPortableQuery(
            GraphicsDisplayDatabase.DtagDisp,
            superHiresMode,
            superHiresMode,
            20,
            supportsEcsDisplay: false,
            supportsAgaDisplay: true);
        Assert.Equal(new byte[] { 0, 1, 0, 0x11 }, aaPairWithoutEcs.Bytes[16..20]);
    }

    private static void AssertDisplayTwentyOneBytePortableSeam(
        uint ordinaryMode,
        uint ehbMode,
        uint superHiresMode)
    {
        var profiles = new[]
        {
            (Ecs: false, Aga: false, Prior: (byte)0x10),
            (Ecs: true, Aga: false, Prior: (byte)0x10),
            (Ecs: false, Aga: true, Prior: (byte)0x11),
            (Ecs: true, Aga: true, Prior: (byte)0x11)
        };

        foreach (var profile in profiles)
        {
            var ordinary = BuildPortableQuery(
                GraphicsDisplayDatabase.DtagDisp,
                ordinaryMode,
                ordinaryMode,
                21,
                profile.Ecs,
                profile.Aga);
            Assert.Equal(new byte[] { 0, 0, 0, profile.Prior, 0x21 }, ordinary.Bytes[16..21]);

            var ehb = BuildPortableQuery(
                GraphicsDisplayDatabase.DtagDisp,
                ehbMode,
                ehbMode,
                21,
                profile.Ecs,
                profile.Aga);
            Assert.Equal(new byte[] { 0, 0, 0, profile.Prior, 0x31 }, ehb.Bytes[16..21]);

            var superHires = BuildPortableQuery(
                GraphicsDisplayDatabase.DtagDisp,
                superHiresMode,
                superHiresMode,
                21,
                profile.Ecs,
                profile.Aga);
            Assert.Equal(
                new byte[]
                {
                    0,
                    profile.Ecs ? (byte)0 : (byte)1,
                    0,
                    profile.Prior,
                    0x01
                },
                superHires.Bytes[16..21]);
        }
    }

    private static byte ExpectedDisplayByte15(uint modeId)
    {
        var monitorFamily = modeId & 0xFFFF_0000u;
        var value = 0x40;
        if (monitorFamily is 0 or 0x0002_0000u)
            value += 0x20;
        if ((modeId & GraphicsModeIds.SuperHiresMode) != 0)
            value += 0x10;
        if ((modeId & GraphicsModeIds.HamMode) != 0)
            value += 0x08;
        if ((modeId & GraphicsModeIds.PlayfieldBitAssignment) != 0)
            value += 0x04;
        if ((modeId & GraphicsModeIds.DualPlayfieldMode) != 0)
            value += 0x02;
        if ((modeId & GraphicsModeIds.InterlaceMode) != 0)
            value += 0x01;
        return checked((byte)value);
    }

    private static void AssertDisplayTwentyTwoBytePortableSeam()
    {
        var profiles = new[]
        {
            (Ecs: false, Aga: false),
            (Ecs: true, Aga: false),
            (Ecs: false, Aga: true),
            (Ecs: true, Aga: true)
        };
        var valueClasses = new HashSet<byte>();
        var modeCount = 0;
        for (var mode = GraphicsDisplayDatabase.NextDisplayInfo(GraphicsModeIds.Invalid);
             mode != GraphicsModeIds.Invalid;
             mode = GraphicsDisplayDatabase.NextDisplayInfo(mode))
        {
            var expectedByte15 = ExpectedDisplayByte15(mode);
            valueClasses.Add(expectedByte15);
            foreach (var profile in profiles)
            {
                var portable = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    22,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(22u, portable.Result);
                Assert.Equal(22, portable.Bytes.Length);
                Assert.Equal(expectedByte15, portable.Bytes[21]);
                Assert.Equal(profile.Aga ? (byte)0x11 : (byte)0x10, portable.Bytes[19]);
                Assert.Equal(
                    (mode & GraphicsModeIds.SuperHiresMode) != 0 && !profile.Ecs ? (byte)1 : (byte)0,
                    portable.Bytes[17]);
            }
            modeCount++;
        }

        Assert.Equal(66, modeCount);
        Assert.Equal(28, valueClasses.Count);

        var ntscOrdinary = GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresKey;
        var ntscEhb = GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteKey;
        Assert.Equal(ExpectedDisplayByte15(ntscOrdinary), ExpectedDisplayByte15(ntscEhb));
        var aaIndependent = BuildPortableQuery(
            GraphicsDisplayDatabase.DtagDisp,
            ntscOrdinary,
            ntscOrdinary,
            22,
            supportsEcsDisplay: false,
            supportsAgaDisplay: true);
        var ocsIndependent = BuildPortableQuery(
            GraphicsDisplayDatabase.DtagDisp,
            ntscOrdinary,
            ntscOrdinary,
            22,
            supportsEcsDisplay: false,
            supportsAgaDisplay: false);
        Assert.Equal(ocsIndependent.Bytes[21], aaIndependent.Bytes[21]);
        Assert.NotEqual(ocsIndependent.Bytes[19], aaIndependent.Bytes[19]);
    }

    private static void AssertDisplayTwentyThreeBytePortableSeam()
    {
        var profiles = new[]
        {
            (Ecs: false, Aga: false),
            (Ecs: true, Aga: false),
            (Ecs: false, Aga: true),
            (Ecs: true, Aga: true)
        };
        var byte22Classes = new HashSet<byte>();
        var modeCount = 0;
        for (var mode = GraphicsDisplayDatabase.NextDisplayInfo(GraphicsModeIds.Invalid);
             mode != GraphicsModeIds.Invalid;
             mode = GraphicsDisplayDatabase.NextDisplayInfo(mode))
        {
            var superHires = (mode & GraphicsModeIds.SuperHiresMode) != 0;
            var ehb = (mode & GraphicsModeIds.ExtraHalfBriteMode) != 0;
            foreach (var profile in profiles)
            {
                var portable = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    23,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(23u, portable.Result);
                Assert.Equal(23, portable.Bytes.Length);
                Assert.Equal((byte)0, portable.Bytes[16]);
                Assert.Equal(superHires && !profile.Ecs ? (byte)1 : (byte)0, portable.Bytes[17]);
                Assert.Equal((byte)0, portable.Bytes[18]);
                Assert.Equal(profile.Aga ? (byte)0x11 : (byte)0x10, portable.Bytes[19]);
                Assert.Equal(superHires ? (byte)0x01 : ehb ? (byte)0x31 : (byte)0x21, portable.Bytes[20]);
                Assert.Equal(ExpectedDisplayByte15(mode), portable.Bytes[21]);
                Assert.Equal((byte)0, portable.Bytes[22]);
                byte22Classes.Add(portable.Bytes[22]);
            }
            modeCount++;
        }

        Assert.Equal(66, modeCount);
        Assert.Equal(new byte[] { 0 }, byte22Classes.OrderBy(value => value));
    }

    private static byte ExpectedDisplayByte17(uint modeId)
    {
        if ((modeId & GraphicsModeIds.SuperHiresMode) != 0)
            return 1;
        return (modeId & GraphicsModeIds.HiresMode) != 0 ? (byte)2 : (byte)4;
    }

    private static void AssertDisplayTwentyFourBytePortableSeam()
    {
        var profiles = new[]
        {
            (Ecs: false, Aga: false),
            (Ecs: true, Aga: false),
            (Ecs: false, Aga: true),
            (Ecs: true, Aga: true)
        };
        var byte23ClassCounts = new Dictionary<byte, int>();
        var modeCount = 0;
        for (var mode = GraphicsDisplayDatabase.NextDisplayInfo(GraphicsModeIds.Invalid);
             mode != GraphicsModeIds.Invalid;
             mode = GraphicsDisplayDatabase.NextDisplayInfo(mode))
        {
            var superHires = (mode & GraphicsModeIds.SuperHiresMode) != 0;
            var ehb = (mode & GraphicsModeIds.ExtraHalfBriteMode) != 0;
            var expectedByte23 = ExpectedDisplayByte17(mode);
            byte23ClassCounts[expectedByte23] =
                byte23ClassCounts.GetValueOrDefault(expectedByte23) + 1;
            foreach (var profile in profiles)
            {
                var portable = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    24,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(24u, portable.Result);
                Assert.Equal(24, portable.Bytes.Length);
                var prior = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    23,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(prior.Bytes, portable.Bytes.Take(23));
                Assert.Equal((byte)0, portable.Bytes[16]);
                Assert.Equal(superHires && !profile.Ecs ? (byte)1 : (byte)0, portable.Bytes[17]);
                Assert.Equal((byte)0, portable.Bytes[18]);
                Assert.Equal(profile.Aga ? (byte)0x11 : (byte)0x10, portable.Bytes[19]);
                Assert.Equal(superHires ? (byte)0x01 : ehb ? (byte)0x31 : (byte)0x21, portable.Bytes[20]);
                Assert.Equal(ExpectedDisplayByte15(mode), portable.Bytes[21]);
                Assert.Equal((byte)0, portable.Bytes[22]);
                Assert.Equal(expectedByte23, portable.Bytes[23]);
            }
            modeCount++;
        }

        Assert.Equal(66, modeCount);
        Assert.Equal(3, byte23ClassCounts.Count);
        Assert.Equal(18, byte23ClassCounts[(byte)1]);
        Assert.Equal(18, byte23ClassCounts[(byte)2]);
        Assert.Equal(30, byte23ClassCounts[(byte)4]);
    }

    private static void AssertDisplayTwentyFiveBytePortableSeam()
    {
        var profiles = new[]
        {
            (Ecs: false, Aga: false),
            (Ecs: true, Aga: false),
            (Ecs: false, Aga: true),
            (Ecs: true, Aga: true)
        };
        var byte24Classes = new HashSet<byte>();
        var modeCount = 0;
        for (var mode = GraphicsDisplayDatabase.NextDisplayInfo(GraphicsModeIds.Invalid);
             mode != GraphicsModeIds.Invalid;
             mode = GraphicsDisplayDatabase.NextDisplayInfo(mode))
        {
            foreach (var profile in profiles)
            {
                var portable = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    25,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(25u, portable.Result);
                Assert.Equal(25, portable.Bytes.Length);
                var prior = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    24,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(prior.Bytes, portable.Bytes.Take(24));
                Assert.Equal((byte)0, portable.Bytes[24]);
                byte24Classes.Add(portable.Bytes[24]);
            }
            modeCount++;
        }

        Assert.Equal(66, modeCount);
        Assert.Equal(new byte[] { 0 }, byte24Classes.OrderBy(value => value));
    }

    private static void AssertDisplayTwentySixBytePortableSeam()
    {
        var profiles = new[]
        {
            (Ecs: false, Aga: false),
            (Ecs: true, Aga: false),
            (Ecs: false, Aga: true),
            (Ecs: true, Aga: true)
        };
        var byte25Classes = new HashSet<byte>();
        var modeCount = 0;
        for (var mode = GraphicsDisplayDatabase.NextDisplayInfo(GraphicsModeIds.Invalid);
             mode != GraphicsModeIds.Invalid;
             mode = GraphicsDisplayDatabase.NextDisplayInfo(mode))
        {
            foreach (var profile in profiles)
            {
                var portable = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    26,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(26u, portable.Result);
                Assert.Equal(26, portable.Bytes.Length);
                var prior = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    25,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(prior.Bytes, portable.Bytes.Take(25));
                var full = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    56,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(48u, full.Result);
                Assert.Equal(48, full.Bytes.Length);
                Assert.Equal(portable.Bytes, full.Bytes.Take(26));
                Assert.Equal((byte)1, full.Bytes[25]);
                Assert.Equal((byte)1, portable.Bytes[25]);
                byte25Classes.Add(portable.Bytes[25]);
            }
            modeCount++;
        }

        Assert.Equal(66, modeCount);
        Assert.Equal(new byte[] { 1 }, byte25Classes.OrderBy(value => value));
    }

    private static ushort ExpectedDisplayPixelSpeed(uint modeId)
        => ExpectedDisplayByte17(modeId) switch
        {
            1 => 0x0023,
            2 => 0x0046,
            4 => 0x008C,
            _ => throw new ArgumentOutOfRangeException(nameof(modeId))
        };

    private static ushort ExpectedDisplayPaletteRange(uint modeId, bool supportsAgaDisplay)
        => supportsAgaDisplay
            ? (ushort)0xFFFF
            : (modeId & GraphicsModeIds.SuperHiresMode) != 0 &&
              (modeId & GraphicsModeIds.InterlaceMode) == 0
                ? (ushort)0x0040
                : (ushort)0x1000;

    private static ushort ExpectedDisplaySpriteResolution(uint modeId)
        => (modeId & GraphicsModeIds.SuperHiresMode) != 0
            ? (ushort)2
            : (ushort)4;

    private static void AssertDisplayTwentySevenBytePortableSeam()
    {
        var profiles = new[]
        {
            (Ecs: false, Aga: false),
            (Ecs: true, Aga: false),
            (Ecs: false, Aga: true),
            (Ecs: true, Aga: true)
        };
        var byte26Classes = new HashSet<byte>();
        var pixelSpeedModeCounts = new Dictionary<ushort, int>();
        var pixelSpeedProfilePairCounts = new Dictionary<ushort, int>();
        var modeCount = 0;
        for (var mode = GraphicsDisplayDatabase.NextDisplayInfo(GraphicsModeIds.Invalid);
             mode != GraphicsModeIds.Invalid;
             mode = GraphicsDisplayDatabase.NextDisplayInfo(mode))
        {
            var expectedPixelSpeed = ExpectedDisplayPixelSpeed(mode);
            pixelSpeedModeCounts[expectedPixelSpeed] =
                pixelSpeedModeCounts.GetValueOrDefault(expectedPixelSpeed) + 1;
            foreach (var profile in profiles)
            {
                var portable = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    27,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(27u, portable.Result);
                Assert.Equal(27, portable.Bytes.Length);
                var prior = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    26,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(prior.Bytes, portable.Bytes.Take(26));
                var full = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    56,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(48u, full.Result);
                Assert.Equal(48, full.Bytes.Length);
                Assert.Equal(portable.Bytes, full.Bytes.Take(27));
                Assert.Equal((byte)0, portable.Bytes[26]);
                Assert.Equal((byte)0, full.Bytes[26]);
                var pixelSpeed = checked((ushort)((full.Bytes[26] << 8) | full.Bytes[27]));
                Assert.Equal(expectedPixelSpeed, pixelSpeed);
                byte26Classes.Add(portable.Bytes[26]);
                pixelSpeedProfilePairCounts[pixelSpeed] =
                    pixelSpeedProfilePairCounts.GetValueOrDefault(pixelSpeed) + 1;
            }
            modeCount++;
        }

        Assert.Equal(66, modeCount);
        Assert.Equal(new byte[] { 0 }, byte26Classes.OrderBy(value => value));
        Assert.Equal(3, pixelSpeedModeCounts.Count);
        Assert.Equal(18, pixelSpeedModeCounts[0x0023]);
        Assert.Equal(18, pixelSpeedModeCounts[0x0046]);
        Assert.Equal(30, pixelSpeedModeCounts[0x008C]);
        Assert.Equal(3, pixelSpeedProfilePairCounts.Count);
        Assert.Equal(72, pixelSpeedProfilePairCounts[0x0023]);
        Assert.Equal(72, pixelSpeedProfilePairCounts[0x0046]);
        Assert.Equal(120, pixelSpeedProfilePairCounts[0x008C]);
    }

    private static void AssertDisplayTwentyEightBytePortableSeam()
    {
        var profiles = new[]
        {
            (Ecs: false, Aga: false),
            (Ecs: true, Aga: false),
            (Ecs: false, Aga: true),
            (Ecs: true, Aga: true)
        };
        var byte27Classes = new HashSet<byte>();
        var pixelSpeedModeCounts = new Dictionary<ushort, int>();
        var pixelSpeedProfilePairCounts = new Dictionary<ushort, int>();
        var modeCount = 0;
        for (var mode = GraphicsDisplayDatabase.NextDisplayInfo(GraphicsModeIds.Invalid);
             mode != GraphicsModeIds.Invalid;
             mode = GraphicsDisplayDatabase.NextDisplayInfo(mode))
        {
            var expectedPixelSpeed = ExpectedDisplayPixelSpeed(mode);
            pixelSpeedModeCounts[expectedPixelSpeed] =
                pixelSpeedModeCounts.GetValueOrDefault(expectedPixelSpeed) + 1;
            foreach (var profile in profiles)
            {
                var portable = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    28,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(28u, portable.Result);
                Assert.Equal(28, portable.Bytes.Length);
                var prior = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    27,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(prior.Bytes, portable.Bytes.Take(27));
                var full = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    56,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(48u, full.Result);
                Assert.Equal(48, full.Bytes.Length);
                Assert.Equal(portable.Bytes, full.Bytes.Take(28));
                var pixelSpeed = checked((ushort)((portable.Bytes[26] << 8) | portable.Bytes[27]));
                Assert.Equal(expectedPixelSpeed, pixelSpeed);
                Assert.Equal((byte)0, portable.Bytes[26]);
                Assert.Equal((byte)(expectedPixelSpeed & 0x00FF), portable.Bytes[27]);
                byte27Classes.Add(portable.Bytes[27]);
                pixelSpeedProfilePairCounts[pixelSpeed] =
                    pixelSpeedProfilePairCounts.GetValueOrDefault(pixelSpeed) + 1;
            }
            modeCount++;
        }

        Assert.Equal(66, modeCount);
        Assert.Equal(new byte[] { 0x23, 0x46, 0x8C }, byte27Classes.OrderBy(value => value));
        Assert.Equal(3, pixelSpeedModeCounts.Count);
        Assert.Equal(18, pixelSpeedModeCounts[0x0023]);
        Assert.Equal(18, pixelSpeedModeCounts[0x0046]);
        Assert.Equal(30, pixelSpeedModeCounts[0x008C]);
        Assert.Equal(3, pixelSpeedProfilePairCounts.Count);
        Assert.Equal(72, pixelSpeedProfilePairCounts[0x0023]);
        Assert.Equal(72, pixelSpeedProfilePairCounts[0x0046]);
        Assert.Equal(120, pixelSpeedProfilePairCounts[0x008C]);
    }

    private static void AssertDisplayTwentyNineBytePortableSeam()
    {
        const ushort expectedNumStdSprites = 0x0008;
        var profiles = new[]
        {
            (Ecs: false, Aga: false),
            (Ecs: true, Aga: false),
            (Ecs: false, Aga: true),
            (Ecs: true, Aga: true)
        };
        var byte28Classes = new HashSet<byte>();
        var numStdSpritesModeCounts = new Dictionary<ushort, int>();
        var numStdSpritesProfilePairCounts = new Dictionary<ushort, int>();
        var modeCount = 0;
        for (var mode = GraphicsDisplayDatabase.NextDisplayInfo(GraphicsModeIds.Invalid);
             mode != GraphicsModeIds.Invalid;
             mode = GraphicsDisplayDatabase.NextDisplayInfo(mode))
        {
            numStdSpritesModeCounts[expectedNumStdSprites] =
                numStdSpritesModeCounts.GetValueOrDefault(expectedNumStdSprites) + 1;
            foreach (var profile in profiles)
            {
                var portable = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    29,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(29u, portable.Result);
                Assert.Equal(29, portable.Bytes.Length);
                var prior = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    28,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(28u, prior.Result);
                Assert.Equal(28, prior.Bytes.Length);
                Assert.Equal(prior.Bytes, portable.Bytes.Take(28));
                var full = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    56,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(48u, full.Result);
                Assert.Equal(48, full.Bytes.Length);
                Assert.Equal(portable.Bytes, full.Bytes.Take(29));
                Assert.Equal((byte)0, portable.Bytes[28]);
                Assert.Equal((byte)0, full.Bytes[28]);
                var numStdSprites = checked((ushort)((full.Bytes[28] << 8) | full.Bytes[29]));
                Assert.Equal(expectedNumStdSprites, numStdSprites);
                byte28Classes.Add(portable.Bytes[28]);
                numStdSpritesProfilePairCounts[numStdSprites] =
                    numStdSpritesProfilePairCounts.GetValueOrDefault(numStdSprites) + 1;
            }
            modeCount++;
        }

        Assert.Equal(66, modeCount);
        Assert.Equal(new byte[] { 0 }, byte28Classes.OrderBy(value => value));
        Assert.Single(numStdSpritesModeCounts);
        Assert.Equal(66, numStdSpritesModeCounts[expectedNumStdSprites]);
        Assert.Single(numStdSpritesProfilePairCounts);
        Assert.Equal(264, numStdSpritesProfilePairCounts[expectedNumStdSprites]);
    }

    private static void AssertDisplayThirtyBytePortableSeam()
    {
        const ushort expectedNumStdSprites = 0x0008;
        var profiles = new[]
        {
            (Ecs: false, Aga: false),
            (Ecs: true, Aga: false),
            (Ecs: false, Aga: true),
            (Ecs: true, Aga: true)
        };
        var byte29Classes = new HashSet<byte>();
        var numStdSpritesModeCounts = new Dictionary<ushort, int>();
        var numStdSpritesProfilePairCounts = new Dictionary<ushort, int>();
        var modeCount = 0;
        for (var mode = GraphicsDisplayDatabase.NextDisplayInfo(GraphicsModeIds.Invalid);
             mode != GraphicsModeIds.Invalid;
             mode = GraphicsDisplayDatabase.NextDisplayInfo(mode))
        {
            numStdSpritesModeCounts[expectedNumStdSprites] =
                numStdSpritesModeCounts.GetValueOrDefault(expectedNumStdSprites) + 1;
            foreach (var profile in profiles)
            {
                var portable = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    30,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(30u, portable.Result);
                Assert.Equal(30, portable.Bytes.Length);
                var prior = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    29,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(29u, prior.Result);
                Assert.Equal(29, prior.Bytes.Length);
                Assert.Equal(prior.Bytes, portable.Bytes.Take(29));
                var full = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    56,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(48u, full.Result);
                Assert.Equal(48, full.Bytes.Length);
                Assert.Equal(portable.Bytes, full.Bytes.Take(30));
                Assert.Equal((byte)0, portable.Bytes[28]);
                Assert.Equal((byte)8, portable.Bytes[29]);
                Assert.Equal((byte)0, full.Bytes[28]);
                Assert.Equal((byte)8, full.Bytes[29]);
                var numStdSprites = checked((ushort)((full.Bytes[28] << 8) | full.Bytes[29]));
                Assert.Equal(expectedNumStdSprites, numStdSprites);
                byte29Classes.Add(portable.Bytes[29]);
                numStdSpritesProfilePairCounts[numStdSprites] =
                    numStdSpritesProfilePairCounts.GetValueOrDefault(numStdSprites) + 1;
            }
            modeCount++;
        }

        Assert.Equal(66, modeCount);
        Assert.Equal(new byte[] { 8 }, byte29Classes.OrderBy(value => value));
        Assert.Single(numStdSpritesModeCounts);
        Assert.Equal(66, numStdSpritesModeCounts[expectedNumStdSprites]);
        Assert.Single(numStdSpritesProfilePairCounts);
        Assert.Equal(264, numStdSpritesProfilePairCounts[expectedNumStdSprites]);
    }

    private static void AssertDisplayThirtyOneBytePortableSeam()
    {
        var profiles = new[]
        {
            (Ecs: false, Aga: false),
            (Ecs: true, Aga: false),
            (Ecs: false, Aga: true),
            (Ecs: true, Aga: true)
        };
        var byte30ClassCounts = new Dictionary<byte, int>();
        var paletteRangeProfilePairCounts = new Dictionary<ushort, int>();
        var modeCount = 0;
        for (var mode = GraphicsDisplayDatabase.NextDisplayInfo(GraphicsModeIds.Invalid);
             mode != GraphicsModeIds.Invalid;
             mode = GraphicsDisplayDatabase.NextDisplayInfo(mode))
        {
            foreach (var profile in profiles)
            {
                var prior = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    30,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(30u, prior.Result);
                Assert.Equal(30, prior.Bytes.Length);
                var portable = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    31,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(31u, portable.Result);
                Assert.Equal(31, portable.Bytes.Length);
                Assert.Equal(prior.Bytes, portable.Bytes.Take(30));
                var next = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    32,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(32u, next.Result);
                Assert.Equal(32, next.Bytes.Length);
                Assert.Equal(portable.Bytes, next.Bytes.Take(31));
                var full = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    56,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(48u, full.Result);
                Assert.Equal(48, full.Bytes.Length);
                Assert.Equal(portable.Bytes, full.Bytes.Take(31));
                Assert.Equal(next.Bytes, full.Bytes.Take(32));

                var expectedPaletteRange = ExpectedDisplayPaletteRange(mode, profile.Aga);
                var paletteRange = checked((ushort)((next.Bytes[30] << 8) | next.Bytes[31]));
                var fullPaletteRange = checked((ushort)((full.Bytes[30] << 8) | full.Bytes[31]));
                Assert.Equal((byte)(expectedPaletteRange >> 8), portable.Bytes[30]);
                Assert.Equal(expectedPaletteRange, paletteRange);
                Assert.Equal(expectedPaletteRange, fullPaletteRange);
                byte30ClassCounts[portable.Bytes[30]] =
                    byte30ClassCounts.GetValueOrDefault(portable.Bytes[30]) + 1;
                paletteRangeProfilePairCounts[paletteRange] =
                    paletteRangeProfilePairCounts.GetValueOrDefault(paletteRange) + 1;
            }
            modeCount++;
        }

        Assert.Equal(66, modeCount);
        Assert.Equal(3, byte30ClassCounts.Count);
        Assert.Equal(18, byte30ClassCounts[0x00]);
        Assert.Equal(114, byte30ClassCounts[0x10]);
        Assert.Equal(132, byte30ClassCounts[0xFF]);
        Assert.Equal(3, paletteRangeProfilePairCounts.Count);
        Assert.Equal(18, paletteRangeProfilePairCounts[0x0040]);
        Assert.Equal(114, paletteRangeProfilePairCounts[0x1000]);
        Assert.Equal(132, paletteRangeProfilePairCounts[0xFFFF]);
    }

    private static void AssertDisplayThirtyTwoBytePortableSeam()
    {
        var profiles = new[]
        {
            (Ecs: false, Aga: false),
            (Ecs: true, Aga: false),
            (Ecs: false, Aga: true),
            (Ecs: true, Aga: true)
        };
        var byte31ClassCounts = new Dictionary<byte, int>();
        var paletteRangeProfilePairCounts = new Dictionary<ushort, int>();
        var modeCount = 0;
        for (var mode = GraphicsDisplayDatabase.NextDisplayInfo(GraphicsModeIds.Invalid);
             mode != GraphicsModeIds.Invalid;
             mode = GraphicsDisplayDatabase.NextDisplayInfo(mode))
        {
            foreach (var profile in profiles)
            {
                var prior = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    31,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(31u, prior.Result);
                Assert.Equal(31, prior.Bytes.Length);
                var portable = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    32,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(32u, portable.Result);
                Assert.Equal(32, portable.Bytes.Length);
                Assert.Equal(prior.Bytes, portable.Bytes.Take(31));
                var next = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    33,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(33u, next.Result);
                Assert.Equal(33, next.Bytes.Length);
                Assert.Equal(portable.Bytes, next.Bytes.Take(32));
                var full = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    56,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(48u, full.Result);
                Assert.Equal(48, full.Bytes.Length);
                Assert.Equal(prior.Bytes, full.Bytes.Take(31));
                Assert.Equal(portable.Bytes, full.Bytes.Take(32));
                Assert.Equal(next.Bytes, full.Bytes.Take(33));

                var expectedPaletteRange = ExpectedDisplayPaletteRange(mode, profile.Aga);
                var paletteRange = checked((ushort)((portable.Bytes[30] << 8) | portable.Bytes[31]));
                var nextPaletteRange = checked((ushort)((next.Bytes[30] << 8) | next.Bytes[31]));
                var fullPaletteRange = checked((ushort)((full.Bytes[30] << 8) | full.Bytes[31]));
                Assert.Equal((byte)(expectedPaletteRange & 0x00FF), portable.Bytes[31]);
                Assert.Equal(expectedPaletteRange, paletteRange);
                Assert.Equal(expectedPaletteRange, nextPaletteRange);
                Assert.Equal(expectedPaletteRange, fullPaletteRange);
                byte31ClassCounts[portable.Bytes[31]] =
                    byte31ClassCounts.GetValueOrDefault(portable.Bytes[31]) + 1;
                paletteRangeProfilePairCounts[paletteRange] =
                    paletteRangeProfilePairCounts.GetValueOrDefault(paletteRange) + 1;
            }
            modeCount++;
        }

        Assert.Equal(66, modeCount);
        Assert.Equal(3, byte31ClassCounts.Count);
        Assert.Equal(18, byte31ClassCounts[0x40]);
        Assert.Equal(114, byte31ClassCounts[0x00]);
        Assert.Equal(132, byte31ClassCounts[0xFF]);
        Assert.Equal(3, paletteRangeProfilePairCounts.Count);
        Assert.Equal(18, paletteRangeProfilePairCounts[0x0040]);
        Assert.Equal(114, paletteRangeProfilePairCounts[0x1000]);
        Assert.Equal(132, paletteRangeProfilePairCounts[0xFFFF]);
    }

    private static void AssertDisplayThirtyThreeBytePortableSeam()
    {
        var profiles = new[]
        {
            (Ecs: false, Aga: false),
            (Ecs: true, Aga: false),
            (Ecs: false, Aga: true),
            (Ecs: true, Aga: true)
        };
        var byte32ClassCounts = new Dictionary<byte, int>();
        var spriteResolutionModeCounts = new Dictionary<ushort, int>();
        var spriteResolutionProfilePairCounts = new Dictionary<ushort, int>();
        var modeCount = 0;
        for (var mode = GraphicsDisplayDatabase.NextDisplayInfo(GraphicsModeIds.Invalid);
             mode != GraphicsModeIds.Invalid;
             mode = GraphicsDisplayDatabase.NextDisplayInfo(mode))
        {
            var expectedSpriteResolution = ExpectedDisplaySpriteResolution(mode);
            spriteResolutionModeCounts[expectedSpriteResolution] =
                spriteResolutionModeCounts.GetValueOrDefault(expectedSpriteResolution) + 1;
            foreach (var profile in profiles)
            {
                var prior = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    32,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(32u, prior.Result);
                Assert.Equal(32, prior.Bytes.Length);
                var portable = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    33,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(33u, portable.Result);
                Assert.Equal(33, portable.Bytes.Length);
                Assert.Equal(prior.Bytes, portable.Bytes.Take(32));
                var next = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    34,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(34u, next.Result);
                Assert.Equal(34, next.Bytes.Length);
                Assert.Equal(portable.Bytes, next.Bytes.Take(33));
                var full = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    56,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(48u, full.Result);
                Assert.Equal(48, full.Bytes.Length);
                Assert.Equal(prior.Bytes, full.Bytes.Take(32));
                Assert.Equal(portable.Bytes, full.Bytes.Take(33));
                Assert.Equal(next.Bytes, full.Bytes.Take(34));

                var spriteResolution = checked((ushort)((next.Bytes[32] << 8) | next.Bytes[33]));
                var fullSpriteResolution = checked((ushort)((full.Bytes[32] << 8) | full.Bytes[33]));
                Assert.Equal((byte)0, portable.Bytes[32]);
                Assert.Equal(expectedSpriteResolution, spriteResolution);
                Assert.Equal(expectedSpriteResolution, fullSpriteResolution);
                byte32ClassCounts[portable.Bytes[32]] =
                    byte32ClassCounts.GetValueOrDefault(portable.Bytes[32]) + 1;
                spriteResolutionProfilePairCounts[spriteResolution] =
                    spriteResolutionProfilePairCounts.GetValueOrDefault(spriteResolution) + 1;
            }
            modeCount++;
        }

        Assert.Equal(66, modeCount);
        Assert.Single(byte32ClassCounts);
        Assert.Equal(264, byte32ClassCounts[0]);
        Assert.Equal(2, spriteResolutionModeCounts.Count);
        Assert.Equal(18, spriteResolutionModeCounts[0x0002]);
        Assert.Equal(48, spriteResolutionModeCounts[0x0004]);
        Assert.Equal(2, spriteResolutionProfilePairCounts.Count);
        Assert.Equal(72, spriteResolutionProfilePairCounts[0x0002]);
        Assert.Equal(192, spriteResolutionProfilePairCounts[0x0004]);
    }

    private static void AssertDisplayThirtyFourBytePortableSeam()
    {
        var profiles = new[]
        {
            (Ecs: false, Aga: false),
            (Ecs: true, Aga: false),
            (Ecs: false, Aga: true),
            (Ecs: true, Aga: true)
        };
        var byte33ClassCounts = new Dictionary<byte, int>();
        var spriteResolutionModeCounts = new Dictionary<ushort, int>();
        var spriteResolutionProfilePairCounts = new Dictionary<ushort, int>();
        var modeCount = 0;
        for (var mode = GraphicsDisplayDatabase.NextDisplayInfo(GraphicsModeIds.Invalid);
             mode != GraphicsModeIds.Invalid;
             mode = GraphicsDisplayDatabase.NextDisplayInfo(mode))
        {
            var expectedSpriteResolution = ExpectedDisplaySpriteResolution(mode);
            spriteResolutionModeCounts[expectedSpriteResolution] =
                spriteResolutionModeCounts.GetValueOrDefault(expectedSpriteResolution) + 1;
            foreach (var profile in profiles)
            {
                var prior = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    33,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(33u, prior.Result);
                Assert.Equal(33, prior.Bytes.Length);
                var portable = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    34,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(34u, portable.Result);
                Assert.Equal(34, portable.Bytes.Length);
                Assert.Equal(prior.Bytes, portable.Bytes.Take(33));
                var next = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    35,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(35u, next.Result);
                Assert.Equal(35, next.Bytes.Length);
                Assert.Equal(portable.Bytes, next.Bytes.Take(34));
                var full = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    56,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(48u, full.Result);
                Assert.Equal(48, full.Bytes.Length);
                Assert.Equal(prior.Bytes, full.Bytes.Take(33));
                Assert.Equal(portable.Bytes, full.Bytes.Take(34));
                Assert.Equal(next.Bytes, full.Bytes.Take(35));

                var spriteResolution = checked((ushort)((portable.Bytes[32] << 8) | portable.Bytes[33]));
                var nextSpriteResolution = checked((ushort)((next.Bytes[32] << 8) | next.Bytes[33]));
                var fullSpriteResolution = checked((ushort)((full.Bytes[32] << 8) | full.Bytes[33]));
                Assert.Equal((byte)(expectedSpriteResolution & 0x00FF), portable.Bytes[33]);
                Assert.Equal(expectedSpriteResolution, spriteResolution);
                Assert.Equal(expectedSpriteResolution, nextSpriteResolution);
                Assert.Equal(expectedSpriteResolution, fullSpriteResolution);
                byte33ClassCounts[portable.Bytes[33]] =
                    byte33ClassCounts.GetValueOrDefault(portable.Bytes[33]) + 1;
                spriteResolutionProfilePairCounts[spriteResolution] =
                    spriteResolutionProfilePairCounts.GetValueOrDefault(spriteResolution) + 1;
            }
            modeCount++;
        }

        Assert.Equal(66, modeCount);
        Assert.Equal(2, byte33ClassCounts.Count);
        Assert.Equal(72, byte33ClassCounts[0x02]);
        Assert.Equal(192, byte33ClassCounts[0x04]);
        Assert.Equal(2, spriteResolutionModeCounts.Count);
        Assert.Equal(18, spriteResolutionModeCounts[0x0002]);
        Assert.Equal(48, spriteResolutionModeCounts[0x0004]);
        Assert.Equal(2, spriteResolutionProfilePairCounts.Count);
        Assert.Equal(72, spriteResolutionProfilePairCounts[0x0002]);
        Assert.Equal(192, spriteResolutionProfilePairCounts[0x0004]);
    }

    private static void AssertDisplayThirtyFiveBytePortableSeam()
    {
        const ushort expectedSpriteResolutionY = 0x0001;
        var profiles = new[]
        {
            (Ecs: false, Aga: false),
            (Ecs: true, Aga: false),
            (Ecs: false, Aga: true),
            (Ecs: true, Aga: true)
        };
        var byte34ClassCounts = new Dictionary<byte, int>();
        var spriteResolutionYModeCounts = new Dictionary<ushort, int>();
        var spriteResolutionYProfilePairCounts = new Dictionary<ushort, int>();
        var modeCount = 0;
        for (var mode = GraphicsDisplayDatabase.NextDisplayInfo(GraphicsModeIds.Invalid);
             mode != GraphicsModeIds.Invalid;
             mode = GraphicsDisplayDatabase.NextDisplayInfo(mode))
        {
            spriteResolutionYModeCounts[expectedSpriteResolutionY] =
                spriteResolutionYModeCounts.GetValueOrDefault(expectedSpriteResolutionY) + 1;
            foreach (var profile in profiles)
            {
                var prior = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    34,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(34u, prior.Result);
                Assert.Equal(34, prior.Bytes.Length);
                var portable = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    35,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(35u, portable.Result);
                Assert.Equal(35, portable.Bytes.Length);
                Assert.Equal(prior.Bytes, portable.Bytes.Take(34));
                var next = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    36,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(36u, next.Result);
                Assert.Equal(36, next.Bytes.Length);
                Assert.Equal(portable.Bytes, next.Bytes.Take(35));
                var full = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    56,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(48u, full.Result);
                Assert.Equal(48, full.Bytes.Length);
                Assert.Equal(prior.Bytes, full.Bytes.Take(34));
                Assert.Equal(portable.Bytes, full.Bytes.Take(35));
                Assert.Equal(next.Bytes, full.Bytes.Take(36));

                var spriteResolutionY = checked((ushort)((next.Bytes[34] << 8) | next.Bytes[35]));
                var fullSpriteResolutionY = checked((ushort)((full.Bytes[34] << 8) | full.Bytes[35]));
                Assert.Equal((byte)0, portable.Bytes[34]);
                Assert.Equal(expectedSpriteResolutionY, spriteResolutionY);
                Assert.Equal(expectedSpriteResolutionY, fullSpriteResolutionY);
                byte34ClassCounts[portable.Bytes[34]] =
                    byte34ClassCounts.GetValueOrDefault(portable.Bytes[34]) + 1;
                spriteResolutionYProfilePairCounts[spriteResolutionY] =
                    spriteResolutionYProfilePairCounts.GetValueOrDefault(spriteResolutionY) + 1;
            }
            modeCount++;
        }

        Assert.Equal(66, modeCount);
        Assert.Single(byte34ClassCounts);
        Assert.Equal(264, byte34ClassCounts[0]);
        Assert.Single(spriteResolutionYModeCounts);
        Assert.Equal(66, spriteResolutionYModeCounts[expectedSpriteResolutionY]);
        Assert.Single(spriteResolutionYProfilePairCounts);
        Assert.Equal(264, spriteResolutionYProfilePairCounts[expectedSpriteResolutionY]);
    }

    private static void AssertDisplayThirtySixBytePortableSeam()
    {
        const ushort expectedSpriteResolutionY = 0x0001;
        var profiles = new[]
        {
            (Ecs: false, Aga: false),
            (Ecs: true, Aga: false),
            (Ecs: false, Aga: true),
            (Ecs: true, Aga: true)
        };
        var byte35ClassCounts = new Dictionary<byte, int>();
        var spriteResolutionYModeCounts = new Dictionary<ushort, int>();
        var spriteResolutionYProfilePairCounts = new Dictionary<ushort, int>();
        var modeCount = 0;
        for (var mode = GraphicsDisplayDatabase.NextDisplayInfo(GraphicsModeIds.Invalid);
             mode != GraphicsModeIds.Invalid;
             mode = GraphicsDisplayDatabase.NextDisplayInfo(mode))
        {
            spriteResolutionYModeCounts[expectedSpriteResolutionY] =
                spriteResolutionYModeCounts.GetValueOrDefault(expectedSpriteResolutionY) + 1;
            foreach (var profile in profiles)
            {
                var prior = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    35,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(35u, prior.Result);
                Assert.Equal(35, prior.Bytes.Length);
                var portable = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    36,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(36u, portable.Result);
                Assert.Equal(36, portable.Bytes.Length);
                Assert.Equal(prior.Bytes, portable.Bytes.Take(35));
                var next = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    37,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(37u, next.Result);
                Assert.Equal(37, next.Bytes.Length);
                Assert.Equal(portable.Bytes, next.Bytes.Take(36));
                var full = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    56,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(48u, full.Result);
                Assert.Equal(48, full.Bytes.Length);
                Assert.Equal(prior.Bytes, full.Bytes.Take(35));
                Assert.Equal(portable.Bytes, full.Bytes.Take(36));
                Assert.Equal(next.Bytes, full.Bytes.Take(37));

                var spriteResolutionY = checked((ushort)((portable.Bytes[34] << 8) | portable.Bytes[35]));
                var nextSpriteResolutionY = checked((ushort)((next.Bytes[34] << 8) | next.Bytes[35]));
                var fullSpriteResolutionY = checked((ushort)((full.Bytes[34] << 8) | full.Bytes[35]));
                Assert.Equal((byte)1, portable.Bytes[35]);
                Assert.Equal(expectedSpriteResolutionY, spriteResolutionY);
                Assert.Equal(expectedSpriteResolutionY, nextSpriteResolutionY);
                Assert.Equal(expectedSpriteResolutionY, fullSpriteResolutionY);
                byte35ClassCounts[portable.Bytes[35]] =
                    byte35ClassCounts.GetValueOrDefault(portable.Bytes[35]) + 1;
                spriteResolutionYProfilePairCounts[spriteResolutionY] =
                    spriteResolutionYProfilePairCounts.GetValueOrDefault(spriteResolutionY) + 1;
            }
            modeCount++;
        }

        Assert.Equal(66, modeCount);
        Assert.Single(byte35ClassCounts);
        Assert.Equal(264, byte35ClassCounts[1]);
        Assert.Single(spriteResolutionYModeCounts);
        Assert.Equal(66, spriteResolutionYModeCounts[expectedSpriteResolutionY]);
        Assert.Single(spriteResolutionYProfilePairCounts);
        Assert.Equal(264, spriteResolutionYProfilePairCounts[expectedSpriteResolutionY]);
    }

    private static void AssertDisplayThirtySevenBytePortableSeam()
    {
        const byte expectedPaddingByte = 0x00;
        var profiles = new[]
        {
            (Ecs: false, Aga: false),
            (Ecs: true, Aga: false),
            (Ecs: false, Aga: true),
            (Ecs: true, Aga: true)
        };
        var byte36ModeCounts = new Dictionary<byte, int>();
        var byte36ProfilePairCounts = new Dictionary<byte, int>();
        var modeCount = 0;
        for (var mode = GraphicsDisplayDatabase.NextDisplayInfo(GraphicsModeIds.Invalid);
             mode != GraphicsModeIds.Invalid;
             mode = GraphicsDisplayDatabase.NextDisplayInfo(mode))
        {
            byte36ModeCounts[expectedPaddingByte] =
                byte36ModeCounts.GetValueOrDefault(expectedPaddingByte) + 1;
            foreach (var profile in profiles)
            {
                var prior = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    36,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(36u, prior.Result);
                Assert.Equal(36, prior.Bytes.Length);
                var portable = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    37,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(37u, portable.Result);
                Assert.Equal(37, portable.Bytes.Length);
                Assert.Equal(prior.Bytes, portable.Bytes.Take(36));
                var next = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    38,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(38u, next.Result);
                Assert.Equal(38, next.Bytes.Length);
                Assert.Equal(portable.Bytes, next.Bytes.Take(37));
                var full = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    56,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(48u, full.Result);
                Assert.Equal(48, full.Bytes.Length);
                Assert.Equal(prior.Bytes, full.Bytes.Take(36));
                Assert.Equal(portable.Bytes, full.Bytes.Take(37));
                Assert.Equal(next.Bytes, full.Bytes.Take(38));

                Assert.Equal(expectedPaddingByte, portable.Bytes[36]);
                Assert.Equal(expectedPaddingByte, next.Bytes[36]);
                Assert.Equal(expectedPaddingByte, full.Bytes[36]);
                byte36ProfilePairCounts[portable.Bytes[36]] =
                    byte36ProfilePairCounts.GetValueOrDefault(portable.Bytes[36]) + 1;
            }
            modeCount++;
        }

        Assert.Equal(66, modeCount);
        Assert.Single(byte36ModeCounts);
        Assert.Equal(66, byte36ModeCounts[expectedPaddingByte]);
        Assert.Single(byte36ProfilePairCounts);
        Assert.Equal(264, byte36ProfilePairCounts[expectedPaddingByte]);
    }

    [Theory]
    [MemberData(nameof(DisplayThirtyEightByteCases))]
    public void GetDisplayInfoDataPublishesTheThirtyEightByteDisplayPrefix(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        AssertDisplayThirtyEightBytePortableSeam();

        using var fixture = new Fixture(relocated, autoInitEntry);
        var maximumCapabilityBase = uint.MaxValue - (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        var lastAlignedCapabilityBase = maximumCapabilityBase & ~1u;
        var firstWrapCapabilityBase = maximumCapabilityBase + 1u;
        var handle = ModeId;
        var destination = 0x300u;
        var requestedBytes = 38u;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = foreignMode;
        var graphicsBase = fixture.GraphicsBase;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var publishes = false;
        var providerOwned = false;
        var ownedZero = false;
        var malformedForeignSpan = false;
        var supportsEcsOracle = true;
        var supportsAgaOracle = false;
        byte? chipRevision = null;
        var logicalCapabilityReads = 0;

        void Publish(uint mode, byte chip, bool ecs, bool aga)
        {
            publishes = true;
            handle = mode;
            expectedMode = mode;
            chipRevision = chip;
            supportsEcsOracle = ecs;
            supportsAgaOracle = aga;
            logicalCapabilityReads = 1;
        }

        switch (scenario)
        {
            case "disp38-pal-ordinary-even-ecs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp38-ntsc-ordinary-odd-alice": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.AaAlice, false, false); destination = 0x301; break;
            case "disp38-ntsc-superhires-lace-exact-end-lisa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresLaceKey, (byte)GraphicsChipRevision.AaLisa, false, false); destination = LastBase(38); break;
            case "disp38-null-ntsc-ehb-lace-aa-pair":
                expectedMode = GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteLaceKey;
                Publish(expectedMode, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true);
                handle = 0; modeId = expectedMode; break;
            case "disp38-private-default-aa-pair": Publish(GraphicsModeIds.DefaultMonitor, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); handle = GraphicsDisplayDatabase.DefaultModeHandle; break;
            case "disp38-pal-dpf-last-aligned-setaa": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresDualPlayfieldKey, (byte)GraphicsChipRevision.SetAa, true, true); graphicsBase = lastAlignedCapabilityBase; break;
            case "disp38-ntsc-dpf2-first-even-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresDualPlayfieldTwoKey, 0, false, false); graphicsBase = 2; break;
            case "disp38-default-ham-setaa": Publish(GraphicsModeIds.DefaultMonitor | GraphicsModeIds.HamKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp38-ntsc-superhires-setaa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp38-pal-superhires-ocs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, 0, false, false); graphicsBase = 2; break;
            case "disp38-ntsc-superhires-ecs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp38-pal-superhires-dpf2-lace-aa-pair": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresDualPlayfieldTwoLaceKey, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); break;
            case "disp38-foreign-provider": handle = foreignMode; modeId = ModeId; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "disp38-null-a6-control": graphicsBase = 0; break;
            case "disp38-odd-a6-control": graphicsBase = 3; break;
            case "disp38-exact-end-a6-control": graphicsBase = maximumCapabilityBase; break;
            case "disp38-first-wrap-a6-control": graphicsBase = firstWrapCapabilityBase; break;
            case "disp38-first-wrap-control": destination = 0xFFFF_FFDB; graphicsBase = uint.MaxValue; break;
            case "disp38-superhires-first-wrap-control": handle = GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey; expectedMode = handle; destination = 0xFFFF_FFDB; break;
            case "disp38-foreign-first-wrap-control": handle = foreignMode; modeId = ModeId; destination = 0xFFFF_FFDB; graphicsBase = uint.MaxValue; malformedForeignSpan = true; break;
            case "disp36-ordinary-valid-control": requestedBytes = 36; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "disp37-superhires-valid-control": requestedBytes = 37; Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp55-control": requestedBytes = 55; expectedResult = requestedBytes; graphicsBase = uint.MaxValue; break;
            case "disp56-control": requestedBytes = 56; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "name38-control": tag = GraphicsDisplayDatabase.DtagName; publishes = true; graphicsBase = uint.MaxValue; break;
            case "dims38-control": tag = GraphicsDisplayDatabase.DtagDims; graphicsBase = uint.MaxValue; break;
            case "mntr38-control": tag = GraphicsDisplayDatabase.DtagMntr; graphicsBase = uint.MaxValue; break;
            case "unsupported38-control": tag = UnsupportedTag; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            case "vec38-provider-control": tag = GraphicsDisplayDatabase.DtagVec; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "sentinel38-control": handle = 0; modeId = GraphicsModeIds.Invalid; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "disp38-ntsc-superhires-lace-exact-end-lisa")
            Assert.Equal((ulong)uint.MaxValue, (ulong)destination + requestedBytes - 1);
        if (scenario is "disp38-first-wrap-control" or "disp38-superhires-first-wrap-control" or "disp38-foreign-first-wrap-control")
            Assert.Equal((ulong)uint.MaxValue + 1, (ulong)destination + requestedBytes - 1);
        if (scenario == "disp38-default-ham-setaa")
            Assert.NotEqual(0u, expectedMode);
        if (chipRevision.HasValue)
            fixture.SeedChipRevision(graphicsBase, chipRevision.Value);

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(tag, handle, modeId, requestedBytes, supportsEcsOracle, supportsAgaOracle);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);
            Assert.Equal(BuildQueryHeader(tag, expectedMode), expectedBytes.Take(16));
            if (tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes == 38)
            {
                var expectedSpriteResolution = ExpectedDisplaySpriteResolution(expectedMode);
                var expectedPaletteRange = ExpectedDisplayPaletteRange(expectedMode, supportsAgaOracle);
                Assert.Equal((byte)0, expectedBytes[37]);
                Assert.Equal((byte)0, expectedBytes[36]);
                Assert.Equal((byte)0, expectedBytes[34]);
                Assert.Equal((byte)1, expectedBytes[35]);
                Assert.Equal((byte)0, expectedBytes[32]);
                Assert.Equal((byte)expectedSpriteResolution, expectedBytes[33]);
                Assert.Equal((byte)(expectedPaletteRange >> 8), expectedBytes[30]);
                Assert.Equal((byte)(expectedPaletteRange & 0x00FF), expectedBytes[31]);
                Assert.Equal((byte)8, expectedBytes[29]);
                Assert.Equal((byte)0, expectedBytes[28]);
                Assert.Equal((byte)(ExpectedDisplayPixelSpeed(expectedMode) & 0x00FF), expectedBytes[27]);
                Assert.Equal((byte)0, expectedBytes[26]);
                Assert.Equal((byte)1, expectedBytes[25]);
                Assert.Equal((byte)0, expectedBytes[24]);
                Assert.Equal(ExpectedDisplayByte17(expectedMode), expectedBytes[23]);
                Assert.Equal((byte)0, expectedBytes[22]);
                Assert.Equal(ExpectedDisplayByte15(expectedMode), expectedBytes[21]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 ? (byte)0x01 :
                    (expectedMode & GraphicsModeIds.ExtraHalfBriteMode) != 0 ? (byte)0x31 : (byte)0x21, expectedBytes[20]);
                Assert.Equal(supportsAgaOracle ? (byte)0x11 : (byte)0x10, expectedBytes[19]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 && !supportsEcsOracle ? (byte)1 : (byte)0, expectedBytes[17]);
            }
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes ? fixture.ReadOutputByte(followingAddress) : (byte)0;
        var result = fixture.Invoke(handle, destination, requestedBytes, tag, modeId, graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        var expectedCapabilityReads = logicalCapabilityReads;
        Check(failures, "result and terminal provenance", () =>
        {
            Assert.Equal(expectedResult, result.Data0); Assert.False(result.UsedFallback); Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned) Assert.Equal(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
            else if (publishes) Assert.Equal(AddQuickFourLongA7Opcode, result.NativeReturnPredecessorOpcode);
            else if (ownedZero) Assert.Equal(MoveQuickZeroD0Opcode, result.NativeReturnPredecessorOpcode);
            else if (malformedForeignSpan) Assert.NotEqual(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
        });
        Check(failures, "capability/default-monitor reads", () => { Assert.Equal(expectedCapabilityReads, result.CapabilityReadCount); Assert.Equal(0, result.DefaultMonitorReadCount); });
        Check(failures, "caller ABI", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter); Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1); Assert.Equal(destination, result.Address1); Assert.Equal(graphicsBase, result.Address6);
            if (publishes && tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes is 27 or 28 or 29 or 30 or 31 or 32 or 33 or 34 or 35 or 36 or 37 or 38)
                Assert.Equal((ushort)0, (ushort)(result.StatusRegister & M68kCpuState.Extend));
            Assert.Empty(result.RegisterDifferences);
        });
        if (publishes)
        {
            Check(failures, "exact portable prefix", () => fixture.AssertOnlyOutputPrefixChanged(before, after, destination, expectedBytes));
            Check(failures, "following byte untouched", () => Assert.Equal(followingByte, fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "memory intact", () => AssertMemoryEqual(before, after));
            Check(failures, "no destination access", () => AssertNoDestinationAccess(result, destination, requestedBytes));
        }
        Assert.True(failures.Count == 0, $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    private static void AssertDisplayThirtyEightBytePortableSeam()
    {
        const byte expectedPaddingByte = 0x00;
        var profiles = new[]
        {
            (Ecs: false, Aga: false),
            (Ecs: true, Aga: false),
            (Ecs: false, Aga: true),
            (Ecs: true, Aga: true)
        };
        var byte37ModeCounts = new Dictionary<byte, int>();
        var byte37ProfilePairCounts = new Dictionary<byte, int>();
        var modeCount = 0;
        for (var mode = GraphicsDisplayDatabase.NextDisplayInfo(GraphicsModeIds.Invalid);
             mode != GraphicsModeIds.Invalid;
             mode = GraphicsDisplayDatabase.NextDisplayInfo(mode))
        {
            byte37ModeCounts[expectedPaddingByte] =
                byte37ModeCounts.GetValueOrDefault(expectedPaddingByte) + 1;
            foreach (var profile in profiles)
            {
                var prior = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    37,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(37u, prior.Result);
                Assert.Equal(37, prior.Bytes.Length);
                var portable = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    38,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(38u, portable.Result);
                Assert.Equal(38, portable.Bytes.Length);
                Assert.Equal(prior.Bytes, portable.Bytes.Take(37));
                var next = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    39,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(39u, next.Result);
                Assert.Equal(39, next.Bytes.Length);
                Assert.Equal(portable.Bytes, next.Bytes.Take(38));
                var full = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    56,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(48u, full.Result);
                Assert.Equal(48, full.Bytes.Length);
                Assert.Equal(prior.Bytes, full.Bytes.Take(37));
                Assert.Equal(portable.Bytes, full.Bytes.Take(38));
                Assert.Equal(next.Bytes, full.Bytes.Take(39));

                Assert.Equal(expectedPaddingByte, portable.Bytes[37]);
                Assert.Equal(expectedPaddingByte, next.Bytes[37]);
                Assert.Equal(expectedPaddingByte, full.Bytes[37]);
                byte37ProfilePairCounts[portable.Bytes[37]] =
                    byte37ProfilePairCounts.GetValueOrDefault(portable.Bytes[37]) + 1;
            }
            modeCount++;
        }

        Assert.Equal(66, modeCount);
        Assert.Single(byte37ModeCounts);
        Assert.Equal(66, byte37ModeCounts[expectedPaddingByte]);
        Assert.Single(byte37ProfilePairCounts);
        Assert.Equal(264, byte37ProfilePairCounts[expectedPaddingByte]);
    }

    [Theory]
    [MemberData(nameof(DisplayThirtyNineByteCases))]
    public void GetDisplayInfoDataPublishesTheThirtyNineByteDisplayPrefix(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        AssertDisplayThirtyNineBytePortableSeam();

        using var fixture = new Fixture(relocated, autoInitEntry);
        var maximumCapabilityBase = uint.MaxValue - (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        var lastAlignedCapabilityBase = maximumCapabilityBase & ~1u;
        var firstWrapCapabilityBase = maximumCapabilityBase + 1u;
        var handle = ModeId;
        var destination = 0x300u;
        var requestedBytes = 39u;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = foreignMode;
        var graphicsBase = fixture.GraphicsBase;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var publishes = false;
        var providerOwned = false;
        var ownedZero = false;
        var malformedForeignSpan = false;
        var supportsEcsOracle = true;
        var supportsAgaOracle = false;
        byte? chipRevision = null;
        var logicalCapabilityReads = 0;

        void Publish(uint mode, byte chip, bool ecs, bool aga)
        {
            publishes = true;
            handle = mode;
            expectedMode = mode;
            chipRevision = chip;
            supportsEcsOracle = ecs;
            supportsAgaOracle = aga;
            logicalCapabilityReads = 1;
        }

        switch (scenario)
        {
            case "disp39-pal-ordinary-even-ecs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp39-ntsc-ordinary-odd-alice": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.AaAlice, false, false); destination = 0x301; break;
            case "disp39-ntsc-superhires-lace-exact-end-lisa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresLaceKey, (byte)GraphicsChipRevision.AaLisa, false, false); destination = LastBase(39); break;
            case "disp39-null-ntsc-ehb-lace-aa-pair":
                expectedMode = GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteLaceKey;
                Publish(expectedMode, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true);
                handle = 0; modeId = expectedMode; break;
            case "disp39-private-default-aa-pair": Publish(GraphicsModeIds.DefaultMonitor, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); handle = GraphicsDisplayDatabase.DefaultModeHandle; break;
            case "disp39-pal-dpf-last-aligned-setaa": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresDualPlayfieldKey, (byte)GraphicsChipRevision.SetAa, true, true); graphicsBase = lastAlignedCapabilityBase; break;
            case "disp39-ntsc-dpf2-first-even-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresDualPlayfieldTwoKey, 0, false, false); graphicsBase = 2; break;
            case "disp39-default-ham-setaa": Publish(GraphicsModeIds.DefaultMonitor | GraphicsModeIds.HamKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp39-ntsc-superhires-setaa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp39-pal-superhires-ocs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, 0, false, false); graphicsBase = 2; break;
            case "disp39-ntsc-superhires-ecs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp39-pal-superhires-dpf2-lace-aa-pair": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresDualPlayfieldTwoLaceKey, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); break;
            case "disp39-foreign-provider": handle = foreignMode; modeId = ModeId; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "disp39-null-a6-control": graphicsBase = 0; break;
            case "disp39-odd-a6-control": graphicsBase = 3; break;
            case "disp39-exact-end-a6-control": graphicsBase = maximumCapabilityBase; break;
            case "disp39-first-wrap-a6-control": graphicsBase = firstWrapCapabilityBase; break;
            case "disp39-first-wrap-control": destination = 0xFFFF_FFDA; graphicsBase = uint.MaxValue; break;
            case "disp39-superhires-first-wrap-control": handle = GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey; expectedMode = handle; destination = 0xFFFF_FFDA; break;
            case "disp39-foreign-first-wrap-control": handle = foreignMode; modeId = ModeId; destination = 0xFFFF_FFDA; graphicsBase = uint.MaxValue; malformedForeignSpan = true; break;
            case "disp37-ordinary-valid-control": requestedBytes = 37; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "disp38-superhires-valid-control": requestedBytes = 38; Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp55-control": requestedBytes = 55; expectedResult = requestedBytes; graphicsBase = uint.MaxValue; break;
            case "disp56-control": requestedBytes = 56; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "name39-control": tag = GraphicsDisplayDatabase.DtagName; publishes = true; graphicsBase = uint.MaxValue; break;
            case "dims39-control": tag = GraphicsDisplayDatabase.DtagDims; graphicsBase = uint.MaxValue; break;
            case "mntr39-control": tag = GraphicsDisplayDatabase.DtagMntr; graphicsBase = uint.MaxValue; break;
            case "unsupported39-control": tag = UnsupportedTag; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            case "vec39-provider-control": tag = GraphicsDisplayDatabase.DtagVec; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "sentinel39-control": handle = 0; modeId = GraphicsModeIds.Invalid; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "disp39-ntsc-superhires-lace-exact-end-lisa")
            Assert.Equal((ulong)uint.MaxValue, (ulong)destination + requestedBytes - 1);
        if (scenario is "disp39-first-wrap-control" or "disp39-superhires-first-wrap-control" or "disp39-foreign-first-wrap-control")
            Assert.Equal((ulong)uint.MaxValue + 1, (ulong)destination + requestedBytes - 1);
        if (scenario == "disp39-default-ham-setaa")
            Assert.NotEqual(0u, expectedMode);
        if (chipRevision.HasValue)
            fixture.SeedChipRevision(graphicsBase, chipRevision.Value);

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(tag, handle, modeId, requestedBytes, supportsEcsOracle, supportsAgaOracle);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);
            Assert.Equal(BuildQueryHeader(tag, expectedMode), expectedBytes.Take(16));
            if (tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes == 39)
            {
                var expectedSpriteResolution = ExpectedDisplaySpriteResolution(expectedMode);
                var expectedPaletteRange = ExpectedDisplayPaletteRange(expectedMode, supportsAgaOracle);
                Assert.Equal((byte)0, expectedBytes[38]);
                Assert.Equal((byte)0, expectedBytes[37]);
                Assert.Equal((byte)0, expectedBytes[36]);
                Assert.Equal((byte)0, expectedBytes[34]);
                Assert.Equal((byte)1, expectedBytes[35]);
                Assert.Equal((byte)0, expectedBytes[32]);
                Assert.Equal((byte)expectedSpriteResolution, expectedBytes[33]);
                Assert.Equal((byte)(expectedPaletteRange >> 8), expectedBytes[30]);
                Assert.Equal((byte)(expectedPaletteRange & 0x00FF), expectedBytes[31]);
                Assert.Equal((byte)8, expectedBytes[29]);
                Assert.Equal((byte)0, expectedBytes[28]);
                Assert.Equal((byte)(ExpectedDisplayPixelSpeed(expectedMode) & 0x00FF), expectedBytes[27]);
                Assert.Equal((byte)0, expectedBytes[26]);
                Assert.Equal((byte)1, expectedBytes[25]);
                Assert.Equal((byte)0, expectedBytes[24]);
                Assert.Equal(ExpectedDisplayByte17(expectedMode), expectedBytes[23]);
                Assert.Equal((byte)0, expectedBytes[22]);
                Assert.Equal(ExpectedDisplayByte15(expectedMode), expectedBytes[21]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 ? (byte)0x01 :
                    (expectedMode & GraphicsModeIds.ExtraHalfBriteMode) != 0 ? (byte)0x31 : (byte)0x21, expectedBytes[20]);
                Assert.Equal(supportsAgaOracle ? (byte)0x11 : (byte)0x10, expectedBytes[19]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 && !supportsEcsOracle ? (byte)1 : (byte)0, expectedBytes[17]);
            }
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes ? fixture.ReadOutputByte(followingAddress) : (byte)0;
        var result = fixture.Invoke(handle, destination, requestedBytes, tag, modeId, graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        var expectedCapabilityReads = logicalCapabilityReads;
        Check(failures, "result and terminal provenance", () =>
        {
            Assert.Equal(expectedResult, result.Data0); Assert.False(result.UsedFallback); Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned) Assert.Equal(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
            else if (publishes) Assert.Equal(AddQuickFourLongA7Opcode, result.NativeReturnPredecessorOpcode);
            else if (ownedZero) Assert.Equal(MoveQuickZeroD0Opcode, result.NativeReturnPredecessorOpcode);
            else if (malformedForeignSpan) Assert.NotEqual(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
        });
        Check(failures, "capability/default-monitor reads", () => { Assert.Equal(expectedCapabilityReads, result.CapabilityReadCount); Assert.Equal(0, result.DefaultMonitorReadCount); });
        Check(failures, "caller ABI", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter); Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1); Assert.Equal(destination, result.Address1); Assert.Equal(graphicsBase, result.Address6);
            if (publishes && tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes is 27 or 28 or 29 or 30 or 31 or 32 or 33 or 34 or 35 or 36 or 37 or 38 or 39)
                Assert.Equal((ushort)0, (ushort)(result.StatusRegister & M68kCpuState.Extend));
            Assert.Empty(result.RegisterDifferences);
        });
        if (publishes)
        {
            Check(failures, "exact portable prefix", () => fixture.AssertOnlyOutputPrefixChanged(before, after, destination, expectedBytes));
            Check(failures, "following byte untouched", () => Assert.Equal(followingByte, fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "memory intact", () => AssertMemoryEqual(before, after));
            Check(failures, "no destination access", () => AssertNoDestinationAccess(result, destination, requestedBytes));
        }
        Assert.True(failures.Count == 0, $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    private static void AssertDisplayThirtyNineBytePortableSeam()
    {
        const byte expectedPaddingByte = 0x00;
        var profiles = new[]
        {
            (Ecs: false, Aga: false),
            (Ecs: true, Aga: false),
            (Ecs: false, Aga: true),
            (Ecs: true, Aga: true)
        };
        var byte38ModeCounts = new Dictionary<byte, int>();
        var byte38ProfilePairCounts = new Dictionary<byte, int>();
        var modeCount = 0;
        for (var mode = GraphicsDisplayDatabase.NextDisplayInfo(GraphicsModeIds.Invalid);
             mode != GraphicsModeIds.Invalid;
             mode = GraphicsDisplayDatabase.NextDisplayInfo(mode))
        {
            byte38ModeCounts[expectedPaddingByte] =
                byte38ModeCounts.GetValueOrDefault(expectedPaddingByte) + 1;
            foreach (var profile in profiles)
            {
                var prior = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    38,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(38u, prior.Result);
                Assert.Equal(38, prior.Bytes.Length);
                var portable = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    39,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(39u, portable.Result);
                Assert.Equal(39, portable.Bytes.Length);
                Assert.Equal(prior.Bytes, portable.Bytes.Take(38));
                var next = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    40,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(40u, next.Result);
                Assert.Equal(40, next.Bytes.Length);
                Assert.Equal(portable.Bytes, next.Bytes.Take(39));
                var full = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    56,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(48u, full.Result);
                Assert.Equal(48, full.Bytes.Length);
                Assert.Equal(prior.Bytes, full.Bytes.Take(38));
                Assert.Equal(portable.Bytes, full.Bytes.Take(39));
                Assert.Equal(next.Bytes, full.Bytes.Take(40));

                Assert.Equal(expectedPaddingByte, portable.Bytes[38]);
                Assert.Equal(expectedPaddingByte, next.Bytes[38]);
                Assert.Equal(expectedPaddingByte, full.Bytes[38]);
                byte38ProfilePairCounts[portable.Bytes[38]] =
                    byte38ProfilePairCounts.GetValueOrDefault(portable.Bytes[38]) + 1;
            }
            modeCount++;
        }

        Assert.Equal(66, modeCount);
        Assert.Single(byte38ModeCounts);
        Assert.Equal(66, byte38ModeCounts[expectedPaddingByte]);
        Assert.Single(byte38ProfilePairCounts);
        Assert.Equal(264, byte38ProfilePairCounts[expectedPaddingByte]);
    }

    [Theory]
    [MemberData(nameof(DisplayFortyByteCases))]
    public void GetDisplayInfoDataPublishesTheFortyByteDisplayPrefix(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        AssertDisplayFortyBytePortableSeam();

        using var fixture = new Fixture(relocated, autoInitEntry);
        var maximumCapabilityBase = uint.MaxValue - (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        var lastAlignedCapabilityBase = maximumCapabilityBase & ~1u;
        var firstWrapCapabilityBase = maximumCapabilityBase + 1u;
        var handle = ModeId;
        var destination = 0x300u;
        var requestedBytes = 40u;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = foreignMode;
        var graphicsBase = fixture.GraphicsBase;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var publishes = false;
        var providerOwned = false;
        var ownedZero = false;
        var malformedForeignSpan = false;
        var supportsEcsOracle = true;
        var supportsAgaOracle = false;
        byte? chipRevision = null;
        var logicalCapabilityReads = 0;

        void Publish(uint mode, byte chip, bool ecs, bool aga)
        {
            publishes = true;
            handle = mode;
            expectedMode = mode;
            chipRevision = chip;
            supportsEcsOracle = ecs;
            supportsAgaOracle = aga;
            logicalCapabilityReads = 1;
        }

        switch (scenario)
        {
            case "disp40-pal-ordinary-even-ecs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp40-ntsc-ordinary-odd-alice": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.AaAlice, false, false); destination = 0x301; break;
            case "disp40-ntsc-superhires-lace-exact-end-lisa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresLaceKey, (byte)GraphicsChipRevision.AaLisa, false, false); destination = LastBase(40); break;
            case "disp40-null-ntsc-ehb-lace-aa-pair":
                expectedMode = GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteLaceKey;
                Publish(expectedMode, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true);
                handle = 0; modeId = expectedMode; break;
            case "disp40-private-default-aa-pair": Publish(GraphicsModeIds.DefaultMonitor, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); handle = GraphicsDisplayDatabase.DefaultModeHandle; break;
            case "disp40-pal-dpf-last-aligned-setaa": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresDualPlayfieldKey, (byte)GraphicsChipRevision.SetAa, true, true); graphicsBase = lastAlignedCapabilityBase; break;
            case "disp40-ntsc-dpf2-first-even-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresDualPlayfieldTwoKey, 0, false, false); graphicsBase = 2; break;
            case "disp40-default-ham-setaa": Publish(GraphicsModeIds.DefaultMonitor | GraphicsModeIds.HamKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp40-ntsc-superhires-setaa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp40-pal-superhires-ocs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, 0, false, false); graphicsBase = 2; break;
            case "disp40-ntsc-superhires-ecs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp40-pal-superhires-dpf2-lace-aa-pair": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresDualPlayfieldTwoLaceKey, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); break;
            case "disp40-foreign-provider": handle = foreignMode; modeId = ModeId; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "disp40-null-a6-control": graphicsBase = 0; break;
            case "disp40-odd-a6-control": graphicsBase = 3; break;
            case "disp40-exact-end-a6-control": graphicsBase = maximumCapabilityBase; break;
            case "disp40-first-wrap-a6-control": graphicsBase = firstWrapCapabilityBase; break;
            case "disp40-first-wrap-control": destination = 0xFFFF_FFD9; graphicsBase = uint.MaxValue; break;
            case "disp40-superhires-first-wrap-control": handle = GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey; expectedMode = handle; destination = 0xFFFF_FFD9; break;
            case "disp40-foreign-first-wrap-control": handle = foreignMode; modeId = ModeId; destination = 0xFFFF_FFD9; graphicsBase = uint.MaxValue; malformedForeignSpan = true; break;
            case "disp38-ordinary-valid-control": requestedBytes = 38; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "disp39-superhires-valid-control": requestedBytes = 39; Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp55-control": requestedBytes = 55; expectedResult = requestedBytes; graphicsBase = uint.MaxValue; break;
            case "disp56-control": requestedBytes = 56; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "name40-control": tag = GraphicsDisplayDatabase.DtagName; publishes = true; graphicsBase = uint.MaxValue; break;
            case "dims40-control": tag = GraphicsDisplayDatabase.DtagDims; graphicsBase = uint.MaxValue; break;
            case "mntr40-control": tag = GraphicsDisplayDatabase.DtagMntr; graphicsBase = uint.MaxValue; break;
            case "unsupported40-control": tag = UnsupportedTag; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            case "vec40-provider-control": tag = GraphicsDisplayDatabase.DtagVec; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "sentinel40-control": handle = 0; modeId = GraphicsModeIds.Invalid; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "disp40-ntsc-superhires-lace-exact-end-lisa")
            Assert.Equal((ulong)uint.MaxValue, (ulong)destination + requestedBytes - 1);
        if (scenario is "disp40-first-wrap-control" or "disp40-superhires-first-wrap-control" or "disp40-foreign-first-wrap-control")
            Assert.Equal((ulong)uint.MaxValue + 1, (ulong)destination + requestedBytes - 1);
        if (scenario == "disp40-default-ham-setaa")
            Assert.NotEqual(0u, expectedMode);
        if (chipRevision.HasValue)
            fixture.SeedChipRevision(graphicsBase, chipRevision.Value);

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(tag, handle, modeId, requestedBytes, supportsEcsOracle, supportsAgaOracle);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);
            Assert.Equal(BuildQueryHeader(tag, expectedMode), expectedBytes.Take(16));
            if (tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes == 40)
            {
                var expectedSpriteResolution = ExpectedDisplaySpriteResolution(expectedMode);
                var expectedPaletteRange = ExpectedDisplayPaletteRange(expectedMode, supportsAgaOracle);
                Assert.Equal((byte)0, expectedBytes[39]);
                Assert.Equal((byte)0, expectedBytes[38]);
                Assert.Equal((byte)0, expectedBytes[37]);
                Assert.Equal((byte)0, expectedBytes[36]);
                Assert.Equal((byte)0, expectedBytes[34]);
                Assert.Equal((byte)1, expectedBytes[35]);
                Assert.Equal((byte)0, expectedBytes[32]);
                Assert.Equal((byte)expectedSpriteResolution, expectedBytes[33]);
                Assert.Equal((byte)(expectedPaletteRange >> 8), expectedBytes[30]);
                Assert.Equal((byte)(expectedPaletteRange & 0x00FF), expectedBytes[31]);
                Assert.Equal((byte)8, expectedBytes[29]);
                Assert.Equal((byte)0, expectedBytes[28]);
                Assert.Equal((byte)(ExpectedDisplayPixelSpeed(expectedMode) & 0x00FF), expectedBytes[27]);
                Assert.Equal((byte)0, expectedBytes[26]);
                Assert.Equal((byte)1, expectedBytes[25]);
                Assert.Equal((byte)0, expectedBytes[24]);
                Assert.Equal(ExpectedDisplayByte17(expectedMode), expectedBytes[23]);
                Assert.Equal((byte)0, expectedBytes[22]);
                Assert.Equal(ExpectedDisplayByte15(expectedMode), expectedBytes[21]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 ? (byte)0x01 :
                    (expectedMode & GraphicsModeIds.ExtraHalfBriteMode) != 0 ? (byte)0x31 : (byte)0x21, expectedBytes[20]);
                Assert.Equal(supportsAgaOracle ? (byte)0x11 : (byte)0x10, expectedBytes[19]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 && !supportsEcsOracle ? (byte)1 : (byte)0, expectedBytes[17]);
            }
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes ? fixture.ReadOutputByte(followingAddress) : (byte)0;
        var result = fixture.Invoke(handle, destination, requestedBytes, tag, modeId, graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        var expectedCapabilityReads = logicalCapabilityReads;
        Check(failures, "result and terminal provenance", () =>
        {
            Assert.Equal(expectedResult, result.Data0); Assert.False(result.UsedFallback); Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned) Assert.Equal(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
            else if (publishes) Assert.Equal(AddQuickFourLongA7Opcode, result.NativeReturnPredecessorOpcode);
            else if (ownedZero) Assert.Equal(MoveQuickZeroD0Opcode, result.NativeReturnPredecessorOpcode);
            else if (malformedForeignSpan) Assert.NotEqual(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
        });
        Check(failures, "capability/default-monitor reads", () => { Assert.Equal(expectedCapabilityReads, result.CapabilityReadCount); Assert.Equal(0, result.DefaultMonitorReadCount); });
        Check(failures, "caller ABI", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter); Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1); Assert.Equal(destination, result.Address1); Assert.Equal(graphicsBase, result.Address6);
            if (publishes && tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes is 27 or 28 or 29 or 30 or 31 or 32 or 33 or 34 or 35 or 36 or 37 or 38 or 39 or 40)
                Assert.Equal((ushort)0, (ushort)(result.StatusRegister & M68kCpuState.Extend));
            Assert.Empty(result.RegisterDifferences);
        });
        if (publishes)
        {
            Check(failures, "exact portable prefix", () => fixture.AssertOnlyOutputPrefixChanged(before, after, destination, expectedBytes));
            Check(failures, "following byte untouched", () => Assert.Equal(followingByte, fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "memory intact", () => AssertMemoryEqual(before, after));
            Check(failures, "no destination access", () => AssertNoDestinationAccess(result, destination, requestedBytes));
        }
        Assert.True(failures.Count == 0, $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    private static void AssertDisplayFortyBytePortableSeam()
    {
        const byte expectedPaddingByte = 0x00;
        var profiles = new[]
        {
            (Ecs: false, Aga: false),
            (Ecs: true, Aga: false),
            (Ecs: false, Aga: true),
            (Ecs: true, Aga: true)
        };
        var byte39ModeCounts = new Dictionary<byte, int>();
        var byte39ProfilePairCounts = new Dictionary<byte, int>();
        var modeCount = 0;
        for (var mode = GraphicsDisplayDatabase.NextDisplayInfo(GraphicsModeIds.Invalid);
             mode != GraphicsModeIds.Invalid;
             mode = GraphicsDisplayDatabase.NextDisplayInfo(mode))
        {
            byte39ModeCounts[expectedPaddingByte] =
                byte39ModeCounts.GetValueOrDefault(expectedPaddingByte) + 1;
            foreach (var profile in profiles)
            {
                var prior = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    39,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(39u, prior.Result);
                Assert.Equal(39, prior.Bytes.Length);
                var portable = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    40,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(40u, portable.Result);
                Assert.Equal(40, portable.Bytes.Length);
                Assert.Equal(prior.Bytes, portable.Bytes.Take(39));
                var next = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    41,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(41u, next.Result);
                Assert.Equal(41, next.Bytes.Length);
                Assert.Equal(portable.Bytes, next.Bytes.Take(40));
                var full = BuildPortableQuery(
                    GraphicsDisplayDatabase.DtagDisp,
                    mode,
                    mode,
                    56,
                    profile.Ecs,
                    profile.Aga);
                Assert.Equal(48u, full.Result);
                Assert.Equal(48, full.Bytes.Length);
                Assert.Equal(prior.Bytes, full.Bytes.Take(39));
                Assert.Equal(portable.Bytes, full.Bytes.Take(40));
                Assert.Equal(next.Bytes, full.Bytes.Take(41));

                Assert.Equal(expectedPaddingByte, portable.Bytes[39]);
                Assert.Equal(expectedPaddingByte, next.Bytes[39]);
                Assert.Equal(expectedPaddingByte, full.Bytes[39]);
                byte39ProfilePairCounts[portable.Bytes[39]] =
                    byte39ProfilePairCounts.GetValueOrDefault(portable.Bytes[39]) + 1;
            }
            modeCount++;
        }

        Assert.Equal(66, modeCount);
        Assert.Single(byte39ModeCounts);
        Assert.Equal(66, byte39ModeCounts[expectedPaddingByte]);
        Assert.Single(byte39ProfilePairCounts);
        Assert.Equal(264, byte39ProfilePairCounts[expectedPaddingByte]);
    }

    [Theory]
    [MemberData(nameof(DisplayFortyOneByteCases))]
    public void GetDisplayInfoDataPublishesTheFortyOneByteDisplayPrefix(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        AssertDisplayFortyOneBytePortableSeam();

        using var fixture = new Fixture(relocated, autoInitEntry);
        var maximumCapabilityBase = uint.MaxValue - (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        var lastAlignedCapabilityBase = maximumCapabilityBase & ~1u;
        var firstWrapCapabilityBase = maximumCapabilityBase + 1u;
        var handle = ModeId;
        var destination = 0x300u;
        var requestedBytes = 41u;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = foreignMode;
        var graphicsBase = fixture.GraphicsBase;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var publishes = false;
        var providerOwned = false;
        var ownedZero = false;
        var malformedForeignSpan = false;
        var supportsEcsOracle = true;
        var supportsAgaOracle = false;
        byte? chipRevision = null;
        var logicalCapabilityReads = 0;

        void Publish(uint mode, byte chip, bool ecs, bool aga)
        {
            publishes = true;
            handle = mode;
            expectedMode = mode;
            chipRevision = chip;
            supportsEcsOracle = ecs;
            supportsAgaOracle = aga;
            logicalCapabilityReads = 1;
        }

        switch (scenario)
        {
            case "disp41-pal-ordinary-even-ecs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp41-ntsc-ordinary-odd-alice": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.AaAlice, false, false); destination = 0x301; break;
            case "disp41-ntsc-superhires-lace-exact-end-lisa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresLaceKey, (byte)GraphicsChipRevision.AaLisa, false, false); destination = LastBase(41); break;
            case "disp41-null-ntsc-ehb-lace-aa-pair":
                expectedMode = GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteLaceKey;
                Publish(expectedMode, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true);
                handle = 0; modeId = expectedMode; break;
            case "disp41-private-default-aa-pair": Publish(GraphicsModeIds.DefaultMonitor, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); handle = GraphicsDisplayDatabase.DefaultModeHandle; break;
            case "disp41-pal-dpf-last-aligned-setaa": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresDualPlayfieldKey, (byte)GraphicsChipRevision.SetAa, true, true); graphicsBase = lastAlignedCapabilityBase; break;
            case "disp41-ntsc-dpf2-first-even-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresDualPlayfieldTwoKey, 0, false, false); graphicsBase = 2; break;
            case "disp41-default-ham-setaa": Publish(GraphicsModeIds.DefaultMonitor | GraphicsModeIds.HamKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp41-ntsc-superhires-setaa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp41-pal-superhires-ocs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, 0, false, false); graphicsBase = 2; break;
            case "disp41-ntsc-superhires-ecs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp41-pal-superhires-dpf2-lace-aa-pair": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresDualPlayfieldTwoLaceKey, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); break;
            case "disp41-foreign-provider": handle = foreignMode; modeId = ModeId; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "disp41-null-a6-control": graphicsBase = 0; break;
            case "disp41-odd-a6-control": graphicsBase = 3; break;
            case "disp41-exact-end-a6-control": graphicsBase = maximumCapabilityBase; break;
            case "disp41-first-wrap-a6-control": graphicsBase = firstWrapCapabilityBase; break;
            case "disp41-first-wrap-control": destination = 0xFFFF_FFD8; graphicsBase = uint.MaxValue; break;
            case "disp41-superhires-first-wrap-control": handle = GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey; expectedMode = handle; destination = 0xFFFF_FFD8; break;
            case "disp41-foreign-first-wrap-control": handle = foreignMode; modeId = ModeId; destination = 0xFFFF_FFD8; graphicsBase = uint.MaxValue; malformedForeignSpan = true; break;
            case "disp39-ordinary-valid-control": requestedBytes = 39; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "disp40-superhires-valid-control": requestedBytes = 40; Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp55-control": requestedBytes = 55; expectedResult = requestedBytes; graphicsBase = uint.MaxValue; break;
            case "disp56-control": requestedBytes = 56; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "name41-control": tag = GraphicsDisplayDatabase.DtagName; publishes = true; graphicsBase = uint.MaxValue; break;
            case "dims41-control": tag = GraphicsDisplayDatabase.DtagDims; graphicsBase = uint.MaxValue; break;
            case "mntr41-control": tag = GraphicsDisplayDatabase.DtagMntr; graphicsBase = uint.MaxValue; break;
            case "unsupported41-control": tag = UnsupportedTag; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            case "vec41-provider-control": tag = GraphicsDisplayDatabase.DtagVec; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "sentinel41-control": handle = 0; modeId = GraphicsModeIds.Invalid; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "disp41-ntsc-superhires-lace-exact-end-lisa")
            Assert.Equal((ulong)uint.MaxValue, (ulong)destination + requestedBytes - 1);
        if (scenario is "disp41-first-wrap-control" or "disp41-superhires-first-wrap-control" or "disp41-foreign-first-wrap-control")
            Assert.Equal((ulong)uint.MaxValue + 1, (ulong)destination + requestedBytes - 1);
        if (scenario == "disp41-default-ham-setaa")
            Assert.NotEqual(0u, expectedMode);
        if (chipRevision.HasValue)
            fixture.SeedChipRevision(graphicsBase, chipRevision.Value);

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(tag, handle, modeId, requestedBytes, supportsEcsOracle, supportsAgaOracle);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);
            Assert.Equal(BuildQueryHeader(tag, expectedMode), expectedBytes.Take(16));
            if (tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes == 41)
            {
                var expectedSpriteResolution = ExpectedDisplaySpriteResolution(expectedMode);
                var expectedPaletteRange = ExpectedDisplayPaletteRange(expectedMode, supportsAgaOracle);
                Assert.Equal(ExpectedDisplayComponentBits(expectedMode, supportsAgaOracle), expectedBytes[40]);
                Assert.Equal((byte)0, expectedBytes[39]);
                Assert.Equal((byte)0, expectedBytes[38]);
                Assert.Equal((byte)0, expectedBytes[37]);
                Assert.Equal((byte)0, expectedBytes[36]);
                Assert.Equal((byte)0, expectedBytes[34]);
                Assert.Equal((byte)1, expectedBytes[35]);
                Assert.Equal((byte)0, expectedBytes[32]);
                Assert.Equal((byte)expectedSpriteResolution, expectedBytes[33]);
                Assert.Equal((byte)(expectedPaletteRange >> 8), expectedBytes[30]);
                Assert.Equal((byte)(expectedPaletteRange & 0x00FF), expectedBytes[31]);
                Assert.Equal((byte)8, expectedBytes[29]);
                Assert.Equal((byte)0, expectedBytes[28]);
                Assert.Equal((byte)(ExpectedDisplayPixelSpeed(expectedMode) & 0x00FF), expectedBytes[27]);
                Assert.Equal((byte)0, expectedBytes[26]);
                Assert.Equal((byte)1, expectedBytes[25]);
                Assert.Equal((byte)0, expectedBytes[24]);
                Assert.Equal(ExpectedDisplayByte17(expectedMode), expectedBytes[23]);
                Assert.Equal((byte)0, expectedBytes[22]);
                Assert.Equal(ExpectedDisplayByte15(expectedMode), expectedBytes[21]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 ? (byte)0x01 :
                    (expectedMode & GraphicsModeIds.ExtraHalfBriteMode) != 0 ? (byte)0x31 : (byte)0x21, expectedBytes[20]);
                Assert.Equal(supportsAgaOracle ? (byte)0x11 : (byte)0x10, expectedBytes[19]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 && !supportsEcsOracle ? (byte)1 : (byte)0, expectedBytes[17]);
            }
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes ? fixture.ReadOutputByte(followingAddress) : (byte)0;
        var result = fixture.Invoke(handle, destination, requestedBytes, tag, modeId, graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        var expectedCapabilityReads = logicalCapabilityReads;
        Check(failures, "result and terminal provenance", () =>
        {
            Assert.Equal(expectedResult, result.Data0); Assert.False(result.UsedFallback); Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned) Assert.Equal(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
            else if (publishes) Assert.Equal(AddQuickFourLongA7Opcode, result.NativeReturnPredecessorOpcode);
            else if (ownedZero) Assert.Equal(MoveQuickZeroD0Opcode, result.NativeReturnPredecessorOpcode);
            else if (malformedForeignSpan) Assert.NotEqual(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
        });
        Check(failures, "capability/default-monitor reads", () => { Assert.Equal(expectedCapabilityReads, result.CapabilityReadCount); Assert.Equal(0, result.DefaultMonitorReadCount); });
        Check(failures, "caller ABI", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter); Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1); Assert.Equal(destination, result.Address1); Assert.Equal(graphicsBase, result.Address6);
            if (publishes && tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes is 27 or 28 or 29 or 30 or 31 or 32 or 33 or 34 or 35 or 36 or 37 or 38 or 39 or 40 or 41)
                Assert.Equal((ushort)0, (ushort)(result.StatusRegister & M68kCpuState.Extend));
            Assert.Empty(result.RegisterDifferences);
        });
        if (publishes)
        {
            Check(failures, "exact portable prefix", () => fixture.AssertOnlyOutputPrefixChanged(before, after, destination, expectedBytes));
            Check(failures, "following byte untouched", () => Assert.Equal(followingByte, fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "memory intact", () => AssertMemoryEqual(before, after));
            Check(failures, "no destination access", () => AssertNoDestinationAccess(result, destination, requestedBytes));
        }
        Assert.True(failures.Count == 0, $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    private static byte ExpectedDisplayComponentBits(uint mode, bool aga)
        => aga ? (byte)8
            : (mode & GraphicsModeIds.SuperHiresMode) != 0 &&
              (mode & GraphicsModeIds.InterlaceMode) == 0 ? (byte)2 : (byte)4;

    private static void AssertDisplayFortyOneBytePortableSeam()
    {
        // Freeze the project oracle, not an independent raw Kickstart capture.
        var profiles = new[]
        {
            (Ecs: false, Aga: false), (Ecs: true, Aga: false),
            (Ecs: false, Aga: true), (Ecs: true, Aga: true)
        };
        var modeCount = 0;
        var pairCount = 0;
        var precisions = new HashSet<byte>();
        for (var mode = GraphicsDisplayDatabase.NextDisplayInfo(GraphicsModeIds.Invalid);
             mode != GraphicsModeIds.Invalid;
             mode = GraphicsDisplayDatabase.NextDisplayInfo(mode))
        {
            foreach (var profile in profiles)
            {
                var expected = ExpectedDisplayComponentBits(mode, profile.Aga);
                var prior = BuildPortableQuery(GraphicsDisplayDatabase.DtagDisp, mode, mode, 40, profile.Ecs, profile.Aga);
                var current = BuildPortableQuery(GraphicsDisplayDatabase.DtagDisp, mode, mode, 41, profile.Ecs, profile.Aga);
                var next = BuildPortableQuery(GraphicsDisplayDatabase.DtagDisp, mode, mode, 42, profile.Ecs, profile.Aga);
                var full = BuildPortableQuery(GraphicsDisplayDatabase.DtagDisp, mode, mode, 56, profile.Ecs, profile.Aga);
                Assert.Equal(40u, prior.Result);
                Assert.Equal(41u, current.Result);
                Assert.Equal(42u, next.Result);
                Assert.Equal(48u, full.Result);
                Assert.Equal(40, prior.Bytes.Length);
                Assert.Equal(41, current.Bytes.Length);
                Assert.Equal(42, next.Bytes.Length);
                Assert.Equal(48, full.Bytes.Length);
                Assert.Equal(prior.Bytes, current.Bytes.Take(40));
                Assert.Equal(current.Bytes, next.Bytes.Take(41));
                Assert.Equal(next.Bytes, full.Bytes.Take(42));
                Assert.Equal(expected, current.Bytes[40]);
                Assert.Equal(expected, full.Bytes[40]);
                Assert.Equal(expected, next.Bytes[41]);
                // Native N41 can select precision from the retained PaletteRange
                // low byte without reading capabilities or the output again.
                Assert.Equal(expected, current.Bytes[31] switch
                {
                    0xFF => (byte)8,
                    0x40 => (byte)2,
                    0x00 => (byte)4,
                    _ => throw new InvalidOperationException("Unexpected palette precision.")
                });
                precisions.Add(expected);
                pairCount++;
            }
            modeCount++;
        }
        Assert.Equal(66, modeCount);
        Assert.Equal(264, pairCount);
        Assert.Equal(new byte[] { 2, 4, 8 }, precisions.OrderBy(value => value));
    }

    [Theory]
    [MemberData(nameof(DisplayFortyTwoByteCases))]
    public void GetDisplayInfoDataPublishesTheFortyTwoByteDisplayPrefix(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        AssertDisplayFortyTwoBytePortableSeam();

        using var fixture = new Fixture(relocated, autoInitEntry);
        var maximumCapabilityBase = uint.MaxValue - (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        var lastAlignedCapabilityBase = maximumCapabilityBase & ~1u;
        var firstWrapCapabilityBase = maximumCapabilityBase + 1u;
        var handle = ModeId;
        var destination = 0x300u;
        var requestedBytes = 42u;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = foreignMode;
        var graphicsBase = fixture.GraphicsBase;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var publishes = false;
        var providerOwned = false;
        var ownedZero = false;
        var malformedForeignSpan = false;
        var supportsEcsOracle = true;
        var supportsAgaOracle = false;
        byte? chipRevision = null;
        var logicalCapabilityReads = 0;

        void Publish(uint mode, byte chip, bool ecs, bool aga)
        {
            publishes = true;
            handle = mode;
            expectedMode = mode;
            chipRevision = chip;
            supportsEcsOracle = ecs;
            supportsAgaOracle = aga;
            logicalCapabilityReads = 1;
        }

        switch (scenario)
        {
            case "disp42-pal-ordinary-even-ecs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp42-ntsc-ordinary-odd-alice": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.AaAlice, false, false); destination = 0x301; break;
            case "disp42-ntsc-superhires-lace-exact-end-lisa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresLaceKey, (byte)GraphicsChipRevision.AaLisa, false, false); destination = LastBase(42); break;
            case "disp42-null-ntsc-ehb-lace-aa-pair":
                expectedMode = GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteLaceKey;
                Publish(expectedMode, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true);
                handle = 0; modeId = expectedMode; break;
            case "disp42-private-default-aa-pair": Publish(GraphicsModeIds.DefaultMonitor, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); handle = GraphicsDisplayDatabase.DefaultModeHandle; break;
            case "disp42-pal-dpf-last-aligned-setaa": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresDualPlayfieldKey, (byte)GraphicsChipRevision.SetAa, true, true); graphicsBase = lastAlignedCapabilityBase; break;
            case "disp42-ntsc-dpf2-first-even-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresDualPlayfieldTwoKey, 0, false, false); graphicsBase = 2; break;
            case "disp42-default-ham-setaa": Publish(GraphicsModeIds.DefaultMonitor | GraphicsModeIds.HamKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp42-ntsc-superhires-setaa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp42-pal-superhires-ocs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, 0, false, false); graphicsBase = 2; break;
            case "disp42-ntsc-superhires-ecs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp42-pal-superhires-dpf2-lace-aa-pair": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresDualPlayfieldTwoLaceKey, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); break;
            case "disp42-foreign-provider": handle = foreignMode; modeId = ModeId; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "disp42-null-a6-control": graphicsBase = 0; break;
            case "disp42-odd-a6-control": graphicsBase = 3; break;
            case "disp42-exact-end-a6-control": graphicsBase = maximumCapabilityBase; break;
            case "disp42-first-wrap-a6-control": graphicsBase = firstWrapCapabilityBase; break;
            case "disp42-first-wrap-control": destination = 0xFFFF_FFD7; graphicsBase = uint.MaxValue; break;
            case "disp42-superhires-first-wrap-control": handle = GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey; expectedMode = handle; destination = 0xFFFF_FFD7; break;
            case "disp42-foreign-first-wrap-control": handle = foreignMode; modeId = ModeId; destination = 0xFFFF_FFD7; graphicsBase = uint.MaxValue; malformedForeignSpan = true; break;
            case "disp40-ordinary-valid-control": requestedBytes = 40; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "disp41-superhires-valid-control": requestedBytes = 41; Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp55-control": requestedBytes = 55; expectedResult = requestedBytes; graphicsBase = uint.MaxValue; break;
            case "disp56-control": requestedBytes = 56; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "name42-control": tag = GraphicsDisplayDatabase.DtagName; publishes = true; graphicsBase = uint.MaxValue; break;
            case "dims42-control": tag = GraphicsDisplayDatabase.DtagDims; graphicsBase = uint.MaxValue; break;
            case "mntr42-control": tag = GraphicsDisplayDatabase.DtagMntr; graphicsBase = uint.MaxValue; break;
            case "unsupported42-control": tag = UnsupportedTag; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            case "vec42-provider-control": tag = GraphicsDisplayDatabase.DtagVec; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "sentinel42-control": handle = 0; modeId = GraphicsModeIds.Invalid; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "disp42-ntsc-superhires-lace-exact-end-lisa")
            Assert.Equal((ulong)uint.MaxValue, (ulong)destination + requestedBytes - 1);
        if (scenario is "disp42-first-wrap-control" or "disp42-superhires-first-wrap-control" or "disp42-foreign-first-wrap-control")
            Assert.Equal((ulong)uint.MaxValue + 1, (ulong)destination + requestedBytes - 1);
        if (scenario == "disp42-default-ham-setaa")
            Assert.NotEqual(0u, expectedMode);
        if (chipRevision.HasValue)
            fixture.SeedChipRevision(graphicsBase, chipRevision.Value);

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(tag, handle, modeId, requestedBytes, supportsEcsOracle, supportsAgaOracle);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);
            Assert.Equal(BuildQueryHeader(tag, expectedMode), expectedBytes.Take(16));
            if (tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes == 42)
            {
                var expectedSpriteResolution = ExpectedDisplaySpriteResolution(expectedMode);
                var expectedPaletteRange = ExpectedDisplayPaletteRange(expectedMode, supportsAgaOracle);
                Assert.Equal(ExpectedDisplayComponentBits(expectedMode, supportsAgaOracle), expectedBytes[41]);
                Assert.Equal(ExpectedDisplayComponentBits(expectedMode, supportsAgaOracle), expectedBytes[40]);
                Assert.Equal((byte)0, expectedBytes[39]);
                Assert.Equal((byte)0, expectedBytes[38]);
                Assert.Equal((byte)0, expectedBytes[37]);
                Assert.Equal((byte)0, expectedBytes[36]);
                Assert.Equal((byte)0, expectedBytes[34]);
                Assert.Equal((byte)1, expectedBytes[35]);
                Assert.Equal((byte)0, expectedBytes[32]);
                Assert.Equal((byte)expectedSpriteResolution, expectedBytes[33]);
                Assert.Equal((byte)(expectedPaletteRange >> 8), expectedBytes[30]);
                Assert.Equal((byte)(expectedPaletteRange & 0x00FF), expectedBytes[31]);
                Assert.Equal((byte)8, expectedBytes[29]);
                Assert.Equal((byte)0, expectedBytes[28]);
                Assert.Equal((byte)(ExpectedDisplayPixelSpeed(expectedMode) & 0x00FF), expectedBytes[27]);
                Assert.Equal((byte)0, expectedBytes[26]);
                Assert.Equal((byte)1, expectedBytes[25]);
                Assert.Equal((byte)0, expectedBytes[24]);
                Assert.Equal(ExpectedDisplayByte17(expectedMode), expectedBytes[23]);
                Assert.Equal((byte)0, expectedBytes[22]);
                Assert.Equal(ExpectedDisplayByte15(expectedMode), expectedBytes[21]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 ? (byte)0x01 :
                    (expectedMode & GraphicsModeIds.ExtraHalfBriteMode) != 0 ? (byte)0x31 : (byte)0x21, expectedBytes[20]);
                Assert.Equal(supportsAgaOracle ? (byte)0x11 : (byte)0x10, expectedBytes[19]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 && !supportsEcsOracle ? (byte)1 : (byte)0, expectedBytes[17]);
            }
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes ? fixture.ReadOutputByte(followingAddress) : (byte)0;
        var result = fixture.Invoke(handle, destination, requestedBytes, tag, modeId, graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        var expectedCapabilityReads = logicalCapabilityReads;
        Check(failures, "result and terminal provenance", () =>
        {
            Assert.Equal(expectedResult, result.Data0); Assert.False(result.UsedFallback); Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned) Assert.Equal(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
            else if (publishes) Assert.Equal(AddQuickFourLongA7Opcode, result.NativeReturnPredecessorOpcode);
            else if (ownedZero) Assert.Equal(MoveQuickZeroD0Opcode, result.NativeReturnPredecessorOpcode);
            else if (malformedForeignSpan) Assert.NotEqual(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
        });
        Check(failures, "capability/default-monitor reads", () => { Assert.Equal(expectedCapabilityReads, result.CapabilityReadCount); Assert.Equal(0, result.DefaultMonitorReadCount); });
        Check(failures, "caller ABI", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter); Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1); Assert.Equal(destination, result.Address1); Assert.Equal(graphicsBase, result.Address6);
            if (publishes && tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes is 27 or 28 or 29 or 30 or 31 or 32 or 33 or 34 or 35 or 36 or 37 or 38 or 39 or 40 or 41 or 42)
                Assert.Equal((ushort)0, (ushort)(result.StatusRegister & M68kCpuState.Extend));
            Assert.Empty(result.RegisterDifferences);
        });
        if (publishes)
        {
            Check(failures, "exact portable prefix", () => fixture.AssertOnlyOutputPrefixChanged(before, after, destination, expectedBytes));
            Check(failures, "following byte untouched", () => Assert.Equal(followingByte, fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "memory intact", () => AssertMemoryEqual(before, after));
            Check(failures, "no destination access", () => AssertNoDestinationAccess(result, destination, requestedBytes));
        }
        Assert.True(failures.Count == 0, $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    private static void AssertDisplayFortyTwoBytePortableSeam()
    {
        AssertDisplayComponentPrefixPortableSeam(42);
    }

    private static void AssertDisplayComponentPrefixPortableSeam(uint size)
    {
        var modeCount = 0;
        var pairCount = 0;
        var precisions = new HashSet<byte>();
        for (var mode = GraphicsDisplayDatabase.NextDisplayInfo(GraphicsModeIds.Invalid);
             mode != GraphicsModeIds.Invalid;
             mode = GraphicsDisplayDatabase.NextDisplayInfo(mode))
        {
            foreach (var ecs in new[] { false, true })
            foreach (var aga in new[] { false, true })
            {
                var expected = ExpectedDisplayComponentBits(mode, aga);
                var prior = BuildPortableQuery(GraphicsDisplayDatabase.DtagDisp, mode, mode, size - 1, ecs, aga);
                var current = BuildPortableQuery(GraphicsDisplayDatabase.DtagDisp, mode, mode, size, ecs, aga);
                var next = BuildPortableQuery(GraphicsDisplayDatabase.DtagDisp, mode, mode, size + 1, ecs, aga);
                var full = BuildPortableQuery(GraphicsDisplayDatabase.DtagDisp, mode, mode, 56, ecs, aga);
                Assert.Equal(Math.Min(size - 1, 48u), prior.Result);
                Assert.Equal(Math.Min(size, 48u), current.Result);
                Assert.Equal(Math.Min(size + 1, 48u), next.Result);
                Assert.Equal(48u, full.Result);
                Assert.Equal(Math.Min((int)size - 1, 48), prior.Bytes.Length);
                Assert.Equal(Math.Min((int)size, 48), current.Bytes.Length);
                Assert.Equal(Math.Min((int)size + 1, 48), next.Bytes.Length);
                Assert.Equal(48, full.Bytes.Length);
                Assert.Equal(prior.Bytes, current.Bytes.Take((int)size - 1));
                Assert.Equal(current.Bytes, next.Bytes.Take((int)size));
                Assert.Equal(next.Bytes, full.Bytes.Take((int)size + 1));
                for (var componentOffset = 0x28; componentOffset < Math.Min((int)size, 0x2B); componentOffset++)
                    Assert.Equal(expected, current.Bytes[componentOffset]);
                Assert.Equal(expected, full.Bytes[0x28]);
                Assert.Equal(expected, full.Bytes[0x29]);
                Assert.Equal(expected, full.Bytes[0x2A]);
                precisions.Add(expected);
                pairCount++;
            }
            modeCount++;
        }
        Assert.Equal(66, modeCount);
        Assert.Equal(264, pairCount);
        Assert.Equal(new byte[] { 2, 4, 8 }, precisions.OrderBy(value => value));
    }

    [Theory]
    [MemberData(nameof(DisplayFortyThreeByteCases))]
    public void GetDisplayInfoDataPublishesTheFortyThreeByteDisplayPrefix(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        AssertDisplayFortyThreeBytePortableSeam();

        using var fixture = new Fixture(relocated, autoInitEntry);
        var maximumCapabilityBase = uint.MaxValue - (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        var lastAlignedCapabilityBase = maximumCapabilityBase & ~1u;
        var firstWrapCapabilityBase = maximumCapabilityBase + 1u;
        var handle = ModeId;
        var destination = 0x300u;
        var requestedBytes = 43u;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = foreignMode;
        var graphicsBase = fixture.GraphicsBase;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var publishes = false;
        var providerOwned = false;
        var ownedZero = false;
        var malformedForeignSpan = false;
        var supportsEcsOracle = true;
        var supportsAgaOracle = false;
        byte? chipRevision = null;
        var logicalCapabilityReads = 0;

        void Publish(uint mode, byte chip, bool ecs, bool aga)
        {
            publishes = true;
            handle = mode;
            expectedMode = mode;
            chipRevision = chip;
            supportsEcsOracle = ecs;
            supportsAgaOracle = aga;
            logicalCapabilityReads = 1;
        }

        switch (scenario)
        {
            case "disp43-pal-ordinary-even-ecs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp43-ntsc-ordinary-odd-alice": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.AaAlice, false, false); destination = 0x301; break;
            case "disp43-ntsc-superhires-lace-exact-end-lisa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresLaceKey, (byte)GraphicsChipRevision.AaLisa, false, false); destination = LastBase(43); break;
            case "disp43-null-ntsc-ehb-lace-aa-pair":
                expectedMode = GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteLaceKey;
                Publish(expectedMode, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true);
                handle = 0; modeId = expectedMode; break;
            case "disp43-private-default-aa-pair": Publish(GraphicsModeIds.DefaultMonitor, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); handle = GraphicsDisplayDatabase.DefaultModeHandle; break;
            case "disp43-pal-dpf-last-aligned-setaa": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresDualPlayfieldKey, (byte)GraphicsChipRevision.SetAa, true, true); graphicsBase = lastAlignedCapabilityBase; break;
            case "disp43-ntsc-dpf2-first-even-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresDualPlayfieldTwoKey, 0, false, false); graphicsBase = 2; break;
            case "disp43-default-ham-setaa": Publish(GraphicsModeIds.DefaultMonitor | GraphicsModeIds.HamKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp43-ntsc-superhires-setaa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "disp43-pal-superhires-ocs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, 0, false, false); graphicsBase = 2; break;
            case "disp43-ntsc-superhires-ecs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp43-pal-superhires-dpf2-lace-aa-pair": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresDualPlayfieldTwoLaceKey, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); break;
            case "disp43-foreign-provider": handle = foreignMode; modeId = ModeId; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "disp43-null-a6-control": graphicsBase = 0; break;
            case "disp43-odd-a6-control": graphicsBase = 3; break;
            case "disp43-exact-end-a6-control": graphicsBase = maximumCapabilityBase; break;
            case "disp43-first-wrap-a6-control": graphicsBase = firstWrapCapabilityBase; break;
            case "disp43-first-wrap-control": destination = 0xFFFF_FFD6; graphicsBase = uint.MaxValue; break;
            case "disp43-superhires-first-wrap-control": handle = GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey; expectedMode = handle; destination = 0xFFFF_FFD6; break;
            case "disp43-foreign-first-wrap-control": handle = foreignMode; modeId = ModeId; destination = 0xFFFF_FFD6; graphicsBase = uint.MaxValue; malformedForeignSpan = true; break;
            case "disp41-ordinary-valid-control": requestedBytes = 41; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "disp42-superhires-valid-control": requestedBytes = 42; Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp56-control": requestedBytes = 56; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "name43-control": tag = GraphicsDisplayDatabase.DtagName; publishes = true; graphicsBase = uint.MaxValue; break;
            case "dims43-control": tag = GraphicsDisplayDatabase.DtagDims; graphicsBase = uint.MaxValue; break;
            case "mntr43-control": tag = GraphicsDisplayDatabase.DtagMntr; graphicsBase = uint.MaxValue; break;
            case "unsupported43-control": tag = UnsupportedTag; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            case "vec43-provider-control": tag = GraphicsDisplayDatabase.DtagVec; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "sentinel43-control": handle = 0; modeId = GraphicsModeIds.Invalid; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "disp43-ntsc-superhires-lace-exact-end-lisa")
            Assert.Equal((ulong)uint.MaxValue, (ulong)destination + requestedBytes - 1);
        if (scenario is "disp43-first-wrap-control" or "disp43-superhires-first-wrap-control" or "disp43-foreign-first-wrap-control")
            Assert.Equal((ulong)uint.MaxValue + 1, (ulong)destination + requestedBytes - 1);
        if (scenario == "disp43-default-ham-setaa")
            Assert.NotEqual(0u, expectedMode);
        if (chipRevision.HasValue)
            fixture.SeedChipRevision(graphicsBase, chipRevision.Value);

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(tag, handle, modeId, requestedBytes, supportsEcsOracle, supportsAgaOracle);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);
            Assert.Equal(BuildQueryHeader(tag, expectedMode), expectedBytes.Take(16));
            if (tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes == 43)
            {
                var expectedSpriteResolution = ExpectedDisplaySpriteResolution(expectedMode);
                var expectedPaletteRange = ExpectedDisplayPaletteRange(expectedMode, supportsAgaOracle);
                Assert.Equal(ExpectedDisplayComponentBits(expectedMode, supportsAgaOracle), expectedBytes[41]);
                Assert.Equal(ExpectedDisplayComponentBits(expectedMode, supportsAgaOracle), expectedBytes[42]);
                Assert.Equal(ExpectedDisplayComponentBits(expectedMode, supportsAgaOracle), expectedBytes[40]);
                Assert.Equal((byte)0, expectedBytes[39]);
                Assert.Equal((byte)0, expectedBytes[38]);
                Assert.Equal((byte)0, expectedBytes[37]);
                Assert.Equal((byte)0, expectedBytes[36]);
                Assert.Equal((byte)0, expectedBytes[34]);
                Assert.Equal((byte)1, expectedBytes[35]);
                Assert.Equal((byte)0, expectedBytes[32]);
                Assert.Equal((byte)expectedSpriteResolution, expectedBytes[33]);
                Assert.Equal((byte)(expectedPaletteRange >> 8), expectedBytes[30]);
                Assert.Equal((byte)(expectedPaletteRange & 0x00FF), expectedBytes[31]);
                Assert.Equal((byte)8, expectedBytes[29]);
                Assert.Equal((byte)0, expectedBytes[28]);
                Assert.Equal((byte)(ExpectedDisplayPixelSpeed(expectedMode) & 0x00FF), expectedBytes[27]);
                Assert.Equal((byte)0, expectedBytes[26]);
                Assert.Equal((byte)1, expectedBytes[25]);
                Assert.Equal((byte)0, expectedBytes[24]);
                Assert.Equal(ExpectedDisplayByte17(expectedMode), expectedBytes[23]);
                Assert.Equal((byte)0, expectedBytes[22]);
                Assert.Equal(ExpectedDisplayByte15(expectedMode), expectedBytes[21]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 ? (byte)0x01 :
                    (expectedMode & GraphicsModeIds.ExtraHalfBriteMode) != 0 ? (byte)0x31 : (byte)0x21, expectedBytes[20]);
                Assert.Equal(supportsAgaOracle ? (byte)0x11 : (byte)0x10, expectedBytes[19]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 && !supportsEcsOracle ? (byte)1 : (byte)0, expectedBytes[17]);
            }
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes ? fixture.ReadOutputByte(followingAddress) : (byte)0;
        var result = fixture.Invoke(handle, destination, requestedBytes, tag, modeId, graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        var expectedCapabilityReads = logicalCapabilityReads;
        Check(failures, "result and terminal provenance", () =>
        {
            Assert.Equal(expectedResult, result.Data0); Assert.False(result.UsedFallback); Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned) Assert.Equal(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
            else if (publishes) Assert.Equal(AddQuickFourLongA7Opcode, result.NativeReturnPredecessorOpcode);
            else if (ownedZero) Assert.Equal(MoveQuickZeroD0Opcode, result.NativeReturnPredecessorOpcode);
            else if (malformedForeignSpan) Assert.NotEqual(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
        });
        Check(failures, "capability/default-monitor reads", () => { Assert.Equal(expectedCapabilityReads, result.CapabilityReadCount); Assert.Equal(0, result.DefaultMonitorReadCount); });
        Check(failures, "caller ABI", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter); Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1); Assert.Equal(destination, result.Address1); Assert.Equal(graphicsBase, result.Address6);
            if (publishes && tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes is 27 or 28 or 29 or 30 or 31 or 32 or 33 or 34 or 35 or 36 or 37 or 38 or 39 or 40 or 41 or 42 or 43)
                Assert.Equal((ushort)0, (ushort)(result.StatusRegister & M68kCpuState.Extend));
            Assert.Empty(result.RegisterDifferences);
        });
        if (publishes)
        {
            Check(failures, "exact portable prefix", () => fixture.AssertOnlyOutputPrefixChanged(before, after, destination, expectedBytes));
            Check(failures, "following byte untouched", () => Assert.Equal(followingByte, fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "memory intact", () => AssertMemoryEqual(before, after));
            Check(failures, "no destination access", () => AssertNoDestinationAccess(result, destination, requestedBytes));
        }
        Assert.True(failures.Count == 0, $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    private static void AssertDisplayFortyThreeBytePortableSeam()
    {
        AssertDisplayComponentPrefixPortableSeam(43);
    }

    [Theory]
    [MemberData(nameof(DisplayReservedTailCases))]
    public void GetDisplayInfoDataPublishesEveryReservedDisplayTailPrefix(
        bool relocated,
        bool autoInitEntry,
        uint prefixSize,
        string scenario)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        AssertDisplayReservedTailPortableSeam(prefixSize);
        var transferSize = Math.Min(prefixSize, 48u);

        using var fixture = new Fixture(relocated, autoInitEntry);
        var maximumCapabilityBase = uint.MaxValue - (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        var lastAlignedCapabilityBase = maximumCapabilityBase & ~1u;
        var firstWrapCapabilityBase = maximumCapabilityBase + 1u;
        var handle = ModeId;
        var destination = 0x300u;
        var requestedBytes = prefixSize;
        var tag = GraphicsDisplayDatabase.DtagDisp;
        var modeId = foreignMode;
        var graphicsBase = fixture.GraphicsBase;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var publishes = false;
        var providerOwned = false;
        var ownedZero = false;
        var malformedForeignSpan = false;
        var supportsEcsOracle = true;
        var supportsAgaOracle = false;
        byte? chipRevision = null;
        var logicalCapabilityReads = 0;

        void Publish(uint mode, byte chip, bool ecs, bool aga)
        {
            publishes = true;
            handle = mode;
            expectedMode = mode;
            chipRevision = chip;
            supportsEcsOracle = ecs;
            supportsAgaOracle = aga;
            logicalCapabilityReads = 1;
        }

        switch (scenario)
        {
            case "tail-pal-ordinary-even-ecs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "tail-ntsc-ordinary-odd-alice": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresKey, (byte)GraphicsChipRevision.AaAlice, false, false); destination = 0x301; break;
            case "tail-ntsc-superhires-lace-exact-end-lisa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresLaceKey, (byte)GraphicsChipRevision.AaLisa, false, false); destination = LastBase((int)transferSize); break;
            case "tail-null-ntsc-ehb-lace-aa-pair":
                expectedMode = GraphicsModeIds.NtscMonitor | GraphicsModeIds.ExtraHalfBriteLaceKey;
                Publish(expectedMode, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true);
                handle = 0; modeId = expectedMode; break;
            case "tail-private-default-aa-pair": Publish(GraphicsModeIds.DefaultMonitor, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); handle = GraphicsDisplayDatabase.DefaultModeHandle; break;
            case "tail-pal-dpf-last-aligned-setaa": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresDualPlayfieldKey, (byte)GraphicsChipRevision.SetAa, true, true); graphicsBase = lastAlignedCapabilityBase; break;
            case "tail-ntsc-dpf2-first-even-ocs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresDualPlayfieldTwoKey, 0, false, false); graphicsBase = 2; break;
            case "tail-default-ham-setaa": Publish(GraphicsModeIds.DefaultMonitor | GraphicsModeIds.HamKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "tail-ntsc-superhires-setaa": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetAa, true, true); break;
            case "tail-pal-superhires-ocs": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, 0, false, false); graphicsBase = 2; break;
            case "tail-ntsc-superhires-ecs": Publish(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "tail-pal-superhires-dpf2-lace-aa-pair": Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresDualPlayfieldTwoLaceKey, (byte)(GraphicsChipRevision.AaAlice | GraphicsChipRevision.AaLisa), false, true); break;
            case "tail-foreign-provider": handle = foreignMode; modeId = ModeId; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "tail-null-a6-control": graphicsBase = 0; break;
            case "tail-odd-a6-control": graphicsBase = 3; break;
            case "tail-exact-end-a6-control": graphicsBase = maximumCapabilityBase; break;
            case "tail-first-wrap-a6-control": graphicsBase = firstWrapCapabilityBase; break;
            case "tail-first-wrap-control": destination = unchecked(LastBase((int)transferSize) + 1u); graphicsBase = uint.MaxValue; break;
            case "tail-superhires-first-wrap-control": handle = GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey; expectedMode = handle; destination = unchecked(LastBase((int)transferSize) + 1u); break;
            case "tail-foreign-first-wrap-control": handle = foreignMode; modeId = ModeId; destination = unchecked(LastBase((int)transferSize) + 1u); graphicsBase = uint.MaxValue; malformedForeignSpan = true; break;
            case "disp42-ordinary-valid-control": requestedBytes = 42; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "disp43-superhires-valid-control": requestedBytes = 43; Publish(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, (byte)GraphicsChipRevision.SetEcs, true, false); break;
            case "disp57-control": requestedBytes = 57; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "disp56-control": requestedBytes = 56; publishes = true; chipRevision = (byte)GraphicsChipRevision.SetEcs; logicalCapabilityReads = 1; break;
            case "name-tail-control": tag = GraphicsDisplayDatabase.DtagName; publishes = true; graphicsBase = uint.MaxValue; break;
            case "dims-tail-control": tag = GraphicsDisplayDatabase.DtagDims; graphicsBase = uint.MaxValue; break;
            case "mntr-tail-control": tag = GraphicsDisplayDatabase.DtagMntr; graphicsBase = uint.MaxValue; break;
            case "unsupported-tail-control": tag = UnsupportedTag; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            case "vec-tail-provider-control": tag = GraphicsDisplayDatabase.DtagVec; graphicsBase = uint.MaxValue; providerOwned = true; break;
            case "sentinel-tail-control": handle = 0; modeId = GraphicsModeIds.Invalid; graphicsBase = uint.MaxValue; expectedResult = 0; ownedZero = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "tail-ntsc-superhires-lace-exact-end-lisa")
            Assert.Equal((ulong)uint.MaxValue, (ulong)destination + transferSize - 1);
        if (scenario is "tail-first-wrap-control" or "tail-superhires-first-wrap-control" or "tail-foreign-first-wrap-control")
            Assert.Equal((ulong)uint.MaxValue + 1, (ulong)destination + transferSize - 1);
        if (scenario == "tail-default-ham-setaa")
            Assert.NotEqual(0u, expectedMode);
        if (chipRevision.HasValue)
            fixture.SeedChipRevision(graphicsBase, chipRevision.Value);

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(tag, handle, modeId, requestedBytes, supportsEcsOracle, supportsAgaOracle);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);
            Assert.Equal(BuildQueryHeader(tag, expectedMode), expectedBytes.Take(16));
            if (tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes == prefixSize)
            {
                var expectedSpriteResolution = ExpectedDisplaySpriteResolution(expectedMode);
                var expectedPaletteRange = ExpectedDisplayPaletteRange(expectedMode, supportsAgaOracle);
                for (var tailOffset = 0x2B; tailOffset < Math.Min((int)prefixSize, 48); tailOffset++)
                    Assert.Equal((byte)0, expectedBytes[tailOffset]);
                Assert.Equal(ExpectedDisplayComponentBits(expectedMode, supportsAgaOracle), expectedBytes[41]);
                Assert.Equal(ExpectedDisplayComponentBits(expectedMode, supportsAgaOracle), expectedBytes[42]);
                Assert.Equal(ExpectedDisplayComponentBits(expectedMode, supportsAgaOracle), expectedBytes[40]);
                Assert.Equal((byte)0, expectedBytes[39]);
                Assert.Equal((byte)0, expectedBytes[38]);
                Assert.Equal((byte)0, expectedBytes[37]);
                Assert.Equal((byte)0, expectedBytes[36]);
                Assert.Equal((byte)0, expectedBytes[34]);
                Assert.Equal((byte)1, expectedBytes[35]);
                Assert.Equal((byte)0, expectedBytes[32]);
                Assert.Equal((byte)expectedSpriteResolution, expectedBytes[33]);
                Assert.Equal((byte)(expectedPaletteRange >> 8), expectedBytes[30]);
                Assert.Equal((byte)(expectedPaletteRange & 0x00FF), expectedBytes[31]);
                Assert.Equal((byte)8, expectedBytes[29]);
                Assert.Equal((byte)0, expectedBytes[28]);
                Assert.Equal((byte)(ExpectedDisplayPixelSpeed(expectedMode) & 0x00FF), expectedBytes[27]);
                Assert.Equal((byte)0, expectedBytes[26]);
                Assert.Equal((byte)1, expectedBytes[25]);
                Assert.Equal((byte)0, expectedBytes[24]);
                Assert.Equal(ExpectedDisplayByte17(expectedMode), expectedBytes[23]);
                Assert.Equal((byte)0, expectedBytes[22]);
                Assert.Equal(ExpectedDisplayByte15(expectedMode), expectedBytes[21]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 ? (byte)0x01 :
                    (expectedMode & GraphicsModeIds.ExtraHalfBriteMode) != 0 ? (byte)0x31 : (byte)0x21, expectedBytes[20]);
                Assert.Equal(supportsAgaOracle ? (byte)0x11 : (byte)0x10, expectedBytes[19]);
                Assert.Equal((expectedMode & GraphicsModeIds.SuperHiresMode) != 0 && !supportsEcsOracle ? (byte)1 : (byte)0, expectedBytes[17]);
            }
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes ? fixture.ReadOutputByte(followingAddress) : (byte)0;
        var result = fixture.Invoke(handle, destination, requestedBytes, tag, modeId, graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        var expectedCapabilityReads = logicalCapabilityReads;
        Check(failures, "result and terminal provenance", () =>
        {
            Assert.Equal(expectedResult, result.Data0); Assert.False(result.UsedFallback); Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned) Assert.Equal(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
            else if (publishes) Assert.Equal(AddQuickFourLongA7Opcode, result.NativeReturnPredecessorOpcode);
            else if (ownedZero) Assert.Equal(MoveQuickZeroD0Opcode, result.NativeReturnPredecessorOpcode);
            else if (malformedForeignSpan) Assert.NotEqual(BranchAlwaysWordOpcode, result.NativeReturnPredecessorOpcode);
        });
        Check(failures, "capability/default-monitor reads", () => { Assert.Equal(expectedCapabilityReads, result.CapabilityReadCount); Assert.Equal(0, result.DefaultMonitorReadCount); });
        Check(failures, "caller ABI", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter); Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1); Assert.Equal(destination, result.Address1); Assert.Equal(graphicsBase, result.Address6);
            if (publishes && tag == GraphicsDisplayDatabase.DtagDisp && requestedBytes is >= 27 and <= 55)
                Assert.Equal((ushort)0, (ushort)(result.StatusRegister & M68kCpuState.Extend));
            Assert.Empty(result.RegisterDifferences);
        });
        if (publishes)
        {
            Check(failures, "exact portable prefix", () => fixture.AssertOnlyOutputPrefixChanged(before, after, destination, expectedBytes));
            Check(failures, "following byte untouched", () => Assert.Equal(followingByte, fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "memory intact", () => AssertMemoryEqual(before, after));
            Check(failures, "no destination access", () => AssertNoDestinationAccess(result, destination, requestedBytes));
        }
        Assert.True(failures.Count == 0, $"{fixture.Route}/N={prefixSize}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void GetDisplayInfoDataReservedTailLoopSmoke()
    {
        // Exercise the new loop before the full route matrix: one, six and
        // twelve stores, exact-end odd/even destinations and each precision.
        foreach (uint size in new uint[] { 44, 49, 55 })
        foreach (var scenario in new[]
        {
            "tail-ntsc-superhires-lace-exact-end-lisa",
            "tail-ntsc-superhires-ecs",
            "tail-ntsc-superhires-setaa"
        })
            GetDisplayInfoDataPublishesEveryReservedDisplayTailPrefix(false, false, size, scenario);
    }

    private static void AssertDisplayReservedTailPortableSeam(uint size)
    {
        Assert.InRange(size, 44u, 55u);
        AssertDisplayComponentPrefixPortableSeam(size);
        var modeCount = 0;
        var pairCount = 0;
        for (var mode = GraphicsDisplayDatabase.NextDisplayInfo(GraphicsModeIds.Invalid);
             mode != GraphicsModeIds.Invalid;
             mode = GraphicsDisplayDatabase.NextDisplayInfo(mode))
        {
            foreach (var ecs in new[] { false, true })
            foreach (var aga in new[] { false, true })
            {
                var full = BuildPortableQuery(GraphicsDisplayDatabase.DtagDisp, mode, mode, 56, ecs, aga);
                var partial = BuildPortableQuery(GraphicsDisplayDatabase.DtagDisp, mode, mode, size, ecs, aga);
                Assert.Equal(Math.Min(size, 48u), partial.Result);
                Assert.Equal(Math.Min((int)size, 48), partial.Bytes.Length);
                Assert.Equal(partial.Bytes, full.Bytes.Take((int)size));
                Assert.All(full.Bytes.Skip(0x2B), value => Assert.Equal((byte)0, value));
                Assert.All(partial.Bytes.Skip(0x2B), value => Assert.Equal((byte)0, value));
                pairCount++;
            }
            modeCount++;
        }
        Assert.Equal(66, modeCount);
        Assert.Equal(264, pairCount);
    }

    [Theory]
    [MemberData(nameof(DimensionHeaderPrefixCases))]
    public void GetDisplayInfoDataPublishesEveryDimensionQueryHeaderPrefixLength(
        bool relocated,
        bool autoInitEntry,
        string scenario,
        uint requestedBytes)
        => AssertQueryHeaderPrefix(relocated, autoInitEntry, scenario, requestedBytes,
            GraphicsDisplayDatabase.DtagDims);

    [Theory]
    [MemberData(nameof(MonitorHeaderPrefixCases))]
    public void GetDisplayInfoDataPublishesEveryMonitorQueryHeaderPrefixLength(
        bool relocated,
        bool autoInitEntry,
        string scenario,
        uint requestedBytes)
        => AssertQueryHeaderPrefix(relocated, autoInitEntry, scenario, requestedBytes,
            GraphicsDisplayDatabase.DtagMntr);

    private static void AssertQueryHeaderPrefix(
        bool relocated,
        bool autoInitEntry,
        string scenario,
        uint requestedBytes,
        uint headerTag)
    {
        const ushort BranchAlwaysWordOpcode = 0x6000;
        const ushort AddQuickFourLongA7Opcode = 0x588F;
        const ushort MoveQuickZeroD0Opcode = 0x7000;
        const uint foreignMode = 0xDEAD_BEEFu;
        var finalCanonicalMode =
            GraphicsModeIds.NtscMonitor |
            GraphicsModeIds.ExtraHalfBriteLaceKey;
        using var fixture = new Fixture(relocated, autoInitEntry);
        var handle = ModeId;
        var destination = (requestedBytes & 1u) == 0 ? 0x300u : 0x301u;
        var tag = headerTag;
        var modeId = foreignMode;
        var graphicsBase = uint.MaxValue;
        var expectedMode = ModeId;
        var expectedResult = requestedBytes;
        var publishes = scenario == "header";
        var providerOwned = false;
        var ownedZero = false;

        switch (scenario)
        {
            case "header": break;
            case "exact-end":
                publishes = true;
                destination = LastBase((int)requestedBytes);
                break;
            case "null-a6":
                publishes = true;
                graphicsBase = 0;
                break;
            case "superhires-odd-a6":
                publishes = true;
                graphicsBase = 3;
                handle = GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey;
                expectedMode = handle;
                break;
            case "null-handle-final-canonical":
                publishes = true;
                handle = 0;
                modeId = finalCanonicalMode;
                expectedMode = finalCanonicalMode;
                break;
            case "private-default":
                publishes = true;
                handle = GraphicsDisplayDatabase.DefaultModeHandle;
                expectedMode = GraphicsModeIds.DefaultMonitor;
                break;
            case "foreign-provider":
                handle = foreignMode;
                modeId = ModeId;
                providerOwned = true;
                break;
            case "first-wrap":
                destination = unchecked(LastBase((int)requestedBytes) + 1u);
                break;
            case "foreign-first-wrap":
                destination = unchecked(LastBase((int)requestedBytes) + 1u);
                handle = foreignMode;
                modeId = ModeId;
                break;
            case "zero-size":
                ownedZero = true;
                break;
            case "null-destination":
                destination = 0;
                expectedResult = 0;
                ownedZero = true;
                break;
            case "dims17-control":
                publishes = true;
                break;
            case "dims87-control":
            case "mntr17-control":
            case "mntr95-control":
                break;
            case "full-dims88-control":
                publishes = true;
                tag = GraphicsDisplayDatabase.DtagDims;
                graphicsBase = fixture.GraphicsBase;
                break;
            case "dims16-control":
                publishes = true;
                tag = GraphicsDisplayDatabase.DtagDims;
                break;
            case "disp16-control":
                publishes = true;
                tag = GraphicsDisplayDatabase.DtagDisp;
                break;
            case "name16-control":
                publishes = true;
                tag = GraphicsDisplayDatabase.DtagName;
                break;
            case "mntr16-control":
                publishes = true;
                tag = GraphicsDisplayDatabase.DtagMntr;
                break;
            case "unsupported-control":
                tag = UnsupportedTag;
                expectedResult = 0;
                ownedZero = true;
                break;
            case "vec-provider-control":
                tag = GraphicsDisplayDatabase.DtagVec;
                providerOwned = true;
                break;
            case "sentinel-control":
                handle = 0;
                modeId = GraphicsModeIds.Invalid;
                expectedResult = 0;
                ownedZero = true;
                break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        if (scenario == "exact-end")
            Assert.Equal((ulong)uint.MaxValue, (ulong)destination + requestedBytes - 1);
        if (scenario is "first-wrap" or "foreign-first-wrap")
            Assert.Equal((ulong)uint.MaxValue + 1, (ulong)destination + requestedBytes - 1);

        var expectedBytes = Array.Empty<byte>();
        if (publishes)
        {
            var portable = BuildPortableQuery(tag, handle, modeId, requestedBytes);
            expectedResult = portable.Result;
            expectedBytes = portable.Bytes;
            Assert.Equal(checked((int)expectedResult), expectedBytes.Length);

            var expectedHeader = BuildQueryHeader(tag, expectedMode);
            var expectedHeaderBytes = Math.Min(expectedHeader.Length, expectedBytes.Length);
            Assert.Equal(
                expectedHeader.Take(expectedHeaderBytes),
                expectedBytes.Take(expectedHeaderBytes));
            if (requestedBytes <= 16u)
                Assert.Equal(checked((int)requestedBytes), expectedBytes.Length);
        }

        var before = fixture.CaptureMemory();
        var followingAddress = unchecked(destination + (uint)expectedBytes.Length);
        var followingByte = publishes
            ? fixture.ReadOutputByte(followingAddress)
            : (byte)0;
        var result = fixture.Invoke(
            handle,
            destination,
            requestedBytes,
            tag,
            modeId,
            graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();

        Check(failures, "result and native/provider terminal", () =>
        {
            Assert.Equal(expectedResult, result.Data0);
            Assert.False(result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
            if (providerOwned)
            {
                Assert.Equal(
                    BranchAlwaysWordOpcode,
                    result.NativeReturnPredecessorOpcode);
            }
            else if (publishes)
            {
                Assert.Equal(
                    AddQuickFourLongA7Opcode,
                    result.NativeReturnPredecessorOpcode);
            }
            else if (ownedZero)
            {
                Assert.Equal(
                    MoveQuickZeroD0Opcode,
                    result.NativeReturnPredecessorOpcode);
            }
        });
        Check(failures, "caller PC/SP, D1, A1, and supplied A6", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(tag, result.Data1);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(graphicsBase, result.Address6);
        });
        Check(failures, "D2-D7/A2-A6 public frame", () =>
            Assert.True(
                result.RegisterDifferences.Length == 0,
                string.Join("; ", result.RegisterDifferences)));
        Check(failures, "only the full DIMS control reads capabilities", () =>
        {
            Assert.Equal(scenario == "full-dims88-control" ? 1 : 0, result.CapabilityReadCount);
            Assert.Equal(0, result.DefaultMonitorReadCount);
        });

        if (publishes)
        {
            Check(failures, "only N exact portable bytes change", () =>
                fixture.AssertOnlyOutputPrefixChanged(
                    before,
                    after,
                    destination,
                    expectedBytes));
            Check(failures, "the byte following the output remains untouched", () =>
                Assert.Equal(
                    followingByte,
                    fixture.ReadOutputByte(followingAddress)));
        }
        else
        {
            Check(failures, "control or provider-owned calls leave memory intact", () =>
                AssertMemoryEqual(before, after));
            Check(failures, "control or provider-owned calls never access A1", () =>
                AssertNoDestinationAccess(
                    result,
                    destination,
                    Math.Max(1u, requestedBytes)));
        }

        Assert.True(
            failures.Count == 0,
            $"{fixture.Route}/{scenario}/N={requestedBytes}:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void GetDisplayInfoDataDimensionHeadersMatchPortableOracle()
    {
        var modes = 0;
        var combinations = 0;
        for (var mode = GraphicsDisplayDatabase.NextDisplayInfo(GraphicsModeIds.Invalid);
             mode != GraphicsModeIds.Invalid;
             mode = GraphicsDisplayDatabase.NextDisplayInfo(mode))
        {
            var recordId = (mode & 0xFFFF1000u) == 0 ? mode | 0x00021000u : mode;
            var expectedHeader = BuildQueryHeader(GraphicsDisplayDatabase.DtagDims, recordId);
            Assert.Equal(new byte[] { 0x80, 0, 0x10, 0 }, expectedHeader.Take(4));
            Assert.Equal(new byte[] { 0, 0, 0, 3, 0, 0, 0, 8 }, expectedHeader.Skip(8));
            foreach (var ecs in new[] { false, true })
            foreach (var aga in new[] { false, true })
            {
                var full = BuildPortableQuery(GraphicsDisplayDatabase.DtagDims, mode, mode, 88, ecs, aga);
                Assert.Equal(66u, full.Result);
                Assert.Equal(66, full.Bytes.Length);
                Assert.Equal(expectedHeader, full.Bytes.Take(16));
                for (uint size = 1; size <= 16; size++)
                {
                    var partial = BuildPortableQuery(GraphicsDisplayDatabase.DtagDims, mode, mode, size, ecs, aga);
                    Assert.Equal(size, partial.Result);
                    Assert.Equal((int)size, partial.Bytes.Length);
                    Assert.Equal(expectedHeader.Take((int)size), partial.Bytes);
                    Assert.Equal(full.Bytes.Take((int)size), partial.Bytes);
                    combinations++;
                }
            }
            modes++;
        }
        Assert.Equal(66, modes);
        Assert.Equal(4224, combinations);
    }

    [Fact]
    public void GetDisplayInfoDataMonitorHeadersMatchPortableOracle()
    {
        var modes = 0;
        var combinations = 0;
        for (var mode = GraphicsDisplayDatabase.NextDisplayInfo(GraphicsModeIds.Invalid);
             mode != GraphicsModeIds.Invalid;
             mode = GraphicsDisplayDatabase.NextDisplayInfo(mode))
        {
            var recordId = (mode & 0xFFFF1000u) == 0 ? mode | 0x00021000u : mode;
            var header = BuildQueryHeader(GraphicsDisplayDatabase.DtagMntr, recordId);
            Assert.Equal(new byte[] { 0x80, 0, 0x20, 0 }, header.Take(4));
            Assert.Equal(new byte[] { 0, 0, 0, 3, 0, 0, 0, 9 }, header.Skip(8));
            foreach (var ecs in new[] { false, true })
            foreach (var aga in new[] { false, true })
            {
                var full = BuildPortableQuery(GraphicsDisplayDatabase.DtagMntr, mode, mode, 96, ecs, aga);
                Assert.Equal(88u, full.Result);
                Assert.Equal(88, full.Bytes.Length);
                Assert.Equal(header, full.Bytes.Take(16));
                for (uint size = 1; size <= 16; size++)
                {
                    var partial = BuildPortableQuery(GraphicsDisplayDatabase.DtagMntr, mode, mode, size, ecs, aga);
                    Assert.Equal(size, partial.Result);
                    Assert.Equal(header.Take((int)size), partial.Bytes);
                    Assert.Equal(full.Bytes.Take((int)size), partial.Bytes);
                    combinations++;
                }
            }
            modes++;
        }
        Assert.Equal(66, modes);
        Assert.Equal(4224, combinations);
    }

    [Theory]
    [InlineData(0x80000000u, 4)]
    [InlineData(0x80001000u, 8)]
    [InlineData(0x80002000u, 9)]
    public void PortableQueryHeaderLengthMatchesCapturedKickstartRecord(uint tag, byte expected)
    {
        // Independent A500 PAL OCS ROM captures establish these values. This
        // test covers Length only; identities and transfer counts are separate.
        foreach (var ecs in new[] { false, true })
        foreach (var aga in new[] { false, true })
        for (var mode = GraphicsDisplayDatabase.NextDisplayInfo(GraphicsModeIds.Invalid);
             mode != GraphicsModeIds.Invalid;
             mode = GraphicsDisplayDatabase.NextDisplayInfo(mode))
        {
            var query = BuildPortableQuery(tag, mode, mode, 16, ecs, aga);
            Assert.Equal(new byte[] { 0, 0, 0, expected }, query.Bytes.Skip(12).Take(4));
        }
    }

    public static IEnumerable<object[]> CapturedHeaderLengthNativeCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInit in new[] { false, true })
        foreach (var (tag, length, sizes) in new[]
        {
            (0x80000000u, (byte)4, new uint[] { 16, 17, 18, 40, 48, 55, 56 }),
            (0x80001000u, (byte)8, new uint[] { 16, 88 }),
            (0x80002000u, (byte)9, new uint[] { 16, 96 })
        })
        foreach (var size in sizes)
            yield return new object[] { relocated, autoInit, tag, length, size };
    }

    [Theory]
    [MemberData(nameof(CapturedHeaderLengthNativeCases))]
    public void NativeQueryHeaderLengthMatchesCapturedKickstartRecord(
        bool relocated, bool autoInit, uint tag, byte expected, uint size)
    {
        using var fixture = new Fixture(relocated, autoInit);
        const uint buffer = 0x300;
        fixture.SeedDefaultMonitor(0x00600000);
        var handle = tag == GraphicsDisplayDatabase.DtagMntr
            ? GraphicsDisplayDatabase.DefaultModeHandle : ModeId;
        var result = fixture.Invoke(handle, buffer, size, tag, ModeId, fixture.GraphicsBase);
        Assert.False(result.UsedFallback);
        Assert.Equal(1, result.NativeReturnCount);
        Assert.Empty(result.RegisterDifferences);
        Assert.Equal(new byte[] { 0, 0, 0, expected },
            Enumerable.Range(12, 4).Select(offset => fixture.ReadOutputByte(buffer + (uint)offset)));
    }

    public static IEnumerable<object[]> ExplicitMonitorHeaderIdentityCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInit in new[] { false, true })
        foreach (var monitor in new uint[] { 0x00011000, 0x00021000 })
        foreach (var key in new uint[] { 0, 0x8000, 0x8020, 0x80 })
        for (uint size = 1; size <= 16; size++)
            yield return new object[] { relocated, autoInit, monitor, key, size };
    }

    [Theory]
    [MemberData(nameof(ExplicitMonitorHeaderIdentityCases))]
    public void NativeMonitorHeaderUsesExplicitMonitorOwnerIdentity(
        bool relocated, bool autoInit, uint monitor, uint key, uint size)
    {
        using var fixture = new Fixture(relocated, autoInit);
        var mode = monitor | key;
        var destination = (size & 1) == 0 ? 0x300u : 0x301u;
        var graphicsBase = (size & 1) == 0 ? 0u : uint.MaxValue;
        var before = fixture.CaptureMemory();
        var expected = BuildQueryHeader(GraphicsDisplayDatabase.DtagMntr, monitor).Take((int)size).ToArray();
        var result = fixture.Invoke(mode, destination, size,
            GraphicsDisplayDatabase.DtagMntr, 0xDEADBEEF, graphicsBase);
        Assert.Equal(size, result.Data0);
        Assert.False(result.UsedFallback);
        Assert.Equal(1, result.NativeReturnCount);
        Assert.Equal((ushort)0x588F, result.NativeReturnPredecessorOpcode);
        Assert.Empty(result.RegisterDifferences);
        Assert.Equal(Fixture.StackPointer, result.StackPointer);
        Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
        Assert.Equal(destination, result.Address1);
        Assert.Equal(graphicsBase, result.Address6);
        Assert.Equal(GraphicsDisplayDatabase.DtagMntr, result.Data1);
        Assert.Equal(0, result.CapabilityReadCount);
        Assert.Equal(0, result.DefaultMonitorReadCount);
        fixture.AssertOnlyOutputPrefixChanged(before, fixture.CaptureMemory(), destination, expected);
    }

    [Fact]
    public void PortableMonitorHeadersUseExplicitMonitorOwnerIdentity()
    {
        var modes = 0;
        var queries = 0;
        for (var mode = GraphicsDisplayDatabase.NextDisplayInfo(GraphicsModeIds.Invalid);
             mode != GraphicsModeIds.Invalid;
             mode = GraphicsDisplayDatabase.NextDisplayInfo(mode))
        {
            var monitor = mode & 0xFFFF1000u;
            if (monitor == 0)
                continue; // Default-alias profile resolution is the next unit.
            Assert.Contains(monitor, new uint[] { 0x00011000, 0x00021000 });
            var header = BuildQueryHeader(GraphicsDisplayDatabase.DtagMntr, monitor);
            foreach (var ecs in new[] { false, true })
            foreach (var aga in new[] { false, true })
            {
                var full = BuildPortableQuery(GraphicsDisplayDatabase.DtagMntr, mode, mode, 96, ecs, aga);
                Assert.Equal(header, full.Bytes.Take(16));
                for (uint size = 1; size <= 16; size++)
                {
                    var partial = BuildPortableQuery(GraphicsDisplayDatabase.DtagMntr, mode, mode, size, ecs, aga);
                    Assert.Equal(header.Take((int)size), partial.Bytes);
                    queries++;
                }
            }
            modes++;
        }
        Assert.Equal(44, modes);
        Assert.Equal(2816, queries);
    }

    [Theory]
    [InlineData(0x80000000u, false)]
    [InlineData(0x80000000u, true)]
    [InlineData(0x80001000u, false)]
    [InlineData(0x80001000u, true)]
    [InlineData(0x80002000u, false)]
    [InlineData(0x80002000u, true)]
    public void PortableDefaultAliasHeadersPublishSelectedMonitorIdentity(uint tag, bool ntsc)
    {
        var selectedMonitor = ntsc ? 0x00011000u : 0x00021000u;
        var modes = 0;
        var queries = 0;
        for (var mode = GraphicsDisplayDatabase.NextDisplayInfo(GraphicsModeIds.Invalid);
             mode != GraphicsModeIds.Invalid;
             mode = GraphicsDisplayDatabase.NextDisplayInfo(mode))
        {
            if ((mode & 0xFFFF1000u) != 0)
                continue;
            var expectedId = tag == 0x80002000u ? selectedMonitor : selectedMonitor | mode;
            var header = BuildQueryHeader(tag, expectedId);
            foreach (var ecs in new[] { false, true })
            foreach (var aga in new[] { false, true })
            foreach (var viaHandle in new[] { false, true })
            foreach (var size in Enumerable.Range(0, 17).Select(value => (uint)value).Append(128u))
            {
                var memory = new NameOracleMemory();
                var providerModes = new List<uint>();
                var handle = viaHandle
                    ? (mode == 0 ? GraphicsDisplayDatabase.DefaultModeHandle : mode) : 0;
                const uint destination = 0x41; // bytewise public output may be odd
                var copied = GraphicsDisplayDatabase.GetDisplayInfoData(
                    memory, handle, destination, size, tag, viaHandle ? 0xDEADBEEFu : mode,
                    monitorSpecProvider: providerMode => { providerModes.Add(providerMode); return 0x12345678; },
                    defaultMonitorNtsc: ntsc, supportsEcsDisplay: ecs, supportsAgaDisplay: aga);
                if (size <= 16)
                    Assert.Equal((int)size, copied);
                else
                    Assert.InRange(copied, 16, (int)size); // transfer-size correction is separate
                Assert.Equal(header.Take(Math.Min(copied, 16)),
                    memory.ReadBytes(destination, Math.Min(copied, 16)));
                Assert.All(memory.ReadBytes(0, (int)destination), value => Assert.Equal((byte)0xA5, value));
                Assert.All(memory.ReadBytes(destination + (uint)copied, 256 - (int)destination - copied),
                    value => Assert.Equal((byte)0xA5, value));
                // Header normalization must not silently change provider handle ownership.
                Assert.All(providerModes, providerMode => Assert.Equal(mode, providerMode));
                if (tag != 0x80002000u || size == 0)
                    Assert.Empty(providerModes);
                if (tag == 0x80002000u && size == 128)
                    Assert.Single(providerModes);
                queries++;
            }
            modes++;
        }
        Assert.Equal(22, modes);
        Assert.Equal(3168, queries);
    }

    public static IEnumerable<object[]> NativeDefaultAliasIdentityCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInit in new[] { false, true })
        foreach (var tag in new uint[] { 0x80000000, 0x80001000, 0x80002000 })
            yield return new object[] { relocated, autoInit, tag };
    }

    [Theory]
    [MemberData(nameof(NativeDefaultAliasIdentityCases))]
    public void NativeDefaultAliasHeadersMatchTheDefaultPalProfile(bool relocated, bool autoInit, uint tag)
    {
        // The existing default image profile is PAL. A configurable native
        // profile/layout path must additionally qualify NTSC; these cases do
        // not permit reading overlapping compact-image string bytes as flags.
        foreach (var key in new uint[] { 0, 0x8000, 0x8020, 0x80 })
        foreach (var size in new uint[] { 1, 5, 6, 16 })
        {
            using var fixture = new Fixture(relocated, autoInit);
            var handle = key == 0 ? GraphicsDisplayDatabase.DefaultModeHandle : key;
            var expectedId = tag == 0x80002000u ? 0x00021000u : 0x00021000u | key;
            var expected = BuildQueryHeader(tag, expectedId).Take((int)size).ToArray();
            var before = fixture.CaptureMemory();
            const uint destination = 0x301;
            var result = fixture.Invoke(handle, destination, size, tag, 0xDEADBEEF, fixture.GraphicsBase);
            Assert.Equal(size, result.Data0);
            Assert.False(result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
            Assert.Equal(tag, result.Data1);
            fixture.AssertOnlyOutputPrefixChanged(before, fixture.CaptureMemory(), destination, expected);
        }
    }

    public static IEnumerable<object[]> NativeDefaultPalPayloadIdentityCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInit in new[] { false, true })
        foreach (var key in new uint[] { 0, 0x8000, 0x8020, 0x80 })
        {
            foreach (var size in new uint[] { 17, 18, 40, 55, 56 })
                yield return new object[] { relocated, autoInit, key, 0x80000000u, size };
            yield return new object[] { relocated, autoInit, key, 0x80001000u, 88u };
            if (key == 0) // non-default native full MNTR lifecycle remains unimplemented
                yield return new object[] { relocated, autoInit, key, 0x80002000u, 96u };
        }
    }

    [Theory]
    [MemberData(nameof(NativeDefaultPalPayloadIdentityCases))]
    public void NativeDefaultPalPayloadAndFullRecordsMatchPortableIdentity(
        bool relocated, bool autoInit, uint key, uint tag, uint size)
    {
        using var fixture = new Fixture(relocated, autoInit);
        const uint monitor = 0x00600000;
        fixture.SeedDefaultMonitor(monitor);
        var handle = key == 0 ? GraphicsDisplayDatabase.DefaultModeHandle : key;
        var memory = new NameOracleMemory();
        var copied = GraphicsDisplayDatabase.GetDisplayInfoData(memory, handle, 0x20, size, tag,
            0xDEADBEEF, monitorSpecProvider: _ => monitor);
        Assert.True(copied > 16);
        var expected = memory.ReadBytes(0x20, copied);
        var before = fixture.CaptureMemory();
        const uint destination = 0x300;
        var result = fixture.Invoke(handle, destination, size, tag, 0xDEADBEEF, fixture.GraphicsBase);
        Assert.False(result.UsedFallback);
        Assert.Equal((uint)copied, result.Data0);
        Assert.Equal(1, result.NativeReturnCount);
        Assert.Empty(result.RegisterDifferences);
        Assert.Equal(Fixture.StackPointer, result.StackPointer);
        Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
        Assert.Equal(destination, result.Address1);
        Assert.Equal(tag, result.Data1);
        Assert.Equal(fixture.GraphicsBase, result.Address6);
        fixture.AssertOnlyOutputPrefixChanged(before, fixture.CaptureMemory(), destination, expected);
    }

    public static IEnumerable<object[]> NativeBootProfileCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInit in new[] { false, true })
        foreach (var ntsc in new[] { false, true })
        foreach (var tag in new uint[] { 0x80000000, 0x80001000, 0x80002000, 0x80003000 })
            yield return new object[] { relocated, autoInit, ntsc, tag };
    }

    [Theory]
    [MemberData(nameof(NativeBootProfileCases))]
    public void NativeBootProfileRecordsMatchPortableAndIgnoreLiveDisplayFlags(
        bool relocated, bool autoInit, bool ntsc, uint tag)
    {
        using var fixture = new Fixture(relocated, autoInit,
            ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal);
        const uint monitorPointer = 0x00600000;
        fixture.SeedDefaultMonitor(monitorPointer);
        foreach (var flipFlags in new[] { false, true })
        {
            fixture.SeedDisplayFlags((ushort)((ntsc ^ flipFlags) ? 1 : 4));
            foreach (var owner in new uint[] { 0, 0x00011000, 0x00021000 })
            foreach (var key in new uint[] { 0, 0x8000, 0x8020, 0x80 })
            {
                var mode = owner | key;
                var handle = mode == 0 ? GraphicsDisplayDatabase.DefaultModeHandle : mode;
                var sizes = Enumerable.Range(1, 16).Select(value => (uint)value).ToList();
                if (tag == 0x80000000)
                    sizes.AddRange(new uint[] { 17, 18, 22, 40, 47, 48, 49, 55, 56, 128, uint.MaxValue });
                if (tag == 0x80003000)
                    sizes.AddRange(new uint[] { 17, 31, 55, 56 });
                if (tag == 0x80001000)
                    sizes.AddRange(new uint[] { 66, 67, 88, 128, uint.MaxValue });
                if (tag == 0x80002000 && mode == 0)
                    sizes.AddRange(new uint[] { 88, 89, 96, 128, uint.MaxValue }); // nondefault ownership remains a separate lifecycle unit
                foreach (var size in sizes)
                {
                    var memory = new NameOracleMemory();
                    var copied = GraphicsDisplayDatabase.GetDisplayInfoData(memory, handle, 0x20, size, tag,
                        0xDEADBEEF, monitorSpecProvider: _ => monitorPointer, defaultMonitorNtsc: ntsc);
                    var expected = memory.ReadBytes(0x20, copied);
                    if (tag == 0x80002000 && copied == 88)
                    {
                        var captured = GraphicsMonitorInfoFamilyRecordTests.Baseline(ntsc);
                        captured[16] = 0; captured[17] = 0x60; captured[18] = 0; captured[19] = 0;
                        Assert.Equal(captured, expected);
                    }
                    var before = fixture.CaptureMemory();
                    var result = fixture.Invoke(handle, 0x300, size, tag, 0xDEADBEEF, fixture.GraphicsBase);
                    Assert.False(result.UsedFallback);
                    Assert.Equal((uint)copied, result.Data0);
                    Assert.Equal(0x300u, result.Address1);
                    Assert.Equal(fixture.GraphicsBase, result.Address6);
                    Assert.Equal(tag, result.Data1);
                    Assert.Equal(1, result.NativeReturnCount);
                    Assert.Empty(result.RegisterDifferences);
                    Assert.Equal(Fixture.StackPointer, result.StackPointer);
                    Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
                    if (size <= 16)
                    {
                        Assert.Equal(0, result.CapabilityReadCount);
                        Assert.Equal(0, result.DefaultMonitorReadCount);
                    }
                    fixture.AssertOnlyOutputPrefixChanged(before, fixture.CaptureMemory(), 0x300, expected);
                }
            }
        }
    }

    public static IEnumerable<object[]> NativeDispTransferEdgeCases()
        => NativeBootProfileCases().Where(row => (uint)row[3] == 0x80000000);

    [Theory]
    [MemberData(nameof(NativeDispTransferEdgeCases))]
    public void NativeDisplayInfoDataDispCapsOddAndFinalAddressesAndPreservesDeclines(
        bool relocated, bool autoInit, bool ntsc, uint tag)
    {
        using var fixture = new Fixture(relocated, autoInit,
            ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal);
        foreach (var requested in new uint[] { 48, 49, 56, uint.MaxValue })
        foreach (var scenario in new[] { "odd", "final-byte", "wrap", "foreign" })
        {
            var destination = scenario == "final-byte" ? 0xFFFF_FFD0u
                : scenario == "wrap" ? 0xFFFF_FFD1u : 0x301u;
            var mode = scenario == "foreign" ? 0xDEAD_BEEFu : 0x21000u;
            var declined = scenario is "wrap" or "foreign";
            var before = fixture.CaptureMemory();
            var result = fixture.Invoke(0, destination, requested, tag, mode);
            Assert.Equal(declined ? requested : 48u, result.Data0);
            Assert.Equal(tag, result.Data1);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(1, result.NativeReturnCount);
            if (declined)
            {
                AssertMemoryEqual(before, fixture.CaptureMemory());
                AssertNoDestinationAccess(result, destination, 48);
            }
            else
            {
                Assert.False(result.UsedFallback);
                var memory = new NameOracleMemory();
                Assert.Equal(48, GraphicsDisplayDatabase.GetDisplayInfoData(memory, 0, 0x20,
                    requested, tag, mode, defaultMonitorNtsc: ntsc));
                fixture.AssertOnlyOutputPrefixChanged(before, fixture.CaptureMemory(), destination,
                    memory.ReadBytes(0x20, 48));
            }
        }
    }

    public static IEnumerable<object[]> NativeDimsTransferEdgeCases()
        => NativeBootProfileCases().Where(row => (uint)row[3] == 0x80001000);

    [Theory]
    [MemberData(nameof(NativeDimsTransferEdgeCases))]
    public void NativeDisplayInfoDataDimsSeventeenAndEighteenBytePrefixesMatchRom(
        bool relocated, bool autoInit, bool ntsc, uint tag)
    {
        using var fixture = new Fixture(relocated, autoInit,
            ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal);
        foreach (var (key, depth) in new (uint, ushort)[] { (0, 5), (0x8000, 4), (0x0800, 6), (0x8020, 2) })
        foreach (var requested in new uint[] { 17, 18 })
        foreach (var destination in new uint[] { 0x300, 0x301, uint.MaxValue - requested + 1 })
        {
            fixture.SeedChipRevision(fixture.GraphicsBase, 3);
            var mode = (ntsc ? 0x11000u : 0x21000u) | key;
            var before = fixture.CaptureMemory();
            var result = fixture.Invoke(0, destination, requested, tag, mode);
            Assert.False(result.UsedFallback);
            Assert.Equal(requested, result.Data0);
            Assert.Equal((byte)0, fixture.ReadOutputByte(destination + 16));
            if (requested == 18)
                Assert.Equal((byte)depth, fixture.ReadOutputByte(destination + 17));
            Assert.Equal(tag, result.Data1);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(1, result.NativeReturnCount);
            var memory = new NameOracleMemory();
            Assert.Equal((int)requested, GraphicsDisplayDatabase.GetDisplayInfoData(memory, 0,
                0x20, requested, tag, mode, defaultMonitorNtsc: ntsc));
            fixture.AssertOnlyOutputPrefixChanged(before, fixture.CaptureMemory(), destination,
                memory.ReadBytes(0x20, (int)requested));
        }
    }

    [Theory]
    [MemberData(nameof(NativeDimsTransferEdgeCases))]
    public void NativeDisplayInfoDataDimsScalarPrefixesMatchCapturedRom(
        bool relocated, bool autoInit, bool ntsc, uint tag)
    {
        using var fixture = new Fixture(relocated, autoInit,
            ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal);
        foreach (var (key, depth) in GraphicsDisplayInfoTransferBoundaryTests.CapturedOcsEcsDepths)
        foreach (var owner in new uint[] { 0, ntsc ? 0x11000u : 0x21000u })
        foreach (var useHandle in new[] { false, true })
        foreach (var requested in Enumerable.Range(19, 15).Select(value => (uint)value))
        foreach (var destination in new uint[] { 0x300, 0x301, uint.MaxValue - Math.Min(requested, 26u) + 1 })
        {
            var copied = Math.Min(requested, 26u);
            fixture.SeedChipRevision(fixture.GraphicsBase, 3);
            var mode = owner | key;
            var handle = useHandle ? mode == 0 ? 0xFFFFFFFEu : mode : 0;
            var before = fixture.CaptureMemory();
            var result = fixture.Invoke(handle, destination, requested, tag, mode);
            Assert.False(result.UsedFallback);
            Assert.Equal(copied, result.Data0);
            Assert.Equal((byte)0, fixture.ReadOutputByte(destination + 16));
            Assert.Equal((byte)depth, fixture.ReadOutputByte(destination + 17));
            Assert.Equal(GraphicsDisplayInfoTransferBoundaryTests.CapturedRasterLimits(key).Take((int)copied - 18),
                Enumerable.Range(18, (int)copied - 18)
                    .Select(index => fixture.ReadOutputByte(destination + (uint)index)));
            Assert.Equal(tag, result.Data1);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(1, result.NativeReturnCount);
            var memory = new NameOracleMemory();
            Assert.Equal((int)copied, GraphicsDisplayDatabase.GetDisplayInfoData(memory,
                handle, 0x20, requested, tag, mode, defaultMonitorNtsc: ntsc));
            fixture.AssertOnlyOutputPrefixChanged(before, fixture.CaptureMemory(), destination,
                memory.ReadBytes(0x20, (int)copied));
        }
    }

    [Theory]
    [MemberData(nameof(NativeDimsTransferEdgeCases))]
    public void NativeDisplayInfoDataDimsScalarPrefixesRejectMalformedEnvelopes(
        bool relocated, bool autoInit, bool ntsc, uint tag)
    {
        using var fixture = new Fixture(relocated, autoInit,
            ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal);
        foreach (var requested in Enumerable.Range(19, 15).Select(value => (uint)value))
        foreach (var scenario in new[] { "first-wrap", "foreign-handle", "null-base", "odd-base", "wrapped-base" })
        {
            var destination = scenario == "first-wrap" ? uint.MaxValue - Math.Min(requested, 26u) + 2 : 0x301u;
            var handle = scenario == "foreign-handle" ? 0x50001000u : 0;
            var suppliedBase = scenario switch
            {
                "null-base" => 0u,
                "odd-base" => 1u,
                "wrapped-base" => 0xFFFFFF14u,
                _ => fixture.GraphicsBase
            };
            var before = fixture.CaptureMemory();
            var result = fixture.Invoke(handle, destination, requested, tag,
                (ntsc ? 0x11000u : 0x21000u) | 0x8000u, suppliedBase);
            Assert.Equal(requested, result.Data0);
            Assert.Equal(tag, result.Data1);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(suppliedBase, result.Address6);
            Assert.Equal(0, result.CapabilityReadCount);
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            AssertMemoryEqual(before, fixture.CaptureMemory());
            AssertNoDestinationAccess(result, destination, requested);
        }
    }

    [Theory]
    [MemberData(nameof(NativeDimsTransferEdgeCases))]
    public void NativeDisplayInfoDataDimsShortDepthCapabilityBoundaries(
        bool relocated, bool autoInit, bool ntsc, uint tag)
    {
        using var fixture = new Fixture(relocated, autoInit,
            ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal);
        foreach (var chip in new byte[] { 0, 2, 3, 4, 8, 12 })
        foreach (var (key, legacyDepth) in new (uint, byte)[] { (0, 5), (0x8000, 4), (0x0800, 6), (0x8020, 2) })
        foreach (var requested in Enumerable.Range(17, 10).Select(value => (uint)value))
        {
            fixture.SeedChipRevision(fixture.GraphicsBase, chip);
            var mode = (ntsc ? 0x11000u : 0x21000u) | key;
            var before = fixture.CaptureMemory();
            var result = fixture.Invoke(0, 0x301, requested, tag, mode);
            Assert.False(result.UsedFallback);
            Assert.Equal(requested, result.Data0);
            // Per-call trace capture observes fixed and relocated reads equally.
            Assert.Equal(requested >= 18 ? 1 : 0, result.CapabilityReadCount);
            Assert.Equal((byte)0, fixture.ReadOutputByte(0x311));
            // AA8 is an existing implementation-policy control, not an AGA
            // ROM claim. Partial Alice/Lisa must retain the legacy depth.
            if (requested >= 18)
                Assert.Equal(chip == 12 ? (byte)8 : legacyDepth, fixture.ReadOutputByte(0x312));
            var memory = new NameOracleMemory();
            Assert.Equal((int)requested, GraphicsDisplayDatabase.GetDisplayInfoData(memory, 0,
                0x20, requested, tag, mode, defaultMonitorNtsc: ntsc,
                supportsEcsDisplay: (chip & 3) == 3, supportsAgaDisplay: chip == 12));
            fixture.AssertOnlyOutputPrefixChanged(before, fixture.CaptureMemory(), 0x301,
                memory.ReadBytes(0x20, (int)requested));
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
        }
        foreach (var badBase in new uint[] { 0, 1, 0xFFFFFF14, uint.MaxValue })
        foreach (var requested in Enumerable.Range(17, 10).Select(value => (uint)value))
        {
            var before = fixture.CaptureMemory();
            var result = fixture.Invoke(0, 0x301, requested, tag, ModeId, badBase);
            Assert.Equal(requested, result.Data0);
            Assert.Equal(0, result.CapabilityReadCount);
            Assert.Equal(badBase, result.Address6);
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            if (requested >= 18)
            {
                AssertMemoryEqual(before, fixture.CaptureMemory());
                AssertNoDestinationAccess(result, 0x301, requested);
            }
            else
            {
                var memory = new NameOracleMemory();
                Assert.Equal(17, GraphicsDisplayDatabase.GetDisplayInfoData(memory, 0, 0x20,
                    17, tag, ModeId, defaultMonitorNtsc: ntsc));
                fixture.AssertOnlyOutputPrefixChanged(before, fixture.CaptureMemory(), 0x301,
                    memory.ReadBytes(0x20, 17));
            }
        }
    }

    [Theory]
    [MemberData(nameof(NativeDimsTransferEdgeCases))]
    public void NativeDisplayInfoDataDimsRasterLimitsMatchCapturedOcsEcsRecords(
        bool relocated, bool autoInit, bool ntsc, uint tag)
    {
        using var fixture = new Fixture(relocated, autoInit,
            ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal);
        foreach (var chip in new byte[] { 0, 2, 3 })
        foreach (var (key, _) in GraphicsDisplayInfoTransferBoundaryTests.CapturedOcsEcsDepths)
        foreach (var owner in new uint[] { 0, ntsc ? 0x11000u : 0x21000u })
        {
            fixture.SeedChipRevision(fixture.GraphicsBase, chip);
            var before = fixture.CaptureMemory();
            var result = fixture.Invoke(0, 0x300, 88, tag, owner | key);
            Assert.False(result.UsedFallback);
            Assert.Equal(66u, result.Data0);
            Assert.Equal(GraphicsDisplayInfoTransferBoundaryTests.CapturedRasterLimits(key),
                Enumerable.Range(0, 8).Select(index => fixture.ReadOutputByte(0x312u + (uint)index)));
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            var memory = new NameOracleMemory();
            Assert.Equal(66, GraphicsDisplayDatabase.GetDisplayInfoData(memory, 0, 0x20,
                88, tag, owner | key, defaultMonitorNtsc: ntsc, supportsEcsDisplay: chip == 3));
            fixture.AssertOnlyOutputPrefixChanged(before, fixture.CaptureMemory(), 0x300,
                memory.ReadBytes(0x20, 66));
        }
    }

    [Theory]
    [MemberData(nameof(NativeDimsTransferEdgeCases))]
    public void NativeDisplayInfoDataDimsRectanglesMatchCapturedOcsEcsRecords(
        bool relocated, bool autoInit, bool ntsc, uint tag)
    {
        using var fixture = new Fixture(relocated, autoInit,
            ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal);
        foreach (var chip in new byte[] { 0, 2, 3 })
        foreach (var (key, _) in GraphicsDisplayInfoTransferBoundaryTests.CapturedOcsEcsDepths)
        foreach (var owner in new uint[] { 0, ntsc ? 0x11000u : 0x21000u })
        {
            fixture.SeedChipRevision(fixture.GraphicsBase, chip);
            var before = fixture.CaptureMemory();
            var mode = owner | key;
            var handle = mode == 0 ? 0xFFFFFFFEu : mode;
            var result = fixture.Invoke(handle, 0x300, 88, tag, uint.MaxValue);
            Assert.False(result.UsedFallback);
            Assert.Equal(66u, result.Data0);
            Assert.Equal(GraphicsDisplayInfoTransferBoundaryTests.CapturedDimensionRectangles(key, ntsc),
                Enumerable.Range(26, 40).Select(index => fixture.ReadOutputByte(0x300u + (uint)index)));
            Assert.Equal(tag, result.Data1);
            Assert.Equal(0x300u, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            var memory = new NameOracleMemory();
            Assert.Equal(66, GraphicsDisplayDatabase.GetDisplayInfoData(memory, handle, 0x20,
                88, tag, uint.MaxValue, defaultMonitorNtsc: ntsc, supportsEcsDisplay: chip == 3));
            fixture.AssertOnlyOutputPrefixChanged(before, fixture.CaptureMemory(), 0x300,
                memory.ReadBytes(0x20, 66));
        }
    }

    [Theory]
    [MemberData(nameof(NativeDimsTransferEdgeCases))]
    public void NativeDisplayInfoDataDimsRectangleTransfersMatchCapturedRom(
        bool relocated, bool autoInit, bool ntsc, uint tag)
    {
        using var fixture = new Fixture(relocated, autoInit,
            ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal);
        foreach (var (key, depth) in GraphicsDisplayInfoTransferBoundaryTests.CapturedOcsEcsDepths)
        foreach (var owner in new uint[] { 0, ntsc ? 0x11000u : 0x21000u })
        foreach (var useHandle in new[] { false, true })
        foreach (var requested in Enumerable.Range(34, 32).Select(value => (uint)value)
                     .Concat(new uint[] { 66, 67, 88, uint.MaxValue }))
        {
            const uint destination = 0x300;
            var copied = CapturedDimsTransferCount(requested);
            var mode = owner | key;
            var handle = useHandle ? mode == 0 ? 0xFFFFFFFEu : mode : 0;
            var requestedMode = useHandle ? uint.MaxValue : mode;
            var expected = CapturedDimsRecord(key, depth, ntsc).Take((int)copied).ToArray();
            var before = fixture.CaptureMemory();
            var result = fixture.Invoke(handle, destination, requested, tag, requestedMode);
            Assert.False(result.UsedFallback);
            Assert.Equal(copied, result.Data0);
            Assert.Equal(tag, result.Data1);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(1, result.NativeReturnCount);
            fixture.AssertOnlyOutputPrefixChanged(before, fixture.CaptureMemory(), destination, expected);
            AssertNoDestinationAccess(result, destination + copied, 8);
        }
    }

    [Theory]
    [MemberData(nameof(NativeDimsTransferEdgeCases))]
    public void NativeDisplayInfoDataDimsRectangleTransferSpanBoundaries(
        bool relocated, bool autoInit, bool ntsc, uint tag)
    {
        using var fixture = new Fixture(relocated, autoInit,
            ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal);
        foreach (var (key, depth) in new (uint, ushort)[] { (0, 5), (0x8000, 4), (0x8020, 2), (0x8004, 4) })
        foreach (var requested in new uint[] { 34, 41, 42, 49, 50, 57, 58, 65, 66, 67, 88, uint.MaxValue })
        foreach (var scenario in new[] { "ordinary", "odd", "exact-end", "odd-last-valid", "first-wrap", "even-wrap", "foreign", "bad-base" })
        {
            var copied = CapturedDimsTransferCount(requested);
            var destination = scenario == "exact-end" ? uint.MaxValue - copied + 1
                : scenario == "odd-last-valid" ? uint.MaxValue - copied
                : scenario == "first-wrap" ? uint.MaxValue - copied + 2
                : scenario == "even-wrap" ? uint.MaxValue - copied + 3
                : scenario == "odd" ? 0x301u : 0x300u;
            // Original-ROM odd full queries did not return in343. Retain
            // transactional native declines until their exception contract is
            // established, rather than calling a safe extension ROM parity.
            var rejected = scenario is "odd" or "odd-last-valid" or "first-wrap" or "even-wrap" or "foreign" or "bad-base";
            var handle = scenario == "foreign" ? 0x50001000u : 0;
            var graphicsBase = scenario == "bad-base" ? 0xFFFFFF14u : fixture.GraphicsBase;
            var before = fixture.CaptureMemory();
            var result = fixture.Invoke(handle, destination, requested, tag,
                (ntsc ? 0x11000u : 0x21000u) | key, graphicsBase);
            Assert.Equal(rejected ? requested : copied, result.Data0);
            Assert.Equal(tag, result.Data1);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(graphicsBase, result.Address6);
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(1, result.NativeReturnCount);
            if (rejected)
            {
                Assert.Equal(0, result.CapabilityReadCount);
                AssertMemoryEqual(before, fixture.CaptureMemory());
                AssertNoDestinationAccess(result, destination, copied);
            }
            else
            {
                Assert.False(result.UsedFallback);
                fixture.AssertOnlyOutputPrefixChanged(before, fixture.CaptureMemory(), destination,
                    CapturedDimsRecord(key, depth, ntsc).Take((int)copied).ToArray());
                AssertNoDestinationAccess(result, unchecked(destination + copied), 8);
            }
        }
    }

    private static uint CapturedDimsTransferCount(uint requested)
        => requested <= 26 ? requested : 26 + 8 * ((Math.Min(requested, 66u) - 26) / 8);

    private static byte[] CapturedDimsRecord(uint key, ushort depth, bool ntsc)
        => BuildQueryHeader(GraphicsDisplayDatabase.DtagDims, (ntsc ? 0x11000u : 0x21000u) | key)
            .Concat(new byte[] { (byte)(depth >> 8), (byte)depth })
            .Concat(GraphicsDisplayInfoTransferBoundaryTests.CapturedRasterLimits(key))
            .Concat(GraphicsDisplayInfoTransferBoundaryTests.CapturedDimensionRectangles(key, ntsc)).ToArray();

    [Theory]
    [MemberData(nameof(NativeDimsTransferEdgeCases))]
    public void NativeDisplayInfoDataDimsDepthMatchesCapturedOcsEcsRecords(
        bool relocated, bool autoInit, bool ntsc, uint tag)
    {
        using var fixture = new Fixture(relocated, autoInit,
            ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal);
        foreach (var chip in new byte[] { 0, 2, 3 })
        foreach (var (key, depth) in GraphicsDisplayInfoTransferBoundaryTests.CapturedOcsEcsDepths)
        foreach (var owner in new uint[] { 0, ntsc ? 0x11000u : 0x21000u })
        {
            fixture.SeedChipRevision(fixture.GraphicsBase, chip);
            var mode = owner | key;
            var before = fixture.CaptureMemory();
            var result = fixture.Invoke(0, 0x300, 88, tag, mode);
            Assert.False(result.UsedFallback);
            Assert.Equal(66u, result.Data0);
            Assert.Equal(depth, (ushort)((fixture.ReadOutputByte(0x310) << 8) | fixture.ReadOutputByte(0x311)));
            Assert.Equal(tag, result.Data1);
            Assert.Equal(0x300u, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(1, result.NativeReturnCount);
            var memory = new NameOracleMemory();
            Assert.Equal(66, GraphicsDisplayDatabase.GetDisplayInfoData(memory, 0, 0x20,
                88, tag, mode, defaultMonitorNtsc: ntsc, supportsEcsDisplay: chip != 0));
            fixture.AssertOnlyOutputPrefixChanged(before, fixture.CaptureMemory(), 0x300, memory.ReadBytes(0x20, 66));
        }
    }

    [Theory]
    [MemberData(nameof(NativeDimsTransferEdgeCases))]
    public void NativeDisplayInfoDataDimsCapsActualSpanAndPreservesMalformedDeclines(
        bool relocated, bool autoInit, bool ntsc, uint tag)
    {
        using var fixture = new Fixture(relocated, autoInit,
            ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal);
        foreach (var requested in new uint[] { 17, 18, 26, 33, 65, 66, 67, 88, uint.MaxValue })
        foreach (var scenario in new[] { "ordinary", "final-byte", "wrap", "odd", "foreign" })
        {
            var destination = scenario == "final-byte" ? 0xFFFF_FFBEu
                : scenario == "wrap" ? 0xFFFF_FFC0u : scenario == "odd" ? 0x301u : 0x300u;
            var mode = scenario == "foreign" ? 0xDEAD_BEEFu : 0x21000u;
            var copied = CapturedDimsTransferCount(requested);
            var declined = scenario == "foreign" || destination > uint.MaxValue - (copied - 1) ||
                (copied >= 34 && (destination & 1) != 0);
            var before = fixture.CaptureMemory();
            var result = fixture.Invoke(0, destination, requested, tag, mode);
            Assert.Equal(declined ? requested : copied, result.Data0);
            Assert.Equal(tag, result.Data1);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(1, result.NativeReturnCount);
            if (declined)
            {
                AssertMemoryEqual(before, fixture.CaptureMemory());
                AssertNoDestinationAccess(result, destination, 88);
            }
            else
            {
                Assert.False(result.UsedFallback);
                var memory = new NameOracleMemory();
                Assert.Equal((int)copied, GraphicsDisplayDatabase.GetDisplayInfoData(memory, 0, 0x20,
                    requested, tag, mode, defaultMonitorNtsc: ntsc));
                fixture.AssertOnlyOutputPrefixChanged(before, fixture.CaptureMemory(), destination,
                    memory.ReadBytes(0x20, (int)copied));
            }
        }
    }

    public static IEnumerable<object[]> NativeMntrTransferEdgeCases()
        => NativeBootProfileCases().Where(row => (uint)row[3] == 0x80002000);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisplayInfoDataMntrViewPositionMatchesCapturedBootPoint(bool ntsc)
    {
        foreach (var requested in new uint[] { 21, 22, 23, 24, 88, 96 })
        foreach (var mode in new uint[] { 0, 0x8000, 0x8020, 0x11000, 0x19000, 0x19020, 0x21000, 0x29000, 0x29020 })
        {
            var memory = new NameOracleMemory();
            var copied = Math.Min(requested, 88u);
            Assert.Equal((int)copied, GraphicsDisplayDatabase.GetDisplayInfoData(
                memory, 0, 0x20, requested, 0x80002000, mode,
                monitorSpecProvider: _ => 0x12345678, defaultMonitorNtsc: ntsc));
            // Independent full88 captures322/352: Point at offsets20..23.
            Assert.Equal(new byte[] { 0x00, 0x81, 0x00, 0x2C }.Take((int)Math.Min(copied - 20, 4)),
                memory.ReadBytes(0x34, (int)Math.Min(copied - 20, 4)));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisplayInfoDataMntrPublishesSelectedMonitorMutablePosition(bool ntsc)
    {
        var positions = new GraphicsMonitorPositionState();
        Assert.True(positions.TrySet(0x11000, 130, 46));
        Assert.True(positions.TrySet(0x11000, 130, 46));
        Assert.True(positions.TrySet(0x21000, -2, -1));
        Assert.True(positions.TrySet(0x21000, null, 7));
        foreach (var mode in new uint[] { 0, 0x8000, 0x8020, 0x11000, 0x19000, 0x19020, 0x21000, 0x29000 })
        foreach (var size in new uint[] { 21, 22, 23, 24, 88 })
        {
            var memory = new NameOracleMemory();
            Assert.Equal((int)size, GraphicsDisplayDatabase.GetDisplayInfoData(
                memory, 0, 0x20, size, 0x80002000, mode,
                defaultMonitorNtsc: ntsc,
                monitorPositionProvider: id => positions.Get(id, ntsc)));
            var selectedNtsc = (mode & 0xFFFF0000u) == 0 ? ntsc : (mode & 0xFFFF0000u) == 0x10000;
            var expected = selectedNtsc
                ? new byte[] { 0, 130, 0, 46 }
                : new byte[] { 0xFF, 0xFE, 0, 7 };
            Assert.Equal(expected.Take((int)Math.Min(size - 20, 4)),
                memory.ReadBytes(0x34, (int)Math.Min(size - 20, 4)));
        }
    }

    [Fact]
    public void DisplayInfoDataMntrPositionStateRejectsAliasesAndIsolatesInstances()
    {
        var first = new GraphicsMonitorPositionState();
        var second = new GraphicsMonitorPositionState();
        foreach (var invalid in new uint[] { 0, 0x8000, 0x19000, 0x31000, uint.MaxValue })
            Assert.False(first.TrySet(invalid, 5, 6));
        Assert.True(first.TrySet(0x11000, short.MinValue, short.MaxValue));
        Assert.True(first.TrySet(0x11000, null, null));
        Assert.Equal(new GraphicsMonitorViewPosition(short.MinValue, short.MaxValue), first.Get(0x19000, false));
        Assert.Equal(GraphicsMonitorViewPosition.BootDefault, first.Get(0x21000, true));
        Assert.Equal(GraphicsMonitorViewPosition.BootDefault, second.Get(0x11000, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisplayInfoDataMntrDefaultViewPositionRemainsOriginalAfterCurrentMoves(bool ntsc)
    {
        foreach (var mode in new uint[] { 0, 0x8000, 0x11000, 0x19000, 0x21000, 0x29000 })
        {
            var memory = new NameOracleMemory();
            Assert.Equal(88, GraphicsDisplayDatabase.GetDisplayInfoData(
                memory, 0, 0x20, 88, GraphicsDisplayDatabase.DtagMntr, mode,
                defaultMonitorNtsc: ntsc,
                monitorPositionProvider: _ => new GraphicsMonitorViewPosition(-2, 7)));
            Assert.Equal(new byte[] { 0xFF, 0xFE, 0, 7 }, memory.ReadBytes(0x34, 4));
            // Original ROM365: default stays0081002C across SetPrefs changes.
            Assert.Equal(new byte[] { 0, 129, 0, 44 }, memory.ReadBytes(0x70, 4));
        }
    }

    [Theory]
    [MemberData(nameof(NativeMntrTransferEdgeCases))]
    public void NativeDisplayInfoDataMntrDefaultViewPositionReadsDistinctOriginalPoint(
        bool relocated, bool autoInit, bool ntsc, uint tag)
    {
        foreach (var overlap in new[] { false, true })
        {
            using var fixture = new Fixture(relocated, autoInit,
                ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal,
                includeNativeRuntimeDescriptor: true);
            fixture.SeedDefaultMonitor(0xCAFE1234);
            fixture.SeedOwnedMonitorPositions(0xFFFE0007, 0x80007FFF,
                ntscOriginal: 0x8000FFFF, palOriginal: 0x007BFF9D);
            var destination = overlap
                ? 0x00600000u + (uint)GraphicsDisplayDatabase.NativeMonitorPositionsOffset +
                    (ntsc ? 0u : 12u) + 8u
                : 0x300u;
            var result = fixture.Invoke(0, destination, 88, tag, 0);
            Assert.Equal(88u, result.Data0);
            Assert.False(result.UsedFallback);
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(1, result.NativeReturnCount);
            Assert.Equal(ntsc ? new byte[] { 0x80, 0, 0xFF, 0xFF } : new byte[] { 0, 123, 0xFF, 0x9D },
                Enumerable.Range(0, 4).Select(i => fixture.ReadOutputByte(destination + 80 + (uint)i)));
            AssertNoDestinationAccess(result, 0xCAFE1234, 0xA0);
            AssertNoDestinationAccess(result, destination + 88, 8);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisplayInfoDataMntrOriginalProviderIsReadOnlyWhenItsBytesAreRequested(bool ntsc)
    {
        foreach (var requested in new uint[] { 16, 20, 24, 80, 81, 82, 83, 84, 88 })
        {
            var memory = new NameOracleMemory();
            var calls = 0;
            Assert.Equal((int)requested, GraphicsDisplayDatabase.GetDisplayInfoData(
                memory, 0, 0x20, requested, GraphicsDisplayDatabase.DtagMntr, 0,
                defaultMonitorNtsc: ntsc,
                monitorPositionProvider: _ => new GraphicsMonitorViewPosition(3, 7),
                monitorOriginalPositionProvider: _ =>
                {
                    calls++;
                    return new GraphicsMonitorViewPosition(short.MinValue, short.MaxValue);
                }));
            Assert.Equal(requested > 80 ? 1 : 0, calls);
            if (requested > 80)
                Assert.Equal(new byte[] { 0x80, 0, 0x7F, 0xFF }.Take((int)Math.Min(requested - 80, 4)),
                    memory.ReadBytes(0x70, (int)Math.Min(requested - 80, 4)));
        }
    }

    [Theory]
    [MemberData(nameof(NativeMntrTransferEdgeCases))]
    public void NativeDisplayInfoDataMntrRuntimeDescriptorShortPrefixesDoNotReadPoints(
        bool relocated, bool autoInit, bool ntsc, uint tag)
    {
        using var fixture = new Fixture(relocated, autoInit,
            ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal,
            includeNativeRuntimeDescriptor: true);
        const uint pointer = 0xCAFE1234;
        fixture.SeedDefaultMonitor(pointer);
        fixture.SeedOwnedMonitorPositions(0xFFFE0007, 0x80007FFF);
        foreach (var requested in new uint[] { 16, 17, 18, 19, 20 })
        foreach (var destination in new uint[] { 0x300, 0x301 })
        {
            var before = fixture.CaptureMemory();
            var result = fixture.Invoke(0, destination, requested, tag, 0);
            Assert.Equal(requested, result.Data0);
            Assert.False(result.UsedFallback);
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(1, result.NativeReturnCount);
            AssertExactDefaultMonitorFieldReads(result, fixture.GraphicsBase, false);
            AssertNoDestinationAccess(result, 0x00600000u + (uint)GraphicsDisplayDatabase.NativeMonitorPositionsOffset + 4, 8);
            AssertNoDestinationAccess(result, 0x00600000u + (uint)GraphicsDisplayDatabase.NativeMonitorPositionsOffset + 16, 8);
            AssertNoDestinationAccess(result, pointer, 0xA0);
            var expected = BuildQueryHeader(tag, ntsc ? 0x11000u : 0x21000u)
                .Concat(new byte[] { 0xCA, 0xFE, 0x12, 0x34 })
                .Take((int)requested).ToArray();
            fixture.AssertOnlyOutputPrefixChanged(before, fixture.CaptureMemory(), destination, expected);
            AssertNoDestinationAccess(result, destination + requested, 8);
        }
    }

    public static IEnumerable<object[]> NativeRuntimeMonitorPositionCases()
    {
        foreach (var route in NativeMntrTransferEdgeCases())
        foreach (var requested in new uint[] { 21, 22, 23, 24, 88 })
            yield return route.Concat(new object[] { requested }).ToArray();
    }

    public static IEnumerable<object[]> NativeRuntimeMonitorPositionPrefixCases()
        => NativeRuntimeMonitorPositionCases().Where(row => (uint)row[4] < 88);

    [Theory]
    [MemberData(nameof(NativeRuntimeMonitorPositionPrefixCases))]
    public void NativeDisplayInfoDataMntrRuntimeDescriptorPublishesOddPrefixExactly(
        bool relocated, bool autoInit, bool ntsc, uint tag, uint requested)
    {
        using var fixture = new Fixture(relocated, autoInit,
            ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal,
            includeNativeRuntimeDescriptor: true);
        fixture.SeedDefaultMonitor(0xCAFE1234);
        fixture.SeedOwnedMonitorPositions(0xFFFE0007, 0x80007FFF);
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(0, 0x301, requested, tag, 0);
        Assert.Equal(requested, result.Data0);
        Assert.False(result.UsedFallback);
        Assert.Empty(result.RegisterDifferences);
        Assert.Equal(Fixture.StackPointer, result.StackPointer);
        Assert.Equal(1, result.NativeReturnCount);
        var point = ntsc ? new byte[] { 0xFF, 0xFE, 0x00, 0x07 }
            : new byte[] { 0x80, 0x00, 0x7F, 0xFF };
        var expected = BuildQueryHeader(tag, ntsc ? 0x11000u : 0x21000u)
            .Concat(new byte[] { 0xCA, 0xFE, 0x12, 0x34 }).Concat(point)
            .Take((int)requested).ToArray();
        fixture.AssertOnlyOutputPrefixChanged(before, fixture.CaptureMemory(), 0x301, expected);
        AssertExactPointerPrefixWrites(result, 0x301, requested);
        AssertNoDestinationAccess(result, 0x301 + requested, 8);
    }

    [Theory]
    [InlineData("tag", true)]
    [InlineData("version", true)]
    [InlineData("zero-version", true)]
    [InlineData("owner", true)]
    [InlineData("size", true)]
    [InlineData("foreign-public", true)]
    [InlineData("odd-database", true)]
    [InlineData("wrapping-database", true)]
    [InlineData("magic", false)]
    [InlineData("database-version", false)]
    [InlineData("database-size", false)]
    [InlineData("monitor-id", false)]
    public void NativeDisplayInfoDataMntrRuntimeDescriptorDeclinesInvalidOwnership(
        string corruption, bool noDatabaseRead)
    {
        foreach (var route in NativeMntrTransferEdgeCases())
        foreach (var requested in new uint[] { 24, 88 })
        {
            var ntsc = (bool)route[2];
            using var fixture = new Fixture((bool)route[0], (bool)route[1],
                ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal,
                includeNativeRuntimeDescriptor: true);
            fixture.SeedDefaultMonitor(0xCAFE1234);
            fixture.SeedOwnedMonitorPositions(0xFFFE0007, 0x80007FFF);
            fixture.CorruptOwnedMonitorState(corruption, ntsc);
            var before = fixture.CaptureMemory();
            var result = fixture.Invoke(0, 0x300, requested, GraphicsDisplayDatabase.DtagMntr, 0);
            Assert.Equal(requested, result.Data0);
            Assert.False(result.UsedFallback);
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(1, result.NativeReturnCount);
            var after = fixture.CaptureMemory();
            Assert.Equal(before.Low, after.Low);
            Assert.Equal(before.GraphicsImage, after.GraphicsImage);
            AssertNoDestinationAccess(result, 0x300, requested);
            if (noDatabaseRead)
                AssertNoDestinationAccess(result, 0x00600000, (uint)GraphicsDisplayDatabase.NativeDatabaseSize);
            AssertNoDestinationAccess(result, 0xCAFE1234, 0xA0);
            if (corruption == "odd-database")
                AssertNoDestinationAccess(result, 0x00600001, (uint)GraphicsDisplayDatabase.NativeDatabaseSize);
            if (corruption == "wrapping-database")
                AssertNoDestinationAccess(result, 0xFFFFFFFE, 2);
        }
    }

    [Theory]
    [MemberData(nameof(NativeRuntimeMonitorPositionCases))]
    public void NativeDisplayInfoDataMntrRuntimeDescriptorRetainsPointBeforeOverlappingOutput(
        bool relocated, bool autoInit, bool ntsc, uint tag, uint requested)
    {
        using var fixture = new Fixture(relocated, autoInit,
            ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal,
            includeNativeRuntimeDescriptor: true);
        fixture.SeedDefaultMonitor(0xCAFE1234);
        fixture.SeedOwnedMonitorPositions(0xFFFE0007, 0x80007FFF);
        var destination = 0x00600000u + (uint)GraphicsDisplayDatabase.NativeMonitorPositionsOffset +
            (ntsc ? 0u : 12u) + 4;
        var result = fixture.Invoke(0, destination, requested, tag, 0);
        Assert.Equal(requested, result.Data0);
        Assert.False(result.UsedFallback);
        Assert.Empty(result.RegisterDifferences);
        Assert.Equal(Fixture.StackPointer, result.StackPointer);
        Assert.Equal(1, result.NativeReturnCount);
        var point = ntsc ? new byte[] { 0xFF, 0xFE, 0x00, 0x07 }
            : new byte[] { 0x80, 0x00, 0x7F, 0xFF };
        Assert.Equal(point.Take((int)Math.Min(requested - 20, 4)),
            Enumerable.Range(0, (int)Math.Min(requested - 20, 4))
                .Select(index => fixture.ReadOutputByte(destination + 20 + (uint)index)));
        // The untouched output tail overlaps CMDB identity/registration fields
        // which must be read before publishing. Only writes are forbidden there.
        Assert.DoesNotContain(result.CpuDataWriteAddresses,
            access => AccessOverlapsPhysicalSpan(access, destination + requested, 8));
    }

    [Theory]
    [MemberData(nameof(NativeRuntimeMonitorPositionCases))]
    public void NativeDisplayInfoDataMntrRuntimeDescriptorReadsOwnedCurrentPoint(
        bool relocated, bool autoInit, bool ntsc, uint tag, uint requested)
    {
        using var fixture = new Fixture(relocated, autoInit,
            ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal,
            includeNativeRuntimeDescriptor: true);
        fixture.SeedDefaultMonitor(0xCAFE1234);
        fixture.SeedOwnedMonitorPositions(0xFFFE0007, 0x80007FFF);
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(0, 0x300, requested, tag, 0);
        Assert.Equal(requested, result.Data0);
        Assert.False(result.UsedFallback);
        Assert.Empty(result.RegisterDifferences);
        Assert.Equal(Fixture.StackPointer, result.StackPointer);
        Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
        Assert.Equal(1, result.NativeReturnCount);
        AssertExactDefaultMonitorFieldReads(result, fixture.GraphicsBase, false);
        AssertNoDestinationAccess(result, 0xCAFE1234, 0xA0);
        var point = ntsc ? new byte[] { 0xFF, 0xFE, 0x00, 0x07 }
            : new byte[] { 0x80, 0x00, 0x7F, 0xFF };
        Assert.Equal(point.Take((int)Math.Min(requested - 20, 4)),
            fixture.CaptureMemory().Low.Skip(0x314).Take((int)Math.Min(requested - 20, 4)));
        Assert.Equal(before.GraphicsImage, fixture.CaptureMemory().GraphicsImage);
        AssertNoDestinationAccess(result, 0x300 + requested, 8);
    }

    [Theory]
    [MemberData(nameof(NativeMntrTransferEdgeCases))]
    public void NativeDisplayInfoDataMntrViewPositionFullRecordMatchesCapturedBootPoint(
        bool relocated, bool autoInit, bool ntsc, uint tag)
    {
        using var fixture = new Fixture(relocated, autoInit,
            ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal);
        fixture.SeedDefaultMonitor(0x12345678);
        foreach (var requested in new uint[] { 88, 96 })
        {
            var result = fixture.Invoke(0, 0x300, requested, tag, 0);
            Assert.Equal(88u, result.Data0);
            Assert.False(result.UsedFallback);
            Assert.Empty(result.RegisterDifferences);
            AssertExactDefaultMonitorFieldReads(result, fixture.GraphicsBase, true);
            Assert.Equal(new byte[] { 0x00, 0x81, 0x00, 0x2C },
                fixture.CaptureMemory().Low.AsSpan(0x314, 4).ToArray());
        }
    }

    [Theory]
    [MemberData(nameof(NativeMntrTransferEdgeCases))]
    public void NativeDisplayInfoDataMntrViewPositionPrefixesMatchCapturedBootPoint(
        bool relocated, bool autoInit, bool ntsc, uint tag)
    {
        using var fixture = new Fixture(relocated, autoInit,
            ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal);
        foreach (var requested in new uint[] { 21, 22, 23, 24 })
        foreach (var destination in new uint[] { 0x300, 0x301 })
        foreach (var pointer in new uint[] { 0x12345678, 0xFEDCBA98, 1 })
        foreach (var useHandle in new[] { false, true })
        {
            fixture.SeedDefaultMonitor(pointer);
            var before = fixture.CaptureMemory();
            var result = fixture.Invoke(useHandle ? 0xFFFFFFFEu : 0, destination, requested,
                tag, useHandle ? 0xDEADBEEFu : 0);
            Assert.False(result.UsedFallback);
            Assert.Equal(requested, result.Data0);
            Assert.Equal(tag, result.Data1);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(1, result.NativeReturnCount);
            Assert.Equal(0, result.CapabilityReadCount);
            AssertExactDefaultMonitorFieldReads(result, fixture.GraphicsBase, true);
            var expected = BuildQueryHeader(tag, ntsc ? 0x11000u : 0x21000u)
                .Concat(new byte[] { (byte)(pointer >> 24), (byte)(pointer >> 16),
                    (byte)(pointer >> 8), (byte)pointer, 0x00, 0x81, 0x00, 0x2C })
                .Take((int)requested).ToArray();
            fixture.AssertOnlyOutputPrefixChanged(before, fixture.CaptureMemory(), destination, expected);
            AssertExactPointerPrefixWrites(result, destination, requested);
            AssertNoDestinationAccess(result, destination + requested, 8);
            AssertNoDestinationAccess(result, pointer, 0xA0);
        }
    }

    [Theory]
    [MemberData(nameof(NativeMntrTransferEdgeCases))]
    public void NativeDisplayInfoDataMntrPointerPrefixesPublishExactResidentBytes(
        bool relocated, bool autoInit, bool ntsc, uint tag)
    {
        using var fixture = new Fixture(relocated, autoInit,
            ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal);
        foreach (var requested in new uint[] { 17, 18, 19, 20 })
        foreach (var pointer in new uint[] { 0x12345678, 0xFEDCBA98, 1 })
        foreach (var useHandle in new[] { false, true })
        {
            fixture.SeedDefaultMonitor(pointer);
            var before = fixture.CaptureMemory();
            var result = fixture.Invoke(useHandle ? 0xFFFFFFFEu : 0, 0x300, requested,
                tag, useHandle ? 0xDEADBEEFu : 0);
            Assert.False(result.UsedFallback);
            Assert.Equal(requested, result.Data0);
            Assert.Equal(tag, result.Data1);
            Assert.Equal(0x300u, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(1, result.NativeReturnCount);
            Assert.Equal(0, result.CapabilityReadCount);
            AssertExactDefaultMonitorFieldReads(result, fixture.GraphicsBase, expected: true);
            var pointerBytes = new byte[]
                { (byte)(pointer >> 24), (byte)(pointer >> 16), (byte)(pointer >> 8), (byte)pointer };
            var expected = BuildQueryHeader(tag, ntsc ? 0x11000u : 0x21000u)
                .Concat(pointerBytes).Take((int)requested).ToArray();
            var portable = new NameOracleMemory();
            Assert.Equal((int)requested, GraphicsDisplayDatabase.GetDisplayInfoData(
                portable, 0, 0x20, requested, tag, 0,
                monitorSpecProvider: _ => pointer, defaultMonitorNtsc: ntsc));
            Assert.Equal(expected, portable.ReadBytes(0x20, (int)requested));
            fixture.AssertOnlyOutputPrefixChanged(before, fixture.CaptureMemory(), 0x300, expected);
            AssertExactPointerPrefixWrites(result, 0x300, requested);
            AssertNoDestinationAccess(result, 0x300 + requested, 8);
            // Pointer values need not identify a readable/even MonitorSpec.
            // Copy its address only; never acquire or dereference the object.
            AssertNoDestinationAccess(result, pointer, 0xA0);
        }
    }

    [Theory]
    [MemberData(nameof(NativeMntrTransferEdgeCases))]
    public void NativeDisplayInfoDataMntrPointerPrefixesGuardResidentFieldEnvelope(
        bool relocated, bool autoInit, bool ntsc, uint tag)
    {
        using var fixture = new Fixture(relocated, autoInit,
            ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal);
        foreach (var requested in new uint[] { 17, 18, 19, 20, 21, 22, 23, 24 })
        foreach (var graphicsBase in new uint[]
            { 0, 1, 2, fixture.GraphicsBase, 0xFFFFFE6E, 0xFFFFFE6F, 0xFFFFFE70, 0xFFFFFFFE, uint.MaxValue })
        foreach (var pointer in new uint[] { 0, 0x12345678 })
        {
            // Independent SDK envelope: LONG at A6+0x18E must end within32bits.
            var sourceAdmitted = graphicsBase != 0 && (graphicsBase & 1) == 0 && graphicsBase <= 0xFFFFFE6E;
            fixture.SeedDefaultMonitor(graphicsBase, pointer);
            var before = fixture.CaptureMemory();
            var result = fixture.Invoke(0, 0x300, requested, tag, 0, graphicsBase);
            Assert.Equal(requested, result.Data0);
            Assert.Equal(tag, result.Data1);
            Assert.Equal(0x300u, result.Address1);
            Assert.Equal(graphicsBase, result.Address6);
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(1, result.NativeReturnCount);
            Assert.Equal(0, result.CapabilityReadCount);
            AssertExactDefaultMonitorFieldReads(result, graphicsBase, sourceAdmitted);
            if (!sourceAdmitted || pointer == 0)
            {
                AssertMemoryEqual(before, fixture.CaptureMemory());
                AssertNoDestinationAccess(result, 0x300, requested);
            }
            else
            {
                Assert.False(result.UsedFallback);
                var expected = BuildQueryHeader(tag, ntsc ? 0x11000u : 0x21000u)
                    .Concat(new byte[] { 0x12, 0x34, 0x56, 0x78, 0x00, 0x81, 0x00, 0x2C }).Take((int)requested).ToArray();
                fixture.AssertOnlyOutputPrefixChanged(before, fixture.CaptureMemory(), 0x300, expected);
                AssertExactPointerPrefixWrites(result, 0x300, requested);
                AssertNoDestinationAccess(result, 0x300 + requested, 8);
            }
        }
    }

    [Theory]
    [MemberData(nameof(NativeMntrTransferEdgeCases))]
    public void NativeDisplayInfoDataMntrPointerPrefixesProveDestinationBeforeResidentOwnership(
        bool relocated, bool autoInit, bool ntsc, uint tag)
    {
        using var fixture = new Fixture(relocated, autoInit,
            ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal);
        const uint pointer = 0x12345678;
        fixture.SeedDefaultMonitor(pointer);
        foreach (var requested in new uint[] { 17, 18, 19, 20, 21, 22, 23, 24 })
        // Independent inclusive endpoints; odd-length transfers have an odd
        // exact-last start. A rounded even-length helper would miss that edge.
        foreach (var destination in new uint[] { 0x300, 0x301, 0,
            requested switch { 17 or 18 => 0xFFFFFFEE, 19 or 20 => 0xFFFFFFEC, 21 or 22 => 0xFFFFFFEA, _ => 0xFFFFFFE8 },
            requested switch { 17 => 0xFFFFFFEF, 18 => 0xFFFFFFEE, 19 => 0xFFFFFFED, 20 => 0xFFFFFFEC,
                21 => 0xFFFFFFEB, 22 => 0xFFFFFFEA, 23 => 0xFFFFFFE9, _ => 0xFFFFFFE8 },
            requested switch { 17 => 0xFFFFFFF0, 18 => 0xFFFFFFEF, 19 => 0xFFFFFFEE, 20 => 0xFFFFFFED,
                21 => 0xFFFFFFEC, 22 => 0xFFFFFFEB, 23 => 0xFFFFFFEA, _ => 0xFFFFFFE9 },
            requested switch { 17 or 18 => 0xFFFFFFF0, 19 or 20 => 0xFFFFFFEE, 21 or 22 => 0xFFFFFFEC, _ => 0xFFFFFFEA } }.Distinct())
        foreach (var owner in new (uint Handle, uint Mode, bool Default, bool Canonical)[]
        {
            (0, 0, true, true),
            (0xFFFFFFFE, 0xDEADBEEF, true, true),
            (0xFFFFFFFE, uint.MaxValue, true, true),
            (0, 0x8000, false, true),
            (0, 0x21000, false, true),
            (0x11000, 0, false, true),
            (0x50001000, 0, false, false),
            (0, 0xDEADBEEF, false, false),
            (0, uint.MaxValue, false, false),
        })
        {
            var noRecord = owner.Handle == 0 && owner.Mode == uint.MaxValue;
            var emptyCanonical = destination == 0 && owner.Canonical;
            var admitted = owner.Default && destination != 0 &&
                (ulong)destination + requested <= 0x100000000UL;
            var before = fixture.CaptureMemory();
            var result = fixture.Invoke(owner.Handle, destination, requested, tag, owner.Mode);
            Assert.Equal(noRecord || emptyCanonical ? 0u : requested, result.Data0);
            Assert.Equal(tag, result.Data1);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(1, result.NativeReturnCount);
            Assert.Equal(0, result.CapabilityReadCount);
            AssertExactDefaultMonitorFieldReads(result, fixture.GraphicsBase, admitted);
            AssertNoDestinationAccess(result, pointer, 0xA0);
            if (!admitted)
            {
                AssertMemoryEqual(before, fixture.CaptureMemory());
                AssertNoDestinationAccess(result, destination, requested + 8);
            }
            else
            {
                Assert.False(result.UsedFallback);
                var expected = BuildQueryHeader(tag, ntsc ? 0x11000u : 0x21000u)
                    .Concat(new byte[] { 0x12, 0x34, 0x56, 0x78, 0x00, 0x81, 0x00, 0x2C }).Take((int)requested).ToArray();
                fixture.AssertOnlyOutputPrefixChanged(before, fixture.CaptureMemory(), destination, expected);
                AssertExactPointerPrefixWrites(result, destination, requested);
                AssertNoDestinationAccess(result, unchecked(destination + requested), 8);
            }
        }
    }

    [Theory]
    [InlineData(129, 44, 0x0081002Cu)]
    [InlineData(-32768, 32767, 0x80007FFFu)]
    [InlineData(-1, -2, 0xFFFFFFFEu)]
    [InlineData(0, 0, 0u)]
    public void DisplayInfoDataMntrViewPositionValuePreservesSignedGuestWords(int x, int y, uint expected)
        => Assert.Equal(expected, new GraphicsMonitorViewPosition((short)x, (short)y).WordPair);

    [Theory]
    [MemberData(nameof(NativeMntrTransferEdgeCases))]
    public void NativeDisplayInfoDataMntrViewPositionRetainsPointerBeforeOverlappingPublication(
        bool relocated, bool autoInit, bool ntsc, uint tag)
    {
        using var fixture = new Fixture(relocated, autoInit,
            ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal);
        foreach (var requested in new uint[] { 21, 22, 23, 24 })
        foreach (var destination in new uint[] { 0x17C, 0x17D, 0x18F, 0x190 })
        {
            // Valid A6=2 puts defaultMonitor at0x190, overlapping output.
            fixture.SeedDefaultMonitor(2, 0x12345678);
            var before = fixture.CaptureMemory();
            var result = fixture.Invoke(0, destination, requested, tag, 0, 2);
            Assert.False(result.UsedFallback);
            Assert.Equal(requested, result.Data0);
            Assert.Equal(tag, result.Data1);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(2u, result.Address6);
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(1, result.NativeReturnCount);
            Assert.Equal(0, result.CapabilityReadCount);
            AssertExactDefaultMonitorFieldReads(result, 2, true);
            var expected = BuildQueryHeader(tag, ntsc ? 0x11000u : 0x21000u)
                .Concat(new byte[] { 0x12, 0x34, 0x56, 0x78, 0, 0x81, 0, 0x2C })
                .Take((int)requested).ToArray();
            fixture.AssertOnlyOutputPrefixChanged(before, fixture.CaptureMemory(), destination, expected);
            AssertExactPointerPrefixWrites(result, destination, requested, source: 0x190);
            // The source LONG may extend into the unused output tail. Writes
            // must still stop exactly at the requested prefix.
            Assert.DoesNotContain(result.CpuDataWriteAddresses,
                access => AccessOverlapsPhysicalSpan(access, destination + requested, 8));
        }
    }

    private static void AssertExactPointerPrefixWrites(CallResult result, uint destination, uint count, uint? source = null)
    {
        // A previous case may have published identical bytes. Prove that this
        // call really wrote each requested byte once, rather than accepting
        // an unchanged buffer whose old contents happen to match.
        var writes = result.CpuDataWriteAddresses
            .Where(access => AccessOverlapsPhysicalSpan(access, destination, count)).ToArray();
        Assert.All(writes, access => Assert.Equal(AmigaBusAccessSize.Byte, access.Size));
        Assert.Equal(Enumerable.Range(0, (int)count)
                .Select(index => unchecked(destination + (uint)index) & 0x00FFFFFFu),
            writes.Select(access => access.Address & 0x00FFFFFFu));
        Assert.DoesNotContain(result.CpuDataReadAddresses,
            access => AccessOverlapsPhysicalSpan(access, destination, count) &&
                (!source.HasValue || !AccessOverlapsPhysicalSpan(access, source.Value, 4)));
    }

    private static void AssertExactDefaultMonitorFieldReads(CallResult result, uint graphicsBase, bool expected)
    {
        var field = unchecked(graphicsBase + 0x18Eu) & 0x00FFFFFFu;
        var accesses = result.CpuDataReadAddresses
            .Where(access => AccessOverlapsPhysicalSpan(access, field, 4))
            .Select(access => (Address: access.Address & 0x00FFFFFFu, access.Size)).ToArray();
        Assert.Equal(expected ? 1 : 0, result.DefaultMonitorReadCount);
        if (!expected)
        {
            Assert.Empty(accesses);
            return;
        }
        // A single logical LONG may be traced as one LONG or two ordered
        // WORD cycles. Reject duplicate/incomplete constituent reads too.
        Assert.True(accesses.SequenceEqual(new[] { (field, AmigaBusAccessSize.Long) }) ||
            accesses.SequenceEqual(new[] { (field, AmigaBusAccessSize.Word),
                ((field + 2) & 0x00FFFFFFu, AmigaBusAccessSize.Word) }),
            "The defaultMonitor field must be read once, completely, without extra constituent reads.");
    }

    [Theory]
    [MemberData(nameof(NativeMntrTransferEdgeCases))]
    public void NativeDisplayInfoDataMntrCapsFullSpanAndPreservesPointerAndPartialOwnership(
        bool relocated, bool autoInit, bool ntsc, uint tag)
    {
        using var fixture = new Fixture(relocated, autoInit,
            ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal);
        const uint monitorPointer = 0x00600000;
        foreach (var requested in new uint[] { 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 87, 88, 89, 96, uint.MaxValue })
        foreach (var scenario in new[] { "ordinary", "final-byte", "wrap", "odd", "nondefault", "foreign", "null-monitor" })
        {
            fixture.SeedDefaultMonitor(scenario == "null-monitor" ? 0 : monitorPointer);
            var destination = scenario == "final-byte" ? 0xFFFF_FFA8u
                : scenario == "wrap" ? 0xFFFF_FFAAu : scenario == "odd" ? 0x301u : 0x300u;
            var mode = scenario == "foreign" ? 0xDEAD_BEEFu : scenario == "nondefault" ? 0x21000u : 0;
            var headerOnly = requested == 16;
            var prefix = requested is >= 17 and <= 24;
            var declined = scenario == "foreign" ||
                (!headerOnly && (scenario is "nondefault" or "null-monitor")) ||
                (!headerOnly && !prefix && (requested < 88 || scenario is "wrap" or "odd"));
            var copied = requested <= 24 ? requested : 88u;
            var before = fixture.CaptureMemory();
            var result = fixture.Invoke(0, destination, requested, tag, mode);
            Assert.Equal(declined ? requested : copied, result.Data0);
            Assert.Equal(tag, result.Data1);
            Assert.Equal(destination, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(1, result.NativeReturnCount);
            Assert.Equal(0, result.CapabilityReadCount);
            if (declined)
            {
                AssertMemoryEqual(before, fixture.CaptureMemory());
                AssertNoDestinationAccess(result, destination, 96);
            }
            else
            {
                Assert.False(result.UsedFallback);
                var memory = new NameOracleMemory();
                Assert.Equal((int)copied, GraphicsDisplayDatabase.GetDisplayInfoData(memory, 0, 0x20,
                    requested, tag, mode, monitorSpecProvider: _ => monitorPointer, defaultMonitorNtsc: ntsc));
                fixture.AssertOnlyOutputPrefixChanged(before, fixture.CaptureMemory(), destination,
                    memory.ReadBytes(0x20, (int)copied));
            }
        }
    }

    private static void AssertNoDestinationAccess(
        CallResult result,
        uint destination,
        uint byteCount)
    {
        Assert.DoesNotContain(
            result.CpuDataReadAddresses,
            access => AccessOverlapsPhysicalSpan(access, destination, byteCount));
        Assert.DoesNotContain(
            result.CpuDataWriteAddresses,
            access => AccessOverlapsPhysicalSpan(access, destination, byteCount));
    }

    private static bool AccessOverlapsPhysicalSpan(
        (uint Address, AmigaBusAccessSize Size) access, uint start, uint byteCount)
    {
        var accessBytes = access.Size switch
        {
            AmigaBusAccessSize.Byte => 1u,
            AmigaBusAccessSize.Word => 2u,
            AmigaBusAccessSize.Long => 4u,
            _ => throw new InvalidOperationException("Unknown CPU data-access width."),
        };
        for (uint offset = 0; offset < accessBytes; offset++)
            if ((unchecked(access.Address + offset - start) & 0x00FFFFFFu) < byteCount)
                return true;
        return false;
    }

    [Theory]
    [InlineData(0u, 2, 1u, 0xA0u, true)]
    [InlineData(0u, 1, 1u, 0xA0u, false)]
    [InlineData(0x18Cu, 4, 0x18Eu, 4u, true)]
    [InlineData(0x18Cu, 2, 0x18Eu, 4u, false)]
    [InlineData(0xFFFFFFFEu, 4, 0u, 1u, true)]
    [InlineData(0xFFFFFFFEu, 2, 0u, 1u, false)]
    [InlineData(0x01000300u, 1, 0x300u, 1u, true)]
    [InlineData(0x300u, 4, 0x300u, 0u, false)]
    public void NativeDisplayInfoDataAccessGuardsCheckWholePhysicalTransfers(
        uint address, int accessBytes, uint start, uint byteCount, bool expected)
    {
        var size = accessBytes switch
        {
            1 => AmigaBusAccessSize.Byte,
            2 => AmigaBusAccessSize.Word,
            4 => AmigaBusAccessSize.Long,
            _ => throw new ArgumentOutOfRangeException(nameof(accessBytes)),
        };
        Assert.Equal(expected, AccessOverlapsPhysicalSpan((address, size), start, byteCount));
    }

    private static uint LastBase(int outputBytes)
        => uint.MaxValue - checked((uint)outputBytes - 1u);

    private static uint FirstEvenWrap(int outputBytes)
        => checked(LastBase(outputBytes) + 2u);

    private static void AssertMemoryEqual(MemorySnapshot expected, MemorySnapshot actual)
    {
        Assert.Equal(expected.GraphicsImage, actual.GraphicsImage);
        Assert.Equal(expected.Resident, actual.Resident);
        Assert.Equal(expected.Caller, actual.Caller);
        Assert.Equal(expected.StackGuards, actual.StackGuards);
        Assert.Equal(expected.Low, actual.Low);
        Assert.Equal(expected.High, actual.High);
    }

    private static void Check(List<string> failures, string label, Action assertion)
    {
        try
        {
            assertion();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failures.Add(label + ": " + exception.Message);
        }
    }

    private sealed record NativeImage(
        byte[] Code,
        IReadOnlyDictionary<GraphicsLvo, int> Entries,
        int Fallback,
        IReadOnlySet<int> LocalFallbacks);

    private sealed record MemorySnapshot(
        byte[] GraphicsImage,
        byte[] Resident,
        byte[] Caller,
        byte[] StackGuards,
        byte[] Low,
        byte[] High);

    private sealed record CallResult(
        uint Data0,
        uint Data1,
        uint Address1,
        uint Address6,
        uint ProgramCounter,
        uint StackPointer,
        ushort StatusRegister,
        bool UsedFallback,
        int NativeReturnCount,
        ushort NativeReturnPredecessorOpcode,
        int CapabilityReadCount,
        uint[] CpuByteDataReadAddresses,
        int DefaultMonitorReadCount,
        (uint Address, AmigaBusAccessSize Size)[] CpuDataReadAddresses,
        (uint Address, AmigaBusAccessSize Size)[] CpuDataWriteAddresses,
        string[] RegisterDifferences);

    private sealed class NameOracleMemory : IGraphicsMemory
    {
        private readonly byte[] _bytes =
            Enumerable.Repeat((byte)0xA5, 0x100).ToArray();

        internal byte[] ReadBytes(uint address, int count)
        {
            Assert.True(TryGetRange(address, count, out var index));
            return _bytes.AsSpan(index, count).ToArray();
        }

        public bool TryReadByte(uint address, out byte value)
        {
            if (!TryGetRange(address, 1, out var index))
            {
                value = 0;
                return false;
            }

            value = _bytes[index];
            return true;
        }

        public bool TryReadWord(uint address, out ushort value)
        {
            if (!TryGetRange(address, sizeof(ushort), out var index))
            {
                value = 0;
                return false;
            }

            value = (ushort)((_bytes[index] << 8) | _bytes[index + 1]);
            return true;
        }

        public bool TryReadLong(uint address, out uint value)
        {
            if (!TryGetRange(address, sizeof(uint), out var index))
            {
                value = 0;
                return false;
            }

            value = ((uint)_bytes[index] << 24) |
                    ((uint)_bytes[index + 1] << 16) |
                    ((uint)_bytes[index + 2] << 8) |
                    _bytes[index + 3];
            return true;
        }

        public bool TryWriteByte(uint address, byte value)
        {
            if (!TryGetRange(address, 1, out var index))
                return false;

            _bytes[index] = value;
            return true;
        }

        public bool TryWriteWord(uint address, ushort value)
        {
            if (!TryGetRange(address, sizeof(ushort), out var index))
                return false;

            _bytes[index] = (byte)(value >> 8);
            _bytes[index + 1] = (byte)value;
            return true;
        }

        public bool TryWriteLong(uint address, uint value)
        {
            if (!TryGetRange(address, sizeof(uint), out var index))
                return false;

            _bytes[index] = (byte)(value >> 24);
            _bytes[index + 1] = (byte)(value >> 16);
            _bytes[index + 2] = (byte)(value >> 8);
            _bytes[index + 3] = (byte)value;
            return true;
        }

        private bool TryGetRange(uint address, int count, out int index)
        {
            if (count < 0 || (ulong)address + (uint)count > (uint)_bytes.Length)
            {
                index = 0;
                return false;
            }

            index = checked((int)address);
            return true;
        }
    }

    private sealed class Fixture : IDisposable
    {
        private const uint FixedCodeAddress = 0x0040_0000;
        private const uint FixedGraphicsBase = 0x0070_0000;
        private const uint FixedResidentAddress = 0x0072_0000;
        private const uint HunkSegmentAddress = 0x0080_0000;
        private const uint HunkResidentAddress = 0x00A8_0000;
        private const uint CallerAddress = 0x00B8_0000;
        private const uint StackAddress = 0x00C7_0000;
        private const uint HighPhysicalAddress = 0x00FF_FF00;
        internal const uint StackPointer = StackAddress + 0x300;
        private static readonly uint[] DataCanaries =
        {
            0xD3D3_0303, 0xD4D4_0404, 0xD5D5_0505,
            0xD6D6_0606, 0xD7D7_0707
        };
        private static readonly uint[] AddressCanaries =
        {
            0xA2A2_0202, 0xA3A3_0303, 0xA4A4_0404, 0xA5A5_0505
        };
        private readonly AmigaBus _bus = new();
        private readonly IM68kCore _cpu;
        private readonly bool _autoInitEntry;
        private readonly uint _nativeCodeAddress;
        private readonly uint _entry;
        private readonly uint _functionEntry;
        private readonly uint _residentAddress;
        private readonly int _residentByteCount;
        private readonly NativeImage _image;
        private readonly int _positiveImageSize;
        private readonly bool _nativeNtsc;

        internal Fixture(bool relocated, bool autoInitEntry,
            GraphicsLibraryImageProfile profile = GraphicsLibraryImageProfile.CompactHost,
            bool includeNativeRuntimeDescriptor = false)
        {
            _nativeNtsc = profile == GraphicsLibraryImageProfile.NativeNtsc;
            _image = profile == GraphicsLibraryImageProfile.NativeNtsc ? NtscImage.Value : Image.Value;
            _positiveImageSize = includeNativeRuntimeDescriptor
                ? GraphicsLibraryImageLayout.NativeRuntimeImageSize
                : profile == GraphicsLibraryImageProfile.CompactHost
                ? PositiveImageSize : GraphicsLibraryImageLayout.NativeMinimumImageSize;
            Assert.Equal(GetDisplayInfoDataLvo, (int)GraphicsLvo.GetDisplayInfoData);
            _autoInitEntry = autoInitEntry;
            Route = GraphicsLvo.GetDisplayInfoData + "/" +
                    (relocated ? "relocated HUNK" : "fixed image") + "/" +
                    (autoInitEntry ? "AUTOINIT JSR(A2)" : "direct native body");

            if (relocated)
            {
                var hunk = profile == GraphicsLibraryImageProfile.CompactHost ? Hunk.Value
                    : NativeGraphicsLibraryHunkBuilder.Build(_image.Code, _image.Entries, _image.Fallback,
                        _positiveImageSize, profile,
                        includeNativeRuntimeDescriptor: includeNativeRuntimeDescriptor);
                var residentByteCount = 0;
                var addresses = new Queue<uint>(new[] { HunkSegmentAddress, HunkResidentAddress });
                var loader = new AmigaHunkProgramLoader(_bus, size =>
                {
                    var address = addresses.Dequeue();
                    var limit = address == HunkSegmentAddress ? HunkResidentAddress : CallerAddress;
                    Assert.True(
                        (ulong)address + (uint)size <= limit,
                        "HUNK fixture segments overlap.");
                    _bus.MapWritableMemory(address, new byte[size]);
                    if (address == HunkResidentAddress)
                        residentByteCount = size;
                    return address;
                });
                var program = loader.Load(hunk.Bytes);
                Assert.Empty(addresses);
                Assert.True(residentByteCount > 0);
                GraphicsBase = program.SegmentBases[0] + (uint)hunk.VectorOffset;
                _nativeCodeAddress = program.SegmentBases[0] + (uint)hunk.NativeCodeOffset;
                _residentAddress = program.SegmentBases[1];
                _residentByteCount = residentByteCount;
            }
            else
            {
                var entries = _image.Entries.ToDictionary(
                    item => item.Key,
                    item => FixedCodeAddress + (uint)item.Value);
                var fallback = FixedCodeAddress + (uint)_image.Fallback;
                var library = NativeGraphicsLibraryImageBuilder.Build(
                    FixedGraphicsBase,
                    FixedResidentAddress,
                    fallback,
                    entries,
                    fallback,
                    _positiveImageSize, profile,
                    includeNativeRuntimeDescriptor: includeNativeRuntimeDescriptor);
                Assert.True(
                    (ulong)FixedCodeAddress + (uint)_image.Code.Length <= library.VectorBase);
                _bus.MapWritableMemory(FixedCodeAddress, _image.Code);
                _bus.MapWritableMemory(library.VectorBase, library.VectorBytes);
                _bus.MapWritableMemory(library.LibraryBase, library.PositiveBytes);
                _bus.MapWritableMemory(library.ResidentAddress, library.ResidentBytes);
                GraphicsBase = library.LibraryBase;
                _nativeCodeAddress = FixedCodeAddress;
                _residentAddress = library.ResidentAddress;
                _residentByteCount = library.ResidentBytes.Length;
            }

            _entry = _nativeCodeAddress +
                     (uint)_image.Entries[GraphicsLvo.GetDisplayInfoData];
            Assert.Equal((ushort)0x4AFC, _bus.ReadWord(_residentAddress));
            var functionArray = _bus.ReadLong(_bus.ReadLong(_residentAddress + 0x16) + 4);
            var functionOrdinal = checked((uint)((-GetDisplayInfoDataLvo / 6) - 1));
            _functionEntry = _bus.ReadLong(functionArray + functionOrdinal * 4u);
            Assert.Equal(_entry, _functionEntry);

            _bus.WriteByte(
                GraphicsBase + (uint)GraphicsLayouts.GfxBaseChipRevBits0,
                (byte)GraphicsChipRevision.SetEcs,
                0);
            _bus.MapWritableMemory(
                HighPhysicalAddress,
                Enumerable.Repeat((byte)0xB7, 0x100).ToArray());
            for (var address = 0u; address < 0x400; address++)
                _bus.WriteByte(address, 0xC3, 0);
            _bus.MapWritableMemory(
                StackAddress,
                Enumerable.Repeat((byte)0x5A, 0x600).ToArray());
            _bus.MapWritableMemory(
                CallerAddress,
                Enumerable.Repeat((byte)0x91, 8).ToArray());
            ReturnAddress = CallerAddress + (autoInitEntry ? 2u : 4u);
            if (autoInitEntry)
                _bus.WriteWord(CallerAddress, 0x4E92); // JSR(A2).
            _bus.WriteWord(ReturnAddress, 0x4E71);
            _cpu = AmigaM68kCoreFactory.Default.Create(
                M68kBackendKind.AccurateM68000,
                _bus);
        }

        internal string Route { get; }
        internal uint GraphicsBase { get; }
        internal uint ReturnAddress { get; }
        // Precise provider-terminal evidence for new tests, including linker
        // RTS clones. CallResult.UsedFallback retains its legacy global-only
        // meaning until the older admission expectations are audited separately.
        internal bool LastUsedProviderTerminal { get; private set; }
        internal byte[] CaptureDatabase() => ReadBytes(0x00600000, GraphicsDisplayDatabase.NativeDatabaseSize + 128);

        internal void SeedDisplayFlags(ushort flags)
            => _bus.WriteWord(GraphicsBase + 0xCE, flags, 0);

        internal void SeedDefaultMonitor(uint monitor)
            => SeedDefaultMonitor(GraphicsBase, monitor);

        internal void SeedOwnedMonitorPositions(uint ntscPoint, uint palPoint,
            uint ntscOriginal = 0x0081002C, uint palOriginal = 0x0081002C)
        {
            const uint database = 0x00600000;
            Assert.Equal(GraphicsLibraryImageLayout.NativeRuntimeImageSize, _positiveImageSize);
            // Extra mapped guard/output room permits a destination overlapping
            // the final Point; the advertised CMDB allocation size stays exact.
            _bus.MapWritableMemory(database, new byte[GraphicsDisplayDatabase.NativeDatabaseSize + 128]);
            _bus.WriteLong(database, GraphicsDisplayDatabase.NativeDatabaseMagic);
            _bus.WriteLong(database + 4, GraphicsDisplayDatabase.NativeDatabaseVersion);
            _bus.WriteLong(database + 8, (uint)GraphicsDisplayDatabase.NativeDatabaseSize);
            _bus.WriteLong(database + 12, (uint)GraphicsDisplayDatabase.NativeDatabaseRecordCount);
            for (var index = 0; index < 2; index++)
            {
                var record = database + (uint)(GraphicsDisplayDatabase.NativeMonitorPositionsOffset +
                    index * GraphicsDisplayDatabase.NativeMonitorPositionRecordSize);
                _bus.WriteLong(record, index == 0 ? 0x11000u : 0x21000u);
                _bus.WriteLong(record + 4, index == 0 ? ntscPoint : palPoint);
                _bus.WriteLong(record + 8, index == 0 ? ntscOriginal : palOriginal);
            }
            _bus.WriteLong(GraphicsBase + (uint)GraphicsLayouts.GfxBaseDisplayInfoDataBase, database);
            _bus.WriteLong(GraphicsBase + (uint)GraphicsLibraryImageLayout.NativeRuntimeDescriptorOwner, GraphicsBase);
            _bus.WriteLong(GraphicsBase + (uint)GraphicsLibraryImageLayout.NativeRuntimeDescriptorDatabase, database);
            _bus.WriteLong(GraphicsBase + (uint)GraphicsLibraryImageLayout.NativeRuntimeDescriptorDatabaseSize,
                (uint)GraphicsDisplayDatabase.NativeDatabaseSize);
            _bus.WriteLong(GraphicsBase + (uint)GraphicsLibraryImageLayout.NativeRuntimeDescriptorTag,
                GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag);
            SeedMonitorRegistrations(_nativeNtsc ? ReadPhysicalLong(DefaultMonitorPhysicalAddress(GraphicsBase)) : 0,
                _nativeNtsc ? 0 : ReadPhysicalLong(DefaultMonitorPhysicalAddress(GraphicsBase)),
                _nativeNtsc ? 0x11000u : 0x21000u);
        }

        internal void SeedMonitorRegistrations(uint ntsc, uint pal, uint defaultFamily)
        {
            const uint database = 0x00600000;
            _bus.WriteLong(database + (uint)GraphicsDisplayDatabase.NativeDefaultMonitorIdOffset, defaultFamily);
            _bus.WriteLong(database + (uint)GraphicsDisplayDatabase.NativeMonitorRegistrationsOffset, ntsc);
            _bus.WriteLong(database + (uint)GraphicsDisplayDatabase.NativeMonitorRegistrationsOffset + 4, pal);
        }

        internal void CorruptOwnedMonitorState(string corruption, bool ntsc)
        {
            var (address, value) = corruption switch
            {
                "tag" => (GraphicsBase + 0x250, 0u),
                "version" => (GraphicsBase + 0x254, 2u),
                "zero-version" => (GraphicsBase + 0x254, 0u),
                "owner" => (GraphicsBase + 0x258, GraphicsBase + 2),
                "size" => (GraphicsBase + 0x260, 16u),
                "foreign-public" => (GraphicsBase + (uint)GraphicsLayouts.GfxBaseDisplayInfoDataBase, 0xCAFE1234u),
                "odd-database" => (GraphicsBase + 0x25C, 0x00600001u),
                "wrapping-database" => (GraphicsBase + 0x25C, 0xFFFFFFFEu),
                "word-database" => (GraphicsBase + 0x25C, 0x00600002u),
                "null-database" => (GraphicsBase + 0x25C, 0u),
                "default-family" => (0x00600000u + (uint)GraphicsDisplayDatabase.NativeDefaultMonitorIdOffset, 0x31000u),
                "magic" => (0x00600000u, 0u),
                "database-version" => (0x00600004u, 1u),
                "database-size" => (0x00600008u, 16u),
                "monitor-id" => (0x00600000u + (uint)GraphicsDisplayDatabase.NativeMonitorPositionsOffset +
                    (ntsc ? 0u : 12u), ntsc ? 0x21000u : 0x11000u),
                _ => throw new ArgumentOutOfRangeException(nameof(corruption))
            };
            _bus.WriteLong(address, value);
            if (corruption is "odd-database" or "wrapping-database" or "word-database" or "null-database")
                _bus.WriteLong(GraphicsBase + (uint)GraphicsLayouts.GfxBaseDisplayInfoDataBase, value);
        }

        internal void SeedDefaultMonitor(uint graphicsBase, uint monitor)
        {
            // The synthetic first-even base lives in poisoned low memory, not
            // in the library image. Give it an explicit compact lib_PosSize:
            // random guard bytes must not opt this legacy boundary fixture into
            // a full native descriptor. Real CMDO fixtures remain untouched.
            if (graphicsBase == 2)
                _bus.WriteWord(graphicsBase + 0x12, GraphicsLayouts.GfxBaseNativeSize, 0);
            var address = DefaultMonitorPhysicalAddress(graphicsBase);
            for (var index = 0; index < sizeof(uint); index++)
            {
                var shift = (sizeof(uint) - 1 - index) * 8;
                _bus.WriteByte(
                    (address + (uint)index) & 0x00FF_FFFFu,
                    (byte)(monitor >> shift),
                    0);
            }
            Assert.Equal(
                monitor,
                ReadPhysicalLong(address));
        }

        internal void SeedChipRevision(uint graphicsBase, byte chipRevision)
        {
            var address = CapabilityPhysicalAddress(graphicsBase);
            _bus.WriteByte(address, chipRevision, 0);
            Assert.Equal(chipRevision, _bus.ReadByte(address));
        }

        private static uint CapabilityPhysicalAddress(uint graphicsBase)
            => ((graphicsBase & 0x00FF_FFFFu) +
                (uint)GraphicsLayouts.GfxBaseChipRevBits0) & 0x00FF_FFFFu;

        private static uint DefaultMonitorPhysicalAddress(uint graphicsBase)
            => ((graphicsBase & 0x00FF_FFFFu) +
                (uint)GraphicsLayouts.GfxBaseDefaultMonitor) & 0x00FF_FFFFu;

        private uint ReadPhysicalLong(uint address)
            => ((uint)_bus.ReadByte(address & 0x00FF_FFFFu) << 24) |
               ((uint)_bus.ReadByte((address + 1u) & 0x00FF_FFFFu) << 16) |
               ((uint)_bus.ReadByte((address + 2u) & 0x00FF_FFFFu) << 8) |
               _bus.ReadByte((address + 3u) & 0x00FF_FFFFu);

        private byte[] ReadBytes(uint address, int count)
            => Enumerable.Range(0, count)
                .Select(index => _bus.ReadByte(address + (uint)index))
                .ToArray();

        internal MemorySnapshot CaptureMemory()
            => new(
                ReadBytes(
                    GraphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize,
                    NativeGraphicsLibraryImageBuilder.VectorTableSize +
                    _positiveImageSize),
                ReadBytes(_residentAddress, _residentByteCount),
                ReadBytes(CallerAddress, 8),
                // Legacy: return4 + staged4 + MOVEM44 + DIMS scratch66 =118.
                // Registered MNTR: return4 + full register frame60 + image88
                // =152. Guard immediately below the applicable maximum frame.
                ReadBytes(StackAddress, _positiveImageSize == GraphicsLibraryImageLayout.NativeRuntimeImageSize ? 0x268 : 0x28A)
                    .Concat(ReadBytes(StackPointer, 0x300))
                    .ToArray(),
                ReadBytes(0, 0x400),
                ReadBytes(HighPhysicalAddress, 0x100));

        internal void AssertOnlyOutputSpanChanged(
            MemorySnapshot before,
            MemorySnapshot after,
            uint destination,
            int outputBytes,
            uint tag,
            uint expectedModeId)
        {
            Assert.Equal(before.GraphicsImage, after.GraphicsImage);
            Assert.Equal(before.Resident, after.Resident);
            Assert.Equal(before.Caller, after.Caller);
            Assert.Equal(before.StackGuards, after.StackGuards);

            var expectedLow = before.Low.ToArray();
            var expectedHigh = before.High.ToArray();
            for (var offset = 0; offset < outputBytes; offset++)
            {
                var physical = (destination + (uint)offset) & 0x00FF_FFFFu;
                if (physical < (uint)expectedLow.Length)
                {
                    expectedLow[(int)physical] = after.Low[(int)physical];
                    continue;
                }

                Assert.InRange(
                    physical,
                    HighPhysicalAddress,
                    HighPhysicalAddress + (uint)expectedHigh.Length - 1u);
                var highOffset = checked((int)(physical - HighPhysicalAddress));
                expectedHigh[highOffset] = after.High[highOffset];
            }

            Assert.Equal(expectedLow, after.Low);
            Assert.Equal(expectedHigh, after.High);
            Assert.Equal(tag, ReadLong(destination));
            Assert.Equal(BuildQueryHeader(tag, expectedModeId).Skip(4).Take(4),
                Enumerable.Range(0, 4).Select(index =>
                    ReadOutputByte(unchecked(destination + 4u + (uint)index))));
        }

        internal void AssertOnlyOutputPrefixChanged(
            MemorySnapshot before,
            MemorySnapshot after,
            uint destination,
            byte[] expectedBytes)
        {
            Assert.NotEmpty(expectedBytes);
            Assert.Equal(before.GraphicsImage, after.GraphicsImage);
            Assert.Equal(before.Resident, after.Resident);
            Assert.Equal(before.Caller, after.Caller);
            Assert.Equal(before.StackGuards, after.StackGuards);

            var expectedLow = before.Low.ToArray();
            var expectedHigh = before.High.ToArray();
            for (var offset = 0; offset < expectedBytes.Length; offset++)
            {
                var physical = (destination + (uint)offset) & 0x00FF_FFFFu;
                if (physical < (uint)expectedLow.Length)
                {
                    expectedLow[(int)physical] = after.Low[(int)physical];
                    continue;
                }

                Assert.InRange(
                    physical,
                    HighPhysicalAddress,
                    HighPhysicalAddress + (uint)expectedHigh.Length - 1u);
                var highOffset = checked((int)(physical - HighPhysicalAddress));
                expectedHigh[highOffset] = after.High[highOffset];
            }

            Assert.Equal(expectedLow, after.Low);
            Assert.Equal(expectedHigh, after.High);
            for (var offset = 0; offset < expectedBytes.Length; offset++)
            {
                Assert.Equal(
                    expectedBytes[offset],
                    ReadOutputByte(destination + (uint)offset));
            }
        }

        internal byte ReadOutputByte(uint address)
            => _bus.ReadByte(address & 0x00FF_FFFFu);

        internal ushort ReadOutputWord(uint address)
            => (ushort)((ReadOutputByte(address) << 8) |
                        ReadOutputByte(address + 1u));

        private uint ReadLong(uint address)
            => ((uint)_bus.ReadByte(address & 0x00FF_FFFFu) << 24) |
               ((uint)_bus.ReadByte((address + 1u) & 0x00FF_FFFFu) << 16) |
               ((uint)_bus.ReadByte((address + 2u) & 0x00FF_FFFFu) << 8) |
               _bus.ReadByte((address + 3u) & 0x00FF_FFFFu);

        internal CallResult Invoke(
            uint handle,
            uint destination,
            uint requestedBytes,
            uint tag,
            uint modeId,
            uint? graphicsBase = null)
        {
            var suppliedGraphicsBase = graphicsBase ?? GraphicsBase;
            var expectedAddresses = AddressCanaries.ToArray();
            if (_autoInitEntry)
                expectedAddresses[0] = _functionEntry;
            var entryStackPointer = StackPointer;
            var entryProgramCounter = CallerAddress;
            if (!_autoInitEntry)
            {
                entryStackPointer -= 4u;
                _bus.WriteLong(entryStackPointer, ReturnAddress);
                entryProgramCounter = _entry;
            }

            _cpu.Reset(entryProgramCounter, entryStackPointer);
            Assert.Equal(M68kCpuState.ResetStatusRegister, _cpu.State.StatusRegister);
            _cpu.State.D[0] = requestedBytes;
            _cpu.State.D[1] = tag;
            _cpu.State.D[2] = modeId;
            for (var index = 0; index < DataCanaries.Length; index++)
                _cpu.State.D[index + 3] = DataCanaries[index];
            _cpu.State.A[0] = handle;
            _cpu.State.A[1] = destination;
            for (var index = 0; index < expectedAddresses.Length; index++)
                _cpu.State.A[index + 2] = expectedAddresses[index];
            _cpu.State.A[6] = suppliedGraphicsBase;
            // Count is a bounded ring length, not a lifetime cursor. Start
            // each invocation with an empty trace so large matrices cannot
            // silently lose all reads after the 65,536-entry ring fills.
            Assert.IsType<BoundedBusAccessLog>(_bus.BusAccesses).Clear();
            var busAccessStart = _bus.BusAccesses.Count;

            if (_autoInitEntry)
            {
                _cpu.ExecuteInstruction();
                Assert.Equal(_functionEntry, _cpu.State.ProgramCounter);
                Assert.Equal(StackPointer - 4u, _cpu.State.A[7]);
                Assert.Equal(ReturnAddress, _bus.ReadLong(_cpu.State.A[7]));
            }
            Assert.Equal(_entry, _cpu.State.ProgramCounter);

            var usedFallback = false;
            LastUsedProviderTerminal = false;
            var nativeReturnCount = 0;
            ushort previousNativeOpcode = 0;
            ushort nativeReturnPredecessorOpcode = 0;
            for (var instruction = 0; instruction < 8_192; instruction++)
            {
                var pc = _cpu.State.ProgramCounter;
                Assert.True(
                    pc >= _nativeCodeAddress &&
                    (ulong)pc < (ulong)_nativeCodeAddress + (uint)_image.Code.Length,
                    $"Unexpected execution address 0x{pc:X8}.");
                usedFallback |= pc == _nativeCodeAddress + (uint)_image.Fallback;
                LastUsedProviderTerminal |= pc == _nativeCodeAddress + (uint)_image.Fallback ||
                    _image.LocalFallbacks.Contains((int)(pc - _nativeCodeAddress));
                var opcode = _bus.ReadWord(pc);
                if (opcode == 0x4E75)
                {
                    nativeReturnCount++;
                    nativeReturnPredecessorOpcode = previousNativeOpcode;
                }

                previousNativeOpcode = opcode;
                _cpu.ExecuteInstruction();
                if (_cpu.State.ProgramCounter != ReturnAddress)
                    continue;

                var differences = new List<string>();
                if (_cpu.State.D[2] != modeId)
                {
                    differences.Add(
                        $"D2 expected {modeId:X8}, actual {_cpu.State.D[2]:X8}");
                }
                for (var index = 0; index < DataCanaries.Length; index++)
                {
                    if (_cpu.State.D[index + 3] != DataCanaries[index])
                    {
                        differences.Add(
                            $"D{index + 3} expected {DataCanaries[index]:X8}, " +
                            $"actual {_cpu.State.D[index + 3]:X8}");
                    }
                }
                for (var index = 0; index < expectedAddresses.Length; index++)
                {
                    if (_cpu.State.A[index + 2] != expectedAddresses[index])
                    {
                        differences.Add(
                            $"A{index + 2} expected {expectedAddresses[index]:X8}, " +
                            $"actual {_cpu.State.A[index + 2]:X8}");
                    }
                }
                if (_cpu.State.A[6] != suppliedGraphicsBase)
                {
                    differences.Add(
                        $"A6 expected {suppliedGraphicsBase:X8}, " +
                        $"actual {_cpu.State.A[6]:X8}");
                }

                Assert.True(_bus.BusAccesses.Count < 65536,
                    "One invocation exhausted the physical-bus trace; access evidence is incomplete.");
                var capabilityAddress = CapabilityPhysicalAddress(suppliedGraphicsBase);
                var cpuByteDataReadAddresses = _bus.BusAccesses
                    .Skip(busAccessStart)
                    .Where(access =>
                        access.Request.Requester == AmigaBusRequester.Cpu &&
                        access.Request.Kind == AmigaBusAccessKind.CpuDataRead &&
                        access.Request.Size == AmigaBusAccessSize.Byte)
                    .Select(access => access.Request.Address)
                    .ToArray();
                var capabilityReadCount = cpuByteDataReadAddresses.Count(address =>
                    (address & 0x00FF_FFFFu) == capabilityAddress);
                var cpuDataReadAddresses = _bus.BusAccesses
                    .Skip(busAccessStart)
                    .Where(access =>
                        access.Request.Requester == AmigaBusRequester.Cpu &&
                        access.Request.Kind == AmigaBusAccessKind.CpuDataRead)
                    .Select(access =>
                        (access.Request.Address, access.Request.Size))
                    .ToArray();
                var cpuDataWriteAddresses = _bus.BusAccesses
                    .Skip(busAccessStart)
                    .Where(access =>
                        access.Request.Requester == AmigaBusRequester.Cpu &&
                        access.Request.Kind == AmigaBusAccessKind.CpuDataWrite)
                    .Select(access =>
                        (access.Request.Address, access.Request.Size))
                    .ToArray();
                var defaultMonitorAddress =
                    DefaultMonitorPhysicalAddress(suppliedGraphicsBase);
                var defaultMonitorLongReads = cpuDataReadAddresses.Count(access =>
                    access.Size == AmigaBusAccessSize.Long &&
                    (access.Address & 0x00FF_FFFFu) == defaultMonitorAddress);
                var defaultMonitorHighWordReads = cpuDataReadAddresses.Count(access =>
                    access.Size == AmigaBusAccessSize.Word &&
                    (access.Address & 0x00FF_FFFFu) == defaultMonitorAddress);
                var defaultMonitorLowWordAddress =
                    (defaultMonitorAddress + 2u) & 0x00FF_FFFFu;
                var defaultMonitorLowWordReads = cpuDataReadAddresses.Count(access =>
                    access.Size == AmigaBusAccessSize.Word &&
                    (access.Address & 0x00FF_FFFFu) == defaultMonitorLowWordAddress);
                var defaultMonitorReadCount = defaultMonitorLongReads +
                                              Math.Min(
                                                  defaultMonitorHighWordReads,
                                                  defaultMonitorLowWordReads);

                return new CallResult(
                    _cpu.State.D[0],
                    _cpu.State.D[1],
                    _cpu.State.A[1],
                    _cpu.State.A[6],
                    _cpu.State.ProgramCounter,
                    _cpu.State.A[7],
                    _cpu.State.StatusRegister,
                    usedFallback,
                    nativeReturnCount,
                    nativeReturnPredecessorOpcode,
                    capabilityReadCount,
                    cpuByteDataReadAddresses,
                    defaultMonitorReadCount,
                    cpuDataReadAddresses,
                    cpuDataWriteAddresses,
                    differences.ToArray());
            }

            throw new InvalidOperationException(
                $"{Route} did not return: PC={_cpu.State.ProgramCounter:X8}, " +
                $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
