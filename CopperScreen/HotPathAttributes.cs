using System;

namespace CopperScreen;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property)]
internal sealed class HotPathAttribute : Attribute
{
    /// <summary>
    /// Maximum number of branch points (if, switch cases, ternary, null coalescing, etc.) allowed in the method body.
    /// Zero means no limit (default). The analyzer reports CMPERF006 if exceeded.
    /// </summary>
    public int MaxBranches { get; set; }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Constructor)]
internal sealed class HotPathAllocationAllowedAttribute : Attribute
{
	public HotPathAllocationAllowedAttribute(string reason)
	{
		Reason = reason;
	}

	public string Reason { get; }
}
