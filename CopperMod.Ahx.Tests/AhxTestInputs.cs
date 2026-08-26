namespace CopperMod.Ahx.Tests;

internal static class AhxTestInputs
{
    public const string RootEnvironmentVariable = "COPPERMOD_AHX_TEST_ROOT";

    public static string Root
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable(RootEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Path.GetFullPath(configured);
            }

            var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
            return Path.Combine(repository, "tmp");
        }
    }

    public static void ReportMissing(string reason)
        => Console.WriteLine($"Local AHX integration coverage not run: {reason} Set {RootEnvironmentVariable} to the fixture root.");
}
