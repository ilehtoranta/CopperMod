// Profile metadata and input protocol values, owned by the application.
// Unsupported profiles remain round-trippable; these types do not execute hardware.
namespace CopperScreen;

internal enum DmaChipModel { OcsAgnus, EcsAgnus, AgaAlice }
internal enum DisplayChipModel { OcsDenise, EcsDenise, AgaLisa }
internal enum VideoStandard { Pal, Ntsc }
internal readonly record struct AmigaChipset(DmaChipModel DmaChip, DisplayChipModel DisplayChip, VideoStandard VideoStandard)
{
    public static AmigaChipset OcsPal => new(DmaChipModel.OcsAgnus, DisplayChipModel.OcsDenise, VideoStandard.Pal);
}
internal enum KickstartVersion { Kickstart13, Kickstart20, Kickstart30, Kickstart31 }
internal enum M68kBackendKind
{
    AccurateM68000 = 0, AccurateM68020 = 1, FastM68000 = 2, JitM68000 = 3,
    Cpu32 = 4, AccurateM68030 = 5, AccurateM68040 = 6, JitM68040 = 7, AccurateM68EC020 = 8
}
internal enum AgnusBusArbitrationMode { Legacy, SlotKernel, ForcedLegacy }
internal enum AmigaHardfileMountMode { Auto, RigidDiskBlock, Partition }
internal static class CopperScreenDefaults
{
    public const int A500BootChipRamSize = 512 * 1024;
    public const int A500BootPseudoFastRamSize = 512 * 1024;
    public const uint A500BootPseudoFastRamBase = 0xC00000;
    public const uint A500RealFastRamBase = 0x200000;
    public const int PalHighResWidth = 716;
    public const int PalHighResHeight = 570;
    public static uint GetDefaultFastRamBase(int size)
    {
        if (size < 512 * 1024 || size > 1024 * 1024 * 1024 || (size & (size - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(size), "Fast RAM must use a standard Zorro II or III size.");
        if (size <= 8 * 1024 * 1024) return 0x200000;
        var alignment = (uint)size;
        return (0x10000000u + alignment - 1) & ~(alignment - 1);
    }
}
internal enum AmigaRawKey : byte
{
    Backquote = 0x00,
    Digit1 = 0x01,
    Digit2 = 0x02,
    Digit3 = 0x03,
    Digit4 = 0x04,
    Digit5 = 0x05,
    Digit6 = 0x06,
    Digit7 = 0x07,
    Digit8 = 0x08,
    Digit9 = 0x09,
    Digit0 = 0x0A,
    Minus = 0x0B,
    Equal = 0x0C,
    Backslash = 0x0D,
    NumPad0 = 0x0F,
    Q = 0x10,
    W = 0x11,
    E = 0x12,
    R = 0x13,
    T = 0x14,
    Y = 0x15,
    U = 0x16,
    I = 0x17,
    O = 0x18,
    P = 0x19,
    BracketLeft = 0x1A,
    BracketRight = 0x1B,
    NumPad1 = 0x1D,
    NumPad2 = 0x1E,
    NumPad3 = 0x1F,
    A = 0x20,
    S = 0x21,
    D = 0x22,
    F = 0x23,
    G = 0x24,
    H = 0x25,
    J = 0x26,
    K = 0x27,
    L = 0x28,
    Semicolon = 0x29,
    Quote = 0x2A,
    IntlNearReturn = 0x2B,
    NumPad4 = 0x2D,
    NumPad5 = 0x2E,
    NumPad6 = 0x2F,
    IntlNearLeftShift = 0x30,
    Z = 0x31,
    X = 0x32,
    C = 0x33,
    V = 0x34,
    B = 0x35,
    N = 0x36,
    M = 0x37,
    Comma = 0x38,
    Period = 0x39,
    Slash = 0x3A,
    NumPadDecimal = 0x3C,
    NumPad7 = 0x3D,
    NumPad8 = 0x3E,
    NumPad9 = 0x3F,
    Space = 0x40,
    Backspace = 0x41,
    Tab = 0x42,
    NumPadEnter = 0x43,
    Return = 0x44,
    Escape = 0x45,
    Delete = 0x46,
    Insert = 0x47,
    PageUp = 0x48,
    PageDown = 0x49,
    NumPadSubtract = 0x4A,
    CursorUp = 0x4C,
    CursorDown = 0x4D,
    CursorRight = 0x4E,
    CursorLeft = 0x4F,
    F1 = 0x50,
    F2 = 0x51,
    F3 = 0x52,
    F4 = 0x53,
    F5 = 0x54,
    F6 = 0x55,
    F7 = 0x56,
    F8 = 0x57,
    F9 = 0x58,
    F10 = 0x59,
    NumPadLeftParen = 0x5A,
    NumPadRightParen = 0x5B,
    NumPadDivide = 0x5C,
    NumPadMultiply = 0x5D,
    NumPadAdd = 0x5E,
    Help = 0x5F,
    LeftShift = 0x60,
    RightShift = 0x61,
    CapsLock = 0x62,
    Control = 0x63,
    LeftAlt = 0x64,
    RightAlt = 0x65,
    LeftAmiga = 0x66,
    RightAmiga = 0x67
}

internal sealed record AmigaHardfilePartitionMetadata
{
    public string? DeviceName { get; init; }

    public uint? TableSize { get; init; }

    public uint? SizeBlockLongs { get; init; }

    public uint? SectorOrigin { get; init; }

    public uint? Surfaces { get; init; }

    public uint? SectorsPerBlock { get; init; }

    public uint? BlocksPerTrack { get; init; }

    public uint? ReservedBlocks { get; init; }

    public uint? PreAllocBlocks { get; init; }

    public uint? Interleave { get; init; }

    public uint? LowCylinder { get; init; }

    public uint? HighCylinder { get; init; }

    public uint? NumBuffers { get; init; }

    public uint? BufferMemoryType { get; init; }

    public uint? MaxTransfer { get; init; }

    public uint? Mask { get; init; }

    public int? BootPriority { get; init; }

    public uint? DosType { get; init; }
}
