using System;

namespace CopperMod.Amiga
{
    internal readonly struct BlitterSpecializationCounters
    {
        public BlitterSpecializationCounters(
            long kernelHits,
            long kernelMisses,
            long generatedKernels,
            long scalarFallbacks)
        {
            KernelHits = kernelHits;
            KernelMisses = kernelMisses;
            GeneratedKernels = generatedKernels;
            ScalarFallbacks = scalarFallbacks;
        }

        public long KernelHits { get; }

        public long KernelMisses { get; }

        public long GeneratedKernels { get; }

        public long ScalarFallbacks { get; }
    }

    internal readonly struct BlitterKernelKey : IEquatable<BlitterKernelKey>
    {
        public BlitterKernelKey(
            bool lineMode,
            bool useA,
            bool useB,
            bool useC,
            bool useD,
            byte minterm,
            int shiftA,
            int shiftB,
            bool descending,
            bool fillEnabled,
            bool fillExclusive,
            bool lineSingleDot,
            bool lineSud,
            bool lineSul,
            bool lineAul,
            bool lineSign)
        {
            LineMode = lineMode;
            UseA = useA;
            UseB = useB;
            UseC = useC;
            UseD = useD;
            Minterm = minterm;
            ShiftA = shiftA;
            ShiftB = shiftB;
            Descending = descending;
            FillEnabled = fillEnabled;
            FillExclusive = fillExclusive;
            LineSingleDot = lineSingleDot;
            LineSud = lineSud;
            LineSul = lineSul;
            LineAul = lineAul;
            LineSign = lineSign;
        }

        public bool LineMode { get; }

        public bool UseA { get; }

        public bool UseB { get; }

        public bool UseC { get; }

        public bool UseD { get; }

        public byte Minterm { get; }

        public int ShiftA { get; }

        public int ShiftB { get; }

        public bool Descending { get; }

        public bool FillEnabled { get; }

        public bool FillExclusive { get; }

        public bool LineSingleDot { get; }

        public bool LineSud { get; }

        public bool LineSul { get; }

        public bool LineAul { get; }

        public bool LineSign { get; }

        public bool Equals(BlitterKernelKey other)
            => LineMode == other.LineMode &&
                UseA == other.UseA &&
                UseB == other.UseB &&
                UseC == other.UseC &&
                UseD == other.UseD &&
                Minterm == other.Minterm &&
                ShiftA == other.ShiftA &&
                ShiftB == other.ShiftB &&
                Descending == other.Descending &&
                FillEnabled == other.FillEnabled &&
                FillExclusive == other.FillExclusive &&
                LineSingleDot == other.LineSingleDot &&
                LineSud == other.LineSud &&
                LineSul == other.LineSul &&
                LineAul == other.LineAul &&
                LineSign == other.LineSign;

        public override bool Equals(object? obj)
            => obj is BlitterKernelKey other && Equals(other);

        public override int GetHashCode()
        {
            var hash = 17;
            hash = (hash * 31) + (LineMode ? 1 : 0);
            hash = (hash * 31) + (UseA ? 1 : 0);
            hash = (hash * 31) + (UseB ? 1 : 0);
            hash = (hash * 31) + (UseC ? 1 : 0);
            hash = (hash * 31) + (UseD ? 1 : 0);
            hash = (hash * 31) + Minterm;
            hash = (hash * 31) + ShiftA;
            hash = (hash * 31) + ShiftB;
            hash = (hash * 31) + (Descending ? 1 : 0);
            hash = (hash * 31) + (FillEnabled ? 1 : 0);
            hash = (hash * 31) + (FillExclusive ? 1 : 0);
            hash = (hash * 31) + (LineSingleDot ? 1 : 0);
            hash = (hash * 31) + (LineSud ? 1 : 0);
            hash = (hash * 31) + (LineSul ? 1 : 0);
            hash = (hash * 31) + (LineAul ? 1 : 0);
            hash = (hash * 31) + (LineSign ? 1 : 0);
            return hash;
        }
    }

    internal struct BlitterAreaKernelState
    {
        public ushort PreviousA;

        public ushort PreviousB;

        public bool FillCarry;
    }

    internal static class BlitterKernelMath
    {
        public static ushort ExecuteArea(
            BlitterKernelKey key,
            ref BlitterAreaKernelState state,
            ushort rawA,
            ushort rawB,
            ushort rawC,
            ushort mask)
        {
            rawA = (ushort)(rawA & mask);
            var sourceA = ShiftSource(rawA, ref state.PreviousA, key.ShiftA, key.Descending);
            var sourceB = ShiftSource(rawB, ref state.PreviousB, key.ShiftB, key.Descending);
            var output = ApplyMinterm(key.Minterm, sourceA, sourceB, rawC);
            return key.FillEnabled
                ? ApplyFill(output, key.FillExclusive, ref state.FillCarry)
                : output;
        }

        public static ushort ExecuteLine(BlitterKernelKey key, ushort lineMask, ushort texture, ushort sourceC)
            => ApplyMinterm(key.Minterm, lineMask, texture, sourceC);

        public static ushort ApplyMinterm(byte minterm, ushort sourceA, ushort sourceB, ushort sourceC)
        {
            uint result = 0;
            var notA = (ushort)~sourceA;
            var notB = (ushort)~sourceB;
            var notC = (ushort)~sourceC;
            if ((minterm & 0x01) != 0)
            {
                result |= (uint)(notA & notB & notC);
            }

            if ((minterm & 0x02) != 0)
            {
                result |= (uint)(notA & notB & sourceC);
            }

            if ((minterm & 0x04) != 0)
            {
                result |= (uint)(notA & sourceB & notC);
            }

            if ((minterm & 0x08) != 0)
            {
                result |= (uint)(notA & sourceB & sourceC);
            }

            if ((minterm & 0x10) != 0)
            {
                result |= (uint)(sourceA & notB & notC);
            }

            if ((minterm & 0x20) != 0)
            {
                result |= (uint)(sourceA & notB & sourceC);
            }

            if ((minterm & 0x40) != 0)
            {
                result |= (uint)(sourceA & sourceB & notC);
            }

            if ((minterm & 0x80) != 0)
            {
                result |= (uint)(sourceA & sourceB & sourceC);
            }

            return (ushort)result;
        }

        public static ushort ShiftSource(ushort current, ref ushort previous, int shift, bool descending)
        {
            shift &= 0x0F;
            if (shift == 0)
            {
                previous = current;
                return current;
            }

            uint combined = descending
                ? ((uint)current << 16) | previous
                : ((uint)previous << 16) | current;
            var value = descending
                ? (ushort)(combined >> (16 - shift))
                : (ushort)(combined >> shift);
            previous = current;
            return value;
        }

        public static ushort ApplyFill(ushort value, bool exclusive, ref bool fillCarry)
        {
            ushort output = 0;
            for (var bit = 0; bit < 16; bit++)
            {
                var mask = (ushort)(1 << bit);
                var input = (value & mask) != 0;
                if (exclusive)
                {
                    if (fillCarry)
                    {
                        output |= mask;
                    }

                    if (input)
                    {
                        fillCarry = !fillCarry;
                    }

                    continue;
                }

                if (fillCarry || input)
                {
                    output |= mask;
                }

                if (input)
                {
                    fillCarry = !fillCarry;
                }
            }

            return output;
        }
    }

    internal readonly struct AmigaBlitterSnapshot
    {
        public AmigaBlitterSnapshot(
            bool busy,
            bool zero,
            long currentCycle,
            uint sourceA,
            uint sourceB,
            uint sourceC,
            uint destinationD,
            int widthWords,
            int height,
            int wordX,
            int rowY,
            bool lineMode,
            long nextDmaCycle,
            long lastDmaCycle,
            int completedMicroOps,
            BlitterSpecializationCounters specializationCounters)
        {
            Busy = busy;
            Zero = zero;
            CurrentCycle = currentCycle;
            SourceA = sourceA;
            SourceB = sourceB;
            SourceC = sourceC;
            DestinationD = destinationD;
            WidthWords = widthWords;
            Height = height;
            WordX = wordX;
            RowY = rowY;
            LineMode = lineMode;
            NextDmaCycle = nextDmaCycle;
            LastDmaCycle = lastDmaCycle;
            CompletedMicroOps = completedMicroOps;
            SpecializationCounters = specializationCounters;
        }

        public bool Busy { get; }

        public bool Zero { get; }

        public long CurrentCycle { get; }

        public uint SourceA { get; }

        public uint SourceB { get; }

        public uint SourceC { get; }

        public uint DestinationD { get; }

        public int WidthWords { get; }

        public int Height { get; }

        public int WordX { get; }

        public int RowY { get; }

        public bool LineMode { get; }

        public long NextDmaCycle { get; }

        public long LastDmaCycle { get; }

        public int CompletedMicroOps { get; }

        public BlitterSpecializationCounters SpecializationCounters { get; }
    }

    internal readonly struct BlitterCompiledKernel
    {
        public BlitterCompiledKernel(BlitterKernelKey key, bool supported)
        {
            Key = key;
            Supported = supported;
        }

        public BlitterKernelKey Key { get; }

        public bool Supported { get; }

        public bool SupportsArea => Supported && !Key.LineMode;

        public bool SupportsLine => Supported && Key.LineMode;

        public ushort ExecuteArea(
            ref BlitterAreaKernelState state,
            ushort rawA,
            ushort rawB,
            ushort rawC,
            ushort mask)
            => BlitterKernelMath.ExecuteArea(Key, ref state, rawA, rawB, rawC, mask);

        public ushort ExecuteLine(ushort lineMask, ushort texture, ushort sourceC)
            => BlitterKernelMath.ExecuteLine(Key, lineMask, texture, sourceC);
    }

    internal sealed class BlitterKernelCache
    {
        private const int CacheCapacity = 1024;

        private readonly BlitterKernelKey[] _keys = new BlitterKernelKey[CacheCapacity];
        private readonly BlitterCompiledKernel[] _kernels = new BlitterCompiledKernel[CacheCapacity];
        private readonly bool[] _valid = new bool[CacheCapacity];
        private long _kernelHits;
        private long _kernelMisses;
        private long _generatedKernels;
        private long _scalarFallbacks;
        private int _count;

        public BlitterCompiledKernel GetOrCreate(BlitterKernelKey key)
        {
            for (var index = 0; index < _count; index++)
            {
                if (_valid[index] && _keys[index].Equals(key))
                {
                    _kernelHits++;
                    return _kernels[index];
                }
            }

            _kernelMisses++;
            var kernel = CreateKernel(key);
            if (!kernel.Supported)
            {
                _scalarFallbacks++;
                return kernel;
            }

            _generatedKernels++;
            if (_count >= CacheCapacity)
            {
                _scalarFallbacks++;
                return default;
            }

            _keys[_count] = key;
            _kernels[_count] = kernel;
            _valid[_count] = true;
            _count++;
            return kernel;
        }

        public void RecordFallback()
        {
            _scalarFallbacks++;
        }

        public BlitterSpecializationCounters CaptureCounters()
            => new BlitterSpecializationCounters(_kernelHits, _kernelMisses, _generatedKernels, _scalarFallbacks);

        private static BlitterCompiledKernel CreateKernel(BlitterKernelKey key)
            => new BlitterCompiledKernel(key, supported: true);
    }

    internal sealed class AmigaBlitter
    {
        private const ushort DmaMasterEnable = 0x0200;
        private const ushort DmaBlitterEnable = 0x0040;
        private const ushort DmaBlitterNasty = 0x0400;
        private const ushort DmaconBlitterZero = 0x2000;
        private const ushort DmaconBlitterBusy = 0x4000;
        private const ushort Bltcon1LineMode = 0x0001;
        private const ushort Bltcon1SingleDot = 0x0002;
        private const ushort Bltcon1Descending = 0x0002;
        private const ushort Bltcon1FillCarryIn = 0x0004;
        private const ushort Bltcon1InclusiveFill = 0x0008;
        private const ushort Bltcon1ExclusiveFill = 0x0010;
        private const ushort Bltcon1LineSud = 0x0010;
        private const ushort Bltcon1LineSul = 0x0008;
        private const ushort Bltcon1LineAul = 0x0004;
        private const ushort Bltcon1LineSign = 0x0040;
        private const int ChipSlotCycles = AgnusChipSlotScheduler.SlotCycles;

        private readonly AmigaBus _bus;
        private readonly bool _specializationEnabled;
        private readonly BlitterKernelCache _kernelCache = new BlitterKernelCache();
        private uint _sourceA;
        private uint _sourceB;
        private uint _sourceC;
        private uint _destinationD;
        private short _sourceAModulo;
        private short _sourceBModulo;
        private short _sourceCModulo;
        private short _destinationDModulo;
        private ushort _bltcon0;
        private ushort _bltcon1;
        private ushort _firstWordMask = 0xFFFF;
        private ushort _lastWordMask = 0xFFFF;
        private ushort _dataA;
        private ushort _dataB;
        private ushort _dataC;
        private ushort _activeFirstWordMask = 0xFFFF;
        private ushort _activeLastWordMask = 0xFFFF;
        private ushort _activeDataA;
        private ushort _activeDataB;
        private ushort _activeDataC;
        private int _dataAShift;
        private int _dataBShift;
        private int _activeDataAShift;
        private int _activeDataBShift;
        private short _activeSourceAModulo;
        private short _activeSourceBModulo;
        private short _activeSourceCModulo;
        private short _activeDestinationDModulo;
        private bool _busy;
        private bool _zeroFlag = true;
        private long _currentCycle;
        private bool _useA;
        private bool _useB;
        private bool _useC;
        private bool _useD;
        private byte _minterm;
        private int _shiftA;
        private int _shiftB;
        private bool _lineMode;
        private bool _descending;
        private int _step;
        private int _widthWords;
        private int _height;
        private int _wordX;
        private int _rowY;
        private uint _workSourceA;
        private uint _workSourceB;
        private uint _workSourceC;
        private uint _workDestinationD;
        private ushort _previousA;
        private ushort _previousB;
        private bool _fillEnabled;
        private bool _fillExclusive;
        private bool _fillCarryInitial;
        private bool _fillCarry;
        private int _lineIndex;
        private int _lineLength;
        private int _lineBit;
        private int _lineY;
        private int _lineLastDrawnY;
        private bool _lineSingleDot;
        private bool _lineSud;
        private bool _lineSul;
        private bool _lineAul;
        private bool _lineSign;
        private int _lineError;
        private int _lineSourceRowStride;
        private int _lineDestinationRowStride;
        private int _lineBPatternStride;
        private long _lastDmaCycle;
        private int _completedMicroOps;
        private bool _completionPending;
        private BlitterCompiledKernel _activeKernel;
        private BlitterAreaKernelState _areaKernelState;

        public AmigaBlitter(AmigaBus bus, bool enableSpecialization = false)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _specializationEnabled = enableSpecialization;
        }

        public ushort DmaconStatusBits
        {
            get
            {
                var status = _zeroFlag ? DmaconBlitterZero : (ushort)0;
                if (_busy)
                {
                    status |= DmaconBlitterBusy;
                }

                return status;
            }
        }

        public void Reset()
        {
            _sourceA = 0;
            _sourceB = 0;
            _sourceC = 0;
            _destinationD = 0;
            _sourceAModulo = 0;
            _sourceBModulo = 0;
            _sourceCModulo = 0;
            _destinationDModulo = 0;
            _bltcon0 = 0;
            _bltcon1 = 0;
            _firstWordMask = 0xFFFF;
            _lastWordMask = 0xFFFF;
            _dataA = 0;
            _dataB = 0;
            _dataC = 0;
            _activeFirstWordMask = 0xFFFF;
            _activeLastWordMask = 0xFFFF;
            _activeDataA = 0;
            _activeDataB = 0;
            _activeDataC = 0;
            _dataAShift = 0;
            _dataBShift = 0;
            _activeDataAShift = 0;
            _activeDataBShift = 0;
            _activeSourceAModulo = 0;
            _activeSourceBModulo = 0;
            _activeSourceCModulo = 0;
            _activeDestinationDModulo = 0;
            _busy = false;
            _zeroFlag = true;
            _currentCycle = 0;
            _useA = false;
            _useB = false;
            _useC = false;
            _useD = false;
            _minterm = 0;
            _shiftA = 0;
            _shiftB = 0;
            _lineMode = false;
            _descending = false;
            _step = 2;
            _widthWords = 0;
            _height = 0;
            _wordX = 0;
            _rowY = 0;
            _workSourceA = 0;
            _workSourceB = 0;
            _workSourceC = 0;
            _workDestinationD = 0;
            _previousA = 0;
            _previousB = 0;
            _fillEnabled = false;
            _fillExclusive = false;
            _fillCarryInitial = false;
            _fillCarry = false;
            _lineIndex = 0;
            _lineLength = 0;
            _lineBit = 0;
            _lineY = 0;
            _lineLastDrawnY = int.MinValue;
            _lineSingleDot = false;
            _lineSud = false;
            _lineSul = false;
            _lineAul = false;
            _lineSign = false;
            _lineError = 0;
            _lineSourceRowStride = 0;
            _lineDestinationRowStride = 0;
            _lineBPatternStride = 0;
            _lastDmaCycle = 0;
            _completedMicroOps = 0;
            _completionPending = false;
            _activeKernel = default;
            _areaKernelState = default;
        }

        public AmigaBlitterSnapshot CaptureSnapshot()
        {
            return new AmigaBlitterSnapshot(
                _busy,
                _zeroFlag,
                _currentCycle,
                _lineMode ? _sourceA : _workSourceA,
                _workSourceB,
                _workSourceC,
                _lineMode ? _destinationD : _workDestinationD,
                _widthWords,
                _height,
                _wordX,
                _rowY,
                _lineMode,
                _currentCycle,
                _lastDmaCycle,
                _completedMicroOps,
                _kernelCache.CaptureCounters());
        }

        public bool Busy => _busy;

        public void WriteRegister(ushort offset, ushort value, long cycle)
        {
            switch (offset)
            {
                case 0x040:
                    _bltcon0 = value;
                    break;
                case 0x042:
                    _bltcon1 = value;
                    break;
                case 0x044:
                    _firstWordMask = value;
                    break;
                case 0x046:
                    _lastWordMask = value;
                    break;
                case 0x048:
                    _sourceC = _bus.WriteChipDmaPointerHigh(_sourceC, value);
                    break;
                case 0x04A:
                    _sourceC = _bus.WriteChipDmaPointerLow(_sourceC, value);
                    break;
                case 0x04C:
                    _sourceB = _bus.WriteChipDmaPointerHigh(_sourceB, value);
                    break;
                case 0x04E:
                    _sourceB = _bus.WriteChipDmaPointerLow(_sourceB, value);
                    break;
                case 0x050:
                    _sourceA = _bus.WriteChipDmaPointerHigh(_sourceA, value);
                    break;
                case 0x052:
                    _sourceA = _bus.WriteChipDmaPointerLow(_sourceA, value);
                    break;
                case 0x054:
                    _destinationD = _bus.WriteChipDmaPointerHigh(_destinationD, value);
                    break;
                case 0x056:
                    _destinationD = _bus.WriteChipDmaPointerLow(_destinationD, value);
                    break;
                case 0x058:
                    StartBlit(value, cycle);
                    break;
                case 0x060:
                    _sourceCModulo = unchecked((short)value);
                    break;
                case 0x062:
                    _sourceBModulo = unchecked((short)value);
                    break;
                case 0x064:
                    _sourceAModulo = unchecked((short)value);
                    break;
                case 0x066:
                    _destinationDModulo = unchecked((short)value);
                    break;
                case 0x070:
                    _dataC = value;
                    break;
                case 0x072:
                    _dataB = value;
                    _dataBShift = (_bltcon1 >> 12) & 0x0F;
                    break;
                case 0x074:
                    _dataA = value;
                    _dataAShift = (_bltcon0 >> 12) & 0x0F;
                    break;
            }
        }

        public void AdvanceTo(long targetCycle)
        {
            System.Diagnostics.Debug.Assert(targetCycle >= 0, "Blitter advance cycles must be non-negative.");
            if (!_busy)
            {
                _currentCycle = Math.Max(_currentCycle, targetCycle);
                return;
            }

            while (_busy)
            {
                if (_completionPending)
                {
                    if (_currentCycle > targetCycle)
                    {
                        return;
                    }

                    FinalizePendingCompletion();
                    continue;
                }

                if (RequiresDmaForCurrentBlit() && !IsBlitterDmaEnabled())
                {
                    _currentCycle = Math.Max(_currentCycle, targetCycle);
                    return;
                }

                var stepEndCycle = GetCurrentStepEndCycle();
                if (stepEndCycle > targetCycle)
                {
                    return;
                }

                if (_lineMode)
                {
                    StepLinePixel(targetCycle);
                }
                else
                {
                    StepAreaWord(targetCycle);
                }
            }
        }

        public long GetPredictedCompletionCycle()
        {
            if (!_busy)
            {
                return _currentCycle;
            }

            if (_completionPending)
            {
                return _currentCycle;
            }

            if (RequiresDmaForCurrentBlit() && !IsBlitterDmaEnabled())
            {
                return long.MaxValue;
            }

            if (_lineMode)
            {
                var remainingPixels = Math.Max(0, _lineLength - _lineIndex);
                return _currentCycle + ((long)remainingPixels * GetLinePixelCycles());
            }

            var remainingWords = Math.Max(0, ((_height - _rowY - 1) * _widthWords) + (_widthWords - _wordX));
            return _currentCycle + ((long)remainingWords * GetAreaWordCycles());
        }

        public long? GetNextWakeCandidateCycle(long currentCycle, long targetCycle)
        {
            if (!_busy || targetCycle <= currentCycle)
            {
                return null;
            }

            var completionCycle = GetPredictedCompletionCycle();
            if (completionCycle == long.MaxValue)
            {
                return null;
            }

            if (completionCycle > targetCycle)
            {
                return null;
            }

            return completionCycle <= currentCycle ? currentCycle + 1 : completionCycle;
        }

        public long AdvanceThroughCpuStall(long requestedCycle)
        {
            if (!ShouldStallCpu())
            {
                return requestedCycle;
            }

            var releaseCycle = GetCurrentStepEndCycle();
            AdvanceTo(releaseCycle);
            return Math.Max(requestedCycle, _currentCycle);
        }

        private void StartBlit(ushort bltsize, long cycle)
        {
            System.Diagnostics.Debug.Assert(cycle >= 0, "Blitter start cycles must be non-negative.");
            AdvanceTo(cycle);
            if (_busy)
            {
                return;
            }

            DecodeCommonRegisters();
            _lineMode = (_bltcon1 & Bltcon1LineMode) != 0;
            _zeroFlag = true;
            _busy = true;
            _completionPending = false;
            _lastDmaCycle = 0;
            _completedMicroOps = 0;
            _currentCycle = _bus.NextChipSlotCycle(Math.Max(_currentCycle, cycle) + ChipSlotCycles);
            _previousA = 0;
            if (_useB)
            {
                _previousB = 0;
            }

            if (_lineMode)
            {
                StartLineBlit(bltsize);
                _activeKernel = _specializationEnabled
                    ? _kernelCache.GetOrCreate(CreateLineKernelKey())
                    : default;
                return;
            }

            _widthWords = bltsize & 0x3F;
            if (_widthWords == 0)
            {
                _widthWords = 64;
            }

            _height = (bltsize >> 6) & 0x03FF;
            if (_height == 0)
            {
                _height = 1024;
            }

            _wordX = 0;
            _rowY = 0;
            _descending = (_bltcon1 & Bltcon1Descending) != 0;
            _step = _descending ? -2 : 2;
            _workSourceA = GetEffectiveBlitterAddress(_sourceA);
            _workSourceB = GetEffectiveBlitterAddress(_sourceB);
            _workSourceC = GetEffectiveBlitterAddress(_sourceC);
            _workDestinationD = GetEffectiveBlitterAddress(_destinationD);
            _activeFirstWordMask = _firstWordMask;
            _activeLastWordMask = _lastWordMask;
            _activeDataA = _dataA;
            _activeDataB = _dataB;
            _activeDataC = _dataC;
            _activeDataAShift = _dataAShift;
            _activeDataBShift = _dataBShift;
            _activeSourceAModulo = _sourceAModulo;
            _activeSourceBModulo = _sourceBModulo;
            _activeSourceCModulo = _sourceCModulo;
            _activeDestinationDModulo = _destinationDModulo;
            _fillEnabled = (_bltcon1 & (Bltcon1InclusiveFill | Bltcon1ExclusiveFill)) != 0;
            _fillExclusive = (_bltcon1 & Bltcon1ExclusiveFill) != 0;
            _fillCarryInitial = (_bltcon1 & Bltcon1FillCarryIn) != 0;
            _fillCarry = _fillCarryInitial;
            _areaKernelState = new BlitterAreaKernelState
            {
                PreviousA = _previousA,
                PreviousB = _previousB,
                FillCarry = _fillCarry
            };
            _activeKernel = _specializationEnabled
                ? _kernelCache.GetOrCreate(CreateAreaKernelKey())
                : default;
        }

        private BlitterKernelKey CreateAreaKernelKey()
            => new BlitterKernelKey(
                false,
                _useA,
                _useB,
                _useC,
                _useD,
                _minterm,
                GetActiveAreaSourceAShift(),
                _useB ? _shiftB : _activeDataBShift,
                _descending,
                _fillEnabled,
                _fillExclusive,
                false,
                false,
                false,
                false,
                false);

        private BlitterKernelKey CreateLineKernelKey()
            => new BlitterKernelKey(
                true,
                _useA,
                _useB,
                _useC,
                _useD,
                _minterm,
                _shiftA,
                _shiftB,
                false,
                false,
                false,
                _lineSingleDot,
                _lineSud,
                _lineSul,
                _lineAul,
                _lineSign);

        private void DecodeCommonRegisters()
        {
            _useA = (_bltcon0 & 0x0800) != 0;
            _useB = (_bltcon0 & 0x0400) != 0;
            _useC = (_bltcon0 & 0x0200) != 0;
            _useD = (_bltcon0 & 0x0100) != 0;
            _minterm = (byte)(_bltcon0 & 0x00FF);
            _shiftA = (_bltcon0 >> 12) & 0x0F;
            _shiftB = (_bltcon1 >> 12) & 0x0F;
        }

        private void StartLineBlit(ushort bltsize)
        {
            _widthWords = bltsize & 0x3F;
            if (_widthWords == 0)
            {
                _widthWords = 64;
            }

            _height = (bltsize >> 6) & 0x03FF;
            if (_height == 0)
            {
                _height = 1024;
            }

            _lineLength = _height;
            _lineIndex = 0;
            _wordX = 0;
            _rowY = 0;
            _lineBit = _shiftA & 0x0F;
            _lineY = 0;
            _lineLastDrawnY = int.MinValue;
            _lineSingleDot = (_bltcon1 & Bltcon1SingleDot) != 0;
            _lineSud = (_bltcon1 & Bltcon1LineSud) != 0;
            _lineSul = (_bltcon1 & Bltcon1LineSul) != 0;
            _lineAul = (_bltcon1 & Bltcon1LineAul) != 0;
            _lineSign = (_bltcon1 & Bltcon1LineSign) != 0;
            _lineError = unchecked((short)_sourceA);
            _lineSourceRowStride = _sourceCModulo & ~1;
            _lineDestinationRowStride = _lineSourceRowStride;
            _lineBPatternStride = _sourceBModulo & ~1;

            _workSourceC = GetEffectiveBlitterAddress(_sourceC);
            _workSourceB = GetEffectiveBlitterAddress(_sourceB);
            _workDestinationD = GetEffectiveBlitterAddress(_destinationD);
        }

        private void StepAreaWord(long targetCycle)
        {
            var stepStart = _currentCycle;
            var stepEnd = stepStart + GetAreaWordCycles();
            var nextReadCycle = stepStart;
            var nextCycle = stepEnd;
            var isFinalWord = _rowY == _height - 1 && _wordX == _widthWords - 1;
            var mask = 0xFFFF;
            if (_wordX == 0)
            {
                mask &= _activeFirstWordMask;
            }

            if (_wordX == _widthWords - 1)
            {
                mask &= _activeLastWordMask;
            }

            var rawA = _activeDataA;
            if (_useA)
            {
                var read = ReadAndStep(ref _workSourceA, _step, nextReadCycle);
                rawA = read.Value;
                RecordBlitterDma(read.BusAccess);
                nextReadCycle = read.BusAccess.CompletedCycle;
                nextCycle = Math.Max(nextCycle, read.BusAccess.CompletedCycle);
            }

            var rawB = _activeDataB;
            if (_useB)
            {
                var read = ReadAndStep(ref _workSourceB, _step, nextReadCycle);
                rawB = read.Value;
                RecordBlitterDma(read.BusAccess);
                nextReadCycle = read.BusAccess.CompletedCycle;
                nextCycle = Math.Max(nextCycle, read.BusAccess.CompletedCycle);
            }

            var rawC = _activeDataC;
            if (_useC)
            {
                var read = ReadAndStep(ref _workSourceC, _step, nextReadCycle);
                rawC = read.Value;
                _activeDataC = rawC;
                RecordBlitterDma(read.BusAccess);
                nextReadCycle = read.BusAccess.CompletedCycle;
                nextCycle = Math.Max(nextCycle, read.BusAccess.CompletedCycle);
            }

            ushort output;
            if (_specializationEnabled && _activeKernel.SupportsArea)
            {
                output = _activeKernel.ExecuteArea(ref _areaKernelState, rawA, rawB, rawC, (ushort)mask);
                _previousA = _areaKernelState.PreviousA;
                _previousB = _areaKernelState.PreviousB;
                _fillCarry = _areaKernelState.FillCarry;
            }
            else
            {
                if (_specializationEnabled)
                {
                    _kernelCache.RecordFallback();
                }

                output = ExecuteAreaScalar(rawA, rawB, rawC, (ushort)mask);
            }

            if (output != 0)
            {
                _zeroFlag = false;
            }

            var internalCompletionCycle = Math.Max(stepEnd, nextReadCycle);
            if (_useD)
            {
                var writeCycle = Math.Max(nextReadCycle, stepEnd - ChipSlotCycles);
                var write = WriteAndStep(ref _workDestinationD, _step, output, writeCycle);
                RecordBlitterDma(write);
                nextCycle = Math.Max(nextCycle, write.CompletedCycle);
            }

            _currentCycle = isFinalWord && _useD
                ? internalCompletionCycle
                : nextCycle;
            AdvanceAreaPosition(targetCycle);
        }

        private void AdvanceAreaPosition(long targetCycle)
        {
            _wordX++;
            if (_wordX < _widthWords)
            {
                return;
            }

            _wordX = 0;
            _rowY++;
            if (_rowY >= _height)
            {
                CompleteBlit(deferInterrupt: _currentCycle > targetCycle);
                return;
            }

            if (_useA)
            {
                _workSourceA = AddModulo(_workSourceA, _activeSourceAModulo, _descending);
            }

            if (_useB)
            {
                _workSourceB = AddModulo(_workSourceB, _activeSourceBModulo, _descending);
            }

            if (_useC)
            {
                _workSourceC = AddModulo(_workSourceC, _activeSourceCModulo, _descending);
            }

            if (_useD)
            {
                _workDestinationD = AddModulo(_workDestinationD, _activeDestinationDModulo, _descending);
            }

            _fillCarry = _fillCarryInitial;
            _areaKernelState.FillCarry = _fillCarry;
        }

        private void StepLinePixel(long targetCycle)
        {
            var stepStart = _currentCycle;
            var stepEnd = stepStart + GetLinePixelCycles();
            var nextCycle = stepEnd;
            if (_useC && (!_lineSingleDot || _lineY != _lineLastDrawnY))
            {
                var nextReadCycle = _useB ? stepStart : stepStart + ChipSlotCycles;
                if (_useB)
                {
                    var firstB = ReadLineBPattern(nextReadCycle);
                    nextReadCycle = firstB.BusAccess.CompletedCycle;
                    nextCycle = Math.Max(nextCycle, firstB.BusAccess.CompletedCycle);
                    var secondB = ReadLineBPattern(nextReadCycle);
                    _dataB = secondB.Value;
                    nextReadCycle = secondB.BusAccess.CompletedCycle;
                    nextCycle = Math.Max(nextCycle, secondB.BusAccess.CompletedCycle);
                    _workSourceB = _bus.AddChipDmaPointerOffset(_workSourceB, _lineBPatternStride);
                }

                var read = _bus.ReadChipWordForDeviceWithResult(
                    AmigaBusRequester.Blitter,
                    AmigaBusAccessKind.Blitter,
                    _workSourceC,
                    nextReadCycle);
                RecordBlitterDma(read.BusAccess);
                nextCycle = Math.Max(nextCycle, read.BusAccess.CompletedCycle);
                var lineMask = RotateRight(_dataA, _lineBit);
                var textureBit = (_dataB & (0x8000 >> ((_shiftB + _lineIndex) & 0x0F))) != 0;
                var texture = textureBit ? (ushort)0xFFFF : (ushort)0;
                var output = _specializationEnabled && _activeKernel.SupportsLine
                    ? _activeKernel.ExecuteLine(lineMask, texture, read.Value)
                    : ApplyMinterm(_minterm, lineMask, texture, read.Value);
                if (_specializationEnabled && !_activeKernel.SupportsLine)
                {
                    _kernelCache.RecordFallback();
                }

                if (output != 0)
                {
                    _zeroFlag = false;
                }

                var destination = _lineIndex == 0 ? _workDestinationD : _workSourceC;
                var write = _bus.WriteChipWordForDeviceWithResult(
                    AmigaBusRequester.Blitter,
                    AmigaBusAccessKind.Blitter,
                    destination,
                    output,
                    Math.Max(read.BusAccess.CompletedCycle, stepEnd - ChipSlotCycles));
                RecordBlitterDma(write);
                nextCycle = Math.Max(nextCycle, write.CompletedCycle);
                _lineLastDrawnY = _lineY;
            }

            _currentCycle = nextCycle;
            _lineIndex++;
            _rowY = _lineIndex;
            if (_lineIndex >= _lineLength)
            {
                CompleteBlit(deferInterrupt: _currentCycle > targetCycle);
                return;
            }

            StepLineAddress();
        }

        private AmigaDeviceWordReadResult ReadLineBPattern(long cycle)
        {
            var read = _bus.ReadChipWordForDeviceWithResult(
                AmigaBusRequester.Blitter,
                AmigaBusAccessKind.Blitter,
                _workSourceB,
                cycle);
            RecordBlitterDma(read.BusAccess);
            return read;
        }

        private void StepLineAddress()
        {
            if (_lineSign)
            {
                _lineError = unchecked(_lineError + _sourceBModulo);
            }
            else
            {
                _lineError = unchecked(_lineError + _sourceAModulo);
                MoveLineMinorAxis();
            }

            MoveLineMajorAxis();
            _lineSign = _lineError < 0;
        }

        private void MoveLineMajorAxis()
        {
            if (_lineSud)
            {
                MoveLineX(_lineAul ? -1 : 1);
            }
            else
            {
                MoveLineY(_lineAul ? -1 : 1);
            }
        }

        private void MoveLineMinorAxis()
        {
            if (_lineSud)
            {
                MoveLineY(_lineSul ? -1 : 1);
            }
            else
            {
                MoveLineX(_lineSul ? -1 : 1);
            }
        }

        private void MoveLineX(int direction)
        {
            if (direction >= 0)
            {
                _lineBit++;
                if (_lineBit <= 15)
                {
                    return;
                }

                _lineBit = 0;
                _workSourceC = _bus.AddChipDmaPointerOffset(_workSourceC, 2);
                _workDestinationD = _bus.AddChipDmaPointerOffset(_workDestinationD, 2);
                return;
            }

            _lineBit--;
            if (_lineBit >= 0)
            {
                return;
            }

            _lineBit = 15;
            _workSourceC = _bus.AddChipDmaPointerOffset(_workSourceC, -2);
            _workDestinationD = _bus.AddChipDmaPointerOffset(_workDestinationD, -2);
        }

        private void MoveLineY(int direction)
        {
            _workSourceC = _bus.AddChipDmaPointerOffset(_workSourceC, direction >= 0 ? _lineSourceRowStride : -_lineSourceRowStride);
            _workDestinationD = _bus.AddChipDmaPointerOffset(_workDestinationD, direction >= 0 ? _lineDestinationRowStride : -_lineDestinationRowStride);
            _lineY += direction;
        }

        private void CompleteBlit(bool deferInterrupt = false)
        {
            if (!_lineMode)
            {
                if (_useA)
                {
                    _sourceA = _workSourceA;
                    _dataA = _previousA;
                }

                if (_useB)
                {
                    _sourceB = _workSourceB;
                    _dataB = _previousB;
                }

                if (_useC)
                {
                    _sourceC = _workSourceC;
                    _dataC = _activeDataC;
                }

                if (_useD)
                {
                    _destinationD = _workDestinationD;
                }
            }
            else
            {
                if (_useA)
                {
                    _sourceA = (uint)(ushort)_lineError;
                }

                if (_useB)
                {
                    _sourceB = _workSourceB;
                }

                _sourceC = _workSourceC;
                _destinationD = _workSourceC;
            }

            if (deferInterrupt)
            {
                _completionPending = true;
                _busy = true;
                return;
            }

            _completionPending = false;
            _busy = false;
            _bus.RequestHardwareInterrupt(AmigaConstants.IntreqBlitter, _currentCycle);
        }

        private void FinalizePendingCompletion()
        {
            _completionPending = false;
            _busy = false;
            _bus.RequestHardwareInterrupt(AmigaConstants.IntreqBlitter, _currentCycle);
        }

        private bool IsBlitterDmaEnabled()
        {
            return (_bus.Paula.Dmacon & (DmaMasterEnable | DmaBlitterEnable)) == (DmaMasterEnable | DmaBlitterEnable);
        }

        private bool RequiresDmaForCurrentBlit()
        {
            return _lineMode
                ? _useC
                : _useA || _useB || _useC || _useD;
        }

        private bool ShouldStallCpu()
        {
            return _busy && RequiresDmaForCurrentBlit() && IsBlitterDmaEnabled() && (_bus.Paula.Dmacon & DmaBlitterNasty) != 0;
        }

        private int GetAreaWordCycles()
        {
            var ticks = 4;
            if (_useB)
            {
                ticks += 2;
            }

            if (_useC)
            {
                ticks += 2;
            }

            if (_fillEnabled && !_useC)
            {
                ticks += 2;
            }

            return ticks;
        }

        private static int GetLinePixelCycles()
        {
            return 8;
        }

        private long GetCurrentStepEndCycle()
        {
            return _currentCycle + (_lineMode ? GetLinePixelCycles() : GetAreaWordCycles());
        }

        private void RecordBlitterDma(AmigaBusAccessResult access)
        {
            _lastDmaCycle = access.GrantedCycle;
            _completedMicroOps++;
        }

        private ushort ExecuteAreaScalar(ushort rawA, ushort rawB, ushort rawC, ushort mask)
        {
            rawA = (ushort)(rawA & mask);
            var a = ShiftSource(rawA, ref _previousA, GetActiveAreaSourceAShift(), _descending);
            var b = ShiftSource(rawB, ref _previousB, _useB ? _shiftB : _activeDataBShift, _descending);
            var output = ApplyMinterm(_minterm, a, b, rawC);
            if (_fillEnabled)
            {
                output = ApplyFill(output);
            }

            _areaKernelState.PreviousA = _previousA;
            _areaKernelState.PreviousB = _previousB;
            _areaKernelState.FillCarry = _fillCarry;
            return output;
        }

        // An all-ones immediate A mask is invariant across the data latch; keep
        // first/last masks on the active ASH instead of a stale BLTADAT shift.
        private int GetActiveAreaSourceAShift()
            => _useA || _activeDataA == 0xFFFF
                ? _shiftA
                : _activeDataAShift;

        private ushort ApplyFill(ushort value)
        {
            var fillCarry = _fillCarry;
            var output = BlitterKernelMath.ApplyFill(value, _fillExclusive, ref fillCarry);
            _fillCarry = fillCarry;
            return output;
        }

        private AmigaDeviceWordReadResult ReadAndStep(ref uint pointer, int step, long cycle)
        {
            var value = _bus.ReadChipWordForDeviceWithResult(
                AmigaBusRequester.Blitter,
                AmigaBusAccessKind.Blitter,
                GetEffectiveBlitterAddress(pointer),
                cycle);
            pointer = _bus.AddChipDmaPointerOffset(pointer, step);
            return value;
        }

        private AmigaBusAccessResult WriteAndStep(ref uint pointer, int step, ushort value, long cycle)
        {
            var access = _bus.WriteChipWordForDeviceWithResult(
                AmigaBusRequester.Blitter,
                AmigaBusAccessKind.Blitter,
                GetEffectiveBlitterAddress(pointer),
                value,
                cycle);
            pointer = _bus.AddChipDmaPointerOffset(pointer, step);
            return access;
        }

        private uint AddModulo(uint pointer, short modulo, bool descending)
        {
            var evenModulo = modulo & ~1;
            return _bus.AddChipDmaPointerOffset(pointer, descending ? -evenModulo : evenModulo);
        }

        private uint GetEffectiveBlitterAddress(uint pointer)
        {
            return _bus.MaskChipDmaAddress(pointer);
        }

        private static ushort RotateRight(ushort value, int bits)
        {
            bits &= 0x0F;
            return bits == 0
                ? value
                : (ushort)((value >> bits) | (value << (16 - bits)));
        }

        private static ushort ShiftSource(ushort current, ref ushort previous, int shift, bool descending)
            => BlitterKernelMath.ShiftSource(current, ref previous, shift, descending);

        private static ushort ApplyMinterm(byte minterm, ushort sourceA, ushort sourceB, ushort sourceC)
            => BlitterKernelMath.ApplyMinterm(minterm, sourceA, sourceB, sourceC);
    }
}
