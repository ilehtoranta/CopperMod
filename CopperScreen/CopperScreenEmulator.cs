using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using CopperMod.Amiga;

namespace CopperScreen;

internal sealed class CopperScreenEmulator
{
	private const int AppFrameRate = 50;
	private const int DefaultAudioSampleRate = 44_100;
	private const int DefaultAudioChannels = 2;
	private static readonly long PalFrameCycles = Math.Max(1, (long)Math.Round(AmigaConstants.A500PalCpuClockHz / AmigaConstants.A500PalVBlankHz));
	private readonly AmigaMachine _machine;
	private readonly AmigaBootController _boot;
	private readonly float[] _frameAudio;
	private readonly int[] _previousInterlaceFrame;
	private bool _bootAttempted;
	private bool _previousInterlaceFrameValid;
	private int _firePulseFrames;
	private long _targetCycle;
	private long _audioCycle;
	private long _frameAudioStartCycle;
	private long _frameAudioEndCycle;
	private int _frameAudioSampleIndex;

	private CopperScreenEmulator(string? diskPath)
	{
		Width = AmigaConstants.PalLowResWidth;
		Height = AmigaConstants.PalLowResHeight;
		Framebuffer = new int[Width * Height];
		_machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
		_boot = new AmigaBootController(_machine);
		_frameAudio = new float[AudioFramesPerAppFrame(DefaultAudioSampleRate) * DefaultAudioChannels];
		_previousInterlaceFrame = new int[Framebuffer.Length];
		DiskPath = diskPath;
		StatusText = diskPath == null ? "insert disk image" : Path.GetFileName(diskPath);
	}

	public int Width { get; }

	public int Height { get; }

	public int[] Framebuffer { get; }

	public string? DiskPath { get; private set; }

	public string StatusText { get; private set; }

	public bool IsPrimaryFirePressed => _machine.Bus.GamePort0FirePressed;

	internal OcsDisplaySnapshot DisplaySnapshot => _machine.Bus.Display.CaptureSnapshot();

	public int AudioFramesPerAppFrame(int sampleRate)
	{
		return Math.Max(1, sampleRate / AppFrameRate);
	}

	public static CopperScreenEmulator Create(string[] args, string baseDirectory)
	{
		return new CopperScreenEmulator(ResolveDiskPath(args, baseDirectory));
	}

	public static CopperScreenEmulator CreateWithoutDisk()
	{
		return new CopperScreenEmulator(null);
	}

	public static string? ResolveDiskPath(string[] args, string baseDirectory)
	{
		_ = baseDirectory;
		if (args.Length > 0 && File.Exists(args[0]))
		{
			return Path.GetFullPath(args[0]);
		}

		return null;
	}

	public static string? ResolveNextDiskPath(string? currentDiskPath)
	{
		if (string.IsNullOrWhiteSpace(currentDiskPath))
		{
			return null;
		}

		var directory = Path.GetDirectoryName(currentDiskPath);
		var fileName = Path.GetFileName(currentDiskPath);
		if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
		{
			return null;
		}

		var match = Regex.Match(fileName, @"(?<prefix>Disk\s*)(?<number>\d+)(?<suffix>\s*of\s*(?<total>\d+))", RegexOptions.IgnoreCase);
		if (!match.Success ||
			!int.TryParse(match.Groups["number"].Value, out var number) ||
			!int.TryParse(match.Groups["total"].Value, out var total) ||
			number >= total)
		{
			return null;
		}

		var nextName = fileName.Remove(match.Groups["number"].Index, match.Groups["number"].Length)
			.Insert(match.Groups["number"].Index, (number + 1).ToString());
		var candidate = Path.Combine(directory, nextName);
		return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
	}

	public bool InsertNextDisk()
	{
		var nextDiskPath = ResolveNextDiskPath(DiskPath);
		if (nextDiskPath == null)
		{
			StatusText = "no next disk image";
			return false;
		}

		return InsertDisk(nextDiskPath, markChanged: true);
	}

	public bool InsertDisk(string diskPath, bool markChanged = true)
	{
		if (!File.Exists(diskPath))
		{
			StatusText = "disk image not found";
			return false;
		}

		DiskPath = Path.GetFullPath(diskPath);
		var disk = AmigaDiskImage.Load(DiskPath);
		if (_bootAttempted)
		{
			_boot.Drive0.Insert(disk, markChanged);
		}

		StatusText = "inserted " + Path.GetFileName(DiskPath);
		return true;
	}

	public void PulsePrimaryFire(int frames = 30)
	{
		_firePulseFrames = Math.Max(_firePulseFrames, Math.Max(1, frames));
	}

	public void RenderNextFrame()
	{
		_machine.Bus.GamePort0FirePressed = _firePulseFrames > 0;
		if (DiskPath == null)
		{
			_frameAudio.AsSpan().Clear();
			_previousInterlaceFrameValid = false;
			InsertDiskScreenRenderer.Render(Framebuffer, Width, Height);
			StatusText = "insert disk image";
			AdvanceInputPulse();
			return;
		}

		if (!_bootAttempted)
		{
			_bootAttempted = true;
			var disk = AmigaDiskImage.Load(DiskPath);
			_boot.StartBootFromDisk(disk);
			_targetCycle = 0;
			_audioCycle = _machine.Cpu.State.Cycles;
		}

		_targetCycle += PalFrameCycles;
		BeginFrameAudio(_targetCycle);
		var result = _boot.ContinueExecutionUntilCycle(_targetCycle, maxInstructions: 100_000, RenderFrameAudioUntil);
		FinishFrameAudio();
		if (HandleBootResult(result))
		{
			AdvanceInputPulse();
			return;
		}

		_machine.Bus.Display.RenderFrame(
			MemoryMarshal.Cast<int, uint>(Framebuffer.AsSpan()),
			_targetCycle - PalFrameCycles,
			_targetCycle);
		StabilizeInterlaceFrame();
		if (IsBlank(Framebuffer))
		{
			InsertDiskScreenRenderer.RenderStatus(Framebuffer, Width, Height, StatusText);
		}

		AdvanceInputPulse();
	}

	public int RenderAudio(Span<float> destination, int sampleRate, int channels)
	{
		if (sampleRate <= 0 || channels <= 0)
		{
			destination.Clear();
			return 0;
		}

		var frames = Math.Min(AudioFramesPerAppFrame(sampleRate), destination.Length / channels);
		var span = destination.Slice(0, frames * channels);
		span.Clear();
		if (!_bootAttempted || DiskPath == null)
		{
			return frames;
		}

		if (sampleRate == DefaultAudioSampleRate && channels == DefaultAudioChannels)
		{
			_frameAudio.AsSpan(0, Math.Min(span.Length, _frameAudio.Length)).CopyTo(span);
			return frames;
		}

		var sourceFrames = _frameAudio.Length / DefaultAudioChannels;
		for (var frame = 0; frame < frames; frame++)
		{
			var sourceFrame = Math.Min(sourceFrames - 1, (int)Math.Round(frame * (sourceFrames - 1) / Math.Max(1.0, frames - 1.0)));
			var left = _frameAudio[sourceFrame * DefaultAudioChannels];
			var right = _frameAudio[(sourceFrame * DefaultAudioChannels) + 1];
			var offset = frame * channels;
			if (channels == 1)
			{
				span[offset] = (left + right) * 0.5f;
			}
			else
			{
				span[offset] = left;
				span[offset + 1] = right;
				for (var extra = 2; extra < channels; extra++)
				{
					span[offset + extra] = (left + right) * 0.5f;
				}
			}
		}

		return frames;
	}

	private void BeginFrameAudio(long targetCycle)
	{
		_frameAudio.AsSpan().Clear();
		_frameAudioStartCycle = _audioCycle;
		_frameAudioEndCycle = Math.Max(_frameAudioStartCycle, targetCycle);
		_frameAudioSampleIndex = 0;
	}

	private void RenderFrameAudioUntil(long previousCycle, long currentCycle)
	{
		_ = previousCycle;
		if (_frameAudioEndCycle <= _frameAudioStartCycle)
		{
			return;
		}

		var frames = _frameAudio.Length / DefaultAudioChannels;
		while (_frameAudioSampleIndex < frames)
		{
			var sampleCycle = _frameAudioStartCycle + (long)Math.Round(
				(_frameAudioEndCycle - _frameAudioStartCycle) * ((_frameAudioSampleIndex + 1) / (double)frames));
			if (sampleCycle > currentCycle)
			{
				break;
			}

			_machine.Bus.Paula.RenderSample(sampleCycle, _frameAudio, _frameAudioSampleIndex, DefaultAudioChannels);
			_frameAudioSampleIndex++;
		}
	}

	private void FinishFrameAudio()
	{
		RenderFrameAudioUntil(_machine.Cpu.State.Cycles, _frameAudioEndCycle);
		_audioCycle = _frameAudioEndCycle;
	}

	private void StabilizeInterlaceFrame()
	{
		if (!_machine.Bus.Display.InterlaceEnabled)
		{
			_previousInterlaceFrameValid = false;
			return;
		}

		if (!_previousInterlaceFrameValid)
		{
			Framebuffer.AsSpan().CopyTo(_previousInterlaceFrame);
			_previousInterlaceFrameValid = true;
			return;
		}

		for (var i = 0; i < Framebuffer.Length; i++)
		{
			var current = Framebuffer[i];
			Framebuffer[i] = AverageOpaquePixels(current, _previousInterlaceFrame[i]);
			_previousInterlaceFrame[i] = current;
		}
	}

	private static int AverageOpaquePixels(int left, int right)
	{
		var a = unchecked((uint)left);
		var b = unchecked((uint)right);
		var r = (((a >> 16) & 0xFF) + ((b >> 16) & 0xFF)) >> 1;
		var g = (((a >> 8) & 0xFF) + ((b >> 8) & 0xFF)) >> 1;
		var blue = ((a & 0xFF) + (b & 0xFF)) >> 1;
		return unchecked((int)(0xFF00_0000u | (r << 16) | (g << 8) | blue));
	}

	private void AdvanceInputPulse()
	{
		if (_firePulseFrames > 0)
		{
			_firePulseFrames--;
		}

		_machine.Bus.GamePort0FirePressed = _firePulseFrames > 0;
	}

	private bool HandleBootResult(AmigaBootResult result)
	{
		var fatalDiagnostics = result.Diagnostics
			.Where(diagnostic => diagnostic.Code is "AMIGA_BOOT_UNSUPPORTED_OPCODE" or "AMIGA_BOOT_FAULT" or "AMIGA_BOOT_PROTECTED_DISK_UNSUPPORTED")
			.ToArray();
		StatusText = fatalDiagnostics.Length == 0
			? $"boot program running: PC=${result.FinalProgramCounter:X6}"
			: string.Join(", ", fatalDiagnostics.Select(diagnostic => diagnostic.Code));
		if (fatalDiagnostics.Length == 0)
		{
			return false;
		}

		InsertDiskScreenRenderer.RenderStatus(Framebuffer, Width, Height, StatusText);
		return true;
	}

	private static bool IsBlank(ReadOnlySpan<int> pixels)
	{
		if (pixels.IsEmpty)
		{
			return true;
		}

		var first = pixels[0];
		for (var i = 1; i < pixels.Length; i++)
		{
			if (pixels[i] != first)
			{
				return false;
			}
		}

		return true;
	}
}
