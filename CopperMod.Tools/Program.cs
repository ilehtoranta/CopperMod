namespace CopperMod.Tools;

internal static class Program
{
	public static int Main(string[] args)
	{
		return CopperModTools.Run(args, Console.Out, Console.Error);
	}
}
