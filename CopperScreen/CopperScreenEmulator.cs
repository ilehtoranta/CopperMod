using System.Runtime.InteropServices;
using System.Text;
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
	private readonly Action<long, long> _renderFrameAudioUntil;
	private bool _bootAttempted;
	private bool _previousInterlaceFrameValid;
	private int _firePulseFrames;
	private long _targetCycle;
	private long _audioCycle;
	private long _frameAudioStartCycle;
	private long _frameAudioEndCycle;
	private int _frameAudioSampleIndex;
	private bool _mousePrimaryFirePressed;
	private bool _mouseSecondFirePressed;
	private bool _joystickUp;
	private bool _joystickDown;
	private bool _joystickLeft;
	private bool _joystickRight;
	private bool _joystickPrimaryFirePressed;
	private bool _joystickSecondFirePressed;

	private CopperScreenEmulator(string? diskPath)
	{
		Width = AmigaConstants.PalLowResWidth;
		Height = AmigaConstants.PalLowResHeight;
		Framebuffer = new int[Width * Height];
		_machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
		_boot = new AmigaBootController(_machine);
		_frameAudio = new float[AudioFramesPerAppFrame(DefaultAudioSampleRate) * DefaultAudioChannels];
		_previousInterlaceFrame = new int[Framebuffer.Length];
		_renderFrameAudioUntil = RenderFrameAudioUntil;
		DiskPath = diskPath;
		StatusText = diskPath == null ? "insert disk image" : Path.GetFileName(diskPath);
	}

	public int Width { get; }

	public int Height { get; }

	public int[] Framebuffer { get; }

	public string? DiskPath { get; private set; }

	public string StatusText { get; private set; }

	public bool IsPaused { get; private set; }

	public bool IsPrimaryFirePressed => _mousePrimaryFirePressed || _joystickPrimaryFirePressed || _firePulseFrames > 0;

	internal OcsDisplaySnapshot DisplaySnapshot => _machine.Bus.Display.CaptureSnapshot();

	public string DiskName => DiskPath == null ? "No disk" : Path.GetFileName(DiskPath);

	public string ProgramCounterText => $"PC=${_machine.Cpu.State.ProgramCounter:X6}";

	public string DriveStatusText
	{
		get
		{
			var disk = _machine.Bus.Disk.CaptureSnapshot();
			var inserted = DiskPath == null ? "empty" : "DF0";
			var motor = disk.MotorOn ? "motor" : "stopped";
			var selected = disk.Selected ? "selected" : "idle";
			return $"{inserted} cyl {disk.Cylinder:00}.{disk.Head} {motor} {selected}";
		}
	}

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

	public void Reset()
	{
		_bootAttempted = false;
		_previousInterlaceFrameValid = false;
		_firePulseFrames = 0;
		_targetCycle = 0;
		_audioCycle = 0;
		_frameAudio.AsSpan().Clear();
		_machine.ResetHardware();
		Array.Fill(Framebuffer, unchecked((int)0xFF000000));
		StatusText = DiskPath == null ? "insert disk image" : Path.GetFileName(DiskPath);
	}

	public bool TogglePaused()
	{
		IsPaused = !IsPaused;
		return IsPaused;
	}

	public void SetPaused(bool paused)
	{
		IsPaused = paused;
	}

	public bool LaunchCopperBenchPath(string amigaPath, out string message)
	{
		message = string.Empty;
		if (DiskPath == null)
		{
			message = "No disk is inserted.";
			return false;
		}

		try
		{
			var disk = AmigaDiskImage.Load(DiskPath);
			var fileSystem = new AmigaDosFileSystem(disk);
			if (!fileSystem.TryCreateLaunchRequest(amigaPath, out var request, out message))
			{
				return false;
			}

			_bootAttempted = true;
			_previousInterlaceFrameValid = false;
			_firePulseFrames = 0;
			_boot.StartWorkbenchSession(disk);
			if (!_boot.TryLaunchProgram(request, out var launchResult, out message))
			{
				return false;
			}

			_targetCycle = _machine.Cpu.State.Cycles;
			_audioCycle = _targetCycle;
			_frameAudio.AsSpan().Clear();
			StatusText = "CopperBench launched " + launchResult.ExecutablePath;
			message = StatusText;
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AmigaEmulationException or ArgumentException)
		{
			message = ex.Message;
			StatusText = message;
			return false;
		}
	}

	public void PulsePrimaryFire(int frames = 30)
	{
		_firePulseFrames = Math.Max(_firePulseFrames, Math.Max(1, frames));
	}

	public void MoveMousePort(int deltaX, int deltaY)
	{
		_machine.Bus.MoveGamePortMouse(0, deltaX, deltaY);
	}

	public void SetMouseButtons(bool primaryFirePressed, bool secondFirePressed)
	{
		_mousePrimaryFirePressed = primaryFirePressed;
		_mouseSecondFirePressed = secondFirePressed;
		ApplyInputState();
	}

	public void SetJoystickPort(
		bool up,
		bool down,
		bool left,
		bool right,
		bool primaryFirePressed,
		bool secondFirePressed)
	{
		_joystickUp = up;
		_joystickDown = down;
		_joystickLeft = left;
		_joystickRight = right;
		_joystickPrimaryFirePressed = primaryFirePressed;
		_joystickSecondFirePressed = secondFirePressed;
		ApplyInputState();
	}

	public void RenderNextFrame()
	{
		ApplyInputState();
		if (DiskPath == null)
		{
			_frameAudio.AsSpan().Clear();
			_previousInterlaceFrameValid = false;
			InsertDiskScreenRenderer.Render(Framebuffer, Width, Height);
			StatusText = "insert disk image";
			AdvanceInputPulse();
			return;
		}

		if (IsPaused)
		{
			_frameAudio.AsSpan().Clear();
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
		var result = _boot.ContinueExecutionUntilCycle(_targetCycle, maxInstructions: 100_000, _renderFrameAudioUntil);
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
		if (!_bootAttempted || DiskPath == null || IsPaused)
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

		ApplyInputState();
	}

	private void ApplyInputState()
	{
		var pulsePrimaryFirePressed = _firePulseFrames > 0;
		_machine.Bus.GamePort0FirePressed = _mousePrimaryFirePressed || pulsePrimaryFirePressed;
		_machine.Bus.GamePort1FirePressed = _joystickPrimaryFirePressed || pulsePrimaryFirePressed;
		_machine.Bus.GamePort0SecondFirePressed = _mouseSecondFirePressed;
		_machine.Bus.GamePort1SecondFirePressed = _joystickSecondFirePressed;
		_machine.Bus.SetGamePortJoystick(1, _joystickUp, _joystickDown, _joystickLeft, _joystickRight);
	}

	private bool HandleBootResult(AmigaBootResult result)
	{
		var fatalStatus = BuildFatalStatus(result.Diagnostics);
		StatusText = fatalStatus == null
			? $"boot program running: PC=${result.FinalProgramCounter:X6}"
			: fatalStatus;
		if (fatalStatus == null)
		{
			return false;
		}

		InsertDiskScreenRenderer.RenderStatus(Framebuffer, Width, Height, StatusText);
		return true;
	}

	private static string? BuildFatalStatus(IReadOnlyList<AmigaBootDiagnostic> diagnostics)
	{
		StringBuilder? builder = null;
		for (var i = 0; i < diagnostics.Count; i++)
		{
			var code = diagnostics[i].Code;
			if (!IsFatalDiagnostic(code))
			{
				continue;
			}

			if (builder == null)
			{
				builder = new StringBuilder(code);
			}
			else
			{
				builder.Append(", ");
				builder.Append(code);
			}
		}

		return builder?.ToString();
	}

	private static bool IsFatalDiagnostic(string code)
	{
		return code is "AMIGA_BOOT_UNSUPPORTED_OPCODE" or "AMIGA_BOOT_FAULT" or "AMIGA_BOOT_PROTECTED_DISK_UNSUPPORTED";
	}

}
