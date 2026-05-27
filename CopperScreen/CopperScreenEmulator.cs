using System.Runtime.InteropServices;
using CopperMod.Amiga;

namespace CopperScreen;

internal sealed class CopperScreenEmulator
{
	private readonly AmigaMachine _machine;
	private readonly AmigaBootController _boot;
	private bool _bootAttempted;

	private CopperScreenEmulator(string? diskPath)
	{
		Width = AmigaConstants.PalLowResWidth;
		Height = AmigaConstants.PalLowResHeight;
		Framebuffer = new int[Width * Height];
		_machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
		_boot = new AmigaBootController(_machine);
		DiskPath = diskPath;
		StatusText = diskPath == null ? "insert disk image" : Path.GetFileName(diskPath);
	}

	public int Width { get; }

	public int Height { get; }

	public int[] Framebuffer { get; }

	public string? DiskPath { get; }

	public string StatusText { get; private set; }

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

	public void RenderNextFrame()
	{
		if (DiskPath == null)
		{
			InsertDiskScreenRenderer.Render(Framebuffer, Width, Height);
			StatusText = "insert disk image";
			return;
		}

		if (!_bootAttempted)
		{
			_bootAttempted = true;
			var disk = AmigaDiskImage.Load(DiskPath);
			var result = _boot.BootFromDisk(disk, maxInstructions: 250_000, runMode: AmigaBootRunMode.ContinueAfterBootDiskRead);
			if (HandleBootResult(result))
			{
				return;
			}
		}
		else
		{
			var result = _boot.ContinueExecution(maxInstructions: 25_000);
			if (HandleBootResult(result))
			{
				return;
			}
		}

		_machine.Bus.Display.RenderFrame(MemoryMarshal.Cast<int, uint>(Framebuffer.AsSpan()));
		if (IsBlank(Framebuffer))
		{
			InsertDiskScreenRenderer.RenderStatus(Framebuffer, Width, Height, StatusText);
		}
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
