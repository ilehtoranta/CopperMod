using CopperMod.Abstractions;
using CopperMod.Cust;

namespace CopperMod.Cust.Tests;

public sealed class HunkParserTests
{
	[Fact]
	public void AlteredBeastFixtureParsesAsSingleChipCodeHunkWithRelocations()
	{
		var hunk = HunkParser.Parse(File.ReadAllBytes(FindWorkspaceFile("TestTunes", "Amiga.CUST", "AlteredBeast.CUST")));

		var segment = Assert.Single(hunk.Segments);
		Assert.Equal(HunkSegmentKind.Code, segment.Kind);
		Assert.Equal(HunkMemoryKind.Chip, segment.MemoryKind);
		Assert.True(segment.Data.Length > 90_000);
		Assert.NotEmpty(segment.Relocations);
		Assert.Contains(segment.Relocations.SelectMany(block => block.Offsets), offset => offset == 0x7A);
	}

	[Fact]
	public void RandomDataIsNotIdentifiedAsHunk()
	{
		Assert.False(HunkParser.Identify(new byte[] { 1, 2, 3, 4, 5, 6 }));
		Assert.False(new CustFormat().CanLoad(new byte[] { 1, 2, 3, 4, 5, 6 }));
	}

	[Fact]
	public void UnsupportedHunkSubsectionIsRejected()
	{
		var data = CreateMinimalHunkWithUnsupportedSubsection();

		Assert.Throws<UnsupportedModuleFormatException>(() => HunkParser.Parse(data));
	}

	private static byte[] CreateMinimalHunkWithUnsupportedSubsection()
	{
		var words = new uint[]
		{
			HunkParser.HunkHeader,
			0,
			1,
			0,
			0,
			1,
			HunkParser.HunkCode,
			1,
			0x4E75_0000,
			0x3ED,
			HunkParser.HunkEnd
		};
		var data = new byte[words.Length * 4];
		for (var i = 0; i < words.Length; i++)
		{
			data[(i * 4) + 0] = (byte)(words[i] >> 24);
			data[(i * 4) + 1] = (byte)(words[i] >> 16);
			data[(i * 4) + 2] = (byte)(words[i] >> 8);
			data[(i * 4) + 3] = (byte)words[i];
		}

		return data;
	}

	internal static string FindWorkspaceFile(params string[] parts)
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory != null)
		{
			var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
			if (File.Exists(candidate))
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		return string.Join(Path.DirectorySeparatorChar, parts);
	}
}
