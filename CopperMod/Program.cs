using CopperMod;
using Terminal.Gui.App;

internal class Program
{
	private static readonly string DefaultTunePath = Path.Combine("TestTunes", "Med", "title");

	private static void Main(string[] args)
	{
		PlayerStartupOptions startupOptions;
		try
		{
			startupOptions = PlayerStartupOptions.Parse(args, FindWorkspaceFileOrNull(DefaultTunePath));
		}
		catch (ArgumentException ex)
		{
			Console.Error.WriteLine(ex.Message);
			return;
		}

		var autoPlay = args.Length > 0 && !string.IsNullOrWhiteSpace(startupOptions.InitialPath);

		using var application = Application.Create().Init();
		using var window = new PlayerWindow(application, startupOptions, autoPlay);
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
