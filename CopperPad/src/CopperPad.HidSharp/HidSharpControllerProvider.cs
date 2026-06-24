/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System.Collections.Concurrent;

namespace CopperPad;

public sealed class HidSharpControllerProvider : IControllerProvider
{
	private static readonly IReadOnlySet<ControllerProfileKind> MappedProfiles = new HashSet<ControllerProfileKind>
	{
		ControllerProfileKind.StandardGamepad,
		ControllerProfileKind.ExtendedGamepad,
		ControllerProfileKind.RawInput
	};

	private readonly ControllerHost _host;
	private readonly ConcurrentDictionary<string, CopperControllerSnapshot> _snapshots = new(StringComparer.Ordinal);
	private bool _disposed;

	public HidSharpControllerProvider(ControllerHostOptions? options = null)
	{
		_host = new ControllerHost(options);
		_host.ControllersChanged += OnControllersChanged;
		_host.ControllerStateChanged += OnControllerStateChanged;
	}

	public event EventHandler<CopperControllersChangedEventArgs>? ControllersChanged;
	public event EventHandler<CopperControllerSnapshotChangedEventArgs>? SnapshotChanged;

	public void Start()
	{
		ThrowIfDisposed();
		_host.Start();
		PublishControllers();
	}

	public void Stop()
	{
		_host.Stop();
		_snapshots.Clear();
		PublishControllers();
	}

	public IReadOnlyList<CopperControllerInfo> GetControllers()
		=> _host.GetControllers().Select(ToCopperInfo).ToArray();

	public bool TryGetSnapshot(string controllerId, out CopperControllerSnapshot snapshot)
		=> _snapshots.TryGetValue(controllerId, out snapshot!);

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_host.ControllersChanged -= OnControllersChanged;
		_host.ControllerStateChanged -= OnControllerStateChanged;
		_host.Dispose();
	}

	private void OnControllersChanged(object? sender, ControllersChangedEventArgs args)
	{
		var present = args.Controllers.Select(controller => controller.Id).ToHashSet(StringComparer.Ordinal);
		foreach (var stale in _snapshots.Keys.Where(id => !present.Contains(id)).ToArray())
		{
			_snapshots.TryRemove(stale, out _);
		}

		ControllersChanged?.Invoke(this, new CopperControllersChangedEventArgs(args.Controllers.Select(ToCopperInfo).ToArray()));
	}

	private void OnControllerStateChanged(object? sender, ControllerStateChangedEventArgs args)
	{
		var snapshot = ToSnapshot(args.State);
		_snapshots[snapshot.ControllerId] = snapshot;
		SnapshotChanged?.Invoke(this, new CopperControllerSnapshotChangedEventArgs(snapshot));
	}

	private void PublishControllers()
		=> ControllersChanged?.Invoke(this, new CopperControllersChangedEventArgs(GetControllers()));

	private static CopperControllerInfo ToCopperInfo(ControllerInfo info)
		=> new(
			info.Id,
			info.ProductName,
			info.VendorId,
			info.ProductId,
			info.Transport,
			info.IsConnected,
			MappedProfiles,
			info.IsConnected ? ControllerMappingSource.Fallback : ControllerMappingSource.None,
			null,
			info.Diagnostic);

	private static CopperControllerSnapshot ToSnapshot(VirtualXboxControllerState state)
	{
		var elements = new Dictionary<ControllerElement, ControllerElementValue>
		{
			[ControllerElement.South] = ControllerElementValue.Button(state.A),
			[ControllerElement.East] = ControllerElementValue.Button(state.B),
			[ControllerElement.West] = ControllerElementValue.Button(state.X),
			[ControllerElement.North] = ControllerElementValue.Button(state.Y),
			[ControllerElement.LeftShoulder] = ControllerElementValue.Button(state.LeftShoulder),
			[ControllerElement.RightShoulder] = ControllerElementValue.Button(state.RightShoulder),
			[ControllerElement.Select] = ControllerElementValue.Button(state.Back),
			[ControllerElement.Start] = ControllerElementValue.Button(state.Start),
			[ControllerElement.Menu] = ControllerElementValue.Button(state.Guide),
			[ControllerElement.LeftStickButton] = ControllerElementValue.Button(state.LeftStick),
			[ControllerElement.RightStickButton] = ControllerElementValue.Button(state.RightStick),
			[ControllerElement.DPadUp] = ControllerElementValue.Button(state.DPadUp),
			[ControllerElement.DPadDown] = ControllerElementValue.Button(state.DPadDown),
			[ControllerElement.DPadLeft] = ControllerElementValue.Button(state.DPadLeft),
			[ControllerElement.DPadRight] = ControllerElementValue.Button(state.DPadRight),
			[ControllerElement.LeftStickX] = ControllerElementValue.Axis(state.LeftX),
			[ControllerElement.LeftStickY] = ControllerElementValue.Axis(state.LeftY),
			[ControllerElement.RightStickX] = ControllerElementValue.Axis(state.RightX),
			[ControllerElement.RightStickY] = ControllerElementValue.Axis(state.RightY),
			[ControllerElement.LeftTrigger] = ControllerElementValue.Trigger(state.LeftTrigger),
			[ControllerElement.RightTrigger] = ControllerElementValue.Trigger(state.RightTrigger)
		};

		return new CopperControllerSnapshot(
			state.ControllerId,
			state.Timestamp,
			state.IsConnected,
			state.ProductName,
			state.VendorId,
			state.ProductId,
			state.Transport,
			elements,
			MappedProfiles,
			state.IsConnected ? ControllerMappingSource.Fallback : ControllerMappingSource.None,
			null,
			state.Diagnostic);
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, nameof(HidSharpControllerProvider));
	}
}
