namespace CopperMod.Replayers;

/// <summary>Defines how a user-supplied native replayer is found and authenticated.</summary>
public sealed class BinaryReplayerDescriptor
{
    /// <summary>Creates a binary replayer descriptor.</summary>
    public BinaryReplayerDescriptor(
        string id,
        string displayName,
        string relativeDirectory,
        string fileName,
        string environmentVariable,
        string appContextDataKey,
        params BinaryReplayerIdentity[] identities)
    {
        Id = RequireToken(id, nameof(id));
        DisplayName = RequireText(displayName, nameof(displayName));
        RelativeDirectory = RequireRelativeDirectory(relativeDirectory, nameof(relativeDirectory));
        FileName = RequireFileName(fileName, nameof(fileName));
        EnvironmentVariable = RequireToken(environmentVariable, nameof(environmentVariable));
        AppContextDataKey = RequireText(appContextDataKey, nameof(appContextDataKey));
        if (identities is null || identities.Length == 0 || identities.Any(static identity => identity is null))
        {
            throw new ArgumentException("At least one trusted binary identity is required.", nameof(identities));
        }

        Identities = Array.AsReadOnly(identities.ToArray());
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string RelativeDirectory { get; }
    public string FileName { get; }
    public string EnvironmentVariable { get; }
    public string AppContextDataKey { get; }
    public IReadOnlyList<BinaryReplayerIdentity> Identities { get; }
    public string DefaultRelativePath => Path.Combine("Replayers", RelativeDirectory, FileName);

    private static string RequireToken(string value, string parameterName)
    {
        value = RequireText(value, parameterName);
        if (value.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("The value cannot contain whitespace.", parameterName);
        }

        return value;
    }

    private static string RequireFileName(string value, string parameterName)
    {
        value = RequireText(value, parameterName);
        if (!string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal) || value is "." or "..")
        {
            throw new ArgumentException("The value must be one file or directory name, not a path.", parameterName);
        }

        return value;
    }

    private static string RequireRelativeDirectory(string value, string parameterName)
    {
        value = RequireText(value, parameterName);
        if (Path.IsPathRooted(value))
        {
            throw new ArgumentException("The value must be a relative directory.", parameterName);
        }

        var segments = value.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".." || Path.GetFileName(segment) != segment))
        {
            throw new ArgumentException("The value must be a safe relative directory without traversal.", parameterName);
        }

        return Path.Combine(segments);
    }

    private static string RequireText(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The value is required.", parameterName)
            : value;
}
