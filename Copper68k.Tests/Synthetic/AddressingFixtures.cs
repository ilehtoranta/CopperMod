namespace Copper68k.Tests.Synthetic;

// M68000PM 2.4 / 2.5. Encodings and pointer locations are constructed here,
// without calling any production effective-address helper.
internal sealed record IndexFixture(bool Full = false, bool AddressIndex = false, int IndexRegister = 7,
    bool LongIndex = false, int Scale = 0, sbyte BriefDisplacement = -32,
    bool SuppressBase = false, bool SuppressIndex = false, int BaseSize = 1, int Indirect = 0, bool EarlyFormatBit = false)
{
    public string Id => Full
        ? $"full/bs={SuppressBase}/is={SuppressIndex}/bd={BaseSize}/iis={Indirect}"
        : $"brief/{(AddressIndex ? "A" : "D")}{IndexRegister}/{(LongIndex ? "L" : "W")}/scale={1 << Scale}/d={BriefDisplacement}/ignored-format={EarlyFormatBit}";
    public ushort Extension => (ushort)((AddressIndex ? 0x8000 : 0) | (IndexRegister << 12) |
        (LongIndex ? 0x800 : 0) | (Scale << 9) | (Full
        ? 0x100 | (SuppressBase ? 0x80 : 0) | (SuppressIndex ? 0x40 : 0) | (BaseSize << 4) | Indirect
        : (EarlyFormatBit ? 0x100 : 0) | (byte)BriefDisplacement));
    public IEnumerable<ushort> Displacements
    {
        get
        {
            if (BaseSize == 2) yield return 0xffe0;
            if (BaseSize == 3) { yield return 1; yield return 0; }
            if ((Indirect & 3) == 2) yield return 0xffc0;
            if ((Indirect & 3) == 3) { yield return 2; yield return 0; }
        }
    }
    public static IEnumerable<IndexFixture> FullStructures()
    {
        foreach (var suppressBase in new[] { false, true })
        foreach (var suppressIndex in new[] { false, true })
        for (var bd = 1; bd <= 3; bd++)
        foreach (var iis in new[] { 0, 1, 2, 3, 5, 6, 7 })
        {
            if (suppressIndex && iis >= 5) continue; // Reserved (M68000PM table 2-2).
            yield return new(Full: true, SuppressBase: suppressBase, SuppressIndex: suppressIndex, BaseSize: bd, Indirect: iis);
        }
    }
}

internal sealed record AddressOptions(IndexFixture? SourceIndex = null, IndexFixture? DestinationIndex = null,
    ushort SourceAbsoluteWord = 0x6000, ushort DestinationAbsoluteWord = 0x6100,
    uint SourceAbsoluteLong = 0x6200, uint DestinationAbsoluteLong = 0x6300,
    short SourceDisplacement = 0x40, short DestinationDisplacement = 0x60);

internal sealed class AddressingFixture(SyntheticMachine machine, int width, List<ushort> words, uint[] D, uint[] A, uint value, AddressOptions options)
{
    public Func<uint>? SourcePointerTarget { get; private set; }
    public Func<uint>? DestinationPointerTarget { get; private set; }
    public uint NextPc => SyntheticMachine.Code + (uint)words.Count * 2;
    public uint Resolve(OperandForm form, bool source)
    {
        var r = form.Register;
        var displacement = source ? options.SourceDisplacement : options.DestinationDisplacement;
        var step = (uint)(width == 1 && r == 7 ? 2 : width);
        switch (form.Mode)
        {
            case 2: return A[r];
            case 3: { var target = A[r]; A[r] = unchecked(A[r] + step); return target; }
            case 4: A[r] = unchecked(A[r] - step); return A[r];
            case 5: words.Add((ushort)displacement); return unchecked(A[r] + (uint)displacement);
            case 6:
                return ResolveIndex(A[r], source);
            case 7:
                switch (r)
                {
                    case 0:
                        var absoluteWord = source ? options.SourceAbsoluteWord : options.DestinationAbsoluteWord;
                        words.Add(absoluteWord); return unchecked((uint)(int)(short)absoluteWord);
                    case 1:
                        var absoluteLong = source ? options.SourceAbsoluteLong : options.DestinationAbsoluteLong;
                        words.Add((ushort)(absoluteLong >> 16)); words.Add((ushort)absoluteLong); return absoluteLong;
                    case 2: { var target = unchecked(NextPc + (uint)displacement); words.Add((ushort)displacement); return target; }
                    case 3: return ResolveIndex(NextPc, source);
                    case 4:
                        if (width == 4) words.Add((ushort)(value >> 16));
                        words.Add((ushort)(value & (width == 1 ? 0xffu : 0xffffu)));
                        return 0;
                }
                break;
        }
        throw new InvalidOperationException($"No memory fixture for {form}");
    }

    private uint ResolveIndex(uint baseAddress, bool source)
    {
        var spec = (source ? options.SourceIndex : options.DestinationIndex) ?? new(BriefDisplacement: source ? (sbyte)0x40 : (sbyte)0x60);
        words.Add(spec.Extension);
        var rawIndex = spec.AddressIndex ? A[spec.IndexRegister] : D[spec.IndexRegister];
        var index = spec.SuppressIndex ? 0u : unchecked((spec.LongIndex ? rawIndex : (uint)(int)(short)rawIndex) << (machine.Model.FullIndex ? spec.Scale : 0));
        if (!spec.Full) return unchecked(baseAddress + index + (uint)(int)spec.BriefDisplacement);
        words.AddRange(spec.Displacements);
        var bd = spec.BaseSize == 2 ? unchecked((uint)-32) : spec.BaseSize == 3 ? 0x10000u : 0;
        var od = (spec.Indirect & 3) == 2 ? unchecked((uint)-64) : (spec.Indirect & 3) == 3 ? 0x20000u : 0;
        var baseWithDisplacement = unchecked((spec.SuppressBase ? 0u : baseAddress) + bd);
        if (spec.Indirect == 0) return unchecked(baseWithDisplacement + index);
        var pointerLocation = unchecked(baseWithDisplacement + (spec.Indirect < 4 ? index : 0u));
        var pointer = source ? 0x200000u : 0x300000u;
        machine.InitializePhysical(pointerLocation, pointer, 4);
        Func<uint> finalTarget = () => unchecked(machine.PeekPhysical(pointerLocation, 4) + od + (spec.Indirect >= 5 ? index : 0u));
        if (source) SourcePointerTarget = finalTarget;
        else DestinationPointerTarget = finalTarget;
        return unchecked(pointer + od + (spec.Indirect >= 5 ? index : 0u));
    }

}
