using System.Security.Cryptography;
using CopperMod.Abstractions;
using CopperMod.Cust;
using CopperMod.Replayers;

namespace CopperMod.EaglePlayer.Tests;

public sealed class EaglePlayerSessionTests
{
    [Fact]
    public void TfmxPlayerUsesRegisteredDirectoryAndIdentity()
    {
        Assert.Equal(Path.Combine("Replayers", "EaglePlayer", "TFMX", "TFMX"), TfmxReplayerBinary.DefaultRelativePath);
        Assert.Equal(8_008, TfmxReplayerBinary.ExpectedSize);
        Assert.Equal("FDF114E1D76EC0FC1D6E7471EC63FE93F0A043D367A562B8B2A398B74538DC21", TfmxReplayerBinary.ExpectedSha256);
        Assert.Equal("https://aminet.net/mus/play/EP_TFMX.lha", TfmxReplayerBinary.ArchiveUrl);
        Assert.Equal("TFMX/EaglePlayers/TFMX", TfmxReplayerBinary.ArchiveMemberPath);
        Assert.Equal(11_074, TfmxReplayerBinary.ExpectedArchiveSize);
        Assert.Equal("72C8882D4AB5E2FAB50C03A1D02D19468374A814C87DBD55D582C2254D59541F", TfmxReplayerBinary.ExpectedArchiveSha256);
    }

    [Fact]
    public void RegistersEveryAuditedEaglePlayerArchiveAsset()
    {
        Assert.Equal(114, EaglePlayerReplayerCatalog.Players.Count);
        Assert.Equal(4, EaglePlayerReplayerCatalog.Companions.Count);
        Assert.Equal(118, EaglePlayerReplayerCatalog.Assets.Count);
        Assert.Equal(118, EaglePlayerReplayerCatalog.Assets.Select(static asset => asset.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(EaglePlayerReplayerCatalog.Assets, static asset =>
        {
            Assert.Equal(EaglePlayerReplayerCatalog.ArchiveUrl, asset.ArchiveSource.DownloadUri.AbsoluteUri);
            Assert.Equal(EaglePlayerReplayerCatalog.ExpectedArchiveSize, asset.ArchiveSource.ArchiveSize);
            Assert.Equal(EaglePlayerReplayerCatalog.ExpectedArchiveSha256, asset.ArchiveSource.ArchiveSha256);
            Assert.Single(asset.Descriptor.Identities);
            Assert.StartsWith(Path.Combine("Replayers", "EaglePlayer"), asset.DefaultRelativePath);
        });
    }

    [Theory]
    [InlineData("futurecomposer1-3", "FutureComposer1.3", 6224)]
    [InlineData("futurecomposer1-4", "FutureComposer1.4", 5212)]
    [InlineData("fred", "Fred", 3560)]
    [InlineData("hippel", "Hippel", 5568)]
    [InlineData("jamcracker", "JamCracker", 4916)]
    [InlineData("sidmon-1-0", "SIDMon 1.0", 5808)]
    [InlineData("soundmon", "SoundMon", 4264)]
    [InlineData("tfmx-7v", "TFMX_7V", 45864)]
    [InlineData("tfmx-pro", "TFMX_Pro", 13488)]
    public void FindsCommonPlayersByStableId(string id, string displayName, int size)
    {
        var registration = EaglePlayerReplayerCatalog.Get(id);

        Assert.Equal(EaglePlayerAssetKind.Player, registration.Kind);
        Assert.Equal(displayName, registration.DisplayName);
        Assert.Equal(size, registration.Descriptor.Identities[0].Size);
    }

    [Fact]
    public void CompanionEngineCannotBeStartedAsPlayerPlugin()
    {
        var companion = EaglePlayerReplayerCatalog.Companions[0];

        Assert.Throws<ArgumentException>(
            () => EaglePlayerSession.LoadConfigured(companion, new byte[] { 1 }));
    }

    [Fact]
    public void FindsAndVerifiesLocallyInstalledTfmxPlayer()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        var path = Path.Combine(root, TfmxReplayerBinary.DefaultRelativePath);
        if (!File.Exists(path))
        {
            return;
        }

        var player = TfmxReplayerBinary.LoadConfigured();

        Assert.Equal(TfmxReplayerBinary.ExpectedSize, player.Length);
        Assert.Equal(TfmxReplayerBinary.ExpectedSha256, Convert.ToHexString(SHA256.HashData(player)));
    }

    [Fact]
    public void RunsAuthenticatedPlayerWithSeparateModuleMemory()
    {
        var player = CreateMinimalPlayerHunk();
        byte[] module = [0xDE, 0xAD, 0xBE, 0xEF, 0x11, 0x22];

        using var session = EaglePlayerSession.Create(Descriptor(player), player, module);

        Assert.True(session.Machine.UsesA500PalCustPlaybackProfile);
        Assert.True(session.Machine.UsesLiveAgnusDma);
        Assert.Equal(module.Length, session.Machine.ListDataLength);
        Assert.NotEqual(CustConstants.DefaultModuleBaseAddress, session.Machine.ListDataAddress);
        Assert.Equal(module, session.Machine.ReadListData());
        Assert.Equal(session.Machine.ListDataAddress, session.Machine.ReadHostBlockLong(CustConstants.DtgCheckDataOffset));
        Assert.Equal((uint)module.Length, session.Machine.ReadHostBlockLong(CustConstants.DtgCheckSizeOffset));
    }

    [Fact]
    public void AcceptsNormalEaglePlayerTagTableWithoutCustomPlayerMarker()
    {
        var player = CreateMinimalPlayerHunk(includeCustomPlayerTag: false);

        using var session = EaglePlayerSession.Create(Descriptor(player), player, new byte[] { 1, 2, 3, 4 });

        Assert.True(session.Machine.UsesA500PalCustPlaybackProfile);
    }

    [Fact]
    public void ResetReinstallsSeparateModuleAtStableAddress()
    {
        var player = CreateMinimalPlayerHunk();
        byte[] module = [1, 2, 3, 4];
        using var session = EaglePlayerSession.Create(Descriptor(player), player, module);
        var address = session.Machine.ListDataAddress;
        session.Machine.WriteListDataByte(0, 0xFF);

        session.Reset();

        Assert.Equal(address, session.Machine.ListDataAddress);
        Assert.Equal(module, session.Machine.ReadListData());
    }

    [Fact]
    public void SilentPlayerStillAdvancesCanonicalCycleTimeline()
    {
        var player = CreateMinimalPlayerHunk();
        using var session = EaglePlayerSession.Create(Descriptor(player), player, new byte[] { 1, 2, 3, 4 });
        var frames = session.GetCurrentTickFrameCount(44_100);
        var samples = new float[frames * 2];
        var startCycle = session.Cycle;

        session.RenderQuantum(samples, frames, 2, 44_100);

        Assert.Equal(startCycle + session.QuantumCycleCount, session.Cycle);
        Assert.All(samples, sample => Assert.True(float.IsFinite(sample)));
    }

    [Fact]
    public void RejectsUnknownPlayerIdentityBeforeParsingGuestCode()
    {
        var trusted = CreateMinimalPlayerHunk();
        var altered = trusted.ToArray();
        altered[^1] ^= 1;

        var exception = Assert.Throws<ModuleLoadException>(
            () => EaglePlayerSession.Create(Descriptor(trusted), altered, new byte[] { 1 }));

        Assert.Contains("has SHA256", exception.Message);
    }

    [Fact]
    public void RejectsAuthenticatedNonHunkPlayer()
    {
        byte[] player = [1, 2, 3, 4];

        Assert.Throws<UnsupportedModuleFormatException>(
            () => EaglePlayerSession.Create(Descriptor(player), player, new byte[] { 1 }));
    }

    [Fact]
    public void RejectsEmptyModuleBeforeStartingGuestCode()
    {
        var player = CreateMinimalPlayerHunk();

        Assert.Throws<ModuleLoadException>(
            () => EaglePlayerSession.Create(Descriptor(player), player, ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void RejectsPlayerAndModuleThatExceedChipRam()
    {
        var player = CreateMinimalPlayerHunk();
        var oversizedModule = new byte[CustConstants.DefaultChipRamSize];

        Assert.Throws<ModuleLoadException>(
            () => EaglePlayerSession.Create(Descriptor(player), player, oversizedModule));
    }

    private static BinaryReplayerDescriptor Descriptor(byte[] player)
        => new(
            "synthetic-eagleplayer",
            "synthetic EaglePlayer",
            Path.Combine("EaglePlayer", "Synthetic"),
            "Synthetic.player",
            "COPPERMOD_SYNTHETIC_EAGLEPLAYER",
            "CopperMod.EaglePlayer.SyntheticPath",
            new BinaryReplayerIdentity(
                "synthetic",
                player.Length,
                Convert.ToHexString(SHA256.HashData(player))));

    private static byte[] CreateMinimalPlayerHunk(bool includeCustomPlayerTag = true)
    {
        const int segmentWords = 20;
        const uint routineOffset = 72;
        var segment = new byte[segmentWords * 4];
        var tagOffset = 0;
        if (includeCustomPlayerTag)
        {
            WriteLong(segment, tagOffset, CustConstants.DtpCustomPlayer);
            WriteLong(segment, tagOffset + 4, 1);
            tagOffset += 8;
        }

        WriteLong(segment, tagOffset, CustConstants.DtpPlayerVersion);
        WriteLong(segment, tagOffset + 4, 1);
        var initPlayerValueOffset = tagOffset + 12;
        var initSoundValueOffset = tagOffset + 20;
        WriteLong(segment, tagOffset + 8, CustConstants.DtpInitPlayer);
        WriteLong(segment, initPlayerValueOffset, routineOffset);
        WriteLong(segment, tagOffset + 16, CustConstants.DtpInitSound);
        WriteLong(segment, initSoundValueOffset, routineOffset);
        WriteLong(segment, tagOffset + 24, CustConstants.TagDone);
        WriteLong(segment, tagOffset + 28, 0);
        segment[routineOffset] = 0x4E;
        segment[routineOffset + 1] = 0x75;

        var words = new List<uint>
        {
            HunkParser.HunkHeader,
            0,
            1,
            0,
            0,
            segmentWords,
            HunkParser.HunkCode,
            segmentWords
        };
        for (var offset = 0; offset < segment.Length; offset += 4)
        {
            words.Add(ReadLong(segment, offset));
        }

        words.Add(HunkParser.HunkReloc32);
        words.Add(2);
        words.Add(0);
        words.Add((uint)initPlayerValueOffset);
        words.Add((uint)initSoundValueOffset);
        words.Add(0);
        words.Add(HunkParser.HunkEnd);

        var result = new byte[words.Count * 4];
        for (var i = 0; i < words.Count; i++)
        {
            WriteLong(result, i * 4, words[i]);
        }

        return result;
    }

    private static uint ReadLong(byte[] data, int offset)
        => ((uint)data[offset] << 24) |
            ((uint)data[offset + 1] << 16) |
            ((uint)data[offset + 2] << 8) |
            data[offset + 3];

    private static void WriteLong(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)value;
    }
}
