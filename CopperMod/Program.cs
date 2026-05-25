using CopperMod;
using Terminal.Gui.App;

internal class Program
{
	private static void Main(string[] args)
	{
		var initialPath = args.Length > 0 ? args[0] : FindWorkspaceFileOrNull("title");
		var autoPlay = args.Length > 0;

		using var application = Application.Create().Init();
		using var window = new PlayerWindow(application, initialPath, autoPlay);
		application.Run(window);
	}

	private static string? FindWorkspaceFileOrNull(string name)
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory != null)
		{
			var candidate = Path.Combine(directory.FullName, name);
			if (File.Exists(candidate))
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		return null;
	}
}
