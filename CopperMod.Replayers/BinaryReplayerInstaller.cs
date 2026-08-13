using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using CopperMod.Abstractions;
using Hst.Compression.Lha;

namespace CopperMod.Replayers;

/// <summary>Downloads, authenticates, extracts, and atomically caches external replayers.</summary>
public static class BinaryReplayerInstaller
{
    private const int MaximumArchiveSize = 4 * 1024 * 1024;
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();

    public static byte[] EnsureInstalled(
        BinaryReplayerDescriptor descriptor,
        BinaryReplayerArchiveSource source,
        HttpClient? httpClient = null,
        CancellationToken cancellationToken = default)
        => EnsureInstalledAsync(descriptor, source, httpClient, cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();

    public static async Task<byte[]> EnsureInstalledAsync(
        BinaryReplayerDescriptor descriptor,
        BinaryReplayerArchiveSource source,
        HttpClient? httpClient = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(source);

        if (BinaryReplayerLoader.TryLoadConfigured(descriptor, out var existing))
        {
            return existing;
        }

        byte[] archive;
        try
        {
            archive = await GetArchiveAsync(httpClient ?? SharedHttpClient, source, cancellationToken).ConfigureAwait(false);
        }
        catch (ModuleLoadException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            throw new ModuleLoadException(
                $"Could not download {descriptor.DisplayName} from '{source.DownloadUri}'. " +
                $"You can install it manually at '{BinaryReplayerLoader.GetCachePath(descriptor)}'.", ex);
        }

        var player = await ExtractLhaMemberAsync(archive, source.ArchiveMemberPath).ConfigureAwait(false);
        player = BinaryReplayerLoader.Validate(descriptor, player, source.ArchiveMemberPath);
        var destination = BinaryReplayerLoader.GetCachePath(descriptor);
        await InstallAtomicallyAsync(descriptor, player, destination, cancellationToken).ConfigureAwait(false);
        return player;
    }

    /// <summary>Gets the authenticated archive's persistent cache path.</summary>
    public static string GetArchiveCachePath(BinaryReplayerArchiveSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Path.Combine(BinaryReplayerLoader.GetCacheRoot(), "ReplayerArchives", source.ArchiveSha256, source.ArchiveFileName);
    }

    internal static async Task<byte[]> ExtractLhaMemberAsync(byte[] archive, string memberPath)
    {
        try
        {
            using var input = new MemoryStream(archive, writable: false);
            using var lha = new LhaArchive(input, LhaOptions.AmigaLhaOptions);
            var wanted = NormalizeMemberPath(memberPath);
            var entry = (await lha.Entries().ConfigureAwait(false))
                .SingleOrDefault(candidate => string.Equals(NormalizeMemberPath(candidate.Name), wanted, StringComparison.Ordinal));
            if (entry is null)
            {
                throw new ModuleLoadException($"The authenticated LhA archive does not contain registered member '{memberPath}'.");
            }

            if (entry.OriginalSize < 0 || entry.OriginalSize > MaximumArchiveSize)
            {
                throw new ModuleLoadException($"Archive member '{memberPath}' has an unsafe extracted size of {entry.OriginalSize} bytes.");
            }

            using var output = new MemoryStream((int)entry.OriginalSize);
            lha.Extract(entry, output);
            return output.ToArray();
        }
        catch (ModuleLoadException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException)
        {
            throw new ModuleLoadException($"Could not extract registered member '{memberPath}' from the authenticated LhA archive.", ex);
        }
    }

    private static async Task<byte[]> DownloadAsync(HttpClient client, BinaryReplayerArchiveSource source, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(source.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new HttpRequestException($"The archive server returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }

        if (response.Content.Headers.ContentLength is > MaximumArchiveSize)
        {
            throw new ModuleLoadException($"The archive response exceeds the {MaximumArchiveSize}-byte safety limit.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream(source.ArchiveSize);
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return output.ToArray();
            }

            if (output.Length + read > MaximumArchiveSize)
            {
                throw new ModuleLoadException($"The archive response exceeds the {MaximumArchiveSize}-byte safety limit.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<byte[]> GetArchiveAsync(
        HttpClient client,
        BinaryReplayerArchiveSource source,
        CancellationToken cancellationToken)
    {
        var cachePath = GetArchiveCachePath(source);
        if (File.Exists(cachePath))
        {
            var cached = await File.ReadAllBytesAsync(cachePath, cancellationToken).ConfigureAwait(false);
            ValidateArchive(source, cached);
            return cached;
        }

        var downloaded = await DownloadAsync(client, source, cancellationToken).ConfigureAwait(false);
        ValidateArchive(source, downloaded);
        await InstallArchiveAtomicallyAsync(source, downloaded, cachePath, cancellationToken).ConfigureAwait(false);
        return downloaded;
    }

    private static void ValidateArchive(BinaryReplayerArchiveSource source, byte[] archive)
    {
        if (archive.Length != source.ArchiveSize)
        {
            throw new ModuleLoadException(
                $"Downloaded archive '{source.DownloadUri}' is {archive.Length} bytes; expected {source.ArchiveSize} bytes.");
        }

        var hash = Convert.ToHexString(SHA256.HashData(archive));
        if (!hash.Equals(source.ArchiveSha256, StringComparison.Ordinal))
        {
            throw new ModuleLoadException(
                $"Downloaded archive '{source.DownloadUri}' has SHA256 {hash}; expected {source.ArchiveSha256}.");
        }
    }

    private static async Task InstallAtomicallyAsync(
        BinaryReplayerDescriptor descriptor,
        byte[] player,
        string destination,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{descriptor.FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, player, cancellationToken).ConfigureAwait(false);
            try
            {
                File.Move(temporary, destination, overwrite: false);
            }
            catch (IOException) when (File.Exists(destination))
            {
                BinaryReplayerLoader.Validate(descriptor, await File.ReadAllBytesAsync(destination, cancellationToken).ConfigureAwait(false), destination);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static async Task InstallArchiveAtomicallyAsync(
        BinaryReplayerArchiveSource source,
        byte[] archive,
        string destination,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{source.ArchiveFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, archive, cancellationToken).ConfigureAwait(false);
            try
            {
                File.Move(temporary, destination, overwrite: false);
            }
            catch (IOException) when (File.Exists(destination))
            {
                ValidateArchive(source, await File.ReadAllBytesAsync(destination, cancellationToken).ConfigureAwait(false));
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string NormalizeMemberPath(string path)
        => string.Join('/', path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries));

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CopperMod", "1.0"));
        return client;
    }
}
