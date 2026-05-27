using System;
using System.Collections.Generic;

namespace CopperMod.Amiga
{
    internal readonly struct CustomRegisterWrite
    {
        public CustomRegisterWrite(long cycle, ushort address, ushort value)
        {
            Cycle = cycle;
            Address = address;
            Value = value;
        }

        public long Cycle { get; }

        public ushort Address { get; }

        public ushort Value { get; }
    }

    internal sealed class AmigaBus : IM68kBus
    {
        private const int MaxCapturedBusAccesses = 65536;
        private readonly byte[] _chipRam;
        private readonly Dictionary<uint, Action<M68kCpuState>> _hostCallbacks = new Dictionary<uint, Action<M68kCpuState>>();
        private readonly List<MappedMemoryRegion> _readOnlyRegions = new List<MappedMemoryRegion>();
        private readonly List<AmigaCiaInterruptEvent> _pendingCiaInterrupts = new List<AmigaCiaInterruptEvent>();
        private readonly BoundedBusAccessLog _busAccesses = new BoundedBusAccessLog(MaxCapturedBusAccesses);
        private readonly byte[] _pendingCustomBytes = new byte[0x200];
        private readonly bool[] _pendingCustomByteWritten = new bool[0x200];
        private readonly long _palFrameCycles;
        private readonly double _palLineCycles;
        private long _nextVerticalBlankCycle;

        public AmigaBus(int chipRamSize = AmigaConstants.DefaultChipRamSize, IAmigaBusArbiter? arbiter = null)
        {
            if (chipRamSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chipRamSize), chipRamSize, "Chip RAM size must be positive.");
            }

            _chipRam = new byte[chipRamSize];
            Arbiter = arbiter ?? new ZeroWaitBusArbiter();
            Paula = new Paula(this);
            Disk = new AmigaDiskController(this);
            Display = new OcsDisplay(this);
            Blitter = new AmigaBlitter(this);
            CiaA = new AmigaCia(AmigaCiaId.A);
            CiaB = new AmigaCia(AmigaCiaId.B);
            _palFrameCycles = Math.Max(1, (long)Math.Round(AmigaConstants.A500PalCpuClockHz / AmigaConstants.A500PalVBlankHz));
            _palLineCycles = AmigaConstants.A500PalCpuClockHz / AmigaConstants.A500PalVBlankHz / AmigaConstants.A500PalRasterLines;
            _nextVerticalBlankCycle = _palFrameCycles;
            CiaA.Reset(0x02);
            CiaB.Reset();
        }

        public Paula Paula { get; }

        public AmigaDiskController Disk { get; }

        public OcsDisplay Display { get; }

        public AmigaBlitter Blitter { get; }

        public IAmigaBusArbiter Arbiter { get; }

        public AmigaCia CiaA { get; }

        public AmigaCia CiaB { get; }

        public bool AudioFilterEnabled { get; private set; }

        public bool GamePort0FirePressed { get; set; }

        public bool GamePort1FirePressed { get; set; }

        public byte[] ChipRam => _chipRam;

        public IReadOnlyList<CustomRegisterWrite> CustomRegisterWrites => Paula.Writes;

        public IReadOnlyList<AmigaBusAccessResult> BusAccesses => _busAccesses;

        public long CiaBTimerAIntervalCycles => CiaB.TimerAIntervalCycles;

        public void Reset()
        {
            Array.Clear(_chipRam);
            Array.Clear(_pendingCustomBytes);
            Array.Clear(_pendingCustomByteWritten);
            _hostCallbacks.Clear();
            _readOnlyRegions.Clear();
            _pendingCiaInterrupts.Clear();
            _busAccesses.Clear();
            CiaA.Reset(0x02);
            CiaB.Reset();
            AudioFilterEnabled = false;
            GamePort0FirePressed = false;
            GamePort1FirePressed = false;
            Paula.Reset();
            Disk.Reset();
            Display.Reset();
            Blitter.Reset();
            _nextVerticalBlankCycle = _palFrameCycles;
        }

        public void RegisterHostCallback(uint address, Action<M68kCpuState> callback)
        {
            _hostCallbacks[address] = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        public bool TryInvokeHost(uint address, M68kCpuState state)
        {
            address = NormalizeAddress(address);
            if (!_hostCallbacks.TryGetValue(address, out var callback))
            {
                return false;
            }

            var access = Arbitrate(
                AmigaBusRequester.Cpu,
                AmigaBusAccessKind.HostTrap,
                AmigaBusAccessTarget.HostTrap,
                address,
                AmigaBusAccessSize.Word,
                state.Cycles,
                isWrite: false);
            state.Cycles = access.CompletedCycle;
            callback(state);
            return true;
        }

        public void MapReadOnlyMemory(uint baseAddress, ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty)
            {
                throw new ArgumentException("Mapped memory cannot be empty.", nameof(data));
            }

            var copy = data.ToArray();
            _readOnlyRegions.Add(new MappedMemoryRegion(baseAddress, copy));
        }

        public byte ReadByte(uint address)
        {
            var cycle = 0L;
            return ReadByte(address, ref cycle, AmigaBusAccessKind.CpuDataRead);
        }

        public byte ReadByte(uint address, ref long cycle, AmigaBusAccessKind accessKind)
        {
            address = NormalizeAddress(address);
            var target = ClassifyTarget(address);
            var access = Arbitrate(AmigaBusRequester.Cpu, accessKind, target, address, AmigaBusAccessSize.Byte, cycle, isWrite: false);
            var value = ReadRawByte(address);
            cycle = access.CompletedCycle;
            return value;
        }

        public void WriteByte(uint address, byte value, long cycle)
        {
            WriteByte(address, value, ref cycle, AmigaBusAccessKind.CpuDataWrite);
        }

        public void WriteByte(uint address, byte value, ref long cycle, AmigaBusAccessKind accessKind)
        {
            address = NormalizeAddress(address);
            var target = ClassifyTarget(address);
            var access = Arbitrate(AmigaBusRequester.Cpu, accessKind, target, address, AmigaBusAccessSize.Byte, cycle, isWrite: true);
            WriteRawByte(address, value, access.GrantedCycle);
            cycle = access.CompletedCycle;
        }

        public void WriteWord(uint address, ushort value, long cycle)
        {
            WriteWord(address, value, ref cycle, AmigaBusAccessKind.CpuDataWrite);
        }

        public void WriteWord(uint address, ushort value, ref long cycle, AmigaBusAccessKind accessKind)
        {
            address = NormalizeAddress(address);
            var target = ClassifyTarget(address);
            var access = Arbitrate(AmigaBusRequester.Cpu, accessKind, target, address, AmigaBusAccessSize.Word, cycle, isWrite: true);
            WriteRawWord(address, value, access.GrantedCycle);
            cycle = access.CompletedCycle;
        }

        public ushort ReadWord(uint address)
        {
            var cycle = 0L;
            return ReadWord(address, ref cycle, AmigaBusAccessKind.CpuDataRead);
        }

        public ushort ReadWord(uint address, ref long cycle, AmigaBusAccessKind accessKind)
        {
            address = NormalizeAddress(address);
            var target = ClassifyTarget(address);
            var access = Arbitrate(AmigaBusRequester.Cpu, accessKind, target, address, AmigaBusAccessSize.Word, cycle, isWrite: false);
            var value = ReadRawWord(address);
            cycle = access.CompletedCycle;
            return value;
        }

        public uint ReadLong(uint address)
        {
            var cycle = 0L;
            return ReadLong(address, ref cycle, AmigaBusAccessKind.CpuDataRead);
        }

        public uint ReadLong(uint address, ref long cycle, AmigaBusAccessKind accessKind)
        {
            address = NormalizeAddress(address);
            var target = ClassifyTarget(address);
            var access = Arbitrate(AmigaBusRequester.Cpu, accessKind, target, address, AmigaBusAccessSize.Long, cycle, isWrite: false);
            var value = ReadRawLong(address);
            cycle = access.CompletedCycle;
            return value;
        }

        public void WriteWord(uint address, ushort value)
        {
            WriteWord(address, value, 0);
        }

        public void WriteLong(uint address, uint value, long cycle = 0)
        {
            WriteLong(address, value, ref cycle, AmigaBusAccessKind.CpuDataWrite);
        }

        public void WriteLong(uint address, uint value, ref long cycle, AmigaBusAccessKind accessKind)
        {
            address = NormalizeAddress(address);
            var target = ClassifyTarget(address);
            var access = Arbitrate(AmigaBusRequester.Cpu, accessKind, target, address, AmigaBusAccessSize.Long, cycle, isWrite: true);
            WriteRawLong(address, value, access.GrantedCycle);
            cycle = access.CompletedCycle;
        }

        public void CopyToChipRam(uint address, ReadOnlySpan<byte> data)
        {
            if (address + data.Length > _chipRam.Length)
            {
                throw new AmigaEmulationException("Load data does not fit in the emulated chip RAM map.");
            }

            data.CopyTo(_chipRam.AsSpan((int)address, data.Length));
        }

        public PaulaDmaReadResult ReadPaulaDmaWord(uint address, long requestedCycle)
        {
            address = NormalizeAddress(address);
            var target = ClassifyTarget(address);
            var access = Arbitrate(
                AmigaBusRequester.Paula,
                AmigaBusAccessKind.PaulaDma,
                target,
                address,
                AmigaBusAccessSize.Word,
                requestedCycle,
                isWrite: false);
            return new PaulaDmaReadResult(ReadRawWord(address), access);
        }

        public ushort ReadChipWordForDevice(AmigaBusRequester requester, AmigaBusAccessKind kind, uint address, long requestedCycle)
        {
            address = NormalizeAddress(address);
            var access = Arbitrate(requester, kind, ClassifyTarget(address), address, AmigaBusAccessSize.Word, requestedCycle, isWrite: false);
            return ReadRawWord(address);
        }

        public void WriteChipWordForDevice(AmigaBusRequester requester, AmigaBusAccessKind kind, uint address, ushort value, long requestedCycle)
        {
            address = NormalizeAddress(address);
            var access = Arbitrate(requester, kind, ClassifyTarget(address), address, AmigaBusAccessSize.Word, requestedCycle, isWrite: true);
            WriteRawByte(address, (byte)(value >> 8), access.GrantedCycle);
            WriteRawByte(address + 1, (byte)value, access.GrantedCycle);
        }

        public void WriteDeviceWord(AmigaBusRequester requester, AmigaBusAccessKind kind, uint address, ushort value, long requestedCycle)
        {
            address = NormalizeAddress(address);
            var access = Arbitrate(requester, kind, ClassifyTarget(address), address, AmigaBusAccessSize.Word, requestedCycle, isWrite: true);
            WriteRawWord(address, value, access.GrantedCycle);
        }

        public void AdvanceRasterTo(long targetCycle)
        {
            UpdateBeamPosition(targetCycle);
            if (targetCycle < _nextVerticalBlankCycle)
            {
                return;
            }

            while (_nextVerticalBlankCycle <= targetCycle)
            {
                WriteDeviceWord(
                    AmigaBusRequester.Bitplane,
                    AmigaBusAccessKind.CustomRegister,
                    0x00DFF09C,
                    (ushort)(0x8000 | AmigaConstants.IntreqVerticalBlank),
                    _nextVerticalBlankCycle);
                _nextVerticalBlankCycle += _palFrameCycles;
            }
        }

        private void UpdateBeamPosition(long targetCycle)
        {
            var cycleInFrame = Math.Max(0, targetCycle) % _palFrameCycles;
            var line = Math.Clamp((int)(cycleInFrame / _palLineCycles), 0, AmigaConstants.A500PalRasterLines - 1);
            var lineCycle = cycleInFrame - (long)(line * _palLineCycles);
            var horizontal = Math.Clamp((int)(lineCycle * 0xE2 / _palLineCycles), 0, 0xE2);
            var frame = Math.Max(0, targetCycle) / _palFrameCycles;
            Paula.SetBeamPosition(line, horizontal, (frame & 1) != 0);
        }

        public void AdvanceCiasTo(long targetCycle)
        {
            CiaA.AdvanceTo(targetCycle, _pendingCiaInterrupts);
            CiaB.AdvanceTo(targetCycle, _pendingCiaInterrupts);
        }

        public long? GetNextCiaInterruptCycle(long maxCycle)
        {
            var ciaA = CiaA.GetNextInterruptCycle(maxCycle);
            var ciaB = CiaB.GetNextInterruptCycle(maxCycle);
            if (!ciaA.HasValue)
            {
                return ciaB;
            }

            if (!ciaB.HasValue)
            {
                return ciaA;
            }

            return Math.Min(ciaA.Value, ciaB.Value);
        }

        public AmigaCia GetCia(AmigaCiaId id)
        {
            return id == AmigaCiaId.A ? CiaA : CiaB;
        }

        public byte AbleCiaInterrupts(AmigaCiaId id, byte value, long cycle)
        {
            return GetCia(id).AbleInterrupts(value, cycle, _pendingCiaInterrupts);
        }

        public byte SetCiaInterrupts(AmigaCiaId id, byte value, long cycle)
        {
            return GetCia(id).SetInterrupts(value, cycle, _pendingCiaInterrupts);
        }

        public IReadOnlyList<AmigaCiaInterruptEvent> DrainCiaInterrupts()
        {
            if (_pendingCiaInterrupts.Count == 0)
            {
                return Array.Empty<AmigaCiaInterruptEvent>();
            }

            var result = _pendingCiaInterrupts.ToArray();
            Array.Sort(
                result,
                static (left, right) =>
                {
                    var cycleCompare = left.Cycle.CompareTo(right.Cycle);
                    return cycleCompare != 0 ? cycleCompare : left.Cia.CompareTo(right.Cia);
                });
            _pendingCiaInterrupts.Clear();
            return result;
        }

        private AmigaBusAccessResult Arbitrate(
            AmigaBusRequester requester,
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite)
        {
            var request = new AmigaBusAccessRequest(
                requester,
                kind,
                target,
                address,
                size,
                Math.Max(0, requestedCycle),
                isWrite);
            var result = Arbiter.Arbitrate(request);
            _busAccesses.Add(result);
            return result;
        }

        private AmigaBusAccessTarget ClassifyTarget(uint address)
        {
            if (address < _chipRam.Length)
            {
                return AmigaBusAccessTarget.ChipRam;
            }

            if (address >= 0x00DFF000 && address < 0x00DFF200)
            {
                return AmigaBusAccessTarget.CustomRegisters;
            }

            if (TryGetCiaRegister(address, out _, out _))
            {
                return AmigaBusAccessTarget.Cia;
            }

            if (_hostCallbacks.ContainsKey(address))
            {
                return AmigaBusAccessTarget.HostTrap;
            }

            for (var i = _readOnlyRegions.Count - 1; i >= 0; i--)
            {
                if (_readOnlyRegions[i].Contains(address))
                {
                    return AmigaBusAccessTarget.Rom;
                }
            }

            return AmigaBusAccessTarget.Unmapped;
        }

        private byte ReadRawByte(uint address)
        {
            address = NormalizeAddress(address);
            if (address < _chipRam.Length)
            {
                return _chipRam[address];
            }

            if (address >= 0x00DFF000 && address < 0x00DFF200)
            {
                var offset = (ushort)(address - 0x00DFF000);
                var diskValue = Disk.ReadByte(offset);
                return diskValue != 0 ? diskValue : Paula.ReadByte(offset);
            }

            if (TryGetCiaRegister(address, out var cia, out var ciaRegister))
            {
                var value = cia.ReadRegister(ciaRegister);
                if (cia == CiaA && ciaRegister == 0)
                {
                    value = Disk.ReadCiaAPortA(value);
                    value = ApplyGamePortFireBits(value);
                }

                return value;
            }

            for (var i = _readOnlyRegions.Count - 1; i >= 0; i--)
            {
                if (_readOnlyRegions[i].TryReadByte(address, out var value))
                {
                    return value;
                }
            }

            return 0;
        }

        private ushort ReadRawWord(uint address)
        {
            return (ushort)((ReadRawByte(address) << 8) | ReadRawByte(address + 1));
        }

        private uint ReadRawLong(uint address)
        {
            return ((uint)ReadRawWord(address) << 16) | ReadRawWord(address + 2);
        }

        private void WriteRawByte(uint address, byte value, long grantedCycle)
        {
            address = NormalizeAddress(address);
            if (address < _chipRam.Length)
            {
                _chipRam[address] = value;
                return;
            }

            if (address >= 0x00DFF000 && address < 0x00DFF200)
            {
                WriteCustomByte((ushort)(address - 0x00DFF000), value, grantedCycle);
                return;
            }

            if (TryGetCiaRegister(address, out var cia, out var ciaRegister))
            {
                cia.WriteRegister(ciaRegister, value, grantedCycle, _pendingCiaInterrupts);
                if (cia == CiaA && ciaRegister == 0)
                {
                    AudioFilterEnabled = (value & 0x02) == 0;
                }

                if (cia == CiaB)
                {
                    Disk.WriteCiaBRegister(ciaRegister, value);
                }
            }
        }

        private void WriteRawWord(uint address, ushort value, long grantedCycle)
        {
            address = NormalizeAddress(address);
            if (address >= 0x00DFF000 && address + 1 < 0x00DFF200)
            {
                Paula.ScheduleWrite(grantedCycle, (ushort)(address - 0x00DFF000), value);
                Display.ScheduleWrite(grantedCycle, (ushort)(address - 0x00DFF000), value);
                Blitter.WriteRegister((ushort)(address - 0x00DFF000), value, grantedCycle);
                Disk.WriteRegister((ushort)(address - 0x00DFF000), value, grantedCycle);
                return;
            }

            WriteRawByte(address, (byte)(value >> 8), grantedCycle);
            WriteRawByte(address + 1, (byte)value, grantedCycle);
        }

        private void WriteRawLong(uint address, uint value, long grantedCycle)
        {
            WriteRawWord(address, (ushort)(value >> 16), grantedCycle);
            WriteRawWord(address + 2, (ushort)value, grantedCycle);
        }

        private void WriteCustomByte(ushort offset, byte value, long cycle)
        {
            var wordOffset = (ushort)(offset & 0x01FE);
            _pendingCustomBytes[offset] = value;
            _pendingCustomByteWritten[offset] = true;

            var highIndex = wordOffset;
            var lowIndex = wordOffset + 1;
            var high = _pendingCustomByteWritten[highIndex]
                ? _pendingCustomBytes[highIndex]
                : Paula.ReadByte((ushort)highIndex);
            var low = _pendingCustomByteWritten[lowIndex]
                ? _pendingCustomBytes[lowIndex]
                : Paula.ReadByte((ushort)lowIndex);
            var wordValue = (ushort)((high << 8) | low);
            Paula.ScheduleWrite(cycle, wordOffset, wordValue);
            Display.ScheduleWrite(cycle, wordOffset, wordValue);
            Blitter.WriteRegister(wordOffset, wordValue, cycle);
            Disk.WriteRegister(wordOffset, wordValue, cycle);

            _pendingCustomByteWritten[highIndex] = false;
            _pendingCustomByteWritten[lowIndex] = false;
        }

        private static uint NormalizeAddress(uint address)
        {
            return address & 0x00FF_FFFF;
        }

        private byte ApplyGamePortFireBits(byte value)
        {
            if (GamePort0FirePressed)
            {
                value = (byte)(value & ~0x40);
            }
            else
            {
                value = (byte)(value | 0x40);
            }

            if (GamePort1FirePressed)
            {
                value = (byte)(value & ~0x80);
            }
            else
            {
                value = (byte)(value | 0x80);
            }

            return value;
        }

        private bool TryGetCiaRegister(uint address, out AmigaCia cia, out int register)
        {
            if (address >= 0x00BFE001 && address <= 0x00BFEF01 && (address & 0xFF) == 0x01)
            {
                cia = CiaA;
                register = (int)((address >> 8) & 0x0F);
                return true;
            }

            if (address >= 0x00BFD000 && address <= 0x00BFDF00 && (address & 0xFF) == 0x00)
            {
                cia = CiaB;
                register = (int)((address >> 8) & 0x0F);
                return true;
            }

            cia = null!;
            register = 0;
            return false;
        }

        private sealed class MappedMemoryRegion
        {
            private readonly byte[] _data;

            public MappedMemoryRegion(uint baseAddress, byte[] data)
            {
                BaseAddress = baseAddress;
                _data = data ?? throw new ArgumentNullException(nameof(data));
            }

            public uint BaseAddress { get; }

            public bool Contains(uint address)
            {
                var offset = address - BaseAddress;
                return address >= BaseAddress && offset < _data.Length;
            }

            public bool TryReadByte(uint address, out byte value)
            {
                var offset = address - BaseAddress;
                if (address < BaseAddress || offset >= _data.Length)
                {
                    value = 0;
                    return false;
                }

                value = _data[offset];
                return true;
            }
        }
    }

    internal sealed class Paula
    {
        private const ushort IntenaMasterEnable = 0x4000;
        private const ushort AudioInterruptMask = 0x0780;
        private const float VoiceScale = 0.25f;
        private const int MaxCapturedWrites = 65536;
        private static readonly double CpuCyclesPerPaulaTick = AmigaConstants.A500PalCpuClockHz / AmigaConstants.A500PalPaulaClockHz;
        private readonly AmigaBus _bus;
        private readonly PaulaChannel[] _channels = new PaulaChannel[AmigaConstants.PaulaChannelCount];
        private readonly List<PendingWrite> _pendingWrites = new List<PendingWrite>();
        private readonly List<PaulaInterruptEvent> _pendingInterrupts = new List<PaulaInterruptEvent>();
        private readonly BoundedWriteLog _writes = new BoundedWriteLog(MaxCapturedWrites);
        private readonly byte[] _registerBytes = new byte[0x200];
        private int _pendingWriteIndex;
        private ushort _adkcon;
        private ushort _dmacon;
        private ushort _intena;
        private ushort _intreq;
        private ushort _vposr;
        private ushort _vhposr;
        private ushort _lastAudioDmaMask;
        private long _lastCycle;
        private float[][]? _captureSamples;
        private int _captureFrameIndex;
        private int _captureSampleRate;

        public Paula(AmigaBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            for (var i = 0; i < _channels.Length; i++)
            {
                _channels[i] = new PaulaChannel(i);
            }
        }

        public IReadOnlyList<CustomRegisterWrite> Writes => _writes;

        public ushort ActiveInterruptBits
        {
            get
            {
                if ((_intena & IntenaMasterEnable) == 0)
                {
                    return 0;
                }

                return (ushort)(_intreq & _intena & 0x3FFF);
            }
        }

        public void Reset()
        {
            Array.Clear(_registerBytes);
            _pendingWrites.Clear();
            _pendingInterrupts.Clear();
            _writes.Clear();
            _pendingWriteIndex = 0;
            _adkcon = 0;
            _dmacon = 0;
            _intena = 0;
            _intreq = 0;
            _vposr = 0;
            _vhposr = 0;
            _lastAudioDmaMask = 0;
            _lastCycle = 0;
            _captureSamples = null;
            _captureFrameIndex = 0;
            _captureSampleRate = 0;
            foreach (var channel in _channels)
            {
                channel.Reset();
            }
        }

        public byte ReadByte(ushort offset)
        {
            if (offset == 0x004)
            {
                return (byte)(_vposr >> 8);
            }

            if (offset == 0x005)
            {
                return (byte)_vposr;
            }

            if (offset == 0x010)
            {
                return (byte)(_adkcon >> 8);
            }

            if (offset == 0x011)
            {
                return (byte)_adkcon;
            }

            if (offset == 0x002)
            {
                return (byte)(_dmacon >> 8);
            }

            if (offset == 0x003)
            {
                return (byte)_dmacon;
            }

            if (offset == 0x006)
            {
                return (byte)(_vhposr >> 8);
            }

            if (offset == 0x007)
            {
                return (byte)_vhposr;
            }

            if (offset == 0x01C)
            {
                return (byte)(_intena >> 8);
            }

            if (offset == 0x01D)
            {
                return (byte)_intena;
            }

            if (offset == 0x01E)
            {
                return (byte)(_intreq >> 8);
            }

            if (offset == 0x01F)
            {
                return (byte)_intreq;
            }

            return offset < _registerBytes.Length ? _registerBytes[offset] : (byte)0;
        }

        public void SetBeamPosition(int verticalLine, int horizontalPosition, bool longFrame)
        {
            verticalLine = Math.Clamp(verticalLine, 0, 0x1FF);
            horizontalPosition = Math.Clamp(horizontalPosition, 0, 0xFF);
            _vposr = (ushort)(((longFrame ? 1 : 0) << 15) | ((verticalLine >> 8) & 0x0001));
            _vhposr = (ushort)(((verticalLine & 0x00FF) << 8) | horizontalPosition);
        }

        public void ScheduleWrite(long cycle, ushort offset, ushort value)
        {
            offset = (ushort)(offset & 0x01FE);
            _writes.Add(new CustomRegisterWrite(cycle, offset, value));
            _pendingWrites.Add(new PendingWrite(cycle, offset, value));
        }

        public IReadOnlyList<PaulaInterruptEvent> DrainInterrupts()
        {
            if (_pendingInterrupts.Count == 0)
            {
                return Array.Empty<PaulaInterruptEvent>();
            }

            var result = _pendingInterrupts.ToArray();
            Array.Sort(
                result,
                static (left, right) =>
                {
                    var cycleCompare = left.Cycle.CompareTo(right.Cycle);
                    return cycleCompare != 0 ? cycleCompare : left.Channel.CompareTo(right.Channel);
                });
            _pendingInterrupts.Clear();
            return result;
        }

        public int GetHighestPendingInterruptLevel()
        {
            var active = ActiveInterruptBits;
            if ((active & 0x2000) != 0)
            {
                return 6;
            }

            if ((active & 0x1800) != 0)
            {
                return 5;
            }

            if ((active & 0x0780) != 0)
            {
                return 4;
            }

            if ((active & 0x0070) != 0)
            {
                return 3;
            }

            if ((active & 0x0008) != 0)
            {
                return 2;
            }

            return (active & 0x0007) != 0 ? 1 : 0;
        }

        public PaulaChannelSnapshot GetChannelSnapshot(int channel)
        {
            if ((uint)channel >= _channels.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(channel), channel, "Paula channel index is outside the supported range.");
            }

            return _channels[channel].GetSnapshot();
        }

        public void BeginChannelCapture(int frames, int sampleRate)
        {
            if (frames <= 0 || sampleRate <= 0)
            {
                _captureSamples = null;
                _captureFrameIndex = 0;
                _captureSampleRate = 0;
                return;
            }

            _captureSamples = new float[_channels.Length][];
            for (var i = 0; i < _captureSamples.Length; i++)
            {
                _captureSamples[i] = new float[frames];
            }

            _captureFrameIndex = 0;
            _captureSampleRate = sampleRate;
        }

        public AmigaChannelWaveform? FinishChannelCapture()
        {
            if (_captureSamples == null)
            {
                return null;
            }

            var channels = new AmigaChannelWaveformChannel[_captureSamples.Length];
            for (var i = 0; i < channels.Length; i++)
            {
                channels[i] = new AmigaChannelWaveformChannel(i, _captureSamples[i], IsActive(_captureSamples[i]));
            }

            var result = new AmigaChannelWaveform(channels, _captureFrameIndex, _captureSampleRate);
            _captureSamples = null;
            _captureFrameIndex = 0;
            _captureSampleRate = 0;
            return result;
        }

        public void RenderSample(long targetCycle, Span<float> destination, int frame, int channels)
        {
            AdvanceTo(targetCycle);
            var left = 0.0f;
            var right = 0.0f;
            var capture = _captureSamples;
            for (var i = 0; i < _channels.Length; i++)
            {
                if (IsAttachedSource(i))
                {
                    if (capture != null && _captureFrameIndex < capture[i].Length)
                    {
                        capture[i][_captureFrameIndex] = 0.0f;
                    }

                    continue;
                }

                var raw = _channels[i].CurrentSample / 128.0f;
                var sample = raw * (_channels[i].Volume / 64.0f) * VoiceScale;
                if (capture != null && _captureFrameIndex < capture[i].Length)
                {
                    capture[i][_captureFrameIndex] = sample;
                }

                if (i == 0 || i == 3)
                {
                    left += sample;
                }
                else
                {
                    right += sample;
                }
            }

            var offset = frame * channels;
            if (channels == 1)
            {
                destination[offset] = Math.Clamp((left + right) * 0.5f, -1.0f, 1.0f);
            }
            else
            {
                destination[offset] = Math.Clamp(left, -1.0f, 1.0f);
                destination[offset + 1] = Math.Clamp(right, -1.0f, 1.0f);
                for (var extra = 2; extra < channels; extra++)
                {
                    destination[offset + extra] = Math.Clamp((left + right) * 0.5f, -1.0f, 1.0f);
                }
            }

            if (capture != null)
            {
                _captureFrameIndex++;
            }
        }

        public void AdvanceTo(long targetCycle)
        {
            if (targetCycle < _lastCycle)
            {
                return;
            }

            while (_pendingWriteIndex < _pendingWrites.Count && _pendingWrites[_pendingWriteIndex].Cycle <= targetCycle)
            {
                var write = _pendingWrites[_pendingWriteIndex++];
                AdvanceChannels(write.Cycle - _lastCycle);
                ApplyWrite(write.Offset, write.Value);
            }

            AdvanceChannels(targetCycle - _lastCycle);
            CompactPendingWrites();
        }

        private void AdvanceChannels(long cpuCycles)
        {
            if (cpuCycles <= 0)
            {
                return;
            }

            var targetCycle = _lastCycle + cpuCycles;
            foreach (var channel in _channels)
            {
                channel.AdvanceTo(targetCycle, _bus, this);
            }

            _lastCycle = targetCycle;
        }

        private void ApplyWrite(ushort offset, ushort value)
        {
            if (offset + 1 < _registerBytes.Length)
            {
                _registerBytes[offset] = (byte)(value >> 8);
                _registerBytes[offset + 1] = (byte)value;
            }

            if (offset == 0x096)
            {
                var old = _dmacon;
                var mask = (ushort)(value & 0x7FFF);
                var audioMask = (ushort)(mask & 0x000F);
                if (audioMask != 0)
                {
                    _lastAudioDmaMask = audioMask;
                }

                if ((value & 0x8000) != 0)
                {
                    if (mask == 0 && _lastAudioDmaMask != 0)
                    {
                        mask = _lastAudioDmaMask;
                    }

                    _dmacon |= mask;
                }
                else
                {
                    _dmacon &= (ushort)~mask;
                }

                WriteRegisterWord(0x002, _dmacon);

                for (var i = 0; i < _channels.Length; i++)
                {
                    var bit = (ushort)(1 << i);
                    var wasEnabled = (old & bit) != 0;
                    var enabled = (_dmacon & bit) != 0;
                    if (enabled && !wasEnabled)
                    {
                        _channels[i].SetDmaEnabled(true, _lastCycle, _bus, this);
                    }
                    else if (!enabled && wasEnabled)
                    {
                        _channels[i].SetDmaEnabled(false, _lastCycle, _bus, this);
                    }
                }

                return;
            }

            if (offset == 0x09A)
            {
                ApplySetClear(ref _intena, value);
                WriteRegisterWord(0x01C, _intena);
                QueuePendingEnabledAudioInterrupts(_lastCycle);
                return;
            }

            if (offset == 0x09C)
            {
                var mask = (ushort)(value & 0x7FFF);
                if ((value & 0x8000) != 0)
                {
                    _intreq |= mask;
                    QueuePendingEnabledAudioInterrupts(_lastCycle, mask);
                }
                else
                {
                    _intreq &= (ushort)~mask;
                }

                WriteRegisterWord(0x01E, _intreq);

                return;
            }

            if (offset == 0x09E)
            {
                ApplySetClear(ref _adkcon, value);
                WriteRegisterWord(0x010, _adkcon);
                return;
            }

            var channelIndex = GetAudioChannelIndex(offset);
            if (channelIndex < 0)
            {
                return;
            }

            var channel = _channels[channelIndex];
            var register = offset - (0x0A0 + (channelIndex * 0x10));
            switch (register)
            {
                case 0x00:
                    channel.Location = (channel.Location & 0x0000_FFFF) | ((uint)value << 16);
                    break;
                case 0x02:
                    channel.Location = (channel.Location & 0xFFFF_0000) | value;
                    break;
                case 0x04:
                    channel.LengthWords = Math.Max(1, (int)value);
                    break;
                case 0x06:
                    channel.Period = ClampPeriod(value);
                    break;
                case 0x08:
                    var volume = value & 0x7F;
                    if (volume == 0 && (value & 0x7F00) != 0)
                    {
                        volume = (value >> 8) & 0x7F;
                    }

                    channel.Volume = Math.Min(64, volume);
                    break;
                case 0x0A:
                    channel.WriteData(value, _lastCycle, this);
                    break;
            }
        }

        private static int GetAudioChannelIndex(ushort offset)
        {
            if (offset < 0x0A0 || offset > 0x0DA)
            {
                return -1;
            }

            var channel = (offset - 0x0A0) / 0x10;
            var register = (offset - 0x0A0) % 0x10;
            return channel < AmigaConstants.PaulaChannelCount && register <= 0x0A ? channel : -1;
        }

        private void RequestAudioInterrupt(int channel, long cycle)
        {
            if ((uint)channel < AmigaConstants.PaulaChannelCount)
            {
                var bit = GetAudioInterruptBit(channel);
                _intreq |= bit;
                WriteRegisterWord(0x01E, _intreq);
                if (IsInterruptEnabled(bit))
                {
                    _pendingInterrupts.Add(new PaulaInterruptEvent(channel, bit, cycle));
                }
            }
        }

        private void ApplyModulationFrom(int sourceChannel, ushort value)
        {
            var targetChannel = sourceChannel + 1;
            if (sourceChannel < 0 || targetChannel >= _channels.Length)
            {
                return;
            }

            if (IsVolumeAttached(sourceChannel))
            {
                _channels[targetChannel].Volume = Math.Min(64, value & 0x7F);
            }

            if (IsPeriodAttached(sourceChannel))
            {
                _channels[targetChannel].Period = ClampPeriod(value);
            }
        }

        private bool IsAttachedSource(int channel)
        {
            return IsVolumeAttached(channel) || IsPeriodAttached(channel);
        }

        private bool IsVolumeAttached(int sourceChannel)
        {
            return sourceChannel is >= 0 and <= 2 && (_adkcon & (1 << sourceChannel)) != 0;
        }

        private bool IsPeriodAttached(int sourceChannel)
        {
            return sourceChannel is >= 0 and <= 2 && (_adkcon & (1 << (sourceChannel + 4))) != 0;
        }

        private bool IsInterruptEnabled(ushort bit)
        {
            return (_intena & IntenaMasterEnable) != 0 && (_intena & bit) != 0;
        }

        private void QueuePendingEnabledAudioInterrupts(long cycle, ushort mask = AudioInterruptMask)
        {
            var active = (ushort)(_intreq & _intena & mask & AudioInterruptMask);
            if ((_intena & IntenaMasterEnable) == 0 || active == 0)
            {
                return;
            }

            for (var channel = 0; channel < _channels.Length; channel++)
            {
                var bit = GetAudioInterruptBit(channel);
                if ((active & bit) != 0)
                {
                    _pendingInterrupts.Add(new PaulaInterruptEvent(channel, bit, cycle));
                }
            }
        }

        private void WriteRegisterWord(ushort offset, ushort value)
        {
            if (offset + 1 >= _registerBytes.Length)
            {
                return;
            }

            _registerBytes[offset] = (byte)(value >> 8);
            _registerBytes[offset + 1] = (byte)value;
        }

        private static void ApplySetClear(ref ushort register, ushort value)
        {
            var mask = (ushort)(value & 0x7FFF);
            if ((value & 0x8000) != 0)
            {
                register |= mask;
            }
            else
            {
                register &= (ushort)~mask;
            }
        }

        private static ushort GetAudioInterruptBit(int channel)
        {
            return (ushort)(0x0080 << channel);
        }

        private static int ClampPeriod(ushort value)
        {
            return Math.Max(1, (int)value);
        }

        private static double GetPeriodCycles(int period)
        {
            return Math.Max(1, period) * CpuCyclesPerPaulaTick;
        }

        private void CompactPendingWrites()
        {
            if (_pendingWriteIndex < 64 || _pendingWriteIndex * 2 < _pendingWrites.Count)
            {
                return;
            }

            _pendingWrites.RemoveRange(0, _pendingWriteIndex);
            _pendingWriteIndex = 0;
        }

        private static bool IsActive(ReadOnlySpan<float> samples)
        {
            for (var i = 0; i < samples.Length; i++)
            {
                if (Math.Abs(samples[i]) > 0.001f)
                {
                    return true;
                }
            }

            return false;
        }

        private readonly struct PendingWrite
        {
            public PendingWrite(long cycle, ushort offset, ushort value)
            {
                Cycle = cycle;
                Offset = offset;
                Value = value;
            }

            public long Cycle { get; }

            public ushort Offset { get; }

            public ushort Value { get; }
        }

        private sealed class PaulaChannel
        {
            private ushort _dataWord;
            private bool _hasDataWord;
            private bool _nextByteIsLow;
            private double _nextSampleCycle;
            private uint _currentAddress;
            private int _remainingWords;

            public PaulaChannel(int index)
            {
                Index = index;
                Reset();
            }

            public int Index { get; }

            public uint Location { get; set; }

            public int LengthWords { get; set; }

            public int Period { get; set; }

            public int Volume { get; set; }

            public sbyte CurrentSample { get; set; }

            public bool DmaEnabled { get; set; }

            public void Reset()
            {
                Location = 0;
                LengthWords = 1;
                Period = 428;
                Volume = 64;
                CurrentSample = 0;
                DmaEnabled = false;
                _dataWord = 0;
                _hasDataWord = false;
                _nextByteIsLow = false;
                _nextSampleCycle = 0;
                _currentAddress = 0;
                _remainingWords = 0;
            }

            public void SetDmaEnabled(bool enabled, long cycle, AmigaBus bus, Paula paula)
            {
                if (!enabled)
                {
                    DmaEnabled = false;
                    _hasDataWord = false;
                    _nextByteIsLow = false;
                    return;
                }

                DmaEnabled = true;
                _currentAddress = Location;
                _remainingWords = Math.Max(1, LengthWords);
                FetchDmaWord(bus, cycle, paula, forceInterrupt: true);
                _nextSampleCycle = cycle + GetPeriodCycles(Period);
            }

            public void WriteData(ushort value, long cycle, Paula paula)
            {
                _dataWord = value;
                _hasDataWord = true;
                _nextByteIsLow = true;
                CurrentSample = unchecked((sbyte)(value >> 8));
                _nextSampleCycle = cycle + GetPeriodCycles(Period);
                paula.RequestAudioInterrupt(Index, cycle);
            }

            public void AdvanceTo(long targetCycle, AmigaBus bus, Paula paula)
            {
                if (!_hasDataWord && !DmaEnabled)
                {
                    return;
                }

                while (_nextSampleCycle <= targetCycle)
                {
                    if (_hasDataWord && _nextByteIsLow)
                    {
                        CurrentSample = unchecked((sbyte)_dataWord);
                        _nextByteIsLow = false;
                        paula.ApplyModulationFrom(Index, _dataWord);
                        _nextSampleCycle += GetPeriodCycles(Period);
                        continue;
                    }

                    if (DmaEnabled)
                    {
                        FetchDmaWord(bus, (long)Math.Round(_nextSampleCycle), paula, forceInterrupt: false);
                        _nextSampleCycle += GetPeriodCycles(Period);
                    }
                    else
                    {
                        _hasDataWord = false;
                        break;
                    }
                }
            }

            public PaulaChannelSnapshot GetSnapshot()
            {
                return new PaulaChannelSnapshot(
                    Index,
                    Location,
                    _currentAddress,
                    LengthWords,
                    _remainingWords,
                    Period,
                    Volume,
                    CurrentSample,
                    DmaEnabled,
                    _dataWord,
                    _hasDataWord,
                    _nextByteIsLow);
            }

            private void FetchDmaWord(AmigaBus bus, long cycle, Paula paula, bool forceInterrupt)
            {
                if (_remainingWords <= 0)
                {
                    _currentAddress = Location;
                    _remainingWords = Math.Max(1, LengthWords);
                    paula.RequestAudioInterrupt(Index, cycle);
                }

                var dmaRead = bus.ReadPaulaDmaWord(_currentAddress, cycle);
                _dataWord = dmaRead.Value;
                _currentAddress += 2;
                _remainingWords--;
                _hasDataWord = true;
                _nextByteIsLow = true;
                CurrentSample = unchecked((sbyte)(_dataWord >> 8));

                if (forceInterrupt || _remainingWords == 0)
                {
                    paula.RequestAudioInterrupt(Index, cycle);
                }
            }

        }
    }

    internal readonly struct PaulaInterruptEvent
    {
        public PaulaInterruptEvent(int channel, ushort intreqBit, long cycle)
        {
            Channel = channel;
            IntreqBit = intreqBit;
            Cycle = cycle;
        }

        public int Channel { get; }

        public ushort IntreqBit { get; }

        public long Cycle { get; }
    }

    internal readonly struct PaulaChannelSnapshot
    {
        public PaulaChannelSnapshot(
            int index,
            uint location,
            uint currentAddress,
            int lengthWords,
            int remainingWords,
            int period,
            int volume,
            sbyte currentSample,
            bool dmaEnabled,
            ushort dataWord,
            bool hasDataWord,
            bool nextByteIsLow)
        {
            Index = index;
            Location = location;
            CurrentAddress = currentAddress;
            LengthWords = lengthWords;
            RemainingWords = remainingWords;
            Period = period;
            Volume = volume;
            CurrentSample = currentSample;
            DmaEnabled = dmaEnabled;
            DataWord = dataWord;
            HasDataWord = hasDataWord;
            NextByteIsLow = nextByteIsLow;
        }

        public int Index { get; }

        public uint Location { get; }

        public uint CurrentAddress { get; }

        public int LengthWords { get; }

        public int RemainingWords { get; }

        public int Period { get; }

        public int Volume { get; }

        public sbyte CurrentSample { get; }

        public bool DmaEnabled { get; }

        public ushort DataWord { get; }

        public bool HasDataWord { get; }

        public bool NextByteIsLow { get; }
    }

    internal sealed class BoundedWriteLog : IReadOnlyList<CustomRegisterWrite>
    {
        private readonly CustomRegisterWrite[] _buffer;
        private int _start;
        private int _count;

        public BoundedWriteLog(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
            }

            _buffer = new CustomRegisterWrite[capacity];
        }

        public int Count => _count;

        public CustomRegisterWrite this[int index]
        {
            get
            {
                if (index < 0 || index >= _count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _buffer[(_start + index) % _buffer.Length];
            }
        }

        public void Add(CustomRegisterWrite write)
        {
            if (_count < _buffer.Length)
            {
                _buffer[(_start + _count) % _buffer.Length] = write;
                _count++;
                return;
            }

            _buffer[_start] = write;
            _start = (_start + 1) % _buffer.Length;
        }

        public void Clear()
        {
            _start = 0;
            _count = 0;
        }

        public IEnumerator<CustomRegisterWrite> GetEnumerator()
        {
            for (var i = 0; i < _count; i++)
            {
                yield return this[i];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
