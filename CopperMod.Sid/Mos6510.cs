using System;

namespace CopperMod.Sid
{
    internal interface ICpuBus
    {
        byte Read(ushort address);

        void Write(ushort address, byte value, int cycleOffset);
    }

    internal sealed class Mos6510
    {
        private const byte Carry = 0x01;
        private const byte Zero = 0x02;
        private const byte InterruptDisable = 0x04;
        private const byte Decimal = 0x08;
        private const byte Break = 0x10;
        private const byte Unused = 0x20;
        private const byte Overflow = 0x40;
        private const byte Negative = 0x80;

        private readonly ICpuBus _bus;

        public Mos6510(ICpuBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            Reset();
        }

        public byte A { get; set; }

        public byte X { get; set; }

        public byte Y { get; set; }

        public byte StackPointer { get; set; }

        public ushort ProgramCounter { get; set; }

        public byte Status { get; set; }

        public long Cycles { get; private set; }

        public bool Halted { get; private set; }

        public byte LastOpcode { get; private set; }

        public void Reset(ushort programCounter = 0)
        {
            A = 0;
            X = 0;
            Y = 0;
            StackPointer = 0xFD;
            ProgramCounter = programCounter;
            Status = Unused | InterruptDisable;
            Cycles = 0;
            Halted = false;
        }

        public void ResetCycles()
        {
            Cycles = 0;
        }

        public void AdvanceCycles(long cycles)
        {
            if (cycles <= 0)
            {
                return;
            }

            Cycles += cycles;
        }

        public void BeginSubroutine(ushort address, byte accumulator, byte x = 0, byte y = 0)
        {
            A = accumulator;
            X = x;
            Y = y;
            StackPointer = 0xFD;
            ProgramCounter = address;
            Halted = false;
            PushWord(0xFFFE, 0);
        }

        public void RequestIrq()
        {
            if (GetFlag(InterruptDisable))
            {
                return;
            }

            PushWord(ProgramCounter, 1);
            Push((byte)(Status & ~Break), 3);
            SetFlag(InterruptDisable, true);
            ProgramCounter = ReadWord(0xFFFE);
            Cycles += 7;
        }

        public void RequestNmi()
        {
            PushWord(ProgramCounter, 1);
            Push((byte)(Status & ~Break), 3);
            SetFlag(InterruptDisable, true);
            ProgramCounter = ReadWord(0xFFFA);
            Cycles += 7;
        }

        public int ExecuteInstruction()
        {
            if (Halted)
            {
                Cycles++;
                return 1;
            }

            var start = Cycles;
            var opcode = FetchByte();
            LastOpcode = opcode;
            switch (opcode)
            {
                case 0x00: Brk(); AddCycles(7); break;
                case 0x01: Ora(Read(IndirectX()), 6); break;
                case 0x02: Jam(); break;
                case 0x03: Slo(IndirectX(), 8); break;
                case 0x04: Nop(ZeroPage(), 3); break;
                case 0x05: Ora(Read(ZeroPage()), 3); break;
                case 0x06: AslMemory(ZeroPage(), 5); break;
                case 0x07: Slo(ZeroPage(), 5); break;
                case 0x08: Push((byte)(Status | Break | Unused), 2); AddCycles(3); break;
                case 0x09: Ora(FetchByte(), 2); break;
                case 0x0A: A = Asl(A); AddCycles(2); break;
                case 0x0B: Anc(FetchByte()); AddCycles(2); break;
                case 0x0C: Nop(Absolute(), 4); break;
                case 0x0D: Ora(Read(Absolute()), 4); break;
                case 0x0E: AslMemory(Absolute(), 6); break;
                case 0x0F: Slo(Absolute(), 6); break;
                case 0x10: Branch(!GetFlag(Negative)); break;
                case 0x11: Ora(Read(IndirectY(out var p11)), 5 + p11); break;
                case 0x12: Jam(); break;
                case 0x13: Slo(IndirectY(out _), 8); break;
                case 0x14: Nop(ZeroPageX(), 4); break;
                case 0x15: Ora(Read(ZeroPageX()), 4); break;
                case 0x16: AslMemory(ZeroPageX(), 6); break;
                case 0x17: Slo(ZeroPageX(), 6); break;
                case 0x18: SetFlag(Carry, false); AddCycles(2); break;
                case 0x19: Ora(Read(AbsoluteY(out var p19)), 4 + p19); break;
                case 0x1A: AddCycles(2); break;
                case 0x1B: Slo(AbsoluteY(out _), 7); break;
                case 0x1C: Nop(AbsoluteX(out var p1c), 4 + p1c); break;
                case 0x1D: Ora(Read(AbsoluteX(out var p1d)), 4 + p1d); break;
                case 0x1E: AslMemory(AbsoluteX(out _), 7); break;
                case 0x1F: Slo(AbsoluteX(out _), 7); break;
                case 0x20: Jsr(); AddCycles(6); break;
                case 0x21: And(Read(IndirectX()), 6); break;
                case 0x22: Jam(); break;
                case 0x23: Rla(IndirectX(), 8); break;
                case 0x24: Bit(Read(ZeroPage()), 3); break;
                case 0x25: And(Read(ZeroPage()), 3); break;
                case 0x26: RolMemory(ZeroPage(), 5); break;
                case 0x27: Rla(ZeroPage(), 5); break;
                case 0x28: Status = (byte)((Pull(3) & ~Break) | Unused); AddCycles(4); break;
                case 0x29: And(FetchByte(), 2); break;
                case 0x2A: A = Rol(A); AddCycles(2); break;
                case 0x2B: Anc(FetchByte()); AddCycles(2); break;
                case 0x2C: Bit(Read(Absolute()), 4); break;
                case 0x2D: And(Read(Absolute()), 4); break;
                case 0x2E: RolMemory(Absolute(), 6); break;
                case 0x2F: Rla(Absolute(), 6); break;
                case 0x30: Branch(GetFlag(Negative)); break;
                case 0x31: And(Read(IndirectY(out var p31)), 5 + p31); break;
                case 0x32: Jam(); break;
                case 0x33: Rla(IndirectY(out _), 8); break;
                case 0x34: Nop(ZeroPageX(), 4); break;
                case 0x35: And(Read(ZeroPageX()), 4); break;
                case 0x36: RolMemory(ZeroPageX(), 6); break;
                case 0x37: Rla(ZeroPageX(), 6); break;
                case 0x38: SetFlag(Carry, true); AddCycles(2); break;
                case 0x39: And(Read(AbsoluteY(out var p39)), 4 + p39); break;
                case 0x3A: AddCycles(2); break;
                case 0x3B: Rla(AbsoluteY(out _), 7); break;
                case 0x3C: Nop(AbsoluteX(out var p3c), 4 + p3c); break;
                case 0x3D: And(Read(AbsoluteX(out var p3d)), 4 + p3d); break;
                case 0x3E: RolMemory(AbsoluteX(out _), 7); break;
                case 0x3F: Rla(AbsoluteX(out _), 7); break;
                case 0x40: Rti(); AddCycles(6); break;
                case 0x41: Eor(Read(IndirectX()), 6); break;
                case 0x42: Jam(); break;
                case 0x43: Sre(IndirectX(), 8); break;
                case 0x44: Nop(ZeroPage(), 3); break;
                case 0x45: Eor(Read(ZeroPage()), 3); break;
                case 0x46: LsrMemory(ZeroPage(), 5); break;
                case 0x47: Sre(ZeroPage(), 5); break;
                case 0x48: Push(A, 2); AddCycles(3); break;
                case 0x49: Eor(FetchByte(), 2); break;
                case 0x4A: A = Lsr(A); AddCycles(2); break;
                case 0x4B: Alr(FetchByte()); AddCycles(2); break;
                case 0x4C: ProgramCounter = Absolute(); AddCycles(3); break;
                case 0x4D: Eor(Read(Absolute()), 4); break;
                case 0x4E: LsrMemory(Absolute(), 6); break;
                case 0x4F: Sre(Absolute(), 6); break;
                case 0x50: Branch(!GetFlag(Overflow)); break;
                case 0x51: Eor(Read(IndirectY(out var p51)), 5 + p51); break;
                case 0x52: Jam(); break;
                case 0x53: Sre(IndirectY(out _), 8); break;
                case 0x54: Nop(ZeroPageX(), 4); break;
                case 0x55: Eor(Read(ZeroPageX()), 4); break;
                case 0x56: LsrMemory(ZeroPageX(), 6); break;
                case 0x57: Sre(ZeroPageX(), 6); break;
                case 0x58: SetFlag(InterruptDisable, false); AddCycles(2); break;
                case 0x59: Eor(Read(AbsoluteY(out var p59)), 4 + p59); break;
                case 0x5A: AddCycles(2); break;
                case 0x5B: Sre(AbsoluteY(out _), 7); break;
                case 0x5C: Nop(AbsoluteX(out var p5c), 4 + p5c); break;
                case 0x5D: Eor(Read(AbsoluteX(out var p5d)), 4 + p5d); break;
                case 0x5E: LsrMemory(AbsoluteX(out _), 7); break;
                case 0x5F: Sre(AbsoluteX(out _), 7); break;
                case 0x60: Rts(); AddCycles(6); break;
                case 0x61: Adc(Read(IndirectX()), 6); break;
                case 0x62: Jam(); break;
                case 0x63: Rra(IndirectX(), 8); break;
                case 0x64: Nop(ZeroPage(), 3); break;
                case 0x65: Adc(Read(ZeroPage()), 3); break;
                case 0x66: RorMemory(ZeroPage(), 5); break;
                case 0x67: Rra(ZeroPage(), 5); break;
                case 0x68: A = Pull(3); SetZn(A); AddCycles(4); break;
                case 0x69: Adc(FetchByte(), 2); break;
                case 0x6A: A = Ror(A); AddCycles(2); break;
                case 0x6B: Arr(FetchByte()); AddCycles(2); break;
                case 0x6C: ProgramCounter = ReadWord(Absolute(), wrapPage: true); AddCycles(5); break;
                case 0x6D: Adc(Read(Absolute()), 4); break;
                case 0x6E: RorMemory(Absolute(), 6); break;
                case 0x6F: Rra(Absolute(), 6); break;
                case 0x70: Branch(GetFlag(Overflow)); break;
                case 0x71: Adc(Read(IndirectY(out var p71)), 5 + p71); break;
                case 0x72: Jam(); break;
                case 0x73: Rra(IndirectY(out _), 8); break;
                case 0x74: Nop(ZeroPageX(), 4); break;
                case 0x75: Adc(Read(ZeroPageX()), 4); break;
                case 0x76: RorMemory(ZeroPageX(), 6); break;
                case 0x77: Rra(ZeroPageX(), 6); break;
                case 0x78: SetFlag(InterruptDisable, true); AddCycles(2); break;
                case 0x79: Adc(Read(AbsoluteY(out var p79)), 4 + p79); break;
                case 0x7A: AddCycles(2); break;
                case 0x7B: Rra(AbsoluteY(out _), 7); break;
                case 0x7C: Nop(AbsoluteX(out var p7c), 4 + p7c); break;
                case 0x7D: Adc(Read(AbsoluteX(out var p7d)), 4 + p7d); break;
                case 0x7E: RorMemory(AbsoluteX(out _), 7); break;
                case 0x7F: Rra(AbsoluteX(out _), 7); break;
                case 0x80: Nop(FetchByte(), 2); break;
                case 0x81: Write(IndirectX(), (byte)(A & X), 6); break;
                case 0x82: Nop(FetchByte(), 2); break;
                case 0x83: Write(IndirectX(), (byte)(A & X), 6); break;
                case 0x84: Write(ZeroPage(), Y, 3); break;
                case 0x85: Write(ZeroPage(), A, 3); break;
                case 0x86: Write(ZeroPage(), X, 3); break;
                case 0x87: Write(ZeroPage(), (byte)(A & X), 3); break;
                case 0x88: Y--; SetZn(Y); AddCycles(2); break;
                case 0x89: Nop(FetchByte(), 2); break;
                case 0x8A: A = X; SetZn(A); AddCycles(2); break;
                case 0x8B: A = (byte)(X & FetchByte()); SetZn(A); AddCycles(2); break;
                case 0x8C: Write(Absolute(), Y, 4); break;
                case 0x8D: Write(Absolute(), A, 4); break;
                case 0x8E: Write(Absolute(), X, 4); break;
                case 0x8F: Write(Absolute(), (byte)(A & X), 4); break;
                case 0x90: Branch(!GetFlag(Carry)); break;
                case 0x91: Write(IndirectY(out _), A, 6); break;
                case 0x92: Jam(); break;
                case 0x93: Ahx(IndirectY(out _), 6); break;
                case 0x94: Write(ZeroPageX(), Y, 4); break;
                case 0x95: Write(ZeroPageX(), A, 4); break;
                case 0x96: Write(ZeroPageY(), X, 4); break;
                case 0x97: Write(ZeroPageY(), (byte)(A & X), 4); break;
                case 0x98: A = Y; SetZn(A); AddCycles(2); break;
                case 0x99: Write(AbsoluteY(out _), A, 5); break;
                case 0x9A: StackPointer = X; AddCycles(2); break;
                case 0x9B: Tas(AbsoluteY(out _), 5); break;
                case 0x9C: Shy(AbsoluteX(out _), 5); break;
                case 0x9D: Write(AbsoluteX(out _), A, 5); break;
                case 0x9E: Shx(AbsoluteY(out _), 5); break;
                case 0x9F: Ahx(AbsoluteY(out _), 5); break;
                case 0xA0: Ldy(FetchByte(), 2); break;
                case 0xA1: Lda(Read(IndirectX()), 6); break;
                case 0xA2: Ldx(FetchByte(), 2); break;
                case 0xA3: Lax(Read(IndirectX()), 6); break;
                case 0xA4: Ldy(Read(ZeroPage()), 3); break;
                case 0xA5: Lda(Read(ZeroPage()), 3); break;
                case 0xA6: Ldx(Read(ZeroPage()), 3); break;
                case 0xA7: Lax(Read(ZeroPage()), 3); break;
                case 0xA8: Y = A; SetZn(Y); AddCycles(2); break;
                case 0xA9: Lda(FetchByte(), 2); break;
                case 0xAA: X = A; SetZn(X); AddCycles(2); break;
                case 0xAB: Lax((byte)(A & FetchByte()), 2); break;
                case 0xAC: Ldy(Read(Absolute()), 4); break;
                case 0xAD: Lda(Read(Absolute()), 4); break;
                case 0xAE: Ldx(Read(Absolute()), 4); break;
                case 0xAF: Lax(Read(Absolute()), 4); break;
                case 0xB0: Branch(GetFlag(Carry)); break;
                case 0xB1: Lda(Read(IndirectY(out var pb1)), 5 + pb1); break;
                case 0xB2: Jam(); break;
                case 0xB3: Lax(Read(IndirectY(out var pb3)), 5 + pb3); break;
                case 0xB4: Ldy(Read(ZeroPageX()), 4); break;
                case 0xB5: Lda(Read(ZeroPageX()), 4); break;
                case 0xB6: Ldx(Read(ZeroPageY()), 4); break;
                case 0xB7: Lax(Read(ZeroPageY()), 4); break;
                case 0xB8: SetFlag(Overflow, false); AddCycles(2); break;
                case 0xB9: Lda(Read(AbsoluteY(out var pb9)), 4 + pb9); break;
                case 0xBA: X = StackPointer; SetZn(X); AddCycles(2); break;
                case 0xBB: Las(Read(AbsoluteY(out var pbb))); AddCycles(4 + pbb); break;
                case 0xBC: Ldy(Read(AbsoluteX(out var pbc)), 4 + pbc); break;
                case 0xBD: Lda(Read(AbsoluteX(out var pbd)), 4 + pbd); break;
                case 0xBE: Ldx(Read(AbsoluteY(out var pbe)), 4 + pbe); break;
                case 0xBF: Lax(Read(AbsoluteY(out var pbf)), 4 + pbf); break;
                case 0xC0: Compare(Y, FetchByte(), 2); break;
                case 0xC1: Compare(A, Read(IndirectX()), 6); break;
                case 0xC2: Nop(FetchByte(), 2); break;
                case 0xC3: Dcp(IndirectX(), 8); break;
                case 0xC4: Compare(Y, Read(ZeroPage()), 3); break;
                case 0xC5: Compare(A, Read(ZeroPage()), 3); break;
                case 0xC6: DecMemory(ZeroPage(), 5); break;
                case 0xC7: Dcp(ZeroPage(), 5); break;
                case 0xC8: Y++; SetZn(Y); AddCycles(2); break;
                case 0xC9: Compare(A, FetchByte(), 2); break;
                case 0xCA: X--; SetZn(X); AddCycles(2); break;
                case 0xCB: Axs(FetchByte()); AddCycles(2); break;
                case 0xCC: Compare(Y, Read(Absolute()), 4); break;
                case 0xCD: Compare(A, Read(Absolute()), 4); break;
                case 0xCE: DecMemory(Absolute(), 6); break;
                case 0xCF: Dcp(Absolute(), 6); break;
                case 0xD0: Branch(!GetFlag(Zero)); break;
                case 0xD1: Compare(A, Read(IndirectY(out var pd1)), 5 + pd1); break;
                case 0xD2: Jam(); break;
                case 0xD3: Dcp(IndirectY(out _), 8); break;
                case 0xD4: Nop(ZeroPageX(), 4); break;
                case 0xD5: Compare(A, Read(ZeroPageX()), 4); break;
                case 0xD6: DecMemory(ZeroPageX(), 6); break;
                case 0xD7: Dcp(ZeroPageX(), 6); break;
                case 0xD8: SetFlag(Decimal, false); AddCycles(2); break;
                case 0xD9: Compare(A, Read(AbsoluteY(out var pd9)), 4 + pd9); break;
                case 0xDA: AddCycles(2); break;
                case 0xDB: Dcp(AbsoluteY(out _), 7); break;
                case 0xDC: Nop(AbsoluteX(out var pdc), 4 + pdc); break;
                case 0xDD: Compare(A, Read(AbsoluteX(out var pdd)), 4 + pdd); break;
                case 0xDE: DecMemory(AbsoluteX(out _), 7); break;
                case 0xDF: Dcp(AbsoluteX(out _), 7); break;
                case 0xE0: Compare(X, FetchByte(), 2); break;
                case 0xE1: Sbc(Read(IndirectX()), 6); break;
                case 0xE2: Nop(FetchByte(), 2); break;
                case 0xE3: Isc(IndirectX(), 8); break;
                case 0xE4: Compare(X, Read(ZeroPage()), 3); break;
                case 0xE5: Sbc(Read(ZeroPage()), 3); break;
                case 0xE6: IncMemory(ZeroPage(), 5); break;
                case 0xE7: Isc(ZeroPage(), 5); break;
                case 0xE8: X++; SetZn(X); AddCycles(2); break;
                case 0xE9: Sbc(FetchByte(), 2); break;
                case 0xEA: AddCycles(2); break;
                case 0xEB: Sbc(FetchByte(), 2); break;
                case 0xEC: Compare(X, Read(Absolute()), 4); break;
                case 0xED: Sbc(Read(Absolute()), 4); break;
                case 0xEE: IncMemory(Absolute(), 6); break;
                case 0xEF: Isc(Absolute(), 6); break;
                case 0xF0: Branch(GetFlag(Zero)); break;
                case 0xF1: Sbc(Read(IndirectY(out var pf1)), 5 + pf1); break;
                case 0xF2: Jam(); break;
                case 0xF3: Isc(IndirectY(out _), 8); break;
                case 0xF4: Nop(ZeroPageX(), 4); break;
                case 0xF5: Sbc(Read(ZeroPageX()), 4); break;
                case 0xF6: IncMemory(ZeroPageX(), 6); break;
                case 0xF7: Isc(ZeroPageX(), 6); break;
                case 0xF8: SetFlag(Decimal, true); AddCycles(2); break;
                case 0xF9: Sbc(Read(AbsoluteY(out var pf9)), 4 + pf9); break;
                case 0xFA: AddCycles(2); break;
                case 0xFB: Isc(AbsoluteY(out _), 7); break;
                case 0xFC: Nop(AbsoluteX(out var pfc), 4 + pfc); break;
                case 0xFD: Sbc(Read(AbsoluteX(out var pfd)), 4 + pfd); break;
                case 0xFE: IncMemory(AbsoluteX(out _), 7); break;
                case 0xFF: Isc(AbsoluteX(out _), 7); break;
            }

            return (int)(Cycles - start);
        }

        private byte FetchByte()
        {
            return Read(ProgramCounter++);
        }

        private ushort FetchWord()
        {
            var low = FetchByte();
            var high = FetchByte();
            return (ushort)(low | (high << 8));
        }

        private byte Read(ushort address)
        {
            return _bus.Read(address);
        }

        private void Write(ushort address, byte value, int totalCycles)
        {
            _bus.Write(address, value, Math.Max(0, totalCycles - 1));
            AddCycles(totalCycles);
        }

        private void WriteReadModifyWrite(ushort address, byte original, byte value, int totalCycles)
        {
            _bus.Write(address, original, Math.Max(0, totalCycles - 2));
            _bus.Write(address, value, Math.Max(0, totalCycles - 1));
            AddCycles(totalCycles);
        }

        private void AddCycles(int cycles)
        {
            Cycles += cycles;
        }

        private bool GetFlag(byte flag)
        {
            return (Status & flag) != 0;
        }

        private void SetFlag(byte flag, bool value)
        {
            Status = value ? (byte)(Status | flag | Unused) : (byte)((Status & ~flag) | Unused);
        }

        private void SetZn(byte value)
        {
            SetFlag(Zero, value == 0);
            SetFlag(Negative, (value & 0x80) != 0);
        }

        private void Ora(byte value, int cycles)
        {
            A |= value;
            SetZn(A);
            AddCycles(cycles);
        }

        private void And(byte value, int cycles)
        {
            A &= value;
            SetZn(A);
            AddCycles(cycles);
        }

        private void Eor(byte value, int cycles)
        {
            A ^= value;
            SetZn(A);
            AddCycles(cycles);
        }

        private void Lda(byte value, int cycles)
        {
            A = value;
            SetZn(A);
            AddCycles(cycles);
        }

        private void Ldx(byte value, int cycles)
        {
            X = value;
            SetZn(X);
            AddCycles(cycles);
        }

        private void Ldy(byte value, int cycles)
        {
            Y = value;
            SetZn(Y);
            AddCycles(cycles);
        }

        private void Lax(byte value, int cycles)
        {
            A = value;
            X = value;
            SetZn(value);
            AddCycles(cycles);
        }

        private void Compare(byte register, byte value, int cycles)
        {
            var result = register - value;
            SetFlag(Carry, register >= value);
            SetZn((byte)result);
            AddCycles(cycles);
        }

        private void Adc(byte value, int cycles)
        {
            if (GetFlag(Decimal))
            {
                DecimalAdc(value);
            }
            else
            {
                BinaryAdc(value);
            }

            AddCycles(cycles);
        }

        private void BinaryAdc(byte value)
        {
            var carryIn = GetFlag(Carry) ? 1 : 0;
            var result = A + value + carryIn;
            var output = (byte)result;
            SetFlag(Carry, result > 0xFF);
            SetFlag(Overflow, (~(A ^ value) & (A ^ output) & 0x80) != 0);
            A = output;
            SetZn(A);
        }

        private void DecimalAdc(byte value)
        {
            var carryIn = GetFlag(Carry) ? 1 : 0;
            var binary = A + value + carryIn;
            var low = (A & 0x0F) + (value & 0x0F) + carryIn;
            var high = (A >> 4) + (value >> 4);
            if (low > 9)
            {
                low += 6;
                high++;
            }

            if (high > 9)
            {
                high += 6;
            }

            var output = (byte)((high << 4) | (low & 0x0F));
            SetFlag(Carry, high > 15);
            SetFlag(Overflow, (~(A ^ value) & (A ^ binary) & 0x80) != 0);
            A = output;
            SetZn(A);
        }

        private void Sbc(byte value, int cycles)
        {
            if (GetFlag(Decimal))
            {
                DecimalSbc(value);
            }
            else
            {
                BinaryAdc((byte)~value);
            }

            AddCycles(cycles);
        }

        private void DecimalSbc(byte value)
        {
            var carryIn = GetFlag(Carry) ? 0 : 1;
            var result = A - value - carryIn;
            var low = (A & 0x0F) - (value & 0x0F) - carryIn;
            var high = (A >> 4) - (value >> 4);
            if (low < 0)
            {
                low -= 6;
                high--;
            }

            if (high < 0)
            {
                high -= 6;
            }

            var output = (byte)(((high << 4) & 0xF0) | (low & 0x0F));
            SetFlag(Carry, result >= 0);
            SetFlag(Overflow, ((A ^ output) & (A ^ value) & 0x80) != 0);
            A = output;
            SetZn(A);
        }

        private byte Asl(byte value)
        {
            SetFlag(Carry, (value & 0x80) != 0);
            value = (byte)(value << 1);
            SetZn(value);
            return value;
        }

        private byte Lsr(byte value)
        {
            SetFlag(Carry, (value & 0x01) != 0);
            value = (byte)(value >> 1);
            SetZn(value);
            return value;
        }

        private byte Rol(byte value)
        {
            var carryIn = GetFlag(Carry) ? 1 : 0;
            SetFlag(Carry, (value & 0x80) != 0);
            value = (byte)((value << 1) | carryIn);
            SetZn(value);
            return value;
        }

        private byte Ror(byte value)
        {
            var carryIn = GetFlag(Carry) ? 0x80 : 0;
            SetFlag(Carry, (value & 0x01) != 0);
            value = (byte)((value >> 1) | carryIn);
            SetZn(value);
            return value;
        }

        private void AslMemory(ushort address, int cycles)
        {
            var original = Read(address);
            WriteReadModifyWrite(address, original, Asl(original), cycles);
        }

        private void LsrMemory(ushort address, int cycles)
        {
            var original = Read(address);
            WriteReadModifyWrite(address, original, Lsr(original), cycles);
        }

        private void RolMemory(ushort address, int cycles)
        {
            var original = Read(address);
            WriteReadModifyWrite(address, original, Rol(original), cycles);
        }

        private void RorMemory(ushort address, int cycles)
        {
            var original = Read(address);
            WriteReadModifyWrite(address, original, Ror(original), cycles);
        }

        private void IncMemory(ushort address, int cycles)
        {
            var original = Read(address);
            var value = (byte)(original + 1);
            SetZn(value);
            WriteReadModifyWrite(address, original, value, cycles);
        }

        private void DecMemory(ushort address, int cycles)
        {
            var original = Read(address);
            var value = (byte)(original - 1);
            SetZn(value);
            WriteReadModifyWrite(address, original, value, cycles);
        }

        private void Bit(byte value, int cycles)
        {
            SetFlag(Zero, (A & value) == 0);
            SetFlag(Overflow, (value & Overflow) != 0);
            SetFlag(Negative, (value & Negative) != 0);
            AddCycles(cycles);
        }

        private void Branch(bool take)
        {
            var offset = unchecked((sbyte)FetchByte());
            if (!take)
            {
                AddCycles(2);
                return;
            }

            var old = ProgramCounter;
            ProgramCounter = (ushort)(ProgramCounter + offset);
            AddCycles(3 + (PageCrossed(old, ProgramCounter) ? 1 : 0));
        }

        private void Jsr()
        {
            var target = FetchWord();
            PushWord((ushort)(ProgramCounter - 1), 3);
            ProgramCounter = target;
        }

        private void Rts()
        {
            ProgramCounter = (ushort)(PullWord(3) + 1);
        }

        private void Rti()
        {
            Status = (byte)((Pull(3) & ~Break) | Unused);
            ProgramCounter = PullWord(4);
        }

        private void Brk()
        {
            ProgramCounter++;
            PushWord(ProgramCounter, 1);
            Push((byte)(Status | Break | Unused), 3);
            SetFlag(InterruptDisable, true);
            ProgramCounter = ReadWord(0xFFFE);
        }

        private void Jam()
        {
            Halted = true;
            AddCycles(1);
        }

        private void Nop(byte ignored, int cycles)
        {
            _ = ignored;
            AddCycles(cycles);
        }

        private void Nop(ushort ignored, int cycles)
        {
            _ = ignored;
            AddCycles(cycles);
        }

        private void Slo(ushort address, int cycles)
        {
            var original = Read(address);
            var value = Asl(original);
            WriteReadModifyWrite(address, original, value, cycles);
            A |= value;
            SetZn(A);
        }

        private void Rla(ushort address, int cycles)
        {
            var original = Read(address);
            var value = Rol(original);
            WriteReadModifyWrite(address, original, value, cycles);
            A &= value;
            SetZn(A);
        }

        private void Sre(ushort address, int cycles)
        {
            var original = Read(address);
            var value = Lsr(original);
            WriteReadModifyWrite(address, original, value, cycles);
            A ^= value;
            SetZn(A);
        }

        private void Rra(ushort address, int cycles)
        {
            var original = Read(address);
            var value = Ror(original);
            WriteReadModifyWrite(address, original, value, cycles);
            if (GetFlag(Decimal))
            {
                DecimalAdc(value);
            }
            else
            {
                BinaryAdc(value);
            }
        }

        private void Dcp(ushort address, int cycles)
        {
            var original = Read(address);
            var value = (byte)(original - 1);
            WriteReadModifyWrite(address, original, value, cycles);
            Compare(A, value, 0);
        }

        private void Isc(ushort address, int cycles)
        {
            var original = Read(address);
            var value = (byte)(original + 1);
            WriteReadModifyWrite(address, original, value, cycles);
            if (GetFlag(Decimal))
            {
                DecimalSbc(value);
            }
            else
            {
                BinaryAdc((byte)~value);
            }
        }

        private void Anc(byte value)
        {
            A &= value;
            SetZn(A);
            SetFlag(Carry, (A & 0x80) != 0);
        }

        private void Alr(byte value)
        {
            A &= value;
            A = Lsr(A);
        }

        private void Arr(byte value)
        {
            A &= value;
            A = Ror(A);
            SetFlag(Carry, (A & 0x40) != 0);
            SetFlag(Overflow, ((A >> 6) ^ (A >> 5) & 1) != 0);
        }

        private void Axs(byte value)
        {
            var source = A & X;
            var result = source - value;
            X = (byte)result;
            SetFlag(Carry, result >= 0);
            SetZn(X);
        }

        private void Las(byte value)
        {
            var result = (byte)(value & StackPointer);
            A = result;
            X = result;
            StackPointer = result;
            SetZn(result);
        }

        private void Ahx(ushort address, int cycles)
        {
            var value = (byte)(A & X & (((address >> 8) + 1) & 0xFF));
            Write(address, value, cycles);
        }

        private void Shx(ushort address, int cycles)
        {
            var value = (byte)(X & (((address >> 8) + 1) & 0xFF));
            Write(address, value, cycles);
        }

        private void Shy(ushort address, int cycles)
        {
            var value = (byte)(Y & (((address >> 8) + 1) & 0xFF));
            Write(address, value, cycles);
        }

        private void Tas(ushort address, int cycles)
        {
            StackPointer = (byte)(A & X);
            var value = (byte)(StackPointer & (((address >> 8) + 1) & 0xFF));
            Write(address, value, cycles);
        }

        private void Push(byte value, int cycleOffset)
        {
            _bus.Write((ushort)(0x0100 | StackPointer), value, cycleOffset);
            StackPointer--;
        }

        private byte Pull(int cycleOffset)
        {
            _ = cycleOffset;
            StackPointer++;
            return Read((ushort)(0x0100 | StackPointer));
        }

        private void PushWord(ushort value, int cycleOffset)
        {
            Push((byte)(value >> 8), cycleOffset);
            Push((byte)value, cycleOffset + 1);
        }

        private ushort PullWord(int cycleOffset)
        {
            var low = Pull(cycleOffset);
            var high = Pull(cycleOffset + 1);
            return (ushort)(low | (high << 8));
        }

        private ushort ReadWord(ushort address, bool wrapPage = false)
        {
            var highAddress = wrapPage
                ? (ushort)((address & 0xFF00) | ((address + 1) & 0x00FF))
                : (ushort)(address + 1);
            return (ushort)(Read(address) | (Read(highAddress) << 8));
        }

        private ushort ZeroPage()
        {
            return FetchByte();
        }

        private ushort ZeroPageX()
        {
            return (byte)(FetchByte() + X);
        }

        private ushort ZeroPageY()
        {
            return (byte)(FetchByte() + Y);
        }

        private ushort Absolute()
        {
            return FetchWord();
        }

        private ushort AbsoluteX(out int pagePenalty)
        {
            var baseAddress = FetchWord();
            var address = (ushort)(baseAddress + X);
            pagePenalty = PageCrossed(baseAddress, address) ? 1 : 0;
            return address;
        }

        private ushort AbsoluteY(out int pagePenalty)
        {
            var baseAddress = FetchWord();
            var address = (ushort)(baseAddress + Y);
            pagePenalty = PageCrossed(baseAddress, address) ? 1 : 0;
            return address;
        }

        private ushort IndirectX()
        {
            var pointer = (byte)(FetchByte() + X);
            return (ushort)(Read(pointer) | (Read((byte)(pointer + 1)) << 8));
        }

        private ushort IndirectY(out int pagePenalty)
        {
            var pointer = FetchByte();
            var baseAddress = (ushort)(Read(pointer) | (Read((byte)(pointer + 1)) << 8));
            var address = (ushort)(baseAddress + Y);
            pagePenalty = PageCrossed(baseAddress, address) ? 1 : 0;
            return address;
        }

        private static bool PageCrossed(int a, int b)
        {
            return (a & 0xFF00) != (b & 0xFF00);
        }
    }
}
