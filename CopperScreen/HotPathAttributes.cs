using System;

namespace CopperScreen;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Constructor)]
internal sealed class HotPathAttribute : Attribute
{
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
