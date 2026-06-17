namespace CopperMod.Sid.Tests;

public sealed class C64KeyboardMatrixTests
{
	[Theory]
	[InlineData(C64Key.Delete, 0, 0)]
	[InlineData(C64Key.F3, 0, 5)]
	[InlineData(C64Key.A, 1, 2)]
	[InlineData(C64Key.Space, 7, 4)]
	[InlineData(C64Key.RunStop, 7, 7)]
	public void MatrixPositionsMatchC64ColumnRowLayout(C64Key key, int column, int row)
	{
		var position = C64KeyboardMatrix.GetPosition(key);

		Assert.Equal(column, position.Column);
		Assert.Equal(row, position.Row);
		Assert.Equal(1 << column, position.ColumnMask);
		Assert.Equal(1 << row, position.RowMask);
	}

	[Fact]
	public void EveryPublicKeyHasAMatrixPosition()
	{
		foreach (var key in Enum.GetValues<C64Key>())
		{
			var position = C64KeyboardMatrix.GetPosition(key);

			Assert.InRange(position.Column, 0, 7);
			Assert.InRange(position.Row, 0, 7);
		}
	}
}
