using CopperScreen;

namespace CopperScreen.Tests;

public sealed class CopperScreenArchitectureTests
{
    [Fact]
    public void CopperScreenReferencesOnlyTheSharedAmigaCoreFromCopperMod()
    {
        var references = typeof(CopperScreenEmulator).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToHashSet();

        Assert.Contains("CopperMod.Amiga", references);
        Assert.DoesNotContain("CopperMod.Cust", references);
        Assert.DoesNotContain("CopperMod", references);
        Assert.DoesNotContain("CopperMod.Abstractions", references);
    }

    [Fact]
    public void CopperScreenProjectDoesNotReferencePlayerProjects()
    {
        var root = FindWorkspaceDirectory();
        var projectText = File.ReadAllText(Path.Combine(root, "CopperScreen", "CopperScreen.csproj"));

        Assert.Contains("CopperMod.Amiga", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("CopperMod.Cust", projectText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CopperMod.Abstractions", projectText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("..\\CopperMod\\", projectText, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindWorkspaceDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CopperMod.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the CopperMod workspace.");
    }
}
