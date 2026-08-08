/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

namespace CopperMod.Amiga.Bus;

/// <summary>
/// A1200 motherboard decode with no IDE drive and no PCMCIA card installed.
/// Values model floating, physically absent devices; no boot-delay shortcuts are used.
/// </summary>
internal sealed class A1200PlatformIo
{
    internal const uint PcmciaStart = 0x0060_0000;
    internal const uint PcmciaEndExclusive = 0x00A8_0000;
    internal const uint IdeStart = 0x00DA_0000;
    internal const uint GayleEndExclusive = 0x00DB_0000;
    internal const uint MotherboardResourcesStart = 0x00DE_0000;
    internal const uint MotherboardResourcesEndExclusive = 0x00DF_0000;
    internal const uint GayleCardStatus = 0x00DA_8000;
    internal const uint GayleInterruptRequest = 0x00DA_9000;
    internal const uint GayleInterruptEnable = 0x00DA_A000;
    internal const uint GayleConfiguration = 0x00DA_B000;
    internal const uint GayleIdentification = 0x00DE_1000;

    private byte _cardControl;
    private byte _interruptRequest;
    private byte _interruptEnable;
    private byte _configuration;
    private int _identificationBit;

    internal byte CardControl => _cardControl;
    internal byte InterruptRequest => _interruptRequest;
    internal byte InterruptEnable => _interruptEnable;
    internal byte Configuration => _configuration;
    internal bool InterruptAsserted => false;

    internal static bool ContainsAddress(uint address)
        => address is >= PcmciaStart and < PcmciaEndExclusive ||
            address is >= IdeStart and < GayleEndExclusive ||
            address is >= MotherboardResourcesStart and < MotherboardResourcesEndExclusive;

    internal byte ReadByte(uint address)
    {
        address &= 0x00FF_FFFF;
        if (address == GayleCardStatus)
        {
            return _cardControl;
        }

        if (address == GayleInterruptRequest)
        {
            return _interruptRequest;
        }

        if (address == GayleInterruptEnable)
        {
            return _interruptEnable;
        }

        if (address == GayleConfiguration)
        {
            return (byte)(_configuration & 0x0F);
        }

        if (address == GayleIdentification)
        {
            // AA Gayle identification is D1, shifted out most-significant bit first.
            var value = (byte)((0xD1 << _identificationBit) & 0x80);
            _identificationBit = (_identificationBit + 1) & 7;
            return value;
        }

        if (address is >= IdeStart and < GayleCardStatus)
        {
            // An unattached ATA bus floats high. In particular, status is FF rather
            // than the emulator-specific 7F fast-boot value used by some emulators.
            return 0xFF;
        }

        // Empty PCMCIA and unpopulated motherboard-resource lanes float high.
        return 0xFF;
    }

    internal void WriteByte(uint address, byte value)
    {
        address &= 0x00FF_FFFF;
        if (address == GayleCardStatus)
        {
            _cardControl = (byte)(value & 0x03);
        }
        else if (address == GayleInterruptRequest)
        {
            _interruptRequest = (byte)((_interruptRequest & value) | (value & 0x03));
        }
        else if (address == GayleInterruptEnable)
        {
            _interruptEnable = value;
        }
        else if (address == GayleConfiguration)
        {
            _configuration = (byte)(value & 0x0F);
        }
        else if (address is >= MotherboardResourcesStart and < MotherboardResourcesEndExclusive)
        {
            _identificationBit = 0;
        }
    }

    internal void Reset()
    {
        _cardControl = 0;
        _interruptRequest = 0;
        _interruptEnable = 0;
        _configuration = 0;
        _identificationBit = 0;
    }
}
