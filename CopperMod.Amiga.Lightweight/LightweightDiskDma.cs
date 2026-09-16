namespace CopperMod.Amiga.Lightweight;

// Read-only standard-ADF DMA. FIFO/input/request timing is the bounded H6b2
// model recorded in LIGHTWEIGHT_A500_ENGINE_PLAN, not a Paula DMAL oracle.
internal struct LightweightDiskDma
{
    public LightweightDiskDma() { }

    private ulong _fifo;
    private int _fifoCount;
    private int _wordBits;
    private bool _armed;
    private bool _waitingForSync;
    private bool _pendingCounts;
    private long _inputCycle = long.MaxValue;
    private uint _pendingAddress;
    private ushort _pendingData;

    internal bool Active { get; private set; }
    internal int Remaining { get; private set; }
    internal long NextCycle { get; private set; } = long.MaxValue;
    internal long PendingOutputCycle { get; private set; } = long.MaxValue;
    internal long LastOutputCycle { get; private set; } = long.MinValue;

    internal void Reset()
    {
        _fifo = 0;
        _fifoCount = _wordBits = Remaining = 0;
        _armed = _waitingForSync = _pendingCounts = Active = false;
        _pendingAddress = 0;
        _pendingData = 0;
        _inputCycle = PendingOutputCycle = NextCycle = long.MaxValue;
        LastOutputCycle = long.MinValue;
    }

    internal void WriteLength(ushort value, long cycle, LightweightA500Machine machine)
    {
        var previouslyArmed = _armed;
        _armed = (value & 0x8000) != 0;
        if (!_armed)
        {
            Cancel();
            return;
        }
        if (!previouslyArmed) return;
        if (Active)
        {
            machine.ReportUnsupportedFeature("active DSKLEN reprogramming without cancellation");
            return;
        }

        Cancel(); // Does not revoke a preceding transfer's accepted RAM word.
        Remaining = value & 0x3FFF;
        if (Remaining == 0)
        {
            _armed = false;
            machine.LatchDiskBlock(cycle);
            return;
        }
        if ((value & 0x4000) != 0)
        {
            machine.ReportUnsupportedFeature("disk write DMA on read-only ADF media");
            return;
        }
        Active = true;
        _waitingForSync = (machine.Adkcon & 0x0400) != 0;
        _wordBits = 0;
    }

    private void Cancel()
    {
        Active = _waitingForSync = _pendingCounts = false;
        Remaining = _wordBits = _fifoCount = 0;
        _fifo = 0;
        _inputCycle = long.MaxValue;
        NextCycle = PendingOutputCycle;
    }

    internal void OnDmaconChanged(long cycle, LightweightA500Machine machine)
    {
        if (!DmaEnabled(machine)) _inputCycle = long.MaxValue;
        else ScheduleInput(cycle);
        RefreshNextCycle();
    }

    internal void OnAdkconChanged(ushort previous, long cycle, LightweightA500Machine machine)
    {
        if (!Active || ((previous ^ machine.Adkcon) & 0x0400) == 0) return;
        machine.ReportUnsupportedFeature("WORDSYNC enable change during active disk DMA");
        _waitingForSync = (machine.Adkcon & 0x0400) != 0;
        _wordBits = 0;
    }

    internal void ReceiveBit(ushort shift, bool wordEqual, long cycle, LightweightA500Machine machine)
    {
        System.Diagnostics.Debug.Assert(Active);
        if (_waitingForSync)
        {
            if (wordEqual && DmaEnabled(machine))
            {
                _waitingForSync = false;
                _wordBits = 0; // First matching word is not transferred.
            }
            return;
        }

        if (++_wordBits == 16)
        {
            _wordBits = 0;
            if (DmaEnabled(machine) && Remaining > _fifoCount + (_pendingCounts ? 1 : 0))
            {
                if (_fifoCount == 3)
                {
                    machine.ReportUnsupportedFeature("disk FIFO overrun timing");
                }
                else
                {
                    _fifo |= (ulong)shift << (_fifoCount * 16);
                    _fifoCount++;
                    ScheduleInput(cycle);
                    RefreshNextCycle();
                }
            }
        }
        // An aligned subsequent sync word may itself be transferred; the
        // boundary for following words is reset on every match.
        if (wordEqual && (machine.Adkcon & 0x0400) != 0) _wordBits = 0;
    }

    internal void Step(long cycle, LightweightA500Machine machine)
    {
        System.Diagnostics.Debug.Assert(cycle == NextCycle);
        if (cycle == PendingOutputCycle)
        {
            machine.WriteChipWordDma(_pendingAddress, _pendingData);
            machine.SetDiskDataFromDma(_pendingData);
            PendingOutputCycle = long.MaxValue;
            LastOutputCycle = cycle; // Also excludes a CPU arriving at this CCK.
            if (_pendingCounts)
            {
                _pendingCounts = false;
                Remaining--;
                machine.SetDiskLengthFromDma(Remaining);
                if (Remaining == 0)
                {
                    Active = _armed = false;
                    _fifo = 0;
                    _fifoCount = 0;
                    _inputCycle = long.MaxValue;
                    machine.LatchDiskBlock(cycle);
                }
            }
        }
        if (cycle == _inputCycle)
        {
            _inputCycle = long.MaxValue;
            if (Active && DmaEnabled(machine) && _fifoCount != 0)
            {
                _pendingAddress = machine.GetDiskPointer();
                _pendingData = (ushort)_fifo;
                _fifo >>= 16;
                _fifoCount--;
                _pendingCounts = true;
                PendingOutputCycle = cycle + LightweightClock.CpuCyclesPerColorClock;
                // Agnus accepts an address and increments its pointer here.
                // Later register writes cannot retarget this accepted word.
                machine.SetDiskPointerFromDma(_pendingAddress + 2);
                ScheduleInput(cycle);
            }
        }
        RefreshNextCycle();
    }

    private static bool DmaEnabled(LightweightA500Machine machine)
        => (machine.Dmacon & 0x0210) == 0x0210;

    private void ScheduleInput(long cycle)
    {
        if (_fifoCount == 0 || _inputCycle != long.MaxValue) return;
        _inputCycle = NextInputAfter(cycle);
    }

    // Accepted input is one CCK before each physical OUT8/OUTA/OUTC. A
    // newly available word cannot retroactively occupy the current input.
    internal static long NextInputAfter(long cycle)
    {
        var line = cycle - cycle % LightweightClock.CpuCyclesPerLine;
        if (line + 14 > cycle) return line + 14;
        if (line + 18 > cycle) return line + 18;
        if (line + 22 > cycle) return line + 22;
        return line + LightweightClock.CpuCyclesPerLine + 14;
    }

    private void RefreshNextCycle() => NextCycle = Math.Min(_inputCycle, PendingOutputCycle);
}
