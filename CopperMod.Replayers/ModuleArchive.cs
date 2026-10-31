using System.IO.Compression;
using CopperMod.Abstractions;
using Hst.Compression.Lha;

namespace CopperMod.Replayers;

/// <summary>An in-memory ZIP or Amiga LhA archive of module files.</summary>
public sealed class ModuleArchive
{
    private const int MaxEntries = 4096;
    private const long MaxMember = 64L * 1024 * 1024;
    private const long MaxTotal = 256L * 1024 * 1024;
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private long _total;

    /// <summary>Detects ZIP and LhA by their signatures.</summary>
    public static bool IsArchive(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && data[0] == 0x50 && data[1] == 0x4b &&
            ((data[2] == 3 && data[3] == 4) || (data[2] == 5 && data[3] == 6) || (data[2] == 7 && data[3] == 8)) ||
        data.Length >= 7 && data[2] == '-' && data[3] == 'l' && (data[4] == 'h' || data[4] == 'z') && data[6] == '-';

    /// <summary>Reads and validates an archive without writing to disk.</summary>
    public static ModuleArchive Open(ReadOnlyMemory<byte> bytes)
    {
        if (!IsArchive(bytes.Span)) throw new ModuleLoadException("Input is not a ZIP or LhA archive.");
        var result = new ModuleArchive();
        try
        {
            using var input = new MemoryStream(bytes.ToArray(), false);
            if (bytes.Span[0] == 0x50 && bytes.Span[1] == 0x4b)
            {
                using var zip = new ZipArchive(input, ZipArchiveMode.Read);
                if (zip.Entries.Count > MaxEntries) throw new ModuleLoadException("Archive contains too many entries.");
                foreach (var entry in zip.Entries)
                {
                    var name = ValidateName(entry.FullName, entry.Name.Length == 0);
                    if (entry.Name.Length == 0) continue;
                    result.CheckSize(entry.Length);
                    using var source = entry.Open();
                    using var output = new LimitedStream(MaxMember, MaxTotal - result._total);
                    source.CopyTo(output);
                    result.Add(name, output.ToArray());
                }
            }
            else
            {
                using var lha = new LhaArchive(input, LhaOptions.AmigaLhaOptions);
                var entries = lha.Entries().GetAwaiter().GetResult().ToArray();
                if (entries.Length > MaxEntries) throw new ModuleLoadException("Archive contains too many entries.");
                foreach (var entry in entries)
                {
                    var directory = entry.Name.EndsWith('/') || entry.Name.EndsWith('\\');
                    var name = ValidateName(entry.Name, directory);
                    if (directory) continue;
                    result.CheckSize(entry.OriginalSize);
                    using var output = new LimitedStream(MaxMember, MaxTotal - result._total);
                    lha.Extract(entry, output);
                    result.Add(name, output.ToArray());
                }
            }
            return result;
        }
        catch (ModuleLoadException) { throw; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            throw new ModuleLoadException("Could not read archive: " + ex.Message, ex);
        }
    }

    /// <summary>Archive member names in deterministic order.</summary>
    public IReadOnlyList<string> Members => _files.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ThenBy(x => x, StringComparer.Ordinal).ToArray();

    /// <summary>Creates a load context for a member and its archived companions.</summary>
    public ModuleLoadContext CreateContext(string member)
    {
        var name = ValidateName(member, false);
        if (!_files.TryGetValue(name, out var data)) throw new ModuleLoadException($"Archive member '{member}' was not found.");
        var lastSlash = name.LastIndexOf('/');
        var directory = lastSlash < 0 ? "" : name[..(lastSlash + 1)];
        var virtualPath = Path.Combine(Path.GetTempPath(), "CopperModArchive", name.Replace('/', Path.DirectorySeparatorChar));
        return new ModuleLoadContext(data, virtualPath, null, relative =>
        {
            try
            {
                var candidate = ValidateName(directory + relative.Replace('\\', '/'), false);
                return _files.TryGetValue(candidate, out var companion) ? companion : null;
            }
            catch (ModuleLoadException) { return null; }
        });
    }

    private static string ValidateName(string name, bool directory)
    {
        name = name.Replace('\\', '/');
        if (directory) name = name.TrimEnd('/');
        if (string.IsNullOrEmpty(name) || name.StartsWith('/') || name.Contains(':') ||
            name.Split('/').Any(part => part is "" or "." or ".."))
            throw new ModuleLoadException($"Unsafe archive member path '{name}'.");
        return name;
    }

    private void CheckSize(long length)
    {
        if (length < 0 || length > MaxMember || length > MaxTotal - _total)
            throw new ModuleLoadException("Archive exceeds the extracted size limit.");
    }
    private void Add(string name, byte[] bytes)
    {
        CheckSize(bytes.Length);
        if (!_files.TryAdd(name, bytes)) throw new ModuleLoadException($"Duplicate archive member '{name}'.");
        _total += bytes.Length;
    }
    private sealed class LimitedStream(long memberLimit, long totalLimit) : MemoryStream
    {
        private void Check(int count)
        {
            if (Position + count > memberLimit || Position + count > totalLimit)
                throw new ModuleLoadException("Archive exceeds the extracted size limit.");
        }
        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { Check(1); base.WriteByte(value); }
        public override void SetLength(long value)
        {
            if (value > memberLimit || value > totalLimit) throw new ModuleLoadException("Archive exceeds the extracted size limit.");
            base.SetLength(value);
        }
    }
}
