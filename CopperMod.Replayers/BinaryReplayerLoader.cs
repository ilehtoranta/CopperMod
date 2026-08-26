using System.Security.Cryptography;
using CopperMod.Abstractions;

namespace CopperMod.Replayers;

/// <summary>Locates and authenticates user-supplied native replay code.</summary>
public static class BinaryReplayerLoader
{
    public const string CacheEnvironmentVariable = "COPPERMOD_REPLAYER_CACHE";
    public const string CacheAppContextDataKey = "CopperMod.Replayers.CacheDirectory";

    /// <summary>Loads a configured binary, searching explicit settings, ancestor-local Replayers directories, and the user cache.</summary>
    public static byte[] LoadConfigured(BinaryReplayerDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (TryLoadConfigured(descriptor, out var binary))
        {
            return binary;
        }

        var identities = string.Join(", ", descriptor.Identities.Select(static identity => identity.Sha256));
        throw new ModuleLoadException(
            $"{descriptor.DisplayName} playback requires {descriptor.FileName}. " +
            $"Set {descriptor.EnvironmentVariable} to its full path, set AppContext data key {descriptor.AppContextDataKey}, " +
            $"place it at {descriptor.DefaultRelativePath} under the application or working directory, " +
            $"or install it in the cache at {GetCachePath(descriptor)}. Expected SHA256: {identities}.");
    }

    /// <summary>Tries to load an existing binary. Missing explicit paths and invalid existing files remain errors.</summary>
    public static bool TryLoadConfigured(BinaryReplayerDescriptor descriptor, out byte[] binary)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var configured = GetExplicitPath(descriptor);
        var path = configured ?? FindDefaultPath(descriptor, AppContext.BaseDirectory, Environment.CurrentDirectory);
        path ??= File.Exists(GetCachePath(descriptor)) ? GetCachePath(descriptor) : null;

        if (path is null)
        {
            binary = [];
            return false;
        }

        if (!File.Exists(path))
        {
            throw new ModuleLoadException($"The explicitly configured {descriptor.DisplayName} replay binary does not exist at '{path}'.");
        }

        try
        {
            binary = Validate(descriptor, File.ReadAllBytes(path), path);
            return true;
        }
        catch (ModuleLoadException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ModuleLoadException($"Could not read the configured {descriptor.DisplayName} replay binary at '{path}'.", ex);
        }
    }

    /// <summary>Returns whether this player has an explicit application or environment path.</summary>
    public static bool HasExplicitConfiguration(BinaryReplayerDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return GetExplicitPath(descriptor) is not null;
    }

    /// <summary>Gets the persistent per-user installation path for a player.</summary>
    public static string GetCachePath(BinaryReplayerDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return Path.Combine(GetCacheRoot(), descriptor.DefaultRelativePath);
    }

    /// <summary>Gets the configured persistent replayer cache root.</summary>
    public static string GetCacheRoot()
    {
        var root = AppContext.GetData(CacheAppContextDataKey) as string;
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Environment.GetEnvironmentVariable(CacheEnvironmentVariable);
        }

        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CopperMod");
        }

        return Path.GetFullPath(root);
    }

    /// <summary>Authenticates a binary against every trusted identity and returns a private copy.</summary>
    public static byte[] Validate(BinaryReplayerDescriptor descriptor, ReadOnlySpan<byte> binary, string? source = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var label = string.IsNullOrWhiteSpace(source) ? descriptor.FileName : source;
        var binaryLength = binary.Length;
        var sizeMatches = descriptor.Identities.Where(identity => identity.Size == binaryLength).ToArray();
        if (sizeMatches.Length == 0)
        {
            var expectedSizes = string.Join(" or ", descriptor.Identities.Select(static identity => identity.Size).Distinct());
            throw new ModuleLoadException(
                $"The {descriptor.DisplayName} replay binary '{label}' is {binary.Length} bytes; expected {expectedSizes} bytes.");
        }

        var actualHash = Convert.ToHexString(SHA256.HashData(binary));
        if (!sizeMatches.Any(identity => actualHash.Equals(identity.Sha256, StringComparison.Ordinal)))
        {
            var expectedHashes = string.Join(" or ", sizeMatches.Select(static identity => identity.Sha256));
            throw new ModuleLoadException(
                $"The {descriptor.DisplayName} replay binary '{label}' has SHA256 {actualHash}; expected {expectedHashes}.");
        }

        return binary.ToArray();
    }

    internal static string? FindDefaultPath(BinaryReplayerDescriptor descriptor, params string[] searchRoots)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var start in searchRoots)
        {
            if (string.IsNullOrWhiteSpace(start))
            {
                continue;
            }

            var directory = new DirectoryInfo(Path.GetFullPath(start));
            while (directory is not null && visited.Add(directory.FullName))
            {
                var candidate = Path.Combine(directory.FullName, descriptor.DefaultRelativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }

    private static string? GetExplicitPath(BinaryReplayerDescriptor descriptor)
    {
        var configured = AppContext.GetData(descriptor.AppContextDataKey) as string;
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = Environment.GetEnvironmentVariable(descriptor.EnvironmentVariable);
        }

        return string.IsNullOrWhiteSpace(configured) ? null : Path.GetFullPath(configured);
    }
}
