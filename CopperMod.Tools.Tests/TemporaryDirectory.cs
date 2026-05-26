namespace CopperMod.Tools.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
	private TemporaryDirectory(string path)
	{
		Path = path;
		Directory.CreateDirectory(path);
	}

	public string Path { get; }

	public static TemporaryDirectory Create()
	{
		return new TemporaryDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CopperMod.Tools.Tests-" + Guid.NewGuid().ToString("N")));
	}

	public void Dispose()
	{
		if (Directory.Exists(Path))
		{
			Directory.Delete(Path, recursive: true);
		}
	}
}
