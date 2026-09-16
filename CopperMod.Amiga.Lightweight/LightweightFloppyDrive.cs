using CopperDisk;

namespace CopperMod.Amiga.Lightweight;

/// <summary>One read-only standard ADF and its physical DF0 control pins.</summary>
internal sealed class LightweightFloppyDrive
{
    internal const int StandardAdfBytes = 80 * 2 * 11 * 512;
    // Chosen drive-model spin-up time, not a universal drive timing claim.
    internal const long MotorSpinUpCycles = LightweightPaulaAudio.PalCpuFrequency / 2;
    private byte[]? _image;
    private byte[][]? _tracks;
    private byte _controlPins = 0xFF;

    internal bool Mounted => _image is not null;
    internal ReadOnlySpan<byte> Image => _image;
    internal ReadOnlySpan<byte> Track => _tracks is null ? default : _tracks[Cylinder * 2 + Head];
    internal int Cylinder { get; private set; }
    internal int Head => (_controlPins & 4) == 0 ? 1 : 0;
    internal bool Selected => (_controlPins & 8) == 0;
    internal bool MotorOn { get; private set; }
    internal bool DiskChanged { get; private set; } = true;
    internal long ReadyCycle { get; private set; } = long.MaxValue;

    internal void Mount(ReadOnlySpan<byte> image)
    {
        if (image.Length != StandardAdfBytes)
            throw new ArgumentException("DF0 supports standard 880 KiB (80-cylinder DD) ADF images only.", nameof(image));
        // Media conversion belongs at the host boundary. A seek must never
        // allocate or invoke a lazy track decoder during emulated execution.
        var ownedImage = image.ToArray();
        var media = AmigaDiskLoader.FromAdfBytes(ownedImage);
        var tracks = new byte[160][];
        for (var track = 0; track < tracks.Length; track++)
            tracks[track] = AmigaDosTrackEncoder.EncodeTrack(media, track / 2, track & 1);
        _image = ownedImage;
        _tracks = tracks;
        DiskChanged = true;
    }

    internal void Eject()
    {
        _image = null;
        _tracks = null;
        DiskChanged = true;
    }

    internal void ResetControl()
    {
        _controlPins = 0xFF;
        MotorOn = false;
        ReadyCycle = long.MaxValue;
        // A reset does not remove media, move the head or clear disk change.
    }

    // Return false for a seek outside this deliberately bounded drive model.
    internal bool WriteControlPins(byte pins, long cycle)
    {
        var previous = _controlPins;
        _controlPins = pins;
        if (!Selected) return true;

        if ((previous & 8) != 0)
        {
            // MOTOR is latched on selection, not on every PRB write.
            var motor = (pins & 0x80) == 0;
            if (motor != MotorOn)
            {
                MotorOn = motor;
                ReadyCycle = motor ? cycle + MotorSpinUpCycles : long.MaxValue;
            }
        }

        if ((previous & 1) != 0 && (pins & 1) == 0)
        {
            if (Mounted) DiskChanged = false;
            if ((pins & 2) != 0)
                Cylinder = Math.Max(0, Cylinder - 1);
            else if (Cylinder < 79)
                Cylinder++;
            else
                return false;
        }
        return true;
    }

    internal byte ReadInputPins(long cycle)
    {
        if (!Selected) return 0xFF;
        var pins = 0xFF;
        if (DiskChanged) pins &= ~0x04;
        if (Mounted) pins &= ~0x08; // H6a media are explicitly write protected.
        if (Cylinder == 0) pins &= ~0x10;
        if (Mounted && MotorOn && cycle >= ReadyCycle) pins &= ~0x20;
        return (byte)pins;
    }
}
