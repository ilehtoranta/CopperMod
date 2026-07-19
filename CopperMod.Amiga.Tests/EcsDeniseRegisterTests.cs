using CopperMod.Amiga.Memory;

namespace CopperMod.Amiga.Tests;

public sealed class EcsDeniseRegisterTests
{
    private const uint CustomBase = 0x00DFF000;

    [Fact]
    public void DeniseIdIsReadableOnlyWithEcsDenise()
    {
        var ecs = CreateBus(AgnusModel.Ocs, DeniseModel.Ecs);
        var ocs = CreateBus(AgnusModel.Ecs, DeniseModel.Ocs);
        var ecsCycle = 0L;
        var ocsCycle = 0L;

        Assert.Equal(0x00FC, ecs.ReadWord(CustomBase + 0x07C, ref ecsCycle, AmigaBusAccessKind.CpuDataRead));
        Assert.Equal(0x00, ecs.ReadByte(CustomBase + 0x07C, ref ecsCycle, AmigaBusAccessKind.CpuDataRead));
        Assert.Equal(0xFC, ecs.ReadByte(CustomBase + 0x07D, ref ecsCycle, AmigaBusAccessKind.CpuDataRead));
        Assert.Equal(0xFFFF, ocs.ReadWord(CustomBase + 0x07C, ref ocsCycle, AmigaBusAccessKind.CpuDataRead));
    }

    [Theory]
    [InlineData(false, false, 0x07C, false)]
    [InlineData(false, true, 0x07C, true)]
    [InlineData(true, false, 0x1C0, true)]
    [InlineData(false, true, 0x1C0, false)]
    [InlineData(true, true, 0x1FC, false)]
    public void ReadableRegisterClassificationIsChipModelAware(
        bool ecsAgnus,
        bool ecsDenise,
        ushort offset,
        bool expected)
    {
        var chipset = new AmigaChipset(
            ecsAgnus ? AgnusModel.Ecs : AgnusModel.Ocs,
            ecsDenise ? DeniseModel.Ecs : DeniseModel.Ocs,
            VideoStandard.Pal);

        Assert.Equal(expected, CustomRegisterScheduleClassifier.IsReadableRegister(chipset, offset));
    }

    [Fact]
    public void EcsGeometryWritesHaveTheExpectedScheduleImpact()
    {
        Assert.Equal(
            HardwareScheduleImpact.Bitplane | HardwareScheduleImpact.Composition,
            CustomRegisterScheduleClassifier.GetPotentialImpact(AmigaChipset.EcsPal, 0x08E));
        Assert.Equal(
            HardwareScheduleImpact.Bitplane | HardwareScheduleImpact.Composition,
            CustomRegisterScheduleClassifier.GetPotentialImpact(AmigaChipset.EcsPal, 0x090));
        Assert.Equal(
            HardwareScheduleImpact.Bitplane | HardwareScheduleImpact.Composition,
            CustomRegisterScheduleClassifier.GetPotentialImpact(AmigaChipset.EcsPal, 0x1E4));
        Assert.Equal(
            AgnusLiveDisplaySlotOwnerMask.Bitplane,
            CustomRegisterScheduleClassifier.GetPreparedDisplaySlotOwnerChanges(
                AmigaChipset.EcsPal,
                0x1E4,
                0x2121));
        Assert.Equal(
            HardwareScheduleImpact.Sprite | HardwareScheduleImpact.Composition,
            CustomRegisterScheduleClassifier.GetPotentialImpact(AmigaChipset.EcsPal, 0x106));
    }

    [Fact]
    public void Bplcon3AndDiwHighUseEcsMasksAndIgnoreOcsWrites()
    {
        var ecs = CreateBus(AgnusModel.Ecs, DeniseModel.Ecs);
        var ocs = CreateBus(AgnusModel.Ocs, DeniseModel.Ocs);

        ecs.WriteWord(CustomBase + 0x106, 0xFFFF);
        ecs.WriteWord(CustomBase + 0x1E4, 0xFFFF);
        ocs.WriteWord(CustomBase + 0x106, 0xFFFF);
        ocs.WriteWord(CustomBase + 0x1E4, 0xFFFF);
        FlushDisplay(ecs);
        FlushDisplay(ocs);

        var ecsSnapshot = ecs.Display.CaptureSnapshot();
        var ocsSnapshot = ocs.Display.CaptureSnapshot();
        Assert.Equal(0x0037, ecsSnapshot.Bplcon3);
        Assert.Equal(0x2F2F, ecsSnapshot.DiwHigh);
        Assert.True(ecsSnapshot.DiwHighValid);
        Assert.Equal(0, ocsSnapshot.Bplcon3);
        Assert.Equal(0, ocsSnapshot.DiwHigh);
        Assert.False(ocsSnapshot.DiwHighValid);
    }

    [Fact]
    public void DiwWritesClearValidityUntilDiwHighIsWrittenAgain()
    {
        var bus = CreateBus(AgnusModel.Ecs, DeniseModel.Ecs);

        bus.WriteWord(CustomBase + 0x1E4, 0x0101);
        FlushDisplay(bus);
        Assert.True(bus.Display.CaptureSnapshot().DiwHighValid);
        Assert.True(bus.AgnusRegisters.DiwHighValid);

        bus.WriteWord(CustomBase + 0x08E, 0x2C81);
        FlushDisplay(bus);
        Assert.False(bus.Display.CaptureSnapshot().DiwHighValid);
        Assert.False(bus.AgnusRegisters.DiwHighValid);

        bus.WriteWord(CustomBase + 0x1E4, 0x0101);
        FlushDisplay(bus);
        Assert.True(bus.Display.CaptureSnapshot().DiwHighValid);
        Assert.True(bus.AgnusRegisters.DiwHighValid);

        bus.WriteWord(CustomBase + 0x090, 0x2CC1);
        FlushDisplay(bus);
        Assert.False(bus.Display.CaptureSnapshot().DiwHighValid);
        Assert.False(bus.AgnusRegisters.DiwHighValid);
    }

    [Fact]
    public void MixedChipsetsKeepAgnusAndDeniseDiwHighStateSeparate()
    {
        var ecsAgnus = CreateBus(AgnusModel.Ecs, DeniseModel.Ocs);
        var ecsDenise = CreateBus(AgnusModel.Ocs, DeniseModel.Ecs);

        ecsAgnus.WriteWord(CustomBase + 0x1E4, 0x2121);
        ecsDenise.WriteWord(CustomBase + 0x1E4, 0x2121);
        FlushDisplay(ecsAgnus);
        FlushDisplay(ecsDenise);

        var ecsAgnusSnapshot = ecsAgnus.Display.CaptureSnapshot();
        var ecsDeniseSnapshot = ecsDenise.Display.CaptureSnapshot();
        Assert.True(ecsAgnus.AgnusRegisters.DiwHighValid);
        Assert.True(ecsAgnusSnapshot.AgnusDiwHighValid);
        Assert.Equal(0x2121, ecsAgnusSnapshot.AgnusDiwHigh);
        Assert.False(ecsAgnusSnapshot.DiwHighValid);
        Assert.Equal(0x181, ecsAgnusSnapshot.AgnusDisplayWindow.HorizontalStart);
        Assert.Equal(0x081, ecsAgnusSnapshot.DeniseDisplayWindow.HorizontalStart);
        Assert.False(ecsDenise.AgnusRegisters.DiwHighValid);
        Assert.False(ecsDeniseSnapshot.AgnusDiwHighValid);
        Assert.True(ecsDeniseSnapshot.DiwHighValid);
        Assert.Equal(0x081, ecsDeniseSnapshot.AgnusDisplayWindow.HorizontalStart);
        Assert.Equal(0x181, ecsDeniseSnapshot.DeniseDisplayWindow.HorizontalStart);
    }

    [Fact]
    public void EcsDeniseUsesNativeSuperHiresCaptureWidth()
    {
        var ecs = CreateBus(AgnusModel.Ocs, DeniseModel.Ecs);
        var ocs = CreateBus(AgnusModel.Ecs, DeniseModel.Ocs);

        Assert.Equal(AmigaConstants.PalSuperHighResWidth, ecs.Display.Width);
        Assert.Equal(AmigaConstants.PalHighResWidth, ocs.Display.Width);
        Assert.Equal(AmigaConstants.PalHighResHeight, ecs.Display.Height);
    }

    [Theory]
    [InlineData(false, 0x0000, 0)]
    [InlineData(false, 0x0040, 0)]
    [InlineData(true, 0x0000, 0)]
    [InlineData(true, 0x0040, 2)]
    [InlineData(true, 0x8000, 1)]
    [InlineData(true, 0x8040, 1)]
    public void DeniseResolutionDecodeIsModelAware(
        bool ecsDenise,
        ushort bplcon0,
        int expected)
    {
        var denise = ecsDenise ? DeniseModel.Ecs : DeniseModel.Ocs;
        Assert.Equal((DeniseResolution)expected, Display.GetDeniseResolution(denise, bplcon0));
    }

    [Theory]
    [InlineData(false, false, 0, 0x7000, 4, 6)]
    [InlineData(true, true, 0, 0x7000, 6, 6)]
    [InlineData(false, true, 0, 0x6000, 6, 6)]
    [InlineData(true, false, 0, 0x6000, 6, 6)]
    [InlineData(false, true, 1, 0x6000, 4, 4)]
    [InlineData(true, true, 2, 0x6040, 2, 2)]
    [InlineData(false, true, 2, 0x6040, 6, 2)]
    [InlineData(true, false, 2, 0x6040, 2, 6)]
    public void FetchAndDecodePlaneCeilingsRemainIndependent(
        bool ecsAgnus,
        bool ecsDenise,
        int resolutionValue,
        ushort bplcon0,
        int expectedFetchPlanes,
        int expectedDecodePlanes)
    {
        var agnus = ecsAgnus ? AgnusModel.Ecs : AgnusModel.Ocs;
        var denise = ecsDenise ? DeniseModel.Ecs : DeniseModel.Ocs;
        var resolution = (DeniseResolution)resolutionValue;
        Assert.Equal(
            expectedFetchPlanes,
            Display.GetAgnusBitplaneFetchPlaneCount(agnus, denise, resolution, bplcon0));
        Assert.Equal(
            expectedDecodePlanes,
            Display.GetDeniseBitplaneDecodePlaneCount(denise, resolution, bplcon0));
    }

    [Fact]
    public void Bplcon3BorderEffectsAreGatedByEcsena()
    {
        var bus = CreateBus(AgnusModel.Ecs, DeniseModel.Ecs);
        bus.WriteWord(CustomBase + 0x180, 0x0F00);
        bus.WriteWord(CustomBase + 0x106, 0x0030);
        var frame = new uint[bus.Display.Width * bus.Display.Height];

        bus.Display.RenderFrame(frame);
        Assert.Equal(0xFFFF0000u, frame[0]);

        bus.WriteWord(CustomBase + 0x100, 0x0001);
        bus.Display.RenderFrame(frame);
        Assert.Equal(0xFF000000u, frame[0]);

        bus.WriteWord(CustomBase + 0x106, 0x0000);
        bus.Display.RenderFrame(frame);
        Assert.Equal(0x00FF0000u, frame[0]);

        bus.WriteWord(CustomBase + 0x106, 0x0010);
        bus.Display.RenderFrame(frame);
        Assert.Equal(0xFFFF0000u, frame[0]);
    }

    [Fact]
    public void BrdsprtPermitsSpritesOutsideTheDisplayWindow()
    {
        var blocked = CreateBorderSpriteBus(0x0010);
        var enabled = CreateBorderSpriteBus(0x0012);
        var blockedFrame = new uint[blocked.Display.Width * blocked.Display.Height];
        var enabledFrame = new uint[enabled.Display.Width * enabled.Display.Height];

        blocked.Display.RenderFrame(blockedFrame);
        enabled.Display.RenderFrame(enabledFrame);

        Assert.Equal(0xFF000000u, blockedFrame[0]);
        Assert.Equal(0xFF00FF00u, enabledFrame[0]);
    }

    [Fact]
    public void SuperHiresSpritesUseUpperColorBankAtSeventyNanosecondWidth()
    {
        var aligned = CreateSuperHiresManualSpriteBus(positionIncrement: false, 0x8000, 0x4000);
        var incremented = CreateSuperHiresManualSpriteBus(positionIncrement: true, 0x8000, 0x4000);
        var alignedFrame = new uint[aligned.Display.Width * aligned.Display.Height];
        var incrementedFrame = new uint[incremented.Display.Width * incremented.Display.Height];

        aligned.Display.RenderFrame(alignedFrame);
        incremented.Display.RenderFrame(incrementedFrame);

        var offset = ((AmigaConstants.PalLowResOverscanBorderY * 2) * aligned.Display.Width) +
            (AmigaConstants.PalLowResOverscanBorderX * 4);
        Assert.Equal(new uint[] { 0xFFFF00AAu, 0xFF55AAFFu, 0xFFFF00AAu, 0xFF55AAFFu },
            alignedFrame.AsSpan(offset, 4).ToArray());
        Assert.Equal(new uint[] { 0xFF000000u, 0xFF000000u, 0xFFFF00AAu, 0xFF55AAFFu },
            incrementedFrame.AsSpan(offset, 4).ToArray());
        Assert.Equal(0xFFFF00AAu, incrementedFrame[offset + 4]);
        Assert.Equal(0xFF55AAFFu, incrementedFrame[offset + 5]);
    }

    [Theory]
    [InlineData(0x0000, 0xFFFF0000u)]
    [InlineData(0x0020, 0xFF00FF00u)]
    public void SuperHiresSpritePriorityOverlaysBelowFourAndXorsAtFour(
        ushort bplcon2,
        uint expected)
    {
        var bus = CreateSuperHiresManualSpriteBus(positionIncrement: false, 0xC000, 0x0000);
        bus.WriteWord(CustomBase + 0x092, 0x003C);
        bus.WriteWord(CustomBase + 0x094, 0x003C);
        bus.WriteWord(CustomBase + 0x180 + (5 * 2), 0x000F);  // Playfield pair 1,1: blue.
        bus.WriteWord(CustomBase + 0x180 + (16 * 2), 0x00F0); // XOR pair 0,0: green.
        bus.WriteWord(CustomBase + 0x180 + (21 * 2), 0x0F00); // Sprite pair 1,1: red.
        SetBitplanePointer(bus, 0, 0x1000);
        SetBitplanePointer(bus, 1, 0x1100);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0xC000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1100, 0x0000);
        bus.WriteWord(CustomBase + 0x104, bplcon2);
        bus.WriteWord(CustomBase + 0x100, 0x2041);
        bus.WriteWord(CustomBase + 0x096, 0x8300);
        var frame = new uint[bus.Display.Width * bus.Display.Height];

        bus.Display.RenderFrame(frame);

        var offset = ((AmigaConstants.PalLowResOverscanBorderY * 2) * bus.Display.Width) +
            (AmigaConstants.PalLowResOverscanBorderX * 4);
        Assert.Equal(expected, frame[offset]);
        Assert.Equal(expected, frame[offset + 1]);
    }

    [Theory]
    [InlineData(0x0004, 0xC000, 0x0000, 16)]
    [InlineData(0x0020, 0x0000, 0xC000, 31)]
    public void SuperHiresSpriteXorUsesTheActiveDualPlayfieldSample(
        ushort bplcon2,
        ushort plane0,
        ushort plane1,
        int xorColorRegister)
    {
        var bus = CreateSuperHiresManualSpriteBus(positionIncrement: false, 0xC000, 0x0000);
        bus.WriteWord(CustomBase + 0x092, 0x003C);
        bus.WriteWord(CustomBase + 0x094, 0x003C);
        bus.WriteWord(CustomBase + 0x180u + (uint)(xorColorRegister * 2), 0x00F0);
        SetBitplanePointer(bus, 0, 0x1000);
        SetBitplanePointer(bus, 1, 0x1100);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, plane0);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1100, plane1);
        bus.WriteWord(CustomBase + 0x104, bplcon2);
        bus.WriteWord(CustomBase + 0x100, 0x2441);
        bus.WriteWord(CustomBase + 0x096, 0x8300);
        var frame = new uint[bus.Display.Width * bus.Display.Height];

        bus.Display.RenderFrame(frame);

        var offset = ((AmigaConstants.PalLowResOverscanBorderY * 2) * bus.Display.Width) +
            (AmigaConstants.PalLowResOverscanBorderX * 4);
        Assert.Equal(0xFF00FF00u, frame[offset]);
        Assert.Equal(0xFF00FF00u, frame[offset + 1]);
    }

    [Fact]
    public void TransparentSuperHiresSpriteSamplesLeavePlayfieldSamplesUntouched()
    {
        var bus = CreateSuperHiresManualSpriteBus(positionIncrement: false, 0x0000, 0x0000);
        bus.WriteWord(CustomBase + 0x092, 0x003C);
        bus.WriteWord(CustomBase + 0x094, 0x003C);
        bus.WriteWord(CustomBase + 0x180 + (5 * 2), 0x000F);
        SetBitplanePointer(bus, 0, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0xC000);
        bus.WriteWord(CustomBase + 0x100, 0x1041);
        bus.WriteWord(CustomBase + 0x096, 0x8300);
        var frame = new uint[bus.Display.Width * bus.Display.Height];

        bus.Display.RenderFrame(frame);

        var offset = ((AmigaConstants.PalLowResOverscanBorderY * 2) * bus.Display.Width) +
            (AmigaConstants.PalLowResOverscanBorderX * 4);
        Assert.Equal(0xFF0000FFu, frame[offset]);
        Assert.Equal(0xFF0000FFu, frame[offset + 1]);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 15)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 15)]
    public void SuperHiresBplcon1DelaysOddAndEvenPlanesInThirtyFiveNanosecondSamples(
        bool oddPlane,
        int delay)
    {
        var bus = CreateBus(AgnusModel.Ecs, DeniseModel.Ecs);
        bus.WriteWord(CustomBase + 0x092, 0x003C);
        bus.WriteWord(CustomBase + 0x094, 0x003C);
        bus.WriteWord(CustomBase + 0x182, 0x0C00);
        bus.WriteWord(CustomBase + 0x184, 0x0C00);
        bus.WriteWord(CustomBase + 0x188, 0x0300);
        bus.WriteWord(CustomBase + 0x190, 0x0300);
        SetBitplanePointer(bus, 0, 0x1000);
        SetBitplanePointer(bus, 1, 0x1100);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, oddPlane ? (ushort)0 : (ushort)0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1100, oddPlane ? (ushort)0x8000 : (ushort)0);
        bus.WriteWord(CustomBase + 0x102, (ushort)(oddPlane ? delay << 4 : delay));
        bus.WriteWord(CustomBase + 0x100, oddPlane ? (ushort)0x2041 : (ushort)0x1041);
        bus.WriteWord(CustomBase + 0x096, 0x8300);
        var frame = new uint[bus.Display.Width * bus.Display.Height];

        bus.Display.RenderFrame(frame);

        var rowOffset = (AmigaConstants.PalLowResOverscanBorderY * 2) * bus.Display.Width;
        var firstSample = AmigaConstants.PalLowResOverscanBorderX * 4;
        for (var sample = 0; sample < delay; sample++)
        {
            Assert.Equal(0xFF000000u, frame[rowOffset + firstSample + sample]);
        }

        Assert.Equal(0xFFFF0000u, frame[rowOffset + firstSample + delay]);
    }

    [Fact]
    public void SuperHiresShifterContinuesAcrossSixteenSampleWordBoundary()
    {
        var bus = CreateBus(AgnusModel.Ecs, DeniseModel.Ecs);
        bus.WriteWord(CustomBase + 0x092, 0x003C);
        bus.WriteWord(CustomBase + 0x094, 0x003C);
        bus.WriteWord(CustomBase + 0x182, 0x0C00);
        bus.WriteWord(CustomBase + 0x188, 0x0300);
        SetBitplanePointer(bus, 0, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x0001);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1002, 0x8000);
        bus.WriteWord(CustomBase + 0x100, 0x1041);
        bus.WriteWord(CustomBase + 0x096, 0x8300);
        var frame = new uint[bus.Display.Width * bus.Display.Height];

        bus.Display.RenderFrame(frame);

        var offset = ((AmigaConstants.PalLowResOverscanBorderY * 2) * bus.Display.Width) +
            (AmigaConstants.PalLowResOverscanBorderX * 4);
        Assert.Equal(0xFFFF0000u, frame[offset + 15]);
        Assert.Equal(0xFFFF0000u, frame[offset + 16]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void SuperHiresSpriteDataComesFromTheGrantedDmaChannel(int spriteIndex)
    {
        var bus = new AmigaBus(
            enableLiveAgnusDma: true,
            chipset: new AmigaChipset(AgnusModel.Ecs, DeniseModel.Ecs, VideoStandard.Pal));
        const uint spriteAddress = 0x2000;
        var x = AmigaConstants.PalLowResOverscanBorderX;
        var y = AmigaConstants.PalLowResOverscanBorderY;
        var hardwareX = x + 129 - AmigaConstants.PalLowResOverscanBorderX;
        var hardwareYStart = y + 0x2C - AmigaConstants.PalLowResOverscanBorderY;
        var hardwareYStop = hardwareYStart + 1;
        var pos = (ushort)((hardwareYStart << 8) | (hardwareX >> 1));
        var ctl = (ushort)((hardwareYStop << 8) | (hardwareX & 1));
        BigEndian.WriteUInt16(bus.ChipRam, (int)spriteAddress, pos);
        BigEndian.WriteUInt16(bus.ChipRam, (int)spriteAddress + 2, ctl);
        BigEndian.WriteUInt16(bus.ChipRam, (int)spriteAddress + 4, 0xC000);
        BigEndian.WriteUInt16(bus.ChipRam, (int)spriteAddress + 6, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, (int)spriteAddress + 8, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, (int)spriteAddress + 10, 0x0000);
        var pointerOffset = CustomBase + 0x120u + (uint)(spriteIndex * 4);
        bus.WriteWord(pointerOffset, (ushort)(spriteAddress >> 16));
        bus.WriteWord(pointerOffset + 2, (ushort)spriteAddress);
        bus.WriteWord(CustomBase + 0x180 + (21 * 2), 0x0F00);
        bus.WriteWord(CustomBase + 0x100, 0x0041);
        bus.WriteWord(CustomBase + 0x096, 0x8220);
        var frame = new uint[bus.Display.Width * bus.Display.Height];

        bus.Display.RenderFrame(frame, 0, AmigaConstants.A500PalCpuCyclesPerFrame);

        var offset = ((y * 2) * bus.Display.Width) + (x * 4);
        Assert.Equal(0xFFFF0000u, frame[offset]);
        Assert.Equal(0xFFFF0000u, frame[offset + 1]);
    }

    [Fact]
    public void FmodeOffsetRemainsAbsent()
    {
        var bus = CreateBus(AgnusModel.Ecs, DeniseModel.Ecs);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0xA55A);
        var cycle = 0L;
        _ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);

        Assert.Equal(0xFFFF, bus.ReadWord(CustomBase + 0x1FC, ref cycle, AmigaBusAccessKind.CpuDataRead));
        Assert.False(CustomRegisterScheduleClassifier.IsReadableRegister(AmigaChipset.EcsPal, 0x1FC));
    }

    private static AmigaBus CreateBus(AgnusModel agnus, DeniseModel denise)
        => new(chipset: new AmigaChipset(agnus, denise, VideoStandard.Pal));

    private static AmigaBus CreateBorderSpriteBus(ushort bplcon3)
    {
        var bus = CreateBus(AgnusModel.Ecs, DeniseModel.Ecs);
        const int hardwareX = 129 - AmigaConstants.PalLowResOverscanBorderX;
        const int hardwareYStart = 0x2C - AmigaConstants.PalLowResOverscanBorderY;
        const int hardwareYStop = hardwareYStart + 1;
        bus.WriteWord(CustomBase + 0x1A2, 0x00F0);
        bus.WriteWord(CustomBase + 0x100, 0x0001);
        bus.WriteWord(CustomBase + 0x106, bplcon3);
        bus.WriteWord(CustomBase + 0x140, (ushort)((hardwareYStart << 8) | (hardwareX >> 1)));
        bus.WriteWord(CustomBase + 0x142, (ushort)((hardwareYStop << 8) | (hardwareX & 1)));
        bus.WriteWord(CustomBase + 0x144, 0x8000);
        bus.WriteWord(CustomBase + 0x146, 0x0000);
        return bus;
    }

    private static AmigaBus CreateSuperHiresManualSpriteBus(
        bool positionIncrement,
        ushort dataA,
        ushort dataB)
    {
        var bus = CreateBus(AgnusModel.Ecs, DeniseModel.Ecs);
        var x = AmigaConstants.PalLowResOverscanBorderX;
        var y = AmigaConstants.PalLowResOverscanBorderY;
        var hardwareX = x + 129 - AmigaConstants.PalLowResOverscanBorderX;
        var hardwareYStart = y + 0x2C - AmigaConstants.PalLowResOverscanBorderY;
        var hardwareYStop = hardwareYStart + 1;
        var pos = (ushort)((hardwareYStart << 8) | (hardwareX >> 1));
        var ctl = (ushort)((hardwareYStop << 8) |
            (hardwareX & 1) |
            (positionIncrement ? 0x0010 : 0));
        bus.WriteWord(CustomBase + 0x180 + (21 * 2), 0x0D2B);
        bus.WriteWord(CustomBase + 0x180 + (26 * 2), 0x0D2B);
        bus.WriteWord(CustomBase + 0x100, 0x0041);
        bus.WriteWord(CustomBase + 0x140, pos);
        bus.WriteWord(CustomBase + 0x142, ctl);
        bus.WriteWord(CustomBase + 0x144, dataA);
        bus.WriteWord(CustomBase + 0x146, dataB);
        return bus;
    }

    private static void SetBitplanePointer(AmigaBus bus, int plane, uint address)
    {
        var offset = CustomBase + 0x0E0u + (uint)(plane * 4);
        bus.WriteWord(offset, (ushort)(address >> 16));
        bus.WriteWord(offset + 2, (ushort)address);
    }

    private static void FlushDisplay(AmigaBus bus)
        => bus.Display.RenderFrame(new uint[bus.Display.Width * bus.Display.Height]);
}
