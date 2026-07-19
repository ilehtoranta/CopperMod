using System.Reflection;

namespace CopperMod.Amiga.Tests;

public sealed class CyberGraphicsArchitectureTests
{
	[Theory]
	[InlineData(typeof(AmigaBus))]
	[InlineData(typeof(CopperMod.Cust.CustFormat))]
	public void CoreAndCustDoNotReferenceCyberGraphics(Type assemblyMarker)
	{
		var assembly = assemblyMarker.Assembly;
		Assert.DoesNotContain(
			assembly.GetReferencedAssemblies(),
			reference => reference.Name == "CopperMod.Amiga.CyberGraphics");
		Assert.DoesNotContain(
			assembly.GetTypes(),
			type => type.Namespace?.Contains("CyberGraphics", StringComparison.Ordinal) == true);
	}
}
