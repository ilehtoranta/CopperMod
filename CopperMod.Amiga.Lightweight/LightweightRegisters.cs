namespace CopperMod.Amiga.Lightweight;

internal sealed class LightweightRegisters
{
    internal const ushort Dmaconr = 0x002;
    internal const ushort Vposr = 0x004;
    internal const ushort Vhposr = 0x006;
    internal const ushort Adkconr = 0x010;
    internal const ushort Dskbytr = 0x01A;
    internal const ushort Dskdatr = 0x008;
    internal const ushort Dskpth = 0x020;
    internal const ushort Dskptl = 0x022;
    internal const ushort Dsklen = 0x024;
    internal const ushort Dsksync = 0x07E;
    internal const ushort Intenar = 0x01C;
    internal const ushort Intreqr = 0x01E;
    internal const ushort Vposw = 0x02A;
    internal const ushort Copcon = 0x02E;
    internal const ushort Bltcon0 = 0x040;
    internal const ushort Bltcon1 = 0x042;
    internal const ushort Bltafwm = 0x044;
    internal const ushort Bltalwm = 0x046;
    internal const ushort Bltcpth = 0x048;
    internal const ushort Bltbpth = 0x04C;
    internal const ushort Bltapth = 0x050;
    internal const ushort Bltdpth = 0x054;
    internal const ushort Bltsize = 0x058;
    internal const ushort Bltcmod = 0x060;
    internal const ushort Bltbmod = 0x062;
    internal const ushort Bltamod = 0x064;
    internal const ushort Bltdmod = 0x066;
    internal const ushort Bltcdat = 0x070;
    internal const ushort Bltbdat = 0x072;
    internal const ushort Bltadat = 0x074;
    internal const ushort Cop1lch = 0x080;
    internal const ushort Cop1lcl = 0x082;
    internal const ushort Cop2lch = 0x084;
    internal const ushort Cop2lcl = 0x086;
    internal const ushort Copjmp1 = 0x088;
    internal const ushort Copjmp2 = 0x08A;
    internal const ushort Diwstrt = 0x08E;
    internal const ushort Diwstop = 0x090;
    internal const ushort Ddfstrt = 0x092;
    internal const ushort Ddfstop = 0x094;
    internal const ushort DmaconWrite = 0x096;
    internal const ushort IntenaWrite = 0x09A;
    internal const ushort IntreqWrite = 0x09C;
    internal const ushort AdkconWrite = 0x09E;
    internal const ushort Aud0lch = 0x0A0;
    internal const ushort Aud0lcl = 0x0A2;
    internal const ushort Aud0len = 0x0A4;
    internal const ushort Aud0per = 0x0A6;
    internal const ushort Aud0vol = 0x0A8;
    internal const ushort Aud0dat = 0x0AA;
    internal const ushort Aud3dat = 0x0DA;
    internal const ushort Bplcon0 = 0x100;
    internal const ushort Bplcon1 = 0x102;
    internal const ushort Bplcon2 = 0x104;
    internal const ushort Bpl1mod = 0x108;
    internal const ushort Bpl2mod = 0x10A;
    internal const ushort BplPointerFirst = 0x0E0;
    internal const ushort BplPointerLast = 0x0FE;
    internal const ushort BpldatFirst = 0x110;
    internal const ushort BpldatLast = 0x11A;
    internal const ushort SpritePointerFirst = 0x120;
    internal const ushort SpritePointerLast = 0x13E;
    internal const ushort SpritePosFirst = 0x140;
    internal const ushort SpriteDataFirst = 0x144;
    internal const ushort SpriteRegisterLast = 0x17E;
    internal const ushort ColorFirst = 0x180;
    internal const ushort ColorLast = 0x1BE;

    private readonly ushort[] _values = new ushort[0x100];

    internal ushort Dmacon { get; private set; }
    internal ushort Adkcon { get; private set; }
    internal ushort Intena { get; private set; }
    internal ushort Intreq { get; private set; }
    internal ushort DdfStopValue { get; private set; }

    internal ushort Read(ushort offset)
    {
        return offset switch
        {
            Dmaconr => Dmacon,
            Adkconr => Adkcon,
            Intenar => Intena,
            Intreqr => Intreq,
            Ddfstop => DdfStopValue,
            _ when (uint)offset < _values.Length * 2 => _values[offset >> 1],
            _ => 0
        };
    }

    internal void Write(ushort offset, ushort value)
    {
        switch (offset)
        {
            case DmaconWrite:
                Dmacon = ApplySetClear(Dmacon, value, 0x07FF);
                break;
            case IntenaWrite:
                Intena = ApplySetClear(Intena, value, 0x7FFF);
                break;
            case IntreqWrite:
                Intreq = ApplySetClear(Intreq, value, 0x3FFF);
                break;
            case AdkconWrite:
                Adkcon = ApplySetClear(Adkcon, value, 0x7FFF);
                break;
            case Ddfstop:
                DdfStopValue = value;
                break;
            default:
                if ((uint)offset < _values.Length * 2)
                    _values[offset >> 1] = value;
                break;
        }
    }

    internal void Reset()
    {
        Array.Clear(_values);
        _values[Dsksync >> 1] = 0x4489;
        Dmacon = 0;
        Adkcon = 0;
        Intena = 0;
        Intreq = 0;
        DdfStopValue = 0;
    }

    internal void SetHardwareInterruptRequest(ushort bits)
        => Intreq = (ushort)(Intreq | (bits & 0x3FFF));

    internal int GetHighestEnabledInterruptLevel()
        => GetHighestEnabledInterruptLevel(Intena, Intreq);

    internal uint GetCopperListPointer(bool secondList)
    {
        var highOffset = secondList ? Cop2lch : Cop1lch;
        var lowOffset = secondList ? Cop2lcl : Cop1lcl;
        return (uint)((Read(highOffset) & 0x0007) << 16) |
            (uint)(Read(lowOffset) & 0xFFFE);
    }

    internal uint GetBitplanePointer(int plane)
    {
        if ((uint)plane >= 8)
            return 0;
        var highOffset = (ushort)(BplPointerFirst + (plane * 4));
        return (uint)((Read(highOffset) & 0x0007) << 16) |
            (uint)(Read((ushort)(highOffset + 2)) & 0xFFFE);
    }

    internal uint GetSpritePointer(int sprite)
    {
        if ((uint)sprite >= 8)
            return 0;
        var highOffset = (ushort)(SpritePointerFirst + (sprite * 4));
        return (uint)((Read(highOffset) & 0x0007) << 16) |
            (uint)(Read((ushort)(highOffset + 2)) & 0xFFFE);
    }

    internal uint GetBlitterPointer(ushort highOffset)
        => (uint)((Read(highOffset) & 0x0007) << 16) |
            (uint)(Read((ushort)(highOffset + 2)) & 0xFFFE);

    internal uint GetDiskPointer()
        => (uint)((Read(Dskpth) & 7) << 16) | (uint)(Read(Dskptl) & 0xFFFE);

    internal void SetDiskPointerFromDma(uint pointer)
    {
        pointer &= 0x0007_FFFE;
        _values[Dskpth >> 1] = (ushort)(pointer >> 16);
        _values[Dskptl >> 1] = (ushort)pointer;
    }

    internal void SetDiskDataFromDma(ushort data) => _values[Dskdatr >> 1] = data;

    internal void SetDiskLengthFromDma(int remaining)
        => _values[Dsklen >> 1] = (ushort)((_values[Dsklen >> 1] & 0xC000) | remaining);

    internal void SetBlitterPointerFromDma(ushort highOffset, uint pointer)
    {
        pointer &= 0x0007_FFFEu;
        _values[highOffset >> 1] = (ushort)(pointer >> 16);
        _values[(highOffset + 2) >> 1] = (ushort)pointer;
    }

    internal void SetBlitterDataFromDma(ushort offset, ushort value)
        => _values[offset >> 1] = value;

    internal void SetBitplanePointerFromDma(int plane, uint pointer)
    {
        if ((uint)plane >= 8)
            return;
        pointer &= 0x0007_FFFEu;
        var highOffset = (ushort)(BplPointerFirst + (plane * 4));
        _values[highOffset >> 1] = (ushort)(pointer >> 16);
        _values[(highOffset + 2) >> 1] = (ushort)pointer;
    }

    internal void SetSpritePointerFromDma(int sprite, uint pointer)
    {
        if ((uint)sprite >= 8)
            return;
        pointer &= 0x0007_FFFEu;
        var highOffset = (ushort)(SpritePointerFirst + (sprite * 4));
        _values[highOffset >> 1] = (ushort)(pointer >> 16);
        _values[(highOffset + 2) >> 1] = (ushort)pointer;
    }

    internal void SetSpriteRegisterFromDma(
        int sprite,
        int registerOffset,
        ushort value)
    {
        if ((uint)sprite >= 8 ||
            (uint)registerOffset > 6 ||
            (registerOffset & 1) != 0)
        {
            return;
        }

        var offset = SpritePosFirst + (sprite * 8) + registerOffset;
        _values[offset >> 1] = value;
    }

    internal static int GetHighestEnabledInterruptLevel(ushort intena, ushort intreq)
    {
        const ushort master = 0x4000;
        var active = (ushort)(intena & intreq & 0x3FFF);
        if ((intena & master) == 0 || active == 0)
            return 0;
        if ((active & 0x2000) != 0)
            return 6;
        if ((active & 0x1800) != 0)
            return 5;
        if ((active & 0x0780) != 0)
            return 4;
        if ((active & 0x0070) != 0)
            return 3;
        if ((active & 0x0008) != 0)
            return 2;
        return 1;
    }

    private static ushort ApplySetClear(ushort current, ushort value, ushort mask)
    {
        return (value & 0x8000) != 0
            ? (ushort)(current | (value & mask))
            : (ushort)(current & ~(value & mask));
    }
}
