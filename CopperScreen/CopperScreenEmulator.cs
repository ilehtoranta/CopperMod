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
	private const int DiskSwapEjectFrames = 25;
	private static readonly long PalFrameCycles = Math.Max(1, (long)Math.Round(AmigaConstants.A500PalCpuClockHz / AmigaConstants.A500PalVBlankHz));
	private readonly AmigaMachine _machine;
	private readonly AmigaBootController _boot;
	private readonly CopperScreenProfile _profile;
	private readonly string? _startupError;
	private readonly float[] _frameAudio;
	private readonly int[] _previousInterlaceFrame;
	private readonly Action<long, long> _renderFrameAudioUntil;
	private bool _bootAttempted;
	private bool _previousInterlaceFrameValid;
	private bool _workbenchHandoffPending;
	private bool _copperBenchRequestPending;
	private AmigaDiskImage? _pendingDiskImage;
	private AmigaDiskImage? _initialDiskImageOverride;
	private string? _pendingDiskPath;
	private int _pendingDiskInsertFrames;
	private int _firePulseFrames;
	private long _targetCycle;
	private long _audioCycle;
	private long _frameAudioStartCycle;
	private long _frameAudioEndCycle;
	private long _frameAudioNextSampleCycle;
	private int _frameAudioSampleIndex;
	private int _frameAudioSampleCount;
	private bool _mousePrimaryFirePressed;
	private bool _mouseSecondFirePressed;
	private bool _joystickUp;
	private bool _joystickDown;
	private bool _joystickLeft;
	private bool _joystickRight;
	private bool _joystickPrimaryFirePressed;
	private bool _joystickSecondFirePressed;

	private CopperScreenEmulator(CopperScreenStartupOptions startupOptions, AmigaDiskImage? initialDiskImageOverride = null)
	{
		var machineOptions = CreateMachineOptions(startupOptions, out _startupError);
		Width = AmigaConstants.PalLowResWidth;
		Height = AmigaConstants.PalLowResHeight;
		Framebuffer = new int[Width * Height];
		_profile = startupOptions.Profile;
		_machine = new AmigaMachine(machineOptions);
		_boot = new AmigaBootController(_machine);
		_boot.AutoStartWorkbenchDefaultTool = false;
		_frameAudio = new float[AudioFramesPerAppFrame(DefaultAudioSampleRate) * DefaultAudioChannels];
		_previousInterlaceFrame = new int[Framebuffer.Length];
		_renderFrameAudioUntil = RenderFrameAudioUntil;
		DiskPath = startupOptions.DiskPath;
		_initialDiskImageOverride = initialDiskImageOverride;
		StatusText = _startupError ?? (DiskPath == null ? "insert disk image" : Path.GetFileName(DiskPath));
	}

	public int Width { get; }

	public int Height { get; }

	public int[] Framebuffer { get; }

	public string? DiskPath { get; private set; }

	public string StatusText { get; private set; }

	public bool IsPaused { get; private set; }

	public bool IsWorkbenchHandoffPending => _workbenchHandoffPending;

	public bool IsPrimaryFirePressed => _mousePrimaryFirePressed || _joystickPrimaryFirePressed || _firePulseFrames > 0;

	public bool IsDiskSwapPending => _pendingDiskImage != null;

	internal OcsDisplaySnapshot DisplaySnapshot => _machine.Bus.Display.CaptureSnapshot();

	public bool AudioFilterEnabled => _machine.Bus.AudioFilterEnabled;

	public CopperScreenCpuState CpuState => new CopperScreenCpuState(
		_machine.Cpu.State.ProgramCounter,
		_machine.Cpu.State.LastInstructionProgramCounter,
		_machine.Cpu.State.StatusRegister);

	public string DiskName => DiskPath == null ? "No disk" : Path.GetFileName(DiskPath);

	public string ProfileName => _profile.DisplayName;

	public string ProgramCounterText => $"PC=${_machine.Cpu.State.ProgramCounter:X6}";

	public string DriveStatusText
	{
		get
		{
			if (_pendingDiskImage != null)
			{
				return "DF0 changing disk";
			}

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

	public CopperScreenDriveState[] CaptureDriveStates()
	{
		var disk = _machine.Bus.Disk.CaptureSnapshot();
		var drives = new CopperScreenDriveState[4];
		for (var driveIndex = 0; driveIndex < drives.Length; driveIndex++)
		{
			var drive = GetDrive(driveIndex);
			var connected = driveIndex < disk.ConnectedDriveCount;
			drives[driveIndex] = new CopperScreenDriveState(
				driveIndex,
				connected,
				connected && drive.HasDisk,
				drive.Cylinder,
				drive.Head,
				connected && drive.MotorOn,
				connected && drive.Selected,
				connected && disk.ActiveDma && disk.ActiveDmaDrive == driveIndex);
		}

		return drives;
	}

	public static CopperScreenEmulator Create(string[] args, string baseDirectory)
	{
		return new CopperScreenEmulator(CopperScreenStartupOptions.Parse(args, baseDirectory));
	}

	internal static CopperScreenEmulator CreateWithLoadedDisk(string[] args, string baseDirectory, AmigaDiskImage disk)
	{
		ArgumentNullException.ThrowIfNull(disk);
		return new CopperScreenEmulator(CopperScreenStartupOptions.Parse(args, baseDirectory), disk);
	}

	public static CopperScreenEmulator CreateWithoutDisk()
	{
		return new CopperScreenEmulator(CopperScreenStartupOptions.Default(AppContext.BaseDirectory));
	}

	public static string? ResolveDiskPath(string[] args, string baseDirectory)
	{
		return CopperScreenStartupOptions.Parse(args, baseDirectory).DiskPath;
	}

	private static AmigaMachineOptions CreateMachineOptions(CopperScreenStartupOptions startupOptions, out string? startupError)
	{
		startupError = startupOptions.Error;
		var machineOptions = startupOptions.Profile.CreateMachineOptions();
		if (!startupOptions.Profile.UsesKickstartRom)
		{
			return machineOptions;
		}

		var romPath = startupOptions.KickstartRomPath ?? FindDefaultKickstart13Rom(startupOptions.BaseDirectory);
		if (romPath == null || !File.Exists(romPath))
		{
			startupError ??= "Kickstart 1.3 ROM not found. Expected ROM\\Kickstart_13.rom or pass --kickstart-rom <path>.";
			return machineOptions;
		}

		try
		{
			machineOptions.WithKickstart(AmigaKickstartConfiguration.FromRomImage(
				AmigaKickstartVersion.Kickstart13,
				File.ReadAllBytes(romPath)));
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
		{
			startupError ??= $"Could not load Kickstart 1.3 ROM: {ex.Message}";
		}

		return machineOptions;
	}

	private static string? FindDefaultKickstart13Rom(string baseDirectory)
	{
		var directory = new DirectoryInfo(baseDirectory);
		while (directory != null)
		{
			var localRom = Path.Combine(directory.FullName, "ROM", "Kickstart_13.rom");
			if (File.Exists(localRom))
			{
				return localRom;
			}

			var projectRom = Path.Combine(directory.FullName, "CopperScreen", "ROM", "Kickstart_13.rom");
			if (File.Exists(projectRom))
			{
				return projectRom;
			}

			directory = directory.Parent;
		}

		return null;
	}

	public static string? ResolveNextDiskPath(string? currentDiskPath)
	{
		return ResolveAdjacentDiskPath(currentDiskPath, 1);
	}

	public static string? ResolvePreviousDiskPath(string? currentDiskPath)
	{
		return ResolveAdjacentDiskPath(currentDiskPath, -1);
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

	public bool InsertPreviousDisk()
	{
		var previousDiskPath = ResolvePreviousDiskPath(DiskPath);
		if (previousDiskPath == null)
		{
			StatusText = "no previous disk image";
			return false;
		}

		return InsertDisk(previousDiskPath, markChanged: true);
	}

	public bool InsertDisk(string diskPath, bool markChanged = true)
	{
		if (!File.Exists(diskPath))
		{
			StatusText = "disk image not found";
			return false;
		}

		var fullPath = Path.GetFullPath(diskPath);
		AmigaDiskImage disk;
		try
		{
			disk = AmigaDiskImage.Load(fullPath);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AmigaEmulationException or ArgumentException)
		{
			StatusText = ex.Message;
			return false;
		}

		return InsertLoadedDisk(fullPath, disk, markChanged);
	}

	internal bool InsertLoadedDisk(string fullPath, AmigaDiskImage disk, bool markChanged = true)
	{
		fullPath = Path.GetFullPath(fullPath);
		_workbenchHandoffPending = false;
		_copperBenchRequestPending = false;
		DiskPath = fullPath;
		if (_bootAttempted && markChanged)
		{
			_pendingDiskImage = disk;
			_pendingDiskPath = fullPath;
			_pendingDiskInsertFrames = DiskSwapEjectFrames;
			EjectAllDrives();
			StatusText = "changing disk to " + Path.GetFileName(fullPath);
			return true;
		}

		_pendingDiskImage = null;
		_pendingDiskPath = null;
		_pendingDiskInsertFrames = 0;
		if (_bootAttempted)
		{
			InsertDiskSet(disk, fullPath, markChanged);
		}

		StatusText = "inserted " + Path.GetFileName(fullPath);
		return true;
	}

	internal void SetStatusText(string statusText)
	{
		StatusText = statusText;
	}

	private static string? ResolveAdjacentDiskPath(string? currentDiskPath, int delta)
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
			!int.TryParse(match.Groups["total"].Value, out var total))
		{
			return null;
		}

		var target = number + delta;
		if (target < 1 || target > total)
		{
			return null;
		}

		var numberText = match.Groups["number"].Value;
		var replacement = numberText.Length > 1
			? target.ToString().PadLeft(numberText.Length, '0')
			: target.ToString();
		var nextName = fileName.Remove(match.Groups["number"].Index, match.Groups["number"].Length)
			.Insert(match.Groups["number"].Index, replacement);
		var candidate = Path.Combine(directory, nextName);
		return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
	}

	private void InsertDiskSet(AmigaDiskImage disk, string? diskPath, bool markChanged)
	{
		_boot.Drive0.Insert(disk, markChanged);
		InsertAdjacentExternalDisks(diskPath, markChanged);
	}

	private void InsertAdjacentExternalDisks(string? diskPath, bool markChanged = false)
	{
		for (var driveIndex = 1; driveIndex < _machine.Bus.Disk.ConnectedDriveCount; driveIndex++)
		{
			var drive = GetDrive(driveIndex);
			var adjacentPath = ResolveAdjacentDiskPath(diskPath, driveIndex);
			if (adjacentPath == null)
			{
				drive.Eject();
				continue;
			}

			try
			{
				drive.Insert(AmigaDiskImage.Load(adjacentPath), markChanged);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AmigaEmulationException or ArgumentException)
			{
				drive.Eject();
			}
		}
	}

	private void EjectAllDrives()
	{
		for (var driveIndex = 0; driveIndex < 4; driveIndex++)
		{
			GetDrive(driveIndex).Eject();
		}
	}

	private AmigaFloppyDrive GetDrive(int driveIndex)
	{
		return driveIndex switch
		{
			0 => _boot.Drive0,
			1 => _boot.Drive1,
			2 => _boot.Drive2,
			3 => _boot.Drive3,
			_ => throw new ArgumentOutOfRangeException(nameof(driveIndex))
		};
	}

	public void Reset()
	{
		_bootAttempted = false;
		_previousInterlaceFrameValid = false;
		_workbenchHandoffPending = false;
		_copperBenchRequestPending = false;
		_firePulseFrames = 0;
		_pendingDiskImage = null;
		_pendingDiskPath = null;
		_pendingDiskInsertFrames = 0;
		_targetCycle = 0;
		_audioCycle = 0;
		_frameAudio.AsSpan().Clear();
		_machine.ResetHardware();
		Array.Fill(Framebuffer, unchecked((int)0xFF000000));
		StatusText = _startupError ?? (DiskPath == null ? "insert disk image" : Path.GetFileName(DiskPath));
	}

	public bool TogglePaused()
	{
		if (_workbenchHandoffPending && IsPaused)
		{
			StatusText = "start a Workbench item from CopperBench";
			_copperBenchRequestPending = true;
			return IsPaused;
		}

		IsPaused = !IsPaused;
		return IsPaused;
	}

	public void SetPaused(bool paused)
	{
		if (_workbenchHandoffPending && !paused)
		{
			StatusText = "start a Workbench item from CopperBench";
			_copperBenchRequestPending = true;
			IsPaused = true;
			return;
		}

		IsPaused = paused;
	}

	public bool ConsumeCopperBenchRequest()
	{
		if (!_copperBenchRequestPending)
		{
			return false;
		}

		_copperBenchRequestPending = false;
		return true;
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
			_workbenchHandoffPending = false;
			_copperBenchRequestPending = false;
			_firePulseFrames = 0;
			_boot.StartWorkbenchSession(disk);
			InsertAdjacentExternalDisks(DiskPath);
			if (!_boot.TryLaunchProgram(request, out var launchResult, out message))
			{
				return false;
			}

			IsPaused = false;
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

	public void KeyDown(AmigaRawKey key)
	{
		_machine.Bus.Keyboard.KeyDown(key, _machine.Cpu.State.Cycles);
	}

	public void KeyUp(AmigaRawKey key)
	{
		_machine.Bus.Keyboard.KeyUp(key, _machine.Cpu.State.Cycles);
	}

	public void RenderNextFrame()
	{
		ApplyInputState();
		if (_startupError != null)
		{
			_frameAudio.AsSpan().Clear();
			_previousInterlaceFrameValid = false;
			InsertDiskScreenRenderer.Render(Framebuffer, Width, Height);
			StatusText = _startupError;
			AdvanceInputPulse();
			return;
		}

		if (DiskPath == null)
		{
			_frameAudio.AsSpan().Clear();
			_previousInterlaceFrameValid = false;
			InsertDiskScreenRenderer.Render(Framebuffer, Width, Height);
			StatusText = "insert disk image";
			AdvanceInputPulse();
			return;
		}

		AdvancePendingDiskInsert();
		if (IsPaused)
		{
			_frameAudio.AsSpan().Clear();
			AdvanceInputPulse();
			return;
		}

		if (!_bootAttempted)
		{
			try
			{
				_bootAttempted = true;
				var disk = _initialDiskImageOverride ?? AmigaDiskImage.Load(DiskPath);
				_initialDiskImageOverride = null;
				if (_profile.UsesKickstartRom)
				{
					_boot.StartKickstartRomBoot(disk);
				}
				else
				{
					_boot.StartBootFromDisk(disk);
				}

				InsertAdjacentExternalDisks(DiskPath);

				_targetCycle = 0;
				_audioCycle = _machine.Cpu.State.Cycles;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AmigaEmulationException or ArgumentException or InvalidOperationException)
			{
				_frameAudio.AsSpan().Clear();
				_previousInterlaceFrameValid = false;
				StatusText = ex.Message;
				InsertDiskScreenRenderer.RenderStatus(Framebuffer, Width, Height, StatusText);
				AdvanceInputPulse();
				return;
			}
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

		_machine.Bus.AdvanceRasterTo(_targetCycle);
		_machine.Bus.AdvanceCiasTo(_targetCycle);
		_machine.Bus.AdvanceDmaTo(_targetCycle);
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

	internal CopperScreenPresentationFrame PreparePresentationFrame(long frameNumber)
	{
		var bgra = new int[Framebuffer.Length];
		Framebuffer.AsSpan().CopyTo(bgra);
		return new CopperScreenPresentationFrame(Width, Height, frameNumber, bgra, DisplaySnapshot);
	}

	internal static void RenderPresentationFrame(CopperScreenPresentationFrame frame, Span<int> destination)
	{
		if (destination.Length < frame.Bgra.Length)
		{
			throw new ArgumentException("Destination framebuffer is too small.", nameof(destination));
		}

		frame.Bgra.AsSpan().CopyTo(destination);
	}

	private void BeginFrameAudio(long targetCycle)
	{
		_frameAudio.AsSpan().Clear();
		_frameAudioStartCycle = _audioCycle;
		_frameAudioEndCycle = Math.Max(_frameAudioStartCycle, targetCycle);
		_frameAudioSampleIndex = 0;
		_frameAudioSampleCount = _frameAudio.Length / DefaultAudioChannels;
		_frameAudioNextSampleCycle = GetFrameAudioSampleCycle(0);
	}

	private void RenderFrameAudioUntil(long previousCycle, long currentCycle)
	{
		_ = previousCycle;
		if (_frameAudioEndCycle <= _frameAudioStartCycle ||
			_frameAudioSampleIndex >= _frameAudioSampleCount ||
			currentCycle < _frameAudioNextSampleCycle)
		{
			return;
		}

		while (_frameAudioSampleIndex < _frameAudioSampleCount &&
			_frameAudioNextSampleCycle <= currentCycle)
		{
			_machine.Bus.Paula.RenderSample(_frameAudioNextSampleCycle, _frameAudio, _frameAudioSampleIndex, DefaultAudioChannels);
			_frameAudioSampleIndex++;
			_frameAudioNextSampleCycle = GetFrameAudioSampleCycle(_frameAudioSampleIndex);
		}
	}

	private long GetFrameAudioSampleCycle(int sampleIndex)
	{
		if (_frameAudioSampleCount <= 0)
		{
			return _frameAudioEndCycle;
		}

		var frameCycles = _frameAudioEndCycle - _frameAudioStartCycle;
		return _frameAudioStartCycle + ((frameCycles * (sampleIndex + 1)) / _frameAudioSampleCount);
	}

	private void FinishFrameAudio()
	{
		RenderFrameAudioUntil(_machine.Cpu.State.Cycles, _frameAudioEndCycle);
		_audioCycle = _frameAudioEndCycle;
	}

	private void AdvancePendingDiskInsert()
	{
		if (_pendingDiskImage == null)
		{
			return;
		}

		if (_pendingDiskInsertFrames > 0)
		{
			_pendingDiskInsertFrames--;
			return;
		}

		var insertedPath = _pendingDiskPath ?? DiskPath;
		InsertDiskSet(_pendingDiskImage, insertedPath, markChanged: true);
		_pendingDiskImage = null;
		_pendingDiskPath = null;
		StatusText = insertedPath == null
			? "inserted disk"
			: "inserted " + Path.GetFileName(insertedPath);
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
		if (result.Diagnostics.Any(diagnostic => diagnostic.Code == "AMIGA_BOOT_DOS_WORKBENCH_HANDOFF"))
		{
			_workbenchHandoffPending = true;
			_copperBenchRequestPending = true;
			IsPaused = true;
			StatusText = "Workbench handoff: choose a CopperBench item";
			return false;
		}

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
		return code is "AMIGA_BOOT_UNSUPPORTED_OPCODE" or "AMIGA_BOOT_FAULT" or "AMIGA_BOOT_PROTECTED_DISK_UNSUPPORTED" or "AMIGA_BOOT_NULL_PC";
	}

}
