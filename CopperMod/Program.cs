using CopperMod;
using Terminal.Gui.App;

internal class Program
{
	private static readonly string DefaultTunePath = Path.Combine("TestTunes", "Med", "title");

	private static int Main(string[] args)
	{
		if (args.Length == 1 && args[0] == "--version")
		{
			Console.WriteLine("CopperMod " + typeof(Program).Assembly.GetName().Version!.ToString(3));
			return 0;
		}
		if (args.Length > 0 && args[0] == "--render")
		{
			return CopperMod.Tools.ExportCommand.Run(new[] { "render" }.Concat(args.Skip(1)).ToArray(), Console.Out, Console.Error);
		}
		PlayerStartupOptions startupOptions;
		try
		{
			startupOptions = PlayerStartupOptions.Parse(args, FindWorkspaceFileOrNull(DefaultTunePath));
		}
		catch (ArgumentException ex)
		{
			Console.Error.WriteLine(ex.Message);
			return 1;
		}

		var autoPlay = args.Length > 0 && !string.IsNullOrWhiteSpace(startupOptions.InitialPath);

		using var application = Application.Create().Init();
		using var window = new PlayerWindow(application, startupOptions, autoPlay);
		application.Run(window);
		return 0;
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
