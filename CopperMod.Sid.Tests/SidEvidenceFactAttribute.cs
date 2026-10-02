namespace CopperMod.Sid.Tests;

/// <summary>Disabled external coverage is reported as skipped, never passed.</summary>
public sealed class SidEvidenceFactAttribute : FactAttribute
{
    public SidEvidenceFactAttribute(params string[] enableFlags)
    {
        if (!Required && !enableFlags.Any(flag => Environment.GetEnvironmentVariable(flag) == "1"))
            Skip = "External SID evidence disabled; enable " + string.Join(" or ", enableFlags) + ".";
    }

    internal static bool Required => Environment.GetEnvironmentVariable("SID_ACCURACY_REQUIRED") == "1";
}

public sealed class PexTablesFactAttribute : FactAttribute
{
    public PexTablesFactAttribute()
    {
        if (!SidEvidenceFactAttribute.Required && PexD418MeasurementTests.FindMeasurementRoot() == null)
            Skip = "Raw Pex measurement tables are unavailable.";
    }
}

public sealed class SidEvidenceTheoryAttribute : TheoryAttribute
{
    public SidEvidenceTheoryAttribute(params string[] enableFlags)
    {
        if (!SidEvidenceFactAttribute.Required && !enableFlags.Any(flag => Environment.GetEnvironmentVariable(flag) == "1"))
            Skip = "Optional SID coverage disabled; enable " + string.Join(" or ", enableFlags) + ".";
    }
}
