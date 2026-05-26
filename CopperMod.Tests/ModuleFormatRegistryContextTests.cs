using CopperMod.Abstractions;
using CopperMod.Rendering;

namespace CopperMod.Tests;

public sealed class ModuleFormatRegistryContextTests
{
	[Fact]
	public void LoadFilePassesSourcePathToContextAwareFormats()
	{
		var directory = Path.Combine(Path.GetTempPath(), "CopperModContextTests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		var path = Path.Combine(directory, "song.fake");
		File.WriteAllBytes(path, new byte[] { 0x43, 0x55, 0x53, 0x54 });
		var format = new RecordingContextFormat();

		try
		{
			using var song = ModuleFormatRegistry.LoadFile(path, new IModuleFormat[] { format });

			Assert.True(format.ContextCanLoadCalled);
			Assert.True(format.ContextLoadCalled);
			Assert.False(format.ByteCanLoadCalled);
			Assert.False(format.ByteLoadCalled);
			Assert.Equal(Path.GetFullPath(path), format.LoadContext?.SourcePath);
			Assert.Equal(Path.GetFullPath(directory), format.LoadContext?.SourceDirectory);
			Assert.Equal("fake", song.Metadata.FormatName);
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	private sealed class RecordingContextFormat : IModuleFormatWithContext
	{
		public string Name => "fake";

		public bool ContextCanLoadCalled { get; private set; }

		public bool ContextLoadCalled { get; private set; }

		public bool ByteCanLoadCalled { get; private set; }

		public bool ByteLoadCalled { get; private set; }

		public ModuleLoadContext? LoadContext { get; private set; }

		public bool CanLoad(ReadOnlySpan<byte> data)
		{
			_ = data;
			ByteCanLoadCalled = true;
			return false;
		}

		public bool CanLoad(ModuleLoadContext context)
		{
			ContextCanLoadCalled = true;
			return context.DataSpan.Length == 4;
		}

		public IModuleSong Load(ReadOnlySpan<byte> data)
		{
			_ = data;
			ByteLoadCalled = true;
			throw new InvalidOperationException("Byte load should not be used for context-aware formats.");
		}

		public IModuleSong Load(ModuleLoadContext context)
		{
			ContextLoadCalled = true;
			LoadContext = context;
			return new DummySong();
		}
	}

	private sealed class DummySong : IModuleSong
	{
		public ModuleMetadata Metadata { get; } = new ModuleMetadata(formatName: "fake");

		public ModulePlaybackCapabilities Capabilities => ModulePlaybackCapabilities.Minimal;

		public IReadOnlyList<ModuleDiagnostic> Diagnostics => Array.Empty<ModuleDiagnostic>();

		public SongDuration Duration => SongDuration.Unknown;

		public PlaybackPosition Position => PlaybackPosition.FromTime(TimeSpan.Zero);

		public bool LoopingEnabled { get; set; }

		public int GetCurrentTickFrameCount(AudioRenderOptions? options = null)
		{
			_ = options;
			return 1;
		}

		public void Reset()
		{
		}

		public void Seek(TimeSpan position)
		{
			_ = position;
		}

		public void Seek(TrackerPosition position)
		{
			_ = position;
		}

		public RenderResult Render(Span<float> destination, AudioRenderOptions? options = null)
		{
			_ = options;
			destination.Clear();
			return new RenderResult(0, 0, Position);
		}

		public RenderResult RenderTick(Span<float> destination, AudioRenderOptions? options = null)
		{
			_ = options;
			destination.Clear();
			return new RenderResult(0, 0, Position);
		}

		public void Dispose()
		{
		}
	}
}
