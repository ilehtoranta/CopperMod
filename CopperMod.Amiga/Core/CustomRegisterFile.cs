/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CopperMod.Amiga.Core;

internal enum CustomRegisterObservationWidth : byte
{
    Byte,
    Word
}

internal enum CustomRegisterByteLane : byte
{
    None,
    High,
    Low
}

internal enum CustomRegisterWriteCause : byte
{
    Explicit,
    UnreadableReadSideEffect
}

internal enum CustomRegisterStorageMode : byte
{
    None,
    DeviceOwned,
    DevicePublished,
    RegisterFile
}

[Flags]
internal enum CustomRegisterWriteTarget : byte
{
    None = 0,
    Agnus = 1 << 0,
    Paula = 1 << 1,
    Display = 1 << 2,
    Blitter = 1 << 3,
    Disk = 1 << 4
}

internal enum CustomRegisterImplementationStatus : byte
{
    Absent,
    Implemented,
    Partial,
    Unimplemented
}

internal enum CustomRegisterReadHandler : byte
{
    None,
    ChipDataBusLatch,
    Agnus,
    Display,
    Dmaconr,
    BeamPosition,
    Disk,
    GamePort,
    Collision,
    Paula,
    PotGo,
    LastWriteMirror,
    StoredValue
}

internal enum CustomRegisterWriteSemantics : byte
{
    Ignore,
    Device,
    SetClear,
    Strobe,
    MaskedStore
}

internal enum CustomRegisterImpactRule : byte
{
    ValueChanged,
    Always,
    Bplcon0Fields,
    Bplcon3Fields,
    DiwhighOwners
}

internal readonly record struct ResolvedCustomRegister(
    CustomRegisterDescriptor Definition,
    bool IsPresent,
    ushort WritableMask,
    string? ReadName,
    string? WriteName,
    CustomRegisterImplementationStatus ImplementationStatus,
    CustomRegisterReadHandler ReadHandler,
    CustomRegisterReadHandler HostReadHandler,
    CustomRegisterWriteSemantics WriteSemantics,
    CustomRegisterWriteTarget WriteTargets,
    HardwareScheduleImpact PotentialImpact,
    CustomRegisterImpactRule ImpactRule,
    CustomRegisterStorageMode StorageMode);

internal readonly record struct CustomRegisterFileSnapshotEntry(
    ushort Offset,
    string Name,
    string? ReadName,
    string? WriteName,
    bool IsPresent,
    CustomRegisterImplementationStatus ImplementationStatus,
    CustomRegisterAccess Access,
    CustomRegisterReadback Readback,
    CustomRegisterReadHandler ReadHandler,
    CustomRegisterReadHandler HostReadHandler,
    CustomRegisterWriteSemantics WriteSemantics,
    CustomRegisterWriteTarget WriteTargets,
    HardwareScheduleImpact PotentialImpact,
    CustomRegisterImpactRule ImpactRule,
    CustomRegisterStorageMode StorageMode,
    bool HasWrite,
    ushort LastWriteValue,
    CustomRegisterObservationWidth LastWriteWidth,
    CustomRegisterByteLane LastWriteLane,
    long LastWriteCycle,
    AmigaBusRequester LastWriteRequester,
    CustomRegisterWriteCause LastWriteCause,
    bool HasStoredValue,
    ushort StoredValue,
    long StoredValueCycle);

internal sealed class CustomRegisterFileSnapshot : IReadOnlyList<CustomRegisterFileSnapshotEntry>
{
    private readonly CustomRegisterFileSnapshotEntry[] _entries;

    public CustomRegisterFileSnapshot(CustomRegisterFileSnapshotEntry[] entries)
        => _entries = entries ?? throw new ArgumentNullException(nameof(entries));

    public int Count => _entries.Length;

    public CustomRegisterFileSnapshotEntry this[int index] => _entries[index];

    public CustomRegisterFileSnapshotEntry Get(ushort offset)
        => _entries[(offset & 0x01FE) >> 1];

    public IEnumerator<CustomRegisterFileSnapshotEntry> GetEnumerator()
        => ((IEnumerable<CustomRegisterFileSnapshotEntry>)_entries).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _entries.GetEnumerator();
}

internal sealed class CustomRegisterFile
{
    private const int RegisterCount = 0x100;

    private static readonly ConcurrentDictionary<AmigaChipset, ResolvedCustomRegister[]> ResolvedProfiles = new();

    private readonly ResolvedCustomRegister[] _definitions;
    private readonly CustomRegisterReadHandler[] _cpuReadHandlers = new CustomRegisterReadHandler[RegisterCount];
    private readonly CustomRegisterReadHandler[] _hostReadHandlers = new CustomRegisterReadHandler[RegisterCount];
    private readonly WriteRuntimeState[] _writeStates = new WriteRuntimeState[RegisterCount];
    private readonly bool[] _hasStoredValues = new bool[RegisterCount];
    private readonly ushort[] _storedValues = new ushort[RegisterCount];
    private readonly long[] _storedValueCycles = new long[RegisterCount];

    public CustomRegisterFile(AmigaChipset chipset)
    {
        _definitions = ResolvedProfiles.GetOrAdd(chipset, static profile => Resolve(profile));
        for (var index = 0; index < _definitions.Length; index++)
        {
            _cpuReadHandlers[index] = _definitions[index].ReadHandler;
            _hostReadHandlers[index] = _definitions[index].HostReadHandler;
        }

        Reset();
    }

    public ref readonly ResolvedCustomRegister Get(ushort offset)
        => ref _definitions[(offset & 0x01FE) >> 1];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CustomRegisterReadHandler GetCpuReadHandler(ushort offset)
        => _cpuReadHandlers[(offset & 0x01FE) >> 1];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CustomRegisterReadHandler GetHostReadHandler(ushort offset)
        => _hostReadHandlers[(offset & 0x01FE) >> 1];

    public static ref readonly ResolvedCustomRegister GetResolved(AmigaChipset chipset, ushort offset)
    {
        var definitions = ResolvedProfiles.GetOrAdd(chipset, static profile => Resolve(profile));
        return ref definitions[(offset & 0x01FE) >> 1];
    }

    public bool CanCopperWrite(ushort offset, ushort copcon)
        => AgnusCopperRegisterAccess.CanWrite(offset, copcon);

    public bool StopsCopper(ushort offset, ushort copcon)
        => AgnusCopperRegisterAccess.StopsCopper(offset, copcon);

    public void Reset()
    {
        Array.Clear(_writeStates);
        Array.Clear(_hasStoredValues);
        Array.Clear(_storedValues);
        Array.Clear(_storedValueCycles);
        for (var index = 0; index < _definitions.Length; index++)
        {
            ref readonly var definition = ref _definitions[index];
            if (definition.StorageMode is not (CustomRegisterStorageMode.RegisterFile or
                CustomRegisterStorageMode.DevicePublished))
            {
                continue;
            }

            _hasStoredValues[index] = true;
            _storedValues[index] = definition.Definition.ResetValue;
        }
    }

    public void RecordWrite(
        ushort offset,
        ushort value,
        CustomRegisterObservationWidth width,
        CustomRegisterByteLane lane,
        long cycle,
        AmigaBusRequester requester,
        CustomRegisterWriteCause cause)
    {
        ref var state = ref _writeStates[(offset & 0x01FE) >> 1];
        state.HasWrite = true;
        state.LastWriteValue = value;
        state.LastWriteWidth = width;
        state.LastWriteLane = lane;
        state.LastWriteCycle = Math.Max(0, cycle);
        state.LastWriteRequester = requester;
        state.LastWriteCause = cause;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool PublishStoredValue(ushort offset, ushort value, long cycle)
    {
        var index = (offset & 0x01FE) >> 1;
        if (_hasStoredValues[index] && _storedValues[index] == value)
        {
            return false;
        }

        _hasStoredValues[index] = true;
        _storedValues[index] = value;
        _storedValueCycles[index] = Math.Max(0, cycle);
        return true;
    }

    public bool ApplyRegisterFileWrite(ushort offset, ushort value, long cycle)
    {
        var index = (offset & 0x01FE) >> 1;
        ref readonly var definition = ref _definitions[index];
        if (definition.StorageMode != CustomRegisterStorageMode.RegisterFile)
        {
            return false;
        }

        var previous = _hasStoredValues[index] ? _storedValues[index] : definition.Definition.ResetValue;
        var stored = ApplyStoredWriteValue(
            previous,
            value,
            definition.WritableMask,
            definition.WriteSemantics);
        if (_hasStoredValues[index] && stored == previous)
        {
            return true;
        }

        _hasStoredValues[index] = true;
        _storedValues[index] = stored;
        _storedValueCycles[index] = Math.Max(0, cycle);
        return true;
    }

    internal static ushort ApplyStoredWriteValue(
        ushort previous,
        ushort value,
        ushort writableMask,
        CustomRegisterWriteSemantics semantics)
    {
        var writableValue = (ushort)(value & writableMask);
        return semantics switch
        {
            CustomRegisterWriteSemantics.Ignore or CustomRegisterWriteSemantics.Strobe => previous,
            CustomRegisterWriteSemantics.SetClear when (value & 0x8000) != 0 =>
                (ushort)(previous | writableValue),
            CustomRegisterWriteSemantics.SetClear => (ushort)(previous & ~writableValue),
            _ => (ushort)((previous & ~writableMask) | writableValue)
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort GetStoredValue(ushort offset)
    {
        var index = (offset & 0x01FE) >> 1;
#if DEBUG
        if (!_hasStoredValues[index])
        {
            throw new InvalidOperationException($"Custom register 0x{offset & 0x01FE:X3} has no published stored value.");
        }
#endif

        return _storedValues[index];
    }

    public bool TryGetLastWriteValue(ushort offset, out ushort value)
    {
        ref readonly var state = ref _writeStates[(offset & 0x01FE) >> 1];
        value = state.LastWriteValue;
        return state.HasWrite;
    }

    public CustomRegisterFileSnapshot CaptureSnapshot()
    {
        var entries = new CustomRegisterFileSnapshotEntry[RegisterCount];
        for (var index = 0; index < entries.Length; index++)
        {
            ref readonly var definition = ref _definitions[index];
            ref readonly var state = ref _writeStates[index];
            entries[index] = new CustomRegisterFileSnapshotEntry(
                (ushort)(index << 1),
                definition.Definition.Name,
                definition.ReadName,
                definition.WriteName,
                definition.IsPresent,
                definition.ImplementationStatus,
                definition.Definition.Access,
                definition.Definition.Readback,
                definition.ReadHandler,
                definition.HostReadHandler,
                definition.WriteSemantics,
                definition.WriteTargets,
                definition.PotentialImpact,
                definition.ImpactRule,
                definition.StorageMode,
                state.HasWrite,
                state.LastWriteValue,
                state.LastWriteWidth,
                state.LastWriteLane,
                state.LastWriteCycle,
                state.LastWriteRequester,
                state.LastWriteCause,
                _hasStoredValues[index],
                _storedValues[index],
                _storedValueCycles[index]);
        }

        return new CustomRegisterFileSnapshot(entries);
    }

    private static ResolvedCustomRegister[] Resolve(AmigaChipset chipset)
    {
        var result = new ResolvedCustomRegister[RegisterCount];
        for (var index = 0; index < result.Length; index++)
        {
            var offset = (ushort)(index << 1);
            var descriptor = CustomRegisterMetadata.GetComplete(offset);
            var present = descriptor.IsPresent(chipset);
            var access = descriptor.Access;
            var storageMode = GetStorageMode(descriptor, present);
            var readHandler = storageMode is CustomRegisterStorageMode.RegisterFile or CustomRegisterStorageMode.DevicePublished
                ? CustomRegisterReadHandler.StoredValue
                : GetReadHandler(offset, present);
            result[index] = new ResolvedCustomRegister(
                descriptor,
                present,
                descriptor.GetWritableMask(chipset),
                present && access is CustomRegisterAccess.ReadOnly or CustomRegisterAccess.ReadWrite
                    ? descriptor.Name
                    : null,
                present && access is CustomRegisterAccess.WriteOnly or CustomRegisterAccess.ReadWrite or CustomRegisterAccess.Strobe
                    ? descriptor.Name
                    : null,
                GetImplementationStatus(offset, present),
                readHandler,
                GetHostReadHandler(offset, readHandler),
                GetWriteSemantics(offset, present, access, storageMode),
                GetWriteTargets(offset, present, chipset),
                GetPotentialImpact(offset, present, chipset),
                GetImpactRule(offset),
                storageMode);
        }

        return result;
    }

    private static CustomRegisterImplementationStatus GetImplementationStatus(ushort offset, bool present)
    {
        if (!present)
        {
            return CustomRegisterImplementationStatus.Absent;
        }

        if (!CustomRegisterMetadata.IsDeclared(offset))
        {
            return CustomRegisterImplementationStatus.Unimplemented;
        }

        if (offset is 0x028 or 0x02C or 0x030 or 0x032 or 0x034 or 0x036 or 0x038 or 0x03A or 0x03C or 0x03E or 0x08C)
        {
            return CustomRegisterImplementationStatus.Unimplemented;
        }

        if (offset is 0x00E or 0x012 or 0x014 or 0x016 or 0x018)
        {
            return CustomRegisterImplementationStatus.Partial;
        }

        return CustomRegisterImplementationStatus.Implemented;
    }

    private static CustomRegisterReadHandler GetReadHandler(ushort offset, bool present)
    {
        if (!present)
        {
            return CustomRegisterReadHandler.None;
        }

        return offset switch
        {
            0x000 => CustomRegisterReadHandler.ChipDataBusLatch,
            0x002 => CustomRegisterReadHandler.Dmaconr,
            0x004 or 0x006 => CustomRegisterReadHandler.BeamPosition,
            0x008 or 0x01A => CustomRegisterReadHandler.Disk,
            0x00A or 0x00C => CustomRegisterReadHandler.GamePort,
            0x00E => CustomRegisterReadHandler.Collision,
            0x016 => CustomRegisterReadHandler.PotGo,
            0x07C => CustomRegisterReadHandler.Display,
            >= 0x1C0 and <= 0x1E2 => CustomRegisterReadHandler.Agnus,
            <= 0x01E => CustomRegisterReadHandler.Paula,
            _ => CustomRegisterReadHandler.None
        };
    }

    private static CustomRegisterReadHandler GetHostReadHandler(
        ushort offset,
        CustomRegisterReadHandler cpuReadHandler)
    {
        if (offset is 0x020 or 0x022 or 0x024 or 0x07E)
        {
            return CustomRegisterReadHandler.Disk;
        }

        if (offset == 0x000)
        {
            return CustomRegisterReadHandler.Paula;
        }

        return cpuReadHandler == CustomRegisterReadHandler.None
            ? CustomRegisterReadHandler.LastWriteMirror
            : cpuReadHandler;
    }

    private static CustomRegisterWriteSemantics GetWriteSemantics(
        ushort offset,
        bool present,
        CustomRegisterAccess access,
        CustomRegisterStorageMode storageMode)
    {
        if (!present || access == CustomRegisterAccess.ReadOnly)
        {
            return CustomRegisterWriteSemantics.Ignore;
        }

        if (storageMode == CustomRegisterStorageMode.RegisterFile)
        {
            return CustomRegisterWriteSemantics.MaskedStore;
        }

        if (access == CustomRegisterAccess.Strobe || offset is 0x088 or 0x08A)
        {
            return CustomRegisterWriteSemantics.Strobe;
        }

        return offset is 0x096 or 0x09A or 0x09C or 0x09E
            ? CustomRegisterWriteSemantics.SetClear
            : CustomRegisterWriteSemantics.Device;
    }

    private static CustomRegisterStorageMode GetStorageMode(
        in CustomRegisterDescriptor descriptor,
        bool present)
    {
        if (!present)
        {
            return CustomRegisterStorageMode.None;
        }

        if (descriptor.Readback != CustomRegisterReadback.Stored)
        {
            return CustomRegisterStorageMode.DeviceOwned;
        }

        return descriptor.Owner == CustomRegisterOwner.None
            ? CustomRegisterStorageMode.RegisterFile
            : CustomRegisterStorageMode.DevicePublished;
    }

    private static CustomRegisterWriteTarget GetWriteTargets(
        ushort offset,
        bool present,
        AmigaChipset chipset)
    {
        if (!present)
        {
            return CustomRegisterWriteTarget.None;
        }

        if (offset is >= 0x040 and <= 0x074)
        {
            return CustomRegisterWriteTarget.Blitter;
        }

        if (offset is 0x020 or 0x022 or 0x024 or 0x026 or 0x07E)
        {
            return CustomRegisterWriteTarget.Disk;
        }

        if (offset is 0x096)
        {
            return CustomRegisterWriteTarget.Agnus |
                CustomRegisterWriteTarget.Paula |
                CustomRegisterWriteTarget.Display |
                CustomRegisterWriteTarget.Blitter |
                CustomRegisterWriteTarget.Disk;
        }

        if (offset is 0x09A or 0x09C)
        {
            return CustomRegisterWriteTarget.Paula;
        }

        if (offset is 0x09E)
        {
            return CustomRegisterWriteTarget.Paula | CustomRegisterWriteTarget.Disk;
        }

        if (offset is >= 0x0A0 and <= 0x0DA && ((offset - 0x0A0) % 0x10) <= 0x0A)
        {
            return CustomRegisterWriteTarget.Paula;
        }

        if (offset is 0x02A or 0x02C ||
            offset is >= 0x1C0 and <= 0x1E2)
        {
            return CustomRegisterWriteTarget.Agnus;
        }

        if (offset is 0x02E or >= 0x080 and <= 0x08A or >= 0x08E and <= 0x094 or
            >= 0x0E0 and <= 0x0F6 or 0x108 or 0x10A or >= 0x120 and < 0x140)
        {
            return CustomRegisterWriteTarget.Agnus | CustomRegisterWriteTarget.Display;
        }

        if (offset is 0x098 or 0x100 or 0x102 or 0x104 or 0x106 or
            >= 0x110 and <= 0x11A or >= 0x140 and < 0x1C0)
        {
            return CustomRegisterWriteTarget.Display;
        }

        if (offset == 0x1E4)
        {
            var targets = CustomRegisterWriteTarget.Display;
            if (chipset.Agnus == AgnusModel.Ecs)
            {
                targets |= CustomRegisterWriteTarget.Agnus;
            }

            return targets;
        }

        return CustomRegisterWriteTarget.None;
    }

    private static HardwareScheduleImpact GetPotentialImpact(
        ushort offset,
        bool present,
        AmigaChipset chipset)
    {
        if (!present && CustomRegisterMetadata.IsDeclared(offset))
        {
            return HardwareScheduleImpact.None;
        }

        if (offset is >= 0x180 and < 0x1C0 ||
            offset is >= 0x120 and < 0x180 ||
            offset is >= 0x0E0 and <= 0x0F6 ||
            offset is >= 0x110 and <= 0x11A)
        {
            return HardwareScheduleImpact.Composition;
        }

        if (offset == 0x02A)
        {
            return HardwareScheduleImpact.Raster;
        }

        if (offset is >= 0x1C0 and <= 0x1E2)
        {
            return chipset.Agnus == AgnusModel.Ecs && offset != 0x1DA
                ? HardwareScheduleImpact.Raster
                : HardwareScheduleImpact.None;
        }

        if (offset == 0x1E4)
        {
            var impact = HardwareScheduleImpact.None;
            if (chipset.Agnus == AgnusModel.Ecs)
            {
                impact |= HardwareScheduleImpact.Bitplane;
            }
            if (chipset.Denise == DeniseModel.Ecs)
            {
                impact |= HardwareScheduleImpact.Composition;
            }

            return impact;
        }

        if (offset is 0x08E or 0x090)
        {
            return HardwareScheduleImpact.Bitplane | HardwareScheduleImpact.Composition;
        }

        if (offset is 0x092 or 0x094 or 0x108 or 0x10A)
        {
            return HardwareScheduleImpact.Bitplane;
        }

        if (offset == 0x100)
        {
            return HardwareScheduleImpact.Bitplane | HardwareScheduleImpact.Composition;
        }

        if (offset is 0x102 or 0x104)
        {
            return HardwareScheduleImpact.Composition;
        }

        if (offset == 0x106)
        {
            return chipset.Denise == DeniseModel.Ecs
                ? HardwareScheduleImpact.Sprite | HardwareScheduleImpact.Composition
                : HardwareScheduleImpact.None;
        }

        if (offset is 0x02E or 0x080 or 0x082 or 0x084 or 0x086 or 0x088 or 0x08A)
        {
            return HardwareScheduleImpact.Copper;
        }

        if (offset == 0x096)
        {
            return HardwareScheduleImpact.Bitplane |
                HardwareScheduleImpact.Sprite |
                HardwareScheduleImpact.Copper |
                HardwareScheduleImpact.Blitter |
                HardwareScheduleImpact.Audio |
                HardwareScheduleImpact.Disk;
        }

        if (offset is 0x09A or 0x09C)
        {
            return HardwareScheduleImpact.All & ~HardwareScheduleImpact.Composition;
        }

        if (offset == 0x09E)
        {
            return HardwareScheduleImpact.Audio | HardwareScheduleImpact.Disk;
        }

        if (offset is >= 0x040 and <= 0x074)
        {
            return offset is 0x05A or 0x05C or 0x05E && chipset.Agnus != AgnusModel.Ecs
                ? HardwareScheduleImpact.None
                : HardwareScheduleImpact.Blitter;
        }

        if (offset is 0x020 or 0x022 or 0x024 or 0x026 or 0x07E)
        {
            return HardwareScheduleImpact.Disk;
        }

        if (offset is >= 0x0A0 and <= 0x0DA)
        {
            var channel = (offset - 0x0A0) / 0x10;
            var register = (offset - 0x0A0) % 0x10;
            if (channel < AmigaConstants.PaulaChannelCount && register <= 0x0A)
            {
                return register == 0x08
                    ? HardwareScheduleImpact.None
                    : HardwareScheduleImpact.Audio;
            }
        }

        return HardwareScheduleImpact.All;
    }

    private static CustomRegisterImpactRule GetImpactRule(ushort offset)
        => offset switch
        {
            0x058 or 0x05E or 0x088 or 0x08A => CustomRegisterImpactRule.Always,
            0x100 => CustomRegisterImpactRule.Bplcon0Fields,
            0x106 => CustomRegisterImpactRule.Bplcon3Fields,
            0x1E4 => CustomRegisterImpactRule.DiwhighOwners,
            _ => CustomRegisterImpactRule.ValueChanged
        };

    private struct WriteRuntimeState
    {
        public bool HasWrite;
        public ushort LastWriteValue;
        public CustomRegisterObservationWidth LastWriteWidth;
        public CustomRegisterByteLane LastWriteLane;
        public long LastWriteCycle;
        public AmigaBusRequester LastWriteRequester;
        public CustomRegisterWriteCause LastWriteCause;
    }

}
