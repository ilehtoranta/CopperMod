using System.Runtime.CompilerServices;

namespace CopperMod.Amiga.Lightweight;

// Synchronized raw-key transport. Power-up/resync MCU sequences: LWA-INPUT-001.
internal struct LightweightKeyboard
{
    private const int PhaseCycles = 142; // ~20 us at PAL CPU clock; peripheral-model choice.
    private const int TimeoutCycles = 1_014_412; // ceil(143 ms), aligned to CCK.
    [InlineArray(10)] private struct KeyQueue { private byte _first; }
    private KeyQueue _queue;
    private int _head, _count, _bits;
    private byte _wireByte;
    private byte _phase; // 0 idle, 1 data, 2 clock low, 3 clock high, 4 handshake.
    private long _hostLowSince;
    private bool _hostHigh;
    internal long NextCycle { get; private set; }
    internal bool WaitingForHandshake => _phase == 4;
    internal bool ClockHigh { get; private set; }
    internal bool DataHigh { get; private set; }

    internal void Reset()
    {
        this = default;
        NextCycle = long.MaxValue;
        _hostLowSince = -1;
        _hostHigh = ClockHigh = DataHigh = true;
    }

    internal bool Enqueue(byte raw, long cycle)
    {
        if (_count == 10) return false;
        _queue[(_head + _count) % 10] = raw;
        _count++;
        TryStart(cycle);
        return true;
    }

    private void TryStart(long cycle)
    {
        if (_phase != 0 || _count == 0 || !_hostHigh) return;
        var raw = _queue[_head];
        _head = (_head + 1) % 10;
        _count--;
        _wireByte = (byte)~((raw << 1) | (raw >> 7));
        _bits = 0;
        _phase = 1;
        NextCycle = (cycle + 2) & ~1L;
    }

    internal void HostDataChanged(bool high, long cycle)
    {
        if (high == _hostHigh) return;
        _hostHigh = high;
        if (!high) _hostLowSince = cycle;
        else
        {
            // Accept >=1 us (8 CPU cycles). Host software should use >=85 us.
            if (_phase == 4 && _hostLowSince >= 0 && cycle - _hostLowSince >= 8)
            {
                _phase = 0;
                NextCycle = long.MaxValue;
            }
            _hostLowSince = -1;
            TryStart(cycle);
        }
    }

    internal void Step(long cycle, LightweightCia cia, LightweightA500Machine machine)
    {
        if (_phase == 4)
        {
            machine.ReportUnsupportedFeature("keyboard handshake timeout: MCU resynchronization is not implemented");
            NextCycle = long.MaxValue;
            return;
        }
        if (cia.SerialOutput)
        {
            machine.ReportUnsupportedFeature("keyboard transfer interrupted by CIA serial output mode");
            NextCycle = long.MaxValue;
            return;
        }
        switch (_phase)
        {
            case 1:
                DataHigh = (_wireByte & 0x80) != 0;
                _phase = 2;
                break;
            case 2:
                ClockHigh = false;
                _phase = 3;
                break;
            case 3:
                ClockHigh = true;
                machine.ReceiveKeyboardBit(DataHigh, cycle);
                _wireByte <<= 1;
                if (++_bits == 8)
                {
                    DataHigh = true;
                    _phase = 4;
                    _hostLowSince = -1;
                    NextCycle = cycle + TimeoutCycles;
                    return;
                }
                _phase = 1;
                break;
        }
        NextCycle = cycle + PhaseCycles;
    }
}
