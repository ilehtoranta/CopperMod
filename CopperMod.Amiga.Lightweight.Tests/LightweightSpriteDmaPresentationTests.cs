using Xunit;

namespace CopperMod.Amiga.Lightweight.Tests;

public sealed class LightweightSpriteDmaPresentationTests
{
    private const ushort DmaSetMasterAndSprites = 0x8220;
    private const int Width = LightweightVideo.PalRasterWidth;
    private const int Black = unchecked((int)0xFF000000);
    private const int Green = unchecked((int)0xFF00FF00);

    [Fact]
    public void CompleteDmaControlPairEntersDeniseOneCckAfterCtlOutput()
    {
        using var machine = CreateStoppedMachine();
        const uint address = 0x5000;
        var (pos, ctl) = EncodePosition(x: 129, y: 44, height: 1);
        SeedWord(machine, address, pos);
        SeedWord(machine, address + 2, ctl);
        SetPointer(machine, channel: 0, address);
        WriteRegister(machine, LightweightRegisters.DmaconWrite,
            DmaSetMasterAndSprites);

        machine.AdvanceHardwareTo(At(25, 0x1A));

        Assert.Equal((ushort)0, machine.GetCustomRegister(
            LightweightRegisters.SpritePosFirst));
        Assert.Equal(At(25, 0x1B), machine.SpriteNextCycle);

        machine.AdvanceHardwareTo(At(25, 0x1B));

        Assert.Equal(pos, machine.GetCustomRegister(
            LightweightRegisters.SpritePosFirst));
        Assert.Equal(ctl, machine.GetCustomRegister(
            (ushort)(LightweightRegisters.SpritePosFirst + 2)));
        Assert.Equal(At(25, 0x1B), machine.SpriteLastRegisterInputCycle);
        Assert.False(machine.IsManualSpriteArmed(0));
    }

    [Fact]
    public void DmaDataAArmsAtFollowingDeniseInputAndRendersFetchedWords()
    {
        using var machine = CreateStoppedMachine();
        const uint address = 0x5400;
        const int spriteX = 129;
        const int spriteY = 44;
        ConfigureWindow(machine);
        WriteRegisterAndSettle(machine, Color(17), 0x00F0);
        WriteSpriteBlock(
            machine,
            address,
            spriteX,
            spriteY,
            height: 1,
            dataA: 0x8000,
            dataB: 0);
        SetPointer(machine, channel: 0, address);
        WriteRegister(machine, LightweightRegisters.DmaconWrite,
            DmaSetMasterAndSprites);

        machine.AdvanceHardwareTo(At(spriteY, 0x18));
        Assert.False(machine.IsManualSpriteArmed(0));
        Assert.Equal((ushort)0, machine.GetCustomRegister(
            LightweightRegisters.SpriteDataFirst));

        machine.AdvanceHardwareTo(At(spriteY, 0x19));
        Assert.True(machine.IsManualSpriteArmed(0));
        Assert.Equal((ushort)0x8000, machine.GetCustomRegister(
            LightweightRegisters.SpriteDataFirst));
        Assert.Equal(At(spriteY, 0x19), machine.SpriteLastRegisterInputCycle);

        machine.ExecuteFrame();

        Assert.Equal(Green, Pixel(machine, spriteX, spriteY));
    }

    [Theory]
    [InlineData(true, Green)]
    [InlineData(false, Black)]
    public void DmaAttachedOddChannelRequiresItsEvenPartner(
        bool includeEvenPartner,
        int expected)
    {
        using var machine = CreateStoppedMachine();
        const uint evenAddress = 0x5800;
        const uint oddAddress = 0x5900;
        const int spriteX = 161;
        const int spriteY = 46;
        ConfigureWindow(machine);
        WriteRegisterAndSettle(machine, Color(21), 0x00F0);
        if (includeEvenPartner)
        {
            WriteSpriteBlock(
                machine,
                evenAddress,
                spriteX,
                spriteY,
                height: 1,
                dataA: 0x8000,
                dataB: 0);
            SetPointer(machine, channel: 0, evenAddress);
        }
        WriteSpriteBlock(
            machine,
            oddAddress,
            spriteX,
            spriteY,
            height: 1,
            dataA: 0x8000,
            dataB: 0,
            attached: true);
        SetPointer(machine, channel: 1, oddAddress);
        WriteRegister(machine, LightweightRegisters.DmaconWrite,
            DmaSetMasterAndSprites);

        machine.ExecuteFrame();

        Assert.Equal(expected, Pixel(machine, spriteX, spriteY));
    }

    [Fact]
    public void DeniedSecondDataWordReusesPriorDatbLatch()
    {
        using var machine = CreateStoppedMachine();
        const uint address = 0x5C00;
        const int spriteX = 161;
        const int spriteY = 48;
        ConfigureWindow(machine);
        WriteRegisterAndSettle(machine, Color(18), 0x00F0);
        WriteRegisterAndSettle(machine, LightweightRegisters.Ddfstrt, 0x0038);
        WriteRegisterAndSettle(machine, LightweightRegisters.Ddfstop, 0x00D0);
        WriteRegisterAndSettle(machine, LightweightRegisters.Bplcon0, 0x6000);
        var (pos, ctl) = EncodePosition(spriteX, spriteY, height: 2);
        SeedWord(machine, address, pos);
        SeedWord(machine, address + 2, ctl);
        SeedWord(machine, address + 4, 0x0000);
        SeedWord(machine, address + 6, 0x8000);
        SeedWord(machine, address + 8, 0x0000);
        SeedWord(machine, address + 10, 0x0000);
        SeedWord(machine, address + 12, 0);
        SeedWord(machine, address + 14, 0);
        SetPointer(machine, channel: 0, address);
        WriteRegister(machine, LightweightRegisters.DmaconWrite, 0x8320);

        var shrinkCycle = At(spriteY + 1, 0x16);
        machine.AdvanceHardwareTo(shrinkCycle);
        WriteRegister(machine, LightweightRegisters.Ddfstrt, 0x0018);
        machine.ExecuteFrame();

        Assert.Equal(Green, Pixel(machine, spriteX, spriteY));
        Assert.Equal(Green, Pixel(machine, spriteX, spriteY + 1));
        Assert.Equal((ushort)0x8000, machine.GetCustomRegister(
            (ushort)(LightweightRegisters.SpriteDataFirst + 2)));
    }

    [Fact]
    public void DmaTerminatorDisarmsEarlierManualData()
    {
        using var machine = CreateStoppedMachine();
        const uint address = 0x6000;
        const int spriteX = 193;
        const int spriteY = 50;
        ConfigureWindow(machine);
        WriteRegisterAndSettle(machine, Color(17), 0x0F00);
        WriteManualSprite(machine, spriteX, spriteY, dataA: 0x8000);
        SeedWord(machine, address, 0);
        SeedWord(machine, address + 2, 0);
        SetPointer(machine, channel: 0, address);
        WriteRegister(machine, LightweightRegisters.DmaconWrite,
            DmaSetMasterAndSprites);

        machine.ExecuteFrame();

        Assert.False(machine.IsManualSpriteArmed(0));
        Assert.Equal(Black, Pixel(machine, spriteX, spriteY));
    }

    [Fact]
    public void Bpl1DatInputEnablesOnlyPixelsAfterItsDenisePhase()
    {
        using var machine = CreateStoppedMachine();
        const int spriteX = 190;
        const int spriteY = 52;
        ConfigureWindow(machine);
        WriteRegisterAndSettle(machine, Color(17), 0x00F0);
        WriteRegisterAndSettle(machine, LightweightRegisters.Bplcon0, 0x1000);
        WriteManualSprite(machine, spriteX, spriteY, dataA: 0xFFFF);

        var outputCycle = At(spriteY, 0x60);
        machine.AdvanceHardwareTo(outputCycle);
        machine.WriteCustomRegisterFromCopper(
            LightweightRegisters.BpldatFirst,
            0,
            outputCycle);
        machine.ExecuteFrame();

        Assert.Equal(Black, Pixel(machine, spriteX, spriteY));
        Assert.Equal(Black, Pixel(machine, spriteX + 1, spriteY));
        Assert.Equal(Green, Pixel(machine, spriteX + 2, spriteY));
        Assert.Equal(Black, Pixel(machine, spriteX + 2, spriteY + 1));
    }

    [Fact]
    public void RequestedBitplaneWithoutBpl1DatKeepsSpriteOutputClosed()
    {
        using var machine = CreateStoppedMachine();
        const int spriteX = 190;
        const int spriteY = 52;
        ConfigureWindow(machine);
        WriteRegisterAndSettle(machine, Color(17), 0x00F0);
        WriteRegisterAndSettle(machine, LightweightRegisters.Bplcon0, 0x1000);
        WriteManualSprite(machine, spriteX, spriteY, dataA: 0xFFFF);

        machine.ExecuteFrame();

        Assert.Equal(Black, Pixel(machine, spriteX + 2, spriteY));
    }

    private static LightweightA500Machine CreateStoppedMachine()
    {
        var machine = new LightweightA500Machine();
        machine.WriteChipWordDma(0x1000, 0x4E72);
        machine.WriteChipWordDma(0x1002, 0x2700);
        machine.Reset();
        return machine;
    }

    private static void ConfigureWindow(LightweightA500Machine machine)
    {
        WriteRegisterAndSettle(machine, LightweightRegisters.Diwstrt, 0x2C81);
        WriteRegisterAndSettle(machine, LightweightRegisters.Diwstop, 0x2CC1);
    }

    private static void WriteSpriteBlock(
        LightweightA500Machine machine,
        uint address,
        int x,
        int y,
        int height,
        ushort dataA,
        ushort dataB,
        bool attached = false)
    {
        var (pos, ctl) = EncodePosition(x, y, height, attached);
        SeedWord(machine, address, pos);
        SeedWord(machine, address + 2, ctl);
        for (var row = 0; row < height; row++)
        {
            SeedWord(machine, address + 4u + (uint)(row * 4), dataA);
            SeedWord(machine, address + 6u + (uint)(row * 4), dataB);
        }
        SeedWord(machine, address + 4u + (uint)(height * 4), 0);
        SeedWord(machine, address + 6u + (uint)(height * 4), 0);
    }

    private static void WriteManualSprite(
        LightweightA500Machine machine,
        int x,
        int y,
        ushort dataA)
    {
        var (pos, ctl) = EncodePosition(x, y, height: 1);
        WriteRegisterAndSettle(machine, LightweightRegisters.SpritePosFirst, pos);
        WriteRegisterAndSettle(machine,
            (ushort)(LightweightRegisters.SpritePosFirst + 2), ctl);
        WriteRegisterAndSettle(machine,
            (ushort)(LightweightRegisters.SpritePosFirst + 6), 0);
        WriteRegisterAndSettle(machine,
            LightweightRegisters.SpriteDataFirst, dataA);
    }

    private static void SetPointer(
        LightweightA500Machine machine,
        int channel,
        uint address)
    {
        var high = (ushort)(LightweightRegisters.SpritePointerFirst +
            (channel * 4));
        WriteRegister(machine, high, (ushort)(address >> 16));
        WriteRegister(machine, (ushort)(high + 2), (ushort)address);
    }

    private static void SeedWord(
        LightweightA500Machine machine,
        uint address,
        ushort value)
        => machine.WriteChipWordDma(address, value);

    private static void WriteRegisterAndSettle(
        LightweightA500Machine machine,
        ushort offset,
        ushort value)
    {
        var cycle = machine.Cycle;
        WriteRegister(machine, offset, value);
        machine.AdvanceHardwareTo(cycle + LightweightClock.CpuCyclesPerColorClock);
    }

    private static void WriteRegister(
        LightweightA500Machine machine,
        ushort offset,
        ushort value)
        => machine.WriteCustomRegisterFromCopper(offset, value, machine.Cycle);

    private static int Pixel(
        LightweightA500Machine machine,
        int x,
        int y)
        => machine.Framebuffer.Span[(y * Width) + x];

    private static ushort Color(int index)
        => (ushort)(LightweightRegisters.ColorFirst + (index * 2));

    private static long At(int line, int horizontal)
        => ((long)line * LightweightClock.CpuCyclesPerLine) +
            ((long)horizontal * LightweightClock.CpuCyclesPerColorClock);

    private static (ushort Pos, ushort Ctl) EncodePosition(
        int x,
        int y,
        int height,
        bool attached = false)
    {
        var horizontal = x - 1;
        var stop = y + height;
        var pos = (ushort)(((y & 0xFF) << 8) |
            ((horizontal >> 1) & 0xFF));
        var ctl = (ushort)(((stop & 0xFF) << 8) |
            (horizontal & 1) |
            ((stop & 0x100) != 0 ? 0x0002 : 0) |
            ((y & 0x100) != 0 ? 0x0004 : 0) |
            (attached ? 0x0080 : 0));
        return (pos, ctl);
    }
}
