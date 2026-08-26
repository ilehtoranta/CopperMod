using CopperMod.Amiga.CopperStart.Exec;

namespace CopperMod.Amiga.Tests;

public sealed class RawDoFmtFormatterTests
{
    [Fact]
    public void BareDecimalConsumesSixteenBitArgumentAndLongConsumesThirtyTwoBits()
    {
        var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot));
        var bus = machine.Bus;
        const uint format = 0x1000;
        const uint data = 0x1100;
        WriteAscii(bus, format, "%d %ld");
        bus.WriteWord(data, unchecked((ushort)-2));
        bus.WriteLong(data + 2, 70_000);

        var text = RawDoFmtFormatter.Format(bus, format, data, _ => string.Empty, out var next);

        Assert.Equal("-2 70000", text);
        Assert.Equal(data + 6, next);
    }

    private static void WriteAscii(AmigaBus bus, uint address, string text)
    {
        for (var index = 0; index < text.Length; index++) bus.WriteByte(address + (uint)index, (byte)text[index], 0);
        bus.WriteByte(address + (uint)text.Length, 0, 0);
    }
}
