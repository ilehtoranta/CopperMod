using System.IO.Compression;
using System.Text;
using CopperMod.Abstractions;
using CopperMod.Rendering;
using CopperMod.Replayers;

namespace CopperMod.Tests;

public sealed class ModuleArchiveTests
{
    [Fact]
    public void ZipProvidesSortedMembersAndIsolatedCompanions()
    {
        var archive = ModuleArchive.Open(Zip(("music/z.prg", new byte[] { 1, 2, 3 }), ("music/A.sid", new byte[] { 4 }), ("music/z.sam", new byte[] { 9 })));
        Assert.Equal(new[] { "music/A.sid", "music/z.prg", "music/z.sam" }, archive.Members);
        var context = archive.CreateContext("music/z.prg").WithRelativeFileAliases(new Dictionary<string, string> { ["smpl.z.prg"] = "z.sam" });
        Assert.True(context.TryReadRelativeFile("smpl.z.prg", out var bytes));
        Assert.Equal(new byte[] { 9 }, bytes);
        Assert.False(context.TryReadRelativeFile("../z.sam", out _));
    }

    [Theory]
    [InlineData("../bad")]
    [InlineData("/bad")]
    [InlineData("C:/bad")]
    public void RejectsUnsafePaths(string name)
        => Assert.Throws<ModuleLoadException>(() => ModuleArchive.Open(Zip((name, new byte[] { 1 }))));

    [Fact]
    public void RejectsCaseInsensitiveDuplicates()
        => Assert.Throws<ModuleLoadException>(() => ModuleArchive.Open(Zip(("A", new byte[] { 1 }), ("a", new byte[] { 2 }))));

    [Fact]
    public void LhaStoredMemberIsReadable()
    {
        var bytes = StoredLha("song.prg", new byte[] { 1, 2, 3 });
        Assert.True(ModuleArchive.IsArchive(bytes));
        var archive = ModuleArchive.Open(bytes);
        Assert.Equal(new[] { "song.prg" }, archive.Members);
        Assert.Equal(new byte[] { 1, 2, 3 }, archive.CreateContext("song.prg").Data.ToArray());
        Assert.Equal(new[] { "song.prg" }, ModuleFormatRegistry.GetArchiveMembers(archive));
    }

    [Fact]
    public void EmptyArchiveHasNoPlayableSongs()
    {
        var bytes = Zip(("readme.txt", new byte[] { 1, 2 }));
        Assert.Empty(ModuleFormatRegistry.GetArchiveMembers(ModuleArchive.Open(bytes)));
        Assert.Throws<ModuleLoadException>(() => ModuleFormatRegistry.Load(bytes));
    }

    [Fact]
    public void PlaylistDetectsAndOrdersPlayableFilesAndRejectsMissingMember()
    {
        var archive = ModuleArchive.Open(Zip(("z.prg", new byte[] { 1, 2, 3 }), ("readme.txt", new byte[] { 1 }), ("a.prg", new byte[] { 4, 5, 6 })));
        Assert.Equal(new[] { "a.prg", "z.prg" }, ModuleFormatRegistry.GetArchiveMembers(archive));
        Assert.Throws<ModuleLoadException>(() => archive.CreateContext("missing.prg"));
        using var song = ModuleFormatRegistry.LoadArchiveMember(archive, "z.prg");
        Assert.NotNull(song);
    }

    [Fact]
    public void RejectsMalformedAndExcessiveArchives()
    {
        Assert.Throws<ModuleLoadException>(() => ModuleArchive.Open(new byte[] { 0x50, 0x4b, 3, 4 }));
        var files = Enumerable.Range(0, 4097).Select(i => ($"{i}.txt", Array.Empty<byte>())).ToArray();
        Assert.Throws<ModuleLoadException>(() => ModuleArchive.Open(Zip(files)));
    }

    [Fact]
    public void RejectsOversizedExtractedMember()
    {
        var bytes = Zip(("large.bin", new byte[64 * 1024 * 1024 + 1]));
        Assert.Throws<ModuleLoadException>(() => ModuleArchive.Open(bytes));
    }

    [Fact]
    public void NestedArchivesAreNotPlaylistEntries()
    {
        var inner = Zip(("song.prg", new byte[] { 1, 2, 3 }));
        var archive = ModuleArchive.Open(Zip(("inner.zip", inner)));
        Assert.Empty(ModuleFormatRegistry.GetArchiveMembers(archive));
        Assert.Throws<ModuleLoadException>(() => ModuleFormatRegistry.LoadArchiveMember(archive, "inner.zip"));
    }

    private static byte[] Zip(params (string Name, byte[] Data)[] files)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var file in files)
            {
                using var output = zip.CreateEntry(file.Name).Open();
                output.Write(file.Data);
            }
        return stream.ToArray();
    }

    private static byte[] StoredLha(string name, byte[] data)
    {
        var nameBytes = Encoding.ASCII.GetBytes(name);
        using var body = new MemoryStream();
        using (var writer = new BinaryWriter(body, Encoding.ASCII, true))
        {
            writer.Write(Encoding.ASCII.GetBytes("-lh0-"));
            writer.Write(data.Length); writer.Write(data.Length); writer.Write(0);
            writer.Write((byte)0x20); writer.Write((byte)0); writer.Write((byte)nameBytes.Length); writer.Write(nameBytes);
            ushort crc = 0;
            foreach (var value in data)
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++) crc = (ushort)((crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1);
            }
            writer.Write(crc);
        }
        var header = body.ToArray();
        using var output = new MemoryStream();
        output.WriteByte((byte)header.Length); output.WriteByte(unchecked((byte)header.Sum(x => x)));
        output.Write(header); output.Write(data); output.WriteByte(0);
        return output.ToArray();
    }
}
