using System;
using System.Collections.Generic;

namespace CopperMod.Amiga
{
    internal enum AmigaCiaId
    {
        A,
        B
    }

    internal readonly struct AmigaCiaInterruptEvent
    {
        public AmigaCiaInterruptEvent(AmigaCiaId cia, byte icrBits, long cycle)
        {
            Cia = cia;
            IcrBits = icrBits;
            Cycle = cycle;
        }

        public AmigaCiaId Cia { get; }

        public byte IcrBits { get; }

        public long Cycle { get; }
    }

    internal sealed class AmigaCia
    {
        public const byte TimerAInterruptMask = 0x01;
        public const byte TimerBInterruptMask = 0x02;
        public const byte TodInterruptMask = 0x04;
        public const byte SerialInterruptMask = 0x08;
        public const byte FlagInterruptMask = 0x10;
        public static readonly long CpuCyclesPerCiaTick = Math.Max(
            1,
            (long)Math.Round(AmigaConstants.A500PalCpuClockHz / AmigaConstants.A500PalCiaClockHz));

        private readonly byte[] _registers = new byte[16];
        private readonly CiaTimer _timerA = new CiaTimer();
        private readonly CiaTimer _timerB = new CiaTimer();
        private int _todCounter;
        private int _todAlarm;
        private byte _icrMask;
        private byte _icrPending;

        public AmigaCia(AmigaCiaId id)
        {
            Id = id;
        }

        public AmigaCiaId Id { get; }

        public byte InterruptMask => _icrMask;

        public byte PendingInterrupts => _icrPending;

        public ushort TimerALatch => _timerA.Latch;

        public ushort TimerBLatch => _timerB.Latch;

        public long TimerAIntervalCycles => _timerA.Latch == 0 ? 0 : _timerA.Latch * CpuCyclesPerCiaTick;

        public void Reset(byte initialPortA = 0)
        {
            Array.Clear(_registers);
            _registers[0] = initialPortA;
            _todCounter = 0;
            _todAlarm = 0;
            _timerA.Reset();
            _timerB.Reset();
            _icrMask = 0;
            _icrPending = 0;
        }

        public byte ReadRegister(int register)
        {
            register &= 0x0F;
            switch (register)
            {
                case 0x00:
                case 0x01:
                    return ReadPortRegister(register, 0xFF);
                case 0x04:
                    return (byte)_timerA.Counter;
                case 0x05:
                    return (byte)(_timerA.Counter >> 8);
                case 0x06:
                    return (byte)_timerB.Counter;
                case 0x07:
                    return (byte)(_timerB.Counter >> 8);
                case 0x08:
                    return (byte)_todCounter;
                case 0x09:
                    return (byte)(_todCounter >> 8);
                case 0x0A:
                    return (byte)(_todCounter >> 16);
                case 0x0D:
                    return ReadInterruptControl();
                case 0x0E:
                    return _timerA.Control;
                case 0x0F:
                    return _timerB.Control;
                default:
                    return _registers[register];
            }
        }

        public byte ReadPortRegister(int register, byte inputPins)
        {
            register &= 0x0F;
            if (register is not (0x00 or 0x01))
            {
                return ReadRegister(register);
            }

            var latch = _registers[register];
            var dataDirection = _registers[register + 2];
            return (byte)((latch & dataDirection) | (inputPins & ~dataDirection));
        }

        public byte ReadPortLatch(int register)
        {
            register &= 0x0F;
            return register is 0x00 or 0x01 ? _registers[register] : (byte)0;
        }

        public void WriteRegister(int register, byte value, long cycle, IList<AmigaCiaInterruptEvent> interruptEvents)
        {
            AdvanceTo(cycle, interruptEvents);
            register &= 0x0F;
            switch (register)
            {
                case 0x04:
                    _timerA.WriteLatchLow(value);
                    break;
                case 0x05:
                    _timerA.WriteLatchHigh(value, cycle);
                    break;
                case 0x06:
                    _timerB.WriteLatchLow(value);
                    break;
                case 0x07:
                    _timerB.WriteLatchHigh(value, cycle);
                    break;
                case 0x08:
                case 0x09:
                case 0x0A:
                    WriteTodRegister(register, value);
                    break;
                case 0x0D:
                    UpdateInterruptMask(value, cycle, interruptEvents);
                    break;
                case 0x0E:
                    _timerA.WriteControl(value, cycle);
                    break;
                case 0x0F:
                    _timerB.WriteControl(value, cycle);
                    break;
                default:
                    _registers[register] = value;
                    break;
            }
        }

        public byte AbleInterrupts(byte value, long cycle, IList<AmigaCiaInterruptEvent> interruptEvents)
        {
            AdvanceTo(cycle, interruptEvents);
            return UpdateInterruptMask(value, cycle, interruptEvents);
        }

        public byte SetInterrupts(byte value, long cycle, IList<AmigaCiaInterruptEvent> interruptEvents)
        {
            AdvanceTo(cycle, interruptEvents);
            var previous = ReadInterruptControlValue();
            var bits = (byte)(value & 0x1F);
            if ((value & 0x80) != 0)
            {
                SetPending(bits, cycle, interruptEvents);
            }
            else
            {
                _icrPending = (byte)(_icrPending & ~bits);
            }

            return previous;
        }

        public void SetSerialData(byte value, long cycle, IList<AmigaCiaInterruptEvent> interruptEvents)
        {
            AdvanceTo(cycle, interruptEvents);
            _registers[0x0C] = value;
            SetPending(SerialInterruptMask, cycle, interruptEvents);
        }

        public void IncrementTod(long cycle, IList<AmigaCiaInterruptEvent> interruptEvents)
        {
            _todCounter = (_todCounter + 1) & 0x00FF_FFFF;
            if (_todCounter == _todAlarm)
            {
                SetPending(TodInterruptMask, cycle, interruptEvents);
            }
        }

        public void PulseFlag(long cycle, IList<AmigaCiaInterruptEvent> interruptEvents)
        {
            AdvanceTo(cycle, interruptEvents);
            SetPending(FlagInterruptMask, cycle, interruptEvents);
        }

        public long? GetNextInterruptCycle(long maxCycle)
        {
            long? result = null;
            result = MinCycle(result, _timerA.GetNextUnderflowCycle(maxCycle));
            result = MinCycle(result, _timerB.GetNextUnderflowCycle(maxCycle));
            return result;
        }

        public void AdvanceTo(long targetCycle, IList<AmigaCiaInterruptEvent> interruptEvents)
        {
            _timerA.AdvanceTo(targetCycle, cycle => SetPending(TimerAInterruptMask, cycle, interruptEvents));
            _timerB.AdvanceTo(targetCycle, cycle => SetPending(TimerBInterruptMask, cycle, interruptEvents));
        }

        private byte UpdateInterruptMask(byte value, long cycle, IList<AmigaCiaInterruptEvent> interruptEvents)
        {
            var previous = _icrMask;
            var bits = (byte)(value & 0x1F);
            if ((value & 0x80) != 0)
            {
                _icrMask = (byte)(_icrMask | bits);
                var activePending = (byte)(_icrPending & bits & _icrMask);
                if (activePending != 0)
                {
                    interruptEvents.Add(new AmigaCiaInterruptEvent(Id, activePending, cycle));
                }
            }
            else
            {
                _icrMask = (byte)(_icrMask & ~bits);
            }

            return previous;
        }

        private byte ReadInterruptControl()
        {
            var value = ReadInterruptControlValue();
            _icrPending = 0;
            return value;
        }

        private void WriteTodRegister(int register, byte value)
        {
            var shift = (register - 0x08) * 8;
            var mask = 0xFF << shift;
            if ((_timerB.Control & 0x80) != 0)
            {
                _todAlarm = (_todAlarm & ~mask) | (value << shift);
            }
            else
            {
                _todCounter = (_todCounter & ~mask) | (value << shift);
            }
        }

        private byte ReadInterruptControlValue()
        {
            var value = _icrPending;
            if ((_icrPending & _icrMask) != 0)
            {
                value |= 0x80;
            }

            return value;
        }

        private void SetPending(byte bits, long cycle, IList<AmigaCiaInterruptEvent> interruptEvents)
        {
            bits = (byte)(bits & 0x1F);
            if (bits == 0)
            {
                return;
            }

            _icrPending = (byte)(_icrPending | bits);
            var active = (byte)(bits & _icrMask);
            interruptEvents.Add(new AmigaCiaInterruptEvent(Id, active != 0 ? active : bits, cycle));
        }

        private static long? MinCycle(long? left, long? right)
        {
            if (!left.HasValue)
            {
                return right;
            }

            if (!right.HasValue)
            {
                return left;
            }

            return Math.Min(left.Value, right.Value);
        }

        private sealed class CiaTimer
        {
            private long _nextTickCycle;

            public ushort Latch { get; private set; }

            public int Counter { get; private set; }

            public byte Control { get; private set; }

            private bool Running => (Control & 0x01) != 0;

            private bool OneShot => (Control & 0x08) != 0;

            public void Reset()
            {
                Latch = 0;
                Counter = 0;
                Control = 0;
                _nextTickCycle = 0;
            }

            public void WriteLatchLow(byte value)
            {
                Latch = (ushort)((Latch & 0xFF00) | value);
                if (!Running)
                {
                    Counter = LatchTicks;
                }
            }

            public void WriteLatchHigh(byte value, long cycle)
            {
                Latch = (ushort)((value << 8) | (Latch & 0x00FF));
                if (OneShot)
                {
                    Load(cycle);
                    Control |= 0x01;
                }
                else if (!Running)
                {
                    Load(cycle);
                }
            }

            public void WriteControl(byte value, long cycle)
            {
                var wasRunning = Running;
                if ((value & 0x10) != 0)
                {
                    Load(cycle);
                }

                Control = (byte)(value & 0xEF);
                if (!wasRunning && Running)
                {
                    if (Counter <= 0)
                    {
                        Counter = LatchTicks;
                    }

                    _nextTickCycle = NextCiaTickAfter(cycle);
                }
            }

            public long? GetNextUnderflowCycle(long maxCycle)
            {
                if (!Running || Counter <= 0)
                {
                    return null;
                }

                var cycle = _nextTickCycle + ((Counter - 1L) * CpuCyclesPerCiaTick);
                return cycle <= maxCycle ? cycle : null;
            }

            public void AdvanceTo(long targetCycle, Action<long> underflow)
            {
                if (!Running || targetCycle < _nextTickCycle)
                {
                    return;
                }

                while (Running)
                {
                    var underflowCycle = _nextTickCycle + ((Counter - 1L) * CpuCyclesPerCiaTick);
                    if (underflowCycle > targetCycle)
                    {
                        break;
                    }

                    underflow(underflowCycle);
                    Counter = LatchTicks;
                    _nextTickCycle = underflowCycle + CpuCyclesPerCiaTick;
                    if (OneShot)
                    {
                        Control = (byte)(Control & ~0x01);
                        return;
                    }
                }

                if (!Running || targetCycle < _nextTickCycle)
                {
                    return;
                }

                var ticks = (int)(((targetCycle - _nextTickCycle) / CpuCyclesPerCiaTick) + 1);
                Counter = Math.Max(0, Counter - ticks);
                _nextTickCycle += ticks * CpuCyclesPerCiaTick;
            }

            private int LatchTicks => Latch == 0 ? 65_536 : Latch;

            private void Load(long cycle)
            {
                Counter = LatchTicks;
                _nextTickCycle = NextCiaTickAfter(cycle);
            }

            private static long NextCiaTickAfter(long cycle)
            {
                return ((cycle / CpuCyclesPerCiaTick) + 1) * CpuCyclesPerCiaTick;
            }
        }
    }
}
