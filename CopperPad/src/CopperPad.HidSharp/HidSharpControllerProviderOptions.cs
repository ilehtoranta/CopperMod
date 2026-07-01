/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

namespace CopperPad;

/// <summary>
/// Configures the HidSharp controller provider and diagnostics host.
/// </summary>
public sealed record HidSharpControllerProviderOptions
{
	/// <summary>Gets the user profile set used before SDL and fallback mappings.</summary>
	public ControllerProfileSet Profiles { get; init; } = ControllerProfileSet.Empty;
	/// <summary>Gets the timeout used when reading HID input reports.</summary>
	public TimeSpan ReadTimeout { get; init; } = TimeSpan.FromMilliseconds(250);
	/// <summary>Gets whether discovery should ignore devices without HID gamepad, joystick, or multiaxis-controller usage.</summary>
	public bool RequireGameControllerUsage { get; init; }
}
