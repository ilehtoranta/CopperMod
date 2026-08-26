using Copper68k;

namespace Copper68k.Tests;

public sealed class M68kCodeSnapshotTests
{
	[Fact]
	public void DefaultAndEmptySnapshotsReportEmpty()
	{
		Assert.True(default(M68kCodeGenerationStamp).IsEmpty);
		Assert.True(new M68kCodeGenerationStamp([], []).IsEmpty);
		Assert.False(new M68kCodeGenerationStamp([0x1000], [1]).IsEmpty);
		Assert.True(default(M68kJitCodeSnapshot).IsEmpty);
		Assert.True(new M68kJitCodeSnapshot(0x1000, [], default, []).IsEmpty);
		Assert.False(new M68kJitCodeSnapshot(0x1000, [0x12], default, []).IsEmpty);
	}

	[Fact]
	public void ReaderReadsOnlyWordsFullyInsideSnapshot()
	{
		var reader = CreateReader(0x1000, [0x12, 0x34, 0x56]);

		Assert.Equal(0x1234, reader.ReadHostWord(0x1000));
		Assert.True(reader.ContainsRange(0x1000, 3));
		Assert.True(reader.ContainsRange(0x1003, 0));
		Assert.False(reader.ContainsRange(0x1002, 2));
		Assert.False(reader.ContainsRange(0x1000, -1));
		Assert.Throws<M68kCodeReadException>(() => reader.ReadHostWord(0x1002));
	}

	[Fact]
	public void OddByteCapturePadsFinalWordWithZero()
	{
		var reader = CreateReader(0x1000, [0x12, 0x34, 0x56]);

		Assert.True(reader.TryCaptureWords(0x1000, 3, out var words));
		Assert.Equal(new ushort[] { 0x1234, 0x5600 }, words);
		Assert.False(reader.TryCaptureWords(0x1000, 0, out var empty));
		Assert.Empty(empty);
		Assert.False(reader.TryCaptureWords(0x1000, -1, out empty));
		Assert.Empty(empty);
	}

	[Fact]
	public void CapturedHostTrapIsSubstitutedInReadsAndWordCaptures()
	{
		var snapshot = new M68kJitCodeSnapshot(
			0x1000,
			[0x70, 0x01, 0x52, 0x80],
			default,
			[0x1002]);
		var reader = new M68kSnapshotCodeReader(snapshot);

		Assert.Equal(0x7001, reader.ReadHostWord(0x1000));
		Assert.Equal(0x4AFC, reader.ReadHostWord(0x1002));
		Assert.True(reader.TryCaptureWords(0x1000, 4, out var words));
		Assert.Equal(new ushort[] { 0x7001, 0x4AFC }, words);
	}

	[Fact]
	public void GenerationLookupUsesNormalizedPagesAndShortestStampArray()
	{
		var stamp = new M68kCodeGenerationStamp(
			[0x1000, 0x1100, 0x1200],
			[7, 8]);
		var snapshot = new M68kJitCodeSnapshot(0x1000, [0x4E, 0x71], stamp, []);
		var reader = new M68kSnapshotCodeReader(snapshot);

		Assert.True(reader.TryGetGeneration(0x10FF, out var first));
		Assert.Equal(7u, first);
		Assert.True(reader.TryGetGeneration(0x1101, out var second));
		Assert.Equal(8u, second);
		Assert.False(reader.TryGetGeneration(0x1200, out var missingGeneration));
		Assert.Equal(0u, missingGeneration);
		Assert.False(reader.TryGetGeneration(0x1300, out missingGeneration));
		Assert.Equal(0u, missingGeneration);
	}

	[Fact]
	public void ReaderWrapsLowSnapshotsAtTwentyFourBitsButKeepsHighSnapshotsFullWidth()
	{
		var wrapped = CreateReader(0x00FF_FFFE, [0x11, 0x22, 0x33, 0x44]);
		var fullWidth = CreateReader(0x1000_0000, [0x55, 0x66, 0x77, 0x88]);

		Assert.Equal(0x3344, wrapped.ReadHostWord(0x0100_0000));
		Assert.True(wrapped.ContainsRange(0x0100_0000, 2));
		Assert.Equal(0x5566, fullWidth.ReadHostWord(0x1000_0000));
		Assert.False(fullWidth.ContainsRange(0, 2));
		Assert.Throws<M68kCodeReadException>(() => fullWidth.ReadHostWord(0));
	}

	[Fact]
	public void ReaderWithMissingArraysUsesSafeFallbacks()
	{
		var snapshot = new M68kJitCodeSnapshot(0x1000, null!, default, null!);
		var reader = new M68kSnapshotCodeReader(snapshot);

		Assert.Equal(0, reader.ByteLength);
		Assert.False(reader.ContainsRange(0x1000, 0));
		Assert.False(reader.TryGetGeneration(0x1000, out var generation));
		Assert.Equal(0u, generation);
	}

	private static M68kSnapshotCodeReader CreateReader(uint root, byte[] bytes)
		=> new(new M68kJitCodeSnapshot(root, bytes, default, []));
}
