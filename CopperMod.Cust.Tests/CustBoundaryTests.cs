using System.Reflection;
using CopperMod.Cust;

namespace CopperMod.Cust.Tests;

public sealed class CustBoundaryTests
{
    [Fact]
    public void CustAssemblyDoesNotReferenceCopperScreen()
    {
        var references = typeof(CustFormat).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToHashSet();

        Assert.DoesNotContain("CopperScreen", references);
    }

    [Fact]
    public void CustProjectDoesNotContainEmulatorAppConcepts()
    {
        var root = FindWorkspaceDirectory();
        var custDirectory = Path.Combine(root, "CopperMod.Cust");
        var forbiddenTerms = new[]
        {
            "CopperScreen",
            "CopperBench",
            "Workbench",
            "AmigaDiskImage",
            "AmigaFloppyDrive",
            "AmigaBoot",
            "BootBlock",
            "ADF",
            "Adf",
            "Floppy",
            "Bitplane",
            "Blitter",
            "Sprite",
            "DisplayHost",
            "WriteableBitmap",
            "Avalonia"
        };

        var offenders = Directory.EnumerateFiles(custDirectory, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file =>
                forbiddenTerms
                    .Where(term => File.ReadAllText(file).Contains(term, StringComparison.Ordinal))
                    .Select(term => $"{Path.GetRelativePath(root, file)} contains {term}"))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void CustProjectDoesNotReferenceCopperScreenProject()
    {
        var root = FindWorkspaceDirectory();
        var projectText = File.ReadAllText(Path.Combine(root, "CopperMod.Cust", "CopperMod.Cust.csproj"));

        Assert.DoesNotContain("CopperScreen", projectText, StringComparison.OrdinalIgnoreCase);
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
