using System.Security.Cryptography;
using CopperMod.Abstractions;

namespace CopperMod.Replayers.Tests;

[Collection("Binary replayer cache")]
public sealed class BinaryReplayerLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CopperMod-Replayers-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ValidatesAnyExplicitlyTrustedIdentityAndReturnsPrivateCopy()
    {
        byte[] first = [1, 2, 3];
        byte[] second = [4, 5, 6, 7];
        var descriptor = Descriptor(Identity("one", first), Identity("two", second));

        var result = BinaryReplayerLoader.Validate(descriptor, second);

        Assert.Equal(second, result);
        Assert.NotSame(second, result);
    }

    [Fact]
    public void RejectsMatchingLengthWithUnknownHash()
    {
        byte[] trusted = [1, 2, 3];
        var descriptor = Descriptor(Identity("trusted", trusted));

        var exception = Assert.Throws<ModuleLoadException>(
            () => BinaryReplayerLoader.Validate(descriptor, new byte[] { 3, 2, 1 }));

        Assert.Contains("has SHA256", exception.Message);
        Assert.Contains(descriptor.Identities[0].Sha256, exception.Message);
    }

    [Fact]
    public void FindsFormatBinaryInAncestorReplayersDirectory()
    {
        byte[] trusted = [0x4E, 0x75];
        var descriptor = Descriptor(Identity("rts", trusted));
        var nested = Path.Combine(_root, "bin", "Debug", "net10.0");
        var binaryPath = Path.Combine(_root, descriptor.DefaultRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(binaryPath)!);
        Directory.CreateDirectory(nested);
        File.WriteAllBytes(binaryPath, trusted);

        var result = BinaryReplayerLoader.FindDefaultPath(descriptor, nested);

        Assert.Equal(binaryPath, result);
    }

    [Fact]
    public void DescriptorRejectsDirectoryTraversal()
    {
        var identity = Identity("rts", new byte[] { 0x4E, 0x75 });
        Assert.Throws<ArgumentException>(
            () => new BinaryReplayerDescriptor("test", "Test", "..", "player.bin", "TEST_PLAYER", "Test.Player", identity));
    }

    [Fact]
    public void DescriptorSupportsNestedPlayerFamilyDirectory()
    {
        var identity = Identity("rts", new byte[] { 0x4E, 0x75 });
        var descriptor = new BinaryReplayerDescriptor(
            "tfmx",
            "TFMX",
            Path.Combine("EaglePlayer", "TFMX"),
            "TFMX.player",
            "COPPERMOD_TFMX_PLAYER",
            "CopperMod.Tfmx.Player",
            identity);

        Assert.Equal(Path.Combine("Replayers", "EaglePlayer", "TFMX", "TFMX.player"), descriptor.DefaultRelativePath);
    }

    [Fact]
    public void UserCachePreservesRegisteredReplayerHierarchy()
    {
        var descriptor = Descriptor(Identity("rts", new byte[] { 0x4E, 0x75 }));
        AppContext.SetData(BinaryReplayerLoader.CacheAppContextDataKey, _root);
        try
        {
            Assert.Equal(Path.Combine(_root, descriptor.DefaultRelativePath), BinaryReplayerLoader.GetCachePath(descriptor));
        }
        finally
        {
            AppContext.SetData(BinaryReplayerLoader.CacheAppContextDataKey, null);
        }
    }

    [Fact]
    public void InvalidCachedBinaryIsRejectedInsteadOfTreatedAsMissing()
    {
        var descriptor = Descriptor(Identity("rts", new byte[] { 0x4E, 0x75 }));
        AppContext.SetData(BinaryReplayerLoader.CacheAppContextDataKey, _root);
        try
        {
            var path = BinaryReplayerLoader.GetCachePath(descriptor);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[] { 0x4E, 0x71 });

            var exception = Assert.Throws<ModuleLoadException>(() => BinaryReplayerLoader.TryLoadConfigured(descriptor, out _));

            Assert.Contains("has SHA256", exception.Message);
        }
        finally
        {
            AppContext.SetData(BinaryReplayerLoader.CacheAppContextDataKey, null);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static BinaryReplayerDescriptor Descriptor(params BinaryReplayerIdentity[] identities)
        => new("test", "Test", "Test", "player.bin", "COPPERMOD_TEST_PLAYER", "CopperMod.Test.Player", identities);

    private static BinaryReplayerIdentity Identity(string version, byte[] data)
        => new(version, data.Length, Convert.ToHexString(SHA256.HashData(data)));
}
