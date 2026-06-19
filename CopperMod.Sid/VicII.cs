using System;

namespace CopperMod.Sid
{
    [HotPath]
    internal sealed class VicII
    {
        private const int SpriteCount = 8;
        private const int SpriteBytesPerLine = 3;
        private const int SpriteBytesPerShape = 63;
        private const int BadlineFirstRasterLine = 0x30;
        private const int BadlineLastRasterLine = 0xF7;
        private const int BaLowFirstPublicCycle = 12;
        private const int AecLowFirstPublicCycle = 15;
        private const int BadlineLastPublicCycle = 54;
        private const int BadlineColumns = 40;
        private const int DisplayCellHeight = 8;
        private static readonly SpriteSlot[] SpriteSlots =
        [
            new SpriteSlot(3, 1),
            new SpriteSlot(4, 4),
            new SpriteSlot(5, 7),
            new SpriteSlot(6, 10),
            new SpriteSlot(7, 13),
            new SpriteSlot(0, 55),
            new SpriteSlot(1, 58),
            new SpriteSlot(2, 61)
        ];

        private readonly C64ClockProfile _clock;
        private readonly byte[] _registers = new byte[0x40];
        private readonly SpriteState[] _sprites = new SpriteState[SpriteCount];
        private byte _irqFlags;
        private byte _irqMask;
        private ushort _rasterCompare;
        private int _rasterLine;
        private int _rasterCycle;
        private bool _verticalDisplayLatch;
        private bool _badlinePending;
        private bool _badlineActive;
        private bool _badlineArtificial;
        private int _badlineFirstFetchCycle;
        private int _badlineFetchIndex;
        private int _badlineRc;
        private int _badlineVc;
        private int _badlineVcBase;
        private int _badlineFliBugColumns;
        private VicMemoryAccessKind _badlineMemoryAccessKind;
        private ushort _badlineMatrixAddress;
        private ushort _badlineGraphicsAddress;
        private byte _badlineMatrixValue;
        private byte _badlineGraphicsValue;
        private bool _rasterCompareMatched;

        public VicII(C64ClockProfile clock)
        {
            _clock = clock;
        }

        public VicDebugState DebugState => new VicDebugState(
            _rasterLine,
            _rasterCycle,
            _rasterCompare,
            _irqFlags,
            _irqMask,
            InterruptLineAsserted,
            CurrentBusState.BadlineCandidate,
            CurrentBusState.BaLow,
            CurrentBusState.AecLow,
            CurrentBusState.TransitionWriteAllowed,
            CurrentBusState.SpriteBaLow,
            CurrentBusState.SpriteAecLow,
            CurrentBusState.ActiveSpriteMask,
            CurrentBusState.CurrentSpriteIndex,
            CurrentBusState.MemoryAccessKind,
            CurrentBusState.MemoryAddress,
            CurrentBusState.MemoryValue,
            CurrentBusState.BadlineActive,
            CurrentBusState.BadlineArtificial,
            CurrentBusState.BadlineFetchIndex,
            _badlineRc,
            _badlineVc,
            _badlineVcBase,
            CurrentBusState.BadlineFliBugColumns,
            CurrentBusState.BadlineMemoryAccessKind,
            CurrentBusState.BadlineMatrixAddress,
            CurrentBusState.BadlineGraphicsAddress,
            CurrentBusState.BadlineMatrixValue,
            CurrentBusState.BadlineGraphicsValue);

        public bool IrqLine => InterruptLineAsserted;

        public VicBusState CurrentBusState
        {
            get
            {
                var publicCycle = _rasterCycle + 1;
                var baLow = IsBadlineBaLow(publicCycle);
                var aecLow = IsBadlineAecLow(publicCycle);
                var spriteBus = GetSpriteBusState(publicCycle);
                return new VicBusState(
                    _rasterLine,
                    _rasterCycle,
                    publicCycle,
                    _badlinePending || _badlineActive,
                    baLow || spriteBus.BaLow,
                    aecLow || spriteBus.AecLow,
                    (baLow || spriteBus.BaLow) && !(aecLow || spriteBus.AecLow),
                    spriteBus.BaLow,
                    spriteBus.AecLow,
                    GetActiveSpriteMask(),
                    spriteBus.SpriteIndex,
                    spriteBus.MemoryAccessKind,
                    spriteBus.MemoryAddress,
                    spriteBus.MemoryValue,
                    _badlineActive,
                    _badlineArtificial,
                    _badlineFetchIndex,
                    _badlineFliBugColumns,
                    _badlineMemoryAccessKind,
                    _badlineMatrixAddress,
                    _badlineGraphicsAddress,
                    _badlineMatrixValue,
                    _badlineGraphicsValue);
            }
        }

        public void Reset()
        {
            System.Array.Clear(_registers);
            _irqFlags = 0;
            _irqMask = 0;
            _rasterCompare = 0;
            _rasterLine = 0;
            _rasterCycle = 0;
            _verticalDisplayLatch = false;
            _badlinePending = false;
            _badlineActive = false;
            _badlineArtificial = false;
            _badlineFirstFetchCycle = 0;
            _badlineFetchIndex = -1;
            _badlineRc = 0;
            _badlineVc = 0;
            _badlineVcBase = 0;
            _badlineFliBugColumns = 0;
            _badlineMemoryAccessKind = VicMemoryAccessKind.None;
            _badlineMatrixAddress = 0;
            _badlineGraphicsAddress = 0;
            _badlineMatrixValue = 0;
            _badlineGraphicsValue = 0;
            _rasterCompareMatched = true;
            for (var i = 0; i < _sprites.Length; i++)
            {
                _sprites[i] = new SpriteState
                {
                    LastPreparedRasterLine = -1
                };
            }

            // Reset starts on raster line 0 with compare 0, but the compare event has
            // not crossed yet. Keep it matched so enabling $D01A does not inherit a
            // stale raster IRQ before the first full frame wraps.
        }

        public byte Read(byte register)
        {
            return (register & 0x3F) switch
            {
                0x11 => (byte)((_registers[0x11] & 0x7F) | ((_rasterLine & 0x100) >> 1)),
                0x12 => (byte)_rasterLine,
                0x19 => ReadIrqFlags(),
                0x1A => _irqMask,
                _ => _registers[register & 0x3F]
            };
        }

        public void CopyRegisters(Span<byte> destination)
        {
            if (destination.Length < _registers.Length)
            {
                throw new ArgumentException("Destination is too small for VIC-II registers.", nameof(destination));
            }

            _registers.CopyTo(destination);
            destination[0x11] = (byte)((_registers[0x11] & 0x7F) | ((_rasterLine & 0x100) >> 1));
            destination[0x12] = (byte)_rasterLine;
            destination[0x19] = (byte)(_irqFlags | 0x70 | (InterruptLineAsserted ? 0x80 : 0x00));
            destination[0x1A] = _irqMask;
        }

        public void Write(byte register, byte value)
        {
            register = (byte)(register & 0x3F);
            _registers[register] = value;
            switch (register)
            {
                case 0x11:
                    _rasterCompare = (ushort)((_rasterCompare & 0x00FF) | ((value & 0x80) << 1));
                    EvaluateRasterCompareAfterCompareWrite();
                    break;
                case 0x12:
                    _rasterCompare = (ushort)((_rasterCompare & 0x0100) | value);
                    EvaluateRasterCompareAfterCompareWrite();
                    break;
                case 0x19:
                    _irqFlags &= (byte)~(value & 0x0F);
                    break;
                case 0x1A:
                    _irqMask = (byte)(value & 0x0F);
                    EvaluateRasterCompare();
                    break;
            }
        }

        public bool Tick()
        {
            return Tick(null, 0);
        }

        public bool Tick(VicMemoryReader? readMemory, int vicBankBase)
        {
            _rasterCycle++;
            if (_rasterCycle >= _clock.CyclesPerRasterLine)
            {
                CompleteRasterLine();
                _rasterCycle = 0;
                _rasterLine++;
                if (_rasterLine >= _clock.RasterLines)
                {
                    _rasterLine = 0;
                }

                BeginRasterLine();
                if (_rasterLine != _rasterCompare)
                {
                    _rasterCompareMatched = false;
                }

                EvaluateRasterCompare();
            }

            EvaluateBadlineState(_rasterCycle + 1);
            ProcessBadlineFetch(readMemory, vicBankBase);
            ProcessSpriteSlot(readMemory, vicBankBase);

            return InterruptLineAsserted;
        }

        public bool IsCpuReadBlocked()
        {
            var publicCycle = _rasterCycle + 1;
            return IsBadlineBaLow(publicCycle) || IsSpriteBaLow(publicCycle);
        }

        public bool IsCpuWriteBlocked()
        {
            var publicCycle = _rasterCycle + 1;
            return IsBadlineAecLow(publicCycle) || IsSpriteAecLow(publicCycle);
        }

        private void EvaluateRasterCompareAfterCompareWrite()
        {
            if (_rasterLine != _rasterCompare)
            {
                _rasterCompareMatched = false;
            }

            EvaluateRasterCompare();
        }

        private void EvaluateRasterCompare()
        {
            if (_rasterLine == _rasterCompare && !_rasterCompareMatched)
            {
                _irqFlags |= 0x01;
                _rasterCompareMatched = true;
            }
        }

        private byte ReadIrqFlags()
        {
            return (byte)(_irqFlags | (InterruptLineAsserted ? 0x80 : 0x00));
        }

        private bool InterruptLineAsserted => (_irqFlags & _irqMask & 0x0F) != 0;

        private bool IsBadlineCandidate()
        {
            return _rasterLine >= BadlineFirstRasterLine &&
                _rasterLine <= BadlineLastRasterLine &&
                _verticalDisplayLatch &&
                (_rasterLine & 0x07) == (_registers[0x11] & 0x07);
        }

        private void BeginRasterLine()
        {
            _badlinePending = false;
            _badlineActive = false;
            _badlineArtificial = false;
            _badlineFirstFetchCycle = 0;
            _badlineFetchIndex = -1;
            _badlineFliBugColumns = 0;
            _badlineMemoryAccessKind = VicMemoryAccessKind.None;
            _badlineMatrixAddress = 0;
            _badlineGraphicsAddress = 0;
            _badlineMatrixValue = 0;
            _badlineGraphicsValue = 0;
            if (_rasterLine == BadlineFirstRasterLine)
            {
                _verticalDisplayLatch = (_registers[0x11] & 0x10) != 0;
                _badlineRc = 0;
                _badlineVc = _badlineVcBase;
            }
            else if (_rasterLine == BadlineLastRasterLine + 1)
            {
                _verticalDisplayLatch = false;
            }
        }

        private void CompleteRasterLine()
        {
            if (!_verticalDisplayLatch ||
                _rasterLine < BadlineFirstRasterLine ||
                _rasterLine > BadlineLastRasterLine)
            {
                return;
            }

            if (_badlineActive)
            {
                _badlineVc = _badlineVcBase + BadlineColumns;
                _badlineRc = 1;
                return;
            }

            _badlineRc = (_badlineRc + 1) & 0x07;
            if (_badlineRc == 0)
            {
                _badlineVcBase = _badlineVc;
            }
        }

        private void EvaluateBadlineState(int publicCycle)
        {
            if (publicCycle < BaLowFirstPublicCycle || publicCycle > BadlineLastPublicCycle)
            {
                return;
            }

            var condition = IsBadlineCandidate();
            if (publicCycle == BaLowFirstPublicCycle)
            {
                _badlinePending = condition;
                _badlineArtificial = false;
                return;
            }

            if (publicCycle < AecLowFirstPublicCycle)
            {
                if (_badlinePending && !condition)
                {
                    _badlinePending = false;
                    _badlineArtificial = false;
                }
                else if (!_badlinePending && condition)
                {
                    _badlinePending = true;
                    _badlineArtificial = true;
                }

                return;
            }

            if (_badlineActive)
            {
                return;
            }

            if (_badlinePending)
            {
                if (condition)
                {
                    ActivateBadline(publicCycle, _badlineArtificial);
                }

                _badlinePending = false;
                return;
            }

            if (condition)
            {
                ActivateBadline(publicCycle, artificial: true);
            }
        }

        private void ActivateBadline(int publicCycle, bool artificial)
        {
            _badlineActive = true;
            _badlineArtificial = artificial;
            _badlineFirstFetchCycle = publicCycle;
            _badlineFetchIndex = -1;
            _badlineRc = 0;
            _badlineVc = _badlineVcBase;
            _badlineFliBugColumns = artificial
                ? System.Math.Min(BadlineColumns, System.Math.Max(0, publicCycle - AecLowFirstPublicCycle + 3))
                : 0;
        }

        private bool IsBadlineBaLow(int publicCycle)
        {
            return (_badlinePending || _badlineActive) &&
                publicCycle >= BaLowFirstPublicCycle &&
                publicCycle <= BadlineLastPublicCycle;
        }

        private bool IsBadlineAecLow(int publicCycle)
        {
            return _badlineActive &&
                publicCycle >= AecLowFirstPublicCycle &&
                publicCycle <= BadlineLastPublicCycle;
        }

        private void ProcessBadlineFetch(VicMemoryReader? readMemory, int vicBankBase)
        {
            var publicCycle = _rasterCycle + 1;
            if (publicCycle < AecLowFirstPublicCycle || publicCycle > BadlineLastPublicCycle)
            {
                _badlineMemoryAccessKind = VicMemoryAccessKind.None;
                return;
            }

            var column = publicCycle - AecLowFirstPublicCycle;
            if (column < 0 || column >= BadlineColumns)
            {
                _badlineMemoryAccessKind = VicMemoryAccessKind.None;
                return;
            }

            if (_badlineActive && publicCycle >= _badlineFirstFetchCycle)
            {
                _badlineFetchIndex = column;
                _badlineMatrixAddress = GetBadlineMatrixAddress(vicBankBase, column);
                _badlineMatrixValue = readMemory?.Invoke(_badlineMatrixAddress) ?? (byte)0;
                _badlineGraphicsAddress = GetBadlineGraphicsAddress(vicBankBase, column, _badlineMatrixValue);
                _badlineGraphicsValue = readMemory?.Invoke(_badlineGraphicsAddress) ?? (byte)0;
                _badlineMemoryAccessKind = VicMemoryAccessKind.BadlineScreen;
                _badlineVc = _badlineVcBase + column + 1;
                return;
            }

            _badlineMemoryAccessKind = VicMemoryAccessKind.None;
        }

        private void ProcessSpriteSlot(VicMemoryReader? readMemory, int vicBankBase)
        {
            var slot = FindSpriteDataSlot(_rasterCycle + 1);
            if (!slot.HasValue)
            {
                return;
            }

            var sprite = slot.Value.SpriteIndex;
            var phase = (_rasterCycle + 1) - slot.Value.FirstPublicCycle;
            var fetchLine = GetSpriteFetchLine(sprite, _rasterLine);
            PrepareSpriteForFetchLine(sprite, fetchLine);
            ref var state = ref _sprites[sprite];
            if (!state.DmaActive)
            {
                return;
            }

            var pointerAddress = GetSpritePointerAddress(vicBankBase, sprite);
            state.LastPointerAddress = pointerAddress;
            if (phase == 0)
            {
                state.Pointer = readMemory?.Invoke(pointerAddress) ?? (byte)0;
                state.LastMemoryAccessKind = VicMemoryAccessKind.SpritePointer;
                state.LastMemoryAddress = pointerAddress;
                state.LastMemoryValue = state.Pointer;
            }

            var dataIndex = state.Mc;
            var dataAddress = GetSpriteDataAddress(vicBankBase, state.Pointer, dataIndex);
            state.LastDataAddress = dataAddress;
            state.LastMemoryAccessKind = phase == 0 ? VicMemoryAccessKind.SpritePointer : VicMemoryAccessKind.SpriteData;
            state.LastMemoryAddress = phase == 0 ? pointerAddress : dataAddress;
            state.LastDataValue = readMemory?.Invoke(dataAddress) ?? (byte)0;
            state.LastMemoryValue = phase == 0 ? state.Pointer : state.LastDataValue;

            if (state.Mc < SpriteBytesPerShape)
            {
                state.Mc++;
            }

            if (phase == SpriteBytesPerLine - 1)
            {
                CompleteSpriteFetchLine(sprite);
            }
        }

        private void PrepareSpriteForFetchLine(int sprite, int fetchLine)
        {
            ref var state = ref _sprites[sprite];
            if (state.LastPreparedRasterLine == fetchLine)
            {
                return;
            }

            if (IsSpriteStartLine(sprite, fetchLine))
            {
                state.DmaActive = true;
                state.McBase = 0;
                state.Mc = 0;
                state.ExpansionFlipFlop = !IsSpriteVerticallyExpanded(sprite);
            }
            else if (state.DmaActive)
            {
                state.Mc = state.McBase;
            }

            state.LastPreparedRasterLine = fetchLine;
        }

        private void CompleteSpriteFetchLine(int sprite)
        {
            ref var state = ref _sprites[sprite];
            if (!state.DmaActive)
            {
                return;
            }

            var expanded = IsSpriteVerticallyExpanded(sprite);
            if (!expanded || state.ExpansionFlipFlop)
            {
                state.McBase = state.Mc;
            }

            state.ExpansionFlipFlop = expanded
                ? !state.ExpansionFlipFlop
                : true;
            if (state.McBase >= SpriteBytesPerShape)
            {
                state.DmaActive = false;
                state.McBase = SpriteBytesPerShape;
                state.Mc = SpriteBytesPerShape;
            }
        }

        private SpriteBusState GetSpriteBusState(int publicCycle)
        {
            var dataSlot = FindSpriteDataSlot(publicCycle);
            if (dataSlot.HasValue && IsSpriteFetchActive(dataSlot.Value.SpriteIndex, GetSpriteFetchLine(dataSlot.Value.SpriteIndex, _rasterLine)))
            {
                ref var state = ref _sprites[dataSlot.Value.SpriteIndex];
                return new SpriteBusState(
                    baLow: true,
                    aecLow: true,
                    dataSlot.Value.SpriteIndex,
                    state.LastMemoryAccessKind,
                    state.LastMemoryAddress,
                    state.LastMemoryValue);
            }

            var transitionSlot = FindSpriteTransitionSlot(publicCycle, out var transitionFetchLine);
            if (transitionSlot.HasValue && IsSpriteFetchActive(transitionSlot.Value.SpriteIndex, transitionFetchLine))
            {
                return new SpriteBusState(
                    baLow: true,
                    aecLow: false,
                    transitionSlot.Value.SpriteIndex,
                    VicMemoryAccessKind.None,
                    0,
                    0);
            }

            return SpriteBusState.None;
        }

        private bool IsSpriteBaLow(int publicCycle)
        {
            var dataSlot = FindSpriteDataSlot(publicCycle);
            if (dataSlot.HasValue &&
                IsSpriteFetchActive(dataSlot.Value.SpriteIndex, GetSpriteFetchLine(dataSlot.Value.SpriteIndex, _rasterLine)))
            {
                return true;
            }

            var transitionSlot = FindSpriteTransitionSlot(publicCycle, out var transitionFetchLine);
            return transitionSlot.HasValue &&
                IsSpriteFetchActive(transitionSlot.Value.SpriteIndex, transitionFetchLine);
        }

        private bool IsSpriteAecLow(int publicCycle)
        {
            var dataSlot = FindSpriteDataSlot(publicCycle);
            return dataSlot.HasValue &&
                IsSpriteFetchActive(dataSlot.Value.SpriteIndex, GetSpriteFetchLine(dataSlot.Value.SpriteIndex, _rasterLine));
        }

        private SpriteSlot? FindSpriteDataSlot(int publicCycle)
        {
            for (var i = 0; i < SpriteSlots.Length; i++)
            {
                var slot = SpriteSlots[i];
                if (publicCycle >= slot.FirstPublicCycle &&
                    publicCycle < slot.FirstPublicCycle + SpriteBytesPerLine)
                {
                    return slot;
                }
            }

            return null;
        }

        private SpriteSlot? FindSpriteTransitionSlot(int publicCycle, out int fetchLine)
        {
            for (var i = 0; i < SpriteSlots.Length; i++)
            {
                var slot = SpriteSlots[i];
                var firstTransitionCycle = slot.FirstPublicCycle - SpriteBytesPerLine;
                if (firstTransitionCycle >= 1)
                {
                    if (publicCycle >= firstTransitionCycle && publicCycle < slot.FirstPublicCycle)
                    {
                        fetchLine = GetSpriteFetchLine(slot.SpriteIndex, _rasterLine);
                        return slot;
                    }

                    continue;
                }

                var wrappedStart = _clock.CyclesPerRasterLine + firstTransitionCycle;
                if (publicCycle >= wrappedStart)
                {
                    fetchLine = GetSpriteFetchLine(slot.SpriteIndex, NextRasterLine(_rasterLine));
                    return slot;
                }
            }

            fetchLine = _rasterLine;
            return null;
        }

        private bool IsSpriteFetchActive(int sprite, int fetchLine)
        {
            var state = _sprites[sprite];
            return state.DmaActive || IsSpriteStartLine(sprite, fetchLine);
        }

        private bool IsSpriteStartLine(int sprite, int fetchLine)
        {
            return (_registers[0x15] & (1 << sprite)) != 0 &&
                ((fetchLine & 0xFF) == _registers[(sprite * 2) + 1]);
        }

        private bool IsSpriteVerticallyExpanded(int sprite)
        {
            return (_registers[0x17] & (1 << sprite)) != 0;
        }

        private int GetSpriteFetchLine(int sprite, int rasterLine)
        {
            return sprite <= 2
                ? NextRasterLine(rasterLine)
                : rasterLine;
        }

        private int NextRasterLine(int rasterLine)
        {
            var next = rasterLine + 1;
            return next >= _clock.RasterLines ? 0 : next;
        }

        private int GetActiveSpriteMask()
        {
            var mask = 0;
            for (var i = 0; i < _sprites.Length; i++)
            {
                if (_sprites[i].DmaActive)
                {
                    mask |= 1 << i;
                }
            }

            return mask;
        }

        private ushort GetSpritePointerAddress(int vicBankBase, int sprite)
        {
            var screenBase = (_registers[0x18] & 0xF0) << 6;
            return (ushort)((vicBankBase + screenBase + 0x03F8 + sprite) & 0xFFFF);
        }

        private static ushort GetSpriteDataAddress(int vicBankBase, byte pointer, int mc)
        {
            return (ushort)((vicBankBase + (pointer * 64) + mc) & 0xFFFF);
        }

        private ushort GetBadlineMatrixAddress(int vicBankBase, int column)
        {
            var screenBase = (_registers[0x18] & 0xF0) << 6;
            return (ushort)((vicBankBase + screenBase + ((_badlineVcBase + column) & 0x03FF)) & 0xFFFF);
        }

        private ushort GetBadlineGraphicsAddress(int vicBankBase, int column, byte matrixValue)
        {
            if (IsBitmapMode())
            {
                return GetBitmapGraphicsAddress(vicBankBase, column);
            }

            var character = IsEcmMode()
                ? matrixValue & 0x3F
                : matrixValue;
            var characterBase = (_registers[0x18] & 0x0E) << 10;
            return (ushort)((vicBankBase + characterBase + (character * 8) + (_badlineRc & 0x07)) & 0xFFFF);
        }

        private ushort GetBitmapGraphicsAddress(int vicBankBase, int column)
        {
            var bitmapBase = (_registers[0x18] & 0x08) << 10;
            var displayRow = System.Math.Max(0, GetDisplayRow(_rasterLine));
            var rowBlock = displayRow / DisplayCellHeight;
            return (ushort)((vicBankBase + bitmapBase + (rowBlock * 320) + (column * 8) + (_badlineRc & 0x07)) & 0xFFFF);
        }

        private int GetDisplayRow(int y)
        {
            return y - GetDisplayTopLine();
        }

        private int GetDisplayTopLine()
        {
            return IsTwentyFiveRowMode() ? BadlineFirstRasterLine : BadlineFirstRasterLine + 4;
        }

        private bool IsTwentyFiveRowMode()
        {
            return (_registers[0x11] & 0x08) != 0;
        }

        private bool IsBitmapMode()
        {
            return (_registers[0x11] & 0x20) != 0;
        }

        private bool IsEcmMode()
        {
            return (_registers[0x11] & 0x40) != 0;
        }

        private readonly struct SpriteSlot
        {
            public SpriteSlot(int spriteIndex, int firstPublicCycle)
            {
                SpriteIndex = spriteIndex;
                FirstPublicCycle = firstPublicCycle;
            }

            public int SpriteIndex { get; }

            public int FirstPublicCycle { get; }
        }

        private struct SpriteState
        {
            public bool DmaActive;
            public int Mc;
            public int McBase;
            public bool ExpansionFlipFlop;
            public byte Pointer;
            public ushort LastPointerAddress;
            public ushort LastDataAddress;
            public byte LastDataValue;
            public int LastPreparedRasterLine;
            public VicMemoryAccessKind LastMemoryAccessKind;
            public ushort LastMemoryAddress;
            public byte LastMemoryValue;
        }

        private readonly struct SpriteBusState
        {
            public SpriteBusState(
                bool baLow,
                bool aecLow,
                int spriteIndex,
                VicMemoryAccessKind memoryAccessKind,
                ushort memoryAddress,
                byte memoryValue)
            {
                BaLow = baLow;
                AecLow = aecLow;
                SpriteIndex = spriteIndex;
                MemoryAccessKind = memoryAccessKind;
                MemoryAddress = memoryAddress;
                MemoryValue = memoryValue;
            }

            public bool BaLow { get; }

            public bool AecLow { get; }

            public int SpriteIndex { get; }

            public VicMemoryAccessKind MemoryAccessKind { get; }

            public ushort MemoryAddress { get; }

            public byte MemoryValue { get; }

            public static SpriteBusState None { get; } = new SpriteBusState(
                false,
                false,
                -1,
                VicMemoryAccessKind.None,
                0,
                0);
        }

    }

    internal delegate byte VicMemoryReader(ushort address);

    internal enum VicMemoryAccessKind
    {
        None,
        SpritePointer,
        SpriteData,
        BadlineScreen,
        BadlineGraphics
    }

    internal readonly struct VicBusState
    {
        public VicBusState(
            int rasterLine,
            int rasterCycle,
            int publicCycle,
            bool badlineCandidate,
            bool baLow,
            bool aecLow,
            bool transitionWriteAllowed,
            bool spriteBaLow,
            bool spriteAecLow,
            int activeSpriteMask,
            int currentSpriteIndex,
            VicMemoryAccessKind memoryAccessKind,
            ushort memoryAddress,
            byte memoryValue,
            bool badlineActive,
            bool badlineArtificial,
            int badlineFetchIndex,
            int badlineFliBugColumns,
            VicMemoryAccessKind badlineMemoryAccessKind,
            ushort badlineMatrixAddress,
            ushort badlineGraphicsAddress,
            byte badlineMatrixValue,
            byte badlineGraphicsValue)
        {
            RasterLine = rasterLine;
            RasterCycle = rasterCycle;
            PublicCycle = publicCycle;
            BadlineCandidate = badlineCandidate;
            BaLow = baLow;
            AecLow = aecLow;
            TransitionWriteAllowed = transitionWriteAllowed;
            SpriteBaLow = spriteBaLow;
            SpriteAecLow = spriteAecLow;
            ActiveSpriteMask = activeSpriteMask;
            CurrentSpriteIndex = currentSpriteIndex;
            MemoryAccessKind = memoryAccessKind;
            MemoryAddress = memoryAddress;
            MemoryValue = memoryValue;
            BadlineActive = badlineActive;
            BadlineArtificial = badlineArtificial;
            BadlineFetchIndex = badlineFetchIndex;
            BadlineFliBugColumns = badlineFliBugColumns;
            BadlineMemoryAccessKind = badlineMemoryAccessKind;
            BadlineMatrixAddress = badlineMatrixAddress;
            BadlineGraphicsAddress = badlineGraphicsAddress;
            BadlineMatrixValue = badlineMatrixValue;
            BadlineGraphicsValue = badlineGraphicsValue;
        }

        public int RasterLine { get; }

        public int RasterCycle { get; }

        public int PublicCycle { get; }

        public bool BadlineCandidate { get; }

        public bool BaLow { get; }

        public bool AecLow { get; }

        public bool TransitionWriteAllowed { get; }

        public bool SpriteBaLow { get; }

        public bool SpriteAecLow { get; }

        public int ActiveSpriteMask { get; }

        public int CurrentSpriteIndex { get; }

        public VicMemoryAccessKind MemoryAccessKind { get; }

        public ushort MemoryAddress { get; }

        public byte MemoryValue { get; }

        public bool BadlineActive { get; }

        public bool BadlineArtificial { get; }

        public int BadlineFetchIndex { get; }

        public int BadlineFliBugColumns { get; }

        public VicMemoryAccessKind BadlineMemoryAccessKind { get; }

        public ushort BadlineMatrixAddress { get; }

        public ushort BadlineGraphicsAddress { get; }

        public byte BadlineMatrixValue { get; }

        public byte BadlineGraphicsValue { get; }
    }
}
