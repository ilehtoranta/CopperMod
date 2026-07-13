using Copper68k;

namespace Copper68k.Tests;

public sealed class M68kInterpreterHierarchyTests
{
	[Fact]
	public void AdvancedTimingInterpretersShareOnlyTheAdvancedTimingBase()
	{
		Assert.Equal(typeof(M68kAdvancedTimingInterpreter), typeof(M68020Interpreter).BaseType);
		Assert.Equal(typeof(M68kAdvancedTimingInterpreter), typeof(M68030Interpreter).BaseType);
		Assert.Equal(typeof(M68kAdvancedTimingInterpreter), typeof(M68040Interpreter).BaseType);
		Assert.NotEqual(typeof(M68020Interpreter), typeof(M68040Interpreter).BaseType);
	}
}
