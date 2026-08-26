/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;
using Copper68k;
using CopperMod.Amiga.Bus;

namespace CopperMod.Amiga.Jit.M68000;

/// <summary>
/// Optional MC68000 JIT boundary for an <see cref="AmigaBus"/>.
/// </summary>
/// <remarks>
/// The adapter deliberately owns its code-write generations and invalidation event.
/// The normal Amiga bus therefore has no JIT bookkeeping on its interpreter write path.
/// It is not enabled by default; factory integration follows after the prefetch and
/// compiled-to-interpreter handoff contract is covered by conformance tests.
/// </remarks>
internal sealed class M68000JitBusAdapter :
    IM68kBus,
    IM68kCodeReader,
    IM68kStablePhysicalAddressMap,
    IM68kJitBus
{
    private const uint CpuAddressMask = 0x00FF_FFFFu;
    private const int CodePageShift = 8;
    private const int MaximumSnapshotBytes = 4096;

    private readonly AmigaBus _bus;
    private readonly IM68kBus _cpuBus;
    private readonly IM68kJitBus _jitBus;
    private readonly IM68kStablePhysicalAddressMap _physicalAddressMap;
    private readonly Dictionary<uint, uint> _codeGenerations = new();

    public M68000JitBusAdapter(AmigaBus bus)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _cpuBus = bus;
        _jitBus = bus;
        _physicalAddressMap = bus;
    }

    /// <summary>
    /// Raised only for writes through this JIT adapter that alter JIT-eligible writable memory.
    /// </summary>
    public event Action<uint, int>? JitCodeRangeWritten;

    public uint CpuPhysicalAddressMapGeneration => _physicalAddressMap.CpuPhysicalAddressMapGeneration;

    public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
        => _cpuBus.ReadByte(address, ref cycle, accessKind);

    public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
        => _cpuBus.ReadWord(address, ref cycle, accessKind);

    public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind)
        => _cpuBus.ReadLong(address, ref cycle, accessKind);

    public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
    {
        _cpuBus.WriteByte(address, value, ref cycle, accessKind);
        InvalidateWrittenCodeRange(address, 1);
    }

    public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
    {
        _cpuBus.WriteWord(address, value, ref cycle, accessKind);
        InvalidateWrittenCodeRange(address, 2);
    }

    public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
    {
        _cpuBus.WriteLong(address, value, ref cycle, accessKind);
        InvalidateWrittenCodeRange(address, 4);
    }

    public bool HasHostGateway(uint address) => _bus.HasHostGateway(address);

    public bool TryInvokeHostGateway(uint instructionProgramCounter, uint token, M68kCpuState state)
        => _bus.TryInvokeHostGateway(instructionProgramCounter, token, state);

    public M68kHostGatewayInvocation InvokeHostGateway(uint instructionProgramCounter, uint token, M68kCpuState state)
        => _bus.InvokeHostGateway(instructionProgramCounter, token, state);

    public void ResetExternalDevices(long cycle) => _bus.ResetExternalDevices(cycle);

    public bool IsCpuPhysicalAddressMapped(uint address, int byteCount, M68kBusAccessKind accessKind)
        => _physicalAddressMap.IsCpuPhysicalAddressMapped(address, byteCount, accessKind);

    public ushort ReadHostWord(uint address) => _bus.ReadHostWord(address);

    public bool IsJitCodeAddress(uint physicalAddress, int byteCount, M68kBusAccessKind accessKind)
    {
        var address = Normalize(physicalAddress);
        return IsWritableJitCodeRange(address, byteCount) ||
            _jitBus.IsJitCodeAddress(address, byteCount, accessKind);
    }

    public bool IsJitReadOnlyCodeAddress(uint physicalAddress, int byteCount, M68kBusAccessKind accessKind)
        => _jitBus.IsJitReadOnlyCodeAddress(Normalize(physicalAddress), byteCount, accessKind);

    public ushort ReadJitCodeWord(uint physicalAddress)
        => _jitBus.ReadJitCodeWord(Normalize(physicalAddress));

    public uint GetJitCodePageGeneration(uint physicalAddress)
        => _codeGenerations.GetValueOrDefault(GetCodePage(Normalize(physicalAddress)));

    public bool JitCodeRangeGenerationMatches(
        uint physicalAddress,
        int byteCount,
        uint startGeneration,
        uint endGeneration)
    {
        if (byteCount <= 0)
        {
            return true;
        }

        var start = Normalize(physicalAddress);
        var end = Normalize(start + (uint)(byteCount - 1));
        return GetJitCodePageGeneration(start) == startGeneration &&
            GetJitCodePageGeneration(end) == endGeneration;
    }

    public bool TryCaptureJitCodeSnapshot(uint physicalRoot, int maxBytes, out M68kJitCodeSnapshot snapshot)
    {
        snapshot = default;
        var root = Normalize(physicalRoot);
        var byteCount = Math.Min(maxBytes, MaximumSnapshotBytes);
        if (byteCount <= 0 || !IsJitCodeAddress(root, byteCount, M68kBusAccessKind.CpuInstructionFetch))
        {
            return false;
        }

        var pageCount = checked((int)((GetCodePage(Normalize(root + (uint)(byteCount - 1))) - GetCodePage(root)) + 1));
        var pages = new uint[pageCount];
        var beforeGenerations = new uint[pageCount];
        CaptureGenerations(root, pages, beforeGenerations);

        var bytes = new byte[byteCount];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = _bus.ReadHostByte(root + (uint)index);
        }

        var afterGenerations = new uint[pageCount];
        CaptureGenerations(root, pages, afterGenerations);
        if (!beforeGenerations.AsSpan().SequenceEqual(afterGenerations))
        {
            return false;
        }

        var gateways = new List<uint>();
        for (var offset = 0; offset + 1 < byteCount; offset += 2)
        {
            var address = root + (uint)offset;
            if (HasHostGateway(address))
            {
                gateways.Add(address);
            }
        }

        snapshot = new M68kJitCodeSnapshot(
            root,
            bytes,
            new M68kCodeGenerationStamp(pages, beforeGenerations),
            gateways.ToArray());
        return true;
    }

    private void InvalidateWrittenCodeRange(uint address, int byteCount)
    {
        if (byteCount <= 0 || !IsWritableJitCodeRange(address, byteCount))
        {
            return;
        }

        var startPage = GetCodePage(Normalize(address));
        var endPage = GetCodePage(Normalize(address + (uint)(byteCount - 1)));
        for (var page = startPage; page <= endPage; page++)
        {
            _codeGenerations[page] = unchecked(_codeGenerations.GetValueOrDefault(page) + 1);
        }

        JitCodeRangeWritten?.Invoke(Normalize(address), byteCount);
    }

    private bool IsWritableJitCodeRange(uint address, int byteCount)
        => IsRangeWithin(Normalize(address), byteCount, _bus.ExpansionRamBase, _bus.ExpansionRam.Length) ||
            IsRangeWithin(Normalize(address), byteCount, _bus.RealFastRamBase, _bus.RealFastRam.Length);

    private static bool IsRangeWithin(uint address, int byteCount, uint baseAddress, int length)
    {
        if (byteCount <= 0 || length <= 0 || address < baseAddress)
        {
            return false;
        }

        var offset = address - baseAddress;
        return offset <= (uint)length && (uint)byteCount <= (uint)length - offset;
    }

    private void CaptureGenerations(uint root, uint[] pages, uint[] generations)
    {
        var firstPage = GetCodePage(root);
        for (var index = 0; index < pages.Length; index++)
        {
            var page = firstPage + (uint)index;
            pages[index] = page << CodePageShift;
            generations[index] = _codeGenerations.GetValueOrDefault(page);
        }
    }

    private static uint Normalize(uint address) => address & CpuAddressMask;

    private static uint GetCodePage(uint address) => address >> CodePageShift;
}
