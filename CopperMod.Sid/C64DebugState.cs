namespace CopperMod.Sid
{
    internal enum C64InterruptSource
    {
        None,
        Cia1,
        Vic,
        Cia2
    }

    internal readonly struct C64MachineDebugState
    {
        public C64MachineDebugState(
            long hardwareCycle,
            CiaDebugState cia1,
            CiaDebugState cia2,
            VicDebugState vic,
            C64MemoryBankState memoryBank,
            BasicRsidDebugState basic,
            bool irqLine,
            bool nmiPending,
            bool cia2NmiLine,
            C64InterruptSource lastInterruptSource)
        {
            HardwareCycle = hardwareCycle;
            Cia1 = cia1;
            Cia2 = cia2;
            Vic = vic;
            MemoryBank = memoryBank;
            Basic = basic;
            IrqLine = irqLine;
            NmiPending = nmiPending;
            Cia2NmiLine = cia2NmiLine;
            LastInterruptSource = lastInterruptSource;
        }

        public long HardwareCycle { get; }

        public CiaDebugState Cia1 { get; }

        public CiaDebugState Cia2 { get; }

        public VicDebugState Vic { get; }

        public C64MemoryBankState MemoryBank { get; }

        public BasicRsidDebugState Basic { get; }

        public bool IrqLine { get; }

        public bool NmiPending { get; }

        public bool Cia2NmiLine { get; }

        public C64InterruptSource LastInterruptSource { get; }
    }

    internal readonly struct C64MemoryBankState
    {
        public C64MemoryBankState(byte direction, byte value, byte effectiveValue, bool basicVisible, bool kernalVisible, bool ioVisible, bool characterVisible)
        {
            Direction = direction;
            Value = value;
            EffectiveValue = effectiveValue;
            BasicVisible = basicVisible;
            KernalVisible = kernalVisible;
            IoVisible = ioVisible;
            CharacterVisible = characterVisible;
        }

        public byte Direction { get; }

        public byte Value { get; }

        public byte EffectiveValue { get; }

        public bool BasicVisible { get; }

        public bool KernalVisible { get; }

        public bool IoVisible { get; }

        public bool CharacterVisible { get; }
    }

    internal readonly struct CiaDebugState
    {
        public CiaDebugState(
            byte portA,
            byte portB,
            byte dataDirectionA,
            byte dataDirectionB,
            ushort timerA,
            ushort timerALatch,
            ushort timerB,
            ushort timerBLatch,
            byte controlA,
            byte controlB,
            byte interruptMask,
            byte interruptData,
            bool interruptLine,
            byte todTenths,
            byte todSeconds,
            byte todMinutes,
            byte todHours)
        {
            PortA = portA;
            PortB = portB;
            DataDirectionA = dataDirectionA;
            DataDirectionB = dataDirectionB;
            TimerA = timerA;
            TimerALatch = timerALatch;
            TimerB = timerB;
            TimerBLatch = timerBLatch;
            ControlA = controlA;
            ControlB = controlB;
            InterruptMask = interruptMask;
            InterruptData = interruptData;
            InterruptLine = interruptLine;
            TodTenths = todTenths;
            TodSeconds = todSeconds;
            TodMinutes = todMinutes;
            TodHours = todHours;
        }

        public byte PortA { get; }

        public byte PortB { get; }

        public byte DataDirectionA { get; }

        public byte DataDirectionB { get; }

        public ushort TimerA { get; }

        public ushort TimerALatch { get; }

        public ushort TimerB { get; }

        public ushort TimerBLatch { get; }

        public byte ControlA { get; }

        public byte ControlB { get; }

        public byte InterruptMask { get; }

        public byte InterruptData { get; }

        public bool InterruptLine { get; }

        public byte TodTenths { get; }

        public byte TodSeconds { get; }

        public byte TodMinutes { get; }

        public byte TodHours { get; }
    }

    internal readonly struct VicDebugState
    {
        public VicDebugState(
            int rasterLine,
            int rasterCycle,
            ushort rasterCompare,
            byte irqFlags,
            byte irqMask,
            bool irqLine,
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
            int badlineRc,
            int badlineVc,
            int badlineVcBase,
            int badlineFliBugColumns,
            VicMemoryAccessKind badlineMemoryAccessKind,
            ushort badlineMatrixAddress,
            ushort badlineGraphicsAddress,
            byte badlineMatrixValue,
            byte badlineGraphicsValue)
        {
            RasterLine = rasterLine;
            RasterCycle = rasterCycle;
            RasterCompare = rasterCompare;
            IrqFlags = irqFlags;
            IrqMask = irqMask;
            IrqLine = irqLine;
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
            BadlineRc = badlineRc;
            BadlineVc = badlineVc;
            BadlineVcBase = badlineVcBase;
            BadlineFliBugColumns = badlineFliBugColumns;
            BadlineMemoryAccessKind = badlineMemoryAccessKind;
            BadlineMatrixAddress = badlineMatrixAddress;
            BadlineGraphicsAddress = badlineGraphicsAddress;
            BadlineMatrixValue = badlineMatrixValue;
            BadlineGraphicsValue = badlineGraphicsValue;
        }

        public int RasterLine { get; }

        public int RasterCycle { get; }

        public ushort RasterCompare { get; }

        public byte IrqFlags { get; }

        public byte IrqMask { get; }

        public bool IrqLine { get; }

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

        public int BadlineRc { get; }

        public int BadlineVc { get; }

        public int BadlineVcBase { get; }

        public int BadlineFliBugColumns { get; }

        public VicMemoryAccessKind BadlineMemoryAccessKind { get; }

        public ushort BadlineMatrixAddress { get; }

        public ushort BadlineGraphicsAddress { get; }

        public byte BadlineMatrixValue { get; }

        public byte BadlineGraphicsValue { get; }
    }

    internal readonly struct BasicRsidDebugState
    {
        public BasicRsidDebugState(
            bool enabled,
            bool active,
            bool ended,
            bool halted,
            int currentLineNumber,
            int statementCount,
            long cyclesConsumed,
            byte lastUnsupportedToken,
            string? lastDiagnostic)
        {
            Enabled = enabled;
            Active = active;
            Ended = ended;
            Halted = halted;
            CurrentLineNumber = currentLineNumber;
            StatementCount = statementCount;
            CyclesConsumed = cyclesConsumed;
            LastUnsupportedToken = lastUnsupportedToken;
            LastDiagnostic = lastDiagnostic;
        }

        public bool Enabled { get; }

        public bool Active { get; }

        public bool Ended { get; }

        public bool Halted { get; }

        public int CurrentLineNumber { get; }

        public int StatementCount { get; }

        public long CyclesConsumed { get; }

        public byte LastUnsupportedToken { get; }

        public string? LastDiagnostic { get; }

        public static BasicRsidDebugState Disabled { get; } = new BasicRsidDebugState(
            enabled: false,
            active: false,
            ended: false,
            halted: false,
            currentLineNumber: 0,
            statementCount: 0,
            cyclesConsumed: 0,
            lastUnsupportedToken: 0,
            lastDiagnostic: null);
    }
}
