using CopperMod.Amiga.Runtime;

namespace CopperMod.Amiga.Tests;

public sealed class AgnusRegisterBankTests
{
    private const uint OcsDmaMask = 0x000F_FFFEu;

    [Fact]
    public void ResetProvidesCanonicalOcsDisplayDefaults()
    {
        var registers = Create(AgnusModel.Ocs);

        Assert.Equal(AgnusRegisterBank.DefaultDiwStart, registers.DiwStart);
        Assert.Equal(AgnusRegisterBank.DefaultDiwStop, registers.DiwStop);
        Assert.Equal(AgnusRegisterBank.DefaultDdfStart, registers.DdfStart);
        Assert.Equal(AgnusRegisterBank.DefaultDdfStop, registers.DdfStop);
        Assert.Equal(0, registers.CopperControl);
        Assert.Equal(0u, registers.CopperListPointer1);
        Assert.Equal(0u, registers.CopperListPointer2);
        Assert.Equal(0, registers.BitplaneModulo1);
        Assert.Equal(0, registers.BitplaneModulo2);
    }

    [Fact]
    public void CommonDisplayRegistersStoreOriginalBusValues()
    {
        var registers = Create(AgnusModel.Ocs);

        registers.Write(AgnusRegisterBank.Copcon, 0x0002);
        registers.Write(AgnusRegisterBank.Diwstrt, 0x1234);
        registers.Write(AgnusRegisterBank.Diwstop, 0x5678);
        registers.Write(AgnusRegisterBank.Ddfstrt, 0x0018);
        registers.Write(AgnusRegisterBank.Ddfstop, 0x00D8);
        registers.Write(AgnusRegisterBank.Bpl1mod, 0xFFFE);
        registers.Write(AgnusRegisterBank.Bpl2mod, 0x0006);

        Assert.Equal(0x0002, registers.CopperControl);
        Assert.Equal(0x1234, registers.DiwStart);
        Assert.Equal(0x5678, registers.DiwStop);
        Assert.Equal(0x0018, registers.DdfStart);
        Assert.Equal(0x00D8, registers.DdfStop);
        Assert.Equal(-2, registers.BitplaneModulo1);
        Assert.Equal(6, registers.BitplaneModulo2);
    }

    [Fact]
    public void PairedDmaPointerWritesUseTheSelectedAgnusMask()
    {
        var registers = Create(AgnusModel.Ocs);

        registers.Write(AgnusRegisterBank.Cop1lch, 0x00FF);
        registers.Write(AgnusRegisterBank.Cop1lcl, 0xFFFF);
        registers.Write(AgnusRegisterBank.BplPointerFirst, 0x00FF);
        registers.Write(AgnusRegisterBank.BplPointerFirst + 2, 0xFFFF);
        registers.Write(AgnusRegisterBank.SpritePointerFirst, 0x00FF);
        registers.Write(AgnusRegisterBank.SpritePointerFirst + 2, 0xFFFF);

        Assert.Equal(OcsDmaMask, registers.CopperListPointer1);
        Assert.Equal(OcsDmaMask, registers.GetBitplanePointer(0));
        Assert.Equal(OcsDmaMask, registers.GetSpritePointer(0));
    }

    [Fact]
    public void DiwHighIsStoredOnlyByEcsAgnus()
    {
        var ocs = Create(AgnusModel.Ocs);
        var ecs = Create(AgnusModel.Ecs);

        var ocsWrite = ocs.Write(AgnusRegisterBank.Diwhigh, 0xFFFF);
        var ecsWrite = ecs.Write(AgnusRegisterBank.Diwhigh, 0xFFFF);

        Assert.False(ocs.IsSupported(AgnusRegisterBank.Diwhigh));
        Assert.False(ocsWrite.Handled);
        Assert.Equal(0, ocs.DiwHigh);
        Assert.True(ecs.IsSupported(AgnusRegisterBank.Diwhigh));
        Assert.True(ecsWrite.Handled);
        Assert.Equal(AgnusRegisterBank.DiwhighWritableMask, ecs.DiwHigh);
        Assert.True(ecs.DiwHighValid);

        ecs.Write(AgnusRegisterBank.Diwstrt, 0x2C81);
        Assert.False(ecs.DiwHighValid);
    }

    [Theory]
    [InlineData(0x000, 0x0000, false, true)]
    [InlineData(0x010, 0x0000, false, true)]
    [InlineData(0x01E, 0x0000, false, true)]
    [InlineData(0x01E, AgnusCopperRegisterAccess.CopperDanger, true, false)]
    [InlineData(0x020, 0x0000, true, false)]
    public void CopperRegisterPolicyIsCanonical(
        ushort offset,
        ushort copcon,
        bool canWrite,
        bool stopsCopper)
    {
        Assert.Equal(canWrite, AgnusCopperRegisterAccess.CanWrite(offset, copcon));
        Assert.Equal(stopsCopper, AgnusCopperRegisterAccess.StopsCopper(offset, copcon));
    }

    [Fact]
    public void CpuWritesUpdateCanonicalAgnusStateBeforeDisplayReplay()
    {
        var bus = new AmigaBus(enableLiveDisplayDma: false);

        bus.WriteWord(0x00DFF080, 0x0003);
        bus.WriteWord(0x00DFF082, 0x4567);
        bus.WriteWord(0x00DFF08E, 0x3344);
        bus.WriteWord(0x00DFF092, 0x0020);

        Assert.Equal(0x0003_4566u & bus.ChipDmaAddressMask, bus.AgnusRegisters.CopperListPointer1);
        Assert.Equal(0x3344, bus.AgnusRegisters.DiwStart);
        Assert.Equal(0x0020, bus.AgnusRegisters.DdfStart);
    }

    [Fact]
    public void CopperMovesAndCpuWritesShareCanonicalStorage()
    {
        const uint list = 0x1000;
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, (int)list + 0, AgnusRegisterBank.Diwstrt);
        BigEndian.WriteUInt16(bus.ChipRam, (int)list + 2, 0x5566);
        BigEndian.WriteUInt16(bus.ChipRam, (int)list + 4, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, (int)list + 6, 0xFFFE);

        new AmigaCopper().ExecuteList(bus, list);

        Assert.Equal(0x5566, bus.AgnusRegisters.DiwStart);
    }

    [Fact]
    public void LiveCopperMovesUpdateCanonicalAgnusState()
    {
        const uint list = 0x1000;
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, (int)list + 0, AgnusRegisterBank.Ddfstrt);
        BigEndian.WriteUInt16(bus.ChipRam, (int)list + 2, 0x0028);
        BigEndian.WriteUInt16(bus.ChipRam, (int)list + 4, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, (int)list + 6, 0xFFFE);
        bus.WriteWord(0x00DFF080, (ushort)(list >> 16));
        bus.WriteWord(0x00DFF082, (ushort)list);
        bus.WriteWord(0x00DFF096, 0x8280);

        bus.Agnus.AdvanceTo(256);

        Assert.Equal(0x0028, bus.AgnusRegisters.DdfStart);
    }

    [Fact]
    public void ResetClearsCanonicalAndHistoricalDisplayStateTogether()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF08E, 0x1122);
        bus.WriteWord(0x00DFF0E0, 0x0003);
        bus.WriteWord(0x00DFF0E2, 0x4566);

        bus.Reset();

        Assert.Equal(AgnusRegisterBank.DefaultDiwStart, bus.AgnusRegisters.DiwStart);
        Assert.Equal(0u, bus.AgnusRegisters.GetBitplanePointer(0));
        Assert.Equal(AgnusRegisterBank.DefaultDiwStart, bus.Display.CaptureSnapshot().DiwStart);
        Assert.Equal(0u, bus.Display.CaptureSnapshot().BitplanePointers[0]);
    }

    private static AgnusRegisterBank Create(AgnusModel model)
        => new(model, new ChipDmaAddressing(model));
}
