using System;
using System.Collections.Generic;
using CopperMod.Abstractions;

namespace CopperMod.Cust
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
        private readonly byte[] _chipRam;
        private readonly Dictionary<uint, Action<M68kCpuState>> _hostCallbacks = new Dictionary<uint, Action<M68kCpuState>>();
        private readonly byte[] _pendingCustomBytes = new byte[0x200];
        private readonly bool[] _pendingCustomByteWritten = new bool[0x200];

        public AmigaBus(int chipRamSize = CustConstants.DefaultChipRamSize)
        {
            if (chipRamSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chipRamSize), chipRamSize, "Chip RAM size must be positive.");
            }

            _chipRam = new byte[chipRamSize];
            Paula = new Paula(this);
        }

        public Paula Paula { get; }

        public bool AudioFilterEnabled { get; private set; }

        public byte[] ChipRam => _chipRam;

        public IReadOnlyList<CustomRegisterWrite> CustomRegisterWrites => Paula.Writes;

        public void Reset()
        {
            Array.Clear(_chipRam);
            Array.Clear(_pendingCustomBytes);
            Array.Clear(_pendingCustomByteWritten);
            _hostCallbacks.Clear();
            AudioFilterEnabled = false;
            Paula.Reset();
        }

        public void RegisterHostCallback(uint address, Action<M68kCpuState> callback)
        {
            _hostCallbacks[address] = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        public bool TryInvokeHost(uint address, M68kCpuState state)
        {
            if (!_hostCallbacks.TryGetValue(address, out var callback))
            {
                return false;
            }

            callback(state);
            return true;
        }

        public byte ReadByte(uint address)
        {
            if (address < _chipRam.Length)
            {
                return _chipRam[address];
            }

            if (address >= 0x00DFF000 && address < 0x00DFF200)
            {
                return Paula.ReadByte((ushort)(address - 0x00DFF000));
            }

            if (address == 0x00BFE001)
            {
                return AudioFilterEnabled ? (byte)0x00 : (byte)0x02;
            }

            return 0;
        }

        public void WriteByte(uint address, byte value, long cycle)
        {
            if (address < _chipRam.Length)
            {
                _chipRam[address] = value;
                return;
            }

            if (address >= 0x00DFF000 && address < 0x00DFF200)
            {
                WriteCustomByte((ushort)(address - 0x00DFF000), value, cycle);
                return;
            }

            if (address == 0x00BFE001)
            {
                AudioFilterEnabled = (value & 0x02) == 0;
            }
        }

        public void WriteWord(uint address, ushort value, long cycle)
        {
            if (address < _chipRam.Length)
            {
                _chipRam[address] = (byte)(value >> 8);
                _chipRam[address + 1] = (byte)value;
                return;
            }

            if (address >= 0x00DFF000 && address + 1 < 0x00DFF200)
            {
                Paula.ScheduleWrite(cycle, (ushort)(address - 0x00DFF000), value);
                return;
            }

            WriteByte(address, (byte)(value >> 8), cycle);
            WriteByte(address + 1, (byte)value, cycle);
        }

        public ushort ReadWord(uint address)
        {
            return (ushort)((ReadByte(address) << 8) | ReadByte(address + 1));
        }

        public uint ReadLong(uint address)
        {
            return ((uint)ReadWord(address) << 16) | ReadWord(address + 2);
        }

        public void WriteWord(uint address, ushort value)
        {
            WriteWord(address, value, 0);
        }

        public void WriteLong(uint address, uint value, long cycle = 0)
        {
            WriteWord(address, (ushort)(value >> 16), cycle);
            WriteWord(address + 2, (ushort)value, cycle);
        }

        public void CopyToChipRam(uint address, ReadOnlySpan<byte> data)
        {
            if (address + data.Length > _chipRam.Length)
            {
                throw new ModuleLoadException("CUST Hunk data does not fit in the emulated chip RAM map.");
            }

            data.CopyTo(_chipRam.AsSpan((int)address, data.Length));
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
            Paula.ScheduleWrite(cycle, wordOffset, (ushort)((high << 8) | low));

            _pendingCustomByteWritten[highIndex] = false;
            _pendingCustomByteWritten[lowIndex] = false;
        }
    }

    internal sealed class Paula
    {
        private const float VoiceScale = 0.25f;
        private const int MaxCapturedWrites = 65536;
        private readonly AmigaBus _bus;
        private readonly PaulaChannel[] _channels = new PaulaChannel[CustConstants.PaulaChannelCount];
        private readonly List<PendingWrite> _pendingWrites = new List<PendingWrite>();
        private readonly BoundedWriteLog _writes = new BoundedWriteLog(MaxCapturedWrites);
        private readonly byte[] _registerBytes = new byte[0x200];
        private int _pendingWriteIndex;
        private ushort _dmacon;
        private ushort _intreq;
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

        public void Reset()
        {
            Array.Clear(_registerBytes);
            _pendingWrites.Clear();
            _writes.Clear();
            _pendingWriteIndex = 0;
            _dmacon = 0;
            _intreq = 0;
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
            if (offset == 0x006)
            {
                _vhposr += 0x0100;
                return (byte)(_vhposr >> 8);
            }

            if (offset == 0x007)
            {
                return (byte)_vhposr;
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

        public void ScheduleWrite(long cycle, ushort offset, ushort value)
        {
            offset = (ushort)(offset & 0x01FE);
            _writes.Add(new CustomRegisterWrite(cycle, offset, value));
            _pendingWrites.Add(new PendingWrite(cycle, offset, value));
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

        public ModuleChannelWaveform? FinishChannelCapture()
        {
            if (_captureSamples == null)
            {
                return null;
            }

            var channels = new ModuleChannelWaveformChannel[_captureSamples.Length];
            for (var i = 0; i < channels.Length; i++)
            {
                channels[i] = new ModuleChannelWaveformChannel(i, _captureSamples[i], IsActive(_captureSamples[i]));
            }

            var result = new ModuleChannelWaveform(channels, _captureFrameIndex, _captureSampleRate);
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

            foreach (var channel in _channels)
            {
                if (channel.Advance(_bus.ChipRam, cpuCycles))
                {
                    SetAudioInterrupt(channel.Index);
                }
            }

            _lastCycle += cpuCycles;
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

                for (var i = 0; i < _channels.Length; i++)
                {
                    var bit = (ushort)(1 << i);
                    var wasEnabled = (old & bit) != 0;
                    var enabled = (_dmacon & bit) != 0;
                    if (enabled && !wasEnabled)
                    {
                        _channels[i].StartDma();
                        SetAudioInterrupt(i);
                    }
                    else if (!enabled && wasEnabled)
                    {
                        _channels[i].DmaEnabled = false;
                    }
                }

                return;
            }

            if (offset == 0x09C)
            {
                var mask = (ushort)(value & 0x7FFF);
                if ((value & 0x8000) != 0)
                {
                    _intreq |= mask;
                }
                else
                {
                    _intreq &= (ushort)~mask;
                }

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
                    channel.Period = Math.Max(1, (int)value);
                    break;
                case 0x08:
                    channel.Volume = Math.Min(64, value & 0x7F);
                    break;
                case 0x0A:
                    channel.CurrentSample = unchecked((sbyte)(value >> 8));
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
            return channel < CustConstants.PaulaChannelCount && register <= 0x0A ? channel : -1;
        }

        private void SetAudioInterrupt(int channel)
        {
            if ((uint)channel < CustConstants.PaulaChannelCount)
            {
                _intreq |= (ushort)(0x0080 << channel);
            }
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
            private double _samplePosition;
            private uint _currentAddress;
            private uint _remainingBytes;

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
                Volume = 0;
                CurrentSample = 0;
                DmaEnabled = false;
                _samplePosition = 0;
                _currentAddress = 0;
                _remainingBytes = 0;
            }

            public void StartDma()
            {
                DmaEnabled = true;
                _currentAddress = Location;
                _remainingBytes = (uint)Math.Max(2, LengthWords * 2);
                _samplePosition = 0;
            }

            public bool Advance(byte[] memory, long cpuCycles)
            {
                if (!DmaEnabled || Period <= 0 || Volume <= 0)
                {
                    return false;
                }

                var interrupted = false;
                _samplePosition += cpuCycles / (Period * 2.0);
                while (_samplePosition >= 1.0)
                {
                    _samplePosition -= 1.0;
                    if (_remainingBytes == 0)
                    {
                        _currentAddress = Location;
                        _remainingBytes = (uint)Math.Max(2, LengthWords * 2);
                        interrupted = true;
                    }

                    CurrentSample = _currentAddress < memory.Length ? unchecked((sbyte)memory[_currentAddress]) : (sbyte)0;
                    _currentAddress++;
                    _remainingBytes--;
                }

                return interrupted;
            }
        }
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
