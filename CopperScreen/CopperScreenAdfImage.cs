using System.IO.Compression;

namespace CopperScreen;

// Owned mount-time bytes, not a Legacy device or decoded-track adapter.
internal sealed class CopperScreenAdfImage
{
    public const int StandardAdfSize = 901120;
    private CopperScreenAdfImage(byte[] data, string name) { Data = data; Name = name; }
    public byte[] Data { get; }
    public string Name { get; }
    public static CopperScreenAdfImage FromAdfBytes(byte[] data, string name)
    {
        if (data.Length != StandardAdfSize)
            throw new NotSupportedException("Lightweight requires a complete standard 880 KiB ADF.");
        _ = CopperDisk.AmigaDiskLoader.FromAdfBytes(data);
        return new(data, name);
    }
    public static CopperScreenAdfImage Load(string path)
    {
        if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = ZipFile.OpenRead(path);
            var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name) &&
                Path.GetExtension(e.Name).Equals(".adf", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (entries.Length != 1)
                throw new NotSupportedException("Select one ADF entry explicitly from this ZIP archive.");
            var entry = entries[0];
            if (entry.Length != StandardAdfSize)
                throw new NotSupportedException("Lightweight requires a complete standard 880 KiB ADF.");
            using var input = entry.Open();
            var bytes = new byte[StandardAdfSize];
            input.ReadExactly(bytes);
            return FromAdfBytes(bytes, entry.Name);
        }
        if (!Path.GetExtension(path).Equals(".adf", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("This build supports standard ADF media only, including ADF in ZIP.");
        using var stream = File.OpenRead(path);
        if (stream.Length != StandardAdfSize)
            throw new NotSupportedException("Lightweight requires a complete standard 880 KiB ADF.");
        var data = new byte[StandardAdfSize];
        stream.ReadExactly(data);
        return FromAdfBytes(data, Path.GetFileName(path));
    }
}
