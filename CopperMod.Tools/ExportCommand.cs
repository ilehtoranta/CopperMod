namespace CopperMod.Tools;

/// <summary>Headless module export entry point shared by the player and tools executable.</summary>
public static class ExportCommand
{
    /// <summary>Runs a tools command and returns its process exit code.</summary>
    public static int Run(string[] args, TextWriter output, TextWriter error) => CopperModTools.Run(args, output, error);
}
