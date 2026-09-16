using CopperDisk;

namespace CopperMod.Amiga.Lightweight;

// H6b1: ideal standard-ADF read input, not an analog Paula PLL model.
// Nominal 300 RPM is independent of ADKCON.FAST and of drive selection.
// All external bit arrivals are sampled on the canonical clock's next CCK.
internal struct LightweightDiskSerial
{
    public LightweightDiskSerial() { }

    internal const int TrackBits = AmigaDosTrackEncoder.EncodedTrackByteCount * 8;
    private const int BitRateDenominator = 2 * 5 * TrackBits;
    private const int WholeCcksPerBit = LightweightPaulaAudio.PalCpuFrequency / BitRateDenominator;
    private const int FractionPerBit = LightweightPaulaAudio.PalCpuFrequency % BitRateDenominator;
    private int _fraction;
    private int _bitPosition;
    private int _byteBits;
    private ushort _shift;
    private byte _data;
    private bool _byteReady;
    private bool _wordEqual;
    private byte _slowPhase;
    private int _slowFirstBit;

    internal long NextCycle { get; private set; } = long.MaxValue;
    internal int BitPosition => _bitPosition;
    internal ushort Shift => _shift;

    internal void Reset()
    {
        NextCycle = long.MaxValue;
        _fraction = _bitPosition = _byteBits = 0;
        _shift = 0;
        _data = 0;
        _byteReady = _wordEqual = false;
        _slowPhase = 0;
        _slowFirstBit = 0;
    }

    internal void OnDriveChanged(LightweightFloppyDrive drive, long cycle, bool mediaChanged = false)
    {
        if (!drive.Mounted || !drive.MotorOn)
        {
            NextCycle = long.MaxValue;
            return;
        }
        if (NextCycle != long.MaxValue && !mediaChanged)
            return; // Selection, side and head changes do not restart rotation.

        // The bounded media model starts a new spindle run at track bit zero.
        // It does not claim physical coast-down or insertion phase fidelity.
        _bitPosition = 0;
        _slowPhase = 0;
        _fraction = BitRateDenominator - 1; // ceil to the receiving CCK
        NextCycle = (Math.Max(cycle, drive.ReadyCycle) + 1) & ~1L;
        ScheduleFollowingBit();
    }

    internal void Step(long cycle, LightweightFloppyDrive drive, LightweightA500Machine machine)
    {
        if (drive.Selected)
        {
            if ((machine.Adkcon & 0x0200) != 0)
            {
                machine.ReportUnsupportedFeature("disk serial recovery with MSBSYNC");
            }
            else
            {
                var track = drive.Track;
                var bit = (track[_bitPosition >> 3] >> (7 - (_bitPosition & 7))) & 1;
                if ((machine.Adkcon & 0x0100) == 0 || _slowPhase != 0)
                    bit = RecoverSlowCell(bit);
                if (bit >= 0)
                {
                    _shift = (ushort)((_shift << 1) | bit);
                    if (++_byteBits == 8)
                    {
                        _byteBits = 0;
                        _data = (byte)_shift;
                        _byteReady = true;
                    }
                    CompareSync(cycle, machine);
                    if (machine.DiskDmaActive)
                        machine.ReceiveDiskBit(_shift, _wordEqual, cycle);
                }
            }
        }

        if (++_bitPosition == TrackBits)
        {
            _bitPosition = 0;
            if (drive.Selected)
                machine.LatchDiskIndex(cycle);
        }
        ScheduleFollowingBit();
    }

    internal void CompareSync(long cycle, LightweightA500Machine machine)
    {
        var equal = _shift == machine.GetCustomRegister(LightweightRegisters.Dsksync);
        if (equal && !_wordEqual)
            machine.LatchDiskSync(cycle);
        _wordEqual = equal;
    }

    // Bounded ideal-ADF model, not a Paula PLL implementation (LWA-DISK-010).
    // WinUAE's 2us-to-4us grouping uses two source cells, or three if the
    // second contains a pulse. Consume them causally, without looking ahead
    // or changing rotation. Publish at completion; an accepted window survives
    // FAST writes. That transition phase still requires hardware verification.
    private int RecoverSlowCell(int bit)
    {
        if (_slowPhase == 0)
        {
            _slowFirstBit = bit;
            _slowPhase = 1;
            return -1;
        }
        if (_slowPhase == 1 && bit != 0)
        {
            _slowPhase = 2;
            return -1;
        }
        var recovered = _slowPhase == 2 ? 1 : _slowFirstBit;
        _slowPhase = 0;
        return recovered;
    }

    internal ushort ReadByteStatus(LightweightRegisters registers)
    {
        var length = registers.Read(LightweightRegisters.Dsklen);
        var value = (ushort)(_data | (_byteReady ? 0x8000 : 0) |
            (_wordEqual ? 0x1000 : 0) | ((length & 0x4000) >> 1) |
            ((length & 0x8000) != 0 && (registers.Dmacon & 0x0210) == 0x0210 ? 0x4000 : 0));
        _byteReady = false;
        return value;
    }

    private void ScheduleFollowingBit()
    {
        var ccks = WholeCcksPerBit;
        _fraction += FractionPerBit;
        if (_fraction >= BitRateDenominator)
        {
            _fraction -= BitRateDenominator;
            ccks++;
        }
        NextCycle += 2 * ccks;
    }
}
