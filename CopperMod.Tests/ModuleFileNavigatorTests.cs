namespace CopperMod.Tests;

public sealed class ModuleFileNavigatorTests
{
	[Fact]
	public void NextFileUsesAlphabeticOrderInsideCurrentFolder()
	{
		using var workspace = TemporaryWorkspace.Create();
		var folder = workspace.CreateDirectory("Album");
		var first = workspace.CreateFile(folder, "01.sid");
		var second = workspace.CreateFile(folder, "02.sid");
		_ = first;

		var actual = ModuleFileNavigator.ResolveNextFilePath(Path.Combine(folder, "01.sid"));

		Assert.Equal(second, actual);
	}

	[Fact]
	public void PreviousFileUsesAlphabeticOrderInsideCurrentFolder()
	{
		using var workspace = TemporaryWorkspace.Create();
		var folder = workspace.CreateDirectory("Album");
		var first = workspace.CreateFile(folder, "01.sid");
		var second = workspace.CreateFile(folder, "02.sid");
		_ = second;

		var actual = ModuleFileNavigator.ResolvePreviousFilePath(Path.Combine(folder, "02.sid"));

		Assert.Equal(first, actual);
	}

	[Fact]
	public void NextFileAtEndEntersNextSiblingFolder()
	{
		using var workspace = TemporaryWorkspace.Create();
		var albumA = workspace.CreateDirectory("Album A");
		var albumB = workspace.CreateDirectory("Album B");
		var current = workspace.CreateFile(albumA, "02.sid");
		var expected = workspace.CreateFile(albumB, "01.sid");

		var actual = ModuleFileNavigator.ResolveNextFilePath(current);

		Assert.Equal(expected, actual);
	}

	[Fact]
	public void PreviousFileAtBeginningEntersPreviousSiblingFolderAtLastFile()
	{
		using var workspace = TemporaryWorkspace.Create();
		var albumA = workspace.CreateDirectory("Album A");
		var albumB = workspace.CreateDirectory("Album B");
		var expected = workspace.CreateFile(albumA, "02.sid");
		_ = workspace.CreateFile(albumA, "01.sid");
		var current = workspace.CreateFile(albumB, "01.sid");

		var actual = ModuleFileNavigator.ResolvePreviousFilePath(current);

		Assert.Equal(expected, actual);
	}

	[Fact]
	public void NextFileSkipsEmptySiblingFoldersAndDescendsIntoNestedFolders()
	{
		using var workspace = TemporaryWorkspace.Create();
		var albumA = workspace.CreateDirectory("Album A");
		var empty = workspace.CreateDirectory("Album B");
		var albumC = workspace.CreateDirectory("Album C");
		var nested = workspace.CreateDirectory(albumC, "Disc 1");
		var current = workspace.CreateFile(albumA, "99.sid");
		var expected = workspace.CreateFile(nested, "01.sid");
		_ = empty;

		var actual = ModuleFileNavigator.ResolveNextFilePath(current);

		Assert.Equal(expected, actual);
	}

	[Fact]
	public void NextFileTreatsFoldersAndFilesAsOneAlphabeticSequence()
	{
		using var workspace = TemporaryWorkspace.Create();
		var album = workspace.CreateDirectory("Album");
		var nested = workspace.CreateDirectory(album, "02 Disc");
		var current = workspace.CreateFile(album, "01.sid");
		var expected = workspace.CreateFile(nested, "01.sid");
		_ = workspace.CreateFile(album, "03.sid");

		var actual = ModuleFileNavigator.ResolveNextFilePath(current);

		Assert.Equal(expected, actual);
	}

	[Fact]
	public void NextFileEnteringSiblingFolderUsesCombinedAlphabeticContents()
	{
		using var workspace = TemporaryWorkspace.Create();
		var albumA = workspace.CreateDirectory("Album A");
		var albumB = workspace.CreateDirectory("Album B");
		var nested = workspace.CreateDirectory(albumB, "A Disc");
		var current = workspace.CreateFile(albumA, "99.sid");
		var expected = workspace.CreateFile(nested, "01.sid");
		_ = workspace.CreateFile(albumB, "B.sid");

		var actual = ModuleFileNavigator.ResolveNextFilePath(current);

		Assert.Equal(expected, actual);
	}

	[Fact]
	public void PreviousFileSkipsEmptySiblingFoldersAndDescendsIntoNestedFolders()
	{
		using var workspace = TemporaryWorkspace.Create();
		var albumA = workspace.CreateDirectory("Album A");
		var nested = workspace.CreateDirectory(albumA, "Disc 2");
		var empty = workspace.CreateDirectory("Album B");
		var albumC = workspace.CreateDirectory("Album C");
		var expected = workspace.CreateFile(nested, "99.sid");
		var current = workspace.CreateFile(albumC, "01.sid");
		_ = empty;

		var actual = ModuleFileNavigator.ResolvePreviousFilePath(current);

		Assert.Equal(expected, actual);
	}

	[Fact]
	public void PreviousFileTreatsFoldersAndFilesAsOneAlphabeticSequence()
	{
		using var workspace = TemporaryWorkspace.Create();
		var album = workspace.CreateDirectory("Album");
		var nested = workspace.CreateDirectory(album, "02 Disc");
		_ = workspace.CreateFile(album, "01.sid");
		var expected = workspace.CreateFile(nested, "99.sid");
		var current = workspace.CreateFile(album, "03.sid");

		var actual = ModuleFileNavigator.ResolvePreviousFilePath(current);

		Assert.Equal(expected, actual);
	}

	[Fact]
	public void PreviousFileEnteringSiblingFolderUsesCombinedAlphabeticContents()
	{
		using var workspace = TemporaryWorkspace.Create();
		var albumA = workspace.CreateDirectory("Album A");
		var nested = workspace.CreateDirectory(albumA, "B Disc");
		var albumB = workspace.CreateDirectory("Album B");
		_ = workspace.CreateFile(albumA, "A.sid");
		var expected = workspace.CreateFile(nested, "99.sid");
		var current = workspace.CreateFile(albumB, "01.sid");

		var actual = ModuleFileNavigator.ResolvePreviousFilePath(current);

		Assert.Equal(expected, actual);
	}

	[Fact]
	public void MissingCurrentPathReturnsNull()
	{
		using var workspace = TemporaryWorkspace.Create();
		var missing = Path.Combine(workspace.Root, "missing.sid");

		Assert.Null(ModuleFileNavigator.ResolveNextFilePath(missing));
		Assert.Null(ModuleFileNavigator.ResolvePreviousFilePath(missing));
	}

	private sealed class TemporaryWorkspace : IDisposable
	{
		private TemporaryWorkspace(string root)
		{
			Root = root;
		}

		public string Root { get; }

		public static TemporaryWorkspace Create()
		{
			var root = Path.Combine(Path.GetTempPath(), "CopperModNavigatorTests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			return new TemporaryWorkspace(root);
		}

		public string CreateDirectory(string name)
		{
			return CreateDirectory(Root, name);
		}

		public string CreateDirectory(string parent, string name)
		{
			var path = Path.Combine(parent, name);
			Directory.CreateDirectory(path);
			return Path.GetFullPath(path);
		}

		public string CreateFile(string directory, string name)
		{
			var path = Path.Combine(directory, name);
			File.WriteAllBytes(path, Array.Empty<byte>());
			return Path.GetFullPath(path);
		}

		public void Dispose()
		{
			if (Directory.Exists(Root))
			{
				Directory.Delete(Root, recursive: true);
			}
		}
	}
}
