/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;

namespace CopperMod.Amiga
{
    internal enum CyberGraphicsPatchDisposition
    {
        ChainOriginal,
        Handled
    }

    internal readonly record struct CyberGraphicsSavedLibraryVector(
        string LibraryName,
        uint LibraryBase,
        ushort LibraryVersion,
        int VectorOffset,
        uint VectorAddress,
        uint OriginalTarget);

    internal abstract class CyberGraphicsLibraryPatchModule
    {
        protected CyberGraphicsLibraryPatchModule(
            string libraryName,
            ushort minimumVersion,
            IReadOnlyList<int> vectorOffsets)
        {
            LibraryName = libraryName ?? throw new ArgumentNullException(nameof(libraryName));
            MinimumVersion = minimumVersion;
            VectorOffsets = vectorOffsets ?? throw new ArgumentNullException(nameof(vectorOffsets));
        }

        public string LibraryName { get; }

        public ushort MinimumVersion { get; }

        public IReadOnlyList<int> VectorOffsets { get; }

        public abstract CyberGraphicsPatchDisposition Dispatch(
            ICyberGraphicsGuestServices? guestServices,
            int vectorOffset,
            uint originalTarget,
            M68kCpuState state);
    }

    internal sealed class CyberGraphicsGraphicsPatches : CyberGraphicsLibraryPatchModule
    {
        // The display database and V39 bitmap vectors are included alongside the
        // drawing gateways. A gateway must chain when neither endpoint is RTG.
        private static readonly int[] Offsets =
        [
            -30,  // BltBitMap
            -60,  // Text
            -192, // LoadRGB4
            -222, // LoadView
            -234, // SetRast
            -240, // Move
            -246, // Draw
            -288, // SetRGB4
            -306, // RectFill
            -552, // ClipBlit
            -606, // BltBitMapRastPort
            -636, // BltMaskBitMapRastPort
            -726, // FindDisplayInfo
            -732, // NextDisplayInfo
            -756, // GetDisplayInfoData
            -798, // ModeNotAvailable
            -852, // SetRGB32
            -882, // LoadRGB32
            -918, // AllocBitMap
            -924, // FreeBitMap
            -942, // ChangeVPBitMap
            -960, // GetBitMapAttr
            -996, // SetRGB32CM
            -1050 // BestModeIDA
        ];

        public CyberGraphicsGraphicsPatches()
            : base("graphics.library", 39, Offsets)
        {
        }

        public override CyberGraphicsPatchDisposition Dispatch(
            ICyberGraphicsGuestServices? guestServices,
            int vectorOffset,
            uint originalTarget,
            M68kCpuState state)
            => guestServices?.TryInvokeGraphicsLibraryPatch(vectorOffset, state) == true
                ? CyberGraphicsPatchDisposition.Handled
                : CyberGraphicsPatchDisposition.ChainOriginal;
    }

    internal sealed class CyberGraphicsIntuitionPatches : CyberGraphicsLibraryPatchModule
    {
        private static readonly int[] Offsets =
        [
            -66,  // CloseScreen
            -198, // OpenScreen
            -246, // ScreenToBack
            -252, // ScreenToFront
            -378, // MakeScreen
            -384, // RemakeDisplay
            -390, // RethinkDisplay
            -612, // OpenScreenTagList
            -768, // AllocScreenBuffer
            -774, // FreeScreenBuffer
            -780  // ChangeScreenBuffer
        ];

        public CyberGraphicsIntuitionPatches()
            : base("intuition.library", 39, Offsets)
        {
        }

        public override CyberGraphicsPatchDisposition Dispatch(
            ICyberGraphicsGuestServices? guestServices,
            int vectorOffset,
            uint originalTarget,
            M68kCpuState state)
            => guestServices?.TryInvokeIntuitionLibraryPatch(vectorOffset, originalTarget, state) == true
                ? CyberGraphicsPatchDisposition.Handled
                : CyberGraphicsPatchDisposition.ChainOriginal;
    }

    internal sealed class CyberGraphicsLayersPatches : CyberGraphicsLibraryPatchModule
    {
        private static readonly int[] Offsets =
        [
            -36,  // CreateUpfrontLayer
            -42,  // CreateBehindLayer
            -60,  // MoveLayer
            -66,  // SizeLayer
            -72,  // ScrollLayer
            -90,  // DeleteLayer
            -126, // SwapBitsRastPortClipRect
            -174, // MoveSizeLayer
            -180, // CreateUpfrontHookLayer
            -186  // CreateBehindHookLayer
        ];

        public CyberGraphicsLayersPatches()
            : base("layers.library", 39, Offsets)
        {
        }

        public override CyberGraphicsPatchDisposition Dispatch(
            ICyberGraphicsGuestServices? guestServices,
            int vectorOffset,
            uint originalTarget,
            M68kCpuState state)
            => guestServices?.TryInvokeLayersLibraryPatch(vectorOffset, state) == true
                ? CyberGraphicsPatchDisposition.Handled
                : CyberGraphicsPatchDisposition.ChainOriginal;
    }

    internal sealed class CyberGraphicsLibraryPatchManager
    {
        private const int ExecLibraryListOffset = 0x17A;
        private const int NodeSuccessorOffset = 0x00;
        private const int NodeNameOffset = 0x0A;
        private const int LibraryVersionOffset = 0x14;
        private const int MaximumLibraryNodes = 512;
        private const int MaximumNameBytes = 96;
        private const ushort JumpAbsoluteLongOpcode = 0x4EF9;

        private readonly AmigaBus _bus;
        private readonly Func<ICyberGraphicsGuestServices?> _guestServices;
        private readonly CyberGraphicsLibraryPatchModule[] _modules;
        private readonly Dictionary<(uint LibraryBase, int VectorOffset), CyberGraphicsSavedLibraryVector> _patches = new();

        public CyberGraphicsLibraryPatchManager(
            AmigaBus bus,
            Func<ICyberGraphicsGuestServices?> guestServices)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _guestServices = guestServices ?? throw new ArgumentNullException(nameof(guestServices));
            _modules =
            [
                new CyberGraphicsGraphicsPatches(),
                new CyberGraphicsLayersPatches(),
                new CyberGraphicsIntuitionPatches()
            ];
        }

        public IReadOnlyList<CyberGraphicsLibraryPatchModule> Modules => _modules;

        public IReadOnlyCollection<CyberGraphicsSavedLibraryVector> InstalledVectors => _patches.Values;

        public int TryInstallAvailable(uint execBase)
        {
            if (execBase == 0 || !_bus.IsMappedMemoryRange(execBase + ExecLibraryListOffset, 12))
            {
                return 0;
            }

            var installed = 0;
            foreach (var module in _modules)
            {
                if (!TryFindLibrary(execBase, module.LibraryName, out var libraryBase, out var version) ||
                    version < module.MinimumVersion)
                {
                    continue;
                }

                foreach (var vectorOffset in module.VectorOffsets)
                {
                    if (TryInstallVector(module, libraryBase, version, vectorOffset))
                    {
                        installed++;
                    }
                }
            }

            return installed;
        }

        public bool IsInstalled(uint libraryBase, int vectorOffset)
            => _patches.ContainsKey((libraryBase, vectorOffset));

        public void Reset()
            => _patches.Clear();

        private bool TryInstallVector(
            CyberGraphicsLibraryPatchModule module,
            uint libraryBase,
            ushort libraryVersion,
            int vectorOffset)
        {
            var key = (libraryBase, vectorOffset);
            if (_patches.ContainsKey(key))
            {
                return false;
            }

            var vectorAddress = AddOffset(libraryBase, vectorOffset);
            if (!_bus.IsMappedMemoryRange(vectorAddress, 6) ||
                _bus.ReadWord(vectorAddress) != JumpAbsoluteLongOpcode)
            {
                return false;
            }

            var originalTarget = _bus.ReadLong(vectorAddress + 2);
            if (originalTarget == 0 || (originalTarget & 1) != 0)
            {
                return false;
            }

            var saved = new CyberGraphicsSavedLibraryVector(
                module.LibraryName,
                libraryBase,
                libraryVersion,
                vectorOffset,
                vectorAddress,
                originalTarget);
            _patches.Add(key, saved);
            _bus.RegisterHostTrapStub(vectorAddress, state => Dispatch(module, saved, state));
            return true;
        }

        private void Dispatch(
            CyberGraphicsLibraryPatchModule module,
            CyberGraphicsSavedLibraryVector saved,
            M68kCpuState state)
        {
            if (module.Dispatch(_guestServices(), saved.VectorOffset, saved.OriginalTarget, state) == CyberGraphicsPatchDisposition.Handled)
            {
                return;
            }

            // The host-trap instruction automatically performs the caller's RTS
            // only when PC is unchanged. Pointing PC at the saved JMP target is a
            // true tail chain: the original routine consumes the existing return
            // address and sees every argument register exactly as supplied.
            state.ProgramCounter = saved.OriginalTarget;
        }

        private bool TryFindLibrary(
            uint execBase,
            string expectedName,
            out uint libraryBase,
            out ushort version)
        {
            libraryBase = 0;
            version = 0;
            var listAddress = execBase + ExecLibraryListOffset;
            var node = _bus.ReadLong(listAddress);
            var tailSentinel = listAddress + 4;
            var visited = new HashSet<uint>();
            for (var count = 0; count < MaximumLibraryNodes && node != 0 && node != tailSentinel; count++)
            {
                if (!visited.Add(node) || !_bus.IsMappedMemoryRange(node, LibraryVersionOffset + 2))
                {
                    return false;
                }

                var nameAddress = _bus.ReadLong(node + NodeNameOffset);
                if (ReadNameEquals(nameAddress, expectedName))
                {
                    libraryBase = node;
                    version = _bus.ReadWord(node + LibraryVersionOffset);
                    return true;
                }

                node = _bus.ReadLong(node + NodeSuccessorOffset);
            }

            return false;
        }

        private bool ReadNameEquals(uint address, string expected)
        {
            if (address == 0)
            {
                return false;
            }

            for (var index = 0; index < MaximumNameBytes; index++)
            {
                if (!_bus.IsMappedMemoryRange(address + (uint)index, 1))
                {
                    return false;
                }

                var value = _bus.ReadByte(address + (uint)index);
                if (index == expected.Length)
                {
                    return value == 0;
                }

                if (value == 0 || char.ToLowerInvariant((char)value) != char.ToLowerInvariant(expected[index]))
                {
                    return false;
                }
            }

            return false;
        }

        private static uint AddOffset(uint address, int offset)
            => unchecked((uint)((long)address + offset));
    }
}
