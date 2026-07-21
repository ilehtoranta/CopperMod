/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Text;

namespace CopperMod.Amiga.Video.Rtg.CyberGraphics
{
    internal sealed class CyberGraphicsRtgFirmware : IAmigaRtgFirmwareProvider
    {
        internal const ushort DiagnosticRomVector = 0x4000;
        internal const int DiagAreaOffset = 0x4000;
        internal const int DiagAreaCopySize = 0x1000;
        internal const int DiagPointOffset = 0x20;
        internal const int ResidentOffset = 0x40;
        internal const int NameOffset = 0x100;
        internal const int IdStringOffset = 0x120;
        internal const int ResidentInitOffset = 0x140;
        internal const int LibraryBaseOffset = 0x300;
        private const int ExecLibraryListOffset = 0x17A;

        private readonly CyberGraphicsLibrary _library;
        private readonly byte[] _rom = CreateBoardRom();
        private AmigaBus? _bus;
        private uint _diagCopyBase;

        internal CyberGraphicsRtgFirmware(CyberGraphicsLibrary library)
            => _library = library ?? throw new ArgumentNullException(nameof(library));

        public AutoconfigIdentity Identity
            => AutoconfigIdentity.CreateIoBoard(
                AutoconfigRtgBoard.BoardSize,
                AutoconfigRtgBoard.ManufacturerId,
                AutoconfigRtgBoard.ProductId,
                DiagnosticRomVector);

        internal bool ResidentInstalled { get; private set; }

        internal uint LibraryBase { get; private set; }

        public void Attach(AmigaBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            WriteTrap(DiagPointOffset, bus.RegisterRelocatableHostGateway(HostDiagBootstrap));
            WriteTrap(ResidentInitOffset, bus.RegisterRelocatableHostGateway(HostResidentInit));
        }

        public byte ReadBoardByte(int offset)
            => (uint)offset < (uint)_rom.Length ? _rom[offset] : (byte)0;

        public void OnConfigured(uint baseAddress)
            => _ = baseAddress;

        public void Reset(bool cold)
        {
            _ = cold;
            ResidentInstalled = false;
            LibraryBase = 0;
            _diagCopyBase = 0;
            _library.ResetRtgState();
        }

        internal uint InstallHostShimResident(AmigaBus bus, uint copyBase, uint execBase)
        {
            ArgumentNullException.ThrowIfNull(bus);
            if (copyBase == 0 || execBase == 0 ||
                !bus.IsMappedMemoryRange(copyBase, DiagAreaCopySize))
            {
                throw new ArgumentOutOfRangeException(nameof(copyBase));
            }

            for (var offset = 0; offset < DiagAreaCopySize; offset++)
            {
                bus.WriteByte(copyBase + (uint)offset, _rom[DiagAreaOffset + offset], 0);
            }

            var state = new M68kCpuState();
            state.A[2] = copyBase;
            state.A[6] = execBase;
            HostDiagBootstrap(state);
            HostResidentInit(state);
            if (state.D[0] == 0)
            {
                throw new InvalidOperationException("CyberGraphX diagnostic resident could not be installed.");
            }

            return state.D[0];
        }

        private void HostDiagBootstrap(M68kCpuState state)
        {
            var bus = _bus;
            _diagCopyBase = state.A[2] & 0x00FF_FFFFu;
            if (bus != null && _diagCopyBase != 0 && bus.IsMappedMemoryRange(_diagCopyBase, DiagAreaCopySize))
            {
                var resident = _diagCopyBase + ResidentOffset;
                bus.WriteLong(resident + 0x02, resident);
                bus.WriteLong(resident + 0x06, _diagCopyBase + ResidentInitOffset + 4u);
                bus.WriteLong(resident + 0x0E, _diagCopyBase + NameOffset);
                bus.WriteLong(resident + 0x12, _diagCopyBase + IdStringOffset);
                bus.WriteLong(resident + 0x16, _diagCopyBase + ResidentInitOffset);
            }

            state.D[0] = 1;
        }

        private void HostResidentInit(M68kCpuState state)
        {
            var bus = _bus;
            var execBase = state.A[6] != 0 ? state.A[6] : bus?.ReadLong(4) ?? 0;
            if (bus == null || _diagCopyBase == 0 || execBase == 0 ||
                !bus.IsMappedMemoryRange(_diagCopyBase, DiagAreaCopySize))
            {
                state.D[0] = 0;
                return;
            }

            LibraryBase = _diagCopyBase + LibraryBaseOffset;
            bus.ClearMemory(LibraryBase - 258, 258 + 0x22);
            bus.WriteByte(LibraryBase + 0x08, 9, 0);
            bus.WriteLong(LibraryBase + 0x0A, _diagCopyBase + NameOffset);
            bus.WriteWord(LibraryBase + 0x10, 258);
            bus.WriteWord(LibraryBase + 0x12, 0x22);
            bus.WriteWord(LibraryBase + 0x14, CyberGraphicsLibrary.Version);
            bus.WriteWord(LibraryBase + 0x16, CyberGraphicsLibrary.Revision);
            bus.WriteLong(LibraryBase + 0x18, _diagCopyBase + IdStringOffset);
            _library.InstallTrapVectors(LibraryBase);
            LinkTail(bus, execBase + ExecLibraryListOffset, LibraryBase);
            _library.InstallSystemPatches(execBase);
            ResidentInstalled = true;
            state.D[0] = LibraryBase;
        }

        private static void LinkTail(AmigaBus bus, uint list, uint node)
        {
            if (!bus.IsMappedMemoryRange(list, 14)) return;
            if (bus.ReadLong(list) == 0 && bus.ReadLong(list + 8) == 0)
            {
                bus.WriteLong(list, list + 4);
                bus.WriteLong(list + 4, 0);
                bus.WriteLong(list + 8, list);
            }

            var tailPred = bus.ReadLong(list + 8);
            if (tailPred == 0) tailPred = list;
            bus.WriteLong(node, list + 4);
            bus.WriteLong(node + 4, tailPred);
            bus.WriteLong(tailPred == list ? list : tailPred, node);
            bus.WriteLong(list + 8, node);
        }

        private static byte[] CreateBoardRom()
        {
            var rom = new byte[AutoconfigRtgBoard.BoardSize];
            rom[DiagAreaOffset] = 0x90;
            WriteUInt16(rom, DiagAreaOffset + 0x02, DiagAreaCopySize);
            WriteUInt16(rom, DiagAreaOffset + 0x04, DiagPointOffset);
            WriteUInt16(rom, DiagAreaOffset + 0x08, NameOffset);
            WriteReturnStub(rom, DiagPointOffset);
            WriteReturnStub(rom, ResidentInitOffset);
            var resident = DiagAreaOffset + ResidentOffset;
            WriteUInt16(rom, resident, 0x4AFC);
            WriteUInt32(rom, resident + 0x02, ResidentOffset);
            WriteUInt32(rom, resident + 0x06, ResidentInitOffset + 4u);
            rom[resident + 0x0A] = 1;
            rom[resident + 0x0B] = 1;
            rom[resident + 0x0C] = 9;
            rom[resident + 0x0D] = 20;
            WriteUInt32(rom, resident + 0x0E, NameOffset);
            WriteUInt32(rom, resident + 0x12, IdStringOffset);
            WriteUInt32(rom, resident + 0x16, ResidentInitOffset);
            WriteAscii(rom, DiagAreaOffset + NameOffset, CyberGraphicsLibrary.Name);
            WriteAscii(rom, DiagAreaOffset + IdStringOffset, "cybergraphics.library 52.1 (Copper RTG)");
            return rom;
        }

        private void WriteTrap(int relativeOffset, uint token)
        {
            var offset = DiagAreaOffset + relativeOffset;
            WriteUInt16(_rom, offset, 0xFF00);
            WriteUInt32(_rom, offset + 2, token);
        }

        private static void WriteReturnStub(byte[] rom, int relativeOffset)
        {
            var offset = DiagAreaOffset + relativeOffset;
            rom[offset] = 0x70;
            rom[offset + 1] = 0x01;
            rom[offset + 2] = 0x4E;
            rom[offset + 3] = 0x75;
        }

        private static void WriteAscii(byte[] rom, int offset, string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            Array.Copy(bytes, 0, rom, offset, bytes.Length);
            rom[offset + bytes.Length] = 0;
        }

        private static void WriteUInt16(byte[] target, int offset, int value)
        {
            target[offset] = (byte)(value >> 8);
            target[offset + 1] = (byte)value;
        }

        private static void WriteUInt32(byte[] target, int offset, uint value)
        {
            target[offset] = (byte)(value >> 24);
            target[offset + 1] = (byte)(value >> 16);
            target[offset + 2] = (byte)(value >> 8);
            target[offset + 3] = (byte)value;
        }

    }
}
