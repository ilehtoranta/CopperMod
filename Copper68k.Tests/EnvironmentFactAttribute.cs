namespace Copper68k.Tests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class EnvironmentFactAttribute : FactAttribute
{
	public EnvironmentFactAttribute(string environmentVariable, string description)
	{
		var value = Environment.GetEnvironmentVariable(environmentVariable);
		if (!IsTruthy(value))
		{
			Skip = $"Set {environmentVariable}=1 to {description}.";
		}
	}

	private static bool IsTruthy(string? value)
		=> value is not null &&
			(value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
			 value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
			 value.Equals("yes", StringComparison.OrdinalIgnoreCase));
}
