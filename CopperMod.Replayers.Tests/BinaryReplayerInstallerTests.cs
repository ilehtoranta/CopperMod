using System.Net;
using System.Security.Cryptography;

namespace CopperMod.Replayers.Tests;

[Collection("Binary replayer cache")]
public sealed class BinaryReplayerInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CopperMod-Installer-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DownloadsRegisteredLhaMemberAndReusesAuthenticatedCache()
    {
        byte[] player = [0x4E, 0x75, 1, 2, 3];
        const string member = "Package/EaglePlayers/TestPlayer";
        var archive = CreateStoredLha(member, player);
        var descriptor = Descriptor(player);
        var source = new BinaryReplayerArchiveSource(
            new Uri("https://example.test/player.lha"),
            member,
            archive.Length,
            Convert.ToHexString(SHA256.HashData(archive)));
        var handler = new ArchiveHandler(archive);
        using var client = new HttpClient(handler);
        AppContext.SetData(BinaryReplayerLoader.CacheAppContextDataKey, _root);
        try
        {
            var installed = await BinaryReplayerInstaller.EnsureInstalledAsync(descriptor, source, client);
            File.Delete(BinaryReplayerLoader.GetCachePath(descriptor));
            var cached = await BinaryReplayerInstaller.EnsureInstalledAsync(descriptor, source, client);

            Assert.Equal(player, installed);
            Assert.Equal(player, cached);
            Assert.Equal(player, File.ReadAllBytes(BinaryReplayerLoader.GetCachePath(descriptor)));
            Assert.Equal(archive, File.ReadAllBytes(BinaryReplayerInstaller.GetArchiveCachePath(source)));
            Assert.Equal(1, handler.RequestCount);
        }
        finally
        {
            AppContext.SetData(BinaryReplayerLoader.CacheAppContextDataKey, null);
        }
    }

    [Fact]
    public async Task RejectsChangedArchiveBeforeExtractionOrInstallation()
    {
        byte[] player = [0x4E, 0x75];
        var archive = CreateStoredLha("Player", player);
        var descriptor = Descriptor(player);
        var source = new BinaryReplayerArchiveSource(
            new Uri("https://example.test/player.lha"),
            "Player",
            archive.Length,
            new string('0', 64));
        using var client = new HttpClient(new ArchiveHandler(archive));
        AppContext.SetData(BinaryReplayerLoader.CacheAppContextDataKey, _root);
        try
        {
            var exception = await Assert.ThrowsAsync<CopperMod.Abstractions.ModuleLoadException>(
                () => BinaryReplayerInstaller.EnsureInstalledAsync(descriptor, source, client));

            Assert.Contains("has SHA256", exception.Message);
            Assert.False(File.Exists(BinaryReplayerLoader.GetCachePath(descriptor)));
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

    private static BinaryReplayerDescriptor Descriptor(byte[] player)
        => new(
            "installer-test",
            "installer test player",
            Path.Combine("EaglePlayer", "Test"),
            "TestPlayer",
            "COPPERMOD_INSTALLER_TEST_PLAYER",
            "CopperMod.Tests.InstallerPlayer",
            new BinaryReplayerIdentity("test", player.Length, Convert.ToHexString(SHA256.HashData(player))));

    private static byte[] CreateStoredLha(string memberPath, byte[] data)
    {
        var name = System.Text.Encoding.ASCII.GetBytes(memberPath);
        using var body = new MemoryStream();
        using (var writer = new BinaryWriter(body, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("-lh0-"));
            writer.Write(data.Length);
            writer.Write(data.Length);
            writer.Write(0);
            writer.Write((byte)0x20);
            writer.Write((byte)0);
            writer.Write((byte)name.Length);
            writer.Write(name);
            writer.Write(Crc16(data));
        }

        var header = body.ToArray();
        using var archive = new MemoryStream();
        archive.WriteByte((byte)header.Length);
        archive.WriteByte(unchecked((byte)header.Sum(static value => value)));
        archive.Write(header);
        archive.Write(data);
        archive.WriteByte(0);
        return archive.ToArray();
    }

    private static ushort Crc16(byte[] data)
    {
        ushort crc = 0;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (ushort)((crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1);
            }
        }

        return crc;
    }

    private sealed class ArchiveHandler(byte[] archive) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(archive),
                RequestMessage = request
            });
        }
    }
}
