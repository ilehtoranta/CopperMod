using System.IO.Compression;
using CopperScreen;

namespace CopperScreen.Lightweight.Tests;

public sealed class HostDependencyTests
{
    [Theory]
    [InlineData(512 * 1024, 0x200000u)]
    [InlineData(8 * 1024 * 1024, 0x200000u)]
    [InlineData(16 * 1024 * 1024, 0x10000000u)]
    [InlineData(512 * 1024 * 1024, 0x20000000u)]
    [InlineData(1024 * 1024 * 1024, 0x40000000u)]
    public void HostProfileKeepsStandardFastRamAddresses(int size, uint address)
        => Assert.Equal(address, CopperScreenDefaults.GetDefaultFastRamBase(size));

    [Theory]
    [InlineData(0)]
    [InlineData(256 * 1024)]
    [InlineData(3 * 1024 * 1024)]
    public void HostProfileRejectsNonstandardFastRamSizes(int size)
        => Assert.Throws<ArgumentOutOfRangeException>(() => CopperScreenDefaults.GetDefaultFastRamBase(size));

    [Fact]
    public void HostAssemblyDoesNotReferenceCopperStartOrLegacyHost()
    {
        var references = typeof(CopperScreenSession).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, r => r.Name!.StartsWith("CopperStart", StringComparison.Ordinal));
        Assert.DoesNotContain(references, r => r.Name is "CopperMod.Amiga" or "CopperMod.Amiga.CyberGraphics" or "CopperMod.Amiga.Emulator" or "CopperSharp.Sdk.Amiga.Support");
    }

    [Fact]
    public async Task UnavailableBenchDoesNotLaunchGuestCode()
    {
        var bench = new CopperBenchViewModel();
        await bench.ShowOverlayAsync(null);
        Assert.True(bench.IsOverlayVisible);
        Assert.Empty(bench.Entries);
        Assert.Contains("not included", bench.SelectedDetails);
        Assert.False(await bench.ActivateSelectedAsync(null, _ => throw new Exception("Must not launch")));
    }

    [Fact]
    public void StandardAdfAndZipLoadingAndAdjacentDiskSelectionAreHostOwned()
    {
        var directory = Path.Combine(Path.GetTempPath(), "copperscreen-media-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var first = Path.Combine(directory, "Game (Disk 1 of 2).adf");
            var second = Path.Combine(directory, "Game (Disk 2 of 2).adf");
            var bytes = new byte[CopperScreenAdfImage.StandardAdfSize];
            bytes[100] = 42;
            File.WriteAllBytes(first, bytes);
            File.WriteAllBytes(second, bytes);
            Assert.Equal(second, CopperScreenDiskNavigation.ResolveAdjacentDiskPath(first, 1));
            Assert.Equal(first, CopperScreenDiskNavigation.ResolveAdjacentDiskPath(second, -1));
            Assert.Equal(bytes, CopperScreenDiskImageArchive.LoadDiskImage(first).Data);
            var zip = Path.Combine(directory, "set.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                using var output = archive.CreateEntry("disk.adf").Open();
                output.Write(bytes);
            }
            Assert.Equal(bytes, CopperScreenDiskImageArchive.LoadDiskImage(zip).Data);
            Assert.Equal(bytes, CopperScreenDiskImageArchive.LoadDiskImage(CopperScreenDiskImageArchive.CreateEntryPath(zip, "disk.adf")).Data);
            Assert.Throws<NotSupportedException>(() => CopperScreenAdfImage.Load(first + ".ipf"));
            File.WriteAllBytes(second, [1, 2]);
            Assert.Throws<NotSupportedException>(() => CopperScreenAdfImage.Load(second));
        }
        finally { Directory.Delete(directory, true); }
    }
}
