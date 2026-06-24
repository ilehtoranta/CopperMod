/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System.Collections.Concurrent;

namespace CopperPad;

public enum ControllerProfileKind
{
	StandardGamepad = 0,
	ExtendedGamepad,
	RawInput
}

public enum ControllerMappingSource
{
	None = 0,
	UserProfile,
	SdlGameControllerDb,
	ProviderNative,
	Fallback
}

public enum ControllerElement
{
	South = 0,
	East,
	West,
	North,
	A = South,
	B = East,
	X = West,
	Y = North,
	LeftShoulder,
	RightShoulder,
	Select,
	Start,
	Menu,
	LeftStickButton,
	RightStickButton,
	DPadUp,
	DPadDown,
	DPadLeft,
	DPadRight,
	LeftStickX,
	LeftStickY,
	RightStickX,
	RightStickY,
	LeftTrigger,
	RightTrigger
}

public enum ControllerElementValueKind
{
	Button = 0,
	Axis
}

public readonly record struct ControllerElementValue(ControllerElementValueKind Kind, bool IsPressed, double Value)
{
	public static ControllerElementValue Button(bool isPressed)
		=> new(ControllerElementValueKind.Button, isPressed, isPressed ? 1 : 0);

	public static ControllerElementValue Axis(double value)
		=> new(ControllerElementValueKind.Axis, false, Math.Clamp(value, -1, 1));

	public static ControllerElementValue Trigger(double value)
		=> new(ControllerElementValueKind.Axis, false, Math.Clamp(value, 0, 1));
}

public sealed record CopperControllerInfo(
	string Id,
	string DisplayName,
	int VendorId,
	int ProductId,
	ControllerTransport Transport,
	bool IsConnected,
	IReadOnlySet<ControllerProfileKind> SupportedProfiles,
	ControllerMappingSource MappingSource,
	string? MappingName,
	string? Diagnostic);

public sealed record CopperControllerSnapshot(
	string ControllerId,
	DateTimeOffset Timestamp,
	bool IsConnected,
	string DisplayName,
	int VendorId,
	int ProductId,
	ControllerTransport Transport,
	IReadOnlyDictionary<ControllerElement, ControllerElementValue> Elements,
	IReadOnlySet<ControllerProfileKind> SupportedProfiles,
	ControllerMappingSource MappingSource,
	string? MappingName,
	string? Diagnostic)
{
	public bool IsPressed(ControllerElement element)
		=> Elements.TryGetValue(ResolveAlias(element), out var value) && value.IsPressed;

	public double GetAxis(ControllerElement element)
		=> Elements.TryGetValue(ResolveAlias(element), out var value) ? value.Value : 0;

	public bool South => IsPressed(ControllerElement.South);
	public bool East => IsPressed(ControllerElement.East);
	public bool West => IsPressed(ControllerElement.West);
	public bool North => IsPressed(ControllerElement.North);
	public bool A => South;
	public bool B => East;
	public bool X => West;
	public bool Y => North;

	public StandardGamepadProfile? StandardGamepad
		=> SupportedProfiles.Contains(ControllerProfileKind.StandardGamepad) ? new StandardGamepadProfile(this) : null;

	public ExtendedGamepadProfile? ExtendedGamepad
		=> SupportedProfiles.Contains(ControllerProfileKind.ExtendedGamepad) ? new ExtendedGamepadProfile(this) : null;

	public RawInputProfile? RawInput
		=> SupportedProfiles.Contains(ControllerProfileKind.RawInput) ? new RawInputProfile(this) : null;

	internal static ControllerElement ResolveAlias(ControllerElement element)
		=> element switch
		{
			ControllerElement.A => ControllerElement.South,
			ControllerElement.B => ControllerElement.East,
			ControllerElement.X => ControllerElement.West,
			ControllerElement.Y => ControllerElement.North,
			_ => element
		};
}

public sealed class StandardGamepadProfile(CopperControllerSnapshot snapshot)
{
	public bool South => snapshot.IsPressed(ControllerElement.South);
	public bool East => snapshot.IsPressed(ControllerElement.East);
	public bool West => snapshot.IsPressed(ControllerElement.West);
	public bool North => snapshot.IsPressed(ControllerElement.North);
	public bool A => South;
	public bool B => East;
	public bool X => West;
	public bool Y => North;
	public bool LeftShoulder => snapshot.IsPressed(ControllerElement.LeftShoulder);
	public bool RightShoulder => snapshot.IsPressed(ControllerElement.RightShoulder);
	public bool Select => snapshot.IsPressed(ControllerElement.Select);
	public bool Start => snapshot.IsPressed(ControllerElement.Start);
	public bool Menu => snapshot.IsPressed(ControllerElement.Menu);
	public bool DPadUp => snapshot.IsPressed(ControllerElement.DPadUp);
	public bool DPadDown => snapshot.IsPressed(ControllerElement.DPadDown);
	public bool DPadLeft => snapshot.IsPressed(ControllerElement.DPadLeft);
	public bool DPadRight => snapshot.IsPressed(ControllerElement.DPadRight);
}

public sealed class ExtendedGamepadProfile(CopperControllerSnapshot snapshot)
{
	public StandardGamepadProfile Standard { get; } = new(snapshot);
	public double LeftStickX => snapshot.GetAxis(ControllerElement.LeftStickX);
	public double LeftStickY => snapshot.GetAxis(ControllerElement.LeftStickY);
	public double RightStickX => snapshot.GetAxis(ControllerElement.RightStickX);
	public double RightStickY => snapshot.GetAxis(ControllerElement.RightStickY);
	public double LeftTrigger => snapshot.GetAxis(ControllerElement.LeftTrigger);
	public double RightTrigger => snapshot.GetAxis(ControllerElement.RightTrigger);
	public bool LeftStickButton => snapshot.IsPressed(ControllerElement.LeftStickButton);
	public bool RightStickButton => snapshot.IsPressed(ControllerElement.RightStickButton);
}

public sealed class RawInputProfile(CopperControllerSnapshot snapshot)
{
	public string? Diagnostic => snapshot.Diagnostic;
	public ControllerMappingSource MappingSource => snapshot.MappingSource;
	public string? MappingName => snapshot.MappingName;
}

public sealed class CopperElementChangedEventArgs(
	CopperController controller,
	ControllerElement element,
	ControllerElementValue previousValue,
	ControllerElementValue currentValue) : EventArgs
{
	public CopperController Controller { get; } = controller;
	public ControllerElement Element { get; } = element;
	public ControllerElementValue PreviousValue { get; } = previousValue;
	public ControllerElementValue CurrentValue { get; } = currentValue;
}

public sealed class CopperProfileChangedEventArgs(
	CopperController controller,
	IReadOnlySet<ControllerProfileKind> previousProfiles,
	IReadOnlySet<ControllerProfileKind> currentProfiles) : EventArgs
{
	public CopperController Controller { get; } = controller;
	public IReadOnlySet<ControllerProfileKind> PreviousProfiles { get; } = previousProfiles;
	public IReadOnlySet<ControllerProfileKind> CurrentProfiles { get; } = currentProfiles;
}

public sealed class CopperControllersChangedEventArgs(IReadOnlyList<CopperControllerInfo> controllers) : EventArgs
{
	public IReadOnlyList<CopperControllerInfo> Controllers { get; } = controllers;
}

public sealed class CopperControllerSnapshotChangedEventArgs(CopperControllerSnapshot snapshot) : EventArgs
{
	public CopperControllerSnapshot Snapshot { get; } = snapshot;
}

public interface IControllerProvider : IDisposable
{
	event EventHandler<CopperControllersChangedEventArgs>? ControllersChanged;
	event EventHandler<CopperControllerSnapshotChangedEventArgs>? SnapshotChanged;

	void Start();
	void Stop();
	IReadOnlyList<CopperControllerInfo> GetControllers();
	bool TryGetSnapshot(string controllerId, out CopperControllerSnapshot snapshot);
}

public sealed class CopperController
{
	private CopperControllerSnapshot _snapshot;

	internal CopperController(CopperControllerInfo info)
	{
		Info = info;
		_snapshot = CreateDisconnectedSnapshot(info);
	}

	public CopperControllerInfo Info { get; private set; }

	public event EventHandler<CopperProfileChangedEventArgs>? ProfileChanged;
	public event EventHandler<CopperElementChangedEventArgs>? ElementChanged;

	public CopperControllerSnapshot GetSnapshot()
		=> _snapshot;

	internal void UpdateInfo(CopperControllerInfo info)
	{
		Info = info;
		if (!_snapshot.IsConnected)
		{
			_snapshot = CreateDisconnectedSnapshot(info);
		}
	}

	internal void UpdateSnapshot(CopperControllerSnapshot snapshot)
	{
		var previous = _snapshot;
		_snapshot = snapshot;

		if (!previous.SupportedProfiles.SetEquals(snapshot.SupportedProfiles))
		{
			ProfileChanged?.Invoke(this, new CopperProfileChangedEventArgs(this, previous.SupportedProfiles, snapshot.SupportedProfiles));
		}

		foreach (var pair in snapshot.Elements)
		{
			previous.Elements.TryGetValue(pair.Key, out var oldValue);
			if (!oldValue.Equals(pair.Value))
			{
				ElementChanged?.Invoke(this, new CopperElementChangedEventArgs(this, pair.Key, oldValue, pair.Value));
			}
		}
	}

	private static CopperControllerSnapshot CreateDisconnectedSnapshot(CopperControllerInfo info)
		=> new(
			info.Id,
			DateTimeOffset.UtcNow,
			false,
			info.DisplayName,
			info.VendorId,
			info.ProductId,
			info.Transport,
			new Dictionary<ControllerElement, ControllerElementValue>(),
			info.SupportedProfiles,
			info.MappingSource,
			info.MappingName,
			info.Diagnostic);
}

public sealed class CopperControllerHost : IDisposable
{
	private readonly IControllerProvider _provider;
	private readonly ConcurrentDictionary<string, CopperController> _controllers = new(StringComparer.Ordinal);
	private bool _disposed;

	public CopperControllerHost(IControllerProvider provider)
	{
		_provider = provider ?? throw new ArgumentNullException(nameof(provider));
		_provider.ControllersChanged += OnControllersChanged;
		_provider.SnapshotChanged += OnSnapshotChanged;
	}

	public event EventHandler<CopperControllersChangedEventArgs>? ControllersChanged;

	public void Start()
	{
		ThrowIfDisposed();
		_provider.Start();
		RefreshControllers(_provider.GetControllers());
	}

	public void Stop()
		=> _provider.Stop();

	public IReadOnlyList<CopperController> GetControllers()
		=> _controllers.Values.OrderBy(controller => controller.Info.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();

	public bool TryGetController(string controllerId, out CopperController controller)
		=> _controllers.TryGetValue(controllerId, out controller!);

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_provider.ControllersChanged -= OnControllersChanged;
		_provider.SnapshotChanged -= OnSnapshotChanged;
		_provider.Dispose();
	}

	private void OnControllersChanged(object? sender, CopperControllersChangedEventArgs args)
	{
		RefreshControllers(args.Controllers);
		ControllersChanged?.Invoke(this, args);
	}

	private void OnSnapshotChanged(object? sender, CopperControllerSnapshotChangedEventArgs args)
	{
		var info = new CopperControllerInfo(
			args.Snapshot.ControllerId,
			args.Snapshot.DisplayName,
			args.Snapshot.VendorId,
			args.Snapshot.ProductId,
			args.Snapshot.Transport,
			args.Snapshot.IsConnected,
			args.Snapshot.SupportedProfiles,
			args.Snapshot.MappingSource,
			args.Snapshot.MappingName,
			args.Snapshot.Diagnostic);
		var controller = _controllers.GetOrAdd(info.Id, _ => new CopperController(info));
		controller.UpdateSnapshot(args.Snapshot);
		controller.UpdateInfo(info);
	}

	private void RefreshControllers(IReadOnlyList<CopperControllerInfo> infos)
	{
		var present = infos.Select(info => info.Id).ToHashSet(StringComparer.Ordinal);
		foreach (var stale in _controllers.Keys.Where(id => !present.Contains(id)).ToArray())
		{
			_controllers.TryRemove(stale, out _);
		}

		foreach (var info in infos)
		{
			_controllers.AddOrUpdate(
				info.Id,
				_ => new CopperController(info),
				(_, existing) =>
				{
					existing.UpdateInfo(info);
					return existing;
				});
		}
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, nameof(CopperControllerHost));
	}
}

internal static class ControllerProfileSetExtensions
{
	public static bool SetEquals(this IReadOnlySet<ControllerProfileKind> left, IReadOnlySet<ControllerProfileKind> right)
		=> left.Count == right.Count && left.All(right.Contains);
}
