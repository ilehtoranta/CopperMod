namespace CopperMod.Tools;

internal sealed class SidD418MatrixGeneratorOptions
{
	private SidD418MatrixGeneratorOptions(string inputRoot, string outputPath)
	{
		InputRoot = inputRoot;
		OutputPath = outputPath;
	}

	public string InputRoot { get; }

	public string OutputPath { get; }

	public static bool IsCommand(string[] args)
	{
		return args.Length > 0 &&
			string.Equals(args[0], SidD418MatrixGenerator.CommandName, StringComparison.OrdinalIgnoreCase);
	}

	public static SidD418MatrixGeneratorOptions Parse(string[] args)
	{
		if (args.Length == 0 || !IsCommand(args))
		{
			throw new CommandLineException("Unknown command. Expected: " + SidD418MatrixGenerator.CommandName);
		}

		string? inputRoot = null;
		string? outputPath = null;

		for (var i = 1; i < args.Length; i++)
		{
			var arg = args[i];
			switch (arg)
			{
				case "--input":
					inputRoot = RequireValue(args, ref i, arg);
					break;
				case "--out":
					outputPath = RequireValue(args, ref i, arg);
					break;
				case "--help":
					throw new CommandLineException(Usage.Text);
				default:
					throw new CommandLineException("Unknown option: " + arg);
			}
		}

		if (string.IsNullOrWhiteSpace(inputRoot))
		{
			throw new CommandLineException("Missing required option: --input");
		}

		if (string.IsNullOrWhiteSpace(outputPath))
		{
			throw new CommandLineException("Missing required option: --out");
		}

		return new SidD418MatrixGeneratorOptions(inputRoot, outputPath);
	}

	private static string RequireValue(string[] args, ref int index, string option)
	{
		if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
		{
			throw new CommandLineException("Missing value for " + option + ".");
		}

		index++;
		return args[index];
	}
}
