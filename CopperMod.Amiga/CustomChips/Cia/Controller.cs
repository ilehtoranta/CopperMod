using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.CustomChips.Cia
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

    internal sealed class Cia
    {
        public const byte TimerAInterruptMask = 0x01;
        public const byte TimerBInterruptMask = 0x02;
        public const byte TodInterruptMask = 0x04;
        public const byte SerialInterruptMask = 0x08;
        public const byte FlagInterruptMask = 0x10;
        public const long CpuCyclesPerCiaTick = AmigaConstants.A500PalCpuCyclesPerCiaTick;

        private readonly byte[] _registers = new byte[16];
        private readonly CiaTimer _timerA;
        private readonly CiaTimer _timerB;
        private readonly long _cpuCyclesPerCiaTick;
        private int _todCounter;
        private int _todAlarm;
        private int _todReadLatch;
        private bool _todReadLatched;
        private byte _icrMask;
        private byte _icrPending;
        private ulong _wakeVersion;

        public Cia(AmigaCiaId id)
            : this(id, RasterTiming.Pal)
        {
        }

        public Cia(AmigaCiaId id, RasterTiming timing)
        {
            Id = id;
            _cpuCyclesPerCiaTick = timing.CpuCyclesPerCiaTick;
            _timerA = new CiaTimer(false, _cpuCyclesPerCiaTick);
            _timerB = new CiaTimer(true, _cpuCyclesPerCiaTick);
        }

        public AmigaCiaId Id { get; }

        public byte InterruptMask => _icrMask;

        public byte PendingInterrupts => _icrPending;

        public ushort TimerALatch => _timerA.Latch;

        public ushort TimerBLatch => _timerB.Latch;

        public long TimerAIntervalCycles => _timerA.LatchTicks * _cpuCyclesPerCiaTick;

        internal ulong WakeVersion => _wakeVersion;

        public void Reset(byte initialPortA = 0, byte initialPortADataDirection = 0)
        {
            Array.Clear(_registers);
            _registers[0] = initialPortA;
            _registers[2] = initialPortADataDirection;
            _todCounter = 0;
            _todAlarm = 0;
            _todReadLatch = 0;
            _todReadLatched = false;
            _timerA.Reset();
            _timerB.Reset();
            _icrMask = 0;
            _icrPending = 0;
            _wakeVersion++;
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
                    return ReadTodRegister(register);
                case 0x09:
                    return ReadTodRegister(register);
                case 0x0A:
                    return ReadTodRegister(register);
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

            _wakeVersion++;
        }

        public byte AbleInterrupts(byte value, long cycle, IList<AmigaCiaInterruptEvent> interruptEvents)
        {
            AdvanceTo(cycle, interruptEvents);
            var previous = UpdateInterruptMask(value, cycle, interruptEvents);
            _wakeVersion++;
            return previous;
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

            _wakeVersion++;
            return previous;
        }

        public void SetSerialData(byte value, long cycle, IList<AmigaCiaInterruptEvent> interruptEvents)
        {
            AdvanceTo(cycle, interruptEvents);
            _registers[0x0C] = value;
            SetPending(SerialInterruptMask, cycle, interruptEvents);
            _wakeVersion++;
        }

        public void IncrementTod(long cycle, IList<AmigaCiaInterruptEvent> interruptEvents)
        {
            _todCounter = (_todCounter + 1) & 0x00FF_FFFF;
            if (_todCounter == _todAlarm)
            {
                SetPending(TodInterruptMask, cycle, interruptEvents);
            }

            _wakeVersion++;
        }

        public void PulseFlag(long cycle, IList<AmigaCiaInterruptEvent> interruptEvents)
        {
            AdvanceTo(cycle, interruptEvents);
            SetPending(FlagInterruptMask, cycle, interruptEvents);
            _wakeVersion++;
        }

        public long? GetNextInterruptCycle(long maxCycle)
        {
            long? result = null;
            result = MinCycle(result, _timerA.GetNextUnderflowCycle(maxCycle));
            result = MinCycle(
                result,
                _timerB.CountsTimerAUnderflows
                    ? _timerA.GetUnderflowCycleAfterEvents(_timerB.Counter, maxCycle)
                    : _timerB.GetNextUnderflowCycle(maxCycle));
            return result;
        }

        public long? GetNextTodInterruptCycle(long targetCycle, long nextTodTickCycle, long todTickIntervalCycles)
        {
            if ((_icrMask & TodInterruptMask) == 0 ||
                todTickIntervalCycles <= 0 ||
                targetCycle < nextTodTickCycle)
            {
                return null;
            }

            var ticksToAlarm = (_todAlarm - _todCounter) & 0x00FF_FFFF;
            if (ticksToAlarm == 0)
            {
                ticksToAlarm = 0x0100_0000;
            }

            var cycle = nextTodTickCycle + (((long)ticksToAlarm - 1L) * todTickIntervalCycles);
            return cycle <= targetCycle ? cycle : null;
        }

        [HotPath]
        public void AdvanceTo(long targetCycle, IList<AmigaCiaInterruptEvent> interruptEvents)
        {
            _timerA.AdvanceTo(
                targetCycle,
                this,
                TimerAInterruptMask,
                interruptEvents,
                _timerB.CountsTimerAUnderflows ? _timerB : null);
            if (!_timerB.CountsTimerAUnderflows)
            {
                _timerB.AdvanceTo(targetCycle, this, TimerBInterruptMask, interruptEvents, null);
            }

            _wakeVersion++;
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

        private byte ReadTodRegister(int register)
        {
            register &= 0x0F;
            if (register == 0x0A && !_todReadLatched)
            {
                _todReadLatch = _todCounter;
                _todReadLatched = true;
            }

            var source = _todReadLatched ? _todReadLatch : _todCounter;
            var value = (byte)(source >> ((register - 0x08) * 8));
            if (register == 0x08)
            {
                _todReadLatched = false;
            }

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

        private void SetPending(
            byte bits,
            long cycle,
            IList<AmigaCiaInterruptEvent> interruptEvents,
            bool queueOnlyWhenNew = false)
        {
            bits = (byte)(bits & 0x1F);
            if (bits == 0)
            {
                return;
            }

            var eventBits = queueOnlyWhenNew ? (byte)(bits & ~_icrPending) : bits;
            _icrPending = (byte)(_icrPending | bits);
            var active = (byte)(eventBits & _icrMask);
            if (active != 0)
            {
                interruptEvents.Add(new AmigaCiaInterruptEvent(Id, active, cycle));
            }
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
            private readonly bool _isTimerB;
            private readonly long _cpuCyclesPerCiaTick;
            private long _nextTickCycle;

            public CiaTimer(bool isTimerB, long cpuCyclesPerCiaTick)
            {
                _isTimerB = isTimerB;
                _cpuCyclesPerCiaTick = cpuCyclesPerCiaTick;
            }

            public ushort Latch { get; private set; }

            public int Counter { get; private set; }

            public byte Control { get; private set; }

            private bool Running => (Control & 0x01) != 0;

            private bool OneShot => (Control & 0x08) != 0;

            public bool CountsTimerAUnderflows => _isTimerB && (Control & 0x60) == 0x40;

            private bool CountsCpu => _isTimerB
                ? (Control & 0x60) == 0
                : (Control & 0x20) == 0;

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
                if (!Running || !CountsCpu || Counter <= 0)
                {
                    return null;
                }

                var cycle = _nextTickCycle + ((Counter - 1L) * _cpuCyclesPerCiaTick);
                return cycle <= maxCycle ? cycle : null;
            }

            public long? GetUnderflowCycleAfterEvents(int eventCount, long maxCycle)
            {
                if (!Running || !CountsCpu || Counter <= 0 || eventCount <= 0)
                {
                    return null;
                }

                if (OneShot && eventCount > 1)
                {
                    return null;
                }

                var first = _nextTickCycle + ((Counter - 1L) * _cpuCyclesPerCiaTick);
                var cycle = first + ((long)eventCount - 1L) * LatchTicks * _cpuCyclesPerCiaTick;
                return cycle <= maxCycle ? cycle : null;
            }

            public void AdvanceTo(
                long targetCycle,
                AmigaCia cia,
                byte interruptMask,
                IList<AmigaCiaInterruptEvent> interruptEvents,
                CiaTimer? timerBUnderflowCounter)
            {
                if (!Running || !CountsCpu || targetCycle < _nextTickCycle)
                {
                    return;
                }

                var underflowCycle = _nextTickCycle + ((Counter - 1L) * _cpuCyclesPerCiaTick);
                if (underflowCycle <= targetCycle)
                {
                    cia.SetPending(interruptMask, underflowCycle, interruptEvents, queueOnlyWhenNew: true);
                    var latchTicks = LatchTicks;
                    var intervalCycles = (long)latchTicks * _cpuCyclesPerCiaTick;
                    var underflows = OneShot
                        ? 1L
                        : 1L + ((targetCycle - underflowCycle) / intervalCycles);
                    timerBUnderflowCounter?.CountExternalEvents(
                        underflowCycle,
                        intervalCycles,
                        underflows,
                        cia,
                        TimerBInterruptMask,
                        interruptEvents);
                    Counter = latchTicks;
                    var lastUnderflowCycle = underflowCycle + ((underflows - 1L) * intervalCycles);
                    _nextTickCycle = lastUnderflowCycle + _cpuCyclesPerCiaTick;
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

                var ticks = (int)(((targetCycle - _nextTickCycle) / _cpuCyclesPerCiaTick) + 1);
                Counter = Math.Max(0, Counter - ticks);
                _nextTickCycle += ticks * _cpuCyclesPerCiaTick;
            }

            public void CountExternalEvent(
                long cycle,
                AmigaCia cia,
                byte interruptMask,
                IList<AmigaCiaInterruptEvent> interruptEvents)
                => CountExternalEvents(cycle, 0, 1, cia, interruptMask, interruptEvents);

            public void CountExternalEvents(
                long firstCycle,
                long intervalCycles,
                long eventCount,
                AmigaCia cia,
                byte interruptMask,
                IList<AmigaCiaInterruptEvent> interruptEvents)
            {
                if (!Running || Counter <= 0 || eventCount <= 0)
                {
                    return;
                }

                if (eventCount < Counter)
                {
                    Counter -= (int)eventCount;
                    if (intervalCycles > 0)
                    {
                        _nextTickCycle = NextCiaTickAfter(firstCycle + ((eventCount - 1L) * intervalCycles));
                    }

                    return;
                }

                var underflowCycle = firstCycle + ((Counter - 1L) * Math.Max(1, intervalCycles));
                cia.SetPending(interruptMask, underflowCycle, interruptEvents, queueOnlyWhenNew: true);
                var latchTicks = LatchTicks;
                if (OneShot)
                {
                    Counter = latchTicks;
                    _nextTickCycle = NextCiaTickAfter(underflowCycle);
                    Control = (byte)(Control & ~0x01);
                    return;
                }

                var remainingEvents = eventCount - Counter;
                if (remainingEvents == 0)
                {
                    Counter = latchTicks;
                }
                else
                {
                    var remainder = remainingEvents % latchTicks;
                    Counter = remainder == 0 ? latchTicks : (int)(latchTicks - remainder);
                }

                if (intervalCycles > 0)
                {
                    _nextTickCycle = NextCiaTickAfter(firstCycle + ((eventCount - 1L) * intervalCycles));
                }
            }

            public int LatchTicks => Latch == 0 ? 65_536 : Latch;

            private void Load(long cycle)
            {
                Counter = LatchTicks;
                _nextTickCycle = NextCiaTickAfter(cycle);
            }

            private long NextCiaTickAfter(long cycle)
            {
                return ((cycle / _cpuCyclesPerCiaTick) + 1) * _cpuCyclesPerCiaTick;
            }
        }
    }
}
