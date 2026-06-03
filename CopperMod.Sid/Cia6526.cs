using System;

namespace CopperMod.Sid
{
    [HotPath]
    internal sealed class Cia6526
    {
        private const byte InterruptTimerA = 0x01;
        private const byte InterruptTimerB = 0x02;
        private const byte InterruptTod = 0x04;
        private const byte InterruptSerial = 0x08;
        private const byte InterruptFlag = 0x10;
        private const byte ControlStart = 0x01;
        private const byte ControlPortOutput = 0x02;
        private const byte ControlToggleOutput = 0x04;
        private const byte ControlOneShot = 0x08;
        private const byte ControlForceLoad = 0x10;
        private const byte ControlCountCnt = 0x20;
        private const byte ControlTimerBCountTimerA = 0x40;
        private const byte ControlTod50Hz = 0x80;

        private byte _portA = 0xFF;
        private byte _portB = 0xFF;
        private byte _ddrA;
        private byte _ddrB;
        private ushort _timerALatch = 0xFFFF;
        private ushort _timerA = 0xFFFF;
        private ushort _timerBLatch = 0xFFFF;
        private ushort _timerB = 0xFFFF;
        private byte _todTenths;
        private byte _todSeconds;
        private byte _todMinutes;
        private byte _todHours = 0x01;
        private byte _alarmTenths;
        private byte _alarmSeconds;
        private byte _alarmMinutes;
        private byte _alarmHours = 0x01;
        private byte _latchedTenths;
        private byte _latchedSeconds;
        private byte _latchedMinutes;
        private byte _latchedHours;
        private bool _todLatched;
        private int _cpuCyclesPerSecond = SidConstants.PalCpuCyclesPerSecond;
        private int _todInputFrequencyHz = 50;
        private int _todCycleAccumulator;
        private int _todInputPulseAccumulator;
        private bool _todStopped;
        private byte _serialData;
        private byte _controlA;
        private byte _controlB;
        private byte _interruptMask;
        private byte _interruptData;
        private bool _timerAPortOutput;
        private bool _timerBPortOutput;
        private bool _timerAPulseActive;
        private bool _timerBPulseActive;

        public CiaDebugState DebugState => new CiaDebugState(
            _portA,
            _portB,
            _ddrA,
            _ddrB,
            _timerA,
            _timerALatch,
            _timerB,
            _timerBLatch,
            _controlA,
            _controlB,
            _interruptMask,
            _interruptData,
            InterruptLineAsserted,
            _todTenths,
            _todSeconds,
            _todMinutes,
            _todHours);

        public bool InterruptLine => InterruptLineAsserted;

        public byte EffectivePortA => ReadPort(_portA, _ddrA);

        public ushort TimerALatch => _timerALatch;

        public void Reset(
            bool defaultTimerA60Hz,
            int cpuCyclesPerSecond = SidConstants.PalCpuCyclesPerSecond,
            int todInputFrequencyHz = 0)
        {
            _cpuCyclesPerSecond = Math.Max(1, cpuCyclesPerSecond);
            _todInputFrequencyHz = todInputFrequencyHz > 0
                ? todInputFrequencyHz
                : InferTodInputFrequencyHz(_cpuCyclesPerSecond);
            _portA = 0xFF;
            _portB = 0xFF;
            _ddrA = 0;
            _ddrB = 0;
            _timerALatch = defaultTimerA60Hz ? (ushort)0x4025 : (ushort)0xFFFF;
            _timerA = _timerALatch;
            _timerBLatch = 0xFFFF;
            _timerB = 0xFFFF;
            _todTenths = 0;
            _todSeconds = 0;
            _todMinutes = 0;
            _todHours = 0x01;
            _alarmTenths = 0;
            _alarmSeconds = 0;
            _alarmMinutes = 0;
            _alarmHours = 0x01;
            _latchedTenths = 0;
            _latchedSeconds = 0;
            _latchedMinutes = 0;
            _latchedHours = 0;
            _todLatched = false;
            _todCycleAccumulator = 0;
            _todInputPulseAccumulator = 0;
            _todStopped = false;
            _serialData = 0;
            _controlA = defaultTimerA60Hz ? (byte)0x11 : (byte)0x00;
            _controlB = 0;
            _interruptMask = defaultTimerA60Hz ? (byte)0x01 : (byte)0x00;
            _interruptData = 0;
            _timerAPortOutput = false;
            _timerBPortOutput = false;
            _timerAPulseActive = false;
            _timerBPulseActive = false;
        }

        public byte Read(byte register)
        {
            return (register & 0x0F) switch
            {
                0x00 => ReadPort(_portA, _ddrA),
                0x01 => ReadPort(ReadPortBOutput(), _ddrB),
                0x02 => _ddrA,
                0x03 => _ddrB,
                0x04 => (byte)_timerA,
                0x05 => (byte)(_timerA >> 8),
                0x06 => (byte)_timerB,
                0x07 => (byte)(_timerB >> 8),
                0x08 => ReadTodTenths(),
                0x09 => ReadTodSeconds(),
                0x0A => ReadTodMinutes(),
                0x0B => ReadTodHours(),
                0x0C => _serialData,
                0x0D => ReadInterruptData(),
                0x0E => _controlA,
                0x0F => _controlB,
                _ => 0
            };
        }

        public void Write(byte register, byte value)
        {
            switch (register & 0x0F)
            {
                case 0x00:
                    _portA = value;
                    break;
                case 0x01:
                    _portB = value;
                    break;
                case 0x02:
                    _ddrA = value;
                    break;
                case 0x03:
                    _ddrB = value;
                    break;
                case 0x04:
                    _timerALatch = (ushort)((_timerALatch & 0xFF00) | value);
                    break;
                case 0x05:
                    _timerALatch = (ushort)((_timerALatch & 0x00FF) | (value << 8));
                    if ((_controlA & 0x01) == 0)
                    {
                        _timerA = _timerALatch;
                    }

                    break;
                case 0x06:
                    _timerBLatch = (ushort)((_timerBLatch & 0xFF00) | value);
                    break;
                case 0x07:
                    _timerBLatch = (ushort)((_timerBLatch & 0x00FF) | (value << 8));
                    if ((_controlB & 0x01) == 0)
                    {
                        _timerB = _timerBLatch;
                    }

                    break;
                case 0x08:
                    WriteTodTenthsOrAlarm(value);
                    break;
                case 0x09:
                    WriteTodOrAlarm(ref _todSeconds, ref _alarmSeconds, value);
                    break;
                case 0x0A:
                    WriteTodOrAlarm(ref _todMinutes, ref _alarmMinutes, value);
                    break;
                case 0x0B:
                    WriteTodHoursOrAlarm(value);
                    break;
                case 0x0C:
                    _serialData = value;
                    break;
                case 0x0D:
                    if ((value & 0x80) != 0)
                    {
                        _interruptMask |= (byte)(value & 0x7F);
                    }
                    else
                    {
                        _interruptMask &= (byte)~(value & 0x7F);
                    }

                    break;
                case 0x0E:
                    _controlA = value;
                    if ((value & ControlForceLoad) != 0)
                    {
                        _timerA = _timerALatch;
                        _controlA &= unchecked((byte)~ControlForceLoad);
                    }

                    break;
                case 0x0F:
                    _controlB = value;
                    if ((value & ControlForceLoad) != 0)
                    {
                        _timerB = _timerBLatch;
                        _controlB &= unchecked((byte)~ControlForceLoad);
                    }

                    break;
            }
        }

        public bool Tick()
        {
            ClearExpiredTimerPortPulses();

            var timerAUnderflow = false;
            if ((_controlA & ControlCountCnt) == 0)
            {
                timerAUnderflow = TickTimer(
                    ref _timerA,
                    _timerALatch,
                    ref _controlA,
                    InterruptTimerA,
                    ref _timerAPortOutput,
                    ref _timerAPulseActive);
            }

            var timerBSource = _controlB & (ControlCountCnt | ControlTimerBCountTimerA);
            switch (timerBSource)
            {
                case 0:
                    _ = TickTimer(
                        ref _timerB,
                        _timerBLatch,
                        ref _controlB,
                        InterruptTimerB,
                        ref _timerBPortOutput,
                        ref _timerBPulseActive);
                    break;
                case ControlTimerBCountTimerA:
                    if (timerAUnderflow)
                    {
                        _ = TickTimer(
                            ref _timerB,
                            _timerBLatch,
                            ref _controlB,
                            InterruptTimerB,
                            ref _timerBPortOutput,
                            ref _timerBPulseActive);
                    }

                    break;
            }

            TickTod();
            return InterruptLineAsserted;
        }

        public void TriggerSerialInterrupt()
        {
            SetInterrupt(InterruptSerial);
        }

        public void TriggerFlagInterrupt()
        {
            SetInterrupt(InterruptFlag);
        }

        private bool TickTimer(
            ref ushort timer,
            ushort latch,
            ref byte control,
            byte interruptBit,
            ref bool portOutput,
            ref bool pulseActive)
        {
            if ((control & ControlStart) == 0)
            {
                return false;
            }

            if (timer != 0)
            {
                timer--;
                return false;
            }

            timer = latch;
            SetInterrupt(interruptBit);
            UpdateTimerPortOutput(control, ref portOutput, ref pulseActive);
            if ((control & ControlOneShot) != 0)
            {
                control &= unchecked((byte)~ControlStart);
            }

            return true;
        }

        private static void UpdateTimerPortOutput(byte control, ref bool portOutput, ref bool pulseActive)
        {
            if ((control & ControlPortOutput) == 0)
            {
                return;
            }

            if ((control & ControlToggleOutput) != 0)
            {
                portOutput = !portOutput;
                return;
            }

            portOutput = true;
            pulseActive = true;
        }

        private void ClearExpiredTimerPortPulses()
        {
            if (_timerAPulseActive)
            {
                _timerAPortOutput = false;
                _timerAPulseActive = false;
            }

            if (_timerBPulseActive)
            {
                _timerBPortOutput = false;
                _timerBPulseActive = false;
            }
        }

        private void TickTod()
        {
            if (_todStopped)
            {
                return;
            }

            _todCycleAccumulator += _todInputFrequencyHz;
            while (_todCycleAccumulator >= _cpuCyclesPerSecond)
            {
                _todCycleAccumulator -= _cpuCyclesPerSecond;
                _todInputPulseAccumulator++;
                if (_todInputPulseAccumulator < GetTodInputPulsesPerTenth())
                {
                    continue;
                }

                _todInputPulseAccumulator = 0;
                IncrementTod();
                if (_todTenths == _alarmTenths &&
                    _todSeconds == _alarmSeconds &&
                    _todMinutes == _alarmMinutes &&
                    _todHours == _alarmHours)
                {
                    SetInterrupt(InterruptTod);
                }
            }
        }

        private void IncrementTod()
        {
            _todTenths = IncrementBcd(_todTenths, 0x09, out var carry);
            if (!carry)
            {
                return;
            }

            _todSeconds = IncrementBcd(_todSeconds, 0x59, out carry);
            if (!carry)
            {
                return;
            }

            _todMinutes = IncrementBcd(_todMinutes, 0x59, out carry);
            if (!carry)
            {
                return;
            }

            _todHours = IncrementHour(_todHours);
        }

        private static byte IncrementBcd(byte value, byte max, out bool carry)
        {
            var ones = (value & 0x0F) + 1;
            var tens = value & 0xF0;
            if (ones >= 0x0A)
            {
                ones = 0;
                tens += 0x10;
            }

            var result = (byte)(tens | ones);
            carry = result > max;
            return carry ? (byte)0 : result;
        }

        private static byte IncrementHour(byte value)
        {
            var pm = value & 0x80;
            var hour = value & 0x1F;
            hour = IncrementBcd((byte)hour, 0x12, out var carry);
            if (hour == 0)
            {
                hour = 0x01;
            }

            if (carry)
            {
                pm ^= 0x80;
            }

            return (byte)(pm | hour);
        }

        private void WriteTodOrAlarm(ref byte tod, ref byte alarm, byte value)
        {
            if ((_controlB & 0x80) != 0)
            {
                alarm = value;
            }
            else
            {
                tod = value;
            }
        }

        private void WriteTodTenthsOrAlarm(byte value)
        {
            if ((_controlB & 0x80) != 0)
            {
                _alarmTenths = value;
                return;
            }

            _todTenths = value;
            _todCycleAccumulator = 0;
            _todInputPulseAccumulator = 0;
            _todStopped = false;
        }

        private void WriteTodHoursOrAlarm(byte value)
        {
            if ((_controlB & 0x80) != 0)
            {
                _alarmHours = value;
                return;
            }

            _todHours = value;
            _todCycleAccumulator = 0;
            _todInputPulseAccumulator = 0;
            _todStopped = true;
        }

        private int GetTodInputPulsesPerTenth()
        {
            return (_controlA & ControlTod50Hz) != 0 ? 5 : 6;
        }

        private static int InferTodInputFrequencyHz(int cpuCyclesPerSecond)
        {
            return Math.Abs(cpuCyclesPerSecond - SidConstants.PalCpuCyclesPerSecond) <
                Math.Abs(cpuCyclesPerSecond - SidConstants.NtscCpuCyclesPerSecond)
                    ? 50
                    : 60;
        }

        private byte ReadTodTenths()
        {
            var value = _todLatched ? _latchedTenths : _todTenths;
            _todLatched = false;
            return value;
        }

        private byte ReadTodSeconds()
        {
            return _todLatched ? _latchedSeconds : _todSeconds;
        }

        private byte ReadTodMinutes()
        {
            return _todLatched ? _latchedMinutes : _todMinutes;
        }

        private byte ReadTodHours()
        {
            _latchedTenths = _todTenths;
            _latchedSeconds = _todSeconds;
            _latchedMinutes = _todMinutes;
            _latchedHours = _todHours;
            _todLatched = true;
            _ = _latchedTenths;
            return _latchedHours;
        }

        private static byte ReadPort(byte port, byte ddr)
        {
            return (byte)((port & ddr) | (~ddr & 0xFF));
        }

        private byte ReadPortBOutput()
        {
            var value = _portB;
            if ((_controlA & ControlPortOutput) != 0)
            {
                value = SetBit(value, 0x40, _timerAPortOutput);
            }

            if ((_controlB & ControlPortOutput) != 0)
            {
                value = SetBit(value, 0x80, _timerBPortOutput);
            }

            return value;
        }

        private static byte SetBit(byte value, byte mask, bool enabled)
        {
            return enabled
                ? (byte)(value | mask)
                : (byte)(value & ~mask);
        }

        private byte ReadInterruptData()
        {
            var value = (byte)(_interruptData | (InterruptLineAsserted ? 0x80 : 0x00));
            _interruptData = 0;
            return value;
        }

        private void SetInterrupt(byte bit)
        {
            _interruptData |= bit;
        }

        private bool InterruptLineAsserted => (_interruptData & _interruptMask & 0x7F) != 0;
    }
}
