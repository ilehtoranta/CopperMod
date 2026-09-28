namespace CopperMod.Amiga.Tests;

/// <summary>
/// Static ownership guard for the CopperStart Layers integration boundary.
/// Public Amiga envelope offsets belong to CopperSharp SDK codecs, not to the
/// emulator adapter, raster provider, or semantic fixtures.
/// </summary>
public sealed class CopperStartLayersArchitectureTests
{
    private static readonly string[] ForbiddenPublicLayouts =
    [
        "LayersLayout",
        "GraphicsLayout.",
        "UtilityLayout",
        "ExecLayout"
    ];

    private static readonly string[] LayersFacingSources =
    [
        "CopperMod.Amiga.Emulator/CopperStart/Layers/LayersHostServices.cs",
        "CopperMod.Amiga.Emulator/CopperStart/Layers/LayersHostCallbackBoundary.cs",
        "CopperMod.Amiga.Emulator/CopperStart/Layers/LayersHostCallbackToken.cs",
        "CopperMod.Amiga.Emulator/CopperStart/Layers/LayersGraphicsBridge.cs",
        "CopperMod.Amiga.Emulator/CopperStart/Layers/LayersPixelTransactions.cs",
        "CopperMod.Amiga.Emulator/CopperStart/Layers/LayersAllocationFaults.cs",
        "CopperMod.Amiga.Emulator/CyberGraphics/ClipRects.cs",
        "CopperMod.Amiga.Emulator/CyberGraphics/LayersRaster.cs",
        "CopperMod.Amiga.Emulator/CyberGraphics/ValidatedLayersRasterMemory.cs",
        "CopperMod.Amiga.Emulator/CyberGraphics/LayersGuestMemory.cs",
        "CopperMod.Amiga.Tests/CopperStartLayersBootTests.cs",
        "CopperMod.Amiga.Tests/CopperStartIntuitionLayersActorBootTests.cs",
        "CopperMod.Amiga.Tests/CopperStartIntuitionLayersTopologyReturnBootTests.cs",
        "CopperMod.Amiga.Tests/LayersHostCallbackTokenTests.cs",
        "CopperMod.Amiga.Tests/CopperStartLayersRasterLedgerTests.cs",
        "CopperMod.Amiga.Tests/KickstartRomLayersDifferentialTests.cs",
        "CopperMod.Amiga.Tests/KickstartRomLayersDifferentialCodecs.cs",
        "CopperMod.Amiga.Tests/KickstartRomLayersClipBlitRefreshTests.cs",
        "CopperMod.Amiga.Tests/KickstartRomLayersDrawRefreshTests.cs",
        "CopperMod.Amiga.Tests/KickstartRomLayersDrawFlagTests.cs",
        "CopperMod.Amiga.Tests/KickstartRomLayersPolyDrawRefreshTests.cs",
        "CopperMod.Amiga.Tests/KickstartRomLayersAreaEndRefreshTests.cs",
        "CopperMod.Amiga.Tests/KickstartRomLayersDrawEllipseRefreshTests.cs",
        "CopperMod.Amiga.Tests/KickstartRomLayersRectFillRefreshTests.cs",
        "CopperMod.Amiga.Tests/KickstartRomLayersEraseRectRefreshTests.cs",
        "CopperMod.Amiga.Tests/KickstartRomLayersFloodRefreshTests.cs",
        "CopperMod.Amiga.Tests/KickstartRomLayersBltPatternRefreshTests.cs",
        "CopperMod.Amiga.Tests/KickstartRomLayersBltTemplateRefreshTests.cs",
        "CopperMod.Amiga.Tests/KickstartRomLayersBltBitMapRastPortRefreshTests.cs",
        "CopperMod.Amiga.Tests/KickstartRomLayersBltMaskBitMapRastPortRefreshTests.cs",
        "CopperMod.Amiga.Tests/LayersTestGuestMemory.cs"
    ];

    [Fact]
    public void LayersFacingSourcesUseOnlySdkOwnedPublicEnvelopeCodecs()
    {
        var root = FindRepositoryRoot();
        foreach (var relativePath in LayersFacingSources)
        {
            var path = Path.Combine(root, relativePath.Replace('/',
                Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"Missing guarded source: {path}");
            var source = File.ReadAllText(path);
            foreach (var forbidden in ForbiddenPublicLayouts)
            {
                Assert.DoesNotContain(forbidden, source);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory != null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CopperMod.sln")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the MedPlayer repository root.");
    }
}
