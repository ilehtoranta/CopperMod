/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CopperPad;

public sealed record ControllerProfileSet
{
	public static ControllerProfileSet Empty { get; } = new();

	public int SchemaVersion { get; init; } = 2;
	public IReadOnlyList<ControllerProfile> Profiles { get; init; } = Array.Empty<ControllerProfile>();

	public ControllerProfile? FindMatch(CopperControllerInfo info)
	{
		foreach (var profile in Profiles)
		{
			if (profile.Matches(info))
			{
				return profile;
			}
		}

		return null;
	}
}

public sealed record ControllerProfile
{
	public string Name { get; init; } = string.Empty;
	public string? Notes { get; init; }
	public int? VendorId { get; init; }
	public int? ProductId { get; init; }
	public string? ProductNameContains { get; init; }
	public string? DeviceId { get; init; }
	public DateTimeOffset? CreatedAt { get; init; }
	public DateTimeOffset? UpdatedAt { get; init; }
	public IReadOnlyList<ControllerBinding> Bindings { get; init; } = Array.Empty<ControllerBinding>();

	public bool Matches(CopperControllerInfo info)
	{
		if (VendorId.HasValue && VendorId.Value != info.VendorId)
		{
			return false;
		}

		if (ProductId.HasValue && ProductId.Value != info.ProductId)
		{
			return false;
		}

		if (!string.IsNullOrWhiteSpace(DeviceId) && !string.Equals(DeviceId, info.Id, StringComparison.Ordinal))
		{
			return false;
		}

		return string.IsNullOrWhiteSpace(ProductNameContains) ||
			info.DisplayName.Contains(ProductNameContains, StringComparison.OrdinalIgnoreCase);
	}
}

public sealed record ControllerBinding
{
	public ControllerElement Target { get; init; }
	public ControllerBindingSource Source { get; init; } = new();
	public AxisCalibration? Axis { get; init; }
}

public enum ControllerBindingSourceKind
{
	ReportBit,
	ReportByte,
	ReportInt16LittleEndian,
	Hat
}

public sealed record ControllerBindingSource
{
	public ControllerBindingSourceKind Kind { get; init; }
	public int Offset { get; init; }
	public int Bit { get; init; }
	public int? HatValue { get; init; }
}

public sealed record AxisCalibration
{
	public int Minimum { get; init; } = 0;
	public int Maximum { get; init; } = 255;
	public int? Center { get; init; }
	public bool Invert { get; init; }
	public double Deadzone { get; init; } = 0.1;
	public double Saturation { get; init; } = 1.0;
}

public interface IControllerProfileStore
{
	ValueTask<ControllerProfileSet> LoadAsync(CancellationToken cancellationToken = default);
	ValueTask SaveAsync(ControllerProfileSet profiles, CancellationToken cancellationToken = default);
}

public static class JsonControllerProfileSerializer
{
	private static readonly JsonSerializerOptions Options = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
	};

	public static string Serialize(ControllerProfileSet profiles)
		=> JsonSerializer.Serialize(Upgrade(profiles), Options);

	public static ControllerProfileSet Deserialize(string json)
		=> Upgrade(JsonSerializer.Deserialize<ControllerProfileSet>(json, Options) ?? ControllerProfileSet.Empty);

	public static async ValueTask<ControllerProfileSet> LoadAsync(Stream stream, CancellationToken cancellationToken = default)
		=> Upgrade(await JsonSerializer.DeserializeAsync<ControllerProfileSet>(stream, Options, cancellationToken).ConfigureAwait(false) ??
			ControllerProfileSet.Empty);

	public static async ValueTask SaveAsync(Stream stream, ControllerProfileSet profiles, CancellationToken cancellationToken = default)
		=> await JsonSerializer.SerializeAsync(stream, Upgrade(profiles), Options, cancellationToken).ConfigureAwait(false);

	private static ControllerProfileSet Upgrade(ControllerProfileSet profiles)
		=> profiles.SchemaVersion >= 2 ? profiles : profiles with { SchemaVersion = 2 };
}
