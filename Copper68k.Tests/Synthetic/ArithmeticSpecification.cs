namespace Copper68k.Tests.Synthetic;

// M68000PM integer instruction definitions. Mathematical signed/unsigned ranges
// form the oracle; no production arithmetic or flag helpers are used.
internal static class ArithmeticSpecification
{
    public static int SizeField(int width) => width == 1 ? 0 : width == 2 ? 1 : 2;
    public static uint RegisterBits(uint value, int width) => (0xa55a5a22u & ~MoveSpecification.Mask(width)) | (value & MoveSpecification.Mask(width));
    public static uint[] Boundaries(int width)
    {
        var mask = MoveSpecification.Mask(width); var sign = 1u << (width * 8 - 1);
        return [0, 1, sign - 1, sign, sign + 1, mask - 1, mask];
    }
    public static long Signed(uint value, int width)
    {
        var sign = 1L << (width * 8 - 1);
        return ((long)(value & MoveSpecification.Mask(width)) ^ sign) - sign;
    }
    public static (uint Value, ushort Sr) Binary(uint destination, uint source, int width, ushort sr, bool subtract, bool compare = false, bool extend = false)
    {
        ulong mask = MoveSpecification.Mask(width), d = destination & mask, s = source & mask;
        var x = extend && (sr & 16) != 0 ? 1UL : 0;
        var raw = subtract ? unchecked(d - s - x) : d + s + x;
        var result = (uint)(raw & mask);
        var carry = subtract ? s + x > d : raw > mask;
        var signedResult = subtract ? Signed((uint)d, width) - Signed((uint)s, width) - (long)x : Signed((uint)d, width) + Signed((uint)s, width) + (long)x;
        var sign = 1L << (width * 8 - 1);
        var overflow = signedResult < -sign || signedResult >= sign;
        var z = result == 0 && (!extend || (sr & 4) != 0);
        var flags = (compare ? sr & 16 : carry ? 16 : 0) | (result >= sign ? 8 : 0) | (z ? 4 : 0) | (overflow ? 2 : 0) | (carry ? 1 : 0);
        return (result, (ushort)((sr & 0xffe0) | flags));
    }
    public static IEnumerable<OperandForm> Sources(int width, bool address = true)
    {
        for (var mode = 0; mode < 7; mode++)
        {
            if (mode == 1 && (width == 1 || !address)) continue;
            for (var reg = 0; reg < 8; reg++) yield return new(mode, reg);
        }
        for (var reg = 0; reg <= 4; reg++) yield return new(7, reg);
    }
    public static IEnumerable<OperandForm> Alterable(bool includeAddress = false)
    {
        for (var mode = 0; mode < 7; mode++)
        {
            if (mode == 1 && !includeAddress) continue;
            for (var reg = 0; reg < 8; reg++) yield return new(mode, reg);
        }
        yield return new(7, 0); yield return new(7, 1);
    }
}

// Shared single-EA fixture, reusable by arithmetic, comparison and future unary
// families. Pointer chains and extension-word PC bases use the MOVE EA fixtures.
internal sealed class OperandFixture
{
    public ArchitecturalExpectation Expected { get; }
    public uint Value { get; }
    public uint Address { get; }
    public int Width { get; }
    public OperandForm Form { get; }
    public uint NextPc => Expected.Pc;
    private readonly SyntheticMachine machine;

    public OperandFixture(SyntheticMachine machine, IReadOnlyList<ushort> prefix, OperandForm form, int width, uint value, bool source = true,
        IndexFixture? index = null, AddressOptions? options = null)
    {
        this.machine = machine; Width = width; Form = form;
        if (form.Mode == 0) machine.Core.State.D[form.Register] = ArithmeticSpecification.RegisterBits(value, width);
        else if (form.Mode == 1)
        {
            if (form.Register == 7) machine.Core.State.SetActiveStackPointer(value);
            else machine.Core.State.A[form.Register] = value;
        }
        var words = prefix.ToList();
        var d = (uint[])machine.Core.State.D.Clone(); var a = (uint[])machine.Core.State.A.Clone();
        var addressing = new AddressingFixture(machine, width, words, d, a, value, options ?? new(SourceIndex: index, DestinationIndex: index));
        uint address = form.Mode < 2 ? 0 : addressing.Resolve(form, source);
        _ = SyntheticExecution.Prepare(machine, words);
        address = (source ? addressing.SourcePointerTarget : addressing.DestinationPointerTarget)?.Invoke() ?? address;
        if (form.Memory)
        {
            for (var offset = -8; offset < width + 8; offset++)
            {
                var physical = machine.Model.Physical(unchecked(address + (uint)offset));
                if (physical >= SyntheticMachine.Code && physical < SyntheticMachine.Code + (uint)words.Count * 2 + 4) continue;
                machine.Bus.Initialize(physical, 0xa5, 1);
            }
            for (var i = 0; i < width; i++)
            {
                var physical = machine.Model.Physical(unchecked(address + (uint)i));
                if (physical >= SyntheticMachine.Code && physical < SyntheticMachine.Code + (uint)words.Count * 2 + 4) continue;
                machine.Bus.Initialize(physical, (byte)(value >> (8 * (width - 1 - i))), 1);
            }
        }
        machine.Start();
        Expected = ArchitecturalExpectation.Capture(machine);
        a.CopyTo(Expected.A, 0);
        Expected.Pc = SyntheticMachine.Code + (uint)words.Count * 2;
        Address = address;
        Value = (form.Mode == 0 ? d[form.Register] : form.Mode == 1 ? a[form.Register] : form.Memory ? machine.PeekPhysical(address, width) : value) & MoveSpecification.Mask(width);
    }
    public void Write(uint result)
    {
        if (Form.Mode == 0) Expected.D[Form.Register] = (Expected.D[Form.Register] & ~MoveSpecification.Mask(Width)) | (result & MoveSpecification.Mask(Width));
        else Expected.Write(Address, result, Width, machine.Model);
    }
}
