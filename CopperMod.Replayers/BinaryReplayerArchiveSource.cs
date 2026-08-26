namespace CopperMod.Replayers;

/// <summary>Describes an authenticated archive containing one binary replayer.</summary>
public sealed record BinaryReplayerArchiveSource
{
    public BinaryReplayerArchiveSource(Uri downloadUri, string archiveMemberPath, int archiveSize, string archiveSha256)
    {
        ArgumentNullException.ThrowIfNull(downloadUri);
        if (!downloadUri.IsAbsoluteUri || downloadUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("The archive URL must be an absolute HTTPS URL.", nameof(downloadUri));
        }

        if (string.IsNullOrWhiteSpace(archiveMemberPath) || Path.IsPathRooted(archiveMemberPath))
        {
            throw new ArgumentException("The archive member must be a relative path.", nameof(archiveMemberPath));
        }

        var segments = archiveMemberPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(static segment => segment is "." or ".."))
        {
            throw new ArgumentException("The archive member path cannot contain traversal.", nameof(archiveMemberPath));
        }

        if (archiveSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(archiveSize));
        }

        if (archiveSha256 is null || archiveSha256.Length != 64 || !archiveSha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("The archive SHA256 must contain 64 hexadecimal characters.", nameof(archiveSha256));
        }

        DownloadUri = downloadUri;
        ArchiveFileName = Path.GetFileName(downloadUri.LocalPath);
        if (string.IsNullOrWhiteSpace(ArchiveFileName))
        {
            throw new ArgumentException("The archive URL must end in a filename.", nameof(downloadUri));
        }

        ArchiveMemberPath = string.Join('/', segments);
        ArchiveSize = archiveSize;
        ArchiveSha256 = archiveSha256.ToUpperInvariant();
    }

    public Uri DownloadUri { get; }
    public string ArchiveFileName { get; }
    public string ArchiveMemberPath { get; }
    public int ArchiveSize { get; }
    public string ArchiveSha256 { get; }
}
