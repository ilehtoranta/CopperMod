using System.Reflection;

namespace CopperScreen;

internal static class CopperStartMetadata
{
	private const string ShaMetadataKey = "CopperStartSha";
	private const string FallbackGitSha = "unknown";

	public const string DisplayVersion = "CopperStart 1.3";

	public static string GitSha { get; } = ResolveGitSha();

	private static string ResolveGitSha()
	{
		foreach (var attribute in typeof(CopperStartMetadata).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
		{
			if (attribute.Key == ShaMetadataKey && !string.IsNullOrWhiteSpace(attribute.Value))
			{
				return attribute.Value;
			}
		}

		return FallbackGitSha;
	}
}
