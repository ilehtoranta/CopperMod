/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System.Collections.Concurrent;

namespace CopperPad;

/// <summary>
/// Identifies the controller profiles exposed by a provider snapshot.
/// </summary>
public enum ControllerProfileKind
{
	/// <summary>A digital gamepad profile with face buttons, shoulders, menu buttons, and D-pad.</summary>
	StandardGamepad = 0,
	/// <summary>A standard gamepad plus analog sticks, triggers, and stick buttons.</summary>
	ExtendedGamepad,
	/// <summary>Raw diagnostic input is available for the controller.</summary>
	RawInput
}

/// <summary>
/// Describes the source that produced a controller mapping.
/// </summary>
public enum ControllerMappingSource
{
	/// <summary>No normalized mapping is active.</summary>
	None = 0,
	/// <summary>The mapping comes from a user profile.</summary>
	UserProfile,
	/// <summary>The mapping comes from the bundled SDL_GameControllerDB data.</summary>
	SdlGameControllerDb,
	/// <summary>The mapping comes from the native platform provider.</summary>
	ProviderNative,
	/// <summary>The mapping comes from a built-in fallback mapper.</summary>
	Fallback
}

/// <summary>
/// Identifies a normalized controller element.
/// </summary>
public enum ControllerElement
{
	/// <summary>The south face button, equivalent to Xbox A.</summary>
	South = 0,
	/// <summary>The east face button, equivalent to Xbox B.</summary>
	East,
	/// <summary>The west face button, equivalent to Xbox X.</summary>
	West,
	/// <summary>The north face button, equivalent to Xbox Y.</summary>
	North,
	/// <summary>Xbox-style alias for <see cref="South"/>.</summary>
	A = South,
	/// <summary>Xbox-style alias for <see cref="East"/>.</summary>
	B = East,
	/// <summary>Xbox-style alias for <see cref="West"/>.</summary>
	X = West,
	/// <summary>Xbox-style alias for <see cref="North"/>.</summary>
	Y = North,
	/// <summary>The left shoulder button.</summary>
	LeftShoulder,
	/// <summary>The right shoulder button.</summary>
	RightShoulder,
	/// <summary>The select, back, or view button.</summary>
	Select,
	/// <summary>The start or options button.</summary>
	Start,
	/// <summary>The system, guide, home, or menu button.</summary>
	Menu,
	/// <summary>The left stick button.</summary>
	LeftStickButton,
	/// <summary>The right stick button.</summary>
	RightStickButton,
	/// <summary>The D-pad up direction.</summary>
	DPadUp,
	/// <summary>The D-pad down direction.</summary>
	DPadDown,
	/// <summary>The D-pad left direction.</summary>
	DPadLeft,
	/// <summary>The D-pad right direction.</summary>
	DPadRight,
	/// <summary>The left stick horizontal axis, normalized to -1..1.</summary>
	LeftStickX,
	/// <summary>The left stick vertical axis, normalized to -1..1.</summary>
	LeftStickY,
	/// <summary>The right stick horizontal axis, normalized to -1..1.</summary>
	RightStickX,
	/// <summary>The right stick vertical axis, normalized to -1..1.</summary>
	RightStickY,
	/// <summary>The left trigger axis, normalized to 0..1.</summary>
	LeftTrigger,
	/// <summary>The right trigger axis, normalized to 0..1.</summary>
	RightTrigger
}

/// <summary>
/// Describes the storage shape of a controller element value.
/// </summary>
public enum ControllerElementValueKind
{
	/// <summary>A digital button value.</summary>
	Button = 0,
	/// <summary>An analog axis or trigger value.</summary>
	Axis
}

/// <summary>
/// Represents a normalized button, axis, or trigger value.
/// </summary>
/// <param name="Kind">The kind of value represented.</param>
/// <param name="IsPressed">Whether a button value is pressed.</param>
/// <param name="Value">The normalized numeric value.</param>
public readonly record struct ControllerElementValue(ControllerElementValueKind Kind, bool IsPressed, double Value)
{
	/// <summary>
	/// Creates a button value.
	/// </summary>
	/// <param name="isPressed">Whether the button is pressed.</param>
	/// <returns>A normalized button value.</returns>
	public static ControllerElementValue Button(bool isPressed)
		=> new(ControllerElementValueKind.Button, isPressed, isPressed ? 1 : 0);

	/// <summary>
	/// Creates an axis value clamped to -1..1.
	/// </summary>
	/// <param name="value">The normalized axis value.</param>
	/// <returns>A normalized axis value.</returns>
	public static ControllerElementValue Axis(double value)
		=> new(ControllerElementValueKind.Axis, false, Math.Clamp(value, -1, 1));

	/// <summary>
	/// Creates a trigger value clamped to 0..1.
	/// </summary>
	/// <param name="value">The normalized trigger value.</param>
	/// <returns>A normalized trigger value.</returns>
	public static ControllerElementValue Trigger(double value)
		=> new(ControllerElementValueKind.Axis, false, Math.Clamp(value, 0, 1));
}

/// <summary>
/// Describes a discovered controller and the profile support currently known for it.
/// </summary>
/// <param name="Id">Stable provider-specific controller identifier.</param>
/// <param name="DisplayName">Human-readable controller name.</param>
/// <param name="VendorId">USB/HID vendor identifier, or zero when unavailable.</param>
/// <param name="ProductId">USB/HID product identifier, or zero when unavailable.</param>
/// <param name="Transport">Best-known controller transport.</param>
/// <param name="IsConnected">Whether the controller is currently connected.</param>
/// <param name="SupportedProfiles">Profiles supported by the current mapping/provider.</param>
/// <param name="MappingSource">Source of the active mapping.</param>
/// <param name="MappingName">Human-readable name of the active mapping, when available.</param>
/// <param name="Diagnostic">Provider diagnostic message, when available.</param>
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

/// <summary>
/// Immutable snapshot of a controller at a point in time.
/// </summary>
/// <param name="ControllerId">Stable provider-specific controller identifier.</param>
/// <param name="Timestamp">Timestamp when the snapshot was produced.</param>
/// <param name="IsConnected">Whether the controller was connected when the snapshot was produced.</param>
/// <param name="DisplayName">Human-readable controller name.</param>
/// <param name="VendorId">USB/HID vendor identifier, or zero when unavailable.</param>
/// <param name="ProductId">USB/HID product identifier, or zero when unavailable.</param>
/// <param name="Transport">Best-known controller transport.</param>
/// <param name="Elements">Normalized element values keyed by <see cref="ControllerElement"/>.</param>
/// <param name="SupportedProfiles">Profiles supported by the snapshot.</param>
/// <param name="MappingSource">Source of the active mapping.</param>
/// <param name="MappingName">Human-readable name of the active mapping, when available.</param>
/// <param name="Diagnostic">Provider diagnostic message, when available.</param>
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
	/// <summary>
	/// Gets whether a button element is pressed.
	/// </summary>
	/// <param name="element">The button element to query.</param>
	/// <returns><see langword="true"/> when the element is pressed.</returns>
	public bool IsPressed(ControllerElement element)
		=> Elements.TryGetValue(ResolveAlias(element), out var value) && value.IsPressed;

	/// <summary>
	/// Gets a normalized axis or trigger value.
	/// </summary>
	/// <param name="element">The axis or trigger element to query.</param>
	/// <returns>The normalized value, or zero when the element is unavailable.</returns>
	public double GetAxis(ControllerElement element)
		=> Elements.TryGetValue(ResolveAlias(element), out var value) ? value.Value : 0;

	/// <summary>Gets whether the south face button is pressed.</summary>
	public bool South => IsPressed(ControllerElement.South);
	/// <summary>Gets whether the east face button is pressed.</summary>
	public bool East => IsPressed(ControllerElement.East);
	/// <summary>Gets whether the west face button is pressed.</summary>
	public bool West => IsPressed(ControllerElement.West);
	/// <summary>Gets whether the north face button is pressed.</summary>
	public bool North => IsPressed(ControllerElement.North);
	/// <summary>Gets whether the Xbox-style A button is pressed.</summary>
	public bool A => South;
	/// <summary>Gets whether the Xbox-style B button is pressed.</summary>
	public bool B => East;
	/// <summary>Gets whether the Xbox-style X button is pressed.</summary>
	public bool X => West;
	/// <summary>Gets whether the Xbox-style Y button is pressed.</summary>
	public bool Y => North;

	/// <summary>Gets a standard gamepad profile view when supported.</summary>
	public StandardGamepadProfile? StandardGamepad
		=> SupportedProfiles.Contains(ControllerProfileKind.StandardGamepad) ? new StandardGamepadProfile(this) : null;

	/// <summary>Gets an extended gamepad profile view when supported.</summary>
	public ExtendedGamepadProfile? ExtendedGamepad
		=> SupportedProfiles.Contains(ControllerProfileKind.ExtendedGamepad) ? new ExtendedGamepadProfile(this) : null;

	/// <summary>Gets a raw input diagnostics profile view when supported.</summary>
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

/// <summary>
/// Convenience view over standard digital gamepad controls.
/// </summary>
/// <param name="snapshot">Snapshot backing the profile view.</param>
public sealed class StandardGamepadProfile(CopperControllerSnapshot snapshot)
{
	/// <summary>Gets whether the south face button is pressed.</summary>
	public bool South => snapshot.IsPressed(ControllerElement.South);
	/// <summary>Gets whether the east face button is pressed.</summary>
	public bool East => snapshot.IsPressed(ControllerElement.East);
	/// <summary>Gets whether the west face button is pressed.</summary>
	public bool West => snapshot.IsPressed(ControllerElement.West);
	/// <summary>Gets whether the north face button is pressed.</summary>
	public bool North => snapshot.IsPressed(ControllerElement.North);
	/// <summary>Gets whether the Xbox-style A button is pressed.</summary>
	public bool A => South;
	/// <summary>Gets whether the Xbox-style B button is pressed.</summary>
	public bool B => East;
	/// <summary>Gets whether the Xbox-style X button is pressed.</summary>
	public bool X => West;
	/// <summary>Gets whether the Xbox-style Y button is pressed.</summary>
	public bool Y => North;
	/// <summary>Gets whether the left shoulder button is pressed.</summary>
	public bool LeftShoulder => snapshot.IsPressed(ControllerElement.LeftShoulder);
	/// <summary>Gets whether the right shoulder button is pressed.</summary>
	public bool RightShoulder => snapshot.IsPressed(ControllerElement.RightShoulder);
	/// <summary>Gets whether the select/back/view button is pressed.</summary>
	public bool Select => snapshot.IsPressed(ControllerElement.Select);
	/// <summary>Gets whether the start/options button is pressed.</summary>
	public bool Start => snapshot.IsPressed(ControllerElement.Start);
	/// <summary>Gets whether the menu/guide/home button is pressed.</summary>
	public bool Menu => snapshot.IsPressed(ControllerElement.Menu);
	/// <summary>Gets whether the D-pad up direction is pressed.</summary>
	public bool DPadUp => snapshot.IsPressed(ControllerElement.DPadUp);
	/// <summary>Gets whether the D-pad down direction is pressed.</summary>
	public bool DPadDown => snapshot.IsPressed(ControllerElement.DPadDown);
	/// <summary>Gets whether the D-pad left direction is pressed.</summary>
	public bool DPadLeft => snapshot.IsPressed(ControllerElement.DPadLeft);
	/// <summary>Gets whether the D-pad right direction is pressed.</summary>
	public bool DPadRight => snapshot.IsPressed(ControllerElement.DPadRight);
}

/// <summary>
/// Convenience view over standard and analog gamepad controls.
/// </summary>
/// <param name="snapshot">Snapshot backing the profile view.</param>
public sealed class ExtendedGamepadProfile(CopperControllerSnapshot snapshot)
{
	/// <summary>Gets the standard digital controls for this snapshot.</summary>
	public StandardGamepadProfile Standard { get; } = new(snapshot);
	/// <summary>Gets the left stick horizontal axis, normalized to -1..1.</summary>
	public double LeftStickX => snapshot.GetAxis(ControllerElement.LeftStickX);
	/// <summary>Gets the left stick vertical axis, normalized to -1..1.</summary>
	public double LeftStickY => snapshot.GetAxis(ControllerElement.LeftStickY);
	/// <summary>Gets the right stick horizontal axis, normalized to -1..1.</summary>
	public double RightStickX => snapshot.GetAxis(ControllerElement.RightStickX);
	/// <summary>Gets the right stick vertical axis, normalized to -1..1.</summary>
	public double RightStickY => snapshot.GetAxis(ControllerElement.RightStickY);
	/// <summary>Gets the left trigger value, normalized to 0..1.</summary>
	public double LeftTrigger => snapshot.GetAxis(ControllerElement.LeftTrigger);
	/// <summary>Gets the right trigger value, normalized to 0..1.</summary>
	public double RightTrigger => snapshot.GetAxis(ControllerElement.RightTrigger);
	/// <summary>Gets whether the left stick button is pressed.</summary>
	public bool LeftStickButton => snapshot.IsPressed(ControllerElement.LeftStickButton);
	/// <summary>Gets whether the right stick button is pressed.</summary>
	public bool RightStickButton => snapshot.IsPressed(ControllerElement.RightStickButton);
}

/// <summary>
/// Convenience view over raw-input mapping diagnostics.
/// </summary>
/// <param name="snapshot">Snapshot backing the profile view.</param>
public sealed class RawInputProfile(CopperControllerSnapshot snapshot)
{
	/// <summary>Gets the provider diagnostic message, when available.</summary>
	public string? Diagnostic => snapshot.Diagnostic;
	/// <summary>Gets the source of the active mapping.</summary>
	public ControllerMappingSource MappingSource => snapshot.MappingSource;
	/// <summary>Gets the name of the active mapping, when available.</summary>
	public string? MappingName => snapshot.MappingName;
}

/// <summary>
/// Event data for a changed controller element.
/// </summary>
/// <param name="controller">Controller that changed.</param>
/// <param name="element">Element that changed.</param>
/// <param name="previousValue">Previous element value.</param>
/// <param name="currentValue">Current element value.</param>
public sealed class CopperElementChangedEventArgs(
	CopperController controller,
	ControllerElement element,
	ControllerElementValue previousValue,
	ControllerElementValue currentValue) : EventArgs
{
	/// <summary>Gets the controller that changed.</summary>
	public CopperController Controller { get; } = controller;
	/// <summary>Gets the element that changed.</summary>
	public ControllerElement Element { get; } = element;
	/// <summary>Gets the previous element value.</summary>
	public ControllerElementValue PreviousValue { get; } = previousValue;
	/// <summary>Gets the current element value.</summary>
	public ControllerElementValue CurrentValue { get; } = currentValue;
}

/// <summary>
/// Event data for changed controller profile support.
/// </summary>
/// <param name="controller">Controller whose profiles changed.</param>
/// <param name="previousProfiles">Previously supported profiles.</param>
/// <param name="currentProfiles">Currently supported profiles.</param>
public sealed class CopperProfileChangedEventArgs(
	CopperController controller,
	IReadOnlySet<ControllerProfileKind> previousProfiles,
	IReadOnlySet<ControllerProfileKind> currentProfiles) : EventArgs
{
	/// <summary>Gets the controller whose profiles changed.</summary>
	public CopperController Controller { get; } = controller;
	/// <summary>Gets the previously supported profiles.</summary>
	public IReadOnlySet<ControllerProfileKind> PreviousProfiles { get; } = previousProfiles;
	/// <summary>Gets the currently supported profiles.</summary>
	public IReadOnlySet<ControllerProfileKind> CurrentProfiles { get; } = currentProfiles;
}

/// <summary>
/// Event data for controller list changes.
/// </summary>
/// <param name="controllers">Current controller list.</param>
public sealed class CopperControllersChangedEventArgs(IReadOnlyList<CopperControllerInfo> controllers) : EventArgs
{
	/// <summary>Gets the current controller list.</summary>
	public IReadOnlyList<CopperControllerInfo> Controllers { get; } = controllers;
}

/// <summary>
/// Event data for a new controller snapshot.
/// </summary>
/// <param name="snapshot">The new controller snapshot.</param>
public sealed class CopperControllerSnapshotChangedEventArgs(CopperControllerSnapshot snapshot) : EventArgs
{
	/// <summary>Gets the new controller snapshot.</summary>
	public CopperControllerSnapshot Snapshot { get; } = snapshot;
}

/// <summary>
/// Platform provider interface used by <see cref="CopperControllerHost"/>.
/// </summary>
public interface IControllerProvider : IDisposable
{
	/// <summary>Raised when the provider's controller list changes.</summary>
	event EventHandler<CopperControllersChangedEventArgs>? ControllersChanged;
	/// <summary>Raised when the provider publishes a controller snapshot.</summary>
	event EventHandler<CopperControllerSnapshotChangedEventArgs>? SnapshotChanged;

	/// <summary>Starts controller discovery and input reporting.</summary>
	void Start();
	/// <summary>Stops controller discovery and input reporting.</summary>
	void Stop();
	/// <summary>Gets the controllers currently known to the provider.</summary>
	/// <returns>The current controller list.</returns>
	IReadOnlyList<CopperControllerInfo> GetControllers();
	/// <summary>Attempts to get the latest snapshot for a controller.</summary>
	/// <param name="controllerId">The provider-specific controller identifier.</param>
	/// <param name="snapshot">The latest snapshot when available.</param>
	/// <returns><see langword="true"/> when a snapshot is available.</returns>
	bool TryGetSnapshot(string controllerId, out CopperControllerSnapshot snapshot);
}

/// <summary>
/// Represents one discovered controller and publishes profile and element changes.
/// </summary>
public sealed class CopperController
{
	private CopperControllerSnapshot _snapshot;

	internal CopperController(CopperControllerInfo info)
	{
		Info = info;
		_snapshot = CreateDisconnectedSnapshot(info);
	}

	/// <summary>Gets the latest controller metadata.</summary>
	public CopperControllerInfo Info { get; private set; }

	/// <summary>Raised when the controller's supported profiles change.</summary>
	public event EventHandler<CopperProfileChangedEventArgs>? ProfileChanged;
	/// <summary>Raised when a normalized controller element changes.</summary>
	public event EventHandler<CopperElementChangedEventArgs>? ElementChanged;

	/// <summary>
	/// Gets the latest snapshot for the controller.
	/// </summary>
	/// <returns>The latest snapshot known to the host.</returns>
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

/// <summary>
/// Coordinates a controller provider and exposes controller objects with snapshots and change events.
/// </summary>
public sealed class CopperControllerHost : IDisposable
{
	private readonly IControllerProvider _provider;
	private readonly ConcurrentDictionary<string, CopperController> _controllers = new(StringComparer.Ordinal);
	private bool _disposed;

	/// <summary>
	/// Creates a controller host for a platform provider.
	/// </summary>
	/// <param name="provider">The provider that discovers controllers and publishes snapshots.</param>
	public CopperControllerHost(IControllerProvider provider)
	{
		_provider = provider ?? throw new ArgumentNullException(nameof(provider));
		_provider.ControllersChanged += OnControllersChanged;
		_provider.SnapshotChanged += OnSnapshotChanged;
	}

	/// <summary>Raised when the available controller list changes.</summary>
	public event EventHandler<CopperControllersChangedEventArgs>? ControllersChanged;

	/// <summary>Starts the underlying provider.</summary>
	public void Start()
	{
		ThrowIfDisposed();
		_provider.Start();
		RefreshControllers(_provider.GetControllers());
	}

	/// <summary>Stops the underlying provider.</summary>
	public void Stop()
		=> _provider.Stop();

	/// <summary>
	/// Gets the controllers currently known to the host.
	/// </summary>
	/// <returns>Controllers ordered by display name.</returns>
	public IReadOnlyList<CopperController> GetControllers()
		=> _controllers.Values.OrderBy(controller => controller.Info.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();

	/// <summary>
	/// Attempts to get a controller by provider-specific identifier.
	/// </summary>
	/// <param name="controllerId">The provider-specific controller identifier.</param>
	/// <param name="controller">The controller when found.</param>
	/// <returns><see langword="true"/> when the controller is known.</returns>
	public bool TryGetController(string controllerId, out CopperController controller)
		=> _controllers.TryGetValue(controllerId, out controller!);

	/// <summary>Releases provider subscriptions and disposes the provider.</summary>
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
