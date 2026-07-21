/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using CopperMod.Amiga;

namespace CopperScreen;

internal readonly record struct CopperScreenEmulatorFrameTiming(
	double CpuMilliseconds,
	double HardwareMilliseconds,
	double DisplayMilliseconds);

internal sealed class CopperScreenEmulator : IDisposable
{
	private const int DefaultAudioSampleRate = 44_100;
	private const int DefaultAudioChannels = 2;
	private const int DiskSwapEjectFrames = 25;
	private const int MouseButtonEdgePulseFrames = 2;
	private const int DefaultBootMaxInstructionsPerFrame = 100_000;
	private const int JitM68040BootMaxInstructionsPerFrame = 2_000_000;
	private const long JitM68040BootRunAheadCyclesPerFrame = 250_000_000;
	private const string RunningStatusText = "boot program running:";
	private const string TraceBusAccessesEnvironmentVariable = "COPPER_AMIGA_TRACE_BUS_ACCESSES";
	private const string VAmigaTsTraceWritesEnvironmentVariable = "COPPER_AMIGA_VAMIGATS_TRACE_WRITES";
	private readonly Machine _machine;
	private readonly long _timingCpuClockHz;
	private readonly long _timingFrameCycles;
	private readonly AmigaBootController _boot;
	private readonly CopperScreenProfile _profile;
	private readonly string _baseDirectory;
	private readonly FloppyDriveAudioOptions _floppyDriveAudioOptions;
	private CopperScreenInputOptions _inputOptions;
	private readonly string? _startupError;
	private readonly string?[] _initialDriveDiskPaths;
	private readonly bool?[] _initialDriveWriteProtected;
	private readonly IReadOnlyList<CopperScreenHardfileSettings> _initialHardDrives;
	private readonly float[] _frameAudio;
	private readonly int[] _interlacePresentationFrame;
	private uint[] _rtgCompositionBuffer = Array.Empty<uint>();
	private readonly FrameExecutionBoundarySchedule _executionBoundarySchedule;
	private bool _bootAttempted;
	private bool _interlacePresentationFrameValid;
	private CopperScreenPresentationOptions _presentationOptions;
	private bool _workbenchHandoffPending;
	private bool _copperBenchRequestPending;
	private bool _copperStartRuntimeHandoff;
	private AmigaDiskImage? _pendingDiskImage;
	private AmigaDiskImage? _initialDiskImageOverride;
	private string? _pendingDiskPath;
	private int _pendingDiskInsertFrames;
	private int _firePulseFrames;
	private int _mousePrimaryFirePulseFrames;
	private int _mouseSecondFirePulseFrames;
	private long _targetCycle;
	private long _audioCycle;
	private long _frameAudioStartCycle;
	private long _frameAudioEndCycle;
	private long _frameAudioNextSampleCycle;
	private long _defaultAudioFrameRemainder;
	private long _outputAudioFrameRemainder;
	private int _frameAudioSampleIndex;
	private int _frameAudioSampleCount;
	private int _outputAudioFrameSampleRate;
	private bool _mousePrimaryFirePressed;
	private bool _mouseSecondFirePressed;
	private readonly CopperScreenJoystickActions[] _joystickActionsByPort = new CopperScreenJoystickActions[2];
	private readonly string?[] _driveDiskPaths = new string?[4];
	private readonly string[] _driveDiskNames = ["No disk", "No disk", "No disk", "No disk"];
	private string _diskName;
	private CopperScreenDebugSnapshot? _debugSnapshot;

	private CopperScreenEmulator(CopperScreenStartupOptions startupOptions, AmigaDiskImage? initialDiskImageOverride = null)
	{
		var machineOptions = CreateMachineOptions(startupOptions, out _startupError);
		_profile = startupOptions.Profile;
		_baseDirectory = startupOptions.BaseDirectory;
		_floppyDriveAudioOptions = startupOptions.FloppyDriveAudio;
		_inputOptions = startupOptions.Input;
		_presentationOptions = startupOptions.Profile.PresentationOptions;
		_initialDriveDiskPaths = startupOptions.DriveDiskPaths.ToArray();
		_initialDriveWriteProtected = startupOptions.DriveWriteProtected.ToArray();
		_initialHardDrives = startupOptions.HardDrives;
		_machine = new Machine(machineOptions);
		_timingCpuClockHz = _machine.Bus.RasterTiming.CpuClockHz;
		_timingFrameCycles = _machine.Bus.RasterTiming.GetFrameCycles(_machine.Bus.RasterTiming.LongFrameLines);
		_boot = new AmigaBootController(_machine);
		var enableHostWorkbenchStartup = !_profile.UsesKickstartRom && _profile.AutoStartWorkbenchStartupSequence;
		_boot.AutoStartWorkbenchDefaultTool = enableHostWorkbenchStartup;
		_boot.AutoRunStartupSequence = enableHostWorkbenchStartup;
		Width = _machine.Bus.Display.Width;
		Height = _machine.Bus.Display.Height;
		Framebuffer = new int[Width * Height];
		Array.Fill(Framebuffer, unchecked((int)0xFF000000));
		_frameAudio = new float[AudioFramesPerAppFrame(DefaultAudioSampleRate) * DefaultAudioChannels];
		_interlacePresentationFrame = new int[Framebuffer.Length];
		_executionBoundarySchedule = new FrameExecutionBoundarySchedule(this);
		DiskPath = startupOptions.DriveDiskPaths.Length > 0 ? startupOptions.DriveDiskPaths[0] : startupOptions.DiskPath;
		_diskName = CopperScreenDiskImageArchive.GetDisplayName(DiskPath);
		for (var driveIndex = 0; driveIndex < _driveDiskPaths.Length; driveIndex++)
		{
			var path = driveIndex < startupOptions.DriveDiskPaths.Length ? startupOptions.DriveDiskPaths[driveIndex] : null;
			SetDriveDiskMetadata(driveIndex, path);
		}
		_initialDiskImageOverride = initialDiskImageOverride;
		StatusText = _startupError ?? GetInitialStatusText();
	}

	public int Width { get; }

	public int Height { get; }

	internal bool IsInterlaced => _machine.Bus.Display.InterlaceEnabled;

	internal int CompletedInterlaceField { get; private set; }

	internal CopperScreenPresentationGeometry PresentationGeometry
		=> CopperScreenPresentationGeometry.FromRasterTiming(
			_machine.Bus.RasterTiming,
			_machine.Bus.Display.Width == _machine.Bus.RasterTiming.PresentationSuperHighResWidth);

	public int[] Framebuffer { get; }

	internal CopperScreenEmulatorFrameTiming LastFrameTiming { get; private set; }

	public string? DiskPath { get; private set; }

	public string StatusText { get; private set; }

	public bool IsPaused { get; private set; }

	public CopperScreenDebugSnapshot? DebugSnapshot => _debugSnapshot;

	internal CopperScreenDebugSnapshot CaptureDebugSnapshot(string reasonCode, string message, params string[] diagnostics)
		=> CreateDebugSnapshot(reasonCode, message, diagnostics);

	public bool IsWorkbenchHandoffPending => _workbenchHandoffPending;

	public bool IsPrimaryFirePressed =>
		_mousePrimaryFirePressed ||
		_mousePrimaryFirePulseFrames > 0 ||
		_joystickActionsByPort.Any(actions => (actions & CopperScreenJoystickActions.Fire) != 0) ||
		_firePulseFrames > 0;

	public bool IsDiskSwapPending => _pendingDiskImage != null;

	internal OcsDisplaySnapshot DisplaySnapshot => _machine.Bus.Display.CaptureSnapshot();

	internal double VideoVBlankHz => _machine.Bus.RasterTiming.VBlankHz;

	public bool AudioFilterEnabled => _machine.Bus.AudioFilterEnabled;

	public CopperScreenCpuState CpuState => new CopperScreenCpuState(
		_machine.Cpu.State.ProgramCounter,
		_machine.Cpu.State.LastInstructionProgramCounter,
		_machine.Cpu.State.StatusRegister);

	public string DiskName => _diskName;

	public string ProfileName => _profile.DisplayName;

	public string BaseDirectory => _baseDirectory;

	public FloppyDriveAudioOptions FloppyDriveAudioOptions => _floppyDriveAudioOptions;

	public string CpuBackendName => _machine.Options.CpuBackend.ToString();

	internal bool CopperStartRuntimeHandoffActive => _copperStartRuntimeHandoff;

	internal long CopperStartRuntimeHandoffCount => _boot.CopperStartRuntimeHandoffCount;

	public M68kJitCounters JitCounters => _machine.Cpu is M68kJitCore jit ? jit.Counters : default;

	internal M68kPlannedInterpreterCounters PlannedInterpreterCounters =>
		_machine.Cpu is M68kInterpreter interpreter
			? interpreter.CapturePlannedInterpreterCounters()
			: M68kPlannedInterpreterCounters.Empty;

	public M68kInstructionFrequencySnapshot InstructionFrequency =>
		_machine.Cpu is IM68kInstructionFrequencyProvider frequencyProvider
			? frequencyProvider.CaptureInstructionFrequency()
			: M68kInstructionFrequencySnapshot.Empty;

	public void Dispose()
	{
		_machine.Dispose();
	}

	public string ProgramCounterText => $"PC=${_machine.Cpu.State.ProgramCounter:X6}";

	public void ResetInstructionFrequency()
	{
		if (_machine.Cpu is IM68kInstructionFrequencyProvider frequencyProvider)
		{
			frequencyProvider.ResetInstructionFrequency();
		}
	}

	public void SetInstructionFrequencyEnabled(bool enabled)
	{
		if (_machine.Cpu is IM68kInstructionFrequencyProvider frequencyProvider)
		{
			frequencyProvider.InstructionFrequencyEnabled = enabled;
		}
	}

	internal void ResetPlannedInterpreterCounters()
	{
		if (_machine.Cpu is M68kInterpreter interpreter)
		{
			interpreter.ResetPlannedInterpreterCounters();
		}
	}

	internal void SetPlannedInterpreterCountersEnabled(bool enabled)
	{
		if (_machine.Cpu is M68kInterpreter interpreter)
		{
			interpreter.PlannedInterpreterCountersEnabled = enabled;
		}
	}

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
		if (sampleRate <= 0)
		{
			return 0;
		}

		return Math.Max(1, (int)CeilingDiv(
			(long)sampleRate * _timingFrameCycles,
			_timingCpuClockHz));
	}

	public CopperScreenDriveState[] CaptureDriveStates()
	{
		var drives = new CopperScreenDriveState[4];
		CaptureDriveStates(drives);
		return drives;
	}

	public void CaptureDriveStates(Span<CopperScreenDriveState> drives)
	{
		var disk = _machine.Bus.Disk.CaptureSnapshot();
		for (var driveIndex = 0; driveIndex < drives.Length; driveIndex++)
		{
			var drive = GetDrive(driveIndex);
			var connected = driveIndex < disk.ConnectedDriveCount;
			var hasDisk = connected && drive.HasDisk;
			var changingDrive0 = driveIndex == 0 && _pendingDiskImage != null;
			drives[driveIndex] = new CopperScreenDriveState(
				driveIndex,
				connected,
				hasDisk,
				hasDisk ? _driveDiskNames[driveIndex] : changingDrive0 ? _diskName : "No disk",
				hasDisk ? _driveDiskPaths[driveIndex] : changingDrive0 ? DiskPath : null,
				drive.Cylinder,
				drive.Head,
				connected && drive.MotorOn,
				connected && drive.Selected,
				connected && drive.WriteProtected,
				connected && disk.ActiveDma && disk.ActiveDmaDrive == driveIndex);
		}
	}

	public static CopperScreenEmulator Create(string[] args, string baseDirectory)
	{
		return new CopperScreenEmulator(CopperScreenStartupOptions.Parse(args, baseDirectory));
	}

	internal static CopperScreenEmulator Create(CopperScreenStartupOptions startupOptions)
	{
		return new CopperScreenEmulator(startupOptions);
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

	private static MachineOptions CreateMachineOptions(CopperScreenStartupOptions startupOptions, out string? startupError)
	{
		startupError = startupOptions.Error;
		var machineOptions = startupOptions.Profile.CreateMachineOptions();
		var startupFloppyDriveCount = GetStartupFloppyDriveCount(startupOptions);
		if (startupFloppyDriveCount > machineOptions.FloppyDriveCount)
		{
			machineOptions.WithFloppyDriveCount(startupFloppyDriveCount);
		}

		if (startupOptions.CpuBackendOverride.HasValue)
		{
			machineOptions.WithCpu(AmigaM68kCoreFactory.Default, startupOptions.CpuBackendOverride.Value);
		}

		if (startupOptions.CopperQuiescentFastPath || startupOptions.CopperQuiescentFastPathVerify)
		{
			machineOptions.WithCopperQuiescentFastPath(
				startupOptions.CopperQuiescentFastPath,
				startupOptions.CopperQuiescentFastPathVerify);
		}

		if (startupOptions.CopperQuiescentDiagnostics)
		{
			machineOptions.WithCopperQuiescentDiagnostics(true);
		}

		if (startupOptions.DeferredCpuBusBatch || startupOptions.DeferredCpuBusBatchVerify)
		{
			machineOptions.WithDeferredCpuBusBatch(
				startupOptions.DeferredCpuBusBatch,
				startupOptions.DeferredCpuBusBatchVerify);
		}

		if (startupOptions.DeferredCpuChipReadSegments)
		{
			machineOptions.WithDeferredCpuChipReadSegments(true);
		}

		if (startupOptions.CpuWaitSlotReference)
		{
			machineOptions.WithCpuWaitSlotReferencePath(true);
		}

		if (startupOptions.HardwareSpecialization)
		{
			machineOptions.WithHardwareSpecialization(true);
		}

		if (IsEnvironmentSwitchEnabled(TraceBusAccessesEnvironmentVariable) ||
			IsEnvironmentSwitchEnabled(VAmigaTsTraceWritesEnvironmentVariable))
		{
			machineOptions.WithBusAccessLogging(true);
		}

		if (startupOptions.HardDrives.Count != 0)
		{
			machineOptions.WithHardfiles(startupOptions.HardDrives.Select(drive => new AmigaHardfileConfiguration(
				drive.Unit,
				drive.Path,
				drive.ReadOnly,
				drive.CreateSizeBytes,
				drive.Mode,
				drive.Partition)));
		}

		if (!startupOptions.Profile.UsesKickstartRom)
		{
			return machineOptions;
		}

		var romPath = startupOptions.KickstartRomPath ?? FindDefaultRomImage(startupOptions.Profile.KickstartSource, startupOptions.BaseDirectory);
		if (romPath == null || !File.Exists(romPath))
		{
			startupError ??= $"{GetRomDisplayName(startupOptions.Profile.KickstartSource)} not found. Expected {GetDefaultRomHint(startupOptions.Profile.KickstartSource)} or pass --kickstart-rom <path>.";
			return machineOptions;
		}

		try
		{
			machineOptions.WithKickstart(KickstartConfiguration.FromRomImage(
				startupOptions.Profile.KickstartVersion,
				CopperScreenKickstartRomArchive.ReadRomImage(
					romPath,
					startupOptions.Profile.KickstartSource,
					startupOptions.Profile.KickstartVersion)));
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or InvalidOperationException)
		{
			startupError ??= $"Could not load {GetRomDisplayName(startupOptions.Profile.KickstartSource)}: {ex.Message}";
		}

		return machineOptions;
	}

	private static bool IsEnvironmentSwitchEnabled(string name)
	{
		var value = Environment.GetEnvironmentVariable(name);
		return value != null &&
			(value == "1" ||
				value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
				value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
				value.Equals("on", StringComparison.OrdinalIgnoreCase));
	}

	private static int GetStartupFloppyDriveCount(CopperScreenStartupOptions startupOptions)
	{
		var driveCount = startupOptions.Profile.FloppyDriveCount;
		for (var driveIndex = 0; driveIndex < startupOptions.DriveDiskPaths.Length; driveIndex++)
		{
			if (!string.IsNullOrWhiteSpace(startupOptions.DriveDiskPaths[driveIndex]))
			{
				driveCount = Math.Max(driveCount, driveIndex + 1);
			}
		}

		var startupDiskPath = startupOptions.DriveDiskPaths.Length > 0
			? startupOptions.DriveDiskPaths[0]
			: startupOptions.DiskPath;
		if (startupOptions.Profile.FloppyDriveCount > 1)
		{
			for (var driveIndex = 1; driveIndex < 4; driveIndex++)
			{
				if (ResolveAdjacentDiskPath(startupDiskPath, driveIndex) != null)
				{
					driveCount = Math.Max(driveCount, driveIndex + 1);
				}
			}
		}

		return Math.Clamp(driveCount, 1, 4);
	}

	private static string? FindDefaultRomImage(CopperScreenKickstartSource source, string baseDirectory)
	{
		return source switch
		{
			CopperScreenKickstartSource.DiagRom => FindDefaultRomImage(
				baseDirectory,
				Path.Combine("ROM", "DiagROM", "diagrom-a500.rom"),
				Path.Combine("ROM", "DiagROM", "diagrom.rom")),
			CopperScreenKickstartSource.Kickstart13Rom => FindDefaultRomImage(baseDirectory, Path.Combine("ROM", "Kickstart_13.rom")),
			CopperScreenKickstartSource.KickstartRom => FindDefaultRomImage(
				baseDirectory,
				Path.Combine("ROM", "kickstart-3.1-a500.rom"),
				Path.Combine("ROM", "Kickstart_13.rom")),
			_ => FindDefaultRomImage(baseDirectory, Path.Combine("ROM", "Kickstart_13.rom"))
		};
	}

	private static string GetRomDisplayName(CopperScreenKickstartSource source)
		=> source switch
		{
			CopperScreenKickstartSource.DiagRom => "DiagROM",
			CopperScreenKickstartSource.KickstartRom => "Kickstart ROM",
			_ => "Kickstart 1.3 ROM"
		};

	private static string GetDefaultRomHint(CopperScreenKickstartSource source)
		=> source switch
		{
			CopperScreenKickstartSource.DiagRom => "ROM\\DiagROM\\diagrom-a500.rom or ROM\\DiagROM\\diagrom.rom",
			CopperScreenKickstartSource.KickstartRom => "ROM\\kickstart-3.1-a500.rom, a .zip containing a ROM, or ROM\\Kickstart_13.rom",
			_ => "ROM\\Kickstart_13.rom"
		};

	private static string? FindDefaultRomImage(string baseDirectory, string relativePath)
		=> FindDefaultRomImage(baseDirectory, [relativePath]);

	private static string? FindDefaultRomImage(string baseDirectory, params string[] relativePaths)
	{
		var directory = new DirectoryInfo(baseDirectory);
		while (directory != null)
		{
			foreach (var relativePath in relativePaths)
			{
				var localRom = Path.Combine(directory.FullName, relativePath);
				if (File.Exists(localRom))
				{
					return localRom;
				}

				var projectRom = Path.Combine(directory.FullName, "CopperScreen", relativePath);
				if (File.Exists(projectRom))
				{
					return projectRom;
				}
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
		if (!CopperScreenDiskImageArchive.DiskPathExists(diskPath))
		{
			StatusText = "disk image not found";
			return false;
		}

		var fullPath = CopperScreenDiskImageArchive.NormalizeDiskPath(diskPath);
		AmigaDiskImage disk;
		try
		{
			disk = CopperScreenDiskImageArchive.LoadDiskImage(fullPath);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AmigaEmulationException or ArgumentException or InvalidDataException)
		{
			StatusText = ex.Message;
			return false;
		}

		return InsertLoadedDisk(fullPath, disk, markChanged);
	}

	internal bool InsertLoadedDisk(string fullPath, AmigaDiskImage disk, bool markChanged = true)
	{
		fullPath = CopperScreenDiskImageArchive.NormalizeDiskPath(fullPath);
		_workbenchHandoffPending = false;
		_copperBenchRequestPending = false;
		_copperStartRuntimeHandoff = false;
		DiskPath = fullPath;
		_diskName = CopperScreenDiskImageArchive.GetDisplayName(fullPath);
		if (_bootAttempted && markChanged)
		{
			_pendingDiskImage = disk;
			_pendingDiskPath = fullPath;
			_pendingDiskInsertFrames = DiskSwapEjectFrames;
			EjectAllDrives();
			StatusText = "changing disk to " + _diskName;
			return true;
		}

		_pendingDiskImage = null;
		_pendingDiskPath = null;
		_pendingDiskInsertFrames = 0;
		if (_bootAttempted)
		{
			InsertDiskSet(disk, fullPath, markChanged);
		}

		StatusText = "inserted " + _diskName;
		return true;
	}

	internal bool InsertLoadedDisk(int driveIndex, string fullPath, AmigaDiskImage disk, bool markChanged = true)
	{
		if (driveIndex == 0)
		{
			return InsertLoadedDisk(fullPath, disk, markChanged);
		}

		if ((uint)driveIndex >= 4)
		{
			throw new ArgumentOutOfRangeException(nameof(driveIndex));
		}

		if (driveIndex >= _machine.Bus.Disk.ConnectedDriveCount)
		{
			StatusText = $"DF{driveIndex}: drive is not connected";
			return false;
		}

		fullPath = CopperScreenDiskImageArchive.NormalizeDiskPath(fullPath);
		GetDrive(driveIndex).Insert(disk, markChanged);
		SetDriveDiskMetadata(driveIndex, fullPath);
		StatusText = $"inserted DF{driveIndex}: {CopperScreenDiskImageArchive.GetDisplayName(fullPath)}";
		return true;
	}

	internal bool SetDriveWriteProtected(int driveIndex, bool writeProtected)
	{
		if ((uint)driveIndex >= 4)
		{
			throw new ArgumentOutOfRangeException(nameof(driveIndex));
		}

		if (driveIndex >= _machine.Bus.Disk.ConnectedDriveCount)
		{
			StatusText = $"DF{driveIndex}: drive is not connected";
			return false;
		}

		var drive = GetDrive(driveIndex);
		if (!drive.HasDisk)
		{
			StatusText = $"DF{driveIndex}: no disk inserted";
			return false;
		}

		drive.SetWriteProtected(writeProtected);
		StatusText = $"DF{driveIndex}: write protect {(writeProtected ? "on" : "off")}";
		return true;
	}

	internal void SetStatusText(string statusText)
	{
		StatusText = statusText;
	}

	internal void CaptureFatalException(Exception exception)
	{
		ArgumentNullException.ThrowIfNull(exception);
		CaptureFatalStop(
			"COPPERSCREEN_RUNTIME_FAULT",
			exception.Message,
			[exception.ToString()]);
	}

	private static string? ResolveAdjacentDiskPath(string? currentDiskPath, int delta)
	{
		if (string.IsNullOrWhiteSpace(currentDiskPath))
		{
			return null;
		}

		var archiveAdjacentPath = CopperScreenDiskImageArchive.ResolveAdjacentEntryPath(currentDiskPath, delta);
		if (archiveAdjacentPath != null)
		{
			return archiveAdjacentPath;
		}

		var directory = Path.GetDirectoryName(currentDiskPath);
		var fileName = Path.GetFileName(currentDiskPath);
		if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
		{
			return null;
		}

		var match = MatchDiskNumber(fileName);
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
		if (File.Exists(candidate))
		{
			return Path.GetFullPath(candidate);
		}

		return ResolveUniqueAdjacentDiskSibling(directory, fileName, match, target, total);
	}

	private static Match MatchDiskNumber(string fileName)
		=> Regex.Match(fileName, @"(?<prefix>Disk\s*)(?<number>\d+)(?<suffix>\s*of\s*(?<total>\d+))", RegexOptions.IgnoreCase);

	private static string? ResolveUniqueAdjacentDiskSibling(
		string directory,
		string fileName,
		Match sourceMatch,
		int target,
		int total)
	{
		if (!Directory.Exists(directory))
		{
			return null;
		}

		var sourceNumberPrefix = fileName[..sourceMatch.Groups["number"].Index];
		var extension = Path.GetExtension(fileName);
		var matches = new List<string>();
		foreach (var path in Directory.EnumerateFiles(directory))
		{
			var siblingName = Path.GetFileName(path);
			if (!string.Equals(Path.GetExtension(siblingName), extension, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var siblingMatch = MatchDiskNumber(siblingName);
			if (!siblingMatch.Success ||
				!int.TryParse(siblingMatch.Groups["number"].Value, out var siblingNumber) ||
				!int.TryParse(siblingMatch.Groups["total"].Value, out var siblingTotal) ||
				siblingNumber != target ||
				siblingTotal != total)
			{
				continue;
			}

			var siblingNumberPrefix = siblingName[..siblingMatch.Groups["number"].Index];
			if (!string.Equals(siblingNumberPrefix, sourceNumberPrefix, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			matches.Add(path);
		}

		return matches.Count == 1 ? Path.GetFullPath(matches[0]) : null;
	}

	private void InsertDiskSet(AmigaDiskImage disk, string? diskPath, bool markChanged)
	{
		_boot.Drive0.Insert(disk, markChanged);
		SetDriveDiskMetadata(0, diskPath);
		InsertAdjacentExternalDisks(diskPath, markChanged);
	}

	private AmigaDiskImage? LoadInitialBootDisk()
	{
		if (DiskPath != null)
		{
			return CopperScreenDiskImageArchive.LoadDiskImage(DiskPath);
		}

		if (_profile.BootsWithoutDisk)
		{
			return AmigaDiskImage.FromAdfBytes(new byte[AmigaDiskImage.StandardAdfSize], "diagrom-blank.adf");
		}

		if (_profile.UsesKickstartRom)
		{
			return null;
		}

		throw new InvalidOperationException("A disk image is required to boot this profile.");
	}

	private void ApplyConfiguredBootDriveWriteProtection()
	{
		if (_initialDriveWriteProtected.Length > 0 && _initialDriveWriteProtected[0] is bool writeProtected)
		{
			_boot.Drive0.SetWriteProtected(writeProtected);
		}
	}

	private void InsertConfiguredExternalDisks(string? diskPath, bool markChanged = false)
	{
		for (var driveIndex = 1; driveIndex < _machine.Bus.Disk.ConnectedDriveCount; driveIndex++)
		{
			var configuredPath = driveIndex < _initialDriveDiskPaths.Length ? _initialDriveDiskPaths[driveIndex] : null;
			var diskToInsert = string.IsNullOrWhiteSpace(configuredPath)
				? ResolveAdjacentDiskPath(diskPath, driveIndex)
				: configuredPath;
			if (diskToInsert == null)
			{
				EjectDrive(driveIndex);
				continue;
			}

			try
			{
				var writeProtected = driveIndex < _initialDriveWriteProtected.Length ? _initialDriveWriteProtected[driveIndex] : null;
				GetDrive(driveIndex).Insert(CopperScreenDiskImageArchive.LoadDiskImage(diskToInsert), markChanged, writeProtected);
				SetDriveDiskMetadata(driveIndex, diskToInsert);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AmigaEmulationException or ArgumentException or InvalidDataException)
			{
				StatusText = string.IsNullOrWhiteSpace(configuredPath)
					? $"Could not auto-insert DF{driveIndex}: {ex.Message}"
					: $"Could not insert DF{driveIndex}: {ex.Message}";
				EjectDrive(driveIndex);
			}
		}
	}

	private void InsertAdjacentExternalDisks(string? diskPath, bool markChanged = false)
	{
		for (var driveIndex = 1; driveIndex < _machine.Bus.Disk.ConnectedDriveCount; driveIndex++)
		{
			var drive = GetDrive(driveIndex);
			var adjacentPath = ResolveAdjacentDiskPath(diskPath, driveIndex);
			if (adjacentPath == null)
			{
				EjectDrive(driveIndex);
				continue;
			}

			try
			{
				drive.Insert(CopperScreenDiskImageArchive.LoadDiskImage(adjacentPath), markChanged);
				SetDriveDiskMetadata(driveIndex, adjacentPath);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AmigaEmulationException or ArgumentException or InvalidDataException)
			{
				EjectDrive(driveIndex);
			}
		}
	}

	private void EjectAllDrives()
	{
		for (var driveIndex = 0; driveIndex < 4; driveIndex++)
		{
			EjectDrive(driveIndex);
		}
	}

	private void EjectDrive(int driveIndex)
	{
		GetDrive(driveIndex).Eject();
		SetDriveDiskMetadata(driveIndex, null);
	}

	private void SetDriveDiskMetadata(int driveIndex, string? diskPath)
	{
		_driveDiskPaths[driveIndex] = diskPath == null ? null : CopperScreenDiskImageArchive.NormalizeDiskPath(diskPath);
		_driveDiskNames[driveIndex] = CopperScreenDiskImageArchive.GetDisplayName(diskPath);
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
		var wasDebuggerStopped = _debugSnapshot != null;
		_bootAttempted = false;
		InvalidateInterlacePresentationHistory();
		_workbenchHandoffPending = false;
		_copperBenchRequestPending = false;
		_copperStartRuntimeHandoff = false;
		_debugSnapshot = null;
		_firePulseFrames = 0;
		_pendingDiskImage = null;
		_pendingDiskPath = null;
		_pendingDiskInsertFrames = 0;
		_targetCycle = 0;
		_audioCycle = 0;
		_frameAudio.AsSpan().Clear();
		_machine.ResetHardware();
		Array.Fill(Framebuffer, unchecked((int)0xFF000000));
		if (wasDebuggerStopped)
		{
			IsPaused = false;
		}

		StatusText = _startupError ?? GetInitialStatusText();
	}

	private string GetInitialStatusText()
	{
		if (DiskPath != null)
		{
			return _diskName;
		}

		return _profile.BootsWithoutDisk || _profile.UsesKickstartRom ? _profile.DisplayName : "insert disk image";
	}

	public bool TogglePaused()
	{
		if (_debugSnapshot != null)
		{
			IsPaused = true;
			StatusText = "debugger stopped: reset to continue";
			return true;
		}

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
		if (_debugSnapshot != null && !paused)
		{
			StatusText = "debugger stopped: reset to continue";
			IsPaused = true;
			return;
		}

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

		if (_profile.UsesKickstartRom)
		{
			message = "Direct CopperBench launch is only available for CopperStart profiles; real Kickstart ROM profiles must boot through the ROM.";
			StatusText = message;
			return false;
		}

		try
		{
			var disk = CopperScreenDiskImageArchive.LoadDiskImage(DiskPath);
			var fileSystem = new AmigaDosFileSystem(disk);
			if (!fileSystem.TryCreateLaunchRequest(amigaPath, out var request, out message))
			{
				return false;
			}

			_bootAttempted = true;
				InvalidateInterlacePresentationHistory();
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
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AmigaEmulationException or ArgumentException or InvalidDataException)
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
		_boot.MoveSyntheticMouse(deltaX, deltaY);
		for (var portIndex = 0; portIndex < _joystickActionsByPort.Length; portIndex++)
		{
			if (_inputOptions.IsMousePort(portIndex))
			{
				_machine.Bus.MoveGamePortMouse(portIndex, deltaX, deltaY);
			}
		}
	}

	public void SetMousePortPosition(int x, int y)
	{
		_boot.SetSyntheticMousePosition(x, y);
	}

	public void SetMousePresentationPosition(int x, int y)
	{
		_boot.SetSyntheticMousePresentationPosition(x, y);
	}

	public void SetMouseButtons(bool primaryFirePressed, bool secondFirePressed)
	{
		_boot.SetSyntheticMouseButtons(primaryFirePressed, secondFirePressed);
		if (primaryFirePressed && !_mousePrimaryFirePressed)
		{
			_mousePrimaryFirePulseFrames = Math.Max(_mousePrimaryFirePulseFrames, MouseButtonEdgePulseFrames);
		}

		if (secondFirePressed && !_mouseSecondFirePressed)
		{
			_mouseSecondFirePulseFrames = Math.Max(_mouseSecondFirePulseFrames, MouseButtonEdgePulseFrames);
		}

		_mousePrimaryFirePressed = primaryFirePressed;
		_mouseSecondFirePressed = secondFirePressed;
		_executionBoundarySchedule.BeginFrame();
	}

	public void SetJoystickPort(
		bool up,
		bool down,
		bool left,
		bool right,
		bool primaryFirePressed,
		bool secondFirePressed)
		=> SetJoystickPort(_inputOptions.JoystickPortIndex, up, down, left, right, primaryFirePressed, secondFirePressed);

	public void SetJoystickPort(
		int portIndex,
		bool up,
		bool down,
		bool left,
		bool right,
		bool primaryFirePressed,
		bool secondFirePressed)
	{
		if ((uint)portIndex >= (uint)_joystickActionsByPort.Length)
		{
			return;
		}

		var actions = CopperScreenJoystickActions.None;
		if (up)
		{
			actions |= CopperScreenJoystickActions.Up;
		}

		if (down)
		{
			actions |= CopperScreenJoystickActions.Down;
		}

		if (left)
		{
			actions |= CopperScreenJoystickActions.Left;
		}

		if (right)
		{
			actions |= CopperScreenJoystickActions.Right;
		}

		if (primaryFirePressed)
		{
			actions |= CopperScreenJoystickActions.Fire;
		}

		if (secondFirePressed)
		{
			actions |= CopperScreenJoystickActions.SecondFire;
		}

		_joystickActionsByPort[portIndex] = actions;
		ApplyInputState();
	}

	public void SetInputOptions(CopperScreenInputOptions inputOptions)
	{
		_inputOptions = inputOptions;
		ApplyInputState();
	}

	public void SetPresentationOptions(CopperScreenPresentationOptions options)
	{
		if (_presentationOptions.Equals(options))
		{
			return;
		}

		if (_presentationOptions.LacedMode != options.LacedMode)
		{
			InvalidateInterlacePresentationHistory();
		}

		_presentationOptions = options;
	}

	public void KeyDown(AmigaRawKey key)
	{
		_machine.Bus.Keyboard.KeyDown(key, _machine.Cpu.State.Cycles);
	}

	public void KeyUp(AmigaRawKey key)
	{
		_machine.Bus.Keyboard.KeyUp(key, _machine.Cpu.State.Cycles);
	}

	[HotPath]
	public void RenderNextFrame()
		=> RenderNextFrame(Framebuffer, renderPresentation: true);

	[HotPath]
	internal void RenderNextFrame(bool renderPresentation)
		=> RenderNextFrame(Framebuffer, renderPresentation);

	[HotPath]
	internal void RenderNextFrame(int[] presentationFramebuffer)
		=> RenderNextFrame(presentationFramebuffer, renderPresentation: true);

	[HotPath]
	private void RenderNextFrame(int[] presentationFramebuffer, bool renderPresentation)
	{
		ValidatePresentationFramebuffer(presentationFramebuffer);
		LastFrameTiming = default;
		ApplyInputState();
		if (_startupError != null)
		{
			_frameAudio.AsSpan().Clear();
			InvalidateInterlacePresentationHistory();
			StatusText = _startupError;
			if (renderPresentation)
			{
				var displayStart = Stopwatch.GetTimestamp();
				RenderStatusFrame(StatusText, presentationFramebuffer);
				LastFrameTiming = LastFrameTiming with
				{
					DisplayMilliseconds = Stopwatch.GetElapsedTime(displayStart).TotalMilliseconds
				};
			}

			_executionBoundarySchedule.CompleteFrame();
			return;
		}

		if (DiskPath == null && !_profile.BootsWithoutDisk && !_profile.UsesKickstartRom)
		{
			_frameAudio.AsSpan().Clear();
			InvalidateInterlacePresentationHistory();
			StatusText = "insert disk image";
			if (renderPresentation)
			{
				var displayStart = Stopwatch.GetTimestamp();
				RenderNoDiskFrame(presentationFramebuffer);
				LastFrameTiming = LastFrameTiming with
				{
					DisplayMilliseconds = Stopwatch.GetElapsedTime(displayStart).TotalMilliseconds
				};
			}

			_executionBoundarySchedule.CompleteFrame();
			return;
		}

		_executionBoundarySchedule.ApplyPendingDiskInsert();
		if (IsPaused)
		{
			_frameAudio.AsSpan().Clear();
			_executionBoundarySchedule.CompleteFrame();
			return;
		}

		if (!_bootAttempted)
		{
			try
			{
				_bootAttempted = true;
				var disk = _initialDiskImageOverride ?? LoadInitialBootDisk();
				_initialDiskImageOverride = null;
				if (_profile.UsesKickstartRom)
				{
					if (disk == null)
					{
						_boot.StartKickstartRomBoot();
					}
					else
					{
						_boot.StartKickstartRomBoot(disk);
					}
				}
				else
				{
					_boot.StartBootFromDisk(disk ?? throw new InvalidOperationException("A disk image is required to boot this profile."));
				}

				_copperStartRuntimeHandoff = false;
				ApplyConfiguredBootDriveWriteProtection();
				InsertConfiguredExternalDisks(DiskPath);

				_targetCycle = 0;
				_audioCycle = _machine.Cpu.State.Cycles;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AmigaEmulationException or ArgumentException or InvalidOperationException)
			{
				_frameAudio.AsSpan().Clear();
				InvalidateInterlacePresentationHistory();
				StatusText = ex.Message;
				if (renderPresentation)
				{
					var displayStart = Stopwatch.GetTimestamp();
					RenderStatusFrame(StatusText, presentationFramebuffer);
					LastFrameTiming = LastFrameTiming with
					{
						DisplayMilliseconds = Stopwatch.GetElapsedTime(displayStart).TotalMilliseconds
					};
				}

				_executionBoundarySchedule.CompleteFrame();
				return;
			}
		}

		var frameStartCycle = _targetCycle;
		var frameTargetCycle = GetPresentationFrameStopCycle(frameStartCycle);
		_targetCycle = frameTargetCycle;
		var executionTargetCycle = GetBootExecutionTargetCycle(frameTargetCycle);
		var presentationBound = false;
		var presentationInterlace = false;
		if (renderPresentation)
		{
			presentationInterlace = BeginPresentationFrame(
				frameStartCycle,
				frameTargetCycle,
				presentationFramebuffer);
			presentationBound = true;
		}

		try
		{
			_executionBoundarySchedule.BeginExecution(frameStartCycle, frameTargetCycle);
			AmigaBootResult result;
			var cpuStart = Stopwatch.GetTimestamp();
		if (_copperStartRuntimeHandoff)
		{
			result = _boot.ContinueCopperStartRuntimeUntilCycle(
				executionTargetCycle,
				GetBootMaxInstructionsPerFrame(),
				_executionBoundarySchedule);
		}
		else
		{
			result = _boot.ContinueExecutionUntilCycle(
				executionTargetCycle,
				GetBootMaxInstructionsPerFrame(),
				_executionBoundarySchedule);
		}

		LastFrameTiming = LastFrameTiming with
		{
			CpuMilliseconds = Stopwatch.GetElapsedTime(cpuStart).TotalMilliseconds
		};

		_executionBoundarySchedule.CompleteExecution(frameTargetCycle);
		if (HandleBootResult(result))
		{
			if (renderPresentation)
			{
				_machine.Bus.Display.AbortPresentationFrame();
				presentationBound = false;
				var displayStart = Stopwatch.GetTimestamp();
				RenderStatusFrame(StatusText, presentationFramebuffer);
				LastFrameTiming = LastFrameTiming with
				{
					DisplayMilliseconds = Stopwatch.GetElapsedTime(displayStart).TotalMilliseconds
				};
			}

			_executionBoundarySchedule.CompleteFrame();
			return;
		}

		if (!_copperStartRuntimeHandoff &&
			!_profile.UsesKickstartRom &&
			_boot.TryPrepareCopperStartRuntimeHandoff())
		{
			_copperStartRuntimeHandoff = true;
		}

		var hardwareStart = Stopwatch.GetTimestamp();
		_machine.Bus.AdvanceHardwareTo(frameTargetCycle);
		LastFrameTiming = LastFrameTiming with
		{
			HardwareMilliseconds = Stopwatch.GetElapsedTime(hardwareStart).TotalMilliseconds
		};

		var presentationFrameStopCycle = GetCompletedPresentationFrameStopCycle(frameStartCycle, frameTargetCycle);
		_targetCycle = presentationFrameStopCycle;
		if (renderPresentation)
		{
			var displayStart = Stopwatch.GetTimestamp();
			CompletePresentationFrame(
				frameStartCycle,
				presentationFrameStopCycle,
				presentationFramebuffer,
				presentationInterlace);
			presentationBound = false;
			if (_boot.TryGetRtgComposition(out var composition))
			{
				_ = TryRenderRtgPresentation(presentationFramebuffer, composition);
			}
			LastFrameTiming = LastFrameTiming with
			{
				DisplayMilliseconds = Stopwatch.GetElapsedTime(displayStart).TotalMilliseconds
			};
		}

		_executionBoundarySchedule.CompleteFrame();
		}
		finally
		{
			if (presentationBound)
			{
				_machine.Bus.Display.AbortPresentationFrame();
			}
		}
	}

	private bool TryRenderRtgPresentation(
		Span<int> destination,
		CyberGraphicsDisplayComposition composition)
	{
		if (composition.Width <= 0 || composition.Height <= 0)
		{
			return false;
		}

		var pixelCount = checked(composition.Width * composition.Height);
		if (_rtgCompositionBuffer.Length < pixelCount)
		{
			_rtgCompositionBuffer = new uint[pixelCount];
		}

		var logical = _rtgCompositionBuffer.AsSpan(0, pixelCount);
		ComposeRtgLogicalFrame(destination, Width, Height, composition, logical);

		destination.Clear();
		var scale = Math.Min(
			(double)Width / composition.Width,
			(double)Height / composition.Height);
		var destinationWidth = Math.Max(1, Math.Min(Width, (int)Math.Round(composition.Width * scale)));
		var destinationHeight = Math.Max(1, Math.Min(Height, (int)Math.Round(composition.Height * scale)));
		var left = (Width - destinationWidth) / 2;
		var top = (Height - destinationHeight) / 2;
		for (var y = 0; y < destinationHeight; y++)
		{
			var sourceY = Math.Min(composition.Height - 1, y * composition.Height / destinationHeight);
			var destinationRow = (top + y) * Width + left;
			var sourceRow = sourceY * composition.Width;
			for (var x = 0; x < destinationWidth; x++)
			{
				var sourceX = Math.Min(composition.Width - 1, x * composition.Width / destinationWidth);
				destination[destinationRow + x] = unchecked((int)logical[sourceRow + sourceX]);
			}
		}

		if (composition.TopIsRtg)
		{
			_boot.GetRtgPointerPosition(out var pointerX, out var pointerY);
			var hostX = left + Math.Clamp(pointerX, 0, composition.Width - 1) * destinationWidth / composition.Width;
			var hostY = top + Math.Clamp(pointerY, 0, composition.Height - 1) * destinationHeight / composition.Height;
			CompositeRtgPointer(destination, hostX, hostY);
		}

		return true;
	}

	internal static void ComposeRtgLogicalFrame(
		ReadOnlySpan<int> planar,
		int planarWidth,
		int planarHeight,
		CyberGraphicsDisplayComposition composition,
		Span<uint> logical)
	{
		var required = checked(composition.Width * composition.Height);
		if (logical.Length < required)
		{
			throw new ArgumentException("The logical RTG composition buffer is too small.", nameof(logical));
		}

		logical = logical[..required];
		var topBackground = composition.Layers.Count != 0 && composition.Layers[0].IsRtg
			? composition.Layers[0].BackgroundColor
			: 0xFF00_0000u;
		logical.Fill(topBackground);
		var copyWidth = Math.Min(planarWidth, composition.Width);
		var copyHeight = Math.Min(planarHeight, composition.Height);
		if (planarWidth > 0 && planarHeight > 0 && planar.Length >= checked(planarWidth * planarHeight))
		{
			for (var y = 0; y < copyHeight; y++)
			{
				for (var x = 0; x < copyWidth; x++)
				{
					logical[y * composition.Width + x] = unchecked((uint)planar[y * planarWidth + x]);
				}
			}
		}

		if (composition.TopDpmsOff)
		{
			logical.Fill(0xFF00_0000u);
			return;
		}

		for (var y = 0; y < composition.Height; y++)
		{
			CyberGraphicsDisplayLayer? owner = null;
			foreach (var layer in composition.Layers)
			{
				if ((long)y >= layer.Y && (long)y < (long)layer.Y + layer.Height)
				{
					owner = layer;
					break;
				}
			}

			if (!owner.HasValue || !owner.Value.IsRtg)
			{
				continue;
			}

			var rtg = owner.Value;
			var row = logical.Slice(y * composition.Width, composition.Width);
			row.Fill(rtg.DpmsOff ? 0xFF00_0000u : rtg.BackgroundColor);
			if (rtg.DpmsOff || rtg.Bgra == null)
			{
				continue;
			}

			var sourceY = rtg.SourceY + (y - rtg.Y);
			if ((uint)sourceY >= (uint)rtg.SourceHeight)
			{
				continue;
			}

			var destinationStart = Math.Max(0, rtg.X);
			var destinationStop = (int)Math.Min(
				composition.Width,
				Math.Max((long)destinationStart, (long)rtg.X + rtg.Width));
			for (var destinationX = destinationStart; destinationX < destinationStop; destinationX++)
			{
				var sourceX = rtg.SourceX + (destinationX - rtg.X);
				if ((uint)sourceX < (uint)rtg.SourceWidth)
				{
					row[destinationX] = rtg.Bgra[sourceY * rtg.SourceWidth + sourceX];
				}
			}
		}
	}

	private void CompositeRtgPointer(Span<int> destination, int originX, int originY)
	{
		for (var y = 0; y < 12; y++)
		{
			var width = Math.Min(8, 1 + (y / 2));
			for (var x = 0; x < width; x++)
			{
				var px = originX + x;
				var py = originY + y;
				if ((uint)px >= Width || (uint)py >= Height)
				{
					continue;
				}

				var edge = x == 0 || x == width - 1 || y == 0 || y == 11;
				destination[py * Width + px] = edge ? unchecked((int)0xFF00_0000u) : unchecked((int)0xFFFF_FFFFu);
			}
		}
	}

	private int GetBootMaxInstructionsPerFrame()
		=> _profile.CpuBackend == M68kBackendKind.JitM68040
			? JitM68040BootMaxInstructionsPerFrame
			: DefaultBootMaxInstructionsPerFrame;

	private long GetBootExecutionTargetCycle(long frameTargetCycle)
	{
		if (_profile.CpuBackend != M68kBackendKind.JitM68040)
		{
			return frameTargetCycle;
		}

		return Math.Max(frameTargetCycle, _machine.Cpu.State.Cycles + JitM68040BootRunAheadCyclesPerFrame);
	}

	internal long GetPresentationFrameStopCycle(long frameStartCycle)
		=> _machine.Bus.GetFrameStopCycle(frameStartCycle);

	private int GetPresentationFrameNumber(long frameStartCycle)
		=> _machine.Bus.GetBeamPosition(frameStartCycle).FrameNumber;

	private int GetCompletedPresentationFrameNumber(long frameStopCycle)
		=> _machine.Bus.GetBeamPosition(Math.Max(0, frameStopCycle - 1)).FrameNumber;

	private long GetCompletedPresentationFrameStopCycle(long frameStartCycle, long predictedFrameStopCycle)
	{
		var actualFrameStopCycle = GetPresentationFrameStopCycle(frameStartCycle);
		return actualFrameStopCycle <= predictedFrameStopCycle
			? actualFrameStopCycle
			: predictedFrameStopCycle;
	}

	[HotPath]
	public int RenderAudio(Span<float> destination, int sampleRate, int channels)
	{
		if (sampleRate <= 0 || channels <= 0)
		{
			destination.Clear();
			return 0;
		}

		var frameCapacity = Math.Min(AudioFramesPerAppFrame(sampleRate), destination.Length / channels);
		var frames = Math.Min(frameCapacity, sampleRate == DefaultAudioSampleRate && channels == DefaultAudioChannels && _bootAttempted && DiskPath != null && !IsPaused
			? _frameAudioSampleCount
			: ConsumeOutputAudioFrameCount(sampleRate));
		var span = destination.Slice(0, frames * channels);
		span.Clear();
		if (!_bootAttempted || DiskPath == null || IsPaused)
		{
			return frames;
		}

		if (sampleRate == DefaultAudioSampleRate && channels == DefaultAudioChannels)
		{
			_frameAudio.AsSpan(0, Math.Min(span.Length, _frameAudioSampleCount * DefaultAudioChannels)).CopyTo(span);
			return frames;
		}

		var sourceFrames = _frameAudioSampleCount;
		if (sourceFrames <= 0)
		{
			return frames;
		}

		for (var frame = 0; frame < frames; frame++)
		{
			var sourceFrame = MapOutputFrameToSourceFrame(frame, frames, sourceFrames);
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

	[HotPathAllocationAllowed("Presentation snapshots allocate a copied framebuffer for UI handoff outside the emulator frame hot path.")]
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
		_frameAudioSampleCount = Math.Min(
			_frameAudio.Length / DefaultAudioChannels,
			ConsumeExactAudioFrameCount(DefaultAudioSampleRate, ref _defaultAudioFrameRemainder));
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
			_machine.Bus.Paula.RenderSample(
				_frameAudioNextSampleCycle,
				_frameAudio,
				_frameAudioSampleIndex,
				DefaultAudioChannels,
				advanceRegisterObservable: false);
			_frameAudioSampleIndex++;
			_frameAudioNextSampleCycle = GetFrameAudioSampleCycle(_frameAudioSampleIndex);
		}
	}

	private long GetFrameAudioBatchHorizon(long currentCycle, long targetCycle)
	{
		if (_frameAudioSampleIndex >= _frameAudioSampleCount)
		{
			return targetCycle;
		}

		return Math.Clamp(_frameAudioNextSampleCycle, currentCycle + 1, targetCycle);
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

	internal static int MapOutputFrameToSourceFrame(int frame, int outputFrames, int sourceFrames)
	{
		if (sourceFrames <= 1 || outputFrames <= 1)
		{
			return 0;
		}

		return Math.Min(sourceFrames - 1, (int)(((long)frame * sourceFrames) / outputFrames));
	}

	private int ConsumeOutputAudioFrameCount(int sampleRate)
	{
		if (_outputAudioFrameSampleRate != sampleRate)
		{
			_outputAudioFrameSampleRate = sampleRate;
			_outputAudioFrameRemainder = 0;
		}

		return ConsumeExactAudioFrameCount(sampleRate, ref _outputAudioFrameRemainder);
	}

	private int ConsumeExactAudioFrameCount(int sampleRate, ref long remainder)
	{
		if (sampleRate <= 0)
		{
			remainder = 0;
			return 0;
		}

		var numerator = ((long)sampleRate * _timingFrameCycles) + remainder;
		var frames = numerator / _timingCpuClockHz;
		remainder = numerator % _timingCpuClockHz;
		return (int)frames;
	}

	private static long CeilingDiv(long numerator, long denominator)
	{
		if (denominator <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(denominator), "Denominator must be positive.");
		}

		return numerator <= 0 ? 0 : ((numerator - 1) / denominator) + 1;
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
		_diskName = CopperScreenDiskImageArchive.GetDisplayName(insertedPath);
		StatusText = insertedPath == null
			? "inserted disk"
			: "inserted " + _diskName;
	}

	private bool BeginPresentationFrame(long frameStartCycle, long frameEndCycle, int[] framebuffer)
	{
		ValidatePresentationFramebuffer(framebuffer);
		var interlace = _machine.Bus.Display.InterlaceEnabled;
		var target = interlace ? _interlacePresentationFrame : framebuffer;
		if (!interlace)
		{
			InvalidateInterlacePresentationHistory();
		}

		_machine.Bus.Display.BeginPresentationFrame(
			new PresentationFrameTarget(target),
			frameStartCycle,
			frameEndCycle);
		return interlace;
	}

	private void CompletePresentationFrame(long frameStartCycle, long frameEndCycle, int[] framebuffer, bool interlace)
	{
		_machine.Bus.Display.CompletePresentationFrame(frameEndCycle);
		if (!interlace)
		{
			return;
		}

		var interlaceField = GetPresentationFrameNumber(frameStartCycle) & 1;
		CompletedInterlaceField = interlaceField;
		if (!_interlacePresentationFrameValid)
		{
			SeedMissingInterlaceFieldRows(
				_interlacePresentationFrame,
				_machine.Bus.Display.Width,
				_machine.Bus.Display.Height,
				interlaceField);
			_interlacePresentationFrameValid = true;
		}

		ComposeInterlacePresentationFrame(interlaceField, framebuffer);
	}

	private void ComposeInterlacePresentationFrame(int interlaceField)
		=> ComposeInterlacePresentationFrame(interlaceField, Framebuffer);

	private void ComposeInterlacePresentationFrame(int interlaceField, int[] framebuffer)
	{
		ValidatePresentationFramebuffer(framebuffer);
		// The host-side phosphor presenter needs the undimmed latest rows from both
		// fields. Stable weave uses this image directly; CRT phosphor composes it later.
		_interlacePresentationFrame.AsSpan().CopyTo(framebuffer);
	}

	private void StabilizeInterlaceFrame()
	{
		if (!_machine.Bus.Display.InterlaceEnabled)
		{
			InvalidateInterlacePresentationHistory();
			return;
		}

		Framebuffer.AsSpan().CopyTo(_interlacePresentationFrame);
		var interlaceField = GetCompletedPresentationFrameNumber(_targetCycle) & 1;
		CompletedInterlaceField = interlaceField;
		if (!_interlacePresentationFrameValid)
		{
			SeedMissingInterlaceFieldRows(
				_interlacePresentationFrame,
				_machine.Bus.Display.Width,
				_machine.Bus.Display.Height,
				interlaceField);
			_interlacePresentationFrameValid = true;
		}

		ComposeInterlacePresentationFrame(interlaceField);
	}

	private void InvalidateInterlacePresentationHistory()
	{
		_interlacePresentationFrameValid = false;
	}

	internal static void SeedMissingInterlaceFieldRows(Span<int> interlaceFrame, int width, int height, int interlaceField)
	{
		System.Diagnostics.Debug.Assert(width > 0);
		System.Diagnostics.Debug.Assert((height & 1) == 0);
		System.Diagnostics.Debug.Assert((interlaceField & ~1) == 0);

		var missingField = interlaceField ^ 1;
		var rowPairs = height >> 1;
		for (var pair = 0; pair < rowPairs; pair++)
		{
			var activeRow = (pair << 1) + interlaceField;
			var missingRow = (pair << 1) + missingField;
			var sourceOffset = activeRow * width;
			var targetOffset = missingRow * width;
			interlaceFrame.Slice(sourceOffset, width).CopyTo(interlaceFrame.Slice(targetOffset, width));
		}
	}

	private void AdvanceInputPulse()
	{
		if (_firePulseFrames > 0)
		{
			_firePulseFrames--;
		}

		if (_mousePrimaryFirePulseFrames > 0)
		{
			_mousePrimaryFirePulseFrames--;
		}

		if (_mouseSecondFirePulseFrames > 0)
		{
			_mouseSecondFirePulseFrames--;
		}

		ApplyInputState();
	}

	private sealed class FrameExecutionBoundarySchedule : IAmigaExecutionBoundarySchedule
	{
		private readonly CopperScreenEmulator _owner;

		public FrameExecutionBoundarySchedule(CopperScreenEmulator owner)
		{
			_owner = owner;
		}

		public void BeginFrame()
			=> _owner.ApplyInputState();

		public void ApplyPendingDiskInsert()
			=> _owner.AdvancePendingDiskInsert();

		public void BeginExecution(long startCycle, long endCycle)
		{
			_ = startCycle;
			_owner.BeginFrameAudio(endCycle);
		}

		public long GetNextBoundaryCycle(long currentCycle, long targetCycle)
			=> _owner.GetFrameAudioBatchHorizon(currentCycle, targetCycle);

		public void AdvanceThrough(long previousCycle, long currentCycle)
			=> _owner.RenderFrameAudioUntil(previousCycle, currentCycle);

		public void CompleteExecution(long endCycle)
		{
			_ = endCycle;
			_owner.FinishFrameAudio();
		}

		public void CompleteFrame()
			=> _owner.AdvanceInputPulse();
	}

	private void ApplyInputState()
	{
		var pulsePrimaryFirePressed = _firePulseFrames > 0;
		_machine.Bus.GamePort0FirePressed = pulsePrimaryFirePressed;
		_machine.Bus.GamePort1FirePressed = pulsePrimaryFirePressed;
		_machine.Bus.GamePort0SecondFirePressed = false;
		_machine.Bus.GamePort1SecondFirePressed = false;
		_machine.Bus.SetGamePortJoystick(0, false, false, false, false);
		_machine.Bus.SetGamePortJoystick(1, false, false, false, false);

		for (var portIndex = 0; portIndex < _joystickActionsByPort.Length; portIndex++)
		{
			var primaryFirePressed = false;
			var secondFirePressed = false;
			if (_inputOptions.IsMousePort(portIndex))
			{
				primaryFirePressed |= _mousePrimaryFirePressed || _mousePrimaryFirePulseFrames > 0;
				secondFirePressed |= _mouseSecondFirePressed || _mouseSecondFirePulseFrames > 0;
			}

			var actions = _joystickActionsByPort[portIndex];
			primaryFirePressed |= (actions & CopperScreenJoystickActions.Fire) != 0;
			secondFirePressed |= (actions & CopperScreenJoystickActions.SecondFire) != 0;
			if (portIndex == 0)
			{
				_machine.Bus.GamePort0FirePressed |= primaryFirePressed;
				_machine.Bus.GamePort0SecondFirePressed |= secondFirePressed;
			}
			else
			{
				_machine.Bus.GamePort1FirePressed |= primaryFirePressed;
				_machine.Bus.GamePort1SecondFirePressed |= secondFirePressed;
			}

			_machine.Bus.SetGamePortJoystick(
				portIndex,
				(actions & CopperScreenJoystickActions.Up) != 0,
				(actions & CopperScreenJoystickActions.Down) != 0,
				(actions & CopperScreenJoystickActions.Left) != 0,
				(actions & CopperScreenJoystickActions.Right) != 0);
		}
	}

	private bool HandleBootResult(AmigaBootResult result)
	{
		if (HasWorkbenchHandoffDiagnostic(result.Diagnostics))
		{
			_workbenchHandoffPending = true;
			_copperBenchRequestPending = true;
			IsPaused = true;
			StatusText = "Workbench handoff: choose a CopperBench item";
			return false;
		}

		var fatalStatus = BuildFatalStatus(result.Diagnostics);
		StatusText = fatalStatus ?? RunningStatusText;
		if (fatalStatus == null)
		{
			return false;
		}

		var fatalDiagnostic = GetFirstFatalDiagnostic(result.Diagnostics);
		CaptureFatalStop(
			fatalDiagnostic.Code,
			fatalDiagnostic.Message,
			FormatDiagnostics(result.Diagnostics));
		RenderStatusFrame(StatusText);
		return true;
	}

	private static bool HasWorkbenchHandoffDiagnostic(IReadOnlyList<AmigaBootDiagnostic> diagnostics)
	{
		for (var i = 0; i < diagnostics.Count; i++)
		{
			if (diagnostics[i].Code == "AMIGA_BOOT_DOS_WORKBENCH_HANDOFF")
			{
				return true;
			}
		}

		return false;
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
		return code is "AMIGA_BOOT_UNSUPPORTED_OPCODE" or "AMIGA_BOOT_FAULT" or "AMIGA_BOOT_PROTECTED_DISK_UNSUPPORTED" or "AMIGA_BOOT_NULL_PC" or "AMIGA_BOOT_DOS_WORKBENCH_MEDIA_INCOMPLETE";
	}

	private static AmigaBootDiagnostic GetFirstFatalDiagnostic(IReadOnlyList<AmigaBootDiagnostic> diagnostics)
	{
		for (var i = 0; i < diagnostics.Count; i++)
		{
			if (IsFatalDiagnostic(diagnostics[i].Code))
			{
				return diagnostics[i];
			}
		}

		return new AmigaBootDiagnostic("AMIGA_BOOT_FAULT", "Unknown fatal boot diagnostic.");
	}

	private static string[] FormatDiagnostics(IReadOnlyList<AmigaBootDiagnostic> diagnostics)
	{
		var formatted = new string[diagnostics.Count];
		for (var i = 0; i < diagnostics.Count; i++)
		{
			formatted[i] = diagnostics[i].Code + ": " + diagnostics[i].Message;
		}

		return formatted;
	}

	private void CaptureFatalStop(string reasonCode, string message, string[] diagnostics)
	{
		if (_debugSnapshot != null)
		{
			return;
		}

		_frameAudio.AsSpan().Clear();
		InvalidateInterlacePresentationHistory();
		IsPaused = true;
		StatusText = reasonCode + ": " + message;
		_debugSnapshot = CreateDebugSnapshot(reasonCode, message, diagnostics);
		RenderStatusFrame(StatusText);
	}

	private void RenderNoDiskFrame()
		=> RenderNoDiskFrame(Framebuffer);

	private void RenderNoDiskFrame(int[] framebuffer)
	{
		ValidatePresentationFramebuffer(framebuffer);
		if (_profile.UsesKickstartRom)
		{
			InsertDiskScreenRenderer.RenderHostStatus(framebuffer, Width, Height, StatusText);
			return;
		}

		InsertDiskScreenRenderer.Render(framebuffer, Width, Height);
	}

	private void RenderStatusFrame(string status)
		=> RenderStatusFrame(status, Framebuffer);

	private void RenderStatusFrame(string status, int[] framebuffer)
	{
		ValidatePresentationFramebuffer(framebuffer);
		if (_profile.UsesKickstartRom)
		{
			InsertDiskScreenRenderer.RenderHostStatus(framebuffer, Width, Height, status);
			return;
		}

		InsertDiskScreenRenderer.RenderStatus(framebuffer, Width, Height, status);
	}

	private void ValidatePresentationFramebuffer(int[] framebuffer)
	{
		ArgumentNullException.ThrowIfNull(framebuffer);
		if (framebuffer.Length < Width * Height)
		{
			throw new ArgumentException("Presentation framebuffer is too small.", nameof(framebuffer));
		}
	}

	private CopperScreenDebugSnapshot CreateDebugSnapshot(string reasonCode, string message, string[] diagnostics)
	{
		var cpu = _machine.Cpu.State;
		var dataRegisters = new uint[8];
		var addressRegisters = new uint[8];
		Array.Copy(cpu.D, dataRegisters, dataRegisters.Length);
		Array.Copy(cpu.A, addressRegisters, addressRegisters.Length);
		var cpuSnapshot = new CopperScreenDebugCpuSnapshot(
			cpu.ProgramCounter,
			cpu.LastInstructionProgramCounter,
			cpu.LastOpcode,
			cpu.StatusRegister,
			cpu.UserStackPointer,
			cpu.SupervisorStackPointer,
			cpu.Cycles,
			cpu.Halted,
			cpu.Stopped,
			dataRegisters,
			addressRegisters,
			cpu.M68020StackModeEnabled,
			cpu.NativeCycles,
			cpu.VectorBaseRegister,
			cpu.SourceFunctionCode,
			cpu.DestinationFunctionCode,
			cpu.CacheControlRegister,
			cpu.CacheAddressRegister,
			cpu.MasterStackPointer,
			_machine.Options.CpuBackend is M68kBackendKind.AccurateM68040 or M68kBackendKind.JitM68040,
			cpu.M68040Fpu.Fpcr,
			cpu.M68040Fpu.Fpsr,
			cpu.M68040Fpu.Fpiar,
			cpu.M68040Mmu.TranslationControl,
			cpu.M68040Mmu.SupervisorRootPointer,
			cpu.M68040Mmu.UserRootPointer,
			cpu.M68040Mmu.InstructionTransparentTranslation0,
			cpu.M68040Mmu.InstructionTransparentTranslation1,
			cpu.M68040Mmu.DataTransparentTranslation0,
			cpu.M68040Mmu.DataTransparentTranslation1,
			cpu.M68040Mmu.Status);
		return new CopperScreenDebugSnapshot(
			DateTimeOffset.Now,
			reasonCode,
			message,
			ProfileName,
			CpuBackendName,
			DiskName,
			DiskPath,
			_machine.Bus.GetBeamPosition(_targetCycle).FrameNumber,
			cpuSnapshot,
			CaptureDriveStates(),
			diagnostics,
			M68kMiniDisassembler.Disassemble(cpu.ProgramCounter, 16, TryReadHostWord),
			CaptureStackWords(cpu.A[7], 16));
	}

	private string[] CaptureStackWords(uint stackPointer, int wordCount)
	{
		var lines = new string[wordCount];
		for (var i = 0; i < lines.Length; i++)
		{
			var address = (stackPointer + (uint)(i * 2)) & 0x00FF_FFFF;
			lines[i] = TryReadHostWord(address, out var value)
				? $"{address:X6}: {value:X4}"
				: $"{address:X6}: ????";
		}

		return lines;
	}

	private bool TryReadHostWord(uint address, out ushort value)
	{
		try
		{
			value = _machine.Bus.ReadHostWord(address & 0x00FF_FFFF);
			return true;
		}
		catch (Exception)
		{
			value = 0;
			return false;
		}
	}

}
