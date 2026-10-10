using Copper68k;

namespace Copper68k.Tests.Synthetic;

internal readonly record struct OperandForm(int Mode, int Register)
{
    public string Id => Mode switch
    {
        0 => $"D{Register}", 1 => $"A{Register}", 2 => $"(A{Register})",
        3 => $"(A{Register})+", 4 => $"-(A{Register})", 5 => $"d16(A{Register})",
        6 => $"index(A{Register})", 7 => new[] { "abs.w", "abs.l", "d16(PC)", "index(PC)", "immediate" }[Register],
        _ => throw new ArgumentOutOfRangeException()
    };
    public bool Memory => Mode >= 2 && !(Mode == 7 && Register == 4);
}

internal static class MoveSpecification
{
    // M68000PM MOVE/MOVEA encoding tables, including all register field values.
    public static bool Legal(ushort opcode)
    {
        var top = opcode >> 12;
        if (top is < 1 or > 3) return false;
        var srcMode = (opcode >> 3) & 7;
        var dstMode = (opcode >> 6) & 7;
        return !(top == 1 && (srcMode == 1 || dstMode == 1)) &&
            (srcMode != 7 || (opcode & 7) <= 4) &&
            (dstMode != 7 || ((opcode >> 9) & 7) <= 1);
    }
    public static int Width(ushort opcode) => (opcode >> 12) switch { 1 => 1, 3 => 2, 2 => 4, _ => throw new ArgumentException() };
    public static ushort Encode(int width, OperandForm source, OperandForm destination) => (ushort)(
        (width == 1 ? 0x1000 : width == 2 ? 0x3000 : 0x2000) |
        (destination.Register << 9) | (destination.Mode << 6) | (source.Mode << 3) | source.Register);
    public static uint Mask(int width) => width == 4 ? uint.MaxValue : (1u << (width * 8)) - 1;
    public static IEnumerable<ushort> Opcodes()
    {
        for (var word = 0x1000; word < 0x4000; word++) if (Legal((ushort)word)) yield return (ushort)word;
    }
}

// Fixtures describe targets without consulting the production decoder or EA helpers.
internal sealed class MoveFixture
{
    private readonly SyntheticMachine machine;
    private readonly List<ushort> words = [];
    public uint[] D { get; }
    public uint[] A { get; }
    public Dictionary<uint, byte> ExpectedMemory { get; private set; } = [];
    public ushort ExpectedSr { get; private set; }
    public uint NextPc => SyntheticMachine.Code + (uint)words.Count * 2;
    public OperandForm Source { get; }
    public OperandForm Destination { get; }
    public int Width { get; }
    public uint DestinationAddress { get; private set; }
    public uint OperandValue { get; private set; }
    public string Id { get; }
    private readonly uint value;
    private readonly int ccr;
    private readonly bool supervisor;
    private readonly AddressOptions options;
    private readonly AddressingFixture addressing;
    private uint inactiveStackPointer;
    private uint masterStackPointer;

    public MoveFixture(SyntheticMachine machine, ushort opcode, uint value = 0x89abcdee, int ccr = 0, bool supervisor = true, string scenario = "canonical", AddressOptions? options = null, Action<SyntheticMachine>? customize = null)
    {
        this.machine = machine;
        this.value = value;
        this.ccr = ccr;
        this.supervisor = supervisor;
        this.options = options ?? new();
        Width = MoveSpecification.Width(opcode);
        Source = new((opcode >> 3) & 7, opcode & 7);
        Destination = new((opcode >> 6) & 7, (opcode >> 9) & 7);
        Id = $"{machine.Model.Id}/{(Destination.Mode == 1 ? "MOVEA" : "MOVE")}/{Width}/{Source.Id}->{Destination.Id}/{scenario}/op={opcode:X4}/v={value:X8}/ccr={ccr:X2}";
        machine.Reset(ccr, supervisor);
        customize?.Invoke(machine);
        D = (uint[])machine.Core.State.D.Clone();
        A = (uint[])machine.Core.State.A.Clone();
        words.Add(opcode);
        addressing = new(machine, Width, words, D, A, value, this.options);
    }

    public void Prepare()
    {
        uint operand;
        uint sourceAddress = 0;
        if (Source.Mode < 2)
        {
            // A register value also serves as an address in aliases; use its initialized value.
            if (Source.Mode == 0)
            {
                machine.Core.State.D[Source.Register] = value;
                D[Source.Register] = value;
            }
            operand = (Source.Mode == 0 ? D[Source.Register] : A[Source.Register]) & MoveSpecification.Mask(Width);
        }
        else
        {
            sourceAddress = addressing.Resolve(Source, true);
            operand = value & MoveSpecification.Mask(Width);
        }
        uint destinationAddress = 0;
        if (Destination.Memory) destinationAddress = addressing.Resolve(Destination, false);

        for (var i = 0; i < words.Count; i++) machine.Bus.Initialize(SyntheticMachine.Code + (uint)i * 2, words[i], 2);
        machine.Bus.Initialize(NextPc, 0x4e71, 2); // NOP sentinel
        machine.Bus.Initialize(NextPc + 2, 0x4e71, 2);
        if (Source.Memory)
        {
            sourceAddress = addressing.SourcePointerTarget?.Invoke() ?? sourceAddress;
            InitializeGuarded(sourceAddress, operand, Width);
            // PC-relative full forms can designate their own extension word.
            // Such operands are fixed by the instruction encoding, not arbitrary data.
            operand = machine.PeekPhysical(sourceAddress, Width);
        }
        destinationAddress = addressing.DestinationPointerTarget?.Invoke() ?? destinationAddress;
        DestinationAddress = destinationAddress;
        OperandValue = operand;
        ExpectedMemory = new(machine.Bus.Memory);
        if (Destination.Mode == 0)
        {
            var mask = MoveSpecification.Mask(Width);
            D[Destination.Register] = (D[Destination.Register] & ~mask) | operand;
        }
        else if (Destination.Mode == 1)
            A[Destination.Register] = Width == 2 ? unchecked((uint)(int)(short)operand) : operand;
        else
            for (var i = 0; i < Width; i++) ExpectedMemory[machine.Model.Physical(unchecked(destinationAddress + (uint)i))] = (byte)(operand >> (8 * (Width - i - 1)));
        ExpectedSr = (ushort)((supervisor ? 0x2700 : 0x0700) | (Destination.Mode == 1 ? ccr :
            (ccr & 0x10) | (operand == 0 ? 4 : 0) | ((operand & (1u << (Width * 8 - 1))) != 0 ? 8 : 0)));
        machine.Start();
        inactiveStackPointer = supervisor ? machine.Core.State.UserStackPointer : machine.Core.State.SupervisorStackPointer;
        masterStackPointer = machine.Core.State.MasterStackPointer;
    }

    private void InitializeGuarded(uint address, uint operand, int width)
    {
        for (var i = -4; i < width + 4; i++)
        {
            var physical = machine.Model.Physical(unchecked(address + (uint)i));
            if (physical >= SyntheticMachine.Code && physical < NextPc + 4) continue;
            machine.Bus.Initialize(physical, i >= 0 && i < width ? (byte)(operand >> (8 * (width - i - 1))) : 0x5au, 1);
        }
    }

    public string? Verify(bool sentinel = false) => new ArchitecturalExpectation
    {
        D = D, A = A, Memory = ExpectedMemory, Sr = ExpectedSr, Pc = NextPc + (sentinel ? 2u : 0u),
        InactiveStackPointer = inactiveStackPointer, MasterStackPointer = masterStackPointer
    }.Verify(machine);
}
