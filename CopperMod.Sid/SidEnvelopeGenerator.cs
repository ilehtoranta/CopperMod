namespace CopperMod.Sid;

/// <summary>
/// Clocked digital envelope. A comparison, divider reset, envelope step and GATE
/// transition are separate events; register writes never reset the rate clock.
/// Timing evidence and the reset convention are recorded in docs/SID-timing-contract.md.
/// </summary>
internal struct SidEnvelopeGenerator
{
    internal const int Attack = 0, Decay = 1, Release = 3;
    private static readonly int[] Comparators =
    {
        0x007f, 0x3000, 0x1e00, 0x0660, 0x0182, 0x5573, 0x000e, 0x3805,
        0x2424, 0x2220, 0x090c, 0x0ecd, 0x010e, 0x23f7, 0x5237, 0x64a8
    };

    public int Counter { get; private set; }
    public byte ReadLatch { get; private set; }
    public int RateLfsr { get; private set; }
    public int RateCounter { get; private set; }
    public int DividerCounter { get; private set; }
    public int DividerPeriod { get; private set; }
    public int State { get; private set; }
    public int StateDelay { get; private set; }
    public int StepDelay { get; private set; }
    public int DividerDelay { get; private set; }
    public bool CounterEnabled { get; private set; }
    public bool RateResetPending { get; private set; }
    private int _pendingPeriod, _nextState, _rateIndex;
    private byte _ad, _sr;
    private bool _gate;

    public void Reset()
    {
        this = default;
        State = _nextState = Release;
        RateLfsr = 0x7fff;
        DividerPeriod = 1;
        // Deterministic player reset: silence, no pending gate or clock event.
        // This deliberately does not claim the unspecified power-on ENV value.
    }

    public void WriteAttackDecay(byte value)
    {
        _ad = value;
        if (State != Release) _rateIndex = State == Attack ? value >> 4 : value & 15;
    }

    public void WriteSustainRelease(byte value)
    {
        _sr = value;
        if (State == Release) _rateIndex = value & 15;
    }

    public void WriteGate(bool gate)
    {
        if (_gate == gate) return;
        _gate = gate;
        _nextState = gate ? Attack : Release;
        StateDelay = gate ? 2 : StepDelay > 0 ? 3 : 2;
        if (!gate) return;
        if (RateResetPending || DividerDelay == 2)
            StepDelay = DividerPeriod == 1 || DividerDelay == 2 ? 2 : 4;
        else if (DividerDelay == 1)
            StateDelay = 3;
    }

    public bool Clock()
    {
        ReadLatch = (byte)Counter;
        if (_pendingPeriod != 0)
        {
            DividerPeriod = _pendingPeriod;
            _pendingPeriod = 0;
        }
        AdvanceState();

        bool stepped = false;
        bool stepDue = StepDelay > 0 && --StepDelay == 0;
        if (stepDue)
        {
            if (CounterEnabled)
            {
                Counter = (Counter + (State == Attack ? 1 : -1)) & 255;
                stepped = true;
                if (Counter == 0) CounterEnabled = false;
                if (State == Attack && Counter == 255)
                {
                    _nextState = Decay;
                    StateDelay = 3;
                }
                _pendingPeriod = Counter switch
                {
                    0 or 255 => 1, 0x5d => 2, 0x36 => 4,
                    0x1a => 8, 0x0e => 16, 0x06 => 30, _ => 0
                };
            }
        }
        else if (DividerDelay > 0 && --DividerDelay == 0)
        {
            DividerCounter = 0;
            if (State == Release || (State == Decay && Counter != (_sr >> 4) * 17))
                StepDelay = 1;
        }
        else if (RateResetPending)
        {
            RateResetPending = false;
            RateLfsr = 0x7fff;
            RateCounter = 0;
            if (State == Attack)
            {
                DividerCounter = 0;
                StepDelay = 2;
            }
            else if (CounterEnabled && ++DividerCounter == DividerPeriod)
                DividerDelay = DividerPeriod == 1 ? 1 : 2;
        }

        if (RateLfsr == Comparators[_rateIndex]) RateResetPending = true;
        else
        {
            RateLfsr = (RateLfsr >> 1) | (((RateLfsr ^ (RateLfsr >> 1)) & 1) << 14);
            RateCounter = (RateCounter + 1) % 32767;
        }
        return stepped;
    }

    private void AdvanceState()
    {
        if (StateDelay == 0) return;
        --StateDelay;
        if (_nextState == Attack)
        {
            if (StateDelay == 1) _rateIndex = _ad & 15;
            if (StateDelay != 0) return;
            CounterEnabled = true;
        }
        else if (_nextState == Release)
        {
            if (!(State == Attack && StateDelay == 0) &&
                !(State == Decay && StateDelay == 1)) return;
        }
        else if (StateDelay != 0) return;

        State = _nextState;
        _rateIndex = State == Attack ? _ad >> 4 : State == Decay ? _ad & 15 : _sr & 15;
    }
}
