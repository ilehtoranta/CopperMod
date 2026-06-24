namespace CopperPad.Gui;

internal static class CrashLog
{
	public static string Path { get; } = System.IO.Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
		"CopperMod",
		"CopperPad",
		"CopperPad.Gui.crash.log");

	public static void Write(string context, Exception exception)
	{
		try
		{
			var directory = System.IO.Path.GetDirectoryName(Path);
			if (!string.IsNullOrWhiteSpace(directory))
			{
				Directory.CreateDirectory(directory);
			}

			File.AppendAllText(
				Path,
				$"[{DateTimeOffset.Now:O}] {context}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
		}
		catch
		{
		}
	}
}
