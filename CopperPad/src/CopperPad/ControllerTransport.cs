/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

namespace CopperPad;

/// <summary>
/// Describes the transport used by a controller when the provider can determine it.
/// </summary>
public enum ControllerTransport
{
	/// <summary>The controller transport is unknown or not reported by the provider.</summary>
	Unknown = 0,
	/// <summary>The controller is connected through USB.</summary>
	Usb,
	/// <summary>The controller is connected through Bluetooth.</summary>
	Bluetooth
}
